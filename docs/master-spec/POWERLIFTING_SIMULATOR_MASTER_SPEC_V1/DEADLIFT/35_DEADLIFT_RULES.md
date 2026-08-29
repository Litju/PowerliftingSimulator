# Deadlift Competition Rules

**Document ID:** `PSMS-DL-35`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/31_DEADLIFT_BIOMECHANICS.md`, `DEADLIFT/34_DEADLIFT_CONTROL_MODEL.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Open the current official IPF Technical Rules Book and record page/section locators.
- Compare existing M1 rule tests and migrate only behavior consistent with this versioned rule contract.
- Freeze all game simplifications in ruleset metadata and UI.

## Authority and versioning

V1 targets the **IPF Technical Rules Book, Version 3, effective 1 March 2026**, verified at implementation time.

## Inputs

Immutable `DeadliftObservationSnapshot`, bar path/velocity, knee/hip/trunk and front-deltoid proxies, grip state,
ground/floor-break modes, Down timestamp, and return/ground contact.

## Command sequence

There is **no start command**. The lifter begins when ready. At a legal, motionless lockout, the head referee issues
**Down**. The bar must then be returned under hand control.

## V1 rule predicates

| Predicate | Implementation |
|---|---|
| start freedom | pull may start after attempt is live and grip/setup are established |
| upward attempt | physical bar rises from floor; no scripted event |
| no downward movement | no significant whole-bar downward reversal before legal lockout |
| lockout | both knees locked, athlete erect under hip/trunk bounds, shoulders/front-deltoid geometry satisfies current rule proxy |
| control | bar becomes motionless enough for Down |
| Down compliance | intentional descent/drop begins only after Down |
| return control | hands remain coupled/in contact until stable plate contact; no uncontrolled dropping |
| support/contact | no disallowed support or grip/contact violation |

A stall with zero/near-zero velocity is not automatically a rule violation; it may become physical failure. A clear
downward reversal before lockout is both a physical/control event and a rule no-lift.

## Simplifications

- Erectness and shoulder completion use calibrated joint/landmark proxies.
- V1 uses a canonical conventional stance and symmetric grip for physical stability.
- Hitching/support interpretation is represented by bar path, contact, and phase-specific leg/body contact rules only
  when robustly observable; ambiguous visual judgments are not invented.
- Bar oscillation tolerance is separated from purposeful downward movement.
- Sumo is not hidden inside this ruleset; it requires its own future domain and tests.

## Pseudocode

```text
EvaluateDeadliftAttempt(trace):
    violations = []
    if meaningful_downward_reversal_before_lockout(trace):
        violations += DOWNWARD_MOVEMENT
    if disallowed_bar_support_or_contact(trace):
        violations += SUPPORT_OR_HITCH_PROXY
    if not bilateral_knee_lockout(trace.finish):
        violations += UNLOCKED_KNEES
    if not legal_erect_shoulder_geometry(trace.finish):
        violations += FAILED_LOCKOUT
    if descent_or_release_before(DOWN_COMMAND):
        violations += EARLY_DOWN_OR_DROP
    if not returned_under_hand_control(trace.return):
        violations += UNCONTROLLED_RETURN

    physical_complete = trace.physical_completion == LEGAL_LOCKOUT_OBSERVED
    return GOOD_LIFT if physical_complete and violations.empty else NO_LIFT(violations)
```

## Tests

Good lift; cannot break floor; stall but recover; downward reversal; unlocked knee; incomplete hips; shoulder geometry
invalid; early drop; early descent before Down; hands release early; controlled return; boundary/tolerance; camera
invariance; physical success plus rule failure.

## Classification

Rule language: `SOURCE_DIRECT` after verification. Joint/shoulder/contact implementation:
`RULE_DERIVED_GAME_PROXY`. True judge visibility, hitch biomechanics, and internal loading are not claimed.
