# Deadlift Test Specification

**Document ID:** `PSMS-DL-38`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/32_DEADLIFT_PHYSICS.md`, `DEADLIFT/35_DEADLIFT_RULES.md`, `DEADLIFT/36_DEADLIFT_FAILURE_MODEL.md`, `DEADLIFT/37_DEADLIFT_TELEMETRY.md`

## Repository verification

- Map historical tests/scenarios to this suite without weakening existing qualified behavior.
- Inspect CI/test assembly definitions and standalone launch tooling.
- Capture visual evidence from the actual asset and target build.

## Test authority

Independent conventional-deadlift suite. No squat phase, contact, balance, bar saddle, or rule abstraction may satisfy it.

## Test layers

### EditMode

State/contact-mode machine; no-start/Down rule; floor-clearance hysteresis; lockout geometry; preload cap; conventional
reference curves; zone/stall/failure logic; grip and telemetry/provenance.

### Physics fixtures

- dynamic loaded bar resting on platform;
- physical floor contact/clearance;
- bilateral finite grips and slip;
- foot friction;
- bar-shin/thigh contact/proximity;
- high-speed return/drop CCD;
- spawn/reset; no kinematic mode switch.

### Integrated scenarios

| ID | Scenario | Required result |
|---|---|---|
| DL-P00 | no drive/grip | bar remains floor-supported; athlete not hidden-supported |
| DL-P01 | grip/brace/slack only | no unintended floor break |
| DL-P02 | light pull | grounded → floor break → knee pass → lockout → Down → ground |
| DL-P03 | moderate | repeatable good lift |
| DL-P04 | heavy | visible below-knee/knee/hip slowdown and good lift |
| DL-P05 | supra-max | cannot break floor or defined zone stall |
| DL-F01 | low grip | physical grip slip/failure |
| DL-F02 | bar-close correction removed | larger drift/demand/failure as calibrated |
| DL-F03 | one hip/knee capacity reduced | asymmetric/posture failure |
| DL-R01 | downward reversal | no-lift/failure |
| DL-R02 | incomplete knees/hips/shoulder geometry | no-lift |
| DL-R03 | early drop before Down | no-lift |
| DL-R04 | uncontrolled return | no-lift |
| DL-E01 | bar spawned in platform/legs | initialization fault |
| DL-X01 | attempted sumo flag | compile/config rejection in V1 |

## Acceptance

- Bar remains one dynamic Rigidbody from floor through return; never kinematic, parented, or velocity-scripted.
- Pull-slack cannot create persistent floor clearance.
- Floor break is contact/clearance observed.
- Light/moderate good lift 9/9; heavy grind 9/9 without catastrophic bar/leg snag; supra-max repeatable failure.
- Grip failures are physical constraint events.
- Lockout requires all frozen predicates; Down/return sequence observed.
- 0 B/tick, no NaN/Inf/projection/duplicate writer, deterministic reset.

## Mutations

Set bar kinematic while grounded; Drive sets bar velocity; floor break from phase/input; infinite grip; disable gravity;
reuse squat controller; bar height-only lockout; auto Down; early release hidden by safety; sumo as stance parameter;
filter contact event.

## Visual checkpoints

Setup, grip, brace/slack, first floor clearance, mid-shin, below knee, knee pass, heavy sticking, lockout, Down, controlled
ground contact, cannot-break-floor, grip failure, bar drift, and early drop. Reject floating grip, bar teleport, severe
shin penetration, elbow curl as prime motion, foot slide, spine catastrophe, bar passing through thighs, or safety erasing
failure.

## Build receipt

M4B receipt records conventional-only scope, physical mode transitions, rule version, load sweep, performance, visual
evidence, limitations, and `M4B_DEADLIFT=PASS|FAIL`.
