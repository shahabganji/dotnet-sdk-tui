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
    private readonly Color? _boxFill;
    private readonly Style? _boxFillStyle;

    public DropShadow(IRenderable inner, Color shadowColor, Color? boxFill = null)
    {
        _inner = inner;
        _shadowColor = shadowColor;
        _shadow = new Style(background: shadowColor);
        _boxFill = boxFill;
        _boxFillStyle = boxFill is { } c ? new Style(background: c) : null;
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

        // The tinted bottom-border row IS the bottom shadow — no separate strip below the
        // box — so we don't need to reserve an extra row of height.
        var lines = Segment.SplitLines(_inner.Render(options, maxWidth - 1));

        // Panels emit a trailing line break, so SplitLines hands back an empty final
        // line. Drop trailing blanks so the shadow sits flush under the bottom border.
        while (lines.Count > 0 && lines[^1].CellCount() == 0)
            lines.RemoveAt(lines.Count - 1);
        if (lines.Count == 0)
            return _inner.Render(options, maxWidth);

        // Shadow the box's actual width, so a content-sized dialog isn't shadowed full-width.
        int boxWidth = lines.Max(l => l.CellCount());
        // Bottom shadow starts this many columns in from the left, giving the box a proper
        // down-right "cast shadow" (Norton Commander style). Mirrors the 1-row inset the
        // right-side shadow has at the top so the cast direction reads consistently.
        const int BottomOffset = 2;

        var output = new List<Segment>();
        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            bool isBottomRow = i == lines.Count - 1;

            if (isBottomRow)
            {
                // Tint the bottom-border row from col BottomOffset onward so the shadow
                // hugs the border glyph. The first BottomOffset cells (╚═) are drawn on
                // the TERMINAL background — a proper offset from the modal, mirroring
                // the top-right corner gap where the right shadow doesn't reach.
                int col = 0;
                foreach (var seg in line)
                {
                    int segWidth = seg.CellCount();
                    if (col + segWidth <= BottomOffset)
                        output.Add(seg);                    // terminal background, glyph only
                    else
                        output.Add(Tint(seg));
                    col += segWidth;
                }
            }
            else if (i == 0)
            {
                // Top border row (with the header): fill everything with the modal bg
                // EXCEPT the very last cell (the ╗ corner glyph), which is drawn on the
                // terminal background — matching the bottom-left corner offset and giving
                // the down-right cast direction its "raised" upper-left look.
                int totalCols = line.Sum(s => s.CellCount());
                int col = 0;
                foreach (var seg in line)
                {
                    int segWidth = seg.CellCount();
                    bool rightCorner = col + segWidth >= totalCols;
                    output.Add(rightCorner ? seg : FillBox(seg));
                    col += segWidth;
                }
            }
            else
            {
                foreach (var seg in line)
                    output.Add(FillBox(seg));
            }

            int width = line.CellCount();
            if (width < boxWidth)
            {
                var pad = new string(' ', boxWidth - width);
                if (isBottomRow)
                    output.Add(new Segment(pad, _shadow));
                else
                    output.Add(new Segment(pad, _boxFillStyle ?? Style.Plain));
            }

            // Shadow on the right edge of every row except the first (offset down by 1 row).
            output.Add(i == 0 ? new Segment(" ") : new Segment(" ", _shadow));
            output.Add(Segment.LineBreak);
        }

        // No extra shadow strip below — the tinted bottom-border row (from col
        // BottomOffset onward) is the entire bottom-side shadow. The right-side
        // shadow reaches the bottom-right corner as usual.

        return output;
    }

    /// <summary>Returns the segment with its background replaced by the shadow colour.</summary>
    private Segment Tint(Segment seg)
    {
        var s = seg.Style ?? Style.Plain;
        return new Segment(seg.Text, new Style(s.Foreground, _shadowColor, s.Decoration, s.Link));
    }

    /// <summary>
    /// Paints the box-fill background under the segment when no explicit background is set,
    /// so the whole modal surface reads as a lifted panel instead of the plain terminal.
    /// Preserves segments that already have their own background (highlights, borders).
    /// </summary>
    private Segment FillBox(Segment seg)
    {
        if (_boxFill is null) return seg;
        var s = seg.Style ?? Style.Plain;
        // Don't overpaint segments that already carry an explicit background — that would
        // wash out things like the yellow header title on its own tinted band.
        if (s.Background != Color.Default) return seg;
        return new Segment(seg.Text, new Style(s.Foreground, _boxFill.Value, s.Decoration, s.Link));
    }
}
