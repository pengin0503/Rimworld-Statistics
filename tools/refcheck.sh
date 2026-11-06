#!/usr/bin/env bash
# Static reference check of the built mod DLL against the audited RimWorld 1.7.4985 index (spec §11.1-3).
# Needs: tools/fetch_dev_inputs.py audit .devinputs/audit, and a Release build (1.7/Assemblies/RimworldStatistics.dll).
# refcheck.py imports its sibling auditlib.py, so it runs with -E -s (no env vars, no user site) instead of -I,
# which would drop the script's own folder from sys.path.
set -euo pipefail
cd "$(dirname "$0")/.."
A=.devinputs/audit
dotnet build "$A/tools/MetadataAudit/MetadataAudit.csproj" -c Release -v quiet -nologo > /dev/null
dotnet "$A/tools/MetadataAudit/bin/Release/net10.0/MetadataAudit.dll" index 1.7/Assemblies/RimworldStatistics.dll .devinputs/mod-index.json
python3 -E -s "$A/tools/refcheck.py" .devinputs/mod-index.json "$A/assembly/Assembly-CSharp" "$A/assembly/UnityStubs" \
  --runtime-config "$A/runtimeconfig.json" --out .devinputs/refcheck.json
python3 -I -c "import json;c=json.load(open('.devinputs/refcheck.json'))['counts'];print(c);assert c.get('type-not-found',0)==0 and c.get('signature-not-found',0)==0"
