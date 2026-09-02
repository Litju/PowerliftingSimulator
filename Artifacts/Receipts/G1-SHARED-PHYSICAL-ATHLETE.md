# G1 Shared Physical Athlete Qualification Receipt

MISSION=GAM_9_RELEASE_CPU_TERMINUS
PR=https://github.com/Litju/PowerliftingSimulator/pull/8
BRANCH=work/gam-9-qualify-shared-substrate
BASE_HEAD=d5a479c7c4da5aac120e530f5fdc59aa0bc79a0c
QUALIFIED_HEAD=5f0f26b
FINAL_HEAD=5f0f26b
FINAL_HEAD_SEMANTICS=qualified implementation/evidence head; this receipt is sealed by the following bookkeeping commit
STATUS=PASS_WITH_LIMITATIONS

TOPOLOGY=PASS; 16 athlete bodies, 15 joints, 14 powered joints, 1 passive neck, one dynamic 105 kg bar Rigidbody, 17 observed bodies
AUTHORITY=PASS; one Simulate authority and one powered writer
POWERED_NEUTRAL=PASS; finite drives and neutral reset preserved
PASSIVE_FALL=PASS; passive neck behavior unchanged
BAR=PASS; symmetric 105 kg loading, finite inertia, 1.125 m gravity drop, finite sleeve/overflow gates

OBSERVATION_STORAGE=PASS; reusable preallocated observation slots with internal read-only slices; stable body order and PrimaryBody semantics preserved
OBSERVATION_ALLOCATION=PASS; no per-tick managed array allocation in the authoritative post-physics observation path
CURRENT_PREVIOUS=PASS; current/previous exchange remains bounded and immutable to callers
TRACE_HISTORICAL_IMMUTABILITY=PASS; later ticks do not overwrite earlier trace samples
TRACE_CAPACITY=3000 samples
TRACE_REGISTERED_BODIES=17
TRACE_RESERVED_BODY_RECORDS=96000
TRACE_RESERVED_STORAGE=6336000 bytes
TRACE_LOGICAL_PAYLOAD=3264000 bytes of 26214400-byte target
TRACE_APPEND_ALLOCATION=0 B/tick after warm-up

GC_ACTIVE_TICK=PASS; GAM9_ALLOC_DEV standalone ProfilerRecorder `Internal/GC.Alloc`, OFF max 0 B/tick over 120 samples
GC_TRACE=PASS; GAM9_ALLOC_DEV standalone ProfilerRecorder `Internal/GC.Alloc`, TRACE max 0 B/tick over 120 samples
STATIC_HOT_PATH_AUDIT=PASS; no managed arrays/LINQ/boxing/string formatting in StepOne observation/trace append path

RESET_REPEAT_COUNT=20 actual trials
TICKS_PER_REPEAT=20
TRACED_REPEAT_COUNT=10
RESET_DELTAS=PASS; position 0.0 m, orientation 0.0 degrees, linear velocity 0.0 m/s, angular velocity 0.0 rad/s
RESET_METADATA=PASS; body ordering, tick, simulation time, 105 kg bar mass, inertia, and powered-controller target state matched every repetition

M05_USE_ACCELERATION_GATE=PASS; explicit `useAcceleration=true` rejected by the powered-drive validator and original drive restored
MUTATION_GATES=PASS; M01-M12 all rejected

PERFORMANCE_BUILD=GAM9_PERF_RELEASE; non-development Windows x64 release
PERFORMANCE_PROFILE=G1_GAMEPLAY_PERFORMANCE_PROFILE; visible humanoid/barbell/camera/100 Hz physics/finite drives ON; developer-only proxy/marker/axis/trail/IMGUI presentation OFF
BENCHMARK=1280x720 windowed, Direct3D11, refresh 60.059349 Hz, vSyncCount=0, targetFrameRate=-1, 600 warm-up and 1000 measured frames
FOUNDATION_FRAME_STEP_P50_P95_P99_WORST_MS=0.1733 / 0.2413 / 0.2962 / 0.3155
FULL_CPU_FRAME_P50_P95_P99_WORST_MS=4.4602 / 5.8293 / 6.2916 / 7.4098
CPU_MAIN_THREAD_P50_P95_P99_WORST_MS=3.0309 / 4.1198 / 4.6040 / 5.5694
CPU_RENDER_THREAD_P50_P95_P99_WORST_MS=0.7408 / 1.2447 / 1.5218 / 2.8643
PRESENT_WAIT_P50_P95_P99_WORST_MS=1.1786 / 3.0288 / 3.4874 / 4.1787
GPU_FRAME_P50_P95_P99_WORST_MS=3.274010 / 3.512239 / 3.699687 / 3.939636
DRAW_CALLS=60; BATCHES=60; SETPASS=12; SHADOW_CASTERS=19; VISIBLE_SKINNED_MESHES=3
PHYSICS_P95_MS=0.2126
FOUR_TICK_P95_MS=0.9363
CONTROLLER_P95_MS=0.0339
PERFORMANCE_GATE=PASS; all stated CPU, GPU, physics, catch-up, and controller budgets met

BUILD_WARNING_AUDIT=PASS; 486 total, 41 distinct messages, 0 blocking, 0 unclassified; all classified non-blocking toolchain warnings with evidence
WINDOWS_BUILD=PASS; allocation Development and performance non-development builds succeeded with 0 errors
WINDOWS_SMOKE=PASS; both player qualification runs passed
EDITMODE=PASS 48/48
PLAYMODE=PASS 29/29
FOCUSED_TESTS=PASS; focused G1 allocation, observation/trace, repeatability, mutation, and timing-contract coverage
MASTER_SPEC=UNCHANGED
DIFF_CHECK=PASS

ASSET_LICENSE_AUDIT=PASS; no new third-party assets introduced
CLAIM_AUDIT=PASS; full Unity CPU frame timing is kept distinct from FoundationRuntime step timing
KNOWN_LIMITATIONS=finite open-loop neutral is not indefinite balance; rigid engineering bar; no grip/rack/lift coupling; no replay product; Mono backend provisional; no cross-platform PhysX determinism claim
NEXT_ACTION=PROJECT_CONTROL_FINAL_PR8_REVIEW
