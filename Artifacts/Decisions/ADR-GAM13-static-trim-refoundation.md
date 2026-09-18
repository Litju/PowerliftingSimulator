# ADR-GAM13B — static-trim refoundation and contact-topology blocker

Status: `BLOCKED_PENDING_OWNER_REVIEW`
Date: 2026-09-18
Issue: GAM-13
Claim ceiling: game/engine control calibration

## Observation

The current production 1x plant has one qualified canonical standing hold at
25 kg and no qualified canonical long hold at 60, 140, 170, or 300 kg. The
failure remains finite physical setup/support/saddle departure.

## Method

`GAM13_STATIC_TRIM_SOLVER_V1` evaluated symmetric target-space biases for
ankle, knee, hip, abdomen, and thorax within the frozen ±12 degree bound. It
used a deterministic finite-difference Jacobian, damped Householder QR,
predeclared continuation loads, local physical residual windows with dynamic
balance feedback disabled, and production-feedback long holds. No direct force,
torque, velocity, transform, load-outcome, or random-search path was used.

## Evidence

- reconciled Stage-A: `trim-refoundation-stage-a-baseline.csv`;
- production 1x continuation/solutions/holds/classification: `trim-*.csv`;
- fixed impedance counterfactuals: `trim-*-impedance-{2,3,4,5,6,8}x.csv`;
- method and interpretation:
  `Artifacts/Research/GAM-13-static-trim-equilibrium-manifold.md`.

The production 1x heavy failures reach the equilibrium-bias boundary and/or
lose support/saddle validity. Fixed load-bearing impedance factors 2x, 3x, 4x,
5x, 6x, and 8x still leave at least one canonical endpoint outside the
standing contract; 60 and 300 kg are the recurring endpoint failures.

## Decision

Do not promote an impedance factor, controller, capacity model, or dynamic
model. The evidence is sufficient to block GAM-13 at the contact-topology gate,
but not to claim that every future saddle/contact substrate is impossible.

Owner review must authorize either:

1. a fixed saddle/contact substrate redesign followed by a fresh trim
   continuation and long-hold qualification; or
2. a formal amendment/re-scope of the canonical standing envelope.

Until then, capacity, sticking, supra-max failure, dynamic identification,
controller synthesis, held-out loads, PR, merge, and Linear closeout remain
out of scope.
