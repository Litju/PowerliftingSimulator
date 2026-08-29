# Coordinates, Units, Time, and Numerical Contract

**Document ID:** `PSMS-03`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `00_READ_ME_FIRST.md`, `02_GAME_ARCHITECTURE.md`

## Repository verification

- Confirm the imported humanoid faces canonical +Z in its authoring/physics bind pose; otherwise record the explicit bind correction.
- Confirm Unity project `Time.fixedDeltaTime`, `Physics.autoSimulation`, solver iteration settings, and any existing manual-simulation code.
- Measure all critical collider dimensions and initial clearances in the actual scene.

## PURPOSE


Freeze one mathematical language for the whole product. Every simulation, rule, telemetry,
replay, and presentation system must be able to state which frame, unit, time domain, tolerance
class, and source class it uses. This contract prevents the historical class of sign, axis, degree/radian,
duplicate-step, and tolerance-leak bugs.


    ## INPUTS


- Unity transforms, rigid-body states, collider contacts, and configured joint frames.
- Lift-specific reference states.
- Buffered player intent sampled from the Input System.
- Fixed-step duration `h`.
- Authoring values expressed in inspector-friendly units.


    ## OUTPUTS


- Canonical world/body/joint/reference-frame transforms.
- Fixed-step simulation timestamps and monotonically increasing tick indices.
- Explicit conversion functions between degrees and radians.
- Named tolerance records.
- Render interpolation factors and immutable observation timestamps.


    ## STATE


`SimulationClock { tick: ulong, time_s: double, h_s: double }`; one immutable
`FrameConvention`; one `ToleranceSet`; previous/current simulation snapshots for render interpolation;
and an accumulator owned only by `PhysicsTickDriver`.


    ## UNITS


SI internally:

| Quantity | Unit |
|---|---:|
| distance | m |
| time | s |
| mass | kg |
| force | N |
| torque/moment | N·m |
| linear velocity | m/s |
| linear acceleration | m/s² |
| angle | rad |
| angular velocity | rad/s |
| angular acceleration | rad/s² |
| power | W |
| work/energy | J |

Degrees are allowed only in authoring data, human-readable debugging, and design tables.
Every boundary conversion uses `rad = deg × π/180` or `deg = rad × 180/π`.


    ## COORDINATE CONVENTION


Unity world is right-handed for cross-product reasoning but uses Unity's conventional axes:

- `+Y`: up.
- `+Z`: athlete-facing forward from the platform toward the head judge.
- `+X`: athlete's right.
- Sagittal anterior/posterior coordinate: `z`.
- Frontal medial/lateral coordinate: `x`.

Frames are named, never implied:

- `W`: world/platform frame.
- `B_i`: rigid-body frame of segment `i`, origin at its rigid-body center of mass.
- `J_i`: joint frame, with primary twist/hinge axis defined during the convention fixture.
- `R_i`: reference-rig bone frame.
- `M`: visible mesh/root frame.
- `BAR`: barbell body frame, longitudinal axis along `+X` in its neutral rack pose.

A transform is written `T_A_from_B`; it maps a vector represented in `B` into `A`.
Quaternions are normalized and their sign is canonicalized before shortest-arc comparison.

Joint-positive conventions for authored scalar summaries are:

| Family | Positive scalar convention |
|---|---|
| ankle | dorsiflexion |
| knee | flexion |
| hip | flexion |
| lumbar/trunk | trunk flexion relative to pelvis |
| shoulder | humeral flexion in the lift's sagittal working plane |
| elbow | flexion |
| wrist | extension |

The actual three-axis joint error is computed in each joint's calibrated local frame; the scalar
conventions above are analysis/reporting conventions, not a substitute for the full orientation.


    ## EQUATIONS


For rigid body `i`, PhysX conceptually realizes

\[
m_i \dot{\mathbf v}_i = \sum \mathbf F_i,\qquad
\mathbf I_i\dot{\boldsymbol\omega}_i +
\boldsymbol\omega_i \times (\mathbf I_i\boldsymbol\omega_i)
= \sum \boldsymbol\tau_i.
\]

The system center of mass is

\[
\mathbf c = \frac{\sum_i m_i\mathbf c_i + m_b\mathbf c_b}{\sum_i m_i+m_b},
\]

where the bar is included only for system-level loaded balance metrics.

A point transform is

\[
{}^W\mathbf p = {}^W\mathbf R_B\,{}^B\mathbf p + {}^W\mathbf t_B.
\]

Render interpolation between two recorded states is

\[
\alpha=\operatorname{clamp}\left(\frac{t_r-t_k}{h},0,1\right),\quad
\mathbf x_r=(1-\alpha)\mathbf x_k+\alpha\mathbf x_{k+1},
\]

with quaternion `slerp` for orientation. Interpolation never feeds back into simulation.

Finite differences used only when no direct engine velocity exists:

\[
v_k=\frac{x_k-x_{k-1}}{h},\qquad
a_k=\frac{v_k-v_{k-1}}{h}.
\]

Analysis derivatives use a separately specified filter and must not be confused with control state.


    ## ASSUMPTIONS


- Platform scale is `1 Unity unit = 1 m`.
- Gravity is `(0, -9.81, 0) m/s²` unless a build-specific platform test proves another configured value.
- The shipped desktop simulation can sustain a 100 Hz physics step.
- PhysX is the runtime forward-dynamics and constraint-solver authority.
- Real-time floating-point execution is not bit-identical across all hardware; deterministic here means
  deterministic scenario construction, ownership, inputs, seeds, and acceptance—not cross-platform
  lockstep identity.


    ## APPROXIMATIONS


- Human segments are rigid.
- Contact is discrete/compliant and solved approximately.
- Render interpolation is visual smoothing, not continuous physics.
- Joint scalar angles are projections for analysis; they do not fully describe 3D orientation.
- Rule geometry uses stable virtual landmarks attached to calibrated bones rather than deforming skin vertices.


    ## GAME CALIBRATIONS


Initial shipping values:

- Physics step `h = 0.010 s` (100 Hz).
- Maximum catch-up: 4 ticks per rendered frame in normal play; a slower-than-real-time diagnostic mode may
  process more without changing recorded simulation time.
- Gravity `9.81 m/s²`.
- Rule geometric margin: `5 mm` unless a lift rule file defines a more conservative named margin.
- Contact persistence: generally 2–3 consecutive physics ticks.
- Visual landmark tolerance: `10 mm`.
- Numerical quaternion normalization threshold: `1e-6`.
- No single global epsilon. Numerical, physics, rule, visual, and gameplay tolerances are separate named fields.


    ## NUMERICAL IMPLEMENTATION


The default shipping mode is an isolated local `PhysicsScene` with automatic simulation disabled for that
scene. A single `PhysicsTickDriver` consumes real-time accumulation and calls `PhysicsScene.Simulate(h)`.
No other `FixedUpdate`, coroutine, test harness, or presentation component may call a physics step.

Use continuous collision detection only for the bar and any small/fast equipment where tunneling tests justify it.
Humanoid segments normally use discrete collision with conservative collider sizing. Enable interpolation on
neither the simulation rigid bodies nor the reference rig; render smoothing comes from snapshots.

Solver iterations are tuned per athlete/bar rigid body after mass and joint fixtures pass. Start with project defaults,
then increase only the bodies whose joint/contact residuals require it. Projection is disabled in normal lift motion;
if used as an emergency break-glass safety, it must terminate the attempt as a physics fault because projection is
nonphysical.

Reset is transactional: disable scene consumption, remove/recreate transient constraints, restore authoritative
states and velocities, clear contacts and input buffers, simulate zero ticks, then verify overlap/clearance invariants.


    ## PSEUDOCODE

    ```text
    RenderUpdate(real_dt):
    accumulator = min(accumulator + real_dt, MAX_ACCUMULATED_TIME)
    ticks = 0
    while accumulator >= H and ticks < MAX_CATCHUP_TICKS:
        tick_driver.StepOne(H)
        accumulator -= H
        ticks += 1
    alpha = clamp(accumulator / H, 0, 1)
    presentation.RenderInterpolated(previous_snapshot, current_snapshot, alpha)

StepOne(h):
    require caller == PhysicsTickDriver
    simulation_clock.Advance(h)
    fixed_pipeline.PrePhysics(simulation_clock)
    local_physics_scene.Simulate(h)
    fixed_pipeline.PostPhysics(simulation_clock)
    ```

    ## UNITY MAPPING


- `Physics.autoSimulation = false` only when project-wide ownership is intentionally migrated; preferred V1 is a
  dedicated additive scene and its `PhysicsScene`.
- `PhysicsScene.Simulate(h)` is called by one authority.
- `Rigidbody.position`, `rotation`, `velocity`, and `angularVelocity` are sampled after the step.
- `Transform.InverseTransformDirection`, `Quaternion.Inverse`, and explicitly calibrated basis quaternions provide
  frame conversion.
- Script execution order is asserted by tests rather than relied on informally.
- `Application.onBeforeRender` may improve display latency but may not mutate simulation.


    ## FAILURE MODES


Frame/sign mismatch; imported asset scale error; degree/radian contamination; duplicate physics stepping;
render state fed back into physics; unstable large `h`; runaway catch-up; unbounded collision penetration;
quaternion wrap discontinuity; rule thresholds using a numerical epsilon; replay timestamps not monotonic;
reset with stale contacts or velocities.


    ## OBSERVABILITY


Every snapshot records `tick`, `simulation_time_s`, world root pose, segment poses and velocities, bar pose and
velocity, phase, and active tolerance-set version. Debug overlays can draw all frames, joint axes, COM, support
polygon, landmark points, contacts, and bar path. A step-owner assertion records the caller and throws in
development builds on duplicate ownership.


    ## TELEMETRY


Normal: tick/time, phase, bar pose/velocity, root/COM pose, outcome events.  
Diagnostic: per-joint frame error, contact count/depth, constraint flags, catch-up ticks, solver settings.  
Research: optional high-rate full segment state and contact manifold export. Research telemetry is disabled by default.


    ## TESTS


- Unit conversion round trips.
- Known parent/child/joint frame fixtures in six cardinal orientations.
- Shortest-arc quaternion error across `±π`.
- Exactly 100 steps advance simulation time by 1.000 s.
- Duplicate step owner is rejected.
- Reset produces byte-stable authored configuration and tolerance versions.
- Render interpolation does not mutate physics state.
- Scene scale fixture: a 2.2 m bar measures 2.2 Unity units.
- Performance overload invokes bounded slow-frame policy rather than silently changing `h`.


    ## MUTATION TESTS


Swap `+Z/-Z`; feed degrees into radian API; enable automatic simulation while manual stepping remains; let a
second component call `Simulate`; share the rule margin as `Mathf.Epsilon`; remove quaternion normalization;
restore position but not velocity on reset. Each mutation must fail at least one dedicated test.


    ## PERFORMANCE CONSIDERATIONS


100 Hz is a design target, not permission for unbounded work. Preallocate snapshots; avoid LINQ and managed
allocation in the fixed pipeline; batch frame conversions; cache static bone/joint transforms; keep diagnostic
drawing out of release builds; profile p50/p95/p99 physics cost separately from rendering.


    ## CLAIM CLASSIFICATION


Rigid-body equations and SI units: `SOURCE_DIRECT` engineering mechanics.  
Exact realized PhysX behavior: `ENGINE_RUNTIME_OBSERVATION`.  
Fixed step and tolerance values: `GAME_CALIBRATION`.  
Cross-platform bit determinism: explicitly **not claimed**.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** contract above, 100 Hz isolated manual scene, snapshot interpolation, named tolerance classes.  
**LATER:** asynchronous replay compression, platform-specific tuning profiles.  
**RESEARCH:** deterministic lockstep or alternate solvers.  
**OUT_OF_SCOPE:** custom general rigid-body integrator and cross-hardware bitwise determinism.
