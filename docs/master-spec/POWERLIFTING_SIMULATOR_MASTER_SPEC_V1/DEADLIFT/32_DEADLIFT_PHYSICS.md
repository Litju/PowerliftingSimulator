# Deadlift Physics

**Document ID:** `PSMS-DL-32`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/31_DEADLIFT_BIOMECHANICS.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `05_POWERED_JOINT_MODEL.md`, `07_PHYSICAL_BARBELL_AND_EQUIPMENT.md`

## Repository verification

- Inspect current scene/prefab colliders, constraints, layers, and physical materials.
- Run spawn overlap and gravity-only fixtures before controller calibration.
- Record exact PhysX/Unity settings used in qualification.

## PURPOSE

    Define the deadlift-only physical topology, including the critical transition from a floor-supported bar
to a freely carried bar through finite grips.

    ## INPUTS

    Physical athlete, bar/platform, bilateral grips, conventional setup reference, capacity/intent, floor and
leg contacts.

    ## OUTPUTS

    Grounded/slack/free contact modes, physically emerging floor break, bar/athlete motion, grip failure,
controlled descent/ground contact.

    ## STATE

    Bar-floor contact manifold, two grip states, foot contacts, optional bar-leg contacts, contact-mode hysteresis,
pre-tension scalar, floor-break tick, descent and ground-contact completion.

    ## UNITS

    SI; physical contacts/constraint diagnostics remain engine quantities unless explicitly classified.

    ## COORDINATE CONVENTION

    Uses the canonical world, body, joint, and bar frames; all lift-specific anchors are calibrated in local coordinates.

    ## EQUATIONS


The floor supplies contact impulses while grounded. The player cannot command a positive bar velocity. `PULL_SLACK`
ramps activation and finite grip/reference preload:

\[
a_{slack,k+1}=\operatorname{clamp}(a_{slack,k}+r_{slack}h,0,1)
\]

while Drive is not yet permitted to advance the free-bar phase. Physical floor break occurs only when the constraint/contact
solution produces positive clearance.

Grip equations match the bench's general compliant concept but have deadlift-specific orientation, capacity, allowed
shaft rotation, and slip criteria; the implementation is independent and is not a shared lift-grip controller.


    ## ASSUMPTIONS

    Conventional stance; floor supports the loaded plates; arms transmit load; hands remain coupled until
slip/break; bar may contact shins/thighs with controlled collision filtering.

    ## APPROXIMATIONS

    Symmetric double-overhand-style V1 grip for numerical stability; pull slack is readiness/preload,
not biological slack; knurl friction and finger contact collapse into finite grip adapters; bar flex render-only.

    ## GAME CALIBRATIONS

    Grip compliance and break thresholds specific to deadlift loads; floor restitution near zero; steel/
platform and bar/leg friction calibrated to avoid bounce/sticking; pre-tension cannot create >2 mm plate clearance.
Collision filtering allows shaft/leg contact but avoids sleeve/head or plate/body artifacts.

    ## NUMERICAL IMPLEMENTATION

    Bar starts dynamic at rest on platform. Never switch body type at floor break. Use contact hysteresis and
bar clearance. Create grips after setup alignment. During descent, keep drives finite; ground completion waits for stable
plate contact. CCD if drop tests tunnel.

    ## PSEUDOCODE

    ```text
    DeadliftPrePhysics(state, reference, capacity):
    if state == PULL_SLACK:
        slack = ramp(slack, intent.grip_and_slack, h)
        grips.SetCapacity(capacity.grip * slack_grip_multiplier(slack))
        powered_athlete.SetTargets(reference.pre_tension_pose, capacity * slack_activation(slack))
    else:
        powered_athlete.SetTargets(reference.pose, capacity)
        grips.SetCapacity(capacity.grip)

DeadliftPostPhysics(snapshot):
    grounded = observe_bar_floor_contact()
    clearance = observe_plate_clearance()
    mode = contact_mode_machine.Update(grounded, clearance)
    if grip_broken or uncontrolled_drop:
        emit_physical_failure()
    ```

    ## UNITY MAPPING

    One dynamic bar Rigidbody from setup through completion; two deadlift-specific ConfigurableJoint grip
adapters; static platform; foot and optional bar-leg colliders; no rack. Contact observers publish mode events.

    ## FAILURE MODES

    Kinematic grounded bar; script toggles floor support; bar launches from initial penetration; infinite grips;
elbow flexion controller lifts artificially; floor break set by phase; bar catches leg collider; early drop presented safely
but not recorded; rebound falsely completes.

    ## OBSERVABILITY

    Floor contact points/depth, plate clearance, grip anchors/caps/slip, foot contacts, bar-leg contacts, body/bar
velocities, collision mode and CCD.

    ## TELEMETRY

    Grounded/free transition, floor contacts, grip state, bar-leg distance/contact, rebound, descent contact,
constraint settings.

    ## TESTS

    Bar remains grounded with no drive; inadequate athlete cannot break floor; sufficient athlete does; no
kinematic transition; low grip slips; contact hysteresis; controlled return; high-speed drop no tunneling; initial overlap
fault isolated.

    ## MUTATION TESTS

    Set bar kinematic while grounded; call MovePosition; set velocity at floor break; remove gravity; infinite grip;
disable floor contact; fake floor-break event from input; parent bar to hands.

    ## PERFORMANCE CONSIDERATIONS

    Constant-size multibody/contact system; no online optimization. Keep floor and leg contacts simple; profile grounded and impact phases.

    ## CLAIM CLASSIFICATION

    Contact/pose direct; grip and slack game approximation; no true grip force, floor reaction, bar strain,
or tissue loading.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** dynamic grounded/free bar, finite grips, physical return. **LATER:** separate sumo and grip
styles. **RESEARCH:** bar flex and detailed contact. **OUT_OF_SCOPE:** biological slack/tendon model.
