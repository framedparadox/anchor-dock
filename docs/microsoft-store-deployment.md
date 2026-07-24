# Deploying DockGx to the Microsoft Store

A practical, end-to-end guide to publishing DockGx on the Microsoft Store. It is written
against this repository's actual configuration, so it also calls out the app-specific gotchas
(unpackaged build, launching arbitrary programs, arbitrary-path icon reading, config in
`%AppData%`, and the missing app icon) that a generic guide would skip.

> TL;DR: DockGx is currently an **unpackaged** app (`WindowsPackageType=None` in
> `src/DockGx/DockGx.csproj`). The Store's primary path wants an **MSIX** package, so the main
> work is (1) adding MSIX packaging + a manifest + visual assets, and (2) a Partner Center
> submission. A no-repackaging alternative (ship the existing `.exe` via the Store's EXE/MSI
> path) is covered as **Option C**.

---

## 0. The key decision: how DockGx reaches the Store

| Option | What you ship | Keeps unpackaged build? | Effort | Recommended when |
|--------|---------------|--------------------------|--------|------------------|
| **A. Single-project MSIX** | One `.msixupload` from the app project | No (project becomes packaged, or use a `Store` build config) | Low | You're happy making MSIX the primary format |
| **B. Packaging project (`.wapproj`)** | A `.msixupload` from a separate project that references DockGx | **Yes** | Medium | You want to keep shipping the unpackaged utility *and* publish to the Store |
| **C. Bring-your-own EXE/MSI** | The current self-contained `.exe` wrapped in an installer | Yes | Low–Medium | You want minimal changes and are OK self-hosting/authoring an installer + updates |

Because DockGx is deliberately "a normal desktop utility with no MSIX" (see the README), **Option
B preserves that story** while adding a Store artifact. **Option A** is simplest if you're fine
with MSIX being the shipping format. Both A and B produce a real MSIX and get the best Store
experience (automatic updates, clean install/uninstall). Steps 3–8 below apply to A and B
identically; Option C is described separately at the end.

---

## 1. Prerequisites

### Accounts & policy
- **A Microsoft Partner Center developer account.** Register at
  <https://partner.microsoft.com/dashboard/registration>. There is a one-time registration fee
  (historically **US$19** for individuals and **US$99** for companies — check current pricing).
- Read the **[Microsoft Store Policies](https://learn.microsoft.com/windows/apps/publish/store-policies)**.
  Two are directly relevant to DockGx:
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

These are the things about DockGx specifically that affect a Store submission.

1. **It's unpackaged today.** `WindowsPackageType=None` and `WindowsAppSDKSelfContained=true`.
   Options A/B add MSIX on top; the self-contained setting is fine for the Store (see §5).

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

4. **Config lives in `%AppData%\DockGx\dock.json`** (`Services/DockStore.cs` via
   `Environment.SpecialFolder.ApplicationData`). Under MSIX this call is **redirected** to the
   package's per-user store (`...\Packages\<PackageFamilyName>\LocalCache\Roaming\DockGx`), so it
   keeps working with no code change. Caveat: a user's existing *unpackaged* `dock.json` will
   **not** be picked up by the packaged app (different redirected location). If that migration
   matters, copy it on first run, or move to `Windows.Storage.ApplicationData.Current.LocalFolder`
   when packaged.

5. **There is no app icon or visual assets yet.** The Store **requires** tile/logo images
   (§4). You must add them; this also resolves recommendation #13 in
   `docs/design-guidelines-review.md`.

6. **Architecture is x64-only** (`<Platforms>x64</Platforms>`, `win-x64`). x64 is accepted by
   the Store and covers most PCs. To also reach Arm64 devices, add an `arm64` build and submit
   both packages in one `.msixupload` (optional).

7. **The window hides from the taskbar and Alt-Tab.** That's fine, but make sure users can still
   *find and launch* it after install: the MSIX manifest gives it a **Start-menu entry** by
   default (don't set `AppListEntry="none"`), and quitting is via the dock's gear menu.

---

## 3. Reserve the app name and get your identity (Partner Center)

1. In Partner Center, go to **Apps and games → New product → App**, and **reserve the name**
   "DockGx" (or your chosen Store name). Name reservation is what unlocks the identity values.
2. Open the product, then **Product management → Product identity**. Copy these three values —
   they must go into the manifest **verbatim**:
   - **Package/Identity/Name** (e.g. `12345YourPublisher.DockGx`)
   - **Package/Identity/Publisher** (e.g. `CN=ABCDEF01-2345-6789-ABCD-EF0123456789`)
   - **Package/Properties/PublisherDisplayName** (your account's display name)

> Mismatched identity is the #1 upload rejection. If you associate the project from Visual Studio
> (**Project → Publish → Associate App with the Store…**), VS fills these in for you.

---

## 4. Add MSIX packaging

### Option A — Single-project MSIX

Add a `Package.appxmanifest` (see §4.3) and an `Images\` folder (see §4.2) next to
`DockGx.csproj`, then enable packaging. To **keep the default `dotnet build` unpackaged** and
only produce MSIX on demand, gate it behind a property instead of hard-flipping the project:

```xml
<!-- In DockGx.csproj -->
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
  <Content Include="Images\**\*.png" />
</ItemGroup>
```

Build the package (§5) with `-p:StorePackage=true`. Everyday `dotnet build` stays unpackaged.

### Option B — Separate packaging project (`.wapproj`) — preserves the unpackaged app

Keep `DockGx.csproj` exactly as it is. In Visual Studio: **Solution → Add → New Project →
"Windows Application Packaging Project"** (name it e.g. `DockGx.Package`), set its **Application**
reference to `DockGx`, and add the manifest/assets to the packaging project instead. This yields
two outputs from one solution: the unpackaged utility (as today) and a Store MSIX. The
`.wapproj` builds with MSBuild/VS (not the .NET CLI), and can be added to `DockGx.slnx`.

### 4.1 Which option to pick for this repo

Option **B** matches DockGx's "unpackaged utility" identity best. Choose **A** if you'd rather
have one project and are fine with MSIX being the shipping format.

### 4.2 Visual assets (required)

The Store rejects a package with no logos. Generate a full scaled set from a single 1024×1024
source with the **Visual Studio Manifest Designer → Visual Assets → Asset Generator** (open
`Package.appxmanifest` → *Visual Assets* → pick a source PNG → *Generate*). It produces, into
`Images\`, at least:

- `Square44x44Logo.png` (app list / taskbar) — with all scale variants and target sizes
- `Square150x150Logo.png` (medium tile)
- `StoreLogo.png` (50×50, used by the Store/Install dialog)
- `Wide310x150Logo.png`, `Square71x71Logo.png`, `Square310x310Logo.png` (optional tiles)
- `SplashScreen.png` (620×300)

Partner Center's **listing** also needs a **≥300×300 Store logo** and at least one **screenshot**
(1366×768 or 1920×1080 works well) — `docs/dock.png` is a good starting screenshot.

### 4.3 `Package.appxmanifest` (full example)

Place this next to `DockGx.csproj`. Replace the `Identity`/`PublisherDisplayName` values with the
ones from §3.

```xml
<?xml version="1.0" encoding="utf-8"?>
<Package
  xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
  xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
  xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
  IgnorableNamespaces="uap rescap">

  <!-- These three MUST match Partner Center → Product identity exactly. -->
  <Identity Name="12345YourPublisher.DockGx"
            Publisher="CN=ABCDEF01-2345-6789-ABCD-EF0123456789"
            Version="1.0.0.0" />

  <Properties>
    <DisplayName>DockGx</DisplayName>
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
    <Application Id="App" Executable="DockGx.exe" EntryPoint="Windows.FullTrustApplication">
      <uap:VisualElements
        DisplayName="DockGx"
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
DockGx currently sets `WindowsAppSDKSelfContained=true` and `SelfContained=true`. That is valid
for the Store and means users don't need the Windows App Runtime installed — at the cost of a
larger package. Alternatively, make it framework-dependent (remove the self-contained flags); the
Store distributes the Windows App SDK framework package as a dependency automatically. Either
works; self-contained is the safer default and is what the repo already uses.

### Build with MSBuild (Option A)
```powershell
# Restore, then produce a Store-signable MSIX for x64.
msbuild src\DockGx\DockGx.csproj `
  /restore `
  /p:Configuration=Release `
  /p:Platform=x64 `
  /p:StorePackage=true `
  /p:UapAppxPackageBuildMode=StoreUpload `
  /p:AppxPackageSigningEnabled=false
```
The output `.msixupload` lands under `src\DockGx\AppPackages\`. `StoreUpload` mode bundles the
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
   > *"DockGx is a launcher/dock. `runFullTrust` is required to start user-pinned applications,
   > files, and links via ShellExecute. `broadFileSystemAccess` is required to read the shell
   > icons of user-pinned items located anywhere on the file system. The app does not read file
   > contents; it only resolves icons and launches items the user explicitly pinned."*
3. **Age ratings** — complete the **IARC** questionnaire (a few minutes). DockGx has no mature
   content, so it will rate low, but the questionnaire is mandatory.
4. **Store listing** — description, at least one **screenshot** (use/adapt `docs/dock.png`), the
   ≥300×300 **Store logo**, feature list, and search terms. Reuse copy from `README.md`.
5. **Privacy policy** — provide a **privacy policy URL**. This is required whenever the app can
   access the network or personal data; DockGx opens user-supplied web links, so include one even
   if it simply states that DockGx stores its configuration locally and collects no personal data.
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
          run: msbuild src/DockGx/DockGx.csproj /restore /p:Configuration=Release /p:Platform=x64 /p:StorePackage=true /p:UapAppxPackageBuildMode=StoreUpload
        - name: Publish with msstore
          run: |
            msstore reconfigure --tenantId ${{ secrets.AAD_TENANT }} --clientId ${{ secrets.AAD_CLIENT }} --clientSecret ${{ secrets.AAD_SECRET }} --sellerId ${{ secrets.SELLER_ID }}
            msstore publish (Get-ChildItem -Recurse -Filter *.msixupload | Select-Object -First 1).FullName
  ```
  Store all credentials as encrypted repository **secrets**; never commit them.

---

## 10. Option C — Publish the existing EXE without MSIX

The Store also accepts traditional Win32 apps distributed as an **EXE or MSI installer** (the
"bring your own installer" path). This lets you ship DockGx's current **self-contained
unpackaged** build with no manifest/repackaging:

1. Build the app as today: `dotnet build src/DockGx/DockGx.csproj -c Release -p:Platform=x64`.
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
- [ ] `Identity Name` / `Publisher` / `PublisherDisplayName` copied **verbatim** into the manifest.
- [ ] Manifest `Version` revision is **0**.
- [ ] Visual assets generated (`Square44x44`, `Square150x150`, `StoreLogo`, splash) — **the app
      finally has an icon**.
- [ ] `runFullTrust` **and** `broadFileSystemAccess` declared *and* justification written
      (or `IconService` refactored to Win32 so `broadFileSystemAccess` can be dropped).
- [ ] Config still saves/loads under MSIX redirection (smoke-test the packaged build).
- [ ] Package builds in **StoreUpload** mode; `.msixupload` produced.
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

> Reminder: the MSIX toolchain is Windows-only, so build and validate on Windows. The snippets
> above were written to match this repo's configuration but have not been built here; verify
> `Package.appxmanifest` values against your Partner Center identity before your first upload.
