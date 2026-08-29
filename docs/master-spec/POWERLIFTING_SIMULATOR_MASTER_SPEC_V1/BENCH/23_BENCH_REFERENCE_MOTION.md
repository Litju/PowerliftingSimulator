# Bench Press Reference Motion

**Document ID:** `PSMS-BP-23`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `BENCH/21_BENCH_BIOMECHANICS.md`, `05_POWERED_JOINT_MODEL.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Inspect and preserve any qualified reference animation clips, but remap them to this ownership contract.
- Author/calibrate key poses on the actual Quaternius asset and verify all joint-limit margins.
- Verify that root motion and Animator writes cannot reach the physical hierarchy.

## PURPOSE

    Produce bench-only setup, descent, touch, pause, press, and lockout targets, including a physical-bar-aware
hand reference, without borrowing squat phase semantics.

    ## INPUTS

    Bench state, `s_b`, player setup/grip/yield/drive/leg-drive intent, physical bar/grip observation, athlete
proportions, contact state.

    ## OUTPUTS

    Torso/leg setup pose, bilateral shoulder/elbow/wrist targets, grip anchor targets, bar-path intent,
target velocities, and state-ready signals.

    ## STATE

    `s_b∈[0,1]`; `0` start/lockout position, `1` touch position. Descent increases; press decreases. Separate
setup/arch scalar, grip width, touch target, pause state, and bilateral correction state.

    ## UNITS

    Phase normalized; angles rad internally; target rates rad/s; positions m; time s.

    ## COORDINATE CONVENTION

    Reference bone rotations are converted explicitly into calibrated physical joint frames; world/root offsets follow the master contract.

    ## EQUATIONS


A bench bar-path reference in athlete sagittal coordinates:

\[
\mathbf p_b(s_b)=
\begin{bmatrix}
x_0\\
y(s_b)\\
z(s_b)
\end{bmatrix},
\]

with a small down-and-forward descent and up-and-back press branch. Arm joint targets are solved from authored
reference poses/short-chain IK on the hidden reference rig, not by moving the physical hands.

\[
\dot s_b =
\begin{cases}
+r_l\,Y, & \text{DESCENT}\\
-r_p\,D, & \text{PRESS/STICKING}\\
0, & \text{PAUSE/commands}
\end{cases}
\]

subject to state gates, acceleration limits, and actual-contact constraints. At touch the reference freezes; Press
authorization resumes the press branch.


    ## ASSUMPTIONS

    A bounded authored arch/setup can be represented by pelvis/thorax/leg target poses. A canonical grip
width and touch point are sufficient for V1. Physical grip compliance absorbs small mismatch between arm reference and
bar state.

    ## APPROXIMATIONS

    Reference shoulder/scapular posture is visual/rig-level; no explicit scapulothoracic DOF. Bar path
is a coaching-informed game path, not a universal optimum. Leg drive modifies setup and press stability/reference, not
bar force.

    ## GAME CALIBRATIONS

    Key poses: flat pre-setup, competition setup/arch, unrack, start, mid-descent, touch, early press,
sticking, lockout, rerack. Touch point is asset-relative and collision-verified. Grip width is within current rules after
verification. At pause, reference bar/arms hold target while physical bar must meet stillness.

    ## NUMERICAL IMPLEMENTATION

    Reference rig can use authored AnimationClips plus procedural grip-width and bar-path adjustments.
Hand targets are computed from BAR grip landmarks transformed into reference space; physical grips remain constraints.
Freeze branch on invalid touch/command. Per-side target corrections are bounded to avoid concealing one-arm failure.

    ## PSEUDOCODE

    ```text
    SampleBenchReference(state, s_b, intent, observation, h):
    setup = sample_bench_setup(intent.brace, authored_arch, foot_pose)
    if state == DESCENT:
        s_b = rate_limited_increase(s_b, intent.yield, h)
    elif state in {PRESS, STICKING}:
        s_b = rate_limited_decrease(s_b, intent.drive, h)

    branch = press_curve if state in PRESS_BRANCH else descent_curve
    bar_intent = branch.SampleBarPath(s_b)
    pose = branch.SampleBodyPose(s_b, setup)
    pose.hand_targets = grip_landmarks_from_bar_intent(bar_intent, grip_width)
    pose = apply_bounded_bilateral_player_correction(pose, intent.balance)
    return BenchReference(s_b, pose, target_rates(pose))
    ```

    ## UNITY MAPPING

    Hidden reference rig plus Playables/AnimationClip sampler; optional Animation Rigging IK only on reference/
visible chains. Physical bar state informs targets through immutable previous observation, preventing same-tick algebraic
loops.

    ## FAILURE MODES

    Reference hand targets force physical bar; touch point misses chest; pause automatically completes by
phase; setup changes during press; both arms corrected to hide asymmetry; bar path copied from squat; discontinuity at
Press command; foot/arch targets exceed joint limits.

    ## OBSERVABILITY

    Reference ghost, bar-path curve, physical bar, hand anchors, `s_b`, setup scalar, grip width, target/actual
shoulder-elbow-wrist traces and branch gates.

    ## TELEMETRY

    Setup/arch, grip width, reference/actual bar path, target joint rates, touch/pause state, bilateral tracking
error and correction.

    ## TESTS

    Start/touch/lockout key poses; touch target inside valid volume; Press freezes until command; physical bar is
not moved by reference; symmetric reference; bounded one-side correction; state branch continuity; no squat phase type.

    ## MUTATION TESTS

    Use `s_q`; root-motion bar; time-only pause; direct MovePosition bar; unlimited IK correction; change arch
during press; ignore physical previous snapshot and create same-tick loop.

    ## PERFORMANCE CONSIDERATIONS

    Pre-sample immutable curves where useful; O(joints), no allocation, no IK iteration in the fixed physical loop beyond a small bounded reference chain.

    ## CLAIM CLASSIFICATION

    Reference and path are `BIOMECHANICALLY_INFORMED_GAME_CALIBRATION`; no universal optimal path or detailed
shoulder physiology claim.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** canonical setup/path/touch/press reference. **LATER:** grip/style profiles.
**RESEARCH:** mocap/optimization. **OUT_OF_SCOPE:** validated scapular or muscle control.
