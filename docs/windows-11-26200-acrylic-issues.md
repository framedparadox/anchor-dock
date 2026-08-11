# Acrylic glass on Windows 11 Enterprise (build 26200): investigation notes

**Status:** investigation only, based on static code review + documented Windows/WinAppSDK
platform behavior. Written without a Windows 11 26200 machine to reproduce on directly — this
environment is Linux-only, so nothing below was confirmed by running Anchor on the actual build.
Treat it as a ranked set of hypotheses and a repro checklist, not a confirmed root cause. See
[Confirming on a real device](#confirming-on-a-real-device) before acting on any of it.

## Scope

Build **26200** is the Canary/Dev-channel branch that shipped as **Windows 11, version 25H2**. It
postdates the SDK version Anchor is actually built and tested against:
`net10.0-windows10.0.26100.0` (see `src/Anchor/Anchor.csproj`), with the manifest's
`MaxVersionTested` derived from that same value (see the `CHANGELOG.md` "Round 3" entry). 26200 is
a newer OS than 26100 — Anchor has never been validated against it, only against the build one
step behind it. That gap doesn't by itself break anything, but it does mean any compositor
behavior Microsoft changed between 26100 and 26200 is untested territory for this app.

The report is: **on Windows 11 Enterprise, build 26200, the acrylic ("glass") theme doesn't really
work** — presumably the dock renders as a flat/opaque color instead of the translucent blur it
shows elsewhere.

## How Anchor's acrylic actually works

Two different mechanisms are in play, and they can fail independently:

- **The dock strips** (`DockWindow`) use a hand-rolled `DesktopAcrylicController` via
  `src/Anchor/Services/AcrylicBackdropManager.cs`, forced `IsInputActive = true` so the glass
  doesn't collapse to a flat color just because the dock is never the foreground window (see the
  class doc comment). This is the "taskbar-style" material — always-on, tuned per theme.
- **Settings / Add / Edit windows** use the simpler built-in `MicaBackdrop`
  (`SystemBackdrop = new MicaBackdrop();` in `SettingsWindow.xaml.cs`, `AddNewWindow.xaml.cs`,
  `EditWindow.xaml.cs`). Mica and Acrylic are different materials with different fallback rules,
  so "the acrylic theme doesn't work" could mean either or both are affected.

Both ultimately depend on the same OS-level plumbing: the DWM/composition stack deciding whether
to actually blur-and-tint the window or substitute a flat fallback color. Anchor has no way to
force that decision — it can only ask, via `DesktopAcrylicController.IsSupported()`
(`AcrylicBackdropManager.cs:51`), which reports OS/API *capability*, not whether the visual effect
will actually render for this user, this session, or this hardware. That gap is the throughline
for every hypothesis below: `IsSupported()` returns `true`, `TryApply()` "succeeds", and the glass
still comes out flat, because the thing that suppressed it lives below the API Anchor is calling.

## Ranked hypotheses

### 1. "Transparency effects" is off (most likely on a managed Enterprise device)

Windows has a **user/policy-level transparency toggle** — Settings ▸ Personalization ▸ Colors ▸
"Transparency effects" — backed by
`HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\EnableTransparency`. When it's
`0`, `DesktopAcrylicController` (and Mica) silently render their **fallback color** instead of the
translucent material — no error, no event, nothing `IsSupported()` reflects.

This is the single most likely explanation specifically for an **Enterprise** build: transparency
effects are a common target for organizational hardening/performance baselines (reduced GPU load
on thin clients/VDI, accessibility defaults, or just an imaging default nobody revisited), pushed
via Group Policy (`Personalization\ForceEnableTransparency` and related settings under
`Administrative Templates ▸ Control Panel ▸ Personalization`) or an Intune policy of the same
shape. A device answering to "Windows 11 Enterprise" is far likelier to have this managed than a
consumer/Pro installation, independent of anything about build 26200 specifically.

- **Code evidence:** `AcrylicBackdropManager.TryApply()` checks `IsSupported()` only; nothing in
  Anchor reads `EnableTransparency` or reacts to it changing (the `UISettings.ColorValuesChanged`
  subscription in `OnSystemColorsChanged` re-tints colors but doesn't detect "transparency is
  off" as a distinct state — see `SyncWithSystemColors()`).
  `_controller.FallbackColor` (`UpdateTheme()`, `AcrylicBackdropManager.cs:185`) is exactly what
  would be showing if this is the cause — a flat, correctly-themed (dark/light) color, which reads
  as "the *theme* is fine, the *glass* isn't."
- **How to confirm:** Settings ▸ Personalization ▸ Colors ▸ "Transparency effects" on the affected
  machine. If it's off, this is very likely the whole story.

### 2. Anchor is running elevated while the shell isn't

DWM does not composite transparency/blur effects for a window owned by a process at a different
integrity level than the desktop's — most commonly, **an elevated ("Run as administrator") app on
a desktop running as standard user**. This is a long-standing, still-current Windows 11 behavior
(predates Mica/Acrylic; Aero glass had the same restriction), not something introduced in a recent
build.

This is worth ranking highly for an Enterprise environment specifically because:
- UAC/App-control policies on managed devices sometimes force elevation for certain launch paths
  (a scheduled task at `RunLevel="highest"`, a policy that elevates everything from a given
  install location, an EDR/AV wrapper that re-launches under a different token).
- Anchor's own startup path doesn't request elevation (`app.manifest` — worth a quick check on the
  affected machine's actual manifest/shortcut if this is suspected), but a device-specific policy
  or a "run as admin" compatibility flag set on the `.exe` (persisted per-user in the registry, not
  in the file) can override that per machine, invisibly to anyone reading the source.
- **How to confirm:** Task Manager ▸ Details ▸ check whether `Anchor.exe` shows elevated
  privileges, or right-click the shortcut/exe ▸ Properties ▸ Compatibility ▸ "Run this program as
  an administrator".

### 3. VDI / thin-client GPU (Windows 365, AVD, Citrix)

Enterprise fleets frequently run desktops as virtual machines with limited or virtualized GPUs.
`DesktopAcrylicController.IsSupported()` is documented to also fold in a hardware/driver check,
but historically (and still, on some virtual GPU drivers) that check can pass on a WDDM-capable
virtual adapter that then can't actually sustain the compositor's real-time blur, so the runtime
falls back silently rather than `IsSupported()` reporting `false` up front.

- **How to confirm:** check whether the affected "Windows 11 Enterprise, build 26200" machine is
  itself the physical console, or a VDI/AVD/W365 session. If the Mica-backed *dialog* windows
  (Settings/Add/Edit) show real Mica blur but the dock's acrylic doesn't (or vice versa), that
  split points at something material-specific (recipe/`DesktopAcrylicKind.Base` vs. `Mica`) rather
  than a blanket GPU limitation, which would be expected to hit both.

### 4. Power/performance policy throttling composition

Enterprise power-management baselines (via Intune/GPO) sometimes push aggressive "Best power
efficiency" or Energy Saver defaults that can affect how eagerly Windows keeps compositor effects
"live" on a background/non-foreground window. Anchor's dock is deliberately never the foreground
window (`IsInputActive = true` exists specifically to fight the *default* "unfocused → flat color"
behavior — see the `AcrylicBackdropManager` doc comment), so it's already fighting an uphill battle
against exactly the kind of policy this hypothesis describes; an aggressive-enough power policy
could plausibly win anyway. Ranked below 1–3 because it's a "maybe contributes" rather than a
clean on/off explanation.

### 5. A genuine 26200 regression

Least likely to be *specific to Enterprise*, but real: Insider/Canary builds do occasionally ship
compositor regressions that get fixed in a later flight. Nothing in this repo's history or in
Anchor's own code changed recently around acrylic (`git log` shows `AcrylicBackdropManager.cs` as
stable), so if this is the cause it is purely an OS-side regression Anchor cannot work around from
user-mode — only detect and message about.

## What this is probably *not*

- **Not** the `IsInputActive = true` "always active" trick itself — that's a deliberate, documented
  choice to keep the glass alive on a window that's never focused (see the class doc comment), and
  removing it would make things *worse* (the taskbar-style look this exists for), not fix a "glass
  doesn't render at all" symptom.
- **Not** the per-theme tint recipe math (`DefaultDarkRecipe`/`DefaultLightRecipe`,
  `SyncWithSystemColors`) — a bad recipe would produce an oddly-*colored* glass, not glass that
  doesn't render as glass at all. Worth double-checking only if the report turns out to mean "the
  color looks off" rather than "it's flat/opaque."
- **Not** the dock-vs-Mica-window split in isolation — both materials share the same
  policy/elevation/hardware dependencies described above, so seeing "acrylic doesn't work" without
  more detail doesn't yet tell us whether Mica is also affected.

## Recommended next steps (not implemented here — scope was "investigate and document")

1. **Confirm which hypothesis it is** using the checklist below before writing any code — numbers
   1 and 2 are the most likely and the cheapest to rule in/out.
2. If it's **#1 (transparency off)**, the actionable fix on Anchor's side isn't "force it on" —
   that's not possible from user-mode — it's **detecting and saying so**. Anchor already has the
   UI pattern for this: `SettingsWindow.xaml`'s `StartupBlockedBar`/`BackupBar` `InfoBar`s
   (`SettingsWindow.xaml.cs:87-88`, `SetBarOpen`). A similar bar reading
   `EnableTransparency` at startup and on `UISettings.ColorValuesChanged` would turn a silent,
   confusing fallback into an explained one, with a link to the Personalization page.
3. If it's **#2 (elevation mismatch)**, Anchor could detect its own elevation
   (`WindowsIdentity.GetCurrent()` + `WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`)
   at startup and warn if elevated — a dock has no reason to run elevated, so this would double as
   a general correctness check, not just an acrylic one.
4. If it's **#3/#4 (VDI/power policy)**, there's likely nothing to build — the `FallbackColor` path
   already exists and is themed correctly (see `AcrylicBackdropManager.cs:219-229`); the outcome is
   "acrylic degrades to a solid, still-correctly-themed dock," which is a reasonable place to land
   on hardware that can't sustain the blur.

## Confirming on a real device

On the affected Windows 11 Enterprise / build 26200 machine, in order:

1. **Settings ▸ Personalization ▸ Colors ▸ "Transparency effects"** — is it on?
2. **Task Manager ▸ Details ▸ Anchor.exe** — elevated or not? Does it match the shell's level?
3. **Is this a VM/VDI session** (Windows 365, Azure Virtual Desktop, Citrix) or physical hardware?
4. **Do the Mica-backed windows** (open Settings, Add, or Edit) show real blur, or are they flat
   too? This tells us whether the problem is acrylic-specific or hits both materials.
5. `dxdiag` ▸ Display tab — confirm a real WDDM 2.x+ driver is in use, not a basic/RDP/virtual
   adapter without hardware acceleration.
6. Check `gpresult /h report.html` (or `rsop.msc`) for any Personalization-category policy under
   Computer/User Configuration — confirms whether #1 is policy-enforced (and therefore not
   user-changeable without an admin) versus just a locally-flipped setting.

Whichever of these comes back positive narrows the fix to either a messaging change in Anchor
(§ Recommended next steps) or "nothing to fix here, it's the platform/policy" — but it removes the
guesswork the current report leaves.
