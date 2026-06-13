using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Full-screen search interface for discovering available .NET SDK and runtime versions.
/// Activated by "/" shortcut, Esc returns to main screen.
/// </summary>
public sealed class SearchView : IView
{
    public string Name => "Search";
    public string Icon => "/";

    private string _searchQuery = "";
    private List<AvailableSdk> _results = [];
    private bool _searching;
    private string? _error;
    private int _selectedIndex;
    private bool _inputActive = true;
    private CancellationTokenSource? _debounceCts;
    private bool _hasPendingSearch;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Set when the user wants to install a search result.</summary>
    internal (string Command, string Args)? PendingCommand { get; private set; }

    public bool NeedsLiveUpdate => _searching || _hasPendingSearch;
    public bool IsTextInputActive => _inputActive;

    public Task ActivateAsync()
    {
        _inputActive = true;
        return Task.CompletedTask;
    }

    internal void ClearPendingCommand() => PendingCommand = null;

    public IRenderable Render(bool focused) => RenderSearchInput();

    /// <summary>Renders the search input panel.</summary>
    public IRenderable RenderSearchInput()
    {
        string cursor = _inputActive ? "|" : "";
        string inputDisplay = _searchQuery.Length > 0
            ? _searchQuery
            : (_inputActive ? "" : "type to search...");

        string searchIcon = _searching ? "*" : "/";
        var inputMarkup = new Markup(
            $"[{MarioTheme.Yellow} bold] {searchIcon} Search: [/][{MarioTheme.White}]{Markup.Escape(inputDisplay)}{cursor}[/]");

        return new Panel(inputMarkup)
            .Header($"[{MarioTheme.Yellow} bold] 🔍 Search .NET SDKs & Runtimes [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.PanelBorderColor)
            .Expand();
    }

    /// <summary>Renders the search results panel.</summary>
    public IRenderable RenderResults()
    {
        var resultParts = new List<IRenderable>();

        if (_searching)
        {
            resultParts.Add(MarioTheme.Info("Searching..."));
        }
        else if (_error is not null)
        {
            resultParts.Add(MarioTheme.Error(_error));
        }
        else if (_results.Count > 0)
        {
            var table = MarioTheme.StyledTable("", "Component", "Version", "Channel", "Support", "Latest");

            int maxRows = Math.Min(_results.Count, 20);
            for (int i = 0; i < maxRows; i++)
            {
                var sdk = _results[i];
                bool selected = !_inputActive && i == _selectedIndex;
                string pointer = selected ? ">" : " ";
                string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;
                string latest = sdk.IsLatest ? "*" : "";
                string componentColor = sdk.Component == "SDK" ? MarioTheme.Green : MarioTheme.Blue;

                table.AddRow(
                    new Markup($"[{style}]{pointer}[/]"),
                    new Markup($"[{componentColor}]{Markup.Escape(sdk.Component)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.Version)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.ChannelVersion)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(FormatPhase(sdk.SupportPhase))}[/]"),
                    new Markup($"[{MarioTheme.Gold}]{latest}[/]"));
            }

            resultParts.Add(table);
            if (_results.Count > maxRows)
                resultParts.Add(MarioTheme.Muted($"Showing {maxRows} of {_results.Count} results"));
        }
        else if (_searchQuery.Length > 0)
        {
            resultParts.Add(MarioTheme.Muted("No results found."));
        }
        else
        {
            resultParts.Add(MarioTheme.Muted("Type a version number, channel (e.g. 10.0), or keyword (latest, lts, preview, runtime)."));
        }

        string hint = _inputActive
            ? $"[{MarioTheme.Gray}]Tab/Down:Results  Esc:Back[/]"
            : $"[{MarioTheme.Gray}]up/down:Navigate  i:Install  Tab:Input  Esc:Back[/]";
        resultParts.Add(new Markup($"\n {hint}"));

        return new Panel(new Rows(resultParts))
            .Header($"[{MarioTheme.Yellow} bold] 📋 Results [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor)
            .Expand();
    }

    public string GetStatusHints()
    {
        if (_searching) return "Searching...";
        if (_inputActive) return "Type to search  Tab:Results  Esc:Back";
        return "up/down:Navigate  i:Install  Tab:Input  Esc:Back";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_searching && key.Key != ConsoleKey.Escape) return KeyResult.NotHandled;

        if (_inputActive)
            return await HandleInputModeAsync(key);

        return await HandleResultsModeAsync(key);
    }

    private async Task<KeyResult> HandleInputModeAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                if (_results.Count > 0)
                {
                    _inputActive = false;
                    _selectedIndex = 0;
                }
                return KeyResult.Handled;

            case ConsoleKey.Backspace:
                if (_searchQuery.Length > 0)
                {
                    _searchQuery = _searchQuery[..^1];
                    TriggerDebouncedSearch();
                }
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                _searchQuery = "";
                _results = [];
                _error = null;
                _debounceCts?.Cancel();
                return KeyResult.Quit; // Signal App to switch back to main screen

            default:
                if (key.KeyChar is >= ' ' and <= '~')
                {
                    _searchQuery += key.KeyChar;
                    TriggerDebouncedSearch();
                    return KeyResult.Handled;
                }
                return KeyResult.NotHandled;
        }
    }

    private async Task<KeyResult> HandleResultsModeAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_results.Count > 0)
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_results.Count > 0)
                    _selectedIndex = Math.Min(Math.Min(_results.Count, 20) - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.Tab:
                _inputActive = true;
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                _searchQuery = "";
                _results = [];
                _error = null;
                _inputActive = true;
                _debounceCts?.Cancel();
                return KeyResult.Quit; // Signal App to switch back to main screen

            case ConsoleKey.I:
                RequestInstall();
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private void TriggerDebouncedSearch()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        _hasPendingSearch = true;
        var token = _debounceCts.Token;
        var query = _searchQuery;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
                if (token.IsCancellationRequested) return;

                _searching = true;

                if (string.IsNullOrWhiteSpace(query))
                {
                    _results = [];
                    _error = null;
                    _searching = false;
                    _hasPendingSearch = false;
                    return;
                }

                var results = await SdkSearchService.SearchAvailableSdksAsync(query, token);
                if (!token.IsCancellationRequested)
                {
                    _results = results;
                    _selectedIndex = 0;
                    _error = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _error = ex.Message;
                    _results = [];
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    _searching = false;
                    _hasPendingSearch = false;
                }
            }
        });
    }

    private void RequestInstall()
    {
        if (_results.Count == 0 || _selectedIndex >= _results.Count) return;

        var sdk = _results[_selectedIndex];
        if (!DotnetUpService.IsInstalled())
        {
            _error = "dotnetup not found. Press Esc and use F3 to install it.";
            return;
        }

        if (sdk.Component == "SDK")
            PendingCommand = ("dotnetup", $"sdk install {sdk.ChannelVersion}");
        else
            PendingCommand = ("dotnetup", $"runtime install {sdk.ChannelVersion}");
    }

    private static string FormatPhase(string phase) =>
        phase.ToLowerInvariant() switch
        {
            "active" => "Active",
            "maintenance" => "Maintenance",
            "preview" => "Preview",
            "go-live" => "Go-Live",
            "rc" => "RC",
            "eol" => "End of Life",
            _ => phase
        };
}
