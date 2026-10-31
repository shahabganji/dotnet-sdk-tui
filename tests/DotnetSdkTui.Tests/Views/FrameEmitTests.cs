namespace DotnetSdkTui.Tests.Views;

// The frame is followed by "\e[0J"; any '\r' left at the end would erase the footer row (Windows bug).
public class FrameEmitTests
{
    [Theory]
    [InlineData("row1\r\nfooter\r\n", "row1\r\nfooter")]
    [InlineData("row1\nfooter\n", "row1\nfooter")]
    [InlineData("row1\nfooter", "row1\nfooter")]
    [InlineData("footer\n\n", "footer\n")]
    public void TrimTrailingNewline_DropsExactlyOneLineTerminator(string input, string expected)
    {
        Assert.Equal(expected, App.TrimTrailingNewline(input));
    }
}
