using Spectre.Console;
using DotnetSdkTui.Views;
using DotnetSdkTui.Theme;
using DotnetSdkTui.Services;

namespace DotnetSdkTui;

public sealed class App
{
    private readonly IView[] _views;
    private int _focusedSection;
    private bool _running = true;
    private string _dotnetUpStatus = "checking...";
    private readonly bool _skipSplash;

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

    private void RenderScreen()
    {
        string cwd = Directory.GetCurrentDirectory();
        List<Models.ProjectInfo> projects = ProjectDetector.Detect();
        string? projectName = projects.Count > 0 ? projects[0].FileName : null;
        string themeName = ThemeManager.Current == AppTheme.Dark ? "🌙 Dark" : "☀️ Light";

        // Header
        AnsiConsole.Write(MarioTheme.Header(_dotnetUpStatus, cwd, projectName, themeName));

        // All sections rendered in a 2-column grid layout
        var topGrid = new Grid().AddColumn().AddColumn();
        topGrid.AddRow(
            _views[0].Render(_focusedSection == 0),  // SDKs
            _views[1].Render(_focusedSection == 1));  // Search

        var bottomGrid = new Grid().AddColumn().AddColumn();
        bottomGrid.AddRow(
            _views[2].Render(_focusedSection == 2),  // Project
            _views[3].Render(_focusedSection == 3));  // Setup

        AnsiConsole.Write(topGrid);
        AnsiConsole.Write(bottomGrid);

        // Footer with focused section hints
        AnsiConsole.Write(MarioTheme.Footer(_views[_focusedSection].GetStatusHints()));
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        // Quit (only when not in text input)
        if (key.Key == ConsoleKey.Q && !_views[_focusedSection].IsTextInputActive)
        {
            _running = false;
            return;
        }

        // Theme toggle
        if (key.Key == ConsoleKey.T && !_views[_focusedSection].IsTextInputActive)
        {
            ThemeManager.Toggle();
            return;
        }

        // Section focus switching with F1-F4
        if (!_views[_focusedSection].IsTextInputActive)
        {
            int sectionIndex = key.Key switch
            {
                ConsoleKey.F1 => 0,
                ConsoleKey.F2 => 1,
                ConsoleKey.F3 => 2,
                ConsoleKey.F4 => 3,
                _ => -1
            };

            if (sectionIndex >= 0 && sectionIndex < _views.Length && sectionIndex != _focusedSection)
            {
                _focusedSection = sectionIndex;
                return;
            }
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
