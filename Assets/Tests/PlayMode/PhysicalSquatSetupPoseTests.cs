using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Qualifies the physical setup pose the athlete actually holds at s_q = 0
    /// once the upper limbs follow the accepted GAM-10 reference, and renders
    /// it so the pose can be looked at rather than inferred.
    ///
    /// The upper limbs are 10 kg on a 100 kg athlete. Moving them out of the
    /// T-pose moves the whole-body centre of mass, so this is also where that
    /// plant change is measured, before any standing control is rederived
    /// against it.
    /// </summary>
    public sealed class PhysicalSquatSetupPoseTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/upper-limb-setup-pose";
        private const string MeasurementPath = "Artifacts/Measurements/GAM11-upper-limb-setup-pose.csv";
        private const string PlantMeasurementPath = "Artifacts/Measurements/GAM11-upper-limb-plant-change.csv";

        private static readonly Dictionary<string, HumanBodyBones> VisibleArmBones = new()
        {
            { "left_upper_arm", HumanBodyBones.LeftUpperArm },
            { "right_upper_arm", HumanBodyBones.RightUpperArm },
            { "left_forearm", HumanBodyBones.LeftLowerArm },
            { "right_forearm", HumanBodyBones.RightLowerArm },
            { "left_hand", HumanBodyBones.LeftHand },
            { "right_hand", HumanBodyBones.RightHand }
        };

        private static readonly string[] UpperLimbJointIds =
        {
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand"
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

        private static void CaptureFrame(string path)
        {
            Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            if (camera == null)
                return;

            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            Object.DestroyImmediate(image);
            target.Release();
            Object.DestroyImmediate(target);
        }

        private Vector3 SystemCenterOfMass(out float totalMass)
        {
            Vector3 weighted = Vector3.zero;
            totalMass = 0f;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in _rig.Segments)
            {
                Rigidbody body = entry.Value.Body;
                if (body == null)
                    continue;
                weighted += body.worldCenterOfMass * body.mass;
                totalMass += body.mass;
            }
            return totalMass > 0f ? weighted / totalMass : Vector3.zero;
        }

        /// <summary>
        /// Where the whole-body centre of mass would sit if the arm chain were
        /// still commanded to its neutral identity, with every other segment
        /// left exactly where it is. This isolates the plant change the upper
        /// limb repair causes from the settling that happens alongside it.
        /// </summary>
        private Vector3 CounterfactualNeutralArmCenterOfMass()
        {
            // Parent-first, so each segment inherits the neutral orientation
            // its parent would have had rather than the one it actually holds.
            string[][] chain =
            {
                new[] { "left_upper_arm", "thorax" },
                new[] { "right_upper_arm", "thorax" },
                new[] { "left_forearm", "left_upper_arm" },
                new[] { "right_forearm", "right_upper_arm" },
                new[] { "left_hand", "left_forearm" },
                new[] { "right_hand", "right_forearm" }
            };

            var neutralRotation = new Dictionary<string, Quaternion>();
            var neutralCom = new Dictionary<string, Vector3>();
            foreach (string[] link in chain)
            {
                Rigidbody body = _rig.Segments[link[0]].Body;
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(link[0]);
                Quaternion parentRotation = neutralRotation.TryGetValue(link[1], out Quaternion inherited)
                    ? inherited
                    : _rig.Segments[link[1]].Body.rotation;
                Quaternion rotation = parentRotation * joint.NeutralParentToChild;
                neutralRotation[link[0]] = rotation;

                // The joint anchor is the one point the segment shares with its
                // parent, so a neutral segment pivots about it. When the parent
                // itself moved, the anchor rides with the parent.
                Vector3 anchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                if (neutralRotation.ContainsKey(link[1]) && neutralCom.ContainsKey(link[1]))
                {
                    Rigidbody parentBody = _rig.Segments[link[1]].Body;
                    anchor = neutralCom[link[1]] + neutralRotation[link[1]] *
                        (Quaternion.Inverse(parentBody.rotation) * (anchor - parentBody.worldCenterOfMass));
                }
                Vector3 com = anchor + rotation *
                    (Quaternion.Inverse(body.rotation) * (body.worldCenterOfMass - anchor));
                neutralCom[link[0]] = com;
            }

            Vector3 weighted = Vector3.zero;
            float totalMass = 0f;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in _rig.Segments)
            {
                Rigidbody body = entry.Value.Body;
                if (body == null)
                    continue;
                weighted += (neutralCom.TryGetValue(entry.Key, out Vector3 moved)
                    ? moved
                    : body.worldCenterOfMass) * body.mass;
                totalMass += body.mass;
            }
            return totalMass > 0f ? weighted / totalMass : Vector3.zero;
        }

        [UnityTest]
        public IEnumerator PHYSICAL_SETUP_POSE_TEN_SECOND_SMOKE_WITH_RENDERED_EVIDENCE()
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "The setup pose has to be looked at. Run this suite without -nographics.");

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

            // Production configuration, unloaded, s_q = 0. Nothing overridden.
            _controller.SetLoad(0f);
            _bootstrap.enabled = false;
            SquatPhysicalAdapter adapter = _controller.Adapter;

            Directory.CreateDirectory(Path.GetFullPath(EvidenceDirectory));
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(MeasurementPath)));

            var report = new StringBuilder();
            report.AppendLine("t_s,joint,target_deg,tracking_error_deg");
            var posture = new StringBuilder();
            posture.AppendLine("t_s,com_x,com_y,com_z,com_ap_error_m,com_ml_error_m," +
                               "pelvis_height_m,neck_error_deg,left_contact,right_contact,failure_reason");
            int[] captureTicks = { 0, 200, 500, 1000 };
            int captureIndex = 0;
            Dictionary<string, (Quaternion Bone, Quaternion Body)> visibleAtStart = SampleVisibleArmPose();
            var worstErrorDeg = new Dictionary<string, float>();
            foreach (string jointId in UpperLimbJointIds)
                worstErrorDeg[jointId] = 0f;

            for (int tick = 0; tick <= 1000; tick++)
            {
                foreach (string jointId in UpperLimbJointIds)
                {
                    PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
                    float errorDeg = joint.Diagnostic.ErrorRad.magnitude * Mathf.Rad2Deg;
                    worstErrorDeg[jointId] = Mathf.Max(worstErrorDeg[jointId], errorDeg);
                }

                if (captureIndex < captureTicks.Length && tick == captureTicks[captureIndex])
                {
                    yield return null;
                    float seconds = tick * (float)SimulationConstants.FixedDeltaTimeSeconds;
                    CaptureFrame(Path.Combine(
                        Path.GetFullPath(EvidenceDirectory), $"setup-pose-t{tick * 10}ms.png"));

                    foreach (string jointId in UpperLimbJointIds)
                    {
                        PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0:F1},{1},{2:F4},{3:F4}", seconds, jointId,
                            Quaternion.Angle(Quaternion.identity, joint.Diagnostic.AppliedTarget),
                            joint.Diagnostic.ErrorRad.magnitude * Mathf.Rad2Deg));
                    }

                    Vector3 com = SystemCenterOfMass(out _);
                    float neckErrorDeg = Quaternion.Angle(
                        _rig.PoweredController.GetJoint("head_neck").Diagnostic.ActualRelative,
                        Quaternion.identity);
                    posture.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F1},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F3},{8},{9},{10}",
                        seconds, com.x, com.y, com.z,
                        adapter.ApComError, adapter.MlComError,
                        _rig.Segments["pelvis"].Body.position.y, neckErrorDeg,
                        _controller.LeftFootContact != null && _controller.LeftFootContact.IsInContact,
                        _controller.RightFootContact != null && _controller.RightFootContact.IsInContact,
                        adapter.FailureReason));
                    captureIndex++;
                }

                if (tick < 1000)
                {
                    _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                    if (_controller.LeftFootContact != null)
                        _controller.LeftFootContact.PhysicsTickUpdate(dt);
                    if (_controller.RightFootContact != null)
                        _controller.RightFootContact.PhysicsTickUpdate(dt);
                }
            }

            Vector3 setupCom = SystemCenterOfMass(out float totalMass);
            Vector3 neutralArmCom = CounterfactualNeutralArmCenterOfMass();
            Vector3 delta = setupCom - neutralArmCom;
            var plant = new StringBuilder();
            plant.AppendLine("quantity,t_pose_arms,gam10_setup_arms,delta");
            plant.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "com_ap_m,{0:F5},{1:F5},{2:F5}", neutralArmCom.z, setupCom.z, delta.z));
            plant.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "com_ml_m,{0:F5},{1:F5},{2:F5}", neutralArmCom.x, setupCom.x, delta.x));
            plant.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "com_height_m,{0:F5},{1:F5},{2:F5}", neutralArmCom.y, setupCom.y, delta.y));
            plant.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "system_mass_kg,{0:F3},{0:F3},0", totalMass));
            File.WriteAllText(Path.GetFullPath(PlantMeasurementPath), plant.ToString());
            File.WriteAllText(Path.GetFullPath(MeasurementPath), report + posture.ToString());

            foreach (string jointId in UpperLimbJointIds)
                Debug.Log($"[SETUP POSE] {jointId} max tracking error {worstErrorDeg[jointId]:F3} deg");
            Debug.Log($"[SETUP POSE] COM AP T-pose={neutralArmCom.z:F5} m, GAM-10 setup={setupCom.z:F5} m, " +
                      $"delta={delta.z:F5} m; ML delta={delta.x:F5} m; height delta={delta.y:F5} m");
            Debug.Log($"[SETUP POSE] captured to {EvidenceDirectory}");

            AssertVisibleArmsFollowTheirBodies(visibleAtStart, SampleVisibleArmPose());

            float pelvisY = _rig.Segments["pelvis"].Body.position.y;
            Assert.That(pelvisY, Is.GreaterThan(0.9f),
                $"The athlete did not survive ten seconds of the corrected setup pose (pelvisY={pelvisY:F4}).");
            yield return null;
        }

        /// <summary>
        /// A physical arm in the accepted pose is worth nothing if the visible
        /// humanoid does not follow it, and the owner judges the visible
        /// humanoid.
        ///
        /// Body and bone carry different calibration offsets, so this compares
        /// how far each of them turned over the run rather than their absolute
        /// orientations.
        /// </summary>
        private Dictionary<string, (Quaternion Bone, Quaternion Body)> SampleVisibleArmPose()
        {
            Animator visible = _rig.VisibleAnimator;
            var sample = new Dictionary<string, (Quaternion, Quaternion)>();
            foreach (KeyValuePair<string, HumanBodyBones> entry in VisibleArmBones)
            {
                Transform bone = visible.GetBoneTransform(entry.Value);
                Assert.That(bone, Is.Not.Null, $"The visible humanoid has no {entry.Value} bone.");
                sample[entry.Key] = (bone.rotation, _rig.Segments[entry.Key].Body.rotation);
            }
            return sample;
        }

        private static void AssertVisibleArmsFollowTheirBodies(
            Dictionary<string, (Quaternion Bone, Quaternion Body)> before,
            Dictionary<string, (Quaternion Bone, Quaternion Body)> after)
        {
            foreach (KeyValuePair<string, HumanBodyBones> entry in VisibleArmBones)
            {
                (Quaternion Bone, Quaternion Body) from = before[entry.Key];
                (Quaternion Bone, Quaternion Body) to = after[entry.Key];
                Quaternion boneTurn = to.Bone * Quaternion.Inverse(from.Bone);
                Quaternion bodyTurn = to.Body * Quaternion.Inverse(from.Body);
                float followerErrorDeg = Quaternion.Angle(boneTurn, bodyTurn);
                Debug.Log($"[VISIBLE FOLLOWER] {entry.Key} turned {Quaternion.Angle(Quaternion.identity, bodyTurn):F2} deg; " +
                          $"visible bone lagged it by {followerErrorDeg:F3} deg");
                Assert.That(followerErrorDeg, Is.LessThanOrEqualTo(1f),
                    $"The visible {entry.Value} turned {followerErrorDeg:F3} deg differently from the " +
                    $"physical body {entry.Key} it is supposed to follow.");
            }
        }
    }
}
