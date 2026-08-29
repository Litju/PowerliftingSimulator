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
`FixedUpdate` owner.

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

`InputTimeDomain` maps monotonic realtime samples into the simulation timestamp
domain. The first sample after construction or reset anchors at the current
simulation epoch; each positive wall-time gap contributes at most the 0.04 s
catch-up bound. `IntentBuffer` keeps edge events in a fixed-capacity ordered
ring and coalesces each continuous channel to its latest pending state, so
continuous captures cannot exhaust the edge capacity. It consumes edge events
once, carries held state forward, and clamps continuous values to their
semantic ranges. `UnityIntentInputAdapter` is the only Input System boundary;
it maps the `Gameplay` actions `Brace`, `Yield`, `Drive`, `Balance`, `Grip`,
`Confirm`, and `Abort` into the buffer. Physics code does not read devices.

# Observation Boundary

`PhysicalObservation` and `PhysicalBodyObservation` are readonly value
contracts. They contain only post-step copied state, tick/time metadata, world
frame, and SI units. `AuthoritativePhysicsScene.CaptureObservation` is the
Unity-to-foundation conversion boundary; later systems must not treat live
Unity component references as observations.

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
  and change the Standalone scripting define while importing; remove that
  unrelated churn before review.
- In Unity 6000.3.22f1, `PhysicsScene.Simulate(float)` returns `void`; do not
  write a boolean-return check around it.
- The Codex environment may expose pre-existing untracked `.agents/` tooling
  and `skills-lock.json`; stage only the single project skill created for
  GAM-2.

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

2026-08-29 after Unity 6000.3.22f1 compile/import, the ownership fixture,
EditMode 16/16, PlayMode 10/10, and master-spec verification PASS. These are
repository validation results for the current repair evidence; they do not
replace owner review or decide later architecture.
