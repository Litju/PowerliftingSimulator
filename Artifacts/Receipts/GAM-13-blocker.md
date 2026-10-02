# GAM-13 blocker receipt — heavy-load squat calibration

MISSION
GAM13_END_TO_END_SQUAT_LOAD_RESPONSE_STICKING_AND_PHYSICAL_FAILURE_CALIBRATION

STATUS
BLOCKED

START_MAIN
4cf5fc5fe9bd7e668e363da35cf4656202da233f (GAM-12 / PR #11 merge; checkpoint
`checkpoint/gam13-pre-calibration`)

BRANCH
work/gam-13-squat-load-calibration (no PR, no merge)

BLOCKER_CLASS
ARCHITECTURAL — HEAVY_LOAD_STANDING_POSTURE_OUTSIDE_QUALIFIED_CONTROL_DOMAIN.
No load-independent finite capacity (and no bounded equilibrium bias) can
produce a successful 60–170 kg squat, because the loaded athlete cannot hold
the setup posture under a ≥40–60 kg bar before any attempt begins. The binding
limit is the GAM-11 standing posture/balance control substrate, which is
qualified only for 0–25 kg; the drive capacity ceiling never binds.

## 1. What changed on the branch

Evidence and tooling only. **No production physics, control, capacity, rule,
failure, lifecycle, or schema value was changed.**

| File | Kind |
|---|---|
| `Assets/Scripts/Squat/SquatLoadResponseAnalyzer.cs` | OFFLINE_DIAGNOSTIC_ONLY analyzer over a frozen trace; no runtime callsite |
| `Assets/Tests/EditMode/GAM13SquatLoadResponseAnalyzerTests.cs` | 6 analyzer contract tests |
| `Assets/Tests/PlayMode/GAM13SquatLoadCalibrationHarness.cs` | reusable fresh-scene load-sweep harness |
| `Assets/Tests/PlayMode/GAM13SquatLoadCalibrationTests.cs` | `[Explicit]` baseline sweep |
| `Assets/Tests/PlayMode/GAM13SquatLoadDiagnosticTests.cs` | `[Explicit]` per-tick setup diagnostic with test-only, restored counterfactuals |
| `Artifacts/Research/GAM-13-load-response-rationale.md` | bounded literature check |
| `Artifacts/Measurements/GAM-13/*.csv` | baseline sweep, authority audit, counterfactuals, collapse excerpt |

## 2. Probe amendment

The canonical 20 kg probe is not representable: the repository barbell is a
20 kg bar plus two 2.5 kg collars and `BarbellLoadingSolver.Solve` rejects any
total below 25 kg. The light probe is therefore 25 kg (identical to the GAM-12
regression load).

## 3. Canonical harness

Fresh `LoadSceneMode.Single` of `SquatPhysicalPrototype` per attempt; the
GAM-12 `SquatAttemptOrchestrator` lifecycle; 100 Hz render-frame clock; no
intent until the open-loop reference leaves BOTTOM, then Drive held at 1.0.
Only `SetLoad(kg)` differs between probes. The Drive tick is Squat + 354 ticks
at every load (25: 121→475; 60: 334→688; 140: 247→601; 170: 281→635;
300: 276→630). Drive does not alter the autocycled reference or capacity.

## 4. Baseline sweep (unmodified main, 3 fresh runs per load)

All fields are bit-identical across the three runs of every load
(`baseline-load-sweep.csv`).

| Load | Terminal | P3 | Primary | Onset / Squat tick | Trace start posture | P2 |
|---:|---|---|---|---|---|---|
| 25 | PHYSICAL_LOCKOUT@855 | NO_PHYSICAL_FAILURE | NONE | — / 121 | standing | NO_LIFT (FAILED_START_POSITION, SUPPORT_VIOLATION) |
| 60 | TIMEOUT@1833 | PHYSICAL_FAILURE | POSTURE_OR_BAR_LOSS | 331 / 334 | trunk pitch 1.40–1.52 rad, depth −0.414 m, COM 0.42 m behind support | INCOMPLETE_ATTEMPT |
| 140 | TIMEOUT@1746 | PHYSICAL_FAILURE | POSTURE_OR_BAR_LOSS | 244 / 247 | trunk 1.19–1.57 rad, depth −0.429 m, COM 1.20 m behind | INCOMPLETE_ATTEMPT |
| 170 | TIMEOUT@1780 | PHYSICAL_FAILURE | POSTURE_OR_BAR_LOSS | 278 / 281 | trunk 1.18–1.49 rad, depth −0.429 m, COM 1.22 m behind | INCOMPLETE_ATTEMPT |
| 300 | TIMEOUT@1775 | PHYSICAL_FAILURE | POSTURE_OR_BAR_LOSS | 273 / 276 | trunk 1.18–1.51 rad, depth −0.429 m, COM 1.26 m behind | INCOMPLETE_ATTEMPT |

25 kg metrics: legal depth (worst side −0.0283 m); bottom 495; ascent 516;
concentric 3.60 s; displacement 0.556 m; mean concentric velocity 0.154 m/s;
peak 0.320 m/s; minimum mid-ascent −0.0072 m/s; max downward reversal after
ascent 0.0066 m; max modeled demand 0.259, mean 0.247, saturation fraction 0;
peak knee/hip tracking error 0.375/0.259 rad; `NO_RESOLVABLE_STICKING`
(4σ velocity resolution 0.0146 m/s). This reproduces the GAM-12 receipt
exactly (lockout 855, 742 samples).

At 60–300 kg the athlete has already collapsed onto the platform during setup;
the lifecycle then qualifies a "start" on the collapsed body (knee/hip/trunk
joint twists near neutral, bar still) and P3 correctly latches
POSTURE_OR_BAR_LOSS on the first trace samples. No descent, bottom, ascent,
sticking, or stall is observable at any load above 25 kg.

## 5. Load-dependent authority audit

Pre-GAM-13 `SquatPhysicalAdapter.PrepareCommands`:
`capacityScale = max(3.8, 3.8·(m_athlete + m_bar)/m_athlete)·(1 + 0.35·brace)`,
then family multipliers ankle 2.5, knee 1.8, hip 1.5, trunk 1.5, neck 1.5,
upper limb 1.0; `maximumForce = BaseCapacityNm·capacityScale·activation`.
Athlete mass 100 kg. Measured ceilings equal the formula exactly
(`baseline-load-authority.csv`):

| Load | Scale | Knee/Hip N·m | Ankle N·m | Trunk N·m | Error needed for knee spring to reach ceiling |
|---:|---:|---:|---:|---:|---:|
| 25 | 4.75 | 2565 | 2137.5 | 1852.5 | 3.21 rad |
| 60 | 6.08 | 3283.2 | 2736 | 2371.2 | 4.10 rad |
| 140 | 9.12 | 4924.8 | 4104 | 3556.8 | 6.16 rad |
| 170 | 10.26 | 5540.4 | 4617 | 4001.4 | 6.93 rad |
| 300 | 15.20 | 8208 | 6840 | 5928 | 10.26 rad |

Classification of every load-aware path:

| Component | Class |
|---|---|
| `PhysicalBarbell` mass/inertia, saddle coupling | EXTERNAL_PHYSICS |
| `SquatPhysicalAdapter` load-ratio `capacityScale` | ATHLETE_CAPACITY — load-proportional (presumptively wrong) |
| `SquatEquilibriumPreload.SpineBiasDegrees/RateDegreesPerPhase(…, EquilibriumLoadKg)` | EQUILIBRIUM_TARGET_BIAS — 0/25 kg table, returns 0 above 25 kg (H17 domain guard) |
| `SquatBalanceObserver` system COM including bar | EXTERNAL_PHYSICS observation feeding CONTROL_FEEDBACK |
| `SquatPredictiveBalanceController.RequestedAnkleTorqueNm` (system mass) | DIAGNOSTIC_ONLY |
| `SquatFailureRecord.LoadKilograms`, trace bar load | DIAGNOSTIC_ONLY (GAM-12 mutation tests prove non-selecting) |

The load-proportional capacity path is real, but it masks nothing: with the
GAM-7 springs (knee 800, hip 900, ankle 650, trunk 800 N·m/rad) the spring
term can only reach the ceiling at errors of 2.3–10 rad — beyond the joints'
ranges — so the ceiling does not bind at any tested load.

## 6. Mechanism of the setup collapse

Per-tick setup diagnostic (`setup-collapse-excerpt.csv`, every 10th tick):

- 25 kg: qualified spine bias 3.93° abdomen / 3.67° thorax; spine joints hold
  within ±0.03 rad; spine/hip/ankle modeled demand ≤ 0.07.
- 40 kg (spine bias 0 by the >25 kg domain guard): abdomen −0.235 rad, thorax
  −0.18 rad quasi-static sag; trunk pitch 0.58 rad; athlete falls near tick 300.
- 60 kg: abdomen −0.367 rad, thorax −0.285 rad by tick 71; the predictive
  balance posture guard scale is 0 over ticks 31–91 (spine error beyond its 8°
  full-withdrawal threshold removes ankle authority); trunk pitch 1.28 rad by
  tick 101; support lost near tick 161. Peak modeled demand of spine 0.19,
  hip 0.10, ankle 0.25 — **no drive ever reaches its ceiling**.

Quasi-static check at 40 kg (ENGINEERING_DERIVED estimate): 188 N·m spine
drive torque (800 N·m/rad × 0.235 rad) against 93.6 kg supported above the
abdomen joint (53.6 kg segment mass fractions + bar) implies a 0.20 m COM
moment arm, consistent with the 0.58 rad trunk pitch. With an assumed 0.35 m
COM height above that joint, the gravitational negative stiffness m·g·h of the
loaded trunk is ≈ 320 N·m/rad at 40 kg, 390 at 60 kg and 770 at 170 kg, against
≈ 400 N·m/rad for the two 800 N·m/rad spine springs in series — the order of
magnitude at which the trunk stops holding, near 60 kg. This estimate explains
the onset; the counterfactuals below are the proof.

## 7. Discriminating counterfactuals

Test-only mutations, restored in `finally`, fresh scene per run
(`heavy-load-counterfactuals.csv`). None is a production change.

| Counterfactual | 60 kg | 170 kg | Other |
|---|---|---|---|
| capacity ×4 (all families) | collapse, onset 331 — **identical to production** | collapse, onset 278 — **identical** | — |
| capacity ×0.25 | never reaches a start | collapse, onset 272 | — |
| trunk spring ×2 | collapse, 260 | never reaches a start | — |
| load-bearing springs ×2 (damping ratio kept) | collapse, 281 | collapse, 204 | — |
| load-bearing springs ×3 | collapse, 264 | collapse, 286 | ankle offset saturates ±15°: balance gain is plant-specific |
| impedance ×2 (springs ×2, ankle target-to-COP gain ×2, spine bias ÷2) | collapse, 272 | collapse, 204 | 25 kg locks out but loses legal depth; 300 kg collapse, 454 |
| impedance ×3 | collapse, 306 | collapse, 354 | 25 kg locks out, depth +0.0125 m (not legal); 300 kg collapse, 433 |
| spine bias linearly extrapolated from the 0/25 kg table, clamped to its 12° hard bound | never reaches a start | never reaches a start | 40 kg folds backward (trunk −1.46 rad); 100 kg collapse; 140 kg no start |
| posture guard disabled | collapse, 278 | collapse, 324 | — |
| guard off + extrapolated bias | collapse, 270 | never reaches a start | — |
| guard off + impedance ×2 | collapse, 250 | collapse, 279 | — |

Capacity ×4 reproduces production tick-for-tick, and ×0.25 still collapses:
over the whole plausible bracket of a load-independent global capacity scale
[0.25, 4]×, the 60 kg outcome is a setup collapse. A deterministic capacity
search cannot find a region where 170 kg is completable, because no value in
the bracket lets 60 kg stand.

## 8. Architecture-change record (constitution protocol)

OBSERVATION
Under the canonical GAM-12 lifecycle, the physical athlete collapses during
setup under a 40–300 kg bar, before any squat can be characterized. Drive
ceilings never bind.

EXPECTED
PSMS-06 / PSMS-SQ-14: finite capacity makes "light, heavy, grinding, and failed
attempts emerge"; moderate load succeeds, calibrated heavy grinds, supra-max
stalls. GAM-13 expected the load-ratio capacity path to be the masking defect.

ACTUAL
The GAM-11 standing posture/balance substrate — a finite-impedance spine
(800 N·m/rad per joint), a 0/25 kg spine equilibrium table guarded to zero
above 25 kg, a ±15°/±7°/±6° bounded balance offset set with an 8° posture guard,
and an ankle target-to-COP gain identified on the unloaded plant — cannot hold
posture under ≥40–60 kg. Drive capacity is not the binding constraint at any
load.

MINIMAL_REPRODUCTION
`GAM13SquatLoadCalibrationTests.GAM13_BASELINE_LOAD_SWEEP` (Explicit) or
`GAM13SquatLoadDiagnosticTests.GAM13_SETUP_AND_ATTEMPT_TICK_DIAGNOSTIC` with
`GAM13_DIAG_LOADS=25,40,60` on unmodified main.

MEASURED_EVIDENCE
Sections 4–7 and the four CSVs under `Artifacts/Measurements/GAM-13/`.

AFFECTED_SPEC_CONTRACT
PSMS-05 (capacity calibrated separately from tracking stiffness; demand proxy
against `maximumForce`), PSMS-06 (finite capacity creates heavy/failed
attempts), PSMS-SQ-11 (bounded AP correction; one ≤10% phase capacity
modifier), PSMS-SQ-14 (settle before Squat), PSMS-SQ-18 SQ-P03…P06, and the
GAM-11 closeout domain `BODYWEIGHT / 0 KG MODE + 25 KG MODERATE LOAD`.

PROPOSED_AMENDMENT
See REQUIRED_ARCHITECTURAL_DECISION.

## 9. Required architectural decision

Owner decision needed before GAM-13 calibration can resume:

1. **Recommended — heavy-load standing/posture re-identification unit
   (M3 control, before GAM-13):** extend the qualified control domain from
   0–25 kg to 0–300 kg by (a) replacing the 0/25 kg spine equilibrium table
   with a load-general equilibrium feed-forward derived from observed supported
   mass and segment geometry (the preload's 12° guard explicitly says to reopen
   substrate calibration rather than widen it), and (b) re-identifying
   load-bearing impedance (spring/damper, possibly activation- or
   capacity-proportional) together with the ankle target-to-COP gain and the
   posture-guard thresholds on that plant. Both need an ADR and amendments to
   PSMS-05/06 (whether load-bearing stiffness may scale with capacity/
   activation) and PSMS-SQ-11 (the correction bounds).
2. Accept spring-limited finite impedance as the capability model and redefine
   "capacity" as stiffness for load-bearing families (spec amendment), then
   recalibrate against the heavy probes.
3. Re-scope GAM-13 to the currently controllable domain. Not viable for the
   mission's envelope: the plant does not reliably stand at 40 kg.

Once standing is solved, the prepared GAM-13 plan applies unchanged: replace
the load-ratio capacity with one versioned load-independent profile
(`capacityScale = GlobalStrengthScale·familyMultiplier·(1 + 0.35·brace)`),
bracket-search the global scale, then calibrate the P3
`REQUIRES_GAM13_CALIBRATION` fields from frozen successful/failed traces.

## 10. Additional finding (not fixed)

The GAM-12 start qualification accepted a supine, collapsed athlete as a valid
start candidate (bar still, knee/hip/trunk twists near neutral) at 60–300 kg.
P2/P3 remained truthful (INCOMPLETE_ATTEMPT / POSTURE_OR_BAR_LOSS), but the
start candidate predicate has no bar-height or standing-posture gate. This is
related to the deferred GAM-12 start-qualification finding and is recorded for
the lifecycle owner, not changed here.

## 11. Gates executed on this branch

| Gate | Result |
|---|---|
| Full EditMode | PASS 212/212 (206 prior + 6 GAM-13 analyzer tests) |
| GAM-12 25 kg lifecycle PlayMode | PASS 1/1; 3 fresh runs identical to the GAM-12 receipt (lockout 855, 742 samples) |
| GAM-13 baseline sweep (Explicit) | executed, 15/15 attempts, bit-identical per load |
| MasterSpec | PASS (68 files, hashes and dependencies) |
| Full PlayMode / graphics PlayMode / performance | NOT RUN — no production behaviour changed and the calibration did not reach qualification |

Evidence is same-machine Unity 6000.3.22f1 headless; no cross-platform PhysX
determinism is claimed. During the runs five external `codebase-memory-mcp`
processes (parent: a user `codex` session) held about a third of total CPU;
they were not started by this work and were not terminated. No performance
claim is made.

## 12. Invariants

P1_CHANGED NO · P2_CHANGED NO · P3_SEMANTICS_CHANGED NO · P4_CHANGED NO ·
PRODUCTION_PHYSICS_CHANGED NO · CAPACITY_CHANGED NO · LOAD_THRESHOLD_SCRIPT NO

LINEAR
GAM-13 remains In Progress; GAM-14 remains Backlog. The Linear connector is
not authorized in this session, so no comment was posted.
