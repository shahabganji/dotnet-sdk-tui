using DotnetSdkTui.Theme;

namespace DotnetSdkTui.Tests.Theming;

// Covers the tab-strip hit-region geometry used to map mouse clicks back to a tab.
public class TabHitRegionTests
{
    [Fact]
    public void ComputeTabHitRegions_ProducesContiguousNonOverlappingSpans()
    {
        string[] labels = ["A", "BB", "CCC"];

        var regions = Ui.ComputeTabHitRegions(labels);

        Assert.Equal(3, regions.Count);
        // Each region is non-empty and the strip is contiguous (no gaps between tabs).
        for (int i = 0; i < regions.Count; i++)
            Assert.True(regions[i].EndExclusive > regions[i].Start);
        for (int i = 1; i < regions.Count; i++)
            Assert.Equal(regions[i - 1].EndExclusive, regions[i].Start);
    }

    [Fact]
    public void ComputeTabHitRegions_FirstTabStartsAfterPadderAndBorder()
    {
        // leftPad(2) + border corner(1) + dash(1) + space(1) => first content column is 6 (1-based).
        var regions = Ui.ComputeTabHitRegions(["A", "B", "C"]);

        Assert.Equal(6, regions[0].Start);
    }

    [Fact]
    public void ComputeTabHitRegions_MatchesRenderedMainScreenTabs()
    {
        // The real main-screen labels (assuming emoji glyphs, width 2 for 📦/🔍 and 1 for ⚙).
        string[] labels = ["📦 SDKs", "⚙ Runtimes", "🔍 Search"];

        var regions = Ui.ComputeTabHitRegions(labels);

        Assert.Equal(new Ui.TabHitRegion(6, 15), regions[0]);
        Assert.Equal(new Ui.TabHitRegion(15, 28), regions[1]);
        Assert.Equal(new Ui.TabHitRegion(28, 40), regions[2]);
    }

    [Fact]
    public void ComputeTabHitRegions_WiderLabelsPushLaterTabsRight()
    {
        var narrow = Ui.ComputeTabHitRegions(["A", "B", "C"]);
        var wide = Ui.ComputeTabHitRegions(["AAAAA", "B", "C"]);

        Assert.True(wide[1].Start > narrow[1].Start);
    }
}
