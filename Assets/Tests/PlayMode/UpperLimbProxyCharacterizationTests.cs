using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H9F: Characterizes upper-limb proxy collision geometry, target pose
    /// feasibility, visible skin envelope, and baseline P0 forearm-thorax contact.
    /// </summary>
    public sealed class UpperLimbProxyCharacterizationTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation";

        private static readonly string[] MonitoredSegments =
        {
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand",
            "thorax", "abdomen", "pelvis"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
                _controller.enabled = false;
            if (_rig != null)
                _rig.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private IEnumerator LoadScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(PhysicalScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _controller.SetLoad(0f);
            _bootstrap.enabled = false;
        }

        private void Step(int tick)
        {
            ContactProbe.Tick = tick;
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void CaptureView(string filename, Vector3 cameraPos, Vector3 lookAtTarget)
        {
            Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (camera == null)
                return;

            Vector3 savedPos = camera.transform.position;
            Quaternion savedRot = camera.transform.rotation;

            camera.transform.position = cameraPos;
            camera.transform.rotation = Quaternion.LookRotation((lookAtTarget - cameraPos).normalized);

            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();

            Directory.CreateDirectory(Path.GetFullPath(EvidenceDirectory));
            File.WriteAllBytes(Path.Combine(Path.GetFullPath(EvidenceDirectory), filename), image.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            camera.transform.position = savedPos;
            camera.transform.rotation = savedRot;

            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }

        // ==================================================================
        // TEST 1: EXACT COLLIDER SEMANTICS AND INERTIA COUPLING AUDIT
        // ==================================================================
        [UnityTest]
        public IEnumerator T1_EXACT_COLLIDER_SEMANTICS_AUDIT()
        {
            yield return LoadScene();

            var csv = new StringBuilder();
            csv.AppendLine("segment_id,recipe_dimensions,collider_type,collider_center," +
                           "capsule_radius_m,capsule_height_m,capsule_direction," +
                           "box_size_m,box_center_m,body_scale,bone_length_m,coverage_ratio");

            foreach (string id in new[] { "left_forearm", "right_forearm", "thorax" })
            {
                PhysicalAthleteRig.SegmentRuntime segment = _rig.Segments[id];
                Collider col = segment.Collider;
                Vector3 dims = segment.DimensionsMeters;
                Vector3 scale = segment.Body.transform.lossyScale;

                Transform proximal = _rig.ReferenceAnimator.GetBoneTransform(segment.Recipe.ProximalBone);
                Transform distal = _rig.ReferenceAnimator.GetBoneTransform(segment.Recipe.DistalBone);
                float boneLength = proximal != null && distal != null ? Vector3.Distance(proximal.position, distal.position) : 0f;

                float capsuleRadius = 0f;
                float capsuleHeight = 0f;
                int capsuleDir = -1;
                Vector3 boxSize = Vector3.zero;
                Vector3 boxCenter = Vector3.zero;
                Vector3 colCenter = Vector3.zero;

                if (col is CapsuleCollider cc)
                {
                    capsuleRadius = cc.radius;
                    capsuleHeight = cc.height;
                    capsuleDir = cc.direction;
                    colCenter = cc.center;
                }
                else if (col is BoxCollider bc)
                {
                    boxSize = bc.size;
                    boxCenter = bc.center;
                    colCenter = bc.center;
                }

                float coverageRatio = boneLength > 0.0001f ? (col is CapsuleCollider ? capsuleHeight / boneLength : boxSize.y / boneLength) : 0f;

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},\"{1}\",{2},\"{3}\",{4:F4},{5:F4},{6},\"{7}\",\"{8}\",\"{9}\",{10:F4},{11:F4}",
                    id, segment.Recipe.DimensionsMeters, col.GetType().Name, colCenter,
                    capsuleRadius, capsuleHeight, capsuleDir,
                    boxSize, boxCenter, scale, boneLength, coverageRatio));

                Debug.Log($"[AUDIT] {id}: Type={col.GetType().Name}, RecipeDims={segment.Recipe.DimensionsMeters}, " +
                          $"CapsuleRadius={capsuleRadius:F4}m, CapsuleHeight={capsuleHeight:F4}m, " +
                          $"BoxSize={boxSize}, Center={colCenter}, BoneLength={boneLength:F4}m");
            }

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-upper-limb-collider-semantics.csv"), csv.ToString());
            yield return null;
        }

        // ==================================================================
        // TEST 2: TARGET-POSE COLLIDER FEASIBILITY (DIAGNOSTIC OVERLAP)
        // ==================================================================
        [UnityTest]
        public IEnumerator T2_TARGET_POSE_COLLIDER_FEASIBILITY()
        {
            yield return LoadScene();

            // Build GAM-10 calibration and solve at setup (phase = 0)
            SquatReferenceRigCalibration calibration = SquatReferenceRigCalibration.Build(
                _rig.ReferenceAnimator,
                _rig.ReferenceAnimator.transform.root,
                "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity");
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                calibration,
                profile.Evaluate(0f, SquatPhaseDirection.None),
                calibration.LeftFoot.PlantarAnchorWorld,
                calibration.RightFoot.PlantarAnchorWorld);
            Assert.That(solution.IsValid, Is.True, solution.RejectionReason);

            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(calibration, solution);
            Quaternion thoraxRot = solution.ChestFrameRotation * calibration.Chest.BoneFromAnatomicalFrame *
                Quaternion.Inverse(_rig.Segments["thorax"].BodyToReferenceBoneRotation);

            PhysicalUpperLimbTargets leftProj = SquatPhysicalUpperLimbProjection.Project(
                _rig, calibration, arms.Left, thoraxRot, isLeft: true);
            PhysicalUpperLimbTargets rightProj = SquatPhysicalUpperLimbProjection.Project(
                _rig, calibration, arms.Right, thoraxRot, isLeft: false);

            Debug.Log($"[TARGET POSE] Left projected flexion: {leftProj.FlexionDeg:F2} deg, Right: {rightProj.FlexionDeg:F2} deg");

            // Evaluate target pose bodies directly:
            // Calculate where upper_arm, forearm would be if joints reached their exact logical targets:
            // Thorax body:
            Transform thoraxTransform = _rig.Segments["thorax"].Body.transform;
            Collider thoraxCollider = _rig.Segments["thorax"].Collider;

            // Compute the target world positions/rotations of upper arm and forearm:
            // For left side:
            Quaternion leftUpperArmRot = ChildBodyRotationFromLogical("left_upper_arm", thoraxRot, leftProj.UpperArm);
            Quaternion leftForearmRot = ChildBodyRotationFromLogical("left_forearm", leftUpperArmRot, leftProj.Forearm);

            Vector3 leftShoulderAnchorWorld = JointAnchorWorld("left_upper_arm");
            Vector3 leftElbowAnchorWorld = leftShoulderAnchorWorld + leftUpperArmRot * AnchorOffsetInChild("left_upper_arm", "left_forearm");
            ConfigurableJoint leftForearmJoint = _rig.Segments["left_forearm"].Body.GetComponent<ConfigurableJoint>();
            Vector3 leftForearmComWorld = leftElbowAnchorWorld - leftForearmRot * leftForearmJoint.anchor;

            // Now measure geometric overlap at this exact target pose:
            Collider leftForearmCollider = _rig.Segments["left_forearm"].Collider;
            Collider rightForearmCollider = _rig.Segments["right_forearm"].Collider;

            Quaternion rightUpperArmRot = ChildBodyRotationFromLogical("right_upper_arm", thoraxRot, rightProj.UpperArm);
            Quaternion rightForearmRot = ChildBodyRotationFromLogical("right_forearm", rightUpperArmRot, rightProj.Forearm);
            Vector3 rightShoulderAnchorWorld = JointAnchorWorld("right_upper_arm");
            Vector3 rightElbowAnchorWorld = rightShoulderAnchorWorld + rightUpperArmRot * AnchorOffsetInChild("right_upper_arm", "right_forearm");
            ConfigurableJoint rightForearmJoint = _rig.Segments["right_forearm"].Body.GetComponent<ConfigurableJoint>();
            Vector3 rightForearmComWorld = rightElbowAnchorWorld - rightForearmRot * rightForearmJoint.anchor;

            // Overlap query for left forearm at target pose:
            bool leftOverlaps = Physics.ComputePenetration(
                leftForearmCollider, leftForearmComWorld, leftForearmRot,
                thoraxCollider, thoraxTransform.position, thoraxTransform.rotation,
                out Vector3 leftDir, out float leftDist);

            // Overlap query for right forearm at target pose:
            bool rightOverlaps = Physics.ComputePenetration(
                rightForearmCollider, rightForearmComWorld, rightForearmRot,
                thoraxCollider, thoraxTransform.position, thoraxTransform.rotation,
                out Vector3 rightDir, out float rightDist);

            var csv = new StringBuilder();
            csv.AppendLine("side,target_flexion_deg,overlaps,penetration_depth_m,normal_x,normal_y,normal_z");
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "left,{0:F3},{1},{2:F5},{3:F3},{4:F3},{5:F3}",
                leftProj.FlexionDeg, leftOverlaps, leftDist, leftDir.x, leftDir.y, leftDir.z));
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "right,{0:F3},{1},{2:F5},{3:F3},{4:F3},{5:F3}",
                rightProj.FlexionDeg, rightOverlaps, rightDist, rightDir.x, rightDir.y, rightDir.z));

            Debug.Log($"[TARGET POSE OVERLAP] Left: Overlaps={leftOverlaps}, Depth={leftDist * 1000f:F2} mm, Normal={leftDir}");
            Debug.Log($"[TARGET POSE OVERLAP] Right: Overlaps={rightOverlaps}, Depth={rightDist * 1000f:F2} mm, Normal={rightDir}");

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-target-pose-feasibility.csv"), csv.ToString());
            yield return null;
        }

        private Vector3 JointAnchorWorld(string childId)
        {
            foreach (PhysicalAthleteRig.JointRuntime j in _rig.Joints)
                if (j.Recipe.ChildId == childId)
                    return j.Joint.transform.TransformPoint(j.Joint.anchor);
            return Vector3.zero;
        }

        private Vector3 AnchorOffsetInChild(string parentSegmentId, string childSegmentId)
        {
            Transform parentBody = _rig.Segments[parentSegmentId].Body.transform;
            Vector3 childAnchorWorld = JointAnchorWorld(childSegmentId);
            return Quaternion.Inverse(parentBody.rotation) * (childAnchorWorld - parentBody.position);
        }

        private Quaternion ChildBodyRotationFromLogical(string childId, Quaternion parentBodyRot, Quaternion logicalTarget)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(childId);
            return parentBodyRot * joint.NeutralParentToChild *
                   (joint.JointSpace * logicalTarget * Quaternion.Inverse(joint.JointSpace));
        }

        // ==================================================================
        // TEST 3: VISIBLE MESH FOREARM ENVELOPE MEASUREMENT
        // ==================================================================
        [UnityTest]
        public IEnumerator T3_VISIBLE_FOREARM_MESH_ENVELOPE()
        {
            yield return LoadScene();

            // Find SkinnedMeshRenderer
            SkinnedMeshRenderer smr = _rig.VisibleAnimator.GetComponentInChildren<SkinnedMeshRenderer>();
            Assert.That(smr, Is.Not.Null, "Visible SkinnedMeshRenderer not found.");

            Mesh mesh = smr.sharedMesh;
            if (mesh == null || !mesh.isReadable)
            {
                Debug.Log("[VISIBLE MESH ENVELOPE] Mesh is not readable at runtime. Using visual render qualification per Section 12.");
                var unreadableCsv = new StringBuilder();
                unreadableCsv.AppendLine("status,message");
                unreadableCsv.AppendLine("UNREADABLE,Mesh is not read/write enabled; visual render evidence used per Section 12");
                File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-visible-forearm-envelope.csv"), unreadableCsv.ToString());
                yield break;
            }

            Vector3[] vertices = mesh.vertices;
            BoneWeight[] weights = mesh.boneWeights;
            Transform[] bones = smr.bones;

            int leftForearmBoneIndex = -1;
            int rightForearmBoneIndex = -1;
            Transform leftForearmBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform rightForearmBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform leftHandBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHandBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightHand);

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == leftForearmBone) leftForearmBoneIndex = i;
                if (bones[i] == rightForearmBone) rightForearmBoneIndex = i;
            }

            var leftRadii = new List<float>();
            var rightRadii = new List<float>();

            // For vertices where forearm bone is dominant weight (> 0.5):
            for (int i = 0; i < vertices.Length; i++)
            {
                BoneWeight bw = weights[i];
                if ((bw.boneIndex0 == leftForearmBoneIndex && bw.weight0 > 0.5f) ||
                    (bw.boneIndex1 == leftForearmBoneIndex && bw.weight1 > 0.5f))
                {
                    Vector3 worldPos = smr.transform.TransformPoint(vertices[i]);
                    // Distance to left forearm bone axis (line between lowerArm and hand)
                    float dist = DistanceToSegment(worldPos, leftForearmBone.position, leftHandBone.position);
                    leftRadii.Add(dist);
                }

                if ((bw.boneIndex0 == rightForearmBoneIndex && bw.weight0 > 0.5f) ||
                    (bw.boneIndex1 == rightForearmBoneIndex && bw.weight1 > 0.5f))
                {
                    Vector3 worldPos = smr.transform.TransformPoint(vertices[i]);
                    float dist = DistanceToSegment(worldPos, rightForearmBone.position, rightHandBone.position);
                    rightRadii.Add(dist);
                }
            }

            leftRadii.Sort();
            rightRadii.Sort();

            float leftP50 = leftRadii.Count > 0 ? leftRadii[(int)(leftRadii.Count * 0.50f)] : 0f;
            float leftP95 = leftRadii.Count > 0 ? leftRadii[(int)(leftRadii.Count * 0.95f)] : 0f;
            float leftMax = leftRadii.Count > 0 ? leftRadii[leftRadii.Count - 1] : 0f;

            float rightP50 = rightRadii.Count > 0 ? rightRadii[(int)(rightRadii.Count * 0.50f)] : 0f;
            float rightP95 = rightRadii.Count > 0 ? rightRadii[(int)(rightRadii.Count * 0.95f)] : 0f;
            float rightMax = rightRadii.Count > 0 ? rightRadii[rightRadii.Count - 1] : 0f;

            Debug.Log($"[VISIBLE MESH ENVELOPE] Left Forearm: count={leftRadii.Count}, P50={leftP50 * 1000f:F1} mm, P95={leftP95 * 1000f:F1} mm, Max={leftMax * 1000f:F1} mm");
            Debug.Log($"[VISIBLE MESH ENVELOPE] Right Forearm: count={rightRadii.Count}, P50={rightP50 * 1000f:F1} mm, P95={rightP95 * 1000f:F1} mm, Max={rightMax * 1000f:F1} mm");
            Debug.Log($"[VISIBLE MESH ENVELOPE] Runtime Capsule Radius: {0.0475f * 1000f:F1} mm");

            var csv = new StringBuilder();
            csv.AppendLine("side,sample_count,radius_p50_m,radius_p95_m,radius_max_m,capsule_radius_m");
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "left,{0},{1:F4},{2:F4},{3:F4},{4:F4}",
                leftRadii.Count, leftP50, leftP95, leftMax, 0.0475f));
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "right,{0},{1:F4},{2:F4},{3:F4},{4:F4}",
                rightRadii.Count, rightP50, rightP95, rightMax, 0.0475f));

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-visible-forearm-envelope.csv"), csv.ToString());
            yield return null;
        }

        private static float DistanceToSegment(Vector3 point, Vector3 segA, Vector3 segB)
        {
            Vector3 v = segB - segA;
            Vector3 w = point - segA;
            float c1 = Vector3.Dot(w, v);
            if (c1 <= 0) return Vector3.Distance(point, segA);
            float c2 = Vector3.Dot(v, v);
            if (c2 <= c1) return Vector3.Distance(point, segB);
            float b = c1 / c2;
            Vector3 pb = segA + b * v;
            return Vector3.Distance(point, pb);
        }

        // ==================================================================
        // TEST 4: P0 BASELINE CONTACT GEOMETRY & FULL 10-SECOND HARNESS
        // ==================================================================
        [UnityTest]
        public IEnumerator T4_P0_BASELINE_CONTACT_GEOMETRY()
        {
            yield return LoadScene();
            ContactCensus census = ContactCensus.Attach(_rig, MonitoredSegments);

            var trace = new StringBuilder();
            trace.AppendLine("tick,time_s,left_elbow_deg,right_elbow_deg,left_elbow_err,right_elbow_err," +
                             "left_shoulder_err,right_shoulder_err,left_wrist_err,right_wrist_err");

            Collider leftForearmCol = _rig.Segments["left_forearm"].Collider;
            Collider rightForearmCol = _rig.Segments["right_forearm"].Collider;
            Collider thoraxCol = _rig.Segments["thorax"].Collider;

            float maxPenetrationLeft = 0f;
            float maxPenetrationRight = 0f;
            Vector3 maxPenDirLeft = Vector3.zero;
            Vector3 maxPenDirRight = Vector3.zero;

            for (int tick = 0; tick <= 1000; tick++)
            {
                // Measure live penetration
                if (Physics.ComputePenetration(
                    leftForearmCol, leftForearmCol.transform.position, leftForearmCol.transform.rotation,
                    thoraxCol, thoraxCol.transform.position, thoraxCol.transform.rotation,
                    out Vector3 dirL, out float distL))
                {
                    if (distL > maxPenetrationLeft)
                    {
                        maxPenetrationLeft = distL;
                        maxPenDirLeft = dirL;
                    }
                }

                if (Physics.ComputePenetration(
                    rightForearmCol, rightForearmCol.transform.position, rightForearmCol.transform.rotation,
                    thoraxCol, thoraxCol.transform.position, thoraxCol.transform.rotation,
                    out Vector3 dirR, out float distR))
                {
                    if (distR > maxPenetrationRight)
                    {
                        maxPenetrationRight = distR;
                        maxPenDirRight = dirR;
                    }
                }

                // Render views at t = 0, 200 (2s), 500 (5s), 1000 (10s)
                if (tick == 0 || tick == 200 || tick == 500 || tick == 1000)
                {
                    yield return null;
                    int ms = tick * 10;
                    CaptureView($"p0_front_t{ms}ms.png", new Vector3(0f, 1.2f, 2.2f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"p0_side_t{ms}ms.png", new Vector3(2.2f, 1.2f, 0f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"p0_oblique_t{ms}ms.png", new Vector3(1.6f, 1.3f, 1.6f), new Vector3(0f, 1.05f, 0f));
                    CaptureView($"p0_leftarm_t{ms}ms.png", new Vector3(-0.7f, 1.25f, 0.7f), new Vector3(-0.3f, 1.15f, 0f));
                    CaptureView($"p0_rightarm_t{ms}ms.png", new Vector3(0.7f, 1.25f, 0.7f), new Vector3(0.3f, 1.15f, 0f));
                }

                if (tick % 25 == 0)
                {
                    float leftElbow = ElbowDegrees("left_forearm");
                    float rightElbow = ElbowDegrees("right_forearm");
                    float leftElbowErr = JointErrorDeg("left_forearm");
                    float rightElbowErr = JointErrorDeg("right_forearm");
                    float leftShErr = JointErrorDeg("left_upper_arm");
                    float rightShErr = JointErrorDeg("right_upper_arm");
                    float leftWrErr = JointErrorDeg("left_hand");
                    float rightWrErr = JointErrorDeg("right_hand");

                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F2},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3}",
                        tick, tick * 0.01f, leftElbow, rightElbow, leftElbowErr, rightElbowErr,
                        leftShErr, rightShErr, leftWrErr, rightWrErr));
                }

                if (tick < 1000)
                    Step(tick);
            }

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-p0-contact-trace.csv"), trace.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-p0-contact-census.csv"), census.ToCsv());

            Debug.Log($"[P0 RESULT] Final Left Elbow: {ElbowDegrees("left_forearm"):F3} deg, Right: {ElbowDegrees("right_forearm"):F3} deg");
            Debug.Log($"[P0 RESULT] Max Penetration Left: {maxPenetrationLeft * 1000f:F2} mm, Right: {maxPenetrationRight * 1000f:F2} mm");
            foreach (string ranked in census.Ranked(6))
                Debug.Log($"[P0 CONTACT] {ranked}");

            yield return null;
        }

        private float ElbowDegrees(string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            return Quaternion.Angle(Quaternion.identity, joint.Diagnostic.ActualRelative);
        }

        private float JointErrorDeg(string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            return joint.Diagnostic.ErrorRad.magnitude * Mathf.Rad2Deg;
        }

        // ==================================================================
        // CONTACT CENSUS AND PROBE
        // ==================================================================
        private sealed class ContactCensus
        {
            private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();

            public sealed class Record
            {
                public string A;
                public string B;
                public int FirstTick = -1;
                public int LastTick;
                public int TickCount;
                public float MaxImpulse;
                public float TotalImpulse;
                public Vector3 Point;
                public Vector3 Normal;
            }

            public static ContactCensus Attach(PhysicalAthleteRig rig, IEnumerable<string> segmentIds)
            {
                var census = new ContactCensus();
                var names = new Dictionary<Rigidbody, string>();
                foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in rig.Segments)
                    if (entry.Value.Body != null)
                        names[entry.Value.Body] = entry.Key;

                foreach (string id in segmentIds)
                {
                    ContactProbe probe = rig.Segments[id].Body.gameObject.AddComponent<ContactProbe>();
                    probe.Initialise(census, id, names);
                }
                return census;
            }

            public void Report(string a, string b, float impulse, Vector3 point, Vector3 normal)
            {
                string key = string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
                if (!_records.TryGetValue(key, out Record record))
                {
                    record = new Record { A = a, B = b, FirstTick = ContactProbe.Tick };
                    _records[key] = record;
                }
                record.LastTick = ContactProbe.Tick;
                record.TickCount++;
                record.TotalImpulse += impulse;
                if (impulse >= record.MaxImpulse)
                {
                    record.MaxImpulse = impulse;
                    record.Point = point;
                    record.Normal = normal;
                }
            }

            public IEnumerable<string> Ranked(int count)
            {
                var ordered = new List<Record>(_records.Values);
                ordered.Sort((x, y) => y.TotalImpulse.CompareTo(x.TotalImpulse));
                for (int index = 0; index < ordered.Count && index < count; index++)
                {
                    Record record = ordered[index];
                    yield return $"{record.A} <-> {record.B}: ticks={record.TickCount} " +
                                 $"maxImpulse={record.MaxImpulse:F2} totalImpulse={record.TotalImpulse:F1} " +
                                 $"first={record.FirstTick} last={record.LastTick} normal={record.Normal}";
                }
            }

            public string ToCsv()
            {
                var report = new StringBuilder();
                report.AppendLine("body_a,body_b,first_tick,last_tick,tick_count," +
                                  "max_normal_impulse,total_normal_impulse,point_x,point_y,point_z," +
                                  "normal_x,normal_y,normal_z");
                var ordered = new List<Record>(_records.Values);
                ordered.Sort((x, y) => y.TotalImpulse.CompareTo(x.TotalImpulse));
                foreach (Record record in ordered)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5:F3},{6:F3},{7:F4},{8:F4},{9:F4},{10:F3},{11:F3},{12:F3}",
                        record.A, record.B, record.FirstTick, record.LastTick, record.TickCount,
                        record.MaxImpulse, record.TotalImpulse,
                        record.Point.x, record.Point.y, record.Point.z,
                        record.Normal.x, record.Normal.y, record.Normal.z));
                }
                return report.ToString();
            }
        }

        private sealed class ContactProbe : MonoBehaviour
        {
            public static int Tick;

            private ContactCensus _census;
            private string _id;
            private Dictionary<Rigidbody, string> _names;

            public void Initialise(ContactCensus census, string id, Dictionary<Rigidbody, string> names)
            {
                _census = census;
                _id = id;
                _names = names;
            }

            private void OnCollisionStay(Collision collision) => Record(collision);
            private void OnCollisionEnter(Collision collision) => Record(collision);

            private void Record(Collision collision)
            {
                if (_census == null || collision.contactCount == 0)
                    return;
                string other = collision.rigidbody != null && _names.TryGetValue(collision.rigidbody, out string name)
                    ? name
                    : collision.collider != null ? collision.collider.name : "world";
                ContactPoint contact = collision.GetContact(0);
                _census.Report(_id, other, collision.impulse.magnitude, contact.point, contact.normal);
            }
        }
    }
}
