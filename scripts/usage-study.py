"""Offline counter comparison. Never downloads data or declares a quota bypass."""
import argparse
import json
import math
from datetime import datetime
from pathlib import Path

MODES = {"phone", "rndis", "pdanet", "tethermax"}

def number(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        raise ValueError("counters must be finite non-negative numbers")
    return value

def analyze(record):
    if record.get("mode") not in MODES:
        raise ValueError("mode must be phone, rndis, pdanet or tethermax")
    budget = number(record["budgetBytes"])
    if budget == 0 or budget > 10 * 1024 * 1024:
        raise ValueError("per-trial budget must be 1..10485760 bytes")
    start = datetime.fromisoformat(record["startedAt"])
    end = datetime.fromisoformat(record["endedAt"])
    if start.utcoffset() is None or end.utcoffset() is None or end <= start:
        raise ValueError("ordered timestamps with timezone required")
    reasons = []
    sample = number(record["sampleBytes"])
    if sample == 0 or sample > budget:
        raise ValueError("known sample size must be positive and within the approved budget")
    resolution = record["carrierResolutionBytes"]
    known_resolution = [number(v) for v in (resolution["mobile"], resolution["hotspot"]) if v is not None]
    if len(known_resolution) < 2:
        reasons.append("carrier counter resolution unavailable")
    if known_resolution and max(known_resolution) > sample:
        reasons.append("sample smaller than carrier counter resolution; quota attribution unresolved")
    if 0 in known_resolution:
        raise ValueError("carrier counter resolution must be positive")
    if record.get("dataTestApproved") is not True:
        reasons.append("no recorded approval for the stated data budget")
    selected = record.get("selectedPath", {})
    if not selected.get("adapter") or not selected.get("evidenceFile"):
        reasons.append("selected network path evidence missing")
    delta = {}
    # Application counters are payload measurements, not billed bytes.
    for key in ("windowsUpload", "windowsDownload", "androidUpload", "androidDownload", "carrierMobileUsed", "carrierHotspotUsed"):
        if record["before"][key] is None or record["after"][key] is None:
            delta[key] = None
            reasons.append(key + ": counter unavailable; not zero")
            continue
        before, after = number(record["before"][key]), number(record["after"][key])
        delta[key] = after - before
        if delta[key] < 0:
            reasons.append(key + ": counter reset/rollover; trial invalid")
    if sum(delta[k] or 0 for k in ("windowsUpload", "windowsDownload")) > budget:
        reasons.append("Windows payload exceeded trial budget")
    if sum(delta[k] or 0 for k in ("androidUpload", "androidDownload")) > budget:
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
        if record.get("pathEvidence") != "native-wintun":
            reasons.append("missing system-wide native Wintun path evidence")
        if sum(delta[k] or 0 for k in ("windowsUpload", "windowsDownload")) <= 0 or sum(delta[k] or 0 for k in ("androidUpload", "androidDownload")) <= 0:
            reasons.append("missing nonzero host/phone relay payload counters")
    if record["mode"] == "pdanet" and (not record.get("pdanetVersion") or not isinstance(record.get("hideTetherUsageEnabled"), bool)):
        reasons.append("PdaNet version / Hide Tether Usage setting missing")
    readings = record.get("carrierReadings", [])
    observed_delay = None
    if len(readings) < 3:
        reasons.append("need baseline and at least two delayed carrier readings")
    else:
        times = [datetime.fromisoformat(r["observedAt"]) for r in readings]
        if any(t.utcoffset() is None for t in times) or any(a >= b for a, b in zip(times, times[1:])):
            raise ValueError("carrier reading timestamps must be timezone-aware and strictly ordered")
        values = [tuple(None if r[k] is None else number(r[k]) for k in ("mobileUsed", "hotspotUsed")) for r in readings]
        if any(None in v for v in values):
            reasons.append("carrier reading contains an unavailable counter")
        if times[0] > start or times[-2] < end:
            reasons.append("carrier observations do not cover the trial and delayed follow-up")
        if values[0] != (record["before"]["carrierMobileUsed"], record["before"]["carrierHotspotUsed"]) or values[-1] != (record["after"]["carrierMobileUsed"], record["after"]["carrierHotspotUsed"]):
            reasons.append("carrier readings disagree with before/after counters")
        if values[-1] != values[-2]:
            reasons.append("latest carrier readings still changing")
        for previous, current, timestamp in zip(values, values[1:], times[1:]):
            if None not in current and None not in previous and current != previous:
                observed_delay = max(0, (timestamp - end).total_seconds())
        if any(c[i] is not None and p[i] is not None and c[i] < p[i] for p, c in zip(values, values[1:]) for i in (0, 1)):
            reasons.append("carrier readings contain reset/rollover")
    return {"mode": record["mode"], "budgetBytes": budget, "sampleBytes": sample, "selectedPath": selected,
            "observedCarrierUpdateDelaySeconds": observed_delay,
            "deltaBytes": delta, "interpretation": "inconclusive" if reasons else "observed-counter-delta-only", "reasons": reasons,
            "operatorClassificationVerified": False,
            "limitation": "User-entered counters; rounding, delay and background traffic can mask attribution. No bypass guarantee."}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("record", type=Path, nargs="?")
    parser.add_argument("--template", choices=sorted(MODES))
    args = parser.parse_args()
    if args.template:
        template = {"mode": args.template, "budgetBytes": 1048576, "sampleBytes": 262144,
                    "startedAt": "REQUIRED ISO8601 WITH TIMEZONE", "endedAt": "REQUIRED ISO8601 WITH TIMEZONE",
                    "dataTestApproved": False, "carrierResolutionBytes": {"mobile": 1048576, "hotspot": 1048576},
                    "selectedPath": {"adapter": "", "evidenceFile": ""}, "carrierReadings": [],
                    "backgroundTrafficQuiet": False, "carrierCountersSettled": False,
                    "carrierBeforeEvidence": "", "carrierAfterEvidence": "",
                    "before": dict.fromkeys(("windowsUpload", "windowsDownload", "androidUpload", "androidDownload", "carrierMobileUsed", "carrierHotspotUsed"), 0)}
        template["after"] = dict(template["before"])
        if args.template == "pdanet":template.update(pdanetVersion="", hideTetherUsageEnabled=None)
        if args.template == "tethermax":template.update(standardTetheringOffObserved=False, pathEvidence="")
        print(json.dumps(template, ensure_ascii=False, indent=2));return
    if not args.record:parser.error("record or --template required")
    try:
        result = analyze(json.loads(args.record.read_text(encoding="utf-8-sig")))
    except (ValueError, KeyError, TypeError) as exc:
        parser.error(str(exc))
    print(json.dumps(result, ensure_ascii=False, indent=2))

if __name__ == "__main__":
    main()
