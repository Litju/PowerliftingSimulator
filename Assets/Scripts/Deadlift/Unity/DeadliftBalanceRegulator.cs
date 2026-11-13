using System;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
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
            public float T0, AcrossIntegral, HipOffset, LastAcross = float.NaN;
            public float S0, Integral, Offset, LastDeviationCommand, LastS = float.NaN, LastTarget = float.NaN;
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

        /// <summary>
        /// Delay-compensated per-foot law (REJECTED, off). Identified at H = 0 and H = 182 N
        /// per hand (frozen-regulator +1 deg steps, fresh plants): the ankle target
        /// acts on the COP one tick later; b = 64.5 / 47.5 mm/deg at 491 / 674 N per
        /// foot, i.e. b = Cb / N_foot with Cb = 31.7 N m/deg; free growth a = 2.3 / 2.0
        /// per tick. With a one-tick delay and a &gt;= 2 no proportional gain is stable
        /// (Jury: a - 1 &lt; b K &lt; 1 is empty); the proportional law only chattered
        /// in a bounded limit cycle at H = 0 and diverged under pretension. This law
        /// predicts x_{k+1} and places the closed-loop pole at (1 - Lambda) a;
        /// Lambda &gt; 1 - 1/a keeps it inside the unit circle.
        /// </summary>
        public bool Predictive { get; set; }
        public float CopTorqueGainNPerDeg { get; set; } = 31.7f;
        public float GrowthPerTick { get; set; } = 2.1f;
        public float Lambda { get; set; } = 0.8f;
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
        /// <summary>
        /// Across-sole (foot roll) loop on the hip y channel. Measured (frozen-regulator +1 deg
        /// hip-y steps, H = 0): left +45.4 mm, right +54.4 mm across the sole (up x sole axis)
        /// at the first effective tick, with &lt;= 4.3 mm along-sole coupling; the ankle hinge
        /// moves the COP along the sole (+63 / +71 mm) with &lt;= 9 mm across. The hinge ankle
        /// and knee leave foot roll unactuated, so the frozen baseline drifts 37 mm across in
        /// 10 ticks; only the hip can act on it. Same law as the along loop. REJECTED (off): with it
        /// A3 fails earlier (bar lifted tick 263) and +/-0.25 deg ankle excitation destabilizes
        /// the hold within ~120 ticks; independent ankle/hip COP loops reduce robustness.
        /// </summary>
        public bool AcrossLoop { get; set; }
        public float AcrossKpDegPerM { get; set; } = IdentifiedCopGrowthPerTick / 0.05f;
        public float AcrossKiDegPerMPerTick { get; set; } = 3f;
        public float MaxHipOffsetDeg { get; set; } = 3f;

        /// <summary>Hip y-channel offset (deg) for the thigh above the given foot.</summary>
        public float HipOffsetDegFor(string footId) => footId == "left_foot" ? _feet[0].HipOffset : footId == "right_foot" ? _feet[1].HipOffset : 0f;

        public float OffsetDegFor(string footId) => footId == "left_foot" ? _feet[0].Offset : footId == "right_foot" ? _feet[1].Offset : 0f;
        public float LastTargetCopZ => _feet[0].LastTarget;
        public float LastCopZ => _feet[0].LastS;

        /// <summary>Measured COP of one foot along its sole axis (m), last completed tick.</summary>
        public float CopAlongAxis(string footId) => footId == "left_foot" ? _feet[0].LastS : _feet[1].LastS;

        /// <summary>Measured COP of one foot across its sole (m, along up x axis: medial for the left foot is +), last completed tick.</summary>
        public float CopAcrossAxis(string footId)
        {
            Foot f = footId == "left_foot" ? _feet[0] : _feet[1];
            return f.Sensor.NormalImpulse > 1e-6 ? Vector3.Dot(f.Sensor.Cop, Vector3.Cross(Vector3.up, f.Axis)) : float.NaN;
        }

        /// <summary>
        /// Plant-identification hook: when frozen, measurements are still
        /// consumed but each foot's offset is held at its last value plus
        /// <see cref="IdentificationStepDeg"/> for that foot. Never used in a judged attempt.
        /// </summary>
        public bool Frozen { get; set; }
        public float IdentificationStepDegLeft { get; set; }
        public float IdentificationStepDegRight { get; set; }

        /// <summary>Closed-loop identification hook: small excitation (deg) added after the law, per foot. Zero in judged attempts.</summary>
        public float ExcitationDegLeft { get; set; }
        public float ExcitationDegRight { get; set; }
        private readonly float[] _frozenAt = new float[2];
        private bool _wasFrozen;

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

            if (Frozen)
            {
                if (!_wasFrozen) { _frozenAt[0] = _feet[0].Offset; _frozenAt[1] = _feet[1].Offset; _wasFrozen = true; }
                for (int i = 0; i < 2; i++)
                {
                    Foot f = _feet[i];
                    f.Sensor.CompleteTick();
                    f.LastS = f.Sensor.NormalImpulse > 1e-6 ? Vector3.Dot(f.Sensor.Cop, f.Axis) : float.NaN;
                    f.Offset = _frozenAt[i] + (i == 0 ? IdentificationStepDegLeft : IdentificationStepDegRight);
                }
                return;
            }
            _wasFrozen = false;

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
                float next;
                if (Predictive)
                {
                    // Delay-compensated law on the identified per-foot plant
                    //   x_{k+1} = a x_k + b v_{k-1},  x = s - s*,  v = u - I,  b = Cb / N_foot.
                    float normalN = (float)(foot.Sensor.NormalImpulse / SimulationConstants.FixedDeltaTimeSeconds);
                    float b = CopTorqueGainNPerDeg / Mathf.Max(normalN, 50f);
                    float x = -e;
                    float predicted = GrowthPerTick * x + b * foot.LastDeviationCommand;
                    float v = -Lambda * GrowthPerTick * predicted / b;
                    v = Mathf.Clamp(v, -MaxOffsetDeg - foot.Integral, MaxOffsetDeg - foot.Integral);
                    foot.LastDeviationCommand = v;
                    next = v + foot.Integral;
                }
                else
                {
                    next = KpDegPerM * e + foot.Integral;
                }
                saturated |= Mathf.Abs(next) > MaxOffsetDeg;
                next += foot.Id == "left_foot" ? ExcitationDegLeft : ExcitationDegRight;
                foot.Offset = Mathf.Clamp(next, -MaxOffsetDeg, MaxOffsetDeg);
                foot.LastS = s;
                foot.LastTarget = target;

                if (AcrossLoop)
                {
                    Vector3 across = Vector3.Cross(Vector3.up, foot.Axis);
                    float a = Vector3.Dot(foot.Sensor.Cop, across);
                    if (FootCopTarget != null)
                        foot.T0 = Vector3.Dot(FootCopTarget(foot.Id), across);
                    else if (float.IsNaN(foot.LastAcross))
                        foot.T0 = a;
                    float ea = foot.T0 - a;
                    foot.AcrossIntegral = Mathf.Clamp(foot.AcrossIntegral + AcrossKiDegPerMPerTick * ea, -MaxHipOffsetDeg, MaxHipOffsetDeg);
                    float h = AcrossKpDegPerM * ea + foot.AcrossIntegral;
                    saturated |= Mathf.Abs(h) > MaxHipOffsetDeg;
                    foot.HipOffset = Mathf.Clamp(h, -MaxHipOffsetDeg, MaxHipOffsetDeg);
                    foot.LastAcross = a;
                }
            }
            Saturated = saturated;
        }
    }
}
