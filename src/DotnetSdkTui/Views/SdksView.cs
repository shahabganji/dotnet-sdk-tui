using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Displays installed .NET SDKs and available channels with install/uninstall/update actions.
/// Shows each individual SDK version, lifecycle status, and support phase.
/// </summary>
public sealed class SdksView : IView
{
    private sealed record SdkRow(
        string Version,
        string Channel,
        string SupportPhase,
        string EolDate,
        bool IsInstalled,
        string Architecture,
        string LifecycleIcon,
        string Description);

    /// <inheritdoc />
    public string Name => "SDKs";

    /// <inheritdoc />
    public string Icon => "🍄";

    private List<SdkRow> _rows = [];
    private bool _loading;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;
    private bool _actionRunning;
    private readonly List<string> _outputLines = [];

    /// <inheritdoc />
    public bool NeedsLiveUpdate => _loading || _actionRunning;

    /// <inheritdoc />
    public bool IsTextInputActive => false;

    /// <inheritdoc />
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
            var listedVersions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Show each installed SDK version individually
            foreach (var sdk in installed)
            {
                if (!string.Equals(sdk.Component, "SDK", StringComparison.OrdinalIgnoreCase))
                    continue;

                string channel = GuessChannel(sdk.Version);
                var channelInfo = channels.FirstOrDefault(c =>
                    sdk.Version.StartsWith(c.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                string supportPhase = channelInfo is not null
                    ? FormatSupportPhase(channelInfo.SupportPhase)
                    : "Installed";
                string eolDate = channelInfo?.EolDate ?? "-";
                string lifecycleIcon = GetLifecycleIcon(channelInfo?.SupportPhase, channelInfo?.EolDate);
                string description = GetChannelDescription(channel, channelInfo?.SupportPhase ?? "unknown");

                rows.Add(new SdkRow(
                    sdk.Version,
                    channel,
                    supportPhase,
                    eolDate,
                    true,
                    sdk.Architecture.Length > 0 ? sdk.Architecture : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                    lifecycleIcon,
                    description));

                listedVersions.Add(sdk.Version);
            }

            // Show available channels that are not installed (latest version only)
            foreach (var channel in channels)
            {
                if (string.IsNullOrWhiteSpace(channel.LatestSdk))
                    continue;

                bool isEol = string.Equals(channel.SupportPhase, "eol", StringComparison.OrdinalIgnoreCase);
                if (isEol)
                    continue;

                bool hasInstalledVersion = installed.Any(s =>
                    string.Equals(s.Component, "SDK", StringComparison.OrdinalIgnoreCase)
                    && s.Version.StartsWith(channel.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                if (!hasInstalledVersion)
                {
                    string lifecycleIcon = GetLifecycleIcon(channel.SupportPhase, channel.EolDate);
                    rows.Add(new SdkRow(
                        channel.LatestSdk,
                        channel.ChannelVersion,
                        FormatSupportPhase(channel.SupportPhase),
                        channel.EolDate ?? "-",
                        false,
                        "-",
                        lifecycleIcon,
                        GetChannelDescription(channel.ChannelVersion, channel.SupportPhase)));
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

    /// <inheritdoc />
    public IRenderable Render(bool focused)
    {
        if (_loading)
            return RenderPanel(focused, MarioTheme.Info("Loading SDKs..."));

        if (_error is not null)
            return RenderPanel(focused, MarioTheme.Error(_error));

        if (_rows.Count == 0)
            return RenderPanel(focused, MarioTheme.Coin("No SDKs found."));

        var parts = new List<IRenderable>();

        var table = MarioTheme.StyledTable("", "", "Version", "Channel", "Status", "Arch", "Support", "EOL");

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool selected = focused && i == _selectedIndex;
            string pointer = selected ? "►" : " ";
            string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;

            string statusText;
            string statusColor;
            if (row.IsInstalled)
            {
                statusColor = MarioTheme.Green;
                statusText = "✓ Installed";
            }
            else
            {
                statusColor = MarioTheme.Blue;
                statusText = "⬇ Available";
            }

            table.AddRow(
                new Markup($"[{style}]{pointer}[/]"),
                new Markup($"{row.LifecycleIcon}"),
                new Markup($"[{style}]{Markup.Escape(row.Version)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Channel)}[/]"),
                new Markup($"[{statusColor} bold]{statusText}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Architecture)}[/]"),
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

        return RenderPanel(focused, new Rows(parts));
    }

    /// <inheritdoc />
    public string GetStatusHints()
    {
        if (_actionRunning) return "Running...";
        return "↑↓:Navigate  i:Install  u:Uninstall  p:Update  r:Refresh";
    }

    /// <inheritdoc />
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
            _statusMessage = $"{row.Version} is already installed.";
            return;
        }

        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Install it first (F4 to focus Setup).";
            return;
        }

        _actionRunning = true;
        _outputLines.Clear();
        _statusMessage = $"Installing SDK {row.Channel}...";

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
            _loading = true;
            _ = LoadAsync();
        }
    }

    private async Task UninstallSelectedAsync()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        var row = _rows[_selectedIndex];
        if (!row.IsInstalled)
        {
            _statusMessage = $"{row.Version} is not installed.";
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
            _loading = true;
            _ = LoadAsync();
        }
    }

    private async Task UpdateSelectedAsync()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        var row = _rows[_selectedIndex];
        if (!row.IsInstalled)
        {
            _statusMessage = $"{row.Version} is not installed. Use 'i' to install.";
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
            _loading = true;
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

    private static IRenderable RenderPanel(bool focused, IRenderable content)
    {
        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        return new Panel(content)
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]🍄 SDKs[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }

    /// <summary>
    /// Returns a lifecycle icon based on support phase and EOL date:
    /// ✅ active/stable, ⚠️ EOL within 6 months, 🛑 end of life, 🔬 preview/RC.
    /// </summary>
    private static string GetLifecycleIcon(string? supportPhase, string? eolDate)
    {
        if (string.IsNullOrEmpty(supportPhase))
            return "  ";

        string phase = supportPhase.ToLowerInvariant();

        if (phase is "eol")
            return "🛑";

        if (phase is "preview" or "go-live" or "rc")
            return "🔬";

        // Check if EOL is within 6 months
        if (!string.IsNullOrWhiteSpace(eolDate)
            && DateTime.TryParse(eolDate, out DateTime eol)
            && eol < DateTime.UtcNow.AddMonths(6))
        {
            return "⚠️";
        }

        return "✅";
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
            "unknown" => "",
            _ => ""
        };

        return $".NET {channel} SDK — {phaseDesc}";
    }
}
