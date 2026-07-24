# DockGx

A floating dock for Windows 11, built with **WinUI 3 / Windows App SDK**. It floats a
compact, glass "strip" above the taskbar that holds apps, files, folders, and web links —
launch anything with a click.

The glass is the **real Windows 11 acrylic material** (the same `DesktopAcrylicBackdrop`
the shell uses), so it blurs the desktop behind it. Pick a **light**, **dark** (default) or
**system-following** theme from Settings.

![The dock](docs/dock.png)

## Features

- **Glass background** — Windows 11 taskbar-style acrylic. Real desktop blur, kept
  translucent even though a dock is never the focused window (see *Architecture*), with a
  matching window border so there's no light/white outline.
- **Theme** — choose **Light**, **Dark** (default) or **System** (follow the Windows setting)
  from Settings ▸ General. The dock, its glass and the Settings/Add windows all switch together;
  a High Contrast accessibility theme always overrides it.
- **Rounded corners** — the native Windows 11 window rounding (DWM).
- **Taskbar-sized icons** — 24 px icons in 40 px cells, matching the Windows 11 taskbar.
- **Settings window** — a gear button opens a Windows-Settings-style window (Mica, left
  navigation) with a **General** page (theme, position, auto-hide, start-with-Windows, reset/quit) and an
  **Apps & links** page that lists every entry with a show/hide switch and a remove button.
- **Add-to-Dock window** — a Windows-app-style modal for adding an **app / file / folder / web
  link / shortcut**, with a type picker, Browse, and auto-suggested names.
- **Automatic icons** — apps, files and folders use the Windows shell icon (the same icon
  Explorer shows); web links auto-fetch the site's **favicon**, cached to disk so it downloads
  once and still shows offline.
- **Drag & drop to add** — drag an app, shortcut (`.lnk`), file or folder from Explorer or the
  desktop straight onto the dock to add it; a URL dragged from a browser becomes a web link.
- **Reorder by dragging** — drag an icon left/right to rearrange it; dragging the dock's
  background moves the whole dock (the two gestures never conflict).
- **Snap to any edge → hides where you left it** — drop the dock near a screen edge and it snaps
  flush and auto-hides behind *that* edge on *that* monitor, revealing on cursor approach. It
  tucks behind the physical screen edge — at the bottom that means **behind the taskbar**, with
  only a small **notch** peeking over it — stays at the position you placed it along the edge (no
  jumping to center), and reveals when you reach the edge (or the notch). On an edge **shared with
  another monitor** it stays pinned and visible instead of sliding into the neighboring screen.
- **Always on top (optional)** — the dock stays above other windows; a Settings toggle lets you
  turn that off while it's floating (when snapped it stays on top so the auto-hide reveal works).
- **Show / hide without deleting** — hide items from the dock (Settings ▸ Apps, or an icon's
  right-click menu) while keeping them in the list.
- **Empty state** — with no items the dock shows a **“＋ Add New”** button.
- **Per-item menu** — right-click an icon: Open, Edit, Rename, Move left/right, Hide, Remove.
- **Persistent** — items, position, snap state and settings are saved to JSON and restored next launch.
- **Out of the way** — borderless, always-on-top, hidden from the taskbar and Alt-Tab.

## Requirements

- Windows 11 (tested on build 26200) — Windows 10 1809+ should also work.
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

> WinUI apps cannot target `AnyCPU`; always pass `-p:Platform=x64` (this machine's architecture).

## Tests

```powershell
dotnet test tests/DockGx.Tests/DockGx.Tests.csproj -p:Platform=x64
```

`tests/DockGx.Tests/` covers the pure-logic pieces (`Models/`, `Services/DockItemFactory.cs`)
with xUnit. It targets the same Windows-qualified TFM as the app (WinUI types like `ImageSource`
require it), so — like the app itself — it only builds and runs on Windows.

## Using it

- **Left-click** an icon to launch it.
- **Hover** an icon for a Windows 11 taskbar-style highlight.
- **Drag an icon** left/right to reorder it. **Drag the dock's background** (the padding around the
  icons, the divider, or the gear) to move the whole dock; drop it *at* a screen edge to snap and
  auto-hide behind that edge, or anywhere else to float.
- **Right-click an icon** → *Open, Edit…, Rename…, Move left, Move right, Hide, Remove*.
- **Gear button** → opens **Settings** (General + Apps & links).
- **Right-click the dock background** → *Add New…*, *Settings…*, *Snap to edge*, *Float (unsnap)*, *Quit*.

Because the dock stays off the taskbar, everything (including **Quit**) lives in the gear/settings
and the right-click menu. When snapped, move the cursor to that edge (within the dock's span) — or
onto the notch — to reveal it.

## Run / debug in VS Code

Open the repo in VS Code and press **F5** (*Launch DockGx*), or run the **build** task
(`Ctrl+Shift+B`). Both are configured in `.vscode/` for the required `x64` platform.

## Where settings live

```
%AppData%\DockGx\dock.json
```

Delete this file to reset the dock to its seeded defaults.

## Architecture

```
src/DockGx/
  App.xaml(.cs)              App entry point; creates the single DockWindow.
  DockWindow.xaml(.cs)       The dock window: glass, chrome, layout, items, drag/reorder, menu.
  DockWindow.AutoHide.cs     Auto-hide controller (cursor polling, slide animation, hidden notch).
  SettingsWindow.xaml(.cs)   Windows-Settings-style window: General + Apps & links pages.
  AddNewWindow.xaml(.cs)     Windows-app-style modal for adding an app/file/folder/link/shortcut.
  Models/
    DockItem.cs              One dock entry (kind, target, icon, hidden). Serializable.
    DockConfig.cs            Persisted items + settings (theme, snap edge, placement, auto-hide, startup).
  Services/
    AcrylicBackdropManager.cs  Applies + keeps-alive the taskbar-style acrylic.
    IconService.cs             Shell-thumbnail icons for apps/files/folders; cached favicons for links.
    Launcher.cs                ShellExecute-based launching (apps, files, URLs).
    DockStore.cs               JSON load/save of the config.
    DockItemFactory.cs         Classifies a target (app/file/folder/link) and suggests a name.
    StartupService.cs          Per-user "start with Windows" Run-key toggle.
    WindowChrome.cs            Borderless/topmost/tool-window, rounded corners, dialog sizing.
    Diag.cs                    Lightweight file logger (%Temp%\dockgx.log).
  Interop/
    NativeMethods.cs           Win32/DWM P/Invoke (corners, Z-order, DPI, cursor).
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
- **`Window` has no `Resources`.** WinUI 3's `Window` is not a `FrameworkElement`, so shared XAML
  resources live on the root panel (`<Grid.Resources>`), not `<Window.Resources>` — the latter
  makes the XAML compiler fail with no diagnostic. The new dialog windows use the built-in
  `MicaBackdrop` for their glass.
- **Unpackaged & self-contained** so it runs like a normal desktop utility with no MSIX and
  no separate runtime install.

## Known limitations / future work

- **Horizontal on every edge.** Snapping works on all four edges, but the dock keeps its
  horizontal layout even on left/right. Rotating to a vertical layout there is the natural
  next step.
- **Magnification is subtle** (stays within the glass strip). True macOS "pop above the dock"
  magnification needs a taller window with a masked backdrop — a good future enhancement.
- **No tray icon yet** — management is via the gear button (Settings) / right-click menu.
- **Web-link icons** fetch the site favicon (site `/favicon.ico`, then a favicon service),
  falling back to a globe glyph when a site has none or there's no connectivity.

## Publishing

- **Microsoft Store** — see [`docs/microsoft-store-deployment.md`](docs/microsoft-store-deployment.md)
  for an end-to-end guide (MSIX packaging, the manifest, restricted-capability justifications for
  the launcher/icon behavior, Partner Center submission, and a no-repackaging EXE alternative).
- **Design review** — [`docs/design-guidelines-review.md`](docs/design-guidelines-review.md)
  tracks how the app measures up to the Windows 11 / Fluent design guidelines.
