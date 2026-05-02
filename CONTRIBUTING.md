# Contributing to FluentRegedit

Thanks for your interest in improving FluentRegedit! This document covers the basics of filing issues, building locally, and sending pull requests.

## Filing issues

Please use the issue templates:

- **Bug report** — for crashes, incorrect behavior, or UI glitches.
- **Feature request** — for new functionality or UX improvements.

Before filing, search existing issues and the [roadmap](./roadmap.md) to avoid duplicates. For security issues, see [SECURITY.md](./SECURITY.md) — **do not** open a public issue.

## Prerequisites

- **Windows 10 build 19041 (21H1)** or newer (Windows 11 recommended).
- **.NET 10 SDK**.
- **Visual Studio 2026 +** with the *Windows App SDK C# Templates* component, **or** the `dotnet` CLI.
- Windows App SDK 2.0 runtime (restored automatically via NuGet).

## Build & test

WinUI 3 / Windows App SDK requires an explicit `x64` (or `x86`/`ARM64`) platform — `AnyCPU` is not supported.

```powershell
# Restore & build
dotnet build src -p:Platform=x64

# Run the app (unpackaged)
dotnet run --project src\FluentRegeditApp -c Debug

# Run the test suite (xUnit v3 + AwesomeAssertions)
dotnet test src
```

## Coding conventions

- Target framework is **.NET 10**, nullable reference types are **enabled** — keep them on and fix warnings rather than suppressing them.
- Follow the existing **MVVM** layout: `Models/`, `Services/`, `ViewModels/`, `Views/`, `Controls/`.
- Keep registry I/O in `Services/`; never call `Microsoft.Win32.Registry` directly from views or view-models.
- Run `dotnet format` before sending a PR if it's available in your toolchain.
- Match the surrounding style — 4-space indent for C#, file-scoped namespaces, `var` when the type is obvious.

## Pull request process

1. Branch from `main` (e.g. `feature/multi-tab`, `fix/path-bar-paste`).
2. Make focused commits — one logical change per PR where possible.
3. Run `dotnet test src` and make sure it passes.
4. Update [`roadmap.md`](./roadmap.md) if your change advances or completes a roadmap item.
5. Link the related issue and roadmap item in the PR description.
6. Fill in the PR template checklist and request a review.

By contributing you agree that your contributions will be licensed under the [MIT License](./LICENSE).
