# M4B Conventional Deadlift Implementation Plan

**Document ID:** `PSMS-RM-73`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/71_M3_SQUAT_IMPLEMENTATION_PLAN.md`, `DEADLIFT/38_DEADLIFT_TEST_SPEC.md`

## Repository verification

- Accepted M3 foundation required; M4A may be complete first per roadmap.
- Current deadlift rule language and lockout proxy must be verified.
- No sumo implementation in this milestone.

## Mission

Add an independent conventional deadlift with physical grounded-to-free bar transition, finite grips, brace/slack/pull,
close path, zone-specific grind/failure, legal lockout/Down/return, telemetry, replay, and presentation.

## Scope firewall

Conventional only. Sumo is not a flag, stance preset, or hidden branch. No squat bar saddle, squat phase, bench pause, or
generic lift controller is reused as mechanics.

## Wave M4B.0 — Reality map

Inventory current deadlift M1 rules/prototypes, platform/bar assets, grip code, presentation/tests. Prove shared foundation
has no bench/squat assumptions.

## Wave M4B.1 — Grounded bar/contact mode

- dynamic bar rests on platform;
- floor/plate contact and clearance landmarks;
- contact hysteresis;
- no kinematic switch/gravity toggle;
- floor impact/CCD and reset;
- conventional setup geometry.

**Exit:** stable grounded bar and trustworthy floor-break observer.

## Wave M4B.2 — Deadlift grips/setup/slack

- independent deadlift grip adapter;
- grip/brace state;
- preload/slack game scalar;
- ensure slack cannot clear floor;
- foot/bar-leg collision/filtering;
- grip slip/break fixtures.

**Exit:** setup/preload stable, finite grip, no scripted lift.

## Wave M4B.3 — Reference/control

- `s_d`, setup/preload/floor-to-knee/knee-to-lockout/descent curves;
- physical floor-break gating;
- knee-pass and close-bar reference bias;
- elbow near-extension;
- Down-controlled descent;
- independent state machine.

**Exit:** light complete pull/return.

## Wave M4B.4 — Biomechanics/rules

- midfoot/bar distance, leg proximity, joint geometry;
- lockout knee/hip/trunk/front-deltoid proxies;
- no-start and Down;
- downward movement/return control;
- current rule source/tests.

## Wave M4B.5 — Failure/load sweep

- cannot break floor;
- below-knee, knee, hip-extension stalls;
- bar drift;
- grip failure;
- downward reversal;
- failed lockout;
- early drop/uncontrolled return;
- moderate/heavy/supra-max.

**Exit:** PSMS-DL-38 matrix.

## Wave M4B.6 — Presentation/analysis

Deadlift cameras, grip/strain/contact audio, floor-break/knee-pass/sticking replay, metrics, visual matrix.

## Wave M4B.7 — Hardening

Mutations, performance, repeated reset, standalone, source/license/claims, receipt.

## Required proof

- bar remains dynamic across all states;
- no code sets bar velocity/position to advance lift;
- floor break is direct contact/clearance event;
- slack-only never clears floor;
- grip loss changes physics;
- lockout is not bar-height-only;
- Down precedes controlled return;
- attempted sumo configuration is rejected.

## Acceptance receipt

```text
MISSION=M4B_CONVENTIONAL_DEADLIFT
STATUS=PASS|FAIL
INDEPENDENT_DEADLIFT_DOMAIN=YES|NO
BAR_DYNAMIC_ALL_PHASES=YES|NO
PHYSICAL_FLOOR_BREAK=PASS|FAIL
SLACK_NO_LIFT=PASS|FAIL
FINITE_GRIPS=PASS|FAIL
LOCKOUT_DOWN_RETURN_RULES=PASS|FAIL
LIGHT_MODERATE=PASS|FAIL
HEAVY_GRIND=PASS|FAIL
SUPRAMAX_FAILURE=PASS|FAIL
SUMO_OUT_OF_SCOPE_ENFORCED=YES|NO
VISUAL_PERFORMANCE_BUILD=PASS|FAIL
OWNER_ACCEPTED=YES|NO
NEXT=M5_ONLY_IF_ACCEPTED
```
