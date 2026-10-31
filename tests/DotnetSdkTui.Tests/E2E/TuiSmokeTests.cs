using System.Text.RegularExpressions;
using DotnetSdkTui.Tests.Helpers;

namespace DotnetSdkTui.Tests.E2E;

/// <summary>
/// Read-only checks of the running TUI (nothing gets installed). Point <c>DSM_E2E_BINARY</c> at a
/// published/installed dsm to verify a release build on the current OS.
/// </summary>
[Trait("Category", "E2E")]
public class TuiSmokeTests
{
    [E2EFact]
    public async Task MainScreen_RendersHeaderTabsAndSetupPanel()
    {
        await using Hex1bTerminal tui = await Hex1bTerminal.LaunchAsync();

        await tui.WaitForTextAsync("Runtimes", timeoutSec: 90);
        await tui.WaitForTextAsync("Setup");
        await tui.WaitForTextAsync("q:Quit");
        string expected = Environment.GetEnvironmentVariable("DSM_E2E_EXPECTED_VERSION") is { Length: > 0 } v
            ? $".NET SDK Manager v{v}"
            : ".NET SDK Manager v";
        await tui.WaitForTextAsync(expected);
        await tui.SaveScreenshotAsync("main-screen");
    }

    [E2EFact]
    public async Task Tab_CyclesToRuntimesAndSearch_AndSearchReturnsResults()
    {
        await using Hex1bTerminal tui = await Hex1bTerminal.LaunchAsync();
        await tui.WaitForTextAsync("q:Quit", timeoutSec: 90);

        await tui.SendKeyAsync("Tab");
        await tui.WaitForTextAsync("Component", timeoutSec: 60);
        await tui.SaveScreenshotAsync("runtimes-tab");

        await tui.SendKeyAsync("Tab");
        await tui.WaitForTextAsync("Search:");
        await tui.SendTextAsync("10.0");
        // The results table header only renders once the release metadata was fetched and parsed.
        await tui.WaitForTextAsync("Latest", timeoutSec: 60);
        await tui.WaitForTextAsync("10.0.");
        await tui.SaveScreenshotAsync("search-tab");
    }

    [E2EFact]
    public async Task F6_CyclesTheme()
    {
        await using Hex1bTerminal tui = await Hex1bTerminal.LaunchAsync();
        await tui.WaitForTextAsync("Theme(", timeoutSec: 90);
        string current = Regex.Match(await tui.CaptureTextAsync(), @"Theme\((\w+)\)").Groups[1].Value;
        Assert.NotEmpty(current);

        await tui.SendKeyAsync("F6");

        await tui.WaitForTextAbsentAsync($"Theme({current})");
        await tui.WaitForTextAsync("Theme(");
    }

    [E2EFact]
    public async Task Q_QuitsAndPrintsGoodbye()
    {
        await using Hex1bTerminal tui = await Hex1bTerminal.LaunchAsync();
        await tui.WaitForTextAsync("q:Quit", timeoutSec: 90);

        await tui.SendKeyAsync("Q");

        // The goodbye banner is written after the TUI clears the screen, so it may sit in scrollback.
        var deadline = DateTime.UtcNow.AddSeconds(20);
        string screen = "";
        while (DateTime.UtcNow < deadline)
        {
            screen = await tui.CaptureTextAsync(scrollback: 200);
            if (screen.Contains("Thanks for using", StringComparison.Ordinal))
                return;
            await Task.Delay(500);
        }
        Assert.Fail($"Goodbye message not shown after quitting.\n{screen}");
    }
}
