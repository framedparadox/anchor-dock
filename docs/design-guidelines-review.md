# DockGx — Windows App Design Guidelines Review

A review of DockGx against Microsoft's **Windows 11 Fluent Design System** and the
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
| 13 | Project | No app icon / assembly product metadata | Low | **Recommendation** |

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

`DockGx.csproj` has no `<ApplicationIcon>` and no product/version metadata (`<Product>`,
`<Version>`, `<Company>`, `<Authors>`). A shipping Windows app should carry an icon and version
resource. (The window is deliberately off the taskbar, so this is about the executable's
properties and future packaging, not the dock UI.)

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
