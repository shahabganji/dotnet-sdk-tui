using System.Reflection;
using System.Text.Json;

namespace DotnetSdkTui.Services;

/// <summary>
/// Provides the current app version and checks GitHub for newer releases.
/// </summary>
internal static class AppVersion
{
    private const string GitHubApiUrl = "https://api.github.com/repos/shahabganji/dotnet-sdk-tui/releases/latest";
    private static readonly HttpClient Http = new();

    /// <summary>Gets the current version from the assembly.</summary>
    public static string Current { get; } = GetCurrentVersion();

    /// <summary>Latest available version, null if not yet checked or no update.</summary>
    public static string? LatestAvailable { get; private set; }

    /// <summary>True if a newer version is available on GitHub.</summary>
    public static bool UpdateAvailable => LatestAvailable is not null
        && !string.Equals(LatestAvailable, Current, StringComparison.OrdinalIgnoreCase);

    /// <summary>True while the update check is still in flight.</summary>
    public static bool CheckInProgress { get; private set; }


    /// <summary>
    /// Checks GitHub for the latest release. Non-blocking, swallows errors silently.
    /// </summary>
    public static async Task CheckForUpdateAsync()
    {
        CheckInProgress = true;
        try
        {
            Http.DefaultRequestHeaders.UserAgent.TryParseAdd("dsm");
            string json = await Http.GetStringAsync(GitHubApiUrl);
            using var doc = JsonDocument.Parse(json);
            string? tagName = doc.RootElement.GetProperty("tag_name").GetString();
            if (tagName is null) return;

            // Strip leading 'v' from tag (e.g. "v0.2.0" -> "0.2.0")
            string remoteVersion = tagName.StartsWith('v') ? tagName[1..] : tagName;

            if (!string.Equals(remoteVersion, Current, StringComparison.OrdinalIgnoreCase))
                LatestAvailable = remoteVersion;
        }
        catch { /* Network errors, rate limits — silently ignore */ }
        finally { CheckInProgress = false; }
    }

    /// <summary>
    /// Self-updates by running the platform install script.
    /// </summary>
    public static Task<int> SelfUpdateAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            // Use cmd.exe wrapper to avoid "Access is denied" when launching PowerShell directly
            return ProcessRunner.RunInteractiveAsync("cmd.exe",
                "/c powershell.exe -ExecutionPolicy Bypass -Command \"irm https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.ps1 | iex\"");
        }

        return ProcessRunner.RunInteractiveAsync("bash",
            "-c \"curl -fsSL https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.sh | bash\"");
    }

    private static string GetCurrentVersion()
    {
        var version = typeof(AppVersion).Assembly.GetName().Version;
        return version?.ToString(3) ?? "0.1.0";
    }
}
