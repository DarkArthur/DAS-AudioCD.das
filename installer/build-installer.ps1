<#
.SYNOPSIS
  Builds the self-contained DiscCheck app and the guided installer (Inno Setup).
.EXAMPLE
  pwsh installer/build-installer.ps1 -Version 1.0.1
#>
param(
    [Parameter(Mandatory)][string]$Version,
    [string]$Output = "artifacts",
    [switch]$SkipPublish
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$publish = Join-Path $root 'publish'
if (-not $SkipPublish) {
    if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
    dotnet publish src/DiscCheck.App -c Release -r win-x64 --self-contained true `
        -p:Version=$Version -p:PublishSingleFile=false -o $publish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }
}

$candidates = @(
    (Get-Command iscc -ErrorAction SilentlyContinue)?.Source,
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { $_ -and (Test-Path $_) }
if (-not $candidates) { throw "Inno Setup 6 not found. Install it: winget install JRSoftware.InnoSetup" }
$iscc = $candidates | Select-Object -First 1

$out = Join-Path $root $Output
New-Item -ItemType Directory -Force $out | Out-Null
& $iscc "/DAppVersion=$Version" "/DSourceDir=$publish" "/DOutputDir=$out" "/DRepoRoot=$root" "installer\DiscCheck.iss"
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

$setup = Join-Path $out "DiscCheck-Setup-$Version-win-x64.exe"
(Get-FileHash $setup -Algorithm SHA256).Hash.ToLower() + "  " + (Split-Path $setup -Leaf) | Set-Content "$setup.sha256"
Write-Host "Installer: $setup"
