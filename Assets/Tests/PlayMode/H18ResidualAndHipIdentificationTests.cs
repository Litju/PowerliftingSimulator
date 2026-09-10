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
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H18: Post-H17 0 kg and 25 kg residual remeasurement and hip equilibrium decision.
    /// Comprehensive diagnostic and system identification test fixture.
    /// </summary>
    public sealed class H18ResidualAndHipIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H18";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/h18-hip";

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

        private static readonly float[] StaticHoldPhases =
        {
            0.00f, 0.10f, 0.25f, 0.40f, 0.55f, 0.80f, 1.00f
        };

        private static readonly string[] MonitoredJoints =
        {
            "left_thigh", "right_thigh", "abdomen", "thorax",
            "left_shank", "right_shank", "left_foot", "right_foot"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _bar;
        private SquatPhysicalPrototypeController _controller;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
            _bar = null;
            _controller = null;
            yield return null;
        }

        private IEnumerator LoadScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _bar = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();

            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_bar, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);

            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True);
        }

        private void PrepareManualRuntime(float loadKg)
        {
            _controller.SetLoad(loadKg);
            _controller.enabled = false;
            _bootstrap.enabled = false;
            _rig.enabled = false;
            _bar.enabled = false;
        }

        private static void AdvanceTicks(FoundationRuntime runtime, int count, PhysicalFootContactDetector leftFoot, PhysicalFootContactDetector rightFoot)
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int i = 0; i < count; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                if (leftFoot != null) leftFoot.PhysicsTickUpdate(dt);
                if (rightFoot != null) rightFoot.PhysicsTickUpdate(dt);
            }
        }

        // -------------------------------------------------------------------------
        // TEST 1: REBASELINE DYNAMIC RESIDUALS AND RANKING (0 kg and 25 kg)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_01_DYNAMIC_RESIDUAL_REBASELINE_AND_RANKING()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            foreach (float load in new[] { 0f, 25f })
            {
                yield return LoadScene();
                PrepareManualRuntime(load);

                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                SquatBarSaddle saddle = _controller.Saddle;
                PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                PhysicalFootContactDetector rightFoot = _controller.RightFootContact;
                Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                Rigidbody thorax = _rig.Segments["thorax"].Body;
                Rigidbody leftFootBody = _rig.Segments["left_foot"].Body;
                Rigidbody rightFootBody = _rig.Segments["right_foot"].Body;

                // Settle 30 ticks
                AdvanceTicks(runtime, 30, leftFoot, rightFoot);

                adapter.StartSquat();

                var csv = new StringBuilder();
                csv.AppendLine(
                    "time,tick,state,direction,sq," +
                    "hip_l_nom,hip_l_grav,hip_l_bal,hip_l_final,hip_l_tgt_vel,hip_l_act,hip_l_act_vel,hip_l_nom_err,hip_l_final_err,hip_l_lim,hip_l_demand,hip_l_max_force," +
                    "hip_r_nom,hip_r_grav,hip_r_bal,hip_r_final,hip_r_tgt_vel,hip_r_act,hip_r_act_vel,hip_r_nom_err,hip_r_final_err,hip_r_lim,hip_r_demand,hip_r_max_force," +
                    "abd_nom,abd_grav,abd_bal,abd_final,abd_tgt_vel,abd_act,abd_act_vel,abd_nom_err,abd_final_err,abd_lim,abd_demand,abd_max_force," +
                    "tho_nom,tho_grav,tho_bal,tho_final,tho_tgt_vel,tho_act,tho_act_vel,tho_nom_err,tho_final_err,tho_lim,tho_demand,tho_max_force," +
                    "knee_l_nom,knee_l_grav,knee_l_bal,knee_l_final,knee_l_tgt_vel,knee_l_act,knee_l_act_vel,knee_l_nom_err,knee_l_final_err,knee_l_lim,knee_l_demand,knee_l_max_force," +
                    "knee_r_nom,knee_r_grav,knee_r_bal,knee_r_final,knee_r_tgt_vel,knee_r_act,knee_r_act_vel,knee_r_nom_err,knee_r_final_err,knee_r_lim,knee_r_demand,knee_r_max_force," +
                    "ankle_l_nom,ankle_l_grav,ankle_l_bal,ankle_l_final,ankle_l_tgt_vel,ankle_l_act,ankle_l_act_vel,ankle_l_nom_err,ankle_l_final_err,ankle_l_lim,ankle_l_demand,ankle_l_max_force," +
                    "ankle_r_nom,ankle_r_grav,ankle_r_bal,ankle_r_final,ankle_r_tgt_vel,ankle_r_act,ankle_r_act_vel,ankle_r_nom_err,ankle_r_final_err,ankle_r_lim,ankle_r_demand,ankle_r_max_force," +
                    "pelvis_pitch,trunk_pitch,ref_trunk_pitch,com_ap,com_y,cop_ap,support_min,support_max,contacts,foot_pitch_l,foot_pitch_r,bar_x,bar_y,bar_z,bar_vx,bar_vy,bar_vz,saddle_sep,saddle_att");

                // Tracking statistics accumulators: [jointName] -> (peakErr, peakSq, sumSqErr, count)
                var stats = new Dictionary<string, (float peakErr, float peakSq, float sumSqErr, int count)>(StringComparer.Ordinal);
                foreach (string j in MonitoredJoints)
                    stats[j] = (0f, 0f, 0f, 0);
                float peakTrunkErr = 0f, peakTrunkSq = 0f, sumSqTrunk = 0f;
                int trunkCount = 0;

                // Squat cycle ticks: 400 ticks (8.0s)
                int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                    float time = tick * (float)SimulationConstants.FixedDeltaTimeSeconds;
                    float sq = adapter.Sq;

                    Vector3 trunkAxis = thorax.position - pelvis.position;
                    float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;
                    float refTrunk = SampleReference(sq);
                    float trunkErr = trunkPitch - refTrunk;
                    if (Mathf.Abs(trunkErr) > Mathf.Abs(peakTrunkErr)) { peakTrunkErr = trunkErr; peakTrunkSq = sq; }
                    sumSqTrunk += trunkErr * trunkErr;
                    trunkCount++;

                    Vector3 pelForward = pelvis.transform.forward;
                    float pelPitch = Mathf.Atan2(pelForward.y, pelForward.z) * Mathf.Rad2Deg;

                    Vector3 lfForward = leftFootBody.transform.forward;
                    float footPitchL = Mathf.Atan2(lfForward.y, lfForward.z) * Mathf.Rad2Deg;
                    Vector3 rfForward = rightFootBody.transform.forward;
                    float footPitchR = Mathf.Atan2(rfForward.y, rfForward.z) * Mathf.Rad2Deg;

                    Vector3 barPos = _bar != null && _bar.Body != null ? _bar.Body.position : Vector3.zero;
                    Vector3 barVel = _bar != null && _bar.Body != null ? _bar.Body.linearVelocity : Vector3.zero;
                    float saddleSep = saddle != null ? saddle.SaddleSeparationMeters : 0f;
                    int saddleAtt = (saddle != null && saddle.IsAttached) ? 1 : 0;

                    var line = new StringBuilder();
                    line.AppendFormat(CultureInfo.InvariantCulture, "{0:F3},{1},{2},{3},{4:F4}",
                        time, tick, adapter.State, adapter.Direction, sq);

                    foreach (string j in MonitoredJoints)
                    {
                        adapter.TryGetTargetComposition(j, out var comp);
                        var diag = powered.GetJoint(j).Diagnostic;

                        float nom = TwistX(comp.Nominal);
                        float grav = TwistX(comp.GravityBias);
                        float bal = TwistX(comp.BalanceOffset);
                        float final = TwistX(comp.Final);
                        float tgtVel = diag.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float act = TwistX(diag.ActualRelative);
                        float actVel = diag.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float nomErr = act - nom;
                        float finalErr = act - final;

                        var s = stats[j];
                        if (Mathf.Abs(nomErr) > Mathf.Abs(s.peakErr)) { s.peakErr = nomErr; s.peakSq = sq; }
                        s.sumSqErr += nomErr * nomErr;
                        s.count++;
                        stats[j] = s;

                        line.AppendFormat(CultureInfo.InvariantCulture,
                            ",{0:F2},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F4},{10:F4},{11:F1}",
                            nom, grav, bal, final, tgtVel, act, actVel, nomErr, finalErr, diag.LimitProximity, diag.ModeledDemand, diag.MaximumForceNm);
                    }

                    line.AppendFormat(CultureInfo.InvariantCulture,
                        ",{0:F2},{1:F2},{2:F2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8},{9:F2},{10:F2},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16:F4},{17:F4},{18}",
                        pelPitch, trunkPitch, refTrunk,
                        adapter.Balance.SystemCom.z, adapter.Balance.SystemCom.y,
                        adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter,
                        adapter.Balance.SupportApMin, adapter.Balance.SupportApMax,
                        adapter.Balance.SupportContactCount,
                        footPitchL, footPitchR,
                        barPos.x, barPos.y, barPos.z, barVel.x, barVel.y, barVel.z,
                        saddleSep, saddleAtt);

                    csv.AppendLine(line.ToString());
                }

                string fileName = string.Format(CultureInfo.InvariantCulture, "post-h17-dynamic-residual-{0:F0}kg.csv", load);
                File.WriteAllText(Path.Combine(dir, fileName), csv.ToString());
                Debug.Log($"[H18_01] Wrote {fileName} ({totalTicks} ticks).");

                // Output summary ranking for this load
                Debug.Log($"=== DYNAMIC RESIDUAL RANKING (Load: {load:F0} kg) ===");
                foreach (string j in MonitoredJoints)
                {
                    var s = stats[j];
                    float rms = Mathf.Sqrt(s.sumSqErr / Mathf.Max(1, s.count));
                    Debug.Log($"  Joint {j,-12}: PeakErr={s.peakErr,6:F2} deg at s_q={s.peakSq:F2} | RMS={rms,5:F2} deg");
                }
                float trunkRms = Mathf.Sqrt(sumSqTrunk / Mathf.Max(1, trunkCount));
                Debug.Log($"  World Trunk  : PeakErr={peakTrunkErr,6:F2} deg at s_q={peakTrunkSq:F2} | RMS={trunkRms,5:F2} deg");
            }

            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 2: STATIC / QUASI-STATIC HIP FEASIBILITY (0 kg and 25 kg)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_02_STATIC_QUASI_STATIC_HIP_FEASIBILITY()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,sq,hip_l_nom,hip_l_grav,hip_l_bal,hip_l_final,hip_l_act,hip_l_nom_err,hip_l_final_err,hip_l_demand," +
                           "hip_r_nom,hip_r_grav,hip_r_bal,hip_r_final,hip_r_act,hip_r_nom_err,hip_r_final_err,hip_r_demand," +
                           "abd_act,abd_err,tho_act,tho_err,trunk_pitch,ref_trunk,trunk_err,com_ap,cop_ap,contacts,saddle_sep");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float sq in StaticHoldPhases)
                {
                    yield return LoadScene();
                    PrepareManualRuntime(load);

                    FoundationRuntime runtime = _bootstrap.Runtime;
                    SquatPhysicalAdapter adapter = _controller.Adapter;
                    PoweredJointController powered = _rig.PoweredController;
                    SquatBarSaddle saddle = _controller.Saddle;
                    PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                    PhysicalFootContactDetector rightFoot = _controller.RightFootContact;
                    Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                    Rigidbody thorax = _rig.Segments["thorax"].Body;

                    // Hold phase with phaseVelocity = 0
                    adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);

                    // Settle for 200 ticks (4.0s)
                    AdvanceTicks(runtime, 200, leftFoot, rightFoot);

                    adapter.TryGetTargetComposition("left_thigh", out var hipLC);
                    adapter.TryGetTargetComposition("right_thigh", out var hipRC);
                    adapter.TryGetTargetComposition("abdomen", out var abdC);
                    adapter.TryGetTargetComposition("thorax", out var thoC);

                    var hipLDiag = powered.GetJoint("left_thigh").Diagnostic;
                    var hipRDiag = powered.GetJoint("right_thigh").Diagnostic;
                    var abdDiag = powered.GetJoint("abdomen").Diagnostic;
                    var thoDiag = powered.GetJoint("thorax").Diagnostic;

                    float hipLNom = TwistX(hipLC.Nominal);
                    float hipLGrav = TwistX(hipLC.GravityBias);
                    float hipLBal = TwistX(hipLC.BalanceOffset);
                    float hipLFinal = TwistX(hipLC.Final);
                    float hipLAct = TwistX(hipLDiag.ActualRelative);
                    float hipLNomErr = hipLAct - hipLNom;
                    float hipLFinalErr = hipLAct - hipLFinal;

                    float hipRNom = TwistX(hipRC.Nominal);
                    float hipRGrav = TwistX(hipRC.GravityBias);
                    float hipRBal = TwistX(hipRC.BalanceOffset);
                    float hipRFinal = TwistX(hipRC.Final);
                    float hipRAct = TwistX(hipRDiag.ActualRelative);
                    float hipRNomErr = hipRAct - hipRNom;
                    float hipRFinalErr = hipRAct - hipRFinal;

                    float abdAct = TwistX(abdDiag.ActualRelative);
                    float abdErr = abdAct - TwistX(abdC.Nominal);
                    float thoAct = TwistX(thoDiag.ActualRelative);
                    float thoErr = thoAct - TwistX(thoC.Nominal);

                    Vector3 trunkAxis = thorax.position - pelvis.position;
                    float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;
                    float refTrunk = SampleReference(sq);
                    float trunkErr = trunkPitch - refTrunk;

                    float comAp = adapter.Balance.SystemCom.z;
                    float copAp = adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter;
                    int contacts = adapter.Balance.SupportContactCount;
                    float saddleSep = saddle != null ? saddle.SaddleSeparationMeters : 0f;

                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F4}," +
                        "{10:F2},{11:F2},{12:F2},{13:F2},{14:F2},{15:F2},{16:F2},{17:F4}," +
                        "{18:F2},{19:F2},{20:F2},{21:F2},{22:F2},{23:F2},{24:F2},{25:F4},{26:F4},{27},{28:F4}",
                        load, sq,
                        hipLNom, hipLGrav, hipLBal, hipLFinal, hipLAct, hipLNomErr, hipLFinalErr, hipLDiag.ModeledDemand,
                        hipRNom, hipRGrav, hipRBal, hipRFinal, hipRAct, hipRNomErr, hipRFinalErr, hipRDiag.ModeledDemand,
                        abdAct, abdErr, thoAct, thoErr, trunkPitch, refTrunk, trunkErr,
                        comAp, copAp, contacts, saddleSep));

                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[STATIC_HOLD load={0:F0}kg sq={1:F2}] hipL_nomErr={2:F2} deg, hipL_finalErr={3:F2} deg, hipL_bal={4:F2} deg | trunkErr={5:F2} deg | contacts={6}",
                        load, sq, hipLNomErr, hipLFinalErr, hipLBal, trunkErr, contacts));
                }
            }

            File.WriteAllText(Path.Combine(dir, "hip-static-feasibility.csv"), csv.ToString());
            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 3: HIP TARGET-RATE CONSISTENCY (Balance OFF vs Balance ON)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_03_HIP_TARGET_RATE_CONSISTENCY()
        {
            foreach (bool balEnabled in new[] { false, true })
            {
                yield return LoadScene();
                PrepareManualRuntime(0f);
                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;

                adapter.BalanceCorrectionsEnabled = balEnabled;
                adapter.StartSquat();

                float maxRateGap = 0f;
                float prevFinalL = 0f;
                bool hasPrev = false;

                for (int tick = 0; tick < 100; tick++)
                {
                    AdvanceTicks(runtime, 1, _controller.LeftFootContact, _controller.RightFootContact);
                    adapter.TryGetTargetComposition("left_thigh", out var comp);
                    float finalDeg = TwistX(comp.Final);
                    float commandedRateDegS = powered.GetJoint("left_thigh").Diagnostic.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;

                    if (hasPrev)
                    {
                        float fdRateDegS = (finalDeg - prevFinalL) / (float)SimulationConstants.FixedDeltaTimeSeconds;
                        float gap = Mathf.Abs(commandedRateDegS - fdRateDegS);
                        if (gap > maxRateGap)
                            maxRateGap = gap;
                    }

                    prevFinalL = finalDeg;
                    hasPrev = true;
                }

                Debug.Log($"[HIP_RATE_CONSISTENCY bal={balEnabled}] MaxRateGap={maxRateGap:F3} deg/s");
            }

            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 4: COMPUTE HIP GRAVITATIONAL GENERALIZED MOMENT
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_04_HIP_GRAVITATIONAL_GENERALIZED_MOMENT()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,sq,tau_g_hip_l,tau_g_hip_r,hip_l_nom_err,hip_r_nom_err,hip_l_demand,hip_r_demand,trunk_err");

            // Physically distal (superior/supported) bodies relative to the hip joints:
            // Pelvis, Abdomen, Thorax, Head, Clavicles, Arms, Forearms, Hands, and Barbell
            string[] superiorSegments =
            {
                "pelvis", "abdomen", "thorax", "head",
                "left_clavicle", "right_clavicle",
                "left_arm", "right_arm",
                "left_forearm", "right_forearm",
                "left_hand", "right_hand"
            };

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float sq in StaticHoldPhases)
                {
                    yield return LoadScene();
                    PrepareManualRuntime(load);

                    FoundationRuntime runtime = _bootstrap.Runtime;
                    SquatPhysicalAdapter adapter = _controller.Adapter;
                    PoweredJointController powered = _rig.PoweredController;

                    adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);
                    AdvanceTicks(runtime, 150, _controller.LeftFootContact, _controller.RightFootContact);

                    var leftThighJoint = powered.GetJoint("left_thigh").Joint;
                    var rightThighJoint = powered.GetJoint("right_thigh").Joint;

                    Vector3 leftHipAnchorWorld = leftThighJoint.transform.TransformPoint(leftThighJoint.anchor);
                    Vector3 rightHipAnchorWorld = rightThighJoint.transform.TransformPoint(rightThighJoint.anchor);
                    Vector3 leftHipAxisWorld = leftThighJoint.transform.TransformDirection(leftThighJoint.axis);
                    Vector3 rightHipAxisWorld = rightThighJoint.transform.TransformDirection(rightThighJoint.axis);

                    // Compute gravitational moment on left and right hip
                    // Upper body + barbell is shared bilaterally across both hips (0.5 factor per hip)
                    float g = 9.81f;
                    float totalUpperMass = 0f;
                    Vector3 totalUpperComMoment = Vector3.zero;

                    foreach (string segName in superiorSegments)
                    {
                        if (_rig.Segments.TryGetValue(segName, out var seg) && seg.Body != null)
                        {
                            float m = seg.Body.mass;
                            Vector3 com = seg.Body.worldCenterOfMass;
                            totalUpperMass += m;
                            totalUpperComMoment += m * com;
                        }
                    }

                    if (load > 0f && _bar != null && _bar.Body != null && _controller.Saddle != null && _controller.Saddle.IsAttached)
                    {
                        float barMass = _bar.Body.mass;
                        Vector3 barCom = _bar.Body.worldCenterOfMass;
                        totalUpperMass += barMass;
                        totalUpperComMoment += barMass * barCom;
                    }

                    Vector3 combinedUpperCom = totalUpperComMoment / totalUpperMass;

                    // Moment: r x (m * g_vec)
                    // With g_vec = (0, -g, 0), tau_x = (r_z) * (totalUpperMass * g)
                    // Each hip supports half of the bilateral superior weight
                    float halfMass = 0.5f * totalUpperMass;

                    Vector3 rLeft = combinedUpperCom - leftHipAnchorWorld;
                    Vector3 rRight = combinedUpperCom - rightHipAnchorWorld;

                    Vector3 tauLeftVec = Vector3.Cross(rLeft, new Vector3(0f, -halfMass * g, 0f));
                    Vector3 tauRightVec = Vector3.Cross(rRight, new Vector3(0f, -halfMass * g, 0f));

                    float tauGLeft = Vector3.Dot(tauLeftVec, leftHipAxisWorld);
                    float tauGRight = Vector3.Dot(tauRightVec, rightHipAxisWorld);

                    adapter.TryGetTargetComposition("left_thigh", out var hipLC);
                    adapter.TryGetTargetComposition("right_thigh", out var hipRC);
                    var hipLDiag = powered.GetJoint("left_thigh").Diagnostic;
                    var hipRDiag = powered.GetJoint("right_thigh").Diagnostic;

                    float hipLErr = TwistX(hipLDiag.ActualRelative) - TwistX(hipLC.Nominal);
                    float hipRErr = TwistX(hipRDiag.ActualRelative) - TwistX(hipRC.Nominal);

                    Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                    Rigidbody thorax = _rig.Segments["thorax"].Body;
                    Vector3 trunkAxis = thorax.position - pelvis.position;
                    float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;
                    float trunkErr = trunkPitch - SampleReference(sq);

                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F4},{7:F4},{8:F2}",
                        load, sq, tauGLeft, tauGRight, hipLErr, hipRErr, hipLDiag.ModeledDemand, hipRDiag.ModeledDemand, trunkErr));

                    Debug.Log(string.Format(CultureInfo.InvariantCulture,
                        "[HIP_GRAVITY load={0:F0}kg sq={1:F2}] tau_g_L={2:F2} Nm, hipL_err={3:F2} deg, demand={4:F3}",
                        load, sq, tauGLeft, hipLErr, hipLDiag.ModeledDemand));
                }
            }

            File.WriteAllText(Path.Combine(dir, "hip-gravity-generalized-moment.csv"), csv.ToString());
            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 5: SPINE-HIP CROSS-COUPLING AND SENSITIVITY
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_05_SPINE_HIP_CROSS_COUPLING_AND_SENSITIVITY()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,sq,delta_tgt,hip_l_act,hip_r_act,abd_act,tho_act,trunk_pitch,com_ap,cop_ap");

            float[] deltaTgts = { -2f, -1f, 0f, 1f, 2f };
            float[] testPhases = { 0.25f, 0.55f };

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float sq in testPhases)
                {
                    float baseHipL = 0f, baseHipR = 0f, baseAbd = 0f, baseTho = 0f, baseTrunk = 0f;

                    foreach (float dDeg in deltaTgts)
                    {
                        yield return LoadScene();
                        PrepareManualRuntime(load);

                        FoundationRuntime runtime = _bootstrap.Runtime;
                        SquatPhysicalAdapter adapter = _controller.Adapter;
                        PoweredJointController powered = _rig.PoweredController;

                        adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);

                        // If non-zero delta, apply hip target offset diagnostic
                        if (Mathf.Abs(dDeg) > 1e-4f)
                        {
                            adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Hip, dDeg);
                        }

                        AdvanceTicks(runtime, 150, _controller.LeftFootContact, _controller.RightFootContact);

                        float hipLAct = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative);
                        float hipRAct = TwistX(powered.GetJoint("right_thigh").Diagnostic.ActualRelative);
                        float abdAct = TwistX(powered.GetJoint("abdomen").Diagnostic.ActualRelative);
                        float thoAct = TwistX(powered.GetJoint("thorax").Diagnostic.ActualRelative);

                        Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                        Rigidbody thorax = _rig.Segments["thorax"].Body;
                        Vector3 trunkAxis = thorax.position - pelvis.position;
                        float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;

                        float comAp = adapter.Balance.SystemCom.z;
                        float copAp = adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter;

                        if (Mathf.Abs(dDeg) < 1e-4f)
                        {
                            baseHipL = hipLAct; baseHipR = hipRAct; baseAbd = abdAct; baseTho = thoAct; baseTrunk = trunkPitch;
                        }

                        csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0:F0},{1:F2},{2:F1},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F4},{9:F4}",
                            load, sq, dDeg, hipLAct, hipRAct, abdAct, thoAct, trunkPitch, comAp, copAp));
                    }
                }
            }

            File.WriteAllText(Path.Combine(dir, "hip-cross-coupling.csv"), csv.ToString());
            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 6: DYNAMIC PHASE-RATE COMPARISON (100% vs 50%)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_06_PHASE_RATE_DEPENDENCE_DISCRIMINATOR()
        {
            yield return LoadScene();

            foreach (float rateScale in new[] { 1.0f, 0.5f })
            {
                yield return LoadScene();
                PrepareManualRuntime(25f);
                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;

                adapter.PhaseRate *= rateScale;
                AdvanceTicks(runtime, 30, _controller.LeftFootContact, _controller.RightFootContact);
                adapter.StartSquat();

                float peakHipErr = 0f;
                int ticks = Mathf.CeilToInt((8.0f / rateScale) / (float)SimulationConstants.FixedDeltaTimeSeconds);

                for (int t = 0; t < ticks && adapter.State != SquatState.COMPLETE; t++)
                {
                    AdvanceTicks(runtime, 1, _controller.LeftFootContact, _controller.RightFootContact);
                    adapter.TryGetTargetComposition("left_thigh", out var comp);
                    float err = TwistX(powered.GetJoint("left_thigh").Diagnostic.ActualRelative) - TwistX(comp.Nominal);
                    if (Mathf.Abs(err) > Mathf.Abs(peakHipErr))
                        peakHipErr = err;
                }

                Debug.Log($"[PHASE_RATE rateScale={rateScale:F1}] PeakHipError={peakHipErr:F2} deg");
            }

            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 7: VISUAL CAPTURE CURRENT PRODUCTION (0 kg and 25 kg)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H18_07_VISUAL_CAPTURE_CURRENT_PRODUCTION()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.Log("[H18_07] Headless mode, skipping visual capture.");
                yield break;
            }

            yield return LoadScene();
            string evidenceDir = Path.Combine(Directory.GetCurrentDirectory(), EvidenceDirectory);
            Directory.CreateDirectory(evidenceDir);

            foreach (float load in new[] { 0f, 25f })
            {
                yield return LoadScene();
                PrepareManualRuntime(load);
                _rig.enabled = true;
                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
                if (camera == null) yield break;

                AdvanceTicks(runtime, 30, _controller.LeftFootContact, _controller.RightFootContact);
                adapter.StartSquat();

                float[] captureSqs = { 0.00f, 0.25f, 0.55f, 0.80f, 1.00f };
                int captureIdx = 0;

                int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, _controller.LeftFootContact, _controller.RightFootContact);

                    if (captureIdx < captureSqs.Length && adapter.Sq >= captureSqs[captureIdx] - 0.02f)
                    {
                        yield return null;
                        string tag = string.Format(CultureInfo.InvariantCulture, "h18_{0:F0}kg_sq{1:F2}", load, captureSqs[captureIdx]);
                        CaptureImage(camera, Path.Combine(evidenceDir, tag + "_side.png"), new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                        CaptureImage(camera, Path.Combine(evidenceDir, tag + "_oblique.png"), new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                        captureIdx++;
                    }
                }
            }

            yield return null;
        }

        private static void CaptureImage(Camera camera, string path, Vector3 pos, Vector3 lookAt)
        {
            camera.transform.position = pos;
            camera.transform.LookAt(lookAt);
            RenderTexture rt = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevRt = RenderTexture.active;
            RenderTexture prevCam = camera.targetTexture;
            Texture2D tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);

            try
            {
                camera.targetTexture = rt;
                camera.Render();
                RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"CaptureImage failed: {ex.Message}");
            }
            finally
            {
                camera.targetTexture = prevCam;
                RenderTexture.active = prevRt;
                RenderTexture.ReleaseTemporary(rt);
                UnityEngine.Object.DestroyImmediate(tex);
            }
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

        private static float SampleReference(float sq)
        {
            if (sq <= ReferencePhases[0])
                return ReferenceWorldTrunkPitchDeg[0];
            for (int i = 1; i < ReferencePhases.Length; i++)
            {
                if (sq > ReferencePhases[i])
                    continue;
                float t = (sq - ReferencePhases[i - 1]) /
                          (ReferencePhases[i] - ReferencePhases[i - 1]);
                return Mathf.Lerp(
                    ReferenceWorldTrunkPitchDeg[i - 1],
                    ReferenceWorldTrunkPitchDeg[i],
                    t);
            }
            return ReferenceWorldTrunkPitchDeg[ReferenceWorldTrunkPitchDeg.Length - 1];
        }
    }
}
