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
                "standardTetheringOffObserved": True, "pathEvidence": "native-wintun",
                "sampleBytes": 1000, "dataTestApproved": True, "carrierResolutionBytes": {"mobile": 100, "hotspot": 100},
                "selectedPath": {"adapter": "TetherMax-IP-test", "evidenceFile": "route.json"},
                "carrierReadings": [{"observedAt": "2026-10-23T20:59:00+03:00", "mobileUsed": 0, "hotspotUsed": 0},
                                    {"observedAt": "2026-10-23T21:15:00+03:00", "mobileUsed": 0, "hotspotUsed": 0},
                                    {"observedAt": "2026-10-23T22:00:00+03:00", "mobileUsed": 0, "hotspotUsed": 0}]}
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
        for mode in ("phone", "rndis", "pdanet", "tethermax"):
            r = self.record();r["mode"] = mode
            self.assertEqual(study.analyze(copy.deepcopy(r))["mode"], mode)
    def test_rounding_and_delayed_updates_remain_inconclusive(self):
        r = self.record();r["carrierResolutionBytes"]["hotspot"] = 1048576
        self.assertIn("resolution", " ".join(study.analyze(r)["reasons"]))
        r = self.record();r["after"]["carrierHotspotUsed"] = 1000;r["carrierReadings"][-1]["hotspotUsed"] = 1000
        result = study.analyze(r)
        self.assertIn("still changing", " ".join(result["reasons"]))
        self.assertEqual(result["observedCarrierUpdateDelaySeconds"], 3540)
    def test_pdanet_requires_version_and_actual_setting(self):
        r = self.record();r["mode"] = "pdanet"
        self.assertEqual(study.analyze(r)["interpretation"], "inconclusive")
        r.update(pdanetVersion="5.23", hideTetherUsageEnabled=True)
        self.assertEqual(study.analyze(r)["interpretation"], "observed-counter-delta-only")
    def test_unavailable_operator_counter_is_never_treated_as_zero(self):
        r = self.record();r["before"]["carrierMobileUsed"] = None;r["after"]["carrierMobileUsed"] = None
        r["carrierResolutionBytes"]["mobile"] = None
        for reading in r["carrierReadings"]:reading["mobileUsed"] = None
        result = study.analyze(r)
        self.assertIsNone(result["deltaBytes"]["carrierMobileUsed"])
        self.assertEqual(result["interpretation"], "inconclusive")

if __name__ == "__main__":unittest.main()
