# Squat Test Specification

**Document ID:** `PSMS-SQ-18`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/12_SQUAT_PHYSICS.md`, `SQUAT/15_SQUAT_RULES.md`, `SQUAT/16_SQUAT_FAILURE_MODEL.md`, `SQUAT/17_SQUAT_TELEMETRY.md`

## Repository verification

- Map historical tests/scenarios to this suite without weakening existing qualified behavior.
- Inspect CI/test assembly definitions and standalone launch tooling.
- Capture visual evidence from the actual asset and target build.

## Test authority

This suite qualifies only the squat domain. Passing bench/deadlift tests cannot substitute. Every scenario uses a frozen
athlete profile, bar load, reference/calibration versions, input trace, initial pose, random seed, Unity/PhysX settings,
and expected observations.

## Test layers

### A. Pure EditMode

- frame/landmark/depth geometry;
- state-machine transition table;
- rule predicates and precedence;
- reference curve continuity/endpoints;
- capacity monotonicity and bounded fatigue;
- failure detector persistence;
- telemetry equations/filter golden vectors;
- serialization/schema invariants.

### B. Isolated physics fixtures

1. joint positive-axis pulses;
2. gravity-only athlete falls;
3. feet/platform friction;
4. bar/back saddle response and break;
5. rack hook clearances;
6. spawn overlap/depenetration;
7. reset identity.

### C. Integrated deterministic scenarios

| ID | Scenario | Required result |
|---|---|---|
| SQ-P00 | drives off | athlete/bar not secretly supported |
| SQ-P01 | unloaded canonical input | settle → legal depth → lockout |
| SQ-P02 | 20–40 kg light | good lift, low/moderate demand |
| SQ-P03 | 60 kg blocker regression | complete or provide explicit joint/limit failure; no architectural collapse |
| SQ-P04 | calibrated moderate | repeatable good lift |
| SQ-P05 | calibrated heavy | visible grind/sticking, eventual good lift |
| SQ-P06 | supra-max | physical stall/failure without load-threshold script |
| SQ-R01 | shallow reversal | physical completion possible, rule no-lift |
| SQ-R02 | early descent | command no-lift |
| SQ-R03 | early rack | no-lift |
| SQ-F01 | forward balance perturbation | forward balance failure |
| SQ-F02 | backward perturbation | backward balance failure |
| SQ-F03 | saddle capacity reduced | bar/posture loss |
| SQ-F04 | lumbar limit mutation | failure diagnosed before redesign |
| SQ-E01 | bar spawned overlapping | initialization fault, not controller failure |

## Quantitative acceptance

- No kinematic athlete segment, foot pin, hidden force, transform-driven phase, or infinite drive.
- Light/moderate scenarios complete legal depth and lockout in 9/9 deterministic repeats.
- Heavy scenario has a reproducible low-velocity/high-demand interval and no catastrophic mesh/constraint failure.
- Supra-max fails in the intended detector class in 9/9 repeats.
- Initial forbidden penetration ≤ configured spawn tolerance.
- Reset restores positions/orientations/velocities/config versions within named reset tolerances.
- Fixed pipeline allocates 0 B/tick after warm-up.
- No NaN/Inf, projection, or duplicate writer.
- Visual landmarks remain within PSMS-56 gates.

## Mutation matrix

| Mutation | Expected catcher |
|---|---|
| pelvis kinematic | T-A physical authority |
| foot pin/lock | T-A/T-C |
| infinite maxForce | T-B |
| second AddTorque balance writer | T-A |
| wrong knee axis | joint fixture/T-C |
| shallow depth average | rule unit test |
| omit bar mass | COM/load regression |
| currentTorque as utilization | claims/schema test |
| spawn overlap | initialization test |
| Animator writes physical bones | ownership/visual test |
| scripted bar velocity | physical authority test |

## Visual checkpoints

1920×1080 canonical captures/video at setup, unrack/walkout, quarter descent, legal bottom, reversal, heavy sticking,
lockout, forward failure, backward failure, and rerack. Reject broken knees/hips, floating bar, severe hand mismatch,
foot skating, collider ejection, spine deformation, mesh separation, or accidental ragdoll success.

## Performance/build

Profile standalone at 60 fps target and 100 Hz physics. Record p50/p95/p99 physics, render, GC, and total frame.
Run EditMode, PlayMode, Windows build, smoke launch, Player.log scan, scene-list invariant, and deterministic replay
playback.

## Completion receipt

The M3 receipt contains commit/tree, Unity version, test counts, scenario table, performance distribution, visual artifact
hashes, configuration/calibration IDs, known limitations, and explicit `M3_SQUAT=PASS` or `FAIL`.
