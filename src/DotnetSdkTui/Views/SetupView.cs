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
    internal (string Command, string Args, string? Note)? PendingCommand { get; private set; }

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
            DotnetUpService.RefreshPath();
            _isInstalled = DotnetUpService.IsInstalled();
            if (_isInstalled)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    _info = await DotnetUpService.GetInfoAsync(cts.Token);
                }
                catch (OperationCanceledException)
                {
                    // dotnetup --info timed out (likely first-run downloading dotnet)
                    // Show as installed without detailed info
                    _info = null;
                }
            }
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
            return Ui.ViewPanel(Ui.IconSetup, "Setup", Ui.Info("Checking dotnetup..."), focused, shadow: false);

        IRenderable content;

        if (_isInstalled && _info is not null)
        {
            string line = $"[{Ui.Green} bold]dotnetup[/] [{Ui.White}]v{Markup.Escape(_info.Version)}[/]" +
                          $"  [{Ui.Gray}]{Markup.Escape(_info.Architecture)}[/]" +
                          $"  [{Ui.Gray}]{Markup.Escape(_info.Rid)}[/]" +
                          $"  [{Ui.Gray}]{Markup.Escape(_info.Commit[..Math.Min(7, _info.Commit.Length)])}[/]";

            if (_statusMessage is not null)
                line += $"  [{Ui.Gold}]{Markup.Escape(_statusMessage)}[/]";
            if (_error is not null)
                line += $"  [{ThemeManager.ErrorColor}]{Markup.Escape(_error)}[/]";

            content = new Markup(line);
        }
        else if (_isInstalled)
        {
            content = new Markup($"[{Ui.Green}]dotnetup installed[/]");
        }
        else
        {
            string msg = $"[{ThemeManager.ErrorColor}]dotnetup not found[/]  [{Ui.Gray}]Press 'i' to install[/]";
            if (_error is not null)
                msg += $"  [{ThemeManager.ErrorColor}]{Markup.Escape(_error)}[/]";
            content = new Markup(msg);
        }

        return Ui.ViewPanel(Ui.IconSetup, "Setup", content, focused, shadow: false);
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
            PendingCommand = ("powershell", "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"", null);
        else
            PendingCommand = ("bash", "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"", null);
    }

    private void RequestUpdate()
    {
        if (OperatingSystem.IsWindows())
            PendingCommand = ("powershell", "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"", null);
        else
            PendingCommand = ("bash", "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"", null);
    }
}
