# GAM-12 Phase 1A Validation Hardening Receipt

MISSION
GAM12_PHASE1A_VALIDATION_HARDENING_AND_RECEIPT_CORRECTION

BASE_SHA
d38ecd005f8fa26563284d6962a508ae2f24ae7f

SPEC_ARTIFACTS
Attached GAM12 Phase 1A mission; `docs/REPOSITORY_CONSTITUTION.md`; `docs/IMPLEMENTATION_PROTOCOL.md`; frozen `SQUAT/15_SQUAT_RULES.md`, `16_SQUAT_FAILURE_MODEL.md`, `17_SQUAT_TELEMETRY.md`, and `18_SQUAT_TEST_SPEC.md`.

SCOPE
Correct P1 provenance/claim wording; qualify complete same-machine traces; qualify trace-append perturbation and warmed timing; verify current COM invariant; run required regression gates; authorize Phase 2.

NON_GOALS
No architecture rewrite, controller/physics tuning, reference-motion change, squat rules, failure classifier, or heavy-load calibration.

INVARIANTS
One post-physics sampling point; fixed 100 Hz acquisition; immutable bounded trace; explicit availability; no camera/screen truth; no P2/P3 judgment; no GAM-13 load science.

IMPLEMENTATION
Added only the bounded P1A PlayMode validation fixture and corrected P1/P1A receipt semantics; no production runtime code changed.

TESTS
P1 pure contracts, P1 integration, P1A complete-trace repeatability, P1A full-trajectory OFF/ON perturbation/timing, COM invariant, focused GAM-11, full EditMode, and one graphics-enabled full PlayMode run.

EVIDENCE
`Artifacts/Measurements/GAM-12-phase1a-repeatability.json`; `Artifacts/Measurements/GAM-12-phase1a-performance.json`; fresh Unity XML/logs retained in ignored `Temp/`.

FINAL_SHA
EXTERNAL_RECORD_AFTER_COMMIT — deliberately not embedded self-referentially.

START_HEAD
d38ecd005f8fa26563284d6962a508ae2f24ae7f

CHECKPOINT
checkpoint/gam12-pre-p1a-validation-hardening at d38ecd005f8fa26563284d6962a508ae2f24ae7f; pushed before modification.

STATUS
PASS_WITH_LIMITATIONS

P1_HEAD
d38ecd005f8fa26563284d6962a508ae2f24ae7f

ACTUATION_LAW_CHANGED=NO
PHYSICS_PARAMETERS_CHANGED=NO
REFERENCE_MOTION_CHANGED=NO
OBSERVER_TIMING_CHANGED=YES
RESET_CONTACT_STATE_CHANGED=YES

TRACE_STORAGE_DETERMINISTIC=YES
FIXED_STEP_ACQUISITION=YES
SAME_MACHINE_ATTEMPT_REPEATABILITY=PASS — qualified on the current Windows/Unity/PhysX machine using the production scene-reload reset boundary.
CROSS_PLATFORM_DETERMINISM=NOT_CLAIMED

REPEATS_0KG=3
REPEATS_25KG=3
TRACE_COMPARISON_METHOD=Complete canonical snapshot fields; exact metadata/enums/availability/flags/counts/schema/ticks; declared unit-specific tolerances for numeric channels; quaternion canonical shortest-arc comparison. OFF/ON perturbation compares all 17 registered bodies at every 800-tick sample.
TRACE_TOLERANCE_BASIS=Position/length/depth/support=0.0001 m existing anchor tolerance; linear/angular velocity=0.0001 in declared units from GAM-9 repeatability; dimensionless and joint angles=0.00001 from FoundationTolerances.UnitConversionRoundTrip; mass/force/impulse=0.0001 in declared units from existing load/drive physics tests; orientation=0.01 deg from GAM-9 repeatability; time=FoundationTolerances.SimulationTimeMapping.
TRACE_REPEATABILITY_RESULT_0KG=EXACT_REPEATABLE — 800 samples per attempt; tick-aligned; no divergence; final-state dispersion zero.
TRACE_REPEATABILITY_RESULT_25KG=EXACT_REPEATABLE — 800 samples per attempt; tick-aligned; no divergence; final-state dispersion zero.

GC_ALLOC_MEASUREMENT_ENVIRONMENT=PlayMode fallback; the existing standalone ProfilerRecorder harness qualifies the GAM-9 physical-athlete scene, not the production squat collector, and PlayMode frame counters cannot isolate one fixed tick honestly.
GC_BYTES_PER_PHYSICS_TICK=NOT_RELIABLY_ISOLATED
GC_ALLOC_STATUS=UNRESOLVED

TIMING_MEASUREMENT_ENVIRONMENT=Unity 6000.3.22f1 PlayMode with graphics disabled; 100 warm-up ticks then 300 Stopwatch samples per recording mode and load around FoundationRuntime.AdvanceRenderFrame(0.01). Total fixed physics/observation path, not collector-only.
TIMING_MEDIAN_MS=0.9046 — maximum recording-ON median in the final headless run across 0/25 kg; recording-ON medians 0.6364/0.9046 ms.
TIMING_P95_MS=1.9496 — maximum recording-ON p95 in the final headless run across 0/25 kg; recording-ON p95 1.2703/1.9496 ms.
TIMING_P99_MS=2.4327 — maximum recording-ON p99 in the final headless run across 0/25 kg; recording-ON p99 1.9088/2.4327 ms.
TIMING_OFF_ON_P95_MS=0.6704/1.2703 ms at 0 kg and 0.8755/1.9496 ms at 25 kg; ON-minus-OFF deltas +0.5999/+1.0741 ms in this run. Earlier repeated runs varied in sign, so no material causal regression is established.
PERFORMANCE_REGRESSION=NO_EVIDENCE — OFF/ON body trajectories were exact at both loads and recording-ON p95 remained within the existing 2.0 ms one-tick budget; the prior approximately 2.1657 ms result was not reproduced as a P1-linked regression. Editor-fallback timing remains limited evidence.

GRAPHICS_PLAYMODE=PASS_WITH_LIMITATIONS — required full graphics run total=181; passed=179; failed=1; skipped=1; inconclusive=0. The one failure was pre-existing `UpperLimbPerformanceTests.UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET` at 3.0552 ms p95; targeted graphics rerun passed 1/1 at 0.5080 ms p95. Sole skip `PhysicalStandingEquilibriumTests.E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM` remains Explicit.

CONTACT_CLAIM=ENGINE/ENGINEERING_ESTIMATE; ENGINE_CONTACT_POINT != FORCE_PLATE_COP
GRF_CLAIM=ENGINE/ENGINEERING_ESTIMATE; TOTAL_NORMAL_IMPULSE != MEASURED_GRF
SUPPORT_REGION_CLAIM=ENGINE/ENGINEERING_ESTIMATE; SUPPORT_AP_ML_BOUNDS != EXACT_CONVEX_SUPPORT_POLYGON

SYSTEM_COM_INVARIANT=PASS — all 16 current athlete mass-model bodies and the active 25 kg bar had local centerOfMass within 1e-5 m of Vector3.zero.
SYSTEM_COM_CLAIM_CEILING=VALID_FOR_CURRENT_ZERO_LOCAL_COM_RIG; NONZERO_LOCAL_COM_RIGS REQUIRE WORLD_COM_CAPTURE_OR_EQUIVALENT_FUTURE_CHANGE

TRACE_FREEZE_LIFECYCLE=PASS
AUTOMATIC_ATTEMPT_END_ORCHESTRATION=NOT_IMPLEMENTED

RULES_IMPLEMENTED=NO
FAILURES_IMPLEMENTED=NO
HEAVY_LOAD_CALIBRATION=NO

MASTER_SPEC=PASS — Verify-MasterSpec.ps1; 68 files, hashes PASS, dependencies PASS.
EDITMODE=PASS — full EditMode 84/84; pure P1 contract fixture 11/11.
P1A_REPEATABILITY=PASS — same-machine complete-trace fixture, 3 x 0 kg and 3 x 25 kg, 800 samples each; exact pairwise repeatability.
GAM11_REGRESSION=PASS_WITH_LIMITATIONS — focused `PhysicalSquatControlPlayModeTests` 4/4 and targeted historical graphics performance test 1/1; the full graphics run exposed only the non-reproducible pre-existing performance-budget variance recorded above.

KNOWN_LIMITATIONS
GC allocation remains unresolved because no reliable isolated per-tick collector measurement is available in the current environment; static review found no new managed-array/LINQ/string-formatting path in the P1 collector hot path, but this is not a zero-byte claim.

The repeatability contract is bounded to the current Windows machine, Unity 6000.3.22f1, PhysX configuration, production scene, and production scene-reload reset boundary. An earlier diagnostic of repeated in-place loaded-saddle reset showed immediate divergence; it was not used as qualifying evidence and no controller/physics tuning was applied.

Engine contact points, impulse-weighted contact point, total normal impulse, and AP/ML support bounds remain engineering estimates, not COP, GRF, or exact support-polygon measurements. Cross-platform physical determinism is not claimed.

The final full graphics suite was not perfectly green because of one historical performance-budget sample; this is recorded rather than suppressed. No GAM-12 test failed, the same test passed in targeted headless and graphics runs, and no production code changed.

NEXT_PHASE
GAM12_PHASE2_DETERMINISTIC_SQUAT_RULE_PROCESSOR_AND_ATTEMPT_JUDGMENT — authorized; do not implement automatically in P1A.

LINEAR
GAM-12 remains In Progress; post one concise result comment after the final commit. Do not alter GAM-13.

FINAL_HEAD_EXTERNAL_RECORD_NOTE
The final commit SHA is recorded externally after commit and in Linear. No self-referential SHA is embedded in this receipt.
