using System;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Converts squat intent and the previous post-physics observation into
    /// finite joint targets. It never writes a Rigidbody force, torque,
    /// velocity, or transform; PoweredJointController remains the sole drive
    /// writer inside the one registered pre-physics callback.
    /// </summary>
    public sealed class SquatPhysicalAdapter : IPhysicalAthleteCommandSource
    {
        public const float MaxBalanceCorrectionRad = 0.17453f; // 10 degrees; target offset only
        private const float MaxMlBalanceCorrectionRad = 0.03491f; // 2 degrees; bounded lateral target trim
        public const float MaxBalanceBiasM = 0.025f; // 2.5 cm player balance bias
        public const float DefaultApKp = 0.80f;
        public const float DefaultApKd = 0.08f;
        public const float DefaultMlKp = 0.65f;
        public const float DefaultMlKd = 0.05f;
        public const float DefaultHipKp = 0.80f;
        public const float DefaultTrunkKp = 1.50f;
        private const float SupportFailureApErrorM = 0.30f;
        private const float SupportFailureMlErrorM = 0.30f;

        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalAthleteRig.SegmentRuntime[] _segments;
        private readonly SquatReferenceProfile _profile;
        private readonly ReferenceTargetFrame[] _descentTargets;
        private readonly ReferenceTargetFrame[] _ascentTargets;
        private const int ReferenceTargetSampleCount = 101;
        private const float LockoutTransitionSq = 0.001f;
        private SquatState _state = SquatState.SETUP;
        private SquatPhaseDirection _direction = SquatPhaseDirection.None;
        private float _sq;
        // Slow enough for the finite drives and contact solver to settle at
        // each meaningful waypoint; this is gameplay pacing, not extra
        // physical authority.
        private float _phaseRate = 0.30f;
        private bool _autoCycle;
        private float _bottomHoldTimer;
        private const float BottomHoldDuration = 0.20f;
        private const float ReversalHoldDuration = 0.10f;
        private float _reversalHoldTimer;
        private SquatBarSaddle _saddle;

        // Telemetry is sampled from the previous post-physics observation.
        private Vector3 _systemCom;
        private Vector3 _supportCenter;
        private float _apComError;
        private float _mlComError;
        private float _balanceCorrectionRad;
        private float _mlBalanceCorrectionRad;
        private bool _isCorrectionSaturated;
        private bool _isDriveSaturated;
        private float _maxDriveSaturation;
        private float _minPelvisHeightM = float.PositiveInfinity;
        private bool _lockoutReached;
        private float _previousComZ;
        private float _previousComX;
        private bool _hasPreviousCom;
        private string _failureReason = "NONE";

        public SquatPhysicalAdapter(PhysicalAthleteRig rig, SquatReferenceProfile profile = null)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _profile = profile ?? SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            _segments = new PhysicalAthleteRig.SegmentRuntime[_rig.Segments.Count];
            int index = 0;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
                _segments[index++] = segment;
            BuildReferenceTargetTables(out _descentTargets, out _ascentTargets);
            _rig.SetCommandSource(this);
            _sq = 0f;
        }

        public SquatBarSaddle Saddle => _saddle;
        public SquatState State => _state;
        public SquatPhaseDirection Direction => _direction;
        public float Sq => _sq;
        public float ApComError => _apComError;
        public float MlComError => _mlComError;
        public Vector3 SystemCom => _systemCom;
        public Vector3 SupportCenter => _supportCenter;
        public float BalanceCorrectionRad => _balanceCorrectionRad;
        public float MlBalanceCorrectionRad => _mlBalanceCorrectionRad;
        public bool IsCorrectionSaturated => _isCorrectionSaturated;
        public bool IsDriveSaturated => _isDriveSaturated;
        public float MaxDriveSaturation => _maxDriveSaturation;
        public float MinPelvisHeightM => _minPelvisHeightM;
        public bool LockoutReached => _lockoutReached;
        public string FailureReason => _failureReason;
        public bool AutoCycle { get => _autoCycle; set => _autoCycle = value; }
        public float PhaseRate { get => _phaseRate; set => _phaseRate = Mathf.Clamp(value, 0.05f, 0.75f); }

        public void SetSaddle(SquatBarSaddle saddle)
        {
            _saddle = saddle;
            if (saddle != null)
                _failureReason = "NONE";
        }

        public void SetState(SquatState state)
        {
            _state = state;
            if (state == SquatState.LOCKOUT)
            {
                _sq = 0f;
                _direction = SquatPhaseDirection.None;
            }
        }

        public void Reset()
        {
            _state = SquatState.SETUP;
            _direction = SquatPhaseDirection.None;
            _sq = 0f;
            _autoCycle = false;
            _bottomHoldTimer = 0f;
            _reversalHoldTimer = 0f;
            _lockoutReached = false;
            _failureReason = "NONE";
            _minPelvisHeightM = float.PositiveInfinity;
            _systemCom = Vector3.zero;
            _supportCenter = Vector3.zero;
            _apComError = 0f;
            _mlComError = 0f;
            _balanceCorrectionRad = 0f;
            _mlBalanceCorrectionRad = 0f;
            _isCorrectionSaturated = false;
            _isDriveSaturated = false;
            _maxDriveSaturation = 0f;
            _hasPreviousCom = false;
        }

        // Retained for deterministic qualification fixtures. Owner gameplay
        // uses Brace/Yield/Drive through PlayerIntentFrame instead.
        public void StartSquat()
        {
            _state = SquatState.DESCENT;
            _direction = SquatPhaseDirection.Descent;
            _autoCycle = true;
            _lockoutReached = false;
            _failureReason = "NONE";
        }

        public void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            PoweredJointController poweredController)
        {
            if (poweredController == null)
                throw new ArgumentNullException(nameof(poweredController));

            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            AdvanceStateAndPhase(dt, intent);
            ComputeComAndSupport(previousObservation, intent);

            if (_saddle != null && _saddle.IsBroken)
            {
                _state = SquatState.FAILURE;
                _direction = SquatPhaseDirection.None;
                _failureReason = "SADDLE_ERROR";
            }
            else if (_failureReason == "NONE" && _state != SquatState.SETUP &&
                     (Mathf.Abs(_apComError) > SupportFailureApErrorM ||
                      Mathf.Abs(_mlComError) > SupportFailureMlErrorM))
            {
                // Diagnostic only: the dynamic bodies continue to simulate so
                // the owner can inspect the finite physical failure.
                _failureReason = "COM_OUTSIDE_SUPPORT";
            }

            float brace = Mathf.Max(intent.Brace01, intent.BraceHeld ? 1f : 0f);

            float comVelocityZ = _hasPreviousCom ? (_systemCom.z - _previousComZ) / dt : 0f;
            float comVelocityX = _hasPreviousCom ? (_systemCom.x - _previousComX) / dt : 0f;
            float ankleCorrection = CalculateBalanceOffset(_apComError, comVelocityZ, dt);
            float mlCorrection = CalculateBalanceOffset(
                _mlComError,
                comVelocityX,
                dt,
                DefaultMlKp,
                DefaultMlKd,
                MaxMlBalanceCorrectionRad);
            _balanceCorrectionRad = ankleCorrection;
            _mlBalanceCorrectionRad = mlCorrection;
            _isCorrectionSaturated = Mathf.Abs(ankleCorrection) >= MaxBalanceCorrectionRad - 0.0001f ||
                Mathf.Abs(mlCorrection) >= MaxBalanceCorrectionRad - 0.0001f;
            _previousComZ = _systemCom.z;
            _previousComX = _systemCom.x;
            _hasPreviousCom = true;

            // Existing GAM-7 family profiles remain the capacity starting
            // authority. Load changes the coupled dynamics; it does not select
            // a scripted success/failure branch.
            float barMass = _saddle != null && _saddle.IsAttached && _saddle.Barbell != null &&
                _saddle.Barbell.Body != null && _saddle.Barbell.Body.gameObject.activeInHierarchy
                ? _saddle.Barbell.LoadedMassKg
                : 0f;
            float loadRatio = (_rig.TotalMassKg + barMass) / Mathf.Max(_rig.TotalMassKg, 0.001f);
            float capacityScale = Mathf.Max(3.8f, loadRatio * 3.8f) * (1f + 0.35f * brace);

            if (_rig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvisSegment) && pelvisSegment.Body != null)
                _minPelvisHeightM = Mathf.Min(_minPelvisHeightM, pelvisSegment.Body.position.y);

            ulong tick = time.Tick;
            float trunkCorrection = Mathf.Clamp(-DefaultTrunkKp * _apComError, -0.25f, 0.25f);
            ReferenceTargetFrame referenceTarget = EvaluateReferenceTarget();
            Quaternion ankleOffset = SagittalAndFrontal(-ankleCorrection * 3.0f, mlCorrection);
            Quaternion kneeOffset = SagittalAndFrontal(0f, mlCorrection * 0.45f);
            Quaternion hipOffset = Quaternion.identity;
            Quaternion braceOffset = SagittalAndFrontal(-UnitContract.DegreesToRadians(3f) * brace, 0f);
            Quaternion trunkOffset = braceOffset * SagittalAndFrontal(trunkCorrection, mlCorrection * 0.50f);

            Quaternion leftAnkleTarget = referenceTarget.LeftFoot * ankleOffset;
            Quaternion rightAnkleTarget = referenceTarget.RightFoot * ankleOffset;
            Quaternion leftKneeTarget = referenceTarget.LeftShank * kneeOffset;
            Quaternion rightKneeTarget = referenceTarget.RightShank * kneeOffset;
            Quaternion leftHipTarget = referenceTarget.LeftThigh * hipOffset;
            Quaternion rightHipTarget = referenceTarget.RightThigh * hipOffset;
            Quaternion abdomenTarget = referenceTarget.Abdomen * trunkOffset;
            Quaternion thoraxTarget = referenceTarget.Thorax * trunkOffset;

            poweredController.ApplyCommand("left_foot", new JointCommand(leftAnkleTarget, Vector3.zero, 1f, capacityScale * 2.5f), tick);
            poweredController.ApplyCommand("right_foot", new JointCommand(rightAnkleTarget, Vector3.zero, 1f, capacityScale * 2.5f), tick);
            poweredController.ApplyCommand("left_shank", new JointCommand(leftKneeTarget, Vector3.zero, 1f, capacityScale * 1.8f), tick);
            poweredController.ApplyCommand("right_shank", new JointCommand(rightKneeTarget, Vector3.zero, 1f, capacityScale * 1.8f), tick);
            poweredController.ApplyCommand("left_thigh", new JointCommand(leftHipTarget, Vector3.zero, 1f, capacityScale * 1.5f), tick);
            poweredController.ApplyCommand("right_thigh", new JointCommand(rightHipTarget, Vector3.zero, 1f, capacityScale * 1.5f), tick);
            poweredController.ApplyCommand("abdomen", new JointCommand(abdomenTarget, Vector3.zero, 1f, capacityScale * 1.5f), tick);
            poweredController.ApplyCommand("thorax", new JointCommand(thoraxTarget, Vector3.zero, 1f, capacityScale * 1.5f), tick);

            ApplyArmSupport(poweredController, capacityScale, tick);
            CheckDriveSaturation(poweredController);
        }

        private void AdvanceStateAndPhase(float dt, PlayerIntentFrame intent)
        {
            float yieldInput = Mathf.Max(intent.Yield01, intent.YieldHeld ? 1f : 0f);
            float driveInput = Mathf.Max(intent.Drive01, intent.DriveHeld ? 1f : 0f);
            bool braceInput = intent.Brace01 > 0.05f || intent.BraceHeld || intent.WasPressed(IntentAction.Brace) ||
                intent.WasPressed(IntentAction.Confirm) || intent.ConfirmHeld;

            if (_autoCycle)
            {
                switch (_state)
                {
                    case SquatState.SETUP:
                    case SquatState.SETTLE:
                    case SquatState.SQUAT_COMMAND:
                        _state = SquatState.DESCENT;
                        _direction = SquatPhaseDirection.Descent;
                        break;
                    case SquatState.DESCENT:
                        _sq = Mathf.Min(1f, _sq + _phaseRate * dt);
                        if (_sq >= 0.999f)
                        {
                            _sq = 1f;
                            _state = SquatState.BOTTOM;
                            _bottomHoldTimer = BottomHoldDuration;
                        }
                        break;
                    case SquatState.BOTTOM:
                        _bottomHoldTimer -= dt;
                        if (_bottomHoldTimer <= 0f)
                        {
                            _state = SquatState.REVERSAL;
                            _direction = SquatPhaseDirection.Ascent;
                            _reversalHoldTimer = ReversalHoldDuration;
                        }
                        break;
                    case SquatState.REVERSAL:
                        _reversalHoldTimer -= dt;
                        if (_reversalHoldTimer <= 0f)
                            _state = SquatState.ASCENT;
                        break;
                    case SquatState.ASCENT:
                        _sq = Mathf.Max(0f, _sq - _phaseRate * dt);
                        if (_sq <= LockoutTransitionSq)
                        {
                            _sq = 0f;
                            _state = SquatState.LOCKOUT;
                            _direction = SquatPhaseDirection.None;
                            _lockoutReached = true;
                            _autoCycle = false;
                        }
                        break;
                    case SquatState.LOCKOUT:
                        _sq = 0f;
                        break;
                }
                return;
            }

            // Manual mode is intent-driven. Brace/Confirm arms the squat;
            // Yield and Drive only alter the reference phase.
            if ((_state == SquatState.SETUP || _state == SquatState.LOCKOUT) && braceInput)
            {
                _state = SquatState.SQUAT_COMMAND;
                _direction = SquatPhaseDirection.None;
                _lockoutReached = false;
            }

            if (yieldInput > 0.05f && (_state == SquatState.SETUP || _state == SquatState.SQUAT_COMMAND || _state == SquatState.LOCKOUT))
            {
                _state = SquatState.DESCENT;
                _direction = SquatPhaseDirection.Descent;
            }

            if (_state == SquatState.DESCENT && yieldInput > 0.05f)
            {
                _sq = Mathf.Min(1f, _sq + _phaseRate * yieldInput * dt);
                if (_sq >= 0.999f)
                {
                    _sq = 1f;
                    _state = SquatState.BOTTOM;
                    _bottomHoldTimer = 0f;
                }
            }

            if (_state == SquatState.BOTTOM && driveInput > 0.05f)
            {
                _state = SquatState.REVERSAL;
                _direction = SquatPhaseDirection.Ascent;
                _reversalHoldTimer = ReversalHoldDuration;
            }

            if (_state == SquatState.REVERSAL && driveInput > 0.05f)
            {
                _reversalHoldTimer -= dt;
                if (_reversalHoldTimer <= 0f)
                    _state = SquatState.ASCENT;
            }

            if (_state == SquatState.ASCENT && driveInput > 0.05f)
            {
                _sq = Mathf.Max(0f, _sq - _phaseRate * driveInput * dt);
                if (_sq <= LockoutTransitionSq)
                {
                    _sq = 0f;
                    _state = SquatState.LOCKOUT;
                    _direction = SquatPhaseDirection.None;
                    _lockoutReached = true;
                }
            }
        }

        private void ComputeComAndSupport(PhysicalObservation observation, PlayerIntentFrame intent)
        {
            Vector3 weightedPosition = Vector3.zero;
            float totalMass = 0f;
            bool hasObservation = observation.BodyCount > 0;

            for (int index = 0; index < _segments.Length; index++)
            {
                PhysicalAthleteRig.SegmentRuntime segment = _segments[index];
                Vector3 position = segment.Body.worldCenterOfMass;
                float mass = segment.Body.mass;
                if (hasObservation && observation.TryGetBody(segment.Recipe.Id, out PhysicalBodyObservation bodyObservation))
                {
                    position = new Vector3(
                        bodyObservation.PositionMeters.X,
                        bodyObservation.PositionMeters.Y,
                        bodyObservation.PositionMeters.Z);
                    mass = bodyObservation.MassKilograms;
                }
                weightedPosition += position * mass;
                totalMass += mass;
            }

            if (_saddle != null && _saddle.IsAttached && _saddle.Barbell != null && _saddle.Barbell.Body != null &&
                _saddle.Barbell.Body.gameObject.activeInHierarchy)
            {
                Vector3 barPosition = _saddle.Barbell.Body.worldCenterOfMass;
                float barMass = _saddle.Barbell.LoadedMassKg;
                if (hasObservation && observation.TryGetBody("barbell", out PhysicalBodyObservation barObservation))
                {
                    barPosition = new Vector3(
                        barObservation.PositionMeters.X,
                        barObservation.PositionMeters.Y,
                        barObservation.PositionMeters.Z);
                    barMass = barObservation.MassKilograms;
                }
                weightedPosition += barPosition * barMass;
                totalMass += barMass;
            }

            _systemCom = totalMass > 0f ? weightedPosition / totalMass : Vector3.zero;

            Vector3 leftFoot = FootPosition("left_foot", observation, hasObservation);
            Vector3 rightFoot = FootPosition("right_foot", observation, hasObservation);
            _supportCenter = (leftFoot + rightFoot) * 0.5f;
            float balanceBias = Mathf.Clamp(intent.BalanceX, -1f, 1f) * MaxBalanceBiasM;
            _apComError = _systemCom.z - _supportCenter.z;
            _mlComError = _systemCom.x - (_supportCenter.x + balanceBias);
        }

        private Vector3 FootPosition(string id, PhysicalObservation observation, bool hasObservation)
        {
            if (!_rig.Segments.TryGetValue(id, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return Vector3.zero;
            if (hasObservation && observation.TryGetBody(id, out PhysicalBodyObservation footObservation))
            {
                return new Vector3(
                    footObservation.PositionMeters.X,
                    footObservation.PositionMeters.Y,
                    footObservation.PositionMeters.Z);
            }
            return foot.Body.position;
        }

        private static Quaternion SagittalAndFrontal(float sagittalRad, float frontalRad)
        {
            Quaternion sagittal = Quaternion.AngleAxis(sagittalRad * Mathf.Rad2Deg, Vector3.right);
            Quaternion frontal = Quaternion.AngleAxis(frontalRad * Mathf.Rad2Deg, Vector3.forward);
            return sagittal * frontal;
        }

        private void ApplyArmSupport(PoweredJointController controller, float capacityScale, ulong tick)
        {
            // Hands are presentation-only in V1. Keep the arm chain at its
            // authored neutral posture so it cannot become an asymmetric
            // upper-body torque while the thorax is balanced under the bar.
            Quaternion leftShoulderRot = Quaternion.identity;
            Quaternion rightShoulderRot = Quaternion.identity;
            Quaternion elbowRot = Quaternion.identity;
            Quaternion wristRot = Quaternion.identity;

            controller.ApplyCommand("left_upper_arm", new JointCommand(leftShoulderRot, Vector3.zero, 1f, capacityScale), tick);
            controller.ApplyCommand("right_upper_arm", new JointCommand(rightShoulderRot, Vector3.zero, 1f, capacityScale), tick);
            controller.ApplyCommand("left_forearm", new JointCommand(elbowRot, Vector3.zero, 1f, capacityScale), tick);
            controller.ApplyCommand("right_forearm", new JointCommand(elbowRot, Vector3.zero, 1f, capacityScale), tick);
            controller.ApplyCommand("left_hand", new JointCommand(wristRot, Vector3.zero, 1f, capacityScale), tick);
            controller.ApplyCommand("right_hand", new JointCommand(wristRot, Vector3.zero, 1f, capacityScale), tick);
        }

        private void BuildReferenceTargetTables(
            out ReferenceTargetFrame[] descentTargets,
            out ReferenceTargetFrame[] ascentTargets)
        {
            Animator referenceAnimator = _rig.ReferenceAnimator;
            if (referenceAnimator == null)
                throw new InvalidOperationException("GAM-11 physical squat requires the canonical reference animator.");

            SquatReferenceRigCalibration calibration = SquatReferenceRigCalibration.Build(
                referenceAnimator,
                referenceAnimator.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");

            Vector3 leftStandingFootAnchor = calibration.LeftFoot.PlantarAnchorWorld;
            Vector3 rightStandingFootAnchor = calibration.RightFoot.PlantarAnchorWorld;
            descentTargets = new ReferenceTargetFrame[ReferenceTargetSampleCount];
            ascentTargets = new ReferenceTargetFrame[ReferenceTargetSampleCount];
            for (int index = 0; index < ReferenceTargetSampleCount; index++)
            {
                float phase = index / (float)(ReferenceTargetSampleCount - 1);
                descentTargets[index] = BuildReferenceTargetFrame(
                    calibration,
                    SquatPhaseDirection.Descent,
                    phase,
                    leftStandingFootAnchor,
                    rightStandingFootAnchor);
                ascentTargets[index] = BuildReferenceTargetFrame(
                    calibration,
                    SquatPhaseDirection.Ascent,
                    phase,
                    leftStandingFootAnchor,
                    rightStandingFootAnchor);
            }
        }

        private ReferenceTargetFrame BuildReferenceTargetFrame(
            SquatReferenceRigCalibration calibration,
            SquatPhaseDirection direction,
            float phase,
            Vector3 leftStandingFootAnchor,
            Vector3 rightStandingFootAnchor)
        {
            SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                calibration,
                _profile.Evaluate(phase, direction),
                leftStandingFootAnchor,
                rightStandingFootAnchor);
            if (!solution.IsValid)
                throw new InvalidOperationException($"GAM-11 reference target calibration is invalid at phase {phase:F3}: {solution.RejectionReason}");

            Quaternion pelvis = ToPhysicalBodyRotation("pelvis", solution.PelvisBoneRotation);
            Quaternion abdomen = ToPhysicalBodyRotation(
                "abdomen",
                solution.SpineFrameRotation * calibration.Spine.BoneFromAnatomicalFrame);
            Quaternion thorax = ToPhysicalBodyRotation(
                "thorax",
                solution.ChestFrameRotation * calibration.Chest.BoneFromAnatomicalFrame);
            Quaternion leftThigh = ToPhysicalBodyRotation("left_thigh", solution.LeftLeg.ThighBoneRotation);
            Quaternion rightThigh = ToPhysicalBodyRotation("right_thigh", solution.RightLeg.ThighBoneRotation);
            Quaternion leftShank = ToPhysicalBodyRotation("left_shank", solution.LeftLeg.ShankBoneRotation);
            Quaternion rightShank = ToPhysicalBodyRotation("right_shank", solution.RightLeg.ShankBoneRotation);
            Quaternion leftFoot = ToPhysicalBodyRotation("left_foot", solution.LeftLeg.FootBoneRotation);
            Quaternion rightFoot = ToPhysicalBodyRotation("right_foot", solution.RightLeg.FootBoneRotation);

            return new ReferenceTargetFrame(
                ToLogicalJointTarget("left_foot", leftShank, leftFoot),
                ToLogicalJointTarget("right_foot", rightShank, rightFoot),
                ToLogicalJointTarget("left_shank", leftThigh, leftShank),
                ToLogicalJointTarget("right_shank", rightThigh, rightShank),
                ToLogicalJointTarget("left_thigh", pelvis, leftThigh),
                ToLogicalJointTarget("right_thigh", pelvis, rightThigh),
                ToLogicalJointTarget("abdomen", pelvis, abdomen),
                ToLogicalJointTarget("thorax", abdomen, thorax));
        }

        private Quaternion ToPhysicalBodyRotation(string segmentId, Quaternion desiredBoneRotation)
        {
            PhysicalAthleteRig.SegmentRuntime segment = _rig.Segments[segmentId];
            return desiredBoneRotation * Quaternion.Inverse(segment.BodyToReferenceBoneRotation);
        }

        private Quaternion ToLogicalJointTarget(string childId, Quaternion desiredParentRotation, Quaternion desiredChildRotation)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(childId);
            Quaternion desiredRelative = Quaternion.Inverse(desiredParentRotation) * desiredChildRotation;
            Quaternion neutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * desiredRelative;
            return Quaternion.Inverse(joint.JointSpace) * neutralDelta * joint.JointSpace;
        }

        private ReferenceTargetFrame EvaluateReferenceTarget()
        {
            ReferenceTargetFrame[] targets = _direction == SquatPhaseDirection.Ascent ? _ascentTargets : _descentTargets;
            float scaled = Mathf.Clamp01(_sq) * (ReferenceTargetSampleCount - 1);
            int lowerIndex = Mathf.FloorToInt(scaled);
            int upperIndex = Mathf.Min(ReferenceTargetSampleCount - 1, lowerIndex + 1);
            float interpolation = scaled - lowerIndex;
            return ReferenceTargetFrame.Interpolate(targets[lowerIndex], targets[upperIndex], interpolation);
        }

        private readonly struct ReferenceTargetFrame
        {
            public ReferenceTargetFrame(
                Quaternion leftFoot,
                Quaternion rightFoot,
                Quaternion leftShank,
                Quaternion rightShank,
                Quaternion leftThigh,
                Quaternion rightThigh,
                Quaternion abdomen,
                Quaternion thorax)
            {
                LeftFoot = leftFoot;
                RightFoot = rightFoot;
                LeftShank = leftShank;
                RightShank = rightShank;
                LeftThigh = leftThigh;
                RightThigh = rightThigh;
                Abdomen = abdomen;
                Thorax = thorax;
            }

            public Quaternion LeftFoot { get; }
            public Quaternion RightFoot { get; }
            public Quaternion LeftShank { get; }
            public Quaternion RightShank { get; }
            public Quaternion LeftThigh { get; }
            public Quaternion RightThigh { get; }
            public Quaternion Abdomen { get; }
            public Quaternion Thorax { get; }

            public static ReferenceTargetFrame Interpolate(
                ReferenceTargetFrame from,
                ReferenceTargetFrame to,
                float interpolation)
            {
                return new ReferenceTargetFrame(
                    Quaternion.Slerp(from.LeftFoot, to.LeftFoot, interpolation),
                    Quaternion.Slerp(from.RightFoot, to.RightFoot, interpolation),
                    Quaternion.Slerp(from.LeftShank, to.LeftShank, interpolation),
                    Quaternion.Slerp(from.RightShank, to.RightShank, interpolation),
                    Quaternion.Slerp(from.LeftThigh, to.LeftThigh, interpolation),
                    Quaternion.Slerp(from.RightThigh, to.RightThigh, interpolation),
                    Quaternion.Slerp(from.Abdomen, to.Abdomen, interpolation),
                    Quaternion.Slerp(from.Thorax, to.Thorax, interpolation));
            }
        }

        private void CheckDriveSaturation(PoweredJointController controller)
        {
            float maximum = 0f;
            for (int index = 0; index < controller.Joints.Count; index++)
            {
                PoweredJointController.PoweredJointRuntime joint = controller.Joints[index];
                if (joint.Profile.HasValue)
                    maximum = Mathf.Max(maximum, joint.Diagnostic.ModeledDemand);
            }
            _maxDriveSaturation = maximum;
            _isDriveSaturated = maximum >= 0.95f;
        }

        public static float CalculateBalanceOffset(
            float comError,
            float comVelocity,
            float dt,
            float kp = DefaultApKp,
            float kd = DefaultApKd,
            float maxCorrectionRad = MaxBalanceCorrectionRad)
        {
            if (!float.IsFinite(dt) || dt <= 0f)
                throw new ArgumentOutOfRangeException(nameof(dt));
            float correction = -kp * comError - kd * comVelocity;
            return Mathf.Clamp(correction, -maxCorrectionRad, maxCorrectionRad);
        }
    }
}
