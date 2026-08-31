MISSION=POWERLIFTING_SIMULATOR_GAM_7_FINITE_POWERED_JOINTS
BASE_HEAD=f97fff241991c87e849b0685fc9fe649107fa775
FINAL_HEAD=resolve with git rev-parse HEAD after the receipt commit
BRANCH=work/gam-7-powered-joints

PRE_PHYSICS_ACTUATOR_SEAM=FoundationRuntime.RegisterPrePhysicsStep; exactly one callback after clock/input sampling and before the sole PhysicsScene.Simulate call
DRIVE_WRITER=PowerliftingSimulator.Athlete.PoweredJointController
DRIVE_WRITER_COUNT=1; duplicate pre-physics registration throws InvalidOperationException

POWERED_JOINT_COUNT=14
PASSIVE_JOINT_COUNT=1; head_neck remains passive

HINGE_DRIVE_MODE=RotationDriveMode.XYAndZ; angularXDrive active; angularYZDrive and slerpDrive zero
MULTIAXIAL_DRIVE_MODE=RotationDriveMode.Slerp; slerpDrive active; angularXDrive and angularYZDrive zero

TARGET_ROTATION_CONVENTION=GAM7_CONFIGURABLE_JOINT_LOCAL_V1; logical normalized/canonical q_target_J maps to inverse(q_target_J); neutral is identity
PARENT_ROTATION_FIXTURE=PASS; equal relative response after whole parent/body fixture rotation
POSITIVE_KNEE_FIXTURE=PASS; positive J-frame X command produces knee flexion
POSITIVE_ELBOW_FIXTURE=PASS; positive J-frame X command produces elbow flexion
QUATERNION_SIGN_EQUIVALENCE=PASS; q and -q convert and rate-limit identically

JOINT_FAMILY_PROFILE_COUNT=7; ankle, knee, hip, trunk, shoulder, elbow, wrist

SPRING_RANGE=250..900 Unity/PhysX engine parameter
DAMPING_RANGE=30..90 Unity/PhysX engine parameter
CAPACITY_RANGE_NM=45..360 GAME_CALIBRATION
TARGET_RATE_RANGE_RAD_S=1.8..3.0

ALL_MAX_FORCE_FINITE=YES
USE_ACCELERATION_FALSE=YES
PROJECTION_NONE=YES
DIRECT_TORQUE_PATH=NONE
CURRENT_TORQUE_AUTHORITY=NONE

ACTIVATION_ZERO=PASS; maximumForce=0
ACTIVATION_HALF=PASS; maximumForce=0.5*family capacity*capacityScale
ACTIVATION_FULL=PASS; maximumForce=family capacity*capacityScale

PASSIVE_MODE=PASS; GAM-6 zero-drive collapse preserved
POWERED_NEUTRAL=PASS; finite open-loop drives materially reduce short-interval collapse
ZERO_ACTIVATION_MODE=PASS; powered architecture retained with maximumForce=0
SELECTED_JOINT_PULSE=PASS; bounded signed 20-degree internal-radian knee/elbow command

POWERED_VS_PASSIVE_RESULT=PASS; at authoritative t=0.75 s whole-body COM drop was 0.44399 m passive versus 0.12824 m powered; positive left-knee pulse reached +5.014 degrees in calibrated J frame

BALANCE_CONTROLLER=NO
ROOT_CONTROL=NO
HIDDEN_SUPPORT=NO
BARBELL=NO
LIFT_WORK=NO

CALIBRATION_ARTIFACT=Artifacts/Measurements/GAM-7-powered-joints.json
VISUAL_EVIDENCE=Artifacts/Evidence/GAM-7/; paired passive/powered t=0.75 s, positive knee pulse, finite-drive proxy/axis diagnostic view

NEW_AUTOMATED_TESTS=6; neutral/sign equivalence, positive knee, positive elbow, parent rotation, finite activation scaling, one writer plus actual-human comparison within the six PlayMode fixtures
FULL_EDITMODE=PASS; 41/41
FULL_PLAYMODE=PASS; 22/22

PROJECT_SKILL=.agents/skills/powerlifting-physical-athlete/SKILL.md
PROJECT_SKILL_UPDATED=PASS; verified GAM-7 authority, calibration, profiles, activation, modes, evidence, and limitations recorded

MASTER_SPEC=PASS; 68 files; hashes PASS; dependencies PASS
COMPILE=PASS; Unity 6000.3.22f1 batch compile/import and test assemblies
DIFF_CHECK=PASS

KNOWN_LIMITATIONS=finite local neutral drives provide no COM/support-polygon feedback or indefinite balance; neck remains passive; target angular velocity contract is implemented but initial powered modes command zero rad/s; final acceptance requires owner powered-athlete review

STATUS=PASS_WITH_LIMITATIONS
