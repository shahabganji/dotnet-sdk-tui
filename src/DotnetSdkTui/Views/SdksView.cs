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
        bool IsInstalled,
        string InstalledVersion,
        string Architecture);

    public string Name => "SDKs";
    public string Icon => "🍄";

    private List<SdkRow> _rows = [];
    private bool _loading;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;
    private bool _actionRunning;

    public bool IsLoading => _loading;

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
            // Load both in parallel
            var installedTask = DotnetUpService.ListInstalledAsync();
            var channelsTask = SdkSearchService.GetChannelsAsync();

            await Task.WhenAll(installedTask, channelsTask);

            List<SdkInfo> installed = installedTask.Result;
            var channels = channelsTask.Result;

            // Build unified rows: active channels with install status
            var rows = new List<SdkRow>();

            foreach (var channel in channels)
            {
                if (string.Equals(channel.SupportPhase, "eol", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (string.IsNullOrWhiteSpace(channel.LatestSdk))
                    continue;

                // Check if any installed SDK matches this channel
                var matchingInstalled = installed.FirstOrDefault(s =>
                    s.Version.StartsWith(channel.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                rows.Add(new SdkRow(
                    channel.ChannelVersion,
                    channel.LatestSdk,
                    FormatSupportPhase(channel.SupportPhase),
                    matchingInstalled is not null,
                    matchingInstalled?.Version ?? "-",
                    matchingInstalled?.Architecture ?? "-"));
            }

            // Also add any installed SDKs not matched to an active channel
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
                        true,
                        sdk.Version,
                        sdk.Architecture));
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

    public IRenderable Render()
    {
        if (_loading)
            return MarioTheme.ContentPanel("SDKs", MarioTheme.Info("Loading SDKs..."));

        if (_error is not null)
            return MarioTheme.ContentPanel("SDKs", MarioTheme.Error(_error));

        if (_rows.Count == 0)
            return MarioTheme.ContentPanel("SDKs", MarioTheme.Coin("No SDK channels found. Check your internet connection."));

        var table = MarioTheme.StyledTable("", "Channel", "Latest SDK", "Status", "Installed", "Arch", "Support");

        for (int i = 0; i < _rows.Count; i++)
        {
            var row = _rows[i];
            bool selected = i == _selectedIndex;
            string pointer = selected ? "►" : " ";
            string style = selected ? $"{MarioTheme.Yellow} bold" : MarioTheme.White;
            string statusIcon = row.IsInstalled ? "✓" : "✗";
            string statusColor = row.IsInstalled ? MarioTheme.Green : MarioTheme.Blue;

            table.AddRow(
                new Markup($"[{style}]{pointer}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Channel)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.LatestVersion)}[/]"),
                new Markup($"[{statusColor} bold]{statusIcon} {(row.IsInstalled ? "Installed" : "Available")}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.InstalledVersion)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.Architecture)}[/]"),
                new Markup($"[{style}]{Markup.Escape(row.SupportPhase)}[/]"));
        }

        var content = new Rows(new IRenderable[]
        {
            table,
            _statusMessage is not null ? new Markup($"\n[{MarioTheme.Gold}]{Markup.Escape(_statusMessage)}[/]") : Text.Empty,
        });

        return MarioTheme.ContentPanel("SDKs", content);
    }

    public string GetStatusHints()
    {
        if (_actionRunning) return "Running...";
        return "↑↓:Navigate  i:Install  u:Uninstall  r:Refresh";
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
            _statusMessage = "dotnetup not found. Go to Setup (4) to install it first.";
            return;
        }

        _actionRunning = true;
        _statusMessage = $"Installing SDK channel {row.Channel}...";

        try
        {
            var result = await DotnetUpService.InstallSdkAsync(row.Channel);
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
            _ = LoadAsync(); // refresh
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
        _statusMessage = $"Uninstalling SDK channel {row.Channel}...";

        try
        {
            var result = await DotnetUpService.UninstallSdkAsync(row.Channel);
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

    private Task RefreshAsync()
    {
        _statusMessage = null;
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
            _ => phase
        };

    private static string GuessChannel(string version)
    {
        var parts = version.Split('.');
        return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;
    }
}
