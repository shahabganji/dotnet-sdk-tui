using System.Diagnostics;
using System.Text.Json;

namespace DotnetSdkTui.Tests.Helpers;

public class Hex1bFixture : IAsyncDisposable
{
    private string? _terminalId;
    private static readonly string AppProjectPath = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "DotnetSdkTui"));

    public string? TerminalId => _terminalId;

    public async Task LaunchAppAsync(string? workingDir = null, int timeoutSec = 30)
    {
        var cwdArg = workingDir is null ? string.Empty : $" --cwd \"{EscapeArg(workingDir)}\"";
        var output = await RunHex1bCommandAsync(
            $"terminal start --json --width 120 --height 40{cwdArg} -- dotnet run --project \"{EscapeArg(AppProjectPath)}\"",
            timeoutSec);

        using var doc = JsonDocument.Parse(output);
        _terminalId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("Failed to get terminal ID");
    }

    public async Task AssertTextPresentAsync(string text, int timeoutSec = 15)
    {
        EnsureTerminal();
        await RunHex1bCommandAsync($"assert {_terminalId} --text-present \"{EscapeArg(text)}\" --timeout {timeoutSec}", timeoutSec + 5);
    }

    public async Task AssertTextAbsentAsync(string text, int timeoutSec = 10)
    {
        EnsureTerminal();
        await RunHex1bCommandAsync($"assert {_terminalId} --text-absent \"{EscapeArg(text)}\" --timeout {timeoutSec}", timeoutSec + 5);
    }

    public async Task SendKeyAsync(string key)
    {
        EnsureTerminal();
        await RunHex1bCommandAsync($"keys {_terminalId} --key {key}");
    }

    public async Task SendTextAsync(string text)
    {
        EnsureTerminal();
        await RunHex1bCommandAsync($"keys {_terminalId} --text \"{EscapeArg(text)}\"");
    }

    public async Task<string> CaptureScreenAsync()
    {
        EnsureTerminal();
        return await RunHex1bCommandAsync($"capture screenshot {_terminalId}");
    }

    public async Task CaptureScreenshotAsync(string outputPath)
    {
        EnsureTerminal();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
        await RunHex1bCommandAsync($"capture screenshot {_terminalId} --format png --output \"{EscapeArg(outputPath)}\"");
    }

    public async ValueTask DisposeAsync()
    {
        if (_terminalId is null)
        {
            return;
        }

        try
        {
            await RunHex1bCommandAsync($"terminal stop {_terminalId}");
        }
        catch
        {
        }
        finally
        {
            _terminalId = null;
        }
    }

    private void EnsureTerminal()
    {
        if (_terminalId is null)
        {
            throw new InvalidOperationException("Terminal not started. Call LaunchAppAsync first.");
        }
    }

    private static string EscapeArg(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static async Task<string> RunHex1bCommandAsync(string arguments, int timeoutSec = 30)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"hex1b {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        var outputTask = process.StandardOutput.ReadToEndAsync(cts.Token);
        var errorTask = process.StandardError.ReadToEndAsync(cts.Token);
        await process.WaitForExitAsync(cts.Token);

        var output = await outputTask;
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"hex1b command failed (exit {process.ExitCode}): {error}");
        }

        return output.Trim();
    }
}
