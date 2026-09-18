# GAM-13 loaded-setup causal audit

Mission `GAM13_CAUSAL_AUDIT_RUNTIME_EXECUTION`, main PC, Unity 6000.3.22f1
(`1c726e1fb402`), headless PhysX, same-machine determinism only.

Predicates: `GAM13_CAUSAL_ONSET_PREDICATES_V1`
(`Artifacts/Research/GAM-13-causal-onset-predicates.md`), committed in
`fa34178` before any trace was executed. Evidence:
`Artifacts/Measurements/GAM-13/causal-audit/` (raw per-tick traces are
`.csv.gz`, LFS). Every derived table regenerates byte-for-byte with
`python Tools/Spec/Analyze-GAM13CausalAudit.py --run-dir <arm> --output <dir> --tag <arm>`.

Language: `OBSERVED` = measured; `SUPPORTED_HYPOTHESIS` = consistent with
discriminating evidence; `ESTABLISHED_CAUSE` = a single-property intervention
moved the predicted downstream failure with confounders fixed.

## 1. Instrumentation qualification

- `08e3e93` (prior host, never run) and `fa34178` compile; support-geometry
  EditMode 5/5.
- The fixture's Stage-A summary at 25/60/140/170/300 kg equals
  `trim-refoundation-stage-a-baseline.csv` to every printed digit, so the
  probes and telemetry do not perturb the physics (OBSERVED).
- `fa34178` corrected the diagnostic saddle angular-occupancy frame and
  replaced the 16/28/40 position arm with the mission's 28/32/40.

## 2. Causal order — production V1 plant (28/1 athlete, 12/6 bar)

Onset ticks (0.01 s). `CONTACT_MODE_CHANGE@1` and `TRACKING_FAILURE@12–22`
fire at every load **including the stable 25 kg hold** (bar/arm contact from
spawn; closed-chain ankle target error), so they are non-discriminating.

| Load | Discriminating canonical order | Supplementary decomposition |
|---:|---|---|
| 25 | none (Stage-A PASS) | bar/arm contact @1 |
| 60 | POSTURE@42 > CAPTURE@118 > SUPPORT_LOSS@202 | guard@18 > posture-joint@42 > **hull capture exit (mediolateral)@80** > trunk gross@87 > COM out of hull@136 > foot lift@158 |
| 140 | POSTURE@21 > CAPTURE@79 > DRIVE_HIGH@94 | guard@13 > trunk gross@48 > COM out of hull@101 |
| 170 | POSTURE@1* > DRIVE_HIGH@65 > CAPTURE@77 > SADDLE_LIMIT@181 | guard@13 > trunk gross@43 > COM out of hull@96 |
| 300 | POSTURE@1* > SADDLE_LIMIT@18 > DRIVE_HIGH@45 > CAPTURE@66 > SUPPORT_LOSS@138 | guard@12 > trunk gross@33 > COM out of hull@82 > ground contact@107 > foot lift@131 |

\* At 170/300 kg the heavy-load equilibrium bias steps the canonical target by
≈12° at tick 0, so the ≥10° posture-joint predicate is true for ticks 1–3 and
re-onsets at ≈16–17; the gross trunk onset is the substantive posture event.

`DRIVE_SATURATION` never fires on a load-bearing joint at any load. At 60 kg
the upper-limb drives are above demand 1 from tick 1 (the arms are loaded by
the bar — §4).

## 3. Solver isolation (bar fixed 12/6)

| Arm | Result |
|---|---|
| 28/4 vs 28/1 | **bit-identical at 25/60/300** — the athlete+bar island runs max(1,4,6)=6 velocity iterations, as predicted |
| 28/8 | same order; onsets move ≤ 5 ticks |
| 32/1, 40/1 | same leading order and Stage-A class; 25/300 onsets move ≤ 3 ticks; 60 kg support loss 202 → 171 → 146 |

`SOLVER_QUANTITATIVE`: convergence changes late timing in the contact-heavy
60 kg run, never the leading order or classification. The retired 24/8 row
changed both budgets and is not used.

## 4. Bar/back load path

- Topology (runtime): `enableCollision=false`; `massScale=connectedMassScale=1`;
  preprocessing on; projection None; break 60 kN / 15 kN·m; anchor (0,0,0) →
  thorax (0,0.08,−0.115). The saddle's bar/limb filter enumerated
  `PhysicalBarbell.GetComponentsInChildren<Collider>()` = **0 colliders**
  (the 9 bar colliders are on the authoritative bar root), so **0 of 135**
  bar/non-thorax pairs were ignored.
- Bar/thorax contact: **none** — 0 callbacks in every run; the shaft
  overlaps the thorax box by ≈2 cm at spawn and hangs 1–4 mm off it once
  settled.
- Bar/limb contact: hands, forearms and upper arms touch the bar from tick 1
  at every load. At 60 kg the hands (|x| ≈ 0.70–0.76 m, outside the 1.31 m
  collar spacing) bear axially on the plate region at 6–8 kN each, in
  opposition (a clamp).
- Joint share by bar momentum elimination (contact normals measured, friction
  bounded μ=0.625), pre-gross-posture medians: 25 kg 1.03 (0.90–1.16),
  140 kg 0.95, 170 kg 0.96, 300 kg 0.98 of the bar's vertical support; the
  limbs pull the bar down (−3 % at 25 kg, −13 % to −26 % at 60 kg). 60 kg
  bounds are uninformative because of the clamp. `currentForce` reports only
  hard-limit rows (non-zero only on the 62 limit-engaged ticks at 300 kg).

Mass ratios bar/thorax (thorax 21.6 kg): 1.16 · 2.78 · 6.48 · 7.87 · 13.89 at
25 · 60 · 140 · 170 · 300 kg (bar/athlete 0.25 · 0.60 · 1.40 · 1.70 · 3.00).

## 5. Support geometry and wrench

- AP capture exits (140/170/300): hull, 2-D AABB and AP proxy agree within
  1.1 mm from spawn through the exit tick; the exit is rearward.
- 60 kg (V1): the exit is **mediolateral** — hull and 2-D AABB agree within
  0.8 mm, but the production AP-only capture proxy disagrees by up to 8.4 cm
  and lags by **38 ticks** (still +9.2 cm at the hull exit).
- At every capture exit the measured COP is **8.4–13.3 cm inside the hull**,
  with the posture guard at 0: available support authority was unused.
- Reported contact impulses have tangential/normal = 0 exactly: PhysX reports
  normal impulses only, so per-contact friction is not observable.
- Centroidal reconstruction from logged body states validates (vertical force
  ≤ 0.3 %, COP ≤ 7 mm) at 25/140/170/300 kg and shows zero violations of the
  necessary conditions (normal > 0, total friction ≤ μN, COP in hull) before
  failure. V1 60 kg fails validation (COP error 8.7 cm, clamp jitter) →
  `NOT_OBSERVABLE` there.

"Support infeasibility" is **not physically established**; the trim label was
a capture-proxy violation with unused COP authority.

## 6. Interventions (one property; plant, balance, capacity, feed-forward, solver fixed)

| Intervention | 25 kg | 60 kg | 300 kg |
|---|---|---|---|
| I1 bar/non-thorax limb filter | stands | **no capture/support failure; upright plateau, 11.4° abdomen sag (posture contract only)** | unchanged (capture 63, support 139) |
| I2 linear limit 0.05 → 0.10 m | stands | unchanged collapse | limit onset 18 → 115; collapse unchanged (capture 71, support 131) |

I1 establishes bar/limb contact as the cause of the 60 kg capture/support
failure. I2 rejects hard-limit engagement as the cause at 300 kg.

## 7. Saddle V2

See `Artifacts/Research/GAM-13-saddle-v2-contact-topology.md`. The only
production change is enumerating the bar Rigidbody's colliders for the
documented limb filter (`GAM13_SADDLE_BAR_LIMB_FILTER_V2`). V2 traces are
bit-identical to I1 at 25/60/300 kg.

V2 plant (production profile):

| Load | Stage-A | Order |
|---:|---|---|
| 25 | PASS (posture 4.03°) | none |
| 60 | FAIL on posture contract only (12.17°); upright, capture ≥ 9.2 cm, 8 contacts | posture-joint@47 |
| 140 | FAIL | guard@14 > posture@22 > capture@76 |
| 170 | FAIL | guard@14 > trunk gross@45 > capture@73 > support@192 |
| 300 | FAIL | guard@14 > trunk gross@34 > capture@63 > support@139 |

V2 capture exits are rearward with the COP 12.8–13.2 cm inside the hull; the
wrench reconstruction validates at all five loads with no violations.

### Static trim rerun from scratch on V2 (±12° bound preserved)

`causal-audit/v2-trim/` (plant label
`GAM13_PRODUCTION_PLANT_V2_GAM13_SADDLE_BAR_LIMB_FILTER_V2`); the V1 files
in `Artifacts/Measurements/GAM-13/trim-*.csv` are unchanged.

| Load | V1 long hold | V2 long hold | V2 biases (ankle, knee, hip, abdomen, thorax °) |
|---:|---|---|---|
| 25 | feasible | feasible | −2.9, 2.0, −0.6, 3.5, 3.5 |
| 60 | infeasible (support loss 183, capture 500 samples) | **feasible** (0, 0; capture ≥ 8.2 cm; trunk 0.55 rad; pelvis 0.964 m) | −1.1, 0.0, 0.5, 4.5, 4.1 |
| 140 | infeasible | infeasible (capture 394) | 0.0, −6.9, 9.0, 11.8, 10.9 |
| 170 | infeasible | infeasible (capture 432) | 2.1, −9.1, **12, 12, 12** |
| 300 | infeasible | infeasible (support 158, capture 500) | **12, −12, 12**, 9.1, **12** |

The 60 kg V2 trim hold passes the trim contract but its canonical posture
error is 0.28 rad (16°), so the Stage-A 10° posture contract would still fail.

The fixture labels (`SADDLE_CONTACT_INFEASIBLE` when max separation ≥ 0.05 m
at any time, checked before support; otherwise `SUPPORT_WRENCH_INFEASIBLE`)
are precedence rules over whole-hold extrema, not onset-ordered causes; the
causal audit supersedes them.

V2 changed the predicted link of the causal chain in the predicted direction
at 60 kg and nowhere else: it fixes 60 kg capture/support, and 140–300 kg
still fail posture-first. The saddle was not tuned further.

## 8. Qualification

`Artifacts/Measurements/GAM-13/causal-audit/qualification-gates.md`:
full EditMode 222/222; default PlayMode 182/182 before and after the fix
(8 graphics fixtures run on the GPU); V2 bit-identical to I1; 25 kg Stage-A
PASS; GAM-12 25 kg lifecycle unchanged relative to the squat command, with
identical P2/P3/terminal outcomes (start qualification 71 ticks earlier);
MasterSpec PASS.

## 9. Remaining boundary

Loaded failure above 60 kg is posture-first on the fixed athlete plant: the
spine folds under the bar, the posture guard withdraws sagittal ankle
authority, and the capture point leaves the rear of a support polygon whose
COP authority is unused. No posture-side intervention was executed, so this
is a `SUPPORTED_HYPOTHESIS`, and it is the architectural boundary where this
audit stops.
