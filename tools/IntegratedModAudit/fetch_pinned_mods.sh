#!/usr/bin/env bash
# Fetches read-only pinned public MOD sources/DLLs into .devinputs/integrated (or $1) for static contract and IL audits.
# No target assembly is bundled into Rimworld Statistics. Cached checkouts are verified via SHA-256 before reuse.
set -euo pipefail

cd "$(dirname "$0")/../.."
root="${1:-.devinputs/integrated}"
script_sha="$(sha256sum "$0" | awk '{print $1}')"
stamp_file="$root/.script.sha256"
manifest_file="$root/.manifest.sha256"

if [[ -f "$stamp_file" && -f "$manifest_file" ]] && [[ "$(cat "$stamp_file")" == "$script_sha" ]]; then
  if (cd "$root" && sha256sum --status -c ".manifest.sha256") 2>/dev/null; then
    echo "Pinned optional MOD inputs verified from cache ($root)."
    exit 0
  fi
fi

rm -rf "$root"
mkdir -p "$root"

fetch_one() {
  local name="$1"
  local repo="$2"
  local sha="$3"
  shift 3
  local dest="$root/$name"
  mkdir -p "$dest"
  (
    cd "$dest"
    git init -q
    git sparse-checkout init --no-cone
    git sparse-checkout set --no-cone "$@"
    local attempt=1
    while ! git -c protocol.version=2 fetch -q --no-tags --depth=1 --filter=blob:none "https://github.com/${repo}.git" "$sha"; do
      if (( attempt >= 3 )); then
        echo "Failed to fetch $repo@$sha after $attempt attempts" >&2
        exit 1
      fi
      sleep "$attempt"
      attempt=$((attempt + 1))
    done
    git checkout -q --detach FETCH_HEAD
    local actual
    actual="$(git rev-parse HEAD)"
    if [[ "$actual" != "$sha" ]]; then
      echo "Pinned commit mismatch for $repo: expected $sha, got $actual" >&2
      exit 1
    fi
    rm -rf .git
    for rel in "$@"; do
      if [[ ! -f "$rel" ]]; then
        echo "Missing sparse-checkout path $rel in $repo@$sha" >&2
        exit 1
      fi
    done
  )
}

pids=()
total_count=0
wait_wave() {
  for pid in "${pids[@]}"; do
    wait "$pid"
  done
  pids=()
}

queue_fetch() {
  fetch_one "$@" &
  pids+=("$!")
  total_count=$((total_count + 1))
  if (( ${#pids[@]} >= 8 )); then
    wait_wave
  fi
}

# Wave 1: original 11 pinned MOD contract / IL audit inputs.
queue_fetch sau pengin0503/rimworld-Smart-Apparel-Upgrade 5e910b82281fa6d5a4ebc3bcb5b88b1610b1dba8 \
  About/About.xml \
  Source/SmartApparelUpgrade.cs

queue_fetch recruitment pengin0503/Rimworld-Recruitment cff2f296bc152ee61000d132107767977193a243 \
  About/About.xml \
  Source/Recruitment/GameComponent_Recruitment.cs \
  Source/Recruitment/RecruitmentDrive.cs \
  Source/Recruitment/Recruitment.csproj

queue_fetch dbh Dubwise56/Dubs-Bad-Hygiene 96ec4b51efaf9ef4baa98aa832a8078be09d3f45 \
  About/About.xml \
  1.7/Assemblies/BadHygiene.dll \
  1.7/Languages/English/Keyed/DubsHygiene.xml \
  1.7/Defs/ThingDefs_Buildings/BuildingsD_WaterManagement.xml

queue_fetch dbh-3258 Dubwise56/Dubs-Bad-Hygiene f949f17b0c8f71e3621ff7c299b5e49e534e27a6 \
  1.7/Assemblies/BadHygiene.dll

queue_fetch hospitality Zaljerem/Hospitality d465bdc07b88d77eb3d158c215af388dcf771863 \
  About/About.xml \
  Source/Hospitality_MapComponent.cs \
  Source/JobDriver_BuyItem.cs \
  Source/Utilities/GuestUtility.cs \
  Source/LordJob_VisitColony.cs \
  Source/LordToil_VisitPoint.cs \
  Source/CompGuest.cs \
  Source/Building_GuestBed.cs \
  Source/IncidentWorker_VisitorGroup.cs

queue_fetch vpe Vanilla-Expanded/VanillaPsycastsExpanded 379b3235a4e91fa03d589af544fd8d5845d23690 \
  About/About.xml \
  1.7/Source/VanillaPsycastsExpanded/Hediff_PsycastAbilities.cs \
  1.7/Source/VanillaPsycastsExpanded/AbilityExtension_Psycast.cs \
  1.7/Assemblies/VanillaPsycastsExpanded.dll

queue_fetch vef-outposts Vanilla-Expanded/VanillaExpandedFramework ee22c9fe0d3460ce7cecf625ab9926b0d9e8eb5c \
  Source/Outposts/Outpost/Outpost.cs \
  Source/Outposts/Outpost/Outpost_Deliveries.cs \
  1.7/Assemblies/Outposts.dll \
  Source/VEF/Abilities/Misc/Ability.cs \
  Source/VEF/Abilities/ModExtensions/AbilityExtension_AbilityMod.cs \
  1.7/Assemblies/VEF.dll \
  1.7/Assemblies/PipeSystem.dll

queue_fetch voe Vanilla-Expanded/VanillaOutpostsExpanded 2b4d6869398719fd071f1e6186473b3433dd95b1 \
  loadFolders.xml \
  1.6/Source/VOE/Outpost_Town.cs \
  1.6/Assemblies/VOE.dll

queue_fetch vte Vanilla-Expanded/VanillaTradingExpanded 85524a235b4ff6dc7b25858420518aa79ef35442 \
  About/About.xml \
  loadFolders.xml \
  1.6/Source/VanillaTradingExpanded/Banks/Bank.cs \
  1.6/Source/VanillaTradingExpanded/TradingManager.cs \
  1.6/Source/VanillaTradingExpanded/AI/LordJob_GrabItemsAndLeave.cs \
  1.6/Source/VanillaTradingExpanded/Misc/TransactionProcess.cs \
  1.6/Source/VanillaTradingExpanded/UI/Window_PerformTransactionCosts.cs \
  1.6/Assemblies/VanillaTradingExpanded.dll

# Exact 1.6 binaries selected by each upstream 1.7 loadFolders.xml.
queue_fetch saurid Vanilla-Expanded/VanillaRacesExpanded-Saurid 021b9f684c7670ef325bc33e17e0a692367913a5 \
  About/About.xml \
  loadFolders.xml \
  1.6/Source/VRESaurids/Comp_HumanHatcher.cs \
  1.6/Assemblies/VRESaurids.dll

queue_fetch phytokin Vanilla-Expanded/VanillaRacesExpanded-Phytokin 2c2eb8dd18cc0cad02b6f68b5b5b657aec25d62b \
  About/About.xml \
  loadFolders.xml \
  1.6/Source/VanillaRacesExpanded-Phytokin/VanillaRacesExpanded-Phytokin/Building/Building_SaplingChild.cs \
  1.6/Assemblies/VanillaRacesExpanded-Phytokin.dll

# Wave 2+: Additional RimWorld 1.7 selected assemblies pinned in docs/planned-mod-statistics/2026-11-13-reference-lock.md
# for AdapterSignatureAudit type/member/IL smoke verification.
queue_fetch giddyup MemeGoddess/GiddyUp2 ccbe93ea4cf623ae88027a5146e32d2a376b2a25 \
  About/About.xml \
  1.7/Assemblies/GiddyUpCore.dll

queue_fetch rimfactory zymex22/Project-RimFactory-Revived ede0db655d1f97120a6b42da265716919537ccf2 \
  About/About.xml \
  Assemblies/ProjectRimFactory.dll

queue_fetch rimatomics Dubwise56/Rimatomics de328ca971de38ffef6dd5e5054a27105e279d01 \
  About/About.xml \
  1.7/Assemblies/Rimatomics.dll

queue_fetch rimefeller Dubwise56/Rimefeller ab45267f1dd807c65b827feb58dba81df8450cfd \
  About/About.xml \
  1.7/Assemblies/Rimefeller.dll

queue_fetch vae Vanilla-Expanded/VanillaAchievementsExpanded bf4b254c3c6696453e3beb93f0956e2e913d0cc9 \
  About/About.xml \
  1.7/Assemblies/AchievementsExpanded.dll

queue_fetch vaspire Vanilla-Expanded/VanillaAspirationsExpanded 249ae9c2a6802ebd87ba1dca55edc776b57e23cf \
  About/About.xml \
  1.7/Assemblies/VAspirE.dll

queue_fetch vfec Vanilla-Expanded/VanillaFactionsExpanded-Classical e87afb54d6eff2de4620179d07b7bda2a7180c7b \
  About/About.xml \
  1.6/Assemblies/VFEC.dll

queue_fetch vfed Vanilla-Expanded/VanillaFactionsExpanded-Deserters 611904562d3d2ca0b7c70721bc8afd1a699ac48a \
  About/About.xml \
  1.6/Assemblies/VFED.dll

queue_fetch vfeempire Vanilla-Expanded/VanillaFactionsExpanded-Empire 2d8c9f2f73337d011a8ed6e0ee538b582a73c8be \
  About/About.xml \
  1.7/Assemblies/VFEEmpire.dll

queue_fetch vfeinsectoids Vanilla-Expanded/VanillaFactionsExpanded-Insectoids2 34746379d55bb450327d8d78f52411db48fa8b39 \
  About/About.xml \
  1.7/Assemblies/VFEInsectoids.dll

queue_fetch vfemedieval Vanilla-Expanded/VanillaFactionsExpanded-Medieval2 5a8d38d58eb42a3ee1afbace7bda8e7a8fff9029 \
  About/About.xml \
  1.6/Assemblies/VFEMedieval.dll

queue_fetch vfepirates Vanilla-Expanded/VanillaFactionsExpanded-Pirates be4040eaa04d834d0290929f2605ac2960ea5e1c \
  About/About.xml \
  1.7/Assemblies/VFEPirates.dll

queue_fetch vfesettlers Vanilla-Expanded/VanillaFactionsExpanded-Settlers a4800414d00c86d418675470b3c07f53985fdc17 \
  About/About.xml \
  1.6/Assemblies/VFE_Settlers.dll

queue_fetch vfetribals Vanilla-Expanded/VanillaFactionsExpanded-Tribals 1827be39a09f2b3de910786efd25af59ce942264 \
  About/About.xml \
  1.6/Assemblies/VFETribals.dll

queue_fetch vfefactory Vanilla-Expanded/VanillaFurnitureExpanded-Factory 788df8db453e07161ef9f1b7d9f0628624ea1641 \
  About/About.xml \
  1.6/Assemblies/VanillaFurnitureExpandedFactory.dll

queue_fetch vfesecurity Vanilla-Expanded/VanillaFurnitureExpanded-Security f7597712b096c130b52d37a1da11b8a87c859a85 \
  About/About.xml \
  1.6/Assemblies/VFESecurity.dll

queue_fetch vge Vanilla-Expanded/VanillaGeneticsExpanded 1837722d1ac76e22697bf5c091ef7fe8ecc1faa4 \
  About/About.xml \
  1.7/Assemblies/GeneticRim.dll

queue_fetch vnpe Vanilla-Expanded/VanillaNutrientPasteExpanded 3ae062af0c9b9dcaa29437dc7d5bedeae6b3adb7 \
  About/About.xml \
  1.6/Assemblies/VNPE.dll

queue_fetch vqeancients Vanilla-Expanded/VanillaQuestsExpanded-Ancients 89f865b00dadc8b465463e8bc0004cf9f5912706 \
  About/About.xml \
  1.6/Assemblies/VanillaQuestsExpandedAncients.dll

queue_fetch vqecryptoforge Vanilla-Expanded/VanillaQuestsExpanded-Cryptoforge 9b5aa8337c8d576b557e0cfc5976cab5bc3ba0d8 \
  About/About.xml \
  1.6/Assemblies/VanillaQuestsExpandedCryptoforge.dll

queue_fetch vqedronefactory Vanilla-Expanded/VanillaQuestsExpanded-DroneFactory 322a1452055fe7545111eace2d5ad2e5d7b1175f \
  About/About.xml \
  1.6/Assemblies/VanillaQuestsExpandedDroneFactory.dll

queue_fetch vqegenerator Vanilla-Expanded/VanillaQuestsExpanded-TheGenerator ebc6357078d4f3387f080ba17cf60cf9ed0cb32a \
  About/About.xml \
  1.6/Assemblies/VanillaQuestsExpandedTheGenerator.dll

queue_fetch vreandroid Vanilla-Expanded/VanillaRacesExpanded-Android 51cfbf09ffe67e71a2c3b799b07901a045c5593e \
  About/About.xml \
  1.6/Assemblies/VREAndroids.dll

queue_fetch vse Vanilla-Expanded/VanillaSkillsExpanded eba43110dc45fa925305245b065f809a0be24e6f \
  About/About.xml \
  1.7/Assemblies/VSE.dll

queue_fetch vsie Vanilla-Expanded/VanillaSocialInteractionsExpanded ac4fdfec2713ebbbb80070d271480e039fc54840 \
  About/About.xml \
  1.7/Assemblies/VanillaSocialInteractionsExpanded.dll

wait_wave

(
  cd "$root"
  LC_ALL=C find . -type f ! -name '.*' | sort | xargs sha256sum > ".manifest.sha256"
)
printf '%s\n' "$script_sha" > "$stamp_file"
echo "Fetched and verified $total_count pinned optional MOD checkouts into $root."
