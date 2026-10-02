# GAM-13 V2-3B Physics Substrate — Full Report

- **Report date:** 2026-09-27
- **Authority:** live GAM-13 description, V2-3B
- **Unity:** 6000.3.22f1
**Decision:** the tick-0 substrate audit passes for unloaded and 25 kg setup, but unloaded standing Gate A fails. GAM-13 is not qualified. The ordered gate prohibits 25 kg standing and all subsequent load, lifecycle, and strength work.

## Executive result

The work corrected and instrumented the current ConfigurableJoint substrate beneath the existing simple V2 command contract:

```text
q_cmd = q_ref(s) + delta_q_balance
```

The implementation now rejects invalid tick-0 physical state before the first simulation step, uses geometry-consistent athlete inertia, validates joint frames and physical bounds, reports active PhysX drive channels separately, aligns the dynamic bar to the thorax before simulation, and records tick-0/reset evidence. The fixed-step owner and copied state/command contracts remain intact.

Both unloaded and 25 kg **tick-0 setup** checks pass. The fresh-process unloaded **standing** qualification fails: the bounded V2 AP ankle correction reaches its `-0.2618 rad` limit on tick 38 while AP COM error is `0.0474 m`, then the COM moves outside the support region and the athlete collapses despite bilateral foot contact. This does not establish joint-constraint error as the initiating dominant cause. No controller gain, correction bound, intrinsic strength, or load rule was changed. No ArticulationBody migration is indicated by this evidence.

The next ordered gate is **not authorized by its acceptance rule**: 25 kg standing was not run. The 60/140/170/300 standing ladder, five-load lifecycle, intrinsic-strength calibration, and final GAM-13 qualification were not run. GAM-14 remains blocked because GAM-13 is incomplete.

## Preflight and repository state

- The active GAM-13 description was read first and identified V2-3B as the authority.
- The requested branch is `work/gam-13-squat-load-calibration`; one Git worktree was present.
- Unity project version is `6000.3.22f1`.
- At preflight, tracked files were clean, HEAD was `8068dfe`, and the branch already contained two commits beyond the then-current upstream. Existing untracked measurement/evidence directories were preserved.
- At report creation, HEAD was `b70bb263da8b010346bead9211c89cd7e63f22d0`, upstream was `b564b0dc4dcf7bb0efb591bc94ec1f767feb0e56`, and the branch was eight commits ahead before this report commit. No push was made. Tracked source was clean before authoring this report; pre-existing untracked evidence was left untouched.
- No new Linear issue was created. Unity remains the engine. No custom engine, ArticulationBody, root pin, foot pin, transform-driven lift, direct bar-velocity edit, or scripted load failure was introduced.
- GAM-10 reference geometry, GAM-12 rules/failure/attempt truth, and GAM-49 depth authority were left unchanged.

## Phase disposition

| Phase | Result | Evidence / disposition |
|---|---|---|
| 0 — preflight and authority | PASS | Branch, worktree, Unity version, tracked-tree baseline, and live V2-3B authority recorded above. |
| 1 — audit artifact | PASS | [Subsystem audit](../Research/GAM-13-v2-physics-substrate-audit.md) records each subsystem with `CURRENT`, `FINDING`, `CHANGE`, `VALIDATION`, and `STATUS`. |
| 2 — initial state | PASS | Hard validation runs at tick 0 before the first `PhysicsScene.Simulate`; unloaded and 25 kg setup receipts pass. |
| 3 — mass, COM, inertia | PASS | All 16 athlete segments recorded; primitive inertia matches the actual box/capsule collider recipes. |
| 4 — joint topology | PASS | All 15 parent/child frames, axes, freedoms, limits, targets, and projection settings validated. |
| 5 — powered drives | PASS | One `PoweredJointController` writer; demand is reported per active PhysX channel, separate from solver torque. |
| 6 — feet/platform | PASS | Tick-0 plantar registration, active-pair penetration, material, and platform contracts pass. |
| 7 — barbell | PARTIAL | Unloaded inactive state and exact 25 kg dynamic setup are recorded. 60/140/170/300 kg mass-property receipts were not generated in this gated run. |
| 8 — bar/thorax saddle | PASS AT TICK 0 | 25 kg saddle starts aligned with zero separation and zero limit occupancy; movement qualification is not established. |
| 9 — simulation ownership | PASS | One authoritative local scene, one 100 Hz `StepOne` owner, ordered prepare/simulate/observe path, and reset evidence. |
| 10 — replaceable boundary | PASS | Minimal joint-command sink seam added; gameplay and replay contracts do not consume `ConfigurableJoint` directly. |
| 11 — audit validation | PASS | Build, focused substrate tests, Master Spec, and GAM-10/12/49 regression groups passed. |
| 12 — unloaded standing | **FAIL** | Gate A failed in a fresh Unity process after 500 samples from validated tick 0. |
| 13 — continue after A+B | NOT ENTERED | Gate B and all later qualification/calibration phases are blocked by Gate A failure. |

## Tick-0 substrate receipt

The concise receipt is [GAM-13 V2-3B Tick-0 Substrate Receipt](../Receipts/GAM-13-v2-3b-tick0-substrate.md). Detailed per-body and per-joint state is preserved in the [unloaded JSON receipt](../Measurements/GAM-13/v2-3b-substrate/tick-0-unloaded-final-gated.json) and [25 kg JSON receipt](../Measurements/GAM-13/v2-3b-substrate/tick-0-25kg-final-gated.json).

| Setup | Validation | Bodies / joints | Contacts and active penetration | Bar / saddle |
|---|---|---|---|---|
| Unloaded | PASS at tick 0, before first simulation | 16 athlete bodies, 15 powered joints, total `99.999992 kg` | Both plantar surfaces on platform; zero active initial penetration pairs; zero startup linear/angular velocity | Bar inactive; no saddle in the unloaded scene; authored 25 kg model remains finite |
| 25 kg | PASS at tick 0, before first simulation | Same athlete topology and mass | Both plantar surfaces on platform; zero active initial penetration pairs; zero startup linear/angular velocity | One dynamic, gravity-enabled bar Rigidbody at exactly `25.0 kg`; aligned saddle with zero initial error, separation, and occupancy |

Both receipts show primed canonical standing targets and verify authored-pose/zero-velocity restoration after one owned step and reset. Foot-to-platform geometric gap is `0 m` on both sides. Collision-disabled intentional overlaps are listed separately from active penetration: four direct athlete-joint pairs in the unloaded state, plus four filtered bar/non-thorax pairs in the 25 kg state. The maximum listed suppressed overlap is `0.101602 m` at pelvis/thigh; those pairs cannot produce contact impulses. Every other tested pair passes the active-penetration gate.

## Athlete mass, COM, and inertia

### CURRENT

The physical athlete consists of 16 dynamic bodies. The runtime masses sum to `99.999992 kg` (`100 kg` within float precision). Body-local COM is explicitly authored at the body origin. The plantar boxes have collider center `(0, 0.047, 0) m`; other listed collider centers are zero. All reported inertia tensor rotations are identity.

### FINDING

The previous box inertia approximation was inconsistent for segments whose physical collider is a capsule. The initial analytic capsule correction also omitted the hemispherical-cap centroid offset. That formula and its test expectation were corrected before final receipts were captured.

### CHANGE

Boxes use analytic rectangular-solid inertia, including the foot collider-center parallel-axis offset. Y-axis capsules use solid-cylinder and hemispherical-cap terms, including the cap-centroid offset. Automatic COM and inertia replacement are disabled after the explicit mass properties are applied. No historical box approximation is retained for capsule bodies.

### VALIDATION

All principal inertias in the final 25 kg receipt are finite and positive; none of the 16 inertia rotations differ from identity. EditMode primitive/recipe tests passed `7/7`; the physical-athlete PlayMode test passed `1/1`. The final receipt contains the full precision COM, collider, mass, inertia, pose, and velocity records.

### STATUS

PASS — recorded athlete mass and inertia are finite, positive, deterministic, and consistent with the authored primitive collider geometry.

### Segment mass and inertia table

Dimensions are in meters. `Box` dimensions are X×Y×Z. `CapsuleY` dimensions report X/Z diameter and total Y height. Inertia is `(Ixx, Iyy, Izz)` in kg·m². Local COM is `(0,0,0) m` for every segment; inertia tensor rotation is identity for every segment. Values below are rounded for readability; the linked JSON receipts retain full precision.

| Segment | Parent | Mass kg | Collider | Dimensions m | Collider center m | COM local m | Inertia tensor kg·m² |
|---|---|---:|---|---|---|---|---|
| abdomen | pelvis | 13.90 | Box | 0.250×0.160×0.170 | 0,0,0 | 0,0,0 | 0.0631292, 0.105872, 0.102049 |
| head_neck | thorax | 8.10 | CapsuleY | 0.210×0.310×0.210 | 0,0,0 | 0,0,0 | 0.0633693, 0.0394419, 0.0633693 |
| left_foot | left_shank | 1.45 | Box | 0.130×0.100×0.290 | 0,0.047,0 | 0,0,0 | 0.0146375, 0.0122042, 0.00651753 |
| left_forearm | left_upper_arm | 1.60 | CapsuleY | 0.095×0.205×0.095 | 0,0,0 | 0,0,0 | 0.00502041, 0.00167281, 0.00502041 |
| left_hand | left_forearm | 0.60 | Box | 0.140×0.060×0.045 | 0,0,0 | 0,0,0 | 0.00028125, 0.00108125, 0.00116000 |
| left_shank | left_thigh | 4.65 | CapsuleY | 0.130×0.385×0.130 | 0,0,0 | 0,0,0 | 0.0510722, 0.00932534, 0.0510722 |
| left_thigh | pelvis | 10.00 | CapsuleY | 0.180×0.360×0.180 | 0,0,0 | 0,0,0 | 0.0981072, 0.0372621, 0.0981072 |
| left_upper_arm | thorax | 2.80 | CapsuleY | 0.120×0.211×0.120 | 0,0,0 | 0,0,0 | 0.00964450, 0.00456825, 0.00964450 |
| pelvis | — | 14.20 | Box | 0.300×0.200×0.180 | 0,0,0 | 0,0,0 | 0.0856733, 0.144840, 0.153833 |
| right_foot | right_shank | 1.45 | Box | 0.130×0.100×0.290 | 0,0.047,0 | 0,0,0 | 0.0146375, 0.0122042, 0.00651753 |
| right_forearm | right_upper_arm | 1.60 | CapsuleY | 0.095×0.205×0.095 | 0,0,0 | 0,0,0 | 0.00502041, 0.00167281, 0.00502041 |
| right_hand | right_forearm | 0.60 | Box | 0.140×0.060×0.045 | 0,0,0 | 0,0,0 | 0.00028125, 0.00108125, 0.00116000 |
| right_shank | right_thigh | 4.65 | CapsuleY | 0.130×0.385×0.130 | 0,0,0 | 0,0,0 | 0.0510722, 0.00932534, 0.0510722 |
| right_thigh | pelvis | 10.00 | CapsuleY | 0.180×0.360×0.180 | 0,0,0 | 0,0,0 | 0.0981072, 0.0372621, 0.0981072 |
| right_upper_arm | thorax | 2.80 | CapsuleY | 0.120×0.211×0.120 | 0,0,0 | 0,0,0 | 0.00964450, 0.00456825, 0.00964450 |
| thorax | abdomen | 21.60 | Box | 0.360×0.280×0.200 | 0,0,0 | 0,0,0 | 0.213120, 0.305280, 0.374400 |

## Powered-joint topology and physical bounds

### CURRENT

There are 15 physical joints; each child connects to its recipe parent. Construction validates parent/child ownership, world anchor coincidence, finite orthogonal axes, joint semantics, fixed angular bounds, and disabled projection. GAM-10 reference mapping and V2 headroom limits remain unchanged.

### FINDING

The former axis construction could leave the secondary axis non-orthogonal, and the full runtime topology was not rejected at build time. Runtime evidence now confirms the authored frames are coincident and axes valid. No projection correction is needed or enabled.

### CHANGE

The secondary axis is projected perpendicular to the primary axis before joint construction. Hard topology checks cover every joint. Neutral reference and command targets continue through the existing joint-space mapping; no second joint writer or physical constraint authority was introduced.

### VALIDATION

Every anchor error in the 25 kg receipt is below `6.0×10⁻⁸ m`; primary and secondary axes are finite, normalized, and non-degenerate. Projection is `None`. GAM-10 joint-limit/reference mapping passed `1/1`; reference tick, render-rate, and root-authority checks passed `2/2`.

### STATUS

PASS — the 15 joint frames, semantics, limits, and target mapping satisfy the current physical contract.

### Joint topology table

Axes are child-local `(x,y,z)` rounded to two decimals. Bounds show X low/high followed by symmetric Y/Z limits in degrees. Drives show the configured `spring / damper / maximumForce` values; these are angular drives, so their force ceiling is a torque ceiling in N·m. Hinges use X twist only and have no active YZ drive. The receipt records actual target rotations, target angular velocities, and solver torque for every joint.

| Child ← parent | Kind | Anchor error mm | Primary axis | Secondary axis | Bounds X; Y/Z | X drive | YZ drive |
|---|---|---:|---|---|---|---|---|
| abdomen ← pelvis | Ball | 4.66e-7 | 1.00,0.00,0.00 | 0.00,0.99,-0.10 | -45/35; ±25/±25 | 800/85/2000.7 | 800/85/2000.7 |
| thorax ← abdomen | Ball | 1.40e-6 | 1.00,0.00,0.00 | 0.00,0.99,0.13 | -50/35; ±30/±30 | 800/85/2000.7 | 800/85/2000.7 |
| head_neck ← thorax | Ball | 0 | 1.00,0.00,0.00 | 0.00,0.96,-0.29 | -55/45; ±45/±45 | 300/11/205.2 | 300/11/205.2 |
| left_upper_arm ← thorax | Ball | 1.49e-5 | -0.03,-0.03,1.00 | 1.00,0.00,0.03 | -105/105; ±165/±165 | 500/55/666.9 | 500/55/666.9 |
| right_upper_arm ← thorax | Ball | 1.49e-5 | 0.03,-0.03,1.00 | -1.00,0.00,0.03 | -105/105; ±165/±165 | 500/55/666.9 | 500/55/666.9 |
| left_forearm ← left_upper_arm | Hinge | 2.98e-5 | 1.00,0.00,-0.03 | 0.03,0.03,1.00 | -145/5; 0/0 | 450/45/513 | — |
| right_forearm ← right_upper_arm | Hinge | 2.98e-5 | 1.00,0.00,0.03 | -0.03,0.03,1.00 | -145/5; 0/0 | 450/45/513 | — |
| left_hand ← left_forearm | Ball | 0 | 0.13,0.56,-0.82 | -0.04,0.83,0.56 | -70/70; ±36/±36 | 250/30/230.85 | 250/30/230.85 |
| right_hand ← right_forearm | Ball | 5.96e-5 | 0.13,0.00,0.99 | -0.04,1.00,0.00 | -70/70; ±36/±36 | 250/30/230.85 | 250/30/230.85 |
| left_thigh ← pelvis | Ball | 0 | 1.00,0.00,0.00 | 0.00,-1.00,0.00 | -45/132; ±50/±50 | 900/90/2770.2 | 900/90/2770.2 |
| right_thigh ← pelvis | Ball | 0 | 1.00,0.00,0.00 | 0.00,-1.00,0.00 | -45/132; ±50/±50 | 900/90/2770.2 | 900/90/2770.2 |
| left_shank ← left_thigh | Hinge | 5.97e-5 | 1.00,0.00,0.00 | 0.00,-0.99,0.11 | -145/5; 0/0 | 800/80/2770.2 | — |
| right_shank ← right_thigh | Hinge | 5.97e-5 | 1.00,0.00,0.00 | 0.00,-0.99,0.11 | -145/5; 0/0 | 800/80/2770.2 | — |
| left_foot ← left_shank | Hinge | 0 | 1.00,0.00,0.00 | 0.00,1.00,0.00 | -55/45; 0/0 | 650/70/2308.5 | — |
| right_foot ← right_shank | Hinge | 0 | 1.00,0.00,0.00 | 0.00,1.00,0.00 | -55/45; 0/0 | 650/70/2308.5 | — |

## Powered drives and demand diagnostics

### CURRENT

`PoweredJointController` remains the only joint-drive writer. Its tested PhysX configuration uses `rotationDriveMode = XYAndZ`, force mode (`useAcceleration = false`), finite spring/damper/maximum-force values, inverse target rotation, negated target angular velocity, and a finite shortest-arc target-rate limit per tick. Hinge joints actuate twist only; ball joints actuate twist and swing/YZ.

### FINDING

The former modeled demand combined unrelated twist and swing axes into one 3D norm. That number did not correspond to the active PhysX drive channels. `ConfigurableJoint.currentTorque` is a solver/constraint diagnostic and cannot stand in for actuator demand.

### CHANGE

Demand is now reported as twist and, for ball joints, swing/YZ channels. The canonical pressure is the maximum normalized demand among the channels active on that joint. Solver torque remains a separate field. Tick-0 validation inspects the actual drive mode, gains, force limits, target rotation, target rate, and primed target state. The command-source boundary accepts `IPhysicalAthleteJointCommandSink`; the current Unity adapter still owns ConfigurableJoint-specific target construction.

### VALIDATION

Powered-joint channel EditMode tests passed `4/4`; substrate/command-seam EditMode tests passed `7/7`. In the unloaded standing trace, the peak modeled active-channel pressure is `1.362562` at tick 1, from swing/YZ demand. This is channel-normalized modeled demand, not `currentTorque` and not a solver-error measurement.

### STATUS

PASS — drive configuration and diagnostics now correspond to active PhysX channels. No drive strength or capacity was tuned.

## Feet, platform, and contact

### CURRENT

The platform is a static `5.0×0.1×5.0 m` box with top at `y=0`. Its material is static/dynamic friction `0.85/0.75`, combine mode `Average`, and zero bounce. Plantar boxes use friction `1.0/1.0` and `Maximum` combine. Contact callbacks publish after simulation; tick 0 uses geometric sole registration.

### FINDING

The soles are geometrically registered without an initial active collider penetration. Contact-detector values become physical observations only after the first simulation step. The standing trace later shows that bilateral contact alone does not guarantee the COM remains supportable.

### CHANGE

Build validation checks the platform/material recipe, both plantar surfaces against the actual platform top within `0.1 mm`, and every active collider pair for initial penetration. Collision-disabled connected overlaps are enumerated rather than hidden.

### VALIDATION

Both tick-0 receipts show a `0 m` foot gap and zero active initial penetration. Gate A records left and right foot contact in all 500 samples. The minimum support margin nevertheless reaches `-0.934 m` during collapse.

### STATUS

PASS — foot/platform registration and material contracts are correct at initialization. Standing support qualification fails separately at Gate A.

## Barbell and thorax saddle

### CURRENT

The bar is a dynamic, gravity-enabled Rigidbody with one compound collider assembly. The 25 kg receipt records exact requested/actual mass, one Rigidbody, nine colliders, zero local COM, and explicit compound inertia. Automatic COM/inertia assignment is disabled. The bar root is placed at the thorax anchor before the Rigidbody enters the simulation scene.

The 25 kg saddle is a finite ConfigurableJoint between the dynamic bar and dynamic thorax. Translation is limited/compliant on X/Y/Z with a `12 mm` limit, `500 kN/m` spring, `6.5 kN·s/m` damper, and `60 kN` maximum drive force. Angular motion is bounded at ±20° on X and ±15° on Y/Z, with `1200/100/1200` angular spring/damper/maximum force. Break force is `60 kN`, break torque `15 kN·m`, and projection is disabled.

### FINDING

The prior permissive `200 mm` initial alignment tolerance and `50 mm` working limit allowed a large spawn mismatch to be left to PhysX. The refactored startup has a `0.1 mm` anchor-error tolerance and authors the aligned transform before simulation. The old 25 kg first-step separation is not a valid measurement of this setup.

### CHANGE

Spawn alignment is now a pre-simulation condition, not a solver recovery task. The saddle reports initial anchor error, current separation, translation-limit occupancy, relative rotation, broken state, and engine force/torque separately. No post-tick transform write, world pin, or direct bar-velocity write was added.

### VALIDATION

At 25 kg tick 0, initial anchor error, separation, and linear-limit occupancy are all zero; relative rotation is `0°`; the saddle is unbroken; current engine force and torque are zero. The receipt records the bar at exactly `25.0 kg`, dynamic and gravity-enabled, with actual-to-modeled compound inertia vector difference `1.35×10⁻⁶ kg·m²`. The unloaded gate has no bar or saddle. No moving loaded-bar trace was qualified because the ordered standing gate stopped at Gate A.

### STATUS

PASS AT INITIALIZATION — the loaded spawn and saddle topology are valid. The normal-load translation-limit occupancy requirement remains unqualified during movement. Only unloaded and 25 kg tick-0 setups are measured here; canonical 60/140/170/300 kg mass-property receipts were not generated.

## Simulation ownership, ordering, and reset

### CURRENT

`PhysicsTickDriver.StepOne` is the single production owner for the authoritative local PhysicsScene. It advances the monotonic tick, samples intent, prepares the pre-physics command, calls `Simulate(0.01 s)` once, publishes a copied post-step observation, commits trace state, then calls post-physics observers. The foundation readiness gate can hold tick 0 until the physical builder passes validation.

### FINDING

The one-owner order was already the correct contract. It did not itself prove that the physical athlete/bar and primed targets were valid before the first step. Reset also needed a repeatability assertion against the authored physical baseline.

### CHANGE

The builder holds the step gate during initial construction and load reset; it releases only after tick-0 checks pass. The reset test compares rebuilt body poses and zero velocities to the captured authored state. Observers remain post-step readers.

### VALIDATION

The production-wide ownership check finds the sole `.Simulate` call in `PhysicsTickDriver.StepOne`; fixed delta is `0.01 s`. `PhysicsOwnershipContractTests` passed `1/1`; `PhysicsFoundationPlayModeTests` passed `14/14`, including gate/order/observation/reset coverage. Unloaded and 25 kg substrate reset tests each passed `1/1` in fresh Unity runs.

### STATUS

PASS — the local simulation has one fixed-step owner and restores the authored tick-0 state on reset.

## Replaceable physical simulation boundary

### CURRENT

The gameplay loop continues to exchange `PlayerIntentFrame` and copied `PhysicalObservation` values. A narrow `IPhysicalAthleteJointCommandSink` is the command destination for the current athlete command source; the Unity backend implements it and owns the joint writes.

### FINDING

Gameplay, rules, input, UI, presentation, replay, and the fixed-step loop do not need `ConfigurableJoint` references. Joint-space readback and target construction are still Unity squat-adapter concerns and would need replacement with a future athlete backend.

### CHANGE

Only the current actuator boundary was added. This leaves a credible escalation route from ConfigurableJoint to ArticulationBody to a native physics core without preparing hypothetical engine abstractions throughout the repository.

### VALIDATION

The EditMode seam test verifies that `IPhysicalAthleteCommandSource` receives copied observation/time/intent plus `IPhysicalAthleteJointCommandSink`, not a `ConfigurableJoint`. A single command source and single powered writer remain enforced.

### STATUS

PASS — current physical authority boundaries are replaceable without changing rules, input, UI, presentation, replay, or game-loop contracts.

## Preserved GAM-10, GAM-12, and GAM-49 authorities

### CURRENT

GAM-10 owns reference geometry and mapping, GAM-12 owns rule/failure/attempt truth, and GAM-49 owns depth authority.

### FINDING

The substrate correction required no edits to those gameplay authorities.

### CHANGE

No lift outcome, failure predicate, attempt lifecycle, reference pose, or depth rule was changed to make physical evidence pass.

### VALIDATION

GAM-10 mapping passed `1/1`, with reference tick/render-rate/root-authority checks passing `2/2`. GAM-12 rule, failure, and attempt suites passed `45/45`, `60/60`, and `18/18`. GAM-49 depth-landmark regressions passed `2/2`. The Master Spec verifier passed.

### STATUS

PASS — those authority contracts remain unchanged and their targeted regressions pass.

## Build and audit validation

### CURRENT

Validation used Unity `6000.3.22f1`, the repository solution, focused EditMode and PlayMode fixtures, authority regression suites, and the Master Spec verifier.

### FINDING

The physical substrate and regression changes compiled and the focused test groups passed. The standing gate is an acceptance scenario and its measured failure is reported separately; it is not evidence of a passing standing test.

### CHANGE

The following validation was performed before standing qualification:

| Validation | Result |
|---|---:|
| `dotnet build PowerliftingSimulator.slnx --no-restore` | PASS, 0 errors; four existing warnings |
| `GAM13V23BSubstrateContractTests` | 7/7 PASS |
| `PoweredJointActuatorContractTests` | 4/4 PASS |
| Tick-0/reset PlayMode, unloaded | 1/1 PASS |
| Tick-0/reset PlayMode, 25 kg | 1/1 PASS |
| `PhysicsOwnershipContractTests` | 1/1 PASS |
| `PhysicsFoundationPlayModeTests` | 14/14 PASS |
| `PhysicalAthletePlayModeTests` | 1/1 PASS |
| `PhysicalBarbellPlayModeTests` | 2/2 PASS |
| GAM-10 mapping and reference regressions | 1/1 and 2/2 PASS |
| GAM-12 rule, failure, attempt regressions | 45/45, 60/60, 18/18 PASS |
| GAM-49 depth regressions | 2/2 PASS |
| Master Spec verifier | PASS |

### VALIDATION

The final unloaded and 25 kg tick-0 JSON receipts are under `Artifacts/Measurements/GAM-13/v2-3b-substrate/`; Unity result XML files accompany the fresh-process receipts. The Gate A runner, trace, and result XML are preserved in `Artifacts/Measurements/GAM-13/v2-3b-substrate/standing/000kg/20260927-gate-a-final/`.

### STATUS

PASS — pre-standing build and regression gates passed. Gate A did not pass.

## Gate A — unloaded standing

### CURRENT

Gate A used a fresh Unity process, unloaded `0 kg` configuration, validated tick 0, and a 500-sample physics trace. Both foot contact detectors reported support in every sample. Because the bar is intentionally inactive at zero load, bar height and saddle state are not applicable to this gate.

### FINDING

The trace starts near the authored AP COM reference: tick-1 AP error is `5.040318×10⁻⁶ m`. The applied ankle AP balance correction first reaches its fixed lower bound of `-0.2618 rad` on tick 38, when AP error is `0.047426 m`; AP error then continues growing. The COM becomes unsupported and posture collapses while both contact detectors stay true.

| Measured result | Evidence |
|---|---|
| Sample count | 500 from validated physics tick 0 |
| Tick-1 AP COM error | `5.040318×10⁻⁶ m` |
| AP correction reaches bound | tick 38, `-0.2618 rad`, AP error `0.047426 m` |
| Pelvis height | minimum `0.143236 m` at tick 142 |
| Trunk pitch | absolute maximum `1.556889 rad` at tick 140 |
| Horizontal COM speed | maximum `1.889497 m/s` |
| Minimum support margin | `-0.934372 m` at tick 138 |
| Bilateral foot contact | true for all 500 samples |
| Modeled active-channel drive pressure | maximum `1.362562` on tick 1, swing/YZ channel |
| Maximum joint-anchor separation | `0.021223 m` at tick 138, after posture collapse |
| Tick-1 joint-anchor separation | `0.001781 m` from the raw trace |
| Saddle/bar | not present in the unloaded gate |

The Gate A summary originally rounded/reported tick-1 joint-anchor separation as `0.0024 m`; this was inconsistent with the raw CSV and the audit summary. The derived summary has been corrected to `0.001781 m`; the raw `qualification-trace.csv` remains unchanged and is the measurement source. The peak separation and failure classification are unchanged.

### CHANGE

No controller gain, AP bound, command layer, strength capacity, load-specific behavior, or scripted outcome was changed after observing Gate A. The cleaned substrate had already passed its tick-0 checks. The trace records the existing V2 bounded AP balance correction as the observed early limitation; joint-anchor separation peaks later, after loss of posture.

### VALIDATION

The Gate A raw trace, runner receipt, gate summary, and fresh-process test XML are preserved at [Gate A evidence](../Measurements/GAM-13/v2-3b-substrate/standing/000kg/20260927-gate-a-final/). The result is `FAIL` against the standing acceptance gate.

### STATUS

FAIL — unloaded standing is not stable. The observed evidence does not meet the stop-condition case for ArticulationBody: initialization, mass/inertia, and saddle spawn checks pass, while maximum joint-anchor separation grows after posture collapse. The current evidence does not show joint-constraint error as the dominant initiating cause.

## Ordered continuation decision

The 25 kg tick-0 substrate receipt is **not** a 25 kg standing qualification. Gate A failed, so the ordered sequence stops here:

- 25 kg standing Gate B: not run.
- 60, 140, 170, and 300 kg standing gates: not run.
- Five-load squat lifecycle: not run.
- Intrinsic-strength calibration: not run.
- Final GAM-13 qualification: not achieved.
- ArticulationBody migration: not recommended by this evidence.
- GAM-14: remains blocked pending a passing GAM-13 qualification.

The code and records establish a more auditable, internally coherent tick-0 substrate, but they do not establish the requested shippable physical squat. Continue from the Gate A failure with the sequential gate intact; do not describe GAM-13 as complete.

## Commit record

The six implementation/evidence commits from the active work are:

1. `e3380dd` — audit artifact and validation harness.
2. `5750c25` — athlete inertia and joint-frame cleanup.
3. `a01ac46` — bar/thorax saddle alignment and constraint cleanup.
4. `27a6ddc` — tick-0 validation, drive diagnostics, and simulation-order gate.
5. `81ab8a7` — tick-0 receipts and Gate A outcome.
6. `b70bb26` — foundation guidance for the tick-0 simulation gate.

This full report and the trace-summary correction are staged and committed separately from the preserved pre-existing untracked measurement artifacts. The branch is not pushed.
