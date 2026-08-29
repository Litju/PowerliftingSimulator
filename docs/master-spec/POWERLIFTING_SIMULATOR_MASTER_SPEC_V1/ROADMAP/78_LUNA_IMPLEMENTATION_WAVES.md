# Luna Implementation Waves

**Document ID:** `PSMS-RM-78`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/70_MASTER_GAME_ROADMAP.md`, `ROADMAP/71_M3_SQUAT_IMPLEMENTATION_PLAN.md`, `ROADMAP/72_M4_BENCH_IMPLEMENTATION_PLAN.md`, `ROADMAP/73_M4_DEADLIFT_IMPLEMENTATION_PLAN.md`, `ENGINEERING/62_TESTING_AND_TERMINUS.md`

## Repository verification

- Use only after the repository-access prompt authorizes Luna execution.
- Update file paths/names from the reality map without changing frozen responsibilities.
- Owner controls commits, pushes, merges, and milestone acceptance.

## Role

GPT-5.6 Luna is the repository-access coding model. This document tells it how to implement without inventing architecture.
Luna must inspect reality, make bounded changes, run local evidence loops, and stop at milestone gates.

## Global execution contract

1. Read `00_READ_ME_FIRST.md`, `manifest.json`, the current milestone plan, and all direct dependencies.
2. Inspect repository; never claim inspection not performed.
3. Produce a short reality/delta report before editing.
4. Preserve qualified M1/M2 behavior unless explicitly superseded.
5. Implement one wave at a time.
6. Add/modify tests with the implementation.
7. Run the smallest tests first, then full gates.
8. Preserve evidence and exact commands/results.
9. Do not commit/push/merge unless the owner's execution prompt authorizes it.
10. Stop at the wave/milestone terminus.
11. Never begin the next milestone without accepted receipt.

## First repository pass

Luna reports:

```text
REPO_ROOT
HEAD/TREE/BRANCH/CLEAN
UNITY_VERSION
PACKAGES
SCENES
ASSEMBLIES
TEST_BASELINE
BUILD_BASELINE
HUMANOID_ASSET_PATH/HIERARCHY/AVATAR/LICENSE
M1_RULE_MEET_COMPONENTS
M2_PRESENTATION_COMPONENTS
PHYSICS_STEP_OWNERS
TRANSFORM/ANIMATOR/FORCE/TORQUE_WRITERS
BAR/EQUIPMENT_PREFABS
TRACE/REPLAY/SAVE
CI/HOOKS
CURRENT_M3_FAILURE_REPRO
DELTA_PLAN
RISKS/BLOCKERS
```

No code change until this map is complete.

## Wave implementation template

For every wave:

```text
MISSION
SCOPE
NON_SCOPE
BASE_HEAD/TREE
FILES_EXPECTED
INVARIANTS
IMPLEMENTATION_STEPS
TESTS_FIRST
MUTATIONS
VISUAL_EVIDENCE
PERFORMANCE_CHECK
BUILD_CHECK
SOURCE/CLAIM_CHECK
STOP_CONDITIONS
RECEIPT
```

## Minimal change rule

Prefer adapting existing qualified systems to the frozen roles over parallel rewrites. Delete or isolate duplicate
authority. Do not create abstractions before at least two real consumers prove commonality; even then, lift mechanics remain
separate.

## Coding boundaries

### Allowed shared runtime namespaces/responsibilities

- bootstrap/tick/time/input buffer;
- coordinate/math/tolerance;
- humanoid binding/physical segments;
- powered joint/capacity interface;
- physical bar primitive;
- snapshot/event/telemetry infrastructure;
- rendering/presentation utilities;
- test/evidence/build tooling.

### Required independent namespaces/responsibilities

- Squat reference/biomechanics/physics/control/rules/failure/telemetry/tests;
- Bench equivalents;
- Conventional deadlift equivalents.

A common `IPhysicalLift` may expose lifecycle only.

## Scientific implementation rule

Every deterministic calculation is a pure processor where practical. User-facing strings are generated from typed results.
No LLM calculates, invents, or edits numerical attempt truth. Source classes/units/frames are mandatory.

## Physics implementation rule

Before tuning a full lift:

- prove scale/bones/mass/inertia;
- prove axes/joints;
- prove contacts;
- prove coupling;
- prove one writer/step;
- prove unloaded;
- then add load.

A controller failure at all loads, including unloaded, is architecture/frame/authority first—not a strength calibration
problem.

## Tool/evidence loop

Use repository-native tools. A typical loop:

```text
inspect
-> edit
-> format/static checks
-> focused EditMode
-> focused PlayMode/fixture
-> deterministic scenario
-> screenshot/replay/log
-> full tests
-> standalone build/smoke
-> git diff/status
-> receipt
```

Do not use blind mass replacement for physical/rule code. Keep changes reviewable.

## Stop conditions

Stop with `BLOCKED` when:

- repository reality invalidates an invariant/critical assumption;
- asset/license/source unavailable;
- a fixture cannot pass without forbidden mechanism;
- current official rule is ambiguous/unverified;
- performance requires changing fixed rate/architecture;
- visual owner decision required;
- destructive repository action not authorized;
- existing qualified behavior would be lost and alternatives need owner choice.

A block report gives evidence and 2–3 bounded options with tradeoffs.

## Final design-only status

At the time of this master specification:

```text
MISSION=POWERLIFTING_SIMULATOR_MASTER_GAME_SPEC_V1
EXECUTION_MODE=DESIGN_ONLY_NO_REPOSITORY_ACCESS
DESIGN_BUNDLE=COMPLETE
ARCHITECTURE=FROZEN_WITH_REPOSITORY_VERIFICATION_POINTS
M3_IMPLEMENTED=NO
M4A_IMPLEMENTED=NO
M4B_IMPLEMENTED=NO
M5_IMPLEMENTED=NO
M6_IMPLEMENTED=NO
M7_IMPLEMENTED=NO
M8_IMPLEMENTED=NO
STATUS=PASS_WITH_OPEN_DECISIONS
NEXT_ACTION=LUNA_REPOSITORY_REALITY_MAP_THEN_M3_WAVE_0
STOP
```

The implementation model must not change this historical design receipt. It creates new milestone receipts with actual
repository evidence.
