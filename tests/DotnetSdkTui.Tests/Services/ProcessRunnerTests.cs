using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_DotnetVersion_CapturesOutput()
    {
        var result = await ProcessRunner.RunAsync("dotnet", "--version");

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
        Assert.True(result.Duration.TotalMilliseconds >= 0);
    }

    [Fact]
    public async Task RunAsync_InvalidCommand_ReturnsError()
    {
        var result = await ProcessRunner.RunAsync("nonexistent-command-xyz", "");

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task RunWithCallbackAsync_CapturesLinesViaCallback()
    {
        var lines = new List<string>();

        var result = await ProcessRunner.RunWithCallbackAsync(
            "dotnet", "--version",
            line => lines.Add(line));

        Assert.Equal(0, result.ExitCode);
        Assert.True(lines.Count > 0);
    }

    [Fact]
    public async Task RunWithCallbackAsync_CapturesStderr()
    {
        var errorLines = new List<string>();

        // dotnet with invalid arg writes to stderr
        var result = await ProcessRunner.RunWithCallbackAsync(
            "dotnet", "--invalid-flag-xyz",
            _ => { },
            err => errorLines.Add(err));

        Assert.NotEqual(0, result.ExitCode);
        Assert.True(errorLines.Count > 0);
    }

    [Fact]
    public void IsCommandAvailable_DotnetExists()
    {
        Assert.True(ProcessRunner.IsCommandAvailable("dotnet"));
    }

    [Fact]
    public void IsCommandAvailable_FakeCommand_ReturnsFalse()
    {
        Assert.False(ProcessRunner.IsCommandAvailable("nonexistent-cmd-xyz-123"));
    }

    [Fact]
    public async Task RunAsync_WithWorkingDirectory_UsesIt()
    {
        var tempDir = Path.GetTempPath();
        var result = await ProcessRunner.RunAsync("dotnet", "--version", tempDir);

        Assert.Equal(0, result.ExitCode);
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }
}
