# Changelog

All notable changes to Anchor are documented here. Format loosely follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); dates are when the change was made,
not necessarily when it shipped in a release.

## [Unreleased]

### Round 6 — multi-monitor auto-hide, and a hover race with it

#### Fixed

- **A dock snapped to an edge shared with another monitor never auto-hid at all** — it stayed
  pinned flush and visible, because sliding it off that edge the way a true outer edge does would
  have carried it onto the neighboring monitor's screen. It now hides there too: instead of
  translating the full-size window off-screen, `DockWindow.SetRevealed` (via `HiddenLeadExtent`)
  shrinks the window in place, down to the notch, flush against the true screen edge — so the
  notch stays on this monitor and nothing ever crosses onto the neighbor's. That transition is a
  snap rather than a slide (animating a live resize would relayout the strip's content on every
  tick); the true-outer-edge case keeps its original eased slide unchanged.
- **Hovering a group icon could pop its fly-out open before an auto-hidden, edge-snapped dock had
  even shown itself**, and once that happened later hovers stopped reliably revealing the dock at
  all. A hidden dock still carries its full strip inside the window — only a sliver of it is left
  on screen — so a stray pointer hit on that sliver could land on a group icon and open its bar
  while the dock underneath was still off-screen. `DockWindow.TrackGroupHover` now holds off
  opening a group on hover, while snapped, until the dock itself is actually revealed; a floating
  dock is unaffected, since it's always revealed.

### Round 5 — a group's fly-out stops flickering, and hover becomes a setting

#### Fixed

- **A hovered group's fly-out flickered open and shut** for as long as the cursor rested on the
  icon. Opening the bar makes the strip fire `PointerExited` — the fly-out's light-dismiss layer
  takes the pointer for a moment as the bar comes up — with the cursor still squarely on the icon,
  and the close that fired on that report was undone by the next pointer move, over and over. The
  bar now stays up on the answer to "where is the cursor actually?", polled against the icon's own
  screen rectangle (`DockWindow.CursorIsOverGroupTrigger`) rather than on a pointer event that
  cannot be trusted at exactly this moment. The dock is also named as the bar's
  `OverlayInputPassThroughElement`, so the strip keeps seeing the cursor underneath an open bar
  instead of being cut off from it.
- **A second click on a group re-opened the bar it had just closed.** The press that lands on the
  icon light-dismisses the bar on its way through, so the click handler found nothing left to
  toggle and opened it again; and in hover mode the cursor was still on the icon afterwards, ready
  to re-open it in any case. A close counts as belonging to the click that caused it, and hover is
  held off that icon until the cursor leaves it — so first click shows the bar, second click hides
  it, whichever mode is on.

#### Added

- **Settings ▸ Appearance ▸ *Open groups on hover***, a toggle alongside *Magnify on hover*. On
  by default, which is how groups already behaved; off ignores the cursor entirely and leaves a
  click to both open and close the bar. Stored as `GroupOpenOnHover` in `dock.json`.

### Round 4 — group fly-outs open on hover

#### Added

- **A group's fly-out now opens as soon as the cursor lands on its icon**, the same bar a click
  already opened — closing the gap called out in Round 1's known limits. It rides the same
  per-move tracking that drives the hover highlight and the fast tooltip
  (`DockWindow.TrackStripPointer`), so it needs no dwell timer or separate pointer subscription.
  Landing on a *different* group while one is already open swaps straight to it — closing the
  first bar and opening the second — the way a menu bar swaps top-level menus under the cursor.
  Re-hovering (or clicking) the group already showing is a no-op rather than a restart of the same
  content. A folder fly-out is unaffected and still opens on click only.

### Round 3 — Microsoft Store readiness

A compliance and correctness pass over the MSIX/Store path. Two of these are behavior bugs that
only appear once the app is installed from the Store, which is exactly where they'd have been
found by a user rather than by us.

#### Fixed

- **"Start with Windows" silently did nothing in a packaged build.** `StartupService` wrote the
  per-user `HKCU\…\CurrentVersion\Run` key, but a packaged process's writes under `HKCU\Software`
  are captured in the package's virtualized registry hive, which Windows' autostart never reads —
  the switch would flip, save, and then not start anything after a reboot. The manifest now
  declares a `windows.startupTask` extension and the service drives it through
  `StartupTask.RequestEnableAsync` when packaged, keeping the Run key for the portable zip.
  `Services/PackagedRuntime.cs` is the single place that tells the two apart.
- **A user's own "off" is now respected.** Windows refuses to let an app re-enable a startup task
  the user disabled in Task Manager. `SetLaunchAtStartupAsync` returns what the entry *actually*
  became rather than what was asked for, and the Settings switch springs back with a note pointing
  at Task Manager instead of claiming an autostart that won't happen.
- **`-p:StorePackage=true` failed after building the package.** Generating the symbol package needs
  `mspdbcmf.exe` (a Visual Studio C++ tool); when it is absent the MSIX targets build the `.msix`
  and *then* fail the build with `MSB6011`. `AppxSymbolPackageEnabled` now defaults to false —
  symbols are optional for a Partner Center upload — and can be turned back on with
  `-p:AppxSymbolPackageEnabled=true`. The dead `WinAppSdkCheckForPdbConversion` property, which
  this version of the targets never read, is gone.
- **The shipped manifest claimed `MaxVersionTested="10.0.19041.0"`** regardless of what
  `Package.appxmanifest` said: the packaging targets overwrite it from `$(TargetPlatformVersion)`.
  The target framework moved to `net10.0-windows10.0.26100.0` so the declared value matches the
  Windows the app is actually tested on. `TargetPlatformMinVersion` is unchanged at 10.0.17763.0,
  so the install floor (Windows 10 1809) is the same.

#### Changed

- **The GitHub update check is absent from the Store build**, not merely off by default. The Store
  updates a packaged app itself, so the banner would have sent users to install a second,
  unmanaged copy of the app they already had — and a Store listing that routes users to a build
  distributed elsewhere is a pattern review looks for. `DockManager.UpdateChecksSupported` gates
  the startup check and collapses the whole Settings card. The portable zip is unaffected; it has
  no other updater.
- **Manifest metadata lines up with the Store listing.** The Start-menu/app-list name is now
  "Anchor Dock", matching `Properties/DisplayName` and the reserved name, and the description no
  longer says "for Windows 11" while `MinVersion` admits Windows 10 1809 — both are policy 10.1.1
  ("metadata must accurately describe the product"). The app's own UI still calls itself Anchor.

#### Added

- **A privacy policy** — [`docs/privacy-policy.md`](docs/privacy-policy.md), linked from
  **Settings ▸ About** and destined for the Partner Center listing field. Store policy 10.5.1
  requires one from "Desktop Bridge and Win32 products" specifically, whatever the app collects
  (here: nothing).
- Tests for the Run-key round trip (including that disabling *removes* the value rather than
  blanking it, and that what's written is a quoted absolute path Windows can run) and for the
  packaged/unpackaged split. The packaged half needs an installed MSIX and stays a manual sideload
  check in `docs/microsoft-store-deployment.md` §11.

### Round 2 — the rest of the roadmap

Everything in this section clears the remaining "Roadmap / future enhancements" items from the
README, plus a redesign of the group fly-out. Built on top of the round-1 work below.

#### Added

- **Fly-out bars.** A group no longer opens a boxed menu: it opens a **second dock** out of the
  clicked icon — the same cells, chrome, spacing and corner radius as the strip itself, running
  across the dock's flow (a vertical bar out of a horizontal dock, and vice versa) and always away
  from the snapped edge. Built once in `DockWindow.FlyoutBar.cs` and shared by groups and folder
  stacks, so the two can't drift apart. It keeps the platform `FlyoutPresenter` as its surface —
  restyled to the strip's chrome — because that already carries the Windows 11 material, shadow
  and light-dismiss behavior.
- **Folder fly-outs (macOS-style stacks).** *Show folder contents* on a folder's right-click menu
  makes it list what's inside it in a fly-out bar instead of handing the folder to Explorer.
  Clicking a subfolder drills in; the last cell is always *Open in File Explorer*; a folder entry's
  own menu offers *Pin to dock*. Hidden and system entries are skipped, the list is capped at 60,
  and entries are built fresh on every open — they're a view of the disk, never persisted.
- **Drag an item into a group.** Dragging an icon over the middle of a group swells that group and
  files the item into it on release; the outer thirds still reorder past it. Dragging a cell **out**
  of an open group bar (sideways, across the bar) puts it back on the strip.
- **Drop a file onto an app icon** to open it with that app, and onto a **group** to file it in
  there. The drop cue is the target icon swelling; the drag caption says which of the two will
  happen. Everything else on the strip still adds the dropped item to the dock.
- **Move an item between docks** — *Move to dock ▸* on an item's menu hands the `DockItem` itself
  over, so its custom icon, arguments and shortcut travel with it.
- **Per-item shortcuts.** Any item can be given its own system-wide combination from
  *Shortcut ▸* on its right-click menu, including one inside a group. `HotkeyService` now keys
  registrations by id and routes `WM_HOTKEY` on `wParam`, so the summon shortcut, search and every
  item share one service. Off by default behind a master switch (Settings ▸ Shortcuts) — each one
  claims a combination from every other app — and the assigned set is listed there.
- **Quick-launch search.** A borderless acrylic card, opened by its own shortcut (unassigned by
  default) or from the tray, filtering every item on every dock. Ranks name-prefix over
  name-substring over target, arrow keys wrap, Enter launches, Esc or losing focus dismisses.
- **Icon size / density.** *Small / Medium / Large* in Settings ▸ Appearance. All dock geometry
  now derives from `DockMetrics`, so the cells, icons, glyphs, separators, running dots, the gear
  and the window sizing all move together and can't drift.
- **Glass personalization** — a frostiness slider (30–100%) and an **accent tint** switch that
  colors the acrylic with the Windows accent instead of the taskbar's neutral grey.
- **Magnification.** Icons swell under the cursor on a cosine curve, tapering over two cells
  either side. The swell happens *inside* each cell — see *Known limits* below.
- **Import / export.** Settings ▸ General writes the whole configuration to a file and restores
  one, wholesale and behind a confirmation. The exported file is the same shape as `dock.json`, so
  a backup can also be dropped into `%AppData%\Anchor` by hand.
- **Opt-in update check.** Asks the GitHub releases API whether a newer Anchor exists, and offers
  the release page or "skip this version". **Off by default**, checked at startup only when it is
  on, silent on any failure, and it never downloads or installs anything. A startup check puts
  nothing on screen — the result surfaces through a tray-menu entry and the Settings banner.
- **Code signing in `package-release.ps1`** — `-CertificateThumbprint` (plus `-TimestampUrl` and
  `-SignToolPath`) signs `Anchor.exe` before the zip is built, so the published SHA256 is the hash
  of the signed archive. Releases still ship unsigned; there is no certificate yet.
- **CI workflow** (`.github/workflows/ci.yml`) — builds x64 and ARM64 warning-free and runs the
  unit tests on every push and PR. The UI smoke tests are a separate, opt-in job pinned to a
  self-hosted `windows-desktop` runner, since UI Automation needs an interactive session.
- **Settings ▸ Appearance** and **Settings ▸ Shortcuts** pages, and a tray-menu *Search…* entry.

#### Changed

- **Language changes apply immediately** — the "restart Anchor to finish switching language"
  prompt is gone. Every window is rebuilt in place against the same `DockProfile`s, so items,
  positions and snap state are untouched. `App.Restart` had no other caller and was removed.
- **The auto-hide slide is time-based.** It was a per-frame "move 28% of what's left", which made
  the duration depend on how far the dock had to travel and ended on a snap; it is now a fixed
  220 ms cubic ease-out at ~120 Hz that lands exactly on target.
- **The Settings shortcut capture** moved into a reusable `HotkeyCaptureButton` control, shared by
  the summon shortcut, the search shortcut and the per-item ones. Each announces what it is a
  shortcut *for*, since the visible content is only the combination.
- `DockManager` gained `LaunchOrFocus` and `LabelFor`, so the strip, the fly-out bars, search and
  the shortcuts all resolve "open this item" and "what is this dock called" the same way.
- `DockItem.CellExtentDefault` / `SeparatorExtent` constants are gone; geometry lives on
  `DockMetrics` and items re-read it via `RefreshMetrics()`.

#### Review pass

A pass back over the above, mostly aimed at logic that was correct but untestable.

- **Pure logic moved out of the window classes**, which a unit test can't construct, and covered:
  `Services/ItemSearch.cs` (quick-launch ranking and filtering), `Services/FolderListing.cs` (a
  folder stack's contents and their order), `Launcher.BuildArguments` (the command line built when
  files are dropped on an app) and `DockMetrics.MagnificationAt` (the swell curve). That is 43 new
  tests over behaviour that previously had none — the argument quoting in particular, where a
  dropped path containing a space arriving as two arguments would look like the app was broken
  rather than Anchor.
- **Fixed: dragging across two adjacent icons could leave the drop cue on the wrong one.** The new
  cell's `DragOver` fires before the old cell's `DragLeave`, so the unconditional clear in
  `DragLeave` wiped the swell off the cell the cursor had just arrived at. It now only clears the
  cue if that cell is still the one showing it.
- **Fixed: `BuildArguments` drops blank paths** (a dragged storage item can have no filesystem
  path) and strips any quote inside a path, so a stray one can't close its own quoting and split
  one argument into several.
- **Fixed: the drag-out watch timer** is now stopped when the dock closes and when a second press
  starts, rather than only on the next mouse-release.
- **Settings ▸ Apps rows now show an item's shortcut** and whether a folder opens a stack. A
  per-item shortcut is assigned from an icon's menu on the dock, so without this an item could
  own a system-wide combination that this page said nothing about.
- **The dock's own right-click menu gained *Search…*.** Quick-launch has no shortcut until the
  user assigns one, and the tray menu alone left it undiscoverable from the dock itself.

#### Known limits in this round

- **Magnification stays inside the strip.** The roadmap asked for icons to pop *above* the dock.
  That needs a window taller than the strip with a masked backdrop, and the dock's glass is a real
  `DesktopAcrylicBackdrop` that paints the whole window and cannot be masked — a taller window
  would be a slab of glass with icons along the bottom. Growing the icon within its (fixed) cell
  keeps the strip exactly as tight as it is.
- **The slide is still timer-driven, not Composition.** Hiding moves the *window* off the screen
  edge, and Composition animates content inside a window — it cannot animate an HWND's position.
  Keeping the window still and sliding its content would leave a full-size invisible window over
  the screen edge eating clicks. The roadmap item is retired; the timing fix above is the part
  that was actually visible.
- **Store / winget submission** still isn't done — the manifests remain staged under
  `packaging/winget/`, and landing them needs a signed release and a PR to `microsoft/winget-pkgs`.
- **Translations for the new strings** are machine-assisted like the rest and unreviewed by native
  speakers.
- The **UI tests** now cover the Appearance and Shortcuts pages and a group fly-out, but snap and
  auto-hide are still untested (the harness would have to move the window and watch it slide) and
  the tray menu still has no UI Automation surface at all.

---

### Round 1 — the original six roadmap items

Everything below was implemented in one working session against the six items in the README's
"Roadmap / future enhancements" section, in this order: separators/custom icons → running-app
indicators → grouping → UI smoke tests → multiple/per-monitor docks → ARM64 build (ARM64 landed
first in practice since it was pure build config with no behavioral risk; the config-schema
migration for multi-dock support went last since every other item was easier to build against a
single dock first).

### Added

- **ARM64 build.** `Anchor.csproj` now targets `x64;ARM64`; `-p:Platform=ARM64` builds natively
  for Windows-on-ARM. `scripts/package-release.ps1` gained `-Architecture x64|arm64|both` and
  produces a zip per architecture. Added an arm64 entry to the winget installer manifest.
- **Separators.** A thin divider you can drop anywhere on the dock. Add one from the dock's
  right-click menu (*Add separator*) or the Add window (*Separator* type). Renders as a hairline
  in a narrow 13px cell instead of a full 40px icon slot.
- **Custom icons.** *Change icon…* on an item's right-click menu opens a file picker (PNG, ICO,
  JPG, BMP, GIF, TIF); *Use the default icon* clears it back to the shell/favicon icon.
- **Running-app indicators.** A dot under an icon whose app already has a window open; clicking it
  brings that window forward instead of starting a second copy. **Shift+click** forces a new
  instance, matching the Windows 11 taskbar. Polls every 2 seconds; toggle in
  Settings ▸ General ▸ *Running-app indicators*. Shortcuts (`.lnk`) are resolved to their target
  exe via `IShellLinkW` so a pinned shortcut still matches its running process.
- **Groups.** One dock icon that holds several items and opens them in a fly-out. Right-click an
  item → *Move to group ▸* (an existing group, or *New group…*); a group's own menu offers
  *Ungroup* (puts its contents back on the strip) and *Open group*. Groups don't nest and can't
  hold separators.
- **Multiple / per-monitor docks.** Add a dock from any dock's right-click menu (*Add another
  dock*) or **Settings ▸ Docks ▸ Add dock**. Each dock has its own items, monitor, edge, auto-hide
  and always-on-top setting; theme, language, the global shortcut and start-with-Windows stay
  shared across all of them. The tray icon and global shortcut summon every dock at once. A dock's
  monitor is derived from its stored position, not a separate display id.
- **UI smoke tests** (`tests/Anchor.UITests/`). Launches the published `Anchor.exe` and drives it
  through UI Automation (FlaUI/UIA3): dock appears, seeded items render as named buttons, gear
  opens Settings and its pages switch, Docks page lists a dock, closing Settings doesn't take the
  dock down. Opt-in and excluded from `Anchor.slnx` so `dotnet test` on the solution stays fast
  and headless; run via `scripts/run-ui-tests.ps1`. Each run uses a throwaway `ANCHOR_DATA_DIR`
  so it never touches a real dock.
- **`ANCHOR_DATA_DIR` environment variable** to redirect `dock.json` and the icon cache to a
  custom folder — enables the UI test harness and a portable install from a removable drive.
  Scopes the single-instance mutex too, so a redirected copy runs alongside the everyday one.
- Settings ▸ Docks page: one card per dock (name, monitor picker, position, transpose, auto-hide,
  always-on-top, remove).
- Apps & links page now lists items grouped by dock (when more than one exists) and shows a
  group's children indented underneath it, with a "move out of group" action.

### Changed

- `DockConfig` restructured around `Docks: List<DockProfile>` instead of a single flat set of
  fields. **Old single-dock config files upgrade automatically on first launch** — folded into
  `Docks[0]` with every setting intact, and the file is rewritten once in the new shape.
- `DockItem` gained `Children` (a group's contents) and `IsRunning`/`HasCustomIcon`/`CellExtent`
  and related sizing properties.
- Settings window reorganized: the old single **General** page's position/auto-hide/always-on-top/
  transpose controls moved to the new **Docks** page (they're per-dock now); General keeps theme,
  language, shortcut, running-app indicators, start-with-Windows, and reset.
- Drag-to-reorder math changed from a uniform cell pitch to walking actual per-cell extents, so
  reordering around a separator's narrower slot doesn't misplace the drop target.
- `dock.json` is now written with relaxed JSON escaping so shortcuts like `Ctrl+Alt+A` and
  non-ASCII item names stay human-readable instead of being `\u`-escaped.

### Fixed

- **Published builds failed to start.** `dotnet publish` was not copying `Anchor.pri` (the
  compiled-XAML resource index) into the output, so any published `Anchor.exe` — including the
  already-released `dist/Anchor-win-x64-1.0.0.zip` — crashed on its first window with *"Cannot
  locate resource from `ms-appx:///DockWindow.xaml`"*. Fixed by enabling `EnableMsixTooling` even
  though the app ships unpackaged; that switch is what generates the resource index.
  **The 1.0.0 release zip should be re-cut from a build that includes this fix.**
- `DisplayArea.FindAll().ToList()` threw `InvalidCastException` through its WinRT projection,
  crashing the Settings window while building the new Docks page's monitor list. Now copied out by
  index instead of through LINQ's `IEnumerable` adapter.

### Known gaps in this round

- Translations for the new strings (German, Spanish, French, Hindi, Japanese, Portuguese,
  Simplified Chinese) are machine-assisted, like the existing eight-language set, and unreviewed
  by native speakers.
- The ARM64 build cross-compiles cleanly from an x64 host but has not been run on real ARM64
  hardware.
- Group fly-outs are click-only (no hover-to-open) and filing an item into a group has no drag
  gesture yet — only the right-click *Move to group* menu.

## Earlier history

Everything before this entry predates this changelog; see `git log` for the commit history
(localization for Japanese/Portuguese/Simplified Chinese, MSIX packaging metadata, winget
manifests, the Settings window, and the initial dock implementation).
