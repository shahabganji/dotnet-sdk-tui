using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

// Covers the channel-picking predicate that decides which .NET channels to open
// during search. The predicate has two prefix checks in opposite directions
// (see MatchesChannel remarks) and this suite pins down each interesting case.
public class SdkSearchServiceMatchesChannelTests
{
    private static ChannelInfo Channel(string version, string latestSdk = "", string latestRuntime = "")
        => new()
        {
            ChannelVersion = version,
            LatestSdk = latestSdk,
            LatestRuntime = latestRuntime,
        };

    // ── Query is a prefix of channel headline data (partial-typing shortcut) ────

    [Fact]
    public void HalfVersion_MatchesChannel_ThroughLatestSdkPrefix()
    {
        // "9.0.31" is a prefix of the channel's latest SDK "9.0.315", so we still open 9.0.
        var channel = Channel("9.0", latestSdk: "9.0.315", latestRuntime: "9.0.20");

        Assert.True(SdkSearchService.MatchesChannel(channel, "9.0.31"));
    }

    [Fact]
    public void ChannelOnlyQuery_MatchesChannel()
    {
        var channel = Channel("9.0", latestSdk: "9.0.315");
        Assert.True(SdkSearchService.MatchesChannel(channel, "9.0"));
    }

    [Fact]
    public void MajorOnlyQuery_MatchesChannel_ThroughChannelVersionPrefix()
    {
        // Channel "9.0" starts with query "9" — legitimate match.
        var channel = Channel("9.0", latestSdk: "9.0.315");
        Assert.True(SdkSearchService.MatchesChannel(channel, "9"));
    }

    [Fact]
    public void QueryWithSharedPrefix_DoesNotMatchThroughBidirectionalClause()
    {
        // Guard on the trailing dot in the bidirectional clause: without it, a query like "90"
        // (referring to a fictional major 90) would spuriously match a channel whose version
        // string is a plain prefix — e.g. a single-digit channel "9" — because "90".StartsWith("9")
        // would be true. With the trailing-dot guard the bidirectional clause requires
        // "90".StartsWith("9.") which is false.
        //
        // The other clauses in the predicate must also reject: ChannelVersion "9" does not start
        // with "90", and there is no LatestSdk / LatestRuntime configured, so the whole predicate
        // returns false as intended.
        var channel = Channel("9");
        Assert.False(SdkSearchService.MatchesChannel(channel, "90"));
    }

    // ── Channel version is a prefix of the query (older-patch case) ────────────

    [Fact]
    public void OlderPatchQuery_MatchesChannel_ThroughBidirectionalPrefix()
    {
        // The regression this bug-fix targets: "9.0.314" is older than latest "9.0.315"
        // so the classic "query is a prefix of latest SDK" check fails, but the query
        // still lives inside the "9.0.*" namespace and must open channel 9.0.
        var channel = Channel("9.0", latestSdk: "9.0.315", latestRuntime: "9.0.20");

        Assert.True(SdkSearchService.MatchesChannel(channel, "9.0.314"));
    }

    [Fact]
    public void OlderPatchQuery_DoesNotMatchDifferentMajor()
    {
        var channel = Channel("8.0", latestSdk: "8.0.422", latestRuntime: "8.0.24");
        Assert.False(SdkSearchService.MatchesChannel(channel, "9.0.314"));
    }

    [Fact]
    public void OlderPatchQuery_DoesNotMatchDifferentMinor()
    {
        var channel = Channel("9.1", latestSdk: "9.1.100");
        Assert.False(SdkSearchService.MatchesChannel(channel, "9.0.314"));
    }

    // ── Basic guards ───────────────────────────────────────────────────────────

    [Fact]
    public void EmptyQuery_ReturnsFalse()
    {
        var channel = Channel("9.0", latestSdk: "9.0.315");
        Assert.False(SdkSearchService.MatchesChannel(channel, string.Empty));
    }

    [Fact]
    public void MatchesLatestRuntimePrefix()
    {
        // A query that happens to prefix the runtime only (not the SDK) still opens the channel.
        var channel = Channel("9.0", latestSdk: "9.0.315", latestRuntime: "9.0.20");
        Assert.True(SdkSearchService.MatchesChannel(channel, "9.0.2"));
    }
}
