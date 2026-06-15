using DotnetSdkTui.Views;
using Spectre.Console;

namespace DotnetSdkTui.Tests.Views;

public class SdksViewTests
{
    [Fact]
    public async Task ActivateAsync_DoesNotThrow()
    {
        var view = new SdksView();
        await view.ActivateAsync();

        // Give it a moment to load in background
        await Task.Delay(500);

        Assert.Equal("SDKs", view.Name);
        Assert.Equal(">", view.Icon);
    }

    [Fact]
    public async Task Render_ReturnsRenderable()
    {
        var view = new SdksView();
        await view.ActivateAsync();
        await Task.Delay(1000); // wait for background load

        var renderable = view.Render(true);
        Assert.NotNull(renderable);
    }

    [Fact]
    public async Task HandleKeyAsync_DownArrow_ReturnsHandled()
    {
        var view = new SdksView();
        await view.ActivateAsync();

        // Wait for the background load to finish rather than guessing a fixed delay —
        // navigation keys return NotHandled while loading, which races slow/CI networks.
        for (int i = 0; i < 100 && view.NeedsLiveUpdate; i++)
            await Task.Delay(100);

        var result = await view.HandleKeyAsync(
            new ConsoleKeyInfo('\0', ConsoleKey.DownArrow, false, false, false));

        Assert.Equal(KeyResult.Handled, result);
    }

    [Fact]
    public async Task HandleKeyAsync_UnknownKey_ReturnsNotHandled()
    {
        var view = new SdksView();
        await view.ActivateAsync();
        await Task.Delay(500);

        var result = await view.HandleKeyAsync(
            new ConsoleKeyInfo('x', ConsoleKey.X, false, false, false));

        Assert.Equal(KeyResult.NotHandled, result);
    }

    [Fact]
    public void GetStatusHints_ContainsExpectedKeys()
    {
        var view = new SdksView();
        var hints = view.GetStatusHints();

        Assert.Contains("Navigate", hints);
        Assert.Contains("Refresh", hints);
    }
}
