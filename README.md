<div align="center">

# .NET SDK Manager

**A cross-platform terminal UI for managing .NET SDKs and Runtimes**

[![Build Status](https://github.com/shahabganji/dotnet-sdk-tui/actions/workflows/build.yml/badge.svg)](https://github.com/shahabganji/dotnet-sdk-tui/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-gold.svg)](https://opensource.org/licenses/MIT)

</div>

`.NET SDK Manager` is a keyboard-driven terminal application built with **Spectre.Console** and compiled with **NativeAOT**. It wraps the official [`dotnetup`](https://aka.ms/dotnetup/docs) CLI to provide a fast, visual experience for discovering, installing, updating, and removing .NET SDKs and Runtimes — all from a single screen.

## Features

- **Animated startup banner** — Aspire-style block letter animation with a teal-lime shine sweep
- **SDKs panel** — View installed SDKs alongside the latest available versions for active and preview channels. Install, uninstall, and update with a single keystroke
- **Runtimes panel** — Same experience for .NET and ASP.NET Core runtimes
- **Live search** — Non-blocking search with debounce and request cancellation. Type to search, results update as you go — the terminal never freezes
- **Setup panel** — Install and manage the `dotnetup` tool itself from within the app
- **Dark / Light themes** — Press `F6` to toggle. The terminal background adapts via OSC 11
- **Lifecycle icons** — At-a-glance status for each version: 🍀 Active, 🏭 Preview, 🚧 Maintenance, 👿 End of Life
- **Smart uninstall** — Automatically resolves dotnetup install specs (feature bands, channels, `latest`) so uninstall just works
- **Scroll windowing** — Panels scroll smoothly when you have many installed versions
- **Graceful exit** — Goodbye message on both `q` and `Ctrl+C`
- **NativeAOT compiled** — Fast startup, small binary, no runtime dependency

## Navigation

| Key | Action |
|-----|--------|
| `↑` / `↓` or `j` / `k` | Navigate rows |
| `Tab` | Cycle focus between panels |
| `i` | Install selected SDK/Runtime |
| `u` | Uninstall selected SDK/Runtime |
| `p` | Update selected SDK/Runtime |
| `r` | Refresh data |
| `F3` | Open search |
| `F6` | Toggle dark/light theme |
| `q` or `Ctrl+C` | Quit |

## Installation

```bash
# macOS / Linux
curl -fsSL https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.sh | bash

# Windows (PowerShell)
irm https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.ps1 | iex
```

Running the install script again will update `dsm` to the latest release.

### Uninstall

```bash
# macOS / Linux
curl -fsSL https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/uninstall.sh | bash

# Windows (PowerShell)
irm https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/uninstall.ps1 | iex
```

### Windows: Defender false positive

Windows Defender may block `dsm.exe` because the binary is unsigned. To allow it:

```powershell
# Option 1: Add exclusion (run as admin)
Add-MpPreference -ExclusionPath "$env:LOCALAPPDATA\dsm"

# Option 2: Windows Security → Virus & threat protection → Protection history → Allow on device
```

### CLI

```bash
dsm                # Launch with animated banner
dsm --no-splash    # Skip the banner animation
dsm --version      # Print version and exit
```

## Build from source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download) or later.

```bash
# Run in development
dotnet run --project src/DotnetSdkTui

# Run without the splash animation
dotnet run --project src/DotnetSdkTui -- --no-splash

# Publish a NativeAOT binary
dotnet publish src/DotnetSdkTui -c Release -r osx-arm64
```

### Supported platforms

| Platform | RID |
|----------|-----|
| macOS (Apple Silicon) | `osx-arm64` |
| macOS (Intel) | `osx-x64` |
| Windows (x64) | `win-x64` |
| Windows (ARM) | `win-arm64` |
| Linux (x64) | `linux-x64` |
| Linux (ARM) | `linux-arm64` |

## Testing

```bash
dotnet test
```

Tests use [hex1b](https://hex1b.dev/guide/cli) for TUI screenshot assertions in a headless virtual terminal.

## Tech stack

- .NET 10 / C# / NativeAOT
- [Spectre.Console](https://spectreconsole.net) — rendering, layout, markup
- [dotnetup](https://aka.ms/dotnetup/docs) — SDK/Runtime acquisition
- System.Text.Json source generators — AOT-safe JSON parsing

## Contributing

Contributions are welcome! Here's how to get started:

1. **Fork** the repository and clone it locally
2. **Create a branch** from `main` for your change (`git checkout -b feature/my-feature`)
3. **Build and run** to make sure everything works: `dotnet run --project src/DotnetSdkTui`
4. **Make your changes** — keep commits focused and well-described
5. **Run the tests**: `dotnet test`
6. **Open a Pull Request** against `main` with a clear description of what you changed and why

### Areas where help is appreciated

- **New platform support** — test and fix issues on platforms you use
- **Accessibility** — improving keyboard navigation, screen reader support
- **Themes** — new color palettes or improvements to the existing dark/light themes
- **Offline mode** — better UX when there's no network connectivity
- **Bug reports** — if something doesn't work, [open an issue](https://github.com/shahabganji/dotnet-sdk-tui/issues)

### Code style

- Keep it simple — avoid over-engineering
- Follow existing patterns in the codebase
- No external dependencies without discussion first

## License

Released under the [MIT License](LICENSE).

---

<div align="center">

Made with ❤ by [Shahab the Guy](https://shahab-the-guy.dev)

</div>
