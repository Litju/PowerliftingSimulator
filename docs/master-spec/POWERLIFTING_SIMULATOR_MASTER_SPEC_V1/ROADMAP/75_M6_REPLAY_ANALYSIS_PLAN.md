# M6 Replay and Analysis Plan

**Document ID:** `PSMS-RM-75`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/74_M5_MEET_BROADCAST_PLAN.md`, `GAME/43_SPORTS_SCIENCE_ANALYSIS.md`, `GAME/44_REPLAY_SYSTEM.md`, `ENGINEERING/65_CLAIMS_PROVENANCE.md`

## Repository verification

- Stable traces from all three accepted lift builds required.
- Exact book/rules/web source locators completed.
- Owner reviews analysis language and replay fidelity.

## Mission

Deliver recorded-state replay and trustworthy lift-specific analysis for every attempt, with clear definitions,
filtering, provenance, quality, comparison, and claim limits.

## Wave M6.0 — Schema freeze

Inventory traces and metric/UI code. Freeze common snapshot header/channel catalog plus independent squat/bench/deadlift
payloads. Establish migration from M3–M5 traces if needed.

## Wave M6.1 — Replay core

- immutable replay container/checksum;
- load/decode/seek/rates/frame-step;
- visible athlete/bar state reconstruction;
- event markers;
- no physics resim;
- schema rejection/migration.

## Wave M6.2 — Lift processors

Implement and golden-test:

- squat depth/COM/support/bar/phase/sticking/joint/demand;
- bench touch/pause/path/tilt/grip/joint/sticking;
- deadlift floor-break/path/proximity/zones/grip/lockout/return;
- raw/display/post-analysis stream separation.

## Wave M6.3 — Provenance/claim enforcement

- claim registry and typed source class;
- unit/frame/filter/quality;
- missing/invalid states;
- forbidden-claim tests;
- exact source locators;
- no currentTorque/muscle/GRF/COP/internal-force leak.

## Wave M6.4 — Replay/analysis UX

- summary and decisive replay;
- scrub/slow/frame step;
- path/landmark/reference overlays;
- charts with event markers;
- definitions/limitations;
- compatible prior-attempt comparison;
- export.

## Wave M6.5 — Evidence/performance

Golden traces, corrupted/missing/migrated replays, long trace, random seek, screenshot/video, UI accessibility, storage/
performance, standalone save/reload.

## Acceptance

Every completed attempt opens an exact recorded-state replay and a lift-specific analysis whose values can be traced to
raw channels/algorithms/source classes. No analysis value affects historical judgment.

```text
MISSION=M6_REPLAY_ANALYSIS
STATUS=PASS|FAIL
RECORDED_STATE_NOT_RESIM=YES|NO
SQUAT_PROCESSOR=PASS|FAIL
BENCH_PROCESSOR=PASS|FAIL
DEADLIFT_PROCESSOR=PASS|FAIL
PROVENANCE_CLAIM_AUDIT=PASS|FAIL
REPLAY_UX=PASS|FAIL
SCHEMA_SAVE_COMPAT=PASS|FAIL
PERFORMANCE_VISUAL=PASS|FAIL
OWNER_ACCEPTED=YES|NO
NEXT=M7_ONLY_IF_ACCEPTED
```
