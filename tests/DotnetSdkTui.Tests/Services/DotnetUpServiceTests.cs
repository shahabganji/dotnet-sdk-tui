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
            "/home/jane", null, "/unused", isWindows: false, isMacOS: false, includeDotnetUpRoot: true);

        Assert.Equal(Path.Combine("/home/jane", ".dotnetup"), dirs[0]);
        Assert.Equal(Path.Combine("/home/jane", ".local", "share", "dotnetup", "dotnet"), dirs[1]);
    }

    [Fact]
    public void GetPathCandidates_WithActiveRoot_OmitsDotnetUpRoot()
    {
        IReadOnlyList<string> dirs = DotnetUpService.GetPathCandidates(
            "/home/jane", null, "/unused", isWindows: false, isMacOS: false, includeDotnetUpRoot: false);

        Assert.DoesNotContain(Path.Combine("/home/jane", ".local", "share", "dotnetup", "dotnet"), dirs);
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
}
