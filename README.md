# Anchor

[![Release](https://img.shields.io/github/v/release/framedparadox/anchor-dock?label=release)](https://github.com/framedparadox/anchor-dock/releases/latest)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-Windows%2011%20%C2%B7%20x64-0078D4)](#requirements)
[![Built with](https://img.shields.io/badge/.NET%2010%20%C2%B7%20WinUI%203-512BD4)](#requirements)

A floating dock for Windows 11, built with **WinUI 3 / Windows App SDK**. It floats a
compact, glass "strip" above the taskbar that holds apps, files, folders, and web links —
launch anything with a click.

The glass is the **real Windows 11 acrylic material** (the same `DesktopAcrylicBackdrop`
the shell uses), so it blurs the desktop behind it. Pick a **light**, **dark** (default) or
**system-following** theme from Settings.

![The dock](docs/dock.png)

Free and open source (MIT). No accounts, no telemetry, no ads and no admin rights — the only thing
Anchor ever sends over the network is a request for a web link's favicon (see
[Privacy & data](#privacy--data)).

---

## Table of contents

- [Features](#features)
- [Install](#install)
- [Requirements](#requirements)
- [Build & run](#build--run)
- [Tests](#tests)
- [Using it](#using-it)
- [Run / debug in VS Code](#run--debug-in-vs-code)
- [Configuration](#configuration)
- [Privacy & data](#privacy--data)
- [Security notes](#security-notes)
- [Troubleshooting](#troubleshooting)
- [Architecture](#architecture)
- [FAQ](#faq)
- [Known limitations](#known-limitations)
- [Roadmap / future enhancements](#roadmap--future-enhancements)
- [Contributing](#contributing)
- [Credits & support](#credits--support)
- [License](#license)

---

## Features

- **Glass background** — Windows 11 taskbar-style acrylic. Real desktop blur, kept
  translucent even though a dock is never the focused window (see *Architecture*), with the
  DWM border rim suppressed so there's no outline around the rounded glass in any theme.
- **Theme** — choose **Light**, **Dark** (default) or **System** (follow the Windows setting)
  from Settings ▸ General. The dock, its glass and the Settings/Add windows all switch together;
  a High Contrast accessibility theme always overrides it.
- **Rounded corners** — the native Windows 11 window rounding (DWM).
- **Taskbar-sized icons** — 24 px icons in 40 px cells, matching the Windows 11 taskbar.
- **Tray icon** — Anchor sits in the notification area while it runs. **Left-click** brings the
  dock to the front (even when it's tucked behind a screen edge); **right-click** gives you
  *Show/Hide dock*, *Add new…*, *Settings…* and *Quit Anchor*. Since the dock is deliberately off
  the taskbar and Alt-Tab, this is the always-available handle on a running Anchor — and it
  re-adds itself if Explorer restarts.
- **Global keyboard shortcut** — a system-wide hotkey (**Ctrl+Alt+A** by default) summons the
  dock: it un-hides it, slides it back out from its edge, raises it above other windows and gives
  it focus. Re-assign it in Settings ▸ General by clicking the shortcut button and pressing the
  combination you want (Esc cancels, Backspace clears); Anchor tells you if another app already
  owns it. At least one of Ctrl / Alt / Win is required.
- **Eight languages** — English, Deutsch, Español, Français, हिन्दी, 日本語, Português (Brasil) and
  简体中文. Anchor follows your Windows display language by default, or pin one in
  Settings ▸ General; changing it offers a one-click restart so every window switches over.
- **Settings window** — a gear button opens a Windows-Settings-style window (Mica, left
  navigation) with a **General** page (theme, language, keyboard shortcut, position,
  transpose-on-side-edges, auto-hide, always-on-top, start-with-Windows, reset), an
  **Apps & links** page that lists every entry with a show/hide switch and a remove button, and an
  **About** page (version + a plain-language privacy summary).
- **Add-to-Dock window** — a Windows-app-style modal for adding an **app / file / folder / web
  link / shortcut**, with a type picker, Browse, and auto-suggested names.
- **Automatic icons** — apps, files and folders use the Windows shell icon (the same icon
  Explorer shows); web links auto-fetch the site's **favicon**, cached to disk so it downloads
  once and still shows offline.
- **Drag & drop to add** — drag an app, shortcut (`.lnk`), file or folder from Explorer or the
  desktop straight onto the dock to add it; a URL dragged from a browser becomes a web link.
- **Reorder by dragging** — drag an icon left/right (or up/down when vertical) to rearrange it;
  dragging the dock's background moves the whole dock (the two gestures never conflict).
- **Snap to any edge → hides where you left it** — drop the dock near a screen edge and it snaps
  flush and auto-hides behind *that* edge on *that* monitor, revealing on cursor approach. It
  tucks behind the physical screen edge — at the bottom that means **behind the taskbar**, with
  only a small **notch** peeking over it — stays at the position you placed it along the edge (no
  jumping to center), and reveals when you reach the edge (or the notch). On an edge **shared with
  another monitor** it stays pinned and visible instead of sliding into the neighboring screen.
- **Transpose (optional)** — a Settings toggle makes the dock stack its icons
  vertically when snapped to the **left or right** edge; top, bottom and floating stay horizontal.
- **Always on top (optional)** — the dock stays above other windows; a Settings toggle lets you
  turn that off while it's floating (when snapped it stays on top so the auto-hide reveal works).
- **Show / hide without deleting** — hide items from the dock (Settings ▸ Apps, or an icon's
  right-click menu) while keeping them in the list.
- **Empty state** — with no items the dock shows a **"＋ Add New"** button.
- **Per-item menu** — right-click an icon: Open, Edit, Rename, Move left/right, Hide, Remove.
- **Keyboard & screen-reader friendly** — items and the gear are real `Button`s: Tab / arrow-key
  focus, Space/Enter to launch, focus visuals, and Narrator names. Context menus are reachable
  with the Menu key / Shift+F10; auto-hide honors the *reduced-motion* and *high-contrast*
  accessibility settings.
- **Single instance** — launching Anchor a second time (e.g. from the Start menu while the
  "start with Windows" copy is already running) quietly exits rather than stacking a second dock.
- **Persistent** — items, position, snap state and settings are saved to JSON and restored next launch.
- **Out of the way** — borderless, always-on-top, hidden from the taskbar and Alt-Tab.

The Settings window (Mica, Windows-Settings-style navigation):

![Settings](docs/AnchorDock_Settings.png)

## Install

Anchor is **portable** — there is no installer, no setup wizard and nothing written outside your
own user profile.

1. Download **`Anchor-win-x64-<version>.zip`** from the
   [latest release](https://github.com/framedparadox/anchor-dock/releases/latest).
2. Extract it to a folder you intend to keep (e.g. `%LocalAppData%\Programs\Anchor`) — Anchor runs
   from where you unzip it, so don't launch it out of the Downloads/temp folder.
3. Run **`Anchor.exe`**. The dock appears above the taskbar and an anchor icon appears in the
   notification area.
4. Optional: turn on **Settings ▸ General ▸ Start with Windows** so it comes back after a reboot.

Notes:

- The zip is a **self-contained** build — ~86 MB compressed, ~215 MB on disk once extracted. It
  carries .NET 10 and the Windows App SDK with it, so nothing else has to be installed.
- Releases are **not code-signed yet**, so SmartScreen may show "Windows protected your PC" the
  first time — choose *More info ▸ Run anyway*. To verify the download instead of trusting it, the
  expected SHA-256 is published in
  [`ajaykontham.Anchor.installer.yaml`](packaging/winget/manifests/a/ajaykontham/Anchor/1.0.0/ajaykontham.Anchor.installer.yaml);
  compare it with `Get-FileHash Anchor-win-x64-1.0.0.zip`.
- **Updating** — quit Anchor (tray ▸ *Quit Anchor*), extract the new zip over the old folder, start
  it again. Your dock lives in `%AppData%\Anchor` and is untouched by the swap.
- **Uninstalling** — quit Anchor, turn off *Start with Windows* first (or delete the `Anchor` value
  under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`), then delete the extracted folder and
  `%AppData%\Anchor`.

A `winget` package (`ajaykontham.Anchor`) is prepared but not yet accepted into the public
`microsoft/winget-pkgs` repository, so for now the release zip is the way to install.

## Requirements

- Windows 11 (tested on build 26200) — Windows 10 1809+ (build 17763) should also work; that is
  the app's `TargetPlatformMinVersion`.
- **x64 only.** WinUI cannot target `AnyCPU`, and the project builds `win-x64`; there is no ARM64 or
  x86 build yet (an ARM64 build is on the [roadmap](#roadmap--future-enhancements)).
- No admin rights, at install or at run time.
- To *build* it: the **.NET 10 SDK**. The build is **self-contained**, so the Windows App Runtime
  does **not** need to be installed separately — on either your machine or a user's.

## Build & run

```powershell
# from the repo root
dotnet build src/Anchor/Anchor.csproj -c Release -p:Platform=x64

# run it
./src/Anchor/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/Anchor.exe
```

> WinUI apps cannot target `AnyCPU`; always pass `-p:Platform=x64` (this machine's architecture)
> for a single project. The whole solution builds with `dotnet build Anchor.slnx -c Release`
> (both projects are x64-only, so x64 is the default — don't add `-p:Platform=x64` at the
> solution level, which trips MSBuild's solution-configuration mapping).

To produce the distributable, self-contained folder and zip (what a release ships):

```powershell
pwsh scripts/package-release.ps1            # version comes from the csproj
pwsh scripts/package-release.ps1 -Version 1.1.0
```

It publishes to `publish\Anchor-win-x64\`, zips that to `dist\Anchor-win-x64-<version>.zip` and
prints the zip's SHA-256. The version comes from
[`src/Anchor/Anchor.csproj`](src/Anchor/Anchor.csproj) — bump `<Version>`, `<FileVersion>` and
`<AssemblyVersion>` together, since Settings ▸ About reads the assembly version.

## Tests

```powershell
dotnet test tests/Anchor.Tests/Anchor.Tests.csproj -p:Platform=x64
```

`tests/Anchor.Tests/` covers the pure-logic pieces (`Models/`, `Services/DockItemFactory.cs`,
`Services/Loc.cs`) with xUnit — the `Kind → Glyph` mapping and property-change notifications on
`DockItem`, the first-run defaults and JSON round-trip of `DockConfig`, `DockItemFactory`
classification / name suggestion, `HotkeyGesture` parsing / rendering / registrability, and the
translation tables (every language defines every English key, no blanks, no strays, matching
`{0}` placeholders) plus language resolution. It targets the same Windows-qualified TFM as the app (WinUI types like
`ImageSource` require it), so — like the app itself — it only builds and runs on Windows. The
shell / network / registry / Win32 / live-XAML pieces are integration-shaped and are verified by
manual testing (see *Using it*); the reasoning is documented in `docs/design-guidelines-review.md`.

## Using it

- **Left-click** an icon to launch it (Space/Enter when it has keyboard focus works too).
- **Hover** an icon for a Windows 11 taskbar-style highlight.
- **Drag an icon** to reorder it. **Drag the dock's background** (the padding around the
  icons, the divider, or the gear) to move the whole dock; drop it *at* a screen edge to snap and
  auto-hide behind that edge, or anywhere else to float.
- **Right-click an icon** → *Open, Edit…, Rename…, Move left, Move right, Hide, Remove*.
- **Gear button** → opens **Settings** (General + Apps & links + About).
- **Right-click the dock background** → *Add New…*, *Settings…*, *Snap to edge*, *Float (unsnap)*, *Quit*.
- **Tray icon** (notification area) → **left-click** to bring the dock to the front;
  **right-click** for *Show/Hide dock*, *Add new…*, *Settings…*, *Quit Anchor*.
- **Ctrl+Alt+A** (default) from anywhere brings the dock to the front — the quickest way back to a
  dock that's auto-hidden behind an edge. Change or clear it in Settings ▸ General.

Because the dock stays off the taskbar, everything (including **Quit**) lives in the gear/settings,
the right-click menu and the tray icon. When snapped, move the cursor to that edge (within the
dock's span) — or onto the notch — to reveal it, or just press the shortcut.

## Run / debug in VS Code

Open the repo in VS Code and press **F5** (*Launch Anchor*), or run the **build** task
(`Ctrl+Shift+B`). Both are configured in `.vscode/` for the required `x64` platform.

## Configuration

All state is saved to a single JSON file:

```
%AppData%\Anchor\dock.json
```

Delete this file to reset the dock to its seeded defaults. Downloaded favicons are cached
separately under `%AppData%\Anchor\IconCache\` (one `<host>.ico` per site); deleting that folder
just forces the icons to be re-fetched.

The file is written atomically (write-to-temp then rename) so a crash mid-save can't corrupt it.
Enums are stored **by name**, and unset optional fields are omitted, so an older file still loads
after an upgrade (a missing `Theme`, for instance, defaults back to `Dark`).

<details>
<summary>Example <code>dock.json</code> (and field reference)</summary>

```jsonc
{
  "Items": [
    {
      "Id": "8f3c…",             // stable GUID (generated once per item)
      "Kind": "Application",     // Application | File | Folder | WebLink | Separator
      "DisplayName": "Notepad",  // shown in the tooltip / Settings list
      "Target": "C:\\Windows\\System32\\notepad.exe", // path, or URL for a WebLink
      "Arguments": "notes.txt",  // optional, apps only; omitted when empty
      "CustomIconPath": null,    // optional path to an icon that overrides the shell icon
      "Hidden": false            // true = kept in the list but not drawn on the dock
    }
  ],
  "Snapped": false,              // true = flush to Edge; false = floating at FreeX/FreeY
  "Edge": "Bottom",              // Bottom | Top | Left | Right
  "FreeX": 640,                  // last top-left (physical px); also the snap anchor
  "FreeY": 900,
  "AutoHide": true,              // when snapped, slide behind the edge and reveal on hover
  "LaunchAtStartup": false,      // mirrors the per-user HKCU Run key
  "AlwaysOnTop": true,           // keep above other windows while floating
  "Theme": "Dark",               // Light | Dark | System
  "VerticalWhenSideSnapped": false, // "Transpose": vertical icons on the left/right edge
  "Language": "",                // "" = follow Windows; else en|de|es|fr|hi|ja|pt|zh-Hans
  "Hotkey": "Ctrl+Alt+A",        // global shortcut; "" = none. Ctrl/Alt/Shift/Win + a key
  "HotkeyEnabled": true,         // register Hotkey with Windows (off keeps the combination)
  "Seeded": true                 // defaults have been seeded (prevents re-seeding an emptied dock)
}
```

You can hand-edit this file while Anchor is closed. `LaunchAtStartup` is the app's view of the
Windows startup entry; the source of truth is the `Anchor` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, which the Settings toggle writes.

`Hotkey` is stored as readable text (`Ctrl+Alt+A`) rather than key codes, so it stays legible.
Assignable keys are `A`–`Z`, `0`–`9`, `F1`–`F24`, `Space`, `Enter`, `Tab`, `Backspace`, `Insert`,
`Delete`, `Home`, `End`, `PageUp`, `PageDown` and the four arrow keys; at least one of `Ctrl`,
`Alt` or `Win` is required. A value Anchor can't parse is treated as "no shortcut" — it never
stops the config from loading.

</details>

## Privacy & data

Anchor runs entirely on your PC. There are **no accounts, no telemetry, no analytics, and no ads**.

- **Your data stays local.** Items, layout and settings live in `%AppData%\Anchor\dock.json`
  and never leave your device.
- **Web-link icons are the only network access.** For a web link, Anchor fetches the site's
  favicon — first directly from the site (`https://<host>/favicon.ico`), and only if that fails,
  from DuckDuckGo's icon service (`https://icons.duckduckgo.com/ip3/<host>.ico`), which receives
  just the site's domain. Nothing else is sent, and downloaded icons are cached under
  `%AppData%\Anchor\IconCache\` so a site is contacted at most once. If you never add a web link,
  Anchor makes no network requests at all.
- **Launching is a hand-off to Windows.** Opening an item hands it to the shell exactly as
  double-clicking it in Explorer would; Anchor does not read your files' contents.

The same summary is available in-app under **Settings ▸ About ▸ Privacy**.

## Security notes

- **Anchor is a launcher, so it starts programs you pin.** Items are launched through
  `ShellExecute` (`Process.Start` with `UseShellExecute=true`) — the same mechanism as an Explorer
  double-click. It only ever launches what *you* explicitly added (via the Add window or
  drag-and-drop) and persisted to your own user-profile config; it does not run undisclosed code.
  Because the config is a plain file under your `%AppData%`, protect it as you would any file that
  can start programs at sign-in.
- **Input is validated at the edges.** Web links are constrained to `http`/`https` on every path
  that creates one (the Add window and drag-and-drop); arbitrary URIs and shell commands
  (`ms-settings:`, `shell:…`) are only accepted through the explicit *Shortcut* type.
- **Favicon downloads are bounded.** Responses are size-capped (~2 MB) and content-sniffed by
  magic bytes before decoding, so a non-image response can't be shown as a broken/oversized image.
  The cache filename is derived from the sanitized host, not free-form text.
- **No elevation required.** "Start with Windows" uses the per-user `HKCU` Run key, so it never
  prompts for admin rights.

## Troubleshooting

- **Diagnostic log.** Anchor writes a lightweight log to `%Temp%\anchor.log` (truncated on each
  start). It records launches, icon-resolution fallbacks, and any otherwise-unhandled exception —
  the first place to look if something misbehaves.
- **An icon shows a globe/page glyph instead of a picture.** The shell thumbnail or favicon
  couldn't be resolved (e.g. the target no longer exists, or the site has no favicon / you're
  offline). Fix the target via the item's **Edit…** menu, or check connectivity for a web link.
- **The snapped dock seems to have vanished.** It auto-hid behind its edge — move the cursor to
  that screen edge (within the dock's span) or onto the small notch to reveal it. To stop it
  hiding, turn off **Settings ▸ Auto-hide when snapped**.
- **Reset everything.** Close Anchor and delete `%AppData%\Anchor\dock.json`, or use
  **Settings ▸ General ▸ Reset dock to defaults** (which asks for confirmation first).

## Architecture

```
src/Anchor/
  App.xaml(.cs)              App entry point; single-instance guard; creates the single DockWindow.
  DockWindow.xaml(.cs)       The dock window: glass, chrome, layout, items, drag/reorder, menu.
  DockWindow.AutoHide.cs     Auto-hide controller (cursor polling, slide animation, hidden notch).
  DockWindow.Tray.cs         Tray icon + global-shortcut wiring; bring-to-front; language switch.
  SettingsWindow.xaml(.cs)   Windows-Settings-style window: General + Apps & links + About pages.
  AddNewWindow.xaml(.cs)     Windows-app-style modal for adding an app/file/folder/link/shortcut.
  Models/
    DockItem.cs              One dock entry (kind, target, icon, hidden). Serializable.
    DockConfig.cs            Persisted items + settings (theme, language, hotkey, snap edge, …).
    HotkeyGesture.cs         A global shortcut: modifiers + key, parsed from / rendered to text.
  Services/
    AcrylicBackdropManager.cs  Applies + keeps-alive the taskbar-style acrylic.
    IconService.cs             Shell-thumbnail icons for apps/files/folders; cached favicons for links.
    Launcher.cs                ShellExecute-based launching (apps, files, URLs).
    DockStore.cs               JSON load/save of the config (atomic write).
    DockItemFactory.cs         Classifies a target (app/file/folder/link) and suggests a name.
    Loc.cs                     The string table: language resolution + key lookup with fallback.
    MessageWindow.cs           Hidden HWND that receives tray callbacks and WM_HOTKEY.
    TrayIconService.cs         Notification-area icon and its native right-click menu.
    HotkeyService.cs           RegisterHotKey wrapper for the one global shortcut.
    StartupService.cs          Per-user "start with Windows" Run-key toggle.
    WindowChrome.cs            Borderless/topmost/tool-window, rounded corners, dialog sizing.
    Diag.cs                    Lightweight file logger (%Temp%\anchor.log).
  Localization/
    LocalizeExtension.cs       XAML markup extension: Text="{loc:Localize Key=…}".
  Strings/                     One embedded JSON string table per language (en, de, es, fr, …).
  Interop/
    NativeMethods.cs           Win32/DWM P/Invoke (corners, Z-order, DPI, cursor, shell icons,
                               tray icon, popup menus, global hotkeys).
  Assets/Anchor.ico            Win32 app icon (ApplicationIcon), generated from docs/anchor.png.
  Images/                      MSIX tile/logo set (StorePackage=true builds only), same source.
  Package.appxmanifest         MSIX manifest for Store packaging (placeholder identity).
tests/Anchor.Tests/            xUnit unit tests for the pure-logic Models/Services.
scripts/package-release.ps1    Self-contained publish → dist\Anchor-win-x64-<version>.zip + SHA-256.
packaging/winget/manifests/    Staged winget-pkgs manifest templates (see docs/winget-deployment.md).
docs/                          Screenshots, Store-deployment guide, winget guide, design review.
.vscode/                       F5 launch + build tasks, pinned to the required x64 platform.
Anchor.slnx                    Solution (app + tests).
```

### Notes / design decisions

- **Always-on glass.** WinUI normally collapses acrylic to a flat fallback color when its
  window is deactivated. A dock is *never* the foreground window, so `AcrylicBackdropManager`
  drives a `DesktopAcrylicController` with `SystemBackdropConfiguration.IsInputActive = true`
  to keep the glass alive. The tint/luminosity recipe is theme-aware and tunable
  (`Dark`/`Light` properties) to match the taskbar exactly.
- **Sizing.** The window is auto-sized to its content. The strip size is computed
  *analytically* from the (uniform) cell metrics rather than by measuring the live tree —
  this avoids the "shrink window → clip content → lock small" feedback loop and needs no
  manual `Measure()` (which throws on live elements).
- **Master vs. visible items.** `DockConfig.Items` is the ordered source of truth (including
  hidden items); the dock renders a filtered projection. A visible reorder is merged back into
  the master list with hidden items kept anchored at their indices, so hiding/showing never
  loses position.
- **Reorder vs. move gestures.** A press that starts on an icon reorders that icon; a press on
  the background/divider/gear moves the whole window. Both are driven by polling the global
  cursor + button state on a timer (WinUI pointer capture is racy while a window moves under the
  cursor).
- **Snap keeps its place.** The dropped position is remembered even while snapped, and the target
  monitor is resolved from that point (`DisplayArea.GetFromPoint`), so a snapped dock hides where
  you left it on the correct screen instead of re-centering.
- **Single instance.** `App` acquires a session-scoped named mutex (`Local\Anchor.SingleInstance.v1`)
  on launch; a second process finds it already held and exits before creating a window, so the
  "start with Windows" copy and a manual launch never produce two overlapping docks.
- **`Window` has no `Resources`.** WinUI 3's `Window` is not a `FrameworkElement`, so shared XAML
  resources live on the root panel (`<Grid.Resources>`), not `<Window.Resources>` — the latter
  makes the XAML compiler fail with no diagnostic. The new dialog windows use the built-in
  `MicaBackdrop` for their glass.
- **One hidden HWND for shell messages.** The tray icon and `RegisterHotKey` both deliver their
  events as window messages, and a WinUI 3 `Window` exposes no `WndProc`. `MessageWindow` creates
  a single never-shown popup window on the UI thread to receive them (a real popup, not an
  `HWND_MESSAGE` child, because `TrackPopupMenuEx` needs an owner that can be made foreground or
  the tray menu won't light-dismiss). Its `WndProc` delegate is rooted for the window's lifetime
  and swallows handler exceptions, since a throw would unwind through native code.
- **Localization is a plain dictionary, not PRI.** Translations are embedded JSON tables
  (`Strings/<code>.json`) resolved once at startup by `Loc`, with English as the per-key fallback.
  Anchor ships unpackaged, where the `.resw`/PRI `ResourceLoader` is awkward and can't be
  overridden per user — and the language here is an *app* setting, not the Windows display
  language, so "match Windows, or pick your own" has to be a runtime choice. XAML reads it through
  a `{loc:Localize}` markup extension, which resolves at load time; already-loaded windows
  therefore need the restart Settings offers. Test coverage asserts every language defines every
  English key, with matching `{0}` placeholders.
- **Unpackaged & self-contained** so it runs like a normal desktop utility with no MSIX and
  no separate runtime install.

## FAQ

**How do I quit Anchor?** Right-click the tray icon → *Quit Anchor*, right-click the dock
background → *Quit Anchor*, or open Settings from the gear. The dock is intentionally off the
taskbar, so there's no taskbar close button.

**Where did my dock go after I snapped it?** It auto-hides behind the edge. Reveal it by moving
the cursor to that screen edge or onto the notch, by pressing **Ctrl+Alt+A**, or by clicking the
tray icon; disable *Auto-hide when snapped* in Settings to keep it always visible.

**The shortcut doesn't work.** Another app almost certainly holds that combination — Settings
says so when it can't register, and shows the shortcut it tried. Pick a different one, or switch
the shortcut off if you don't want Anchor holding a global binding at all.

**Can I use a language you don't ship?** Not yet, but adding one is a single file: copy
`src/Anchor/Strings/en.json`, translate the values, and add the code to `Loc.Available`. The
tests will tell you if you missed a key.

**Can I run it on multiple monitors?** Yes. A snapped dock hides and reveals on whichever monitor
you dropped it on. On an edge that borders another monitor it stays pinned and visible instead of
sliding into the neighbor.

**Can I use my own icon for an item?** Set `CustomIconPath` for that item in `dock.json` (while
Anchor is closed) to point at an image file; it overrides the shell/favicon icon.

**Does it need admin rights?** No. Everything (including "start with Windows") is per-user.

## Known limitations

- **One global hotkey, one action.** The shortcut only summons the dock; there's no per-item
  binding, and no chorded/sequence shortcuts. Assignable keys are limited to a fixed,
  layout-independent table (see *Configuration*) so the stored text means the same thing on
  every keyboard layout.
- **Language changes need a restart to apply everywhere.** `{loc:Localize}` resolves when XAML
  loads, so the dock strip and any open window keep their old strings until relaunch — Settings
  offers a one-click restart. Translations are machine-assisted and unreviewed by native
  speakers; corrections are welcome (`src/Anchor/Strings/*.json`).
- **No magnification.** Hovering an icon gives the Windows 11 taskbar-style subtle-fill highlight,
  not a macOS-style zoom; the icons never grow beyond the glass strip.
- **Timer-driven animations.** The auto-hide slide uses a `DispatcherQueueTimer` calling
  `AppWindow.Move` per frame rather than a Composition animation; smooth today, a polish item.
- **Web-link icons** fetch the site favicon (site `/favicon.ico`, then a favicon service),
  falling back to a globe glyph when a site has none or there's no connectivity.
- **Custom icons are config-file-only.** A per-item `CustomIconPath` overrides the shell/favicon
  icon, but only by hand-editing `dock.json` — there's no "Change icon…" in the UI.
- **Separators are half-built.** `DockItemKind.Separator` exists in the model (and `Launcher`
  ignores clicks on one), but nothing creates one and the dock has no separator visual — an item of
  that kind renders as an empty cell.
- **x64 only, unsigned, manual updates.** No ARM64/x86 build, no code-signing certificate, and no
  in-app update check — see the roadmap below.

## Roadmap / future enhancements

Nothing here is committed or scheduled; it's the shortlist of what would most improve Anchor next,
roughly in the order the value-to-effort ratio suggests. Comments and PRs on any of it are welcome —
open an issue first for the bigger items.

**Dock & items**

- **Grouping / folders** — club related apps, links or shortcuts into one dock icon that opens a
  fly-out, keeping long docks short. Model-wise a group is a `DockItem` kind that holds child
  items rather than a target.
- **Finish separators, and expose custom icons** — give `DockItemKind.Separator` a thin divider
  visual plus a *Separator* entry in the Add window, and add "Change icon…" to an item's
  right-click menu so `CustomIconPath` stops being a hand-edit.
- **Folder fly-outs** — a folder item lists its contents on hover/click instead of only opening
  Explorer, macOS-stack style.
- **Drop a file onto an app icon** to open it with that app (the dock already accepts drops to
  *add*; this routes the drop to `Launcher` with the path as an argument instead).
- **Running-app indicators** — a dot under icons whose app is running, and click-to-focus the
  existing window rather than starting a second copy.
- **Multiple / per-monitor docks** — more than one strip, each with its own items and edge, instead
  of a single dock that follows you.

**Interaction**

- **Per-item hotkeys** (`Ctrl+Alt+1…9` to launch slot *n*). `HotkeyService` is deliberately
  single-shortcut today — one fixed id, and `Register` replaces the previous gesture — so this means
  keying registrations by id and routing `WM_HOTKEY` on `wParam`.
- **Quick-launch search** — press the hotkey twice (or a second shortcut) for a small filter box
  over the dock's items.
- **Icon-size / density setting** — 24 px cells match the taskbar, but a *Small / Medium / Large*
  option, plus an opacity or accent-tint slider for the acrylic, is the obvious personalization gap.
- **macOS-style magnification** — needs a taller window with a masked backdrop so icons can pop
  above the strip; the current design deliberately stays inside it.
- **Composition-based animations** — replace the per-frame `AppWindow.Move` slide with a
  Composition/implicit animation for a cheaper, smoother auto-hide reveal.

**Platform & distribution**

- **ARM64 build** — add `win-arm64` to the publish matrix so Windows-on-ARM machines run natively
  instead of emulated.
- **Code-signed releases** — a signing certificate removes the SmartScreen warning on first run and
  is a prerequisite for a smooth winget/Store listing.
- **Update check** — an opt-in "check for updates" that reads the GitHub releases feed and links to
  the new zip. Strictly opt-in, so the no-network-by-default promise in *Privacy & data* holds.
- **Store / winget availability** — land the staged manifests under `packaging/winget/` so
  `winget install ajaykontham.Anchor` works, and finish the MSIX/Store submission that
  `-p:StorePackage=true` already builds for (packaging notes live in `docs/`).
- **Import / export** — a one-click backup of `dock.json` (plus the icon cache) to move a dock
  between machines.

**Quality**

- **Live language switching** without the restart prompt — requires re-resolving `{loc:Localize}`
  bindings at runtime instead of at XAML load.
- **Native-speaker review of the eight translations**, which are currently machine-assisted, and
  more languages beyond them (`src/Anchor/Strings/en.json` is the template).
- **UI smoke tests** — the shell/Win32/live-XAML paths are manual-only today; a WinAppDriver or
  Appium harness would cover launch, snap, auto-hide and the tray menu.

## Contributing

Contributions are welcome. A few things that keep the bar consistent:

- Build with `-p:Platform=x64` (WinUI can't be `AnyCPU`) and keep the build **warning-free**.
- Add or update tests under `tests/Anchor.Tests/` for any pure-logic change, and run
  `dotnet test` before opening a PR.
- Match the existing commenting style — explain *why*, not just *what*, especially around the
  Win32/DWM interop and the drag/auto-hide timing.
- UI-affecting changes can't be unit-tested here; smoke-test them on Windows and note what you
  checked (see *Using it*).
- [`docs/design-guidelines-review.md`](docs/design-guidelines-review.md) tracks how the app measures
  up against the Windows 11 / Fluent design guidelines — worth a look before changing the UI.

## Credits & support

Developed by **[ajaykontham](https://github.com/framedparadox)**. Anchor is free and open source; bug
reports, feature requests and translation fixes are all welcome.

- **Source** — <https://github.com/framedparadox/anchor-dock>
- **Issues / feature requests** — <https://github.com/framedparadox/anchor-dock/issues>
- **Diagnostics to attach to a bug report** — `%Temp%\anchor.log` and, if it's about layout or
  items, your `%AppData%\Anchor\dock.json` (it contains the paths you pinned, so redact anything
  you'd rather not share).

The same links are in the app under **Settings ▸ About**.

## License

Released under the [MIT License](LICENSE) — © ajaykontham.
