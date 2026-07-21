<#
.SYNOPSIS
    Keeps the uk/pl translation satellites (Strings.uk.resx, Strings.pl.resx) topped up against
    the neutral Strings.resx, and reports how much of each is actually translated.

.DESCRIPTION
    Strings.resx (English, neutral) is the source of truth for which keys exist. The satellites
    only need their <value> translated — no code changes, ever (see
    docs/plans/localization-implementation-plan.md). This script never overwrites a value someone
    already translated; it only adds keys a satellite is missing, as:

        <value>[TODO] <english text></value>

    so an untranslated string is visibly "[TODO] ..." both in the running app and in a grep,
    instead of silently falling back to English and looking done.

.PARAMETER Sync
    Add any neutral key missing from a satellite, as [TODO]-prefixed English. Idempotent — safe
    to re-run after new keys are added to Strings.resx; existing translations are never touched.

.PARAMETER Status
    Report per-language: total keys, translated, still-[TODO], and any missing entirely. This is
    the default action when no switch is given.

.EXAMPLE
    ./scripts/translations.ps1 -Sync
    ./scripts/translations.ps1 -Status
#>

param(
    [switch]$Sync,
    [switch]$Status
)

$ErrorActionPreference = "Stop"

if (-not $Sync -and -not $Status) {
    $Status = $true
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$resourcesDir = Join-Path $repoRoot "src/AdaVoice.App/Resources"
$neutralPath = Join-Path $resourcesDir "Strings.resx"
$languages = @("uk", "pl")
$todoPrefix = "[TODO] "
$xmlNs = "http://www.w3.org/XML/1998/namespace"

function Get-ResxDocument {
    param([string]$Path)

    # [xml](Get-Content -Raw ...) trips over the BOM on these files (PowerShell keeps it as a
    # literal leading character, which breaks the [xml] cast even though the file itself is
    # well-formed) — XmlDocument.Load(path) reads the file directly and handles the BOM correctly.
    $doc = New-Object System.Xml.XmlDocument
    $doc.Load($Path)
    return $doc
}

function Get-ResxEntries {
    param($Doc)

    # Ordered map of key name -> value, in document order.
    $entries = [ordered]@{}
    foreach ($node in $Doc.root.data) {
        $entries[$node.name] = $node.value
    }
    return $entries
}

function Add-ResxEntry {
    param($Doc, [string]$Name, [string]$Value)

    $data = $Doc.CreateElement("data")
    $data.SetAttribute("name", $Name) | Out-Null
    # The reserved "xml" prefix is predefined by the XML spec; CreateAttribute("xml", ...) binds
    # to it directly instead of minting a new prefix (SetAttribute(name, ns, value) picks an
    # arbitrary one, e.g. "d2p1:space", which is valid XML but not what a resx reader expects).
    $spaceAttr = $Doc.CreateAttribute("xml", "space", $xmlNs)
    $spaceAttr.Value = "preserve"
    $data.Attributes.Append($spaceAttr) | Out-Null

    $valueNode = $Doc.CreateElement("value")
    $valueNode.InnerText = $Value
    $data.AppendChild($valueNode) | Out-Null

    $Doc.root.AppendChild($data) | Out-Null
}

if (-not (Test-Path $neutralPath)) {
    throw "Neutral resx not found: $neutralPath"
}
$neutralDoc = Get-ResxDocument -Path $neutralPath
$neutralEntries = Get-ResxEntries -Doc $neutralDoc

foreach ($lang in $languages) {
    $satellitePath = Join-Path $resourcesDir "Strings.$lang.resx"
    if (-not (Test-Path $satellitePath)) {
        throw "Satellite resx not found: $satellitePath (expected one per language even if empty)"
    }

    $satelliteDoc = Get-ResxDocument -Path $satellitePath
    $satelliteEntries = Get-ResxEntries -Doc $satelliteDoc

    if ($Sync) {
        $added = 0
        foreach ($name in $neutralEntries.Keys) {
            if (-not $satelliteEntries.Contains($name)) {
                Add-ResxEntry -Doc $satelliteDoc -Name $name -Value "$todoPrefix$($neutralEntries[$name])"
                $added++
            }
        }
        if ($added -gt 0) {
            $satelliteDoc.Save($satellitePath)
            Write-Host "$lang : added $added missing key(s)."
        } else {
            Write-Host "$lang : already has every key."
        }
        # Reload so -Status (if also requested) reports the post-sync state.
        $satelliteDoc = Get-ResxDocument -Path $satellitePath
        $satelliteEntries = Get-ResxEntries -Doc $satelliteDoc
    }

    if ($Status) {
        $total = $neutralEntries.Count
        $translated = 0
        $todo = 0
        $missing = 0
        foreach ($name in $neutralEntries.Keys) {
            if (-not $satelliteEntries.Contains($name)) {
                $missing++
            } elseif ($satelliteEntries[$name].StartsWith($todoPrefix)) {
                $todo++
            } else {
                $translated++
            }
        }

        Write-Host ""
        Write-Host "$lang : $translated / $total translated, $todo pending, $missing missing"
    }
}
