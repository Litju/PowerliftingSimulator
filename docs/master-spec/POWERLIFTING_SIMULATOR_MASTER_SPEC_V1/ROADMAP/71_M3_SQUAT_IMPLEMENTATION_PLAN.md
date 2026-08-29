# M3 Squat Implementation Plan

**Document ID:** `PSMS-RM-71`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/70_MASTER_GAME_ROADMAP.md`, `SQUAT/18_SQUAT_TEST_SPEC.md`, `ENGINEERING/60_RUNTIME_EXECUTION_ORDER.md`

## Repository verification

- Repository map and baseline required before estimating file-level changes.
- Owner approves exact deletions/migrations after inspection.
- No M4 work begins until M3 receipt is accepted.

## Mission

Ship the actual humanoid performing a physically realized, competition-rule-aware squat with finite capability, physical
bar/contact, load-dependent grind/failure, replayable trace, and polished presentation.

## Non-mission

Do not implement bench, deadlift, career, custom rigid-body solver, HQP/WBC, muscle model, or broad refactor unrelated to
the squat vertical slice.

## Wave M3.0 — Repository reality and baseline

- inventory scenes, prefabs, assemblies, packages, input, M1/M2 systems/tests;
- identify every physical/transform writer;
- capture current unloaded and 60 kg behavior;
- map Quaternius bones/measurements/license;
- verify current tests/build;
- preserve evidence;
- produce deletion/migration map.

**Exit:** no code architecture change until owner can see actual baseline and proposed minimal delta.

## Wave M3.1 — Runtime/ownership foundation

- isolated local PhysicsScene and single tick driver;
- fixed 100 Hz pipeline;
- buffered fixed intent;
- immutable raw snapshot;
- writer/step assertions;
- physical/reference/visible hierarchy;
- remove/disable competing Animator/AddTorque/root/foot support paths.

**Tests:** duplicate step/writer, gravity-only fall, render no-writeback, fixed time, reset boundary.  
**Exit:** one physical authority proven.

## Wave M3.2 — Asset-driven physical athlete

- bind actual humanoid bones;
- measure segment geometry;
- assign source/engineering-derived masses/COM/inertia;
- primitive colliders and joint anchors;
- joint convention fixtures;
- visible follower overlay;
- initial-overlap report.

**Exit:** stable gravity/contact rig and accepted neutral visual overlay; no lift motion yet.

## Wave M3.3 — Powered joint and capacity fixtures

- family drive assets;
- finite force drive with no projection;
- command-side demand;
- capacity/profile/activation;
- isolated ankle/knee/hip/trunk/upper-body step tests;
- unloaded standing/settling;
- eliminate currentTorque utilization.

**Exit:** bounded nonoscillatory fixtures and stable standing without hidden support.

## Wave M3.4 — Physical bar/rack/back coupling

- one dynamic bar with legal-style mass/geometry;
- rack/platform contacts and plate loading;
- squat saddle/collider;
- unrack/walkout/settle;
- hands visual only;
- high-speed/contact/reset tests.

**Exit:** light bar physically retained, rack clear, no depenetration launch.

## Wave M3.5 — Squat reference/control/biomechanics

- independent SquatController/state machine;
- actual-asset reference curves;
- COM/support and bounded pose correction;
- bilateral depth landmarks;
- reference/actual ghost/debug;
- setup through unloaded legal lockout.

**Exit:** unloaded and light squat complete 9/9 with legal depth/lockout and acceptable video.

## Wave M3.6 — Rules/failures/load sweep

- current versioned squat rules;
- command flow;
- shallow/early/double descent/reversal/lockout/Rack tests;
- balance/collapse/reversal/stall/bar/posture detectors;
- moderate/heavy/supra-max sweeps;
- specifically reproduce and resolve/diagnose historical 60 kg lumbar-limit blocker without architecture expansion.

**Exit:** scenario matrix in PSMS-SQ-18 passes; failures emerge physically and are explained.

## Wave M3.7 — Presentation/telemetry/replay seed

- physical visible pose polish;
- hands/head/strain layers;
- squat camera/HUD/audio;
- raw trace and squat analysis;
- state replay for squat;
- required capture matrix.

**Exit:** owner-approved real-time/heavy/failure video and analysis definitions.

## Wave M3.8 — Hardening/receipt

- all EditMode/PlayMode;
- mutation suite;
- 30-attempt repeats;
- reset/soak;
- standalone build/smoke/log scan;
- p50/p95/p99 and GC;
- licenses/claims;
- clean tree/receipt.

## Mandatory deletions/forbidden survivors

No shipping path may retain:

- custom HQP/KKT/WBC as active controller;
- direct joint AddTorque from multiple systems;
- kinematic pelvis/root lock/foot pins;
- physical Animator writes;
- scripted bar motion;
- generic lift state machine;
- currentTorque athlete claim;
- safety action before failure snapshot.

Research code may remain only isolated, disabled, clearly named, and excluded from build/tests if owner approves.

## M3 acceptance

All PSMS-SQ-18 gates, shared foundation gates, visual evidence, standalone performance/build, source/license/claims, and
owner acceptance. Result receipt ends:

```text
MISSION=M3_PHYSICAL_ATHLETE_AND_SQUAT
STATUS=PASS|FAIL
M3_SQUAT=PASS|FAIL
ONE_PHYSICAL_AUTHORITY=YES|NO
HIDDEN_SUPPORT=NO|YES
UNLOADED=PASS|FAIL
LIGHT=PASS|FAIL
MODERATE=PASS|FAIL
HEAVY_GRIND=PASS|FAIL
SUPRAMAX_FAILURE=PASS|FAIL
RULE_MATRIX=PASS|FAIL
VISUAL_GATE=PASS|FAIL
PERFORMANCE=PASS|FAIL
STANDALONE=PASS|FAIL
OWNER_ACCEPTED=YES|NO
NEXT=M4A_BENCH_ONLY_IF_ACCEPTED
```
