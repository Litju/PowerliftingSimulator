# GAM-11 Phase 5H12 — deep squat forward divergence identification

Start `c7278c0` · Checkpoint `checkpoint/gam11-pre-deep-squat-forward-identification`

**The posture limit guard withdraws the balance controller's entire ankle
authority at squat depth, because the hip's reference-commanded flexion trips a
guard calibrated for standing.** The ankle limit is a consequence, reached long
after the divergence starts. Nothing was tuned.

## The measured mechanism

`SquatPredictiveBalanceController.ComputePostureGuardScale` computes

```
limitScale = 1 - InverseLerp(0.55, 0.85, PostureLimitProximity)
```

and `ankleTargetOffset *= PostureGuardScale`. `PostureLimitProximity` is the
maximum limit proximity over `CanonicalPostureJoints` — hips, trunk, knees, and
deliberately **not** the ankles.

The accepted GAM-10 reference commands 111° of hip flexion at the bottom against
an authored 120° range. That is 93% of range **by reference design**, in a
normal deep squat. The guard reads it as a joint about to hit its stop.

Held pose, unloaded, full production composition
(`GAM11-5h12-f2-balance-authority-vs-depth.csv`):

| s_q | limit proximity | driver joint | guard scale | raw ankle command | **final ankle command** |
|---|---|---|---|---|---|
| 0.55 | 0.610 | left_thigh | 0.616 | 8.5° | 4.1° |
| 0.70 | 0.718 | left_thigh | 0.439 | 11.1° | 4.9° |
| 0.80 | 0.794 | left_thigh | 0.185 | 9.4° | **1.7°** |
| 0.90 | 0.861 | left_thigh | **0.000** | −7.8° | **0.00°** |
| 1.00 | 0.935 | left_thigh | **0.000** | 124.4° | **0.00°** |

At `s_q=0.80`, hold tick 100, the balance law asks for **68.0° of ankle
correction and receives 0.000°**. It wants the centre of pressure at +0.105 m
and achieves +0.045 m, because its only actuator has been switched off.

## The bottom is not a stable equilibrium

`F1`, fresh reset per sample, phase ramped at the production rate then held with
zero phase velocity for 250 ticks:

| s_q | verdict | behaviour |
|---|---|---|
| 0.55 | **HELD** | fully settled by tick 225: com AP −0.0295, speed 0.0005, ankle 25.8°, trunk 41.0°, all steady |
| 0.80 | diverges | starts at rest (speed 0.0057) and accelerates forward monotonically |
| 1.00 | diverges | — |

The 0.80 divergence is a textbook unstable equilibrium: it begins essentially at
rest, with 8 plantar contacts throughout and the COM inside the support polygon,
and grows exponentially. Nothing pushes it.

**The ankle is not the cause.** At the moment divergence begins the ankle is at
29.5° of a 45° dorsiflexion limit (proximity 0.656) and tracking its target
within 1.6°. It only reaches its limit around hold tick 100, roughly 80 ticks
*after* the COM starts moving. The ordering is COM → trunk → ankle, not ankle →
COM.

**The ankle is not being commanded there either.** At the bottom the reference
nominal is −27.00°, the gravity bias is 0.00°, and the balance offset is 0.00°.
The reference never asks for more than 27° of the 45° available.

## S0 and S1 are not informative, and that is itself a finding

The mission's decision matrix routes on whether nominal-only (S0) or
nominal-plus-gravity-bias (S1) can hold the bottom. Neither can — but neither
can hold **standing** either:

| load | case | s_q=0.00 |
|---|---|---|
| 0 kg | S0 | NOT_FEASIBLE, pelvis drift 0.83 m, zero contacts |
| 0 kg | S1 | NOT_FEASIBLE, pelvis drift 0.83 m, zero contacts |
| 0 kg | S2 | **HELD** |

With the dynamic balance controller off, the athlete cannot stand at any phase.
That corroborates the pre-existing `E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`
failure, which reports the same "lost plantar contact" for the preload-only case.

So S0/S1 failing at the bottom says nothing about the bottom specifically, and
reading it as CASE A would have been wrong. The discriminating comparison is
**S2 at 0.55 versus S2 at 0.80**, and that is what isolates depth.

## Counterfactual: the guard is causal, and removing it is not a repair

`F3`, identical dynamic descent, the only difference being `PostureGuardEnabled`:

| load | guard | first front crossing | first COM exit | max ankle proximity | final pelvis y |
|---|---|---|---|---|---|
| 0 kg | ON | 366 | 396 | 1.000 | 0.5296 |
| 0 kg | **OFF** | **never** | **never** | **0.702** | 0.4710 |
| 25 kg | ON | 331 | 350 | 1.000 | 0.5764 |
| 25 kg | **OFF** | never | never | 0.602 | 0.0900 |

At 0 kg, disabling only the guard removes the forward divergence completely: the
capture point never crosses the front edge, the COM never leaves support, the
ankle never approaches its limit, and the athlete reaches *more* depth.

**At 25 kg it does not fix anything — it swaps the failure.** The front-edge
columns read "never" only because the athlete falls the other way. The trace
shows com AP going −0.10 → −0.83 → −1.34 by tick 160, trunk to −85°, pelvis to
0.147, contacts lost. With the guard off the ankle balance offset saturates its
15° bound (raw 18.0°, final clamped 15.0°) and drives a **rearward** collapse
early in the descent.

So the guard is causal for the forward divergence *and* is simultaneously
masking a second defect: the ankle balance correction is mis-scaled under load.
H13 cannot simply delete the guard.

## Bar pitch moment, measured rather than assumed

The H11 receipt said load makes the divergence earlier "as a forward-tipping bar
moment should". That was inference. Measured at 25 kg about the ankle line,
+Z anterior (`GAM11-5h12-f4-bar-pitch-moment.txt`):

| s_q | bar AP | ankle AP | lever arm | moment | direction |
|---|---|---|---|---|---|
| 0.00 | −0.1633 | −0.0874 | −0.0759 | −18.6 N·m | **REARWARD** |
| 0.55 | −0.0020 | −0.0880 | +0.0860 | +21.1 N·m | FORWARD |
| 0.80 | +0.0701 | −0.0881 | +0.1582 | +38.8 N·m | FORWARD |
| 1.00 | +0.3865 | −0.0630 | +0.4495 | +110.2 N·m | FORWARD |

The direction **reverses** around s_q ≈ 0.5. Standing, the bar sits behind the
ankle line and pulls the athlete rearward, which is correct for a back squat.
The H11 statement was right at depth and wrong at standing, and was made
generally. Corrected here.

Caveat: the s_q=1.00 row is taken at a pose that is already diverging, so its
0.45 m lever arm partly reflects the divergence rather than a clean equilibrium.
The 0.55 and 0.80 rows are inside the stable or near-stable region.

## Classification

`FIRST_CAUSE_CLASSIFICATION = CASE C — CURRENT_BALANCE_CONTROLLER_CAUSAL`,
specifically the posture limit guard's use of reference-commanded joint range as
a proxy for balance-induced joint risk. It is the **initiator**: it is what
removes the controller's ability to act, and disabling it alone removes the
forward divergence unloaded.

It is not the only defect. Cases D, E and F are all confirmed by measurement and
they compound:

| case | defect | measured |
|---|---|---|
| **C** | posture limit guard zeroes ankle authority at depth | guard scale 0.000 from s_q 0.85; 68° command truncated to 0° |
| **D** | standing COM reference misplaces the setpoint | 6.1 cm forward error at the bottom, 21% of the support polygon |
| **E** | single standing target-to-COP gain | 1.88× under-estimate at s_q 0.55, R² 0.9998 |
| **F** | reduced-order model at the loaded reversal | LIPM 48.5 mm vs full centroidal 11.3 mm |

C initiates. D biases the setpoint in the divergence direction. E is why
restoring authority over-corrects rearward at 25 kg. F is why the model would
mispredict at the loaded reversal even with C and D fixed.

Confidence: **high** for C being the initiator unloaded, and high for D, E and F
existing as independent measured defects. **Medium** for the completeness of the
interaction between them at 25 kg, which has not been separated experimentally.

## The other three defects, now measured

The guard explains why the controller stops acting. It does not explain whether
the model underneath would have been right had it kept acting. Three further
measurements say it would not, and together they explain why removing the guard
alone swaps the failure direction instead of fixing it.

### CASE D — the standing COM reference misplaces the setpoint (section 6)

The controller calibrates the system centre of mass against the support polygon
once, in setup, and holds that relationship for the whole squat. Derived from the
accepted GAM-10 reference using the same segment mass fractions and the same
centre placement rule the rig builds bodies from
(`GAM11-5h12-r1-reference-com-trajectory.txt`):

| | 0 kg | 25 kg |
|---|---|---|
| reference COM AP standing | −0.03153 m | −0.05369 m |
| reference COM AP bottom | −0.09223 m | −0.09704 m |
| **delta from standing** | **−0.0607 m** | **−0.0433 m** |
| max delta over the path | 0.0648 m at s_q 0.83 | 0.0481 m at s_q 0.80 |

The accepted reference moves the centre of mass **6.1 cm posteriorly** from
standing to the bottom. The controller holds the standing value, so at depth its
setpoint sits 6.1 cm too far **forward** — 21% of the 29 cm support polygon, and
biased in exactly the direction the athlete diverges. It reports a COM error near
zero at s_q 0.80 because its target is wrong, not because it is on target.

`PHASE_COM_REFERENCE_REQUIRED = YES.`

### CASE E — the target-to-COP gain is phase dependent (section 7)

Production uses one standing-identified constant,
`MeasuredTargetToCopMPerRad = 0.20446`. Measured by settling the closed loop,
then freezing the ankle offset at its settled value plus a known delta for ten
ticks, so the perturbation is a plant response rather than a fall:

| s_q | measured gain | R² | vs production |
|---|---|---|---|
| 0.00 | 0.26530 m/rad | 0.641 | 1.30× |
| 0.55 | **0.38516 m/rad** | **0.9998** | **1.88×** |
| 0.80 | — | — | `GAIN_NOT_IDENTIFIABLE_QUASISTATICALLY` |
| 1.00 | — | — | `GAIN_NOT_IDENTIFIABLE_QUASISTATICALLY` |

`TARGET_TO_COP_PHASE_DEPENDENT = YES.` The plant is nearly twice as sensitive at
mid-squat as the constant assumes, so a correction sized with that constant
over-shoots once it is actually delivered. **That is the missing explanation for
the 25 kg guard-off rearward collapse**: restoring authority to a loop whose gain
model is half the truth drives the athlete past the target and out the back.

A first attempt at this probe replaced the controller's ankle offset outright.
That opens the loop, and this plant has no open-loop static equilibrium at any
phase — standing included, where com AP ran to +0.32 m. It measured a fall, not a
gain. The freeze-and-step design above is what the numbers come from.

### CASE F — the reduced-order model breaks down under load (sections 11, 12)

The planar COP relation was derived under this project's axes rather than copied,
and its signs are asserted by `CENTROIDAL_SIGN_CONVENTION_UNIT_TEST`:

```
p_z = c_z - h*c_ddot_z/(c_ddot_y + g) - Hdot_x/(m*(c_ddot_y + g))
```

Mean absolute COP prediction error against the engine-measured centre of pressure
(`GAM11-5h12-c1-centroidal-residuals.txt`):

| window | 0 kg LIPM | 0 kg full | 25 kg LIPM | 25 kg full |
|---|---|---|---|---|
| mid descent | 0.0030 | 0.0049 | 0.0035 | 0.0055 |
| near parallel | 0.0032 | 0.0050 | 0.0035 | 0.0047 |
| bottom | 0.0043 | 0.0024 | 0.0080 | 0.0039 |
| **reversal** | 0.0080 | 0.0061 | **0.0485** | **0.0113** |

Unloaded, every model is within 8 mm and the reduced order is adequate. At 25 kg
reversal the LIPM prediction is wrong by **48.5 mm** while the full centroidal
prediction is wrong by 11.3 mm — a factor of 4.3. The angular-momentum term
contributes 22.4 mm there and the vertical-acceleration term 16.6 mm.

`LEGACY_CAPTURE_POINT_VALIDITY`: the observer's capture point uses
`omega = sqrt(g/h)`, which is pure LIPM. Unloaded it remains a serviceable early
indicator. At the loaded reversal its assumptions are violated by roughly the
width of a boot, so a front-edge crossing there should not be read as exact
physical uncapturability.

### Section 10 — the trunk is a passenger

Measured in both frames rather than against an absolute lean threshold.

Joint frame, against the drives' own targets: abdomen tracking error reaches
9.45° and thorax 5.00° at the bottom. The spine drives are holding.

World frame, actual against the reference world trunk pitch derived in R1
(`GAM11-5h12-c3-world-trunk-tracking-error.csv`):

| tick | s_q | reference | actual | error |
|---|---|---|---|---|
| 120 | 0.36 | 22.10° | 23.75° | +1.65° |
| 240 | 0.72 | 33.56° | 43.19° | +9.63° |
| 360 | 1.00 | 35.97° | 58.19° | **+22.22°** |

`TRUNK_REFERENCE_ERROR_ONSET_TICK = 335` at a 15° threshold. The reference itself
asks for 36° of world trunk pitch at the bottom, so the 58° seen in the H11
visuals is roughly 36° of intended squat plus 22° of divergence — not, as an
absolute threshold would have suggested, a 58° defect.

**Causal ordering, unloaded:** guard scale reaches zero (~tick 300) → trunk
departs its reference (335) → capture point crosses the front edge (366) → centre
of mass crosses (396). The trunk sits downstream of the guard and upstream of the
balance loss.

A first version of this comparison put the reference trunk *joint* flexion beside
the actual *world* trunk pitch and called the difference a tracking error. Those
are different frames — the world value also carries the hip rotation — and the
column is now named `mixed_frame_difference_deg` so it cannot be read that way.
The table above is the corrected world-frame comparison.

## Test status

- MasterSpec: `STATUS=PASS`, 68 files, hashes and dependencies pass.
- EditMode: **67/67 pass.**
- Full PlayMode: **140 total, 136 pass, 4 fail.** The four are the same
  pre-existing failures carried since H10, byte-identical to the H11 run
  (`G1`, `G2`, `G3`, `E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`). All ten new
  H12 tests pass and no new failure appears anywhere in the suite.
- Physical squat gates unloaded and 25 kg: still **FAIL**, unrelaxed.

## Visual review

Guard on and guard off descents rendered at the same ticks and phases and
viewed (`Artifacts/Evidence/GAM-11/h12-forward-divergence/`). At tick 396,
s_q 0.90, the contrast is unambiguous: with the guard enabled the athlete is
folded forward with the trunk close to horizontal and the head down; with the
guard disabled, same tick and same phase, it holds an upright, recognisable
deep squat with the trunk near 45 degrees and the feet flat. The rendered
difference matches the measured one.

## Production change

One diagnostic hook was added to `SquatPhysicalAdapter`:
`HoldReferencePhaseForQualification`. It places the reference phase and reports
the matching phase velocity so a fixture can ask what the plant does at a held
pose. It writes no physical state, touches no composition layer, and gameplay
never calls it. No gain, bound, limit, capacity, reference or contact value
changed.

## Status

`PASS` — the initiator of the post-H11 forward divergence is identified,
localised to a named function, and demonstrated by counterfactual. No production
control value was tuned, as instructed. H13 implements.
