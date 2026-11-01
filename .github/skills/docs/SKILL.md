---
name: docs
description: Author and update the project documentation site (docs/) and capture emoji-correct screenshots of the dsm TUI. Use when adding or editing guide pages, adding screenshots, or gating doc content by platform.
---

# Documentation Skill

How to work on the `dsm` documentation site under `docs/`, and — most importantly —
how to capture **screenshots of the TUI that render emoji/symbols correctly**.

## Site structure

The site is plain static HTML (deployed as-is to Azure Static Web Apps — **no build step**),
modelled on aspire.dev (marketing home + Starlight docs) with the dsm teal/lime palette.

- `docs/index.html` — **landing page** (`<body data-page="home" class="landing">`): hero whose copy
  fades/blurs on scroll, auto-hiding header, CSS-3D stack of ".NET SDK" tiles, a "Live preview"
  window cycling TUI screenshots, numbered `.lp-section`s (`.kicker` + `.sec-title`), footer.
  Its ids are its own (`#get-started`, `#features`, …); any **other** hash (e.g. old `index.html#install`)
  is redirected to `docs.html#…` by `landing.js` — never reuse a docs id on the landing page.
- `docs/assets/landing.css` / `landing.js` — landing-only styles/behaviour, loaded **after**
  `site.css`/`site.js` (which still provide tokens, header, buttons, tabs, code frames, search, banner).
- `docs/docs.html` — docs home (hero, "Choose your path", features, install, reference, contributing).
- `docs/guide.html` — the **.NET** step-by-step guide.
- `docs/guide-brew.html` — the **Homebrew** guide (macOS-only).
- `docs/assets/site.css` — **all** styles: design tokens (`:root` dark default, `[data-theme="light"]`),
  shell layout, components, responsive rules. Pages have **no** inline `<style>` blocks.
- `docs/assets/site.js` — **all** behavior. The `NAV` array at the top is the single source of truth
  for the left sidebar (topics → groups → items `{title, page, hash, badge, badgeKind, macOnly}`);
  every `hash` must exist as an `id` on that page. It also builds the right/mobile "On this page" TOC,
  heading anchors, code-frame copy buttons, tabs, scrollspy, the ⌘K search dialog, theme toggle,
  sidebar collapse/drawer, `.mac-only` reveal and the latest-release banner (skipped on `file:`).
- `docs/images/NN-name.png` — screenshots, numbered sequentially (`01`–`12` .NET, `13`+ brew).

**Page shell** (identical across pages — copy it verbatim): inline head script (applies saved
`theme`/`sidebar-collapsed` before paint) → `header.sl-header` → `.sidebar-backdrop` →
`aside#sidebar.sl-sidebar` (filled by JS) → `main.sl-main#_top` containing `.sl-banner[hidden]`,
`details.mobile-toc[hidden]`, and `.sl-page > .sl-content-col (header.page-header + .sl-content)`
+ `aside.sl-toc` → `#search-dialog` → `<script src="assets/site.js">`. `<body data-page="...">`
tells JS which sidebar topic is active (`docs`, `guide`, `guide-brew` — the `page` values in `NAV`
are file names without `.html`). The brand link goes to `index.html` (landing); "Overview" is `docs.html`.
Header CTA: `<span class="lbl-long">Try .NET SDK Manager</span><span class="lbl-short">Try dsm</span>`
(short label below 64rem).

**Components** (classes in `site.css`):

- `.step[id] > .step-header > .step-num + h2` — numbered Starlight-style steps (scrollspy marks `.is-current`).
- `.callout.tip|.note|.warn|.caution|.danger` with a `.ctitle` — asides.
- `.code-frame.terminal` (window dots + `.frame-title`) or `.code-frame` (`.frame-tab`); bare `<pre>`
  is auto-framed. `data-nocopy` disables the copy button. Syntax spans: `.cmd .flag .arg .comment`.
- `.tabs[data-sync="key"]` with `role="tab" data-os="mac|win"` buttons — synced/persisted across pages.
- `.spotlight`, `.legend`, `.card-grid > .card`, `ul.paths` (timeline links), `.hero-card`, `.badge`.
- `ol.breadcrumbs`, `.page-actions` (Edit page / `[data-copy-link]`), `.pager` (`a.prev`/`a.next`), `.sl-footer`.
- TOC includes every `.sl-content h2, h3` except inside `.hero-card`, `.callout`, `.card`, `.spotlight`, `.no-toc`.

When adding a guide, copy `guide.html`, swap the steps, add a topic (and groups) to `NAV` in
`site.js`, and update the `.pager` prev/next links on neighbouring pages.

## Capturing TUI screenshots with emoji (IMPORTANT)

The app uses emoji/symbols (📦 🍀 🏭 🍺 ⬆ ❤). **hex1b's PNG export cannot render these**
in the current version (0.164.1) — they come out as tofu boxes, and its **SVG export drops
astral-plane emoji** (🍺 📦 …) entirely. Capturing `--format text`/`png`/`svg` therefore all fail.

**Working pipeline: hex1b HTML export → headless Chrome render.** hex1b's HTML export keeps
the real characters; Chrome renders them with full color-emoji + system fonts.

```bash
CHROME="/Applications/Google Chrome.app/Contents/MacOS/Google Chrome"

# 1. Launch the app in a virtual terminal (120x40 shows full lists without scroll truncation)
ID=$(dotnet hex1b terminal start --json --width 120 --height 40 -- \
       dotnet run --project src/DotnetSdkTui -- --no-splash \
     | python3 -c "import sys,json;print(json.load(sys.stdin)['id'])")
dotnet hex1b assert "$ID" --text-present "SDK Manager" --timeout 30

# 2. Drive the UI to the screen you want (example: Homebrew list)
dotnet hex1b keys "$ID" --key F2     # open workspace
dotnet hex1b keys "$ID" --key R      # refresh; give async loads a few seconds
sleep 7

# 3. Export the screen as HTML (keeps emoji as real text)
dotnet hex1b capture screenshot "$ID" --format html --output /tmp/shot.html

# 4. Force SVG to fill the viewport and eliminate every wrapper margin/padding/border,
#    so the resulting PNG contains only the terminal — not a single pixel of navy border.
#    Use `|` as the sed delimiter — `#` in hex colors breaks `#`-delimited sed.
sed -i '' 's|</head>|<style>*{margin:0!important;padding:0!important}html,body,body.minimal{background:#1a1a2e!important;height:100%!important;width:100%!important}.container,body.minimal .container{height:100vh!important;width:100vw!important;padding:0!important;margin:0!important}.svg-container,body.minimal .svg-container{background:transparent!important;border:none!important;box-shadow:none!important;padding:0!important;margin:0!important;display:block!important;width:100vw!important;height:100vh!important}.svg-container svg{box-shadow:none!important;display:block!important;width:100vw!important;height:100vh!important;max-width:none!important;max-height:none!important}</style></head>|' /tmp/shot.html

# 5. Render to PNG with Chrome (?minimal=true hides hex1b's inspector chrome).
#    Extract the SVG's intrinsic dimensions and pass them as --window-size so the terminal
#    fills the frame exactly. --default-background-color pins Chrome's page background to
#    the terminal-dark color so no white ever bleeds through.
DIMS=$(perl -ne 'if(/<svg[^>]*width="(\d+)"[^>]*height="(\d+)"/){print"$1,$2";exit}' /tmp/shot.html)
"$CHROME" --headless=new --no-sandbox --disable-gpu --hide-scrollbars \
  --force-device-scale-factor=2 --default-background-color=1e1e1e --window-size="$DIMS" \
  --screenshot=docs/images/NN-name.png "file:///tmp/shot.html?minimal=true"

# 6. (Belt-and-suspenders) Auto-trim any lingering uniform navy border with PIL.
#    A well-formed capture is already pixel-perfect and this is a no-op.
python3 - <<'PY'
from PIL import Image, ImageChops
out = "docs/images/NN-name.png"
im = Image.open(out)
bg = Image.new(im.mode, im.size, (26, 26, 46))   # hex1b's #1a1a2e
diff = ImageChops.difference(im, bg)
bbox = diff.getbbox()
if bbox and bbox != (0, 0, im.size[0], im.size[1]):
    im.crop(bbox).save(out)
PY

# 7. Clean up
dotnet hex1b terminal stop "$ID"
```

Notes:
- `--force-device-scale-factor=2` → crisp 2x images.
- `--window-size` MUST match the SVG's intrinsic `width`/`height` (extracted at step 5)
  and the CSS in step 4 must force the SVG to fill the viewport (`width:100vw; height:100vh`),
  otherwise hex1b's flex-centered container leaves navy padding around the terminal.
- **Verify pixel-perfect framing** by sampling the corner pixel: it should be terminal-dark
  (~`(30, 30, 30)`), never hex1b's navy (`(26, 26, 46)` = `#1a1a2e`). The step-6 auto-trim
  is a safety net.
- **Background gotcha**: hex1b's `?minimal=true` sets `body { background: transparent }`,
  which headless Chrome renders as WHITE unless overridden. The step-4 sed pins body to
  `#1a1a2e` and step-5's `--default-background-color=1e1e1e` matches the terminal itself.
- Always **verify** by viewing the produced PNG before committing (the view tool renders PNGs visually).
- Background loads (installed list, search) can lag on a busy machine — `sleep` generously or
  press `r` to force a refresh before capturing.
- This same pipeline works for the `.NET` screens too if those images ever need regenerating.

## Gating doc content to a platform (e.g. macOS-only)

Homebrew is macOS-only, so its menu links only show on macOS. The docs are static, so detect
client-side: mark elements `class="mac-only" style="display:none"` (or `macOnly: true` in `NAV`);
`site.js` reveals them with:

```js
(function () {
  var ua = navigator.userAgent || '';
  var plat = (navigator.userAgentData && navigator.userAgentData.platform) || navigator.platform || '';
  var isMac = /Mac/i.test(plat + ' ' + ua) && !/iPhone|iPad|iPod/i.test(ua);
  if (isMac) document.querySelectorAll('.mac-only').forEach(function (el) { el.style.display = ''; });
})();
```

Hidden-by-default means non-macOS visitors never see a flash. The guide page itself stays
reachable by direct URL — only the menu link is gated.

## Conventions

- Documentation for app features lives in `docs/` (the site), **not** the README. Keep README focused
  on install/build/contribute.
- Key badges use `<kbd>`; inline commands/paths use `<code>`; tips/warnings use `.callout.tip`/`.callout.warn` etc.
- Edit styles/behavior only in `docs/assets/site.css` / `site.js`; don't add per-page `<style>`/`<script>` blocks (beyond the shell's theme head script).
- Verify visually in dark **and** light themes and at mobile width (~400px) before committing.
- New screenshots: next sequential number in `docs/images/`, referenced from a `<figure>` with a `<figcaption>`.
- Related testing tool: see the [hex1b skill](../hex1b/SKILL.md) for the full terminal-automation reference.
