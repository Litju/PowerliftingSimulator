# ADR-GAM13 — heavy-load standing control

Status: `BLOCKED_PENDING_OWNER_REVIEW`  
Date: 2026-09-17  
Issue: GAM-13  
Claim ceiling: game/engine control calibration

## Observation

The GAM-12 25 kg physical baseline remains available, while the 60/140/170/300
kg probes can collapse during setup before a meaningful squat. The current
controller is qualified only on the light domain.

## Expected

GAM-13 requires one fixed physical plant to establish and hold credible setup
across 25/60/140/170/300 kg before finite capacity, sticking, and physical
failure can be calibrated.

## Actual / measured evidence

Stage-A fresh-scene holds passed 25 kg but did not pass 60 kg or 300 kg across
the bounded candidate search recorded in
`Artifacts/Measurements/GAM-13/stage-a-impedance-search.csv`. The 3x plant’s
measured target-to-COP slope also changed materially with load, so the old
0.20446 m/rad inversion cannot be reused.

## Decision

Adopt as the intended architecture, but do not qualify or freeze it yet:

1. one fixed load-bearing impedance profile;
2. bounded load-general phase × load equilibrium feedforward identified with
   balance active;
3. fresh balance-plant identification after impedance freeze;
4. finite load-independent athlete capacity only after Stage-A exit.

The candidate implementation is retained as an evidence-producing branch
change, but GAM-13 remains blocked until the physical standing contract passes.

## Rejected approaches

- stiffness-as-strength;
- linear 0/25 kg bias extrapolation;
- per-load impedance or balance-gain tables;
- posture-guard removal as a production fix;
- widening the 12° equilibrium bound;
- load-threshold failure scripting;
- changing P1/P2/P3/P4 semantics to hide setup collapse.

## Consequences

The capacity search, sticking/failure calibration, held-out probes, PR, merge,
and Linear transition are intentionally not performed. The original blocker
receipt remains immutable provenance.
