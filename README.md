# FluentRegedit

A modern, Fluent Design replacement for the Windows Registry Editor (`regedit.exe`), built with **WinUI 3** and the **Windows App SDK**.

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

## Requirements (development)

- **Windows 10 21H1 (build 19041)** or newer (Windows 11 recommended for Mica).
- **.NET 8 SDK** (the project currently builds with the .NET 10 preview SDK installed on the dev machine; .NET 8 is the target framework).
- **Windows App SDK 1.8** runtime (restored automatically via NuGet).
- **Visual Studio 2022 17.10+** with the *Windows App SDK C# Templates* component, **or** the command-line `dotnet` CLI.

## Build

```powershell
dotnet build src
```

The solution lives in `src/FluentRegedit.slnx` and contains a single project, `FluentRegeditApp`.

Supported platforms: `x86`, `x64`, `ARM64`.

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
└── FluentRegeditApp/
    ├── App.xaml(.cs)          # Application entry point
    ├── MainWindow.xaml(.cs)   # Shell window
    ├── Assets/                # App icons & splash
    └── ...
```

Internal structure (added incrementally as features land):

- `Models/`     — registry tree / value DTOs
- `Services/`   — registry access, backup, import/export, search
- `ViewModels/` — MVVM view-models
- `Views/`      — XAML views and dialogs
- `Controls/`   — reusable controls (path bar, value editors, …)

## Contributing

The roadmap in [`roadmap.md`](./roadmap.md) is the source of truth for scope. Pick an unchecked item, open an issue, and send a PR.

## License

TBD.
