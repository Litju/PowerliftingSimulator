# GAM-13 static-trim equilibrium manifold refoundation

Date: 2026-09-18
Issue: GAM-13
Evidence head: `dd0d747a6f6ae46ed9e64105908e2fdf25267199`
Unity: `6000.3.22f1 (1c726e1fb402)`
Claim class: `GAME_ENGINE_CONTROL_CALIBRATION`

## Decision

`GAM13_COMPLETION_DECISION=BLOCKED`

The production `GAM13_PRODUCTION_PLANT_V1` has a bounded local standing trim at
25 kg, but no canonical long-hold standing qualification at 60, 140, 170, or
300 kg. Fixed load-bearing impedance counterfactuals from 2x through 8x also
leave at least one canonical endpoint outside the standing contract. The
blocking evidence is support/saddle topology under loaded standing, not a
validated dynamic-control failure and not an intrinsic athlete-capacity
measurement.

This does not prove that every possible future saddle/contact substrate is
impossible. It does establish that the current plant family cannot be used for
controller synthesis or capacity calibration without an owner-authorized
contact/plant decision.

## Method

The trim variables are symmetric equilibrium target-space biases, in degrees:

```text
[ankle, knee, hip, abdomen, thorax]
```

Each channel is bounded to `[-12, +12]` degrees. No force, torque, impulse,
velocity, or transform is written by the solver. Each candidate resets the
physical load, retains gravity, physical drives, contacts, saddle, and finite
capacity, holds the canonical standing reference, and evaluates a physical
window with dynamic balance feedback disabled. The residual uses COM AP/ML
acceleration and velocity, trunk angular velocity/acceleration, pelvis and
joint velocity, posture error, and support/capture loss.

The solver is `GAM13_STATIC_TRIM_SOLVER_V1`: finite-difference Jacobian,
damped Householder QR least-squares, deterministic damping retries, maximum
per-iteration step 3 degrees, residual tolerance 0.25 RMS, and no random
search. The continuation grid was fixed before evaluation:

The actual production solver profile during these runs was athlete `28/1`
position/velocity iterations and barbell `12/6`. The earlier baseline artifact
metadata that said athlete `12/4` was corrected after source audit. The older
solver-sensitivity experiment labelled `24/8` as “higher” is not a clean
higher-iteration comparison against the current `28/1` production profile;
its conclusion is not used here.

```text
25, 35, 45, 55, 60, 70, 85, 105, 125, 140, 155, 170, 200, 230, 260, 300 kg
```

Accepted local candidates were then re-evaluated with production balance
feedback enabled for 100 warm-up ticks plus 500 measured ticks. The hold gate
requires finite telemetry, retained support, capture interior, upright posture,
no joint-limit dependence, saddle separation below the 0.05 m standing gate,
and residual RMS no greater than 0.25.

## Current production plant

The reconciled fresh-scene Stage-A baseline is in
[`trim-refoundation-stage-a-baseline.csv`](../Measurements/GAM-13/trim-refoundation-stage-a-baseline.csv).
It records the exact commit, Unity revision, 100 Hz timestep, solver settings,
plant/preload/capacity versions, and the measured standing gates. It confirms
25 kg PASS and 60/140/170/300 kg FAIL.

The final production-plant continuation and canonical solutions are:

- [`trim-continuation-search.csv`](../Measurements/GAM-13/trim-continuation-search.csv)
- [`trim-canonical-solutions.csv`](../Measurements/GAM-13/trim-canonical-solutions.csv)
- [`trim-constraint-classification.csv`](../Measurements/GAM-13/trim-constraint-classification.csv)
- [`trim-hold-validation.csv`](../Measurements/GAM-13/trim-hold-validation.csv)

At 1x, the first continuation step already fails the declared residual gate
at 35 kg. The continuation reaches the ±12 degree boundary by 140–170 kg and
enters saddle/support failure classifications at heavier steps. Canonical
long-hold results are:

| Load | Trim hold | Primary measured classification |
|---:|---|---|
| 25 kg | PASS | none |
| 60 kg | FAIL | saddle/contact plus support loss |
| 140 kg | FAIL | support-wrench infeasible; bias bound reached |
| 170 kg | FAIL | support-wrench infeasible; bias bound reached |
| 300 kg | FAIL | saddle/contact plus support loss |

## Fixed-impedance counterfactuals

The diagnostic factors scale load-bearing spring by `λ` and damping by
`sqrt(λ)`; no factor is production code. Results are preserved separately:

| Fixed factor | 60 kg | 140 kg | 170 kg | 300 kg |
|---:|---|---|---|---|
| 2x | support/contact fail | limit/saturation evidence | limit/saturation evidence | saddle/contact fail |
| 3x | support/capture fail | PASS | PASS | saddle/contact fail |
| 4x | saddle/contact fail | PASS | PASS | saddle/contact fail |
| 5x | saddle/contact fail | PASS | PASS | saddle/contact fail |
| 6x | saddle/contact fail | PASS | support fail | saddle/contact fail |
| 8x | support/capture fail | PASS | PASS | saddle/contact fail |

Evidence files are `trim-hold-validation-impedance-{2,3,4,5,6,8}x.csv` and
the corresponding continuation, canonical-solution, and constraint CSVs in
`Artifacts/Measurements/GAM-13/`. The 8x run is retained as a diagnostic
ceiling only; it is not proposed for production.

The repeated 60/300 endpoint failure across the fixed-impedance sweep, with
finite values but support loss or saddle separation beyond the standing gate,
is the present quantitative contact-topology blocker. It is not evidence to
select a per-load impedance, gain, or outcome branch.

## Frozen boundaries preserved

No P1, P2, P3, or P4 semantics changed. No direct force/torque assist, velocity
overwrite, transform teleport, load-threshold outcome, per-load gain table, or
per-load impedance table was added. The solver is offline/test-only and has no
runtime callsite. Athlete capacity, sticking, supra-max failure, dynamic model
identification, and controller synthesis were not started after the trim gate.

## Required owner decision

Authorize one of the following before GAM-13 continues:

1. redesign and re-qualify the fixed bar-to-upper-back saddle/contact
   substrate, with a new static-trim run from scratch; or
2. formally amend/re-scope the canonical standing envelope.

Do not widen the 12-degree bound, promote 8x impedance, or synthesize a
controller around transient/collapsed samples.
