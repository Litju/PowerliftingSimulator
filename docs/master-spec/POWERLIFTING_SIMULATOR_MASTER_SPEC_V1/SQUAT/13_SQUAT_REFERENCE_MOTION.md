# Squat Reference Motion

**Document ID:** `PSMS-SQ-13`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `SQUAT/11_SQUAT_BIOMECHANICS.md`, `05_POWERED_JOINT_MODEL.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Inspect and preserve any qualified reference animation clips, but remap them to this ownership contract.
- Author/calibrate key poses on the actual Quaternius asset and verify all joint-limit margins.
- Verify that root motion and Animator writes cannot reach the physical hierarchy.

## PURPOSE

    Produce a squat-only target pose and target-rate trajectory on the actual humanoid that expresses intended
descent, bottom, reversal, and ascent without moving physical transforms.

    ## INPUTS

    Squat state, `s_q`, player Yield/Drive/Brace/Balance, measured asset proportions, technique profile,
previous physical observation.

    ## OUTPUTS

    Joint target orientations/rates, desired COM/bar relation, hand visual targets, bounded balance-corrected
reference pose, phase events.

    ## STATE

    `s_q∈[0,1]`, direction, target rate, reference pose history, bottom dwell, branch-specific spline cursor,
balance offset state. `s_q=0` standing and `s_q=1` canonical legal-bottom reference; ascent decreases toward zero.

    ## UNITS

    Phase normalized; angles rad internally; target rates rad/s; positions m; time s.

    ## COORDINATE CONVENTION

    Reference bone rotations are converted explicitly into calibrated physical joint frames; world/root offsets follow the master contract.

    ## EQUATIONS


Each joint family uses a lift-authored cubic Hermite curve:

\[
q_i(s)=h_{00}(s)q_{i,0}+h_{10}(s)m_{i,0}+h_{01}(s)q_{i,1}+h_{11}(s)m_{i,1},
\]

or a piecewise extension through authored waypoints. Quaternion joints use squad/slerp with sign canonicalization.

Phase rate:

\[
\dot s_q =
\begin{cases}
+r_y\,Y\,g_{\mathrm{descent}}(s_q), & \text{DESCENT}\\
-r_d\,D\,g_{\mathrm{ascent}}(s_q), & \text{ASCENT/STICKING}\\
0, & \text{otherwise}
\end{cases}
\]

with acceleration/rate limiting. Physical success is not phase completion: `s_q` is intent and may advance only within
state gates, while realized motion may lag or stall.

Balance corrections are added in joint space after sampling and clamped separately.


    ## ASSUMPTIONS

    One canonical powerlifting-style reference is enough for V1. The legal-bottom key pose is authored
slightly deeper than the rule threshold to give a physical margin. Reference phase can pause while physical observation
catches up.

    ## APPROXIMATIONS

    Reference curves are not measured subject-specific motion. Upper-body/hand posture is authored.
Balance is a small reference offset, not optimal control. The same reference can produce different physical motion under
different loads.

    ## GAME CALIBRATIONS

    Waypoints at standing, quarter descent, near-parallel, legal bottom, early ascent, sticking, and
lockout. Target rates slower in walkout/settle; descent user-controllable within safe range; ascent Drive governs activation
and reference rate but is capped. Bottom reversal requires actual depth/velocity evidence, not `s_q==1` alone.

    ## NUMERICAL IMPLEMENTATION

    Sample reference before drive configuration at 100 Hz. Store curves as immutable data sampled into
arrays or AnimationClip poses on hidden rig. Convert bone-local reference to physical joint frames once. Never evaluate
Animator on physical skeleton. Rate-limit per-joint targets after balance correction.

    ## PSEUDOCODE

    ```text
    SampleSquatReference(state, s_q, intent, observation, h):
    s_q = update_squat_phase_intent(state, s_q, intent, h)
    pose = descent_or_ascent_curve(state).Sample(s_q)
    pose = apply_brace_profile(pose, intent.brace)
    correction = squat_balance.Compute(observation)
    pose = apply_bounded_squat_correction(pose, correction, intent.balance)
    target_rates = finite_difference_reference(pose, previous_pose, h)
    return SquatReference(s_q, pose, target_rates)
    ```

    ## UNITY MAPPING

    Reference AnimationClip/PlayableGraph or curve asset drives hidden rig; a sampler extracts local rotations.
A `SquatReferenceAdapter` maps them to joint targets. Animation Rigging may place visual hands after physics but is not
part of the reference-to-physics path.

    ## FAILURE MODES

    Bottom pose not legal on actual rig; branch discontinuity at reversal; phase continues while actual body
collapses; reference too fast; balance correction fights phase; animation root motion moves physical root; degrees/radians
or bone-frame mismatch.

    ## OBSERVABILITY

    Show reference and physical skeleton ghosts, `s_q`, branch, target/actual joint curves, balance offset, and
phase gates. Record authored curve version.

    ## TELEMETRY

    Reference phase/rate, target joint angles/rates, actual tracking error, correction, bottom/reversal/lockout
gates.

    ## TESTS

    Reference pose continuity; exact endpoint/waypoint fixtures; legal bottom on measured rig; no root transform
write; same input yields same reference at different render frame rates; branch reversal continuous; bounded corrections.

    ## MUTATION TESTS

    Drive physical Animator; root motion; time-only phase ignores input/observation; skip rate limit; reuse bench/
deadlift curve; set phase from bar position and feed back without hysteresis; bottom key pose shallow.

    ## PERFORMANCE CONSIDERATIONS

    Pre-sample immutable curves where useful; O(joints), no allocation, no IK iteration in the fixed physical loop beyond a small bounded reference chain.

    ## CLAIM CLASSIFICATION

    Reference poses are `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION`; actual motion is runtime direct.
No claim of normative optimal technique.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** one canonical squat curve, user phase intent, balance offsets. **LATER:** technique variants.
**RESEARCH:** motion-capture fitting. **OUT_OF_SCOPE:** subject-specific optimal control.
