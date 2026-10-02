# GAM-13 V2-3B Physics Substrate Audit

Authority: active Linear issue GAM-13 V2-3B. Unity remains the game engine. The V2 controller remains `q_cmd = q_ref(s) + delta_q_balance`; no controller or strength tuning was performed.

## Tick-0 physical state

### CURRENT

The physical athlete, support platform, bar, optional saddle, and primed standing command are validated during squat initialization and load reset before the first local physics step. The foundation holds `PhysicsTickDriver.StepOne` until the squat validator releases it. Each Rigidbody is checked for finite pose, COM, inertia, and velocities; athlete mass/topology and anchors are checked; active collider pairs are tested; and the bar and saddle contracts are checked when loaded. A failure throws before the first `PhysicsScene.Simulate`.

### FINDING

The initial state now has a hard gate. The unloaded receipt lists four connected athlete-joint overlaps; the loaded receipt adds four bar/non-thorax setup overlaps. They are collision-suppressed (maximum recorded overlap is 101.6 mm at pelvis/thigh). No active pair begins penetrated.

### CHANGE

Added complete tick-0 validation, including the primed drive targets. The bar spawn is derived from the thorax saddle anchor before its Rigidbody is registered, so reset returns to the aligned pose.

### VALIDATION

Fresh-process PlayMode tick-0 audit passed for unloaded and 25 kg setups with the first-tick hold active. Both receipts record tick 0, 16 athlete bodies, 15 joints, 100 kg total athlete mass, bilateral plantar geometry, zero active initial penetration pairs, and exact reset after one owned step:

- `Artifacts/Measurements/GAM-13/v2-3b-substrate/tick-0-unloaded-final-gated.json`
- `Artifacts/Measurements/GAM-13/v2-3b-substrate/tick-0-25kg-final-gated.json`

The receipts enumerate intentional suppressed overlaps separately by collider pair and depth.

### STATUS

PASS — both authored startup states validate before simulation.

## Athlete mass, COM, and inertia

### CURRENT

The 16 authored masses sum to 99.999992 kg at runtime (100 kg within float precision). Each body’s local origin is its authored COM. Boxes use analytic box inertia; Y-axis capsules use solid-cylinder plus hemispherical-cap inertia, including the cap-centroid offset; the foot box includes its collider-center parallel-axis term. Automatic Unity COM and inertia derivation are disabled after these authored properties are assigned.

### FINDING

The former box approximation for capsules was inconsistent with the actual collision shape. The first analytic capsule correction also omitted the cap-centroid offset; the final formula and test expectation include it.

### CHANGE

Replaced the mixed box/capsule approximation with collider-consistent primitive inertia about each authored COM. Inertia principal axes stay aligned with the body frame; the current recipe offsets only the plantar boxes along one principal axis.

### VALIDATION

The audit receipts contain mass, collider kind/dimensions/center, COM, inertia tensor, and tensor rotation for every segment. EditMode primitive-formula and recipe tests passed 7/7; the production athlete PlayMode reset/fall test passed 1/1.

### STATUS

PASS — no capsule uses box inertia; values are finite, positive, deterministic, and retained by PhysX.

## Powered-joint topology and reference bounds

### CURRENT

All 15 child joints connect to the recipe parent. Build validation checks child/parent ownership, coincident anchors, finite normalized orthogonal axes, recipe primary-axis direction, hinge/ball angular freedom, physical bounds, and disabled projection. GAM-10 reference geometry and the V2 joint-headroom contract are unchanged.

### FINDING

The previous frame construction could provide a non-orthogonal secondary axis and did not hard-check the full runtime topology. The accepted physical limits and reference targets remain unchanged.

### CHANGE

Projected the secondary axis perpendicular to the primary and added construction checks for topology, axes, anchors, degrees of freedom, and limits. The sole powered writer continues to map logical neutral identity through the existing joint-space conversion.

### VALIDATION

`GAM13V2JointLimitMappingTests` passed 1/1. GAM-10 reference tick and render-rate/root-authority regressions passed 2/2. No physical bounds or GAM-10 reference angles changed.

### STATUS

PASS — configured topology is valid and existing reference/headroom regressions hold.

## Powered drives and demand diagnostics

### CURRENT

`PoweredJointController` remains the single actuator writer. The active PhysX contract is `XYAndZ`, force mode, finite per-family spring/damper and maximum force, inverse target rotation, negated target angular velocity, and a per-tick shortest-arc target-rate limit. Hinges drive twist only; ball joints drive twist and swing/YZ.

### FINDING

The prior modeled demand combined twist and swing into a 3D vector norm, which did not correspond to either active PhysX drive channel. `ConfigurableJoint.currentTorque` remains a solver/constraint diagnostic, not drive demand.

### CHANGE

Diagnostics now report twist demand and normalized pressure separately from ball-joint swing/YZ demand and pressure. Canonical modeled drive pressure is the maximum active-channel pressure. Tick-0 priming validates actual drive mode, gains, force ceilings, target rotation, and target-rate limit. The command-source interface now receives an `IPhysicalAthleteJointCommandSink`; the Unity backend still owns the only ConfigurableJoint writer.

### VALIDATION

EditMode channel tests passed 4/4; the substrate/command-seam EditMode tests passed 7/7. The complete Unity-generated solution build passed with 0 errors. The existing warning set is limited to unassigned serialized/test fields.

### STATUS

PASS — reported saturation is channel-aligned and solver torque stays separate.

## Feet, platform, and contact

### CURRENT

The static platform is 5 m × 0.1 m × 5 m, with static/dynamic friction 0.85/0.75 and Average combine. Plantar boxes use 1.0/1.0 friction and Maximum combine. Build validation requires both soles within 0.1 mm of the actual platform top and checks all active collider pairs before simulation. Contact callbacks are consumed after simulation.

### FINDING

Tick-0 bilateral support is geometric; contact-detector state is only authoritative after the first physics step. The standing trace confirms both contacts persist, even while the unloaded posture later fails.

### CHANGE

Added hard platform/material/plantar checks and pre-simulation active-pair penetration rejection. Joint-suppressed overlaps are recorded rather than hidden.

### VALIDATION

Both tick-0 receipts report bilateral plantar geometry. Gate A recorded bilateral foot contact in all 500 samples and zero active initial penetration. Its posture failure is recorded separately below.

### STATUS

PASS — platform registration, material pairing, and contact persistence validate.

## Barbell mass and rigid-body state

### CURRENT

The bar is one dynamic, gravity-enabled Rigidbody with a single compound collider assembly. The authored loading model is 20 kg bare bar plus 5 kg collars plus symmetric plates; the compound model supplies COM and inertia. Automatic COM/inertia are disabled so plate/collar collider placement cannot replace the model.

### FINDING

The first tick-0 check found the inactive unloaded bar’s Unity inertia tensor reads zero after deactivation. The active bar must therefore be validated immediately after its colliders are laid out and before an unloaded setup deactivates it. The authored model remains available and positive for the inactive bar.

### CHANGE

Bar load construction now applies mass properties after moving load-dependent colliders and verifies the active Rigidbody against the compound model. Tick-0 validation checks the model for an inactive unloaded bar and the Rigidbody properties whenever the bar is active.

### VALIDATION

The 25 kg receipt reports exact requested/actual mass and a 1.35×10⁻⁶ kg·m² inertia difference. The bar has one Rigidbody and dynamic/gravity state when loaded. PhysicalBarbell PlayMode loading/collider regressions passed 2/2.

### STATUS

PASS — loaded canonical bar state matches its authored mass model; unloaded bar is physically inactive.

## Bar-to-thorax saddle

### CURRENT

Loaded setup aligns the bar root to the thorax anchor before Rigidbody registration. Initial anchor tolerance is 0.1 mm. Translation is compliant on all axes with 12 mm limit, 500 kN/m spring and 6.5 kN·s/m damping; angular freedom is bounded, projection is disabled, and break force/torque are finite. Current and trace diagnostics keep initial error, separation, limit occupancy, relative rotation, break state, engine force, and engine torque separate.

### FINDING

The previous permissive 200 mm spawn tolerance and 50 mm working limit allowed a startup mismatch to be left for PhysX to reconcile. The former 25 kg trace’s first-step 50.0068 mm separation is no longer representative of the refactored startup.

### CHANGE

The scene builder now authors the aligned pose before the bar body is registered. Saddle construction fails immediately on a mismatch above 0.1 mm. The finite, millimetre-scale compliant load path remains between two dynamic bodies.

### VALIDATION

At 25 kg tick 0: initial error 0, separation 0, limit occupancy 0, relative rotation 0°, unbroken, engine force/torque 0. Tick-0 reset/rebuild checks pass. Gate A has no saddle because the bar is intentionally inactive.

### STATUS

PASS — tick-0 saddle configuration is aligned and does not begin on a translation limit.

## Simulation ownership and reset

### CURRENT

`PhysicsTickDriver.StepOne` advances the monotonic clock, samples intent, runs the single pre-physics command callback, simulates the authoritative local scene once at 0.01 s, publishes a copied observation, commits trace, and then calls post-physics observers. A readiness guard blocks the first step until tick-0 validation succeeds. The aligned bar spawn is the registered reset pose.

### FINDING

The existing one-owner order was retained. Startup now proves the complete physical state and targets are valid at tick 0, and a reset after an actual owned step must restore the authored poses and zero velocities.

### CHANGE

Added the initialization guard before the first step and verified reset/rebuild against captured tick-0 body poses. No second simulation owner or post-step transform write was added.

### VALIDATION

Master Spec verifier passed. `PhysicsOwnershipContractTests` passed 1/1; `PhysicsFoundationPlayModeTests` passed 14/14, including the first-step readiness gate, stable post-step observation, and reset; the V2-3B tick-0/reset PlayMode test passed for unloaded and 25 kg.

### STATUS

PASS — one local scene, one fixed-step owner, 0.01 s step, and exact reset baseline.

## Replaceable physical simulation boundary

### CURRENT

Input and replay still use `PlayerIntentFrame`; physical state still crosses the loop as copied `PhysicalObservation`. The Unity squat command source now emits target commands through `IPhysicalAthleteJointCommandSink`. `PoweredJointController` implements that sink and owns ConfigurableJoint writes.

### FINDING

Rules, input, UI, presentation, replay, and the fixed-step loop do not depend on ConfigurableJoint. Joint-space readback and target construction remain inside the current Unity squat adapter, where a future athlete backend would replace that adapter-facing readback implementation.

### CHANGE

Added only the current physical command seam; no engine-wide abstraction or gameplay layer was added. The escalation path remains ConfigurableJoint → ArticulationBody → native physics only if product evidence later proves it necessary.

### VALIDATION

The EditMode seam test confirms `IPhysicalAthleteCommandSource` accepts copied observation/time/intent plus the joint-command sink, not a ConfigurableJoint. One command source and one drive writer are enforced.

### STATUS

PASS — gameplay loop contracts are preserved and the actuator command boundary is replaceable.

## GAM-10, GAM-12, and GAM-49 authority regressions

### CURRENT

GAM-10 reference mapping, GAM-12 rules/failure/attempt authority, and GAM-49 surface-landmark depth remain the unchanged owners for their contracts.

### FINDING

The substrate work did not require changes to lift rules, failure predicates, reference geometry, or depth authority.

### CHANGE

No authority logic was changed. The physical command and observation seams continue to feed the existing rule and depth consumers.

### VALIDATION

GAM-10 joint-limit/reference mapping passed 1/1; GAM-10 reference tick and render-rate/root-authority checks passed 2/2. GAM-12 EditMode rule, failure, and attempt tests passed 45/45, 60/60, and 18/18. GAM-49 depth-landmark provider tests passed 2/2.

### STATUS

PASS — preserved authority regressions remain green.

## Standing qualification outcome

### CURRENT

Standing qualification was run in a fresh Unity process at 0 kg from a validated tick 0. The 500-sample window maintained bilateral foot contact, but the athlete did not remain upright. The 25 kg standing gate and every later ladder/lifecycle/strength phase remain unrun.

### FINDING

Gate A fails at the existing V2 balance bound, not at initialization: AP COM error begins at 5.0×10⁻⁶ m on tick 1, reaches 0.0474 m as the ankle correction hits its fixed −0.2618 rad bound on tick 38, then continues growing. The pelvis falls to 0.1432 m by tick 142; trunk pitch reaches 1.5569 rad at tick 140; horizontal COM speed peaks at 1.8895 m/s. Both feet remain in contact throughout. Maximum joint-anchor separation reaches 0.0212 m at tick 138, after posture collapse; tick-0 anchors and first-step separation are materially smaller. Peak active-channel modeled demand is 1.3626 on tick 1, driven by swing/YZ demand. No bar, saddle, pin, transform-driven motion, or strength change is involved.

### CHANGE

No controller bound, gain, strength, or load rule was changed in response. The substrate defects found in the source audit were corrected. Because Gate A still fails after a valid tick-0 substrate, qualification stops here; no Gate B 25 kg standing run, heavier standing ladder, lifecycle, intrinsic-strength calibration, or final GAM-13 qualification was run.

### VALIDATION

Fresh-process Gate A failure evidence: `Artifacts/Measurements/GAM-13/v2-3b-substrate/standing/000kg/20260927-gate-a-final/`. The sequential acceptance rule prohibited proceeding to 25 kg standing.

### STATUS

FAIL — the cleaned ConfigurableJoint substrate has not qualified the simple V2 controller for unloaded standing. ArticulationBody escalation is not supported by this evidence: the startup, inertia, saddle, and anchor checks pass, and the observed initiating limit is controller AP correction saturation rather than dominant joint-constraint error.
