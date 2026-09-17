# GAM-12 Phase 3A.1 terminal failed-lockout semantic repair receipt

MISSION
GAM12_PHASE3A1_TERMINAL_FAILED_LOCKOUT_SEMANTIC_REPAIR

STATUS
PASS

START_HEAD
3096bacd1992cd16b6e06c92eaf6b6993a7935c1

## Architecture-change record

OBSERVATION
Two successive timer formulations of `FAILED_LOCKOUT` both produced a physical
failure on an accepted real 25 kg attempt that physically completes.

EXPECTED
A 25 kg attempt that reaches a valid physical lockout yields
`NO_PHYSICAL_FAILURE`.

ACTUAL
- Pre-P3A policy (bounded dwell measured from ascent establishment) latched
  `FAILED_LOCKOUT` at tick 576.
- P3A policy (bounded dwell measured from completion-region entry) would latch
  `FAILED_LOCKOUT` at tick 806.
- The same attempt achieves a valid physical lockout at tick 855.

MINIMAL_REPRODUCTION
One fresh real 25 kg `SquatPhysicalPrototype` lifecycle at 100 Hz
(`LoadSceneMode.Single`, physical athlete, physical bar, `FoundationRuntime`),
frozen canonical trace ticks 118-859, 742 samples.

MEASURED_EVIDENCE
`Artifacts/Evidence/GAM-12/GAM-12-P3A1-25kg-lockout-diagnostic.csv`
(SHA-256 `6F497EAEA70B6748A1B50E38F0CACB8C6A72823EF896A2653E918DD56CA3A7E3`),
one row per frozen sample with bar height/velocity/angular velocity, standing
reference, height deficit, bilateral knee/hip angles, trunk angle, and every
lockout sub-gate.

| Event | Tick | Measured state |
|---|---:|---|
| standing reference acquired | 118 | bar Y 1.403096 m, bar vY 0.009522 m/s |
| physical descent onset | 172 | |
| physical bottom | 483 | bar Y 0.849880 m |
| ascent established | 517 | |
| completion-region entry | 747 | height deficit 0.048604 m; max knee 0.760829 rad; max hip 0.642550 rad; trunk 0.232955 rad |
| trunk erectness qualified | 763 | |
| hip erectness qualified | 813 | |
| bilateral knee lock qualified | 831 | |
| physical lockout achieved | 855 | bar linear stillness was the final gate to qualify |

LOCKOUT_DIAGNOSTIC_CAUSE
The bar's *height* enters the 0.05 m standing-reference completion band 108
ticks (1.08 s) before lockout is physically achievable, because the final
centimetres of bar rise correspond to the last ~40 deg of knee extension at low
velocity. At both false-latch points every unmet lockout gate — bilateral knee
extension, hip extension, trunk erectness, and finally bar linear stillness —
was still converging monotonically toward lockout. Nothing irreversible had
occurred. Elapsed time inside the completion region is therefore not
irreversible evidence of a failed lockout.

AFFECTED_SPEC_CONTRACT
`SQUAT/16_SQUAT_FAILURE_MODEL.md` illustrative pseudocode line
`if physical_lockout_timeout(obs): candidates += FAILED_LOCKOUT`.

PROPOSED_AMENDMENT
No master-spec edit. The frozen document already names
`noisy one-tick false positive` and `Detector uses phase not physics` as
failure modes of this detector and requires that failure is never scripted. The
implementation-level refinement is that `physical_lockout_timeout` is realised
as a terminal postcondition rather than an elapsed-time latch, which is the
only formulation consistent with the measured evidence. The named calibration
is retained as provenance and explicitly marked as a non-selector.

## Final semantics

OLD_FAILED_LOCKOUT_POLICY
Bounded dwell from ascent establishment (pre-P3A), then bounded dwell from
completion-region entry (P3A). Both were elapsed-time latches inside sequential
streaming selection.

NEW_FAILED_LOCKOUT_POLICY
```text
if physical lockout was achieved at any tick:
    no FAILED_LOCKOUT
else if the attempt terminates
        AND the completion region had been credibly entered
        AND physical lockout was never achieved:
    FAILED_LOCKOUT
else:
    no FAILED_LOCKOUT yet
```
Sequential processing tracks ascent establishment, physical lockout, first
credible completion-region entry, and the observed completion-region dwell. It
never latches `FAILED_LOCKOUT`. The class is decided exactly once, at
finalization, from authoritative lifecycle terminality.

ONSET_POLICY
`OnsetTick` = first credible completion-region entry.

LATCH_POLICY
`LatchedTick` = authoritative terminal tick.

COMPLETION_REGION_DEFINITION
Unchanged: ascent established AND standing reference available AND
`barY >= standingReferenceY - LockoutHeightToleranceM`. It means only that the
athlete has reached the vicinity in which a final lockout is physically
possible. `LockoutHeightToleranceM` was not tuned.

PHYSICAL_LOCKOUT_DEFINITION
Unchanged: completion-region height, bilateral knee lock, hip erectness, trunk
erectness, bar linear control, bar angular control. No tolerance was relaxed.

TERMINAL_CONTEXT_TYPE
`PowerliftingSimulator.Squat.SquatFailureCompletionContext` — pure C# immutable
value: `IsTerminal`, `TerminalTick`, `TerminalTimeSeconds`, `TerminalReason`,
`IsWellFormed`. No Unity type, no MonoBehaviour, no controller state, no
physical write.

TERMINAL_CONTEXT_VALIDATION
`SquatFailureTerminalContextStatus` records `NOT_PROVIDED`, `NOT_TERMINAL`,
`TRACE_COVERED`, `REJECTED_MALFORMED`, `REJECTED_UNCOVERED_TICK`, or
`REJECTED_TIME_MISMATCH`. The terminal tick must be covered by the frozen
trace and its time must map to that canonical sample within
`FoundationTolerances.SimulationTimeMapping`. Only `TRACE_COVERED` permits the
postcondition; a rejected context can only suppress it and can never create a
failure.

P3_API
`Evaluate(SquatTrace)` — unchanged pure non-terminal physical interpretation.
`Evaluate(SquatTrace, SquatFailureCompletionContext)` — terminal finalization
seam. One added public method; no new type hierarchy.

TERMINALITY_DOES_NOT_CHOOSE_A_FAILURE
`TerminalReason` is lifecycle evidence only. A timeout below the completion
region does not become `FAILED_LOCKOUT`; `MID_ASCENT_STALL` and `BAR_REVERSAL`
retain their own physical evidence; an attempt with no defensible physical
cause stays `INCOMPLETE_ATTEMPT` / `UNDETERMINED`.

PRECEDENCE
Unchanged. `GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1`: the terminal
`FAILED_LOCKOUT` onset is late in the attempt, so any earlier irreversible
streaming cause remains primary and `FAILED_LOCKOUT` is recorded as secondary.

OLD_60_TICK_PARAMETER
`lockout_completion_dwell` = 60 ticks is retained as calibration provenance and
marked `NOT_USED_BY_CANONICAL_FAILED_LOCKOUT_SELECTION` /
`REQUIRES_GAM13_CALIBRATION`. It is no longer emitted as a selecting threshold
on the failure event; the observed dwell is emitted as
`completion_region_dwell_observed` measurement only. Mutating it to 1 or to
5000 does not change the P3A.1 classification, onset, or latch.

## Versions

FAILURE_MODEL_VERSION
GAM12_P3A1_FAILURE_MODEL_V1

FAILURE_CALIBRATION_VERSION
GAM12_P3A1_FAILURE_CALIBRATION_PROVISIONAL_V1

FAILURE_PRECEDENCE_VERSION
GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1 (unchanged)

P1_SCHEMA_CHANGED
NO

P2_SEMANTICS_CHANGED
NO

P3_ONTOLOGY_CHANGED
NO — the seven named failure kinds are unchanged; only the terminal evidence
semantics of `FAILED_LOCKOUT` were repaired and versioned.

PRODUCTION_PHYSICS_CHANGED
NO

HEAVY_LOAD_CALIBRATION
NO

## Gates

FOCUSED_P3A1_AND_P4_EDITMODE
PASS — 75/75.

25KG_COMPLETE_LIFECYCLE
PASS — 3/3 fresh `LoadSceneMode.Single` attempts, P3 `NO_PHYSICAL_FAILURE`.

DESCENT_COLLAPSE_PROVENANCE
Retained from the P3A repair: selecting provenance reports only the actual
selectors; foot-contact and modeled-demand channels/thresholds are not claimed.

NEXT
GAM12 completion receipt `Artifacts/Receipts/GAM-12-completion.md`.
