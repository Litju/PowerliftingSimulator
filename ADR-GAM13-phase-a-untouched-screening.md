# ADR-GAM13 — Phase A untouched load screening and causal classification

Status: `PHASE_A_COMPLETE / PHASE_B_UNRESOLVED / PRODUCTION_NOT_QUALIFIED`

Issue: Linear `GAM-13` — M3.4 squat load response, sticking behavior, and physical failure

Date: 2026-09-25

Branch: `work/gam-13-squat-load-calibration`

Production baseline: `2aff0202d7574a17414bd4524b8e2ff8f3359884`

Screening run: [`20260925-phase-a-untouched-r7`](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/run-manifest.json)

## Decision

Do not calibrate production physics from this ladder. The screen establishes a valid 25 kg completion, a 60 kg physical posture/bar-loss failure, and a loaded-start collapse at 140/170/300 kg before a squat command or attempt record. It does not isolate one coherent production parameter family as the cause. The unresolved hypotheses and next discriminating boundary are recorded below. GAM-13 remains open; GAM-14 is not authorized.

No production physics, control, athlete, bar, scene, rule, lifecycle, depth-landmark, or project time-step value was changed. The GAM-13 test harness was extended only to capture and export the same post-physics schema per process, including an explicit no-attempt path.

## Frozen protocol and evidence

- Unity `6000.3.22f1`, editor `D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe`.
- Exact checked-in production baseline on one worktree, one Unity process per load, loads `25, 60, 140, 170, 300 kg`, in that order.
- One attempt per load, except the captured no-attempt start-window cases. The no-attempt bound was 2200 ticks at the frozen 100 Hz step.
- The only varying input was bar load. The existing harness applied its same setup and drive-intent schedule without controller/configuration overrides.
- The runner verified the production source, scene, time-step, physics settings, and project-settings hashes stayed constant across the five process runs. It also verified five unique Unity PIDs and one common 952-column schema.
- Modeled demand is labeled `MODELED_DEMAND_NOT_MEASURED_ACTUATOR_SATURATION`. Solver torque fields are labeled `ENGINE_SOLVER_DIAGNOSTIC_NOT_DIRECT_DRIVE_TORQUE`.
- The shared GAM-49 surface-landmark provider remains the sole rule-depth proxy. Joint-center values are diagnostic only. The preserved game threshold is `-0.005 m`; positive margin is `-0.005 - worstDepth`.

The pre-screen editor-authored Standalone scripting define was held constant for all five processes and restored after the run. It has no code consumer and is not a GAM-13 physics calibration. The run manifest contains the editor hash, process IDs, baseline, and source fingerprints.

## Cross-load observations

| Load | Lifecycle, P2, and P3 | Surface proxy: minimum L/R/worst; margin; first strict/game tick | Physical bar events and ascent | Descriptive measured behavior |
|---:|---|---|---|---|
| 25 kg | `PHYSICAL_LOCKOUT`; P2 `GOOD_LIFT`, no violations; P3 `NO_PHYSICAL_FAILURE` | `−0.120673 / −0.123380 / −0.120673 m`; `+0.115673 m`; ticks `331 / 334` | Descent `123`; bottom `422`; ascent established `443`; lockout `787`. Ascent `3.65 s`, `0.5599 m`, mean speed `0.1534 m/s`, peak `0.3268 m/s`. No resolvable `v_max1 → v_min → v_max2` or sticking interval. | Clean legal lockout with persistent bilateral support. The signal does not establish a sticking phase; measured mean ascent speed is modest for the light probe. |
| 60 kg | `TIMEOUT`; P2 `INCOMPLETE_ATTEMPT/UNDETERMINED`, no rule violations; P3 `PHYSICAL_FAILURE`, primary `POSTURE_OR_BAR_LOSS`, secondary `BALANCE_LOSS` and `TRUNK_HARD_LIMIT`. Failure onset/latch `437/438`. | `−0.417381 / −0.417368 / −0.417341 m`; `+0.412341 m`; ticks `357 / 358` | Descent `152`; offline bar-minimum tick `583`; P3 ultimately sees a physical/legal bottom, but its failure onset/latch precedes that bar minimum. No ascent or physical lockout. Peak analyzed upward speed `0.0294 m/s`; no sticking events were identifiable. | The trace first latches posture/bar-loss, then continues into a deeper physical bottom without establishing an ascent. Contact is lost later at tick `544`. This is not a moderate clean success. |
| 140 kg | No attempt record after 2200 ticks in `START_WINDOW/SETUP`; no squat command. P2 and P3 were not evaluated. | `+0.010864 / +0.012210 / +0.012210 m`; `−0.017210 m`; no strict or game-qualified tick. | No attempt-owned descent, bottom, ascent, lockout, or failure event. The raw bar and support traces show motion during setup, outside lift phases. | Loaded setup never advanced to a squat attempt. Bilateral contact callbacks remained present, while the rear support margin and slip proxy deteriorated. |
| 170 kg | No attempt record after 2200 ticks in `START_WINDOW/SETUP`; no squat command. P2 and P3 were not evaluated. | `+0.002088 / +0.007552 / +0.007552 m`; `−0.012552 m`; no strict or game-qualified tick. | No attempt-owned lift events; raw setup trace only. Bilateral contact was lost at tick `165`. | Loaded setup did not advance. Contact and support degraded; the trace does not contain a near-max squat or a sticking region. |
| 300 kg | No attempt record after 2200 ticks in `START_WINDOW/SETUP`; no squat command. P2 and P3 were not evaluated. | `−0.303789 / −0.304056 / −0.303789 m`; `+0.298789 m`; strict/game predicate first true at `150/151` during setup. | No attempt-owned descent, bottom, ascent, lockout, or failure event; raw setup only. Bilateral contact was lost at tick `132`. | The surface-depth predicate crossed its game threshold during an unattempted setup collapse. This is not a legal squat bottom, a supra-max stall, or a P3 physical failure classification. |

`v_max1`, `v_min`, and `v_max2` are `NA` at every load. At 25 kg the analyzer returned `NoStickingRegion`; 60 kg did not lock out; 140/170/300 kg never produced an attempt record. No sticking phase is inferred from those traces.

## Physical realization and constraint/contact health

The values below are measured from the same frozen traces. `Reference / applied target / actual` are worst-side surface-proxy depths at the actual minimum-depth sample. The setup-only rows are not squat target-tracking evaluations.

| Load | Reference / applied / actual at minimum (m); applied→actual delta | Persistent bilateral support; minimum front/rear margin (m); maximum accumulated left/right slip (m) | Maximum hip-anchor L/R (m); knee-anchor L/R (m); inferred pelvis-origin disagreement (m) |
|---:|---|---|---|---|
| 25 kg | `−0.064418 / −0.064418 / −0.120673`; `−0.056255` | Yes, fraction `1.000`; `+0.1802 / +0.1047`; `0.000642 / 0.000676` | `0.000006 / 0.000389`; `0.000133 / 0.000010`; `0.000394` |
| 60 kg | `+0.087360 / +0.087360 / −0.417341`; `−0.504700` | No, fraction `0.909`; `+0.1987 / −1.1422`; `0.296531 / 0.618406` | `0.001183 / 0.002277`; `0.001474 / 0.000212`; `0.001325` |
| 140 kg | `+0.328800 / +0.321684 / +0.012210`; `−0.309474` | Contact callbacks persisted, fraction `1.000`; `+0.2156 / −1.2063`; `1.071901 / 1.055365` | `0.002582 / 0.002593`; `0.001168 / 0.000303`; `0.001851` |
| 170 kg | `+0.328800 / +0.321657 / +0.007552`; `−0.314105` | No, fraction `0.979`; `+0.2194 / −1.2255`; `0.626818 / 0.633507` | `0.001832 / 0.002213`; `0.001976 / 0.000383`; `0.002201` |
| 300 kg | `+0.328800 / +0.328800 / −0.303789`; `−0.632589` | No, fraction `0.941`; `+0.2279 / −1.2590`; `0.849471 / 0.886531` | `0.002388 / 0.004941`; `0.004294 / 0.000521`; `0.004185` |

The recorded worst normalized modeled demands across each complete trace were `0.303, 0.543, 1.850, 1.847, 1.841` for 25/60/140/170/300 kg. These are game-model diagnostics, not measured actuator saturation. Maximum joint-limit proximity was `0.977` at 25 kg and `1.0` at the other probes. The raw per-joint requested/applied/actual orientations, modeled demand, limit proximity, and solver torque diagnostics are in every load trace.

## Causal classification

### ESTABLISHED

- The 25 kg production attempt is legal under the shared surface proxy, returns `GOOD_LIFT`, retains support, and reaches a physical lockout.
- At 60 kg P3 latches posture/bar-loss with balance-loss and trunk-hard-limit evidence at ticks `437/438`. The frozen trace later reaches an offline bar minimum at tick `583`, and P3 ultimately records bottom/legal-bottom evidence; no ascent is established and no lockout occurs.
- At 140/170/300 kg the runtime stays in `SETUP`/`START_WINDOW` for 2200 ticks. There is no squat command or attempt record, so P2/P3 are explicitly not evaluated.
- At 140/170 kg the worst-side surface proxy remains above zero throughout the captured setup trace. At 300 kg it becomes game-qualified during setup, after support loss; that sample is not attempt-owned legal-bottom evidence.
- Raw modeled demand does not establish actuator saturation or direct drive torque. The surface-depth and joint-center channels remain separate authorities.

### SUPPORTED

The first visible load-response defect after the clean 25 kg completion is at 60 kg: an attempt is issued and the surface proxy qualifies, but P3 latches posture/bar-loss before ascent establishment. At 140 kg and above the loaded athlete never advances from setup to an attempt. Foot slip, negative rear support margin, and actual-to-applied surface-depth divergence grow in these failing/setup traces. The evidence supports a loaded-standing/postural-support problem as the broad failure domain.

### REJECTED

- The 140/170/300 kg files do not show squat attempts, so they cannot be classified as heavy grinding, slow sticking, or supra-max lift failure.
- The 300 kg game-depth sample during setup is not evidence of a legal squat bottom or successful attempt.
- No `v_max1 → v_min → v_max2` sticking signal is identifiable.
- The ladder does not show a GAM-49 surface-depth authority regression. Joint-center diagnostics do not override the surface proxy.
- Modeled demand and solver constraint torque do not establish measured actuator saturation or direct drive torque.

### UNRESOLVED

The traces do not isolate whether the broad setup/posture defect is driven by loaded-standing equilibrium/preload, dynamic balance control, support and slip, trunk joint-limit tracking, bar/athlete coupling, or the start-window readiness path. The no-attempt records contain no P2/P3 judgment, and the available evidence does not distinguish these candidate causes enough to select a coherent parameter family.

**Phase C is not authorized.** No controller, joint, balance, preload, support, rule, depth landmark, or lifecycle parameter should be changed from this evidence. A future intervention needs a discriminating experiment that separates the listed domains, a falsifier, and a bounded observable before any production tuning.

## Configuration and claim ceiling

- Final production configuration for this phase: the checked-in baseline at `2aff0202d7574a17414bd4524b8e2ff8f3359884`, capacity model `GAM13_SQUAT_ATHLETE_CAPACITY_V1`, failure calibration `GAM12_P3A1_FAILURE_CALIBRATION_PROVISIONAL_V1`; no physics parameters changed.
- Load-response order: only 25 kg produced a clean completed attempt; 60 kg failed physically; 140/170/300 kg were unattempted setup failures. The target easy → moderate → grind → near-max → supra-max envelope is not qualified.
- Depth: legal game surface proxy at 25 and 60 kg attempt minima; 140/170 kg setup minima are shallow; 300 kg crosses the depth threshold during setup collapse without a squat attempt.
- Sticking: not resolved at any load.
- Physical failure: one evaluated P3 posture/bar-loss failure at 60 kg. No P3 failure classification exists for 140/170/300 kg because no attempt record exists.
- Constraint/contact: clean at 25 kg; 60 kg loses support after the failure onset; 140 kg retains contact callbacks but has large slip and negative rear support margin; 170/300 kg lose bilateral contact in setup. Joint-anchor and pelvis-origin disagreements remain finite and are recorded.
- Claim ceiling: same-machine Unity/PhysX game-simulation evidence using the repository’s engineering body and shared rule-proxy landmarks. It is not a biological actuator-force, direct skin-surface, referee-judgment, or cross-platform determinism claim.
- GAM-14: `NOT_AUTHORIZED`.

## Reproduction files

- Frozen screening plan: [20260925-screening-plan.md](Artifacts/Measurements/GAM-13/phase-a-untouched/20260925-screening-plan.md)
- Fresh-process manifest: [run-manifest.json](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/run-manifest.json)
- 25 kg: [trace](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/025kg/GAM13-phase-a-025kg.csv), [test result](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/025kg/test-results.xml)
- 60 kg: [trace](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/060kg/GAM13-phase-a-060kg.csv), [test result](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/060kg/test-results.xml)
- 140 kg: [trace](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/140kg/GAM13-phase-a-140kg.csv), [test result](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/140kg/test-results.xml)
- 170 kg: [trace](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/170kg/GAM13-phase-a-170kg.csv), [test result](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/170kg/test-results.xml)
- 300 kg: [trace](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/300kg/GAM13-phase-a-300kg.csv), [test result](Artifacts/Measurements/GAM-13/phase-a-untouched/run-20260925-phase-a-untouched-r7/300kg/test-results.xml)
