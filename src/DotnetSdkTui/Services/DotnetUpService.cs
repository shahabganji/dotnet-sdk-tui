using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

/// <summary>
/// Service for interacting with the dotnetup CLI tool and falling back to the dotnet CLI.
/// </summary>
public static class DotnetUpService
{
    /// <summary>Checks whether the dotnetup command is available on the system PATH.</summary>
    public static bool IsInstalled() => ProcessRunner.IsCommandAvailable("dotnetup");

    /// <summary>
    /// Retrieves dotnetup version and configuration info via <c>dotnetup --info --format json</c>.
    /// </summary>
    public static Task<DotnetUpInfo?> GetInfoAsync(CancellationToken ct = default) =>
        ProcessRunner.RunJsonAsync("dotnetup", "--info --format json", AppJsonContext.Default.DotnetUpInfo, ct: ct);

    /// <summary>
    /// Lists all installed .NET SDKs and runtimes using dotnet CLI.
    /// Always uses <c>dotnet --list-sdks</c> and <c>dotnet --list-runtimes</c> for accurate data.
    /// </summary>
    public static async Task<List<SdkInfo>> ListInstalledAsync(CancellationToken ct = default)
    {
        var installations = new List<SdkInfo>();

        ProcessResult sdkResult = await ProcessRunner.RunAsync("dotnet", "--list-sdks", ct: ct);
        if (sdkResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(sdkResult.Output))
        {
            installations.AddRange(ParseDotnetSdkList(sdkResult.Output));
        }

        ProcessResult runtimeResult = await ProcessRunner.RunAsync("dotnet", "--list-runtimes", ct: ct);
        if (runtimeResult.ExitCode == 0 && !string.IsNullOrWhiteSpace(runtimeResult.Output))
        {
            installations.AddRange(ParseDotnetRuntimeList(runtimeResult.Output));
        }

        return installations;
    }

    /// <summary>
    /// Lists the installations that dotnetup actually tracks (i.e. installed and managed by
    /// dotnetup itself), parsed from <c>dotnetup list --format Json</c>'s <c>installations</c> array.
    /// Returns an empty list when dotnetup tracks nothing or the command fails — callers should
    /// treat an empty result as "dotnetup manages no installations".
    /// </summary>
    public static async Task<List<SdkInfo>> ListTrackedAsync(CancellationToken ct = default) =>
        await TryListTrackedAsync(ct) ?? [];

    /// <summary>
    /// Like <see cref="ListTrackedAsync"/>, but null when dotnetup couldn't be asked (missing, failed,
    /// or unexpected output) — callers deleting files must not mistake that for "manages nothing".
    /// </summary>
    public static async Task<List<SdkInfo>?> TryListTrackedAsync(CancellationToken ct = default)
    {
        var tracked = new List<SdkInfo>();
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync("dotnetup", "list --format Json", ct: ct);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
                return null;

            using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
            if (!doc.RootElement.TryGetProperty("installations", out var installations)
                || installations.ValueKind != System.Text.Json.JsonValueKind.Array)
                return null;

            foreach (var inst in installations.EnumerateArray())
            {
                string component = inst.TryGetProperty("component", out var c) ? c.GetString() ?? "" : "";
                string version = inst.TryGetProperty("version", out var v) ? v.GetString() ?? "" : "";
                string installRoot = inst.TryGetProperty("installRoot", out var r) ? r.GetString() ?? "" : "";
                string arch = inst.TryGetProperty("architecture", out var a) ? a.GetString() ?? "" : "";

                if (version.Length == 0) continue;
                tracked.Add(new SdkInfo(component, version, installRoot, arch));
            }
        }
        catch
        {
            return null;
        }

        return tracked;
    }

    /// <summary>
    /// Lists SDKs/runtimes in well-known dotnet folders that are neither the dotnet on PATH nor
    /// tracked by dotnetup (e.g. a leftover <c>~/.dotnet</c> from the dotnet-install script) — they
    /// are invisible to <c>dotnet --list-sdks</c> but still on disk. Each folder's own muxer lists
    /// them. Empty when dotnetup isn't installed: there's nothing to migrate them into.
    /// </summary>
    public static async Task<List<SdkInfo>> ListOtherRootInstallsAsync(
        IEnumerable<string> trackedRoots, CancellationToken ct = default)
    {
        var found = new List<SdkInfo>();
        if (!IsInstalled()) return found;

        bool isWindows = OperatingSystem.IsWindows();
        IReadOnlyList<string> roots = GetOtherRootCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            isWindows,
            OperatingSystem.IsMacOS(),
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            SettingsStore.Load().InstallRoot,
            InstallLocationService.GetActiveMuxerDirectory(),
            trackedRoots,
            File.Exists);

        string? activeDir = InstallLocationService.GetActiveMuxerDirectory();
        foreach (string root in roots)
        {
            string muxer = Path.Combine(root, MuxerFileName(isWindows));
            string realRoot = InstallRemoval.Canonical(root);
            if (activeDir is not null && InstallRemoval.SamePath(realRoot, activeDir))
                continue;
            try
            {
                // The muxer reports its real path (e.g. /private/tmp for /tmp), so match both spellings.
                ProcessResult sdks = await ProcessRunner.RunAsync(muxer, "--list-sdks", ct: ct);
                if (sdks.ExitCode == 0)
                    found.AddRange(ParseDotnetSdkList(sdks.Output).Where(i => IsManagedInstallRoot(i.InstallRoot, [root, realRoot])));

                ProcessResult runtimes = await ProcessRunner.RunAsync(muxer, "--list-runtimes", ct: ct);
                if (runtimes.ExitCode == 0)
                    found.AddRange(ParseDotnetRuntimeList(runtimes.Output).Where(i => IsManagedInstallRoot(i.InstallRoot, [root, realRoot])));
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                // Best effort: a broken or foreign-architecture muxer just means that folder isn't listed.
            }
        }

        return found;
    }

    /// <summary>
    /// Well-known dotnet folders (DOTNET_ROOT, remembered install folder, dotnetup default, legacy
    /// roots) that hold a muxer but are neither the active muxer's folder nor managed by dotnetup.
    /// </summary>
    internal static IReadOnlyList<string> GetOtherRootCandidates(
        string home, string? xdgDataHome, string localAppData, bool isWindows, bool isMacOS,
        string? dotnetRootEnv, string? rememberedRoot, string? activeMuxerDir,
        IEnumerable<string> trackedRoots, Func<string, bool> fileExists)
    {
        var candidates = new List<string?>
        {
            dotnetRootEnv,
            rememberedRoot,
            GetDotnetUpDefaultInstallRoot(home, xdgDataHome, localAppData, isWindows, isMacOS),
        };
        candidates.AddRange(GetLegacyDotnetRoots(home, isWindows));

        string active = activeMuxerDir?.Trim().TrimEnd('/', '\\') ?? "";
        List<string> tracked = trackedRoots.ToList();
        string muxer = MuxerFileName(isWindows);
        var roots = new List<string>();
        foreach (string? candidate in candidates)
        {
            string root = candidate?.Trim().TrimEnd('/', '\\') ?? "";
            if (root.Length == 0
                || roots.Contains(root, StringComparer.OrdinalIgnoreCase)
                || root.Equals(active, StringComparison.OrdinalIgnoreCase)
                || IsManagedInstallRoot(root, tracked)
                || !fileExists(Path.Combine(root, muxer)))
                continue;
            roots.Add(root);
        }
        return roots;
    }

    /// <summary>
    /// Determines whether an SDK/runtime install path reported by <c>dotnet --list-sdks</c> lives
    /// under one of the roots that dotnetup manages.
    /// </summary>
    /// <remarks>
    /// <c>dotnet --list-sdks</c> reports the leaf directory (e.g. <c>~/Library/Application Support/dotnet/sdk</c>),
    /// while dotnetup tracks the base root (e.g. <c>~/Library/Application Support/dotnet</c>), so a path is
    /// considered managed when it equals, or is nested under, a managed root. SDKs installed elsewhere
    /// (e.g. the official installer's <c>/usr/local/share/dotnet</c>) are external and cannot be managed
    /// by dotnetup.
    /// </remarks>
    public static bool IsManagedInstallRoot(string installRoot, IEnumerable<string> managedRoots)
    {
        ReadOnlySpan<char> normalized = NormalizeRoot(installRoot);
        if (normalized.IsEmpty) return false;

        foreach (string root in managedRoots)
        {
            ReadOnlySpan<char> r = NormalizeRoot(root);
            if (r.IsEmpty) continue;

            if (normalized.Equals(r, StringComparison.OrdinalIgnoreCase))
                return true;

            // Nested under the managed root (handles both '/' and '\' separators) without
            // allocating a "<root>/" probe string per candidate.
            if (normalized.Length > r.Length
                && (normalized[r.Length] is '/' or '\\')
                && normalized[..r.Length].Equals(r, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>Trims surrounding whitespace and trailing path separators for stable comparison.</summary>
    private static ReadOnlySpan<char> NormalizeRoot(string path) =>
        path.AsSpan().Trim().TrimEnd("/\\");

    /// <summary>Installs an SDK channel via <c>dotnetup sdk install</c>, pinned to the active dotnet root.</summary>
    public static Task<ProcessResult> InstallSdkAsync(string channel, CancellationToken ct = default) =>
        ProcessRunner.RunWithCallbackAsync(
            "dotnetup",
            InstallLocationService.AugmentInstallArgs("dotnetup", $"sdk install {channel}"),
            null, null, null, ct);

    /// <summary>Uninstalls an SDK channel via <c>dotnetup sdk uninstall</c>.</summary>
    public static Task<ProcessResult> UninstallSdkAsync(string channel, CancellationToken ct = default) =>
        ProcessRunner.RunWithCallbackAsync("dotnetup", $"sdk uninstall {channel}", null, null, null, ct);

    /// <summary>Updates all tracked installations to their latest versions.</summary>
    public static Task<ProcessResult> UpdateAllAsync(CancellationToken ct = default) =>
        ProcessRunner.RunWithCallbackAsync("dotnetup", "update", null, null, null, ct);

    /// <summary>Updates tracked SDK installations to their latest versions.</summary>
    public static Task<ProcessResult> UpdateSdksAsync(CancellationToken ct = default) =>
        ProcessRunner.RunWithCallbackAsync("dotnetup", "sdk update", null, null, null, ct);

    /// <summary>
    /// The command that downloads and runs dotnetup's bootstrap script (installs or updates dotnetup).
    /// </summary>
    internal static (string Command, string Args) BootstrapCommand()
    {
        if (!OperatingSystem.IsWindows())
            return ("bash", "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"");

        // dotnetup's install script requires pwsh (PowerShell 7+) — it uses
        // RuntimeInformation.OSArchitecture which doesn't exist in Windows PowerShell 5.1.
        // -UseBasicParsing: without it, patched Windows PowerShell stops at an interactive
        // "Script Execution Risk" prompt before downloading anything.
        string shell = ProcessRunner.IsCommandAvailable("pwsh") ? "pwsh" : "powershell.exe";
        return (shell, "-NoProfile -ExecutionPolicy Bypass -Command \"iwr -UseBasicParsing https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"");
    }

    /// <summary>
    /// Installs the dotnetup tool itself using the platform-specific bootstrap script.
    /// </summary>
    public static async Task<ProcessResult> InstallDotnetUpAsync(CancellationToken ct = default)
    {
        // Use RunInteractiveAsync on Windows (UseShellExecute=true avoids Access Denied)
        if (OperatingSystem.IsWindows())
        {
            var (shell, args) = BootstrapCommand();
            int exitCode = await ProcessRunner.RunInteractiveAsync(shell, args);
            return new ProcessResult(exitCode, "", "", TimeSpan.Zero);
        }

        var (command, arguments) = BootstrapCommand();
        var result = await ProcessRunner.RunWithCallbackAsync(command, arguments, null, null, null, ct);

        // Auto-add dotnetup and dotnet to PATH on macOS/Linux
        if (result.ExitCode == 0)
        {
            EnsurePathInShellProfile();
            RefreshPath();
            Console.WriteLine();
            Console.WriteLine("\u2713 dotnetup and dotnet paths have been added to your shell profile automatically.");
            Console.WriteLine("  No manual PATH setup needed — dsm handles it for you.");
        }

        return result;
    }

    /// <summary>
    /// Ensures dotnetup and (when no dotnet is resolvable on PATH yet) a dotnet root are on the
    /// current process PATH and in the shell profile (.zshrc/.bashrc/fish). Uses the same
    /// never-shadow rule as <see cref="RefreshPath"/>. Call after any install that may have added
    /// dotnetup or dotnet.
    /// </summary>
    public static void EnsurePathInShellProfile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var dirsToAdd = GetCurrentPathCandidates().Where(Directory.Exists).ToList();
        if (dirsToAdd.Count == 0) return;

        // Add to current process PATH
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        string updated = PrependMissingDirs(currentPath, dirsToAdd, Directory.Exists, Path.PathSeparator);
        if (!ReferenceEquals(updated, currentPath))
            Environment.SetEnvironmentVariable("PATH", updated);

        // Add to shell profile
        string shell = Path.GetFileName(Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash");
        string profile = shell switch
        {
            "zsh" => Path.Combine(home, ".zshrc"),
            "fish" => Path.Combine(home, ".config", "fish", "config.fish"),
            _ => Path.Combine(home, ".bashrc")
        };

        string profileContent = File.Exists(profile) ? File.ReadAllText(profile) : "";
        foreach (var dir in dirsToAdd)
        {
            if (!profileContent.Contains(dir))
            {
                string line = shell == "fish"
                    ? $"fish_add_path \"{dir}\""
                    : $"export PATH=\"{dir}:$PATH\"";
                File.AppendAllText(profile, $"\n{line}\n");
            }
        }
    }

    /// <summary>
    /// Ensures common dotnet and dotnetup paths are on the current process PATH.
    /// Call at startup and after installations so commands resolve immediately.
    /// </summary>
    public static void RefreshPath()
    {
        // The install folder the user explicitly chose (and asked dsm to remember) wins over
        // whatever dotnet PATH resolves first, so dsm lists what it installs.
        string? remembered = SettingsStore.Load().InstallRoot;
        if (remembered is not null)
            PreferRootOnPath(remembered);

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        IReadOnlyList<string> candidates = GetCurrentPathCandidates();
        string updated = PrependMissingDirs(currentPath, candidates, Directory.Exists, Path.PathSeparator);
        if (!ReferenceEquals(updated, currentPath))
            Environment.SetEnvironmentVariable("PATH", updated);
    }

    /// <summary>
    /// Puts <paramref name="root"/> first on the current process PATH (session only) when it holds a
    /// <c>dotnet</c> muxer — used after dotnetup installed into an explicitly chosen folder with
    /// <c>--set-default-install</c>, so dsm immediately reads the folder new shells will use.
    /// </summary>
    public static void PreferRootOnPath(string root)
    {
        string? preferred = GetPreferredRootOnPath(root, File.Exists);
        if (preferred is null) return;

        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        string updated = PrependMissingDirs(
            RemovePathEntry(currentPath, preferred, Path.PathSeparator), [preferred], Directory.Exists, Path.PathSeparator);
        Environment.SetEnvironmentVariable("PATH", updated);
    }

    /// <summary>Normalized <paramref name="root"/> when it contains a <c>dotnet</c> muxer, otherwise null.</summary>
    internal static string? GetPreferredRootOnPath(string? root, Func<string, bool> fileExists)
    {
        string normalized = root?.Trim().TrimEnd('/', '\\') ?? "";
        return normalized.Length > 0 && fileExists(Path.Combine(normalized, MuxerFileName(OperatingSystem.IsWindows())))
            ? normalized
            : null;
    }

    /// <summary>Removes every entry equal to <paramref name="dir"/> (ignoring trailing separators) from a PATH string.</summary>
    internal static string RemovePathEntry(string path, string dir, char separator)
    {
        StringComparison comparison = separator == ';' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string target = dir.TrimEnd('/', '\\');
        return string.Join(separator, path
            .Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(e => !e.TrimEnd('/', '\\').Equals(target, comparison)));
    }

    /// <summary>
    /// The PATH candidates for this machine's environment, evaluated against the current process PATH.
    /// </summary>
    private static IReadOnlyList<string> GetCurrentPathCandidates()
    {
        // Only when dsm lets dotnetup pick its default root (no user-level active root, see
        // InstallLocationService) — otherwise installs are pinned to the active root and PATH
        // must keep pointing at it.
        string? activeRoot = InstallLocationService.GetActiveInstallRoot();
        // Any dotnet on PATH (user-level or system-wide) must never be shadowed by a fallback root.
        bool dotnetOnPath = InstallLocationService.FindDotnetMuxerOnPath() is not null;

        return GetPathCandidates(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            OperatingSystem.IsWindows(),
            OperatingSystem.IsMacOS(),
            includeDotnetUpRoot: activeRoot is null,
            dotnetOnPath,
            Environment.GetEnvironmentVariable("DOTNET_ROOT"),
            File.Exists);
    }

    /// <summary>The platform's <c>dotnet</c> muxer file name.</summary>
    internal static string MuxerFileName(bool isWindows) => isWindows ? "dotnet.exe" : "dotnet";

    /// <summary>
    /// dotnetup's default .NET installation root — where it installs when no
    /// <c>--install-path</c> is given (e.g. on a machine without a user-level dotnet):
    /// <c>%LOCALAPPDATA%\dotnetup\dotnet</c>, <c>~/Library/Application Support/dotnetup/dotnet</c>,
    /// or <c>$XDG_DATA_HOME/dotnetup/dotnet</c> (default <c>~/.local/share/dotnetup/dotnet</c>).
    /// </summary>
    internal static string GetDotnetUpDefaultInstallRoot(
        string home, string? xdgDataHome, string localAppData, bool isWindows, bool isMacOS)
    {
        string dataDir = isWindows ? localAppData
            : isMacOS ? Path.Combine(home, "Library", "Application Support")
            : !string.IsNullOrWhiteSpace(xdgDataHome) ? xdgDataHome
            : Path.Combine(home, ".local", "share");
        return Path.Combine(dataDir, "dotnetup", "dotnet");
    }

    /// <summary>
    /// Directories that hold <c>dotnetup</c> or a user-level <c>dotnet</c>, in PATH priority order.
    /// The dotnetup default root (when included) comes first: installs made there with
    /// <c>--set-default-install</c> become the user's default dotnet in new shells, so dsm must
    /// see them immediately too.
    /// </summary>
    /// <remarks>
    /// A fallback dotnet root is added only when no <c>dotnet</c> (user-level or system-wide) is
    /// already resolvable on PATH — never ahead of it — and then just one: <c>DOTNET_ROOT</c> when it holds a muxer, else the
    /// first legacy root (pre-0.2.0 dotnetup, dotnet-install script) that does. Prepending every
    /// existing legacy folder let a stale <c>~/.dotnet</c> shadow the user's real dotnet, so dsm listed
    /// the wrong SDKs as unmanaged and pinned migrations to a folder dotnetup doesn't track.
    /// </remarks>
    internal static IReadOnlyList<string> GetPathCandidates(
        string home, string? xdgDataHome, string localAppData, bool isWindows, bool isMacOS,
        bool includeDotnetUpRoot, bool dotnetOnPath, string? dotnetRootEnv, Func<string, bool> fileExists)
    {
        var dirs = new List<string> { Path.Combine(home, ".dotnetup") };
        if (includeDotnetUpRoot)
            dirs.Add(GetDotnetUpDefaultInstallRoot(home, xdgDataHome, localAppData, isWindows, isMacOS));

        if (dotnetOnPath)
            return dirs;

        var fallbacks = new List<string>();
        if (!string.IsNullOrWhiteSpace(dotnetRootEnv))
            fallbacks.Add(dotnetRootEnv.Trim().TrimEnd('/', '\\'));
        fallbacks.AddRange(GetLegacyDotnetRoots(home, isWindows));

        string muxer = MuxerFileName(isWindows);
        string? fallback = fallbacks.FirstOrDefault(d => fileExists(Path.Combine(d, muxer)));
        if (fallback is not null)
            dirs.Add(fallback);
        return dirs;
    }

    /// <summary>Pre-0.2.0 dotnetup roots and the dotnet-install script default (Unix only).</summary>
    internal static IReadOnlyList<string> GetLegacyDotnetRoots(string home, bool isWindows) =>
        isWindows
            ? []
            : [Path.Combine(home, "Library", "Application Support", "dotnet"), Path.Combine(home, ".dotnet")];

    /// <summary>
    /// Prepends each existing directory not already on <paramref name="path"/> (compared entry by
    /// entry, ignoring trailing separators). Returns the same instance when nothing changes.
    /// </summary>
    internal static string PrependMissingDirs(
        string path, IEnumerable<string> dirs, Func<string, bool> directoryExists, char separator)
    {
        StringComparison comparison = separator == ';' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var entries = path.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(e => e.TrimEnd('/', '\\'))
            .ToList();

        var toAdd = dirs
            .Where(d => directoryExists(d)
                && !entries.Any(e => e.Equals(d.TrimEnd('/', '\\'), comparison)))
            .ToList();

        return toAdd.Count == 0
            ? path
            : string.Join(separator, toAdd) + (path.Length > 0 ? separator + path : "");
    }

    /// <summary>
    /// Parses the text output of <c>dotnet --list-sdks</c> into a list of <see cref="SdkInfo"/>.
    /// Each line has the format: <c>VERSION [INSTALL_PATH]</c>.
    /// </summary>
    internal static List<SdkInfo> ParseDotnetSdkList(string output)
    {
        var installations = new List<SdkInfo>();

        foreach (ReadOnlySpan<char> rawLine in output.AsSpan().EnumerateLines())
        {
            ReadOnlySpan<char> line = rawLine.Trim();
            int separatorIndex = line.IndexOf(" [");
            int openBracketIndex = line.IndexOf('[');
            int closeBracketIndex = line.LastIndexOf(']');

            if (separatorIndex <= 0 || openBracketIndex < 0 || closeBracketIndex <= openBracketIndex)
            {
                continue;
            }

            ReadOnlySpan<char> version = line[..separatorIndex].Trim();
            ReadOnlySpan<char> installRoot = line[(openBracketIndex + 1)..closeBracketIndex].Trim();

            if (version.IsEmpty || installRoot.IsEmpty)
            {
                continue;
            }

            installations.Add(new SdkInfo("SDK", version.ToString(), installRoot.ToString(), string.Empty));
        }

        return installations;
    }

    /// <summary>
    /// Parses the text output of <c>dotnet --list-runtimes</c> into a list of <see cref="SdkInfo"/>.
    /// Each line has the format: <c>RUNTIME_NAME VERSION [INSTALL_PATH]</c>.
    /// </summary>
    internal static List<SdkInfo> ParseDotnetRuntimeList(string output)
    {
        var installations = new List<SdkInfo>();

        foreach (ReadOnlySpan<char> rawLine in output.AsSpan().EnumerateLines())
        {
            // Format: "Microsoft.NETCore.App 10.0.0 [/path/to/shared/Microsoft.NETCore.App]"
            ReadOnlySpan<char> line = rawLine.Trim();
            int firstSpace = line.IndexOf(' ');
            if (firstSpace <= 0) continue;

            ReadOnlySpan<char> runtimeName = line[..firstSpace];
            ReadOnlySpan<char> remaining = line[(firstSpace + 1)..].TrimStart();

            int separatorIndex = remaining.IndexOf(" [");
            int openBracketIndex = remaining.IndexOf('[');
            int closeBracketIndex = remaining.LastIndexOf(']');

            if (separatorIndex <= 0 || openBracketIndex < 0 || closeBracketIndex <= openBracketIndex)
                continue;

            ReadOnlySpan<char> version = remaining[..separatorIndex].Trim();
            ReadOnlySpan<char> installRoot = remaining[(openBracketIndex + 1)..closeBracketIndex].Trim();

            if (version.IsEmpty || installRoot.IsEmpty)
                continue;

            // Map runtime names to component types (literals — no allocation for known names).
            string component =
                runtimeName.SequenceEqual("Microsoft.NETCore.App") ? "Runtime" :
                runtimeName.SequenceEqual("Microsoft.AspNetCore.App") ? "ASP.NET Core" :
                runtimeName.SequenceEqual("Microsoft.WindowsDesktop.App") ? "Windows Desktop" :
                runtimeName.ToString();

            installations.Add(new SdkInfo(component, version.ToString(), installRoot.ToString(), string.Empty));
        }

        return installations;
    }
}
