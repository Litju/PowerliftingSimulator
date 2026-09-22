# GAM-48 Gate 3 — fresh-process equivalence receipt

Run: 20260922-034036
Unity: D:\Dev\Unity\6000.3.22f1\Editor\Unity.exe
Process isolation: one Unity process per arm; no scene-reload substitution.

## Hard gate

HOLD_1.00_FULL == C0_FULL: PASS

Compared numeric fields: SETTLED_MEAN_WORST_DEPTH_M, SETTLED_MIN_WORST_DEPTH_M, SETTLED_MAX_WORST_DEPTH_M, DEEPEST_WORST_DEPTH_M, DEEPEST_SQ, BOTTOM_BAR_VELOCITY_MPS, BOTTOM_PELVIS_VELOCITY_MPS
Absolute tolerance: 1e-5

Compared categorical fields: PHASE, SUPPORT_RETAINED, FINITE_VALID_CONTROL, LEGAL, DEEPEST_TICK.

Canonical baseline fresh-process repeatability: PASS for two independent
processes, including numeric tolerance 1e-5 and categorical lifecycle/P3
classification equality.

The raw XML, logs, and per-arm summaries are retained in `run-20260922-034036`.
