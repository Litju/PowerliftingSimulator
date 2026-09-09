using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Athlete
{
    public readonly struct QualifiedIgnoredPair
    {
        public string SegmentA { get; }
        public string SegmentB { get; }
        public string Reason { get; }

        public QualifiedIgnoredPair(string segmentA, string segmentB, string reason)
        {
            SegmentA = segmentA;
            SegmentB = segmentB;
            Reason = reason;
        }

        public bool Matches(string a, string b)
        {
            return (string.Equals(SegmentA, a, StringComparison.Ordinal) && string.Equals(SegmentB, b, StringComparison.Ordinal)) ||
                   (string.Equals(SegmentA, b, StringComparison.Ordinal) && string.Equals(SegmentB, a, StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Governs self-collision filtering for the physical athlete rig.
    /// In addition to standard parent-child adjacent joint pair suppression,
    /// explicitly whitelists qualified non-adjacent proxy collider false positives
    /// with required causal justification metadata (GAM-11 Phase 5 H9F).
    /// </summary>
    public static class PhysicalAthleteSelfCollisionPolicy
    {
        public const string CoarseProxyFalsePositiveReason = "COARSE_PROXY_FALSE_POSITIVE_IN_ACCEPTED_SQUAT_SETUP";

        /// <summary>
        /// GAM-11 Phase 5H11. The thigh capsule and the abdomen box are each
        /// defensible on their own and both are registered within 3 mm of the
        /// reference landmarks they stand for, but their shape combination has
        /// no anatomy in it. A constant-radius capsule with a hemispherical cap
        /// meeting a flat-bottomed rectangular box cannot reproduce the taper
        /// and concavity that let a real thigh and a real belly slide past each
        /// other, so the two primitives interpenetrate by 83 mm on a reference
        /// path where the visible surfaces never touch and keep a 13 mm gap.
        /// </summary>
        public const string DeepSquatCoarseProxyFalsePositiveReason =
            "COARSE_RIGID_PROXY_FALSE_POSITIVE_IN_DEEP_SQUAT";

        /// <summary>
        /// Explicit qualified non-adjacent pair exclusions.
        ///
        /// Bilateral forearms penetrate the coarse thorax proxy box during
        /// accepted squat setup even though the visible humanoid mesh has
        /// clearance (Phase 5H9F).
        ///
        /// Bilateral thighs penetrate the coarse abdomen proxy box through the
        /// accepted squat descent, from 64 deg of hip flexion, while the
        /// visible thigh and torso surfaces stay 13 mm apart at the deepest
        /// commanded pose (Phase 5H11,
        /// Artifacts/Measurements/GAM-11/GAM11-5h11-v1-visible-envelope.txt).
        /// This is the first cause of dynamic squat failure identified in
        /// Phase 5H10.
        /// </summary>
        public static readonly IReadOnlyList<QualifiedIgnoredPair> QualifiedIgnoredPairs = new[]
        {
            new QualifiedIgnoredPair("left_forearm", "thorax", CoarseProxyFalsePositiveReason),
            new QualifiedIgnoredPair("right_forearm", "thorax", CoarseProxyFalsePositiveReason),
            new QualifiedIgnoredPair("left_thigh", "abdomen", DeepSquatCoarseProxyFalsePositiveReason),
            new QualifiedIgnoredPair("right_thigh", "abdomen", DeepSquatCoarseProxyFalsePositiveReason)
        };

        public static void Apply(
            IReadOnlyList<PhysicalAthleteRig.JointRuntime> joints,
            IReadOnlyDictionary<string, PhysicalAthleteRig.SegmentRuntime> segments)
        {
            if (joints == null) throw new ArgumentNullException(nameof(joints));
            if (segments == null) throw new ArgumentNullException(nameof(segments));

            // 1. Adjacent segment pairs across joints are disabled structurally
            foreach (PhysicalAthleteRig.JointRuntime joint in joints)
            {
                PhysicalAthleteRig.SegmentRuntime child = segments[joint.Recipe.ChildId];
                PhysicalAthleteRig.SegmentRuntime parent = segments[child.Recipe.ParentId];
                Physics.IgnoreCollision(parent.Collider, child.Collider, true);
            }

            // 2. Qualified non-adjacent exception pairs
            foreach (QualifiedIgnoredPair pair in QualifiedIgnoredPairs)
            {
                if (segments.TryGetValue(pair.SegmentA, out PhysicalAthleteRig.SegmentRuntime segA) &&
                    segments.TryGetValue(pair.SegmentB, out PhysicalAthleteRig.SegmentRuntime segB))
                {
                    if (segA.Collider != null && segB.Collider != null)
                    {
                        Physics.IgnoreCollision(segA.Collider, segB.Collider, true);
                    }
                }
            }
        }

        public static bool IsPairIgnoredByPolicy(string segmentA, string segmentB, out string reason)
        {
            foreach (QualifiedIgnoredPair pair in QualifiedIgnoredPairs)
            {
                if (pair.Matches(segmentA, segmentB))
                {
                    reason = pair.Reason;
                    return true;
                }
            }

            reason = null;
            return false;
        }
    }
}
