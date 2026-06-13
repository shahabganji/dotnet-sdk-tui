using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Displays installed .NET runtimes in a table panel.
/// </summary>
public sealed class RuntimesView : IView
{
    public string Name => "Runtimes";
    public string Icon => ">";

    private List<SdkInfo> _runtimes = [];
    private bool _loading;
    private string? _error;
    private int _selectedIndex;

    public bool NeedsLiveUpdate => _loading;
    public bool IsTextInputActive => false;

    public Task ActivateAsync()
    {
        if (_runtimes.Count == 0 && !_loading)
        {
            _loading = true;
            _error = null;
            _ = LoadAsync();
        }
        return Task.CompletedTask;
    }

    internal void Refresh()
    {
        _loading = true;
        _error = null;
        _runtimes = [];
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var installed = await DotnetUpService.ListInstalledAsync();
            _runtimes = installed
                .Where(s => !string.Equals(s.Component, "SDK", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => s.Version)
                .ToList();
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _runtimes.Count - 1));
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
        if (_loading)
            return RenderPanel(focused, MarioTheme.Info("Loading runtimes..."));

        if (_error is not null)
            return RenderPanel(focused, MarioTheme.Error(_error));

        if (_runtimes.Count == 0)
            return RenderPanel(focused, MarioTheme.Muted("No runtimes found."));

        var table = MarioTheme.StyledTable("", "Component", "Version", "Arch");

        for (int i = 0; i < _runtimes.Count; i++)
        {
            var rt = _runtimes[i];
            bool selected = focused && i == _selectedIndex;
            string pointer = selected ? ">" : " ";
            string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;
            string arch = rt.Architecture.Length > 0
                ? rt.Architecture
                : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant();

            table.AddRow(
                new Markup($"[{style}]{pointer}[/]"),
                new Markup($"[{style}]{Markup.Escape(rt.DisplayComponent)}[/]"),
                new Markup($"[{style}]{Markup.Escape(rt.Version)}[/]"),
                new Markup($"[{style}]{Markup.Escape(arch)}[/]"));
        }

        return RenderPanel(focused, table);
    }

    public string GetStatusHints()
    {
        return "up/down:Navigate  r:Refresh";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_loading) return KeyResult.NotHandled;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_runtimes.Count > 0)
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_runtimes.Count > 0)
                    _selectedIndex = Math.Min(_runtimes.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.R:
                _loading = true;
                _error = null;
                _runtimes = [];
                _ = LoadAsync();
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private static IRenderable RenderPanel(bool focused, IRenderable content)
    {
        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        return new Panel(content)
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]> Runtimes[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }
}
