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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H9F: Discriminates collider geometry from collision policy across:
    /// Diagnostic A: Exact pair suppression (left/right forearm <-> thorax).
    /// Diagnostic B: Forearm capsule radius sweep (100%, 90%, 80%, 70%, 50%, point).
    /// Diagnostic C: Thorax proxy sensitivity / penetration geometry.
    /// </summary>
    public sealed class UpperLimbProxyDiscriminationTests
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
        // DIAGNOSTIC A: EXACT PAIR SUPPRESSION
        // ==================================================================
        [UnityTest]
        public IEnumerator D1_DIAGNOSTIC_A_EXACT_PAIR_SUPPRESSION()
        {
            yield return LoadScene();

            // Disable ONLY bilateral forearm <-> thorax collision
            Collider leftForearmCol = _rig.Segments["left_forearm"].Collider;
            Collider rightForearmCol = _rig.Segments["right_forearm"].Collider;
            Collider thoraxCol = _rig.Segments["thorax"].Collider;

            Physics.IgnoreCollision(leftForearmCol, thoraxCol, true);
            Physics.IgnoreCollision(rightForearmCol, thoraxCol, true);

            ContactCensus census = ContactCensus.Attach(_rig, MonitoredSegments);

            var trace = new StringBuilder();
            trace.AppendLine("tick,time_s,left_elbow_deg,right_elbow_deg,left_elbow_err,right_elbow_err," +
                             "left_shoulder_err,right_shoulder_err,left_wrist_err,right_wrist_err," +
                             "pelvis_y,com_x,com_y,com_z");

            for (int tick = 0; tick <= 1000; tick++)
            {
                if (tick == 0 || tick == 200 || tick == 500 || tick == 1000)
                {
                    yield return null;
                    int ms = tick * 10;
                    CaptureView($"pair_suppression_front_t{ms}ms.png", new Vector3(0f, 1.2f, 2.2f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"pair_suppression_side_t{ms}ms.png", new Vector3(2.2f, 1.2f, 0f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"pair_suppression_oblique_t{ms}ms.png", new Vector3(1.6f, 1.3f, 1.6f), new Vector3(0f, 1.05f, 0f));
                    CaptureView($"pair_suppression_leftarm_t{ms}ms.png", new Vector3(-0.7f, 1.25f, 0.7f), new Vector3(-0.3f, 1.15f, 0f));
                    CaptureView($"pair_suppression_rightarm_t{ms}ms.png", new Vector3(0.7f, 1.25f, 0.7f), new Vector3(0.3f, 1.15f, 0f));
                }

                if (tick % 25 == 0)
                {
                    Vector3 com = _rig.CalculateWholeBodyCom();
                    float pelvisY = _rig.Segments["pelvis"].Body.position.y;
                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F2},{2:F3},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10:F4},{11:F4},{12:F4},{13:F4}",
                        tick, tick * 0.01f,
                        ElbowDegrees("left_forearm"), ElbowDegrees("right_forearm"),
                        JointErrorDeg("left_forearm"), JointErrorDeg("right_forearm"),
                        JointErrorDeg("left_upper_arm"), JointErrorDeg("right_upper_arm"),
                        JointErrorDeg("left_hand"), JointErrorDeg("right_hand"),
                        pelvisY, com.x, com.y, com.z));
                }

                if (tick < 1000)
                    Step(tick);
            }

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-pair-suppression-trace.csv"), trace.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-pair-suppression-census.csv"), census.ToCsv());

            float finalLeftElbow = ElbowDegrees("left_forearm");
            float finalRightElbow = ElbowDegrees("right_forearm");
            float finalLeftElbowErr = JointErrorDeg("left_forearm");
            float finalRightElbowErr = JointErrorDeg("right_forearm");
            float finalLeftShoulderErr = JointErrorDeg("left_upper_arm");
            float finalRightShoulderErr = JointErrorDeg("right_upper_arm");
            float finalLeftWristErr = JointErrorDeg("left_hand");
            float finalRightWristErr = JointErrorDeg("right_hand");

            Debug.Log($"[DIAGNOSTIC A RESULT] Left Elbow: {finalLeftElbow:F3} deg (err: {finalLeftElbowErr:F3} deg), " +
                      $"Right Elbow: {finalRightElbow:F3} deg (err: {finalRightElbowErr:F3} deg)");
            Debug.Log($"[DIAGNOSTIC A RESULT] Shoulder Error: L={finalLeftShoulderErr:F3} deg, R={finalRightShoulderErr:F3} deg");
            Debug.Log($"[DIAGNOSTIC A RESULT] Wrist Error: L={finalLeftWristErr:F3} deg, R={finalRightWristErr:F3} deg");

            // Evaluate accepted GAM-10 task-space metrics:
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
            SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(calibration, solution);

            Debug.Log($"[REF POS] UpperChest: {SquatReferenceUpperLimb.UpperChestBonePosition(calibration, solution)}");
            Debug.Log($"[REF POS] BarCenter: {arms.BarCenter}");
            Debug.Log($"[REF POS] Left Shoulder: {arms.Left.ShoulderCenter}");
            Debug.Log($"[REF POS] Left Elbow: {arms.Left.ElbowCenter}");
            Debug.Log($"[REF POS] Left Hand: {arms.Left.HandCenter}");
            Debug.Log($"[REF POS] Right Shoulder: {arms.Right.ShoulderCenter}");
            Debug.Log($"[REF POS] Right Elbow: {arms.Right.ElbowCenter}");
            Debug.Log($"[REF POS] Right Hand: {arms.Right.HandCenter}");

            // Left side task-space
            Transform leftHandBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform leftLowerArmBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Vector3 leftHandCenter = leftHandBone.position;
            Vector3 leftElbowCenter = leftLowerArmBone.position;
            Vector3 leftForearmDir = (leftHandCenter - leftElbowCenter).normalized;
            Vector3 leftPalmNormal = leftHandBone.rotation * Vector3.left;

            float leftHandCenterErr = Vector3.Distance(leftHandCenter, arms.Left.HandCenter);
            float leftElbowCenterErr = Vector3.Distance(leftElbowCenter, arms.Left.ElbowCenter);
            float leftForearmDirErr = Vector3.Angle(leftForearmDir, (arms.Left.HandCenter - arms.Left.ElbowCenter).normalized);
            Vector3 acceptedLeftPalmNormal = arms.Left.HandBoneRotation * Vector3.left;
            float leftPalmNormalErr = Vector3.Angle(leftPalmNormal, acceptedLeftPalmNormal);
            float leftPalmOrientationErr = Quaternion.Angle(leftHandBone.rotation, arms.Left.HandBoneRotation);
            float leftPalmBarSurfaceErr = Mathf.Abs(Vector3.Distance(leftHandCenter, arms.BarCenter) - 0.0145f);

            // Right side task-space
            Transform rightHandBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform rightLowerArmBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Vector3 rightHandCenter = rightHandBone.position;
            Vector3 rightElbowCenter = rightLowerArmBone.position;
            Vector3 rightForearmDir = (rightHandCenter - rightElbowCenter).normalized;
            Vector3 rightPalmNormal = rightHandBone.rotation * Vector3.right;

            float rightHandCenterErr = Vector3.Distance(rightHandCenter, arms.Right.HandCenter);
            float rightElbowCenterErr = Vector3.Distance(rightElbowCenter, arms.Right.ElbowCenter);
            float rightForearmDirErr = Vector3.Angle(rightForearmDir, (arms.Right.HandCenter - arms.Right.ElbowCenter).normalized);
            Vector3 acceptedRightPalmNormal = arms.Right.HandBoneRotation * Vector3.right;
            float rightPalmNormalErr = Vector3.Angle(rightPalmNormal, acceptedRightPalmNormal);
            float rightPalmOrientationErr = Quaternion.Angle(rightHandBone.rotation, arms.Right.HandBoneRotation);
            float rightPalmBarSurfaceErr = Mathf.Abs(Vector3.Distance(rightHandCenter, arms.BarCenter) - 0.0145f);

            var taskCsv = new StringBuilder();
            taskCsv.AppendLine("side,hand_center_err_m,elbow_center_err_m,forearm_dir_err_deg,palm_normal_err_deg,palm_orient_err_deg,palm_bar_surf_err_m");
            taskCsv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "left,{0:F5},{1:F5},{2:F3},{3:F3},{4:F3},{5:F5}",
                leftHandCenterErr, leftElbowCenterErr, leftForearmDirErr, leftPalmNormalErr, leftPalmOrientationErr, leftPalmBarSurfaceErr));
            taskCsv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "right,{0:F5},{1:F5},{2:F3},{3:F3},{4:F3},{5:F5}",
                rightHandCenterErr, rightElbowCenterErr, rightForearmDirErr, rightPalmNormalErr, rightPalmOrientationErr, rightPalmBarSurfaceErr));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-pair-suppression-task-space.csv"), taskCsv.ToString());

            Debug.Log($"[TASK SPACE LEFT] handErr={leftHandCenterErr * 1000f:F2} mm, elbowErr={leftElbowCenterErr * 1000f:F2} mm, " +
                      $"forearmDirErr={leftForearmDirErr:F2} deg, palmNormalErr={leftPalmNormalErr:F2} deg");
            Debug.Log($"[TASK SPACE RIGHT] handErr={rightHandCenterErr * 1000f:F2} mm, elbowErr={rightElbowCenterErr * 1000f:F2} mm, " +
                      $"forearmDirErr={rightForearmDirErr:F2} deg, palmNormalErr={rightPalmNormalErr:F2} deg");

            Assert.That(finalLeftElbow, Is.GreaterThan(100f),
                $"Pair suppression did not free the left elbow: actual was {finalLeftElbow:F3} deg.");
            Assert.That(finalRightElbow, Is.GreaterThan(100f),
                $"Pair suppression did not free the right elbow: actual was {finalRightElbow:F3} deg.");

            yield return null;
        }

        // ==================================================================
        // DIAGNOSTIC B: FOREARM RADIUS SWEEP
        // ==================================================================
        [UnityTest]
        public IEnumerator D2_DIAGNOSTIC_B_FOREARM_RADIUS_SWEEP()
        {
            var sweepReport = new StringBuilder();
            sweepReport.AppendLine("factor,radius_m,settled_left_elbow_deg,settled_right_elbow_deg," +
                                   "left_elbow_err,right_elbow_err,left_contact_ticks,right_contact_ticks," +
                                   "target_pose_penetration_m");

            float[] factors = { 1.0f, 0.9f, 0.8f, 0.7f, 0.5f, 0.1f, 0.02f };
            float baseRadius = 0.0475f;

            foreach (float factor in factors)
            {
                yield return LoadScene();

                CapsuleCollider leftCap = _rig.Segments["left_forearm"].Collider as CapsuleCollider;
                CapsuleCollider rightCap = _rig.Segments["right_forearm"].Collider as CapsuleCollider;
                float currentRadius = baseRadius * factor;

                // PRESERVE mass and inertia explicitly:
                // Modify ONLY collider radius:
                leftCap.radius = currentRadius;
                rightCap.radius = currentRadius;

                // Target pose penetration measurement for this radius:
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
                SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(calibration, solution);
                Quaternion thoraxRot = solution.ChestFrameRotation * calibration.Chest.BoneFromAnatomicalFrame *
                    Quaternion.Inverse(_rig.Segments["thorax"].BodyToReferenceBoneRotation);

                PhysicalUpperLimbTargets leftProj = SquatPhysicalUpperLimbProjection.Project(
                    _rig, calibration, arms.Left, thoraxRot, isLeft: true);
                Quaternion leftUpperArmRot = ChildBodyRotationFromLogical("left_upper_arm", thoraxRot, leftProj.UpperArm);
                Quaternion leftForearmRot = ChildBodyRotationFromLogical("left_forearm", leftUpperArmRot, leftProj.Forearm);
                Vector3 leftShoulderAnchorWorld = JointAnchorWorld("left_upper_arm");
                Vector3 leftElbowAnchorWorld = leftShoulderAnchorWorld + leftUpperArmRot * AnchorOffsetInChild("left_upper_arm", "left_forearm");
                ConfigurableJoint leftForearmJoint = _rig.Segments["left_forearm"].Body.GetComponent<ConfigurableJoint>();
                Vector3 leftForearmComWorld = leftElbowAnchorWorld - leftForearmRot * leftForearmJoint.anchor;

                Collider thoraxCollider = _rig.Segments["thorax"].Collider;
                bool overlapsAtTarget = Physics.ComputePenetration(
                    leftCap, leftForearmComWorld, leftForearmRot,
                    thoraxCollider, _rig.Segments["thorax"].Body.transform.position, _rig.Segments["thorax"].Body.transform.rotation,
                    out _, out float targetPenDepth);

                ContactCensus census = ContactCensus.Attach(_rig, new[] { "left_forearm", "right_forearm", "thorax" });

                // Run 400 ticks (4 seconds) to measure settled angle
                for (int tick = 0; tick < 400; tick++)
                    Step(tick);

                float leftElbow = ElbowDegrees("left_forearm");
                float rightElbow = ElbowDegrees("right_forearm");
                float leftErr = JointErrorDeg("left_forearm");
                float rightErr = JointErrorDeg("right_forearm");

                int leftTicks = census.GetPairTicks("left_forearm", "thorax");
                int rightTicks = census.GetPairTicks("right_forearm", "thorax");

                sweepReport.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1:F4},{2:F3},{3:F3},{4:F3},{5:F3},{6},{7},{8:F5}",
                    factor, currentRadius, leftElbow, rightElbow, leftErr, rightErr,
                    leftTicks, rightTicks, overlapsAtTarget ? targetPenDepth : 0f));

                Debug.Log($"[SWEEP factor={factor:F2} radius={currentRadius * 1000f:F1}mm] " +
                          $"leftElbow={leftElbow:F2} deg (err: {leftErr:F2}), " +
                          $"targetPen={targetPenDepth * 1000f:F1}mm, contactTicks={leftTicks}");
            }

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-forearm-radius-sweep.csv"), sweepReport.ToString());
            yield return null;
        }

        // ==================================================================
        // DIAGNOSTIC C: THORAX PROXY SENSITIVITY
        // ==================================================================
        [UnityTest]
        public IEnumerator D3_DIAGNOSTIC_C_THORAX_PROXY_SENSITIVITY()
        {
            var report = new StringBuilder();
            report.AppendLine("thorax_width_scale,thorax_width_m,target_pose_penetration_m,settled_elbow_deg,contact_ticks");

            float[] widthScales = { 1.0f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f, 0.3f };
            float baseWidth = 0.36f;

            foreach (float scale in widthScales)
            {
                yield return LoadScene();

                BoxCollider thoraxBox = _rig.Segments["thorax"].Collider as BoxCollider;
                float currentWidth = baseWidth * scale;

                // PRESERVE mass and inertia explicitly:
                thoraxBox.size = new Vector3(currentWidth, 0.28f, 0.20f);

                CapsuleCollider leftCap = _rig.Segments["left_forearm"].Collider as CapsuleCollider;

                // Target pose penetration
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
                SquatReferenceUpperLimbSolution arms = SquatReferenceUpperLimb.Solve(calibration, solution);
                Quaternion thoraxRot = solution.ChestFrameRotation * calibration.Chest.BoneFromAnatomicalFrame *
                    Quaternion.Inverse(_rig.Segments["thorax"].BodyToReferenceBoneRotation);

                PhysicalUpperLimbTargets leftProj = SquatPhysicalUpperLimbProjection.Project(
                    _rig, calibration, arms.Left, thoraxRot, isLeft: true);
                Quaternion leftUpperArmRot = ChildBodyRotationFromLogical("left_upper_arm", thoraxRot, leftProj.UpperArm);
                Quaternion leftForearmRot = ChildBodyRotationFromLogical("left_forearm", leftUpperArmRot, leftProj.Forearm);
                Vector3 leftShoulderAnchorWorld = JointAnchorWorld("left_upper_arm");
                Vector3 leftElbowAnchorWorld = leftShoulderAnchorWorld + leftUpperArmRot * AnchorOffsetInChild("left_upper_arm", "left_forearm");
                ConfigurableJoint leftForearmJoint = _rig.Segments["left_forearm"].Body.GetComponent<ConfigurableJoint>();
                Vector3 leftForearmComWorld = leftElbowAnchorWorld - leftForearmRot * leftForearmJoint.anchor;

                bool overlaps = Physics.ComputePenetration(
                    leftCap, leftForearmComWorld, leftForearmRot,
                    thoraxBox, _rig.Segments["thorax"].Body.transform.position, _rig.Segments["thorax"].Body.transform.rotation,
                    out _, out float penDepth);

                ContactCensus census = ContactCensus.Attach(_rig, new[] { "left_forearm", "thorax" });

                for (int tick = 0; tick < 400; tick++)
                    Step(tick);

                float leftElbow = ElbowDegrees("left_forearm");
                int ticks = census.GetPairTicks("left_forearm", "thorax");

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1:F4},{2:F5},{3:F3},{4}",
                    scale, currentWidth, overlaps ? penDepth : 0f, leftElbow, ticks));

                Debug.Log($"[THORAX SENSITIVITY scale={scale:F2} width={currentWidth * 1000f:F1}mm] " +
                          $"targetPen={penDepth * 1000f:F1}mm, elbow={leftElbow:F2} deg, contactTicks={ticks}");
            }

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-thorax-sensitivity.csv"), report.ToString());
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

            public int GetPairTicks(string a, string b)
            {
                string key = string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
                return _records.TryGetValue(key, out Record rec) ? rec.TickCount : 0;
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
