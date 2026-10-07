# Powerlifting Simulator

A Unity physics-driven powerlifting prototype with an articulated athlete,
physical barbell, and squat simulation. This repository does not contain a
complete three-lift game.

After cloning, run `git lfs pull` to materialize the model and textures.
Open the project in Unity **6000.3.22f1** (URP). The squat prototype scene is
`Assets/Scenes/Prototype/SquatPhysicalPrototype.unity`; the build scene list is
in `ProjectSettings/EditorBuildSettings.asset`.

[Architecture](docs/ARCHITECTURE.md) describes the ownership boundaries.
Run EditMode and PlayMode suites through Unity's Test Runner. Batch EditMode
and non-graphics PlayMode subsets may use
`-batchmode -nographics -projectPath <repo> -runTests` with
`-testPlatform editmode` (or `playmode`), `-testResults <output.xml>`,
and `-logFile <output.log>`; omit `-quit` for test runs. Graphics-dependent
PlayMode tests must run with a graphics device. Physics benchmarks use
`Tools/Benchmarks/Run-PhysicsBenchmark.ps1` with `-Tier Isolated`, `Athlete`,
or `Squat` and an optional `-UnityExecutable <path>`.

Project history and decisions live in Linear; implementation provenance lives
in Git and pull requests. Generated evidence under `Artifacts/` stays local.

All rights reserved unless otherwise stated. The imported athlete's terms and
source record are in [License_Standard.txt](Assets/Characters/Athlete/Source/License_Standard.txt)
and [PROVENANCE.md](Assets/Characters/Athlete/Source/PROVENANCE.md).
