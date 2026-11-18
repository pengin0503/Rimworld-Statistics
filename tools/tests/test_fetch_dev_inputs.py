import contextlib
import importlib.util
import io
import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock


def _load_module():
    path = Path(__file__).resolve().parents[1] / "fetch_dev_inputs.py"
    spec = importlib.util.spec_from_file_location("fetch_dev_inputs", path)
    assert spec is not None and spec.loader is not None
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


class FakePaginator:
    def __init__(self, objects):
        self._objects = objects

    def paginate(self, Bucket, Prefix):
        contents = [
            {"Key": key, "Size": len(payload)}
            for key, payload in sorted(self._objects.items())
            if key.startswith(Prefix)
        ]
        return [{"Contents": contents}] if contents else [{}]


class FakeReadOnlyS3:
    def __init__(self, objects):
        self._objects = dict(objects)
        self.calls = []

    def get_paginator(self, op_name):
        if op_name != "list_objects_v2":
            raise AssertionError(f"unexpected paginator {op_name}")
        self.calls.append(("paginate", op_name))
        return FakePaginator(self._objects)

    def head_object(self, Bucket, Key):
        self.calls.append(("head_object", Key))
        return {"ContentLength": len(self._objects[Key])}

    def download_file(self, Bucket, Key, Filename):
        self.calls.append(("download_file", Key))
        Path(Filename).write_bytes(self._objects[Key])


class FetchDevInputsTests(unittest.TestCase):
    def setUp(self):
        self.mod = _load_module()

    def test_default_refs_targets_game_windows_1_7_5011(self):
        with mock.patch.dict(os.environ, {}, clear=False):
            os.environ.pop("RWS_DEV_GAME_PLATFORM", None)
            kind, dest, platform, wids = self.mod.parse_args(["fetch_dev_inputs.py", "refs", "/tmp/out"])
        self.assertEqual(kind, "refs")
        self.assertEqual(dest, Path("/tmp/out"))
        self.assertEqual(platform, "windows")
        self.assertEqual(wids, [])
        keys = [k for k, _ in self.mod.refs_for_platform(platform)]
        self.assertIn("Game-Windows/Assembly-CSharp.dll", keys)
        self.assertIn("Game-Windows/Version.txt", keys)
        self.assertNotIn("Game-Linux/Assembly-CSharp.dll", keys)

    def test_linux_platform_override_supported_via_flag_and_env(self):
        _, _, platform_flag, _ = self.mod.parse_args(
            ["fetch_dev_inputs.py", "refs", "/tmp/out", "--platform", "linux"]
        )
        self.assertEqual(platform_flag, "linux")
        keys = [k for k, _ in self.mod.refs_for_platform(platform_flag)]
        self.assertIn("Game-Linux/Assembly-CSharp.dll", keys)

        with mock.patch.dict(os.environ, {"RWS_DEV_GAME_PLATFORM": "linux"}, clear=False):
            _, _, platform_env, _ = self.mod.parse_args(["fetch_dev_inputs.py", "refs", "/tmp/out"])
        self.assertEqual(platform_env, "linux")

    def test_refs_downloads_windows_5011_without_printing_bucket_name(self):
        objects = {
            "Game-Windows/Assembly-CSharp.dll": b"asm-win-5011",
            "Game-Windows/UnityStubs.dll": b"unity-stubs",
            "Game-Windows/Waylith.dll": b"waylith",
            "Game-Windows/Version.txt": b"1.7.5011 rev24\n",
            "Workshop/2009463077/1.7/Assemblies/0Harmony.dll": b"harmony",
        }
        fake_s3 = FakeReadOnlyS3(objects)
        secret_bucket = "private-secret-bucket-name"
        with tempfile.TemporaryDirectory() as tmp:
            out = io.StringIO()
            with contextlib.redirect_stdout(out), mock.patch.dict(os.environ, {}, clear=False):
                os.environ.pop("RWS_DEV_GAME_PLATFORM", None)
                rc = self.mod.main(
                    ["fetch_dev_inputs.py", "refs", tmp],
                    s3_client_factory=lambda: (fake_s3, secret_bucket),
                )
            self.assertEqual(rc, 0)
            stdout = out.getvalue()
            self.assertNotIn(secret_bucket, stdout)
            self.assertIn("game version (windows): 1.7.5011 rev24", stdout)
            self.assertNotIn("WARNING", stdout)
            self.assertEqual((Path(tmp) / "game" / "Assembly-CSharp.dll").read_bytes(), b"asm-win-5011")
            self.assertEqual((Path(tmp) / "harmony" / "0Harmony.dll").read_bytes(), b"harmony")

            # Second run skips already-downloaded files of matching size.
            out2 = io.StringIO()
            with contextlib.redirect_stdout(out2), mock.patch.dict(os.environ, {}, clear=False):
                os.environ.pop("RWS_DEV_GAME_PLATFORM", None)
                self.mod.main(
                    ["fetch_dev_inputs.py", "refs", tmp],
                    s3_client_factory=lambda: (fake_s3, secret_bucket),
                )
            self.assertIn("0 downloaded, 5 already present", out2.getvalue())

    def test_workshop_downloads_selected_packages_and_rejects_traversal(self):
        objects = {
            "Workshop/2992224079/About/About.xml": b"<ModMetaData />",
            "Workshop/2992224079/1.6/Assemblies/Hospital.dll": b"hospital-dll",
        }
        fake_s3 = FakeReadOnlyS3(objects)
        with tempfile.TemporaryDirectory() as tmp:
            out = io.StringIO()
            with contextlib.redirect_stdout(out):
                rc = self.mod.main(
                    ["fetch_dev_inputs.py", "workshop", tmp, "2992224079", "2992224079"],
                    s3_client_factory=lambda: (fake_s3, "hidden-bucket"),
                )
            self.assertEqual(rc, 0)
            self.assertEqual(
                (Path(tmp) / "2992224079" / "1.6" / "Assemblies" / "Hospital.dll").read_bytes(),
                b"hospital-dll",
            )

        with self.assertRaises(SystemExit) as ctx:
            with contextlib.redirect_stderr(io.StringIO()):
                self.mod.parse_args(["fetch_dev_inputs.py", "workshop", "/tmp/out", "../escape"])
        self.assertEqual(ctx.exception.code, 2)

        with tempfile.TemporaryDirectory() as tmp:
            with self.assertRaises(SystemExit) as ctx2:
                with contextlib.redirect_stderr(io.StringIO()):
                    self.mod.safe_target(Path(tmp), "../outside.dll")
            self.assertEqual(ctx2.exception.code, 2)


if __name__ == "__main__":
    unittest.main()
