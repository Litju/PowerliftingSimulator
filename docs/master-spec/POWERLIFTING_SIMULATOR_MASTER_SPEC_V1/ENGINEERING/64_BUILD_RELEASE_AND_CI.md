# Build, Release, and CI

**Document ID:** `PSMS-ENG-64`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ENGINEERING/62_TESTING_AND_TERMINUS.md`, `ENGINEERING/63_PERFORMANCE_BUDGET.md`, `ENGINEERING/65_CLAIMS_PROVENANCE.md`, `GAME/45_SAVE_AND_CAREER.md`

## Repository verification

- Inspect current repository workflows/build scripts/hooks and Unity license runner.
- Freeze Mono vs IL2CPP and supported Windows versions through testing.
- Verify storefront legal, accessibility, privacy, and packaging requirements before release.

## Purpose

Produce repeatable tested Windows builds with frozen Unity/package/configuration state, preserved evidence, license/claim
audit, and no editor-only dependency.

## Source/repository contract

- one canonical repository/worktree during release work;
- clean base and clean qualified tree;
- text serialization/meta files committed;
- packages lock committed;
- project settings changes reviewed;
- no generated cache/build payload committed;
- secrets absent;
- third-party license inventory committed;
- release tag points to qualified commit.

## CI stages

```text
Checkout
 -> environment/version verification
 -> dependency/package restore
 -> static architecture checks
 -> EditMode tests
 -> PlayMode tests
 -> deterministic scenario tests
 -> Windows standalone build
 -> standalone smoke/Player.log scan
 -> save/replay round trip
 -> artifact/evidence packaging
 -> claim/license audit
 -> receipt/sign-off
```

Expensive visual and performance qualification may run on a self-hosted GPU runner, but local and remote receipts must
identify exact tree equivalence.

## Unity invocation

Use the exact installed Unity editor version in batch mode, with explicit project path, test platform/results/log path,
and no interactive dialogs. Build scripts return nonzero on test/build/log/invariant failure. Avoid parsing only the last
console line; preserve XML/JSON logs.

## Static architecture checks

Repository scripts scan for:

- `AddForce/AddTorque/MovePosition/MoveRotation` outside approved adapters;
- `Physics.Simulate` outside tick driver;
- Animator/transform writes on physical hierarchy;
- kinematic pelvis/bar or forbidden foot locks;
- `maximumForce = infinity`;
- ConfigurableJoint projection in valid profiles;
- direct Input System reads in lift controllers;
- currentTorque in success/utilization/claims;
- generic shared lift mechanics;
- rules reading render/Animator state;
- replay resimulation.

Allow-lists are narrow and code-reviewed.

## Standalone smoke

Automated or scripted:

1. launch build;
2. reach menu;
3. load a deterministic scenario scene;
4. execute/complete or fail expected attempt;
5. open replay;
6. save and reload;
7. return/exit cleanly;
8. scan Player.log for exceptions/errors/assertions/physics faults;
9. verify output receipt/hash.

Headless physics tests do not replace a graphics/input standalone smoke.

## Build settings

- target Windows x64;
- IL2CPP or Mono decision frozen after compatibility/performance testing;
- URP quality profiles;
- exact scene list;
- development/debug symbols separated;
- deterministic scripting define set;
- no editor scene/config mutation during build;
- product/company/version identifiers;
- crash/log path and privacy policy.

## Versioning

Semantic product version plus content/schema/ruleset/calibration versions. Replays/saves display compatibility. Rulebook
or scoring updates can ship as data revisions only with tests and release notes.

## Release checklist

- all milestone receipts accepted;
- final clean qualification;
- license/attribution;
- store metadata/screenshots;
- claim/disclaimer copy;
- controls/accessibility;
- save migration/backup;
- crash/log behavior;
- performance minimum specs;
- known limitations;
- checksum/signature;
- smoke on clean machine;
- rollback build retained.

## Artifact bundle

- player build;
- symbols where appropriate;
- test results;
- performance reports;
- visual evidence index;
- package/project version report;
- license/attribution;
- source/claim register;
- manifest/checksums;
- release receipt.

## Failure policy

Any test, build, smoke, log exception, missing artifact, dirty tree, package drift, unsupported claim, or license gap fails
the release. Rerunning until green without root-cause evidence is prohibited.

## Tests

CI negative tests; exact Unity mismatch; package drift; scene-list mutation; standalone crash; Player.log error; missing
license; source claim violation; save/replay incompatibility; checksum; clean-machine install/launch; rollback.

## Scope

**SHIP_V1:** Windows CI/build/release contract above.  
**LATER:** macOS/Linux/console/store automation.  
**RESEARCH:** cloud simulation farms.  
**OUT_OF_SCOPE:** claiming platform support not qualified.
