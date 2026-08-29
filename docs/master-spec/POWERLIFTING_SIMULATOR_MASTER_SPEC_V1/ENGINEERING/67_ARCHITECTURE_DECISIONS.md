# Architecture Decisions and Adversarial Review

**Document ID:** `PSMS-ENG-67`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `00_READ_ME_FIRST.md`, `02_GAME_ARCHITECTURE.md`, `ENGINEERING/60_RUNTIME_EXECUTION_ORDER.md`, `ENGINEERING/62_TESTING_AND_TERMINUS.md`, `ENGINEERING/65_CLAIMS_PROVENANCE.md`

## Repository verification

- Create formal ADR files/records in the repository if this consolidated decision log is split.
- Close every open decision with code/config/test/source evidence.
- Do not reinterpret rejected alternatives during implementation without a new owner-approved ADR.

## Decision record format

Each ADR states decision, context, alternatives, consequences, verification, and reversal trigger. The list below is the
frozen V1 authority.

## ADR-001 — This is a game

**Decision:** player-facing human powerlifting game with scientific honesty.  
**Rejected:** robotics research platform, clinical simulator, force-plate replacement.  
**Consequence:** complexity must earn visible/mechanical/game value.

## ADR-002 — Real humanoid from the beginning

**Decision:** Quaternius `Superhero_Male_FullBody` is canonical visible athlete.  
**Rejected:** mannequin-first pipeline with later skin replacement.  
**Risk:** asset proportions/rig limitations.  
**Verification:** physical/visible overlay and all lift gates.

## ADR-003 — Three-rig ownership

**Decision:** hidden reference, hidden physical, visible follower.  
**Rejected:** Animator and physics on same transforms; mannequin proxy unrelated to visible rig.  
**Consequence:** explicit bind mapping and one-frame render pipeline.

## ADR-004 — One physical authority

**Decision:** finite ConfigurableJoint drives/couplings configured by one adapter; PhysX realizes movement.  
**Rejected:** additive torque controllers, root lock, foot pins, hidden support, transform squat, scripted bar velocity.

## ADR-005 — PhysX, not custom solver

**Decision:** use Unity/PhysX forward dynamics.  
**Rejected for shipping:** HQP, KKT, CWC/AWP/FWP, custom floating-base inverse dynamics.  
**Preserve:** research notes and lessons.

## ADR-006 — Isolated manually stepped PhysicsScene

**Decision:** 100 Hz local scene and one tick driver.  
**Rejected:** uncontrolled FixedUpdate ownership and project-wide implicit stepping.  
**Open verification:** exact repository scene/bootstrap integration.

## ADR-007 — Separate lift domains

**Decision:** shared athlete/bar/telemetry lifecycle only; squat, bench, conventional deadlift have independent state,
contact, biomechanics, controls, rules, failures.  
**Rejected:** parameterized generic `LiftDefinition`.

## ADR-008 — Reference is intent

**Decision:** biomechanically informed target motion; actual motion may deviate/stall/fail.  
**Rejected:** root motion/animation enforcement.

## ADR-009 — Finite force drives

**Decision:** `useAcceleration=false`, finite maximumForce, family tuning.  
**Rejected:** infinite/acceleration authority as default.  
**Caveat:** Unity drive parameters are not exact biological torque.

## ADR-010 — currentTorque is diagnostic

**Decision:** exclude from utilization, fatigue, success, and scientific claims.  
**Rejected:** treating it as measured athlete torque.

## ADR-011 — Minimal capacity model first

**Decision:** mass-scaled calibrated family capacity, activation, bounded attempt fatigue.  
**Later:** angle/velocity curves only if required.  
**Rejected V1:** detailed muscle model.

## ADR-012 — One physical bar

**Decision:** one dynamic rigid bar, compound colliders, physical plates/equipment contacts.  
**Rejected:** per-plate bodies, kinematic bar phases, render flex affecting truth.

## ADR-013 — Lift-specific coupling

**Decision:** squat finite back saddle; bench and deadlift independent compliant bilateral grips.  
**Rejected:** generic coupling abstraction that forces identical topology; hard overconstraints.

## ADR-014 — Input expresses intent

**Decision:** Brace/Yield/Drive/Balance/Grip/Confirm buffered to fixed ticks.  
**Rejected:** direct bones/forces and render-dependent input.

## ADR-015 — Rules from immutable snapshots

**Decision:** versioned current IPF-style processors; proxy limitations explicit.  
**Rejected:** animation tags, UI state, camera pixels, post-safety state.

## ADR-016 — Replay recorded state

**Decision:** 100 Hz state playback/interpolation.  
**Rejected:** input resimulation as player replay authority.

## ADR-017 — Scientific claim ceiling

**Decision:** direct/derived/engineering/game/provisional/not-observable taxonomy.  
**Rejected:** muscle, tendon, true GRF/COP/internal force, clinical/injury claims without validation.

## ADR-018 — Deterministic where possible

**Decision:** deterministic scenarios, pure processors, fixed step, seeds, immutable receipts.  
**Rejected:** random outcome rolls and opaque tuning.  
**Nonclaim:** cross-platform bitwise determinism.

## ADR-019 — Visual evidence is a gate

**Decision:** screenshots/video and owner review required.  
**Rejected:** tests-only completion.

## ADR-020 — Safety after truth freeze

**Decision:** safety/spotter assistance can alter post-failure presentation only after decisive snapshot/judgment state.  
**Rejected:** catching/teleporting bar before recording failure.

## ADR-021 — Conventional deadlift V1

**Decision:** sumo is a future independent domain.  
**Rejected:** stance boolean that pretends mechanics/contact/reference are identical.

## ADR-022 — Bounded scope and waves

**Decision:** ship squat, bench, deadlift, meet, replay/analysis, product loop, polish in ordered vertical waves.  
**Rejected:** full engine rewrite or parallel implementation of every subsystem.

# Adversarial review

## Attack: “Three rigs are overengineered; animate the visible body directly.”

That recreates dual ownership. The reference must continue even when physics deviates, and the visible mesh must show
physics. Three roles are logically necessary even if Unity hierarchy implementation is optimized.

## Attack: “A kinematic pelvis would make the game stable.”

It would hide support, destroy load-path credibility, and turn failures into scripted joint animation. The gravity-only
test intentionally fails any such solution.

## Attack: “Use a generic lift state machine to reduce code.”

Lifecycle sharing is fine. Mechanical generalization is not: bench has bench/body/feet/two-hand closed-chain contact and
pause; deadlift has grounded/free transition; squat has bar-back/bilateral balance/depth. A generic phase/data asset would
move complexity into unsafe conditionals and weaken tests.

## Attack: “Use true PD torque/currentTorque for scientific metrics.”

PhysX drives are implicit solver constraints and the project already observed that naive explicit PD and currentTorque do
not expose the desired drive truth. Command-side conceptual demand is honest; applied biological torque is unavailable.

## Attack: “Make supra-max failure deterministic by checking load.”

That removes physics as causality and makes technique/input irrelevant. Load sweeps calibrate capacity; the runtime still
executes the physical model.

## Attack: “Two grip constraints will always explode.”

They can if hard and inconsistent. The design uses small compliance, explicit free/limited DOFs, finite caps, no projection,
verified initial alignment, and isolated asymmetry fixtures. If this cannot pass, V1 must simplify the grip topology
explicitly—not hide the instability.

## Attack: “100 Hz is unnecessary.”

It is a current design target for articulated/contact stability and trace resolution. It remains only after standalone
performance evidence. The architecture supports a separately qualified fixed rate, never variable stepping.

## Attack: “The mass fractions and capacity numbers look scientific.”

The mass fractions are source-inspired and redistributed when segment definitions differ. Capacity numbers are explicitly
provisional game seeds. UI/provenance prevents promotion.

## Attack: “The game will still look robotic.”

That risk is real. It is addressed by actual asset fitting, reference quality, finite physical response, render interpolation,
bounded visual secondary layers, camera/audio, and mandatory video review. It is not solved by increasing controller math.

## Attack: “Safety assistance violates no-hidden-support.”

Only if active before failure truth. Post-latch safety is presentation/accessibility and explicitly recorded. A valid lift
cannot receive it.

## Attack: “This bundle is too large to implement.”

The implementation plan does not ask Luna to build all documents at once. It uses vertical waves, terminus gates, and
forbids beginning the next lift before the current one passes. The bundle prevents architectural invention during coding.

# Open decisions requiring repository evidence

1. Exact hierarchy and dimensions of the imported Quaternius asset.
2. Whether the local PhysicsScene can be integrated without disrupting M1/M2 scenes.
3. Exact joint drive modes/gains/solver settings after fixtures.
4. Final grip/saddle DOFs and finite caps.
5. Final 100 Hz performance on reference hardware.
6. Existing rule/trace/save/presentation code to preserve or migrate.
7. Final current IPF page/section locators and equipment/scoring constants.
8. Reference/minimum hardware, build backend, and release platform requirements.
9. Art/audio/font/license inventory.
10. Human playtest calibration.

Until these are closed by evidence, the architecture is conceptually complete but implementation status is
`PASS_WITH_OPEN_DECISIONS`.
