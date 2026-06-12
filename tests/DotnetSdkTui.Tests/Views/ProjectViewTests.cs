using DotnetSdkTui.Views;

namespace DotnetSdkTui.Tests.Views;

public class ProjectViewTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectViewTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tui-pv-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Name_ReturnsProject()
    {
        var view = new ProjectView();
        Assert.Equal("Project", view.Name);
    }

    [Fact]
    public async Task ActivateAsync_WithNoProject_RendersMuted()
    {
        var savedDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_tempDir);
            var view = new ProjectView();
            await view.ActivateAsync();

            var hints = view.GetStatusHints();
            Assert.Contains("No project", hints);
        }
        finally
        {
            Directory.SetCurrentDirectory(savedDir);
        }
    }

    [Fact]
    public async Task ActivateAsync_WithCsproj_DetectsProject()
    {
        File.WriteAllText(Path.Combine(_tempDir, "Test.csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>");

        var savedDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_tempDir);
            var view = new ProjectView();
            await view.ActivateAsync();

            var hints = view.GetStatusHints();
            Assert.Contains("Build", hints);
        }
        finally
        {
            Directory.SetCurrentDirectory(savedDir);
        }
    }

    [Fact]
    public async Task HandleKeyAsync_NoProject_ReturnsNotHandled()
    {
        var savedDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_tempDir);
            var view = new ProjectView();
            await view.ActivateAsync();

            var result = await view.HandleKeyAsync(
                new ConsoleKeyInfo('b', ConsoleKey.B, false, false, false));

            Assert.Equal(KeyResult.NotHandled, result);
        }
        finally
        {
            Directory.SetCurrentDirectory(savedDir);
        }
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
