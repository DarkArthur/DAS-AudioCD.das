<#
.SYNOPSIS
  Downloads the DiscCheck application (self-contained win-x64 zip, no installer) from a GitHub release,
  verifies its SHA-256 and extracts it.
.PARAMETER Version
  Release tag without the "v" (e.g. 1.1.0). Default: latest release.
.PARAMETER Destination
  Folder that will contain the "DiscCheck-<version>" subfolder. Default: Downloads of the current user.
.PARAMETER KeepZip
  Keep the downloaded zip after extraction.
.EXAMPLE
  pwsh tools/download-app.ps1
  pwsh tools/download-app.ps1 -Version 1.1.0 -Destination C:\Tools -KeepZip
.NOTES
  The application needs administrator rights at run time (optical drive access).
#>
param(
    [string]$Version,
    [string]$Destination = (Join-Path $env:USERPROFILE 'Downloads'),
    [string]$Repo = 'DarkArthur/DAS-AudioCD.das',
    [switch]$KeepZip
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) not found. Install it and run 'gh auth login'." }

$tag = if ($Version) { "v$($Version.TrimStart('v'))" } else { (gh release view --repo $Repo --json tagName --jq .tagName) }
$ver = $tag.TrimStart('v')
New-Item -ItemType Directory -Force $Destination | Out-Null
gh release download $tag --repo $Repo --pattern "DiscCheck-$ver-win-x64.zip*" --dir $Destination --clobber
if ($LASTEXITCODE -ne 0) { throw "Download failed for $tag" }

$zip = Join-Path $Destination "DiscCheck-$ver-win-x64.zip"
$expected = (Get-Content "$zip.sha256").Split(' ')[0].ToLower()
$actual = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLower()
if ($expected -ne $actual) { throw "SHA-256 mismatch for ${zip}: expected $expected, got $actual" }

$target = Join-Path $Destination "DiscCheck-$ver"
if ([IO.Directory]::Exists($target)) { [IO.Directory]::Delete($target, $true) }
Expand-Archive $zip -DestinationPath $target
if (-not $KeepZip) { [IO.File]::Delete($zip); [IO.File]::Delete("$zip.sha256") }
Write-Host "OK  $target\DiscCheck.App.exe  (SHA-256 verified)"
