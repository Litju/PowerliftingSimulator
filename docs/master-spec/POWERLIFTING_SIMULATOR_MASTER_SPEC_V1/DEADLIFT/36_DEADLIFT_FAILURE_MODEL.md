# Deadlift Failure Model

**Document ID:** `PSMS-DL-36`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/34_DEADLIFT_CONTROL_MODEL.md`, `DEADLIFT/35_DEADLIFT_RULES.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Map historical failure scenarios and current safety behavior after repository inspection.
- Verify every safety intervention occurs only after outcome/trace freeze.
- Calibrate thresholds by deterministic load sweeps and visual review.

## PURPOSE

    Detect deadlift-specific floor, grip, bar-path, zone-stall, reversal, lockout, and return failures while
preserving the grounded/free physical truth.

    ## INPUTS

    Deadlift observation/trace, state/commands, floor mode, bar position/velocity, grip, joint posture/demand.

    ## OUTPUTS

    Latched `DeadliftFailureRecord`, stall zone/cause, rule/control secondary reasons, safety/drop handling.

    ## STATE

    Floor-stall, below-knee, knee-level, hip-extension counters; grip slip/break; bar drift; downward reversal;
lockout and return-control; frozen pre-failure window.

    ## UNITS

    m, m/s, rad, rad/s, s, normalized demand; thresholds are versioned by category.

    ## COORDINATE CONVENTION

    All direction/zone tests use canonical athlete/world frames and physical bar/landmark state.

    ## EQUATIONS


Zone is based on bar height relative to calibrated knee proxy:

- `FLOOR`: grounded/no valid floor break;
- `BELOW_KNEE`;
- `KNEE_LEVEL`;
- `ABOVE_KNEE/HIP_EXTENSION`.

A stall is low upward bar speed under high Drive/demand for zone-specific dwell. Cannot-break-floor requires grounded
contact/clearance plus effort evidence. Bar drift is sagittal distance beyond hard bound with worsening posture/demand.
Grip failure is physical constraint slip/break, not an animation event.


    ## ASSUMPTIONS

    Stalls may recover. A downward reversal before lockout is irreversible for competition result.
A failed bar can be returned/dropped safely after the failure record.

    ## APPROXIMATIONS

    Safety can damp a post-failure drop or protect the visible character; this is explicitly outside
competition truth. Zone thresholds are calibrated to the asset.

    ## GAME CALIBRATIONS

    Floor stall dwell longest; knee/hip stalls shorter; grip warning/hard; drift warning/hard; downward
reversal tolerance excludes tiny numerical oscillation; lockout timeout after near-erect state; return-control threshold.

    ## NUMERICAL IMPLEMENTATION

    Evaluate direct bar state and grounded mode post-physics. Persist/hysteresis. Latch primary cause; record
rule violations. Safety behavior starts next tick or later after snapshot publication.

    ## PSEUDOCODE

    ```text
    DetectDeadliftFailure(obs, state):
    candidates = []
    if grip_broken_or_uncontrolled(obs): candidates += GRIP_FAILURE
    if floor_stall_timeout(obs): candidates += CANNOT_BREAK_FLOOR
    if zone_stall_timeout(obs, BELOW_KNEE): candidates += BELOW_KNEE_STALL
    if zone_stall_timeout(obs, KNEE_LEVEL): candidates += KNEE_STALL
    if zone_stall_timeout(obs, HIP_EXTENSION): candidates += HIP_EXTENSION_STALL
    if hard_bar_drift(obs): candidates += BAR_DRIFT
    if downward_reversal_before_lockout(obs): candidates += DOWNWARD_REVERSAL
    if lockout_timeout(obs): candidates += FAILED_LOCKOUT
    if early_drop_or_uncontrolled_return(obs): candidates += RETURN_FAILURE
    return latch_by_precedence_and_first_tick(candidates)
    ```

    ## UNITY MAPPING

    Pure detector; post-failure safety may reduce drive, guide fall, or damp bar only after truth freeze.
Physical floor/leg/hand contacts remain observed.

    ## FAILURE MODES

    Floor stall set by elapsed phase only; bar grounded ignored; grip animation failure without constraint;
microbounce false reversal; bar drift corrected by force before record; early drop erased by safety; sumo thresholds used.

    ## OBSERVABILITY

    Zone, floor contact/clearance, bar path/velocity, grip, posture, demand, detector counters and safety timing.

    ## TELEMETRY

    Cause/zone/onset, floor-break attempt time, velocity/distance/demand, grip, lockout and Down/return events.

    ## TESTS

    Cannot break; each zone stall; recover; grip slip; drift; downward reversal; failed lockout; early drop;
controlled return; one-tick bounce; safety ordering; no load-threshold selector.

    ## MUTATION TESTS

    Fail solely by load; floor-break phase timer; ignore grounded; safety edits trace; no hysteresis; call drift
injury; presentation selects zone.

    ## PERFORMANCE CONSIDERATIONS

    Constant number of detectors and ring-buffer writes per tick; preallocate all records.

    ## CLAIM CLASSIFICATION

    Game/engine classification; no spinal, muscular, or injury diagnosis.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** all V1 deadlift failures and safety handoff. **LATER:** variant-specific causes.
**RESEARCH:** predictive diagnostics. **OUT_OF_SCOPE:** injury prediction.
