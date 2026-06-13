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

    // Focus on main screen: 0=SDKs, 1=Runtimes
    private int _mainFocus;
    private const int FocusSdks = 0;
    private const int FocusRuntimes = 1;

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
        Console.CursorVisible = false;

        if (!_skipSplash)
            await MarioTheme.RenderSplashAsync();

        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        _ = LoadSetupInfoAsync();

        // Activate views
        await Task.WhenAll(
            _sdksView.ActivateAsync(),
            _runtimesView.ActivateAsync());

        AnsiConsole.Clear();

        while (_running)
        {
            Console.SetCursorPosition(0, 0);
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

        Console.CursorVisible = true;
        AnsiConsole.Clear();
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
                new Layout("Header").Size(4),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(1));

        // Header with setup info side-by-side
        root["Header"].Update(MarioTheme.Header(_dotnetUpStatus, _setupInfo));

        // Footer
        IView focusedView = GetFocusedMainView();
        root["Footer"].Update(MarioTheme.Footer(focusedView.GetStatusHints()));

        // Body: SDKs and Runtimes
        root["Body"].SplitRows(
            new Layout("SDKs").MinimumSize(8),
            new Layout("Runtimes").MinimumSize(5));

        root["Body"]["SDKs"].Update(_sdksView.Render(_mainFocus == FocusSdks));
        root["Body"]["Runtimes"].Update(_runtimesView.Render(_mainFocus == FocusRuntimes));

        AnsiConsole.Write(root);
    }

    private void RenderSearchScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(4),
                new Layout("SearchInput").Size(5),
                new Layout("Results").MinimumSize(5),
                new Layout("Footer").Size(1));

        root["Header"].Update(MarioTheme.Header(_dotnetUpStatus, _setupInfo));
        root["SearchInput"].Update(_searchView.RenderSearchInput());
        root["Results"].Update(_searchView.RenderResults());
        root["Footer"].Update(MarioTheme.Footer(_searchView.GetStatusHints()));

        AnsiConsole.Write(root);
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
        // Cmd+/ on Mac (sends 0x1F), Ctrl+/ on Linux/Windows, or bare "/" as fallback
        bool isSearchShortcut = key.KeyChar == '\x1f'
            || (key.Key == ConsoleKey.Oem2 && (key.Modifiers & ConsoleModifiers.Control) != 0)
            || key.KeyChar == '/';
        if (isSearchShortcut && !GetFocusedMainView().IsTextInputActive)
        {
            _screen = Screen.Search;
            AnsiConsole.Clear();
            await _searchView.ActivateAsync();
            return;
        }

        // F1/F2 switch focus
        int sectionIndex = key.Key switch
        {
            ConsoleKey.F1 => FocusSdks,
            ConsoleKey.F2 => FocusRuntimes,
            _ => -1
        };

        if (sectionIndex >= 0)
        {
            _mainFocus = sectionIndex;
            return;
        }

        // F5 toggles theme
        if (key.Key == ConsoleKey.F5)
        {
            ThemeManager.Toggle();
            return;
        }

        // Quit
        if (key.Key == ConsoleKey.Q && !GetFocusedMainView().IsTextInputActive)
        {
            _running = false;
            return;
        }

        // Tab cycling between SDKs and Runtimes
        if (key.Key == ConsoleKey.Tab && !GetFocusedMainView().IsTextInputActive)
        {
            _mainFocus = _mainFocus == FocusSdks ? FocusRuntimes : FocusSdks;
            return;
        }

        // Pass key to focused view
        await GetFocusedMainView().HandleKeyAsync(key);
    }

    private async Task HandleSearchKeyAsync(ConsoleKeyInfo key)
    {
        // F5 toggles theme even in search
        if (key.Key == ConsoleKey.F5)
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

        if (pending is null)
            return false;

        // Exit TUI, run command interactively
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

        Console.ReadKey(true);
        Console.CursorVisible = false;

        // Refresh data after install/uninstall
        _sdksView.Refresh();
        _runtimesView.Refresh();
        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        _ = LoadSetupInfoAsync();

        return true;
    }

    private IView GetFocusedMainView() => _mainFocus switch
    {
        FocusSdks => _sdksView,
        FocusRuntimes => _runtimesView,
        _ => _sdksView
    };

    private bool IsLiveUpdateNeeded()
    {
        return _sdksView.NeedsLiveUpdate
            || _runtimesView.NeedsLiveUpdate
            || _searchView.NeedsLiveUpdate;
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
