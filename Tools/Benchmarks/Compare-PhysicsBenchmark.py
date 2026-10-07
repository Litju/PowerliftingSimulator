"""Physics Benchmark V1 aggregation and comparison (GAM-50).

stdlib only. Subcommands:

  manifest  --bench-root DIR [--receipt FILE]
      Writes DIR/manifest.json: git SHA, Unity version, project physics
      settings, solver/drive/saddle constants scraped from source, and
      SHA-256 hashes of every physics-relevant source/config file.

  aggregate --bench-root DIR
      Merges DIR/runs/*/raw/*.json (latest run wins per case) into
      DIR/results.json, DIR/failure-matrix.json and DIR/summary.md.

  compare   --before DIR --after DIR [--out FILE]
      Metric-by-metric before/after table for repair evidence.
"""

import argparse
import datetime
import glob
import hashlib
import json
import os
import re
import subprocess
import sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))

LAYERS = {
    1: "units / coordinate frames / equations",
    2: "mass / COM / inertia",
    3: "joint topology / axes / target convention",
    4: "drive semantics",
    5: "constraint convergence",
    6: "contact / friction",
    7: "bar / saddle load path",
    8: "timestep / solver numerical convergence",
    9: "athlete equilibrium",
    10: "controller",
    11: "gameplay qualification",
}

HASHED_SOURCES = [
    "ProjectSettings/DynamicsManager.asset",
    "ProjectSettings/TimeManager.asset",
    "ProjectSettings/ProjectVersion.txt",
    "Assets/Scripts/Foundation/CoordinatesAndUnits.cs",
    "Assets/Scripts/Foundation/Unity/PhysicsTickDriver.cs",
    "Assets/Scripts/Foundation/Unity/AuthoritativePhysicsScene.cs",
    "Assets/Scripts/Foundation/Unity/FoundationRuntime.cs",
    "Assets/Scripts/Athlete/PhysicalAthleteDefinition.cs",
    "Assets/Scripts/Athlete/PhysicalAthleteRig.cs",
    "Assets/Scripts/Athlete/PhysicalAthleteSolverProfile.cs",
    "Assets/Scripts/Athlete/PhysicalAthleteSelfCollisionPolicy.cs",
    "Assets/Scripts/Athlete/PoweredJointController.cs",
    "Assets/Scripts/Equipment/BarbellConfiguration.cs",
    "Assets/Scripts/Equipment/PhysicalBarbell.cs",
    "Assets/Scripts/Squat/Unity/SquatBarSaddle.cs",
    "Assets/Scripts/Squat/Unity/SquatPhysicalAdapter.cs",
    "Assets/Scripts/Squat/Unity/SquatPhysicalPrototypeController.cs",
    "Assets/Scripts/Squat/Unity/SquatComStabilizerV2.cs",
    "Assets/Scripts/Squat/Unity/SquatEquilibriumPreload.cs",
    "Assets/Scripts/Squat/Unity/SquatReferenceKinematics.cs",
    "Assets/Scripts/Squat/SquatReferenceMotion.cs",
    "Assets/Scripts/Squat/SquatStaticTrimSolver.cs",
    "Assets/Scripts/Squat/SquatFailureDetector.cs",
    "Assets/Scripts/Squat/SquatAttemptLifecycle.cs",
]


def read(path):
    with open(path, encoding="utf-8") as handle:
        return handle.read()


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1 << 16), b""):
            digest.update(chunk)
    return digest.hexdigest()


def git(*args):
    return subprocess.run(["git", "-C", ROOT] + list(args), capture_output=True, text=True).stdout.strip()


def yaml_value(text, key):
    match = re.search(r"^\s*" + re.escape(key) + r":\s*(.+)$", text, re.MULTILINE)
    return match.group(1).strip() if match else None


def scrape_constants():
    joint = read(os.path.join(ROOT, "Assets/Scripts/Athlete/PoweredJointController.cs"))
    profiles = {}
    for m in re.finditer(r'new JointFamilyProfile\("(\w+)",\s*([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f,\s*([\d.]+)f\)', joint):
        profiles[m.group(1)] = {
            "spring_nm_per_rad": float(m.group(2)),
            "damper_nm_s_per_rad": float(m.group(3)),
            "base_capacity_nm": float(m.group(4)),
            "max_target_rate_rad_s": float(m.group(5)),
        }
    saddle = read(os.path.join(ROOT, "Assets/Scripts/Squat/Unity/SquatBarSaddle.cs"))
    saddle_constants = {m.group(1): float(m.group(2)) for m in re.finditer(r"public const float (\w+) = ([\d.]+)f;", saddle)}
    solver = read(os.path.join(ROOT, "Assets/Scripts/Athlete/PhysicalAthleteSolverProfile.cs"))
    units = read(os.path.join(ROOT, "Assets/Scripts/Foundation/CoordinatesAndUnits.cs"))
    definition = read(os.path.join(ROOT, "Assets/Scripts/Athlete/PhysicalAthleteDefinition.cs"))
    segments = {}
    for m in re.finditer(r'new PhysicalSegmentRecipe\("(\w+)",\s*(?:null|"\w+"),[^;]*?,\s*([\d.]+)f,\s*PhysicalColliderKind', definition):
        segments[m.group(1)] = float(m.group(2))
    strength = []
    for path in glob.glob(os.path.join(ROOT, "Assets/Scripts/**/*.cs"), recursive=True):
        for m in re.finditer(r"(\w*AthleteStrengthScale\w*)\s*=\s*([\d.]+)f", read(path)):
            strength.append({"file": os.path.relpath(path, ROOT).replace("\\", "/"), "symbol": m.group(1), "value": float(m.group(2))})
    return {
        "fixed_dt_s": float(re.search(r"ProductionFixedDeltaTimeSeconds = ([\d.]+)d", units).group(1)),
        "physics_substeps_per_tick": int(re.search(r"PhysicsSubstepsPerTick = (\d+);", units).group(1)),
        "athlete_position_iterations": int(re.search(r"PositionIterations = (\d+);", solver).group(1)),
        "athlete_velocity_iterations": int(re.search(r"VelocityIterations = (\d+);", solver).group(1)),
        "barbell_position_iterations": 12,
        "barbell_velocity_iterations": 6,
        "island_effective_iterations_note": "PhysX solves an island at the max per-body iteration count: bar-on-athlete island runs 28 position / 6 velocity.",
        "athlete_prototype_body_mass_kg": float(re.search(r"PrototypeBodyMassKg = ([\d.]+)f", definition).group(1)),
        "athlete_segment_mass_fractions": segments,
        "athlete_body_linear_damping": 0.04,
        "athlete_body_angular_damping": 0.08,
        "athlete_body_max_angular_velocity": 35.0,
        "joint_family_profiles": profiles,
        "saddle": saddle_constants,
        "contact_materials": {
            "foot": {"static": 1.0, "dynamic": 1.0, "combine": "Maximum"},
            "platform": {"static": 0.85, "dynamic": 0.75, "combine": "Average"},
            "effective_foot_platform": {"static": 1.0, "dynamic": 1.0, "rule": "PhysX combine priority Maximum > Average"},
        },
        "athlete_strength_scale_assignments": strength,
    }


def read_run_metadata(run_dir):
    metadata_path = os.path.join(run_dir, "run.json")
    if not os.path.exists(metadata_path):
        return {}
    with open(metadata_path, encoding="utf-8-sig") as handle:
        return json.load(handle)


def run_metadata_rows(bench_root):
    rows = []
    for run_dir in sorted(glob.glob(os.path.join(bench_root, "runs", "*"))):
        if not os.path.isdir(run_dir):
            continue
        metadata = read_run_metadata(run_dir)
        if not metadata:
            continue
        component = os.path.basename(run_dir)
        rows.append({
            "run_id": metadata.get("run_id", component),
            "label": metadata.get("label", ""),
            "path_component": component,
            "run_path": os.path.relpath(run_dir, ROOT).replace("\\", "/"),
        })
    return rows


def cmd_manifest(args):
    dynamics = read(os.path.join(ROOT, "ProjectSettings/DynamicsManager.asset"))
    time_manager = read(os.path.join(ROOT, "ProjectSettings/TimeManager.asset"))
    version = read(os.path.join(ROOT, "ProjectSettings/ProjectVersion.txt"))
    manifest = {
        "schema": "PHYSICS_BENCHMARK_V1_MANIFEST",
        "created_utc": datetime.datetime.now(datetime.timezone.utc).isoformat(),
        "git_sha": git("rev-parse", "HEAD"),
        "git_branch": git("rev-parse", "--abbrev-ref", "HEAD"),
        "working_tree_physics_dirty": bool(git("status", "--porcelain", "--", "Assets/Scripts", "ProjectSettings")),
        "unity_version": yaml_value(version, "m_EditorVersionWithRevision"),
        "project_physics": {
            "gravity": yaml_value(dynamics, "m_Gravity"),
            "default_solver_iterations": yaml_value(dynamics, "m_DefaultSolverIterations"),
            "default_solver_velocity_iterations": yaml_value(dynamics, "m_DefaultSolverVelocityIterations"),
            "default_contact_offset": yaml_value(dynamics, "m_DefaultContactOffset"),
            "bounce_threshold": yaml_value(dynamics, "m_BounceThreshold"),
            "sleep_threshold": yaml_value(dynamics, "m_SleepThreshold"),
            "auto_simulation": yaml_value(dynamics, "m_AutoSimulation"),
            "auto_sync_transforms": yaml_value(dynamics, "m_AutoSyncTransforms"),
            "solver_type": yaml_value(dynamics, "m_SolverType"),
            "friction_type": yaml_value(dynamics, "m_FrictionType"),
            "enhanced_determinism": yaml_value(dynamics, "m_EnableEnhancedDeterminism"),
            "improved_patch_friction": yaml_value(dynamics, "m_ImprovedPatchFriction"),
            "simulation_mode": yaml_value(dynamics, "m_SimulationMode"),
            "default_max_angular_speed": yaml_value(dynamics, "m_DefaultMaxAngularSpeed"),
            "time_manager_fixed_timestep": yaml_value(time_manager, "Fixed Timestep"),
            "simulation_owner": "AuthoritativePhysicsScene local PhysicsScene, stepped by PhysicsTickDriver.StepOne via PhysicsScene.Simulate(SimulationConstants.FixedDeltaTimeSeconds); TimeManager fixed timestep does not drive the athlete.",
        },
        "production_constants": scrape_constants(),
        "source_hashes": {p: sha256(os.path.join(ROOT, p)) for p in HASHED_SOURCES if os.path.exists(os.path.join(ROOT, p))},
        "runs": run_metadata_rows(args.bench_root),
    }
    if args.receipt and os.path.exists(args.receipt):
        with open(args.receipt, encoding="utf-8-sig") as handle:
            manifest["runtime_receipt"] = json.load(handle)
        manifest["runtime_receipt_path"] = os.path.relpath(args.receipt, ROOT).replace("\\", "/")
    os.makedirs(args.bench_root, exist_ok=True)
    out = os.path.join(args.bench_root, "manifest.json")
    with open(out, "w", encoding="utf-8") as handle:
        json.dump(manifest, handle, indent=2)
    print("MANIFEST", out)


def load_raw(bench_root):
    cases = {}
    for path in sorted(glob.glob(os.path.join(bench_root, "runs", "*", "raw", "*.json"))):
        run_dir = os.path.dirname(os.path.dirname(path))
        run_path_component = os.path.basename(run_dir)
        run_metadata = read_run_metadata(run_dir)
        with open(path, encoding="utf-8") as handle:
            data = json.load(handle)
        # raw/ also holds runtime receipts and oracle exports; only benchmark cases aggregate.
        if data.get("schema") != "PHYSICS_BENCHMARK_V1" or "metrics" not in data:
            continue
        key = data["case"]
        # The path component starts with the run timestamp, so it remains a compact sort key.
        if key not in cases or cases[key]["run_path_component"] <= run_path_component:
            data["run_id"] = run_metadata.get("run_id", run_path_component)
            data["run_path_component"] = run_path_component
            data["label"] = run_metadata.get("label", "")
            data["raw_path"] = os.path.relpath(path, ROOT).replace("\\", "/")
            cases[key] = data
    return cases


def severity(metric):
    tol = metric.get("tolerance")
    err = metric.get("abs_error")
    kind = metric.get("tolerance_kind")
    observed = metric.get("observed")
    if observed is None:
        return "critical"
    try:
        if kind == "Relative" and metric.get("rel_error") is not None:
            ratio = metric["rel_error"] / tol
        elif kind in ("UpperBound", "LowerBound"):
            ratio = abs(observed) / abs(tol) if tol else float("inf")
            if kind == "LowerBound":
                ratio = abs(tol) / max(abs(observed), 1e-12)
        else:
            ratio = err / tol
    except (TypeError, ZeroDivisionError):
        return "critical"
    if ratio >= 3:
        return "critical"
    if ratio >= 1.5:
        return "major"
    return "minor"


def cmd_aggregate(args):
    cases = load_raw(args.bench_root)
    repairs_path = os.path.join(os.path.dirname(__file__), "repairs.json")
    repairs = {}
    if os.path.exists(repairs_path):
        with open(repairs_path, encoding="utf-8") as handle:
            for row in json.load(handle):
                repairs[(row["case"], row["metric"])] = row
    # Evidence-based localization overlay: a metric's built-in layer is where
    # its symptom shows; when a discriminating experiment proves an earlier or
    # different root-cause layer, the reassignment and its evidence are
    # recorded here, never by editing the metric.
    localization_path = os.path.join(os.path.dirname(__file__), "localization.json")
    localization = []
    if os.path.exists(localization_path):
        with open(localization_path, encoding="utf-8") as handle:
            localization = json.load(handle)
    results = []
    failures = []
    for key in sorted(cases):
        data = cases[key]
        for metric in data["metrics"]:
            metric = dict(metric)
            metric["symptom_layer"] = metric["layer"]
            for rule in localization:
                if re.match(rule["case"], metric["case"]) and re.match(rule["metric"], metric["metric"]) and                         re.search(rule.get("configuration", ""), metric["configuration"]):
                    metric["layer"] = rule["layer"]
                    metric["localization_evidence"] = rule["evidence"]
                    break
            metric["run_id"] = data["run_id"]
            metric["run_path_component"] = data["run_path_component"]
            metric["label"] = data.get("label", "")
            metric["raw_path"] = data["raw_path"]
            results.append(metric)
            if metric["gated"] and not metric["pass"]:
                failures.append(metric)
    earliest = min((f["layer"] for f in failures), default=None)
    matrix = []
    for f in failures:
        repair = repairs.get((f["case"], f["metric"]), {})
        matrix.append({
            "benchmark": f["case"],
            "metric": f["metric"],
            "configuration": f["configuration"],
            "expected": f["expected"],
            "observed": f["observed"],
            "error": f["abs_error"],
            "relative_error": f["rel_error"],
            "tolerance": f["tolerance"],
            "tolerance_kind": f["tolerance_kind"],
            "tolerance_source": f["tolerance_source"],
            "severity": severity(f),
            "earliest_causal_layer": f["layer"],
            "earliest_causal_layer_name": LAYERS.get(f["layer"], f["layer_name"]),
            "symptom_layer": f.get("symptom_layer"),
            "localization_evidence": f.get("localization_evidence"),
            "failure_meaning": f["failure_meaning"],
            "evidence": f["raw_path"],
            "repair_authorized": f["layer"] == earliest,
            "repair_blocked_by_layer": None if f["layer"] == earliest else earliest,
            "repair_commit": repair.get("repair_commit"),
            "post_repair_result": repair.get("post_repair_result"),
        })
    with open(os.path.join(args.bench_root, "results.json"), "w", encoding="utf-8") as handle:
        json.dump({"schema": "PHYSICS_BENCHMARK_V1_RESULTS", "cases": sorted(cases), "metrics": results}, handle, indent=2)
    with open(os.path.join(args.bench_root, "failure-matrix.json"), "w", encoding="utf-8") as handle:
        json.dump({"schema": "PHYSICS_BENCHMARK_V1_FAILURE_MATRIX", "earliest_failing_layer": earliest,
                   "rows": matrix}, handle, indent=2)

    lines = ["# Physics Benchmark V1 summary", "",
             "Bench root: `%s`" % os.path.relpath(args.bench_root, ROOT).replace("\\", "/"), ""]
    lines.append("| case | run | gated | pass | fail |")
    lines.append("|---|---|---:|---:|---:|")
    for key in sorted(cases):
        gated = [m for m in cases[key]["metrics"] if m["gated"]]
        passed = sum(1 for m in gated if m["pass"])
        lines.append("| %s | %s | %d | %d | %d |" % (key, cases[key]["run_path_component"], len(gated), passed, len(gated) - passed))
    lines.append("")
    if earliest is None:
        lines.append("**All gated metrics pass.**")
    else:
        lines.append("**Earliest failing layer: %d (%s).**" % (earliest, LAYERS.get(earliest)))
        lines.append("")
        lines.append("| sev | layer | case | metric | config | expected | observed | tol |")
        lines.append("|---|---:|---|---|---|---:|---:|---:|")
        for row in sorted(matrix, key=lambda r: (r["earliest_causal_layer"], r["benchmark"])):
            lines.append("| %s | %d | %s | %s | %s | %s | %s | %s %s |" % (
                row["severity"], row["earliest_causal_layer"], row["benchmark"], row["metric"], row["configuration"],
                fmt(row["expected"]), fmt(row["observed"]), fmt(row["tolerance"]), row["tolerance_kind"]))
    with open(os.path.join(args.bench_root, "summary.md"), "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines) + "\n")
    print("AGGREGATE cases=%d metrics=%d failures=%d earliest_layer=%s" % (len(cases), len(results), len(failures), earliest))


def fmt(value):
    if value is None:
        return "NA"
    if isinstance(value, float):
        return "%.6g" % value
    return str(value)


def cmd_compare(args):
    before = {(m["case"], m["metric"], m["configuration"]): m for m in json.load(open(os.path.join(args.before, "results.json")))["metrics"]}
    after = {(m["case"], m["metric"], m["configuration"]): m for m in json.load(open(os.path.join(args.after, "results.json")))["metrics"]}
    lines = ["| case | metric | config | before | after | before pass | after pass |", "|---|---|---|---:|---:|---|---|"]
    for key in sorted(set(before) | set(after)):
        b = before.get(key)
        a = after.get(key)
        if (b and not b["gated"]) and (a and not a["gated"]):
            continue
        if b and a and b["pass"] == a["pass"] and b["observed"] == a["observed"]:
            continue
        lines.append("| %s | %s | %s | %s | %s | %s | %s |" % (key[0], key[1], key[2],
                     fmt(b["observed"]) if b else "-", fmt(a["observed"]) if a else "-",
                     b["pass"] if b else "-", a["pass"] if a else "-"))
    text = "\n".join(lines) + "\n"
    if args.out:
        with open(args.out, "w", encoding="utf-8") as handle:
            handle.write(text)
    print(text)


def main():
    parser = argparse.ArgumentParser()
    sub = parser.add_subparsers(dest="command", required=True)
    m = sub.add_parser("manifest")
    m.add_argument("--bench-root", required=True)
    m.add_argument("--receipt")
    a = sub.add_parser("aggregate")
    a.add_argument("--bench-root", required=True)
    c = sub.add_parser("compare")
    c.add_argument("--before", required=True)
    c.add_argument("--after", required=True)
    c.add_argument("--out")
    args = parser.parse_args()
    {"manifest": cmd_manifest, "aggregate": cmd_aggregate, "compare": cmd_compare}[args.command](args)


if __name__ == "__main__":
    sys.exit(main())
