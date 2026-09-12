# GAM-11 Phase 5H1 — ground registration and foot contact repair

Start `902a5fd` · Head `55f27d0` · Checkpoint `checkpoint/gam11-pre-ground-registration`

**The authorized substrate repair is done and qualified.** The athlete now
stands on the platform from tick zero. The standing gate still does not pass,
but for a different and better-understood reason.

## The repair

| | before | after |
|---|---|---|
| platform support plane | y = 0.0000 (assumed) | y = 0.0000 (derived from the platform collider) |
| reference plantar anchor L/R | 0.09423 / 0.09423 | 0.00000 / 0.00000 |
| foot collider sole L/R | 0.04680 | 0.00000 |
| collider sole minus plantar anchor | −47.5 mm | **0.00 mm** |
| spawn gap L/R | 46.8 mm | **0.00 mm** |
| first contact tick | 9 | **0** |
| pre-contact descent speed | ~1.1 m/s | **0.0000 m/s** |
| pelvis loss over 20 ticks | 50–60 mm | **8.65 mm** |
| rig registration offset | — | −0.09423 m |

Two changes, both initial construction only:

1. **Foot collider seated on the canonical sole.** The 0.130 × 0.100 × 0.290 m
   box was centred on the foot body origin, leaving its underside 47.5 mm below
   the plantar anchor the accepted reference defines. Only the collider centre
   moved; size is unchanged.
2. **Rig registered to the platform.** The authored rig sat 94.2 mm above the
   support surface. It is translated once, before any body exists and long
   before the first simulated tick. GAM-10 geometry is untouched — the pose is
   identical, it is simply placed on the ground.

Registration has to precede body construction, so the scene controller now runs
at execution order −950 (ahead of the rig at −900), and the rig and barbell skip
their own `Start` build when a controller already built them.

**Mass properties are safe and proven safe.** `centerOfMass` and
`inertiaTensor` are both authored explicitly in `CreateSegment`, so moving a
collider cannot change them. `GR4` asserts this rather than assuming it: foot
mass 1.45 kg, COM at the body origin, inertia equal to `BoxInertia(mass,
dimensions)`, inertia rotation identity — all unchanged.

New invariants, all passing: `GR1` collider-to-plantar registration, `GR2`
rig-to-platform registration, `GR3` no authored gap **or penetration** (both
directions — a penetration would be a hidden spring faking the support we are
trying to earn), `GR4` mass properties authored not derived, `GR5` no ballistic
startup.

## Experiment A on the grounded plant

The question Phase 5H could not ask. `GAM11-experiment-a-baseline.csv`:

```
tick pelvis_y  com_ap   com_vel  cop_meas  ankle_act  knee_act  hip_act contacts
   0  0.9808  -0.0318   0.0000     -         0.00      0.00     0.00      0
   5  0.9737  -0.0317   0.0091   -0.0866    -0.40      0.26    -0.23      8
  20  0.9730  -0.0208   0.1066   -0.0701    -2.42      1.02     0.42      8
  60  0.9505   0.0641   0.3321   -0.0287   -11.64      1.98     5.59      8
 120  0.7323   0.4545   1.1187    0.1353   -40.03      0.85     9.04      8
```

**There is no sag.** The pelvis holds within 7 mm for the first 0.2 s and the
knee and hip barely move (2 deg and 9 deg at the point of departure). The plant
is a clean single inverted pendulum toppling forward about the ankle, with the
COP sitting ~55 mm behind the COM. That is the whole failure, and it is now
legible.

## Preload identification

`GAM11-ankle-preload-cop-sensitivity.csv`, measured at 0.6 s while still
upright, balance off:

| ankle bias | COM − COP | COM velocity |
|---|---|---|
| 0 | +0.0909 | 0.325 |
| −4 | +0.0593 | 0.223 |
| −8 | +0.0272 | 0.118 |
| −10 | +0.0112 | 0.065 |
| **−12** | **−0.0049** | **0.011** |

Monotonic, crossing zero at about **−11.5 deg** of ankle plantarflexion bias —
the same direction the Phase 5H sweep favoured.

Two things worth recording about the mechanism, because both contradict the
model I used earlier:

- The COP moves only **9 mm** across the entire ladder while the COM moves
  **87 mm**. The preload is not modulating the centre of pressure; it is
  holding the body where it started.
- The realised ankle torque is roughly **a fifth** of `spring × error`. Every
  torque estimate derived from the nominal `positionSpring` — including the
  2.4 deg prediction in the Phase 5H report — has been optimistic by about that
  factor. The spring is not changed; the control layer should use the measured
  effective gain.

## Experiment B — fails, structurally

Committed failing. At −11.5 deg the athlete holds near its start pose at 0.6 s
and the time to fall extends substantially, but it still departs by ~2.7 s.

This is not a tuning gap. **A feed-forward trim cannot stabilise a statically
unstable plant.** An inverted pendulum has no open-loop equilibrium that
survives perturbation; the preload cancels the steady gravitational moment at
one posture, and any residual grows exponentially from there. Experiment B as
specified — 5 s, balance off, no monotonic divergence — is not achievable open
loop for this plant, at any preload.

## Experiment C — attempted, and why the preload is not adopted

With −11.5 deg preload plus the Phase 5 predictive balance controller, the
athlete departs **backwards** (COM AP −0.032 → −0.554 m). The preload was
identified with feedback disabled, so with the loop closed the two corrections
stack and overshoot.

`QualifiedStanding()` therefore stays **zero on every family**. The −11.5 deg
value is recorded in the code comment and the measurements, not shipped: it
sits on the 12 deg investigation boundary rather than near the 6 deg soft
target, and it destabilises the closed loop.

## Recommended next step

The plant is now correct and its behaviour is simple and legible. The preload
must be identified **with the balance loop closed**, jointly with the
controller gains, rather than open loop and then handed over. Concretely: a
small joint grid over ankle preload and the balance proportional gain, scored
on the 10 s gate. The measured effective ankle stiffness (about a fifth of
nominal) should replace the nominal spring in the controller's torque-to-offset
mapping first, since that error is currently absorbed silently by the COP
feedback term.

## Regression

- EditMode: **63 / 63 passed** (unchanged).
- PlayMode after the substrate repair: **51 passed, 9 failed** — five more
  passes than before (the new `GR` suite) and the same nine failures. Two are
  headless-only (`requires a graphics device`); the rest are the real physical
  state: `G1`/`G2`/`G3`, `R3`/`R4`, `S1`, `E3`.
- GAM-6, GAM-7, GAM-9 and GAM-10 all unaffected: passive ragdoll, powered
  neutral, deterministic reset, joint sign, one-writer authority, mutation
  gates, and the GAM-10 source and root-authority gates all pass.

`AddForce`, `AddTorque`, velocity writes, `MovePosition`, `MoveRotation`,
runtime physical transform writes, kinematic pelvis, foot pins, bar parenting
and hand constraints: all absent from the GAM-11 production path. The rig
translation is initial construction, before any body exists. GAM-7 spring and
damper unchanged.

GAM-11 stays `IN_PROGRESS`. The squat was not touched.
