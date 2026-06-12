using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

public sealed class SetupView : IView
{
    public string Name => "Setup";
    public string Icon => "●";

    private bool _loading;
    private bool _actionRunning;
    private string? _error;
    private string? _statusMessage;
    private bool _isInstalled;
    private DotnetUpInfo? _info;

    public bool IsLoading => _loading || _actionRunning;

    public Task ActivateAsync()
    {
        _loading = true;
        _error = null;
        _statusMessage = null;
        _ = LoadAsync();
        return Task.CompletedTask;
    }

    private async Task LoadAsync()
    {
        try
        {
            _isInstalled = DotnetUpService.IsInstalled();

            if (_isInstalled)
            {
                _info = await DotnetUpService.GetInfoAsync();
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

    public IRenderable Render()
    {
        if (_loading)
            return MarioTheme.ContentPanel("dotnetup Setup", MarioTheme.Info("Checking dotnetup..."));

        var parts = new List<IRenderable>();

        if (_isInstalled)
        {
            parts.Add(MarioTheme.Success("dotnetup is installed!"));
            parts.Add(Text.Empty);

            if (_info is not null)
            {
                var infoTable = MarioTheme.StyledTable("Property", "Value");
                infoTable.AddRow(
                    new Markup($"[{MarioTheme.Yellow}]Version[/]"),
                    new Markup($"[{MarioTheme.White}]{Markup.Escape(_info.Version)}[/]"));
                infoTable.AddRow(
                    new Markup($"[{MarioTheme.Yellow}]Commit[/]"),
                    new Markup($"[{MarioTheme.White}]{Markup.Escape(_info.Commit)}[/]"));
                infoTable.AddRow(
                    new Markup($"[{MarioTheme.Yellow}]Architecture[/]"),
                    new Markup($"[{MarioTheme.White}]{Markup.Escape(_info.Architecture)}[/]"));
                infoTable.AddRow(
                    new Markup($"[{MarioTheme.Yellow}]RID[/]"),
                    new Markup($"[{MarioTheme.White}]{Markup.Escape(_info.Rid)}[/]"));
                parts.Add(infoTable);
            }
            else
            {
                parts.Add(MarioTheme.Muted("Could not retrieve dotnetup info (--info --json may not be supported)."));
            }

            parts.Add(Text.Empty);
            parts.Add(new Markup($"[{MarioTheme.Blue}]u[/]:[{MarioTheme.White}]Update dotnetup[/]  " +
                                  $"[{MarioTheme.Blue}]r[/]:[{MarioTheme.White}]Refresh[/]"));
        }
        else
        {
            parts.Add(MarioTheme.Error("dotnetup is not installed."));
            parts.Add(Text.Empty);
            parts.Add(MarioTheme.Info("dotnetup is the official .NET SDK acquisition tool."));
            parts.Add(MarioTheme.Muted("It manages SDK installations, channels, and updates."));
            parts.Add(Text.Empty);

            string installCmd = OperatingSystem.IsWindows()
                ? "iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex"
                : "curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash";

            parts.Add(new Markup($"[{MarioTheme.Yellow}]Install command:[/]"));
            parts.Add(new Markup($"[{MarioTheme.Gray}]  {Markup.Escape(installCmd)}[/]"));
            parts.Add(Text.Empty);
            parts.Add(new Markup($"[{MarioTheme.Gold}]Press 'i' to install dotnetup now.[/]"));
        }

        if (_error is not null)
        {
            parts.Add(Text.Empty);
            parts.Add(MarioTheme.Error(_error));
        }

        if (_statusMessage is not null)
        {
            parts.Add(Text.Empty);
            parts.Add(MarioTheme.Coin(_statusMessage));
        }

        return MarioTheme.ContentPanel("dotnetup Setup", new Rows(parts));
    }

    public string GetStatusHints()
    {
        if (_actionRunning) return "Running...";
        return _isInstalled ? "u:Update  r:Refresh" : "i:Install dotnetup  r:Refresh";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_actionRunning || _loading) return KeyResult.NotHandled;

        switch (key.Key)
        {
            case ConsoleKey.I when !_isInstalled:
                await InstallDotnetUpAsync();
                return KeyResult.Handled;

            case ConsoleKey.U when _isInstalled:
                await UpdateDotnetUpAsync();
                return KeyResult.Handled;

            case ConsoleKey.R:
                _loading = true;
                _error = null;
                _statusMessage = null;
                _ = LoadAsync();
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private async Task InstallDotnetUpAsync()
    {
        _actionRunning = true;
        _statusMessage = "Installing dotnetup...";

        try
        {
            var result = await DotnetUpService.InstallDotnetUpAsync();
            if (result.ExitCode == 0)
            {
                _statusMessage = "dotnetup installed successfully!";
                _isInstalled = true;
                _ = LoadAsync();
            }
            else
            {
                _statusMessage = $"Installation failed (exit code {result.ExitCode}).";
                _error = result.Error.Length > 200 ? result.Error[..200] + "..." : result.Error;
            }
        }
        catch (Exception ex)
        {
            _statusMessage = "Installation failed.";
            _error = ex.Message;
        }
        finally
        {
            _actionRunning = false;
        }
    }

    private async Task UpdateDotnetUpAsync()
    {
        _actionRunning = true;
        _statusMessage = "Updating dotnetup...";

        try
        {
            var result = await DotnetUpService.UpdateAllAsync();
            _statusMessage = result.ExitCode == 0
                ? $"Update completed ({result.Duration.TotalSeconds:F1}s)."
                : $"Update failed (exit code {result.ExitCode}).";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Update error: {ex.Message}";
        }
        finally
        {
            _actionRunning = false;
            _ = LoadAsync();
        }
    }
}
