#!/usr/bin/env bash
# Development-only independent consumer ZIP. The host owns the one SDK DLL.
set -euo pipefail
cd "$(dirname "$0")/.."
out=${1:?usage: tools/package_sdk_sample.sh output.zip}
dotnet build examples/StatisticsSdkSample/StatisticsSdkSample.csproj -c Release -nologo -v quiet
mkdir -p "$(dirname "$out")"
out_abs="$(cd "$(dirname "$out")" && pwd)/$(basename "$out")"
stage="$(dirname "$out_abs")/.sdk-sample-stage"
rm -rf "$stage" "$out_abs"
trap 'rm -rf "$stage"' EXIT
mkdir -p "$stage/StatisticsSdkSample/About" "$stage/StatisticsSdkSample/1.7/Assemblies"
cp examples/StatisticsSdkSample/About/About.xml "$stage/StatisticsSdkSample/About/"
cp .devoutputs/sample/StatisticsSdkSample.dll "$stage/StatisticsSdkSample/1.7/Assemblies/"
(cd "$stage" && zip -q -r -D -X "$out_abs" StatisticsSdkSample)
echo "wrote $out_abs"
