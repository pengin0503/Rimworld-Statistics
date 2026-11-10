"""Workshop preparation is offline, non-destructive and rejects invalid release ZIPs."""
import hashlib
import importlib.util
import os
import struct
import shutil
import subprocess
import tempfile
import unittest
import zipfile
import zlib
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[1] / "prepare_workshop.py"
PS_SCRIPT = SCRIPT.parent / "make_workshop_vdf.ps1"
SPEC = importlib.util.spec_from_file_location("prepare_workshop", SCRIPT)
assert SPEC is not None and SPEC.loader is not None
mod = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(mod)

ABOUT = b"""<ModMetaData>
<name>Rimworld Statistics</name>
<packageId>pengin0503.rimworldstatistics</packageId>
<modVersion>1.3.0</modVersion>
<supportedVersions><li>1.7</li></supportedVersions>
<modDependencies><li><packageId>brrainz.harmony</packageId></li></modDependencies>
</ModMetaData>"""
NAMES = {
    "RimworldStatistics/About/About.xml": ABOUT,
    "RimworldStatistics/1.7/Assemblies/RimworldStatistics.dll": b"MZexample1",
    "RimworldStatistics/1.7/Assemblies/RimworldStatistics.Core.dll": b"MZexample2",
    "RimworldStatistics/1.7/Assemblies/RimworldStatistics.Api.dll": b"MZexample3",
    "RimworldStatistics/Languages/Japanese/Keyed/RimworldStatistics.xml": b"<LanguageData />",
    "RimworldStatistics/Languages/English/Keyed/RimworldStatistics.xml": b"<LanguageData />",
}


def preview_png() -> bytes:
    # Minimal valid PNG, 640x360, no external dependency.
    width, height = 640, 360
    chunk = lambda tag, data: (struct.pack(">I", len(data)) + tag + data
                               + struct.pack(">I", zlib.crc32(tag + data) & 0xffffffff))
    scan = b"\x00" + bytes([0, 0, 0]) * width
    pixels = scan * height
    return (b"\x89PNG\r\n\x1a\n"
            + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(pixels, level=9))
            + chunk(b"IEND", b""))


class WorkshopPrepareTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.zip = self.root / "RimworldStatistics-v1.3.0.zip"
        self.preview = self.root / "Preview.png"
        self.preview.write_bytes(preview_png())
        self.license = self.root / "LICENSE"
        self.license.write_text("MIT License\nCopyright (c) 2026 pengin0503\n")
        self.make_zip(NAMES)
        self.sha = self.root / "RimworldStatistics-v1.3.0.sha256"
        self.write_sha()

    def make_zip(self, members):
        with zipfile.ZipFile(self.zip, "w") as z:
            for name, data in members.items():
                z.writestr(name, data)

    def write_sha(self):
        self.sha.write_text(hashlib.sha256(self.zip.read_bytes()).hexdigest() + "  " + self.zip.name + "\n")

    def test_valid_stage_is_mod_folder_with_private_vdf_and_no_upload(self):
        modroot = mod.stage(self.zip, self.preview, self.root / "staged", "1.3.0", self.license)
        self.assertTrue((modroot / "About/Preview.png").exists())
        self.assertEqual(8, len(list(modroot.rglob("*.*"))))
        self.assertTrue((modroot / "LICENSE").exists())
        self.assertEqual(ABOUT, (modroot / "About/About.xml").read_bytes())
        vdf = self.root / "staged/workshop.vdf"
        mod.write_vdf(modroot, vdf, "0")
        data = vdf.read_text()
        self.assertIn('"appid" "294100"', data)
        self.assertIn('"publishedfileid" "0"', data)
        self.assertIn('"visibility" "2"', data)
        self.assertNotIn("password", data.lower())
        mod.verify_sha(self.zip, self.sha)

    def test_vdf_escapes_windows_paths_and_rejects_unicode_ids(self):
        self.assertEqual('"' + r'C:\\Games\\RimWorld' + '"', mod.vdf_quote(r'C:\Games\RimWorld'))
        self.assertRaisesRegex(ValueError, "Unsafe", mod.vdf_quote, "bad\nvalue")
        staged = mod.stage(self.zip, self.preview, self.root / "out", "1.3.0", self.license)
        with self.assertRaisesRegex(ValueError, "Published file ID"):
            mod.write_vdf(staged, self.root / "out/workshop.vdf", "１２３")

    @unittest.skipUnless(shutil.which("pwsh"), "PowerShell not installed")
    def test_pc_vdf_tool_first_upload_and_safe_updates(self):
        staged = mod.stage(self.zip, self.preview, self.root / "out", "1.3.0", self.license)

        def run(name, workshop_id="0"):
            return subprocess.run(
                ["pwsh", "-NoProfile", "-File", str(PS_SCRIPT),
                 "-ModPath", str(staged), "-OutputPath", str(self.root / name),
                 "-PublishedFileId", workshop_id],
                text=True, capture_output=True, timeout=25,
            )

        first = run("initial.vdf")
        self.assertEqual(0, first.returncode, first.stderr)
        initial = (self.root / "initial.vdf").read_text(encoding="utf-8")
        self.assertIn('"appid" "294100"', initial)
        self.assertIn('"publishedfileid" "0"', initial)
        self.assertIn('"visibility" "2"', initial)
        if os.name == "nt":
            # PowerShell may canonicalize paths to DOS 8.3 aliases.
            lines = [line for line in initial.splitlines() if '"contentfolder"' in line]
            self.assertEqual(1, len(lines))
            actual_path = lines[0].split('"')[3]
            self.assertIn(r"\\", actual_path)
            self.assertTrue(Path(actual_path.replace("\\\\", "\\")).samefile(staged))
        self.assertIn('"description"', initial)
        self.assertIn("STEAM_UPLOAD=NOT_PERFORMED", first.stdout)

        (staged / "secrets.txt").write_text("do not publish")
        self.assertNotEqual(0, run("extra.vdf").returncode)
        self.assertFalse((self.root / "extra.vdf").exists())
        (staged / "secrets.txt").unlink()

        (staged / ".hidden-secret").write_text("no hidden files")
        self.assertNotEqual(0, run("hidden.vdf").returncode)
        (staged / ".hidden-secret").unlink()


        (staged / "About/PublishedFileId.txt").write_text("123456789\n")
        self.assertNotEqual(0, run("duplicate.vdf", "0").returncode)
        self.assertNotEqual(0, run("wrong-id.vdf", "123456780").returncode)
        updated = run("update.vdf", "123456789")
        self.assertEqual(0, updated.returncode, updated.stderr)
        update = (self.root / "update.vdf").read_text(encoding="utf-8")
        self.assertIn('"publishedfileid" "123456789"', update)
        self.assertNotIn('"visibility"', update)
        self.assertNotIn('"description"', update)
        self.assertNotIn('"title"', update)
        self.assertNotEqual(0, run("update.vdf", "123456789").returncode)

    def test_bad_hash_is_rejected(self):
        self.sha.write_text("0" * 64 + "  " + self.zip.name + "\n")
        with self.assertRaisesRegex(ValueError, "mismatch"):
            mod.verify_sha(self.zip, self.sha)

    def test_extra_dev_content_is_rejected(self):
        self.make_zip({**NAMES, "RimworldStatistics/tools/VerifyHarness.dll": b"X"})
        self.write_sha()
        with self.assertRaisesRegex(ValueError, "Unexpected ZIP payload"):
            mod.stage(self.zip, self.preview, self.root / "out", "1.3.0", self.license)

    def test_path_traversal_is_rejected(self):
        self.make_zip({**NAMES, "RimworldStatistics/../outside.txt": b"attack"})
        self.write_sha()
        with self.assertRaisesRegex(ValueError, "Unsafe ZIP entry"):
            mod.stage(self.zip, self.preview, self.root / "out", "1.3.0", self.license)

    def test_wrong_version_is_rejected(self):
        with self.assertRaisesRegex(ValueError, "modVersion"):
            mod.stage(self.zip, self.preview, self.root / "out", "1.2.2", self.license)

    def test_existing_output_not_overwritten(self):
        dest = self.root / "out"
        dest.mkdir()
        (dest / "keep.txt").write_text("existing")
        with self.assertRaisesRegex(ValueError, "must not exist"):
            mod.stage(self.zip, self.preview, dest, "1.3.0", self.license)
        self.assertEqual("existing", (dest / "keep.txt").read_text())

    def test_preview_wrong_dimensions_rejected(self):
        data = bytearray(self.preview.read_bytes())
        data[16:20] = struct.pack(">I", 100)
        self.preview.write_bytes(data)
        with self.assertRaisesRegex(ValueError, "640x360"):
            mod.stage(self.zip, self.preview, self.root / "out", "1.3.0", self.license)

    def test_workshop_id_not_guessed_or_auto_published(self):
        modroot = mod.stage(self.zip, self.preview, self.root / "out", "1.3.0", self.license)
        with self.assertRaisesRegex(ValueError, "Published file ID"):
            mod.write_vdf(modroot, self.root / "out/workshop.vdf", "not-an-id")
        mod.write_vdf(modroot, self.root / "out/workshop.vdf", "123456789")
        self.assertIn('"publishedfileid" "123456789"', (self.root / "out/workshop.vdf").read_text())
        self.assertIn('"visibility" "2"', (self.root / "out/workshop.vdf").read_text())


if __name__ == "__main__":
    unittest.main()
