using System.Diagnostics;

namespace DotnetSdkTui.Tests.Integration;

[Trait("Category", "Integration")]
public class AppStartupTests
{
    private static readonly string ProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DotnetSdkTui"));

    [Fact]
    public async Task App_StartsAndCanBeKilled()
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run --project \"{ProjectPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment = { ["TERM"] = "dumb" }
            }
        };

        Assert.True(process.Start(), "App should start successfully");

        // Give it time to initialize
        await Task.Delay(3000);

        // App should still be running (it's a TUI loop)
        Assert.False(process.HasExited, "App should still be running after startup");

        // Kill it
        process.Kill(entireProcessTree: true);
        await process.WaitForExitAsync();
    }

    [Fact]
    public void ProjectPath_Exists()
    {
        Assert.True(Directory.Exists(ProjectPath),
            $"Project path should exist: {ProjectPath}");
    }

    [Fact]
    public void CsprojFile_Exists()
    {
        var csproj = Path.Combine(ProjectPath, "DotnetSdkTui.csproj");
        Assert.True(File.Exists(csproj), "DotnetSdkTui.csproj should exist");
    }
}
