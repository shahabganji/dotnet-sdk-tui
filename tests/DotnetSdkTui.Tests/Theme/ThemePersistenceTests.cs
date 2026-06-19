using DotnetSdkTui.Services;
using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Tests.Theming;

// Covers theme persistence (save on cycle) and restore-on-startup, plus the defaults. These touch
// process-global state — the DSM_CONFIG_DIR override and ThemeManager's static selection — so they
// run sequentially within this single class (xUnit does not parallelize tests in the same class).
// Each test gets its own temp config dir via the constructor/Dispose pair.
public class ThemePersistenceTests : IDisposable
{
    private readonly string _dir;

    public ThemePersistenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "dsm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("DSM_CONFIG_DIR", _dir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("DSM_CONFIG_DIR", null);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ── SettingsStore ───────────────────────────────────────────────────

    [Fact]
    public void SettingsStore_RoundTrips_Theme()
    {
        SettingsStore.Save(new UserSettings { Theme = "Mint" });
        Assert.Equal("Mint", SettingsStore.Load().Theme);
    }

    [Fact]
    public void SettingsStore_Load_NoFile_ReturnsDefaults()
    {
        Assert.Null(SettingsStore.Load().Theme);
    }

    [Fact]
    public void SettingsStore_Load_CorruptFile_DoesNotThrow_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.json"), "{ not valid json ]");
        Assert.Null(SettingsStore.Load().Theme);
    }

    // ── ThemeManager restore / defaults ─────────────────────────────────

    [Fact]
    public void Restore_NoSettings_DefaultsToTeal()
    {
        ThemeManager.Restore();

        Assert.Equal("Teal", ThemeManager.ThemeName);
        Assert.Equal(AppTheme.Dark, ThemeManager.Current);
        Assert.Equal("#0E4F47", ThemeManager.SelectedRowBg);
        Assert.Equal("#1DB9A0", ThemeManager.FocusedBorder);
    }

    [Fact]
    public void Restore_LoadsSavedTheme_WithItsColorsAndBase()
    {
        SettingsStore.Save(new UserSettings { Theme = "Lavender" });

        ThemeManager.Restore();

        Assert.Equal("Lavender", ThemeManager.ThemeName);
        Assert.Equal(AppTheme.Light, ThemeManager.Current);     // light-based theme
        Assert.Equal("#DAD2EC", ThemeManager.SelectedRowBg);
        Assert.Equal("#4A2E7A", ThemeManager.SelectedRowText);
        Assert.Equal("#6E57B0", ThemeManager.FocusedBorder);
    }

    [Fact]
    public void Restore_UnknownTheme_FallsBackToTeal()
    {
        SettingsStore.Save(new UserSettings { Theme = "Chartreuse" });

        ThemeManager.Restore();

        Assert.Equal("Teal", ThemeManager.ThemeName);
    }

    // ── ThemeManager cycle / persistence ────────────────────────────────

    [Fact]
    public void Cycle_PersistsSelection_AndIsRestoredNextTime()
    {
        ThemeManager.Restore();                 // Teal
        Assert.Equal("Teal", ThemeManager.ThemeName);

        ThemeManager.Cycle();                   // Teal -> Indigo
        Assert.Equal("Indigo", ThemeManager.ThemeName);
        Assert.Equal("Indigo", SettingsStore.Load().Theme);   // written to disk

        ThemeManager.Restore();                 // simulate next launch
        Assert.Equal("Indigo", ThemeManager.ThemeName);
    }

    [Fact]
    public void Cycle_WrapsThroughAllFourThemes()
    {
        SettingsStore.Save(new UserSettings { Theme = "Teal" });
        ThemeManager.Restore();

        ThemeManager.Cycle();   // Indigo
        ThemeManager.Cycle();   // Mint
        ThemeManager.Cycle();   // Lavender
        Assert.Equal("Lavender", ThemeManager.ThemeName);

        ThemeManager.Cycle();   // wraps back to Teal
        Assert.Equal("Teal", ThemeManager.ThemeName);
    }
}
