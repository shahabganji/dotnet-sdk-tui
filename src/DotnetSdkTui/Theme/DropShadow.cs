using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

/// <summary>
/// Wraps a renderable with a faux drop-shadow: a dark band along the right and
/// bottom edges, offset one cell down-and-right, so the content appears lifted
/// off the screen. Used to make the focused view "pop".
/// </summary>
/// <remarks>
/// Spectre.Console has no native shadow primitive, so this composes one at the
/// segment level. It reserves one column on the right and one row at the bottom
/// of its allotted space for the shadow, rendering the inner content into the
/// remaining area so the box plus its shadow still fit the layout cell exactly.
/// </remarks>
internal sealed class DropShadow : IRenderable
{
    private readonly IRenderable _inner;
    private readonly Style _shadow;

    public DropShadow(IRenderable inner, Color shadowColor)
    {
        _inner = inner;
        _shadow = new Style(background: shadowColor);
    }

    public Measurement Measure(RenderOptions options, int maxWidth)
    {
        var inner = _inner.Measure(options, Math.Max(1, maxWidth - 1));
        return new Measurement(Math.Min(inner.Min + 1, maxWidth), Math.Min(inner.Max + 1, maxWidth));
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth)
    {
        if (maxWidth <= 1)
            return _inner.Render(options, maxWidth);

        int innerWidth = maxWidth - 1;

        // Reserve the bottom row for the shadow so box + shadow fit the cell height.
        var innerOptions = options.Height is { } h && h > 1
            ? options with { Height = h - 1 }
            : options;

        var lines = Segment.SplitLines(_inner.Render(innerOptions, innerWidth));

        // Panels emit a trailing line break, so SplitLines hands back an empty final
        // line. Drop trailing blanks so the shadow sits flush under the bottom border
        // instead of leaving a gap row between the box and its shadow.
        while (lines.Count > 0 && lines[^1].CellCount() == 0)
            lines.RemoveAt(lines.Count - 1);

        var output = new List<Segment>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            int width = line.CellCount();
            output.AddRange(line);
            if (width < innerWidth)
                output.Add(new Segment(new string(' ', innerWidth - width)));

            // Shadow on the right edge of every row except the first (offset down).
            output.Add(i == 0 ? new Segment(" ") : new Segment(" ", _shadow));
            output.Add(Segment.LineBreak);
        }

        // Shadow along the bottom edge, offset one column to the right.
        output.Add(new Segment(" "));
        output.Add(new Segment(new string(' ', innerWidth), _shadow));
        output.Add(Segment.LineBreak);

        return output;
    }
}
