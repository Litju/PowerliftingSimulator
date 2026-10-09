using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public enum SquatOneStepWalkoutState
    {
        BILATERAL_STANDING,
        SHIFT_TO_LEFT_STANCE,
        VERIFY_RIGHT_UNLOAD,
        RIGHT_SWING_CLEAR,
        RIGHT_SWING_BACK,
        RIGHT_TOUCHDOWN,
        RIGHT_LOAD_ACCEPT,
        BILATERAL_RECOVERY,
        ONE_STEP_READY,
        ABORT_RECOVERY,
        ABORTED
    }

    /// <summary>
    /// Converts squat intent and the previous post-physics observation into
    /// finite joint targets. It never writes a Rigidbody force, torque,
    /// velocity, or transform; PoweredJointController remains the sole drive
    /// writer inside the one registered pre-physics callback.
    /// </summary>
    public sealed class SquatPhysicalAdapter : IPhysicalAthleteCommandSource
    {
        public const string CapacityCalibrationVersion = "GAM50_INTRINSIC_STRENGTH_V1";
        // GAM-50 single intrinsic-strength calibration, made once 140 and 170 kg
        // reached comparable valid mechanics on the qualified substrate. The
        // binding drive is the knee in ascent; capacity-limit scales measured
        // at 5.13 were 140 kg 0.70, 170 kg 0.79, 300 kg 1.18. At 0.75 the peak
        // ascent demand is 25 kg ~0.48, 60 kg ~0.61, 140 kg 0.93 (heavy),
        // 170 kg 1.07 (near-max, saturating and completing), and 300 kg cannot
        // hold the loaded setup (supra-max): the GAM-13 V2-5 envelope.
        public const float ProductionAthleteStrengthScale = 0.75f;

        /// <summary>
        /// The one intrinsic, load-independent strength scalar. Equals
        /// <see cref="ProductionAthleteStrengthScale"/> in builds and normal
        /// editor runs; GAM50_STRENGTH_SCALE_OVERRIDE (editor only, read once)
        /// exists solely for the GAM-50 calibration sweep.
        /// </summary>
        public static readonly float AthleteStrengthScale = ResolveAthleteStrengthScale();

        private static float ResolveAthleteStrengthScale()
        {
#if UNITY_EDITOR
            string text = System.Environment.GetEnvironmentVariable("GAM50_STRENGTH_SCALE_OVERRIDE");
            if (!string.IsNullOrWhiteSpace(text) &&
                float.TryParse(text, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out float value) &&
                value > 0f && value <= 20f)
                return value;
#endif
            return ProductionAthleteStrengthScale;
        }
#if UNITY_EDITOR
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
#endif
        private const float SupportFailureApErrorM = 0.30f;
        private const float SupportFailureMlErrorM = 0.30f;

        private readonly PhysicalAthleteRig _rig;
        private readonly PhysicalAthleteRig.SegmentRuntime[] _segments;
        private readonly SquatReferenceProfile _profile;
        private readonly ReferenceTargetFrame[] _referenceTargets;
        private readonly float[] _referenceComOffsetApSamples = new float[ReferenceTargetSampleCount];
        private readonly ReferenceBodyPose[] _referenceBodyPoses = new ReferenceBodyPose[PhysicalAthleteDefinition.Segments.Count];
        private readonly Dictionary<string, int> _segmentIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        private SquatReferenceRigCalibration _referenceCalibration;
        private SquatDepthLandmarkProvider _depthLandmarkProvider;
        private const int ReferenceTargetSampleCount = 101;
        private const float LockoutTransitionSq = 0.001f;
        private const float MinimumTrunkParticipationRad = 0.0017f; // 0.1 deg
        private const float MaxTrunkNormalizationScale = 4f;
        public const float OneStepPosteriorDistanceM = 0.18f;
        public const float OneStepSwingClearanceM = 0.07f;
        public const string OneStepTargetFrame = "W";
        private const float OneStepLandingToleranceM = 0.03f;
        private const float OneStepLoadedImpulseFraction = 0.05f;
        private const float OneStepUnloadImpulseFraction = 0.01f;
        private const int OneStepUnloadPersistenceTicks = 10;
        private const int OneStepOffGroundPersistenceTicks = 2;
        private const int OneStepLoadAcceptanceTicks = 10;
        private const int OneStepRecoveryPersistenceTicks = 20;
        private SquatState _state = SquatState.SETUP;
        private bool _squatCommandIssued;
        private SquatPhaseDirection _direction = SquatPhaseDirection.None;
        private float _sq;
        // Slow enough for the finite drives and contact solver to settle at
        // each meaningful waypoint; this is gameplay pacing, not extra
        // physical authority.
        private float _phaseRate = 0.30f;
        private float _phaseVelocity;
#if UNITY_EDITOR
        private bool _autoCycle;
        private const float BottomHoldDuration = 0.20f;
#endif
        // The manual (shipping) phase path resets this on reaching BOTTOM too.
        private float _bottomHoldTimer;
        private const float ReversalHoldDuration = 0.10f;
        private float _reversalHoldTimer;
        private SquatBarSaddle _saddle;
        private readonly SquatBalanceObserver _observer;
        private readonly SquatComStabilizerV2 _balanceV2 = new SquatComStabilizerV2();
        private SquatBalanceCorrectionV2 _lastBalanceCorrection;
        private float _referenceComOffsetAp;
        private float _referenceComOffsetMl;
        private Vector3 _referenceSupportCenter;
        private bool _hasStandingComReference;
        private PhysicalFootContactDetector _leftFoot;
        private PhysicalFootContactDetector _rightFoot;
        private SquatOneStepWalkoutState _oneStepWalkoutState = SquatOneStepWalkoutState.BILATERAL_STANDING;
        private ulong _oneStepStateEnteredTick;
        private Vector3 _oneStepRightStartBodyWorld;
        private Vector3 _oneStepRightLandingBodyWorld;
        private Vector3 _oneStepRightTargetBodyWorld;
        private Quaternion _oneStepRightFootBodyRotation;
        private float _oneStepInitialComWorldX;
        private float _oneStepLeftComTargetWorldX;
        private float _oneStepSupportPlaneY;
        private int _oneStepUnloadTicks;
        private int _oneStepOffGroundTicks;
        private int _oneStepLoadTicks;
        private int _oneStepRecoveryTicks;
        private bool _oneStepRightIkActive;
        private bool _oneStepAbortTouchdownObserved;
        private bool _oneStepSawNearZeroTouch;
        private string _oneStepFailure = string.Empty;
#if UNITY_EDITOR
        private readonly SquatPredictiveBalanceController _balanceController = new SquatPredictiveBalanceController();
#endif
        private ReferenceTargetFrame _nominalReferenceTarget;
#if UNITY_EDITOR
        private readonly SquatEquilibriumPreload _preload = SquatEquilibriumPreload.QualifiedStanding();
#endif
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
            for (int segmentIndex = 0; segmentIndex < PhysicalAthleteDefinition.Segments.Count; segmentIndex++)
                _segmentIndexes.Add(PhysicalAthleteDefinition.Segments[segmentIndex].Id, segmentIndex);
            _observer = new SquatBalanceObserver(_rig, _segments);
            _referenceTargets = BuildReferenceTargetTable();
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
        public SquatComStabilizerV2Calibration StabilizerCalibration => _balanceV2.Calibration;
        public SquatBalanceCorrectionV2 BalanceCorrectionV2 => _lastBalanceCorrection;
        public bool HasStandingComReference => _hasStandingComReference;
        public float ReferenceComOffsetAp => _referenceComOffsetAp;
        public float ReferenceComOffsetMl => _referenceComOffsetMl;
        public Vector3 ReferenceSupportCenter => _referenceSupportCenter;
#if UNITY_EDITOR
        public SquatPredictiveBalanceController BalanceController => _balanceController;
        public SquatEquilibriumPreload Preload => _preload;

        public float StandingEquilibriumBiasDegrees(SquatJointFamily family) =>
            _preload.BiasDegrees(family, 0f, EquilibriumLoadKg);
#endif

        public SquatReferenceRigCalibration ReferenceCalibration => _referenceCalibration;

        public Quaternion ReferencePelvisBodyRotation(float phase, SquatPhaseDirection direction) =>
            EvaluateReferenceTarget(phase, direction).PelvisBodyRotation;

        /// <summary>
        /// Where the owner-accepted GAM-10 reference puts the sole of the foot
        /// in the standing pose. The physical foot collider is supposed to
        /// reach this.
        /// </summary>
        public Vector3 LeftReferencePlantarAnchorWorld { get; private set; }
        public Vector3 RightReferencePlantarAnchorWorld { get; private set; }
        public Vector3 CanonicalPlantarSupportCenter
        {
            get
            {
                // Before a physics step, the planted collider footprint is the available support geometry.
                if (_rig.Segments.TryGetValue("left_foot", out PhysicalAthleteRig.SegmentRuntime left) &&
                    _rig.Segments.TryGetValue("right_foot", out PhysicalAthleteRig.SegmentRuntime right) &&
                    left.Collider != null && right.Collider != null)
                {
                    Bounds leftBounds = left.Collider.bounds;
                    Bounds rightBounds = right.Collider.bounds;
                    return new Vector3(
                        0.5f * (Mathf.Min(leftBounds.min.x, rightBounds.min.x) + Mathf.Max(leftBounds.max.x, rightBounds.max.x)),
                        0.5f * (leftBounds.center.y + rightBounds.center.y),
                        0.5f * (Mathf.Min(leftBounds.min.z, rightBounds.min.z) + Mathf.Max(leftBounds.max.z, rightBounds.max.z)));
                }

                return 0.5f * (LeftReferencePlantarAnchorWorld + RightReferencePlantarAnchorWorld);
            }
        }

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
#if UNITY_EDITOR
        public bool BalanceCorrectionsEnabled { get; set; } = true;

        /// <summary>
        /// Editor-only plant-identification seam. Disables the AP stabilizer
        /// contribution at every allocated joint while leaving ML feedback
        /// and the rest of the command path unchanged.
        /// </summary>
        public bool ApFeedbackContributionEnabled { get; set; } = true;

        /// <summary>Qualification-only selector for the GAM-13 V2-3M AP-reference comparison.</summary>
        public bool UsePhaseDependentApComReferenceForQualification { get; set; }

        /// <summary>
        /// Reversible GAM-43 diagnostic seam. Production uses the canonical
        /// pose error; the historical target-deflection input is available
        /// only for the one requested semantics comparison.
        /// </summary>
        public bool ExperimentalUseTargetDeflectionForPostureGuard { get; set; }

        /// <summary>
        /// Diagnostic override. When set, the ankle sagittal target offset is
        /// held at this value instead of being solved, so the achievable
        /// centre-of-pressure travel can be measured against a known command.
        /// </summary>
        public float? AnkleSagittalOffsetOverrideRad { get; set; }

        /// <summary>
        /// Test-time target residual. The default is zero, so gameplay keeps
        /// the established balance command. Identification may add a bounded
        /// residual without replacing the stabilizing balance loop.
        /// </summary>
        public float AnkleSagittalOffsetAdditiveRad { get; set; }

        /// <summary>Test-time sagittal residual added to both hip targets.</summary>
        public float HipSagittalOffsetAdditiveRad { get; set; }

        /// <summary>Test-time sagittal residual added to abdomen and thorax targets.</summary>
        public float TrunkSagittalOffsetAdditiveRad { get; set; }
#endif

        public void SetFootContactDetectors(
            PhysicalFootContactDetector leftFoot,
            PhysicalFootContactDetector rightFoot)
        {
            _leftFoot = leftFoot;
            _rightFoot = rightFoot;
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
        public SquatOneStepWalkoutState OneStepWalkoutState => _oneStepWalkoutState;
        public string OneStepWalkoutFailure => _oneStepFailure;
        public bool OneStepSawNearZeroTouch => _oneStepSawNearZeroTouch;
        public bool CanBeginOneStepWalkout
        {
            get
            {
                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                float loadedImpulse = OneStepImpulseThreshold(OneStepLoadedImpulseFraction, dt);
                return _oneStepWalkoutState == SquatOneStepWalkoutState.BILATERAL_STANDING &&
                    HasFiniteComSupport() && IsFootLoaded(_leftFoot, loadedImpulse) &&
                    IsFootLoaded(_rightFoot, loadedImpulse) && IsComControlledByBothFeet(0.15f);
            }
        }
        public float OneStepPosteriorDisplacementM =>
            _oneStepRightStartBodyWorld.z - _rig.Segments["right_foot"].Body.position.z;

        // IPF-derived depth proxy measured on the physical bodies. Pelvis
        // drop is kept separately as a regression metric; it is not legality.
        public float RuleDepthLeftM { get; private set; }
        public float RuleDepthRightM { get; private set; }
        public bool LegalDepth { get; private set; }
        public float WorstSideDepthM { get; private set; }
        public SquatJointCenterDepthDiagnostic JointCenterDepthDiagnostic { get; private set; }
        public string FailureReason => _failureReason;
#if UNITY_EDITOR
        public bool AutoCycle { get => _autoCycle; set => _autoCycle = value; }
#endif
        public float PhaseRate { get => _phaseRate; set => _phaseRate = Mathf.Clamp(value, 0.05f, 0.75f); }

        public void SetSaddle(SquatBarSaddle saddle)
        {
            _saddle = saddle;
            if (saddle != null)
                _failureReason = "NONE";
        }

        public void BeginOneStepWalkout(ulong currentTick)
        {
            if (!_hasStandingComReference || _state != SquatState.SETUP || _squatCommandIssued ||
                _oneStepWalkoutState != SquatOneStepWalkoutState.BILATERAL_STANDING)
                throw new InvalidOperationException("The one-step walkout requires an untouched validated standing setup.");
            if (!CanBeginOneStepWalkout)
                throw new InvalidOperationException("Bilateral loaded support and finite controlled COM are required before walkout.");
            if (_saddle == null || !_saddle.IsAttached || _saddle.Barbell == null ||
                Mathf.Abs(_saddle.Barbell.LoadedMassKg - 25f) > 0.001f)
                throw new InvalidOperationException("The GAM-60 one-step walkout is qualified only with an attached 25 kg bar.");
            if (_leftFoot == null || _rightFoot == null ||
                _rig.Segments["left_foot"].Body == null || _rig.Segments["right_foot"].Body == null)
                throw new InvalidOperationException("Both physical foot contact detectors are required.");

            _oneStepRightStartBodyWorld = _rig.Segments["right_foot"].Body.position;
            _oneStepRightLandingBodyWorld = _oneStepRightStartBodyWorld + Vector3.back * OneStepPosteriorDistanceM;
            _oneStepRightTargetBodyWorld = _oneStepRightStartBodyWorld;
            _oneStepRightFootBodyRotation = _rig.Segments["right_foot"].Body.rotation;
            _oneStepInitialComWorldX = _observer.SystemCom.x;
            _oneStepLeftComTargetWorldX = _rig.Segments["left_foot"].Body.position.x;
            _oneStepSupportPlaneY = _observer.SupportPlaneY;
            _oneStepStateEnteredTick = currentTick;
            _oneStepUnloadTicks = 0;
            _oneStepOffGroundTicks = 0;
            _oneStepLoadTicks = 0;
            _oneStepRecoveryTicks = 0;
            _oneStepRightIkActive = false;
            _oneStepAbortTouchdownObserved = false;
            _oneStepSawNearZeroTouch = false;
            _oneStepFailure = string.Empty;
            SetOneStepWalkoutState(SquatOneStepWalkoutState.SHIFT_TO_LEFT_STANCE, currentTick);
        }

        public void CaptureStandingComReference()
        {
            Vector3 supportCenter = CanonicalPlantarSupportCenter;
            Vector3 systemCom = _observer.MeasureCurrentSystemCom(_saddle);
            _referenceSupportCenter = supportCenter;
            CaptureReferenceComOffsetSamples(supportCenter);
            _referenceComOffsetAp = _referenceComOffsetApSamples[0];
            _referenceComOffsetMl = systemCom.x - supportCenter.x;
            _hasStandingComReference = true;
        }

        private void CaptureReferenceComOffsetSamples(Vector3 supportCenter)
        {
            float barMassKg = _saddle != null && _saddle.IsAttached && _saddle.Barbell != null &&
                _saddle.Barbell.Body != null && _saddle.Barbell.Body.gameObject.activeInHierarchy
                ? _saddle.Barbell.Body.mass
                : 0f;

            for (int sampleIndex = 0; sampleIndex < _referenceComOffsetApSamples.Length; sampleIndex++)
            {
                float phase = sampleIndex / (float)(_referenceComOffsetApSamples.Length - 1);
                SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                    _referenceCalibration,
                    _profile.Evaluate(phase, SquatPhaseDirection.Descent),
                    LeftReferencePlantarAnchorWorld,
                    RightReferencePlantarAnchorWorld);
                if (!solution.IsValid)
                    throw new InvalidOperationException(
                        $"Cannot derive the phase COM reference at {phase:F2}: {solution.RejectionReason}");

                Vector3 systemCom = MeasureReferenceSystemCom(_referenceTargets[sampleIndex], solution, barMassKg);
                _referenceComOffsetApSamples[sampleIndex] = systemCom.z - supportCenter.z;
            }
        }

        private Vector3 MeasureReferenceSystemCom(
            ReferenceTargetFrame reference,
            SquatReferenceKinematicSolution solution,
            float barMassKg)
        {
            Vector3 weightedPosition = Vector3.zero;
            float totalMassKg = 0f;
            for (int index = 0; index < PhysicalAthleteDefinition.Segments.Count; index++)
            {
                PhysicalSegmentRecipe recipe = PhysicalAthleteDefinition.Segments[index];
                PhysicalAthleteRig.SegmentRuntime segment = _rig.Segments[recipe.Id];
                Quaternion rotation;
                Vector3 position;
                if (recipe.ParentId == null)
                {
                    rotation = reference.PelvisBodyRotation;
                    position = solution.PelvisBonePosition + recipe.FixedCenterOffsetMeters;
                }
                else
                {
                    ReferenceBodyPose parent = _referenceBodyPoses[_segmentIndexes[recipe.ParentId]];
                    PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(recipe.Id);
                    rotation = parent.Rotation * PoweredJointController.ToParentChildRelativeRotation(
                        joint.NeutralParentToChild,
                        joint.JointSpace,
                        recipe.Id == "head_neck" ? Quaternion.identity : reference.ForJoint(recipe.Id));
                    Vector3 parentAnchor = parent.Position + parent.Rotation * joint.Joint.connectedAnchor;
                    position = parentAnchor - rotation * joint.Joint.anchor;
                }

                _referenceBodyPoses[index] = new ReferenceBodyPose(position, rotation);
                weightedPosition += position * segment.Body.mass;
                totalMassKg += segment.Body.mass;
            }

            if (barMassKg > 0f && _saddle != null)
            {
                ReferenceBodyPose thorax = _referenceBodyPoses[_segmentIndexes["thorax"]];
                Vector3 barCenter = thorax.Position + thorax.Rotation * SquatBarSaddle.ThoraxLocalAnchor;
                weightedPosition += barCenter * barMassKg;
                totalMassKg += barMassKg;
            }

            return totalMassKg > 0f ? weightedPosition / totalMassKg : Vector3.zero;
        }

        private float ReferenceComOffsetApAtPhase(float phase)
        {
            float scaled = Mathf.Clamp01(phase) * (_referenceComOffsetApSamples.Length - 1);
            int lower = Mathf.FloorToInt(scaled);
            int upper = Mathf.Min(_referenceComOffsetApSamples.Length - 1, lower + 1);
            return Mathf.Lerp(_referenceComOffsetApSamples[lower], _referenceComOffsetApSamples[upper], scaled - lower);
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
            _squatCommandIssued = false;
            _direction = SquatPhaseDirection.None;
            _sq = 0f;
#if UNITY_EDITOR
            _autoCycle = false;
            _bottomHoldTimer = 0f;
#endif
            _reversalHoldTimer = 0f;
            _lockoutReached = false;
            _failureReason = "NONE";
            _minPelvisHeightM = float.PositiveInfinity;
            _systemCom = Vector3.zero;
            _supportCenter = Vector3.zero;
            _oneStepWalkoutState = SquatOneStepWalkoutState.BILATERAL_STANDING;
            _oneStepStateEnteredTick = 0ul;
            _oneStepRightStartBodyWorld = Vector3.zero;
            _oneStepRightLandingBodyWorld = Vector3.zero;
            _oneStepRightTargetBodyWorld = Vector3.zero;
            _oneStepRightFootBodyRotation = Quaternion.identity;
            _oneStepInitialComWorldX = 0f;
            _oneStepLeftComTargetWorldX = 0f;
            _oneStepSupportPlaneY = 0f;
            _oneStepUnloadTicks = 0;
            _oneStepOffGroundTicks = 0;
            _oneStepLoadTicks = 0;
            _oneStepRecoveryTicks = 0;
            _oneStepRightIkActive = false;
            _oneStepAbortTouchdownObserved = false;
            _oneStepSawNearZeroTouch = false;
            _oneStepFailure = string.Empty;
            _apComError = 0f;
            _mlComError = 0f;
            _balanceCorrectionRad = 0f;
            _mlBalanceCorrectionRad = 0f;
            _lastBalanceCorrection = default;
            _balanceV2.Reset();
#if UNITY_EDITOR
            ApFeedbackContributionEnabled = true;
            AnkleSagittalOffsetAdditiveRad = 0f;
            HipSagittalOffsetAdditiveRad = 0f;
            TrunkSagittalOffsetAdditiveRad = 0f;
#endif
            _isCorrectionSaturated = false;
            _isDriveSaturated = false;
            _maxDriveSaturation = 0f;
            _canonicalPoseErrorRad = 0f;
            _canonicalPoseErrorRateRadPerS = 0f;
            _targetActualDeflectionRad = 0f;
            _targetActualDeflectionRateRadPerS = 0f;
            _postureLimitProximity = 0f;
            _postureUnexpectedMarginConsumed = 0f;
            _postureWorstJoint = "NONE";
            _hasPostureHistory = false;
#if UNITY_EDITOR
            _balanceController.Reset();
            _qualificationPhaseVelocity = 0f;
#endif
            _referenceComOffsetAp = 0f;
            _referenceComOffsetMl = 0f;
            Array.Clear(_referenceComOffsetApSamples, 0, _referenceComOffsetApSamples.Length);
            _referenceSupportCenter = Vector3.zero;
            _hasStandingComReference = false;
            _composition.Clear();
        }

#if UNITY_EDITOR
        // Historical diagnostic auto-cycle. Owner gameplay never calls this.
        public void StartSquat()
        {
            _state = SquatState.DESCENT;
            _direction = SquatPhaseDirection.Descent;
            _autoCycle = true;
            _lockoutReached = false;
            _failureReason = "NONE";
        }
#endif

        public void BeginIntentDrivenSquat()
        {
            _state = SquatState.SQUAT_COMMAND;
            _squatCommandIssued = true;
            _direction = SquatPhaseDirection.None;
            _lockoutReached = false;
            _failureReason = "NONE";
#if UNITY_EDITOR
            _autoCycle = false;
#endif
        }

#if UNITY_EDITOR
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
#endif

        public void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            IPhysicalAthleteJointCommandSink jointCommandSink)
        {
            if (jointCommandSink == null)
                throw new ArgumentNullException(nameof(jointCommandSink));
            if (!_hasStandingComReference)
                throw new InvalidOperationException("The physical squat must capture its standing COM reference after reset and saddle attachment.");

            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            _phaseVelocity = 0f;
            AdvanceStateAndPhase(dt, intent);
#if UNITY_EDITOR
            if (!_autoCycle && _qualificationPhaseVelocity != 0f)
                _phaseVelocity = _qualificationPhaseVelocity;
#endif
#if UNITY_EDITOR
            _referenceComOffsetAp = UsePhaseDependentApComReferenceForQualification
                ? ReferenceComOffsetApAtPhase(_sq)
                : _referenceComOffsetApSamples[0];
#else
            _referenceComOffsetAp = _referenceComOffsetApSamples[0];
#endif
            ComputeComAndSupport(previousObservation);
            AdvanceOneStepWalkout(time);
            float activeComOffsetMl = OneStepComOffsetMl();
            _mlComError = (_systemCom.x - _supportCenter.x) - activeComOffsetMl;
            _lastBalanceCorrection = _balanceV2.Solve(
                _systemCom,
                _observer.SystemComVelocity,
                _supportCenter,
                _referenceComOffsetAp,
                activeComOffsetMl,
                intent.BalanceX,
                dt);

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
                _failureReason = "COM_OUTSIDE_SUPPORT";
            }

            const float effort = 1f;

            if (_rig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvisSegment) && pelvisSegment.Body != null)
                _minPelvisHeightM = Mathf.Min(_minPelvisHeightM, pelvisSegment.Body.position.y);

            EvaluateRuleDepth();
            ReferenceTargetFrame reference = EvaluateReferenceTarget();
            _nominalReferenceTarget = reference;
            float barMassKg = _saddle != null && _saddle.Barbell != null
                ? _saddle.Barbell.LoadedMassKg
                : 0f;
            _balanceCorrectionRad = _lastBalanceCorrection.AppliedApRad;
            _mlBalanceCorrectionRad = _lastBalanceCorrection.AppliedMlRad;
            _isCorrectionSaturated = _lastBalanceCorrection.IsBoundSaturated;

            float apFeedbackScale = 1f;
            float ankleProbeResidualRad = 0f;
            float hipProbeResidualRad = 0f;
            float trunkProbeResidualRad = 0f;
#if UNITY_EDITOR
            apFeedbackScale = ApFeedbackContributionEnabled ? 1f : 0f;
            ankleProbeResidualRad = AnkleSagittalOffsetAdditiveRad;
            hipProbeResidualRad = HipSagittalOffsetAdditiveRad;
            trunkProbeResidualRad = TrunkSagittalOffsetAdditiveRad;
#endif
            Quaternion ankleBalance = SagittalAndFrontal(
                _lastBalanceCorrection.AnkleApRad * apFeedbackScale + ankleProbeResidualRad,
                _lastBalanceCorrection.AnkleMlRad);
            Quaternion hipBalance = SagittalAndFrontal(
                _lastBalanceCorrection.HipApRad * apFeedbackScale + hipProbeResidualRad,
                _lastBalanceCorrection.HipMlRad);
            Quaternion abdomenBalance = SagittalAndFrontal(
                _lastBalanceCorrection.TrunkApRad * apFeedbackScale + trunkProbeResidualRad,
                0f);
            Quaternion thoraxBalance = SagittalAndFrontal(
                _lastBalanceCorrection.TrunkApRad * apFeedbackScale + trunkProbeResidualRad,
                0f);

            Quaternion leftAnkleTrim = StaticGravityTrim("left_foot", reference.LeftFoot, barMassKg);
            Quaternion rightAnkleTrim = StaticGravityTrim("right_foot", reference.RightFoot, barMassKg);
            Quaternion leftKneeTrim = StaticGravityTrim("left_shank", reference.LeftShank, barMassKg);
            Quaternion rightKneeTrim = StaticGravityTrim("right_shank", reference.RightShank, barMassKg);
            Quaternion leftHipTrim = StaticGravityTrim("left_thigh", reference.LeftThigh, barMassKg);
            Quaternion rightHipTrim = StaticGravityTrim("right_thigh", reference.RightThigh, barMassKg);
            Quaternion abdomenTrim = StaticGravityTrim("abdomen", reference.Abdomen, barMassKg);
            Quaternion thoraxTrim = StaticGravityTrim("thorax", reference.Thorax, barMassKg);

            Quaternion leftAnkleTarget = Compose("left_foot", reference.LeftFoot, leftAnkleTrim, ankleBalance);
            Quaternion rightAnkleTarget = Compose("right_foot", reference.RightFoot, rightAnkleTrim, ankleBalance);
            Quaternion leftKneeTarget = Compose("left_shank", reference.LeftShank, leftKneeTrim, Quaternion.identity);
            Quaternion rightKneeTarget = Compose("right_shank", reference.RightShank, rightKneeTrim, Quaternion.identity);
            Quaternion leftHipTarget = Compose("left_thigh", reference.LeftThigh, leftHipTrim, hipBalance);
            Quaternion rightHipTarget = Compose("right_thigh", reference.RightThigh, rightHipTrim, hipBalance);
            Quaternion abdomenTarget = Compose("abdomen", reference.Abdomen, abdomenTrim, abdomenBalance);
            Quaternion thoraxTarget = Compose("thorax", reference.Thorax, thoraxTrim, thoraxBalance);

            if (_oneStepRightIkActive)
            {
                if (TrySolveOneStepRightLeg(out Quaternion ikHip, out Quaternion ikKnee, out Quaternion ikFoot))
                {
                    rightHipTarget = Compose("right_thigh", ikHip, Quaternion.identity, Quaternion.identity);
                    rightKneeTarget = Compose("right_shank", ikKnee, Quaternion.identity, Quaternion.identity);
                    rightAnkleTarget = Compose("right_foot", ikFoot, Quaternion.identity, Quaternion.identity);
                }
                else
                {
                    AbortOneStepWalkout("RIGHT_FOOT_TARGET_UNREACHABLE", time.Tick);
                }
            }

            ReferenceRateFrame rate = Mathf.Abs(_phaseVelocity) > 1e-5f
                ? EvaluateReferenceRatePerPhase(_sq, _direction)
                : ReferenceRateFrame.Zero;
            float phaseVelocity = _phaseVelocity;
            ulong tick = time.Tick;

            jointCommandSink.ApplyCommand("left_foot", new JointCommand(leftAnkleTarget, rate.LeftFoot * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("right_foot", new JointCommand(rightAnkleTarget, rate.RightFoot * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("left_shank", new JointCommand(leftKneeTarget, rate.LeftShank * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("right_shank", new JointCommand(rightKneeTarget, rate.RightShank * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("left_thigh", new JointCommand(leftHipTarget, rate.LeftThigh * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("right_thigh", new JointCommand(rightHipTarget, rate.RightThigh * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("abdomen", new JointCommand(abdomenTarget, rate.Abdomen * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand("thorax", new JointCommand(thoraxTarget, rate.Thorax * phaseVelocity, effort, AthleteStrengthScale), tick);
            jointCommandSink.ApplyCommand(
                "head_neck",
                new JointCommand(Quaternion.identity, Vector3.zero, effort, AthleteStrengthScale),
                tick);

            ApplyUpperLimbReference(jointCommandSink, reference, rate, phaseVelocity, effort, tick);
            CheckDriveSaturation(_rig.PoweredController);
        }
        private float OneStepComOffsetMl()
        {
            if (_oneStepWalkoutState == SquatOneStepWalkoutState.SHIFT_TO_LEFT_STANCE ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.VERIFY_RIGHT_UNLOAD ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.RIGHT_SWING_CLEAR ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.RIGHT_SWING_BACK ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.RIGHT_TOUCHDOWN ||
                (_oneStepWalkoutState == SquatOneStepWalkoutState.ABORT_RECOVERY && !_oneStepAbortTouchdownObserved) ||
                (_oneStepWalkoutState == SquatOneStepWalkoutState.ABORTED && !_oneStepAbortTouchdownObserved))
                return _oneStepLeftComTargetWorldX - _supportCenter.x;
            return _referenceComOffsetMl;
        }

        private void AdvanceOneStepWalkout(SimulationTime time)
        {
            if (_oneStepWalkoutState == SquatOneStepWalkoutState.BILATERAL_STANDING ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.ONE_STEP_READY ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.ABORTED)
                return;

            if (time.Tick - _oneStepStateEnteredTick > (ulong)OneStepStateTimeoutTicks(_oneStepWalkoutState))
            {
                if (_oneStepWalkoutState == SquatOneStepWalkoutState.ABORT_RECOVERY)
                    SetOneStepWalkoutState(SquatOneStepWalkoutState.ABORTED, time.Tick);
                else
                    AbortOneStepWalkout("STATE_TIMEOUT_" + _oneStepWalkoutState, time.Tick);
                return;
            }

            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            float loadedImpulse = OneStepImpulseThreshold(OneStepLoadedImpulseFraction, dt);
            float unloadImpulse = OneStepImpulseThreshold(OneStepUnloadImpulseFraction, dt);
            bool leftLoaded = IsFootLoaded(_leftFoot, loadedImpulse);
            bool rightLoaded = IsFootLoaded(_rightFoot, loadedImpulse);
            float comSpeed = _observer.SystemComVelocity.magnitude;

            if (_oneStepWalkoutState == SquatOneStepWalkoutState.ABORT_RECOVERY)
            {
                if (!_oneStepAbortTouchdownObserved && _rightFoot.CompletedContactCount > 0 &&
                    IsRightFootInLandingRegion(_oneStepRightTargetBodyWorld))
                    _oneStepAbortTouchdownObserved = true;
                if (leftLoaded && rightLoaded && IsComControlledByBothFeet(0.20f))
                    _oneStepRecoveryTicks++;
                else
                    _oneStepRecoveryTicks = 0;
                if (_oneStepRecoveryTicks >= OneStepRecoveryPersistenceTicks)
                    SetOneStepWalkoutState(SquatOneStepWalkoutState.ABORTED, time.Tick);
                return;
            }

            if (!HasFiniteComSupport() || !leftLoaded || !float.IsFinite(comSpeed) || comSpeed > 0.75f)
            {
                AbortOneStepWalkout("LEFT_SUPPORT_OR_COM_INVALID", time.Tick);
                return;
            }

            switch (_oneStepWalkoutState)
            {
                case SquatOneStepWalkoutState.SHIFT_TO_LEFT_STANCE:
                    if (!IsComControlledByBothFeet(0.75f))
                    {
                        AbortOneStepWalkout("BILATERAL_COM_UNCONTROLLED", time.Tick);
                        return;
                    }
                    if (_leftFoot.CompletedNormalImpulseTotal > _rightFoot.CompletedNormalImpulseTotal * 1.15f &&
                        _oneStepInitialComWorldX - _observer.SystemCom.x >= 0.025f &&
                        IsComControlledByFoot(_leftFoot, 0.55f))
                        SetOneStepWalkoutState(SquatOneStepWalkoutState.VERIFY_RIGHT_UNLOAD, time.Tick);
                    break;

                case SquatOneStepWalkoutState.VERIFY_RIGHT_UNLOAD:
                    if (!IsComControlledByFoot(_leftFoot, 0.55f))
                    {
                        AbortOneStepWalkout("LEFT_STANCE_COM_UNCONTROLLED", time.Tick);
                        return;
                    }
                    if (_rightFoot.CompletedNormalImpulseTotal < unloadImpulse)
                    {
                        _oneStepUnloadTicks++;
                        if (_rightFoot.CompletedContactCount > 0)
                            _oneStepSawNearZeroTouch = true;
                    }
                    else
                    {
                        _oneStepUnloadTicks = 0;
                        _oneStepOffGroundTicks = 0;
                    }
                    if (_oneStepUnloadTicks >= OneStepUnloadPersistenceTicks)
                    {
                        if (_rightFoot.CompletedContactCount == 0 &&
                            _rightFoot.CompletedNormalImpulseTotal < unloadImpulse)
                        {
                            _oneStepOffGroundTicks++;
                            if (_oneStepOffGroundTicks >= OneStepOffGroundPersistenceTicks)
                            {
                                _oneStepRightTargetBodyWorld = _oneStepRightStartBodyWorld +
                                    Vector3.up * OneStepSwingClearanceM;
                                _oneStepRightIkActive = true;
                                SetOneStepWalkoutState(SquatOneStepWalkoutState.RIGHT_SWING_CLEAR, time.Tick);
                            }
                        }
                        else
                        {
                            _oneStepRightTargetBodyWorld = _oneStepRightStartBodyWorld + Vector3.up * 0.015f;
                            _oneStepRightIkActive = true;
                            _oneStepOffGroundTicks = 0;
                        }
                    }
                    break;

                case SquatOneStepWalkoutState.RIGHT_SWING_CLEAR:
                    _oneStepRightTargetBodyWorld = _oneStepRightStartBodyWorld +
                        Vector3.up * OneStepSwingClearanceM;
                    if (_rightFoot.CompletedContactCount == 0 && IsRightFootClearOfPlatform(0.04f) &&
                        IsComControlledByFoot(_leftFoot, 0.55f))
                    {
                        _oneStepRightTargetBodyWorld = _oneStepRightLandingBodyWorld +
                            Vector3.up * OneStepSwingClearanceM;
                        SetOneStepWalkoutState(SquatOneStepWalkoutState.RIGHT_SWING_BACK, time.Tick);
                    }
                    break;

                case SquatOneStepWalkoutState.RIGHT_SWING_BACK:
                    _oneStepRightTargetBodyWorld = _oneStepRightLandingBodyWorld +
                        Vector3.up * OneStepSwingClearanceM;
                    if (_oneStepRightStartBodyWorld.z - _rig.Segments["right_foot"].Body.position.z >=
                            OneStepPosteriorDistanceM - OneStepLandingToleranceM &&
                        IsRightFootClearOfPlatform(0.04f) && _rightFoot.CompletedContactCount == 0 &&
                        IsComControlledByFoot(_leftFoot, 0.55f))
                    {
                        _oneStepRightTargetBodyWorld = _oneStepRightLandingBodyWorld;
                        SetOneStepWalkoutState(SquatOneStepWalkoutState.RIGHT_TOUCHDOWN, time.Tick);
                    }
                    break;

                case SquatOneStepWalkoutState.RIGHT_TOUCHDOWN:
                    _oneStepRightTargetBodyWorld = _oneStepRightLandingBodyWorld;
                    if (_rightFoot.CompletedContactCount > 0 &&
                        IsRightFootInLandingRegion(_oneStepRightLandingBodyWorld))
                    {
                        _oneStepLoadTicks = 0;
                        SetOneStepWalkoutState(SquatOneStepWalkoutState.RIGHT_LOAD_ACCEPT, time.Tick);
                    }
                    break;

                case SquatOneStepWalkoutState.RIGHT_LOAD_ACCEPT:
                    _oneStepRightTargetBodyWorld = _oneStepRightLandingBodyWorld;
                    if (leftLoaded && rightLoaded)
                        _oneStepLoadTicks++;
                    else
                        _oneStepLoadTicks = 0;
                    if (_oneStepLoadTicks >= OneStepLoadAcceptanceTicks)
                        SetOneStepWalkoutState(SquatOneStepWalkoutState.BILATERAL_RECOVERY, time.Tick);
                    break;

                case SquatOneStepWalkoutState.BILATERAL_RECOVERY:
                    _oneStepRightTargetBodyWorld = _oneStepRightLandingBodyWorld;
                    if (leftLoaded && rightLoaded && IsComControlledByBothFeet(0.20f))
                        _oneStepRecoveryTicks++;
                    else
                        _oneStepRecoveryTicks = 0;
                    if (_oneStepRecoveryTicks >= OneStepRecoveryPersistenceTicks)
                        SetOneStepWalkoutState(SquatOneStepWalkoutState.ONE_STEP_READY, time.Tick);
                    break;
            }
        }

        private void AbortOneStepWalkout(string reason, ulong tick)
        {
            if (_oneStepWalkoutState == SquatOneStepWalkoutState.ABORT_RECOVERY ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.ABORTED ||
                _oneStepWalkoutState == SquatOneStepWalkoutState.ONE_STEP_READY)
                return;

            _oneStepFailure = reason;
            if (_oneStepRightIkActive)
            {
                Vector3 foot = _rig.Segments["right_foot"].Body.position;
                _oneStepRightTargetBodyWorld = new Vector3(
                    Mathf.Clamp(foot.x, _oneStepRightStartBodyWorld.x - OneStepLandingToleranceM,
                        _oneStepRightStartBodyWorld.x + OneStepLandingToleranceM),
                    _oneStepRightStartBodyWorld.y,
                    Mathf.Clamp(foot.z, _oneStepRightLandingBodyWorld.z - OneStepLandingToleranceM,
                        _oneStepRightStartBodyWorld.z + OneStepLandingToleranceM));
            }
            else
            {
                _oneStepAbortTouchdownObserved = true;
            }
            _oneStepRecoveryTicks = 0;
            SetOneStepWalkoutState(SquatOneStepWalkoutState.ABORT_RECOVERY, tick);
        }

        private void SetOneStepWalkoutState(SquatOneStepWalkoutState state, ulong tick)
        {
            _oneStepWalkoutState = state;
            _oneStepStateEnteredTick = tick;
        }

        private static int OneStepStateTimeoutTicks(SquatOneStepWalkoutState state)
        {
            switch (state)
            {
                case SquatOneStepWalkoutState.SHIFT_TO_LEFT_STANCE:
                case SquatOneStepWalkoutState.VERIFY_RIGHT_UNLOAD:
                case SquatOneStepWalkoutState.RIGHT_LOAD_ACCEPT:
                    return 400;
                case SquatOneStepWalkoutState.RIGHT_SWING_CLEAR:
                case SquatOneStepWalkoutState.RIGHT_TOUCHDOWN:
                case SquatOneStepWalkoutState.ABORT_RECOVERY:
                    return 300;
                case SquatOneStepWalkoutState.RIGHT_SWING_BACK:
                case SquatOneStepWalkoutState.BILATERAL_RECOVERY:
                    return 500;
                default:
                    return 0;
            }
        }

        private float OneStepImpulseThreshold(float fraction, float dt) =>
            Mathf.Max(0.02f, _observer.SystemMassKg * SquatBalanceObserver.GravityMagnitudeMps2 * dt * fraction);

        private bool IsFootLoaded(PhysicalFootContactDetector foot, float loadedImpulse) =>
            foot != null && foot.CompletedContactCount > 0 &&
            float.IsFinite(foot.CompletedNormalImpulseTotal) &&
            foot.CompletedNormalImpulseTotal >= loadedImpulse;

        private bool HasFiniteComSupport() =>
            _observer.HasSupport && _observer.SystemMassKg > 0f && float.IsFinite(_observer.SystemMassKg) &&
            PoweredJointController.IsFinite(_observer.SystemCom) &&
            PoweredJointController.IsFinite(_observer.SystemComVelocity) &&
            float.IsFinite(_observer.CaptureAp) && float.IsFinite(_observer.CaptureMl) &&
            float.IsFinite(_observer.SupportApMin) && float.IsFinite(_observer.SupportApMax) &&
            float.IsFinite(_observer.SupportMlMin) && float.IsFinite(_observer.SupportMlMax) &&
            float.IsFinite(_observer.SupportPlaneY);

        private bool IsComControlledByFoot(PhysicalFootContactDetector foot, float maximumSpeedMps)
        {
            if (!HasFiniteComSupport() || !float.IsFinite(maximumSpeedMps) ||
                _observer.SystemComVelocity.magnitude > maximumSpeedMps ||
                foot == null || foot.CompletedContactCount == 0)
                return false;

            float minX = float.PositiveInfinity;
            float maxX = float.NegativeInfinity;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;
            for (int index = 0; index < foot.CompletedContactCount; index++)
            {
                Vector3 point = foot.CompletedContactPoint(index);
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.z);
                maxZ = Mathf.Max(maxZ, point.z);
            }
            return _observer.CaptureMl >= minX - 0.04f && _observer.CaptureMl <= maxX + 0.04f &&
                _observer.CaptureAp >= minZ - 0.04f && _observer.CaptureAp <= maxZ + 0.04f;
        }

        private bool IsComControlledByBothFeet(float maximumSpeedMps) =>
            HasFiniteComSupport() && float.IsFinite(maximumSpeedMps) &&
            _observer.SystemComVelocity.magnitude <= maximumSpeedMps &&
            _observer.CaptureMargin2D >= -0.025f;

        private bool IsRightFootClearOfPlatform(float clearanceM)
        {
            Collider collider = _rig.Segments["right_foot"].Collider;
            return collider != null && float.IsFinite(_oneStepSupportPlaneY) &&
                collider.bounds.min.y >= _oneStepSupportPlaneY + clearanceM;
        }

        private bool IsRightFootInLandingRegion(Vector3 target)
        {
            Vector3 position = _rig.Segments["right_foot"].Body.position;
            return Mathf.Abs(position.x - target.x) <= OneStepLandingToleranceM &&
                Mathf.Abs(position.z - target.z) <= OneStepLandingToleranceM &&
                Mathf.Abs(position.y - target.y) <= OneStepLandingToleranceM;
        }

        private bool TrySolveOneStepRightLeg(
            out Quaternion hipTarget,
            out Quaternion kneeTarget,
            out Quaternion ankleTarget)
        {
            hipTarget = kneeTarget = ankleTarget = Quaternion.identity;
            PhysicalAthleteRig.SegmentRuntime pelvis = _rig.Segments["pelvis"];
            PoweredJointController.PoweredJointRuntime hipJoint = _rig.PoweredController.GetJoint("right_thigh");
            PoweredJointController.PoweredJointRuntime kneeJoint = _rig.PoweredController.GetJoint("right_shank");
            PoweredJointController.PoweredJointRuntime footJoint = _rig.PoweredController.GetJoint("right_foot");

            Vector3 hip = hipJoint.Joint.connectedBody.transform.TransformPoint(hipJoint.Joint.connectedAnchor);
            Vector3 ankle = _oneStepRightTargetBodyWorld + _oneStepRightFootBodyRotation * footJoint.Joint.anchor;
            Vector3 legVector = Vector3.ProjectOnPlane(ankle - hip, Vector3.right);
            float distance = legVector.magnitude;
            float thighLength = _referenceCalibration.RightThighLengthM;
            float shankLength = _referenceCalibration.RightShankLengthM;
            float minimumReach = Mathf.Abs(thighLength - shankLength) + 0.01f;
            float maximumReach = thighLength + shankLength - 0.01f;
            if (!PoweredJointController.IsFinite(hip) || !PoweredJointController.IsFinite(ankle) ||
                !float.IsFinite(distance) || distance < minimumReach || distance > maximumReach ||
                Mathf.Abs(ankle.x - hip.x) > 0.04f)
                return false;

            Vector3 direction = legVector / distance;
            Vector3 forwardSide = Vector3.ProjectOnPlane(Vector3.forward, direction);
            if (forwardSide.sqrMagnitude < 1e-6f)
                return false;
            forwardSide.Normalize();
            if (Vector3.Dot(forwardSide, Vector3.forward) < 0f)
                forwardSide = -forwardSide;

            float along = (thighLength * thighLength - shankLength * shankLength + distance * distance) /
                (2f * distance);
            float bend = Mathf.Sqrt(Mathf.Max(0f, thighLength * thighLength - along * along));
            Vector3 knee = hip + direction * along + forwardSide * bend;
            Vector3 shankUp = (knee - ankle).normalized;
            Vector3 thighUp = (hip - knee).normalized;
            Quaternion shankFrame = Quaternion.FromToRotation(
                _referenceCalibration.RightShank.AnatomicalFrameBind.Up,
                shankUp) * _referenceCalibration.RightShank.AnatomicalFrameBind.Rotation;
            Quaternion thighFrame = Quaternion.FromToRotation(
                _referenceCalibration.RightThigh.AnatomicalFrameBind.Up,
                thighUp) * _referenceCalibration.RightThigh.AnatomicalFrameBind.Rotation;
            Quaternion thighBodyRotation = ToPhysicalBodyRotation(
                "right_thigh", thighFrame * _referenceCalibration.RightThigh.BoneFromAnatomicalFrame);
            Quaternion shankBodyRotation = ToPhysicalBodyRotation(
                "right_shank", shankFrame * _referenceCalibration.RightShank.BoneFromAnatomicalFrame);

            hipTarget = ToLogicalJointTarget("right_thigh", pelvis.Body.rotation, thighBodyRotation);
            kneeTarget = ToLogicalJointTarget("right_shank", thighBodyRotation, shankBodyRotation);
            ankleTarget = ToLogicalJointTarget("right_foot", shankBodyRotation, _oneStepRightFootBodyRotation);
            return IsWithinJointFlexionBound("right_thigh", hipTarget) &&
                IsWithinJointFlexionBound("right_shank", kneeTarget) &&
                IsWithinJointFlexionBound("right_foot", ankleTarget);
        }

        private bool IsWithinJointFlexionBound(string jointId, Quaternion target)
        {
            if (!PoweredJointController.IsFinite(target))
                return false;
            PhysicalJointRecipe recipe = _rig.PoweredController.GetJoint(jointId).Recipe;
            float degrees = PoweredJointController.SignedTwistRadians(target, Vector3.right) * Mathf.Rad2Deg;
            return float.IsFinite(degrees) && degrees >= recipe.LowDegrees - 0.5f &&
                degrees <= recipe.HighDegrees + 0.5f;
        }

        private void AdvanceStateAndPhase(float dt, PlayerIntentFrame intent)
        {
            float yieldInput = Mathf.Max(intent.Yield01, intent.YieldHeld ? 1f : 0f);
            float driveInput = Mathf.Max(intent.Drive01, intent.DriveHeld ? 1f : 0f);
            bool braceInput = intent.Brace01 > 0.05f || intent.BraceHeld || intent.WasPressed(IntentAction.Brace) ||
                intent.WasPressed(IntentAction.Confirm) || intent.ConfirmHeld;

#if UNITY_EDITOR
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
                    case SquatState.STICKING:
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
#endif

            if (!_squatCommandIssued)
                return;

            // The issued squat command opens player-driven reference motion.
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

            if ((_state == SquatState.ASCENT || _state == SquatState.STICKING) && driveInput > 0.05f)
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

        private void ComputeComAndSupport(PhysicalObservation observation)
        {
            // Support is the plantar contact polygon the solver actually
            // produced last step, not the midpoint of the two foot bodies.
            // A foot body centre reports support the athlete may not have.
            _observer.BeginPhysicsStep();
            _observer.Observe(observation, _saddle, LowestFootBodyY());

            _systemCom = _observer.SystemCom;
            Vector3 canonicalSupportCenter = _referenceSupportCenter;
            _supportCenter = new Vector3(
                _observer.HasSupport ? _observer.SupportMlCenter : canonicalSupportCenter.x,
                _observer.SupportPlaneY,
                _observer.HasSupport ? _observer.SupportApCenter : canonicalSupportCenter.z);

            _apComError = (_systemCom.z - _supportCenter.z) - _referenceComOffsetAp;
            _mlComError = (_systemCom.x - _supportCenter.x) - _referenceComOffsetMl;
        }

        /// <summary>
        /// Bilateral GAM-10 hip-crease and knee-top surface proxies on the
        /// physical rig. Joint centers remain a separate diagnostic.
        /// </summary>
        private void EvaluateRuleDepth()
        {
            if (!TryGetSurfaceRuleLandmarks(out SquatRuleLandmarkSet landmarks))
            {
                LegalDepth = false;
                RuleDepthLeftM = float.NaN;
                RuleDepthRightM = float.NaN;
                WorstSideDepthM = float.NaN;
                JointCenterDepthDiagnostic = new SquatJointCenterDepthDiagnostic(float.NaN, float.NaN);
                return;
            }

            SquatDepthObservation depth = landmarks.Depth;
            RuleDepthLeftM = depth.LeftDepthM;
            RuleDepthRightM = depth.RightDepthM;
            WorstSideDepthM = depth.WorstSideDepthM;
            LegalDepth = depth.BilateralGameJudgmentQualified;
            JointCenterDepthDiagnostic = landmarks.JointCenterDepthDiagnostic;
        }

        /// <summary>Captures the shared GAM-10 surface proxies without writing physics state.</summary>
        public bool TryGetSurfaceRuleLandmarks(out SquatRuleLandmarkSet landmarks)
        {
            landmarks = default;
            if (_referenceCalibration == null ||
                !_rig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvis) ||
                pelvis.Body == null ||
                !_rig.Segments.TryGetValue("left_shank", out PhysicalAthleteRig.SegmentRuntime leftShank) ||
                leftShank.Body == null ||
                !_rig.Segments.TryGetValue("right_shank", out PhysicalAthleteRig.SegmentRuntime rightShank) ||
                rightShank.Body == null ||
                !TryJointAnchor("left_thigh", out Vector3 leftHipCenter) ||
                !TryJointAnchor("right_thigh", out Vector3 rightHipCenter) ||
                !TryJointAnchor("left_shank", out Vector3 leftKneeCenter) ||
                !TryJointAnchor("right_shank", out Vector3 rightKneeCenter))
                return false;

            Quaternion pelvisFrameRotation = ReferenceFrameWorldRotation(
                pelvis,
                _referenceCalibration.Pelvis);
            Quaternion leftShankFrameRotation = ReferenceFrameWorldRotation(
                leftShank,
                _referenceCalibration.LeftShank);
            Quaternion rightShankFrameRotation = ReferenceFrameWorldRotation(
                rightShank,
                _referenceCalibration.RightShank);
            return _depthLandmarkProvider.TryEvaluate(
                leftHipCenter,
                rightHipCenter,
                pelvisFrameRotation,
                leftKneeCenter,
                leftShankFrameRotation,
                rightKneeCenter,
                rightShankFrameRotation,
                out landmarks);
        }

        private static Quaternion ReferenceFrameWorldRotation(
            PhysicalAthleteRig.SegmentRuntime segment,
            SquatReferenceBoneFrame calibration) =>
            segment.Body.rotation * segment.BodyToReferenceBoneRotation *
            Quaternion.Inverse(calibration.BoneFromAnatomicalFrame);

        private bool TryJointAnchor(string jointId, out Vector3 worldAnchor)
        {
            worldAnchor = Vector3.zero;
            PoweredJointController.PoweredJointRuntime runtime = _rig.PoweredController.GetJoint(jointId);
            if (runtime == null || runtime.Joint == null)
                return false;
            worldAnchor = runtime.Joint.transform.TransformPoint(runtime.Joint.anchor);
            return true;
        }

#if UNITY_EDITOR
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
#endif

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

#if UNITY_EDITOR
        /// <summary>
        /// The equilibrium bias term of
        /// FINAL = NOMINAL_GAM10 + GRAVITY_EQUILIBRIUM_BIAS + DYNAMIC_BALANCE,
        /// in logical joint space.
        ///
        /// The flat per-family bias stays as it was. The spine additionally
        /// carries the 5H15 phase and load calibration, because the moment it
        /// holds changes by a factor of five between standing and the bottom
        /// and by half again with the bar, so a single constant cannot be
        /// right at both ends.
        /// </summary>
        private float PreloadLogicalRad(SquatJointFamily family) =>
            UnitContract.DegreesToRadians(_preload.BiasDegrees(family, _sq, EquilibriumLoadKg)) *
            _familyFlexionSign[(int)family];
#endif

        private Quaternion Compose(string jointId, Quaternion nominal, Quaternion gravityBias, Quaternion balanceOffset)
        {
            Quaternion final = nominal * gravityBias * balanceOffset;
            _composition[jointId] = new JointTargetComposition(nominal, gravityBias, balanceOffset, final);
            return final;
        }

        // Segments carried by both legs at once; for a leg joint the free
        // (non-grounded) side is half of this set plus that leg's distal-from-
        // pelvis segments above the joint.
        private static readonly string[] UpperBodySegments =
        {
            "pelvis", "abdomen", "thorax", "head_neck", "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm", "left_hand", "right_hand"
        };

        /// <summary>
        /// Static gravity compensation, physically derived (GAM-50 R9). The
        /// target is offset about the joint's logical flexion axis by
        /// tau_g / K, where tau_g is the gravity moment the drive must hold:
        /// the weight of the joint's free subtree (child side for the trunk
        /// joints; parent side, with the upper body and bar shared equally by
        /// the two grounded legs, for hips and knees) about the joint anchor,
        /// from the measured configuration and the bar actually on the saddle.
        /// At equilibrium the drive then holds q_ref instead of sagging by
        /// tau_g / K, and gravity's destabilising stiffness is cancelled
        /// rather than fought. Bounded by the joint's finite capacity. The
        /// ankles carry no compensation: they are the balance loop's actuator.
        ///
        /// Replaces the GAM-13 fitted lever-arm trim, whose constants were
        /// fitted while the unconverged solve delivered 20-60% of K e and so
        /// supplied under a quarter of the knee offset a deep heavy hold needs
        /// once the drives converge.
        /// </summary>
        private Quaternion StaticGravityTrim(string jointId, Quaternion nominal, float barMassKg)
        {
            bool leg = jointId == "left_shank" || jointId == "right_shank" ||
                jointId == "left_thigh" || jointId == "right_thigh";
            bool trunk = jointId == "abdomen" || jointId == "thorax";
            if (!leg && !trunk)
                return Quaternion.identity;

            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            ConfigurableJoint configurable = joint.Joint;
            Vector3 pivot = configurable.transform.TransformPoint(configurable.anchor);
            Vector3 axis = configurable.transform.TransformDirection(configurable.axis).normalized;
            Vector3 gravity = Physics.gravity;
            Vector3 moment = Vector3.zero;
            float upperShare = leg ? 0.5f : 1f;

            void Add(Rigidbody body, float share)
            {
                if (body == null)
                    return;
                moment += Vector3.Cross(body.worldCenterOfMass - pivot, body.mass * share * gravity);
            }

            bool carriesBar;
            if (leg)
            {
                foreach (string id in UpperBodySegments)
                    Add(_rig.Segments[id].Body, upperShare);
                if (jointId == "left_shank")
                    Add(_rig.Segments["left_thigh"].Body, 1f);
                else if (jointId == "right_shank")
                    Add(_rig.Segments["right_thigh"].Body, 1f);
                carriesBar = true;
            }
            else
            {
                carriesBar = false;
                foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                {
                    if (IsInSubtree(recipe.Id, jointId))
                    {
                        Add(_rig.Segments[recipe.Id].Body, 1f);
                        carriesBar |= recipe.Id == "thorax";
                    }
                }
            }

            if (carriesBar && barMassKg > 0f && _saddle != null && _saddle.IsAttached &&
                _saddle.Barbell != null && _saddle.Barbell.Body != null)
            {
                Rigidbody bar = _saddle.Barbell.Body;
                moment += Vector3.Cross(bar.worldCenterOfMass - pivot, barMassKg * upperShare * gravity);
            }

            // Drive torque on the child about +axis equals K e. Free child side:
            // K e = -G . a; free parent side: K e = +G . a.
            float requiredTorqueNm = (leg ? 1f : -1f) * Vector3.Dot(moment, axis);
            JointFamilyProfile profile = joint.Profile.Value;
            float capacityNm = profile.BaseCapacityNm * AthleteStrengthScale;
            requiredTorqueNm = Mathf.Clamp(requiredTorqueNm, -capacityNm, capacityNm);
            return SagittalAndFrontal(requiredTorqueNm / profile.Spring, 0f);
        }

        private static bool IsInSubtree(string segmentId, string rootId)
        {
            string current = segmentId;
            while (current != null)
            {
                if (current == rootId)
                    return true;
                current = ParentOf(current);
            }
            return false;
        }

        private static string ParentOf(string segmentId)
        {
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                if (recipe.Id == segmentId)
                    return recipe.ParentId;
            }
            return null;
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

#if UNITY_EDITOR
        /// <summary>
        /// Angular velocity of the composed spine target, in the same
        /// parent-frame convention EvaluateReferenceRatePerPhase produces.
        ///
        /// The final target is NOMINAL * GRAVITY_BIAS, and for a left-composed
        /// rate convention the product rule is
        /// omega(A*B) = omega(A) + Rot(A) * omega(B), so the bias rate has to
        /// be rotated by the nominal before it is added. The nominal spine
        /// target is very nearly a pure rotation about the sagittal axis, so
        /// Rot(nominal) leaves that axis almost unchanged and naive addition
        /// would be close; it is done exactly anyway because it costs one
        /// quaternion-vector product and does not depend on that being true.
        ///
        /// Balance is deliberately absent. It is feedback state, not a
        /// deterministic trajectory, and differentiating it would feed loop
        /// noise into the drive.
        ///
        /// Allocation-free and O(1): the bias slope is read from the
        /// calibrated table rather than differenced at runtime.
        /// </summary>
        private Vector3 SpineTargetRate(
            Vector3 nominalRatePerPhase,
            float phaseVelocity,
            Quaternion nominalTarget,
            SquatJointFamily family)
        {
            Vector3 nominalRate = nominalRatePerPhase * phaseVelocity;
            if (!SpineBiasRateFeedforwardEnabled || Mathf.Abs(phaseVelocity) <= 1e-5f)
                return nominalRate;

            float biasRateRadPerPhase = UnitContract.DegreesToRadians(
                _preload.BiasRateDegreesPerPhase(family, _sq, EquilibriumLoadKg)) *
                _familyFlexionSign[(int)family];
            Vector3 biasRate = nominalTarget * (Vector3.right * (biasRateRadPerPhase * phaseVelocity));
            return nominalRate + biasRate;
        }

        /// <summary>
        /// On is production since 5H16. Off reproduces the 5H15 command, whose
        /// target angular velocity carried the nominal reference derivative
        /// only while its target position also carried the gravity bias, so
        /// the drive was told to travel along one path and arrive on another.
        /// </summary>
        public bool SpineBiasRateFeedforwardEnabled { get; set; } = true;
#endif

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
            IPhysicalAthleteJointCommandSink jointCommandSink,
            ReferenceTargetFrame referenceTarget,
            ReferenceRateFrame rate,
            float phaseVelocity,
            float effort,
            ulong tick)
        {
            ApplyUpperLimbJoint(jointCommandSink, "left_upper_arm", referenceTarget.LeftUpperArm, rate.LeftUpperArm, phaseVelocity, effort, tick);
            ApplyUpperLimbJoint(jointCommandSink, "right_upper_arm", referenceTarget.RightUpperArm, rate.RightUpperArm, phaseVelocity, effort, tick);
            ApplyUpperLimbJoint(jointCommandSink, "left_forearm", referenceTarget.LeftForearm, rate.LeftForearm, phaseVelocity, effort, tick);
            ApplyUpperLimbJoint(jointCommandSink, "right_forearm", referenceTarget.RightForearm, rate.RightForearm, phaseVelocity, effort, tick);
            ApplyUpperLimbJoint(jointCommandSink, "left_hand", referenceTarget.LeftHand, rate.LeftHand, phaseVelocity, effort, tick);
            ApplyUpperLimbJoint(jointCommandSink, "right_hand", referenceTarget.RightHand, rate.RightHand, phaseVelocity, effort, tick);
        }

        private void ApplyUpperLimbJoint(
            IPhysicalAthleteJointCommandSink jointCommandSink,
            string jointId,
            Quaternion referenceTarget,
            Vector3 ratePerPhase,
            float phaseVelocity,
            float effort,
            ulong tick)
        {
            Quaternion target = Compose(jointId, referenceTarget, Quaternion.identity, Quaternion.identity);
            jointCommandSink.ApplyCommand(
                jointId,
                new JointCommand(target, ratePerPhase * phaseVelocity, effort, AthleteStrengthScale),
                tick);
        }

        private ReferenceTargetFrame[] BuildReferenceTargetTable()
        {
            Animator referenceAnimator = _rig.ReferenceAnimator;
            if (referenceAnimator == null)
                throw new InvalidOperationException("GAM-11 physical squat requires the canonical reference animator.");

            SquatReferenceRigCalibration calibration = SquatReferenceRigCalibration.Build(
                referenceAnimator,
                referenceAnimator.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");
            _referenceCalibration = calibration;
            _depthLandmarkProvider = new SquatDepthLandmarkProvider(calibration);

            Vector3 leftStandingFootAnchor = calibration.LeftFoot.PlantarAnchorWorld;
            Vector3 rightStandingFootAnchor = calibration.RightFoot.PlantarAnchorWorld;
            LeftReferencePlantarAnchorWorld = leftStandingFootAnchor;
            RightReferencePlantarAnchorWorld = rightStandingFootAnchor;
            var targets = new ReferenceTargetFrame[ReferenceTargetSampleCount];
            for (int index = 0; index < ReferenceTargetSampleCount; index++)
            {
                float phase = index / (float)(ReferenceTargetSampleCount - 1);
                targets[index] = BuildReferenceTargetFrame(
                    calibration,
                    SquatPhaseDirection.Descent,
                    phase,
                    leftStandingFootAnchor,
                    rightStandingFootAnchor);
            }
            return targets;
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
                pelvis,
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
            return PoweredJointController.ToLogicalTargetRotation(
                joint.NeutralParentToChild,
                joint.JointSpace,
                desiredRelative);
        }

        private ReferenceTargetFrame EvaluateReferenceTarget() => EvaluateReferenceTarget(_sq, _direction);

        private ReferenceTargetFrame EvaluateReferenceTarget(float phase, SquatPhaseDirection direction)
        {
            float scaled = Mathf.Clamp01(phase) * (ReferenceTargetSampleCount - 1);
            int lowerIndex = Mathf.FloorToInt(scaled);
            int upperIndex = Mathf.Min(ReferenceTargetSampleCount - 1, lowerIndex + 1);
            float interpolation = scaled - lowerIndex;
            return ReferenceTargetFrame.Interpolate(
                _referenceTargets[lowerIndex], _referenceTargets[upperIndex], interpolation);
        }

        /// <summary>
        /// Reference target angular rate in logical joint space, per unit of
        /// phase. Multiplied by the current phase velocity it becomes the
        /// feed-forward target velocity the drive expects, rather than the
        /// zero the adapter used to send while the reference was moving.
        /// </summary>
        private readonly struct ReferenceBodyPose
        {
            public ReferenceBodyPose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }

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
            float scaled = Mathf.Clamp01(phase) * (ReferenceTargetSampleCount - 1);
            int lowerIndex = Mathf.Clamp(Mathf.FloorToInt(scaled), 0, ReferenceTargetSampleCount - 2);
            int upperIndex = lowerIndex + 1;
            float phaseStep = 1f / (ReferenceTargetSampleCount - 1);
            ReferenceTargetFrame from = _referenceTargets[lowerIndex];
            ReferenceTargetFrame to = _referenceTargets[upperIndex];
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
                Quaternion pelvisBodyRotation,
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
                PelvisBodyRotation = pelvisBodyRotation;
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

            public Quaternion PelvisBodyRotation { get; }
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
                    Quaternion.Slerp(from.PelvisBodyRotation, to.PelvisBodyRotation, interpolation),
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
        /// Standing posture diagnostics for the previous post-physics state
        /// in logical joint coordinates. Canonical pose error compares actual
        /// pose with the canonical reference; target deflection compares
        /// actual pose with the composed target and is an elastic tracking
        /// diagnostic, not posture error. Limit proximity is independent.
        ///
        /// The canonical pose error is what stops balance from spending
        /// posture: the controller cannot withdraw authority from a cost it
        /// cannot see.
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

        public float CanonicalPostureErrorRad => _canonicalPoseErrorRad;
        public float CanonicalPostureErrorRateRadPerS => _canonicalPoseErrorRateRadPerS;
        public float TargetActualDeflectionRad => _targetActualDeflectionRad;
        public float CanonicalPostureLimitProximity => _postureLimitProximity;
        public float CanonicalPostureUnexpectedMarginConsumed => _postureUnexpectedMarginConsumed;

        /// <summary>
        /// Bar load the spine equilibrium calibration is evaluated at. The
        /// prototype controller is the single writer; nothing else sets it,
        /// so there is one authority for the load the bias assumes.
        /// </summary>
#if UNITY_EDITOR
        public float EquilibriumLoadKg { get; set; }
#endif
        public string CanonicalPostureWorstJoint => _postureWorstJoint;

        private float _canonicalPoseErrorRad;
        private float _canonicalPoseErrorRateRadPerS;
        private float _targetActualDeflectionRad;
        private float _targetActualDeflectionRateRadPerS;
        private float _postureLimitProximity;
        private float _postureUnexpectedMarginConsumed;
        private string _postureWorstJoint = "NONE";
        private bool _hasPostureHistory;

#if UNITY_EDITOR
        private void ObserveCanonicalPosture(float dt)
        {
            float worstCanonicalError = 0f;
            float worstTargetDeflection = 0f;
            float worstLimit = 0f;
            float worstConsumed = 0f;
            string worstJoint = "NONE";

            for (int index = 0; index < CanonicalPostureJoints.Length; index++)
            {
                string jointId = CanonicalPostureJoints[index];
                if (!_composition.TryGetValue(jointId, out JointTargetComposition composition))
                    continue;
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
                if (joint == null)
                    continue;

                float canonicalErrorRad = Quaternion.Angle(
                    composition.Nominal,
                    joint.Diagnostic.ActualRelative) * Mathf.Deg2Rad;
                float targetDeflectionRad = Quaternion.Angle(
                    composition.Final,
                    joint.Diagnostic.ActualRelative) * Mathf.Deg2Rad;
                if (canonicalErrorRad > worstCanonicalError)
                {
                    worstCanonicalError = canonicalErrorRad;
                    worstJoint = jointId;
                }
                worstTargetDeflection = Mathf.Max(worstTargetDeflection, targetDeflectionRad);
                worstLimit = Mathf.Max(worstLimit, joint.Diagnostic.LimitProximity);
                worstConsumed = Mathf.Max(worstConsumed, ConsumedMarginFraction(joint, composition));
            }

            _canonicalPoseErrorRateRadPerS = _hasPostureHistory && dt > 0f
                ? (worstCanonicalError - _canonicalPoseErrorRad) / dt
                : 0f;
            _targetActualDeflectionRateRadPerS = _hasPostureHistory && dt > 0f
                ? (worstTargetDeflection - _targetActualDeflectionRad) / dt
                : 0f;
            _canonicalPoseErrorRad = worstCanonicalError;
            _targetActualDeflectionRad = worstTargetDeflection;
            _postureLimitProximity = worstLimit;
            _postureUnexpectedMarginConsumed = worstConsumed;
            _postureWorstJoint = worstJoint;
            _hasPostureHistory = true;

            _balanceController.ObservePosture(
                ExperimentalUseTargetDeflectionForPostureGuard ? _targetActualDeflectionRad : _canonicalPoseErrorRad,
                ExperimentalUseTargetDeflectionForPostureGuard ? _targetActualDeflectionRateRadPerS : _canonicalPoseErrorRateRadPerS,
                _postureLimitProximity,
                _postureUnexpectedMarginConsumed);
        }

        /// <summary>
        /// The share of the limit margin the accepted reference left unused
        /// that this joint has since given away.
        ///
        /// Nominal occupancy is what the reference itself commands; the other
        /// two are what the composed target asks for and what the joint
        /// actually reached. Both are measured against the same denominator,
        /// the margin the reference left, so a reference that deliberately
        /// sits deep tightens the scale rather than tripping the guard on its
        /// own. A joint tracking its reference returns zero at any depth.
        /// </summary>
        private static float ConsumedMarginFraction(
            PoweredJointController.PoweredJointRuntime joint,
            JointTargetComposition composition)
        {
            float low = joint.Recipe.LowDegrees;
            float high = joint.Recipe.HighDegrees;

            float expected = PoweredJointController.LimitProximityOf(
                composition.Nominal * composition.GravityBias,
                low,
                high);
            float headroom = Mathf.Max(1f - expected, 0.001f);

            float commanded = PoweredJointController.LimitProximityOf(composition.Final, low, high);
            float actual = joint.Diagnostic.LimitProximity;

            float consumed = Mathf.Max(commanded, actual) - expected;
            return Mathf.Clamp01(consumed / headroom);
        }
#endif

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
            _isDriveSaturated = maximum >= PoweredJointController.ModeledDemandSaturationThreshold;
        }

#if UNITY_EDITOR
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
#endif
    }
}
