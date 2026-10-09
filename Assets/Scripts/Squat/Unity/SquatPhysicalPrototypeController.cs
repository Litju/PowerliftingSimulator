using System;
using System.Collections.Generic;
using System.Globalization;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Squat.Unity
{
    // Ahead of PhysicalAthleteRig, so the athlete is registered to the
    // platform before the rig builds any body from the authored pose.
    [DefaultExecutionOrder(-950)]
    [DisallowMultipleComponent]
    public sealed class SquatPhysicalPrototypeController : MonoBehaviour
    {
        [SerializeField] private FoundationBootstrap foundation;
        [SerializeField] private PhysicalAthleteRig athleteRig;
        [SerializeField] private PhysicalBarbell barbell;
        [SerializeField] private float barLoadKg = 25f;
        [SerializeField] private bool autoSquatOnStart = false;
        [SerializeField] private bool attachBarSaddle = true;
        [SerializeField] private bool showDebugGui;

        private const float PlantarSymmetryToleranceM = 0.001f;

        private SquatPhysicalAdapter _adapter;
        private SquatBarSaddle _saddle;
        private PhysicalFootContactDetector _leftFootContact;
        private PhysicalFootContactDetector _rightFootContact;
        private SquatObservationCollector _observationCollector;
        private SquatAttemptOrchestrator _attemptOrchestrator;
        private bool _isInitialized;
        private bool _tickZeroSubstrateValidated;
        private ulong _tickZeroValidationTick = ulong.MaxValue;
        private int _suppressedInitialPenetrationPairCount;
        private float _maximumSuppressedInitialPenetrationMeters;
        private readonly List<string> _suppressedInitialPenetrationPairs = new List<string>();
        private float _selectedLoadKg = 25f;
        private string _startupFailure = string.Empty;
        private GUIStyle _hudLabelStyle;
        private GUIStyle _hudTitleStyle;
        private GUIStyle _playerTitleStyle;
        private GUIStyle _playerBodyStyle;
        private GUIStyle _playerResultStyle;
        private GUIStyle _playerButtonStyle;
        private GUIStyle _playerControlsStyle;

        public SquatPhysicalAdapter Adapter => _adapter;
        public SquatBarSaddle Saddle => _saddle;
        public PhysicalFootContactDetector LeftFootContact => _leftFootContact;
        public PhysicalFootContactDetector RightFootContact => _rightFootContact;
        public SquatObservationCollector ObservationCollector => _observationCollector;
        public PhysicalAthleteRig AthleteRig => athleteRig;
        public SquatTrace ObservationTrace => _observationCollector == null ? null : _observationCollector.Trace;
        public SquatAttemptOrchestrator AttemptOrchestrator => _attemptOrchestrator;
        public SquatAttemptLifecycle AttemptLifecycle => _attemptOrchestrator == null ? null : _attemptOrchestrator.Lifecycle;
        public SquatAttemptRecord AttemptRecord => _attemptOrchestrator == null ? null : _attemptOrchestrator.Record;
        public float CurrentLoadKg => _selectedLoadKg;
        public bool IsInitialized => _isInitialized;
        public bool TickZeroSubstrateValidated => _tickZeroSubstrateValidated;
        public ulong TickZeroValidationTick => _tickZeroValidationTick;
        public int SuppressedInitialPenetrationPairCount => _suppressedInitialPenetrationPairCount;
        public float MaximumSuppressedInitialPenetrationMeters => _maximumSuppressedInitialPenetrationMeters;
        public IReadOnlyList<string> SuppressedInitialPenetrationPairs => _suppressedInitialPenetrationPairs;
        public bool RuntimeWired => _isInitialized && foundation != null && athleteRig != null &&
            barbell != null && _adapter != null && (_selectedLoadKg <= 0f || _saddle != null);
        public bool CanRequestOneStepWalkoutIntent => _isInitialized && _selectedLoadKg == 25f &&
            _attemptOrchestrator != null && !_attemptOrchestrator.HasStarted &&
            _adapter != null && _adapter.CanBeginOneStepWalkout;
        public string StartupFailure => _startupFailure;

        private void Awake()
        {
            _selectedLoadKg = Mathf.Max(0f, barLoadKg);
            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (foundation != null && foundation.Runtime != null && foundation.Runtime.IsInitialized)
                foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
        }

        private void Start()
        {
            Initialize();
        }

        public void Initialize()
        {
            if (_isInitialized)
                return;

            if (foundation == null)
                foundation = FindFirstObjectByType<FoundationBootstrap>();
            if (athleteRig == null)
                athleteRig = FindFirstObjectByType<PhysicalAthleteRig>();
            if (barbell == null)
                barbell = FindFirstObjectByType<PhysicalBarbell>();

            if (foundation == null || foundation.Runtime == null || !foundation.Runtime.IsInitialized)
            {
                _startupFailure = "Foundation runtime is not initialized.";
                return;
            }
            foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            if (athleteRig == null)
            {
                _startupFailure = "PhysicalAthleteRig is missing.";
                return;
            }

            if (athleteRig.PoweredController == null)
            {
                RegisterAthleteToGround();
                athleteRig.Build();
            }

            _adapter = new SquatPhysicalAdapter(athleteRig);
            AddFootContactDetectors();
            _adapter.SetFootContactDetectors(_leftFootContact, _rightFootContact);

            if (barbell != null)
            {
                PrepareBarbellSpawn();
                if (!barbell.IsBuilt)
                    barbell.Build();
                barbell.SetGameplayPerformanceProfile(true);
                ApplySelectedLoadWithoutSaddle();
            }

            if (attachBarSaddle && _selectedLoadKg > 0f)
                EnsureSaddle();

            _adapter.Reset();
            _adapter.SetSaddle(_saddle);
            athleteRig.SetGameplayPerformanceProfile(true);
            _adapter.CaptureStandingComReference();
            athleteRig.PrimeCommandSource();
            ValidateInitialSubstrate();

            _observationCollector = new SquatObservationCollector(
                foundation.Runtime,
                athleteRig,
                barbell,
                _adapter,
                _leftFootContact,
                _rightFootContact);
            _attemptOrchestrator = new SquatAttemptOrchestrator(
                _observationCollector,
                _adapter);

            if (autoSquatOnStart)
                _attemptOrchestrator.BeginAttempt();
            foundation.Runtime.MarkInitialPhysicalStateValidated();

            _isInitialized = true;
            _startupFailure = string.Empty;
        }

        /// <summary>
        /// Places the authored athlete so the canonical sole rests on the
        /// platform before any body exists. The plantar plane comes from the
        /// GAM-10 reference calibration, which owns that definition; this only
        /// consumes it.
        /// </summary>
        private void RegisterAthleteToGround()
        {
            Animator reference = athleteRig.ReferenceAnimator;
            if (reference == null)
                throw new InvalidOperationException("Ground registration requires the canonical reference animator.");

            SquatReferenceRigCalibration calibration = SquatReferenceRigCalibration.Build(
                reference,
                reference.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");

            float leftPlantarY = calibration.LeftFoot.PlantarAnchorWorld.y;
            float rightPlantarY = calibration.RightFoot.PlantarAnchorWorld.y;
            float asymmetry = Mathf.Abs(leftPlantarY - rightPlantarY);
            if (asymmetry > PlantarSymmetryToleranceM)
            {
                throw new InvalidOperationException(
                    $"The canonical plantar anchors are asymmetric by {asymmetry * 1000f:F2} mm (left {leftPlantarY:F4}, right {rightPlantarY:F4}). Ground registration would tilt the athlete; fix the reference calibration first.");
            }

            athleteRig.RegisterCanonicalGround(0.5f * (leftPlantarY + rightPlantarY));
        }

        private void AddFootContactDetectors()
        {
            if (athleteRig.Segments.TryGetValue("left_foot", out PhysicalAthleteRig.SegmentRuntime leftFoot) && leftFoot.Body != null)
            {
                _leftFootContact = leftFoot.Body.gameObject.GetComponent<PhysicalFootContactDetector>();
                if (_leftFootContact == null)
                    _leftFootContact = leftFoot.Body.gameObject.AddComponent<PhysicalFootContactDetector>();
            }
            if (athleteRig.Segments.TryGetValue("right_foot", out PhysicalAthleteRig.SegmentRuntime rightFoot) && rightFoot.Body != null)
            {
                _rightFootContact = rightFoot.Body.gameObject.GetComponent<PhysicalFootContactDetector>();
                if (_rightFootContact == null)
                    _rightFootContact = rightFoot.Body.gameObject.AddComponent<PhysicalFootContactDetector>();
            }
        }

        private void Update()
        {
            if (!_isInitialized)
            {
                Initialize();
                return;
            }

            int ticks = foundation.Runtime.LastCatchUpTicks;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int tick = 0; tick < ticks; tick++)
            {
                if (_leftFootContact != null)
                    _leftFootContact.PhysicsTickUpdate(dt);
                if (_rightFootContact != null)
                    _rightFootContact.PhysicsTickUpdate(dt);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (!_attemptOrchestrator.HasStarted && keyboard.digit1Key.wasPressedThisFrame)
                SetLoad(0f);
            else if (!_attemptOrchestrator.HasStarted && keyboard.digit2Key.wasPressedThisFrame)
                SetLoad(25f);
            else if (!_attemptOrchestrator.HasStarted && keyboard.digit3Key.wasPressedThisFrame)
                SetLoad(105f);
            else if (keyboard.fKey.wasPressedThisFrame && !_attemptOrchestrator.HasStarted &&
                _adapter.OneStepWalkoutState == SquatOneStepWalkoutState.BILATERAL_STANDING)
                BeginAttempt();
            else if (keyboard.escapeKey.wasPressedThisFrame &&
                _attemptOrchestrator.HasSquatCommand && !_attemptOrchestrator.IsTruthFrozen)
                AbortAttempt();
            else if (keyboard.bKey.wasPressedThisFrame)
                ReleaseSaddleForFailureInspection();
            else if (keyboard.rKey.wasPressedThisFrame)
                ResetPrototype();
        }

        public void SetLoad(float loadKg)
        {
            if (!_isInitialized || barbell == null)
                return;
            if (_observationCollector != null && _observationCollector.IsRecording)
                throw new InvalidOperationException("The squat load cannot change while an observation trace is recording.");
            if (_attemptOrchestrator != null && _attemptOrchestrator.HasStarted)
                throw new InvalidOperationException("The squat load cannot change after an attempt lifecycle has started; reset the scene first.");

            _selectedLoadKg = Mathf.Max(0f, loadKg);
            if (_saddle != null)
            {
                _saddle.BreakSaddle();
                _saddle = null;
            }

            foundation.Reset();
            foundation.Runtime.HoldPhysicsUntilInitialStateValidated();
            _leftFootContact?.ResetContact();
            _rightFootContact?.ResetContact();
            if (_observationCollector != null && !_observationCollector.IsRecording)
                _observationCollector.Clear();
            if (_selectedLoadKg <= 0f)
            {
                if (barbell.Body != null)
                    barbell.Body.gameObject.SetActive(false);
            }
            else
            {
                if (barbell.Body != null)
                    barbell.Body.gameObject.SetActive(true);
                barbell.ConfigureLoad(_selectedLoadKg);
                EnsureSaddle();
            }

            _adapter.Reset();
            _adapter.SetSaddle(_saddle);
            athleteRig.SetCommandSource(_adapter);
            _adapter.CaptureStandingComReference();
            athleteRig.PrimeCommandSource();
            ValidateInitialSubstrate();
            foundation.Runtime.MarkInitialPhysicalStateValidated();
        }

#if UNITY_EDITOR
        public void BeginPhysicalSquatMotionForQualification()
        {
            if (!_isInitialized || _adapter == null || !_adapter.HasStandingComReference)
                throw new InvalidOperationException("A validated physical setup is required before the mechanics probe.");
            if (_attemptOrchestrator == null || _attemptOrchestrator.HasStarted)
                throw new InvalidOperationException("The mechanics probe must not start a squat attempt lifecycle.");

            _adapter.BeginIntentDrivenSquat();
        }
#endif

        private void ValidateInitialSubstrate()
        {
            _tickZeroSubstrateValidated = false;
            FoundationRuntime runtime = foundation.Runtime;
            if (runtime.CurrentTime.Tick != 0ul)
                throw new InvalidOperationException("The complete physical substrate must validate at tick 0 before simulation.");
            if (athleteRig.Segments.Count != 16 || athleteRig.Joints.Count != 15 ||
                Mathf.Abs(athleteRig.TotalMassKg - PhysicalAthleteDefinition.PrototypeBodyMassKg) > 0.0001f ||
                athleteRig.MaxInitialNonAdjacentPenetrationMeters > InitialPenetrationToleranceM)
                throw new InvalidOperationException("The athlete topology, mass, or neutral collision state failed tick-0 validation.");

            foreach (PhysicalAthleteRig.SegmentRuntime segment in athleteRig.Segments.Values)
            {
                Rigidbody body = segment.Body;
                if (body == null || body.gameObject.scene != runtime.AuthoritativeScene ||
                    body.isKinematic || !body.useGravity || body.GetComponents<Rigidbody>().Length != 1 ||
                    segment.Collider == null || !segment.Collider.enabled || segment.Collider.isTrigger ||
                    segment.Collider.attachedRigidbody != body ||
                    !Finite(body.position) || !Finite(body.rotation) || !Finite(body.centerOfMass) ||
                    !Finite(body.inertiaTensor) || !Finite(body.inertiaTensorRotation) ||
                    !Finite(body.linearVelocity) || !Finite(body.angularVelocity) ||
                    body.linearVelocity.magnitude > InitialVelocityToleranceMps ||
                    body.angularVelocity.magnitude > InitialVelocityToleranceMps ||
                    body.mass <= 0f || body.inertiaTensor.x <= 0f || body.inertiaTensor.y <= 0f || body.inertiaTensor.z <= 0f)
                    throw new InvalidOperationException($"Athlete segment '{segment.Recipe.Id}' is invalid at tick 0.");
            }

            foreach (PhysicalAthleteRig.JointRuntime runtimeJoint in athleteRig.Joints)
            {
                ConfigurableJoint joint = runtimeJoint.Joint;
                if (joint == null || joint.connectedBody == null)
                    throw new InvalidOperationException($"Athlete joint '{runtimeJoint.Recipe.ChildId}' is missing a connected body.");
                Vector3 childAnchor = joint.transform.TransformPoint(joint.anchor);
                Vector3 parentAnchor = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                if (!Finite(childAnchor) || !Finite(parentAnchor) ||
                    Vector3.Distance(childAnchor, parentAnchor) > PhysicalAthleteDefinition.AnchorToleranceMeters)
                    throw new InvalidOperationException($"Athlete joint '{runtimeJoint.Recipe.ChildId}' failed tick-0 anchor validation.");
            }

            ValidateBarbellInitialState(runtime);
            ValidateSaddleInitialState();
            ValidateInitialColliderPenetrations();

            if (athleteRig.PoweredController.Mode != PoweredAthleteMode.Controlled)
                throw new InvalidOperationException("The canonical standing command was not primed before tick 1.");
            athleteRig.PoweredController.ValidateAppliedDrives();
            foreach (PoweredJointController.PoweredJointRuntime joint in athleteRig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue)
                    continue;
                if (joint.LastCommandTick != 0ul ||
                    !PoweredJointController.IsFinite(joint.AppliedTarget) ||
                    !PoweredJointController.IsFinite(joint.Joint.targetRotation) ||
                    !PoweredJointController.IsFinite(joint.Joint.targetAngularVelocity))
                    throw new InvalidOperationException($"Powered joint '{joint.Id}' was not primed with a finite tick-0 target.");
            }

            _tickZeroValidationTick = runtime.CurrentTime.Tick;
            _tickZeroSubstrateValidated = true;
        }

        private void ValidateBarbellInitialState(FoundationRuntime runtime)
        {
            if (barbell == null || barbell.Body == null || barbell.LoadPlan == null || barbell.InertiaModel == null)
                throw new InvalidOperationException("The authoritative barbell is not fully constructed at tick 0.");

            Rigidbody body = barbell.Body;
            BarbellInertiaModel model = barbell.InertiaModel;
            Collider[] colliders = body.GetComponentsInChildren<Collider>(true);
            Rigidbody[] bodies = body.GetComponentsInChildren<Rigidbody>(true);
            var failures = new List<string>();
            if (bodies.Length != 1 || bodies[0] != body || colliders.Length == 0)
                failures.Add("single-body collider topology");
            if (body.gameObject.scene != runtime.AuthoritativeScene || body.isKinematic || !body.useGravity ||
                body.automaticCenterOfMass || body.automaticInertiaTensor)
                failures.Add("authoritative dynamic/gravity state");
            if (!Finite(body.position) || !Finite(body.rotation) || !Finite(body.centerOfMass) ||
                !Finite(body.inertiaTensor) || !Finite(body.inertiaTensorRotation) ||
                !Finite(body.linearVelocity) || !Finite(body.angularVelocity) ||
                body.linearVelocity.magnitude > InitialVelocityToleranceMps ||
                body.angularVelocity.magnitude > InitialVelocityToleranceMps)
                failures.Add("finite zero-velocity state");
            float massError = Mathf.Abs(body.mass - barbell.LoadPlan.TotalMassKg);
            float requestedMassError = Mathf.Abs(barbell.LoadPlan.TotalMassKg - barbell.LoadPlan.RequestedTotalMassKg);
            float comError = Vector3.Distance(body.centerOfMass, model.CenterOfMassBarMeters);
            float inertiaError = body.gameObject.activeInHierarchy
                ? Vector3.Distance(body.inertiaTensor, model.InertiaTensorKgM2)
                : 0f;
            float inertiaRotationError = Quaternion.Angle(body.inertiaTensorRotation, Quaternion.identity);
            if (massError > 0.001f || requestedMassError > 0.001f || comError > 0.0001f ||
                !Finite(model.InertiaTensorKgM2) || model.InertiaTensorKgM2.x <= 0f ||
                model.InertiaTensorKgM2.y <= 0f || model.InertiaTensorKgM2.z <= 0f ||
                inertiaError > 0.0001f || inertiaRotationError > 0.001f)
                failures.Add("authored mass/COM/compound inertia");
            if (failures.Count > 0)
                throw new InvalidOperationException(
                    $"Barbell tick-0 contract failed: {string.Join(", ", failures)}; " +
                    $"mass={body.mass:R}/{barbell.LoadPlan.TotalMassKg:R}kg massError={massError:R}kg " +
                    $"requested={barbell.LoadPlan.RequestedTotalMassKg:R}kg requestedError={requestedMassError:R}kg " +
                    $"COM={body.centerOfMass} modelCOM={model.CenterOfMassBarMeters} error={comError:R}m " +
                    $"inertia={body.inertiaTensor} modelInertia={model.InertiaTensorKgM2} error={inertiaError:R}kgm2 " +
                    $"inertiaRotationError={inertiaRotationError:R}deg.");

            if (_selectedLoadKg > 0f &&
                (!body.gameObject.activeInHierarchy || Mathf.Abs(body.mass - _selectedLoadKg) > 0.001f))
                throw new InvalidOperationException("The selected canonical load does not match the active barbell mass.");
            if (_selectedLoadKg <= 0f && body.gameObject.activeInHierarchy)
                throw new InvalidOperationException("The unloaded standing setup must not leave a bar active.");
            foreach (Collider collider in colliders)
            {
                bool insideBarRoot = collider.transform == body.transform || collider.transform.IsChildOf(body.transform);
                if (!insideBarRoot ||
                    (collider.gameObject.activeInHierarchy &&
                     (collider.attachedRigidbody != body || !Finite(collider.bounds.min) || !Finite(collider.bounds.max))))
                    throw new InvalidOperationException(
                        $"Barbell collider '{collider.name}' failed ownership/finiteness validation: " +
                        $"insideRoot={insideBarRoot}, active={collider.gameObject.activeInHierarchy}, enabled={collider.enabled}, " +
                        $"attachedBody={(collider.attachedRigidbody != null ? collider.attachedRigidbody.name : "none")}, " +
                        $"root={body.name}, boundsMin={collider.bounds.min}, boundsMax={collider.bounds.max}.");
            }
        }

        private void ValidateSaddleInitialState()
        {
            if (_selectedLoadKg <= 0f)
            {
                if (_saddle != null)
                    throw new InvalidOperationException("An unloaded standing setup must not retain a bar saddle.");
                return;
            }

            float configuredLinearLimitM = SquatBarSaddle.ExperimentalOverride?.LinearLimitM ??
                SquatBarSaddle.DefaultLinearLimitM;

            if (_saddle == null || !_saddle.IsAttached || _saddle.IsBroken || _saddle.Joint == null ||
                !athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax) ||
                _saddle.Joint.connectedBody != thorax.Body ||
                _saddle.Joint.GetComponent<Rigidbody>() != barbell.Body ||
                _saddle.Joint.projectionMode != JointProjectionMode.None ||
                _saddle.Joint.enableCollision ||
                _saddle.Joint.xMotion != ConfigurableJointMotion.Limited ||
                _saddle.Joint.yMotion != ConfigurableJointMotion.Limited ||
                _saddle.Joint.zMotion != ConfigurableJointMotion.Limited ||
                !float.IsFinite(_saddle.Joint.breakForce) || _saddle.Joint.breakForce <= 0f ||
                !float.IsFinite(_saddle.Joint.breakTorque) || _saddle.Joint.breakTorque <= 0f ||
                !float.IsFinite(configuredLinearLimitM) || configuredLinearLimitM <= 0f ||
                !float.IsFinite(_saddle.Joint.linearLimit.limit) ||
                Mathf.Abs(_saddle.Joint.linearLimit.limit - configuredLinearLimitM) > 0.000001f ||
                _saddle.InitialAnchorErrorMeters > SquatBarSaddle.InitialAnchorToleranceM ||
                _saddle.SaddleSeparationMeters > SquatBarSaddle.InitialAnchorToleranceM ||
                _saddle.CurrentLinearLimitOccupancy > 0.01f ||
                !float.IsFinite(_saddle.RelativeRotationDegrees) ||
                !Finite(_saddle.CurrentForceEngine) || !Finite(_saddle.CurrentTorqueEngine))
                throw new InvalidOperationException("The bar/thorax saddle failed its finite aligned tick-0 contract.");
        }

        private void ValidateInitialColliderPenetrations()
        {
            var colliders = new List<Collider>(27);
            foreach (PhysicalAthleteRig.SegmentRuntime segment in athleteRig.Segments.Values)
                colliders.Add(segment.Collider);
            if (athleteRig.PlatformCollider == null)
                throw new InvalidOperationException("The authoritative support platform is missing.");
            colliders.Add(athleteRig.PlatformCollider);
            colliders.AddRange(barbell.Body.GetComponentsInChildren<Collider>(true));

            _suppressedInitialPenetrationPairCount = 0;
            _maximumSuppressedInitialPenetrationMeters = 0f;
            _suppressedInitialPenetrationPairs.Clear();
            for (int first = 0; first < colliders.Count; first++)
            {
                Collider a = colliders[first];
                if (!a.enabled || !a.gameObject.activeInHierarchy)
                    continue;
                for (int second = first + 1; second < colliders.Count; second++)
                {
                    Collider b = colliders[second];
                    if (!b.enabled || !b.gameObject.activeInHierarchy ||
                        (a.attachedRigidbody != null && a.attachedRigidbody == b.attachedRigidbody))
                        continue;

                    bool collisionSuppressed = Physics.GetIgnoreCollision(a, b) || IsCollisionSuppressedByJoint(a, b);
                    if (!Physics.ComputePenetration(
                        a, a.transform.position, a.transform.rotation,
                        b, b.transform.position, b.transform.rotation,
                        out _, out float penetration) || penetration <= InitialPenetrationToleranceM)
                        continue;
                    if (collisionSuppressed)
                    {
                        _suppressedInitialPenetrationPairCount++;
                        _maximumSuppressedInitialPenetrationMeters = Mathf.Max(
                            _maximumSuppressedInitialPenetrationMeters, penetration);
                        _suppressedInitialPenetrationPairs.Add(
                            $"{a.name}|{b.name}|{penetration.ToString("R", CultureInfo.InvariantCulture)}");
                        continue;
                    }
                    throw new InvalidOperationException(
                        $"Active colliders '{a.name}' and '{b.name}' begin with {penetration * 1000f:F3} mm penetration.");
                }
            }
        }

        private bool IsCollisionSuppressedByJoint(Collider a, Collider b)
        {
            Rigidbody bodyA = a.attachedRigidbody;
            Rigidbody bodyB = b.attachedRigidbody;
            foreach (PhysicalAthleteRig.JointRuntime joint in athleteRig.Joints)
            {
                if (!joint.Joint.enableCollision && SameBodyPair(
                    bodyA, bodyB, joint.Joint.GetComponent<Rigidbody>(), joint.Joint.connectedBody))
                    return true;
            }
            return _saddle != null && _saddle.Joint != null && !_saddle.Joint.enableCollision &&
                SameBodyPair(bodyA, bodyB, _saddle.Joint.GetComponent<Rigidbody>(), _saddle.Joint.connectedBody);
        }

        private static bool SameBodyPair(Rigidbody a, Rigidbody b, Rigidbody first, Rigidbody second) =>
            (a == first && b == second) || (a == second && b == first);

        private const float InitialVelocityToleranceMps = 0.00001f;
        private const float InitialPenetrationToleranceM = 0.0001f;

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool Finite(Quaternion value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z) && float.IsFinite(value.w);

        public void BeginAttempt()
        {
            if (!_isInitialized || _attemptOrchestrator == null)
                throw new InvalidOperationException("The squat attempt lifecycle is not initialized.");
            if (_adapter.OneStepWalkoutState != SquatOneStepWalkoutState.BILATERAL_STANDING)
                throw new InvalidOperationException("The judged squat remains unavailable after this one-step walkout gate.");

            _attemptOrchestrator.BeginAttempt();
        }

        public void RequestOneStepWalkoutIntent()
        {
            if (!CanRequestOneStepWalkoutIntent)
                throw new InvalidOperationException("The 25 kg one-step Walkout intent requires loaded bilateral standing before an attempt starts.");

            _adapter.BeginOneStepWalkout(foundation.Runtime.CurrentTime.Tick);
        }

        public SquatAttemptRecord AbortAttempt()
        {
            if (!_isInitialized || _attemptOrchestrator == null)
                throw new InvalidOperationException("The squat attempt lifecycle is not initialized.");

            return _attemptOrchestrator.Abort();
        }

        public void ResetPrototype()
        {
            if (!_isInitialized)
                return;

            // A gameplay reset rebuilds the production scene so every dynamic
            // body returns to its authored spawn pose without writing a
            // physical transform or velocity from the control path.
            SceneManager.LoadScene(SceneManager.GetActiveScene().path, LoadSceneMode.Single);
        }

        public void SelectLoadFromPlayerUi(float loadKg) => SetLoad(loadKg);

        public void StartAttemptFromPlayerUi() => BeginAttempt();

        public void RetryFromPlayerUi() => ResetPrototype();

        private void ApplySelectedLoadWithoutSaddle()
        {
            if (barbell == null || barbell.Body == null)
                return;

            if (_selectedLoadKg <= 0f)
            {
                barbell.Body.gameObject.SetActive(false);
                return;
            }

            barbell.Body.gameObject.SetActive(true);
            barbell.ConfigureLoad(_selectedLoadKg);
        }

        private void EnsureSaddle()
        {
            if (!attachBarSaddle || _selectedLoadKg <= 0f || barbell == null || barbell.Body == null ||
                athleteRig == null || !barbell.Body.gameObject.activeInHierarchy)
                return;
            if (_saddle != null && _saddle.IsAttached)
                return;
            if (!athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax) || thorax.Body == null)
                return;

            _saddle = new SquatBarSaddle(barbell, thorax.Body);
            _adapter?.SetSaddle(_saddle);
        }

        private void PrepareBarbellSpawn()
        {
            if (!attachBarSaddle)
                return;
            if (!athleteRig.Segments.TryGetValue("thorax", out PhysicalAthleteRig.SegmentRuntime thorax) ||
                thorax.Body == null)
                throw new InvalidOperationException("Saddle spawn registration requires the built physical thorax.");

            Vector3 alignedPosition = SquatBarSaddle.AlignedBarRootPosition(thorax.Body, Quaternion.identity);
            if (!barbell.IsBuilt)
            {
                barbell.SetInitialSpawnPosition(alignedPosition);
                return;
            }

            Vector3 barAnchor = barbell.Body.transform.TransformPoint(SquatBarSaddle.BarLocalAnchor);
            Vector3 thoraxAnchor = thorax.Body.transform.TransformPoint(SquatBarSaddle.ThoraxLocalAnchor);
            float initialError = Vector3.Distance(barAnchor, thoraxAnchor);
            if (initialError > SquatBarSaddle.InitialAnchorToleranceM)
                throw new InvalidOperationException(
                    $"The barbell was built before saddle alignment and starts {initialError * 1000f:F3} mm away from the thorax anchor.");
        }

        private void ReleaseSaddleForFailureInspection()
        {
            if (_saddle == null)
                return;
            _saddle.BreakSaddle();
            _adapter?.SetSaddle(_saddle);
        }

        private void OnGUI()
        {
            EnsureHudStyles();
            DrawPlayerHud();
            if (showDebugGui)
                DrawDebugHud();
        }

        private void DrawPlayerHud()
        {
            GUILayout.BeginArea(new Rect(18f, 18f, 390f, 382f), GUI.skin.box);
            GUILayout.Space(4f);
            GUILayout.Label("SQUAT", _playerTitleStyle, GUILayout.Height(30f));
            GUILayout.Label(string.Format(CultureInfo.InvariantCulture, "BAR LOAD  {0:0} kg", _selectedLoadKg),
                _playerBodyStyle, GUILayout.Height(24f));

            SquatAttemptLifecycleState lifecycleState = _attemptOrchestrator == null
                ? SquatAttemptLifecycleState.IDLE
                : _attemptOrchestrator.Lifecycle.State;
            SquatAttemptRecord record = AttemptRecord;
            if (_isInitialized && lifecycleState == SquatAttemptLifecycleState.IDLE && record == null)
            {
                GUILayout.BeginHorizontal();
                DrawLoadButton(0f);
                DrawLoadButton(25f);
                DrawLoadButton(105f);
                GUILayout.EndHorizontal();
            }

            if (!_isInitialized)
            {
                GUILayout.Label("Preparing the squat…", _playerBodyStyle, GUILayout.Height(30f));
            }
            else if (record != null)
            {
                GUILayout.Label("ATTEMPT COMPLETE", _playerBodyStyle, GUILayout.Height(26f));
                GUILayout.Label(FormatPlayerResult(record), _playerResultStyle, GUILayout.MinHeight(58f));
                if (GUILayout.Button("RETRY", _playerButtonStyle, GUILayout.Height(40f)))
                {
                    RetryFromPlayerUi();
                    GUILayout.EndArea();
                    return;
                }
            }
            else if (lifecycleState == SquatAttemptLifecycleState.IDLE)
            {
                GUILayout.Label("READY", _playerResultStyle, GUILayout.Height(28f));
                SquatOneStepWalkoutState walkoutState = _adapter.OneStepWalkoutState;
                bool walkoutInProgress = walkoutState != SquatOneStepWalkoutState.BILATERAL_STANDING &&
                    walkoutState != SquatOneStepWalkoutState.ONE_STEP_READY &&
                    walkoutState != SquatOneStepWalkoutState.ABORTED;
                if (CanRequestOneStepWalkoutIntent &&
                    GUILayout.Button("WALKOUT STEP", _playerButtonStyle, GUILayout.Height(38f)))
                    RequestOneStepWalkoutIntent();
                if (walkoutInProgress)
                    GUILayout.Label("WALKOUT IN PROGRESS", _playerBodyStyle, GUILayout.Height(24f));
                else if (walkoutState == SquatOneStepWalkoutState.ABORTED)
                    GUILayout.Label("WALKOUT ABORTED · RESET TO RETRY", _playerBodyStyle, GUILayout.Height(24f));
                else if (walkoutState == SquatOneStepWalkoutState.ONE_STEP_READY)
                    GUILayout.Label("ONE-STEP WALKOUT READY", _playerBodyStyle, GUILayout.Height(24f));
                else if (walkoutState == SquatOneStepWalkoutState.BILATERAL_STANDING &&
                    GUILayout.Button("START ATTEMPT", _playerButtonStyle, GUILayout.Height(42f)))
                    StartAttemptFromPlayerUi();
            }
            else
            {
                GUILayout.Label("ATTEMPT ACTIVE", _playerResultStyle, GUILayout.Height(28f));
                GUILayout.Label(PlayerInstruction(lifecycleState), _playerBodyStyle, GUILayout.MinHeight(44f));
            }

            GUILayout.Space(4f);
            GUILayout.Label("1 / 2 / 3 load  ·  F start  ·  Space brace / confirm", _playerControlsStyle, GUILayout.Height(20f));
            GUILayout.Label("S / ↓ descend  ·  W / ↑ drive up  ·  Esc abort  ·  R reset", _playerControlsStyle, GUILayout.Height(20f));
            if (GUILayout.Button(showDebugGui ? "HIDE DEVELOPER TELEMETRY" : "DEVELOPER TELEMETRY", GUILayout.Height(28f)))
                showDebugGui = !showDebugGui;
            GUILayout.EndArea();
        }

        private void DrawLoadButton(float loadKg)
        {
            Color previous = GUI.backgroundColor;
            if (Mathf.Approximately(_selectedLoadKg, loadKg))
                GUI.backgroundColor = new Color(0.42f, 0.82f, 0.68f);
            if (GUILayout.Button(loadKg.ToString("0", CultureInfo.InvariantCulture) + " kg",
                _playerButtonStyle, GUILayout.Height(38f)))
                SelectLoadFromPlayerUi(loadKg);
            GUI.backgroundColor = previous;
        }

        private string PlayerInstruction(SquatAttemptLifecycleState lifecycleState)
        {
            if (lifecycleState == SquatAttemptLifecycleState.START_WINDOW)
                return "Hold steady while your start position is checked.";
            if (lifecycleState == SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION)
                return "Stand tall and hold steady while the result is finalized.";

            switch (_adapter.State)
            {
                case SquatState.SQUAT_COMMAND:
                    return "Press and hold S or ↓ to begin the descent.";
                case SquatState.DESCENT:
                    return "Continue the descent with S or ↓.";
                case SquatState.BOTTOM:
                case SquatState.REVERSAL:
                    return "Drive up with W or ↑.";
                case SquatState.ASCENT:
                case SquatState.STICKING:
                    return "Keep driving up with W or ↑.";
                case SquatState.LOCKOUT:
                case SquatState.RACK_COMMAND:
                case SquatState.RERACK:
                    return "Stand tall and hold steady while the attempt finishes.";
                case SquatState.FAILURE:
                    return "Attempt ended. Review the result.";
                default:
                    return "Get set. Follow the cue to descend, then drive up.";
            }
        }

        public static string FormatPlayerResult(SquatAttemptRecord record)
        {
            if (record == null)
                return string.Empty;

            return FormatPlayerResult(
                record.TerminalReason,
                record.Judgment.EvidenceStatus,
                record.RuleOutcome,
                record.Judgment.PrimaryViolationKind,
                record.FailureResult.EvidenceStatus,
                record.PhysicalFailureOutcome,
                record.FailureResult.PrimaryFailureKind);
        }

        public static string FormatPlayerResult(
            SquatAttemptTerminalReason terminalReason,
            SquatJudgmentEvidenceStatus ruleEvidenceStatus,
            SquatJudgmentOutcome ruleOutcome,
            SquatRuleViolationKind primaryViolationKind,
            SquatFailureEvidenceStatus failureEvidenceStatus,
            SquatFailureResultKind failureOutcome,
            SquatFailureKind primaryFailureKind)
        {
            bool terminalOverride =
                terminalReason == SquatAttemptTerminalReason.ABORTED ||
                terminalReason == SquatAttemptTerminalReason.TIMEOUT ||
                terminalReason == SquatAttemptTerminalReason.LIFECYCLE_FAULT;
            bool terminalPhysicalFailure = terminalReason == SquatAttemptTerminalReason.PHYSICAL_FAILURE;
            bool physicalFailure =
                failureEvidenceStatus == SquatFailureEvidenceStatus.EVALUABLE &&
                failureOutcome == SquatFailureResultKind.PHYSICAL_FAILURE;
            bool physicalUnavailable =
                failureEvidenceStatus != SquatFailureEvidenceStatus.EVALUABLE ||
                failureOutcome == SquatFailureResultKind.UNDETERMINED;

            string result;
            if (terminalReason == SquatAttemptTerminalReason.ABORTED)
                result = "ATTEMPT ABORTED";
            else if (terminalReason == SquatAttemptTerminalReason.TIMEOUT)
                result = "ATTEMPT TIMEOUT";
            else if (terminalReason == SquatAttemptTerminalReason.LIFECYCLE_FAULT)
                result = "LIFECYCLE FAULT";
            else if (terminalPhysicalFailure || physicalFailure)
            {
                result = "PHYSICAL FAILURE";
                if (failureEvidenceStatus == SquatFailureEvidenceStatus.EVALUABLE &&
                    primaryFailureKind != SquatFailureKind.NONE)
                    result += "\nReason: " + Humanize(primaryFailureKind.ToString());
                else if (failureEvidenceStatus != SquatFailureEvidenceStatus.EVALUABLE)
                    result += "\nPhysical evidence: " + Humanize(failureEvidenceStatus.ToString());
            }
            else if (physicalUnavailable)
            {
                result = "RESULT UNAVAILABLE";
                result += "\nPhysical: " + (failureEvidenceStatus != SquatFailureEvidenceStatus.EVALUABLE
                    ? Humanize(failureEvidenceStatus.ToString())
                    : Humanize(failureOutcome.ToString()));
            }
            else
            {
                result = FormatRuleVerdict(ruleEvidenceStatus, ruleOutcome, primaryViolationKind);
            }

            bool headlineIsRuleVerdict =
                !terminalOverride &&
                !terminalPhysicalFailure &&
                !physicalFailure &&
                !physicalUnavailable;
            if (!headlineIsRuleVerdict)
                result += "\n" + FormatRuleLine(ruleEvidenceStatus, ruleOutcome, primaryViolationKind);

            if (terminalOverride)
            {
                if (failureEvidenceStatus != SquatFailureEvidenceStatus.EVALUABLE)
                    result += "\nPhysical: " + Humanize(failureEvidenceStatus.ToString());
                else if (physicalFailure)
                    result += "\nPhysical: " + (primaryFailureKind == SquatFailureKind.NONE
                        ? "failure"
                        : Humanize(primaryFailureKind.ToString()));
                else if (failureOutcome == SquatFailureResultKind.UNDETERMINED)
                    result += "\nPhysical: undetermined";
            }

            return result;
        }

        private static string FormatRuleVerdict(
            SquatJudgmentEvidenceStatus evidenceStatus,
            SquatJudgmentOutcome outcome,
            SquatRuleViolationKind primaryViolationKind)
        {
            if (evidenceStatus != SquatJudgmentEvidenceStatus.EVALUABLE)
                return "UNJUDGED\nRules: " + Humanize(evidenceStatus.ToString());
            if (outcome == SquatJudgmentOutcome.GOOD_LIFT)
                return "GOOD LIFT";
            if (outcome == SquatJudgmentOutcome.NO_LIFT)
                return "NO LIFT" + (primaryViolationKind == SquatRuleViolationKind.NONE
                    ? string.Empty
                    : "\nReason: " + Humanize(primaryViolationKind.ToString()));
            return "UNDECIDED\nRules: " + Humanize(outcome.ToString());
        }

        private static string FormatRuleLine(
            SquatJudgmentEvidenceStatus evidenceStatus,
            SquatJudgmentOutcome outcome,
            SquatRuleViolationKind primaryViolationKind)
        {
            if (evidenceStatus != SquatJudgmentEvidenceStatus.EVALUABLE)
                return "Rules: " + Humanize(evidenceStatus.ToString());
            if (outcome == SquatJudgmentOutcome.GOOD_LIFT)
                return "Rules: GOOD LIFT";
            if (outcome == SquatJudgmentOutcome.NO_LIFT)
                return "Rules: NO LIFT" + (primaryViolationKind == SquatRuleViolationKind.NONE
                    ? string.Empty
                    : " (" + Humanize(primaryViolationKind.ToString()) + ")");
            return "Rules: " + Humanize(outcome.ToString());
        }

        private static string Humanize(string value) => value.Replace('_', ' ').ToLowerInvariant();

        private void DrawDebugHud()
        {
            float top = 418f;
            GUILayout.BeginArea(new Rect(18f, top, Screen.width - 36f,
                Mathf.Max(200f, Screen.height - top - 18f)), GUI.skin.box);
            GUILayout.Label("PHYSICAL SQUAT", _hudTitleStyle, GUILayout.Height(16f));
            if (!_isInitialized)
            {
                GUILayout.Label("STARTUP: " + (_startupFailure.Length == 0 ? "waiting" : _startupFailure), _hudLabelStyle, GUILayout.Height(13f));
                GUILayout.EndArea();
                return;
            }

            float pelvisHeight = athleteRig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvis) && pelvis.Body != null
                ? pelvis.Body.position.y
                : 0f;
            string leftContact = _leftFootContact != null && _leftFootContact.IsInContact ? "YES" : "NO";
            string rightContact = _rightFootContact != null && _rightFootContact.IsInContact ? "YES" : "NO";
            float leftSlip = _leftFootContact != null ? _leftFootContact.SlipSpeed : 0f;
            float rightSlip = _rightFootContact != null ? _rightFootContact.SlipSpeed : 0f;
            float saddleError = _saddle != null ? _saddle.SaddleSeparationMeters : float.PositiveInfinity;
            string failure = _adapter.FailureReason;
            if (_saddle != null && !_saddle.SpawnAlignmentWithinTolerance && failure == "NONE")
                failure = "SPAWN_ALIGNMENT";

            SquatAttemptRecord record = _attemptOrchestrator.Record;
            HudLabel(record == null
                ? _attemptOrchestrator.HasStarted
                    ? "ATTEMPT: ACTIVE — " + _attemptOrchestrator.Lifecycle.State
                    : "ATTEMPT: READY — select a load, then press F"
                : "ATTEMPT: FINALIZED");
            if (record != null)
            {
                string ruleResult = record.Judgment.EvidenceStatus != SquatJudgmentEvidenceStatus.EVALUABLE
                    ? "UNEVALUABLE (" + record.Judgment.EvidenceStatus + ")"
                    : record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT
                        ? "LEGAL GOOD_LIFT"
                        : record.RuleOutcome == SquatJudgmentOutcome.NO_LIFT
                            ? "NO_LIFT (" + record.Judgment.PrimaryViolationKind + ")"
                            : "UNDETERMINED (" + record.RuleOutcome + ")";
                string physicalResult = record.FailureResult.EvidenceStatus != SquatFailureEvidenceStatus.EVALUABLE
                    ? "UNEVALUABLE (" + record.FailureResult.EvidenceStatus + ")"
                    : record.PhysicalFailureOutcome == SquatFailureResultKind.PHYSICAL_FAILURE
                        ? "PHYSICAL_FAILURE (" + record.FailureResult.PrimaryFailureKind + ")"
                        : record.PhysicalFailureOutcome.ToString();
                HudLabel("RULES: " + ruleResult);
                HudLabel("PHYSICAL: " + physicalResult);
                HudLabel("TERMINAL: " + record.TerminalReason);
            }

            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(315f));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "STATE: {0}", _adapter.State));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "s_q: {0:F3}", _adapter.Sq));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "LOAD_KG: {0:F1}", _selectedLoadKg));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "PELVIS_HEIGHT: {0:F3} m", pelvisHeight));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "SYSTEM_COM_AP_ERROR: {0:+0.000;-0.000} m", _adapter.ApComError));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "SYSTEM_COM_ML_ERROR: {0:+0.000;-0.000} m", _adapter.MlComError));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "MAX_DRIVE_SATURATION: {0:F2}", _adapter.MaxDriveSaturation));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "FAILURE_REASON: {0}", failure));
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(300f));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "LEFT_FOOT_CONTACT: {0}", leftContact));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "RIGHT_FOOT_CONTACT: {0}", rightContact));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "LEFT_FOOT_SLIP: {0:F4} m/s", leftSlip));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "RIGHT_FOOT_SLIP: {0:F4} m/s", rightSlip));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "BAR_SADDLE_ATTACHED: {0}", _saddle != null && _saddle.IsAttached ? "YES" : "NO"));
            HudLabel(string.Format(CultureInfo.InvariantCulture, "BAR_SADDLE_ERROR: {0}", float.IsInfinity(saddleError) ? "N/A" : saddleError.ToString("F4", CultureInfo.InvariantCulture) + " m"));
            GUILayout.EndVertical();

            SquatBalanceObserver balance = _adapter.Balance;
            GUILayout.BeginVertical(GUILayout.Width(330f));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "COM_AP: {0:+0.000;-0.000} m  VEL: {1:+0.000;-0.000} m/s",
                balance.SystemCom.z, balance.SystemComVelocity.z));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "COM_ML: {0:+0.000;-0.000} m  VEL: {1:+0.000;-0.000} m/s",
                balance.SystemCom.x, balance.SystemComVelocity.x));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "CAPTURE_AP: {0:+0.000;-0.000} m", balance.CaptureAp));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "SUPPORT_AP: [{0:+0.000;-0.000}, {1:+0.000;-0.000}] m",
                balance.SupportApMin, balance.SupportApMax));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "CAPTURE_MARGIN: rear {0:0.000} front {1:0.000} m",
                balance.CaptureMarginRear, balance.CaptureMarginFront));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "ANKLE_NOMINAL: {0:+0.0;-0.0} deg", HudSagittalDegrees(AnkleComposition().Nominal)));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "V2_BALANCE_AP/ML: {0:+0.0;-0.0} / {1:+0.0;-0.0} deg",
                _adapter.BalanceCorrectionRad * Mathf.Rad2Deg,
                _adapter.MlBalanceCorrectionRad * Mathf.Rad2Deg));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "ANKLE_FINAL_TARGET: {0:+0.0;-0.0} deg", HudSagittalDegrees(AnkleComposition().Final)));
            HudLabel(string.Format(CultureInfo.InvariantCulture,
                "ANKLE_ACTUAL: {0:+0.0;-0.0} deg", HudAnkleActualDegrees()));
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();

            HudLabel("CONTROLS: F Start attempt | SPACE Brace/Confirm | S Descend | W Ascend | ESC Abort active attempt | R Reset");
            HudLabel("LOAD_CONTROLS: 1 Unloaded | 2 25 kg | 3 105 kg");
            GUILayout.EndArea();
        }

        private SquatPhysicalAdapter.JointTargetComposition AnkleComposition()
        {
            _adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition composition);
            return composition;
        }

        private float HudAnkleActualDegrees() =>
            athleteRig == null
                ? 0f
                : HudSagittalDegrees(athleteRig.PoweredController.GetJoint("left_foot").Diagnostic.ActualRelative);

        private static float HudSagittalDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude) * Mathf.Rad2Deg;
        }

        private void HudLabel(string text)
        {
            GUILayout.Label(text, _hudLabelStyle, GUILayout.Height(13f));
        }

        private void EnsureHudStyles()
        {
            if (_hudLabelStyle != null)
                return;

            _hudLabelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 10,
                fixedHeight = 13f,
                padding = new RectOffset(0, 0, 0, 0),
                margin = new RectOffset(0, 0, 0, 0)
            };
            _hudTitleStyle = new GUIStyle(_hudLabelStyle)
            {
                fontSize = 13,
                fixedHeight = 16f,
                fontStyle = FontStyle.Bold
            };
            _playerTitleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 22,
                fixedHeight = 30f,
                fontStyle = FontStyle.Bold
            };
            _playerBodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                wordWrap = true
            };
            _playerResultStyle = new GUIStyle(_playerBodyStyle)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold
            };
            _playerButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            _playerControlsStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                wordWrap = true
            };
        }
    }
}
