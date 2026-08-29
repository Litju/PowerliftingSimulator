# Squat Control Model

**Document ID:** `PSMS-SQ-14`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/13_SQUAT_REFERENCE_MOTION.md`, `05_POWERED_JOINT_MODEL.md`, `06_ATHLETE_CAPACITY_MODEL.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Map or replace current lift controller classes only after repository inspection.
- Prove with code search and mutation tests that no second force/torque/transform writer remains.
- Preserve qualified M1 rule outcomes while moving physical ownership to this controller path.

## PURPOSE

    Own all squat state transitions, interpret player intent, advance squat reference phase, request finite
capacity, and detect physical readiness/failure without applying a second torque/force layer.

    ## INPUTS

    Previous squat observation, current sampled intent, rules state, attempt clock, squat reference asset,
athlete capacity state.

    ## OUTPUTS

    Next squat state, `s_q`, corrected reference pose, activation requests, drive configuration request,
rule-relevant events, failure candidate.

    ## STATE

    The explicit squat state machine, phase/direction, command receipts, settle timer based on actual stability,
bottom/reversal/lockout persistence, stall monitor, and bounded balance integrator-free correction state.

    ## UNITS

    State/phase dimensionless; time s; target pose/rate and capacity follow their source contracts.

    ## COORDINATE CONVENTION

    Consumes lift-specific observations in canonical athlete/world frames; emits joint-frame targets only.

    ## EQUATIONS


Controller output is a reference/capacity request:

\[
\mathcal U_q = \{\mathbf q_{ref,q},\boldsymbol\omega_{ref,q},
A_{cmd,q},C_q\},
\]

not direct torque. AP/ML error is fed to reference offsets only. A stall candidate after reversal is

\[
v_b < v_{stall},\quad D>D_{min},\quad
u_{leg/trunk}\ge u_{stall}
\]

for `T_stall`, with bar height outside bottom and lockout windows. Physical failure requires a lift-specific persistence
and/or safety criterion; it is not chosen merely from load.


    ## ASSUMPTIONS

    Settle and commands matter. A stable unloaded/light squat can be controlled with the reference,
capacity, and small balance correction. The controller uses previous post-physics observation.

    ## APPROXIMATIONS

    Walkout is a bounded authored sequence. Player balance input biases correction/reference. The
controller does not reconstruct forces or solve whole-body optimization.

    ## GAME CALIBRATIONS

    Settle requires feet contact, low root/bar velocity, and COM margin for 0.25 s. Descent begins only
after Squat command plus Yield. Reversal requires actual legal depth or the state may become shallow/failure. Stall seed:
bar vertical speed <0.015 m/s for 0.35 s under strong Drive/demand. Lockout requires physical geometry/stillness before
Rack command.

    ## NUMERICAL IMPLEMENTATION

    One pre-physics update per tick. Transition guards use hysteresis/persistence. State transition events
are timestamped. Any emergency safety intervention happens after outcome/failure capture and belongs to presentation/
safety, not this controller.

    ## PSEUDOCODE

    ```text
    SquatFixedTick(previous, intent, rules, h):
    switch state:
        SETUP: if intent.confirm: state = UNRACK
        UNRACK: if bar_clear_of_hooks(previous): state = WALKOUT
        WALKOUT: if walkout_complete(previous): state = SETTLE
        SETTLE: if physically_settled(previous): request_squat_command()
        SQUAT_COMMAND:
            if rules.squat_command_received and intent.yield: state = DESCENT
        DESCENT:
            advance_s_q(intent.yield)
            if previous.depth_legal: state = BOTTOM
            elif collapse_detected(previous): fail(DESCENT_COLLAPSE)
        BOTTOM:
            if reversal_evidence(previous, intent.drive): state = REVERSAL
            elif upward_without_depth: fail(SHALLOW)
        REVERSAL: state = ASCENT when bar_velocity_up(previous)
        ASCENT:
            decrease_s_q(intent.drive)
            if stall(previous): state = STICKING
            if lockout(previous): state = LOCKOUT
        STICKING:
            if recovered(previous): state = ASCENT
            elif stall_timeout: fail(MID_ASCENT_STALL)
        LOCKOUT: request_rack_when_still()
        RACK_COMMAND:
            if rules.rack_received and intent.confirm: state = RERACK
        RERACK: if bar_secure_on_hooks(previous): state = COMPLETE

    reference = sample_squat_reference(state, s_q, intent, previous)
    capacity = request_squat_capacity(state, intent)
    return state, reference, capacity
    ```

    ## UNITY MAPPING

    Pure `SquatController` domain object called by `PhysicsTickDriver`; it writes only target/capacity records.
`SquatPhysicalAdapter` applies those to shared powered joints and the squat saddle. Rule processor is separate.

    ## FAILURE MODES

    Two controllers write hips/trunk; state advances from animation time; command and physical state conflated;
stall at bottom misclassified; shallow allowed to reverse; balance direct torque; transition oscillation; Rack auto-executes.

    ## OBSERVABILITY

    Current state, guard truth table, phase, input, command receipts, physical observation age, target/capacity
request, stall counters, transition history.

    ## TELEMETRY

    All state entry/exit times, command latency, phase trace, intent, guard failures, demand/stall, final failure
classification.

    ## TESTS

    Happy path; early descent; shallow reversal; collapse; stall/recover; stall/fail; bar reversal; lockout/Rack;
identical path with injected observations; no direct physics API; one controller writer.

    ## MUTATION TESTS

    AddTorque balance; phase from Animator; skip Squat/Rack; generic LiftController; direct load threshold;
transition on one noisy tick; currentTorque success.

    ## PERFORMANCE CONSIDERATIONS

    One state-machine and O(joints) request construction per 100 Hz tick; zero allocation; no optimization solve.

    ## CLAIM CLASSIFICATION

    Controller logic game engineering; state/rule events direct; no CNS or human motor-control claim.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** full state/intent/reference/capacity controller. **LATER:** assists and technique profiles.
**RESEARCH:** model-predictive/WBC. **OUT_OF_SCOPE:** robotics controller stack.
