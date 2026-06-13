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

        // Auto-add dotnetup to PATH on macOS/Linux if not already there
        if (result.ExitCode == 0)
            EnsureDotnetUpOnPath();

        return result;
    }

    /// <summary>
    /// Adds ~/.dotnetup to the shell profile and current process PATH if not already present.
    /// </summary>
    private static void EnsureDotnetUpOnPath()
    {
        string dotnetupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnetup");
        if (!Directory.Exists(dotnetupDir)) return;

        // Add to current process PATH
        string currentPath = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (!currentPath.Contains(dotnetupDir))
        {
            Environment.SetEnvironmentVariable("PATH", $"{dotnetupDir}:{currentPath}");
        }

        // Add to shell profile
        string shell = Path.GetFileName(Environment.GetEnvironmentVariable("SHELL") ?? "/bin/bash");
        string profile = shell switch
        {
            "zsh" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".zshrc"),
            "fish" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "fish", "config.fish"),
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".bashrc")
        };

        if (!File.Exists(profile) || !File.ReadAllText(profile).Contains(dotnetupDir))
        {
            string line = shell == "fish"
                ? $"fish_add_path \"{dotnetupDir}\""
                : $"export PATH=\"{dotnetupDir}:$PATH\"";
            File.AppendAllText(profile, $"\n{line}\n");
            Console.WriteLine($"Added {dotnetupDir} to {profile}");
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

        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int separatorIndex = rawLine.IndexOf(" [", StringComparison.Ordinal);
            int openBracketIndex = rawLine.IndexOf('[', StringComparison.Ordinal);
            int closeBracketIndex = rawLine.LastIndexOf(']');

            if (separatorIndex <= 0 || openBracketIndex < 0 || closeBracketIndex <= openBracketIndex)
            {
                continue;
            }

            string version = rawLine[..separatorIndex].Trim();
            string installRoot = rawLine[(openBracketIndex + 1)..closeBracketIndex].Trim();

            if (version.Length == 0 || installRoot.Length == 0)
            {
                continue;
            }

            installations.Add(new SdkInfo("SDK", version, installRoot, string.Empty));
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

        foreach (string rawLine in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            // Format: "Microsoft.NETCore.App 10.0.0 [/path/to/shared/Microsoft.NETCore.App]"
            int firstSpace = rawLine.IndexOf(' ');
            if (firstSpace <= 0) continue;

            string runtimeName = rawLine[..firstSpace];
            string remaining = rawLine[(firstSpace + 1)..].TrimStart();

            int separatorIndex = remaining.IndexOf(" [", StringComparison.Ordinal);
            int openBracketIndex = remaining.IndexOf('[', StringComparison.Ordinal);
            int closeBracketIndex = remaining.LastIndexOf(']');

            if (separatorIndex <= 0 || openBracketIndex < 0 || closeBracketIndex <= openBracketIndex)
                continue;

            string version = remaining[..separatorIndex].Trim();
            string installRoot = remaining[(openBracketIndex + 1)..closeBracketIndex].Trim();

            if (version.Length == 0 || installRoot.Length == 0)
                continue;

            // Map runtime names to component types
            string component = runtimeName switch
            {
                "Microsoft.NETCore.App" => "Runtime",
                "Microsoft.AspNetCore.App" => "ASP.NET Core",
                "Microsoft.WindowsDesktop.App" => "Windows Desktop",
                _ => runtimeName
            };

            installations.Add(new SdkInfo(component, version, installRoot, string.Empty));
        }

        return installations;
    }
}
