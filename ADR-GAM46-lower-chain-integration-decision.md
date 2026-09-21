# ADR-GAM46 — lower-chain integration decision

Date: 2026-09-21
Authority: Linear `GAM-46`
Base head: `4a308cf3e590580d4eb6e3155aaab70bbb99d859`
Candidate: `LC1` only

## Decision

```text
WINNER=NONE
STOP_PHASE=PHASE1_25KG_LIFECYCLE
```

LC1 materially removes the GAM-45 P2 low-load start oscillation and qualifies
the unchanged three-sample start window, but its canonical 25 kg lifecycle
does not satisfy the required legal-depth/P2/P3 semantics. The frozen stop
rule therefore ends GAM-46 before the transition region.

## Candidate tested

Let `L` be physical bar load in kg:

```text
u = clamp((L - 170) / 60, 0, 1)
alpha(L) = u*u*(3 - 2*u)
```

Only the existing F2 ankle, knee, and hip contribution was multiplied by
`alpha(L)`. The existing F2 phase fade and abdomen/thorax spine law were
unchanged. No plant, saddle, balance gain, capacity, rule, tolerance, or
direct force/torque/velocity/transform path changed.

## Evidence

### Start window

Fresh `25 kg` LC1 start evidence qualified with longest valid run `3/3`,
Squat command tick `33`, lifecycle `COMPLETE`, and terminal
`PHYSICAL_LOCKOUT`. Compared with GAM-45 P2, bar vertical zero-crossings
changed `301 → 0`; angular X/Y/Z zero-crossings changed
`153/144/277 → 1/2/1`. Raw ankle demand was
`-3.61543155..1.86014032 rad`; applied demand was
`-0.199999988..0.23999998 rad`. Minimum capture margin was `0.09336543 m`
and maximum COM speed was `0.0434336476 m/s`.

This establishes start-window restoration and materially reduced oscillation,
not lifecycle qualification by itself.

### Canonical lifecycle

The fresh `25 kg` `5x + LC1 + S1` lifecycle finalized a physical lockout but
failed the required semantic gates:

```text
terminal_reason=PHYSICAL_LOCKOUT
trace_count=720
squat_command_tick=33
legal_physical_depth=false
min_worst_side_depth_m=0.0411848724
p2_outcome=NO_LIFT
p2_violations=FAILED_START_POSITION|SUPPORT_VIOLATION|INSUFFICIENT_DEPTH
p3_evidence=INCOMPLETE_ATTEMPT
p3_outcome=UNDETERMINED
physical_success=false
```

Required `P3=NO_PHYSICAL_FAILURE` and legal depth were not observed. The
setup-only standing row passed, but standing alone cannot promote the
candidate.

## Stop boundary

Not run after the Phase 1 lifecycle failure:

- transition loads `155/170/180/190/200/215/230 kg`;
- full standing set and canonical standing repeats;
- lifecycle x3;
- guard-semantics diagnostic at 170 kg;
- heavy-load regression comparison against GAM-44;
- full EditMode/default PlayMode promotion validation.

No LC2, sweep, anchor movement, rule/tolerance change, capacity change,
saddle change, plant change, balance-gain change, sticking/failure tuning,
GAM-14 start, or GAM-13 merge was performed.

## Claim ceiling

LC1 is deterministic game-control calibration using physical load as a
scheduling variable, not a biological recruitment model. The 170/230 kg
anchors are project-evidence boundaries. Zero-crossing reductions describe
observed oscillatory behavior and do not alone prove a mathematical stable
limit cycle.

```text
GAM45_STATE=DONE
GAM46_STATE=STOPPED_PHASE1_LIFECYCLE_FAIL
GAM13_STATE=IN_PROGRESS
GAM14_STATE=BACKLOG
NEXT_GATE=OWNER_REVIEW_LC1_LIFECYCLE_FAILURE
```
