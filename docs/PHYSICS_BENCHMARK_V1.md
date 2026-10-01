# Physics Benchmark V1

Authority: Linear GAM-50 (M3.4B — Physics Benchmark, Audit & Repair Qualification).
GAM-10 reference, GAM-12 rules/failure and GAM-49 depth authority stay sealed; this
benchmark consumes their thresholds and never relaxes them.

Context of use: a mechanically credible, physics-driven powerlifting game. Not a
physiological or neuromuscular simulation.

## What it is

A permanent, layered benchmark of the physics stack the squat runs on, from Newton's
laws up to the full squat. Every metric row carries:

`expected`, `observed`, absolute and relative error, the tolerance, the **source of the
tolerance**, pass/fail, what a failure means, and the **earliest causal layer** it
implicates.

Tolerances come only from analytic solutions, sealed project authority, numerical
convergence, or an explicit engineering/product tolerance stated below. None was chosen
to make the current implementation pass.

## Causal layers

| # | layer | owns |
|---|---|---|
| 1 | units / frames / equations | gravity, integrator, oracle agreement |
| 2 | mass / COM / inertia | α = τ/I, pendulum |
| 3 | joint topology / axes / target convention | targetRotation / targetAngularVelocity sign and frame |
| 4 | drive semantics | K, D, maximumForce, demand model |
| 5 | constraint convergence | chains, anchor stretch, settling |
| 6 | contact / friction | Coulomb thresholds, resting contact |
| 7 | bar / saddle load path | saddle statics, reported forces |
| 8 | timestep / solver convergence | dt and iteration sweeps |
| 9 | athlete equilibrium | standing, static pose matrix, lockout settling |
| 10 | controller | balance, trims, phase control |
| 11 | gameplay qualification | 25/60/140/170/300 kg mechanics |

A failure is repaired at the earliest layer the evidence implicates. Nothing at layer N
is repaired while a lower layer is red.

## Cases

Isolated cases run in a private PhysX scene (`IsolatedPhysicsWorld`) stepped by explicit
`PhysicsScene.Simulate`, the same ownership model as the production
`AuthoritativePhysicsScene`. They gate at production settings (dt = 0.01 s, athlete
solver 28 position / 1 velocity iterations) and characterise the GAM-50 sweep
(dt 0.020/0.010/0.005; position 14/28/56; velocity 1/2/4) around them.

| id | case | expected from | key tolerances |
|---|---|---|---|
| B01 | free fall | exact semi-implicit Euler `g dt² N(N+1)/2`; first-order bias `g t dt/2` | float32 accumulation bound; bias ratio ±2% |
| B02 | constant torque | `ω = τ t / I` on principal, rotated-tensor and rotated-body axes | 1e-4 relative; off-axis 1e-4 |
| B03 | pendulum | compound period with finite-amplitude series; energy | period 0.5% (engineering); energy 2% over 5 periods at production dt |
| B04 | PD static known load | `K e = m g L cos e` per family at 10/50/90% capacity | sealed realization band 0.95–1.05 (GAM-11 `PhysicalAthleteSolverProfile`) |
| B05 | PD step response | continuous 2nd-order analytic | final 0.5%; RMS ≤ 5% of amplitude (product); overshoot ±2% |
| B06 | maximumForce saturation | twist: `α = (τ_g − F)/I` from a saturated start; swing: production demand model's authority | 5% |
| B07 | target sign/frame | logical joint-space target realised with its own sign; independent world axis-angle | 0.05°/0.1°; 0.01 rad/s |
| B08 | friction | Coulomb incline `a = g(sin − μ cos)`; push threshold `μ N` | stick ≤ 1 mm/s; slide 5–10% |
| B09 | drop/contact | no rebound; `N = m g`; impulse–momentum | 0.02 m/s; 1%; 3% |
| B10 | saddle load path | `sag = m g / k`; reported force `m g`; stillness | 5%; 2%; GAM-12 0.020 m/s within 0.5 s |
| B11 | 3-link driven chain + bar | independent planar statics `K e = τ(q₀+e)`, Hessian stability check | 5% band, 0.002 rad floor; anchors ≤ 2 mm; production within band of dt 0.005 / 56 / 4 |
| B12 | shared athlete standing | 10 s production standing hold, 0–300 kg | sealed GAM-12/13 thresholds + engineering (below) |
| B13 | static canonical pose matrix | 6 phases × 6 loads, canonical q_ref initialised pre-simulation only, held 3 s | same |
| B13-oracle | independent dynamics oracle | quasi-static Newton–Euler outside Unity (`PhysicsOracle.py`) | max(10%, 10 N·m) |
| B14 | full squat mechanics | sealed GAM-13 mechanics probe (GAM-12 lockout, GAM-49 depth) | outcome |
| B14L | lockout extension | unchanged simulation continued 3 s after the existing timeout | sealed lockout predicates incl. 0.020 m/s |
| B15 | determinism | ≥10 fresh processes per load, bit-exact per-tick state hashes | identical hashes, outcomes, event ticks; bar spread ≤ 0.1 mm |
| B16 | substrate A/B | B11 chain as Rigidbody+ConfigurableJoint vs ArticulationBody | static error, stretch, dt/solver sensitivity, repeatability, CPU |

### Athlete-level tolerances

Sealed: COM support margin ≥ −0.02 m, saddle separation ≤ 0.02 m, saddle limit occupancy
< 0.95, joint limit proximity < 0.95, modeled drive demand < 0.95 (GAM-13 qualification
constants); lockout bar stillness 0.020 m/s (GAM-12, unchanged).

Explicit engineering/product: canonical q_ref tracking ≤ 0.10 rad (GAM-13 V2-3M product
tolerance), joint anchor gap ≤ 1 cm, pelvis sag ≤ 5 cm over a held pose, planted-foot
travel ≤ 5 mm, settled horizontal COM speed ≤ 0.020 m/s, canonical foot orientation
within 1° of the registered plantar plane.

The B13 `no_balance_feedback` variant is characterisation only: a stiffness-only ankle
cannot stabilise every load, which is equilibrium physics rather than a substrate defect.

### Oracle model

For each powered joint, the generalized gravity torque about its world flexion axis is
the moment of the weight of its free subtree (child subtree for trunk/neck/arms; parent
side for hips/knees/ankles). The two legs close a loop through the ground; the oracle
splits the upper body equally between them, exact for the sagittal moment of the
symmetric canonical squat. The oracle uses only exported masses, COMs, anchors and axes,
never Unity's solver output.

## Running

```
Tools/Benchmarks/Run-PhysicsBenchmark.ps1 -Tier Isolated
Tools/Benchmarks/Run-PhysicsBenchmark.ps1 -Tier Athlete
Tools/Benchmarks/Run-PhysicsBenchmark.ps1 -Tier Squat -LoadsKg 25,60,140 -Repeats 10
```

Sweeps use `-Scope sweeps/<name>` so they never replace baseline rows, and
`-Environment @{ PHYSICS_BENCHMARK_POS_ITERS = '56' }` (athlete solver override,
pre-simulation only).

Outputs, per commit: `Artifacts/Benchmarks/Physics/<sha>/` with `manifest.json`,
`results.json`, `failure-matrix.json`, `summary.md` and `runs/<run>/raw/`.
`Compare-PhysicsBenchmark.py compare --before A --after B` produces before/after tables.
Repairs are recorded in `Artifacts/Benchmarks/Physics/repairs.json` and flow into the
failure matrix.

## Exit states

`PHYSICS_BENCHMARK_QUALIFIED` or `PHYSICS_SUBSTRATE_NOT_QUALIFIED`, as defined in GAM-50.
The current verdict and its evidence live in `Artifacts/Receipts/GAM-50-*.md`.
