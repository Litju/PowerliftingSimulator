# GAM-48 — Observation authority and depth-isolation plan

Authority: Linear `GAM-48`
Branch: `work/gam-13-squat-load-calibration`
Base: `b3ccf058b4c4416f1426ae913f57e373a29c0817`
Unity: `6000.3.22f1`

## Scope

Repair observation and lifecycle authority only. Frozen LC1 anchors/formula,
5x plant, sqrt(5) damping, S1, F2 spine law, solver, capacity, bar model,
balance bounds, canonical rules/tolerances, phase rate/dwell, sticking/failure
calibration, and direct-assistance prohibition remain unchanged.

## Authority contract

| Concern | Authoritative source | Non-authoritative data |
|---|---|---|
| Persistent foot support | active platform collision-pair set, updated by Enter/Stay/Exit | none |
| Per-step contact telemetry | completed contact-point and impulse buffer for the just-completed physics step | persistent support state |
| Aggregate support | both foot persistent states plus the latest valid contact geometry | stale rule guesses, transforms, or phase |
| Start qualification and P2 | the same contiguous pre-command snapshots, with the explicit qualifier boundary recorded | a later re-evaluated window |
| P3 physical completion | explicit attempt boundary and post-physics physical evidence | P2 judgment, reference phase, or pre-command motion |
| Depth causes | reference geometry, FK-mapped target, then dynamic realization as separate arms | correlated observations treated as causes |

Persistent contact survives a load/reset operation when the collision pair is
still active. Reset clears only per-step buffers, slip accumulators, and other
attempt telemetry; an actual Exit removes the pair. Contact geometry is
diagnostic and never writes physics.

## Gate order

1. Gate 1: persistent support authority and deterministic contact regressions.
2. Gate 2: shared attempt/start/P2 boundary and explicit P3 boundary/evidence.
3. Gate 3: fresh-process held/composition isolation and `HOLD_1.00_FULL == C0_FULL`.
4. Gate 4: current-head reference qualification, FK mapping decomposition,
   corrected C0/C1/C2, and canonical 25 kg dynamic lifecycle.

Each gate is validated before one exact-file atomic commit. No physics tuning or
causal classification is allowed before Gates 1–3 pass. If Gate 4 remains
non-identifying, the ADR records the unresolved hypothesis set and the exact
next authorized physics gate without tuning.

## Required regression coverage

- load/reset while both feet remain planted;
- sustained static contact, including sleeping/static-body behavior when the
  engine produces no new manifold callback;
- contact loss and re-entry;
- one authoritative start window shared by qualification and P2;
- pre-command settling excluded from physical descent;
- P3 completion independent of P2 and requiring descent, physical bottom,
  legal bottom, ascent, and physical lockout evidence;
- fresh Unity-process repeatability and declared numeric tolerances.

## Validation

Before each commit: focused EditMode/PlayMode contracts, affected squat
regressions, Master Spec verification, `git diff --check`, exact staged-file
review, and no Unity-generated churn. Experimental equivalence uses separate
Unity processes, not scene reloads or same-process repetitions.
