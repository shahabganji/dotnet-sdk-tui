using Spectre.Console;

namespace DotnetSdkTui.Theme;

/// <summary>Defines the available visual themes for the application.</summary>
public enum AppTheme
{
    /// <summary>Dark theme with vivid colors on dark backgrounds.</summary>
    Dark,

    /// <summary>Classic retro theme using ANSI colors — works on any terminal.</summary>
    Classic
}

/// <summary>
/// Manages the active theme and provides theme-adaptive color values.
/// All color properties update automatically when the theme changes.
/// </summary>
public static class ThemeManager
{
    private static AppTheme _current = AppTheme.Dark;

    /// <summary>Gets the currently active theme.</summary>
    public static AppTheme Current => _current;

    /// <summary>Toggles between dark and classic themes.</summary>
    public static void Toggle()
    {
        _current = _current == AppTheme.Dark ? AppTheme.Classic : AppTheme.Dark;
    }

    /// <summary>Sets the active theme explicitly.</summary>
    public static void Set(AppTheme theme) => _current = theme;

    // Primary branding colors (always the same)
    public const string MarioRed = "#E52521";
    public const string MarioGreen = "#43B047";
    public const string MarioBlue = "#049CD8";
    public const string MarioYellow = "#FBD000";
    public const string MarioGold = "#FFD700";
    public const string MarioBrown = "#C84C09";

    // Theme-adaptive colors
    // Classic theme uses ANSI named colors that terminals map to their own palette,
    // making them work correctly on both dark and light terminal backgrounds.
    public static string Foreground => _current == AppTheme.Dark ? "#E0E0E0" : "white";
    public static string Background => _current == AppTheme.Dark ? "#1A1A2E" : "default";
    public static string Muted => _current == AppTheme.Dark ? "#888888" : "grey";
    public static string DimText => _current == AppTheme.Dark ? "#555555" : "grey";
    public static string PanelBorder => _current == AppTheme.Dark ? "#43B047" : "cyan";
    public static string TableBorder => _current == AppTheme.Dark ? "#C84C09" : "yellow";
    public static string HeaderBorder => _current == AppTheme.Dark ? "#E52521" : "red";
    public static string SelectedRow => _current == AppTheme.Dark ? "#FBD000" : "yellow";
    public static string InstalledColor => _current == AppTheme.Dark ? "#43B047" : "green";
    public static string AvailableColor => _current == AppTheme.Dark ? "#049CD8" : "cyan";
    public static string ErrorColor => _current == AppTheme.Dark ? "#E52521" : "red";
    public static string SuccessColor => _current == AppTheme.Dark ? "#43B047" : "green";
    public static string InfoColor => _current == AppTheme.Dark ? "#049CD8" : "cyan";
    public static string AccentColor => _current == AppTheme.Dark ? "#FFD700" : "yellow";
    public static string SectionTitle => _current == AppTheme.Dark ? "#FBD000" : "yellow";
    public static string InputBg => _current == AppTheme.Dark ? "#2A2A4E" : "default";
    public static string OutputText => _current == AppTheme.Dark ? "#AAAAAA" : "grey";
    public static string OutputError => _current == AppTheme.Dark ? "#FF6B6B" : "red";

    public static Color ForegroundColor => _current == AppTheme.Dark ? ParseHex("#E0E0E0") : Color.White;
    public static Color PanelBorderColor => _current == AppTheme.Dark ? ParseHex("#43B047") : Color.Cyan1;
    public static Color TableBorderColor => _current == AppTheme.Dark ? ParseHex("#C84C09") : Color.Yellow;
    public static Color HeaderBorderColor => _current == AppTheme.Dark ? ParseHex("#E52521") : Color.Red;
    public static Color SelectedRowColor => _current == AppTheme.Dark ? ParseHex("#FBD000") : Color.Yellow;

    internal static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return new Color(r, g, b);
    }
}
