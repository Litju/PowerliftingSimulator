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
                Assert.That(IsFinitePositive(joint.PrimaryAxisWorld), Is.True,
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

        private static bool IsFinitePositive(Vector3 value) =>
            float.IsFinite(value.x) && value.x > 0f &&
            float.IsFinite(value.y) && value.y > 0f &&
            float.IsFinite(value.z) && value.z > 0f;

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
