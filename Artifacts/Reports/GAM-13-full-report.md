# GAM-13 full evidence report

Date: 2026-09-18
Mission: `GAM13_END_TO_END_SQUAT_LOAD_RESPONSE_STICKING_AND_PHYSICAL_FAILURE_CALIBRATION`
Status: **BLOCKED — static trim refoundation found no qualified fixed plant because loaded saddle/contact topology fails at canonical endpoints**
Branch: `work/gam-13-squat-load-calibration`
Evidence head: `dd0d747a6f6ae46ed9e64105908e2fdf25267199` plus the trim-refoundation evidence candidate
Unity: `6000.3.22f1 (1c726e1fb402)`
Claim class: `GAME_ENGINE_CONTROL_CALIBRATION`

## Executive verdict

GAM-13 cannot continue to capacity, sticking, failure, or held-out-load
calibration. The current production 1x plant passes standing at 25 kg but fails
the Stage-A standing contract at 60, 140, 170, and 300 kg. The failure is
setup/posture/support collapse before a meaningful squat attempt; it is not a
measured athlete-capacity ceiling.

The new dynamic experiment strengthens the conclusion without overstating it:

- 25 kg has a valid local equilibrium window, but its fitted model has a
  small one-step error and catastrophic held-out 50-tick free prediction error.
- 60/140/170/300 kg have zero valid local-equilibrium rows in both
  identification and validation trajectories; those samples are retained as
  `TRANSIENT_LOCAL_RESPONSE`, not mislabeled LTI plants.
- Native solver settings and a higher 24/8 diagnostic setting both pass 25 kg
  and fail 60/300 kg. The result is not explained by solver iteration count
  alone.

This is enough to block synthesis. It is not evidence that every conceivable
future target-space controller is mathematically impossible; it is evidence
that no controller can be responsibly qualified from the currently measured
plant family.

## Scope and claim discipline

This report distinguishes:

- static/local sensitivity from a dynamic model;
- dynamic controllability from stabilizability;
- stabilizability from robust stability over load variation;
- transient pre-collapse response from equilibrium linearization;
- same-machine repeatability from physical-model certainty;
- qualitative human squat observations from this game's rigid-body model.

The report makes no claim of measured human torque, measured human GRF/COP,
muscle activation, physiological fatigue, biological strength, or
cross-platform PhysX determinism.

## Production plant and experiment

The selected plant is `GAM13_PRODUCTION_PLANT_V1`: the existing 1x production
load-bearing impedance. The earlier 8x substrate remains diagnostic evidence
only and was not used to fit this production model.

The Unity PlayMode fixture used fresh scenes, the production saddle and feet,
the fixed 100 Hz simulation, the existing bounded balance loop, and a
default-zero additive ankle residual solely for test-time excitation. It ran
two deterministic multisine identification trajectories and two held-out
validation trajectories at each canonical load. Each trajectory contained 100
warm-up ticks and 800 excitation ticks. Inputs in the raw CSV are the actual
post-rate-limit target residuals.

Evidence:

- [dynamic identification excitation](../Measurements/GAM-13/dynamic-id-excitation.csv)
- [dynamic validation trajectories](../Measurements/GAM-13/dynamic-id-validation.csv)
- [fitted model JSON](../Measurements/GAM-13/dynamic-plant-models.json)
- [model summary](../Measurements/GAM-13/dynamic-plant-summary.csv)
- [load transition map](../Measurements/GAM-13/load-transition-map.csv)
- [reproducible analysis script](../../Tools/Spec/Analyze-GAM13DynamicPlant.py)

The local ARX/state-space candidate uses state/output coordinates
`[COM_AP, COM_AP_velocity, COP_AP, capture_AP, trunk_pitch]` and three actual
applied target residual inputs: ankle, hip, and trunk.

## Stage-A standing qualification

The current production CLI run used the fixed production plant, no experimental
impedance override, and fresh scenes at all canonical loads. The fixture holds
setup for 600 ticks: 100 settle ticks and 500 measured ticks.

| Load | Result | Min pelvis (m) | Max trunk (rad) | Min capture margin (m) | Max COM speed (m/s) | Support |
|---:|---|---:|---:|---:|---:|---|
| 25 kg | **PASS** | 0.9777 | 0.1456 | 0.1079 | 0.0059 | retained |
| 60 kg | **FAIL** | 0.1038 | 2.3943 | -1.3407 | 2.3484 | lost |
| 140 kg | **FAIL** | 0.0923 | 1.6007 | -1.6603 | 2.3727 | unstable |
| 170 kg | **FAIL** | 0.1005 | 1.8516 | -1.6647 | 2.4794 | unstable |
| 300 kg | **FAIL** | 0.0987 | 2.9239 | -1.2651 | 2.2889 | lost |

The failed cases violate multiple independent gates. Values remain finite; this
is physical/setup failure, not a numerical NaN/Inf failure.

## Solver sensitivity

The diagnostic compared untouched production solver settings—athlete 12/4
iterations and bar 12/6—with a higher 24/8 setting at 25, 60, and 300 kg.

| Solver profile | 25 kg | 60 kg | 300 kg |
|---|---|---|---|
| Native production | PASS | FAIL | FAIL |
| Higher 24/8 | PASS | FAIL | FAIL |

The full rows are in
[solver-sensitivity.csv](../Measurements/GAM-13/solver-sensitivity.csv).
The diagnostic does not justify permanently increasing solver cost and does not
explain the heavy-load failure as an iteration-count defect. A smaller
timestep diagnostic was not promoted because the authoritative 100 Hz timestep
is a frozen runtime contract.

## Dynamic identification results

| Load | Identification rows | Validation rows | Classification | Model result |
|---:|---:|---:|---|---|
| 25 kg | 1600 | 1600 | `LOCAL_EQUILIBRIUM` | local fit, rejected for robust synthesis |
| 60 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |
| 140 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |
| 170 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |
| 300 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |

For the 25 kg local model:

- one-step held-out RMSE: `0.0003148458` in the mixed state units;
- 50-tick free multi-step RMSE: `5.5387906569e+41`;
- maximum absolute row-sum bound of fitted `A`: `11.9168`;
- local controllability rank: `5/5`;
- direct measured-state observability rank: `5/5`;
- stabilizability: **not established**;
- robust stability over load: **not established**.

The small one-step error is insufficient. The free multi-step validation
diverges, so the model is retained for diagnosis and rejected for controller
synthesis. The 5/5 rank result is not a stability proof.

## Why no controller was synthesized

The fixed-controller gate requires a validated dynamic model family covering
the intended standing region. That prerequisite is absent:

1. The heavy loads have no valid equilibrium data under the selected plant.
2. The only valid local 25 kg model fails long-horizon held-out prediction.
3. The existing static 3x/8x experiments were test-only substrates and cannot
   justify shipping a controller for the 1x production plant.
4. No quantitative evidence supports gain scheduling, because the scheduled
   operating regions themselves are not valid dynamic models.

Therefore:

- no fixed controller candidate was promoted;
- no gain schedule was added;
- no per-load gain or impedance table was added;
- no direct force/torque assistance was added;
- athlete capacity remains separate and uncalibrated;
- sticking and physical-failure calibration did not start.

## Biomechanics context, used correctly

The human studies below support only qualitative event framing: a successful
heavy squat can contain a sticking region, and failure is distinguished by the
absence of later recovery. They do not validate Unity magnitudes, rigid-body
parameters, actuator capacity, or control stability.

- Larsen, Kristiansen, and van den Tillaar, “New Insights About the Sticking
  Region in Back Squats,” *Frontiers in Sports and Active Living* 3 (2021),
  original research, DOI
  [10.3389/fspor.2021.691459](https://doi.org/10.3389/fspor.2021.691459).
  Twenty-five recreationally trained lifters performed 3-RM squats; the paper
  analyzes kinematic/kinetic sticking-region events.
- van den Tillaar, Andersen, and Sæterbakken, “The Existence of a Sticking
  Region in Free Weight Squats,” *Journal of Human Kinetics* 42 (2014),
  original research, DOI
  [10.2478/hukin-2014-0061](https://doi.org/10.2478/hukin-2014-0061).
  The sticking region was not present in every participant, so its absence is
  not itself a model failure.
- Larsen, Kristiansen, and van den Tillaar, “Effects of Barbell Load on
  Kinematics, Kinetics, and Myoelectric Activity in Back Squats,” *Sports
  Biomechanics* (2022), original research, DOI
  [10.1080/14763141.2022.2085164](https://doi.org/10.1080/14763141.2022.2085164).
  The 90/100/102% design supports qualitative load ordering and distinguishes
  successful heavy lifts from failed attempts; it is not a game calibration
  dataset.

## Control and system-identification references

These are methodological authorities for the report, not evidence about the
Unity plant:

- Forssell and Ljung, “Closed-loop Identification Revisited,” *Automatica* 35
  (1999), 1215–1241, original methodological research, DOI
  [10.1016/S0005-1098(99)00022-9](https://doi.org/10.1016/S0005-1098(99)00022-9).
  Closed-loop regulation changes the identification problem; the regulator
  cannot be ignored when interpreting data collected under feedback.
- Van Overschee and De Moor, “Closed-Loop Subspace System Identification,”
  *Proceedings of the 36th IEEE Conference on Decision and Control* (1997),
  original methodological research, DOI
  [10.1109/CDC.1997.657851](https://doi.org/10.1109/CDC.1997.657851).
  This is the basis for treating a MIMO closed-loop dataset as a dynamic
  state-space identification problem rather than a static inverse.
- Schoukens and Ljung, “Nonlinear System Identification: A User-Oriented Road
  Map,” *IEEE Control Systems* 39 (2019), 28–99, peer-reviewed methodological
  roadmap, DOI
  [10.1109/MCS.2019.2938121](https://doi.org/10.1109/MCS.2019.2938121).
  It motivates local validity regions, deliberate excitation, and separate
  validation for nonlinear systems.
- Doyle, Glover, Khargonekar, and Francis, “State-Space Solutions to Standard
  H2 and H-infinity Control Problems,” *IEEE Transactions on Automatic
  Control* 34 (1989), 831–847, primary robust-control theory, DOI
  [10.1109/9.29425](https://doi.org/10.1109/9.29425).
  Robust synthesis requires closed-loop stability/performance conditions, not
  a nonzero static determinant.

No blogs, search-result summaries, or uncited vendor claims are used as
technical evidence. The full audited bibliography is in
[GAM-13-top-tier-control-references.md](../Research/GAM-13-top-tier-control-references.md).

## Verification and delivery status

| Gate | Result |
|---|---|
| Focused GAM-13 contract tests | PASS |
| Dynamic identification PlayMode | PASS |
| Solver sensitivity diagnostic | PASS |
| Full EditMode | PASS 215/215 |
| MasterSpec | PASS: 68 files, hashes/dependencies |
| Stage-A production standing | 25 PASS; 60/140/170/300 FAIL |
| Full PlayMode | not run as final acceptance; Stage-A is blocked |
| Graphics PlayMode | not run in this wave |
| Performance | not run in this wave |
| 25 kg lifecycle rerun | not run after this wave; prior GAM-12 baseline remains accepted |
| Capacity/sticking/failure/held-out calibration | not started |
| PR/merge | not created |

## Final decision

`GAM13_COMPLETION_DECISION=BLOCKED`

The next authorized decision is architectural: qualify a fixed physical
substrate capable of establishing loaded equilibrium, or amend/re-scope the
GAM-13 standing envelope. Until that decision and evidence exist, synthesizing
or shipping a robust controller would be tuning a symptom rather than proving
the dynamics.

## Static-trim refoundation addendum — 2026-09-18

The current-HEAD Stage-A baseline was regenerated without experimental
overrides and preserved separately as
`Artifacts/Measurements/GAM-13/trim-refoundation-stage-a-baseline.csv`.
It confirms 25 kg PASS and 60/140/170/300 kg FAIL; the older
`stage-a-standing-final.csv` remains historical provenance.

The bounded static-trim experiment then evaluated the physical plant before
dynamic identification. It found a 25 kg long-hold trim but no canonical
60/140/170/300 kg long-hold trim on the production 1x plant. Diagnostic fixed
impedance factors 2x, 3x, 4x, 5x, 6x, and 8x were also tested with square-root
damping scaling. None qualified all five canonical loads; the remaining
endpoint failures were saddle/support topology failures with finite telemetry.

This addendum supersedes the report's earlier “no valid equilibrium data”
wording only in scope: a local bounded trim is now proven at 25 kg, while the
heavy plant family remains unqualified. The old dynamic model remains
diagnostic-only. Capacity, sticking, supra-max failure, controller synthesis,
held-out loads, PR, merge, and Linear closeout remain blocked pending the owner
decision recorded in `Artifacts/Receipts/GAM-13-trim-refoundation-blocker.md`.
