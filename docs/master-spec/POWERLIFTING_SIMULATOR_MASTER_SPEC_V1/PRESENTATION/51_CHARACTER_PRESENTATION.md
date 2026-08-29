# Character Presentation

**Document ID:** `PSMS-PRES-51`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `04_HUMANOID_PHYSICAL_ATHLETE.md`, `PRESENTATION/50_RENDER_ARCHITECTURE.md`, `05_POWERED_JOINT_MODEL.md`

## Repository verification

- Inspect the exact Superhero_Male_FullBody mesh, materials, rig, facial options, LODs, and licenses.
- Capture physical/visible overlay video for all lift extrema and failures.
- Verify any added animation/VFX assets have compatible licenses.

## PURPOSE


Make the actual visible powerlifter look intentional, alive, strained, and readable while every load-bearing pose remains
rooted in the physical athlete.


    ## INPUTS


Interpolated physical bone poses, reference tracking error, modeled demand/fatigue, lift state, commands, failure record,
athlete cosmetic/profile data.


    ## OUTPUTS


Visible body pose, bounded hands/fingers/head/face corrections, breathing/strain layers, skin/material response, failure
reaction, and character animation events.


    ## STATE


Physical follower pose; visual rig weights; gaze/head state; hand/finger state; breathing/strain scalar; facial state;
cosmetic/material selection. All reset deterministically between attempts.


    ## UNITS


Pose m/rad; visual blend weights `[0,1]`; time s. Strain is a game/presentation scalar.


    ## COORDINATE CONVENTION


Body follows physical segment frames. Hand targets use BAR landmarks only after physical pose; head/gaze uses world
camera/referee targets with bounded neck correction.


    ## EQUATIONS


Presentation strain:

\[
S=\operatorname{smoothstep}(u_0,u_1,\max_i \bar u_i)
\,w_{drive}+w_f f_{attempt},
\]

where `u` is modeled demand and `f` is game attempt fatigue. `S` controls audio/material/facial/secondary animation only.

Visual correction bound:

\[
\|\Delta \mathbf p_{hand}\|\le p_{max},\qquad
\|\operatorname{Log}(\Delta q_{hand})\|\le\theta_{max}.
\]

Exceeding the bound exposes a physical/rig defect; it does not silently increase the correction.


    ## ASSUMPTIONS


The Quaternius asset is stylized and may not have full facial/muscle topology. Clear silhouette, bar-body relationship,
and joint continuity matter more than anatomical skin simulation.


    ## APPROXIMATIONS


Breathing, facial strain, tremor, muscle material response, fingers, and cloth are cosmetic. Hands may use visual IK because
bench/deadlift grips already carry the bar physically; squat hands are visual guides.


    ## GAME CALIBRATIONS


Visual hand correction ≤20 mm and ≤8° seed; head/gaze ≤15° from physical/reference neck; micro-tremor only at high demand,
low amplitude, never on authoritative root/bar. Breathing pauses/changes by phase for drama without altering capacity.
Failure reactions blend from the last physical snapshot and safety state.


    ## NUMERICAL IMPLEMENTATION


Apply in order: physical follower → spine/limb visual smoothing if allowed → hand/finger correction → head/gaze → face/
material. Never accumulate corrections frame-to-frame. Use event/state-driven Playables or rig weights. On mismatch above
bound, log and show raw physical pose in debug; release can clamp but qualification fails.


    ## PSEUDOCODE

    ```text
    PresentCharacter(pose, attempt):
    visible.ApplyPhysicalPose(pose)
    strain = strain_model(attempt.modeled_demand, attempt.fatigue, attempt.state)
    hands.ApplyBoundedVisualCorrection(pose.bar_grip_landmarks)
    fingers.ApplyGripCurl(attempt.grip_state)
    head.ApplyBoundedGaze(attempt.referee_or_bar_target)
    face.ApplyState(strain, attempt.failure, attempt.result)
    materials.ApplySweatAndStrain(strain)
    ```

    ## UNITY MAPPING


Visible `Animator`/PlayableGraph only for additive render layers that do not own load-bearing transforms; Animation Rigging
for hand/head/fingers; MaterialPropertyBlock for sweat/strain; blend shapes if asset supports them; no runtime mesh rewrite
required V1.


    ## FAILURE MODES


Rubber limbs; dislocated shoulders; hands float/penetrate; visual IK hides physical failure; strain before effort; face
continues after reset; visible root differs from physical; stylized asset marketed as clinical anatomy.


    ## OBSERVABILITY


Physical/visible/reference overlay; correction magnitudes; strain inputs/output; rig layer weights; bone error heatmap;
contact/hand landmarks.


    ## TELEMETRY


Max/rms visual correction by bone, strain trace, rig-layer weights, clipping flags, skinning time. Presentation data stays
separate from attempt metrics.


    ## TESTS


Bounds; no writeback; reset; light vs heavy strain; failure state; hand alignment all extrema; head/gaze bounds; screenshot/
video visual review; missing facial features degrade gracefully.


    ## MUTATION TESTS


Unlimited IK; use visual correction as physical contact; shake bar/root; strain drives capacity; Animator root motion;
carry correction across frames; hide failure by snapping pose.


    ## PERFORMANCE CONSIDERATIONS


A few bounded rig constraints and material parameters; avoid per-frame allocations and blend-shape overuse.


    ## CLAIM CLASSIFICATION


All secondary character effects: `PRESENTATION_ONLY`. Visible load-bearing pose: engine state visualization. No anatomical,
physiological, or emotional truth claim.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** physical follower, bounded hand/head/finger/strain presentation.  
**LATER:** richer face/body customization and deformation.  
**RESEARCH:** data-driven muscle/skin.  
**OUT_OF_SCOPE:** anatomical soft-tissue simulation.
