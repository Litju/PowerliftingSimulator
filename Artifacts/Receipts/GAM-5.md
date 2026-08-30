# GAM-5 Canonical Humanoid Receipt

```text
MISSION=POWERLIFTING_SIMULATOR_GAM_5_CANONICAL_HUMANOID
LINEAR_ISSUE=GAM-5

BASE_HEAD=f6ce6348e9f91a55b3faedf6865bf296b6f7c086
QUALIFIED_HEAD=resolve with git rev-parse HEAD after the receipt commit
BRANCH=work/gam-5-canonical-humanoid

ASSET_PROVIDER=Quaternius
PACK=Universal Base Characters[Standard]; August 2025 pack page; 2025-12-16 Standard archive upload
SOURCE_URL=https://quaternius.com/packs/universalbasecharacters.html
ARCHIVE_SHA256=fdbf1804c90dfc1ea03e992bff7da2dfd1a79318e13270a660180f9308455f40
MODEL=Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx
MODEL_SHA256=79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9
LICENSE_EVIDENCE=Assets/Characters/Athlete/Source/License_Standard.txt; official pack page; official itch.io listing
LICENSE_CLASSIFICATION=CC0 1.0 Universal from consistent pack-specific evidence; general 2026-08-28 QAL context preserved in PROVENANCE.md

UNITY_VERSION=6000.3.22f1

MODEL_SCALE=1; useFileScale=true; bakeAxisConversion=false; 1 Unity unit=1 m
CALIBRATION_POSE=QUATERNIUS_SUPERHERO_MALE_IMPORT_BIND_POSE_V1; T-pose; root=(0,0,0); explicit Y=180-degree bind correction
UP_AXIS=+Y
FORWARD_AXIS=+Z after explicit correction; imported identity faces -Z

AVATAR_VALID=YES; Superhero_Male_FullBodyAvatar is valid and human
HUMANOID_MAPPING=PASS; 16 required bones resolve once; UpperChest, Neck, shoulders, toes, and middle proximal bones also resolve
REQUIRED_BONES=Hips,Spine,Chest,Head,bilateral UpperArm,LowerArm,Hand,UpperLeg,LowerLeg,Foot
MISSING_OPTIONAL_BONES=NONE among UpperChest,Neck,LeftToes,RightToes,LeftShoulder,RightShoulder

MEASUREMENT_ARTIFACT=Artifacts/Measurements/GAM-5-canonical-humanoid.json
MEASURED_HEIGHT_OR_STATURE_PROXY_M=1.91558015 rendered-bounds proxy
LEFT_THIGH_PROXY_M=0.42880017 bone-pivot distance
RIGHT_THIGH_PROXY_M=0.42880017 bone-pivot distance
LEFT_SHANK_PROXY_M=0.45878845 bone-pivot distance
RIGHT_SHANK_PROXY_M=0.45878845 bone-pivot distance
LEFT_FOOT_PROXY_M=0.15907356 foot-to-toe bone-pivot distance
RIGHT_FOOT_PROXY_M=0.15907355 foot-to-toe bone-pivot distance

CALIBRATION_SCENE=Assets/Scenes/Prototype/PhysicalAthleteCalibration.unity
VISUAL_EVIDENCE=Artifacts/Evidence/GAM-5/GAM-5-full-body-front-three-quarter.png; GAM-5-full-body-side.png; GAM-5-skeleton-overlay.png

REFERENCE_RIG_OWNERSHIP=Animator-owned authored/reference intent; GAM-5 calibration uses ReferenceVisibleRig_GAM5
PHYSICAL_RIG_OWNERSHIP=future sole Rigidbody/Collider/ConfigurableJoint authority; null/not implemented in GAM-5; Animator and IK forbidden from load-bearing transforms
VISIBLE_RIG_OWNERSHIP=player-facing skinned mesh; future consumer of physical snapshots plus bind offsets; presentation never physical truth

NEW_AUTOMATED_TESTS=1; CANONICAL_HUMANOID_REQUIRED_BONES_RESOLVE also checks valid Humanoid Avatar and finite positive importer scale
EXISTING_FOUNDATION_REGRESSION=GAM-2 qualification remains authoritative; no foundation runtime file changed
UNITY_COMPILE=PASS; fresh Unity 6000.3.22f1 batch import/compile; Tundra build success; return code 0
GAM5_EDITMODE_TEST=1/1 PASS; failed=0; skipped=0
MASTER_SPEC_INTEGRITY=PASS; 68 files; hashes and dependencies pass

PHYSICAL_RIG_IMPLEMENTED=NO
POWERED_JOINTS_IMPLEMENTED=NO
LIFT_WORK=NO

PROJECT_SKILL=.agents/skills/powerlifting-physical-athlete/SKILL.md
PROJECT_SKILL_UPDATED=PASS

KNOWN_LIMITATIONS=GAM-5 uses one combined reference/visible calibration hierarchy; no physical follower exists. Skeleton screenshot projects debug lines 0.18 m toward the +Z camera for readability; exact unprojected pivots and rotations are in the scene gizmos and JSON. Transform-pivot distances are not anatomical truth. The free Standard base model is bald and remains in its imported T-pose.

PONYTAIL_REVIEW=PASS; no speculative subsystem or redundant abstraction retained
ANTI_AI_SLOP_REVIEW=PASS; unused roughness import and failed transient overlay implementation removed; changes remain confined to GAM-5

STATUS=PASS
```
