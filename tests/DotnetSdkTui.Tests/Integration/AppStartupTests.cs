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
                Arguments = $"run --project \"{ProjectPath}\" -- --no-splash",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Environment =
                {
                    ["TERM"] = "xterm-256color",
                    ["COLUMNS"] = "120",
                    ["LINES"] = "40"
                }
            }
        };

        Assert.True(process.Start(), "App should start successfully");

        // Wait for dotnet run compilation + app startup
        await Task.Delay(15000);

        // App should still be running (it's a TUI loop)
        string stderr = process.HasExited ? await process.StandardError.ReadToEndAsync() : "";
        string stdout = process.HasExited ? await process.StandardOutput.ReadToEndAsync() : "";
        Assert.False(process.HasExited, $"App exited with code {(process.HasExited ? process.ExitCode : -1)}. stderr: {stderr[..Math.Min(500, stderr.Length)]}. stdout: {stdout[..Math.Min(200, stdout.Length)]}");

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
