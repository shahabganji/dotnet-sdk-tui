using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Spectre.Console;

namespace DotnetSdkTui.Services;

/// <summary>
/// Encapsulates the result of a completed process execution.
/// </summary>
/// <param name="ExitCode">Process exit code (0 = success).</param>
/// <param name="Output">Captured standard output.</param>
/// <param name="Error">Captured standard error.</param>
/// <param name="Duration">Wall-clock duration of the process.</param>
public record ProcessResult(int ExitCode, string Output, string Error, TimeSpan Duration);

/// <summary>
/// Provides methods for running external processes with buffered, live, or callback-based output capture.
/// </summary>
public static class ProcessRunner
{
    /// <summary>
    /// Runs a process and captures all output in memory (buffered).
    /// </summary>
    public static async Task<ProcessResult> RunAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(command, arguments, workingDirectory) };
        var stopwatch = Stopwatch.StartNew();

        try
        {
            if (!process.Start())
            {
                stopwatch.Stop();
                return new ProcessResult(-1, string.Empty, $"Failed to start process '{command}'.", stopwatch.Elapsed);
            }

            using var registration = RegisterCancellation(process, ct);
            Task<string> outputTask = process.StandardOutput.ReadToEndAsync(ct);
            Task<string> errorTask = process.StandardError.ReadToEndAsync(ct);

            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync(ct));

            stopwatch.Stop();
            return new ProcessResult(process.ExitCode, await outputTask, await errorTask, stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            ThrowIfCancellationRequested(process, ct);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ProcessResult(-1, string.Empty, ex.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Runs a process and streams output directly to <see cref="AnsiConsole"/>.
    /// Useful for operations where output should appear in the terminal immediately.
    /// </summary>
    public static async Task<ProcessResult> RunWithLiveOutputAsync(string command, string arguments, string? workingDirectory = null, CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(command, arguments, workingDirectory) };
        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputCompleted.TrySetResult();
                return;
            }

            output.AppendLine(e.Data);
            AnsiConsole.WriteLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorCompleted.TrySetResult();
                return;
            }

            error.AppendLine(e.Data);
            AnsiConsole.MarkupLine($"[red]{Markup.Escape(e.Data)}[/]");
        };

        try
        {
            if (!process.Start())
            {
                stopwatch.Stop();
                return new ProcessResult(-1, string.Empty, $"Failed to start process '{command}'.", stopwatch.Elapsed);
            }

            using var registration = RegisterCancellation(process, ct);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            await Task.WhenAll(outputCompleted.Task, errorCompleted.Task);

            stopwatch.Stop();
            return new ProcessResult(process.ExitCode, output.ToString(), error.ToString(), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            ThrowIfCancellationRequested(process, ct);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ProcessResult(-1, output.ToString(), ex.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Runs a process and invokes callbacks for each line of stdout and stderr.
    /// Used by the TUI to capture output into in-app panels.
    /// </summary>
    public static async Task<ProcessResult> RunWithCallbackAsync(
        string command, string arguments,
        Action<string>? onOutput, Action<string>? onError = null,
        string? workingDirectory = null, CancellationToken ct = default)
    {
        using var process = new Process { StartInfo = CreateStartInfo(command, arguments, workingDirectory) };
        var stopwatch = Stopwatch.StartNew();
        var output = new StringBuilder();
        var error = new StringBuilder();
        var outputCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var errorCompleted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                outputCompleted.TrySetResult();
                return;
            }

            output.AppendLine(e.Data);
            onOutput?.Invoke(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                errorCompleted.TrySetResult();
                return;
            }

            error.AppendLine(e.Data);
            onError?.Invoke(e.Data);
        };

        try
        {
            if (!process.Start())
            {
                stopwatch.Stop();
                return new ProcessResult(-1, string.Empty, $"Failed to start process '{command}'.", stopwatch.Elapsed);
            }

            using var registration = RegisterCancellation(process, ct);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync(ct);
            await Task.WhenAll(outputCompleted.Task, errorCompleted.Task);

            stopwatch.Stop();
            return new ProcessResult(process.ExitCode, output.ToString(), error.ToString(), stopwatch.Elapsed);
        }
        catch (OperationCanceledException)
        {
            ThrowIfCancellationRequested(process, ct);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return new ProcessResult(-1, output.ToString(), ex.Message, stopwatch.Elapsed);
        }
    }

    /// <summary>
    /// Runs a process, captures its output, and deserializes it as JSON.
    /// Returns <c>default</c> on non-zero exit code or deserialization failure.
    /// </summary>
    public static async Task<T?> RunJsonAsync<T>(string command, string arguments, JsonTypeInfo<T> jsonTypeInfo, string? workingDirectory = null, CancellationToken ct = default)
    {
        ProcessResult result = await RunAsync(command, arguments, workingDirectory, ct);
        if (result.ExitCode != 0 || string.IsNullOrWhiteSpace(result.Output))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize(result.Output, jsonTypeInfo);
        }
        catch (JsonException)
        {
            return default;
        }
        catch (NotSupportedException)
        {
            return default;
        }
    }

    /// <summary>
    /// Checks whether a command is available on the system PATH.
    /// Uses <c>where</c> on Windows or <c>which</c> on Unix.
    /// </summary>
    public static bool IsCommandAvailable(string command)
    {
        string lookupCommand = OperatingSystem.IsWindows() ? "where" : "which";

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = lookupCommand,
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        try
        {
            return process.Start() && process.WaitForExit(5000) && process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Runs a process without redirecting stdout/stderr so the child process
    /// writes directly to the terminal. Used for streaming install output.
    /// </summary>
    public static async Task<int> RunInteractiveAsync(string command, string arguments, string? workingDirectory = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            UseShellExecute = false
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            psi.WorkingDirectory = workingDirectory;

        using var process = new Process { StartInfo = psi };
        if (!process.Start())
            return -1;

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static ProcessStartInfo CreateStartInfo(string command, string arguments, string? workingDirectory)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        return startInfo;
    }

    private static CancellationTokenRegistration RegisterCancellation(Process process, CancellationToken ct)
    {
        if (!ct.CanBeCanceled)
        {
            return default;
        }

        return ct.Register(() =>
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
        });
    }

    private static void ThrowIfCancellationRequested(Process process, CancellationToken ct)
    {
        if (!ct.IsCancellationRequested)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
        }

        ct.ThrowIfCancellationRequested();
    }
}
