# M7 Product Game Loop Plan

**Document ID:** `PSMS-RM-76`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `ROADMAP/75_M6_REPLAY_ANALYSIS_PLAN.md`, `GAME/42_ATHLETE_PROGRESSION.md`, `GAME/45_SAVE_AND_CAREER.md`, `GAME/46_GAME_LOOP_AND_CONTENT.md`

## Repository verification

- M5/M6 accepted.
- Freeze V1 content scope and target audience/storefront.
- Human playtest required; scripted tests alone cannot close.

## Mission

Turn the qualified simulation/meet/replay systems into a complete game: onboarding, practice/training, career, athlete
progression, records, opponents, saves, content progression, settings, and replayable reasons to continue.

## Wave M7.0 — Product inventory

Map current menus/scenes/settings/save code, content/licenses, and UX gaps. Freeze V1 content list and cut anything that
does not serve the core loop.

## Wave M7.1 — Save/profile foundation

- atomic save/backup/migration;
- profile/athlete/career DTOs;
- replay index;
- settings;
- crash transaction semantics;
- fault injection.

## Wave M7.2 — Onboarding/tutorial

- controls and commands;
- squat depth;
- bench touch/pause;
- deadlift grip/slack/floor break;
- heavy grind/failure;
- novice three-lift event;
- accessibility profiles;
- mastery tests.

## Wave M7.3 — Training/session loop

- session planning and load selection;
- attempts/sets;
- session receipts;
- analysis/retry;
- readiness/fatigue game state;
- records.

## Wave M7.4 — Progression/career

- seven attributes and specialization;
- deterministic progression ledger/idempotence;
- venues/meet tiers;
- seeded rivals;
- reputation/resources/cosmetic unlocks;
- season/goal structure;
- no medical/prescriptive claims.

## Wave M7.5 — UX/content polish

- complete core screen flow;
- controller navigation;
- error/recovery;
- settings/accessibility;
- credits/licenses/limitations;
- enough fictional venues/opponents/goals for coherent V1.

## Wave M7.6 — Full-loop qualification

Cold boot → tutorial/profile → training → meet → replay/analysis → progression/save → reload → next event.
Run long career simulation, save migration, duplicate receipt, missing replay, corrupted save recovery, and human playtest.

## Acceptance

A player unfamiliar with the project can install, learn controls, complete attempts/meet, understand outcomes, review and
progress, save/reload, and continue without developer intervention.

```text
MISSION=M7_PRODUCT_GAME_LOOP
STATUS=PASS|FAIL
ONBOARDING=PASS|FAIL
TRAINING_LOOP=PASS|FAIL
CAREER_PROGRESSION=PASS|FAIL
SAVE_MIGRATION_RECOVERY=PASS|FAIL
ACCESSIBILITY_UX=PASS|FAIL
CONTENT_COMPLETE=YES|NO
HUMAN_PLAYTEST=PASS|FAIL
OWNER_ACCEPTED=YES|NO
NEXT=M8_ONLY_IF_ACCEPTED
```
