using DotnetSdkTui.Services;

namespace DotnetSdkTui.Tests.Services;

// Covers key/mouse decoding, in particular that keys typed right after Esc aren't swallowed.
public class InputReaderTests
{
    private static InputReader Reader(params ConsoleKeyInfo[] keys)
    {
        var queue = new Queue<ConsoleKeyInfo>(keys);
        return new InputReader(() => queue.Count > 0, queue.Dequeue);
    }

    private static ConsoleKeyInfo Key(char c, ConsoleKey k) => new(c, k, false, false, false);
    private static ConsoleKeyInfo Ch(char c) => Key(c, ConsoleKey.NoName);
    private static readonly ConsoleKeyInfo Esc = Key('\x1b', ConsoleKey.Escape);
    private static readonly ConsoleKeyInfo Tab = Key('\t', ConsoleKey.Tab);

    [Fact]
    public void EscapeThenTabs_YieldsEscapeAndEveryTab()
    {
        var reader = Reader(Esc, Tab, Tab);

        var keys = new List<ConsoleKey>();
        while (reader.TryRead(out var key, out var mouse))
        {
            Assert.Null(mouse);
            keys.Add(key.Key);
        }

        Assert.Equal([ConsoleKey.Escape, ConsoleKey.Tab, ConsoleKey.Tab], keys);
    }

    [Fact]
    public void SgrMouseSequence_DecodesToMouseEvent()
    {
        var reader = Reader([Esc, .. "[<0;28;6M".Select(Ch)]);

        Assert.True(reader.TryRead(out _, out var mouse));
        Assert.Equal(new MouseInput.MouseEvent(0, 28, 6, true), mouse);
        Assert.False(reader.KeyAvailable);
    }

    [Fact]
    public void LoneEscape_ReturnsEscape()
    {
        var reader = Reader(Esc);

        Assert.True(reader.TryRead(out var key, out var mouse));
        Assert.Equal(ConsoleKey.Escape, key.Key);
        Assert.Null(mouse);
        Assert.False(reader.TryRead(out _, out _));
    }
}
