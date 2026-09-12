# GAM-11 Phase 5H14 — plantar support, trunk fold and bar path cause isolation

Start `e2e6ec8` · Checkpoint `checkpoint/gam11-pre-plantar-support-identification`

**There is no plantar support problem.** The feet stay flat and fully loaded
through the entire descent at both loads. The residual visual defect is about
10 degrees of spinal over-flexion, produced by finite-stiffness droop in the
abdomen and thorax drives against an uncompensated gravitational moment. The
bar follows it. Nothing was changed.

## Two H13 claims withdrawn

This phase was commissioned on three premises from the H13 receipt. Two of them
were mine and were wrong.

1. *"Questionable rearfoot/heel support"* and the support interval collapsing
   to 38 mm. The 38 mm figure came from H12 F1, measured under the old
   absolute-occupancy guard, before H13 replaced it. That guard zeroed the
   ankle channel and let the athlete pitch onto the forefoot; the collapse was
   its consequence. H13 removed the guard and the receipt repeated the pre-fix
   number without re-measuring it. Under the current controller the support
   interval is **0.2900 m — the full plantar length — at every sampled phase,
   both loads.**

2. *"The bar still migrates forward off the shelf."* The bar is seated across
   the upper back and held by a saddle whose separation never exceeds 7.7 mm
   against a 120 mm break threshold. The side camera occludes it behind the
   torso; the oblique frame shows it seated. I read a camera angle as a defect.

The third premise — excessive world trunk pitch — is real, and is the finding.

## §§4–5 — foot witness geometry and plantar distribution

Witness points derived from the runtime box collider, not placed by eye:

```
collider size (0.13, 0.10, 0.29)   center (0, 0.05, 0)   contactOffset 0.0100 m
heel witness local AP  -0.1450      toe witness local AP  +0.1450
plantar plane local Y  -0.0025      plantar AP length      0.2900 m
```

Clearance verdicts are stated against the collider's own `contactOffset`:
beyond that separation the solver cannot generate a contact there at all, so
the tolerance is the engine's scale rather than a chosen one.

## §6 — event ordering

| event | rule | 0 kg | 25 kg |
|---|---|---|---|
| `FIRST_REARFOOT_UNLOAD` | posterior half carries no normal impulse | **never** | **never** |
| `FIRST_GEOMETRIC_HEEL_LIFT` | both heel witnesses clear by > contactOffset | **never** | **never** |
| `FIRST_SUPPORT_SPAN_COLLAPSE` | span below half the measured standing span | **never** | **never** |
| `FIRST_TRUNK_DIVERGENCE` | trunk pitch exceeds reference by 15° (H12 criterion) | **never** | **never** |
| `FIRST_BAR_FORWARD_MIGRATION` | bar COM anterior of support centre | tick 1 | tick 221 (s_q 0.663) |
| `FIRST_COP_FOREFOOT_EDGE` | COP within the controller's own 30 mm margin | **never** | **never** |

Through the descent, both loads: support length 0.2900 m, heel clearance
−0.0001 to −0.0003 m, foot world pitch −0.02 to 0.00°, posterior impulse
fraction 0.44–0.68, 8 contacts, COP fraction 0.32–0.56.

## §7 — classification

`PLANTAR_SUPPORT_CLASS = FULL_FOOT_SUPPORT.` Not heel lift, not rearfoot
unloading, not a contact-reporting defect. The support representation is
faithful; there is simply nothing wrong with the support.

## §8 — trunk decomposition

At the bottom, actual against the flexion the reference commands at the same
phase:

| quantity | 0 kg | 25 kg |
|---|---|---|
| world trunk excess | +9.71° | +10.13° |
| hip excess | +0.79° | +2.55° |
| abdomen excess | +8.59° | +12.12° |
| thorax excess | +4.51° | +6.69° |
| **spine total** | **+13.10°** | **+18.81°** |

`TRUNK_EXCESS_SOURCE = SPINAL_TRUNK_FLEXION` at both loads.

The hip tracks its reference — the joint §16 forbids touching turns out not to
need touching. The abdomen reaches 18.5° where 6.2° was commanded.

The reference itself asks 35.97° of world trunk pitch at the bottom against
46.10° actual, so roughly three quarters of the visible lean is the accepted
low-bar reference and one quarter is the defect.

## Why the spine does not hold

`ModeledDemand` is the conceptual drive torque over the authored ceiling.

| load | abdomen peak | thorax peak | ceiling | binding |
|---|---|---|---|---|
| 0 kg | 0.083 | 0.044 | 1482 N·m | **NO** |
| 25 kg | 0.095 | 0.055 | 1852 N·m | **NO** |

The drives carry 12° of error while using under a tenth of their authority.
This is proportional droop holding a gravity moment, not saturation. The
gravity-preload channel that would trim it contributes **0.000° unloaded and at
most 0.158° at 25 kg** — two orders of magnitude below the droop.

`SquatEquilibriumPreload.QualifiedStanding()` sets every family to zero
deliberately, with a documented rationale: a preload is a feed-forward trim
that has to be identified with the loop closed, and the ankle value measured
open-loop was rejected for exactly that reason. So the spine bias is zero **by
design, not by defect.**

## §9 — bar path

`SADDLE_CAUSAL = NO.` Separation 3.4–7.7 mm, never detached. Bar relative to
support centre at 25 kg: −0.1571 m standing → −0.0371 at s_q 0.55 → +0.0105 at
0.80. It crosses the support centre at s_q 0.663, after the trunk excess is
already 5.79° at s_q 0.55. The bar is section 9 **case A**: following the
trunk.

## §§11–12 — causal ordering and the gain

```
CAUSAL_CHAIN =
  spinal drive droop under an uncompensated gravitational moment
    -> world trunk pitch exceeds reference by ~10 deg
      -> saddled bar carried forward with the thorax
  plantar support and the ankle balance loop are not in the chain
```

`FIRST_CAUSE_CLASSIFICATION = CASE B`, trunk/proximal control causal, refined:
it is not balance control, it is absent spinal gravity compensation.

`TARGET_TO_COP_OVERCOMMAND_CAUSAL = NO.` §12 gates the gain work on
plantarflexion overcommand preceding rearfoot unloading. There is no rearfoot
unloading to precede, and raw ankle demand is 1.5–17.4° against a 15° bound
versus H13's pre-fix 79° and 5.28 bounds. **§§13 and 14 were not run and the
gain stays frozen.**

## §18 — production decision

`PRODUCTION_FIX_APPLIED = NO.`

The cause is identified with high confidence, but the repair is not a narrow
defect fix. The spine preload is zero by documented design, so giving the spine
a phase-varying gravity feed-forward is a new control channel, and this file's
own rationale says such a trim must be identified with the loop closed. That is
H15 scope with its own identification, not a change to make on the way out of a
diagnosis phase.

## §§19–20 — gates and regression

- `PHYSICAL_SQUAT_GATE_0KG = PASS`, `PHYSICAL_SQUAT_GATE_25KG = PASS`.
- MasterSpec `STATUS=PASS`, 68 files.
- EditMode **67/67**.
- Full PlayMode **147/149**; the two failures are the pre-existing 105 kg (out
  of scope) and E3 preload-only equilibrium.
- No production file touched, so no performance run was warranted.

## §17 — visual

`VISUAL_AGENT_VERDICT = SUPPORT_AND_BAR_CORRECT, TRUNK_OVER_FLEXED.`

Both drive paths were captured, because the earlier screenshot came from the
production auto-cycle while this phase drives the phase directly. They agree:
auto-cycle at s_q 0.98 gives heel clearance −0.0001 m, support 0.2900 m, trunk
46.91° against a 35.86° reference. The oblique frame shows both heels flat and
the bar seated across the back. Owner visual review remains required.

## Next first cause

Spinal gravity compensation. H15 should identify, with the loop closed, the
abdomen and thorax feed-forward trim that removes the ~10° droop without
raising stiffness, touching GAM-10, or forcing a more upright torso.
