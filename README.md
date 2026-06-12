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
- **K9s-style layout** — persistent header, tab bar, content panel, and hotkey footer
- **🍄 SDKs tab** — unified view of installed SDKs + active available channels (8.0, 9.0, 10.0, 11.0…)
- **★ Search tab** — inline search with text input for installed & online SDK catalogs
- **🔥 Project tab** — auto-detect `.sln`, `.slnx`, `.csproj` and run Restore, Build, Test, Run, Publish with live output
- **● Setup tab** — install and configure `dotnetup` from inside the app
- **Tab navigation** — switch tabs with `1-4`, `Tab`/`Shift+Tab`, quit with `q`
- **NativeAOT compiled** — fast startup, small binary, no runtime dependency

## Navigation

```text
 🍄 SDKs  │ ★ Search  │ 🔥 Project  │ ● Setup
───────────────────────────────────────────────
 Tab 1: SDKs      — ↑↓ navigate, i:Install, u:Uninstall, r:Refresh
 Tab 2: Search    — type query + Enter, ↑↓ results, i:Install, /:New search
 Tab 3: Project   — r:Restore, b:Build, t:Test, n:Run, p:Publish, c:Clear
 Tab 4: Setup     — i:Install dotnetup, u:Update, s:Status
 Global: 1-4 switch tabs, Tab/Shift+Tab cycle, q:Quit
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
