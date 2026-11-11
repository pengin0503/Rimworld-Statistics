"""Regression checks for the conservative 266-output historical-recovery inventory."""
import collections
import pathlib
import sys
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "tools"))
from historical_recovery_inventory import build_inventory, csv_text


class HistoricalRecoveryInventoryTests(unittest.TestCase):
    def test_every_implemented_acquisition_output_has_one_recovery_decision(self):
        rows = build_inventory(ROOT)
        self.assertEqual(266, len(rows))
        self.assertEqual(
            {"live": 217, "snapshot": 40, "derived": 9},
            dict(collections.Counter(row["catalog"] for row in rows)),
        )
        keys = [row["metric_key"] for row in rows]
        self.assertEqual(len(keys), len(set(keys)))
        self.assertTrue(all(row["metric_key"] and row["acquisition_id"] for row in rows))
        self.assertTrue(all(row["evidence"] == "UNVERIFIED" for row in rows))
        self.assertTrue(all(row["recovery_decision"] in {
            "EVENT_LOG_REQUIRED", "SAVED_STATE_CANDIDATE",
            "DEPENDS_ON_INPUT",
        } for row in rows))

    def test_source_backed_examples_are_not_promoted_to_historical_exactness(self):
        by_key = {row["metric_key"]: row for row in build_inventory(ROOT)}
        self.assertEqual("EVENT_LOG_REQUIRED",
                         by_key["research.live.completed"]["recovery_decision"])
        self.assertEqual("SAVED_STATE_CANDIDATE",
                         by_key["research.snapshot.finished"]["recovery_decision"])
        self.assertEqual("DEPENDS_ON_INPUT",
                         by_key["ranking.research.peak"]["recovery_decision"])
        self.assertEqual("UNVERIFIED",
                         by_key["research.snapshot.finished"]["evidence"])

    def test_committed_inventory_is_deterministic_and_matches_registry_sources(self):
        expected = (ROOT / "docs/research/2026-11-09-historical-recovery-inventory.csv").read_text(encoding="utf-8")
        self.assertEqual(expected, csv_text(build_inventory(ROOT)))


if __name__ == "__main__":
    unittest.main()
