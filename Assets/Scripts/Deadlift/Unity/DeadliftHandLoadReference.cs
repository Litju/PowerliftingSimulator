using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 Gate 5: the athlete-side contact-consistent reference of the
    /// gripped deadlift as a function of the measured per-hand vertical load H.
    ///
    /// The C4 athlete statics (joint torques, foot normals and foot COPs)
    /// depend only on gravity, the posture and the hand-bar wrench; the plate
    /// reactions belong to the bar alone. One family tau(H) therefore covers the
    /// grounded pull (plates still loaded) and the separated bar (plates zero)
    /// without a mode switch: at separation the hands carry the bar, and the
    /// measured H reaches W_b / 2 continuously. This replaces two writers that
    /// were measured to fight the pull (Gate 5): the A3 pretension servo, which
    /// unwinds H toward its 275.9 N setpoint while the player's pull raises the
    /// real hand load, and the posture-fixed airborne schedule, whose grounded
    /// endpoint (H = 275.9 N) was not the applied reference.
    ///
    /// Posture: either fixed (the two bounding solutions at the placed setup,
    /// exact and affine in H there) or re-solved every tick at the current
    /// body configuration (<see cref="ResolveAtCurrentPosture"/>), which is the
    /// quasi-static gravity-and-load compensation of the moving pose. Dynamic
    /// (inertial) terms are not included; they are left to feedback.
    /// H is an observation, not a force command: it is clamped to
    /// [0, <see cref="MaximumHandPullN"/>] (the static hang of the bar), and
    /// the clamp is reported. This class never writes a force, body or joint.
    /// </summary>
    public sealed class DeadliftHandLoadReference
    {
        private readonly Func<float, DeadliftContactStatics> _solve;
        private DeadliftContactStatics _zero, _max, _current;

        public DeadliftHandLoadReference(Func<float, DeadliftContactStatics> solveAtCurrentPosture, float maximumHandPullN, bool resolveAtCurrentPosture)
        {
            _solve = solveAtCurrentPosture ?? throw new ArgumentNullException(nameof(solveAtCurrentPosture));
            if (!(maximumHandPullN > 0f))
                throw new ArgumentOutOfRangeException(nameof(maximumHandPullN));
            MaximumHandPullN = maximumHandPullN;
            ResolveAtCurrentPosture = resolveAtCurrentPosture;
            _zero = _solve(0f);
            _max = _solve(maximumHandPullN);
            if (!_zero.Feasible)
                throw new ArgumentException("the H = 0 statics of the setup must be feasible");
            foreach (DeadliftContactStatics.JointLoad load in _max.Joints)
                if (load.DemandFraction > 1f)
                    throw new ArgumentException($"joint {load.Id} demand {load.DemandFraction:F3} exceeds its ceiling at the maximum hand load");
            _current = _zero;
        }

        public float MaximumHandPullN { get; }
        public bool ResolveAtCurrentPosture { get; }
        public float HandPullN { get; private set; }
        public float MeasuredHandPullN { get; private set; }
        public bool Clamped { get; private set; }
        /// <summary>Largest joint drive demand fraction of the reference last applied.</summary>
        public float MaxDemandFraction { get; private set; }
        public int Solves { get; private set; }
        public string MaxDemandJoint { get; private set; } = string.Empty;

        /// <summary>Selects the reference for an observed per-hand load (already filtered by the caller).</summary>
        public void Observe(float perHandUpN)
        {
            MeasuredHandPullN = perHandUpN;
            float h = float.IsNaN(perHandUpN) ? HandPullN : perHandUpN;
            Clamped = h < 0f || h > MaximumHandPullN;
            HandPullN = Mathf.Clamp(h, 0f, MaximumHandPullN);
            if (ResolveAtCurrentPosture)
            {
                _zero = _solve(0f);
                _current = _solve(HandPullN);
                Solves += 2;
            }
            MaxDemandFraction = 0f;
            foreach (DeadliftContactStatics.JointLoad load in Current().Joints)
                if (load.DemandFraction > MaxDemandFraction)
                {
                    MaxDemandFraction = load.DemandFraction;
                    MaxDemandJoint = load.Id;
                }
        }

        private float Fraction => HandPullN / MaximumHandPullN;

        private DeadliftContactStatics Current() => ResolveAtCurrentPosture ? _current : (Fraction >= 0.5f ? _max : _zero);

        public Dictionary<string, Vector3> DriveTorquesAxes()
        {
            if (ResolveAtCurrentPosture)
                return _current.DriveTorquesAxes();
            Dictionary<string, Vector3> a = _zero.DriveTorquesAxes(), b = _max.DriveTorquesAxes();
            var r = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Vector3> e in a)
                r[e.Key] = Vector3.LerpUnclamped(e.Value, b[e.Key], Fraction);
            return r;
        }

        public Vector3 FootCop(string footId)
        {
            bool left = footId == "left_foot";
            if (ResolveAtCurrentPosture)
                return left ? _current.CopL : _current.CopR;
            float n0 = left ? _zero.FootLN : _zero.FootRN, n1 = left ? _max.FootLN : _max.FootRN;
            Vector3 m0 = (left ? _zero.CopL : _zero.CopR) * n0, m1 = (left ? _max.CopL : _max.CopR) * n1;
            return Vector3.LerpUnclamped(m0, m1, Fraction) / Mathf.LerpUnclamped(n0, n1, Fraction);
        }

        public float FootNormalN(string footId)
        {
            bool left = footId == "left_foot";
            if (ResolveAtCurrentPosture)
                return left ? _current.FootLN : _current.FootRN;
            return Mathf.LerpUnclamped(left ? _zero.FootLN : _zero.FootRN, left ? _max.FootLN : _max.FootRN, Fraction);
        }

        public float FootNormalAtZeroN(string footId) => footId == "left_foot" ? _zero.FootLN : _zero.FootRN;
    }
}
