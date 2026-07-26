# Deploying Anchor to the Microsoft Store

A practical, end-to-end guide to publishing Anchor on the Microsoft Store. It is written
against this repository's actual configuration, so it also calls out the app-specific gotchas
(unpackaged default build, launching arbitrary programs, arbitrary-path icon reading, config in
`%AppData%`) that a generic guide would skip.

> TL;DR: Anchor's default `dotnet build`/`dotnet publish` stays an **unpackaged** app
> (`WindowsPackageType=None` in `src/Anchor/Anchor.csproj`). **Option A (single-project MSIX) is
> already wired up** behind the `StorePackage` MSBuild property — `Package.appxmanifest` and the
> `src/Anchor/Images/` visual assets exist in this repo, generated from `docs/anchor.png`. What's
> **left** is Partner Center account setup (§3), swapping the placeholder `Identity`/
> `PublisherDisplayName` values for your real ones, and the actual submission (§7). A
> no-repackaging alternative (ship the existing `.exe` via the Store's EXE/MSI path) is covered as
> **Option C**.

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
  - **Restricted capabilities:** `runFullTrust` and `broadFileSystemAccess` (both needed here —
    see §3) require a written justification during submission.

### Tools
- **Windows 10 2004+ / Windows 11** dev machine (MSIX tooling is Windows-only).
- **Visual Studio 2022 (17.x)** with the **.NET Desktop** and **Windows App SDK / WinUI**
  workloads, or the standalone **MSBuild** + Windows SDK. Note: **MSIX packaging generally
  requires MSBuild/Visual Studio, not `dotnet build`** — the plain .NET CLI cannot emit an MSIX.
- **.NET 10 SDK** (matches `TargetFramework net10.0-windows10.0.19041.0`).
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

3. **It reads icons from arbitrary file-system paths.** `Services/IconService.cs` calls
   `StorageFile.GetFileFromPathAsync` / `StorageFolder.GetFolderFromPathAsync` on targets like
   `C:\Windows\explorer.exe` and any file/folder the user pins. Under MSIX, the WinRT `Storage`
   broker restricts arbitrary paths unless you declare **`broadFileSystemAccess`** (a restricted
   capability). Without it, icon resolution for pinned items outside the package will fail even
   though the app is full trust. Declare it and justify it, **or** refactor `IconService` to use
   Win32 icon extraction (`SHGetFileInfo` / `IShellItemImageFactory`), which is not subject to
   the WinRT broker and would let you drop the capability.

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

6. **Architecture is x64-only** (`<Platforms>x64</Platforms>`, `win-x64`). x64 is accepted by
   the Store and covers most PCs. To also reach Arm64 devices, add an `arm64` build and submit
   both packages in one `.msixupload` (optional).

7. **The window hides from the taskbar and Alt-Tab.** That's fine, but make sure users can still
   *find and launch* it after install: the MSIX manifest gives it a **Start-menu entry** by
   default (don't set `AppListEntry="none"`), and quitting is via the dock's gear menu.

---

## 3. Reserve the app name and get your identity (Partner Center)

1. In Partner Center, go to **Apps and games → New product → App**, and **reserve the name**
   "Anchor" (or your chosen Store name). Name reservation is what unlocks the identity values.
2. Open the product, then **Product management → Product identity**. Copy these three values —
   they must go into the manifest **verbatim**:
   - **Package/Identity/Name** (e.g. `12345YourPublisher.Anchor`)
   - **Package/Identity/Publisher** (e.g. `CN=ABCDEF01-2345-6789-ABCD-EF0123456789`)
   - **Package/Properties/PublisherDisplayName** (your account's display name)

> Mismatched identity is the #1 upload rejection. If you associate the project from Visual Studio
> (**Project → Publish → Associate App with the Store…**), VS fills these in for you.

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
  <AppxBundle>Never</AppxBundle>
  <AppxAutoIncrementPackageRevision>false</AppxAutoIncrementPackageRevision>
</PropertyGroup>

<ItemGroup Condition="'$(StorePackage)' == 'true'">
  <AppxManifest Include="Package.appxmanifest">
    <SubType>Designer</SubType>
  </AppxManifest>
</ItemGroup>

<ItemGroup Condition="'$(StorePackage)' == 'true'">
  <Content Include="Images\**\*.png" />
</ItemGroup>
```

`src/Anchor/Package.appxmanifest` (see §4.3) and `src/Anchor/Images/*.png` (see §4.2) already
exist next to `Anchor.csproj`. Build the package (§5) with `-p:StorePackage=true`; this was
verified to produce a correct merged `AppxManifest.xml` and package the `Images\` assets via
`dotnet build -p:StorePackage=true` (the actual `.msixupload` bundling step still needs
MSBuild/Visual Studio — see §5).

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

### 4.3 `Package.appxmanifest` — already in place, identity is a placeholder

`src/Anchor/Package.appxmanifest` matches the template below. The one thing **you must still
edit** is the `Identity`/`PublisherDisplayName` block — it currently holds placeholder values and
must be replaced with the real ones from §3 before you can upload.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <!-- PLACEHOLDER — these three MUST be replaced with Partner Center → Product identity values,
       verbatim, before upload. -->
  <Identity Name="12345YourPublisher.Anchor"
            Publisher="CN=ABCDEF01-2345-6789-ABCD-EF0123456789"
            Version="1.0.0.0" />

  <Properties>
    <DisplayName>Anchor</DisplayName>
    <PublisherDisplayName>Your Publisher Display Name</PublisherDisplayName>
    <Logo>Images\StoreLogo.png</Logo>
  </Properties>

  <Dependencies>
    <!-- Matches TargetPlatformMinVersion (10.0.17763.0) in the csproj. -->
    <TargetDeviceFamily Name="Windows.Desktop"
                        MinVersion="10.0.17763.0"
                        MaxVersionTested="10.0.26100.0" />
  </Dependencies>

  <Resources>
    <Resource Language="en-US" />
  </Resources>

  <Applications>
    <!-- Windows.FullTrustApplication = a normal full-trust desktop exe. -->
    <Application Id="App" Executable="Anchor.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="Anchor"
        Description="A floating dock for Windows 11."
        BackgroundColor="transparent"
        Square150x150Logo="Images\Square150x150Logo.png"
        Square44x44Logo="Images\Square44x44Logo.png">
        <uap:DefaultTile Wide310x150Logo="Images\Wide310x150Logo.png"
                         Square71x71Logo="Images\Square71x71Logo.png"
                         Square310x310Logo="Images\Square310x310Logo.png" />
        <uap:SplashScreen Image="Images\SplashScreen.png" />
      </uap:VisualElements>
    </Application>
  </Applications>

  <Capabilities>
    <!-- Required: the dock launches arbitrary programs (Process.Start / ShellExecute). -->
    <rescap:Capability Name="runFullTrust" />
    <!-- Required: IconService reads icons from arbitrary paths via WinRT StorageFile.
         Drop this only if you switch icon loading to Win32 (SHGetFileInfo). -->
    <rescap:Capability Name="broadFileSystemAccess" />
  </Capabilities>
</Package>
```

> **Store version rule:** the `Version` **revision must be 0** (`Major.Minor.Build.0`). The Store
> reserves the 4th field. Bump `Build` (or `Minor`) for each submission.

---

## 5. Build the package

### Self-contained vs framework-dependent
Anchor currently sets `WindowsAppSDKSelfContained=true` and `SelfContained=true`. That is valid
for the Store and means users don't need the Windows App Runtime installed — at the cost of a
larger package. Alternatively, make it framework-dependent (remove the self-contained flags); the
Store distributes the Windows App SDK framework package as a dependency automatically. Either
works; self-contained is the safer default and is what the repo already uses.

### Build with MSBuild (Option A)
```powershell
# Restore, then produce a Store-signable MSIX for x64.
msbuild src\Anchor\Anchor.csproj `
  /restore `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:StorePackage=true `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxPackageSigningEnabled=false
```
The output `.msixupload` lands under `src\Anchor\AppPackages\`. `StoreUpload` mode bundles the
symbols and is the format Partner Center expects. For multi-arch, build `x64` and `arm64` and use
`AppxBundle=Always` so both land in one `.msixupload`.

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
   Because you declared restricted capabilities, you'll be prompted to **explain why**
   `runFullTrust` and `broadFileSystemAccess` are needed. Suggested wording:
   > *"Anchor is a launcher/dock. `runFullTrust` is required to start user-pinned applications,
   > files, and links via ShellExecute. `broadFileSystemAccess` is required to read the shell
   > icons of user-pinned items located anywhere on the file system. The app does not read file
   > contents; it only resolves icons and launches items the user explicitly pinned."*
3. **Age ratings** — complete the **IARC** questionnaire (a few minutes). Anchor has no mature
   content, so it will rate low, but the questionnaire is mandatory.
4. **Store listing** — description, at least one **screenshot** (use/adapt `docs/dock.png`), the
   ≥300×300 **Store logo**, feature list, and search terms. Reuse copy from `README.md`.
5. **Privacy policy** — provide a **privacy policy URL**. This is required whenever the app can
   access the network or personal data; Anchor opens user-supplied web links, so include one even
   if it simply states that Anchor stores its configuration locally and collects no personal data.
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

- [ ] Partner Center account active; app **name reserved**.
- [ ] `Identity Name` / `Publisher` / `PublisherDisplayName` in `src/Anchor/Package.appxmanifest`
      replaced **verbatim** with your real Partner Center values (currently placeholders).
- [ ] Manifest `Version` revision is **0**.
- [x] Visual assets generated (`Square44x44`, `Square150x150`, `StoreLogo`, splash) — done, see §4.2.
- [x] `runFullTrust` **and** `broadFileSystemAccess` declared in the manifest — justification
      wording for the submission form is drafted in §7 (or refactor `IconService` to Win32 so
      `broadFileSystemAccess` can be dropped).
- [ ] Config still saves/loads under MSIX redirection (smoke-test the packaged build).
- [ ] Package builds in **StoreUpload** mode; `.msixupload` produced (needs MSBuild/VS — §5).
- [ ] **WACK passes.**
- [ ] Listing complete: description, ≥1 screenshot, ≥300×300 logo, **privacy policy URL**, age
      rating (IARC), category, markets, price.
- [ ] Submitted, and certification email received.

---

### References
- Publish a Windows app to the Store: <https://learn.microsoft.com/windows/apps/publish/>
- Package a WinUI 3 / desktop app (MSIX): <https://learn.microsoft.com/windows/apps/package-and-deploy/>
- App capability declarations (incl. restricted): <https://learn.microsoft.com/windows/uwp/packaging/app-capability-declarations>
- Microsoft Store policies: <https://learn.microsoft.com/windows/apps/publish/store-policies>
- Windows App Certification Kit: <https://learn.microsoft.com/windows/win32/win_cert/windows-app-certification-kit>
- Microsoft Store Developer CLI: <https://learn.microsoft.com/windows/apps/publish/msstore-dev-cli/overview>

> Reminder: the MSIX toolchain is Windows-only. `dotnet build src\Anchor\Anchor.csproj
> -p:StorePackage=true` was run against this repo's actual configuration and confirmed
> `Package.appxmanifest` merges correctly and `Images\*.png` package into the AppX layout; the
> full `.msixupload`/WACK/signing steps still need MSBuild or Visual Studio (§5–§6) and haven't
> been exercised end-to-end. Verify `Package.appxmanifest` identity values against your Partner
> Center product before your first upload.
