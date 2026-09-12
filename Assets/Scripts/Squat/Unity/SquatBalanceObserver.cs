using System;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Derives the balance state of the physical athlete from the previous
    /// post-physics step. It reads bodies and plantar contacts and writes
    /// nothing; every actuation decision belongs to the controller that
    /// consumes this.
    /// </summary>
    public sealed class SquatBalanceObserver
    {
        /// <summary>
        /// The support polygon and centre of pressure here are derived from
        /// the engine's own contact points and solver impulses. They are an
        /// estimate of what the solver did, not a force-plate measurement.
        /// </summary>
        public const string ContactClaimClass = "ENGINE_PHYSICS_CONTACT_ESTIMATE";

        /// <summary>
        /// Capture point and omega come from the linear inverted pendulum
        /// approximation. It is a control model for a game, not a claim about
        /// the athlete's true multi-segment dynamics.
        /// </summary>
        public const string ReducedOrderClaimClass = "GAME_CONTROL_REDUCED_ORDER_MODEL";

        public const float GravityMagnitudeMps2 = 9.81f;
        private const float MinimumComHeightM = 0.20f;
        private const float MinimumNormalImpulse = 1e-6f;

        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalAthleteRig.SegmentRuntime[] _segments;
        private PhysicalFootContactDetector _leftFoot;
        private PhysicalFootContactDetector _rightFoot;

        public SquatBalanceObserver(PhysicalAthleteRig rig, PhysicalAthleteRig.SegmentRuntime[] segments)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _segments = segments ?? throw new ArgumentNullException(nameof(segments));
        }

        // Mass model.
        public Vector3 SystemCom { get; private set; }
        public Vector3 SystemComVelocity { get; private set; }
        public float SystemMassKg { get; private set; }

        // Contact-derived support, all in world metres. AP is z, ML is x.
        public int SupportContactCount { get; private set; }
        public bool HasSupport { get; private set; }
        public float SupportApMin { get; private set; }
        public float SupportApMax { get; private set; }
        public float SupportMlMin { get; private set; }
        public float SupportMlMax { get; private set; }
        public float SupportPlaneY { get; private set; }
        public float SupportApCenter => 0.5f * (SupportApMin + SupportApMax);
        public float SupportMlCenter => 0.5f * (SupportMlMin + SupportMlMax);
        public float SupportApLength => SupportApMax - SupportApMin;
        public float SupportMlWidth => SupportMlMax - SupportMlMin;

        // Engine contact-impulse centre of pressure.
        public bool HasCopEstimate { get; private set; }
        public Vector3 CopEstimate { get; private set; }
        public float TotalNormalImpulse { get; private set; }

        // Reduced-order predictive state.
        public float ComHeightM { get; private set; }
        public float Omega { get; private set; }
        public float CaptureAp { get; private set; }
        public float CaptureMl { get; private set; }
        public float CaptureMarginFront => SupportApMax - CaptureAp;
        public float CaptureMarginRear => CaptureAp - SupportApMin;
        public float ComApMarginFront => SupportApMax - SystemCom.z;
        public float ComApMarginRear => SystemCom.z - SupportApMin;

        public void SetFootContactDetectors(
            PhysicalFootContactDetector leftFoot,
            PhysicalFootContactDetector rightFoot)
        {
            _leftFoot = leftFoot;
            _rightFoot = rightFoot;
        }

        /// <summary>
        /// Promotes the contact buffers so this step's readers see the
        /// contacts the previous simulated step actually produced.
        /// </summary>
        public void BeginPhysicsStep()
        {
            if (_leftFoot != null)
                _leftFoot.BeginPhysicsStep();
            if (_rightFoot != null)
                _rightFoot.BeginPhysicsStep();
        }

        public void Observe(PhysicalObservation observation, SquatBarSaddle saddle, float fallbackSupportPlaneY)
        {
            ObserveMassModel(observation, saddle);
            ObserveSupportAndCop(fallbackSupportPlaneY);
            ObserveReducedOrderState();
        }

        private void ObserveMassModel(PhysicalObservation observation, SquatBarSaddle saddle)
        {
            bool hasObservation = observation.BodyCount > 0;
            Vector3 weightedPosition = Vector3.zero;
            Vector3 weightedVelocity = Vector3.zero;
            float totalMass = 0f;

            for (int index = 0; index < _segments.Length; index++)
            {
                PhysicalAthleteRig.SegmentRuntime segment = _segments[index];
                if (segment.Body == null)
                    continue;
                Accumulate(
                    segment.Recipe.Id,
                    segment.Body.worldCenterOfMass,
                    segment.Body.linearVelocity,
                    segment.Body.mass,
                    observation,
                    hasObservation,
                    ref weightedPosition,
                    ref weightedVelocity,
                    ref totalMass);
            }

            if (saddle != null && saddle.IsAttached && saddle.Barbell != null && saddle.Barbell.Body != null &&
                saddle.Barbell.Body.gameObject.activeInHierarchy)
            {
                Accumulate(
                    "barbell",
                    saddle.Barbell.Body.worldCenterOfMass,
                    saddle.Barbell.Body.linearVelocity,
                    saddle.Barbell.LoadedMassKg,
                    observation,
                    hasObservation,
                    ref weightedPosition,
                    ref weightedVelocity,
                    ref totalMass);
            }

            SystemMassKg = totalMass;
            SystemCom = totalMass > 0f ? weightedPosition / totalMass : Vector3.zero;
            SystemComVelocity = totalMass > 0f ? weightedVelocity / totalMass : Vector3.zero;
        }

        private static void Accumulate(
            string bodyId,
            Vector3 position,
            Vector3 velocity,
            float mass,
            PhysicalObservation observation,
            bool hasObservation,
            ref Vector3 weightedPosition,
            ref Vector3 weightedVelocity,
            ref float totalMass)
        {
            if (hasObservation && observation.TryGetBody(bodyId, out PhysicalBodyObservation body))
            {
                position = new Vector3(
                    body.PositionMeters.X,
                    body.PositionMeters.Y,
                    body.PositionMeters.Z);
                velocity = new Vector3(
                    body.LinearVelocityMetersPerSecond.X,
                    body.LinearVelocityMetersPerSecond.Y,
                    body.LinearVelocityMetersPerSecond.Z);
                mass = body.MassKilograms;
            }

            weightedPosition += position * mass;
            weightedVelocity += velocity * mass;
            totalMass += mass;
        }

        private void ObserveSupportAndCop(float fallbackSupportPlaneY)
        {
            float apMin = float.PositiveInfinity;
            float apMax = float.NegativeInfinity;
            float mlMin = float.PositiveInfinity;
            float mlMax = float.NegativeInfinity;
            float planeYSum = 0f;
            int count = 0;
            Vector3 impulseWeightedPoint = Vector3.zero;
            float impulseSum = 0f;

            AccumulateContacts(_leftFoot, ref apMin, ref apMax, ref mlMin, ref mlMax, ref planeYSum, ref count,
                ref impulseWeightedPoint, ref impulseSum);
            AccumulateContacts(_rightFoot, ref apMin, ref apMax, ref mlMin, ref mlMax, ref planeYSum, ref count,
                ref impulseWeightedPoint, ref impulseSum);

            SupportContactCount = count;
            HasSupport = count > 0;
            TotalNormalImpulse = impulseSum;

            if (!HasSupport)
            {
                // No plantar contact at all. Report the absence rather than
                // substituting a body-centre guess that would read as support.
                SupportApMin = SupportApMax = SystemCom.z;
                SupportMlMin = SupportMlMax = SystemCom.x;
                SupportPlaneY = fallbackSupportPlaneY;
                HasCopEstimate = false;
                CopEstimate = Vector3.zero;
                return;
            }

            SupportApMin = apMin;
            SupportApMax = apMax;
            SupportMlMin = mlMin;
            SupportMlMax = mlMax;
            SupportPlaneY = planeYSum / count;

            if (impulseSum > MinimumNormalImpulse)
            {
                Vector3 cop = impulseWeightedPoint / impulseSum;
                CopEstimate = new Vector3(cop.x, SupportPlaneY, cop.z);
                HasCopEstimate = true;
            }
            else
            {
                // Contacts exist but carry no measurable normal impulse this
                // step; a COP would be division noise, so say so.
                CopEstimate = Vector3.zero;
                HasCopEstimate = false;
            }
        }

        private static void AccumulateContacts(
            PhysicalFootContactDetector detector,
            ref float apMin,
            ref float apMax,
            ref float mlMin,
            ref float mlMax,
            ref float planeYSum,
            ref int count,
            ref Vector3 impulseWeightedPoint,
            ref float impulseSum)
        {
            if (detector == null)
                return;

            int contacts = detector.CompletedContactCount;
            for (int index = 0; index < contacts; index++)
            {
                Vector3 point = detector.CompletedContactPoint(index);
                float normalImpulse = detector.CompletedNormalImpulse(index);
                apMin = Mathf.Min(apMin, point.z);
                apMax = Mathf.Max(apMax, point.z);
                mlMin = Mathf.Min(mlMin, point.x);
                mlMax = Mathf.Max(mlMax, point.x);
                planeYSum += point.y;
                count++;
                impulseWeightedPoint += point * normalImpulse;
                impulseSum += normalImpulse;
            }
        }

        private void ObserveReducedOrderState()
        {
            ComHeightM = Mathf.Max(MinimumComHeightM, SystemCom.y - SupportPlaneY);
            Omega = Mathf.Sqrt(GravityMagnitudeMps2 / ComHeightM);
            CaptureAp = SystemCom.z + SystemComVelocity.z / Omega;
            CaptureMl = SystemCom.x + SystemComVelocity.x / Omega;
        }
    }
}
