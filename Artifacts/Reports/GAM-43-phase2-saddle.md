# GAM-43 Phase 2 — saddle benchmark

`FROZEN_FEED_FORWARD=F1`  · `PLANT=5x GAM-7, sqrt(5) damping`  ·
`DEVELOPMENT=25 / 60 / 140 / 170 / 300 kg`  · `HOLDOUT=230 / 270 kg`

## Result

`SADDLE_WINNER=NONE`

All three authorized saddle arms passed 25/60/140/170 kg standing and produced
a valid loaded 300 kg setup. None passed both saddle holdouts. The GAM-43 stop
condition therefore applies; Phase 3 is not run.

| Saddle | Development | 300 kg setup | 230 kg holdout | 270 kg holdout | Decision |
|---|---:|---|---|---|---|
| S0 — 50 kN/m, 3 kN*s/m, 50 mm | 4/4 standing; setup valid | valid setup only | FAIL: collapse, capture `-1.6637 m`, saddle occupancy `1.0102` | FAIL: collapse, capture `-1.7125 m`, occupancy `1.0229` | rejected |
| S1 — 50 kN/m, 3 kN*s/m, 100 mm | 4/4 standing; setup valid | valid setup only | FAIL: collapse, capture `-1.6744 m`, occupancy `1.0191` | FAIL: collapse, capture `-1.7100 m` | rejected |
| S2 — 75 kN/m, 3,674.234614 N*s/m, 50 mm | 4/4 standing; setup valid | valid setup only | FAIL: collapse, capture `-1.6554 m`, occupancy `1.0378` | FAIL: collapse, capture `-1.7001 m` | rejected |

The 300 kg rows were evaluated only for valid loaded setup as authorized; they
were not treated as standing passes. The 230/270 rows were full standing
holdouts and failed support, upright, and signed capture-hull requirements.

## Stop boundary

`INTEGRATED_CANDIDATE=NOT_RUN`  
`25KG_LIFECYCLE=NOT_RUN`  — Phase 3 was not eligible.  
`GUARD_SEMANTICS_RESULT=NOT_RUN`  — diagnostic is downstream of a selected integrated candidate.

No S3 was created. No saddle value was tuned after observing these results.
Evidence CSVs are in `Artifacts/Measurements/GAM-13/GAM43/phase2/`; fresh Unity
completion XML is in `Artifacts/Measurements/GAM-13/GAM43/raw/phase2/`.
