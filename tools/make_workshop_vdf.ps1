# Verify a GitHub Actions Workshop candidate and generate a local SteamCMD VDF.
# Does NOT authenticate with Steam, invoke SteamCMD or upload content.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ModPath,
    [Parameter(Mandatory = $true)][string]$ManifestPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [string]$PublishedFileId = '0',
    [string]$ChangeNote = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($PublishedFileId -cnotmatch '^(0|[1-9][0-9]*)$') {
    throw 'PublishedFileId must be 0 or a positive ASCII integer.'
}
$mod = (Resolve-Path -LiteralPath $ModPath -ErrorAction Stop).ProviderPath
$manifestFile = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).ProviderPath
$output = [IO.Path]::GetFullPath($OutputPath)
$sep = [IO.Path]::DirectorySeparatorChar
if ($output.StartsWith($mod.TrimEnd($sep) + $sep, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'VDF output must be outside the mod folder.'
}
if (Test-Path -LiteralPath $output) {
    throw 'VDF output exists. Use a fresh path to avoid overwriting an existing Workshop ID.'
}
if (-not (Test-Path -LiteralPath (Split-Path -Parent $output) -PathType Container)) {
    throw 'VDF output parent directory is missing.'
}
$about = Join-Path $mod 'About/About.xml'
$preview = Join-Path $mod 'About/Preview.png'
$idPath = Join-Path $mod 'About/PublishedFileId.txt'
if (-not (Test-Path -LiteralPath $about -PathType Leaf) -or
    -not (Test-Path -LiteralPath $preview -PathType Leaf)) {
    throw 'About.xml or Preview.png is missing.'
}
[xml]$metadata = Get-Content -LiteralPath $about -Raw -Encoding UTF8
$version = [string]$metadata.ModMetaData.modVersion
if ($metadata.ModMetaData.packageId -cne 'pengin0503.rimworldstatistics' -or
    $version -cnotmatch '^[0-9]+\.[0-9]+\.[0-9]+$' -or
    @($metadata.ModMetaData.supportedVersions.li) -cnotcontains '1.7' -or
    @($metadata.ModMetaData.modDependencies.li | ForEach-Object { $_.packageId }) -cnotcontains 'brrainz.harmony') {
    throw 'MOD package ID, version, RimWorld 1.7 or Harmony dependency mismatch.'
}
if ((Get-Item -LiteralPath $preview).Length -ge 1000000) {
    throw 'Preview.png must be under 1 MB.'
}

$manifest = Get-Content -LiteralPath $manifestFile -Raw -Encoding UTF8 | ConvertFrom-Json
if ($manifest.schema -ne 1 -or $manifest.version -cne $version -or
    $manifest.packageId -cne 'pengin0503.rimworldstatistics') {
    throw 'Manifest schema, package ID, or version does not match About.xml.'
}
$expected = New-Object 'System.Collections.Generic.Dictionary[string,string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($entry in @($manifest.files)) {
    $name = [string]$entry.path
    $hash = [string]$entry.sha256
    if (-not $name -or $name.Contains('\') -or $name.Contains(':') -or
        $name.StartsWith('/') -or $name.EndsWith('/') -or
        @($name.Split('/') | Where-Object { $_ -eq '' -or $_ -eq '.' -or $_ -eq '..' }).Count -gt 0 -or
        $hash -cnotmatch '^[0-9a-f]{64}$') {
        throw "Unsafe or malformed file entry in Workshop manifest: $name"
    }
    if ($expected.ContainsKey($name)) {
        throw "Duplicate Workshop manifest entry: $name"
    }
    $expected.Add($name, $hash)
}
$required = @(
    'About/About.xml', 'About/Preview.png', 'LICENSE',
    '1.7/Assemblies/RimworldStatistics.dll',
    '1.7/Assemblies/RimworldStatistics.Core.dll',
    '1.7/Assemblies/RimworldStatistics.Api.dll',
    'Languages/English/Keyed/RimworldStatistics.xml',
    'Languages/Japanese/Keyed/RimworldStatistics.xml'
)
foreach ($path in $required) {
    if (-not $expected.ContainsKey($path)) {
        throw "Workshop manifest missing required file: $path"
    }
}
if ($PublishedFileId -eq '0') {
    if (Test-Path -LiteralPath $idPath) {
        throw 'PublishedFileId.txt exists; refusing to create a duplicate Workshop item.'
    }
} else {
    if (-not (Test-Path -LiteralPath $idPath -PathType Leaf)) {
        throw 'Updating requires About/PublishedFileId.txt.'
    }
    if ((Get-Content -LiteralPath $idPath -Raw).Trim() -cne $PublishedFileId) {
        throw 'Workshop ID mismatch with About/PublishedFileId.txt.'
    }
    if ($expected.ContainsKey('About/PublishedFileId.txt')) {
        throw 'Manifest must not include PublishedFileId.txt.'
    }
}
$all = @(Get-ChildItem -LiteralPath $mod -Recurse -Force)
if ($all | Where-Object { ($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0 }) {
    throw 'Symlinks/junctions in Workshop content are forbidden.'
}
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
foreach ($file in @($all | Where-Object { -not $_.PSIsContainer })) {
    $relative = [IO.Path]::GetRelativePath($mod, $file.FullName).Replace('\', '/')
    if ($relative -eq 'About/PublishedFileId.txt' -and $PublishedFileId -ne '0') {
        continue
    }
    if (-not $expected.ContainsKey($relative)) {
        throw "Unexpected file in Workshop folder: $relative"
    }
    $actualHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $file.FullName).Hash.ToLowerInvariant()
    if ($actualHash -cne $expected[$relative]) {
        throw "Workshop payload hash mismatch: $relative"
    }
    if (-not $seen.Add($relative)) { throw "Duplicate file: $relative" }
}
if ($seen.Count -ne $expected.Count) {
    throw 'Workshop folder is missing one or more manifest files.'
}

function Quote-Vdf([string]$value) {
    if ($value.IndexOfAny([char[]]@('"', [char]13, [char]10, [char]0)) -ge 0) {
        throw 'Unsafe VDF value.'
    }
    return '"' + $value.Replace('\', '\\') + '"'
}

if (-not $ChangeNote) { $ChangeNote = "Rimworld Statistics v$version" }
$fields = [ordered]@{
    appid = '294100'
    publishedfileid = $PublishedFileId
    contentfolder = $mod
    previewfile = $preview
}
if ($PublishedFileId -eq '0') {
    $fields['visibility'] = '2'
    $fields['title'] = 'Rimworld Statistics'
    $fields['description'] = 'Lifetime statistics across RimWorld saves. Requires Harmony. See the bilingual listing before public release.'
}
$fields['changenote'] = $ChangeNote
$lines = @('"workshopitem"', '{')
foreach ($key in $fields.Keys) {
    $lines += '    ' + (Quote-Vdf $key) + ' ' + (Quote-Vdf $fields[$key])
}
$lines += '}'
[IO.File]::WriteAllText($output, (($lines -join [char]10) + [char]10), [Text.UTF8Encoding]::new($false))
Write-Output "VDF_READY=$output"
Write-Output "APP_ID=294100 VERSION=$version PUBLISHED_FILE_ID=$PublishedFileId"
Write-Output 'STEAM_UPLOAD=NOT_PERFORMED'
