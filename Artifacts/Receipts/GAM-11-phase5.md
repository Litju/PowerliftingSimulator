# GAM-11 Phase 5 — contact-aware predictive balance

Start: `532b9a7` · Head: `67f0461` · Branch `work/gam-11-squat-physical-control`

| phase | state |
|---|---|
| 5A measurement truth | done, `a41ef7c` |
| 5B actuator contract | done, `18e4c49` |
| 5C reference mapping | done, `ec1d93e` |
| 5D static feasibility | done, verdict below |
| 5E predictive controller | implemented, `67f0461` |
| 5F ten second standing gate | **FAILS** |
| 5G squat | **not started** — gated behind 5F |

## 5A — what the support polygon actually is

Support was the midpoint of two foot rigidbody centres, which is a point.
Measured from the solver's own plantar contacts it is a real polygon:

```
contacts=8  ap=[-0.1548, 0.1353] len=0.290  ml=[-0.1793, 0.1792] width=0.359
cop=(-0.0067, 0.0000, -0.0708)  total normal impulse=11.02
mass=100.00 kg  com=(0.0009, 1.1019, -0.0134)  h=1.1019  omega=2.9837
```

COP is an impulse-weighted `ENGINE_PHYSICS_CONTACT_ESTIMATE`, not a force
plate. Capture point and omega are a `GAME_CONTROL_REDUCED_ORDER_MODEL`.

**This corrects the Phase 4 conclusion.** Against a real polygon the athlete
starts *well inside* its base of support: front margin 0.167 m, rear margin
0.123 m. The earlier "COM outside support" reading was an artefact of
measuring against a point. Phase 4's diagnosis was wrong; the fall is real
but the mechanism was not what I said it was.

## 5D — static feasibility verdict

`STATUS = BALANCE_CONTROLLER_REQUIRED`

With no bar, no balance corrections, and s_q held at 0, the COM starts inside
support and the COP sits *behind* it (−0.072 vs −0.032). A COP behind the COM
is a forward moment, so the athlete tips forward with 0.167 m of front margin
unused. The ankle then saturates trying to hold a *pose* rather than regulate
a *COM*, and the foot finally pitches to −84 deg.

## 5B — actuator contract

`capacityScale` was multiplying `positionSpring` and `positionDamper`, so a
heavier bar silently stiffened every joint. Capacity now reaches the joint
only through `maximumForce = baseCapacityNm * capacityScale * activation`,
`useAcceleration` stays false, and demand is reported against the gains
actually written. Covered by `PoweredJointActuatorContractTests`.

## 5C — reference mapping

- Trunk: canonical 10 deg arrived as 4 + 6 deg across the only two physical
  trunk segments. Participating segments are renormalised to sum to the
  canonical request, preserving the reference's 40/60 split. Verified at four
  phases (R7). GAM-10 output untouched.
- Rates: the adapter sent `Vector3.zero` target velocity while the reference
  was moving. The canonical rate is now differentiated in logical joint space
  and fed forward scaled by phase velocity.
- Depth: `rule_depth_left_m`, `rule_depth_right_m`, `legal_depth` now come
  from `SquatDepthGeometry` on the physical hip and knee anchors. Pelvis drop
  is a secondary regression metric only.

## 5E / 5F — the controller, and where it stops

The controller does what the brief specifies: desired COM acceleration →
desired COP through LIPM → clamped inside the measured polygon → bounded
joint-target offsets only. Two things were wrong in my first cut and are
worth recording because both were sign-or-gating errors, not tuning:

1. **Hip strategy sign.** I had a forward COM error *folding* the hip. That is
   right for the impulsive hip strategy but wrong for a controller that can
   only hold a bounded steady offset — it leaned the athlete further forward
   and accelerated the collapse. Steady-state hip action for a forward COM is
   hip and trunk *extension*, carrying upper body mass back over the feet.
2. **Hip gating.** Blending the hip in on shrinking capture margin turned it
   on hardest exactly when the stance was most fragile. Per Horak and Nashner
   the hip is what you reach for when the ankle has run out, so the gate is
   now the ankle bound alone.

Measured progress across the three iterations, unloaded:

| | COM ap @ tick 100 | pelvis y @ tick 100 | saturated fraction | COP excursion |
|---|---|---|---|---|
| hip sign wrong | 0.1423 | 1.0149 | 0.882 | 0.0000 |
| hip sign fixed | 0.1476 | 0.9277 | 0.004 | 0.0000 |
| + COP feedback, ankle bound 15 deg, Kd 12 | **0.0724** | **0.9948** | 0.004 | 0.0000 |

Divergence roughly halved, the COP never leaves the support polygon, and the
actuators are off their ceiling. **The athlete still falls.** The gate is
committed failing rather than relaxed.

### The specific mechanism, as measured

The controller asks for `cop_des = 0.1052` (the front interior margin) from
about tick 25 onward and holds that request. The COP the solver actually
produces only reaches roughly 0.02–0.07. By tick 100 the COM (0.0724) is
closing on the maximum admissible COP (0.1052); once the COM passes it, no
admissible COP can be ahead of the COM and the fall is unrecoverable by any
zero-step law. **The loss happens in the first second, in the window where
the COP is short of its request.**

Ankle authority is *not* the ceiling. The open-loop sweep
(`GAM11-ankle-cop-authority.csv`) shows a commanded 6 deg offset already puts
the COP at 0.105, the front of the useful polygon. The sweep is contaminated
after that step because the athlete falls during it, so treat only the first
rows as clean — but they are enough to say the ankle can reach the COP it is
asked for when the body is still upright.

What the sweep also shows is the thing I would chase next: at a *commanded*
0 deg the ankle's **actual** angle is already −9.5 deg dorsiflexed. The body
is moving before the controller has meaningful authority. Every trace shows
the pelvis dropping 1.075 → 1.024 m in the first 25 ticks, a 5 cm sag as the
drives take load, and that sag is what seeds the forward COM velocity the
controller then spends its whole budget fighting.

## Recommended next step

Do not tune the balance gains further; the evidence says they are not the
binding constraint. The candidate is the spawn transient: the accepted
standing pose is not in static equilibrium with the finite drives, so the
athlete free-falls ~5 cm before the drives load, and starts the balance
problem already moving. Two contract-legal things to test, in order:

1. Measure whether the spawn pose is in equilibrium at all — hold every joint
   at the reference with balance off and log the first 25 ticks at full rate.
2. If it is not, a bounded gravity feed-forward on the joint targets (the
   extension offset worth the quasi-static gravitational torque at each joint)
   would let the athlete hold the accepted pose instead of settling below it.
   That is still only a joint-target offset, the same contract the ankle COP
   mapping already uses.

## Regression

Full PlayMode suite after Phase 5: 44 passed, 7 failed.

- Failing by design, the real physical state: `S1` standing gate, `R3`/`R4`
  standing holds, `G1`/`G2`/`G3` squat qualification.
- Failing headless only, not regressions: `GAM10_CLOSED_CHAIN_REFERENCE_VISUAL_QUALIFICATION`
  and `G1_PHYSICAL_SUBSTRATE_QUALIFICATION_EVIDENCE`, both
  "requires a graphics device".
- Every authority gate still passes: `G4_MUTATION_GATES_REJECT_FORBIDDEN_AUTHORITY`,
  `ONE_POWERED_JOINT_WRITER_ONLY`, `POSITIVE_KNEE_FLEXION_COMMAND_HAS_CORRECT_SIGN`,
  `TARGET_ROTATION_IS_PARENT_WORLD_ROTATION_INVARIANT`, and the GAM-10 source
  and root-authority gates.

No AddForce, AddTorque, velocity write, MovePosition, MoveRotation, physical
transform write, kinematic pelvis, foot pin, bar parenting, hand constraint,
projection rescue or threshold relaxation was added.

GAM-11 stays `IN_PROGRESS`.
