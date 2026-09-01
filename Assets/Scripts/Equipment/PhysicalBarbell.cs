using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Equipment
{
    public enum BarLandmark : byte
    {
        Center,
        LeftRing,
        RightRing,
        LeftCollarFace,
        RightCollarFace,
        LeftSleeveEnd,
        RightSleeveEnd
    }

    [DefaultExecutionOrder(-800)]
    [DisallowMultipleComponent]
    public sealed class PhysicalBarbell : MonoBehaviour
    {
        private const string MeasurementArtifactPath = "Artifacts/Measurements/GAM-8-physical-barbell.json";
        private const int PlateVisualPoolPerSide = 40;
        private const float ImpulseApplicationXBarMeters = 0.250f;

        [SerializeField] private FoundationBootstrap foundation;
        [SerializeField] private float initialLoadKg = 105f;
        [SerializeField] private Vector3 spawnPosition = new Vector3(0f, 1.35f, 1.55f);
        [SerializeField] private bool showDebugLandmarks = true;
        [SerializeField] private float diagnosticImpulseNewtonSeconds = 80f;

        private readonly List<GameObject> _leftPlateVisuals = new List<GameObject>(PlateVisualPoolPerSide);
        private readonly List<GameObject> _rightPlateVisuals = new List<GameObject>(PlateVisualPoolPerSide);
        private readonly Dictionary<float, Material> _plateMaterials = new Dictionary<float, Material>();
        private readonly List<GameObject> _debugMarkers = new List<GameObject>();
        private readonly List<LineRenderer> _inertiaAxes = new List<LineRenderer>(3);

        private GameObject _barRoot;
        private Rigidbody _barBody;
        private MeshCollider _leftPlateCollider;
        private MeshCollider _rightPlateCollider;
        private LineRenderer _recordedTrail;
        private Material _steelMaterial;
        private Material _sleeveMaterial;
        private Material _collarMaterial;
        private Material _landmarkMaterial;
        private Material _axisMaterial;
        private PhysicsMaterial _contactMaterial;
        private BarbellLoadPlan _loadPlan;
        private BarbellInertiaModel _inertiaModel;
        private bool _trailVisible;
        private bool _pendingImpulseMeasurement;
        private ulong _impulseApplicationTick;
        private Vector3 _preImpulseLinearVelocity;
        private Vector3 _preImpulseAngularVelocity;
        private Vector3 _lastImpulseLinearResponse;
        private Vector3 _lastImpulseAngularResponse;
        private string _status = "Building physical barbell...";
        private int _lastTrailSampleCount = -1;

        public Rigidbody Body => _barBody;
        public BarbellLoadPlan LoadPlan => _loadPlan;
        public BarbellInertiaModel InertiaModel => _inertiaModel;
        public float LoadedMassKg => _barBody == null ? 0f : _barBody.mass;
        public bool IsBuilt => _barBody != null;
        public string Status => _status;
        public Vector3 LastImpulseLinearResponse => _lastImpulseLinearResponse;
        public Vector3 LastImpulseAngularResponse => _lastImpulseAngularResponse;
        public bool HasPendingImpulseMeasurement => _pendingImpulseMeasurement;
        public bool IsRecordedTrailVisible => _trailVisible;
        public int RecordedTrailPointCount => _recordedTrail == null ? 0 : _recordedTrail.positionCount;

        private void Start()
        {
            Build();
        }

        public void Build()
        {
            if (_barBody != null)
                throw new InvalidOperationException("The physical barbell has already been built.");

            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (foundation == null || !foundation.Runtime.IsInitialized)
                throw new InvalidOperationException("The foundation runtime must initialize before the physical barbell.");

            _loadPlan = BarbellLoadingSolver.Solve(initialLoadKg);
            _barRoot = new GameObject("Barbell_GAM8_Authoritative");
            SceneManager.MoveGameObjectToScene(_barRoot, foundation.Runtime.AuthoritativeScene);
            _barRoot.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);

            _barBody = _barRoot.AddComponent<Rigidbody>();
            _barBody.useGravity = true;
            _barBody.isKinematic = false;
            _barBody.interpolation = RigidbodyInterpolation.None;
            _barBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _barBody.linearDamping = 0.035f;
            _barBody.angularDamping = 0.055f;
            _barBody.solverIterations = 12;
            _barBody.solverVelocityIterations = 6;
            _barBody.maxAngularVelocity = 50f;
            _barBody.sleepThreshold = 0.005f;

            CreateMaterials();
            CreateStaticVisuals();
            CreatePhysicalColliders();
            CreatePlateVisualPool();
            CreateDebugOverlay();
            CreateRecordedTrail();
            ApplyLoadInternal(_loadPlan);

            foundation.Runtime.RegisterBody(_barBody, BarbellPrototypeConfiguration.BodyId);
            _status = "105 kg loaded: one dynamic Rigidbody, gravity on";
        }

        private void Update()
        {
            if (_barBody == null)
                return;

            if (_pendingImpulseMeasurement && foundation.Runtime.CurrentTime.Tick > _impulseApplicationTick)
            {
                _lastImpulseLinearResponse = _barBody.linearVelocity - _preImpulseLinearVelocity;
                _lastImpulseAngularResponse = _barBody.angularVelocity - _preImpulseAngularVelocity;
                _pendingImpulseMeasurement = false;
                _status = string.Format(
                    CultureInfo.InvariantCulture,
                    "Impulse response: dv {0:0.000} m/s, dw {1:0.000} rad/s",
                    _lastImpulseLinearResponse.magnitude,
                    _lastImpulseAngularResponse.magnitude);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.digit1Key.wasPressedThisFrame)
                ConfigureLoad(25f);
            else if (keyboard.digit2Key.wasPressedThisFrame)
                ConfigureLoad(105f);
            else if (keyboard.digit3Key.wasPressedThisFrame)
                ConfigureLoad(205f);
            else if (keyboard.dKey.wasPressedThisFrame)
                ResetAndDrop();
            else if (keyboard.pKey.wasPressedThisFrame)
                ApplyDiagnosticImpulse();
            else if (keyboard.rKey.wasPressedThisFrame)
                ToggleRecording();
            else if (keyboard.gKey.wasPressedThisFrame)
                ToggleRecordedTrail();
            else if (keyboard.cKey.wasPressedThisFrame)
                showDebugLandmarks = !showDebugLandmarks;
            else if (keyboard.fKey.wasPressedThisFrame)
                FreezeForInspection();
        }

        private void LateUpdate()
        {
            if (_barBody == null)
                return;

            ApplyDebugVisibility();
            if (_trailVisible)
                UpdateRecordedTrail();
        }

        public void ConfigureLoad(float totalLoadKg)
        {
            if (_barBody == null)
                return;

            BarbellLoadPlan plan = BarbellLoadingSolver.Solve(totalLoadKg);
            foundation.Runtime.Reset();
            ResetImpulseMeasurement();
            ResetRecordedTrailPresentation();
            ApplyLoadInternal(plan);
            _barBody.isKinematic = false;
            _barBody.useGravity = true;
            _barBody.WakeUp();
            _status = string.Format(CultureInfo.InvariantCulture, "{0:0.##} kg loaded: symmetric plate plan", totalLoadKg);
        }

        public void ResetAndDrop()
        {
            if (_barBody == null)
                return;

            foundation.Runtime.Reset();
            ResetImpulseMeasurement();
            ResetRecordedTrailPresentation();
            _barBody.isKinematic = false;
            _barBody.useGravity = true;
            _barBody.WakeUp();
            _status = "DROP: released as a dynamic body under gravity";
        }

        public void FreezeForInspection()
        {
            if (_barBody == null)
                return;

            foundation.Runtime.Reset();
            ResetImpulseMeasurement();
            ResetRecordedTrailPresentation();
            _barBody.linearVelocity = Vector3.zero;
            _barBody.angularVelocity = Vector3.zero;
            _barBody.isKinematic = true;
            _status = "AUTHORING INSPECTION: bar frozen explicitly (not a physical trial)";
        }

        public void ApplyDiagnosticImpulse()
        {
            if (_barBody == null || _barBody.isKinematic)
                return;

            _preImpulseLinearVelocity = _barBody.linearVelocity;
            _preImpulseAngularVelocity = _barBody.angularVelocity;
            _lastImpulseLinearResponse = Vector3.zero;
            _lastImpulseAngularResponse = Vector3.zero;
            _impulseApplicationTick = foundation.Runtime.CurrentTime.Tick;
            Vector3 impulse = Vector3.forward * diagnosticImpulseNewtonSeconds;
            _barBody.AddForceAtPosition(impulse, GetWorldPointFromBarX(ImpulseApplicationXBarMeters), ForceMode.Impulse);
            _pendingImpulseMeasurement = true;
            _status = string.Format(
                CultureInfo.InvariantCulture,
                "PHYSICAL IMPULSE: {0:0.#} N*s at BAR x={1:0.000} m",
                diagnosticImpulseNewtonSeconds,
                ImpulseApplicationXBarMeters);
        }

        public void ToggleRecording()
        {
            if (_barBody == null)
                return;

            if (foundation.Runtime.AttemptTrace.IsRecording)
            {
                foundation.Runtime.EndAttemptTrace();
                _status = string.Format(CultureInfo.InvariantCulture, "TRACE STOPPED: {0} samples", foundation.Runtime.AttemptTrace.Count);
                return;
            }

            foundation.Runtime.BeginAttemptTrace();
            ResetRecordedTrailPresentation();
            _status = "TRACE RECORDING: post-physics observations only";
        }

        public void ClearTrace()
        {
            if (foundation == null || !foundation.Runtime.IsInitialized)
                return;

            foundation.Runtime.EndAttemptTrace();
            foundation.Runtime.AttemptTrace.Clear();
            ResetRecordedTrailPresentation();
            _status = "TRACE CLEARED: no recorded state retained";
        }

        public void ToggleRecordedTrail()
        {
            if (_recordedTrail == null)
                return;

            if (foundation.Runtime.AttemptTrace.Count < 2)
            {
                _status = "TRACE TRAIL: record at least two post-physics samples first";
                return;
            }

            _trailVisible = !_trailVisible;
            _recordedTrail.enabled = _trailVisible;
            if (_trailVisible)
                UpdateRecordedTrail();
            _status = _trailVisible ? "RECORDED STATE: trail visible, no physics authority" : "RECORDED STATE: trail hidden";
        }

        public Vector3 GetWorldPointFromBarX(float xBarMeters)
        {
            if (_barRoot == null)
                throw new InvalidOperationException("The physical barbell has not been built.");
            return _barBody.position + _barBody.rotation * new Vector3(xBarMeters, 0f, 0f);
        }

        public Vector3 GetWorldLandmark(BarLandmark landmark)
        {
            switch (landmark)
            {
                case BarLandmark.Center:
                    return GetWorldPointFromBarX(0f);
                case BarLandmark.LeftRing:
                    return GetWorldPointFromBarX(-BarbellPrototypeConfiguration.RingXBarMeters);
                case BarLandmark.RightRing:
                    return GetWorldPointFromBarX(BarbellPrototypeConfiguration.RingXBarMeters);
                case BarLandmark.LeftCollarFace:
                    return GetWorldPointFromBarX(-BarbellPrototypeConfiguration.CollarFaceXBarMeters);
                case BarLandmark.RightCollarFace:
                    return GetWorldPointFromBarX(BarbellPrototypeConfiguration.CollarFaceXBarMeters);
                case BarLandmark.LeftSleeveEnd:
                    return GetWorldPointFromBarX(-BarbellPrototypeConfiguration.SleeveEndXBarMeters);
                case BarLandmark.RightSleeveEnd:
                    return GetWorldPointFromBarX(BarbellPrototypeConfiguration.SleeveEndXBarMeters);
                default:
                    throw new ArgumentOutOfRangeException(nameof(landmark), landmark, null);
            }
        }

        public void WriteMeasurementArtifact()
        {
            if (_barBody == null)
                throw new InvalidOperationException("The physical barbell must be built before writing its artifact.");

            BarbellLoadPlan[] plans =
            {
                BarbellLoadingSolver.Solve(25f),
                BarbellLoadingSolver.Solve(105f),
                BarbellLoadingSolver.Solve(205f)
            };
            var artifact = new MeasurementArtifact
            {
                mission = "POWERLIFTING_SIMULATOR_GAM_8_PHYSICAL_BARBELL_AND_STATE_SEAM",
                linearIssue = "GAM-8",
                unityVersion = Application.unityVersion,
                ipfSourceTitle = BarbellPrototypeConfiguration.RulebookTitle,
                ipfEffectiveDate = BarbellPrototypeConfiguration.RulebookEffectiveDate,
                ipfVersion = BarbellPrototypeConfiguration.RulebookVersion,
                bodyId = BarbellPrototypeConfiguration.BodyId,
                sourceDirectRuleBounds = BuildSourceDirectRuleBounds(),
                projectCalibration = BuildProjectCalibrationRecord(),
                loadingModel = BuildLoadingModelRecord(),
                loads = BuildLoadRecords(plans),
                inertia = BuildInertiaRecords(plans),
                landmarks = BuildLandmarkRecord(),
                runtime = new RuntimeRecord
                {
                    rigidbodyCount = _barRoot.GetComponentsInChildren<Rigidbody>(true).Length,
                    isKinematic = _barBody.isKinematic,
                    useGravity = _barBody.useGravity,
                    collisionDetectionMode = _barBody.collisionDetectionMode.ToString(),
                    initialSpawnPosition = VectorRecord.From(spawnPosition)
                },
                observation = new ObservationRecord
                {
                    observedBodyCount = foundation.Runtime.CurrentObservation.BodyCount,
                    barBodyId = BarbellPrototypeConfiguration.BodyId,
                    traceSchemaVersion = AttemptTrace.SchemaVersion,
                    traceCapacity = foundation.Runtime.AttemptTrace.Capacity,
                    replaySeam = "Recorded post-physics bar poses are rendered as a main-scene LineRenderer trail; no replay physics or resimulation."
                }
            };

            string path = Path.GetFullPath(MeasurementArtifactPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(artifact, true));
            _status = "WROTE Artifacts/Measurements/GAM-8-physical-barbell.json";
        }

        private static SourceDirectRuleBoundsRecord BuildSourceDirectRuleBounds() => new SourceDirectRuleBoundsRecord
        {
            overallLengthMaxM = 2.20f,
            collarFaceSpacingMinM = 1.31f,
            collarFaceSpacingMaxM = 1.32f,
            shaftDiameterMinM = 0.028f,
            shaftDiameterMaxM = 0.029f,
            barAndCollarsMassKg = 25f,
            sleeveDiameterMinM = 0.050f,
            sleeveDiameterMaxM = 0.052f,
            ringSpacingM = 0.810f,
            collarMassEachKg = 2.5f,
            plateDenominationsKg = new[] { 25f, 20f, 15f, 10f, 5f, 2.5f, 1.25f },
            largestPlateDiameterMaxM = 0.450f,
            plates20AndOverThicknessMaxM = 0.060f,
            plates15AndUnderThicknessMaxM = 0.030f,
            plateColors = "25=red;20=blue;15=yellow;10_and_under=any",
            sourceClass = "SOURCE_DIRECT_IPF_2026_V3"
        };

        private ProjectCalibrationRecord BuildProjectCalibrationRecord() => new ProjectCalibrationRecord
        {
            selectedBarGeometry = BuildBarGeometryRecord(),
            plateGeometry = BuildPlateGeometryRecords(),
            inventory = BuildInventoryRecords(),
            contactDynamicFriction = BarbellPrototypeConfiguration.ContactDynamicFriction,
            contactStaticFriction = BarbellPrototypeConfiguration.ContactStaticFriction,
            contactRestitution = BarbellPrototypeConfiguration.ContactRestitution,
            spawnPosition = VectorRecord.From(spawnPosition),
            sourceClass = "GAME_CALIBRATION"
        };

        private static LoadingModelRecord BuildLoadingModelRecord() => new LoadingModelRecord
        {
            bareBarMassKg = BarbellPrototypeConfiguration.BareBarMassKg,
            collarMassEachKg = BarbellPrototypeConfiguration.CollarMassEachKg,
            baseBarbellMassKg = BarbellPrototypeConfiguration.BaseBarbellMassKg,
            plateMassRule = "requested total = 20 kg bare bar + 5 kg collars + equal plate mass on each side",
            sourceClass = "SOURCE_DERIVED_AND_GAME_CALIBRATION"
        };

        private static LandmarkRecord BuildLandmarkRecord() => new LandmarkRecord
        {
            centerXBarM = 0f,
            leftRingXBarM = -BarbellPrototypeConfiguration.RingXBarMeters,
            rightRingXBarM = BarbellPrototypeConfiguration.RingXBarMeters,
            leftCollarFaceXBarM = -BarbellPrototypeConfiguration.CollarFaceXBarMeters,
            rightCollarFaceXBarM = BarbellPrototypeConfiguration.CollarFaceXBarMeters,
            leftSleeveEndXBarM = -BarbellPrototypeConfiguration.SleeveEndXBarMeters,
            rightSleeveEndXBarM = BarbellPrototypeConfiguration.SleeveEndXBarMeters,
            sourceClass = "GAME_CALIBRATION_FROM_SOURCE_RANGE"
        };

        private void ApplyLoadInternal(BarbellLoadPlan plan)
        {
            _loadPlan = plan;
            _inertiaModel = BarbellPrototypeConfiguration.ComputeInertia(plan);
            _barBody.mass = plan.TotalMassKg;
            _barBody.centerOfMass = _inertiaModel.CenterOfMassBarMeters;
            _barBody.inertiaTensor = _inertiaModel.InertiaTensorKgM2;
            _barBody.inertiaTensorRotation = Quaternion.identity;
            ApplyPlateVisuals(plan.PlatesPerSideKg, _leftPlateVisuals, -1f, "left");
            ApplyPlateVisuals(plan.PlatesPerSideKg, _rightPlateVisuals, 1f, "right");
            ApplyPlateCollider(_leftPlateCollider, plan.PlatesPerSideKg, -1f);
            ApplyPlateCollider(_rightPlateCollider, plan.PlatesPerSideKg, 1f);
            UpdateDebugOverlay();
        }

        private void CreateMaterials()
        {
            _steelMaterial = CreateMaterial("GAM8_Steel", new Color(0.22f, 0.25f, 0.29f), 0.85f);
            _sleeveMaterial = CreateMaterial("GAM8_SleeveSteel", new Color(0.38f, 0.42f, 0.48f), 0.9f);
            _collarMaterial = CreateMaterial("GAM8_RemovableCollar", new Color(0.08f, 0.10f, 0.12f), 0.65f);
            _landmarkMaterial = CreateMaterial("GAM8_Landmarks", new Color(0.16f, 0.85f, 0.95f), 0.1f);
            _axisMaterial = CreateMaterial("GAM8_InertiaAxes", new Color(0.95f, 0.72f, 0.22f), 0.1f);
            _contactMaterial = new PhysicsMaterial("GAM8_BarPlatformContact")
            {
                dynamicFriction = BarbellPrototypeConfiguration.ContactDynamicFriction,
                staticFriction = BarbellPrototypeConfiguration.ContactStaticFriction,
                bounciness = BarbellPrototypeConfiguration.ContactRestitution,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
        }

        private void CreateStaticVisuals()
        {
            CreateCylinderVisual("ShaftVisual", BarbellPrototypeConfiguration.CollarFaceSpacingMeters, BarbellPrototypeConfiguration.ShaftDiameterMeters, 0f, _steelMaterial);
            float sleeveLength = BarbellPrototypeConfiguration.SleeveEndXBarMeters - BarbellPrototypeConfiguration.CollarFaceXBarMeters;
            float sleeveCenter = (BarbellPrototypeConfiguration.CollarFaceXBarMeters + BarbellPrototypeConfiguration.SleeveEndXBarMeters) * 0.5f;
            CreateCylinderVisual("LeftSleeveVisual", sleeveLength, BarbellPrototypeConfiguration.SleeveDiameterMeters, -sleeveCenter, _sleeveMaterial);
            CreateCylinderVisual("RightSleeveVisual", sleeveLength, BarbellPrototypeConfiguration.SleeveDiameterMeters, sleeveCenter, _sleeveMaterial);
            CreateCylinderVisual("LeftShoulderVisual", 0.040f, 0.090f, -BarbellPrototypeConfiguration.CollarFaceXBarMeters, _steelMaterial);
            CreateCylinderVisual("RightShoulderVisual", 0.040f, 0.090f, BarbellPrototypeConfiguration.CollarFaceXBarMeters, _steelMaterial);
            CreateCylinderVisual("LeftCollarVisual", BarbellPrototypeConfiguration.CollarThicknessMeters, BarbellPrototypeConfiguration.CollarDiameterMeters, -(BarbellPrototypeConfiguration.CollarFaceXBarMeters + BarbellPrototypeConfiguration.CollarThicknessMeters * 0.5f), _collarMaterial);
            CreateCylinderVisual("RightCollarVisual", BarbellPrototypeConfiguration.CollarThicknessMeters, BarbellPrototypeConfiguration.CollarDiameterMeters, BarbellPrototypeConfiguration.CollarFaceXBarMeters + BarbellPrototypeConfiguration.CollarThicknessMeters * 0.5f, _collarMaterial);
            CreateCylinderVisual("LeftRingVisual", 0.012f, 0.036f, -BarbellPrototypeConfiguration.RingXBarMeters, _collarMaterial);
            CreateCylinderVisual("RightRingVisual", 0.012f, 0.036f, BarbellPrototypeConfiguration.RingXBarMeters, _collarMaterial);
        }

        private void CreatePhysicalColliders()
        {
            CapsuleCollider shaft = _barRoot.AddComponent<CapsuleCollider>();
            shaft.direction = 0;
            shaft.radius = BarbellPrototypeConfiguration.ShaftDiameterMeters * 0.5f;
            shaft.height = BarbellPrototypeConfiguration.CollarFaceSpacingMeters;
            shaft.material = _contactMaterial;

            float sleeveLength = BarbellPrototypeConfiguration.SleeveEndXBarMeters - BarbellPrototypeConfiguration.CollarFaceXBarMeters;
            float sleeveCenter = (BarbellPrototypeConfiguration.CollarFaceXBarMeters + BarbellPrototypeConfiguration.SleeveEndXBarMeters) * 0.5f;
            CreateSleeveCollider("LeftSleeveCollider", -sleeveCenter, sleeveLength);
            CreateSleeveCollider("RightSleeveCollider", sleeveCenter, sleeveLength);
            CreateShoulderCollider("LeftShoulderCollider", -BarbellPrototypeConfiguration.CollarFaceXBarMeters);
            CreateShoulderCollider("RightShoulderCollider", BarbellPrototypeConfiguration.CollarFaceXBarMeters);
            _leftPlateCollider = CreatePlateCollider("LeftPlateAggregateCollider");
            _rightPlateCollider = CreatePlateCollider("RightPlateAggregateCollider");
        }

        private void CreateSleeveCollider(string name, float centerX, float length)
        {
            GameObject colliderObject = new GameObject(name);
            colliderObject.transform.SetParent(_barRoot.transform, false);
            colliderObject.transform.localPosition = new Vector3(centerX, 0f, 0f);
            CapsuleCollider sleeve = colliderObject.AddComponent<CapsuleCollider>();
            sleeve.direction = 0;
            sleeve.radius = BarbellPrototypeConfiguration.SleeveDiameterMeters * 0.5f;
            sleeve.height = length;
            sleeve.material = _contactMaterial;
        }

        private void CreateShoulderCollider(string name, float centerX)
        {
            GameObject colliderObject = new GameObject(name);
            colliderObject.transform.SetParent(_barRoot.transform, false);
            colliderObject.transform.localPosition = new Vector3(centerX, 0f, 0f);
            BoxCollider shoulder = colliderObject.AddComponent<BoxCollider>();
            shoulder.size = new Vector3(0.040f, 0.090f, 0.090f);
            shoulder.material = _contactMaterial;
        }

        private MeshCollider CreatePlateCollider(string name)
        {
            GameObject colliderObject = new GameObject(name);
            colliderObject.transform.SetParent(_barRoot.transform, false);
            colliderObject.SetActive(false);
            MeshCollider collider = colliderObject.AddComponent<MeshCollider>();
            collider.convex = true;
            collider.material = _contactMaterial;
            return collider;
        }

        private void CreatePlateVisualPool()
        {
            for (int index = 0; index < PlateVisualPoolPerSide; index++)
            {
                _leftPlateVisuals.Add(CreatePlateVisual("LeftPlateVisual_" + index));
                _rightPlateVisuals.Add(CreatePlateVisual("RightPlateVisual_" + index));
            }
        }

        private GameObject CreatePlateVisual(string name)
        {
            GameObject plate = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            plate.name = name;
            plate.transform.SetParent(_barRoot.transform, false);
            DestroyImmediate(plate.GetComponent<Collider>());
            plate.SetActive(false);
            return plate;
        }

        private void CreateDebugOverlay()
        {
            CreateDebugMarker("BarCOM", 0.038f, Vector3.zero, _landmarkMaterial);
            CreateDebugMarker("LeftRingLandmark", 0.025f, new Vector3(-BarbellPrototypeConfiguration.RingXBarMeters, 0f, 0f), _landmarkMaterial);
            CreateDebugMarker("RightRingLandmark", 0.025f, new Vector3(BarbellPrototypeConfiguration.RingXBarMeters, 0f, 0f), _landmarkMaterial);
            CreateDebugMarker("LeftCollarFaceLandmark", 0.025f, new Vector3(-BarbellPrototypeConfiguration.CollarFaceXBarMeters, 0f, 0f), _landmarkMaterial);
            CreateDebugMarker("RightCollarFaceLandmark", 0.025f, new Vector3(BarbellPrototypeConfiguration.CollarFaceXBarMeters, 0f, 0f), _landmarkMaterial);
            CreateDebugMarker("LeftSleeveEndLandmark", 0.025f, new Vector3(-BarbellPrototypeConfiguration.SleeveEndXBarMeters, 0f, 0f), _landmarkMaterial);
            CreateDebugMarker("RightSleeveEndLandmark", 0.025f, new Vector3(BarbellPrototypeConfiguration.SleeveEndXBarMeters, 0f, 0f), _landmarkMaterial);

            CreateInertiaAxis("InertiaAxisX", Vector3.right, Color.red);
            CreateInertiaAxis("InertiaAxisY", Vector3.up, Color.green);
            CreateInertiaAxis("InertiaAxisZ", Vector3.forward, Color.blue);
            ApplyDebugVisibility();
        }

        private void CreateRecordedTrail()
        {
            GameObject trailObject = new GameObject("RecordedBarTrail_GAM8_PresentationOnly");
            trailObject.transform.SetParent(transform, false);
            _recordedTrail = trailObject.AddComponent<LineRenderer>();
            _recordedTrail.useWorldSpace = true;
            _recordedTrail.widthMultiplier = 0.014f;
            _recordedTrail.positionCount = 0;
            _recordedTrail.numCapVertices = 3;
            _recordedTrail.material = CreateMaterial("GAM8_RecordedTrail", new Color(0.16f, 0.85f, 0.95f), 0.1f);
            _recordedTrail.startColor = new Color(0.16f, 0.85f, 0.95f, 0.9f);
            _recordedTrail.endColor = new Color(0.95f, 0.72f, 0.22f, 0.9f);
            _recordedTrail.enabled = false;
        }

        private void CreateDebugMarker(string name, float diameter, Vector3 localPosition, Material material)
        {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = name;
            marker.transform.SetParent(_barRoot.transform, false);
            marker.transform.localPosition = localPosition;
            marker.transform.localScale = Vector3.one * diameter;
            DestroyImmediate(marker.GetComponent<Collider>());
            SetMaterial(marker.GetComponent<Renderer>(), material);
            _debugMarkers.Add(marker);
        }

        private void CreateInertiaAxis(string name, Vector3 axis, Color color)
        {
            GameObject lineObject = new GameObject(name);
            lineObject.transform.SetParent(_barRoot.transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, -axis * 0.28f);
            line.SetPosition(1, axis * 0.28f);
            line.widthMultiplier = 0.008f;
            line.material = _axisMaterial;
            line.startColor = color;
            line.endColor = color;
            _inertiaAxes.Add(line);
        }

        private GameObject CreateCylinderVisual(string name, float length, float diameter, float centerX, Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visual.name = name;
            visual.transform.SetParent(_barRoot.transform, false);
            visual.transform.localPosition = new Vector3(centerX, 0f, 0f);
            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            visual.transform.localScale = new Vector3(diameter, length * 0.5f, diameter);
            DestroyImmediate(visual.GetComponent<Collider>());
            SetMaterial(visual.GetComponent<Renderer>(), material);
            return visual;
        }

        private void ApplyPlateVisuals(IReadOnlyList<float> plates, List<GameObject> visuals, float side, string sideName)
        {
            float accumulatedThickness = 0f;
            for (int index = 0; index < visuals.Count; index++)
            {
                GameObject visual = visuals[index];
                if (index >= plates.Count)
                {
                    visual.SetActive(false);
                    continue;
                }

                float massKg = plates[index];
                BarbellPlateGeometry geometry = BarbellPrototypeConfiguration.GetPlateGeometry(massKg);
                visual.SetActive(true);
                visual.name = string.Format(CultureInfo.InvariantCulture, "{0}Plate_{1:0.##}kg_{2}", sideName, massKg, index);
                visual.transform.localPosition = new Vector3(
                    side * (BarbellPrototypeConfiguration.PlateStartXBarMeters + accumulatedThickness + geometry.ThicknessMeters * 0.5f),
                    0f,
                    0f);
                visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                visual.transform.localScale = new Vector3(geometry.DiameterMeters, geometry.ThicknessMeters * 0.5f, geometry.DiameterMeters);
                SetMaterial(visual.GetComponent<Renderer>(), GetPlateMaterial(geometry));
                accumulatedThickness += geometry.ThicknessMeters;
            }
        }

        private void ApplyPlateCollider(MeshCollider collider, IReadOnlyList<float> plates, float side)
        {
            if (plates.Count == 0)
            {
                collider.sharedMesh = null;
                collider.gameObject.SetActive(false);
                return;
            }

            float stackLength = 0f;
            float radius = 0f;
            for (int index = 0; index < plates.Count; index++)
            {
                BarbellPlateGeometry geometry = BarbellPrototypeConfiguration.GetPlateGeometry(plates[index]);
                stackLength += geometry.ThicknessMeters;
                radius = Mathf.Max(radius, geometry.DiameterMeters * 0.5f);
            }

            Mesh previousMesh = collider.sharedMesh;
            collider.sharedMesh = null;
            if (previousMesh != null)
                DestroyImmediate(previousMesh);
            collider.sharedMesh = CreateCylinderMesh(stackLength, radius, 24);
            collider.transform.localPosition = new Vector3(
                side * (BarbellPrototypeConfiguration.PlateStartXBarMeters + stackLength * 0.5f),
                0f,
                0f);
            collider.gameObject.SetActive(true);
        }

        private void UpdateDebugOverlay()
        {
            if (_inertiaModel == null)
                return;

            float axisScale = Mathf.Clamp(0.18f + _inertiaModel.InertiaTensorKgM2.magnitude * 0.003f, 0.18f, 0.48f);
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int index = 0; index < _inertiaAxes.Count; index++)
            {
                _inertiaAxes[index].SetPosition(0, -axes[index] * axisScale);
                _inertiaAxes[index].SetPosition(1, axes[index] * axisScale);
            }
        }

        private void ApplyDebugVisibility()
        {
            for (int index = 0; index < _debugMarkers.Count; index++)
                _debugMarkers[index].SetActive(showDebugLandmarks);
            for (int index = 0; index < _inertiaAxes.Count; index++)
                _inertiaAxes[index].enabled = showDebugLandmarks;
        }

        private void UpdateRecordedTrail()
        {
            AttemptTrace trace = foundation.Runtime.AttemptTrace;
            if (trace.Count < 2 || trace.Count == _lastTrailSampleCount)
                return;

            _recordedTrail.positionCount = trace.Count;
            for (int index = 0; index < trace.Count; index++)
            {
                AttemptTraceSample sample = trace.GetSample(index);
                if (!sample.Observation.TryGetBody(BarbellPrototypeConfiguration.BodyId, out PhysicalBodyObservation bar))
                    continue;
                _recordedTrail.SetPosition(index, ToUnityVector(bar.PositionMeters));
            }
            _lastTrailSampleCount = trace.Count;
        }

        private void ResetRecordedTrailPresentation()
        {
            _trailVisible = false;
            _lastTrailSampleCount = -1;
            if (_recordedTrail == null)
                return;

            _recordedTrail.positionCount = 0;
            _recordedTrail.enabled = false;
        }

        private void ResetImpulseMeasurement()
        {
            _pendingImpulseMeasurement = false;
            _impulseApplicationTick = 0ul;
            _preImpulseLinearVelocity = Vector3.zero;
            _preImpulseAngularVelocity = Vector3.zero;
            _lastImpulseLinearResponse = Vector3.zero;
            _lastImpulseAngularResponse = Vector3.zero;
        }

        private Material GetPlateMaterial(BarbellPlateGeometry geometry)
        {
            if (_plateMaterials.TryGetValue(geometry.MassKilograms, out Material material))
                return material;

            material = CreateMaterial(
                string.Format(CultureInfo.InvariantCulture, "GAM8_Plate_{0:0.##}kg", geometry.MassKilograms),
                geometry.Color,
                geometry.MassKilograms >= 15f ? 0.15f : 0.05f);
            _plateMaterials.Add(geometry.MassKilograms, material);
            return material;
        }

        private static Material CreateMaterial(string name, Color color, float metallic)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return null;

            var material = new Material(shader)
            {
                name = name,
                color = color
            };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", 0.72f);
            return material;
        }

        private static void SetMaterial(Renderer renderer, Material material)
        {
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
        }

        private static Mesh CreateCylinderMesh(float length, float radius, int segments)
        {
            var mesh = new Mesh { name = "GAM8_ConvexPlateAggregate" };
            var vertices = new Vector3[segments * 2 + 2];
            float halfLength = length * 0.5f;
            for (int index = 0; index < segments; index++)
            {
                float angle = Mathf.PI * 2f * index / segments;
                float y = Mathf.Cos(angle) * radius;
                float z = Mathf.Sin(angle) * radius;
                vertices[index] = new Vector3(-halfLength, y, z);
                vertices[segments + index] = new Vector3(halfLength, y, z);
            }
            vertices[segments * 2] = new Vector3(-halfLength, 0f, 0f);
            vertices[segments * 2 + 1] = new Vector3(halfLength, 0f, 0f);

            var triangles = new int[segments * 12];
            int triangleIndex = 0;
            for (int index = 0; index < segments; index++)
            {
                int next = (index + 1) % segments;
                triangles[triangleIndex++] = index;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = segments + index;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = segments + next;
                triangles[triangleIndex++] = segments + index;
                triangles[triangleIndex++] = segments * 2;
                triangles[triangleIndex++] = next;
                triangles[triangleIndex++] = index;
                triangles[triangleIndex++] = segments * 2 + 1;
                triangles[triangleIndex++] = segments + index;
                triangles[triangleIndex++] = segments + next;
            }
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 ToUnityVector(Vector3Value value) => new Vector3(value.X, value.Y, value.Z);

        private string FormatPlatePlan()
        {
            if (_loadPlan == null)
                return "-";

            var builder = new StringBuilder();
            for (int index = 0; index < _loadPlan.PlatesPerSideKg.Count; index++)
            {
                if (index > 0)
                    builder.Append(" + ");
                builder.Append(_loadPlan.PlatesPerSideKg[index].ToString("0.##", CultureInfo.InvariantCulture));
            }
            return builder.Length == 0 ? "none" : builder.ToString();
        }

        private void OnGUI()
        {
            if (_barBody == null)
                return;

            GUILayout.BeginArea(new Rect(16f, 16f, 390f, 270f), GUI.skin.window);
            GUILayout.Label("GAM-8 PHYSICAL BARBELL");
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "Load {0:0.##} kg | bar + collars 25 kg", _barBody.mass));
            GUILayout.Label("Per side: " + FormatPlatePlan());
            GUILayout.Label("Inertia kg*m2: " + _inertiaModel.InertiaTensorKgM2.ToString("F4"));
            GUILayout.Label("COM BAR: " + _inertiaModel.CenterOfMassBarMeters.ToString("F5"));
            GUILayout.Label("Observed bodies: " + foundation.Runtime.CurrentObservation.BodyCount);
            GUILayout.Label(_status);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("25 kg")) ConfigureLoad(25f);
            if (GUILayout.Button("105 kg")) ConfigureLoad(105f);
            if (GUILayout.Button("205 kg")) ConfigureLoad(205f);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Drop")) ResetAndDrop();
            if (GUILayout.Button("Impulse")) ApplyDiagnosticImpulse();
            if (GUILayout.Button("Freeze")) FreezeForInspection();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button(foundation.Runtime.AttemptTrace.IsRecording ? "Stop Trace" : "Record Trace")) ToggleRecording();
            if (GUILayout.Button(_trailVisible ? "Hide Trail" : "Show Trail")) ToggleRecordedTrail();
            if (GUILayout.Button("Write JSON")) WriteMeasurementArtifact();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Clear Trace")) ClearTrace();
            GUILayout.Label("Keys: 1/2/3 load, D drop, P impulse, R record, G trail, C debug, F inspect");
            GUILayout.EndArea();
        }

        private static BarGeometryRecord BuildBarGeometryRecord() => new BarGeometryRecord
        {
            overallLengthM = BarbellPrototypeConfiguration.OverallLengthMeters,
            collarFaceSpacingM = BarbellPrototypeConfiguration.CollarFaceSpacingMeters,
            shaftDiameterM = BarbellPrototypeConfiguration.ShaftDiameterMeters,
            sleeveDiameterM = BarbellPrototypeConfiguration.SleeveDiameterMeters,
            ringXBarM = BarbellPrototypeConfiguration.RingXBarMeters,
            collarFaceXBarM = BarbellPrototypeConfiguration.CollarFaceXBarMeters,
            sleeveEndXBarM = BarbellPrototypeConfiguration.SleeveEndXBarMeters,
            sourceClass = "GAME_CALIBRATION_FROM_SOURCE_RANGE"
        };

        private PlateGeometryRecord[] BuildPlateGeometryRecords()
        {
            var records = new List<PlateGeometryRecord>();
            for (int index = 0; index < BarbellPrototypeConfiguration.Inventory.Count; index++)
            {
                BarbellInventoryEntry entry = BarbellPrototypeConfiguration.Inventory[index];
                BarbellPlateGeometry geometry = BarbellPrototypeConfiguration.GetPlateGeometry(entry.MassKilograms);
                records.Add(new PlateGeometryRecord
                {
                    massKg = geometry.MassKilograms,
                    diameterM = geometry.DiameterMeters,
                    thicknessM = geometry.ThicknessMeters,
                    color = ColorUtility.ToHtmlStringRGB(geometry.Color),
                    colorSource = geometry.ColorSource
                });
            }
            return records.ToArray();
        }

        private InventoryRecord[] BuildInventoryRecords()
        {
            var records = new InventoryRecord[BarbellPrototypeConfiguration.Inventory.Count];
            for (int index = 0; index < records.Length; index++)
            {
                BarbellInventoryEntry entry = BarbellPrototypeConfiguration.Inventory[index];
                records[index] = new InventoryRecord { massKg = entry.MassKilograms, maximumPairsPerSide = entry.MaximumPairsPerSide, sourceClass = "GAME_CALIBRATION" };
            }
            return records;
        }

        private LoadRecord[] BuildLoadRecords(IReadOnlyList<BarbellLoadPlan> plans)
        {
            var records = new LoadRecord[plans.Count];
            for (int index = 0; index < plans.Count; index++)
            {
                BarbellLoadPlan plan = plans[index];
                records[index] = new LoadRecord
                {
                    requestedTotalMassKg = plan.RequestedTotalMassKg,
                    totalMassKg = plan.TotalMassKg,
                    leftPlanKg = Copy(plan.PlatesPerSideKg),
                    rightPlanKg = Copy(plan.PlatesPerSideKg),
                    symmetryErrorKg = 0f
                };
            }
            return records;
        }

        private InertiaRecord[] BuildInertiaRecords(IReadOnlyList<BarbellLoadPlan> plans)
        {
            var records = new InertiaRecord[plans.Count];
            for (int index = 0; index < plans.Count; index++)
            {
                BarbellInertiaModel model = BarbellPrototypeConfiguration.ComputeInertia(plans[index]);
                records[index] = new InertiaRecord
                {
                    loadKg = plans[index].TotalMassKg,
                    componentMasses = BuildComponentRecords(model.Components),
                    effectiveDensityKgM3 = model.EffectiveDensityKgM3,
                    centerOfMassBarM = VectorRecord.From(model.CenterOfMassBarMeters),
                    inertiaTensorKgM2 = VectorRecord.From(model.InertiaTensorKgM2),
                    method = "Aligned solid-cylinder primitives plus parallel-axis theorem; bare shaft/sleeves use a common effective density scaled to 20 kg."
                };
            }
            return records;
        }

        private static ComponentRecord[] BuildComponentRecords(IReadOnlyList<BarbellMassComponent> components)
        {
            var records = new ComponentRecord[components.Count];
            for (int index = 0; index < components.Count; index++)
            {
                BarbellMassComponent component = components[index];
                records[index] = new ComponentRecord
                {
                    id = component.Id,
                    massKg = component.MassKilograms,
                    positionBarM = VectorRecord.From(component.PositionBarMeters),
                    dimensionsBarM = VectorRecord.From(component.DimensionsBarMeters),
                    principalInertiaKgM2 = VectorRecord.From(component.PrincipalInertiaKgM2)
                };
            }
            return records;
        }

        private static float[] Copy(IReadOnlyList<float> values)
        {
            var copy = new float[values.Count];
            for (int index = 0; index < copy.Length; index++)
                copy[index] = values[index];
            return copy;
        }

        [Serializable]
        private sealed class MeasurementArtifact
        {
            public string mission;
            public string linearIssue;
            public string unityVersion;
            public string ipfSourceTitle;
            public string ipfEffectiveDate;
            public string ipfVersion;
            public string bodyId;
            public SourceDirectRuleBoundsRecord sourceDirectRuleBounds;
            public ProjectCalibrationRecord projectCalibration;
            public LoadingModelRecord loadingModel;
            public LoadRecord[] loads;
            public InertiaRecord[] inertia;
            public LandmarkRecord landmarks;
            public RuntimeRecord runtime;
            public ObservationRecord observation;
        }

        [Serializable]
        private sealed class SourceDirectRuleBoundsRecord
        {
            public float overallLengthMaxM;
            public float collarFaceSpacingMinM;
            public float collarFaceSpacingMaxM;
            public float shaftDiameterMinM;
            public float shaftDiameterMaxM;
            public float barAndCollarsMassKg;
            public float sleeveDiameterMinM;
            public float sleeveDiameterMaxM;
            public float ringSpacingM;
            public float collarMassEachKg;
            public float[] plateDenominationsKg;
            public float largestPlateDiameterMaxM;
            public float plates20AndOverThicknessMaxM;
            public float plates15AndUnderThicknessMaxM;
            public string plateColors;
            public string sourceClass;
        }

        [Serializable]
        private sealed class ProjectCalibrationRecord
        {
            public BarGeometryRecord selectedBarGeometry;
            public PlateGeometryRecord[] plateGeometry;
            public InventoryRecord[] inventory;
            public float contactDynamicFriction;
            public float contactStaticFriction;
            public float contactRestitution;
            public VectorRecord spawnPosition;
            public string sourceClass;
        }

        [Serializable]
        private sealed class LoadingModelRecord
        {
            public float bareBarMassKg;
            public float collarMassEachKg;
            public float baseBarbellMassKg;
            public string plateMassRule;
            public string sourceClass;
        }

        [Serializable]
        private sealed class BarGeometryRecord
        {
            public float overallLengthM;
            public float collarFaceSpacingM;
            public float shaftDiameterM;
            public float sleeveDiameterM;
            public float ringXBarM;
            public float collarFaceXBarM;
            public float sleeveEndXBarM;
            public string sourceClass;
        }

        [Serializable]
        private sealed class PlateGeometryRecord
        {
            public float massKg;
            public float diameterM;
            public float thicknessM;
            public string color;
            public string colorSource;
        }

        [Serializable]
        private sealed class InventoryRecord
        {
            public float massKg;
            public int maximumPairsPerSide;
            public string sourceClass;
        }

        [Serializable]
        private sealed class LoadRecord
        {
            public float requestedTotalMassKg;
            public float totalMassKg;
            public float[] leftPlanKg;
            public float[] rightPlanKg;
            public float symmetryErrorKg;
        }

        [Serializable]
        private sealed class InertiaRecord
        {
            public float loadKg;
            public ComponentRecord[] componentMasses;
            public float effectiveDensityKgM3;
            public VectorRecord centerOfMassBarM;
            public VectorRecord inertiaTensorKgM2;
            public string method;
        }

        [Serializable]
        private sealed class LandmarkRecord
        {
            public float centerXBarM;
            public float leftRingXBarM;
            public float rightRingXBarM;
            public float leftCollarFaceXBarM;
            public float rightCollarFaceXBarM;
            public float leftSleeveEndXBarM;
            public float rightSleeveEndXBarM;
            public string sourceClass;
        }

        [Serializable]
        private sealed class ComponentRecord
        {
            public string id;
            public float massKg;
            public VectorRecord positionBarM;
            public VectorRecord dimensionsBarM;
            public VectorRecord principalInertiaKgM2;
        }

        [Serializable]
        private sealed class RuntimeRecord
        {
            public int rigidbodyCount;
            public bool isKinematic;
            public bool useGravity;
            public string collisionDetectionMode;
            public VectorRecord initialSpawnPosition;
        }

        [Serializable]
        private sealed class ObservationRecord
        {
            public int observedBodyCount;
            public string barBodyId;
            public string traceSchemaVersion;
            public int traceCapacity;
            public string replaySeam;
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
