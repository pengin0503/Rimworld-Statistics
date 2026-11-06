#!/usr/bin/env bash
# Every "RWS_…" key used in Source/RimworldStatistics must exist in each Languages/*/Keyed file (and none unused).
# Like the plan's check, but key names may contain digits (e.g. RWS_LoadSource_Backup1).
set -euo pipefail
cd "$(dirname "$0")/.."
used=$(grep -ohE '"RWS_[A-Za-z0-9_]+"' -r Source/RimworldStatistics | tr -d '"' | sort -u)
status=0
for f in Languages/*/Keyed/RimworldStatistics.xml; do
  have=$(grep -oE '<RWS_[A-Za-z0-9_]+>' "$f" | tr -d '<>' | sort -u)
  missing=$(comm -23 <(echo "$used") <(echo "$have"))
  unused=$(comm -13 <(echo "$used") <(echo "$have"))
  [ -n "$missing" ] && { echo "$f missing:"; echo "$missing" | sed 's/^/  /'; status=1; }
  [ -n "$unused" ] && { echo "$f unused:"; echo "$unused" | sed 's/^/  /'; status=1; }
done
[ $status -eq 0 ] && echo "translation keys: $(echo "$used" | wc -l) used, all present in every language"
exit $status
