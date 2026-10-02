# GAM-13 V2.1 standing gate

Run: `20260927-v21-footprint-standing-r2`

Branch: `work/gam-13-squat-load-calibration`

Unity: `6000.3.22f1`

The fresh-process gate started at 25 kg and stopped after that load failed. The 60, 140, 170, and 300 kg processes were not run. The PlayMode result is `Failed(Child)`; the standing test reports maximum saddle separation `0.0500068143 m`, just above its `0.05 m` criterion.

The first recorded physics sample (tick 1) verifies the corrected reference and control path:

- captured and observed support centers match: AP `-0.009719029 m`, ML approximately `0 m`;
- captured AP COM offset `-0.038730305 m` matches the current support-relative offset; AP error is `3.7e-9 m`;
- raw and applied AP corrections are both `0 rad`; AP saturation is false;
- minimum intrinsic capacity fraction is `1.0`;
- maximum modeled demand is `1.3942`, above the finite drive ceiling;
- maximum joint-anchor separation is `0.00114 m`.

This failure does not qualify as ConfigurableJoint substrate failure: the first tick shows no AP saturation or COM-reference error, intrinsic capacity is available, the modeled demand exceeds that finite capacity, and joint-anchor separation is small. Saddle separation is slightly above the standing limit on the first sample and falls below it on tick 2. The trace later records loss of upright posture, but that later state is not used to infer the initiating cause.

Per the sequential gate, qualification stops here. No higher-load standing runs, lifecycle characterization, strength-scale tuning, or ArticulationBody migration were performed.
