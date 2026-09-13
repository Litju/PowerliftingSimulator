using System;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Unity-only adapter for the canonical post-physics squat observation.
    /// It reads copied foundation state and read-only engine diagnostics, then
    /// appends one immutable value snapshot to a bounded raw trace.
    /// </summary>
    public sealed class SquatObservationCollector
    {
        public const string PostPhysicsSamplingPoint = SquatTelemetrySchema.SamplingPointId;

        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalBarbell _barbell;
        private readonly SquatPhysicalAdapter _adapter;
        private readonly PhysicalFootContactDetector _leftFoot;
        private readonly PhysicalFootContactDetector _rightFoot;
        private readonly SquatTrace _trace;

        private bool _hasAttemptStart;
        private ulong _attemptStartTick;
        private SquatObservationSnapshot _lastSnapshot;
        private bool _hasLastSnapshot;

        public SquatObservationCollector(
            FoundationRuntime runtime,
            PhysicalAthleteRig rig,
            PhysicalBarbell barbell,
            SquatPhysicalAdapter adapter,
            PhysicalFootContactDetector leftFoot,
            PhysicalFootContactDetector rightFoot,
            int traceCapacity = SquatTrace.DefaultCapacity)
        {
            if (runtime == null)
                throw new ArgumentNullException(nameof(runtime));
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _barbell = barbell;
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _leftFoot = leftFoot;
            _rightFoot = rightFoot;
            if (_rig.PoweredController == null)
                throw new InvalidOperationException("The squat observation collector requires a built physical athlete.");

            _trace = new SquatTrace(traceCapacity);
            runtime.RegisterPostPhysicsStep(CapturePostPhysics);
        }

        public SquatTrace Trace => _trace;
        public bool IsRecording => _trace.IsRecording;
        public bool IsFrozen => _trace.IsFrozen;
        public bool HasLastSnapshot => _hasLastSnapshot;
        public SquatObservationSnapshot LastSnapshot => _lastSnapshot;

        public void BeginRecording()
        {
            _trace.BeginRecording();
            _hasAttemptStart = false;
            _hasLastSnapshot = false;
        }

        public void EndRecording() => _trace.EndRecording();

        public void Clear()
        {
            _trace.Clear();
            _hasAttemptStart = false;
            _attemptStartTick = 0ul;
            _lastSnapshot = default;
            _hasLastSnapshot = false;
        }

        private void CapturePostPhysics(
            SimulationTime time,
            PhysicalObservation physicalObservation,
            PlayerIntentFrame intent)
        {
            _rig.PoweredController.CapturePostPhysicsDiagnostics();
            _leftFoot?.CompletePhysicsStep();
            _rightFoot?.CompletePhysicsStep();
            float stepSeconds = (float)time.FixedDeltaTimeSeconds;
            _leftFoot?.PhysicsTickUpdate(stepSeconds);
            _rightFoot?.PhysicsTickUpdate(stepSeconds);

            SquatBarSaddle saddle = _adapter.Saddle;
            _adapter.Balance.Observe(
                physicalObservation,
                saddle,
                FallbackSupportPlaneY(physicalObservation));

            if (_trace.IsRecording && !_hasAttemptStart)
            {
                _attemptStartTick = time.Tick;
                _hasAttemptStart = true;
            }

            bool hasAttemptRelativeTime = _hasAttemptStart;
            double attemptRelativeTimeSeconds = hasAttemptRelativeTime
                ? (time.Tick - _attemptStartTick) * SimulationConstants.FixedDeltaTimeSeconds
                : double.NaN;
            SquatObservationSnapshot snapshot = BuildSnapshot(
                time,
                physicalObservation,
                intent,
                saddle,
                hasAttemptRelativeTime,
                attemptRelativeTimeSeconds);
            _lastSnapshot = snapshot;
            _hasLastSnapshot = true;
            if (_trace.IsRecording)
                _trace.Append(snapshot);
        }

        private SquatObservationSnapshot BuildSnapshot(
            SimulationTime time,
            PhysicalObservation physicalObservation,
            PlayerIntentFrame intent,
            SquatBarSaddle saddle,
            bool hasAttemptRelativeTime,
            double attemptRelativeTimeSeconds)
        {
            SquatBarObservation bar = CaptureBar(physicalObservation, saddle);
            SquatDepthLandmarks depth = CaptureDepth();
            SquatSupportObservation support = CaptureSupport();
            SquatFootObservation leftFoot = CaptureFoot(_leftFoot);
            SquatFootObservation rightFoot = CaptureFoot(_rightFoot);
            SquatJointObservationSet joints = CaptureJoints();
            CaptureBody(
                physicalObservation,
                "pelvis",
                out Vector3Value pelvisPosition,
                out Vector3Value pelvisVelocity,
                out _);
            CaptureBody(
                physicalObservation,
                "thorax",
                out _,
                out _,
                out QuaternionValue thoraxOrientation);
            float trunkPitch = SquatTelemetryValue.IsFinite(thoraxOrientation)
                ? SquatObservationSnapshot.WorldPitchRadians(thoraxOrientation)
                : float.NaN;

            CaptureDriveAvailability(out SquatTelemetryAvailability driveAvailability, out float maximumDemand);
            bool driveSaturated = driveAvailability == SquatTelemetryAvailability.AVAILABLE &&
                maximumDemand >= PoweredJointController.ModeledDemandSaturationThreshold;

            SquatTelemetryQualityFlags quality =
                SquatTelemetryQualityFlags.POST_PHYSICS | SquatTelemetryQualityFlags.RAW;
            if (hasAttemptRelativeTime)
                quality |= SquatTelemetryQualityFlags.ATTEMPT_RELATIVE_TIME;
            if (bar.IsAvailable)
                quality |= SquatTelemetryQualityFlags.BAR_AVAILABLE;
            if (support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE)
                quality |= SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE;
            if (support.HasSupport)
                quality |= SquatTelemetryQualityFlags.SUPPORT_CONTACT_PRESENT;
            if (depth.Availability == SquatTelemetryAvailability.AVAILABLE)
                quality |= SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE;
            if (AllJointsAvailable(joints))
                quality |= SquatTelemetryQualityFlags.JOINTS_AVAILABLE;
            if (driveAvailability == SquatTelemetryAvailability.AVAILABLE)
                quality |= SquatTelemetryQualityFlags.DRIVE_DIAGNOSTICS_AVAILABLE;
            if (leftFoot.Availability == SquatTelemetryAvailability.AVAILABLE)
                quality |= SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE;
            if (rightFoot.Availability == SquatTelemetryAvailability.AVAILABLE)
                quality |= SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE;

            return new SquatObservationSnapshot(
                time.Tick,
                time.SimulationTimeSeconds,
                time.FixedDeltaTimeSeconds,
                hasAttemptRelativeTime,
                attemptRelativeTimeSeconds,
                _adapter.State,
                _adapter.Direction,
                _adapter.Sq,
                SquatIntentSnapshot.From(intent),
                bar,
                depth,
                support,
                leftFoot,
                rightFoot,
                joints,
                pelvisPosition,
                pelvisVelocity,
                thoraxOrientation,
                trunkPitch,
                driveAvailability,
                driveSaturated,
                maximumDemand,
                quality);
        }

        private SquatBarObservation CaptureBar(
            PhysicalObservation physicalObservation,
            SquatBarSaddle saddle)
        {
            if (_barbell == null || _barbell.Body == null || !_barbell.Body.gameObject.activeInHierarchy ||
                !physicalObservation.TryGetBody("barbell", out PhysicalBodyObservation body) ||
                !SquatTelemetryValue.IsFinite(body.PositionMeters) ||
                !SquatTelemetryValue.IsFinite(body.LinearVelocityMetersPerSecond) ||
                !SquatTelemetryValue.IsFinite(body.AngularVelocityRadiansPerSecond) ||
                !SquatTelemetryValue.IsFinite(body.RotationWorldFromBody) ||
                !SquatTelemetryValue.IsFinite(body.MassKilograms) || body.MassKilograms < 0f)
                return SquatBarObservation.Unavailable();

            Vector3Value barAngularVelocity = body.RotationWorldFromBody.Inverse().TransformDirection(
                body.AngularVelocityRadiansPerSecond);
            SquatTelemetryAvailability saddleAvailability = SquatTelemetryAvailability.NOT_AVAILABLE;
            bool saddleAttached = false;
            bool saddleBroken = false;
            float saddleSeparation = float.NaN;
            if (saddle != null && SquatTelemetryValue.IsFinite(saddle.SaddleSeparationMeters))
            {
                saddleAvailability = SquatTelemetryAvailability.AVAILABLE;
                saddleAttached = saddle.IsAttached;
                saddleBroken = saddle.IsBroken;
                saddleSeparation = saddle.SaddleSeparationMeters;
            }

            return SquatBarObservation.Available(
                body.PositionMeters,
                body.LinearVelocityMetersPerSecond,
                body.RotationWorldFromBody,
                barAngularVelocity,
                body.MassKilograms,
                saddleAvailability,
                saddleAttached,
                saddleBroken,
                saddleSeparation);
        }

        private SquatDepthLandmarks CaptureDepth()
        {
            if (!_adapter.TryGetRawDepthLandmarks(
                out SquatPoint3 leftHip,
                out SquatPoint3 rightHip,
                out SquatPoint3 leftKnee,
                out SquatPoint3 rightKnee))
                return SquatDepthLandmarks.Unavailable();

            return new SquatDepthLandmarks(
                leftHip.Y,
                rightHip.Y,
                leftKnee.Y,
                rightKnee.Y,
                SquatDepthGeometry.DefaultDepthMarginM);
        }

        private SquatSupportObservation CaptureSupport()
        {
            SquatBalanceObserver balance = _adapter.Balance;
            Vector3Value com = ToValue(balance.SystemCom);
            Vector3Value comVelocity = ToValue(balance.SystemComVelocity);
            bool comAvailable = SquatTelemetryValue.IsFinite(com) &&
                SquatTelemetryValue.IsFinite(comVelocity) &&
                SquatTelemetryValue.IsFinite(balance.SystemMassKg);
            if (!comAvailable)
                return SquatSupportObservation.Unavailable();

            bool hasSupport = balance.HasSupport;
            SquatTelemetryAvailability enginePointAvailability = balance.HasCopEstimate
                ? SquatTelemetryAvailability.AVAILABLE
                : SquatTelemetryAvailability.NOT_AVAILABLE;
            Vector3Value enginePoint = balance.HasCopEstimate
                ? ToValue(balance.CopEstimate)
                : SquatTelemetryValue.UnavailableVector3;
            if (enginePointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                !SquatTelemetryValue.IsFinite(enginePoint))
            {
                enginePointAvailability = SquatTelemetryAvailability.NOT_AVAILABLE;
                enginePoint = SquatTelemetryValue.UnavailableVector3;
            }

            return new SquatSupportObservation(
                SquatTelemetryAvailability.AVAILABLE,
                com,
                comVelocity,
                balance.SystemMassKg,
                SquatTelemetryAvailability.AVAILABLE,
                hasSupport,
                hasSupport ? balance.SupportApMin : float.NaN,
                hasSupport ? balance.SupportApMax : float.NaN,
                hasSupport ? balance.SupportMlMin : float.NaN,
                hasSupport ? balance.SupportMlMax : float.NaN,
                hasSupport ? balance.SupportPlaneY : float.NaN,
                hasSupport ? balance.SupportContactCount : 0,
                enginePointAvailability,
                enginePoint,
                balance.TotalNormalImpulse);
        }

        private static SquatFootObservation CaptureFoot(PhysicalFootContactDetector detector)
        {
            return detector == null
                ? SquatFootObservation.Unavailable()
                : SquatFootObservation.Available(
                    detector.IsInContact,
                    detector.ContactCount,
                    detector.CompletedContactCount,
                    detector.SlipAccumulatedM,
                    detector.SlipSpeed);
        }

        private SquatJointObservationSet CaptureJoints()
        {
            SquatPhaseDirection direction = _adapter.Direction == SquatPhaseDirection.None
                ? SquatPhaseDirection.Descent
                : _adapter.Direction;
            SquatReferencePose reference = _adapter.ReferenceAnatomicalPose(_adapter.Sq, direction);
            return new SquatJointObservationSet(
                CaptureJoint("left_shank", reference.KneeFlexionRad, SquatJointFamily.Knee),
                CaptureJoint("right_shank", reference.KneeFlexionRad, SquatJointFamily.Knee),
                CaptureJoint("left_thigh", reference.HipFlexionRad, SquatJointFamily.Hip),
                CaptureJoint("right_thigh", reference.HipFlexionRad, SquatJointFamily.Hip),
                CaptureJoint("left_foot", reference.AnkleDorsiflexionRad, SquatJointFamily.Ankle),
                CaptureJoint("right_foot", reference.AnkleDorsiflexionRad, SquatJointFamily.Ankle),
                CaptureJoint("abdomen", reference.TrunkFlexionRad, SquatJointFamily.Abdomen),
                CaptureJoint("thorax", reference.TrunkFlexionRad, SquatJointFamily.Thorax));
        }

        private SquatJointObservation CaptureJoint(
            string jointId,
            float canonicalReferenceAngle,
            SquatJointFamily family)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            if (!joint.HasPostPhysicsDiagnostic || !joint.Profile.HasValue)
                return SquatJointObservation.Unavailable();

            PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
            float actualAngle = PoweredJointController.SignedTwistRadians(
                diagnostic.ActualRelative,
                Vector3.right);
            float referenceAngle = canonicalReferenceAngle * _adapter.FamilyFlexionSign(family);
            Vector3Value actualVelocity = ToValue(diagnostic.ActualAngularVelocityRadS);
            if (!SquatTelemetryValue.IsFinite(actualAngle) || !SquatTelemetryValue.IsFinite(actualVelocity))
                return SquatJointObservation.Unavailable();

            return SquatJointObservation.Available(
                actualAngle,
                actualVelocity,
                referenceAngle,
                actualAngle - referenceAngle,
                diagnostic.LimitProximity,
                diagnostic.ModeledDemand,
                diagnostic.MaximumForceNm,
                diagnostic.Activation,
                diagnostic.CapacityScale);
        }

        private void CaptureDriveAvailability(
            out SquatTelemetryAvailability availability,
            out float maximumDemand)
        {
            bool hasPoweredJoint = false;
            bool allAvailable = true;
            maximumDemand = 0f;
            for (int index = 0; index < _rig.PoweredController.Joints.Count; index++)
            {
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.Joints[index];
                if (!joint.Profile.HasValue)
                    continue;

                hasPoweredJoint = true;
                if (!joint.HasPostPhysicsDiagnostic)
                {
                    allAvailable = false;
                    continue;
                }

                maximumDemand = Mathf.Max(maximumDemand, joint.PostPhysicsDiagnostic.ModeledDemand);
            }

            availability = hasPoweredJoint && allAvailable
                ? SquatTelemetryAvailability.AVAILABLE
                : SquatTelemetryAvailability.NOT_AVAILABLE;
            if (availability != SquatTelemetryAvailability.AVAILABLE)
                maximumDemand = float.NaN;
        }

        private static bool AllJointsAvailable(SquatJointObservationSet joints) =>
            joints.LeftKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.RightKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.LeftHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.RightHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.LeftAnkle.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.RightAnkle.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.Abdomen.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joints.Thorax.JointAvailability == SquatTelemetryAvailability.AVAILABLE;

        private float FallbackSupportPlaneY(PhysicalObservation observation)
        {
            float lowest = float.PositiveInfinity;
            if (observation.TryGetBody("left_foot", out PhysicalBodyObservation leftFoot))
                lowest = Mathf.Min(lowest, leftFoot.PositionMeters.Y);
            if (observation.TryGetBody("right_foot", out PhysicalBodyObservation rightFoot))
                lowest = Mathf.Min(lowest, rightFoot.PositionMeters.Y);
            return float.IsPositiveInfinity(lowest)
                ? PhysicalAthleteDefinition.PlatformSupportPlaneY
                : lowest;
        }

        private static void CaptureBody(
            PhysicalObservation observation,
            string bodyId,
            out Vector3Value position,
            out Vector3Value velocity,
            out QuaternionValue rotation)
        {
            if (observation.TryGetBody(bodyId, out PhysicalBodyObservation body))
            {
                position = body.PositionMeters;
                velocity = body.LinearVelocityMetersPerSecond;
                rotation = body.RotationWorldFromBody;
                return;
            }

            position = SquatTelemetryValue.UnavailableVector3;
            velocity = SquatTelemetryValue.UnavailableVector3;
            rotation = SquatTelemetryValue.UnavailableQuaternion;
        }

        private static Vector3Value ToValue(Vector3 value) => new Vector3Value(value.x, value.y, value.z);
    }
}
