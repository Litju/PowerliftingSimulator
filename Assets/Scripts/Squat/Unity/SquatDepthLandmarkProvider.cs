using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public readonly struct SquatJointCenterDepthDiagnostic
    {
        public SquatJointCenterDepthDiagnostic(float leftDepthM, float rightDepthM)
        {
            LeftDepthM = leftDepthM;
            RightDepthM = rightDepthM;
            WorstSideDepthM = Mathf.Max(leftDepthM, rightDepthM);
        }

        public float LeftDepthM { get; }
        public float RightDepthM { get; }
        public float WorstSideDepthM { get; }
    }

    public readonly struct SquatRuleLandmarkSet
    {
        public SquatRuleLandmarkSet(
            Vector3 leftHipCreaseWorld,
            Vector3 rightHipCreaseWorld,
            Vector3 leftKneeTopWorld,
            Vector3 rightKneeTopWorld,
            SquatDepthObservation depth,
            SquatJointCenterDepthDiagnostic jointCenterDepthDiagnostic)
        {
            LeftHipCreaseWorld = leftHipCreaseWorld;
            RightHipCreaseWorld = rightHipCreaseWorld;
            LeftKneeTopWorld = leftKneeTopWorld;
            RightKneeTopWorld = rightKneeTopWorld;
            Depth = depth;
            JointCenterDepthDiagnostic = jointCenterDepthDiagnostic;
        }

        public Vector3 LeftHipCreaseWorld { get; }
        public Vector3 RightHipCreaseWorld { get; }
        public Vector3 LeftKneeTopWorld { get; }
        public Vector3 RightKneeTopWorld { get; }
        public SquatDepthObservation Depth { get; }
        public SquatJointCenterDepthDiagnostic JointCenterDepthDiagnostic { get; }
    }

    /// <summary>
    /// Read-only GAM-10 surface-proxy evaluation shared by the reference and
    /// physical squat poses. It stores the existing offsets and allocates
    /// nothing while evaluating valid runtime frames.
    /// </summary>
    public readonly struct SquatDepthLandmarkProvider
    {
        private readonly Vector3 _leftHipCenterOffsetInPelvisFrame;
        private readonly Vector3 _rightHipCenterOffsetInPelvisFrame;
        private readonly Vector3 _leftHipCreaseOffsetInPelvisFrame;
        private readonly Vector3 _rightHipCreaseOffsetInPelvisFrame;
        private readonly Vector3 _leftKneeTopOffsetInShankFrame;
        private readonly Vector3 _rightKneeTopOffsetInShankFrame;

        public SquatDepthLandmarkProvider(SquatReferenceRigCalibration calibration)
        {
            if (calibration == null)
                throw new System.ArgumentNullException(nameof(calibration));

            _leftHipCenterOffsetInPelvisFrame = calibration.LeftHipOffsetInPelvisFrame;
            _rightHipCenterOffsetInPelvisFrame = calibration.RightHipOffsetInPelvisFrame;
            _leftHipCreaseOffsetInPelvisFrame = calibration.LeftHipCreaseOffsetInPelvisFrame;
            _rightHipCreaseOffsetInPelvisFrame = calibration.RightHipCreaseOffsetInPelvisFrame;
            _leftKneeTopOffsetInShankFrame = calibration.LeftKneeTopOffsetInShankFrame;
            _rightKneeTopOffsetInShankFrame = calibration.RightKneeTopOffsetInShankFrame;
        }

        public bool TryEvaluate(
            Vector3 leftHipCenterWorld,
            Vector3 rightHipCenterWorld,
            Quaternion pelvisFrameWorldRotation,
            Vector3 leftKneeCenterWorld,
            Quaternion leftShankFrameWorldRotation,
            Vector3 rightKneeCenterWorld,
            Quaternion rightShankFrameWorldRotation,
            out SquatRuleLandmarkSet landmarks)
        {
            landmarks = default;
            if (!IsFinite(leftHipCenterWorld) || !IsFinite(rightHipCenterWorld) ||
                !IsFinite(leftKneeCenterWorld) || !IsFinite(rightKneeCenterWorld) ||
                !TryNormalize(pelvisFrameWorldRotation, out Quaternion pelvisRotation) ||
                !TryNormalize(leftShankFrameWorldRotation, out Quaternion leftShankRotation) ||
                !TryNormalize(rightShankFrameWorldRotation, out Quaternion rightShankRotation))
                return false;

            // Match GAM-10's bilateral pelvis-origin reconstruction with its frozen center offsets.
            Vector3 leftPelvisOrigin = leftHipCenterWorld -
                pelvisRotation * _leftHipCenterOffsetInPelvisFrame;
            Vector3 rightPelvisOrigin = rightHipCenterWorld -
                pelvisRotation * _rightHipCenterOffsetInPelvisFrame;
            Vector3 pelvisCenterWorld = (leftPelvisOrigin + rightPelvisOrigin) * 0.5f;
            Vector3 leftHipCrease = pelvisCenterWorld + pelvisRotation * _leftHipCreaseOffsetInPelvisFrame;
            Vector3 rightHipCrease = pelvisCenterWorld + pelvisRotation * _rightHipCreaseOffsetInPelvisFrame;
            Vector3 leftKneeTop = leftKneeCenterWorld + leftShankRotation * _leftKneeTopOffsetInShankFrame;
            Vector3 rightKneeTop = rightKneeCenterWorld + rightShankRotation * _rightKneeTopOffsetInShankFrame;
            if (!IsFinite(leftHipCrease) || !IsFinite(rightHipCrease) ||
                !IsFinite(leftKneeTop) || !IsFinite(rightKneeTop))
                return false;

            SquatDepthObservation depth = SquatDepthGeometry.Evaluate(
                ToSquatPoint(leftHipCrease),
                ToSquatPoint(rightHipCrease),
                ToSquatPoint(leftKneeTop),
                ToSquatPoint(rightKneeTop),
                SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M);
            landmarks = new SquatRuleLandmarkSet(
                leftHipCrease,
                rightHipCrease,
                leftKneeTop,
                rightKneeTop,
                depth,
                new SquatJointCenterDepthDiagnostic(
                    leftHipCenterWorld.y - leftKneeCenterWorld.y,
                    rightHipCenterWorld.y - rightKneeCenterWorld.y));
            return true;
        }

        private static bool TryNormalize(Quaternion rotation, out Quaternion normalized)
        {
            normalized = default;
            if (!IsFinite(rotation))
                return false;
            float magnitudeSquared = rotation.x * rotation.x + rotation.y * rotation.y +
                rotation.z * rotation.z + rotation.w * rotation.w;
            if (!float.IsFinite(magnitudeSquared) || magnitudeSquared <= 1e-8f)
                return false;
            normalized = Quaternion.Normalize(rotation);
            return IsFinite(normalized);
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsFinite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) &&
            float.IsFinite(value.z) && float.IsFinite(value.w);

        private static SquatPoint3 ToSquatPoint(Vector3 value) =>
            new SquatPoint3(value.x, value.y, value.z);
    }
}
