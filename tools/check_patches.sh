#!/usr/bin/env bash
# Every [HarmonyPatch] target of the built mod exists in the audited 1.7.4985 index, resolves to one method, and
# accepts the parameters our Prefix/Postfix inject. Also checks fields read by reflection (AccessTools.Field).
# Needs: tools/fetch_dev_inputs.py audit .devinputs/audit, and a Release build.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build tools/PatchTargetCheck/PatchTargetCheck.csproj -c Release -nologo -v quiet > /dev/null
dotnet tools/PatchTargetCheck/bin/Release/net10.0/PatchTargetCheck.dll 1.7/Assemblies/RimworldStatistics.dll \
  .devinputs/audit/assembly/Assembly-CSharp \
  --field RimWorld.ShipCountdown:shipRoot \
  --field RimWorld.ShipCountdown:customLaunchString "$@"
