using System.Text.RegularExpressions;
using DotnetSdkTui.Tests.Helpers;
using Xunit.Abstractions;

namespace DotnetSdkTui.Tests.E2E;

/// <summary>
/// The full user journey on a machine without .NET: install dotnetup from the Setup panel, install
/// a runtime from Search, see it in the Runtimes tab, then uninstall it again — all through the TUI.
/// Installs into the current user's profile, so it only runs with <c>DSM_E2E_FULL=1</c> (CI).
/// </summary>
[Trait("Category", "FullFlow")]
public partial class FullFlowTests(ITestOutputHelper output)
{
    // A GA runtime whose version is not a prefix of any SDK version, so it is the top Search result.
    private const string RuntimeVersion = "10.0.0";

    [FullFlowFact]
    public async Task FreshMachine_InstallsDotnetUp_ThenInstallsAndUninstallsRuntime()
    {
        await using Hex1bTerminal tui = await Hex1bTerminal.LaunchAsync(Hex1bTerminal.FreshMachineEnvironment());
        await tui.WaitForTextAsync("q:Quit", timeoutSec: 90);

        // 1. dotnetup from the Setup panel.
        string setup = await WaitForAnyAsync(tui, ["dotnetup not found", "dotnetup v", "dotnetup installed"], timeoutSec: 60);
        await tui.SaveScreenshotAsync("full-1-start");
        if (setup == "dotnetup not found")
        {
            await tui.SendKeyAsync("S");
            await tui.WaitForTextAsync("i:Install dotnetup");
            await RunCommandAsync(tui, () => tui.SendKeyAsync("I"), "full-2-dotnetup-installed", timeoutSec: 300);
            await tui.SendKeyAsync("Enter");
            await WaitForAnyAsync(tui, ["dotnetup v", "dotnetup installed"], timeoutSec: 60);
            await tui.SendKeyAsync("Escape");
        }

        // 2. Install the runtime from the Search tab.
        await tui.SendKeyAsync("Tab");
        await tui.SendKeyAsync("Tab");
        await tui.WaitForTextAsync("Search:");
        await tui.SendTextAsync(RuntimeVersion);
        await tui.WaitForTextAsync("Latest", timeoutSec: 60);
        await tui.SendKeyAsync("DownArrow");
        string install = await RunCommandAsync(tui, () => tui.SendKeyAsync("I"), "full-3-runtime-installed", timeoutSec: 600);
        Assert.Contains($"runtime install {RuntimeVersion}", install);
        await tui.SendKeyAsync("Enter");

        // 3. The Runtimes tab lists it as installed.
        await tui.SendKeyAsync("Tab");
        await tui.SendKeyAsync("Tab");
        await tui.WaitForTextAsync("Component", timeoutSec: 60);
        int row = await WaitForInstalledRowAsync(tui, present: true, timeoutSec: 60);
        await tui.SaveScreenshotAsync("full-4-runtimes-tab");

        // 4. Uninstall it from the Runtimes tab.
        for (int i = 0; i < row; i++)
            await tui.SendKeyAsync("DownArrow");
        string uninstall = await RunCommandAsync(tui, () => tui.SendKeyAsync("U"), "full-5-runtime-uninstalled", timeoutSec: 300);
        Assert.Contains($"runtime uninstall {RuntimeVersion}", uninstall);
        await tui.SendKeyAsync("Enter");
        await tui.WaitForTextAsync("Component", timeoutSec: 60);
        await WaitForInstalledRowAsync(tui, present: false, timeoutSec: 60);
        await tui.SaveScreenshotAsync("full-6-after-uninstall");

        await tui.SendKeyAsync("Q");
    }

    /// <summary>
    /// Triggers an install/uninstall (dsm leaves the TUI to show its output) and waits for it to
    /// finish. Returns that run's output; fails on a non-zero exit. Earlier runs stay in scrollback,
    /// so only result markers beyond the ones already present count.
    /// </summary>
    private async Task<string> RunCommandAsync(Hex1bTerminal tui, Func<Task> trigger, string screenshot, int timeoutSec)
    {
        int before = CountResults(await tui.CaptureTextAsync(scrollback: Scrollback));
        await trigger();

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        string screen = "";
        while (DateTime.UtcNow < deadline)
        {
            screen = await tui.CaptureTextAsync(scrollback: Scrollback);
            if (CountResults(screen) > before)
            {
                await tui.SaveScreenshotAsync(screenshot);
                string run = screen[Math.Max(0, screen.LastIndexOf("Running:", StringComparison.Ordinal))..];
                output.WriteLine(run);
                Assert.True(run.Contains("Completed successfully", StringComparison.Ordinal), $"Command failed:\n{run}");
                return run;
            }
            await Task.Delay(2000);
        }
        await tui.SaveScreenshotAsync(screenshot + "-timeout");
        throw new Xunit.Sdk.XunitException($"Command did not finish within {timeoutSec}s:\n{screen}");
    }

    private const int Scrollback = 2000;

    private static int CountResults(string screen) =>
        Regex.Count(screen, "Completed successfully|Failed \\(exit code");

    /// <summary>
    /// Waits until the runtime row is (or is no longer) listed as Installed and returns its index
    /// among the visible table rows.
    /// </summary>
    private static async Task<int> WaitForInstalledRowAsync(Hex1bTerminal tui, bool present, int timeoutSec)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        string screen = "";
        while (DateTime.UtcNow < deadline)
        {
            screen = await tui.CaptureTextAsync();
            List<string> rows = screen.Split('\n').Where(l => TableRow().IsMatch(l)).ToList();
            int index = rows.FindIndex(r =>
                Regex.IsMatch(r, $@"│\s*Runtime\s*│\s*{Regex.Escape(RuntimeVersion)}\s*│") && r.Contains("Installed", StringComparison.Ordinal));
            if (present == index >= 0)
                return index;
            await Task.Delay(1000);
        }
        throw new Xunit.Sdk.XunitException(
            $"Runtime {RuntimeVersion} was {(present ? "not" : "still")} listed as installed after {timeoutSec}s:\n{screen}");
    }

    private static async Task<string> WaitForAnyAsync(Hex1bTerminal tui, string[] texts, int timeoutSec)
    {
        var deadline = DateTime.UtcNow.AddSeconds(timeoutSec);
        string screen = "";
        while (DateTime.UtcNow < deadline)
        {
            screen = await tui.CaptureTextAsync();
            if (texts.FirstOrDefault(t => screen.Contains(t, StringComparison.Ordinal)) is { } found)
                return found;
            await Task.Delay(1000);
        }
        throw new Xunit.Sdk.XunitException($"None of [{string.Join(", ", texts)}] appeared within {timeoutSec}s:\n{screen}");
    }

    // A data row of a dsm table: a cell holding a version number.
    [GeneratedRegex(@"│\s*\d+\.\d+\.\d+\S*\s*│")]
    private static partial Regex TableRow();
}
