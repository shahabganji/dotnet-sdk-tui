using DotnetSdkTui.Models;

namespace DotnetSdkTui.Services;

public static class DotnetUpService
{
    public static bool IsInstalled() => ProcessRunner.IsCommandAvailable("dotnetup");

    public static Task<DotnetUpInfo?> GetInfoAsync(CancellationToken ct = default) =>
        ProcessRunner.RunJsonAsync("dotnetup", "--info --json", AppJsonContext.Default.DotnetUpInfo, ct: ct);

    public static async Task<List<SdkInfo>> ListInstalledAsync(CancellationToken ct = default)
    {
        if (IsInstalled())
        {
            SdkListResponse? response = await ProcessRunner.RunJsonAsync(
                "dotnetup",
                "list --json",
                AppJsonContext.Default.SdkListResponse,
                ct: ct);

            return response?.Installations ?? [];
        }

        ProcessResult result = await ProcessRunner.RunAsync("dotnet", "--list-sdks", ct: ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            return [];
        }

        return ParseDotnetSdkList(result.Output);
    }

    public static Task<ProcessResult> InstallSdkAsync(string channel, CancellationToken ct = default) =>
        ProcessRunner.RunWithLiveOutputAsync("dotnetup", $"sdk install {channel}", ct: ct);

    public static Task<ProcessResult> UninstallSdkAsync(string channel, CancellationToken ct = default) =>
        ProcessRunner.RunWithLiveOutputAsync("dotnetup", $"sdk uninstall {channel}", ct: ct);

    public static Task<ProcessResult> UpdateAllAsync(CancellationToken ct = default) =>
        ProcessRunner.RunWithLiveOutputAsync("dotnetup", "update", ct: ct);

    public static Task<ProcessResult> UpdateSdksAsync(CancellationToken ct = default) =>
        ProcessRunner.RunWithLiveOutputAsync("dotnetup", "sdk update", ct: ct);

    public static Task<ProcessResult> InstallDotnetUpAsync(CancellationToken ct = default)
    {
        if (OperatingSystem.IsWindows())
        {
            return ProcessRunner.RunWithLiveOutputAsync(
                "powershell",
                "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"",
                ct: ct);
        }

        return ProcessRunner.RunWithLiveOutputAsync(
            "bash",
            "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"",
            ct: ct);
    }

    public static Task<ProcessResult> RunDotnetCommandAsync(string command, string? projectPath = null, CancellationToken ct = default)
    {
        string? workingDirectory = ResolveWorkingDirectory(projectPath);

        return IsInstalled()
            ? ProcessRunner.RunWithLiveOutputAsync("dotnetup", $"dotnet {command}", workingDirectory, ct)
            : ProcessRunner.RunWithLiveOutputAsync("dotnet", command, workingDirectory, ct);
    }

    private static List<SdkInfo> ParseDotnetSdkList(string output)
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

            installations.Add(new SdkInfo("sdk", version, installRoot, string.Empty));
        }

        return installations;
    }

    private static string? ResolveWorkingDirectory(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            return null;
        }

        if (Directory.Exists(projectPath))
        {
            return projectPath;
        }

        return Path.GetDirectoryName(projectPath);
    }
}
