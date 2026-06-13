using Spectre.Console;

namespace DotnetSdkTui.Theme;

public enum AppTheme
{
    Dark,
    Light
}

public static class ThemeManager
{
    private static AppTheme _current = AppTheme.Dark;

    public static AppTheme Current => _current;

    public static void Toggle()
    {
        _current = _current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
    }

    public static void Set(AppTheme theme) => _current = theme;

    // Primary branding colors (always the same)
    public const string MarioRed = "#E52521";
    public const string MarioGreen = "#43B047";
    public const string MarioBlue = "#049CD8";
    public const string MarioYellow = "#FBD000";
    public const string MarioGold = "#FFD700";
    public const string MarioBrown = "#C84C09";

    // Theme-adaptive colors
    public static string Foreground => _current == AppTheme.Dark ? "#E0E0E0" : "#1A1A1A";
    public static string Background => _current == AppTheme.Dark ? "#1A1A2E" : "#FAFAFA";
    public static string Muted => _current == AppTheme.Dark ? "#888888" : "#666666";
    public static string DimText => _current == AppTheme.Dark ? "#555555" : "#999999";
    public static string PanelBorder => _current == AppTheme.Dark ? "#43B047" : "#2E8B57";
    public static string TableBorder => _current == AppTheme.Dark ? "#C84C09" : "#A0522D";
    public static string HeaderBorder => _current == AppTheme.Dark ? "#E52521" : "#B22222";
    public static string SelectedRow => _current == AppTheme.Dark ? "#FBD000" : "#DAA520";
    public static string InstalledColor => _current == AppTheme.Dark ? "#43B047" : "#228B22";
    public static string AvailableColor => _current == AppTheme.Dark ? "#049CD8" : "#4169E1";
    public static string ErrorColor => _current == AppTheme.Dark ? "#E52521" : "#DC143C";
    public static string SuccessColor => _current == AppTheme.Dark ? "#43B047" : "#228B22";
    public static string InfoColor => _current == AppTheme.Dark ? "#049CD8" : "#4169E1";
    public static string AccentColor => _current == AppTheme.Dark ? "#FFD700" : "#DAA520";
    public static string SectionTitle => _current == AppTheme.Dark ? "#FBD000" : "#B8860B";
    public static string InputBg => _current == AppTheme.Dark ? "#2A2A4E" : "#FFFFFF";
    public static string OutputText => _current == AppTheme.Dark ? "#AAAAAA" : "#333333";
    public static string OutputError => _current == AppTheme.Dark ? "#FF6B6B" : "#DC143C";

    public static Color ForegroundColor => ParseHex(Foreground);
    public static Color PanelBorderColor => ParseHex(PanelBorder);
    public static Color TableBorderColor => ParseHex(TableBorder);
    public static Color HeaderBorderColor => ParseHex(HeaderBorder);
    public static Color SelectedRowColor => ParseHex(SelectedRow);

    private static Color ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        byte r = Convert.ToByte(hex[..2], 16);
        byte g = Convert.ToByte(hex[2..4], 16);
        byte b = Convert.ToByte(hex[4..6], 16);
        return new Color(r, g, b);
    }
}
