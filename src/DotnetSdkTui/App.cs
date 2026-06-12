using Spectre.Console;
using DotnetSdkTui.Views;
using DotnetSdkTui.Theme;
using DotnetSdkTui.Services;

namespace DotnetSdkTui;

public sealed class App
{
    private readonly IView[] _views;
    private readonly (string Name, string Icon)[] _tabInfo;
    private int _activeTab;
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

        _tabInfo =
        [
            ("SDKs", "🍄"),
            ("Search", "★"),
            ("Project", "🔥"),
            ("Setup", "●"),
        ];
    }

    public async Task RunAsync()
    {
        Console.CursorVisible = false;

        if (!_skipSplash)
            await MarioTheme.RenderSplashAsync();

        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        await _views[_activeTab].ActivateAsync();

        var layout = new Layout("Root")
            .SplitRows(
                new Layout("Header").Size(3),
                new Layout("Tabs").Size(1),
                new Layout("Content").MinimumSize(5),
                new Layout("Footer").Size(1));

        while (_running)
        {
            AnsiConsole.Clear();
            UpdateLayout(layout);
            AnsiConsole.Write(layout);

            // If a view needs live updates (e.g., command running), poll mode
            if (IsLiveUpdateNeeded())
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

    private void UpdateLayout(Layout layout)
    {
        string cwd = Directory.GetCurrentDirectory();
        List<Models.ProjectInfo> projects = ProjectDetector.Detect();
        string? projectName = projects.Count > 0 ? projects[0].FileName : null;

        layout["Header"].Update(MarioTheme.Header(_dotnetUpStatus, cwd, projectName));
        layout["Tabs"].Update(MarioTheme.TabBar(_tabInfo, _activeTab));
        layout["Content"].Update(_views[_activeTab].Render());
        layout["Footer"].Update(MarioTheme.Footer(_views[_activeTab].GetStatusHints()));
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        // Quit
        if (key.Key == ConsoleKey.Q && !IsTextInputActive())
        {
            _running = false;
            return;
        }

        // Tab switching with number keys (only when not in text input mode)
        if (!IsTextInputActive())
        {
            int tabIndex = key.Key switch
            {
                ConsoleKey.D1 or ConsoleKey.NumPad1 => 0,
                ConsoleKey.D2 or ConsoleKey.NumPad2 => 1,
                ConsoleKey.D3 or ConsoleKey.NumPad3 => 2,
                ConsoleKey.D4 or ConsoleKey.NumPad4 => 3,
                _ => -1
            };

            if (tabIndex >= 0 && tabIndex < _views.Length && tabIndex != _activeTab)
            {
                _activeTab = tabIndex;
                await _views[_activeTab].ActivateAsync();
                return;
            }
        }

        // Tab cycling
        if (key.Key == ConsoleKey.Tab)
        {
            if ((key.Modifiers & ConsoleModifiers.Shift) != 0)
                _activeTab = (_activeTab - 1 + _views.Length) % _views.Length;
            else
                _activeTab = (_activeTab + 1) % _views.Length;

            await _views[_activeTab].ActivateAsync();
            return;
        }

        // Pass to current view
        var result = await _views[_activeTab].HandleKeyAsync(key);
        if (result == KeyResult.Quit)
        {
            _running = false;
        }
    }

    private bool IsTextInputActive()
    {
        return _views[_activeTab] is SearchView { InputMode: true };
    }

    private bool IsLiveUpdateNeeded()
    {
        return _views[_activeTab] is ProjectView { IsRunning: true }
            || _views[_activeTab] is SdksView { IsLoading: true }
            || _views[_activeTab] is SetupView { IsLoading: true };
    }
}
