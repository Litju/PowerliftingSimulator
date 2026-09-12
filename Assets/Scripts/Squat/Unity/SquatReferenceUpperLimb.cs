using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// One upper limb of the accepted GAM-10 back-squat setup, expressed as
    /// reference bone rotations.
    /// </summary>
    public readonly struct SquatReferenceUpperLimbSide
    {
        public SquatReferenceUpperLimbSide(
            Quaternion upperArmBoneRotation,
            Quaternion forearmBoneRotation,
            Quaternion handBoneRotation,
            Vector3 shoulderCenter,
            Vector3 elbowCenter,
            Vector3 handCenter,
            float forearmHandAngleDeg,
            float palmBarSurfaceErrorM)
        {
            UpperArmBoneRotation = upperArmBoneRotation;
            ForearmBoneRotation = forearmBoneRotation;
            HandBoneRotation = handBoneRotation;
            ShoulderCenter = shoulderCenter;
            ElbowCenter = elbowCenter;
            HandCenter = handCenter;
            ForearmHandAngleDeg = forearmHandAngleDeg;
            PalmBarSurfaceErrorM = palmBarSurfaceErrorM;
        }

        public Quaternion UpperArmBoneRotation { get; }
        public Quaternion ForearmBoneRotation { get; }
        public Quaternion HandBoneRotation { get; }
        public Vector3 ShoulderCenter { get; }
        public Vector3 ElbowCenter { get; }
        public Vector3 HandCenter { get; }
        public float ForearmHandAngleDeg { get; }
        public float PalmBarSurfaceErrorM { get; }
    }

    public readonly struct SquatReferenceUpperLimbSolution
    {
        public SquatReferenceUpperLimbSolution(
            SquatReferenceUpperLimbSide left,
            SquatReferenceUpperLimbSide right,
            Vector3 barCenter)
        {
            Left = left;
            Right = right;
            BarCenter = barCenter;
        }

        public SquatReferenceUpperLimbSide Left { get; }
        public SquatReferenceUpperLimbSide Right { get; }
        public Vector3 BarCenter { get; }
    }

    /// <summary>
    /// The single authority for the accepted GAM-10 bar-support arm geometry.
    /// The rendered reference preview and the physical adapter both read their
    /// upper-limb pose from here, so the physical athlete cannot silently
    /// diverge from the upper body the owner already accepted.
    ///
    /// The solve needs the posed thorax and shoulder-root positions. The
    /// preview supplies them from its live rig; the physical adapter has no
    /// posed rig and supplies them from PropagateBonePosition, which
    /// reproduces the same rigid hierarchy propagation analytically.
    /// </summary>
    public static class SquatReferenceUpperLimb
    {
        public const float HumerusAxialDeg = 0f;
        public const float BarRadiusM = 0.0145f;
        public const string UpperLimbAuthorityId = "GAM10_CANONICAL_BACK_SQUAT_BAR_SUPPORT_V1";

        public static SquatReferenceUpperLimbSolution Solve(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution)
        {
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));
            if (solution == null)
                throw new ArgumentNullException(nameof(solution));

            return Solve(
                calibration,
                solution,
                UpperChestBonePosition(calibration, solution),
                ShoulderRootPosition(calibration, solution, isLeft: true),
                ShoulderRootPosition(calibration, solution, isLeft: false));
        }

        public static SquatReferenceUpperLimbSolution Solve(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution,
            Vector3 upperChestPosition,
            Vector3 leftShoulderPosition,
            Vector3 rightShoulderPosition)
        {
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));
            if (solution == null)
                throw new ArgumentNullException(nameof(solution));

            Vector3 thoraxForward = solution.UpperChestFrameRotation * calibration.GameForward;
            Vector3 thoraxUp = solution.UpperChestFrameRotation * calibration.GameUp;
            Vector3 thoraxRight = solution.UpperChestFrameRotation * calibration.GameRight;

            Vector3 barCenter = upperChestPosition -
                thoraxForward * SquatReferenceKinematics.ArmBackOffsetM +
                thoraxUp * SquatReferenceKinematics.ArmBarHeightM;

            Vector3 leftGrip = barCenter - thoraxRight * SquatReferenceKinematics.ArmBarHalfWidthM;
            Vector3 rightGrip = barCenter + thoraxRight * SquatReferenceKinematics.ArmBarHalfWidthM;

            return new SquatReferenceUpperLimbSolution(
                SolveSide(
                    calibration.LeftUpperArm, calibration.LeftForearm, calibration.LeftHand,
                    leftShoulderPosition, leftGrip, thoraxForward, thoraxUp, thoraxRight, isLeft: true),
                SolveSide(
                    calibration.RightUpperArm, calibration.RightForearm, calibration.RightHand,
                    rightShoulderPosition, rightGrip, thoraxForward, thoraxUp, thoraxRight, isLeft: false),
                barCenter);
        }

        /// <summary>
        /// World position of the UpperChest bone once the trunk carries the
        /// solved frame rotations, derived by rigid propagation down the bind
        /// hierarchy rather than by reading a posed transform.
        /// </summary>
        public static Vector3 UpperChestBonePosition(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution)
        {
            Vector3 spine = PropagateBonePosition(
                solution.PelvisBonePosition, solution.PelvisBoneRotation,
                calibration.Pelvis, calibration.Spine);
            Vector3 chest = PropagateBonePosition(
                spine, BoneRotation(calibration.Spine, solution.SpineFrameRotation),
                calibration.Spine, calibration.Chest);
            return PropagateBonePosition(
                chest, BoneRotation(calibration.Chest, solution.ChestFrameRotation),
                calibration.Chest, calibration.UpperChest);
        }

        /// <summary>
        /// World position of a shoulder (upper arm) root. The clavicle is not
        /// posed by the reference solve, so the upper arm rides rigidly with
        /// the UpperChest bone.
        /// </summary>
        public static Vector3 ShoulderRootPosition(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution,
            bool isLeft)
        {
            return PropagateBonePosition(
                UpperChestBonePosition(calibration, solution),
                BoneRotation(calibration.UpperChest, solution.UpperChestFrameRotation),
                calibration.UpperChest,
                isLeft ? calibration.LeftUpperArm : calibration.RightUpperArm);
        }

        private static Quaternion BoneRotation(SquatReferenceBoneFrame bone, Quaternion frameRotation) =>
            frameRotation * bone.BoneFromAnatomicalFrame;

        private static Vector3 PropagateBonePosition(
            Vector3 parentPosition,
            Quaternion parentRotation,
            SquatReferenceBoneFrame parent,
            SquatReferenceBoneFrame child)
        {
            Vector3 offsetInParentBind = Quaternion.Inverse(parent.BindRotation) *
                (child.BindPosition - parent.BindPosition);
            return parentPosition + parentRotation * offsetInParentBind;
        }

        private static SquatReferenceUpperLimbSide SolveSide(
            SquatReferenceBoneFrame upperArmFrame,
            SquatReferenceBoneFrame forearmFrame,
            SquatReferenceBoneFrame handFrame,
            Vector3 shoulder,
            Vector3 barGripCenterline,
            Vector3 thoraxForward,
            Vector3 thoraxUp,
            Vector3 thoraxRight,
            bool isLeft)
        {
            float upperLength = Vector3.Distance(forearmFrame.BindPosition, upperArmFrame.BindPosition);
            float forearmLength = Vector3.Distance(handFrame.BindPosition, forearmFrame.BindPosition);

            float sideSign = isLeft ? -1f : 1f;
            Vector3 poleHint = -thoraxUp * 0.95f - thoraxForward * 0.04f + thoraxRight * (sideSign * 0.30f);

            // The palm faces the front of the body with a slight upward rest
            // angle, which is what puts the forearm under the bar.
            Vector3 palmNormalTarget = (thoraxForward * 0.95f + thoraxUp * 0.25f).normalized;

            SolveTwoBone(
                shoulder, barGripCenterline, upperLength, forearmLength, poleHint,
                out Vector3 approxElbow, out Vector3 approxHand);
            Vector3 forearmApproachDir = (approxHand - approxElbow).normalized;

            Quaternion approxHandRot = HandRotation(forearmApproachDir, palmNormalTarget, isLeft);

            // In FBX local space the hand's longitudinal axis is +Y and the
            // palm normal is +X on the right hand, -X on the left.
            Vector3 palmOffset = isLeft ? new Vector3(-0.018f, 0.038f, 0f) : new Vector3(0.018f, 0.038f, 0f);
            Vector3 palmNormalWorld = approxHandRot * (isLeft ? Vector3.left : Vector3.right);
            Vector3 palmContactTarget = barGripCenterline - palmNormalWorld * BarRadiusM;
            Vector3 handBoneTarget = palmContactTarget - approxHandRot * palmOffset;

            SolveTwoBone(
                shoulder, handBoneTarget, upperLength, forearmLength, poleHint,
                out Vector3 elbow, out Vector3 solvedHand);

            Vector3 upperDir = (elbow - shoulder).normalized;
            Vector3 forearmFinalDir = (solvedHand - elbow).normalized;

            // Upper arm: longitudinal axis (+Y) along the humerus, anterior
            // surface (+Z, biceps) facing forward.
            Vector3 upperBoneForward = Vector3.ProjectOnPlane(thoraxForward, upperDir).normalized;
            Vector3 upperBoneRight = Vector3.Cross(upperDir, upperBoneForward).normalized;
            Quaternion baseUpperRot = FromAxes(upperBoneRight, upperDir, upperBoneForward);
            float humerusSignedDeg = isLeft ? -HumerusAxialDeg : HumerusAxialDeg;
            Quaternion upperRot = Quaternion.AngleAxis(humerusSignedDeg, upperDir) * baseUpperRot;

            Quaternion handRot = HandRotation(forearmFinalDir, palmNormalTarget, isLeft);

            // The forearm follows the hand through their bind relationship, so
            // the wrist keeps the authored neutral the reference rig carries.
            Quaternion handBindRelativeToForearm =
                Quaternion.Inverse(forearmFrame.BindRotation) * handFrame.BindRotation;
            Quaternion forearmRot = handRot * Quaternion.Inverse(handBindRelativeToForearm);

            float forearmHandAngleDeg = Vector3.Angle(forearmFinalDir, handRot * Vector3.up);
            Vector3 palmCenterWorld = solvedHand + handRot * palmOffset;
            Vector3 radialOffset = Vector3.ProjectOnPlane(palmCenterWorld - barGripCenterline, thoraxRight);
            float palmBarSurfaceErrorM = Mathf.Abs(radialOffset.magnitude - BarRadiusM);

            return new SquatReferenceUpperLimbSide(
                upperRot, forearmRot, handRot,
                shoulder, elbow, solvedHand,
                forearmHandAngleDeg, palmBarSurfaceErrorM);
        }

        private static Quaternion HandRotation(Vector3 longitudinal, Vector3 palmNormalTarget, bool isLeft)
        {
            Vector3 up = longitudinal;
            Vector3 palmPerp = Vector3.ProjectOnPlane(palmNormalTarget, up).normalized;
            Vector3 right = isLeft ? -palmPerp : palmPerp;
            Vector3 forward = Vector3.Cross(right, up).normalized;
            return FromAxes(right, up, forward);
        }

        private static Quaternion FromAxes(Vector3 right, Vector3 up, Vector3 forward)
        {
            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(right.x, right.y, right.z, 0f));
            matrix.SetColumn(1, new Vector4(up.x, up.y, up.z, 0f));
            matrix.SetColumn(2, new Vector4(forward.x, forward.y, forward.z, 0f));
            return matrix.rotation;
        }

        public static void SolveTwoBone(
            Vector3 root,
            Vector3 target,
            float upperLength,
            float lowerLength,
            Vector3 poleHint,
            out Vector3 elbow,
            out Vector3 solvedTarget)
        {
            Vector3 delta = target - root;
            float distance = Mathf.Max(delta.magnitude, 0.0001f);
            Vector3 direction = delta / distance;
            float maximum = Mathf.Max(upperLength + lowerLength - 0.0005f, 0.0001f);
            float minimum = Mathf.Min(Mathf.Abs(upperLength - lowerLength) + 0.0005f, maximum);
            float clampedDistance = Mathf.Clamp(distance, minimum, maximum);
            solvedTarget = root + direction * clampedDistance;
            float along = (upperLength * upperLength - lowerLength * lowerLength +
                clampedDistance * clampedDistance) / (2f * clampedDistance);
            float heightSquared = Mathf.Max(0f, upperLength * upperLength - along * along);
            Vector3 pole = Vector3.ProjectOnPlane(poleHint, direction);
            if (pole.sqrMagnitude < 1e-8f)
                pole = Vector3.Cross(direction, Vector3.right);
            if (pole.sqrMagnitude < 1e-8f)
                pole = Vector3.Cross(direction, Vector3.forward);
            elbow = root + direction * along + pole.normalized * Mathf.Sqrt(heightSquared);
        }
    }
}
