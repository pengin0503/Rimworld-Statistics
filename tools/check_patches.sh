#!/usr/bin/env bash
# Every [HarmonyPatch] target of the built mod exists in the audited 1.7.4985 index, resolves to one method, and
# accepts the parameters our Prefix/Postfix/Finalizer inject, with types Harmony can bind (tools/PatchTargetCheckSelfTest
# holds the synthetic cases). Also checks fields read by reflection (AccessTools.Field).
# Needs: tools/fetch_dev_inputs.py audit .devinputs/audit, and a Release build.
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build tools/PatchTargetCheck/PatchTargetCheck.csproj -c Release -nologo -v quiet > /dev/null
idx=.devinputs/ref-indexes
extra_args=()
if [[ -s "$idx/vpe-index/chunk-0000.json.gz" && -s "$idx/vef-index/chunk-0000.json.gz" && -s "$idx/outposts-index/chunk-0000.json.gz" && -s "$idx/voe-index/chunk-0000.json.gz" ]]; then
  extra_args+=(
    --index "$idx/vpe-index" --index "$idx/vef-index"
    --target 'VpeMetricPatch:VanillaPsycastsExpanded.Hediff_PsycastAbilities:ChangeLevel:int32'
    --target 'VpeCastMetricPatch:VanillaPsycastsExpanded.AbilityExtension_Psycast:Cast:[Assembly-CSharp]RimWorld.Planet.GlobalTargetInfo[]|[VEF]VEF.Abilities.Ability'
    --index "$idx/outposts-index" --index "$idx/voe-index"
    --target 'OutpostProduceMetricPatch:Outposts.Outpost:Produce:'
    --target 'OutpostProduceMetricPatch:VOE.Outpost_Town:Produce:'
    --target 'OutpostDeliverMetricPatch:Outposts.Outpost:Deliver:[mscorlib]System.Collections.Generic.IEnumerable`1<[Assembly-CSharp]Verse.Thing>'
  )
fi
dotnet tools/PatchTargetCheck/bin/Release/net10.0/PatchTargetCheck.dll 1.7/Assemblies/RimworldStatistics.dll \
  .devinputs/audit/assembly/Assembly-CSharp \
  --field RimWorld.ShipCountdown:shipRoot \
  --field RimWorld.ShipCountdown:customLaunchString "${extra_args[@]}" "$@"
