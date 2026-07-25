# Publishing Anchor to winget

A practical guide to getting Anchor installable via `winget install`. Unlike the Microsoft Store
(`docs/microsoft-store-deployment.md`), winget doesn't need Partner Center, MSIX, or a
certification review — you submit a manifest (plain YAML) to Microsoft's public
[`winget-pkgs`](https://github.com/microsoft/winget-pkgs) repo, pointing at an installer you host
yourself (a GitHub Release is the usual choice).

> TL;DR: manifest templates already exist in this repo under
> `packaging/winget/manifests/f/framedparadox/Anchor/1.0.0/`, referencing
> `framedparadox.Anchor` as the package identifier. What's **left**: cut a real GitHub Release with
> `scripts/package-release.ps1`'s zip, drop the real URL/SHA256 into the installer manifest, and
> open a PR against `microsoft/winget-pkgs`.

---

## 1. How this differs from the Store

- No developer account, no fee, no restricted-capability justification, no age-rating form.
- You host the installer (GitHub Releases is simplest — free, versioned, gives a stable URL).
- Submission is a **pull request** to a public GitHub repo, reviewed by Microsoft/community bots
  and maintainers — typically merged within a few days if validation passes.
- Anchor doesn't need MSIX for this path. The templates here ship the existing **self-contained,
  unpackaged** build as a zip (winget's `InstallerType: zip` + `NestedInstallerType: portable`),
  so nothing about the app itself changes. If you'd rather ship the MSIX built in
  `docs/microsoft-store-deployment.md` instead, see §6.

## 2. Prerequisites

- A GitHub account (to host the release and open the winget-pkgs PR).
- The **winget CLI** (ships with Windows 11 / App Installer) for local validation:
  `winget validate <path-to-manifest-folder>` and `winget install --manifest <path>`.
- Optional: [`wingetcreate`](https://github.com/microsoft/winget-create)
  (`winget install Microsoft.WingetCreate`) automates updating the installer URL/hash for new
  versions instead of hand-editing YAML.

## 3. Build and host the installer

1. Run the packaging script from the repo root (PowerShell, Windows):
   ```powershell
   pwsh scripts/package-release.ps1 -Version 1.0.0
   ```
   This publishes a self-contained Release build and writes
   `dist\Anchor-win-x64-1.0.0.zip`, printing its **SHA256**.
2. Create a GitHub Release (tag `v1.0.0` to match `PackageVersion`) on
   `https://github.com/framedparadox/dock-gx/releases/new` and attach the zip as a release asset.
3. Copy the asset's download URL — GitHub Releases URLs are stable and follow the pattern
   `https://github.com/<owner>/<repo>/releases/download/<tag>/<asset-file-name>`.

## 4. Fill in the manifest templates

Edit `packaging/winget/manifests/f/framedparadox/Anchor/1.0.0/framedparadox.Anchor.installer.yaml`:

- `InstallerUrl` → the real Release asset URL from §3.
- `InstallerSha256` → the SHA256 the packaging script printed (uppercase or lowercase both work).

The other two manifest files
(`framedparadox.Anchor.yaml`, `framedparadox.Anchor.locale.en-US.yaml`) need no changes for a
first submission — review them once for accuracy (description, tags, URLs) since they're your
Store-facing copy on `winget show`.

> **Renaming the publisher/package identifier:** the templates use `framedparadox.Anchor`
> (`framedparadox` is this repo's current GitHub org/owner). If you publish under a different
> GitHub handle or organization, rename the identifier throughout all three files *and* move the
> folder to match winget-pkgs' required path convention:
> `manifests/<lowercase-first-letter-of-publisher>/<Publisher>/<Package>/<Version>/`.

## 5. Validate locally, then submit

```powershell
# From the repo root, on Windows with winget installed:
winget validate packaging\winget\manifests\f\framedparadox\Anchor\1.0.0

# Optional: actually install from the local manifest to smoke-test it end-to-end.
winget install --manifest packaging\winget\manifests\f\framedparadox\Anchor\1.0.0
```

Once validation and the local install both work:

1. Fork [`microsoft/winget-pkgs`](https://github.com/microsoft/winget-pkgs).
2. Copy `packaging/winget/manifests/f/framedparadox/Anchor/1.0.0/` into the fork at the identical
   path: `manifests/f/framedparadox/Anchor/1.0.0/`.
3. Commit, push, and open a PR against `microsoft/winget-pkgs` (the repo's PR template and
   automated checks — `winget-manifest-verification`, install/uninstall testing in a sandbox —
   walk you through the rest). Or use `wingetcreate submit` to automate steps 1–3.
4. Address any bot feedback (usually a hash mismatch, a URL that 404s, or a schema lint issue).

## 6. Updating for a new version

1. Bump the version, e.g. `1.1.0`, in all three manifest file names' parent folder
   (`.../Anchor/1.1.0/`) and inside each file's `PackageVersion`.
2. `pwsh scripts/package-release.ps1 -Version 1.1.0`, publish a new GitHub Release, update
   `InstallerUrl`/`InstallerSha256`.
3. `winget validate`, then a new PR to `winget-pkgs` (a new version folder, not an edit to
   `1.0.0/`) — or `wingetcreate update framedparadox.Anchor -v 1.1.0 -u <url> -s <path>` to
   generate the bump automatically.

## 7. Alternative: ship the MSIX instead of a zip

If you've already built the MSIX from `docs/microsoft-store-deployment.md` (§4–§5) and don't want
to maintain two installer formats, winget also accepts `InstallerType: msix` pointing at a signed
`.msix`/`.msixbundle` (self-signed is fine for winget, unlike the Store — but users must trust the
cert, so a properly signed cert from a CA is friendlier). That trades the zip/portable section in
§4 for:

```yaml
InstallerType: msix
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/framedparadox/dock-gx/releases/download/v1.0.0/Anchor_1.0.0.0_x64.msix
    InstallerSha256: <sha256 of the .msix>
    SignatureSha256: <sha256 of the certificate's public key, if signed — see winget-pkgs docs>
```

Not implemented in this repo's templates because it requires a real code-signing certificate to
be worth doing (an unsigned/self-signed MSIX gives installers a scarier trust prompt than a plain
zip). The zip/portable path in §3–§5 has no such requirement.

---

### References
- winget-pkgs contributing guide: <https://github.com/microsoft/winget-pkgs/blob/master/CONTRIBUTING.md>
- Manifest schema docs: <https://github.com/microsoft/winget-pkgs/tree/master/doc/manifest/schema>
- `wingetcreate`: <https://github.com/microsoft/winget-create>

> Reminder: `winget validate`/`winget install --manifest` are Windows-only and were not run here.
> The manifest templates were hand-written against the schema referenced above (1.9.0) — check
> the current schema version at submission time and re-validate before opening a PR.
