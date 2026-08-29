# Physical Barbell and Competition Equipment

**Document ID:** `PSMS-07`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `03_COORDINATES_UNITS_NUMERICS.md`, `02_GAME_ARCHITECTURE.md`

## Repository verification

- Verify dimensions against the current IPF technical rules and the exact equipment modeled in the scene.
- Inspect existing bar/plate/rack/bench prefabs and remove duplicate rigid bodies.
- Run initial-overlap reports for every lift setup and plate configuration.

## PURPOSE


Define one authoritative physical barbell and stable competition equipment that participate in contact and load transfer
without double physics or presentation-driven bar motion.


    ## INPUTS


Attempt load; plate inventory; rack/bench/platform configuration; lift setup pose; grip/back coupling requests;
collision material calibration; rulebook version.


    ## OUTPUTS


One bar rigid-body state; compound collision geometry; mass and inertia; plate visual arrangement; rack/floor/bench
contacts; equipment events; authoritative bar landmarks for rules and telemetry.


    ## STATE


`BarbellState`: one Rigidbody, shaft/sleeve/collar compound colliders, loaded mass, inertia, pose, velocity, contact mode,
rack state, floor state, and lift-specific coupling references. Plate meshes and optional flex are render children only.
Equipment objects are classified as physical static colliders, interactive supports, or presentation-only.


    ## UNITS


Dimensions m; mass kg; inertia kg·m²; contact force is not published as true force unless directly available and validated;
bar displacement m; velocity m/s; angular velocity rad/s.


    ## COORDINATE CONVENTION


The neutral bar longitudinal axis is `+X_BAR`; sleeve endpoints and knurl/ring landmarks are fixed offsets in `BAR`.
World vertical is `+Y`; bar height is its shaft center `y` unless a rule metric specifies another landmark.


    ## EQUATIONS


Total bar mass:

\[
m_b = m_{\mathrm{shaft+collars}}+\sum_p m_p.
\]

A compound inertia is computed by summing each primitive/plate contribution with the parallel-axis theorem:

\[
\mathbf I_{\mathrm{bar}} = \sum_k
\left[\mathbf R_k\mathbf I_{k,C}\mathbf R_k^T
+m_k(\|\mathbf d_k\|^2\mathbf 1-\mathbf d_k\mathbf d_k^T)\right].
\]

Bar linear work estimate:

\[
W_{\mathrm{bar},g} = m_b g (y_2-y_1),
\]

labeled an external-load gravitational potential-energy change, not total athlete work.


    ## ASSUMPTIONS


- A single rigid bar is sufficient for V1 mechanics.
- Plate loading changes total mass and inertia.
- Rack, platform, and bench can use stable static/kinematic equipment colliders.
- Physical bar flex is unnecessary for V1 gameplay.


    ## APPROXIMATIONS


Current IPF-style seed geometry: total bar length no more than 2.2 m; shaft diameter 28–29 mm; collar-face spacing
1.31–1.32 m; sleeve diameter 50–52 mm; bar plus collars 25 kg; maximum plate diameter 0.45 m; each collar 2.5 kg.
Exact V1 values are frozen only after rulebook/equipment verification. Plate holes/grooves are visual; colliders use
simple cylinders. Bar flex is a render-only deformation driven from load/acceleration and never affects physics or rules.


    ## GAME CALIBRATIONS


- Shaft compound collider segmented only as needed for stable rack/hand/back contact.
- Dynamic friction/restitution are low and calibrated per surface class: steel/rack, steel/platform, shoe/platform,
  body/bench.
- Bar CCD: continuous dynamic for high-speed failure/drop tests if discrete tests tunnel.
- Initial bar-to-body separation must exceed the named spawn clearance before constraints are created.
- Plate arrangement is symmetric within `1 g` and `1 mm` visual tolerance.


    ## NUMERICAL IMPLEMENTATION


Set all mass/collider geometry before the first physics step. Never place a bar intersecting the torso/rack and rely on
depenetration. Couplings are created only after overlap validation. Plate visuals can be rebuilt between attempts, not
during a lift. Collision layers prevent irrelevant plate-plate/self collisions inside the single bar assembly.


    ## PSEUDOCODE

    ```text
    ConfigureBar(load_kg, inventory, lift_setup):
    plate_plan = loading_solver.solve(load_kg, inventory, base_bar_kg=25)
    assert plate_plan.symmetric
    bar.mass = plate_plan.total_mass
    bar.inertia = compound_inertia(shaft, collars, plate_plan)
    rebuild_plate_visuals(plate_plan)
    place_bar_at_verified_setup(lift_setup)
    clear_velocities(bar)
    assert no_forbidden_overlap(bar, athlete, equipment)
    set_contact_mode(lift_setup.initial_bar_mode)

UpdateRenderFlex(snapshot):
    visual_flex = calibrated_visual_curve(snapshot.load, snapshot.bar_acceleration)
    deform_render_children_only(visual_flex)
    ```

    ## UNITY MAPPING


- One `Rigidbody` on the bar root.
- Primitive `CapsuleCollider`/`BoxCollider`/`MeshCollider` only if convex and justified; cylinder approximations may use
  capsules or authored convex primitives.
- Plate meshes/collars are child renderers with no Rigidbody.
- Rack hooks, safeties, bench, and platform use explicit collision layers and materials.
- Lift-specific grip/back adapters reference the same bar body.


    ## FAILURE MODES


Duplicate bar rigid bodies; asymmetric loading; incorrect total mass; bar begins interpenetrating torso/rack; tunneling;
bar collider catches clothing/mesh proxy; overly bouncy rack/floor; hidden kinematic bar; scripted velocity; visual flex
changes authoritative shaft landmarks; closed-loop overconstraint through both hands and torso/equipment.


    ## OBSERVABILITY


Debug view shows compound colliders, COM, inertia axes, sleeve/ring landmarks, contact points, penetration, rack/floor
state, plate plan, and active couplings. Spawn report separates geometry faults from controller faults.


    ## TELEMETRY


Load, bar pose/velocity/acceleration estimate, contact/rack state, grip/back coupling state, plate plan, and any collision
fault. Contact forces are engine diagnostics unless separately validated.


    ## TESTS


- Loading solver creates exact symmetric legal load.
- Mass/inertia change when plates change.
- Exactly one authoritative Rigidbody exists.
- Bar rests stably on floor and hooks.
- No initial forbidden overlap in each lift setup.
- High-speed failure test does not tunnel.
- Removing plate visuals does not change physics.
- Render flex cannot change rule/telemetry landmarks.
- Reset restores identical bar state and plate plan.


    ## MUTATION TESTS


Add Rigidbody to sleeves; set bar kinematic; directly set bar velocity from lift phase; ignore plate inertia; spawn inside
torso; allow visual flex transform to move physics; load plates asymmetrically; let presentation own rack state.


    ## PERFORMANCE CONSIDERATIONS


One dynamic compound rigid body is cheap. Keep collider count low; cache plate plans; rebuild visuals outside active
simulation; avoid continuous collision on every humanoid segment.


    ## CLAIM CLASSIFICATION


Rule dimensions: `SOURCE_DIRECT` only after current rulebook verification. Runtime mass/pose: `SOURCE_DIRECT`.
Compound inertia: `ENGINEERING_DERIVED`. Render flex: `PRESENTATION_CALIBRATION`. True bar strain/stress: not claimed.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** one rigid bar, symmetric plates, rack/platform/bench, physical contacts.  
**LATER:** different certified bars/equipment and safety hardware.  
**RESEARCH:** flexible-body bar model.  
**OUT_OF_SCOPE:** finite-element bar/plate deformation.
