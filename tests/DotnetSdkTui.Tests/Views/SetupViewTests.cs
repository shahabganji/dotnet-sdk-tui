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

    // --- Install-folder diagnostics ---

    private const string AppSupport = "/Users/jane/Library/Application Support/dotnet";
    private const string DotDotnet = "/Users/jane/.dotnet";

    [Fact]
    public void BuildRootLineModel_ActiveRootManaged_SingleCalmLine()
    {
        var lines = SetupView.BuildRootLineModel(AppSupport, [AppSupport], rememberedRoot: null);

        var line = Assert.Single(lines);
        Assert.Equal(AppSupport, line.Path);
        Assert.False(line.Warn);
    }

    [Fact]
    public void BuildRootLineModel_ActiveRootDiffersFromTracked_WarnsWithBothFolders()
    {
        var lines = SetupView.BuildRootLineModel(DotDotnet, [AppSupport], rememberedRoot: null);

        Assert.Equal([DotDotnet, AppSupport], lines.Select(l => l.Path));
        Assert.All(lines, l => Assert.True(l.Warn));
    }

    [Fact]
    public void BuildRootLineModel_RememberedRoot_AddsForgetHint()
    {
        var lines = SetupView.BuildRootLineModel(AppSupport, [AppSupport], rememberedRoot: AppSupport);

        Assert.Equal(2, lines.Count);
        Assert.Contains("f:forget", lines[1].Suffix);
    }

    [Fact]
    public void FitPath_KeepsDistinctiveTail()
    {
        Assert.Equal("…Support/dotnet", SetupView.FitPath("~/Library/Application Support/dotnet", 15));
        Assert.Equal("~/.dotnet", SetupView.FitPath("~/.dotnet", 15));
    }
}
