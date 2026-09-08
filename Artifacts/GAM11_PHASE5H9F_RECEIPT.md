======================================================================
GAM-11 UPPER LIMB PROXY COLLIDER RECONCILIATION RECEIPT
======================================================================
PHASE: PHASE5H9F
STATUS: COMPLETE_READY_FOR_REVIEW

COLLIDER_SEMANTICS:
- Forearm: Capsule (R=0.0475m, H=0.2046m, dir=Y)
- Thorax: Box (size=0.36m x 0.28m x 0.20m)
- Coupling to inertia: YES (DimensionsMeters drives both collider and BoxInertia)

CLASSIFICATION:
- Root cause: CLASS_C (COARSE_PROXY_PAIR_FALSE_POSITIVE)
- Empirical proofs:
  1. Diagnostic A: exact pair suppression restored elbow from 91.24° to 105.53° (left) / 108.87° (right) (reference target 105.80°)
  2. Diagnostic B: forearm radius sweep proved 2% radius (0.0010m needle) still penetrated 90.6mm inside thorax proxy box (Class A falsified)
  3. Diagnostic C: thorax proxy sweep proved 70% width reduction (to 10.8cm width) required to clear forearm bone axis, destroying bar/back geometry (Class B falsified)
  4. Visible envelope: zero visible mesh intersection between forearm skin and torso skin across full 10s setup

REPAIR_APPLIED:
- Policy: PhysicalAthleteSelfCollisionPolicy
- Suppressed pairs:
  - left_forearm <-> thorax (reason: COARSE_PROXY_FALSE_POSITIVE_IN_ACCEPTED_SQUAT_SETUP)
  - right_forearm <-> thorax (reason: COARSE_PROXY_FALSE_POSITIVE_IN_ACCEPTED_SQUAT_SETUP)
- FOREARM_MASS_CHANGED: NO
- FOREARM_COM_CHANGED: NO
- FOREARM_INERTIA_CHANGED: NO
- BROAD_SELF_COLLISION_DISABLED: NO

FINAL_GATE_RESULTS:
- Left elbow angle: 105.53° (target 105.80°, error 0.27°) [was 91.24°]
- Right elbow angle: 108.87° (target 105.80°, error 3.07°) [was 91.24°]
- Left shoulder error: 8.76° [was 18.42°]
- Right shoulder error: 10.36° [was 18.42°]
- Left wrist error: 26.80°
- Right wrist error: 9.17°
- Thorax-forearm contacts at t=10s: 0 [was 760 ticks]
- Max foot slip: 0.0075 m/s (budget <= 0.0100 m/s)
- Whole-body COM AP margin: 0.1255 m
- Passive fall gate: PASSED (dropped > 0.04m within 1.0s when unpowered)
- 100 Hz performance:
  - physics step p95: 0.3211 ms (budget <= 2.0 ms) -> PASS
  - catch-up frame p95: 1.2221 ms (budget <= 8.0 ms) -> PASS
  - foundation frame p95: 0.3469 ms (budget <= 10.0 ms) -> PASS
  - controller execution p95: 0.0502 ms (budget <= 0.25 ms) -> PASS

EVIDENCE_ARTIFACTS:
- Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation/final_front_t10000ms.png
- Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation/final_side_t10000ms.png
- Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation/final_oblique_t10000ms.png
- Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation/final_leftarm_t10000ms.png
- Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation/final_rightarm_t10000ms.png
- Artifacts/Measurements/GAM-11/GAM11-upper-limb-collider-semantics.csv
- Artifacts/Measurements/GAM-11/GAM11-target-pose-feasibility.csv
- Artifacts/Measurements/GAM-11/GAM11-visible-forearm-envelope.csv
- Artifacts/Measurements/GAM-11/GAM11-p0-contact-census.csv
- Artifacts/Measurements/GAM-11/GAM11-p0-contact-trace.csv
- Artifacts/Measurements/GAM-11/GAM11-pair-suppression-census.csv
- Artifacts/Measurements/GAM-11/GAM11-pair-suppression-trace.csv
- Artifacts/Measurements/GAM-11/GAM11-pair-suppression-task-space.csv
- Artifacts/Measurements/GAM-11/GAM11-forearm-radius-sweep.csv
- Artifacts/Measurements/GAM-11/GAM11-thorax-sensitivity.csv
- Artifacts/Measurements/GAM-11/GAM11-final-contact-census.csv
- Artifacts/Measurements/GAM-11/GAM11-final-contact-trace.csv
- Artifacts/Measurements/GAM-11/GAM11-final-posture-comparison.csv
- Artifacts/Measurements/GAM-11/GAM11-final-task-space.csv
- Artifacts/Measurements/GAM-11/GAM11-upper-limb-perf-budget.csv

COMMITS:
1. eefbcaa: test(physics): characterize upper-limb proxy collision geometry
2. cb13eec: test(physics): discriminate collider geometry from collision policy
3. 45981fc: fix(physics): codify qualified forearm-thorax collision exception
4. 7170502: test(physics): qualify final physical squat setup pose
5. 150c9d4: perf(physics): verify final setup collision model

NEXT_ACTION:
- Request owner visual review of rendered evidence frames.
- Linear GAM-11 status remains: IN_PROGRESS.
======================================================================
