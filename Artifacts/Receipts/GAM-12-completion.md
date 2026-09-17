# GAM-12 completion receipt

MISSION
GAM12_SQUAT_RULES_FAILURE_TELEMETRY_AND_ATTEMPT_LIFECYCLE

GAM12_COMPLETION_DECISION
PASS

STATUS
PASS — every GAM-12 acceptance gate passed. The only non-passing outcomes in the
final headless PlayMode run are 8 graphics-device gates and 2 explicit skips,
none of them GAM-12 tests.

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
PASS — 75/75 before the PR-review fixes. XML SHA-256
`4895B97B1D2FA2BD00F0F4696A00B8D07382871B29887B385ADECA78ACD56A4A`.

FULL_EDITMODE
PASS — 206/206 after the PR-review fixes (204 plus two terminal-context guard
tests). XML SHA-256
`2CC21F9836D1FDB367C0DB1EB0928A880A0AF66C6AAAC5E643DC60DFE6A7771E`.

GAM12_PLAYMODE_INTEGRATION
PASS — 4/4 on the committed tree: attempt-lifecycle 25 kg closeout,
physical-failure integration, rule-processor integration, and observation-trace
integration. XML SHA-256
`76D9FF0DD54B3D76DB664CAB03ADC6566E847385E5237D455F171546E441159E`.
The 25 kg lifecycle test performs three internal fresh `LoadSceneMode.Single`
repeats.

HEADLESS_PLAYMODE
184 total; 174 passed; 8 failed; 2 skipped; 0 inconclusive. That is 10 outcomes
that are not passes: 8 failures and 2 skips, accounted for separately below.
XML SHA-256
`6C24CD3EF4A84A433E3C5BC89D964ACE4BEA6262ADFC49518B550424AF1EE191`.
All 8 failures require a graphics device (`RenderTexture.Create failed`, or
explicit "requires a graphics device" assertions). None is a GAM-12 test.
Both skips are explicit and expected:
`PhysicalStandingEquilibriumTests.E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`
(characterization experiment excluded from default qualification suites) and
`PhysicalStandingVisualSmokeTests.V1_UNLOADED_STANDING_VISUAL_SMOKE`
(declines to capture blank frames without a graphics device).
Every GAM-12 EditMode and PlayMode test passed.

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
- `UpperLimbPerformanceTests.UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET` still
  failed on that rerun and passed later once machine contention was removed. See
  `PERFORMANCE_GATE` below.
The sole skip `PhysicalStandingEquilibriumTests.E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM`
is explicitly excluded from default qualification suites.

This graphics run predates the PR-review fixes. The post-fix verification was
the full headless PlayMode run recorded under `HEADLESS_PLAYMODE`, whose only
non-passes are graphics-device gates and the two explicit skips, plus full
EditMode 206/206 and MasterSpec PASS.

PERFORMANCE_GATE
PASS — resolved, and the earlier failure was proven environmental.

`UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET` first failed with physics_step p95
of 2.5183 ms headless, 7.0104 ms in the full graphics run, 3.0144 ms targeted,
and 2.8977 ms fully isolated, against a 2.0 ms budget, while the committed
GAM-11 baseline for the same test is 0.3300 ms.

Measured machine state during those runs: Intel i5-1135G7 clamped to 1382 MHz,
CPU pinned at 100 % load, and 457 MB free of 7585 MB physical memory, with 19
external `codebase-memory-mcp` worker processes holding roughly 91 CPU-hours in
aggregate. Those processes are not part of this repository and were not started
by this work. Unity was measured accumulating 0.3 s of CPU per 20 s of wall
clock, about 1.5 % of one core.

After that contention was removed (free physical memory recovered from 457 MB to
1900 MB), the same test passes with every metric inside budget: physics_step p95
0.7123 ms, catch_up_frame 2.1985 ms, foundation_frame 0.6645 ms,
controller_execution 0.0716 ms.

Corroborating discriminators that GAM-12 was never the cause:
- The degradation is uniform across four independent metrics, including paths
  GAM-12 does not touch: physics_step 0.3300 -> 2.8977 ms, catch_up_frame
  1.0762 -> 18.9549 ms, foundation_frame 0.3348 -> 2.7729 ms,
  controller_execution 0.0395 -> 0.2655 ms.
- `Artifacts/Measurements/GAM-12-phase1a-performance.json` regenerated on the
  same machine shows the trace-recording-**OFF** path — which never executes
  any changed code — degrading from p95 0.6704 ms to 3.1379 ms, while the
  recording ON-minus-OFF delta *shrank* from 0.5999 ms to 0.2818 ms. The
  incremental cost of the observation path did not increase.
- The same binary produced 2.52, 7.01, 3.01, 2.90 and finally 0.71 ms across
  five runs in one session; a deterministic code regression would not vary 10x.
- The only per-tick production addition is one null-checked delegate invocation
  in `SquatObservationCollector.CapturePostPhysics`; the registered
  `SquatAttemptOrchestrator.HandleSnapshot` returns immediately when no attempt
  is armed, and the perf fixture never arms one. No per-tick allocation was
  added.

No performance budget was relaxed. All regenerated prior-issue evidence and
measurement artifacts were restored to their committed values so that no
qualified GAM-6..GAM-11 or GAM-12 P1A baseline is overwritten by a measurement
taken from this run.

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

## PR review resolution

An automated review of `main -> work/gam-12-squat-rules-telemetry` raised eight
findings. Five were resolved in this closeout; three are accepted as real and
deliberately deferred because fixing them would breach a GAM-12 prohibition or
change a sealed layer on speculation rather than on a failing test.

RESOLVED
1. `PhysicsTickDriver.StepOne` committed the Foundation attempt-trace append
   only after the registered post-physics observer ran, and
   `CompleteRenderFrame` skipped its accumulator decrement when a step threw.
   An observer fault could therefore leave a trace gap and retain render time
   for a tick that had already advanced the simulation clock. The append now
   precedes the observer, and a tick whose clock advanced always consumes its
   accumulated render time. The success path is unchanged.
2. `SquatAttemptOrchestrator.HandleStartWindow` accepted the first recorded
   sample as the lockout standing reference without revalidating it. The
   reference is now taken from the first start-window sample that still
   satisfies the raw start predicate.
3. `EARLY_SETUP_HISTORY_CANNOT_CHANGE_LOCKOUT_REFERENCE` compared two identical
   traces and could not fail. It now shifts only the recorded standing height
   and asserts that the reference is read from the trace rather than assumed.
4. `GAM12Phase1AValidationTests` wrote the shared accumulator's verdict into a
   per-pair slot in the sample-count mismatch branch, which mislabelled every
   later pair in the same load as a repeatability failure. The branch now uses
   a dedicated per-pair accumulator and merges it into the aggregate.
5. Receipt accounting and a stale pre-P4 statement in
   `Artifacts/Research/GAM-12-P3-failure-model-map.md` were corrected.

DEFERRED — `SquatRuleProcessor` reports `attemptCommencement` (bilateral knee
unlock) as `EventTicks.DescentOnsetTick`, which the attempt record surfaces as
`PhysicalDescentOnsetTick`. The real 25 kg run shows the two genuinely differ:
P2 reports 150 while the P3 physical-motion predicate finds 172. The label is
therefore imprecise. Changing it alters output of the sealed P2 processor, which
this mission explicitly prohibits, and it changes no rule verdict. Recorded here
as a confirmed provenance defect for a P2 follow-up.

DEFERRED — `SquatObservationCollector.CaptureSupport` and `CaptureJoint` pass
runtime values to strict `AVAILABLE` constructors without validating every
field, so a non-finite or out-of-range producer value would throw inside the
post-physics callback instead of yielding an explicit `NOT_AVAILABLE`. No
observed run produces such a value. The fix belongs with the P1 layer and would
otherwise convert an unobserved producer fault into silent unavailability on
speculation.

DEFERRED — `SquatAttemptOrchestrator` does not restart start qualification when
an already-recorded start-window sample stops qualifying; it still issues Squat.
The sealed rule processor remains the authority on start legality and already
reports `FAILED_START_POSITION` for exactly this condition, so attempt truth is
not corrupted. Restarting qualification mid-recording requires ending and
clearing the trace, a behavioural redesign of the qualification path that this
closeout will not make against a proven 3/3 gate.

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
