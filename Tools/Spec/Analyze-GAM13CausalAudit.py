#!/usr/bin/env python3
"""Offline analysis of GAM-13 causal-audit traces (GAM13_CAUSAL_ONSET_PREDICATES_V1).

Stdlib only. Reads the per-run files written by GAM13CausalAuditTests
(trace-, bodies-, bodymeta-, contacts-, causal-summary-*.csv) and writes
derived evidence tables. Nothing here changes a predicate: every rule it
applies is the frozen one in Artifacts/Research/GAM-13-causal-onset-predicates.md;
derived views that go beyond V1 are labelled as such in their column names.
"""

import argparse
import csv
import gzip
import math
import os
import re
from pathlib import Path

DT = 0.01
G = 9.81
MU = 1.0
MU_BAR_HAND = 0.625
SADDLE_SPRING = 50000.0
SADDLE_DAMPER = 3000.0
MATERIAL_MARGIN_M = 0.01

CANONICAL = [
    "DRIVE_HIGH", "DRIVE_SATURATION", "TRACKING_FAILURE", "POSTURE_DEPARTURE",
    "CAPTURE_DEPARTURE", "SUPPORT_LOSS", "SADDLE_LINEAR_LIMIT", "SADDLE_GROSS_FAILURE",
    "CONTACT_MODE_CHANGE",
]
SUPPLEMENTARY = [
    "POSTURE_JOINT_ERROR", "POSTURE_GROSS", "GUARD_WITHDRAWAL", "CAPTURE_DEPARTURE_HULL",
    "COM_OUTSIDE_HULL", "FOOT_LIFT", "FOOT_SLIP", "PLANTAR_COUNT_CHANGE", "BAR_THORAX_CONTACT",
    "BAR_ATHLETE_CONTACT", "NONPLANTAR_GROUND_CONTACT", "SADDLE_ANGULAR_LIMIT",
]


# ---------------------------------------------------------------- utilities

def num(value):
    try:
        x = float(value)
    except (TypeError, ValueError):
        return math.nan
    return x


def fmt(value, digits=6):
    if value is None:
        return "NA"
    if isinstance(value, bool):
        return "true" if value else "false"
    if isinstance(value, int):
        return str(value)
    if isinstance(value, float):
        if math.isnan(value):
            return "NA"
        return f"{value:.{digits}g}"
    return str(value)


def read_csv(path):
    path = Path(path)
    if not path.exists() and Path(str(path) + ".gz").exists():
        path = Path(str(path) + ".gz")
    if path.suffix == ".gz":
        with gzip.open(path, "rt", newline="") as handle:
            return list(csv.DictReader(handle))
    with open(path, newline="") as handle:
        return list(csv.DictReader(handle))


def write_csv(path, header, rows):
    with open(path, "w", newline="") as handle:
        writer = csv.writer(handle, lineterminator="\n")
        writer.writerow(header)
        for row in rows:
            writer.writerow([fmt(value) for value in row])


def median(values):
    values = sorted(v for v in values if not math.isnan(v))
    if not values:
        return math.nan
    mid = len(values) // 2
    return values[mid] if len(values) % 2 else 0.5 * (values[mid - 1] + values[mid])


def percentile(values, q):
    values = sorted(v for v in values if not math.isnan(v))
    if not values:
        return math.nan
    index = min(len(values) - 1, max(0, int(round(q * (len(values) - 1)))))
    return values[index]


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


def sub(a, b):
    return (a[0] - b[0], a[1] - b[1], a[2] - b[2])


def scale(a, s):
    return (a[0] * s, a[1] * s, a[2] * s)


def dot(a, b):
    return a[0] * b[0] + a[1] * b[1] + a[2] * b[2]


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0])


def norm(a):
    return math.sqrt(dot(a, a))


def qmul(a, b):
    ax, ay, az, aw = a
    bx, by, bz, bw = b
    return (
        aw * bx + ax * bw + ay * bz - az * by,
        aw * by - ax * bz + ay * bw + az * bx,
        aw * bz + ax * by - ay * bx + az * bw,
        aw * bw - ax * bx - ay * by - az * bz,
    )


def qrot(q, v):
    qv = (v[0], v[1], v[2], 0.0)
    qc = (-q[0], -q[1], -q[2], q[3])
    r = qmul(qmul(q, qv), qc)
    return (r[0], r[1], r[2])


def qinv_rot(q, v):
    return qrot((-q[0], -q[1], -q[2], q[3]), v)


# ---------------------------------------------------------------- run files

RUN_PATTERN = re.compile(r"^trace-(p\d+-v\d+)-(.+)-(\d+)kg\.csv(\.gz)?$")


class Run:
    def __init__(self, directory, name):
        match = RUN_PATTERN.match(name)
        self.directory = Path(directory)
        self.profile = match.group(1)
        self.intervention = match.group(2)
        self.load = int(match.group(3))
        self.run_id = f"{self.profile}-{self.intervention}-{self.load}kg"
        self.trace = read_csv(self.directory / name)
        self.by_tick = {int(row["tick"]): row for row in self.trace}
        self.ticks = sorted(self.by_tick)
        self._bodies = None
        self._meta = None
        self._contacts = None

    def col(self, name):
        return [num(self.by_tick[t][name]) for t in self.ticks]

    @property
    def bodies(self):
        if self._bodies is None:
            self._bodies = {int(r["tick"]): r for r in read_csv(self.directory / f"bodies-{self.run_id}.csv")}
        return self._bodies

    @property
    def meta(self):
        if self._meta is None:
            self._meta = {r["body"]: r for r in read_csv(self.directory / f"bodymeta-{self.run_id}.csv")}
        return self._meta

    @property
    def contacts(self):
        if self._contacts is None:
            grouped = {}
            for row in read_csv(self.directory / f"contacts-{self.run_id}.csv"):
                grouped.setdefault(int(row["tick"]), []).append(row)
            self._contacts = grouped
        return self._contacts

    def onsets(self, summary):
        return summary.get(self.run_id, {})


def discover(directory):
    runs = []
    for name in sorted(os.listdir(directory)):
        if RUN_PATTERN.match(name):
            runs.append(Run(directory, name))
    return runs


def read_summaries(directory):
    table = {}
    for name in os.listdir(directory):
        if not name.startswith("causal-summary-"):
            continue
        for row in read_csv(Path(directory) / name):
            profile = f"p{row['athlete_position_iterations']}-v{row['athlete_velocity_iterations']}"
            run_id = f"{profile}-{row['intervention']}-{int(float(row['load_kg']))}kg"
            onsets = {}
            for event in CANONICAL + SUPPLEMENTARY:
                value = row.get(event.lower() + "_onset_tick", "NA")
                onsets[event] = None if value in ("NA", "") else int(value)
            onsets["_row"] = row
            table[run_id] = onsets
    return table


# ---------------------------------------------------------------- causal order

def persistent_onset(run, event):
    """Start of the last uninterrupted true run of sig_<event> (derived view)."""
    column = "sig_" + event
    values = [run.by_tick[t][column] == "1" for t in run.ticks]
    if not values or not values[-1]:
        # Not true at the end: report the start of the longest run instead.
        best_start, best_len, start = None, 0, None
        for index, flag in enumerate(values + [False]):
            if flag and start is None:
                start = index
            elif not flag and start is not None:
                if index - start > best_len:
                    best_len, best_start = index - start, start
                start = None
        return None if best_start is None else run.ticks[best_start]
    index = len(values) - 1
    while index > 0 and values[index - 1]:
        index -= 1
    return run.ticks[index]


def causal_order(runs, summary, reference_load=25):
    rows = []
    stable = {}
    for run in runs:
        if run.load == reference_load:
            stable[(run.profile, run.intervention)] = run.onsets(summary)
    for run in runs:
        onsets = run.onsets(summary)
        reference = stable.get((run.profile, run.intervention), {})
        ordered = sorted(
            ((onsets.get(e), CANONICAL.index(e), e) for e in CANONICAL if onsets.get(e) is not None))
        v1 = ">".join(f"{e}@{t}" for t, _, e in ordered) or "NONE"
        discriminating = [
            (t, i, e) for t, i, e in ordered if reference.get(e) is None or run.load == reference_load]
        non_disc = [e for t, i, e in ordered if reference.get(e) is not None and run.load != reference_load]
        disc = ">".join(f"{e}@{t}" for t, _, e in discriminating) or "NONE"
        supp = sorted(((onsets.get(e), e) for e in SUPPLEMENTARY if onsets.get(e) is not None))
        persistent = []
        for event in CANONICAL:
            if onsets.get(event) is not None:
                persistent.append(f"{event}@{persistent_onset(run, event)}")
        rows.append([
            run.load, run.profile, run.intervention, v1, disc, "|".join(non_disc) or "NONE",
            ">".join(f"{e}@{t}" for t, e in supp) or "NONE", "|".join(persistent) or "NONE",
            onsets.get("_row", {}).get("stage_a_pass", "NA"),
        ] + [onsets.get(e) for e in CANONICAL + SUPPLEMENTARY])
    header = [
        "load_kg", "profile", "intervention", "canonical_order_v1", "discriminating_order_v1",
        "non_discriminating_events_also_at_25kg", "supplementary_order_v1", "derived_persistent_onsets",
        "stage_a_pass",
    ] + [e.lower() + "_onset_tick" for e in CANONICAL + SUPPLEMENTARY]
    return header, rows


# ---------------------------------------------------------------- load path

def body_state(row, body):
    get = lambda f: num(row[f"{body}_{f}"])
    return {
        "c": (get("cx"), get("cy"), get("cz")),
        "v": (get("vx"), get("vy"), get("vz")),
        "q": (get("qx"), get("qy"), get("qz"), get("qw")),
        "w": (get("wx"), get("wy"), get("wz")),
    }


def point_velocity(state, point):
    return add(state["v"], cross(state["w"], sub(point, state["c"])))


def bar_contact_sign(run):
    """+1 if reported bar-probe impulses act on the bar, -1 if on the other body.

    A pushing contact on a cylinder acts toward its axis, so the impulse on
    the bar has a negative component along the outward radial direction.
    """
    votes = []
    for tick, contacts in run.contacts.items():
        row = run.by_tick.get(tick)
        if row is None:
            continue
        origin = (num(row["bar_x"]), num(row["bar_y"]), num(row["bar_z"]))
        q = (num(row["bar_qx"]), num(row["bar_qy"]), num(row["bar_qz"]), num(row["bar_qw"]))
        axis = qrot(q, (1.0, 0.0, 0.0))
        for c in contacts:
            if c["body"] != "barbell" or c["other"] == "platform":
                continue
            j = (num(c["impulse_x"]), num(c["impulse_y"]), num(c["impulse_z"]))
            if norm(j) < 1e-5:
                continue
            offset = sub((num(c["point_x"]), num(c["point_y"]), num(c["point_z"])), origin)
            radial = sub(offset, scale(axis, dot(offset, axis)))
            if norm(radial) < 1e-4:
                continue
            votes.append(dot(j, radial) / (norm(j) * norm(radial)))
    if not votes:
        return 1.0, 0, math.nan
    agreement = median(votes)
    return (1.0 if agreement < 0 else -1.0), len(votes), agreement


def load_path(run, summary):
    """Bar momentum balance by elimination.

    F_total = m*a - m*g - F_damp is the whole non-gravity force on the bar.
    Measured contact normals are removed; what remains is the saddle joint
    plus the unreported contact friction, bounded by mu * |F_normal|.
    """
    onsets = run.onsets(summary)
    mass = num(run.by_tick[run.ticks[0]]["bar_mass_kg"])
    damping = num(run.meta["barbell"]["linear_damping"])
    weight = mass * G
    sign, votes, agreement = bar_contact_sign(run)
    samples = []
    for index in range(1, len(run.ticks)):
        t, tp = run.ticks[index], run.ticks[index - 1]
        row, prev = run.by_tick[t], run.by_tick[tp]
        if row["saddle_attached"] != "true" or int(float(row["bar_ground_contacts"])) > 0:
            continue
        v = (num(row["bar_vx"]), num(row["bar_vy"]), num(row["bar_vz"]))
        vp = (num(prev["bar_vx"]), num(prev["bar_vy"]), num(prev["bar_vz"]))
        accel = scale(sub(v, vp), 1.0 / DT)
        total = sub(sub(scale(accel, mass), (0.0, -weight, 0.0)), scale(v, -mass * damping))
        normal_sum = 0.0
        normal_vec = (0.0, 0.0, 0.0)
        for c in run.contacts.get(t, []):
            if c["body"] != "barbell" or c["other"] == "platform":
                continue
            j = scale((num(c["impulse_x"]), num(c["impulse_y"]), num(c["impulse_z"])), sign / DT)
            normal_vec = add(normal_vec, j)
            normal_sum += norm(j)
        joint_plus_friction = sub(total, normal_vec)
        sep_y = num(row["saddle_sep_y"])
        samples.append({
            "tick": t, "total_y": total[1], "contact_y": normal_vec[1], "joint_y": joint_plus_friction[1],
            "friction_bound": MU_BAR_HAND * normal_sum, "accel": norm(accel), "sep_y": sep_y,
            "occupancy": num(row["saddle_linear_occupancy"]),
            "engine": norm((num(row["saddle_force_x"]), num(row["saddle_force_y"]), num(row["saddle_force_z"]))),
        })

    def window(end_events, start=10):
        ends = [onsets.get(e) for e in end_events if onsets.get(e) is not None]
        end = min(ends) if ends else run.ticks[-1] + 1
        return [s for s in samples if start <= s["tick"] < end], end

    results = {}
    windows = {
        "v1": window(["POSTURE_DEPARTURE", "SUPPORT_LOSS", "SADDLE_GROSS_FAILURE"]),
        "pre_gross": window(["POSTURE_GROSS", "SUPPORT_LOSS", "SADDLE_GROSS_FAILURE"]),
        "attached": window(["SADDLE_GROSS_FAILURE"], start=1),
    }
    for key, (chosen, end) in windows.items():
        share = [s["joint_y"] / s["total_y"] for s in chosen if s["total_y"] > 0.2 * weight]
        low = [(s["joint_y"] - s["friction_bound"]) / s["total_y"] for s in chosen if s["total_y"] > 0.2 * weight]
        high = [(s["joint_y"] + s["friction_bound"]) / s["total_y"] for s in chosen if s["total_y"] > 0.2 * weight]
        quasi = [s for s in chosen if s["accel"] < 0.5 and s["sep_y"] > 0.001 and s["occupancy"] < 0.95]
        realized = [s["joint_y"] / (SADDLE_SPRING * s["sep_y"]) for s in quasi]
        results[key] = {
            "samples": len(chosen), "end": end,
            "total_y": median([s["total_y"] / weight for s in chosen]),
            "contact_y": median([s["contact_y"] / weight for s in chosen]),
            "joint_y": median([s["joint_y"] / weight for s in chosen]),
            "friction_bound": median([s["friction_bound"] / weight for s in chosen]),
            "share": median(share), "share_low": median(low), "share_high": median(high),
            "share_low_p05": percentile(low, 0.05), "share_high_p95": percentile(high, 0.95),
            "quasi_static_samples": len(quasi), "drive_realized_fraction": median(realized),
        }
    engine_nonzero = [s for s in samples if s["engine"] > 1.0]
    at_limit = [s for s in engine_nonzero if s["occupancy"] >= 0.95]
    return {
        "mass": mass, "contact_sign": sign, "contact_sign_votes": votes,
        "contact_sign_radial_agreement": agreement, "windows": results,
        "engine_nonzero_ticks": len(engine_nonzero), "engine_nonzero_at_limit": len(at_limit),
        "samples": len(samples),
    }


# ---------------------------------------------------------------- support geometry

def support_geometry(run, summary):
    onsets = run.onsets(summary)
    out = {}
    for label, event in (("proxy", "CAPTURE_DEPARTURE"), ("hull", "CAPTURE_DEPARTURE_HULL")):
        tick = onsets.get(event)
        out[label + "_tick"] = tick
        if tick is None or tick not in run.by_tick:
            for q in ("com", "cop", "capture"):
                for kind in ("hull", "aabb", "aabb_ap"):
                    out[f"{label}_{q}_{kind}"] = math.nan
            continue
        row = run.by_tick[tick]
        for q in ("com", "cop", "capture"):
            out[f"{label}_{q}_hull"] = num(row[f"{q}_hull_margin_m"])
            out[f"{label}_{q}_aabb"] = num(row[f"{q}_aabb_margin_m"])
            out[f"{label}_{q}_aabb_ap"] = num(row[f"{q}_aabb_ap_margin_m"])
    end = onsets.get("SUPPORT_LOSS") or run.ticks[-1] + 1
    window = [t for t in run.ticks if t < end]
    for q in ("com", "cop", "capture"):
        d2 = [abs(num(run.by_tick[t][f"{q}_hull_margin_m"]) - num(run.by_tick[t][f"{q}_aabb_margin_m"])) for t in window]
        dap = [abs(num(run.by_tick[t][f"{q}_hull_margin_m"]) - num(run.by_tick[t][f"{q}_aabb_ap_margin_m"])) for t in window]
        out[f"max_abs_hull_minus_aabb2d_{q}"] = max((x for x in d2 if not math.isnan(x)), default=math.nan)
        out[f"max_abs_hull_minus_ap_proxy_{q}"] = max((x for x in dap if not math.isnan(x)), default=math.nan)
    hull_tick = onsets.get("CAPTURE_DEPARTURE_HULL")
    near = [t for t in run.ticks if hull_tick is None or t <= hull_tick]
    for q in ("com", "capture"):
        for kind, column in (("aabb2d", "aabb_margin_m"), ("ap_proxy", "aabb_ap_margin_m")):
            values = [abs(num(run.by_tick[t][f"{q}_hull_margin_m"]) - num(run.by_tick[t][f"{q}_{column}"]))
                      for t in near]
            out[f"max_abs_hull_minus_{kind}_{q}_to_capture_exit"] = max(
                (x for x in values if not math.isnan(x)), default=math.nan)
    if hull_tick is not None:
        cop = [num(run.by_tick[t]["cop_hull_margin_m"]) for t in run.ticks if hull_tick - 20 <= t <= hull_tick]
        out["cop_hull_margin_min_pre_capture_exit"] = min((x for x in cop if not math.isnan(x)), default=math.nan)
        row = run.by_tick[hull_tick]
        out["capture_exit_direction"] = capture_exit_direction(row)
    else:
        out["cop_hull_margin_min_pre_capture_exit"] = math.nan
        out["capture_exit_direction"] = "NONE"
    proxy, hull = out["proxy_tick"], out["hull_tick"]
    out["proxy_lag_ticks"] = None if proxy is None or hull is None else proxy - hull
    out["material_disagreement"] = any(
        out[f"max_abs_hull_minus_{kind}_{q}"] >= MATERIAL_MARGIN_M
        for kind in ("aabb2d", "ap_proxy") for q in ("com", "capture")
        if not math.isnan(out[f"max_abs_hull_minus_{kind}_{q}"]))
    return out


def capture_exit_direction(row):
    ap = num(row["capture_aabb_ap_margin_m"])
    two_d = num(row["capture_aabb_margin_m"])
    if math.isnan(ap) or math.isnan(two_d):
        return "UNKNOWN"
    if ap > MATERIAL_MARGIN_M and two_d <= MATERIAL_MARGIN_M:
        return "MEDIOLATERAL"
    if ap <= MATERIAL_MARGIN_M:
        return "ANTEROPOSTERIOR" if (num(row["capture_ap"]) - num(row["support_ap_min"])) > \
            (num(row["support_ap_max"]) - num(row["capture_ap"])) else "ANTEROPOSTERIOR_REAR"
    return "INTERIOR"


# ---------------------------------------------------------------- wrench

def wrench(run, summary):
    onsets = run.onsets(summary)
    # 1. Tangential impulse observability in reported contacts.
    ratios, normals = [], 0
    for contacts in run.contacts.values():
        for c in contacts:
            if c["other"] != "platform" or c["body"] not in ("left_foot", "right_foot"):
                continue
            n = (num(c["normal_x"]), num(c["normal_y"]), num(c["normal_z"]))
            j = (num(c["impulse_x"]), num(c["impulse_y"]), num(c["impulse_z"]))
            jn = dot(j, n)
            if abs(jn) < 1e-6:
                continue
            normals += 1
            ratios.append(norm(sub(j, scale(n, jn))) / abs(jn))
    tangential_max = max(ratios) if ratios else math.nan

    # 2. Required external contact wrench from logged body states.
    bodies = [b for b in run.meta if b != "barbell"] + ["barbell"]
    masses = {b: num(run.meta[b]["mass_kg"]) for b in bodies}
    total = sum(masses.values())
    inertia = {b: (num(run.meta[b]["inertia_x"]), num(run.meta[b]["inertia_y"]), num(run.meta[b]["inertia_z"]))
               for b in bodies}
    irot = {b: (num(run.meta[b]["inertia_rot_x"]), num(run.meta[b]["inertia_rot_y"]),
                num(run.meta[b]["inertia_rot_z"]), num(run.meta[b]["inertia_rot_w"])) for b in bodies}

    def centroidal(tick):
        row = run.bodies[tick]
        states = {b: body_state(row, b) for b in bodies}
        com = scale(
            (sum(masses[b] * states[b]["c"][0] for b in bodies),
             sum(masses[b] * states[b]["c"][1] for b in bodies),
             sum(masses[b] * states[b]["c"][2] for b in bodies)), 1.0 / total)
        p = (0.0, 0.0, 0.0)
        l = (0.0, 0.0, 0.0)
        for b in bodies:
            s = states[b]
            p = add(p, scale(s["v"], masses[b]))
            l = add(l, scale(cross(sub(s["c"], com), s["v"]), masses[b]))
            rot = qmul(s["q"], irot[b])
            w_local = qinv_rot(rot, s["w"])
            hw = (inertia[b][0] * w_local[0], inertia[b][1] * w_local[1], inertia[b][2] * w_local[2])
            l = add(l, qrot(rot, hw))
        return com, p, l

    end_events = ["POSTURE_GROSS", "NONPLANTAR_GROUND_CONTACT", "SUPPORT_LOSS", "SADDLE_GROSS_FAILURE"]
    ends = [onsets.get(e) for e in end_events if onsets.get(e) is not None]
    end = min(ends) if ends else run.ticks[-1] + 1
    force_errors, cop_errors, samples = [], [], 0
    infeasible = {"normal": 0, "friction": 0, "cop_outside": 0}
    previous = centroidal(run.ticks[0])
    for t in run.ticks[1:]:
        current = centroidal(t)
        if not (10 <= t < end):
            previous = current
            continue
        force = sub(scale(sub(current[1], previous[1]), 1.0 / DT), (0.0, -total * G, 0.0))
        moment = scale(sub(current[2], previous[2]), 1.0 / DT)
        previous = current
        row = run.by_tick[t]
        measured_normal = (num(row["left_normal_impulse_n_s"]) + num(row["right_normal_impulse_n_s"])) / DT
        if measured_normal <= 1e-6 or row["cop_available"] != "true":
            continue
        samples += 1
        force_errors.append(abs(force[1] - measured_normal) / measured_normal)
        com = current[0]
        plane_y = cop_plane(run, t)
        dy = plane_y - com[1]
        if force[1] <= 1e-6:
            infeasible["normal"] += 1
            continue
        dz = (dy * force[2] - moment[0]) / force[1]
        dx = (moment[2] + dy * force[0]) / force[1]
        cop = (com[0] + dx, com[2] + dz)
        cop_errors.append(math.hypot(cop[0] - num(row["cop_x"]), cop[1] - num(row["cop_z"])))
        if math.hypot(force[0], force[2]) > MU * force[1]:
            infeasible["friction"] += 1
        if not inside_hull(plantar_points(run, t), cop):
            infeasible["cop_outside"] += 1
    force_p50 = median(force_errors)
    cop_p50 = median(cop_errors)
    validated = samples >= 20 and force_p50 <= 0.10 and cop_p50 <= 0.02
    tangential_reported = not math.isnan(tangential_max) and tangential_max >= 1e-4
    if validated:
        verdict = "NECESSARY_CONDITIONS_EVALUATED"
    else:
        verdict = "NOT_OBSERVABLE"
    return {
        "reported_contacts": normals, "tangential_to_normal_max": tangential_max,
        "tangential_reported": tangential_reported, "window_end": end, "samples": samples,
        "vertical_force_rel_error_p50": force_p50, "vertical_force_rel_error_p95": percentile(force_errors, 0.95),
        "cop_error_m_p50": cop_p50, "cop_error_m_p95": percentile(cop_errors, 0.95),
        "validated": validated, "infeasible_normal": infeasible["normal"],
        "infeasible_friction": infeasible["friction"], "infeasible_cop": infeasible["cop_outside"],
        "verdict": verdict,
    }


def plantar_points(run, tick):
    points = []
    for c in run.contacts.get(tick, []):
        if c["other"] == "platform" and c["body"] in ("left_foot", "right_foot"):
            points.append((num(c["point_x"]), num(c["point_z"])))
    return points


def cop_plane(run, tick):
    ys = [num(c["point_y"]) for c in run.contacts.get(tick, [])
          if c["other"] == "platform" and c["body"] in ("left_foot", "right_foot")]
    return sum(ys) / len(ys) if ys else 0.0


def hull(points):
    points = sorted(set(points))
    if len(points) <= 2:
        return points

    def turn(o, a, b):
        return (a[0] - o[0]) * (b[1] - o[1]) - (a[1] - o[1]) * (b[0] - o[0])

    lower, upper = [], []
    for p in points:
        while len(lower) >= 2 and turn(lower[-2], lower[-1], p) <= 0:
            lower.pop()
        lower.append(p)
    for p in reversed(points):
        while len(upper) >= 2 and turn(upper[-2], upper[-1], p) <= 0:
            upper.pop()
        upper.append(p)
    return lower[:-1] + upper[:-1]


def inside_hull(points, query, tolerance=1e-4):
    polygon = hull(points)
    if len(polygon) < 3:
        return False
    for index in range(len(polygon)):
        a, b = polygon[index], polygon[(index + 1) % len(polygon)]
        cross_value = (b[0] - a[0]) * (query[1] - a[1]) - (b[1] - a[1]) * (query[0] - a[0])
        length = math.hypot(b[0] - a[0], b[1] - a[1])
        if length > 0 and cross_value / length < -tolerance:
            return False
    return True


# ---------------------------------------------------------------- solver comparison

def leading_order(onsets):
    """Canonical events up to and including the terminal event (V1 'leading events')."""
    ordered = sorted((onsets[e], CANONICAL.index(e), e) for e in CANONICAL if onsets.get(e) is not None)
    terminal = [t for t, _, e in ordered if e in ("SUPPORT_LOSS", "SADDLE_GROSS_FAILURE")]
    cutoff = min(terminal) if terminal else None
    return [e for t, _, e in ordered if cutoff is None or t <= cutoff]


def compare_traces(a, b):
    """Bit-identity of every trace field except the profile label."""
    first_diff = None
    for t in a.ticks:
        ra, rb = a.by_tick.get(t), b.by_tick.get(t)
        if rb is None:
            return False, t
        for key, value in ra.items():
            if key == "profile":
                continue
            if rb.get(key) != value:
                first_diff = t
                return False, first_diff
    return True, None


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run-dir", action="append", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--tag", required=True)
    parser.add_argument("--solver-reference", default="p28-v1")
    parser.add_argument("--skip-wrench", action="store_true")
    args = parser.parse_args()

    output = Path(args.output)
    output.mkdir(parents=True, exist_ok=True)
    runs, summary = [], {}
    for directory in args.run_dir:
        runs.extend(discover(directory))
        summary.update(read_summaries(directory))

    header, rows = causal_order(runs, summary)
    write_csv(output / f"causal-order-{args.tag}.csv", header, rows)

    lp_rows = []
    for run in runs:
        lp = load_path(run, summary)
        for key, w in lp["windows"].items():
            lp_rows.append([
                run.load, run.profile, run.intervention, key, w["samples"], w["end"], lp["mass"],
                lp["contact_sign"], lp["contact_sign_votes"], lp["contact_sign_radial_agreement"],
                w["total_y"], w["contact_y"], w["joint_y"], w["friction_bound"], w["share"], w["share_low"],
                w["share_high"], w["share_low_p05"], w["share_high_p95"], w["quasi_static_samples"],
                w["drive_realized_fraction"], lp["engine_nonzero_ticks"], lp["engine_nonzero_at_limit"],
                lp["samples"]])
    write_csv(output / f"load-path-{args.tag}.csv", [
        "load_kg", "profile", "intervention", "window", "samples", "window_end_tick", "bar_mass_kg",
        "contact_impulse_sign", "contact_sign_votes", "contact_radial_cosine_p50",
        "bar_support_vertical_over_weight_p50", "arm_contact_normal_vertical_over_weight_p50",
        "joint_plus_friction_vertical_over_weight_p50", "friction_bound_over_weight_p50",
        "joint_vertical_share_p50", "joint_share_low_p50", "joint_share_high_p50", "joint_share_low_p05",
        "joint_share_high_p95", "quasi_static_samples", "drive_realized_stiffness_fraction_p50",
        "engine_force_nonzero_ticks", "engine_force_nonzero_at_linear_limit", "attached_samples"], lp_rows)

    sg_rows, sg_header = [], None
    for run in runs:
        sg = support_geometry(run, summary)
        if sg_header is None:
            sg_header = ["load_kg", "profile", "intervention"] + list(sg.keys())
        sg_rows.append([run.load, run.profile, run.intervention] + list(sg.values()))
    write_csv(output / f"support-geometry-{args.tag}.csv", sg_header, sg_rows)

    if not args.skip_wrench:
        wr_rows, wr_header = [], None
        for run in runs:
            wr = wrench(run, summary)
            if wr_header is None:
                wr_header = ["load_kg", "profile", "intervention"] + list(wr.keys())
            wr_rows.append([run.load, run.profile, run.intervention] + list(wr.values()))
        write_csv(output / f"wrench-observability-{args.tag}.csv", wr_header, wr_rows)

    reference = {(r.load, r.intervention): r for r in runs if r.profile == args.solver_reference}
    sc_rows = []
    for run in runs:
        ref = reference.get((run.load, run.intervention))
        if ref is None or ref is run:
            continue
        identical, first = compare_traces(ref, run)
        ref_on, run_on = ref.onsets(summary), run.onsets(summary)
        shifts = []
        for event in CANONICAL:
            a, b = ref_on.get(event), run_on.get(event)
            if a is None and b is None:
                continue
            shifts.append(f"{event}:{fmt(a)}->{fmt(b)}")
        order_ref = leading_order(ref_on)
        order_run = leading_order(run_on)
        max_shift = max((abs(ref_on[e] - run_on[e]) for e in CANONICAL
                         if ref_on.get(e) is not None and run_on.get(e) is not None), default=0)
        same_class = ref_on["_row"]["stage_a_pass"] == run_on["_row"]["stage_a_pass"]
        if order_ref != order_run or not same_class:
            classification = "SOLVER_CAUSAL"
        elif max_shift > 5 or set(order_ref) != set(order_run):
            classification = "SOLVER_QUANTITATIVE"
        else:
            classification = "SOLVER_INSENSITIVE"
        sc_rows.append([run.load, ref.profile, run.profile, run.intervention, identical, first,
                        ">".join(order_ref), ">".join(order_run), max_shift, same_class, classification,
                        "|".join(shifts)])
    if sc_rows:
        write_csv(output / f"solver-comparison-{args.tag}.csv", [
            "load_kg", "reference_profile", "profile", "intervention", "trace_bit_identical",
            "first_differing_tick", "reference_order", "profile_order", "max_shared_onset_shift_ticks",
            "same_stage_a_classification", "classification", "onset_shifts"], sc_rows)


if __name__ == "__main__":
    main()
