# Deadlift Telemetry and Analysis

**Document ID:** `PSMS-DL-37`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/31_DEADLIFT_BIOMECHANICS.md`, `DEADLIFT/35_DEADLIFT_RULES.md`, `DEADLIFT/36_DEADLIFT_FAILURE_MODEL.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Inspect existing AttemptTrace schemas and preserve compatible M1 data semantics.
- Verify the actual physics sample rate and all channel units in standalone builds.
- Run golden-signal tests for filters and metric boundaries.

## PURPOSE

    Define authoritative deadlift trace and metrics for setup/slack, physical floor break, bar path and
proximity, velocity by zone, grip, lockout, Down command, return, rules, and failure.

    ## INPUTS

    Post-physics bar/floor/grip/athlete state, deadlift landmarks, state/intent/commands/rules/failure.

    ## OUTPUTS

    `DeadliftObservationSnapshot`, raw trace, finalized analysis with zones, replay/overlay channels.

    ## STATE

    100 Hz samples; grip/slack/Pull/floor-break/knee-pass/lockout/Down/ground indices; filter and zone versions.

    ## UNITS

    SI/radians internally; display conversions are metadata-backed and never overwrite stored values.

    ## COORDINATE CONVENTION

    Every spatial channel declares world, athlete-local, joint, or BAR frame. Camera/screen coordinates are prohibited for truth.

    ## EQUATIONS


Time to floor break is Pull onset to the first persistent physical floor-break event. Concentric duration is floor break
to legal lockout. Mean concentric velocity:

\[
\bar v_c=(y_{lockout}-y_{floorbreak})/(t_{lockout}-t_{floorbreak}).
\]

Bar drift is `z_bar - z_ref(s_d)`. Bar-to-leg distance uses collider/landmark closest points. Sticking/stall is summarized
by physically observed floor, below-knee, knee-level, and hip-extension zones.


    ## ASSUMPTIONS

    Engine state is authoritative only for this simulation. The record is not a force plate or motion-capture
measurement. Raw and derived channels remain separate.

    ## APPROXIMATIONS

    Virtual landmarks, rigid segments, conceptual drive demand, and filtering limit the claim ceiling.
No metric receives more significant digits in UI/export than its model and calibration justify.

    ## GAME CALIBRATIONS

    100 Hz raw; direct velocity for control/rules. Online display 50 ms causal smoothing. Post-attempt
zero-phase two-pass second-order Butterworth default 10 Hz with reflected edges. Floor contact, clearance, grip break,
and command events are unfiltered. Acceleration omitted on short or invalid traces.

    ## NUMERICAL IMPLEMENTATION

    Preallocate a fixed-capacity trace for the maximum attempt duration. Snapshot after PhysX and after all
observers, before presentation. Store raw float state and double/ulong time identifiers. Postprocessing is pure, versioned,
and repeatable. Missing events yield explicit `NOT_AVAILABLE`, never zero.

    ## PSEUDOCODE

    ```text
    RecordDeadliftSnapshot(post_physics):
    freeze bar pose/velocity/floor contacts/clearance,
           grips, athlete/joints, bar-leg distances,
           state, intent, commands, rules, failure
    trace.Append(snapshot)

AnalyzeDeadlift(trace):
    validate_event_order(floor_break, knee_pass, lockout, Down, ground)
    postprocess_bar_channels()
    compute_floor_break_time, velocity, drift, proximity, zones
    attach provenance, filter, quality
    ```

    ## UNITY MAPPING

    Pure trace records plus a Unity adapter that samples Rigidbody/joint/contact state. Analysis runs after
attempt completion, ideally off the fixed path. Replay consumes the same snapshots. CSV/JSON export includes schema,
ruleset, calibration, source class, units, coordinate frame, filter, and quality flags.

    ## FAILURE MODES

    Floor break from intent; zone from animation phase only; filtered contact event; proximity from skin;
bar gravitational work called total athlete work; slack called tendon slack; grip diagnostic called force.

    ## OBSERVABILITY

    A channel catalog identifies producer, sampling point, unit, frame, source class, algorithm version,
and consumer. Debug UI can overlay raw vs display vs postprocessed signals without mixing them.

    ## TELEMETRY


| Metric | Unit | Source class |
|---|---:|---|
| grounded/floor break | bool, s | ENGINE_RUNTIME_OBSERVATION |
| slack duration | s | GAME_EVENT |
| bar displacement/velocity | m, m/s | DIRECT/DERIVED |
| acceleration | m/s² | DERIVED_FILTERED/PROVISIONAL |
| bar-midfoot/drift/leg distance | m | ENGINEERING_DERIVED |
| knee-pass/zone times | s | EVENT_DERIVED |
| grip slip/failure | m, enum | ENGINE_RUNTIME_OBSERVATION |
| lockout geometry/timing | rad, m, s | RULE_DERIVED_GAME_PROXY |
| external bar PE/work estimate | J | ENGINEERING_DERIVED_LIMITED |
| true grip/GRF/spinal/muscle forces | — | NOT_OBSERVABLE |


    ## TESTS

    Known floor-break/contact, displacement/mean velocity, zone boundaries, drift sign, closest distance,
filter phase and edges, short trace, immutable data, provenance/units, Down/return event order.

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

    **SHIP_V1:** complete deadlift trace/metrics. **LATER:** comparison and separate sumo metrics.
**RESEARCH:** validated kinetics. **OUT_OF_SCOPE:** internal forces and physiological work.
