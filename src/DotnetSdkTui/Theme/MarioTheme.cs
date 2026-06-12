using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

public static class MarioTheme
{
    // Super Mario color palette
    public const string Red = "#E52521";
    public const string Blue = "#049CD8";
    public const string Yellow = "#FBD000";
    public const string Green = "#43B047";
    public const string Gold = "#FFD700";
    public const string Brown = "#C84C09";
    public const string White = "#FFFFFF";
    public const string Gray = "#888888";
    public const string DarkGray = "#555555";
    public const string BrickRed = "#B44420";

    public static Style ActiveTabStyle => new(Color.Black, new Color(251, 208, 0));
    public static Style InactiveTabStyle => new(new Color(136, 136, 136));
    public static Style HeaderStyle => new(new Color(229, 37, 33));
    public static Style SuccessStyle => new(new Color(67, 176, 71));
    public static Style ErrorStyle => new(new Color(229, 37, 33));
    public static Style HintStyle => new(new Color(136, 136, 136));
    public static Style SelectedRowStyle => new(new Color(251, 208, 0), decoration: Decoration.Bold);
    public static Style InstalledStyle => new(new Color(67, 176, 71));
    public static Style AvailableStyle => new(new Color(4, 156, 216));

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
                string color = useGold ? Gold : Yellow;
                AnsiConsole.MarkupLine($"[{color}]{Markup.Escape(line)}[/]");
                useGold = !useGold;
            }

            await Task.Delay(200);
        }

        // Final splash
        AnsiConsole.Clear();
        AnsiConsole.WriteLine();

        foreach (string line in SplashLogo)
        {
            AnsiConsole.MarkupLine($"[{Red}]{Markup.Escape(line)}[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{Gold}]  Let's-a go![/]");

        await Task.Delay(1200);
    }

    public static IRenderable Header(string dotnetUpStatus, string cwd, string? project)
    {
        var grid = new Grid().AddColumn().AddColumn().AddColumn();

        string title = $"[{Red} bold]★ .NET SDK TUI ★[/]";
        string status = $"[{Green}]dotnetup: {Markup.Escape(dotnetUpStatus)}[/]";
        string dir = $"[{Blue}]{Markup.Escape(TruncatePath(cwd, 40))}[/]";

        if (project is not null)
        {
            dir += $" [{Yellow}]● {Markup.Escape(project)}[/]";
        }

        grid.AddRow(title, status, dir);

        return new Panel(grid)
            .Border(BoxBorder.Heavy)
            .BorderColor(new Color(229, 37, 33))
            .Padding(0, 0);
    }

    public static IRenderable TabBar(IReadOnlyList<(string Name, string Icon)> tabs, int activeIndex)
    {
        var cols = new Columns();
        var items = new List<IRenderable>();

        for (int i = 0; i < tabs.Count; i++)
        {
            string label = $" {i + 1}:{tabs[i].Icon}{tabs[i].Name} ";
            if (i == activeIndex)
            {
                items.Add(new Markup($"[black on {Yellow}]{Markup.Escape(label)}[/]"));
            }
            else
            {
                items.Add(new Markup($"[{Gray}]{Markup.Escape(label)}[/]"));
            }
        }

        return new Columns(items).Padding(1, 0);
    }

    public static IRenderable Footer(string viewHints)
    {
        string global = $"[{DarkGray}]1-4:Tab  Tab:Next  q:Quit[/]";
        string hints = $"[{Gold}]{viewHints}[/]";
        return new Markup($" {hints}  {global}");
    }

    public static Table StyledTable(params string[] columns)
    {
        var table = new Table()
            .Border(TableBorder.Rounded)
            .BorderColor(new Color(200, 76, 9));

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
            .BorderColor(new Color(67, 176, 71))
            .Expand();
    }

    public static Markup Success(string message) =>
        new($"[{Green}]🍄 {Markup.Escape(message)}[/]");

    public static Markup Error(string message) =>
        new($"[{Red}]🔥 {Markup.Escape(message)}[/]");

    public static Markup Info(string message) =>
        new($"[{Blue}]★ {Markup.Escape(message)}[/]");

    public static Markup Coin(string message) =>
        new($"[{Gold}]● {Markup.Escape(message)}[/]");

    public static Markup Muted(string message) =>
        new($"[{Gray}]{Markup.Escape(message)}[/]");

    private static string TruncatePath(string path, int max)
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
