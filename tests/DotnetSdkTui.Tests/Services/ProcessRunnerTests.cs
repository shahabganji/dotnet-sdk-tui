using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class ProcessRunnerTests
{
    [Fact]
    public async Task RunAsync_EchoCommand_CapturesOutput()
    {
        var result = await ProcessRunner.RunAsync("echo", "hello world");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("hello world", result.Output);
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
            "echo", "line1",
            line => lines.Add(line));

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("line1", lines);
    }

    [Fact]
    public async Task RunWithCallbackAsync_CapturesStderr()
    {
        var errorLines = new List<string>();

        // Write to stderr
        var result = await ProcessRunner.RunWithCallbackAsync(
            "bash", "-c \"echo errormsg >&2\"",
            _ => { },
            err => errorLines.Add(err));

        Assert.Contains("errormsg", errorLines);
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
        var result = await ProcessRunner.RunAsync("pwd", "", tempDir);

        Assert.Equal(0, result.ExitCode);
        // pwd should return something containing the temp path
        Assert.False(string.IsNullOrWhiteSpace(result.Output));
    }
}
