"""Independent static-dynamics oracle for Physics Benchmark V1 (GAM-50 section 6).

stdlib only; nothing here reads Unity's solver output to build its model.

Inputs: <raw>/oracle/*.json written by OracleExport (live segment masses,
world COMs, joint anchors and world flexion axes, bar mass/COM).

Model: quasi-static Newton-Euler on the athlete's kinematic tree. For each
powered joint the generalized gravity torque about the joint's world flexion
axis is the moment of the weight of the free (non-grounded) subtree:

  trunk / neck / arm joints: free side is the child subtree   -> K e = -G . a
  hip / knee / ankle joints: free side is the parent side      -> K e = +G . a

Both feet are on the ground, so the body above the hips (pelvis, trunk, head,
arms, bar) is a closed loop through the two legs. The oracle splits it equally
between the legs, which is exact for the sagittal moment of a bilaterally
symmetric pose (the canonical squat is symmetric by construction).

Outputs a raw benchmark case (same schema as PhysicsBenchmarkRecorder) so the
comparator aggregates it with every other case:
  - oracle generalized torque vs Unity modeled drive torque K e + D e_dot
  - oracle torque vs finite maximumForce (capacity demand at canonical poses)
"""

import argparse
import glob
import json
import math
import os

UPPER = {"pelvis", "abdomen", "thorax", "head_neck", "left_upper_arm", "right_upper_arm",
         "left_forearm", "right_forearm", "left_hand", "right_hand"}
SAGITTAL = ["left_foot", "right_foot", "left_shank", "right_shank", "left_thigh", "right_thigh", "abdomen", "thorax"]
LEG_DISTAL = {  # parent-side (grounded-away) leg segments carried by each leg joint, plus half the upper body
    "left_thigh": [], "right_thigh": [],
    "left_shank": ["left_thigh"], "right_shank": ["right_thigh"],
    "left_foot": ["left_shank", "left_thigh"], "right_foot": ["right_shank", "right_thigh"],
}

# Engineering tolerance for oracle/Unity agreement: 10% of the oracle torque
# with a 10 N m floor (equal-split and contact-distribution assumptions).
REL_TOL = 0.10
ABS_FLOOR_NM = 10.0


def sub(a, b):
    return [a[0] - b[0], a[1] - b[1], a[2] - b[2]]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def moment(points, pivot, gravity):
    """Sum over (mass, com) of (com - pivot) x (m g)."""
    total = [0.0, 0.0, 0.0]
    for mass, com in points:
        f = [mass * gravity[0], mass * gravity[1], mass * gravity[2]]
        m = cross(sub(com, pivot), f)
        total = [total[0] + m[0], total[1] + m[1], total[2] + m[2]]
    return total


def children_map(bodies):
    children = {}
    for b in bodies:
        children.setdefault(b["parent"], []).append(b["id"])
    return children


def subtree(root, children):
    out = [root]
    for c in children.get(root, []):
        out.extend(subtree(c, children))
    return out


def oracle_torques(export):
    gravity = export["gravity"]
    bodies = {b["id"]: b for b in export["bodies"]}
    children = children_map(export["bodies"])
    bar = export.get("bar")

    def points(ids, include_bar_if=None):
        pts = [(bodies[i]["mass"], bodies[i]["com_world"]) for i in ids]
        if bar is not None and include_bar_if is not None and include_bar_if in ids:
            pts.append((bar["mass"], bar["com_world"]))
        return pts

    result = {}
    for joint in export["joints"]:
        jid = joint["id"]
        if not joint.get("powered"):
            continue
        pivot = joint["anchor_world"]
        axis = joint["axis_world"]
        if jid in LEG_DISTAL:
            upper_pts = points(sorted(UPPER), include_bar_if="thorax")
            half_upper = [(m * 0.5, c) for m, c in upper_pts]
            g = moment(half_upper + points(LEG_DISTAL[jid]), pivot, gravity)
            predicted = dot(g, axis)
        else:
            ids = subtree(jid, children)
            g = moment(points(ids, include_bar_if="thorax"), pivot, gravity)
            predicted = -dot(g, axis)
        result[jid] = predicted
    return result


def metric(case, name, cfg, expected, observed, tol, kind, source, meaning, layer, gated=True):
    abs_err = None if expected is None or observed is None else abs(observed - expected)
    rel_err = None if abs_err is None or not expected else abs_err / abs(expected)
    if not gated:
        passed = True
    elif kind == "Absolute":
        passed = abs_err is not None and abs_err <= tol
    elif kind == "UpperBound":
        passed = observed is not None and observed <= tol
    else:
        passed = rel_err is not None and rel_err <= tol
    return {
        "case": case, "metric": name, "configuration": cfg, "expected": expected, "observed": observed,
        "abs_error": abs_err, "rel_error": rel_err, "tolerance": tol if gated else None,
        "tolerance_kind": kind if gated else "Informational", "tolerance_source": source, "pass": passed,
        "gated": gated, "failure_meaning": meaning, "layer": layer, "layer_name": "",
    }


def run(raw_dir):
    exports = sorted(glob.glob(os.path.join(raw_dir, "oracle", "*.json")))
    rows = []
    case = "B13_oracle_static_torque"
    for path in exports:
        with open(path, encoding="utf-8") as handle:
            export = json.load(handle)
        predicted = oracle_torques(export)
        settled = export["stage"] == "settled"
        feet = export.get("feet", {})
        grounded = feet.get("left_contact") and feet.get("right_contact")
        speeds = [math.sqrt(dot(b["velocity"], b["velocity"])) for b in export["bodies"]]
        static = max(speeds) < 0.02
        cfg = "%s;stage=%s;load=%g;phase=%.2f;%s" % (export["case"], export["stage"], export["load_kg"], export["phase"], export["variant"])
        gate_case = settled and grounded and static and export["variant"] == "production"
        joints_by_id = {j["id"]: j for j in export["joints"]}
        # The left/right split of the upper body between two grounded legs is
        # statically indeterminate; their sum about a common flexion axis is
        # not. Bilateral leg joints gate on the pair sum.
        for left, right in (("left_foot", "right_foot"), ("left_shank", "right_shank"), ("left_thigh", "right_thigh")):
            jl, jr = joints_by_id.get(left), joints_by_id.get(right)
            if not jl or not jr or "modeled_twist_torque" not in jl or "modeled_twist_torque" not in jr:
                continue
            parallel = dot(jl["axis_world"], jr["axis_world"]) > 0.98
            oracle = predicted[left] + predicted[right]
            unity = jl["modeled_twist_torque"] + jr["modeled_twist_torque"]
            tol = max(REL_TOL * abs(oracle), ABS_FLOOR_NM)
            rows.append(metric(case, "%s_pair_sum_unity_vs_oracle_torque_nm" % left.split("_", 1)[1], cfg, oracle, unity, tol,
                               "Absolute", "independent quasi-static Newton-Euler, bilateral sum; max(10%%, 10 N m)",
                               "Unity's modeled bilateral drive torque disagrees with independent statics: frame, mass, or drive-model error.",
                               1, gated=gate_case and parallel))
        for joint in export["joints"]:
            jid = joint["id"]
            if jid not in SAGITTAL or "modeled_twist_torque" not in joint:
                continue
            oracle = predicted[jid]
            unity = joint["modeled_twist_torque"]
            tol = max(REL_TOL * abs(oracle), ABS_FLOOR_NM)
            bilateral = jid.startswith("left_") or jid.startswith("right_")
            gate = gate_case and not bilateral
            rows.append(metric(case, "%s_unity_vs_oracle_torque_nm" % jid, cfg, oracle, unity, tol, "Absolute",
                               "independent quasi-static Newton-Euler (bilateral joints: equal-split characterization only); max(10%% of oracle, 10 N m)",
                               "Unity's modeled drive torque disagrees with independent statics: frame, mass, or drive-model error.",
                               1, gated=gate))
            rows.append(metric(case, "%s_solver_vs_oracle_torque_nm" % jid, cfg, oracle, joint.get("solver_twist_torque"),
                               tol, "Absolute", "characterization", "Engine-reported constraint torque vs statics.", 4, gated=False))
            cap = joint.get("max_force")
            if cap:
                rows.append(metric(case, "%s_oracle_demand_fraction" % jid, cfg, None, abs(oracle) / cap, None,
                                   "Informational", "oracle torque / maximumForce",
                                   "Static capacity demand of the pose, independent of Unity's solver.", 9, gated=False))
    out = os.path.join(raw_dir, case + ".json")
    os.makedirs(os.path.dirname(out), exist_ok=True)
    with open(out, "w", encoding="utf-8") as handle:
        json.dump({"schema": "PHYSICS_BENCHMARK_V1", "case": case, "unity_version": "n/a (offline oracle)",
                   "platform": "python", "notes": {"exports": str(len(exports))}, "metrics": rows}, handle, indent=1)
    failed = [r for r in rows if r["gated"] and not r["pass"]]
    print("ORACLE exports=%d metrics=%d gated=%d failed=%d -> %s" % (
        len(exports), len(rows), sum(1 for r in rows if r["gated"]), len(failed), out))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-dir", required=True)
    run(parser.parse_args().raw_dir)


if __name__ == "__main__":
    main()
