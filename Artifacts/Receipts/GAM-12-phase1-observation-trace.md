# GAM-12 Phase 1 Observation and Trace Receipt

MISSION
GAM12_PHASE1_POST_PHYSICS_OBSERVATION_TRACE_AND_PROVENANCE_AUTHORITY

STATUS
PASS_WITH_LIMITATIONS

START_HEAD
e28c61c3e5bffca47a5b6a2f90f511c733586122

FINAL_HEAD_EXTERNAL_RECORD_NOTE
The final commit SHA is recorded externally after commit and in Linear. No self-referential SHA is embedded in this receipt.

CHECKPOINT
checkpoint/gam12-pre-p1-observation-trace at e28c61c3e5bffca47a5b6a2f90f511c733586122; pushed before implementation.

SNAPSHOT_TYPE
`SquatObservationSnapshot` readonly value contract, schema `GAM12_SQUAT_OBSERVATION_SNAPSHOT_V1`.

TRACE_TYPE
`SquatTrace`, fixed-capacity append-only raw trace, schema `GAM12_SQUAT_TRACE_V1`.

SCHEMA_ID
`GAM12_SQUAT_OBSERVATION_SNAPSHOT_V1`; catalog `GAM12_P1_SQUAT_TELEMETRY_CATALOG_V1`.

PHYSICS_SAMPLE_PERIOD
0.010 s / 100 Hz, from `SimulationConstants.FixedDeltaTimeSeconds`.

POST_PHYSICS_SAMPLING_POINT
`PhysicsTickDriver.StepOne`: after the sole local `PhysicsScene.Simulate(0.01)`, after `AuthoritativePhysicsScene.CaptureObservation` and publication, the registered `SquatObservationCollector` promotes current contact callbacks, captures read-only post-step joint diagnostics, refreshes the squat observer, builds one snapshot, and appends it to `SquatTrace`; the existing foundation `AttemptTrace` append follows.

FOUNDATION_TRACE_REUSE_DECISION
Use a specialized `SquatTrace`. `AttemptTrace` already owns generic copied body observations plus intent but cannot carry the squat-specific compact fields atomically without parallel storage and duplicated lifecycle coupling. No broad Foundation refactor was made; only the minimal post-physics callback seam was added.

UNITY_ADAPTER
`SquatObservationCollector` in `PowerliftingSimulator.Squat.Unity`; pure snapshot/schema/trace contracts remain in the no-engine-reference `PowerliftingSimulator.Squat` assembly.

PRODUCTION_CONTROL_CHANGED
NO. The collector has no Rigidbody, drive-target, force, torque, velocity, transform, or phase-control write path. Post-step diagnostics use a separate read-only slot so controller timing is unchanged.

CHANNEL_COUNT
164 catalog channels, each with canonical name, unit, frame, source class, availability semantics, provenance version, and claim note.

SOURCE_CLASSES
Used: `ENGINE_RUNTIME_OBSERVATION`, `GAME_EVENT`, `RULE_DERIVED_GAME_PROXY`, `ENGINEERING_DERIVED`, `DERIVED`, `GAME_MODEL`, `NOT_OBSERVABLE`. Declared for later use but not forced into P1: `DERIVED_FILTERED`, `PROVISIONAL`.

FRAMES
`NONE`, `WORLD`, `JOINT`, `BAR`; no camera or screen-space truth.

MISSING_DATA_POLICY
Availability is explicit. Missing float/vector/quaternion channels use NaN sentinels with `NOT_AVAILABLE`; missing counts use `-1`. A missing bar never emits a measured zero. Support bounds and contact-point estimates are unavailable when no support/contact producer is present. `NOT_OBSERVABLE` channels cannot receive runtime values.

0KG_BAR_POLICY
The physical bar GameObject is inactive; `bar_available=false`, bar position/velocity/orientation/angular velocity/load are explicitly unavailable, and saddle channels are unavailable.

25KG_BAR_POLICY
The dynamic physical bar is active and captured from the post-physics `PhysicalObservation`; load is 25 kg, raw position/velocity match the registered bar Rigidbody, and the finite saddle is available, attached, and coherent in the qualified attempt.

DEPTH_SOURCE
Raw calibrated `left_thigh`/`right_thigh` and `left_shank`/`right_shank` joint anchors from `SquatPhysicalAdapter`; pure bilateral depth margins are deterministic `SquatDepthLandmarks` values. No Phase-1 legal-lift judgment is stored.

COM_SUPPORT_SOURCE
`SquatBalanceObserver` mass-weighted engine-model COM and velocity from copied post-physics bodies; support bounds/contact counts from buffered `PhysicalFootContactDetector` plantar contacts. The impulse-weighted engine contact point is source-classed as an engine contact estimate, never human COP or force-plate data.

JOINT_SOURCE
`PoweredJointController.CapturePostPhysicsDiagnostics` reads current post-PhysX calibrated joint state into a separate diagnostic slot. Snapshot scalar angles/rates are logical `J_i` twist projections; thorax orientation/pitch is copied/derived from the world body state. Reference scalars and actual-reference errors are secondary context/engineering diagnostics.

DRIVE_DIAGNOSTIC_SOURCE
Post-step `PoweredJointDiagnostic` modeled demand, maximum force, activation, and capacity scale. These are `GAME_MODEL` quantities; `currentTorque` is not relabeled as athlete output.

RULE_JUDGMENT_IMPLEMENTED
NO.

FAILURE_CLASSIFIER_IMPLEMENTED
NO. Existing GAM-11 diagnostics remain untouched and are not promoted to GAM-12 canonical truth.

HEAVY_LOAD_CALIBRATION_IMPLEMENTED
NO.

MASTER_SPEC
PASS — `Verify-MasterSpec.ps1`: 68 files, hashes PASS, dependencies PASS.

EDITMODE
PASS — new P1 pure contracts 11/11; full EditMode 84/84.

P1_INTEGRATION
PASS — one bounded production-scene fixture, actual 0 kg and 25 kg deterministic squat attempts, 800 raw samples each, post-physics parity and freeze checks 1/1.

GAM11_REGRESSION
PASS — focused `PhysicalSquatControlPlayModeTests` 4/4, including existing unloaded 0 kg and 25 kg physical qualifications.

FULL_REGRESSION_IF_RUN
Additional headless PlayMode run: 167/178. Nine non-passes were graphics-required/RenderTexture visual gates under `-nographics` plus one performance variance (2.1657 ms vs 2.0 ms); this was not used as the P1 acceptance gate. No full graphics PlayMode run was required by the P1 protocol.

KNOWN_LIMITATIONS
The named `powerlifting-squat` skill was not installed in the available skill catalog. The existing environment does not provide a reliable isolated 0 B/tick measurement gate for the complete Unity pipeline, so allocation freedom is enforced by fixed storage/value contracts and static inspection rather than claimed as a measured byte count. Engine contact points remain estimates; true GRF/COP, biological force, and clinical quantities are not observable. Unity/Burst cache warnings occurred during batch startup but did not affect the passing P1 fixture or EditMode results.

NEXT_PHASE
`GAM12_PHASE2_DETERMINISTIC_SQUAT_RULE_PROCESSOR_AND_ATTEMPT_JUDGMENT`; do not start automatically.

LINEAR_COMMENT
Post one concise Phase 1 result comment on `GAM-12` after the final commit; keep the issue `In Progress`.
