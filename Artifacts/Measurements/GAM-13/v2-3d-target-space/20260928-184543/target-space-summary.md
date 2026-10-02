# GAM-13 V2-3D target-space representation

Focused PlayMode regression: **PASS, 1/1**, fresh Unity `6000.3.22f1` process 22844. Only the representation test ran; the 500-tick standing gate was not run.

## Classification

**CASE A — logical production target contract is correct.** The logical ankle balance offset, composed target delta, issued `JointCommand`, and `PoweredJointController.AppliedTarget` preserve both correction signs. Unity `ConfigurableJoint.targetRotation` is the expected inverse representation and matched `ToUnityTargetRotation(AppliedTarget)` with `0°` quaternion error.

The first 25 kg Gate B trace sample admitted by the old assertion is tick 2: `AppliedApRad=+0.000330548617`, `AnkleApRad=+0.000330548617`, and `AnkleMlRad=-3.49903672e-7`. This is a sub-milliradian twist. In a focused reproduction (`AppliedApRad=+0.000330527982`, `AnkleMlRad=-3.49627868e-7`), the logical balance quaternion was `(0.000165263991, 2.88904491e-11, -1.74813934e-7, 1)`. Unity's float `Quaternion.ToAngleAxis` reports its signed twist as `0 rad` because `w` rounds to `1`; the strict test product becomes `0`, not a negative logical target. The original failure is therefore in the assertion's near-zero twist extraction, not in target composition, command issuance, applied target, or Unity conversion.

## Representative positive and negative corrections

| Stage | Positive correction | Negative correction |
|---|---:|---:|
| `AppliedApRad` | `+0.0156509858 rad` | `-0.0156509727 rad` |
| `AnkleApRad` / `AnkleMlRad` | `+0.0156509858 / 0 rad` | `-0.0156509727 / 0 rad` |
| `SignedTwist(BalanceOffset, +X)` | `+0.01564029 rad` | `-0.01564029 rad` |
| `SignedTwist(Final * inverse(Nominal), +X)` | `+0.01564029 rad` | `-0.01564029 rad` |
| issued command delta twist | `+0.0156555269 rad` | `-0.0156555269 rad` |
| applied target delta twist | `+0.01564029 rad` | `-0.01564029 rad` |
| Unity `targetRotation` twist | `-0.01564029 rad` | `+0.01564029 rad` |
| conversion error | `0°` | `0°` |

The full quaternion values for nominal, balance offset, final target, logical delta, issued command, requested command, applied target, Unity target, and converted target are in `unity.log`. The test separately asserts the Unity inverse representation by quaternion equality; it does not require Unity's stored twist sign to equal logical sign.

## Contract repair

The standing gate now checks logical balance, composed-target, and command deltas only when `|AnkleApRad| >= 0.001 rad`, above the measured `ToAngleAxis` zero-resolution region. No production physics, controller parameters, or Unity conversion changed.
