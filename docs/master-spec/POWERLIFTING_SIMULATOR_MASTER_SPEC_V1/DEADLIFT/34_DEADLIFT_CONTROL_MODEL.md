# Deadlift Control Model

**Document ID:** `PSMS-DL-34`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/33_DEADLIFT_REFERENCE_MOTION.md`, `05_POWERED_JOINT_MODEL.md`, `06_ATHLETE_CAPACITY_MODEL.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Map or replace current lift controller classes only after repository inspection.
- Prove with code search and mutation tests that no second force/torque/transform writer remains.
- Preserve qualified M1 rule outcomes while moving physical ownership to this controller path.

## PURPOSE

    Own conventional deadlift setup, grip, brace, pre-tension, physical floor-break gating, pull, lockout,
Down command, and controlled return.

    ## INPUTS

    Previous deadlift observation, intent, rules state, floor/grip/bar proximity state, reference/capacity.

    ## OUTPUTS

    Deadlift state, `s_d`, slack/preload request, grip and joint capacity/activation, close-bar reference bias,
failure/rule events.

    ## STATE

    State machine; `s_d`; slack scalar; grip latch; grounded/free mode; floor-break/knee-pass/lockout events;
stall/reversal/drop monitors; Down receipt.

    ## UNITS

    State/phase dimensionless; time s; target pose/rate and capacity follow their source contracts.

    ## COORDINATE CONVENTION

    Consumes lift-specific observations in canonical athlete/world frames; emits joint-frame targets only.

    ## EQUATIONS


Deadlift request:

\[
\mathcal U_d=\{\mathbf q_{setup/pull},\mathbf p_{grips},
A_{brace/pull},C_{hip/knee/trunk/grip},a_{slack}\}.
\]

`a_slack` ramps only preparatory activation/coupling. The controller does not emit bar force. A floor-stall candidate:

\[
\text{grounded}\land D>D_{min}\land u_{\mathrm{hip/knee/trunk}}\ge u_0
\]

for `T_floor`; later stall zones use actual bar height relative to knee and low upward velocity.


    ## ASSUMPTIONS

    No start command. The athlete can set and pull when ready. Floor break is physics observation.
Lockout and Down govern completion/return.

    ## APPROXIMATIONS

    Pull slack is a game timing mechanic. Close-bar input changes bounded posture/reference.
Grip is finite. No tendon or bar elasticity is calculated.

    ## GAME CALIBRATIONS

    Brace/grip readiness before Pull; preload ramp 0.2–0.5 s; cannot advance above preload cap until
floor break. Distinct stall dwell at floor (longer), below knee, knee, and hip extension. Lockout stillness precedes Down.
Descent drive lower than ascent and bar must return under hand control.

    ## NUMERICAL IMPLEMENTATION

    State consumes post-physics grounded/clearance mode. Hysteresis prevents grounded/free chatter.
Failure latches. Down transition cannot be synthesized by presentation. Grip release after ground contact is allowed only
when velocity/contact stability pass.

    ## PSEUDOCODE

    ```text
    DeadliftFixedTick(previous, intent, rules, h):
    switch state:
        SETUP: if setup_valid(previous): state = GRIP
        GRIP: if intent.grip and grips_engaged(previous): state = BRACE
        BRACE: if brace_ready(): state = PULL_SLACK
        PULL_SLACK:
            ramp_preload(intent)
            if intent.drive: state = PULL
        PULL:
            if previous.floor_break: state = FLOOR_BREAK
            elif floor_stall_timeout(previous): fail(CANNOT_BREAK_FLOOR)
        FLOOR_BREAK: state = INITIAL_ASCENT
        INITIAL_ASCENT:
            increase_s_d(intent.drive)
            if previous.knee_pass: state = KNEE_PASS
            elif stall(previous): state = STICKING
        KNEE_PASS: state = HIP_EXTENSION
        STICKING:
            if recovered(previous): return_to_zone_state()
            elif timeout or downward_reversal: fail()
        HIP_EXTENSION:
            increase_s_d(intent.drive)
            if previous.lockout: state = LOCKOUT
        LOCKOUT: request_down_when_still()
        DOWN_COMMAND:
            if rules.down_received and intent.confirm: state = DESCENT
        DESCENT:
            decrease_s_d(intent.yield_or_drive_down)
            if stable_ground_contact(previous): state = GROUND_CONTACT
        GROUND_CONTACT: release_grips_when_safe(); state = COMPLETE

    return deadlift_reference_capacity_grip_request(...)
    ```

    ## UNITY MAPPING

    Pure `DeadliftController`; `DeadliftPhysicalAdapter` applies targets/capacity to shared athlete and
deadlift-specific grips. Contact mode observer is separate. No squat/bench subclass mechanics.

    ## FAILURE MODES

    Advance pull branch before floor break; script bar velocity; no grip readiness; floor stall treated as
instant fail; auto Down; release before ground; close-bar direct force; generic phase/state.

    ## OBSERVABILITY

    State/guards, slack, grounded/free, floor/knee/hip stall zone, intent, grip, phase, lockout/down.

    ## TELEMETRY

    Setup/slack/floor-break times, phase, bar zone, stall/recovery, grip, lockout/Down/return timing.

    ## TESTS

    Happy path; drive with no grip; insufficient floor break; floor stall; below-knee/knee/hip stalls; grip slip;
downward reversal; lockout; early Down/drop; controlled return; no direct bar write.

    ## MUTATION TESTS

    Set bar velocity on Drive; toggle kinematic; skip grip/brace; floor-break from input; auto Down; reuse squat
controller; infinite grip; release bar at lockout.

    ## PERFORMANCE CONSIDERATIONS

    One state-machine and O(joints) request construction per 100 Hz tick; zero allocation; no optimization solve.

    ## CLAIM CLASSIFICATION

    Game controller; no biological slack/CNS/internal load claim.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** full conventional deadlift control. **LATER:** style assists/separate sumo. **RESEARCH:** advanced
control. **OUT_OF_SCOPE:** robotics WBC.
