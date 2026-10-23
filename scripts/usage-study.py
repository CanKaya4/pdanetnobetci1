"""Offline counter comparison. Never downloads data or declares a quota bypass."""
import argparse
import json
import math
from datetime import datetime
from pathlib import Path

MODES = {"phone", "rndis", "tethermax"}

def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        raise ValueError("counters must be finite non-negative numbers")
    return value

def analyze(record):
    if record.get("mode") not in MODES:
        raise ValueError("mode must be phone, rndis or tethermax")
    budget = number(record["budgetBytes"])
    if budget == 0 or budget > 10 * 1024 * 1024:
        raise ValueError("per-trial budget must be 1..10485760 bytes")
    start = datetime.fromisoformat(record["startedAt"])
    end = datetime.fromisoformat(record["endedAt"])
    if start.utcoffset() is None or end.utcoffset() is None or end <= start:
        raise ValueError("ordered timestamps with timezone required")
    reasons = []
    delta = {}
    # Application counters are payload measurements, not billed bytes.
    for key in ("windowsUpload", "windowsDownload", "androidUpload", "androidDownload", "carrierMobileUsed", "carrierHotspotUsed"):
        before, after = number(record["before"][key]), number(record["after"][key])
        delta[key] = after - before
        if delta[key] < 0:
            reasons.append(key + ": counter reset/rollover; trial invalid")
    if delta["windowsUpload"] + delta["windowsDownload"] > budget:
        reasons.append("Windows payload exceeded trial budget")
    if delta["androidUpload"] + delta["androidDownload"] > budget:
        reasons.append("Android payload exceeded trial budget")
    if record.get("backgroundTrafficQuiet") is not True:
        reasons.append("background traffic not controlled")
    if record.get("carrierCountersSettled") is not True:
        reasons.append("carrier counters may still be delayed")
    for key in ("carrierBeforeEvidence", "carrierAfterEvidence"):
        if not isinstance(record.get(key), str) or not record[key].strip():
            reasons.append(key + ": missing operator screenshot/reference")
    if record["mode"] == "tethermax":
        if record.get("standardTetheringOffObserved") is not True:
            reasons.append("hotspot-off state not independently observed")
        if record.get("pathEvidence") != "isolated-ip-relay":
            reasons.append("missing isolated relay path evidence")
        if delta["windowsUpload"] + delta["windowsDownload"] <= 0 or delta["androidUpload"] + delta["androidDownload"] <= 0:
            reasons.append("missing nonzero host/phone relay payload counters")
    return {"mode": record["mode"], "deltaBytes": delta, "interpretation": "inconclusive" if reasons else "observed-counter-delta-only", "reasons": reasons,
            "operatorClassificationVerified": False,
            "limitation": "User-entered counters; rounding, delay and background traffic can mask attribution. No bypass guarantee."}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("record", type=Path)
    args = parser.parse_args()
    try:
        result = analyze(json.loads(args.record.read_text(encoding="utf-8-sig")))
    except (ValueError, KeyError, TypeError) as exc:
        parser.error(str(exc))
    print(json.dumps(result, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
