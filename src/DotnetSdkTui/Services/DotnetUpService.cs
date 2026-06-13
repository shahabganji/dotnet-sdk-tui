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
    public static Task<ProcessResult> InstallDotnetUpAsync(CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProcessRunner.RunWithCallbackAsync(
                "powershell",
                "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"",
                null, null, null, ct);
        }

        return ProcessRunner.RunWithCallbackAsync(
            "bash",
            "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"",
            null, null, null, ct);
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
