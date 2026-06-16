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
}
