using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Presentation-only bar grip. It follows the physically realized hand
    /// bones and the real bar shaft, then poses the skinned finger bones around
    /// the shaft. It never writes a Rigidbody, collider, joint or bar pose, and
    /// it does not create a second load path.
    ///
    /// The palm frame is measured from the rendered hand skin and the live bar
    /// (see <see cref="SquatPalmSurface"/>). The fingers are not physical
    /// bodies; they are the avatar's existing skinned finger bones.
    /// </summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class SquatPresentationGrip : MonoBehaviour
    {
        public const string PresentationId = "GAM68_PRESENTATION_ONLY_BAR_GRIP_V1";
        public const string Ownership = "presentation-only skinned fingers; no body, joint, force or bar write";

        private const float BarRadiusM = 0.0145f;
        private const float FingerWrapDamping = 14f;
        private const float ThumbWrapScale = 0.72f;

        private static readonly FingerCurlProfile[] CurlProfiles =
        {
            new FingerCurlProfile("index", 52f, 68f, 44f),
            new FingerCurlProfile("middle", 56f, 72f, 46f),
            new FingerCurlProfile("ring", 56f, 71f, 45f),
            new FingerCurlProfile("pinky", 50f, 65f, 42f),
            new FingerCurlProfile("thumb", 34f, 46f, 30f)
        };

        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _barbell;
        private Animator _visibleAnimator;
        private HandPresentation _left;
        private HandPresentation _right;
        private bool _calibrated;
        private bool _calibrationWarningLogged;
        private float _lastCalibrationAttemptTime;
        private float _leftPalmGapM = float.NaN;
        private float _rightPalmGapM = float.NaN;
        private float _leftPalmNormalErrorDeg = float.NaN;
        private float _rightPalmNormalErrorDeg = float.NaN;
        private float _leftGripAmount;
        private float _rightGripAmount;
        private int _physicalBodyCountAtBuild;
        private int _physicalJointCountAtBuild;

        public string CalibrationId => SquatPalmSurface.CalibrationId;
        public bool IsCalibrated => _calibrated;
        public float LeftPalmGapM => _leftPalmGapM;
        public float RightPalmGapM => _rightPalmGapM;
        public float LeftPalmNormalErrorDeg => _leftPalmNormalErrorDeg;
        public float RightPalmNormalErrorDeg => _rightPalmNormalErrorDeg;
        public float LeftGripAmount => _leftGripAmount;
        public float RightGripAmount => _rightGripAmount;
        public int PhysicalBodyCountAtBuild => _physicalBodyCountAtBuild;
        public int PhysicalJointCountAtBuild => _physicalJointCountAtBuild;

        public void Configure(PhysicalAthleteRig rig, PhysicalBarbell barbell)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _barbell = barbell;
            _visibleAnimator = rig.VisibleAnimator;
            _physicalBodyCountAtBuild = rig.Segments.Count;
            _physicalJointCountAtBuild = rig.Joints.Count;
            _left = BuildHand(HumanBodyBones.LeftHand, isLeft: true);
            _right = BuildHand(HumanBodyBones.RightHand, isLeft: false);
            _calibrated = false;
            _calibrationWarningLogged = false;
        }

        private void LateUpdate()
        {
            if (_rig == null || _barbell == null || _visibleAnimator == null)
                return;

            Rigidbody bar = _barbell.Body;
            if (bar == null || !bar.gameObject.activeInHierarchy)
            {
                Relax(_left);
                Relax(_right);
                return;
            }

            if (!_calibrated)
                TryCalibrate(bar);
            if (!_calibrated)
                return;

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            PoseHand(_left, bar, dt);
            PoseHand(_right, bar, dt);
        }

        private void TryCalibrate(Rigidbody bar)
        {
            // Calibration is cheap but should not spam a failed measurement
            // every frame while a reload or setup transition is in progress.
            if (Time.unscaledTime - _lastCalibrationAttemptTime < 0.25f)
                return;
            _lastCalibrationAttemptTime = Time.unscaledTime;

            Vector3 axis = bar.rotation * Vector3.right;
            Vector3 shaft = bar.position;
            bool left = SquatPalmSurface.TryMeasure(
                _visibleAnimator, true, shaft, axis, out SquatPalmSurfaceFrame leftFrame, out string leftReason);
            bool right = SquatPalmSurface.TryMeasure(
                _visibleAnimator, false, shaft, axis, out SquatPalmSurfaceFrame rightFrame, out string rightReason);
            if (!left || !right)
            {
                if (!_calibrationWarningLogged)
                {
                    _calibrationWarningLogged = true;
                    Debug.LogWarning(
                        $"GAM-68 presentation grip waiting for a measured palm frame: left={leftReason}; right={rightReason}");
                }
                return;
            }

            _left.Frame = leftFrame;
            _right.Frame = rightFrame;
            CalibrateFingerAxes(_left);
            CalibrateFingerAxes(_right);
            _calibrated = true;
            Debug.Log(
                $"GAM-68 presentation grip calibrated from skinned palm surfaces and the live bar: " +
                $"left span {leftFrame.SpanMeters * 1000f:F1} mm, right span {rightFrame.SpanMeters * 1000f:F1} mm.");
        }

        private void PoseHand(HandPresentation hand, Rigidbody bar, float dt)
        {
            if (hand == null || hand.Hand == null)
                return;

            Vector3 barAxis = (bar.rotation * Vector3.right).normalized;
            Vector3 barCenter = bar.position;
            Vector3 palmCenter = hand.Hand.TransformPoint(hand.Frame.CenterLocal);
            Vector3 palmNormal = (hand.Hand.rotation * hand.Frame.NormalLocal).normalized;
            Vector3 closest = ClosestPointOnShaft(palmCenter, barCenter, barAxis);
            Vector3 radial = palmCenter - closest;
            float radialDistance = radial.magnitude;
            float gap = radialDistance - BarRadiusM;
            float normalDot = radialDistance > 1e-5f
                ? Vector3.Dot(palmNormal, -radial / radialDistance)
                : 1f;
            float proximity = 1f - Mathf.Clamp01((gap - 0.025f) / 0.060f);
            float alignment = Mathf.Clamp01(normalDot / 0.55f);
            float grip = Mathf.Clamp01(proximity * alignment);

            if (hand.IsLeft)
            {
                _leftPalmGapM = gap;
                _leftPalmNormalErrorDeg = Mathf.Acos(Mathf.Clamp(normalDot, -1f, 1f)) * Mathf.Rad2Deg;
                _leftGripAmount = grip;
            }
            else
            {
                _rightPalmGapM = gap;
                _rightPalmNormalErrorDeg = Mathf.Acos(Mathf.Clamp(normalDot, -1f, 1f)) * Mathf.Rad2Deg;
                _rightGripAmount = grip;
            }

            for (int fingerIndex = 0; fingerIndex < hand.Fingers.Count; fingerIndex++)
            {
                FingerPresentation finger = hand.Fingers[fingerIndex];
                FingerCurlProfile profile = finger.Profile;
                bool thumb = string.Equals(profile.Name, "thumb", StringComparison.Ordinal);
                float scale = grip * (thumb ? ThumbWrapScale : 1f);
                for (int joint = 0; joint < finger.Bones.Length; joint++)
                {
                    float target = profile.Angle(joint) * scale;
                    finger.CurrentAngles[joint] = Mathf.Lerp(
                        finger.CurrentAngles[joint],
                        target,
                        1f - Mathf.Exp(-FingerWrapDamping * dt));
                    Transform bone = finger.Bones[joint];
                    bone.localRotation = Quaternion.AngleAxis(
                        finger.CurrentAngles[joint],
                        finger.AxisInParentLocal[joint]) * finger.BindLocalRotations[joint];
                }
            }
        }

        private static Vector3 ClosestPointOnShaft(Vector3 point, Vector3 shaftOrigin, Vector3 axis)
        {
            float along = Vector3.Dot(point - shaftOrigin, axis);
            return shaftOrigin + axis * along;
        }

        private void CalibrateFingerAxes(HandPresentation hand)
        {
            if (hand == null)
                return;
            Vector3 palmNormalWorld = hand.Hand.rotation * hand.Frame.NormalLocal;
            for (int fingerIndex = 0; fingerIndex < hand.Fingers.Count; fingerIndex++)
            {
                FingerPresentation finger = hand.Fingers[fingerIndex];
                for (int joint = 0; joint < finger.Bones.Length; joint++)
                {
                    Transform bone = finger.Bones[joint];
                    Vector3 directionWorld = (bone.rotation * Vector3.up).normalized;
                    Vector3 flexionWorld = Vector3.Cross(directionWorld, palmNormalWorld);
                    if (flexionWorld.sqrMagnitude < 1e-8f)
                        flexionWorld = hand.Hand.right;
                    flexionWorld.Normalize();
                    finger.AxisInParentLocal[joint] =
                        Quaternion.Inverse(bone.parent.rotation) * flexionWorld;
                    finger.CurrentAngles[joint] = 0f;
                }
            }
        }

        private void Relax(HandPresentation hand)
        {
            if (hand == null)
                return;
            for (int fingerIndex = 0; fingerIndex < hand.Fingers.Count; fingerIndex++)
            {
                FingerPresentation finger = hand.Fingers[fingerIndex];
                for (int joint = 0; joint < finger.Bones.Length; joint++)
                {
                    finger.CurrentAngles[joint] = 0f;
                    finger.Bones[joint].localRotation = finger.BindLocalRotations[joint];
                }
            }
        }

        private void OnDisable()
        {
            Relax(_left);
            Relax(_right);
        }

        private HandPresentation BuildHand(HumanBodyBones handBone, bool isLeft)
        {
            Transform hand = _visibleAnimator.GetBoneTransform(handBone);
            if (hand == null)
                throw new InvalidOperationException($"The visible rig has no {handBone}.");
            var presentation = new HandPresentation(isLeft, hand);
            for (int childIndex = 0; childIndex < hand.childCount; childIndex++)
            {
                Transform child = hand.GetChild(childIndex);
                FingerCurlProfile profile = FindProfile(child.name);
                if (profile == null)
                    continue;
                var bones = new List<Transform>(3);
                Transform current = child;
                while (current != null && bones.Count < 3)
                {
                    bones.Add(current);
                    Transform next = null;
                    for (int index = 0; index < current.childCount; index++)
                    {
                        Transform candidate = current.GetChild(index);
                        if (FindProfile(candidate.name) == profile)
                        {
                            next = candidate;
                            break;
                        }
                    }
                    current = next;
                }
                if (bones.Count == 0)
                    continue;
                var finger = new FingerPresentation(profile, bones.ToArray());
                for (int joint = 0; joint < finger.Bones.Length; joint++)
                {
                    finger.BindLocalRotations[joint] = finger.Bones[joint].localRotation;
                    finger.CurrentAngles[joint] = 0f;
                }
                presentation.Fingers.Add(finger);
            }
            if (presentation.Fingers.Count == 0)
                throw new InvalidOperationException($"The visible {handBone} has no skinned finger chains.");
            return presentation;
        }

        private static FingerCurlProfile FindProfile(string boneName)
        {
            for (int index = 0; index < CurlProfiles.Length; index++)
            {
                if (boneName.StartsWith(CurlProfiles[index].Name, StringComparison.OrdinalIgnoreCase))
                    return CurlProfiles[index];
            }
            return null;
        }

        private sealed class HandPresentation
        {
            public HandPresentation(bool isLeft, Transform hand)
            {
                IsLeft = isLeft;
                Hand = hand;
            }

            public bool IsLeft { get; }
            public Transform Hand { get; }
            public SquatPalmSurfaceFrame Frame { get; set; }
            public List<FingerPresentation> Fingers { get; } = new List<FingerPresentation>(5);
        }

        private sealed class FingerPresentation
        {
            public FingerPresentation(FingerCurlProfile profile, Transform[] bones)
            {
                Profile = profile;
                Bones = bones;
                BindLocalRotations = new Quaternion[bones.Length];
                AxisInParentLocal = new Vector3[bones.Length];
                CurrentAngles = new float[bones.Length];
            }

            public FingerCurlProfile Profile { get; }
            public Transform[] Bones { get; }
            public Quaternion[] BindLocalRotations { get; }
            public Vector3[] AxisInParentLocal { get; }
            public float[] CurrentAngles { get; }
        }

        private sealed class FingerCurlProfile
        {
            private readonly float[] _angles;

            public FingerCurlProfile(string name, params float[] angles)
            {
                Name = name;
                _angles = angles;
            }

            public string Name { get; }

            public float Angle(int joint) => _angles[Mathf.Clamp(joint, 0, _angles.Length - 1)];
        }
    }
}
