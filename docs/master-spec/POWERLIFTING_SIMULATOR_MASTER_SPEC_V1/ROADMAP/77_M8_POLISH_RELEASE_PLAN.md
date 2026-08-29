# M8 Polish and Release Plan

**Document ID:** `PSMS-RM-77`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/76_M7_PRODUCT_GAME_LOOP_PLAN.md`, `PRESENTATION/56_VISUAL_QUALITY_GATES.md`, `ENGINEERING/63_PERFORMANCE_BUDGET.md`, `ENGINEERING/64_BUILD_RELEASE_AND_CI.md`

## Repository verification

- Freeze storefront/platform/system requirements.
- Complete license/privacy/legal review appropriate to release.
- Owner accepts final RC evidence and known limitations.

## Mission

Qualify and ship the complete V1 product. No new mechanical subsystem unless it fixes a release blocker through an
approved ADR.

## Wave M8.0 — Feature freeze

- all prior receipts accepted;
- V1 content/claims/platform frozen;
- open decisions/known limitations triaged;
- release branch and version;
- no scope growth.

## Wave M8.1 — Visual/animation/contact polish

Execute complete PSMS-56 matrix, fix clipping/jitter/hand/foot/bar/camera/lighting/UI defects, capture final evidence.
No visual fix may write physical truth.

## Wave M8.2 — Audio/UI/accessibility/localization readiness

Command clarity, mixer/loudness, subtitles, color-independent lights, input presets/remapping scope, text scaling, aspect
ratios, settings/error recovery, credits/licenses/limitations.

## Wave M8.3 — Performance/memory/soak

Reference/minimum hardware, every lift/venue/mode, cold/warm loads, p50/p95/p99/worst, GPU/CPU, 100 Hz physics, GC,
memory/storage, 60-minute soak, repeated reset/replay/save.

## Wave M8.4 — Build/CI/release audit

Clean checkout, package lock, all tests/mutations, Windows build, clean-machine smoke, Player.log, save migration/backup,
replay compatibility, source/license/claim audit, privacy, artifacts/checksums, rollback.

## Wave M8.5 — Store/product readiness

Name/description/screenshots/trailer/control/system requirements/privacy/license/attribution/support/known limitations.
Marketing language must match PSMS-65.

## Wave M8.6 — Release candidate

RC1 → defect triage → RC2 only for blockers → final acceptance. Re-run full gates after any code/data change. Preserve
receipts and evidence.

## Release blockers

- any hidden support/duplicate authority/scripted bar;
- incomplete/unstable lift scenario;
- rule/version error;
- missing/broken replay/save;
- unsupported scientific claim;
- critical visual defect;
- p95/p99/performance or GC failure;
- crash/error log;
- missing license;
- dirty/unreproducible build;
- owner not accepted.

## Final receipt

```text
MISSION=M8_V1_RELEASE
STATUS=PASS|FAIL
RELEASE_VERSION=
QUALIFIED_HEAD=
TREE_HASH=
UNITY_PACKAGE_LOCK=
ALL_TESTS_MUTATIONS=PASS|FAIL
ALL_LIFTS=PASS|FAIL
MEET=PASS|FAIL
REPLAY_ANALYSIS=PASS|FAIL
CAREER_SAVE=PASS|FAIL
VISUAL_AUDIO_UI_ACCESSIBILITY=PASS|FAIL
PERFORMANCE_SOAK=PASS|FAIL
STANDALONE_CLEAN_MACHINE=PASS|FAIL
LICENSE_CLAIM_PRIVACY=PASS|FAIL
KNOWN_LIMITATIONS=
OWNER_ACCEPTED=YES|NO
RELEASE_ARTIFACT_HASH=
NEXT=SHIP|FIX_BLOCKERS
```

## Post-release

M9 begins only after stable V1. Candidate work: additional athletes, sumo independent domain, venues, community
challenges, richer analysis, external validation, platform ports. Research never destabilizes the release branch.
