# M4A Bench Implementation Plan

**Document ID:** `PSMS-RM-72`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/71_M3_SQUAT_IMPLEMENTATION_PLAN.md`, `BENCH/28_BENCH_TEST_SPEC.md`

## Repository verification

- M3 accepted foundation is prerequisite.
- Current official bench bottom rule language must be verified.
- No deadlift implementation begins before M4A acceptance.

## Mission

Add an independently designed physical bench press on the accepted powered-athlete substrate: setup/arch/contacts,
two compliant grips, physical bar, Start/touch/pause/Press/lockout/Rack, load-dependent failure, telemetry, replay, and
presentation.

## Hard rule

Do not copy SquatController and rename phases. Shared code is limited to athlete, bar primitive, fixed pipeline, snapshots,
input infrastructure, cameras/rendering utilities, and test/build infrastructure.

## Wave M4A.0 — Reality map

Inventory existing bench M1 rules/prototypes/presentation, bench/rack assets, physical colliders, UI/audio, tests. Write
the exact delta and identify any squat-specific leakage in shared substrate.

## Wave M4A.1 — Bench equipment and setup contacts

- measured bench/rack geometry;
- head/upper-back/shoulder/glute/foot contacts;
- authored setup/arch/foot reference;
- stable physical body on bench;
- rule contact observer;
- setup visual gates.

**Exit:** stable setup with no hidden support or body ejection.

## Wave M4A.2 — Bilateral grip fixtures

- independent bench grip adapter type;
- finite radial/orientation drives;
- free shaft rotation and limited axial slide;
- symmetric initial alignment;
- low/high grip and unilateral/asymmetry tests;
- no projection/parenting.

**Exit:** bar can be carried/unracked, both hands remain plausible, reduced grip physically slips/fails.

## Wave M4A.3 — Bench reference/control

- `s_b`, setup/descent/press branches;
- Start/Press/Rack state machine;
- physical bar-aware reference hand targets;
- finite shoulder/elbow/wrist capacity;
- touch freeze and command gates;
- bounded bilateral player correction.

**Exit:** light full physical rep through rack.

## Wave M4A.4 — Biomechanics/rules

- touch volume/location;
- direct stillness/pause;
- current bilateral elbow-vs-shoulder bottom proxy;
- required contacts;
- bar path/tilt/symmetry;
- bilateral lockout;
- exact current ruleset locators/tests.

**Exit:** good/invalid touch/one-elbow/moving pause/early command matrices pass.

## Wave M4A.5 — Failure/load sweep

- off-chest/midrange/one-arm/grip/reversal/lockout failures;
- moderate/heavy/supra-max;
- safety catch/spotter only after truth freeze;
- reset/repeat.

**Exit:** PSMS-BP-28 scenario matrix.

## Wave M4A.6 — Presentation/analysis

- bench cameras/HUD/audio;
- hand/finger/strain polish;
- touch/pause/press replay;
- metrics/provenance;
- visual capture matrix.

## Wave M4A.7 — Hardening

Mutation, performance, standalone, save/replay compatibility, claims/license, receipt.

## Mandatory bench-specific evidence

- two grip DOF/config diagrams;
- grip step/slip/break traces;
- bench contact truth overlay;
- bilateral elbow-depth screenshots;
- raw bar velocity at pause;
- endpoint tilt under asymmetry;
- safety ordering replay.

## Acceptance receipt

```text
MISSION=M4A_BENCH
STATUS=PASS|FAIL
M3_FOUNDATION_UNCHANGED=YES|NO
INDEPENDENT_BENCH_DOMAIN=YES|NO
COMPLIANT_BILATERAL_GRIPS=PASS|FAIL
SETUP_CONTACTS=PASS|FAIL
TOUCH_ELBOW_PAUSE_RULES=PASS|FAIL
LIGHT_MODERATE=PASS|FAIL
HEAVY_GRIND=PASS|FAIL
SUPRAMAX_FAILURE=PASS|FAIL
VISUAL_PERFORMANCE_BUILD=PASS|FAIL
OWNER_ACCEPTED=YES|NO
NEXT=M4B_DEADLIFT_ONLY_IF_ACCEPTED
```
