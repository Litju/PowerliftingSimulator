# UI and UX

**Document ID:** `PSMS-PRES-53`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `08_INPUT_AND_PLAYER_INTENT.md`, `GAME/40_MEET_SYSTEM.md`, `GAME/43_SPORTS_SCIENCE_ANALYSIS.md`, `PRESENTATION/52_CAMERA_AND_BROADCAST.md`

## Repository verification

- Inventory existing M2 HUD/screens/components and preserve working presentation architecture.
- Run keyboard/gamepad navigation and accessibility review.
- Verify target resolution/aspect ratio/storefront requirements.

## UX promise

A new player understands what to do now; an experienced player sees the athlete and bar, not a dashboard; a technical
player can inspect definitions, traces, and provenance after the attempt.

## Information hierarchy

### During setup

1. lift/load/attempt;
2. setup action and command readiness;
3. control prompt;
4. attempt clock;
5. optional assistance cues.

### During active lift

1. visible athlete/bar;
2. current/next referee command;
3. minimal intent cue;
4. optional accessibility feedback.

No live spreadsheet of angles, torque, or velocity by default.

### After attempt

1. good/no-lift and reason;
2. physical versus rule result;
3. decisive replay;
4. 3–5 most useful metrics;
5. deeper analysis tabs;
6. retry/next attempt.

## Core screens

- boot/legal/accessibility;
- main menu/profile;
- career hub;
- training/session planning;
- meet registration/attempt declaration;
- platform gameplay HUD;
- judgment/result;
- replay/analysis;
- progression/session summary;
- settings/controls/graphics/audio;
- credits/licenses/claim limitations;
- debug/engineering UI excluded from release navigation.

## HUD states by lift

**Squat:** `SETUP`, `UNRACK`, `WALKOUT`, “Wait for SQUAT,” Yield/Drive, “Hold lockout,” “Wait for RACK.”  
**Bench:** setup/grip, unrack, “Wait for START,” lower, touch/hold, “Wait for PRESS,” drive, “Wait for RACK.”  
**Deadlift:** grip/brace/slack, “Pull when ready,” Drive, “Hold lockout,” “Wait for DOWN,” controlled return.

Prompts come from state/guard records, not ad hoc timers.

## Result language

Examples:

- `GOOD LIFT`
- `NO LIFT — INSUFFICIENT DEPTH`
- `PHYSICAL FAILURE — MID-ASCENT STALL`
- `NO LIFT — EARLY PRESS`
- `TECHNICAL FAULT — BAR INITIALIZATION OVERLAP`

Secondary reasons are expandable. Avoid blame and medical language.

## Analysis UX

Every metric has:

- name;
- value/unit;
- plain-language definition;
- source class;
- quality;
- algorithm/filter version;
- limitation;
- replay jump link.

Charts share time/phase event markers. Raw/display/analysis streams are visually distinguished. Missing is `— / Not
available`, never zero.

## Accessibility

- full keyboard/gamepad remapping later; V1 at least alternate presets;
- hold/toggle options for Brace/Grip;
- text size and UI scale;
- high contrast and colorblind-safe referee lights with shape/text;
- subtitles/visual command cues;
- reduced camera shake/motion blur;
- no audio-only information;
- pause options appropriate to mode;
- input timing assistance declared in record metadata.

## Visual language

Use a restrained broadcast/scientific visual system: high-contrast typography, large load/result numbers, neutral panels,
one accent per state, three referee-light forms with labels, clean plots, SI/imperial display toggle. Do not imitate a
medical device.

## Error states

- incompatible save/trace;
- missing controller/device;
- technical physics initialization fault;
- build/version mismatch;
- no disk space/save failure;
- unsupported graphics setting.

Each gives recovery, preserves data, and references a diagnostic code without exposing stack traces to normal users.

## Tests

State-to-prompt table; command sync; no prompt from presentation timer; navigation/controller focus; localization overflow;
16:9/ultrawide; text scaling; color-independent lights; metric definitions/provenance; missing values; save/error recovery;
screenshots; no debug claims in release.

## Scope

**SHIP_V1:** all core screens/HUD/result/replay/analysis/settings/accessibility baseline.  
**LATER:** full remapping/localization/community UI.  
**RESEARCH:** adaptive information density.  
**OUT_OF_SCOPE:** clinical dashboard aesthetics or opaque gamified metrics.
