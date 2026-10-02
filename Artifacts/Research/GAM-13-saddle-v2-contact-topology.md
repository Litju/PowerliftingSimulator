# GAM-13 Saddle V2 — contact-topology correction

`GAM13_SADDLE_BAR_LIMB_FILTER_V2`

Conditional design produced by `GAM13_CAUSAL_AUDIT_RUNTIME_EXECUTION` only
because intervention I1 established saddle/contact causality at 60 kg. It is
the smallest physically coherent change that implements the tested
intervention in production. It is not a tuning step.

## Measured V1 topology (runtime, not comments)

| Property | V1 value | Evidence |
|---|---|---|
| Bar | one dynamic Rigidbody, gravity on, mass = load, 12/6 solver | `causal-topology.csv` |
| Coupling | finite `ConfigurableJoint` bar → thorax, anchor (0,0,0) → (0,0.08,−0.115) | same |
| Linear | limited 0.05 m (hard), drive 50 kN/m, 3 kN·s/m, 60 kN cap | same |
| Angular | ±20° / 15° / 15° limits, 1200 N·m/rad drive | same |
| Break | 60 kN / 15 kN·m; projection None; preprocessing on | same |
| Mass scaling | `massScale = connectedMassScale = 1` | same |
| Bar/thorax collision | disabled by `enableCollision = false`; 0 callbacks, 0 penetration in every run | traces |
| Bar/limb collision filter | **not applied**: the saddle enumerated `PhysicalBarbell.GetComponentsInChildren<Collider>()`, which finds 0 colliders because the 9 bar colliders live on the authoritative bar root; 0 of 135 bar/limb pairs ignored | `causal-topology.csv` |
| Resulting contact | hands, forearms and upper arms touch the bar from tick 1 at every load | `BAR_ATHLETE_CONTACT@1` |

Bar/thorax mass ratio: 1.16 (25 kg), 2.78 (60), 6.48 (140), 7.87 (170),
13.89 (300). Thorax 21.6 kg, athlete 100 kg.

## Load path (measured)

Bar momentum balance by elimination (bar `m·a − m·g − damping` minus measured
contact normals; unreported contact friction bounded by μ = 0.625):

- The saddle joint carries ≈ 100 % of the bar's vertical support in every
  quasi-static window (25 kg: 1.03 of bar weight, friction-bounded
  0.90–1.16, while the hands pull the bar down ≈ 3 %).
- `ConfigurableJoint.currentForce` reports only hard-limit rows here: it is
  zero whenever the limit is not engaged (all ticks at 25/60/140/170 kg after
  the spawn snap) and non-zero only on the 62 limit-engaged ticks at 300 kg.
  The soft drive's force is not reported, so the joint share is established
  by elimination, not by `currentForce`.
- Quasi-static joint force over the drive's authored `k·separation` is
  0.80 in the V1 25 kg run (hands in contact) and 0.94–0.97 with limb
  contact filtered (I1, 25 and 60 kg).
- At 60 kg the hands sit at |x| ≈ 0.70–0.76 m from the bar centre, outside
  the 1.31 m collar spacing, and bear axially on the plate/collar region with
  6–8 kN each, opposite in sign (a clamp). At 25 kg (no plates) they slide in
  to ≈ ±0.48 m and exchange ≈ 25 N.

## Causal basis

| Intervention (single property, all else production) | 60 kg | 300 kg |
|---|---|---|
| I1 `saddle.ignore_bar_athlete_non_thorax=true` | capture/support failure **removed**; stable upright plateau; only the 10° posture contract still fails (11.4° abdomen error) | unchanged collapse |
| I2 `saddle.linear_limit_m=0.10` | unchanged collapse | limit engagement removed (18→115), collapse **unchanged** |

I1 is an `ESTABLISHED_CAUSE` for the 60 kg capture/support failure. I2
rejects hard-limit engagement as the cause of the 300 kg failure.

## V2 change

Enumerate the bar Rigidbody's own colliders when building the documented
bar ↔ non-thorax athlete collision filter. Everything else is unchanged:

- bar remains one dynamic Rigidbody, gravity on, same mass/inertia authority;
- joint type, anchors, limits, drives, break limits, projection,
  preprocessing and mass scaling are unchanged;
- no kinematic pinning, transform or velocity writes, hidden support,
  infinite grip, break-force change or load-specific branch;
- `BreakSaddle` restores every filtered pair, so a released bar can again
  strike the athlete.

## Topology decisions made explicitly

- **Upper-back contact as the primary vertical load path — not adopted.** The
  joint already carries the complete vertical load; no executed intervention
  indicates that replacing it with physical thorax contact changes a failure
  (I2 shows the joint's hard limit is not causal at 300 kg). Enabling
  bar/thorax contact is a separate redesign without causal support and is
  deferred.
- **Hands/arms as orientation/retention — not adopted.** V1 hand contact was
  an unfiltered collision artifact, not a retention mechanism; V2 makes the
  limbs non-load-bearing as the saddle contract already stated. Bar
  orientation retention stays with the joint's bounded angular limits and
  drives.
- **Finite retention constraint.** The existing finite joint is retained as
  the only coupling; its break limits are unchanged.
