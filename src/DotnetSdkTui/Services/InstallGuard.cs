using System.Text;
using System.Text.Json;

namespace DotnetSdkTui.Services;

/// <summary>
/// Makes dotnetup SDK/runtime installs and uninstalls affect only the version the user picked.
/// </summary>
/// <remarks>
/// dotnetup tracks <em>install specs</em> (e.g. <c>10.0</c>, <c>9.0.3xx</c>, <c>latest</c>,
/// <c>8.0.422</c>) and, on every uninstall/update, garbage-collects any installation that is not
/// the <em>latest</em> match of some remaining spec. That silently deletes unrelated versions:
/// with spec <c>10.0</c> tracking 10.0.400, installing 10.0.403 re-points the spec and leaves
/// 10.0.400 unreferenced, so the next unrelated uninstall removes it.
/// <para>
/// The guard prevents that by pinning every other installation with an explicit exact-version
/// spec before the command runs (a no-op install — the files are already present), and by
/// translating "uninstall version X" into removing exactly the specs that keep X alive.
/// </para>
/// </remarks>
public static class InstallGuard
{
    internal const string Sdk = "SDK";
    internal const string Runtime = "Runtime";
    internal const string AspNetCore = "ASPNETCore";
    internal const string WindowsDesktop = "WindowsDesktop";

    internal sealed record InstallSpec(string Component, string VersionOrChannel, string Source, string InstallRoot);

    internal sealed record Installation(string Component, string Version, string InstallRoot);

    internal sealed record ManifestState(IReadOnlyList<InstallSpec> Specs, IReadOnlyList<Installation> Installations);

    internal enum CommandKind { None, Install, Uninstall }

    /// <summary>A dotnetup <c>sdk|runtime install|uninstall</c> command split into its parts.</summary>
    /// <param name="Family"><c>sdk</c> or <c>runtime</c>.</param>
    /// <param name="Targets">Positional channel/version/component-spec arguments.</param>
    internal sealed record ParsedCommand(CommandKind Kind, string Family, IReadOnlyList<string> Targets, string? InstallPath);

    /// <summary>Outcome of planning an uninstall: the dotnetup argument lists to run, or an error.</summary>
    internal sealed record UninstallPlan(IReadOnlyList<string> Steps, string? Error);

    /// <summary>
    /// Runs <paramref name="arguments"/> (a dotnetup argument list) so it cannot remove any
    /// installation other than the one requested. Non sdk/runtime install/uninstall commands are
    /// passed straight through.
    /// </summary>
    /// <param name="run">Runs one dotnetup argument list and returns its exit code.</param>
    /// <param name="readListJson">Returns <c>dotnetup list --format Json</c> output, or null on failure.</param>
    /// <param name="log">Writes a progress line for the user.</param>
    internal static async Task<int> RunGuardedAsync(
        string arguments,
        Func<string, Task<int>> run,
        Func<Task<string?>> readListJson,
        Action<string> log)
    {
        ParsedCommand command = ParseCommand(arguments);
        if (command.Kind == CommandKind.None)
            return await run(arguments);

        ManifestState? state = ParseListJson(await readListJson());

        if (command.Kind == CommandKind.Install)
        {
            if (state is null)
            {
                log("Warning: could not read dotnetup's install list; other versions could not be protected.");
            }
            else
            {
                int pinExit = await RunStepsAsync(PlanPins(state, exclude: []), run, log, isPin: true);
                if (pinExit != 0)
                    return pinExit;
            }

            return await run(arguments);
        }

        if (state is null)
        {
            log("Could not read dotnetup's install list, so the uninstall was cancelled to avoid removing other versions.");
            return 1;
        }

        var requested = new List<(string Component, string Version)>();
        foreach (string target in command.Targets)
            requested.Add(SplitTarget(command.Family, target));

        if (requested.Count == 0)
        {
            log("Nothing to uninstall: no version was given.");
            return 1;
        }

        foreach ((string component, string version) in requested)
        {
            UninstallPlan plan = PlanUninstall(state, command.Family, component, version, command.InstallPath);
            if (plan.Error is not null)
            {
                log(plan.Error);
                return 1;
            }

            int exit = await RunStepsAsync(plan.Steps, run, log, isPin: false);
            if (exit != 0)
                return exit;

            // Re-read so the next target plans against the post-uninstall state.
            state = ParseListJson(await readListJson());
            if (state is null)
                return 0;

            ReportIfStillInstalled(state, component, version, log);
        }

        return 0;
    }

    private static async Task<int> RunStepsAsync(
        IReadOnlyList<string> steps, Func<string, Task<int>> run, Action<string> log, bool isPin)
    {
        if (isPin && steps.Count > 0)
            log("Pinning the other installed versions so this change can't remove them (already installed, nothing is downloaded):");

        foreach (string step in steps)
        {
            log($"  dotnetup {step}");
            int exit = await run(step);
            if (exit != 0)
            {
                log(isPin
                    ? $"Pinning failed (exit code {exit}); stopped before changing anything else."
                    : $"Step failed (exit code {exit}); stopped.");
                return exit;
            }
        }

        return 0;
    }

    private static void ReportIfStillInstalled(ManifestState state, string component, string version, Action<string> log)
    {
        foreach (Installation inst in state.Installations)
        {
            if (!SameComponent(inst.Component, component) || !SameVersion(inst.Version, version))
                continue;

            var holders = state.Specs
                .Where(s => SameComponent(s.Component, component) && SameRoot(s.InstallRoot, inst.InstallRoot)
                    && ResolveKept(s, state.Installations) == inst)
                .Select(s => $"'{s.VersionOrChannel}' ({s.Source})");
            log($"{version} is still installed in {inst.InstallRoot}; it is still required by: {string.Join(", ", holders)}.");
        }
    }

    /// <summary>
    /// Builds the dotnetup argument lists that pin every installation lacking an explicit
    /// exact-version spec. Global.json specs don't count — dotnetup rewrites or drops them when
    /// the global.json changes.
    /// </summary>
    internal static IReadOnlyList<string> PlanPins(ManifestState state, IReadOnlyCollection<Installation> exclude)
    {
        var groups = new List<(string Root, string Family, List<string> Targets)>();

        foreach (Installation inst in state.Installations)
        {
            if (exclude.Contains(inst) || IsPinned(state, inst))
                continue;

            string? family = FamilyOf(inst.Component);
            string? target = family is null ? null : ToTarget(inst.Component, inst.Version);
            if (family is null || target is null)
                continue;

            int index = groups.FindIndex(g => g.Family == family && SameRoot(g.Root, inst.InstallRoot));
            if (index < 0)
            {
                groups.Add((inst.InstallRoot, family, []));
                index = groups.Count - 1;
            }
            groups[index].Targets.Add(target);
        }

        return groups
            .OrderBy(g => g.Root, StringComparer.Ordinal)
            .ThenBy(g => g.Family == "sdk" ? 0 : 1)
            .Select(g => $"{g.Family} install {string.Join(' ', g.Targets)} --install-path \"{g.Root}\"")
            .ToList();
    }

    /// <summary>
    /// Plans removal of exactly one version: pin every other installation, then remove only the
    /// specs whose latest match is that version, so dotnetup's garbage collection deletes nothing else.
    /// </summary>
    internal static UninstallPlan PlanUninstall(
        ManifestState state, string family, string component, string version, string? installPath)
    {
        var matches = state.Installations
            .Where(i => SameComponent(i.Component, component) && SameVersion(i.Version, version))
            .ToList();

        // Prefer the root the command was pinned to, but don't fail if dotnetup tracks it elsewhere.
        if (installPath is not null && matches.Any(i => SameRoot(i.InstallRoot, installPath)))
            matches = matches.Where(i => SameRoot(i.InstallRoot, installPath)).ToList();

        if (matches.Count == 0)
        {
            string hint = SameComponent(component, Sdk)
                ? "dotnetup doesn't track it, so it can't be uninstalled with dotnetup."
                : "dotnetup doesn't track it as a standalone runtime; it was probably installed with an SDK — uninstall that SDK instead.";
            return new UninstallPlan([], $"{DisplayName(component)} {version} was not uninstalled: {hint}");
        }

        string? target = ToTarget(component, version);
        if (target is null)
            return new UninstallPlan([], $"{DisplayName(component)} {version} can't be uninstalled with dotnetup.");

        var steps = new List<string>(PlanPins(state, matches));

        foreach (Installation inst in matches)
        {
            string root = $"--install-path \"{inst.InstallRoot}\"";
            var holders = state.Specs
                .Where(s => SameComponent(s.Component, component) && SameRoot(s.InstallRoot, inst.InstallRoot)
                    && ResolveKept(s, state.Installations) == inst)
                .ToList();

            if (holders.Count == 0)
            {
                // Already unreferenced (left behind by an earlier channel update): pin it, then drop
                // the pin so the resulting garbage collection removes just this version.
                steps.Add($"{family} install {target} {root}");
                steps.Add($"{family} uninstall {target} --source explicit {root}");
                continue;
            }

            foreach (InstallSpec spec in holders)
            {
                string specTarget = ToTarget(component, spec.VersionOrChannel) ?? spec.VersionOrChannel;
                steps.Add($"{family} uninstall {specTarget} --source {spec.Source.ToLowerInvariant()} {root}");
            }
        }

        return new UninstallPlan(steps, null);
    }

    /// <summary>
    /// Mirrors dotnetup's garbage collection: a spec keeps only the newest installation (same
    /// component and root) that matches it. Returns null when it matches nothing.
    /// </summary>
    internal static Installation? ResolveKept(InstallSpec spec, IEnumerable<Installation> installations)
    {
        Installation? best = null;
        foreach (Installation inst in installations)
        {
            if (!SameComponent(inst.Component, spec.Component)
                || !SameRoot(inst.InstallRoot, spec.InstallRoot)
                || !SpecMatches(spec.VersionOrChannel, inst.Version))
                continue;

            if (best is null || SdkSearchService.CompareSdkVersions(inst.Version, best.Version) > 0)
                best = inst;
        }
        return best;
    }

    /// <summary>
    /// Whether an install spec (exact version, <c>10</c>, <c>10.0</c>, <c>10.0.4xx</c>,
    /// <c>latest</c>, <c>lts</c>, <c>preview</c>) covers <paramref name="version"/>. Verified against
    /// dotnetup: <c>latest</c>/<c>lts</c> skip prereleases, <c>lts</c> is even majors, channels and
    /// <c>preview</c> include prereleases, and unknown keywords (e.g. <c>sts</c>) match nothing.
    /// </summary>
    internal static bool SpecMatches(string spec, string version)
    {
        spec = spec.Trim();
        if (SameVersion(spec, version))
            return true;

        if (!TryParseVersion(version, out int major, out int minor, out int patch, out bool prerelease))
            return false;

        if (spec.Equals("latest", StringComparison.OrdinalIgnoreCase))
            return !prerelease;
        if (spec.Equals("preview", StringComparison.OrdinalIgnoreCase))
            return true;
        if (spec.Equals("lts", StringComparison.OrdinalIgnoreCase))
            return !prerelease && major % 2 == 0;

        string[] parts = spec.Split('.');
        if (parts.Length == 1)
            return int.TryParse(parts[0], out int m) && m == major;

        if (!int.TryParse(parts[0], out int specMajor) || !int.TryParse(parts[1], out int specMinor)
            || specMajor != major || specMinor != minor)
            return false;

        if (parts.Length == 2)
            return true;

        // Feature band, e.g. "10.0.4xx" covers 10.0.400–10.0.499.
        string band = parts[2];
        return parts.Length == 3
            && band.Length == 3
            && char.IsAsciiDigit(band[0])
            && band.EndsWith("xx", StringComparison.OrdinalIgnoreCase)
            && patch / 100 == band[0] - '0';
    }

    /// <summary>Parses <c>dotnetup list --format Json</c> output. Returns null when unreadable.</summary>
    internal static ManifestState? ParseListJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            var specs = new List<InstallSpec>();
            if (root.TryGetProperty("installSpecs", out JsonElement specArray) && specArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement s in specArray.EnumerateArray())
                {
                    string value = GetString(s, "versionOrChannel");
                    if (value.Length == 0) continue;
                    string source = GetString(s, "source");
                    specs.Add(new InstallSpec(
                        GetString(s, "component"), value, source.Length > 0 ? source : "Explicit", GetString(s, "installRoot")));
                }
            }

            var installations = new List<Installation>();
            if (root.TryGetProperty("installations", out JsonElement instArray) && instArray.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement i in instArray.EnumerateArray())
                {
                    string version = GetString(i, "version");
                    if (version.Length == 0) continue;
                    installations.Add(new Installation(GetString(i, "component"), version, GetString(i, "installRoot")));
                }
            }

            return new ManifestState(specs, installations);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Recognises <c>dotnetup sdk|runtime install|uninstall …</c> and extracts its positional
    /// targets and <c>--install-path</c>. Anything else parses as <see cref="CommandKind.None"/>.
    /// </summary>
    internal static ParsedCommand ParseCommand(string arguments)
    {
        List<string> tokens = Tokenize(arguments);
        if (tokens.Count < 2)
            return new ParsedCommand(CommandKind.None, "", [], null);

        string family = tokens[0].ToLowerInvariant();
        CommandKind kind = tokens[1].ToLowerInvariant() switch
        {
            "install" => CommandKind.Install,
            "uninstall" => CommandKind.Uninstall,
            _ => CommandKind.None,
        };
        if (family is not ("sdk" or "runtime") || kind == CommandKind.None)
            return new ParsedCommand(CommandKind.None, "", [], null);

        var targets = new List<string>();
        string? installPath = null;
        for (int i = 2; i < tokens.Count; i++)
        {
            string token = tokens[i];
            if (!token.StartsWith('-'))
            {
                targets.Add(token);
                continue;
            }

            // Options that consume the next token as their value.
            bool takesValue = token.ToLowerInvariant() is "--install-path" or "--manifest-path" or "--source" or "--verbosity" or "-v";
            if (takesValue && i + 1 < tokens.Count)
            {
                if (token.Equals("--install-path", StringComparison.OrdinalIgnoreCase))
                    installPath = tokens[i + 1];
                i++;
            }
        }

        return new ParsedCommand(kind, family, targets, installPath);
    }

    /// <summary>Splits a command target into (manifest component, version). <c>aspnetcore@9.0.0</c> → (ASPNETCore, 9.0.0).</summary>
    internal static (string Component, string Version) SplitTarget(string family, string target)
    {
        if (family == "sdk")
            return (Sdk, target);

        int at = target.IndexOf('@');
        if (at < 0)
            return (Runtime, target);

        string prefix = target[..at].ToLowerInvariant();
        string version = target[(at + 1)..];
        return prefix switch
        {
            "aspnetcore" => (AspNetCore, version),
            "windowsdesktop" => (WindowsDesktop, version),
            _ => (Runtime, version),
        };
    }

    /// <summary>
    /// Builds the runtime/SDK uninstall arguments for an installed component as reported by
    /// <c>dotnet --list-runtimes</c> (display names such as "ASP.NET Core").
    /// </summary>
    internal static string UninstallArgsFor(string displayOrManifestComponent, string version)
    {
        string component = NormalizeComponent(displayOrManifestComponent);
        string family = FamilyOf(component) ?? "runtime";
        return $"{family} uninstall {ToTarget(component, version) ?? version}";
    }

    /// <summary>Maps display names from <c>dotnet --list-runtimes</c> to dotnetup manifest component names.</summary>
    internal static string NormalizeComponent(string component)
    {
        string compact = component.Replace(".", "").Replace(" ", "").ToUpperInvariant();
        return compact switch
        {
            "SDK" or "NETSDK" => Sdk,
            "RUNTIME" or "MICROSOFTNETCOREAPP" => Runtime,
            "ASPNETCORE" or "MICROSOFTASPNETCOREAPP" => AspNetCore,
            "WINDOWSDESKTOP" or "MICROSOFTWINDOWSDESKTOPAPP" => WindowsDesktop,
            _ => component,
        };
    }

    private static bool IsPinned(ManifestState state, Installation inst) =>
        state.Specs.Any(s =>
            s.Source.Equals("Explicit", StringComparison.OrdinalIgnoreCase)
            && SameComponent(s.Component, inst.Component)
            && SameRoot(s.InstallRoot, inst.InstallRoot)
            && SameVersion(s.VersionOrChannel, inst.Version));

    private static string? FamilyOf(string component) => NormalizeComponent(component) switch
    {
        Sdk => "sdk",
        Runtime or AspNetCore or WindowsDesktop => "runtime",
        _ => null,
    };

    private static string? ToTarget(string component, string versionOrChannel) => NormalizeComponent(component) switch
    {
        Sdk or Runtime => versionOrChannel,
        AspNetCore => $"aspnetcore@{versionOrChannel}",
        WindowsDesktop => $"windowsdesktop@{versionOrChannel}",
        _ => null,
    };

    private static string DisplayName(string component) => NormalizeComponent(component) switch
    {
        Sdk => ".NET SDK",
        Runtime => ".NET Runtime",
        AspNetCore => "ASP.NET Core Runtime",
        WindowsDesktop => "Windows Desktop Runtime",
        _ => component,
    };

    private static bool SameComponent(string a, string b) =>
        NormalizeComponent(a).Equals(NormalizeComponent(b), StringComparison.OrdinalIgnoreCase);

    private static bool SameVersion(string a, string b) => a.Trim().Equals(b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool SameRoot(string a, string b) =>
        a.AsSpan().Trim().TrimEnd("/\\").Equals(b.AsSpan().Trim().TrimEnd("/\\"), StringComparison.OrdinalIgnoreCase);

    private static bool TryParseVersion(string version, out int major, out int minor, out int patch, out bool prerelease)
    {
        major = minor = patch = 0;
        int dash = version.IndexOf('-');
        prerelease = dash >= 0;
        string core = dash >= 0 ? version[..dash] : version;
        string[] parts = core.Split('.');
        return parts.Length >= 3
            && int.TryParse(parts[0], out major)
            && int.TryParse(parts[1], out minor)
            && int.TryParse(parts[2], out patch);
    }

    private static string GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    /// <summary>Splits on whitespace, keeping double-quoted segments (e.g. paths with spaces) together.</summary>
    private static List<string> Tokenize(string arguments)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool hasToken = false;

        foreach (char c in arguments)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasToken = true;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (hasToken)
                {
                    tokens.Add(current.ToString());
                    current.Clear();
                    hasToken = false;
                }
            }
            else
            {
                current.Append(c);
                hasToken = true;
            }
        }

        if (hasToken)
            tokens.Add(current.ToString());
        return tokens;
    }
}
