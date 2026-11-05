# GAM-64 — Bench Reduced Physical Model V1 (BRPM-1): PROVISIONAL

**Architecture decision:** BRPM-1, 2026-11-05.  
**Status:** PROVISIONAL CANDIDATE — NOT APPROVED for implementation; pending complete game-wide architecture and runtime audit.  
**Repo:** Litju/PowerliftingSimulator; issue GAM-64 (single active work item).  
**Branch:** codex/gam64-bench-reduced-plant, forked from clean main commit 687112b43afe71fe3e63e9c422708377a7f5bade.  
**Unity:** 6000.3.22f1.  
**Product scope:** one complete 25 kg bench press, not an all-lift muscle model.

**ARCHITECTURAL HOLD (2026-11-05):** An independent repository-wide static inspection identified lift-specific UI/game orchestration inside the Squat assembly and a hardcoded 16-body/15-joint athlete builder. The six-body implementation's integration with the game, its physical support/contact coverage, and its source-level qualification have **not** been established. Do not implement or promote this document as an approved specification until the owner has reviewed a game-wide code, runtime, UX and test audit. Existing main and dirty historical bench source are unchanged.

## 0 — Decision and evidence

The reported GAM-64 V1 geometric IK/liftoff, V2 object control, and mirrored Option A did not achieve a qualified judged bench. A2 failed before rack unloading: right captured-reference travel 3.32105 mm over the unchanged 3.000 mm contract, with intermittent/absent right palm load; the bar never cleared the rack. Left-right mirrored commands did not address the physical failure. These outcomes do not prove that the overall Rigidbody substrate, any finite grasp, or the shoulder/elbow topology is impossible.

Replace the bench plant; do not incrementally patch the archived controller. Select a **bench-specific reduced dynamic body model and finite point-handle grasp**, retaining Unity/PhysX, the same dynamic physical barbell and real external support reactions. Keep the verified 16-body squat model untouched. A separate full-body/muscle research track is out of this issue.

This is a **declared game approximation**, not a physiological muscle model: wrists, fingers and leg drive are not independently actuated in BRPM-1. The visual avatar is not a physical writer.

## 1 — Physical bodies, masses and geometry (LOCKED)

Exactly six dynamic Rigidbody bodies:

| ID | Count | Mass / source | Physical role |
|---|---:|---|---|
| bench_core | 1 | 90.0 kg, aggregate canonical non-arm segments | Free dynamic supported pelvis/trunk/head/legs with chest, buttocks, back and planted foot contact geometries |
| upper_arm_L/R | 2 | 2.8 kg each, canonical fraction | Powered shoulder-to-elbow transmission |
| forearm_hand_L/R | 2 | 2.2 kg each = 1.6 forearm + 0.6 hand | Powered elbow-to-grip force transmission; hand rigidly part of forearm |
| barbell | 1 | Existing 25 kg compound-barbell loading model | Full 3D dynamic judged object |

Model athlete total mass = 100 kg. Core and distal aggregate COM and inertia tensors MUST be computed from the original canonical segments and the selected *bench setup pose* with the parallel-axis theorem, with stated body frames, SI units and inertia orientations. No guessed lumped core inertia. Reuse PhysicalBarbell mass/inertia and its existing compound-collider source unchanged.

Core is NOT frozen, world-pinned or kinematic during physics. Its colliders are dynamically supported by actual bench back/glute contacts, planted-foot-floor contacts and bar/chest contacts. Foot collider geometry is rigidly part of this declared core surrogate; individual legs do NOT articulate or produce leg drive. Visual lower limbs follow a fixed plausible bench pose and never apply forces.

Power exactly four anatomical joints, eight rotational DOFs:
- Left/right shoulder = anatomical ball 3-axis joint each; existing source shoulder joint limits, sign/frame conventions, 130 Nm *family ceiling*, source-governed rate and drive law.
- Left/right elbow = anatomical hinge 1-axis each; existing source elbow joint limits, 100 Nm *family ceiling*, source-governed rate and drive law.
- Wrist is not a physical joint in this model. The hand is a rigid distal aggregate with neutral visual wrist alignment. The historical 36-degree wrist cone is **not loosened**; the corresponding DOF is simply absent in this newly declared reduced model. No wrist torque or independent wrist strength may be claimed.

The torso/core and bar retain unconstrained 6D body motion, subject to real contacts and finite grip forces. No hidden bar rail/slider, COM pin, transform correction, or suspension support.

## 2 — Single finite grasp force path (LOCKED)

Exactly one ConfigurableJoint between each distal forearm-hand aggregate and the actual dynamic bar at the athlete's measured shaft grip point. No second load-bearing palm/finger collision or parallel support force writer.

Per grasp:
- Linear X/Y/Z: Free, each equipped with a finite PD position/velocity drive relative to a captured physical anchor frame.
- Angular X/Y/Z: Free, unpowered; angular X, angular YZ and Slerp drives all off.
- Constraint projection: None. No linear hard lock, weld or kinematic pin; no hidden mass scaling.
- Active finite drive ceiling: initial 25 kg baseline from prior GAM-64 contract = 245.25 N per local linear axis per hand, subject to **exact local source verification** before implementation. The actual spring/damping coefficients and captured reference are source-qualified/identified and shall not be invented or retuned by a sweep.
- The previous 3 mm captured-reference travel contract remains a strict acceptance limit. Realized body-anchor separation and rest-reference travel must be logged separately. Grip failure is an actual contract violation, not an arbitrary new threshold.

Both grasps transmit **forces, not free couple moments**. Moment around the bar COM comes only from the cross products of the two grip-point forces with their measured moment arms. Bar shaft roll is free and *not* an actively controlled or judged physical DOF. Do not demand full 6D force closure from two point-handle grasps. One read-only engagement/proximity sensor can establish when the player can connect the grips; it applies no force.

The initial grip constraint is captured at measured current anchors with zero target discontinuity; engage without teleporting the bar or injecting unexplained impulses. Failure, release and reacquisition are physical state transitions, not prescribed trajectory outcomes.

## 3 — Actuator writer and system ownership (LOCKED)

Reuse the single existing PoweredJointController writer for the reduced rig, instantiated with the four real PhysicalAthleteRig.JointRuntime objects. Verified current source constructor accepts any provided joint list and need not modify the canonical 15-joint squat runtime. Reuse the source's bounded PD joint-drive authority; do not introduce competing AddTorque or joint drive writers.

The two finite grasp D6s each own their *distinct* three linear drive channels. Neither the game UI nor high-level bench controller writes bar position, velocity, orientation or forces.

PhysicsTickDriver remains the only simulation owner: 100 Hz authoritative steps, two 5 ms substeps, completed observation ticks, +Y world up, +Z athlete forward and +X athlete right. Preserve SI units and angular radians. Bench has an isolated source/scene, not a second clock.

**Physics:** PhysX body motion, reactions and contacts.
**Player intent:** phase/request only.
**Control:** bar task reference and admissible bounded joint references.
**Actuation:** existing joint-drive writer plus the two finite grip drives.
**Observations:** copied completed tick bar/arm/grip/contact state.
**Rules:** separate physical referee based on observations; rendered animation cannot write lift truth.

## 4 — Whole-system dynamics and control contract

The bar has a 5-DOF controllable task: its world COM X/Y/Z and the **orientation of its shaft axis** (two independent angles). Roll about the shaft is free and excluded from commanded task and rule truth.

At an unsupported stationary 25 kg bar the hands must collectively balance approximately 245.25 N vertically plus all required non-roll moments. The two grip-point forces obey:

    F_bar = F_L + F_R + F_external
    M_bar,COM = r_L x F_L + r_R x F_R + M_external

No externally applied desired bar force is allowed. Any requested object wrench is only an internal controller reference, mapped through the actual grip force map and arm Jacobian into available finite shoulder/elbow actuation. Never assume left/right forces are exactly equal, and never assume commanded torque equals realized PhysX torque.

State feedback uses real completed bar poses/twists, joint positions/angular velocities, captured grip anchors, core support, rack/chest contact and bounded joint-drive diagnostics. An object-level reference may guide the arms through the existing PD target interface **only after** the mapping/sign, torque bounds and realized response are characterized on this exact reduced plant. Do not reuse V2 arbitrary object-space gains by intuition. No competing actuator mode and no velocity injection. If 8 driven anatomical DOFs cannot achieve the required 5-dimensional bar task within physical limits, record the actual mechanism and stop instead of masking it with gains.

The contact-mode supervisor changes the task and observations when rack support, unsupported lockout and chest contact change the governing constraints. Use real physical events, not elapsed time as a transition oracle.

## 5 — Explicit gameplay / contact-mode state machine

1. SETUP: Author initial legal supine core/arm geometry and bar on real rack **before first physics tick**. No interpenetrations or authored movement after simulated start.
2. GRIP: Player requests bilateral engagement. Validate current anchor geometry; create both single finite D6s. Rack actually carries the bar before athlete load acceptance.
3. LOAD_ACCEPTANCE: Dynamic arms physically accept load; real rack/body contacts provide observed reactions. A sleeping bar with no new callback must not be misclassified as weightless or unsupported. Do not fabricate unobserved reaction values.
4. UNRACK: Arm commands physically clear the genuine rack geometry. Transition on observed rack clearance/contact mode.
5. START_READY: Physically unsupported and motionless bar under bilateral finite support; actual valid elbow lockout, stable core/feet and real 12 authentic completed dynamic START observations. Referee issues START.
6. DESCENT: Player lower intent; bar descends through finite actuator/grasp forces.
7. CHEST_PAUSE: Physical chest-bar contact and existing motionless/pause rule; referee issues PRESS, not a timer that moves the bar.
8. PRESS: Player press intent; actual concentric bar motion, with real saturation, sticking and possible failure.
9. LOCKOUT: Observable joint/bar endpoint satisfies the original rule. Referee issues RACK and completes judged record.
10. RERACK/RESULT/RETRY: Recovery/authoring may occur *only after* judged terminalization with explicit ownership handoff. Never rescue a failing judged attempt or rewrite result truth.

Preserve the established IPF-aligned rule authority; the existing 8-degree elbow lockout and 3 mm grasp constraints are unchanged. Any rule observation that refers to a removed wrist channel must be explicitly redefined for this reduced topology and tested; never silently return PASS. Timeout is a circuit breaker, not a physics transition.

## 6 — Visual rig, systems integration and scientific boundary

Reuse GAM-65 gym menu, camera/audio/input and result/retry experience. Full visual athlete tracks one core and four dynamic arm bodies. Its visual-only fingers/wrists/legs may use a fixed, believable bench pose and cannot feed forces or contacts back into physics. Bench-specific visible bone mapping may differ from the qualified squat.

GAM-66 earned PR and current capacity remain distinct, and bar-velocity samples are *measured shadow observations*. No PR-derived motor strength multiplier or individualized force–velocity law is qualified for bench. GAM-63 full 16-body squat and source/physics regressions remain immutable.

## 7 — Acceptance contract and stop rule

**Same real plant:** All physical qualifications use the same six dynamic bodies, four powered anatomical joints, two finite grips, one dynamic bar, real core/foot/bench/rack/chest contacts and one physics clock. No hidden kinematic fixture accepted as a full bench receipt.

**Mechanical:** Canonical 100 kg mass and derived aggregate inertias; legal source joint frames; zero invalid initial penetration; bilateral finite grip force without competing contacts; actual unsupported 25 kg lockout equilibrium and bounded shoulder/elbow actuation; intact 3D bar and no artificial force source.

**Full gameplay:** rack support → grip → physical load acceptance/unrack → 12 dynamic START observations → descent → legal chest pause/PRESS → physical press → legal lockout/RACK → truthful success/failure/retry. Require genuine failure cases, ten consecutive successful fresh 25 kg attempts, and owner-visible Unity rehearsal.

**Regression:** GAM-63 ten-attempt squat, GAM-65 game loop and intended bench availability update, GAM-66 shared contract 8/8 and relevant foundation tests. Bench is not Done until physical and gameplay acceptance, scoped PR review, verified exact head and confirmed merge.

This is **one replacement mechanical architecture**. Implement a complete causal chain, qualify its primitives and compose the gameplay; do not return to V1/V2 tuning or automatically initiate model C/engine migration. If the model's necessary mechanism is falsified, report the exact boundary and stop for owner product decision. Raw CSV/XML and scratch harnesses remain outside Git.

## 8 — Clean branch and historical provenance

- Old dirty Windows worktree: codex/gam64-linear-reference-manifold, located under gam64-lockout-manifold/PowerliftingSimulator. **Do not reset, clean, force checkout, delete, or overwrite it.** Reported historical GAM-64 measurement count was 4,539 before the migration discussion; reverify locally.
- New clean branch: codex/gam64-bench-reduced-plant, branched from main@687112b. This design document is its only intentional initial commit. No legacy bench source or raw measurements are cherry-picked wholesale.
- Create a **second** local Git worktree for the new remote branch. Recover only independently reviewed/qualified helper code deliberately, with new acceptance tests. No new Linear issue: GAM-64 remains sole active implementation item.
- Linear owns execution state/decisions; Git+PR owns implementation provenance; tests own acceptance; raw artifacts remain local/ignored.

## Technical basis

- Verified repository baseline: PhysicalAthleteDefinition.cs, PhysicalAthleteRig.cs, PoweredJointController.cs, PhysicalBarbell.cs, PhysicsTickDriver and docs/ARCHITECTURE.md at main@687112b.
- Lynch & Park, *Modern Robotics* (2017): Jacobians (ch. 5), constrained dynamics (ch. 8), hybrid force/motion control (ch. 11), grasping/contact limits (ch. 12).
- Thomas/Zeni/Winter, *Biomechanics and Motor Control* (5th ed.): anthropometry and multibody moments (ch. 4–5), model validation (ch. 10).
- Batlle/Barjau, *Rigid Body Dynamics*: rigid-body inertias and constraint ill-conditioning (ch. 2–4).
- Millington, *Game Physics Engine Development*: contact and iterative-joint stability (ch. 14–17).
- Åström/Murray, *Feedback Systems*: explicit plant, feedback limitations and architectures.
- Unity 6000.3 ConfigurableJoint and PhysX 5.4 D6 documentation: finite drives, solver limits, no projection as hidden correction.
- IPF rules effective 01 March 2026 (v3): competition START, PRESS and RACK.

**Design is frozen, but unmeasured source-specific drive stiffness/damping and the resulting realized torque/force capacity are NOT fabricated as qualified parameters.**