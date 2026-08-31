using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Athlete
{
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class PhysicalAthleteRig : MonoBehaviour
    {
        private const string ArtifactPath = "Artifacts/Measurements/GAM-7-powered-joints.json";

        [SerializeField] private FoundationBootstrap foundation;
        [SerializeField] private AthleteRigOwnership ownership;
        [SerializeField] private Animator referenceAnimator;
        [SerializeField] private Animator visibleAnimator;
        [SerializeField] private Material proxyMaterial;
        [SerializeField] private bool showVisibleMesh = true;
        [SerializeField] private bool showPhysicalColliders = true;
        [SerializeField] private bool showSegmentCom = true;
        [SerializeField] private bool showWholeBodyCom = true;
        [SerializeField] private bool showJointAnchors = true;
        [SerializeField] private bool showJointAxes = true;

        private readonly Dictionary<string, SegmentRuntime> _segments = new Dictionary<string, SegmentRuntime>(StringComparer.Ordinal);
        private readonly List<JointRuntime> _joints = new List<JointRuntime>();
        private readonly List<VisibleFollowerBinding> _visibleBindings = new List<VisibleFollowerBinding>();
        private readonly List<DebugMarker> _segmentComMarkers = new List<DebugMarker>();
        private readonly List<DebugMarker> _jointAnchorMarkers = new List<DebugMarker>();
        private readonly List<LineRenderer> _jointAxisLines = new List<LineRenderer>();
        private Renderer[] _visibleRenderers = Array.Empty<Renderer>();
        private GameObject _physicalRoot;
        private DebugMarker _wholeBodyComMarker;
        private PoweredJointController _poweredController;
        private bool _inspectionFrozen;
        private float _qualifiedPassiveComDropMeters;
        private float _qualifiedPoweredComDropMeters;
        private float _qualifiedPositivePulseDegrees;
        private string _status = "Building finite powered physical athlete...";

        public IReadOnlyDictionary<string, SegmentRuntime> Segments => _segments;
        public IReadOnlyList<JointRuntime> Joints => _joints;
        public bool IsInspectionFrozen => _inspectionFrozen;
        public float TotalMassKg => _segments.Values.Sum(segment => segment.Body.mass);
        public float MaxInitialNonAdjacentPenetrationMeters { get; private set; }
        public PoweredJointController PoweredController => _poweredController;

        public void Configure(
            FoundationBootstrap foundationBootstrap,
            AthleteRigOwnership rigOwnership,
            Animator reference,
            Animator visible,
            Material debugProxyMaterial)
        {
            foundation = foundationBootstrap;
            ownership = rigOwnership;
            referenceAnimator = reference;
            visibleAnimator = visible;
            proxyMaterial = debugProxyMaterial;
        }

        private void Start()
        {
            Build();
        }

        public void Build()
        {
            if (_physicalRoot != null)
                throw new InvalidOperationException("The physical athlete has already been built.");
            if (foundation == null || !foundation.Runtime.IsInitialized)
                throw new InvalidOperationException("The foundation runtime must initialize before the physical athlete.");
            if (referenceAnimator == null || visibleAnimator == null)
                throw new InvalidOperationException("Separate reference and visible Humanoid animators are required.");

            PhysicalAthleteDefinition.ValidateDefinition();
            ValidateHumanoid(referenceAnimator, "reference");
            ValidateHumanoid(visibleAnimator, "visible");

            referenceAnimator.enabled = false;
            visibleAnimator.enabled = false;
            _visibleRenderers = visibleAnimator.GetComponentsInChildren<Renderer>(true);

            _physicalRoot = new GameObject("PhysicalRig_GAM6_Authoritative");
            SceneManager.MoveGameObjectToScene(_physicalRoot, foundation.Runtime.AuthoritativeScene);

            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                CreateSegment(recipe);
            foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
                CreateJoint(recipe);

            _poweredController = new PoweredJointController(_joints);
            foundation.Runtime.RegisterPrePhysicsStep(_poweredController.Step);

            DisableAdjacentSelfCollision();
            MaxInitialNonAdjacentPenetrationMeters = MeasureInitialNonAdjacentPenetration();
            CreatePlatformCollider();
            BuildVisibleFollower();
            CreateRuntimeDebugOverlay();
            ownership.ConfigurePhysicalRig(referenceAnimator, visibleAnimator.transform, _physicalRoot.transform);
            ApplyVisibility();
            ValidateRuntime();
            _status = "PASSIVE: gravity on, 16/16 bodies dynamic, drives off";
        }

        private void LateUpdate()
        {
            if (_segments.Count == 0 || _segments["pelvis"].Body == null)
                return;

            foreach (VisibleFollowerBinding binding in _visibleBindings)
                binding.VisibleBone.rotation = binding.Body.rotation * binding.BodyToBoneRotation;

            SegmentRuntime pelvis = _segments["pelvis"];
            Transform visibleHips = visibleAnimator.GetBoneTransform(HumanBodyBones.Hips);
            visibleHips.position = pelvis.Body.position + pelvis.Body.rotation * pelvis.BodyToVisiblePosition;
            UpdateRuntimeDebugOverlay();
        }

        public void ResetPassive()
        {
            ResetToMode(PoweredAthleteMode.Passive);
            _status = "RESET -> PASSIVE";
        }

        public void StartPoweredNeutral()
        {
            ResetToMode(PoweredAthleteMode.PoweredNeutral);
            _status = "POWERED NEUTRAL: finite open-loop joint authority";
        }

        public void StartZeroActivation()
        {
            ResetToMode(PoweredAthleteMode.ZeroActivation);
            _status = "ZERO ACTIVATION: powered architecture, maximumForce = 0";
        }

        public void StartSelectedJointPulse(bool positive)
        {
            ResetToMode(PoweredAthleteMode.SelectedJointPulse);
            _poweredController.SetPulse(positive);
            _status = $"JOINT PULSE: {_poweredController.SelectedJointId} {(positive ? "+" : "-")}20 deg";
        }

        public void SelectNextPulseJoint(int direction)
        {
            _poweredController.SelectNextPulseJoint(direction);
            _status = $"Selected pulse joint: {_poweredController.SelectedJointId}";
        }

        private void ResetToMode(PoweredAthleteMode mode)
        {
            if (_segments.Count == 0)
                return;

            SetInspectionFrozen(false);
            foundation.Reset();
            _poweredController.Reset(mode);
            foreach (SegmentRuntime segment in _segments.Values)
                segment.Body.WakeUp();
        }

        public void InspectNeutral()
        {
            if (_segments.Count == 0)
                return;

            foundation.Reset();
            _poweredController.Reset(PoweredAthleteMode.Passive);
            SetInspectionFrozen(true);
            _status = "AUTHORING INSPECTION: explicitly frozen (not gameplay)";
        }

        public void ReleasePassive()
        {
            SetInspectionFrozen(false);
            _poweredController.Reset(PoweredAthleteMode.Passive);
            foreach (SegmentRuntime segment in _segments.Values)
                segment.Body.WakeUp();
            _status = "PASSIVE: released under gravity";
        }

        private void SetInspectionFrozen(bool frozen)
        {
            _inspectionFrozen = frozen;
            foreach (SegmentRuntime segment in _segments.Values)
            {
                segment.Body.isKinematic = frozen;
                if (!frozen)
                    segment.Body.WakeUp();
            }
        }

        private void CreateSegment(PhysicalSegmentRecipe recipe)
        {
            Transform proximal = RequireBone(referenceAnimator, recipe.ProximalBone);
            Transform distal = RequireBone(referenceAnimator, recipe.DistalBone);
            Vector3 axis = distal.position - proximal.position;
            float measuredLength = axis.magnitude;
            Vector3 center = recipe.ProximalBone == recipe.DistalBone
                ? proximal.position
                : Vector3.Lerp(proximal.position, distal.position, recipe.ComFraction);
            center += recipe.FixedCenterOffsetMeters;

            Quaternion rotation = ResolveBodyRotation(recipe, axis, measuredLength);

            GameObject bodyObject = new GameObject(recipe.Id);
            bodyObject.transform.SetParent(_physicalRoot.transform, false);
            bodyObject.transform.SetPositionAndRotation(center, rotation);
            Rigidbody body = bodyObject.AddComponent<Rigidbody>();
            body.mass = PhysicalAthleteDefinition.PrototypeBodyMassKg * recipe.MassFraction;
            body.useGravity = true;
            body.isKinematic = false;
            body.linearDamping = 0.04f;
            body.angularDamping = 0.08f;
            body.interpolation = RigidbodyInterpolation.None;
            body.collisionDetectionMode = CollisionDetectionMode.Discrete;
            body.solverIterations = 12;
            body.solverVelocityIterations = 4;
            body.maxAngularVelocity = 35f;
            body.centerOfMass = Vector3.zero;

            Vector3 dimensions = ResolveDimensions(recipe, measuredLength);
            Collider collider = AddCollider(bodyObject, recipe.Collider, dimensions);
            body.inertiaTensor = PhysicalAthleteDefinition.BoxInertia(body.mass, dimensions);
            body.inertiaTensorRotation = Quaternion.identity;

            Renderer proxyRenderer = CreateProxyVisual(bodyObject.transform, recipe.Collider, dimensions);
            Transform visibleBone = RequireBone(visibleAnimator, recipe.VisibleBone);
            Vector3 bodyToVisiblePosition = Quaternion.Inverse(body.rotation) * (visibleBone.position - body.position);
            var runtime = new SegmentRuntime(recipe, body, collider, proxyRenderer, dimensions, bodyToVisiblePosition);
            _segments.Add(recipe.Id, runtime);

            if (recipe.ParentId == null)
                foundation.Runtime.RegisterPrimaryBody(body, recipe.Id);
            else
                foundation.Runtime.RegisterBody(body, recipe.Id);
        }

        private void CreateJoint(PhysicalJointRecipe recipe)
        {
            SegmentRuntime child = _segments[recipe.ChildId];
            SegmentRuntime parent = _segments[child.Recipe.ParentId];
            Vector3 anchorWorld = RequireBone(referenceAnimator, recipe.AnchorBone).position;
            ConfigurableJoint joint = child.Body.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.Body;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = child.Body.transform.InverseTransformPoint(anchorWorld);
            joint.connectedAnchor = parent.Body.transform.InverseTransformPoint(anchorWorld);
            joint.axis = child.Body.transform.InverseTransformDirection(recipe.PrimaryAxisWorld.normalized);
            Vector3 secondaryWorld = Mathf.Abs(Vector3.Dot(recipe.PrimaryAxisWorld.normalized, Vector3.up)) < 0.9f
                ? Vector3.up
                : Vector3.forward;
            joint.secondaryAxis = child.Body.transform.InverseTransformDirection(secondaryWorld);
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = recipe.Kind == PhysicalJointKind.Hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.angularZMotion = recipe.Kind == PhysicalJointKind.Hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = Limit(recipe.LowDegrees);
            joint.highAngularXLimit = Limit(recipe.HighDegrees);
            joint.angularYLimit = Limit(recipe.SecondaryLimitDegrees);
            joint.angularZLimit = Limit(recipe.SecondaryLimitDegrees);
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            _joints.Add(new JointRuntime(recipe, joint, anchorWorld));
        }

        private void DisableAdjacentSelfCollision()
        {
            foreach (JointRuntime joint in _joints)
            {
                SegmentRuntime child = _segments[joint.Recipe.ChildId];
                SegmentRuntime parent = _segments[child.Recipe.ParentId];
                Physics.IgnoreCollision(parent.Collider, child.Collider, true);
            }
        }

        private void CreatePlatformCollider()
        {
            GameObject platform = new GameObject("PhysicalPlatform_GAM6");
            SceneManager.MoveGameObjectToScene(platform, foundation.Runtime.AuthoritativeScene);
            platform.transform.SetPositionAndRotation(new Vector3(0f, -0.05f, 0f), Quaternion.identity);
            BoxCollider collider = platform.AddComponent<BoxCollider>();
            collider.size = new Vector3(5f, 0.10f, 5f);
            PhysicsMaterial material = new PhysicsMaterial("GAM6_PlatformContact")
            {
                dynamicFriction = 0.75f,
                staticFriction = 0.85f,
                bounciness = 0f,
                frictionCombine = PhysicsMaterialCombine.Average,
                bounceCombine = PhysicsMaterialCombine.Minimum
            };
            collider.material = material;
        }

        private void BuildVisibleFollower()
        {
            foreach (SegmentRuntime segment in _segments.Values)
            {
                Transform visibleBone = RequireBone(visibleAnimator, segment.Recipe.VisibleBone);
                Quaternion bodyToBone = Quaternion.Inverse(segment.Body.rotation) * visibleBone.rotation;
                _visibleBindings.Add(new VisibleFollowerBinding(segment.Body, visibleBone, bodyToBone));
            }
        }

        private static Vector3 ResolveDimensions(PhysicalSegmentRecipe recipe, float measuredLength)
        {
            Vector3 dimensions = recipe.DimensionsMeters;
            if (recipe.Collider == PhysicalColliderKind.Capsule && measuredLength > 0.0001f && recipe.Id != "head_neck")
                dimensions.y = measuredLength * 0.84f;
            return dimensions;
        }

        private static Quaternion ResolveBodyRotation(PhysicalSegmentRecipe recipe, Vector3 axis, float measuredLength)
        {
            if (measuredLength <= 0.0001f)
                return Quaternion.identity;
            if (recipe.Id.EndsWith("_foot", StringComparison.Ordinal))
                return Quaternion.identity;
            if (recipe.Id.EndsWith("_hand", StringComparison.Ordinal))
                return Quaternion.FromToRotation(Vector3.right, axis.normalized);
            return Quaternion.FromToRotation(Vector3.up, axis.normalized);
        }

        private float MeasureInitialNonAdjacentPenetration()
        {
            float maximum = 0f;
            SegmentRuntime[] segments = _segments.Values.ToArray();
            for (int firstIndex = 0; firstIndex < segments.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < segments.Length; secondIndex++)
                {
                    SegmentRuntime first = segments[firstIndex];
                    SegmentRuntime second = segments[secondIndex];
                    if (string.Equals(first.Recipe.ParentId, second.Recipe.Id, StringComparison.Ordinal) ||
                        string.Equals(second.Recipe.ParentId, first.Recipe.Id, StringComparison.Ordinal))
                        continue;

                    if (Physics.ComputePenetration(
                        first.Collider, first.Collider.transform.position, first.Collider.transform.rotation,
                        second.Collider, second.Collider.transform.position, second.Collider.transform.rotation,
                        out _, out float distance))
                        maximum = Mathf.Max(maximum, distance);
                }
            }
            return maximum;
        }

        private static Collider AddCollider(GameObject owner, PhysicalColliderKind kind, Vector3 dimensions)
        {
            switch (kind)
            {
                case PhysicalColliderKind.Box:
                    BoxCollider box = owner.AddComponent<BoxCollider>();
                    box.size = dimensions;
                    return box;
                case PhysicalColliderKind.Capsule:
                    CapsuleCollider capsule = owner.AddComponent<CapsuleCollider>();
                    capsule.direction = 1;
                    capsule.radius = dimensions.x * 0.5f;
                    capsule.height = Mathf.Max(dimensions.y, dimensions.x);
                    return capsule;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }
        }

        private Renderer CreateProxyVisual(Transform parent, PhysicalColliderKind kind, Vector3 dimensions)
        {
            PrimitiveType primitive = kind == PhysicalColliderKind.Box ? PrimitiveType.Cube : PrimitiveType.Capsule;
            GameObject proxy = GameObject.CreatePrimitive(primitive);
            proxy.name = "DebugProxy";
            proxy.transform.SetParent(parent, false);
            if (kind == PhysicalColliderKind.Capsule)
                proxy.transform.localScale = new Vector3(dimensions.x, dimensions.y * 0.5f, dimensions.z);
            else
                proxy.transform.localScale = dimensions;
            DestroyImmediate(proxy.GetComponent<Collider>());
            Renderer renderer = proxy.GetComponent<Renderer>();
            if (proxyMaterial != null)
                renderer.sharedMaterial = proxyMaterial;
            return renderer;
        }

        private void ValidateRuntime()
        {
            if (_segments.Count != 16 || _joints.Count != 15)
                throw new InvalidOperationException("Runtime topology is not the canonical 16-body/15-joint graph.");
            if (Mathf.Abs(TotalMassKg - PhysicalAthleteDefinition.PrototypeBodyMassKg) > 0.0001f)
                throw new InvalidOperationException($"Assigned mass {TotalMassKg:R} kg does not match profile mass.");

            foreach (SegmentRuntime segment in _segments.Values)
            {
                Vector3 inertia = segment.Body.inertiaTensor;
                if (segment.Body.isKinematic || !segment.Body.useGravity || segment.Body.mass <= 0f ||
                    !Finite(inertia) || inertia.x <= 0f || inertia.y <= 0f || inertia.z <= 0f)
                    throw new InvalidOperationException($"Segment '{segment.Recipe.Id}' is not a finite dynamic gravity body.");
            }

            foreach (JointRuntime jointRuntime in _joints)
            {
                ConfigurableJoint joint = jointRuntime.Joint;
                Vector3 childAnchor = joint.transform.TransformPoint(joint.anchor);
                Vector3 parentAnchor = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                if (Vector3.Distance(childAnchor, parentAnchor) > PhysicalAthleteDefinition.AnchorToleranceMeters)
                    throw new InvalidOperationException($"Joint '{jointRuntime.Recipe.ChildId}' bind anchors do not coincide.");
                if (joint.projectionMode != JointProjectionMode.None || HasPoweredDrive(joint))
                    throw new InvalidOperationException($"Joint '{jointRuntime.Recipe.ChildId}' violates passive-mode authority.");
            }
        }

        private void ApplyVisibility()
        {
            foreach (Renderer renderer in _visibleRenderers)
                renderer.enabled = showVisibleMesh;
            foreach (SegmentRuntime segment in _segments.Values)
                segment.ProxyRenderer.enabled = showPhysicalColliders;
            foreach (DebugMarker marker in _segmentComMarkers)
                marker.Renderer.enabled = showSegmentCom;
            if (_wholeBodyComMarker != null)
                _wholeBodyComMarker.Renderer.enabled = showWholeBodyCom;
            foreach (DebugMarker marker in _jointAnchorMarkers)
                marker.Renderer.enabled = showJointAnchors;
            foreach (LineRenderer line in _jointAxisLines)
                line.enabled = showJointAxes;
        }

        private void CreateRuntimeDebugOverlay()
        {
            foreach (SegmentRuntime segment in _segments.Values)
                _segmentComMarkers.Add(CreateMarker("COM_" + segment.Recipe.Id, 0.028f, Color.yellow));
            _wholeBodyComMarker = CreateMarker("WholeBodyCOM", 0.075f, Color.magenta);

            foreach (JointRuntime joint in _joints)
            {
                _jointAnchorMarkers.Add(CreateMarker("Anchor_" + joint.Recipe.ChildId, 0.034f, Color.cyan));
                GameObject lineObject = new GameObject("Axis_" + joint.Recipe.ChildId);
                lineObject.transform.SetParent(transform, false);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.positionCount = 2;
                line.startWidth = 0.012f;
                line.endWidth = 0.012f;
                line.sharedMaterial = proxyMaterial;
                line.startColor = Color.red;
                line.endColor = Color.red;
                line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                line.receiveShadows = false;
                _jointAxisLines.Add(line);
            }
            UpdateRuntimeDebugOverlay();
        }

        private DebugMarker CreateMarker(string markerName, float diameter, Color color)
        {
            GameObject markerObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            markerObject.name = markerName;
            markerObject.transform.SetParent(transform, false);
            markerObject.transform.localScale = Vector3.one * diameter;
            DestroyImmediate(markerObject.GetComponent<Collider>());
            Renderer renderer = markerObject.GetComponent<Renderer>();
            if (proxyMaterial != null)
                renderer.sharedMaterial = proxyMaterial;
            renderer.material.color = color;
            return new DebugMarker(markerObject.transform, renderer);
        }

        private void UpdateRuntimeDebugOverlay()
        {
            if (_segmentComMarkers.Count != _segments.Count)
                return;

            int segmentIndex = 0;
            foreach (SegmentRuntime segment in _segments.Values)
                _segmentComMarkers[segmentIndex++].Transform.position = segment.Body.worldCenterOfMass;
            _wholeBodyComMarker.Transform.position = CalculateWholeBodyCom();

            for (int index = 0; index < _joints.Count; index++)
            {
                JointRuntime joint = _joints[index];
                Vector3 anchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 axis = joint.Joint.transform.TransformDirection(joint.Joint.axis).normalized;
                _jointAnchorMarkers[index].Transform.position = anchor;
                _jointAxisLines[index].SetPosition(0, anchor - axis * 0.10f);
                _jointAxisLines[index].SetPosition(1, anchor + axis * 0.10f);
            }
        }

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(18f, 18f, 520f, 430f), GUI.skin.box);
            GUILayout.Label("GAM-7 Finite Powered Athlete");
            GUILayout.Label(_status);
            GUILayout.Label($"Bodies: {_segments.Count}/16   Powered: {_poweredController?.PoweredJointCount ?? 0}/14   Mass: {TotalMassKg:F4} kg");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Passive")) ResetPassive();
            if (GUILayout.Button("Powered Neutral")) StartPoweredNeutral();
            if (GUILayout.Button("Zero Activation")) StartZeroActivation();
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("< Joint")) SelectNextPulseJoint(-1);
            GUILayout.Label(_poweredController?.SelectedJointId ?? "building", GUILayout.Width(110f));
            if (GUILayout.Button("Joint >")) SelectNextPulseJoint(1);
            if (GUILayout.Button("Pulse -")) StartSelectedJointPulse(false);
            if (GUILayout.Button("Pulse +")) StartSelectedJointPulse(true);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Activation", GUILayout.Width(70f));
            if (GUILayout.Button("0")) _poweredController?.SetGlobalActivation(0f);
            if (GUILayout.Button("0.5")) _poweredController?.SetGlobalActivation(0.5f);
            if (GUILayout.Button("1")) _poweredController?.SetGlobalActivation(1f);
            GUILayout.Label($"{(_poweredController?.GlobalActivation ?? 0f):F2}", GUILayout.Width(40f));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset")) ResetPassive();
            if (GUILayout.Button("Inspect Neutral")) InspectNeutral();
            if (GUILayout.Button("Release")) ReleasePassive();
            GUILayout.EndHorizontal();

            if (_poweredController != null)
            {
                PoweredJointController.PoweredJointRuntime selected = _poweredController.GetJoint(_poweredController.SelectedJointId);
                PoweredJointDiagnostic diagnostic = selected.Diagnostic;
                JointFamilyProfile profile = selected.Profile.Value;
                GUILayout.Label($"Mode: {_poweredController.Mode}   Family: {profile.Id}");
                GUILayout.Label($"Requested J: {FormatQuaternion(diagnostic.RequestedTarget)}");
                GUILayout.Label($"Applied J: {FormatQuaternion(diagnostic.AppliedTarget)}");
                GUILayout.Label($"Actual J: {FormatQuaternion(diagnostic.ActualRelative)}");
                GUILayout.Label($"Error rad: {diagnostic.ErrorRad.ToString("F3")}   target w: {diagnostic.TargetAngularVelocityRadS.ToString("F2")}");
                GUILayout.Label($"Kp/Kd: {profile.Spring:F0}/{profile.Damper:F0}   maxForce: {diagnostic.MaximumForceNm:F1} N*m");
                GUILayout.Label($"Activation: {diagnostic.Activation:F2}   capacity scale: {diagnostic.CapacityScale:F2}   demand*: {diagnostic.ModeledDemand:F2}");
                GUILayout.Label($"Limit proximity: {diagnostic.LimitProximity:P0}   *command-side conceptual diagnostic");
            }

            bool visible = GUILayout.Toggle(showVisibleMesh, "Show visible athlete");
            bool proxy = GUILayout.Toggle(showPhysicalColliders, "Show physical colliders");
            showSegmentCom = GUILayout.Toggle(showSegmentCom, "Show segment COMs");
            showWholeBodyCom = GUILayout.Toggle(showWholeBodyCom, "Show whole-body COM");
            showJointAnchors = GUILayout.Toggle(showJointAnchors, "Show joint anchors");
            showJointAxes = GUILayout.Toggle(showJointAxes, "Show joint axes");
            showVisibleMesh = visible;
            showPhysicalColliders = proxy;
            ApplyVisibility();
            GUILayout.EndArea();
        }

        private static string FormatQuaternion(Quaternion value) =>
            $"({value.w:F3}, {value.x:F3}, {value.y:F3}, {value.z:F3})";

        public Vector3 CalculateWholeBodyCom()
        {
            Vector3 weighted = Vector3.zero;
            float mass = 0f;
            foreach (SegmentRuntime segment in _segments.Values)
            {
                weighted += segment.Body.worldCenterOfMass * segment.Body.mass;
                mass += segment.Body.mass;
            }
            return mass > 0f ? weighted / mass : Vector3.zero;
        }

        public void WriteArtifact()
        {
            var artifact = new Artifact
            {
                mission = "POWERLIFTING_SIMULATOR_GAM_7_FINITE_POWERED_JOINTS",
                profileId = "GAM7_FINITE_POWERED_JOINTS_GAME_CALIBRATION_V1",
                physicsStep_s = SimulationConstants.FixedDeltaTimeSeconds,
                driveWriter = PoweredJointController.WriterId,
                driveWriterCount = 1,
                sourceClass = PoweredJointController.SourceClass,
                targetRotationConversion = PoweredJointController.CalibrationVersion + ": logical q_target_J -> inverse(q_target_J); neutral identity; local joint targets",
                targetAngularVelocityConversion = "logical omega_target_J rad/s -> -omega_target_J in Unity local target convention",
                useAcceleration = false,
                projectionMode = "None",
                poweredJointCount = _poweredController.PoweredJointCount,
                passiveJointCount = _poweredController.PassiveJointCount,
                familyProfiles = PoweredJointController.FamilyProfiles.Select(FamilyProfileRecord.From).ToArray(),
                joints = _poweredController.Joints.Select(PoweredJointRecord.From).ToArray(),
                qualification = new QualificationRecord
                {
                    passiveWholeBodyComDrop_m = _qualifiedPassiveComDropMeters,
                    poweredWholeBodyComDrop_m = _qualifiedPoweredComDropMeters,
                    positivePulseRelativeAngle_deg = _qualifiedPositivePulseDegrees,
                    metric = "Whole-body COM drop from identical neutral state at fixed short interval; pulse is calibrated J-frame signed twist.",
                    sourceClass = "ENGINE_RUNTIME_OBSERVATION"
                }
            };

            string fullPath = Path.GetFullPath(ArtifactPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(artifact, true) + Environment.NewLine);
        }

        public void RecordQualification(float passiveComDropMeters, float poweredComDropMeters, float positivePulseDegrees)
        {
            _qualifiedPassiveComDropMeters = passiveComDropMeters;
            _qualifiedPoweredComDropMeters = poweredComDropMeters;
            _qualifiedPositivePulseDegrees = positivePulseDegrees;
            WriteArtifact();
        }

        private static bool HasPoweredDrive(ConfigurableJoint joint) =>
            joint.angularXDrive.positionSpring != 0f || joint.angularXDrive.positionDamper != 0f || joint.angularXDrive.maximumForce != 0f ||
            joint.angularYZDrive.positionSpring != 0f || joint.angularYZDrive.positionDamper != 0f || joint.angularYZDrive.maximumForce != 0f ||
            joint.slerpDrive.positionSpring != 0f || joint.slerpDrive.positionDamper != 0f || joint.slerpDrive.maximumForce != 0f;

        private static SoftJointLimit Limit(float degrees) => new SoftJointLimit { limit = degrees, bounciness = 0f, contactDistance = 2f };
        private static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static Transform RequireBone(Animator animator, HumanBodyBones bone)
        {
            Transform transform = animator.GetBoneTransform(bone);
            return transform != null ? transform : throw new InvalidOperationException($"Required Humanoid bone '{bone}' did not resolve.");
        }

        private static void ValidateHumanoid(Animator animator, string role)
        {
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException($"The {role} rig requires a valid Humanoid Avatar.");
        }

        [Serializable]
        private sealed class Artifact
        {
            public string mission;
            public string profileId;
            public double physicsStep_s;
            public string driveWriter;
            public int driveWriterCount;
            public string sourceClass;
            public string targetRotationConversion;
            public string targetAngularVelocityConversion;
            public bool useAcceleration;
            public string projectionMode;
            public int poweredJointCount;
            public int passiveJointCount;
            public FamilyProfileRecord[] familyProfiles;
            public PoweredJointRecord[] joints;
            public QualificationRecord qualification;
        }

        [Serializable]
        private struct VectorRecord
        {
            public float x;
            public float y;
            public float z;
            public static VectorRecord From(Vector3 value) => new VectorRecord { x = value.x, y = value.y, z = value.z };
        }

        [Serializable]
        private struct QuaternionRecord
        {
            public float w;
            public float x;
            public float y;
            public float z;
            public static QuaternionRecord From(Quaternion value) => new QuaternionRecord
            {
                w = value.w,
                x = value.x,
                y = value.y,
                z = value.z
            };
        }

        [Serializable]
        private sealed class FamilyProfileRecord
        {
            public string id;
            public float spring;
            public float damper;
            public float baseCapacity_Nm;
            public float maxTargetRate_rad_s;
            public string sourceClass;

            public static FamilyProfileRecord From(JointFamilyProfile profile) => new FamilyProfileRecord
            {
                id = profile.Id,
                spring = profile.Spring,
                damper = profile.Damper,
                baseCapacity_Nm = profile.BaseCapacityNm,
                maxTargetRate_rad_s = profile.MaxTargetRateRadS,
                sourceClass = PoweredJointController.SourceClass
            };
        }

        [Serializable]
        private sealed class PoweredJointRecord
        {
            public string jointId;
            public string parentBody;
            public string childBody;
            public string family;
            public bool powered;
            public VectorRecord axis_B_child;
            public VectorRecord secondaryAxis_B_child;
            public QuaternionRecord neutralParentToChild;
            public QuaternionRecord jointSpaceBasis;
            public string driveMode;
            public float activation;
            public float capacityScale;
            public float configuredMaximumForceAtFullActivation_Nm;
            public float targetRateBound_rad_s;
            public string positiveConvention;
            public string targetRotationCalibration;
            public bool useAcceleration;
            public string projectionMode;

            public static PoweredJointRecord From(PoweredJointController.PoweredJointRuntime runtime)
            {
                JointFamilyProfile? profile = runtime.Profile;
                return new PoweredJointRecord
                {
                    jointId = runtime.Id + "_joint",
                    parentBody = runtime.Joint.connectedBody.name,
                    childBody = runtime.Id,
                    family = profile.HasValue ? profile.Value.Id : runtime.Recipe.Family,
                    powered = profile.HasValue,
                    axis_B_child = VectorRecord.From(runtime.Joint.axis),
                    secondaryAxis_B_child = VectorRecord.From(runtime.Joint.secondaryAxis),
                    neutralParentToChild = QuaternionRecord.From(runtime.NeutralParentToChild),
                    jointSpaceBasis = QuaternionRecord.From(runtime.JointSpace),
                    driveMode = profile.HasValue
                        ? (runtime.Recipe.Kind == PhysicalJointKind.Hinge ? "XAndYZ" : "Slerp")
                        : "Passive",
                    activation = runtime.Diagnostic.Activation,
                    capacityScale = runtime.Diagnostic.CapacityScale,
                    configuredMaximumForceAtFullActivation_Nm = profile.HasValue ? profile.Value.BaseCapacityNm : 0f,
                    targetRateBound_rad_s = profile.HasValue ? profile.Value.MaxTargetRateRadS : 0f,
                    positiveConvention = PositiveConvention(runtime.Recipe.Family),
                    targetRotationCalibration = PoweredJointController.CalibrationVersion,
                    useAcceleration = false,
                    projectionMode = runtime.Joint.projectionMode.ToString()
                };
            }

            private static string PositiveConvention(string family)
            {
                switch (family)
                {
                    case "ankle": return "dorsiflexion";
                    case "knee": return "flexion";
                    case "hip": return "flexion";
                    case "lumbar":
                    case "trunk": return "trunk flexion relative pelvis";
                    case "shoulder": return "humeral flexion in sagittal working plane";
                    case "elbow": return "flexion";
                    case "wrist": return "extension";
                    default: return "passive";
                }
            }
        }

        [Serializable]
        private sealed class QualificationRecord
        {
            public float passiveWholeBodyComDrop_m;
            public float poweredWholeBodyComDrop_m;
            public float positivePulseRelativeAngle_deg;
            public string metric;
            public string sourceClass;
        }

        public sealed class SegmentRuntime
        {
            public SegmentRuntime(PhysicalSegmentRecipe recipe, Rigidbody body, Collider collider, Renderer proxyRenderer, Vector3 dimensionsMeters, Vector3 bodyToVisiblePosition)
            {
                Recipe = recipe;
                Body = body;
                Collider = collider;
                ProxyRenderer = proxyRenderer;
                DimensionsMeters = dimensionsMeters;
                BodyToVisiblePosition = bodyToVisiblePosition;
            }
            public PhysicalSegmentRecipe Recipe { get; }
            public Rigidbody Body { get; }
            public Collider Collider { get; }
            public Renderer ProxyRenderer { get; }
            public Vector3 DimensionsMeters { get; }
            public Vector3 BodyToVisiblePosition { get; }
        }

        public sealed class JointRuntime
        {
            public JointRuntime(PhysicalJointRecipe recipe, ConfigurableJoint joint, Vector3 anchorWorld)
            {
                Recipe = recipe;
                Joint = joint;
                AnchorWorld = anchorWorld;
            }
            public PhysicalJointRecipe Recipe { get; }
            public ConfigurableJoint Joint { get; }
            public Vector3 AnchorWorld { get; }
        }

        private readonly struct VisibleFollowerBinding
        {
            public VisibleFollowerBinding(Rigidbody body, Transform visibleBone, Quaternion bodyToBoneRotation)
            {
                Body = body;
                VisibleBone = visibleBone;
                BodyToBoneRotation = bodyToBoneRotation;
            }
            public Rigidbody Body { get; }
            public Transform VisibleBone { get; }
            public Quaternion BodyToBoneRotation { get; }
        }

        private sealed class DebugMarker
        {
            public DebugMarker(Transform transform, Renderer renderer)
            {
                Transform = transform;
                Renderer = renderer;
            }
            public Transform Transform { get; }
            public Renderer Renderer { get; }
        }
    }
}
