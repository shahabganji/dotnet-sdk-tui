using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Views;
using DotnetSdkTui.Theme;
using DotnetSdkTui.Services;

namespace DotnetSdkTui;

/// <summary>
/// Main application with two screens:
/// - Main: dotnetup status + SDKs panel + Runtimes panel
/// - Search: full-screen search (activated by "/", Esc returns)
/// Install/uninstall operations exit TUI to show real terminal output.
/// </summary>
public sealed class App
{
    private readonly SdksView _sdksView;
    private readonly RuntimesView _runtimesView;
    private readonly SearchView _searchView;
    private readonly SetupView _setupView;
    private readonly BrewView _brewView;

    private enum Screen { Main, Search, Brew }
    private Screen _screen = Screen.Main;

    // Focus on main screen: 0=SDKs, 1=Runtimes, 2=Setup
    private int _mainFocus;
    private const int FocusSdks = 0;
    private const int FocusRuntimes = 1;
    private const int FocusSetup = 2;

    private bool _running = true;
    private string _dotnetUpStatus = "checking...";
    private string? _setupInfo;
    private readonly bool _skipSplash;

    // Last terminal size observed at render time; the main loop re-renders when this changes
    // so resizing the window doesn't leave stale geometry on the screen.
    private int _lastWidth;
    private int _lastHeight;
    private volatile bool _resizeSignaled;
    private IDisposable? _winchRegistration;

    // Per-frame back-buffer + last-committed frame. The render path builds the new frame in
    // _frameWriter via a private Spectre console, prefixes/suffixes it with sync-output
    // escapes and resize-strip wipes, and writes the whole thing in a single Console.Write.
    // If the resulting frame is byte-identical to the previous one we skip the syscall.
    private readonly System.IO.StringWriter _frameWriter = new();
    private IAnsiConsole? _renderConsole;
    private string? _lastFrame;

    // Set when the user requests bulk migration (Shift+M); handled by CheckBulkMigrateAsync.
    private IReadOnlyList<SdksView.SdkMigration>? _pendingBulkMigrate;

    public App(bool skipSplash = false)
    {
        _skipSplash = skipSplash;
        _sdksView = new SdksView();
        _runtimesView = new RuntimesView();
        _searchView = new SearchView();
        _setupView = new SetupView();
        _brewView = new BrewView();
    }

    public async Task RunAsync()
    {
        // Ensure dotnet and dotnetup are on PATH (covers dotnetup-managed installs)
        DotnetUpService.RefreshPath();

        // Ensure UTF-8 output for box-drawing characters on Windows
        if (OperatingSystem.IsWindows())
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        }

        try { Console.CursorVisible = false; } catch (IOException) { }

        // Ensure Spectre.Console has valid dimensions (Layout crashes with 0 height in redirected terminals)
        try { _ = Console.WindowHeight; }
        catch
        {
            AnsiConsole.Profile.Width = 120;
            AnsiConsole.Profile.Height = 40;
        }

        ThemeManager.Restore();

        // Graceful Ctrl+C: stop the loop instead of killing the process
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        // Re-render automatically when the terminal is resized. On macOS/Linux the kernel
        // delivers SIGWINCH instantly; on Windows we fall back to size polling in the main loop.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                _winchRegistration = System.Runtime.InteropServices.PosixSignalRegistration.Create(
                    System.Runtime.InteropServices.PosixSignal.SIGWINCH,
                    _ => _resizeSignaled = true);
            }
            catch { /* signal hookup is a best-effort optimisation */ }
        }

        // Kick off the data loads BEFORE the splash so that by the time the splash
        // animation finishes (~3 s), the SDK / Runtime / Setup data is already cached.
        // ActivateAsync is fire-and-forget — the splash continues to animate normally
        // because Spectre.Console.Live runs on the Console thread and the loads run
        // on the thread pool.
        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        _ = LoadSetupInfoAsync();
        _ = AppVersion.CheckForUpdateAsync();
        var prefetch = Task.WhenAll(
            _sdksView.ActivateAsync(),
            _runtimesView.ActivateAsync(),
            _setupView.ActivateAsync());

        if (!_skipSplash)
            await Ui.RenderSplashAsync();

        // Make sure the prefetch is observed (it almost always completed during the splash).
        await prefetch;

        AnsiConsole.Clear();

        while (_running)
        {
            // Resize handling — k9s-style. Render each frame to a private in-memory buffer,
            // wrap it with the synchronized-output escape (DEC 2026) plus any pre-/post-frame
            // erases, and emit the whole thing in ONE Console.Write so the terminal sees the
            // new frame as a single atomic update — no row-by-row paint. If the resulting
            // frame is byte-identical to the previous one we skip the write entirely.
            int width = SafeWidth();
            int height = SafeHeight();
            bool resized = _resizeSignaled || width != _lastWidth || height != _lastHeight;
            if (resized) _resizeSignaled = false;

            EmitFrame(BuildScreen(), width, height, resized);

            // Check for pending interactive commands from views
            if (await CheckPendingCommandsAsync())
            {
                AnsiConsole.Clear();
                _lastFrame = null;          // force a real write on the first frame after the prompt
                continue;
            }

            // Check for a pending bulk-migration request (Shift+M)
            if (await CheckBulkMigrateAsync())
            {
                AnsiConsole.Clear();
                _lastFrame = null;
                continue;
            }

            // Always poll instead of doing a blocking ReadKey so SIGWINCH (or a Windows size
            // change picked up by TerminalSizeChanged) can interrupt the wait and re-render.
            // Live-update screens use a tight 200 ms re-render cycle; idle screens wake every ~1 s.
            // Polling resolution is 20 ms so a drag during resize never sees more than a one-frame
            // (~50 Hz) lag between the kernel reporting SIGWINCH and us repainting.
            bool tight = _screen == Screen.Search || IsLiveUpdateNeeded();
            var deadline = DateTime.UtcNow.AddMilliseconds(tight ? 200 : 1000);
            while (DateTime.UtcNow < deadline && _running)
            {
                if (_resizeSignaled || TerminalSizeChanged()) break;

                try
                {
                    if (Console.KeyAvailable)
                    {
                        var key = Console.ReadKey(true);
                        await HandleKeyAsync(key);
                        break;
                    }
                }
                catch (InvalidOperationException) { break; }
                await Task.Delay(20);
            }
        }

        _winchRegistration?.Dispose();
        try { Console.CursorVisible = true; } catch (IOException) { }
        Ui.RenderGoodbye();
    }

    /// <summary>
    /// Wipes the vertical strip of cells <paramref name="newWidth"/>..<paramref name="oldWidth"/>
    /// across the first <paramref name="rows"/> rows. Used when the terminal shrinks horizontally
    /// so stale content from the previous (wider) frame doesn't peek out from behind the new
    /// (narrower) frame. Painting only the orphaned strip — not the full screen — keeps the
    /// repaint invisible to the eye instead of flashing.
    /// </summary>
    /// <summary>
    /// Builds the entire next frame in memory (a private Spectre console writing into
    /// <see cref="_frameWriter"/>), prepends/appends the resize-wipe and synchronized-output
    /// escapes, and emits everything in a single <see cref="Console.Write(string)"/> so the
    /// terminal sees the new frame as one atomic update — the cure for "row-by-row paint"
    /// flicker during a resize drag. If the resulting frame is identical to the previously
    /// committed one, the syscall is skipped entirely.
    /// </summary>
    private void EmitFrame(IRenderable renderable, int width, int height, bool resized)
    {
        if (_renderConsole is null)
        {
            _renderConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(_frameWriter),
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Interactive = InteractionSupport.No,
            });
        }
        _renderConsole.Profile.Width = width;
        _renderConsole.Profile.Height = height;

        var body = _frameWriter.GetStringBuilder();
        body.Clear();
        _renderConsole.Write(renderable);
        string bodyText = body.ToString();

        var sb = new StringBuilder(bodyText.Length + 256);
        // 1. Begin synchronized output (modern terminals buffer until end-marker; older ones
        //    silently ignore both escapes).
        sb.Append("\x1B[?2026h");
        // 2. On horizontal shrink, wipe the orphaned right strip BEFORE drawing the new frame
        //    so it never becomes visible (we're inside the sync region so this is invisible).
        if (resized && _lastWidth > width && _lastHeight > 0)
        {
            int rows = Math.Min(_lastHeight, height);
            int stripCol = width + 1; // 1-based column for ANSI CUP
            for (int row = 1; row <= rows; row++)
            {
                sb.Append("\x1B[").Append(row).Append(';').Append(stripCol).Append('H');
                sb.Append("\x1B[K");
            }
        }
        // 3. Move cursor to top-left, paint the frame, and erase anything below it (handles
        //    a vertical shrink without needing a full-screen clear).
        sb.Append("\x1B[H");
        sb.Append(bodyText);
        sb.Append("\x1B[0J");
        // 4. Commit.
        sb.Append("\x1B[?2026l");

        string frame = sb.ToString();
        _lastWidth = width;
        _lastHeight = height;

        // Frame deduplication: when no input arrived and no live data refreshed, the rebuilt
        // frame is byte-identical to the last commit, so the terminal would just re-paint
        // the same pixels. Skip the syscall and any associated repaint cost entirely.
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        try { Console.Write(frame); } catch (IOException) { }
    }

    private static int SafeWidth()
    {
        try { return Console.WindowWidth; } catch { return 80; }
    }

    private static int SafeHeight()
    {
        try { return Console.WindowHeight; } catch { return 24; }
    }

    private bool TerminalSizeChanged()
    {
        try
        {
            return Console.WindowWidth != _lastWidth || Console.WindowHeight != _lastHeight;
        }
        catch { return false; }
    }

    private IRenderable BuildScreen()
    {
        if (_screen == Screen.Search) return BuildSearchScreen();
        if (_screen == Screen.Brew)   return BuildBrewScreen();
        return BuildMainScreen();
    }

    private IRenderable BuildMainScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("TopPad").Size(1),
                new Layout("Top").Size(3),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(2));

        root["TopPad"].Update(new Text(""));

        // Top row: Welcome info (left) + Setup panel (right, interactive)
        root["Top"].SplitColumns(
            new Layout("Welcome"),
            new Layout("Setup"));

        root["Top"]["Welcome"].Update(Ui.WelcomePanel());
        root["Top"]["Setup"].Update(_setupView.Render(_mainFocus == FocusSetup));

        // Footer (with top padding line)
        IView focusedView = GetFocusedMainView();
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(focusedView.GetStatusHints())));

        // Body: SDKs and Runtimes
        root["Body"].SplitRows(
            new Layout("SDKs").MinimumSize(8),
            new Layout("Runtimes").MinimumSize(5));

        root["Body"]["SDKs"].Update(_sdksView.Render(_mainFocus == FocusSdks));
        root["Body"]["Runtimes"].Update(_runtimesView.Render(_mainFocus == FocusRuntimes));

        return new Padder(root, new Padding(2, 0, 2, 0));
    }

    private IRenderable BuildSearchScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("TopPad").Size(1),
                new Layout("Header").Size(3),
                new Layout("SearchInput").Size(5),
                new Layout("Results").MinimumSize(5),
                new Layout("Footer").Size(2));

        root["TopPad"].Update(new Text(""));
        root["Header"].Update(Ui.SearchHeader(_setupInfo));
        root["SearchInput"].Update(_searchView.RenderSearchInput());
        root["Results"].Update(_searchView.RenderResults());
        // Search is a focused context — no package-manager switching here, so omit F2/F3 hints.
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(_searchView.GetStatusHints(), $"F1:Help  F6:Theme({ThemeManager.ThemeName})")));

        return new Padder(root, new Padding(2, 0, 2, 0));
    }

    private IRenderable BuildBrewScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("TopPad").Size(1),
                new Layout("Top").Size(3),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(2));

        root["TopPad"].Update(new Text(""));
        root["Top"].Update(Ui.WelcomePanel());
        root["Body"].Update(_brewView.Render(true));
        // While the brew search view is open it's a focused context: drop the workspace-switch hints.
        string brewGlobal = _brewView.IsSearching
            ? $"F1:Help  F6:Theme({ThemeManager.ThemeName})"
            : $"F1:Help  F2:.NET  F3:Search  F6:Theme({ThemeManager.ThemeName})  q:Quit";
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(_brewView.GetStatusHints(), brewGlobal)));

        return new Padder(root, new Padding(2, 0, 2, 0));
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        // F1 opens the docs site, regardless of which screen is active.
        if (key.Key == ConsoleKey.F1)
        {
            OpenUrl("https://sdk-manager.net");
            return;
        }

        if (_screen == Screen.Search)
        {
            await HandleSearchKeyAsync(key);
            return;
        }

        if (_screen == Screen.Brew)
        {
            await HandleBrewKeyAsync(key);
            return;
        }

        await HandleMainKeyAsync(key);
    }

    /// <summary>
    /// Launches the user's default browser pointing at <paramref name="url"/>. Cross-platform:
    /// macOS uses <c>open</c>, Windows uses <c>cmd /c start</c>, Linux uses <c>xdg-open</c>.
    /// Failures are silently swallowed because opening the docs is a best-effort convenience.
    /// </summary>
    private static void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd";
                psi.Arguments = $"/c start \"\" \"{url}\"";
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi.FileName = "open";
                psi.Arguments = url;
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.Arguments = url;
            }
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* best-effort */ }
    }

    private async Task HandleMainKeyAsync(ConsoleKeyInfo key)
    {
        // F2 opens the Homebrew workspace (macOS only)
        if (key.Key == ConsoleKey.F2 && BrewService.IsSupported() && !GetFocusedMainView().IsTextInputActive)
        {
            _screen = Screen.Brew;
            AnsiConsole.Clear();
            await _brewView.ActivateAsync();
            return;
        }

        // F3 opens search
        if (key.Key == ConsoleKey.F3 && !GetFocusedMainView().IsTextInputActive)
        {
            _screen = Screen.Search;
            AnsiConsole.Clear();
            await _searchView.ActivateAsync();
            return;
        }

        // F5/F6 cycles theme
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Cycle();
            return;
        }

        // Ctrl+U self-update
        if (key.Key == ConsoleKey.U && key.Modifiers.HasFlag(ConsoleModifiers.Control) && AppVersion.UpdateAvailable)
        {
            ThemeManager.ResetBackground();
            AnsiConsole.Clear();
            Console.CursorVisible = true;
            Console.WriteLine($"Updating dsm from v{AppVersion.Current} to v{AppVersion.LatestAvailable}...");
            Console.WriteLine(new string('-', 60));
            await AppVersion.SelfUpdateAsync();
            Console.WriteLine();
            Console.WriteLine("Update complete. Please restart dsm.");
            _running = false;
            return;
        }

        // Quit
        if (key.Key == ConsoleKey.Q && !GetFocusedMainView().IsTextInputActive)
        {
            _running = false;
            return;
        }

        // Tab cycles focus between SDKs, Runtimes, and Setup; Shift+Tab cycles backward.
        if (key.Key == ConsoleKey.Tab && !GetFocusedMainView().IsTextInputActive)
        {
            int step = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
            _mainFocus = (_mainFocus + step + 3) % 3;
            return;
        }

        // Shift+M: bulk-migrate all unmanaged SDKs into dotnetup (global, any focus).
        if (key.Key == ConsoleKey.M
            && (key.Modifiers.HasFlag(ConsoleModifiers.Shift) || key.KeyChar == 'M')
            && !GetFocusedMainView().IsTextInputActive)
        {
            if (DotnetUpService.IsInstalled() && _sdksView.HasUnmanaged)
                _pendingBulkMigrate = _sdksView.GetUnmanagedMigrations();
            return;
        }

        // Pass key to focused view
        await GetFocusedMainView().HandleKeyAsync(key);
    }

    private async Task HandleSearchKeyAsync(ConsoleKeyInfo key)
    {
        // F5/F6 cycles theme even in search
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Cycle();
            return;
        }

        var result = await _searchView.HandleKeyAsync(key);

        // Quit from search means "go back to main"
        if (result == KeyResult.Quit)
        {
            _screen = Screen.Main;
            AnsiConsole.Clear();
        }
    }

    private async Task HandleBrewKeyAsync(ConsoleKeyInfo key)
    {
        // F5/F6 cycles theme even in the brew workspace
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Cycle();
            return;
        }

        // F2 toggles back to the .NET workspace — but not while the brew search view is open.
        if (key.Key == ConsoleKey.F2 && !_brewView.IsSearching)
        {
            _screen = Screen.Main;
            AnsiConsole.Clear();
            return;
        }

        var result = await _brewView.HandleKeyAsync(key);

        // Quit from the brew workspace means "go back to main"
        if (result == KeyResult.Quit)
        {
            _screen = Screen.Main;
            AnsiConsole.Clear();
        }
    }

    /// <summary>
    /// Checks if any view has a pending interactive command.
    /// If so, exits TUI, runs the command with real terminal output, then resumes.
    /// </summary>
    private async Task<bool> CheckPendingCommandsAsync()
    {
        (string cmd, string args, string? note)? pending = null;

        if (_sdksView.PendingCommand is not null)
        {
            pending = _sdksView.PendingCommand;
            _sdksView.ClearPendingCommand();
        }
        else if (_searchView.PendingCommand is not null)
        {
            pending = _searchView.PendingCommand;
            _searchView.ClearPendingCommand();
        }
        else if (_runtimesView.PendingCommand is not null)
        {
            pending = _runtimesView.PendingCommand;
            _runtimesView.ClearPendingCommand();
        }
        else if (_setupView.PendingCommand is not null)
        {
            pending = _setupView.PendingCommand;
            _setupView.ClearPendingCommand();
        }
        else if (_brewView.PendingCommand is not null)
        {
            pending = _brewView.PendingCommand;
            _brewView.ClearPendingCommand();
        }

        if (pending is null)
            return false;

        await RunInteractiveAndRefreshAsync(pending.Value.cmd, pending.Value.args, pending.Value.note);
        return true;
    }

    /// <summary>
    /// Exits the TUI, runs an external command with real terminal output, optionally prints a
    /// follow-up note on success, then restores the TUI and refreshes all views.
    /// </summary>
    private async Task RunInteractiveAndRefreshAsync(string cmd, string args, string? note)
    {
        // Exit TUI, restore terminal to original settings for the external command
        ThemeManager.ResetBackground();
        AnsiConsole.Clear();
        Console.CursorVisible = true;

        Console.WriteLine($"Running: {cmd} {args}");
        Console.WriteLine(new string('-', 60));

        // Brew commands run non-interactively (skip the "Ask mode" y/n prompt).
        IReadOnlyDictionary<string, string>? environment =
            cmd == "brew" ? BrewService.NonInteractiveEnv : null;
        int exitCode = await ProcessRunner.RunInteractiveAsync(cmd, args, environment: environment);

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(note))
        {
            Console.WriteLine(note);
            Console.WriteLine(new string('-', 60));
        }

        Console.WriteLine(exitCode == 0
            ? "Completed successfully. Press any key to continue..."
            : $"Failed (exit code {exitCode}). Press any key to continue...");

        try { Console.ReadKey(true); } catch (InvalidOperationException) { }
        try { Console.CursorVisible = false; } catch (IOException) { }

        // Re-apply theme background before returning to TUI
        ThemeManager.ApplyBackground();

        if (cmd == "brew")
        {
            // Brew commands only affect the Homebrew workspace; skip dotnet PATH logic.
            _brewView.Refresh();
        }
        else
        {
            // Refresh PATH (add dotnetup + dotnet to process PATH and shell profile)
            DotnetUpService.RefreshPath();
            DotnetUpService.EnsurePathInShellProfile();
            _sdksView.Refresh();
            _runtimesView.Refresh();
            _setupView.Refresh();
            _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
            _ = LoadSetupInfoAsync();
        }
    }

    /// <summary>
    /// If a bulk migration was requested, exits the TUI, shows a confirmation dialog summarising
    /// which versions move to which, and on confirmation migrates all unmanaged SDKs to dotnetup.
    /// </summary>
    private async Task<bool> CheckBulkMigrateAsync()
    {
        var plan = _pendingBulkMigrate;
        _pendingBulkMigrate = null;
        if (plan is null || plan.Count == 0)
            return false;

        // Leave the live TUI so we can render the dialog and read a confirmation.
        ThemeManager.ResetBackground();
        AnsiConsole.Clear();
        Console.CursorVisible = true;

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Channel")
            .AddColumn("Current version");
        foreach (var m in plan)
            summary.AddRow(Markup.Escape(m.Channel), Markup.Escape(m.CurrentVersion));

        var body = new Rows(
            new Markup($"[bold]Migrate {plan.Count} unmanaged SDK(s) to dotnetup[/]"),
            new Text(""),
            summary,
            new Text(""),
            new Markup("[yellow]This will:[/]"),
            new Markup("  • Migrate these system SDKs into dotnetup, updating each to the latest patch"),
            new Markup("  • Possibly include other system installs dotnetup detects"),
            new Markup("  • Leave your existing copies in place until you remove them yourself"),
            new Markup("[grey]dsm keeps dotnetup's dotnet on your PATH. Downloads may be several hundred MB per SDK.[/]"));

        var dialog = new Panel(body)
            .Header("[yellow bold] Bulk migrate [/]")
            .Border(BoxBorder.Double)
            .BorderColor(ThemeManager.PanelBorderColor)
            .Padding(2, 1);
        AnsiConsole.Write(new DropShadow(dialog, ThemeManager.ShadowColor));
        AnsiConsole.WriteLine();

        bool confirmed = AnsiConsole.Prompt(
            new ConfirmationPrompt("Migrate all of these to dotnetup now?") { DefaultValue = false });

        if (!confirmed)
        {
            // Nothing ran — restore the TUI background and return to the main screen.
            ThemeManager.ApplyBackground();
            try { Console.CursorVisible = false; } catch (IOException) { }
            return true;
        }

        string channels = string.Join(' ', plan.Select(m => m.Channel).Distinct());
        const string note =
            "dotnetup now manages these SDKs. Your original copies remain on disk; remove the ones you no " +
            "longer need with the official .NET uninstall tool (e.g. 'sudo dotnet-core-uninstall remove " +
            "--sdk <version>'). Those locations may hold other system-installed versions, so don't delete " +
            "whole folders.";

        await RunInteractiveAndRefreshAsync("dotnetup", $"sdk install {channels} --migrate-from-system", note);
        return true;
    }

    private IView GetFocusedMainView() => _mainFocus switch
    {
        FocusSdks => _sdksView,
        FocusRuntimes => _runtimesView,
        FocusSetup => _setupView,
        _ => _sdksView
    };

    private bool IsLiveUpdateNeeded()
    {
        return _sdksView.NeedsLiveUpdate
            || _runtimesView.NeedsLiveUpdate
            || _searchView.NeedsLiveUpdate
            || _brewView.NeedsLiveUpdate
            || AppVersion.CheckInProgress;
    }

    private async Task LoadSetupInfoAsync()
    {
        try
        {
            if (DotnetUpService.IsInstalled())
            {
                var info = await DotnetUpService.GetInfoAsync();
                if (info is not null)
                    _setupInfo = $"v{info.Version}  {info.Architecture}  {info.Rid}";
            }
        }
        catch { }
    }
}
