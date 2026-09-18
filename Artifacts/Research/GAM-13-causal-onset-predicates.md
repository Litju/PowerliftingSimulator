# GAM-13 causal audit — frozen onset predicates and analysis rules

`GAM13_CAUSAL_ONSET_PREDICATES_V1`

Frozen and committed **before** any trace from
`GAM13CausalAuditTests` was executed or interpreted. Any later change to a
threshold, window or rule below must be a new version with its own commit and
must be reported alongside the V1 result, never in place of it.

## Evidence language

- `OBSERVED` — directly measured in a fresh trace from this fixture.
- `SUPPORTED_HYPOTHESIS` — consistent with discriminating evidence.
- `ESTABLISHED_CAUSE` — an isolated single-property intervention changes the
  predicted downstream failure while the relevant confounders stay fixed.

Co-occurrence and ordering alone never establish cause. An unexecuted
hypothesis is never reported as rejected.

## Sampling and timing

- Fresh `SquatPhysicalPrototype` scene per run (`LoadSceneMode.Single`),
  `SetLoad(kg)`, controller and bootstrap components disabled, 600
  authoritative 0.01 s ticks driven by `FoundationRuntime.AdvanceRenderFrame`.
- Every tick holds the production SETUP reference
  (`s_q = 0`, `SquatPhaseDirection.None`, `SquatState.SETUP`), which is the
  state the production adapter idles in before an attempt.
- One row per tick is sampled **post-physics**. Body, contact, saddle and
  joint diagnostics describe the state after step `t`. Balance-controller
  fields in the same row are the command applied **during** step `t`, computed
  pre-physics from the observation of step `t-1`.
- Solver profile is applied to all 16 athlete bodies after `SetLoad`; the bar
  must remain `12/6` or the run aborts.

## Onset rule

An event's onset is the first tick of the first run of **3 consecutive** true
samples. The canonical order sorts onsets by tick; equal ticks are reported as
ties (the string uses declaration order only as a stable tiebreak).

## Canonical events

| Event | Predicate | Threshold source |
|---|---|---|
| `DRIVE_HIGH` | max load-bearing modeled demand ≥ 0.50 | new diagnostic threshold |
| `DRIVE_SATURATION` | max load-bearing modeled demand ≥ 0.95 | `PoweredJointController.ModeledDemandSaturationThreshold` |
| `TRACKING_FAILURE` | max load-bearing \|applied target − actual\| ≥ 10° | Stage-A posture-error contract magnitude |
| `POSTURE_DEPARTURE` | canonical posture error ≥ 10° **or** \|trunk pitch\| ≥ 0.70 rad **or** pelvis y ≤ 0.90 m | Stage-A standing contract |
| `CAPTURE_DEPARTURE` | plantar support present **and** min(AP capture margin front, rear) ≤ 0.01 m | Stage-A contract on the production AABB proxy |
| `SUPPORT_LOSS` | production `HasSupport == false` (no plantar contact on either foot) | production observer |
| `SADDLE_LINEAR_LIMIT` | saddle attached **and** anchor separation / linear limit ≥ 0.95 | new occupancy view of the existing 0.05 m limit |
| `SADDLE_GROSS_FAILURE` | `SquatBarSaddle.IsBroken` (detached or separation > 0.12 m) | production saddle |
| `CONTACT_MODE_CHANGE` | per-foot plantar count differs from the first bilateral-contact reference, **or** foot slip > 0.05 m/s, **or** any bar/thorax callback, **or** any bar/athlete-body collision, **or** any non-plantar athlete body touching the platform | new diagnostic definition |

Load-bearing joints: both ankles (`*_foot`), knees (`*_shank`), hips
(`*_thigh`), `abdomen`, `thorax`.

Supplementary decompositions (reported, never used for the canonical order):
`POSTURE_JOINT_ERROR`, `POSTURE_GROSS`, `GUARD_WITHDRAWAL` (guard scale ≤ 0.50),
`CAPTURE_DEPARTURE_HULL` (exact hull capture margin ≤ 0.01 m),
`COM_OUTSIDE_HULL` (COM hull margin ≤ 0), `FOOT_LIFT`, `FOOT_SLIP`,
`PLANTAR_COUNT_CHANGE`, `BAR_THORAX_CONTACT`, `BAR_ATHLETE_CONTACT`,
`NONPLANTAR_GROUND_CONTACT`, `SADDLE_ANGULAR_LIMIT` (any axis occupancy ≥ 0.95).

Final classification uses the unchanged Stage-A standing contract over ticks
100–599 (`stage_a_pass`), so the result is comparable with
`trim-refoundation-stage-a-baseline.csv`.

## Solver isolation classification

PhysX solves each island with the maximum iteration counts of its bodies.
With the saddle attached, athlete and bar share one island, so athlete
velocity iterations 1 and 4 against the bar's 6 both give an island `28/6`.
**Prediction:** 28/1 and 28/4 are bit-identical; 28/8 is the only velocity
arm that changes the island.

- `SOLVER_INSENSITIVE`: same canonical order, every shared onset within
  ±5 ticks, same Stage-A classification.
- `SOLVER_QUANTITATIVE`: same canonical order and classification, but some
  onset moves by more than 5 ticks or a magnitude changes materially.
- `SOLVER_CAUSAL`: the canonical order of the leading events or the Stage-A
  classification changes.

Timing change alone is never classified as causal.

## Bar/back load path

- Topology is read at runtime from the live `ConfigurableJoint` and collider
  pairs, not from comments.
- Physical bar/athlete contact is measured by collision callbacks on the bar
  (all bodies) and the thorax, plus a read-only `Physics.ComputePenetration`
  / `ClosestPoint` query between the bar shaft and thorax box.
- Joint load share: bar momentum balance `m·a = m·g + F_joint + F_contact`.
  The engine sign/frame of `currentForce` is identified empirically as the
  candidate (±world, ±bar-local) that best matches `m·(a − g) − F_contact`.
  Share = vertical joint force / (vertical joint force + vertical bar/athlete
  contact force), median over ticks 10 → first of `POSTURE_DEPARTURE`,
  `SUPPORT_LOSS`, `SADDLE_GROSS_FAILURE`.
- Joint `currentForce`/`currentTorque` are engine diagnostics, never
  biological forces.

## Support geometry

Plantar contact points from the production detectors are projected to the
support plane (ML = x, AP = z). Exact signed Euclidean margins are reported for
the convex hull, the 2-D AABB, and the production AP-only AABB proxy, for COM,
impulse-weighted COP and capture point. AABB and hull **materially disagree**
when their margins differ by ≥ 0.01 m (the capture-margin contract) at the
production `CAPTURE_DEPARTURE` onset or anywhere in the pre-support-loss
window. Geometry alone never yields `SUPPORT_WRENCH_INFEASIBLE`.

## Wrench feasibility

1. Test whether reported contact impulses carry tangential components
   (tangential/normal ratio < 1e-4 ⇒ friction impulses are not reported).
2. Reconstruct the required external contact wrench from logged body
   states: `F = dP/dt − M·g`, `M_com = dL_com/dt` (central differences).
3. Validate: reconstructed vertical force within 10 % (median) of measured
   normal impulse / dt, and reconstructed COP within 0.02 m of the measured
   COP, over the quasi-static window.
4. Only if step 3 passes, test feasibility in an 8-facet linearized friction
   cone with μ = 1.0 (foot `Maximum` combine outranks platform `Average`) at
   the observed contact points.

If step 3 fails or step 1 shows friction is unreported and the reconstruction
cannot substitute for it, report `WRENCH_FEASIBILITY=NOT_OBSERVABLE` and name
the missing quantities.

## Interventions

Only after the baseline order is known, and only on the leading hypothesis:
exactly one `GAM13_CAUSAL_INTERVENTION=key=value` per run, with athlete plant,
balance controller, capacity, feed-forward and solver fixed. A component
becomes `ESTABLISHED_CAUSE` only if the intervention moves the predicted
downstream event in the predicted direction.
