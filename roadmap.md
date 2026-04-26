# FluentRegedit — Roadmap

This roadmap tracks **feature parity with the built-in Windows Registry Editor** plus the **modern UX additions** that justify FluentRegedit's existence.

Legend: `[ ]` not started · `[~]` in progress · `[x]` done

---

## 1. Core navigation

- [~] Tree view of the five predefined root hives:
  - [x] `HKEY_CLASSES_ROOT` (HKCR)
  - [x] `HKEY_CURRENT_USER` (HKCU)
  - [x] `HKEY_LOCAL_MACHINE` (HKLM)
  - [x] `HKEY_USERS` (HKU)
  - [x] `HKEY_CURRENT_CONFIG` (HKCC)
- [x] Lazy-loaded subkeys (expand on demand).
- [ ] 32-bit / 64-bit registry view toggle (WOW6432Node).
- [x] Two-pane layout: tree on the left, values on the right.
- [~] Keyboard navigation (arrows, Enter, F2 rename, Del delete).
- [x] Right-click context menus on keys and values.
- [x] Status bar showing current full path and value count.

## 2. Path bar & navigation UX *(modern)*

- [x] Editable breadcrumb path bar (Explorer-style).
- [x] Paste a full `HKLM\Software\Foo` path and jump.
- [x] Back / forward / up navigation buttons with history.
- [x] Copy current key path / key name / value name to clipboard.
- [ ] "Jump to key" command palette (Ctrl+G).
- [ ] Multi-tab browsing of different keys.
- [ ] Recent locations & pinned favorites list.

## 3. Values

- [x] List values with columns: **Name**, **Type**, **Data**.
- [x] Default `(Default)` value handling.
- [ ] Sortable & resizable columns.
- [ ] Inline filter / quick-filter box for the value pane.
- [ ] All registry value kinds supported:
  - [x] `REG_SZ`
  - [x] `REG_EXPAND_SZ`
  - [x] `REG_MULTI_SZ`
  - [x] `REG_DWORD` (decimal / hex toggle)
  - [x] `REG_QWORD` (decimal / hex toggle)
  - [x] `REG_BINARY` (hex editor)
  - [x] `REG_NONE`
  - [ ] `REG_LINK` (read-only)
  - [ ] `REG_RESOURCE_LIST` (read-only)
  - [ ] `REG_FULL_RESOURCE_DESCRIPTOR` (read-only)
  - [ ] `REG_RESOURCE_REQUIREMENTS_LIST` (read-only)

## 4. Editing

- [x] Create new key.
- [ ] Rename key.
- [x] Delete key (with confirmation + automatic snapshot).
- [x] Create new value (any kind).
- [x] Edit value data with type-specific editor dialogs.
- [ ] Rename value.
- [x] Delete value.
- [x] Modify default `(Default)` value.
- [ ] Permissions editor (ACL on keys).
- [ ] Take ownership helper.

## 5. Search *(parity + modern)*

- [x] Find dialog: search in **key names**, **value names**, **value data**, with **match-whole-string** and **case-sensitive** options.
- [ ] Find Next (F3).
- [x] **Modern**: results panel with *all matches at once*, grouped by hive, with previews.
- [ ] Regex search.
- [x] Scoped search (current subtree only).
- [x] Cancellable, non-blocking background search.

## 6. Import / Export

- [x] Export current key (or whole hive) to `.reg` (Unicode v5 format).
- [ ] Export to legacy `.reg` (Win9x format).
- [x] Import `.reg` files.
- [ ] **Modern**: diff preview before import.
- [ ] Export to `.json` and `.csv` (modern).
- [ ] Drag & drop `.reg` files onto the window.
- [ ] Save hive (`reg save`) / Load hive (`reg load`).
- [ ] Unload hive.
- [ ] Connect / Disconnect Network Registry — *(stretch goal)*.

## 7. Backup & safety *(modern)*

- [x] One-click "Backup current key" → timestamped `.reg` snapshot in user data.
- [x] Automatic snapshot before any destructive operation (delete key, delete value).
- [ ] Snapshot manager: list, restore, delete, export snapshots.
- [ ] Optional global "system restore point" before risky ops.
- [ ] Undo last operation (best-effort, journal-based).

## 8. Favorites & bookmarks

- [ ] Add to favorites (regedit parity).
- [ ] Manage favorites (rename / delete / reorder).
- [ ] Sync favorites to a portable JSON file.

## 9. Modern UX polish

- [x] Mica backdrop & custom title bar.
- [ ] Dark / light / system theme.
- [ ] Compact / comfortable density toggle.
- [ ] Localization scaffold (en, es).
- [ ] Settings page (theme, default view, snapshot directory, confirmation prompts).
- [ ] Telemetry off by default; transparent diagnostics log.
- [ ] Accessibility: full keyboard, screen-reader names on tree/list items, high-contrast.
- [ ] Command palette (Ctrl+Shift+P) with all actions.
- [ ] Toast notifications for long-running operations (export, search, import).

## 10. Tooling & quality

- [ ] Unit tests for registry services (against a sandbox hive under `HKCU\Software\FluentRegedit\Tests`).
- [ ] CI build (GitHub Actions, `dotnet build src`).
- [ ] MSIX packaging & signing pipeline.
- [ ] Crash reporting opt-in.
