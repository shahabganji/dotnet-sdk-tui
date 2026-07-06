using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

/// <summary>
/// Service wrapping the native <c>dotnet workload</c> CLI. Reads (list, search, config,
/// --version) parse text tables into models; mutations (install/update/uninstall/repair)
/// are returned as command tuples so <see cref="App"/> can exit the TUI and stream real
/// terminal output — matching the SDKs/Runtimes UX.
/// </summary>
/// <remarks>
/// <para>
/// <b>Text-parsing risk:</b> as of .NET 10.0.301, only <c>dotnet workload search version
/// --format json</c> supports JSON. Everything else is human-readable table text. The
/// parser below is fenced with unit tests + fixtures under
/// <c>tests/DotnetSdkTui.Tests/Services/</c>; when a future SDK changes the format, tests
/// fail fast rather than the panel silently going blank.
/// </para>
/// <para>
/// Workloads are scoped per <b>SDK feature band</b>, not per exact SDK. <c>dotnet workload
/// list</c> reports state for whatever SDK the .NET host resolves to (governed by
/// <c>global.json</c> → newest). The panel header surfaces the active band so this isn't
/// invisible.
/// </para>
/// </remarks>
public static class WorkloadService
{
    /// <summary>Checks whether the <c>dotnet</c> CLI is available on PATH.</summary>
    public static bool IsInstalled() => ProcessRunner.IsCommandAvailable("dotnet");

    /// <summary>
    /// Fetches the workload host environment (workload version, update mode, active SDK
    /// version + feature band). When <paramref name="targetSdk"/> is set, the operation
    /// resolves to that specific SDK via a scratch-directory <c>global.json</c>; otherwise
    /// it uses whatever <c>dotnet</c> resolves to in the current shell. Returns null if
    /// <c>dotnet</c> is unavailable.
    /// </summary>
    /// <remarks>
    /// Extracts only the last non-empty stdout line for single-value reads. This is a
    /// defensive fallback against the .NET first-run banner (see App.RunAsync where
    /// <c>DOTNET_NOLOGO</c> + <c>DOTNET_SKIP_FIRST_TIME_EXPERIENCE</c> are set to prevent
    /// the banner from appearing in the first place).
    /// </remarks>
    public static async Task<WorkloadEnv?> GetEnvAsync(string? targetSdk = null, CancellationToken ct = default)
    {
        if (!IsInstalled()) return null;

        string? cwd = targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk);

        Task<ProcessResult> wlVerTask   = ProcessRunner.RunAsync("dotnet", "workload --version",            workingDirectory: cwd, ct: ct);
        Task<ProcessResult> modeTask    = ProcessRunner.RunAsync("dotnet", "workload config --update-mode", workingDirectory: cwd, ct: ct);
        Task<ProcessResult> sdkVerTask  = ProcessRunner.RunAsync("dotnet", "--version",                     workingDirectory: cwd, ct: ct);
        await Task.WhenAll(wlVerTask, modeTask, sdkVerTask);

        string workloadVersion = wlVerTask.Result.ExitCode == 0
            ? LastNonEmptyLine(wlVerTask.Result.Output) ?? "unknown"
            : "unknown";
        string updateMode = modeTask.Result.ExitCode == 0
            ? LastNonEmptyLine(modeTask.Result.Output) ?? "unknown"
            : "unknown";
        string sdkVersion = sdkVerTask.Result.ExitCode == 0
            ? LastNonEmptyLine(sdkVerTask.Result.Output) ?? (targetSdk ?? "unknown")
            : (targetSdk ?? "unknown");
        string band = DeriveFeatureBand(sdkVersion);

        return new WorkloadEnv(workloadVersion, updateMode, sdkVersion, band);
    }

    /// <summary>
    /// Returns the last non-empty, trimmed line of <paramref name="output"/>, or
    /// <c>null</c> when no such line exists. Used to strip the .NET first-run banner
    /// (Welcome to .NET, ASP.NET Core certs notice, docs links, …) from single-value
    /// stdout reads.
    /// </summary>
    internal static string? LastNonEmptyLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return null;
        string[] lines = output.Replace("\r\n", "\n").Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            string line = lines[i].Trim();
            if (line.Length > 0) return line;
        }
        return null;
    }

    /// <summary>
    /// Returns the merged list of workloads: every discoverable workload from
    /// <c>dotnet workload search</c>, annotated with installation state pulled from
    /// <c>dotnet workload list</c>. When <paramref name="targetSdk"/> is set, both
    /// underlying calls are scoped to that SDK via a scratch <c>global.json</c>.
    /// </summary>
    public static async Task<List<WorkloadInfo>> ListMergedAsync(string? targetSdk = null, string? searchPattern = null, CancellationToken ct = default)
    {
        Task<Dictionary<string, InstalledRow>> installedTask = ListInstalledAsync(targetSdk, ct);
        Task<List<AvailableRow>> availableTask = SearchAvailableAsync(targetSdk, searchPattern, ct);
        await Task.WhenAll(installedTask, availableTask);

        Dictionary<string, InstalledRow> installed = installedTask.Result;
        List<AvailableRow> available = availableTask.Result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var merged = new List<WorkloadInfo>(available.Count + installed.Count);

        foreach (AvailableRow row in available)
        {
            bool isInstalled = installed.TryGetValue(row.Id, out InstalledRow inst);
            merged.Add(new WorkloadInfo(
                row.Id,
                row.Description,
                isInstalled,
                isInstalled ? inst.ManifestVersion : null,
                isInstalled ? inst.InstallationSource : null,
                UpdateAvailable: false));
            seen.Add(row.Id);
        }

        // An installed workload might not appear in `search` output (e.g. removed from feed);
        // surface it anyway so the user can still uninstall it.
        foreach (KeyValuePair<string, InstalledRow> kv in installed)
        {
            if (seen.Contains(kv.Key)) continue;
            merged.Add(new WorkloadInfo(
                kv.Key,
                "(installed; not in current search catalog)",
                true,
                kv.Value.ManifestVersion,
                kv.Value.InstallationSource,
                UpdateAvailable: false));
        }

        merged.Sort((a, b) =>
        {
            int i = b.IsInstalled.CompareTo(a.IsInstalled);
            return i != 0 ? i : string.Compare(a.Id, b.Id, StringComparison.OrdinalIgnoreCase);
        });

        return merged;
    }

    /// <summary>
    /// Parses <c>dotnet workload list</c> into id → (manifest version, install source).
    /// When <paramref name="targetSdk"/> is set, invokes from the scratch dir that pins
    /// the .NET host to that SDK.
    /// </summary>
    public static async Task<Dictionary<string, InstalledRow>> ListInstalledAsync(string? targetSdk = null, CancellationToken ct = default)
    {
        var result = new Dictionary<string, InstalledRow>(StringComparer.OrdinalIgnoreCase);
        if (!IsInstalled()) return result;

        string? cwd = targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk);
        ProcessResult r = await ProcessRunner.RunAsync("dotnet", "workload list", workingDirectory: cwd, ct: ct);
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Output)) return result;

        foreach (string[] cells in WorkloadTableParser.ParseTable(r.Output, "Installed Workload Id"))
        {
            if (cells.Length < 1 || string.IsNullOrWhiteSpace(cells[0])) continue;
            string id      = cells[0].Trim();
            string version = cells.Length > 1 ? cells[1].Trim() : "";
            string source  = cells.Length > 2 ? cells[2].Trim() : "";
            result[id] = new InstalledRow(version, source);
        }

        return result;
    }

    /// <summary>
    /// Parses <c>dotnet workload search [pattern]</c> into (id, description) pairs.
    /// When <paramref name="targetSdk"/> is set, invokes from the scratch dir.
    /// </summary>
    public static async Task<List<AvailableRow>> SearchAvailableAsync(string? targetSdk = null, string? pattern = null, CancellationToken ct = default)
    {
        var result = new List<AvailableRow>();
        if (!IsInstalled()) return result;

        string args = string.IsNullOrWhiteSpace(pattern)
            ? "workload search"
            : $"workload search {EscapeArg(pattern)}";

        string? cwd = targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk);
        ProcessResult r = await ProcessRunner.RunAsync("dotnet", args, workingDirectory: cwd, ct: ct);
        if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Output)) return result;

        foreach (string[] cells in WorkloadTableParser.ParseTable(r.Output, "Workload ID"))
        {
            if (cells.Length < 1 || string.IsNullOrWhiteSpace(cells[0])) continue;
            string id = cells[0].Trim();
            string description = cells.Length > 1 ? cells[1].Trim() : "";
            result.Add(new AvailableRow(id, description));
        }

        return result;
    }

    /// <summary>
    /// Command tuple to install a workload (streams real terminal output). When
    /// <paramref name="targetSdk"/> is set, <c>Cwd</c> points at the scratch dir that
    /// pins the .NET host to that SDK.
    /// </summary>
    public static (string Command, string Args, string? Note, string? Cwd) BuildInstall(string id, bool includePreviews, string? targetSdk = null) =>
        ("dotnet",
         includePreviews
             ? $"workload install {EscapeArg(id)} --include-previews"
             : $"workload install {EscapeArg(id)}",
         null,
         targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk));

    /// <summary>Command tuple to uninstall a workload.</summary>
    public static (string Command, string Args, string? Note, string? Cwd) BuildUninstall(string id, string? targetSdk = null) =>
        ("dotnet",
         $"workload uninstall {EscapeArg(id)}",
         null,
         targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk));

    /// <summary>Command tuple to update all installed workloads.</summary>
    public static (string Command, string Args, string? Note, string? Cwd) BuildUpdate(bool includePreviews, string? targetSdk = null) =>
        ("dotnet",
         includePreviews ? "workload update --include-previews" : "workload update",
         null,
         targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk));

    /// <summary>Command tuple to repair workload installations.</summary>
    public static (string Command, string Args, string? Note, string? Cwd) BuildRepair(string? targetSdk = null) =>
        ("dotnet",
         "workload repair",
         null,
         targetSdk is null ? null : EnsureSdkPinDirectory(targetSdk));

    /// <summary>
    /// Command tuple to switch the workload update mode. This is a machine-wide setting,
    /// so <c>Cwd</c> is unused (no SDK-scoping applies).
    /// </summary>
    public static (string Command, string Args, string? Note, string? Cwd) BuildSetUpdateMode(string mode) =>
        ("dotnet",
         $"workload config --update-mode {EscapeArg(mode)}",
         $"Workload update mode set to '{mode}'. This is a machine-wide setting; all future workload updates on this machine use this mode.",
         null);

    /// <summary>
    /// Derives an SDK feature band from a full SDK version string.
    /// Rules: for <c>Major.Minor.Patch</c>, the band is <c>Major.Minor.{Patch/100 * 100}</c>,
    /// preserving any preview suffix (e.g. <c>-preview.5</c>).
    /// </summary>
    /// <example>
    /// <code>
    /// DeriveFeatureBand("10.0.301")               // "10.0.300"
    /// DeriveFeatureBand("8.0.422")                // "8.0.400"
    /// DeriveFeatureBand("11.0.100-preview.5.…")   // "11.0.100-preview.5"
    /// </code>
    /// </example>
    public static string DeriveFeatureBand(string sdkVersion)
    {
        if (string.IsNullOrWhiteSpace(sdkVersion)) return "unknown";

        // Split "10.0.301-preview.5.26302.115" → "10.0.301" + "-preview.5.…"
        int dashIdx = sdkVersion.IndexOf('-');
        string core = dashIdx >= 0 ? sdkVersion[..dashIdx] : sdkVersion;
        string suffix = dashIdx >= 0 ? sdkVersion[dashIdx..] : "";

        string[] parts = core.Split('.');
        if (parts.Length < 3) return sdkVersion;
        if (!int.TryParse(parts[2], out int patch)) return sdkVersion;

        int band = (patch / 100) * 100;
        string bandCore = $"{parts[0]}.{parts[1]}.{band:D3}";

        // For preview SDKs, keep the preview suffix trimmed to the meaningful part
        // (e.g. "-preview.5") — matches the on-disk sdk-manifests folder names.
        if (!string.IsNullOrEmpty(suffix))
        {
            // Keep up to the second dot after the initial "-", so "-preview.5.26302.115" → "-preview.5"
            int firstDot = suffix.IndexOf('.');
            if (firstDot > 0)
            {
                int secondDot = suffix.IndexOf('.', firstDot + 1);
                if (secondDot > 0) suffix = suffix[..secondDot];
            }
        }

        return bandCore + suffix;
    }

    /// <summary>
    /// Materialises a scratch directory containing a <c>global.json</c> that pins the .NET
    /// host to <paramref name="sdkVersion"/>, and returns its path. Every subsequent
    /// <c>dotnet workload …</c> invocation with that path as <c>WorkingDirectory</c> will
    /// deterministically resolve to that exact SDK — the mechanism that lets the panel
    /// target a specific feature band regardless of the user's shell state.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The scratch dir lives under <c>~/.dsm/scratch/&lt;sdkVersion&gt;/</c> (or
    /// <c>%LOCALAPPDATA%\dsm\scratch\&lt;sdkVersion&gt;\</c> on Windows). The file is
    /// written idempotently: if the existing content already matches, we skip the write.
    /// </para>
    /// <para>
    /// <c>rollForward: disable</c> is deliberate — it prevents the host from silently
    /// picking a newer compatible SDK, which would defeat band scoping.
    /// </para>
    /// </remarks>
    public static string EnsureSdkPinDirectory(string sdkVersion)
    {
        if (string.IsNullOrWhiteSpace(sdkVersion))
            throw new ArgumentException("SDK version is required.", nameof(sdkVersion));

        string root = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dsm", "scratch")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsm", "scratch");

        string dir = Path.Combine(root, sdkVersion);
        Directory.CreateDirectory(dir);

        string file = Path.Combine(dir, "global.json");
        string content = $"{{\"sdk\":{{\"version\":\"{sdkVersion}\",\"rollForward\":\"disable\"}}}}";

        // Idempotent: only rewrite when content differs.
        if (!File.Exists(file) || File.ReadAllText(file) != content)
            File.WriteAllText(file, content);

        return dir;
    }

    private static string EscapeArg(string s) =>
        s.Contains(' ') || s.Contains('"') ? $"\"{s.Replace("\"", "\\\"")}\"" : s;

    /// <summary>Parsed row from <c>dotnet workload list</c>.</summary>
    public readonly record struct InstalledRow(string ManifestVersion, string InstallationSource);

    /// <summary>Parsed row from <c>dotnet workload search</c>.</summary>
    public readonly record struct AvailableRow(string Id, string Description);
}

/// <summary>
/// Parser for the fixed-width text tables produced by <c>dotnet workload list</c> and
/// <c>dotnet workload search</c>. Exposed as a static helper so tests can exercise it
/// directly against captured fixtures.
/// </summary>
/// <remarks>
/// Both commands emit the same shape:
/// <code>
///   [Preamble lines]
///   [Blank line(s)]
///   Header1   Header2    Header3
///   -----------------------------
///   value1    value2     value3
///   ...
///   [Optional trailing hint line]
/// </code>
/// Columns are separated by two-or-more spaces (single-space runs occur inside values,
/// e.g. ".NET MAUI SDK for all platforms"). We anchor column boundaries against the
/// header row's word starts, then split each data row on those column positions —
/// robust against space-in-value cases that a naive split fails on.
/// </remarks>
public static class WorkloadTableParser
{
    /// <summary>
    /// Parses a table from CLI output, locating the header row by
    /// <paramref name="headerKeyword"/> (e.g. "Installed Workload Id" or "Workload ID").
    /// Returns one row per data line as an array of cell strings.
    /// </summary>
    public static IEnumerable<string[]> ParseTable(string output, string headerKeyword)
    {
        string[] lines = output.Replace("\r\n", "\n").Split('\n');

        int headerIdx = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(headerKeyword, StringComparison.OrdinalIgnoreCase))
            {
                headerIdx = i;
                break;
            }
        }
        if (headerIdx < 0) yield break;

        // Column start positions: the first non-space char of each ≥2-space-separated
        // segment on the header line.
        int[] cols = FindColumnStarts(lines[headerIdx]);
        if (cols.Length == 0) yield break;

        // Skip the header + the dashed separator line (if present).
        int dataStart = headerIdx + 1;
        if (dataStart < lines.Length && IsSeparatorLine(lines[dataStart]))
            dataStart++;

        for (int i = dataStart; i < lines.Length; i++)
        {
            string line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("Use ", StringComparison.OrdinalIgnoreCase)) continue; // trailing hint
            if (IsSeparatorLine(line)) continue;

            yield return SplitByColumns(line, cols);
        }
    }

    /// <summary>Returns the 0-based column-start positions on <paramref name="header"/>.</summary>
    internal static int[] FindColumnStarts(string header)
    {
        // Match the start of each word/segment: start-of-string OR ≥2 spaces preceding.
        var starts = new List<int>();
        for (int i = 0; i < header.Length; i++)
        {
            if (char.IsWhiteSpace(header[i])) continue;
            if (i == 0) { starts.Add(0); continue; }
            // Look back: at least 2 spaces mean this is a new column.
            if (i >= 2 && header[i - 1] == ' ' && header[i - 2] == ' ')
                starts.Add(i);
        }
        return starts.ToArray();
    }

    /// <summary>A line is a separator if it's non-empty and made of only '-' and whitespace.</summary>
    internal static bool IsSeparatorLine(string line)
    {
        bool sawDash = false;
        foreach (char c in line)
        {
            if (c == '-') { sawDash = true; continue; }
            if (!char.IsWhiteSpace(c)) return false;
        }
        return sawDash;
    }

    /// <summary>Splits <paramref name="line"/> at the given 0-based column-start positions.</summary>
    internal static string[] SplitByColumns(string line, int[] cols)
    {
        var cells = new string[cols.Length];
        for (int c = 0; c < cols.Length; c++)
        {
            int start = cols[c];
            if (start >= line.Length) { cells[c] = ""; continue; }
            int end = c + 1 < cols.Length ? Math.Min(cols[c + 1], line.Length) : line.Length;
            cells[c] = line[start..end].Trim();
        }
        return cells;
    }
}
