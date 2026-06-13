using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Compact one-liner for dotnetup status. Manages install/update via interactive commands.
/// </summary>
public sealed class SetupView : IView
{
    public string Name => "Setup";
    public string Icon => " ";

    private bool _loading;
    private string? _error;
    private string? _statusMessage;
    private bool _isInstalled;
    private DotnetUpInfo? _info;

    /// <summary>Set by install/update to signal App to run a command interactively.</summary>
    internal (string Command, string Args)? PendingCommand { get; private set; }

    public bool NeedsLiveUpdate => _loading;
    public bool IsTextInputActive => false;

    public Task ActivateAsync()
    {
        if (!_loading && _info is null && !_isInstalled)
        {
            _loading = true;
            _error = null;
            _statusMessage = null;
            _ = LoadAsync();
        }
        return Task.CompletedTask;
    }

    internal void ClearPendingCommand() => PendingCommand = null;

    internal void Refresh()
    {
        _loading = true;
        _error = null;
        _statusMessage = null;
        _info = null;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            _isInstalled = DotnetUpService.IsInstalled();
            if (_isInstalled)
                _info = await DotnetUpService.GetInfoAsync();
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
        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";

        if (_loading)
        {
            return new Panel(MarioTheme.Info("Checking dotnetup..."))
                .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]Setup[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
                .Expand();
        }

        IRenderable content;

        if (_isInstalled && _info is not null)
        {
            string line = $"[{MarioTheme.Green} bold]dotnetup[/] [{MarioTheme.White}]v{Markup.Escape(_info.Version)}[/]" +
                          $"  [{MarioTheme.Gray}]{Markup.Escape(_info.Architecture)}[/]" +
                          $"  [{MarioTheme.Gray}]{Markup.Escape(_info.Rid)}[/]" +
                          $"  [{MarioTheme.Gray}]{Markup.Escape(_info.Commit[..Math.Min(7, _info.Commit.Length)])}[/]";

            if (_statusMessage is not null)
                line += $"  [{MarioTheme.Gold}]{Markup.Escape(_statusMessage)}[/]";
            if (_error is not null)
                line += $"  [{ThemeManager.ErrorColor}]{Markup.Escape(_error)}[/]";

            content = new Markup(line);
        }
        else if (_isInstalled)
        {
            content = new Markup($"[{MarioTheme.Green}]dotnetup installed[/]");
        }
        else
        {
            string msg = $"[{ThemeManager.ErrorColor}]dotnetup not found[/]  [{MarioTheme.Gray}]Press 'i' to install[/]";
            if (_error is not null)
                msg += $"  [{ThemeManager.ErrorColor}]{Markup.Escape(_error)}[/]";
            content = new Markup(msg);
        }

        return new Panel(content)
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]Setup[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }

    public string GetStatusHints()
    {
        return _isInstalled ? "u:Update dotnetup  r:Refresh" : "i:Install dotnetup  r:Refresh";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_loading) return KeyResult.NotHandled;

        switch (key.Key)
        {
            case ConsoleKey.I when !_isInstalled:
                RequestInstall();
                return KeyResult.Handled;

            case ConsoleKey.U when _isInstalled:
                RequestUpdate();
                return KeyResult.Handled;

            case ConsoleKey.R:
                Refresh();
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private void RequestInstall()
    {
        if (OperatingSystem.IsWindows())
            PendingCommand = ("powershell", "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"");
        else
            PendingCommand = ("bash", "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"");
    }

    private void RequestUpdate()
    {
        if (OperatingSystem.IsWindows())
            PendingCommand = ("powershell", "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"");
        else
            PendingCommand = ("bash", "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"");
    }
}
