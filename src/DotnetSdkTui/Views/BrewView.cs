using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Self-contained Homebrew workspace: lists installed formulae and offers an inline
/// debounced search over the full catalog. Install/uninstall exit the TUI to show real
/// terminal output, mirroring the .NET SDK workspace.
/// </summary>
public sealed class BrewView : IView
{
    private enum Mode { List, Search }

    public string Name => "Homebrew";
    public string Icon => "\U0001f37a"; // 🍺

    private Mode _mode = Mode.List;
    private List<BrewPackage> _installed = [];
    private List<BrewPackage> _results = [];
    private string _query = "";
    private bool _activated;
    private bool _loading;
    private bool _searching;
    private bool _hasPendingSearch;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;
    private int _scrollOffset;
    private CancellationTokenSource? _searchCts;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>Set by install/uninstall to signal App to run a command interactively.</summary>
    internal (string Command, string Args, string? Note)? PendingCommand { get; private set; }

    public bool NeedsLiveUpdate => _loading || _searching || _hasPendingSearch;
    public bool IsTextInputActive => _mode == Mode.Search;

    private List<BrewPackage> CurrentItems => _mode == Mode.Search ? _results : _installed;

    public Task ActivateAsync()
    {
        if (!_activated && BrewService.IsInstalled())
        {
            _activated = true;
            Refresh();
        }
        return Task.CompletedTask;
    }

    internal void ClearPendingCommand() => PendingCommand = null;

    internal void Refresh()
    {
        if (!BrewService.IsInstalled())
            return;

        _statusMessage = null;
        _loading = true;
        _error = null;
        _ = LoadInstalledAsync();
    }

    private async Task LoadInstalledAsync()
    {
        try
        {
            _installed = await BrewService.ListInstalledAsync();
            if (_mode == Mode.List)
                _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _installed.Count - 1));
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _loading = false;
        }
    }

    public IRenderable Render(bool focused)
    {
        if (!BrewService.IsInstalled())
            return RenderPanel(focused, new Rows(
                Ui.Info("Homebrew is not installed."),
                new Markup($"[{Ui.Gray}]Install it from [/][{Ui.Blue} underline link=https://brew.sh]https://brew.sh[/][{Ui.Gray}], then press r to refresh.[/]")));

        if (_loading && _installed.Count == 0)
            return RenderPanel(focused, Ui.Info("Loading installed formulae..."));

        if (_error is not null)
            return RenderPanel(focused, Ui.Error(_error));

        var parts = new List<IRenderable>();

        if (_mode == Mode.Search)
            parts.Add(RenderSearchLine());

        parts.Add(RenderTable(focused));

        // Description of the selected item (when known)
        var items = CurrentItems;
        if (focused && _selectedIndex < items.Count && !string.IsNullOrWhiteSpace(items[_selectedIndex].Description))
            parts.Add(new Markup($"\n[{Ui.Gray}]{Markup.Escape(items[_selectedIndex].Description!)}[/]"));

        if (_statusMessage is not null)
            parts.Add(new Markup($"\n[{Ui.Gold}]{Markup.Escape(_statusMessage)}[/]"));

        return RenderPanel(focused, new Rows(parts));
    }

    private IRenderable RenderSearchLine()
    {
        string icon = _searching ? "*" : "/";
        string display = _query.Length > 0 ? _query : "type to search formulae...";
        return new Markup($"[{Ui.Yellow} bold] {icon} Search: [/][{Ui.White}]{Markup.Escape(display)}[/][{Ui.Yellow}]|[/]\n");
    }

    private IRenderable RenderTable(bool focused)
    {
        var items = CurrentItems;

        if (items.Count == 0)
        {
            if (_mode == Mode.Search)
                return Ui.Muted(_searching ? "Searching..." : _query.Length > 0 ? "No formulae found." : "Type to search.");
            return Ui.Muted("No formulae installed.");
        }

        int windowHeight;
        try { windowHeight = Console.WindowHeight; } catch { windowHeight = 40; }
        int visibleCount = Math.Min(items.Count, Math.Max(5, windowHeight - 14));

        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        else if (_selectedIndex >= _scrollOffset + visibleCount)
            _scrollOffset = _selectedIndex - visibleCount + 1;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, items.Count - visibleCount));
        int endIndex = Math.Min(_scrollOffset + visibleCount, items.Count);

        Table table = _mode == Mode.Search
            ? Ui.StyledTable("", "Name", "Version", "Status")
            : Ui.StyledTable("", "Name", "Version");

        for (int i = _scrollOffset; i < endIndex; i++)
        {
            BrewPackage pkg = items[i];
            bool selected = focused && i == _selectedIndex;
            string pointer = selected ? ">" : " ";
            string style = selected ? $"{Ui.Yellow} bold" : Ui.White;
            string version = pkg.IsInstalled ? (pkg.InstalledVersion ?? "-") : (pkg.LatestVersion ?? "-");

            if (_mode == Mode.Search)
            {
                string statusColor = pkg.IsInstalled ? Ui.Green : Ui.Blue;
                string statusText = pkg.IsInstalled ? "Installed" : "Available";
                table.AddRow(
                    new Markup($"[{style}]{pointer}[/]"),
                    new Markup($"[{style}]{Markup.Escape(pkg.Name)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(version)}[/]"),
                    new Markup($"[{statusColor}]{statusText}[/]"));
            }
            else
            {
                table.AddRow(
                    new Markup($"[{style}]{pointer}[/]"),
                    new Markup($"[{style}]{Markup.Escape(pkg.Name)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(version)}[/]"));
            }
        }

        return table;
    }

    public string GetStatusHints()
    {
        if (!BrewService.IsInstalled())
            return "r:Refresh  Esc:Back  (install Homebrew to manage packages)";
        if (_mode == Mode.Search)
            return "type:Search  up/down:Navigate  Enter:Install  Esc:Cancel";
        return "up/down:Navigate  /:Search  u:Uninstall  r:Refresh  Esc:Back";
    }

    public Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (!BrewService.IsInstalled())
        {
            if (key.Key is ConsoleKey.R) { Refresh(); return Task.FromResult(KeyResult.Handled); }
            if (key.Key is ConsoleKey.Escape or ConsoleKey.Q) return Task.FromResult(KeyResult.Quit);
            return Task.FromResult(KeyResult.NotHandled);
        }

        return Task.FromResult(_mode == Mode.Search ? HandleSearchKey(key) : HandleListKey(key));
    }

    private KeyResult HandleListKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_installed.Count > 0) _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_installed.Count > 0) _selectedIndex = Math.Min(_installed.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.Oem2: // "/"
                EnterSearchMode();
                return KeyResult.Handled;

            case ConsoleKey.U:
                RequestUninstall();
                return KeyResult.Handled;

            case ConsoleKey.R:
                Refresh();
                return KeyResult.Handled;

            case ConsoleKey.Escape or ConsoleKey.Q:
                return KeyResult.Quit;

            default:
                // "/" sometimes arrives only as a char depending on layout
                if (key.KeyChar == '/') { EnterSearchMode(); return KeyResult.Handled; }
                return KeyResult.NotHandled;
        }
    }

    private KeyResult HandleSearchKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Escape:
                ExitSearchMode();
                return KeyResult.Handled;

            case ConsoleKey.Backspace:
                if (_query.Length > 0)
                {
                    _query = _query[..^1];
                    TriggerDebouncedSearch();
                }
                return KeyResult.Handled;

            case ConsoleKey.UpArrow:
                if (_results.Count > 0) _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow:
                if (_results.Count > 0) _selectedIndex = Math.Min(_results.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.Enter:
                RequestInstall();
                return KeyResult.Handled;

            default:
                if (key.KeyChar is >= ' ' and <= '~')
                {
                    _query += key.KeyChar;
                    TriggerDebouncedSearch();
                    return KeyResult.Handled;
                }
                return KeyResult.NotHandled;
        }
    }

    private void EnterSearchMode()
    {
        _mode = Mode.Search;
        _query = "";
        _results = [];
        _selectedIndex = 0;
        _scrollOffset = 0;
        _statusMessage = null;
    }

    private void ExitSearchMode()
    {
        CancelSearch();
        _mode = Mode.List;
        _query = "";
        _results = [];
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _installed.Count - 1));
        _scrollOffset = 0;
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
        _searchCts?.Cancel();
        _searching = false;
        _searchCts = new CancellationTokenSource();
        _hasPendingSearch = true;
        var token = _searchCts.Token;
        string query = _query;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
                if (token.IsCancellationRequested) return;

                if (string.IsNullOrWhiteSpace(query))
                {
                    _results = [];
                    _searching = false;
                    _hasPendingSearch = false;
                    return;
                }

                _searching = true;
                var results = await BrewService.SearchAsync(query, token);
                if (!token.IsCancellationRequested)
                {
                    _results = results;
                    _selectedIndex = 0;
                    _scrollOffset = 0;
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
        var items = CurrentItems;
        if (items.Count == 0 || _selectedIndex >= items.Count) return;

        BrewPackage pkg = items[_selectedIndex];
        if (pkg.IsInstalled)
        {
            _statusMessage = $"{pkg.Name} is already installed.";
            return;
        }

        var (command, args) = BrewService.InstallCommand(pkg.Name);
        PendingCommand = (command, args, null);
    }

    private void RequestUninstall()
    {
        if (_installed.Count == 0 || _selectedIndex >= _installed.Count) return;

        BrewPackage pkg = _installed[_selectedIndex];
        var (command, args) = BrewService.UninstallCommand(pkg.Name);
        PendingCommand = (command, args, null);
    }

    private static IRenderable RenderPanel(bool focused, IRenderable content)
    {
        string focusIndicator = focused ? $"[{Ui.Green} bold]●[/] " : $"[{Ui.Gray}]○[/] ";
        return new Panel(content)
            .Header($"{focusIndicator}[{Ui.Yellow} bold]\U0001f37a Homebrew[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }
}
