# GAM-49 depth-authority divergence resolution

Date: 2026-10-05

## Classification

`D — GAM49_GATE4_RECOMPUTATION_REGRESSION`

The sealed 2aff020 Gate 4 chain reproduces: C0, HOLD 1.00, and canonical 25 kg
each pass in a fresh Unity process. Its canonical worst-side depth is
`-0.053725183 m` at tick 402.

The current physical trajectory also passes depth when the qualification
fixture supplies its normal player inputs. At the deepest current sample,
the production provider and P2 input both report left `-0.0628821552 m`,
right `-0.0628621 m`, worst `-0.0628621 m`; threshold is `-0.005 m`. P2 is
`EVALUABLE/GOOD_LIFT` with no violations, and P3 records legal bottom and
completed lockout.

The old current fixture did not send `Yield` or the bottom `Drive` input. It
left the adapter in `SQUAT_COMMAND`, then compared the standing trace depth
against P2's incomplete-attempt result. The depth assertion was invalid for
that trace. The qualification fixture now sends the normal inputs, releases
`Yield` at the first bottom, and requires an evaluable P2 result before making
the depth comparison. No production physics, threshold, or rule semantics
changed.

No commit between 2aff0202 and ef567cc changes the GAM-49/GAM-47 calculation
or runner files; those sources are identical across the range. Thus there is
no first divergent commit in the implicated calculation layer.

## Verification

- Smallest canonical 25 kg case: `1/1 PASS`.
- Full GAM-49 Gate 4: `PASS` (C0, HOLD 1.00, canonical 25 kg each `1/1`).
- GAM-12 authority: EditMode `130/130 PASS`; PlayMode `7/7 PASS`.
- Physics Benchmark V1 checks: `26/26 PASS` (12 isolated, 12 athlete, 2 B17
  editor parity); independent oracle `180/180` gated metrics pass.
- Master Spec and tracked-artifact hygiene: `PASS`.
- No GAM-13 Squat tier was run.
