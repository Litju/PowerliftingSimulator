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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H12, sections 4 and 5. H11 removed the rearward collapse and the
    /// squat now fails forward, late. Before anyone touches ankle range,
    /// balance gains or the reference, this asks the question those repairs all
    /// presuppose an answer to: can the plant hold the commanded pose at all,
    /// standing still, with the dynamics taken out?
    ///
    /// Three composition cases at each phase, each from a fresh reset:
    ///
    ///   S0  nominal GAM-10 reference only
    ///   S1  nominal + the gravity equilibrium preload
    ///   S2  full production composition, balance included
    ///
    /// If S0 or S1 cannot hold the bottom, the forward divergence is not a
    /// control problem and tuning the balance law would be aimed at a symptom.
    /// </summary>
    public sealed class DeepSquatStaticFeasibilityTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

        // Reach the pose at the production phase rate, then hold it still.
        private const float ApproachPhaseRate = 0.30f;
        private const int HoldTicks = 250;
        private const int VerdictWindowTicks = 60;

        private static readonly float[] Phases = { 0.00f, 0.55f, 0.80f, 1.00f };

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

        private enum CompositionCase
        {
            S0_NominalOnly,
            S1_NominalPlusGravityBias,
            S2_FullProduction
        }

        [UnityTest]
        public IEnumerator F1_STATIC_DEEP_PHASE_FEASIBILITY_SWEEP()
        {
            var results = new List<Sample>();
            foreach (float load in new[] { 0f, 25f })
            {
                foreach (CompositionCase composition in new[]
                         {
                             CompositionCase.S0_NominalOnly,
                             CompositionCase.S1_NominalPlusGravityBias,
                             CompositionCase.S2_FullProduction
                         })
                {
                    foreach (float phase in Phases)
                    {
                        yield return HoldAndMeasure(load, composition, phase, results);
                    }
                }
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H12 F1 STATIC DEEP-PHASE FEASIBILITY");
            report.AppendLine("Fresh reset per sample. Phase ramped at the production rate then held with");
            report.AppendLine("zero phase velocity. Verdict taken over the last " +
                              VerdictWindowTicks.ToString(CultureInfo.InvariantCulture) + " ticks of a " +
                              HoldTicks.ToString(CultureInfo.InvariantCulture) + " tick hold.");
            report.AppendLine();
            report.AppendLine("load  case  s_q   held  comAP   supp[min,max]      |vCOM|  pelvisDrift  contacts  divTick  reason");
            foreach (Sample s in results)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F0}  {1,-2}  {2,4:F2}  {3,-5}  {4,6:F4}  [{5,7:F4},{6,7:F4}]  {7,6:F4}  {8,11:F4}  {9,8}  {10,6}  {11}",
                    s.LoadKg, s.CaseTag, s.Phase, s.Held, s.ComAp, s.SupportMin, s.SupportMax,
                    s.ComSpeed, s.PelvisDrift, s.Contacts, s.DivergenceTick, s.Reason));
            }

            report.AppendLine();
            report.AppendLine("--- ankle anatomical decomposition at each held sample ---");
            report.AppendLine("load case s_q | nominal  bias  balance   final | actual  dorsiflex | limits(dorsi,plantar) prox | trackErr");
            foreach (Sample s in results)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F0} {1,-2} {2,4:F2} | {3,7:F2} {4,5:F2} {5,7:F2} {6,7:F2} | {7,6:F2} {8,10:F2} | ({9:F0},{10:F0}) {11,6:F3} | {12,6:F2}",
                    s.LoadKg, s.CaseTag, s.Phase,
                    s.AnkleNominalDeg, s.AnkleBiasDeg, s.AnkleBalanceDeg, s.AnkleFinalDeg,
                    s.AnkleActualLogicalDeg, s.AnkleDorsiflexionDeg,
                    s.AnkleDorsiLimitDeg, s.AnklePlantarLimitDeg, s.AnkleProximity, s.AnkleTrackingErrorDeg));
            }

            report.AppendLine();
            report.AppendLine(Verdicts(results));

            WriteMeasurement("GAM11-5h12-f1-static-feasibility.txt", report.ToString());
            WriteMeasurement("GAM11-5h12-f1-static-feasibility.csv", Csv(results));
            Debug.Log(report.ToString());

            Assert.That(results.Count, Is.EqualTo(2 * 3 * Phases.Length),
                "The sweep did not complete every sample.");
            yield return null;
        }

        /// <summary>
        /// F1 shows the held pose is stable at s_q 0.55 and divergent at 0.80,
        /// with the ankle nowhere near its limit when the divergence starts.
        /// Something is withdrawing the controller's authority as the squat
        /// deepens. This records the balance law's own internals against the
        /// per-joint limit proximities that feed them, so the mechanism is
        /// measured rather than read out of the source.
        /// </summary>
        [UnityTest]
        public IEnumerator F2_BALANCE_AUTHORITY_VERSUS_DEPTH()
        {
            var csv = new StringBuilder();
            csv.AppendLine("phase,hold_tick,com_ap,com_ref_ap,com_ap_error,com_speed," +
                           "desired_com_accel,cop_desired_unclamped,cop_desired,cop_measured,cop_error," +
                           "raw_ankle_offset_deg,final_ankle_offset_deg,posture_guard_scale," +
                           "posture_error_rad,posture_limit_proximity,limit_driver_joint,limit_driver_proximity," +
                           "ankle_authority_fraction,ankle_saturated,hip_blend,hip_offset_deg,trunk_offset_deg," +
                           "knee_prox,hip_prox,abdomen_prox,thorax_prox,ankle_prox," +
                           "ankle_dorsiflexion_deg,knee_flexion_deg,chain_max_demand,support_min,support_max,contacts");

            foreach (float phase in new[] { 0.55f, 0.70f, 0.80f, 0.90f, 1.00f })
                yield return TraceBalanceAtPhase(phase, csv);

            WriteMeasurement("GAM11-5h12-f2-balance-authority-vs-depth.csv", csv.ToString());
            Debug.Log("[F2] balance authority trace written");
            yield return null;
        }

        private IEnumerator TraceBalanceAtPhase(float phase, StringBuilder csv)
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;

            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
            float current = 0f;
            int guard = 0;
            while (current < phase - 1e-4f && guard++ < 4000)
            {
                current = Mathf.Min(phase, current + ApproachPhaseRate * dt);
                adapter.HoldReferencePhaseForQualification(current, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }
            adapter.HoldReferencePhaseForQualification(phase, SquatPhaseDirection.Descent, state, 0f);

            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;

            for (int tick = 0; tick < HoldTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                if (tick % 5 != 0 && tick != HoldTicks - 1)
                    continue;

                float kneeProx = Prox(powered, "left_shank");
                float hipProx = Prox(powered, "left_thigh");
                float abdProx = Prox(powered, "abdomen");
                float thoProx = Prox(powered, "thorax");
                float ankProx = Prox(powered, "left_foot");

                // Which joint is actually setting the guard. The ankles are
                // deliberately not in the canonical posture set, so naming the
                // driver matters.
                string driver = "left_shank";
                float driverProx = kneeProx;
                foreach (var pair in new[]
                         {
                             ("left_thigh", hipProx), ("abdomen", abdProx), ("thorax", thoProx)
                         })
                {
                    if (pair.Item2 > driverProx) { driverProx = pair.Item2; driver = pair.Item1; }
                }

                float chainDemand = 0f;
                foreach (string id in new[] { "left_foot", "left_shank", "left_thigh", "abdomen", "thorax" })
                    chainDemand = Mathf.Max(chainDemand, powered.GetJoint(id).Diagnostic.ModeledDemand);

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4}," +
                    "{11:F3},{12:F3},{13:F4},{14:F4},{15:F4},{16},{17:F4},{18:F4},{19},{20:F3},{21:F3},{22:F3}," +
                    "{23:F3},{24:F3},{25:F3},{26:F3},{27:F3},{28:F2},{29:F2},{30:F3},{31:F4},{32:F4},{33}",
                    phase, tick,
                    balance.SystemCom.z, bc.ComRefAp, bc.ComApError,
                    new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude,
                    bc.DesiredComAccelerationAp, bc.CopDesiredApUnclamped, bc.CopDesiredAp,
                    bc.CopMeasuredAp, bc.CopErrorAp,
                    bc.RawAnkleSagittalOffsetRad * Mathf.Rad2Deg,
                    bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                    bc.PostureGuardScale, bc.PostureErrorRad, bc.PostureLimitProximity,
                    driver, driverProx,
                    bc.AnkleAuthorityFraction, bc.IsAnkleOffsetSaturated ? 1 : 0,
                    bc.HipStrategyBlend, bc.HipSagittalOffsetRad * Mathf.Rad2Deg,
                    bc.TrunkSagittalOffsetRad * Mathf.Rad2Deg,
                    kneeProx, hipProx, abdProx, thoProx, ankProx,
                    -TwistX(powered.GetJoint("left_foot").Diagnostic.ActualRelative),
                    TwistX(powered.GetJoint("left_shank").Diagnostic.ActualRelative),
                    chainDemand, balance.SupportApMin, balance.SupportApMax, balance.SupportContactCount));
            }
            yield return null;
        }

        private static float Prox(PoweredJointController powered, string id) =>
            powered.GetJoint(id).Diagnostic.LimitProximity;

        /// <summary>
        /// The counterfactual. F2 shows the posture limit guard driving the
        /// ankle balance offset to exactly zero from s_q 0.85, with the hip's
        /// reference-commanded flexion as the thing that trips it. If that is
        /// causal and not a passenger, disabling only the guard and changing no
        /// gain, bound, limit or reference has to move the forward divergence.
        ///
        /// This is a diagnostic toggle on an existing public property. Nothing
        /// is tuned and nothing is committed to production.
        /// </summary>
        [UnityTest]
        public IEnumerator F3_POSTURE_GUARD_COUNTERFACTUAL()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H12 F3 POSTURE LIMIT GUARD COUNTERFACTUAL");
            report.AppendLine("Identical dynamic descent. The only difference is PostureGuardEnabled.");
            report.AppendLine();
            report.AppendLine("load  guard  deepestSq  finalPelvisY  minGuardScale  firstFrontCross  firstComOut  maxAnkleProx  lostContact");

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,guard_enabled,tick,sq,com_ap,com_speed,support_min,support_max," +
                           "guard_scale,limit_prox,raw_ankle_deg,final_ankle_deg,ankle_prox,hip_prox," +
                           "pelvis_y,trunk_incl_deg,contacts");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (bool guardOn in new[] { true, false })
                    yield return RunGuardCase(load, guardOn, report, csv);
            }

            WriteMeasurement("GAM11-5h12-f3-posture-guard-counterfactual.txt", report.ToString());
            WriteMeasurement("GAM11-5h12-f3-posture-guard-counterfactual.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator RunGuardCase(float loadKg, bool guardEnabled, StringBuilder report, StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody thorax = _rig.Segments["thorax"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            bc.PostureGuardEnabled = guardEnabled;

            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            adapter.StartSquat();

            float minGuard = 1f;
            float maxAnkleProx = 0f;
            float deepestSq = 0f;
            int firstFrontCross = -1;
            int firstComOut = -1;
            bool lostContact = false;

            for (int tick = 0; tick < 400; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                minGuard = Mathf.Min(minGuard, bc.PostureGuardScale);
                maxAnkleProx = Mathf.Max(maxAnkleProx, Prox(powered, "left_foot"));
                deepestSq = Mathf.Max(deepestSq, adapter.Sq);
                if (!balance.HasSupport)
                    lostContact = true;
                if (firstFrontCross < 0 && balance.HasSupport && balance.CaptureAp > balance.SupportApMax)
                    firstFrontCross = tick;
                if (firstComOut < 0 && balance.HasSupport && balance.SystemCom.z > balance.SupportApMax)
                    firstComOut = tick;

                if (tick % 10 == 0)
                {
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F3},{11:F3},{12:F3},{13:F3},{14:F4},{15:F2},{16}",
                        loadKg, guardEnabled ? 1 : 0, tick, adapter.Sq, balance.SystemCom.z,
                        new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude,
                        balance.SupportApMin, balance.SupportApMax,
                        bc.PostureGuardScale, bc.PostureLimitProximity,
                        bc.RawAnkleSagittalOffsetRad * Mathf.Rad2Deg,
                        bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                        Prox(powered, "left_foot"), Prox(powered, "left_thigh"),
                        pelvis.position.y, TrunkInclination(pelvis, thorax), balance.SupportContactCount));
                }
            }

            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,4:F0}  {1,-5}  {2,9:F2}  {3,12:F4}  {4,13:F4}  {5,15}  {6,11}  {7,12:F3}  {8}",
                loadKg, guardEnabled ? "ON" : "OFF", deepestSq, pelvis.position.y, minGuard,
                firstFrontCross < 0 ? "never" : firstFrontCross.ToString(CultureInfo.InvariantCulture),
                firstComOut < 0 ? "never" : firstComOut.ToString(CultureInfo.InvariantCulture),
                maxAnkleProx, lostContact));

            bc.PostureGuardEnabled = true;
            yield return null;
        }

        /// <summary>
        /// Section 13. The H11 receipt asserted that load makes the forward
        /// divergence earlier "as a forward-tipping bar moment should", which
        /// was inference, not measurement. This measures the lever arm and the
        /// gravitational pitching moment the bar actually applies about the
        /// ankle line, with the project's signs, and reports the direction
        /// rather than assuming it.
        /// </summary>
        [UnityTest]
        public IEnumerator F4_BAR_GRAVITATIONAL_PITCH_MOMENT()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H12 F4 BAR GRAVITATIONAL PITCH MOMENT AT 25 KG");
            report.AppendLine("+Z is anterior. A bar centre of mass anterior to the ankle line gives a");
            report.AppendLine("positive, forward-toppling moment about that line.");
            report.AppendLine();
            report.AppendLine("s_q   barAP     ankleAP   leverArm   supportCtr  copAP     tau_Nm   direction");

            foreach (float phase in new[] { 0.00f, 0.55f, 0.80f, 1.00f })
            {
                _controller.SetLoad(25f);
                SquatPhysicalAdapter adapter = _controller.Adapter;
                FoundationRuntime runtime = _bootstrap.Runtime;
                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                adapter.Preload.Enabled = true;
                adapter.BalanceCorrectionsEnabled = true;

                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                for (int i = 0; i < 60; i++)
                {
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }
                SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                float current = 0f;
                int guard = 0;
                while (current < phase - 1e-4f && guard++ < 4000)
                {
                    current = Mathf.Min(phase, current + ApproachPhaseRate * dt);
                    adapter.HoldReferencePhaseForQualification(current, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }
                adapter.HoldReferencePhaseForQualification(phase, SquatPhaseDirection.Descent, state, 0f);
                for (int i = 0; i < 40; i++)
                {
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    TickFeet(dt);
                }

                Rigidbody bar = _controller.Saddle != null && _controller.Saddle.Barbell != null
                    ? _controller.Saddle.Barbell.Body
                    : null;
                if (bar == null)
                    continue;

                SquatBalanceObserver balance = adapter.Balance;
                Vector3 barCom = bar.worldCenterOfMass;
                float ankleAp = 0.5f * (JointAnchorWorld("left_foot").z + JointAnchorWorld("right_foot").z);
                float lever = barCom.z - ankleAp;
                float mass = bar.mass;
                float tau = mass * 9.81f * lever;

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F2}  {1,8:F4}  {2,8:F4}  {3,8:F4}  {4,10:F4}  {5,8:F4}  {6,7:F1}  {7}",
                    phase, barCom.z, ankleAp, lever, balance.SupportApCenter,
                    balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                    tau, lever > 0.005f ? "FORWARD" : lever < -0.005f ? "REARWARD" : "NEUTRAL"));
                yield return null;
            }

            WriteMeasurement("GAM11-5h12-f4-bar-pitch-moment.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        /// <summary>
        /// Section 16. Renders the guard on and guard off descents at the same
        /// ticks so the mechanism is visible rather than only tabulated: the
        /// last state that is still tracking, the onset of the forward
        /// divergence, the front-support crossing, and the bottom.
        /// </summary>
        [UnityTest]
        public IEnumerator F5_FORWARD_DIVERGENCE_VISUAL_EVIDENCE()
        {
            foreach (bool guardOn in new[] { true, false })
                yield return CaptureDescent(guardOn);
            yield return null;
        }

        private IEnumerator CaptureDescent(bool guardEnabled)
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.BalanceController.PostureGuardEnabled = guardEnabled;

            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            adapter.StartSquat();
            int[] captureTicks = { 250, 300, 366, 396 };
            int next = 0;
            string tag = guardEnabled ? "guardON" : "guardOFF";

            for (int tick = 0; tick < 400; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                if (next < captureTicks.Length && tick == captureTicks[next])
                {
                    yield return null;
                    Capture(camera, string.Format(CultureInfo.InvariantCulture,
                        "{0}_t{1:D3}_sq{2:F2}_side.png", tag, tick, adapter.Sq),
                        new Vector3(2.6f, 1.0f, 0f), new Vector3(0f, 0.85f, 0f));
                    next++;
                }
            }
            adapter.BalanceController.PostureGuardEnabled = true;
            yield return null;
        }

        private static void Capture(Camera camera, string filename, Vector3 eye, Vector3 focus)
        {
            if (camera == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                return;
            Vector3 savedPos = camera.transform.position;
            Quaternion savedRot = camera.transform.rotation;
            camera.transform.position = eye;
            camera.transform.rotation = Quaternion.LookRotation((focus - eye).normalized);
            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                string dir = Path.GetFullPath("Artifacts/Evidence/GAM-11/h12-forward-divergence");
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, filename), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                camera.transform.position = savedPos;
                camera.transform.rotation = savedRot;
            }
        }

        private Vector3 JointAnchorWorld(string childId)
        {
            foreach (PhysicalAthleteRig.JointRuntime joint in _rig.Joints)
            {
                if (string.Equals(joint.Recipe.ChildId, childId, StringComparison.Ordinal))
                    return joint.Joint.transform.TransformPoint(joint.Joint.anchor);
            }
            throw new InvalidOperationException("No joint for child " + childId);
        }

        private IEnumerator HoldAndMeasure(
            float loadKg,
            CompositionCase composition,
            float phase,
            List<Sample> results)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = composition != CompositionCase.S0_NominalOnly;
            adapter.BalanceCorrectionsEnabled = composition == CompositionCase.S2_FullProduction;

            SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
            SquatPhaseDirection direction = phase <= 0.0001f
                ? SquatPhaseDirection.None
                : SquatPhaseDirection.Descent;

            // Settle standing before the phase starts moving, exactly as the
            // production path does.
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            // Ramp to the sample phase at the production rate, reporting the
            // true rate so the drives get the same feed-forward they would in
            // gameplay, then stop the clock.
            float current = 0f;
            int guard = 0;
            while (current < phase - 1e-4f && guard++ < 4000)
            {
                current = Mathf.Min(phase, current + ApproachPhaseRate * dt);
                adapter.HoldReferencePhaseForQualification(current, direction, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }
            adapter.HoldReferencePhaseForQualification(phase, direction, state, 0f);

            SquatBalanceObserver balance = adapter.Balance;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            PoweredJointController powered = _rig.PoweredController;

            float pelvisAtHoldStart = pelvis.position.y;
            float worstComSpeed = 0f;
            float pelvisDrift = 0f;
            bool lostSupport = false;
            bool comLeftSupport = false;
            string firstReason = "HELD";

            // The verdict alone cannot tell an arrival transient from a pose
            // that genuinely will not hold, so the whole hold window is traced
            // and the tick divergence begins is recorded separately.
            string tag = string.Format(CultureInfo.InvariantCulture, "{0:F0}kg-{1}-sq{2:F2}",
                loadKg, composition == CompositionCase.S0_NominalOnly ? "S0"
                    : composition == CompositionCase.S1_NominalPlusGravityBias ? "S1" : "S2", phase);
            var hold = new StringBuilder();
            hold.AppendLine("hold_tick,com_ap,com_speed,support_min,support_max,cop_ap,contacts," +
                            "pelvis_y,ankle_dorsiflexion_deg,ankle_proximity,trunk_incl_deg");
            int divergenceTick = -1;
            float comApAtHoldStart = balance.SystemCom.z;

            for (int tick = 0; tick < HoldTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                float speedNow = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude;
                if (divergenceTick < 0 &&
                    (Mathf.Abs(balance.SystemCom.z - comApAtHoldStart) > 0.02f || speedNow > 0.10f))
                {
                    divergenceTick = tick;
                }

                if (tick % 5 == 0 || tick == HoldTicks - 1)
                {
                    PoweredJointDiagnostic ankleNow = powered.GetJoint("left_foot").Diagnostic;
                    hold.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6},{7:F4},{8:F2},{9:F3},{10:F2}",
                        tick, balance.SystemCom.z, speedNow, balance.SupportApMin, balance.SupportApMax,
                        balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                        balance.SupportContactCount, pelvis.position.y,
                        -TwistX(ankleNow.ActualRelative), ankleNow.LimitProximity,
                        TrunkInclination(pelvis, _rig.Segments["thorax"].Body)));
                }

                if (tick < HoldTicks - VerdictWindowTicks)
                    continue;

                float speed = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude;
                worstComSpeed = Mathf.Max(worstComSpeed, speed);
                pelvisDrift = Mathf.Max(pelvisDrift, Mathf.Abs(pelvis.position.y - pelvisAtHoldStart));
                if (!balance.HasSupport)
                {
                    lostSupport = true;
                    if (firstReason == "HELD") firstReason = "LOST_PLANTAR_CONTACT";
                }
                else if (balance.SystemCom.z < balance.SupportApMin || balance.SystemCom.z > balance.SupportApMax)
                {
                    comLeftSupport = true;
                    if (firstReason == "HELD")
                        firstReason = balance.SystemCom.z > balance.SupportApMax ? "COM_PAST_FRONT" : "COM_PAST_REAR";
                }
            }

            bool held = !lostSupport && !comLeftSupport && worstComSpeed < 0.05f && pelvisDrift < 0.02f;
            if (held)
                firstReason = "HELD";
            else if (firstReason == "HELD")
                firstReason = worstComSpeed >= 0.05f ? "COM_STILL_MOVING" : "PELVIS_DRIFTING";

            WriteMeasurement("GAM11-5h12-f1-hold-" + tag + ".csv", hold.ToString());

            Sample sample = BuildSample(adapter, balance, pelvis, loadKg, composition, phase,
                held, firstReason, worstComSpeed, pelvisDrift);
            sample.DivergenceTick = divergenceTick;
            results.Add(sample);
            yield return null;
        }

        private Sample BuildSample(
            SquatPhysicalAdapter adapter,
            SquatBalanceObserver balance,
            Rigidbody pelvis,
            float loadKg,
            CompositionCase composition,
            float phase,
            bool held,
            string reason,
            float comSpeed,
            float pelvisDrift)
        {
            PoweredJointController powered = _rig.PoweredController;
            PoweredJointController.PoweredJointRuntime ankle = powered.GetJoint("left_foot");
            PhysicalJointRecipe recipe = ankle.Recipe;

            adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition c);

            float actualLogical = TwistX(ankle.Diagnostic.ActualRelative);
            // The powered controller reports limit proximity as
            // |x| / (x >= 0 ? HighDegrees : -LowDegrees) using the recipe, so
            // the negative logical direction is the recipe's LowDegrees side.
            // That is the side the ankle travels during a descent, which makes
            // it dorsiflexion; the magnitude of LowDegrees is therefore the
            // anatomical dorsiflexion limit.
            float dorsiflexion = -actualLogical;
            float dorsiLimit = Mathf.Abs(recipe.LowDegrees);
            float plantarLimit = Mathf.Abs(recipe.HighDegrees);

            var sample = new Sample
            {
                LoadKg = loadKg,
                CaseTag = composition == CompositionCase.S0_NominalOnly ? "S0"
                    : composition == CompositionCase.S1_NominalPlusGravityBias ? "S1" : "S2",
                Phase = phase,
                Held = held,
                Reason = reason,
                ComAp = balance.SystemCom.z,
                ComMl = balance.SystemCom.x,
                ComHeight = balance.SystemCom.y,
                ComSpeed = comSpeed,
                SupportMin = balance.SupportApMin,
                SupportMax = balance.SupportApMax,
                CopAp = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                CaptureAp = balance.CaptureAp,
                Contacts = balance.SupportContactCount,
                PelvisY = pelvis.position.y,
                PelvisDrift = pelvisDrift,
                AnkleNominalDeg = TwistX(c.Nominal),
                AnkleBiasDeg = TwistX(c.GravityBias),
                AnkleBalanceDeg = TwistX(c.BalanceOffset),
                AnkleFinalDeg = TwistX(c.Final),
                AnkleActualLogicalDeg = actualLogical,
                AnkleDorsiflexionDeg = dorsiflexion,
                AnkleDorsiLimitDeg = dorsiLimit,
                AnklePlantarLimitDeg = plantarLimit,
                AnkleProximity = ankle.Diagnostic.LimitProximity,
                AnkleTrackingErrorDeg = Quaternion.Angle(ankle.AppliedTarget, ankle.Diagnostic.ActualRelative),
                KneeActualDeg = TwistX(powered.GetJoint("left_shank").Diagnostic.ActualRelative),
                HipActualDeg = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative),
                TrunkInclinationDeg = TrunkInclination(pelvis, _rig.Segments["thorax"].Body),
                SaddleSeparationM = _controller.Saddle != null ? _controller.Saddle.SaddleSeparationMeters : 0f
            };
            return sample;
        }

        private static string Verdicts(List<Sample> results)
        {
            var text = new StringBuilder();
            foreach (float load in new[] { 0f, 25f })
            {
                foreach (string tag in new[] { "S0", "S1", "S2" })
                {
                    Sample bottom = results.Find(s =>
                        Mathf.Approximately(s.LoadKg, load) && s.CaseTag == tag && s.Phase >= 0.999f);
                    string key = string.Format(CultureInfo.InvariantCulture,
                        "STATIC_{0}KG_{1}", load, tag);
                    text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,-16} = {1}   (bottom: {2}, comAP {3:F4} vs support [{4:F4},{5:F4}], ankle dorsiflexion {6:F1} of {7:F0} deg)",
                        key, bottom != null && bottom.Held ? "FEASIBLE" : "NOT_FEASIBLE",
                        bottom == null ? "missing" : bottom.Reason,
                        bottom?.ComAp ?? 0f, bottom?.SupportMin ?? 0f, bottom?.SupportMax ?? 0f,
                        bottom?.AnkleDorsiflexionDeg ?? 0f, bottom?.AnkleDorsiLimitDeg ?? 0f));
                }
            }
            return text.ToString();
        }

        private static string Csv(List<Sample> results)
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,case,phase,held,reason,com_ap,com_ml,com_height,com_speed," +
                           "support_min,support_max,cop_ap,capture_ap,contacts,pelvis_y,pelvis_drift," +
                           "ankle_nominal_deg,ankle_bias_deg,ankle_balance_deg,ankle_final_deg," +
                           "ankle_actual_logical_deg,ankle_dorsiflexion_deg,ankle_dorsi_limit_deg," +
                           "ankle_plantar_limit_deg,ankle_proximity,ankle_tracking_err_deg," +
                           "knee_actual_deg,hip_actual_deg,trunk_inclination_deg,saddle_sep_m,divergence_tick");
            foreach (Sample s in results)
            {
                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1},{2:F2},{3},{4},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12:F5},{13},{14:F5},{15:F5}," +
                    "{16:F3},{17:F3},{18:F3},{19:F3},{20:F3},{21:F3},{22:F1},{23:F1},{24:F4},{25:F3},{26:F3},{27:F3},{28:F3},{29:F5}",
                    s.LoadKg, s.CaseTag, s.Phase, s.Held ? 1 : 0, s.Reason,
                    s.ComAp, s.ComMl, s.ComHeight, s.ComSpeed, s.SupportMin, s.SupportMax, s.CopAp, s.CaptureAp,
                    s.Contacts, s.PelvisY, s.PelvisDrift,
                    s.AnkleNominalDeg, s.AnkleBiasDeg, s.AnkleBalanceDeg, s.AnkleFinalDeg,
                    s.AnkleActualLogicalDeg, s.AnkleDorsiflexionDeg, s.AnkleDorsiLimitDeg, s.AnklePlantarLimitDeg,
                    s.AnkleProximity, s.AnkleTrackingErrorDeg,
                    s.KneeActualDeg, s.HipActualDeg, s.TrunkInclinationDeg, s.SaddleSeparationM, s.DivergenceTick));
            }
            return csv.ToString();
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static float TrunkInclination(Rigidbody pelvis, Rigidbody thorax)
        {
            Vector3 axis = thorax.position - pelvis.position;
            return axis.sqrMagnitude < 1e-8f ? 0f : Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
        }

        private static float TwistX(Quaternion rotation)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            var twist = new Quaternion(projection.x, projection.y, projection.z, rotation.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }

        private sealed class Sample
        {
            public float LoadKg;
            public string CaseTag;
            public float Phase;
            public bool Held;
            public string Reason;
            public float ComAp;
            public float ComMl;
            public float ComHeight;
            public float ComSpeed;
            public float SupportMin;
            public float SupportMax;
            public float CopAp;
            public float CaptureAp;
            public int Contacts;
            public float PelvisY;
            public float PelvisDrift;
            public float AnkleNominalDeg;
            public float AnkleBiasDeg;
            public float AnkleBalanceDeg;
            public float AnkleFinalDeg;
            public float AnkleActualLogicalDeg;
            public float AnkleDorsiflexionDeg;
            public float AnkleDorsiLimitDeg;
            public float AnklePlantarLimitDeg;
            public float AnkleProximity;
            public float AnkleTrackingErrorDeg;
            public float KneeActualDeg;
            public float HipActualDeg;
            public float TrunkInclinationDeg;
            public float SaddleSeparationM;
            public int DivergenceTick = -1;
        }
    }
}
