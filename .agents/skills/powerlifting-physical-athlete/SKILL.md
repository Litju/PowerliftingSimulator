---
name: powerlifting-physical-athlete
description: repository-specific operating knowledge for the Powerlifting Simulator canonical humanoid and shared physical-athlete implementation
---

# Purpose

Keep GAM-5 through GAM-9 aligned with verified athlete-asset facts. This skill owns the canonical athlete import, its measured rig geometry, and reference/physical/visible ownership. It does not own the foundation timestep, coordinates, input, observations, or physics-scene authority; those remain in `powerlifting-foundation`.

# Canonical Asset

- Exact asset: Quaternius `Superhero_Male_FullBody` from `Universal Base Characters[Standard]`.
- Pack page: `https://quaternius.com/packs/universalbasecharacters.html`.
- Official download page: `https://quaternius.itch.io/universal-base-characters`.
- Pack release shown by Quaternius: August 2025. Standard archive upload shown by itch.io: 2025-12-16.
- Download date: 2026-08-30.
- Archive: `Universal Base Characters[Standard].zip`, 128,968,391 bytes, SHA-256 `fdbf1804c90dfc1ea03e992bff7da2dfd1a79318e13270a660180f9308455f40`.
- Imported files:
  - `Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx`
  - `Assets/Characters/Athlete/Textures/T_Superhero_Male_Ligh.png`
  - `Assets/Characters/Athlete/Textures/T_Superhero_Male_Normal.png`
  - `Assets/Characters/Athlete/Source/License_Standard.txt`
- Model SHA-256: `79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9`.
- Provenance record: `Assets/Characters/Athlete/Source/PROVENANCE.md`.
- License evidence: the included unmodified `License_Standard.txt`, official pack page, and official itch.io listing all state CC0 1.0 Universal. The general Quaternius QAL v1.0 page dated 2026-08-28 exists and states changes are non-retroactive; do not replace the preserved pack-specific evidence or claim authorship.

# Unity Import Contract

- Verified Unity: `6000.3.22f1`.
- ModelImporter: global scale `1`, file scale enabled, axis conversion not baked, animation type `Human`, Avatar created from this model, animation import disabled, and material import disabled.
- Avatar: `Superhero_Male_FullBodyAvatar`; valid and Humanoid.
- Imported identity faces `-Z`. `CanonicalAthleteCalibration` applies the explicit root correction `Quaternion(0, 1, 0, approximately 0)`, a 180-degree Y rotation, so calibrated athlete-forward is canonical `+Z`; `+Y` is up and athlete-right is `+X`.
- Calibration pose: `QUATERNIUS_SUPERHERO_MALE_IMPORT_BIND_POSE_V1`, the imported T-pose/reference pose with no controller and root motion disabled.
- Calibration root position is `(0,0,0)`. The imported model child is raised `0.10173738 m` so rendered bounds meet the floor.
- All skinned renderer material slots in the calibration scene use `Assets/Characters/Athlete/Materials/CanonicalAthlete.mat`, a URP Lit material using the imported light albedo and normal map. Visual inspection passed front/three-quarter and side views.

# Verified Bone Map

Paths are relative to the imported `ReferenceVisibleRig_GAM5` root.

| HumanBodyBones | Transform | Exact hierarchy path |
|---|---|---|
| Hips | `pelvis` | `Armature/root/pelvis` |
| Spine | `spine_01` | `Armature/root/pelvis/spine_01` |
| Chest | `spine_02` | `Armature/root/pelvis/spine_01/spine_02` |
| UpperChest | `spine_03` | `Armature/root/pelvis/spine_01/spine_02/spine_03` |
| Neck | `neck_01` | `Armature/root/pelvis/spine_01/spine_02/spine_03/neck_01` |
| Head | `Head` | `Armature/root/pelvis/spine_01/spine_02/spine_03/neck_01/Head` |
| LeftShoulder | `clavicle_l` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_l` |
| LeftUpperArm | `upperarm_l` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_l/upperarm_l` |
| LeftLowerArm | `lowerarm_l` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_l/upperarm_l/lowerarm_l` |
| LeftHand | `hand_l` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_l/upperarm_l/lowerarm_l/hand_l` |
| RightShoulder | `clavicle_r` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_r` |
| RightUpperArm | `upperarm_r` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_r/upperarm_r` |
| RightLowerArm | `lowerarm_r` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_r/upperarm_r/lowerarm_r` |
| RightHand | `hand_r` | `Armature/root/pelvis/spine_01/spine_02/spine_03/clavicle_r/upperarm_r/lowerarm_r/hand_r` |
| LeftUpperLeg | `thigh_l` | `Armature/root/pelvis/thigh_l` |
| LeftLowerLeg | `calf_l` | `Armature/root/pelvis/thigh_l/calf_l` |
| LeftFoot | `foot_l` | `Armature/root/pelvis/thigh_l/calf_l/foot_l` |
| LeftToes | `ball_l` | `Armature/root/pelvis/thigh_l/calf_l/foot_l/ball_l` |
| RightUpperLeg | `thigh_r` | `Armature/root/pelvis/thigh_r` |
| RightLowerLeg | `calf_r` | `Armature/root/pelvis/thigh_r/calf_r` |
| RightFoot | `foot_r` | `Armature/root/pelvis/thigh_r/calf_r/foot_r` |
| RightToes | `ball_r` | `Armature/root/pelvis/thigh_r/calf_r/foot_r/ball_r` |

All required bones resolve once. UpperChest, Neck, shoulders, toes, and bilateral middle-finger proximal bones are present; no listed optional bone is missing.

# Measured Geometry

- Artifact: `Artifacts/Measurements/GAM-5-canonical-humanoid.json`.
- Units: SI meters; `1 Unity unit = 1 m`.
- Rendered stature proxy: `1.91558015 m` from combined `SkinnedMeshRenderer` world bounds.
- Hip width proxy: `0.22861032 m`.
- Thigh bone-pivot distance: left/right `0.42880017 m`.
- Shank bone-pivot distance: left/right `0.45878845 m`.
- Foot-to-toe bone-pivot proxy: left/right approximately `0.15907355 m`.
- Upper-arm bone-pivot distance: left `0.25111511 m`, right `0.25111505 m`.
- Forearm bone-pivot distance: left `0.24359427 m`, right `0.24359423 m`.
- Hand-to-middle-proximal proxy: left `0.11651794 m`, right `0.11651802 m`.
- Spine-to-chest `0.10634995 m`; chest-to-upper-chest `0.13313361 m`; upper-chest-to-neck `0.21509321 m`; neck-to-head `0.08285239 m`.
- Shoulder width proxy: `0.42402336 m`.
- Foot-center stance separation: `0.22861031 m`.
- Method: Euclidean distances between Unity Humanoid bone-transform world positions in the calibrated bind pose. Rendered height alone uses combined renderer bounds. Full positions and rotations are in the JSON artifact.

# Rig Ownership

- `ReferenceRig`: future hidden authored/reference intent; Animator may own this rig.
- `PhysicalRig`: future hidden Rigidbody/Collider/ConfigurableJoint truth and sole physical authority. It does not exist in GAM-5; Animator and IK may never own its load-bearing transforms.
- `VisibleRig`: player-facing skinned human; it will eventually consume recorded physical poses plus fixed bind offsets. Later bounded cosmetic corrections may not become physical truth.
- GAM-5 uses one imported `ReferenceVisibleRig_GAM5` hierarchy for calibration only. `AthleteRigOwnership` records the reference Animator and visible root while leaving `PhysicalRigRoot` null.

# Asset-Specific Quirks

- The imported identity orientation faces `-Z`; the canonical scene needs the verified 180-degree Y bind correction.
- The archive spells the light albedo filename `T_Superhero_Male_Ligh.png`; preserve that real filename.
- Rig names are Unreal-style lowercase limb names (`thigh_l`, `calf_l`, `upperarm_l`) rather than Mixamo names.
- Toe bones are named `ball_l` and `ball_r`; UpperChest is `spine_03`.
- The bind pose is a T-pose and the Standard free model is bald. No hairstyle or customization asset is required for GAM-5.

# Prototype Validation Workflow

1. Open `Assets/Scenes/Prototype/PhysicalAthleteCalibration.unity`.
2. The athlete, floor, lights, and inspection camera should be immediately visible. Disable `SkeletonBoneOverlay` for the clean human view or enable it for the cyan bone view.
3. Select `CanonicalAthleteCalibration` to see live bone connections, critical labels, and canonical axes from `HumanoidSkeletonDebug` in Scene view.
4. Rebuild measurements/evidence with `Powerlifting Simulator > GAM-5 > Build Calibration Scene and Evidence` only after changing the canonical import.
5. Run the single EditMode invariant `CANONICAL_HUMANOID_REQUIRED_BONES_RESOLVE` after importer or rig changes.

# Scientific Claim Ceiling

The JSON records asset transform pivots, their distances, rotations, and rendered bounds. Segment names such as thigh, shank, hand, stance, and shoulder are engineering proxies. They do not establish anatomical joint centers, biological segment lengths, COM, mass, inertia, muscle properties, tissue load, or clinical anatomy. GAM-6 must keep this distinction when building the physical proxy.

# Physical Segmentation

- `Assets/Scripts/Athlete/PhysicalAthleteDefinition.cs` owns the verified GAM-6 16-body recipe: pelvis, abdomen, thorax, head_neck, bilateral upper arms, forearms, hands, thighs, shanks, and feet.
- `Assets/Scripts/Athlete/PhysicalAthleteRig.cs` creates the bodies in `FoundationRuntime.AuthoritativeScene`; the graph has 15 `ConfigurableJoint` parent-child constraints and no Rigidbody-per-render-bone proliferation.
- `Assets/Scenes/Prototype/PhysicalAthletePhysics.unity` is the owner-review scene. It keeps separate hidden reference, authoritative physical, and player-visible rigs.

# Mass, COM, and Inertia

- The prototype profile is `GAM6_QUATERNIUS_100KG_GAME_CALIBRATION_V1`; 100 kg is a `GAME_CALIBRATION`, not a population claim.
- The frozen mass fractions are used without redistribution and assign exactly 100 kg across the 16 bodies. Runtime segment mass values are `ENGINEERING_DERIVED` from the calibrated profile mass and frozen fractions.
- Limb longitudinal proxy placement uses de Leva (1996) fractions where definitions are close enough to guide an engineering proxy; torso, head/neck, hands, and final foot placement use explicit proxy centers. All runtime COM placements remain `ENGINEERING_DERIVED` because GAM-5 bone pivots are not anatomical joint centers.
- Principal inertia is the analytic solid-box tensor `I_x=m(h^2+d^2)/12`, `I_y=m(w^2+d^2)/12`, `I_z=m(w^2+h^2)/12` using each actual proxy's dimensions. Capsule bodies intentionally use that documented equivalent-box seed; all axes are positive and finite.

# Collider and Joint Recipes

- Pelvis, abdomen, thorax, hands, and feet use boxes; head/neck and long limb segments use capsules. Feet remain platform-aligned while hand boxes align their long local X axis to the measured hand proxy.
- Joint anchors are the calibrated GAM-5 Humanoid pivot proxies transformed into both connected body frames. The neutral world anchors coincide within `0.0001 m`.
- Knees, elbows, and ankles are hinge-dominant. Hips, shoulders, trunk, wrists, and neck are bounded multiaxial joints. Limits are broad `GAME_CALIBRATION` ranges, not clinical or subject-specific ROM.
- Projection is `None`; every angular drive spring, damper, and maximum force is zero in GAM-6.

# Collision, Passive Mode, and Reset

- Adjacent parent-child collider pairs are ignored explicitly. Nonadjacent self-collision remains enabled; the qualified neutral configuration measured `0 m` maximum nonadjacent penetration.
- `PASSIVE_RAGDOLL` starts with gravity enabled and all 16 bodies, including the pelvis, dynamic. There is no hidden support, position lock, root pin, balance force, or Animator on the physical rig.
- `FoundationRuntime.RegisterBody` extends the established authoritative-scene seam so reset restores every registered body pose, rotation, kinematic state, linear/angular velocity, and sleep state without adding another physics step owner.
- The visible follower applies fixed body-to-bone bind rotation offsets in `LateUpdate` and the pelvis bind-position offset one way; it never writes back to physics.
- Runtime review controls expose `Reset + Passive`, explicit authoring-only `Inspect Neutral`, and `Release`, plus visible mesh, physical proxy, COM, anchor, and joint-axis toggles.

# GAM-6 Evidence

- Durable calibration: `Artifacts/Measurements/GAM-6-physical-humanoid.json`.
- Visual evidence: `Artifacts/Evidence/GAM-6/` contains neutral overlay, collider/COM/axis debug, falling, and settled views.
- The qualified PlayMode fixture observed gravity-driven collapse by 1.0 s, finite bounded velocities, less than `0.08 m` anchor separation through 3.0 s, and exact neutral reset with zero velocities.

# Powered Joint Authority

- `PoweredJointController` is the sole athlete drive writer. It owns `targetRotation`, `targetAngularVelocity`, drive mode, and all angular drive structures for the 15 physical joints.
- `FoundationRuntime.RegisterPrePhysicsStep` accepts one callback only. `PhysicsTickDriver.StepOne` invokes it exactly once after clock advance and input sampling and before the sole `PhysicsScene.Simulate(0.01)` call; duplicate registration throws and reset preserves the registration.
- GAM-7 powers 14 joints and leaves `head_neck` passive. Hinge-dominant ankles, knees, and elbows use `RotationDriveMode.XYAndZ` with only `angularXDrive`; multiaxial joints use `Slerp`.

# Joint-Space Calibration

- Calibration version is `GAM7_CONFIGURABLE_JOINT_LOCAL_V1`. Logical neutral is identity in each calibrated `J_i` frame. Unity local `targetRotation` receives the canonicalized inverse of the requested `J_i` orientation; target angular velocity in rad/s receives the corresponding negated local target vector.
- Each runtime joint stores neutral parent-to-child orientation plus the orthonormal joint-space basis derived from `axis` and `secondaryAxis`. Diagnostics remove the neutral relative orientation and express actual/error state in that basis.
- Physical fixtures in Unity 6000.3.22f1 verified neutral identity, quaternion sign equivalence, positive knee flexion, positive elbow flexion, and equal relative response after rotating the whole parent/body fixture in world space.

# Joint Family Profiles

All values below are `GAME_CALIBRATION`, not biological joint properties.

| Family | Spring | Damper | Base capacity N*m | Max target rate rad/s |
|---|---:|---:|---:|---:|
| ankle | 650 | 70 | 180 | 2.0 |
| knee | 800 | 80 | 300 | 2.5 |
| hip | 900 | 90 | 360 | 2.2 |
| trunk | 800 | 85 | 260 | 1.8 |
| shoulder | 500 | 55 | 130 | 2.5 |
| elbow | 450 | 45 | 100 | 3.0 |
| wrist | 250 | 30 | 45 | 2.5 |

# Activation and Capacity

- The finite force-mode authority contract is `maximumForce = baseCapacity_Nm * capacityScale * activation`, with activation clamped to `[0,1]`, finite nonnegative capacity scale, and `useAcceleration=false`.
- Physical fixtures verified activation `0`, `0.5`, and `1` produce zero, half, and full finite maximum force. Spring/damper govern tracking response and do not define capacity.
- Requested orientations are normalized/canonicalized and shortest-arc rate limited per 0.01 s tick. The command-side modeled demand is an `ENGINEERING_CONCEPTUAL` diagnostic and does not use `ConfigurableJoint.currentTorque`.

# Powered Prototype Modes

- `PASSIVE` restores the GAM-6 zero-drive collapse.
- `POWERED_NEUTRAL` applies finite open-loop neutral tracking with no root or balance control.
- `ZERO_ACTIVATION` retains the powered architecture with zero maximum force.
- `SELECTED_JOINT_PULSE` applies a bounded signed 20-degree internal-radian command to the selected knee or elbow.
- The actual review scene measured whole-body COM drop after 0.75 s of authoritative simulation as `0.44399 m` passive versus `0.12824 m` powered. The positive left-knee pulse measured `+5.014 degrees` in calibrated joint space, and the visible human followed both results.

# Known Open-Loop Limitations

Finite local neutral drives materially delay collapse but do not provide COM feedback, support-polygon control, or indefinite balance. GAM-7 has no root control, hidden support, direct torque path, barbell behavior, lift controller, or fatigue model.

# Evolution Rules

Only add facts verified in this repository by imported assets, Unity inspection, measurements, tests, or visual evidence. Do not add generic Unity tutorials, speculative powered-joint architecture, or a debugging diary. Do not state that a physical rig, mass model, colliders, joints, or follower exists until its owning mission implements and qualifies it.

# Last Verified

2026-08-31 with Unity `6000.3.22f1`: GAM-5/GAM-6 asset and passive-rig facts remain valid. GAM-7 adds one pre-physics drive authority, 14 finite powered joints in seven family profiles, calibrated local target conversion, activation scaling, shortest-arc target limiting, passive/powered/zero/pulse review modes, focused convention fixtures, and paired actual-human visual evidence. Owner powered-athlete review remains the final acceptance boundary.
