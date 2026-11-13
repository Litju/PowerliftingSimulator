using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 A3: the contact-consistent reference of the grounded, gripped
    /// setup as a function of the commanded per-hand pull H (pretension).
    ///
    /// At the fixed placed configuration, every C4 joint torque, every foot
    /// normal N_i and every foot moment N_i COP_i is affine in H (the declared
    /// wrench set is linear in H), so two <see cref="DeadliftContactStatics"/>
    /// solutions, at 0 and at <see cref="MaximumHandPullN"/>, give the exact
    /// reference for any H between them: tau(H) = tau0 + (H / Hmax)(tauMax - tau0),
    /// COP_i(H) = M_i(H) / N_i(H). It is a fixed function of the command, not of
    /// measured state. Commands outside [0, Hmax] are rejected, never clamped,
    /// so an invalid pull cannot silently reach the plant. Hmax must leave the
    /// plates loaded: the bar-break boundary (plates at zero) is not part of A3.
    /// </summary>
    public sealed class DeadliftGroundedReference
    {
        private readonly DeadliftContactStatics _zero, _max;

        public DeadliftGroundedReference(DeadliftContactStatics atZero, DeadliftContactStatics atMaximum)
        {
            _zero = atZero ?? throw new ArgumentNullException(nameof(atZero));
            _max = atMaximum ?? throw new ArgumentNullException(nameof(atMaximum));
            if (!_zero.Feasible || !_max.Feasible)
                throw new ArgumentException("both bounding contact-statics solutions must be feasible");
            if (_zero.HandPullN != 0f || _max.HandPullN <= 0f)
                throw new ArgumentException("bounds must be H = 0 and H > 0");
            if (_max.PlateLN <= 0f || _max.PlateRN <= 0f)
                throw new ArgumentException("the upper bound must keep both plates loaded (bar break is not pretension)");
        }

        public float MaximumHandPullN => _max.HandPullN;
        public float HandPullN { get; private set; }

        /// <summary>Accepts H in [0, MaximumHandPullN]; otherwise rejects and keeps the previous command.</summary>
        public bool TryCommandHandPull(float handPullN)
        {
            if (float.IsNaN(handPullN) || handPullN < 0f || handPullN > MaximumHandPullN)
                return false;
            HandPullN = handPullN;
            return true;
        }

        private float Fraction => HandPullN / MaximumHandPullN;

        public Dictionary<string, Vector3> DriveTorquesAxes()
        {
            Dictionary<string, Vector3> a = _zero.DriveTorquesAxes(), b = _max.DriveTorquesAxes();
            var r = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Vector3> e in a)
                r[e.Key] = Vector3.LerpUnclamped(e.Value, b[e.Key], Fraction);
            return r;
        }

        public Vector3 FootCop(string footId)
        {
            bool left = footId == "left_foot";
            float n0 = left ? _zero.FootLN : _zero.FootRN, n1 = left ? _max.FootLN : _max.FootRN;
            Vector3 m0 = (left ? _zero.CopL : _zero.CopR) * n0, m1 = (left ? _max.CopL : _max.CopR) * n1;
            float n = Mathf.LerpUnclamped(n0, n1, Fraction);
            return Vector3.LerpUnclamped(m0, m1, Fraction) / n;
        }

        /// <summary>C4 statics normal reaction of one foot at the commanded pull (affine in H).</summary>
        public float FootNormalN(string footId) => footId == "left_foot"
            ? Mathf.LerpUnclamped(_zero.FootLN, _max.FootLN, Fraction)
            : Mathf.LerpUnclamped(_zero.FootRN, _max.FootRN, Fraction);

        /// <summary>The same foot's reaction at H = 0.</summary>
        public float FootNormalAtZeroN(string footId) => footId == "left_foot" ? _zero.FootLN : _zero.FootRN;

        public float PlateSumN => Mathf.LerpUnclamped(_zero.PlateLN + _zero.PlateRN, _max.PlateLN + _max.PlateRN, Fraction);
        public float FootSumN => Mathf.LerpUnclamped(_zero.FootLN + _zero.FootRN, _max.FootLN + _max.FootRN, Fraction);
    }
}
