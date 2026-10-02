# ADR-GAM49 — Squat depth landmark authority

- Status: Accepted
- Date: 2026-09-24
- Issue: Linear GAM-49
- Branch: `work/gam-13-squat-load-calibration`

## Decision

Use one read-only `SquatDepthLandmarkProvider` for the canonical GAM-10 reference and the physical squat runtime. The provider applies the existing GAM-10 pelvis-frame hip-crease and side-specific shank-frame knee-top offsets to the current pose, returns all four world-space points, and calculates bilateral depth. Reference markers, runtime observations, target-only FK, P2, P3 legal-bottom evidence, and depth telemetry now share that output.

The pelvis-frame origin is reconstructed from the two current hip centers with the existing GAM-10 `LeftHipOffsetInPelvisFrame` and `RightHipOffsetInPelvisFrame`, matching the GAM-10 reference solver. Physical frame rotations are mapped through the existing physical-body/reference-bone calibration. Joint-center depth is emitted separately as `SquatJointCenterDepthDiagnostic`; it has no legal predicate and is not available to P2 or P3.

Depth is measured in Unity world frame `W`, in meters, along `+Y` up:

```text
dL = hipCreaseLeft.y  - kneeTopLeft.y
dR = hipCreaseRight.y - kneeTopRight.y
worstSideDepth = max(dL, dR)
```

Negative values mean the corresponding hip-crease proxy is lower. No bilateral averaging is permitted.

## Rule and game judgment

The official IPF Technical Rule Book, Version 3 effective 01 March 2026, §4.1 item 3 (PDF page 20), requires the top surface of the legs at the hip joint to be lower than the top of the knees. This is the strict `IPF_RULE_PREDICATE`: `dL < 0 && dR < 0`. [Official IPF rule book](https://www.powerlifting.sport/fileadmin/ipf/data/rules/technical-rules/english/2026_IPF_Technical_Rulebook__effective_01_March_2026__v3.pdf).

The unchanged `GAME_JUDGMENT_MARGIN_M = 0.005` is a project game threshold, not an IPF-prescribed distance. Game qualification is bilateral `worstSideDepth <= -0.005 m`. The observation stores the strict IPF predicate and game qualification separately.

## GAM-10 calibration provenance

The authoritative calibration remains `GAM10_CANONICAL_QUATERNIUS_JOINT_FRAMES_V2` in `SquatReferenceRigCalibration`:

- Hip crease: inset `0.055 m`, down `0.055 m`, forward `0.060 m`, stored in the left/right pelvis-frame offsets.
- Knee top: up `0.045 m`, forward `0.035 m`, stored in the left/right shank-frame offsets.
- No landmark offset was added or changed. The canonical reference motion, target mapping, physics parameters, support authority, balance/preload, joint limits, and rule sequence were not tuned.

## Gate 2 — Reference parity and visual qualification

The shared provider matched the pre-GAM-49 GAM-10 calculation at every required state within the predeclared `1e-5 m` tolerance. Bilateral points were finite and repeated evaluations were deterministic.

| State | Phase | Existing GAM-10 depth, both sides (m) | Provider depth, both sides (m) | Game qualified |
|---|---:|---:|---:|---|
| Standing | 0.00 | `+0.328800082` | `+0.328800082` | No |
| Descent | 0.25 | `+0.296560347` | `+0.296560347` | No |
| Descent | 0.55 | `+0.131952822` | `+0.131952822` | No |
| Descent | 0.80 | `+0.021256626` | `+0.021256626` | No |
| Bottom | 1.00 | `−0.076313080` | `−0.076313080` | Yes |
| Ascent | 0.64 | `+0.113025844` | `+0.113025844` | No |

At bottom the strict IPF predicate and the existing 5 mm game qualification both pass. Graphics-enabled evidence shows the actual canonical Quaternius humanoid from world `+X` sagittal side view with provider-positioned hip-crease and knee-top markers: [GAM49-depth-landmarks-side.png](Artifacts/Evidence/GAM-49/GAM49-depth-landmarks-side.png). Marker size/color is display-only; marker centers are unchanged.

## Gate 3 — Physical C0/HOLD decomposition

Each 25 kg C0 and HOLD run used a fresh Unity process. Both retained support and finite valid control and matched within `1e-5 m`.

| Metric (worst side) | C0/HOLD value (m) | Interpretation |
|---|---:|---|
| GAM-10 reference surface proxy | `−0.07631302` | Game-qualified |
| Applied-target surface proxy | `−0.07662012` | Game-qualified |
| Actual physical surface proxy | `−0.05245333` | Game-qualified; `47.453 mm` beyond the `−0.005 m` threshold |
| Applied-target joint-center diagnostic | `+0.02226769` | Shallow on this diagnostic metric |
| Actual joint-center diagnostic | `+0.04317535` | Shallow; diagnostic only |
| Joint-center realization delta | `+0.02090766` | Reproduces the prior `~20.9 mm` diagnostic gap |
| Surface-proxy realization delta | `+0.02416680` | Actual is shallower than target but remains game-qualified |

The prior physical realization gap is still measurable on joint centers. It does not make the rule-authoritative surface proxy illegal.

## Gate 4 — Canonical 25 kg lifecycle

C0 and HOLD were rerun in separate fresh Unity processes, followed by the canonical 25 kg dynamic attempt in a third fresh process. The attempt trace matched the provider output at every sampled tick.

- Dynamic worst-side surface depth: `−0.05372518 m`; strict IPF predicate true; 5 mm game qualification true; residual deficit `0 mm`.
- Joint-center diagnostic at the same surface-deepest sample: left `+0.04170150 m`, right `+0.03823513 m`.
- P2: `EVALUABLE/GOOD_LIFT`, `P2_VIOLATIONS=NONE`, and no `INSUFFICIENT_DEPTH`.
- P3: physical descent, bottom, ascent, and lockout all seen; `P3_LEGAL_BOTTOM_SEEN=true`; completion prerequisites missing `NONE`; `EVALUABLE/NO_PHYSICAL_FAILURE`.
- Start and support authority passed. P2 and P3 consume the same snapshot surface qualification; center diagnostics do not feed either judgment.

## Resolution and next gate

Classify the prior `INSUFFICIENT_DEPTH` as an **observation-contract defect**. The corrected surface-rule proxy is legal in both held C0/HOLD and the dynamic 25 kg lifecycle. Low-load realization discrimination is not needed. The next authorized gate is **GAM-13 ordered load calibration**. GAM-13 remains open; GAM-14 was not started.

## Claim ceiling

This establishes parity for the repository’s deterministic GAM-10 bone-frame proxy and the measured Unity/PhysX runtime at the tested 25 kg condition. The landmarks are not direct skin-surface measurements or anatomical joint-center measurements, and the result is not a claim that the game reproduces referee visual judgment or universal biomechanics.

## Receipts

- Gate 0 contract and producer/consumer inventory: [`GAM49-depth-landmark-authority-plan.md`](GAM49-depth-landmark-authority-plan.md).
- Gate 1 provider and deterministic regressions: [`gate1-provider-receipt.md`](Artifacts/Measurements/GAM-49/gate1-provider-receipt.md).
- Gate 2 phase ladder, tolerance, and visual receipt: [`gate2-reference-parity.md`](Artifacts/Measurements/GAM-49/gate2-reference-parity.md), with the image linked above.
- Gate 3 C0/HOLD target and actual decomposition: [`gate3-C0_FULL-decomposition.md`](Artifacts/Measurements/GAM-49/gate3-fresh-process/gate3-C0_FULL-decomposition.md) and [`gate3-HOLD_1.00_FULL-decomposition.md`](Artifacts/Measurements/GAM-49/gate3-fresh-process/gate3-HOLD_1.00_FULL-decomposition.md).
- Gate 4 fresh-process C0/HOLD and lifecycle receipt: [`GAM49-gate4-requalification-receipt.md`](Artifacts/Measurements/GAM-49/gate4-fresh-process/run-20260924-035312/GAM49-gate4-requalification-receipt.md); the canonical trace is [`dynamic-baseline-trace.csv`](Artifacts/Measurements/GAM-49/gate4-fresh-process/dynamic-baseline-trace.csv).
