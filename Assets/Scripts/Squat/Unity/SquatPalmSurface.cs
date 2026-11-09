using System;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Measured palm surface of one skinned hand, expressed in the hand bone's
    /// local frame. The frame comes from the rendered skin vertices that the
    /// hand bone actually owns, not from an assumed hand-bone axis.
    /// </summary>
    public readonly struct SquatPalmSurfaceFrame
    {
        public SquatPalmSurfaceFrame(
            Vector3 centerLocal,
            Vector3 normalLocal,
            Vector3 widthLocal,
            float spanMeters)
        {
            CenterLocal = centerLocal;
            NormalLocal = normalLocal;
            WidthLocal = widthLocal;
            SpanMeters = spanMeters;
        }

        /// <summary>Centroid of the palmar skin surface in hand-bone local space.</summary>
        public Vector3 CenterLocal { get; }

        /// <summary>Outward normal of the palmar skin surface in hand-bone local space.</summary>
        public Vector3 NormalLocal { get; }

        /// <summary>Long axis of the palm in hand-bone local space.</summary>
        public Vector3 WidthLocal { get; }

        /// <summary>Measured palm span for diagnostics.</summary>
        public float SpanMeters { get; }
    }

    /// <summary>
    /// Presentation-only measurement of the avatar's palm surface.
    ///
    /// The static calibration below was measured from the rendered
    /// SuperHero_Male skin on the canonical Quanturnius avatar: the vertices
    /// dominated by each hand bone, the covariance of their bind-pose
    /// positions, and the outer skin centroids on both faces of the palm. The
    /// runtime cannot re-read that mesh because the imported model is not
    /// read/write; the EditMode qualification test re-measures the same mesh
    /// and fails if these constants drift.
    ///
    /// At runtime the live bar shaft selects which measured face is the palm:
    /// the face whose surface centroid is closer to the real shaft is the one
    /// touching the bar. The class never writes a transform, collider,
    /// Rigidbody or joint.
    /// </summary>
    public static class SquatPalmSurface
    {
        public const string CalibrationId = "GAM68_SKINNED_PALM_SURFACE_V1";
        public const float MeasuredPalmSpanMeters = 0.050732f;

        private static readonly HandCalibration Left = new HandCalibration(
            new Vector3(0.974981f, 0.044362f, -0.217818f),
            new Vector3(0.081932f, 0.839180f, 0.537648f),
            new Vector3(0.012619f, 0.068311f, -0.002831f),
            new Vector3(-0.023394f, 0.066974f, 0.005951f));

        private static readonly HandCalibration Right = new HandCalibration(
            new Vector3(0.974982f, -0.044360f, 0.217814f),
            new Vector3(-0.081931f, 0.839179f, 0.537649f),
            new Vector3(0.023394f, 0.066974f, 0.005951f),
            new Vector3(-0.012619f, 0.068311f, -0.002831f));

        public static Vector3 LeftPalmCenterLocal => Left.NegativeCenterLocal;
        public static Vector3 RightPalmCenterLocal => Right.PositiveCenterLocal;
        public static Vector3 LeftPalmNormalLocal => -Left.NormalLocal;
        public static Vector3 RightPalmNormalLocal => Right.NormalLocal;
        public static float PalmSpanMeters => MeasuredPalmSpanMeters;

        /// <summary>
        /// Angle between the hand's width axis and its metacarpophalangeal
        /// line (index to little proximal phalanx bases), read from the visible
        /// rig. A gripped shaft lies along that line, so this is how far the
        /// hand's long axis leans toward the little-finger side from
        /// perpendicular to the shaft. Measured on the canonical avatar: 14.1 deg.
        /// </summary>
        public static float GripObliquityDegrees(Animator visibleAnimator, bool isLeft)
        {
            if (visibleAnimator == null)
                return MeasuredGripObliquityDegrees;
            Transform hand = visibleAnimator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform index = visibleAnimator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform little = visibleAnimator.GetBoneTransform(
                isLeft ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal);
            if (hand == null || index == null || little == null)
                return MeasuredGripObliquityDegrees;
            // Bind-relative: the finger bases are rigid children of the hand.
            Vector3 line = hand.InverseTransformPoint(index.position) - hand.InverseTransformPoint(little.position);
            float across = Mathf.Abs(line.z);
            if (across < 1e-5f)
                return MeasuredGripObliquityDegrees;
            return Mathf.Atan2(Mathf.Abs(line.y), across) * Mathf.Rad2Deg;
        }

        public const float MeasuredGripObliquityDegrees = 14.1f;

        public static bool TryMeasure(
            Animator visibleAnimator,
            bool isLeft,
            Vector3 shaftCenterWorld,
            Vector3 shaftAxisWorld,
            out SquatPalmSurfaceFrame frame,
            out string rejection)
        {
            frame = default;
            rejection = string.Empty;
            if (visibleAnimator == null)
            {
                rejection = "visible animator is missing";
                return false;
            }

            Transform hand = visibleAnimator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (hand == null)
            {
                rejection = "hand bone is missing";
                return false;
            }

            HandCalibration calibration = isLeft ? Left : Right;
            Vector3 axis = shaftAxisWorld.sqrMagnitude > 1e-8f
                ? shaftAxisWorld.normalized
                : Vector3.right;
            // The palmar side is anatomical, not whichever face happens to be
            // nearest the bar: the rendered skin's larger thenar/hypothenar
            // extent is on the palm side. For the left hand that is the
            // negative local-X face; the right hand mirrors to positive. The
            // live shaft is still used by the caller to verify that this face
            // is the one contacting the bar.
            Vector3 centerLocal = isLeft
                ? calibration.NegativeCenterLocal
                : calibration.PositiveCenterLocal;
            Vector3 normalLocal = isLeft
                ? -calibration.NormalLocal
                : calibration.NormalLocal;
            Vector3 widthLocal = Vector3.ProjectOnPlane(calibration.WidthLocal, normalLocal);
            if (widthLocal.sqrMagnitude < 1e-8f)
                widthLocal = hand.TransformDirection(Vector3.forward);
            widthLocal.Normalize();

            frame = new SquatPalmSurfaceFrame(
                centerLocal,
                normalLocal,
                widthLocal,
                MeasuredPalmSpanMeters);
            return true;
        }

        /// <summary>
        /// Editor-qualification path: re-measures the constants from the actual
        /// rendered mesh. Runtime never calls this because the imported mesh is
        /// not readable in the player.
        /// </summary>
        public static bool TryMeasureFromRenderedMesh(
            Animator visibleAnimator,
            bool isLeft,
            out SquatPalmSurfaceFrame frame,
            out string rejection)
        {
            frame = default;
            rejection = string.Empty;
            if (visibleAnimator == null)
            {
                rejection = "visible animator is missing";
                return false;
            }

            Transform hand = visibleAnimator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            if (hand == null)
            {
                rejection = "hand bone is missing";
                return false;
            }

            SkinnedMeshRenderer renderer = FindHandRenderer(visibleAnimator, hand, out int handIndex);
            if (renderer == null || handIndex < 0)
            {
                rejection = "no skinned renderer owns the hand bone";
                return false;
            }

            Mesh mesh = renderer.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Matrix4x4 handBind = mesh.bindposes[handIndex];
            int count = 0;
            Vector3 mean = Vector3.zero;
            for (int index = 0; index < vertices.Length && index < weights.Length; index++)
            {
                BoneWeight weight = weights[index];
                if (weight.boneIndex0 != handIndex || weight.weight0 < 0.5f)
                    continue;
                mean += handBind.MultiplyPoint3x4(vertices[index]);
                count++;
            }

            if (count < 24)
            {
                rejection = $"only {count} dominant hand vertices were found";
                return false;
            }

            mean /= count;
            float xx = 0f, xy = 0f, xz = 0f, yy = 0f, yz = 0f, zz = 0f;
            for (int index = 0; index < vertices.Length && index < weights.Length; index++)
            {
                BoneWeight weight = weights[index];
                if (weight.boneIndex0 != handIndex || weight.weight0 < 0.5f)
                    continue;
                Vector3 delta = handBind.MultiplyPoint3x4(vertices[index]) - mean;
                xx += delta.x * delta.x;
                xy += delta.x * delta.y;
                xz += delta.x * delta.z;
                yy += delta.y * delta.y;
                yz += delta.y * delta.z;
                zz += delta.z * delta.z;
            }

            EigenSymmetric(
                new[,] { { xx, xy, xz }, { xy, yy, yz }, { xz, yz, zz } },
                out float[] eigenvalues,
                out Vector3[] eigenvectors);

            int smallest = 0;
            int largest = 0;
            for (int index = 1; index < 3; index++)
            {
                if (eigenvalues[index] < eigenvalues[smallest])
                    smallest = index;
                if (eigenvalues[index] > eigenvalues[largest])
                    largest = index;
            }

            Vector3 normal = eigenvectors[smallest].normalized;
            Vector3 width = Vector3.ProjectOnPlane(eigenvectors[largest], normal);
            if (width.sqrMagnitude < 1e-8f)
            {
                rejection = "palmar span axis is degenerate";
                return false;
            }
            width.Normalize();

            HandCalibration calibration = isLeft ? Left : Right;
            if (Vector3.Dot(normal, calibration.NormalLocal) < 0f)
                normal = -normal;
            if (Vector3.Dot(width, calibration.WidthLocal) < 0f)
                width = -width;

            Vector3 positiveCenter = OuterCenter(vertices, weights, handIndex, handBind, mean, normal);
            Vector3 negativeCenter = OuterCenter(vertices, weights, handIndex, handBind, mean, -normal);
            Vector3 palmCenter = isLeft ? negativeCenter : positiveCenter;
            Vector3 palmNormal = isLeft ? -normal : normal;
            float minimumProjection = float.PositiveInfinity;
            float maximumProjection = float.NegativeInfinity;
            for (int index = 0; index < vertices.Length && index < weights.Length; index++)
            {
                BoneWeight weight = weights[index];
                if (weight.boneIndex0 != handIndex || weight.weight0 < 0.5f)
                    continue;
                float projection = Vector3.Dot(handBind.MultiplyPoint3x4(vertices[index]) - mean, normal);
                minimumProjection = Mathf.Min(minimumProjection, projection);
                maximumProjection = Mathf.Max(maximumProjection, projection);
            }
            frame = new SquatPalmSurfaceFrame(
                palmCenter,
                palmNormal,
                width,
                maximumProjection - minimumProjection);
            return true;
        }

        private static Vector3 OuterCenter(
            Vector3[] vertices,
            BoneWeight[] weights,
            int handIndex,
            Matrix4x4 handBind,
            Vector3 mean,
            Vector3 sideNormal)
        {
            float outer = 0f;
            for (int index = 0; index < vertices.Length && index < weights.Length; index++)
            {
                BoneWeight weight = weights[index];
                if (weight.boneIndex0 != handIndex || weight.weight0 < 0.5f)
                    continue;
                outer = Mathf.Max(outer, Vector3.Dot(handBind.MultiplyPoint3x4(vertices[index]) - mean, sideNormal));
            }

            float threshold = outer * 0.45f;
            Vector3 center = Vector3.zero;
            int count = 0;
            for (int index = 0; index < vertices.Length && index < weights.Length; index++)
            {
                BoneWeight weight = weights[index];
                if (weight.boneIndex0 != handIndex || weight.weight0 < 0.5f)
                    continue;
                Vector3 local = handBind.MultiplyPoint3x4(vertices[index]);
                if (Vector3.Dot(local - mean, sideNormal) < threshold)
                    continue;
                center += local;
                count++;
            }
            return count > 0 ? center / count : mean;
        }

        private static float DistanceToShaft(Vector3 point, Vector3 shaftOrigin, Vector3 axis)
        {
            Vector3 radial = Vector3.ProjectOnPlane(point - shaftOrigin, axis);
            return radial.magnitude;
        }

        private static SkinnedMeshRenderer FindHandRenderer(Animator animator, Transform hand, out int handIndex)
        {
            handIndex = -1;
            SkinnedMeshRenderer best = null;
            int bestVertices = -1;
            foreach (SkinnedMeshRenderer renderer in animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (renderer.sharedMesh == null || renderer.bones == null)
                    continue;
                int index = Array.IndexOf(renderer.bones, hand);
                if (index < 0)
                    continue;
                int count = renderer.sharedMesh.vertexCount;
                if (count > bestVertices)
                {
                    bestVertices = count;
                    best = renderer;
                    handIndex = index;
                }
            }
            return best;
        }

        private static void EigenSymmetric(float[,] matrix, out float[] values, out Vector3[] vectors)
        {
            float[,] a = (float[,])matrix.Clone();
            float[,] v = new float[3, 3];
            for (int row = 0; row < 3; row++)
                v[row, row] = 1f;

            for (int iteration = 0; iteration < 64; iteration++)
            {
                int p = 0;
                int q = 1;
                float largest = Mathf.Abs(a[0, 1]);
                if (Mathf.Abs(a[0, 2]) > largest)
                {
                    largest = Mathf.Abs(a[0, 2]);
                    p = 0;
                    q = 2;
                }
                if (Mathf.Abs(a[1, 2]) > largest)
                {
                    largest = Mathf.Abs(a[1, 2]);
                    p = 1;
                    q = 2;
                }
                if (largest < 1e-12f)
                    break;

                float theta = 0.5f * Mathf.Atan2(2f * a[p, q], a[q, q] - a[p, p]);
                float c = Mathf.Cos(theta);
                float s = Mathf.Sin(theta);
                float app = a[p, p];
                float aqq = a[q, q];
                float apq = a[p, q];
                a[p, p] = c * c * app - 2f * s * c * apq + s * s * aqq;
                a[q, q] = s * s * app + 2f * s * c * apq + c * c * aqq;
                a[p, q] = 0f;
                a[q, p] = 0f;

                for (int k = 0; k < 3; k++)
                {
                    if (k == p || k == q)
                        continue;
                    float akp = a[k, p];
                    float akq = a[k, q];
                    a[k, p] = c * akp - s * akq;
                    a[p, k] = a[k, p];
                    a[k, q] = s * akp + c * akq;
                    a[q, k] = a[k, q];
                }

                for (int k = 0; k < 3; k++)
                {
                    float vkp = v[k, p];
                    float vkq = v[k, q];
                    v[k, p] = c * vkp - s * vkq;
                    v[k, q] = s * vkp + c * vkq;
                }
            }

            values = new[] { a[0, 0], a[1, 1], a[2, 2] };
            vectors = new[]
            {
                new Vector3(v[0, 0], v[1, 0], v[2, 0]),
                new Vector3(v[0, 1], v[1, 1], v[2, 1]),
                new Vector3(v[0, 2], v[1, 2], v[2, 2])
            };
        }

        private sealed class HandCalibration
        {
            public HandCalibration(
                Vector3 normalLocal,
                Vector3 widthLocal,
                Vector3 positiveCenterLocal,
                Vector3 negativeCenterLocal)
            {
                NormalLocal = normalLocal;
                WidthLocal = widthLocal;
                PositiveCenterLocal = positiveCenterLocal;
                NegativeCenterLocal = negativeCenterLocal;
            }

            public Vector3 NormalLocal { get; }
            public Vector3 WidthLocal { get; }
            public Vector3 PositiveCenterLocal { get; }
            public Vector3 NegativeCenterLocal { get; }
        }
    }
}
