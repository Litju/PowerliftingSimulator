# ADR-GAM47 — 25 kg squat-depth regression isolation decision

Date: 2026-09-22
Authority: Linear `GAM-47`
Base/expected start: `f32d3581d85a6e886f5ccbd13142b9b417fafa5f`
Architecture: `5x + LC1 + F2 spine + S1`
Load: `25 kg`

## Decision

```text
ROOT_DOMAIN=DEEP_PHASE_EQUILIBRIUM_COMPOSITION
HELD_BOTTOM_RESULT=HELD_BOTTOM_SHALLOW
SUPPORTED_BLOCKER=NOMINAL_REFERENCE_OR_TRACKING_MAPPING
ESTABLISHED_CAUSE=NONE_OF_PREDECLARED_COMPOSITION_ARMS
```

The held bottom is not qualified because support is not retained in the
predeclared settled report, even where the depth landmarks are geometrically
deep. C0, C1, and C2 therefore remain shallow under the frozen qualification
predicate. No production correction or tuning is justified by this issue.

## Dynamic baseline

```text
DYNAMIC_DEEPEST_LEFT_DEPTH=0.0411848724 m
DYNAMIC_DEEPEST_RIGHT_DEPTH=0.0381783843 m
DYNAMIC_DEEPEST_DEPTH=0.0411848724 m
DYNAMIC_DEPTH_DEFICIT=0.04618487 m
DYNAMIC_DEEPEST_TICK=382
DYNAMIC_SQ_AT_DEEPEST=1
P2_START_RESULT=EVALUABLE/NO_LIFT
P2_VIOLATIONS=FAILED_START_POSITION|SUPPORT_VIOLATION|INSUFFICIENT_DEPTH
```

## Held and composition results

The fixed settle window was 200 authoritative ticks; classification used the
final 50 ticks.

| arm | settled worst-side depth | support retained | finite valid control | result |
|---|---:|---:|---:|---|
| HOLD s_q=0.00 | 0.428775221 m | no | yes | HELD_BOTTOM_SHALLOW |
| HOLD s_q=0.25 | 0.397956252 m | no | yes | HELD_BOTTOM_SHALLOW |
| HOLD s_q=0.55 | -0.407490134 m | no | yes | HELD_BOTTOM_SHALLOW |
| HOLD s_q=0.80 | -0.4287507 m | no | yes | HELD_BOTTOM_SHALLOW |
| HOLD s_q=1.00 | -0.4128773 m | no | yes | HELD_BOTTOM_SHALLOW |
| C0 full composition | 0.04129057 m | no | yes | HELD_BOTTOM_SHALLOW |
| C1 no dynamic balance | -0.411717325 m | no | yes | HELD_BOTTOM_SHALLOW |
| C2 nominal only | 0.0160067752 m | no | yes | HELD_BOTTOM_SHALLOW |

C1 is geometrically deep but is not `HELD_BOTTOM_LEGAL` because the unchanged
support-retention condition fails. The predeclared C1 causal classification is
therefore not established. C2 remains shallow, which triggers the specified
nominal-reference/tracking-mapping blocker stop.

## P2 semantic audit

The pre-record qualifier reached `OverallStartCandidate=true` for ticks
27–29, with the required run of three. The actual pre-Squat samples evaluated
by P2 were ticks 30–32; all three failed the bar-motion predicates, and the
foot contact observations were false. Squat was issued at tick 33.

`FAILED_START_POSITION` is **both** candidate-specific and a pre-existing
lifecycle/orchestrator semantic issue: the LC1 candidate loses the motionless
start condition immediately after its qualifier run, while P2 re-evaluates a
new post-record three-sample window rather than judging the qualifier samples
that caused recording to begin. No start rule or tolerance was changed.

## Support audit

```text
SUPPORT_VIOLATION_SOURCE=ACTUAL_SUPPORT_LOSS
ACTUAL_SUPPORT_LOSS_TICK=33
MAX_ACCUMULATED_SLIP_M=0
MAX_SLIP_SPEED_MPS=0
```

The support violation is not accumulated slip and not slip speed in the
canonical baseline. The support proxy is an observation/rules game proxy, not
force-plate biomechanics.

## P3 physical-stage audit

```text
P3_PHYSICAL_DESCENT=true
P3_PHYSICAL_BOTTOM=true
P3_LEGAL_BOTTOM=false
P3_ASCENT_ESTABLISHED=false
P3_PHYSICAL_LOCKOUT=false
P3_TERMINAL_CONTEXT=TRACE_COVERED
P3_MISSING_STAGE=LEGAL_BOTTOM|ASCENT_ESTABLISHED|PHYSICAL_LOCKOUT
```

The exact first missing physical-completion prerequisite is `LEGAL_BOTTOM`;
the absent ascent and lockout stages are downstream consequences in the P3
stage sequence. P3 was evaluated independently from P2.

## Hypotheses

Rejected as the established root domain:

- `DYNAMIC_TRACKING_TIMING`: a phase hold did not qualify a legal supported
  bottom.
- `DYNAMIC_BALANCE_DEPTH_BIAS`: C1 was geometrically deep but failed the
  unchanged support-retention predicate.
- `EQUILIBRIUM_PRELOAD_DEPTH_BIAS`: C2 remained shallow.

Supported blocker:

- `NOMINAL_REFERENCE_OR_TRACKING_MAPPING`: C2 remained shallow, so the
  predeclared stop rule applies.

## Next gate and claim ceiling

```text
NEXT_GATE=OWNER_REVIEW_NOMINAL_REFERENCE_OR_TRACKING_MAPPING_BLOCKER
GAM13_STATE=IN_PROGRESS
GAM14_STATE=BACKLOG
```

Do not tune phase rate, dwell, balance gains, capacity, sticking/failure,
rules, tolerances, LC1, S1, or F2 in GAM-47. The evidence establishes only
engine-runtime observations and bounded game-derived depth/support proxies; it
does not establish biological torque, GRF/COP, or a universal biomechanical
cause.

## Evidence

- `Artifacts/Measurements/GAM-47/dynamic-baseline-trace.csv`
- `Artifacts/Measurements/GAM-47/dynamic-baseline-summary.md`
- `Artifacts/Measurements/GAM-47/p2-start-window-comparison.csv`
- `Artifacts/Measurements/GAM-47/support-violation-source.md`
- `Artifacts/Measurements/GAM-47/p3-physical-stage-report.md`
- `Artifacts/Measurements/GAM-47/held-phase-ladder.csv`
- `Artifacts/Measurements/GAM-47/held-phase-ladder-summary.md`
- `Artifacts/Measurements/GAM-47/composition-audit.csv`
- `Artifacts/Measurements/GAM-47/composition-audit-summary.md`
