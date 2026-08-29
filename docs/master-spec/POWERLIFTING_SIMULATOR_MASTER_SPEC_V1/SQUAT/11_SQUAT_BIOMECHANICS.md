# Squat Biomechanics

**Document ID:** `PSMS-SQ-11`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/10_SQUAT_PRODUCT_SPEC.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `07_PHYSICAL_BARBELL_AND_EQUIPMENT.md`

## Repository verification

- Verify all anatomical landmark offsets on the actual rig.
- Run deterministic synthetic geometry fixtures before physical calibration.
- Separate source-derived statements from game calibration in UI and documentation.

## PURPOSE

    Constrain squat reference motion, balance, legal depth, and load-dependent failure using defensible
multisegment mechanics while allowing legitimate powerlifting technique variation.

    ## INPUTS

    Measured segment mass/COM, bar mass/pose, foot support polygons, hip/knee/ankle/trunk orientations and
velocities, bilateral hip-crease and knee-top landmarks, player balance intent, phase state.

    ## OUTPUTS

    Athlete-plus-bar COM, COM projection relative to support, sagittal bar/segment moment arms, legal-depth
observation, posture/coordination diagnostics, bounded reference corrections, and biomechanical event flags.

    ## STATE

    Current and previous COM, support polygon, bar path, landmark heights, scalar joint summaries, trunk angle,
phase extrema, and persistence windows for bottom/reversal/sticking.

    ## UNITS

    SI throughout: m, s, kg, N·m for geometric moment proxies, rad/rad·s⁻¹, W/J only when correctly classified.

    ## COORDINATE CONVENTION

    Sagittal analysis uses world `Y-Z`; anterior is `+Z`. Frontal analysis uses `X-Y`. Moment arms are signed
perpendicular distances in a declared plane, not unsigned screen pixels. Joint scalar positives follow the master contract.

    ## EQUATIONS


Athlete-plus-bar COM:

\[
\mathbf c_{sys}=\frac{m_b\mathbf c_b+\sum_i m_i\mathbf c_i}{m_b+\sum_i m_i}.
\]

In a sagittal quasi-static explanatory snapshot, external gravitational moment about joint center `j`:

\[
M_{j,g}\approx m_b g\,d_{b,j}+\sum_{i\in superior(j)}m_i g\,d_{i,j},
\]

where signed `d` is the horizontal (`Z`) moment arm. This is not runtime inverse dynamics.

COM support margin is signed distance from projected COM to the nearest edge of the convex union of active foot
support polygons. A negative margin means outside support.

Depth score for each side:

\[
d_L=y_{hip,L}-y_{kneeTop,L},\quad d_R=y_{hip,R}-y_{kneeTop,R}.
\]

The conservative legal-depth condition is

\[
\max(d_L,d_R)\le -m_{depth}
\]

for `N_depth` consecutive ticks during the bottom window.

A bounded AP reference correction is

\[
\delta = \operatorname{clip}(K_p(c_z-c_z^\*)+K_d\dot c_z,\,-\delta_{max},\delta_{max}),
\]

then distributed to ankle, hip, and trunk target offsets. It never applies force directly.


    ## ASSUMPTIONS

    The squat can be represented by rigid segments, bilateral feet, and an upper-back bar load. The
system COM and joint geometry constrain credible motion, but no single torso angle, stance, or knee-travel pattern is
universally correct. Feet remain the base of support until physical failure.

    ## APPROXIMATIONS

    Moment-arm displays are geometric demand indicators, not measured net joint moments. COM is
computed from model segment parameters. No true COP/GRF is claimed. Depth landmarks are virtual bone-attached proxies.
Frontal balance is a bounded reference correction rather than a complete 3D balance controller.

    ## GAME CALIBRATIONS

    Depth margin seed `m_depth=0.005 m`; persistence 3 ticks. Support warning at 20 mm inside edge,
physical balance-risk event when projected COM remains outside by >10 mm or foot contact degrades. Sticking detection
is data-driven from loaded bar velocity after reversal; no fixed universal joint-angle region. One small phase capacity
modifier (≤10%) is allowed only if load sweeps cannot produce a stable, credible grind otherwise.

    ## NUMERICAL IMPLEMENTATION

    Compute COM and landmarks after PhysX. Build foot polygons from contact-capable foot collider footprints
projected on platform; require active contact. Use robust half-space distance with named physics/rule tolerances. Low-pass
only analysis velocity; the controller consumes engine velocities and previous COM directly. Track minima/maxima by tick.

    ## PSEUDOCODE

    ```text
    ObserveSquatBiomechanics(snapshot):
    support = union_active_foot_polygons(snapshot.contacts)
    com = mass_weighted_com(snapshot.athlete_segments, snapshot.bar)
    margin = signed_support_margin(project_xz(com), support)

    left_depth = hip_crease_L.y - knee_top_L.y
    right_depth = hip_crease_R.y - knee_top_R.y
    depth_legal = persist(max(left_depth, right_depth) <= -depth_margin)

    ap_error = com.z - reference_com_z(snapshot.phase)
    correction = clamp(Kp*ap_error + Kd*com_velocity.z,
                       -max_correction, max_correction)
    return SquatBiomechObservation(com, margin, left_depth, right_depth,
                                   depth_legal, correction, moment_arm_proxies())
    ```

    ## UNITY MAPPING

    Landmarks are calibrated `Transform` offsets on pelvis/thigh/knee-related physical frames. COM uses
`Rigidbody.worldCenterOfMass`. Contacts come from the athlete/platform collision observer. Reference corrections modify
the squat target pose record before `PoweredAthlete.SetTargets`.

    ## FAILURE MODES

    Wrong landmark placement; using knee angle as depth; COM without bar; support polygon surviving after
foot loss; sign reversal; correction too large and becoming hidden control; moment proxy labeled true moment; depth
flicker; one hip legal and the other high but average accepted.

    ## OBSERVABILITY

    Draw COM, projected point, support polygon/margin, bar vertical line, hip/knee landmarks, moment-arm lines,
trunk angle, depth values, and correction distribution. Every bottom/rule event stores the exact landmark positions.

    ## TELEMETRY

    Depth per side and minimum margin, bar/COM displacement/velocity, ankle/knee/hip/trunk scalar kinematics,
moment-arm proxies, contact state, correction magnitude, bottom/reversal/sticking timestamps.

    ## TESTS

    Known synthetic landmark fixtures pass/fail depth; unilateral-high case fails; adding bar mass shifts system
COM; removing one foot changes support; sign fixtures for forward/backward correction; identical poses under camera
changes give identical result; moment-arm proxy zero when line of action crosses joint.

    ## MUTATION TESTS

    Average left/right depth; use knee angle; omit bar mass; treat COP as COM; apply correction with AddForce;
accept depth for one tick; keep support after contact loss; reverse AP sign.

    ## PERFORMANCE CONSIDERATIONS

    O(segments + contacts). Cache segment masses and landmark offsets. Convex support polygon has very few points.

    ## CLAIM CLASSIFICATION

    COM and geometry: `ENGINEERING_DERIVED`; anthropometric inputs: source/engineering-derived; depth:
`RULE_DERIVED_GAME_PROXY`; moment arms: `GEOMETRIC_PROXY`; GRF/COP and true joint moments: `NOT_OBSERVABLE`.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** COM/support geometry, bilateral depth, moment-arm proxies, bounded AP/ML reference correction.
**LATER:** technique profiles and calibrated stance/bar-placement variants. **RESEARCH:** inverse dynamics/force plates.
**OUT_OF_SCOPE:** prescriptive universal squat and clinical loading.
