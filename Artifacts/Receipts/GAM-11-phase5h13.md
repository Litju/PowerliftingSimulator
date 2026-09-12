# GAM-11 Phase 5H13 — phase-aware balance plant identification and guard redesign

Start `cd3d828` · Checkpoint `checkpoint/gam11-pre-phase-aware-balance-identification`

**The posture limit guard was reading a quantity the balance correction cannot
move, and withdrawing the ankle on the strength of it.** Replacing the reading
rather than the thresholds turns both failing physical squat gates green. The
target-to-COP gain error is real, is larger in magnitude, and is deliberately
not fixed here.

## Preflight deviation

The mission specified `EXPECTED_START_HEAD=cbefb57`. Actual HEAD was `cd3d828`,
one commit ahead: the completion of the four H12 sections this mission lists as
its own §5, §7, §11 and §12. The checkpoint was therefore cut at `cd3d828`, not
at `cbefb57`, so it protects that work rather than discarding it. Those four
sections were consumed as evidence and not repeated.

## §3 — the guard cascade

`GUARD_SUPPRESSES_PROXIMAL_STRATEGY = YES`, on 4 of 4 guard-closed samples.

`saturationFraction` is computed after `ankleTargetOffset *= PostureGuardScale`,
and `HipStrategyBlend` is gated on it. So the guard removes the ankle and then
removes the evidence that the ankle needed removing.

| load | s_q | guard scale | raw ankle | guarded | raw authority | reported authority | hip blend | blend from raw |
|---|---|---|---|---|---|---|---|---|
| 0 | 0.80 | 0.166 | −17.5° | −2.9° | 1.164 | 0.193 | 0.000 | 1.000 |
| 0 | 1.00 | 0.000 | 33.5° | 0.0° | 2.236 | 0.000 | 0.000 | 1.000 |
| 25 | 0.80 | 0.129 | **79.1°** | 10.2° | **5.276** | 0.679 | 0.000 | 1.000 |
| 25 | 1.00 | 0.000 | 18.4° | 0.0° | 1.227 | 0.000 | 0.000 | 1.000 |

## §4 — nominal versus correction-induced limit risk

For the hip, `q_final == q_nominal` at s_q 0.55, 0.80 and 1.00 under **both**
loads. The balance correction contributes exactly nothing to the hip target at
depth, so the 0.925 occupancy that closes the guard at the bottom is the
accepted reference's own posture and nothing else.

`CORRECTION_REDUCES_LIMIT_MARGIN` is false at every deep sample. The only
correction-induced consumption anywhere in the sweep is at 25 kg standing,
where occupancy is 0.058 to 0.133 and nothing is at risk.

What does vary with depth is actual minus nominal occupancy — hip 4.5–8.3°,
abdomen 16.2° at 25 kg s_q 0.80. That is posture the reference did not ask for,
and it is what the new guard reads.

## §§5–7, §§11–13 — carried from H12

Completed in `cd3d828` and used as given:

- Reference system COM moves 60.7 mm posteriorly unloaded and 43.3 mm at 25 kg
  from standing to the bottom. The production standing-fixed reference is stale
  by that amount at depth.
- Target-to-COP gain 0.26530 standing and 0.38516 at s_q 0.55 (R² 0.9998)
  against a production constant of 0.20446. Deeper phases are
  `GAIN_NOT_IDENTIFIABLE_QUASISTATICALLY`.
- Centroidal residuals: unloaded every model within 8 mm. At the 25 kg reversal
  LIPM is wrong by 48.5 mm against 11.3 mm for the full centroidal form, with
  22.4 mm from angular momentum and 16.6 mm from vertical acceleration.
- `LEGACY_CAPTURE_POINT_VALIDITY = PARTIAL`. The observer's capture point is
  pure LIPM, so at the loaded reversal a front-edge crossing is not exact
  physical uncapturability. It stays as a diagnostic.
- Bar lever arm at 25 kg: −0.0759 m standing (rearward, −18.6 N·m) through
  +0.1582 m at s_q 0.80 (+38.8 N·m). The s_q 1.00 value of 0.4495 m is a
  diverged state, not an equilibrium property, and is not used as one.

## §§8–10 — four-way decomposition

One recorded descent per load, replayed offline through the controller's own
equations. Peak raw ankle request, degrees, before the 15° bound:

| load | M00 | M10 | M01 | M11 |
|---|---|---|---|---|
| 0 | 143.32 | 167.79 | 76.08 | 89.07 |
| 25 | 180.59 | 196.04 | 95.87 | 104.07 |

`COM_REFERENCE_ERROR_CONTRIBUTION = +17.1 pct unloaded, +8.6 pct at 25 kg.`
The phase COM reference **raises** the request and raises over-bound samples
from 20 to 81 and from 39 to 69. It corrects where the loop aims, not how hard
it pulls.

`PLANT_GAIN_ERROR_CONTRIBUTION = −46.9 pct at both loads.` The gain carries the
magnitude.

Under all four models the deep request still exceeds the bound severalfold, so
neither correction makes the bottom feasible on its own.

## §§14–16 — guard redesign

Replacement semantics, no new thresholds:

```
consumed   = max(commandedOccupancy, actualOccupancy) − nominalOccupancy
limitScale = 1 − clamp01(consumed / (1 − nominalOccupancy))
```

The denominator is the reference's own remaining headroom at that phase, so the
scale is supplied by the reference rather than chosen.

Harness validated against committed H12 numbers before any claim: the G0 rows
reproduce F1 and F3 to the digit (static 0 kg s_q 1.00 comAP −0.2092
COM_PAST_REAR; 25 kg s_q 0.80 comAP 0.8219; dynamic 25 kg G1 finalPelvisY
0.0900 with contact lost).

| candidate | static held | min guard scale | ankle fully withdrawn | dynamic |
|---|---|---|---|---|
| G0 | 2/6 | 0.0000 | 2 | both loads diverge |
| G1 | 4/6 | 0.0000 | 1 | 0 kg clean, 25 kg collapses to pelvis 0.0900 |
| **G2** | **6/6** | **0.3966** | **0** | **both loads clean, no crossing, no contact loss** |

G2 removes G0's forward divergence without buying G1's rearward collapse.

## §17 — production decision

`CASE A`, with the gain deferred. Guard semantics alone are wrong; the COM
reference is measurably stale but is not what drives the over-command, and
correcting it alone makes the request worse.

Shipped: G2 limit semantics, raw-demand proximal trigger.
Not shipped: phase COM reference, phase-local gain mapping.

The gain is the larger magnitude term and is the obvious next move, but its
standing sample disagrees with the production constant by 30 percent because
the two were identified under different protocols, and it is unidentifiable
below s_q 0.55. Changing it would move a standing setpoint a passing gate rests
on, to buy an improvement the guard change already delivers.

## §§20–23 — validation

- `PHYSICAL_SQUAT_GATE_0KG = PASS` (was failing at `cd3d828`)
- `PHYSICAL_SQUAT_GATE_25KG = PASS` (was failing at `cd3d828`)
- MasterSpec `STATUS=PASS`, 68 files.
- EditMode **67/67**.
- Full PlayMode with a graphics device **143/145**, 2 failures, both
  pre-existing: G3 at 105 kg (out of scope, not run or tuned against on its
  own) and E3 preload-only equilibrium. Baseline was 136/140 with 4.
- A first PlayMode run under `-nographics` showed 5 extra failures. All five
  are graphics-device failures ("requires a graphics device",
  "RenderTexture.Create failed"), none control-related, and all clear with a
  display.

Architecture gates: no AddForce, no AddTorque, no velocity write, no runtime
transform write, no kinematic pelvis, no foot pins, no bar parenting, no
hand-bar constraints. Ankle ROM, actuator profile, capacity, solver profile,
collision policy, GAM-10 and every balance bound unchanged. 105 kg not tested
as a target.

## §21 — visual

`VISUAL_AGENT_VERDICT = IMPROVED_NOT_ACCEPTED.`

Against the same capture at `cd3d828`, the 25 kg bottom carries the trunk more
upright and holds the bar closer to the shelf. It is still not a squat to
accept: the trunk folds well past the reference's 36 deg and the bar still
migrates forward off the shelf. Owner visual review remains required.

## Next first cause

The residual forward trunk fold at depth, with the phase-local target-to-COP
gain as the leading candidate and the H12 static evidence — the support polygon
collapsing from 290 mm at s_q 0.55 to 38 mm at 0.80 — as the second.
