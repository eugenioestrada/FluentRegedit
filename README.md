# FluentRegedit

<!-- Update OWNER/FluentRegedit to the actual GitHub owner/repo once published. -->
[![CI](https://github.com/OWNER/FluentRegedit/actions/workflows/build.yml/badge.svg)](https://github.com/OWNER/FluentRegedit/actions/workflows/build.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](./LICENSE)

A modern, Fluent Design replacement for the Windows Registry Editor (`regedit.exe`), built with **WinUI 3** and the **Windows App SDK**.

> 🚧 **Status:** Early preview — feature parity with `regedit.exe` is in progress, see the [roadmap](./roadmap.md).

## Tech stack

- **WinUI 3** + **Windows App SDK 1.8**
- **.NET 8**
- **MVVM** architecture
- **xUnit v3** + **AwesomeAssertions** for tests

## Table of contents

- [Why a modern Regedit?](#why-a-modern-regedit)
- [Goals](#goals)
- [Non-goals](#non-goals)
- [Screenshots](#screenshots)
- [Requirements (development)](#requirements-development)
- [Build](#build)
- [Test](#test)
- [Run](#run)
- [Project layout](#project-layout)
- [Contributing](#contributing)
- [License](#license)

## Why a modern Regedit?

The built-in `regedit.exe` has barely changed in over two decades. It still ships with:

- A Win32 UI that ignores Fluent / Mica / dark mode conventions.
- Single-pane navigation with no tabs, no breadcrumbs, no recent locations.
- A search experience that blocks the UI and only finds *one match at a time*.
- No first-class concept of **backups**, **diffs** or **safe edits**.
- A clunky `.reg` import/export flow with no preview.
- No keyboard-friendly path bar — you cannot paste a `HKLM\Software\...` path and jump.
- No context-aware editors for binary, multi-string, or DWORD/QWORD values beyond the bare minimum.

The Windows registry is still a critical surface for power users, IT pros and developers. **FluentRegedit** aims to bring it into 2025 with a polished, productive, Fluent-style experience that is safer by default.

## Goals

1. **Feature parity** with the built-in `regedit.exe` (see [`roadmap.md`](./roadmap.md)).
2. **Safer editing**: automatic snapshots before destructive operations, diff preview on import, undo where feasible.
3. **Modern UX**: Mica backdrop, dark mode, breadcrumb path bar, multi-tab navigation, fast filtering, instant search.
4. **Keyboard-first**: copy paths, paste paths, jump-to-key, command palette.
5. **Scriptable & exportable**: round-trip `.reg` files faithfully and offer JSON/CSV exports.

## Non-goals

- Editing remote registries (initially).
- Replacing Group Policy tooling.
- Mobile / non-Windows support.

## Screenshots

_Screenshots coming soon._

## Requirements (development)

- **Windows 10 21H1 (build 19041)** or newer (Windows 11 recommended for Mica).
- **.NET 8 SDK** (the project currently builds with the .NET 10 preview SDK installed on the dev machine; .NET 8 is the target framework).
- **Windows App SDK 1.8** runtime (restored automatically via NuGet).
- **Visual Studio 2022 17.10+** with the *Windows App SDK C# Templates* component, **or** the command-line `dotnet` CLI.

## Build

WinUI 3 / Windows App SDK requires an explicit platform — `AnyCPU` is **not** supported. Use `x64` (recommended), `x86`, or `ARM64`:

```powershell
dotnet build src -p:Platform=x64
```

The solution lives in `src/FluentRegedit.slnx` and contains the `FluentRegeditApp` project plus the `FluentRegeditApp.Tests` test project.

Supported platforms: `x86`, `x64`, `ARM64`.

## Test

Tests run on **xUnit v3** with **AwesomeAssertions**:

```powershell
dotnet test src
```

The test suite exercises registry services against a sandbox hive under `HKCU\Software\FluentRegedit\Tests`.

## Run

From Visual Studio: set `FluentRegeditApp` as the startup project and press F5.

From the command line (unpackaged):

```powershell
dotnet run --project src\FluentRegeditApp -c Debug
```

> ⚠️ Editing `HKEY_LOCAL_MACHINE` and other protected hives requires running the app **as Administrator**. FluentRegedit will detect this and surface a clear UAC prompt / banner when elevation is needed.

## Project layout

```
src/
├── FluentRegedit.slnx              # Solution
├── FluentRegeditApp/               # Main WinUI 3 app
│   ├── App.xaml(.cs)               # Application entry point
│   ├── MainWindow.xaml(.cs)        # Shell window
│   ├── Assets/                     # App icons & splash
│   ├── Controls/                   # Reusable controls (path bar, value editors, …)
│   ├── Models/                     # Registry tree / value DTOs
│   ├── Properties/                 # Launch settings, etc.
│   ├── Services/                   # Registry access, backup, import/export, search
│   ├── ViewModels/                 # MVVM view-models
│   └── Views/                      # XAML views and dialogs
└── FluentRegeditApp.Tests/         # xUnit v3 + AwesomeAssertions test project
```

## Contributing

The roadmap in [`roadmap.md`](./roadmap.md) is the source of truth for scope. Pick an unchecked item, open an issue, and send a PR. See [CONTRIBUTING.md](./CONTRIBUTING.md) for build/test details and conventions, and [CODE_OF_CONDUCT.md](./CODE_OF_CONDUCT.md) for community expectations.

## License

Licensed under the [MIT License](./LICENSE).
