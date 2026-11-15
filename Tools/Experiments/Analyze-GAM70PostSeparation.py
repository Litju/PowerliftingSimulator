"""GAM-70 Gate 4 post-separation diagnosis from the ownership/normalization traces.

Read-only analysis of committed per-tick CSVs. It reports the first support
loss, task-space COM/COP state, vertical external-force residuals and the
ankle channel authority at that tick. It never changes a physics value.

Usage: python Tools/Experiments/Analyze-GAM70PostSeparation.py [csv ...]
"""
from __future__ import annotations

import csv
import math
import os
import sys

WA = 100.0 * 9.81      # athlete weight, N (GAM-6 profile)
WB = 75.0 * 9.81       # bar weight, N
LOSS_N = 20.0          # "support lost" observation threshold, N
DEFAULT = [
    "Logs/GAM70/gate4-ownership-A-legacy-overwrite.csv",
    "Logs/GAM70/gate4-ownership-B-explicit-handoff.csv",
    "Logs/GAM70/gate4-ownership-N0-control-schedule.csv",
    "Logs/GAM70/gate4-ownership-N1-static-h0.csv",
    "Logs/GAM70/gate4-ownership-N2-measured-h0.csv",
]


def f(row: dict, key: str) -> float:
    try:
        return float(row[key])
    except (KeyError, TypeError, ValueError):
        return float("nan")


def combined_cop(row: dict) -> tuple[float, float]:
    nl, nr = f(row, "footL"), f(row, "footR")
    xl, zl, xr, zr = f(row, "copLx"), f(row, "copLz"), f(row, "copRx"), f(row, "copRz")
    if math.isnan(xl) and math.isnan(xr):
        return float("nan"), float("nan")
    if math.isnan(xl):
        return xr, zr
    if math.isnan(xr):
        return xl, zl
    n = nl + nr
    return (nl * xl + nr * xr) / n, (nl * zl + nr * zr) / n


def analyze(path: str) -> str:
    with open(path, newline="") as h:
        rows = list(csv.DictReader(h))
    if not rows:
        return f"{path}: empty"
    name = rows[0]["case"]
    sep = next((i for i, r in enumerate(rows) if f(r, "frac") > 0.0), None)
    ramp = next((i for i, r in enumerate(rows) if f(r, "frac") >= 0.999), None)
    loss = None
    if sep is not None:
        loss = next((i for i, r in enumerate(rows[sep:], sep)
                     if min(f(r, "footL"), f(r, "footR")) < LOSS_N), None)
    out = [f"=== {name} ({path})", f"ticks {len(rows)}  separation entry {sep}  ramp end {ramp}  first support loss {loss}"]
    for label, idx in (("ramp end", ramp), ("first loss", loss), ("last", len(rows) - 1)):
        if idx is None:
            continue
        r = rows[idx]
        cx, cz = combined_cop(r)
        comx, comz = f(r, "comX"), f(r, "comZ")
        dx, dz = (comx - cx, comz - cz) if not math.isnan(cx) else (float("nan"), float("nan"))
        gsum = f(r, "gripLy") + f(r, "gripRy")
        out.append(
            f"  {label:10s} t={int(f(r, 'tick')):4d} owner={r['owner']:18s} phase={r['phase']:10s} frac={f(r, 'frac'):.2f} "
            f"barY={f(r, 'barY'):.4f} barVy={f(r, 'barVy'):+.3f}")
        out.append(
            f"             feet L/R {f(r, 'footL'):7.1f}/{f(r, 'footR'):7.1f} N  plates {f(r, 'plateL') + f(r, 'plateR'):6.1f} N  "
            f"grip sum {gsum:7.1f} N  handMeas {f(r, 'handMeas'):6.1f} N")
        out.append(
            f"             COM ({comx:+.4f},{f(r, 'comY'):.4f},{comz:+.4f}) m  v ({f(r, 'vx'):+.3f},{f(r, 'vy'):+.3f},{f(r, 'vz'):+.3f}) m/s")
        out.append(
            f"             COP ({cx:+.4f},{cz:+.4f}) m  COM-COP horiz ({dx * 1000:+.1f},{dz * 1000:+.1f}) mm  "
            f"|.| {math.hypot(dx, dz) * 1000:.1f} mm")
        out.append(
            f"             athlete ext-vert residual {f(r, 'footL') + f(r, 'footR') + gsum - WA:+.1f} N  "
            f"bar ext-vert residual {f(r, 'plateL') + f(r, 'plateR') - gsum - WB:+.1f} N")
        out.append(
            f"             ref ankle X L/R {f(r, 'refALX'):6.1f}/{f(r, 'refARX'):6.1f} N m  "
            f"actual X {f(r, 'aXLact'):+7.2f}/{f(r, 'aXRact'):+7.2f} deg  applied X {f(r, 'aXLapp'):+7.2f}/{f(r, 'aXRapp'):+7.2f} deg")
        out.append(
            f"             ankle X demand L/R {f(r, 'aXLdem'):7.1f}/{f(r, 'aXRdem'):7.1f} N m "
            f"({f(r, 'aXLfrac'):.2f}/{f(r, 'aXRfrac'):.2f} x {f(r, 'aXLcap'):.0f} N m) prox {f(r, 'aXLprox'):.3f}/{f(r, 'aXRprox'):.3f} "
            f"solver X {f(r, 'aXLsol'):+7.1f}/{f(r, 'aXRsol'):+7.1f} N m")
        out.append(
            f"             ankle Z demand L/R {f(r, 'aXZdem'):7.1f}/{f(r, 'aZRdem'):7.1f} N m "
            f"({f(r, 'aXZfrac'):.2f}/{f(r, 'aZRfrac'):.2f} x {f(r, 'aXZcap'):.0f} N m) prox {f(r, 'aXZprox'):.3f}/{f(r, 'aZRprox'):.3f} "
            f"solver Z {f(r, 'aXZsol'):+7.1f}/{f(r, 'aZRsol'):+7.1f} N m gain L/R {f(r, 'regGL'):.2f}/{f(r, 'regGR'):.2f}")
    return "\n".join(out)


def main(argv: list[str]) -> int:
    paths = argv[1:] or DEFAULT
    for p in paths:
        if not os.path.exists(p):
            print(f"missing {p}")
            continue
        print(analyze(p))
        print()
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
