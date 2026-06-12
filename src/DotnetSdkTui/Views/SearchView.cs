using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

public sealed class SearchView : IView
{
    public string Name => "Search";
    public string Icon => "★";
    public bool InputMode { get; private set; } = true;

    private string _searchQuery = "";
    private List<AvailableSdk> _results = [];
    private bool _searching;
    private string? _error;
    private int _selectedIndex;

    public Task ActivateAsync()
    {
        InputMode = true;
        _searchQuery = "";
        _results = [];
        _error = null;
        _selectedIndex = 0;
        return Task.CompletedTask;
    }

    public IRenderable Render()
    {
        var parts = new List<IRenderable>();

        // Search input
        string cursor = InputMode ? "▌" : "";
        string inputDisplay = _searchQuery.Length > 0 ? _searchQuery : (InputMode ? "" : "(press / to search)");
        parts.Add(new Markup($"[{MarioTheme.Yellow} bold]Search:[/] [{MarioTheme.White}]{Markup.Escape(inputDisplay)}{cursor}[/]"));
        parts.Add(Text.Empty);

        if (_searching)
        {
            parts.Add(MarioTheme.Info("Searching..."));
        }
        else if (_error is not null)
        {
            parts.Add(MarioTheme.Error(_error));
        }
        else if (_results.Count > 0)
        {
            var table = MarioTheme.StyledTable("", "Version", "Channel", "Support", "Latest");

            for (int i = 0; i < _results.Count; i++)
            {
                var sdk = _results[i];
                bool selected = i == _selectedIndex;
                string pointer = selected ? "►" : " ";
                string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;
                string latest = sdk.IsLatest ? "★" : "";

                table.AddRow(
                    new Markup($"[{style}]{pointer}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.Version)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.ChannelVersion)}[/]"),
                    new Markup($"[{style}]{Markup.Escape(sdk.SupportPhase)}[/]"),
                    new Markup($"[{MarioTheme.Gold}]{latest}[/]"));
            }

            parts.Add(table);
            parts.Add(new Markup($"[{MarioTheme.Gray}]{_results.Count} result(s)[/]"));
        }
        else if (!InputMode)
        {
            parts.Add(MarioTheme.Muted("No results. Try a version number like 10.0, or 'latest', 'lts'."));
        }
        else
        {
            parts.Add(MarioTheme.Muted("Type a version (e.g. 10.0, 9.0) or keyword (latest, lts, preview) and press Enter."));
        }

        return MarioTheme.ContentPanel("Search SDKs", new Rows(parts));
    }

    public string GetStatusHints()
    {
        if (_searching) return "Searching...";
        if (InputMode) return "Type query  Enter:Search  Esc:Clear";
        return "↑↓:Navigate  i:Install  /:New search  Esc:Back";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_searching) return KeyResult.NotHandled;

        if (InputMode)
        {
            return await HandleInputModeAsync(key);
        }

        return await HandleResultsModeAsync(key);
    }

    private async Task<KeyResult> HandleInputModeAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Enter:
                if (_searchQuery.Length > 0)
                {
                    InputMode = false;
                    _searching = true;
                    _ = SearchAsync();
                }
                return KeyResult.Handled;

            case ConsoleKey.Backspace:
                if (_searchQuery.Length > 0)
                    _searchQuery = _searchQuery[..^1];
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                _searchQuery = "";
                return KeyResult.Handled;

            default:
                if (key.KeyChar is >= ' ' and <= '~')
                {
                    _searchQuery += key.KeyChar;
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
                    _selectedIndex = Math.Min(_results.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.I:
                await InstallSelectedAsync();
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                InputMode = true;
                _searchQuery = "";
                _results = [];
                _selectedIndex = 0;
                return KeyResult.Handled;

            case ConsoleKey.Oem2: // '/' key
                InputMode = true;
                _searchQuery = "";
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private async Task SearchAsync()
    {
        try
        {
            _results = await SdkSearchService.SearchAvailableSdksAsync(_searchQuery);
            _selectedIndex = 0;
            _error = null;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            _results = [];
        }
        finally
        {
            _searching = false;
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (_results.Count == 0 || _selectedIndex >= _results.Count) return;

        var sdk = _results[_selectedIndex];
        if (!DotnetUpService.IsInstalled())
        {
            _error = "dotnetup not found. Go to Setup (4) to install it first.";
            return;
        }

        _searching = true;
        try
        {
            var result = await DotnetUpService.InstallSdkAsync(sdk.ChannelVersion);
            _error = result.ExitCode == 0
                ? null
                : $"Install failed (exit code {result.ExitCode}).";
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
        finally
        {
            _searching = false;
        }
    }
}
