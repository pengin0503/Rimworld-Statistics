import subprocess
import sys
import tempfile
import unittest
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / "tools" / "check_sdk_package.py"


class CheckSdkPackageTests(unittest.TestCase):
    def _build_valid_zip(self, zip_path: Path, extra_entries: dict[str, bytes] | None = None) -> None:
        with zipfile.ZipFile(zip_path, "w") as zf:
            for about in ("About/About.xml", "About/Preview.png", "About/PublishedFileId.txt"):
                zf.writestr(f"RimworldStatistics/{about}", (ROOT / about).read_bytes())
            for dll in ("RimworldStatistics.Api.dll", "RimworldStatistics.Core.dll", "RimworldStatistics.dll"):
                zf.writestr(f"RimworldStatistics/1.7/Assemblies/{dll}", b"MZ-mock")
            for xml in sorted((ROOT / "Languages").glob("*/Keyed/*.xml")):
                zf.writestr(f"RimworldStatistics/{xml.relative_to(ROOT).as_posix()}", xml.read_bytes())
            for name, payload in (extra_entries or {}).items():
                zf.writestr(name, payload)

    def test_valid_production_zip_passes_strict_whitelist(self) -> None:
        with tempfile.TemporaryDirectory() as tmp:
            z = Path(tmp) / "host.zip"
            self._build_valid_zip(z)
            proc = subprocess.run(
                [sys.executable, str(SCRIPT), str(z), "--verify-contents"],
                cwd=ROOT,
                capture_output=True,
                text=True,
            )
            self.assertEqual(0, proc.returncode, proc.stderr)

    def test_unexpected_files_are_rejected_by_whitelist(self) -> None:
        for leaked in (
            "RimworldStatistics/1.7/Assemblies/RimworldStatistics.pdb",
            "RimworldStatistics/.env",
            "RimworldStatistics/r2_credentials.env",
            "RimworldStatistics/Languages/English/Keyed/extra-untracked.xml",
            "RimworldStatistics/About/extra.json",
        ):
            with self.subTest(leaked=leaked), tempfile.TemporaryDirectory() as tmp:
                z = Path(tmp) / "host.zip"
                self._build_valid_zip(z, {leaked: b"leaked"})
                proc = subprocess.run(
                    [sys.executable, str(SCRIPT), str(z), "--verify-contents"],
                    cwd=ROOT,
                    capture_output=True,
                    text=True,
                )
                self.assertNotEqual(0, proc.returncode, f"Expected failure for {leaked}")


if __name__ == "__main__":
    unittest.main()
