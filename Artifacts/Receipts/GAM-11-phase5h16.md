# GAM-11 Phase 5H16 — spine equilibrium trajectory rate and standing reconciliation

Start `096e14c` · Checkpoint `checkpoint/gam11-pre-h16-spine-trajectory-reconciliation`

**Both H15 residuals were real and complementary.** The gravity bias moved the
target position without appearing in the target velocity, and the standing knot
that H15 measured and withheld was safe to ship. Together they cut the summed
world trunk error at 25 kg from 18.76° to 7.52° and the worst single phase from
12.27° to 4.89°.

## §4 — the rate mismatch, confirmed at source and measured

`EvaluateReferenceRatePerPhase` builds the commanded target angular velocity
from the nominal GAM-10 target frames alone, while H15 added a phase-dependent
gravity bias to the target position.

```
CURRENT_COMMAND_RATE_FORMULA = d(NOMINAL_GAM10)/ds · ds/dt
EXPECTED_FINAL_TARGET_RATE   = d(NOMINAL · GRAVITY_BIAS)/ds · ds/dt
```

The oracle differences the composition the adapter actually published, tick by
tick, so it is the real target rather than a reconstruction. With feedforward
off the command reproduces the nominal difference exactly — 0.0957 rad/s against
a composed-target rate of 0.0420 at 0 kg s_q 0.10 — and the worst gap is
**0.0996 rad/s at 25 kg s_q 0.40**, where the omission also **reverses the
sign**: commanded +0.0336 rad/s while the composed target travels at −0.0656.

### §8 — the rate expression is derived, not guessed

`RatePerPhase` composes as `delta = to * inverse(from)`, the parent-frame
convention, so the product rule is **ω(A·B) = ω(A) + Rot(A)·ω(B)** and the bias
rate must be rotated by the nominal before it is added. The nominal spine target
is very nearly a pure sagittal rotation, so naive addition would be close, but
the exact form costs one quaternion-vector product and does not depend on that.

`COMMAND_RATE_FINITE_DIFFERENCE_PARITY`: residual against the oracle collapses
to **~2e-4 rad/s away from the table knots**. At knots it reaches 4e-3 to 9e-3,
which is the central difference straddling the piecewise-linear slope step, not
an error in the formula — an analytic one-sided slope and a difference spanning
a corner cannot agree there.

## §5 — bias rate magnitude

Table slope steps (deg per unit phase, abdomen, 25 kg): −6.04 → −18.93 at
s_q 0.25, → −3.92 at 0.55, → 0.00 at 0.80. At the production phase rate the
omitted velocity term reaches 5.7 deg/s, against nominal rates of 1.7–8.0 deg/s
— the same order as the signal it was missing from.

## §6 — standing authorization

Three independent ten-second holds per case, production balance on, nothing
else varied.

| load | bias | abdErr | thoErr | trunkErr | hipErr | support | contacts | drift |
|---|---|---|---|---|---|---|---|---|
| 0 | zero | −1.24 | −1.19 | −0.87 | −0.13 | 0.2900 | 8 | 0.011 |
| 0 | measured | **0.00** | **0.00** | **+0.36** | −0.28 | 0.2900 | 8 | 0.000 |
| 25 | zero | −6.92 | −5.58 | −7.75 | 1.93 | 0.2900 | 8 | 0.017 |
| 25 | measured | **+0.25** | **+0.19** | **−0.47** | 0.67 | 0.2900 | 8 | 0.026 |

No backward departure, no COP pathology, no oscillation, no support change, and
the hip improves. `STANDING_BIAS_SAFE = YES`.

## §7 — factorial

Summed |world trunk error| over six sampled phases, and worst single phase:

| case | 25 kg sum | 25 kg worst | 0 kg sum | 0 kg worst |
|---|---|---|---|---|
| F00 (H15) | 31.44 | 12.27 | 14.48 | 4.84 |
| F10 (standing) | 14.55 | 5.55 | 11.82 | 3.82 |
| F01 (rate) | 29.62 | 11.99 | 14.26 | 4.50 |
| **F11 (both)** | **11.48** | **4.70** | **11.38** | **3.39** |

The two are **complementary, not redundant** — which is why the factorial was
necessary. Standing bias owns the early trajectory (25 kg standing: −9.51 →
−1.75, while rate alone leaves −9.33). Rate feedforward owns the middle
(25 kg s_q 0.55: −2.01 → −1.08, while standing alone leaves −2.03).

`SELECTED_PRODUCTION_REPAIR = CASE C, both.`

## §9 — knot behaviour

The position table is piecewise linear, so its derivative steps at the knots and
the commanded target velocity now steps with it: **4.08 deg/s at s_q 0.25, 4.17
at 0.55, 1.09 at 0.80**. Against the trunk damper that is a torque step far
below the authored ceiling, and no transient, oscillation or support change
follows it in any trace. **No smoothing was added** — that would alter the H15
calibrated path and needs its own evidence.
`INTERPOLATION_REGULARITY_DECISION_REQUIRED = NO.`

## §13 — H15 static preserved

Both repairs are inert at zero phase velocity, and the measurement confirms it:
0 kg s_q 0.80 abdomen residual −0.07 before and after; 25 kg bottom −0.43 →
−0.48. Standing static improves from −1.23 to 0.00 (0 kg) and −7.00 to +0.23
(25 kg). `H15_DEEP_STATIC_PRESERVED = YES.`

## §14 — dynamic before/after

World trunk error against the GAM-10 reference:

| s_q | 0 kg H15 → H16 | 25 kg H15 → H16 |
|---|---|---|
| 0.00 | −1.65 → **−0.33** | −9.44 → **−1.92** |
| 0.10 | −4.84 → **−3.38** | −12.27 → **−4.89** |
| 0.25 | −3.07 → **−2.22** | −7.26 → **−4.02** |
| 0.55 | +0.42 → +0.85 | −1.85 → **−1.27** |
| 0.80 | +2.25 → +2.35 | −0.02 → −0.16 |
| 1.00 | +2.25 → +2.37 | +0.19 → −0.15 |

25 kg summed error 18.76 → 7.52. 0 kg 9.64 → 8.12.

**Honest residual:** at 0 kg the deep phases get marginally *worse* (+2.25 →
+2.37 at the bottom). That ~2.3° unloaded deep residual is addressed by neither
repair and is the clearest remaining spine defect.

## §15 — hip, observation only

`HIP_PRODUCTION_BIAS = FROZEN_ZERO` and it stayed frozen. The hip is essentially
unmoved: 0 kg s_q 0.25 6.67 → 6.55, bottom −0.14 → −0.16; 25 kg s_q 0.25 7.95 →
7.12, bottom −1.05 → −1.04. The corrected spine trajectory does **not** remove
the hip droop, so H17 hip equilibrium calibration remains warranted.

## §16 — visual

`VISUAL_AGENT_VERDICT = FOLLOWS_GAM10_CONTINUOUSLY.` The spine now tracks the
accepted shape from standing through depth rather than only arriving at the
bottom. Feet flat, bar seated, no artificial upright torso, no early backward
lean, no visible jerk at the knots. Owner visual review remains required.

## §17–§18 — regression

- MasterSpec `STATUS=PASS`, 68 files.
- EditMode **67/67**.
- Full PlayMode with graphics **155/157**.
- Performance: hard budgets pass, controller P95 0.075 → **0.090 ms**, physics
  P95 0.681 ms. O(1), no allocation, no runtime differencing or inversion.

### One out-of-scope regression, reported not buried

The 105 kg qualification has failed the same assertion for four phases at a
stable 1.063 m. It now fails at **141.0 m** — a bounded miss has become a
numerical divergence. The calibration itself is correctly clamped there: blend
saturates at the 25 kg column and the largest table entry is 8.17°, so the table
cannot emit anything unbounded. What happens downstream of that was **not
investigated, because 105 kg is out of scope for this mission**, but the change
in failure character is attributable to this phase and must be resolved before
105 kg is ever brought back in scope.

Frozen and unchanged: GAM-10, hip bias and targets, ankle balance law,
target-to-COP gain, balance gains and bounds, posture guard, proximal trigger,
phase rate, joint K/D, maxForce, capacityScale, solver iterations, joint limits
and axes, collision policy, mass/COM/inertia, foot geometry, bar, saddle. No
AddForce, AddTorque, velocity write, transform write, integral controller, MPC,
WBC or runtime inverse dynamics.

## Next first cause

Two candidates, in order. First the **105 kg divergence**, which is now a
blocker for that load rather than a known ceiling. Then **hip equilibrium
droop**, unchanged by this phase at 6.5–7.1° through mid-descent, with the same
identification H15 used for the spine. The unloaded ~2.3° deep spine residual is
third and is smaller than both.
