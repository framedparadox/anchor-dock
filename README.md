# DockGx

A floating dock for Windows 11, built with **WinUI 3 / Windows App SDK**. It floats a
compact, glass "strip" above the taskbar that holds apps, files, folders, and web links —
launch anything with a click.

The glass is the **real Windows 11 acrylic material** (the same `DesktopAcrylicBackdrop`
the shell uses), so it blurs the desktop behind it. Pick a **light**, **dark** (default) or
**system-following** theme from Settings.

![The dock](docs/dock.png)

---

## Table of contents

- [Features](#features)
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
- [Known limitations / future work](#known-limitations--future-work)
- [Publishing](#publishing)
- [Contributing](#contributing)
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
- **Settings window** — a gear button opens a Windows-Settings-style window (Mica, left
  navigation) with a **General** page (theme, position, transpose-on-side-edges, auto-hide,
  always-on-top, start-with-Windows, reset), an **Apps & links** page that lists every entry with
  a show/hide switch and a remove button, and an **About** page (version + a plain-language
  privacy summary).
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
- **Single instance** — launching DockGx a second time (e.g. from the Start menu while the
  "start with Windows" copy is already running) quietly exits rather than stacking a second dock.
- **Persistent** — items, position, snap state and settings are saved to JSON and restored next launch.
- **Out of the way** — borderless, always-on-top, hidden from the taskbar and Alt-Tab.

## Requirements

- Windows 11 (tested on build 26200) — Windows 10 1809+ (build 17763) should also work; that is
  the app's `TargetPlatformMinVersion`.
- .NET 10 SDK.
- The build is **self-contained**, so the Windows App Runtime does **not** need to be installed
  separately.

## Build & run

```powershell
# from the repo root
dotnet build src/DockGx/DockGx.csproj -c Release -p:Platform=x64

# run it
./src/DockGx/bin/x64/Release/net10.0-windows10.0.19041.0/win-x64/DockGx.exe
```

> WinUI apps cannot target `AnyCPU`; always pass `-p:Platform=x64` (this machine's architecture)
> for a single project. The whole solution builds with `dotnet build DockGx.slnx -c Release`
> (both projects are x64-only, so x64 is the default — don't add `-p:Platform=x64` at the
> solution level, which trips MSBuild's solution-configuration mapping).

## Tests

```powershell
dotnet test tests/DockGx.Tests/DockGx.Tests.csproj -p:Platform=x64
```

`tests/DockGx.Tests/` covers the pure-logic pieces (`Models/`, `Services/DockItemFactory.cs`)
with xUnit — the `Kind → Glyph` mapping and property-change notifications on `DockItem`, the
first-run defaults and JSON round-trip of `DockConfig`, and `DockItemFactory` classification /
name suggestion. It targets the same Windows-qualified TFM as the app (WinUI types like
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

Because the dock stays off the taskbar, everything (including **Quit**) lives in the gear/settings
and the right-click menu. When snapped, move the cursor to that edge (within the dock's span) — or
onto the notch — to reveal it.

## Run / debug in VS Code

Open the repo in VS Code and press **F5** (*Launch DockGx*), or run the **build** task
(`Ctrl+Shift+B`). Both are configured in `.vscode/` for the required `x64` platform.

## Configuration

All state is saved to a single JSON file:

```
%AppData%\DockGx\dock.json
```

Delete this file to reset the dock to its seeded defaults. Downloaded favicons are cached
separately under `%AppData%\DockGx\IconCache\` (one `<host>.ico` per site); deleting that folder
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
  "Seeded": true                 // defaults have been seeded (prevents re-seeding an emptied dock)
}
```

You can hand-edit this file while DockGx is closed. `LaunchAtStartup` is the app's view of the
Windows startup entry; the source of truth is the `DockGx` value under
`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, which the Settings toggle writes.

</details>

## Privacy & data

DockGx runs entirely on your PC. There are **no accounts, no telemetry, no analytics, and no ads**.

- **Your data stays local.** Items, layout and settings live in `%AppData%\DockGx\dock.json`
  and never leave your device.
- **Web-link icons are the only network access.** For a web link, DockGx fetches the site's
  favicon — first directly from the site (`https://<host>/favicon.ico`), and only if that fails,
  from DuckDuckGo's icon service (`https://icons.duckduckgo.com/ip3/<host>.ico`), which receives
  just the site's domain. Nothing else is sent, and downloaded icons are cached under
  `%AppData%\DockGx\IconCache\` so a site is contacted at most once. If you never add a web link,
  DockGx makes no network requests at all.
- **Launching is a hand-off to Windows.** Opening an item hands it to the shell exactly as
  double-clicking it in Explorer would; DockGx does not read your files' contents.

The same summary is available in-app under **Settings ▸ About ▸ Privacy**.

## Security notes

- **DockGx is a launcher, so it starts programs you pin.** Items are launched through
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

- **Diagnostic log.** DockGx writes a lightweight log to `%Temp%\dockgx.log` (truncated on each
  start). It records launches, icon-resolution fallbacks, and any otherwise-unhandled exception —
  the first place to look if something misbehaves.
- **An icon shows a globe/page glyph instead of a picture.** The shell thumbnail or favicon
  couldn't be resolved (e.g. the target no longer exists, or the site has no favicon / you're
  offline). Fix the target via the item's **Edit…** menu, or check connectivity for a web link.
- **The snapped dock seems to have vanished.** It auto-hid behind its edge — move the cursor to
  that screen edge (within the dock's span) or onto the small notch to reveal it. To stop it
  hiding, turn off **Settings ▸ Auto-hide when snapped**.
- **Reset everything.** Close DockGx and delete `%AppData%\DockGx\dock.json`, or use
  **Settings ▸ General ▸ Reset dock to defaults** (which asks for confirmation first).

## Architecture

```
src/DockGx/
  App.xaml(.cs)              App entry point; single-instance guard; creates the single DockWindow.
  DockWindow.xaml(.cs)       The dock window: glass, chrome, layout, items, drag/reorder, menu.
  DockWindow.AutoHide.cs     Auto-hide controller (cursor polling, slide animation, hidden notch).
  SettingsWindow.xaml(.cs)   Windows-Settings-style window: General + Apps & links + About pages.
  AddNewWindow.xaml(.cs)     Windows-app-style modal for adding an app/file/folder/link/shortcut.
  Models/
    DockItem.cs              One dock entry (kind, target, icon, hidden). Serializable.
    DockConfig.cs            Persisted items + settings (theme, snap edge, placement, auto-hide, startup).
  Services/
    AcrylicBackdropManager.cs  Applies + keeps-alive the taskbar-style acrylic.
    IconService.cs             Shell-thumbnail icons for apps/files/folders; cached favicons for links.
    Launcher.cs                ShellExecute-based launching (apps, files, URLs).
    DockStore.cs               JSON load/save of the config (atomic write).
    DockItemFactory.cs         Classifies a target (app/file/folder/link) and suggests a name.
    StartupService.cs          Per-user "start with Windows" Run-key toggle.
    WindowChrome.cs            Borderless/topmost/tool-window, rounded corners, dialog sizing.
    Diag.cs                    Lightweight file logger (%Temp%\dockgx.log).
  Interop/
    NativeMethods.cs           Win32/DWM P/Invoke (corners, Z-order, DPI, cursor, shell icons).
tests/DockGx.Tests/            xUnit unit tests for the pure-logic Models/Services.
docs/                          Screenshot, Store-deployment guide, design-guidelines review.
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
- **Single instance.** `App` acquires a session-scoped named mutex (`Local\DockGx.SingleInstance.v1`)
  on launch; a second process finds it already held and exits before creating a window, so the
  "start with Windows" copy and a manual launch never produce two overlapping docks.
- **`Window` has no `Resources`.** WinUI 3's `Window` is not a `FrameworkElement`, so shared XAML
  resources live on the root panel (`<Grid.Resources>`), not `<Window.Resources>` — the latter
  makes the XAML compiler fail with no diagnostic. The new dialog windows use the built-in
  `MicaBackdrop` for their glass.
- **Unpackaged & self-contained** so it runs like a normal desktop utility with no MSIX and
  no separate runtime install.

## FAQ

**How do I quit DockGx?** Right-click the dock background → *Quit DockGx*, or open Settings from
the gear. The dock is intentionally off the taskbar, so there's no taskbar close button.

**Where did my dock go after I snapped it?** It auto-hides behind the edge. Reveal it by moving
the cursor to that screen edge or onto the notch; disable *Auto-hide when snapped* in Settings to
keep it always visible.

**Can I run it on multiple monitors?** Yes. A snapped dock hides and reveals on whichever monitor
you dropped it on. On an edge that borders another monitor it stays pinned and visible instead of
sliding into the neighbor.

**Can I use my own icon for an item?** Set `CustomIconPath` for that item in `dock.json` (while
DockGx is closed) to point at an image file; it overrides the shell/favicon icon.

**Does it need admin rights?** No. Everything (including "start with Windows") is per-user.

**Why is there no app icon on the .exe yet?** That's the one remaining shipping gap — see
*Known limitations* and the Store guide.

## Known limitations / future work

- **No app icon yet.** The executable has no `<ApplicationIcon>` and the Store package needs a
  full logo set — both require an actual `.ico` / source image (visual-design work, not a code
  change). This is the last blocker for a polished shipping/Store build; see
  `docs/microsoft-store-deployment.md` and recommendation #13 in
  `docs/design-guidelines-review.md`.
- **No global hotkey.** Once focused, the dock is fully keyboard-operable, but a keyboard-only
  user still needs a mouse to *move focus onto it* the first time. A registered global hotkey is
  the natural fix (design-review recommendation #10).
- **No tray icon.** Management is via the gear button (Settings) / right-click menu.
- **Magnification is subtle** (stays within the glass strip). True macOS "pop above the dock"
  magnification needs a taller window with a masked backdrop — a good future enhancement.
- **Timer-driven animations.** The auto-hide slide uses a `DispatcherQueueTimer` calling
  `AppWindow.Move` per frame rather than a Composition animation; smooth today, a polish item.
- **Web-link icons** fetch the site favicon (site `/favicon.ico`, then a favicon service),
  falling back to a globe glyph when a site has none or there's no connectivity.

## Publishing

- **Microsoft Store** — see [`docs/microsoft-store-deployment.md`](docs/microsoft-store-deployment.md)
  for an end-to-end guide (MSIX packaging, the manifest, restricted-capability justifications for
  the launcher/icon behavior, Partner Center submission, and a no-repackaging EXE alternative).
  In short: the app is deliberately **unpackaged** (`WindowsPackageType=None`) today, so a Store
  submission means adding MSIX packaging + visual assets, and declaring/justifying `runFullTrust`
  (it launches programs) and `broadFileSystemAccess` (it reads shell icons from arbitrary paths).
- **Design review** — [`docs/design-guidelines-review.md`](docs/design-guidelines-review.md)
  tracks how the app measures up to the Windows 11 / Fluent design guidelines.

## Contributing

Contributions are welcome. A few things that keep the bar consistent:

- Build with `-p:Platform=x64` (WinUI can't be `AnyCPU`) and keep the build **warning-free**.
- Add or update tests under `tests/DockGx.Tests/` for any pure-logic change, and run
  `dotnet test` before opening a PR.
- Match the existing commenting style — explain *why*, not just *what*, especially around the
  Win32/DWM interop and the drag/auto-hide timing.
- UI-affecting changes can't be unit-tested here; smoke-test them on Windows and note what you
  checked (see *Using it*).

## License

Released under the [MIT License](LICENSE).
