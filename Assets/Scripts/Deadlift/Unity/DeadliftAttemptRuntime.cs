using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70: one physically simulated deadlift attempt, composed exactly as
    /// qualified by the Gate 5-7 PlayMode evidence:
    ///   B2 initial condition (placed once before tick 0) with both grips welded,
    ///   deadlift ankle-roll DOF, contact-consistent C4 statics,
    ///   A3 pretension servo while the bar is grounded,
    ///   measured hand-load reference (re-solved at the current posture) from the
    ///   player's pull on, ankle balance regulator (moving phase: no quiet-stance
    ///   anchor, 0.1 s integral leak, athlete+bar COM),
    ///   player Drive -> bar break -> solved ascent path gated by the capture-point
    ///   support margin, referee Down -> controlled lowering -> bar returned ->
    ///   grips released.
    /// The only joint writer is PoweredJointController through
    /// <see cref="DeadliftSetupHold"/>; no body, bar or force is written after
    /// tick 0. It is driven by the foundation's authoritative tick.
    /// </summary>
    public sealed class DeadliftAttemptRuntime
    {
        public const int PretensionStartTick = 100, PretensionRampTicks = 100;

        /// <summary>Lift-path progress rate at full Drive (1/s). Static so the robustness sweep can vary it.</summary>
        public static float LiftRatePerSecond = 0.30f;
        /// <summary>Bound on the change of the lift-progress rate (1/s^2). Static so the robustness sweep can vary it.</summary>
        public static float LiftAccelPerSecond2 = 0.6f;

        private readonly FoundationBootstrap _foundation;
        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalBarbell _barbell;
        private DeadliftBarSensor _barSensor;
        private DeadliftGroundedReference _groundedRef;
        private DeadliftPretensionLoop _pretension;
        private float _pretensionTargetN;
        private Collider _leftPlate, _rightPlate;
        private ulong _tick;

        public DeadliftAttemptRuntime(FoundationBootstrap foundation, PhysicalAthleteRig rig, PhysicalBarbell barbell)
        {
            _foundation = foundation ?? throw new ArgumentNullException(nameof(foundation));
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _barbell = barbell ?? throw new ArgumentNullException(nameof(barbell));
        }

        public DeadliftPlantModel Model { get; private set; }
        public DeadliftSetupSolver Measurements { get; private set; }
        public DeadliftSetupHold Hold { get; private set; }
        public DeadliftBalanceRegulator Regulator { get; private set; }
        public DeadliftPullController Pull { get; private set; }
        public DeadliftLiftPath Path { get; private set; }
        public DeadliftSupportMonitor Monitor { get; private set; }
        public DeadliftReferee Referee { get; private set; }
        public DeadliftAttemptObserver Observer { get; private set; }
        public BenchGrip LeftGrip { get; private set; }
        public BenchGrip RightGrip { get; private set; }
        public DeadliftObservation LastObservation { get; private set; }
        public float PlateLeftN { get; private set; }
        public float PlateRightN { get; private set; }
        public float BarOnLegN { get; private set; }
        public bool GripsReleased { get; private set; }
        public ulong Tick => _tick;
        public Rigidbody Bar => _barbell.Body;
        public Rigidbody Pelvis => _rig.Segments["pelvis"].Body;

        /// <summary>Smaller of the two foot normal loads on the last completed tick (N).</summary>
        public float FootLoadMinN
        {
            get
            {
                double dt = SimulationConstants.FixedDeltaTimeSeconds;
                var l = _rig.Segments["left_foot"].Body.GetComponent<DeadliftFootPressureSensor>();
                var r = _rig.Segments["right_foot"].Body.GetComponent<DeadliftFootPressureSensor>();
                return l == null || r == null ? float.NaN : (float)(System.Math.Min(l.NormalImpulse, r.NormalImpulse) / dt);
            }
        }
        public float TickZeroMaxPenetrationM { get; private set; }
        public string PathReport { get; private set; } = string.Empty;

        /// <summary>Optional read-only observer after each completed tick (tests, telemetry).</summary>
        public event Action<DeadliftAttemptRuntime> TickCompleted;

        /// <summary>
        /// Composes the attempt. Call once, before the foundation's first
        /// authoritative tick; the rig must be standing in its registered pose
        /// (the setup solver measures it).
        /// </summary>
        public void Initialize()
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            _foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            if (_rig.PoweredController == null)
                _rig.Build();
            _rig.SetGameplayPerformanceProfile(true);

            var feasibility = new DeadliftSetupFeasibilitySolver(_rig);
            Measurements = feasibility.Measurements;
            DeadliftSetupSpec spec = DeadliftQualifiedSetup.SumoPronated();
            Model = Measurements.BuildModel(DeadliftQualifiedSetup.B2, spec);

            PlaceAtSetup();
            DeadliftAnkleRoll.Enable(_rig);
            ValidateTickZero();

            float wbHalf = 0.5f * _barbell.Body.mass * -Physics.gravity.y;
            float h = 0.75f * wbHalf;
            DeadliftContactStatics s0 = SolveStatics(0f), sH = SolveStatics(h);
            _groundedRef = new DeadliftGroundedReference(s0, sH);
            Regulator = new DeadliftBalanceRegulator(_rig, _rig.PlatformCollider) { ComFeedback = true, KiDegPerMPerTick = 3f, RollLoop = true, ComPositionAnchor = false };
            Hold.Regulator = Regulator;
            Hold.UseGroundedReference(_rig, _groundedRef);
            _pretension = new DeadliftPretensionLoop(_groundedRef, MeasuredPerHandUpN);
            Hold.Pretension = _pretension;
            _pretensionTargetN = h;
            Hold.UseHandLoadReference(new DeadliftHandLoadReference(SolveStatics, wbHalf, true), MeasuredPerHandUpN);

            Pull = new DeadliftPullController(_rig, _barbell, () => PlateLeftN, () => PlateRightN)
            {
                RampPerSecond = 1f, ReleasePerSecond = 2f,
                HipExtensionLimitDeg = 2.5f, KneeExtensionLimitDeg = 2f, TrunkExtensionLimitDeg = -1.5f,
                MaxComShiftM = 0.05f, GovernorReleaseM = 0.03f,
                LiftEnabled = true, LiftRatePerSecond = LiftRatePerSecond, LiftAccelPerSecond2 = LiftAccelPerSecond2
            };
            Hold.Pull = Pull;

            Path = new DeadliftLiftPath(_rig, Measurements, Model, _barbell.Body.mass);
            Path.Solve();
            PathReport = Path.Describe();
            Hold.LiftPath = Path;
            Hold.MovingPhaseIntegralLeakSeconds = 0.10f;
            Hold.MovingPhaseSystemCom = true;
            Monitor = new DeadliftSupportMonitor(_rig, _barbell.Body, _rig.PlatformCollider);
            Pull.SupportMarginM = () => Monitor.Update();
            Pull.ReferenceDemand = () => Hold.HandLoadReference.MaxDemandFraction;

            Referee = new DeadliftReferee(_barbell.Body.worldCenterOfMass.y, _barbell.Body.mass * -Physics.gravity.y, dt);
            Observer = new DeadliftAttemptObserver(_rig, _barbell.Body, () => PlateLeftN + PlateRightN, () => BarOnLegN,
                () => LeftGrip != null && LeftGrip.IsAttached, () => RightGrip != null && RightGrip.IsAttached);

            _rig.PrimeCommandSource();
            DeadliftAnkleRoll.ValidateAppliedAuthority(_rig);
            _foundation.Runtime.RegisterPostPhysicsStep(OnPostPhysicsStep);
            _foundation.Runtime.MarkInitialPhysicalStateValidated();
        }

        private void PlaceAtSetup()
        {
            _barbell.ConfigureLoad(DeadliftQualifiedSetup.BarKg);
            _foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            Rigidbody bar = _barbell.Body;
            bar.isKinematic = false;
            bar.position = Model.BarCenterWorld;
            bar.rotation = Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right);
            bar.transform.SetPositionAndRotation(bar.position, bar.rotation);
            bar.linearVelocity = Vector3.zero;
            bar.angularVelocity = Vector3.zero;
            bar.sleepThreshold = 0f;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in _rig.Segments)
            {
                Rigidbody body = entry.Value.Body;
                Pose pose = Model.Poses[entry.Key];
                body.isKinematic = false;
                body.transform.SetPositionAndRotation(pose.position, pose.rotation);
                body.position = pose.position;
                body.rotation = pose.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.sleepThreshold = 0f;
            }
            Physics.SyncTransforms();

            _leftPlate = bar.transform.Find("LeftPlateAggregateCollider")?.GetComponent<Collider>();
            _rightPlate = bar.transform.Find("RightPlateAggregateCollider")?.GetComponent<Collider>();
            if (_leftPlate == null || _rightPlate == null)
                throw new InvalidOperationException("The barbell has no plate aggregate colliders.");
            _barSensor = bar.gameObject.GetComponent<DeadliftBarSensor>() ?? bar.gameObject.AddComponent<DeadliftBarSensor>();
            _barSensor.Ground = _rig.PlatformCollider;
            _barSensor.LeftPlate = _leftPlate;
            _barSensor.RightPlate = _rightPlate;
            foreach (string id in new[] { "left_thigh", "right_thigh", "left_shank", "right_shank" })
                _barSensor.Legs.Add(_rig.Segments[id].Collider);

            // Declared approximation (as the bench): the bar does not collide with the limbs that grasp it.
            foreach (Collider c in bar.GetComponentsInChildren<Collider>(true))
                foreach (string id in new[] { "left_hand", "right_hand", "left_forearm", "right_forearm", "left_upper_arm", "right_upper_arm" })
                    Physics.IgnoreCollision(_rig.Segments[id].Collider, c, true);

            LeftGrip = new BenchGrip("left");
            LeftGrip.Attach(_rig.Segments["left_hand"].Body, bar, Model.LeftGripAnchorLocal);
            RightGrip = new BenchGrip("right");
            RightGrip.Attach(_rig.Segments["right_hand"].Body, bar, Model.RightGripAnchorLocal);

            Hold = new DeadliftSetupHold(Model.SetupLogical);
            _rig.SetCommandSource(Hold);
            _rig.PrimeCommandSource();
        }

        private void ValidateTickZero()
        {
            float worst = 0f;
            foreach (PhysicalAthleteRig.SegmentRuntime s in _rig.Segments.Values)
                if (Physics.ComputePenetration(s.Collider, s.Collider.transform.position, s.Collider.transform.rotation,
                        _rig.PlatformCollider, _rig.PlatformCollider.transform.position, _rig.PlatformCollider.transform.rotation, out _, out float d))
                    worst = Mathf.Max(worst, d);
            TickZeroMaxPenetrationM = worst;
            foreach (PhysicalAthleteRig.JointRuntime joint in _rig.Joints)
            {
                Vector3 a = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 b = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
                if (Vector3.Distance(a, b) > 0.0005f)
                    throw new InvalidOperationException($"Joint {joint.Recipe.ChildId} anchors separate after setup placement.");
            }
        }

        public DeadliftContactStatics SolveStatics(float handPullN)
        {
            Vector3 la = _rig.Segments["left_hand"].Body.transform.TransformPoint(LeftGrip != null ? LeftGrip.HandAnchorLocal : Model.LeftGripAnchorLocal);
            Vector3 ra = _rig.Segments["right_hand"].Body.transform.TransformPoint(RightGrip != null ? RightGrip.HandAnchorLocal : Model.RightGripAnchorLocal);
            return DeadliftContactStatics.Solve(_rig, _barbell.Body, _leftPlate, _rightPlate, la, ra, handPullN);
        }

        public float MeasuredPerHandUpN()
        {
            float sum = 0f;
            if (LeftGrip != null && LeftGrip.IsAttached) sum += -LeftGrip.SolverForceRaw.y;
            if (RightGrip != null && RightGrip.IsAttached) sum += -RightGrip.SolverForceRaw.y;
            return 0.5f * sum;
        }

        private void OnPostPhysicsStep(SimulationTime time, PhysicalObservation observation, PlayerIntentFrame intent)
        {
            double dt = SimulationConstants.FixedDeltaTimeSeconds;
            _tick = time.Tick;
            _barSensor.CompleteTick();
            PlateLeftN = (float)(_barSensor.LeftNormalImpulse / dt);
            PlateRightN = (float)(_barSensor.RightNormalImpulse / dt);
            BarOnLegN = (float)(_barSensor.LegNormalImpulse / dt);
            _rig.PoweredController.CapturePostPhysicsDiagnostics();

            // Grounded setup: the pretension servo takes up the slack (A3), as qualified.
            long t = (long)time.Tick;
            float target = t <= PretensionStartTick ? 0f : t <= PretensionStartTick + PretensionRampTicks
                ? _pretensionTargetN * (t - PretensionStartTick) / PretensionRampTicks : _pretensionTargetN;
            _pretension.TrySetTarget(target);

            LastObservation = Observer.Observe(time.Tick, time.Tick * dt);
            Referee.Observe(LastObservation);
            if (Referee.DownGiven)
                Pull.CommandDown(time.Tick);
            if (!GripsReleased && Pull.Phase == DeadliftPullPhase.Returned && Referee.Completed)
            {
                LeftGrip.Release();
                RightGrip.Release();
                GripsReleased = true;
            }
            TickCompleted?.Invoke(this);
        }
    }
}
