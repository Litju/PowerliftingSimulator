# Bench Press Telemetry and Analysis

**Document ID:** `PSMS-BP-27`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/21_BENCH_BIOMECHANICS.md`, `BENCH/25_BENCH_RULES.md`, `BENCH/26_BENCH_FAILURE_MODEL.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Inspect existing AttemptTrace schemas and preserve compatible M1 data semantics.
- Verify the actual physics sample rate and all channel units in standalone builds.
- Run golden-signal tests for filters and metric boundaries.

## PURPOSE

    Define authoritative bench trace and metrics for setup contacts, touch, pause, bar path, bilateral press,
grip, joint kinematics, sticking, commands, rules, and failure.

    ## INPUTS

    Post-physics athlete/bar/grip/contact state, bench landmarks, state/intent/commands/rules/failure.

    ## OUTPUTS

    `BenchObservationSnapshot`, raw trace, finalized analysis, replay/overlay channels.

    ## STATE

    100 Hz samples; setup/Start/descent/touch/pause/Press/sticking/lockout/Rack indices; side-specific channels;
filter/provenance versions.

    ## UNITS

    SI/radians internally; display conversions are metadata-backed and never overwrite stored values.

    ## COORDINATE CONVENTION

    Every spatial channel declares world, athlete-local, joint, or BAR frame. Camera/screen coordinates are prohibited for truth.

    ## EQUATIONS


Pause duration is the contiguous valid touch-and-still interval before Press. Bar tilt:

\[
\Delta y_k=y_{end,R,k}-y_{end,L,k}.
\]

Mean press velocity uses lockout minus press-onset shaft-center height divided by duration and is distinct from mean
signed direct velocity. Bilateral asymmetry summaries report peak/rms endpoint and hand-anchor differences; they are
game-model symmetry indicators only.


    ## ASSUMPTIONS

    Engine state is authoritative only for this simulation. The record is not a force plate or motion-capture
measurement. Raw and derived channels remain separate.

    ## APPROXIMATIONS

    Virtual landmarks, rigid segments, conceptual drive demand, and filtering limit the claim ceiling.
No metric receives more significant digits in UI/export than its model and calibration justify.

    ## GAME CALIBRATIONS

    100 Hz raw; contact/commands unfiltered. Online display 50 ms causal smoothing. Post-attempt default
zero-phase two-pass second-order Butterworth at 10 Hz for bar velocity and tilt-rate display; reflected edges; acceleration
optional/provisional. Pause rules always use direct velocity thresholds.

    ## NUMERICAL IMPLEMENTATION

    Preallocate a fixed-capacity trace for the maximum attempt duration. Snapshot after PhysX and after all
observers, before presentation. Store raw float state and double/ulong time identifiers. Postprocessing is pure, versioned,
and repeatable. Missing events yield explicit `NOT_AVAILABLE`, never zero.

    ## PSEUDOCODE

    ```text
    RecordBenchSnapshot(post_physics):
    freeze bar center/endpoints, grips, contacts,
           shoulder-elbow-wrist landmarks, joint state,
           bench state, intent, commands, rules, failure
    trace.Append(snapshot)

AnalyzeBench(trace):
    validate(Start < touch < Press < lockout < Rack)
    compute_touch_pause_path_velocity_tilt()
    compute_bilateral_joint_and_grip_summary()
    derive_sticking()
    attach provenance, filter, quality
    ```

    ## UNITY MAPPING

    Pure trace records plus a Unity adapter that samples Rigidbody/joint/contact state. Analysis runs after
attempt completion, ideally off the fixed path. Replay consumes the same snapshots. CSV/JSON export includes schema,
ruleset, calibration, source class, units, coordinate frame, filter, and quality flags.

    ## FAILURE MODES

    Pause from phase timer; filter used for command; average symmetry hides one arm; visual hand pose used
instead of physical grip; modeled demand called muscle activation; tilt sign tied to camera.

    ## OBSERVABILITY

    A channel catalog identifies producer, sampling point, unit, frame, source class, algorithm version,
and consumer. Debug UI can overlay raw vs display vs postprocessed signals without mixing them.

    ## TELEMETRY


| Metric | Unit | Source class |
|---|---:|---|
| setup contacts | bool/time | ENGINE_RUNTIME_OBSERVATION |
| touch location and elbow depth | m | RULE_DERIVED_GAME_PROXY |
| pause duration | s | RULE_DERIVED |
| bar displacement/velocity/tilt | m, m/s | DIRECT/DERIVED |
| acceleration | m/s² | DERIVED_FILTERED/PROVISIONAL |
| bilateral grip slip/error | m, rad | ENGINEERING_DERIVED |
| sticking interval | s, m | GAME_ANALYSIS_DERIVED |
| joint angles/rates | rad, rad/s | ENGINEERING_DERIVED |
| modeled demand/power | 1, W | GAME_MODEL |
| true shoulder/elbow/muscle force | — | NOT_OBSERVABLE |


    ## TESTS

    Known path/tilt, event ordering, pause direct vs display-filtered, one-arm synthetic signal, filter phase,
trace immutability, camera invariance, provenance/quality required.

    ## MUTATION TESTS

    Use filtered state for rules; derive velocity from render transforms; omit frame/unit/source; write zero
for missing; mutate snapshots; use currentTorque as true athlete output; reuse another lift's event semantics. Each mutation
must fail schema or golden tests.

    ## PERFORMANCE CONSIDERATIONS

    Raw trace is a struct-of-arrays or compact immutable records with no per-tick allocations. Filter and
analysis run once after attempt; normal telemetry remains bounded.

    ## CLAIM CLASSIFICATION

    All fields carry explicit source class. Direct engine state is `ENGINE_RUNTIME_OBSERVATION`; geometry and
time derivatives are `DERIVED`; capacity/demand are `GAME_MODEL`; acceleration often `PROVISIONAL`; true muscle/tendon/
internal joint/GRF/COP quantities remain `NOT_OBSERVABLE`.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** complete bench trace/metrics. **LATER:** comparisons and technique summaries.
**RESEARCH:** validation with external measurements. **OUT_OF_SCOPE:** clinical shoulder kinetics.
