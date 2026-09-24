# GAM-49 Gate 3 — fresh-process C0/HOLD depth decomposition

Run: 20260924-032848
Unity: D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe
Process isolation: C0_FULL and HOLD_1.00_FULL each ran in a fresh Unity process.

Result: PASS

Both arms retained support and finite control. Surface-rule and joint-center values matched between arms within 1e-5 m. The reference, target composition stages, applied target, and actual physical bottom are in the per-arm decomposition files.

Actual surface-rule worst side for C0_FULL: -0.0524533279 m. Game judgment threshold: -0.005 m. Residual deficit from that threshold: 0 mm.
Actual surface-rule game qualification: true. The ~20.9 mm joint-center realization gap remains diagnostic and does not change that rule result.

Joint-center values are diagnostic only. C0/HOLD equivalence does not determine 25 kg attempt legality; Gate 4 owns the fresh canonical lifecycle decision.

Raw XML/logs and this receipt are retained in `run-20260924-032848`. The per-arm traces, summaries, decompositions, target-composition CSVs, and applied-target CSVs are in `Artifacts/Measurements/GAM-49/gate3-fresh-process`.
