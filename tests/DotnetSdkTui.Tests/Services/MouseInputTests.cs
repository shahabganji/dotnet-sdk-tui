using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

// Covers the SGR (1006) mouse-report parser that powers the clickable tab strip.
public class MouseInputTests
{
    [Fact]
    public void ParseSgr_LeftPress_ReturnsCoordinatesAndPress()
    {
        var e = MouseInput.ParseSgr("0;28;6M");

        Assert.NotNull(e);
        Assert.Equal(0, e!.Value.Button);
        Assert.Equal(28, e.Value.Column);
        Assert.Equal(6, e.Value.Row);
        Assert.True(e.Value.IsPress);
        Assert.True(e.Value.IsLeftPress);
    }

    [Fact]
    public void ParseSgr_LeftRelease_IsNotAPress()
    {
        var e = MouseInput.ParseSgr("0;28;6m");

        Assert.NotNull(e);
        Assert.False(e!.Value.IsPress);
        Assert.False(e.Value.IsLeftPress);
    }

    [Theory]
    [InlineData("2;10;6M")]   // right button (button 2)
    [InlineData("1;10;6M")]   // middle button (button 1)
    [InlineData("32;10;6M")]  // motion event (bit 5 set)
    [InlineData("64;10;6M")]  // wheel up
    public void ParseSgr_NonLeftButtons_AreNotLeftPress(string body)
    {
        var e = MouseInput.ParseSgr(body);

        Assert.NotNull(e);
        Assert.False(e!.Value.IsLeftPress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0;28;6")]     // missing final byte
    [InlineData("0;28M")]      // too few fields
    [InlineData("0;28;6;9M")]  // too many fields
    [InlineData("x;28;6M")]    // non-numeric button
    [InlineData("0;y;6M")]     // non-numeric column
    public void ParseSgr_Malformed_ReturnsNull(string body)
    {
        Assert.Null(MouseInput.ParseSgr(body));
    }

    [Fact]
    public void ParseX10_LeftPress_DecodesOffsetCoordinates()
    {
        // Bytes are value + 32: button 0, column 28, row 6.
        var e = MouseInput.ParseX10(new string([(char)(0 + 32), (char)(28 + 32), (char)(6 + 32)]));

        Assert.NotNull(e);
        Assert.Equal(0, e!.Value.Button);
        Assert.Equal(28, e.Value.Column);
        Assert.Equal(6, e.Value.Row);
        Assert.True(e.Value.IsPress);
        Assert.True(e.Value.IsLeftPress);
    }

    [Fact]
    public void ParseX10_Release_IsNotAPress()
    {
        // Button code 3 (low two bits set) signals a release in X10.
        var e = MouseInput.ParseX10(new string([(char)(3 + 32), (char)(28 + 32), (char)(6 + 32)]));

        Assert.NotNull(e);
        Assert.False(e!.Value.IsPress);
        Assert.False(e.Value.IsLeftPress);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]  // fewer than three bytes
    public void ParseX10_TooShort_ReturnsNull(string body)
    {
        Assert.Null(MouseInput.ParseX10(body));
    }
}
