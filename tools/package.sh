#!/usr/bin/env bash
# Builds the mod in Release and zips it as RimworldStatistics/{About,1.7/Assemblies,Languages}.
# usage: tools/package.sh <out.zip>      (needs .refs/ from tools/fetch_dev_inputs.py refs .refs, or GameDir/HarmonyDll)
set -euo pipefail
cd "$(dirname "$0")/.."
out=${1:?usage: tools/package.sh <out.zip>}

dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release -nologo -v quiet

mkdir -p "$(dirname "$out")"
out_abs="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
stage="$(dirname "$out_abs")/.package-stage"
rm -rf "$stage" "$out_abs"
trap 'rm -rf "$stage"' EXIT

mod="$stage/RimworldStatistics"
mkdir -p "$mod/About" "$mod/1.7/Assemblies"
cp About/About.xml "$mod/About/"
cp 1.7/Assemblies/RimworldStatistics.dll 1.7/Assemblies/RimworldStatistics.Core.dll "$mod/1.7/Assemblies/"
for lang in Languages/*/; do
  name=$(basename "$lang")
  mkdir -p "$mod/Languages/$name/Keyed"
  cp "$lang/Keyed/RimworldStatistics.xml" "$mod/Languages/$name/Keyed/"
done

# -D: no directory entries, -X: no extra file attributes, so the archive holds exactly the mod files.
(cd "$stage" && zip -q -r -D -X "$out_abs" RimworldStatistics)
echo "wrote $out_abs"
