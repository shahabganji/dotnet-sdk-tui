using Spectre.Console;
using Spectre.Console.Rendering;

namespace DotnetSdkTui.Theme;

/// <summary>
/// Wraps a renderable with a faux drop-shadow: a dark band along the right and
/// bottom edges, offset one cell down-and-right, so the content appears lifted
/// off the screen. Used for popup dialogs, Norton Commander-style.
/// </summary>
/// <remarks>
/// Spectre.Console has no native shadow primitive, so this composes one at the
/// segment level. It reserves one column on the right and one row at the bottom
/// of its allotted space for the shadow, rendering the inner content into the
/// remaining area so the box plus its shadow still fit the layout cell exactly.
/// <para>
/// Box-drawing border glyphs (┗━┛) sit in the lower-middle of their character
/// cell, so a shadow placed purely in the row below leaves a visible dark sliver
/// under the border. To avoid that, the bottom-border row's own background is
/// tinted with the shadow colour, letting the shadow hug the box edge.
/// </para>
/// </remarks>
internal sealed class DropShadow : IRenderable
{
    private readonly IRenderable _inner;
    private readonly Color _shadowColor;
    private readonly Style _shadow;

    public DropShadow(IRenderable inner, Color shadowColor)
    {
        _inner = inner;
        _shadowColor = shadowColor;
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

        // Reserve the bottom row for the shadow so box + shadow fit the available height.
        var innerOptions = options.Height is { } h && h > 1
            ? options with { Height = h - 1 }
            : options;

        var lines = Segment.SplitLines(_inner.Render(innerOptions, maxWidth - 1));

        // Panels emit a trailing line break, so SplitLines hands back an empty final
        // line. Drop trailing blanks so the shadow sits flush under the bottom border.
        while (lines.Count > 0 && lines[^1].CellCount() == 0)
            lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0)
            return _inner.Render(options, maxWidth);

        // Shadow the box's actual width, so a content-sized dialog isn't shadowed full-width.
        int boxWidth = lines.Max(l => l.CellCount());

        var output = new List<Segment>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            bool isBottomRow = i == lines.Count - 1;

            if (isBottomRow)
                // Tint the bottom-border row so the shadow reaches up to the box edge,
                // closing the sub-cell sliver below the border glyph.
                foreach (var seg in line)
                    output.Add(Tint(seg));
            else
                output.AddRange(line);

            int width = line.CellCount();
            if (width < boxWidth)
            {
                var pad = new string(' ', boxWidth - width);
                output.Add(isBottomRow ? new Segment(pad, _shadow) : new Segment(pad));
            }

            // Shadow on the right edge of every row except the first (offset down).
            output.Add(i == 0 ? new Segment(" ") : new Segment(" ", _shadow));
            output.Add(Segment.LineBreak);
        }

        // Shadow along the bottom edge, offset one column to the right.
        output.Add(new Segment(" "));
        output.Add(new Segment(new string(' ', boxWidth), _shadow));
        output.Add(Segment.LineBreak);

        return output;
    }

    /// <summary>Returns the segment with its background replaced by the shadow colour.</summary>
    private Segment Tint(Segment seg)
    {
        var s = seg.Style ?? Style.Plain;
        return new Segment(seg.Text, new Style(s.Foreground, _shadowColor, s.Decoration, s.Link));
    }
}
