using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class DotnetUpServiceTests
{
    // dotnetup tracks the base root; `dotnet --list-sdks` reports the leaf (e.g. ".../dotnet/sdk").
    private const string ManagedRoot = "/Users/jane/Library/Application Support/dotnet";

    [Fact]
    public void IsManagedInstallRoot_SdkLeafUnderManagedRoot_IsManaged()
    {
        bool managed = DotnetUpService.IsManagedInstallRoot(
            $"{ManagedRoot}/sdk",
            [ManagedRoot]);

        Assert.True(managed);
    }

    [Fact]
    public void IsManagedInstallRoot_RuntimeLeafUnderManagedRoot_IsManaged()
    {
        bool managed = DotnetUpService.IsManagedInstallRoot(
            $"{ManagedRoot}/shared/Microsoft.NETCore.App",
            [ManagedRoot]);

        Assert.True(managed);
    }

    [Fact]
    public void IsManagedInstallRoot_PathEqualToRoot_IsManaged()
    {
        Assert.True(DotnetUpService.IsManagedInstallRoot(ManagedRoot, [ManagedRoot]));
    }

    [Fact]
    public void IsManagedInstallRoot_TrailingSeparatorsIgnored()
    {
        bool managed = DotnetUpService.IsManagedInstallRoot(
            $"{ManagedRoot}/sdk/",
            [$"{ManagedRoot}/"]);

        Assert.True(managed);
    }

    // The reported bug: SDKs installed by the official installer live under /usr/local/share/dotnet,
    // which is outside any dotnetup-managed root, so dotnetup must not be asked to uninstall them.
    [Fact]
    public void IsManagedInstallRoot_OfficialInstallerPath_IsExternal()
    {
        bool managed = DotnetUpService.IsManagedInstallRoot(
            "/usr/local/share/dotnet/sdk",
            [ManagedRoot]);

        Assert.False(managed);
    }

    // The friend's exact case: dotnetup is installed but tracks nothing yet, so every
    // pre-existing SDK is external and none can be managed.
    [Fact]
    public void IsManagedInstallRoot_NoTrackedInstallations_IsExternal()
    {
        bool managed = DotnetUpService.IsManagedInstallRoot(
            "/usr/local/share/dotnet/sdk",
            []);

        Assert.False(managed);
    }

    [Fact]
    public void IsManagedInstallRoot_SiblingDirectoryNotTreatedAsNested()
    {
        // "/usr/local/share/dotnet-preview" must not match a managed root of "/usr/local/share/dotnet".
        bool managed = DotnetUpService.IsManagedInstallRoot(
            "/usr/local/share/dotnet-preview/sdk",
            ["/usr/local/share/dotnet"]);

        Assert.False(managed);
    }

    [Fact]
    public void IsManagedInstallRoot_EmptyInstallRoot_IsExternal()
    {
        Assert.False(DotnetUpService.IsManagedInstallRoot("", [ManagedRoot]));
    }

    [Fact]
    public void IsManagedInstallRoot_WindowsNestedPath_IsManaged()
    {
        bool managed = DotnetUpService.IsManagedInstallRoot(
            @"C:\Users\jane\AppData\Local\dotnet\sdk",
            [@"C:\Users\jane\AppData\Local\dotnet"]);

        Assert.True(managed);
    }

    [Theory]
    [InlineData(false, null, "/home/jane/.local/share/dotnetup/dotnet")]
    [InlineData(false, "/data", "/data/dotnetup/dotnet")]
    [InlineData(true, "/data", "/home/jane/Library/Application Support/dotnetup/dotnet")]
    public void GetDotnetUpDefaultInstallRoot_Unix_MatchesDotnetUpDefaults(
        bool isMacOS, string? xdgDataHome, string expected)
    {
        string root = DotnetUpService.GetDotnetUpDefaultInstallRoot(
            "/home/jane", xdgDataHome, "/unused", isWindows: false, isMacOS);

        Assert.Equal(expected.Replace('/', Path.DirectorySeparatorChar), root.Replace('/', Path.DirectorySeparatorChar));
    }

    [Fact]
    public void GetDotnetUpDefaultInstallRoot_Windows_UsesLocalAppData()
    {
        string localAppData = Path.Combine("C:", "Users", "jane", "AppData", "Local");

        string root = DotnetUpService.GetDotnetUpDefaultInstallRoot(
            "/unused", null, localAppData, isWindows: true, isMacOS: false);

        Assert.Equal(Path.Combine(localAppData, "dotnetup", "dotnet"), root);
    }

    [Fact]
    public void GetPathCandidates_WithoutActiveRoot_PutsDotnetUpRootRightAfterDotnetUpTool()
    {
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/home/jane", null, "/unused", isWindows: false, isMacOS: false, includeDotnetUpRoot: true,
            dotnetOnPath: false, dotnetRootEnv: null, fileExists: _ => false);

        Assert.Equal(Path.Combine("/home/jane", ".dotnetup"), dirs[0]);
        Assert.Equal(Path.Combine("/home/jane", ".local", "share", "dotnetup", "dotnet"), dirs[1]);
    }

    [Fact]
    public void GetPathCandidates_WithActiveRoot_OmitsDotnetUpRoot()
    {
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/home/jane", null, "/unused", isWindows: false, isMacOS: false, includeDotnetUpRoot: false,
            dotnetOnPath: true, dotnetRootEnv: null, fileExists: _ => true);

        Assert.DoesNotContain(Path.Combine("/home/jane", ".local", "share", "dotnetup", "dotnet"), dirs);
    }

    [Fact]
    public void GetPathCandidates_UserDotnetAlreadyOnPath_NeverAddsStaleLegacyRoot()
    {
        // Regression: a leftover ~/.dotnet (dotnet-install script) was prepended ahead of the user's
        // real dotnet, so dsm listed its SDKs as unmanaged and pinned migrations to it.
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/Users/jane", null, "/unused", isWindows: false, isMacOS: true, includeDotnetUpRoot: false,
            dotnetOnPath: true, dotnetRootEnv: "/Users/jane/Library/Application Support/dotnet",
            fileExists: _ => true);

        Assert.Equal([Path.Combine("/Users/jane", ".dotnetup")], dirs);
    }

    [Fact]
    public void GetPathCandidates_SystemDotnetOnPath_NeverAddsStaleLegacyRoot()
    {
        // A system-wide dotnet (/usr/local/share/dotnet) has no user-level active root, but a leftover
        // ~/.dotnet must still not be prepended ahead of it.
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/Users/jane", null, "/unused", isWindows: false, isMacOS: true, includeDotnetUpRoot: true,
            dotnetOnPath: true, dotnetRootEnv: null, fileExists: _ => true);

        Assert.Equal(
            [Path.Combine("/Users/jane", ".dotnetup"),
             Path.Combine("/Users/jane", "Library", "Application Support", "dotnetup", "dotnet")],
            dirs);
    }

    [Fact]
    public void GetPathCandidates_NoDotnetOnPath_PrefersDotnetRootWithMuxer()
    {
        string dotnetRoot = "/opt/custom/dotnet";
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/Users/jane", null, "/unused", isWindows: false, isMacOS: true, includeDotnetUpRoot: false,
            dotnetOnPath: false, dotnetRootEnv: dotnetRoot + "/", fileExists: _ => true);

        Assert.Equal([Path.Combine("/Users/jane", ".dotnetup"), dotnetRoot], dirs);
    }

    [Fact]
    public void GetPathCandidates_NoDotnetOnPath_AddsOnlyFirstLegacyRootWithMuxer()
    {
        string appSupport = Path.Combine("/Users/jane", "Library", "Application Support", "dotnet");
        string dotDotnet = Path.Combine("/Users/jane", ".dotnet");
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/Users/jane", null, "/unused", isWindows: false, isMacOS: true, includeDotnetUpRoot: false,
            dotnetOnPath: false, dotnetRootEnv: "/missing/dotnet",
            fileExists: f => f == Path.Combine(dotDotnet, "dotnet"));

        Assert.Equal([Path.Combine("/Users/jane", ".dotnetup"), dotDotnet], dirs);
        Assert.DoesNotContain(appSupport, dirs);
    }

    [Fact]
    public void GetPathCandidates_Windows_HasNoLegacyRoots()
    {
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            @"C:\Users\jane", null, @"C:\Users\jane\AppData\Local", isWindows: true, isMacOS: false,
            includeDotnetUpRoot: false, dotnetOnPath: false, dotnetRootEnv: null, fileExists: _ => true);

        Assert.Equal([Path.Combine(@"C:\Users\jane", ".dotnetup")], dirs);
    }

    // --- GetOtherRootCandidates: dotnet folders the active dotnet doesn't read ---

    private static readonly string MacHome = "/Users/jane";
    private static readonly string AppSupportRoot = Path.Combine(MacHome, "Library", "Application Support", "dotnet");
    private static readonly string DotDotnetRoot = Path.Combine(MacHome, ".dotnet");

    [Fact]
    public void GetOtherRootCandidates_StaleDotDotnet_IsListedButActiveRootIsNot()
    {
        IReadOnlyList<string> roots = DotnetUpService.GetOtherRootCandidates(
            MacHome, null, "/unused", isWindows: false, isMacOS: true,
            dotnetRootEnv: AppSupportRoot, rememberedRoot: null, activeMuxerDir: AppSupportRoot + "/",
            trackedRoots: [AppSupportRoot],
            fileExists: f => f == Path.Combine(AppSupportRoot, "dotnet") || f == Path.Combine(DotDotnetRoot, "dotnet"));

        Assert.Equal([DotDotnetRoot], roots);
    }

    [Fact]
    public void GetOtherRootCandidates_ExcludesTrackedRootsAndFoldersWithoutMuxer()
    {
        string dotnetUpRoot = Path.Combine(MacHome, "Library", "Application Support", "dotnetup", "dotnet");
        IReadOnlyList<string> roots = DotnetUpService.GetOtherRootCandidates(
            MacHome, null, "/unused", isWindows: false, isMacOS: true,
            dotnetRootEnv: null, rememberedRoot: null, activeMuxerDir: "/usr/local/share/dotnet",
            trackedRoots: [dotnetUpRoot],
            fileExists: f => f != Path.Combine(DotDotnetRoot, "dotnet"));

        Assert.Equal([AppSupportRoot], roots);
    }

    [Fact]
    public void GetOtherRootCandidates_DeduplicatesDotnetRootAndLegacyRoot()
    {
        IReadOnlyList<string> roots = DotnetUpService.GetOtherRootCandidates(
            MacHome, null, "/unused", isWindows: false, isMacOS: true,
            dotnetRootEnv: DotDotnetRoot + "/", rememberedRoot: DotDotnetRoot, activeMuxerDir: AppSupportRoot,
            trackedRoots: [], fileExists: f => !f.Contains("dotnetup"));

        Assert.Equal([DotDotnetRoot], roots);
    }

    [Fact]
    public void RemovePathEntry_RemovesExactEntryOnly()
    {
        string updated = DotnetUpService.RemovePathEntry("/a/dotnet:/usr/bin:/a/dotnet/:/a/dotnet-old", "/a/dotnet", ':');

        Assert.Equal("/usr/bin:/a/dotnet-old", updated);
    }

    [Fact]
    public void PrependMissingDirs_AddsOnlyExistingDirsNotAlreadyOnPath()
    {
        string path = "/usr/bin:/home/jane/.dotnetup/";

        string updated = DotnetUpService.PrependMissingDirs(
            path,
            ["/home/jane/.dotnetup", "/home/jane/.local/share/dotnetup/dotnet", "/missing", "/home/jane/.dotnet"],
            d => d != "/missing",
            ':');

        Assert.Equal("/home/jane/.local/share/dotnetup/dotnet:/home/jane/.dotnet:/usr/bin:/home/jane/.dotnetup/", updated);
    }

    [Fact]
    public void PrependMissingDirs_SiblingWithSharedPrefix_IsNotTreatedAsPresent()
    {
        string updated = DotnetUpService.PrependMissingDirs(
            "/home/jane/.dotnetup", ["/home/jane/.dotnet"], _ => true, ':');

        Assert.Equal("/home/jane/.dotnet:/home/jane/.dotnetup", updated);
    }

    [Fact]
    public void PrependMissingDirs_WindowsPath_ComparesCaseInsensitively()
    {
        string path = @"C:\Users\Jane\AppData\Local\DotnetUp\Dotnet\;C:\Windows";

        string updated = DotnetUpService.PrependMissingDirs(
            path, [@"C:\Users\jane\AppData\Local\dotnetup\dotnet"], _ => true, ';');

        Assert.Same(path, updated);
    }

    [Fact]
    public void BootstrapCommand_OnWindows_AvoidsInteractiveWebRequestPrompt()
    {
        var (_, args) = DotnetUpService.BootstrapCommand();

        // Windows PowerShell prompts ("Script Execution Risk") on iwr without -UseBasicParsing,
        // which hangs the Setup panel's install.
        if (OperatingSystem.IsWindows())
            Assert.Contains("iwr -UseBasicParsing", args);
        else
            Assert.Contains("curl -fsSL", args);
    }
}
