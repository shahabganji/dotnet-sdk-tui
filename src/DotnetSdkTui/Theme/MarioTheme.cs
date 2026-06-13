using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

public static class MarioTheme
{
    // Convenience accessors that delegate to ThemeManager
    public static string Red => ThemeManager.MarioRed;
    public static string Blue => ThemeManager.InfoColor;
    public static string Yellow => ThemeManager.SectionTitle;
    public static string Green => ThemeManager.SuccessColor;
    public static string Gold => ThemeManager.AccentColor;
    public static string Brown => ThemeManager.MarioBrown;
    public static string White => ThemeManager.Foreground;
    public static string Gray => ThemeManager.Muted;
    public static string DarkGray => ThemeManager.DimText;

    private static readonly string[][] CoinFrames =
    [
        [
            "       ╔═══╗       ",
            "       ║ ? ║       ",
            "       ╚═══╝       ",
        ],
        [
            "         ●         ",
            "       ╔═══╗       ",
            "       ║ ? ║       ",
            "       ╚═══╝       ",
        ],
        [
            "        ●●●        ",
            "         ●         ",
            "       ╔═══╗       ",
            "       ║   ║       ",
            "       ╚═══╝       ",
        ],
        [
            "      ●  ★  ●      ",
            "        ●●●        ",
            "       ╔═══╗       ",
            "       ║   ║       ",
            "       ╚═══╝       ",
        ],
        [
            "    ●    ★    ●    ",
            "      ●  ●  ●      ",
            "       ╔═══╗       ",
            "       ║   ║       ",
            "       ╚═══╝       ",
            "                    ",
            "  .NET SDK TUI      ",
        ],
        [
            "  ●      ★      ●  ",
            "                    ",
            "       ╔═══╗       ",
            "       ║   ║       ",
            "       ╚═══╝       ",
            "                    ",
            "   ★ .NET SDK TUI ★ ",
            "    SUPER  EDITION   ",
        ],
    ];

    private static readonly string[] SplashLogo =
    [
        "╔══════════════════════════════════════════╗",
        "║                                          ║",
        "║    ★  .NET SDK TUI  ★  SUPER EDITION     ║",
        "║                                          ║",
        "║       ╔═══╗    🍄                        ║",
        "║       ║   ║    Manage your SDKs!          ║",
        "║       ╚═══╝                               ║",
        "║                                          ║",
        "║          It's-a me, dotnet!               ║",
        "║                                          ║",
        "╚══════════════════════════════════════════╝",
    ];

    public static async Task RenderSplashAsync()
    {
        AnsiConsole.Clear();
        Console.CursorVisible = false;

        foreach (string[] frame in CoinFrames)
        {
            AnsiConsole.Clear();
            AnsiConsole.WriteLine();

            bool useGold = true;
            foreach (string line in frame)
            {
                string color = useGold ? ThemeManager.MarioGold : ThemeManager.MarioYellow;
                AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(line)}[/]");
                useGold = !useGold;
            }

            await Task.Delay(200);
        }

        AnsiConsole.Clear();
        AnsiConsole.WriteLine();

        foreach (string line in SplashLogo)
        {
            AnsiConsole.MarkupLine($"[{ThemeManager.MarioRed}]{Markup.Escape(line)}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{ThemeManager.MarioGold}]  Let's-a go![/]");

        await Task.Delay(1200);
    }

    public static IRenderable Header(string dotnetUpStatus, string cwd, string? project, string themeName)
    {
        var grid = new Grid().AddColumn().AddColumn().AddColumn().AddColumn();

        string title = $"[{Red} bold]★ .NET SDK TUI ★[/]";
        string status = $"[{Green}]dotnetup: {Markup.Escape(dotnetUpStatus)}[/]";
        string dir = $"[{Blue}]{Markup.Escape(TruncatePath(cwd, 35))}[/]";
        string theme = $"[{DarkGray}]{Markup.Escape(themeName)}[/]";

        if (project is not null)
        {
            dir += $" [{Yellow}]● {Markup.Escape(project)}[/]";
        }

        grid.AddRow(title, status, dir, theme);

        return new Panel(grid)
            .Border(BoxBorder.Heavy)
            .BorderColor(ThemeManager.HeaderBorderColor)
            .Padding(0, 0);
    }

    public static IRenderable Footer(string hints)
    {
        string global = $"[{DarkGray}]F1:SDKs  F2:Search  F3:Project  F4:Setup  T:Theme  q:Quit[/]";
        string hintMarkup = $"[{Gold}]{hints}[/]";
        return new Markup($" {hintMarkup}  {global}");
    }

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

    public static Panel ContentPanel(string title, IRenderable content)
    {
        return new Panel(content)
            .Header($"[{Red} bold] ★ {Markup.Escape(title)} ★ [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.PanelBorderColor)
            .Expand();
    }

    public static Panel SectionPanel(string title, IRenderable content)
    {
        return new Panel(content)
            .Header($"[{Yellow} bold] {Markup.Escape(title)} [/]")
            .Border(BoxBorder.Rounded)
            .BorderColor(ThemeManager.TableBorderColor)
            .Expand();
    }

    public static Markup Success(string message) =>
        new($"[{Green}]🍄 {Markup.Escape(message)}[/]");

    public static Markup Error(string message) =>
        new($"[{ThemeManager.ErrorColor}]🔥 {Markup.Escape(message)}[/]");

    public static Markup Info(string message) =>
        new($"[{Blue}]★ {Markup.Escape(message)}[/]");

    public static Markup Coin(string message) =>
        new($"[{Gold}]● {Markup.Escape(message)}[/]");

    public static Markup Muted(string message) =>
        new($"[{Gray}]{Markup.Escape(message)}[/]");

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
