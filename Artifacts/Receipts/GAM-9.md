MISSION=POWERLIFTING_SIMULATOR_GAM_9_QUALIFY_SHARED_PHYSICAL_SUBSTRATE
LINEAR_ISSUE=GAM-9

BASE_HEAD=d5a479c7c4da5aac120e530f5fdc59aa0bc79a0c
QUALIFIED_HEAD=f43346f5241b298eb704f3e53638abdd53772753
FINAL_HEAD=f43346f5241b298eb704f3e53638abdd53772753
FINAL_HEAD_SEMANTICS=qualified implementation head; this receipt and the three project-local skill updates are sealed in a later bookkeeping commit
TREE_HASH=c003c4d3aef1038b4d49f1deefacd360bd7deb85
BRANCH=work/gam-9-qualify-shared-substrate

UNITY_VERSION=6000.3.22f1
PACKAGE_LOCK_HASH=CBF46DE16C5A5D81C1A7FE1C56DE064EB3EE5CE52B0C957A7A910748D8D95380
PHYSICS_STEP_S=0.010

PHYSICS_SCENE_COUNT=1 non-default scene PowerliftingSimulator.AuthoritativePhysics
PHYSICS_SIMULATE_AUTHORITY_COUNT=1 local PhysicsScene.Simulate; global Physics.Simulate=0
POWERED_WRITER_COUNT=1 authoritative pre-physics owner; duplicate-writer gate rejected

ATHLETE_BODY_COUNT=16 dynamic gravity bodies
BAR_BODY_COUNT=1 dynamic gravity Rigidbody
OBSERVED_BODY_COUNT=17 post-physics immutable observation bodies
POWERED_JOINT_COUNT=14
PASSIVE_JOINT_COUNT=1 passive neck
ATHLETE_TOTAL_MASS_KG=100.0
HIDDEN_SUPPORT=NOT_FOUND; non-adjacent penetration <=0.015 m and passive fall proof passed
ANIMATOR_PHYSICAL_AUTHORITY=NO
DRIVE_AUTHORITY=PASS; finite profile-owned drives, useAcceleration=false, projection disabled, no AddTorque path

PASSIVE_NO_SUPPORT=PASS; 0.4529830813 m COM drop over 75 local ticks / 0.75 s
POWERED_NEUTRAL=PASS; 0.1230710745 m COM drop over 75 local ticks / 0.75 s; finite authority materially reduced collapse
ZERO_ACTIVATION=PASS; all powered-joint maximumForce values were zero
JOINT_PULSE=PASS; positive left-shank response 5.01373052597 degrees
FEET_PLATFORM_CONTACT=PASS; both physical feet and the existing authoritative platform were exercised without hidden support
BARBELL=PASS; one dynamic 105 kg bar; 1.1250306368 m gravity drop; symmetric 105 kg loading; physical trail is presentation-only
BAR_SLEEVE_OVERFLOW=PASS; maximum finite inventory request 725 kg rejected by the loading solver

RESET_REPEAT_COUNT=20 manual samples plus 20 trace samples per repeatability run
RESET_POSITION_MAX_DELTA_M=0.0
RESET_ORIENTATION_MAX_DELTA=0.0 degrees
RESET_LINEAR_VELOCITY_MAX_DELTA_MPS=0.0
RESET_ANGULAR_VELOCITY_MAX_DELTA_RAD_S=0.0
RESET_BAR_MASS_KG=105.0
RESET_BAR_INERTIA_MAX_DELTA_KG_M2=0.0
RESET_REPEATABILITY=PASS; reset clears observation, trace, input samples, targets, and velocities while preserving dynamic gravity bodies

TRACE=PASS; immutable post-physics observation paired with intent; 20-sample reset trace and 300-sample normal trace; no Rigidbody below recorded trail
MUTATION_GATE_COUNT=12
MUTATION_RESULTS=PASS 12/12 rejected: M01 authority audit; M02 duplicate writer; M03 kinematic pelvis; M04 infinite drive; M05 useAcceleration; M06 projection; M07 extra bar Rigidbody; M08 asymmetric plate; M09 sleeve overflow; M10 duplicate body ID; M11 physical trail; M12 wrong-sign knee command

PERFORMANCE_CPU_FRAME_P50_MS=0.4144
PERFORMANCE_CPU_FRAME_P95_MS=1.1238
PERFORMANCE_CPU_FRAME_P99_MS=4.7479
PERFORMANCE_CPU_FRAME_WORST_MS=16.0287
PERFORMANCE_PHYSICS_P50_MS=0.4293
PERFORMANCE_PHYSICS_P95_MS=1.3815
PERFORMANCE_PHYSICS_P99_MS=3.0669
PERFORMANCE_PHYSICS_WORST_MS=19.4682
FOUR_TICK_CATCHUP=PASS; editor p95=4.6389 ms, standalone p95=1.7815 ms; 8 ms p95 gate
GC_ACTIVE_TICK=PASS; editor 0 bytes/tick
WORKING_SET=PASS; standalone process working set=450363392 bytes < 2147483648-byte target; editor allocated memory=274249175 bytes and editor Process.WorkingSet64 was unavailable
TRACE_MEMORY=PASS; 0 bytes allocated across 300 normal trace samples; 26214400-byte budget
BOUNDED_SOAK=PASS; 100 editor cycles; baseline rigidbodies=17, joints=15; peak allocated memory=274251231 bytes
GPU_FRAME_TIMING=NOT_AVAILABLE_IN_CURRENT_HARNESS; no GPU p95 claim made

WINDOWS_BUILD=PASS; BuildReport.summary.result=Succeeded; totalErrors=0; totalWarnings=486; totalSize=148169330 bytes
WINDOWS_SCRIPTING_BACKEND=Mono2x
WINDOWS_BUILD_SHA256=8DA50E79B68E979C53CAFB639961CA6C34D39BFED8E87B91CF9CAF5C12C081FE
WINDOWS_SMOKE=PASS; 17-body observation, powered-neutral response, 105 kg bar drop, catch-up, physics, render, controller, and memory gates
PLAYER_LOG_SCAN=PASS; no forbidden error markers or unhandled exceptions
WINDOWS_SCREENSHOT_SHA256=0C31C734B286482A1E1204A500F95D14BC06DF705FC2ED81CB2DA6DD49005C1C

EDITMODE=PASS 47/47; one full EditMode run
PLAYMODE=PASS 28/28; one full PlayMode run
FOCUSED_PLAYMODE=PASS 4/4; final focused G1 run after evidence-only test corrections

MASTER_SPEC=PASS; 68 files, HASHES=PASS, DEPENDENCIES=PASS
DIFF_CHECK=PASS; staged and unstaged checks clean
ASSET_LICENSE_AUDIT=PASS; Quaternius Standard athlete provenance and included CC0 1.0 evidence retained; no new third-party assets
CLAIM_AUDIT=PASS; no unsupported determinism/biomechanics claim; production source has one local simulation owner and no scripted physical motion

VISUAL_EVIDENCE=Artifacts/Evidence/GAM-9/; six editor qualification captures plus GAM-9-windows-standalone.png
MEASUREMENT_ARTIFACTS=Artifacts/Measurements/GAM-9-g1-qualification.json; GAM-9-mutation-gates.json; GAM-9-repeatability.json; GAM-9-performance.json; GAM-9-standalone-smoke.json; GAM-9-focused-playmode-final-2.xml; GAM-9-full-editmode.xml; GAM-9-full-playmode.xml

PR=https://github.com/Litju/PowerliftingSimulator/pull/8; open and not merged
LINEAR_STATUS=UNAVAILABLE_NO_LINEAR_CONNECTOR; GAM-9 status/attachment was not verified or changed

KNOWN_LIMITATIONS=G1 substrate only; prototype scene, not final venue/lift/replay/release qualification; open-loop neutral is finite posture authority, not indefinite balance; rigid engineering bar with no grip/rack/lift coupling; GPU timing unavailable in current harness; no cross-platform PhysX determinism claim

OWNER_ACCEPTED=NO_PENDING_OWNER_REVIEW
NEXT_ACTION=OWNER G1 REVIEW
STATUS=PASS_WITH_LIMITATIONS
