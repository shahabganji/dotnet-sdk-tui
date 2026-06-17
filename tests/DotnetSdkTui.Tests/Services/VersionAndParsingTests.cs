using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

// Guards the span-based (allocation-free) rewrites of the version comparator and the
// dotnet CLI output parsers against behaviour regressions.
public class VersionAndParsingTests
{
    [Theory]
    [InlineData("10.0.301", "10.0.100")]   // higher patch wins
    [InlineData("10.0.100", "9.0.305")]    // higher major wins
    [InlineData("9.0.305", "9.0.100")]
    [InlineData("8.0.10", "8.0.2")]        // numeric, not lexical
    public void CompareSdkVersions_OrdersNumerically(string greater, string lesser)
    {
        Assert.True(SdkSearchService.CompareSdkVersions(greater, lesser) > 0);
        Assert.True(SdkSearchService.CompareSdkVersions(lesser, greater) < 0);
    }

    [Fact]
    public void CompareSdkVersions_EqualVersions_ReturnsZero()
    {
        Assert.Equal(0, SdkSearchService.CompareSdkVersions("10.0.301", "10.0.301"));
    }

    [Fact]
    public void CompareSdkVersions_PrereleaseSortsBeforeRelease()
    {
        // A pre-release of the same version is ordered ahead of (less than) the stable release.
        Assert.True(SdkSearchService.CompareSdkVersions("11.0.100-preview.5", "11.0.100") < 0);
    }

    [Fact]
    public void CompareSdkVersions_PrereleaseTags_OrderedOrdinally()
    {
        Assert.True(SdkSearchService.CompareSdkVersions("11.0.100-rc.1", "11.0.100-preview.7") > 0);
    }

    [Fact]
    public void ParseDotnetSdkList_ParsesVersionAndRoot()
    {
        const string output = """
            8.0.408 [/usr/local/share/dotnet/sdk]
            10.0.100 [/Users/x/Library/Application Support/dotnet/sdk]
            """;

        var sdks = DotnetUpService.ParseDotnetSdkList(output);

        Assert.Equal(2, sdks.Count);
        Assert.Equal("SDK", sdks[0].Component);
        Assert.Equal("8.0.408", sdks[0].Version);
        Assert.Equal("/usr/local/share/dotnet/sdk", sdks[0].InstallRoot);
        Assert.Equal("10.0.100", sdks[1].Version);
        Assert.Equal("/Users/x/Library/Application Support/dotnet/sdk", sdks[1].InstallRoot);
    }

    [Fact]
    public void ParseDotnetSdkList_SkipsMalformedAndBlankLines()
    {
        const string output = "garbage line\n\n9.0.305 [/opt/dotnet/sdk]\n";

        var sdks = DotnetUpService.ParseDotnetSdkList(output);

        Assert.Single(sdks);
        Assert.Equal("9.0.305", sdks[0].Version);
    }

    [Fact]
    public void ParseDotnetRuntimeList_MapsComponentNames()
    {
        const string output = """
            Microsoft.NETCore.App 10.0.0 [/usr/local/share/dotnet/shared/Microsoft.NETCore.App]
            Microsoft.AspNetCore.App 8.0.15 [/usr/local/share/dotnet/shared/Microsoft.AspNetCore.App]
            """;

        var runtimes = DotnetUpService.ParseDotnetRuntimeList(output);

        Assert.Equal(2, runtimes.Count);
        Assert.Equal("Runtime", runtimes[0].Component);
        Assert.Equal("10.0.0", runtimes[0].Version);
        Assert.Equal("ASP.NET Core", runtimes[1].Component);
        Assert.Equal("8.0.15", runtimes[1].Version);
    }
}
