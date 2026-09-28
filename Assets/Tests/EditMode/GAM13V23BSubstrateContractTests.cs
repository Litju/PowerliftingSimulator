using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13V23BSubstrateContractTests
    {
        [Test]
        public void GAM13_V23B_AUDIT_HARNESS_RECIPE_GRAPH_IS_A_SINGLE_PHYSICAL_TREE()
        {
            PhysicalAthleteDefinition.ValidateDefinition();
            var segments = new Dictionary<string, PhysicalSegmentRecipe>();
            var jointChildren = new HashSet<string>();
            int roots = 0;

            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
            {
                Assert.That(segments.TryAdd(segment.Id, segment), Is.True, $"Duplicate segment {segment.Id}.");
                if (segment.ParentId == null)
                {
                    roots++;
                    continue;
                }

                Assert.That(segments.ContainsKey(segment.ParentId), Is.True,
                    $"Parent {segment.ParentId} must precede {segment.Id}.");
                Assert.That(segment.MassFraction, Is.GreaterThan(0f));
                Assert.That(IsValidColliderDimensions(segment), Is.True,
                    $"Segment {segment.Id} has invalid collider dimensions.");
            }

            foreach (PhysicalJointRecipe joint in PhysicalAthleteDefinition.Joints)
            {
                Assert.That(jointChildren.Add(joint.ChildId), Is.True, $"Duplicate joint for {joint.ChildId}.");
                Assert.That(segments.TryGetValue(joint.ChildId, out PhysicalSegmentRecipe child), Is.True,
                    $"Joint child {joint.ChildId} is missing.");
                Assert.That(child.ParentId, Is.Not.Null);
                Assert.That(IsFiniteNonZero(joint.PrimaryAxisWorld), Is.True,
                    $"Joint {joint.ChildId} has a degenerate primary axis.");
                Assert.That(float.IsFinite(joint.LowDegrees) && float.IsFinite(joint.HighDegrees) &&
                    joint.LowDegrees < joint.HighDegrees, Is.True, $"Joint {joint.ChildId} has invalid X bounds.");
                Assert.That(joint.SecondaryLimitDegrees, Is.GreaterThanOrEqualTo(0f));
            }

            Assert.That(roots, Is.EqualTo(1));
            Assert.That(jointChildren.Count, Is.EqualTo(segments.Count - roots));
            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
            {
                if (segment.ParentId != null)
                    Assert.That(jointChildren.Contains(segment.Id), Is.True, $"Segment {segment.Id} has no joint.");
            }
        }

        [TestCase(PhysicalColliderKind.Box, 1f, 2f, 4f, 6f, 0f, 0f, 0f, 4.3333335f, 3.3333333f, 1.6666666f)]
        [TestCase(PhysicalColliderKind.Capsule, 2f, 1f, 1f, 1f, 0f, 0f, 0f, 0.2f, 0.2f, 0.2f)]
        [TestCase(PhysicalColliderKind.Capsule, 8f, 1f, 3f, 1f, 0f, 0f, 0f, 5.325f, 0.95f, 5.325f)]
        public void GAM13_V23B_PRIMITIVE_INERTIA_MATCHES_COLLIDER_GEOMETRY(
            PhysicalColliderKind kind,
            float mass,
            float x,
            float y,
            float z,
            float centerX,
            float centerY,
            float centerZ,
            float expectedX,
            float expectedY,
            float expectedZ)
        {
            Vector3 inertia = PhysicalAthleteDefinition.PrimitiveInertiaAboutBodyCenter(
                kind, mass, new Vector3(x, y, z), new Vector3(centerX, centerY, centerZ));

            Assert.That(inertia.x, Is.EqualTo(expectedX).Within(0.00001f));
            Assert.That(inertia.y, Is.EqualTo(expectedY).Within(0.00001f));
            Assert.That(inertia.z, Is.EqualTo(expectedZ).Within(0.00001f));
            Assert.That(inertia.x > 0f && inertia.y > 0f && inertia.z > 0f, Is.True);
        }

        [Test]
        public void GAM13_V23B_FOOT_COLLIDER_OFFSET_ADDS_PARALLEL_AXIS_INERTIA()
        {
            Vector3 centered = PhysicalAthleteDefinition.PrimitiveInertiaAboutBodyCenter(
                PhysicalColliderKind.Box, 1f, new Vector3(2f, 4f, 6f), Vector3.zero);
            Vector3 offset = PhysicalAthleteDefinition.PrimitiveInertiaAboutBodyCenter(
                PhysicalColliderKind.Box, 1f, new Vector3(2f, 4f, 6f), new Vector3(0f, 0.1f, 0f));

            Assert.That(offset.x - centered.x, Is.EqualTo(0.01f).Within(0.000001f));
            Assert.That(offset.y, Is.EqualTo(centered.y).Within(0.000001f));
            Assert.That(offset.z - centered.z, Is.EqualTo(0.01f).Within(0.000001f));
            Assert.That(offset, Is.EqualTo(PhysicalAthleteDefinition.PrimitiveInertiaAboutBodyCenter(
                PhysicalColliderKind.Box, 1f, new Vector3(2f, 4f, 6f), new Vector3(0f, 0.1f, 0f))));
        }

        [Test]
        public void GAM13_V23B_CAPSULE_REJECTS_GEOMETRY_PHYSX_WOULD_CLAMP()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                PhysicalAthleteDefinition.PrimitiveInertiaAboutBodyCenter(
                    PhysicalColliderKind.Capsule, 1f, new Vector3(0.2f, 0.1f, 0.2f), Vector3.zero));
        }

        [Test]
        public void GAM13_V23B_GAMEPLAY_COMMAND_CONTRACT_DOES_NOT_EXPOSE_CONFIGURABLE_JOINTS()
        {
            System.Reflection.ParameterInfo[] parameters =
                typeof(IPhysicalAthleteCommandSource).GetMethod(nameof(IPhysicalAthleteCommandSource.PrepareCommands)).GetParameters();

            Assert.That(parameters.Length, Is.EqualTo(4));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(PowerliftingSimulator.Foundation.PhysicalObservation)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(PowerliftingSimulator.Foundation.SimulationTime)));
            Assert.That(parameters[2].ParameterType, Is.EqualTo(typeof(PowerliftingSimulator.Foundation.PlayerIntentFrame)));
            Assert.That(parameters[3].ParameterType, Is.EqualTo(typeof(IPhysicalAthleteJointCommandSink)));
        }

        private static bool IsFinitePositive(Vector3 value) =>
            float.IsFinite(value.x) && value.x > 0f &&
            float.IsFinite(value.y) && value.y > 0f &&
            float.IsFinite(value.z) && value.z > 0f;

        private static bool IsFiniteNonZero(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) &&
            value.sqrMagnitude > 0.000001f;

        private static bool IsValidColliderDimensions(PhysicalSegmentRecipe segment)
        {
            Vector3 dimensions = segment.DimensionsMeters;
            return float.IsFinite(dimensions.x) && dimensions.x > 0f &&
                float.IsFinite(dimensions.y) &&
                (segment.Collider == PhysicalColliderKind.Capsule ? dimensions.y >= 0f : dimensions.y > 0f) &&
                float.IsFinite(dimensions.z) && dimensions.z > 0f;
        }
    }
}
