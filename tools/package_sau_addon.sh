#!/usr/bin/env bash
# Optional external statistics addon. No host API, target-mod or game DLL is shipped.
set -euo pipefail
cd "$(dirname "$0")/.."
out=${1:?usage: tools/package_sau_addon.sh output.zip}

dotnet build addons/SmartApparelUpgrade/SmartApparelStatisticsAddon.csproj -c Release --nologo -v quiet
mkdir -p "$(dirname "$out")"
out_abs="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
stage="$(dirname "$out_abs")/.sau-addon-stage"
rm -rf "$stage" "$out_abs"
trap 'rm -rf "$stage"' EXIT
mod="$stage/RimworldStatistics.SmartApparelAddon"
mkdir -p "$mod/About" "$mod/1.7/Assemblies"
cp addons/SmartApparelUpgrade/About/About.xml "$mod/About/"
cp .devoutputs/sau-addon/RimworldStatistics.SmartApparelAddon.dll "$mod/1.7/Assemblies/"
for lang in Japanese English; do
  mkdir -p "$mod/Languages/$lang/Keyed"
  cp "addons/SmartApparelUpgrade/Languages/$lang/Keyed/RimworldStatistics_SAU.xml" "$mod/Languages/$lang/Keyed/"
done
(cd "$stage" && zip -q -r -D -X "$out_abs" RimworldStatistics.SmartApparelAddon)
echo "wrote $out_abs"
