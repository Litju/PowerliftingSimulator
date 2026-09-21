# GAM-44 interaction benchmark

Authority: Linear `GAM-44`  
Branch: `work/gam-13-squat-load-calibration`  
Unity: `6000.3.22f1`

## Harness correction

The GAM-43 `300 kg` attached-saddle exception was removed before new runs. `pass` now equals the same standing gate for every load. Historical GAM-43 evidence was not rewritten.

The full standing gate is finite state, upright, retained support, canonical pose `< 10°`, signed 2-D hull margin `> 0.01 m`, COM speed `< 0.25 m/s`, sustained drive saturation `< 5%`, joint-limit proximity `< 0.95`, attached/valid saddle, and no persistent linear or angular saddle-limit occupancy `>= 0.95`.

## Control

Evidence: `Artifacts/Measurements/GAM-44/control/F1-S0-control.csv`.

| Load | Result | Hull min (m) | Canonical max (deg) | Raw ankle max | Applied ankle max | Hip max | Trunk max | Saddle linear max | Saddle angular max | Drive demand max |
|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| 25 | PASS | 0.105985 | 2.809 | 1.179 | 1.176 | 0.025 | 0.015 | 0.106 | 0.007 | 0.119 |
| 170 | PASS | 0.069629 | 2.737 | 0.585 | 0.562 | 0.000 | 0.000 | 0.680 | 0.021 | 0.286 |
| 230 | FAIL | -1.663710 | 18.187 | 6.557 | 5.568 | 1.000 | 1.000 | 1.010 | 1.007 | 0.935 |
| 270 | FAIL | -1.712458 | 17.828 | 6.218 | 5.238 | 1.000 | 1.000 | 1.023 | 1.023 | 0.955 |
| 300 | FAIL | -1.718176 | 17.965 | 6.256 | 5.538 | 1.000 | 1.000 | 1.024 | 1.044 | 1.010 |

The corrected 300 kg control fails the standing gate, as required. Collapse is balance/capture/posture-led; physical saturation remains below the sustained 5% gate.

## Exact F2 × saddle matrix

Evidence: `Artifacts/Measurements/GAM-44/matrix/F2-S0.csv`, `F2-S1.csv`, and `F2-S2.csv`.

| Arm | 25 | 170 | 230 | 270 | 300 setup |
|---|---|---|---|---|---|
| F2 + S0 | PASS | PASS | PASS | FAIL | FAIL |
| F2 + S1 | PASS | PASS | PASS | PASS | PASS |
| F2 + S2 | PASS | PASS | FAIL | FAIL | FAIL |

`F2+S0` fails 270 kg with saddle linear occupancy `1.015`; angular occupancy is only `0.042`. `F2+S2` fails 230/270/300 with capture/support/posture collapse while angular occupancy follows the collapse and physical saturation is not sustained. `F2+S1` has no standing-gate failure at 230/270/300; at corrected 300 kg its linear occupancy is `0.599`, angular occupancy `0.038`, raw/applied ankle authority `0.577/0.577`, hip/trunk usage `0/0`, and modeled drive demand `0.461`.

## Survivor qualification

Only `F2+S1` advanced. It passed the full load set `25 / 60 / 100 / 140 / 155 / 170 / 230 / 270 / 300 kg` and all three fresh repeats at `25 / 60 / 140 / 170 / 300 kg`.

Evidence: `Artifacts/Measurements/GAM-44/qualification/`.

The canonical-pose versus historical-target-deflection guard diagnostic also passed both arms at 170 kg. The canonical-pose arm retained guard scale `1.000`; the historical target-deflection arm reduced it to `0.988`. This diagnostic does not authorize the historical input.

## Lifecycle boundary

The corrected lifecycle harness now advances the shared foot-contact observation boundary, but the unchanged F2+S1 25 kg lifecycle still remains in `START_WINDOW` with adapter `SETUP` and `s_q=0` through 2200 ticks. No legal-depth, physical-lockout, P3, or terminal-semantic evidence was produced. This prevents F2+S1 from being a final production winner even though it is the interaction survivor.

Claim ceiling: deterministic Unity/PhysX game-calibration evidence for the frozen candidate set; no biological joint-loading, true COP/GRF, or real-world performance claim.
