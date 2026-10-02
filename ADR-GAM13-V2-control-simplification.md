# ADR-GAM13-V2-control-simplification

**Status:** Accepted for GAM-13 V2 implementation  
**Authority:** Live GAM-13 Linear description, with `POWERLIFTING_SIMULATOR_MASTER_SPEC_V1` as product and physics authority  
**Date:** 2026-09-26

## Decision

Replace the V1 squat command architecture with one phase coordinate, one smooth GAM-10 reference family, and one bounded COM/support-center stabilizer. V1 remains historical evidence. This ADR records the issue-authorized implementation amendment; it does not edit the frozen master-spec bundle.

## V1 RETAIN

- The physical athlete, physical barbell, finite upper-back saddle, gravity, inertia, collision, contact, friction, and the single PhysX authority.
- GAM-10 visible squat geometry and its mapped joint-space reference.
- GAM-49 shared surface-landmark depth authority, rules, failure observations, and attempt lifecycle contracts.
- `PoweredJointController` as the sole joint-drive writer and its finite force-drive semantics.
- Offline bar-velocity sticking analysis, existing forensic traces, and accepted Phase-A/B1-A evidence in Git history.

## V1 REMOVE_FROM_RUNTIME

- `SquatEquilibriumPreload` command authority and every phase-by-load reference correction.
- `SquatPredictiveBalanceController` feedback, including COP inversion, capture-point feedback, posture and joint-limit guards, ankle authority switching, and hip-strategy switching.
- Production command overrides, equilibrium/load tables, and brace-driven posture bias.
- Squat-layer family capacity multipliers and authored STICKING motor behavior or timeout failure.

V1-only forensic code may remain available to historical/debug tests, but no V1 controller output may enter a V2 target or actuator command.

## OBSERVATION_ONLY

COP, capture point, anchor separation, solver diagnostics, modeled drive demand, joint-limit proximity, and authored `SquatState.STICKING` are observations or presentation data only. Physical sticking is derived offline from the measured bar-velocity trace. None may change reference phase, strength, target posture, or physical outcome.

## V2 IMPLEMENT

Use one scalar phase `s ∈ [0,1]`: `0` is standing/lockout and `1` is the canonical bottom. Yield advances `s`; Drive requests decreasing `s`. Descent and ascent traverse the same smooth GAM-10 movement family in opposite directions. No load- or sticking-conditioned trajectory is allowed.

The production target is:

```text
q_cmd = q_ref(s) + delta_q_balance
```

With athlete-plus-bar COM `c`, COM velocity `v`, plantar support center `p`, and optional normalized player ML bias `b_ml`:

```text
e_ap = c.z - p.z
e_ml = c.x - p.x
u_ap = -(KpAP * e_ap + KdAP * v.z)
u_ml = -(KpML * e_ml + KdML * v.x)
delta_q_raw = W_ap * u_ap + W_ml * u_ml + W_player * b_ml
delta_q_balance = rate_limit(clamp(delta_q_raw, -correction_bound, +correction_bound), correction_rate, dt)
```

`W_ap`, `W_ml`, `W_player`, the bounds, and rates are fixed target-space mappings from one immutable, versioned calibration. Ankle is the primary correction, hip is smaller, trunk is a small AP counter-correction, and ML correction uses ankle plus a smaller hip contribution. The player bias is optional and bounded by the same final correction limits. The stabilizer is allocation-free, receives no bar-mass or load input, and has no hidden state other than its previous bounded output for rate limiting.

The V2 stabilizer calibration has exactly 14 active scalar values: four PD gains; five fixed AP/ML joint weights; two AP/ML correction bounds; two AP/ML correction rates; and one player-ML-bias bound. No load knots, phase tables, posture offsets, or alternate strategies are active.

`PoweredJointController` remains the sole physical actuator writer. For each controlled joint:

```text
maximumForce_j = BaseCapacityNm_j * AthleteStrengthScale * Effort
```

`AthleteStrengthScale` is one load-independent scalar. `Effort` is bounded brace/drive activation. Bar mass changes the physical plant only; it never selects or scales actuator capacity.

## Exact physics invariants

- The physical athlete and one authoritative loaded bar remain dynamic rigid bodies in the authoritative Unity PhysicsScene; gravity and measured inertia remain enabled.
- PhysX owns all body motion. Foot/platform contacts, friction, collisions, and the finite upper-back saddle remain active.
- The canonical simulation step remains 0.01 s with its existing single simulation owner.
- One target writer per joint: `PoweredJointController`; force-drive semantics, fixed family spring/damper, finite `maximumForce`, and rate-limited joint targets remain in force.
- The GAM-10 squat reference and GAM-49 surface-depth provider are unchanged. Reference motion expresses intent and never writes body transforms.
- No lift Transform writes, Rigidbody velocity writes, direct forces/torques outside the joint drives, hidden support, root/foot pin, direct bar-velocity scripting, or load-threshold outcome branches.
- Rules and physical failure consume observed motion. No authored sticking state or lookup table can create an outcome.

## Joint limits

Keep every canonical GAM-10 mapped joint component at least 10 degrees from its hard physical bound at its most extreme state. The hip flexion component must have at least 20 degrees of reserve at legal bottom. Change only the smallest fixed substrate bounds needed; preserve GAM-10 angles and GAM-49 depth. Limits are never load-dependent and never create a stall or failure.

## Complexity budget

One reference family, one controller topology, one stabilizer calibration with 14 active scalar values, fixed family target weights, and no load-knot tables. The stabilizer allocates no memory per tick. The powered-joint family profiles remain the finite shared substrate contract; there are no squat-specific capacity multipliers.

## Anti-loop rule

After implementation, allow at most one fixed stabilizer tuning pass, then one fixed joint-impedance/non-binding-limit correction pass. Do not add controller layers. Do not tune strength until fresh-process standing qualification passes at 25, 60, 140, 170, and 300 kg. If ConfigurableJoint convergence remains the dominant blocker after those two bounded passes, stop and classify `CONFIGURABLEJOINT_SUBSTRATE_NOT_QUALIFIED`; evaluate an ArticulationBody substrate gate instead.

## ArticulationBody fallback condition

Use the fallback only when measured ConfigurableJoint constraint convergence is still the dominant blocker after the single stabilizer pass and the single impedance/non-binding-limit pass, with controller topology and forbidden assistance unchanged. The fallback is a substrate qualification decision, not a reason to restore V1 guards, preload tables, or load-specific authority.
