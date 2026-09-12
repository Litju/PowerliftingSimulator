# GAM-11 Phase 5H11 — thigh/abdomen contact model reconciliation

Start `2ee53cf` · Checkpoint `checkpoint/gam11-pre-thigh-abdomen-contact-reconcile`

**The contact H10 identified is a coarse rigid proxy false positive.** Both
primitives are correctly registered and neither is oversized, but a
constant-radius capsule against a flat-bottomed box cannot reproduce the taper
that lets a real thigh and a real belly slide past each other. The repair is two
qualified pairs in the existing collision policy. No dimension, mass, inertia,
registration, actuator, balance or reference value changed.

## The three claims H10 left fused

| claim | measurement | verdict |
|---|---|---|
| the proxies are in the wrong place | hip anchor 2.0 mm from the reference hip bone, abdomen anchor 2.6 mm from the spine bone, capsule axis exactly on the femur, capsule tip 4.5 mm past the hip anchor | **Class C excluded** |
| the proxies are the wrong size | thigh capsule r=0.0900 m vs skin mean radial spread 0.0929 m, max 0.1416 m; abdomen box 29.7 mm narrower and 29.6 mm shallower than the torso | **Classes A and B excluded** |
| the contact is real | visible surfaces never intersect on the reference path; 13.4 mm still open at the deepest commanded pose | **Classes F and G excluded** |

`contactOffset` (0.0100 m each, 0.0200 m summed) opens the contact 13° of hip
flexion early — 21 ticks — but load bearing begins at 64.7°, exactly where the
surfaces actually intersect. **Class D is real and measurable but not causal**,
so the offset is left alone rather than changed globally.

What survives is **Class E**.

## Contact is not overlap

Measured separately, on the same tick grid, unloaded:

```
FIRST_CONTACT_TICK              = 146   hip 53.2 deg
FIRST_GEOMETRIC_OVERLAP_TICK    = 167   hip 64.7 deg
FIRST_POSITIVE_PENETRATION_TICK = 168
FIRST_LOAD_BEARING_CONTACT_TICK = 167   hip 64.7 deg
```

The static reference-path sweep agrees within a degree without simulating
anything: proxy contact at 51.2°, penetration at 64.1°, and reaching the
commanded 111° would need **82.6 mm** of interpenetration — more than the
abdomen box's entire half-thickness.

Two penetration figures appear in this receipt and they are different
measurements, not a discrepancy:

- **82.6 mm — `PROXY_MTV_PENETRATION_RUNTIME`.** `Physics.ComputePenetration`,
  the PhysX minimum translation vector, evaluated on the *production runtime
  colliders* posed by rotating the settled physical bodies about their as-built
  joint anchors by the reference angles (T3, physical scene).
- **72.1 mm — `PROXY_SEGMENT_TO_BOX_DEPTH_REFERENCE`.** An analytic
  segment-to-oriented-box closest-point distance minus the capsule radius,
  evaluated on proxies *reconstructed from the posed reference skeleton* (V1,
  preview scene).

The second is not an MTV and systematically under-reads it once the capsule is
deeply inside the box, so 72.1 ≤ 82.6 is the expected ordering. Both are far
past the abdomen box's 80 mm half-thickness and both support the same
classification.

## The skin measurement, and the trap in it

The first version of this measurement reported a flat 4.3–5.9 mm gap at every
hip angle including standing. That is not clearance, it is the mesh's vertex
spacing at the hip crease: the skin is one continuous surface, so thigh-weighted
and torso-weighted vertices are always neighbours across the fold and the raw
minimum reports the seam no matter what the athlete is doing. Excluding a sphere
around the hip bone from both sets, with the radius swept rather than chosen
once, gives the real answer:

| hip | 0° | 40° | 51° | 65° | 80° | 95° | 111° |
|---|---|---|---|---|---|---|---|
| skin gap (r=0.16) | 53.4 mm | 39.3 | 32.5 | 33.1 | 36.2 | 12.4 | **13.4 mm** |
| proxy segment-to-box depth | 0 | 0 | 0 | 0 | 11.0 | 38.7 | **72.1 mm** |

The body walks the path with a centimetre to spare. The primitives demand seven.

## The repair

Two pairs added to `PhysicalAthleteSelfCollisionPolicy.QualifiedIgnoredPairs`
with reason `COARSE_RIGID_PROXY_FALSE_POSITIVE_IN_DEEP_SQUAT`. The exemption is
exactly `left_thigh`/`abdomen` and `right_thigh`/`abdomen`; thigh against
thorax, head, shank, foot and the opposite thigh all stay live, and the matrix
regression now asserts that.

## Verification

`T4_OLD_H10_FIRST_CAUSE_REMOVED`, unloaded, production plant:

```
thighAbdomenContactTicks = 0
worstTrackingErrorInH10Window (ticks 146-250) = 7.55 deg   (was > 20)
deepestHipTarget = -111.00 deg   deepestHipActual = -116.84 deg
```

The hip now reaches and slightly overshoots the commanded flexion. 7.55° is the
ordinary drive droop H10 measured before the block engaged.

The H10 sweep, re-run on the production policy:

| | 0 kg before | 0 kg after | 25 kg before | 25 kg after |
|---|---|---|---|---|
| final pelvis y | 0.1585 | **0.5296** | 0.1084 | **0.5773** |
| peak drive demand | 3.17 | **0.29** | 1.18 | **0.28** |
| posture breakdown | tick 250 | **never** | tick 251 | **never** |
| plantar contact lost | tick 394 | **never** | tick 365 | **never** |
| foot slip | tick 307 | **never** | tick 344 | **never** |
| direction of loss | rearward | forward | rearward | forward |

A defect found while doing this: the H10 fixture's `RunIdentification` forced
the thigh/abdomen pair **on** by default, a leftover from its counterfactual
switch. It would have silently overridden this repair in every future
measurement. It is now a tri-state that defaults to the production policy, and
only the counterfactual touches collision state.

## The new first cause

Both loads now share one mechanism, and it is the opposite direction from the
old one. The descent is clean — COM holds near −0.03 m, well inside
[−0.155, +0.135], from tick 0 to about 300 — and the athlete loses it forward at
the bottom:

| | 0 kg | 25 kg |
|---|---|---|
| ankle at dorsiflexion limit | tick 365, proximity 0.987 | tick 341, proximity 0.984 |
| capture point over the front edge | tick 366 | tick 331 |
| COM over the front edge | tick 396 | tick 351 |

`NEW_FIRST_DIVERGENCE_MECHANISM`: at the bottom the trunk keeps pitching forward
(0 kg 48°→58°, 25 kg 55°→68°) while the ankle is pinned at its dorsiflexion
limit and can no longer return the centre of pressure, so the centre of mass
runs out the front of the base of support. Load makes it earlier, which is what
a bar adding forward-tipping moment should do.

`TRUNK_PITCH_DIVERGENCE` fires earlier still (tick 309 / 194) but at 39–47° of
trunk lean, which is ordinary for a squat. That threshold was chosen in H10 for
a rearward-collapse regime and is **not** a defect at these values. It is
reported for completeness, not as the cause.

## Test status

- **MasterSpec: `STATUS=PASS`** — 68 files, `HASHES=PASS`, `DEPENDENCIES=PASS`.
- Full EditMode: 67/67 pass.
- Targeted PlayMode (collision matrix, upper-limb contracts, setup pose, ground
  registration, actuator realization, standing closed loop, shared substrate,
  powered-joint contracts): 29/29 pass.
- Behavioural PlayMode batch: 26/30, **4 failures, all pre-existing.** Verified
  by reverting only the policy and its two regressions and re-running: the same
  four fail before the change. `E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM` fails
  byte-identically either way. `G1` moves *past* its foot-lift assertion and
  fails later on lockout recovery; `G2` improves from 1.297 to 1.132 m of bar
  height error. `G3` is 105 kg and out of scope.
- **Full PlayMode sweep: 130 total, 126 pass, 4 fail** — exactly the same four,
  and no others anywhere in the suite. `Artifacts/GAM11-5h11-playmode-full.xml`.
- Physical squat gates unloaded and 25 kg: still **FAIL**, for the new forward
  cause. Not relaxed.

## Visual review

Re-captured the 25 kg motion evidence on the repaired plant and viewed every
required phase: standing (front, side, oblique), the former contact-onset region
at sq=0.56, near-parallel at sq=0.80, the deepest reached pose at sq=1.00, and
the first new divergence at sq=0.97.

**The H11 acceptance criterion passes.** There is no gross interpenetration of
the visible thigh through the torso at any captured pose; the hip fold reads as
anatomical throughout, which is the specific failure mode a pair-filtering
repair had to be checked against.

Two observations recorded rather than acted on, both belonging to the new
forward cause and not to the collision repair:

1. **At the bottom the trunk goes close to horizontal**, considerably flatter
   than a competition squat should be. This is the forward drift the
   measurements already report, seen from the side.
2. **The bar rides forward over the shoulders at the deepest and reversal
   poses** and its roll becomes visible from sq=0.56 onward. The saddle stays
   inside its limits throughout (max separation 0.0076 m, no angular exceedance
   at 25 kg), so this is the shelf angle following the trunk rather than a
   coupling failure. Before the repair the athlete was on the floor at these
   ticks, so this is not a regression — but it is a real part of the picture the
   next phase inherits.

Owner visual review is still required.

## Not done here

105 kg was not tested or repaired. H10 found its setup invalid before
`StartSquat` — abdomen 34.7° off target, saddle pinned at its linear limit under
3500 N — and that needs its own static-load-setup mission.

## Evidence

- `Artifacts/Measurements/GAM-11/GAM11-5h11-t1-collider-semantics.txt`
- `Artifacts/Measurements/GAM-11/GAM11-5h11-t2-contact-vs-penetration.{txt,csv}`
- `Artifacts/Measurements/GAM-11/GAM11-5h11-t3-reference-path-sweep.{txt,csv}`
- `Artifacts/Measurements/GAM-11/GAM11-5h11-t4-first-cause-removed.{txt,csv}`
- `Artifacts/Measurements/GAM-11/GAM11-5h11-v1-visible-envelope.{txt,csv}`
- `Artifacts/Evidence/GAM-11/thigh-abdomen/` — skin, proxy and combined frames
  at seven hip angles
- `Artifacts/Evidence/GAM-11/squat-motion/phys_25kg_*` — post-repair motion

## Status

`PASS_WITH_LIMITATIONS` — the contact model is classified, the smallest
justified repair is applied and verified, and the old first cause is gone
without a new artifact. The squat still does not complete, for a new and
separately identified forward-balance cause. Four pre-existing test failures
remain, none introduced here.
