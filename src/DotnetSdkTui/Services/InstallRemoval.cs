using System.Text.Json;
using System.Text.RegularExpressions;

namespace DotnetSdkTui.Services;

/// <summary>A shared framework (runtime) folder: <c>shared/&lt;Framework&gt;/&lt;Version&gt;</c>.</summary>
internal sealed record RuntimeRef(string Framework, string Version);

/// <summary>
/// The runtimes an SDK ships with (its "bundle"), read from the SDK folder, and its workload feature band.
/// <c>NetCore</c>/<c>AspNetCore</c> are null when they can't be determined.
/// </summary>
internal sealed record SdkBundle(string Version, string? NetCore, string? AspNetCore, string Band);

/// <summary>What deleting some SDKs/runtimes from one dotnet folder would remove, and what it keeps (and why).</summary>
internal sealed class RemovalPlan(string root)
{
    public string Root { get; } = root;

    /// <summary>Human-readable items that will be removed.</summary>
    public List<string> Removed { get; } = [];

    /// <summary>Human-readable items deliberately left in place, with the reason.</summary>
    public List<string> Kept { get; } = [];

    /// <summary>Absolute files/folders to delete, all strictly inside <see cref="Root"/>.</summary>
    public List<string> Paths { get; } = [];

    /// <summary>True when nothing .NET is left in the folder afterwards (muxer and host go too).</summary>
    public bool RemovesWholeInstall { get; set; }

    public bool IsEmpty => Paths.Count == 0;
}

/// <summary>Outcome of <see cref="InstallRemoval.Execute"/>.</summary>
internal sealed record RemovalResult(int Deleted, IReadOnlyList<string> Errors);

/// <summary>
/// Deletes SDKs/runtimes from a dotnet folder dotnetup doesn't manage (an "Other location" such as a
/// leftover <c>~/.dotnet</c>), the way the .NET installers lay them out — never the whole folder.
/// </summary>
/// <remarks>
/// <para>
/// <c>~/.dotnet</c> is also the .NET CLI's per-user folder (global <c>tools</c>, caches, first-run
/// sentinels), so only paths that belong to the removed versions are touched.
/// </para>
/// <para>
/// An SDK ships with a runtime (Microsoft.NETCore.App + Microsoft.AspNetCore.App, plus hostfxr, targeting
/// and apphost packs, templates). Those go with the SDK unless another SDK left in the folder ships the
/// same version; a runtime that a remaining SDK ships with is never removed on its own. Workload files are
/// garbage-collected per feature band, conservatively: anything another band or install still references
/// stays.
/// </para>
/// </remarks>
internal static partial class InstallRemoval
{
    internal const string NetCoreApp = "Microsoft.NETCore.App";
    internal const string AspNetCoreApp = "Microsoft.AspNetCore.App";
    internal const string WindowsDesktopApp = "Microsoft.WindowsDesktop.App";

    /// <summary>
    /// The dotnet folder for a path as printed by <c>dotnet --list-sdks</c> (<c>&lt;root&gt;/sdk</c>) or
    /// <c>--list-runtimes</c> (<c>&lt;root&gt;/shared/&lt;framework&gt;</c>).
    /// </summary>
    internal static string DotnetRootOf(string listedPath)
    {
        string p = listedPath.Trim().TrimEnd('/', '\\');
        string? parent = Path.GetDirectoryName(p);
        if (parent is null) return p;
        if (Path.GetFileName(p).Equals("sdk", StringComparison.OrdinalIgnoreCase))
            return parent;
        if (Path.GetFileName(parent).Equals("shared", StringComparison.OrdinalIgnoreCase))
            return Path.GetDirectoryName(parent) ?? p;
        return p;
    }

    /// <summary>The framework name for a <c>--list-runtimes</c> path (<c>&lt;root&gt;/shared/&lt;framework&gt;</c>).</summary>
    internal static string FrameworkOf(string listedPath) =>
        Path.GetFileName(listedPath.Trim().TrimEnd('/', '\\'));

    /// <summary>Reads which runtime versions SDK <paramref name="sdkVersion"/> in <paramref name="root"/> ships with.</summary>
    internal static SdkBundle ReadBundle(string root, string sdkVersion)
    {
        string sdkDir = Path.Combine(root, "sdk", sdkVersion);
        string band = WorkloadService.DeriveFeatureBand(sdkVersion);
        string? netCore = null, tfm = null, aspNetCore = null;

        try
        {
            string config = Path.Combine(sdkDir, "dotnet.runtimeconfig.json");
            if (File.Exists(config))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(config));
                JsonElement options = doc.RootElement.GetProperty("runtimeOptions");
                if (options.TryGetProperty("tfm", out JsonElement t)) tfm = t.GetString();
                if (options.TryGetProperty("framework", out JsonElement fw)
                    && fw.TryGetProperty("version", out JsonElement v))
                    netCore = v.GetString();
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
                                      or KeyNotFoundException or InvalidOperationException)
        {
        }

        try
        {
            string props = Path.Combine(sdkDir, "Microsoft.NETCoreSdk.BundledVersions.props");
            if (File.Exists(props))
            {
                string text = File.ReadAllText(props);
                netCore ??= BundledNetCoreRegex().Match(text) is { Success: true } m ? m.Groups[1].Value : null;
                aspNetCore = FindAspNetCoreVersion(text, tfm);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }

        return new SdkBundle(sdkVersion, netCore, aspNetCore, band);
    }

    /// <summary>
    /// The ASP.NET Core runtime an SDK ships with: <c>LatestRuntimeFrameworkVersion</c> of the
    /// <c>Microsoft.AspNetCore.App</c> known framework reference for the SDK's own target framework.
    /// </summary>
    internal static string? FindAspNetCoreVersion(string bundledVersionsProps, string? tfm)
    {
        if (string.IsNullOrEmpty(tfm)) return null;
        foreach (Match element in KnownFrameworkReferenceRegex().Matches(bundledVersionsProps))
        {
            string attrs = element.Groups[1].Value;
            if (Attr(attrs, "Include") == AspNetCoreApp
                && string.Equals(Attr(attrs, "TargetFramework"), tfm, StringComparison.OrdinalIgnoreCase))
                return Attr(attrs, "LatestRuntimeFrameworkVersion");
        }
        return null;

        static string? Attr(string attrs, string name) =>
            Regex.Match(attrs, $@"\b{name}=""([^""]*)""") is { Success: true } m ? m.Groups[1].Value : null;
    }

    /// <summary>The SDKs left in <paramref name="root"/> that ship with runtime <paramref name="runtime"/>.</summary>
    internal static IReadOnlyList<string> FindBundlingSdks(string root, RuntimeRef runtime, IEnumerable<string>? excludingSdks = null)
    {
        var excluded = new HashSet<string>(excludingSdks ?? [], StringComparer.OrdinalIgnoreCase);
        return ListNames(Path.Combine(root, "sdk"))
            .Where(v => !excluded.Contains(v))
            .Where(v => BundledRuntimes(ReadBundle(root, v)).Contains(runtime))
            .ToList();
    }

    /// <summary>
    /// Plans removing <paramref name="sdkVersions"/> and <paramref name="runtimes"/> from <paramref name="root"/>.
    /// </summary>
    /// <param name="bandUsedElsewhere">
    /// True when another .NET install keeps "user-local" workloads for that band in this folder — only possible
    /// when <paramref name="root"/> is the CLI's per-user <c>~/.dotnet</c>. Such workload files are kept.
    /// </param>
    internal static RemovalPlan Plan(
        string root, IEnumerable<string> sdkVersions, IEnumerable<RuntimeRef> runtimes,
        Func<string, bool>? bandUsedElsewhere = null)
    {
        root = Path.GetFullPath(root.Trim().TrimEnd('/', '\\'));
        bandUsedElsewhere ??= _ => false;
        var plan = new RemovalPlan(root);
        var comparer = StringComparer.OrdinalIgnoreCase;

        List<string> allSdks = ListNames(Path.Combine(root, "sdk"));
        var removeSdks = new List<string>();
        foreach (string v in sdkVersions.Distinct(comparer))
        {
            string? match = allSdks.FirstOrDefault(s => comparer.Equals(s, v));
            if (match is null) plan.Kept.Add($"SDK {v} is no longer in this folder.");
            else removeSdks.Add(match);
        }
        List<string> remainingSdks = allSdks.Where(s => !removeSdks.Contains(s, comparer)).ToList();

        Dictionary<string, SdkBundle> remainingBundles = remainingSdks.ToDictionary(v => v, v => ReadBundle(root, v), comparer);
        string? KeptBy(RuntimeRef r) =>
            remainingBundles.Values.FirstOrDefault(b => BundledRuntimes(b).Contains(r))?.Version;

        var removeRuntimes = new List<RuntimeRef>();
        var removedBundles = new List<SdkBundle>();

        foreach (string v in removeSdks)
        {
            plan.Paths.Add(Path.Combine(root, "sdk", v));
            plan.Removed.Add($"SDK {v}");

            SdkBundle bundle = ReadBundle(root, v);
            removedBundles.Add(bundle);
            if (bundle.NetCore is null)
                plan.Kept.Add($"Couldn't tell which runtime ships with SDK {v}; runtimes are left in place.");

            foreach (RuntimeRef r in BundledRuntimes(bundle))
            {
                if (!Directory.Exists(SharedDir(root, r)) || removeRuntimes.Contains(r)) continue;
                if (KeptBy(r) is { } other)
                {
                    plan.Kept.Add($"{r.Framework} {r.Version} — SDK {other} in this folder ships with it too.");
                    continue;
                }
                removeRuntimes.Add(r);
                plan.Removed.Add($"{r.Framework} {r.Version} (ships with SDK {v})");
            }
        }

        foreach (RuntimeRef r in runtimes.Distinct())
        {
            if (removeRuntimes.Contains(r)) continue;
            if (!Directory.Exists(SharedDir(root, r)))
            {
                plan.Kept.Add($"{r.Framework} {r.Version} is no longer in this folder.");
                continue;
            }
            if (KeptBy(r) is { } sdk)
            {
                plan.Kept.Add($"{r.Framework} {r.Version} ships with SDK {sdk} in this folder — delete or migrate that SDK to remove it.");
                continue;
            }
            removeRuntimes.Add(r);
            plan.Removed.Add($"{r.Framework} {r.Version}");
        }

        foreach (RuntimeRef r in removeRuntimes)
            plan.Paths.Add(SharedDir(root, r));

        List<RuntimeRef> remainingRuntimes = ListNames(Path.Combine(root, "shared"))
            .SelectMany(fw => ListNames(Path.Combine(root, "shared", fw)).Select(ver => new RuntimeRef(fw, ver)))
            .Where(r => !removeRuntimes.Contains(r))
            .ToList();
        bool nothingLeft = remainingSdks.Count == 0 && remainingRuntimes.Count == 0;

        // hostfxr: the muxer needs one, so keep the last one unless the whole install goes.
        string fxrDir = Path.Combine(root, "host", "fxr");
        List<string> fxrs = ListNames(fxrDir);
        var removedNetCore = removeRuntimes.Where(r => r.Framework == NetCoreApp).Select(r => r.Version).ToHashSet(comparer);
        bool fxrRemains = fxrs.Any(f => !removedNetCore.Contains(f));
        foreach (string f in fxrs.Where(removedNetCore.Contains))
            if (fxrRemains || nothingLeft)
                plan.Paths.Add(Path.Combine(fxrDir, f));

        // Targeting/apphost packs and templates belong to the removed SDKs' bundled versions.
        var bundleVersions = removedBundles
            .SelectMany(b => new[] { (NetCoreApp, b.NetCore), (WindowsDesktopApp, b.NetCore), (AspNetCoreApp, b.AspNetCore) })
            .Where(x => x.Item2 is not null)
            .Select(x => new RuntimeRef(x.Item1, x.Item2!))
            .Where(r => KeptBy(r) is null)
            .Distinct()
            .ToList();
        foreach (string pack in ListNames(Path.Combine(root, "packs")).Where(IsSdkPack))
        {
            string? fw = new[] { NetCoreApp, AspNetCoreApp, WindowsDesktopApp }
                .FirstOrDefault(f => pack.StartsWith(f + ".", StringComparison.OrdinalIgnoreCase));
            if (fw is null) continue;
            foreach (RuntimeRef r in bundleVersions.Where(r => r.Framework == fw))
                if (!IsWorkloadPack(root, pack, r.Version))
                    AddIfExists(plan, Path.Combine(root, "packs", pack, r.Version));
        }
        var remainingTemplateVersions = remainingBundles.Values
            .SelectMany(b => new[] { b.NetCore, b.AspNetCore })
            .Where(v => v is not null)
            .ToHashSet(comparer);
        foreach (string? t in removedBundles.SelectMany(b => new[] { b.AspNetCore, b.NetCore }).Distinct(comparer))
            if (t is not null && !remainingTemplateVersions.Contains(t))
                AddIfExists(plan, Path.Combine(root, "templates", t));

        PlanWorkloadCleanup(plan, removedBundles, remainingBundles.Values.ToList(), nothingLeft, bandUsedElsewhere);

        if (!nothingLeft && plan.Paths.Any(p => IsUnder(Path.Combine(root, "host"), p)
                                               || IsUnder(Path.Combine(root, "packs"), p)
                                               || IsUnder(Path.Combine(root, "templates"), p)))
            plan.Removed.Add("their host resolver, targeting packs and templates");

        if (nothingLeft && plan.Paths.Count > 0)
        {
            plan.RemovesWholeInstall = true;
            plan.Removed.Add("the dotnet host, packs and templates (no SDK or runtime is left in this folder)");
            foreach (string name in new[] { "dotnet", "dotnet.exe", "LICENSE.txt", "ThirdPartyNotices.txt", "host", "templates" })
                AddIfExists(plan, Path.Combine(root, name));

            string meta = Path.Combine(root, "metadata", "workloads");
            bool workloadsShared = ListNames(meta).Concat(ListNames(Path.Combine(root, "sdk-manifests")))
                .Any(b => LooksLikeBand(b) && bandUsedElsewhere(b));
            if (workloadsShared)
            {
                // Only the packs that came with the SDKs; workload packs may serve another install.
                foreach (string pack in ListNames(Path.Combine(root, "packs")).Where(IsSdkPack))
                {
                    string packDir = Path.Combine(root, "packs", pack);
                    if (!Directory.Exists(Path.Combine(root, "metadata", "workloads", "InstalledPacks", "v1", pack)))
                        AddIfExists(plan, packDir);
                    else
                        foreach (string v in ListNames(packDir).Where(v => !IsWorkloadPack(root, pack, v)))
                            AddIfExists(plan, Path.Combine(packDir, v));
                }
            }
            else
            {
                foreach (string name in new[] { "packs", "sdk-manifests", "library-packs", "template-packs" })
                    AddIfExists(plan, Path.Combine(root, name));
                AddIfExists(plan, meta);
            }
        }

        // Drop paths nested inside another planned path, keeping order stable.
        List<string> distinct = plan.Paths.Distinct(comparer).ToList();
        plan.Paths.Clear();
        plan.Paths.AddRange(distinct.Where(p => !distinct.Any(o => !comparer.Equals(o, p) && IsUnder(o, p))));
        return plan;
    }

    /// <summary>
    /// Workload files for the removed SDKs' feature bands: install records, packs and manifests that nothing
    /// else references, and the band's own metadata.
    /// </summary>
    private static void PlanWorkloadCleanup(
        RemovalPlan plan, List<SdkBundle> removed, List<SdkBundle> remaining, bool nothingLeft, Func<string, bool> bandUsedElsewhere)
    {
        var comparer = StringComparer.OrdinalIgnoreCase;
        string root = plan.Root;
        string meta = Path.Combine(root, "metadata", "workloads");
        var remainingBands = remaining.Select(b => b.Band).ToHashSet(comparer);

        var bands = new HashSet<string>(comparer);
        foreach (string band in removed.Select(b => b.Band).Distinct(comparer))
        {
            if (remainingBands.Contains(band)) continue;
            if (bandUsedElsewhere(band))
            {
                plan.Kept.Add($"Workloads for band {band} — another .NET install keeps its user-level workloads here.");
                continue;
            }
            bands.Add(band);
        }

        if (nothingLeft)
        {
            // Every band in the folder is unused now — unless another install keeps user-level workloads here.
            foreach (string band in ListNames(meta).Concat(ListNames(Path.Combine(root, "sdk-manifests"))))
                if (!bandUsedElsewhere(band) && LooksLikeBand(band))
                    bands.Add(band);
        }

        if (bands.Count == 0) return;

        foreach (string band in bands.Order(comparer))
        {
            List<string> ids = ListNames(Path.Combine(meta, band, "InstalledWorkloads"));
            if (Directory.Exists(Path.Combine(meta, band)))
            {
                plan.Paths.Add(Path.Combine(meta, band));
                if (ids.Count > 0)
                    plan.Removed.Add($"workloads for band {band}: {string.Join(", ", ids)}");
            }
        }

        // Install records: metadata/workloads/Installed*/v1/.../<referencing band>.
        var referencedManifestBands = new HashSet<string>(comparer);
        foreach (string kind in ListNames(meta).Where(n => n.StartsWith("Installed", StringComparison.Ordinal)))
        {
            string v1 = Path.Combine(meta, kind, "v1");
            if (!Directory.Exists(v1)) continue;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(v1, "*", SearchOption.AllDirectories).ToList(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { continue; }

            foreach (IGrouping<string, string> group in files.GroupBy(f => Path.GetDirectoryName(f)!, comparer))
            {
                List<string> stale = group.Where(f => bands.Contains(Path.GetFileName(f))).ToList();
                bool freed = stale.Count == group.Count();
                string[] parts = Path.GetRelativePath(v1, group.Key).Split(Path.DirectorySeparatorChar);

                if (kind == "InstalledManifests" && parts.Length == 3 && !freed)
                    referencedManifestBands.Add(parts[2]);
                if (stale.Count == 0) continue;

                plan.Paths.AddRange(freed ? [group.Key] : stale);
                if (!freed) continue;

                if (kind == "InstalledPacks" && parts.Length == 2)
                    AddPackPayload(plan, parts[0], parts[1]);
                else if (kind == "InstalledManifests" && parts.Length == 3)
                    AddIfExists(plan, Path.Combine(root, "sdk-manifests", parts[2], parts[0], parts[1]));
            }
        }

        // A band's manifests folder may also serve later SDKs of the same major.minor (they fall back to it).
        var remainingMajorMinor = remaining.Select(b => MajorMinor(b.Version)).ToHashSet(comparer);
        foreach (string band in bands)
        {
            string dir = Path.Combine(root, "sdk-manifests", band);
            if (Directory.Exists(dir)
                && !referencedManifestBands.Contains(band)
                && !remainingMajorMinor.Contains(MajorMinor(band)))
                plan.Paths.Add(dir);
        }
    }

    /// <summary>A workload pack's files: <c>packs/&lt;id&gt;/&lt;version&gt;</c> or a library/template <c>.nupkg</c>.</summary>
    private static void AddPackPayload(RemovalPlan plan, string packId, string packVersion)
    {
        if (AddIfExists(plan, Path.Combine(plan.Root, "packs", packId, packVersion)))
            return;

        string nupkg = $"{packId}.{packVersion}.nupkg";
        foreach (string folder in new[] { "library-packs", "template-packs" })
        {
            string dir = Path.Combine(plan.Root, folder);
            if (!Directory.Exists(dir)) continue;
            foreach (string file in Directory.EnumerateFiles(dir))
                if (Path.GetFileName(file).Equals(nupkg, StringComparison.OrdinalIgnoreCase))
                    plan.Paths.Add(file);
        }
    }

    /// <summary>
    /// Deletes every path in <paramref name="plan"/> (symlinks themselves, never their targets), then prunes
    /// folders left empty inside the dotnet folder. Paths outside the folder are refused.
    /// </summary>
    internal static RemovalResult Execute(RemovalPlan plan)
    {
        var errors = new List<string>();
        int deleted = 0;
        var parents = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in plan.Paths)
        {
            string path = Path.GetFullPath(raw);
            if (!IsUnder(plan.Root, path))
            {
                errors.Add($"Skipped {path}: outside {plan.Root}.");
                continue;
            }
            if (HasLinkedAncestor(plan.Root, path))
            {
                errors.Add($"Skipped {path}: a folder on its path is a symlink.");
                continue;
            }

            try
            {
                var info = new FileInfo(path);
                if (info.LinkTarget is not null || (info.Exists && !info.Attributes.HasFlag(FileAttributes.Directory)))
                {
                    if (Directory.Exists(path) && OperatingSystem.IsWindows()) Directory.Delete(path);
                    else File.Delete(path);
                }
                else if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
                else
                {
                    continue;
                }
                deleted++;
                if (Path.GetDirectoryName(path) is { } parent) parents.Add(parent);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                errors.Add($"{path}: {e.Message}");
            }
        }

        foreach (string start in parents.OrderByDescending(p => p.Length))
        {
            string? dir = start;
            while (dir is not null && IsUnder(plan.Root, dir))
            {
                try
                {
                    if (!Directory.Exists(dir) || Directory.EnumerateFileSystemEntries(dir).Any()) break;
                    Directory.Delete(dir);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { break; }
                dir = Path.GetDirectoryName(dir);
            }
        }

        return new RemovalResult(deleted, errors);
    }

    /// <summary>Total size in bytes of the planned paths (best effort; symlinks aren't followed).</summary>
    internal static long MeasureBytes(RemovalPlan plan)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        long total = 0;
        foreach (string path in plan.Paths)
        {
            try
            {
                if (File.Exists(path)) total += new FileInfo(path).Length;
                else if (Directory.Exists(path))
                    total += new DirectoryInfo(path).EnumerateFiles("*", options).Sum(f => f.Length);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        }
        return total;
    }

    /// <summary>
    /// The CLI's per-user folder (<c>$DOTNET_CLI_HOME/.dotnet</c> or <c>~/.dotnet</c>), where installs whose
    /// own folder isn't writable keep "user-local" workloads.
    /// </summary>
    internal static string CliHomeDotnetDir()
    {
        string? cliHome = Environment.GetEnvironmentVariable("DOTNET_CLI_HOME");
        string home = string.IsNullOrWhiteSpace(cliHome)
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : cliHome;
        return Path.Combine(home, ".dotnet");
    }

    /// <summary>
    /// Guard for <see cref="Plan"/>: when <paramref name="root"/> is the CLI's per-user folder, a band is in use
    /// if any of <paramref name="otherRoots"/> marks its workloads for that band as user-local.
    /// </summary>
    internal static Func<string, bool> UserLocalBandGuard(string root, IEnumerable<string> otherRoots)
    {
        if (!SamePath(root, CliHomeDotnetDir()))
            return _ => false;

        List<string> others = otherRoots
            .Where(r => !string.IsNullOrWhiteSpace(r) && !SamePath(r, root))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return band => others.Any(r => File.Exists(Path.Combine(r, "metadata", "workloads", band, "userlocal")));
    }

    /// <summary>Well-known machine-wide dotnet folders (other installs that may use user-local workloads).</summary>
    internal static IReadOnlyList<string> SystemDotnetRoots() =>
        OperatingSystem.IsWindows()
            ? [Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet")]
            : ["/usr/local/share/dotnet", "/usr/share/dotnet", "/usr/lib/dotnet", "/usr/lib64/dotnet", "/opt/dotnet"];

    /// <summary>True when both paths name the same folder, after resolving symlinks in every component.</summary>
    internal static bool SamePath(string a, string b) =>
        string.Equals(
            Canonical(a).TrimEnd('/', '\\'),
            Canonical(b).TrimEnd('/', '\\'),
            OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Absolute path with symlinks resolved in every component (like <c>realpath</c>, e.g. macOS
    /// <c>/tmp</c> → <c>/private/tmp</c>). Components that don't exist are kept as written.
    /// </summary>
    internal static string Canonical(string path, int depth = 0)
    {
        string full = Path.GetFullPath(path.Trim());
        string? rootPart = Path.GetPathRoot(full);
        if (string.IsNullOrEmpty(rootPart) || depth > 32)
            return full;

        string current = rootPart;
        string[] parts = full[rootPart.Length..].Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            try
            {
                if (new FileInfo(current).LinkTarget is null)
                    continue;
                FileSystemInfo? target = File.ResolveLinkTarget(current, returnFinalTarget: true);
                if (target is null)
                    continue;
                current = Canonical(target.FullName, depth + 1);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Unreadable component: compare the rest as written.
            }
        }
        return current;
    }

    /// <summary>True when a folder between <paramref name="root"/> (exclusive) and <paramref name="path"/> is a symlink.</summary>
    private static bool HasLinkedAncestor(string root, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        while (dir is not null && IsUnder(root, dir))
        {
            if (new DirectoryInfo(dir).LinkTarget is not null)
                return true;
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }

    /// <summary>"1.2 GB"-style size.</summary>
    internal static string FormatBytes(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / (double)(1L << 10):0} KB",
        _ => $"{bytes} B",
    };

    private static IEnumerable<RuntimeRef> BundledRuntimes(SdkBundle b)
    {
        if (b.NetCore is not null)
        {
            yield return new RuntimeRef(NetCoreApp, b.NetCore);
            yield return new RuntimeRef(WindowsDesktopApp, b.NetCore);
        }
        if (b.AspNetCore is not null)
            yield return new RuntimeRef(AspNetCoreApp, b.AspNetCore);
    }

    private static string SharedDir(string root, RuntimeRef r) => Path.Combine(root, "shared", r.Framework, r.Version);

    private static bool AddIfExists(RemovalPlan plan, string path)
    {
        if (!Directory.Exists(path) && !File.Exists(path) && new FileInfo(path).LinkTarget is null)
            return false;
        plan.Paths.Add(path);
        return true;
    }

    private static List<string> ListNames(string dir)
    {
        try
        {
            return Directory.Exists(dir)
                ? Directory.EnumerateFileSystemEntries(dir).Select(Path.GetFileName).OfType<string>()
                    .Where(n => !n.StartsWith('.')).ToList()
                : [];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>True when <paramref name="path"/> is strictly inside <paramref name="parent"/>.</summary>
    private static bool IsUnder(string parent, string path)
    {
        string p = Path.GetFullPath(parent).TrimEnd('/', '\\') + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(p, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeBand(string name) => name.Length > 0 && char.IsDigit(name[0]);

    /// <summary>
    /// Targeting/apphost packs the SDK itself ships. Other <c>Microsoft.NETCore.App.*</c> packs (Mono, AOT and
    /// runtime packs) come from workloads and are reference-counted by their install records instead.
    /// </summary>
    private static bool IsSdkPack(string pack) =>
        pack.Equals("NETStandard.Library.Ref", StringComparison.OrdinalIgnoreCase)
        || pack.Equals("Microsoft.NETCore.App.Ref", StringComparison.OrdinalIgnoreCase)
        || pack.Equals("Microsoft.AspNetCore.App.Ref", StringComparison.OrdinalIgnoreCase)
        || pack.Equals("Microsoft.AspNetCore.App.Internal.Assets", StringComparison.OrdinalIgnoreCase)
        || pack.Equals("Microsoft.WindowsDesktop.App.Ref", StringComparison.OrdinalIgnoreCase)
        || pack.StartsWith("Microsoft.NETCore.App.Host.", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when a workload has an install record for <paramref name="pack"/>/<paramref name="version"/>.</summary>
    private static bool IsWorkloadPack(string root, string pack, string version) =>
        Directory.Exists(Path.Combine(root, "metadata", "workloads", "InstalledPacks", "v1", pack, version));

    private static string MajorMinor(string version)
    {
        string[] parts = version.Split('.', '-');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    [GeneratedRegex(@"<BundledNETCoreAppPackageVersion>([^<]+)</BundledNETCoreAppPackageVersion>")]
    private static partial Regex BundledNetCoreRegex();

    [GeneratedRegex(@"<KnownFrameworkReference\b([^>]*)>", RegexOptions.Singleline)]
    private static partial Regex KnownFrameworkReferenceRegex();
}
