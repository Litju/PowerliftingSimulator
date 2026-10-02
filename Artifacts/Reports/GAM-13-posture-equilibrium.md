# GAM-13 loaded standing — posture guard, equilibrium and plant isolation

Mission `GAM13_POSTURE_EQUILIBRIUM_FIX`, main PC, Unity 6000.3.22f1
(`1c726e1fb402`), headless PhysX, 100 Hz, athlete solver 28/1, bar 12/6,
Saddle V2. Arms, rules and predictions were committed before execution in
`Artifacts/Research/GAM-13-posture-equilibrium-isolation.md`
(`579cce6`, amended `87be4dd` before the 3x follow-up arms). Evidence:
`Artifacts/Measurements/GAM-13/posture-equilibrium/`; every derived table
regenerates with

```text
python Tools/Spec/Analyze-GAM13PostureEquilibrium.py \
  --arm A0=<a0> --arm G1=<g1> --arm E1=<e1> --arm K3=<k3> --arm K3G=<k3g> --arm C3=<c3> \
  --guard-arm G1 --stability-arm A0 --output <analysis>
```

Outcome: **BLOCKED at 300 kg.** A fixed 3x load-bearing plant, with the
feed-forward, guard, capacity and solver unchanged, qualifies 25/60/140/170 kg
standing in three fresh repeats. No tested change qualifies 300 kg, and the
static analysis places 300 kg above what a 3x plant can hold. The same 3x
plant also costs the 25 kg attempt its legal depth. No production code
changed.

## 1. Baseline

The V2 causal baseline was reproduced first: `causal-summary-baseline.csv`
and all five trace and body files are bit-identical to the committed
`causal-audit/v2-baseline/`. With the new instrumentation (A0) the bodies stay
bit-identical at every tick; the only trace difference is the tick-0
pre-physics balance diagnostics of the first scene in a process, computed
from the scene-load observation and never reaching the plant.

| Load | Stage-A | Posture error | Outcome |
|---:|---|---:|---|
| 25 | PASS | 4.03 deg | upright |
| 60 | FAIL | 12.17 deg | upright; posture contract only |
| 140 | FAIL | 47.4 deg | guard@14 > posture@22 > capture@76 |
| 170 | FAIL | 49.0 deg | guard@14 > capture@73 > support loss@192 |
| 300 | FAIL | 75.8 deg | guard@14 > posture gross@34 > capture@63 > support loss@139 |

## 2. Posture guard alone (G1, 1x)

`balance.posture_guard=false`: the guard never scales the balance command.
Saddle V2, 1x impedance, feed-forward, capacity and solver unchanged.

| Load | First capture onset A0 → G1 | Gross posture A0 → G1 | Support loss A0 → G1 | Class |
|---:|---|---|---|---|
| 25 | none → none | none → none | none → none | NO_CHANGE (PASS) |
| 60 | none → none | none → none | none → none | NO_CHANGE (posture 12.19 deg) |
| 140 | 76 → 100 | 51 → 53 | none → none | DELAYS |
| 170 | 73 → 85 | 45 → 45 | 192 → 210 | DELAYS |
| 300 | 63 → 68 | 34 → 33 | 139 → 143 | NO_CHANGE |

The guard only delays the capture exit; the trunk folds at the same tick with
or without it. On the 3x plant the same change (K3G) leaves 25–170 kg
qualified and 300 kg failing. The guard stays in production unchanged.

Why it withdraws early: the canonical posture error it reads is taken against
`Nominal x GravityBias`. At static equilibrium target minus actual is the
spring deflection `tau/k`, so the reading grows with load whatever the pose,
and passes the guard's 8 deg full-withdrawal point on the loaded settle
transient. At 60 kg on 1x the abdomen reads 11.35 deg while it is 4 deg from
its canonical angle.

## 3. Runtime standing equilibrium (1x) against the V2 trim

Anatomical degrees (ankle, knee, hip, abdomen, thorax). The composed gravity
bias rotation matches the preload in every joint and never drifts.

| Load | Runtime (production) | V2 trim | Runtime − trim |
|---:|---|---|---|
| 25 | 0, 0, 0, 3.93, 3.67 | −2.9, 2.0, −0.6, 3.5, 3.5 | 2.9, −2.0, 0.6, 0.4, 0.2 |
| 60 | 0, 0, 0, 7.40, 6.60 | −1.1, 0.0, 0.5, 4.5, 4.1 | 1.1, 0.0, −0.5, 2.9, 2.5 |
| 140 | 0, 0, 0, 11.2, 10.3 | 0.0, −6.9, 9.0, 11.8, 10.9 | 0.0, **6.9, −9.0**, −0.6, −0.6 |
| 170 | 0, 0, 0, 11.8, 11.0 | 2.1, −9.1, **12, 12, 12** | −2.1, **9.1, −12**, −0.2, −1.0 |
| 300 | 0, 0, 0, **12, 12** | **12, −12, 12**, 9.1, **12** | **−12, 12, −12**, 2.9, 0.0 |

Static posture chain at the spawn pose (shanks held; knee and hip pairs
summed; gravity stiffness `G` from the logged anchors, masses and centres of
mass; `b = K^-1 tau`):

| Load | Required knee, hip, abdomen, thorax at 1x | Spine / hip+spine margin at 1x | at 3x | Smallest stable factor spine / hip+spine / +knee |
|---:|---|---|---|---|
| 25 | −0.5, 0.4, 2.4, 3.0 | +0.53 / +0.39 | +0.84 / +0.80 | 0.47 / 0.61 / 0.87 |
| 60 | −1.5, 1.2, 4.9, 5.8 | +0.26 / +0.04 | +0.75 / +0.68 | 0.74 / 0.96 / 1.33 |
| 140 | −3.7, 3.1, 10.8, **12.4** | **−0.36 / −0.74** | +0.55 / +0.42 | 1.36 / 1.74 / 2.39 |
| 170 | −4.6, 3.9, **13.0, 14.9** | **−0.59 / −1.04** | +0.47 / +0.32 | 1.59 / 2.04 / 2.78 |
| 300 | −8.2, 7.0, **22.6, 25.6** | **−1.58 / −2.32** | +0.14 / **−0.11** | 2.58 / **3.32** / 4.50 |

The hip+spine margin has the sign of every upright/fold outcome in this
mission (10/10 load-plant pairs): positive wherever the athlete stays upright
(including 60 kg on 1x, +0.04) and negative wherever the trunk folds
(140/170/300 on 1x, 300 on 3x, where the rear capture exit comes first).
The knee chain is more conservative (it ignores the knee's −5 deg stop and
the ankle loop).

Classification: heavy standing on 1x fails for a **combination**:

- `LOWER_CHAIN_MISSING` — knee and hip are 0 at runtime; the trim needs
  −6.9 to −12 and +9 to +12 deg at 140–300 kg.
- `BOUND_REACHED` — the runtime spine is at 12 deg at 300 kg; the trim is at
  the bound at 170 and 300 kg; the static spine need is 12.4–25.6 deg from
  140 kg.
- `STIFFNESS_INSUFFICIENT_1X` — the loaded spine alone is statically unstable
  from 140 kg (no bias can hold an indefinite `K − G`), and because the
  Stage-A posture error reads `tau/k`, the 10 deg contract cannot be met at
  1x from 140 kg even on the canonical pose.

## 4. Whole-body equilibrium on 1x (E1)

The five V2 trim biases per load (diagnostic, applied exactly): 25 kg PASS;
60 kg worse (posture 16.9 deg); 140/170/300 kg still collapse, although
140 and 170 kg are strongly delayed (capture 76 → 275 and 73 → 205, gross
posture 51 → 246 and 45 → 120). **Equilibrium compensation alone cannot fix
1x.**

## 5. One fixed stronger plant (K3, 3x)

Ankle, knee, hip and trunk springs x3, dampers x sqrt(3); every load; nothing
else changed. Three fresh repeats are bit-identical.

| Load | Stage-A | Posture error | 2-D hull capture | Max LB demand | Saddle linear occupancy | Spine off canonical | Qualified |
|---:|---|---:|---:|---:|---:|---:|---|
| 25 | PASS | 1.23 | 0.107 m | 0.08 | 0.11 | 2.8 deg | YES |
| 60 | PASS | 1.81 | 0.093 m | 0.07 | 0.25 | 5.7 deg | YES |
| 140 | PASS | 3.40 | 0.076 m | 0.09 | 0.57 | 8.2 deg | YES |
| 170 | PASS | 5.02 | 0.072 m | 0.13 | 0.69 | 7.7 deg | YES |
| 300 | FAIL | 20.2 | −1.74 m | 0.70 | 1.02 | — | NO |

`PRESERVES_25_60`, `FIXES_140_170`, `IMPROVES_300` (first onset 34 → 43,
support loss 139 → 196, gross posture 34 → 144) but does not fix it. The
spine sits flexed off canonical because the spine table was identified on 1x.
It stays under 10 deg, and Stage-A passes.

300 kg on 3x is capture-first: guard@19 > rear capture exit@43 > gross
posture@144 > support loss@196. The system COM in the canonical pose sits
3.3 cm ahead of the heel edge and 0.3 cm ahead of the rearmost admissible COP
(the 3 cm interior margin), so the balance loop has no rearward authority.
The hip+spine chain is also below its static threshold (3.32x).

## 6. 300 kg on the 3x plant (predeclared V1.1 arms)

| Arm | 300 kg outcome | 25–170 kg |
|---|---|---|
| K3G `balance.posture_guard=false` | capture 43 → 93, gross posture 144 → 79, support loss 196 → 266; FAIL | all qualified |
| T3 trim from scratch on 3x | converges 25–230 kg inside the bound (max 9.4 deg; smooth in load); 260/300 kg do not converge; 300 kg hold loses support | — |
| C3 T3 biases on 3x | capture 43 → 183, gross posture 144 → 175, support loss 196 → 291; FAIL | 25/60/140 qualified, **170 kg fails the posture contract (11.30 deg)** |

Necessity rules: equilibrium compensation is **not** independently necessary
(K3 already qualifies 25–170 kg without it, and C3 does not qualify 300 kg). The
guard change is not necessary either. Only the stiffer plant is necessary, and
3x is not sufficient at 300 kg. Step 7's condition for combining is not met,
and step 8's production equilibrium law is not warranted. Per the mission,
freezing waits for all five loads, so nothing was promoted.

C3 also shows the posture contract working against compensation: at 170 kg it
holds the pose closer to canonical than K3 (largest joint deviation 4.9 vs
7.7 deg), yet fails the contract at 11.30 deg where K3 reads 5.02 deg, because
the contract measures deflection from the biased target, not the pose.

## 6a. 25 kg lifecycle on the candidate plant (diagnostic)

`GAM13_CANDIDATE_PLANT_25KG_LIFECYCLE` runs the unchanged GAM-12 25 kg
attempt three times on a named plant (`posture-equilibrium/lifecycle/`):

| Plant | Start window | Squat | Descent | Bottom | Ascent | Lockout | Trace | P2 | P3 | Terminal |
|---|---|---:|---:|---:|---:|---:|---:|---|---|---|
| production (x3, identical) | 49–51 | 52 | 80 | 427 | 447 | 791 | 747 | NO_LIFT (FAILED_START_POSITION, SUPPORT_VIOLATION) | NO_PHYSICAL_FAILURE | PHYSICAL_LOCKOUT |
| 3x (x3, identical) | 53–55 | 56 | 80 | 410 | 445 | 771 | 723 | NO_LIFT (FAILED_START_POSITION, SUPPORT_VIOLATION, **INSUFFICIENT_DEPTH**) | NO_PHYSICAL_FAILURE | PHYSICAL_LOCKOUT |

Production reproduces the accepted GAM-12 table exactly. The 3x plant keeps
lockout and P3, but **loses legal depth**. The deep-phase spine and equilibrium
terms were identified on the 1x plant, where the joints comply under load; on
a stiffer plant with the same phase feed-forward the athlete no longer reaches
depth. So the 3x candidate is not promotable as tested even for 25–170 kg: a
stiffer plant needs its phase feed-forward re-identified (dynamic-model
refoundation territory) before 25 kg behaviour can be preserved.

## 7. Capture check (AP only)

On every qualified run the production AP capture proxy and the 2-D hull agree
to within 0.5 mm (e.g. 140 kg on 3x: 0.0768 vs 0.0763 m). They disagreed by up
to 8.4 cm and 38 ticks on the V1 60 kg lateral exit (causal audit), so the AP
proxy is not a valid support-region check. **Stage-A needs a 2-D capture
margin**; this mission's qualification contract already requires it. Required
change, not made here: replace the AP-only `min(CaptureMarginFront,
CaptureMarginRear) > 0.01 m` in the Stage-A contract (the Stage-A fixture and
the causal accumulator) with the signed margin of the capture point to the
plantar contact hull (`SquatSupportGeometry.Measure`) `> 0.01 m`. Balance
control is unchanged.

## 8. Remaining blocker and the decision it needs

300 kg standing has two independent limits on the fixed 3x plant:

1. **Stiffness.** The loaded trunk-on-thigh chain needs at least 3.32x GAM-7
   (4.50x with the knees) to be statically stable at 300 kg; 3x leaves it
   indefinite.
2. **Balance geometry.** In the canonical GAM-10 pose the 300 kg system COM
   is latched as the balance reference 3.3 cm from the heel edge, leaving about
   0.3 cm of rearward COP authority. Real heavy squatters bring the bar over
   mid-foot; the canonical pose does not.

A third constraint applies to whatever plant is chosen: on a stiffer plant the
1x-identified phase feed-forward costs the 25 kg attempt its legal depth
(section 6a).

The mission forbids another plant sweep, and it forbids per-load plants and
balance redesign. The owner has to decide one of: (a) authorize one further
fixed plant value above 3.32x (4.5x clears every chain with margin),
qualified at all five loads, together with re-identifying the phase
feed-forward on that plant so the 25 kg lifecycle keeps legal depth;
(b) re-scope the canonical standing envelope below 300 kg (3x is qualified
to 170 kg; the hip+spine threshold reaches 3x at about 268 kg), still with
the feed-forward re-identification; or (c) authorize a balance
set-point/whole-body lean change for heavy loads as its own mission.
