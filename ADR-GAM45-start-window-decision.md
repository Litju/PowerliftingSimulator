# ADR-GAM45 — F2+S1 start-window decision

Date: 2026-09-21
Authority: Linear `GAM-45`
Base: `d1b1a9c01463395f4e841beefc30c4685b0d7842`
Final evidence head: `2708b96`

## Decision

```text
START_BLOCKER=START_BAR_MOTION
BALANCE_LIMIT_CYCLE=ESTABLISHED
ESTABLISHED_CAUSE=F2_LOWER_CHAIN_STANDING_COMPENSATION
CORRECTION=NONE
WINNER=NONE
```

F2+S1 does not qualify the unchanged three-sample start predicate at 25 kg.
The failing mechanism is a persistent closed-loop bar-motion cycle caused by
the F2 ankle/knee/hip standing compensation interacting with the 5x plant and
S1. P1 qualifies; P2 does not; disabling only the F2 lower-chain standing
compensation restores qualification and the tick-33 lifecycle boundary.

## Evidence classification

### Observed

P2 produces 2200 authoritative diagnostics, longest valid run 2, no command,
and ends in `START_WINDOW` / `SETUP`. Bar linear, vertical, and angular
threshold margins repeatedly become negative. Knee, hip, trunk, support, foot
availability, and support presence remain valid.

### Supported

P2 has 301 vertical bar-velocity zero crossings and signed angular zero
crossings of 153/144/277 on X/Y/Z. Raw ankle demand reaches
`-3.67582965..2.908341 rad`; the applied target remains bounded at
`-0.2618..0.2618 rad`. COP and capture channels oscillate while the 2-D capture
margin stays positive.

### Established cause

The predeclared single-property isolation arm removes only F2 ankle/knee/hip
standing compensation. It preserves F2 spine, 5x, S1, balance controller,
solver, capacity, rules, and tolerances, then qualifies with longest run 3 and
the same tick-33 Squat chronology as P1. This is causal isolation, not a
production promotion.

## Rejected hypotheses

* `START_POSTURE_KNEE`: rejected; all knee pass predicates remain true.
* `START_POSTURE_HIP`: rejected; all hip pass predicates remain true.
* `START_POSTURE_TRUNK`: rejected; abdomen/thorax pass predicates remain true.
* `START_SUPPORT`: rejected; support is available/present and foot channels are
  available.
* `START_OBSERVATION_AVAILABILITY`: rejected; required observations are
  available and finite.
* `START_MULTI_FACTOR`: rejected; the failing predicate group is bar motion.
* harness/observation sequencing bug: rejected; the same production boundary
  qualifies under the isolated lower-chain arm, and P0/P1 complete from fresh
  processes.
* tolerance or rule defect: rejected as an action; no rule or tolerance was
  changed, and P2 physically violates the unchanged motionless-bar contract.

## Correction and qualification boundary

No production correction was implemented. The lower-chain-disabled arm is a
diagnostic intervention and changes the F2 candidate; it is not a justified
GAM-13 production winner without an owner decision on the F2 control law.
Therefore full standing requalification and three-repeat qualification of a
corrected production candidate are `NOT_RUN`. GAM-44's prior F2+S1 standing
qualification remains separate evidence and does not establish lifecycle
eligibility.

The default lifecycle integration test passed three deterministic 25 kg
repeats: physical lockout, `P2=EVALUABLE/NO_LIFT`,
`P3=NO_PHYSICAL_FAILURE`, and trace-covered terminal semantics. The default P0
and diagnostic P1/isolation arms also produced physical lockout and complete
terminal records. P2 produced no lifecycle record, so its depth, lockout, P3,
and terminal semantics remain `NOT_OBSERVED`.

## Validation and state

* GAM45 fresh-process P0/P1/P2/isolation probes: PASS.
* Unity compilation: PASS.
* MasterSpec verification: PASS.
* `git diff --check`: PASS.
* focused lifecycle validation remains the final gate before handoff.

```text
GAM45_STATE=COMPLETE_EVIDENCE_NO_FIX
GAM13_STATE=IN_PROGRESS
GAM14_STATE=BACKLOG
NEXT_GATE=OWNER_REVIEW_F2_CONTROL_LAW_BEFORE_ANY_PRODUCTION_CORRECTION
```
