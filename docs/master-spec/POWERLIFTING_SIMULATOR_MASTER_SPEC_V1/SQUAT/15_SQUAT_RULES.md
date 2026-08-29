# Squat Competition Rules

**Document ID:** `PSMS-SQ-15`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/11_SQUAT_BIOMECHANICS.md`, `SQUAT/14_SQUAT_CONTROL_MODEL.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Open the current official IPF Technical Rules Book and record page/section locators.
- Compare existing M1 rule tests and migrate only behavior consistent with this versioned rule contract.
- Freeze all game simplifications in ruleset metadata and UI.

## Authority and versioning

V1 targets the **IPF Technical Rules Book, Version 3, effective 1 March 2026**, subject to implementation-time
verification against the official PDF. The game stores `ruleSetId`, source version, effective date, and any declared
simplification. Existing qualified M1 rule behavior is preserved when compatible.

## Inputs

Immutable post-physics `SquatObservationSnapshot`, command timestamps, player action edges, contact state, attempt clock,
and rule tolerance set. Rules never read animation, HUD state, or mutable transforms directly.

## Required command sequence

1. Athlete/bar reaches an erect start position with knees locked.
2. Head referee issues **Squat**.
3. Athlete descends.
4. Athlete returns to an erect position with knees locked.
5. Head referee issues **Rack**.
6. Athlete reracks.

## V1 rule predicates

| Predicate | Implementation |
|---|---|
| start position | bilateral knee lockout proxy, hip/trunk erect bounds, bar controlled, feet established |
| command compliance | descent begins only after Squat timestamp |
| legal depth | top surface of each leg at hip joint lower than top of corresponding knee; conservative bilateral landmark test |
| one descent | no second meaningful downward movement after upward reversal, with rule hysteresis |
| ascent control | no significant whole-bar downward reversal after ascent starts |
| finish | bilateral knees/hips within lockout bounds; bar controlled/still |
| Rack compliance | rerack begins only after Rack timestamp |
| support | no disallowed loss/step/contact event under the selected game rules |

`m_depth`, velocity reversal tolerance, lockout angles, and persistence are named rule proxies, not numerical epsilons.

## Official rule vs game simplification

- The central depth and command principles are represented directly as geometric/time predicates.
- V1 may simplify judge visibility, minor foot movement interpretation, bar displacement caused by oscillation, and
  spotter contact. Every simplification is displayed in the ruleset metadata.
- Safety intervention after a latched physical failure cannot turn a no-lift into a good lift.
- Referee lights are presentation of a frozen judgment; they do not recompute it.

## Judgment model

Each virtual referee receives the same authoritative snapshot stream but evaluates a configured viewing/strictness profile
only for presentation variety. The canonical competition outcome is deterministic. V1 may simply use one deterministic
processor and mirror the result to three lights; later judge-specific interpretation cannot override core geometry without
an explicit ruleset version.

## Pseudocode

```text
EvaluateSquatAttempt(trace):
    violations = []
    if descent_started_before(SQUAT_COMMAND):
        violations += EARLY_DESCENT
    if not trace.bottom.depth_legal_bilateral:
        violations += INSUFFICIENT_DEPTH
    if meaningful_second_descent(trace):
        violations += DOUBLE_DESCENT
    if whole_bar_downward_reversal_after_ascent(trace):
        violations += DOWNWARD_MOVEMENT
    if not valid_bilateral_lockout(trace.finish):
        violations += FAILED_LOCKOUT
    if rerack_started_before(RACK_COMMAND):
        violations += EARLY_RACK
    if disallowed_support_or_contact(trace):
        violations += SUPPORT_VIOLATION

    physical_complete = trace.physical_completion == LOCKOUT_REACHED
    return GOOD_LIFT if physical_complete and violations.empty else NO_LIFT(violations)
```

## Failure precedence

A physical collapse, saddle break, or unrecovered stall latches physical failure. Rule violations remain recorded even if
physical failure follows. Result UI shows the primary reason plus secondary violations.

## Tests

Good lift; shallow both sides; one hip high; early descent; double descent; bar reversal; unlocked knee; early rack;
camera changes; threshold-boundary fixtures; physical completion with rule failure; physical failure with no prior
rule violation; safety intervention after result.

## Source and claim classification

Rulebook text/geometry: `SOURCE_DIRECT` after version verification. Landmark implementation: `RULE_DERIVED_GAME_PROXY`.
Referee behavior beyond deterministic predicates: `GAME_SIMPLIFICATION`.
