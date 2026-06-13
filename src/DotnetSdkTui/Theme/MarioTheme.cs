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

    private static readonly string[] SplashBanner =
    [
        @"    ___  _  _ ___ _____   ___ ___  _  __  __  __    _   _  _   _   ___ ___ ___  ",
        @"   |   \| \| | __|_   _| / __|   \| |/ / |  \/  |  /_\ | \| | /_\ / __| __| _ \ ",
        @"  _| |) | .` | _|  | |   \__ \ |) | ' <  | |\/| | / _ \| .` |/ _ \ (_ | _||   / ",
        @" (_)___/|_|\_|___| |_|   |___/___/|_|\_\ |_|  |_|/_/ \_\_|\_/_/ \_\___|___|_|_\ ",
        @"                                                                                  ",
    ];

    /// <summary>
    /// Renders the startup splash animation with an Aspire-style ASCII banner.
    /// </summary>
    public static async Task RenderSplashAsync()
    {
        AnsiConsole.Clear();
        Console.CursorVisible = false;

        // Animate banner line by line
        AnsiConsole.WriteLine();
        foreach (string line in SplashBanner)
        {
            AnsiConsole.MarkupLine($"[{ThemeManager.MarioRed} bold]{Markup.Escape(line)}[/]");
            await Task.Delay(80);
        }

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{ThemeManager.MarioGold} bold]  ★ .NET SDK Manager — Super Edition ★[/]");
        AnsiConsole.MarkupLine($"[{ThemeManager.MarioGreen}]  Manage your .NET SDKs with style![/]");
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine($"[{ThemeManager.MarioBlue}]  It's-a me, dotnet![/]");

        await Task.Delay(1500);
    }

    /// <summary>
    /// Renders the header bar with title, dotnetup status, working directory, and theme indicator.
    /// </summary>
    public static IRenderable Header(string dotnetUpStatus, string cwd, string? project, string themeName)
    {
        var grid = new Grid().AddColumn().AddColumn().AddColumn().AddColumn();

        string title = $"[{Red} bold]★ .NET SDK Manager ★[/]";
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

    /// <summary>
    /// Renders the footer with view-specific hints and global shortcuts.
    /// </summary>
    public static IRenderable Footer(string hints)
    {
        string global = $"[{DarkGray}]F1:SDKs  F2:Search  F3:Project  F4:Setup  F5:Theme  q:Quit[/]";
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
