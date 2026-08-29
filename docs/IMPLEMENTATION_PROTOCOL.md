# Implementation Protocol

## Work-unit contract

Each issue or wave must state:

```text
MISSION
BASE_SHA
SPEC_ARTIFACTS
SCOPE
NON_GOALS
INVARIANTS
IMPLEMENTATION
TESTS
EVIDENCE
FINAL_SHA
STATUS
```

## Bug loop

```text
OBSERVATION
→ CLASSIFICATION
→ HYPOTHESIS
→ DISCRIMINATING TEST
→ RESULT
→ REPAIR
→ REGRESSION
```

## No speculative stacking

Do not apply several speculative fixes before rerunning the discriminating test. One hypothesis, one measured result, then the smallest repair.

## Evidence classes

- `SOURCE_DIRECT` — value or rule copied from a named source with a locator.
- `SOURCE_DERIVED` — deterministic calculation from direct source values.
- `ENGINEERING_DERIVED` — quantity computed from engine state under an explicit model.
- `GAME_CALIBRATION` — deliberate playability or numerical-behaviour parameter.
- `PROVISIONAL` — proposed value awaiting an implementation fixture.
- `NOT_OBSERVABLE` — quantity the game does not have evidence to claim.

## Status semantics

- `PASS` — all required acceptance work executed and passed.
- `PASS_WITH_LIMITATIONS` — required work passed with explicit, non-blocking limits recorded.
- `BLOCKED` — a required input, authority, or acceptance gate is unavailable; evidence and options are recorded.
- `FAIL` — executed acceptance work did not pass.

Never report `PASS` if required acceptance work was not executed.

## Git discipline

- `main` remains releasable.
- Feature work uses bounded issue branches/PRs once Wave 0 establishes the workflow.
- Never force-push except for explicit repository-history maintenance with owner authorization.
- Never destructively reset or discard unknown user work.
- Keep commits coherent, reviewable, and traceable to the Linear issue.

## GAM-1 stop boundary

After repository constitution acceptance, stop. GAM-2 owns package freezing, assemblies, coordinates, units, and runtime order. GAM-1 adds no runtime systems or speculative scaffolding.
