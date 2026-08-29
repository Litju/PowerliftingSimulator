# Finite Powered Joint Model

**Document ID:** `PSMS-05`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `03_COORDINATES_UNITS_NUMERICS.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`

## Repository verification

- Run isolated ConfigurableJoint convention fixtures in the exact Unity/PhysX version.
- Measure Slerp and X/YZ drive response under known inertia, spring, damper, maximumForce, and timestep.
- Verify whether any existing project code reads `currentTorque` and remove it from athlete utilization truth.

## PURPOSE


Turn lift-specific target pose intent into finite, diagnosable physical authority without claiming that Unity's
implicit joint drive is an exact biological torque source.


    ## INPUTS


Current/target joint orientations; current/target relative angular velocity; athlete capacity; activation; joint
family parameters; phase-specific target-rate limits; joint limit state.


    ## OUTPUTS


Configured `targetRotation`, `targetAngularVelocity`, drive spring/damper/maximumForce, conceptual requested torque,
normalized demand proxy, saturation state, and joint diagnostic record.


    ## STATE


Per powered joint: calibrated zero orientation; selected drive mode; target orientation/rate; `Kp`, `Kd`,
`maximumForce`; capacity; demand and tracking-error histories; limit proximity; saturation dwell. No integral state
exists in V1.


    ## UNITS


Orientation rad/quaternion; angular velocity rad/s; conceptual torque and `maximumForce` N·m for finite force drives;
spring/damper values use Unity's documented drive semantics and are treated as engine parameters, not directly
identified biological stiffness/damping.


    ## COORDINATE CONVENTION


All errors are computed in calibrated joint coordinates. For scalar hinge-dominant joints, the primary axis is
validated by a positive-pulse fixture. For ball/trunk/shoulder joints, shortest-arc quaternion error is used.


    ## EQUATIONS


Conceptual model only:

\[
\mathbf e_i = \operatorname{Log}\left(
\mathbf q_{i,\mathrm{target}}\mathbf q_{i,\mathrm{actual}}^{-1}\right),
\]

\[
\boldsymbol\tau_{i,\mathrm{des}} =
\mathbf K_{p,i}\mathbf e_i +
\mathbf K_{d,i}(\boldsymbol\omega_{i,\mathrm{target}}-
\boldsymbol\omega_{i,\mathrm{rel}}),
\]

\[
\tau_{i,\mathrm{available}} = C_i A_i,\qquad
\boldsymbol\tau_{i,\mathrm{concept}} =
\operatorname{clip}_{\tau_{i,\mathrm{available}}}
(\boldsymbol\tau_{i,\mathrm{des}}).
\]

The demand proxy is

\[
u_i = \operatorname{clamp}
\left(\frac{\|\boldsymbol\tau_{i,\mathrm{des}}\|}
{\max(\tau_{i,\mathrm{available}},\epsilon_\tau)},0,u_{\max}\right).
\]

This is **not** asserted to be the torque PhysX applies. It provides a deterministic command-side measure.


    ## ASSUMPTIONS


- Finite position/velocity drives are sufficient to produce V1 motion.
- Unity's force drive (`useAcceleration = false`) makes load and body mass relevant.
- Target motion changes are bounded and authored; the controller is not asked to track discontinuous poses.
- One system owns all drive fields.


    ## APPROXIMATIONS


The implicit constraint drive, solver iterations, contacts, mass distribution, and timestep interact. Therefore a raw
explicit PD equation is explanatory and useful for tests, but it is not a numerical reconstruction of internal PhysX
torque. `ConfigurableJoint.currentTorque` is diagnostic solver output only and not athlete torque/utilization truth.


    ## GAME CALIBRATIONS


Joint-family authoring, not arbitrary per-joint proliferation:

- ankle
- knee
- hip
- lumbar/trunk
- shoulder
- elbow
- wrist/hand stabilization

Each family has a base response ratio, damping ratio target, finite force cap, target-rate cap, and joint-limit buffer.
Lift profiles may multiply these values by a small named factor. Start with low spring, raise damping until nonoscillatory,
then raise spring only until unloaded tracking is adequate. Capacity is calibrated separately from tracking stiffness.


    ## NUMERICAL IMPLEMENTATION


Set drive fields before the physics step. Avoid per-tick mode switching. Rate-limit target rotations using shortest-arc
slerp and target angular velocity. Demand saturation uses command-side values and dwell/hysteresis, not equality against
an internal engine value. When near a hard joint limit, reduce reference demand; do not use projection to conceal it.


    ## PSEUDOCODE

    ```text
    EvaluateJoint(joint, target, capacity, h):
    target_q = rate_limit_shortest_arc(
        joint.previous_target_q, target.q, joint.max_target_rate, h)
    target_w = clamp_magnitude(target.w, joint.max_target_rate)

    error_vec = quaternion_log(target_q * inverse(joint.actual_relative_q))
    velocity_error = target_w - joint.actual_relative_w
    tau_des = Kp * error_vec + Kd * velocity_error
    tau_available = max(0, capacity * target.activation)
    demand = magnitude(tau_des) / max(tau_available, TORQUE_EPS)

    joint.configurable.targetRotation = to_unity_target_rotation(target_q)
    joint.configurable.targetAngularVelocity = to_joint_frame(target_w)
    joint.drive.positionSpring = spring_from_family()
    joint.drive.positionDamper = damper_from_family()
    joint.drive.maximumForce = tau_available
    joint.drive.useAcceleration = false

    return JointDiagnostic(error_vec, velocity_error, demand,
                           saturation_hysteresis.Update(demand >= 1))
    ```

    ## UNITY MAPPING


- Hinge-dominant knees/elbows/ankles: locked unused axes and validated X/YZ angular drive configuration.
- Hips, lumbar, shoulders: Slerp drive unless isolated fixtures prove a more stable axis decomposition.
- `rotationDriveMode`, `slerpDrive`, `angularXDrive`, `angularYZDrive`, `targetRotation`,
  `targetAngularVelocity`, and finite `maximumForce`.
- `projectionMode = None` in valid attempts.
- Joint limits are physical guards, not authored trajectory substitutes.


    ## FAILURE MODES


Wrong target-rotation frame; quaternion long-path; excessive spring/low damping; infinite max force; acceleration drive
removing desired mass sensitivity; double writer; target discontinuity; persistent saturation; hard-limit impact;
projection masking instability; interpreting currentTorque as athlete output.


    ## OBSERVABILITY


Per joint: target/actual orientation, local error vector, relative/target angular velocity, configured drive values,
capacity, conceptual demand, saturation dwell, limit distance, and writer identity.


    ## TELEMETRY


Normal: family-level peak/mean modeled demand and saturation duration.  
Diagnostic: per-tick values for selected joints.  
Research: all joints plus engine `currentForce/currentTorque`, clearly labeled as solver diagnostics.


    ## TESTS


- Positive target pulse rotates in expected anatomical sign.
- 90° equivalent quaternion signs yield the same shortest error.
- Maximum force is finite and scales with athlete capacity.
- Increased external load increases tracking error or failure probability under identical intent.
- With zero capacity, the drive cannot hold the body.
- Step response remains bounded under accepted mass range.
- One writer assertion catches duplicate drive mutation.
- `currentTorque` does not enter success, fatigue, utilization, or claim logic.


    ## MUTATION TESTS


Set `maximumForce = infinity`; set `useAcceleration = true`; reverse a joint axis; enable a second torque writer;
teleport to targets; use Euler subtraction across wrap; add integral accumulation; promote currentTorque to athlete
utilization; enable projection. Each must break a named invariant/test.


    ## PERFORMANCE CONSIDERATIONS


O(number of powered joints) with no online matrix optimization. Cache family data and target-frame conversions.
Avoid quaternion allocations and reflection. Diagnostics use ring buffers.


    ## CLAIM CLASSIFICATION


Control equation: `ENGINEERING_CONCEPTUAL`. Capacity: `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION`.
Configured drive values: `SOURCE_DIRECT` runtime configuration. Applied biological torque: `NOT_OBSERVABLE`.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** finite force drives, command-side demand proxy, family tuning, no integral.  
**LATER:** phase-dependent family multipliers and calibrated angle/velocity capacity curves.  
**RESEARCH:** inverse dynamics, torque estimation, impedance identification.  
**OUT_OF_SCOPE:** HQP/KKT/whole-body optimization in the shipping loop.
