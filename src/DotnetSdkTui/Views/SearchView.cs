using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Full-screen search interface for discovering available .NET SDK and runtime versions.
/// Activated by "/" shortcut, Esc returns to main screen.
/// Search is fully non-blocking: typing is always responsive, results update asynchronously
/// with debounce, and previous HTTP requests are cancelled when a new query arrives.
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
    private CancellationTokenSource? _searchCts;
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
            $"[{Ui.Yellow} bold] {searchIcon} Search: [/][{Ui.White}]{Markup.Escape(inputDisplay)}{cursor}[/]");

        return new Panel(inputMarkup)
            .Header($"[{Ui.Yellow} bold] {Ui.IconSearch} Search .NET SDKs & Runtimes [/]")
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
            resultParts.Add(Ui.Info("Searching..."));
        }
        else if (_error is not null)
        {
            resultParts.Add(Ui.Error(_error));
        }
        else if (_results.Count > 0)
        {
            var table = Ui.StyledTable("", "Component", "Version", "Channel", "Support", "Latest");

            int maxRows = Math.Min(_results.Count, 20);
            for (int i = 0; i < maxRows; i++)
            {
                var sdk = _results[i];
                bool selected = !_inputActive && i == _selectedIndex;
                string pointer = selected ? ">" : " ";
                string style = selected ? $"{Ui.Yellow} bold" : Ui.White;
                string latest = sdk.IsLatest ? "*" : "";
                string componentColor = sdk.Component == "SDK" ? Ui.Green : Ui.Blue;

                table.AddRow(
                    new Markup($"[{style}]{pointer}[/]"),
                    new Markup($"[{componentColor}]{Markup.Escape(sdk.Component)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.Version)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.ChannelVersion)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(FormatPhase(sdk.SupportPhase))}[/]"),
                    new Markup($"[{Ui.Gold}]{latest}[/]"));
            }

            resultParts.Add(table);
            if (_results.Count > maxRows)
                resultParts.Add(Ui.Muted($"Showing {maxRows} of {_results.Count} results"));
        }
        else if (_searchQuery.Length > 0)
        {
            resultParts.Add(Ui.Muted("No results found."));
        }
        else
        {
            resultParts.Add(Ui.Muted("Type a version number, channel (e.g. 10.0), or keyword (latest, lts, preview, runtime)."));
        }

        string hint = _inputActive
            ? $"[{Ui.Gray}]Tab/Down:Results  Esc:Back[/]"
            : $"[{Ui.Gray}]up/down:Navigate  i:Install  Tab:Input  Esc:Back[/]";
        resultParts.Add(new Markup($"\n {hint}"));

        return new Panel(new Rows(resultParts))
            .Header($"[{Ui.Yellow} bold] {Ui.IconResults} Results [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor)
            .Expand();
    }

    public string GetStatusHints()
    {
        if (_searching) return "Searching...";
        if (_inputActive) return "Type to search  Tab:Results  Esc:Back";
        return DotnetUpService.IsInstalled()
            ? "up/down:Navigate  i:Install  Tab:Input  Esc:Back"
            : "up/down:Navigate  Tab:Input  Esc:Back";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        // Input is NEVER blocked — typing is always responsive regardless of search state
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
                CancelSearch();
                return KeyResult.Quit;

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
                CancelSearch();
                return KeyResult.Quit;

            case ConsoleKey.I:
                RequestInstall();
                return KeyResult.Handled;

            default:
                // Any printable character switches back to input mode and types
                if (key.KeyChar is >= ' ' and <= '~')
                {
                    _inputActive = true;
                    _searchQuery += key.KeyChar;
                    TriggerDebouncedSearch();
                    return KeyResult.Handled;
                }
                return KeyResult.NotHandled;
        }
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _searching = false;
        _hasPendingSearch = false;
    }

    private void TriggerDebouncedSearch()
    {
        // Cancel any previous debounce timer AND in-flight HTTP request
        _searchCts?.Cancel();
        _searching = false;
        _searchCts = new CancellationTokenSource();
        _hasPendingSearch = true;
        var token = _searchCts.Token;
        var query = _searchQuery;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
                if (token.IsCancellationRequested) return;

                _searching = true;
                _error = null;

                if (string.IsNullOrWhiteSpace(query))
                {
                    _results = [];
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
            PendingCommand = ("dotnetup", $"sdk install {sdk.Version}");
        else
            PendingCommand = ("dotnetup", $"runtime install {sdk.Version}");
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
