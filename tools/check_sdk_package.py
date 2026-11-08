#!/usr/bin/env python3
"""Fail CI when the host or sample packages contain wrong/multiple SDK assemblies."""
import sys
import zipfile
from pathlib import PurePosixPath


def dlls(path):
    with zipfile.ZipFile(path) as archive:
        invalid = archive.testzip()
        if invalid is not None:
            raise SystemExit(f"{path}: corrupt member {invalid}")
        return [p for p in archive.namelist() if PurePosixPath(p).suffix.lower() == ".dll"]


def main(host, sample):
    host_dlls = dlls(host)
    sample_dlls = dlls(sample)
    sdk_name = "RimworldStatistics.Api.dll"
    sdk = [p for p in host_dlls if PurePosixPath(p).name == sdk_name]
    if len(sdk) != 1:
        raise SystemExit(f"Host SDK DLL count {len(sdk)} != 1: {sdk}")
    if any(PurePosixPath(p).name == sdk_name for p in sample_dlls):
        raise SystemExit("Sample MOD must NOT bundle the host SDK DLL")
    if sorted(PurePosixPath(p).name for p in host_dlls) != [
        "RimworldStatistics.Api.dll", "RimworldStatistics.Core.dll", "RimworldStatistics.dll"
    ]:
        raise SystemExit(f"Unexpected host DLLs: {host_dlls}")
    if [PurePosixPath(p).name for p in sample_dlls] != ["StatisticsSdkSample.dll"]:
        raise SystemExit(f"Unexpected sample DLLs: {sample_dlls}")
    print("SDK package audit: host has exactly one API DLL; consumer has zero.")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        raise SystemExit("usage: check_sdk_package.py host.zip sample.zip")
    main(sys.argv[1], sys.argv[2])
