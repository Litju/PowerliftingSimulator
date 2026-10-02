# GAM-13 posture / equilibrium isolation — qualification gates

Unity 6000.3.22f1 (`1c726e1fb402`), main PC, batch mode. Raw XML/logs are
machine-local under `D:\Dev\UnityLogs\GAM13\posture\`. Production code is
unchanged in this mission; only the explicit GAM-13 causal fixture and the
offline analysis changed.

## Baseline reproduction (before any change)

| Gate | Result |
|---|---|
| `GAM13_CAUSAL_AUDIT_BASELINE_AND_TOPOLOGY` at HEAD `9d331e9` | PASS; `causal-summary-baseline.csv` and all five trace and body files bit-identical to `causal-audit/v2-baseline/` |
| A0 (new instrumentation, `none`) | bodies bit-identical at every tick and load; trace rows identical except the tick-0 pre-physics balance diagnostics of the first scene in the process |
| Final-fixture reruns of A0/G1/E1 | bodies bit-identical to the first runs at all 15 arm/load pairs |

## Arms

| Arm | Result |
|---|---|
| G1 guard off, 1x | 25 PASS; 60/140/170/300 FAIL; guard class NO_CHANGE/NO_CHANGE/DELAYS/DELAYS/NO_CHANGE |
| E1 V2 trim, 1x | 25 PASS; 60/140/170/300 FAIL |
| K3 fixed 3x, three fresh processes | 25/60/140/170 qualified; 300 FAIL; the three repeats are bit-identical (`per-tick-md5.csv`) |
| K3G guard off, 3x | 25/60/140/170 qualified; 300 FAIL |
| T3 static trim on V2 at 3x | converged 25–230 kg; 260/300 kg not converged; holds 25/60/140/170 feasible, 300 infeasible |
| C3 T3 biases, 3x | 25/60/140 qualified; 170 FAIL (posture 11.30 deg); 300 FAIL |

## Official Stage-A fixture (`GAM13_STAGE_A_FIXED_PLANT_STANDING_QUALIFICATION`)

| Plant | 25 | 60 | 140 | 170 | 300 | Evidence |
|---|---|---|---|---|---|---|
| production (1x) | PASS | FAIL | FAIL | FAIL | FAIL | `stage-a/stage-a-standing-production.csv` |
| `GAM13_STAGE_A_IMPEDANCE=3` | PASS | PASS | PASS | PASS | FAIL | `stage-a/stage-a-standing-impedance-3x.csv` |

Both match the causal fixture's Stage-A summaries digit for digit. The fixture
writes the tracked `Measurements/GAM-13/stage-a-standing-final.csv`; that file
was restored, not changed.

## 25 kg lifecycle

| Gate | Result |
|---|---|
| `GAM12SquatAttemptLifecycleIntegrationTests` (default suite, x3) | PASS; START_WINDOW 49–51, SQUAT 52, DESCENT 80, BOTTOM 427, ASCENT 447, LOCKOUT 791, TRACE_COUNT 747; P2 EVALUABLE/NO_LIFT (FAILED_START_POSITION, SUPPORT_VIOLATION); P3 EVALUABLE/NO_PHYSICAL_FAILURE; PHYSICAL_LOCKOUT, identical to the accepted table |
| `GAM13_CANDIDATE_PLANT_25KG_LIFECYCLE`, production | identical to the row above, three runs |
| `GAM13_CANDIDATE_PLANT_25KG_LIFECYCLE`, 3x | three identical runs; lockout and P3 kept; P2 adds INSUFFICIENT_DEPTH (bottom 410, lockout 771, trace 723) |

## Suites

| Gate | Result |
|---|---|
| Full EditMode | **222/222** (includes `GAM13StaticTrimSolverTests`, `GAM13SupportGeometryTests`) |
| Default PlayMode, `-nographics` | 174 pass, 8 fail, 18 explicit skipped (17 before + the new diagnostic); the 8 failures are exactly the graphics-device fixtures |
| Those 8 fixtures with a GPU | **8/8** |
| Default PlayMode total | **182/182** |
| MasterSpec | PASS (68 files, hashes, dependencies) |

The default suite regenerated 295 tracked evidence/settings files and an
untracked `ProjectSettings/SceneTemplateSettings.json`; all were restored or
removed, not committed.
