using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class ProjectDetectorTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectDetectorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tui-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void Detect_EmptyDirectory_ReturnsEmpty()
    {
        var result = ProjectDetector.Detect(_tempDir);
        Assert.Empty(result);
    }

    [Fact]
    public void Detect_WithCsproj_FindsProject()
    {
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), "<Project/>");

        var result = ProjectDetector.Detect(_tempDir);

        Assert.Single(result);
        Assert.Equal("App.csproj", result[0].FileName);
        Assert.Equal(Models.ProjectType.CSharpProject, result[0].ProjectType);
    }

    [Fact]
    public void Detect_WithSln_FindsSolution()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MySolution.sln"), "");

        var result = ProjectDetector.Detect(_tempDir);

        Assert.Single(result);
        Assert.Equal("MySolution.sln", result[0].FileName);
        Assert.Equal(Models.ProjectType.Solution, result[0].ProjectType);
    }

    [Fact]
    public void Detect_WithSlnx_FindsSolutionX()
    {
        File.WriteAllText(Path.Combine(_tempDir, "MySolution.slnx"), "");

        var result = ProjectDetector.Detect(_tempDir);

        Assert.Single(result);
        Assert.Equal(Models.ProjectType.SolutionX, result[0].ProjectType);
    }

    [Fact]
    public void Detect_MixedFiles_PrioritizesSolutionFirst()
    {
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), "<Project/>");
        File.WriteAllText(Path.Combine(_tempDir, "App.sln"), "");
        File.WriteAllText(Path.Combine(_tempDir, "App.slnx"), "");

        var result = ProjectDetector.Detect(_tempDir);

        Assert.Equal(3, result.Count);
        Assert.Equal(Models.ProjectType.Solution, result[0].ProjectType);
        Assert.Equal(Models.ProjectType.SolutionX, result[1].ProjectType);
        Assert.Equal(Models.ProjectType.CSharpProject, result[2].ProjectType);
    }

    [Fact]
    public void HasProject_WithProject_ReturnsTrue()
    {
        File.WriteAllText(Path.Combine(_tempDir, "App.csproj"), "<Project/>");
        Assert.True(ProjectDetector.HasProject(_tempDir));
    }

    [Fact]
    public void HasProject_EmptyDir_ReturnsFalse()
    {
        Assert.False(ProjectDetector.HasProject(_tempDir));
    }

    [Fact]
    public void GetDotnetArgument_CSharpProject_ReturnsProjectFlag()
    {
        var project = new Models.ProjectInfo("/path/to/App.csproj", Models.ProjectType.CSharpProject);
        var arg = ProjectDetector.GetDotnetArgument(project);
        Assert.Equal("--project /path/to/App.csproj", arg);
    }

    [Fact]
    public void GetDotnetArgument_Solution_ReturnsPath()
    {
        var project = new Models.ProjectInfo("/path/to/App.sln", Models.ProjectType.Solution);
        var arg = ProjectDetector.GetDotnetArgument(project);
        Assert.Equal("/path/to/App.sln", arg);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }
}
