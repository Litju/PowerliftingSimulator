# GAM-9 Final G1 Qualification Receipt

MISSION=GAM_9_RELEASE_CPU_TERMINUS
PR=https://github.com/Litju/PowerliftingSimulator/pull/8
BRANCH=work/gam-9-qualify-shared-substrate
BASE_HEAD=d5a479c7c4da5aac120e530f5fdc59aa0bc79a0c
QUALIFIED_HEAD=5f0f26b
FINAL_HEAD=5f0f26b
FINAL_HEAD_SEMANTICS=qualified implementation/evidence head; this receipt is sealed by the following bookkeeping commit
PR_STATUS=OPEN_NOT_MERGED

STATUS=PASS_WITH_LIMITATIONS

## Shared physical substrate

TOPOLOGY=PASS; 16 athlete bodies, 15 joints, 14 powered joints, 1 passive neck, 1 dynamic bar Rigidbody, 17 observed bodies
PHYSICS_AUTHORITY=PASS; one Simulate authority and one powered writer
POWERED_NEUTRAL=PASS; finite joint drives preserved; passive fall and bar behavior preserved
BAR=PASS; 105 kg bar, symmetric loading, finite inertia, 1.125 m gravity drop

OBSERVATION_ALLOCATION_DEFECT=FIXED; the per-tick `new PhysicalBodyObservation[_bodies.Count]` plus defensive-copy array was removed from the authoritative observation path
OBSERVATION_STORAGE_DESIGN=PASS; bounded preallocated three-slot observation exchange with internal read-only slices and stable 17-body ordering
OBSERVATION_IMMUTABILITY=PASS; public observation access exposes values only; no Rigidbody/GameObject references or mutable arrays escape
CURRENT_PREVIOUS_CONTRACT=PASS; current/previous observations retain their documented bounded lifetime across slot exchange
TRACE_HISTORICAL_IMMUTABILITY=PASS; trace samples copy values into distinct flat reserved slices and remain unchanged after later ticks
TRACE_CAPACITY_SAMPLES=3000
TRACE_REGISTERED_BODY_COUNT=17
TRACE_RESERVED_BODY_RECORD_COUNT=96000
TRACE_RESERVED_BODY_RECORD_STORAGE=6144000 bytes
TRACE_RESERVED_STORAGE=6336000 bytes
TRACE_LOGICAL_PAYLOAD_STORAGE=3264000 bytes
TRACE_LOGICAL_UNCOMPRESSED_BUDGET=26214400 bytes
TRACE_APPEND_ALLOCATION=0 B/tick after warm-up

## Allocation qualification

ALLOCATION_BUILD=GAM9_ALLOC_DEV; Windows x64 Development build; allocation-only evidence
ALLOCATION_METRIC=Unity ProfilerRecorder `Internal/GC.Alloc` samples in the standalone allocation player
GC_ACTIVE_TICK=PASS; OFF max 0 B/tick over 120 samples
GC_TRACE=PASS; TRACE max 0 B/tick over 120 samples
STATIC_HOT_PATH_AUDIT=PASS; no managed `new[]`, LINQ, boxing, or string formatting in normal `StepOne` observation/trace append path
TRACE_RESERVED_STORAGE_SEMANTICS=PASS; reserved storage is reported separately from incremental append allocation

## Reset repeatability and mutation gates

RESET_REPEAT_COUNT=20 actual repeated trials
TICKS_PER_REPEAT=20
TRACED_REPEAT_COUNT=10; 10 recording-OFF and 10 recording-ON trials
RESET_SEQUENCE=105 kg bar, PoweredNeutral, canonical reset, identical fixed tick sequence
RESET_POSITION_MAX_DELTA_M=0.0
RESET_ORIENTATION_MAX_DELTA_DEGREES=0.0
RESET_LINEAR_VELOCITY_MAX_DELTA_MPS=0.0
RESET_ANGULAR_VELOCITY_MAX_DELTA_RAD_S=0.0
RESET_BODY_ORDER=PASS
RESET_TICK_AND_SIMULATION_TIME=PASS
RESET_BAR_MASS_AND_INERTIA=PASS; 105 kg and zero inertia delta
RESET_POWERED_CONTROLLER_TARGET_STATE=PASS
M05_USE_ACCELERATION_GATE=PASS; `useAcceleration=true` was rejected by `ValidatePoweredDrive`, then the original drive was restored
MUTATION_GATES=PASS; M01-M12 all rejected, including M05 useAcceleration

## Windows performance qualification

PERFORMANCE_BUILD=GAM9_PERF_RELEASE; Windows x64 non-development release; Mono backend provisional
PERFORMANCE_BUILD_OPTIONS=Development=OFF; Autoconnect Profiler=OFF; Deep Profiling=OFF; Script Debugging=OFF; FrameTimingStats=ON
BENCHMARK_WINDOW=1280x720 windowed, Direct3D11, refresh 60.059349 Hz, vSyncCount=0, targetFrameRate=-1
WARMUP_FRAMES=600
MEASUREMENT_FRAMES=1000 actual rendered frames
GAMEPLAY_PROFILE=G1_GAMEPLAY_PERFORMANCE_PROFILE; visible humanoid/barbell/camera/100 Hz physics/finite drives ON; physical proxies, COM/anchor/axis/bar debug visuals, trail, and qualification IMGUI OFF

FOUNDATION_FRAME_STEP_P50_P95_P99_WORST_MS=0.1733 / 0.2413 / 0.2962 / 0.3155; FoundationRuntime stage only
FULL_CPU_FRAME_P50_P95_P99_WORST_MS=4.4602 / 5.8293 / 6.2916 / 7.4098; UnityEngine.FrameTimingManager.cpuFrameTime
CPU_MAIN_THREAD_P50_P95_P99_WORST_MS=3.0309 / 4.1198 / 4.6040 / 5.5694
CPU_RENDER_THREAD_P50_P95_P99_WORST_MS=0.7408 / 1.2447 / 1.5218 / 2.8643
PRESENT_WAIT_P50_P95_P99_WORST_MS=1.1786 / 3.0288 / 3.4874 / 4.1787
GPU_FRAME_P50_P95_P99_WORST_MS=3.274010 / 3.512239 / 3.699687 / 3.939636; valid FrameTimingManager GPU channel, 862 samples
DRAW_CALLS_P50_P95_P99_WORST=60 / 60 / 60 / 60
BATCHES_P50_P95_P99_WORST=60 / 60 / 60 / 60
SETPASS_P50_P95_P99_WORST=12 / 12 / 12 / 12
SHADOW_CASTERS_P50_P95_P99_WORST=19 / 19 / 19 / 19
VISIBLE_SKINNED_MESHES_P50_P95_P99_WORST=3 / 3 / 3 / 3
PHYSICS_P95_MS=0.2126
FOUR_TICK_P95_MS=0.9363
CONTROLLER_P95_MS=0.0339
PERFORMANCE_GATE=PASS; CPU Total p95 <= 10.0 ms, GPU p95 <= 12.0 ms, physics p95 <= 2.0 ms, four-tick p95 <= 8.0 ms, controller p95 <= 0.25 ms
INVALID_MEASUREMENT_EXCLUDED=PASS; an earlier hidden-window run with zero render counters was excluded and not used as acceptance evidence

## Build, test, and audit evidence

BUILD_WARNING_AUDIT=PASS; 486 total warnings, 41 distinct messages, 0 blocking, 0 unclassified; all classified `NON_BLOCKING_TOOLCHAIN_WARNING` with message evidence
WINDOWS_BUILD=PASS; separate allocation Development and performance non-development release builds succeeded with 0 errors
WINDOWS_SMOKE=PASS; allocation and release players exited PASS at exact 1280x720/D3D11
FOCUSED_TESTS=PASS; allocation, trace immutability, repeatability, mutation, and frame-metric contract gates
EDITMODE=PASS 48/48; one final full EditMode run
PLAYMODE=PASS 29/29; one final full PlayMode run
PLAYER_LOG=PASS; `Temp/GAM9/standalone-player-final4.log` and `Temp/GAM9/standalone-allocation-dev-final-player.log`
MEASUREMENT_ARTIFACTS=Artifacts/Measurements/GAM-9-performance.json; GAM-9-g1-qualification.json; GAM-9-repeatability.json; GAM-9-mutation-gates.json; GAM-9-standalone-smoke.json; GAM-9-standalone-allocation-dev.json; GAM-9-build-warning-audit.json; GAM-9-build-warning-audit-alloc-dev.json
TEST_ARTIFACTS=Artifacts/Measurements/GAM-9-full-editmode-final.xml; Artifacts/Measurements/GAM-9-full-playmode-final2.xml
MASTER_SPEC=UNCHANGED
DIFF_CHECK=PASS

ASSET_LICENSE_AUDIT=PASS; no new third-party assets introduced
CLAIM_AUDIT=PASS; receipt separates full Unity frame timing from FoundationRuntime execution timing and reports reserved trace storage truthfully
KNOWN_LIMITATIONS=finite open-loop neutral is not indefinite balance; rigid engineering bar with no grip/rack/lift coupling; no full replay product; Mono backend provisional; no cross-platform PhysX determinism claim
NEXT_ACTION=PROJECT_CONTROL_FINAL_PR8_REVIEW
