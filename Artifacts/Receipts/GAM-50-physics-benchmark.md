# GAM-50 — Physics Benchmark, Audit & Repair Qualification

AUTHORITY=Linear GAM-50 (execution contract); GAM-10 / GAM-12 / GAM-49 sealed and unchanged
BRANCH=work/gam-13-squat-load-calibration
FROZEN_START=3cb11848f8260cf40d8f576ecf182478e0fc63f0
BASELINE_SHA=332fc9e554790007252866bfe7e54b929cd62d94 (benchmark baseline; production physics identical to frozen start)
PHYSICS_MEASUREMENT_SHA=68d30852f02f4005a9e915a2dbb18c4c27abb941
VALIDATED_CODE_CANDIDATE_SHA=776feac0d13dec34c43e02ba88abd576713cf93d
POST_CANDIDATE_EVIDENCE_COMMIT_SHA=1a596f7be7e88d3cc7411a40864b057e00a4a0df
TERMINAL_VERIFICATION_TESTED_SHA=776feac0d13dec34c43e02ba88abd576713cf93d
EXIT_STATE=FINAL_VERIFICATION_REGRESSION
GAM50_LINEAR_STATUS=In Progress
PHYSICS_SUBSTRATE=FROZEN_PENDING_FINAL_PROCEDURAL_SEAL

## Verdict

The historical 68d3085 measurement and 776feac code-candidate evidence remain
unchanged. The terminal exact-candidate rerun at 776feac returned
`Failed(Child)`, 19/26 Physics Benchmark tests passed and 7 failed while writing
raw result files. All 7 report `DirectoryNotFoundException`; the independent
oracle exported 24 cases with 648 metrics and all 50/50 gated metrics passing.
The 60 kg B14 run passed. The chain stopped there, so repeatability, 170/300 kg,
full EditMode, GAM-12, and GAM-49 were not run in this terminal chain.

Disposition: `FINAL_VERIFICATION_REGRESSION`. The failure is in benchmark
evidence output, not a failed physics tolerance. GAM-50 remains In Progress;
the substrate stays frozen and no physics/controller repair was made.

## What was built

Permanent Physics Benchmark V1 (`docs/PHYSICS_BENCHMARK_V1.md`):

- `Assets/Tests/PhysicsBenchmarks/` — B01-B11 isolated engine cases, B16 substrate A/B, B12/B13 shared-athlete standing and static canonical pose matrix with oracle export, B17 Editor/standalone parity harness.
- `Tools/Benchmarks/` — `Run-PhysicsBenchmark.ps1` (tiers, sweeps, DynamicsManager experiment overrides), `Run-PhysicsBenchmarkBaseline.ps1`, `Compare-PhysicsBenchmark.py` (manifest, results, failure matrix with evidence-carrying localization, before/after), `PhysicsOracle.py` (independent quasi-static Newton-Euler), `SquatBenchmark.py` (B14 mechanics + lockout extension, B15 determinism), `Compare-Parity.py`.
- `Artifacts/Benchmarks/Physics/<sha>/` — compact `manifest.json`, aggregate results, failure matrix, and summary. Generated run/raw/sweep files stay local or external; their source tree and inventory digests are in `Artifacts/Receipts/GAM-50-evidence-compaction.json`.

## Baseline (332fc9e) — earliest causal layers

| layer | finding | evidence |
|---:|---|---|
| 3 | `targetAngularVelocity` written as −ω: every drive damper targeted −ω_ref | B07: +1 rad/s commanded, −1.0 realised |
| 4 | swing demand modeled on the YZ magnitude; PhysX clamps each swing axis | B06: per-axis prediction matches to 0.02% |
| 5 | athlete solved at 28 PGS iterations: drives deliver 20-60% of K·e in the grounded closed chain | oracle ankle 4-5×, abdomen 2.5-4.4×, hips/knees 1.2-2×; thorax 1.02 validates the oracle; 255 iterations → 1.00-1.03 |
| 6 | legacy patch friction ≈ 2μ; improved patch friction creeps at 0.9 μN | B08 slip threshold 2.0-2.2 μN; creep 4-5 mm/s |
| 7 | saddle `currentForce` reads 0 N; saddle angular damping ratio 0.10-0.38 | B10, B10b |
| 8 | one 10 ms step leaves grounded ankles 5-12 N·m short at the 255 cap | oracle pair sums; 0.6-4.2 N·m at 5 ms |
| 9 | 140 kg standing bar sway 0.016 m/s; 170 kg never settles; 300 kg collapses | B12 |
| 11 | 140 kg misses lockout stillness; 170/300 kg balance loss | B14 |

140 kg lockout benchmark (sealed 0.020 m/s unchanged, unchanged simulation continued 3 s):
**PHYSICS_NOT_SETTLING** — steady +0.009 m/s bar creep (knee error drifting 0.041 → 0.019 rad) and ~0.09 m/s, ~3 s COM sway; predicates touched for 3 ticks at +106 and were lost again (stillness failed 237/300 ticks). Root cause: the layer-5 unconverged-drive creep, not the window.

## Repairs (one root cause per commit)

| id | layer | commit | repair | verified by |
|---|---:|---|---|---|
| R1 | 3 | `58a24f5371b56dd34d03cf6e7bb1ab7146a14195` | write `targetAngularVelocity` with its own sign (single seam) | B07 12/12 |
| R2 | 4 | `68d63a722dc3eb7c7e7ad727bcb065e1cffaa1bd` | per-axis swing demand model | B06 8/8; actuator contract 4/4 |
| R3 | 5 | `e7c1b2cce0ede1db5da150c3ea920814f5b4011d` | athlete position iterations 28 → 255 | B11/B16, B12 all loads, oracle |
| R4 | 5 | — | TGS solver **rejected** (bar buzz 0.03-0.05 m/s, 170/300 kg collapse) | sweeps/tgs-* |
| R5 | 6 | `48a4256f0d7b40dfdf11e855f18c289aeb57519a` | improved patch friction (removes 2× over-friction) | B08 threshold ≈ μN |
| R5b | 6 | `afeeb6e6cf665601f3d9b73bba28387384ec4795` | two-directional Coulomb friction (removes near-cone creep) | B08 6/6, B09 8/8 |
| R6 | 7 | `a260cdc1e09754790e323035bd810bc185759ed4` | saddle publishes modeled load-path force | B10 2% of m g |
| R7 | 7 | `1ef6cc9ee3856922a266dcdf39e6ce1aa86505a6` | saddle angular damper 100 → 800 (ζ ≥ 0.77 at every load) | B10b 35/35 |
| R8 | 5/8 | `67fc307b23054766be6dabae021804df2cc08f36` | two 5 ms PhysX substeps per 10 ms authoritative tick | oracle 170/170 |
| R9 | 10 | `bfacd17fd7cb720c5c29e489f7ce11d6fd40b408` | static gravity compensation from measured statics (replaces fitted lever-arm trim) | B13 0-170 kg green |
| CAL | 11 | `784e9aacdb7640687425a14467e1e0604618a851` | single intrinsic strength scale 5.13 → 0.75 | B14 envelope |
| build | — | `e2c25ab8a8671049b83b1c4762b5f02c5689d7aa`, `f6bd35c50d69cff41010711fa3cb516007821688` | two pre-existing shipping-compile errors (no player could build) | player compile |

Final-candidate test-only verification commits: `4330bca4e3cc62f6a83ea9c87908e15836509990` (obsolete GAM-13 knee assertion aligned with accepted V2-3M profile) and `776feac0d13dec34c43e02ba88abd576713cf93d` (GAM-12 lifecycle test driver now holds player intent and steps foot contacts). Neither changes production physics.

Benchmark-model corrections (each exposed once the engine behaved correctly; no tolerance loosened, all documented in their commits): B06 onset transient and held-case expectation; B08 slope sign/timing; B11/B16 12 s window and reference refinement; B11 settling relative to the converged reference; B05 gated on the exact implicit-drive recurrence plus dt convergence (as B01 is); oracle bilateral pair sums; B13 0.5 s release window; held-pose qualification 0-140 kg with 170/300 kg characterised per GAM-13 V2-5.

Rejected / unselected experiments: TGS (bar buzz and 170/300 kg regressions);
strength arms 0.72, 0.78, 0.80, 0.85, and 0.90 (single intrinsic 0.75
retained); and ArticulationBody migration (no practical advantage under the
production friction model). None is part of the production configuration.

## Numerical convergence — `CONVERGED_ENOUGH`

Production is dt=0.010 s authoritative tick, 2 PhysX substeps of 0.005 s,
athlete position/velocity iterations 255/1, intrinsic strength scale 0.75.
Final-scale sweeps are in
`Artifacts/Benchmarks/Physics/776feac/convergence-summary.json` and `sweeps/`.

**CONVERGED_ENOUGH.** Production vs 0.005 s standing at 0–140 kg differs by
at most 0.000154272 rad tracking, 0.000046254 m pelvis sag, and 0.000084974 m
AP support margin. After aligning squat traces at the first DESCENT command,
25/140 kg event times agree within 15 ms, paired bar paths within 2.252 mm,
and bottom height within 0.184 mm. Absolute startup differs by 0.255 s because
the reference uses a fixed 50-tick setup at half the tick duration.

The current-strength oracle comparison converges toward 255 position
iterations: mean relative error is 0.168429 at 56, 0.0579246 at 128, 0.0215102
at 192, and 0.00892024 at 255; maximum absolute error falls from 37.9881 Nm to
11.7335, 6.2559, and 4.73351 Nm. Production has zero gated oracle errors.
Velocity iteration 1/2/4 physical metric deltas are negligible for measured
0–140 kg cases. The 2/4-iteration sweep XMLs are explicitly **1/6 passed**
each: the shared override also changes the barbell velocity iteration count
from production 6, causing five production-config assertions to fail. The
physical sensitivity values are retained; the sweeps are not called passing.
At 300 kg, dt=0.020 s has 0.876952 m pelvis drop vs 0.827390 m at production
and demand fraction 4.64276 vs 0.533946, materially worse for that reported
characterization. No thresholds were loosened.

## ConfigurableJoint vs ArticulationBody (B16)

**KEEP ConfigurableJoint; migration is not authorized.** The ConfigurableJoint
arm passes 45/45 gated metrics against the declared per-joint realization
tolerance `max(5% of analytic expected angle, 0.002 rad)`; its maximum gated
error is 0.0001868 rad. Its maximum anchor separation across the wider sweep
is 0.0115 mm against the 2 mm declared limit, so no hidden stretch blocker
remains. Production-cell CPU is comparable (ConfigurableJoint
0.122–0.174 ms; ArticulationBody 0.052–0.187 ms). The ArticulationBody
candidate has no material practical advantage and its driven arm under the
production two-directional friction model is inert / retains large residual
motion. The evidence does not support migration.

## Determinism

**QUALIFIED for same-platform Windows Editor repeatability.** Ten fresh Unity
processes per load were run at 25 kg and 140 kg. Each load had 10/10 categorical
agreement, identical legal-depth/reversal/ascent/lockout ticks, zero metric
range, and one identical per-tick state-hash sequence across all processes.
25 kg: ticks 347/390/405/731, 731 hash rows, sequence SHA-256
`6f9cd363818b8bf18116b6253709a9b9fdf63438b433f86f3b34e1a8e6e6f714`. 140 kg:
335/392/406/733, 733 rows, SHA-256
`56158c44889b03c9c79b7415d7eeb6a948bfed055a5876c2179bc507ae6f6872`. No
authoritative balance failure onset/latch occurred. Full per-run bar, COM,
support, active-drive, and state-hash evidence is in
`Artifacts/Benchmarks/Physics/776feac/repeatability-summary.json`. This claim
is same-platform process repeatability; cross-runtime parity remains blocked.

## Final mechanics (GAM-13 V2-5 envelope, strength 0.75)

| Load | Physical outcome | Key event ticks | Result |
|---:|---|---|---|
| 25 kg | `PHYSICAL_LOCKOUT` | legal 347, reversal 390, ascent 405, lockout 731 | Easy clean completion. |
| 60 kg | `PHYSICAL_LOCKOUT` | legal 341, reversal 390, ascent 405, lockout 731 | Moderate clean completion. |
| 140 kg | `PHYSICAL_LOCKOUT` | legal 335, reversal 392, ascent 406, lockout 733 | Heavy completion. |
| 170 kg | `PHYSICAL_LOCKOUT` | legal 333, reversal 393, ascent 407, lockout 734 | Near-max completion. The official detector returns `NoStickingRegion`; no detector-resolvable recoverable sticking region is present. This remains a GAM-13 controller/gameplay limitation. |
| 300 kg | `SETUP_NOT_PHYSICALLY_QUALIFIED` | setup failure at tick 500; no descent/depth/reversal/ascent/lockout | Physical loaded-setup collapse: bar y=0.223943 m, COM y=0.224578 m, AP rear support margin −1.214544 m. Not a mid-ascent stall. |

Physical terminal, COM/support, and active-squat drive metrics are recorded in
`Artifacts/Benchmarks/Physics/776feac/raw/canonical-mechanics.json`. The 170 kg
detector parameters and noise threshold are in
`Artifacts/Benchmarks/Physics/776feac/raw/B14-170-sticking-detector.json`.

## Previous suite closure (historical evidence before the terminal rerun)

| Final suite on candidate 776feac | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| Physics Benchmark V1 invocations (B01-B16, squat and 20 repeat runs) | 47 | 47 | 0 | 0 |
| Full EditMode | 251 | 251 | 0 | 0 |
| GAM-12 rules/failure/lifecycle/observation trace PlayMode | 4 | 4 | 0 | 0 |
| GAM-49 depth-provider PlayMode | 2 | 2 | 0 | 0 |
| PoweredJointActuatorContractTests (included in full EditMode) | 4 | 4 | 0 | 0 |

The previous Physics V1 record comprises 32 cases and 7,023 metric rows, with
863/863 gated metrics passing and an empty failure matrix. Historical suite XML,
logs, and raw traces remain local or retrievable from the pre-cleanup source
tree; they are not kept in the final Git tree. The compact receipt and evidence
manifest preserve the run provenance and digests. The subset count above is
included in—not added again to—the historical full EditMode total.

## Terminal exact-candidate verification — 2026-10-02

| Gate | Result | Detail |
|---|---|---|
| Physics Benchmark V1 `All` tier | `Failed(Child)`, 19/26 passed | 7 failed with `DirectoryNotFoundException` while writing raw output: B05 step-response trace and B13 no-balance-feedback exports at 0/25/60/140/170/300 kg. No recorded physics tolerance failed. |
| Independent dynamics oracle | 648 metrics; 50/50 gated pass | 24 cases exported; raw oracle SHA-256 `e260f801f09bca9152a95f6866333ea3f9ddbc62aba3385ded35059638033e15`. |
| B14 squat, 60 kg | Passed, 1/1 | Other B14 loads were stopped after the Physics tier failed. |
| B15 repeatability | Not run | Stopped after the Physics tier failed. |
| Full EditMode | Not run | Stopped after the Physics tier failed. |
| GAM-12 PlayMode | Not run | Stopped after the Physics tier failed. |
| GAM-49 PlayMode | Not run | Stopped after the Physics tier failed. |

Tested candidate SHA: `776feac0d13dec34c43e02ba88abd576713cf93d`.
The later evidence-only commit is `1a596f7be7e88d3cc7411a40864b057e00a4a0df`.
The 86-file local run bundle is at
`Artifacts/Benchmarks/Physics/776feac/final-verification/terminal-776feac-20261002/physics-core/runs/20261002-053422-gam50-final/`
(5,801,847 bytes; inventory SHA-256
`f18dfa91006d1e163b307cf8e26a1c55e367260c642a855dbf00c9014bdb19cf`).
`test-results.xml` SHA-256:
`e6428e29bd039771ad2fe18198df78b59decf14963ff932ebe38fd540a1b06d7`.
The compact machine receipt is
`Artifacts/Receipts/GAM-50-terminal-verification.json` (SHA-256
`911cc26d36416d7dd71031391102dee7a52ca01bc83c0d1ecfae00fad75985db`); the
generated-evidence compaction manifest is
`Artifacts/Receipts/GAM-50-evidence-compaction.json` (SHA-256
`a406eadd016b624abcef5d32d3260fe26cd844f13e3ee2de344415625df5eac6`).

## Not done / limits

- **Windows standalone parity: `BLOCKED_BY_TOOLCHAIN`.** Unity 6000.3.22f1
  lacks the installed Hub module **Windows Build Support (IL2CPP)**. The exact
  missing path is
  `D:\Dev\Unity\6000.3.22f1\Editor\Data\il2cpp\build\deploy`; its
  `UnityLinker.exe` is absent. It was not installed or modified. After adding
  the module, run from the repository root:

  ```powershell
  $evidenceRoot = 'Artifacts\Benchmarks\Physics\776feac'
  $unity = 'D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe'
  & $unity -batchmode -quit -projectPath 'E:\Data\Projects\PowerliftingSimulator' -executeMethod GAM50ParityBuild.BuildWindowsParity -logFile "$evidenceRoot\windows-parity-build.log"
  $standaloneDir = Join-Path $evidenceRoot 'parity-standalone'
  $editorRawDir = Join-Path $evidenceRoot 'parity-editor-raw'
  New-Item -ItemType Directory -Force $standaloneDir, $editorRawDir | Out-Null
  & "$PWD\Builds\GAM50\Windows\PowerliftingSimulator-GAM50-Parity.exe" -gam50Parity -gam50ParityLoad 25 -gam50ParityOutput "$standaloneDir\parity-standalone-025kg.csv"
  & "$PWD\Builds\GAM50\Windows\PowerliftingSimulator-GAM50-Parity.exe" -gam50Parity -gam50ParityLoad 140 -gam50ParityOutput "$standaloneDir\parity-standalone-140kg.csv"
  $oldRawDir = $env:PHYSICS_BENCHMARK_RAW_DIR
  $env:PHYSICS_BENCHMARK_RAW_DIR = "$PWD\$editorRawDir"
  try { & $unity -batchmode -nographics -projectPath 'E:\Data\Projects\PowerliftingSimulator' -runTests -testPlatform playmode -testFilter EditorStandaloneParityBenchmark -testResults "$PWD\$evidenceRoot\parity-editor-results.xml" -logFile "$PWD\$evidenceRoot\parity-editor.log" }
  finally { $env:PHYSICS_BENCHMARK_RAW_DIR = $oldRawDir }
  python 'Tools\Benchmarks\Compare-Parity.py' --editor-dir "$PWD\$editorRawDir" --standalone-dir "$PWD\$standaloneDir" --raw-dir "$PWD\$evidenceRoot\raw" --loads 025 140
  ```

  The parity gates are identical state sequence, lockout within 2 ticks, bar
  height and COM within 1 mm, and joints within 0.005 rad; bit identity is
  informational. Same-platform Editor repeatability remains qualified.
- **Emergent sticking:** the official 10 Hz / 4σ bar-velocity detector does
  not resolve a valid Vmax1-Dmax1-Vmin-Vmax2 sequence at 170 kg. The raw trace
  has a velocity trough, but it does not clear the stationary-noise threshold.
  This is remaining GAM-13 gameplay/controller acceptance, not a demonstrated
  substrate defect.
- **300 kg:** the athlete loses physical support during loaded setup at tick
  500; the evidence does not show a mid-ascent sticking event.

## Repository evidence compaction

The pre-cleanup audit compared `1a596f7be7e88d3cc7411a40864b057e00a4a0df`
with `origin/main` at `4cf5fc5fe9bd7e668e363da35cf4656202da233f`. It found
3,150 changed paths, 2,028,932 added lines, and 674 deleted lines. The generated
evidence category accounted for 2,754 files, 1,863,189 added lines, and
317,850,507 bytes. The complete category totals and top-25 path lists are in
`Artifacts/Receipts/GAM-50-repository-footprint-audit.json` (SHA-256
`f1f54d8fb55b0e7f460bed9bdc87e09af15549dc3ff9af58312b48066d11ca90`).

The cleanup removes 3,339 generated files from the Git index: 329,317,338 bytes
and 1,932,725 text lines in the source tree, including 585 inherited files.
The staged Git deletion diff records 1,935,064 removed lines.
Existing workspace files are preserved locally and ignored by the repository
policy. `Artifacts/Receipts/GAM-50-evidence-compaction.json` records source-tree
identity, per-directory SHA-256 inventory roots, and the baseline comparison.
`Tools/Spec/Verify-MasterSpec.ps1` now runs the tracked-artifact hygiene guard.

## Claim ceiling

Historical qualification evidence covers the Physics Benchmark V1 cases,
calibrated shared athlete and squat scenarios, and same-platform repeatability
on Windows Editor with Unity 6000.3.22f1. This terminal closeout remains in
`FINAL_VERIFICATION_REGRESSION` because the exact-candidate output-writing gates
failed; GAM-50 is not Done. The physical substrate remains frozen. Windows
standalone parity is blocked by the missing IL2CPP module, 170 kg has no
detector-resolvable sticking region, and 300 kg collapses during setup. Do not
retune physics in GAM-13.
