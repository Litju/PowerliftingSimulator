using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalAthleteSelfCollisionPolicyTests
    {
        private const string ExpectedReason = "COARSE_PROXY_FALSE_POSITIVE_IN_ACCEPTED_SQUAT_SETUP";

        [Test]
        public void COLLISION_POLICY_IGNORES_BILATERAL_FOREARM_THORAX_WITH_EXACT_METADATA()
        {
            Assert.That(PhysicalAthleteSelfCollisionPolicy.CoarseProxyFalsePositiveReason, Is.EqualTo(ExpectedReason));
            Assert.That(PhysicalAthleteSelfCollisionPolicy.QualifiedIgnoredPairs.Count, Is.EqualTo(2));

            QualifiedIgnoredPair left = PhysicalAthleteSelfCollisionPolicy.QualifiedIgnoredPairs[0];
            Assert.That(left.SegmentA, Is.EqualTo("left_forearm"));
            Assert.That(left.SegmentB, Is.EqualTo("thorax"));
            Assert.That(left.Reason, Is.EqualTo(ExpectedReason));

            QualifiedIgnoredPair right = PhysicalAthleteSelfCollisionPolicy.QualifiedIgnoredPairs[1];
            Assert.That(right.SegmentA, Is.EqualTo("right_forearm"));
            Assert.That(right.SegmentB, Is.EqualTo("thorax"));
            Assert.That(right.Reason, Is.EqualTo(ExpectedReason));

            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_forearm", "thorax", out string reasonL), Is.True);
            Assert.That(reasonL, Is.EqualTo(ExpectedReason));

            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("thorax", "left_forearm", out string reasonLRev), Is.True);
            Assert.That(reasonLRev, Is.EqualTo(ExpectedReason));

            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("right_forearm", "thorax", out string reasonR), Is.True);
            Assert.That(reasonR, Is.EqualTo(ExpectedReason));

            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("thorax", "right_forearm", out string reasonRRev), Is.True);
            Assert.That(reasonRRev, Is.EqualTo(ExpectedReason));
        }

        [Test]
        public void COLLISION_POLICY_DOES_NOT_IGNORE_NON_WHITELISTED_PAIRS()
        {
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_hand", "thorax", out _), Is.False);
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("right_hand", "thorax", out _), Is.False);
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_forearm", "head_neck", out _), Is.False);
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_forearm", "pelvis", out _), Is.False);
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_upper_arm", "pelvis", out _), Is.False);
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_thigh", "thorax", out _), Is.False);
            Assert.That(PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy("left_hand", "head_neck", out _), Is.False);
        }

        [Test]
        public void FOREARM_MASS_PROPERTIES_ARE_UNCHANGED()
        {
            PhysicalSegmentRecipe leftForearm = default;
            PhysicalSegmentRecipe rightForearm = default;
            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
            {
                if (segment.Id == "left_forearm") leftForearm = segment;
                if (segment.Id == "right_forearm") rightForearm = segment;
            }

            Assert.That(leftForearm.Id, Is.EqualTo("left_forearm"));
            Assert.That(leftForearm.MassFraction, Is.EqualTo(0.016f));
            Assert.That(leftForearm.DimensionsMeters, Is.EqualTo(new Vector3(0.095f, 0f, 0.095f)));
            Assert.That(leftForearm.ComFraction, Is.EqualTo(0.4574f));

            Assert.That(rightForearm.Id, Is.EqualTo("right_forearm"));
            Assert.That(rightForearm.MassFraction, Is.EqualTo(0.016f));
            Assert.That(rightForearm.DimensionsMeters, Is.EqualTo(new Vector3(0.095f, 0f, 0.095f)));
            Assert.That(rightForearm.ComFraction, Is.EqualTo(0.4574f));
        }
    }
}
