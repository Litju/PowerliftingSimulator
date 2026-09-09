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
a proxy for balance-induced joint risk.

Confidence: **high** for the guard being causal at 0 kg (clean counterfactual,
measured guard scale reaching exactly zero, measured 68° command truncated to
0°). **Medium** for it being the whole story at 25 kg, where a second
mis-scaling is demonstrably present underneath.

## Not done, and why

Within the timebox I prioritised the static feasibility question and the guard
mechanism, which together were decisive. These remain open and are H13 inputs:

- §6 reference system-COM trajectory derived from GAM-10 — **not derived.** The
  controller still uses the standing COM relationship throughout. Whether that
  is a second error is unmeasured.
- §7 phase-local ankle-target-to-COP gain re-identification — **not run.** The
  25 kg guard-off rearward collapse is circumstantial evidence that the single
  standing gain (0.20446 m/rad) is wrong at depth, but it is not measured.
- §11 centroidal dynamics residual and §12 capture-point validity —
  **not computed.**

## Test status

- MasterSpec: `STATUS=PASS`, 68 files, hashes and dependencies pass.
- EditMode: **67/67 pass.**
- Full PlayMode: **134 total, 130 pass, 4 fail.** The four are the same
  pre-existing failures carried since H10, byte-identical to the H11 run
  (`G1`, `G2`, `G3`, `E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`). The four new
  H12 tests all pass and no new failure appears anywhere in the suite.
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
