using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Models;
using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Views;

/// <summary>
/// Compact dotnetup status plus install-folder diagnostics: the dotnet root dsm reads, the folder
/// dotnetup manages (highlighted when they differ), and any remembered install folder.
/// Manages install/update via interactive commands.
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
    private string? _activeRoot;
    private List<string> _trackedRoots = [];
    private string? _rememberedRoot;

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
            _activeRoot = InstallLocationService.GetActiveMuxerDirectory();
            _rememberedRoot = SettingsStore.Load().InstallRoot;
            _trackedRoots = [];
            if (_isInstalled)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    _info = await DotnetUpService.GetInfoAsync(cts.Token);
                    _trackedRoots = (await DotnetUpService.ListTrackedAsync(cts.Token))
                        .Select(t => t.InstallRoot.TrimEnd('/', '\\'))
                        .Where(r => r.Length > 0)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
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
            return Ui.ViewPanel(Ui.IconSetup, "Setup", Ui.Info("Checking dotnetup..."), focused);

        IRenderable content;

        if (_isInstalled && _info is not null)
        {
            // Optional details are dropped right-to-left (commit, RID, arch) rather than wrapped.
            string head = $"dotnetup v{_info.Version}";
            string line = $"[{Ui.Green} bold]dotnetup[/] [{Ui.White}]v{Markup.Escape(FitText(_info.Version, ContentWidth() - 10))}[/]";
            int used = head.Length;
            foreach (string detail in (string[])[_info.Architecture, _info.Rid, _info.Commit[..Math.Min(7, _info.Commit.Length)]])
            {
                if (used + 2 + detail.Length > ContentWidth()) break;
                line += $"  [{Ui.Gray}]{Markup.Escape(detail)}[/]";
                used += 2 + detail.Length;
            }

            var lines = new List<IRenderable> { new Markup(line) };
            lines.AddRange(BuildRootLines());
            // Messages get their own pre-fitted line: wrapping would push diagnostics out of the
            // fixed-height top row.
            if (_statusMessage is not null)
                lines.Add(new Markup($"[{Ui.Gold}]{Markup.Escape(FitText(_statusMessage, ContentWidth()))}[/]"));
            if (_error is not null)
                lines.Add(new Markup($"[{ThemeManager.ErrorColor}]{Markup.Escape(FitText(_error, ContentWidth()))}[/]"));

            content = new Rows(lines);
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

        return Ui.ViewPanel(Ui.IconSetup, "Setup", content, focused);
    }

    /// <summary>
    /// Content lines the panel renders (excluding borders), so App can size the top row to fit.
    /// </summary>
    internal int ContentLineCount =>
        !_loading && _isInstalled && _info is not null
            ? 1 + GetRootLines().Count + (_statusMessage is null ? 0 : 1) + (_error is null ? 0 : 1)
            : 1;

    /// <summary>A diagnostic line: label, folder path, optional suffix, and whether to warn.</summary>
    internal sealed record RootLine(string Label, string Path, string Suffix, bool Warn);

    /// <summary>
    /// Folder diagnostics: the dotnet root dsm reads vs. the folder(s) dotnetup manages — one line
    /// when they agree, highlighted separate lines when they differ — plus the remembered folder.
    /// </summary>
    internal IReadOnlyList<RootLine> GetRootLines() =>
        BuildRootLineModel(_activeRoot, _trackedRoots, _rememberedRoot);

    internal static IReadOnlyList<RootLine> BuildRootLineModel(
        string? activeRoot, IReadOnlyList<string> trackedRoots, string? rememberedRoot)
    {
        var lines = new List<RootLine>();
        if (activeRoot is null)
            lines.Add(new RootLine("dotnet", "not found on PATH", "", Warn: true));
        else if (trackedRoots.Count == 0)
            lines.Add(new RootLine("dotnet", activeRoot, "(dotnetup adopts it on first install)", Warn: false));
        else if (DotnetUpService.IsManagedInstallRoot(activeRoot, trackedRoots))
            lines.Add(new RootLine("dotnet", activeRoot, "✓ managed by dotnetup", Warn: false));
        else
        {
            lines.Add(new RootLine("dotnet", activeRoot, "≠ dotnetup folder", Warn: true));
            foreach (string root in trackedRoots)
                lines.Add(new RootLine("dotnetup", root, "", Warn: true));
        }

        if (!string.IsNullOrWhiteSpace(rememberedRoot))
            lines.Add(new RootLine("installs", rememberedRoot, "remembered · f:forget", Warn: false));
        return lines;
    }

    private IEnumerable<IRenderable> BuildRootLines()
    {
        int width = ContentWidth();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (RootLine line in GetRootLines())
        {
            string label = line.Label.PadRight(9);
            string suffix = line.Suffix.Length > 0 ? "  " + line.Suffix : "";
            string path = FitPath(AbbreviateHome(line.Path, home), width - label.Length - suffix.Length);
            string pathColor = line.Warn ? Ui.Yellow : Ui.White;
            string suffixColor = line.Warn ? Ui.Yellow : line.Suffix.StartsWith('✓') ? Ui.Green : Ui.Gray;
            yield return new Markup(
                $"[{Ui.Gray}]{Markup.Escape(label)}[/][{pathColor}]{Markup.Escape(path)}[/]" +
                (suffix.Length > 0 ? $"[{suffixColor}]{Markup.Escape(suffix)}[/]" : ""));
        }
    }

    /// <summary>
    /// Usable text width inside the Setup panel: half the screen minus outer padding, borders and
    /// inner padding. Lines are pre-fitted so Spectre never wraps them — the top row's height is
    /// sized from the line count.
    /// </summary>
    private static int ContentWidth()
    {
        int terminalWidth;
        try { terminalWidth = Console.WindowWidth; } catch { terminalWidth = 120; }
        return Math.Max(20, (terminalWidth - 4) / 2 - 4);
    }

    /// <summary>Cuts <paramref name="text"/> to <paramref name="max"/> chars, ending with "…" when shortened.</summary>
    internal static string FitText(string text, int max) =>
        text.Length <= max ? text : max <= 1 ? "…" : text[..(max - 1)] + "…";

    /// <summary>Replaces a leading home directory with <c>~</c>.</summary>
    internal static string AbbreviateHome(string path, string home) =>
        home.Length > 0 && path.StartsWith(home, StringComparison.Ordinal)
            && (path.Length == home.Length || path[home.Length] is '/' or '\\')
            ? "~" + path[home.Length..]
            : path;

    /// <summary>Keeps the end of <paramref name="path"/> (the distinctive part) within <paramref name="max"/> chars.</summary>
    internal static string FitPath(string path, int max)
    {
        if (path.Length <= max) return path;
        if (max <= 1) return "…";
        return "…" + path[^(max - 1)..];
    }

    public string GetStatusHints()
    {
        string hints = _isInstalled ? "u:Update dotnetup  r:Refresh" : "i:Install dotnetup  r:Refresh";
        if (!string.IsNullOrWhiteSpace(_rememberedRoot))
            hints += "  f:Forget install folder";
        return hints;
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

            case ConsoleKey.F when !string.IsNullOrWhiteSpace(_rememberedRoot):
                SettingsStore.Update(s => s.InstallRoot = null);
                Refresh();
                _statusMessage = "Install folder forgotten - dsm asks again if folders differ";
                return KeyResult.Handled;

            default:
                return KeyResult.NotHandled;
        }
    }

    // Installing and updating dotnetup both re-run its bootstrap script.
    private void RequestInstall() => RequestBootstrap();

    private void RequestUpdate() => RequestBootstrap();

    private void RequestBootstrap()
    {
        var (command, args) = DotnetUpService.BootstrapCommand();
        PendingCommand = (command, args, null);
    }
}
