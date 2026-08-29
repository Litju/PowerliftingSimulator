# Deadlift Biomechanics

**Document ID:** `PSMS-DL-31`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/30_DEADLIFT_PRODUCT_SPEC.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `07_PHYSICAL_BARBELL_AND_EQUIPMENT.md`

## Repository verification

- Verify all anatomical landmark offsets on the actual rig.
- Run deterministic synthetic geometry fixtures before physical calibration.
- Separate source-derived statements from game calibration in UI and documentation.

## PURPOSE

    Constrain conventional deadlift setup, grounded-to-free transition, bar proximity, coordinated knee/hip
extension, grip, and lockout using lift-specific mechanics.

    ## INPUTS

    Bar/floor contacts and clearance, foot support, hand grip states, hip/knee/trunk/shoulder frames, bar-to-leg
distances, front-deltoid and lockout landmarks, player brace/slack/pull/bar-close intent, phase.

    ## OUTPUTS

    Grounded/slack/floor-break/free-bar mode, setup geometry, bar proximity, knee/hip/trunk coordination,
lockout proxy, grip reserve/slip, bar drift, and lift-specific failure events.

    ## STATE

    Initial bar radius/floor height, floor-contact manifold, pretension state, first-clearance tick, bar path,
minimum shin/thigh distance, knee-pass event, hip/knee extension, lockout dwell, down-command state.

    ## UNITS

    SI throughout: m, s, kg, N·m for geometric moment proxies, rad/rad·s⁻¹, W/J only when correctly classified.

    ## COORDINATE CONVENTION

    World `+Y` up, athlete forward `+Z`, shaft `+X`. Bar-over-midfoot and leg proximity are computed in the
athlete sagittal plane. Conventional stance is the calibrated V1 setup.

    ## EQUATIONS


Floor clearance:

\[
h_{clear}=y_{plateBottom}-y_{platform}.
\]

`FLOOR_BREAK` occurs when floor contact is absent and `h_clear > m_clear` for `N_clear` consecutive ticks; no force
threshold is required.

Horizontal bar distance to the athlete midfoot reference:

\[
d_{mid}=z_{bar}-z_{midfoot}.
\]

Closest bar-to-leg proximity uses segment/capsule closest-point geometry, not mesh vertices.

A sagittal external moment proxy about hip or knee is

\[
\tilde M_j = m_b g\,|z_{bar}-z_j|,
\]

used only to explain why bar drift increases demand.

Lockout proxy requires, concurrently:

\[
|\theta_{knee,L}|<\theta_{knee,lock},\quad
|\theta_{knee,R}|<\theta_{knee,lock},
\]

\[
|\theta_{hip}|<\theta_{hip,lock},\quad
\theta_{trunk}\in[\theta_{min},\theta_{max}],
\]

and the front-deltoid proxies lie behind the vertical projection of the bar under the frozen rule convention.


    ## ASSUMPTIONS

    Conventional deadlift is modeled first. The bar begins physically on the platform. Arms transmit load
through finite grips; elbows remain near extension by reference. A close bar generally reduces external moment arms but
no single setup geometry is prescribed for all bodies.

    ## APPROXIMATIONS

    `PULL_SLACK` is preparatory game intent, not tendon/bar slack biology. No true hand force, lumbar
load, or GRF. Lockout uses joint/landmark proxies. Bar whip is render-only if used. Mixed grip is visual/content later;
V1 can use double overhand/neutralized symmetric grip for stability.

    ## GAME CALIBRATIONS

    Floor-clearance margin seed 3 mm for 2–3 ticks. Slack-ready requires grip engaged, brace above threshold,
bar vertical speed below 0.01 m/s, and bounded pre-tension buildup; it must not lift the plate clear. Bar-distance warning
>50 mm from calibrated close-path reference. Knee-pass event uses bar center crossing the knee proxy height with upward
velocity. Lockout stillness seed 0.15 s. Grip slip threshold/dwell calibrated across loads.

    ## NUMERICAL IMPLEMENTATION

    Contact-observed mode machine runs after physics. The controller may increase grip/activation during
PULL_SLACK but cannot remove floor contact or move the bar. Closest-point calculations use simple physical colliders.
Use hysteresis around floor clearance to prevent chattering. Direct bar velocity drives downward-reversal detection.

    ## PSEUDOCODE

    ```text
    ObserveDeadliftBiomechanics(snapshot):
    grounded = floor_contact_filter.Update(snapshot.bar_floor_contacts)
    clearance = plate_bottom_y(snapshot.bar) - platform_y
    floor_break = persist((not grounded) and clearance > clear_margin)

    midfoot_distance = bar.z - athlete_midfoot_reference.z
    leg_distance = closest_bar_to_shin_thigh(snapshot)
    knee_pass = upward(bar) and bar.y >= knee_proxy_height

    lockout = persist(knees_locked(snapshot) and hips_locked(snapshot)
                      and trunk_erect(snapshot)
                      and deltoids_behind_bar(snapshot)
                      and bar_still(snapshot))

    return DeadliftBiomechObservation(
        grounded, floor_break, midfoot_distance, leg_distance,
        knee_pass, lockout, grip_slip(snapshot), moment_arm_proxies())
    ```

    ## UNITY MAPPING

    Bar/platform contacts from collision observer; plate-bottom and bar center fixed BAR-frame landmarks;
physical shank/thigh collider closest points; calibrated joint/shoulder landmarks; bilateral compliant grip adapters.
No trigger disables gravity at floor break.

    ## FAILURE MODES

    Floor break inferred from intent; contact chatter; bar velocity scripted; setup judged from one universal
hip height; bar proximity based on skin; lockout from bar height only; early Down ignored; grip failure presentation
without physical coupling loss; moment proxy labeled true joint torque.

    ## OBSERVABILITY

    Draw floor clearance, floor contacts, midfoot/bar vertical lines, closest bar-to-shin/thigh segments, knee-pass
plane, lockout joint/shoulder proxies, grip anchors/slip, and bar path.

    ## TELEMETRY

    Time to floor break, slack duration, bar clearance/path/velocity, bar-midfoot and leg distances, knee-pass,
hip/knee/trunk angles, grip reserve/slip, sticking region, lockout stillness and down timing.

    ## TESTS

    Bar intent without sufficient capacity remains grounded; floor break only after contact/clearance; bar drift
increases moment proxy; valid lockout; bent knee fails; shoulders/deltoid geometry fails; downward reversal; early drop;
grip capacity mutation causes physical slip; camera invariance.

    ## MUTATION TESTS

    Set floor break on Drive input; disable gravity; set bar velocity; use bar height only for lockout; remove
hysteresis; infer contact from phase; infinite grip; accept sumo through a stance parameter.

    ## PERFORMANCE CONSIDERATIONS

    Constant contacts and closest-point tests; no mesh collision queries or inverse dynamics.

    ## CLAIM CLASSIFICATION

    Grounded/clearance/bar path: runtime direct/derived; lockout: `RULE_DERIVED_GAME_PROXY`; moment arms:
`GEOMETRIC_PROXY`; slack, internal loads, true grip force: game calibration/not observable.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** conventional setup, grounded transition, bar proximity, grip, lockout.
**LATER:** separate sumo domain, grip variants. **RESEARCH:** flexible bar and validated kinetics.
**OUT_OF_SCOPE:** biological tendon slack and spinal injury analysis.
