# Bench Press Competition Rules

**Document ID:** `PSMS-BP-25`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/21_BENCH_BIOMECHANICS.md`, `BENCH/24_BENCH_CONTROL_MODEL.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Open the current official IPF Technical Rules Book and record page/section locators.
- Compare existing M1 rule tests and migrate only behavior consistent with this versioned rule contract.
- Freeze all game simplifications in ruleset metadata and UI.

## Authority and versioning

V1 targets the **IPF Technical Rules Book, Version 3, effective 1 March 2026**. This version includes the current
competition bottom-position criterion based on the underside of both elbow joints relative to the top of the respective
shoulder joint. Luna must verify the official PDF/page language before freezing constants.

## Inputs

Immutable `BenchObservationSnapshot`, Start/Press/Rack timestamps, physical contacts, bar/chest contact and stillness,
bilateral elbow/shoulder proxies, bar path, grip state, and attempt clock.

## Command sequence

1. Athlete establishes legal setup and receives bar at arm's length.
2. Head referee issues **Start**.
3. Athlete lowers bar to chest/abdominal area.
4. Bar is motionless and bottom criteria are satisfied.
5. Head referee issues **Press**.
6. Athlete presses to bilateral arm lockout.
7. Head referee issues **Rack**.
8. Athlete reracks.

## V1 rule predicates

| Predicate | Implementation |
|---|---|
| setup contact | head/shoulder region and buttocks on bench under game proxy; both feet satisfy configured floor rule |
| start | both elbows locked, bar controlled, required contacts valid |
| descent command | no descent before Start |
| touch | shaft/contact volume contacts valid chest/abdominal target; not belt/neck/rack |
| elbow depth | underside proxy of each elbow at or below top proxy of corresponding shoulder at valid touch |
| pause | direct bar velocity/angular velocity within stillness bounds for referee dwell |
| press command | upward press cannot begin before Press |
| press | no significant whole-bar downward movement after upward motion starts |
| lockout | both elbows locked simultaneously; bar controlled/still |
| rack | rerack begins only after Rack |
| retained contacts | any selected head/butt/foot contact rules remain valid through the required interval |

## Simplifications

- The deforming human surface is represented by calibrated volumes/landmarks.
- V1 defines unambiguous foot/head/butt contact persistence instead of simulating nuanced judge visibility.
- Minor unequal arm extension is allowed only within a symmetric lockout tolerance; a complete one-arm lag fails.
- Grip width and thumb/hand style may be simplified to the canonical grip profile.
- Safety catch behavior is post-judgment.

## Pseudocode

```text
EvaluateBenchAttempt(trace):
    violations = []
    if descent_before(START_COMMAND): violations += EARLY_DESCENT
    if required_setup_contacts_lost(trace): violations += CONTACT_VIOLATION
    if not valid_chest_or_abdomen_touch(trace.bottom): violations += INVALID_TOUCH
    if not elbows_below_or_level_shoulders_bilateral(trace.bottom):
        violations += INSUFFICIENT_ELBOW_DEPTH
    if press_started_before(PRESS_COMMAND): violations += EARLY_PRESS
    if whole_bar_downward_reversal(trace.press): violations += DOWNWARD_MOVEMENT
    if not bilateral_elbow_lockout(trace.finish): violations += FAILED_LOCKOUT
    if rerack_before(RACK_COMMAND): violations += EARLY_RACK

    physical_complete = trace.physical_completion == BILATERAL_LOCKOUT
    return GOOD_LIFT if physical_complete and violations.empty else NO_LIFT(violations)
```

## Event ordering

Touch, stillness, elbow-depth, and Press authorization are separate. A touch does not automatically cause a Press command.
The Press command does not move the bar. Lockout must be observed before Rack.

## Tests

Valid attempt; early descent; invalid touch; only one elbow reaches depth; moving bar; early press; downward movement;
one-arm lockout; butt/foot/head contact loss under configured rules; early rack; physical bar failure; safety catch; exact
boundary fixtures.

## Classification

Rule language: `SOURCE_DIRECT` after verification. Contact/landmark/velocity predicates:
`RULE_DERIVED_GAME_PROXY`. Detailed shoulder anatomy: not claimed.
