#!/usr/bin/env python3
"""Offline analysis for GAM13_POSTURE_EQUILIBRIUM_ISOLATION_V1.

Stdlib only. Reads the per-run files GAM13CausalAuditTests writes
(causal-summary-*.csv, equilibrium-audit-*.csv, joints-*, bodymeta-*,
bodies-*) and derives:

  arm-comparison.csv      one row per arm and load: Stage-A class, the
                          predeclared qualification, and the onsets that
                          decide the guard/equilibrium/impedance questions
  guard-classification.csv  PREVENTS / DELAYS / NO_CHANGE / WORSENS per load
  static-stability.csv    sagittal posture chain (knee, hip, lumbar, thorax;
                          shanks held, bilateral joints summed) at the spawn
                          pose: gravity moments, required static bias
                          b = K^-1 tau, the eigenvalues of K - G, and the
                          normalized margin of the nested sub-chains (spine;
                          hip + spine; knee + hip + spine). The knee chain
                          ignores the knee's -5 deg hyperextension stop and
                          the ankle balance loop, so it is the most
                          conservative of the three. The smallest stable
                          GAM-7 impedance factor per chain follows
                          analytically from the 1x margin (no sweep).
  equilibrium-audit.csv   runtime standing bias against the V2 trim

Rules are the frozen ones in
Artifacts/Research/GAM-13-posture-equilibrium-isolation.md.
"""

import argparse
import csv
import gzip
import math
import os
import re
from pathlib import Path

G = 9.81
SOLVER_BAND_TICKS = 5
CAPTURE_EVENTS = ("capture_departure", "capture_departure_hull")
RUN_PATTERN = re.compile(r"^trace-(p\d+-v\d+)-(.+)-(\d+)kg\.csv(\.gz)?$")

# Posture chain from the shanks up. Each degree of freedom moves every body
# listed for it and everything listed for the DOFs after it.
CHAIN = [
    ("knee", ("left_shank", "right_shank"), ("left_thigh", "right_thigh")),
    ("hip", ("left_thigh", "right_thigh"), ("pelvis",)),
    ("lumbar", ("abdomen",), ("abdomen",)),
    ("thorax", ("thorax",), ("thorax", "head_neck", "left_upper_arm", "right_upper_arm",
                             "left_forearm", "right_forearm", "left_hand", "right_hand", "barbell")),
]
# Anatomical flexion per unit forward pitch of the distal bodies about the
# joint. Knee flexion with the shank held tips the thigh backwards.
ANATOMICAL_SIGN = {"knee": -1.0, "hip": 1.0, "lumbar": 1.0, "thorax": 1.0}
TRIM_FAMILY = {"knee": "Knee", "hip": "Hip", "lumbar": "Abdomen", "thorax": "Thorax"}


def num(value):
    try:
        return float(value)
    except (TypeError, ValueError):
        return math.nan


def fmt(value, digits=6):
    if value is None:
        return "NA"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        return "NA" if math.isnan(value) else f"{value:.{digits}g}"
    return str(value)


def read_csv(path):
    path = Path(path)
    if not path.exists() and Path(str(path) + ".gz").exists():
        path = Path(str(path) + ".gz")
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", newline="") as handle:
        return list(csv.DictReader(handle))


def first_row(path):
    path = Path(path)
    if not path.exists() and Path(str(path) + ".gz").exists():
        path = Path(str(path) + ".gz")
    opener = gzip.open if path.suffix == ".gz" else open
    with opener(path, "rt", newline="") as handle:
        return next(csv.DictReader(handle))


def write_csv(path, header, rows):
    with open(path, "w", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(header)
        for row in rows:
            writer.writerow([fmt(value) for value in row])


def onset(row, event):
    value = row.get(event + "_onset_tick", "NA")
    return None if value in ("NA", "") else int(value)


# ---------------------------------------------------------------- linear algebra

def jacobi_eigenvalues(matrix, sweeps=100):
    a = [list(r) for r in matrix]
    n = len(a)
    for _ in range(sweeps):
        off = sum(a[i][j] ** 2 for i in range(n) for j in range(n) if i != j)
        if off < 1e-18:
            break
        for p in range(n):
            for q in range(p + 1, n):
                if abs(a[p][q]) < 1e-15:
                    continue
                theta = (a[q][q] - a[p][p]) / (2.0 * a[p][q])
                t = (1.0 if theta >= 0 else -1.0) / (abs(theta) + math.sqrt(theta * theta + 1.0))
                c = 1.0 / math.sqrt(t * t + 1.0)
                s = t * c
                for k in range(n):
                    akp, akq = a[k][p], a[k][q]
                    a[k][p], a[k][q] = c * akp - s * akq, s * akp + c * akq
                for k in range(n):
                    apk, aqk = a[p][k], a[q][k]
                    a[p][k], a[q][k] = c * apk - s * aqk, s * apk + c * aqk
    return sorted(a[i][i] for i in range(n))


# ---------------------------------------------------------------- static stability

def chain_model(directory, run_id):
    joints = {r["segment"]: r for r in read_csv(Path(directory) / f"joints-{run_id}.csv")}
    masses = {r["body"]: num(r["mass_kg"]) for r in read_csv(Path(directory) / f"bodymeta-{run_id}.csv")}
    spawn = first_row(Path(directory) / f"bodies-{run_id}.csv")
    com = {b: (num(spawn[f"{b}_cy"]), num(spawn[f"{b}_cz"])) for b in masses}

    dofs = []
    for index, (name, joint_ids, _) in enumerate(CHAIN):
        anchors = [(num(joints[j]["anchor_y"]), num(joints[j]["anchor_z"])) for j in joint_ids]
        anchor = (sum(a[0] for a in anchors) / len(anchors), sum(a[1] for a in anchors) / len(anchors))
        stiffness = sum(num(joints[j]["spring"]) for j in dict.fromkeys(joint_ids))
        moved = []
        for _, _, bodies in CHAIN[index:]:
            moved.extend(bodies)
        dofs.append({"name": name, "anchor": anchor, "k": stiffness, "moved": moved})
    return dofs, masses, com


def gravity_terms(dofs, masses, com):
    n = len(dofs)
    tau = []
    for dof in dofs:
        y0, z0 = dof["anchor"]
        tau.append(sum(masses[b] * G * (com[b][1] - z0) for b in dof["moved"]))
    gmat = [[0.0] * n for _ in range(n)]
    for i in range(n):
        for j in range(n):
            k = max(i, j)
            yk = dofs[k]["anchor"][0]
            gmat[i][j] = sum(masses[b] * G * (com[b][0] - yk) for b in dofs[k]["moved"])
    return tau, gmat


def normalized_min_eig(k, gmat, start):
    """Smallest eigenvalue of K^-1/2 (K - G) K^-1/2 over the sub-chain from
    DOF `start` up; G's sub-block is exact because G_ij depends only on the
    more distal joint. Positive means the springs out-stiffen gravity."""
    idx = range(start, len(k))
    scaled = [[((k[i] if i == j else 0.0) - gmat[i][j]) / math.sqrt(k[i] * k[j]) for j in idx] for i in idx]
    return jacobi_eigenvalues(scaled)[0]


def stability_rows(directory, run_id, load, arm, factors):
    dofs, masses, com = chain_model(directory, run_id)
    tau, gmat = gravity_terms(dofs, masses, com)
    rows = []
    for factor in factors:
        k = [dof["k"] * factor for dof in dofs]
        kmg = [[(k[i] if i == j else 0.0) - gmat[i][j] for j in range(len(k))] for i in range(len(k))]
        eig = jacobi_eigenvalues(kmg)
        spine = normalized_min_eig(k, gmat, 2)
        trunk = normalized_min_eig(k, gmat, 1)
        full = normalized_min_eig(k, gmat, 0)
        bias = [math.degrees(-tau[i] / k[i]) * ANATOMICAL_SIGN[dofs[i]["name"]] for i in range(len(k))]
        # Analytic, not a sweep: the normalized margin at factor f is
        # 1 - mu/f with mu the largest eigenvalue of K^-1/2 G K^-1/2, so the
        # smallest stable factor for each chain is mu measured in 1x units.
        base = [dof["k"] for dof in dofs]
        thresholds = [1.0 - normalized_min_eig(base, gmat, start) for start in (2, 1, 0)]
        rows.append([
            load, arm, run_id, factor, sum(masses.values()), masses.get("barbell", math.nan),
        ] + [dof["k"] * factor for dof in dofs] + tau + [gmat[i][i] for i in range(len(k))] + eig + [
            spine, spine > 0.0, trunk, trunk > 0.0, full, full > 0.0,
        ] + thresholds + bias + [max(abs(b) for b in bias), max(abs(b) for b in bias) <= 12.0])
    return rows


STABILITY_HEADER = [
    "load_kg", "arm", "run_id", "impedance_factor", "system_mass_kg", "bar_mass_kg",
    "k_knee_pair", "k_hip_pair", "k_lumbar", "k_thorax",
    "tau_knee_nm", "tau_hip_nm", "tau_lumbar_nm", "tau_thorax_nm",
    "g_knee", "g_hip", "g_lumbar", "g_thorax",
    "eig1_k_minus_g", "eig2_k_minus_g", "eig3_k_minus_g", "eig4_k_minus_g",
    "spine_normalized_min_eig", "spine_stable", "hip_spine_normalized_min_eig", "hip_spine_stable",
    "knee_hip_spine_normalized_min_eig", "knee_hip_spine_stable",
    "min_stable_factor_spine", "min_stable_factor_hip_spine", "min_stable_factor_knee_hip_spine",
    "required_knee_bias_deg", "required_hip_bias_deg", "required_abdomen_bias_deg", "required_thorax_bias_deg",
    "max_required_bias_deg", "within_12_deg",
]


# ---------------------------------------------------------------- arms

def read_arm(directory):
    runs = {}
    for name in sorted(os.listdir(directory)):
        if not name.startswith("causal-summary-"):
            continue
        for row in read_csv(Path(directory) / name):
            profile = f"p{row['athlete_position_iterations']}-v{row['athlete_velocity_iterations']}"
            load = int(float(row["load_kg"]))
            row["_run_id"] = f"{profile}-{row['intervention']}-{load}kg"
            runs[load] = row
    return runs


def first_capture(row):
    ticks = [onset(row, e) for e in CAPTURE_EVENTS if onset(row, e) is not None]
    return min(ticks) if ticks else None


def guard_class(base, arm):
    b, a = first_capture(base), first_capture(arm)
    same_class = base["stage_a_pass"] == arm["stage_a_pass"]
    support_kept = onset(arm, "support_loss") is None
    if b is not None and a is None and support_kept:
        return "PREVENTS"
    if b is not None and a is not None and a - b > SOLVER_BAND_TICKS:
        return "DELAYS"
    if (b is None and a is None and same_class) or (
            b is not None and a is not None and abs(a - b) <= SOLVER_BAND_TICKS and same_class):
        return "NO_CHANGE"
    return "WORSENS"


ARM_HEADER = [
    "arm", "load_kg", "intervention", "stage_a_pass", "qualified", "upright",
    "posture_joint_error_onset", "posture_gross_onset", "guard_withdrawal_onset",
    "capture_departure_onset", "capture_departure_hull_onset", "support_loss_onset",
    "drive_high_onset", "drive_saturation_onset", "saddle_linear_limit_onset",
    "min_hull_capture_margin_m", "max_load_bearing_demand", "max_saddle_linear_occupancy",
    "stage_a_summary",
]


def arm_row(arm, load, row):
    return [
        arm, load, row["intervention"], row["stage_a_pass"], row.get("qualified", "NA"), row["upright"],
        onset(row, "posture_joint_error"), onset(row, "posture_gross"), onset(row, "guard_withdrawal"),
        onset(row, "capture_departure"), onset(row, "capture_departure_hull"), onset(row, "support_loss"),
        onset(row, "drive_high"), onset(row, "drive_saturation"), onset(row, "saddle_linear_limit"),
        num(row.get("min_hull_capture_margin_m")), num(row.get("max_load_bearing_demand")),
        num(row.get("max_saddle_linear_occupancy")), row["stage_a_summary"],
    ]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--arm", action="append", required=True,
                        help="label=directory; the first arm is the reference for the guard rule")
    parser.add_argument("--guard-arm", help="label of the guard-off arm")
    parser.add_argument("--stability-factors", default="1,3")
    parser.add_argument("--stability-arm", default=None,
                        help="label of the 1x arm whose spawn geometry feeds the static check (default: first arm)")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    factors = [float(f) for f in args.stability_factors.split(",")]
    arms = []
    for spec in args.arm:
        label, directory = spec.split("=", 1)
        arms.append((label, directory, read_arm(directory)))

    rows = []
    for label, _, runs in arms:
        for load in sorted(runs):
            rows.append(arm_row(label, load, runs[load]))
    write_csv(output / "arm-comparison.csv", ARM_HEADER, rows)

    if args.guard_arm:
        reference = arms[0][2]
        guard = next(runs for label, _, runs in arms if label == args.guard_arm)
        guard_rows = []
        for load in sorted(reference):
            if load not in guard:
                continue
            base, arm = reference[load], guard[load]
            guard_rows.append([
                load, first_capture(base), first_capture(arm), onset(base, "posture_gross"),
                onset(arm, "posture_gross"), onset(base, "support_loss"), onset(arm, "support_loss"),
                base["stage_a_pass"], arm["stage_a_pass"], guard_class(base, arm)])
        write_csv(output / "guard-classification.csv", [
            "load_kg", "reference_first_capture_onset", "guard_off_first_capture_onset",
            "reference_posture_gross_onset", "guard_off_posture_gross_onset",
            "reference_support_loss_onset", "guard_off_support_loss_onset",
            "reference_stage_a_pass", "guard_off_stage_a_pass", "classification"], guard_rows)

    stability = []
    stability_label = args.stability_arm or arms[0][0]
    for label, directory, runs in arms:
        if label != stability_label:
            continue
        for load in sorted(runs):
            run_id = runs[load]["_run_id"]
            if (Path(directory) / f"joints-{run_id}.csv").exists():
                stability.extend(stability_rows(directory, run_id, load, label, factors))
    if stability:
        write_csv(output / "static-stability.csv", STABILITY_HEADER, stability)

    audit = []
    header = None
    for label, directory, _ in arms:
        for name in sorted(os.listdir(directory)):
            if not name.startswith("equilibrium-audit-"):
                continue
            for row in read_csv(Path(directory) / name):
                if header is None:
                    header = ["arm"] + list(row.keys())
                audit.append([label] + list(row.values()))
    if audit:
        write_csv(output / "equilibrium-audit.csv", header, audit)


if __name__ == "__main__":
    main()
