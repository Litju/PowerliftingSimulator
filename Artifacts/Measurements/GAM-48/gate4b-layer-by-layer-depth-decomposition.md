# GAM-48 Gate 4b — layer-by-layer depth decomposition

Run: 20260923-221554
Unity: 6000.3.22f1
Arms: C0_FULL and HOLD_1.00_FULL, each captured in its own Unity process
Load/phase: 25 kg, phase 1, descent
Held window: 200 ticks; values below are the per-side means over the final 50 ticks
Coordinate/units: Unity world W, +Y up; meters; positive delta means shallower

## Landmark contract

The physical rule and physical-target FK use production hip/knee ConfigurableJoint anchors and SquatDepthGeometry. D_REF_RULE_PROXY is the canonical GAM-10 solution's hip/knee joint-center geometry evaluated with that same rule metric. D_REF_GAM10_CALIBRATED_LANDMARKS is the separately qualified crease/top-offset reference depth. These two reference measurements differ by 98.8549 mm, so legality cannot be transferred from one to the other.

The FK reads the exact runtime adapter quaternion targets, the physical rig's neutral parent-child orientation and J-space basis, and each production ConfigurableJoint anchor/connectedAnchor. It uses the adapter's phase-1 mapped pelvis body rotation as the same root carrier for every target arm. The physical rule observation supplies D_ACTUAL. No reference solve is used to reconstruct a physical target.

## Target composition table

All values are meters; legal means bilateral worst side <= -0.005 m.

| State | Left | Right | Worst side | Legal |
|---|---:|---:|---:|---|
| D_REF_RULE_PROXY | 0.0225417614 | 0.0225417614 | 0.0225417614 | No |
| D_REF_GAM10_CALIBRATED_LANDMARKS | -0.07631308 | -0.07631314 | -0.07631308 | Yes |
| D_NOMINAL_TARGET | 0.02254184 | 0.02254184 | 0.02254184 | No |
| D_GRAVITY_TARGET | 0.02254184 | 0.02254184 | 0.02254184 | No |
| D_BALANCE_TARGET | 0.02226769 | 0.0222676918 | 0.0222676918 | No |
| D_FINAL_TARGET | 0.02226769 | 0.0222676918 | 0.0222676918 | No |
| D_APPLIED_TARGET | 0.02226769 | 0.0222676918 | 0.0222676918 | No |
| D_ACTUAL | 0.0431753546 | 0.0401087478 | 0.0431753546 | No |

## Signed layer differences

| Difference | Left | Right | Worst side |
|---|---:|---:|---:|
| Nominal mapping: D_NOMINAL_TARGET - D_REF_RULE_PROXY | 0.0000000782 | 0.0000000782 | 0.0000000782 |
| Gravity composition: D_GRAVITY_TARGET - D_NOMINAL_TARGET | 0 | 0 | 0 |
| Balance composition: D_BALANCE_TARGET - D_NOMINAL_TARGET | -0.0002741497 | -0.0002741478 | -0.0002741478 |
| Full composition: D_FINAL_TARGET - D_NOMINAL_TARGET | -0.0002741497 | -0.0002741478 | -0.0002741478 |
| Rate limit: D_APPLIED_TARGET - D_FINAL_TARGET | 0 | 0 | 0 |
| Physical realization: D_ACTUAL - D_APPLIED_TARGET | +0.0209076647 | +0.0178410560 | +0.0209076647 |

The maximum per-joint FINAL-to-APPLIED angular error during the settled window was 0.0200017449 rad on each foot target. Those ankle differences do not move the production hip/knee anchor depth landmarks; measured rate-limit depth displacement is 0 m.

## Repeatability and validity

- Fresh C0/HOLD processes passed the existing Gate 3 equivalence check and the Gate 4b comparison. Maximum difference across all reported left/right/worst-side depths and layer deltas: 0 m; modeled-demand difference: 0.
- Both arms retained support and finite valid control.
- Runtime FK is deterministic and read-only in the test: repeated target evaluation matched within 1e-7 m, and rigid-body pose, rotation, velocity, angular velocity, and sleep state remained unchanged.
- C0/HOLD settled maximum MODELED_DRIVE_DEMAND was 0.266502559; MODELED_DRIVE_DEMAND_HIGH was false at the 0.95 threshold.
- Numeric comparison tolerance: 1e-5 m. Material contribution threshold: 0.005 m.

## Classification

The production joint-space mapping reproduces the matched GAM-10 hip/knee joint-center reference within 0.0001 mm. Gravity, balance, full-composition, and rate-limit depth contributions are each below 5 mm. Physical realization makes the settled result another 20.908 mm shallower on the worst side.

The matched rule-proxy reference is itself shallow, while the qualified GAM-10 crease/top reference is legal and differs by 98.855 mm. The specified legal-reference decision branch therefore cannot be applied across these unlike landmark definitions. Keep the cause non-unique between the reference-to-rule landmark contract and the measured physical-realization gap; do not label this a unique joint-space mapping, target-composition, or physical-realization root cause.

Claim ceiling: current Unity/PhysX runtime observations and rule-proxy geometry only.
