# GAM-13 trim refoundation blocker receipt

MISSION
`GAM13_STATIC_TRIM_EQUILIBRIUM_MANIFOLD_DYNAMIC_MODEL_REFOUNDATION_AND_END_TO_END_CLOSEOUT`

STATUS
`BLOCKED`

START_HEAD
`dd0d747a6f6ae46ed9e64105908e2fdf25267199`

CURRENT_HEAD
`dd0d747a6f6ae46ed9e64105908e2fdf25267199` plus the uncommitted offline
trim-solver/test/evidence candidate described below.

BLOCKER_CLASS
`SADDLE_CONTACT_TOPOLOGY_PREVENTS_CANONICAL_LOADED_STANDING`

## Reconciled Stage-A baseline

Fresh current-HEAD production 1x evidence is in
`Artifacts/Measurements/GAM-13/trim-refoundation-stage-a-baseline.csv`:

```text
25 kg  PASS
60 kg  FAIL
140 kg FAIL
170 kg FAIL
300 kg FAIL
```

Historical `stage-a-standing-final.csv` was preserved unchanged; its earlier
140/170 PASS rows are stale candidate provenance, not current-HEAD evidence.
The baseline metadata was corrected during audit to the actual source profile:
athlete solver `28/1`, bar solver `12/6`. The prior `12/4` athlete metadata was
incorrect; no measured physics row changed.

## Static trim result

The deterministic bounded solver used five target-space bias channels,
`[-12,+12]` degrees, finite-difference Jacobian, damped QR, and the fixed
continuation grid specified in
`Artifacts/Research/GAM-13-static-trim-equilibrium-manifold.md`.

Production 1x:

```text
TRIM_25  local candidate; long hold PASS
TRIM_60  long hold FAIL: saddle/contact and support loss
TRIM_140 long hold FAIL: support-wrench infeasible; bias bound reached
TRIM_170 long hold FAIL: support-wrench infeasible; bias bound reached
TRIM_300 long hold FAIL: saddle/contact and support loss
```

Fixed diagnostic impedance factors 2x, 3x, 4x, 5x, 6x, and 8x were tested
with damping scaled by the square root of the spring factor. No candidate
qualified all five canonical holds. The endpoint failures remain 60 and/or
300 kg, with saddle separation/support loss. The 8x run is diagnostic-only.

## Binding evidence

- current production plant has no qualified heavy standing family;
- no tested fixed impedance produces all canonical long holds;
- the remaining endpoint failures are support/saddle topology failures with
  finite telemetry, not NaN/Inf and not a capacity calibration result;
- increasing the fixed impedance does not remove the endpoint contact failure;
- the old dynamic model remains rejected and was not used for synthesis.

## Production changes

Added only an offline/test seam:

- `Assets/Scripts/Squat/SquatStaticTrimSolver.cs`
- `Assets/Tests/EditMode/GAM13StaticTrimSolverTests.cs`
- `Assets/Tests/PlayMode/GAM13StaticTrimEquilibriumTests.cs`
- deterministic trim measurements and research/receipt artifacts.

No runtime callsite was added. P1/P2/P3/P4 remain unchanged. No capacity,
controller, failure, sticking, gain, impedance, or load-outcome production
calibration was promoted.

## Required architectural decision

Owner authorization is required to redesign and re-qualify the fixed
bar-to-upper-back saddle/contact substrate, or to amend/re-scope the canonical
standing envelope. GAM-13 must not proceed to dynamic identification,
controller synthesis, capacity, sticking, failure, held-out loads, PR, merge,
or Linear closeout until that decision produces a valid trim family.

PR_CREATED=NO
MERGE_PERFORMED=NO
GAM13_LINEAR_STATE=IN_PROGRESS
GAM14_LINEAR_STATE=BACKLOG
