#!/usr/bin/env bash
# Static reference check of the built mod DLL against the audited RimWorld 1.7.4985 index (spec §11.1-3).
# Needs: tools/fetch_dev_inputs.py audit .devinputs/audit, and a Release build (1.7/Assemblies/RimworldStatistics.dll).
# usage: tools/refcheck.sh [dll]   (default: the mod DLL; the verification harness passes its own DLL)
# refcheck.py imports its sibling auditlib.py, so it runs with -E -s (no env vars, no user site) instead of -I,
# which would drop the script's own folder from sys.path.
set -euo pipefail
cd "$(dirname "$0")/.."
A=.devinputs/audit
dll=${1:-1.7/Assemblies/RimworldStatistics.dll}
name=$(basename "$dll" .dll)
dotnet build "$A/tools/MetadataAudit/MetadataAudit.csproj" -c Release -v quiet -nologo > /dev/null
dotnet "$A/tools/MetadataAudit/bin/Release/net10.0/MetadataAudit.dll" index "$dll" ".devinputs/$name-index.json"
python3 -E -s "$A/tools/refcheck.py" ".devinputs/$name-index.json" "$A/assembly/Assembly-CSharp" "$A/assembly/UnityStubs" \
  --runtime-config "$A/runtimeconfig.json" --out ".devinputs/$name-refcheck.json"
python3 -I -c "import json;import sys;c=json.load(open(sys.argv[1]))['counts'];print(c);assert c.get('type-not-found',0)==0 and c.get('signature-not-found',0)==0" ".devinputs/$name-refcheck.json"
