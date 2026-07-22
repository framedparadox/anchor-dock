# DockGx

A macOS-style dock for Windows 11, built with **WinUI 3 / Windows App SDK**. It floats a
compact, glass "strip" above the taskbar that holds apps, files, folders, and web links —
launch anything with a click.

The glass is the **real Windows 11 acrylic material** (the same `DesktopAcrylicBackdrop`
the shell uses), so it blurs the desktop behind it and follows the system light/dark theme.

![The dock](docs/dock.png)

## Features

- **Glass background** — Windows 11 dark taskbar-style acrylic. Real desktop blur, kept
  translucent even though a dock is never the focused window (see *Architecture*). Pinned to
  dark, with a dark window border so there's no light/white outline.
- **Rounded corners** — the native Windows 11 window rounding (DWM).
- **Taskbar-sized icons** — 24 px icons in 40 px cells, matching the Windows 11 taskbar.
- **Divider + settings button** — a gear button (and right-click anywhere) opens the dock menu.
- **Add anything** — apps (`.exe`), shortcuts (`.lnk`), files, folders, and web links, via the
  menu. Icons come from the Windows shell (the same icons Explorer shows).
- **Draggable** — grab the dock's background and drag it around the screen (icons stay clickable).
- **Snap to any edge → hides behind it** — drop the dock near a screen edge and it snaps flush
  and auto-hides behind that edge (bottom / top / left / right), revealing on cursor approach.
- **Per-item menu** — right-click an icon: Open, Edit, Rename, Move left/right, Remove.
- **Persistent** — items, position, and snap state are saved to JSON and restored next launch.
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

## Using it

- **Left-click** an icon to launch it.
- **Hover** an icon for a Windows 11 taskbar-style highlight.
- **Drag the dock** from its background (the padding/divider — *not* the icons) to move it.
  Drop it near a screen edge to snap + auto-hide behind that edge; drop it elsewhere to float.
- **Right-click an icon** → *Open, Edit…, Rename…, Move left, Move right, Remove*.
- **Gear button** (or **right-click the dock background**) → add an App / File / Folder /
  Web Link, choose *Snap to edge*, *Float (unsnap)*, or *Quit*.

Because the dock stays off the taskbar, everything (including **Quit**) lives in that menu.
When snapped, move the cursor to that edge (within the dock's span) to reveal it.

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
  DockWindow.xaml(.cs)       The dock window: glass, chrome, layout, items, menu.
  DockWindow.AutoHide.cs     Auto-hide controller (cursor polling + slide animation).
  Models/
    DockItem.cs              One dock entry (kind, target, icon). Serializable.
    DockConfig.cs            Persisted items + settings (auto-hide, edge, icon size).
  Services/
    AcrylicBackdropManager.cs  Applies + keeps-alive the taskbar-style acrylic.
    IconService.cs             Shell-thumbnail icons for apps/files/folders.
    Launcher.cs                ShellExecute-based launching (apps, files, URLs).
    DockStore.cs               JSON load/save of the config.
    WindowChrome.cs            Borderless/topmost/tool-window + rounded corners.
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
- **Unpackaged & self-contained** so it runs like a normal desktop utility with no MSIX and
  no separate runtime install.

## Known limitations / future work

- **Horizontal on every edge.** Snapping works on all four edges, but the dock keeps its
  horizontal layout even on left/right. Rotating to a vertical layout there is the natural
  next step.
- **Magnification is subtle** (stays within the glass strip). True macOS "pop above the dock"
  magnification needs a taller window with a masked backdrop — a good future enhancement.
- **No tray icon yet** — management is via the gear button / right-click menu.
- **Web-link icons** use a globe glyph (no favicon fetching yet).
