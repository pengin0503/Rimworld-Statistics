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
    git -c protocol.version=2 fetch -q --no-tags --depth=1 --filter=blob:none "https://github.com/${repo}.git" "$sha"
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

fetch_one sau pengin0503/rimworld-Smart-Apparel-Upgrade 5e910b82281fa6d5a4ebc3bcb5b88b1610b1dba8 \
  About/About.xml \
  Source/SmartApparelUpgrade.cs &
pids+=("$!")

fetch_one recruitment pengin0503/Rimworld-Recruitment cff2f296bc152ee61000d132107767977193a243 \
  About/About.xml \
  Source/Recruitment/GameComponent_Recruitment.cs \
  Source/Recruitment/RecruitmentDrive.cs \
  Source/Recruitment/Recruitment.csproj &
pids+=("$!")

fetch_one dbh Dubwise56/Dubs-Bad-Hygiene 96ec4b51efaf9ef4baa98aa832a8078be09d3f45 \
  About/About.xml \
  1.7/Assemblies/BadHygiene.dll \
  1.7/Languages/English/Keyed/DubsHygiene.xml \
  1.7/Defs/ThingDefs_Buildings/BuildingsD_WaterManagement.xml &
pids+=("$!")

fetch_one dbh-3258 Dubwise56/Dubs-Bad-Hygiene f949f17b0c8f71e3621ff7c299b5e49e534e27a6 \
  1.7/Assemblies/BadHygiene.dll &
pids+=("$!")

fetch_one hospitality Zaljerem/Hospitality d465bdc07b88d77eb3d158c215af388dcf771863 \
  About/About.xml \
  Source/Hospitality_MapComponent.cs \
  Source/JobDriver_BuyItem.cs \
  Source/Utilities/GuestUtility.cs \
  Source/LordJob_VisitColony.cs \
  Source/LordToil_VisitPoint.cs \
  Source/CompGuest.cs \
  Source/Building_GuestBed.cs \
  Source/IncidentWorker_VisitorGroup.cs &
pids+=("$!")

fetch_one vpe Vanilla-Expanded/VanillaPsycastsExpanded 379b3235a4e91fa03d589af544fd8d5845d23690 \
  About/About.xml \
  1.7/Source/VanillaPsycastsExpanded/Hediff_PsycastAbilities.cs \
  1.7/Source/VanillaPsycastsExpanded/AbilityExtension_Psycast.cs \
  1.7/Assemblies/VanillaPsycastsExpanded.dll &
pids+=("$!")

fetch_one vef-outposts Vanilla-Expanded/VanillaExpandedFramework ee22c9fe0d3460ce7cecf625ab9926b0d9e8eb5c \
  Source/Outposts/Outpost/Outpost.cs \
  Source/Outposts/Outpost/Outpost_Deliveries.cs \
  1.7/Assemblies/Outposts.dll \
  Source/VEF/Abilities/Misc/Ability.cs \
  Source/VEF/Abilities/ModExtensions/AbilityExtension_AbilityMod.cs \
  1.7/Assemblies/VEF.dll &
pids+=("$!")

fetch_one voe Vanilla-Expanded/VanillaOutpostsExpanded 2b4d6869398719fd071f1e6186473b3433dd95b1 \
  loadFolders.xml \
  1.6/Source/VOE/Outpost_Town.cs \
  1.6/Assemblies/VOE.dll &
pids+=("$!")

fetch_one vte Vanilla-Expanded/VanillaTradingExpanded 85524a235b4ff6dc7b25858420518aa79ef35442 \
  About/About.xml \
  loadFolders.xml \
  1.6/Source/VanillaTradingExpanded/Banks/Bank.cs \
  1.6/Source/VanillaTradingExpanded/TradingManager.cs \
  1.6/Source/VanillaTradingExpanded/AI/LordJob_GrabItemsAndLeave.cs \
  1.6/Source/VanillaTradingExpanded/Misc/TransactionProcess.cs \
  1.6/Source/VanillaTradingExpanded/UI/Window_PerformTransactionCosts.cs \
  1.6/Assemblies/VanillaTradingExpanded.dll &
pids+=("$!")

# Exact 1.6 binaries selected by each upstream 1.7 loadFolders.xml.
fetch_one saurid Vanilla-Expanded/VanillaRacesExpanded-Saurid 021b9f684c7670ef325bc33e17e0a692367913a5 \
  About/About.xml \
  loadFolders.xml \
  1.6/Source/VRESaurids/Comp_HumanHatcher.cs \
  1.6/Assemblies/VRESaurids.dll &
pids+=("$!")

fetch_one phytokin Vanilla-Expanded/VanillaRacesExpanded-Phytokin 2c2eb8dd18cc0cad02b6f68b5b5b657aec25d62b \
  About/About.xml \
  loadFolders.xml \
  1.6/Source/VanillaRacesExpanded-Phytokin/VanillaRacesExpanded-Phytokin/Building/Building_SaplingChild.cs \
  1.6/Assemblies/VanillaRacesExpanded-Phytokin.dll &
pids+=("$!")

for pid in "${pids[@]}"; do
  wait "$pid"
done

(
  cd "$root"
  LC_ALL=C find . -type f ! -name '.*' | sort | xargs sha256sum > ".manifest.sha256"
)
printf '%s\n' "$script_sha" > "$stamp_file"
echo "Fetched and verified 11 pinned optional MOD checkouts into $root."
