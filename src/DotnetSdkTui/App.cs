using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;
using DotnetSdkTui.Views;
using DotnetSdkTui.Theme;
using DotnetSdkTui.Services;

namespace DotnetSdkTui;

/// <summary>
/// Main application with two screens:
/// - Main: Copilot-CLI-style tab strip (SDKs / Runtimes / Search) + Setup panel top-right.
///   Only the active tab's body renders; Tab / Shift+Tab cycles tabs.
/// - Brew: separate Homebrew workspace (F2, macOS only).
/// Install/uninstall operations exit TUI to show real terminal output.
/// </summary>
public sealed class App
{
    private readonly SdksView _sdksView;
    private readonly RuntimesView _runtimesView;
    private readonly SearchView _searchView;
    private readonly SetupView _setupView;
    private readonly BrewView _brewView;
    private readonly WorkloadsView _workloadsView;

    private enum Screen { Main, Brew, Workloads }
    private Screen _screen = Screen.Main;

    // Main-screen tabs, cycled by Tab / Shift+Tab.
    private enum MainTab { Sdks, Runtimes, Search }
    private MainTab _tab = MainTab.Sdks;
    private static readonly MainTab[] TabOrder = [MainTab.Sdks, MainTab.Runtimes, MainTab.Search];

    // Nominal screen row (1-based) of the tab strip when the window is tall enough for the layout to
    // render at full size: TopPad(1) + Top(3) + TabsPad(1) puts the Body panel's top border — where
    // the tabs live — on row 6. Spectre compresses the top rows on short/resized windows, so the real
    // row is detected per-frame in EmitFrame (see _tabRow) and this is only the fallback.
    private const int MainTabRow = 6;

    // Actual screen row of the tab strip in the most recently rendered frame, and the plain-text token
    // used to locate it. Recomputed each frame so hit-testing tracks window resizes/compression.
    private int _tabRow = MainTabRow;
    private string _tabStripProbe = "SDKs";

    // Setup panel rectangle in the latest rendered frame (1-based). Recomputed each frame so clicks
    // track resize/compression and focus Setup from any point inside its panel.
    private int _setupTopRow = 2;
    private int _setupBottomRow = 2;
    private Ui.TabHitRegion _setupHitRegion = new(1, 1);

    // Clickable column ranges for each tab, refreshed whenever the main screen is built.
    private IReadOnlyList<Ui.TabHitRegion> _tabHitRegions = [];

    // Setup lives top-right, outside the tab cycle. Press `s` to route keys to it; Esc / `s`
    // to return focus to the active tab.
    private bool _setupFocused;

    private bool _running = true;
    private string _dotnetUpStatus = "checking...";
    private string? _setupInfo;
    private readonly bool _skipSplash;

    // Last terminal size observed at render time; the main loop re-renders when this changes
    // so resizing the window doesn't leave stale geometry on the screen.
    private int _lastWidth;
    private int _lastHeight;
    private volatile bool _resizeSignaled;
    private IDisposable? _winchRegistration;

    // Per-frame back-buffer + last-committed frame. The render path builds the new frame in
    // _frameWriter via a private Spectre console, prefixes/suffixes it with sync-output
    // escapes and resize-strip wipes, and writes the whole thing in a single Console.Write.
    // If the resulting frame is byte-identical to the previous one we skip the syscall.
    private readonly System.IO.StringWriter _frameWriter = new();
    private IAnsiConsole? _renderConsole;
    private string? _lastFrame;

    // Set when the user requests bulk migration (Shift+M); handled by CheckBulkMigrateAsync.
    private IReadOnlyList<SdksView.SdkMigration>? _pendingBulkMigrate;

    public App(bool skipSplash = false)
    {
        _skipSplash = skipSplash;
        _sdksView = new SdksView();
        _runtimesView = new RuntimesView();
        _searchView = new SearchView();
        _setupView = new SetupView();
        _brewView = new BrewView();
        _workloadsView = new WorkloadsView();
    }

    public async Task RunAsync()
    {
        // Ensure dotnet and dotnetup are on PATH (covers dotnetup-managed installs)
        DotnetUpService.RefreshPath();

        // Suppress the first-run experience and .NET logo banner in every child `dotnet`
        // process we spawn — otherwise the multi-line welcome text leaks into single-line
        // reads like `dotnet workload --version` and `dotnet workload config --update-mode`,
        // which corrupts the Workloads panel header.
        Environment.SetEnvironmentVariable("DOTNET_NOLOGO", "1");
        Environment.SetEnvironmentVariable("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1");

        // Ensure UTF-8 output for box-drawing characters on Windows
        if (OperatingSystem.IsWindows())
        {
            try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        }

        try { Console.CursorVisible = false; } catch (IOException) { }

        // Ensure Spectre.Console has valid dimensions (Layout crashes with 0 height in redirected terminals)
        try { _ = Console.WindowHeight; }
        catch
        {
            AnsiConsole.Profile.Width = 120;
            AnsiConsole.Profile.Height = 40;
        }

        ThemeManager.Restore();

        // Graceful Ctrl+C: stop the loop instead of killing the process
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        // Re-render automatically when the terminal is resized. On macOS/Linux the kernel
        // delivers SIGWINCH instantly; on Windows we fall back to size polling in the main loop.
        if (!OperatingSystem.IsWindows())
        {
            try
            {
                _winchRegistration = System.Runtime.InteropServices.PosixSignalRegistration.Create(
                    System.Runtime.InteropServices.PosixSignal.SIGWINCH,
                    _ => _resizeSignaled = true);
            }
            catch { /* signal hookup is a best-effort optimisation */ }
        }

        // Kick off the data loads BEFORE the splash so that by the time the splash
        // animation finishes (~3 s), the SDK / Runtime / Setup data is already cached.
        // ActivateAsync is fire-and-forget — the splash continues to animate normally
        // because Spectre.Console.Live runs on the Console thread and the loads run
        // on the thread pool.
        _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
        _ = LoadSetupInfoAsync();
        _ = AppVersion.CheckForUpdateAsync();
        var prefetch = Task.WhenAll(
            _sdksView.ActivateAsync(),
            _runtimesView.ActivateAsync(),
            _setupView.ActivateAsync());

        if (!_skipSplash)
            await Ui.RenderSplashAsync();

        // Make sure the prefetch is observed (it almost always completed during the splash).
        await prefetch;

        AnsiConsole.Clear();
        // Turn on mouse reporting so the tab strip is clickable. Wrapped in try/catch because
        // redirected or dumb terminals may reject the escape; failure just leaves tabs keyboard-only.
        try { Console.Write(MouseInput.EnableSequence); } catch (IOException) { }

        while (_running)
        {
            // Resize handling — k9s-style. Render each frame to a private in-memory buffer,
            // wrap it with the synchronized-output escape (DEC 2026) plus any pre-/post-frame
            // erases, and emit the whole thing in ONE Console.Write so the terminal sees the
            // new frame as a single atomic update — no row-by-row paint. If the resulting
            // frame is byte-identical to the previous one we skip the write entirely.
            int width = SafeWidth();
            int height = SafeHeight();
            bool resized = _resizeSignaled || width != _lastWidth || height != _lastHeight;
            if (resized) _resizeSignaled = false;

            EmitFrame(BuildScreen(), width, height, resized);

            // Check for pending interactive commands from views
            if (await CheckPendingCommandsAsync())
            {
                AnsiConsole.Clear();
                _lastFrame = null;          // force a real write on the first frame after the prompt
                continue;
            }

            // Check for a pending bulk-migration request (Shift+M)
            if (await CheckBulkMigrateAsync())
            {
                AnsiConsole.Clear();
                _lastFrame = null;
                continue;
            }

            // Check for a pending workload update-mode toggle (Workloads panel `m`)
            if (await CheckWorkloadModeToggleAsync())
            {
                AnsiConsole.Clear();
                _lastFrame = null;
                continue;
            }

            // Check for a pending workloads drill-in request (SDKs panel `w`)
            if (await CheckWorkloadsDrillInAsync())
            {
                AnsiConsole.Clear();
                _lastFrame = null;
                continue;
            }

            // Always poll instead of doing a blocking ReadKey so SIGWINCH (or a Windows size
            // change picked up by TerminalSizeChanged) can interrupt the wait and re-render.
            // Live-update screens use a tight 200 ms re-render cycle; idle screens wake every ~1 s.
            // Polling resolution is 20 ms so a drag during resize never sees more than a one-frame
            // (~50 Hz) lag between the kernel reporting SIGWINCH and us repainting.
            bool tight = (_screen == Screen.Main && _tab == MainTab.Search) || _screen == Screen.Workloads || IsLiveUpdateNeeded();
            var deadline = DateTime.UtcNow.AddMilliseconds(tight ? 200 : 1000);
            while (DateTime.UtcNow < deadline && _running)
            {
                if (_resizeSignaled || TerminalSizeChanged()) break;

                try
                {
                    if (Console.KeyAvailable)
                    {
                        if (TryReadInput(out var key, out var mouse))
                        {
                            if (mouse is { } m) await HandleMouseAsync(m);
                            else await HandleKeyAsync(key);
                        }
                        break;
                    }
                }
                catch (InvalidOperationException) { break; }
                await Task.Delay(20);
            }
        }

        _winchRegistration?.Dispose();
        try { Console.Write(MouseInput.DisableSequence); } catch (IOException) { }
        try { Console.CursorVisible = true; } catch (IOException) { }
        Ui.RenderGoodbye();
    }

    /// <summary>
    /// Wipes the vertical strip of cells <paramref name="newWidth"/>..<paramref name="oldWidth"/>
    /// across the first <paramref name="rows"/> rows. Used when the terminal shrinks horizontally
    /// so stale content from the previous (wider) frame doesn't peek out from behind the new
    /// (narrower) frame. Painting only the orphaned strip — not the full screen — keeps the
    /// repaint invisible to the eye instead of flashing.
    /// </summary>
    /// <summary>
    /// Builds the entire next frame in memory (a private Spectre console writing into
    /// <see cref="_frameWriter"/>), prepends/appends the resize-wipe and synchronized-output
    /// escapes, and emits everything in a single <see cref="Console.Write(string)"/> so the
    /// terminal sees the new frame as one atomic update — the cure for "row-by-row paint"
    /// flicker during a resize drag. If the resulting frame is identical to the previously
    /// committed one, the syscall is skipped entirely.
    /// </summary>
    private void EmitFrame(IRenderable renderable, int width, int height, bool resized)
    {
        if (_renderConsole is null)
        {
            _renderConsole = AnsiConsole.Create(new AnsiConsoleSettings
            {
                Out = new AnsiConsoleOutput(_frameWriter),
                Ansi = AnsiSupport.Yes,
                ColorSystem = ColorSystemSupport.TrueColor,
                Interactive = InteractionSupport.No,
            });
        }
        _renderConsole.Profile.Width = width;
        _renderConsole.Profile.Height = height;

        var body = _frameWriter.GetStringBuilder();
        body.Clear();
        _renderConsole.Write(renderable);
        string bodyText = body.ToString();

        // Spectre terminates its render with a trailing newline. Writing that newline while the
        // cursor sits on the terminal's last row scrolls the whole screen up by one line, which
        // (a) shifts every row up relative to the frame we think we drew — breaking absolute-row
        // mouse hit-testing on the tab strip — and (b) needlessly churns the scrollback. Drop a
        // single trailing newline so the frame occupies exactly its rows and never scrolls.
        if (bodyText.EndsWith('\n')) bodyText = bodyText[..^1];

        // Locate the tab strip's real screen row for click hit-testing. Each '\n' in the emitted body
        // advances one row from cursor-home (row 1), and Spectre may compress the rows above the tab
        // strip on short windows — so we read the row back from the frame we just built.
        if (_screen == Screen.Main)
        {
            _tabRow = FindTabRow(bodyText);
            if (TryFindSetupHitRegion(bodyText, out int setupTopRow, out int setupBottomRow, out Ui.TabHitRegion setupRegion))
            {
                _setupTopRow = setupTopRow;
                _setupBottomRow = setupBottomRow;
                _setupHitRegion = setupRegion;
            }
        }

        var sb = new StringBuilder(bodyText.Length + 256);
        // 1. Begin synchronized output (modern terminals buffer until end-marker; older ones
        //    silently ignore both escapes).
        sb.Append("\x1B[?2026h");
        // 1b. Hide the cursor for the duration of the frame. Some terminals re-enable the
        //     cursor when a program writes past its previous "hidden" state, so we assert
        //     hidden on every frame — DECSET 25 low.
        sb.Append("\x1B[?25l");
        // 2. On horizontal shrink, wipe the orphaned right strip BEFORE drawing the new frame
        //    so it never becomes visible (we're inside the sync region so this is invisible).
        if (resized && _lastWidth > width && _lastHeight > 0)
        {
            int rows = Math.Min(_lastHeight, height);
            int stripCol = width + 1; // 1-based column for ANSI CUP
            for (int row = 1; row <= rows; row++)
            {
                sb.Append("\x1B[").Append(row).Append(';').Append(stripCol).Append('H');
                sb.Append("\x1B[K");
            }
        }
        // 3. Move cursor to top-left, paint the frame, and erase anything below it (handles
        //    a vertical shrink without needing a full-screen clear).
        sb.Append("\x1B[H");
        sb.Append(bodyText);
        sb.Append("\x1B[0J");
        // 4. Commit.
        sb.Append("\x1B[?2026l");

        string frame = sb.ToString();
        _lastWidth = width;
        _lastHeight = height;

        // Frame deduplication: when no input arrived and no live data refreshed, the rebuilt
        // frame is byte-identical to the last commit, so the terminal would just re-paint
        // the same pixels. Skip the syscall and any associated repaint cost entirely.
        if (frame == _lastFrame) return;
        _lastFrame = frame;

        try { Console.Write(frame); } catch (IOException) { }
    }

    private static int SafeWidth()
    {
        try { return Console.WindowWidth; } catch { return 80; }
    }

    private static int SafeHeight()
    {
        try { return Console.WindowHeight; } catch { return 24; }
    }

    private bool TerminalSizeChanged()
    {
        try
        {
            return Console.WindowWidth != _lastWidth || Console.WindowHeight != _lastHeight;
        }
        catch { return false; }
    }

    private IRenderable BuildScreen()
    {
        if (_screen == Screen.Brew)      return BuildBrewScreen();
        if (_screen == Screen.Workloads) return BuildWorkloadsScreen();
        return BuildMainScreen();
    }

    private IRenderable BuildMainScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("TopPad").Size(1),
                new Layout("Top").Size(3),
                new Layout("TabsPad").Size(1),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(2));

        root["TopPad"].Update(new Text(""));
        root["TabsPad"].Update(new Text(""));

        // Top row: mascot column + Welcome (left) + Setup panel (right).
        // The mascot lives outside the Welcome panel so the panel's title stays on ONE line.
        root["Top"].SplitColumns(
            new Layout("Mascot").Size(9),
            new Layout("Welcome"),
            new Layout("Setup"));

        root["Top"]["Mascot"].Update(Ui.MascotArt());
        root["Top"]["Welcome"].Update(Ui.WelcomePanel());
        root["Top"]["Setup"].Update(_setupView.Render(_setupFocused));

        // Body: a single tabbed panel — the tab strip lives inside the panel's top border so
        // the tabs visually "connect" to the panel below.
        root["Body"].Update(BuildTabbedBody());

        // Footer hints reflect whoever currently receives keystrokes.
        IView keyedView = GetKeyedView();
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(keyedView.GetStatusHints())));

        return new Padder(root, new Padding(2, 0, 2, 0));
    }

    private IRenderable BuildTabbedBody()
    {
        bool tabFocused = !_setupFocused;

        IRenderable content = _tab switch
        {
            MainTab.Sdks     => _sdksView.RenderContent(tabFocused),
            MainTab.Runtimes => _runtimesView.RenderContent(tabFocused),
            MainTab.Search   => _searchView.RenderContent(tabFocused),
            _                => _sdksView.RenderContent(tabFocused),
        };

        string[] labels = [$"{Ui.IconSdks} SDKs", $"{Ui.IconRuntimes} Runtimes", $"{Ui.IconSearch} Search"];
        _tabHitRegions = Ui.ComputeTabHitRegions(labels);
        // Plain-text token (the word after the icon) used to locate the tab strip row in the rendered
        // frame — the first tab's label, e.g. "SDKs".
        _tabStripProbe = labels[0][(labels[0].LastIndexOf(' ') + 1)..];

        int activeTabIndex = Array.IndexOf(TabOrder, _tab);
        return Ui.TabbedPanel(
            labels,
            activeTabIndex,
            content,
            focused: tabFocused,
            dimAll: _setupFocused);
    }

    private IRenderable BuildBrewScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("TopPad").Size(1),
                new Layout("Top").Size(3),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(2));

        root["TopPad"].Update(new Text(""));
        root["Top"].Update(Ui.WelcomePanel());
        root["Body"].Update(_brewView.Render(true));
        // While the brew search view is open it's a focused context: drop the workspace-switch hints.
        string brewGlobal = _brewView.IsSearching
            ? $"F1:Help  F6:Theme({ThemeManager.ThemeName})"
            : $"F1:Help  F2:.NET  F3:Search  F6:Theme({ThemeManager.ThemeName})  q:Quit";
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(_brewView.GetStatusHints(), brewGlobal)));

        return new Padder(root, new Padding(2, 0, 2, 0));
    }

    private IRenderable BuildWorkloadsScreen()
    {
        var root = new Layout("Root")
            .SplitRows(
                new Layout("TopPad").Size(1),
                new Layout("Top").Size(3),
                new Layout("Body").MinimumSize(10),
                new Layout("Footer").Size(2));

        root["TopPad"].Update(new Text(""));
        root["Top"].Update(Ui.WelcomePanel());
        root["Body"].Update(_workloadsView.Render(true));
        // Workloads is a drill-in workspace; no cross-workspace shortcuts here except theme + help.
        string wlGlobal = $"F1:Help  F6:Theme({ThemeManager.ThemeName})";
        root["Footer"].Update(new Rows(new Text(""), Ui.Footer(_workloadsView.GetStatusHints(), wlGlobal)));

        return new Padder(root, new Padding(2, 0, 2, 0));
    }

    private async Task HandleKeyAsync(ConsoleKeyInfo key)
    {
        // F1 opens the docs site, regardless of which screen is active.
        if (key.Key == ConsoleKey.F1)
        {
            OpenUrl("https://sdk-manager.net");
            return;
        }

        if (_screen == Screen.Brew)
        {
            await HandleBrewKeyAsync(key);
            return;
        }

        if (_screen == Screen.Workloads)
        {
            await HandleWorkloadsKeyAsync(key);
            return;
        }

        await HandleMainKeyAsync(key);
    }

    /// <summary>
    /// Launches the user's default browser pointing at <paramref name="url"/>. Cross-platform:
    /// macOS uses <c>open</c>, Windows uses <c>cmd /c start</c>, Linux uses <c>xdg-open</c>.
    /// Failures are silently swallowed because opening the docs is a best-effort convenience.
    /// </summary>
    private static void OpenUrl(string url)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            if (OperatingSystem.IsWindows())
            {
                psi.FileName = "cmd";
                psi.Arguments = $"/c start \"\" \"{url}\"";
            }
            else if (OperatingSystem.IsMacOS())
            {
                psi.FileName = "open";
                psi.Arguments = url;
            }
            else
            {
                psi.FileName = "xdg-open";
                psi.Arguments = url;
            }
            System.Diagnostics.Process.Start(psi);
        }
        catch { /* best-effort */ }
    }

    /// <summary>
    /// Switches to <paramref name="tab"/> and returns keystroke focus to the tab body (pulling it
    /// away from Setup if needed). Shared by the Tab key and the clickable tab strip.
    /// </summary>
    private async Task SwitchToTabAsync(MainTab tab)
    {
        _tab = tab;
        // Leaving Setup — pull focus back onto the tab we just landed on.
        _setupFocused = false;
        // Fresh Search activation refreshes the debounce state.
        if (_tab == MainTab.Search) await _searchView.ActivateAsync();
    }

    /// <summary>
    /// Routes mouse reports on the main screen. Left-clicking a tab switches tabs; left-clicking the
    /// Setup panel header gives Setup keyboard focus.
    /// </summary>
    private async Task HandleMouseAsync(MouseInput.MouseEvent m)
    {
        if (!m.IsLeftPress || _screen != Screen.Main) return;

        if (m.Row == _tabRow)
        {
            for (int i = 0; i < _tabHitRegions.Count && i < TabOrder.Length; i++)
            {
                var region = _tabHitRegions[i];
                if (m.Column >= region.Start && m.Column < region.EndExclusive)
                {
                    await SwitchToTabAsync(TabOrder[i]);
                    return;
                }
            }
        }

        if (m.Row >= _setupTopRow && m.Row <= _setupBottomRow
            && m.Column >= _setupHitRegion.Start && m.Column < _setupHitRegion.EndExclusive)
            _setupFocused = true;
    }

    /// <summary>
    /// Reads the next input. Most keys pass straight through, but a mouse report — either SGR
    /// (<c>ESC [ &lt; Cb ; Cx ; Cy M|m</c>) or legacy X10 (<c>ESC [ M b x y</c>) — is decoded into
    /// <paramref name="mouse"/> (see <see cref="MouseInput"/>). A lone Escape or an unrecognised escape
    /// sequence is returned as the Escape key. Returns <c>false</c> only when no input was pending.
    /// </summary>
    private static bool TryReadInput(out ConsoleKeyInfo key, out MouseInput.MouseEvent? mouse)
    {
        key = default;
        mouse = null;

        if (!Console.KeyAvailable) return false;

        ConsoleKeyInfo first = Console.ReadKey(true);
        if (first.Key != ConsoleKey.Escape)
        {
            key = first;
            return true;
        }

        // Possible mouse sequence. The continuation bytes usually arrive in the same terminal write
        // as the ESC, but we wait briefly for each so a split read doesn't misfire a lone Escape.
        if (!TryReadCharWithin(40, out char c1)) { key = first; return true; }
        if (c1 != '[') { key = first; return true; }

        if (!TryReadCharWithin(40, out char c2)) { key = first; return true; }

        if (c2 == '<')
        {
            // SGR (1006) extended encoding.
            var body = new StringBuilder();
            while (TryReadCharWithin(40, out char c))
            {
                body.Append(c);
                if (c is 'M' or 'm') break;
            }
            mouse = MouseInput.ParseSgr(body.ToString());
        }
        else if (c2 == 'M')
        {
            // Legacy X10 encoding: exactly three bytes follow.
            var body = new StringBuilder();
            for (int i = 0; i < 3 && TryReadCharWithin(40, out char c); i++) body.Append(c);
            mouse = MouseInput.ParseX10(body.ToString());
        }

        if (mouse is null) key = first; // not a mouse report — treat the leading ESC as a plain key
        return true;
    }

    /// <summary>Reads the next key's character within <paramref name="ms"/> milliseconds, if one arrives.</summary>
    private static bool TryReadCharWithin(int ms, out char ch)
    {
        ch = '\0';
        DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            if (Console.KeyAvailable) { ch = Console.ReadKey(true).KeyChar; return true; }
            System.Threading.Thread.Sleep(1);
        }
        return false;
    }

    /// <summary>
    /// Returns the 1-based screen row of the tab strip in a freshly rendered <paramref name="bodyText"/>
    /// frame. Each newline advances one row from cursor-home (row 1); the tab strip is the first row
    /// containing the first tab's label token. Falls back to <see cref="MainTabRow"/> if not found.
    /// </summary>
    private int FindTabRow(string bodyText)
    {
        int row = 1;
        int start = 0;
        while (true)
        {
            int nl = bodyText.IndexOf('\n', start);
            string line = nl < 0 ? bodyText[start..] : bodyText[start..nl];
            if (line.Contains(_tabStripProbe, StringComparison.Ordinal)) return row;
            if (nl < 0) break;
            start = nl + 1;
            row++;
        }
        return MainTabRow;
    }

    private static readonly char[] SetupLeftBorders = ['╭', '╔', '┌', '+'];
    private static readonly char[] SetupRightBorders = ['╮', '╗', '┐', '+'];
    private static readonly char[] SetupBottomLeftBorders = ['╰', '╚', '└', '+'];

    private static bool TryFindSetupHitRegion(string bodyText, out int topRow, out int bottomRow, out Ui.TabHitRegion region)
    {
        const string setupProbe = "Setup";
        topRow = 1;
        bottomRow = 1;
        int start = 0;
        while (true)
        {
            int nl = bodyText.IndexOf('\n', start);
            string rawLine = nl < 0 ? bodyText[start..] : bodyText[start..nl];
            string line = StripAnsi(rawLine);

            int setup = line.IndexOf(setupProbe, StringComparison.Ordinal);
            if (setup >= 0)
            {
                int left = FindLastAny(line, setup, SetupLeftBorders);
                int right = FindFirstAny(line, setup + setupProbe.Length, SetupRightBorders);
                int leftIndex = left >= 0 ? left : setup;
                int rightIndex = right >= 0 ? right : setup + setupProbe.Length - 1;

                int colStart = Ui.VisibleWidth(line[..leftIndex]) + 1;
                int colEndExclusive = Ui.VisibleWidth(line[..(rightIndex + 1)]) + 1;

                bottomRow = FindSetupBottomRow(bodyText, topRow, colStart);
                region = new Ui.TabHitRegion(colStart, colEndExclusive);
                return true;
            }

            if (nl < 0) break;
            start = nl + 1;
            topRow++;
        }

        region = default;
        return false;
    }

    private static int FindSetupBottomRow(string bodyText, int setupTopRow, int setupLeftCol)
    {
        int row = 1;
        int start = 0;
        while (row <= setupTopRow)
        {
            int nl = bodyText.IndexOf('\n', start);
            if (nl < 0) return setupTopRow;
            start = nl + 1;
            row++;
        }

        int probeLimit = setupTopRow + 8;
        while (row <= probeLimit)
        {
            int nl = bodyText.IndexOf('\n', start);
            string rawLine = nl < 0 ? bodyText[start..] : bodyText[start..nl];
            string line = StripAnsi(rawLine);

            int leftIndex = FindNthColumnIndex(line, setupLeftCol);
            if (leftIndex >= 0 && IsAny(line[leftIndex], SetupBottomLeftBorders))
                return row;

            if (nl < 0) break;
            start = nl + 1;
            row++;
        }

        return setupTopRow + 2;
    }

    private static int FindLastAny(string line, int endExclusive, IReadOnlyList<char> chars)
    {
        for (int i = Math.Min(endExclusive, line.Length) - 1; i >= 0; i--)
            if (IsAny(line[i], chars)) return i;
        return -1;
    }

    private static int FindFirstAny(string line, int start, IReadOnlyList<char> chars)
    {
        for (int i = Math.Max(start, 0); i < line.Length; i++)
            if (IsAny(line[i], chars)) return i;
        return -1;
    }

    private static bool IsAny(char c, IReadOnlyList<char> chars)
    {
        for (int i = 0; i < chars.Count; i++)
            if (chars[i] == c) return true;
        return false;
    }

    private static int FindNthColumnIndex(string line, int column1Based)
    {
        if (column1Based <= 0) return -1;
        int col = 1;
        for (int i = 0; i < line.Length; i++)
        {
            if (col == column1Based) return i;
            col += Ui.VisibleWidth(line[i].ToString());
        }
        return -1;
    }

    private static string StripAnsi(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\x1B' && i + 1 < text.Length && text[i + 1] == '[')
            {
                i += 2;
                while (i < text.Length)
                {
                    char ch = text[i];
                    if (ch >= '@' && ch <= '~') break;
                    i++;
                }
                continue;
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private async Task HandleMainKeyAsync(ConsoleKeyInfo key)
    {
        IView activeView = GetActiveTabView();
        IView keyedView = GetKeyedView();

        // F2 opens the Homebrew workspace (macOS only). Always available — function keys
        // never collide with a search box's text input.
        if (key.Key == ConsoleKey.F2 && BrewService.IsSupported())
        {
            _screen = Screen.Brew;
            AnsiConsole.Clear();
            await _brewView.ActivateAsync();
            return;
        }

        // F5/F6 cycles theme
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Cycle();
            return;
        }

        // Ctrl+U self-update
        if (key.Key == ConsoleKey.U && key.Modifiers.HasFlag(ConsoleModifiers.Control) && AppVersion.UpdateAvailable)
        {
            ThemeManager.ResetBackground();
            AnsiConsole.Clear();
            Console.CursorVisible = true;
            Console.WriteLine($"Updating dsm from v{AppVersion.Current} to v{AppVersion.LatestAvailable}...");
            Console.WriteLine(new string('-', 60));
            await AppVersion.SelfUpdateAsync();
            Console.WriteLine();
            Console.WriteLine("Update complete. Please restart dsm.");
            _running = false;
            return;
        }

        // Quit (never while a text input owns the keystrokes)
        if (key.Key == ConsoleKey.Q && !keyedView.IsTextInputActive)
        {
            _running = false;
            return;
        }

        // Tab / Shift+Tab cycles tabs. Since Tab is a global navigation gesture in the
        // Copilot-CLI-style tab strip, we honour it even while a view's text input is active
        // (Search reads printable characters, so Tab isn't lost as content).
        if (key.Key == ConsoleKey.Tab)
        {
            int step = key.Modifiers.HasFlag(ConsoleModifiers.Shift) ? -1 : 1;
            int idx = Array.IndexOf(TabOrder, _tab);
            await SwitchToTabAsync(TabOrder[(idx + step + TabOrder.Length) % TabOrder.Length]);
            return;
        }

        // Shift+M: bulk-migrate all unmanaged SDKs into dotnetup (global, any tab).
        if (key.Key == ConsoleKey.M
            && (key.Modifiers.HasFlag(ConsoleModifiers.Shift) || key.KeyChar == 'M')
            && !keyedView.IsTextInputActive)
        {
            if (DotnetUpService.IsInstalled() && _sdksView.HasUnmanaged)
                _pendingBulkMigrate = _sdksView.GetUnmanagedMigrations();
            return;
        }

        // 's' (lowercase) toggles Setup focus so its `i`/`u`/`r` keys can be reached. Skipped
        // while a text input is active so the letter can be typed into the search box.
        if (key.KeyChar == 's' && !keyedView.IsTextInputActive)
        {
            _setupFocused = !_setupFocused;
            return;
        }

        // Escape while Setup is focused returns to the active tab.
        if (key.Key == ConsoleKey.Escape && _setupFocused)
        {
            _setupFocused = false;
            return;
        }

        // Route the key to whichever view currently owns the keystrokes.
        await keyedView.HandleKeyAsync(key);
    }

    private async Task HandleBrewKeyAsync(ConsoleKeyInfo key)
    {
        // F5/F6 cycles theme even in the brew workspace
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Cycle();
            return;
        }

        // F2 toggles back to the .NET workspace — but not while the brew search view is open.
        if (key.Key == ConsoleKey.F2 && !_brewView.IsSearching)
        {
            _screen = Screen.Main;
            AnsiConsole.Clear();
            return;
        }

        var result = await _brewView.HandleKeyAsync(key);

        // Quit from the brew workspace means "go back to main"
        if (result == KeyResult.Quit)
        {
            _screen = Screen.Main;
            AnsiConsole.Clear();
        }
    }

    private async Task HandleWorkloadsKeyAsync(ConsoleKeyInfo key)
    {
        // F5/F6 cycles theme even in the workloads workspace
        if (key.Key is ConsoleKey.F5 or ConsoleKey.F6)
        {
            ThemeManager.Cycle();
            return;
        }

        var result = await _workloadsView.HandleKeyAsync(key);

        // Quit from the workloads workspace means "go back to main"
        if (result == KeyResult.Quit)
        {
            _screen = Screen.Main;
            AnsiConsole.Clear();
        }
    }

    /// <summary>
    /// Checks if any view has a pending interactive command.
    /// If so, exits TUI, runs the command with real terminal output, then resumes.
    /// </summary>
    private async Task<bool> CheckPendingCommandsAsync()
    {
        (string cmd, string args, string? note, string? cwd)? pending = null;

        if (_sdksView.PendingCommand is not null)
        {
            var p = _sdksView.PendingCommand.Value;
            pending = (p.Command, p.Args, p.Note, null);
            _sdksView.ClearPendingCommand();
        }
        else if (_searchView.PendingCommand is not null)
        {
            var p = _searchView.PendingCommand.Value;
            pending = (p.Command, p.Args, p.Note, null);
            _searchView.ClearPendingCommand();
        }
        else if (_runtimesView.PendingCommand is not null)
        {
            var p = _runtimesView.PendingCommand.Value;
            pending = (p.Command, p.Args, p.Note, null);
            _runtimesView.ClearPendingCommand();
        }
        else if (_setupView.PendingCommand is not null)
        {
            var p = _setupView.PendingCommand.Value;
            pending = (p.Command, p.Args, p.Note, null);
            _setupView.ClearPendingCommand();
        }
        else if (_brewView.PendingCommand is not null)
        {
            var p = _brewView.PendingCommand.Value;
            pending = (p.Command, p.Args, p.Note, null);
            _brewView.ClearPendingCommand();
        }
        else if (_workloadsView.PendingCommand is not null)
        {
            // Workloads carry a scratch-dir Cwd so the command resolves against the pinned SDK.
            pending = _workloadsView.PendingCommand;
            _workloadsView.ClearPendingCommand();
        }

        if (pending is null)
            return false;

        await RunInteractiveAndRefreshAsync(pending.Value.cmd, pending.Value.args, pending.Value.note, pending.Value.cwd);
        return true;
    }

    /// <summary>
    /// Exits the TUI, runs an external command with real terminal output, optionally prints a
    /// follow-up note on success, then restores the TUI and refreshes all views.
    /// </summary>
    private async Task RunInteractiveAndRefreshAsync(string cmd, string args, string? note, string? cwd = null)
    {
        // Pin dotnetup installs to the active dotnet root so they show up in `dotnet --list-sdks`.
        args = InstallLocationService.AugmentInstallArgs(cmd, args);

        // Exit TUI, restore terminal to original settings for the external command
        ThemeManager.ResetBackground();
        try { Console.Write(MouseInput.DisableSequence); } catch (IOException) { }
        AnsiConsole.Clear();
        Console.CursorVisible = true;

        Console.WriteLine($"Running: {cmd} {args}");
        Console.WriteLine(new string('-', 60));

        // Brew commands run non-interactively (skip the "Ask mode" y/n prompt).
        IReadOnlyDictionary<string, string>? environment =
            cmd == "brew" ? BrewService.NonInteractiveEnv : null;
        int exitCode = await ProcessRunner.RunInteractiveAsync(cmd, args, workingDirectory: cwd, environment: environment);

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));

        if (exitCode == 0 && !string.IsNullOrWhiteSpace(note))
        {
            Console.WriteLine(note);
            Console.WriteLine(new string('-', 60));
        }

        Console.WriteLine(exitCode == 0
            ? "Completed successfully. Press any key to continue..."
            : $"Failed (exit code {exitCode}). Press any key to continue...");

        try { Console.ReadKey(true); } catch (InvalidOperationException) { }
        try { Console.CursorVisible = false; } catch (IOException) { }
        // Back to the TUI — re-arm mouse reporting for the clickable tab strip.
        try { Console.Write(MouseInput.EnableSequence); } catch (IOException) { }

        // Re-apply theme background before returning to TUI
        ThemeManager.ApplyBackground();

        if (cmd == "brew")
        {
            // Brew commands only affect the Homebrew workspace; skip dotnet PATH logic.
            _brewView.Refresh();
        }
        else
        {
            // Refresh PATH (add dotnetup + dotnet to process PATH and shell profile)
            DotnetUpService.RefreshPath();
            DotnetUpService.EnsurePathInShellProfile();
            _sdksView.Refresh();
            _runtimesView.Refresh();
            _setupView.Refresh();
            _workloadsView.Refresh();
            _dotnetUpStatus = DotnetUpService.IsInstalled() ? "installed" : "not found";
            _ = LoadSetupInfoAsync();
        }
    }

    /// <summary>
    /// If a bulk migration was requested, exits the TUI, shows a confirmation dialog summarising
    /// which versions move to which, and on confirmation migrates all unmanaged SDKs to dotnetup.
    /// </summary>
    private async Task<bool> CheckBulkMigrateAsync()
    {
        var plan = _pendingBulkMigrate;
        _pendingBulkMigrate = null;
        if (plan is null || plan.Count == 0)
            return false;

        // Keep the theme's OSC 11 background in place while the popup shows — otherwise
        // the terminal reverts to its default (usually dark) background and any light-theme
        // foreground colours (near-black) become invisible.
        AnsiConsole.Clear();
        Console.CursorVisible = true;

        var summary = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("Channel")
            .AddColumn("Current version");
        foreach (var m in plan)
            summary.AddRow(Markup.Escape(m.Channel), Markup.Escape(m.CurrentVersion));

        var body = new Rows(
            new Markup($"[{Ui.White} bold]Migrate {plan.Count} unmanaged SDK(s) to dotnetup[/]"),
            new Text(""),
            summary,
            new Text(""),
            new Markup($"[{Ui.Yellow}]This will:[/]"),
            new Markup($"[{Ui.White}]  • Migrate these system SDKs into dotnetup, updating each to the latest patch[/]"),
            new Markup($"[{Ui.White}]  • Possibly include other system installs dotnetup detects[/]"),
            new Markup($"[{Ui.White}]  • Leave your existing copies in place until you remove them yourself[/]"),
            new Markup($"[{Ui.Gray}]dsm keeps dotnetup's dotnet on your PATH. Downloads may be several hundred MB per SDK.[/]"));

        var dialog = new Panel(body)
            .Header($"[{Ui.Yellow} bold] Bulk migrate [/]")
            .Border(BoxBorder.Double)
            .BorderColor(ThemeManager.PanelBorderColor)
            .Padding(2, 1);
        AnsiConsole.Write(new DropShadow(dialog, ThemeManager.ShadowColor, ThemeManager.ModalBackgroundColor));
        AnsiConsole.WriteLine();

        bool confirmed = AnsiConsole.Prompt(
            new ConfirmationPrompt("Migrate all of these to dotnetup now?") { DefaultValue = false });

        if (!confirmed)
        {
            // Nothing ran — restore the TUI background and return to the main screen.
            ThemeManager.ApplyBackground();
            try { Console.CursorVisible = false; } catch (IOException) { }
            return true;
        }

        string channels = string.Join(' ', plan.Select(m => m.Channel).Distinct());
        const string note =
            "dotnetup now manages these SDKs. Your original copies remain on disk; remove the ones you no " +
            "longer need with the official .NET uninstall tool (e.g. 'sudo dotnet-core-uninstall remove " +
            "--sdk <version>'). Those locations may hold other system-installed versions, so don't delete " +
            "whole folders.";

        await RunInteractiveAndRefreshAsync("dotnetup", $"sdk install {channels} --migrate-from-system", note);
        return true;
    }

    /// <summary>
    /// If the Workloads panel staged a machine-wide update-mode toggle (`m`), exits the TUI,
    /// shows a confirmation dialog, and on confirm runs
    /// <c>dotnet workload config --update-mode &lt;target&gt;</c>. The setting affects every
    /// future workload update on this machine, from any tool — so the confirm is mandatory.
    /// </summary>
    private async Task<bool> CheckWorkloadModeToggleAsync()
    {
        string? target = _workloadsView.PendingModeToggle;
        _workloadsView.ClearPendingModeToggle();
        if (target is null) return false;

        var (current, _) = _workloadsView.GetModeToggleSummary();

        // Keep the theme's OSC 11 background in place while the popup shows — otherwise
        // the terminal reverts to its default (usually dark) background and any light-theme
        // foreground colours (near-black) become invisible.
        AnsiConsole.Clear();
        Console.CursorVisible = true;

        // Keep the dialog compact: short bullet summaries + a small explicit width
        // so the Panel hugs its content and the DropShadow falls fully outside the border.
        // All colours route through Ui.* so the popup respects the active theme.
        var body = new Rows(
            new Markup($"[{Ui.White} bold]Change workload update mode[/]"),
            new Text(""),
            new Markup($"  [{Ui.White}]Current:[/]  [{Ui.White}]{Markup.Escape(current)}[/]"),
            new Markup($"  [{Ui.White}]Target:[/]   [{Ui.Green}]{Markup.Escape(target)}[/]"),
            new Text(""),
            new Markup($"[{Ui.Yellow}]Machine-wide setting.[/]"),
            new Markup($"[{Ui.White}]  • [b]workload-set[/] — manifests move together (recommended)[/]"),
            new Markup($"[{Ui.White}]  • [b]manifests[/]    — each manifest updates independently[/]"),
            new Text(""),
            new Markup($"[{Ui.Gray}]Runs: dotnet workload config --update-mode {Markup.Escape(target)}[/]"));

        var dialog = new Panel(body)
            .Header($"[{Ui.Yellow} bold] Workload update mode [/]")
            .Border(BoxBorder.Double)
            .BorderColor(ThemeManager.PanelBorderColor)
            .Expand();
        // Top padding=1 gives breathing room under the header; bottom=0 makes the last content
        // line sit on the row immediately above the border. What LOOKS like extra bottom
        // padding in the rendered image is actually a font-metric artifact (box-drawing
        // glyphs sit near the top of their cell, text baselines sit lower). Verified via
        // SVG y-coordinates: text row and border row are exactly one cell (~18px) apart.
        dialog.Padding = new Padding(2, 1, 2, 0);

        int terminalWidth;
        try { terminalWidth = Console.WindowWidth; } catch { terminalWidth = 80; }
        // Cap the dialog to ~2/3 of the terminal (min 60, max 80) so the shadow never
        // touches the edge and the border always hugs the content, then center it.
        int dialogWidth = Math.Clamp(terminalWidth * 2 / 3, 60, 80);
        // A single-column Grid of the chosen width lets Panel.Expand() fill exactly
        // that column (not the whole terminal). The surrounding Grid centers the dialog
        // horizontally by allocating equal-flex empty columns on either side.
        var dialogGrid = new Grid().AddColumn(new GridColumn().Width(dialogWidth));
        dialogGrid.AddRow(dialog);

        int side = Math.Max(0, (terminalWidth - dialogWidth - 2) / 2);   // -2 leaves room for shadow
        var centered = new Padder(new DropShadow(dialogGrid, ThemeManager.ShadowColor, ThemeManager.ModalBackgroundColor),
                                  new Padding(side, 0, 0, 0));
        AnsiConsole.Write(centered);
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{Ui.White}]Change workload update mode to[/] [{Ui.Green}]'{Markup.Escape(target)}'[/] [{Ui.White}]now?[/] [{Ui.Gray}](y/N, Esc to cancel)[/]");

        // Custom key loop so Esc cleanly cancels the change and returns to the Workloads panel.
        // Spectre's built-in ConfirmationPrompt doesn't treat Esc as "no" — it just ignores it.
        bool confirmed = false;
        while (true)
        {
            ConsoleKeyInfo k;
            try { k = Console.ReadKey(intercept: true); }
            catch (InvalidOperationException) { break; }

            if (k.Key is ConsoleKey.Escape or ConsoleKey.Enter || k.KeyChar is 'n' or 'N')
                break;                            // cancel — return to workloads unchanged
            if (k.KeyChar is 'y' or 'Y')
            {
                confirmed = true;
                break;
            }
            // Ignore anything else and keep waiting.
        }

        if (!confirmed)
        {
            ThemeManager.ApplyBackground();
            try { Console.CursorVisible = false; } catch (IOException) { }
            return true;
        }

        var (cmd, args, note, cwd) = WorkloadService.BuildSetUpdateMode(target);
        await RunInteractiveAndRefreshAsync(cmd, args, note, cwd);
        return true;
    }

    /// <summary>
    /// If the SDKs panel requested a workloads drill-in (via <c>w</c> on an installed row),
    /// activates the Workloads workspace scoped to that SDK and switches screens.
    /// </summary>
    private async Task<bool> CheckWorkloadsDrillInAsync()
    {
        string? sdk = _sdksView.PendingWorkloadsSdk;
        _sdksView.ClearPendingWorkloadsSdk();
        if (sdk is null) return false;

        _screen = Screen.Workloads;
        AnsiConsole.Clear();
        await _workloadsView.ActivateForSdkAsync(sdk);
        return true;
    }

    private IView GetActiveTabView() => _tab switch
    {
        MainTab.Sdks     => _sdksView,
        MainTab.Runtimes => _runtimesView,
        MainTab.Search   => _searchView,
        _                => _sdksView,
    };

    /// <summary>
    /// The view that currently receives keystrokes: Setup when it's focused, otherwise the
    /// active tab's view.
    /// </summary>
    private IView GetKeyedView() => _setupFocused ? _setupView : GetActiveTabView();

    private bool IsLiveUpdateNeeded()
    {
        return _sdksView.NeedsLiveUpdate
            || _runtimesView.NeedsLiveUpdate
            || _searchView.NeedsLiveUpdate
            || _brewView.NeedsLiveUpdate
            || _workloadsView.NeedsLiveUpdate
            || AppVersion.CheckInProgress;
    }

    private async Task LoadSetupInfoAsync()
    {
        try
        {
            if (DotnetUpService.IsInstalled())
            {
                var info = await DotnetUpService.GetInfoAsync();
                if (info is not null)
                    _setupInfo = $"v{info.Version}  {info.Architecture}  {info.Rid}";
            }
        }
        catch { }
    }
}
