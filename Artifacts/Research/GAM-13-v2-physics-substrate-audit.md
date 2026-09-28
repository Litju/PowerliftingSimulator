# GAM-13 V2-3B Physics Substrate Audit

Authority: live Linear issue GAM-13, V2-3B active. Scope is physical construction through post-physics observation; the V2 controller remains `q_cmd = q_ref(s) + delta_q_balance`.

## Tick-0 physical state

### CURRENT

`PhysicalAthleteRig.Build` creates and validates the athlete before simulation. It checks total mass, positive finite inertia, zero projection, zero passive drives, and anchor coincidence to `0.1 mm`. It measures nonadjacent athlete self-penetration, but does not validate all body transforms/velocities, joint axes, platform/body penetration, bar state, or the complete primed V2 command before the first `PhysicsScene.Simulate`.

### FINDING

There is no single hard pre-simulation gate for the complete athlete + platform + bar + saddle state. Startup errors can reach the first solver step.

### CHANGE

Add a build-time tick-0 validator for finite body state, zero unintended velocity, collider penetration, plantar/platform registration, anchor/axis validity, primed targets, and bar/saddle initialization. Fail initialization before simulation on any violation.

### VALIDATION

Pending focused EditMode and PlayMode substrate tests and a fresh-process tick-0 receipt.

### STATUS

OPEN — source audit complete; implementation and runtime validation pending.

## Athlete mass, COM, and inertia

### CURRENT

The 16-body model assigns exactly 100 kg from explicit segment fractions. Each body uses a zero local COM. `PhysicalAthleteRig.CreateSegment` assigns `PhysicalAthleteDefinition.BoxInertia` for both box and capsule colliders; capsule dimensions use the measured segment length and collider radius. The foot collider is offset to register its sole while its authored body COM remains at the segment origin.

### FINDING

Box inertia is not consistent with the actual capsule geometry. The authored segment origin is the intended COM; the foot collider offset also needs its parallel-axis contribution.

### CHANGE

Keep authored masses and COMs. Replace the box-only tensor with exact box/capsule primitive inertia about the authored COM, including collider-offset terms, and retain a principal-axis rotation consistent with the body frame.

### VALIDATION

Pending per-segment mass/COM/inertia receipt, positive finite principal inertia checks, formula tests, and deterministic rebuild comparison.

### STATUS

OPEN — box/capsule inconsistency confirmed in source.

## Powered-joint topology and reference bounds

### CURRENT

The 15 joints are created on each child body and connected to the recipe’s parent. Anchors come from GAM-10 reference bones. Hinges lock Y/Z; ball joints limit X/Y/Z. Limits invert authored X bounds to match Unity target-rotation sign. Projection is disabled. Primary axes are resolved in child space; secondary axes are selected from world up/forward without Gram-Schmidt orthogonalization. Existing GAM-13 joint-limit mapping tests encode the V2 headroom contract.

### FINDING

Parent/child and target-space mappings are explicit, but axis validity/orthogonality and the complete configured joint contract are not hard-validated at construction.

### CHANGE

Orthonormalize and validate each joint frame. Validate recipe parent, active degrees of freedom, physical bounds, canonical reference headroom, and neutral/target rotation mapping without projection or a second physical writer.

### VALIDATION

Pending focused topology, target-mapping, and existing joint-headroom regression tests.

### STATUS

OPEN — source mapping exists; additional build-time checks pending.

## Powered drive and demand diagnostics

### CURRENT

`PoweredJointController` is the only joint-drive writer. It uses `XYAndZ`, finite force-mode drives, target rotation, target angular velocity, and shortest-arc target-rate limiting. Hinges activate angular X only; ball joints activate X and YZ. The modeled demand currently forms a 3D torque vector and divides its magnitude by maximum force. `ConfigurableJoint.currentTorque` is already exposed separately as a solver diagnostic.

### FINDING

The current norm combines unrelated twist and swing axes, so it is not evidence of saturation in an active PhysX drive channel.

### CHANGE

Report hinge twist demand, and ball twist plus YZ swing demand. Define actuator pressure as the maximum active-channel demand fraction. Keep solver torque separate and label it as an engine/constraint diagnostic.

### VALIDATION

Pending drive-contract assertions and EditMode demand tests for hinge, ball, inactive channels, target-rate limit, and finite maximum force.

### STATUS

OPEN — current demand calculation is not channel-aligned.

## Feet, platform, and contact

### CURRENT

The platform is a static 5 m × 0.1 m × 5 m box with a 0.85 static / 0.75 dynamic friction material and Average combine. Foot boxes are placed on the canonical plantar plane and use 1.0 static/dynamic friction with Maximum combine. The rig checks nonadjacent athlete self-penetration before creating the platform. `PhysicalFootContactDetector` promotes collision-callback data at the next physics-tick update.

### FINDING

Material intent and post-simulation contact timing are explicit. Tick-0 geometric foot/platform registration and unintended external penetrations are not part of the current hard validation gate.

### CHANGE

Validate both plantar box bottoms against the actual platform top and reject initial penetrations among active collision pairs. Preserve physical friction and record that detector contact becomes authoritative after the first simulation step.

### VALIDATION

Pending tick-0 bilateral plantar geometry checks and standing-window bilateral contact checks.

### STATUS

OPEN — contact materials are authored; full initialization validation pending.

## Barbell mass and rigid-body state

### CURRENT

The bar uses one dynamic gravity-enabled Rigidbody. The loading model assigns 20 kg to the bare bar plus 5 kg collars and symmetric plate mass. Compound COM/inertia are calculated from cylinder components and assigned to the root; shaft/sleeve/shoulder, plate, and collar colliders are child colliders without extra Rigidbodies.

### FINDING

The modeled construction is physically dynamic and load-independent in outcome selection. Canonical-load mass, COM, inertia, collider topology, and finite tick-0 state are not checked together before simulation.

### CHANGE

Keep the existing compound bar model and verify exact requested mass, finite symmetric COM/inertia, single-body collider topology, gravity, and dynamic state at initialization for each tested load.

### VALIDATION

Pending canonical 25/60/140/170/300 kg loading checks and the tick-0 bar-state receipt.

### STATUS

OPEN — compound mass model present; complete substrate validation pending.

## Bar-to-thorax saddle

### CURRENT

`SquatBarSaddle` joins the dynamic bar to the dynamic thorax with finite angular bounds and finite break force/torque. Translation is compliant on all axes with a 50 mm limit, 50 kN/m spring, and 3 kN·s/m damping. It records initial anchor error but accepts up to 200 mm and does not align the bar before creating the joint. The prior fresh-process 25 kg trace recorded 50.0068 mm maximum separation on the first sample, marginally over its 50 mm standing criterion.

### FINDING

The spawn tolerance is wider than the entire intended translation range, so a large initialization mismatch may be left for PhysX to reconcile. Current evidence identifies this as a substrate defect; it does not establish a joint-solver failure.

### CHANGE

Set the bar’s authored initial spawn position from the thorax anchor before bar-body registration. Reject any remaining initialization mismatch at a tight tolerance. Use tightly compliant millimetre-scale translation with finite force/break limits and bounded angular freedom. Report anchor error, separation, limit occupancy, relative rotation, break state, and current engine force/torque separately.

### VALIDATION

Pending tick-0 alignment assertion and unloaded/25 kg saddle separation and limit-occupancy measurements.

### STATUS

OPEN — initialization alignment and working range require refactor.

## Simulation ownership and reset

### CURRENT

`PhysicsTickDriver.StepOne` advances the monotonic clock, samples intent, runs one pre-physics callback, calls the authoritative local `PhysicsScene.Simulate(0.01)`, captures/publishes a copied observation, commits trace, then invokes the post-physics observer. Production code has one `Simulate` call. Registered Rigidbody reset snapshots preserve pose, velocity, sleep, and kinematic state.

### FINDING

The fixed-step ordering and one-owner boundary are present. The startup contract does not yet prove that full squat construction and target priming finish before the first tick, or that the aligned saddle pose is the reset baseline.

### CHANGE

Keep the existing step owner and 0.01 s step. Make squat initialization fail before the first tick if substrate validation or command priming fails; make the aligned bar spawn pose the registered reset state and verify exact reset/rebuild.

### VALIDATION

Pending simulation-ownership, observer immutability, initial-tick, and reset determinism checks plus the Master Spec verifier.

### STATUS

OPEN — runtime owner is single; first-tick and aligned-reset qualification pending.

## Replaceable physical simulation boundary

### CURRENT

Gameplay input and replay state already flow through `PlayerIntentFrame` and copied `PhysicalObservation`. The Unity squat adapter still receives concrete `PoweredJointController` access through `IPhysicalAthleteCommandSource` and reads `PoweredJointRuntime`/ConfigurableJoint details for target mapping and diagnostics.

### FINDING

The observation and input loop are stable contracts, but the actuator seam leaks ConfigurableJoint implementation details into the squat command path.

### CHANGE

Narrow the athlete command/readback seam to copied joint state plus target commands while leaving `PoweredJointController` as the sole Unity actuator writer. Keep rules, input, UI, presentation, replay, and fixed-step ownership on their current contracts; add no engine-wide abstraction.

### VALIDATION

Pending dependency/ownership checks proving gameplay consumers use the state/command seam and only the Unity athlete backend writes ConfigurableJoint properties.

### STATUS

OPEN — replaceable loop contracts exist; actuator seam needs cleanup.
