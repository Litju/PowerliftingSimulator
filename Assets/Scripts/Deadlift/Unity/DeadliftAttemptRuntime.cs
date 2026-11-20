using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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

        /// <summary>Shipped value of <see cref="BarProximityPath"/>.</summary>
        public const bool BarProximityPathDefault = false;
        /// <summary>
        /// Solve the ascent path with the phase-dependent bar-proximity objective
        /// (<see cref="DeadliftLiftPath.BarProximity"/>). Static so the A/B can run the control path on a matched plant.
        /// </summary>
        public static bool BarProximityPath = BarProximityPathDefault;

        private readonly FoundationBootstrap _foundation;
        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalBarbell _barbell;
        private DeadliftBarSensor _barSensor;
        private DeadliftGroundedReference _groundedRef;
        private DeadliftPretensionLoop _pretension;
        private float _pretensionTargetN;
        private Collider _leftPlate, _rightPlate;
        private ulong _tick;
        private DeadliftPreparedRejoin _preparedRejoin;
        private readonly Dictionary<PoweredJointController.PoweredJointRuntime, (Quaternion Initial, Vector3 Axis)> _shoulderStarts =
            new Dictionary<PoweredJointController.PoweredJointRuntime, (Quaternion Initial, Vector3 Axis)>();

        public DeadliftAttemptRuntime(FoundationBootstrap foundation, PhysicalAthleteRig rig, PhysicalBarbell barbell)
        {
            _foundation = foundation ?? throw new ArgumentNullException(nameof(foundation));
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _barbell = barbell ?? throw new ArgumentNullException(nameof(barbell));
        }

        public bool UpperChainPreparation { get; set; }

        /// <summary>Experimental prepared reference with stock late-ascent rejoin. Configure before Initialize.</summary>
        public bool PreparedClearanceRejoin { get; set; }
        /// <summary>Opt-in prepared ascent into the suspended close-bar endpoint.</summary>
        public bool PreparedCloseLockout { get; set; }
        /// <summary>The grip pretension servo has reached its final target (the grip is set).</summary>
        public bool PretensionEstablished => _pretension != null && _pretension.TargetPerHandN >= _pretensionTargetN &&
            (PlayerSetup || _tick > PretensionStartTick + PretensionRampTicks) &&
            Mathf.Min(LeftGrip.ForceOnBarWorld.y, RightGrip.ForceOnBarWorld.y) >= 0.95f * _pretensionTargetN;
        /// <summary>
        /// Player-owned setup (configure before Initialize): Grip takes the slack out of the bar,
        /// Brace braces the trunk and sets the lats, and Drive pulls only while both are held.
        /// Off, the qualified automatic schedule runs (tests and the stock control).
        /// </summary>
        public bool PlayerSetup { get; set; }
        public bool LastBraceHeld { get; private set; }
        /// <summary>What the internal preparation is waiting for while the player asks to pull (HUD).</summary>
        public string PreparationStatus { get; private set; } = string.Empty;
        public DeadliftPreparedAscentPlan PreparedAscentPlan =>
            _preparedRejoin?.IsReady == true ? _preparedRejoin.Plan : null;
        internal bool SolvePreparedPlanAcrossFrames { get; set; }
        public DeadliftPreparedAscentPlan PostKneeAscentPlan => _preparedRejoin?.PostKneePlan;
        internal DeadliftPreparedAscentPlan PendingPreparedPlan => _preparedRejoin?.PendingPlan;

        internal void AdvancePreparedSolve()
        {
            if (!_preparedRejoin.AdvanceSolve())
                return;

            _preparedRejoin.Update();
            _foundation.Runtime.TicksSuspended = PendingPreparedPlan != null;
        }

        /// <summary>
        /// Admit the close-bar references from the offline-generated artifact instead
        /// of solving them on the live plant (configure before Initialize). The
        /// artifact is validated while the attempt is prepared; at separation and
        /// knee passage the measured state is admitted inside the physics tick and
        /// the simulation is never suspended.
        /// </summary>
        public bool UseReferenceCache { get; set; }
        /// <summary>Artifact to admit; null loads <see cref="DeadliftCloseBarReference.ResourcePath"/>.</summary>
        public DeadliftCloseBarReference ReferenceArtifact { get; set; }
        public DeadliftReferenceAdmission Admission { get; private set; }
        /// <summary>Hash of the declared model inputs, taken when the attempt is composed (before tick 0).</summary>
        public string ModelFingerprint { get; private set; }
        /// <summary>The stock lift path was restored from the validated artifact instead of solved at load.</summary>
        public bool StockPathFromArtifact { get; private set; }
        public DeadliftReferenceAdmission.Result AscentAdmission => _preparedRejoin?.AscentAdmission;
        public DeadliftReferenceAdmission.Result PostKneeAdmission => _preparedRejoin?.PostKneeAdmission;
        /// <summary>Why the measured plant was not admitted to the reference family (empty when admitted).</summary>
        public string ReferenceRejection { get; private set; } = string.Empty;
        public ulong ReferenceRejectionTick { get; private set; }

        /// <summary>Load-stage wall time (ms) by stage, measured in Initialize.</summary>
        public readonly Dictionary<string, double> InitializationMilliseconds = new Dictionary<string, double>();
        /// <summary>Wall time (ms) of the attempt's post-physics work on the last tick, and its maximum.</summary>
        public double LastTickMilliseconds { get; private set; }
        public double MaxTickMilliseconds { get; private set; }
        public ulong MaxTickMillisecondsTick { get; private set; }
        private readonly Stopwatch _tickClock = new Stopwatch();

        /// <summary>Generator side: the solved references of this attempt as a versioned artifact.</summary>
        public DeadliftCloseBarReference RecordReference(string generator)
        {
            if (_preparedRejoin?.Plan == null || PostKneeAscentPlan == null)
                throw new InvalidOperationException("Both the ascent and the post-knee reference must be solved first.");
            return new DeadliftCloseBarReference
            {
                Fingerprint = ModelFingerprint,
                Generator = generator,
                JointIds = _rig.PoweredController.Joints.Select(j => j.Id).ToArray(),
                Prefix = DeadliftCloseBarReference.Record(_preparedRejoin.Plan),
                PostKnee = DeadliftCloseBarReference.Record(PostKneeAscentPlan),
                StockPath = Path.Capture()
            };
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
            var stage = Stopwatch.StartNew();
            void Stage(string name)
            {
                InitializationMilliseconds[name] = stage.Elapsed.TotalMilliseconds;
                stage.Restart();
            }
            _foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            if (_rig.PoweredController == null)
                _rig.Build();
            _rig.SetGameplayPerformanceProfile(true);

            var feasibility = new DeadliftSetupFeasibilitySolver(_rig);
            Measurements = feasibility.Measurements;
            DeadliftSetupSpec spec = DeadliftQualifiedSetup.SumoPronated();
            Model = Measurements.BuildModel(DeadliftQualifiedSetup.B2, spec);
            Stage("setup model");

            PlaceAtSetup();
            DeadliftAnkleRoll.Enable(_rig);
            ValidateTickZero();
            ModelFingerprint = DeadliftCloseBarReference.ComputeFingerprint(_rig, _barbell.Body);

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
            Stage("placement and grounded statics");

            Pull = new DeadliftPullController(_rig, _barbell, () => PlateLeftN, () => PlateRightN)
            {
                RampPerSecond = 1f, ReleasePerSecond = 2f,
                HipExtensionLimitDeg = 2.5f, KneeExtensionLimitDeg = 2f, TrunkExtensionLimitDeg = -1.5f,
                MaxComShiftM = 0.05f, GovernorReleaseM = 0.03f,
                RequireBraceAndGrip = PlayerSetup,   // player setup: Drive pulls only with Grip and Brace held
                RearmAfterAbort = true,
                LiftEnabled = true, LiftRatePerSecond = LiftRatePerSecond, LiftAccelPerSecond2 = LiftAccelPerSecond2
            };
            Hold.Pull = Pull;

            Path = new DeadliftLiftPath(_rig, Measurements, Model, _barbell.Body.mass) { BarProximity = BarProximityPath && !PreparedClearanceRejoin && !PreparedCloseLockout };
            Path.RollPlanLimitDeg = Mathf.Min(Path.RollPlanLimitDeg, DeadliftAnkleRoll.LimitDegrees - Path.JointMarginDeg - Regulator.MaxRollOffsetDeg);
            DeadliftCloseBarReference artifact = null;
            string artifactFailure = null;
            if (UseReferenceCache)
            {
                artifact = ReferenceArtifact ?? DeadliftCloseBarReference.LoadResource(out artifactFailure);
                Stage("reference artifact load");
            }
            // The stock path is state independent: restore it from a matching artifact, otherwise solve it here.
            StockPathFromArtifact = artifact?.StockPath != null && !Path.BarProximity &&
                string.Equals(ModelFingerprint, artifact.Fingerprint, StringComparison.Ordinal);
            if (StockPathFromArtifact)
                Path.Restore(artifact.StockPath);
            else
                Path.Solve();
            Stage(StockPathFromArtifact ? "stock lift path (artifact)" : "stock lift path (solved)");
            if (Path.BarProximity)
            {
                // Same joint speed and acceleration as the qualified Gate 6 path (deg/s), whatever this path's length.
                float k = DeadliftLiftPath.QualifiedJointSpeedDegPerS / (LiftRatePerSecond * Path.JointPathLengthDeg);
                Pull.LiftRatePerSecond = LiftRatePerSecond * k;
                Pull.LowerRatePerSecond *= k;
                Pull.LiftAccelPerSecond2 = LiftAccelPerSecond2 * k;
            }
            PathReport = Path.Describe();
            Hold.LiftPath = Path;
            Hold.MovingPhaseIntegralLeakSeconds = 0.10f;
            Hold.MovingPhaseSystemCom = true;
            Monitor = new DeadliftSupportMonitor(_rig, _barbell.Body, _rig.PlatformCollider);
            Pull.SupportMarginM = () =>
            {
                float potential = Monitor.Update();
                if (Regulator.ContactConsistentControl && (Monitor.LoadedFeet < 2 || Monitor.ActiveMarginM < 0f))
                    return float.NegativeInfinity;
                return potential;
            };
            Pull.ReferenceDemand = () => Hold.HandLoadReference.MaxDemandFraction;
            if (UpperChainPreparation || PreparedClearanceRejoin || PreparedCloseLockout)
                Pull.GroundedReady = PrepareForPull;

            if (PreparedCloseLockout && UseReferenceCache)
            {
                string failure = artifactFailure;
                if (artifact != null)
                    Admission = DeadliftReferenceAdmission.Prepare(this, _rig, artifact, out failure);
                Stage("reference validation and gains");
                if (Admission == null)
                    throw new InvalidOperationException("The close-bar reference family cannot be admitted for this athlete: " + failure);
                Pull.AdmittedProgress = () => _preparedRejoin?.AdmittedProgress ?? 1f;
            }

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

        private bool PrepareForPull()
        {
            // Rearmed pulls keep the measured hand-load owner established on the first pull.
            if (Hold.HandLoadClaimed)
                return true;
            // Measured readiness (no tick gate): the grip pretension is set and the athlete and bar are
            // quasi-static by the rulebook's own motionless criterion, before the shoulders are set.
            if (!PretensionEstablished)
                return Preparing(PlayerSetup ? "hold Left Shift to take the slack out of the bar" : "setting the grip pretension");
            if (PlayerSetup && !LastBraceHeld)
                return Preparing("hold Space to brace and set the lats");
            if (PlateLeftN <= DeadliftPullController.SeparationPlateN || PlateRightN <= DeadliftPullController.SeparationPlateN)
                return Preparing("the bar must rest on both plates");
            if (!LoadedCop("left_foot") || !LoadedCop("right_foot"))
                return Preparing("both feet must be loaded flat");
            if (Monitor.Update() < Pull.MinSupportMarginM)
                return Preparing("balance over the feet");
            if (_shoulderStarts.Count == 0 && (Bar.linearVelocity.magnitude >= DeadliftRulebook.MotionlessSpeedMps ||
                    Monitor.SystemVelocity.magnitude >= DeadliftRulebook.MotionlessSpeedMps))
                return Preparing("settling: bar and body must be still");

            // Use the grounded B2 transfer probe's bounded extension reference.
            if (_shoulderStarts.Count == 0)
            {
                foreach (var shoulder in _rig.PoweredController.Joints)
                {
                    if (shoulder.Recipe.Family != "shoulder")
                        continue;
                    Vector3 axis = DeadliftPreparedRejoin.ShoulderExtensionAxis(_rig, shoulder);
                    _shoulderStarts.Add(shoulder, (shoulder.PostPhysicsDiagnostic.ActualRelative, axis));
                    Hold.PreparationOffsets.Add(shoulder.Id, Quaternion.AngleAxis(2f, axis));
                }
                return Preparing("setting the shoulders");
            }

            // Gate 5 measured ~270 N per hand at the grounded endpoint. The transfer
            // experiment retained ~265 N with both plates supported.
            Vector3 left = LeftGrip.ForceOnBarWorld, right = RightGrip.ForceOnBarWorld;
            if (Mathf.Min(left.y, right.y) < 0.95f * _pretensionTargetN || left.z + right.z >= -10f)
                return Preparing("loading the hands");

            foreach (var shoulder in _shoulderStarts)
            {
                var d = shoulder.Key.PostPhysicsDiagnostic;
                var start = shoulder.Value;
                float realized = PoweredJointController.SignedTwistRadians(Quaternion.Inverse(start.Initial) * d.ActualRelative, start.Axis) * Mathf.Rad2Deg;
                if (realized < 0.5f || d.ModeledDemand >= 1f)
                    return Preparing("setting the shoulders");
            }

            foreach (var trunk in _rig.PoweredController.Joints)
            {
                if (trunk.Id != "abdomen" && trunk.Id != "thorax")
                    continue;
                var d = trunk.PostPhysicsDiagnostic;
                if (d.ModeledDemand >= 1f || Quaternion.Angle(Model.SetupLogical[trunk.Id], d.ActualRelative) > 3f)
                    return Preparing("bracing the trunk");
            }
            PreparationStatus = "ready";
            return true;
        }

        private bool Preparing(string reason)
        {
            PreparationStatus = reason;
            return false;
        }

        private void RejectIfNotAdmitted()
        {
            if (_preparedRejoin == null || !_preparedRejoin.Rejected || ReferenceRejectionTick != 0)
                return;
            ReferenceRejection = _preparedRejoin.RejectionReason;
            ReferenceRejectionTick = _tick;
            Pull.CommandReturn(_tick);
            UnityEngine.Debug.LogWarning($"GAM-70 tick {_tick}: {ReferenceRejection}. Returning the bar.");
        }

        private bool LoadedCop(string id)
        {
            var sensor = _rig.Segments[id].Body.GetComponent<DeadliftFootPressureSensor>();
            float cop = Vector3.Dot(sensor.Cop, sensor.Axis);
            return sensor.NormalImpulse / SimulationConstants.FixedDeltaTimeSeconds > Monitor.LoadedFootN &&
                cop > sensor.LoadedAxisMin && cop < sensor.LoadedAxisMax;
        }

        private void OnPostPhysicsStep(SimulationTime time, PhysicalObservation observation, PlayerIntentFrame intent)
        {
            _tickClock.Restart();
            double dt = SimulationConstants.FixedDeltaTimeSeconds;
            _tick = time.Tick;
            _barSensor.CompleteTick();
            PlateLeftN = (float)(_barSensor.LeftNormalImpulse / dt);
            PlateRightN = (float)(_barSensor.RightNormalImpulse / dt);
            BarOnLegN = (float)(_barSensor.LegNormalImpulse / dt);
            _rig.PoweredController.CapturePostPhysicsDiagnostics();

            if (_shoulderStarts.Count != 0 && (Pull.Phase >= DeadliftPullPhase.Separated ||
                Pull.Phase == DeadliftPullPhase.Aborted || Pull.Intent01 <= 0.05f))
            {
                _shoulderStarts.Clear();
                try
                {
                    if ((PreparedClearanceRejoin || PreparedCloseLockout) && Pull.Phase == DeadliftPullPhase.Separated)
                    {
                        _preparedRejoin = new DeadliftPreparedRejoin(this, _rig, SolvePreparedPlanAcrossFrames && Admission == null);
                        _foundation.Runtime.TicksSuspended = PendingPreparedPlan != null;
                        RejectIfNotAdmitted();
                    }
                }
                finally
                {
                    Hold.PreparationOffsets.Clear();
                }
            }

            // Grounded setup: build upward hand load while the plates still support the bar.
            long t = (long)time.Tick;
            float target;
            if (PlayerSetup)
            {
                // Grip (Left Shift) takes the slack out: the hand-load target rises at the qualified
                // ramp rate while it is held and relaxes at the same rate when it is released.
                float step = _pretensionTargetN / PretensionRampTicks;
                bool hold = intent.Grip01 >= 0.5f || Hold.HandLoadClaimed;
                target = Mathf.Clamp(_pretension.TargetPerHandN + (hold ? step : -step), 0f, _pretensionTargetN);
            }
            else
            {
                target = t <= PretensionStartTick ? 0f : t <= PretensionStartTick + PretensionRampTicks
                    ? _pretensionTargetN * (t - PretensionStartTick) / PretensionRampTicks : _pretensionTargetN;
            }
            _pretension.TrySetTarget(target);
            LastBraceHeld = intent.Brace01 >= 0.5f;

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
            if (_preparedRejoin?.IsReady == true && !_preparedRejoin.Rejected)
            {
                _preparedRejoin.Update();
                _foundation.Runtime.TicksSuspended = PendingPreparedPlan != null;
                RejectIfNotAdmitted();
            }
            LastTickMilliseconds = _tickClock.Elapsed.TotalMilliseconds;
            if (LastTickMilliseconds > MaxTickMilliseconds)
            {
                MaxTickMilliseconds = LastTickMilliseconds;
                MaxTickMillisecondsTick = time.Tick;
            }
            TickCompleted?.Invoke(this);
        }
    }
}
