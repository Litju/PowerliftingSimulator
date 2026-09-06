# GAM-11 squat model reconciliation

Base: `9cef01d` (GAM-10 merged)
Rejected state preserved at: `checkpoint/gam11-pre-opus-reconcile` = `543caf1`

## 1. What the forensic pass actually found

The working hypothesis behind this mission was that GAM-11 had authored a
second squat geometry inside `SquatPhysicalAdapter`. That hypothesis is
**falsified**. There are no replacement knee/hip/ankle curves, no
`1.35 rad`-family clamps, no `hip = knee + delta` relation, and no
load-dependent technique branch anywhere in the adapter.

`SquatPhysicalAdapter` builds its target tables by calling
`SquatReferenceProfile.Evaluate` and `SquatReferenceKinematics.Solve`
directly. The profile id is `CANONICAL_POWERLIFTING_SQUAT_V2_CLOSED_CHAIN`
and its waypoints are the owner-accepted V2 values exactly
(`27/114/111/16` at legal bottom, and so on). The property name
`CanonicalPowerliftingSquatV1` is a stale accessor name on the V2 profile,
not a second profile.

The reference-to-physical mapping is also correct, and now measured rather
than assumed:

| joint | canonical anatomical @ s_q 0.25 | measured logical target | flexion sign |
|---|---|---|---|
| ankle (`*_foot`) | +10.00 deg dorsiflexion | -10.00 deg about X | negative |
| knee (`*_shank`) | +32.00 deg flexion | +32.00 deg about X | positive |
| hip (`*_thigh`) | +30.00 deg flexion | -30.00 deg about X | negative |
| trunk (`abdomen`+`thorax`) | +10.00 deg flexion | +2.00 / +3.00 deg about X | positive |

Magnitudes transfer exactly; only the per-joint sign convention differs, and
it is consistent left/right. At `s_q = 0` every controlled joint's logical
target is identity to **0.00 deg**, so the accepted standing pose and the
physical spawn neutral agree.

**The physical system is asking for the accepted movement family. It cannot
perform it.**

## 2. Authority map

| variable | GAM-10 authority | GAM-11 source | drift | action |
|---|---|---|---|---|
| s_q, phase | n/a (preview scrubs) | adapter state machine | no | keep |
| ankle / knee / hip / trunk target | `SquatReferenceProfile` V2 | same, via `SquatReferenceKinematics.Solve` | **no** | keep |
| joint coordinate axes / signs | `SquatReferenceRigCalibration` | derived per joint, verified by R6 | no | keep |
| joint limits | GAM-6/7 anatomical | symmetrised in GAM-11 | **yes** | reverted (`7c1038d`) |
| target angular velocity | n/a | always `Vector3.zero` | no | keep |
| balance correction | n/a | ankle/trunk target offsets | **yes, unbounded** | bounded (`7eed6c8`) |
| drive spring / damper | GAM-7 `JointFamilyProfile` | multiplied by `capacityScale` (>= 3.8) | **yes** | reported, not yet reverted |
| drive-demand telemetry | GAM-7 | modelled unscaled gains | **yes** | corrected (`7eed6c8`) |
| foot anchors | reference plantar anchor | same | no | keep |
| support centre | n/a | midpoint of the two foot **body centres** | suspect | see 5 |
| bar / saddle | n/a | one finite `ConfigurableJoint` | no | keep |
| legal depth | `SquatDepthGeometry` | tests use vertical pelvis descent only | **yes** | not addressed |

## 3. Confirmed defects, and what was repaired

1. **Symmetrised joint limits** (`7c1038d`). Knee `-5..145` had become
   `-145..145`, hip high `+45` had become `+120`, ankle low `-45` had become
   `-55`. R2/R6 show there was no sign error for those limits to compensate,
   so they only removed the anatomical guard against a knee or hip buckling
   backwards. Restored to the GAM-6/7 values.

2. **Balance correction exceeded its own declared bound** (`7eed6c8`). The
   ankle offset was applied at `3.0x` the `MaxBalanceCorrectionRad` clamp, so
   it could trim the ankle by 30 deg while the accepted reference ankle only
   travels 27 deg across the entire squat. The multiplier is now `1.0`. The
   direction was verified correct by R6: a forward COM error does drive the
   ankle away from dorsiflexion.

3. **Drive-saturation telemetry under-reported by the capacity scale**
   (`7eed6c8`). `Drive()` writes `spring * capacityScale`, but
   `BuildDiagnostic` modelled the unscaled `profile.Spring`, so the HUD's
   `MAX_DRIVE_SATURATION` was low by a factor of ~4-5. Now modelled on the
   gains actually written.

## 4. The real root cause of the owner rejection

With those three props removed, the unloaded athlete falls forward from the
accepted standing pose in about 2.2 s and comes to rest with the pelvis at
**0.126 m**. The owner's rejected editor screenshot shows **0.122 m**. The
failure reproduces exactly.

Trace (`Artifacts/Measurements/GAM11-standing-hold-unloaded.csv`), no squat
input, `s_q = 0` throughout:

```
tick   pelvis_y   ap_com_error   ankle_balance   drive_saturation
   0     1.0748       -0.0221         0.0177           0.102
  75     1.0128       +0.0929        -0.0968           0.878
 125     1.0166       +0.2229        -0.1745 (max)     0.993
 200     0.6922       +0.8138        -0.1745           0.543
 225     0.1176       +1.0577        -0.1745           0.183
```

The centre of mass leaves the foot support monotonically from the first
tenth of a second. The balance offset saturates at its bound by tick 125 and
never arrests the fall.

This is not a gain-tuning problem. A bounded ankle *target offset* cannot
stabilise this system, because the usable ankle moment is capped by the foot
tipping about its toe (roughly `m*g*half_foot_length`, about 88 Nm), not by
joint capacity — measured drive demand at the moment of departure is only
0.5-1.0, so the drives are not the limiter. Once the COM passes the toe there
is no recovery available to a target-offset controller.

**The previous GAM-11 green result was produced by the props, not by a stable
squat.** Three of them (30 deg ankle trim, symmetric limits, 4-5x spring
inflation) each did part of the work of holding the athlete up, and the
saturation metric that would have exposed the fourth was itself understated.

## 5. Open items, deliberately not hacked

- **Support centre definition.** `_supportCenter` is the midpoint of the two
  foot *rigidbody centres*, not the contact polygon centre. If the foot body
  origin is not the mid-foot, the AP error the controller regulates is biased
  by that offset. Worth measuring before any balance redesign.
- **Drive spring inflation.** `capacityScale` still multiplies
  `positionSpring`, which is an authority increase over the GAM-7-qualified
  drive family. It is left in place and reported rather than reverted,
  because removing it is a change to the qualified substrate and needs its
  own decision.
- **Depth.** `test_descent_m` (vertical pelvis drop) is still standing in for
  squat legality. `rule_depth_left_m` / `rule_depth_right_m` / `legal_depth`
  from `SquatDepthGeometry` are not wired into the physical tests.
- **Trunk split.** The canonical +10 deg trunk flexion arrives as +2 deg
  abdomen and +3 deg thorax. The sum is 5 deg, not 10. This does not affect
  the fall, but it means the physical trunk requests half the accepted trunk
  inclination and should be reconciled.

## 6. Test results after the repairs

| test | before | after |
|---|---|---|
| G1 unloaded | Passed | **Failed** — final pelvis 0.127 m vs standing 1.021 m |
| G2 25 kg | Passed | **Failed** — bar did not return to lockout |
| G3 105 kg | Failed (accepted) | Failed |
| G4 mutation gates | Passed | Passed |
| R1 reference source is GAM-10 V2 | n/a | Passed |
| R2 standing target is spawn neutral | n/a | Passed (0.00 deg) |
| R3 unloaded standing hold | n/a | **Failed** — falls to 0.126 m |
| R4 25 kg standing hold | n/a | **Failed** — falls to 0.126 m |
| R5 balance correction bounded | n/a | Passed |
| R6 joint frame sign calibration | n/a | Passed |

The G1/G2 regressions are the point, not a setback: they are the same
physical failure the owner saw, now visible to the suite instead of hidden
by the props.

## 7. Status

`STOP` per mission section 28. A stable standing pose currently requires
either authority the mission forbids, or a balance controller that is a
design change rather than a repair: the present design can only emit bounded
ankle and trunk target offsets, and that is provably insufficient once the
COM approaches the toe.

The reference ghost overlay and the owner Play-mode handoff were **not**
built. Their prerequisite (section 12: the athlete approximately attempts the
accepted movement with balance offsets at zero) is not met, and staging a
handoff around an athlete that falls over at `s_q = 0` would misrepresent the
state.

GAM-11 remains `IN_PROGRESS`.
