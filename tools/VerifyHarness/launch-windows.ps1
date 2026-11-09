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
    [switch]$SetupOnly
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
if (-not (Test-Path -LiteralPath $targetConfig -PathType Leaf)) {
    Copy-Item -LiteralPath $sourceConfig -Destination $targetConfig
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
$nodes = @($active.SelectNodes('li'))
foreach ($node in $nodes) {
    if ($node.InnerText.Trim() -ieq $harnessId) {
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

Write-Host "[RWS Verify] Isolated test profile: $test"
Write-Host "[RWS Verify] Test configuration:   $targetConfig"
Write-Host "[RWS Verify] Normal saves/config are unchanged."
Write-Host "[RWS Verify] Both MOD packages must already be installed."

if ($SetupOnly) { return }

$exe = FullPath $GameExe
$directory = Split-Path -Path $exe -Parent
# Start-Process accepts one argument string; quote the folder separately to
# preserve spaces. No permanent Steam launch-options changes are needed.
$gameArgs = '-savedatafolder="' + $test + '" -rwsverify=all'
Start-Process -FilePath $exe -WorkingDirectory $directory -ArgumentList $gameArgs
