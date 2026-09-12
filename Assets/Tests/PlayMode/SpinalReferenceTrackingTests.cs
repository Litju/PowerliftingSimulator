using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
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
    /// Phase 5H15, sections 21 to 23.
    ///
    /// The static holds show the bias doing its job at rest. This runs the
    /// moving squat, on the same driven descent H14 measured so the two are
    /// directly comparable, and captures the frames the visual question is
    /// decided on.
    ///
    /// The visual question is whether the physical athlete moved toward the
    /// accepted GAM-10 spinal configuration, not whether it looks upright.
    /// GAM-10 asks for 35.97 deg of world trunk pitch at the bottom and that
    /// forward lean is the movement, not the defect.
    /// </summary>
    public sealed class SpinalReferenceTrackingTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H15";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/h15-spine";
        private const float ApproachPhaseRate = 0.30f;

        /// <summary>
        /// Reference world trunk pitch on the accepted GAM-10 pose. Provenance:
        /// GAM11-5h12-r1-reference-com-trajectory.csv, same pelvis-to-thorax
        /// definition the physical side uses.
        /// </summary>
        private static readonly float[] ReferencePhases =
        {
            0.00f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.35f, 0.40f, 0.45f, 0.50f,
            0.55f, 0.60f, 0.65f, 0.70f, 0.75f, 0.80f, 0.85f, 0.90f, 0.95f, 1.00f
        };

        private static readonly float[] ReferenceWorldTrunkPitchDeg =
        {
            3.91f, 6.30f, 8.91f, 11.59f, 14.19f, 16.56f, 18.93f, 21.56f, 24.27f, 26.91f, 29.32f,
            31.34f, 32.17f, 32.52f, 33.25f, 34.04f, 34.72f, 35.16f, 35.47f, 35.73f, 35.97f
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;
        private float _platformTopY;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;

            GameObject platform = GameObject.Find("PhysicalPlatform_GAM6");
            Assert.That(platform, Is.Not.Null);
            _platformTopY = platform.GetComponent<Collider>().bounds.max.y;
        }

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

        [UnityTest]
        public IEnumerator V1_SPINE_REFERENCE_TRACKING_DYNAMIC()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H15 V1 DYNAMIC SPINAL REFERENCE TRACKING");
            report.AppendLine("Driven descent at the production phase rate, the same trace H14 measured,");
            report.AppendLine("so the before and after are like for like. abdErr and thoErr are actual");
            report.AppendLine("minus GAM-10 nominal; trunkErr is world pitch minus the reference pitch.");
            report.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,sq,abd_nominal,abd_bias,abd_actual,abd_err," +
                           "tho_nominal,tho_bias,tho_actual,tho_err,hip_nominal,hip_actual,hip_err," +
                           "world_trunk,ref_trunk,trunk_err,support_length,heel_clr_l,heel_clr_r," +
                           "com_ap,cop_ap,contacts,saddle_sep,saddle_attached,abd_demand,tho_demand");

            foreach (float load in new[] { 0f, 25f })
                yield return RunDescent(load, report, csv);

            WriteMeasurement("spine-dynamic-tracking.txt", report.ToString());
            WriteMeasurement("spine-dynamic-tracking.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator RunDescent(float loadKg, StringBuilder report, StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thoraxBody = _rig.Segments["thorax"].Body;
            PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            var captureStops = new List<float> { 0.00f, 0.25f, 0.55f, 0.80f, 1.00f };
            int nextStop = 0;

            report.AppendLine("--- load " + loadKg.ToString("F0", CultureInfo.InvariantCulture) + " kg ---");
            report.AppendLine("  s_q   abdNom  abdBias  abdAct  abdErr |  thoNom  thoBias  thoAct  thoErr |" +
                              "  trunk   refTrunk  trunkErr |  hipErr  support  contacts");

            float sq = 0f;
            int tick = 0;
            while (tick < 420)
            {
                if (nextStop < captureStops.Count && sq >= captureStops[nextStop] - 1e-4f)
                {
                    yield return null;
                    string tag = string.Format(CultureInfo.InvariantCulture,
                        "h15_{0:F0}kg_sq{1:F2}", loadKg, captureStops[nextStop]);
                    Capture(camera, tag + "_side.png", new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                    Capture(camera, tag + "_oblique.png", new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                    report.AppendLine(Line(adapter, powered, balance, pelvis, thoraxBody, sq));
                    nextStop++;
                    continue;
                }

                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;

                if (tick % 4 != 0)
                    continue;

                adapter.TryGetTargetComposition("abdomen", out var abdomen);
                adapter.TryGetTargetComposition("thorax", out var thorax);
                adapter.TryGetTargetComposition("left_thigh", out var hip);
                float abdomenActual = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
                float thoraxActual = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);
                float hipActual = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative);
                Vector3 axis = thoraxBody.position - pelvis.position;
                float worldTrunk = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
                SquatBarSaddle saddle = _controller.Saddle;

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1},{2:F4},{3:F3},{4:F3},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10:F3}," +
                    "{11:F3},{12:F3},{13:F3},{14:F3},{15:F3},{16:F3},{17:F5},{18:F5},{19:F5}," +
                    "{20:F5},{21:F5},{22},{23:F5},{24},{25:F4},{26:F4}",
                    loadKg, tick, sq,
                    TwistX(abdomen.Nominal), TwistX(abdomen.GravityBias), abdomenActual,
                    abdomenActual - TwistX(abdomen.Nominal),
                    TwistX(thorax.Nominal), TwistX(thorax.GravityBias), thoraxActual,
                    thoraxActual - TwistX(thorax.Nominal),
                    TwistX(hip.Nominal), hipActual, hipActual - TwistX(hip.Nominal),
                    worldTrunk, SampleReference(sq), worldTrunk - SampleReference(sq),
                    balance.SupportApLength, HeelClearance("left_foot"), HeelClearance("right_foot"),
                    balance.SystemCom.z,
                    balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter,
                    balance.SupportContactCount,
                    saddle != null ? saddle.SaddleSeparationMeters : 0f,
                    saddle != null && saddle.IsAttached ? 1 : 0,
                    powered.GetJoint("abdomen").Diagnostic.ModeledDemand,
                    powered.GetJoint("thorax").Diagnostic.ModeledDemand));
            }

            report.AppendLine();
            yield return null;
        }

        private string Line(
            SquatPhysicalAdapter adapter, PoweredJointController powered, SquatBalanceObserver balance,
            Rigidbody pelvis, Rigidbody thoraxBody, float sq)
        {
            adapter.TryGetTargetComposition("abdomen", out var abdomen);
            adapter.TryGetTargetComposition("thorax", out var thorax);
            adapter.TryGetTargetComposition("left_thigh", out var hip);
            float abdomenNominal = TwistX(abdomen.Nominal);
            float thoraxNominal = TwistX(thorax.Nominal);
            float hipNominal = TwistX(hip.Nominal);
            float abdomenActual = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
            float thoraxActual = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);
            float hipActual = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative);
            Vector3 axis = thoraxBody.position - pelvis.position;
            float worldTrunk = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
            float reference = SampleReference(sq);

            return string.Format(CultureInfo.InvariantCulture,
                "  {0,4:F2}  {1,6:F2}  {2,7:F2}  {3,6:F2}  {4,6:F2} |  {5,6:F2}  {6,7:F2}  {7,6:F2}  {8,6:F2} |" +
                "  {9,6:F2}  {10,8:F2}  {11,8:F2} |  {12,6:F2}  {13,7:F4}  {14,8}",
                sq, abdomenNominal, TwistX(abdomen.GravityBias), abdomenActual, abdomenActual - abdomenNominal,
                thoraxNominal, TwistX(thorax.GravityBias), thoraxActual, thoraxActual - thoraxNominal,
                worldTrunk, reference, worldTrunk - reference,
                hipActual - hipNominal, balance.SupportApLength, balance.SupportContactCount);
        }

        private float HeelClearance(string footId)
        {
            var box = (BoxCollider)_rig.Segments[footId].Collider;
            Vector3 c = box.center;
            Vector3 h = 0.5f * box.size;
            Vector3 heel = box.transform.TransformPoint(new Vector3(c.x, c.y - h.y, c.z - h.z));
            return heel.y - _platformTopY;
        }

        private static float SampleReference(float sq)
        {
            if (sq <= ReferencePhases[0])
                return ReferenceWorldTrunkPitchDeg[0];
            for (int i = 1; i < ReferencePhases.Length; i++)
            {
                if (sq > ReferencePhases[i])
                    continue;
                float t = Mathf.InverseLerp(ReferencePhases[i - 1], ReferencePhases[i], sq);
                return Mathf.Lerp(ReferenceWorldTrunkPitchDeg[i - 1], ReferenceWorldTrunkPitchDeg[i], t);
            }
            return ReferenceWorldTrunkPitchDeg[ReferenceWorldTrunkPitchDeg.Length - 1];
        }

        private static void Capture(Camera camera, string filename, Vector3 position, Vector3 lookAt)
        {
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            var texture = new RenderTexture(1280, 720, 24);
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = texture;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            RenderTexture.active = previous;
            camera.targetTexture = null;

            string directory = Path.Combine(Directory.GetCurrentDirectory(), EvidenceDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
        }

        private static float TwistX(Quaternion rotation)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(rotation);
            var vector = new Vector3(q.x, q.y, q.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            var twist = new Quaternion(projection.x, projection.y, projection.z, q.w);
            float magnitude = Mathf.Sqrt(
                twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(
                twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }
    }
}
