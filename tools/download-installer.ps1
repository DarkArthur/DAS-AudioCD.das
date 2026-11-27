<#
.SYNOPSIS
  Downloads the DiscCheck guided installer (setup.exe) from a GitHub release and verifies its SHA-256.
.PARAMETER Version
  Release tag without the "v" (e.g. 1.1.0). Default: latest release.
.PARAMETER Destination
  Target folder. Default: Downloads of the current user.
.EXAMPLE
  pwsh tools/download-installer.ps1
  pwsh tools/download-installer.ps1 -Version 1.1.0 -Destination C:\Temp
#>
param(
    [string]$Version,
    [string]$Destination = (Join-Path $env:USERPROFILE 'Downloads'),
    [string]$Repo = 'DarkArthur/DAS-AudioCD.das'
)
$ErrorActionPreference = 'Stop'
if (-not (Get-Command gh -ErrorAction SilentlyContinue)) { throw "GitHub CLI (gh) not found. Install it and run 'gh auth login'." }

$tag = if ($Version) { "v$($Version.TrimStart('v'))" } else { (gh release view --repo $Repo --json tagName --jq .tagName) }
New-Item -ItemType Directory -Force $Destination | Out-Null
gh release download $tag --repo $Repo --pattern 'DiscCheck-Setup-*' --dir $Destination --clobber
if ($LASTEXITCODE -ne 0) { throw "Download failed for $tag" }

$exe = Get-ChildItem $Destination -Filter "DiscCheck-Setup-$($tag.TrimStart('v'))-win-x64.exe" | Select-Object -First 1
if (-not $exe) { throw "Installer for $tag not found in $Destination" }
$expected = (Get-Content "$($exe.FullName).sha256").Split(' ')[0].ToLower()
$actual = (Get-FileHash $exe.FullName -Algorithm SHA256).Hash.ToLower()
if ($expected -ne $actual) { throw "SHA-256 mismatch for $($exe.Name): expected $expected, got $actual" }
Write-Host "OK  $($exe.FullName)  (SHA-256 verified)"
