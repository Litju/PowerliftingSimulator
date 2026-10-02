# GAM-44 guard semantics diagnostic

Candidate: `F2+S1`
Load: `170 kg`

| Guard input | Standing gate | Max canonical pose error | Max target/actual deflection | Min guard scale |
|---|---|---:|---:|---:|
| Canonical pose | PASS | 3.357° | 4.911° | 1.000 |
| Historical target deflection | PASS | 3.334° | 4.910° | 0.988 |

The canonical-pose input remains the production semantics. The historical target-deflection input is diagnostic only.

Evidence: `Artifacts/Measurements/GAM-44/guard/guard-semantics.csv` and `Artifacts/Evidence/GAM-44/guard/F2-S1-guard-playmode.xml`.
