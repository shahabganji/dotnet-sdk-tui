using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

public sealed class SdksView : IView
{
    private sealed record SdkRow(
        string Channel,
        string LatestVersion,
        string SupportPhase,
        string EolDate,
        bool IsInstalled,
        string InstalledVersion,
        string Architecture,
        string Description);

    public string Name => "SDKs";
    public string Icon => "🍄";

    private List<SdkRow> _rows = [];
    private bool _loading;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;
    private bool _actionRunning;
    private readonly List<string> _outputLines = [];

    public bool NeedsLiveUpdate => _loading || _actionRunning;
    public bool IsTextInputActive => false;

    public Task ActivateAsync()
    {
        if (_rows.Count == 0)
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
            var installedTask = DotnetUpService.ListInstalledAsync();
            var channelsTask = SdkSearchService.GetChannelsAsync();

            await Task.WhenAll(installedTask, channelsTask);

            List<SdkInfo> installed = installedTask.Result;
            var channels = channelsTask.Result;

            var rows = new List<SdkRow>();

            foreach (var channel in channels)
            {
                if (string.IsNullOrWhiteSpace(channel.LatestSdk))
                    continue;

                var matchingInstalled = installed.FirstOrDefault(s =>
                    s.Version.StartsWith(channel.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                bool isEol = string.Equals(channel.SupportPhase, "eol", StringComparison.OrdinalIgnoreCase);
                string eolDate = channel.EolDate ?? "-";
                string description = GetChannelDescription(channel.ChannelVersion, channel.SupportPhase);

                // Show non-EOL channels, or EOL channels that are installed
                if (!isEol || matchingInstalled is not null)
                {
                    rows.Add(new SdkRow(
                        channel.ChannelVersion,
                        channel.LatestSdk,
                        FormatSupportPhase(channel.SupportPhase),
                        eolDate,
                        matchingInstalled is not null,
                        matchingInstalled?.Version ?? "-",
                        matchingInstalled?.Architecture ?? "-",
                        description));
                }
            }

            // Add installed SDKs not matched to any known channel
            foreach (var sdk in installed)
            {
                bool alreadyListed = rows.Any(r =>
                    sdk.Version.StartsWith(r.Channel, StringComparison.OrdinalIgnoreCase));

                if (!alreadyListed)
                {
                    string channelGuess = GuessChannel(sdk.Version);
                    rows.Add(new SdkRow(
                        channelGuess,
                        sdk.Version,
                        "Installed",
                        "-",
                        true,
                        sdk.Version,
                        sdk.Architecture,
                        $".NET {channelGuess} SDK (locally installed)"));
                }
            }

            _rows = rows;
            _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _rows.Count - 1));
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
            return MarioTheme.SectionPanel("🍄 SDKs", MarioTheme.Info("Loading SDKs..."));

        if (_error is not null)
            return MarioTheme.SectionPanel("🍄 SDKs", MarioTheme.Error(_error));

        if (_rows.Count == 0)
            return MarioTheme.SectionPanel("🍄 SDKs", MarioTheme.Coin("No SDK channels found."));

        var parts = new List<IRenderable>();

        var table = MarioTheme.StyledTable("", "Channel", "Latest", "Status", "Installed", "Support", "EOL");

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool selected = focused && i == _selectedIndex;
            string pointer = selected ? "►" : " ";
            string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;

            string statusIcon;
            string statusColor;
            string statusText;
            if (row.IsInstalled)
            {
                bool hasUpdate = !string.Equals(row.InstalledVersion, row.LatestVersion, StringComparison.OrdinalIgnoreCase)
                    && row.InstalledVersion != "-";
                if (hasUpdate)
                {
                    statusIcon = "⬆";
                    statusColor = ThemeManager.MarioYellow;
                    statusText = "Update";
                }
                else
                {
                    statusIcon = "✓";
                    statusColor = MarioTheme.Green;
                    statusText = "Installed";
                }
            }
            else
            {
                statusIcon = "✗";
                statusColor = MarioTheme.Blue;
                statusText = "Available";
            }

            table.AddRow(
                new Markup($"[{style}]{pointer}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Channel)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.LatestVersion)}[/]"),
                new Markup($"[{statusColor} bold]{statusIcon} {statusText}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.InstalledVersion)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.SupportPhase)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.EolDate)}[/]"));
        }

        parts.Add(table);

        // Show selected SDK description
        if (focused && _selectedIndex < _rows.Count)
        {
            var selectedRow = _rows[_selectedIndex];
            parts.Add(new Markup($"\n[{MarioTheme.Gray}]{Markup.Escape(selectedRow.Description)}[/]"));
        }

        // Output from install/uninstall operations
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

        if (_statusMessage is not null)
            parts.Add(new Markup($"\n[{MarioTheme.Gold}]{Markup.Escape(_statusMessage)}[/]"));

        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        var panel = new Panel(new Rows(parts))
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]🍄 SDKs[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();

        return panel;
    }

    public string GetStatusHints()
    {
        if (_actionRunning) return "Running...";
        return "↑↓:Navigate  i:Install  u:Uninstall  p:Update  r:Refresh";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_actionRunning || _loading) return KeyResult.NotHandled;

        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_rows.Count > 0)
                    _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_rows.Count > 0)
                    _selectedIndex = Math.Min(_rows.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.I:
                await InstallSelectedAsync();
                return KeyResult.Handled;

            case ConsoleKey.U:
                await UninstallSelectedAsync();
                return KeyResult.Handled;

            case ConsoleKey.P:
                await UpdateSelectedAsync();
                return KeyResult.Handled;

            case ConsoleKey.R:
                await RefreshAsync();
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    private async Task InstallSelectedAsync()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        var row = _rows[_selectedIndex];
        if (row.IsInstalled)
        {
            _statusMessage = $"Channel {row.Channel} is already installed.";
            return;
        }

        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Install it first (F4 to focus Setup).";
            return;
        }

        _actionRunning = true;
        _outputLines.Clear();
        _statusMessage = $"Installing SDK channel {row.Channel}...";

        try
        {
            var result = await ProcessRunner.RunWithCallbackAsync(
                "dotnetup", $"sdk install {row.Channel}",
                line => _outputLines.Add(line),
                errLine => _outputLines.Add($"ERR|{errLine}"));

            _statusMessage = result.ExitCode == 0
                ? $"Installed {row.Channel} successfully! ({result.Duration.TotalSeconds:F1}s)"
                : $"Install failed (exit code {result.ExitCode}).";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Install error: {ex.Message}";
        }
        finally
        {
            _actionRunning = false;
            _ = LoadAsync();
        }
    }

    private async Task UninstallSelectedAsync()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        var row = _rows[_selectedIndex];
        if (!row.IsInstalled)
        {
            _statusMessage = $"Channel {row.Channel} is not installed.";
            return;
        }

        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Cannot uninstall without dotnetup.";
            return;
        }

        _actionRunning = true;
        _outputLines.Clear();
        _statusMessage = $"Uninstalling SDK channel {row.Channel}...";

        try
        {
            var result = await ProcessRunner.RunWithCallbackAsync(
                "dotnetup", $"sdk uninstall {row.Channel}",
                line => _outputLines.Add(line),
                errLine => _outputLines.Add($"ERR|{errLine}"));

            _statusMessage = result.ExitCode == 0
                ? $"Uninstalled {row.Channel}. ({result.Duration.TotalSeconds:F1}s)"
                : $"Uninstall failed (exit code {result.ExitCode}).";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Uninstall error: {ex.Message}";
        }
        finally
        {
            _actionRunning = false;
            _ = LoadAsync();
        }
    }

    private async Task UpdateSelectedAsync()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        var row = _rows[_selectedIndex];
        if (!row.IsInstalled)
        {
            _statusMessage = $"Channel {row.Channel} is not installed. Use 'i' to install.";
            return;
        }

        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Cannot update without dotnetup.";
            return;
        }

        _actionRunning = true;
        _outputLines.Clear();
        _statusMessage = $"Updating SDK channel {row.Channel}...";

        try
        {
            var result = await ProcessRunner.RunWithCallbackAsync(
                "dotnetup", $"sdk install {row.Channel}",
                line => _outputLines.Add(line),
                errLine => _outputLines.Add($"ERR|{errLine}"));

            _statusMessage = result.ExitCode == 0
                ? $"Updated {row.Channel}! ({result.Duration.TotalSeconds:F1}s)"
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

    private Task RefreshAsync()
    {
        _statusMessage = null;
        _outputLines.Clear();
        _loading = true;
        _error = null;
        _ = LoadAsync();
        return Task.CompletedTask;
    }

    private static string FormatSupportPhase(string phase) =>
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

    private static string GuessChannel(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }

    private static string GetChannelDescription(string channel, string supportPhase)
    {
        string phaseDesc = supportPhase.ToLowerInvariant() switch
        {
            "active" => "Actively supported with updates and patches.",
            "maintenance" => "Maintenance mode — security fixes only.",
            "preview" => "Preview release — not for production use.",
            "go-live" => "Go-Live — production supported preview.",
            "rc" => "Release Candidate — final preview before GA.",
            "eol" => "End of Life — no longer supported.",
            _ => ""
        };

        return $".NET {channel} SDK — {phaseDesc}";
    }
}
