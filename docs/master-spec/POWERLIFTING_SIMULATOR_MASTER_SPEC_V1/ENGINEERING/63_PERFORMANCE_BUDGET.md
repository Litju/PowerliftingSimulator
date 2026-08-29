# Performance Budget

**Document ID:** `PSMS-ENG-63`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `03_COORDINATES_UNITS_NUMERICS.md`, `ENGINEERING/60_RUNTIME_EXECUTION_ORDER.md`, `PRESENTATION/50_RENDER_ARCHITECTURE.md`, `ENGINEERING/62_TESTING_AND_TERMINUS.md`

## Repository verification

- Freeze reference/minimum hardware and graphics profile.
- Measure current M2 baseline and every M3–M8 milestone.
- Verify ProfilerRecorder/standalone measurement tooling in the target Unity version.

## Target

Reference target: Windows desktop, 1920×1080, 60 fps presentation, 100 Hz physics, one hero athlete/bar, competition
venue, replay/analysis. Hardware floor is frozen during implementation after measuring the actual target market and current
project.

## Frame budget

60 fps gives 16.67 ms. CPU and GPU run partly in parallel; budgets are gates, not a requirement to sum naively.

| Subsystem | p95 target |
|---|---:|
| full CPU frame | ≤ 10.0 ms |
| GPU frame | ≤ 12.0 ms |
| one 100 Hz physics tick | ≤ 2.0 ms |
| worst normal 4-tick catch-up physics work | ≤ 8.0 ms |
| input/controller/reference/capacity per tick | ≤ 0.25 ms |
| post-physics observation/rules/failure/snapshot per tick | ≤ 0.40 ms |
| visible pose + visual rig | ≤ 0.75 ms |
| camera/UI/audio | ≤ 1.0 ms |
| managed allocation active attempt | 0 B/tick; near-zero/frame after warm-up |

p99 and worst are also recorded. A single accepted worst frame may be higher during scene loading only, not active lifting.

## Physics budget strategy

- approximately 16 athlete bodies plus one bar;
- primitive colliders;
- limited self-collision;
- no online optimization/Jacobians/HQP;
- finite joint drives;
- solver iterations raised selectively;
- no per-tick object creation;
- isolated local scene;
- one step authority;
- diagnostic channels ring-buffered.

100 Hz is retained only if p95/p99 and standalone visual quality pass. The fallback is controlled scope/solver/collider
optimization—not silently changing `h` per machine. A future quality profile may offer a separately qualified fixed rate.

## Rendering budget strategy

- one hero skinned character;
- moderate venue geometry;
- static batching/GPU instancing;
- controlled shadow distance/casters;
- modest URP post-processing;
- pooled VFX/audio;
- no extra active gameplay cameras;
- MaterialPropertyBlock instead of material copies;
- LOD/culling for crowd/venue.

## Memory/storage

Initial release budgets to verify:

| Category | Budget |
|---|---:|
| runtime working set | ≤ 2 GB reference target |
| managed GC during active attempt | 0 B/tick |
| normal attempt trace | ≤ 25 MB uncompressed target |
| replay decode working memory | ≤ 64 MB target |
| scene transition peak | measured and ≤ platform limit |
| save profile | ≤ 5 MB excluding replays |

These are engineering targets, not user-facing claims.

## Loading

- load venue/athlete/equipment before attempt;
- no synchronous asset load during active lift;
- preload command/audio/VFX;
- async scene transition with progress;
- warm shader variants where needed;
- first-attempt hitch test after cold boot.

## Profiling protocol

1. Development build with profiler markers for diagnosis.
2. Release/non-development build for acceptance.
3. Warm-up then 30+ repeated attempts per lift scenario.
4. Record p50/p95/p99/worst CPU/GPU/physics/GC/memory.
5. Include light/heavy/failure, replay, meet, and career transitions.
6. Capture profiler data and receipt hashes.
7. Test target/minimum hardware once frozen.

Historical M2 frame-time evidence is context, not a permanent guarantee after physical athlete integration.

## Overload behavior

Accumulator/catch-up is bounded. If the game cannot keep up:

- keep fixed `h`;
- cap catch-up;
- record overload;
- reduce render quality only through declared profile;
- avoid spiral of death;
- never skip rules/snapshots for a simulated tick;
- release build may pause and report a technical performance fault under extreme persistent overload rather than run an
  invalid simulation.

## Soak

At least 60 minutes through repeated attempts/replays/menus:

- no increasing memory trend;
- no stale physics/contact state;
- no audio/source leaks;
- no growing trace/event buffers;
- stable reset;
- no thermal/frame degradation beyond hardware variance.

## Tests

Budget marker presence; active allocation; trace capacity; 4-tick stress; quality profile physics invariance; cold/warm
load; soak; GPU/CPU independence; replay seek; worst-case championship venue; unsupported hardware warning.

## Scope

**SHIP_V1:** budgets/profiling above.  
**LATER:** platform-specific profiles and deeper jobification.  
**RESEARCH:** GPU physics/advanced crowds.  
**OUT_OF_SCOPE:** premature optimization that changes product architecture without evidence.
