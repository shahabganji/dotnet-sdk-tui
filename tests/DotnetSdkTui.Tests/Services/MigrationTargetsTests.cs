using DotnetSdkTui.Models;
using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class MigrationTargetsTests
{
    private static readonly SdkInfo[] Tracked =
    [
        new("SDK", "8.0.100", "/managed/dotnet", "arm64"),
        new("SDK", "8.0.416", "/managed/dotnet", "arm64"),
        new("SDK", "8.0.4150", "/managed/dotnet", "arm64"),
        new("Runtime", "9.0.11", "/managed/dotnet", "arm64"),
        new("SDK", "9.0.308", "/managed/dotnet", "arm64"),
    ];

    private static MigrationItem Sdk(string version, string channel) =>
        new(version, channel, "/home/u/.dotnet/sdk", IsOtherLocation: true);

    [Fact]
    public void PickTargetSdk_SameVersionTracked_PicksIt()
    {
        Assert.Equal("8.0.100", MigrationTargets.PickTargetSdk(Sdk("8.0.100", "8.0"), Tracked)?.Version);
    }

    [Fact]
    public void PickTargetSdk_ChannelMigration_PicksNewestInChannel()
    {
        Assert.Equal("8.0.4150", MigrationTargets.PickTargetSdk(Sdk("8.0.101", "8.0"), Tracked)?.Version);
        Assert.Equal("9.0.308", MigrationTargets.PickTargetSdk(Sdk("9.0.100-preview.6.24328.19", "9.0"), Tracked)?.Version);
    }

    [Fact]
    public void PickTargetSdk_NothingInChannel_ReturnsNull()
    {
        Assert.Null(MigrationTargets.PickTargetSdk(Sdk("7.0.306", "7.0"), Tracked));
    }
}
