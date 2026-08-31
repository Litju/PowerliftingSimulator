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
        private const string ArtifactPath = "Artifacts/Measurements/GAM-6-physical-humanoid.json";

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
        private bool _inspectionFrozen;
        private string _status = "Building passive physical athlete...";

        public IReadOnlyDictionary<string, SegmentRuntime> Segments => _segments;
        public IReadOnlyList<JointRuntime> Joints => _joints;
        public bool IsInspectionFrozen => _inspectionFrozen;
        public float TotalMassKg => _segments.Values.Sum(segment => segment.Body.mass);
        public float MaxInitialNonAdjacentPenetrationMeters { get; private set; }

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

            DisableAdjacentSelfCollision();
            MaxInitialNonAdjacentPenetrationMeters = MeasureInitialNonAdjacentPenetration();
            CreatePlatformCollider();
            BuildVisibleFollower();
            CreateRuntimeDebugOverlay();
            ownership.ConfigurePhysicalRig(referenceAnimator, visibleAnimator.transform, _physicalRoot.transform);
            ApplyVisibility();
            ValidateRuntime();
            WriteArtifact();
            _status = "PASSIVE_RAGDOLL: gravity on, 16/16 bodies dynamic, drives off";
        }

        private void LateUpdate()
        {
            if (_segments.Count == 0)
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
            if (_segments.Count == 0)
                return;

            SetInspectionFrozen(false);
            foundation.Reset();
            foreach (SegmentRuntime segment in _segments.Values)
                segment.Body.WakeUp();
            _status = "RESET -> PASSIVE_RAGDOLL";
        }

        public void InspectNeutral()
        {
            if (_segments.Count == 0)
                return;

            foundation.Reset();
            SetInspectionFrozen(true);
            _status = "AUTHORING INSPECTION: explicitly frozen (not gameplay)";
        }

        public void ReleasePassive()
        {
            SetInspectionFrozen(false);
            foreach (SegmentRuntime segment in _segments.Values)
                segment.Body.WakeUp();
            _status = "PASSIVE_RAGDOLL: released under gravity";
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
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.angularXDrive = ZeroDrive();
            joint.angularYZDrive = ZeroDrive();
            joint.slerpDrive = ZeroDrive();
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
            GUILayout.BeginArea(new Rect(18f, 18f, 420f, 245f), GUI.skin.box);
            GUILayout.Label("GAM-6 Physical Athlete");
            GUILayout.Label(_status);
            GUILayout.Label($"Bodies: {_segments.Count}/16   Joints: {_joints.Count}/15   Mass: {TotalMassKg:F4} kg");
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Reset + Passive")) ResetPassive();
            if (GUILayout.Button("Inspect Neutral")) InspectNeutral();
            if (GUILayout.Button("Release")) ReleasePassive();
            GUILayout.EndHorizontal();
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
                mission = "POWERLIFTING_SIMULATOR_GAM_6_PHYSICAL_HUMANOID",
                profileId = PhysicalAthleteDefinition.ProfileId,
                bodyMass_kg = PhysicalAthleteDefinition.PrototypeBodyMassKg,
                assignedMass_kg = TotalMassKg,
                segmentCount = _segments.Count,
                jointCount = _joints.Count,
                gravity_m_per_s2 = Physics.gravity.y,
                physicsStep_s = SimulationConstants.FixedDeltaTimeSeconds,
                wholeAthleteComNeutral_W_m = VectorRecord.From(CalculateWholeBodyCom()),
                selfCollisionPolicy = "Adjacent parent-child collider pairs ignored explicitly; nonadjacent pairs remain enabled.",
                geometryAuthority = "Artifacts/Measurements/GAM-5-canonical-humanoid.json",
                massModel = "Frozen 16-segment fractions; 100 kg profile is GAME_CALIBRATION; no redistribution.",
                comModel = "Source-compatible de Leva longitudinal fractions applied to asset bone-pivot proxies where available, otherwise explicit engineering proxy centers; all runtime placements classified ENGINEERING_DERIVED.",
                inertiaModel = "Analytic equivalent-box principal inertia at each rigid-body COM, derived from the actual primitive proxy dimensions; ENGINEERING_DERIVED.",
                anthropometricSource = "Paolo de Leva (1996), Journal of Biomechanics 29(9):1223-1230, DOI 10.1016/0021-9290(95)00178-6; fractions are population means applied only as engineering proxy placements, not subject anatomy.",
                maxInitialNonAdjacentPenetration_m = MaxInitialNonAdjacentPenetrationMeters,
                segments = _segments.Values.Select(SegmentRecord.From).ToArray(),
                joints = _joints.Select(JointRecord.From).ToArray()
            };

            string fullPath = Path.GetFullPath(ArtifactPath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, JsonUtility.ToJson(artifact, true) + Environment.NewLine);
        }

        private static bool HasPoweredDrive(ConfigurableJoint joint) =>
            joint.angularXDrive.positionSpring != 0f || joint.angularXDrive.positionDamper != 0f || joint.angularXDrive.maximumForce != 0f ||
            joint.angularYZDrive.positionSpring != 0f || joint.angularYZDrive.positionDamper != 0f || joint.angularYZDrive.maximumForce != 0f ||
            joint.slerpDrive.positionSpring != 0f || joint.slerpDrive.positionDamper != 0f || joint.slerpDrive.maximumForce != 0f;

        private static JointDrive ZeroDrive() => new JointDrive { positionSpring = 0f, positionDamper = 0f, maximumForce = 0f };
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
            public float bodyMass_kg;
            public float assignedMass_kg;
            public int segmentCount;
            public int jointCount;
            public float gravity_m_per_s2;
            public double physicsStep_s;
            public VectorRecord wholeAthleteComNeutral_W_m;
            public string selfCollisionPolicy;
            public string geometryAuthority;
            public string massModel;
            public string comModel;
            public string inertiaModel;
            public string anthropometricSource;
            public float maxInitialNonAdjacentPenetration_m;
            public SegmentRecord[] segments;
            public JointRecord[] joints;
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
        private sealed class SegmentRecord
        {
            public string name;
            public string parent;
            public string proximalBoneProxy;
            public string distalBoneProxy;
            public float mass_kg;
            public float massFraction;
            public string massSourceClass;
            public string comSourceClass;
            public VectorRecord com_W_m;
            public string colliderType;
            public VectorRecord colliderDimensions_m;
            public VectorRecord inertiaTensor_kg_m2;
            public VectorRecord inertiaTensorRotation_xyz;

            public static SegmentRecord From(SegmentRuntime segment) => new SegmentRecord
            {
                name = segment.Recipe.Id,
                parent = segment.Recipe.ParentId,
                proximalBoneProxy = segment.Recipe.ProximalBone.ToString(),
                distalBoneProxy = segment.Recipe.DistalBone.ToString(),
                mass_kg = segment.Body.mass,
                massFraction = segment.Recipe.MassFraction,
                massSourceClass = "ENGINEERING_DERIVED",
                comSourceClass = "ENGINEERING_DERIVED",
                com_W_m = VectorRecord.From(segment.Body.worldCenterOfMass),
                colliderType = segment.Recipe.Collider.ToString(),
                colliderDimensions_m = VectorRecord.From(segment.DimensionsMeters),
                inertiaTensor_kg_m2 = VectorRecord.From(segment.Body.inertiaTensor),
                inertiaTensorRotation_xyz = VectorRecord.From(segment.Body.inertiaTensorRotation.eulerAngles)
            };
        }

        [Serializable]
        private sealed class JointRecord
        {
            public string name;
            public string family;
            public string parentBody;
            public string childBody;
            public VectorRecord anchor_W_m;
            public VectorRecord primaryAxis_W;
            public string motion;
            public float lowLimit_deg;
            public float highLimit_deg;
            public float secondaryLimit_deg;
            public string limitSourceClass;
            public string projectionMode;
            public string poweredDrive;

            public static JointRecord From(JointRuntime runtime) => new JointRecord
            {
                name = runtime.Recipe.ChildId + "_joint",
                family = runtime.Recipe.Family,
                parentBody = runtime.Joint.connectedBody.name,
                childBody = runtime.Recipe.ChildId,
                anchor_W_m = VectorRecord.From(runtime.AnchorWorld),
                primaryAxis_W = VectorRecord.From(runtime.Recipe.PrimaryAxisWorld),
                motion = runtime.Recipe.Kind.ToString(),
                lowLimit_deg = runtime.Recipe.LowDegrees,
                highLimit_deg = runtime.Recipe.HighDegrees,
                secondaryLimit_deg = runtime.Recipe.SecondaryLimitDegrees,
                limitSourceClass = "GAME_CALIBRATION",
                projectionMode = runtime.Joint.projectionMode.ToString(),
                poweredDrive = "DISABLED"
            };
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
