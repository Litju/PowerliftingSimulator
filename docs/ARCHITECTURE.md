# Architecture

## Physical authority

This is a mechanically credible game model, not a physiological simulator.
PhysX owns physical motion. Finite `ConfigurableJoint` drives express intended
motion; actual motion may deviate, stall, or fail. Load alone must not select
an outcome. Animation, cameras, UI, and reference motion do not write physical
transforms or determine lift truth. Visible-human readability and honest
physical/rule causality take priority over additional model complexity.

The athlete has separate reference, physical, and visible rig roles. The
visible rig follows physical state. `PhysicalAthleteDefinition` owns the
segment/joint recipe; `PhysicalAthleteRig` builds the bodies and
`PoweredJointController` is the final drive writer. `SquatPhysicalAdapter`
supplies squat commands through the shared athlete command boundary.
`PhysicalBarbell` owns one dynamic compound-collider bar; `SquatBarSaddle`
provides its finite, compliant back coupling. Feet remain dynamic contacts.

Shared athlete/equipment infrastructure does not imply shared lift semantics:
state, reference, contacts, controls, rules, and failures belong to each lift.
The implemented domain is squat; future-domain designs are not runtime authority.

## Assemblies and time

`Foundation` and `Squat` are engine-independent assemblies with
`noEngineReferences`. `Foundation.Unity` owns scene/input integration;
`Athlete` and `Equipment` own the shared physical substrate; `Squat.Unity`
connects the squat domain to it. Dependencies are declared in their `.asmdef`
files. Editor builders author scenes/assets; test assemblies are separate
from production assemblies.

Internal units are SI, with radians for angles. The world convention is +Y up,
+Z athlete-forward, +X athlete-right. Frame/unit conversions and numerical
tolerances live in `CoordinatesAndUnits.cs`; time constants live there too.
Numerical epsilons, rule margins, and game calibrations remain distinct.

`PhysicsTickDriver.StepOne` is the sole production simulation call site in a
local `PhysicsScene`. Each 100 Hz authoritative tick samples buffered intent,
executes the registered pre-physics owner, performs two 5 ms PhysX substeps,
publishes copied body observations, appends the foundation attempt trace,
and invokes the post-physics observer. The render accumulator accepts at most
four ticks / 40 ms; Unity's global fixed timestep is not this clock.
Tick-0 readiness validation precedes stepping when the physical builder holds
the gate. Reset restores bodies, clock, input epoch, observations, and traces;
the builder must hold and revalidate any reauthored physical setup.

`UnityIntentInputAdapter` maps render-time events to accepted simulation time;
`IntentBuffer` preserves edges and held/continuous intent across catch-up.
Physics consumes `PlayerIntentFrame`, not device state. Repeatability is
qualified for a given build/configuration; cross-platform bit identity is not
assumed.

## Observations and attempt truth

Foundation observation views are copied values valid through the next
publication; retained history belongs in `AttemptTrace`. `SquatObservationCollector`
captures post-physics contact/joint/body diagnostics and appends bounded,
immutable squat snapshots. `SquatAttemptOrchestrator` consumes these snapshots
for start qualification, commands, physical terminal conditions, and trace
finalization. Pure `SquatRuleProcessor` and `SquatFailureDetector` evaluate
the frozen record. Safety/presentation cannot turn a failed record into success.

`SquatDepthLandmarkProvider` is the shared calibrated bone-frame depth authority
for runtime and reference evaluation. Both sides must satisfy the configured
depth predicate; render/camera coordinates are never rule inputs. Ruleset,
calibration, telemetry schema, and detector thresholds are defined/versioned
in executable code and tested there. Replay reads recorded state rather than
resimulating player input.

Modeled drive demand is a command-side diagnostic, not measured biological
torque. `currentTorque` is a solver diagnostic. COM/support and calibrated
landmarks are model-derived quantities; true muscle forces, internal joint
loads, force-plate COP/GRF, and injury prediction are not observable here.

## Verification inputs and provenance

EditMode, PlayMode, and PhysicsBenchmarks are Unity Test Framework assemblies.
Benchmark tolerances and analytic oracles live with their executable cases
and `Tools/Benchmarks` scripts. `repairs.json` and `localization.json` beside
the comparison tool are consumed classification/repair overlays; they do not
change production physics or metric thresholds.

Tests/tools generate measurements, receipts, traces, and screenshots under
ignored `Artifacts/` paths. Explicit historical-evidence tests may require
local non-versioned inputs. Historical paths in unchanged source comments
and overlay provenance refer to prior measurements, not required repository
files; retrieve tracked originals from Git history with `git log --all -- <path>`
and `git show <commit>:<path>`. The imported athlete retains its license and
source provenance beside the asset.
