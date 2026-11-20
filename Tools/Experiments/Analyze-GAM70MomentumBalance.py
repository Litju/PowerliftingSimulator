"""GAM-70 Gate 5 momentum-balance reconstruction from gate5-dynamics-*.csv traces.

Read-only, stdlib only. Per completed tick (dt = 0.01 s) it reconstructs:
  * linear momentum balance of the athlete+bar system:
        dP/dt = F_feet + F_plates + F_other_floor + M g
    Unity contact reports carry normal impulses only, so the vertical
    residual validates the instrumentation and the horizontal rate of change
    is the friction the soles had to supply (inferred, not measured);
  * the bar alone: m_b a_b - F_plates - m_b g = force the hands put on the bar
    (exact grip wrench, compared with the last-substep Joint.currentForce);
  * angular momentum of the system about its own COM, L_c = L_O - c x P
    (sagittal component about world X), and its rate;
  * segment sagittal angular acceleration (finite difference of omega_x);
  * first TRUE zero-normal contact loss per foot (no loaded point), kept
    separate from the legacy 20 N "support-loss" warning (diagnostic only).

Usage: python -I Tools/Experiments/Analyze-GAM70MomentumBalance.py [csv ...]
"""
from __future__ import annotations

import csv
import math
import os
import sys

DT = 0.01
G = -9.81
WARN_N = 20.0   # legacy analyzer threshold: a labeled diagnostic, never a contact-loss definition
SEGMENTS = ["pelvis", "abdomen", "thorax", "head_neck", "left_upper_arm", "right_upper_arm", "left_forearm",
            "right_forearm", "left_hand", "right_hand", "left_thigh", "right_thigh", "left_shank", "right_shank",
            "left_foot", "right_foot"]


def num(row: dict, key: str) -> float:
    try:
        return float(row[key])
    except (KeyError, TypeError, ValueError):
        return float("nan")


def vec(row: dict, prefix: str, keys=("x", "y", "z")) -> list[float]:
    return [num(row, prefix + k) for k in keys]


def cross(a, b):
    return [a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0]]


def analyze(path: str) -> str:
    with open(path, newline="") as h:
        rows = list(csv.DictReader(h))
    if len(rows) < 3:
        return f"{path}: too short"
    name = rows[0]["case"]
    ma, mb = num(rows[0], "Am"), num(rows[0], "Bm")
    m = ma + mb
    out = [f"=== {name}  (athlete {ma:.1f} kg, bar {mb:.1f} kg)"]
    sep = next((i for i, r in enumerate(rows) if r["phase"] == "Separated"), None)
    worst_vres, worst_bar_grip = 0.0, 0.0
    lc_max, lc_tick, fric_max, fric_tick = 0.0, None, 0.0, None
    zero_l = zero_r = warn = None
    for i in range(1, len(rows)):
        r, p = rows[i], rows[i - 1]
        tick = int(num(r, "tick"))
        Pa, Pb = vec(r, "AP"), vec(r, "BP")
        Pa0, Pb0 = vec(p, "AP"), vec(p, "BP")
        dP = [(Pa[k] + Pb[k] - Pa0[k] - Pb0[k]) / DT for k in range(3)]
        F = [num(r, f"fLF{k}") + num(r, f"fRF{k}") + num(r, f"plF{k}") + num(r, f"fOF{k}") for k in "xyz"]
        vres = dP[1] - (F[1] + m * G)
        if sep is not None and i > sep and warn is None:
            worst_vres = max(worst_vres, abs(vres))
        fric = math.hypot(dP[0] - F[0], dP[2] - F[2])
        if fric > fric_max:
            fric_max, fric_tick = fric, tick
        # bar: hands' force on the bar from the bar's own momentum balance
        dPb = [(Pb[k] - Pb0[k]) / DT for k in range(3)]
        hands_on_bar_y = dPb[1] - num(r, "plFy") - num(r, "aBFy") - mb * G
        raw_y = -(num(r, "gLFy") + num(r, "gRFy"))
        if sep is not None and i > sep:
            worst_bar_grip = max(worst_bar_grip, abs(hands_on_bar_y - raw_y))
        # angular momentum about the system COM (sagittal, world X)
        ca, cb = vec(r, "Ac"), vec(r, "Bc")
        c = [(ma * ca[k] + mb * cb[k]) / m for k in range(3)]
        L = [num(r, f"AL{k}") + num(r, f"BL{k}") for k in "xyz"]
        P = [Pa[k] + Pb[k] for k in range(3)]
        lc = L[0] - cross(c, P)[0]
        if abs(lc) > lc_max:
            lc_max, lc_tick = abs(lc), tick
        if sep is not None and i > sep:
            nl, nr = num(r, "fLFy"), num(r, "fRFy")
            if zero_l is None and nl <= 0.0:
                zero_l = tick
            if zero_r is None and nr <= 0.0:
                zero_r = tick
            if warn is None and min(nl, nr) < WARN_N:
                warn = tick
    out.append(f"  separation entry tick {int(num(rows[sep], 'tick')) if sep is not None else None}")
    out.append(f"  vertical linear-momentum residual, separation to first contact warning, max |dP/dt - (F_contacts + M g)| = {worst_vres:.1f} N")
    out.append(f"  bar balance vs Joint.currentForce (last substep), max |difference| = {worst_bar_grip:.1f} N")
    out.append(f"  inferred sole friction (horizontal dP/dt), max {fric_max:.1f} N at tick {fric_tick}")
    out.append(f"  sagittal angular momentum about system COM, max |L_c,x| {lc_max:.2f} kg m^2/s at tick {lc_tick}")
    out.append(f"  TRUE zero-normal contact loss after separation: left {zero_l} right {zero_r}"
               f" | diagnostic <{WARN_N:.0f} N warning: {warn}")
    # angular acceleration snapshot around the first warning / end
    probe = [t for t in (sep, (warn - int(num(rows[0], 'tick'))) if warn else None) if t is not None]
    for idx in probe:
        if idx <= 0 or idx >= len(rows) - 1:
            continue
        r0, r1 = rows[idx], rows[idx + 1]
        acc = {s: (num(r1, "w_" + s) - num(r0, "w_" + s)) / DT for s in SEGMENTS}
        top = sorted(acc.items(), key=lambda kv: -abs(kv[1]))[:5]
        out.append(f"  tick {int(num(r1, 'tick'))}: largest sagittal segment angular accelerations (rad/s^2) " +
                   ", ".join(f"{k} {v:+.1f}" for k, v in top))
    return "\n".join(out)


def main(argv: list[str]) -> int:
    paths = argv[1:]
    if not paths:
        d = "Logs/GAM70"
        paths = sorted(os.path.join(d, f) for f in os.listdir(d) if f.startswith("gate5-dynamics-")) if os.path.isdir(d) else []
    for p in paths:
        print(analyze(p))
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
