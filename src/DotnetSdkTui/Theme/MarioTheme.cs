using System.Text;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

/// <summary>
/// Provides Super Mario-themed UI components for the terminal interface,
/// adapting colors to the active light/dark theme.
/// </summary>
public static class MarioTheme
{
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

    // .NET brand purple — two shades for Aspire-style 3D pixel effect
    private const string BannerPrimary = "#512BD4";
    private const string BannerHighlight = "#9B8ADE";

    // Block letter definitions: each letter is 5 rows, 7 chars wide.
    // Two words rendered as two lines: ".NET SDK" and "MANAGER".
    private static readonly Dictionary<char, string[]> BannerFont = new()
    {
        ['.'] = ["       ", "       ", "       ", "  ██   ", "  ██   "],
        ['N'] = ["██   ██", "███  ██", "██ █ ██", "██  ███", "██   ██"],
        ['E'] = ["███████", "██     ", "█████  ", "██     ", "███████"],
        ['T'] = ["███████", "  ███  ", "  ███  ", "  ███  ", "  ███  "],
        ['S'] = [" █████ ", "██     ", " █████ ", "     ██", " █████ "],
        ['D'] = ["██████ ", "██   ██", "██   ██", "██   ██", "██████ "],
        ['K'] = ["██  ██ ", "██ ██  ", "████   ", "██ ██  ", "██  ██ "],
        ['M'] = ["██   ██", "███ ███", "██ █ ██", "██   ██", "██   ██"],
        ['A'] = [" █████ ", "██   ██", "███████", "██   ██", "██   ██"],
        ['G'] = [" █████ ", "██     ", "██ ████", "██   ██", " █████ "],
        ['R'] = ["██████ ", "██   ██", "██████ ", "██  ██ ", "██   ██"],
    };

    private static readonly string[][] BannerWords = [
        [".","N","E","T"," ","S","D","K"],
        ["M","A","N","A","G","E","R"],
    ];

    /// <summary>
    /// Renders the startup splash animation with an Aspire-style block letter banner.
    /// Uses filled █ characters with two shades of .NET purple for a 3D pixel-art effect.
    /// Spells ".NET SDK" on line 1, "MANAGER" on line 2, animated row by row.
    /// </summary>
    public static async Task RenderSplashAsync()
    {
        AnsiConsole.Clear();
        Console.CursorVisible = false;

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"  [{BannerHighlight}]Welcome to the[/]");
        AnsiConsole.WriteLine();

        // Render each word line
        foreach (var wordLetters in BannerWords)
        {
            for (int row = 0; row < 5; row++)
            {
                var line = new StringBuilder("  ");
                for (int i = 0; i < wordLetters.Length; i++)
                {
                    char ch = wordLetters[i][0];
                    if (ch == ' ')
                    {
                        line.Append("   "); // word gap
                    }
                    else
                    {
                        line.Append(BannerFont[ch][row]);
                        if (i < wordLetters.Length - 1 && wordLetters[i + 1][0] != ' ')
                            line.Append("  ");
                    }
                }

                RenderBannerLine(line.ToString());
                await Task.Delay(60);
            }

            AnsiConsole.WriteLine();
        }

        await Task.Delay(1200);
    }

    private static void RenderBannerLine(string line)
    {
        var sb = new StringBuilder();
        int blockRun = 0;

        foreach (char c in line)
        {
            if (c == '█')
            {
                // Alternate colors every 2 consecutive blocks for pixel-art depth
                string color = (blockRun / 2) % 2 == 0 ? BannerPrimary : BannerHighlight;
                sb.Append($"[{color}]█[/]");
                blockRun++;
            }
            else
            {
                sb.Append(c);
                blockRun = 0;
            }
        }

        AnsiConsole.MarkupLine(sb.ToString());
    }

    /// <summary>
    /// Renders the header as two side-by-side panels: app info (left) and setup info (right).
    /// </summary>
    public static IRenderable Header(string dotnetUpStatus, string? setupInfo = null)
    {
        string cwd = Directory.GetCurrentDirectory();

        // Left panel: welcome info
        string title = $"[{Red} bold].NET SDK Manager[/]  [{Blue}]{Markup.Escape(TruncatePath(cwd, 40))}[/]";
        string credits = $"[{DarkGray}]Created with[/] [{Red}]\u2764[/] [{DarkGray}]by[/] [{Blue} link=https://shahab-the-guy.dev]Shahab the Guy[/]";

        var leftPanel = new Panel(new Rows(new Markup(title), new Markup(credits)))
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.HeaderBorderColor)
            .Expand();

        // Right panel: setup/dotnetup info
        string setupLine;
        if (setupInfo is not null)
            setupLine = $"[{Green} bold]dotnetup[/] [{White}]{Markup.Escape(setupInfo)}[/]";
        else
            setupLine = $"[{Green}]dotnetup:[/] [{White}]{Markup.Escape(dotnetUpStatus)}[/]";

        var rightPanel = new Panel(new Markup(setupLine))
            .Header($"[{Yellow} bold]Setup[/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor)
            .Expand();

        return new Columns(leftPanel, rightPanel);
    }

    /// <summary>
    /// Renders the footer with view-specific hints and global shortcuts.
    /// </summary>
    public static IRenderable Footer(string hints)
    {
        string global = $"[{DarkGray}]Tab:Switch  F3:Search  F5:Theme  q:Quit[/]";
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
            table.AddColumn(new TableColumn($"[{Yellow} bold]{Markup.Escape(col)}[/]"));
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
}
