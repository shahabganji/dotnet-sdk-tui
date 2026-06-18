using System.Text.Json;
using System.Text.Json.Serialization;

namespace DotnetSdkTui.Services;

/// <summary>Persisted user preferences, stored as JSON under the user's config directory.</summary>
public sealed class UserSettings
{
    /// <summary>Name of the last selected theme (see ThemeManager). Null until the user changes it.</summary>
    public string? Theme { get; set; }
}

/// <summary>Source-generated JSON context so settings (de)serialize under Native AOT.</summary>
[JsonSerializable(typeof(UserSettings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class UserSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Loads and saves <see cref="UserSettings"/> as <c>dsm/settings.json</c> under the platform's
/// per-user application-data folder (<c>~/Library/Application Support</c> on macOS, <c>~/.config</c>
/// on Linux, <c>%APPDATA%</c> on Windows), or under <c>DSM_CONFIG_DIR</c> when that environment
/// variable is set (used by tests and for relocating config). All operations are best-effort: any
/// I/O or parse failure falls back to defaults rather than disrupting the app.
/// </summary>
public static class SettingsStore
{
    private static string ConfigDir =>
        Environment.GetEnvironmentVariable("DSM_CONFIG_DIR") is { Length: > 0 } dir
            ? dir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dsm");

    private static string FilePath => Path.Combine(ConfigDir, "settings.json");

    /// <summary>Reads the saved settings, or returns defaults if none exist or the file is unreadable.</summary>
    public static UserSettings Load()
    {
        try
        {
            string path = FilePath;
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize(json, UserSettingsJsonContext.Default.UserSettings) ?? new UserSettings();
            }
        }
        catch { /* corrupt or unreadable settings — fall back to defaults */ }
        return new UserSettings();
    }

    /// <summary>Writes the settings, creating the config directory if needed. Failures are ignored.</summary>
    public static void Save(UserSettings settings)
    {
        try
        {
            string path = FilePath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string json = JsonSerializer.Serialize(settings, UserSettingsJsonContext.Default.UserSettings);
            File.WriteAllText(path, json);
        }
        catch { /* best-effort persistence — ignore I/O failures */ }
    }
}
