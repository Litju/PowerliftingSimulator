# GAM-11 Phase 5H20 — Event-based biomechanical validation and control decision

MISSION=GAM11_PHASE5H20_EVENT_BASED_BIOMECHANICAL_VALIDATION_AND_CONTROL_DECISION
ISSUE=GAM-11
START_HEAD=84fc03bdfeb97231eefc93278400eafed6839d1b
FINAL_HEAD=resolve with `git rev-parse HEAD` after the receipt commit
CHECKPOINT=`checkpoint/gam11-pre-h20-event-validation` = `84fc03bdfeb97231eefc93278400eafed6839d1b`
WORKTREE_STATUS=clean before H20; final status verified after explicit staging/commit
SKILLS_USED=`powerlifting-foundation`, `powerlifting-physical-athlete`, `powerlifting-equipment`, `unity-physics`, `unity-csharp-scripting`, `physics-tuning`, `biomechanics-modeler`,`numerical-vvuq-scientist`, `scientific-critical-thinking`, `research`; Linear connector used for issue read/post

## Authority and frozen-scope result

`H19_AUTHORITY_CONFIRMED=YES`. The H19 raw traces, H19 tests, H19 receipt,
H17/H18 receipts, MasterSpec, current balance/gravity-equilibrium code, GAM-10
reference seam, and GAM-11 physical qualification gates were read before the
H20 run. H19 findings retained: persistent bottom lower-limb residual,
rate-dependent ascent residual, closed-chain coupling, and stable 0/25 kg
physical paths. H19's unsupported inverse-dynamics/current-torque and
"authentic sticking" claims are not used as H20 evidence.

`PRODUCTION_CONTROL_CHANGED=NO`
`GAM10_CHANGED=NO`
`LOWER_LIMB_TIGHTENING_REQUIRED=NO`

H20 adds one Unity-independent offline diagnostic detector, its deterministic
EditMode tests, the fresh-trial evidence harness, and evidence/research files.
No production target, gain, capacity, phase rate, force, torque, velocity, or
transform writer was changed.

## Event detection

`WORLD_VERTICAL=+Y_UP`; the primary signal is the authoritative physical bar
Rigidbody's `linearVelocity.y` in m/s, not a rendered transform derivative.
Raw `bar_y_m` and `bar_vy_mps` are preserved in every 100 Hz H20 trace. `v0` is
the lowest direct bar position in the one-repetition trace. Candidate extrema
are found from an offline diagnostic signal only; the detector accepts a full
sequence only when the ordered `v0 → vmax1 → dmax1 → vmin → vmax2` pattern is
resolved above the measured noise floor.

`EVENT_DETECTION_METHOD=` local maxima/minima of a deterministic filtered bar
velocity; `dmax1` is the most negative central-difference acceleration between
the selected `vmax1` and `vmin`; `vmax1 → vmin` is the sticking interval only
when the complete ordered sequence is resolved. Detection never reads `s_q`.

`FILTERING_METHOD=` second-order Butterworth low-pass, 10 Hz cutoff at 100 Hz,
forward pass followed by reverse pass for zero phase, nine-sample reflected
edge padding. Filtering is diagnostic/offline and does not alter simulation.
Raw acceleration is also retained as the central difference of raw velocity;
filtered acceleration is used only for `dmax1` identification.

`NOISE_FLOOR_METHOD=` final 60 ticks of a 120-tick no-intent SETUP hold at the
same production configuration, using robust MAD scale (`1.4826 × MAD`) on
held velocity and its first difference. The 25 kg low-motion window measured:

| quantity | robust sigma | four-sigma resolution threshold |
|---|---:|---:|
| velocity | `0.004848627 m/s` | `0.019394508 m/s` |
| acceleration | `0.080142610 m/s²` | `0.320570439 m/s²` |

The held-window median absolute velocity was `0.016943600 m/s`; this is a
conservative low-motion/solver-and-plant floor, not a claim of perfect static
equilibrium.

`EVENT_DETECTOR_TEST_RESULT=PASS 6/6 EditMode`

The synthetic contracts cover: clean canonical sequence; monotonic ascent;
noise-only trace; small numerical wiggles; deterministic prominence selection;
and repeated identical input. The result is bitwise/numerically deterministic
for identical input and independent of `s_q`.

## Fresh physical trials

Each load used three independent fresh scene resets, the production scene,
100 Hz local physics, and the existing physical athlete/bar coupling. Each
trace contains 697 post-start samples through physical lockout at
`6.969999844 s`.

`0KG_REPEATABILITY=PASS 3/3; bodyweight-only path; normalized traces identical`

`25KG_REPEATABILITY=PASS 3/3; bar Rigidbody present; normalized traces identical;
candidate indices identical: v0=373, vmax1=403, dmax1=411, vmin=423, vmax2=471`

`0KG_EVENT_SEQUENCE=NO_BAR_SIGNAL`. The current qualified 0 kg mode deactivates
the physical bar GameObject, so a bar-velocity event sequence is not
observable. This is a configuration limitation, not a substituted COM/body
signal.

`25KG_EVENT_SEQUENCE=NO_RESOLVABLE_STICKING_REGION`. The raw/filtered trace has
observable candidate extrema, but the complete sequence is rejected by the
declared noise criterion in every repeat and every sensitivity variant.

`0KG_STICKING_REGION=NOT_APPLICABLE_NO_BAR_SIGNAL`
`25KG_STICKING_REGION=NO_RESOLVABLE_STICKING_REGION`

### 25 kg candidate event measurements

These are explicitly **candidates**, not accepted biomechanical events. Joint
angles in this table are the H20 explicit geometric included-angle proxies;
the raw traces also carry calibrated joint-space twists, angular velocities,
COM, COP/support, contact, slip, saddle, and reference-context fields.

| candidate | time from start (s) | time from v0 (s) | displacement from v0 (m) | raw v (m/s) | filtered a (m/s²) | knee (°) | hip (°) | ankle (°) | world trunk (°) | COM z (m) | COP z (m) | support AP (m) | contacts | saddle (m) |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---:|---:|
| `v0` | 3.74 | 0.00 | 0.0000 | -0.0020 | +0.8192 | 46.00 | 53.68 | 49.70 | 35.67 | -0.0460 | -0.0506 | [-0.1547, 0.1353] | 8 | 0.0045 |
| `vmax1` | 4.04 | 0.30 | 0.0365 | +0.1781 | +0.0051 | 51.06 | 57.73 | 49.84 | 36.23 | -0.0448 | -0.0403 | [-0.1547, 0.1353] | 8 | 0.0044 |
| `dmax1` | 4.12 | 0.38 | 0.0506 | +0.1741 | -0.0821 | 53.05 | 59.27 | 50.05 | 36.37 | -0.0450 | -0.0412 | [-0.1547, 0.1353] | 8 | 0.0044 |
| `vmin` | 4.24 | 0.50 | 0.0709 | +0.1682 | +0.0077 | 55.88 | 61.61 | 50.35 | 36.41 | -0.0453 | -0.0412 | [-0.1547, 0.1353] | 8 | 0.0043 |
| `vmax2` | 4.72 | 0.98 | 0.1708 | +0.2422 | +0.0019 | 69.85 | 73.98 | 52.38 | 35.29 | -0.0468 | -0.0441 | [-0.1547, 0.1353] | 8 | 0.0045 |

The candidate `vmax1 → vmin` drop is `0.009849027 m/s`, `50.8%` of the
four-sigma velocity threshold. Candidate `dmax1` magnitude is
`0.082111360 m/s²`, `25.6%` of the four-sigma acceleration threshold. These
are the reasons H20 reports no resolvable sticking region rather than selecting
the visually convenient extrema.

## H19 knee-error peak

`KNEE_ERROR_PEAK_EVENT_LOCATION=`

- 0 kg: no bar signal and no physical bar event coordinate; peak is
  `17.7771°` at `t=5.41 s`, `s_q=0.469`.
- 25 kg: no accepted event sequence. The `20.6927°` peak is at
  `t=5.45 s`, `s_q=0.457`, `1.71 s` after the direct `v0` candidate and
  `0.2936 m` above it. It occurs **after the unaccepted `vmax2` candidate**
  at `t=4.72 s`, not inside an accepted sticking region.

At the 25 kg peak: calibrated joint-space angles are knee `85.20°`, hip
`-77.25°`, ankle `-26.80°`; included proxies are knee `88.28°`, hip `90.75°`,
ankle `56.68°`; world trunk pitch `31.82°`; bar velocity `+0.2984 m/s`; raw
bar acceleration `+0.5016 m/s²`; COM z `-0.0499 m`; COP z `-0.0587 m`; COP
support fraction `0.331`; support AP `[-0.1547, 0.1353] m`; eight support
contacts; saddle separation `0.0050 m`; accumulated slip `0.00072 m` left and
`0.00116 m` right. The corresponding 0 kg peak has no bar values, eight
support contacts, COM z `-0.0249 m`, COP z `-0.0146 m`, and the same coherent
bodyweight path.

## Literature and convention reconciliation

Full source records are in
[`Artifacts/Research/GAM-11-H20-sticking-region-literature.md`](../Research/GAM-11-H20-sticking-region-literature.md).
Primary sources used:

- Larsen, Kristiansen, and van den Tillaar (2021), 25 recreationally trained
  lifters, 3-RM high-bar back squat, 200 Hz encoder/500 Hz motion capture,
  DOI [10.3389/fspor.2021.691459](https://doi.org/10.3389/fspor.2021.691459).
- van den Tillaar, Knutli, and Larsen (2020), 10 competitive powerlifters,
  matched high/low-bar 5-RM squats, DOI
  [10.3389/fspor.2020.604177](https://doi.org/10.3389/fspor.2020.604177).
- van den Tillaar, Andersen, and Sæterbakken (2014), 15 males, free-weight
  6-RM squats; 10/15 displayed a clear sticking region, DOI
  [10.2478/hukin-2014-0061](https://doi.org/10.2478/hukin-2014-0061).
- van den Tillaar (2015), 11 resistance-trained males, full free-weight 6-RM
  back squats, [stable dLib record](https://dlib.si/details/URN:NBN:SI:doc-8BD93SCR?language=eng).
- Larsen et al. (2022), 12 healthy males, 90/100/102% 1-RM back squats, DOI
  [10.1080/14763141.2022.2085164](https://doi.org/10.1080/14763141.2022.2085164).

`ANGLE_CONVENTION_RECONCILIATION=` H20 records calibrated `J_i` X-twist
separately from explicit geometric included proxies. Simulation trunk pitch is
from world +Y vertical toward +Z anterior. Literature studies use
marker/segment/Cardan conventions and do not agree numerically on the sign and
direction of their knee/hip angle labels. H20 therefore compares timing,
displacement, velocity, smooth bilateral coordination, trunk trend, and
support qualitatively; it does not equate controller coordinates, solver
diagnostics, or included proxies with human inverse-dynamics angles.

`EMPIRICAL_COMPARISON=` The candidate trajectory has human-compatible qualitative
coordination: slight trunk-pitch increase through candidate `dmax1`/`vmin`,
then reduction by candidate `vmax2`; smooth knee/hip extension; nearly stable
ankle proxy; and planted bilateral support. Candidate timing/displacement is
shorter/shallower than several 3-RM/6-RM event means, but those candidates are
below the game's defensible noise threshold and are not treated as validated
human event matches. The 2014 result that some human repetitions lack a clear
sticking region supports interpreting H20's no-region result without declaring
controller pathology.

## Visual result

`VISUAL_RESULT=PASS_WITH_LIMITATIONS`. The graphics H20 run produced and the
agent inspected 22 deterministic 1280×720 frames: 0 kg bodyweight bottom,
knee-error peak, lockout; 25 kg candidate `v0`, `vmax1`, `dmax1`, `vmin`,
`vmax2`, knee-error peak, and lockout, each side and oblique. Full feet remain
visible after diagnostic framing correction. The inspected frames show flat
support, stable seated bar coupling at 25 kg, no gross bar migration, no mesh
or collider separation, no obvious trunk collapse, and coherent lockout. Event
frames are labeled candidates because no full sticking sequence resolved.

## Decision

`CONTROL_DECISION=ACCEPT_FINITE_IMPEDANCE`

The 0/25 kg physical paths are repeatable, the 25 kg bar signal is coherent but
does not contain a noise-resolvable sticking region, joint/trunk coordination
is qualitatively plausible, eight-foot-contact support and saddle coupling are
stable, and the H19 knee residual creates no product-visible defect at its
measured peak. The residual is physical deviation in a finite-impedance plant,
not evidence by magnitude alone for a controller defect. Do not change the
controller and do not tune a sticking region into existence.

`CLAIM_CEILING=mechanically plausible finite-impedance game squat model in the
qualified 0/25 kg production domain; not biologically authentic and not
validated human biomechanics, muscle behavior, true joint moments, GRF, or
force-plate COP.`

## Qualification

`MASTER_SPEC=PASS` — 68 files, hashes and dependencies verified.
`EDITMODE=PASS` — full suite 73/73; H20 detector contract 6/6.
`PLAYMODE_H20=PASS` — H20 fresh-trial/graphics test 1/1; headless replay also 1/1.
`GAM11_TARGETED=20/21 PASS` — all 0/25 physical-control, balance, ground,
reconciliation and standing gates passed; the single failure is the known
105 kg G3 physical gate recorded by H17 as an honest out-of-scope failure.
`PLAYMODE_FULL_GRAPHICS=175/177 PASS` — the same known 105 kg G3 failure plus
the existing exploratory `PhysicalStandingEquilibriumTests.E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`
failure. Neither failure is an H20 0/25 regression.
`PERFORMANCE_IF_APPLICABLE=NOT_APPLICABLE` — the detector is offline and the
H20 collector is test-only; no production fixed-step hot path was added.

## Open limitations and next action

`OPEN_LIMITATIONS=` 0 kg has no physical bar signal in the current qualified
configuration; 25 kg has no noise-resolvable sticking region; event-angle
comparison remains convention-limited; contact COP/normal force remains an
engine impulse estimate; results are same-machine Unity/PhysX evidence, not a
cross-platform determinism claim; 105 kg remains outside H20/GAM-13 scope.

`NEXT_ACTION=` Keep GAM-11 In Progress. Request owner review/acceptance of the
H20 finite-impedance baseline and its explicit no-sticking/no-bar limitations;
resolve the separate 105 kg acceptance gate before closure. Do not implement a
control correction in H20 and do not start GAM-12 automatically.
