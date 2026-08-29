# Squat Telemetry and Analysis

**Document ID:** `PSMS-SQ-17`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/11_SQUAT_BIOMECHANICS.md`, `SQUAT/15_SQUAT_RULES.md`, `SQUAT/16_SQUAT_FAILURE_MODEL.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Inspect existing AttemptTrace schemas and preserve compatible M1 data semantics.
- Verify the actual physics sample rate and all channel units in standalone builds.
- Run golden-signal tests for filters and metric boundaries.

## PURPOSE

    Define authoritative squat observations and a post-attempt record for depth, COM/support, bar motion,
joint kinematics, phase timing, modeled demand, and failure/rule provenance.

    ## INPUTS

    Post-physics athlete/bar states, contacts, calibrated landmarks, squat state/intent/commands, rules,
failure detector, and capacity diagnostics.

    ## OUTPUTS

    `SquatObservationSnapshot`, raw `SquatTrace`, finalized `SquatAnalysis`, replay channels, and quality flags.

    ## STATE

    100 Hz immutable samples; event indices for command, descent, bottom, reversal, sticking, lockout, Rack,
failure; filter specification and provenance versions.

    ## UNITS

    SI/radians internally; display conversions are metadata-backed and never overwrite stored values.

    ## COORDINATE CONVENTION

    Every spatial channel declares world, athlete-local, joint, or BAR frame. Camera/screen coordinates are prohibited for truth.

    ## EQUATIONS


Depth margin at sample `k`:

\[
d_k=-\max(y_{hip,L}-y_{knee,L},y_{hip,R}-y_{knee,R}).
\]

Eccentric duration is descent onset to reversal; concentric duration is reversal to physical lockout.
Mean concentric velocity is both `Δy/Δt` and time-average signed direct velocity as separate fields.
COM/support margin and geometric moment-arm proxies follow PSMS-SQ-11.
Sticking is a declared interval satisfying the squat detector's low-velocity/high-demand/state predicates.


    ## ASSUMPTIONS

    Engine state is authoritative only for this simulation. The record is not a force plate or motion-capture
measurement. Raw and derived channels remain separate.

    ## APPROXIMATIONS

    Virtual landmarks, rigid segments, conceptual drive demand, and filtering limit the claim ceiling.
No metric receives more significant digits in UI/export than its model and calibration justify.

    ## GAME CALIBRATIONS

    Raw 100 Hz direct states. Online HUD uses causal 50 ms smoothing only. Post-attempt default is
zero-phase two-pass second-order Butterworth at 10 Hz on bar/COM velocity before optional central-difference acceleration;
reflected edge padding; coefficients and actual cutoff stored. Rule depth uses unfiltered landmarks plus persistence.

    ## NUMERICAL IMPLEMENTATION

    Preallocate a fixed-capacity trace for the maximum attempt duration. Snapshot after PhysX and after all
observers, before presentation. Store raw float state and double/ulong time identifiers. Postprocessing is pure, versioned,
and repeatable. Missing events yield explicit `NOT_AVAILABLE`, never zero.

    ## PSEUDOCODE

    ```text
    RecordSquatSnapshot(post_physics):
    freeze athlete, bar, contacts, COM, support,
           landmarks, joint state, drive diagnostics,
           squat state, intent, commands, rules, failure
    trace.Append(snapshot)

AnalyzeSquat(trace):
    validate_event_order()
    derive_depth_extrema_and_durations()
    postprocess_bar_com_velocity()
    derive_sticking_and_joint_summary()
    attach source_class, algorithm_version, filter_spec, quality
    ```

    ## UNITY MAPPING

    Pure trace records plus a Unity adapter that samples Rigidbody/joint/contact state. Analysis runs after
attempt completion, ideally off the fixed path. Replay consumes the same snapshots. CSV/JSON export includes schema,
ruleset, calibration, source class, units, coordinate frame, filter, and quality flags.

    ## FAILURE MODES

    Depth from average side; filtered data drives rule; phase used instead of physical events; acceleration
without quality flag; conceptual torque called measured; COM called COP; missing algorithm/filter version.

    ## OBSERVABILITY

    A channel catalog identifies producer, sampling point, unit, frame, source class, algorithm version,
and consumer. Debug UI can overlay raw vs display vs postprocessed signals without mixing them.

    ## TELEMETRY


| Metric | Unit | Source class |
|---|---:|---|
| load/outcome/commands | kg, enum, s | DIRECT/GAME_EVENT |
| bilateral depth and minimum margin | m | RULE_DERIVED_GAME_PROXY |
| eccentric/concentric duration | s | DERIVED |
| bar/COM displacement and velocity | m, m/s | DIRECT/DERIVED |
| acceleration | m/s² | DERIVED_FILTERED/PROVISIONAL |
| support margin | m | ENGINEERING_DERIVED |
| sticking interval | s, m | GAME_ANALYSIS_DERIVED |
| joint angles/rates | rad, rad/s | ENGINEERING_DERIVED |
| modeled drive demand/power | 1, W | GAME_MODEL/ENGINEERING_DERIVED |
| GRF/COP/muscle/internal joint force | — | NOT_OBSERVABLE |


    ## TESTS

    Synthetic depth and duration; constant-velocity path; known Butterworth phase; edge behavior; zero-duration
guards; immutable trace; event-order errors; source class and unit mandatory; camera invariance.

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

    **SHIP_V1:** raw trace, defined metrics, filter/provenance/quality. **LATER:** attempt comparison and
coaching summaries. **RESEARCH:** external validation. **OUT_OF_SCOPE:** true forces and clinical interpretation.
