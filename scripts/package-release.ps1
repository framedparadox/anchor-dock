<#
.SYNOPSIS
    Builds a self-contained Release publish of Anchor and zips it for distribution
    (GitHub Releases, winget, manual download).

.DESCRIPTION
    Produces dist\Anchor-win-x64-<version>.zip containing the published app folder, and prints
    the zip's SHA256 (needed for a winget InstallerSha256 entry — see docs/winget-deployment.md).

.PARAMETER Version
    Version string used in the output file name, e.g. "1.0.0". Defaults to the csproj's <Version>.

.EXAMPLE
    pwsh scripts/package-release.ps1
    pwsh scripts/package-release.ps1 -Version 1.1.0
#>
param(
    [string]$Version
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $repoRoot "src\Anchor\Anchor.csproj"

if (-not $Version) {
    [xml]$xml = Get-Content $csproj
    $Version = $xml.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
    if (-not $Version) { $Version = "1.0.0" }
}

$publishDir = Join-Path $repoRoot "publish\Anchor-win-x64"
$distDir = Join-Path $repoRoot "dist"
$zipPath = Join-Path $distDir "Anchor-win-x64-$Version.zip"

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

Write-Host "Publishing Anchor $Version (Release, x64, self-contained)..."
dotnet publish $csproj -c Release -p:Platform=x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Write-Host "Zipping to $zipPath..."
Compress-Archive -Path (Join-Path $publishDir '*') -DestinationPath $zipPath

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
Write-Host ""
Write-Host "Done."
Write-Host "  Zip:    $zipPath"
Write-Host "  SHA256: $hash"
Write-Host ""
Write-Host "Next: upload the zip to a GitHub Release, then fill InstallerUrl/InstallerSha256 in"
Write-Host "packaging/winget/manifests/.../Anchor.installer.yaml (see docs/winget-deployment.md)."
