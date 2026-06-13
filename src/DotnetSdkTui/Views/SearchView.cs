using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Provides a live-search interface for discovering available .NET SDK versions.
/// Search results update automatically as the user types (with 300ms debounce).
/// </summary>
public sealed class SearchView : IView
{
    /// <inheritdoc />
    public string Name => "Search";

    /// <inheritdoc />
    public string Icon => "★";

    private string _searchQuery = "";
    private List<AvailableSdk> _results = [];
    private bool _searching;
    private string? _error;
    private int _selectedIndex;
    private bool _inputActive = true;
    private CancellationTokenSource? _debounceCts;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <inheritdoc />
    public bool NeedsLiveUpdate => _searching;

    /// <inheritdoc />
    public bool IsTextInputActive => _inputActive;

    /// <inheritdoc />
    public Task ActivateAsync()
    {
        _inputActive = true;
        return Task.CompletedTask;
    }

    public IRenderable Render(bool focused)
    {
        var parts = new List<IRenderable>();

        // Search input
        string cursor = focused && _inputActive ? "▌" : "";
        string inputDisplay = _searchQuery.Length > 0 ? _searchQuery : (focused && _inputActive ? "" : "type to search...");
        parts.Add(new Markup($"[{MarioTheme.Yellow} bold]🔍 Search:[/] [{MarioTheme.White}]{Markup.Escape(inputDisplay)}{cursor}[/]"));

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

            int maxRows = Math.Min(_results.Count, 10);
            for (int i = 0; i < maxRows; i++)
            {
                var sdk = _results[i];
                bool selected = focused && !_inputActive && i == _selectedIndex;
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
            if (_results.Count > maxRows)
                parts.Add(MarioTheme.Muted($"Showing {maxRows} of {_results.Count} results"));
            else
                parts.Add(MarioTheme.Muted($"{_results.Count} result(s)"));
        }
        else if (_searchQuery.Length == 0)
        {
            parts.Add(MarioTheme.Muted("Type a version (10.0, 9.0) or keyword (latest, lts, preview)."));
        }
        else
        {
            parts.Add(MarioTheme.Muted("No results found."));
        }

        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        return new Panel(new Rows(parts))
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]★ Search[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }

    public string GetStatusHints()
    {
        if (_searching) return "Searching...";
        if (_inputActive) return "Type to search (live)  Tab:Results  Esc:Clear";
        return "↑↓:Navigate  i:Install  Tab:Input  Esc:Back";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_searching && key.Key != ConsoleKey.Escape) return KeyResult.NotHandled;

        if (_inputActive)
        {
            return await HandleInputModeAsync(key);
        }

        return await HandleResultsModeAsync(key);
    }

    private async Task<KeyResult> HandleInputModeAsync(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Tab:
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
                return KeyResult.Handled;

            case ConsoleKey.DownArrow:
                if (_results.Count > 0)
                {
                    _inputActive = false;
                    _selectedIndex = 0;
                }
                return KeyResult.Handled;

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
                {
                    if (_selectedIndex == 0)
                    {
                        _inputActive = true;
                    }
                    else
                    {
                        _selectedIndex = Math.Max(0, _selectedIndex - 1);
                    }
                }
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_results.Count > 0)
                    _selectedIndex = Math.Min(Math.Min(_results.Count, 10) - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.Tab:
                _inputActive = true;
                return KeyResult.Handled;

            case ConsoleKey.I:
                await InstallSelectedAsync();
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                _inputActive = true;
                return KeyResult.Handled;

            default:
                // Allow typing to go back to input mode
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

    private void TriggerDebouncedSearch()
    {
        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
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
            catch (OperationCanceledException)
            {
                // Debounce cancelled — expected
            }
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
                    _searching = false;
            }
        });
    }

    private async Task InstallSelectedAsync()
    {
        if (_results.Count == 0 || _selectedIndex >= _results.Count) return;

        var sdk = _results[_selectedIndex];
        if (!DotnetUpService.IsInstalled())
        {
            _error = "dotnetup not found. Install it first (F4 to focus Setup).";
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
