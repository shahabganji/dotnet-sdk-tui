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
public readonly record struct ThemeDef(string Name, AppTheme Base, string BarBg, string BarText);

/// <summary>
/// Manages the active theme and provides theme-adaptive color values.
/// All color properties update automatically when the theme changes.
/// </summary>
public static class ThemeManager
{
    // Two dark-based and two light-based themes; F6 cycles through them in order.
    private static readonly ThemeDef[] Themes =
    [
        new("Teal",     AppTheme.Dark,  "#0E4F47", "#C8E64D"),
        new("Indigo",   AppTheme.Dark,  "#312A5E", "#FFD700"),
        new("Mint",     AppTheme.Light, "#CDE8CF", "#14532D"),
        new("Lavender", AppTheme.Light, "#DAD2EC", "#4A2E7A"),
    ];

    private static int _index;
    private static AppTheme _current = Themes[0].Base;

    /// <summary>Gets the active light/dark base (drives backgrounds, borders and text).</summary>
    public static AppTheme Current => _current;

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
        if (_settings.Theme is not null)
        {
            int i = Array.FindIndex(Themes, t => t.Name == _settings.Theme);
            if (i >= 0) _index = i;
        }
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

    // ── Theme-adaptive colors ──────────────────────────────────────────
    //
    //   Dark:  bright/vivid on dark terminal backgrounds
    //   Light: deeper/muted so they stay readable on white/light backgrounds

    public static string Foreground    => _current == AppTheme.Dark ? "#E0E0E0" : "#1E1E1E";
    public static string Background    => _current == AppTheme.Dark ? "#1A1A2E" : "default";
    public static string Muted         => _current == AppTheme.Dark ? "#888888" : "#6B7280";
    public static string DimText       => _current == AppTheme.Dark ? "#555555" : "#9CA3AF";
    public static string PanelBorder   => _current == AppTheme.Dark ? "#43B047" : "#15803D";
    public static string TableBorder   => _current == AppTheme.Dark ? "#C84C09" : "#92400E";
    public static string HeaderBorder  => _current == AppTheme.Dark ? "#E52521" : "#B91C1C";
    public static string SelectedRow   => _current == AppTheme.Dark ? "#FBD000" : "#A16207";
    // Selection highlight bar: a colored row background with a contrasting text color replaces the
    // old ">" pointer. The pair comes from the active theme (see Themes).
    public static string SelectedRowText => Themes[_index].BarText;
    public static string SelectedRowBg   => Themes[_index].BarBg;
    public static string InstalledColor => _current == AppTheme.Dark ? "#43B047" : "#15803D";
    public static string AvailableColor => _current == AppTheme.Dark ? "#049CD8" : "#0369A1";
    public static string ErrorColor    => _current == AppTheme.Dark ? "#E52521" : "#B91C1C";
    public static string SuccessColor  => _current == AppTheme.Dark ? "#43B047" : "#15803D";
    public static string InfoColor     => _current == AppTheme.Dark ? "#049CD8" : "#0369A1";
    public static string AccentColor   => _current == AppTheme.Dark ? "#FFD700" : "#B45309";
    public static string SectionTitle  => _current == AppTheme.Dark ? "#FBD000" : "#9A3412";
    public static string InputBg       => _current == AppTheme.Dark ? "#2A2A4E" : "default";
    public static string OutputText    => _current == AppTheme.Dark ? "#AAAAAA" : "#4B5563";
    public static string OutputError   => _current == AppTheme.Dark ? "#FF6B6B" : "#DC2626";

    public static Color ForegroundColor   => _current == AppTheme.Dark ? ParseHex("#E0E0E0") : ParseHex("#1E1E1E");
    public static Color PanelBorderColor  => _current == AppTheme.Dark ? ParseHex("#43B047") : ParseHex("#15803D");
    public static Color TableBorderColor  => _current == AppTheme.Dark ? ParseHex("#C84C09") : ParseHex("#92400E");
    public static Color HeaderBorderColor => _current == AppTheme.Dark ? ParseHex("#E52521") : ParseHex("#B91C1C");
    public static Color SelectedRowColor  => _current == AppTheme.Dark ? ParseHex("#FBD000") : ParseHex("#A16207");

    // ── Focus-adaptive view borders ─────────────────────────────────────
    //
    //   Focused:   a bright, saturated green so the active view "pops" — paired
    //              with a Norton Commander-style double-line border.
    //   Unfocused: a desaturated slate/grey so inactive views recede into the
    //              background — paired with a thin, dimmed single-line border.
    //
    //   In light mode the focused green is deepened and saturated so it stays
    //   crisp against the pale background (a brighter green washes out there).
    public static Color FocusedBorderColor   => _current == AppTheme.Dark ? ParseHex("#5BE85F") : ParseHex("#0B6E2E");
    public static Color UnfocusedBorderColor => _current == AppTheme.Dark ? ParseHex("#4A4A5E") : ParseHex("#C9C2B4");

    /// <summary>
    /// Classic drop-shadow fill for popup dialogs — a near-black (dark theme) or muted grey
    /// (light theme) offset behind the dialog, like the old Norton Commander pop-ups.
    /// </summary>
    public static Color ShadowColor => _current == AppTheme.Dark ? ParseHex("#08080C") : ParseHex("#BBB3A3");

    internal static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return new Color(r, g, b);
    }
}
