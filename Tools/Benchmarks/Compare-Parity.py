"""GAM-50 Editor vs Windows standalone parity (B17). stdlib only.

Compares parity-editor-<load>kg.csv with parity-standalone-<load>kg.csv (both
written by GAM50ParityHarness) and writes a raw benchmark case.

Contract: same-platform repeatability is the primary determinism contract
(B15). Across runtimes, bit identity is recorded but not required; the gates
are explicit metric tolerances:
  - identical state sequence and lockout tick (+/- 2 ticks),
  - bar height within 1 mm and COM within 1 mm at every common tick,
  - joint angles within 0.005 rad.
"""

import argparse
import csv
import json
import os


def read(path):
    with open(path, encoding="utf-8") as handle:
        lines = [line for line in handle if not line.startswith("#")]
    return list(csv.DictReader(lines))


def metric(case, name, cfg, expected, observed, tol, kind, source, meaning, gated=True):
    abs_err = None if expected is None or observed is None else abs(observed - expected)
    if not gated:
        passed = True
    elif kind == "Absolute":
        passed = abs_err is not None and abs_err <= tol
    else:
        passed = observed is not None and observed <= tol
    return {"case": case, "metric": name, "configuration": cfg, "expected": expected, "observed": observed,
            "abs_error": abs_err, "rel_error": None, "tolerance": tol if gated else None,
            "tolerance_kind": kind if gated else "Informational", "tolerance_source": source, "pass": passed,
            "gated": gated, "failure_meaning": meaning, "layer": 8, "layer_name": "NumericalConvergence"}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--editor-dir", required=True)
    parser.add_argument("--standalone-dir", required=True)
    parser.add_argument("--raw-dir", required=True)
    parser.add_argument("--loads", nargs="+", default=["025", "140"])
    args = parser.parse_args()
    rows = []
    for load in args.loads:
        e = read(os.path.join(args.editor_dir, "parity-editor-%skg.csv" % load))
        s = read(os.path.join(args.standalone_dir, "parity-standalone-%skg.csv" % load))
        cfg = "load=%skg" % load
        n = min(len(e), len(s))
        first_hash = next((i for i in range(n) if e[i]["state_hash"] != s[i]["state_hash"]), None)
        first_state = next((i for i in range(n) if e[i]["state"] != s[i]["state"]), None)

        def lockout_tick(trace):
            return next((int(r["tick"]) for r in trace if r["state"] == "LOCKOUT"), None)

        def worst(column):
            return max(abs(float(e[i][column]) - float(s[i][column])) for i in range(n))

        le, ls = lockout_tick(e), lockout_tick(s)
        rows.append(metric("B17_editor_standalone_parity", "bit_identical_ticks_fraction", cfg, None,
                           (first_hash if first_hash is not None else n) / float(n), None, "Informational",
                           "", "Fraction of ticks before the first cross-runtime hash difference.", gated=False))
        rows.append(metric("B17_editor_standalone_parity", "state_sequence_identical", cfg, 1,
                           1 if first_state is None and len(e) == len(s) else 0, 0, "Absolute",
                           "identical squat state sequence and trace length across runtimes",
                           "Editor and standalone take different squat paths."))
        rows.append(metric("B17_editor_standalone_parity", "lockout_tick", cfg, le, ls, 2, "Absolute",
                           "lockout within 2 ticks across runtimes", "Lockout timing differs across runtimes."))
        rows.append(metric("B17_editor_standalone_parity", "max_bar_y_diff_m", cfg, 0, worst("bar_y"), 0.001,
                           "UpperBound", "engineering: 1 mm", "Bar trajectory differs across runtimes."))
        rows.append(metric("B17_editor_standalone_parity", "max_com_diff_m", cfg, 0,
                           max(worst("com_x"), worst("com_z")), 0.001, "UpperBound", "engineering: 1 mm",
                           "COM trajectory differs across runtimes."))
        rows.append(metric("B17_editor_standalone_parity", "max_joint_angle_diff_rad", cfg, 0,
                           max(worst("left_knee_rad"), worst("right_knee_rad"), worst("left_hip_rad")), 0.005,
                           "UpperBound", "engineering: 0.005 rad", "Joint trajectories differ across runtimes."))
    out = os.path.join(args.raw_dir, "B17_editor_standalone_parity.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump({"schema": "PHYSICS_BENCHMARK_V1", "case": "B17_editor_standalone_parity",
                   "unity_version": "editor vs WindowsPlayer", "platform": "Windows", "notes": {},
                   "metrics": rows}, handle, indent=1)
    failed = [r for r in rows if r["gated"] and not r["pass"]]
    print("PARITY rows=%d failed=%d -> %s" % (len(rows), len(failed), out))
    for r in rows:
        print(" ", r["configuration"], r["metric"], r["expected"], r["observed"], "PASS" if r["pass"] else "FAIL")


if __name__ == "__main__":
    main()
