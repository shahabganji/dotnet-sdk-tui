---
name: clickable-tui
description: Make elements in the dsm Spectre.Console TUI respond to mouse clicks — tabs, buttons, table rows, panels, menu entries, links, or any on-screen target. Use this whenever the user wants something in the terminal UI to be clickable, "work with the mouse", react to a click/tap, or when adding a new interactive region. Also use it when a click is being received but the wrong thing (or nothing) happens, since the same hit-testing rules explain those bugs.
---

# Making TUI elements clickable

This app (`dsm`, a Spectre.Console + NativeAOT TUI) already has working mouse support: xterm
SGR mouse reporting is enabled, click sequences are decoded into events, and the tab strip is
click-to-switch. Adding a new clickable element is mostly about **mapping a click's (column,
row) back to the thing under it** and reusing the existing plumbing — not about reinventing
input handling.

Read this whole file first. The infrastructure section tells you what already exists so you
don't duplicate it; the recipe tells you the four moving parts; the **gotchas** section is
where the real bugs live (they cost this project a full debugging session).

## What already exists (reuse it, don't rebuild it)

| Concern | Where | Notes |
| --- | --- | --- |
| Enable/disable mouse reporting | `src/DotnetSdkTui/Services/MouseInput.cs` — `EnableSequence` / `DisableSequence` | SGR 1006 + button tracking 1000. Already written to the console on startup and around interactive suspends in `App.RunAsync` / `RunInteractiveAndRefreshAsync`. |
| Parse a click into an event | `MouseInput.ParseSgr` / `ParseX10` → `MouseEvent(Button, Column, Row, IsPress)` with `IsLeftPress` | Coordinates are **1-based**, matching the terminal. `IsLeftPress` already filters out motion/wheel/non-left buttons. |
| Read & decode input in the loop | `App.TryReadInput` (+ `TryReadCharWithin`) | Turns a raw `ESC[<…M/m` sequence into a `MouseEvent`; hands everything else back as a `ConsoleKeyInfo`. You rarely need to touch this. |
| Dispatch to a handler | `App` poll loop → `HandleMouseAsync(MouseEvent)` | This is where you add new hit-testing. |

So: **the event already arrives at `HandleMouseAsync` with a correct 1-based column and row.**
Your job is to know where your element is on screen and compare.

## The core problem: knowing where your element is

A click gives you an absolute screen `(Column, Row)`. To act on it you need the element's
absolute screen rectangle. Spectre doesn't hand you layout coordinates, so you derive them.
There are two proven techniques in this codebase; prefer whichever fits:

1. **Compute geometry from the layout** (used for tab *columns*). If you know the padding,
   borders, and content widths, you can calculate column spans directly. See
   `Ui.ComputeTabHitRegions` — it mirrors the `Padder` + panel border + per-label widths to
   produce contiguous 1-based column ranges. This is exact and testable but must be kept in
   sync with the rendering code.

2. **Read the position back from the rendered frame** (used for the tab *row*). The frame is
   rendered to a string before being written to the terminal. Scan that string for a marker
   unique to your element and count newlines to get its row. See `App.FindTabRow`: it finds
   the first line containing a probe token (the first tab's label) and returns `1 + newlines
   before it`. This automatically tracks layout compression/resize because it reflects what was
   actually drawn.

Use technique 2 for **rows** (they shift when the layout compresses on short windows) and
technique 1 for **columns within a known row** (they're stable given the labels). For a brand
new element, the same split usually applies.

## Recipe: add a new clickable region

**1. Give the element a stable, unique on-screen marker.** For row detection you need a token
that appears in the rendered frame only where your element is (and above any coincidental body
text, since detection returns the *first* match). Tab detection uses the first tab's label
(`"SDKs"`), stored in `_tabStripProbe` and derived from `labels[0]` when the body is built.

**2. Compute the element's column span(s).** Add a helper next to `Ui.ComputeTabHitRegions`
that returns 1-based, end-exclusive column ranges, reusing `Ui.VisibleWidth` for width math
(critical for emoji — see gotchas). Store the result on a field the way `_tabHitRegions` is
stored when the screen is built.

**3. Detect the element's row per frame.** In `EmitFrame`, after `bodyText` is built, compute
the row with a `FindTabRow`-style scan and cache it (like `_tabRow`). Recompute every frame —
never hardcode a row; it moves with window size.

**4. Hit-test in `HandleMouseAsync`.** Gate on `m.IsLeftPress`, the correct `Screen`, and the
cached row, then map `m.Column` to your region and act:

```csharp
if (!m.IsLeftPress || _screen != Screen.Main || m.Row != _tabRow) return;
for (int i = 0; i < _tabHitRegions.Count && i < TabOrder.Length; i++)
{
    var region = _tabHitRegions[i];
    if (m.Column >= region.Start && m.Column < region.EndExclusive)
    {
        await SwitchToTabAsync(TabOrder[i]);
        return;
    }
}
```

For a multi-row element (e.g. a table), detect the element's top row and its height, then use
`m.Row - topRow` as the index into rows, applying the same column checks per cell as needed.

## Gotchas (this is where the bugs are)

### The frame must not scroll — strip the trailing newline
`EmitFrame` writes `ESC[H` (home to row 1) then the rendered body, so **screen row = 1 + count
of `\n` before a line**. Spectre's renderer ends its output with a **trailing newline**. If the
frame is as tall as the terminal, writing that final newline on the last row makes the terminal
**scroll up by one line** — every absolute row then shifts up by one, so a click on your element
lands one row too high and hit-testing silently fails. The fix already in `EmitFrame`:

```csharp
if (bodyText.EndsWith('\n')) bodyText = bodyText[..^1];
```

If you change frame emission, preserve this. Symptom to recognize: clicks are received with the
right **columns** but the **row is off by exactly one** (see the diagnosing section).

### Absolute-row hit-testing depends on `ESC[H` + no scroll
Row math only works because the frame is painted from cursor-home with no leading blank output
and no scroll. Don't prepend output before the frame, and keep `FindTabRow`-style detection
reading the *same* `bodyText` that gets written.

### Emoji and wide characters — always use `Ui.VisibleWidth`
Column geometry must count display columns, not `string.Length`. `📦` and `🔍` occupy 2 cells,
`⚙` occupies 1. `Ui.VisibleWidth` / `Ui.IsWide` already encode this. Getting it wrong shifts
every region after the first emoji. Never hand-count label widths.

### 1-based coordinates
`MouseEvent.Column`/`Row` are 1-based (terminal convention), and `ComputeTabHitRegions` /
`FindTabRow` are 1-based too. Keep everything 1-based to avoid off-by-one drift.

### Only act on left presses
Gate on `IsLeftPress`. The terminal also sends a release (`m`) and, with motion tracking, moves
and wheel events — acting on those double-fires or misfires.

### Terminal support varies
Ghostty and iTerm2 send SGR (1006) which works end to end. **Terminal.app** uses legacy X10
(`ESC[M b x y`), and .NET's terminfo decodes `ESC[M` as F1 via `Console.ReadKey`, so the X10
path is effectively dead there. Treat mouse as an enhancement over keyboard, never the only way
to reach a feature. Don't promise Terminal.app click support.

### No hand cursor on hover
You can't portably change the mouse pointer to a hand over clickable regions — the terminal owns
the pointer. Only OSC 8 hyperlinks give a hover-hand in some terminals, and only for URLs. Don't
attempt cursor changes; make clickable areas discoverable another way if needed.

## Testing clickable elements (use hex1b, not ad-hoc PTY scripts)

The `hex1b` skill wraps the app in a headless virtual terminal and can inject real mouse clicks.
This is the correct way to verify — it exercises the same enable → emit → click → decode path a
real terminal does, and it reproduces scroll/row offsets that a naive PTY harness hides.

```bash
# Launch the app at a realistic size
ID=$(dotnet hex1b terminal start --json --width 120 --height 40 -- \
  dotnet run --project ./src/DotnetSdkTui/DotnetSdkTui.csproj | jq -r .id)

dotnet hex1b assert $ID --text-present "SDKs" --timeout 20   # wait for render

# hex1b mouse coords are 0-based col row; the app's events are 1-based —
# clicking hex1b col C hits the app's column C+1. Account for this when
# targeting a region computed in 1-based space.
dotnet hex1b mouse click $ID 19 5        # click a tab
dotnet hex1b capture screenshot $ID --format text   # verify the active view changed

dotnet hex1b terminal stop $ID
```

Verify by asserting on **body content that differs per state** (e.g. the Runtimes view has a
`Component` column that the SDKs view lacks; the Search view has a text input, not a table) —
the tab-strip header always shows every label regardless of which tab is active, so it can't
tell you which tab is selected.

Add unit tests for pure geometry the way `tests/.../Theme/TabHitRegionTests.cs` asserts exact
region spans and `tests/.../Services/MouseInputTests.cs` covers the parsers. Geometry and parsing
are deterministic and cheap to test; the end-to-end click is what hex1b is for.

## Diagnosing "the click doesn't work"
Reason about it in this order — each step isolates one layer:

1. **Is the event arriving?** If unsure, temporarily log inside `HandleMouseAsync` (gate behind
   an env var like the project has done before, and remove it after). No event → mouse reporting
   isn't enabled for that screen, or the sequence isn't being decoded (`TryReadInput`).
2. **Do the columns match but the row is off by one?** That's the trailing-newline scroll —
   confirm `EmitFrame` still strips it, and that your row detection reads the emitted `bodyText`.
3. **Is the row right but columns are off?** Emoji/width math — audit your `VisibleWidth` usage
   and the padding/border constants against the actual render.
4. **Everything matches but nothing happens?** Your handler's `Screen`/state gate is wrong, or
   the region→action mapping is off.

A note learned the hard way: `dsm` is normally an installed NativeAOT binary
(`~/.local/bin/dsm`). Editing source does **not** change it. Test your changes with `dotnet run`
or `dotnet hex1b … -- dotnet run …`, and never overwrite the user's installed binary to test.

## Key files
- `src/DotnetSdkTui/Services/MouseInput.cs` — enable/disable, `MouseEvent`, `ParseSgr`, `ParseX10`.
- `src/DotnetSdkTui/App.cs` — `TryReadInput`, `HandleMouseAsync`, `FindTabRow`, `EmitFrame`
  (trailing-newline strip + row detection), and the mouse enable/disable calls.
- `src/DotnetSdkTui/Theme/Ui.cs` — `TabHitRegion`, `ComputeTabHitRegions`, `VisibleWidth`, `IsWide`.
- `tests/DotnetSdkTui.Tests/Theme/TabHitRegionTests.cs`, `tests/.../Services/MouseInputTests.cs`.
- `.github/skills/hex1b/SKILL.md` — the terminal-automation tool for injecting clicks in tests.
