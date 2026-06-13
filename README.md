<div align="center">

```text
    ╔═══════════════════════════════╗
    ║  ★  dotnet-sdk-tui  ★        ║
    ║  ●  It's-a me, SDK Manager!  ║
    ╚═══════════════════════════════╝
       🍄  ★  🔥  ●  🍄  ★  🔥
```

**A cross-platform NativeAOT C# terminal UI for managing .NET SDKs — Super Mario style!**

[![Build Status](https://github.com/shahabganji/dotnet-sdk-tui/actions/workflows/build.yml/badge.svg)](https://github.com/shahabganji/dotnet-sdk-tui/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-gold.svg)](https://opensource.org/licenses/MIT)

</div>

`dotnet-sdk-tui` is a **Super Mario-themed** k9s-style terminal application built with **Spectre.Console** that wraps the official **`dotnetup`** tool and core **`dotnet`** CLI commands. It provides a fast, keyboard-driven, panel-based experience for discovering, installing, updating, and removing .NET SDKs, plus common project actions.

## Features

- **? Block coin animation splash** — Mario-themed startup with coin burst animation
- **K9s-style unified layout** — all sections visible on one screen: SDKs, Search, Project, Setup
- **🍄 SDKs section** — real installed SDKs + available channels with Install/Uninstall/Update actions, lifecycle info
- **★ Search section** — inline live search with debounce, results update as you type
- **🔥 Project section** — auto-detect `.sln`, `.slnx`, `.csproj` and run Restore, Build, Test, Run, Publish with live streaming output
- **● Setup section** — install and update `dotnetup` tool itself from inside the app
- **🌙/☀️ Theme toggle** — dark and light themes, press `T` to switch
- **Section focus** — switch focus with `F1-F4`, `Tab`/`Shift+Tab`, quit with `q`
- **NativeAOT compiled** — fast startup, small binary, no runtime dependency

## Navigation

```text
 🍄 SDKs  │ ★ Search  │ 🔥 Project  │ ● Setup
───────────────────────────────────────────────
 F1: SDKs      — ↑↓ navigate, i:Install, u:Uninstall, p:Update, r:Refresh
 F2: Search    — type to search (live), ↑↓ results, i:Install
 F3: Project   — r:Restore, b:Build, t:Test, n:Run, p:Publish, c:Clear
 F4: Setup     — i:Install dotnetup, u:Update dotnetup, r:Refresh
 Global: F1-F4 focus, Tab/Shift+Tab cycle, T:Theme, q:Quit
```

## Installation

```bash
# macOS / Linux
curl -fsSL https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.sh | bash

# Windows (PowerShell)
irm https://raw.githubusercontent.com/shahabganji/dotnet-sdk-tui/main/install/install.ps1 | iex
```

## Build from source

```bash
dotnet build src/DotnetSdkTui
dotnet run --project src/DotnetSdkTui
```

## CLI flags

```bash
dotnet-sdk-tui              # Full app with splash animation
dotnet-sdk-tui --no-splash  # Skip splash, go straight to tabs
dotnet-sdk-tui --version    # Print version and exit
```

## NativeAOT publish

```bash
dotnet publish src/DotnetSdkTui -c Release -r osx-arm64
```

## Supported platforms

- `osx-x64`
- `osx-arm64`
- `win-x64`
- `win-arm64`
- `linux-x64`
- `linux-arm64`

## dotnetup integration

- Uses **`dotnetup`**, the official .NET SDK acquisition tool, when available
- Falls back to **`dotnet --list-sdks`** when `dotnetup` is not installed
- Runs project actions through **`dotnetup dotnet <cmd>`** for correct SDK hive resolution
- Shows active channels (LTS + current) directly — no explicit search required

## Testing

```bash
dotnet test   # 39 unit + integration tests
```

## CI/CD

- **GitHub Actions CI** builds and tests on Ubuntu, macOS, and Windows
- **GitHub Actions CD** publishes NativeAOT binaries for 6 RIDs and creates a GitHub Release

## Tech stack

- .NET 10 · C# · NativeAOT · Spectre.Console · System.Text.Json source generators

## Contributing

Contributions welcome! Open an issue for bugs or ideas, submit a PR to improve the app.

## License

Released under the **MIT License**.
