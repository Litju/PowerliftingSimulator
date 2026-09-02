using System;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class SquatReferencePreview : MonoBehaviour
    {
        public const string ProfileId = "CANONICAL_POWERLIFTING_SQUAT_V1";
        public const string ReferenceOwner = "SquatReferencePreview / dedicated preview hierarchy only";
        public const string AssetPath = "Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx";
        public const string AssetSha256 = "79344418d754a59730b79d1874752e9592143db34abe8adf138fa9a92a4768e9";

        private const float DepthMarginM = SquatDepthGeometry.DefaultDepthMarginM;
        private const float RenderStepSeconds = 0.01f;
        private const float StandingHoldSeconds = 0.45f;
        private const float BottomHoldSeconds = 0.18f;
        private const float ReversalSeconds = 0.12f;
        private const float StickingHoldSeconds = 0.16f;
        private const float RootFloorToleranceM = 0.0001f;

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
        private readonly BoneBinding[] _allBindings = new BoneBinding[19];
        private Transform _hips;
        private Transform _leftCalf;
        private Transform _rightCalf;
        private Transform _upperChest;
        private Vector3 _canonicalRight;
        private Vector3 _canonicalForward;
        private Vector3 _canonicalUp;
        private Vector3 _baseRootLocalPosition;
        private Vector3 _leftHipCreaseOffset;
        private Vector3 _rightHipCreaseOffset;
        private Vector3 _leftKneeTopOffset;
        private Vector3 _rightKneeTopOffset;
        private Renderer[] _renderers = Array.Empty<Renderer>();
        private LineRenderer _leftDepthLine;
        private LineRenderer _rightDepthLine;
        private LineRenderer _referenceBarGhost;
        private SquatDepthObservation _depth;
        private SquatReferenceSample _sample;
        private SquatPhaseDirection _direction;
        private SquatState _state;
        private PreviewStage _previewStage;
        private float _phase;
        private float _phaseRate;
        private float _stageElapsed;
        private float _renderAccumulator;
        private ulong _simulationTick;
        private float _manualYield;
        private float _manualDrive;
        private bool _initialized;

        public string Profile => ProfileId;
        public string ClaimClass => SquatReferenceProfile.CanonicalPowerliftingSquatV1.ClaimClass;
        public string Ownership => ReferenceOwner;
        public Animator ReferenceAnimator => referenceAnimator;
        public AthleteRigOwnership RigOwnership => ownership;
        public SquatState State => _state;
        public SquatPhaseDirection Direction => _direction;
        public float Phase => _phase;
        public float PhaseRate => _phaseRate;
        public ulong SimulationTick => _simulationTick;
        public SquatDepthObservation CurrentDepth => _depth;
        public SquatReferenceSample CurrentSample => _sample;
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
            _baseRootLocalPosition = referenceRoot.localPosition;
            _renderers = referenceAnimator.GetComponentsInChildren<Renderer>(true);
            CacheBindingsAndLandmarks();
            CreateOverlay();
            _state = SquatState.LOCKOUT;
            _direction = SquatPhaseDirection.None;
            _phase = 0f;
            _phaseRate = 0f;
            _previewStage = PreviewStage.Standing;
            _initialized = true;
            ApplyCurrentPose(PlayerIntentFrame.Empty);
        }

        private void Update()
        {
            if (!_initialized)
                return;

            _renderAccumulator += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            int ticks = 0;
            while (_renderAccumulator >= RenderStepSeconds && ticks < SimulationConstants.MaxCatchUpTicksPerRenderFrame)
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
            if (!_initialized)
                return;

            RepositionPreviewRootToFloor();
            UpdateOverlay();
        }

        private void OnGUI()
        {
            if (!_initialized)
                return;

            GUILayout.BeginArea(new Rect(18f, 18f, 430f, 286f), GUI.skin.box);
            GUILayout.Label("GAM-10 Squat Reference Motion");
            GUILayout.Label("REFERENCE ONLY - unloaded visual calibration; no physical execution");
            GUILayout.Label($"State: {_state}   Waypoint: {CurrentWaypoint}");
            GUILayout.Label($"s_q: {_phase:F3}   Direction: {_direction}   Rate: {_phaseRate:F3}/s");
            GUILayout.Label($"Depth L/R: {_depth.LeftDepthM:F3} / {_depth.RightDepthM:F3} m   " +
                (_depth.BilateralLegalReference ? "LEGAL BILATERAL" : "NOT LEGAL"));

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

            GUILayout.Label($"Balance intent X: {_sample.BalanceIntentX:F2}   correction applied: {_sample.BalanceCorrectionApplied}");
            GUILayout.Label("Brace affects the reference posture profile only; Yield/Drive select phase direction.");
            GUILayout.EndArea();
        }

        public void SetAutoMode(bool enabled)
        {
            autoMode = enabled;
            paused = false;
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
                _direction = _direction == SquatPhaseDirection.None ? SquatPhaseDirection.Descent : _direction;
                _state = _direction == SquatPhaseDirection.Descent ? SquatState.DESCENT : SquatState.ASCENT;
            }
            ApplyCurrentPose(CreateIntent(_manualYield, _manualDrive));
        }

        public void SetReviewPose(
            float phase,
            SquatPhaseDirection direction,
            SquatState state)
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");
            autoMode = false;
            paused = true;
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
            if (_direction == SquatPhaseDirection.None)
                _direction = SquatPhaseDirection.Descent;
            _state = _direction == SquatPhaseDirection.Descent ? SquatState.DESCENT : SquatState.ASCENT;
            float targetRate = _direction == SquatPhaseDirection.Descent
                ? SquatReferenceMotion.DescentRatePerSecond * Mathf.Max(intent.Yield01, intent.YieldHeld ? 1f : 0f)
                : -SquatReferenceMotion.AscentRatePerSecond * Mathf.Max(intent.Drive01, intent.DriveHeld ? 1f : 0f);
            AdvancePhase(targetRate, RenderStepSeconds);
            _simulationTick++;
            ApplyCurrentPose(intent);
        }

        public void WriteMeasurementArtifact(string path)
        {
            if (!_initialized)
                throw new InvalidOperationException("The squat reference preview is not initialized.");
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("A measurement path is required.", nameof(path));

            float savedPhase = _phase;
            float savedPhaseRate = _phaseRate;
            SquatState savedState = _state;
            SquatPhaseDirection savedDirection = _direction;
            bool savedAuto = autoMode;
            bool savedPaused = paused;
            WaypointMeasurement[] waypointMeasurements = new WaypointMeasurement[7];
            SquatReferenceWaypointRecord[] waypoints = ToWaypointArray();
            for (int index = 0; index < waypoints.Length; index++)
            {
                SquatReferenceWaypointRecord waypoint = waypoints[index];
                SquatPhaseDirection direction = waypoint.Waypoint == SquatReferenceWaypoint.EARLY_ASCENT ||
                    waypoint.Waypoint == SquatReferenceWaypoint.STICKING
                    ? SquatPhaseDirection.Ascent
                    : SquatPhaseDirection.Descent;
                SquatState state = StateForWaypoint(waypoint.Waypoint);
                _phase = waypoint.Phase;
                _phaseRate = 0f;
                _direction = direction;
                _state = state;
                ApplyCurrentPose(PlayerIntentFrame.Empty);
                waypointMeasurements[index] = new WaypointMeasurement
                {
                    waypoint = waypoint.Waypoint.ToString(),
                    phase = waypoint.Phase,
                    direction = direction.ToString(),
                    pose = PoseRecord.From(waypoint.Pose),
                    leftDepthM = _depth.LeftDepthM,
                    rightDepthM = _depth.RightDepthM,
                    bilateralLegalReference = _depth.BilateralLegalReference
                };
            }

            _phase = savedPhase;
            _phaseRate = savedPhaseRate;
            _state = savedState;
            _direction = savedDirection;
            autoMode = savedAuto;
            paused = savedPaused;
            ApplyCurrentPose(PlayerIntentFrame.Empty);

            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            MeasurementArtifact artifact = new MeasurementArtifact
            {
                schema = "GAM10_SQUAT_REFERENCE_V1",
                mission = "POWERLIFTING_SIMULATOR_GAM_10_SQUAT_DOMAIN_AND_REFERENCE_MOTION",
                 unityVersion = Application.unityVersion,
                 profile = ProfileId,
                 claimClass = ClaimClass,
                 referenceOwner = ReferenceOwner,
                 physicalAuthorityTouched = false,
                 asset = new AssetRecord
                {
                    provider = "Quaternius",
                    pack = "Universal Base Characters[Standard]",
                    modelPath = AssetPath,
                    modelSha256 = AssetSha256,
                    avatar = referenceAnimator.avatar.name
                },
                states = Enum.GetNames(typeof(SquatState)),
                waypoints = waypointMeasurements,
                phaseConvention = SquatReferenceMotion.PhaseConvention,
                landmarkCalibration = new[]
                {
                    LandmarkCalibrationRecord.Create("leftHipCreaseProxy", "Hips / R_i local", _leftHipCreaseOffset, _hips),
                    LandmarkCalibrationRecord.Create("rightHipCreaseProxy", "Hips / R_i local", _rightHipCreaseOffset, _hips),
                    LandmarkCalibrationRecord.Create("leftKneeTopProxy", "LeftLowerLeg / R_i local", _leftKneeTopOffset, _leftCalf),
                    LandmarkCalibrationRecord.Create("rightKneeTopProxy", "RightLowerLeg / R_i local", _rightKneeTopOffset, _rightCalf)
                },
                referenceTiming = new TimingRecord
                {
                    fixedStepS = RenderStepSeconds,
                    descentRateMinPerS = 0f,
                    descentRateMaxPerS = SquatReferenceMotion.DescentRatePerSecond,
                    ascentRateMinPerS = -SquatReferenceMotion.AscentRatePerSecond,
                    ascentRateMaxPerS = 0f,
                    maxPhaseRatePerS = SquatReferenceMotion.MaxPhaseRatePerSecond,
                    maxPhaseAccelerationPerSS = SquatReferenceMotion.MaxPhaseAccelerationPerSecondSquared,
                    timingClaim = "GAME_CALIBRATION; visually credible unloaded/reference timing, not human normative timing"
                },
                 continuity = new ContinuityRecord
                 {
                    maxKeyPoseValueDiscontinuity = 0f,
                    reversalPoseDiscontinuity = profile.ReversalPoseDiscontinuity,
                    lockoutPoseDiscontinuity = profile.LockoutPoseDiscontinuity,
                     curve = "Piecewise cubic Hermite; C0 at shared keys; C1 within each segment; reversal held at phase-rate zero"
                 },
                 referenceRootCorrection = new RootCorrectionRecord
                 {
                     owner = ReferenceOwner,
                     mode = "preview-only deterministic vertical floor clearance",
                     source = "reference renderer bounds min.y; referenceRoot only",
                     deterministic = true,
                     physicalHierarchyWrites = 0
                 },
                 coordinateConventions = new CoordinateRecord
                {
                    worldFrame = "W",
                    referenceFrame = "R_i",
                    up = "+Y",
                    forward = "+Z",
                    right = "+X",
                    internalAngles = "radians",
                    unityConversion = "one adapter applies calibrated bone-local Quaternion.AngleAxis corrections"
                },
                ruleSource = new RuleSourceRecord
                {
                    title = "IPF Technical Rule Book",
                    effectiveDate = "01 March 2026",
                    version = "3",
                    sourceClass = SquatDepthGeometry.SourceClass,
                    depthCriterion = "max(leftDepthM,rightDepthM) <= -depthMarginM",
                    depthMarginM = DepthMarginM
                },
                knownLimitations = new[]
                {
                    "Reference motion is biomechanically informed game calibration, not motion-capture ground truth.",
                    "It is not optimal, subject-specific, clinical, inverse-dynamics, or muscle-force analysis.",
                    "Landmarks are stable bone-attached rule proxies, not measurement-grade hip-crease anatomy.",
                    "This preview does not execute a physical squat, balance controller, or bar-on-back coupling.",
                    "STICKING is an authored ascent waypoint; loaded runtime sticking detection belongs to a later unit."
                }
            };

            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory());
            File.WriteAllText(fullPath, JsonUtility.ToJson(artifact, true) + Environment.NewLine);
        }

        private void CacheBindingsAndLandmarks()
        {
            _hips = RequireBone(HumanBodyBones.Hips);
            _leftCalf = RequireBone(HumanBodyBones.LeftLowerLeg);
            _rightCalf = RequireBone(HumanBodyBones.RightLowerLeg);
            _upperChest = RequireBone(HumanBodyBones.UpperChest);
            _canonicalUp = referenceRoot.up.normalized;
            _canonicalRight = referenceRoot.TransformDirection(Vector3.left).normalized;
            _canonicalForward = referenceRoot.TransformDirection(Vector3.back).normalized;

            int index = 0;
            _allBindings[index++] = Bind(HumanBodyBones.Spine, _canonicalRight, 0.35f);
            _allBindings[index++] = Bind(HumanBodyBones.Chest, _canonicalRight, 0.65f);
            _allBindings[index++] = Bind(HumanBodyBones.UpperChest, _canonicalRight, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.Neck, _canonicalRight, -0.45f);
            _allBindings[index++] = Bind(HumanBodyBones.Head, _canonicalRight, -0.45f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftUpperLeg, _canonicalRight, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightUpperLeg, _canonicalRight, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftLowerLeg, _canonicalRight, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightLowerLeg, _canonicalRight, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftFoot, _canonicalRight, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightFoot, _canonicalRight, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftUpperArm, _canonicalForward, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightUpperArm, _canonicalForward, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftLowerArm, _canonicalForward, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightLowerArm, _canonicalForward, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftHand, _canonicalForward, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightHand, _canonicalForward, -1f);
            _allBindings[index++] = Bind(HumanBodyBones.LeftShoulder, _canonicalForward, 1f);
            _allBindings[index++] = Bind(HumanBodyBones.RightShoulder, _canonicalForward, -1f);

            Vector3 hipLeftWorldOffset = _canonicalUp * -0.055f + _canonicalForward * 0.060f + _canonicalRight * -0.055f;
            Vector3 hipRightWorldOffset = _canonicalUp * -0.055f + _canonicalForward * 0.060f + _canonicalRight * 0.055f;
            Vector3 kneeWorldOffset = _canonicalUp * 0.045f + _canonicalForward * 0.035f;
            _leftHipCreaseOffset = _hips.InverseTransformVector(hipLeftWorldOffset);
            _rightHipCreaseOffset = _hips.InverseTransformVector(hipRightWorldOffset);
            _leftKneeTopOffset = _leftCalf.InverseTransformVector(kneeWorldOffset);
            _rightKneeTopOffset = _rightCalf.InverseTransformVector(kneeWorldOffset);
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
            ResetBindings();
            ApplyBinding(0, _sample.Pose.TrunkFlexionRad);
            ApplyBinding(1, _sample.Pose.TrunkFlexionRad);
            ApplyBinding(2, _sample.Pose.TrunkFlexionRad);
            ApplyBinding(3, _sample.Pose.TrunkFlexionRad);
            ApplyBinding(4, _sample.Pose.TrunkFlexionRad);
            ApplyBinding(5, _sample.Pose.HipFlexionRad);
            ApplyBinding(6, _sample.Pose.HipFlexionRad);
            ApplyBinding(7, _sample.Pose.KneeFlexionRad);
            ApplyBinding(8, _sample.Pose.KneeFlexionRad);
            ApplyBinding(9, _sample.Pose.AnkleDorsiflexionRad);
            ApplyBinding(10, _sample.Pose.AnkleDorsiflexionRad);
            ApplyBinding(11, _sample.Pose.ShoulderFlexionRad);
            ApplyBinding(12, _sample.Pose.ShoulderFlexionRad);
            ApplyBinding(13, _sample.Pose.ElbowFlexionRad);
            ApplyBinding(14, _sample.Pose.ElbowFlexionRad);
            ApplyBinding(15, _sample.Pose.WristExtensionRad);
            ApplyBinding(16, _sample.Pose.WristExtensionRad);
            ApplyBinding(17, _sample.Pose.ShoulderFlexionRad * 0.35f);
            ApplyBinding(18, _sample.Pose.ShoulderFlexionRad * 0.35f);
            RepositionPreviewRootToFloor();
            UpdateOverlay();
        }

        private void ResetBindings()
        {
            for (int index = 0; index < _allBindings.Length; index++)
                _allBindings[index].Bone.localRotation = _allBindings[index].BindLocalRotation;
        }

        private void ApplyBinding(int index, float scalarRadians)
        {
            BoneBinding binding = _allBindings[index];
            float degrees = UnitContract.RadiansToDegrees(scalarRadians * binding.Scale);
            binding.Bone.localRotation = Quaternion.AngleAxis(degrees, binding.AxisInParentLocal) * binding.BindLocalRotation;
        }

        private void RepositionPreviewRootToFloor()
        {
            Bounds bounds = CalculateBounds();
            float correction = bounds.min.y;
            if (Mathf.Abs(correction) > RootFloorToleranceM)
                referenceRoot.localPosition = _baseRootLocalPosition + Vector3.up * -correction;
            else
                referenceRoot.localPosition = _baseRootLocalPosition;
        }

        private void UpdateOverlay()
        {
            SquatPoint3 leftHip = Point(_hips.TransformPoint(_leftHipCreaseOffset));
            SquatPoint3 rightHip = Point(_hips.TransformPoint(_rightHipCreaseOffset));
            SquatPoint3 leftKnee = Point(_leftCalf.TransformPoint(_leftKneeTopOffset));
            SquatPoint3 rightKnee = Point(_rightCalf.TransformPoint(_rightKneeTopOffset));
            _depth = SquatDepthGeometry.Evaluate(leftHip, rightHip, leftKnee, rightKnee, DepthMarginM);

            Vector3[] positions =
            {
                ToUnity(leftHip), ToUnity(rightHip), ToUnity(leftKnee), ToUnity(rightKnee)
            };
            for (int index = 0; index < _landmarkMarkers.Length; index++)
                _landmarkMarkers[index].Transform.position = positions[index];
            _leftDepthLine.SetPosition(0, positions[0]);
            _leftDepthLine.SetPosition(1, positions[2]);
            _rightDepthLine.SetPosition(0, positions[1]);
            _rightDepthLine.SetPosition(1, positions[3]);

            Vector3 barCenter = _upperChest.position + _canonicalForward * 0.005f + _canonicalUp * 0.095f;
            _referenceBarGhost.SetPosition(0, barCenter - _canonicalRight * 0.72f);
            _referenceBarGhost.SetPosition(1, barCenter + _canonicalRight * 0.72f);
            ApplyOverlayVisibility();
        }

        private void ApplyOverlayVisibility()
        {
            bool landmarksVisible = showLandmarks && _initialized;
            for (int index = 0; index < _landmarkMarkers.Length; index++)
                _landmarkMarkers[index].Renderer.enabled = landmarksVisible;
            _leftDepthLine.enabled = landmarksVisible;
            _rightDepthLine.enabled = landmarksVisible;
            _referenceBarGhost.enabled = showReferenceBarGhost && _initialized;
        }

        private BoneBinding Bind(HumanBodyBones bone, Vector3 worldAxis, float scale)
        {
            Transform target = RequireBone(bone);
            Transform parent = target.parent ?? referenceRoot;
            Vector3 parentAxis = parent.InverseTransformDirection(worldAxis).normalized;
            return new BoneBinding(target, parentAxis, target.localRotation, scale);
        }

        private Transform RequireBone(HumanBodyBones bone)
        {
            Transform target = referenceAnimator.GetBoneTransform(bone);
            return target != null
                ? target
                : throw new InvalidOperationException($"Squat reference bone '{bone}' did not resolve on the canonical asset.");
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

        private Bounds CalculateBounds()
        {
            if (_renderers.Length == 0)
                return new Bounds(referenceRoot.position, Vector3.zero);
            Bounds bounds = _renderers[0].bounds;
            for (int index = 1; index < _renderers.Length; index++)
                bounds.Encapsulate(_renderers[index].bounds);
            return bounds;
        }

        private SquatReferenceWaypointRecord[] ToWaypointArray()
        {
            var result = new SquatReferenceWaypointRecord[SquatReferenceProfile.CanonicalPowerliftingSquatV1.Waypoints.Count];
            for (int index = 0; index < result.Length; index++)
                result[index] = SquatReferenceProfile.CanonicalPowerliftingSquatV1.Waypoints[index];
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

        private static SquatReferenceWaypoint ResolveWaypoint(float phase, SquatState state, SquatPhaseDirection direction)
        {
            if (phase <= 0.0001f || state == SquatState.LOCKOUT)
                return SquatReferenceWaypoint.LOCKOUT;
            if (phase >= 0.9999f || state == SquatState.BOTTOM)
                return SquatReferenceWaypoint.LEGAL_BOTTOM;
            if (state == SquatState.STICKING || Mathf.Abs(phase - 0.64f) < 0.025f)
                return SquatReferenceWaypoint.STICKING;
            if (direction == SquatPhaseDirection.Ascent && phase <= 0.85f)
                return SquatReferenceWaypoint.EARLY_ASCENT;
            if (phase <= 0.34f)
                return SquatReferenceWaypoint.QUARTER_DESCENT;
            return SquatReferenceWaypoint.NEAR_PARALLEL;
        }

        private static SquatPoint3 Point(Vector3 value) => new SquatPoint3(value.x, value.y, value.z);
        private static Vector3 ToUnity(SquatPoint3 value) => new Vector3(value.X, value.Y, value.Z);

        private static PlayerIntentFrame CreateIntent(float yield, float drive) => new PlayerIntentFrame(
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

        [Serializable]
        private struct BoneBinding
        {
            public BoneBinding(Transform bone, Vector3 axisInParentLocal, Quaternion bindLocalRotation, float scale)
            {
                Bone = bone;
                AxisInParentLocal = axisInParentLocal;
                BindLocalRotation = bindLocalRotation;
                Scale = scale;
            }

            public Transform Bone;
            public Vector3 AxisInParentLocal;
            public Quaternion BindLocalRotation;
            public float Scale;
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
            public LandmarkCalibrationRecord[] landmarkCalibration;
            public TimingRecord referenceTiming;
             public ContinuityRecord continuity;
             public RootCorrectionRecord referenceRootCorrection;
             public CoordinateRecord coordinateConventions;
            public RuleSourceRecord ruleSource;
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
        private struct LandmarkCalibrationRecord
        {
            public string id;
            public string boneFrame;
            public VectorRecord localOffset_m;
            public VectorRecord worldPreviewPosition_m;
            public string sourceClass;

            public static LandmarkCalibrationRecord Create(string id, string frame, Vector3 localOffset, Transform bone) => new LandmarkCalibrationRecord
            {
                id = id,
                boneFrame = frame,
                localOffset_m = VectorRecord.From(localOffset),
                worldPreviewPosition_m = VectorRecord.From(bone.TransformPoint(localOffset)),
                sourceClass = SquatDepthGeometry.SourceClass
            };
        }

        [Serializable]
        private struct TimingRecord
        {
            public float fixedStepS;
            public float descentRateMinPerS;
            public float descentRateMaxPerS;
            public float ascentRateMinPerS;
            public float ascentRateMaxPerS;
            public float maxPhaseRatePerS;
            public float maxPhaseAccelerationPerSS;
            public string timingClaim;
        }

        [Serializable]
        private struct ContinuityRecord
        {
            public float maxKeyPoseValueDiscontinuity;
            public float reversalPoseDiscontinuity;
            public float lockoutPoseDiscontinuity;
            public string curve;
        }

        [Serializable]
        private struct RootCorrectionRecord
        {
            public string owner;
            public string mode;
            public string source;
            public bool deterministic;
            public int physicalHierarchyWrites;
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
        private struct RuleSourceRecord
        {
            public string title;
            public string effectiveDate;
            public string version;
            public string sourceClass;
            public string depthCriterion;
            public float depthMarginM;
        }

        [Serializable]
        private struct VectorRecord
        {
            public float x;
            public float y;
            public float z;

            public static VectorRecord From(Vector3 value) => new VectorRecord { x = value.x, y = value.y, z = value.z };
        }

    }
}
