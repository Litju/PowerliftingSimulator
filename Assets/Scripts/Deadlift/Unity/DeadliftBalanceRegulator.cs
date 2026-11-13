using System;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 deadlift-local balance regulator for the grounded, gripped B2
    /// setup. It is a reference shaper, not a joint writer: it returns one
    /// ankle hinge target offset per foot (deg) that <see cref="DeadliftSetupHold"/>
    /// adds to that ankle's reference, so PoweredJointController stays the
    /// only physical joint writer. No body, bar or force write; no world pin.
    ///
    /// Measured plant (C5 identification, fresh B2 plants, C4 H=0 reference,
    /// grips on, 10 ms ticks):
    ///   * the unperturbed per-foot COP deviation grows ~2.0x per tick; with
    ///     only the combined AP COP regulated, a left/right differential
    ///     (medio-lateral) runaway remained, so the unstable state is each
    ///     foot's own COP, not the combined COP;
    ///   * +1 deg on one ankle moves that foot's COP ~54 mm along its own
    ///     long axis (44 mm world Z, 32 mm world X through the 42 deg toe-out)
    ///     within the same tick, and keeps rising (c_{k+1} = a c_k + b u_k,
    ///     a ~ 1.8, b ~ 0.054 m/deg);
    ///   * stability of |a - b Kp| &lt; 1 gives Kp in ((a-1)/b, (a+1)/b) =
    ///     (15, 52) deg/m; Kp = a/b is used.
    /// Rejected and measured: two integral-only combined-COP laws (limit cycle
    /// at G 0.044 m/deg Kappa 1; too slow at G 0.12 m/deg Kappa 0.5) and a
    /// proportional combined-COP law (AP held, differential ML runaway).
    ///
    /// Law, per foot i with sole axis f_i (horizontal foot forward at engagement):
    ///   s_i = f_i . COP_i (measured, last completed tick); s0_i its first value;
    ///   outer COM term d = (1 + Kx)(z - z0) + Kv vz (world Z), shared;
    ///   s*_i = clamp(s0_i + d / f_i.z, loaded extent of foot i along f_i +/- margin);
    ///   e_i = s*_i - s_i; I_i = clamp(I_i + Ki e_i); u_i = clamp(Kp e_i + I_i, +/- MaxOffsetDeg).
    /// The outer term places the COP ahead of a forward-moving COM (Winter
    /// 5.2.5), qualitatively; rigid grips and two-foot contact violate a LIPM.
    /// </summary>
    public sealed class DeadliftBalanceRegulator
    {
        public const float IdentifiedFootCopGainMPerDeg = 0.054f;
        public const float IdentifiedCopGrowthPerTick = 1.8f;

        private readonly PhysicalAthleteRig _rig;
        private readonly Foot[] _feet;
        private bool _comEngaged;
        private float _z0;

        private sealed class Foot
        {
            public string Id;
            public DeadliftFootPressureSensor Sensor;
            public Vector3 Axis;
            public bool Engaged;
            public float S0, Integral, Offset, LastS = float.NaN, LastTarget = float.NaN;
        }

        public DeadliftBalanceRegulator(PhysicalAthleteRig rig, Collider ground)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            if (ground == null)
                throw new ArgumentNullException(nameof(ground));
            _feet = new[] { MakeFoot("left_foot", ground), MakeFoot("right_foot", ground) };
        }

        /// <summary>Enables the outer COM term d; when false each foot COP is servoed to its statics target only.</summary>
        public bool ComFeedback { get; set; } = true;
        public float Kx { get; set; } = 0.4f;
        public float KvSeconds { get; set; } = 0.25f;
        public float KpDegPerM { get; set; } = IdentifiedCopGrowthPerTick / IdentifiedFootCopGainMPerDeg;
        public float KiDegPerMPerTick { get; set; } = 3f;
        public float MaxOffsetDeg { get; set; } = 4f;
        public float SupportMarginM { get; set; } = 0.01f;

        /// <summary>
        /// Optional per-foot COP target (world) from the contact-consistent
        /// statics, e.g. under pretension. Without it the first measured COP is used.
        /// </summary>
        public Func<string, Vector3> FootCopTarget { get; set; }

        public bool Saturated { get; private set; }
        public float LastComZ { get; private set; }
        public float LastComVz { get; private set; }

        /// <summary>Mean of the two offsets (for traces).</summary>
        public float OffsetDeg => 0.5f * (_feet[0].Offset + _feet[1].Offset);
        public float OffsetDegFor(string footId) => footId == "left_foot" ? _feet[0].Offset : footId == "right_foot" ? _feet[1].Offset : 0f;
        public float LastTargetCopZ => _feet[0].LastTarget;
        public float LastCopZ => _feet[0].LastS;

        private Foot MakeFoot(string id, Collider ground)
        {
            Rigidbody body = _rig.Segments[id].Body;
            DeadliftFootPressureSensor s = body.GetComponent<DeadliftFootPressureSensor>() ?? body.gameObject.AddComponent<DeadliftFootPressureSensor>();
            s.Ground = ground;
            Vector3 f = Vector3.ProjectOnPlane(body.transform.forward, Vector3.up).normalized;
            s.Axis = f;
            return new Foot { Id = id, Sensor = s, Axis = f };
        }

        /// <summary>Called once per tick before commands are prepared; consumes the last completed tick's contacts.</summary>
        public void Update()
        {
            float mass = 0f;
            Vector3 c = Vector3.zero, p = Vector3.zero;
            foreach (PhysicalAthleteRig.SegmentRuntime seg in _rig.Segments.Values)
            {
                mass += seg.Body.mass;
                c += seg.Body.mass * seg.Body.worldCenterOfMass;
                p += seg.Body.mass * seg.Body.linearVelocity;
            }
            c /= mass;
            p /= mass;
            LastComZ = c.z;
            LastComVz = p.z;
            if (!_comEngaged)
            {
                _z0 = c.z;
                _comEngaged = true;
            }
            float d = ComFeedback ? (1f + Kx) * (c.z - _z0) + KvSeconds * p.z : 0f;

            bool saturated = false;
            foreach (Foot foot in _feet)
            {
                foot.Sensor.CompleteTick();
                if (foot.Sensor.NormalImpulse <= 1e-6)
                {
                    foot.LastS = float.NaN; // no measured support: hold, never invent a COP
                    continue;
                }
                float s = Vector3.Dot(foot.Sensor.Cop, foot.Axis);
                if (FootCopTarget != null)
                {
                    foot.S0 = Vector3.Dot(FootCopTarget(foot.Id), foot.Axis);
                    foot.Engaged = true;
                }
                else if (!foot.Engaged)
                {
                    foot.S0 = s;
                    foot.Engaged = true;
                }
                float target = foot.S0 + (Mathf.Abs(foot.Axis.z) > 0.2f ? d / foot.Axis.z : 0f);
                float lo = foot.Sensor.LoadedAxisMin + SupportMarginM, hi = foot.Sensor.LoadedAxisMax - SupportMarginM;
                if (hi > lo)
                    target = Mathf.Clamp(target, lo, hi);
                float e = target - s;
                foot.Integral = Mathf.Clamp(foot.Integral + KiDegPerMPerTick * e, -MaxOffsetDeg, MaxOffsetDeg);
                float next = KpDegPerM * e + foot.Integral;
                saturated |= Mathf.Abs(next) > MaxOffsetDeg;
                foot.Offset = Mathf.Clamp(next, -MaxOffsetDeg, MaxOffsetDeg);
                foot.LastS = s;
                foot.LastTarget = target;
            }
            Saturated = saturated;
        }
    }
}
