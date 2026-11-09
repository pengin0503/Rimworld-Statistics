"""Tests for the independent read-only private RimWorld save structure probe."""
import base64
import os
from pathlib import Path
import struct
import sys
import tempfile
import unittest
import zlib

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from check_private_save_shapes import SUPPORTED, inspect, sha256_file


def _recorder(def_name, data, compressed=True):
    raw = struct.pack("<" + "f" * len(data), *data)
    if compressed:
        comp = zlib.compressobj(level=6, wbits=-zlib.MAX_WBITS)
        payload = comp.compress(raw) + comp.flush()
        tag = "recordsDeflate"
    else:
        payload = raw
        tag = "records"
    encoded = base64.b64encode(payload).decode()
    return f"<li><def>{def_name}</def><{tag}>{encoded}</{tag}></li>"


def _save(include_component=True, invalid_recorder=False, duplicate=False):
    recs = "".join(_recorder(name, [2.0, 4.0, 8.0], compressed=True) for name in sorted(SUPPORTED))
    if invalid_recorder:
        recs += "<li><def>Wealth_Total</def><recordsDeflate>INVALID%</recordsDeflate></li>"
    if duplicate:
        recs += _recorder("Wealth_Total", [1.0, 2.0, 3.0])
    comp = ("<li Class='RimworldStatistics.GameComponent_RimworldStatistics'>"
            "<campaignId>synthetic</campaignId></li>") if include_component else ""
    quests = "".join([
        "<li><id>1</id><acceptanceTick>5</acceptanceTick><ended>True</ended>"
        "<endOutcome>Success</endOutcome><cleanupTick>65</cleanupTick></li>",
        "<li><id>2</id><acceptanceTick>10</acceptanceTick><ended>True</ended>"
        "<endOutcome>Fail</endOutcome><cleanupTick>80</cleanupTick></li>",
        "<li><id>3</id><acceptanceExpireTick>200</acceptanceExpireTick></li>",
        "<li><id>4</id><acceptanceExpireTick>500</acceptanceExpireTick></li>",
    ])
    return (
        "<savegame><meta><gameVersion>1.7.5000 rev1275</gameVersion>"
        "<modIds><li>example.one</li><li>example.two</li></modIds></meta>"
        "<game><tickManager><ticksGame>300</ticksGame></tickManager>"
        "<storyWatcher><statsRecord><numRaidsEnemy>3</numRaidsEnemy></statsRecord></storyWatcher>"
        "<history><autoRecorderGroups><li><recorders>" + recs
        + "</recorders></li></autoRecorderGroups></history>"
        + "<questManager><quests>" + quests + "</quests></questManager>"
        + "<components>" + comp + "</components></game></savegame>"
    ).encode()


class PrivateSaveProbeTests(unittest.TestCase):
    def setUp(self):
        self.directory = tempfile.TemporaryDirectory()
        self.addCleanup(self.directory.cleanup)
        self.path = os.path.join(self.directory.name, "anonymous.rws")

    def parse(self, data):
        Path(self.path).write_bytes(data)
        before = sha256_file(self.path)
        result = inspect(self.path)
        self.assertEqual(before, sha256_file(self.path))
        return result

    def test_compressed_history_and_retained_quest_outcomes(self):
        state = self.parse(_save())
        self.assertEqual("1.7.5000 rev1275", state["version"])
        self.assertEqual(300, int(state["ticks"]))
        self.assertEqual(3, int(state["raids"]))
        self.assertEqual(2, state["mod_count"])
        self.assertTrue(state["has_component"])
        self.assertFalse(state["has_checkpoint"])
        self.assertEqual(4, state["quest_count"])
        self.assertEqual({name: 3 for name in SUPPORTED}, state["recorder_lengths"])
        self.assertEqual({"accepted": 2, "success": 1, "failure": 1, "expired": 1},
                         state["quest_outcomes"])
        self.assertAlmostEqual(8.0, state["wealth_total"]["latest"])
        self.assertAlmostEqual(8.0, state["wealth_total"]["max"])
        self.assertAlmostEqual(14.0 / 3.0, state["wealth_total"]["mean"])

    def test_component_can_be_absent(self):
        state = self.parse(_save(include_component=False))
        self.assertFalse(state["has_component"])

    def test_malformed_compressed_history_is_rejected(self):
        with self.assertRaises((ValueError, AssertionError)):
            self.parse(_save(invalid_recorder=True))

    def test_duplicate_history_recorder_is_rejected(self):
        with self.assertRaises(ValueError):
            self.parse(_save(duplicate=True))

    def test_rejects_truncated_xml(self):
        from xml.parsers.expat import ExpatError
        with self.assertRaises(ExpatError):
            self.parse(_save()[:-22])

    def test_rejects_dtd_even_with_internal_entity(self):
        payload = _save().replace(b"<savegame>", b'<!DOCTYPE savegame [<!ENTITY x "test">]><savegame>')
        with self.assertRaises(ValueError):
            self.parse(payload)


if __name__ == "__main__":
    unittest.main()
