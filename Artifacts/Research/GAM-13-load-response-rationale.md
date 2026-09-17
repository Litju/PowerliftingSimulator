# GAM-13 — Squat load-response rationale (bounded science check)

Research date: 2026-09-16. Search: PubMed (targeted queries on the squat
load–velocity relationship, the sticking region, and near-maximal/failed
squats), plus the primary records already verified in
[`GAM-11-H20-sticking-region-literature.md`](GAM-11-H20-sticking-region-literature.md).

Purpose: **qualitative constraints only.** No human velocity threshold, %1RM
value, joint moment, or timing below is copied into the game as a constant,
detector threshold, or acceptance limit.

## Q1 — How does successful concentric velocity change with relative load?

| Supported qualitative relation | Source | Game simplification | Explicit non-claim |
|---|---|---|---|
| Mean concentric/propulsive bar velocity falls close to linearly and monotonically as relative load rises toward 1RM in the free-weight back squat. | Gantois et al. 2022, *Biol Sport* 40:201–208, 25 trained men, BSQ progressive test to 1RM, linear encoder. [DOI 10.5114/biolsport.2023.112966](https://doi.org/10.5114/biolsport.2023.112966) | Among **successful** probes, representative mean concentric bar velocity should be non-increasing with bar load and concentric duration non-decreasing, within a declared tolerance. | No velocity-per-%1RM table; the game has no measured 1RM, and the relation is individual/technique specific. |
| The relation is strong but exercise-variant and depth specific; full/parallel squats show a sticking region at high loads, half squats do not. | Martínez-Cava et al. 2019, *J Sports Sci* 37:1088–1096, 52 strength-trained males, full/parallel/half squat to 1RM. [DOI 10.1080/02640414.2018.1544187](https://doi.org/10.1080/02640414.2018.1544187) | A deep (legal-depth) squat may legitimately produce a velocity dip only at heavy loads; light loads need not show one. | No claim that the game rig reproduces human depth-specific velocity values. |

## Q2 — How is a successful sticking region distinguished from terminal failure?

| Supported qualitative relation | Source | Game simplification | Explicit non-claim |
|---|---|---|---|
| The sticking region is an event-defined interval inside a **successful** ascent: first upward velocity peak (`vmax1`) → local velocity minimum (`vmin`), followed by re-acceleration (`vmax2`) and completion. | Larsen, Kristiansen & van den Tillaar 2021, *Front Sports Act Living* 3:691459 (3-RM, 25 lifters). [DOI 10.3389/fspor.2021.691459](https://doi.org/10.3389/fspor.2021.691459) | A GAM-13 offline analyzer recognizes sticking as *ascent established → velocity rise → local reduction/minimum → later recovery → lockout* on the frozen trace. It never influences physics and is never failure truth. | Not a human event detector; no event-time or displacement norms adopted. |
| Some lifters/repetitions show no clear sticking region at all. | van den Tillaar, Andersen & Sæterbakken 2014, *J Hum Kinet* 42:63–71 (10/15 showed one). [DOI 10.2478/hukin-2014-0061](https://doi.org/10.2478/hukin-2014-0061) | Absence of a resolvable dip at light/moderate load is not a model defect. | — |
| Terminal failure is distinguished by the *outcome*: the lifter does not recover upward progress (the attempt is failed), not by a single low velocity value. At 102% 1RM lifters failed; at 90–100% they lifted with a sticking region. | Larsen et al. 2022, *Sports Biomech* 24:2856–2870, 12 resistance-trained males, 90/100/102% 1RM. [DOI 10.1080/14763141.2022.2085164](https://doi.org/10.1080/14763141.2022.2085164) | Failure is classified by P3 from *persistent* no-progress / reversal evidence under high modeled demand, never by `v < X`. Recovery (renewed upward velocity or displacement) ends a stall candidate. | No `v < X ⇒ failure` rule; P3 remains the only failure authority. |

## Q3 — Qualitative behaviour near maximal / failed attempts

| Supported qualitative relation | Source | Game simplification | Explicit non-claim |
|---|---|---|---|
| With heavier loads, bar displacement and velocity inside the sticking region decrease; the failed (supra-maximal) lift shows reduced hip/knee extension progress. | Larsen et al. 2022 (above). | Near the capacity limit, the physical trace should show a longer, slower, lower-velocity ascent segment under high modeled demand that still recovers; beyond it, upward progress should stop or reverse. | No claim about muscle activation, hip moment, or EMG; modeled demand is a command-side game quantity. |
| In elite powerlifters, increasing load from 90→100% 1RM reduced velocity in the sticking and post-sticking regions and increased forward lean late in the sticking region, while the lifts still succeeded. | Andersen et al. 2026, *J Strength Cond Res* (8 elite powerlifters, 90/95/100% 1RM). [DOI 10.1519/JSC.0000000000005532](https://doi.org/10.1519/JSC.0000000000005532) | A successful near-max game squat may be very slow and show trunk-pitch growth, and still be a physical success. | No joint-torque-ratio or trunk-angle target copied. |

## Critical-appraisal notes

- All studies are small, trained-male-dominated samples with encoder/motion
  capture measurement; they support **direction and shape** of relations, not
  transferable magnitudes (GRADE: low–moderate certainty for magnitudes,
  adequate for the qualitative monotone trend, which is consistent across
  independent studies).
- %1RM is defined relative to a measured human maximum. The game has no
  measured 1RM; its "maximum" is an emergent property of one finite capacity
  profile acting on the rigid-body plant, so relative-load framing is used only
  as an ordering concept.
- The game plant is a finite-impedance PD actuator model (PSMS-05), not a
  muscle model. Similar-looking bar-velocity shapes do not validate internal
  mechanics.

## What GAM-13 takes from this

1. Ordering, not values: successful-probe velocity non-increasing and duration
   non-decreasing with load, demand/saturation exposure generally rising.
2. Sticking is a successful-lift event pattern with later recovery; it is
   analysed offline and is not failure.
3. Supra-maximal failure must be emergent loss of upward progress (or
   reversal/collapse) from finite capacity — classified by P3 from persistent
   physical evidence, never from load or a single velocity sample.

CLAIM_CLASS: `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION` (qualitative only).
