using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Displays installed .NET runtimes and available runtime channels with the same
/// features as SdksView: lifecycle icons, channel, support, EOL, install/uninstall.
/// </summary>
public sealed class RuntimesView : IView
{
    private sealed record RuntimeRow(
        string Component,
        string Version,
        string Channel,
        string SupportPhase,
        string EolDate,
        bool IsInstalled,
        bool IsManaged,
        string InstallRoot,
        string Architecture,
        string LifecycleIcon,
        string Description);

    public string Name => "Runtimes";
    public string Icon => " ";

    private List<RuntimeRow> _rows = [];
    private bool _loading;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;
    private int _scrollOffset;

    /// <summary>
    /// Set by install/uninstall/migrate to signal App to run a command interactively.
    /// <c>Note</c>, when present, is printed in the terminal after the command succeeds.
    /// </summary>
    internal (string Command, string Args, string? Note)? PendingCommand { get; private set; }

    public bool NeedsLiveUpdate => _loading;
    public bool IsTextInputActive => false;

    public Task ActivateAsync()
    {
        if (_rows.Count == 0 && !_loading)
        {
            _loading = true;
            _error = null;
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
            // Installations dotnetup actually manages; empty when dotnetup is absent or tracks nothing.
            Task<List<SdkInfo>> trackedTask = DotnetUpService.IsInstalled()
                ? DotnetUpService.ListTrackedAsync()
                : Task.FromResult(new List<SdkInfo>());

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
            List<SdkInfo> tracked = await trackedTask;
            var managedRoots = tracked
                .Select(t => t.InstallRoot)
                .Where(r => !string.IsNullOrWhiteSpace(r))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var rows = new List<RuntimeRow>();

            // Show installed runtimes (not SDKs)
            foreach (var rt in installed.Where(s => !string.Equals(s.Component, "SDK", StringComparison.OrdinalIgnoreCase)))
            {
                string channel = GuessChannel(rt.Version);
                var channelInfo = channels.FirstOrDefault(c =>
                    rt.Version.StartsWith(c.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                // Without dotnetup the managed/unmanaged distinction is meaningless (nothing can be
                // managed), so don't flag installs as unmanaged — keep the plain "Installed" status.
                bool isManaged = !DotnetUpService.IsInstalled()
                    || DotnetUpService.IsManagedInstallRoot(rt.InstallRoot, managedRoots);

                string supportPhase = channelInfo is not null
                    ? FormatSupportPhase(channelInfo.SupportPhase)
                    : "Installed";
                string eolDate = Ui.FormatDate(channelInfo?.EolDate);
                string lifecycleIcon = GetLifecycleIcon(channelInfo?.SupportPhase, channelInfo?.EolDate);
                string description = isManaged
                    ? $".NET {channel} {rt.DisplayComponent} - {supportPhase}"
                    : $"Unmanaged - installed outside dotnetup at {rt.InstallRoot}. Press m to let dotnetup manage (migrate) it.";

                rows.Add(new RuntimeRow(
                    rt.DisplayComponent,
                    rt.Version,
                    channel,
                    supportPhase,
                    eolDate,
                    true,
                    isManaged,
                    rt.InstallRoot,
                    rt.Architecture.Length > 0 ? rt.Architecture : System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
                    lifecycleIcon,
                    description));
            }

            // Show latest available runtime for channels where it's not already installed
            foreach (var channel in channels)
            {
                if (string.IsNullOrWhiteSpace(channel.LatestRuntime))
                    continue;

                // Only show latest available for active and preview channels, not maintenance/eol
                string phase = channel.SupportPhase.ToLowerInvariant();
                if (phase is "eol" or "maintenance")
                    continue;

                bool hasLatestInstalled = installed.Any(s =>
                    !string.Equals(s.Component, "SDK", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(s.Version, channel.LatestRuntime, StringComparison.OrdinalIgnoreCase));

                if (!hasLatestInstalled)
                {
                    string lifecycleIcon = GetLifecycleIcon(channel.SupportPhase, channel.EolDate);
                    rows.Add(new RuntimeRow(
                        "Runtime",
                        channel.LatestRuntime,
                        channel.ChannelVersion,
                        FormatSupportPhase(channel.SupportPhase),
                        Ui.FormatDate(channel.EolDate),
                        false,
                        false,
                        "",
                        "-",
                        lifecycleIcon,
                        $".NET {channel.ChannelVersion} Runtime - {FormatSupportPhase(channel.SupportPhase)}"));
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
            return RenderPanel(focused, Ui.Info("Loading runtimes..."));

        if (_error is not null)
            return RenderPanel(focused, Ui.Error(_error));

        if (_rows.Count == 0)
            return RenderPanel(focused, Ui.Muted("No runtimes found."));

        var parts = new List<IRenderable>();

        // Calculate visible window — each row is ~2 lines with padding
        int windowHeight;
        try { windowHeight = Console.WindowHeight; } catch { windowHeight = 40; }
        int availableHeight = Math.Max(3, windowHeight / 4);
        int visibleCount = Math.Min(_rows.Count, availableHeight);

        // Keep selected row in view
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        else if (_selectedIndex >= _scrollOffset + visibleCount)
            _scrollOffset = _selectedIndex - visibleCount + 1;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _rows.Count - visibleCount));

        int endIndex = Math.Min(_scrollOffset + visibleCount, _rows.Count);

        var tableRows = new List<(Cell[], bool)>();
        for (int i = _scrollOffset; i < endIndex; i++)
        {
            var row = _rows[i];
            bool selected = focused && i == _selectedIndex;

            string statusText;
            string statusColor;
            if (row.IsInstalled && !row.IsManaged)
            {
                statusColor = Ui.Gold;
                statusText = "Unmanaged";
            }
            else if (row.IsInstalled)
            {
                statusColor = Ui.Green;
                statusText = "Installed";
            }
            else
            {
                statusColor = Ui.Blue;
                statusText = "Available";
            }

            tableRows.Add((new[]
            {
                new Cell(row.LifecycleIcon, Ui.White, IsMarkup: true),
                new Cell(row.Component, Ui.White),
                new Cell(row.Version, Ui.White),
                new Cell(row.Channel, Ui.White),
                new Cell(statusText, statusColor),
                new Cell(row.Architecture, Ui.White),
                new Cell(row.SupportPhase, Ui.White),
                new Cell(row.EolDate, Ui.White),
            }, selected));
        }

        parts.Add(Ui.SelectableTable(
            new[] { "", "Component", "Version", "Channel", "Status", "Arch", "Support", "EOL" },
            tableRows));

        if (focused && _selectedIndex < _rows.Count)
        {
            var selectedRow = _rows[_selectedIndex];
            parts.Add(new Markup($"\n[{Ui.Gray}]{Markup.Escape(selectedRow.Description)}[/]"));
        }

        if (_statusMessage is not null)
            parts.Add(new Markup($"\n[{Ui.Gold}]{Markup.Escape(_statusMessage)}[/]"));

        return RenderPanel(focused, new Rows(parts));
    }

    public string GetStatusHints()
    {
        if (!DotnetUpService.IsInstalled())
            return "up/down:Navigate  r:Refresh  (install dotnetup to manage runtimes)";

        // Surface the migrate action only when the selected runtime is unmanaged.
        if (_selectedIndex < _rows.Count && _rows[_selectedIndex] is { IsInstalled: true, IsManaged: false })
            return "up/down:Navigate  m:Migrate to dotnetup  r:Refresh";

        return "up/down:Navigate  i:Install  u:Uninstall  r:Refresh";
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
                if (!DotnetUpService.IsInstalled()) { _statusMessage = "dotnetup required. Install it from the Setup panel."; return KeyResult.Handled; }
                RequestInstall();
                return KeyResult.Handled;

            case ConsoleKey.U:
                if (!DotnetUpService.IsInstalled()) { _statusMessage = "dotnetup required. Install it from the Setup panel."; return KeyResult.Handled; }
                await RequestUninstallAsync();
                return KeyResult.Handled;

            case ConsoleKey.M:
                if (!DotnetUpService.IsInstalled()) { _statusMessage = "dotnetup required. Install it from the Setup panel."; return KeyResult.Handled; }
                RequestMigrate();
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
            _statusMessage = row.IsManaged
                ? $"{row.Version} is already installed."
                : UnmanagedMessage(row.Version, row.InstallRoot);
            return;
        }
        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Press F3 to install it.";
            return;
        }
        PendingCommand = ("dotnetup", $"runtime install {row.Channel}", null);
    }

    private async Task RequestUninstallAsync()
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
            _statusMessage = "dotnetup not found.";
            return;
        }
        if (!row.IsManaged)
        {
            _statusMessage = UnmanagedMessage(row.Version, row.InstallRoot);
            return;
        }
        string spec = await DotnetUpService.ResolveInstallSpecAsync(row.Version, row.Component);
        PendingCommand = ("dotnetup", $"runtime uninstall {spec} --source all", null);
    }

    /// <summary>
    /// Brings a single unmanaged (externally installed) runtime under dotnetup management by installing
    /// that exact version into dotnetup's directory and making it the default dotnet.
    /// </summary>
    /// <remarks>
    /// Deliberately installs the exact selected version rather than using <c>--migrate-from-system</c>,
    /// which migrates every matching system channel at once — not what a per-row action should do.
    /// </remarks>
    private void RequestMigrate()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;
        var row = _rows[_selectedIndex];
        if (!row.IsInstalled)
        {
            _statusMessage = $"{row.Version} is not installed. Use 'i' to install it via dotnetup.";
            return;
        }
        if (row.IsManaged)
        {
            _statusMessage = $"{row.Version} is already managed by dotnetup.";
            return;
        }
        if (!DotnetUpService.IsInstalled())
        {
            _statusMessage = "dotnetup not found. Cannot migrate without dotnetup.";
            return;
        }

        // ASP.NET Core runtimes need a component-qualified spec; the core runtime takes a bare version.
        string spec = row.Component.Contains("ASP", StringComparison.OrdinalIgnoreCase)
            ? $"aspnetcore@{row.Version}"
            : row.Version;

        string note =
            $"dotnetup now manages {row.Component} {row.Version} in its own directory. " +
            $"The original copy is still at {row.InstallRoot}; remove it with the official " +
            ".NET uninstall tool if you no longer need it. " +
            "That location may hold other system-installed versions, so don't delete the whole folder.";

        PendingCommand = ("dotnetup", $"runtime install {spec}", note);
    }

    private static IRenderable RenderPanel(bool focused, IRenderable content) =>
        Ui.ViewPanel(Ui.IconRuntimes, "Runtimes", content, focused);

    private static string GetLifecycleIcon(string? supportPhase, string? eolDate)
    {
        if (string.IsNullOrEmpty(supportPhase))
            return " ";

        string phase = supportPhase.ToLowerInvariant();

        if (phase is "eol")
            return Ui.IconEol;

        if (phase is "preview" or "go-live" or "rc")
            return Ui.IconPreview;

        if (phase is "maintenance")
            return Ui.IconMaint;

        if (!string.IsNullOrWhiteSpace(eolDate)
            && DateTime.TryParse(eolDate, out DateTime eol)
            && eol < DateTime.UtcNow.AddMonths(6))
        {
            return Ui.IconMaint;
        }

        return Ui.IconActive;
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

    /// <summary>Consistent status message shown when an action is attempted on an unmanaged runtime.</summary>
    private static string UnmanagedMessage(string version, string installRoot) =>
        $"{version} is unmanaged (installed at {installRoot}) - press m to let dotnetup manage it.";
}
