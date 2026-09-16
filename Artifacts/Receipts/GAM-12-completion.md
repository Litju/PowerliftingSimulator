# GAM-12 completion receipt

MISSION
GAM12_SQUAT_RULES_FAILURE_TELEMETRY_AND_ATTEMPT_LIFECYCLE

GAM12_COMPLETION_DECISION
PASS

STATUS
PASS_WITH_LIMITATIONS — every GAM-12 acceptance gate passed. One pre-existing
GAM-11 performance gate fails for a measured machine-state reason recorded
under `PERFORMANCE_GATE`; no budget was relaxed and it is not claimed as
passing.

START_HEAD
3096bacd1992cd16b6e06c92eaf6b6993a7935c1

BASE
origin/main @ e28c61c3e5bffca47a5b6a2f90f511c733586122

## Phase history

| Phase | Scope | Head |
|---|---|---|
| P1 | canonical immutable post-physics observation + frozen trace | d38ecd005f8fa26563284d6962a508ae2f24ae7f |
| P1A | observation/trace validation hardening | 6c3ed347830346405618325da3d92b4f2c406337 |
| P2 | deterministic IPF-derived rule processor | 5181b757d1d4010cf8ef9f47d6f5352d2f5ea09e |
| P2A | rule semantic boundary repair | d3898de9a43518046041ede6dc2659fbc788fdec |
| P2A.1 | rule start/timeline trust boundary seal | db190b8108a526a00cf9e447adbbb28943dcfd31 |
| P3 | deterministic physical-failure ontology and precedence | 3096bacd1992cd16b6e06c92eaf6b6993a7935c1 |
| P3A / P3A.1 | failed-lockout semantic repair to a terminal postcondition | this closeout |
| P4 | authoritative attempt lifecycle and immutable attempt record | this closeout |

## Schemas and versions

OBSERVATION_SCHEMA
GAM12_SQUAT_OBSERVATION_SNAPSHOT_V1

TRACE_SCHEMA
GAM12_SQUAT_TRACE_V1

RULESET_ID
IPF_2026_V3_SQUAT_GAME_V1

RULE_IMPLEMENTATION_VERSION
GAM12_P2A1_RULE_PROCESSOR_V1

RULE_TOLERANCE_VERSION
GAM12_P2_RULE_TOLERANCES_V1

FAILURE_MODEL_VERSION
GAM12_P3A1_FAILURE_MODEL_V1

FAILURE_CALIBRATION_VERSION
GAM12_P3A1_FAILURE_CALIBRATION_PROVISIONAL_V1

FAILURE_PRECEDENCE_VERSION
GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1

ATTEMPT_LIFECYCLE_VERSION
GAM12_P4_ATTEMPT_LIFECYCLE_V1

ATTEMPT_RECORD_VERSION
GAM12_P4_ATTEMPT_RECORD_V1

## Truth pipeline

TRUTH_PIPELINE_ORDER
```text
simulation
-> post-physics observation snapshot
-> canonical trace append
-> authoritative attempt terminal event
-> trace freeze (EndRecording)
-> P2 Evaluate(frozen trace, command timeline)
-> P3 Evaluate(same frozen trace, terminal context)
-> immutable SquatAttemptRecord
-> truth frozen (trace sealed)
-> safety/presentation eligibility
```

SAME_TRACE_FOR_P2_AND_P3
Enforced. `SquatAttemptLifecycle.FinalizeAttempt` passes one frozen
`SquatTrace` instance to both processors and then seals it; the PlayMode gate
asserts `record.Trace` is the same reference as the collector trace.

TRACE_FREEZE_POLICY
`SquatObservationCollector.EndRecording()` is required before either processor
runs. Finalization permanently seals the non-empty trace against append, clear,
or a second recording (`SquatTrace.IsTruthSealed`).

SAFETY_HANDOFF_POLICY
No safety handoff is eligible before the attempt record is frozen. The handoff
is value-only and cannot edit evidence, judgment, failure, or physics. No
safety write occurs before truth freeze.

## Final failed-lockout semantic

FAILED_LOCKOUT_FINAL_SEMANTIC
Terminal postcondition. Physical lockout achieved at any tick forbids the
class. Otherwise `FAILED_LOCKOUT` requires authoritative attempt termination
plus credible prior entry into the physical completion region with lockout
never achieved. Onset is the first credible completion-region entry; latch is
the authoritative terminal tick. Sequential streaming selection never latches
it from elapsed time.

LOCKOUT_DIAGNOSTIC_CAUSE
Measured on one fresh real 25 kg attempt: the bar height enters the 0.05 m
standing-reference completion band at tick 747, while trunk erectness qualifies
at 763, hip erectness at 813, bilateral knee lock at 831, and bar linear
stillness — the final gate — at 855. Every unmet gate converges monotonically
across those 108 ticks (1.08 s), so nothing irreversible had occurred. Both
timer formulations (dwell from ascent establishment, latch 576; dwell from
completion-region entry, latch 806) were false positives.
Evidence: `Artifacts/Evidence/GAM-12/GAM-12-P3A1-25kg-lockout-diagnostic.csv`.

OLD_60_TICK_SELECTOR_ACTIVE
NO. `lockout_completion_dwell` = 60 ticks is retained only as calibration
provenance, marked `NOT_USED_BY_CANONICAL_FAILED_LOCKOUT_SELECTION` and
`REQUIRES_GAM13_CALIBRATION`, and is no longer emitted as a selecting threshold.
Mutating it to 1 or 5000 does not change the classification, onset, or latch.

DESCENT_COLLAPSE_PROVENANCE_POLICY
Selecting provenance is limited to raw downward motion plus support bounds,
hard trunk, or critical joint-limit evidence. Foot-contact and modeled-demand
channels/thresholds are not reported as selectors.

LOCKOUT_REFERENCE_POLICY
The canonical trace begins only after a persisted raw start candidate; its
first sample is the authoritative P3 standing-reference sample. Earlier
setup/walkout history is excluded from the attempt trace.

## 25 kg qualification

25KG_RUNS
3 fresh `LoadSceneMode.Single` attempts on the real `SquatPhysicalPrototype`
scene (physical athlete, physical bar, `FoundationRuntime`, 100 Hz).

25KG_EVENT_TICKS (identical in all three runs)
```text
START_WINDOW=118-120  SQUAT=121  DESCENT=150  BOTTOM=497  ASCENT=516
LOCKOUT=855  RACK=858  RERACK=859  TRACE_FREEZE=859  TRACE_COUNT=742
TERMINAL=PHYSICAL_LOCKOUT@855  TERMINAL_CONTEXT=TRACE_COVERED
```

25KG_RULE_RESULTS
3/3 `EVALUABLE` / `NO_LIFT`; violations `FAILED_START_POSITION`,
`SUPPORT_VIOLATION`. This is the sealed P2 judgment of the current physical
start pose and walkout; P2 semantics were not reopened to make the result
nicer.

25KG_PHYSICAL_RESULTS
3/3 `EVALUABLE` / `NO_PHYSICAL_FAILURE`; primary `NONE`; failure onset and
latch `NOT_AVAILABLE`. No physical failure is fabricated and none is hidden.

25KG_REPEATABILITY
3/3 identical terminal reason, rule outcome, primary rule violation, physical
failure outcome, primary failure kind, command count, and trace sample count.
Semantic event ordering is coherent in every run.

0KG_CLAIM_CEILING
The canonical physical bar is unavailable at 0 kg; no bar samples or complete
competition/failure claim are fabricated. P2/P3 may remain
`INSUFFICIENT_EVIDENCE`. No P1 redesign was performed.

## Gates

FOCUSED_P3A1_AND_P4_EDITMODE
PASS — 75/75. XML SHA-256
`4895B97B1D2FA2BD00F0F4696A00B8D07382871B29887B385ADECA78ACD56A4A`.

FULL_EDITMODE
PASS — 204/204. XML SHA-256
`6097D2365228C0A9CF194AF182B18EABF5D779339B85E37E18DCCEFB07D4A2E3`.

GAM12_PLAYMODE_INTEGRATION
PASS — 4/4 on the committed tree: attempt-lifecycle 25 kg closeout,
physical-failure integration, rule-processor integration, and observation-trace
integration. XML SHA-256
`76D9FF0DD54B3D76DB664CAB03ADC6566E847385E5237D455F171546E441159E`.
The 25 kg lifecycle test performs three internal fresh `LoadSceneMode.Single`
repeats.

HEADLESS_PLAYMODE
184 total; 173 passed; 9 failed; 2 skipped. XML SHA-256
`73FDA7E5A08225FD460E78FD265450BF8E97BEC24EA1D01070416777A01EAE5D`.
All nine non-passes are pre-existing environment gates, not GAM-12
regressions: eight require a graphics device (`RenderTexture.Create failed`
or explicit "requires a graphics device" assertions) and one is the known
`UpperLimbPerformanceTests.UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET`
physics-step p95 variance (2.5183 ms vs the 2.0 ms budget). Every GAM-12
EditMode and PlayMode test passed.

GRAPHICS_PLAYMODE
PASS_WITH_LIMITATIONS — full graphics run: 184 total; 179 passed; 4 failed;
1 skipped. Targeted rerun XML SHA-256
`DCA80A509FDC0DA15713A32CE8CB2A4394A8A7161B1AF1B2F5DEB7F86FB88C7B`.
Full-run XML SHA-256
`ABA5EAF35E60AB0F4EA1AE3536CCACAB172AEBCA19874BB24C1B4B219F3D2AF4`.
A targeted graphics rerun of all four on a quieter machine returned 10/11
passed:
- `GAM12Phase1AValidationTests.GAM12_P1A_SAME_MACHINE_COMPLETE_TRACE_REPEATABILITY_0KG_AND_25KG`
  PASSED on rerun. The full-run failure was the known Unity AI Assistant relay
  log (`connection.state_change ... Process exited unexpectedly`), not a test
  assertion.
- `SpineStandingAndFactorialTests.B3_POST_H16_VISUAL_GATE` PASSED on rerun. The
  full-run failure was a transient `System.IO.IOException: Win32 IO returned
  1224` (`ERROR_USER_MAPPED_FILE`) writing a GAM-11 evidence PNG.
- `UpperLimbProxyCharacterizationTests.T4_P0_BASELINE_CONTACT_GEOMETRY` PASSED
  on rerun, same transient Win32 1224 cause.
- `UpperLimbPerformanceTests.UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET`
  still FAILS. See `PERFORMANCE_GATE` below.
The sole skip `PhysicalStandingEquilibriumTests.E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`
is explicitly excluded from default qualification suites.

PERFORMANCE_GATE
FAIL — pre-existing GAM-11 gate, attributed to machine state, not to GAM-12.
`UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET` measured physics_step p95
2.5183 ms headless, 7.0104 ms in the full graphics run, 3.0144 ms targeted, and
2.8977 ms fully isolated, against a 2.0 ms budget. The committed GAM-11
baseline for the same test is 0.3300 ms.

Measured machine state during these runs: Intel i5-1135G7 clamped to
1382 MHz of its 2400 MHz base (no turbo headroom), CPU at 100 % load, and
457 MB free of 7585 MB physical memory, with six external
`codebase-memory-mcp` processes each holding roughly 26 000-29 000 s of
accumulated CPU time. Those processes are not part of this repository and were
not started by this work.

Discriminating evidence that GAM-12 is not the cause:
- The degradation is uniform across four independent metrics, including paths
  GAM-12 does not touch: physics_step 0.3300 -> 2.8977 ms, catch_up_frame
  1.0762 -> 18.9549 ms, foundation_frame 0.3348 -> 2.7729 ms,
  controller_execution 0.0395 -> 0.2655 ms.
- `Artifacts/Measurements/GAM-12-phase1a-performance.json` regenerated on the
  same machine shows the trace-recording-**OFF** path — which never executes
  any changed code — degrading from p95 0.6704 ms to 3.1379 ms, while the
  recording ON-minus-OFF delta *shrank* from 0.5999 ms to 0.2818 ms. The
  incremental cost of the observation path did not increase.
- The same binary produced 2.52, 7.01, 3.01 and 2.90 ms across four runs in one
  session; a deterministic code regression would not vary by 2.8x.
- The only per-tick production addition is one null-checked delegate invocation
  in `SquatObservationCollector.CapturePostPhysics`; the registered
  `SquatAttemptOrchestrator.HandleSnapshot` returns immediately when no attempt
  is armed, and the perf fixture never arms one. No per-tick allocation was
  added.

No performance budget was relaxed, and this gate is not claimed as passing.
All regenerated prior-issue evidence and measurement artifacts were restored to
their committed values so that no qualified GAM-6..GAM-11 or GAM-12 P1A
baseline is overwritten with a throttled-machine measurement.

MASTER_SPEC
PASS — `Tools/Spec/Verify-MasterSpec.ps1`: `MASTER_SPEC_FILES=68`,
`HASHES=PASS`, `DEPENDENCIES=PASS`, `STATUS=PASS`.

CI_STATUS
NO_GITHUB_CI_STATUS_AVAILABLE — the repository has no hosted workflow; local
repository-approved Unity 6000.3.22f1 evidence is the acceptance gate.

## Invariants held

PRODUCTION_PHYSICS_CHANGED
NO — no joint spring/damping/maxForce, balance gain, capacity/activation,
mass/inertia/friction/solver, squat phase rate, or reference movement family
was modified.

P1_SCHEMA_CHANGED
NO — `GAM12_SQUAT_OBSERVATION_SNAPSHOT_V1` and `GAM12_SQUAT_TRACE_V1` are
unchanged. `SquatTrace` gained only an attempt-truth seal.

P2_SEMANTICS_CHANGED
NO — the sealed IPF-derived rule processor, tolerances, and versions are
untouched.

P3_ONTOLOGY_CHANGED
NO — the seven named failure kinds and the first-irreversible precedence are
unchanged. Only the terminal evidence semantics of `FAILED_LOCKOUT` were
repaired and versioned.

HEAVY_LOAD_CALIBRATION
NO — no load sweep, no threshold retuning, no scripted load-threshold failure.

LOAD_THRESHOLD_SCRIPT
NO — load metadata mutation to 500 kg does not change any classification.

## Known limitations

- Same-machine Unity 6000.3.22f1 evidence is not a cross-platform PhysX
  determinism claim.
- 0 kg remains bar-insufficient; no complete competition classification is
  fabricated without a physical bar.
- Support remains the bounded P1 contact/slip game proxy, not exact
  referee-visible step/rocking classification.
- P3 behaviour-dependent numeric boundaries (reversal timeout/recovery, stall
  velocity/demand/no-progress/dwell, bar-reversal bounds, descent-collapse
  bounds, and the retained lockout completion dwell) remain provisional and
  require GAM-13 load-response calibration.
- The 25 kg attempt is an honest rule `NO_LIFT` (`FAILED_START_POSITION`,
  `SUPPORT_VIOLATION`). Bringing the physical start pose and walkout inside the
  sealed P2 start predicate is not GAM-12 scope.
- GC-allocation per tick remains an unresolved measurement from P1A; static
  review found no new managed allocation in the collector hot path, but this is
  not a zero-byte claim.
- Rack mechanics are not implemented: `Rack`/`Rerack` are explicit value events
  that mark the qualification boundary and leave the physical bar untouched.
- No injury, clinical, GRF, COP, or measured-physiology claim is made. Failure
  type is a game/engine classification.

NEXT
GAM-13 owns squat load-response, sticking-region, and failure calibration.
Canonical future probes: 20, 60, 140, 170, 300 kg. No scripted load-threshold
failure.
