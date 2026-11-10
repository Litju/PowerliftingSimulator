using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// Composition root of the bench scene. It owns the bench-local plant -
    /// the canonical 16-body athlete placed supine, the dynamic bar, the
    /// authoritative bench, two hand-bar grasps - plus the single post-physics
    /// observer that feeds the referee. The accepted squat's controller,
    /// saddle and referee are not present in this scene.
    ///
    /// Authority boundary: the supine setup with the hands on the racked bar
    /// is an authored initial condition written once, before tick zero, and
    /// validated. From tick one every body, including the bar, moves only by
    /// solver forces: drives, gravity, contacts and the grip constraints.
    /// The only external force is the explicitly labeled spotter, enabled
    /// only after the referee has terminated a failed or abandoned attempt.
    /// </summary>
    [DefaultExecutionOrder(-950)]
    [DisallowMultipleComponent]
    public sealed class BenchPressPrototypeController : MonoBehaviour
    {
        public const string ScenePath = "Assets/Scenes/Prototype/BenchPressPrototype.unity";
        public const string SceneName = "BenchPressPrototype";
        public const float QualifiedLoadKg = 25f;
        public const float FootFlatMaxTiltDeg = 15f;
        public const float ElbowUndersideRadiusM = 0.0475f; // forearm proxy radius
        public const float ShoulderTopRadiusM = 0.060f;     // upper-arm proxy radius
        public const float SpotterSpringNpm = 3000f;
        public const float SpotterDamperNspm = 450f;
        public const float SpotterMaxForceN = 700f;

        private static float s_pendingLoadKg = float.NaN;

        [SerializeField] private FoundationBootstrap foundation;
        [SerializeField] private PhysicalAthleteRig athleteRig;
        [SerializeField] private PhysicalBarbell barbell;
        [SerializeField] private float loadKg = QualifiedLoadKg;

        private readonly Dictionary<string, BenchContactSensor> _sensors = new Dictionary<string, BenchContactSensor>(StringComparer.Ordinal);
        private readonly BenchTelemetry _telemetry = new BenchTelemetry();
        private BenchSetupSolver _solver;
        private BenchPlantModel _model;
        private BenchEquipment _equipment;
        private BenchPressController _controller;
        private BenchReferee _referee;
        private BenchGrip _leftGrip;
        private BenchGrip _rightGrip;
        private ConfigurableJoint _spotter;
        private BenchContactSensor _barSensor;
        private Vector3 _previousBarVelocity;
        private bool _abortRequested;
        private bool _previousAbort;
        private int _restCarryTicks;
        private string _startupFailure = string.Empty;

        public event Action<BenchCommand> CommandIssued;

        public bool IsInitialized { get; private set; }
        public string StartupFailure => _startupFailure;
        public float LoadKg => loadKg;
        public BenchPlantModel Model => _model;
        public BenchSetupSolver Solver => _solver;
        public BenchEquipment Equipment => _equipment;
        public BenchPressController Controller => _controller;
        public BenchReferee Referee => _referee;
        public BenchGrip LeftGrip => _leftGrip;
        public BenchGrip RightGrip => _rightGrip;
        public BenchTelemetry Telemetry => _telemetry;
        public PhysicalAthleteRig AthleteRig => athleteRig;
        public PhysicalBarbell Barbell => barbell;
        public FoundationBootstrap Foundation => foundation;
        public bool SpotterActive => _spotter != null;
        public bool AttemptFinished { get; private set; }
        public BenchObservation LastObservation { get; private set; }
        public float TickZeroMaxPenetrationM { get; private set; }
        public float TickZeroMaxGripSeparationM { get; private set; }
        public List<string> TickZeroSelfPenetrations { get; } = new List<string>();

        public static void SetPendingLoad(float kg) => s_pendingLoadKg = kg;

        public BenchContactSensor Sensor(string segmentId) => _sensors[segmentId];

        private void Awake()
        {
            if (!float.IsNaN(s_pendingLoadKg))
                loadKg = s_pendingLoadKg;
            s_pendingLoadKg = float.NaN;
            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (foundation != null && foundation.Runtime != null && foundation.Runtime.IsInitialized)
                foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
        }

        private void Start() => Initialize();

        public void Initialize()
        {
            if (IsInitialized)
                return;
            try
            {
                InitializeInternal();
                IsInitialized = true;
            }
            catch (Exception exception)
            {
                _startupFailure = exception.Message;
                Debug.LogError("GAM-69 bench startup failed: " + exception);
            }
        }

        private void InitializeInternal()
        {
            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (athleteRig == null)
                athleteRig = FindFirstObjectByType<PhysicalAthleteRig>();
            if (barbell == null)
                barbell = FindFirstObjectByType<PhysicalBarbell>();
            if (foundation == null || !foundation.Runtime.IsInitialized || athleteRig == null || barbell == null)
                throw new InvalidOperationException("The bench scene needs a foundation, a physical athlete and a barbell.");
            if (loadKg < QualifiedLoadKg)
                throw new InvalidOperationException("The bench requires a loaded bar; 25 kg (bar and collars) is the minimum.");

            foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            BenchSurfaceRegistry.Clear();

            if (athleteRig.PoweredController == null)
                athleteRig.Build();
            athleteRig.SetGameplayPerformanceProfile(true);

            float barRadius = BarbellPrototypeConfiguration.ShaftDiameterMeters * 0.5f;
            _solver = new BenchSetupSolver(athleteRig);
            _model = _solver.Solve(barRadius);
            Debug.Log("GAM-69 bench plant model:\n" + _model.Describe());
            if (BenchSetupSolver.IsRejected(_model))
                throw new InvalidOperationException("Bench plant rejected: " + string.Join(" | ", _model.Notes));

            // The bar starts resting in the rests. ConfigureLoad resets the
            // foundation; the athlete is still in its registered pose then,
            // so it is placed afterwards.
            if (!barbell.IsBuilt)
            {
                barbell.SetInitialSpawnPosition(_model.Equipment.BarRestCenter(barRadius));
                barbell.Build();
            }
            barbell.SetGameplayPerformanceProfile(true);
            barbell.ConfigureLoad(loadKg);
            foundation.Runtime.HoldPhysicsUntilInitialStateValidated();

            PlaceAthleteSupine();
            KeepPlantAwake();

            PhysicsMaterial pad = new PhysicsMaterial("GAM69_BenchPadVinyl")
            {
                staticFriction = 0.80f,
                dynamicFriction = 0.70f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            PhysicsMaterial steel = new PhysicsMaterial("GAM69_RackSteel")
            {
                staticFriction = 0.40f,
                dynamicFriction = 0.35f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            _equipment = new BenchEquipment(_model.Equipment);
            _equipment.Build(foundation.Runtime.AuthoritativeScene, pad, steel);

            RegisterSurfacesAndSensors();
            IgnoreGraspedBarCollisions();
            Physics.SyncTransforms();

            _leftGrip = new BenchGrip("left");
            _rightGrip = new BenchGrip("right");
            _leftGrip.Attach(athleteRig.Segments["left_hand"].Body, barbell.Body, _model.LeftGripAnchorLocal);
            _rightGrip.Attach(athleteRig.Segments["right_hand"].Body, barbell.Body, _model.RightGripAnchorLocal);

            _controller = new BenchPressController(athleteRig, _model)
            {
                BarWeightN = barbell.Body.mass * Mathf.Abs(Physics.gravity.y)
            };
            _referee = new BenchReferee(SimulationConstants.FixedDeltaTimeSeconds);
            athleteRig.SetCommandSource(_controller);
            athleteRig.PrimeCommandSource();

            ValidateTickZero();
            foundation.Runtime.RegisterPostPhysicsStep(OnPostPhysicsStep);

            SquatPresentationGrip grip = athleteRig.GetComponent<SquatPresentationGrip>() ??
                athleteRig.gameObject.AddComponent<SquatPresentationGrip>();
            grip.Configure(athleteRig, barbell);

            foundation.Runtime.MarkInitialPhysicalStateValidated();
        }

        /// <summary>
        /// Contact observation needs awake bodies: PhysX reports no contacts
        /// for a sleeping pair, so a settled lifter would read as "raised from
        /// the bench". Sleeping is a solver shortcut, not dynamics; the bench
        /// plant (this scene only) keeps its bodies awake.
        /// </summary>
        private void KeepPlantAwake()
        {
            foreach (PhysicalAthleteRig.SegmentRuntime segment in athleteRig.Segments.Values)
                segment.Body.sleepThreshold = 0f;
            barbell.Body.sleepThreshold = 0f;
        }

        private void PlaceAthleteSupine()
        {
            Dictionary<string, Pose> poses = _solver.Kinematics.Solve(_model.PelvisWorld, _model.SetupLogical);
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in athleteRig.Segments)
            {
                Rigidbody body = entry.Value.Body;
                Pose pose = poses[entry.Key];
                body.isKinematic = false;
                body.transform.SetPositionAndRotation(pose.position, pose.rotation);
                body.position = pose.position;
                body.rotation = pose.rotation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
            Physics.SyncTransforms();
        }

        private void RegisterSurfacesAndSensors()
        {
            BenchSurfaceRegistry.Register(athleteRig.PlatformCollider, BenchSurface.Floor);
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in athleteRig.Segments)
            {
                BenchSurface surface = SurfaceFor(entry.Key);
                BenchSurfaceRegistry.Register(entry.Value.Collider, surface);
            }
            foreach (Collider collider in barbell.Body.GetComponentsInChildren<Collider>(true))
                BenchSurfaceRegistry.Register(collider, BenchSurface.Bar);

            foreach (string id in athleteRig.Segments.Keys)
            {
                Rigidbody body = athleteRig.Segments[id].Body;
                BenchContactSensor sensor = body.GetComponent<BenchContactSensor>() ?? body.gameObject.AddComponent<BenchContactSensor>();
                sensor.OwnSurface = SurfaceFor(id);
                _sensors[id] = sensor;
            }
            _barSensor = barbell.Body.GetComponent<BenchContactSensor>() ?? barbell.Body.gameObject.AddComponent<BenchContactSensor>();
            _barSensor.OwnSurface = BenchSurface.Bar;
            _sensors["bar"] = _barSensor;
        }

        private static BenchSurface SurfaceFor(string id)
        {
            switch (id)
            {
                case "head_neck": return BenchSurface.AthleteHead;
                case "thorax": return BenchSurface.AthleteThorax;
                case "abdomen": return BenchSurface.AthleteAbdomen;
                case "pelvis": return BenchSurface.AthletePelvis;
                case "left_foot":
                case "right_foot": return BenchSurface.AthleteFoot;
                default:
                    return id.Contains("arm") || id.Contains("hand") ? BenchSurface.AthleteArm : BenchSurface.AthleteLeg;
            }
        }

        /// <summary>
        /// Game approximation, declared: the bar does not collide with the
        /// limbs that hold it (hands, forearms, upper arms). The grasp joint
        /// carries that load path instead; coarse limb proxies would otherwise
        /// fight the constraint around the shaft. Chest, abdomen, pelvis and
        /// head contacts with the bar remain fully active.
        /// </summary>
        private void IgnoreGraspedBarCollisions()
        {
            Collider[] bar = barbell.Body.GetComponentsInChildren<Collider>(true);
            foreach (string id in new[] { "left_hand", "right_hand", "left_forearm", "right_forearm", "left_upper_arm", "right_upper_arm" })
            {
                Collider limb = athleteRig.Segments[id].Collider;
                foreach (Collider c in bar)
                    Physics.IgnoreCollision(limb, c, true);
            }
        }

        private void ValidateTickZero()
        {
            float worst = 0f;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in athleteRig.Segments)
            {
                Collider body = entry.Value.Collider;
                foreach (Collider env in _equipment.Colliders)
                    worst = Mathf.Max(worst, Penetration(body, env));
                worst = Mathf.Max(worst, Penetration(body, athleteRig.PlatformCollider));
            }
            foreach (Collider barCollider in barbell.Body.GetComponentsInChildren<Collider>(true))
            {
                foreach (Collider env in _equipment.Colliders)
                {
                    // The shaft rests on the rest tops: touching, not inside.
                    float d = Penetration(barCollider, env);
                    if (d > 0.002f)
                        throw new InvalidOperationException($"Bar collider {barCollider.name} penetrates {env.name} by {d * 1000f:F1} mm at tick zero.");
                    worst = Mathf.Max(worst, d);
                }
            }
            TickZeroMaxPenetrationM = worst;

            // Self-collision among segments that are allowed to collide.
            var segments = new List<PhysicalAthleteRig.SegmentRuntime>(athleteRig.Segments.Values);
            TickZeroSelfPenetrations.Clear();
            for (int i = 0; i < segments.Count; i++)
            {
                for (int j = i + 1; j < segments.Count; j++)
                {
                    Collider a = segments[i].Collider, b = segments[j].Collider;
                    if (Physics.GetIgnoreCollision(a, b))
                        continue;
                    float d = Penetration(a, b);
                    if (d > 0f)
                        TickZeroSelfPenetrations.Add($"{segments[i].Recipe.Id}-{segments[j].Recipe.Id} {d * 1000f:F1} mm");
                }
            }
            foreach (Collider bar in barbell.Body.GetComponentsInChildren<Collider>(true))
            {
                foreach (PhysicalAthleteRig.SegmentRuntime segment in segments)
                {
                    if (Physics.GetIgnoreCollision(bar, segment.Collider))
                        continue;
                    float d = Penetration(bar, segment.Collider);
                    if (d > 0f)
                        TickZeroSelfPenetrations.Add($"bar:{bar.name}-{segment.Recipe.Id} {d * 1000f:F1} mm");
                }
            }
            if (worst > 0.004f)
                throw new InvalidOperationException($"Supine setup penetrates the equipment by {worst * 1000f:F1} mm.");

            foreach (PhysicalAthleteRig.JointRuntime joint in athleteRig.Joints)
            {
                Vector3 a = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 b = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
                if (Vector3.Distance(a, b) > 0.0005f)
                    throw new InvalidOperationException($"Joint {joint.Recipe.ChildId} anchors separate after supine placement.");
            }

            TickZeroMaxGripSeparationM = Mathf.Max(_leftGrip.AnchorSeparationM, _rightGrip.AnchorSeparationM);
            if (TickZeroMaxGripSeparationM > 0.0005f)
                throw new InvalidOperationException("Grip anchors do not coincide at attach.");
        }

        private static float Penetration(Collider a, Collider b)
        {
            if (a == null || b == null)
                return 0f;
            return Physics.ComputePenetration(a, a.transform.position, a.transform.rotation, b, b.transform.position, b.transform.rotation,
                out _, out float distance) ? distance : 0f;
        }

        // ------------------------------------------------------------------
        // Player / test API

        public void RequestLiftOff() => _controller?.RequestLiftOff();

        public void RequestRerack() => _controller?.RequestRerack();

        public void RequestAbort() => _abortRequested = true;

        public void ReloadWithLoad(float kg)
        {
            SetPendingLoad(kg);
            SceneManager.LoadScene(SceneName, LoadSceneMode.Single);
        }

        // ------------------------------------------------------------------
        // Post-physics: the only observer

        private void OnPostPhysicsStep(SimulationTime time, PhysicalObservation observation, PlayerIntentFrame intent)
        {
            double dt = SimulationConstants.FixedDeltaTimeSeconds;
            foreach (BenchContactSensor sensor in _sensors.Values)
                sensor.CompleteTick(dt);
            athleteRig.PoweredController.CapturePostPhysicsDiagnostics();

            BenchObservation o = BuildObservation(time);
            LastObservation = o;
            _controller.LastObservation = o;
            _controller.HasObservation = true;

            bool abortEdge = (intent.AbortHeld && !_previousAbort) || _abortRequested;
            _previousAbort = intent.AbortHeld;
            _abortRequested = false;

            BenchCommand command = BenchCommand.None;
            if (!_referee.IsComplete)
            {
                command = abortEdge && _referee.State != BenchRefereeState.AwaitingLiftOff ? _referee.Abort(o) : _referee.Observe(o);
                if (_referee.IsComplete && NeedsSpotter())
                    BeginSpotter();
            }
            else if (abortEdge && !AttemptFinished && !SpotterActive && _controller.Phase != BenchMotionPhase.Reracking)
            {
                BeginSpotter();
            }

            UpdateSpotterTarget();
            UpdateRerackCompletion();
            RecordTelemetry(time, o, command);
            if (command != BenchCommand.None)
                CommandIssued?.Invoke(command);
        }

        private bool NeedsSpotter()
        {
            if (_referee.IsGoodLift)
                return false;
            foreach (BenchFaultRecord fault in _referee.Faults)
            {
                switch (fault.Fault)
                {
                    case BenchFault.NotLockedOut:
                    case BenchFault.BarOnSafeties:
                    case BenchFault.GripLost:
                    case BenchFault.NotMotionlessOnChest:
                    case BenchFault.BarOnBelt:
                    case BenchFault.AttemptAborted:
                        return true;
                }
            }
            return false;
        }

        private void BeginSpotter()
        {
            if (_spotter != null)
                return;
            ConfigurableJoint joint = barbell.Body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = null;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = barbell.Body.position;
            joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
            joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;
            var drive = new JointDrive { positionSpring = SpotterSpringNpm, positionDamper = SpotterDamperNspm, maximumForce = SpotterMaxForceN, useAcceleration = false };
            joint.xDrive = drive;
            joint.yDrive = drive;
            joint.zDrive = drive;
            joint.targetPosition = Vector3.zero;
            joint.projectionMode = JointProjectionMode.None;
            _spotter = joint;
            _controller.BeginSpotterRescue();
        }

        private void UpdateSpotterTarget()
        {
            if (_spotter == null)
                return;
            bool onLift = _controller.Phase == BenchMotionPhase.Reracking || _controller.Phase == BenchMotionPhase.Released;
            Vector3 target = onLift ? _model.LiftPath.BarCenter(_controller.LiftS) : _model.PressPath.BarCenter(_controller.PressS);
            // The world anchor moves along the arms' own reference path: the
            // spotters guide the bar where the lifter's arms are taking it.
            _spotter.connectedAnchor = target;
        }

        private void UpdateRerackCompletion()
        {
            if (AttemptFinished || _controller.Phase != BenchMotionPhase.Reracking || _controller.LiftS > 0.001f)
            {
                _restCarryTicks = 0;
                return;
            }
            _restCarryTicks = _controller.RestsCarryBar || (_spotter != null && LastObservation.BarRackForceN > 20.0) ? _restCarryTicks + 1 : 0;
            if (_restCarryTicks < 15)
                return;

            _leftGrip.Release();
            _rightGrip.Release();
            if (_spotter != null)
            {
                Destroy(_spotter);
                _spotter = null;
            }
            _controller.MarkReleased();
            AttemptFinished = true;
            if (!_referee.IsComplete)
                _referee.Observe(LastObservation);
        }

        private BenchObservation BuildObservation(SimulationTime time)
        {
            Rigidbody bar = barbell.Body;
            IReadOnlyDictionary<string, PhysicalAthleteRig.SegmentRuntime> s = athleteRig.Segments;
            PoweredJointController pc = athleteRig.PoweredController;

            Vector3 leftShoulder = JointAnchor(pc, "left_upper_arm");
            Vector3 rightShoulder = JointAnchor(pc, "right_upper_arm");
            Vector3 leftElbow = JointAnchor(pc, "left_forearm");
            Vector3 rightElbow = JointAnchor(pc, "right_forearm");
            Vector3 shoulderMid = 0.5f * (leftShoulder + rightShoulder);
            Vector3 barPos = bar.position;
            float offset = new Vector2(barPos.x - shoulderMid.x, barPos.z - shoulderMid.z).magnitude;

            Vector3 leftEnd = bar.transform.TransformPoint(new Vector3(-BarbellPrototypeConfiguration.CollarFaceXBarMeters, 0f, 0f));
            Vector3 rightEnd = bar.transform.TransformPoint(new Vector3(BarbellPrototypeConfiguration.CollarFaceXBarMeters, 0f, 0f));

            double head = _sensors["head_neck"].NormalForce(BenchSurface.BenchPad);
            double shoulders = _sensors["thorax"].NormalForce(BenchSurface.BenchPad);
            double buttocks = _sensors["pelvis"].NormalForce(BenchSurface.BenchPad);
            BenchContactSensor lf = _sensors["left_foot"], rf = _sensors["right_foot"];
            double feetOnBench = lf.NormalForce(BenchSurface.BenchPad) + lf.NormalForce(BenchSurface.BenchFrame) +
                rf.NormalForce(BenchSurface.BenchPad) + rf.NormalForce(BenchSurface.BenchFrame);

            Vector3 li = s["left_hand"].Body.transform.TransformPoint(_model.LeftIndexLocal);
            Vector3 ri = s["right_hand"].Body.transform.TransformPoint(_model.RightIndexLocal);
            Vector3 normalTarget = (Quaternion.AngleAxis(-BenchSetupSolver.PalmUpTiltDeg, Vector3.right) * Vector3.forward).normalized;
            bool reverse =
                Vector3.Dot(s["left_hand"].Body.transform.TransformDirection(_model.LeftPalmNormalLocal), normalTarget) < 0f ||
                Vector3.Dot(s["right_hand"].Body.transform.TransformDirection(_model.RightPalmNormalLocal), normalTarget) < 0f;

            return new BenchObservation(
                time.Tick,
                time.SimulationTimeSeconds,
                barPos.y,
                offset,
                bar.linearVelocity.magnitude,
                bar.GetPointVelocity(leftEnd).y,
                bar.GetPointVelocity(rightEnd).y,
                leftEnd.y,
                rightEnd.y,
                ElbowFlexDeg(pc, "left_forearm"),
                ElbowFlexDeg(pc, "right_forearm"),
                leftElbow.y - ElbowUndersideRadiusM <= leftShoulder.y + ShoulderTopRadiusM,
                rightElbow.y - ElbowUndersideRadiusM <= rightShoulder.y + ShoulderTopRadiusM,
                head,
                shoulders,
                buttocks,
                lf.NormalForce(BenchSurface.Floor),
                rf.NormalForce(BenchSurface.Floor),
                Vector3.Angle(s["left_foot"].Body.transform.up, Vector3.up) <= FootFlatMaxTiltDeg,
                Vector3.Angle(s["right_foot"].Body.transform.up, Vector3.up) <= FootFlatMaxTiltDeg,
                feetOnBench,
                _barSensor.NormalForce(BenchSurface.AthleteThorax) + _barSensor.NormalForce(BenchSurface.AthleteAbdomen),
                _barSensor.NormalForce(BenchSurface.AthletePelvis),
                _barSensor.NormalForce(BenchSurface.BarRest) + _barSensor.NormalForce(BenchSurface.Upright),
                _barSensor.NormalForce(BenchSurface.Safety),
                _leftGrip.IsAttached,
                _rightGrip.IsAttached,
                _leftGrip.LateralSlipM,
                _rightGrip.LateralSlipM,
                Mathf.Abs(ri.x - li.x),
                reverse,
                _spotter != null,
                s["thorax"].Body.linearVelocity.y);
        }

        private static Vector3 JointAnchor(PoweredJointController pc, string id)
        {
            ConfigurableJoint joint = pc.GetJoint(id).Joint;
            return joint.transform.TransformPoint(joint.anchor);
        }

        private static float ElbowFlexDeg(PoweredJointController pc, string id)
        {
            PoweredJointController.PoweredJointRuntime joint = pc.GetJoint(id);
            Quaternion parentToChild = Quaternion.Inverse(joint.Joint.connectedBody.rotation) * joint.Joint.transform.rotation;
            Quaternion logical = PoweredJointController.ToLogicalTargetRotation(joint.NeutralParentToChild, joint.JointSpace, parentToChild);
            return PoweredJointController.SignedTwistRadians(logical, Vector3.right) * Mathf.Rad2Deg;
        }

        private void RecordTelemetry(SimulationTime time, in BenchObservation o, BenchCommand command)
        {
            Rigidbody bar = barbell.Body;
            Vector3 v = bar.linearVelocity;
            Vector3 a = (v - _previousBarVelocity) / (float)SimulationConstants.FixedDeltaTimeSeconds;
            _previousBarVelocity = v;

            var commanded = new float[6];
            var realized = new float[6];
            var demand = new float[6];
            var torque = new float[6];
            for (int i = 0; i < 6; i++)
            {
                PoweredJointController.PoweredJointRuntime joint = athleteRig.PoweredController.GetJoint(BenchSetupSolver.ArmJointIds[i]);
                PoweredJointDiagnostic d = joint.HasPostPhysicsDiagnostic ? joint.PostPhysicsDiagnostic : joint.Diagnostic;
                commanded[i] = Quaternion.Angle(Quaternion.identity, d.AppliedTarget);
                realized[i] = Quaternion.Angle(Quaternion.identity, d.ActualRelative);
                if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                {
                    commanded[i] = PoweredJointController.SignedTwistRadians(d.AppliedTarget, Vector3.right) * Mathf.Rad2Deg;
                    realized[i] = PoweredJointController.SignedTwistRadians(d.ActualRelative, Vector3.right) * Mathf.Rad2Deg;
                }
                demand[i] = d.ModeledDemand;
                torque[i] = d.SolverTorqueJointSpaceNm.magnitude;
            }

            _telemetry.Add(new BenchTelemetrySample
            {
                Tick = time.Tick,
                TimeS = time.SimulationTimeSeconds,
                Phase = _controller.Phase,
                RefereeState = _referee.State,
                Command = command,
                LiftS = _controller.LiftS,
                PressS = _controller.PressS,
                BarPosition = bar.position,
                BarVelocity = v,
                BarAcceleration = a,
                BarAngularVelocity = bar.angularVelocity,
                LeftHandForceRaw = _leftGrip.SolverForceRaw,
                RightHandForceRaw = _rightGrip.SolverForceRaw,
                LeftHandTorqueRaw = _leftGrip.SolverTorqueRaw,
                RightHandTorqueRaw = _rightGrip.SolverTorqueRaw,
                BarContactForceWorld = BarContactForce(),
                LeftGripSeparationM = _leftGrip.IsAttached ? _leftGrip.AnchorSeparationM : 0f,
                RightGripSeparationM = _rightGrip.IsAttached ? _rightGrip.AnchorSeparationM : 0f,
                LeftSlipM = (float)o.LeftHandSlipM,
                RightSlipM = (float)o.RightHandSlipM,
                HeadN = (float)o.HeadBenchForceN,
                ShouldersN = (float)o.ShouldersBenchForceN,
                AbdomenN = (float)_sensors["abdomen"].NormalForce(BenchSurface.BenchPad),
                ButtocksN = (float)o.ButtocksBenchForceN,
                LeftFootN = (float)o.LeftFootFloorForceN,
                RightFootN = (float)o.RightFootFloorForceN,
                FeetOnBenchN = (float)o.FeetOnBenchForceN,
                BarChestN = (float)o.BarChestForceN,
                BarBeltN = (float)o.BarBeltForceN,
                BarRackN = (float)o.BarRackForceN,
                BarSafetyN = (float)o.BarSafetyForceN,
                LeftElbowFlexDeg = (float)o.LeftElbowFlexionDeg,
                RightElbowFlexDeg = (float)o.RightElbowFlexionDeg,
                ArmCommandedDeg = commanded,
                ArmRealizedDeg = realized,
                ArmDemand = demand,
                ArmSolverTorqueNm = torque,
                SpotterActive = _spotter != null
            });
        }

        /// <summary>Sum of every contact force the environment and body apply to the bar this tick, world frame.</summary>
        public Vector3 BarContactForce()
        {
            Vector3 sum = Vector3.zero;
            foreach (BenchSurface surface in (BenchSurface[])Enum.GetValues(typeof(BenchSurface)))
                sum += _barSensor.ForceVector(surface);
            return sum;
        }
    }
}
