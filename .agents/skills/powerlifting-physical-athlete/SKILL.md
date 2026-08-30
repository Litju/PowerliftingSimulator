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

# Evolution Rules

Only add facts verified in this repository by imported assets, Unity inspection, measurements, tests, or visual evidence. Do not add generic Unity tutorials, speculative powered-joint architecture, or a debugging diary. Do not state that a physical rig, mass model, colliders, joints, or follower exists until its owning mission implements and qualifies it.

# Last Verified

2026-08-30 with Unity `6000.3.22f1`: official Standard archive and hashes verified; Humanoid Avatar valid; 16 required bones plus 8 useful optional bones resolved; measured artifact regenerated; front, side, and projected skeleton evidence visually inspected; no athlete Rigidbody, Collider, or ConfigurableJoint exists.
