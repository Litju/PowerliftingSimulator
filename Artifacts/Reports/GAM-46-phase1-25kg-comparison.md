# GAM-46 Phase 1 — LC1 25 kg comparison

Date: 2026-09-21
Authority: Linear `GAM-46`
Candidate: `LC1`
Load: `25 kg`
Architecture: `5x + LC1 + S1`

## Start-window result

The fresh-process GAM-45 start probe qualified LC1:

| Metric | GAM-45 P2 | GAM-46 LC1 |
|---|---:|---:|
| longest valid run | 2 | 3 |
| required run | 3 | 3 |
| probe ticks | 2200 | 749 |
| Squat command tick | unavailable | 33 |
| lifecycle state | `START_WINDOW` | `COMPLETE` |
| terminal reason | not finalized | `PHYSICAL_LOCKOUT` |
| bar vertical zero crossings | 301 | 0 |
| bar angular X/Y/Z zero crossings | 153 / 144 / 277 | 1 / 2 / 1 |
| raw ankle demand range (rad) | -3.67582965..2.908341 | -3.61543155..1.86014032 |
| applied ankle demand range (rad) | -0.2618..0.2618 | -0.199999988..0.23999998 |
| minimum 2-D capture margin (m) | 0.08284136 | 0.09336543 |
| maximum COM speed (m/s) | 0.07106796 | 0.0434336476 |

LC1 therefore materially removes the GAM-45 low-load start oscillation and
passes the start chronology gate. The full per-tick source evidence is
`Artifacts/Measurements/GAM-45/start-window-LC1.csv` and the summaries are
`start-window-LC1-summary.csv` and the committed GAM-45 P2 summary.

## Lifecycle result

The canonical GAM-13 lifecycle harness ran one fresh `25 kg` LC1+S1 attempt.
The Unity test runner completed, but the candidate lifecycle gate failed:

- terminal reason: `PHYSICAL_LOCKOUT`;
- trace count: `720`;
- Squat command tick: `33`;
- legal physical depth: `false`;
- minimum worst-side depth: `0.0411848724 m`;
- P2: `NO_LIFT`, violations `FAILED_START_POSITION|SUPPORT_VIOLATION|INSUFFICIENT_DEPTH`;
- P3 evidence: `INCOMPLETE_ATTEMPT`;
- P3 outcome: `UNDETERMINED`, not `NO_PHYSICAL_FAILURE`;
- harness `physical_success`: `false`.

The setup-only standing sample in the same run passed its standing gates, but
that does not satisfy the lifecycle gate. Full transition, full standing,
canonical repeats, lifecycle x3, guard semantics, and heavy-load regression
were not run after the Phase 1 stop.

Source: `Artifacts/Measurements/GAM-46/phase1/integrated-25kg-lifecycle.csv`.

## Decision

`WINNER=NONE`. LC1 passes the low-load start-window restoration but fails the
required canonical 25 kg lifecycle semantics. Per the frozen stop rule, no
transition or follow-on candidate work is authorized.
