# M5 Meet and Broadcast Plan

**Document ID:** `PSMS-RM-74`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/72_M4_BENCH_IMPLEMENTATION_PLAN.md`, `ROADMAP/73_M4_DEADLIFT_IMPLEMENTATION_PLAN.md`, `GAME/40_MEET_SYSTEM.md`, `PRESENTATION/52_CAMERA_AND_BROADCAST.md`

## Repository verification

- M3/M4A/M4B receipts accepted.
- Current attempt/tie/score rules verified.
- Existing M1/M2 systems mapped before replacement.

## Mission

Combine the three accepted lift domains into a coherent single-player meet with declarations, attempts, commands,
judgments, totals/placing, opponents, cameras, UI, audio, lights, and results—without modifying the lift physics to fit
the meet.

## Wave M5.0 — Integration inventory

Map existing M1 meet logic and M2 presentation. Identify domain seams, duplicate state, and current ruleset/scoring data.
Freeze migration.

## Wave M5.1 — Meet domain

- athlete entry/class;
- attempt declaration/load validation;
- rounds/order/clock;
- lift scene orchestration;
- immutable attempt judgment;
- best lifts/total/placing;
- seeded opponents;
- save checkpoint.

Pure EditMode tests first.

## Wave M5.2 — Referee coordinator

- state-ready requests from each lift;
- exact command events;
- command voice/subtitles;
- deterministic canonical judgment;
- three lights;
- rule/failure reason presentation;
- no physical writes.

## Wave M5.3 — Scene flow/equipment

- transition among squat/bench/deadlift setups;
- rack/bench/platform configuration;
- plate loading;
- warmup/loading screen;
- no stale bodies/contacts/input;
- attempt reset and crash-safe checkpoint.

## Wave M5.4 — Broadcast presentation

- venue establish/setup/gameplay/critical/result/reaction;
- scoreboard/attempt clock;
- crowd/audio/music;
- record/placing moments;
- lift-specific framing preserved;
- accessibility.

## Wave M5.5 — Full meet scenarios

- all good;
- mixed no-lifts;
- bomb-out/no total;
- tie/order fixtures;
- record attempt;
- technical fault recovery;
- save/reload between rounds;
- repeated meets.

## Wave M5.6 — Hardening

Performance with championship venue, standalone smoke, visual capture, scoring/rules verification, licenses, receipt.

## Acceptance

A full nine-attempt meet can be completed from menu to result/save in a standalone build; each attempt trace/result remains
owned by its lift; commands/lights/scoreboard are synchronized; no stale state; totals/placing correct; broadcast and
performance pass.

```text
MISSION=M5_MEET_BROADCAST
STATUS=PASS|FAIL
ALL_THREE_LIFTS_UNCHANGED=YES|NO
NINE_ATTEMPT_FLOW=PASS|FAIL
COMMANDS_JUDGMENT=PASS|FAIL
TOTAL_PLACING=PASS|FAIL
SAVE_RECOVERY=PASS|FAIL
BROADCAST_VISUAL_AUDIO=PASS|FAIL
PERFORMANCE_STANDALONE=PASS|FAIL
OWNER_ACCEPTED=YES|NO
NEXT=M6_ONLY_IF_ACCEPTED
```
