# ADR-GAM44 — feed-forward × saddle interaction decision

Date: 2026-09-21  
Authority: Linear `GAM-44`  
Base: `4ce3c4eea056d5f989a4b4276f13059edd05685d`

## Decision

```text
INTERACTION_WINNER=F2+S1
FINAL_PRODUCTION_WINNER=NONE
```

`F2+S1` is the only frozen interaction arm that passes 230 kg, 270 kg, and the corrected 300 kg standing/setup gate. It is therefore the interaction survivor, not a production winner: the canonical 25 kg lifecycle remains in `START_WINDOW`/`SETUP` through 2200 ticks and produces no legal-depth, lockout, P3, or terminal record.

No additional preload knot, saddle value, stiffness value, capacity, gain, F3, or S3 is authorized by this decision.

## Evidence

| Arm | 230 kg | 270 kg | 300 kg setup | Interpretation |
|---|---|---|---|---|
| F1 + S0 control | FAIL | FAIL | FAIL | Corrected control reproduces GAM-43 heavy collapse; 300 no longer receives an attached-saddle pass. |
| F2 + S0 | PASS | FAIL | FAIL | 270 kg is saddle-linear-limit limited (`1.015`); angular occupancy is `0.042`. |
| F2 + S1 | PASS | PASS | PASS | Heavy standing rescued with linear occupancy `0.599` and angular occupancy `0.038` at 300 kg. |
| F2 + S2 | FAIL | FAIL | FAIL | Capture/support/posture collapse; angular limit follows rather than leads. |

The survivor passed the prescribed `25 / 60 / 100 / 140 / 155 / 170 / 230 / 270 / 300 kg` qualification and all three fresh repeats at `25 / 60 / 140 / 170 / 300 kg`.

## Authority and onset interpretation

At the F2+S0 270 kg failure, saddle linear occupancy crosses first (`tick 16`), before tracking failure (`tick 51`); angular occupancy does not cross. At the F2+S2 230 kg failure, capture (`tick 104`) and posture (`tick 253`) precede angular saddle occupancy (`tick 317`), with no linear-limit onset. The separate channels remain uncombined in the CSV evidence.

For F2+S1 heavy loads, raw and applied ankle authority remain below 1.0, hip/trunk strategy usage remains zero, and physical drive saturation remains zero. For the failing F2+S0/F2+S2 heavy cases, raw ankle demand and/or proximal strategy reaches the bound only after the relevant capture/posture collapse or saddle event; physical drive saturation is not the leading failure pattern.

## Final-eligibility boundary

The canonical 25 kg lifecycle diagnostic did not finalize. The corrected shared lifecycle harness now advances foot-contact observations, but the candidate still remains in `START_WINDOW` with adapter `SETUP` and `s_q=0` at the 2200-tick limit. Therefore:

```text
LEGAL_DEPTH=NOT_OBSERVED
PHYSICAL_LOCKOUT=NOT_OBSERVED
P3=NOT_OBSERVED
P2/P3/TERMINAL_SEMANTICS=NOT_OBSERVED
```

The guard diagnostic passes both canonical-pose and historical-target-deflection inputs at 170 kg; only the canonical-pose input remains production semantics.

## Stop boundary and claim ceiling

The GAM-44 interaction question is answered: F2×S1 rescues the heavy loaded-standing domain under the corrected setup contract. The lifecycle result prevents a production promotion. Case-A `HEAVY_LOAD_BALANCE_CONTROL_ALLOCATION` is not triggered because an F2×S arm did pass the heavy standing gates; no replacement architecture gate is assigned here.

This ADR claims only deterministic Unity/PhysX game-calibration behavior for the frozen candidate set. It does not claim biological joint loading, true COP/GRF, tissue loading, or real-world human performance.
