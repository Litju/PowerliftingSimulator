# GAM-49 saddle initial-state contract

Date: 2026-10-04

Base: `ef567cc1611a417c1c70bff6f74b06ce74ffe88f`

Sealed pass: `2aff0202d7574a17414bd4524b8e2ff8f3359884`

## Classification

`STALE_GAM49_INITIAL_ALIGNMENT_CONTRACT`

The bar and thorax were exactly aligned at the failing tick. The failing
predicate compared the configured saddle limit to the global default even
though the sealed GAM-49 harness explicitly configures a different limit.

## Fresh-process C0 measurement

The unmodified GAM-49 C0 case reached `SetLoad(25)` after the scene had
initialized. It had simulated 4 ticks before `SetLoad` reset the runtime. The
failure then occurred synchronously at tick 0, before any post-reset physics
step, with `_isInitialized=true` and the foundation runtime initialized.

| Measurement | Tick-0 result |
|---|---|
| Thorax Rigidbody transform | position `(-1.55995716e-09, 1.3498075, -0.0178438332)`; rotation `(-0.06566459, 0, 5.74058e-09, 0.9978418)` |
| Bar root transform | position `(-2.38976838e-09, 1.41404736, -0.142335773)`; rotation `(0, 0, 0, 1)` |
| Expected aligned bar root | `(-2.38976838e-09, 1.41404736, -0.142335773)` |
| Actual minus expected bar root | `0 m` |
| Joint anchors | bar `(0, 0, 0)`; thorax `(0, 0.08, -0.115)` |
| World anchor separation | `(0, 0, 0)`, magnitude `0 m` |
| Saddle relative angular separation | `0°`; bar-to-thorax world angle `7.529985°` |
| Linear joint motions and limit | X/Y/Z `Limited`; configured limit `0.10 m` |
| Angular limits | X `-20°/+20°`; Y/Z `15°` |
| Linear spring / damper / max force | `50,000 N/m` / `3,000 N·s/m` / `60,000 N` on each axis |
| Angular spring / damper / max force | `1,200` / `800` / `1,200` |
| Bar mass | `25 kg` |
| Current joint force / torque | `(0, 0, 0)` / `(0, 0, 0)` |

All measured alignment, finiteness, attachment, limit-occupancy, and break-force
predicates passed. The sole mismatch was the assertion's expected limit:
`DefaultLinearLimitM = 0.012 m`, versus the active harness override of `0.10 m`.

## First divergent contract

Commit `27a6ddccfccdf05742fcd22daae37f2dc65a9ba4`
(`fix(gam13): validate tick-zero physics contracts`) introduced the comparison
against `DefaultLinearLimitM`. The harness's `ConfigurePlant()` and its
`ExperimentalConfiguration(50000, 3000, 0.10)` were already present in sealed
pass `2aff0202`; that pass also used the 5× joint-impedance setup. The accepted
replacement authority is the saddle's effective configured value:
`ExperimentalOverride.LinearLimitM` when present, otherwise
`DefaultLinearLimitM`.

## Repair

`ValidateSaddleInitialState` now compares the joint limit to that effective
configured value and validates that it is finite and positive. No runtime
physics parameter, saddle setup, controller, or test ordering changed.

## Verification

- Focused fresh-process C0 after repair: `1/1 PASS`.
- GAM-49 Gate 4: C0 `PASS`; HOLD 1.00 `PASS`; canonical 25 kg lifecycle
  `FAIL` at the P2 depth-authority assertion (`Expected: True; But was: False`).
  The Gate 4 chain is therefore not green and no GAM-49 PR was opened.
- Physics Benchmark V1 `All`: `26/26 PASS`; independent oracle `180/180`
  gated metrics pass; squat cases 25/60/140 kg each `1/1 PASS`; aggregate
  `7,004` metrics, zero failures.
- GAM-12 affected authority: EditMode `130/130 PASS`; PlayMode `7/7 PASS`.

Fresh XML and logs remain in local `Logs/` and
`Artifacts/Measurements/GAM-49/gate4-fresh-process/run-20261004-032913/`.
The initial measurement log is retained at
`Artifacts/Measurements/GAM-49/gate4-fresh-process/diagnostic-current/C0-measured.log`
(SHA-256 `6c98eb37be802d3b2b45d7d78f863a05c91bcebffe7a6682350b071d6d4bcbb8`).

Key result hashes:

- Focused C0 after repair: `f2925232eed6dd90142af76e69e23a0dceb02f6c4ecfd2a9b9b8e44f4bfe0ff0`.
- Gate 4 C0 / HOLD / canonical XMLs: `c1fcbd1eacc9ddd50082a6ba9520a681f697ec5e3e6743960e74e2905d09f001` / `3caec0495f722180f2af4e99d26347d3724f4f94710dcb316ee1434ad49c1942` / `b1739d880b050ea38cedfd05d84c31f2b7dae9b93c84e25f3f38b363a6d3a117`.
- Physics Benchmark V1 run receipt: `3f9a424cdc7c61b142d12319b525505ee66d6a7902cdf11448e49ef097ea2995`.
- GAM-12 EditMode / PlayMode XMLs: `36562292e850c4d2df4870b15a8fdcfd7252056dc1f9f99110f0f6d18831967d` / `204e81eca83496a94367ba44df0445fd4224e127eef4ca02cfd21ff12d4d5e22`.
