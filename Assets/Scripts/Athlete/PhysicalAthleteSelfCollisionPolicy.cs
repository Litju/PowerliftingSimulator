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
        /// Explicit qualified non-adjacent pair exclusions.
        /// Bilateral forearms penetrate the coarse thorax proxy box during accepted squat setup
        /// even though visible humanoid mesh has clearance.
        /// </summary>
        public static readonly IReadOnlyList<QualifiedIgnoredPair> QualifiedIgnoredPairs = new[]
        {
            new QualifiedIgnoredPair("left_forearm", "thorax", CoarseProxyFalsePositiveReason),
            new QualifiedIgnoredPair("right_forearm", "thorax", CoarseProxyFalsePositiveReason)
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
