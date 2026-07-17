namespace DotnetSdkTui.Services;

/// <summary>
/// Minimal xterm SGR (1006) mouse-reporting support. We enable button tracking so the terminal
/// emits <c>ESC [ &lt; Cb ; Cx ; Cy M|m</c> on click, then parse those sequences into
/// <see cref="MouseEvent"/> values. Only left-button presses are acted on (see App), which is all
/// the clickable tab strip needs.
/// </summary>
public static class MouseInput
{
    /// <summary>Enable normal button tracking (1000) with SGR extended coordinates (1006).</summary>
    public const string EnableSequence = "\x1b[?1000h\x1b[?1006h";

    /// <summary>Disable SGR extended coordinates (1006) and button tracking (1000).</summary>
    public const string DisableSequence = "\x1b[?1006l\x1b[?1000l";

    /// <summary>A decoded mouse report. Coordinates are 1-based, matching the terminal.</summary>
    public readonly record struct MouseEvent(int Button, int Column, int Row, bool IsPress)
    {
        /// <summary>True for a left-button press: button 0, not a motion (32) or wheel (64) event, final byte 'M'.</summary>
        public bool IsLeftPress => IsPress && (Button & 0b11) == 0 && (Button & 32) == 0 && (Button & 64) == 0;
    }

    /// <summary>
    /// Parses the body of an SGR mouse sequence — the part after the leading <c>ESC [ &lt;</c> —
    /// e.g. <c>"0;28;6M"</c>. Returns <c>null</c> when the body is malformed.
    /// </summary>
    public static MouseEvent? ParseSgr(string body)
    {
        if (string.IsNullOrEmpty(body)) return null;

        char final = body[^1];
        if (final != 'M' && final != 'm') return null;

        string[] parts = body[..^1].Split(';');
        if (parts.Length != 3) return null;

        if (!int.TryParse(parts[0], out int button) ||
            !int.TryParse(parts[1], out int column) ||
            !int.TryParse(parts[2], out int row))
        {
            return null;
        }

        return new MouseEvent(button, column, row, IsPress: final == 'M');
    }

    /// <summary>
    /// Parses a legacy X10/normal (non-SGR) mouse report — the three bytes following <c>ESC [ M</c>.
    /// Each byte is the value offset by 32. Used by terminals that don't support SGR (1006) extended
    /// reporting. Returns <c>null</c> when fewer than three bytes are supplied. In this encoding a
    /// button code of 3 (in the low two bits) is a release; 0/1/2 are left/middle/right presses.
    /// </summary>
    public static MouseEvent? ParseX10(string bytes)
    {
        if (bytes is null || bytes.Length < 3) return null;

        int button = bytes[0] - 32;
        int column = bytes[1] - 32;
        int row = bytes[2] - 32;

        // No distinct release code per-button in X10; (button & 3) == 3 signals a button release.
        bool isPress = (button & 0b11) != 0b11;
        return new MouseEvent(button, column, row, isPress);
    }
}
