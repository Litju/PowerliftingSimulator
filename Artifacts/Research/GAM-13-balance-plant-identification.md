# GAM-13 balance-plant identification

Date: 2026-09-17
Claim class: `GAME_ENGINE_CONTROL_PLANT_IDENTIFICATION`
Plant: fixed GAM-13 physical scene, 100 Hz, target-space perturbations, 8x
diagnostic impedance profile applied only by the identification test

## Method

Fifteen zero-input fresh-scene runs (three at each 25/60/140/170/300 kg)
established the repeatability/noise floor. Forty-two signed fresh-scene
perturbation runs measured ankle, hip, and coordinated abdomen/thorax target
steps. The stable 25 kg case used 0.5°, 1°, and 2° amplitudes; every load used
±1° steps. The balance controller was disabled, the existing equilibrium
feed-forward stayed enabled, and no physical force/torque or transform was
written by the harness.

Rows after support loss, capture departure, pelvis departure, trunk departure,
joint-limit proximity, or saddle departure remain in the raw CSV but are
excluded from response summaries. This is a local plant identification, not a
fit to the collapsed regime.

## OBSERVED_PLANT

The plant is a delayed, coupled, load-dependent multi-input system. A local
static/low-frequency matrix is useful for direction and scale, but it is not a
constant inverse and does not support a single global state-space claim. The
common valid window is 21 ticks at 25/60/140/170 kg and 6 ticks at 300 kg; the
300 kg window is too short for a strong dynamic claim.

The signed local gain matrices below use the symmetric ±1° estimate. COP and
COM are metres per input radian; trunk is radians per input radian.

| Load | COP: ankle / hip / trunk | COM: ankle / hip / trunk | trunk: ankle / hip / trunk |
|---:|---|---|---|
| 25 kg | 0.868 / 0.339 / -0.473 | -0.124 / 0.013 / -0.029 | 0.023 / 0.811 / -1.748 |
| 60 kg | 0.690 / 0.307 / -0.445 | -0.093 / 0.018 / -0.037 | -0.044 / 0.810 / -1.795 |
| 140 kg | 0.431 / 0.225 / -0.344 | -0.065 / 0.020 / -0.046 | -0.141 / 0.878 / -1.918 |
| 170 kg | 0.359 / 0.203 / -0.313 | -0.059 / 0.021 / -0.047 | -0.098 / 0.905 / -1.978 |
| 300 kg | 0.251 / 0.076 / -0.062 | -0.010 / 0.012 / -0.020 | -0.056 / 0.479 / -1.039 |

The 300 kg matrix is directionally informative only: its six-tick common
window is too short for a strong dynamic claim.

## EXISTING_CONTROLLER_ASSUMPTION

The production loop assumes that COM position/velocity can be converted to a
desired COP, then inverted through one scalar ankle target-to-COP gain
(`0.20446 m/rad`), with a heuristic hip/trunk blend when the ankle approaches
its bound.

## ASSUMPTION_VIOLATIONS

- The ankle gain is not load invariant: the identified local gain falls by
  about 71% from 25 to 300 kg.
- Hip and trunk target channels both move COP and COM, so trunk and balance are
  not separable in this plant.
- The trunk channel produces a substantial COP response in the opposite
  direction to the ankle channel at 25/60 kg.
- The trunk response magnitude is load dependent and the 300 kg channel is
  nearly unobservable; its measured local sign is not strong enough to use as
  a standalone control direction.
- Detectable response delays range from 0–1 ticks for COP, 2–6 ticks for COM,
  and 0–23 ticks for trunk posture, depending on channel, load, and valid
  window.
- The valid operating window collapses with load, so a controller cannot be
  validated by post-collapse samples or by a single fitted transient.

## ANKLE_RESPONSE

Positive logical ankle target correction moves COP anteriorly at every load
with a positive local gain. The gain is approximately 0.868, 0.690, 0.431,
0.359, and 0.251 m/rad at 25, 60, 140, 170, and 300 kg. The same correction
also shifts COM posteriorly and changes trunk pitch; it is therefore not a
pure COP actuator. At 25 kg the gain is approximately linear across 0.5°,
1°, and 2° (within the short pre-departure window), but its transient trend
still changes over time.

## HIP_RESPONSE

Positive logical hip correction moves COP anteriorly, with a smaller gain than
the ankle at every load. It changes trunk posture strongly: the 25/60/140/170 kg
local trunk response is approximately +0.81/+0.81/+0.88/+0.91 rad/rad. The
300 kg window weakens to approximately +0.48 rad/rad, so the proximal channel
loses useful authority as the plant departs.

## TRUNK_RESPONSE

The coordinated abdomen/thorax target step moves COP posteriorly at 25/60/140/170
kg and changes trunk posture strongly (approximately -1.75/-1.80/-1.92/-1.98
rad/rad). Its COP gain weakens toward zero with load. At 300 kg its local COP
gain is effectively zero and the window is too short to claim a stable dynamic
sign beyond the initial response.

## CROSS_COUPLING

All three channels affect COP, COM, and trunk. Ankle correction can improve
COP while worsening trunk posture; trunk correction changes COP enough that a
separable trunk/balance loop is invalid. A scalar ankle inversion plus an
independent proximal heuristic therefore discards measured plant directions.

## LOAD_VARIATION

The ankle COP slope changes smoothly downward with load, but the other output
rows become state dependent as the plant approaches departure. The scaled
matrix column-norm ratio is 2.329, 2.002, 1.810, 1.856, and 2.054, while the
scaled determinant magnitude is 4.954, 3.854, 1.446, 1.284, and 0.221 at the
five loads.
These are conditioning diagnostics with explicit row scaling, not a
biomechanical Jacobian; the 300 kg value is limited by its six-tick window.

## FIXED_GAIN_FEASIBILITY

`NO` for the existing scalar COP-inversion form and its fixed gains. The
measured plant is multi-input, coupled, and load/state dependent; the old
constant inversion is contradicted directly. This does not prove that every
fixed target-space state-feedback law is impossible.

## SCHEDULING_REQUIRED

`NO` as a first redesign requirement. Use one fixed bounded state-feedback
structure and gain set first. If later evidence requires scheduling, it must be
a smooth function of directly observed physical quantities such as system mass,
COM height, support geometry, and measured local response—not a load-keyed
outcome table.

## CURRENT_CONTROLLER_FORM_ADEQUATE

`NO`. The minimal mismatch is the scalar `COM acceleration → desired COP →
ankle inverse` assumption, not a demonstrated lack of actuator capacity.

## CONTROLLABILITY_OR_STABILIZABILITY_EVIDENCE

The local target-to-output matrices remain full-rank at 25/60/140/170 kg;
their scaled determinants are -4.954/-3.854/-1.446/-1.284. The 300 kg matrix
also has a non-zero scaled determinant (-0.221), but only six common valid
ticks support it. This establishes local multi-input directionality, not a
closed-loop stability guarantee.

Four fresh five-load Stage-A candidate runs are recorded in
`Artifacts/Measurements/GAM-13/controller-candidate-search.csv`. The bounded
vector candidates repeatedly passed 25/140/170 kg but failed 60 kg setup
stability; the 300 kg case either collapsed or exceeded the fixed saddle gate.
Therefore the evidence blocks promotion of a fixed controller candidate, but
does not justify claiming that every possible target-space controller is
impossible.

## WHY_MINIMAL_REDESIGN_FAILS

The proposed vector law is structurally valid but its first measured
implementation does not stabilize the frozen plant family. At 60 kg the
closed loop still reaches min pelvis approximately 0.096–0.133 m, trunk
departure approximately 1.26–1.70 rad, negative capture margin, and support
loss. At 300 kg the failed candidates reach min pelvis approximately
0.092–0.097 m, trunk departure approximately 1.45–1.56 rad, negative capture
margin, and support loss; the near-upright candidate still reports 0.0509 m
saddle separation against the 0.0500 m gate. No candidate is promoted as a
production fix.

## PROPOSED_MINIMAL_CONTROLLER

Replace the scalar ankle inversion and heuristic proximal blend with one
bounded, integrator-free target-space vector law. Feed it COM AP error/velocity,
capture-state error, and trunk posture/rate; distribute the resulting ankle,
hip, and trunk target offsets through fixed signed coefficients derived from the
identified directions. Clamp the vector and each channel, decay safely when
support is absent, and preserve the existing single `PoweredJointController`
writer. The proposed shipping calculation remains O(1), allocation-free,
finite, and free of direct force/torque assistance. The first implementation
candidate was not promoted: fresh Stage-A runs at the frozen 8x diagnostic
plant passed 25/140/170 kg but failed 60/300 kg, so controller qualification
and the downstream GAM-13 lifecycle remain blocked.

## WHY_THIS_IS_SMALLER_THAN_ALTERNATIVES

It removes the invalid scalar inverse and reuses the existing observation and
target composition seams. It does not add an optimizer, online identification,
torque controller, or a second physics authority.

## REJECTED_ALTERNATIVES

- Further blind stiffness, COP-gain, posture-bias, or capacity sweeps.
- Per-load gain, impedance, or outcome lookup tables.
- Treating drive saturation as the universal cause; 300 kg collapses with a
  very small reported saturation fraction.
- Direct `AddForce`/`AddTorque`, transform teleportation, or whole-body torque
  optimization.
- Fitting samples after support/posture collapse.
- Widening the existing 12° equilibrium investigation bound.

## Provenance

- Raw samples: `Artifacts/Measurements/GAM-13/balance-plant-id-impulses.csv`
- Noise floor: `Artifacts/Measurements/GAM-13/balance-plant-id-noise.csv`
- Response summaries: `Artifacts/Measurements/GAM-13/balance-plant-id-summary.csv`
- Local matrices: `Artifacts/Measurements/GAM-13/balance-plant-id-matrices.csv`
- Harness: `Assets/Tests/PlayMode/GAM13BalancePlantIdentificationTests.cs`
- Control references: `Artifacts/Research/GAM-13-top-tier-control-references.md`

No production controller value was changed to produce this report. The later
posture metric correction only makes the intentional gravity-bias target part
of the expected pose; it does not qualify a controller.
