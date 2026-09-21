# GAM-44 — F2 × saddle interaction benchmark

Authority: Linear `GAM-44`  
Branch: `work/gam-13-squat-load-calibration`  
Start head: `4ce3c4eea056d5f989a4b4276f13059edd05685d`  
Unity: `6000.3.22f1`

## Frozen candidates

Only the existing GAM-43 candidates are used:

- `F1`: 5x spine-only re-identification.
- `F2`: 5x whole-body re-identification.
- `S0`: `50,000 N/m`, `3,000 N·s/m`, `0.050 m` linear limit.
- `S1`: `50,000 N/m`, `3,000 N·s/m`, `0.100 m` linear limit.
- `S2`: `75,000 N/m`, `3,674.234614 N·s/m`, `0.050 m` linear limit.

Athlete stiffness, damping profile, solver iterations, capacity, bar mass/inertia, balance gains, and authority bounds remain frozen.

## Standing qualification

Every load, including `300 kg`, uses one standing/setup gate over ticks 100–599:

- finite telemetry;
- upright;
- support retained;
- canonical pose error `< 10°`;
- signed 2-D capture-hull margin `> 0.01 m`;
- COM speed `< 0.25 m/s`;
- sustained modeled drive saturation `< 5%`;
- joint-limit proximity `< 0.95`;
- saddle attached, unbroken, and valid;
- no persistent linear or angular saddle-limit occupancy `>= 0.95`;
- saddle separation below the existing plausible-separation bound.

The prior GAM-43 `300 kg` attached-saddle exception is retired. Historical GAM-43 CSVs are not rewritten.

## Runs

Phase 1 control: `F1 + S0` at `25 / 170 / 230 / 270 / 300 kg`, one fresh scene per load.

Phase 2 matrix: `F2 + S0`, `F2 + S1`, and `F2 + S2` at `25 / 170 / 230 / 270 / 300 kg`, one fresh scene per load and arm.

No `F3`, `S3`, stiffness, gain, capacity, preload, or post-result tuning is allowed.

## Telemetry and onset semantics

Each run writes one aggregate CSV row and, when enabled, one post-physics raw row per authoritative 0.01 s tick. The aggregate records pose/deflection, hull/AP margins, COM speed, support/contact state, raw and applied ankle authority, posture guard scale, hip strategy blend and usage, trunk usage, modeled demand, saturation, joint limits, COP authority, saddle separation, separate linear/angular occupancy, current force/torque, and the standing gate result.

The first-onset fields use the frozen `GAM13_CAUSAL_ONSET_PREDICATES_V1` rule: the first tick of three consecutive true samples. Tracking uses target/actual deflection `>= 10°`; capture uses the AP capture margin `<= 0.01 m`; posture uses canonical pose `>= 10°`, trunk pitch `>= 0.70 rad`, or pelvis height `<= 0.90 m`; support uses loss of production support; saddle events use attached linear or any-axis angular occupancy `>= 0.95`.

Linear and angular saddle occupancy remain separate diagnostic fields. Engine `currentForce/currentTorque` remain engine diagnostics, not biological force claims.

## Decision boundary

If every F2×S arm fails `230 kg` and/or `270 kg` with raw ankle demand above available authority, proximal strategy at/near bounds, capture/support collapse, and physical drive saturation not leading, record `INTERACTION_WINNER=NONE`, seal the evidence, and stop at `HEAVY_LOAD_BALANCE_CONTROL_ALLOCATION`.

Only if a candidate qualifies `230 kg`, `270 kg`, and corrected `300 kg` setup does the holdout/full-qualification branch run. Selection uses hard gates, holdouts/generalization, Pareto robustness, then simplicity for a material tie; no mixed-unit scalar score is created.

Claim ceiling: this is a deterministic Unity/PhysX game-calibration benchmark for the frozen candidate set. It does not establish biological joint loading, true COP/GRF, or real-world human performance.
