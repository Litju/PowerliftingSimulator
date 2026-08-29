# Bench Press Physics

**Document ID:** `PSMS-BP-22`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/21_BENCH_BIOMECHANICS.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `05_POWERED_JOINT_MODEL.md`, `07_PHYSICAL_BARBELL_AND_EQUIPMENT.md`

## Repository verification

- Inspect current scene/prefab colliders, constraints, layers, and physical materials.
- Run spawn overlap and gravity-only fixtures before controller calibration.
- Record exact PhysX/Unity settings used in qualification.

## PURPOSE

    Define the bench-only constrained multibody system: body supported by bench/floor while two compliant
hands mechanically carry one free bar.

    ## INPUTS

    Physical athlete, bench/floor/rack, bar, bilateral grip adapter targets/capacities, bench reference pose,
required contacts.

    ## OUTPUTS

    Bar/arm/body motion, bench/foot contacts, grip slip/break, unrack/rerack transitions, physical failure.

    ## STATE

    Bench support contacts, two foot contacts, left/right grip constraint state, rack-hook contacts, bar touch,
grip compliance and break accumulation.

    ## UNITS

    SI; physical contacts/constraint diagnostics remain engine quantities unless explicitly classified.

    ## COORDINATE CONVENTION

    Uses the canonical world, body, joint, and bar frames; all lift-specific anchors are calibrated in local coordinates.

    ## EQUATIONS


Each grip is a finite compliant six-DOF adapter with:

- limited/free rotation about the bar shaft;
- small axial slide;
- finite radial position drive;
- finite wrist-orientation drive.

Conceptually for hand `s`:

\[
\mathbf F_{g,s}=\operatorname{clip}(\mathbf K_g\mathbf e_{p,s}+
\mathbf D_g\dot{\mathbf e}_{p,s},F_{g,s}^{max}),
\]

\[
\boldsymbol\tau_{g,s}=\operatorname{clip}(\mathbf K_{gr}\mathbf e_{r,s}+
\mathbf D_{gr}\dot{\mathbf e}_{r,s},\tau_{g,s}^{max}).
\]

Both are applied through PhysX constraints. Compliance prevents a mathematically rigid loop among bar, two arms,
torso, and bench.


    ## ASSUMPTIONS

    Bench and floor are stable external supports. A simplified thorax/pelvis body can maintain authored
arch with finite drives. Two hands must carry the bar physically; visual IK alone is insufficient.

    ## APPROXIMATIONS

    Grip adapters combine hand/finger closure and friction. Bench pad compliance is represented by
contact material/collider configuration rather than soft-body deformation. Shoulder blade motion is visual/reference-level.

    ## GAME CALIBRATIONS

    Grip radial compliance small enough to look connected, large enough to avoid solver fighting;
shaft twist free or low-resistance; axial slide limited to keep grip width. Grip break/slip scales with grip attribute.
Bench friction prevents gross body slide but remains finite. Rack hooks have generous verified clearance.

    ## NUMERICAL IMPLEMENTATION

    Create grips only after hands and bar are aligned without overlap. Never use projection to keep hands
on the bar. Configure both adapters symmetrically, then test asymmetrical perturbations. Bench/body contacts use simple
shapes. On grip break, transition to controlled failure/safety without retroactively changing result.

    ## PSEUDOCODE

    ```text
    BenchPrePhysics(state, reference, capacity):
    grips.SetTargets(reference.hand_anchors)
    grips.SetFiniteCapacity(capacity.left_grip, capacity.right_grip)
    powered_athlete.SetTargets(reference.body_and_arm_pose, capacity)

BenchPostPhysics(snapshot):
    contacts = observe_bench_feet_bar_rack()
    left = left_grip.ObserveSlipAndBreak()
    right = right_grip.ObserveSlipAndBreak()
    if bar_uncontrolled or both_grips_broken:
        emit_physical_failure()
    ```

    ## UNITY MAPPING

    Two ConfigurableJoint grip adapters between hand bodies and the one bar Rigidbody; translational and
rotational degrees explicitly configured. Bench/platform static colliders. Bar rack contacts physical. Visual two-bone IK
targets recorded bar grip points after physics.

    ## FAILURE MODES

    Overconstrained explosion; grips created while misaligned; one arm receives all bar mass due to anchor
error; infinite grip; bar clips chest; bench collider ejects pelvis; rack catches sleeve; foot contact hidden; safety
teleports bar before outcome.

    ## OBSERVABILITY

    Grip error/limits/caps, bar-hand anchors, bench and foot contacts, bar/rack/chest contacts, body penetration,
constraint projection flags.

    ## TELEMETRY

    Grip slip/break, left/right error/demand, contact state, bar tilt/path, rack events, chest penetration depth.

    ## TESTS

    Bar hangs from both hands; remove one grip and observe asymmetric load; low grip causes slip; high but finite
grip succeeds; bench supports body with drives reduced; projection disabled; grip initial alignment invariant; rack/unrack.

    ## MUTATION TESTS

    Parent bar to hands; use one kinematic bar; lock every grip DOF; enable projection; infinite break force;
visual IK drives hands physically; apply hidden bench support force.

    ## PERFORMANCE CONSIDERATIONS

    Constant-size multibody/contact system; no online optimization. Keep grip and support contact count minimal; profile solver cost under asymmetry.

    ## CLAIM CLASSIFICATION

    Physical bar/grip/contact runtime direct; grip parameters game calibration; no true hand force, bench
reaction, scapular mechanics, or shoulder load.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** compliant bilateral grips, physical bench/feet/rack. **LATER:** grip-width variants and safety
spotter presentation. **RESEARCH:** hand contact mechanics. **OUT_OF_SCOPE:** soft pad/body deformation.
