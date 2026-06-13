using DotnetSdkTui.Views;

namespace DotnetSdkTui.Tests.Views;

public class SetupViewTests
{
    [Fact]
    public void Name_ReturnsSetup()
    {
        var view = new SetupView();
        Assert.Equal("Setup", view.Name);
    }

    [Fact]
    public async Task ActivateAsync_DoesNotThrow()
    {
        var view = new SetupView();
        await view.ActivateAsync();
        await Task.Delay(500);

        var renderable = view.Render(true);
        Assert.NotNull(renderable);
    }

    [Fact]
    public async Task HandleKeyAsync_Refresh_ReturnsHandled()
    {
        var view = new SetupView();
        await view.ActivateAsync();
        await Task.Delay(500);

        var result = await view.HandleKeyAsync(
            new ConsoleKeyInfo('r', ConsoleKey.R, false, false, false));

        Assert.Equal(KeyResult.Handled, result);
    }

    [Fact]
    public async Task HandleKeyAsync_UnknownKey_ReturnsNotHandled()
    {
        var view = new SetupView();
        await view.ActivateAsync();
        await Task.Delay(500);

        var result = await view.HandleKeyAsync(
            new ConsoleKeyInfo('z', ConsoleKey.Z, false, false, false));

        Assert.Equal(KeyResult.NotHandled, result);
    }

    [Fact]
    public void GetStatusHints_ContainsRefresh()
    {
        var view = new SetupView();
        var hints = view.GetStatusHints();
        Assert.Contains("Refresh", hints);
    }
}
