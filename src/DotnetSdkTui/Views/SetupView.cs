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
    private readonly List<string> _outputLines = [];

    public bool NeedsLiveUpdate => _loading || _actionRunning;
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

    public IRenderable Render(bool focused)
    {
        if (_loading)
        {
            string focusInd = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
            return new Panel(MarioTheme.Info("Checking dotnetup..."))
                .Header($"{focusInd}[{MarioTheme.Yellow} bold]● Setup[/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
                .Expand();
        }

        var parts = new List<IRenderable>();

        if (_isInstalled)
        {
            parts.Add(MarioTheme.Success("dotnetup is installed!"));

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

            parts.Add(new Markup($"[{MarioTheme.Blue}]u[/]:[{MarioTheme.White}]Update dotnetup[/]  " +
                                  $"[{MarioTheme.Blue}]r[/]:[{MarioTheme.White}]Refresh[/]"));
        }
        else
        {
            parts.Add(MarioTheme.Error("dotnetup is not installed."));
            parts.Add(MarioTheme.Info("dotnetup is the official .NET SDK acquisition tool."));

            string installCmd = OperatingSystem.IsWindows()
                ? "iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex"
                : "curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash";

            parts.Add(new Markup($"[{MarioTheme.Yellow}]Install command:[/] [{MarioTheme.Gray}]{Markup.Escape(installCmd)}[/]"));
            parts.Add(new Markup($"[{MarioTheme.Gold}]Press 'i' to install dotnetup now.[/]"));
        }

        // Output from operations (rendered INSIDE the panel)
        if (_outputLines.Count > 0)
        {
            var outputParts = new List<IRenderable>();
            int start = Math.Max(0, _outputLines.Count - 8);
            for (int i = start; i < _outputLines.Count; i++)
            {
                string line = _outputLines[i];
                string color = line.StartsWith("ERR|") ? ThemeManager.OutputError : ThemeManager.OutputText;
                string text = line.StartsWith("ERR|") ? line[4..] : line;
                outputParts.Add(new Markup($"[{color}]{Markup.Escape(text)}[/]"));
            }
            parts.Add(new Panel(new Rows(outputParts))
                .Header($"[{MarioTheme.Brown}] Output [/]")
                .Border(BoxBorder.Rounded)
                .BorderColor(ThemeManager.TableBorderColor)
                .Expand());
        }

        if (_error is not null)
            parts.Add(MarioTheme.Error(_error));

        if (_statusMessage is not null)
            parts.Add(MarioTheme.Coin(_statusMessage));

        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        return new Panel(new Rows(parts))
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]● Setup (dotnetup)[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }

    public string GetStatusHints()
    {
        if (_actionRunning) return "Running...";
        return _isInstalled ? "u:Update dotnetup  r:Refresh" : "i:Install dotnetup  r:Refresh";
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
                await UpdateDotnetUpSelfAsync();
                return KeyResult.Handled;

            case ConsoleKey.R:
                _loading = true;
                _error = null;
                _statusMessage = null;
                _outputLines.Clear();
                _info = null;
                _ = LoadAsync();
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private async Task InstallDotnetUpAsync()
    {
        _actionRunning = true;
        _outputLines.Clear();
        _statusMessage = "Installing dotnetup...";

        try
        {
            string cmd;
            string args;
            if (OperatingSystem.IsWindows())
            {
                cmd = "powershell";
                args = "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"";
            }
            else
            {
                cmd = "bash";
                args = "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"";
            }

            var result = await ProcessRunner.RunWithCallbackAsync(
                cmd, args,
                line => _outputLines.Add(line),
                errLine => _outputLines.Add($"ERR|{errLine}"));

            if (result.ExitCode == 0)
            {
                _statusMessage = "dotnetup installed successfully!";
                _isInstalled = true;
                _loading = true;
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

    private async Task UpdateDotnetUpSelfAsync()
    {
        _actionRunning = true;
        _outputLines.Clear();
        _statusMessage = "Updating dotnetup itself...";

        try
        {
            // Re-run the install script to update dotnetup itself
            string cmd;
            string args;
            if (OperatingSystem.IsWindows())
            {
                cmd = "powershell";
                args = "-Command \"iwr https://aka.ms/dotnetup/get-dotnetup.ps1 | iex\"";
            }
            else
            {
                cmd = "bash";
                args = "-c \"curl -fsSL https://aka.ms/dotnetup/get-dotnetup.sh | bash\"";
            }

            var result = await ProcessRunner.RunWithCallbackAsync(
                cmd, args,
                line => _outputLines.Add(line),
                errLine => _outputLines.Add($"ERR|{errLine}"));

            _statusMessage = result.ExitCode == 0
                ? $"dotnetup updated successfully ({result.Duration.TotalSeconds:F1}s)."
                : $"Update failed (exit code {result.ExitCode}).";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Update error: {ex.Message}";
        }
        finally
        {
            _actionRunning = false;
            _info = null;
            _loading = true;
            _ = LoadAsync();
        }
    }
}
