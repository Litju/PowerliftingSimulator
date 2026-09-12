# GAM-11 Phase 5H — static equilibrium preload

Start `e9be0f6` · Head `01e812f` · Branch `work/gam-11-squat-physical-control`

**Answer to the primary question: NO.** The canonical GAM-10 standing pose
cannot be made a finite-drive static equilibrium by a bounded target-space
preload, because the athlete is not standing on the platform when the
experiment begins. Classification: `PHYSICAL_SUBSTRATE_EQUILIBRIUM_BLOCK`,
cause `NEUTRAL_POSE_PROBLEM` with a secondary `FOOT_CONTACT_GEOMETRY_PROBLEM`.

## The measurement that settles it

```
platform contact plane        y = 0.0000
GAM-10 reference plantar anchor y = 0.0942   <- the accepted sole position
physical foot collider bottom   y = 0.0468
spawn gap (collider to platform)    0.0468 m
collider bottom minus reference sole -0.0475 m
foot collider size (0.130, 0.100, 0.290) m, bounds z [-0.1546, 0.1357]
```

Two independent vertical registration errors that partially cancel:

1. **The athlete rig and the platform are not registered to the same ground
   plane.** The owner-accepted GAM-10 standing pose puts the sole of the foot
   at y = 0.0942, 9.4 cm above the platform surface. GAM-10 was qualified as a
   reference preview that never had to touch a physics collider, so this is
   not a defect *in* GAM-10 — it is GAM-11 placing the physical rig from the
   animator's world pose without registering the reference ground plane to the
   platform.
2. **The physical foot collider does not sit on the reference sole.** The
   0.100 m box is centred on the foot bone, so its underside is 4.75 cm below
   the reference plantar anchor rather than coincident with it.

Net effect: the athlete spawns 4.68 cm in the air and free-falls onto the
platform, landing at about 1.1 m/s.

```
tick  pelvis_y  foot_min_y  foot_vel_y  contacts
   0   1.0758      0.0468      0.0000      0
   2   1.0729      0.0438     -0.1961      0
   4   1.0660      0.0370     -0.3920      0
   6   1.0553      0.0262     -0.5878      0
  10   1.0161      ~0          ~-1.1        8
```

The foot length itself is fine: the 0.290 m collider matches the measured
support polygon exactly. The defect is vertical, not longitudinal.

## Experiment A — baseline, nominal reference only

Immutable record in `GAM11-experiment-a-baseline.csv`. Initial pelvis 1.0749 m,
free fall to first contact at tick 10, then a monotonic forward departure:
COM AP velocity 0.000 at landing, 0.156 at 0.30 s, 0.286 at 0.50 s, 0.557 at
0.80 s. Dominant actual joint deviations at 0.80 s are ankle −20.6 deg logical
(that is +20.6 deg dorsiflexion, the shank rotating forward over the foot) and
the trunk trailing at −23.5 abdomen and −18.3 thorax. The knee barely moves,
2.3 deg. So the collapse is a rotation about the ankle, not a leg buckle.

## Preload identification

Deterministic, one family at a time, seven biases each, balance completely off.
Full grid in `GAM11-preload-identification.csv`.

The decisive column is initial sag, which is **0.0553 to 0.0657 m across all 35
conditions** — essentially invariant. Preload cannot change it, because during
the free fall there is nothing to push against. Measured sensitivity of the
forward departure (COM AP velocity at 0.5 s, baseline 0.279):

| family | −6 deg | +6 deg | effect |
|---|---|---|---|
| Ankle | 0.173 | 0.390 | dominant, negative helps |
| Knee | 0.306 | 0.278 | negligible |
| Hip | 0.300 | 0.265 | weak, positive helps |
| Abdomen | 0.284 | 0.270 | weak, positive helps |
| Thorax | 0.284 | 0.275 | negligible |

Ankle carries the effect; everything else together is worth about a fifth of
it. Nine combined candidates (`GAM11-preload-combined-candidates.csv`) confirm
the extrapolation rather than beating it. The best candidate at the boundary,
ankle −12, knee +2, hip/abdomen/thorax +6, still ends 1.5 s with COM AP
velocity 0.818 m/s, capture margin −0.354 m and 35.4 deg of posture error.
Pushing hip/abdomen/thorax to +12 makes it worse, not better.

**Experiment B fails and is committed failing.** `QualifiedStanding()` returns
zero on every family, because no candidate qualified.

## Why the arithmetic and the experiment disagree, and why that matters

A static check says this should be easy. The ankle anchor sits at AP −0.088,
the COM at −0.032, so the ankle carries a 0.056 m lever, about 55 Nm, which at
the invariant 650 Nm/rad ankle spring across two ankles is roughly **2.4 deg**
of preload — comfortably inside the 6 deg soft target.

The experiment cannot confirm or refute that, because every trial begins with
a landing impact instead of at rest in contact. That is precisely why the
result is a substrate block rather than a preload result: the question is
well posed, the plant is not yet in a state where it can be asked.

## Classification

| candidate cause | verdict | evidence |
|---|---|---|
| `NEUTRAL_POSE_PROBLEM` | **primary** | reference sole at y=0.0942 against a platform at y=0.0000 |
| `FOOT_CONTACT_GEOMETRY_PROBLEM` | **secondary** | collider underside 0.0475 m below the reference sole |
| `DRIVE_CAPACITY_PROBLEM` | ruled out | max saturation 0.12–0.31 through the whole baseline; drives never near ceiling |
| `FRICTION_PROBLEM` | ruled out | foot material 1.0/1.0, slip below 0.01 m/s until after the fall is decided |
| `MASS_COM_PROBLEM` | not indicated | COM 0.056 m anterior to the ankle, ankle at 23 % of foot length from the heel; both anatomically plausible |
| `JOINT_ANCHOR_PROBLEM` | not indicated | hip, knee and ankle anchors reproduce the canonical angles exactly, R6 unchanged |

## Recommended reopening, for owner decision

This is a GAM-6 substrate change and it touches how the accepted reference is
placed, so I have not made it. Two specific corrections, in order:

1. Register the physical rig to the platform: place it so the GAM-10 reference
   plantar anchor coincides with the platform contact plane. This is a
   placement change, not a change to GAM-10 geometry, and should be provable
   by a test asserting `reference plantar anchor y == support plane y`.
2. Seat the foot collider on the reference sole rather than centring it on the
   foot bone, so plantar contact happens where the accepted model says the
   sole is.

With both corrected, rerun Experiment A first. If the athlete then starts at
rest in contact, the ~2.4 deg ankle preload prediction becomes testable and
Experiment B can be answered honestly.

## Invariants and regression

`AddForce`, `AddTorque`, linear and angular velocity writes, `MovePosition`,
`MoveRotation`, physical transform writes, kinematic pelvis, foot pins, bar
parenting and hand constraints: all absent from the GAM-11 production path.
The only matches in the repository are `PhysicalBarbell.FreezeForInspection`
and `ApplyDiagnosticImpulse`, pre-existing GAM-8 authoring entry points that
the adapter and prototype controller never call. `PoweredJointController`
remains the only joint-drive writer and `positionSpring` / `positionDamper`
are written from the profile alone.

- EditMode: **63 / 63 passed.**
- PlayMode: 46 passed, 9 failed — the same eight as Phase 5 plus `E3`
  Experiment B. Two of those eight are headless-only
  (`requires a graphics device`); the rest are the real physical state.
- No new regressions. GAM-10 authority, joint sign and one-writer gates all
  pass.

GAM-11 stays `IN_PROGRESS`.
