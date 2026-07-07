using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Full-screen Workloads workspace (F4). Lists workloads for the current SDK's feature
/// band with install/uninstall/update actions that exit the TUI to show real terminal
/// output — matching the SDKs / Runtimes / Brew UX.
/// </summary>
/// <remarks>
/// Workloads are scoped per SDK feature band, not per exact SDK. The header surfaces
/// the active SDK version, feature band, workload version, and update mode so users
/// always know which band's state they're looking at.
/// </remarks>
public sealed class WorkloadsView : IView
{
    public string Name => "Workloads";
    public string Icon => "\U0001f9e9"; // 🧩

    private List<WorkloadInfo> _rows = [];
    private WorkloadEnv? _env;
    private string? _targetSdk;         // Non-null when opened via drill-in (`w` on SDK row). Scopes every
                                        // `dotnet workload …` invocation to that SDK via a scratch global.json.
    private bool _loading;
    private bool _searching;
    private bool _hasPendingSearch;
    private string? _error;
    private string? _statusMessage;
    private int _selectedIndex;
    private int _scrollOffset;

    // Search state — '/' opens an inline query field. Every debounced keystroke
    // authoritatively re-runs `dotnet workload search <pattern>` (per user decision
    // 6/8 during design review).
    private bool _searchOpen;
    private bool _searchInputActive = true;
    private string _query = "";
    private CancellationTokenSource? _searchCts;
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Confirmation-flow flag: pressing <c>m</c> stages an update-mode toggle; the App
    /// pops a confirm dialog before committing (matches the Shift+M bulk-migrate flow).
    /// </summary>
    internal string? PendingModeToggle { get; private set; }

    /// <summary>Set by install/uninstall/update to signal App to run a command interactively.</summary>
    internal (string Command, string Args, string? Note, string? Cwd)? PendingCommand { get; private set; }

    public bool NeedsLiveUpdate => _loading || _searching || _hasPendingSearch;
    public bool IsTextInputActive => _searchOpen && _searchInputActive;

    /// <summary>True whenever the search overlay is open (typing or navigating results).</summary>
    public bool IsSearching => _searchOpen;

    public Task ActivateAsync()
    {
        // Drill-in only: workloads are always scoped to a specific SDK. If ActivateAsync is
        // called without a target set (e.g. from splash prefetch), it's a no-op — the view
        // stays empty until ActivateForSdkAsync is called.
        return Task.CompletedTask;
    }

    /// <summary>
    /// Opens the Workloads workspace scoped to <paramref name="sdkVersion"/>. Every
    /// subsequent <c>dotnet workload …</c> call is forced to resolve to that SDK via a
    /// scratch-directory <c>global.json</c> (see <see cref="WorkloadService.EnsureSdkPinDirectory"/>).
    /// Safe to call repeatedly — re-invoking with a different SDK refreshes the panel.
    /// </summary>
    public Task ActivateForSdkAsync(string sdkVersion)
    {
        if (string.IsNullOrWhiteSpace(sdkVersion))
            throw new ArgumentException("SDK version is required.", nameof(sdkVersion));

        _targetSdk = sdkVersion;
        _rows = [];
        _env = null;
        _selectedIndex = 0;
        _scrollOffset = 0;
        _statusMessage = null;
        _error = null;

        if (WorkloadService.IsInstalled())
        {
            _loading = true;
            _ = LoadAsync();
        }

        return Task.CompletedTask;
    }

    internal void ClearPendingCommand() => PendingCommand = null;
    internal void ClearPendingModeToggle() => PendingModeToggle = null;

    /// <summary>Called by App after a mode toggle is confirmed and executed.</summary>
    internal void OnModeToggled() => Refresh();

    internal void Refresh()
    {
        if (!WorkloadService.IsInstalled()) return;
        if (_targetSdk is null) return;   // Drill-in required; nothing to refresh without a target.

        _statusMessage = null;
        _loading = true;
        _error = null;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            Task<WorkloadEnv?> envTask = WorkloadService.GetEnvAsync(_targetSdk);
            Task<List<WorkloadInfo>> rowsTask = WorkloadService.ListMergedAsync(_targetSdk);
            await Task.WhenAll(envTask, rowsTask);

            _env = envTask.Result;
            _rows = rowsTask.Result;
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
        if (!WorkloadService.IsInstalled())
            return RenderPanel(focused, new Rows(
                Ui.Info("The `dotnet` CLI is not on PATH."),
                new Markup($"[{Ui.Gray}]Install a .NET SDK, then press r to refresh.[/]")));

        if (_targetSdk is null)
            return RenderPanel(focused, new Rows(
                Ui.Info("No SDK selected."),
                new Markup($"[{Ui.Gray}]Return to the main screen and press [b]w[/] on an installed SDK to manage its workloads.[/]")));

        if (_loading && _rows.Count == 0)
            return RenderPanel(focused, Ui.Info($"Loading workloads for SDK {_targetSdk}..."));

        if (_error is not null)
            return RenderPanel(focused, Ui.Error(_error));

        var parts = new List<IRenderable>();

        parts.Add(RenderHeader());

        if (_searchOpen)
            parts.Add(RenderSearchLine());

        parts.Add(RenderTable(focused));

        // Description of the selected item
        if (focused && _selectedIndex < _rows.Count && !string.IsNullOrWhiteSpace(_rows[_selectedIndex].Description))
            parts.Add(new Markup($"\n[{Ui.Gray}]{Markup.Escape(_rows[_selectedIndex].Description)}[/]"));

        if (_statusMessage is not null)
            parts.Add(new Markup($"\n[{Ui.Gold}]{Markup.Escape(_statusMessage)}[/]"));

        return RenderPanel(focused, new Rows(parts));
    }

    private IRenderable RenderHeader()
    {
        WorkloadEnv? e = _env;
        string sdk    = e?.ActiveSdkVersion   ?? "?";
        string band   = e?.ActiveFeatureBand  ?? "?";
        string wlVer  = e?.WorkloadVersion    ?? "?";
        string mode   = e?.UpdateMode         ?? "?";

        string line = $"[{Ui.Yellow} bold]SDK[/] [{Ui.White}]{Markup.Escape(sdk)}[/]  " +
                      $"[{Ui.Yellow} bold]band[/] [{Ui.White}]{Markup.Escape(band)}[/]  " +
                      $"[{Ui.Yellow} bold]workloads[/] [{Ui.White}]{Markup.Escape(wlVer)}[/]  " +
                      $"[{Ui.Yellow} bold]mode[/] [{Ui.White}]{Markup.Escape(mode)}[/]";

        if (OperatingSystem.IsWindows())
            line += $"\n[{Ui.Gold}]Install may prompt for elevation (UAC).[/]";

        return new Markup(line + "\n");
    }

    private IRenderable RenderSearchLine()
    {
        string icon = _searching ? "*" : "/";
        string cursor = _searchInputActive ? "|" : "";
        string body = _query.Length > 0
            ? $"[{Ui.White}]{Markup.Escape(_query)}[/]"                                                      // typed text — theme foreground
            : $"[{Ui.Gray}]type to filter (invokes `dotnet workload search`)...[/]";                          // placeholder — dimmed
        return new Markup($"[{Ui.Yellow} bold] {icon} Search: [/]{body}[{Ui.Yellow}]{cursor}[/]\n");
    }

    private IRenderable RenderTable(bool focused)
    {
        if (_rows.Count == 0)
        {
            if (_searchOpen)
                return Ui.Muted(_searching ? "Searching..." : _query.Length > 0 ? "No workloads found." : "Type to search.");
            return Ui.Muted("No workloads discovered.");
        }

        int windowHeight;
        try { windowHeight = Console.WindowHeight; } catch { windowHeight = 40; }
        int visibleCount = Math.Min(_rows.Count, Math.Max(5, windowHeight - 16));

        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        else if (_selectedIndex >= _scrollOffset + visibleCount)
            _scrollOffset = _selectedIndex - visibleCount + 1;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, _rows.Count - visibleCount));
        int endIndex = Math.Min(_scrollOffset + visibleCount, _rows.Count);

        var tableRows = new List<(Cell[], bool)>();
        for (int i = _scrollOffset; i < endIndex; i++)
        {
            WorkloadInfo row = _rows[i];
            bool selectable = !_searchOpen || !_searchInputActive;
            bool selected = focused && selectable && i == _selectedIndex;

            string icon = GetLifecycleIcon(row);
            string statusText;
            string statusColor;
            if (row.IsInstalled)
            {
                statusColor = Ui.Green;
                statusText = "Installed";
            }
            else
            {
                statusColor = Ui.Blue;
                statusText = "Available";
            }

            string manifest = row.InstalledManifestVersion ?? "-";
            string source   = row.InstallationSource       ?? "-";

            tableRows.Add((new[]
            {
                new Cell(icon, Ui.White, IsMarkup: true),
                new Cell(row.Id, Ui.White),
                new Cell(statusText, statusColor),
                new Cell(manifest, Ui.White),
                new Cell(source, Ui.White),
            }, selected));
        }

        return Ui.SelectableTable(
            new[] { "", "Workload", "Status", "Manifest", "Source" },
            tableRows,
            // Drop least-essential columns first at narrow widths: Source → Manifest → Status → icon.
            // Workload id always stays.
            dropOrder: new[] { 4, 3, 2, 0 },
            flexibleColumn: 1);
    }

    /// <summary>
    /// Preview workloads (id ends with "-experimental" or contains "preview") get the 🏭 icon;
    /// installed workloads get 🍀; anything else is a blank cell. Matches SDK/Runtime conventions.
    /// </summary>
    private static string GetLifecycleIcon(WorkloadInfo row)
    {
        bool preview = row.Id.Contains("experimental", StringComparison.OrdinalIgnoreCase)
                    || row.Id.Contains("preview",      StringComparison.OrdinalIgnoreCase);
        if (row.IsInstalled) return Ui.IconActive;
        if (preview)         return Ui.IconPreview;
        return " ";
    }

    public string GetStatusHints()
    {
        if (!WorkloadService.IsInstalled())
            return "r:Refresh  Esc:Back  (install .NET SDK to manage workloads)";
        if (_searchOpen)
        {
            return _searchInputActive
                ? "Type to search  Tab:Results  Esc:Cancel"
                : "up/down:Navigate  i:Install  Tab:Search  Esc:Cancel";
        }
        return "up/down:Navigate  i:Install  u:Uninstall  p:Update  Shift+P:Repair  m:Toggle-mode  /:Search  r:Refresh  Esc:Back";
    }

    public Task<KeyResult> HandleKeyAsync(ConsoleKeyInfo key)
    {
        if (!WorkloadService.IsInstalled())
        {
            if (key.Key is ConsoleKey.R) { Refresh(); return Task.FromResult(KeyResult.Handled); }
            if (key.Key is ConsoleKey.Escape) return Task.FromResult(KeyResult.Quit);
            return Task.FromResult(KeyResult.NotHandled);
        }

        return Task.FromResult(_searchOpen ? HandleSearchKey(key) : HandleListKey(key));
    }

    private KeyResult HandleListKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_rows.Count > 0) _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_rows.Count > 0) _selectedIndex = Math.Min(_rows.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.Oem2:  // "/"
                EnterSearchMode();
                return KeyResult.Handled;

            case ConsoleKey.I:
                RequestInstall();
                return KeyResult.Handled;

            case ConsoleKey.U:
                RequestUninstall();
                return KeyResult.Handled;

            case ConsoleKey.P:
                if (key.Modifiers.HasFlag(ConsoleModifiers.Shift) || key.KeyChar == 'P')
                    RequestRepair();
                else
                    RequestUpdate();
                return KeyResult.Handled;

            case ConsoleKey.M:
                RequestModeToggle();
                return KeyResult.Handled;

            case ConsoleKey.R:
                Refresh();
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                return KeyResult.Quit;

            default:
                if (key.KeyChar == '/') { EnterSearchMode(); return KeyResult.Handled; }
                return KeyResult.NotHandled;
        }
    }

    private KeyResult HandleSearchKey(ConsoleKeyInfo key) =>
        _searchInputActive ? HandleSearchInputKey(key) : HandleSearchNavKey(key);

    private KeyResult HandleSearchInputKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.Tab:
            case ConsoleKey.DownArrow:
                if (_rows.Count > 0)
                {
                    _searchInputActive = false;
                    _selectedIndex = 0;
                    _scrollOffset = 0;
                }
                return KeyResult.Handled;

            case ConsoleKey.Backspace:
                if (_query.Length > 0)
                {
                    _query = _query[..^1];
                    TriggerDebouncedSearch();
                }
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                ExitSearchMode();
                return KeyResult.Handled;

            default:
                if (key.KeyChar is >= ' ' and <= '~')
                {
                    _query += key.KeyChar;
                    TriggerDebouncedSearch();
                    return KeyResult.Handled;
                }
                return KeyResult.NotHandled;
        }
    }

    private KeyResult HandleSearchNavKey(ConsoleKeyInfo key)
    {
        switch (key.Key)
        {
            case ConsoleKey.UpArrow or ConsoleKey.K:
                if (_rows.Count > 0) _selectedIndex = Math.Max(0, _selectedIndex - 1);
                return KeyResult.Handled;

            case ConsoleKey.DownArrow or ConsoleKey.J:
                if (_rows.Count > 0) _selectedIndex = Math.Min(_rows.Count - 1, _selectedIndex + 1);
                return KeyResult.Handled;

            case ConsoleKey.Tab:
                _searchInputActive = true;
                return KeyResult.Handled;

            case ConsoleKey.I or ConsoleKey.Enter:
                RequestInstall();
                return KeyResult.Handled;

            case ConsoleKey.Escape:
                ExitSearchMode();
                return KeyResult.Handled;

            default:
                if (key.KeyChar is >= ' ' and <= '~')
                {
                    _searchInputActive = true;
                    _query += key.KeyChar;
                    TriggerDebouncedSearch();
                    return KeyResult.Handled;
                }
                return KeyResult.NotHandled;
        }
    }

    private void EnterSearchMode()
    {
        _searchOpen = true;
        _searchInputActive = true;
        _query = "";
        _selectedIndex = 0;
        _scrollOffset = 0;
        _statusMessage = null;
        // Do not clear _rows — leave the current merged list visible until the user types.
    }

    private void ExitSearchMode()
    {
        CancelSearch();
        _searchOpen = false;
        _searchInputActive = true;
        _query = "";
        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _rows.Count - 1));
        _scrollOffset = 0;
        // Restore the unfiltered merged list.
        Refresh();
    }

    private void CancelSearch()
    {
        _searchCts?.Cancel();
        _searchCts = null;
        _searching = false;
        _hasPendingSearch = false;
    }

    private void TriggerDebouncedSearch()
    {
        _searchCts?.Cancel();
        _searching = false;
        _searchCts = new CancellationTokenSource();
        _hasPendingSearch = true;
        var token = _searchCts.Token;
        string query = _query;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(DebounceDelay, token);
                if (token.IsCancellationRequested) return;

                _searching = true;
                List<WorkloadInfo> results = string.IsNullOrWhiteSpace(query)
                    ? await WorkloadService.ListMergedAsync(_targetSdk, null, token)
                    : await WorkloadService.ListMergedAsync(_targetSdk, query, token);

                if (!token.IsCancellationRequested)
                {
                    _rows = results;
                    _selectedIndex = 0;
                    _scrollOffset = 0;
                    _error = null;
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    _error = ex.Message;
                    _rows = [];
                }
            }
            finally
            {
                if (!token.IsCancellationRequested)
                {
                    _searching = false;
                    _hasPendingSearch = false;
                }
            }
        });
    }

    private void RequestInstall()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        WorkloadInfo row = _rows[_selectedIndex];
        if (row.IsInstalled)
        {
            _statusMessage = $"{row.Id} is already installed.";
            return;
        }

        bool preview = row.Id.Contains("experimental", StringComparison.OrdinalIgnoreCase)
                    || row.Id.Contains("preview",      StringComparison.OrdinalIgnoreCase);
        PendingCommand = WorkloadService.BuildInstall(row.Id, preview, _targetSdk);
    }

    private void RequestUninstall()
    {
        if (_rows.Count == 0 || _selectedIndex >= _rows.Count) return;

        WorkloadInfo row = _rows[_selectedIndex];
        if (!row.IsInstalled)
        {
            _statusMessage = $"{row.Id} is not installed.";
            return;
        }

        PendingCommand = WorkloadService.BuildUninstall(row.Id, _targetSdk);
    }

    private void RequestUpdate()
    {
        // `dotnet workload update` is all-installed-workloads at once; there's no per-id form.
        PendingCommand = WorkloadService.BuildUpdate(includePreviews: false, _targetSdk);
    }

    private void RequestRepair()
    {
        PendingCommand = WorkloadService.BuildRepair(_targetSdk);
    }

    /// <summary>
    /// Stages a machine-wide update-mode toggle. App reads this, pops a confirm dialog,
    /// and (on confirm) runs <c>dotnet workload config --update-mode …</c>.
    /// </summary>
    private void RequestModeToggle()
    {
        string current = _env?.UpdateMode ?? "workload-set";
        string target = string.Equals(current, "workload-set", StringComparison.OrdinalIgnoreCase)
            ? "manifests"
            : "workload-set";
        PendingModeToggle = target;
    }

    /// <summary>Returns (currentMode, targetMode) for the confirm dialog.</summary>
    internal (string Current, string Target) GetModeToggleSummary()
    {
        string current = _env?.UpdateMode ?? "workload-set";
        string target = PendingModeToggle ?? current;
        return (current, target);
    }

    // Full-screen focus-aware panel, matching Brew and Search workspaces.
    private static IRenderable RenderPanel(bool focused, IRenderable content) =>
        Ui.ViewPanel("\U0001f9e9", "Workloads", content, focused);
}
