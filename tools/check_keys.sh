#!/usr/bin/env bash
# Every "RWS_…" key used in Source/RimworldStatistics (and the metric registry in Source/RimworldStatistics.Core) must
# exist in each language's Keyed files (Languages/<lang>/Keyed/*.xml, read together), and none may be unused or duplicated.
# Like the plan's check, but key names may contain digits (e.g. RWS_LoadSource_Backup1).
set -euo pipefail
cd "$(dirname "$0")/.."
used=$(grep -ohE '"RWS_[A-Za-z0-9_]+"' -r --include='*.cs' Source/RimworldStatistics Source/RimworldStatistics.Core | tr -d '"' | sort -u)
status=0
for d in Languages/*/Keyed; do
  f="$d"
  all=$(cat "$d"/*.xml | grep -oE '<RWS_[A-Za-z0-9_]+>' | tr -d '<>' | sort)
  dup=$(echo "$all" | uniq -d)
  [ -n "$dup" ] && { echo "$d duplicated:"; echo "$dup" | sed 's/^/  /'; status=1; }
  have=$(echo "$all" | sort -u)
  missing=$(comm -23 <(echo "$used") <(echo "$have"))
  unused=$(comm -13 <(echo "$used") <(echo "$have"))
  [ -n "$missing" ] && { echo "$f missing:"; echo "$missing" | sed 's/^/  /'; status=1; }
  [ -n "$unused" ] && { echo "$f unused:"; echo "$unused" | sed 's/^/  /'; status=1; }
done
[ $status -eq 0 ] && echo "translation keys: $(echo "$used" | wc -l) used, all present in every language"
exit $status
