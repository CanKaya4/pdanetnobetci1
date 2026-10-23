import copy
import importlib.util
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location("usage_study", Path(__file__).with_name("usage-study.py"))
study = importlib.util.module_from_spec(spec)
spec.loader.exec_module(study)

class StudyTests(unittest.TestCase):
    def record(self):
        before = dict.fromkeys(("windowsUpload", "windowsDownload", "androidUpload", "androidDownload", "carrierMobileUsed", "carrierHotspotUsed"), 0)
        return {"mode": "tethermax", "budgetBytes": 1048576,
                "startedAt": "2026-10-23T21:00:00+03:00", "endedAt": "2026-10-23T21:01:00+03:00",
                "before": before, "after": {**before, "windowsDownload": 1000, "androidDownload": 1000},
                "backgroundTrafficQuiet": True, "carrierCountersSettled": True,
                "carrierBeforeEvidence": "before.png", "carrierAfterEvidence": "settled.png",
                "standardTetheringOffObserved": True, "pathEvidence": "isolated-ip-relay"}
    def test_zero_operator_delta_never_proves_bypass(self):
        result = study.analyze(self.record())
        self.assertFalse(result["operatorClassificationVerified"])
        self.assertEqual(result["interpretation"], "observed-counter-delta-only")
    def test_resets_delay_missing_evidence_and_rndis_false_positive(self):
        for key, value in (("carrierCountersSettled", False), ("standardTetheringOffObserved", False), ("pathEvidence", "rndis"), ("backgroundTrafficQuiet", False), ("carrierBeforeEvidence", "")):
            r = self.record(); r[key] = value
            self.assertEqual(study.analyze(r)["interpretation"], "inconclusive")
        r = self.record();r["before"]["androidUpload"] = 100
        self.assertIn("counter reset", " ".join(study.analyze(r)["reasons"]))
    def test_reject_nonfinite_negative_and_excessive_budget(self):
        for value in (float("nan"), float("inf"), -1, True, 20*1024*1024):
            r = self.record(); r["budgetBytes"] = value
            with self.assertRaises(ValueError):study.analyze(r)
    def test_separate_modes(self):
        for mode in ("phone", "rndis", "tethermax"):
            r = self.record();r["mode"] = mode
            self.assertEqual(study.analyze(copy.deepcopy(r))["mode"], mode)

if __name__ == "__main__":unittest.main()
