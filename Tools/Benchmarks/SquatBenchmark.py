"""Physics Benchmark V1 squat tier (B14 mechanics, B14L lockout extension, B15 determinism).

stdlib only. Reads the sealed GAM-13 mechanics probe outputs written by
Run-PhysicsBenchmark.ps1 -Tier Squat:

  <squat-root>/<load>kg/repNN/qualification-trace.csv
  <squat-root>/<load>kg/repNN/qualification-trace.lockout-extension.csv
  <squat-root>/<load>kg/repNN/state-hashes.csv

and writes raw benchmark cases (PhysicsBenchmarkRecorder schema) to --raw-dir.
"""

import argparse
import csv
import glob
import json
import math
import os

BAR_STILL_MPS = 0.020  # sealed GAM-12 lockout bar stillness (unchanged)
# GAM-13 V2-5: 25 easy, 60 moderate, 140 heavy, 170 near-max must complete;
# above 170 kg (300) is supra-max and must fail physically.
SUPRA_MAX_THRESHOLD_KG = 170
PHYSICAL_FAILURE_REASONS = {"SETUP_NOT_PHYSICALLY_QUALIFIED", "BALANCE_LOSS", "PHYSICAL_LOCKOUT_NOT_REACHED",
                            "NO_PHYSICAL_DESCENT", "NO_LEGAL_PHYSICAL_DEPTH", "NO_PHYSICAL_REVERSAL",
                            "NO_PHYSICAL_ASCENT", "PLANTAR_SUPPORT_LOST", "COM_OUTSIDE_SUPPORT"}


def rows(path):
    with open(path, encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def num(value):
    try:
        out = float(value)
        return out if math.isfinite(out) else None
    except (TypeError, ValueError):
        return None


def metric(case, name, cfg, expected, observed, tol, kind, source, meaning, layer, gated=True):
    abs_err = None if expected is None or observed is None else abs(observed - expected)
    rel_err = None if abs_err is None or not expected else abs_err / abs(expected)
    if not gated:
        passed = True
    elif kind == "Absolute":
        passed = abs_err is not None and abs_err <= tol
    elif kind == "UpperBound":
        passed = observed is not None and observed <= tol
    elif kind == "LowerBound":
        passed = observed is not None and observed >= tol
    else:
        passed = rel_err is not None and rel_err <= tol
    return {"case": case, "metric": name, "configuration": cfg, "expected": expected, "observed": observed,
            "abs_error": abs_err, "rel_error": rel_err, "tolerance": tol if gated else None,
            "tolerance_kind": kind if gated else "Informational", "tolerance_source": source, "pass": passed,
            "gated": gated, "failure_meaning": meaning, "layer": layer, "layer_name": ""}


def write_case(raw_dir, case, metrics, notes):
    with open(os.path.join(raw_dir, case + ".json"), "w", encoding="utf-8") as handle:
        json.dump({"schema": "PHYSICS_BENCHMARK_V1", "case": case, "unity_version": "see runtime-physics-contract.json",
                   "platform": "WindowsEditor", "notes": notes, "metrics": metrics}, handle, indent=1)


def tick_of(value):
    return None if value in (None, "", "NA") else int(value)


def analyze_extension(case, cfg, path, metrics, notes):
    ext = rows(path)
    if not ext:
        return
    natural = next((r for r in ext if int(r["consecutive_lockout"]) >= 3), None)
    speeds = [num(r["bar_speed_mps"]) for r in ext]
    vy = [num(r["bar_vy_mps"]) or 0.0 for r in ext]
    crossings = sum(1 for a, b in zip(vy, vy[1:]) if a * b < 0)
    duration = len(ext) * 0.01
    tail = ext[-100:]
    tail_speed = max(num(r["bar_speed_mps"]) for r in tail)
    mean_vy = sum(vy) / len(vy)
    amplitude = math.sqrt(sum((v - mean_vy) ** 2 for v in vy) / len(vy))
    still_fraction = sum(1 for s in speeds if s is not None and s <= BAR_STILL_MPS) / len(speeds)
    # Settling means the sealed predicates hold and keep holding: a natural
    # lockout counts only if every tick of the final second is lockout. A
    # threshold crossing that is lost again is oscillation, not settling.
    settled_tail = all(r["is_lockout"] == "true" for r in tail)
    classification = "QUALIFICATION_WINDOW_TOO_SHORT" if natural and settled_tail else "PHYSICS_NOT_SETTLING"
    notes["extension_final_second_all_lockout"] = str(settled_tail)
    notes["lockout_extension_classification"] = classification
    notes["natural_lockout_ticks_since_lockout_start"] = natural["ticks_since_lockout_start"] if natural else "none"
    failing = {}
    for r in ext:
        for p in r["failed_predicates"].split("|"):
            failing[p] = failing.get(p, 0) + 1
    notes["extension_failed_predicate_counts"] = json.dumps(failing)
    metrics.append(metric(case, "lockout_extension_settles", cfg, 1, 1 if natural and settled_tail else 0, 0, "Absolute",
                          "sealed GAM-12 lockout predicates (0.020 m/s unchanged) become true within the bounded 3 s continuation and hold through its final second",
                          "PHYSICS_NOT_SETTLING: the unchanged simulation never satisfies the existing lockout predicates.", 9))
    metrics.append(metric(case, "extension_tail_max_bar_speed_mps", cfg, None, tail_speed, None, "Informational",
                          "", "Max bar speed over the last second of the continuation.", 9, gated=False))
    metrics.append(metric(case, "extension_bar_vy_oscillation_hz", cfg, None, crossings / 2.0 / duration, None,
                          "Informational", "", "Bar vertical velocity sign changes / 2 / duration.", 9, gated=False))
    metrics.append(metric(case, "extension_bar_vy_rms_mps", cfg, None, amplitude, None, "Informational", "",
                          "Bar vertical velocity oscillation amplitude (RMS).", 9, gated=False))
    metrics.append(metric(case, "extension_bar_still_fraction", cfg, None, still_fraction, None, "Informational", "",
                          "Fraction of continuation ticks with bar speed <= 0.020 m/s.", 9, gated=False))
    last = ext[-1]
    for key in ("max_knee_rad", "max_hip_rad", "max_trunk_rad", "com_speed_mps", "support_margin_m",
                "max_demand_fraction", "max_anchor_separation_m"):
        metrics.append(metric(case, "extension_final_" + key, cfg, None, num(last[key]), None, "Informational", "",
                              "Final value after the bounded continuation.", 9, gated=False))


def analyze(squat_root, raw_dir):
    by_load = {}
    for trace_path in sorted(glob.glob(os.path.join(squat_root, "*kg", "rep*", "qualification-trace.csv"))):
        rep_dir = os.path.dirname(trace_path)
        load = os.path.basename(os.path.dirname(rep_dir))
        by_load.setdefault(load, []).append(rep_dir)

    for load, reps in sorted(by_load.items()):
        case = "B14_squat_%s" % load
        metrics, notes = [], {}
        first = reps[0]
        trace = rows(os.path.join(first, "qualification-trace.csv"))
        last = trace[-1]
        cfg = "load=%s;rep=%s" % (load, os.path.basename(first))
        lockout = last["lockout"] == "true"
        reason = last["physical_failure_reason"]
        notes["outcome"] = "PHYSICAL_LOCKOUT" if lockout else reason
        load_kg = int(load.rstrip("kg"))
        if load_kg <= SUPRA_MAX_THRESHOLD_KG:
            metrics.append(metric(case, "mechanics_valid_lockout", cfg, 1, 1 if lockout else 0, 0, "Absolute",
                                  "GAM-13 mechanics probe: legal depth, reversal, ascent and settled physical lockout (sealed GAM-12/GAM-49 predicates)",
                                  "The squat does not complete as valid physical mechanics: " + reason, 11))
            metrics.append(metric(case, "legal_depth_reached", cfg, 1, 1 if last["legal_depth_reached"] == "true" else 0, 0,
                                  "Absolute", "GAM-49 sealed depth authority", "Legal depth not reached.", 11))
        else:
            # GAM-13 V2-5: supra-max loads must fail, and fail physically.
            physical = (not lockout) and reason in PHYSICAL_FAILURE_REASONS
            metrics.append(metric(case, "supra_max_fails_physically", cfg, 1, 1 if physical else 0, 0, "Absolute",
                                  "GAM-13 V2-5 envelope: 300 kg supra-max physical stall/failure",
                                  "A supra-max load completes, or fails for a non-physical reason: " + reason, 11))
        for key in ("descent_tick", "reversal_tick", "ascent_tick"):
            metrics.append(metric(case, key, cfg, None, tick_of(last[key]), None, "Informational", "",
                                  "Event tick from the mechanics probe.", 11, gated=False))
        ext_path = os.path.join(first, "qualification-trace.lockout-extension.csv")
        if os.path.exists(ext_path):
            analyze_extension(case, cfg, ext_path, metrics, notes)
        write_case(raw_dir, case, metrics, notes)

        if len(reps) < 2:
            continue
        # B15 determinism: bit-exact per-tick state hashes across fresh processes.
        dcase = "B15_determinism_%s" % load
        dmetrics, dnotes = [], {"repetitions": str(len(reps))}
        hashes = []
        outcomes = []
        events = []
        bar_y = []
        for rep in reps:
            h = rows(os.path.join(rep, "state-hashes.csv"))
            hashes.append([(r["tick"], r["state_hash"]) for r in h])
            t = rows(os.path.join(rep, "qualification-trace.csv"))
            lt = t[-1]
            outcomes.append("LOCKOUT" if lt["lockout"] == "true" else lt["physical_failure_reason"])
            events.append(tuple(lt[k] for k in ("descent_tick", "reversal_tick", "ascent_tick")) + (lt["tick"],))
            bar_y.append([num(r["bar_y_m"]) for r in t])
        reference = hashes[0]
        first_divergence = None
        for other in hashes[1:]:
            for index, (a, b) in enumerate(zip(reference, other)):
                if a != b:
                    first_divergence = index if first_divergence is None else min(first_divergence, index)
                    break
            if len(other) != len(reference):
                first_divergence = min(first_divergence or len(reference), min(len(other), len(reference)))
        identical = first_divergence is None
        dcfg = "load=%s;reps=%d" % (load, len(reps))
        dmetrics.append(metric(dcase, "state_hash_sequences_identical", dcfg, 1, 1 if identical else 0, 0, "Absolute",
                               "same build, same platform, fresh processes: PhysX with identical inputs is bit-reproducible",
                               "Same-platform simulation is not repeatable.", 8))
        dmetrics.append(metric(dcase, "first_divergent_tick_index", dcfg, None, first_divergence, None, "Informational", "",
                               "Index of the first per-tick hash mismatch (None = identical).", 8, gated=False))
        dmetrics.append(metric(dcase, "distinct_outcomes", dcfg, 1, len(set(outcomes)), 0, "Absolute",
                               "identical final physical outcome across repetitions", "Outcome varies between identical runs.", 8))
        dmetrics.append(metric(dcase, "distinct_event_tick_tuples", dcfg, 1, len(set(events)), 0, "Absolute",
                               "identical descent/reversal/ascent/end ticks across repetitions", "Event timing varies.", 8))
        n = min(len(b) for b in bar_y)
        worst = 0.0
        for i in range(n):
            values = [b[i] for b in bar_y if b[i] is not None]
            if values:
                worst = max(worst, max(values) - min(values))
        dmetrics.append(metric(dcase, "max_bar_y_spread_m", dcfg, 0, worst, 1e-4, "UpperBound",
                               "engineering: bar trajectories agree to 0.1 mm across repetitions",
                               "Bar trajectory is not repeatable.", 8))
        dnotes["outcomes"] = json.dumps(outcomes)
        write_case(raw_dir, dcase, dmetrics, dnotes)
    print("SQUAT_BENCHMARK loads=%s" % ",".join(sorted(by_load)))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--squat-root", required=True)
    parser.add_argument("--raw-dir", required=True)
    args = parser.parse_args()
    analyze(args.squat_root, args.raw_dir)


if __name__ == "__main__":
    main()
