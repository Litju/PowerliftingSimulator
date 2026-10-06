# GAM-50 Final Qualification V2

STATUS=`PHYSICS_BENCHMARK_QUALIFIED` · PHYSICS_SUBSTRATE=`FROZEN` · TESTED_MAIN_SHA=`9a73bec06e1c29f10727f9d7bc127fd8a7f236f8`

Unity: `6000.3.22f1`; LFS hydrated before qualification.

## Qualification results

- V2-3C plant sign: `PASS`; `θ± = ±0.0174532924 rad`; `COP+ = 0.03541779 m`; `COP− = -0.149266392 m`; `ΔCOP = 0.184684187 m`; `G_ap = +5.290812 m/rad`; signal/scatter `7.785`; bilateral contact and `BalanceOffset` AP realization passed. Classification: `POSITIVE_ANKLE_TARGET_MOVES_COP_FORWARD`.
- Physics Benchmark V1: `26/26 PASS`; oracle: `150` cases, `4,050` metrics, `180/180` gated metrics passed.
- B14 mechanics: `25 / 60 / 140 / 170 / 300 kg`, each `1/1 PASS`.
- B15 repeatability: `25 kg 10/10`, `140 kg 10/10`; state-hash sequences, event ticks, and outcomes were identical; bar-height spread `0 m`.
- GAM-12: EditMode `130/130 PASS`; PlayMode `7/7 PASS`.
- GAM-49 Gate 4: `PASS` (C0, HOLD 1.00, canonical 25 kg lifecycle).
- V2 stabilizer EditMode: `6/6 PASS`.
- Default EditMode: `248 passed`, `3 skipped`, `0 failed`.
- Master Spec and tracked artifact hygiene: `PASS`.

The V23C repair changed only the test fixture. No production physics or controller code changed. Qualification evidence is retained under `E:\Data\Projects\_gam13_v23c_repro\final-qualification`.
