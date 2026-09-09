# GAM-11 Phase 5H10 — dynamic squat system identification

Start `2e23cdc` · Branch `work/gam-11-squat-physical-control`

**The first physical cause of dynamic squat failure is the athlete's own
collision proxies.** The thigh capsule rests on the abdomen box from 53° of hip
flexion and mechanically blocks the hip at 80° against a reference that commands
111°. Nothing was tuned to find this and nothing is tuned by this phase.

## What was measured

`SquatDynamicSystemIdentificationTests` samples nineteen candidate causes on one
tick grid — coupling, support, actuator, tracking, numerics — and reports which
crosses its threshold first. Three fixtures:

- `S1_DYNAMIC_SQUAT_FIRST_CAUSE_LOAD_SWEEP` — 0 / 25 / 105 kg, ordered event
  timelines.
- `S2_HIP_FLEXION_BLOCKER_DISCRIMINATION` — reads limits, drives and contacts
  off the running plant.
- `S3_THIGH_ABDOMEN_SUPPRESSION_COUNTERFACTUAL` — the ablation.

## The load sweep exonerates the bar

Zero kilograms removes the barbell body and the saddle entirely. The athlete
still fails, and it fails **the same way and at the same tick** as it does under
25 kg.

| | 0 kg | 25 kg | 105 kg |
|---|---|---|---|
| first event | `right_thigh` tracking, **tick 250** | `right_thigh` tracking, **tick 250** | already broken at tick 0 |
| `LOAD_CHAIN_DRIVE_SATURATED` | never | never | never |
| `CONTROL_LAW_LEFT_REFERENCE` | never | never | never |
| `SADDLE_JOINT_DESTROYED` | n/a | never | never |
| `SADDLE_SEPARATION_IMPLAUSIBLE` | n/a | never | never |
| final pelvis y | 0.1585 | 0.1084 | 0.1300 |

A cause that is identical with and without the bar is not the bar. The saddle
survives every run; its one angular exceedance at 25 kg (tick 370) happens
120 ticks after the athlete is already falling. The load-bearing chain never
reaches its actuator ceiling at any load, and the balance law never moves a
command more than 20° off the canonical reference. **Coupling, capacity and
control law are all excluded as the first cause.**

## What actually stops the hip

At 0 kg the hip is commanded through the full reference and refuses to follow.
`GAM11-5h10-joint-decomposition-0kg.csv`:

```
tick  right_thigh  actual   target   track_err  limit_prox  demand
 140              -50.07   -57.82       7.75       0.417     0.059
 180              -67.33   -76.17       8.88       0.561     0.080
 240              -71.45   -88.65      17.51       0.595     0.134
 300              -75.52  -104.20      29.18       0.629     0.200
 340              -76.78  -111.00      34.78       0.640     0.225
```

The knee in the same chain tracks to 121° with 1–7° of error. The hip stalls.
Three things can hold a joint short of a target it is not saturated against:

| candidate | measurement | verdict |
|---|---|---|
| angular limit | as-built hip `[-45, +120]`, deepest actual 80.0°, proximity plateaus at 0.64 | **excluded** — 40° of range left |
| actuator capacity | demand ≤ 0.24 of a 2052 N·m ceiling for the whole stall | **excluded** |
| collision | `left_thigh ↔ abdomen` and `right_thigh ↔ abdomen`, sustained, 8164 and 7257 N·s total normal impulse | **confirmed** |

Those two pairs are the *only* athlete-to-athlete contacts in the entire
descent. Neither is suppressed: the thigh's parent is the pelvis, so the
adjacency rule in `PhysicalAthleteSelfCollisionPolicy` does not reach the
abdomen, and no qualified exception covers it.

## The causal chain, on one tick grid

```
tick 146   thigh proxy first touches abdomen proxy    hip flexion  53.4°
tick 167   the contact becomes load-bearing           hip flexion  64.9°
tick 186   hip tracking error passes 10°              hip flexion  68.0°
tick 250   hip tracking error passes 20°  <- S1 first event
tick 323   capture point leaves the support polygon, rearward
tick 342   centre of mass leaves the support polygon, rearward
tick 394   plantar contact lost
```

Contact impulse and hip tracking error rise together, monotonically, while the
actuator sits at a fifth of its authority. Deepest hip flexion reached is 80.0°
against 111.0° commanded — a **31° deficit the drive never closes**.

## The counterfactual

`Physics.IgnoreCollision` on the two thigh/abdomen pairs. Identical descent,
identical load, nothing else changed, no gain touched.

| | baseline | thigh/abdomen suppressed |
|---|---|---|
| `POSTURE_TRACKING_BREAKDOWN` | tick 250 | **never** |
| hip tracking divergence | tick 250, `right_thigh` | **never at the hip** |
| `PLANTAR_CONTACT_LOST` | tick 394 | **never** |
| `FOOT_SLIP` | tick 307 | **never** |
| `ADAPTER_DECLARED_FAILURE` | tick 380 | **never** |
| peak drive demand | 3.17 | **0.29** |
| final pelvis y | 0.1585 (on the floor) | **0.5297 (in a squat)** |
| direction of loss | rearward, com −0.1545 | forward, com +0.1358 |

Removing one collider pair converts a backward collapse to the floor into an
athlete that holds a squat. The reversal of direction is itself evidence: with
the block present the pelvis cannot fold under and the trunk is driven
rearward; without it the athlete goes forward, which is where a deep squat
actually goes.

**Classification: `ENGINEERING_DERIVED` — collision proxy geometry, not
controller, reference, capacity or coupling.**

## Two findings this phase records but does not repair

1. **Whether the contact is a proxy artefact or real anatomy is not yet
   measured.** The proxies touch at 53° of hip flexion while the accepted GAM-10
   reference requires 111°, so the coarse geometry forbids 58° of the range the
   reference needs — but the visible-mesh clearance has not been measured, and
   that is the evidence standard the forearm/thorax exception was held to in
   `45981fc`. Until it is, the repair is not qualified. It may be an abdomen box
   that is too deep, a thigh capsule that is too fat, a hip anchor in the wrong
   place, or a genuinely correct contact against a reference that is too deep.
   Those are four different repairs.

2. **105 kg never reaches a valid setup.** At the end of the 60-tick settle,
   before `StartSquat`, the abdomen is already 34.7° off target, the trunk is
   35.3° from its unloaded value, and the saddle is pinned at its 0.05 m linear
   limit carrying a 3500 N constraint force. The 105 kg dynamic result is
   downstream of a static setup failure and was not used to identify the dynamic
   first cause. This is a separate defect and needs its own phase.

## The next cause in line

With the thigh/abdomen block suppressed, the unloaded athlete holds the squat
and then loses it forward: trunk pitch passes 47° at tick 308, the ankle reaches
0.98 of its dorsiflexion limit at tick 364, and the centre of mass crosses the
**front** support edge at tick 396. That is the ankle-limited forward drift a
real deep squat has to solve, and it is a legitimately different problem from
this one.

## Evidence

- `Artifacts/Measurements/GAM-11/GAM11-5h10-first-cause-report.txt`
- `Artifacts/Measurements/GAM-11/GAM11-5h10-squat-trace-{0kg,25kg,105kg}.csv`
- `Artifacts/Measurements/GAM-11/GAM11-5h10-joint-decomposition-{0kg,25kg,105kg}.csv`
- `Artifacts/Measurements/GAM-11/GAM11-5h10-hip-blocker-report.txt`
- `Artifacts/Measurements/GAM-11/GAM11-5h10-hip-blocker-trace.csv`
- `Artifacts/Measurements/GAM-11/GAM11-5h10-hip-blocker-census.csv`
- `Artifacts/Measurements/GAM-11/GAM11-5h10-counterfactual-report.txt`

## Status

`PASS` — the phase objective was to identify the first physical cause before any
tuning, and it is identified, isolated and demonstrated by ablation. No
controller, reference, capacity or geometry value was changed.
