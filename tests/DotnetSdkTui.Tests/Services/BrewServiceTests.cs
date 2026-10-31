using DotnetSdkTui.Models;
using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

public class BrewServiceTests
{
    [Fact]
    public void ParseInstalled_ReadsNamesVersionsAndPendingUpgrades()
    {
        const string output = "zstd 1.5.7_1\ngh 2.99.0\n";
        var outdated = new Dictionary<string, string> { ["gh"] = "2.103.3" };

        List<BrewPackage> packages = BrewService.ParseInstalled(output, outdated);

        Assert.Equal(["gh", "zstd"], packages.Select(p => p.Name));
        Assert.Equal(("2.99.0", "2.103.3"), (packages[0].InstalledVersion, packages[0].LatestVersion));
        Assert.Equal(("1.5.7_1", "1.5.7_1"), (packages[1].InstalledVersion, packages[1].LatestVersion));
    }

    // A failing `brew list` (e.g. while brew upgrades its portable Ruby) must surface a reason
    // instead of silently rendering an empty list.
    [Fact]
    public void DescribeFailure_ShowsBrewErrorAndRetryHint()
    {
        var result = new ProcessResult(1, "",
            "==> Downloading portable-ruby\nError: Failed to install portable Ruby\n", TimeSpan.Zero);

        string message = BrewService.DescribeFailure("brew list", result);

        Assert.Equal("brew list failed (exit code 1): Error: Failed to install portable Ruby (press r to retry)", message);
    }

    [Fact]
    public void DescribeFailure_WithoutOutput_StillExplainsFailure()
    {
        string message = BrewService.DescribeFailure("brew list", new ProcessResult(-1, "", "", TimeSpan.Zero));

        Assert.Equal("brew list failed (exit code -1) (press r to retry)", message);
    }
}
