using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

/// <summary>
/// Provides Super Mario-themed UI components for the terminal interface,
/// adapting colors to the active light/dark theme.
/// </summary>
public static class Ui
{
    /// <summary>Whether the terminal supports emoji rendering (Windows Terminal, macOS, Linux).</summary>
    private static readonly bool SupportsEmoji = !OperatingSystem.IsWindows()
        || Environment.GetEnvironmentVariable("WT_SESSION") is not null;

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

    // ── Icons with Windows conhost fallbacks ──────────────────────────
    public static string IconSdks     => SupportsEmoji ? "📦" : ">";
    public static string IconRuntimes => SupportsEmoji ? "⚙"  : "*";
    public static string IconSetup    => SupportsEmoji ? "🔧" : "#";
    public static string IconSearch   => SupportsEmoji ? "🔍" : "/";
    public static string IconResults  => SupportsEmoji ? "📋" : "=";
    public static string IconHeart    => SupportsEmoji ? "\u2764" : "<3";
    public static string IconActive   => SupportsEmoji ? "🍀" : $"[{Green} bold]●[/]";
    public static string IconPreview  => SupportsEmoji ? "🏭" : $"[{Blue} bold]●[/]";
    public static string IconMaint    => SupportsEmoji ? "🚧" : $"[{Yellow} bold]●[/]";
    public static string IconEol      => SupportsEmoji ? "👿" : $"[{Red} bold]●[/]";

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
        string title = $"[{Red} bold].NET SDK Manager[/]  [{DarkGray}]Made with[/] [{Red}]\u2764[/] [{DarkGray}]by[/] [{Blue} italic underline link=https://shahab-the-guy.dev]Shahab the Guy[/]";

        return new Panel(new Markup(title))
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.HeaderBorderColor)
            .Expand();
    }

    /// <summary>
    /// Renders a simple header for the search screen.
    /// </summary>
    public static IRenderable SearchHeader(string? setupInfo)
    {
        string title = $"[{Red} bold].NET SDK Manager[/]  [{DarkGray}]Made with[/] [{Red}]\u2764[/] [{DarkGray}]by[/] [{Blue} italic underline link=https://shahab-the-guy.dev]Shahab the Guy[/]";
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
    public static IRenderable Footer(string hints)
    {
        string global = $"[{DarkGray}]Tab:Switch  F3:Search  F6:Theme  q:Quit[/]";
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
