using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class InstallLocationServiceTests
{
    // The active root the user's dotnet CLI reads (note the space — quoting matters).
    private const string ActiveRoot = "/Users/jane/Library/Application Support/dotnet";
    private const string Home = "/Users/jane";

    // --- AugmentInstallArgs: which commands get the flags ---

    [Fact]
    public void AugmentInstallArgs_SdkInstallWithActiveRoot_AppendsInstallPathAndSetDefault()
    {
        string args = InstallLocationService.AugmentInstallArgs("dotnetup", "sdk install 6.0.428", ActiveRoot);

        Assert.Equal($"sdk install 6.0.428 --install-path \"{ActiveRoot}\" --set-default-install", args);
    }

    [Fact]
    public void AugmentInstallArgs_RuntimeInstallWithActiveRoot_AppendsInstallPathAndSetDefault()
    {
        string args = InstallLocationService.AugmentInstallArgs("dotnetup", "runtime install 9.0", ActiveRoot);

        Assert.Equal($"runtime install 9.0 --install-path \"{ActiveRoot}\" --set-default-install", args);
    }

    // Backward compatibility: no previous installation → let dotnetup pick its default path,
    // but still make it the default install so PATH/DOTNET_ROOT get wired up.
    [Fact]
    public void AugmentInstallArgs_SdkInstallWithoutActiveRoot_AppendsOnlySetDefault()
    {
        string args = InstallLocationService.AugmentInstallArgs("dotnetup", "sdk install 10.0", installRoot: null);

        Assert.Equal("sdk install 10.0 --set-default-install", args);
    }

    // The setup flow passes extra flags; they must be preserved.
    [Fact]
    public void AugmentInstallArgs_MigrateFromSystemFlow_IsStillAugmented()
    {
        string args = InstallLocationService.AugmentInstallArgs("dotnetup", "sdk install 9.0 --migrate-from-system", ActiveRoot);

        Assert.Equal($"sdk install 9.0 --migrate-from-system --install-path \"{ActiveRoot}\" --set-default-install", args);
    }

    // Deliberate contract: an explicit --install-path means the caller located the install
    // themselves, so dsm passes the arguments through completely untouched — appending
    // --set-default-install would silently repoint the user's global default.
    [Fact]
    public void AugmentInstallArgs_ExplicitInstallPath_IsLeftUntouched()
    {
        const string explicitArgs = "sdk install 8.0 --install-path \"/custom/root\"";

        Assert.Equal(explicitArgs, InstallLocationService.AugmentInstallArgs("dotnetup", explicitArgs, ActiveRoot));
    }

    [Fact]
    public void AugmentInstallArgs_ExistingSetDefaultInstall_IsNotDuplicated()
    {
        string args = InstallLocationService.AugmentInstallArgs("dotnetup", "sdk install 8.0 --set-default-install", installRoot: null);

        Assert.Equal("sdk install 8.0 --set-default-install", args);
    }

    [Theory]
    [InlineData("sdk uninstall 6.0.428")]
    [InlineData("sdk update")]
    [InlineData("update")]
    [InlineData("list --format Json")]
    public void AugmentInstallArgs_NonInstallCommands_AreLeftUntouched(string arguments)
    {
        Assert.Equal(arguments, InstallLocationService.AugmentInstallArgs("dotnetup", arguments, ActiveRoot));
    }

    [Fact]
    public void AugmentInstallArgs_NonDotnetupCommand_IsLeftUntouched()
    {
        Assert.Equal("install dotnet-sdk", InstallLocationService.AugmentInstallArgs("brew", "install dotnet-sdk", ActiveRoot));
    }

    // --- ResolveActiveInstallRoot: which root wins ---

    // `dotnet --list-sdks` reads from the muxer's own directory, not DOTNET_ROOT, so the
    // muxer found on PATH decides where installs must land to be visible.
    [Fact]
    public void ResolveActiveInstallRoot_MuxerAndDotnetRootBothExist_MuxerWins()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: "/Users/jane/Library/Application Support/dotnetup/dotnet",
            muxerPath: $"{ActiveRoot}/dotnet",
            userHome: Home,
            directoryExists: _ => true);

        Assert.Equal(ActiveRoot, root);
    }

    [Fact]
    public void ResolveActiveInstallRoot_NoMuxerOnPath_FallsBackToDotnetRoot()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: ActiveRoot,
            muxerPath: null,
            userHome: Home,
            directoryExists: dir => dir == ActiveRoot);

        Assert.Equal(ActiveRoot, root);
    }

    [Fact]
    public void ResolveActiveInstallRoot_MuxerDirectoryMissing_FallsBackToDotnetRoot()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: ActiveRoot,
            muxerPath: "/Users/jane/stale/dotnet",
            userHome: Home,
            directoryExists: dir => dir == ActiveRoot);

        Assert.Equal(ActiveRoot, root);
    }

    [Fact]
    public void ResolveActiveInstallRoot_NoEnvNoMuxer_ReturnsNull()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: null,
            muxerPath: null,
            userHome: Home,
            directoryExists: _ => true);

        Assert.Null(root);
    }

    // System roots (official installer) can't take user-level installs — fall back to
    // dotnetup's default rather than producing a permission error.
    [Fact]
    public void ResolveActiveInstallRoot_SystemRoot_ReturnsNull()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: null,
            muxerPath: "/usr/local/share/dotnet/dotnet",
            userHome: Home,
            directoryExists: _ => true);

        Assert.Null(root);
    }

    // The muxer is authoritative even when it disqualifies the result: when the active dotnet
    // is the system one, installing into a user root would be invisible to it, so no
    // --install-path at all — dotnetup's default plus --set-default-install takes over.
    [Fact]
    public void ResolveActiveInstallRoot_MuxerInSystemRoot_DoesNotFallBackToDotnetRoot()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: ActiveRoot,
            muxerPath: "/usr/local/share/dotnet/dotnet",
            userHome: Home,
            directoryExists: _ => true);

        Assert.Null(root);
    }

    [Fact]
    public void ResolveActiveInstallRoot_TrailingSeparator_IsNormalized()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: $"{ActiveRoot}/",
            muxerPath: null,
            userHome: Home,
            directoryExists: _ => true);

        Assert.Equal(ActiveRoot, root);
    }

    // The muxer's parent directory must be computed without rewriting separators to the host
    // platform's, so resolution behaves identically on every OS.
    [Fact]
    public void ResolveActiveInstallRoot_WindowsStyleMuxerPath_ResolvesRoot()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: null,
            muxerPath: @"C:\Users\jane\AppData\Local\dotnet\dotnet.exe",
            userHome: @"C:\Users\jane",
            directoryExists: _ => true);

        Assert.Equal(@"C:\Users\jane\AppData\Local\dotnet", root);
    }

    [Fact]
    public void ResolveActiveInstallRoot_RootEqualToHome_IsAllowed()
    {
        string? root = InstallLocationService.ResolveActiveInstallRoot(
            dotnetRootEnv: "/Users/jane/.dotnet",
            muxerPath: null,
            userHome: Home,
            directoryExists: _ => true);

        Assert.Equal("/Users/jane/.dotnet", root);
    }
}
