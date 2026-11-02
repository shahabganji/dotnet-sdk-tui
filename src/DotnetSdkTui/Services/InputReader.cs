namespace DotnetSdkTui.Services;

/// <summary>
/// Reads keys and decodes mouse reports. Most keys pass straight through, but a mouse report — either SGR
/// (<c>ESC [ &lt; Cb ; Cx ; Cy M|m</c>) or legacy X10 (<c>ESC [ M b x y</c>) — is decoded into a
/// <see cref="MouseInput.MouseEvent"/>. A lone Escape or an unrecognised escape sequence is returned as the
/// Escape key. A key that merely follows an Escape (e.g. Esc then Tab typed quickly) is kept for the next read.
/// </summary>
public sealed class InputReader(Func<bool> keyAvailable, Func<ConsoleKeyInfo> readKey)
{
    private const int ContinuationWaitMs = 40;
    private readonly Queue<ConsoleKeyInfo> _pending = new();

    /// <summary>Reads from the real console.</summary>
    public static InputReader Console { get; } =
        new(() => System.Console.KeyAvailable, () => System.Console.ReadKey(true));

    public bool KeyAvailable => _pending.Count > 0 || keyAvailable();

    /// <summary>Reads the next input. Returns <c>false</c> only when no input was pending.</summary>
    public bool TryRead(out ConsoleKeyInfo key, out MouseInput.MouseEvent? mouse)
    {
        key = default;
        mouse = null;

        if (!KeyAvailable) return false;

        ConsoleKeyInfo first = Next();
        key = first;
        if (first.Key != ConsoleKey.Escape) return true;

        // Possible mouse sequence. The continuation bytes usually arrive in the same terminal write
        // as the ESC, but we wait briefly for each so a split read doesn't misfire a lone Escape.
        if (!TryReadWithin(out ConsoleKeyInfo k1)) return true;
        if (k1.KeyChar != '[')
        {
            _pending.Enqueue(k1);
            return true;
        }

        if (!TryReadWithin(out ConsoleKeyInfo k2)) return true;

        if (k2.KeyChar == '<')
        {
            // SGR (1006) extended encoding.
            var body = new System.Text.StringBuilder();
            while (TryReadWithin(out ConsoleKeyInfo k))
            {
                body.Append(k.KeyChar);
                if (k.KeyChar is 'M' or 'm') break;
            }
            mouse = MouseInput.ParseSgr(body.ToString());
        }
        else if (k2.KeyChar == 'M')
        {
            // Legacy X10 encoding: exactly three bytes follow.
            var body = new System.Text.StringBuilder();
            for (int i = 0; i < 3 && TryReadWithin(out ConsoleKeyInfo k); i++) body.Append(k.KeyChar);
            mouse = MouseInput.ParseX10(body.ToString());
        }

        if (mouse is not null) key = default;
        return true;
    }

    private ConsoleKeyInfo Next() => _pending.Count > 0 ? _pending.Dequeue() : readKey();

    private bool TryReadWithin(out ConsoleKeyInfo key)
    {
        key = default;
        DateTime end = DateTime.UtcNow.AddMilliseconds(ContinuationWaitMs);
        while (DateTime.UtcNow < end)
        {
            if (KeyAvailable) { key = Next(); return true; }
            Thread.Sleep(1);
        }
        return false;
    }
}
