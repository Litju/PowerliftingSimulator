using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Athlete
{
    public enum PhysicalColliderKind : byte
    {
        Box,
        Capsule
    }

    public enum PhysicalJointKind : byte
    {
        Ball,
        Hinge
    }

    public readonly struct PhysicalSegmentRecipe
    {
        public PhysicalSegmentRecipe(
            string id,
            string parentId,
            HumanBodyBones proximalBone,
            HumanBodyBones distalBone,
            HumanBodyBones visibleBone,
            float massFraction,
            PhysicalColliderKind collider,
            Vector3 dimensionsMeters,
            float comFraction,
            Vector3 fixedCenterOffsetMeters = default)
        {
            Id = id;
            ParentId = parentId;
            ProximalBone = proximalBone;
            DistalBone = distalBone;
            VisibleBone = visibleBone;
            MassFraction = massFraction;
            Collider = collider;
            DimensionsMeters = dimensionsMeters;
            ComFraction = comFraction;
            FixedCenterOffsetMeters = fixedCenterOffsetMeters;
        }

        public string Id { get; }
        public string ParentId { get; }
        public HumanBodyBones ProximalBone { get; }
        public HumanBodyBones DistalBone { get; }
        public HumanBodyBones VisibleBone { get; }
        public float MassFraction { get; }
        public PhysicalColliderKind Collider { get; }
        public Vector3 DimensionsMeters { get; }
        public float ComFraction { get; }
        public Vector3 FixedCenterOffsetMeters { get; }
    }

    public readonly struct PhysicalJointRecipe
    {
        public PhysicalJointRecipe(
            string childId,
            HumanBodyBones anchorBone,
            PhysicalJointKind kind,
            Vector3 primaryAxisWorld,
            float lowDegrees,
            float highDegrees,
            float secondaryLimitDegrees,
            string family)
        {
            ChildId = childId;
            AnchorBone = anchorBone;
            Kind = kind;
            PrimaryAxisWorld = primaryAxisWorld;
            LowDegrees = lowDegrees;
            HighDegrees = highDegrees;
            SecondaryLimitDegrees = secondaryLimitDegrees;
            Family = family;
        }

        public string ChildId { get; }
        public HumanBodyBones AnchorBone { get; }
        public PhysicalJointKind Kind { get; }
        public Vector3 PrimaryAxisWorld { get; }
        public float LowDegrees { get; }
        public float HighDegrees { get; }
        public float SecondaryLimitDegrees { get; }
        public string Family { get; }
    }

    public static class PhysicalAthleteDefinition
    {
        public const float PrototypeBodyMassKg = 100f;
        public const float AnchorToleranceMeters = 0.0001f;
        public const string ProfileId = "GAM6_QUATERNIUS_100KG_GAME_CALIBRATION_V1";

        public static readonly IReadOnlyList<PhysicalSegmentRecipe> Segments = new[]
        {
            new PhysicalSegmentRecipe("pelvis", null, HumanBodyBones.Hips, HumanBodyBones.Hips, HumanBodyBones.Hips, 0.142f, PhysicalColliderKind.Box, new Vector3(0.30f, 0.20f, 0.18f), 0f, new Vector3(0f, 0.025f, 0f)),
            new PhysicalSegmentRecipe("abdomen", "pelvis", HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Spine, 0.139f, PhysicalColliderKind.Box, new Vector3(0.25f, 0.16f, 0.17f), 0.50f),
            new PhysicalSegmentRecipe("thorax", "abdomen", HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Chest, 0.216f, PhysicalColliderKind.Box, new Vector3(0.36f, 0.28f, 0.20f), 0.48f),
            new PhysicalSegmentRecipe("head_neck", "thorax", HumanBodyBones.Neck, HumanBodyBones.Head, HumanBodyBones.Head, 0.081f, PhysicalColliderKind.Capsule, new Vector3(0.21f, 0.31f, 0.21f), 0.78f, new Vector3(0f, 0.09f, 0f)),
            new PhysicalSegmentRecipe("left_upper_arm", "thorax", HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, 0.028f, PhysicalColliderKind.Capsule, new Vector3(0.12f, 0f, 0.12f), 0.5772f),
            new PhysicalSegmentRecipe("right_upper_arm", "thorax", HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, 0.028f, PhysicalColliderKind.Capsule, new Vector3(0.12f, 0f, 0.12f), 0.5772f),
            new PhysicalSegmentRecipe("left_forearm", "left_upper_arm", HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, 0.016f, PhysicalColliderKind.Capsule, new Vector3(0.095f, 0f, 0.095f), 0.4574f),
            new PhysicalSegmentRecipe("right_forearm", "right_upper_arm", HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, 0.016f, PhysicalColliderKind.Capsule, new Vector3(0.095f, 0f, 0.095f), 0.4574f),
            new PhysicalSegmentRecipe("left_hand", "left_forearm", HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftHand, 0.006f, PhysicalColliderKind.Box, new Vector3(0.14f, 0.06f, 0.045f), 0.50f),
            new PhysicalSegmentRecipe("right_hand", "right_forearm", HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightHand, 0.006f, PhysicalColliderKind.Box, new Vector3(0.14f, 0.06f, 0.045f), 0.50f),
            new PhysicalSegmentRecipe("left_thigh", "pelvis", HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, 0.100f, PhysicalColliderKind.Capsule, new Vector3(0.18f, 0f, 0.18f), 0.4095f),
            new PhysicalSegmentRecipe("right_thigh", "pelvis", HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, 0.100f, PhysicalColliderKind.Capsule, new Vector3(0.18f, 0f, 0.18f), 0.4095f),
            new PhysicalSegmentRecipe("left_shank", "left_thigh", HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, 0.0465f, PhysicalColliderKind.Capsule, new Vector3(0.13f, 0f, 0.13f), 0.4459f),
            new PhysicalSegmentRecipe("right_shank", "right_thigh", HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, 0.0465f, PhysicalColliderKind.Capsule, new Vector3(0.13f, 0f, 0.13f), 0.4459f),
            new PhysicalSegmentRecipe("left_foot", "left_shank", HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.LeftFoot, 0.0145f, PhysicalColliderKind.Box, new Vector3(0.13f, 0.10f, 0.29f), 0.4415f, new Vector3(0f, -0.06f, 0.015f)),
            new PhysicalSegmentRecipe("right_foot", "right_shank", HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HumanBodyBones.RightFoot, 0.0145f, PhysicalColliderKind.Box, new Vector3(0.13f, 0.10f, 0.29f), 0.4415f, new Vector3(0f, -0.06f, 0.015f))
        };

        public static readonly IReadOnlyList<PhysicalJointRecipe> Joints = new[]
        {
            new PhysicalJointRecipe("abdomen", HumanBodyBones.Spine, PhysicalJointKind.Ball, Vector3.right, -35f, 45f, 25f, "lumbar"),
            new PhysicalJointRecipe("thorax", HumanBodyBones.Chest, PhysicalJointKind.Ball, Vector3.right, -35f, 50f, 30f, "trunk"),
            new PhysicalJointRecipe("head_neck", HumanBodyBones.Neck, PhysicalJointKind.Ball, Vector3.right, -45f, 55f, 45f, "neck"),
            new PhysicalJointRecipe("left_upper_arm", HumanBodyBones.LeftUpperArm, PhysicalJointKind.Ball, Vector3.forward, -100f, 100f, 105f, "shoulder"),
            new PhysicalJointRecipe("right_upper_arm", HumanBodyBones.RightUpperArm, PhysicalJointKind.Ball, Vector3.forward, -100f, 100f, 105f, "shoulder"),
            new PhysicalJointRecipe("left_forearm", HumanBodyBones.LeftLowerArm, PhysicalJointKind.Hinge, Vector3.forward, -5f, 145f, 0f, "elbow"),
            new PhysicalJointRecipe("right_forearm", HumanBodyBones.RightLowerArm, PhysicalJointKind.Hinge, Vector3.forward, -5f, 145f, 0f, "elbow"),
            new PhysicalJointRecipe("left_hand", HumanBodyBones.LeftHand, PhysicalJointKind.Ball, Vector3.forward, -70f, 70f, 30f, "wrist"),
            new PhysicalJointRecipe("right_hand", HumanBodyBones.RightHand, PhysicalJointKind.Ball, Vector3.forward, -70f, 70f, 30f, "wrist"),
            new PhysicalJointRecipe("left_thigh", HumanBodyBones.LeftUpperLeg, PhysicalJointKind.Ball, Vector3.right, -120f, 45f, 50f, "hip"),
            new PhysicalJointRecipe("right_thigh", HumanBodyBones.RightUpperLeg, PhysicalJointKind.Ball, Vector3.right, -120f, 45f, 50f, "hip"),
            new PhysicalJointRecipe("left_shank", HumanBodyBones.LeftLowerLeg, PhysicalJointKind.Hinge, Vector3.right, -5f, 145f, 0f, "knee"),
            new PhysicalJointRecipe("right_shank", HumanBodyBones.RightLowerLeg, PhysicalJointKind.Hinge, Vector3.right, -5f, 145f, 0f, "knee"),
            new PhysicalJointRecipe("left_foot", HumanBodyBones.LeftFoot, PhysicalJointKind.Hinge, Vector3.right, -45f, 55f, 0f, "ankle"),
            new PhysicalJointRecipe("right_foot", HumanBodyBones.RightFoot, PhysicalJointKind.Hinge, Vector3.right, -45f, 55f, 0f, "ankle")
        };

        public static Vector3 BoxInertia(float massKg, Vector3 sizeMeters)
        {
            return new Vector3(
                massKg * (sizeMeters.y * sizeMeters.y + sizeMeters.z * sizeMeters.z) / 12f,
                massKg * (sizeMeters.x * sizeMeters.x + sizeMeters.z * sizeMeters.z) / 12f,
                massKg * (sizeMeters.x * sizeMeters.x + sizeMeters.y * sizeMeters.y) / 12f);
        }

        public static void ValidateDefinition()
        {
            if (Segments.Count != 16 || Joints.Count != 15)
                throw new InvalidOperationException("The GAM-6 physical athlete requires exactly 16 segments and 15 joints.");

            float sum = 0f;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (PhysicalSegmentRecipe segment in Segments)
            {
                if (!ids.Add(segment.Id))
                    throw new InvalidOperationException($"Duplicate physical segment '{segment.Id}'.");
                if (segment.ParentId != null && !ids.Contains(segment.ParentId))
                    throw new InvalidOperationException($"Parent '{segment.ParentId}' must precede child '{segment.Id}'.");
                sum += segment.MassFraction;
            }

            if (Mathf.Abs(sum - 1f) > 0.000001f)
                throw new InvalidOperationException($"Physical segment mass fractions sum to {sum:R}, not 1.");
        }
    }
}
