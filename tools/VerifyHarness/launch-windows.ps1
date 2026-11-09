#Requires -Version 5.1
<#
.SYNOPSIS
  Launch RimWorld with an isolated VerifyHarness profile without changing Steam
  launch options, the normal mod list, or any real save files.
.EXAMPLE
  .\tools\VerifyHarness\launch-windows.ps1 -GameExe 'D:\SteamLibrary\steamapps\common\RimWorld\RimWorldWin64.exe'
.NOTES
  Install matching RimworldStatistics and RimworldStatisticsVerifyHarness MODs
  before running. This script only enables their package IDs in the copied
  test profile; it does not download/install or modify game MOD files.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$GameExe,
    [string]$TestDataFolder = (Join-Path $env:LOCALAPPDATA 'RimworldStatistics-Verify'),
    [string]$NormalDataFolder = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios'),
    [switch]$SetupOnly,
    [switch]$Minimal,
    [string]$SaveFile,
    [switch]$UseSaveModList
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function FullPath([string]$path) {
    return [System.IO.Path]::GetFullPath($path).TrimEnd([char[]]@('\','/'))
}

if (-not (Test-Path -LiteralPath $GameExe -PathType Leaf)) {
    throw "RimWorld executable not found: $GameExe. Supply -GameExe with the installed RimWorldWin64.exe path."
}

$normal = FullPath $NormalDataFolder
$test = FullPath $TestDataFolder
$separator = [System.IO.Path]::DirectorySeparatorChar
if ([string]::Equals($test, $normal, [StringComparison]::OrdinalIgnoreCase) -or
    $test.StartsWith($normal + $separator, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'TestDataFolder must not be your real RimWorld save-data folder (or any subfolder of it).'
}
if ($test.Contains('=')) {
    throw 'TestDataFolder cannot contain = because the RimWorld command-line parser splits on =.'
}
if ($UseSaveModList -and $Minimal) {
    throw 'UseSaveModList and Minimal cannot be combined: an existing save needs its original mod dependencies.'
}
if ($UseSaveModList -and [string]::IsNullOrWhiteSpace($SaveFile)) {
    throw 'UseSaveModList requires -SaveFile <an existing .rws file>.'
}

# Read only the <meta><modIds> subtree, not the potentially huge world payload.
# Reject invalid or incompatible inputs BEFORE touching the isolated test configuration.
$saveModIds = @()
if ($UseSaveModList) {
    if (-not (Test-Path -LiteralPath $SaveFile -PathType Leaf) -or
        [System.IO.Path]::GetExtension($SaveFile) -ine '.rws') {
        throw "SaveFile must be an existing .rws file: $SaveFile"
    }
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $reader = [System.Xml.XmlReader]::Create([System.IO.Path]::GetFullPath($SaveFile), $settings)
    try {
        [void]$reader.MoveToContent()
        if ($reader.LocalName -ne 'savegame' -or
            -not $reader.ReadToDescendant('meta') -or
            -not $reader.ReadToDescendant('modIds')) {
            throw 'SaveFile has no savegame/meta/modIds section.'
        }
        $subtree = $reader.ReadSubtree()
        try {
            $modDoc = [System.Xml.XmlDocument]::new()
            $modDoc.XmlResolver = $null
            $modDoc.Load($subtree)
        } finally {
            $subtree.Dispose()
        }
    } finally {
        $reader.Dispose()
    }
    $saveModIds = @($modDoc.SelectNodes('/modIds/li') |
        ForEach-Object { $_.InnerText.Trim() } | Where-Object { $_.Length -gt 0 })
    if ($saveModIds.Count -eq 0) {
        throw 'The save has no usable modIds. No MOD list was changed.'
    }
}

$sourceConfig = Join-Path $normal 'Config\ModsConfig.xml'
if (-not (Test-Path -LiteralPath $sourceConfig -PathType Leaf)) {
    throw "Normal ModsConfig.xml not found: $sourceConfig. Launch RimWorld normally once or supply -NormalDataFolder."
}

$marker = Join-Path $test '.rimworld-statistics-verification'
if (Test-Path -LiteralPath $test) {
    if (-not (Test-Path -LiteralPath $marker)) {
        if (@(Get-ChildItem -LiteralPath $test -Force | Select-Object -First 1).Count -gt 0) {
            throw "Refusing to use an existing unmarked folder: $test. Choose a new -TestDataFolder."
        }
    }
} else {
    [void](New-Item -ItemType Directory -Path $test -Force)
}
if (-not (Test-Path -LiteralPath $marker)) {
    [System.IO.File]::WriteAllText($marker, 'RWS Verify isolated workspace' + [Environment]::NewLine)
}
$configFolder = Join-Path $test 'Config'
[void](New-Item -Path $configFolder -ItemType Directory -Force)
$targetConfig = Join-Path $configFolder 'ModsConfig.xml'
$modeFile = Join-Path $test '.rws-verify-modlist-mode'
$desiredMode = if ($Minimal) { 'minimal' } elseif ($UseSaveModList) { 'save' } else { 'full' }
$previousMode = if (Test-Path -LiteralPath $modeFile -PathType Leaf) {
    [System.IO.File]::ReadAllText($modeFile).Trim()
} else { '' }
# Preserve in-game changes on repeated launches in the same mode, but
# restore the original *test copy* when switching between minimal and full.
if ($UseSaveModList -or
    -not (Test-Path -LiteralPath $targetConfig -PathType Leaf) -or
    $previousMode -ne $desiredMode) {
    Copy-Item -LiteralPath $sourceConfig -Destination $targetConfig -Force
}

# Only the copied *test* mod configuration is edited. Preserve all other
# mods/DLC from the user's normal configuration, without writing it back.
[xml]$doc = [System.IO.File]::ReadAllText($targetConfig)
$active = $doc.SelectSingleNode('/ModsConfigData/activeMods')
if ($null -eq $active) {
    throw "Missing ModsConfigData/activeMods in test configuration: $targetConfig"
}
$mainId = 'pengin0503.rimworldstatistics'
$harnessId = 'pengin0503.rimworldstatistics.verifyharness'
if ($UseSaveModList) {
    # A copied save must be loaded with its own MOD order, not the user's CURRENT
    # normal ModsConfig.xml (which may be an unrelated QoL-only profile).
    $active.RemoveAll()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $saveModIds) {
        if ($seen.Add($id)) {
            $node = $doc.CreateElement('li')
            $node.InnerText = $id
            [void]$active.AppendChild($node)
        }
    }
} elseif ($Minimal) {
    # For a NEW throwaway colony: eliminate third-party background work while
    # retaining vanilla DLC, Harmony, and Statistics. Do not use this mode to
    # load an existing heavily-modded save that depends on other packages.
    $keep = @('ludeon.rimworld', 'ludeon.rimworld.royalty',
        'ludeon.rimworld.ideology', 'ludeon.rimworld.biotech',
        'ludeon.rimworld.anomaly', 'ludeon.rimworld.odyssey',
        'brrainz.harmony', $mainId, $harnessId)
    foreach ($node in @($active.SelectNodes('li'))) {
        if ($keep -notcontains $node.InnerText.Trim().ToLowerInvariant()) {
            [void]$active.RemoveChild($node)
        }
    }
}

$nodes = @($active.SelectNodes('li'))
foreach ($node in $nodes) {
    if ($node.InnerText.Trim() -ieq $harnessId -or
        ($UseSaveModList -and $node.InnerText.Trim() -ieq $mainId)) {
        # The main MOD and the harness must appear after the save's original
        # dependencies; remove saved entries before appending them once below.
        [void]$active.RemoveChild($node)
    }
}
$hasMain = @($active.SelectNodes('li') | Where-Object { $_.InnerText.Trim() -ieq $mainId }).Count -gt 0
if (-not $hasMain) {
    $main = $doc.CreateElement('li')
    $main.InnerText = $mainId
    [void]$active.AppendChild($main)
}
$harness = $doc.CreateElement('li')
$harness.InnerText = $harnessId
[void]$active.AppendChild($harness)
$doc.Save($targetConfig)
[System.IO.File]::WriteAllText($modeFile, $desiredMode)

if (-not [string]::IsNullOrWhiteSpace($SaveFile)) {
    if (-not (Test-Path -LiteralPath $SaveFile -PathType Leaf) -or
        [System.IO.Path]::GetExtension($SaveFile) -ine '.rws') {
        throw "SaveFile must be an existing .rws file: $SaveFile"
    }
    $sourceSave = [System.IO.Path]::GetFullPath($SaveFile)
    $testSaves = Join-Path $test 'Saves'
    [void](New-Item -ItemType Directory -Path $testSaves -Force)
    $name = [System.IO.Path]::GetFileNameWithoutExtension($sourceSave)
    $copyPath = Join-Path $testSaves ('RWSVerify-' + $name + '.rws')
    $hash = (Get-FileHash -LiteralPath $sourceSave -Algorithm SHA256).Hash
    if ((Test-Path -LiteralPath $copyPath -PathType Leaf) -and
        (Get-FileHash -LiteralPath $copyPath -Algorithm SHA256).Hash -ne $hash) {
        # Never overwrite a changed test save. Keep both histories.
        $available = $false
        for ($i = 1; $i -le 9999; $i++) {
            $candidate = Join-Path $testSaves ('RWSVerify-' + $name + '-' + $i.ToString('0000') + '.rws')
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                $copyPath = $candidate
                $available = $true
                break
            }
            if ((Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash -eq $hash) {
                $copyPath = $candidate
                $available = $true
                break
            }
        }
        if (-not $available) { throw 'Too many test copies of this save; choose a new TestDataFolder.' }
    }
    if (-not (Test-Path -LiteralPath $copyPath -PathType Leaf)) {
        Copy-Item -LiteralPath $sourceSave -Destination $copyPath
    }
    Write-Host "[RWS Verify] Test save copied/reused at: $copyPath"
}


Write-Host "[RWS Verify] Isolated test profile: $test"
Write-Host "[RWS Verify] Test configuration:   $targetConfig"
Write-Host "[RWS Verify] Normal saves/config are unchanged."
Write-Host "[RWS Verify] Both MOD packages must already be installed."
if ($Minimal) { Write-Host "[RWS Verify] Minimal test mod list enabled (vanilla/DLC/Harmony/RWS only)." }
if ($UseSaveModList) {
    Write-Host "[RWS Verify] Restored $($saveModIds.Count) saved MOD entries (before deduplication/RWS additions)."
    Write-Host "[RWS Verify] MOD packages are not installed by this script; missing mods/DLC must be resolved separately."
}

if ($SetupOnly) { return }

$exe = FullPath $GameExe
$directory = Split-Path -Path $exe -Parent
# Start-Process accepts one argument string; quote the folder separately to
# preserve spaces. No permanent Steam launch-options changes are needed.
$gameArgs = '-savedatafolder="' + $test + '" -rwsverify=all'
Start-Process -FilePath $exe -WorkingDirectory $directory -ArgumentList $gameArgs
