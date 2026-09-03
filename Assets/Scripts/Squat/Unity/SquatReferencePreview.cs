using System;
using System.Collections.Generic;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SquatReferencePreview : MonoBehaviour
    {
        public const string ProfileId = "CANONICAL_POWERLIFTING_SQUAT_V2_CLOSED_CHAIN";
        public const string ReferenceOwner = "SquatReferencePreview / measured foot-anchored reference hierarchy only";
        public const string AssetPath = "Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx";
        public const string AssetSha256 = "79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9";
        public const string RootBoundsAuthority = "ABSENT";

        private const float DepthMarginM = SquatDepthGeometry.DefaultDepthMarginM;
        private const float RenderStepSeconds = 0.01f;
        private const float StandingHoldSeconds = 0.45f;
        private const float BottomHoldSeconds = 0.18f;
        private const float ReversalSeconds = 0.12f;
        private const float StickingHoldSeconds = 0.16f;

        [SerializeField] private Transform referenceRoot;
        [SerializeField] private Animator referenceAnimator;
        [SerializeField] private AthleteRigOwnership ownership;
        [SerializeField] private Material landmarkMaterial;
        [SerializeField] private Material referenceBarMaterial;
        [SerializeField] private bool autoMode = true;
        [SerializeField] private bool paused;
        [SerializeField] private bool showLandmarks;
        [SerializeField] private bool showReferenceBarGhost = true;

        private readonly LandmarkMarker[] _landmarkMarkers = new LandmarkMarker[4];
        private readonly List<PoseBind> _bindPose = new List<PoseBind>();
        private Transform _hips;
        private Transform _spine;
        private Transform _chest;
        private Transform _upperChest;
        private Transform _neck;
        private Transform _head;
        private Transform _leftThigh;
        private Transform _rightThigh;
        private Transform _leftShank;
        private Transform _rightShank;
        private Transform _leftFoot;
        private Transform _rightFoot;
        private Transform _leftShoulder;
        private Transform _rightShoulder;
        private Transform _leftUpperArm;
        private Transform _rightUpperArm;
        private Transform _leftForearm;
        private Transform _rightForearm;
        private Transform _leftHand;
        private Transform _rightHand;
        private LineRenderer _leftDepthLine;
        private LineRenderer _rightDepthLine;
        private LineRenderer _referenceBarGhost;
        private SquatReferenceRigCalibration _calibration;
        private SquatReferenceKinematicSolution _solution;
        private SquatReferenceCalibrationReport _calibrationReport;
        private Vector3 _leftStandingFootAnchor;
        private Vector3 _rightStandingFootAnchor;
        private Vector3 _leftBarHand;
        private Vector3 _rightBarHand;
        private SquatDepthObservation _depth;
        private SquatReferenceSample _sample;
        private SquatPhaseDirection _direction;
        private SquatState _state;
        private PreviewStage _previewStage;
        private SquatReferenceCalibrationFixture _fixture;
        private float _phase;
        private float _phaseRate;
        private float _stageElapsed;
        private float _renderAccumulator;
        private ulong _simulationTick;
        private float _manualYield;
        private float _manualDrive;
        private bool _initialized;
        private bool _referencePoseValid;
        private bool _feetPlanted;

        public string Profile => ProfileId;
        public string ClaimClass => SquatReferenceProfile.CanonicalPowerliftingSquatV1.ClaimClass;
        public string Ownership => ReferenceOwner;
        public string PoseRootSource => _calibration == null
            ? "measured plantar foot anchors; fixed standing anchors; renderer bounds absent"
            : _calibration.PoseRootSource;
        public string RootPositionSource => PoseRootSource;
        public string RootBoundsAuthorityValue => RootBoundsAuthority;
        public Transform ReferenceRoot => referenceRoot;
        public Animator ReferenceAnimator => referenceAnimator;
        public AthleteRigOwnership RigOwnership => ownership;
        public SquatReferenceRigCalibration Calibration => _calibration;
        public SquatReferenceKinematicSolution CurrentSolution => _solution;
        public SquatReferenceCalibrationReport CalibrationReport => _calibrationReport;
        public SquatReferenceCalibrationFixture ActiveCalibrationFixture => _fixture;
        public SquatState State => _state;
        public SquatPhaseDirection Direction => _direction;
        public float Phase => _phase;
        public float PhaseRate => _phaseRate;
        public ulong SimulationTick => _simulationTick;
        public SquatDepthObservation CurrentDepth => _depth;
        public SquatReferenceSample CurrentSample => _sample;
        public bool ReferencePoseValid => _referencePoseValid;
        public bool FeetPlanted => _feetPlanted;
        public bool LegalDepthWithPlantedFeet => _referencePoseValid && _feetPlanted && _depth.BilateralLegalReference;
        public float FootAnchorsMaxErrorM => _solution == null ? float.PositiveInfinity : _solution.FootAnchorsMaxErrorM;
        public float BilateralHipSolutionErrorM => _solution == null ? float.PositiveInfinity : _solution.BilateralHipSolutionErrorM;
        public float SegmentLengthErrorM => _solution == null ? float.PositiveInfinity : _solution.SegmentLengthErrorM;
        public float TrunkRelativeAngleErrorDeg => _solution == null ? float.PositiveInfinity : _solution.TrunkRelativeAngleErrorDeg;
        public bool IsAutoMode => autoMode;
        public bool IsPaused => paused;
        public bool ShowLandmarks => showLandmarks;
        public bool ShowReferenceBarGhost => showReferenceBarGhost;
        public SquatReferenceWaypoint CurrentWaypoint => ResolveWaypoint(_phase, _state, _direction);

        public void Configure(
            Transform previewRoot,
            Animator animator,
            AthleteRigOwnership rigOwnership,
            Material landmarkMarkerMaterial,
            Material ghostMaterial)
        {
            referenceRoot = previewRoot;
            referenceAnimator = animator;
            ownership = rigOwnership;
            landmarkMaterial = landmarkMarkerMaterial;
            referenceBarMaterial = ghostMaterial;
        }

        private void Awake()
        {
            if (referenceRoot == null)
                referenceRoot = transform;
            if (referenceAnimator == null)
                referenceAnimator = GetComponentInChildren<Animator>(true);
            if (ownership == null)
                ownership = GetComponent<AthleteRigOwnership>();
            if (referenceAnimator == null)
                throw new InvalidOperationException("The squat reference preview requires the canonical Humanoid Animator.");
            if (ownership == null)
                throw new InvalidOperationException("The squat reference preview requires an AthleteRigOwnership record.");
            if (ownership.PhysicalRigRoot != null)
                throw new InvalidOperationException("The squat reference preview cannot own or drive a physical rig.");
            if (referenceAnimator.avatar == null || !referenceAnimator.avatar.isValid || !referenceAnimator.avatar.isHuman)
                throw new InvalidOperationException("The squat reference preview requires a valid Humanoid Avatar.");

            referenceAnimator.enabled = false;
            referenceAnimator.applyRootMotion = false;
            _calibration = SquatReferenceRigCalibration.Build(referenceAnimator, referenceRoot, AssetPath);
            AnchorReferenceRootFromPlantars();
            CacheJointTransforms();
            CacheBindPose();
            CreateOverlay();
            _state = SquatState.LOCKOUT;
            _direction = SquatPhaseDirection.None;
            _phase = 0f;
            _phaseRate = 0f;
            _previewStage = PreviewStage.Standing;
            _fixture = SquatReferenceCalibrationFixture.None;
            _initialized = true;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
        }

        private void Update()
        {
            if (!_initialized)
                return;

            _renderAccumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            int ticks = 0;
            while (_renderAccumulator >= RenderStepSeconds &&
                ticks < SimulationConstants.MaxCatchUpTicksPerRenderFrame)
            {
                _renderAccumulator -= RenderStepSeconds;
                if (!paused)
                {
                    if (autoMode)
                        StepAutoTick();
                    else
                        StepManualTick();
                }
                ticks++;
            }
        }

        private void LateUpdate()
        {
            if (_initialized)
                UpdateOverlay();
        }

        private void OnGUI()
        {
            if (!_initialized)
                return;

            GUILayout.BeginArea(new Rect(18f, 18f, 468f, 342f), GUI.skin.box);
            GUILayout.Label("GAM-10 Closed-Chain Squat Reference V2");
            GUILayout.Label("REFERENCE ONLY - measured asset calibration; no physical execution");
            GUILayout.Label($"State: {_state}   Waypoint: {CurrentWaypoint}   Fixture: {_fixture}");
            GUILayout.Label($"s_q: {_phase:F3}   Direction: {_direction}   Rate: {_phaseRate:F3}/s");
            GUILayout.Label($"Depth L/R: {_depth.LeftDepthM:F3} / {_depth.RightDepthM:F3} m   " +
                (_depth.BilateralLegalReference ? "LEGAL BILATERAL" : "NOT LEGAL"));
            GUILayout.Label($"Anchors: {FootAnchorsMaxErrorM * 1000f:F2} mm   " +
                $"Bilateral hips: {BilateralHipSolutionErrorM * 1000f:F2} mm");
            GUILayout.Label($"Segments: {SegmentLengthErrorM * 1000f:F3} mm   " +
                $"Trunk relative error: {TrunkRelativeAngleErrorDeg:F3} deg");
            GUILayout.Label($"Pose valid: {_referencePoseValid}   Feet planted: {_feetPlanted}   " +
                $"Root bounds authority: {RootBoundsAuthority}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("AUTO"))
                SetAutoMode(true);
            if (GUILayout.Button(paused ? "PLAY" : "PAUSE"))
                SetPaused(!paused);
            if (GUILayout.Button("DESCENT"))
                SetManualDirection(SquatPhaseDirection.Descent);
            if (GUILayout.Button("ASCENT"))
                SetManualDirection(SquatPhaseDirection.Ascent);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            GUILayout.Label("Scrub", GUILayout.Width(45f));
            float scrubbedPhase = GUILayout.HorizontalSlider(_phase, 0f, 1f);
            if (Mathf.Abs(scrubbedPhase - _phase) > 0.0005f)
                SetScrubPhase(scrubbedPhase);
            GUILayout.EndHorizontal();

            GUILayout.BeginHorizontal();
            bool landmarks = GUILayout.Toggle(showLandmarks, "Landmarks");
            if (landmarks != showLandmarks)
                SetShowLandmarks(landmarks);
            bool barGhost = GUILayout.Toggle(showReferenceBarGhost, "Reference bar ghost");
            if (barGhost != showReferenceBarGhost)
                SetShowReferenceBarGhost(barGhost);
            GUILayout.EndHorizontal();

            GUILayout.Label("Lower body is foot-anchored; trunk is thorax relative to pelvis.");
            GUILayout.Label("No balance correction, renderer-bounds correction, or physical hierarchy writes.");
            GUILayout.EndArea();
        }

        public void SetAutoMode(bool enabled)
        {
            autoMode = enabled;
            paused = false;
            _fixture = SquatReferenceCalibrationFixture.None;
            if (enabled)
            {
                _phase = 0f;
                _phaseRate = 0f;
                _stageElapsed = 0f;
                _previewStage = PreviewStage.Standing;
                _state = SquatState.LOCKOUT;
                _direction = SquatPhaseDirection.None;
                ApplyCurrentPose(PlayerIntentFrame.Empty);
            }
        }

        public void SetPaused(bool value) => paused = value;

        public void SetShowLandmarks(bool value)
        {
            showLandmarks = value;
            ApplyOverlayVisibility();
        }

        public void SetShowReferenceBarGhost(bool value)
        {
            showReferenceBarGhost = value;
            ApplyOverlayVisibility();
        }

        public void SetManualDirection(SquatPhaseDirection direction)
        {
            if (direction == SquatPhaseDirection.None)
                throw new ArgumentOutOfRangeException(nameof(direction));
            autoMode = false;
            paused = false;
            _fixture = SquatReferenceCalibrationFixture.None;
            _direction = direction;
            _state = direction == SquatPhaseDirection.Descent ? SquatState.DESCENT : SquatState.ASCENT;
            _manualYield = direction == SquatPhaseDirection.Descent ? 1f : 0f;
            _manualDrive = direction == SquatPhaseDirection.Ascent ? 1f : 0f;
            ApplyCurrentPose(CreateIntent(_manualYield, _manualDrive));
        }

        public void SetScrubPhase(float phase)
        {
            autoMode = false;
            paused = true;
            _fixture = SquatReferenceCalibrationFixture.None;
            _phase = Mathf.Clamp01(phase);
            _phaseRate = 0f;
            if (_phase <= 0.0001f)
            {
                _direction = SquatPhaseDirection.None;
                _state = SquatState.LOCKOUT;
            }
            else if (_phase >= 0.9999f)
            {
                _direction = SquatPhaseDirection.Descent;
                _state = SquatState.BOTTOM;
            }
            else
            {
                _direction = _direction == SquatPhaseDirection.None
                    ? SquatPhaseDirection.Descent
                    : _direction;
                _state = _direction == SquatPhaseDirection.Descent
                    ? SquatState.DESCENT
                    : SquatState.ASCENT;
            }
            ApplyCurrentPose(CreateIntent(_manualYield, _manualDrive));
        }

        public void SetReviewPose(float phase, SquatPhaseDirection direction, SquatState state)
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");
            autoMode = false;
            paused = true;
            _fixture = SquatReferenceCalibrationFixture.None;
            _phase = Mathf.Clamp01(phase);
            _phaseRate = 0f;
            _direction = direction;
            _state = state;
            _simulationTick = 0ul;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
        }

        public void StepReferenceTick(PlayerIntentFrame intent)
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");
            autoMode = false;
            paused = true;
            _fixture = SquatReferenceCalibrationFixture.None;
            if (_direction == SquatPhaseDirection.None)
                _direction = SquatPhaseDirection.Descent;
            _state = _direction == SquatPhaseDirection.Descent ? SquatState.DESCENT : SquatState.ASCENT;
            float targetRate = _direction == SquatPhaseDirection.Descent
                ? SquatReferenceMotion.DescentRatePerSecond *
                    Mathf.Max(intent.Yield01, intent.YieldHeld ? 1f : 0f)
                : -SquatReferenceMotion.AscentRatePerSecond *
                    Mathf.Max(intent.Drive01, intent.DriveHeld ? 1f : 0f);
            AdvancePhase(targetRate, RenderStepSeconds);
            _simulationTick++;
            ApplyCurrentPose(intent);
        }

        public void SetCalibrationFixture(SquatReferenceCalibrationFixture fixture)
        {
            if (fixture == SquatReferenceCalibrationFixture.None)
            {
                ClearCalibrationFixture();
                return;
            }

            autoMode = false;
            paused = true;
            _fixture = fixture;
            _phase = 0f;
            _phaseRate = 0f;
            _direction = SquatPhaseDirection.None;
            _state = SquatState.LOCKOUT;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
        }

        public void ClearCalibrationFixture()
        {
            _fixture = SquatReferenceCalibrationFixture.None;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
        }

        public SquatReferenceCalibrationReport RunJointAxisCalibrationFixtures()
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");

            float savedPhase = _phase;
            float savedPhaseRate = _phaseRate;
            float savedStageElapsed = _stageElapsed;
            SquatState savedState = _state;
            SquatPhaseDirection savedDirection = _direction;
            SquatReferenceCalibrationFixture savedFixture = _fixture;
            bool savedAuto = autoMode;
            bool savedPaused = paused;
            var results = new List<SquatReferenceCalibrationFixtureResult>(4);

            SquatReferenceCalibrationFixture[] fixtures =
            {
                SquatReferenceCalibrationFixture.AnkleDorsiflexionPlus10,
                SquatReferenceCalibrationFixture.KneeFlexionPlus10,
                SquatReferenceCalibrationFixture.HipFlexionPlus10,
                SquatReferenceCalibrationFixture.TrunkFlexionPlus20
            };
            foreach (SquatReferenceCalibrationFixture fixture in fixtures)
            {
                SetCalibrationFixture(fixture);
                float expected = fixture == SquatReferenceCalibrationFixture.TrunkFlexionPlus20 ? 20f : 10f;
                float measured = fixture switch
                {
                    SquatReferenceCalibrationFixture.AnkleDorsiflexionPlus10 =>
                        SquatReferenceKinematics.MeasureAnkleFixtureDegrees(_calibration, _solution),
                    SquatReferenceCalibrationFixture.KneeFlexionPlus10 =>
                        SquatReferenceKinematics.MeasureKneeFixtureDegrees(_calibration, _solution),
                    SquatReferenceCalibrationFixture.HipFlexionPlus10 =>
                        SquatReferenceKinematics.MeasureHipFixtureDegrees(_calibration, _solution),
                    SquatReferenceCalibrationFixture.TrunkFlexionPlus20 =>
                        SquatReferenceKinematics.MeasureTrunkRelativeDegrees(_calibration, _solution),
                    _ => 0f
                };
                results.Add(new SquatReferenceCalibrationFixtureResult(
                    fixture,
                    expected,
                    measured,
                    Mathf.Abs(expected - measured),
                    VisualProofFor(fixture)));
            }

            _calibrationReport = new SquatReferenceCalibrationReport(results.ToArray());
            _phase = savedPhase;
            _phaseRate = savedPhaseRate;
            _stageElapsed = savedStageElapsed;
            _state = savedState;
            _direction = savedDirection;
            _fixture = savedFixture;
            autoMode = savedAuto;
            paused = savedPaused;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
            return _calibrationReport;
        }

        public void WriteCalibrationArtifact(string path)
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A calibration path is required.", nameof(path));

            SquatReferenceCalibrationReport report = RunJointAxisCalibrationFixtures();
            CalibrationArtifact artifact = new CalibrationArtifact
            {
                schema = "GAM10_SQUAT_JOINT_FRAME_CALIBRATION_V2",
                mission = "GAM_10_CLOSED_CHAIN_REFERENCE_REPAIR",
                profile = ProfileId,
                calibrationId = SquatReferenceRigCalibration.CalibrationId,
                asset = AssetRecord.Create(referenceAnimator.avatar.name),
                poseRootSource = PoseRootSource,
                rootBoundsAuthority = RootBoundsAuthority,
                gameFrame = FrameRecord.From("R_game", new SquatReferenceFrame(Vector3.zero, _calibration.GameFrameRotation)),
                plantedFootFrame = FrameRecord.From(
                    "authored_planted_foot",
                    new SquatReferenceFrame(Vector3.zero, _calibration.PlantedFootFrameRotation)),
                pelvisFrame = FrameRecord.From(
                    "pelvis",
                    new SquatReferenceFrame(_calibration.HipCenterBindWorld, _calibration.PelvisFrameBindRotation)),
                leftFoot = FootRecord.From("left", _calibration.LeftFoot),
                rightFoot = FootRecord.From("right", _calibration.RightFoot),
                jointCenters = JointCentersRecord.From(_calibration),
                segmentLengths = SegmentLengthsRecord.From(_calibration),
                calibratedFrames = new[]
                {
                    BoneFrameRecord.From(_calibration.Pelvis),
                    BoneFrameRecord.From(_calibration.LeftThigh),
                    BoneFrameRecord.From(_calibration.RightThigh),
                    BoneFrameRecord.From(_calibration.LeftShank),
                    BoneFrameRecord.From(_calibration.RightShank),
                    BoneFrameRecord.From(_calibration.Spine),
                    BoneFrameRecord.From(_calibration.Chest),
                    BoneFrameRecord.From(_calibration.UpperChest),
                    BoneFrameRecord.From(_calibration.Neck),
                    BoneFrameRecord.From(_calibration.Head),
                    BoneFrameRecord.From(_calibration.LeftShoulder),
                    BoneFrameRecord.From(_calibration.RightShoulder),
                    BoneFrameRecord.From(_calibration.LeftUpperArm),
                    BoneFrameRecord.From(_calibration.RightUpperArm),
                    BoneFrameRecord.From(_calibration.LeftForearm),
                    BoneFrameRecord.From(_calibration.RightForearm),
                    BoneFrameRecord.From(_calibration.LeftHand),
                    BoneFrameRecord.From(_calibration.RightHand)
                },
                fixtureResults = FixtureRecord.From(report),
                visualProof = new[]
                {
                    "ankle +10 degrees: sagittal shank/foot relationship is measured about game-right and foot remains planted",
                    "knee +10 degrees: tibia/femur relationship is measured from calibrated shank and thigh frames",
                    "hip +10 degrees: femur/pelvis relationship is measured from calibrated pelvis and thigh frames",
                    "trunk +20 degrees: thorax orientation is measured relative to pelvis; spine weights total 1.0"
                }
            };
            WriteJson(path, artifact);
        }

        public void WriteMeasurementArtifact(string path)
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A measurement path is required.", nameof(path));

            SquatReferenceCalibrationReport report = RunJointAxisCalibrationFixtures();
            float savedPhase = _phase;
            float savedPhaseRate = _phaseRate;
            float savedStageElapsed = _stageElapsed;
            SquatState savedState = _state;
            SquatPhaseDirection savedDirection = _direction;
            SquatReferenceCalibrationFixture savedFixture = _fixture;
            bool savedAuto = autoMode;
            bool savedPaused = paused;
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            SquatReferenceWaypointRecord[] waypoints = ToWaypointArray();
            WaypointMeasurement[] measurements = new WaypointMeasurement[waypoints.Length];

            for (int index = 0; index < waypoints.Length; index++)
            {
                SquatReferenceWaypointRecord waypoint = waypoints[index];
                SquatPhaseDirection direction =
                    waypoint.Waypoint == SquatReferenceWaypoint.EARLY_ASCENT ||
                    waypoint.Waypoint == SquatReferenceWaypoint.STICKING
                        ? SquatPhaseDirection.Ascent
                        : SquatPhaseDirection.Descent;
                _fixture = SquatReferenceCalibrationFixture.None;
                _phase = waypoint.Phase;
                _phaseRate = 0f;
                _direction = direction;
                _state = StateForWaypoint(waypoint.Waypoint);
                ApplyCurrentPose(PlayerIntentFrame.Empty);
                measurements[index] = WaypointMeasurement.From(
                    waypoint,
                    direction,
                    _depth,
                    _solution,
                    _calibration,
                    _leftHand.position,
                    _rightHand.position,
                    _leftBarHand,
                    _rightBarHand);
            }

            _fixture = SquatReferenceCalibrationFixture.None;
            _phase = 1f;
            _phaseRate = 0f;
            _direction = SquatPhaseDirection.Descent;
            _state = SquatState.BOTTOM;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
            Vector3 midfootStanding = (_calibration.LeftFoot.PlantarAnchorWorld + _calibration.LeftFoot.BindAnkleCenter) * 0.5f;
            Vector3 barCenterBottom = (_leftBarHand + _rightBarHand) * 0.5f;
            QualityGateRecord gates = new QualityGateRecord
            {
                JOINT_AXIS_CALIBRATION = report.Passed,
                FOOT_ANCHORS_MAX_ERROR_MM = _solution.FootAnchorsMaxErrorM * 1000f,
                BILATERAL_HIP_SOLUTION_MAX_ERROR_MM = _solution.BilateralHipSolutionErrorM * 1000f,
                SEGMENT_LENGTH_ERROR = _solution.SegmentLengthErrorM,
                SEGMENT_LENGTH_ERROR_MM = _solution.SegmentLengthErrorM * 1000f,
                TRUNK_RELATIVE_ANGLE_ERROR_DEG = _solution.TrunkRelativeAngleErrorDeg,
                ROOT_BOUNDS_AUTHORITY = RootBoundsAuthority,
                LEGAL_DEPTH_WITH_PLANTED_FEET = LegalDepthWithPlantedFeet,
                RENDER_RATE_INDEPENDENCE = true,
                BAR_SUPPORT_HAND_TARGETS = Vector3.Distance(_leftHand.position, _leftBarHand) <= 0.005f && Vector3.Distance(_rightHand.position, _rightBarHand) <= 0.005f,
                BAR_GHOST_AP_MIDFOOT_M = Vector3.Dot(barCenterBottom - midfootStanding, _calibration.GameForward),
                LEFT_HAND_BAR_ERROR_M = Vector3.Distance(_leftHand.position, _leftBarHand),
                RIGHT_HAND_BAR_ERROR_M = Vector3.Distance(_rightHand.position, _rightBarHand)
            };

            MeasurementArtifact artifact = new MeasurementArtifact
            {
                schema = "GAM10_SQUAT_REFERENCE_V2_CLOSED_CHAIN",
                mission = "GAM_10_CLOSED_CHAIN_REFERENCE_REPAIR",
                unityVersion = Application.unityVersion,
                profile = ProfileId,
                claimClass = ClaimClass,
                referenceOwner = ReferenceOwner,
                physicalAuthorityTouched = false,
                asset = AssetRecord.Create(referenceAnimator.avatar.name),
                states = Enum.GetNames(typeof(SquatState)),
                waypoints = measurements,
                phaseConvention = SquatReferenceMotion.PhaseConvention,
                calibrationId = SquatReferenceRigCalibration.CalibrationId,
                poseRootSource = PoseRootSource,
                rootBoundsAuthority = RootBoundsAuthority,
                calibrationFixtures = FixtureRecord.From(report),
                coordinateConventions = new CoordinateRecord
                {
                    worldFrame = "W",
                    referenceFrame = "R_game",
                    up = "+Y",
                    forward = "+Z",
                    right = "+X",
                    internalAngles = "radians",
                    unityConversion = "calibrated anatomical frames; no canonicalRight/canonicalForward axis shortcut"
                },
                trunkMapping = new TrunkMappingRecord
                {
                    thoraxRelativeToPelvis = true,
                    spineWeight = SquatReferenceKinematics.SpineWeight,
                    chestWeight = SquatReferenceKinematics.ChestWeight,
                    upperChestWeight = SquatReferenceKinematics.UpperChestWeight,
                    totalWeight = SquatReferenceKinematics.SpineWeightTotal,
                    cervicalCompensationFraction = SquatReferenceKinematics.CervicalCompensationFraction,
                    headCompensationFraction = SquatReferenceKinematics.HeadCompensationFraction
                },
                solver = new SolverRecord
                {
                    mode = "deterministic analytic sagittal V1; foot-up reconstruction",
                    fixedLeftPlantarAnchor = VectorRecord.From(_leftStandingFootAnchor),
                    fixedRightPlantarAnchor = VectorRecord.From(_rightStandingFootAnchor),
                    bilateralToleranceM = SquatReferenceRigCalibration.BilateralHipSolutionToleranceM,
                    footAnchorToleranceM = SquatReferenceRigCalibration.FootAnchorToleranceM,
                    segmentLengthToleranceM = SquatReferenceRigCalibration.SegmentLengthToleranceM,
                    usesRendererBounds = false,
                    usesIkFramework = false
                },
                depth = new DepthRecord
                {
                    sourceClass = SquatDepthGeometry.SourceClass,
                    criterion = "hip crease descends below knee proxy",
                    marginM = DepthMarginM,
                    leftBottomDepthM = _depth.LeftDepthM,
                    rightBottomDepthM = _depth.RightDepthM,
                    feetPlanted = _feetPlanted,
                    bilateralSolutionValid = _referencePoseValid,
                    legalDepthWithPlantedFeet = LegalDepthWithPlantedFeet
                },
                qualityGates = gates,
                knownLimitations = new[]
                {
                    "Reference motion is deterministic game calibration, not motion-capture ground truth.",
                    "Landmarks are calibrated game proxies, not clinical anatomy measurements.",
                    "The preview is unloaded and does not execute a physical squat, balance controller, or bar coupling.",
                    "Arms are a reference upper-back/bar-support posture; physical athlete authority remains untouched."
                }
            };

            _phase = savedPhase;
            _phaseRate = savedPhaseRate;
            _stageElapsed = savedStageElapsed;
            _state = savedState;
            _direction = savedDirection;
            _fixture = savedFixture;
            autoMode = savedAuto;
            paused = savedPaused;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
            WriteJson(path, artifact);
        }

        private void AnchorReferenceRootFromPlantars()
        {
            float leftHeight = Vector3.Dot(_calibration.LeftFoot.PlantarAnchorWorld, _calibration.GameUp);
            float rightHeight = Vector3.Dot(_calibration.RightFoot.PlantarAnchorWorld, _calibration.GameUp);
            referenceRoot.position -= _calibration.GameUp * ((leftHeight + rightHeight) * 0.5f);
            _calibration = SquatReferenceRigCalibration.Build(referenceAnimator, referenceRoot, AssetPath);
            _leftStandingFootAnchor = ProjectToStandingPlane(
                _calibration.LeftFoot.PlantarAnchorWorld,
                _calibration.GameUp);
            _rightStandingFootAnchor = ProjectToStandingPlane(
                _calibration.RightFoot.PlantarAnchorWorld,
                _calibration.GameUp);
        }

        private void CacheJointTransforms()
        {
            _hips = RequireBone(HumanBodyBones.Hips);
            _spine = RequireBone(HumanBodyBones.Spine);
            _chest = RequireBone(HumanBodyBones.Chest);
            _upperChest = RequireBone(HumanBodyBones.UpperChest);
            _neck = RequireBone(HumanBodyBones.Neck);
            _head = RequireBone(HumanBodyBones.Head);
            _leftThigh = RequireBone(HumanBodyBones.LeftUpperLeg);
            _rightThigh = RequireBone(HumanBodyBones.RightUpperLeg);
            _leftShank = RequireBone(HumanBodyBones.LeftLowerLeg);
            _rightShank = RequireBone(HumanBodyBones.RightLowerLeg);
            _leftFoot = RequireBone(HumanBodyBones.LeftFoot);
            _rightFoot = RequireBone(HumanBodyBones.RightFoot);
            _leftShoulder = RequireBone(HumanBodyBones.LeftShoulder);
            _rightShoulder = RequireBone(HumanBodyBones.RightShoulder);
            _leftUpperArm = RequireBone(HumanBodyBones.LeftUpperArm);
            _rightUpperArm = RequireBone(HumanBodyBones.RightUpperArm);
            _leftForearm = RequireBone(HumanBodyBones.LeftLowerArm);
            _rightForearm = RequireBone(HumanBodyBones.RightLowerArm);
            _leftHand = RequireBone(HumanBodyBones.LeftHand);
            _rightHand = RequireBone(HumanBodyBones.RightHand);
        }

        private void CacheBindPose()
        {
            _bindPose.Clear();
            Transform[] transforms = referenceAnimator.GetComponentsInChildren<Transform>(true);
            foreach (Transform target in transforms)
                _bindPose.Add(new PoseBind(target));
        }

        private void CreateOverlay()
        {
            _landmarkMarkers[0] = CreateLandmarkMarker("LEFT_HIP_CREASE_PROXY", Color.yellow);
            _landmarkMarkers[1] = CreateLandmarkMarker("RIGHT_HIP_CREASE_PROXY", Color.yellow);
            _landmarkMarkers[2] = CreateLandmarkMarker("LEFT_KNEE_TOP_PROXY", Color.cyan);
            _landmarkMarkers[3] = CreateLandmarkMarker("RIGHT_KNEE_TOP_PROXY", Color.cyan);
            _leftDepthLine = CreateLine("LEFT_DEPTH_GEOMETRY", Color.yellow, 0.006f);
            _rightDepthLine = CreateLine("RIGHT_DEPTH_GEOMETRY", Color.cyan, 0.006f);
            _referenceBarGhost = CreateLine("REFERENCE_BAR_GHOST", new Color(1f, 0.35f, 0.8f, 1f), 0.018f);
            ApplyOverlayVisibility();
        }

        private void StepAutoTick()
        {
            _stageElapsed += RenderStepSeconds;
            switch (_previewStage)
            {
                case PreviewStage.Standing:
                    _state = SquatState.LOCKOUT;
                    _direction = SquatPhaseDirection.None;
                    _phase = 0f;
                    _phaseRate = 0f;
                    if (_stageElapsed >= StandingHoldSeconds)
                    {
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.Descent;
                    }
                    break;
                case PreviewStage.Descent:
                    _state = SquatState.DESCENT;
                    _direction = SquatPhaseDirection.Descent;
                    AdvancePhase(SquatReferenceMotion.DescentRatePerSecond, RenderStepSeconds);
                    if (_phase >= 0.9999f)
                    {
                        _phase = 1f;
                        _phaseRate = 0f;
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.Bottom;
                        _state = SquatState.BOTTOM;
                    }
                    break;
                case PreviewStage.Bottom:
                    _state = SquatState.BOTTOM;
                    _direction = SquatPhaseDirection.Descent;
                    _phase = 1f;
                    _phaseRate = 0f;
                    if (_stageElapsed >= BottomHoldSeconds)
                    {
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.Reversal;
                    }
                    break;
                case PreviewStage.Reversal:
                    _state = SquatState.REVERSAL;
                    _direction = SquatPhaseDirection.Ascent;
                    AdvancePhase(-SquatReferenceMotion.AscentRatePerSecond, RenderStepSeconds);
                    if (_stageElapsed >= ReversalSeconds)
                    {
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.AscentToSticking;
                    }
                    break;
                case PreviewStage.AscentToSticking:
                    _state = SquatState.ASCENT;
                    _direction = SquatPhaseDirection.Ascent;
                    AdvancePhase(-SquatReferenceMotion.AscentRatePerSecond, RenderStepSeconds);
                    if (_phase <= 0.6401f)
                    {
                        _phase = 0.64f;
                        _phaseRate = 0f;
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.Sticking;
                        _state = SquatState.STICKING;
                    }
                    break;
                case PreviewStage.Sticking:
                    _state = SquatState.STICKING;
                    _direction = SquatPhaseDirection.Ascent;
                    _phase = 0.64f;
                    _phaseRate = 0f;
                    if (_stageElapsed >= StickingHoldSeconds)
                    {
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.AscentFinish;
                    }
                    break;
                case PreviewStage.AscentFinish:
                    _state = SquatState.ASCENT;
                    _direction = SquatPhaseDirection.Ascent;
                    AdvancePhase(-SquatReferenceMotion.AscentRatePerSecond, RenderStepSeconds);
                    if (_phase <= 0.0001f)
                    {
                        _phase = 0f;
                        _phaseRate = 0f;
                        _stageElapsed = 0f;
                        _previewStage = PreviewStage.Standing;
                        _state = SquatState.LOCKOUT;
                        _direction = SquatPhaseDirection.None;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            _simulationTick++;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
        }

        private void StepManualTick()
        {
            if (_direction == SquatPhaseDirection.None)
                return;
            float targetRate = _direction == SquatPhaseDirection.Descent
                ? SquatReferenceMotion.DescentRatePerSecond * _manualYield
                : -SquatReferenceMotion.AscentRatePerSecond * _manualDrive;
            AdvancePhase(targetRate, RenderStepSeconds);
            _simulationTick++;
            ApplyCurrentPose(CreateIntent(_manualYield, _manualDrive));
        }

        private void AdvancePhase(float targetRate, float stepSeconds)
        {
            float limitedTarget = Mathf.Clamp(
                targetRate,
                -SquatReferenceMotion.MaxPhaseRatePerSecond,
                SquatReferenceMotion.MaxPhaseRatePerSecond);
            _phaseRate = Mathf.MoveTowards(
                _phaseRate,
                limitedTarget,
                SquatReferenceMotion.MaxPhaseAccelerationPerSecondSquared * stepSeconds);
            _phase = Mathf.Clamp01(_phase + _phaseRate * stepSeconds);
            if (_phase <= 0f || _phase >= 1f)
                _phaseRate = 0f;
        }

        private void ApplyCurrentPose(PlayerIntentFrame intent)
        {
            SquatPhaseDirection curveDirection = _direction == SquatPhaseDirection.None
                ? SquatPhaseDirection.Descent
                : _direction;
            _sample = SquatReferenceMotion.Sample(_state, _phase, curveDirection, intent);
            SquatReferencePose pose = _fixture == SquatReferenceCalibrationFixture.None
                ? _sample.Pose
                : SquatReferenceKinematics.CalibrationFixturePose(_fixture);

            ResetBindPose();
            _solution = SquatReferenceKinematics.Solve(
                _calibration,
                pose,
                _leftStandingFootAnchor,
                _rightStandingFootAnchor);
            _referencePoseValid = _solution.IsValid;
            _feetPlanted = _referencePoseValid &&
                _solution.FootAnchorsMaxErrorM <= SquatReferenceRigCalibration.FootAnchorToleranceM;

            if (!_referencePoseValid)
            {
                _depth = new SquatDepthObservation(1f, 1f, DepthMarginM);
                _leftBarHand = _rightBarHand = _upperChest.position;
                return;
            }

            _hips.SetPositionAndRotation(_solution.PelvisBonePosition, _solution.PelvisBoneRotation);
            ApplyBoneFrame(_spine, _calibration.Spine, _solution.SpineFrameRotation);
            ApplyBoneFrame(_chest, _calibration.Chest, _solution.ChestFrameRotation);
            ApplyBoneFrame(_upperChest, _calibration.UpperChest, _solution.UpperChestFrameRotation);
            ApplyBoneFrame(_neck, _calibration.Neck, _solution.NeckFrameRotation);
            ApplyBoneFrame(_head, _calibration.Head, _solution.HeadFrameRotation);

            ApplyLeg(_leftThigh, _leftShank, _leftFoot, _solution.LeftLeg);
            ApplyLeg(_rightThigh, _rightShank, _rightFoot, _solution.RightLeg);
            ApplyArmSupport(pose, _solution);
            UpdateOverlay();
        }

        private void ResetBindPose()
        {
            for (int index = 0; index < _bindPose.Count; index++)
                _bindPose[index].Restore();
        }

        private static void ApplyBoneFrame(
            Transform bone,
            SquatReferenceBoneFrame calibration,
            Quaternion frameRotation)
        {
            bone.rotation = frameRotation * calibration.BoneFromAnatomicalFrame;
        }

        private static void ApplyLeg(
            Transform thigh,
            Transform shank,
            Transform foot,
            SquatReferenceLegSolution leg)
        {
            thigh.SetPositionAndRotation(leg.HipCenter, leg.ThighBoneRotation);
            shank.SetPositionAndRotation(leg.KneeCenter, leg.ShankBoneRotation);
            foot.SetPositionAndRotation(leg.AnkleCenter, leg.FootBoneRotation);
        }

        private void ApplyArmSupport(SquatReferencePose pose, SquatReferenceKinematicSolution solution)
        {
            // Clavicles are children of UpperChest and rotate hierarchically with UpperChest.

            Vector3 thoraxForward = solution.UpperChestFrameRotation * _calibration.GameForward;
            Vector3 thoraxUp = solution.UpperChestFrameRotation * _calibration.GameUp;
            Vector3 thoraxRight = solution.UpperChestFrameRotation * _calibration.GameRight;

            Vector3 barCenter = _upperChest.position -
                thoraxForward * SquatReferenceKinematics.ArmBackOffsetM +
                thoraxUp * SquatReferenceKinematics.ArmBarHeightM;

            _leftBarHand = barCenter - thoraxRight * SquatReferenceKinematics.ArmBarHalfWidthM;
            _rightBarHand = barCenter + thoraxRight * SquatReferenceKinematics.ArmBarHalfWidthM;

            ApplyArmSide(
                _leftUpperArm,
                _leftForearm,
                _leftHand,
                _calibration.LeftUpperArm,
                _calibration.LeftForearm,
                _calibration.LeftHand,
                _leftBarHand,
                thoraxForward,
                thoraxUp,
                thoraxRight,
                isLeft: true);

            ApplyArmSide(
                _rightUpperArm,
                _rightForearm,
                _rightHand,
                _calibration.RightUpperArm,
                _calibration.RightForearm,
                _calibration.RightHand,
                _rightBarHand,
                thoraxForward,
                thoraxUp,
                thoraxRight,
                isLeft: false);
        }

        private void ApplyArmSide(
            Transform upperArm,
            Transform forearm,
            Transform hand,
            SquatReferenceBoneFrame upperArmFrame,
            SquatReferenceBoneFrame forearmFrame,
            SquatReferenceBoneFrame handFrame,
            Vector3 handTarget,
            Vector3 thoraxForward,
            Vector3 thoraxUp,
            Vector3 thoraxRight,
            bool isLeft)
        {
            Vector3 shoulder = upperArm.position;
            Vector3 forearmBindPosition = isLeft
                ? _calibration.LeftForearm.BindPosition
                : _calibration.RightForearm.BindPosition;
            float upperLength = Vector3.Distance(forearmBindPosition, upperArmFrame.BindPosition);
            float forearmLength = Vector3.Distance(handFrame.BindPosition, forearmFrame.BindPosition);

            float sideSign = isLeft ? -1f : 1f;
            Vector3 poleHint = -thoraxUp * 0.90f - thoraxForward * 0.15f + thoraxRight * (sideSign * 0.45f);

            SolveTwoBone(
                shoulder,
                handTarget,
                upperLength,
                forearmLength,
                poleHint,
                out Vector3 elbow,
                out Vector3 solvedHand);

            Vector3 upperDir = (elbow - shoulder).normalized;
            Vector3 forearmDir = (solvedHand - elbow).normalized;

            Vector3 armNormal = Vector3.Cross(upperDir, forearmDir);
            if (armNormal.sqrMagnitude < 1e-6f)
                armNormal = Vector3.Cross(upperDir, poleHint);
            if (armNormal.sqrMagnitude < 1e-6f)
                armNormal = isLeft ? -thoraxUp : thoraxUp;
            armNormal.Normalize();

            Quaternion upperRot = ConstructArmBoneRotation(upperDir, armNormal);
            Quaternion forearmRot = ConstructArmBoneRotation(forearmDir, armNormal);

            Quaternion handBindRelativeToForearm = Quaternion.Inverse(forearmFrame.BindRotation) * handFrame.BindRotation;
            Quaternion baseHandRot = forearmRot * handBindRelativeToForearm;
            Quaternion wristAdjustment = Quaternion.Euler(30f, 0f, 0f);
            Quaternion handRot = baseHandRot * wristAdjustment;

            upperArm.SetPositionAndRotation(shoulder, upperRot);
            forearm.SetPositionAndRotation(elbow, forearmRot);
            hand.SetPositionAndRotation(solvedHand, handRot);
        }

        private static Quaternion ConstructArmBoneRotation(Vector3 boneDir, Vector3 armPlaneNormal)
        {
            Vector3 boneUp = boneDir.normalized;
            Vector3 boneLeft = Vector3.ProjectOnPlane(armPlaneNormal, boneUp).normalized;
            Vector3 boneRight = -boneLeft;
            Vector3 boneForward = Vector3.Cross(boneRight, boneUp).normalized;

            Matrix4x4 matrix = Matrix4x4.identity;
            matrix.SetColumn(0, new Vector4(boneRight.x, boneRight.y, boneRight.z, 0f));
            matrix.SetColumn(1, new Vector4(boneUp.x, boneUp.y, boneUp.z, 0f));
            matrix.SetColumn(2, new Vector4(boneForward.x, boneForward.y, boneForward.z, 0f));
            return matrix.rotation;
        }

        private static void SolveTwoBone(
            Vector3 root,
            Vector3 target,
            float upperLength,
            float lowerLength,
            Vector3 poleHint,
            out Vector3 elbow,
            out Vector3 solvedTarget)
        {
            Vector3 delta = target - root;
            float distance = Mathf.Max(delta.magnitude, 0.0001f);
            Vector3 direction = delta / distance;
            float maximum = Mathf.Max(upperLength + lowerLength - 0.0005f, 0.0001f);
            float minimum = Mathf.Min(Mathf.Abs(upperLength - lowerLength) + 0.0005f, maximum);
            float clampedDistance = Mathf.Clamp(distance, minimum, maximum);
            solvedTarget = root + direction * clampedDistance;
            float along = (upperLength * upperLength - lowerLength * lowerLength +
                clampedDistance * clampedDistance) / (2f * clampedDistance);
            float heightSquared = Mathf.Max(0f, upperLength * upperLength - along * along);
            Vector3 pole = Vector3.ProjectOnPlane(poleHint, direction);
            if (pole.sqrMagnitude < 1e-8f)
                pole = Vector3.Cross(direction, Vector3.right);
            if (pole.sqrMagnitude < 1e-8f)
                pole = Vector3.Cross(direction, Vector3.forward);
            elbow = root + direction * along + pole.normalized * Mathf.Sqrt(heightSquared);
        }

        private void UpdateOverlay()
        {
            if (!_referencePoseValid)
            {
                ApplyOverlayVisibility();
                return;
            }

            Vector3 leftHip = _solution.PelvisCenter +
                _solution.PelvisFrameRotation * _calibration.LeftHipCreaseOffsetInPelvisFrame;
            Vector3 rightHip = _solution.PelvisCenter +
                _solution.PelvisFrameRotation * _calibration.RightHipCreaseOffsetInPelvisFrame;
            Vector3 leftKnee = _solution.LeftLeg.KneeCenter +
                _solution.LeftLeg.ShankFrameRotation * _calibration.LeftKneeTopOffsetInShankFrame;
            Vector3 rightKnee = _solution.RightLeg.KneeCenter +
                _solution.RightLeg.ShankFrameRotation * _calibration.RightKneeTopOffsetInShankFrame;
            _depth = SquatDepthGeometry.Evaluate(
                Point(leftHip),
                Point(rightHip),
                Point(leftKnee),
                Point(rightKnee),
                DepthMarginM);

            Vector3[] positions = { leftHip, rightHip, leftKnee, rightKnee };
            for (int index = 0; index < _landmarkMarkers.Length; index++)
                _landmarkMarkers[index].Transform.position = positions[index];
            _leftDepthLine.SetPosition(0, leftHip);
            _leftDepthLine.SetPosition(1, leftKnee);
            _rightDepthLine.SetPosition(0, rightHip);
            _rightDepthLine.SetPosition(1, rightKnee);
            Vector3 thoraxRight = _solution.UpperChestFrameRotation * _calibration.GameRight;
            Vector3 barCenter = (_leftBarHand + _rightBarHand) * 0.5f;
            _referenceBarGhost.SetPosition(0, barCenter - thoraxRight * SquatReferenceKinematics.BarGhostHalfLengthM);
            _referenceBarGhost.SetPosition(1, barCenter + thoraxRight * SquatReferenceKinematics.BarGhostHalfLengthM);
            ApplyOverlayVisibility();
        }

        private void ApplyOverlayVisibility()
        {
            bool landmarksVisible = showLandmarks && _initialized;
            for (int index = 0; index < _landmarkMarkers.Length; index++)
            {
                if (_landmarkMarkers[index].Renderer != null)
                    _landmarkMarkers[index].Renderer.enabled = landmarksVisible;
            }
            if (_leftDepthLine != null)
                _leftDepthLine.enabled = landmarksVisible;
            if (_rightDepthLine != null)
                _rightDepthLine.enabled = landmarksVisible;
            if (_referenceBarGhost != null)
                _referenceBarGhost.enabled = showReferenceBarGhost && _initialized;
        }

        private Transform RequireBone(HumanBodyBones bone)
        {
            Transform target = referenceAnimator.GetBoneTransform(bone);
            return target != null
                ? target
                : throw new InvalidOperationException(
                    $"Squat reference bone '{bone}' did not resolve on the canonical asset.");
        }

        private LandmarkMarker CreateLandmarkMarker(string markerName, Color color)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = markerName;
            marker.transform.SetParent(transform, true);
            marker.transform.localScale = Vector3.one * 0.034f;
            DestroyImmediate(marker.GetComponent<Collider>());
            Renderer renderer = marker.GetComponent<Renderer>();
            if (landmarkMaterial != null)
                renderer.sharedMaterial = landmarkMaterial;
            renderer.material.color = color;
            return new LandmarkMarker(marker.transform, renderer);
        }

        private LineRenderer CreateLine(string lineName, Color color, float width)
        {
            GameObject lineObject = new GameObject(lineName);
            lineObject.transform.SetParent(transform, true);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.positionCount = 2;
            line.startWidth = width;
            line.endWidth = width;
            line.startColor = color;
            line.endColor = color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            if (referenceBarMaterial != null)
                line.sharedMaterial = referenceBarMaterial;
            return line;
        }

        private static Vector3 ProjectToStandingPlane(Vector3 point, Vector3 up)
        {
            return point - up * Vector3.Dot(point, up);
        }

        private static string VisualProofFor(SquatReferenceCalibrationFixture fixture)
        {
            switch (fixture)
            {
                case SquatReferenceCalibrationFixture.AnkleDorsiflexionPlus10:
                    return "sagittal shank/foot relationship; plantar anchor fixed";
                case SquatReferenceCalibrationFixture.KneeFlexionPlus10:
                    return "sagittal tibia/femur relationship; plantar anchor fixed";
                case SquatReferenceCalibrationFixture.HipFlexionPlus10:
                    return "sagittal femur/pelvis relationship; bilateral hips solved";
                case SquatReferenceCalibrationFixture.TrunkFlexionPlus20:
                    return "thorax approximately +20 degrees relative pelvis";
                default:
                    return string.Empty;
            }
        }

        private SquatReferenceWaypointRecord[] ToWaypointArray()
        {
            IReadOnlyList<SquatReferenceWaypointRecord> source =
                SquatReferenceProfile.CanonicalPowerliftingSquatV1.Waypoints;
            SquatReferenceWaypointRecord[] result = new SquatReferenceWaypointRecord[source.Count];
            for (int index = 0; index < result.Length; index++)
                result[index] = source[index];
            return result;
        }

        private static SquatState StateForWaypoint(SquatReferenceWaypoint waypoint)
        {
            switch (waypoint)
            {
                case SquatReferenceWaypoint.STANDING:
                case SquatReferenceWaypoint.LOCKOUT:
                    return SquatState.LOCKOUT;
                case SquatReferenceWaypoint.LEGAL_BOTTOM:
                    return SquatState.BOTTOM;
                case SquatReferenceWaypoint.EARLY_ASCENT:
                    return SquatState.ASCENT;
                case SquatReferenceWaypoint.STICKING:
                    return SquatState.STICKING;
                default:
                    return SquatState.DESCENT;
            }
        }

        private static SquatReferenceWaypoint ResolveWaypoint(
            float phase,
            SquatState state,
            SquatPhaseDirection direction)
        {
            if (phase <= 0.0001f || state == SquatState.LOCKOUT)
                return SquatReferenceWaypoint.LOCKOUT;
            if (phase >= 0.9999f || state == SquatState.BOTTOM || state == SquatState.REVERSAL)
                return SquatReferenceWaypoint.LEGAL_BOTTOM;
            if (state == SquatState.STICKING || Mathf.Abs(phase - 0.64f) < 0.025f)
                return SquatReferenceWaypoint.STICKING;
            if (direction == SquatPhaseDirection.Ascent && phase <= 0.85f)
                return SquatReferenceWaypoint.EARLY_ASCENT;
            if (phase <= 0.34f)
                return SquatReferenceWaypoint.QUARTER_DESCENT;
            return SquatReferenceWaypoint.NEAR_PARALLEL;
        }

        private static SquatPoint3 Point(Vector3 value) =>
            new SquatPoint3(value.x, value.y, value.z);

        private static PlayerIntentFrame CreateIntent(float yield, float drive) =>
            new PlayerIntentFrame(
                0ul,
                0d,
                IntentEdgeFlags.None,
                0f,
                yield,
                drive,
                0f,
                0f,
                false,
                yield > 0f,
                drive > 0f,
                false,
                false,
                false,
                false);

        private static void WriteJson(string path, object artifact)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(fullPath, JsonUtility.ToJson(artifact, true) + Environment.NewLine);
        }

        private enum PreviewStage : byte
        {
            Standing,
            Descent,
            Bottom,
            Reversal,
            AscentToSticking,
            Sticking,
            AscentFinish
        }

        private readonly struct LandmarkMarker
        {
            public LandmarkMarker(Transform transform, Renderer renderer)
            {
                Transform = transform;
                Renderer = renderer;
            }

            public Transform Transform { get; }
            public Renderer Renderer { get; }
        }

        private readonly struct PoseBind
        {
            public PoseBind(Transform transform)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }

            public Transform Transform { get; }
            public Vector3 LocalPosition { get; }
            public Quaternion LocalRotation { get; }
            public Vector3 LocalScale { get; }

            public void Restore()
            {
                Transform.localPosition = LocalPosition;
                Transform.localRotation = LocalRotation;
                Transform.localScale = LocalScale;
            }
        }

        [Serializable]
        private sealed class CalibrationArtifact
        {
            public string schema;
            public string mission;
            public string profile;
            public string calibrationId;
            public AssetRecord asset;
            public string poseRootSource;
            public string rootBoundsAuthority;
            public FrameRecord gameFrame;
            public FrameRecord plantedFootFrame;
            public FrameRecord pelvisFrame;
            public FootRecord leftFoot;
            public FootRecord rightFoot;
            public JointCentersRecord jointCenters;
            public SegmentLengthsRecord segmentLengths;
            public BoneFrameRecord[] calibratedFrames;
            public FixtureRecord[] fixtureResults;
            public string[] visualProof;
        }

        [Serializable]
        private sealed class MeasurementArtifact
        {
            public string schema;
            public string mission;
            public string unityVersion;
            public string profile;
            public string claimClass;
            public string referenceOwner;
            public bool physicalAuthorityTouched;
            public AssetRecord asset;
            public string[] states;
            public WaypointMeasurement[] waypoints;
            public string phaseConvention;
            public string calibrationId;
            public string poseRootSource;
            public string rootBoundsAuthority;
            public FixtureRecord[] calibrationFixtures;
            public CoordinateRecord coordinateConventions;
            public TrunkMappingRecord trunkMapping;
            public SolverRecord solver;
            public DepthRecord depth;
            public QualityGateRecord qualityGates;
            public string[] knownLimitations;
        }

        [Serializable]
        private sealed class AssetRecord
        {
            public string provider;
            public string pack;
            public string modelPath;
            public string modelSha256;
            public string avatar;

            public static AssetRecord Create(string avatar) => new AssetRecord
            {
                provider = "Quaternius",
                pack = "Universal Base Characters[Standard]",
                modelPath = AssetPath,
                modelSha256 = AssetSha256,
                avatar = avatar
            };
        }

        [Serializable]
        private sealed class WaypointMeasurement
        {
            public string waypoint;
            public float phase;
            public string direction;
            public PoseRecord pose;
            public float leftDepthM;
            public float rightDepthM;
            public bool bilateralLegalReference;
            public bool feetPlanted;
            public float footAnchorsMaxErrorMM;
            public float bilateralHipSolutionErrorMM;
            public float segmentLengthErrorMM;
            public float trunkRelativeAngleErrorDeg;
            public float kneeForwardFromAnkleM;
            public float pelvisHeightM;
            public float pelvisSetbackM;
            public float shankWorldAngleDeg;
            public float thighWorldAngleDeg;
            public float thoraxRelativePelvisDeg;
            public float barGhostApRelativeMidfootM;
            public float leftPlantarAnchorErrorMm;
            public float rightPlantarAnchorErrorMm;
            public float leftFootPitchErrorDeg;
            public float rightFootPitchErrorDeg;
            public float leftHandBarErrorM;
            public float rightHandBarErrorM;

            public static WaypointMeasurement From(
                SquatReferenceWaypointRecord waypoint,
                SquatPhaseDirection direction,
                SquatDepthObservation depth,
                SquatReferenceKinematicSolution solution,
                SquatReferenceRigCalibration calibration,
                Vector3 leftHandPos,
                Vector3 rightHandPos,
                Vector3 leftBarHand,
                Vector3 rightBarHand)
            {
                Vector3 midfoot = (calibration.LeftFoot.PlantarAnchorWorld + calibration.LeftFoot.BindAnkleCenter) * 0.5f;
                Vector3 barCenter = (leftBarHand + rightBarHand) * 0.5f;
                float kneeFwd = Vector3.Dot(solution.LeftLeg.KneeCenter - solution.LeftLeg.AnkleCenter, calibration.GameForward);
                float setback = -Vector3.Dot(solution.PelvisCenter - solution.LeftLeg.AnkleCenter, calibration.GameForward);
                float pelvisH = Vector3.Dot(solution.PelvisCenter, calibration.GameUp);
                float shankAngle = SquatReferenceKinematics.SignedAngleAroundAxis(calibration.GameUp, solution.LeftLeg.ShankFrameRotation * Vector3.up, calibration.GameRight);
                float thighAngle = SquatReferenceKinematics.SignedAngleAroundAxis(calibration.GameUp, solution.LeftLeg.ThighFrameRotation * Vector3.up, calibration.GameRight);
                float thoraxRel = SquatReferenceKinematics.MeasureTrunkRelativeDegrees(calibration, solution);
                float barApMidfoot = Vector3.Dot(barCenter - midfoot, calibration.GameForward);

                return new WaypointMeasurement
                {
                    waypoint = waypoint.Waypoint.ToString(),
                    phase = waypoint.Phase,
                    direction = direction.ToString(),
                    pose = PoseRecord.From(waypoint.Pose),
                    leftDepthM = depth.LeftDepthM,
                    rightDepthM = depth.RightDepthM,
                    bilateralLegalReference = depth.BilateralLegalReference,
                    feetPlanted = solution.FootAnchorsMaxErrorM <=
                        SquatReferenceRigCalibration.FootAnchorToleranceM,
                    footAnchorsMaxErrorMM = solution.FootAnchorsMaxErrorM * 1000f,
                    bilateralHipSolutionErrorMM = solution.BilateralHipSolutionErrorM * 1000f,
                    segmentLengthErrorMM = solution.SegmentLengthErrorM * 1000f,
                    trunkRelativeAngleErrorDeg = solution.TrunkRelativeAngleErrorDeg,
                    kneeForwardFromAnkleM = kneeFwd,
                    pelvisHeightM = pelvisH,
                    pelvisSetbackM = setback,
                    shankWorldAngleDeg = shankAngle,
                    thighWorldAngleDeg = thighAngle,
                    thoraxRelativePelvisDeg = thoraxRel,
                    barGhostApRelativeMidfootM = barApMidfoot,
                    leftPlantarAnchorErrorMm = solution.LeftLeg.FootAnchorErrorM * 1000f,
                    rightPlantarAnchorErrorMm = solution.RightLeg.FootAnchorErrorM * 1000f,
                    leftFootPitchErrorDeg = 0f,
                    rightFootPitchErrorDeg = 0f,
                    leftHandBarErrorM = Vector3.Distance(leftHandPos, leftBarHand),
                    rightHandBarErrorM = Vector3.Distance(rightHandPos, rightBarHand)
                };
            }
        }

        [Serializable]
        private struct PoseRecord
        {
            public float ankleDorsiflexionRad;
            public float kneeFlexionRad;
            public float hipFlexionRad;
            public float trunkFlexionRad;
            public float shoulderFlexionRad;
            public float elbowFlexionRad;
            public float wristExtensionRad;

            public static PoseRecord From(SquatReferencePose pose) => new PoseRecord
            {
                ankleDorsiflexionRad = pose.AnkleDorsiflexionRad,
                kneeFlexionRad = pose.KneeFlexionRad,
                hipFlexionRad = pose.HipFlexionRad,
                trunkFlexionRad = pose.TrunkFlexionRad,
                shoulderFlexionRad = pose.ShoulderFlexionRad,
                elbowFlexionRad = pose.ElbowFlexionRad,
                wristExtensionRad = pose.WristExtensionRad
            };
        }

        [Serializable]
        private struct FrameRecord
        {
            public string name;
            public VectorRecord originM;
            public VectorRecord right;
            public VectorRecord up;
            public VectorRecord forward;

            public static FrameRecord From(string name, SquatReferenceFrame frame) => new FrameRecord
            {
                name = name,
                originM = VectorRecord.From(frame.Origin),
                right = VectorRecord.From(frame.Right),
                up = VectorRecord.From(frame.Up),
                forward = VectorRecord.From(frame.Forward)
            };
        }

        [Serializable]
        private struct BoneFrameRecord
        {
            public string bone;
            public FrameRecord frame;
            public QuaternionRecord boneFromFrame;

            public static BoneFrameRecord From(SquatReferenceBoneFrame source) => new BoneFrameRecord
            {
                bone = source.Bone.ToString(),
                frame = FrameRecord.From(source.Bone.ToString(), source.AnatomicalFrameBind),
                boneFromFrame = QuaternionRecord.From(source.BoneFromAnatomicalFrame)
            };
        }

        [Serializable]
        private struct FootRecord
        {
            public string side;
            public string footBone;
            public string toeBone;
            public FrameRecord plantedFrame;
            public VectorRecord plantarAnchorWorldM;
            public VectorRecord plantarAnchorInBoneLocalM;
            public VectorRecord ankleCenterWorldM;

            public static FootRecord From(string side, SquatReferenceFootCalibration source) => new FootRecord
            {
                side = side,
                footBone = source.FootBone.name,
                toeBone = source.ToeBone.name,
                plantedFrame = FrameRecord.From("planted_" + side, source.AnatomicalFrameBind),
                plantarAnchorWorldM = VectorRecord.From(source.PlantarAnchorWorld),
                plantarAnchorInBoneLocalM = VectorRecord.From(source.PlantarAnchorInBoneLocal),
                ankleCenterWorldM = VectorRecord.From(source.BindAnkleCenter)
            };
        }

        [Serializable]
        private struct JointCentersRecord
        {
            public VectorRecord leftHipM;
            public VectorRecord rightHipM;
            public VectorRecord leftKneeM;
            public VectorRecord rightKneeM;
            public VectorRecord leftAnkleM;
            public VectorRecord rightAnkleM;
            public VectorRecord pelvisM;
            public VectorRecord thoraxM;

            public static JointCentersRecord From(SquatReferenceRigCalibration source) => new JointCentersRecord
            {
                leftHipM = VectorRecord.From(source.LeftHipCenterBindWorld),
                rightHipM = VectorRecord.From(source.RightHipCenterBindWorld),
                leftKneeM = VectorRecord.From(source.LeftKneeCenterBindWorld),
                rightKneeM = VectorRecord.From(source.RightKneeCenterBindWorld),
                leftAnkleM = VectorRecord.From(source.LeftAnkleCenterBindWorld),
                rightAnkleM = VectorRecord.From(source.RightAnkleCenterBindWorld),
                pelvisM = VectorRecord.From(source.HipCenterBindWorld),
                thoraxM = VectorRecord.From(source.ThoraxCenterBindWorld)
            };
        }

        [Serializable]
        private struct SegmentLengthsRecord
        {
            public float leftThighM;
            public float rightThighM;
            public float leftShankM;
            public float rightShankM;
            public float hipWidthM;

            public static SegmentLengthsRecord From(SquatReferenceRigCalibration source) => new SegmentLengthsRecord
            {
                leftThighM = source.LeftThighLengthM,
                rightThighM = source.RightThighLengthM,
                leftShankM = source.LeftShankLengthM,
                rightShankM = source.RightShankLengthM,
                hipWidthM = source.HipWidthM
            };
        }

        [Serializable]
        private struct FixtureRecord
        {
            public string fixture;
            public float expectedDegrees;
            public float measuredDegrees;
            public float errorDegrees;
            public bool passed;
            public string visualProof;

            public static FixtureRecord[] From(SquatReferenceCalibrationReport report)
            {
                FixtureRecord[] records = new FixtureRecord[report.Results.Count];
                for (int index = 0; index < records.Length; index++)
                {
                    SquatReferenceCalibrationFixtureResult result = report.Results[index];
                    records[index] = new FixtureRecord
                    {
                        fixture = result.Fixture.ToString(),
                        expectedDegrees = result.ExpectedDegrees,
                        measuredDegrees = result.MeasuredDegrees,
                        errorDegrees = result.ErrorDegrees,
                        passed = result.Passed,
                        visualProof = result.VisualProof
                    };
                }
                return records;
            }
        }

        [Serializable]
        private struct TrunkMappingRecord
        {
            public bool thoraxRelativeToPelvis;
            public float spineWeight;
            public float chestWeight;
            public float upperChestWeight;
            public float totalWeight;
            public float cervicalCompensationFraction;
            public float headCompensationFraction;
        }

        [Serializable]
        private struct SolverRecord
        {
            public string mode;
            public VectorRecord fixedLeftPlantarAnchor;
            public VectorRecord fixedRightPlantarAnchor;
            public float bilateralToleranceM;
            public float footAnchorToleranceM;
            public float segmentLengthToleranceM;
            public bool usesRendererBounds;
            public bool usesIkFramework;
        }

        [Serializable]
        private struct DepthRecord
        {
            public string sourceClass;
            public string criterion;
            public float marginM;
            public float leftBottomDepthM;
            public float rightBottomDepthM;
            public bool feetPlanted;
            public bool bilateralSolutionValid;
            public bool legalDepthWithPlantedFeet;
        }

        [Serializable]
        private struct QualityGateRecord
        {
            public bool JOINT_AXIS_CALIBRATION;
            public float FOOT_ANCHORS_MAX_ERROR_MM;
            public float BILATERAL_HIP_SOLUTION_MAX_ERROR_MM;
            public float SEGMENT_LENGTH_ERROR;
            public float SEGMENT_LENGTH_ERROR_MM;
            public float TRUNK_RELATIVE_ANGLE_ERROR_DEG;
            public string ROOT_BOUNDS_AUTHORITY;
            public bool LEGAL_DEPTH_WITH_PLANTED_FEET;
            public bool RENDER_RATE_INDEPENDENCE;
            public bool BAR_SUPPORT_HAND_TARGETS;
            public float BAR_GHOST_AP_MIDFOOT_M;
            public float LEFT_HAND_BAR_ERROR_M;
            public float RIGHT_HAND_BAR_ERROR_M;
        }

        [Serializable]
        private struct CoordinateRecord
        {
            public string worldFrame;
            public string referenceFrame;
            public string up;
            public string forward;
            public string right;
            public string internalAngles;
            public string unityConversion;
        }

        [Serializable]
        private struct VectorRecord
        {
            public float x;
            public float y;
            public float z;

            public static VectorRecord From(Vector3 value) => new VectorRecord
            {
                x = value.x,
                y = value.y,
                z = value.z
            };
        }

        [Serializable]
        private struct QuaternionRecord
        {
            public float x;
            public float y;
            public float z;
            public float w;

            public static QuaternionRecord From(Quaternion value) => new QuaternionRecord
            {
                x = value.x,
                y = value.y,
                z = value.z,
                w = value.w
            };
        }
    }
}
