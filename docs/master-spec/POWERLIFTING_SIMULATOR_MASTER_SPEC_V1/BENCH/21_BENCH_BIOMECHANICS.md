# Bench Press Biomechanics

**Document ID:** `PSMS-BP-21`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/20_BENCH_PRODUCT_SPEC.md`, `04_HUMANOID_PHYSICAL_ATHLETE.md`, `07_PHYSICAL_BARBELL_AND_EQUIPMENT.md`

## Repository verification

- Verify all anatomical landmark offsets on the actual rig.
- Run deterministic synthetic geometry fixtures before physical calibration.
- Separate source-derived statements from game calibration in UI and documentation.

## PURPOSE

    Constrain bench-specific setup, bar path, touch/pause, bilateral press mechanics, and contact legality
without pretending that a simplified humanoid reproduces detailed shoulder/scapular physiology.

    ## INPUTS

    Bar and hand states, shoulder/elbow/wrist frames, chest/abdomen touch volume, bench/head/upper-back/glute/foot
contacts, authored arch, player leg-drive/balance intent, phase.

    ## OUTPUTS

    Bar-to-joint moment-arm proxies, bilateral elbow-depth rule proxy, valid touch/pause state, symmetry,
wrist stacking, contact stability, and bounded bench-specific reference corrections.

    ## STATE

    Setup contact set, grip width, arch magnitude, bar path history, chest contact dwell, bar-still dwell,
bilateral elbow/shoulder positions, press symmetry, and sticking interval.

    ## UNITS

    SI throughout: m, s, kg, N·m for geometric moment proxies, rad/rad·s⁻¹, W/J only when correctly classified.

    ## COORDINATE CONVENTION

    Bench longitudinal body axis is approximately world `Z`; bar shaft is `X`. Bar vertical is `Y`. Bar path
is analyzed in the athlete sagittal plane (`Y-Z`). Left/right symmetry is `X` about the authored body midline.

    ## EQUATIONS


For each arm, a geometric external moment proxy about shoulder/elbow is

\[
\tilde M_j = m_b g\,d_{b,j}/2,
\]

with `d` the sagittal or perpendicular distance from joint proxy to the bar line of action; the equal load split is only
a neutral reference. Actual imbalance is represented by hand/bar geometry and tracking.

Grip width:

\[
w_g=\|\mathbf p_{hand,R}-\mathbf p_{hand,L}\|_X.
\]

Bar symmetry/tilt:

\[
\Delta y_{bar}=y_{end,R}-y_{end,L},\qquad
\Delta v_{hand}=v_{hand,R,y}-v_{hand,L,y}.
\]

Current-rule elbow-depth proxy at touch for each side:

\[
e_s=y_{elbow,s}-y_{shoulderTop,s}.
\]

Legal bottom requires `e_L <= m_elbow` and `e_R <= m_elbow` under the calibrated convention, a valid bar-chest/abdomen
contact, and bar stillness:

\[
\|\mathbf v_b\|<v_{pause},\quad \|\boldsymbol\omega_b\|<\omega_{pause}
\]

for a referee-controlled dwell.


    ## ASSUMPTIONS

    The bench and floor provide external support. Head/upper back/shoulder region and glutes remain in
contact under the game rule model. A bounded authored arch is a setup posture. Arms share the bar through compliant
grips; asymmetry may emerge physically.

    ## APPROXIMATIONS

    No scapulothoracic articulation, pectoral/triceps muscle force, joint contact force, or injury risk.
The chest is a calibrated physical/trigger contact volume. Elbow-vs-shoulder depth uses stable proxies, not deforming skin.
Equal half-load moment estimates are explanatory only.

    ## GAME CALIBRATIONS

    Grip constrained within official-style ring/width geometry after rule verification. Touch speed must
be below a calibrated control threshold; pause stillness seed `|v|<0.02 m/s`, `|ω|<0.08 rad/s` for at least 0.20 s before
the referee can issue Press. Contact loss persistence 2 ticks. Bilateral bar-end height difference warning 15 mm and
physical imbalance failure threshold/dwell calibrated from heavy attempts. Arch range is authored and visually gated.

    ## NUMERICAL IMPLEMENTATION

    Use direct bar Rigidbody velocity for pause. Contact events identify chest/bench/feet; rule state uses
persistence and hysteresis. Symmetry is evaluated in athlete-local coordinates. The biomechanics observer never sets bar
pose or applies corrective force. Leg-drive input modifies reference setup/trunk/foot pressure intent and capacity mapping.

    ## PSEUDOCODE

    ```text
    ObserveBenchBiomechanics(snapshot):
    touch = chest_contact_filter.Update(snapshot.bar_chest_contacts)
    still = magnitude(bar.velocity) < pause_v and \
            magnitude(bar.angular_velocity) < pause_w
    pause_ready = touch and persist(still, pause_ticks)

    elbow_L = elbow_proxy_L.y - shoulder_top_L.y
    elbow_R = elbow_proxy_R.y - shoulder_top_R.y
    elbow_depth_ok = elbow_L <= elbow_margin and elbow_R <= elbow_margin

    tilt = bar_end_R.y - bar_end_L.y
    contact_ok = required_bench_and_foot_contacts(snapshot)
    return BenchBiomechObservation(
        touch, pause_ready, elbow_depth_ok, tilt, contact_ok,
        shoulder_elbow_moment_arm_proxies(snapshot))
    ```

    ## UNITY MAPPING

    Physical bench collider and contact observers; bar/chest contact layer; calibrated shoulder/elbow landmarks;
hand grip adapter anchors; visible hand IK after physics. The rule observer reads these outputs; no collider trigger alone
moves phase.

    ## FAILURE MODES

    Touch volume misplaced; elbows evaluated in screen coordinates; one side passes and average accepted;
pause from phase timer without stillness; butt/foot contact checked visually; grips rigidly overconstrained; leg drive as
hidden bar force; shoulder moment proxy labeled biological torque.

    ## OBSERVABILITY

    Draw bar path and endpoints, hand anchors/compliance, shoulder/elbow/wrist proxies, chest touch volume,
contact patches, arch bounds, required support contacts, tilt and symmetry traces.

    ## TELEMETRY

    Grip width, setup contacts, touch time/location, pause stillness, elbow-depth per side, bar path/velocity,
bar tilt, hand slip, shoulder/elbow angle proxies, sticking interval, contact violations.

    ## TESTS

    Valid symmetric touch; no chest contact; one elbow above threshold; moving bar never becomes pause-ready;
camera invariance; removed glute/foot contact violation; asymmetric hand drive produces tilt; small grip compliance does
not alter legal touch truth.

    ## MUTATION TESTS

    Use elapsed time only for pause; accept either elbow; teleport bar to chest; infer butt contact from animation;
apply leg-drive AddForce; lock both grips with zero compliance/projection; use hand IK position as physical contact.

    ## PERFORMANCE CONSIDERATIONS

    Constant-sized contact/landmark set; use direct Rigidbody state; no raycasts over the whole scene.

    ## CLAIM CLASSIFICATION

    Bar/contact/kinematics: runtime direct; elbow-depth and touch: `RULE_DERIVED_GAME_PROXY`; moment arms:
`GEOMETRIC_PROXY`; detailed shoulder/scapular/muscle forces: `NOT_OBSERVABLE`.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** setup contacts, touch/pause, bilateral elbow-depth, path/symmetry proxies.
**LATER:** legal grip variants and athlete morphology calibration. **RESEARCH:** detailed shoulder model.
**OUT_OF_SCOPE:** clinical shoulder safety and muscle recruitment.
