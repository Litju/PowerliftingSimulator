MISSION=POWERLIFTING_SIMULATOR_GAM_6_PHYSICAL_HUMANOID
BASE_HEAD=2aa7fc6aa7091425132325b0f2113ef55a64a24d
FINAL_HEAD=resolve with git rev-parse HEAD after the receipt commit
BRANCH=work/gam-6-physical-humanoid

PHYSICAL_SEGMENTS=16
JOINTS=15 passive ConfigurableJoint constraints
ATHLETE_MASS_KG=100.0000
MASS_ERROR_KG=0.0000

COM_MODEL=ENGINEERING_DERIVED; de Leva longitudinal proxies where source-compatible plus explicit proxy centers from GAM-5 measured bone pivots
INERTIA_MODEL=ENGINEERING_DERIVED; finite positive analytic equivalent-box principal tensors using actual proxy dimensions
COLLIDERS=pelvis/abdomen/thorax/hands/feet boxes; head-neck and long limbs capsules; adjacent parent-child pairs ignored only
JOINT_FRAMES=GAM-5 measured Humanoid pivot proxies transformed into both body frames; neutral anchors coincide within 0.0001 m
PASSIVE_LIMITS=bounded hinge-dominant knees/elbows/ankles and bounded multiaxial hips/shoulders/trunk/wrists/neck

ALL_DYNAMIC=YES; 16/16 including pelvis; gravity enabled
HIDDEN_SUPPORT=NONE
POWERED_DRIVES=NONE; all angular springs/dampers/maximum forces zero
PROJECTION=NONE

PASSIVE_RAGDOLL=PASS; gravity-driven collapse by 1.0 s and coherent bounded state through 3.0 s
RESET=PASS; all bodies restore exact neutral poses with zero linear/angular velocity
VISIBLE_ALIGNMENT=PASS; neutral physical overlay visually inspected against the canonical athlete
VISIBLE_FOLLOWER=PASS; one-way LateUpdate bind-offset follower with no physics writeback

MEASUREMENT_ARTIFACT=Artifacts/Measurements/GAM-6-physical-humanoid.json
VISUAL_EVIDENCE=Artifacts/Evidence/GAM-6/; neutral overlay, collider/COM/joint-axis debug, 0.35 s fall, 1.00 s fall, 3.00 s settled

AUTOMATED_TESTS=PASS; 1 new EditMode invariant test; 1 new PlayMode physical integration test; full EditMode 40/40; full PlayMode 16/16

PROJECT_SKILL=.agents/skills/powerlifting-physical-athlete/SKILL.md
PROJECT_SKILL_STATUS=PASS; GAM-6 authority, construction, evidence, and evolution rules recorded

COMPILE=PASS; Unity 6000.3.22f1 batch compile/test runs; Tundra success
MASTER_SPEC=PASS; 68 files; hashes PASS; dependencies PASS
DIFF_CHECK=PASS

PR=https://github.com/Litju/PowerliftingSimulator/pull/5
LINEAR_STATUS=GAM-6 In Review; PR attached

KNOWN_LIMITATIONS=COM locations are engineering proxies because asset pivots are not anatomical joint centers; capsules use documented equivalent-box inertia seeds; joint limits are broad game calibration; final acceptance requires owner physical review
STATUS=PASS_WITH_LIMITATIONS

NEXT AUTHORIZED UNIT=OWNER PHYSICAL REVIEW
