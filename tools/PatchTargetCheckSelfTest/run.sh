#!/usr/bin/env bash
# Self-test of tools/PatchTargetCheck (#313), no game needed: synthetic Harmony patches it must accept (exit 0) and
# mismatches it must refuse (exit 1, each reported). The synthetic target is indexed with tools/RefMetadataIndex.
set -euo pipefail
cd "$(dirname "$0")/../.."
work=$(mktemp -d)
trap 'rm -rf "${work:?}"' EXIT
build() { dotnet build "$1" -c Release -nologo -v quiet ${2:+-o "$2"} -p:UseNuGetRefs=true > "$work/build.log" || { cat "$work/build.log"; exit 1; }; }
build tools/RefMetadataIndex/RefMetadataIndex.csproj
build tools/PatchTargetCheck/PatchTargetCheck.csproj
build tools/PatchTargetCheckSelfTest/Good/SyntheticGoodPatches.csproj "$work/good"
build tools/PatchTargetCheckSelfTest/Bad/SyntheticBadPatches.csproj "$work/bad"
dotnet tools/RefMetadataIndex/bin/Release/net10.0/RefMetadataIndex.dll "$work/good/SyntheticTarget.dll" "$work/index" > /dev/null
check() { dotnet tools/PatchTargetCheck/bin/Release/net10.0/PatchTargetCheck.dll "$1" "$work/index"; }

status=0
if ! check "$work/good/SyntheticGoodPatches.dll" > "$work/good.txt"; then
  echo "FAIL: the correct synthetic patches were refused:"; cat "$work/good.txt"; status=1
elif ! grep -qx 'patch targets checked: 6, failures: 0' "$work/good.txt"; then
  echo "FAIL: unexpected result for the correct synthetic patches:"; cat "$work/good.txt"; status=1
fi

if check "$work/bad/SyntheticBadPatches.dll" > "$work/bad.txt"; then
  echo "FAIL: the mismatched synthetic patches passed:"; cat "$work/bad.txt"; status=1
fi
expected=(
  "RunWrongTypes.Postfix: parameter 'count' "
  "RunWrongTypes.Postfix: __result "
  "RunWrongPosition.Prefix: parameter '__0' "
  "RunStaticInstance.Prefix: __instance on static "
  "RunInstanceField.Postfix: instance field label injected into static "
  "ActVoidResult.Postfix: __result on void "
  "ActDowncast.Prefix: parameter 'other' "
  "DescribeFieldType.Postfix: ___label "
  "DescribeMissingField.Postfix: field missing not on "
  "DescribeRefType.Prefix: parameter 'scale' "
  "DescribeRefObject.Prefix: parameter 'scale' "
  "DescribeUnrelatedInstance.Prefix: __instance "
  "DescribeUnknownParameter.Prefix: parameter 'amount' is not a parameter "
  "DescribeArgsType.Prefix: __args must be "
  "DescribePrefixReturn.Prefix: returns "
  "CellResultType.Postfix: __result "
)
for line in "${expected[@]}"; do
  grep -qF "FAIL $line" "$work/bad.txt" || { echo "FAIL: not reported: $line"; status=1; }
done
reported=$(grep -c '^FAIL ' "$work/bad.txt" || true)
if [ "$reported" -ne "${#expected[@]}" ]; then
  echo "FAIL: expected ${#expected[@]} failures for the mismatched patches, got $reported:"; cat "$work/bad.txt"; status=1
fi
[ $status -eq 0 ] && echo "PatchTargetCheck self-test: 6 correct targets pass, ${#expected[@]} mismatches refused"
exit $status
