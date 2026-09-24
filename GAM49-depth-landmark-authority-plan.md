# GAM-49 — Depth landmark authority plan

## Work unit

```text
MISSION=GAM49_DEPTH_LANDMARK_AUTHORITY
BASE_SHA=366163ff583c051cc17f3ea038fda85c3d398b34
SPEC_ARTIFACTS=Linear GAM-49; MASTER_SPEC_V1 SQUAT/11 and SQUAT/15; GAM-10 calibration; GAM-48 Gate 4b receipts
SCOPE=Freeze the squat-depth landmark contract, then implement and qualify it through the required gates.
NON_GOALS=Physics/control/reference-motion/offset tuning; load calibration beyond the canonical 25 kg decision; GAM-14; closing or merging GAM-13.
INVARIANTS=PhysX remains the only forward-motion authority; rule judgment consumes read-only post-step observations; C0/HOLD and attempt lifecycle authority from GAM-48 remain intact.
STATUS=GATE_0_PLAN_FROZEN
```

## Gate 0 — Frozen authority

### Authority

- Linear GAM-49 controls acceptance and gate order. GAM-48 is complete; its Gate 4b runtime decomposition and owner review are the inherited baseline.
- `docs/master-spec/POWERLIFTING_SIMULATOR_MASTER_SPEC_V1/` is immutable design authority. The squat rules and biomechanics chapters define bilateral surface-landmark depth, world-height differences, and a 5 mm game calibration.
- Repository source, tests, and fresh Unity receipts establish implementation truth.
- The official IPF Technical Rule Book, Version 3 effective 01 March 2026, §4.1 item 3 (PDF page 20) states: “the top surface of the legs at the hip joint is lower than the top of the knees.” It does not prescribe a 5 mm distance. Source: <https://www.powerlifting.sport/fileadmin/ipf/data/rules/technical-rules/english/2026_IPF_Technical_Rulebook__effective_01_March_2026__v3.pdf>.

### Frozen landmark contract

- **Rule landmarks:** left/right hip-crease surface proxies and left/right knee-top surface proxies. Each point is expressed in Unity world frame `W`; `+Y` is up, units are meters. The full XYZ points are retained for evidence and visualization; depth uses only world Y.
- **Frame evaluation:** each hip-crease point is the current pelvis-frame origin plus its existing GAM-10 pelvis-frame offset. Each knee-top point is the current side-specific shank-frame origin plus its existing GAM-10 shank-frame offset. Reference and physical poses use the same read-only provider and those same four offsets.
- **Signed depth:** `dL = hipCreaseLeft.y - kneeTopLeft.y`; `dR = hipCreaseRight.y - kneeTopRight.y`. Negative means the hip-crease proxy is lower. `worstSideDepth = max(dL, dR)`; bilateral acceptance never averages sides.
- **`IPF_RULE_PREDICATE`:** both corresponding hip-joint surface proxies are strictly below their knee-top surface proxies (`dL < 0 && dR < 0`). This encodes the rule wording and is not the game's stricter threshold.
- **`GAME_JUDGMENT_MARGIN_M = 0.005`:** existing deterministic game judgment remains bilateral `dL <= -0.005 && dR <= -0.005` (equivalently `worstSideDepth <= -0.005`). This is a game judgment margin, not an IPF-prescribed distance. Existing phase, persistence, and attempt sequencing stay unchanged.
- **Joint centers:** may be emitted only as separately named `JointCenterDepthDiagnostic` evidence. They are never hip-crease/knee-top landmarks and never feed rule truth.
- **Claim ceiling:** `RULE_DERIVED_GAME_PROXY`. The calibrated points are deterministic bone/frame-attached proxies, not directly measured skin surfaces, anatomical joint centers, or a claim that the game reproduces referee visual judgment.

### Existing GAM-10 calibration provenance (frozen, no retuning)

Authority is `SquatReferenceRigCalibration.CalibrationId = GAM10_CANONICAL_QUATERNIUS_JOINT_FRAMES_V2` in `Assets/Scripts/Squat/Unity/SquatReferenceKinematics.cs`. `Build` derives and stores the local offsets from the existing named game-frame proxy constants:

- Hip crease: inset `0.055 m`, down `0.055 m`, forward `0.060 m`; left/right signs follow the measured game-right axis. The resulting vectors are stored as `LeftHipCreaseOffsetInPelvisFrame` and `RightHipCreaseOffsetInPelvisFrame`.
- Knee top: up `0.045 m`, forward `0.035 m`, stored as `LeftKneeTopOffsetInShankFrame` and `RightKneeTopOffsetInShankFrame` after conversion into the calibrated side-specific shank frame.
- These values are the accepted GAM-10 game proxy calibration. GAM-49 consumes the existing stored vectors; it adds no offsets and changes none of their values.

### Gate 0 producer/consumer inventory

| Path | Current responsibility | GAM-49 disposition |
|---|---|---|
| `SquatReferenceRigCalibration.Build` in `Assets/Scripts/Squat/Unity/SquatReferenceKinematics.cs` | Owns GAM-10 proxy constants and creates pelvis/shank-frame offset vectors. | Preserve as the only calibration authority. |
| `SquatReferencePreview.UpdateOverlay` in `Assets/Scripts/Squat/Unity/SquatReferencePreview.cs` | Applies GAM-10 offsets to the sampled reference solution; evaluates reference depth and places overlay markers/lines. | Route through shared provider; retain full points and actual-humanoid side-view markers. |
| `SquatPhysicalAdapter.EvaluateRuleDepth` / `TryGetRawDepthLandmarks` in `Assets/Scripts/Squat/Unity/SquatPhysicalAdapter.cs` | Currently labels thigh/shank ConfigurableJoint anchors as hip crease/knee top and evaluates runtime depth. | Replace rule-landmark production with shared provider on current physical pelvis/shank frames. Keep anchors only in the diagnostic channel. |
| `SquatObservationCollector.CaptureDepth` in `Assets/Scripts/Squat/Unity/SquatObservationCollector.cs` | Copies runtime landmark Y values and the depth margin into post-step snapshots. | Consume the adapter's authoritative provider result without recomputing landmarks. |
| `SquatDepthLandmarks` / `SquatObservationSnapshot` in `Assets/Scripts/Squat/SquatObservationSnapshot.cs` | Stores bilateral raw landmark heights, per-side depth, and margin in immutable trace evidence. | Preserve the contract and expose provider-derived values; keep diagnostic centers separately named. |
| `SquatRuleProcessor.FindLegalDepthIndex` in `Assets/Scripts/Squat/SquatRuleProcessor.cs` | P2 evaluates bilateral legal bottom and emits `INSUFFICIENT_DEPTH`. | Use only the authoritative surface-proxy observation and named game margin. |
| `SquatFailureDetector.IsLegalBottom` / `LegalBottomSeen` in `Assets/Scripts/Squat/SquatFailureDetector.cs` | P3 reports legal-bottom evidence separately from physical completion prerequisites. | Use the same authoritative surface-proxy observation; legal depth remains independent of P3 physical completion. |
| `SquatDepthGeometry` in `Assets/Scripts/Squat/SquatDepthGeometry.cs` | Computes signed bilateral depth/worst-side judgment with the existing 5 mm default. | Own or delegate the shared deterministic point-provider operation and expose explicit rule-predicate vs game-margin semantics. |
| `SquatTelemetrySchema` and `SquatLoadResponseAnalyzer` in `Assets/Scripts/Squat/` | Name/report landmark world heights, depth margin, and legality analysis. | Preserve channel semantics, identify surface proxy and game-margin provenance, and report centers only as diagnostics. |
| Reference/trace/rule tests: `GAM10SquatReferencePreviewQualificationTests`, `GAM12SquatRuleProcessorTests`, `SquatObservationTraceContractTests`, `GAM12ObservationTraceIntegrationTests`, `GAM47DepthRegressionIsolationTests`, `GAM48DepthDecompositionTests`, `GAM48Gate4bRecorder` | Qualify reference geometry, observation, P2/P3 evidence, and GAM-48 decomposition; GAM-48 recorder currently has a separate inline GAM-10 offset calculation. | Reuse the provider in qualification and receipts; remove duplicate offset application and add parity/diagnostic-isolation coverage. |

### Inherited GAM-48 evidence and sequencing

- Base commit `366163f` is clean and equals `origin/work/gam-13-squat-load-calibration`; one existing worktree is present.
- GAM-48 Gate 4b fresh-process receipt: C0/HOLD matched exactly at the recorded 1e-5 m comparison tolerance; GAM-10 calibrated reference worst-side depth was `-0.07631308 m`; production joint-center metric was `+0.0225417614 m`; their difference was `0.0988549 m`; actual C0 worst-side joint-center depth was `+0.0431753546 m`.
- The prior `~20.908 mm` shallowing is on the joint-center/applied-target metric. It cannot decide physical legality under the frozen surface proxy. Recompute the target/actual decomposition under the unified provider.
- P3 physical completion is descent + physical bottom + ascent established + physical lockout. `P3_LEGAL_BOTTOM_SEEN` remains a separate evidence field and does not gate P3 completion.

## Execution gates and commit boundaries

Each completed gate/item is validated, staged by exact path, committed atomically, and pushed without force. Restore Unity churn not present in the pre-import candidate.

1. **Gate 0:** this plan, complete source/consumer inventory, and frozen semantics; no production edits.
2. **Gate 1:** one deterministic, allocation-safe, read-only provider shared by reference and physical runtime; use it for rule snapshots, P2, P3 legal-bottom, telemetry, and visualization. Joint-center depth is isolated. Add focused deterministic regressions.
3. **Gate 2:** prove reference parity against the existing GAM-10 method at standing, phases `0.25`, `0.55`, `0.80`, `1.00` bottom, and representative ascent. Predeclare tolerance before evaluation. Confirm bilateral finite/deterministic points, unchanged legal bottom under the game margin, and a side-view visual on the actual humanoid. If proxy placement is visually indefensible, stop with `LANDMARK_CALIBRATION_NOT_QUALIFIED` and do not retune offsets.
4. **Gate 3:** on the canonical 25 kg C0/HOLD bottom, record reference surface depth, applied-target surface-proxy FK depth, actual physical surface-proxy depth, target and actual joint-center diagnostics, and both realization deltas. Run PhysX comparisons in fresh Unity processes.
5. **Gate 4:** rerun C0/HOLD equivalence and the canonical 25 kg lifecycle in fresh Unity processes. Prove P2 and P3 legal-depth evidence share the same provider output, support authority remains valid, P3 physical completion remains independent, and no physics parameter changed. Decide only from surface-proxy result:
   - Legal: classify prior `INSUFFICIENT_DEPTH` as an observation-contract defect; skip low-load realization tuning and authorize return to GAM-13 ordered load calibration.
   - Illegal: report residual worst-side deficit from the `-0.005 m` game threshold; stop and authorize only a separate low-load realization-discrimination issue.

## Validation and deliverables

- Validate each gate before its commit; inspect exact staged paths and `git diff --cached --check` before commit.
- Required delivered artifacts: this plan; producer/consumer inventory; shared-provider code and deterministic regressions; reference parity receipt; actual-humanoid side-view evidence; fresh-process physical target/actual surface-vs-center decomposition; fresh 25 kg trace; corrected P2/P3 receipt; `ADR-GAM49-depth-landmark-authority.md`.
- Final ADR records the landmark contract, strict rule predicate vs 5 mm game margin, reference parity, physical 25 kg surface depth and center diagnostic, prior `INSUFFICIENT_DEPTH` disposition, low-load discrimination decision, exact next authorized gate, and claim ceiling.
- No physics, target, rule-sequence, or reference-angle tuning; no added support or transform/velocity/force/torque writes; no heavy-load calibration; do not start GAM-14 or merge/close GAM-13.
