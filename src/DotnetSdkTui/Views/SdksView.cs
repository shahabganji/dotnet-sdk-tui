using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Displays installed .NET SDKs and available channels with install/uninstall/update actions.
/// Install/uninstall operations exit the TUI to show real terminal output.
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

    public string Name => "SDKs";
    public string Icon => ">";

    private List<SdkRow> _rows = [];
    private bool _loading;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;

    /// <summary>Set by install/uninstall to signal App to run a command interactively.</summary>
    internal (string Command, string Args)? PendingCommand { get; private set; }

    public bool NeedsLiveUpdate => _loading;
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

    internal void ClearPendingCommand() => PendingCommand = null;

    internal void Refresh()
    {
        _statusMessage = null;
        _loading = true;
        _error = null;
        _rows = [];
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var installedTask = DotnetUpService.ListInstalledAsync();
            List<ChannelInfo> channels;
            try
            {
                channels = await SdkSearchService.GetChannelsAsync();
            }
            catch
            {
                channels = [];
                _statusMessage = "Offline - showing local installations only";
            }

            List<SdkInfo> installed = await installedTask;

            var rows = new List<SdkRow>();

            // Show only installed SDKs (not runtimes)
            foreach (var sdk in installed.Where(s => string.Equals(s.Component, "SDK", StringComparison.OrdinalIgnoreCase)))
            {
                string channel = GuessChannel(sdk.Version);
                var channelInfo = channels.FirstOrDefault(c =>
                    sdk.Version.StartsWith(c.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                string supportPhase = channelInfo is not null
                    ? FormatSupportPhase(channelInfo.SupportPhase)
                    : "Installed";
                string eolDate = MarioTheme.FormatDate(channelInfo?.EolDate);
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
            }

            // Show available channels that are not installed
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
                        MarioTheme.FormatDate(channel.EolDate),
                        false,
                        "-",
                        lifecycleIcon,
                        GetChannelDescription(channel.ChannelVersion, channel.SupportPhase)));
                }
            }

            // Sort: newest first, preview/available at top
            rows.Sort((a, b) => SdkSearchService.CompareSdkVersions(b.Version, a.Version));

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
            return RenderPanel(focused, MarioTheme.Info("Loading SDKs..."));

        if (_error is not null)
            return RenderPanel(focused, MarioTheme.Error(_error));

        if (_rows.Count == 0)
            return RenderPanel(focused, MarioTheme.Muted("No SDKs found."));

        var parts = new List<IRenderable>();

        var table = MarioTheme.StyledTable("", "", "Version", "Channel", "Status", "Arch", "Support", "EOL");

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool selected = focused && i == _selectedIndex;
            string pointer = selected ? ">" : " ";
            string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;

            string statusText;
            string statusColor;
            if (row.IsInstalled)
            {
                statusColor = MarioTheme.Green;
                statusText = "Installed";
            }
            else
            {
                statusColor = MarioTheme.Blue;
                statusText = "Available";
            }

            table.AddRow(
                new Markup($"[{style}]{pointer}[/]"),
                new Markup($"{row.LifecycleIcon}"),
                new Markup($"[{style}]{Markup.Escape(row.Version)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Channel)}[/]"),
                new Markup($"[{statusColor}]{statusText}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Architecture)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.SupportPhase)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.EolDate)}[/]"));
        }

        parts.Add(table);

        if (focused && _selectedIndex < _rows.Count)
        {
            var selectedRow = _rows[_selectedIndex];
            parts.Add(new Markup($"\n[{MarioTheme.Gray}]{Markup.Escape(selectedRow.Description)}[/]"));
        }

        if (_statusMessage is not null)
            parts.Add(new Markup($"\n[{MarioTheme.Gold}]{Markup.Escape(_statusMessage)}[/]"));

        return RenderPanel(focused, new Rows(parts));
    }

    public string GetStatusHints()
    {
        return "up/down:Navigate  i:Install  u:Uninstall  p:Update  r:Refresh";
    }

    public async Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (_loading) return KeyResult.NotHandled;

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
                RequestInstall();
                return KeyResult.Handled;

            case ConsoleKey.U:
                RequestUninstall();
                return KeyResult.Handled;

            case ConsoleKey.P:
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
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        var row = _rows[_selectedIndex];
        if (row.IsInstalled)
        {
            _statusMessage = $"{row.Version} is already installed.";
            return;
        }

        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Press F3 to install it.";
            return;
        }

        PendingCommand = ("dotnetup", $"sdk install {row.Channel}");
    }

    private void RequestUninstall()
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

        PendingCommand = ("dotnetup", $"sdk uninstall {row.Channel}");
    }

    private void RequestUpdate()
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

        PendingCommand = ("dotnetup", $"sdk install {row.Channel}");
    }

    private static IRenderable RenderPanel(bool focused, IRenderable content)
    {
        string focusIndicator = focused ? $"[{MarioTheme.Green} bold]●[/] " : $"[{MarioTheme.Gray}]○[/] ";
        return new Panel(content)
            .Header($"{focusIndicator}[{MarioTheme.Yellow} bold]> SDKs[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(focused ? ThemeManager.PanelBorderColor : ThemeManager.TableBorderColor)
            .Expand();
    }

    private static string GetLifecycleIcon(string? supportPhase, string? eolDate)
    {
        if (string.IsNullOrEmpty(supportPhase))
            return " ";

        string phase = supportPhase.ToLowerInvariant();

        if (phase is "eol")
            return $"[{MarioTheme.Red} bold]●[/]";

        if (phase is "preview" or "go-live" or "rc")
            return $"[{MarioTheme.Blue} bold]●[/]";

        if (!string.IsNullOrWhiteSpace(eolDate)
            && DateTime.TryParse(eolDate, out DateTime eol)
            && eol < DateTime.UtcNow.AddMonths(6))
        {
            return $"[{MarioTheme.Yellow} bold]●[/]";
        }

        return $"[{MarioTheme.Green} bold]●[/]";
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
            "maintenance" => "Maintenance mode - security fixes only.",
            "preview" => "Preview release - not for production use.",
            "go-live" => "Go-Live - production supported preview.",
            "rc" => "Release Candidate - final preview before GA.",
            "eol" => "End of Life - no longer supported.",
            _ => ""
        };

        return $".NET {channel} SDK - {phaseDesc}";
    }
}
