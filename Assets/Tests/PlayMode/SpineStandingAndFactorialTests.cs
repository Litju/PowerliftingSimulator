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
    /// Phase 5H16, sections 6, 7 and 9.
    ///
    /// Two H15 residuals are on trial. The standing spine bias was measured
    /// and deliberately withheld, and the gravity bias moves the target
    /// position without appearing in the target velocity. B1 tests the first
    /// alone at standing, B2 crosses both on the full descent so the causal
    /// contribution of each is separable.
    /// </summary>
    public sealed class SpineStandingAndFactorialTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H16";
        private const float ApproachPhaseRate = 0.30f;

        /// <summary>
        /// Closed-loop standing requirement solved in 5H15 from J u = -e0.
        /// Provenance: GAM11-5h15 spine-coupled-response.csv, s_q 0.00 rows.
        /// </summary>
        private const float StandingAbdomen0Kg = 0.83f;
        private const float StandingThorax0Kg = 0.92f;
        private const float StandingAbdomen25Kg = 3.93f;
        private const float StandingThorax25Kg = 3.67f;

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
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
            {
                SquatPhysicalAdapter adapter = _controller.Adapter;
                if (adapter != null)
                {
                    adapter.SpineBiasRateFeedforwardEnabled = false;
                    ClearStanding(adapter);
                }
                _controller.enabled = false;
            }
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

        private static void ClearStanding(SquatPhysicalAdapter adapter)
        {
            adapter.Preload.StandingAbdomenBiasDegrees0Kg = 0f;
            adapter.Preload.StandingThoraxBiasDegrees0Kg = 0f;
            adapter.Preload.StandingAbdomenBiasDegrees25Kg = 0f;
            adapter.Preload.StandingThoraxBiasDegrees25Kg = 0f;
        }

        private static void ApplyStanding(SquatPhysicalAdapter adapter, bool enabled)
        {
            adapter.Preload.StandingAbdomenBiasDegrees0Kg = enabled ? StandingAbdomen0Kg : 0f;
            adapter.Preload.StandingThoraxBiasDegrees0Kg = enabled ? StandingThorax0Kg : 0f;
            adapter.Preload.StandingAbdomenBiasDegrees25Kg = enabled ? StandingAbdomen25Kg : 0f;
            adapter.Preload.StandingThoraxBiasDegrees25Kg = enabled ? StandingThorax25Kg : 0f;
        }

        // ------------------------------------------------------------------
        // B1. Standing authorization, section 6.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B1_STANDING_SPINE_BIAS_AUTHORIZATION()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H16 B1 STANDING SPINE BIAS AUTHORIZATION");
            report.AppendLine("Ten second standing hold, three independent fresh resets per case,");
            report.AppendLine("production balance on. The only variable is the standing spine knot.");
            report.AppendLine("Values are the mean of the three resets over the last 100 ticks.");
            report.AppendLine();
            report.AppendLine("load  bias      abdErr  thoErr  trunkErr  hipErr  comAP    copAP    support  contacts  drift");

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,standing_bias,reset,abd_err,tho_err,world_trunk,ref_trunk,trunk_err," +
                           "hip_err,com_ap,cop_ap,support_length,contacts,abd_drift,tho_drift," +
                           "abd_demand,tho_demand,ankle_balance_deg,saddle_sep,saddle_attached");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (bool enabled in new[] { false, true })
                {
                    var runs = new List<StandingResult>();
                    for (int reset = 0; reset < 3; reset++)
                    {
                        var result = new StandingResult[1];
                        yield return StandingHold(load, enabled, reset, result, csv);
                        runs.Add(result[0]);
                    }

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,4:F0}  {1,-8}  {2,6:F2}  {3,6:F2}  {4,8:F2}  {5,6:F2}  {6,7:F4}  {7,7:F4}  {8,7:F4}  {9,8}  {10,6:F3}",
                        load, enabled ? "measured" : "zero",
                        Mean(runs, r => r.AbdomenError), Mean(runs, r => r.ThoraxError),
                        Mean(runs, r => r.TrunkError), Mean(runs, r => r.HipError),
                        Mean(runs, r => r.ComAp), Mean(runs, r => r.CopAp),
                        Mean(runs, r => r.SupportLength), runs[0].Contacts,
                        Mean(runs, r => Mathf.Abs(r.AbdomenDrift))));
                }
            }

            WriteMeasurement("standing-bias-authorization.txt", report.ToString());
            WriteMeasurement("standing-bias-authorization.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private struct StandingResult
        {
            public float AbdomenError;
            public float ThoraxError;
            public float TrunkError;
            public float HipError;
            public float ComAp;
            public float CopAp;
            public float SupportLength;
            public int Contacts;
            public float AbdomenDrift;
            public float ThoraxDrift;
        }

        private static float Mean(List<StandingResult> runs, Func<StandingResult, float> select)
        {
            float sum = 0f;
            foreach (StandingResult r in runs)
                sum += select(r);
            return runs.Count > 0 ? sum / runs.Count : 0f;
        }

        private IEnumerator StandingHold(
            float loadKg, bool standingBias, int reset, StandingResult[] result, StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thoraxBody = _rig.Segments["thorax"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.SpineBiasRateFeedforwardEnabled = false;
            ApplyStanding(adapter, standingBias);
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

            float abdomenStart = 0f;
            float thoraxStart = 0f;
            var accumulator = new StandingResult();
            int samples = 0;

            for (int tick = 0; tick < 1000; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                float abdomenNow = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
                float thoraxNow = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);
                if (tick == 900)
                {
                    abdomenStart = abdomenNow;
                    thoraxStart = thoraxNow;
                }
                if (tick < 900)
                    continue;

                adapter.TryGetTargetComposition("abdomen", out var abdomen);
                adapter.TryGetTargetComposition("thorax", out var thorax);
                adapter.TryGetTargetComposition("left_thigh", out var hip);
                Vector3 axis = thoraxBody.position - pelvis.position;

                accumulator.AbdomenError += abdomenNow - TwistX(abdomen.Nominal);
                accumulator.ThoraxError += thoraxNow - TwistX(thorax.Nominal);
                accumulator.TrunkError += Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg - ReferenceWorldTrunkPitchDeg[0];
                accumulator.HipError +=
                    TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative) - TwistX(hip.Nominal);
                accumulator.ComAp += balance.SystemCom.z;
                accumulator.CopAp += balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter;
                accumulator.SupportLength += balance.SupportApLength;
                accumulator.Contacts = balance.SupportContactCount;
                accumulator.AbdomenDrift = abdomenNow - abdomenStart;
                accumulator.ThoraxDrift = thoraxNow - thoraxStart;
                samples++;
            }

            float inverse = samples > 0 ? 1f / samples : 0f;
            accumulator.AbdomenError *= inverse;
            accumulator.ThoraxError *= inverse;
            accumulator.TrunkError *= inverse;
            accumulator.HipError *= inverse;
            accumulator.ComAp *= inverse;
            accumulator.CopAp *= inverse;
            accumulator.SupportLength *= inverse;

            SquatBarSaddle saddle = _controller.Saddle;
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0:F0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F5},{10:F5},{11:F5},{12}," +
                "{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18:F5},{19}",
                loadKg, standingBias ? 1 : 0, reset,
                accumulator.AbdomenError, accumulator.ThoraxError,
                accumulator.TrunkError + ReferenceWorldTrunkPitchDeg[0], ReferenceWorldTrunkPitchDeg[0],
                accumulator.TrunkError, accumulator.HipError,
                accumulator.ComAp, accumulator.CopAp, accumulator.SupportLength, accumulator.Contacts,
                accumulator.AbdomenDrift, accumulator.ThoraxDrift,
                powered.GetJoint("abdomen").Diagnostic.ModeledDemand,
                powered.GetJoint("thorax").Diagnostic.ModeledDemand,
                adapter.BalanceCorrectionRad * Mathf.Rad2Deg,
                saddle != null ? saddle.SaddleSeparationMeters : 0f,
                saddle != null && saddle.IsAttached ? 1 : 0));

            result[0] = accumulator;
            yield return null;
        }

        // ------------------------------------------------------------------
        // B2. Factorial discrimination, sections 7 and 9.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator B2_FACTORIAL_STANDING_AND_RATE()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H16 B2 FACTORIAL: STANDING BIAS x GRAVITY-BIAS RATE");
            report.AppendLine("Fresh reset per run, identical driven descent, production balance on.");
            report.AppendLine("  F00 H15 as shipped   F10 standing bias   F01 rate   F11 both");
            report.AppendLine("trunkErr is world pitch minus the GAM-10 reference pitch at that phase.");
            report.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,case,sq,abd_err,tho_err,world_trunk,ref_trunk,trunk_err,hip_err," +
                           "support_length,contacts,abd_target_rate,tho_target_rate,abd_actual_rate," +
                           "abd_demand,com_ap,cop_ap,saddle_sep");

            foreach (float load in new[] { 0f, 25f })
            {
                report.AppendLine("--- load " + load.ToString("F0", CultureInfo.InvariantCulture) + " kg ---");
                report.AppendLine("  case  s_q    abdErr  thoErr  trunkErr |  hipErr  support  contacts");
                foreach (string caseTag in new[] { "F00", "F10", "F01", "F11" })
                {
                    bool standing = caseTag == "F10" || caseTag == "F11";
                    bool rate = caseTag == "F01" || caseTag == "F11";
                    yield return FactorialDescent(load, caseTag, standing, rate, report, csv);
                }
                report.AppendLine();
            }

            WriteMeasurement("spine-factorial.txt", report.ToString());
            WriteMeasurement("spine-factorial.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator FactorialDescent(
            float loadKg, string caseTag, bool standingBias, bool rateFeedforward,
            StringBuilder report, StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thoraxBody = _rig.Segments["thorax"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            ApplyStanding(adapter, standingBias);
            adapter.SpineBiasRateFeedforwardEnabled = rateFeedforward;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            var stops = new List<float> { 0.00f, 0.10f, 0.25f, 0.55f, 0.80f, 1.00f };
            int nextStop = 0;
            float sq = 0f;
            int tick = 0;

            while (tick < 420)
            {
                if (nextStop < stops.Count && sq >= stops[nextStop] - 1e-4f)
                {
                    adapter.TryGetTargetComposition("abdomen", out var abdomenC);
                    adapter.TryGetTargetComposition("thorax", out var thoraxC);
                    adapter.TryGetTargetComposition("left_thigh", out var hipC);
                    float abdomenActual = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
                    float thoraxActual = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);
                    float hipActual = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative);
                    Vector3 axis = thoraxBody.position - pelvis.position;
                    float worldTrunk = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
                    float reference = SampleReference(sq);
                    SquatBarSaddle saddle = _controller.Saddle;

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-4}  {1,4:F2}  {2,7:F2}  {3,6:F2}  {4,8:F2} |  {5,6:F2}  {6,7:F4}  {7,8}",
                        caseTag, sq,
                        abdomenActual - TwistX(abdomenC.Nominal),
                        thoraxActual - TwistX(thoraxC.Nominal),
                        worldTrunk - reference,
                        hipActual - TwistX(hipC.Nominal),
                        balance.SupportApLength, balance.SupportContactCount));

                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F5},{10}," +
                        "{11:F5},{12:F5},{13:F5},{14:F4},{15:F5},{16:F5},{17:F5}",
                        loadKg, caseTag, sq,
                        abdomenActual - TwistX(abdomenC.Nominal),
                        thoraxActual - TwistX(thoraxC.Nominal),
                        worldTrunk, reference, worldTrunk - reference,
                        hipActual - TwistX(hipC.Nominal),
                        balance.SupportApLength, balance.SupportContactCount,
                        powered.GetJoint("abdomen").Diagnostic.TargetAngularVelocityRadS.x,
                        powered.GetJoint("thorax").Diagnostic.TargetAngularVelocityRadS.x,
                        powered.GetJoint("abdomen").Diagnostic.ActualAngularVelocityRadS.x,
                        powered.GetJoint("abdomen").Diagnostic.ModeledDemand,
                        balance.SystemCom.z,
                        balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter,
                        saddle != null ? saddle.SaddleSeparationMeters : 0f));
                    nextStop++;
                    continue;
                }

                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;
            }
            yield return null;
        }

        // ------------------------------------------------------------------
        // B3. Visual gate, section 16.
        // ------------------------------------------------------------------

        /// <summary>
        /// Captures the shipped configuration across the whole descent, not
        /// just the bottom, because the question this phase answers is whether
        /// the spine follows the accepted shape continuously from standing
        /// through depth. Each frame is logged with the trunk error measured at
        /// that instant so the image and the number cannot drift apart.
        /// </summary>
        [UnityTest]
        public IEnumerator B3_POST_H16_VISUAL_GATE()
        {
            var log = new StringBuilder();
            log.AppendLine("GAM-11 PHASE 5H16 B3 VISUAL GATE, SHIPPED CONFIGURATION");
            log.AppendLine("Standing spine knot qualified and gravity-bias rate feedforward on.");
            log.AppendLine();
            log.AppendLine("  tag                      s_q   abdErr  thoErr  trunk   refTrunk  trunkErr  support");

            foreach (float load in new[] { 0f, 25f })
                yield return CaptureDescent(load, log);

            WriteMeasurement("post-h16-visual-gate.txt", log.ToString());
            Debug.Log(log.ToString());
            yield return null;
        }

        private IEnumerator CaptureDescent(float loadKg, StringBuilder log)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thoraxBody = _rig.Segments["thorax"].Body;
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
            var stops = new List<float> { 0.00f, 0.10f, 0.25f, 0.55f, 0.80f, 1.00f };
            int nextStop = 0;
            float sq = 0f;
            int tick = 0;

            while (tick < 420 && nextStop < stops.Count)
            {
                if (sq >= stops[nextStop] - 1e-4f)
                {
                    yield return null;
                    string tag = string.Format(CultureInfo.InvariantCulture,
                        "h16_{0:F0}kg_sq{1:F2}", loadKg, stops[nextStop]);
                    Capture(camera, tag + "_side.png", new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                    Capture(camera, tag + "_oblique.png", new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));

                    adapter.TryGetTargetComposition("abdomen", out var abdomen);
                    adapter.TryGetTargetComposition("thorax", out var thorax);
                    Vector3 axis = thoraxBody.position - pelvis.position;
                    float worldTrunk = Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
                    float reference = SampleReference(sq);
                    log.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,-22}  {1,4:F2}  {2,6:F2}  {3,6:F2}  {4,6:F2}  {5,8:F2}  {6,8:F2}  {7,7:F4}",
                        tag, sq,
                        TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative) - TwistX(abdomen.Nominal),
                        TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative) - TwistX(thorax.Nominal),
                        worldTrunk, reference, worldTrunk - reference, balance.SupportApLength));
                    nextStop++;
                    continue;
                }

                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;
            }
            log.AppendLine();
            yield return null;
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

            string directory = Path.Combine(
                Directory.GetCurrentDirectory(), "Artifacts/Evidence/GAM-11/h16-spine");
            Directory.CreateDirectory(directory);
            File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(texture);
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
