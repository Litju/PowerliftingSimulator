# GAM-49 Gate 1 — Shared surface-rule landmark provider

```text
RESULT=PASS
BASE_SHA=a389c6f5d36a728750cb693506a564beb9489e96
UNITY=6000.3.22f1 (1c726e1fb402)
CLAIM_CEILING=GAM10_CALIBRATED_RULE_DERIVED_GAME_PROXY
```

## Contract applied

`SquatDepthLandmarkProvider` is a readonly value provider shared by the reference preview, physical adapter, and target-only FK evidence. It copies the existing GAM-10 pelvis-frame hip-center/crease offsets and side-specific shank-frame knee-top offsets at construction, then returns all four world-space points, the bilateral surface depth, and a separately named joint-center diagnostic. Valid-frame evaluation allocates no objects and makes no Unity state writes.

The provider reconstructs the current pelvis-frame origin from the bilateral current hip centers and their existing GAM-10 pelvis-frame offsets, matching the reference solver. It evaluates the existing crease/top offsets against the current pose frames; no landmark values were changed.

The rule observation carries the strict `IPFRulePredicateSatisfied` (`dL < 0 && dR < 0`) separately from `BilateralGameJudgmentQualified` (`worstSideDepth <= -GAME_JUDGMENT_MARGIN_M`). The unchanged `GAME_JUDGMENT_MARGIN_M` is `0.005 m`, described in rule metadata and telemetry as a game judgment margin, not an IPF-prescribed distance. P2 and P3 consume the immutable provider-derived surface observation. Their logic cannot read the joint-center diagnostic.

## Verification

- Full EditMode suite: **227/227 passed**, no skips. Result: [`gate1-editmode.xml`](gate1-editmode.xml).
- Shared-provider PlayMode regressions: **2/2 passed**. They verify reference provider output and bilateral points, GAM-10 bottom parity (`−0.07631308 m` left, `−0.07631314 m` right), world-yaw/translation covariance, one-side-high worst-side rejection, strict rule/game-margin separation, invalid-frame rejection, and repeated physical landmark reads that preserve every athlete Rigidbody pose, rotation, linear/angular velocity, and sleep state. Result: [`gate1-provider-playmode.xml`](gate1-provider-playmode.xml).
- Master Spec verifier passed at Gate 0; Gate 1 changed no Master Spec file.
- Unity changed one project scripting define and created a template-settings file during import. Both were restored/removed; neither is part of this candidate.

## Changed production surfaces

- `SquatReferencePreview` uses provider points for depth, markers, lines, and measurement records.
- `SquatPhysicalAdapter` uses current physical pelvis/shank frames for the same landmarks; joint centers are emitted only through `SquatJointCenterDepthDiagnostic`.
- `SquatObservationCollector` copies provider point heights into immutable snapshots. Rule processing, P3 legal-bottom evidence, and load-response analysis share the snapshot's computed game qualification.
- Physical target FK and the GAM-48 diagnostic recorder use the same provider; duplicate GAM-10 offset application was removed.

No physics parameters, support authority, reference angles, or GAM-10 landmark offsets changed. No transform, velocity, force, or torque write was added. Gate 2 phase-ladder parity and actual-humanoid side-view qualification remain pending.
