---
name: powerlifting-foundation
description: repository-specific operating knowledge for the Powerlifting Simulator shared engineering foundation
---

# Purpose

Keep GAM-2 foundation changes aligned with the frozen master specification and
with the contracts already enforced in this repository. This skill covers the
engine-independent simulation contracts and their minimal Unity adapter; it
does not cover lifts, athletes, presentation, or later gameplay systems.

# Use When

Use this skill when changing the fixed-step runtime, manual physics-scene
ownership, player intent buffering, physical observations, or the foundation
bootstrap. Read the master specification and the repository constitution
before changing a frozen contract.

# Canonical Authorities

- `docs/REPOSITORY_CONSTITUTION.md`
- `docs/IMPLEMENTATION_PROTOCOL.md`
- `docs/master-spec/POWERLIFTING_SIMULATOR_MASTER_SPEC_V1/`
- `docs/baselines/UNITY_BASELINE.md`
- `docs/baselines/GAM-2_PACKAGE_FREEZE.md`

# Assembly Graph

`PowerliftingSimulator.Foundation` is engine-independent and has
`noEngineReferences`. `PowerliftingSimulator.Foundation.Unity` references it
and `Unity.InputSystem`; it owns Unity scene and physics integration. The
EditMode and PlayMode test assemblies reference the foundation assemblies and
are not runtime production layers. No presentation or gameplay assembly exists
for GAM-2.

# Namespace

The root namespace is `PowerliftingSimulator`. Foundation contracts are under
`PowerliftingSimulator.Foundation`; Unity integration is under
`PowerliftingSimulator.Foundation.Unity`.

# Coordinate / Unit Contract

Internal quantities are SI: meters, kilograms, seconds, radians, meters per
second, and radians per second. World axes are right-handed: +Y up, +Z
athlete-forward, and +X athlete-right. The canonical frame identifiers are
`W`, `B_i`, `J_i`, `R_i`, `M`, and `BAR`. The executable value types and
conversions live in `Assets/Scripts/Foundation/CoordinatesAndUnits.cs`.

# Simulation Runtime Contract

`SimulationClock` derives simulation time from the monotonic tick, using the
frozen 100 Hz / 0.01 s step in `SimulationConstants`. `PhysicsTickDriver` is
the single production step owner. Its `StepOne` samples the intent frame,
simulates one local Unity `PhysicsScene` step, then publishes a copied
observation. Render catch-up is capped at four ticks and 0.04 s; there is no
`FixedUpdate` owner. `ProjectSettings/TimeManager.asset` intentionally retains
Unity's ordinary 0.02 s global fixed timestep. The foundation does not depend
on that global value because its authoritative local scene always steps at
0.01 s.

# Physics Scene Ownership

`AuthoritativePhysicsScene` creates a `LocalPhysicsMode.Physics3D` scene without
changing the global `Physics.simulationMode`. Project settings retain ordinary
automatic default-scene ownership (`m_AutoSimulation: 1`); the local scene is
explicitly stepped by `PhysicsTickDriver.StepOne`. A Unity 6000.3.22f1 fixture
with simple Rigidbody/SphereCollider probes verified both independent advance
and a bounded Script-mode transition without double-stepping the default scene.
The production-wide ownership test excludes test/generated/build paths and
requires exactly one `.Simulate(...)` call at that `StepOne` owner. Reset
restores the small registered probe at the reset boundary and shutdown unloads
the local scene.

# Input Boundary

`InputTimeDomain` maps each monotonic realtime render interval `[R0, R1]` onto
the accepted simulation interval `[S0, S1]`. An event at `E` maps affinely to
`S0 + ((E - R0) / (R1 - R0)) * (S1 - S0)`. The standalone path derives
`S1 = S0 + min(R1 - R0, 0.04 s)`; the runtime path supplies the authoritative
horizon after accounting for pending fractional time. Events must belong to
the current interval and remain ordered. Reopening the same interval resets
only event-order progress, preserving the original real interval and accepted
horizon so retry does not accept wall time twice.

`IntentBuffer` stores edges in a fixed-capacity ordered 64-event ring. Eligible
edges are exposed in consumption order, consumed exactly once, and update held
state; an edge older than the current contiguous fixed interval is rejected
instead of silently reassigned. Continuous input has five channels with a
five-entry history per channel (`MaxCatchUpTicksPerRenderFrame + 1`, 25 total).
The latest value in each eligibility-tick bucket replaces only that bucket's
newest value, preserving the history needed by four-tick catch-up while keeping
memory bounded. Continuous values persist after consumption and are clamped to
their semantic ranges.

`UnityIntentInputAdapter` is the only Input System boundary. It stages callbacks
in sequence order, maps the complete capture, and calls `IntentBuffer.ApplyBatch`,
which validates before mutating. Capture checkpoints the time mapper; rejection
restores that checkpoint and retains staged callbacks for retry. Reset clears
staged input and sequence state, establishes a new realtime epoch, and rejects
callbacks timestamped before that epoch. It maps `Gameplay` actions `Brace`,
`Yield`, `Drive`, `Balance`, `Grip`, `Confirm`, and `Abort`; physics code does
not read devices.

# Quaternion Contract

`QuaternionValue` rejects non-finite components at normalization/inversion,
normalizes before transform and comparison operations, and canonicalizes the
double cover by making the first significant component in `(W, X, Y, Z)`
positive. The 1e-6 tie threshold gives deterministic pi-boundary identity, so
`q` and `-q` canonicalize identically and have zero shortest-arc error. The
shortest-arc calculation preserves small real rotations and crosses the
plus/minus-pi boundary on the short path.

# Observation Boundary

`PhysicalObservation` and `PhysicalBodyObservation` are readonly value
contracts. They contain only post-step copied state, tick/time metadata, world
frame, and SI units. `AuthoritativePhysicsScene.CaptureObservation` is the
Unity-to-foundation conversion boundary; later systems must not treat live
Unity component references as observations.

# GAM-8 Observation and Trace Evolution

`AuthoritativePhysicsScene` captures every registered body in stable
registration order after the single local `PhysicsScene.Simulate` call.
`PhysicalObservation.Bodies` is a copied read-only collection with `BodyCount`,
`BodyAt`, and `TryGetBody`; it exposes values only, never Rigidbody references.
The bar's durable identity is `barbell`, while `PrimaryBody` keeps the existing
foundation convenience semantics.

`AttemptTrace` is schema `GAM8_ATTEMPT_TRACE_V1` with a fixed default capacity
of 3000 samples. `PhysicsTickDriver` appends an immutable observation paired
with the already sampled `PlayerIntentFrame` only after simulation and
publication. Appends require recording mode, the matching tick, and strict
monotonic order; a full trace fails instead of overwriting its oldest state.
`BeginRecording`, `EndRecording`, and `Clear` are explicit attempt/reset
boundaries. Trace reads cannot influence physics.

The recorded-state replay seam is presentation-only: the GAM-8 bar trail reads
captured BAR poses into a main-scene `LineRenderer` with no Rigidbody and no
resimulation. It is not an input replay system or a second physics scene.

# Canonical Validation Commands

From the repository root:

- `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Tools\Spec\Verify-MasterSpec.ps1`
- Unity 6000.3.22f1 batch compile/import with `-batchmode -nographics -quit`.
- Unity Test Framework batch runs with `-runTests -testPlatform editmode` and
  `-runTests -testPlatform playmode`, each with `-testResults`; omit `-quit`
  because this Test Framework version warns that it prevents command-line test
  execution.
- `git diff --check`

# Known Repository Traps

- Unity may add `com.unity.dt.app-ui` to `ProjectSettings/EditorBuildSettings.asset`
  and change `ProjectSettings/ProjectSettings.asset` while importing; revert
  only churn absent from the pre-Unity candidate. Preserve the intentional
  `ProjectSettings/TimeManager.asset` 0.02 s decision.
- In Unity 6000.3.22f1, `PhysicsScene.Simulate(float)` returns `void`; do not
  write a boolean-return check around it.
- On a clean `Library/Bee` regeneration, the first backend probe can return 4
  with `Require frontend run`; this is preparatory when the frontend and second
  backend run follow and `Tundra build success` plus return code 0 are recorded.
- `Unity.exe` is a GUI process on Windows. Launch it with an explicit process
  handle and validate the fresh log/XML; a blank PowerShell `$LASTEXITCODE`
  does not prove completion.
- Test Framework runs must omit `-quit`. Accept results only from a fresh,
  parseable completed XML, and stop only that exact Unity PID if it lingers
  after writing the result.
- The Codex environment may expose pre-existing untracked `.agents/` tooling
  and `skills-lock.json`; stage only the intended GAM-2 candidate and the
  project-local `powerlifting-foundation` skill.

# Verified Lessons

- Keep the pure assembly separate because dependency direction is a useful
  executable boundary even before a non-Unity consumer exists.
- Use a tiny Rigidbody probe for actual manual-step and lifecycle tests; do not
  introduce an athlete, bar, lift phase, or presentation seam at foundation
  stage.
- Treat floating-point accumulator comparisons with an explicit small tolerance
  while deriving authoritative simulation time from integer ticks.
- In Unity 6000.3.22f1, a local `PhysicsScene` can be explicitly stepped while
  the default scene remains under `FixedUpdate`; preserve that result with the
  minimal fixture and production-wide ownership test.
- Reset the input mapper and buffer together at an attempt boundary so fresh
  realtime samples re-anchor instead of being compared with the prior epoch.

# Evolution Rules

Update this file only with repository facts verified by source, tests, or
authoritative docs. Every future change must preserve one production physics
step owner, the SI/frame contract, the fixed tick mapping, and the observation
boundary. Add later-domain guidance only when its issue is implemented and
tested; do not pre-seed future architecture here.

# Last Verified

2026-08-30 after Unity 6000.3.22f1 Level 1 Bee regeneration and compile/import:
affected EditMode 2/2, full EditMode 38/38, full PlayMode 15/15, sole production
`.Simulate` count 1, global fixed timestep 0.02 s, authoritative local timestep
0.01 s, and master-spec verification all PASS. These are repository validation
results and do not decide later architecture.

2026-08-31 after GAM-8 qualification on the same Unity version: complete
registered-body observation was 17 bodies in stable order; the bounded trace
passed monotonic/immutability/neutrality checks; the existing athlete remained
the primary body and sole pre-physics callback owner.
