<#
.SYNOPSIS
    Publishes AdaVoice.App as a self-contained win-x64 build and zips it for distribution.

.DESCRIPTION
    Feedback-trial packaging (see docs/plans/... beta release plan) — not the production
    installer. Self-contained so the target machine needs no .NET runtime install; a plain
    folder (not single-file) because WPF + the native WASAPI/COM interop in
    AdaVoice.Audio.Wasapi is the kind of thing single-file extraction can trip over.

.PARAMETER Version
    Optional version/tag to bake into the zip file name, e.g. "v0.1.0-beta.1".
    If omitted, the zip is named AdaVoice-win-x64.zip.

.PARAMETER OutputRoot
    Root folder (relative to the repo root) for publish output and the zip. Defaults to
    "artifacts", which is already gitignored.

.EXAMPLE
    ./scripts/publish.ps1 -Version v0.1.0-beta.1
#>

param(
    [string]$Version = "",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputRoot = "artifacts"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "src/AdaVoice.App/AdaVoice.App.csproj"
$publishDir = Join-Path $repoRoot "$OutputRoot/publish/$Runtime"

if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}

Write-Host "Publishing AdaVoice.App ($Configuration, $Runtime, self-contained)..."
dotnet publish $project -c $Configuration -r $Runtime --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$zipName = if ($Version) { "AdaVoice-$Version-$Runtime.zip" } else { "AdaVoice-$Runtime.zip" }
$zipPath = Join-Path $repoRoot "$OutputRoot/$zipName"
if (Test-Path $zipPath) {
    Remove-Item -Force $zipPath
}

Write-Host "Zipping publish output to $zipPath..."
Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath

Write-Host "Done."
Write-Host "  Publish folder: $publishDir"
Write-Host "  Zip:            $zipPath"
