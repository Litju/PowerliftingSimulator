# Bench Press Failure Model

**Document ID:** `PSMS-BP-26`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/24_BENCH_CONTROL_MODEL.md`, `BENCH/25_BENCH_RULES.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Map historical failure scenarios and current safety behavior after repository inspection.
- Verify every safety intervention occurs only after outcome/trace freeze.
- Calibrate thresholds by deterministic load sweeps and visual review.

## PURPOSE

    Detect bench-specific support, grip, touch, bilateral press, stall, reversal, and lockout failures, then
hand off to safe bar presentation without altering truth.

    ## INPUTS

    Bench observation/trace, state/commands, grip states, bar/chest/rack/support contacts, bilateral demand.

    ## OUTPUTS

    Latched `BenchFailureRecord`, cause hierarchy, safety catch request, player-facing explanation.

    ## STATE

    Support-contact counters; left/right grip/demand/error; off-chest and midrange stall windows; tilt/reversal;
latched result and pre-failure ring.

    ## UNITS

    m, m/s, rad, rad/s, s, normalized demand; thresholds are versioned by category.

    ## COORDINATE CONVENTION

    All direction/zone tests use canonical athlete/world frames and physical bar/landmark state.

    ## EQUATIONS


- off-chest failure: Press authorized, Drive high, bar fails to exceed touch height by margin before timeout;
- midrange stall: bar center upward velocity near zero in midrange with high demand for dwell;
- one-arm imbalance: bar-end height/velocity difference exceeds threshold and grows under asymmetric error;
- downward reversal: center and/or both endpoints descend beyond rule tolerance after press ascent established;
- loss of control: grip break/slip plus bar angular/linear state outside controlled envelope;
- failed lockout: one/both elbows remain outside lockout after timeout.


    ## ASSUMPTIONS

    A safety rack/spotter representation is necessary for presentation. It activates after the physical/rule
outcome is frozen. One-arm imbalance may recover if inside the calibrated warning window.

    ## APPROXIMATIONS

    Safety catch may use explicitly nonphysical damping/kinematic assistance after failure only.
Thresholds are gameplay safety/visual limits, not injury thresholds.

    ## GAME CALIBRATIONS

    Distinct off-chest and midrange windows; tilt warning and hard dwell; grip break state immediate hard
candidate; touch bounce/early press is primarily rule/control failure; safety capture delay kept short after latch.

    ## NUMERICAL IMPLEMENTATION

    Post-physics detector with direct bar endpoints, grip errors and joint state. Use hysteresis. On failure,
freeze trace then signal safety. Never overwrite bar state before frozen snapshot.

    ## PSEUDOCODE

    ```text
    DetectBenchFailure(obs, state):
    candidates = []
    if grip_uncontrolled(obs): candidates += LOSS_OF_CONTROL
    if off_chest_timeout(obs, state): candidates += OFF_CHEST_FAILURE
    if midrange_stall_timeout(obs, state): candidates += MIDRANGE_STALL
    if hard_bilateral_imbalance(obs): candidates += ONE_ARM_IMBALANCE
    if whole_bar_downward_reversal(obs): candidates += BAR_REVERSAL
    if lockout_timeout(obs): candidates += FAILED_LOCKOUT
    return latch_by_first_irreversible_event(candidates)
    ```

    ## UNITY MAPPING

    Pure detector; `BenchSafetyPresenter` can animate spotters or engage safety catches after latch. A physical
safety bar may already exist but its contact cannot hide an invalid attempt.

    ## FAILURE MODES

    Safety teleports before result; touch bounce mistaken for off-chest success; average bar endpoint hides
one-arm failure; grip break not recorded; rule early Press called physical failure only; detector unlatches.

    ## OBSERVABILITY

    Bar endpoint/center traces, grips, contacts, bilateral joint errors/demand, detector counters, safety state.

    ## TELEMETRY

    Cause, onset, bar height/velocity/tilt, side-specific grip and demand, support contacts, command timing,
safety capture.

    ## TESTS

    Off-chest, midrange, one-arm, downward reversal, grip loss, failed lockout, recoverable slow rep, early press
rule failure, safety ordering, compound causes.

    ## MUTATION TESTS

    Load threshold; center-only symmetry; safety before latch; ignore one grip; no dwell; call contact loss injury;
presentation decides failure.

    ## PERFORMANCE CONSIDERATIONS

    Constant number of detectors and ring-buffer writes per tick; preallocate all records.

    ## CLAIM CLASSIFICATION

    Game/engine failure categories only; no shoulder/chest injury or muscle diagnosis.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** complete bench failure/safety model. **LATER:** animated spotter richness.
**RESEARCH:** learned failure prediction. **OUT_OF_SCOPE:** injury risk.
