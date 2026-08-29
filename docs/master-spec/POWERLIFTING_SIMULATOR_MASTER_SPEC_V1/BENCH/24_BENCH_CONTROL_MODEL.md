# Bench Press Control Model

**Document ID:** `PSMS-BP-24`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/23_BENCH_REFERENCE_MOTION.md`, `05_POWERED_JOINT_MODEL.md`, `06_ATHLETE_CAPACITY_MODEL.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Map or replace current lift controller classes only after repository inspection.
- Prove with code search and mutation tests that no second force/torque/transform writer remains.
- Preserve qualified M1 rule outcomes while moving physical ownership to this controller path.

## PURPOSE

    Own the bench-only sequence from setup through rack, coordinate bilateral reference/capacity and compliant
grips, and keep touch/pause/commands physically grounded.

    ## INPUTS

    Previous bench observation, intent, rules state, grip/bench contacts, reference and capacity profiles.

    ## OUTPUTS

    Bench state, `s_b`, setup/arm/hand targets, grip capacity, press activation, rule/failure events.

    ## STATE

    Setup/arch/grip selections; state machine; Start/Press/Rack receipts; touch and stillness dwell; left/right
drive balance; stall and downward-reversal monitors.

    ## UNITS

    State/phase dimensionless; time s; target pose/rate and capacity follow their source contracts.

    ## COORDINATE CONVENTION

    Consumes lift-specific observations in canonical athlete/world frames; emits joint-frame targets only.

    ## EQUATIONS


Bench control request:

\[
\mathcal U_b=\{\mathbf q_{torso/legs},\mathbf q_{arms},
\mathbf p_{grip,L/R},A_{press,L/R},C_{shoulder/elbow/grip}\}.
\]

Player Balance biases left/right activation within a small zero-sum bound:

\[
A_L=\operatorname{clip}(A-\delta_A),\quad
A_R=\operatorname{clip}(A+\delta_A),
\]

so it cannot create net capacity. Touch/pause/command gates freeze `s_b` while physics remains active.


    ## ASSUMPTIONS

    Setup is complete before Start. Physical touch and stillness are required. Bilateral imbalance should
remain possible. Two grip constraints are already the physical authority.

    ## APPROXIMATIONS

    Leg drive changes setup/stability/capacity request; no direct bar force. A small player symmetry
bias is game control, not literal independent neural drive.

    ## GAME CALIBRATIONS

    Setup settles 0.25 s. Start requires required contacts and locked elbows. Touch freezes descent.
Press command is issued only after valid touch/elbow depth/stillness. Off-chest stall window differs from midrange;
whole-bar downward reversal is detected from both endpoints/center with tolerance. Lockout requires both elbows.

    ## NUMERICAL IMPLEMENTATION

    Use previous observation; state guards persistent. Grip capacity is set before PhysX with no mode changes.
After a physical grip/bar failure, latch failure; presentation safety may catch bar later. Press-phase reference cannot
advance before command.

    ## PSEUDOCODE

    ```text
    BenchFixedTick(previous, intent, rules, h):
    switch state:
        SETUP: update_setup_intent()
        ARCH_POSITION: if setup_stable(previous): state = GRIP
        GRIP: if grips_engaged(previous): state = UNRACK
        UNRACK: if bar_clear_of_hooks(previous): state = START_POSITION
        START_POSITION: if start_ready(previous): request_start_command()
        START_COMMAND:
            if rules.start_received and intent.yield: state = DESCENT
        DESCENT:
            increase_s_b(intent.yield)
            if valid_touch(previous): state = CHEST_TOUCH
        CHEST_TOUCH: state = PAUSE
        PAUSE:
            hold_reference()
            if pause_legal(previous): request_press_command()
        PRESS_COMMAND:
            if rules.press_received and intent.drive: state = PRESS
        PRESS:
            decrease_s_b(intent.drive)
            if off_chest_or_mid_stall(previous): state = STICKING
            if bilateral_lockout(previous): state = LOCKOUT
        STICKING:
            if recovered(previous): state = PRESS
            elif failure_timeout or downward_reversal: fail()
        LOCKOUT: request_rack_when_still()
        RACK_COMMAND:
            if rules.rack_received and intent.confirm: state = RERACK
        RERACK: if bar_secure(previous): state = COMPLETE

    return bench_reference_and_capacity_request(...)
    ```

    ## UNITY MAPPING

    Pure `BenchController`; applies through `BenchPhysicalAdapter`, shared powered-joint API, and bench-specific
grip adapters. No SquatController inheritance beyond lifecycle interface.

    ## FAILURE MODES

    Pause from timer only; Start/Press/Rack ignored; bar motion frozen kinematically; left/right correction
adds capacity; touch trigger moves bar; grip writer competes with arm controller; generic lift phases.

    ## OBSERVABILITY

    State/guards, setup contacts, command receipts, `s_b`, left/right activation bias, touch/pause/stall counters,
grip status.

    ## TELEMETRY

    Setup duration, Start/Press/Rack timing, touch/pause, phase, bilateral intent/demand, stall location, failure.

    ## TESTS

    Happy path; invalid contacts; early descent; invalid elbow depth; moving pause; early press; off-chest stall;
one-arm imbalance; downward reversal; unilateral lockout; early rack; grip failure; no direct bar API.

    ## MUTATION TESTS

    Auto-Press on touch; freeze bar at pause; AddForce leg drive; average elbows; infinite grip; generic controller;
one-side correction increases total activation.

    ## PERFORMANCE CONSIDERATIONS

    One state-machine and O(joints) request construction per 100 Hz tick; zero allocation; no optimization solve.

    ## CLAIM CLASSIFICATION

    Game controller and rule gates; no biological motor-control or shoulder-force claim.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** complete bench state/control. **LATER:** setup assists/styles. **RESEARCH:** advanced bilateral
control. **OUT_OF_SCOPE:** clinical motor model.
