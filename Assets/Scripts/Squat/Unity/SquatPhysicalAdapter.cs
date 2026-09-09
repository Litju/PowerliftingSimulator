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
        // Multiplier applied to the clamped ankle balance correction before
        // it reaches the ankle target. It stays at 1 so the offset the ankle
        // actually receives is the offset MaxBalanceCorrectionRad declares:
        // the reference ankle only travels 27 deg across the whole squat, so
        // a wider balance offset would outrank the accepted movement family.
        public const float AnkleBalanceOffsetFactor = 1.0f;
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
        private const float MinimumTrunkParticipationRad = 0.0017f; // 0.1 deg
        private const float MaxTrunkNormalizationScale = 4f;
        private SquatState _state = SquatState.SETUP;
        private SquatPhaseDirection _direction = SquatPhaseDirection.None;
        private float _sq;
        // Slow enough for the finite drives and contact solver to settle at
        // each meaningful waypoint; this is gameplay pacing, not extra
        // physical authority.
        private float _phaseRate = 0.30f;
        private float _phaseVelocity;
        private bool _autoCycle;
        private float _bottomHoldTimer;
        private const float BottomHoldDuration = 0.20f;
        private const float ReversalHoldDuration = 0.10f;
        private float _reversalHoldTimer;
        private SquatBarSaddle _saddle;
        private readonly SquatBalanceObserver _observer;
        private readonly SquatPredictiveBalanceController _balanceController = new SquatPredictiveBalanceController();
        private float _standingComApOffset;
        private float _standingComMlOffset;
        private bool _hasStandingCalibration;
        private ReferenceTargetFrame _nominalReferenceTarget;
        private readonly SquatEquilibriumPreload _preload = SquatEquilibriumPreload.QualifiedStanding();
        private readonly float[] _familyFlexionSign = new float[5];
        private readonly System.Collections.Generic.Dictionary<string, JointTargetComposition> _composition =
            new System.Collections.Generic.Dictionary<string, JointTargetComposition>(8);

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
        private string _failureReason = "NONE";

        public SquatPhysicalAdapter(PhysicalAthleteRig rig, SquatReferenceProfile profile = null)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _profile = profile ?? SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            _segments = new PhysicalAthleteRig.SegmentRuntime[_rig.Segments.Count];
            int index = 0;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
                _segments[index++] = segment;
            _observer = new SquatBalanceObserver(_rig, _segments);
            BuildReferenceTargetTables(out _descentTargets, out _ascentTargets);
            CalibrateFamilyFlexionSigns();
            _rig.SetCommandSource(this);
            _sq = 0f;
        }

        // GAM-11 reconciliation seam. Read-only projections of the GAM-10
        // reference so a test or the owner overlay can prove the physical
        // adapter requests the accepted movement family and nothing else.
        public string ReferenceProfileId => _profile.ProfileId;

        public SquatReferencePose ReferenceAnatomicalPose(float phase, SquatPhaseDirection direction) =>
            _profile.Evaluate(Mathf.Clamp01(phase), direction);

        public Quaternion ReferenceLogicalTarget(string jointId, float phase, SquatPhaseDirection direction)
        {
            ReferenceTargetFrame frame = EvaluateReferenceTarget(phase, direction);
            return frame.ForJoint(jointId);
        }

        public SquatBalanceObserver Balance => _observer;
        public SquatPredictiveBalanceController BalanceController => _balanceController;
        public SquatEquilibriumPreload Preload => _preload;

        /// <summary>
        /// Where the owner-accepted GAM-10 reference puts the sole of the foot
        /// in the standing pose. The physical foot collider is supposed to
        /// reach this.
        /// </summary>
        public Vector3 LeftReferencePlantarAnchorWorld { get; private set; }
        public Vector3 RightReferencePlantarAnchorWorld { get; private set; }

        /// <summary>
        /// The three target layers for one controlled joint, kept separate so
        /// telemetry and tests can see each contribution rather than only the
        /// composed result.
        /// </summary>
        public readonly struct JointTargetComposition
        {
            public JointTargetComposition(Quaternion nominal, Quaternion gravityBias, Quaternion balanceOffset, Quaternion final)
            {
                Nominal = nominal;
                GravityBias = gravityBias;
                BalanceOffset = balanceOffset;
                Final = final;
            }

            public Quaternion Nominal { get; }
            public Quaternion GravityBias { get; }
            public Quaternion BalanceOffset { get; }
            public Quaternion Final { get; }
        }

        public bool TryGetTargetComposition(string jointId, out JointTargetComposition composition) =>
            _composition.TryGetValue(jointId, out composition);

        /// <summary>
        /// Sign that converts a canonical anatomical flexion into logical
        /// joint space for this family, measured from the qualified reference
        /// mapping rather than assumed.
        /// </summary>
        public float FamilyFlexionSign(SquatJointFamily family) => _familyFlexionSign[(int)family];

        /// <summary>
        /// Phase 5D diagnostic switch. With this off the athlete is driven by
        /// the GAM-10 reference alone, which separates a reference or mapping
        /// failure from a balance failure.
        /// </summary>
        public bool BalanceCorrectionsEnabled { get; set; } = true;

        /// <summary>
        /// Diagnostic override. When set, the ankle sagittal target offset is
        /// held at this value instead of being solved, so the achievable
        /// centre-of-pressure travel can be measured against a known command.
        /// </summary>
        public float? AnkleSagittalOffsetOverrideRad { get; set; }

        public void SetFootContactDetectors(
            PhysicalFootContactDetector leftFoot,
            PhysicalFootContactDetector rightFoot)
        {
            _observer.SetFootContactDetectors(leftFoot, rightFoot);
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

        // IPF-derived depth proxy measured on the physical bodies. Pelvis
        // drop is kept separately as a regression metric; it is not legality.
        public float RuleDepthLeftM { get; private set; }
        public float RuleDepthRightM { get; private set; }
        public bool LegalDepth { get; private set; }
        public float WorstSideDepthM { get; private set; }
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
            _postureErrorRad = 0f;
            _postureErrorRateRadPerS = 0f;
            _postureLimitProximity = 0f;
            _postureWorstJoint = "NONE";
            _hasPostureHistory = false;
            _balanceController.Reset();
            _hasStandingCalibration = false;
            _qualificationPhaseVelocity = 0f;
            _composition.Clear();
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

        /// <summary>
        /// Qualification only. Places the reference phase directly and reports
        /// the phase velocity that goes with it, so a fixture can ask what the
        /// plant does at a held pose rather than only what it does while
        /// sweeping through one.
        ///
        /// This writes no physical state. The bodies still have to reach the
        /// pose through the same drives, against the same gravity, and the
        /// composition layers are untouched — it moves the reference clock and
        /// nothing else. Gameplay never calls it: the owner path is
        /// Brace/Yield/Drive through PlayerIntentFrame.
        /// </summary>
        public void HoldReferencePhaseForQualification(
            float phase,
            SquatPhaseDirection direction,
            SquatState state,
            float phaseVelocityPerSecond = 0f)
        {
            _sq = Mathf.Clamp01(phase);
            _direction = direction;
            _state = state;
            _autoCycle = false;
            _qualificationPhaseVelocity = phaseVelocityPerSecond;
        }

        private float _qualificationPhaseVelocity;

        public void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            PoweredJointController poweredController)
        {
            if (poweredController == null)
                throw new ArgumentNullException(nameof(poweredController));

            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            _phaseVelocity = 0f;
            AdvanceStateAndPhase(dt, intent);
            // A held qualification phase does not advance itself, so the rate
            // feed-forward has to come from the fixture that placed it.
            if (!_autoCycle && _qualificationPhaseVelocity != 0f)
                _phaseVelocity = _qualificationPhaseVelocity;
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

            CalibrateStandingComRelationship();
            float balanceBias = Mathf.Clamp(intent.BalanceX, -1f, 1f) * MaxBalanceBiasM;
            ObserveCanonicalPosture(dt);
            if (BalanceCorrectionsEnabled)
            {
                _balanceController.Solve(
                    _observer,
                    _observer.SupportApCenter + _standingComApOffset,
                    _observer.SupportMlCenter + _standingComMlOffset + balanceBias,
                    AnkleAnchorAp(),
                    dt);
            }
            else
            {
                _balanceController.Reset();
            }

            _balanceCorrectionRad = _balanceController.AnkleSagittalOffsetRad;
            _mlBalanceCorrectionRad = _balanceController.AnkleFrontalOffsetRad;
            _isCorrectionSaturated = _balanceController.IsAnkleOffsetSaturated;

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

            EvaluateRuleDepth();

            ulong tick = time.Tick;

            // FINAL = NOMINAL_GAM10 + GRAVITY_EQUILIBRIUM_BIAS + DYNAMIC_BALANCE.
            // The three terms stay separable in code, telemetry and tests, so
            // the owner overlay can show exactly how far each layer moved the
            // accepted reference.
            ReferenceTargetFrame referenceTarget = EvaluateReferenceTarget();
            _nominalReferenceTarget = referenceTarget;

            float anklePreload = PreloadLogicalRad(SquatJointFamily.Ankle);
            float kneePreload = PreloadLogicalRad(SquatJointFamily.Knee);
            float hipPreload = PreloadLogicalRad(SquatJointFamily.Hip);
            float abdomenPreload = PreloadLogicalRad(SquatJointFamily.Abdomen);
            float thoraxPreload = PreloadLogicalRad(SquatJointFamily.Thorax);

            Quaternion ankleGravityBias = SagittalAndFrontal(anklePreload, 0f);
            Quaternion kneeGravityBias = SagittalAndFrontal(kneePreload, 0f);
            Quaternion hipGravityBias = SagittalAndFrontal(hipPreload, 0f);
            Quaternion abdomenGravityBias = SagittalAndFrontal(abdomenPreload, 0f);
            Quaternion thoraxGravityBias = SagittalAndFrontal(thoraxPreload, 0f);

            float ankleSagittalOffset = AnkleSagittalOffsetOverrideRad ?? _balanceController.AnkleSagittalOffsetRad;
            Quaternion ankleBalance = SagittalAndFrontal(
                ankleSagittalOffset,
                _balanceController.AnkleFrontalOffsetRad);
            Quaternion kneeBalance = Quaternion.identity;
            Quaternion hipBalance = SagittalAndFrontal(
                _balanceController.HipSagittalOffsetRad,
                _balanceController.HipFrontalOffsetRad);
            Quaternion braceOffset = SagittalAndFrontal(-UnitContract.DegreesToRadians(3f) * brace, 0f);
            Quaternion trunkBalance = braceOffset * SagittalAndFrontal(
                _balanceController.TrunkSagittalOffsetRad,
                _balanceController.HipFrontalOffsetRad * 0.5f);

            Quaternion leftAnkleTarget = Compose("left_foot", referenceTarget.LeftFoot, ankleGravityBias, ankleBalance);
            Quaternion rightAnkleTarget = Compose("right_foot", referenceTarget.RightFoot, ankleGravityBias, ankleBalance);
            Quaternion leftKneeTarget = Compose("left_shank", referenceTarget.LeftShank, kneeGravityBias, kneeBalance);
            Quaternion rightKneeTarget = Compose("right_shank", referenceTarget.RightShank, kneeGravityBias, kneeBalance);
            Quaternion leftHipTarget = Compose("left_thigh", referenceTarget.LeftThigh, hipGravityBias, hipBalance);
            Quaternion rightHipTarget = Compose("right_thigh", referenceTarget.RightThigh, hipGravityBias, hipBalance);
            Quaternion abdomenTarget = Compose("abdomen", referenceTarget.Abdomen, abdomenGravityBias, trunkBalance);
            Quaternion thoraxTarget = Compose("thorax", referenceTarget.Thorax, thoraxGravityBias, trunkBalance);

            // Feed the canonical reference rate forward while the phase is
            // actually moving, so the drive is not asked to hold a moving
            // target with a standstill velocity command.
            ReferenceRateFrame rate = Mathf.Abs(_phaseVelocity) > 1e-5f
                ? EvaluateReferenceRatePerPhase(_sq, _direction)
                : ReferenceRateFrame.Zero;
            float phaseVelocity = _phaseVelocity;

            poweredController.ApplyCommand("left_foot", new JointCommand(leftAnkleTarget, rate.LeftFoot * phaseVelocity, 1f, capacityScale * 2.5f), tick);
            poweredController.ApplyCommand("right_foot", new JointCommand(rightAnkleTarget, rate.RightFoot * phaseVelocity, 1f, capacityScale * 2.5f), tick);
            poweredController.ApplyCommand("left_shank", new JointCommand(leftKneeTarget, rate.LeftShank * phaseVelocity, 1f, capacityScale * 1.8f), tick);
            poweredController.ApplyCommand("right_shank", new JointCommand(rightKneeTarget, rate.RightShank * phaseVelocity, 1f, capacityScale * 1.8f), tick);
            poweredController.ApplyCommand("left_thigh", new JointCommand(leftHipTarget, rate.LeftThigh * phaseVelocity, 1f, capacityScale * 1.5f), tick);
            poweredController.ApplyCommand("right_thigh", new JointCommand(rightHipTarget, rate.RightThigh * phaseVelocity, 1f, capacityScale * 1.5f), tick);
            poweredController.ApplyCommand("abdomen", new JointCommand(abdomenTarget, rate.Abdomen * phaseVelocity, 1f, capacityScale * 1.5f), tick);
            poweredController.ApplyCommand("thorax", new JointCommand(thoraxTarget, rate.Thorax * phaseVelocity, 1f, capacityScale * 1.5f), tick);

            // The head holds its canonical neutral relative to the thorax and
            // nothing else. It is a postural actuator, not part of the balance
            // law: no centre of mass, centre of pressure or capture state
            // reaches it. Without a command at all its activation stays zero,
            // which is what left the head unheld.
            poweredController.ApplyCommand(
                "head_neck",
                new JointCommand(Quaternion.identity, Vector3.zero, 1f, capacityScale * 1.5f),
                tick);

            ApplyUpperLimbReference(poweredController, referenceTarget, rate, phaseVelocity, capacityScale, tick);
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
                        _phaseVelocity = _phaseRate;
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
                        _phaseVelocity = -_phaseRate;
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
                _phaseVelocity = _phaseRate * yieldInput;
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
                _phaseVelocity = -_phaseRate * driveInput;
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
            // Support is the plantar contact polygon the solver actually
            // produced last step, not the midpoint of the two foot bodies.
            // A foot body centre reports support the athlete may not have.
            _observer.BeginPhysicsStep();
            _observer.Observe(observation, _saddle, LowestFootBodyY());

            _systemCom = _observer.SystemCom;
            _supportCenter = new Vector3(
                _observer.SupportMlCenter,
                _observer.SupportPlaneY,
                _observer.SupportApCenter);

            float balanceBias = Mathf.Clamp(intent.BalanceX, -1f, 1f) * MaxBalanceBiasM;
            _apComError = _systemCom.z - _observer.SupportApCenter;
            _mlComError = _systemCom.x - (_observer.SupportMlCenter + balanceBias);
        }

        /// <summary>
        /// Bilateral hip-crease versus knee-top depth on the physical rig,
        /// using the hip and knee joint anchors as the rule proxies. This is
        /// squat legality; vertical pelvis descent is not.
        /// </summary>
        private void EvaluateRuleDepth()
        {
            if (!TryJointAnchor("left_thigh", out Vector3 leftHipCrease) ||
                !TryJointAnchor("right_thigh", out Vector3 rightHipCrease) ||
                !TryJointAnchor("left_shank", out Vector3 leftKneeTop) ||
                !TryJointAnchor("right_shank", out Vector3 rightKneeTop))
                return;

            SquatDepthObservation depth = SquatDepthGeometry.Evaluate(
                leftHipCrease.y,
                rightHipCrease.y,
                leftKneeTop.y,
                rightKneeTop.y);
            RuleDepthLeftM = depth.LeftDepthM;
            RuleDepthRightM = depth.RightDepthM;
            WorstSideDepthM = depth.WorstSideDepthM;
            LegalDepth = depth.BilateralLegalReference;
        }

        private bool TryJointAnchor(string jointId, out Vector3 worldAnchor)
        {
            worldAnchor = Vector3.zero;
            PoweredJointController.PoweredJointRuntime runtime = _rig.PoweredController.GetJoint(jointId);
            if (runtime == null || runtime.Joint == null)
                return false;
            worldAnchor = runtime.Joint.transform.TransformPoint(runtime.Joint.anchor);
            return true;
        }

        /// <summary>
        /// The accepted standing pose is the calibration. Recording how the
        /// system COM sat relative to the plantar support at that pose gives
        /// the balance reference, rather than assuming the polygon centre.
        /// A powerlifting squat holds the system over roughly that same point
        /// throughout, so the standing relationship is also the squat
        /// reference until the phase-specific trajectory lands.
        /// </summary>
        private void CalibrateStandingComRelationship()
        {
            if (_hasStandingCalibration || !_observer.HasSupport || _state != SquatState.SETUP)
                return;
            _standingComApOffset = _observer.SystemCom.z - _observer.SupportApCenter;
            _standingComMlOffset = _observer.SystemCom.x - _observer.SupportMlCenter;
            _hasStandingCalibration = true;
        }

        private float AnkleAnchorAp()
        {
            bool hasLeft = TryJointAnchor("left_foot", out Vector3 left);
            bool hasRight = TryJointAnchor("right_foot", out Vector3 right);
            if (hasLeft && hasRight)
                return 0.5f * (left.z + right.z);
            if (hasLeft)
                return left.z;
            if (hasRight)
                return right.z;
            return _observer.SupportApCenter;
        }

        /// <summary>
        /// Reads the flexion direction of each joint family straight out of
        /// the qualified reference mapping, at the quarter-descent waypoint
        /// where every canonical angle is a positive flexion. Nothing here is
        /// assumed; if the mapping is ever recalibrated these follow it.
        /// </summary>
        private void CalibrateFamilyFlexionSigns()
        {
            SquatReferencePose pose = _profile.Evaluate(0.25f, SquatPhaseDirection.Descent);
            AssignSign(SquatJointFamily.Ankle, "left_foot", pose.AnkleDorsiflexionRad);
            AssignSign(SquatJointFamily.Knee, "left_shank", pose.KneeFlexionRad);
            AssignSign(SquatJointFamily.Hip, "left_thigh", pose.HipFlexionRad);
            AssignSign(SquatJointFamily.Abdomen, "abdomen", pose.TrunkFlexionRad);
            AssignSign(SquatJointFamily.Thorax, "thorax", pose.TrunkFlexionRad);
        }

        private void AssignSign(SquatJointFamily family, string jointId, float canonicalFlexionRad)
        {
            float logical = SignedSagittalRadians(
                EvaluateReferenceTarget(0.25f, SquatPhaseDirection.Descent).ForJoint(jointId));
            float sign = Mathf.Abs(logical) < 1e-4f || Mathf.Abs(canonicalFlexionRad) < 1e-4f
                ? 1f
                : Mathf.Sign(logical) * Mathf.Sign(canonicalFlexionRad);
            _familyFlexionSign[(int)family] = sign;
        }

        private float PreloadLogicalRad(SquatJointFamily family) =>
            _preload.AnatomicalFlexionBiasRad(family) * _familyFlexionSign[(int)family];

        private Quaternion Compose(string jointId, Quaternion nominal, Quaternion gravityBias, Quaternion balanceOffset)
        {
            Quaternion final = nominal * gravityBias * balanceOffset;
            _composition[jointId] = new JointTargetComposition(nominal, gravityBias, balanceOffset, final);
            return final;
        }

        private float LowestFootBodyY()
        {
            float lowest = float.PositiveInfinity;
            if (_rig.Segments.TryGetValue("left_foot", out PhysicalAthleteRig.SegmentRuntime left) && left.Body != null)
                lowest = Mathf.Min(lowest, left.Body.position.y);
            if (_rig.Segments.TryGetValue("right_foot", out PhysicalAthleteRig.SegmentRuntime right) && right.Body != null)
                lowest = Mathf.Min(lowest, right.Body.position.y);
            return float.IsPositiveInfinity(lowest) ? 0f : lowest;
        }

        private static Quaternion SagittalAndFrontal(float sagittalRad, float frontalRad)
        {
            Quaternion sagittal = Quaternion.AngleAxis(sagittalRad * Mathf.Rad2Deg, Vector3.right);
            Quaternion frontal = Quaternion.AngleAxis(frontalRad * Mathf.Rad2Deg, Vector3.forward);
            return sagittal * frontal;
        }

        /// <summary>
        /// Drives the physical upper limbs from the accepted GAM-10 bar-support
        /// reference. These joints carry no balance or gravity term, so the
        /// final target is the reference target: the arms present the accepted
        /// setup, they do not participate in the balance law.
        ///
        /// The hands stay presentation-only. No hand-bar constraint exists;
        /// the load still runs bar to upper-back saddle to thorax.
        /// </summary>
        private void ApplyUpperLimbReference(
            PoweredJointController controller,
            ReferenceTargetFrame referenceTarget,
            ReferenceRateFrame rate,
            float phaseVelocity,
            float capacityScale,
            ulong tick)
        {
            ApplyUpperLimbJoint(controller, "left_upper_arm", referenceTarget.LeftUpperArm, rate.LeftUpperArm, phaseVelocity, capacityScale, tick);
            ApplyUpperLimbJoint(controller, "right_upper_arm", referenceTarget.RightUpperArm, rate.RightUpperArm, phaseVelocity, capacityScale, tick);
            ApplyUpperLimbJoint(controller, "left_forearm", referenceTarget.LeftForearm, rate.LeftForearm, phaseVelocity, capacityScale, tick);
            ApplyUpperLimbJoint(controller, "right_forearm", referenceTarget.RightForearm, rate.RightForearm, phaseVelocity, capacityScale, tick);
            ApplyUpperLimbJoint(controller, "left_hand", referenceTarget.LeftHand, rate.LeftHand, phaseVelocity, capacityScale, tick);
            ApplyUpperLimbJoint(controller, "right_hand", referenceTarget.RightHand, rate.RightHand, phaseVelocity, capacityScale, tick);
        }

        private void ApplyUpperLimbJoint(
            PoweredJointController controller,
            string jointId,
            Quaternion referenceTarget,
            Vector3 ratePerPhase,
            float phaseVelocity,
            float capacityScale,
            ulong tick)
        {
            Quaternion target = Compose(jointId, referenceTarget, Quaternion.identity, Quaternion.identity);
            controller.ApplyCommand(
                jointId,
                new JointCommand(target, ratePerPhase * phaseVelocity, 1f, capacityScale),
                tick);
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
            LeftReferencePlantarAnchorWorld = leftStandingFootAnchor;
            RightReferencePlantarAnchorWorld = rightStandingFootAnchor;
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

            Quaternion abdomenTarget = ToLogicalJointTarget("abdomen", pelvis, abdomen);
            Quaternion thoraxTarget = ToLogicalJointTarget("thorax", abdomen, thorax);
            NormalizeTrunkDistribution(
                _profile.Evaluate(phase, direction).TrunkFlexionRad,
                ref abdomenTarget,
                ref thoraxTarget);

            // The upper limbs come from the same accepted GAM-10 authority the
            // rendered reference preview draws, but they reach the physical
            // joints through the task-space projection rather than as raw bone
            // rotations: the preview's forearm quaternion is not a statement
            // about which joint owns which rotation, and a one-axis elbow
            // cannot hold it.
            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(calibration, solution);
            PhysicalUpperLimbTargets leftArm = SquatPhysicalUpperLimbProjection.Project(
                _rig, calibration, arms.Left, thorax, isLeft: true);
            PhysicalUpperLimbTargets rightArm = SquatPhysicalUpperLimbProjection.Project(
                _rig, calibration, arms.Right, thorax, isLeft: false);

            return new ReferenceTargetFrame(
                ToLogicalJointTarget("left_foot", leftShank, leftFoot),
                ToLogicalJointTarget("right_foot", rightShank, rightFoot),
                ToLogicalJointTarget("left_shank", leftThigh, leftShank),
                ToLogicalJointTarget("right_shank", rightThigh, rightShank),
                ToLogicalJointTarget("left_thigh", pelvis, leftThigh),
                ToLogicalJointTarget("right_thigh", pelvis, rightThigh),
                abdomenTarget,
                thoraxTarget,
                leftArm.UpperArm,
                rightArm.UpperArm,
                leftArm.Forearm,
                rightArm.Forearm,
                leftArm.Hand,
                rightArm.Hand);
        }

        /// <summary>
        /// The canonical trunk flexion is authored across a humanoid spine
        /// chain, but the physical rig only owns two of those segments, so the
        /// abdomen and thorax targets together carried about half the accepted
        /// trunk inclination. This renormalises the participating segments so
        /// they sum to the canonical request while preserving how the
        /// reference distributed the flexion between them. The GAM-10
        /// reference output itself is untouched.
        /// </summary>
        private static void NormalizeTrunkDistribution(
            float canonicalTrunkFlexionRad,
            ref Quaternion abdomenTarget,
            ref Quaternion thoraxTarget)
        {
            float abdomenRad = SignedSagittalRadians(abdomenTarget);
            float thoraxRad = SignedSagittalRadians(thoraxTarget);
            float participatingSum = abdomenRad + thoraxRad;
            if (Mathf.Abs(participatingSum) < MinimumTrunkParticipationRad ||
                Mathf.Abs(canonicalTrunkFlexionRad) < MinimumTrunkParticipationRad)
                return;

            float scale = canonicalTrunkFlexionRad / participatingSum;
            if (!float.IsFinite(scale) || scale <= 0f)
                return;

            scale = Mathf.Min(scale, MaxTrunkNormalizationScale);
            abdomenTarget = Quaternion.SlerpUnclamped(Quaternion.identity, abdomenTarget, scale);
            thoraxTarget = Quaternion.SlerpUnclamped(Quaternion.identity, thoraxTarget, scale);
        }

        private static float SignedSagittalRadians(Quaternion rotation)
        {
            rotation = NormalizeCanonicalQuaternion(rotation);
            float vectorMagnitude = Mathf.Sqrt(
                rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (vectorMagnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / vectorMagnitude);
        }

        private static Quaternion NormalizeCanonicalQuaternion(Quaternion rotation) =>
            rotation.w < 0f
                ? new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w)
                : rotation;

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

        private ReferenceTargetFrame EvaluateReferenceTarget() => EvaluateReferenceTarget(_sq, _direction);

        private ReferenceTargetFrame EvaluateReferenceTarget(float phase, SquatPhaseDirection direction)
        {
            ReferenceTargetFrame[] targets = direction == SquatPhaseDirection.Ascent ? _ascentTargets : _descentTargets;
            float scaled = Mathf.Clamp01(phase) * (ReferenceTargetSampleCount - 1);
            int lowerIndex = Mathf.FloorToInt(scaled);
            int upperIndex = Mathf.Min(ReferenceTargetSampleCount - 1, lowerIndex + 1);
            float interpolation = scaled - lowerIndex;
            return ReferenceTargetFrame.Interpolate(targets[lowerIndex], targets[upperIndex], interpolation);
        }

        /// <summary>
        /// Reference target angular rate in logical joint space, per unit of
        /// phase. Multiplied by the current phase velocity it becomes the
        /// feed-forward target velocity the drive expects, rather than the
        /// zero the adapter used to send while the reference was moving.
        /// </summary>
        private readonly struct ReferenceRateFrame
        {
            public ReferenceRateFrame(
                Vector3 leftFoot, Vector3 rightFoot,
                Vector3 leftShank, Vector3 rightShank,
                Vector3 leftThigh, Vector3 rightThigh,
                Vector3 abdomen, Vector3 thorax,
                Vector3 leftUpperArm, Vector3 rightUpperArm,
                Vector3 leftForearm, Vector3 rightForearm,
                Vector3 leftHand, Vector3 rightHand)
            {
                LeftFoot = leftFoot;
                RightFoot = rightFoot;
                LeftShank = leftShank;
                RightShank = rightShank;
                LeftThigh = leftThigh;
                RightThigh = rightThigh;
                Abdomen = abdomen;
                Thorax = thorax;
                LeftUpperArm = leftUpperArm;
                RightUpperArm = rightUpperArm;
                LeftForearm = leftForearm;
                RightForearm = rightForearm;
                LeftHand = leftHand;
                RightHand = rightHand;
            }

            public Vector3 LeftFoot { get; }
            public Vector3 RightFoot { get; }
            public Vector3 LeftShank { get; }
            public Vector3 RightShank { get; }
            public Vector3 LeftThigh { get; }
            public Vector3 RightThigh { get; }
            public Vector3 Abdomen { get; }
            public Vector3 Thorax { get; }
            public Vector3 LeftUpperArm { get; }
            public Vector3 RightUpperArm { get; }
            public Vector3 LeftForearm { get; }
            public Vector3 RightForearm { get; }
            public Vector3 LeftHand { get; }
            public Vector3 RightHand { get; }

            public static readonly ReferenceRateFrame Zero = new ReferenceRateFrame(
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                Vector3.zero, Vector3.zero, Vector3.zero, Vector3.zero,
                Vector3.zero, Vector3.zero);
        }

        private ReferenceRateFrame EvaluateReferenceRatePerPhase(float phase, SquatPhaseDirection direction)
        {
            ReferenceTargetFrame[] targets = direction == SquatPhaseDirection.Ascent ? _ascentTargets : _descentTargets;
            float scaled = Mathf.Clamp01(phase) * (ReferenceTargetSampleCount - 1);
            int lowerIndex = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, ReferenceTargetSampleCount - 2);
            int upperIndex = lowerIndex + 1;
            float phaseStep = 1f / (ReferenceTargetSampleCount - 1);
            ReferenceTargetFrame from = targets[lowerIndex];
            ReferenceTargetFrame to = targets[upperIndex];
            return new ReferenceRateFrame(
                RatePerPhase(from.LeftFoot, to.LeftFoot, phaseStep),
                RatePerPhase(from.RightFoot, to.RightFoot, phaseStep),
                RatePerPhase(from.LeftShank, to.LeftShank, phaseStep),
                RatePerPhase(from.RightShank, to.RightShank, phaseStep),
                RatePerPhase(from.LeftThigh, to.LeftThigh, phaseStep),
                RatePerPhase(from.RightThigh, to.RightThigh, phaseStep),
                RatePerPhase(from.Abdomen, to.Abdomen, phaseStep),
                RatePerPhase(from.Thorax, to.Thorax, phaseStep),
                RatePerPhase(from.LeftUpperArm, to.LeftUpperArm, phaseStep),
                RatePerPhase(from.RightUpperArm, to.RightUpperArm, phaseStep),
                RatePerPhase(from.LeftForearm, to.LeftForearm, phaseStep),
                RatePerPhase(from.RightForearm, to.RightForearm, phaseStep),
                RatePerPhase(from.LeftHand, to.LeftHand, phaseStep),
                RatePerPhase(from.RightHand, to.RightHand, phaseStep));
        }

        private static Vector3 RatePerPhase(Quaternion from, Quaternion to, float phaseStep)
        {
            Quaternion delta = NormalizeCanonicalQuaternion(to * Quaternion.Inverse(from));
            float vectorMagnitude = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
            if (vectorMagnitude <= 1e-6f)
                return Vector3.zero;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(delta.w, -1f, 1f));
            return new Vector3(delta.x, delta.y, delta.z) * (angle / vectorMagnitude) / phaseStep;
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
                Quaternion thorax,
                Quaternion leftUpperArm,
                Quaternion rightUpperArm,
                Quaternion leftForearm,
                Quaternion rightForearm,
                Quaternion leftHand,
                Quaternion rightHand)
            {
                LeftFoot = leftFoot;
                RightFoot = rightFoot;
                LeftShank = leftShank;
                RightShank = rightShank;
                LeftThigh = leftThigh;
                RightThigh = rightThigh;
                Abdomen = abdomen;
                Thorax = thorax;
                LeftUpperArm = leftUpperArm;
                RightUpperArm = rightUpperArm;
                LeftForearm = leftForearm;
                RightForearm = rightForearm;
                LeftHand = leftHand;
                RightHand = rightHand;
            }

            public Quaternion LeftFoot { get; }
            public Quaternion RightFoot { get; }
            public Quaternion LeftShank { get; }
            public Quaternion RightShank { get; }
            public Quaternion LeftThigh { get; }
            public Quaternion RightThigh { get; }
            public Quaternion Abdomen { get; }
            public Quaternion Thorax { get; }
            public Quaternion LeftUpperArm { get; }
            public Quaternion RightUpperArm { get; }
            public Quaternion LeftForearm { get; }
            public Quaternion RightForearm { get; }
            public Quaternion LeftHand { get; }
            public Quaternion RightHand { get; }

            public Quaternion ForJoint(string jointId)
            {
                switch (jointId)
                {
                    case "left_foot": return LeftFoot;
                    case "right_foot": return RightFoot;
                    case "left_shank": return LeftShank;
                    case "right_shank": return RightShank;
                    case "left_thigh": return LeftThigh;
                    case "right_thigh": return RightThigh;
                    case "abdomen": return Abdomen;
                    case "thorax": return Thorax;
                    case "left_upper_arm": return LeftUpperArm;
                    case "right_upper_arm": return RightUpperArm;
                    case "left_forearm": return LeftForearm;
                    case "right_forearm": return RightForearm;
                    case "left_hand": return LeftHand;
                    case "right_hand": return RightHand;
                    default: throw new ArgumentException($"'{jointId}' is not a squat-controlled joint.", nameof(jointId));
                }
            }

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
                    Quaternion.Slerp(from.Thorax, to.Thorax, interpolation),
                    Quaternion.Slerp(from.LeftUpperArm, to.LeftUpperArm, interpolation),
                    Quaternion.Slerp(from.RightUpperArm, to.RightUpperArm, interpolation),
                    Quaternion.Slerp(from.LeftForearm, to.LeftForearm, interpolation),
                    Quaternion.Slerp(from.RightForearm, to.RightForearm, interpolation),
                    Quaternion.Slerp(from.LeftHand, to.LeftHand, interpolation),
                    Quaternion.Slerp(from.RightHand, to.RightHand, interpolation));
            }
        }

        /// <summary>
        /// Canonical posture error for the joints standing depends on, read
        /// from the previous post-physics state in logical joint coordinates
        /// rather than from world-space appearance, plus how close any of them
        /// is to its anatomical limit.
        ///
        /// This is what stops balance from spending posture: the controller
        /// cannot withdraw authority from a cost it cannot see.
        /// </summary>
        /// <summary>
        /// The joints the balance strategy spends, which deliberately
        /// excludes the ankles it commands. An authorised ankle offset shows
        /// up as ankle deviation from the canonical pose by construction, so
        /// feeding the ankles to the guard makes it read its own command as
        /// damage and throttle itself to nothing: with them included the
        /// guard scale went to zero and the athlete fell at 1.72 s. The ankle
        /// is bounded by its own target bound; this measures what the ankle
        /// is costing everything else.
        /// </summary>
        private static readonly string[] CanonicalPostureJoints =
        {
            "left_thigh", "right_thigh", "abdomen", "thorax",
            "left_shank", "right_shank"
        };

        public float CanonicalPostureErrorRad => _postureErrorRad;
        public float CanonicalPostureErrorRateRadPerS => _postureErrorRateRadPerS;
        public float CanonicalPostureLimitProximity => _postureLimitProximity;
        public string CanonicalPostureWorstJoint => _postureWorstJoint;

        private float _postureErrorRad;
        private float _postureErrorRateRadPerS;
        private float _postureLimitProximity;
        private string _postureWorstJoint = "NONE";
        private bool _hasPostureHistory;

        private void ObserveCanonicalPosture(float dt)
        {
            float worstError = 0f;
            float worstLimit = 0f;
            string worstJoint = "NONE";

            for (int index = 0; index < CanonicalPostureJoints.Length; index++)
            {
                string jointId = CanonicalPostureJoints[index];
                if (!_composition.TryGetValue(jointId, out JointTargetComposition composition))
                    continue;
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
                if (joint == null)
                    continue;

                float errorRad = Quaternion.Angle(composition.Nominal, joint.Diagnostic.ActualRelative) * Mathf.Deg2Rad;
                if (errorRad > worstError)
                {
                    worstError = errorRad;
                    worstJoint = jointId;
                }
                worstLimit = Mathf.Max(worstLimit, joint.Diagnostic.LimitProximity);
            }

            _postureErrorRateRadPerS = _hasPostureHistory && dt > 0f
                ? (worstError - _postureErrorRad) / dt
                : 0f;
            _postureErrorRad = worstError;
            _postureLimitProximity = worstLimit;
            _postureWorstJoint = worstJoint;
            _hasPostureHistory = true;

            _balanceController.ObservePosture(_postureErrorRad, _postureErrorRateRadPerS, _postureLimitProximity);
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
