using Spectre.Console;

namespace DotnetSdkTui.Theme;

/// <summary>Defines the available visual themes for the application.</summary>
public enum AppTheme
{
    /// <summary>Dark theme — vivid colors on dark backgrounds.</summary>
    Dark,

    /// <summary>Light theme — muted colors on light backgrounds.</summary>
    Light
}

/// <summary>
/// A complete selectable theme: a light/dark base plus the selection-bar colors that pair with it.
/// Cycled in order with F6.
/// </summary>
/// <param name="Name">Short label shown in the footer.</param>
/// <param name="Base">The light/dark base that drives backgrounds, borders and text.</param>
/// <param name="BarBg">Selection-bar background.</param>
/// <param name="BarText">Selection-bar text color.</param>
/// <param name="Border">Focused-view border color: the bar's hue, lightness-shifted to read on the base.</param>
public readonly record struct ThemeDef(string Name, AppTheme Base, string BarBg, string BarText, string Border);

/// <summary>
/// Manages the active theme and provides theme-adaptive color values.
/// All color properties update automatically when the theme changes.
/// </summary>
public static class ThemeManager
{
    // Two dark-based, two light-based, and one colorblind-accessible theme; F6 cycles through them in order.
    private static readonly ThemeDef[] Themes =
    [
        new("Teal",     AppTheme.Dark,  "#0E4F47", "#C8E64D", "#1DB9A0"),
        new("Indigo",   AppTheme.Dark,  "#312A5E", "#FFD700", "#7A6AD9"),
        new("Mint",     AppTheme.Light, "#CDE8CF", "#14532D", "#2E9D6E"),
        new("Lavender", AppTheme.Light, "#DAD2EC", "#4A2E7A", "#6E57B0"),
        new("Accessible", AppTheme.Dark, "#332288", "#DDCC77", "#8A7CE6"), // Okabe-Ito palette for colorblind accessibility; border is the indigo bar hue lightened to read on the dark base
    ];

    private static int _index;
    private static AppTheme _current = Themes[0].Base;

    /// <summary>Gets the active light/dark base (drives backgrounds, borders and text).</summary>
    public static AppTheme Current => _current;

    /// <summary>
    /// Whether the colorblind-accessible theme is active. When true, every semantic colour below
    /// resolves to the colourblind-safe palette so the UI never relies on a red/green distinction.
    /// </summary>
    public static bool IsAccessible => Themes[_index].Name == "Accessible";

    /// <summary>Short label of the active theme (for footer/status display).</summary>
    public static string ThemeName => Themes[_index].Name;

    // The settings loaded once at startup and reused, so cycling the theme never re-reads the file.
    private static Services.UserSettings _settings = new();

    /// <summary>
    /// Restores the theme saved from a previous session (if any), then applies it. Falls back to the
    /// first theme when nothing is saved or the saved name is unknown. Call once at startup.
    /// </summary>
    public static void Restore()
    {
        _settings = Services.SettingsStore.Load();
        int index = 0;
        if (_settings.Theme is not null)
        {
            int i = Array.FindIndex(Themes, t => t.Name == _settings.Theme);
            if (i >= 0) index = i;
        }
        _index = index;
        _current = Themes[_index].Base;
        ApplyBackground();
    }

    /// <summary>Advances to the next theme, switching both the base palette and the selection bar, and persists the choice.</summary>
    public static void Cycle()
    {
        _index = (_index + 1) % Themes.Length;
        _current = Themes[_index].Base;
        ApplyBackground();
        _settings.Theme = Themes[_index].Name;
        Services.SettingsStore.Save(_settings);
    }

    /// <summary>Sets the terminal default background color via OSC 11.</summary>
    public static void ApplyBackground()
    {
        if (_current == AppTheme.Light)
            Console.Write("\x1b]11;rgb:f0/ec/e3\x07");
        else
            Console.Write("\x1b]11;rgb:1a/1a/2e\x07");
        Console.Out.Flush();
    }

    /// <summary>Resets the terminal background to its original color.</summary>
    public static void ResetBackground()
    {
        Console.Write("\x1b]111\x07");
        Console.Out.Flush();
    }

    // Fixed branding colors (always the same regardless of theme)
    public const string MarioRed = "#E52521";
    public const string MarioGreen = "#43B047";
    public const string MarioBlue = "#049CD8";
    public const string MarioYellow = "#FBD000";
    public const string MarioGold = "#FFD700";
    public const string MarioBrown = "#C84C09";

    // ── Colorblind-safe palette (Okabe-Ito) ─────────────────────────────
    // Used whenever the Accessible theme is active. These hues stay mutually distinguishable
    // under deuteranopia, protanopia and tritanopia, so status, borders and branding never
    // rely on a red/green contrast alone.
    private const string AccGreen  = "#009E73"; // bluish green — success / installed / active
    private const string AccBlue   = "#56B4E9"; // sky blue — info / available
    private const string AccYellow = "#F0E442"; // yellow — section titles / headers
    private const string AccOrange = "#E69F00"; // orange — accent / unmanaged / warnings
    private const string AccRed    = "#D55E00"; // vermillion — errors / brand red (reads distinct from green)
    private const string AccFg     = "#F5F5F5"; // near-white primary text
    private const string AccMuted  = "#B4B4B4"; // secondary text
    private const string AccDim    = "#7C7C7C"; // tertiary text

    // Brand logo/mascot ramp: teal → lime normally; a colourblind-safe blue → yellow ramp
    // when the Accessible theme is active (see Ui banner + mascot).
    public static string BrandRed        => IsAccessible ? AccRed    : MarioRed;
    public static string BrandPrimary    => IsAccessible ? AccBlue   : "#1DB9A0";
    public static string BrandPrimaryDark => IsAccessible ? "#0072B2" : "#148F7B";
    public static string BrandShine      => IsAccessible ? AccYellow : "#C8E64D";

    // ── Theme-adaptive colors ──────────────────────────────────────────
    //
    //   Dark:  bright/vivid on dark terminal backgrounds
    //   Light: deeper/muted so they stay readable on white/light backgrounds

    public static string Foreground    => IsAccessible ? AccFg   : _current == AppTheme.Dark ? "#E0E0E0" : "#1E1E1E";
    public static string Background    => _current == AppTheme.Dark ? "#1A1A2E" : "default";
    public static string Muted         => IsAccessible ? AccMuted : _current == AppTheme.Dark ? "#888888" : "#6B7280";
    public static string DimText       => IsAccessible ? AccDim   : _current == AppTheme.Dark ? "#555555" : "#9CA3AF";
    public static string PanelBorder   => IsAccessible ? AccGreen : _current == AppTheme.Dark ? "#43B047" : "#15803D";
    public static string TableBorder   => IsAccessible ? AccOrange : _current == AppTheme.Dark ? "#C84C09" : "#92400E";
    public static string HeaderBorder  => IsAccessible ? AccBlue  : _current == AppTheme.Dark ? "#E52521" : "#B91C1C";
    public static string SelectedRow   => IsAccessible ? AccYellow : _current == AppTheme.Dark ? "#FBD000" : "#A16207";
    // Selection highlight bar: a colored row background with a contrasting text color replaces the
    // old ">" pointer. The pair comes from the active theme (see Themes).
    public static string SelectedRowText => Themes[_index].BarText;
    public static string SelectedRowBg   => Themes[_index].BarBg;
    public static string InstalledColor => IsAccessible ? AccGreen  : _current == AppTheme.Dark ? "#43B047" : "#15803D";
    public static string AvailableColor => IsAccessible ? AccBlue   : _current == AppTheme.Dark ? "#049CD8" : "#0369A1";
    public static string ErrorColor    => IsAccessible ? AccRed    : _current == AppTheme.Dark ? "#E52521" : "#B91C1C";
    public static string SuccessColor  => IsAccessible ? AccGreen  : _current == AppTheme.Dark ? "#43B047" : "#15803D";
    public static string InfoColor     => IsAccessible ? AccBlue   : _current == AppTheme.Dark ? "#049CD8" : "#0369A1";
    public static string AccentColor   => IsAccessible ? AccOrange : _current == AppTheme.Dark ? "#FFD700" : "#B45309";
    public static string SectionTitle  => IsAccessible ? AccYellow : _current == AppTheme.Dark ? "#FBD000" : "#9A3412";
    public static string InputBg       => _current == AppTheme.Dark ? "#2A2A4E" : "default";
    public static string OutputText    => IsAccessible ? AccMuted : _current == AppTheme.Dark ? "#AAAAAA" : "#4B5563";
    public static string OutputError   => IsAccessible ? AccRed   : _current == AppTheme.Dark ? "#FF6B6B" : "#DC2626";

    public static Color ForegroundColor   => IsAccessible ? ParseHex(AccFg)    : _current == AppTheme.Dark ? ParseHex("#E0E0E0") : ParseHex("#1E1E1E");
    public static Color PanelBorderColor  => IsAccessible ? ParseHex(AccGreen)  : _current == AppTheme.Dark ? ParseHex("#43B047") : ParseHex("#15803D");
    public static Color TableBorderColor  => IsAccessible ? ParseHex(AccOrange) : _current == AppTheme.Dark ? ParseHex("#C84C09") : ParseHex("#92400E");
    public static Color HeaderBorderColor => IsAccessible ? ParseHex(AccBlue)   : _current == AppTheme.Dark ? ParseHex("#E52521") : ParseHex("#B91C1C");
    public static Color SelectedRowColor  => IsAccessible ? ParseHex(AccYellow) : _current == AppTheme.Dark ? ParseHex("#FBD000") : ParseHex("#A16207");

    // ── Focus-adaptive view borders ─────────────────────────────────────
    //
    //   Focused:   the active theme's accent (the selection bar's hue, lightness-shifted to read on
    //              the base) so the active view "pops" and shares one accent with the selected row —
    //              paired with a Norton Commander-style double-line border.
    //   Unfocused: a desaturated slate/grey so inactive views recede into the background — paired with
    //              a thin, dimmed single-line border.
    public static Color FocusedBorderColor   => ParseHex(Themes[_index].Border);
    public static Color UnfocusedBorderColor => _current == AppTheme.Dark ? ParseHex("#4A4A5E") : ParseHex("#C9C2B4");

    /// <summary>The focused-border accent as a markup color string (for the focus indicator, etc.).</summary>
    public static string FocusedBorder => Themes[_index].Border;

    /// <summary>
    /// Classic drop-shadow fill for popup dialogs — a near-black (dark theme) or muted grey
    /// (light theme) offset behind the dialog, like the old Norton Commander pop-ups.
    /// </summary>
    public static Color ShadowColor => _current == AppTheme.Dark ? ParseHex("#08080C") : ParseHex("#BBB3A3");

    /// <summary>
    /// Modal dialog surface fill — a subtle offset from the terminal background so the popup
    /// visually lifts off the base and its drop shadow reads as a real cast shadow.
    /// Dark theme: a couple of steps lighter than the navy base. Light theme: a couple of
    /// steps darker than the cream base.
    /// </summary>
    public static Color ModalBackgroundColor => _current == AppTheme.Dark ? ParseHex("#262640") : ParseHex("#E4DDCB");

    internal static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return new Color(r, g, b);
    }
}
