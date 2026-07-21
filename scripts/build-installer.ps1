<#
.SYNOPSIS
    Packages the self-contained publish output into a Windows installer (Setup.exe) using
    Inno Setup.

.DESCRIPTION
    Beta-trial packaging step (see docs/plans/production-readiness-plan.md #7 for the v1
    installer gate this is a step toward) -- turns "unzip and run the raw exe" into a normal
    double-click Setup/Uninstall experience. Per-user install, no admin/UAC prompt (see
    installer/AdaVoice.iss for why). Still unsigned, so SmartScreen still warns once on first
    run -- same as the zip build, unchanged by this script.

    Requires scripts/publish.ps1 to have been run first (this script does not publish itself
    -- it packages whatever is already in artifacts/publish/win-x64, so a stale publish would
    silently ship stale content otherwise).

    Requires the Inno Setup Compiler (ISCC.exe) installed on this machine -- a one-time,
    build-machine-only dependency. Free download: https://jrsoftware.org/isdl.php
    End users of the resulting installer never need Inno Setup themselves.

.PARAMETER Version
    Version/tag baked into the installer's file name and AppVersion, e.g. "v0.1.0-beta.2".

.EXAMPLE
    ./scripts/publish.ps1 -Version v0.1.0-beta.2
    ./scripts/build-installer.ps1 -Version v0.1.0-beta.2
#>

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repoRoot "artifacts/publish/win-x64"
$issPath = Join-Path $repoRoot "installer/AdaVoice.iss"

if (-not (Test-Path $publishDir)) {
    throw "Publish output not found at $publishDir -- run scripts/publish.ps1 first."
}

$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source,
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe") # winget's default per-user install path
) | Where-Object { $_ -and (Test-Path $_) }

$iscc = $isccCandidates | Select-Object -First 1
if (-not $iscc) {
    throw "ISCC.exe (Inno Setup Compiler) not found. Install it once from " +
        "https://jrsoftware.org/isdl.php, then re-run this script. " +
        "(End users of the built installer never need this -- it's a build-machine-only tool.)"
}

Write-Host "Building installer with $iscc ..."
& $iscc "/DMyAppVersion=$Version" $issPath
if ($LASTEXITCODE -ne 0) {
    throw "ISCC.exe failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $repoRoot "artifacts/AdaVoice-Setup-$Version.exe"
Write-Host "Done."
Write-Host "  Installer: $exePath"
