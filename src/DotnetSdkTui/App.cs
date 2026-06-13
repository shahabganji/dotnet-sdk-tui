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

    private enum Screen { Main, Search }
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

    public App(bool skipSplash = false)
    {
        _skipSplash = skipSplash;
        _sdksView = new SdksView();
        _runtimesView = new RuntimesView();
        _searchView = new SearchView();
        _setupView = new SetupView();
    }

    public async Task RunAsync()
    {
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

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_screen == Screen.Search)
        {
            await HandleSearchKeyAsync(key);
            return;
        }

        await HandleMainKeyAsync(key);
    }

    private async Task HandleMainKeyAsync(ConsoleKeyInfo key)
    {
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

    /// <summary>
    /// Checks if any view has a pending interactive command.
    /// If so, exits TUI, runs the command with real terminal output, then resumes.
    /// </summary>
    private async Task<bool> CheckPendingCommandsAsync()
    {
        (string cmd, string args)? pending = null;

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

        if (pending is null)
            return false;

        // Exit TUI, restore terminal to original settings for the external command
        ThemeManager.ResetBackground();
        AnsiConsole.Clear();
        Console.CursorVisible = true;

        Console.WriteLine($"Running: {pending.Value.cmd} {pending.Value.args}");
        Console.WriteLine(new string('-', 60));

        int exitCode = await ProcessRunner.RunInteractiveAsync(pending.Value.cmd, pending.Value.args);

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine(exitCode == 0
            ? "Completed successfully. Press any key to continue..."
            : $"Failed (exit code {exitCode}). Press any key to continue...");

        try { Console.ReadKey(true); } catch (InvalidOperationException) { }
        try { Console.CursorVisible = false; } catch (IOException) { }

        // Re-apply theme background before returning to TUI
        ThemeManager.ApplyBackground();

        // Refresh data after install/uninstall
        _sdksView.Refresh();
        _runtimesView.Refresh();
        _setupView.Refresh();
        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        _ = LoadSetupInfoAsync();

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
