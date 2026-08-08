# Deploying Anchor to the Microsoft Store

A practical, end-to-end guide to publishing Anchor on the Microsoft Store. It is written
against this repository's actual configuration, so it also calls out the app-specific gotchas
(unpackaged default build, launching arbitrary programs, arbitrary-path icon reading, config in
`%AppData%`) that a generic guide would skip.

> TL;DR: Anchor's default `dotnet build`/`dotnet publish` stays an **unpackaged** app
> (`WindowsPackageType=None` in `src/Anchor/Anchor.csproj`). **Option A (single-project MSIX) is
> already wired up** behind the `StorePackage` MSBuild property — `Package.appxmanifest`, the real
> Partner Center identity, and the `src/Anchor/Images/` visual assets are all in this repo, and
> `dotnet build -p:StorePackage=true` produces a signable `.msix` today. What's **left** is the
> `.msixupload` bundling step (needs MSBuild/VS — §5), WACK (§6), and completing the submission
> forms (§7), of which the **privacy policy URL** and **support contact** are the two that have
> failed a submission here before. A no-repackaging alternative (ship the existing `.exe` via the
> Store's EXE/MSI path) is covered as **Option C**.

---

## 0. The key decision: how Anchor reaches the Store

| Option | What you ship | Keeps unpackaged build? | Effort | Recommended when |
|--------|---------------|--------------------------|--------|------------------|
| **A. Single-project MSIX** | One `.msixupload` from the app project | No (project becomes packaged, or use a `Store` build config) | Low | You're happy making MSIX the primary format |
| **B. Packaging project (`.wapproj`)** | A `.msixupload` from a separate project that references Anchor | **Yes** | Medium | You want to keep shipping the unpackaged utility *and* publish to the Store |
| **C. Bring-your-own EXE/MSI** | The current self-contained `.exe` wrapped in an installer | Yes | Low–Medium | You want minimal changes and are OK self-hosting/authoring an installer + updates |

Because Anchor is deliberately "a normal desktop utility with no MSIX" by default (see the
README), this repo implements **Option A** the low-friction way: the packaging bits (manifest,
images, csproj properties) are gated behind `-p:StorePackage=true`, so `dotnet build`/`dotnet
publish` without that flag stay exactly as unpackaged as before, and only an explicit Store build
turns on MSIX. If you'd rather have a fully separate project so the app project never even
mentions MSIX, Option B is still available (not implemented here) — see §4.1. Both A and B produce
a real MSIX and get the best Store experience (automatic updates, clean install/uninstall). Steps
3–8 below apply to A and B identically; Option C is described separately at the end.

---

## 1. Prerequisites

### Accounts & policy
- **A Microsoft Partner Center developer account.** Register at
  <https://partner.microsoft.com/dashboard/registration>. There is a one-time registration fee
  (historically **US$19** for individuals and **US$99** for companies — check current pricing).
- Read the **[Microsoft Store Policies](https://learn.microsoft.com/windows/apps/publish/store-policies)**.
  Two are directly relevant to Anchor:
  - **10.2 / 10.1 (functionality & security):** an app that launches other programs is allowed,
    but it must do what it says and not run undisclosed code.
  - **Restricted capabilities:** `runFullTrust` requires a written justification during
    submission (see §3). Anchor previously also declared `broadFileSystemAccess`; that request
    was **denied** on review — Microsoft's own rejection wording warns that resubmitting with
    the same justification will likely get the same outcome — so `IconService` was refactored to
    avoid needing it at all (see item 3 in §2 below). Don't re-add it without a materially new
    justification.
  - **Support info:** Partner Center **Properties → Support Info** requires a valid support
    contact or developer website URL. A submission can be rejected for missing this alone — set
    it under **Use different details for this app → Support contact info** before resubmitting.

### Tools
- **Windows 10 2004+ / Windows 11** dev machine (MSIX tooling is Windows-only).
- **Visual Studio 2022 (17.x)** with the **.NET Desktop** and **Windows App SDK / WinUI**
  workloads, or the standalone **MSBuild** + Windows SDK. Note: **MSIX packaging generally
  requires MSBuild/Visual Studio, not `dotnet build`** — the plain .NET CLI cannot emit an MSIX.
- **.NET 10 SDK** (matches `TargetFramework net10.0-windows10.0.26100.0`).
- **Windows App Certification Kit (WACK)** — installed with the Windows SDK; used in §6.
- Optional for CI: the **Microsoft Store Developer CLI (`msstore`)** — see §9.

---

## 2. App-specific considerations (read before packaging)

These are the things about Anchor specifically that affect a Store submission.

1. **It's unpackaged by default.** `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`
   are the settings a plain `dotnet build` sees; MSIX only turns on with `-p:StorePackage=true`
   (Option A, already wired up — see §4). The self-contained setting is fine for the Store (§5).

2. **It launches arbitrary apps/files/URLs.** `Services/Launcher.cs` uses `Process.Start` with
   `UseShellExecute=true`. This requires the app to be **full trust**
   (`EntryPoint="Windows.FullTrustApplication"` + the `runFullTrust` capability). That's normal
   for a packaged WinUI 3 desktop app, but it *is* a restricted capability you must justify.

3. **It reads icons from arbitrary file-system paths — via Win32, not the WinRT broker.**
   `Services/IconService.cs` resolves icons for targets like `C:\Windows\explorer.exe` and any
   file/folder the user pins entirely through Win32 shell APIs (`SHGetFileInfo` +
   `SHGetImageList`/`IImageList` for a 256x256 "jumbo" icon matching Explorer, falling back to
   the classic 32x32 icon). It previously used the WinRT `StorageFile`/`StorageFolder` APIs for
   this, which under MSIX go through a broker that restricts arbitrary paths unless you declare
   **`broadFileSystemAccess`** (a restricted capability) — but that capability request was
   **denied** by Store review, so the WinRT calls were removed instead. A full-trust packaged
   process needs no broker capability for plain Win32 file/icon access, so this needs nothing in
   `<Capabilities>` beyond `runFullTrust`.

4. **Config lives in `%AppData%\Anchor\dock.json`** (`Services/DockStore.cs` via
   `Environment.SpecialFolder.ApplicationData`). Under MSIX this call is **redirected** to the
   package's per-user store (`...\Packages\<PackageFamilyName>\LocalCache\Roaming\Anchor`), so it
   keeps working with no code change. Caveat: a user's existing *unpackaged* `dock.json` will
   **not** be picked up by the packaged app (different redirected location). If that migration
   matters, copy it on first run, or move to `Windows.Storage.ApplicationData.Current.LocalFolder`
   when packaged.

5. **Visual assets already exist.** `src/Anchor/Images/` has the full Store tile/logo set
   (`Square44x44Logo` + scale/targetsize variants, `Square150x150Logo`, `StoreLogo`,
   `Wide310x150Logo`, `Square71x71Logo`, `Square310x310Logo`, `SplashScreen`), generated from
   `docs/anchor.png` — see §4.2. `src/Anchor/Assets/Anchor.ico` (also generated from
   `docs/anchor.png`) is wired up as the unpackaged exe's `<ApplicationIcon>` too, closing
   recommendation #13 in `docs/design-guidelines-review.md`.

6. **Two architectures.** `<Platforms>x64;ARM64</Platforms>`, with the RID derived from
   `Platform`. Build both and submit them in one `.msixupload` (`AppxBundle=Always` is already
   set) to reach Windows-on-ARM devices as well as x64.

7. **The window hides from the taskbar and Alt-Tab.** That's fine, but make sure users can still
   *find and launch* it after install: the MSIX manifest gives it a **Start-menu entry** by
   default (don't set `AppListEntry="none"`), and quitting is via the dock's gear menu.

8. **"Start with Windows" works differently when packaged, and the code knows the difference.**
   The portable build writes the per-user `HKCU\…\CurrentVersion\Run` key. A *packaged* process
   must not: its writes under `HKCU\Software` land in the package's virtualized registry hive,
   which Windows' autostart never reads — the toggle would appear to work and silently do nothing
   after a reboot. `Package.appxmanifest` therefore declares a
   `windows.startupTask` extension (`TaskId="AnchorStartupTask"`, `Enabled="false"`), and
   `Services/StartupService.cs` enables it through `StartupTask.RequestEnableAsync` when packaged.
   The two must stay in sync — the `TaskId` string appears in both files.

   Consequence worth knowing before you test: a user can disable the entry in **Task Manager ▸
   Startup apps**, and Windows then refuses to let the app re-enable it. Anchor detects that
   (`StartupTaskState.DisabledByUser`), puts the Settings switch back and shows a note pointing at
   Task Manager. That is the required behavior, not a bug.

9. **The GitHub update check does not exist in the packaged build.** `Services/UpdateService.cs`
   is opt-in and off by default in the portable zip; when packaged, `DockManager.UpdateChecksSupported`
   is false, the startup check never runs and the whole Settings card is collapsed. The Store
   updates a packaged app itself, so a banner sending users to a GitHub release would both be
   redundant and route them to a build distributed outside the Store — the pattern Store review
   looks for. Don't "fix" this by re-enabling it.

10. **Which build am I in?** `Services/PackagedRuntime.cs` answers it once, via
    `GetCurrentPackageFullName`. Anything that has to differ between the two forms should go
    through it rather than re-deriving package identity.

---

## 3. Reserve the app name and get your identity (Partner Center) — done

`src/Anchor/Package.appxmanifest` already carries the **real** values from this repo's Partner
Center product:

| Manifest field | Value |
|---|---|
| `Package/Identity/Name` | `44492ajaykontham.AnchorDock` |
| `Package/Identity/Publisher` | `CN=93C75305-77D7-448E-B1C4-591A0E9665E1` |
| `Package/Properties/PublisherDisplayName` | `ajaykontham` |
| `Package/Properties/DisplayName` | `Anchor Dock` — must equal the **reserved name** |

`Applications/Application/uap:VisualElements@DisplayName` is also `Anchor Dock`, deliberately: it
is what the Start menu and the installed-apps list show, and Store policy 10.1.1 wants the app's
metadata to say the same thing everywhere. The app's own UI still calls itself "Anchor".

> If you ever start a fresh product, these come from **Product management → Product identity** and
> must be copied **verbatim** — mismatched identity is the #1 upload rejection. Associating the
> project from Visual Studio (**Project → Publish → Associate App with the Store…**) fills them in.

---

## 4. MSIX packaging (already implemented — Option A)

### What's already in the repo

`Anchor.csproj` gates the packaging bits behind the `StorePackage` MSBuild property, so an
everyday `dotnet build`/`dotnet publish` is unaffected:

```xml
<!-- In Anchor.csproj -->
<PropertyGroup Condition="'$(StorePackage)' == 'true'">
  <WindowsPackageType>MSIX</WindowsPackageType>
  <EnableMsixTooling>true</EnableMsixTooling>
  <!-- Store re-signs your package; no personal code-signing cert needed for upload. -->
  <AppxPackageSigningEnabled>false</AppxPackageSigningEnabled>
  <GenerateAppInstallerFile>false</GenerateAppInstallerFile>
  <AppxBundle>Always</AppxBundle>
  <AppxAutoIncrementPackageRevision>false</AppxAutoIncrementPackageRevision>
  <UapAppxPackageBuildMode>StoreUpload</UapAppxPackageBuildMode>
  <!-- See the note on symbols below. -->
  <AppxSymbolPackageEnabled Condition="'$(AppxSymbolPackageEnabled)' == ''">false</AppxSymbolPackageEnabled>
</PropertyGroup>

<ItemGroup Condition="'$(StorePackage)' == 'true'">
  <AppxManifest Include="Package.appxmanifest">
    <SubType>Designer</SubType>
  </AppxManifest>
</ItemGroup>
```

(`Images\**\*.png` is included unconditionally, not just for Store builds — the About page renders
the logo out of the app folder via `ms-appx:///Images/…` in the unpackaged build too.)

`src/Anchor/Package.appxmanifest` (see §4.3) and `src/Anchor/Images/*.png` (see §4.2) already
exist next to `Anchor.csproj`. `dotnet build src\Anchor\Anchor.csproj -c Release -p:Platform=x64
-p:StorePackage=true` was run against this configuration and **produces a real
`AppPackages\Anchor_<version>_Test\Anchor_<version>_x64.msix`** whose embedded `AppxManifest.xml`
carries the identity, the `runFullTrust` capability and the `windows.startupTask` extension. The
`.msixupload` bundling step still needs MSBuild/Visual Studio (§5).

> **Symbols are off by default.** Generating the `.appxsym` needs `mspdbcmf.exe`, which ships with
> Visual Studio's C++ tooling. When it is missing the MSIX targets don't degrade gracefully: they
> build the package and *then* fail the build (`MSB6011`), so `-p:StorePackage=true` never
> completes. Symbols are optional for a Partner Center upload — they only feed Store crash
> analytics — so `AppxSymbolPackageEnabled` defaults to `false`. On a machine with the **Desktop
> development with C++** workload, pass `-p:AppxSymbolPackageEnabled=true` to include them.

> **`MaxVersionTested` comes from the csproj, not the manifest.** The packaging targets overwrite
> the manifest's value from `$(TargetPlatformVersion)`, i.e. the `TargetFramework`
> (`net10.0-windows10.0.26100.0`). If you lower the TFM, the shipped manifest quietly claims the
> app was only tested that far back. `TargetPlatformMinVersion` (10.0.17763.0) is what actually
> sets the install floor, and is separate.

### Option B — Separate packaging project (`.wapproj`) — not implemented here

If you'd rather the app project never mention MSIX at all, you can instead revert
`Anchor.csproj`'s `StorePackage`-gated blocks and, in Visual Studio: **Solution → Add → New
Project → "Windows Application Packaging Project"** (name it e.g. `Anchor.Package`), set its
**Application** reference to `Anchor`, and move `Package.appxmanifest`/`Images\` there. This
yields two outputs from one solution: the unpackaged utility and a separate Store MSIX project.
The `.wapproj` builds with MSBuild/VS (not the .NET CLI), and can be added to `Anchor.slnx`.

### 4.1 Which option this repo picked

**Option A** — one project, MSIX gated behind `-p:StorePackage=true`. It keeps the change surface
small (no second project to maintain) while leaving `dotnet build`/`dotnet publish` exactly as
unpackaged as before. Switch to Option B if the packaging properties in `Anchor.csproj` ever feel
like they're getting in the way of the plain-utility build.

### 4.2 Visual assets — already generated

`src/Anchor/Images/` contains a full scaled set generated programmatically from the 500×500
`docs/anchor.png` (see the git history for the generation script if you need to regenerate them
from a different source):

- `Square44x44Logo.png` + `.scale-200.png` + `.targetsize-{16,24,32,48,256}.png` (app list /
  taskbar)
- `Square150x150Logo.png` + `.scale-200.png` (medium tile)
- `StoreLogo.png` (50×50, used by the Store/Install dialog)
- `Wide310x150Logo.png`, `Square71x71Logo.png`, `Square310x310Logo.png` (tiles)
- `SplashScreen.png` (620×300)

The non-square `Wide310x150Logo.png`/`SplashScreen.png` letterbox the (square) source image on a
flat fill color sampled from the source's average color, rather than stretching or cropping it.
If you commission a proper wide/splash-specific design later, just overwrite these two files —
nothing else needs to change.

Partner Center's **listing** also needs a **≥300×300 Store logo** and at least one **screenshot**
(1366×768 or 1920×1080 works well) — `docs/dock.png` is a good starting screenshot, or a fresh
screenshot of the app running under its new name.

### 4.3 `Package.appxmanifest` — complete

Read [`src/Anchor/Package.appxmanifest`](../src/Anchor/Package.appxmanifest) itself rather than a
copy here; it is commented in place and a duplicate in this file only drifts. The parts that carry
compliance weight, and why:

| Element | Why it is the way it is |
|---|---|
| `Identity` / `PublisherDisplayName` | Real Partner Center values (§3). Verbatim, or the upload is rejected. |
| `Properties/DisplayName` = `Anchor Dock` | Must equal the **reserved name**. |
| `VisualElements@DisplayName` = `Anchor Dock` | Start menu / installed-apps list. Kept equal to the above for policy 10.1.1. |
| `VisualElements@Description` | Deliberately does **not** say "Windows 11": `MinVersion` admits Windows 10 1809, and 10.1.1 requires metadata to match what the app actually supports. If you would rather market it as Windows-11-only, raise `MinVersion` to `10.0.22000.0` instead of re-adding the claim. |
| `Resources` = `en-US` only | The app ships eight UI languages, but each language *declared here* is one the **Store listing** must also be translated into (policy 10.7). One declared language, one listing to write. |
| `windows.startupTask` extension | The only autostart mechanism that works when packaged — see §2.8. |
| `rescap:Capability runFullTrust` | The one restricted capability, justified at §7.2. `broadFileSystemAccess` was denied on review and must not come back. |

> **Store version rule:** the `Version` **revision must be 0** (`Major.Minor.Build.0`). The Store
> reserves the 4th field. Bump `Build` (or `Minor`) for each submission, and keep
> `<Version>`/`<FileVersion>`/`<AssemblyVersion>` in `Anchor.csproj` in step — Settings ▸ About
> reads the assembly version.

---

## 5. Build the package

### Self-contained vs framework-dependent
Anchor currently sets `WindowsAppSDKSelfContained=true` and `SelfContained=true`. That is valid
for the Store and means users don't need the Windows App Runtime installed — at the cost of a
larger package. Alternatively, make it framework-dependent (remove the self-contained flags); the
Store distributes the Windows App SDK framework package as a dependency automatically. Either
works; self-contained is the safer default and is what the repo already uses.

### Build with MSBuild (Option A)

Three steps: restore once, build each architecture, then bundle the two by hand. The whole thing
was run against this repo and produced the artifacts named below.

```powershell
$msbuild = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"

# 1. Restore ONCE. Both RIDs come from <RuntimeIdentifiers> in Anchor.csproj — see the warning below.
& $msbuild src\Anchor\Anchor.csproj /t:Restore /p:Configuration=Release /p:Platform=x64 /p:StorePackage=true

# 2. Build each architecture. Each pass emits one .msix under
#    src\Anchor\bin\<Platform>\Release\<tfm>\win-<arch>\Upload\Anchor_<version>\.
foreach ($p in 'x64', 'ARM64') {
  & $msbuild src\Anchor\Anchor.csproj `
    /p:Configuration=Release `
    /p:Platform=$p `
    /p:StorePackage=true `
    /p:UapAppxPackageBuildMode=StoreUpload `
    /p:AppxPackageSigningEnabled=false
}

# 3. Bundle both into the single artifact Partner Center takes. Copy the two .msix from the Upload
#    folders into one staging directory first — MakeAppx bundles a directory, not a file list.
$makeappx = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.6901\bin\10.0.26100.0\x64\MakeAppx.exe"
& $makeappx bundle /d <staging-dir> /p Anchor_1.0.0.0_x64_arm64.msixbundle /bv 1.0.0.0 /o
```

A `.msixupload` is just a zip containing that `.msixbundle`; Partner Center accepts either, and it
equally accepts the two per-architecture `.msix` files uploaded separately into one submission.

> **`AppxBundlePlatforms` does nothing here — don't rely on it.** The usual
> `-p:AppxBundlePlatforms="x64|arm64"` one-liner is a *packaging-project* (`.wapproj`) idiom. Under
> Windows App SDK 1.8's single-project MSIX targets it is silently ignored: the build runs
> `GenerateUploadMsixPackage`, emits a `.msix` for the one `$(Platform)` it was given, and never
> runs a bundle target — no ARM64 pass, no `.msixupload`, and **no warning**. It looks like a clean
> success. Hence the explicit per-architecture loop and the manual `MakeAppx bundle` above.

> **Restore once, not per platform.** Both architectures share this project's single
> `obj\project.assets.json`, so a per-platform restore makes the second overwrite the first and the
> next build fails with `NETSDK1047` ("Assets file doesn't have a target for …/win-x64"). That is
> what `<RuntimeIdentifiers>win-x64;win-arm64</RuntimeIdentifiers>` in `Anchor.csproj` is for — it
> is restore-time only and does not change what any single build compiles.

`dotnet build` with the same properties gets you a valid, inspectable `.msix` (useful for checking
the merged manifest, and for sideload testing once signed) but not the bundle — `MakeAppx` is a
Windows SDK tool and the MSIX toolchain is Windows-only throughout.

### Sideload the built package to test it
The packaged behavior — the startup task, the redirected config folder, the collapsed update card —
can only be verified from an installed package, not from `bin\`. To install locally you need the
package signed with a certificate your machine trusts:

```powershell
# One-off: a self-signed cert whose subject matches Package/Identity/Publisher exactly.
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=93C75305-77D7-448E-B1C4-591A0E9665E1" `
  -KeyUsage DigitalSignature -FriendlyName "Anchor sideload" -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
# Export it and trust it under Local Machine ▸ Trusted People, then:
signtool sign /fd SHA256 /a /f anchor-test.pfx /p <password> Anchor_1.0.0.0_x64.msix
Add-AppxPackage .\Anchor_1.0.0.0_x64.msix
```

This certificate is for **local testing only** — never ship it. Uninstall with
`Get-AppxPackage *AnchorDock* | Remove-AppxPackage`.

### Build from Visual Studio (Option A or B)
Right-click the app (A) or the packaging project (B) → **Publish → Create App Packages… →
Microsoft Store using <your reserved name>** → choose architectures → **Create**. VS offers to
run **WACK** at the end (§6). This is the most reliable path the first time.

> **Signing note:** packages destined for the Store do **not** need your own code-signing
> certificate. Microsoft signs them with your Store identity on ingestion. You only need a
> (self-signed) cert if you want to **sideload** the MSIX for local testing.

---

## 6. Validate with the Windows App Certification Kit (WACK)

Run WACK before you upload — it catches most certification failures locally.

- From VS: the *Create App Packages* wizard → **Launch Windows App Certification Kit**.
- Standalone: **Start → "Windows App Cert Kit"** → *Validate a Store app* → point it at the built
  package.

Fix anything it flags (common ones: missing logo scales, debug/`IsDevelopmentMode` build,
unsupported APIs). A green WACK report is a strong predictor of passing Store certification.

---

## 7. Create the Store submission (Partner Center)

In your reserved product, create a **new submission** and complete each section:

1. **Packages** — upload the `.msixupload`. Partner Center validates identity/version and shows
   the supported architectures.
2. **Properties** — pick a **category** (e.g. *Utilities & tools*), declare what the app does.
   Because you declared a restricted capability, you'll be prompted to **explain why**
   `runFullTrust` is needed. Suggested wording:
   > *"Anchor is a launcher/dock. `runFullTrust` is required to start user-pinned applications,
   > files, and links via ShellExecute, which is not possible from an AppContainer-sandboxed
   > process. All icon resolution and file access use plain Win32 APIs available to any
   > full-trust process; the app declares no other restricted capability."*
   >
   > Also set **Support Info** on this page (or under **Use different details for this app →
   > Support contact info**) to a valid support contact or developer website URL — a submission
   > was previously rejected solely for leaving this blank.
3. **Age ratings** — complete the **IARC** questionnaire (a few minutes). Anchor has no mature
   content, so it will rate low, but the questionnaire is mandatory.
4. **Store listing** — description, at least one **screenshot** (use/adapt `docs/dock.png`), the
   ≥300×300 **Store logo**, feature list, and search terms (**max seven**, policy 10.1.3). Reuse
   copy from `README.md`, but keep the product *name* free of descriptive/marketing text (10.1.1).
5. **Privacy policy — mandatory, not optional.** Policy 10.5.1 says outright that "product types
   that inherently have access to Personal Information must always have privacy policies… these
   include, but are not limited to, **Desktop Bridge and Win32 products**." Anchor is a full-trust
   Win32 product, so the field must be filled in whatever the app actually collects (which is
   nothing). [`docs/privacy-policy.md`](privacy-policy.md) is written for this; paste its public
   URL:

   ```
   https://github.com/framedparadox/anchor-dock/blob/main/docs/privacy-policy.md
   ```

   The same document is linked in-app from **Settings ▸ About**. If you later move it to a
   project website, update the `NavigateUri` in `SettingsWindow.xaml` in the same change.
6. **Pricing and availability** — Free (recommended) and your target **markets**.

Then **Submit to the Store**. Certification typically completes within hours to ~3 business days;
you'll get an email on pass/fail with a report. On pass, choose to publish immediately or on a
date.

---

## 8. Updating the app

1. Increase the manifest `Version` (keep the **revision at 0**, e.g. `1.0.0.0 → 1.1.0.0`).
2. Rebuild the `.msixupload` (§5) and run WACK (§6).
3. In Partner Center, create a **new submission**, upload the new package, adjust the listing if
   needed, and submit. The Store delivers the update to installed users automatically.

---

## 9. Optional: automate publishing in CI

- **Microsoft Store Developer CLI (`msstore`)** — install with `winget install Microsoft.MsStoreCli`.
  Use `msstore reconfigure` (with an Azure AD app registered for the Partner Center Submission
  API) then `msstore publish` to push a built package. It also has a GitHub Action.
- **Partner Center Submission API / StoreBroker** — PowerShell module for scripted submissions.
- **GitHub Actions sketch** (build on Windows, then publish):
  ```yaml
  jobs:
    store:
      runs-on: windows-latest
      steps:
        - uses: actions/checkout@v4
        - uses: actions/setup-dotnet@v4
          with: { dotnet-version: '10.0.x' }
        - name: Build MSIX
          run: msbuild src/Anchor/Anchor.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:StorePackage=true /p:UapAppxPackageBuildMode=StoreUpload
        - name: Publish with msstore
          run: |
            msstore reconfigure --tenantId ${{ secrets.AAD_TENANT }} --clientId ${{ secrets.AAD_CLIENT }} --clientSecret ${{ secrets.AAD_SECRET }} --sellerId ${{ secrets.SELLER_ID }}
            msstore publish (Get-ChildItem -Recurse -Filter *.msixupload | Select-Object -First 1).FullName
  ```
  Store all credentials as encrypted repository **secrets**; never commit them.

---

## 10. Option C — Publish the existing EXE without MSIX

The Store also accepts traditional Win32 apps distributed as an **EXE or MSI installer** (the
"bring your own installer" path). This lets you ship Anchor's current **self-contained
unpackaged** build with no manifest/repackaging:

1. Build the app as today: `dotnet build src/Anchor/Anchor.csproj -c Release -p:Platform=x64`.
2. Wrap the output folder in an installer (e.g. **WiX/MSI**, **Inno Setup**, or **Squirrel**),
   because the Store's app-install experience drives an installer, not a loose folder.
3. In Partner Center, create an **EXE/MSI app** product, provide the installer URL/binary, and
   fill in the **install/uninstall/silent** command lines and a **custom app ID**.
4. Complete the same listing/age-rating/privacy steps as §7.

Trade-offs: no automatic MSIX updates (you manage versioning/updates through your installer), and
you own the installer's clean-uninstall behavior. Upside: minimal change to the app and it stays
a plain desktop utility.

---

## 11. Pre-submission checklist

**In the repo — done**

- [x] Partner Center account active; app **name reserved**; `Identity Name` / `Publisher` /
      `PublisherDisplayName` are the real values, verbatim (§3).
- [x] Manifest `Version` revision is **0** (`1.0.0.0`).
- [x] Visual assets generated (`Square44x44`, `Square150x150`, `StoreLogo`, splash) — §4.2.
- [x] Only `runFullTrust` declared — `broadFileSystemAccess` was dropped after being denied on
      review; `IconService` resolves all icons via Win32 (§2.3). Justification wording is in §7.2.
- [x] `Properties/DisplayName`, `VisualElements@DisplayName` and the reserved name all agree
      ("Anchor Dock"); the description no longer claims a Windows version `MinVersion` doesn't
      match (§4.3).
- [x] "Start with Windows" uses `windows.startupTask` when packaged, not the Run key, so it
      actually works after install (§2.8).
- [x] The GitHub update check is compiled out of the packaged build (§2.9).
- [x] A **privacy policy** exists at `docs/privacy-policy.md` and is linked from Settings ▸ About
      (§7.5) — required for a Win32 product by policy 10.5.1.
- [x] `-p:StorePackage=true` completes and emits a `.msix` with the expected manifest (§4).

**Before you press submit — needs you, or a Windows box with VS**

- [ ] Sideload the signed package (§5) and smoke-test the *packaged* behavior specifically:
      Settings ▸ General ▸ **Start with Windows** survives a reboot and appears in Task Manager ▸
      Startup apps; the **update card is absent**; config saves and reloads (it lives under
      `…\Packages\<PackageFamilyName>\LocalCache\Roaming\Anchor` when packaged, not
      `%AppData%\Anchor` — an existing unpackaged `dock.json` will **not** be picked up).
- [ ] Uninstall the sideloaded package and confirm nothing is left behind (policy 10.2.7).
- [x] `.msixupload` produced in **StoreUpload** mode for **x64 and ARM64** (§5) — built and its
      bundle manifest read back: identity `44492ajaykontham.AnchorDock`, publisher
      `CN=93C75305-…`, version `1.0.0.0`, payload packages `application x64` + `application arm64`.
- [ ] **WACK passes.** Installed at
      `C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe`; run it from an
      **elevated** prompt against the bundle:
      `appcert.exe test -appxpackagepath <…>.msixbundle -reportoutputpath wack-report.xml`
- [ ] Partner Center **Properties → Support Info**: valid support contact or developer website URL
      (a prior submission was rejected for this alone).
- [ ] Partner Center **Properties**: category (*Utilities & tools*), `runFullTrust` justification
      pasted (§7.2).
- [ ] Listing complete: description, ≥1 screenshot, ≥300×300 logo, **privacy policy URL**,
      ≤7 search terms, age rating (IARC), markets, price.
- [ ] Submitted, and certification email received.

---

### References
- Publish a Windows app to the Store: <https://learn.microsoft.com/windows/apps/publish/>
- Package a WinUI 3 / desktop app (MSIX): <https://learn.microsoft.com/windows/apps/package-and-deploy/>
- App capability declarations (incl. restricted): <https://learn.microsoft.com/windows/uwp/packaging/app-capability-declarations>
- Microsoft Store policies: <https://learn.microsoft.com/windows/apps/publish/store-policies>
- Windows App Certification Kit: <https://learn.microsoft.com/windows/win32/win_cert/windows-app-certification-kit>
- Microsoft Store Developer CLI: <https://learn.microsoft.com/windows/apps/publish/msstore-dev-cli/overview>

> Reminder on what has and hasn't been proven here. The full §5 sequence — restore, an x64 build,
> an ARM64 build, and `MakeAppx bundle` — was run against this repo's actual configuration and
> produced `AppPackages\Anchor_1.0.0.0_StoreUpload\Anchor_1.0.0.0_x64_arm64.msixbundle` (and the
> equivalent `.msixupload`). Read back from the built artifacts, not the sources: each `.msix`
> carries the real identity with the right `ProcessorArchitecture`, `runFullTrust`, the
> `windows.startupTask` extension, `MinVersion=10.0.17763.0` / `MaxVersionTested=10.0.26100.0`, and
> a payload containing `Anchor.exe`, `resources.pri` and every `Images\` tile; the bundle manifest
> lists both architectures at `1.0.0.0`. **Not** exercised: signing, sideload install, WACK, and the
> packaged-only runtime behavior (startup task, redirected config) — those need an elevated prompt
> and an actual install, and are the remaining boxes in §11's second list. Symbols were not shipped
> (`.appxsym` is optional, feeds only Store crash analytics; add with
> `-p:AppxSymbolPackageEnabled=true`). The MSIX toolchain is Windows-only throughout.
