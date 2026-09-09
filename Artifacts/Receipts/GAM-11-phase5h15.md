# GAM-11 Phase 5H15 — spinal gravity-equilibrium bias identification and reference tracking repair

Start `ac3f8bd` · Checkpoint `checkpoint/gam11-pre-spine-equilibrium-bias-identification`

**Gravity is the disturbance, and compensating it restores the accepted GAM-10
spinal posture.** At 25 kg the bottom now holds 36.16° of world trunk pitch
against a 35.97° reference, where H14 measured 46.10°. No stiffness, damping,
ceiling, capacity, solver, limit, balance or GAM-10 value changed, and no
integral term was added.

## §§6–9 — is it actually gravity

H14 inferred gravity from the drives not being saturated. That inference is not
sound alone: a trunk joint also carries bar load, coupled distal reactions and
contact terms. So the gravitational generalized moment was measured directly —
Σ (r_com − r_joint) × (m g) over the gravitationally distal bodies, projected on
each joint's calibrated flexion axis, from engine masses and `worldCenterOfMass`.

Ten held samples, five phases × two loads, fresh reset each, held 400 ticks and
averaged over the last 100. **All ten settle** (≤ 0.056° of travel across the
window).

| load | s_q | abd error | τ_g abd | ratio |
|---|---|---|---|---|
| 0 | 0.25 | 3.39 | 40.5 | 0.0838 |
| 0 | 0.55 | 7.63 | 88.3 | 0.0864 |
| 0 | 0.80 | 8.33 | 96.0 | 0.0868 |
| 0 | 1.00 | 8.50 | 97.6 | 0.0871 |
| 25 | 0.00 | −6.95 | −80.4 | 0.0865 |
| 25 | 0.25 | 2.21 | 27.0 | 0.0818 |
| 25 | 0.55 | 10.81 | 130.2 | 0.0830 |
| 25 | 0.80 | 11.99 | 144.1 | 0.0832 |
| 25 | 1.00 | 9.29 | 132.7 | 0.0700 |

Sign agrees **10 of 10**, including the two samples where the moment is negative
and the spine leans *back* — the case a gravity-independent explanation would
not predict. Excluding the unloaded standing sample (τ only −10.3 N·m, ratio
noise-dominated), the ratio spans 0.070–0.087 deg/N·m: a **1.24× spread across a
27–144 N·m moment range and both loads**. That is a finite compliance of roughly
12 N·m/deg.

`GRAVITY_EQUILIBRIUM_CAUSALITY = HIGH.`

### Two measurement bugs fixed before trusting this

The settled test first keyed on peak instantaneous angular acceleration, which
at a 100 Hz tick differences solver jitter and reported *every* sample as
unsettled; it now keys on net joint travel. And the causality check compared the
error against the *negated* moment, inverting the expected relationship — both
quantities are about the same axis and a finite spring yields along the moment
applied to it. The compensating bias is the opposite sign; that inversion
belongs in the solve.

## §10 — coupled 2×2 response

The spine is two joints in series carrying the same distal mass, so a target
offset does not buy an equal actual correction. `J[i][j] = d(actual_i)/d(bias_j)`,
central difference on ±2°, fresh reset per perturbation:

| load | s_q | Jaa | Jat | Jta | Jtt | det | cond |
|---|---|---|---|---|---|---|---|
| 0 | 0.55 | 1.190 | 0.122 | 0.103 | 1.104 | 1.302 | 1.2 |
| 0 | 1.00 | 1.052 | 0.103 | 0.071 | 1.081 | 1.130 | 1.2 |
| 25 | 0.55 | 1.391 | 0.278 | 0.237 | 1.228 | 1.642 | 1.5 |
| 25 | 0.80 | 1.352 | 0.253 | 0.211 | 1.207 | 1.579 | 1.5 |
| 25 | 1.00 | 0.510 | **−0.269** | 0.182 | 1.183 | 0.652 | 2.3 |

Diagonal-dominant and well conditioned everywhere except the loaded bottom.
Coupling is real but modest (0.10–0.39) and rises with load. Diagonals exceed 1,
so assuming unity would have under-compensated.

## §§11–12 — required bias and physics cross-check

From `J u = −e0`:

| load | s_q | u_abd | u_tho | bound |
|---|---|---|---|---|
| 0 | 0.00 | +0.83 | +0.92 | soft |
| 0 | 0.55 | −6.11 | −3.03 | soft–hard |
| 0 | 1.00 | −7.74 | −3.59 | soft–hard |
| 25 | 0.00 | +3.93 | +3.67 | soft |
| 25 | 0.80 | −8.17 | −3.66 | soft–hard |
| 25 | 1.00 | **−19.15** | −1.79 | **OVER — not qualified** |

`PHYSICS_PRIOR_AGREEMENT = YES.` The prior τ_g/K_eff with K_eff ≈ 12 N·m/deg
predicts −8.0° at 0 kg bottom and −12.0° at 25 kg s_q 0.80 against measured
−7.74 and −8.17: same sign, same phase trend, same order. The empirical solve is
smaller at load because the diagonals exceed one and the coupling shares work
between the two joints — which is exactly why the identification was not
replaced by the prior.

## §§15–17 — representation

`BIAS_PHASE_DEPENDENT = YES` (0 → −7.74° across the descent, a 5× moment range).
`BIAS_LOAD_DEPENDENT = YES`, but **not additively**: the 0→25 kg delta is −1.08°
at s_q 0.55 and −1.44° at 0.80 yet **+1.05°** at 0.25. So the permitted
`bodyweight(s_q) + load_component` decomposition is *not* supported and was not
used. The representation is a 2-D table of measured knots, linear in phase,
linear in load between the two identified columns, **clamped at 25 kg** so 105 kg
cannot be reached by extrapolation.

### Two deliberate departures from the raw solve

**Standing held at zero at both loads**, though the measurement asks +0.83°
(0 kg) and +3.93° (25 kg). With the bar the spine leans back rather than
forward, so the miss is real — but §20 treats moving a qualified standing
setpoint as a material plant change, and it should not arrive as a side effect
of repairing the bottom. Recorded, not applied.

**Loaded bottom holds the s_q 0.80 value.** Its own solve returned −19.15°
against a 12° hard bound, on the one ill-conditioned sample. Holding the deepest
qualified neighbour beats an unqualified extrapolation.

## §13 — static validation

| load | s_q | abd err off → on | tho err off → on | trunk off → on |
|---|---|---|---|---|
| 0 | 0.55 | 7.63 → −0.12 | 3.97 → −0.08 | 40.85 → 34.36 |
| 0 | 1.00 | 8.50 → −1.19 | 4.43 → −0.35 | 45.56 → 37.46 |
| 25 | 0.80 | 11.97 → −0.44 | 6.14 → −0.29 | 45.43 → 35.45 |
| 25 | 1.00 | 9.29 → −0.43 | 5.59 → −0.28 | 42.72 → 35.47 |

Support 0.2900 m at every sample, every hold still settles, saddle seated,
standing moves 0.02° unloaded and 0.00° at 25 kg. Hip keeps its own droop
(uncompensated, out of scope) but does not worsen: improves at **7 of 8** deep
samples, most at 25 kg s_q 0.80 (−6.69 → −5.11), worsens by 0.21° at one.

## §§21–23 — dynamic validation and visual

| bottom | H14 | H15 | reference |
|---|---|---|---|
| world trunk 0 kg | 45.68 | **38.22** | 35.97 |
| world trunk 25 kg | 46.10 | **36.16** | 35.97 |
| abdomen err 0 kg | +8.59 | **−0.77** | — |
| abdomen err 25 kg | +12.12 | **−0.11** | — |
| thorax err 25 kg | +6.69 | **−0.07** | — |

Support 0.2900 m with 8 contacts throughout, heels down, saddle seated, hip
error −0.14° (0 kg) and −1.07° (25 kg) at the bottom.

`VISUAL_AGENT_VERDICT = MOVED_TOWARD_GAM10.` The deep pose is an organized
low-bar squat — spine braced rather than folded, feet flat, bar seated across
the back. The forward lean that remains is the reference's own 35.97°, which
§5 and §16 protect. Owner visual acceptance remains final.

### Two honest residuals

Both follow from holding standing at zero. At 25 kg standing the trunk still
sits 9.44° behind its reference. At 25 kg s_q 0.25 it sits 7.26° behind, because
between standing and the first knot the true requirement crosses zero and
interpolating from an anchored zero undershoots through the crossing. Both are
in the early descent, not at depth.

## §14 — thresholds

`OWNER_THRESHOLD_REQUIRED = YES.` No project-qualified abdomen/thorax tolerance
exists and none was invented. The result is stated as measured before/after
reduction plus rendered comparison.

## §§26–27 — regression

- MasterSpec `STATUS=PASS`, 68 files.
- EditMode **67/67**.
- Full PlayMode with a graphics device **151/153**; the two failures are the
  pre-existing 105 kg and E3 preload-only cases, with byte-identical messages.
- Physical squat gates **0 kg PASS**, **25 kg PASS**.
- Performance: hard budgets pass, controller P95 0.075 ms, physics P95 0.405 ms.
  The bias evaluation is two table walks and a lerp — no allocation, no runtime
  solve, no matrix inversion.

Frozen and unchanged: GAM-10, H13 guard and proximal trigger, target-to-COP
gain, balance gains and bounds, phase rate, joint K/D, maxForce, capacityScale,
solver iterations, anatomical limits, joint axes, collision policy, mass/COM/
inertia, feet, platform, bar and saddle. No AddForce, AddTorque, velocity write,
transform write, kinematic support or integral controller. 105 kg out of scope.

## Next first cause

Hip equilibrium droop. The hip carries the same uncompensated gravitational
moment the spine did — 4.9° at the unloaded bottom, 6.7° at 25 kg s_q 0.80 —
and its preload is still zero. The same identification applies. Second: the
standing spine bias this phase deliberately withheld, which needs its own
standing-gate authorization.
