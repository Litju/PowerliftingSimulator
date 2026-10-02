# GAM-47 — 25 kg dynamic-depth regression isolation plan

Mission: `GAM47_25KG_DEPTH_REGRESSION_ISOLATION`
Authority: Linear `GAM-47`
Base/expected start head: `f32d3581d85a6e886f5ccbd13142b9b417fafa5f`
Branch: `work/gam-13-squat-load-calibration`
Unity: `6000.3.22f1`

## Frozen scope

This experiment uses the canonical `5x + LC1 + F2 spine + S1` architecture at
25 kg. LC1 anchors/formula, 5x plant, S1, F2 spine, balance gains/authority,
capacity, solver, bar mass/inertia, rules, tolerances, depth margin, and P1/P2/
P3/P4 semantics are unchanged.

Only read-only diagnostics and test-only experiment controls may be added. No
force, torque, velocity, transform, gain, capacity, rule, tolerance, phase-rate,
dwell, sticking/failure, LC2, GAM-14, or GAM-13 merge change is authorized.

## Phase 0 — observability contract

Every experiment sample records:

- adapter state and `s_q`;
- nominal joint target, equilibrium/preload contribution, dynamic balance
  contribution, final composed target, actual joint orientation, target/actual
  deflection, and actual-vs-canonical error;
- bilateral hip-crease Y, bilateral knee-top Y, bilateral depth, worst-side
  depth, and legal-depth bool;
- pelvis/bar height and velocity, COM/capture/support, foot contact/slip, raw
  and applied ankle authority, hip/trunk strategy, drive demand/saturation,
  joint-limit proximity, and saddle state;
- P3 physical descent, physical bottom, legal bottom, ascent established,
  physical lockout, and terminal-context coverage.

Diagnostics are copied observations only. They cannot write physical state or
change the production control path.

## Phase 1 — dynamic baseline

Run one fresh canonical 25 kg LC1+S1 lifecycle. Report exactly:

- deepest depth, deficit to `-0.005 m`, deepest-depth tick, and `s_q` at that
  tick;
- target/actual errors at deepest depth;
- support-violation source;
- exact P2 pre-Squat samples evaluated by P2;
- exact missing P3 stage(s), independently of P2.

Commit the baseline evidence before starting the held-phase ladder.

## Phase 2 — held-phase ladder

Run fresh independent holds through the normal reference/control path at
`s_q = 0.00`, `0.25`, `0.55`, `0.80`, and `1.00`. After the requested phase is
entered, freeze only phase progression. Do not pin transforms, write
velocities, add forces/torques, or change gains.

The fixed settle window is **200 authoritative ticks / 2.00 s** at 100 Hz.
All ticks are recorded. The settled report is the final 50 ticks; its
worst-side-depth value is the arithmetic mean of those 50 raw worst-side
depths. Support must remain retained on every settled tick, and all recorded
control values must remain finite and valid. No window or threshold may be
changed after seeing a result.

At `s_q=1.00`:

- `HELD_BOTTOM_LEGAL` iff settled mean worst-side depth `<= -0.005 m`, support
  is retained throughout the settled report, and control is finite/valid;
- otherwise `HELD_BOTTOM_SHALLOW`.

If legal, classify `ROOT_DOMAIN=DYNAMIC_TRACKING_TIMING` and quantify time at
`s_q >= 0.90`, phase rate/acceleration, joint tracking lag, deepest-depth lag,
bottom dwell, bar/pelvis velocity near bottom, and capture/support through the
transition. Do not tune phase rate or dwell.

If shallow, classify `ROOT_DOMAIN=DEEP_PHASE_EQUILIBRIUM_COMPOSITION` and run
only the held `s_q=1.00` composition audit:

- `C0`: nominal + equilibrium/preload + dynamic balance;
- `C1`: nominal + equilibrium/preload, dynamic balance zeroed;
- `C2`: nominal only, equilibrium/preload and dynamic balance zeroed.

Plant, S1, LC1, capacity, solver, drives, rules, tolerances, and the settle
window remain fixed. Classify only by the predeclared legal/support/finite
criteria. Stop if C2 remains shallow.

## Semantic audit

Compare the recorded pre-Squat samples actually evaluated by P2 with the
pre-record qualifier samples. Classify `FAILED_START_POSITION` as
candidate-specific, pre-existing lifecycle/orchestrator semantics, or both.

For `SUPPORT_VIOLATION`, identify actual support loss, accumulated slip beyond
the existing threshold, or slip speed beyond the existing threshold. Do not
use `support_ever_lost=false` as a rebuttal by itself.

For P3, identify the first exact missing physical-completion prerequisite from
the recorded P3 booleans/stage coverage. Do not infer P3 from P2.

## Commit and validation gates

1. Commit this plan before any new result.
2. Commit read-only diagnostics and focused contracts.
3. Commit dynamic baseline evidence.
4. Commit held-phase ladder evidence.
5. Commit composition-audit evidence only if Decision B runs.
6. Commit the ADR with root domain, evidence, rejected hypotheses, claim
   ceiling, and next gate.

Before each commit and at completion run focused GAM-47 tests, relevant
lifecycle/depth tests, MasterSpec verification, `git diff --check`, and exact
diff review. Push non-force only after the coherent evidence set is complete.

## Stop conditions

Do not tune or merge. Do not run C1/C2 after Decision A resolves the domain.
Stop after `C2` remains shallow, after a required evidence path is unavailable,
or when a requested result cannot be established without changing frozen
semantics.
