#!/usr/bin/env bash
# Builds the development-only verification harness and zips it as RimworldStatisticsVerifyHarness/{About,1.7/Assemblies}.
# usage: tools/VerifyHarness/package.sh <out.zip>   (same references as tools/package.sh: .refs/ or GameDir/HarmonyDll)
# It is never part of tools/package.sh or the publish workflow; install it next to the main mod for a verification run only.
set -euo pipefail
cd "$(dirname "$0")"
out=${1:?usage: tools/VerifyHarness/package.sh <out.zip>}

dotnet build RimworldStatistics.VerifyHarness.csproj -c Release -nologo -v quiet

mkdir -p "$(dirname "$out")"
out_abs="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
stage="$(dirname "$out_abs")/.verify-harness-stage"
rm -rf "$stage" "$out_abs"
trap 'rm -rf "$stage"' EXIT

mod="$stage/RimworldStatisticsVerifyHarness"
mkdir -p "$mod/About" "$mod/1.7/Assemblies"
cp Mod/About/About.xml "$mod/About/"
cp Mod/1.7/Assemblies/RimworldStatistics.VerifyHarness.dll "$mod/1.7/Assemblies/"

(cd "$stage" && zip -q -r -D -X "$out_abs" RimworldStatisticsVerifyHarness)
echo "wrote $out_abs"
