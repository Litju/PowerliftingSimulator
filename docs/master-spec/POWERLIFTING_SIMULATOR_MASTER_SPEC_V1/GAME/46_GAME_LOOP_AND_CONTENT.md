# Game Loop and Content Plan

**Document ID:** `PSMS-GAME-46`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `GAME/40_MEET_SYSTEM.md`, `GAME/41_GAMEPLAY_AND_DIFFICULTY.md`, `GAME/42_ATHLETE_PROGRESSION.md`, `GAME/43_SPORTS_SCIENCE_ANALYSIS.md`, `GAME/44_REPLAY_SYSTEM.md`, `GAME/45_SAVE_AND_CAREER.md`

## Repository verification

- Inventory existing M1/M2 scenes and presentation content.
- Playtest onboarding/session-length targets after M4B.
- Verify every third-party content license and platform release requirement.

## North-star loop

```text
Plan
  -> Choose athlete/session/meet
  -> Select setup and load
  -> Perform visible physical lift
  -> Receive rule judgment
  -> Review replay and analysis
  -> Progress athlete/career
  -> Unlock challenge/content
  -> Plan again
```

The physical lift is the dramatic center; menus and analysis serve it.

## Session types

### Technique session

Short deterministic drills: commands, depth, touch/pause, slack/floor break, balance/path. Low consequence and strong
feedback.

### Strength session

Player selects sets/loads across one or more lifts. Attempt fatigue/readiness and progression are game-calibrated.
The core physics remains identical.

### Mock meet

Nine attempts, strict sequence, immediate results, no career stakes unless chosen.

### Career meet

Full broadcast/presentation, opponents, records, progression/resources, venue advancement.

### Challenge mode

Frozen athlete/profile/load/calibration/input-assist constraints for comparable records:

- deepest legal heavy squat;
- paused bench precision;
- deadlift floor-break timing;
- total under fixed athlete profile;
- no-assist competition challenge.

## Content matrix

| Content | V1 | Later |
|---|---|---|
| athlete | canonical male superhero asset with configurable game profile | more body types/visible characters after physical fitting |
| lifts | squat, bench, conventional deadlift | sumo as separate domain, variants/noncompetition training |
| venues | training gym, local meet, championship arena | additional venues |
| equipment | IPF-style bar/plates/rack/bench/platform | brands/styles/certified variations |
| modes | tutorial, practice, mock meet, career, replay | local multiplayer, community challenges |
| presentation | broadcast cameras, HUD, audio, lights | commentators, richer crowds |
| analysis | V1 metrics/provenance | trends/comparisons |

## Onboarding

The first hour is playable before deep analysis:

1. Learn Brace/Yield/Drive on an unloaded squat.
2. Receive a Squat command and hit depth.
3. Learn bench Start/touch/pause/Press.
4. Learn deadlift Grip/Slack/Drive/Down.
5. Complete a three-lift novice event.
6. Unlock analysis and career planning.

Advanced numerical overlays are opt-in.

## Content authoring contracts

A content asset cannot change architecture. Every new athlete/venue/equipment/technique profile declares:

- source/license/provenance;
- compatible physical rig version;
- dimensions/mass/contact data;
- compatible lift domain(s);
- calibration version;
- visual/audio dependencies;
- deterministic tests;
- performance budget.

A technique profile is data within one lift only if it preserves that lift's topology/state/rule contract. Sumo fails that
test and therefore remains a separate future domain.

## Economy and rewards

V1 rewards performance and mastery with fictional reputation, venue access, equipment cosmetics, analysis tools, and
athlete progression. No loot boxes or random stat purchases. Cosmetic equipment cannot alter physical dimensions/mass
unless explicitly a separate qualified equipment profile.

## Failure loop

Failure is content:

- immediate physical/rule reason;
- decisive replay moment;
- one or two trace-backed suggestions;
- rapid retry in practice;
- strategic consequence in meet/career.

Do not punish a player for a technical fault; surface it as a diagnostic and preserve save integrity.

## Session length targets

- tutorial drill: 2–5 min;
- practice lift: 3–10 min;
- mock meet: 20–35 min;
- career meet: 25–45 min;
- analysis/replay: optional.

These are product targets to validate in playtests, not hard timers.

## Completion definition

The whole game is not complete when one squat works. Release requires:

- all three independently qualified lift domains;
- meet/broadcast flow;
- replay/analysis;
- career/save/progression;
- visual/audio/UI quality;
- performance/build/CI;
- licensing and claim provenance;
- onboarding and enough content for a coherent paid/free release.

## Scope

**SHIP_V1:** complete loop above with focused content.  
**LATER:** new characters/venues/domains/community modes.  
**RESEARCH:** physics-enhanced experimental mode.  
**OUT_OF_SCOPE:** endless content before the core three-lift game ships.
