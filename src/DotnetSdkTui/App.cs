using Spectre.Console;
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

        ThemeManager.ApplyBackground();

        // Graceful Ctrl+C: stop the loop instead of killing the process
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        if (!_skipSplash)
            await Ui.RenderSplashAsync();

        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        _ = LoadSetupInfoAsync();
        _ = AppVersion.CheckForUpdateAsync();

        // Activate views
        await Task.WhenAll(
            _sdksView.ActivateAsync(),
            _runtimesView.ActivateAsync(),
            _setupView.ActivateAsync());

        AnsiConsole.Clear();

        while (_running)
        {
            try { Console.SetCursorPosition(0, 0); } catch (IOException) { }
            RenderScreen();

            // Check for pending interactive commands from views
            if (await CheckPendingCommandsAsync())
            {
                AnsiConsole.Clear();
                continue;
            }

            // Check for a pending bulk-migration request (Shift+M)
            if (await CheckBulkMigrateAsync())
            {
                AnsiConsole.Clear();
                continue;
            }

            // In search mode or during live updates, use non-blocking polling
            // so async results can trigger re-renders
            if (_screen == Screen.Search || IsLiveUpdateNeeded())
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(200);
                while (DateTime.UtcNow < deadline && _running)
                {
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
                    await Task.Delay(30);
                }
            }
            else
            {
                try
                {
                    var key = Console.ReadKey(true);
                    await HandleKeyAsync(key);
                }
                catch (InvalidOperationException)
                {
                    await Task.Delay(100);
                }
            }
        }

        try { Console.CursorVisible = true; } catch (IOException) { }
        Ui.RenderGoodbye();
    }

    private void RenderScreen()
    {
        if (_screen == Screen.Search)
        {
            RenderSearchScreen();
            return;
        }

        if (_screen == Screen.Brew)
        {
            RenderBrewScreen();
            return;
        }

        RenderMainScreen();
    }

    private void RenderMainScreen()
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

        AnsiConsole.Write(new Padder(root, new Padding(2, 0, 2, 0)));
    }

    private void RenderSearchScreen()
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
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(_searchView.GetStatusHints())));

        AnsiConsole.Write(new Padder(root, new Padding(2, 0, 2, 0)));
    }

    private void RenderBrewScreen()
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
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(_brewView.GetStatusHints(), "F1:.NET  F3:Search  F6:Theme  q:Quit")));

        AnsiConsole.Write(new Padder(root, new Padding(2, 0, 2, 0)));
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
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

        // F5/F6 toggles theme
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Toggle();
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

        // Tab cycling between SDKs, Runtimes, and Setup
        if (key.Key == ConsoleKey.Tab && !GetFocusedMainView().IsTextInputActive)
        {
            _mainFocus = (_mainFocus + 1) % 3;
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
        // F5/F6 toggles theme even in search
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Toggle();
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
        // F5/F6 toggles theme even in the brew workspace
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Toggle();
            return;
        }

        // F1 returns to the .NET main screen (works even while typing a search)
        if (key.Key == ConsoleKey.F1)
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
