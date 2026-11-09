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
        string Description,
        bool IsOtherLocation = false);

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

    /// <summary>Set by <c>m</c>: a dotnetup migration, which App follows with an offer to clean up the old copy.</summary>
    internal MigrationRequest? PendingMigration { get; private set; }

    /// <summary>Set by <c>D</c> on an "Other location" row: App confirms and deletes that runtime from its folder.</summary>
    internal MigrationItem? PendingDelete { get; private set; }

    internal void ClearPendingMigration() => PendingMigration = null;

    internal void ClearPendingDelete() => PendingDelete = null;

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
            // Installs in other dotnet folders (e.g. a stale ~/.dotnet) the active dotnet doesn't see.
            List<SdkInfo> other = await DotnetUpService.ListOtherRootInstallsAsync(managedRoots);

            var rows = new List<RuntimeRow>();

            // Show installed runtimes (not SDKs)
            foreach (var (rt, isOther) in installed.Select(s => (s, false)).Concat(other.Select(s => (s, true)))
                .Where(e => !string.Equals(e.Item1.Component, "SDK", StringComparison.OrdinalIgnoreCase)))
            {
                string channel = GuessChannel(rt.Version);
                var channelInfo = channels.FirstOrDefault(c =>
                    rt.Version.StartsWith(c.ChannelVersion, StringComparison.OrdinalIgnoreCase));

                // Without dotnetup the managed/unmanaged distinction is meaningless (nothing can be
                // managed), so don't flag installs as unmanaged — keep the plain "Installed" status.
                bool isManaged = !isOther
                    && (!DotnetUpService.IsInstalled()
                        || DotnetUpService.IsManagedInstallRoot(rt.InstallRoot, managedRoots));

                string supportPhase = channelInfo is not null
                    ? FormatSupportPhase(channelInfo.SupportPhase)
                    : "Installed";
                string eolDate = Ui.FormatDate(channelInfo?.EolDate);
                string lifecycleIcon = GetLifecycleIcon(channelInfo?.SupportPhase, channelInfo?.EolDate);
                string description = isManaged
                    ? $".NET {channel} {rt.DisplayComponent} - {supportPhase}"
                    : isOther
                        ? $"Other location - {rt.InstallRoot} is not used by the dotnet on your PATH. Press m to migrate it to dotnetup, or D to delete it."
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
                    description,
                    isOther));
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
        return RenderPanel(focused, RenderContent(focused));
    }

    /// <summary>
    /// Renders just the inner content (no outer view panel) so a caller can wrap it in a
    /// custom chrome — e.g. a tabbed panel whose header contains the tab strip.
    /// </summary>
    public IRenderable RenderContent(bool focused)
    {
        if (_loading)
            return Ui.Info("Loading runtimes...");

        if (_error is not null)
            return Ui.Error(_error);

        if (_rows.Count == 0)
            return Ui.Muted("No runtimes found.");

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
            if (row.IsOtherLocation)
            {
                statusColor = Ui.Brown;
                statusText = "Other location";
            }
            else if (row.IsInstalled && !row.IsManaged)
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
            tableRows,
            // EOL → Support → Arch → Channel → Component → Status → icon. Version always stays.
            dropOrder: new[] { 7, 6, 5, 3, 1, 4, 0 },
            flexibleColumn: 2));

        if (focused && _selectedIndex < _rows.Count)
        {
            var selectedRow = _rows[_selectedIndex];
            parts.Add(new Markup($"\n[{Ui.Gray}]{Markup.Escape(selectedRow.Description)}[/]"));
        }

        if (_statusMessage is not null)
            parts.Add(new Markup($"\n[{Ui.Gold}]{Markup.Escape(_statusMessage)}[/]"));

        return new Rows(parts);
    }

    public string GetStatusHints()
    {
        if (!DotnetUpService.IsInstalled())
            return "up/down:Navigate  r:Refresh  (install dotnetup to manage runtimes)";

        // Surface the migrate action only when the selected runtime is unmanaged.
        if (_selectedIndex < _rows.Count && _rows[_selectedIndex] is { IsInstalled: true, IsManaged: false } sel)
            return sel.IsOtherLocation
                ? "up/down:Navigate  m:Migrate to dotnetup  D:Delete  r:Refresh"
                : "up/down:Navigate  m:Migrate to dotnetup  r:Refresh";

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
                RequestUninstall();
                return KeyResult.Handled;

            case ConsoleKey.M:
                if (!DotnetUpService.IsInstalled()) { _statusMessage = "dotnetup required. Install it from the Setup panel."; return KeyResult.Handled; }
                RequestMigrate();
                return KeyResult.Handled;

            case ConsoleKey.D when key.KeyChar == 'D' || key.Modifiers.HasFlag(ConsoleModifiers.Shift):
                RequestDelete();
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
            _statusMessage = "dotnetup not found.";
            return;
        }
        if (!row.IsManaged)
        {
            _statusMessage = UnmanagedMessage(row.Version, row.InstallRoot);
            return;
        }
        // Exact component@version; InstallGuard removes only the specs that keep this runtime.
        PendingCommand = ("dotnetup", InstallGuard.UninstallArgsFor(row.Component, row.Version), null);
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

        // Other-location copies are offered for deletion after the install; system copies need the uninstall tool.
        string? note = row.IsOtherLocation
            ? null
            : $"dotnetup now manages {row.Component} {row.Version} in its own directory. " +
              $"The original copy is still at {row.InstallRoot}; remove it with the official " +
              ".NET uninstall tool if you no longer need it. " +
              "That location may hold other system-installed versions, so don't delete the whole folder.";

        PendingMigration = new MigrationRequest($"runtime install {spec}", note, [ToMigrationItem(row)]);
    }

    /// <summary>
    /// Handles <c>D</c>: deletes the selected runtime — only from an "Other location" folder. A runtime an SDK
    /// in that folder ships with stays (the SDK needs it); delete or migrate the SDK instead.
    /// </summary>
    private void RequestDelete()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        RuntimeRow row = _rows[_selectedIndex];
        if (!row.IsOtherLocation)
        {
            _statusMessage = row switch
            {
                { IsInstalled: false } => $"{row.Version} is not installed.",
                { IsManaged: true } => $"D only deletes Other location runtimes - press u to uninstall {row.Version} with dotnetup.",
                _ => $"D only deletes Other location runtimes - {row.Version} is in your active dotnet; press m to migrate it.",
            };
            return;
        }

        MigrationItem item = ToMigrationItem(row);
        IReadOnlyList<string> sdks = InstallRemoval.FindBundlingSdks(item.Root, new RuntimeRef(item.Framework!, item.Version));
        if (sdks.Count > 0)
        {
            _statusMessage = $"{row.Component} {row.Version} ships with SDK {string.Join(", ", sdks)} in {item.Root} - " +
                             "delete (D) or migrate (m) that SDK on the SDKs tab instead.";
            return;
        }

        PendingDelete = item;
    }

    private static MigrationItem ToMigrationItem(RuntimeRow row) =>
        new(row.Version, row.Channel, row.InstallRoot, row.IsOtherLocation, InstallRemoval.FrameworkOf(row.InstallRoot));

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
