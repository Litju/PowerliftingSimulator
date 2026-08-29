# Squat Physics

**Document ID:** `PSMS-SQ-12`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/11_SQUAT_BIOMECHANICS.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `05_POWERED_JOINT_MODEL.md`, `07_PHYSICAL_BARBELL_AND_EQUIPMENT.md`

## Repository verification

- Inspect current scene/prefab colliders, constraints, layers, and physical materials.
- Run spawn overlap and gravity-only fixtures before controller calibration.
- Record exact PhysX/Unity settings used in qualification.

## PURPOSE

    Define the exact rigid-body/contact/coupling system that realizes a squat and preserves load-dependent
deviation without kinematic support.

    ## INPUTS

    Physical athlete rig, bar rigid body, rack/platform, squat reference targets, finite capacities, contact
observations, setup state.

    ## OUTPUTS

    PhysX motion, foot/platform contacts, upper-back/bar coupling, rack transitions, bar and athlete state,
physics-fault events.

    ## STATE

    Both foot contacts; bar saddle contact/coupling; hook contacts; walkout foot state; physical phase;
constraint diagnostics; no hidden balance force.

    ## UNITS

    SI; physical contacts/constraint diagnostics remain engine quantities unless explicitly classified.

    ## COORDINATE CONVENTION

    Uses the canonical world, body, joint, and bar frames; all lift-specific anchors are calibrated in local coordinates.

    ## EQUATIONS


PhysX solves the coupled Newton–Euler and constraint system. The squat-specific coupling is one finite
`SquatBarSaddle` between upper thorax/back proxy and bar:

\[
\mathbf F_s \approx \operatorname{clip}(\mathbf K_x\mathbf e_x+\mathbf D_x\dot{\mathbf e}_x,F_{s,\max}),
\]
\[
\boldsymbol\tau_s \approx \operatorname{clip}(\mathbf K_r\mathbf e_r+\mathbf D_r\dot{\mathbf e}_r,\tau_{s,\max}),
\]

as a conceptual description of a compliant ConfigurableJoint. It permits small axial and rotational motion and can
break/declare failure. Gravity and physical foot reactions close the load path.


    ## ASSUMPTIONS

    One back coupling plus physical contact is a stable game approximation of bar-on-back retention.
Hands need not carry the bar mechanically in V1. Feet and platform contacts provide all external support.

    ## APPROXIMATIONS

    The saddle represents combined bar placement, upper-back contact, and grip retention. It is not
an invisible vertical support: it connects two dynamic bodies and has finite force. Walkout uses authored reference intent,
not locomotion AI.

    ## GAME CALIBRATIONS

    Saddle translational freedom/limits are small but nonzero; shaft roll allowed within a bounded range;
finite break threshold; back collider prevents visible embedding. Hook clearance and unrack height are verified.
Foot friction is high enough for plausible planted feet but finite. No foot pins.

    ## NUMERICAL IMPLEMENTATION

    Create saddle after initial-overlap check; remove hook contact by actual vertical/horizontal clearance.
Do not toggle bar kinematic. Use discrete humanoid collisions and bar CCD if qualified. Joint projection disabled.
Walkout target rate is slower than lift motion and must settle before command.

    ## PSEUDOCODE

    ```text
    SquatPrePhysics(state, reference, capacity):
    if state == UNRACK:
        set_squat_targets(reference.unrack)
        saddle.SetFiniteAuthority(capacity.trunk, capacity.grip_proxy)
    elif state == WALKOUT:
        set_squat_targets(reference.walkout)
    elif state in ACTIVE_SQUAT_STATES:
        set_squat_targets(reference.pose)
    verify_single_physical_writer()

SquatPostPhysics(snapshot):
    observe_feet_platform()
    observe_bar_back_and_hooks()
    if forbidden_penetration or saddle_broken:
        emit_physics_failure()
    ```

    ## UNITY MAPPING

    Dynamic athlete and bar; static/kinematic rack/platform; one ConfigurableJoint saddle to thorax/upper-chest
body; no hand joints; collision layers suppress bar–head artifacts while preserving upper-back contact. Rack logic observes
contacts/clearance.

    ## FAILURE MODES

    Bar depenetration launch; saddle too stiff; bar floats above back; saddle secretly infinite; feet skate;
walkout destabilizes rig; hook snags during descent; bar-body self-collision explosion; pelvis kinematic.

    ## OBSERVABILITY

    Saddle anchors/errors/force caps, bar-back penetration, hook contacts, foot contacts, solver settings, all
powered joint limits and current physical authority.

    ## TELEMETRY

    Saddle error/break, foot contact/slip, hook/rack events, bar pose, joint-limit proximity, contact counts/depth.

    ## TESTS

    Gravity drop with drives off; unloaded squat; bar remains on back under light load; saddle break mutation;
no rack contact during active descent; foot friction reduction produces visible slip; overlap-at-spawn detected separately.

    ## MUTATION TESTS

    Pin feet; make pelvis kinematic; parent bar to torso transform; add hand constraints; set saddle force infinite;
disable bar gravity; script bar path.

    ## PERFORMANCE CONSIDERATIONS

    Constant-size multibody/contact system; no online optimization. Keep foot/bar/back collider count minimal.

    ## CLAIM CLASSIFICATION

    Physical state/contact direct; saddle is `GAME_PHYSICS_APPROXIMATION`; no true back pressure, hand force,
GRF, or joint loading.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** dynamic feet/bar/athlete, one finite saddle, physical rack. **LATER:** physical hand guidance
after proof. **RESEARCH:** articulated fingers/flexible bar. **OUT_OF_SCOPE:** force-plate truth.
