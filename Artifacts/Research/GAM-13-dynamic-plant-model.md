# GAM-13 dynamic plant model

Date: 2026-09-18
Claim class: `GAME_ENGINE_CONTROL_PLANT_IDENTIFICATION`
Plant: `GAM13_PRODUCTION_PLANT_V1` — the existing production 1x impedance
Model candidate: `GAM13_DYNAMIC_PLANT_MODEL_V1`

## Method

The Unity 6000.3.22f1 PlayMode fixture ran 20 fresh-scene trajectories: two
deterministic multisine identification trajectories and two held-out validation
trajectories at each of 25, 60, 140, 170, and 300 kg. Each trajectory used 100
warm-up ticks followed by 800 excitation ticks at 100 Hz. Excitation amplitudes
were bounded below 0.30 degrees. Inputs in the CSV are the actual applied
post-rate-limit target residuals, not merely requested values. The existing
bounded balance loop remained active; the new additive ankle residual defaults
to zero in production.

Raw evidence:

- `Artifacts/Measurements/GAM-13/dynamic-id-excitation.csv`
- `Artifacts/Measurements/GAM-13/dynamic-id-validation.csv`
- `Artifacts/Measurements/GAM-13/dynamic-plant-models.json`
- `Artifacts/Measurements/GAM-13/dynamic-plant-summary.csv`
- `Artifacts/Measurements/GAM-13/load-transition-map.csv`

The fit is a local first-order ARX/state-space estimate over
`[COM_AP, COM_AP_velocity, COP_AP, capture_AP, trunk_pitch]` with the three
actual applied target residuals as inputs. `Tools/Spec/Analyze-GAM13DynamicPlant.py`
fits identification data only and scores held-out trajectories separately.

## Results

| Load | Identification rows | Held-out rows | Classification | Model result |
|---:|---:|---:|---|---|
| 25 kg | 1600 | 1600 | `LOCAL_EQUILIBRIUM` | fit, but not robust |
| 60 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |
| 140 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |
| 170 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |
| 300 kg | 0 | 0 | `TRANSIENT_LOCAL_RESPONSE` | no valid equilibrium window |

At 25 kg the one-step held-out RMSE is `0.0003148458` in the mixed state
units, but the 50-tick free multi-step RMSE is `5.5387906569e+41`. The fitted
state transition has a maximum absolute row-sum bound of `11.9168`. The local
controllability matrix has rank `5/5` and the directly measured state output
has observability rank `5/5`; these are local model diagnostics only. They do
not establish stabilizability or robust stability. The model is rejected for
controller synthesis because its long-horizon validation diverges despite a
small one-step error.

## Interpretation

The production plant has one usable closed-loop local operating region at 25
kg. The intended loaded family has no valid equilibrium trajectory under the
same fixed plant and bounded controller at 60–300 kg, so those rows are not
silently fitted as LTI plants. The evidence therefore cannot support one
fixed-gain controller, gain scheduling, or a capacity calibration across the
Stage-A family.

## Solver sensitivity

The bounded diagnostic compared untouched production solver settings (athlete
12/4 iterations, bar 12/6) with a higher 24/8 setting at 25, 60, and 300 kg.
25 kg passed under both settings. 60 and 300 kg failed under both settings,
with the same setup-collapse classification. The result does not support
classifying the loaded failure as a simple solver-iteration defect. A smaller
diagnostic timestep was not run because the authoritative fixed timestep is a
frozen 100 Hz contract; no timestep change was promoted.

Evidence: `Artifacts/Measurements/GAM-13/solver-sensitivity.csv`.

The following distinctions remain explicit:

- local controllability rank is not dynamic stabilizability;
- a low one-step error is not multi-step predictive validity;
- transient collapse data is not equilibrium linearization;
- same-machine repeatability is not physical-model certainty.

## Decision

`GAM13_DYNAMIC_PLANT_MODEL_V1` is retained as diagnostic evidence only. No
controller candidate is promoted, no production gain is changed, and no
per-load controller or impedance table is added. GAM-13 remains blocked at the
dynamic standing-model gate until a fixed physical substrate and a valid
loaded equilibrium family exist for identification.

Claim ceiling: same-machine game-physics/control evidence. No human torque,
GRF/COP measurement, muscle activation, physiological fatigue, or
cross-platform determinism claim is made.
