# Humanoid Physical Athlete

**Document ID:** `PSMS-04`  
**Authority:** `MASTER_SPEC_V1`  
**Status:** `FROZEN`  
**Dependencies:** `02_GAME_ARCHITECTURE.md`, `03_COORDINATES_UNITS_NUMERICS.md`

## Repository verification

- Inspect the actual Quaternius hierarchy, Humanoid Avatar mapping, bind pose, skin weights, and bone names.
- Re-measure every segment length and joint center from the imported asset; historical dimensions are hints only.
- Verify exact downloaded Quaternius package/version and its license file before shipping.

## PURPOSE


Create one anthropometrically informed powered rigid-body athlete that drives the actual visible
`Superhero_Male_FullBody` character without allowing animation to overwrite physical truth.


    ## INPUTS


- Imported humanoid transform hierarchy and calibrated neutral pose.
- Athlete body mass and authored height/scale.
- Winter-inspired segment mass fractions.
- Joint-family configuration, collider recipes, and reference-rig target orientations.
- Lift-specific capacity and control commands.


    ## OUTPUTS


- Physical segments, colliders, rigid bodies, inertia settings, and ConfigurableJoints.
- Stable mappings among reference, physical, and visible skeletons.
- Total/system COM and calibrated anatomical landmarks.
- An immutable athlete observation after every physics step.


    ## STATE


Three-rig architecture:

1. `ReferenceRig`: hidden, Animator-owned, samples authored intent.
2. `PhysicalRig`: hidden rigid bodies and joints; sole physical authority.
3. `VisibleRig`: skinned visible character; follows recorded physical bone poses for rendering.

The athlete root state, per-segment rigid-body states, bind offsets, landmark offsets, and joint calibration
quaternions are versioned authoring assets. During active physics, no Animator or IK component may write
`PhysicalRig` transforms.


    ## UNITS


Segment geometry in m; mass in kg; inertia in kg·m²; joint limits and internal orientations in rad;
authoring/debug angles may be degrees with explicit conversion.


    ## COORDINATE CONVENTION


Uses the canonical `W`, `B_i`, `J_i`, `R_i`, and `M` frames. Bone-to-body and body-to-visible offsets are
calibrated once from the actual asset. The physical body origin is at the configured rigid-body COM, which
need not coincide with the bone pivot.


    ## EQUATIONS


For athlete mass `M`, source fraction `f_i`, and redistribution correction `r_i`:

\[
m_i = M\,\frac{f_i r_i}{\sum_j f_j r_j}.
\]

For source-compatible segment COM fraction `d_i` along the measured proximal-to-distal axis:

\[
\mathbf c_i = \mathbf p_{\text{prox}} + d_i
(\mathbf p_{\text{dist}}-\mathbf p_{\text{prox}}).
\]

A primitive-derived inertia tensor is used as the runtime seed; for a capsule-like segment it is computed
from measured dimensions and mass, then assigned in the body's calibrated principal frame. The parallel-axis
theorem is used only when moving a known inertia from one point to another:

\[
\mathbf I_P=\mathbf I_C+m\left(\|\mathbf d\|^2\mathbf 1-\mathbf d\mathbf d^T\right).
\]

Whole-athlete COM:

\[
\mathbf c_A = \frac{1}{M}\sum_i m_i\mathbf c_i.
\]


    ## ASSUMPTIONS


- A 16-segment Winter-inspired partition is adequate for game-level dynamics.
- The visible mesh can tolerate a hidden physical proxy with measured bind offsets.
- Rigid segments and simplified joints are sufficient to create recognizable powerlifting.
- Skin deformation is presentation, not anatomical truth.


    ## APPROXIMATIONS


The source fractions are inspirations, not direct truth when the physical segmentation differs. V1 fractions:

| Segment | Fraction |
|---|---:|
| pelvis | 0.142 |
| abdomen | 0.139 |
| thorax | 0.216 |
| head + neck | 0.081 |
| each upper arm | 0.028 |
| each forearm | 0.016 |
| each hand | 0.006 |
| each thigh | 0.100 |
| each shank | 0.0465 |
| each foot | 0.0145 |

If Unity's skeleton combines or splits a region differently, mass is redistributed within the affected source
group and marked `ENGINEERING_DERIVED`. No skin vertex is used as a stable rule landmark.


    ## GAME CALIBRATIONS


- Historical asset measurements (`shank ≈ 0.447 m`, `thigh ≈ 0.421 m`, `foot ≈ 0.271 m`,
  `stance ≈ 0.112 m`) are nonbinding regression hints.
- Collider radius is initially 25–40% of local segment width and then fitted visually/physically.
- Adjacent self-collisions are disabled by explicit layer/pair policy; nonadjacent collisions are enabled only
  where they add gameplay value without solver instability.
- Total assigned mass must match athlete body mass within `1e-4 kg`.
- Inertia tensor values must remain positive and finite; no zero-inertia axis.


    ## NUMERICAL IMPLEMENTATION


Build from proximal to distal after the asset is in a canonical calibration pose. Capture all reference offsets,
then instantiate bodies/colliders and joints. Set mass and center of mass before applying inertia. Prefer simple
capsules/boxes/spheres, not mesh colliders. Validate initial overlap independently from controller tuning.
The render follower consumes post-physics snapshots in `LateUpdate` and applies the recorded physical pose plus
fixed bind offsets. It never writes back.


    ## PSEUDOCODE

    ```text
    BindHumanoid(asset, athlete_profile):
    bones = humanoid_mapper.ResolveRequiredBones(asset)
    measurements = measure_bone_centers_and_lengths(bones)
    source_segments = winter_partition(athlete_profile.body_mass)
    physical_segments = redistribute_to_runtime_segments(source_segments, measurements)
    assert sum(mass(physical_segments)) == athlete_profile.body_mass

    for segment in proximal_to_distal(physical_segments):
        body = create_rigidbody(segment)
        collider = fit_primitive_collider(segment, measurements)
        configure_mass_com_inertia(body, segment)
        save_bind_offsets(reference_bone, body, visible_bone)

    for joint in joint_graph:
        create_configurable_joint(joint)
        calibrate_joint_frame_and_zero(joint)

    validate_no_forbidden_overlap()
    validate_all_required_bones_and_landmarks()

LateRender(snapshot):
    for mapped_bone in visible_map:
        visible_bone.pose = snapshot.physical_pose * mapped_bone.bind_offset
    ```

    ## UNITY MAPPING


- Required `HumanBodyBones`: Hips, Spine, Chest, optional UpperChest, Head, bilateral UpperLeg/LowerLeg/Foot,
  UpperArm/LowerArm/Hand.
- `Animator` exists only on `ReferenceRig` during an active attempt.
- `Rigidbody`, primitive `Collider`, and `ConfigurableJoint` components exist only on `PhysicalRig`.
- `SkinnedMeshRenderer` remains on the visible hierarchy.
- Animation Rigging is allowed only after physical pose application and only for non-authoritative visual
  corrections such as finger contact or tiny hand alignment.


    ## FAILURE MODES


Missing/duplicate bones; bad Avatar mapping; wrong scale; collider overlap at spawn; joint anchors not coincident;
inertia axis misalignment; mass sum drift; double render ownership; visible mesh lag; nonadjacent self-collision
explosion; spine joint limit hit; left/right mirror error; physical success with visibly broken anatomy.


    ## OBSERVABILITY


Authoring validator prints a segment table: bone mapping, length, mass, COM, collider dimensions, inertia, parent,
joint axis, limits, and source class. Debug view draws physical shapes over the visible mesh and can isolate one
segment/joint at a time. Initial overlap reports include collider pair, penetration direction, and depth.


    ## TELEMETRY


Normal: total mass, root/COM pose, critical joint angles.  
Diagnostic: all segment poses/velocities, joint errors, joint-limit proximity, initial and active overlap pairs.  
Research: complete rigid-body state and force/contact exports.


    ## TESTS


- All required bones resolve once.
- Physical mass sums exactly to profile mass.
- Left/right mirrored measurements are within configured asset tolerance.
- Each joint anchor coincides in world space at bind.
- Every inertia tensor is finite/positive.
- With drives disabled the body falls; no hidden support.
- Animator mutation cannot move a physical body.
- Visible follower matches physical landmarks within visual tolerance.
- Initial state contains no forbidden penetration.
- Known neutral pose yields expected scalar joint signs.


    ## MUTATION TESTS


Enable Animator on the physical hierarchy; set pelvis kinematic; zero a segment mass; swap thigh/shank bodies;
misplace a joint anchor; reuse reference transforms as physical transforms; enable all self-collisions; omit a hand;
apply one Winter source fraction to a differently defined combined segment without redistribution.


    ## PERFORMANCE CONSIDERATIONS


Approximately 16 bodies and a bounded number of primitive colliders are inexpensive relative to scene rendering.
Cache mappings and offsets; avoid hierarchy lookups per tick; preallocate observation arrays; do not rebuild the rig
during an attempt.


    ## CLAIM CLASSIFICATION


Segment fractions and COM/inertia method: `SOURCE_DERIVED` where definitions match, otherwise
`ENGINEERING_DERIVED`. Actual asset measurements: `SOURCE_DIRECT` project measurement after Luna verifies.
Visible motion: `ENGINE_RUNTIME_OBSERVATION`. No claim of anatomical joint centers, muscle forces, tissue loads,
or clinical validity.


    ## SHIP_V1 / LATER / RESEARCH / OUT_OF_SCOPE


**SHIP_V1:** three-rig architecture, measured physical proxy, 16-segment mass mapping, simple colliders.  
**LATER:** athlete body-shape presets and validated alternate proportions.  
**RESEARCH:** muscle-actuated or deformable-body representations.  
**OUT_OF_SCOPE:** clinical musculoskeletal anatomy, soft-tissue dynamics, injury prediction.
