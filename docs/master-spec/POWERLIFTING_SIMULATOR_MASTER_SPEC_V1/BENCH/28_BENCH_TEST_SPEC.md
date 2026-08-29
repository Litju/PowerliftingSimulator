# Bench Press Test Specification

**Document ID:** `PSMS-BP-28`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/22_BENCH_PHYSICS.md`, `BENCH/25_BENCH_RULES.md`, `BENCH/26_BENCH_FAILURE_MODEL.md`, `BENCH/27_BENCH_TELEMETRY.md`

## Repository verification

- Map historical tests/scenarios to this suite without weakening existing qualified behavior.
- Inspect CI/test assembly definitions and standalone launch tooling.
- Capture visual evidence from the actual asset and target build.

## Test authority

Independent bench suite. It cannot inherit squat expectations for contacts, phases, biomechanics, grip, or rules.

## Test layers

### EditMode

Bench state machine; Start/Press/Rack ordering; touch/elbow-depth geometry; required support contacts; pause stillness;
bilateral lockout; reference bar path; grip configuration; off-chest/midrange/imbalance detectors; telemetry/filter tests.

### Physics fixtures

- bench supports the powered/ragdoll athlete without hidden force;
- left/right compliant grip response, axial slide, shaft rotation, finite slip/break;
- bar unrack/rerack hook clearance;
- bar/chest contact volume;
- foot/bench friction;
- asymmetric perturbation;
- spawn/reset.

### Integrated scenarios

| ID | Scenario | Required result |
|---|---|---|
| BP-P00 | drives/grips off | no hidden support; bar falls |
| BP-P01 | empty/light bar setup | stable contacts, unrack/start |
| BP-P02 | light full attempt | valid touch/pause/press/lockout/rack |
| BP-P03 | moderate | repeatable good lift |
| BP-P04 | heavy | visible press slowdown/sticking, good lift |
| BP-P05 | supra-max | off-chest or midrange physical failure |
| BP-R01 | early descent | no-lift |
| BP-R02 | invalid touch | no-lift |
| BP-R03 | one elbow above shoulder criterion | no-lift |
| BP-R04 | bar moving at bottom | no Press authorization |
| BP-R05 | early press | no-lift |
| BP-R06 | one elbow not locked | no-lift |
| BP-R07 | early rack | no-lift |
| BP-F01 | left capacity reduced | one-arm imbalance/failure |
| BP-F02 | grip capacity reduced | physical slip/loss of control |
| BP-F03 | required contact lost | configured rule failure |
| BP-E01 | grips created misaligned | initialization fault |

## Acceptance

- One dynamic bar; two finite compliant grip adapters; no parenting/teleport/projection.
- Light/moderate 9/9 good lifts with valid contacts/touch/elbow-depth/pause/commands/lockout.
- Heavy grind is visible but bar remains controlled.
- Supra-max failure is repeatable and safely presented after truth freeze.
- Bar endpoint tilt remains below good-lift visual/rule bound in successful cases.
- Hands remain visually attached within tolerance without driving physics.
- 0 B/tick after warm-up; no NaN/Inf, projection, duplicate writer, or stale contact after reset.

## Mutation matrix

Infinite grips; all grip DOFs locked; projection; kinematic/parented bar; visual IK writes physical hands; pause by timer;
average bilateral elbow depth; leg-drive AddForce; Start/Press/Rack bypass; currentTorque claims; safety catch before latch.

## Visual checkpoints

Setup/arch, grip, unrack, Start, mid-descent, chest touch, pause, early press violation, off-chest failure, heavy sticking,
one-arm imbalance, lockout, rerack, and safe failure. Reject floating hands, bar through torso, detached shoulders, butt/
feet visibly contradictory to rule state, extreme wrist break, rack snag, or spotter/safety changing result.

## Build receipt

M4A receipt includes all test/performance/visual evidence, exact rulebook/ruleset version, grip configuration, known
shoulder-model limitations, and `M4A_BENCH=PASS|FAIL`.
