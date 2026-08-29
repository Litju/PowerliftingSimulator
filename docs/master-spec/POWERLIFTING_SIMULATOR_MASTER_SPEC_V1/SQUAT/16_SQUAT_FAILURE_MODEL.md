# Squat Failure Model

**Document ID:** `PSMS-SQ-16`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/14_SQUAT_CONTROL_MODEL.md`, `SQUAT/15_SQUAT_RULES.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Map historical failure scenarios and current safety behavior after repository inspection.
- Verify every safety intervention occurs only after outcome/trace freeze.
- Calibrate thresholds by deterministic load sweeps and visual review.

## PURPOSE

    Classify and detect squat-specific physical, rule, and player-control failures early enough for truthful
analysis and safe presentation without scripting failure from load.

    ## INPUTS

    Squat observation/trace, state, intent, rule result, joint demand/limits, foot/bar/saddle contacts.

    ## OUTPUTS

    Latched `SquatFailureRecord`, primary/secondary causes, failure tick/state, safety handoff, analysis message.

    ## STATE

    Per-detector persistence and extrema; latched primary cause; secondary causes; pre-failure snapshot window;
safety intervention state kept outside attempt truth.

    ## UNITS

    m, m/s, rad, rad/s, s, normalized demand; thresholds are versioned by category.

    ## COORDINATE CONVENTION

    All direction/zone tests use canonical athlete/world frames and physical bar/landmark state.

    ## EQUATIONS


Examples:

- forward/back balance loss: support margin outside edge and COM/bar velocity directed outward for `T_balance`;
- descent collapse: pelvis/bar downward speed exceeds reference envelope while Drive absent/authority saturated and
  posture/contact gates fail;
- failed reversal: legal bottom reached but no sustained upward bar/pelvis velocity before timeout under Drive;
- stall: upward velocity below threshold with high modeled demand for dwell;
- bar reversal: filtered/direct whole-bar vertical displacement decreases beyond rule margin after ascent establishment;
- trunk loss: lumbar/trunk angle exceeds physical/visual safe bound or critical joint limit is hit.

Detector confidence is deterministic state/persistence, not probability.


    ## ASSUMPTIONS

    A failure can have several causes. The first irreversible cause is primary; later rule violations remain
secondary. Safety action may alter subsequent physics only after the record is frozen.

    ## APPROXIMATIONS

    Posture/safety thresholds are game limits, not injury limits. Bar/saddle failure is a game coupling
fault. Spotter/rack rescue may use explicit noncompetition safety approximations.

    ## GAME CALIBRATIONS

    Balance dwell ~0.10–0.20 s; reversal timeout and stall dwell calibrated by load sweep; trunk limit has
warning and hard bands below ConfigurableJoint hard limit; bar reversal tolerance distinct from velocity noise. Exact
values live in versioned `SquatFailureCalibration`.

    ## NUMERICAL IMPLEMENTATION

    Evaluate post-physics after biomechanics/rules observations. Use direct state and persistence. Freeze
pre-failure ring buffer. Latch once; do not oscillate back to active. Safety controller gets a copy of frozen outcome.

    ## PSEUDOCODE

    ```text
    DetectSquatFailure(obs, state):
    candidates = []
    if support_loss_persistent(obs): candidates += BALANCE_LOSS(direction)
    if descent_collapse(obs, state): candidates += DESCENT_COLLAPSE
    if failed_reversal(obs, state): candidates += FAILED_REVERSAL
    if ascent_stall_timeout(obs, state): candidates += MID_ASCENT_STALL
    if bar_downward_reversal(obs): candidates += BAR_REVERSAL
    if trunk_limit_or_saddle_failure(obs): candidates += POSTURE_OR_BAR_LOSS
    if physical_lockout_timeout(obs): candidates += FAILED_LOCKOUT
    return latch_by_precedence_and_first_tick(candidates)
    ```

    ## UNITY MAPPING

    `SquatFailureDetector` is pure/domain code. A `SquatSafetyPresenter` may weaken drives, enable safeties,
or transition to controlled collapse after receipt of a frozen record. It cannot edit the trace or judgment.

    ## FAILURE MODES

    Detector uses phase not physics; load threshold; noisy one-tick false positive; safety catches bar before
record; failure unlatches; trunk threshold called injury risk; saddle break hidden; physical and rule reasons collapsed.

    ## OBSERVABILITY

    Detector booleans/counters, threshold versions, pre-failure trace, support/COM/bar/contact/joint state, safety
handoff time.

    ## TELEMETRY

    Primary/secondary cause, onset tick, state, load, velocity extrema, demand/saturation, contact/limit state,
time to safety.

    ## TESTS

    Every named failure; compound failure precedence; one-tick noise; warning vs hard; safety after latch; load
alone never selects outcome; unloaded collapse classified as architecture/controller fault when appropriate.

    ## MUTATION TESTS

    `if load > max then fail`; safety before latch; no persistence; call trunk angle injury; clear failure on
velocity recovery; presentation recomputes cause.

    ## PERFORMANCE CONSIDERATIONS

    Constant number of detectors and ring-buffer writes per tick; preallocate all records.

    ## CLAIM CLASSIFICATION

    Failure type is game/engine classification. No injury or biological diagnosis.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** all listed squat failures and controlled safety handoff. **LATER:** spotter variants.
**RESEARCH:** data-driven diagnostics. **OUT_OF_SCOPE:** injury prediction.
