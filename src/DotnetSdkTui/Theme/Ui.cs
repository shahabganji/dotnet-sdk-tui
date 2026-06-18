using System.Globalization;
using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

/// <summary>
/// A single cell in a <see cref="Ui.SelectableTable"/> row.
/// </summary>
/// <param name="Text">Cell content. When <see cref="IsMarkup"/> is <c>false</c> this is plain text
/// that gets escaped and (for unselected rows) colored with <see cref="Color"/>; when <c>true</c> it is
/// emitted verbatim as already-styled markup (e.g. an emoji or a colored glyph).</param>
/// <param name="Color">Foreground markup style applied to plain-text cells in unselected rows.</param>
/// <param name="IsMarkup">Whether <see cref="Text"/> is pre-formatted markup.</param>
public readonly record struct Cell(string Text, string Color, bool IsMarkup = false);

/// <summary>
/// Provides Super Mario-themed UI components for the terminal interface,
/// adapting colors to the active light/dark theme.
/// </summary>
public static class Ui
{
    /// <summary>Whether the terminal supports emoji rendering (never on Windows due to font issues).</summary>
    private static readonly bool SupportsEmoji = !OperatingSystem.IsWindows();

    // Convenience accessors that delegate to ThemeManager
    /// <summary>Red accent color.</summary>
    public static string Red => ThemeManager.MarioRed;

    /// <summary>Blue info color, adapts to theme.</summary>
    public static string Blue => ThemeManager.InfoColor;

    /// <summary>Yellow section title color, adapts to theme.</summary>
    public static string Yellow => ThemeManager.SectionTitle;

    /// <summary>Green success color, adapts to theme.</summary>
    public static string Green => ThemeManager.SuccessColor;

    /// <summary>Gold accent color, adapts to theme.</summary>
    public static string Gold => ThemeManager.AccentColor;

    /// <summary>Brown border color.</summary>
    public static string Brown => ThemeManager.MarioBrown;

    /// <summary>Primary foreground text color, adapts to theme.</summary>
    public static string White => ThemeManager.Foreground;

    /// <summary>Muted text color for secondary content.</summary>
    public static string Gray => ThemeManager.Muted;

    /// <summary>Dim text color for tertiary content.</summary>
    public static string DarkGray => ThemeManager.DimText;

    /// <summary>Full markup style for text in the selected (highlighted) row: themed text color on the highlight bar, bold.</summary>
    public static string Selected => $"{ThemeManager.SelectedRowText} on {ThemeManager.SelectedRowBg} bold";

    // ── Icons with Windows conhost fallbacks ──────────────────────────
    public static string IconSdks     => SupportsEmoji ? "📦" : "\u25c6";
    public static string IconRuntimes => SupportsEmoji ? "⚙"  : "\u25cb";
    public static string IconSetup    => SupportsEmoji ? "🔧" : "\u2666";
    public static string IconSearch   => SupportsEmoji ? "🔍" : "\u25b7";
    public static string IconResults  => SupportsEmoji ? "📋" : "\u2261";
    public static string IconHeart    => "\u2764";
    public static string IconActive   => SupportsEmoji ? "🍀" : $"[{Green}]\u2713[/]";
    public static string IconPreview  => SupportsEmoji ? "🏭" : $"[{Blue}]\u25cb[/]";
    public static string IconMaint    => SupportsEmoji ? "🚧" : $"[{Yellow}]\u25b3[/]";
    public static string IconEol      => SupportsEmoji ? "👿" : $"[{Red}]\u2717[/]";

    // Teal-to-lime gradient inspired by shahab-the-guy.dev banner
    private const string BannerPrimary = "#1DB9A0";   // Full blocks █ (teal)
    private const string BannerDark = "#148F7B";       // Half blocks ▀ (darker teal shadow)
    private const string BannerShine = "#C8E64D";      // Shine sweep highlight (lime-yellow)
    private const int BannerRowCount = 6;

    // Block letter definitions: each letter is 6 rows with ▀ shadow for 3D depth.
    private static readonly Dictionary<char, string[]> BannerFont = new()
    {
        ['.'] = ["  ", "  ", "  ", "  ", "██", "▀▀"],
        ['N'] = ["██   ██", "███  ██", "████ ██", "██ ████", "██  ███", "▀▀   ▀▀"],
        ['E'] = ["███████", "██▀▀▀▀▀", "█████  ", "██▀▀▀  ", "███████", "▀▀▀▀▀▀▀"],
        ['T'] = ["███████", "▀▀███▀▀", "  ███  ", "  ███  ", "  ███  ", "  ▀▀▀  "],
        ['S'] = ["███████", "██▀▀▀▀▀", "███████", "▀▀▀▀▀██", "███████", "▀▀▀▀▀▀▀"],
        ['D'] = ["██████ ", "██▀▀▀██", "██   ██", "██   ██", "██████ ", "▀▀▀▀▀▀ "],
        ['K'] = ["██  ██ ", "██ ██  ", "████   ", "██▀██  ", "██  ██ ", "▀▀  ▀▀ "],
    };

    private static readonly char[] BannerLetters = ['.', 'N', 'E', 'T', ' ', 'S', 'D', 'K'];
    private static readonly string[] BannerLines = ComposeBannerLines();
    private static readonly int[] LetterPositions = ComputeLetterPositions();

    /// <summary>
    /// Renders the startup splash with an Aspire-style animated banner using Live display.
    /// 5-phase animation: empty panel → typewriter welcome → letter reveal → version slide → shine sweep.
    /// </summary>
    public static async Task RenderSplashAsync()
    {
        AnsiConsole.Clear();
        Console.CursorVisible = false;

        const string welcomeText = "Welcome to the";
        const string versionText = "Manager";
        var bannerWidth = BannerLines[0].TrimEnd().Length;
        var versionPadding = Math.Max(0, bannerWidth - versionText.Length);

        await AnsiConsole.Live(CreateBannerPanel(CreateEmptyFrame()))
            .AutoClear(false)
            .StartAsync(async ctx =>
            {
                // Phase 1: Empty panel
                ctx.UpdateTarget(CreateBannerPanel(CreateEmptyFrame()));
                await BannerDelayAsync(80);

                // Phase 2: Welcome text typewriter
                for (int i = 1; i <= welcomeText.Length; i += 3)
                {
                    var partial = welcomeText[..Math.Min(i, welcomeText.Length)];
                    ctx.UpdateTarget(CreateBannerPanel(CreateWelcomeFrame(partial)));
                    await BannerDelayAsync(40);
                }

                // Phase 3: Letter reveal one by one
                for (int letterIdx = 0; letterIdx <= LetterPositions.Length; letterIdx++)
                {
                    int visibleCols = letterIdx < LetterPositions.Length
                        ? LetterPositions[letterIdx]
                        : BannerLines[0].Length;
                    ctx.UpdateTarget(CreateBannerPanel(CreateLetterRevealFrame(welcomeText, visibleCols)));
                    await BannerDelayAsync(70);
                }

                // Phase 4: Version text slides in from right
                for (int i = 1; i <= 8; i++)
                {
                    int visibleChars = (int)Math.Ceiling((double)versionText.Length * i / 8);
                    var partialVer = versionText[(versionText.Length - visibleChars)..];
                    var verPad = Math.Max(0, bannerWidth - partialVer.Length);
                    ctx.UpdateTarget(CreateBannerPanel(CreateFullFrame(welcomeText, partialVer, verPad, -1)));
                    await BannerDelayAsync(50);
                }

                // Phase 5: Shine sweep across block letters
                for (int shineCol = 0; shineCol <= bannerWidth; shineCol += 3)
                {
                    ctx.UpdateTarget(CreateBannerPanel(CreateFullFrame(welcomeText, versionText, versionPadding, shineCol)));
                    await BannerDelayAsync(35);
                }

                // Final frame (no shine) — hold briefly
                ctx.UpdateTarget(CreateBannerPanel(CreateFullFrame(welcomeText, versionText, versionPadding, -1)));
                await BannerDelayAsync(800);
            });

        AnsiConsole.Clear();
    }

    private static async Task BannerDelayAsync(int ms)
    {
        try { await Task.Delay(ms); }
        catch (TaskCanceledException) { }
    }

    private static Panel CreateBannerPanel(IRenderable content)
    {
        return new Panel(content)
            .Border(BoxBorder.Rounded)
            .BorderColor(Color.Grey)
            .Padding(2, 1);
    }

    private static Rows CreateEmptyFrame()
    {
        var elements = new List<IRenderable> { new Text("") };
        for (int i = 0; i < BannerRowCount; i++)
            elements.Add(new Text(""));
        elements.Add(new Text(""));
        return new Rows(elements);
    }

    private static Rows CreateWelcomeFrame(string partialWelcome)
    {
        var elements = new List<IRenderable>
        {
            new Markup($"[white]{Markup.Escape(partialWelcome)}[/]")
        };
        for (int i = 0; i < BannerRowCount; i++)
            elements.Add(new Text(""));
        elements.Add(new Text(""));
        return new Rows(elements);
    }

    private static Rows CreateLetterRevealFrame(string welcomeText, int visibleCols)
    {
        var elements = new List<IRenderable>
        {
            new Markup($"[white]{Markup.Escape(welcomeText)}[/]")
        };
        foreach (var line in BannerLines)
        {
            var partial = visibleCols >= line.Length
                ? line
                : line[..visibleCols].PadRight(line.Length);
            elements.Add(new Markup(BuildLineMarkup(partial, -1)));
        }
        elements.Add(new Text(""));
        return new Rows(elements);
    }

    private static Rows CreateFullFrame(string welcomeText, string versionText, int versionPadding, int shineCol)
    {
        var elements = new List<IRenderable>
        {
            new Markup($"[white]{Markup.Escape(welcomeText)}[/]")
        };
        foreach (var line in BannerLines)
        {
            elements.Add(new Markup(BuildLineMarkup(line, shineCol)));
        }
        elements.Add(new Markup($"[white]{new string(' ', versionPadding)}{Markup.Escape(versionText)}[/]"));
        return new Rows(elements);
    }

    private static string BuildLineMarkup(string line, int shineCol)
    {
        var sb = new StringBuilder();
        for (int col = 0; col < line.Length; col++)
        {
            char c = line[col];
            if (c == ' ')
            {
                sb.Append(' ');
                continue;
            }

            string color;
            if (shineCol >= 0 && col >= shineCol && col < shineCol + 3)
                color = BannerShine;
            else if (c == '▀')
                color = BannerDark;
            else
                color = BannerPrimary;

            string charStr = c switch
            {
                '[' => "[[",
                ']' => "]]",
                _ => c.ToString()
            };
            sb.Append($"[{color}]{charStr}[/]");
        }
        return sb.ToString();
    }

    private static string[] ComposeBannerLines()
    {
        var lines = new string[BannerRowCount];
        for (int row = 0; row < BannerRowCount; row++)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < BannerLetters.Length; i++)
            {
                char ch = BannerLetters[i];
                if (ch == ' ')
                {
                    sb.Append("   ");
                }
                else
                {
                    sb.Append(BannerFont[ch][row]);
                    if (i < BannerLetters.Length - 1 && BannerLetters[i + 1] != ' ')
                        sb.Append(' ');
                }
            }
            lines[row] = sb.ToString();
        }
        return lines;
    }

    private static int[] ComputeLetterPositions()
    {
        var positions = new List<int>();
        int col = 0;
        for (int i = 0; i < BannerLetters.Length; i++)
        {
            char ch = BannerLetters[i];
            if (ch == ' ')
            {
                col += 3;
                continue;
            }
            positions.Add(col);
            col += BannerFont[ch][0].Length;
            if (i < BannerLetters.Length - 1 && BannerLetters[i + 1] != ' ')
                col += 1;
        }
        return positions.ToArray();
    }

    /// <summary>
    /// Renders the welcome info panel (left side of header row).
    /// </summary>
    public static IRenderable WelcomePanel()
    {
        string version = Services.AppVersion.Current;
        string title = $"[{Red} bold].NET SDK Manager[/] [{DarkGray}]v{version}[/]  [{DarkGray}]Made with[/] [{Red}]{IconHeart}[/] [{DarkGray}]by[/] [{Blue} italic underline link=https://shahab-the-guy.dev]Shahab the Guy[/]";

        var panel = new Panel(new Markup(title))
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.HeaderBorderColor)
            .Expand();

        if (Services.AppVersion.UpdateAvailable)
            panel.Header($"[{Gold} bold] \u2b06 v{Services.AppVersion.LatestAvailable} available (Ctrl+U) [/]", Justify.Right);

        return panel;
    }

    /// <summary>
    /// Renders a simple header for the search screen.
    /// </summary>
    public static IRenderable SearchHeader(string? setupInfo)
    {
        string version = Services.AppVersion.Current;
        string title = $"[{Red} bold].NET SDK Manager[/] [{DarkGray}]v{version}[/]  [{DarkGray}]Made with[/] [{Red}]{IconHeart}[/] [{DarkGray}]by[/] [{Blue} italic underline link=https://shahab-the-guy.dev]Shahab the Guy[/]";
        string setup = setupInfo is not null
            ? $"[{Green} bold]dotnetup[/] [{White}]{Markup.Escape(setupInfo)}[/]"
            : $"[{Green}]dotnetup[/]";

        var left = new Panel(new Markup(title))
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.HeaderBorderColor)
            .Expand();

        var right = new Panel(new Markup(setup))
            .Header($"[{Yellow} bold]Setup[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor)
            .Expand();

        return new Columns(left, right);
    }

    /// <summary>
    /// Renders the footer with view-specific hints and global shortcuts.
    /// </summary>
    public static IRenderable Footer(string hints, string? globalOverride = null)
    {
        // Homebrew is macOS-only, so only advertise the F2 workspace there.
        string globalText = globalOverride ?? (OperatingSystem.IsMacOS()
            ? "Tab:Switch  F2:Brew  F3:Search  F6:Theme  q:Quit"
            : "Tab:Switch  F3:Search  F6:Theme  q:Quit");
        string global = $"[{DarkGray}]{Markup.Escape(globalText)}[/]";
        string hintMarkup = $"[{Gold}]{hints}[/]";
        return new Markup($" {hintMarkup}  {global}");
    }

    /// <summary>
    /// Creates a styled table with the Mario theme border and column headers.
    /// </summary>
    public static Table StyledTable(params string[] columns)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor);

        foreach (string col in columns)
        {
            table.AddColumn(new TableColumn($"[{Yellow} bold]{Markup.Escape(col)}[/]").Padding(1, 0, 1, 1));
        }

        return table;
    }

    /// <summary>Horizontal padding inside each column cell (matches <see cref="StyledTable"/>).</summary>
    private const int CellPad = 1;

    /// <summary>
    /// Renders a rounded, fully-bordered table (matching <see cref="StyledTable"/>'s frame, header and
    /// column dividers) in which the selected row is drawn as a single solid Norton Commander-style
    /// highlight bar: the vertical dividers cross every other row but give way to the bar on the
    /// selected one, while the outer frame stays intact.
    /// </summary>
    /// <param name="headers">Column headers (an empty string yields a header-less column, e.g. an icon column).</param>
    /// <param name="rows">The rows, each carrying its cells and whether it is the selected row.</param>
    public static IRenderable SelectableTable(IReadOnlyList<string> headers, IReadOnlyList<(Cell[] Cells, bool Selected)> rows)
    {
        int n = headers.Count;

        // Column = widest content + left/right padding, just like a Spectre table cell.
        int[] col = new int[n];
        for (int j = 0; j < n; j++)
            col[j] = VisibleWidth(headers[j]);
        foreach (var (cells, _) in rows)
            for (int j = 0; j < n && j < cells.Length; j++)
                col[j] = Math.Max(col[j], VisibleWidth(cells[j].IsMarkup ? cells[j].Text : Markup.Escape(cells[j].Text)));
        for (int j = 0; j < n; j++)
            col[j] += 2 * CellPad;

        string tb = ThemeManager.TableBorder;
        string vbar = $"[{tb}]│[/]";
        string selDivider = $"[{tb} on {ThemeManager.SelectedRowBg}]│[/]"; // divider kept on the highlight bar
        string leftPad = new string(' ', CellPad);

        string Rule(char l, char m, char r)
        {
            var s = new StringBuilder().Append(l);
            for (int j = 0; j < n; j++)
            {
                s.Append('─', col[j]);
                s.Append(j < n - 1 ? m : r);
            }
            return $"[{tb}]{s}[/]";
        }

        var lines = new List<IRenderable> { new Markup(Rule('╭', '┬', '╮')) };

        // Header row + separator rule.
        var hb = new StringBuilder(vbar);
        for (int j = 0; j < n; j++)
        {
            hb.Append(leftPad)
              .Append($"[{Yellow} bold]{Markup.Escape(headers[j])}[/]")
              .Append(new string(' ', col[j] - CellPad - VisibleWidth(headers[j])))
              .Append(vbar);
        }
        lines.Add(new Markup(hb.ToString()));
        lines.Add(new Markup(Rule('├', '┼', '┤')));

        // Data rows.
        for (int r = 0; r < rows.Count; r++)
        {
            var (cells, selected) = rows[r];
            var sb = new StringBuilder(vbar);   // left frame border (kept on every row)
            for (int j = 0; j < n; j++)
            {
                Cell cell = j < cells.Length ? cells[j] : new Cell(string.Empty, White);
                string disp = cell.IsMarkup ? cell.Text : Markup.Escape(cell.Text);
                int rightFill = Math.Max(0, col[j] - CellPad - VisibleWidth(disp));

                // The cell body sits on the highlight bar when selected; the column divider stays
                // visible on the bar (border color over the selection background).
                string body = $"{leftPad}{(selected || cell.IsMarkup ? disp : $"[{cell.Color}]{disp}[/]")}{new string(' ', rightFill)}";
                sb.Append(selected ? $"[{Selected}]{body}[/]" : body);

                if (j < n - 1) sb.Append(selected ? selDivider : vbar);
            }
            sb.Append(vbar);                    // right frame border
            lines.Add(new Markup(sb.ToString()));
        }

        lines.Add(new Markup(Rule('╰', '┴', '╯')));
        return new Rows(lines);
    }

    /// <summary>
    /// Visible cell width of a markup string: strips <c>[…]</c> style tags (honoring <c>[[</c>/<c>]]</c>
    /// escapes) and counts East-Asian-wide and emoji code points as two columns.
    /// </summary>
    public static int VisibleWidth(string markup)
    {
        var text = new StringBuilder(markup.Length);
        for (int i = 0; i < markup.Length; i++)
        {
            char c = markup[i];
            if (c == '[')
            {
                if (i + 1 < markup.Length && markup[i + 1] == '[') { text.Append('['); i++; }
                else { int close = markup.IndexOf(']', i); if (close < 0) break; i = close; }
            }
            else if (c == ']')
            {
                if (i + 1 < markup.Length && markup[i + 1] == ']') { text.Append(']'); i++; }
            }
            else
            {
                text.Append(c);
            }
        }

        int w = 0;
        var en = StringInfo.GetTextElementEnumerator(text.ToString());
        while (en.MoveNext())
        {
            string el = (string)en.Current;
            w += IsWide(char.ConvertToUtf32(el, 0)) ? 2 : 1;
        }
        return w;
    }

    private static bool IsWide(int cp) =>
        cp >= 0x1100 && (
            cp <= 0x115F ||                          // Hangul Jamo
            cp is 0x2329 or 0x232A ||
            (cp >= 0x2E80 && cp <= 0xA4CF && cp != 0x303F) ||  // CJK … Yi
            (cp >= 0xAC00 && cp <= 0xD7A3) ||        // Hangul Syllables
            (cp >= 0xF900 && cp <= 0xFAFF) ||        // CJK Compatibility Ideographs
            (cp >= 0xFE30 && cp <= 0xFE4F) ||        // CJK Compatibility Forms
            (cp >= 0xFF00 && cp <= 0xFF60) ||        // Fullwidth Forms
            (cp >= 0xFFE0 && cp <= 0xFFE6) ||
            (cp >= 0x1F300 && cp <= 0x1FAFF) ||      // emoji & pictographs
            (cp >= 0x20000 && cp <= 0x3FFFD));       // CJK Extension B+


    /// <summary>
    /// Wraps a switchable view's content in a focus-aware panel, Norton Commander-style.
    /// <para>
    /// The focused view is marked with a bright <see cref="BoxBorder.Double"/> (double-line)
    /// frame drawn in a vivid green with a bold decoration, plus a filled <c>●</c> indicator
    /// and a bold yellow title — the classic NC "active panel" look.
    /// </para>
    /// <para>
    /// Unfocused views recede into the background: a thin <see cref="BoxBorder.Rounded"/> border
    /// drawn in a desaturated slate/grey with a dim decoration, plus a muted hollow <c>○</c>
    /// indicator and a dimmed title.
    /// </para>
    /// Panels deliberately carry no drop-shadow; shadows are reserved for popup dialogs.
    /// </summary>
    /// <param name="icon">Leading icon for the header (e.g. an emoji or glyph).</param>
    /// <param name="title">View title shown in the header.</param>
    /// <param name="content">The rendered view body.</param>
    /// <param name="focused">Whether this view currently holds focus.</param>
    public static IRenderable ViewPanel(string icon, string title, IRenderable content, bool focused)
    {
        string indicator = focused
            ? $"[{Green} bold]●[/] "   // ● filled, vivid
            : $"[{DarkGray}]○[/] ";    // ○ hollow, dim
        string titleStyle = focused ? $"{Yellow} bold" : DarkGray;

        var borderStyle = focused
            ? new Style(ThemeManager.FocusedBorderColor, decoration: Decoration.Bold)
            : new Style(ThemeManager.UnfocusedBorderColor, decoration: Decoration.Dim);

        // Trailing space after the title keeps it off the corner of the double border.
        return new Panel(content)
            .Header($"{indicator}[{titleStyle}]{icon} {Markup.Escape(title)} [/]")
            .Border(focused ? BoxBorder.Double : BoxBorder.Rounded)
            .BorderStyle(borderStyle)
            .Expand();
    }

    /// <summary>
    /// Creates a content panel with a themed title header.
    /// </summary>
    public static Panel ContentPanel(string title, IRenderable content)
    {
        return new Panel(content)
            .Header($"[{Red} bold] ★ {Markup.Escape(title)} ★ [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.PanelBorderColor)
            .Expand();
    }

    /// <summary>
    /// Creates a section panel with a yellow title header (used for sub-sections).
    /// </summary>
    public static Panel SectionPanel(string title, IRenderable content)
    {
        return new Panel(content)
            .Header($"[{Yellow} bold] {Markup.Escape(title)} [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor)
            .Expand();
    }

    /// <summary>Formats a success message with a mushroom icon.</summary>
    public static Markup Success(string message) =>
        new($"[{Green}]🍄 {Markup.Escape(message)}[/]");

    /// <summary>Formats an error message with a fire icon.</summary>
    public static Markup Error(string message) =>
        new($"[{ThemeManager.ErrorColor}]🔥 {Markup.Escape(message)}[/]");

    /// <summary>Formats an informational message with a star icon.</summary>
    public static Markup Info(string message) =>
        new($"[{Blue}]★ {Markup.Escape(message)}[/]");

    /// <summary>Formats a coin/status message with a bullet icon.</summary>
    public static Markup Coin(string message) =>
        new($"[{Gold}]● {Markup.Escape(message)}[/]");

    /// <summary>Formats a muted/secondary message in gray.</summary>
    public static Markup Muted(string message) =>
        new($"[{Gray}]{Markup.Escape(message)}[/]");

    /// <summary>
    /// Truncates a file path to fit within the given character limit,
    /// replacing the home directory with <c>~</c>.
    /// </summary>
    public static string TruncatePath(string path, int max)
    {
        if (path.Length <= max) return path;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (path.StartsWith(home, StringComparison.OrdinalIgnoreCase))
        {
            path = "~" + path[home.Length..];
        }

        return path.Length <= max ? path : "..." + path[^(max - 3)..];
    }

    /// <summary>
    /// Formats an ISO date string (e.g. "2028-11-14") to "14 November 2028" format.
    /// Returns the original string if parsing fails or input is "-".
    /// </summary>
    public static string FormatDate(string? date)
    {
        if (string.IsNullOrWhiteSpace(date) || date == "-")
            return "-";

        if (DateTime.TryParse(date, out DateTime parsed))
            return parsed.ToString("dd MMMM yyyy");

        return date;
    }

    /// <summary>
    /// Renders a goodbye message when the application exits.
    /// </summary>
    public static void RenderGoodbye()
    {
        ThemeManager.ResetBackground();
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [{BannerPrimary}]Thanks for using[/] [{Red} bold].NET SDK Manager[/]");
        AnsiConsole.MarkupLine($"  [{DarkGray}]Made with[/] [{Red}]\u2764[/] [{DarkGray}]by[/] [{Blue} italic underline link=https://shahab-the-guy.dev]Shahab the Guy[/]");
        AnsiConsole.WriteLine();
    }
}
