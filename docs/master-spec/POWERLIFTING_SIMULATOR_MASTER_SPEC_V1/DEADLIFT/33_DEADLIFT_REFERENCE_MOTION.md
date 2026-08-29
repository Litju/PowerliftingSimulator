# Deadlift Reference Motion

**Document ID:** `PSMS-DL-33`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `DEADLIFT/31_DEADLIFT_BIOMECHANICS.md`, `05_POWERED_JOINT_MODEL.md`, `08_INPUT_AND_PLAYER_INTENT.md`

## Repository verification

- Inspect and preserve any qualified reference animation clips, but remap them to this ownership contract.
- Author/calibrate key poses on the actual Quaternius asset and verify all joint-limit margins.
- Verify that root motion and Animator writes cannot reach the physical hierarchy.

## PURPOSE

    Produce a conventional deadlift setup-to-lockout reference with a separate grounded/pre-tension stage,
close-bar intent, and post-command descent.

    ## INPUTS

    Deadlift state, `s_d`, brace/slack/drive/bar-close intent, physical grounded/bar/grip observation, athlete
proportions.

    ## OUTPUTS

    Conventional setup, pre-tension, knee/hip/trunk/shoulder targets, hand grip targets, bar-close reference,
target rates and phase-ready signals.

    ## STATE

    `s_d∈[0,1]`: `0` floor setup and `1` lockout; it increases during pull and decreases during controlled
descent. Separate setup and slack scalars exist before floor break. Knee-pass and hip-extension branches are explicit.

    ## UNITS

    Phase normalized; angles rad internally; target rates rad/s; positions m; time s.

    ## COORDINATE CONVENTION

    Reference bone rotations are converted explicitly into calibrated physical joint frames; world/root offsets follow the master contract.

    ## EQUATIONS


Piecewise reference:

\[
\mathbf q_{ref}(s_d)=
\begin{cases}
\mathbf q_{floor\rightarrow knee}(s_d), & 0\le s_d<s_k\\
\mathbf q_{knee\rightarrow lockout}(s_d), & s_k\le s_d\le1.
\end{cases}
\]

\[
\dot s_d =
\begin{cases}
0, & \text{SETUP/GRIP/BRACE/PULL\_SLACK}\\
+r_p D\,g(s_d), & \text{PULL through LOCKOUT}\\
-r_{\downarrow}Y, & \text{DESCENT after DOWN}\\
0, & \text{otherwise}.
\end{cases}
\]

Before floor break, phase intent may rise only to a small preload cap `s_preload`; the reference cannot advance the bar
through the floor. Once physical clearance is observed, the free-bar branch is enabled.


    ## ASSUMPTIONS

    A conventional setup profile parameterized from measured limb lengths is sufficient. The desired bar
path remains close and approximately vertical, while real motion may drift. Arms remain near extended.

    ## APPROXIMATIONS

    Hip height and torso angle are not universal prescriptions; they are authored to this rig/profile.
Pull-slack pose is a small coordinated preload. No tendon/bar slack or spinal mechanics are modeled. Bar-close correction
changes posture/reference, not bar force.

    ## GAME CALIBRATIONS

    Key poses: standing approach, grip/setup, brace, preload, floor break, mid-shin, below knee, knee pass,
sticking, hip through, lockout, controlled return. `s_k` is tied to reference knee-pass, while the state transition uses
physical bar/knee observation. Preload target must not create floor clearance.

    ## NUMERICAL IMPLEMENTATION

    Sample with piecewise Hermite/quaternion curves. Use previous physical bar position for bounded close-bar
reference correction. Keep elbow target near extension and never use elbow drive as primary lift power. Freeze upward
phase on no-drive or rule/failure state, but physical body remains free.

    ## PSEUDOCODE

    ```text
    SampleDeadliftReference(state, s_d, slack, intent, observation, h):
    if state == PULL_SLACK:
        slack = ramp(slack, intent.grip, h)
        pose = preload_curve.Sample(slack)
        s_d = min(s_d, preload_phase_cap)
    elif state in PULL_BRANCH:
        s_d = rate_limited_increase(s_d, intent.drive, h)
        pose = pull_curve.Sample(s_d)
    elif state == DESCENT:
        s_d = rate_limited_decrease(s_d, intent.yield, h)
        pose = descent_curve.Sample(s_d)
    else:
        pose = setup_or_lockout_pose(state)

    pose = apply_bounded_bar_close_reference(
        pose, observation.bar_distance, intent.balance)
    pose.elbow_targets = near_extension
    return DeadliftReference(s_d, slack, pose, target_rates(pose))
    ```

    ## UNITY MAPPING

    Hidden deadlift reference rig/curve asset. Grip target landmarks derive from bar geometry. Previous snapshot
only is used for correction. Physical arms/bar are never transform-driven. Separate deadlift controller/asset types.

    ## FAILURE MODES

    Preload lifts bar by target enforcement; elbow curl becomes power source; phase advances from input before
floor break; setup ignores limb lengths; bar-close correction teleports hands/bar; sumo embedded as parameter; branch
discontinuity at knee.

    ## OBSERVABILITY

    Reference/physical ghosts, `s_d`, slack scalar, piecewise branch, bar-close error/correction, elbow extension,
key pose and floor-break gates.

    ## TELEMETRY

    Reference phase/rate/slack, target joint curves, physical tracking, bar-close correction, floor-break and
knee-pass branch timing.

    ## TESTS

    Preload does not clear floor; physical floor break gates free branch; continuous at `s_k`; elbow target remains
bounded; descent requires Down; no direct bar write; conventional geometry scales with measured limbs; no squat type reuse.

    ## MUTATION TESTS

    Advance phase solely from Drive; directly raise bar at preload; reuse squat curve; elbow curl; unlimited
bar-close correction; add sumo flag; root motion.

    ## PERFORMANCE CONSIDERATIONS

    Pre-sample immutable curves where useful; O(joints), no allocation, no IK iteration in the fixed physical loop beyond a small bounded reference chain.

    ## CLAIM CLASSIFICATION

    Reference/setup/slack are game calibrations informed by conventional technique. No normative technique,
tendon slack, internal loading, or universal hip-height claim.

    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE

    **SHIP_V1:** conventional setup/preload/pull/descent reference. **LATER:** separate sumo and grip profiles.
**RESEARCH:** measured motion fitting. **OUT_OF_SCOPE:** biological optimization.
