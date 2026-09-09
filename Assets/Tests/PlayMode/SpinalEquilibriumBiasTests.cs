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
    /// Phase 5H15, sections 6 to 13.
    ///
    /// H14 measured the abdomen holding 18.5 deg where 6.4 was commanded, with
    /// the drive using under a tenth of its authored ceiling, and called the
    /// mechanism droop under an uncompensated gravitational moment. Demand well
    /// below the ceiling proves the ceiling is not binding. It does not by
    /// itself prove gravity is the disturbance, so this phase measures the
    /// gravitational generalized moment directly and checks whether the error
    /// actually tracks it before any compensation is proposed.
    ///
    /// The spine is two joints in series carrying the same distal mass, so a
    /// bias on one moves the other. Nothing here assumes a target offset
    /// produces an equal actual correction: the 2x2 closed-loop response is
    /// identified by central difference and the required bias is solved from
    /// that, not from torque over authored stiffness.
    /// </summary>
    public sealed class SpinalEquilibriumBiasTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H15";
        private const float ApproachPhaseRate = 0.30f;

        /// <summary>Ticks held before any measurement window opens.</summary>
        private const int SettleTicks = 400;

        /// <summary>Window the settled verdict and the measurement average use.</summary>
        private const int WindowTicks = 100;

        private static readonly float[] Phases = { 0.00f, 0.25f, 0.55f, 0.80f, 1.00f };

        /// <summary>
        /// Bodies gravitationally distal to each spine joint. The bar is added
        /// separately when loaded, because it reaches the chain through the
        /// saddle rather than through a segment recipe.
        /// </summary>
        private static readonly string[] DistalOfAbdomen =
        {
            "abdomen", "thorax", "head_neck",
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand"
        };

        private static readonly string[] DistalOfThorax =
        {
            "thorax", "head_neck",
            "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm",
            "left_hand", "right_hand"
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
                if (adapter != null && adapter.Preload != null)
                {
                    // Never leave a diagnostic bias on the shared preload.
                    adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Abdomen, 0f);
                    adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Thorax, 0f);
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

        // ------------------------------------------------------------------
        // Held-sample measurement.
        // ------------------------------------------------------------------

        private struct Hold
        {
            public float LoadKg;
            public float Phase;

            public float AbdomenNominal;
            public float AbdomenBias;
            public float AbdomenFinal;
            public float AbdomenActual;
            public float AbdomenError;      // actual - nominal, the reference miss
            public float AbdomenOmega;
            public float AbdomenAlpha;
            public float AbdomenDrift;
            public float AbdomenDemand;
            public float AbdomenMaxForce;
            public float AbdomenSolverTorqueX;

            public float ThoraxNominal;
            public float ThoraxBias;
            public float ThoraxFinal;
            public float ThoraxActual;
            public float ThoraxError;
            public float ThoraxOmega;
            public float ThoraxAlpha;
            public float ThoraxDrift;
            public float ThoraxDemand;
            public float ThoraxMaxForce;
            public float ThoraxSolverTorqueX;

            public float HipNominal;
            public float HipActual;

            public float TauGAbdomenBody;
            public float TauGAbdomenBar;
            public float TauGAbdomenTotal;
            public float TauGThoraxBody;
            public float TauGThoraxBar;
            public float TauGThoraxTotal;

            public float WorldTrunkPitch;
            public float ComAp;
            public float SupportMin;
            public float SupportMax;
            public float SupportLength;
            public float CopAp;
            public int Contacts;
            public float SaddleSeparation;
            public bool SaddleAttached;
            public bool Settled;
        }

        /// <summary>
        /// Ramps to a phase at the production rate, holds it with zero phase
        /// velocity, then averages over a window at the end of the hold. The
        /// settled verdict comes from measured motion across that window rather
        /// than from a single tick.
        /// </summary>
        private IEnumerator HoldAndMeasure(
            float loadKg, float phase, float abdomenBiasDeg, float thoraxBiasDeg, Hold[] result)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Abdomen, abdomenBiasDeg);
            adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Thorax, thoraxBiasDeg);
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

            var accumulator = new Hold { LoadKg = loadKg, Phase = phase };
            int samples = 0;
            float previousAbdomenOmega = 0f;
            float previousThoraxOmega = 0f;
            float peakAbdomenOmega = 0f;
            float peakThoraxOmega = 0f;
            float peakAbdomenAlpha = 0f;
            float peakThoraxAlpha = 0f;
            float abdomenWindowStart = 0f;
            float thoraxWindowStart = 0f;
            float abdomenWindowEnd = 0f;
            float thoraxWindowEnd = 0f;

            for (int tick = 0; tick < SettleTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                PoweredJointController.PoweredJointRuntime abdomen = powered.GetJoint("abdomen");
                PoweredJointController.PoweredJointRuntime thorax = powered.GetJoint("thorax");
                float abdomenOmega = abdomen.Diagnostic.ActualAngularVelocityRadS.x;
                float thoraxOmega = thorax.Diagnostic.ActualAngularVelocityRadS.x;
                float abdomenAlpha = (abdomenOmega - previousAbdomenOmega) / dt;
                float thoraxAlpha = (thoraxOmega - previousThoraxOmega) / dt;
                previousAbdomenOmega = abdomenOmega;
                previousThoraxOmega = thoraxOmega;

                float abdomenNow = TwistX(abdomen.Diagnostic.ActualRelative);
                float thoraxNow = TwistX(thorax.Diagnostic.ActualRelative);
                if (tick == SettleTicks - WindowTicks)
                {
                    abdomenWindowStart = abdomenNow;
                    thoraxWindowStart = thoraxNow;
                }
                if (tick < SettleTicks - WindowTicks)
                    continue;
                abdomenWindowEnd = abdomenNow;
                thoraxWindowEnd = thoraxNow;

                peakAbdomenOmega = Mathf.Max(peakAbdomenOmega, Mathf.Abs(abdomenOmega));
                peakThoraxOmega = Mathf.Max(peakThoraxOmega, Mathf.Abs(thoraxOmega));
                peakAbdomenAlpha = Mathf.Max(peakAbdomenAlpha, Mathf.Abs(abdomenAlpha));
                peakThoraxAlpha = Mathf.Max(peakThoraxAlpha, Mathf.Abs(thoraxAlpha));

                adapter.TryGetTargetComposition("abdomen", out var abdomenComposition);
                adapter.TryGetTargetComposition("thorax", out var thoraxComposition);
                adapter.TryGetTargetComposition("left_thigh", out var hipComposition);

                accumulator.AbdomenNominal += TwistX(abdomenComposition.Nominal);
                accumulator.AbdomenBias += TwistX(abdomenComposition.GravityBias);
                accumulator.AbdomenFinal += TwistX(abdomenComposition.Final);
                accumulator.AbdomenActual += TwistX(abdomen.Diagnostic.ActualRelative);
                accumulator.AbdomenDemand += abdomen.Diagnostic.ModeledDemand;
                accumulator.AbdomenMaxForce = abdomen.Diagnostic.MaximumForceNm;
                accumulator.AbdomenSolverTorqueX += abdomen.Diagnostic.SolverTorqueJointSpaceNm.x;

                accumulator.ThoraxNominal += TwistX(thoraxComposition.Nominal);
                accumulator.ThoraxBias += TwistX(thoraxComposition.GravityBias);
                accumulator.ThoraxFinal += TwistX(thoraxComposition.Final);
                accumulator.ThoraxActual += TwistX(thorax.Diagnostic.ActualRelative);
                accumulator.ThoraxDemand += thorax.Diagnostic.ModeledDemand;
                accumulator.ThoraxMaxForce = thorax.Diagnostic.MaximumForceNm;
                accumulator.ThoraxSolverTorqueX += thorax.Diagnostic.SolverTorqueJointSpaceNm.x;

                accumulator.HipNominal += TwistX(hipComposition.Nominal);
                accumulator.HipActual += TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative);

                GravityMoment("abdomen", DistalOfAbdomen, loadKg,
                    out float abdomenBody, out float abdomenBar);
                GravityMoment("thorax", DistalOfThorax, loadKg,
                    out float thoraxBody, out float thoraxBar);
                accumulator.TauGAbdomenBody += abdomenBody;
                accumulator.TauGAbdomenBar += abdomenBar;
                accumulator.TauGThoraxBody += thoraxBody;
                accumulator.TauGThoraxBar += thoraxBar;

                Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                Rigidbody thoraxBody2 = _rig.Segments["thorax"].Body;
                Vector3 axis = thoraxBody2.position - pelvis.position;
                accumulator.WorldTrunkPitch += Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;

                accumulator.ComAp += balance.SystemCom.z;
                accumulator.SupportMin += balance.SupportApMin;
                accumulator.SupportMax += balance.SupportApMax;
                accumulator.SupportLength += balance.SupportApLength;
                accumulator.CopAp += balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter;
                accumulator.Contacts = balance.SupportContactCount;
                samples++;
            }

            float inverse = samples > 0 ? 1f / samples : 0f;
            accumulator.AbdomenNominal *= inverse;
            accumulator.AbdomenBias *= inverse;
            accumulator.AbdomenFinal *= inverse;
            accumulator.AbdomenActual *= inverse;
            accumulator.AbdomenDemand *= inverse;
            accumulator.AbdomenSolverTorqueX *= inverse;
            accumulator.ThoraxNominal *= inverse;
            accumulator.ThoraxBias *= inverse;
            accumulator.ThoraxFinal *= inverse;
            accumulator.ThoraxActual *= inverse;
            accumulator.ThoraxDemand *= inverse;
            accumulator.ThoraxSolverTorqueX *= inverse;
            accumulator.HipNominal *= inverse;
            accumulator.HipActual *= inverse;
            accumulator.TauGAbdomenBody *= inverse;
            accumulator.TauGAbdomenBar *= inverse;
            accumulator.TauGThoraxBody *= inverse;
            accumulator.TauGThoraxBar *= inverse;
            accumulator.WorldTrunkPitch *= inverse;
            accumulator.ComAp *= inverse;
            accumulator.SupportMin *= inverse;
            accumulator.SupportMax *= inverse;
            accumulator.SupportLength *= inverse;
            accumulator.CopAp *= inverse;

            accumulator.TauGAbdomenTotal = accumulator.TauGAbdomenBody + accumulator.TauGAbdomenBar;
            accumulator.TauGThoraxTotal = accumulator.TauGThoraxBody + accumulator.TauGThoraxBar;
            accumulator.AbdomenError = accumulator.AbdomenActual - accumulator.AbdomenNominal;
            accumulator.ThoraxError = accumulator.ThoraxActual - accumulator.ThoraxNominal;
            accumulator.AbdomenOmega = peakAbdomenOmega;
            accumulator.ThoraxOmega = peakThoraxOmega;
            accumulator.AbdomenAlpha = peakAbdomenAlpha;
            accumulator.ThoraxAlpha = peakThoraxAlpha;

            // Settled is decided on how far the joint actually travels across
            // the window, not on peak instantaneous rate. Differencing angular
            // velocity at a 100 Hz tick amplifies solver jitter into tens of
            // deg/s^2 on a joint that is not moving anywhere, so a peak-rate
            // gate measures noise. Net drift over 1 s of hold does not.
            accumulator.AbdomenDrift = abdomenWindowEnd - abdomenWindowStart;
            accumulator.ThoraxDrift = thoraxWindowEnd - thoraxWindowStart;
            accumulator.Settled =
                Mathf.Abs(accumulator.AbdomenDrift) < 0.10f &&
                Mathf.Abs(accumulator.ThoraxDrift) < 0.10f;

            SquatBarSaddle saddle = _controller.Saddle;
            if (saddle != null)
            {
                accumulator.SaddleAttached = saddle.IsAttached;
                accumulator.SaddleSeparation = saddle.SaddleSeparationMeters;
            }

            result[0] = accumulator;
            yield return null;
        }

        /// <summary>
        /// Sagittal gravitational generalized moment about a spine joint, from
        /// the actual rigid bodies: sum of (r_com - r_joint) x (m g) projected
        /// onto the joint's calibrated flexion axis in world. Masses and
        /// centres of mass are the engine's, never the mesh's.
        /// </summary>
        private void GravityMoment(
            string jointId, string[] distal, float loadKg, out float bodyMoment, out float barMoment)
        {
            PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
            Transform child = joint.Joint.transform;
            Vector3 anchor = child.TransformPoint(joint.Joint.anchor);
            Vector3 axis = child.TransformDirection(joint.Joint.axis).normalized;
            Vector3 gravity = Physics.gravity;

            bodyMoment = 0f;
            for (int index = 0; index < distal.Length; index++)
            {
                if (!_rig.Segments.TryGetValue(distal[index], out PhysicalAthleteRig.SegmentRuntime segment))
                    continue;
                Rigidbody body = segment.Body;
                if (body == null)
                    continue;
                Vector3 lever = body.worldCenterOfMass - anchor;
                bodyMoment += Vector3.Dot(Vector3.Cross(lever, body.mass * gravity), axis);
            }

            barMoment = 0f;
            if (loadKg <= 0.01f)
                return;
            PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            if (barbell == null || barbell.Body == null)
                return;
            Vector3 barLever = barbell.Body.worldCenterOfMass - anchor;
            barMoment = Vector3.Dot(Vector3.Cross(barLever, barbell.Body.mass * gravity), axis);
        }

        // ------------------------------------------------------------------
        // E1. Baseline equilibrium and gravity moments, sections 6 to 9.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator E1_SPINE_EQUILIBRIUM_BASELINE_AND_GRAVITY_MOMENT()
        {
            var holds = new List<Hold>();
            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float phase in Phases)
                {
                    var result = new Hold[1];
                    yield return HoldAndMeasure(load, phase, 0f, 0f, result);
                    holds.Add(result[0]);
                }
            }

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,phase,settled,abd_nominal,abd_bias,abd_final,abd_actual,abd_error," +
                           "abd_omega_deg_s,abd_alpha_deg_s2,abd_demand,abd_max_force,abd_solver_torque_x," +
                           "tho_nominal,tho_bias,tho_final,tho_actual,tho_error," +
                           "tho_omega_deg_s,tho_alpha_deg_s2,tho_demand,tho_max_force,tho_solver_torque_x," +
                           "hip_nominal,hip_actual,tau_g_abd_body,tau_g_abd_bar,tau_g_abd_total," +
                           "tau_g_tho_body,tau_g_tho_bar,tau_g_tho_total,world_trunk_pitch," +
                           "com_ap,support_min,support_max,support_length,cop_ap,contacts," +
                           "saddle_sep,saddle_attached");
            foreach (Hold h in holds)
                csv.AppendLine(Csv(h));

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H15 E1 SPINE EQUILIBRIUM BASELINE AND GRAVITATIONAL MOMENT");
            report.AppendLine("Fresh reset per sample, ramped at the production rate then held with zero");
            report.AppendLine("phase velocity for " + SettleTicks.ToString(CultureInfo.InvariantCulture) +
                              " ticks, averaged over the last " + WindowTicks.ToString(CultureInfo.InvariantCulture) + ".");
            report.AppendLine("error is actual minus the GAM-10 nominal, the reference miss the phase is about.");
            report.AppendLine("tau_g is the gravitational generalized moment about the joint's calibrated");
            report.AppendLine("flexion axis, from engine masses and centres of mass.");
            report.AppendLine();
            report.AppendLine("load  s_q   settled  abdNom  abdAct  abdErr  drift  demand |" +
                              "  thoNom  thoAct  thoErr  drift  demand |  tauG_abd  tauG_tho  err/tauG");
            foreach (Hold h in holds)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F0}  {1,4:F2}  {2,-7}  {3,6:F2}  {4,6:F2}  {5,6:F2}  {6,6:F3}  {7,6:F3} |" +
                    "  {8,6:F2}  {9,6:F2}  {10,6:F2}  {11,6:F3}  {12,6:F3} |  {13,8:F1}  {14,8:F1}  {15,8:F5}",
                    h.LoadKg, h.Phase, h.Settled,
                    h.AbdomenNominal, h.AbdomenActual, h.AbdomenError, h.AbdomenDrift, h.AbdomenDemand,
                    h.ThoraxNominal, h.ThoraxActual, h.ThoraxError, h.ThoraxDrift, h.ThoraxDemand,
                    h.TauGAbdomenTotal, h.TauGThoraxTotal,
                    Mathf.Abs(h.TauGAbdomenTotal) > 1f ? h.AbdomenError / h.TauGAbdomenTotal : 0f));
            }

            report.AppendLine();
            report.AppendLine(ClassifyGravityCausality(holds));

            WriteMeasurement("spine-equilibrium-baseline.csv", csv.ToString());
            WriteMeasurement("spine-gravity-generalized-moments.csv", csv.ToString());
            WriteMeasurement("spine-equilibrium-baseline.txt", report.ToString());
            Debug.Log(report.ToString());

            Assert.That(holds.Count, Is.EqualTo(2 * Phases.Length));
            yield return null;
        }

        /// <summary>
        /// Section 9. Gravity is the candidate disturbance, so the error must
        /// point the way gravity pushes and must grow with it. Both are checked
        /// on the measured numbers rather than assumed from H14.
        /// </summary>
        private static string ClassifyGravityCausality(List<Hold> holds)
        {
            var text = new StringBuilder();
            int signAgree = 0;
            int considered = 0;
            float minRatio = float.MaxValue;
            float maxRatio = 0f;

            foreach (Hold h in holds)
            {
                if (!h.Settled || Mathf.Abs(h.TauGAbdomenTotal) < 1f)
                    continue;
                considered++;
                // Both quantities are expressed about the same calibrated
                // joint-space flexion axis, and a finite spring yields along
                // the moment applied to it, so a positive gravitational
                // flexion moment must appear as a positive flexion error.
                // The compensating bias is the opposite sign; that inversion
                // belongs in the solve, not in this check.
                if (Mathf.Sign(h.AbdomenError) == Mathf.Sign(h.TauGAbdomenTotal))
                    signAgree++;
                float ratio = h.AbdomenError / h.TauGAbdomenTotal;
                minRatio = Mathf.Min(minRatio, ratio);
                maxRatio = Mathf.Max(maxRatio, ratio);
            }

            text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "Settled samples with a material abdomen gravity moment: {0}. Error sign agrees with " +
                "the gravitational flexion direction in {1} of them.", considered, signAgree));
            if (considered > 0)
            {
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "error / gravity-moment ratio spans {0:F5} to {1:F5} deg per N.m, a spread of {2:F2}x.",
                    minRatio, maxRatio, maxRatio / Mathf.Max(minRatio, 1e-6f)));
            }

            string verdict;
            if (considered == 0)
                verdict = "LOW (no settled sample carried a material gravity moment)";
            else if (signAgree == considered && maxRatio / Mathf.Max(minRatio, 1e-6f) < 3f)
                verdict = "HIGH";
            else if (signAgree == considered)
                verdict = "MEDIUM (sign is consistent, magnitude scaling is not proportional)";
            else
                verdict = "LOW (the error does not consistently follow the gravitational direction)";
            text.AppendLine("GRAVITY_EQUILIBRIUM_CAUSALITY = " + verdict);
            return text.ToString();
        }

        // ------------------------------------------------------------------
        // E2. Coupled 2x2 response, section 10.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator E2_COUPLED_SPINE_TARGET_RESPONSE()
        {
            const float probeDeg = 2f;

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,phase,probe_deg,j_abd_abd,j_abd_tho,j_tho_abd,j_tho_tho," +
                           "determinant,condition,e0_abd,e0_tho,u_abd,u_tho,within_soft_bound,within_hard_bound");

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H15 E2 COUPLED ABDOMEN-THORAX TARGET RESPONSE");
            report.AppendLine("Central difference on a +/-" + probeDeg.ToString("F0", CultureInfo.InvariantCulture) +
                              " deg anatomical flexion bias, fresh reset per perturbation.");
            report.AppendLine("J[i][j] is d(actual_i) / d(target_bias_j), degrees per degree.");
            report.AppendLine("u is the offline solve of J u = -e0 for the bias that would zero the");
            report.AppendLine("reference miss. Bounds are the preload's own: soft " +
                              (SquatEquilibriumPreload.SoftBoundRad * Mathf.Rad2Deg).ToString("F0", CultureInfo.InvariantCulture) +
                              " deg, hard " +
                              (SquatEquilibriumPreload.HardBoundRad * Mathf.Rad2Deg).ToString("F0", CultureInfo.InvariantCulture) + " deg.");
            report.AppendLine();
            report.AppendLine("load  s_q    Jaa     Jat     Jta     Jtt      det    cond |   e0_abd  e0_tho |   u_abd   u_tho  bound");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float phase in Phases)
                {
                    var baseline = new Hold[1];
                    var abdomenPlus = new Hold[1];
                    var abdomenMinus = new Hold[1];
                    var thoraxPlus = new Hold[1];
                    var thoraxMinus = new Hold[1];

                    yield return HoldAndMeasure(load, phase, 0f, 0f, baseline);
                    yield return HoldAndMeasure(load, phase, probeDeg, 0f, abdomenPlus);
                    yield return HoldAndMeasure(load, phase, -probeDeg, 0f, abdomenMinus);
                    yield return HoldAndMeasure(load, phase, 0f, probeDeg, thoraxPlus);
                    yield return HoldAndMeasure(load, phase, 0f, -probeDeg, thoraxMinus);

                    float span = 2f * probeDeg;
                    float jaa = (abdomenPlus[0].AbdomenActual - abdomenMinus[0].AbdomenActual) / span;
                    float jta = (abdomenPlus[0].ThoraxActual - abdomenMinus[0].ThoraxActual) / span;
                    float jat = (thoraxPlus[0].AbdomenActual - thoraxMinus[0].AbdomenActual) / span;
                    float jtt = (thoraxPlus[0].ThoraxActual - thoraxMinus[0].ThoraxActual) / span;

                    float determinant = jaa * jtt - jat * jta;
                    float condition = ConditionNumber(jaa, jat, jta, jtt);

                    float e0Abdomen = baseline[0].AbdomenError;
                    float e0Thorax = baseline[0].ThoraxError;

                    float uAbdomen = 0f;
                    float uThorax = 0f;
                    bool invertible = Mathf.Abs(determinant) > 1e-4f;
                    if (invertible)
                    {
                        // u = -J^-1 e0
                        uAbdomen = -(jtt * e0Abdomen - jat * e0Thorax) / determinant;
                        uThorax = -(-jta * e0Abdomen + jaa * e0Thorax) / determinant;
                    }

                    float soft = SquatEquilibriumPreload.SoftBoundRad * Mathf.Rad2Deg;
                    float hard = SquatEquilibriumPreload.HardBoundRad * Mathf.Rad2Deg;
                    float worst = Mathf.Max(Mathf.Abs(uAbdomen), Mathf.Abs(uThorax));
                    string bound = !invertible ? "SINGULAR"
                        : worst <= soft ? "soft"
                        : worst <= hard ? "HARD"
                        : "OVER";

                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1:F2},{2:F1},{3:F5},{4:F5},{5:F5},{6:F5},{7:F6},{8:F3},{9:F4},{10:F4},{11:F4},{12:F4},{13},{14}",
                        load, phase, probeDeg, jaa, jat, jta, jtt, determinant, condition,
                        e0Abdomen, e0Thorax, uAbdomen, uThorax,
                        worst <= soft ? 1 : 0, worst <= hard ? 1 : 0));

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,4:F0}  {1,4:F2}  {2,6:F3}  {3,6:F3}  {4,6:F3}  {5,6:F3}  {6,7:F4}  {7,5:F1} |  {8,7:F2}  {9,7:F2} |  {10,6:F2}  {11,6:F2}  {12}",
                        load, phase, jaa, jat, jta, jtt, determinant, condition,
                        e0Abdomen, e0Thorax, uAbdomen, uThorax, bound));
                }
            }

            WriteMeasurement("spine-coupled-response.csv", csv.ToString());
            WriteMeasurement("spine-coupled-response.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private static float ConditionNumber(float a, float b, float c, float d)
        {
            // Singular values of a 2x2 via the closed form on J^T J.
            float m = a * a + b * b + c * c + d * d;
            float determinant = a * d - b * c;
            float discriminant = Mathf.Sqrt(Mathf.Max(0f, m * m - 4f * determinant * determinant));
            float sigmaMax = Mathf.Sqrt(Mathf.Max(0f, 0.5f * (m + discriminant)));
            float sigmaMin = Mathf.Sqrt(Mathf.Max(0f, 0.5f * (m - discriminant)));
            return sigmaMin < 1e-6f ? float.PositiveInfinity : sigmaMax / sigmaMin;
        }

        // ------------------------------------------------------------------
        // Shared helpers.
        // ------------------------------------------------------------------

        private static string Csv(Hold h) => string.Format(CultureInfo.InvariantCulture,
            "{0:F0},{1:F2},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F3},{10:F5},{11:F1},{12:F3}," +
            "{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18:F4},{19:F3},{20:F5},{21:F1},{22:F3}," +
            "{23:F4},{24:F4},{25:F2},{26:F2},{27:F2},{28:F2},{29:F2},{30:F2},{31:F3}," +
            "{32:F5},{33:F5},{34:F5},{35:F5},{36:F5},{37},{38:F5},{39}",
            h.LoadKg, h.Phase, h.Settled ? 1 : 0,
            h.AbdomenNominal, h.AbdomenBias, h.AbdomenFinal, h.AbdomenActual, h.AbdomenError,
            h.AbdomenOmega * Mathf.Rad2Deg, h.AbdomenAlpha * Mathf.Rad2Deg,
            h.AbdomenDemand, h.AbdomenMaxForce, h.AbdomenSolverTorqueX,
            h.ThoraxNominal, h.ThoraxBias, h.ThoraxFinal, h.ThoraxActual, h.ThoraxError,
            h.ThoraxOmega * Mathf.Rad2Deg, h.ThoraxAlpha * Mathf.Rad2Deg,
            h.ThoraxDemand, h.ThoraxMaxForce, h.ThoraxSolverTorqueX,
            h.HipNominal, h.HipActual,
            h.TauGAbdomenBody, h.TauGAbdomenBar, h.TauGAbdomenTotal,
            h.TauGThoraxBody, h.TauGThoraxBar, h.TauGThoraxTotal, h.WorldTrunkPitch,
            h.ComAp, h.SupportMin, h.SupportMax, h.SupportLength, h.CopAp, h.Contacts,
            h.SaddleSeparation, h.SaddleAttached ? 1 : 0);

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
