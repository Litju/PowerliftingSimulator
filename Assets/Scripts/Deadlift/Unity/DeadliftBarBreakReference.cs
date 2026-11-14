using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 Gate 3: explicit support-mode load schedule for the transition
    /// from the grounded pretension reference to the separated-bar boundary.
    ///
    /// The grounded C4 reference is only valid while the plates carry the bar.
    /// Once the plates unload, the hands carry the full bar weight and the joint
    /// torques must follow the C4 solution at the bar-break boundary
    /// (H = bar weight / 2, plate normals zero). This class interpolates the two
    /// already-measured C4 solutions by the measured plate-unload fraction:
    ///   tau(f) = tau_grounded + f (tau_boundary - tau_grounded),
    ///   COP_i(f) from the corresponding normal-moment interpolation.
    /// It is not an integral reference extension and it never commands a force,
    /// a body or the bar. f is supplied by the controller from measured contact.
    /// </summary>
    public sealed class DeadliftBarBreakReference
    {
        private readonly DeadliftContactStatics _grounded, _boundary;

        public DeadliftBarBreakReference(DeadliftContactStatics grounded, DeadliftContactStatics boundary)
        {
            _grounded = grounded ?? throw new ArgumentNullException(nameof(grounded));
            _boundary = boundary ?? throw new ArgumentNullException(nameof(boundary));
            if (!_grounded.Feasible)
                throw new ArgumentException("the grounded pretension statics must be feasible");
            if (_boundary.HandPullN <= _grounded.HandPullN)
                throw new ArgumentException("the boundary must carry more hand load than the grounded state");
            if (_boundary.PlateLN < -0.01f || _boundary.PlateRN < -0.01f)
                throw new ArgumentException("the boundary has negative plate normals (the bar would already be lifting)");
            foreach (DeadliftContactStatics.JointLoad load in _boundary.Joints)
                if (load.DemandFraction > 1f)
                    throw new ArgumentException($"boundary joint {load.Id} demand {load.DemandFraction:F3} exceeds its ceiling");
        }

        public float GroundedHandPullN => _grounded.HandPullN;
        public float BoundaryHandPullN => _boundary.HandPullN;
        public float GroundedPlateSumN => _grounded.PlateLN + _grounded.PlateRN;
        public float BoundaryPlateSumN => _boundary.PlateLN + _boundary.PlateRN;

        /// <summary>Blended joint torque set on each child's joint axes.</summary>
        public Dictionary<string, Vector3> Torques(float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            Dictionary<string, Vector3> a = _grounded.DriveTorquesAxes(), b = _boundary.DriveTorquesAxes();
            var r = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Vector3> e in a)
                r[e.Key] = Vector3.LerpUnclamped(e.Value, b[e.Key], fraction);
            return r;
        }

        public Vector3 FootCop(string footId, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            bool left = footId == "left_foot";
            float n0 = left ? _grounded.FootLN : _grounded.FootRN;
            float n1 = left ? _boundary.FootLN : _boundary.FootRN;
            Vector3 m0 = (left ? _grounded.CopL : _grounded.CopR) * n0;
            Vector3 m1 = (left ? _boundary.CopL : _boundary.CopR) * n1;
            float n = Mathf.LerpUnclamped(n0, n1, fraction);
            return n > 1e-3f ? Vector3.LerpUnclamped(m0, m1, fraction) / n : (left ? _grounded.CopL : _grounded.CopR);
        }

        public float FootNormalN(string footId, float fraction)
        {
            bool left = footId == "left_foot";
            return Mathf.LerpUnclamped(left ? _grounded.FootLN : _grounded.FootRN,
                left ? _boundary.FootLN : _boundary.FootRN, Mathf.Clamp01(fraction));
        }

        public string Describe() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "bar-break load schedule: H {0:F1} -> {1:F1} N per hand, plates {2:F1} -> {3:F1} N, feet {4:F1}/{5:F1} -> {6:F1}/{7:F1} N; boundary joint demand max {8:F3}",
            GroundedHandPullN, BoundaryHandPullN, GroundedPlateSumN, BoundaryPlateSumN,
            _grounded.FootLN, _grounded.FootRN, _boundary.FootLN, _boundary.FootRN, MaxBoundaryDemand());

        private float MaxBoundaryDemand()
        {
            float max = 0f;
            foreach (DeadliftContactStatics.JointLoad load in _boundary.Joints)
                max = Mathf.Max(max, load.DemandFraction);
            return max;
        }
    }
}
