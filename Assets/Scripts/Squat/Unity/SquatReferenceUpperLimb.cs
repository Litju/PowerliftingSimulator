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
            Vector3 referencePalmNormal = (thoraxForward * 0.95f + thoraxUp * 0.25f).normalized;

            return new SquatReferenceUpperLimbSolution(
                SolveSide(
                    calibration.LeftUpperArm, calibration.LeftForearm, calibration.LeftHand,
                    leftShoulderPosition, leftGrip, thoraxForward, thoraxUp, thoraxRight,
                    thoraxRight, referencePalmNormal, new Vector3(-0.018f, 0.038f, 0f), isLeft: true),
                SolveSide(
                    calibration.RightUpperArm, calibration.RightForearm, calibration.RightHand,
                    rightShoulderPosition, rightGrip, thoraxForward, thoraxUp, thoraxRight,
                    thoraxRight, referencePalmNormal, new Vector3(0.018f, 0.038f, 0f), isLeft: false),
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

        /// <summary>
        /// Grip-plane coordination for the live bar (GAM-68).
        ///
        /// The previous live solve kept the hand collinear with the forearm and
        /// let a discrete elbow swivel choose the forearm. With the 0.90 m grip
        /// and the physical shoulder anchors 0.42 m apart, that hung the elbow
        /// under the shoulder and leaned the forearm ~47 deg outward, so the
        /// hand pointed along the shaft and the palm lay on top of it.
        ///
        /// Here the hand pose is defined by the grip itself:
        ///  * the shaft lies along the hand's measured metacarpophalangeal line
        ///    (obliquity from the avatar's index/little proximal bones), so the
        ///    hand long axis crosses the shaft and the fingers can wrap it;
        ///  * the measured palmar surface touches the shaft with its signed
        ///    outward normal pointing at the shaft axis, from behind the bar;
        ///  * <paramref name="handPitchRad"/> is the one free rotation of that
        ///    grip about the shaft, chosen by the caller against the real
        ///    joint limits.
        /// The wrist position then follows in closed form, and the elbow is
        /// placed continuously on its reachable circle: as far posterior as the
        /// forearm-to-hand swing budget allows. No candidate list, no swivel
        /// steps, no new degree of freedom.
        /// </summary>
        public static bool TrySolveSideForLiveGrip(
            SquatReferenceRigCalibration calibration,
            bool isLeft,
            Vector3 shoulder,
            Vector3 gripOnShaftAxis,
            Vector3 thoraxForward,
            Vector3 thoraxUp,
            Vector3 lateral,
            Vector3 barAxis,
            Vector3 palmCenterLocal,
            Vector3 palmNormalLocal,
            float gripObliquityDeg,
            float handPitchRad,
            float wristSwingBudgetDeg,
            out SquatReferenceUpperLimbSide side)
        {
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));
            side = default;
            SquatReferenceBoneFrame upperArmFrame = isLeft ? calibration.LeftUpperArm : calibration.RightUpperArm;
            SquatReferenceBoneFrame forearmFrame = isLeft ? calibration.LeftForearm : calibration.RightForearm;
            SquatReferenceBoneFrame handFrame = isLeft ? calibration.LeftHand : calibration.RightHand;
            float upperLength = Vector3.Distance(forearmFrame.BindPosition, upperArmFrame.BindPosition);
            float forearmLength = Vector3.Distance(handFrame.BindPosition, forearmFrame.BindPosition);

            Vector3 axis = barAxis.normalized;
            Vector3 up = Vector3.ProjectOnPlane(thoraxUp, axis).normalized;
            Vector3 outward = Vector3.Dot(lateral, axis) >= 0f ? axis : -axis;

            // Fingers up over the shaft, pitched about the shaft, tipped toward
            // the little-finger (outer) side by the measured MCP obliquity.
            Vector3 pitched = Quaternion.AngleAxis(handPitchRad * Mathf.Rad2Deg, axis) * up;
            float oblique = gripObliquityDeg * Mathf.Deg2Rad;
            Vector3 handLong = (pitched * Mathf.Cos(oblique) + outward * Mathf.Sin(oblique)).normalized;
            Vector3 palmNormal = Vector3.Cross(handLong, axis).normalized;
            if (Vector3.Dot(palmNormal, thoraxForward) < 0f)
                palmNormal = -palmNormal;

            Quaternion handRot = HandRotationFromMeasuredPalm(handLong, palmNormal, palmNormalLocal);
            Vector3 palmContact = gripOnShaftAxis - palmNormal * BarRadiusM;
            Vector3 wrist = palmContact - handRot * palmCenterLocal;

            Vector3 shoulderToWrist = wrist - shoulder;
            float distance = shoulderToWrist.magnitude;
            if (distance >= upperLength + forearmLength - 0.0005f ||
                distance <= Mathf.Abs(upperLength - forearmLength) + 0.0005f)
                return false;
            Vector3 direction = shoulderToWrist / distance;
            float along = (upperLength * upperLength - forearmLength * forearmLength + distance * distance) /
                (2f * distance);
            float radius = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
            Vector3 center = shoulder + direction * along;
            Vector3 u = Vector3.ProjectOnPlane(-thoraxUp, direction);
            if (u.sqrMagnitude < 1e-8f)
                u = Vector3.ProjectOnPlane(-thoraxForward, direction);
            u.Normalize();
            Vector3 v = Vector3.Cross(direction, u);

            // Elbow(theta) = center + radius (u cos + v sin). Maximise the
            // posterior component subject to the forearm staying inside the
            // wrist swing cone around the hand long axis:
            //   dot(wrist - elbow, handLong) >= forearmLength * cos(budget)
            //   <=> radius (a cos + b sin) <= k
            Vector3 posterior = -thoraxForward;
            float pu = Vector3.Dot(u, posterior);
            float pv = Vector3.Dot(v, posterior);
            float a = radius * Vector3.Dot(u, handLong);
            float b = radius * Vector3.Dot(v, handLong);
            float k = Vector3.Dot(wrist - center, handLong) -
                forearmLength * Mathf.Cos(wristSwingBudgetDeg * Mathf.Deg2Rad);
            float best = Mathf.Atan2(pv, pu);
            if (a * Mathf.Cos(best) + b * Mathf.Sin(best) > k + 1e-7f)
            {
                float m = Mathf.Sqrt(a * a + b * b);
                if (m < 1e-7f || Mathf.Abs(k) > m)
                    return false;
                float phi = Mathf.Atan2(b, a);
                float spread = Mathf.Acos(Mathf.Clamp(k / m, -1f, 1f));
                float first = phi + spread;
                float second = phi - spread;
                best = pu * Mathf.Cos(first) + pv * Mathf.Sin(first) >=
                    pu * Mathf.Cos(second) + pv * Mathf.Sin(second)
                    ? first
                    : second;
            }

            Vector3 elbow = center + radius * (u * Mathf.Cos(best) + v * Mathf.Sin(best));
            side = BuildSide(
                upperArmFrame, forearmFrame, handFrame, shoulder, elbow, wrist, handRot,
                thoraxForward, isLeft, wrist + handRot * palmCenterLocal, gripOnShaftAxis, axis);
            return true;
        }

        /// <summary>
        /// Hand bone rotation whose +Y (measured long axis) is
        /// <paramref name="handLong"/> and whose measured palmar normal, made
        /// perpendicular to +Y, is <paramref name="palmNormal"/>.
        /// </summary>
        public static Quaternion HandRotationFromMeasuredPalm(
            Vector3 handLong,
            Vector3 palmNormal,
            Vector3 palmNormalLocal)
        {
            Vector3 localNormal = Vector3.ProjectOnPlane(palmNormalLocal, Vector3.up).normalized;
            Quaternion local = Quaternion.LookRotation(Vector3.Cross(localNormal, Vector3.up), Vector3.up);
            Vector3 worldNormal = Vector3.ProjectOnPlane(palmNormal, handLong).normalized;
            Quaternion world = Quaternion.LookRotation(Vector3.Cross(worldNormal, handLong), handLong);
            return world * Quaternion.Inverse(local);
        }

        private static SquatReferenceUpperLimbSide BuildSide(
            SquatReferenceBoneFrame upperArmFrame,
            SquatReferenceBoneFrame forearmFrame,
            SquatReferenceBoneFrame handFrame,
            Vector3 shoulder,
            Vector3 elbow,
            Vector3 wrist,
            Quaternion handRot,
            Vector3 thoraxForward,
            bool isLeft,
            Vector3 palmCenterWorld,
            Vector3 gripOnShaftAxis,
            Vector3 barAxis)
        {
            Vector3 upperDir = (elbow - shoulder).normalized;
            Vector3 forearmDir = (wrist - elbow).normalized;
            Vector3 upperBoneForward = Vector3.ProjectOnPlane(thoraxForward, upperDir).normalized;
            Vector3 upperBoneRight = Vector3.Cross(upperDir, upperBoneForward).normalized;
            Quaternion baseUpperRot = FromAxes(upperBoneRight, upperDir, upperBoneForward);
            float humerusSignedDeg = isLeft ? -HumerusAxialDeg : HumerusAxialDeg;
            Quaternion upperRot = Quaternion.AngleAxis(humerusSignedDeg, upperDir) * baseUpperRot;
            Quaternion handBindRelativeToForearm =
                Quaternion.Inverse(forearmFrame.BindRotation) * handFrame.BindRotation;
            Quaternion forearmRot = handRot * Quaternion.Inverse(handBindRelativeToForearm);
            float forearmHandAngleDeg = Vector3.Angle(forearmDir, handRot * Vector3.up);
            Vector3 radialOffset = Vector3.ProjectOnPlane(palmCenterWorld - gripOnShaftAxis, barAxis);
            float palmBarSurfaceErrorM = Mathf.Abs(radialOffset.magnitude - BarRadiusM);
            return new SquatReferenceUpperLimbSide(
                upperRot, forearmRot, handRot,
                shoulder, elbow, wrist,
                forearmHandAngleDeg, palmBarSurfaceErrorM);
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
            Vector3 barAxis,
            Vector3 palmNormalTarget,
            Vector3 palmOffset,
            bool isLeft)
        {
            float upperLength = Vector3.Distance(forearmFrame.BindPosition, upperArmFrame.BindPosition);
            float forearmLength = Vector3.Distance(handFrame.BindPosition, forearmFrame.BindPosition);

            float sideSign = isLeft ? -1f : 1f;
            Vector3 poleHint = -thoraxUp * 0.95f - thoraxForward * 0.04f + thoraxRight * (sideSign * 0.30f);

            Vector3 wristTarget = barGripCenterline;
            Vector3 elbow = shoulder;
            Vector3 solvedHand = shoulder;
            Vector3 upperDir = (solvedHand - shoulder).normalized;
            Vector3 forearmFinalDir = (solvedHand - elbow).normalized;
            Quaternion handRot = Quaternion.identity;
            Vector3 palmCenterWorld = barGripCenterline;

            void SolveWrist(Vector3 normalTarget)
            {
                for (int iteration = 0; iteration < 6; iteration++)
                {
                    SolveTwoBone(
                        shoulder, wristTarget, upperLength, forearmLength, poleHint,
                        out elbow, out solvedHand);

                    upperDir = (elbow - shoulder).normalized;
                    forearmFinalDir = (solvedHand - elbow).normalized;
                    handRot = HandRotation(forearmFinalDir, normalTarget, isLeft);
                    Vector3 actualPalmNormal = handRot * (isLeft ? Vector3.left : Vector3.right);
                    palmCenterWorld = solvedHand + handRot * palmOffset;
                    Vector3 desiredPalm = barGripCenterline - actualPalmNormal * BarRadiusM;
                    Vector3 error = desiredPalm - palmCenterWorld;
                    if (error.sqrMagnitude < 0.00000025f)
                        break;
                    wristTarget += error;
                }
            }

            // Place the palm surface on the reference shaft. A few fixed-point
            // iterations converge because the palm offset is small relative to
            // the arm length.
            SolveWrist(palmNormalTarget);

            // Upper arm: longitudinal axis (+Y) along the humerus, anterior
            // surface (+Z, biceps) facing forward.
            Vector3 upperBoneForward = Vector3.ProjectOnPlane(thoraxForward, upperDir).normalized;
            Vector3 upperBoneRight = Vector3.Cross(upperDir, upperBoneForward).normalized;
            Quaternion baseUpperRot = FromAxes(upperBoneRight, upperDir, upperBoneForward);
            float humerusSignedDeg = isLeft ? -HumerusAxialDeg : HumerusAxialDeg;
            Quaternion upperRot = Quaternion.AngleAxis(humerusSignedDeg, upperDir) * baseUpperRot;

            // The forearm follows the hand through their bind relationship, so
            // the wrist keeps the authored neutral the reference rig carries.
            Quaternion handBindRelativeToForearm =
                Quaternion.Inverse(forearmFrame.BindRotation) * handFrame.BindRotation;
            Quaternion forearmRot = handRot * Quaternion.Inverse(handBindRelativeToForearm);

            float forearmHandAngleDeg = Vector3.Angle(forearmFinalDir, handRot * Vector3.up);
            Vector3 radialOffset = Vector3.ProjectOnPlane(palmCenterWorld - barGripCenterline, barAxis);
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
