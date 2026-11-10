using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Presentation-only bar grip. It reads the physically realized hand bones
    /// and the real bar shaft, then wraps the avatar's existing skinned finger
    /// bones around the shaft. It never writes a Rigidbody, collider, joint or
    /// bar pose, and it does not create a second load path.
    ///
    /// The wrap is geometric, not a preset curl: each phalanx flexes about its
    /// own knuckle axis, toward the palmar side verified from the avatar's
    /// humanoid finger-flexion muscles, until its measured palmar pad touches
    /// the shaft surface. Flexion is bounded by standard anatomical ranges, so
    /// a hand that is not on the bar cannot curl into a fist around nothing
    /// and a finger can never bend backwards to reach it.
    /// </summary>
    [DefaultExecutionOrder(900)]
    [DisallowMultipleComponent]
    public sealed class SquatPresentationGrip : MonoBehaviour
    {
        public const string PresentationId = "GAM68_PRESENTATION_ONLY_CONTACT_WRAP_GRIP_V2";
        public const string Ownership = "presentation-only skinned fingers; no body, joint, force or bar write";

        public const float BarRadiusM = 0.0145f;

        /// <summary>
        /// Palmar skin depth below each finger bone axis, measured from the
        /// rendered SuperHero_Male skin (mean of index, middle, ring and little
        /// dominant-bone vertices along the bone's palmar +Z):
        /// proximal 14.6 mm, middle 9.5 mm, distal 9.4 mm. Re-measured by the
        /// EditMode qualification test.
        /// </summary>
        public static readonly float[] FingerPadDepthM = { 0.0146f, 0.0095f, 0.0094f };

        /// <summary>
        /// Thumb metacarpal, proximal and distal pad depth from the same skin
        /// measurement: proximal 18.8 mm, distal 12.5 mm. The metacarpal's own
        /// 37.9 mm extent is the thenar web, not the surface facing the shaft,
        /// so it uses the proximal phalanx depth.
        /// </summary>
        public static readonly float[] ThumbPadDepthM = { 0.0188f, 0.0188f, 0.0125f };

        /// <summary>
        /// Active flexion range (deg) of MCP, PIP and DIP joints (AAOS normative
        /// values: 90, 100, 90), and of the thumb CMC/MCP/IP (thumb CMC is held
        /// to the opposition arc; MCP 50, IP 80).
        /// </summary>
        public static readonly float[] FingerFlexionMaxDeg = { 90f, 100f, 90f };
        public static readonly float[] ThumbFlexionMaxDeg = { 45f, 50f, 80f };

        /// <summary>Palm gap beyond which the hand is treated as off the bar (one palm span).</summary>
        public const float OffBarPalmGapM = SquatPalmSurface.MeasuredPalmSpanMeters;

        private const float FingerResponseRate = 18f;
        private const int SegmentSamples = 6;

        private static readonly string[] FingerNames = { "index", "middle", "ring", "pinky", "thumb" };

        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _barbell;
        private Animator _visibleAnimator;
        private HandPresentation _left;
        private HandPresentation _right;
        private int _physicalBodyCountAtBuild;
        private int _physicalJointCountAtBuild;

        public string CalibrationId => SquatPalmSurface.CalibrationId;
        public bool IsCalibrated => _left != null && _right != null;
        public float LeftPalmGapM => _left != null ? _left.PalmGapM : float.NaN;
        public float RightPalmGapM => _right != null ? _right.PalmGapM : float.NaN;
        public float LeftPalmNormalErrorDeg => _left != null ? _left.PalmNormalErrorDeg : float.NaN;
        public float RightPalmNormalErrorDeg => _right != null ? _right.PalmNormalErrorDeg : float.NaN;
        public int PhysicalBodyCountAtBuild => _physicalBodyCountAtBuild;
        public int PhysicalJointCountAtBuild => _physicalJointCountAtBuild;

        /// <summary>Per finger (index, middle, ring, little, thumb): fingertip pad clearance to the shaft surface (m, negative = penetration).</summary>
        public IReadOnlyList<float> LeftTipClearancesM => _left != null ? _left.TipClearances : Array.Empty<float>();
        public IReadOnlyList<float> RightTipClearancesM => _right != null ? _right.TipClearances : Array.Empty<float>();

        /// <summary>Per finger: deepest pad penetration of any phalanx into the shaft (m, 0 if none; thumb phalanges only).</summary>
        public IReadOnlyList<float> LeftMaxPenetrationM => _left != null ? _left.MaxPenetrations : Array.Empty<float>();
        public IReadOnlyList<float> RightMaxPenetrationM => _right != null ? _right.MaxPenetrations : Array.Empty<float>();

        /// <summary>Per finger: total wrap angle (sum of joint flexions, deg).</summary>
        public IReadOnlyList<float> LeftWrapDeg => _left != null ? _left.WrapDegrees : Array.Empty<float>();
        public IReadOnlyList<float> RightWrapDeg => _right != null ? _right.WrapDegrees : Array.Empty<float>();

        /// <summary>Per finger and joint: applied flexion (deg), flattened finger*3+joint.</summary>
        public IReadOnlyList<float> LeftJointFlexionDeg => _left != null ? _left.JointFlexion : Array.Empty<float>();
        public IReadOnlyList<float> RightJointFlexionDeg => _right != null ? _right.JointFlexion : Array.Empty<float>();

        public void Configure(PhysicalAthleteRig rig, PhysicalBarbell barbell)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _barbell = barbell;
            _visibleAnimator = rig.VisibleAnimator;
            _physicalBodyCountAtBuild = rig.Segments.Count;
            _physicalJointCountAtBuild = rig.Joints.Count;
            _left = BuildHand(HumanBodyBones.LeftHand, isLeft: true);
            _right = BuildHand(HumanBodyBones.RightHand, isLeft: false);
        }

        private void LateUpdate()
        {
            if (_rig == null || _visibleAnimator == null || _left == null || _right == null)
                return;

            Rigidbody bar = _barbell != null ? _barbell.Body : null;
            if (bar == null || !bar.gameObject.activeInHierarchy)
            {
                Relax(_left);
                Relax(_right);
                return;
            }

            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            Vector3 axis = (bar.rotation * Vector3.right).normalized;
            PoseHand(_left, bar.position, axis, dt);
            PoseHand(_right, bar.position, axis, dt);
        }

        private void PoseHand(HandPresentation hand, Vector3 barCenter, Vector3 axis, float dt)
        {
            Vector3 palmNormalLocal = hand.IsLeft ? SquatPalmSurface.LeftPalmNormalLocal : SquatPalmSurface.RightPalmNormalLocal;
            Vector3 palmCenterLocal = hand.IsLeft ? SquatPalmSurface.LeftPalmCenterLocal : SquatPalmSurface.RightPalmCenterLocal;
            Vector3 palmCenter = hand.Hand.TransformPoint(palmCenterLocal);
            Vector3 palmNormal = (hand.Hand.rotation * palmNormalLocal).normalized;
            Vector3 radial = RadialFromAxis(palmCenter, barCenter, axis);
            float radialDistance = radial.magnitude;
            hand.PalmGapM = radialDistance - BarRadiusM;
            // Signed: 0 deg means the palmar surface faces the shaft axis.
            hand.PalmNormalErrorDeg = radialDistance > 1e-6f
                ? Vector3.Angle(palmNormal, -radial / radialDistance)
                : 0f;
            bool onBar = hand.PalmGapM < OffBarPalmGapM;
            float blend = 1f - Mathf.Exp(-FingerResponseRate * dt);

            for (int fingerIndex = 0; fingerIndex < hand.Fingers.Count; fingerIndex++)
            {
                FingerPresentation finger = hand.Fingers[fingerIndex];
                float[] pads = finger.IsThumb ? ThumbPadDepthM : FingerPadDepthM;
                float[] limits = finger.IsThumb ? ThumbFlexionMaxDeg : FingerFlexionMaxDeg;
                for (int joint = 0; joint < finger.JointCount; joint++)
                    finger.Bones[joint].localRotation = finger.BindLocalRotations[joint];

                float wrap = 0f;
                float penetration = 0f;
                for (int joint = 0; joint < finger.JointCount; joint++)
                {
                    Transform bone = finger.Bones[joint];
                    Transform child = finger.Bones[joint + 1];
                    float target = 0f;
                    if (onBar && finger.IsThumb && joint == 0)
                    {
                        target = ThumbCarpometacarpalAngle(finger, palmNormal, barCenter, axis);
                    }
                    else if (onBar)
                    {
                        Vector3 pivot = bone.position;
                        Vector3 segment = child.position - pivot;
                        Vector3 flexionAxis = FlexionAxis(finger, bone, segment, barCenter, axis);
                        target = ContactAngle(
                            pivot, segment, flexionAxis, barCenter, axis,
                            BarRadiusM + pads[Mathf.Min(joint, pads.Length - 1)],
                            0f,
                            limits[joint]);
                    }

                    // Closing follows the shaft immediately (lagging would let
                    // the moving shaft pass through the fingers); releasing off
                    // the bar eases open.
                    float applied = !finger.Snapped || target >= finger.Angles[joint]
                        ? target
                        : Mathf.Lerp(finger.Angles[joint], target, blend);
                    if (onBar)
                        applied = target;
                    finger.Angles[joint] = applied;
                    if (Mathf.Abs(applied) > 1e-4f)
                    {
                        Vector3 pivotAxis = finger.IsThumb && joint == 0
                            ? palmNormal
                            : FlexionAxis(finger, bone, child.position - bone.position, barCenter, axis);
                        bone.rotation = Quaternion.AngleAxis(applied, pivotAxis) * bone.rotation;
                    }
                    wrap += applied;
                }
                finger.Snapped = true;

                // The thumb metacarpal lies inside the thenar pad that rests on
                // the shaft with the palm, so only phalanges are scored.
                for (int joint = finger.IsThumb ? 1 : 0; joint < finger.JointCount; joint++)
                {
                    float pad = BarRadiusM + pads[Mathf.Min(joint, pads.Length - 1)];
                    float clearance = SegmentClearance(
                        finger.Bones[joint].position, finger.Bones[joint + 1].position, barCenter, axis, pad);
                    penetration = Mathf.Max(penetration, -clearance);
                    hand.JointFlexion[fingerIndex * 3 + joint] = finger.Angles[joint];
                }
                int last = finger.JointCount - 1;
                hand.TipClearances[fingerIndex] = RadialFromAxis(finger.Bones[last + 1].position, barCenter, axis).magnitude -
                    (BarRadiusM + pads[Mathf.Min(last, pads.Length - 1)]);
                hand.MaxPenetrations[fingerIndex] = Mathf.Max(0f, penetration);
                hand.WrapDegrees[fingerIndex] = wrap;
            }
        }

        /// <summary>
        /// The thumb's bind pose crosses the gripped shaft. The CMC joint swings
        /// the whole thumb in the palm plane (about the palmar normal) by the
        /// smallest angle, within the opposition arc, that lets the rigid thumb
        /// phalanges clear the shaft surface; if none does, the angle with the most
        /// clearance. The MCP and IP joints then close onto the shaft by contact.
        /// </summary>
        private static float ThumbCarpometacarpalAngle(
            FingerPresentation thumb, Vector3 palmNormal, Vector3 barCenter, Vector3 axis)
        {
            Vector3 pivot = thumb.Bones[0].position;
            float limit = ThumbFlexionMaxDeg[0];
            float ChainClearance(float angle)
            {
                Quaternion turn = Quaternion.AngleAxis(angle, palmNormal);
                float minimum = float.PositiveInfinity;
                // Phalanges only: the metacarpal lies in the thenar pad that
                // rests on the shaft with the palm.
                for (int joint = 1; joint < thumb.JointCount; joint++)
                {
                    Vector3 from = pivot + turn * (thumb.Bones[joint].position - pivot);
                    Vector3 to = pivot + turn * (thumb.Bones[joint + 1].position - pivot);
                    minimum = Mathf.Min(minimum, SegmentClearance(
                        from, to, barCenter, axis, BarRadiusM + ThumbPadDepthM[joint]));
                }
                return minimum;
            }

            if (ChainClearance(0f) >= 0f)
                return 0f;
            float bestAngle = 0f;
            float bestClearance = float.NegativeInfinity;
            for (float step = 1f; step <= limit; step += 1f)
            {
                foreach (float angle in new[] { step, -step })
                {
                    float clearance = ChainClearance(angle);
                    if (clearance >= 0f)
                        return angle;
                    if (clearance > bestClearance)
                    {
                        bestClearance = clearance;
                        bestAngle = angle;
                    }
                }
            }
            return bestAngle;
        }

        /// <summary>
        /// Flexion axis of one phalanx. For the fingers it is the knuckle axis
        /// perpendicular to the segment and to the bone's palmar +Z, signed so
        /// positive flexion moves the segment palmward (verified against the
        /// avatar's humanoid finger-curl muscles). The thumb opposes across the
        /// shaft, so its axis turns the segment toward the shaft axis.
        /// </summary>
        private static Vector3 FlexionAxis(
            FingerPresentation finger, Transform bone, Vector3 segment, Vector3 barCenter, Vector3 axis)
        {
            Vector3 toward = finger.IsThumb
                ? -RadialFromAxis(bone.position, barCenter, axis)
                : bone.rotation * Vector3.forward;
            Vector3 flexion = Vector3.Cross(segment, toward);
            if (flexion.sqrMagnitude < 1e-12f)
                flexion = bone.rotation * Vector3.right;
            return flexion.normalized;
        }

        /// <summary>
        /// The flexion at which the phalanx first touches the shaft surface.
        /// If it cannot touch within its range it closes as far toward the
        /// shaft as the range allows; if it starts inside the surface (thumb
        /// only) it opens by the smallest angle that clears it.
        /// </summary>
        private static float ContactAngle(
            Vector3 pivot,
            Vector3 segment,
            Vector3 flexionAxis,
            Vector3 barCenter,
            Vector3 axis,
            float contactRadius,
            float minimumDeg,
            float maximumDeg)
        {
            float Clearance(float angle) =>
                SegmentClearance(pivot, pivot + Quaternion.AngleAxis(angle, flexionAxis) * segment, barCenter, axis, contactRadius);

            float start = Clearance(0f);
            if (start < 0f)
            {
                for (float step = 1f; step <= Mathf.Max(maximumDeg, -minimumDeg); step += 1f)
                {
                    if (step <= maximumDeg && Clearance(step) >= 0f)
                        return Refine(Clearance, step - 1f, step, opening: false);
                    if (-step >= minimumDeg && Clearance(-step) >= 0f)
                        return Refine(Clearance, -step + 1f, -step, opening: false);
                }
                return 0f;
            }

            float bestAngle = 0f;
            float bestClearance = start;
            for (float angle = 1f; angle <= maximumDeg; angle += 1f)
            {
                float clearance = Clearance(angle);
                if (clearance <= 0f)
                    return Refine(Clearance, angle - 1f, angle, opening: true);
                if (clearance < bestClearance)
                {
                    bestClearance = clearance;
                    bestAngle = angle;
                }
            }
            return bestAngle;
        }

        private static float Refine(Func<float, float> clearance, float outside, float inside, bool opening)
        {
            // Bisect between an angle with positive clearance and one without.
            float a = opening ? outside : inside;
            float b = opening ? inside : outside;
            for (int iteration = 0; iteration < 10; iteration++)
            {
                float mid = 0.5f * (a + b);
                if (clearance(mid) >= 0f)
                    a = mid;
                else
                    b = mid;
            }
            return a;
        }

        private static float SegmentClearance(Vector3 from, Vector3 to, Vector3 barCenter, Vector3 axis, float contactRadius)
        {
            float minimum = float.PositiveInfinity;
            for (int sample = 1; sample <= SegmentSamples; sample++)
            {
                Vector3 point = Vector3.Lerp(from, to, sample / (float)SegmentSamples);
                minimum = Mathf.Min(minimum, RadialFromAxis(point, barCenter, axis).magnitude);
            }
            return minimum - contactRadius;
        }

        private static Vector3 RadialFromAxis(Vector3 point, Vector3 shaftOrigin, Vector3 axis)
        {
            Vector3 delta = point - shaftOrigin;
            return delta - axis * Vector3.Dot(delta, axis);
        }

        private void Relax(HandPresentation hand)
        {
            if (hand == null)
                return;
            foreach (FingerPresentation finger in hand.Fingers)
            {
                for (int joint = 0; joint < finger.JointCount; joint++)
                {
                    finger.Angles[joint] = 0f;
                    finger.Bones[joint].localRotation = finger.BindLocalRotations[joint];
                }
                finger.Snapped = false;
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
            foreach (string name in FingerNames)
            {
                Transform root = null;
                for (int childIndex = 0; childIndex < hand.childCount; childIndex++)
                {
                    Transform child = hand.GetChild(childIndex);
                    if (child.name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    {
                        root = child;
                        break;
                    }
                }
                if (root == null)
                    throw new InvalidOperationException($"The visible {handBone} has no {name} chain.");

                var bones = new List<Transform>(4);
                Transform current = root;
                while (current != null && bones.Count < 4)
                {
                    bones.Add(current);
                    Transform next = null;
                    for (int index = 0; index < current.childCount; index++)
                    {
                        if (current.GetChild(index).name.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                        {
                            next = current.GetChild(index);
                            break;
                        }
                    }
                    current = next;
                }
                if (bones.Count < 4)
                    throw new InvalidOperationException(
                        $"The visible {name} chain on {handBone} needs three phalanges and a tip; found {bones.Count} bones.");
                presentation.Fingers.Add(new FingerPresentation(
                    string.Equals(name, "thumb", StringComparison.Ordinal), bones.ToArray()));
            }
            return presentation;
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
            public float PalmGapM { get; set; } = float.NaN;
            public float PalmNormalErrorDeg { get; set; } = float.NaN;
            public List<FingerPresentation> Fingers { get; } = new List<FingerPresentation>(5);
            public float[] TipClearances { get; } = new float[5];
            public float[] MaxPenetrations { get; } = new float[5];
            public float[] WrapDegrees { get; } = new float[5];
            public float[] JointFlexion { get; } = new float[15];
        }

        private sealed class FingerPresentation
        {
            public FingerPresentation(bool isThumb, Transform[] bones)
            {
                IsThumb = isThumb;
                Bones = bones;
                BindLocalRotations = new Quaternion[bones.Length];
                for (int index = 0; index < bones.Length; index++)
                    BindLocalRotations[index] = bones[index].localRotation;
                Angles = new float[JointCount];
            }

            public bool IsThumb { get; }

            /// <summary>Three phalanges followed by the tip (leaf) bone.</summary>
            public Transform[] Bones { get; }
            public int JointCount => 3;
            public Quaternion[] BindLocalRotations { get; }
            public float[] Angles { get; }
            public bool Snapped { get; set; }
        }
    }
}
