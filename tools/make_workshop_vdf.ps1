# Generates only a local SteamCMD VDF. Never contacts Steam or reads credentials.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ModPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$PublishedFileId = '0',
    [string]$ChangeNote = 'Rimworld Statistics v1.3.0'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PublishedFileId -cnotmatch '^(0|[1-9][0-9]*)$') {
    throw 'PublishedFileId must be 0 (new item) or an ASCII positive integer.'
}
$mod = (Resolve-Path -LiteralPath $ModPath -ErrorAction Stop).ProviderPath
$output = [IO.Path]::GetFullPath($OutputPath)
$sep = [IO.Path]::DirectorySeparatorChar
if ($output.StartsWith($mod.TrimEnd($sep) + $sep, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'VDF output must be outside the mod folder.'
}
if (Test-Path -LiteralPath $output) {
    throw 'VDF output already exists. Use a new filename to avoid overwriting an existing Workshop ID.'
}
if (-not (Test-Path -LiteralPath (Split-Path -Parent $output) -PathType Container)) {
    throw 'VDF output parent directory does not exist.'
}
$about = Join-Path $mod 'About/About.xml'
$preview = Join-Path $mod 'About/Preview.png'
$idPath = Join-Path $mod 'About/PublishedFileId.txt'
if (-not (Test-Path -LiteralPath $about -PathType Leaf) -or
    -not (Test-Path -LiteralPath $preview -PathType Leaf)) {
    throw 'Required About.xml or Preview.png is missing.'
}
[xml]$metadata = Get-Content -LiteralPath $about -Raw -Encoding UTF8
if ($metadata.ModMetaData.packageId -cne 'pengin0503.rimworldstatistics' -or
    $metadata.ModMetaData.modVersion -cne '1.3.0' -or
    @($metadata.ModMetaData.supportedVersions.li) -cnotcontains '1.7' -or
    @($metadata.ModMetaData.modDependencies.li | ForEach-Object { $_.packageId }) -cnotcontains 'brrainz.harmony') {
    throw 'About.xml package ID, v1.3.0, RimWorld 1.7 or Harmony dependency mismatch.'
}
if ((Get-Item -LiteralPath $preview).Length -ge 1000000) {
    throw 'Preview.png must be under 1 MB.'
}
$expected = @(
    'About/About.xml',
    'About/Preview.png',
    'LICENSE',
    '1.7/Assemblies/RimworldStatistics.dll',
    '1.7/Assemblies/RimworldStatistics.Core.dll',
    '1.7/Assemblies/RimworldStatistics.Api.dll',
    'Languages/English/Keyed/RimworldStatistics.xml',
    'Languages/Japanese/Keyed/RimworldStatistics.xml'
)
if ($PublishedFileId -eq '0') {
    if (Test-Path -LiteralPath $idPath) {
        throw 'PublishedFileId.txt already exists. Refusing to create another Workshop item.'
    }
} else {
    if (-not (Test-Path -LiteralPath $idPath -PathType Leaf)) {
        throw 'Existing Workshop updates require About/PublishedFileId.txt.'
    }
    $savedId = (Get-Content -LiteralPath $idPath -Raw).Trim()
    if ($savedId -cne $PublishedFileId) {
        throw 'Workshop ID does not match About/PublishedFileId.txt.'
    }
    $expected += 'About/PublishedFileId.txt'
}
$actual = @(Get-ChildItem -LiteralPath $mod -File -Recurse | ForEach-Object {
    $_.FullName.Substring($mod.TrimEnd($sep).Length + 1).Replace('\', '/')
})
$difference = Compare-Object -ReferenceObject ($expected | Sort-Object) -DifferenceObject ($actual | Sort-Object)
if ($difference) {
    $names = ($difference | ForEach-Object { $_.SideIndicator + ' ' + $_.InputObject }) -join ', '
    throw "Mod folder has missing or unexpected files: $names"
}

function Quote-Vdf([string]$value) {
    if ($value.IndexOfAny([char[]]@('"', [char]13, [char]10, [char]0)) -ge 0) {
        throw 'Unsafe VDF field; quotes, newlines and NUL are forbidden.'
    }
    return '"' + $value.Replace('\', '\\') + '"'
}

$fields = [ordered]@{
    appid = '294100'
    publishedfileid = $PublishedFileId
    contentfolder = $mod
    previewfile = $preview
}
if ($PublishedFileId -eq '0') {
    $fields['visibility'] = '2' # Hidden/private first; user reviews before publishing.
    $fields['title'] = 'Rimworld Statistics'
    $fields['description'] = 'RimWorld 1.7 statistics across saves. Harmony required. Edit the bilingual Workshop listing before public release.'
}
$fields['changenote'] = $ChangeNote
$lines = @('"workshopitem"', '{')
foreach ($key in $fields.Keys) {
    $lines += '    ' + (Quote-Vdf $key) + ' ' + (Quote-Vdf $fields[$key])
}
$lines += '}'
[IO.File]::WriteAllText($output, (($lines -join [char]10) + [char]10), [Text.UTF8Encoding]::new($false))
Write-Output "VDF_READY=$output"
Write-Output "APP_ID=294100 PUBLISHED_FILE_ID=$PublishedFileId"
Write-Output 'STEAM_UPLOAD=NOT_PERFORMED'
