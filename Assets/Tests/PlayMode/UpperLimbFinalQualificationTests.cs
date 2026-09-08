using System;
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
    /// Phase 5H9F: Final physical squat setup pose qualification with codified
    /// PhysicalAthleteSelfCollisionPolicy active.
    /// </summary>
    public sealed class UpperLimbFinalQualificationTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string PassiveScene = "PhysicalAthletePhysics";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/upper-limb-proxy-reconciliation";

        private static readonly string[] MonitoredSegments =
        {
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand",
            "thorax", "abdomen", "pelvis",
            "head_neck", "left_thigh", "right_thigh"
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
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
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

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _controller.SetLoad(0f);
            _bootstrap.enabled = false;
        }

        private void Step(int tick)
        {
            FinalContactProbe.Tick = tick;
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void CaptureView(string filename, Vector3 cameraPos, Vector3 lookAtTarget)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
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

            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
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

        private sealed class FinalContactCensus
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

            public static FinalContactCensus Attach(PhysicalAthleteRig rig, IEnumerable<string> segmentIds)
            {
                var census = new FinalContactCensus();
                var names = new Dictionary<Rigidbody, string>();
                foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in rig.Segments)
                    if (entry.Value.Body != null)
                        names[entry.Value.Body] = entry.Key;

                foreach (string id in segmentIds)
                {
                    FinalContactProbe probe = rig.Segments[id].Body.gameObject.AddComponent<FinalContactProbe>();
                    probe.Initialise(census, id, names);
                }
                return census;
            }

            public void Report(string a, string b, float impulse, Vector3 point, Vector3 normal)
            {
                string key = string.CompareOrdinal(a, b) <= 0 ? $"{a}|{b}" : $"{b}|{a}";
                if (!_records.TryGetValue(key, out Record record))
                {
                    record = new Record { A = a, B = b, FirstTick = FinalContactProbe.Tick };
                    _records[key] = record;
                }
                record.LastTick = FinalContactProbe.Tick;
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

            public int TotalContacts()
            {
                int sum = 0;
                foreach (Record r in _records.Values) sum += r.TickCount;
                return sum;
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

        private sealed class FinalContactProbe : MonoBehaviour
        {
            public static int Tick;
            private FinalContactCensus _census;
            private string _id;
            private Dictionary<Rigidbody, string> _names;

            public void Initialise(FinalContactCensus census, string id, Dictionary<Rigidbody, string> names)
            {
                _census = census;
                _id = id;
                _names = names;
            }

            private void OnCollisionStay(Collision collision) => Record(collision);
            private void OnCollisionEnter(Collision collision) => Record(collision);

            private void Record(Collision collision)
            {
                if (_census == null || collision.contactCount == 0) return;
                string other = collision.rigidbody != null && _names.TryGetValue(collision.rigidbody, out string name)
                    ? name
                    : collision.collider != null ? collision.collider.name : "world";
                ContactPoint contact = collision.GetContact(0);
                _census.Report(_id, other, collision.impulse.magnitude, contact.point, contact.normal);
            }
        }

        [UnityTest]
        public IEnumerator FINAL_PHYSICAL_SQUAT_SETUP_QUALIFICATION()
        {
            LogAssert.ignoreFailingMessages = true;
            yield return LoadScene();
            FinalContactCensus census = FinalContactCensus.Attach(_rig, MonitoredSegments);

            var trace = new StringBuilder();
            trace.AppendLine("tick,time_s,left_elbow_deg,right_elbow_deg,left_elbow_err,right_elbow_err," +
                             "left_shoulder_err,right_shoulder_err,left_wrist_err,right_wrist_err,com_ap,cop_ap,margin_ap,slip_mps");

            float maxSlip = 0f;

            for (int tick = 0; tick <= 1000; tick++)
            {
                // Capture views at t = 0, 200 (2s), 500 (5s), 1000 (10s)
                if (tick == 0 || tick == 200 || tick == 500 || tick == 1000)
                {
                    yield return null;
                    int ms = tick * 10;
                    CaptureView($"final_front_t{ms}ms.png", new Vector3(0f, 1.2f, 2.2f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"final_side_t{ms}ms.png", new Vector3(2.2f, 1.2f, 0f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"final_oblique_t{ms}ms.png", new Vector3(1.6f, 1.4f, 1.6f), new Vector3(0f, 1.0f, 0f));
                    CaptureView($"final_leftarm_t{ms}ms.png", new Vector3(-1.0f, 1.3f, 0.5f), new Vector3(-0.3f, 1.2f, 0.0f));
                    CaptureView($"final_rightarm_t{ms}ms.png", new Vector3(1.0f, 1.3f, 0.5f), new Vector3(0.3f, 1.2f, 0.0f));
                }

                if (tick < 1000)
                    Step(tick);

                // Sample metrics
                float lElbowDeg = ElbowDegrees("left_forearm");
                float rElbowDeg = ElbowDegrees("right_forearm");
                float lElbowErr = JointErrorDeg("left_forearm");
                float rElbowErr = JointErrorDeg("right_forearm");
                float lShoulderErr = JointErrorDeg("left_upper_arm");
                float rShoulderErr = JointErrorDeg("right_upper_arm");
                float lWristErr = JointErrorDeg("left_hand");
                float rWristErr = JointErrorDeg("right_hand");

                SquatPhysicalAdapter adapter = _controller.Adapter;
                float comAp = adapter != null ? adapter.ApComError : 0f;
                float copAp = adapter != null && adapter.Balance != null && adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : 0f;
                float marginAp = adapter != null && adapter.Balance != null ? Mathf.Min(adapter.Balance.ComApMarginFront, adapter.Balance.ComApMarginRear) : 0f;

                float leftFootSlip = _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f;
                float rightFootSlip = _controller.RightFootContact != null ? _controller.RightFootContact.SlipSpeed : 0f;
                float tickSlip = Mathf.Max(leftFootSlip, rightFootSlip);
                if (tickSlip > maxSlip) maxSlip = tickSlip;

                if (tick % 10 == 0)
                {
                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2},{10:F4},{11:F4},{12:F4},{13:F4}",
                        tick, tick * 0.01f, lElbowDeg, rElbowDeg, lElbowErr, rElbowErr,
                        lShoulderErr, rShoulderErr, lWristErr, rWristErr,
                        comAp, copAp, marginAp, tickSlip));
                }
            }

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-final-contact-trace.csv"), trace.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-final-contact-census.csv"), census.ToCsv());

            // Measure final joint angles
            float finalLElbowDeg = ElbowDegrees("left_forearm");
            float finalRElbowDeg = ElbowDegrees("right_forearm");
            float finalLElbowErr = JointErrorDeg("left_forearm");
            float finalRElbowErr = JointErrorDeg("right_forearm");
            float finalLShoulderErr = JointErrorDeg("left_upper_arm");
            float finalRShoulderErr = JointErrorDeg("right_upper_arm");
            float finalLWristErr = JointErrorDeg("left_hand");
            float finalRWristErr = JointErrorDeg("right_hand");

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

            Transform leftHandBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftHand);
            Transform rightHandBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightHand);
            Transform leftLowerArmBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
            Transform rightLowerArmBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            Transform chestBone = _rig.VisibleAnimator.GetBoneTransform(HumanBodyBones.Chest);

            float leftHandToBar = Vector3.Distance(leftHandBone.position, arms.BarCenter);
            float rightHandToBar = Vector3.Distance(rightHandBone.position, arms.BarCenter);
            float leftHandToChest = Vector3.Distance(leftHandBone.position, chestBone.position);
            float rightHandToChest = Vector3.Distance(rightHandBone.position, chestBone.position);

            var taskCsv = new StringBuilder();
            taskCsv.AppendLine("metric,left_value_m,right_value_m");
            taskCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "hand_to_bar_distance_m,{0:F4},{1:F4}", leftHandToBar, rightHandToBar));
            taskCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "hand_to_upper_back_distance_m,{0:F4},{1:F4}", leftHandToChest, rightHandToChest));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-final-task-space.csv"), taskCsv.ToString());

            int thoraxForearmCount = census.GetPairTicks("left_forearm", "thorax") + census.GetPairTicks("right_forearm", "thorax");

            // Posture comparison
            var postureCsv = new StringBuilder();
            postureCsv.AppendLine("metric,p0_baseline,final_qualified,target,error");
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "left_elbow_deg,91.24,{0:F2},105.80,{1:F2}", finalLElbowDeg, finalLElbowErr));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "right_elbow_deg,91.24,{0:F2},105.80,{1:F2}", finalRElbowDeg, finalRElbowErr));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "left_shoulder_err_deg,18.42,{0:F2},0.00,{0:F2}", finalLShoulderErr));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "right_shoulder_err_deg,18.42,{0:F2},0.00,{0:F2}", finalRShoulderErr));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "left_wrist_err_deg,12.15,{0:F2},0.00,{0:F2}", finalLWristErr));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "right_wrist_err_deg,12.15,{0:F2},0.00,{0:F2}", finalRWristErr));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "thorax_forearm_contacts,760,{0},0,{0}", thoraxForearmCount));
            postureCsv.AppendLine(string.Format(CultureInfo.InvariantCulture, "max_foot_slip_mps,0.0025,{0:F4},0.0100,{0:F4}", maxSlip));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-final-posture-comparison.csv"), postureCsv.ToString());

            Debug.Log($"[FINAL QUALIFICATION] Left Elbow: {finalLElbowDeg:F2} deg (err {finalLElbowErr:F2}), Right Elbow: {finalRElbowDeg:F2} deg (err {finalRElbowErr:F2})");
            Debug.Log($"[FINAL QUALIFICATION] Thorax-forearm contacts: {thoraxForearmCount}, Max slip: {maxSlip:F4} m/s");

            // ASSERTIONS
            Assert.That(thoraxForearmCount, Is.EqualTo(0), "Thorax-forearm collision count must be 0 with qualified collision policy");
            Assert.That(finalLElbowDeg, Is.InRange(104f, 110f), $"Left elbow angle {finalLElbowDeg:F2} deg out of range");
            Assert.That(finalRElbowDeg, Is.InRange(104f, 110f), $"Right elbow angle {finalRElbowDeg:F2} deg out of range");
            Assert.That(_rig.Segments["pelvis"].Body.position.y, Is.GreaterThan(0.9f), "Pelvis height survived 10s setup");
        }

        [UnityTest]
        public IEnumerator FINAL_PASSIVE_FALL_GATE()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(PassiveScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            Assert.That(rig, Is.Not.Null);

            float initialComY = rig.CalculateWholeBodyCom().y;
            rig.ReleasePassive();

            yield return new WaitForSecondsRealtime(1.0f);

            float fallingComY = rig.CalculateWholeBodyCom().y;
            Assert.That(fallingComY, Is.LessThan(initialComY - 0.04f),
                $"Passive fall gate failed: Com Y dropped from {initialComY:F4} to {fallingComY:F4} (delta = {initialComY - fallingComY:F4} m, required > 0.04 m)");
        }
    }
}
