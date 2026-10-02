# GAM-13 posture / equilibrium isolation — predeclared arms and rules

`GAM13_POSTURE_EQUILIBRIUM_ISOLATION_V1`

Written and committed **before** any arm below was executed. The onset
predicates, sampling and evidence language are unchanged from
`GAM13_CAUSAL_ONSET_PREDICATES_V1`
(`GAM-13-causal-onset-predicates.md`); this note only adds the arms, the
classification rules for them, and the predictions they are tested against.

## Fixed plant

Saddle V2 (`GAM13_SADDLE_BAR_LIMB_FILTER_V2`), athlete solver 28/1, bar 12/6,
GAM-7 joint impedance (1x), `GAM13_SQUAT_ATHLETE_CAPACITY_V1`,
`GAM13_SQUAT_EQUILIBRIUM_FEEDFORWARD_V1` with the 5H16 standing knots, the
production balance controller and posture guard. Fresh scene per run, SETUP
hold at `s_q = 0`, 600 ticks, Stage-A contract from tick 100.

The V2 baseline was reproduced first: `causal-summary-baseline.csv` and all
five trace and body files are bit-identical to the committed
`causal-audit/v2-baseline/` evidence.

## Arms (one property each, relative to the named plant)

| Arm | Plant | Property changed |
|---|---|---|
| A0 | 1x | none (instrumentation parity; must stay bit-identical to the baseline) |
| G1 | 1x | `balance.posture_guard=false` — the guard never scales the balance command (posture and limit halves both off) |
| E1 | 1x | `equilibrium.target=v2_trim` — the five standing biases (ankle, knee, hip, abdomen, thorax) are the committed V2 trim solution for that load (`causal-audit/v2-trim/trim-canonical-solutions.csv`); diagnostic per-load values, never production |
| K3 | 3x | none — one fixed load-bearing impedance for every load: ankle, knee, hip and trunk spring x3, damper x sqrt(3) (constant damping ratio); feed-forward, capacity, guard and solver unchanged |
| C  | 3x | an equilibrium change on top of K3 — run **only** if G1/E1/K3 show both the stiffer plant and the equilibrium change are independently necessary |

## Classification rules

Guard (G1 against A0, per load; the solver-quantitative band from the causal
audit is 5 ticks):

- `PREVENTS` — A0 had a capture departure (production AP proxy or 2-D hull)
  and G1 has none in 600 ticks, with support retained.
- `DELAYS` — both have it and G1's onset is more than 5 ticks later.
- `NO_CHANGE` — both have it within 5 ticks, or neither has it, with the same
  Stage-A class.
- Anything else (earlier onset, new failure) is reported as `WORSENS`.

Equilibrium demand (runtime audit against the V2 trim, per canonical load):

- `LOWER_CHAIN_MISSING` — the runtime knee or hip standing bias differs from
  the trim by 3 degrees or more at a load that fails.
- `BOUND_REACHED` — a runtime or trim standing bias is within 0.1 degree of
  the 12 degree hard limit.
- `STIFFNESS_INSUFFICIENT_1X` — the static posture chain (knee, hip, lumbar,
  thorax, shanks held; bilateral joints summed) has an indefinite
  `K - G` at the spawn pose, where `G` is the gravity stiffness from the
  measured joint anchors, body masses and centres of mass; or E1 fails at
  that load. `K - G` indefinite means no target bias can hold the pose: the
  spring is weaker than the load's toppling stiffness.
- The required static bias of the same chain is `b = K^-1 tau`, with `tau`
  the gravity moments at the spawn pose. It is reported per load and plant.

3x (K3): `PRESERVES_25_60` if 25 and 60 kg pass Stage-A; `FIXES_140_170` if
both pass; `IMPROVES_300` if 300 kg passes or its first capture/posture
onset moves more than 5 ticks later with a later or absent support loss.

## Qualification contract (unchanged Stage-A plus four explicit checks)

Measured window ticks 100–599. Stage-A pass (upright, support retained,
AP capture margin > 0.01 m, COM speed < 0.25 m/s, foot pitch < 12 deg,
posture error < 10 deg, limit proximity < 0.95, saturated fraction < 0.05,
saddle separation < 0.05 m) **and** 2-D hull capture margin > 0.01 m **and**
no load-bearing modeled demand >= 0.95 **and** saddle linear-limit occupancy
< 0.95 **and** finite telemetry. Three fresh repeats per load.

## Predictions (before execution)

1. G1 does not prevent the 140/170/300 kg capture failure. The fold is
   posture-first and, by the static estimate below, the 1x spine cannot hold
   the loaded trunk whatever the ankle does; with the guard off the ankle
   keeps its COP authority, so capture departure is at most delayed. 25 kg
   stays PASS and 60 kg keeps failing only the posture contract.
2. The runtime standing bias is spine-only: ankle, knee and hip are 0 at every
   load while the V2 trim needs knee -6.9 to -12 and hip +9 to +12 deg at
   140–300 kg, and the spine is at the bound at 300 kg. Expected class:
   `LOWER_CHAIN_MISSING` + `BOUND_REACHED` + `STIFFNESS_INSUFFICIENT_1X`.
3. Hand estimate from the V2 25 kg trace (lumbar ~1.05 m, thorax joint
   ~1.21 m, bar 1.40 m and 13–14 cm behind the spine, upper body 54 kg):
   with 800 N m/rad per spine joint `K - G` is indefinite from about 100 kg
   of bar, so E1 fails at 140/170/300 kg on 1x.
4. At 3x the same chain stays positive definite up to 300 kg and the static
   spine bias falls to about a third of the 1x need (roughly 9–10 deg at
   300 kg), so K3 is expected to hold 140 and 170 kg. 300 kg is uncertain:
   its system COM sits near the rear of the support polygon and the
   feed-forward was identified on the 1x plant.

## Amendment V1.1 — 300 kg on the 3x plant (committed before these arms ran)

Executed so far (evidence under `Artifacts/Measurements/GAM-13/posture-equilibrium/`):
A0 bit-identical to the baseline bodies; G1 = NO_CHANGE at 25/60/300,
DELAYS at 140/170, gross posture onset unmoved; E1 fails 60/140/170/300 on
1x; K3 passes and qualifies 25/60/140/170 and fails 300. On K3 the 300 kg
chain is guard withdrawal @19 > capture exit (rear) @43 > gross posture @144 >
support loss @196: posture now holds, and the first event is the guard
removing ankle authority while the system COM, latched as the balance
reference at the first supported tick, sits 3 cm in front of the heel edge.

Two findings shape the remaining arms:

- The canonical posture error (Stage-A contract and guard input) is taken
  against `Nominal x GravityBias`. At static equilibrium it therefore reads the
  spring deflection `tau/k`, not the deviation from the canonical pose; the
  fixture now also reports the true canonical deviation (diagnostic only,
  not a pass criterion).
- On K3 the 1x-identified spine table flexes the spine 2.8, 5.7, 8.2 and 7.7
  deg off canonical at 25/60/140/170 kg (Stage-A still passes).

Added arms, all on the fixed 3x plant, all five loads:

| Arm | Property changed on K3 |
|---|---|
| K3G | `balance.posture_guard=false` |
| T3  | offline: the unchanged `GAM13_STATIC_TRIM_SOLVER_V1` run from scratch on the V2 plant at 3x (`GAM13_TRIM_IMPEDANCE=3`), +/-12 deg, same grid |
| C3  | `equilibrium.target=v2_trim_3x` — the five T3 canonical biases, diagnostic per-load values |

Rules: a load is fixed by an arm if it qualifies under the contract above.
Equilibrium compensation is independently necessary at 300 kg if K3 fails
there and C3 qualifies; the guard change is necessary if K3 fails and K3G
qualifies. Only a necessary change may reach production, and only as one
smooth bounded law of physical system mass (equilibrium) or one fixed
behaviour (guard).

Predictions: K3G delays the 300 kg capture exit but does not fix it (the
latched COM reference leaves about 0.3 cm between the COM and the rearmost
admissible COP). T3 converges at 25–170 kg with biases well inside the bound
and needs a forward ankle bias at 300 kg. C3 at 300 kg is uncertain: the
trim's ankle lean is opposed by the latched COM reference once balance is on.
