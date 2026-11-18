#!/usr/bin/env bash
# Builds the mod in Release and zips it as RimworldStatistics/{About,1.7/Assemblies,Languages}.
# usage: tools/package.sh [--no-build] <out.zip>      (needs .refs/ from tools/fetch_dev_inputs.py refs .refs, or GameDir/HarmonyDll)
set -euo pipefail
cd "$(dirname "$0")/.."
no_build=false
if [[ "${1:-}" == "--no-build" ]]; then
  no_build=true
  shift
fi
out=${1:?usage: tools/package.sh [--no-build] <out.zip>}

if [[ "$no_build" == false ]]; then
  dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release -nologo -v quiet
fi

mkdir -p "$(dirname "$out")"
out_abs="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
stage="$(dirname "$out_abs")/.package-stage"
rm -rf "$stage" "$out_abs"
trap 'rm -rf "$stage"' EXIT

mod="$stage/RimworldStatistics"
mkdir -p "$mod/About" "$mod/1.7/Assemblies"
cp About/About.xml About/Preview.png About/PublishedFileId.txt "$mod/About/"
cp 1.7/Assemblies/RimworldStatistics.dll 1.7/Assemblies/RimworldStatistics.Core.dll 1.7/Assemblies/RimworldStatistics.Api.dll "$mod/1.7/Assemblies/"
shopt -s nullglob
for lang in Languages/*/; do
  xmls=("${lang}Keyed/"*.xml)
  if [ "${#xmls[@]}" -eq 0 ]; then
    echo "No Keyed XML files found in ${lang}Keyed" >&2
    exit 1
  fi
  mkdir -p "$mod/${lang}Keyed"
  cp "${xmls[@]}" "$mod/${lang}Keyed/"
done
shopt -u nullglob

# -D: no directory entries, -X: no extra file attributes, so the archive holds exactly the mod files.
(cd "$stage" && zip -q -r -D -X "$out_abs" RimworldStatistics)
echo "wrote $out_abs"
