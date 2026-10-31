using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace DotnetSdkTui.Tests.Helpers;

/// <summary>
/// Drives dsm inside a headless hex1b virtual terminal. Launches the binary named by
/// <c>DSM_E2E_BINARY</c> (e.g. an installed release build) or, when unset, <c>dotnet run</c> on the
/// source project. Each instance gets its own <c>DSM_CONFIG_DIR</c> so user settings never leak in.
/// </summary>
public sealed class Hex1bTerminal : IAsyncDisposable
{
    public const int Width = 160;
    public const int Height = 40;

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly string ArtifactsDir = Path.Combine(RepoRoot, "tests", "DotnetSdkTui.Tests", "test-artifacts");

    private readonly string _configDir = Path.Combine(Path.GetTempPath(), "dsm-e2e-" + Guid.NewGuid().ToString("N")[..8]);
    private string? _id;
    private int? _rootPid;

    /// <summary>Path of the binary under test, or null to run from source.</summary>
    public static string? Binary => Environment.GetEnvironmentVariable("DSM_E2E_BINARY") is { Length: > 0 } b ? b : null;

    /// <summary>
    /// Optional hex1b apphost (<c>dotnet tool install hex1b.tool --tool-path DIR</c>). Required on Windows:
    /// under <c>dotnet hex1b</c> the tool mistakes <c>dotnet.exe</c> for its own apphost and fails to spawn
    /// its terminal host. Unset = <c>dotnet hex1b</c> via the repo's local tool manifest.
    /// </summary>
    private static string? Hex1bExecutable =>
        Environment.GetEnvironmentVariable("DSM_E2E_HEX1B") is { Length: > 0 } h ? h : null;

    /// <summary>Starts dsm. <paramref name="environment"/> overrides the inherited env; a null value removes a variable.</summary>
    public static async Task<Hex1bTerminal> LaunchAsync(
        IReadOnlyDictionary<string, string?>? environment = null, params string[] dsmArgs)
    {
        var terminal = new Hex1bTerminal();
        Directory.CreateDirectory(terminal._configDir);

        var env = new Dictionary<string, string?>(environment ?? new Dictionary<string, string?>())
        {
            ["DSM_CONFIG_DIR"] = terminal._configDir,
        };

        List<string> command = Binary is { } binary
            ? [binary]
            : ["dotnet", "run", "--project", Path.Combine(RepoRoot, "src", "DotnetSdkTui"), "--"];
        command.AddRange(dsmArgs.Length > 0 ? dsmArgs : ["--no-splash"]);

        List<string> args = ["terminal", "start", "--json", "--width", $"{Width}", "--height", $"{Height}", "--"];
        args.AddRange(OperatingSystem.IsWindows() ? WrapInShell(command) : command);

        string json = await RunHex1bAsync(args, env, timeoutSec: 60);
        using var doc = JsonDocument.Parse(json);
        terminal._id = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("hex1b did not return a terminal id");
        if (doc.RootElement.TryGetProperty("pid", out var pid) && int.TryParse(pid.ToString(), out int rootPid))
            terminal._rootPid = rootPid;
        return terminal;
    }

    /// <summary>
    /// ConPTY discards whatever its only client wrote just before exiting (e.g. dsm's goodbye text).
    /// Real users start dsm from a shell that keeps the console alive, so do the same here.
    /// </summary>
    private static string[] WrapInShell(List<string> command)
    {
        string invocation = "& " + string.Join(' ', command.Select(a => "'" + a.Replace("'", "''") + "'"));
        string powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return [powershell, "-NoProfile", "-NonInteractive", "-Command",
            $"{invocation}; $code = $LASTEXITCODE; Start-Sleep -Seconds 3; exit $code"];
    }

    /// <summary>
    /// Environment that makes dsm see a machine with no .NET installed: every PATH entry holding a
    /// <c>dotnet</c> muxer is dropped and DOTNET_ROOT / MSBuild variables injected by <c>dotnet test</c> are cleared.
    /// </summary>
    public static Dictionary<string, string?> FreshMachineEnvironment()
    {
        string muxer = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
        string[] essentials = OperatingSystem.IsWindows() ? [] : ["bash", "curl", "tar"];
        var kept = new List<string>();

        foreach (string dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(Path.Combine(dir, muxer)))
            {
                kept.Add(dir);
                continue;
            }

            if (essentials.FirstOrDefault(tool => File.Exists(Path.Combine(dir, tool))) is { } tool)
                throw new InvalidOperationException(
                    $"'{Path.Combine(dir, muxer)}' shares a directory with '{tool}', so it cannot be hidden from dsm. " +
                    "Remove that dotnet link before running fresh-machine tests.");
        }

        var env = new Dictionary<string, string?> { ["PATH"] = string.Join(Path.PathSeparator, kept) };
        foreach (string name in Environment.GetEnvironmentVariables().Keys.Cast<string>())
        {
            if (name.StartsWith("DOTNET_ROOT", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("MSBuild", StringComparison.OrdinalIgnoreCase)
                || name.Equals("DOTNET_HOST_PATH", StringComparison.OrdinalIgnoreCase))
                env[name] = null;
        }

        // A hex1b apphost still needs to find its runtime; the arch-specific variable takes precedence
        // for apphosts while dsm (and dotnetup) only look at DOTNET_ROOT.
        if (Hex1bExecutable is not null
            && Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host
            && Path.GetDirectoryName(host) is { } privateRoot)
        {
            env[$"DOTNET_ROOT_{RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant()}"] = privateRoot;
        }
        return env;
    }

    public async Task WaitForTextAsync(string text, int timeoutSec = 30)
    {
        if (!await TryAssertAsync("--text-present", text, timeoutSec))
            throw new Xunit.Sdk.XunitException($"Timed out after {timeoutSec}s waiting for \"{text}\".\n{await CaptureTextAsync()}");
    }

    public async Task WaitForTextAbsentAsync(string text, int timeoutSec = 15)
    {
        if (!await TryAssertAsync("--text-absent", text, timeoutSec))
            throw new Xunit.Sdk.XunitException($"\"{text}\" still visible after {timeoutSec}s.\n{await CaptureTextAsync()}");
    }

    public async Task<bool> IsTextVisibleAsync(string text) =>
        (await CaptureTextAsync()).Contains(text, StringComparison.Ordinal);

    public Task SendKeyAsync(string key, bool shift = false, bool ctrl = false)
    {
        List<string> args = ["keys", Id, "--key", key];
        if (shift) args.Add("--shift");
        if (ctrl) args.Add("--ctrl");
        return RunHex1bAsync(args);
    }

    public Task SendTextAsync(string text) => RunHex1bAsync(["keys", Id, "--text", text]);

    public Task<string> CaptureTextAsync(int scrollback = 0) =>
        RunHex1bAsync(scrollback > 0
            ? ["capture", "screenshot", Id, "--scrollback", $"{scrollback}"]
            : ["capture", "screenshot", Id]);

    /// <summary>Saves a text + PNG screenshot under test-artifacts/ (uploaded by CI).</summary>
    public async Task SaveScreenshotAsync(string name)
    {
        Directory.CreateDirectory(ArtifactsDir);
        string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
        string stem = Path.Combine(ArtifactsDir, $"{os}-{name}");
        try
        {
            await File.WriteAllTextAsync(stem + ".txt", await CaptureTextAsync(scrollback: 200));
            await RunHex1bAsync(["capture", "screenshot", Id, "--format", "png", "--output", stem + ".png"]);
        }
        catch (Exception ex)
        {
            await File.WriteAllTextAsync(stem + ".error.txt", ex.ToString());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_id is not null)
        {
            // On Windows `terminal stop` leaves dsm.exe (a grandchild via the shell wrapper) running and
            // locked against uninstall, so kill the hex1b host's whole process tree first.
            if (OperatingSystem.IsWindows() && _rootPid is { } rootPid)
            {
                try { using var root = Process.GetProcessById(rootPid); root.Kill(entireProcessTree: true); } catch { }
            }
            try { await RunHex1bAsync(["terminal", "stop", _id]); } catch { }
            _id = null;
        }
        try { Directory.Delete(_configDir, recursive: true); } catch { }
    }

    private string Id => _id ?? throw new InvalidOperationException("Terminal not started.");

    private async Task<bool> TryAssertAsync(string mode, string text, int timeoutSec)
    {
        try
        {
            await RunHex1bAsync(["assert", Id, mode, text, "--timeout", $"{timeoutSec}"], timeoutSec: timeoutSec + 30);
            return true;
        }
        catch (Hex1bCommandException)
        {
            return false;
        }
    }

    private static async Task<string> RunHex1bAsync(
        IEnumerable<string> arguments, IReadOnlyDictionary<string, string?>? environment = null, int timeoutSec = 60)
    {
        var psi = new ProcessStartInfo
        {
            // `dotnet test` exports DOTNET_HOST_PATH; invoking the muxer by full path keeps hex1b working
            // even when the environment handed to dsm has dotnet stripped from PATH.
            FileName = Hex1bExecutable
                ?? (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host ? host : "dotnet"),
            WorkingDirectory = RepoRoot, // resolves the hex1b local tool from .config/dotnet-tools.json
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        if (Hex1bExecutable is null)
            psi.ArgumentList.Add("hex1b");
        foreach (string arg in arguments)
            psi.ArgumentList.Add(arg);
        if (environment is not null)
        {
            foreach ((string key, string? value) in environment)
            {
                if (value is null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
        }

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start hex1b");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        Task pumps = Task.WhenAll(PumpAsync(process.StandardOutput, stdout), PumpAsync(process.StandardError, stderr));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new Hex1bCommandException($"hex1b {string.Join(' ', arguments)} timed out after {timeoutSec}s");
        }

        // `terminal start` leaves a host process behind. On Windows it inherits our pipe handles, so
        // the streams never reach EOF; don't wait for it, just drain what was written before exit.
        await Task.WhenAny(pumps, Task.Delay(TimeSpan.FromSeconds(2)));
        string output;
        lock (stdout) output = stdout.ToString();
        if (process.ExitCode != 0)
        {
            string error;
            lock (stderr) error = stderr.ToString();
            throw new Hex1bCommandException(
                $"hex1b {string.Join(' ', arguments)} failed (exit {process.ExitCode}): {error}{output}");
        }
        // Only drop the final newline: leading/trailing blank rows matter in screen captures.
        return output.TrimEnd('\r', '\n');
    }

    private static async Task PumpAsync(StreamReader reader, StringBuilder sink)
    {
        var buffer = new char[4096];
        try
        {
            int read;
            while ((read = await reader.ReadAsync(buffer)) > 0)
            {
                lock (sink) sink.Append(buffer, 0, read);
            }
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Stream closed when the Process was disposed.
        }
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, ".config", "dotnet-tools.json")))
                return dir.FullName;
        }
        throw new InvalidOperationException("Could not locate the repository root (.config/dotnet-tools.json).");
    }

    private sealed class Hex1bCommandException(string message) : Exception(message);
}

/// <summary>Runs only when <c>DSM_E2E=1</c> (needs the hex1b tool: <c>dotnet tool restore</c>).</summary>
public sealed class E2EFactAttribute : FactAttribute
{
    public E2EFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DSM_E2E") != "1")
            Skip = "Set DSM_E2E=1 to run hex1b end-to-end tests";
    }
}

/// <summary>
/// Runs only when <c>DSM_E2E_FULL=1</c>. These tests install dotnetup and a real .NET runtime into the
/// current user's profile, so they are meant for disposable CI machines.
/// </summary>
public sealed class FullFlowFactAttribute : FactAttribute
{
    public FullFlowFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("DSM_E2E_FULL") != "1")
            Skip = "Set DSM_E2E_FULL=1 to run tests that install dotnetup and a .NET runtime for the current user";
    }
}
