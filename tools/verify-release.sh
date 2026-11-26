#!/usr/bin/env bash
# One-command pre-release verification (Q-item / PR-8 item 7): game build, Core tests, translation keys and the export/
# version consistency checks. Fails fast with the name of the first failing step. It publishes nothing.
set -euo pipefail
cd "$(dirname "$0")/.."

step="startup"
created_tmp=""
cleanup() {
  if [[ -n "$created_tmp" ]]; then
    rm -rf -- "$created_tmp"
  fi
}
trap cleanup EXIT
trap 'status=$?; echo "verify-release: FAILED at \"$step\" (exit $status)" >&2; exit "$status"' ERR

step="environment"
if ! command -v dotnet >/dev/null 2>&1; then
  if [[ -x "${HOME:-}/.dotnet/dotnet" ]]; then
    export PATH="${HOME}/.dotnet:$PATH"
  else
    echo "verify-release: dotnet was not found on PATH or in ~/.dotnet" >&2
    exit 1
  fi
fi
if [[ -z "${TMPDIR:-}" || ! -w "${TMPDIR}" ]]; then
  created_tmp="$(mktemp -d "$PWD/.tmp-dotnet.XXXXXX")"
  export TMPDIR="$created_tmp"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export UseNuGetRefs=true

step="game build"
echo "==> game build (0 warnings / 0 errors expected)"
dotnet build Source/RimworldStatistics/RimworldStatistics.csproj -c Release -p:UseNuGetRefs=true

step="Core tests"
echo "==> Core tests (0 failed expected)"
# The pre-existing allocation-contract test warms past Tier-1 promotion. Immediately after a build, JIT promotion can
# still land inside its measurement and fail with a few hundred bytes; disabling tiering measures the code, not JIT
# timing. This affects only this verification run, not the shipped assemblies or CI.
export DOTNET_TieredCompilation=0
dotnet test Tests/RimworldStatistics.Core.Tests/RimworldStatistics.Core.Tests.csproj -c Release -p:UseNuGetRefs=true

step="translation keys"
echo "==> translation keys"
bash tools/check_keys.sh

step="CSV export header contract"
echo "==> CSV export header contract"
python3 tools/check_export_headers.py

step="README/About.xml version consistency"
echo "==> README/About.xml version consistency"
python3 tools/check_version_consistency.py

echo "verify-release: all checks passed"
