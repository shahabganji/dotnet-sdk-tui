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
    public static async Task<List<SdkInfo>> ListTrackedAsync(CancellationToken ct = default)
    {
        var tracked = new List<SdkInfo>();
        try
        {
            ProcessResult result = await ProcessRunner.RunAsync("dotnetup", "list --format Json", ct: ct);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
                return tracked;

            using var doc = System.Text.Json.JsonDocument.Parse(result.Output);
            if (!doc.RootElement.TryGetProperty("installations", out var installations))
                return tracked;

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
        catch { }

        return tracked;
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

    /// <summary>Installs an SDK channel via <c>dotnetup sdk install</c>.</summary>
    public static Task<ProcessResult> InstallSdkAsync(string channel, CancellationToken ct = default) =>
        ProcessRunner.RunWithCallbackAsync("dotnetup", $"sdk install {channel}", null, null, null, ct);

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
    /// Installs the dotnetup tool itself using the platform-specific bootstrap script.
    /// </summary>
    public static async Task<ProcessResult> InstallDotnetUpAsync(CancellationToken ct = default)
    {
        // Use RunInteractiveAsync on Windows (UseShellExecute=true avoids Access Denied)
        if (OperatingSystem.IsWindows())
        {
            // dotnetup's install script requires pwsh (PowerShell 7+) — it uses
            // RuntimeInformation.OSArchitecture which doesn't exist in Windows PowerShell 5.1
            string shell = ProcessRunner.IsCommandAvailable("pwsh") ? "pwsh" : "powershell.exe";
            int exitCode = await ProcessRunner.RunInteractiveAsync(
                shell,
                "-ExecutionPolicy Bypass -Command \"iwr -UseBasicParsing https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"");
            return new ProcessResult(exitCode, "", "", TimeSpan.Zero);
        }

        var result = await ProcessRunner.RunWithCallbackAsync(
            "bash",
            "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"",
            null, null, null, ct);

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
    /// Adds ~/.dotnetup to the shell profile and current process PATH if not already present.
    /// </summary>
    /// <summary>
    /// Ensures dotnetup and dotnet paths are in the shell profile (.zshrc/.bashrc/fish).
    /// Call after any install that may have added dotnetup or dotnet.
    /// </summary>
    public static void EnsurePathInShellProfile()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string dotnetupDir = Path.Combine(home, ".dotnetup");

        // Collect directories to add
        var dirsToAdd = new List<string>();
        if (Directory.Exists(dotnetupDir)) dirsToAdd.Add(dotnetupDir);

        // Also add dotnet managed installation path
        if (!OperatingSystem.IsWindows())
        {
            string dotnetDir = Path.Combine(home, "Library", "Application Support", "dotnet");
            if (Directory.Exists(dotnetDir)) dirsToAdd.Add(dotnetDir);
            string linuxDotnet = Path.Combine(home, ".dotnet");
            if (Directory.Exists(linuxDotnet)) dirsToAdd.Add(linuxDotnet);
        }

        if (dirsToAdd.Count == 0) return;

        // Add to current process PATH
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in dirsToAdd)
        {
            if (!currentPath.Contains(dir))
                currentPath = $"{dir}:{currentPath}";
        }
        Environment.SetEnvironmentVariable("PATH", currentPath);

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
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dirsToAdd = new List<string>();

        // dotnetup tool
        string dotnetupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnetup");
        if (Directory.Exists(dotnetupDir) && !currentPath.Contains(dotnetupDir))
            dirsToAdd.Add(dotnetupDir);

        // dotnet managed by dotnetup (macOS/Linux)
        if (!OperatingSystem.IsWindows())
        {
            string dotnetDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Application Support", "dotnet");
            if (Directory.Exists(dotnetDir) && !currentPath.Contains(dotnetDir))
                dirsToAdd.Add(dotnetDir);

            // Linux default
            string linuxDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
            if (Directory.Exists(linuxDotnet) && !currentPath.Contains(linuxDotnet))
                dirsToAdd.Add(linuxDotnet);
        }

        if (dirsToAdd.Count > 0)
        {
            string sep = OperatingSystem.IsWindows() ? ";" : ":";
            Environment.SetEnvironmentVariable("PATH", string.Join(sep, dirsToAdd) + sep + currentPath);
        }
    }

    /// <summary>
    /// Resolves an installed version to the dotnetup install spec (channel) that tracks it.
    /// Parses <c>dotnetup list --format Json</c> and finds the best matching spec.
    /// Tries: exact version → feature band pattern (xx) → major.minor → keywords (latest/lts).
    /// Falls back to computing the feature band from the version if nothing matches.
    /// </summary>
    public static async Task<string> ResolveInstallSpecAsync(string version, string component, CancellationToken ct = default)
    {
        try
        {
            var result = await ProcessRunner.RunAsync("dotnetup", "list --format Json", ct: ct);
            if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
                return version;

            var doc = System.Text.Json.JsonDocument.Parse(result.Output);
            if (!doc.RootElement.TryGetProperty("installSpecs", out var specs))
                return version;

            var candidates = new List<(string spec, int priority)>();
            bool hasLatest = false;

            foreach (var spec in specs.EnumerateArray())
            {
                string specComponent = spec.GetProperty("component").GetString() ?? "";
                string specChannel = spec.GetProperty("versionOrChannel").GetString() ?? "";
                if (string.IsNullOrEmpty(specChannel)) continue;

                bool componentMatch = component.Equals("SDK", StringComparison.OrdinalIgnoreCase)
                    ? specComponent.Equals("SDK", StringComparison.OrdinalIgnoreCase)
                    : !specComponent.Equals("SDK", StringComparison.OrdinalIgnoreCase);
                if (!componentMatch) continue;

                // Exact match — best possible
                if (specChannel.Equals(version, StringComparison.OrdinalIgnoreCase))
                    return version;

                // Feature band pattern (e.g. "10.0.2xx" matches "10.0.204")
                if (specChannel.Contains("xx", StringComparison.OrdinalIgnoreCase))
                {
                    string prefix = specChannel.Replace("xx", "", StringComparison.OrdinalIgnoreCase);
                    if (version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        candidates.Add((specChannel, 0)); // highest priority
                }

                // Major.minor match (e.g. "10.0" or "10" matches "10.0.301")
                if (specChannel.Count(c => c == '.') < 2 && !specChannel.Contains("xx"))
                {
                    string prefix = specChannel.EndsWith('.') ? specChannel : specChannel + ".";
                    if (version.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        candidates.Add((specChannel, 1));
                }

                if (specChannel.Equals("latest", StringComparison.OrdinalIgnoreCase)
                    || specChannel.Equals("lts", StringComparison.OrdinalIgnoreCase))
                    hasLatest = true;
            }

            // Prefer most specific match
            if (candidates.Count > 0)
            {
                candidates.Sort((a, b) => a.priority != b.priority
                    ? a.priority.CompareTo(b.priority)
                    : b.spec.Length.CompareTo(a.spec.Length));
                return candidates[0].spec;
            }

            // Fallback: if "latest" is tracked, use it (covers versions installed via latest)
            if (hasLatest)
                return "latest";
        }
        catch { }

        // Last resort: compute the feature band pattern from the version
        return ComputeFeatureBand(version);
    }

    /// <summary>
    /// Computes the feature band pattern from a version string.
    /// E.g. "10.0.301" → "10.0.3xx", "9.0.315" → "9.0.3xx".
    /// Returns the original version if it can't be parsed.
    /// </summary>
    private static string ComputeFeatureBand(string version)
    {
        var parts = version.Split('.');
        if (parts.Length >= 3 && parts[2].Length >= 1 && char.IsDigit(parts[2][0]))
            return $"{parts[0]}.{parts[1]}.{parts[2][0]}xx";
        return version;
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
