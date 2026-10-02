# GAM-43 Phase 1 — feed-forward benchmark

`FROZEN_PLANT=5x GAM-7 load-bearing stiffness, sqrt(5) damping, current Saddle V2`  
`DEVELOPMENT=25 / 60 / 140 / 170 kg`  · `HOLDOUT=100 / 155 kg`  · `REPEATS=1 per arm/load`

## Result

`FEEDFORWARD_WINNER=F1`

F1 and F2 passed every development and holdout hard gate. F0 passed 25/60/140
kg but failed the 170 kg development gate because canonical-pose error was
`10.0461016 deg`.

| Candidate | Development | Holdouts | Worst pose error | Worst hull margin | Worst drive demand | Worst joint limit | Decision |
|---|---|---|---:|---:|---:|---:|---|
| F0 current | 3/4 | 2/2 | 10.0461 deg | 0.0715645 m | 0.1113 | 0.2218 | rejected: 170 kg pose gate |
| F1 spine-only re-ID | 4/4 | 2/2 | 3.0635 deg | 0.0672906 m | 0.2872 | 0.0628 | selected |
| F2 whole-body re-ID | 4/4 | 2/2 | 3.3678 deg | 0.0718015 m | 0.3573 | 0.0713 | rejected: weaker overall headroom and more complex |

F1's worst-case hull margin is lower than F2's by `0.00451 m`, and F2 has a
slightly lower saddle-limit occupancy (`0.6840` vs `0.6865`). F1 is materially
better on canonical pose, COM speed, modeled drive demand, joint-limit
proximity, and balance-command usage. Its architecture is also spine-only,
which is the simpler eligible law. No weighted average was used.

## Gate detail

All F1/F2 rows were finite, upright, support-retained, attached to a valid
saddle, below the 10 degree canonical-pose gate, above the 0.01 m signed
capture-hull margin, below 5% sustained drive saturation, and below 0.95
joint/saddle limit occupancy. F0's 100/155 kg holdouts passed; that does not
repair its failed 170 kg development row.

The raw per-arm CSV evidence is in
`Artifacts/Measurements/GAM-13/GAM43/phase1/`. Fresh Unity completion XML is
in `Artifacts/Measurements/GAM-13/GAM43/raw/phase1/`.

## Freeze boundary

F1 is frozen for Phase 2. No F1 value, phase law, load knot, or gate was
changed after observing the Phase-1 results. Phase 2 may vary only the exact
S0/S1/S2 saddle property listed in the benchmark plan.
