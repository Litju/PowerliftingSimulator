# Testing and Terminus

**Document ID:** `PSMS-ENG-62`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/18_SQUAT_TEST_SPEC.md`, `BENCH/28_BENCH_TEST_SPEC.md`, `DEADLIFT/38_DEADLIFT_TEST_SPEC.md`, `PRESENTATION/56_VISUAL_QUALITY_GATES.md`

## Repository verification

- Inspect current Unity Test Framework assemblies, headless commands, and CI workflows.
- Map historical 69/69, 96/96, and 16/16 tests to current authority.
- Verify target standalone hardware and evidence-storage workflow.

## Definition of done

A milestone is complete only when architecture, deterministic tests, physical scenarios, mutation resistance, visual
evidence, performance, build smoke, logs, provenance, and owner review agree. “The editor compiles” or “tests are green”
is insufficient.

## Test pyramid

1. **Pure unit/EditMode:** math, frames, state machines, rules, capacity, filtering, serialization.
2. **Component fixtures:** joint axes/drives, contacts, grip/saddle, bar/equipment, humanoid binding.
3. **Integrated PlayMode:** full lift scenarios in isolated physics scene.
4. **Standalone smoke:** built player launch, scene flow, input, attempt, replay, save, exit.
5. **Visual/video qualification:** human review under PSMS-56.
6. **Performance soak:** p50/p95/p99, memory/GC, repeat/reset.
7. **Mutation tests:** prove forbidden mechanisms and threshold errors are caught.
8. **Release audit:** license, claims, build settings, package lock, artifacts.

## Deterministic scenario contract

Every scenario declares:

```text
scenario_id
lift_domain
scene/prefab
athlete_profile + hash
load/plate plan
initial state + hash
input trace
ruleset/calibration/config versions
Unity/PhysX/build
seed
duration/tick limit
expected state/event/result/failure
metric tolerances
required screenshots/video
```

No hidden editor state.

## Global architecture tests

- exactly one physics-step authority;
- exactly one physical joint writer;
- no Animator/transform ownership of physical bodies;
- no hidden support/force/foot pin/root lock;
- one dynamic bar;
- no direct bar velocity;
- no infinite authority/projection in valid attempts;
- controllers are lift-specific;
- rules use immutable snapshots;
- replay uses recorded state;
- source classes required;
- fixed SI/frame contract;
- reset clears complete state.

## Physics qualification sequence

Do not tune full lifts first:

1. asset scale/bone mapping;
2. mass/COM/inertia;
3. individual joint convention;
4. gravity/no-support;
5. platform/bench/floor contacts;
6. bar spawn/equipment;
7. coupling fixtures;
8. unloaded motion;
9. light load;
10. moderate;
11. heavy;
12. supra-max;
13. failure/safety;
14. reset/repeat;
15. standalone/performance.

## Mutation strategy

Mutants are explicit configuration/code branches in test-only assemblies or controlled edits:

- duplicate AddTorque/Simulate/Animator;
- kinematic pelvis/bar;
- infinite maximumForce/grip;
- wrong axes/signs;
- spawn overlap;
- missing bar mass;
- average bilateral rule;
- filtered rule input;
- currentTorque claim;
- phase/scripted outcome;
- safety before freeze;
- replay resim;
- missing provenance.

A test suite with no mutation evidence cannot claim it protects the architecture.

## Visual terminus

Required still/video matrices are defined per lift and PSMS-56. Each artifact has scenario/tick/build metadata and owner
acceptance. Cropping away a defect is invalid. A scenario that passes numerically but fails visual gates remains failed.

## Performance terminus

Standalone reference hardware, frozen graphics profile, repeated runs, warm-up excluded, p50/p95/p99/worst recorded.
No average-only acceptance. GC allocation, physics ticks, catch-up, GPU/CPU, memory, and long-session stability included.

## Build/CI terminus

- clean checkout/restore;
- package-lock verification;
- EditMode and PlayMode;
- Windows standalone build;
- smoke script;
- Player.log/error scan;
- scene/config invariants;
- save/replay round trip;
- artifact/upload;
- receipt hash;
- clean Git state.

## Milestone receipt

```text
MISSION
STATUS
BASE_HEAD
QUALIFIED_HEAD
TREE_HASH
UNITY_VERSION
PACKAGE_LOCK_HASH
TEST_COUNTS
SCENARIO_RESULTS
MUTATION_RESULTS
PERFORMANCE
VISUAL_EVIDENCE
BUILD_SMOKE
LOG_SCAN
LICENSE_CLAIM_AUDIT
KNOWN_LIMITATIONS
OWNER_ACCEPTED
NEXT_ACTION
```

No implementation model marks owner acceptance itself.

## Failure policy

- Code/test defect: fail, preserve evidence.
- Physics/calibration miss: fail with trace and smallest failing fixture.
- Technical initialization fault: separate from athlete/control failure.
- Nondeterminism: quarantine and reproduce with exact configuration.
- Flaky test: fail until root cause or explicit quarantined non-gating classification; no blind rerun-to-green.
- Missing evidence: fail.
- Unsupported scientific claim: fail release audit.

## Terminus by milestone

- **M3:** squat complete and independent.
- **M4A:** bench complete and independent.
- **M4B:** conventional deadlift complete and independent.
- **M5:** meet/broadcast.
- **M6:** replay/analysis.
- **M7:** career/product loop.
- **M8:** polish/performance/release.
- **M9:** optional post-launch/research, not required for V1.

## Tests for the testing system

Deliberately inject failing tests, failed build, Player.log error, missing screenshot, bad checksum, unsupported metric,
dirty tree, and stale receipt. CI must reject each.

## Final product terminus

`PASS` requires every release gate and no unresolved blocking open decision. Otherwise the correct status is
`PASS_WITH_OPEN_DECISIONS` or `FAIL`, with decisions/blocks named—not hidden behind a green count.
