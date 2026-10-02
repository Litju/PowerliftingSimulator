# ADR-GAM13 — loaded-standing architecture benchmark

Status: `SEALED_NO_WINNER`
Issue: GAM-43  · Parent: GAM-13  · Date: 2026-09-21
Claim ceiling: game/engine control calibration on Unity 6000.3.22f1, same-machine PhysX evidence

## Decision

`WINNER=NONE`

No production architecture is authorized by this experiment. The Phase-1
feed-forward winner was `F1`, but no authorized saddle variant passed both
Phase-2 holdouts. The final winner rule therefore returns NONE.

The exact eligible intermediate arm was:

```text
5x GAM-7 load-bearing stiffness
sqrt(5) GAM-7 damping
F1: bounded spine-only phase x load re-identification
S*: none
```

F1 was not promoted because it has no eligible S* partner.

## Hard-gate results

Phase 1:

* F0: 25/60/140 passed; 170 failed canonical pose at `10.0461016 deg`; 100/155 holdouts passed.
* F1: all four development loads and both holdouts passed every standing gate.
* F2: all four development loads and both holdouts passed every standing gate.

Phase 2:

* S0: 25/60/140/170 passed; 300 setup valid; 230/270 failed.
* S1: 25/60/140/170 passed; 300 setup valid; 230/270 failed.
* S2: 25/60/140/170 passed; 300 setup valid; 230/270 failed.

The Phase-2 holdout failures were physical standing failures, not merely a
scorecard rejection: each arm lost support and failed upright/capture behavior
at one or both holdouts. S0/S1/S2 were not eligible.

## Worst-case margins

Best eligible Phase-1 arm F1 had worst observed margins of:

* canonical pose: `10 - 3.063481 = 6.936519 deg`;
* signed capture hull: `0.06729059 - 0.01 = 0.05729059 m`;
* drive demand: `1 - 0.2872175 = 0.7127825` to the modeled saturation threshold;
* joint-limit proximity: `0.95 - 0.0627778 = 0.8872222`;
* saddle-limit occupancy: `0.95 - 0.6865349 = 0.2634651`.

Those margins describe an intermediate arm, not a final winner.

For the best Phase-2 holdout attempt, S2 at 230 kg, the signed hull margin
was `-1.65539014 m` and saddle-limit occupancy was `1.03779888`; at 270 kg the
hull margin was `-1.70010066 m`. Both are hard failures.

## Rejected variants and holdout behavior

* F0 current feed-forward: rejected at the 170 kg development pose gate.
* F2 whole-body re-identification: eligible in Phase 1, rejected in favor of
  F1 by worst-case headroom and simpler architecture.
* S0 current saddle: 230/270 holdouts failed.
* S1 100 mm travel: 230/270 holdouts failed.
* S2 stronger finite saddle: 230/270 holdouts failed.

No S3 was created and no value was tuned after results.

## Lifecycle and guard result

`25KG_LIFECYCLE=NOT_RUN` because no integrated candidate existed.
`GUARD_SEMANTICS_RESULT=NOT_RUN` for the same stop-boundary reason.
No P2/P3/terminal claim is made by GAM-43.

## Limitations

* The campaign did not test an integrated winner, a squat lifecycle, or guard
  semantics because the predeclared Phase-2 stop condition fired.
* 300 kg was evaluated only as a valid loaded setup, as authorized; it was not
  treated as a standing pass.
* Evidence is same-machine Unity/PhysX evidence, not a cross-platform
  determinism claim.
* The feed-forward and saddle overrides are reversible experiment seams with
  defaults preserving the pre-experiment production behavior. They are not a
  production winner implementation.

## Claim ceiling

The evidence supports only this claim: the exact GAM-43 candidate set did not
produce a loaded-standing architecture that satisfies all prescribed
development and holdout gates. It does not establish the cause of the 230/270
failures beyond the recorded telemetry, and it does not authorize capacity,
sticking, failure, or production-control changes.

## Proposed production specification

`NONE`. Keep GAM-13 production architecture unchanged pending a separately
authorized architecture decision for the 230/270 kg standing failure. Do not
implement F1, S0, S1, or S2 as a production winner from this experiment.

## State

```text
GAM43_STATE=COMPLETE_NO_WINNER
GAM13_STATE=IN_PROGRESS
GAM14_STATE=BACKLOG
NEXT_GATE=REVIEW_ADR_AND_FREEZE_PRODUCTION_SPEC
```
