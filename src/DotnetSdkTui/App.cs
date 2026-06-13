using Spectre.Console;
using DotnetSdkTui.Views;
using DotnetSdkTui.Theme;
using DotnetSdkTui.Services;

namespace DotnetSdkTui;

/// <summary>
/// Main application class that renders all sections on a single unified screen
/// and handles keyboard navigation between focused sections.
/// </summary>
public sealed class App
{
    private readonly IView[] _views;
    private int _focusedSection;
    private bool _running = true;
    private string _dotnetUpStatus = "checking...";
    private readonly bool _skipSplash;

    /// <summary>
    /// Initializes the application with all four view sections.
    /// </summary>
    /// <param name="skipSplash">When true, skips the startup splash animation.</param>
    public App(bool skipSplash = false)
    {
        _skipSplash = skipSplash;
        _views =
        [
            new SdksView(),
            new SearchView(),
            new ProjectView(),
            new SetupView()
        ];
    }

    /// <summary>
    /// Runs the main application loop: renders the screen, handles input, and polls for live updates.
    /// </summary>
    public async Task RunAsync()
    {
        Console.CursorVisible = false;

        if (!_skipSplash)
            await MarioTheme.RenderSplashAsync();

        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";

        // Activate all views on startup
        foreach (var view in _views)
            await view.ActivateAsync();

        while (_running)
        {
            AnsiConsole.Clear();
            RenderScreen();

            if (IsLiveUpdateNeeded())
            {
                var deadline = DateTime.UtcNow.AddMilliseconds(150);
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
                    catch (InvalidOperationException)
                    {
                        break;
                    }

                    await Task.Delay(50);
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

    /// <summary>
    /// Renders the full unified screen: header, 2x2 section grid, and footer.
    /// Uses Spectre.Console Layout for proper full-screen rendering.
    /// </summary>
    private void RenderScreen()
    {
        string cwd = Directory.GetCurrentDirectory();
        List<Models.ProjectInfo> projects = ProjectDetector.Detect();
        string? projectName = projects.Count > 0 ? projects[0].FileName : null;
        string themeName = ThemeManager.Current == AppTheme.Dark ? "🌙 Dark" : "☀️ Light";

        // Use Layout for proper full-screen sizing
        var root = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(1));

        var body = root["Body"]
            .SplitRows(
                new Layout("TopRow"),
                new Layout("BottomRow"));

        body["TopRow"].SplitColumns(
            new Layout("SDKs"),
            new Layout("Search"));

        body["BottomRow"].SplitColumns(
            new Layout("Project"),
            new Layout("Setup"));

        root["Header"].Update(MarioTheme.Header(_dotnetUpStatus, cwd, projectName, themeName));
        root["Footer"].Update(MarioTheme.Footer(_views[_focusedSection].GetStatusHints()));

        body["TopRow"]["SDKs"].Update(_views[0].Render(_focusedSection == 0));
        body["TopRow"]["Search"].Update(_views[1].Render(_focusedSection == 1));
        body["BottomRow"]["Project"].Update(_views[2].Render(_focusedSection == 2));
        body["BottomRow"]["Setup"].Update(_views[3].Render(_focusedSection == 3));

        AnsiConsole.Write(root);
    }

    /// <summary>
    /// Handles a key press: global shortcuts first, then delegates to the focused section.
    /// </summary>
    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        // F1-F4 ALWAYS switch section focus (even during text input)
        int sectionIndex = key.Key switch
        {
            ConsoleKey.F1 => 0,
            ConsoleKey.F2 => 1,
            ConsoleKey.F3 => 2,
            ConsoleKey.F4 => 3,
            _ => -1
        };

        if (sectionIndex >= 0 && sectionIndex < _views.Length)
        {
            _focusedSection = sectionIndex;
            return;
        }

        // F5 toggles theme (never conflicts with view keys)
        if (key.Key == ConsoleKey.F5)
        {
            ThemeManager.Toggle();
            return;
        }

        // Quit (only when not in text input)
        if (key.Key == ConsoleKey.Q && !_views[_focusedSection].IsTextInputActive)
        {
            _running = false;
            return;
        }

        // Tab cycling between sections (only when focused view is not capturing text)
        if (key.Key == ConsoleKey.Tab && !_views[_focusedSection].IsTextInputActive)
        {
            if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                _focusedSection = (_focusedSection - 1 + _views.Length) % _views.Length;
            else
                _focusedSection = (_focusedSection + 1) % _views.Length;
            return;
        }

        // Pass key to focused view
        var result = await _views[_focusedSection].HandleKeyAsync(key);
        if (result == KeyResult.Quit)
        {
            _running = false;
        }
    }

    private bool IsLiveUpdateNeeded()
    {
        foreach (var view in _views)
        {
            if (view.NeedsLiveUpdate)
                return true;
        }
        return false;
    }
}
