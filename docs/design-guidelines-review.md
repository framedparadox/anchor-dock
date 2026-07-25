# Anchor — Windows App Design Guidelines Review

A review of Anchor against Microsoft's **Windows 11 Fluent Design System** and the
[Windows app design guidelines](https://learn.microsoft.com/windows/apps/design/). The
project is a WinUI 3 / Windows App SDK desktop app, so it is held to the Fluent/WinUI bar.

The review is organized by the design-fundamental areas Microsoft publishes: **Accessibility,
Controls, Color & Theming, Materials, Typography, Iconography, Layout, Motion, Input, and
Windowing**. Each finding is tagged with a severity and whether it was **Fixed** in this pass
or is a **Recommendation** for follow-up.

> Note on verification: this pass was authored in a Linux CI environment with no Windows
> toolchain, so the changes were **not compiled or run**. They are standard WinUI XAML/C#
> patterns and were reviewed by hand; please build and smoke-test on Windows before shipping.

---

## Summary

The app already did several things well: Segoe Fluent Icons, Windows 11 rounded corners (DWM),
per-monitor-v2 DPI awareness, real acrylic material tuned per theme, tooltips on every control,
a 4-epx-aligned layout with 40 px taskbar-sized cells, and correct use of `MenuFlyout` and the
"…" ellipsis convention for commands that open a dialog.

The **dominant gap was accessibility**: the interactive elements were bare `Grid`s driven only
by pointer events, which made the whole dock unreachable by keyboard and largely invisible to
assistive technology. That, plus a few theming/typography/iconography items, is what this pass
addresses.

| # | Area | Finding | Severity | Status |
|---|------|---------|----------|--------|
| 1 | Accessibility | No keyboard operability (items/gear were pointer-only `Grid`s) | High | **Fixed** |
| 2 | Accessibility | No UI Automation name / control pattern for items | High | **Fixed** |
| 3 | Accessibility | No focus visuals | High | **Fixed** |
| 4 | Accessibility | No keyboard path to the context menus | High | **Fixed** |
| 5 | Accessibility | High Contrast theme ignored (forced Dark + hardcoded acrylic) | High | **Fixed** |
| 6 | Accessibility | Reduced-motion setting ignored by the auto-hide slide | Medium | **Fixed** |
| 7 | Controls | Custom panels + pointer handlers instead of `Button` | Medium | **Fixed** |
| 8 | Typography | Flyout headers hardcoded a font weight instead of the type ramp | Low | **Fixed** |
| 9 | Iconography | Fluent glyphs stored as raw non-ASCII literals in source | Low | **Fixed** |
| 10 | Accessibility | Keyboard-only users cannot *reach* the dock (no Alt-Tab, no hotkey) | High | **Recommendation** |
| 11 | Motion | Timer-driven animations instead of Composition | Low | **Recommendation** |
| 12 | Theming | Hardcoded colors don't follow the accent color | Low | **Recommendation** |
| 13 | Project | No app icon / assembly product metadata | Low | **Fixed** |

---

## Fixed in this pass

### 1–4, 7. Accessibility + correct control usage — dock items and gear are now `Button`s

**Guideline:** *"Apps should be usable with a keyboard alone"* and *"use the platform controls"*
([Keyboard interactions](https://learn.microsoft.com/windows/apps/design/input/keyboard-interactions),
[Accessibility](https://learn.microsoft.com/windows/apps/design/accessibility/accessibility)).

**Before:** each dock item and the settings gear were `Grid` elements wired to `PointerEntered`,
`PointerExited`, `Tapped`, `PointerReleased`, and `RightTapped`. A `Grid` is not a `Control`: it
cannot take keyboard focus, has no Invoke automation pattern, and shows no focus visual. Launching
was done from both `Tapped` and `PointerReleased` with a 350 ms debounce to avoid double-firing —
a workaround for not using a control that raises a single `Click`.

**After:** the item template root and the gear are `Button`s. From the platform, for free, this
gives:

- **Keyboard focus + Tab order** (Button is a `Control`, `IsTabStop` by default).
- **Space/Enter invoke** via a single `Click` handler (`Item_Click` / `Settings_Click`),
  replacing the dual `Tapped` + `PointerReleased` + debounce logic.
- **Focus visuals** (`UseSystemFocusVisuals` is on by default).
- **Hover and pressed visual states** using the standard subtle-fill brushes — replacing the
  hand-rolled `HoverPill` + storyboard, and adding a pressed state that was previously missing.
- **An Invoke automation pattern**, so Narrator announces "button" and can activate it.

Arrow-key navigation across the strip is enabled with
`XYFocusKeyboardNavigation="Enabled"` on the item `StackPanel`.

**Drag still works from an icon.** Dragging the whole dock relies on `DockStrip.PointerPressed`.
Because `Button` marks `PointerPressed` as handled for its own press visual, the drag subscription
was changed to `AddHandler(UIElement.PointerPressedEvent, …, handledEventsToo: true)` so a press
that starts on an icon still begins a drag. The drag itself is unchanged (global cursor polling),
and `_dragOccurred` still suppresses the click that ends a drag. That flag is now also cleared
after the gesture ends (low-priority dispatch) so a later keyboard `Enter` is never swallowed.

### 2. Screen-reader names

Each item `Button` sets `AutomationProperties.Name="{x:Bind DisplayName}"`, so Narrator announces
the item's real name. The gear sets `AutomationProperties.Name="Dock settings"`. The purely
decorative visuals (the icon `Image`/`FontIcon` inside a button and the `Divider` rectangle) are
marked `AutomationProperties.AccessibilityView="Raw"` so they are not announced as separate,
nameless nodes.

### 4. Keyboard-reachable context menus

**Guideline:** context menus must be reachable via the keyboard (Menu key / Shift+F10)
([Context menus](https://learn.microsoft.com/windows/apps/design/controls/menus-and-context-menus)).

The per-item menu and the dock menu were bound to `RightTapped` (pointer-only). They now use
`ContextRequested`, which fires for **both** right-click and the keyboard context-menu gesture.
When invoked by keyboard there is no pointer position, so the menu is shown anchored on the
element (`TryGetPosition` → `ShowAt(target)`); with a pointer it opens at the cursor as before.
The gear `Button` also opens the full dock menu on `Click`, giving keyboard users a path to Add /
Snap / Float / Quit.

### 5. High Contrast support

**Guideline:** *"Support high contrast themes"* — don't hardcode colors that override the system
high-contrast palette
([High-contrast](https://learn.microsoft.com/windows/apps/design/accessibility/high-contrast-themes)).

The app pins its content to `RequestedTheme="Dark"` and paints a hand-tuned acrylic, which is a
reasonable choice for matching the dark taskbar — but it made the dock ignore High Contrast, an
accessibility feature. `DockWindow` now detects High Contrast at startup (guarded via
`AccessibilitySettings.HighContrast`) and, when it is on, resets the root to
`ElementTheme.Default` and paints an opaque `SolidBackgroundFillColorBaseBrush` instead of the
acrylic, so the shell's high-contrast system colors come through and the dock stays legible. The
`FontIcon` foregrounds already use `ThemeResource` brushes, which map correctly under HC.

### 6. Reduced motion

**Guideline:** *"Respect the user's motion preferences"*
([Motion](https://learn.microsoft.com/windows/apps/design/motion/)).

WinUI's built-in control animations already honor the system "show animations" setting, but the
custom timer-driven auto-hide **slide** did not. `SetRevealed` now checks
`UISettings.AnimationsEnabled` (cached, guarded) and, under reduced motion, jumps straight to the
target position instead of sliding.

### 8. Typography — use the type ramp

**Guideline:** use the [Fluent type ramp](https://learn.microsoft.com/windows/apps/design/style/typography)
rather than ad-hoc font settings.

The three flyout headers ("Rename", "Edit target", "Add Web Link") hardcoded
`FontWeight = SemiBold` on a plain `TextBlock`. They now use the `BodyStrongTextBlockStyle`
type-ramp style via a small `FlyoutHeader` helper (falling back to SemiBold if the resource is
somehow absent).

### 9. Iconography — robust glyph literals

The fallback Segoe Fluent Icons glyphs in `DockItem.Glyph` were stored as **raw non-ASCII
characters** embedded in the `.cs` source (Globe / Folder / Page). Those code points are correct,
but literal PUA characters in source are fragile (an editor or `.gitattributes`/encoding change
can silently corrupt them) and opaque to read. They are now explicit, self-documenting escapes
(`"\uE774"`, `"\uE8B7"`, `"\uE7C3"`), leaving the file pure ASCII.

---

## Recommendations (not implemented here)

### 10. Let keyboard-only users *reach* the dock — High priority

The dock is intentionally hidden from the taskbar and Alt-Tab (`WS_EX_TOOLWINDOW`,
`IsShownInSwitchers = false`). The fixes above make the dock fully operable **once it has focus**,
but a keyboard-only user still has no way to *move focus to it* without first clicking it with a
mouse. The idiomatic fix is a **global hotkey** (e.g. `RegisterHotKey`, Win+something) that
activates the dock window and focuses the first item. This was left out of this pass because it
needs a Win32 hotkey registration + message hook and should be verified on real hardware. It is
the most important remaining accessibility item.

### 11. Prefer Composition animations over timers — Low priority

The hover fade (removed) and the auto-hide slide/drag use `DispatcherQueueTimer`s ticking every
8–15 ms and calling `AppWindow.Move` per frame. Fluent's motion guidance and WinUI favor
Composition animations, which run off the UI thread and are smoother. The window-position slide
is inherently a Win32 move so it can't be a pure Composition animation, but it could be driven by
a frame callback or eased with the system's standard timing. Functional today; a polish item.

### 12. Follow the accent color — Low priority

Colors are hardcoded in a few places (`AcrylicBackdropManager` tints, the
`0x00161616` DWM border in `WindowChrome.RemoveWindowBorder`, the divider's `Opacity="0.15"`).
Fluent apps generally let the user's **accent color** show through (e.g. on the focused/pressed
state). Consider sourcing hover/pressed accents from `AccentFillColor*` theme resources. Note the
`AcrylicBackdropManager.Light` recipe is currently dead code because the tree is pinned to Dark.

### 13. App identity / metadata — Low priority

`Anchor.csproj` has no `<ApplicationIcon>` and no product/version metadata (`<Product>`,
`<Version>`, `<Company>`, `<Authors>`). A shipping Windows app should carry an icon and version
resource. (The window is deliberately off the taskbar, so this is about the executable's
properties and future packaging, not the dock UI.)

---

## Pass 2 (2026-07-23) — delta review + test coverage

A follow-up pass covering everything added since Pass 1 above: drag-and-drop add, the
"Always on top" setting, the Settings "Apps & links" remove button, and a general re-audit for
Fluent/Store compliance. As with Pass 1, this was authored without a Windows toolchain — changes
are standard, narrowly-scoped WinUI XAML/C# patterns reviewed by hand; build and smoke-test on
Windows before shipping.

| # | Area | Finding | Severity | Status |
|---|------|---------|----------|--------|
| 14 | Iconography | The Settings "remove item" glyph was a raw PUA literal in `.cs` source (the exact anti-pattern #9 from Pass 1 fixed elsewhere, reintroduced here) | Low | **Fixed** |
| 15 | Dialogs | "Reset dock to defaults" (wipes every pinned item, no undo) had no confirmation | Medium | **Fixed** |
| 16 | Windowing | Settings / Add-to-Dock windows were unconditionally always-on-top, even when the dock itself is floating and not topmost | Low | **Fixed** |
| 17 | Accessibility | Add-to-Dock's type-selector tiles (`ToggleButton` + icon/text `StackPanel` content) had no explicit `AutomationProperties.Name`, relying on the framework's plain-text-content fallback | Low | **Fixed** |
| 18 | Input | The Add-to-Dock window had no `Escape`-to-cancel keyboard accelerator (a plain `Window` doesn't get this for free the way a light-dismiss `Flyout` does) | Low | **Fixed** |
| 19 | Input | The Rename/Edit-target flyout text boxes didn't commit on `Enter` (only via the button click) | Low | **Fixed** |
| 20 | Project identity | `Anchor.csproj` had no `<Product>`/`<Company>`/`<Version>`/`<Description>` metadata | Low | **Fixed (partial)** |
| 21 | Testing | No automated test coverage existed anywhere in the repo | Medium | **Fixed (partial)** |
| 22 | Accessibility | Add-to-Dock's type selector is five independent `ToggleButton`s doing manual radio-group bookkeeping, not a `RadioButtons`/`SelectorItem`-backed control — Narrator announces each as an isolated toggle, not "1 of 5" group membership | Medium | **Recommendation** |
| 23 | Feedback | Removing an item (context menu "Remove", or the Settings Apps-list trash button) is immediate and irreversible, with only a tooltip as a warning | Low | **Recommendation** |

### 14–20. Fixed in this pass

**#14 — Iconography.** `SettingsWindow.xaml.cs`'s per-row "Remove from dock" button set
`Glyph` to a raw non-ASCII Private-Use-Area character in source, the same fragility Pass 1
already called out and fixed for `DockItem.Glyph` (recommendation #9: an editor/encoding change
can silently corrupt an un-escaped PUA literal, and it's opaque to read in a diff). Changed to
the explicit escape `"\uE74D"` (Segoe Fluent Icons "Delete"), matching the convention Pass 1
established.

**#15 — Confirm destructive actions.** Fluent guidance is to confirm actions that are hard to
recover from
([Dialogs and flyouts](https://learn.microsoft.com/windows/apps/design/controls/dialogs-and-flyouts/dialogs)).
"Reset dock to defaults" clears every pinned item with no undo, but previously fired on a single
click. It now shows a `ContentDialog` ("This removes every pinned app, file, folder and link
you've added... This can't be undone.") with **Cancel as the default button**, so an accidental
`Enter` press can't wipe the dock. Per-item "Remove" was deliberately left as-is: removing one
easily-re-added item mirrors low-stakes platform conventions (Start menu "Unpin," taskbar "Unpin
from taskbar" don't confirm either); only the bulk, harder-to-recover Reset warranted a dialog.

**#16 — Don't be needlessly always-on-top.** `SettingsWindow` and `AddNewWindow` both
unconditionally set `IsAlwaysOnTop = true` — reasonable when the dock itself is topmost (snapped,
or floating with the user's "Always on top" setting on) so these windows can float above it, but
otherwise it meant a plain Settings/Add dialog would outrank *every other app on the machine*
(full-screen apps, video calls) for no reason, contrary to general Windows desktop UX practice of
reserving always-on-top for windows that truly need it. Both windows now compute
`dock.Config.Snapped || dock.Config.AlwaysOnTop` at open time instead of hardcoding `true`.
(This is a snapshot taken when the window opens, not a live binding — matching the previous
always-`true` behavior's own timing, just conditioned correctly. Making it track a later toggle
of "Always on top" while Settings is already open is a possible future refinement.)

**#17 — Robust accessible names.** The five type-selector tiles in Add-to-Dock are
`ToggleButton`s whose `Content` is an icon + `TextBlock` subtree rather than a plain string.
WinUI's default name-from-content fallback generally handles this correctly, but it's an
implicit, easy-to-regress behavior; each tile now sets `AutomationProperties.Name` explicitly
("App", "File", "Folder", "Web Link", "Shortcut") and marks its icon/label children
`AccessibilityView="Raw"` (matching the pattern Pass 1 used for dock items), so Narrator's
announcement doesn't depend on inference.

**#18–19. Keyboard access for dialog-style windows.**
[Keyboard interactions](https://learn.microsoft.com/windows/apps/design/input/keyboard-interactions)
guidance expects `Esc` to dismiss a dialog-like surface. A `Flyout` gets this for free
(light-dismiss), but Add-to-Dock is a real top-level `Window`, which does not — it now has a
`KeyboardAccelerator Key="Escape"` on its Cancel button. (An `Enter`-submits accelerator on "Add
to Dock" was deliberately **not** added: with a focused Cancel button, a global `Key="Enter"`
accelerator elsewhere risks double-invoking both the accelerator and the focused button's own
native Enter-activation — an ambiguity not worth taking on unverified without a Windows build to
test against.) Separately, the Rename/Edit-target flyouts' text boxes now commit on `Enter`
(matching their button); `Escape` for those was **not** touched because `Flyout` already
dismisses on `Escape` natively.

**#20 — Project/app identity.** Added `<Product>`, `<Company>`, `<Authors>`, `<Description>`,
and version properties to `Anchor.csproj` — shown in the exe's Details tab and Task Manager, and
required verbatim-consistent metadata for a Store/installer submission (see
`docs/microsoft-store-deployment.md`). This was a **partial** fix for recommendation #13 at the
time: the app still had no `<ApplicationIcon>` / `.ico` asset, since that needed a source image to
generate the icon from. Closed in full in Pass 3 below once one existed.

### 21. Test coverage added

The repository had **no automated tests at all**. Added `tests/Anchor.Tests/` (referenced from
`Anchor.slnx`), covering the parts of the app that are pure logic with no live shell/network/
registry/XAML-tree dependency:

- `Models/DockItemTests.cs` — `INotifyPropertyChanged` firing, the `Kind → Glyph` mapping, the
  `IconImage`-null → glyph-visible / image-hidden visibility contract, `IsSeparator`, `Id`
  uniqueness, and property defaults.
- `Models/DockConfigTests.cs` — default settings values (what a first-run dock starts with).
- `Services/DockItemFactoryTests.cs` — `Classify()` (folder / http(s) / launchable-extension /
  generic-file branches) and `SuggestName()` (URL host, path-based name, trailing-separator
  trimming).

**Deliberately not covered**, and why: `IconService` (shell thumbnails + a real HTTP favicon
fetch), `Launcher` (`ShellExecute`), `WindowChrome`/`NativeMethods` (Win32/DWM interop),
`StartupService` (the real per-user registry `Run` key), and `DockStore` (real `%AppData%` I/O)
are integration-shaped — they talk to real OS/network resources and the app currently has no
seams (no injectable filesystem/registry/HTTP abstraction) to fake them out. `DockWindow`,
`SettingsWindow`, and `AddNewWindow` are UI classes that need a live `Application` + window +
dispatcher to construct at all, so interaction behavior (drag/reorder, auto-hide, snap, the
dialogs above) is still verified only by manual testing, per the README's "Using it" section.
Introducing DI seams for the OS-facing services so they *can* be unit-tested (e.g. an
`IFileSystem`/`IShellLauncher` abstraction) would be a reasonable next step but is a larger
refactor than this review pass, and is called out here as scope for a follow-up rather than done
speculatively.

**Not verified by actually building**: like Pass 1, this was written without access to a Windows
/ `dotnet` toolchain (this environment has neither), so the test project's configuration and the
tests themselves have been checked carefully by hand but not compiled or run. Build and run
`dotnet test Anchor.slnx` (or `Test Explorer` in Visual Studio) on Windows before relying on this
as a regression gate.

### 22–23. Recommendations (not implemented here)

**#22 — True radio-group semantics for the type selector — Medium priority.** Add-to-Dock's
"What are you adding?" tiles are five independently-toggled `ToggleButton`s with hand-rolled
mutual-exclusion logic (`SelectType` unchecks the other four). Narrator therefore announces each
as a standalone toggle ("App, toggle button, on") rather than as a member of a group ("App, 1 of
5"), and there's no `RadioButtons`/`ListView SelectionMode="Single"` selection pattern backing it.
The Fluent-correct control for "choose exactly one from a small set"
([Radio buttons](https://learn.microsoft.com/windows/apps/design/controls/radio-button)) is
`RadioButtons`, which can host arbitrary item content (including an icon+label tile) via a custom
`ItemTemplate`/style. Left as a recommendation rather than done here because it's a real
structural/visual change to a working, already-reasonably-accessible control (#17 above) that
should be verified interactively on Windows, not guessed at blind.

**#23 — Consider a lightweight undo affordance for "Remove" — Low priority.** Per-item removal
(context-menu "Remove", Settings Apps-list trash button) is immediate with no confirmation or
undo, relying only on the tooltip/accessible name to convey what will happen. This matches
platform convention closely enough that it wasn't escalated to a confirmation dialog (see #15),
but a `Snackbar`/`InfoBar`-style "Removed 'X' — Undo" affordance for a few seconds after removal
would be a nice, low-risk polish item consistent with how Windows itself softens single-item
removals (e.g. Outlook's "Message moved. Undo").

---

## What was verified as already-compliant

- **Materials.** Acrylic is an appropriate material for a floating, taskbar-like utility surface;
  forcing it active (`IsInputActive = true`) so it doesn't collapse to a flat fallback while
  unfocused is the correct technique for a never-foreground window.
- **Rounded corners.** Uses the native Windows 11 DWM rounding (`DWMWCP_ROUND`) plus matching
  `CornerRadius` on inner elements.
- **Icon size & layout.** 24 px icons in 40 px cells with 4 px spacing — matches the Windows 11
  taskbar and the 4-epx alignment grid; 40 px meets the recommended minimum touch-target size.
- **Iconography.** Segoe Fluent Icons via `SymbolThemeFontFamily`; the gear uses the standard
  Settings glyph `E713`.
- **DPI.** Per-monitor-v2 via the manifest; DIP→pixel conversion is explicit in layout math.
- **Title bar / windowing.** `ExtendsContentIntoTitleBar` is the right call for a borderless
  surface, and is also needed here so the content island receives pointer input.
- **Tooltips & command affordances.** Every actionable element has a tooltip; menu commands that
  open a dialog use the "…" ellipsis.

---

## Pass 3 (2026-07-25) — Anchor rebrand + app icon

The project was renamed from DockGx to **Anchor** (namespaces, assembly, AppData/registry/log
identifiers, project and solution file names) and given a real source icon (`docs/anchor.png`).

**Recommendation #13 — App icon — closed.** A multi-resolution `.ico` (16–256 px) generated from
`docs/anchor.png` is now wired up via `<ApplicationIcon>Assets\Anchor.ico</ApplicationIcon>` in
`Anchor.csproj`; the built `Anchor.exe` carries a real icon in Explorer, the taskbar, and Alt-Tab.
The same source also produced the Microsoft Store MSIX tile/logo set (`src/Anchor/Images/`) — see
`docs/microsoft-store-deployment.md`. Row 13 and #20 in the tables above are now fully **Fixed**;
the only remaining identity gap is Partner Center's actual publisher identity, which is an account
detail, not a code change.
