# Anchor Dock — Microsoft Store release runbook

The operational checklist for cutting a Store build, start to finish. Run it top to bottom; every
step states what "pass" looks like so you can tell a green step from a step that merely didn't
error.

This is the *how*. [`microsoft-store-deployment.md`](microsoft-store-deployment.md) is the *why* —
it explains the packaging design, the capability justification, and the Partner Center decisions
behind these commands. Read that once; run this every release.

> **Windows-only.** The MSIX toolchain does not exist on other platforms. Every step below assumes
> a Windows machine with the tooling in §0.

---

## 0. One-time setup

Confirm each of these exists before your first run. Versions are what this runbook was last
validated against.

| Tool | Why | Check |
|---|---|---|
| .NET 10 SDK | Matches `net10.0-windows10.0.26100.0` | `dotnet --list-sdks` |
| Visual Studio 2022/18 (or standalone MSBuild) | **MSIX cannot be produced by `dotnet build`** — the packaging targets need MSBuild | `& "$env:ProgramFiles\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" -version` |
| Windows SDK build tools | Supplies `MakeAppx.exe` for the bundle step | `ls "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools"` |
| Windows App Certification Kit | §6 | `Test-Path "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe"` |

Set these once per shell; the rest of the runbook refers to them:

```powershell
$msbuild  = "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
$makeappx = "$env:USERPROFILE\.nuget\packages\microsoft.windows.sdk.buildtools\10.0.26100.6901\bin\10.0.26100.0\x64\MakeAppx.exe"
$version  = "1.1.0.0"   # must match <Version> in Anchor.csproj and Package.appxmanifest
$outdir   = "src\Anchor\AppPackages\Anchor_${version}_StoreUpload"
```

---

## 1. Version bump

Three files must agree, or the upload is rejected.

1. `src/Anchor/Anchor.csproj` — `<Version>`, `<FileVersion>`, `<AssemblyVersion>`
2. `src/Anchor/Package.appxmanifest` — `Package/Identity/@Version`
3. `CHANGELOG.md` — a section for the new version

**The 4th field must be `0`.** The Store reserves the revision field; bump `Minor` or `Build`
(`1.1.0.0 → 1.2.0.0`). Settings ▸ About reads the assembly version, so a mismatch is user-visible.

```powershell
# Pass: all three print the same Major.Minor.Build.0
Select-String -Path src\Anchor\Anchor.csproj -Pattern '<(Assembly|File)?Version>'
Select-String -Path src\Anchor\Package.appxmanifest -Pattern 'Version="'
```

---

## 2. Pre-flight compliance checks

These are the things that have historically failed a submission or silently shipped wrong. They
are fast; run them every time rather than trusting that nothing moved.

```powershell
# a. Only runFullTrust is declared. broadFileSystemAccess was DENIED on review — if it comes
#    back, stop and read microsoft-store-deployment.md §2.3 before doing anything else.
Select-String -Path src\Anchor\Package.appxmanifest -Pattern 'Capability Name'

# b. No WinRT Storage broker calls on arbitrary paths (that is what needed the denied capability).
#    Pass: 0. Icons must resolve through Win32 (SHGetFileInfo / IImageList), never StorageFile.
#    Windows.Storage.Pickers is fine and deliberately not matched here — a picker is user-invoked
#    and grants access to the file the user chose, which needs no capability.
(Get-ChildItem src\Anchor -Recurse -Filter *.cs |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } |
    Select-String -Pattern 'StorageFile|StorageFolder|KnownFolders' | Measure-Object).Count

# c. No elevation. MSIX rejects requireAdministrator; absence of trustInfo means asInvoker.
Select-String -Path src\Anchor\app.manifest -Pattern 'requestedExecutionLevel' -Quiet   # Pass: False

# d. All 14 tile assets present.
(Get-ChildItem src\Anchor\Images\*.png).Count                                    # Pass: 14
```

Also confirm by eye, once per release:

- [ ] **Privacy policy is committed and reachable on `main`.** Policy 10.5.1 makes it mandatory for
      a full-trust Win32 product. The listing links to
      `https://github.com/framedparadox/anchor-dock/blob/main/docs/privacy-policy.md`, and Settings ▸
      About links to the same URL — a 404 here fails certification.
      Check: `git ls-tree origin/main --name-only docs/privacy-policy.md`
- [ ] **The privacy policy still matches what the app does.** It currently promises exactly two
      network calls: the site's own favicon, and the DuckDuckGo icon fallback. If a release adds a
      third, the policy is now false and must be updated in the same change.
- [ ] **`MaxVersionTested` names a Windows you actually tested on.** The packaging targets overwrite
      the manifest value from `$(TargetPlatformVersion)`, so it follows the csproj TFM, not the
      manifest.

---

## 3. Build and test

```powershell
dotnet build src\Anchor\Anchor.csproj -c Release -p:Platform=x64   -warnaserror
dotnet build src\Anchor\Anchor.csproj -c Release -p:Platform=ARM64 -warnaserror
dotnet test  tests\Anchor.Tests\Anchor.Tests.csproj -c Release
```

**Pass:** both builds report `0 Warning(s), 0 Errors`, and the test run reports `Failed: 0`
(221 tests as of 1.1.0.0).

`-warnaserror` is not optional here. CI is currently the only other place this runs, and see §9 —
do not assume CI caught it.

---

## 4. Build the MSIX packages

Restore **once**, then build each architecture. Do not restore per-platform.

```powershell
& $msbuild src\Anchor\Anchor.csproj /t:Restore /p:Configuration=Release /p:Platform=x64 /p:StorePackage=true

foreach ($p in 'x64','ARM64') {
  & $msbuild src\Anchor\Anchor.csproj `
    /p:Configuration=Release /p:Platform=$p /p:StorePackage=true `
    /p:UapAppxPackageBuildMode=StoreUpload /p:AppxPackageSigningEnabled=false
  if ($LASTEXITCODE -ne 0) { throw "$p build failed" }
}
```

**Pass:** each pass emits
`src\Anchor\bin\<Platform>\Release\<tfm>\win-<arch>\Upload\Anchor_<version>\Anchor_<version>_<arch>.msix`.

Three traps, all of which look like success if you're not watching:

- **`AppxBundlePlatforms` does nothing.** It is a `.wapproj` idiom. Under single-project MSIX it is
  silently ignored — you get one architecture, no bundle, and no warning. Hence the explicit loop.
- **Restore once.** Both architectures share one `obj\project.assets.json`; a per-platform restore
  makes the second overwrite the first and the next build dies with `NETSDK1047`.
- **Rebuild after *any* source edit.** A package built before your last edit is a package that
  ships the wrong code. If you touched a file after starting the build, start over.

---

## 5. Bundle into the upload artifact

`MakeAppx` bundles a *directory*, so stage the two `.msix` first.

```powershell
Remove-Item -Recurse -Force $outdir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path "$outdir\staging" | Out-Null
Copy-Item "src\Anchor\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Upload\Anchor_$version\Anchor_${version}_x64.msix"       "$outdir\staging"
Copy-Item "src\Anchor\bin\ARM64\Release\net10.0-windows10.0.26100.0\win-arm64\Upload\Anchor_$version\Anchor_${version}_arm64.msix" "$outdir\staging"

& $makeappx bundle /d "$outdir\staging" /p "$outdir\Anchor_${version}_x64_arm64.msixbundle" /bv $version /o

# A .msixupload is just a zip containing the bundle.
Compress-Archive -Path "$outdir\Anchor_${version}_x64_arm64.msixbundle" `
                 -DestinationPath "$outdir\Anchor_${version}_x64_arm64.msixupload" -CompressionLevel Optimal
Remove-Item -Recurse -Force "$outdir\staging"
```

**Pass:** `Bundle creation succeeded`, and `$outdir` holds a `.msixbundle` (~169 MB) and a
`.msixupload` (~168 MB).

---

## 6. Verify the artifacts — read the package, not the source

The whole point of this step is that it does not trust the build. Read the values back out of what
was actually produced.

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "$outdir\Anchor_${version}_x64_arm64.msixbundle"))
$e   = $zip.Entries | Where-Object { $_.FullName -eq 'AppxMetadata/AppxBundleManifest.xml' }
$sr  = New-Object IO.StreamReader($e.Open()); $xml = [xml]$sr.ReadToEnd(); $sr.Dispose(); $zip.Dispose()
$xml.Bundle.Identity
$xml.Bundle.Packages.Package | Select-Object Architecture, Version
```

**Pass — all of these, exactly:**

| Field | Expected |
|---|---|
| `Identity/@Name` | `44492ajaykontham.AnchorDock` |
| `Identity/@Publisher` | `CN=93C75305-77D7-448E-B1C4-591A0E9665E1` |
| `Identity/@Version` | the version from §1, revision `0` |
| Payload packages | exactly two — `x64` and `arm64`, both at that version |

Then unpack one `.msix` and check the app manifest and payload:

```powershell
& $makeappx unpack /p "src\Anchor\bin\x64\Release\net10.0-windows10.0.26100.0\win-x64\Upload\Anchor_$version\Anchor_${version}_x64.msix" /d .\_verify /o /nv
$m = [xml](Get-Content .\_verify\AppxManifest.xml)

# Pass: runFullTrust, and nothing else. Filter to Element nodes — <Capabilities> carries an XML
# comment, and an unfiltered ChildNodes prints "#comment" as if it were a second capability.
$m.Package.Capabilities.ChildNodes |
    Where-Object { $_.NodeType -eq 'Element' } | ForEach-Object { $_.Name }

$m.Package.Applications.Application.EntryPoint          # Pass: Windows.FullTrustApplication
(Get-ChildItem .\_verify\Images).Count                  # Pass: 14

# Pass: the two TaskIds are identical.
$m.Package.Applications.Application.Extensions.Extension.StartupTask | Format-List TaskId, Enabled
(Select-String -Path src\Anchor\Services\StartupService.cs -Pattern 'TaskId = "(.+)"').Matches.Groups[1].Value
```

The manifest's `StartupTask/@TaskId` must equal `StartupService.TaskId` in code, which is why the
last two lines print both. A mismatch makes `StartupTask.GetAsync` throw and "Start with Windows" a
silent no-op — it will not fail the build and it will not fail WACK. This check is the only thing
that catches it before a user does.

---

## 7. WACK — Windows App Certification Kit

**Requires an elevated prompt.** Open PowerShell as Administrator; this step cannot be run from a
normal shell or from an unelevated agent session.

```powershell
& "C:\Program Files (x86)\Windows Kits\10\App Certification Kit\appcert.exe" test `
  -appxpackagepath "<absolute path>\Anchor_1.1.0.0_x64_arm64.msixbundle" `
  -reportoutputpath wack-report.xml
```

**Pass:** the report's overall result is **PASS**. Open `wack-report.xml` and read it — do not go
by exit code alone. A green WACK is the strongest local predictor of passing certification.

Warnings are acceptable; failures are not. Common ones: missing logo scales, a Debug-configuration
build, unsupported APIs.

---

## 8. Sideload smoke test — packaged behavior only

Several behaviors differ between the packaged and unpackaged builds and **cannot be tested from
`bin\`**. This step exists to catch exactly those. It needs a signed package, because Windows will
not install an unsigned one.

```powershell
# One-off: a self-signed cert whose subject matches Package/Identity/Publisher EXACTLY.
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=93C75305-77D7-448E-B1C4-591A0E9665E1" `
  -KeyUsage DigitalSignature -FriendlyName "Anchor sideload" -CertStoreLocation "Cert:\CurrentUser\My" `
  -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
# Export it, trust it under Local Machine ▸ Trusted People, then:
signtool sign /fd SHA256 /a /f anchor-test.pfx /p <password> Anchor_1.1.0.0_x64.msix
Add-AppxPackage .\Anchor_1.1.0.0_x64.msix
```

> This certificate is for local testing only. **Never ship it.** Packages destined for the Store
> need no certificate of yours — Microsoft signs them with your Store identity on ingestion.

Then verify each of these by hand:

- [ ] **Start menu entry exists** and launches the app. The dock hides from the taskbar and
      Alt-Tab, so this is the user's only way in after install.
- [ ] **Settings ▸ General ▸ Start with Windows** turns on, appears in **Task Manager ▸ Startup
      apps**, and **survives a reboot**. This is the `windows.startupTask` path, which the portable
      build never exercises — the Run key it uses instead would silently do nothing when packaged.
- [ ] **Disable the entry in Task Manager, then try to re-enable it in Settings.** Expected: the
      switch returns to off with a note pointing at Task Manager. Windows forbids the app from
      overriding a user's choice; this is correct behavior, not a bug.
- [ ] **The update card is absent** from Settings ▸ General. The Store updates the app itself, and
      a banner routing users to a GitHub release is the pattern review looks for.
- [ ] **Config saves and reloads.** When packaged it lives under
      `…\Packages\<PackageFamilyName>\LocalCache\Roaming\Anchor`, **not** `%AppData%\Anchor` — an
      existing unpackaged `dock.json` will *not* be picked up. That is expected.
- [ ] **Pin an app, a file, a folder and a web link**; confirm each launches and each shows a real
      icon. This exercises the Win32 icon path that replaced the denied-capability WinRT one.
- [ ] **Uninstall and confirm nothing is left behind** (policy 10.2.7):
      `Get-AppxPackage *AnchorDock* | Remove-AppxPackage`

---

## 9. Known gaps — read before relying on CI

- **CI does not gate this repo's active branches.** `.github/workflows/ci.yml` triggers on `push`
  and `pull_request` to a `release` branch, which does not exist locally or on the remote. Neither
  trigger can fire; `workflow_dispatch` is the only way to run it. **Treat §3 as the real gate** and
  run it locally every release until the workflow targets `main`/`develop`.
- **Symbols are not shipped.** `AppxSymbolPackageEnabled` defaults to `false` because generating the
  `.appxsym` needs `mspdbcmf.exe` from Visual Studio's C++ tooling, and the MSIX targets fail the
  whole build (`MSB6011`) when it is missing rather than skipping the step. Symbols are optional for
  upload — they only feed Store crash analytics. On a machine with the **Desktop development with
  C++** workload, add `-p:AppxSymbolPackageEnabled=true` in §4.

---

## 10. Partner Center submission

1. **Packages** — upload the `.msixupload`. Partner Center validates identity and version and
   should show both architectures.
2. **Properties** — category *Utilities & tools*, and paste the `runFullTrust` justification:
   > *Anchor is a launcher/dock. `runFullTrust` is required to start user-pinned applications,
   > files, and links via ShellExecute, which is not possible from an AppContainer-sandboxed
   > process. All icon resolution and file access use plain Win32 APIs available to any full-trust
   > process; the app declares no other restricted capability.*
3. **Support Info** — a valid support contact or developer website URL. **A submission here was
   once rejected for this field alone.** Set it under *Use different details for this app →
   Support contact info*.
4. **Age ratings** — complete the IARC questionnaire. Mandatory even though Anchor rates low.
5. **Store listing** — description, ≥1 screenshot, ≥300×300 Store logo, **privacy policy URL**,
   ≤7 search terms (policy 10.1.3). Keep the product *name* free of marketing text (10.1.1).
6. **Pricing and availability** — Free, and your target markets.

Then **Submit**. Certification typically takes hours to ~3 business days.

---

## 11. Sign-off

A release is done when every one of these is true:

- [ ] §1 versions agree across all three files, revision `0`
- [ ] §2 compliance checks pass; privacy policy live on `main` and still accurate
- [ ] §3 both architectures build `0 warning / 0 error`; all tests pass
- [ ] §4 both `.msix` produced
- [ ] §5 `.msixbundle` + `.msixupload` produced
- [ ] §6 bundle manifest read back: right identity, right version, both architectures,
      `runFullTrust` only, startup task present
- [ ] §7 **WACK reports PASS** *(needs an elevated prompt)*
- [ ] §8 **sideload smoke test done, uninstall clean** *(needs a signed package + install)*
- [ ] §10 submitted, certification email received

> §7 and §8 are the two steps that cannot be completed from an unelevated, non-interactive session.
> Everything above them can be automated; these two need a human at an Administrator prompt on a
> real desktop. **Do not submit without them** — they are the only coverage for WACK failures and
> for the packaged-only behaviors (startup task, redirected config) that no other step touches.
