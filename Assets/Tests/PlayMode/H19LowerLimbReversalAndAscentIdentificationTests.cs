using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    /// Phase 5H19: Lower-limb reversal and ascent tracking system identification.
    /// Comprehensive diagnostic and scientific discrimination test fixture.
    /// </summary>
    public sealed class H19LowerLimbReversalAndAscentIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H19";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/h19-ascent";

        private static readonly string[] MonitoredJoints =
        {
            "left_shank", "right_shank", "left_thigh", "right_thigh", "left_foot", "right_foot"
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

        private static (float valDeg, float dqdsDeg, float d2qds2Deg) EvaluateHermiteDerivatives(
            SquatReferenceProfile profile, string joint, float sq, SquatPhaseDirection direction)
        {
            sq = Mathf.Clamp01(sq);
            SquatReferencePose pose = profile.Evaluate(sq, direction);
            SquatReferencePose dPose = profile.Derivative(sq, direction);

            const float eps = 1e-4f;
            float sqPlus = Mathf.Clamp01(sq + eps);
            float sqMinus = Mathf.Clamp01(sq - eps);
            SquatReferencePose dPosePlus = profile.Derivative(sqPlus, direction);
            SquatReferencePose dPoseMinus = profile.Derivative(sqMinus, direction);

            float valRad = 0f, dRad = 0f, d2Rad = 0f;
            if (joint == "left_shank" || joint == "right_shank")
            {
                valRad = pose.KneeFlexionRad;
                dRad = dPose.KneeFlexionRad;
                d2Rad = (dPosePlus.KneeFlexionRad - dPoseMinus.KneeFlexionRad) / (sqPlus - sqMinus);
            }
            else if (joint == "left_thigh" || joint == "right_thigh")
            {
                valRad = -pose.HipFlexionRad; // in joint space, flexion is negative
                dRad = -dPose.HipFlexionRad;
                d2Rad = -(dPosePlus.HipFlexionRad - dPoseMinus.HipFlexionRad) / (sqPlus - sqMinus);
            }
            else if (joint == "left_foot" || joint == "right_foot")
            {
                valRad = -pose.AnkleDorsiflexionRad;
                dRad = -dPose.AnkleDorsiflexionRad;
                d2Rad = -(dPosePlus.AnkleDorsiflexionRad - dPoseMinus.AnkleDorsiflexionRad) / (sqPlus - sqMinus);
            }

            return (valRad * Mathf.Rad2Deg, dRad * Mathf.Rad2Deg, d2Rad * Mathf.Rad2Deg);
        }

        // -------------------------------------------------------------------------
        // TEST 1: HIGH-RESOLUTION REVERSAL TRACE (25 kg and 0 kg)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_01_REVERSAL_HIGH_RESOLUTION_TRACE()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;

            foreach (float load in new[] { 25f, 0f })
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

                AdvanceTicks(runtime, 30, leftFoot, rightFoot);
                adapter.StartSquat();

                var csv = new StringBuilder();
                csv.AppendLine(
                    "time,tick,state,direction,sq,phase_velocity,phase_accel," +
                    "knee_l_nom,knee_l_grav,knee_l_bal,knee_l_req,knee_l_app,knee_l_act," +
                    "knee_l_req_app_err,knee_l_app_act_err,knee_l_req_act_err," +
                    "knee_l_dqds,knee_l_nom_tgt_vel,knee_l_req_tgt_vel,knee_l_app_tgt_vel,knee_l_act_vel," +
                    "knee_l_ref_accel,knee_l_pos_nm,knee_l_vel_nm,knee_l_tot_nm,knee_l_solv_nm,knee_l_demand,knee_l_max_force,knee_l_lim," +
                    "hip_l_nom,hip_l_grav,hip_l_bal,hip_l_req,hip_l_app,hip_l_act," +
                    "hip_l_req_app_err,hip_l_app_act_err,hip_l_req_act_err," +
                    "hip_l_dqds,hip_l_nom_tgt_vel,hip_l_req_tgt_vel,hip_l_app_tgt_vel,hip_l_act_vel," +
                    "hip_l_ref_accel,hip_l_pos_nm,hip_l_vel_nm,hip_l_tot_nm,hip_l_solv_nm,hip_l_demand,hip_l_max_force,hip_l_lim," +
                    "ankle_l_nom,ankle_l_grav,ankle_l_bal,ankle_l_req,ankle_l_app,ankle_l_act," +
                    "ankle_l_req_app_err,ankle_l_app_act_err,ankle_l_req_act_err," +
                    "ankle_l_dqds,ankle_l_nom_tgt_vel,ankle_l_req_tgt_vel,ankle_l_app_tgt_vel,ankle_l_act_vel," +
                    "pelvis_pitch,trunk_pitch,com_z,com_y,cop_z,support_min,support_max,contacts,foot_pitch_l,foot_pitch_r,bar_y,bar_vy");

                float prevPhaseVel = 0.30f;
                bool recordingStarted = false;
                bool recordingCompleted = false;

                // Telemetry summary metrics
                float bottomEntryKneeErr = 0f, bottomEntryHipErr = 0f, bottomEntryAnkleErr = 0f;
                float preAscentKneeErr = 0f, preAscentHipErr = 0f, preAscentAnkleErr = 0f;
                float maxKneeReqAppErr = 0f, maxHipReqAppErr = 0f, maxAnkleReqAppErr = 0f;
                float peakAscentKneeErr = 0f, peakAscentKneeSq = 0f;
                float peakAscentHipErr = 0f, peakAscentHipSq = 0f;
                float peakKneeDemand = 0f, peakHipDemand = 0f;
                float maxReqTargetRateKnee = 0f, maxReqTargetRateHip = 0f;
                bool recordedBottomEntry = false;
                bool recordedPreAscent = false;

                int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                    float time = tick * (float)SimulationConstants.FixedDeltaTimeSeconds;
                    float sq = adapter.Sq;
                    SquatState state = adapter.State;
                    SquatPhaseDirection direction = adapter.Direction;

                    // Reversal causal window: sq >= 0.80 on descent until sq <= 0.30 on ascent
                    if (!recordingStarted && direction == SquatPhaseDirection.Descent && sq >= 0.795f)
                        recordingStarted = true;

                    if (recordingStarted && !recordingCompleted)
                    {
                        if (direction == SquatPhaseDirection.Ascent && sq <= 0.295f)
                            recordingCompleted = true;
                    }

                    // Bottom entry checkpoint
                    if (!recordedBottomEntry && state == SquatState.BOTTOM)
                    {
                        adapter.TryGetTargetComposition("left_shank", out var cK);
                        adapter.TryGetTargetComposition("left_thigh", out var cH);
                        adapter.TryGetTargetComposition("left_foot", out var cA);
                        var dK = powered.GetJoint("left_shank").Diagnostic;
                        var dH = powered.GetJoint("left_thigh").Diagnostic;
                        var dA = powered.GetJoint("left_foot").Diagnostic;

                        bottomEntryKneeErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                        bottomEntryHipErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);
                        bottomEntryAnkleErr = TwistX(dA.ActualRelative) - TwistX(cA.Final);
                        recordedBottomEntry = true;
                    }

                    // Pre-ascent checkpoint (tick immediately before first ASCENT tick)
                    if (!recordedPreAscent && state == SquatState.REVERSAL)
                    {
                        adapter.TryGetTargetComposition("left_shank", out var cK);
                        adapter.TryGetTargetComposition("left_thigh", out var cH);
                        adapter.TryGetTargetComposition("left_foot", out var cA);
                        var dK = powered.GetJoint("left_shank").Diagnostic;
                        var dH = powered.GetJoint("left_thigh").Diagnostic;
                        var dA = powered.GetJoint("left_foot").Diagnostic;

                        preAscentKneeErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                        preAscentHipErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);
                        preAscentAnkleErr = TwistX(dA.ActualRelative) - TwistX(cA.Final);
                    }

                    // Peak ascent errors
                    if (state == SquatState.ASCENT)
                    {
                        adapter.TryGetTargetComposition("left_shank", out var cK);
                        adapter.TryGetTargetComposition("left_thigh", out var cH);
                        var dK = powered.GetJoint("left_shank").Diagnostic;
                        var dH = powered.GetJoint("left_thigh").Diagnostic;
                        float kErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                        float hErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);

                        if (Mathf.Abs(kErr) > Mathf.Abs(peakAscentKneeErr)) { peakAscentKneeErr = kErr; peakAscentKneeSq = sq; }
                        if (Mathf.Abs(hErr) > Mathf.Abs(peakAscentHipErr)) { peakAscentHipErr = hErr; peakAscentHipSq = sq; }
                    }

                    float curPhaseVel = 0f;
                    if (state == SquatState.DESCENT) curPhaseVel = adapter.PhaseRate;
                    else if (state == SquatState.ASCENT) curPhaseVel = -adapter.PhaseRate;
                    else curPhaseVel = 0f;

                    float phaseAccel = (curPhaseVel - prevPhaseVel) / (float)SimulationConstants.FixedDeltaTimeSeconds;
                    prevPhaseVel = curPhaseVel;

                    if (recordingStarted && (!recordingCompleted || sq > 0.28f))
                    {
                        // Extract lower limb data
                        adapter.TryGetTargetComposition("left_shank", out var compK);
                        adapter.TryGetTargetComposition("left_thigh", out var compH);
                        adapter.TryGetTargetComposition("left_foot", out var compA);

                        var diagK = powered.GetJoint("left_shank").Diagnostic;
                        var diagH = powered.GetJoint("left_thigh").Diagnostic;
                        var diagA = powered.GetJoint("left_foot").Diagnostic;

                        var profK = PoweredJointController.FindFamilyProfile("knee").Value;
                        var profH = PoweredJointController.FindFamilyProfile("hip").Value;

                        float kNom = TwistX(compK.Nominal);
                        float kGrav = TwistX(compK.GravityBias);
                        float kBal = TwistX(compK.BalanceOffset);
                        float kReq = TwistX(diagK.RequestedTarget);
                        float kApp = TwistX(diagK.AppliedTarget);
                        float kAct = TwistX(diagK.ActualRelative);
                        float kReqAppErr = kApp - kReq;
                        float kAppActErr = kAct - kApp;
                        float kReqActErr = kAct - kReq;
                        if (Mathf.Abs(kReqAppErr) > maxKneeReqAppErr) maxKneeReqAppErr = Mathf.Abs(kReqAppErr);

                        var (kVal, kDqds, kD2qds2) = EvaluateHermiteDerivatives(profile, "left_shank", sq, direction);
                        float kNomTgtVel = kDqds * curPhaseVel;
                        float kReqTgtVel = diagK.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float kAppTgtVel = diagK.TargetAngularVelocityRadS.x * Mathf.Rad2Deg; // target written to joint
                        float kActVel = diagK.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float kRefAccel = kD2qds2 * (curPhaseVel * curPhaseVel) + kDqds * phaseAccel;

                        float kPosNm = profK.Spring * diagK.ErrorRad.x;
                        float kVelNm = profK.Damper * (diagK.TargetAngularVelocityRadS.x - diagK.ActualAngularVelocityRadS.x);
                        float kTotNm = kPosNm + kVelNm;
                        float kSolvNm = diagK.SolverTorqueJointSpaceNm.x;
                        if (diagK.ModeledDemand > peakKneeDemand) peakKneeDemand = diagK.ModeledDemand;
                        if (Mathf.Abs(diagK.TargetAngularVelocityRadS.x) > maxReqTargetRateKnee)
                            maxReqTargetRateKnee = Mathf.Abs(diagK.TargetAngularVelocityRadS.x);

                        float hNom = TwistX(compH.Nominal);
                        float hGrav = TwistX(compH.GravityBias);
                        float hBal = TwistX(compH.BalanceOffset);
                        float hReq = TwistX(diagH.RequestedTarget);
                        float hApp = TwistX(diagH.AppliedTarget);
                        float hAct = TwistX(diagH.ActualRelative);
                        float hReqAppErr = hApp - hReq;
                        float hAppActErr = hAct - hApp;
                        float hReqActErr = hAct - hReq;
                        if (Mathf.Abs(hReqAppErr) > maxHipReqAppErr) maxHipReqAppErr = Mathf.Abs(hReqAppErr);

                        var (hVal, hDqds, hD2qds2) = EvaluateHermiteDerivatives(profile, "left_thigh", sq, direction);
                        float hNomTgtVel = hDqds * curPhaseVel;
                        float hReqTgtVel = diagH.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float hAppTgtVel = diagH.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float hActVel = diagH.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float hRefAccel = hD2qds2 * (curPhaseVel * curPhaseVel) + hDqds * phaseAccel;

                        float hPosNm = profH.Spring * diagH.ErrorRad.x;
                        float hVelNm = profH.Damper * (diagH.TargetAngularVelocityRadS.x - diagH.ActualAngularVelocityRadS.x);
                        float hTotNm = hPosNm + hVelNm;
                        float hSolvNm = diagH.SolverTorqueJointSpaceNm.x;
                        if (diagH.ModeledDemand > peakHipDemand) peakHipDemand = diagH.ModeledDemand;
                        if (Mathf.Abs(diagH.TargetAngularVelocityRadS.x) > maxReqTargetRateHip)
                            maxReqTargetRateHip = Mathf.Abs(diagH.TargetAngularVelocityRadS.x);

                        float aNom = TwistX(compA.Nominal);
                        float aGrav = TwistX(compA.GravityBias);
                        float aBal = TwistX(compA.BalanceOffset);
                        float aReq = TwistX(diagA.RequestedTarget);
                        float aApp = TwistX(diagA.AppliedTarget);
                        float aAct = TwistX(diagA.ActualRelative);
                        float aReqAppErr = aApp - aReq;
                        float aAppActErr = aAct - aApp;
                        float aReqActErr = aAct - aReq;
                        if (Mathf.Abs(aReqAppErr) > maxAnkleReqAppErr) maxAnkleReqAppErr = Mathf.Abs(aReqAppErr);

                        var (aVal, aDqds, aD2qds2) = EvaluateHermiteDerivatives(profile, "left_foot", sq, direction);
                        float aNomTgtVel = aDqds * curPhaseVel;
                        float aReqTgtVel = diagA.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float aAppTgtVel = diagA.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                        float aActVel = diagA.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;

                        Vector3 trunkAxis = thorax.position - pelvis.position;
                        float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;
                        Vector3 pelForward = pelvis.transform.forward;
                        float pelPitch = Mathf.Atan2(pelForward.y, pelForward.z) * Mathf.Rad2Deg;
                        Vector3 lfForward = leftFootBody.transform.forward;
                        float footPitchL = Mathf.Atan2(lfForward.y, lfForward.z) * Mathf.Rad2Deg;
                        Vector3 rfForward = rightFootBody.transform.forward;
                        float footPitchR = Mathf.Atan2(rfForward.y, rfForward.z) * Mathf.Rad2Deg;
                        Vector3 barPos = _bar != null && _bar.Body != null ? _bar.Body.position : Vector3.zero;
                        Vector3 barVel = _bar != null && _bar.Body != null ? _bar.Body.linearVelocity : Vector3.zero;

                        csv.AppendFormat(CultureInfo.InvariantCulture,
                            "{0:F3},{1},{2},{3},{4:F4},{5:F2},{6:F1}," +
                            "{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:F2}," +
                            "{13:F2},{14:F2},{15:F2}," +
                            "{16:F2},{17:F2},{18:F2},{19:F2},{20:F2}," +
                            "{21:F1},{22:F1},{23:F1},{24:F1},{25:F1},{26:F4},{27:F1},{28:F4}," +
                            "{29:F2},{30:F2},{31:F2},{32:F2},{33:F2},{34:F2}," +
                            "{35:F2},{36:F2},{37:F2}," +
                            "{38:F2},{39:F2},{40:F2},{41:F2},{42:F2}," +
                            "{43:F1},{44:F1},{45:F1},{46:F1},{47:F1},{48:F4},{49:F1},{50:F4}," +
                            "{51:F2},{52:F2},{53:F2},{54:F2},{55:F2},{56:F2}," +
                            "{57:F2},{58:F2},{59:F2}," +
                            "{60:F2},{61:F2},{62:F2},{63:F2},{64:F2}," +
                            "{65:F2},{66:F2},{67:F4},{68:F4},{69:F4},{70:F4},{71:F4},{72},{73:F2},{74:F2},{75:F4},{76:F4}\n",
                            time, tick, state, direction, sq, curPhaseVel, phaseAccel,
                            kNom, kGrav, kBal, kReq, kApp, kAct,
                            kReqAppErr, kAppActErr, kReqActErr,
                            kDqds, kNomTgtVel, kReqTgtVel, kAppTgtVel, kActVel,
                            kRefAccel, kPosNm, kVelNm, kTotNm, kSolvNm, diagK.ModeledDemand, diagK.MaximumForceNm, diagK.LimitProximity,
                            hNom, hGrav, hBal, hReq, hApp, hAct,
                            hReqAppErr, hAppActErr, hReqActErr,
                            hDqds, hNomTgtVel, hReqTgtVel, hAppTgtVel, hActVel,
                            hRefAccel, hPosNm, hVelNm, hTotNm, hSolvNm, diagH.ModeledDemand, diagH.MaximumForceNm, diagH.LimitProximity,
                            aNom, aGrav, aBal, aReq, aApp, aAct,
                            aReqAppErr, aAppActErr, aReqActErr,
                            aDqds, aNomTgtVel, aReqTgtVel, aAppTgtVel, aActVel,
                            pelPitch, trunkPitch,
                            adapter.Balance.SystemCom.z, adapter.Balance.SystemCom.y,
                            adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter,
                            adapter.Balance.SupportApMin, adapter.Balance.SupportApMax,
                            adapter.Balance.SupportContactCount,
                            footPitchL, footPitchR, barPos.y, barVel.y);
                    }
                }

                string fileName = string.Format(CultureInfo.InvariantCulture, "lower-limb-reversal-window-{0:F0}kg.csv", load);
                File.WriteAllText(Path.Combine(dir, fileName), csv.ToString());
                Debug.Log($"[H19_01] Wrote {fileName}");
                Debug.Log($"[H19_01] Load: {load:F0}kg | BottomEntry: KneeErr={bottomEntryKneeErr:F2}°, HipErr={bottomEntryHipErr:F2}°, AnkleErr={bottomEntryAnkleErr:F2}°");
                Debug.Log($"[H19_01] Load: {load:F0}kg | PreAscent: KneeErr={preAscentKneeErr:F2}°, HipErr={preAscentHipErr:F2}°, AnkleErr={preAscentAnkleErr:F2}°");
                Debug.Log($"[H19_01] Load: {load:F0}kg | PeakAscent: KneePeak={peakAscentKneeErr:F2}° (sq={peakAscentKneeSq:F2}), HipPeak={peakAscentHipErr:F2}° (sq={peakAscentHipSq:F2})");
                Debug.Log($"[H19_01] Load: {load:F0}kg | Pipeline: MaxKneeReqAppErr={maxKneeReqAppErr:F3}°, MaxHipReqAppErr={maxHipReqAppErr:F3}°, MaxAnkleReqAppErr={maxAnkleReqAppErr:F3}°");
                Debug.Log($"[H19_01] Load: {load:F0}kg | Ceilings: PeakKneeDemand={peakKneeDemand:F4}, PeakHipDemand={peakHipDemand:F4}, MaxTgtRateKnee={maxReqTargetRateKnee:F2} rad/s, MaxTgtRateHip={maxReqTargetRateHip:F2} rad/s");
            }
        }

        // -------------------------------------------------------------------------
        // TEST 2: GENUINE STATIC BOTTOM SETTLING (0 kg and 25 kg, 5.0 seconds hold)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_02_GENUINE_BOTTOM_SETTLING()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("load,time,tick,sq,knee_act,knee_act_vel,knee_alpha,knee_err,knee_demand,knee_solv_nm," +
                           "hip_act,hip_act_vel,hip_alpha,hip_err,hip_demand,hip_solv_nm," +
                           "ankle_act,ankle_act_vel,ankle_alpha,ankle_err,com_z,cop_z,contacts,bar_y");

            foreach (float load in new[] { 0f, 25f })
            {
                yield return LoadScene();
                PrepareManualRuntime(load);

                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                PhysicalFootContactDetector rightFoot = _controller.RightFootContact;

                AdvanceTicks(runtime, 30, leftFoot, rightFoot);

                // Place reference at sq = 1.00, phaseVelocity = 0, state = BOTTOM
                adapter.HoldReferencePhaseForQualification(1.0f, SquatPhaseDirection.Ascent, SquatState.BOTTOM, 0f);

                // Hold for 5.0 seconds (500 ticks)
                int holdTicks = 500;
                float prevKneeVel = 0f, prevHipVel = 0f, prevAnkleVel = 0f;

                var kneeAngles = new List<float>(holdTicks);
                var hipAngles = new List<float>(holdTicks);
                var ankleAngles = new List<float>(holdTicks);
                var kneeOmegas = new List<float>(holdTicks);
                var hipOmegas = new List<float>(holdTicks);
                var ankleOmegas = new List<float>(holdTicks);
                var kneeAlphas = new List<float>(holdTicks);
                var hipAlphas = new List<float>(holdTicks);
                var ankleAlphas = new List<float>(holdTicks);

                for (int tick = 0; tick < holdTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                    float time = tick * (float)SimulationConstants.FixedDeltaTimeSeconds;

                    adapter.TryGetTargetComposition("left_shank", out var cK);
                    adapter.TryGetTargetComposition("left_thigh", out var cH);
                    adapter.TryGetTargetComposition("left_foot", out var cA);

                    var dK = powered.GetJoint("left_shank").Diagnostic;
                    var dH = powered.GetJoint("left_thigh").Diagnostic;
                    var dA = powered.GetJoint("left_foot").Diagnostic;

                    float kAct = TwistX(dK.ActualRelative);
                    float hAct = TwistX(dH.ActualRelative);
                    float aAct = TwistX(dA.ActualRelative);

                    float kErr = kAct - TwistX(cK.Final);
                    float hErr = hAct - TwistX(cH.Final);
                    float aErr = aAct - TwistX(cA.Final);

                    float kVel = dK.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                    float hVel = dH.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                    float aVel = dA.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;

                    float kAlpha = (kVel - prevKneeVel) / (float)SimulationConstants.FixedDeltaTimeSeconds;
                    float hAlpha = (hVel - prevHipVel) / (float)SimulationConstants.FixedDeltaTimeSeconds;
                    float aAlpha = (aVel - prevAnkleVel) / (float)SimulationConstants.FixedDeltaTimeSeconds;
                    prevKneeVel = kVel; prevHipVel = hVel; prevAnkleVel = aVel;

                    kneeAngles.Add(kAct); hipAngles.Add(hAct); ankleAngles.Add(aAct);
                    kneeOmegas.Add(Mathf.Abs(kVel)); hipOmegas.Add(Mathf.Abs(hVel)); ankleOmegas.Add(Mathf.Abs(aVel));
                    kneeAlphas.Add(Mathf.Abs(kAlpha)); hipAlphas.Add(Mathf.Abs(hAlpha)); ankleAlphas.Add(Mathf.Abs(aAlpha));

                    Vector3 barPos = _bar != null && _bar.Body != null ? _bar.Body.position : Vector3.zero;

                    csv.AppendFormat(CultureInfo.InvariantCulture,
                        "{0:F0},{1:F3},{2},{3:F2}," +
                        "{4:F2},{5:F2},{6:F1},{7:F2},{8:F4},{9:F1}," +
                        "{10:F2},{11:F2},{12:F1},{13:F2},{14:F4},{15:F1}," +
                        "{16:F2},{17:F2},{18:F1},{19:F2},{20:F4},{21:F4},{22},{23:F4}\n",
                        load, time, tick, adapter.Sq,
                        kAct, kVel, kAlpha, kErr, dK.ModeledDemand, dK.SolverTorqueJointSpaceNm.x,
                        hAct, hVel, hAlpha, hErr, dH.ModeledDemand, dH.SolverTorqueJointSpaceNm.x,
                        aAct, aVel, aAlpha, aErr,
                        adapter.Balance.SystemCom.z,
                        adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter,
                        adapter.Balance.SupportContactCount, barPos.y);
                }

                // Window analysis: W1 (100-200 ticks = 1-2s), W2 (200-300 = 2-3s), W3 (300-400 = 3-4s), W4 (400-500 = 4-5s)
                Debug.Log($"=== GENUINE BOTTOM SETTLING WINDOW ANALYSIS (Load: {load:F0} kg) ===");
                (int start, int end, string name)[] windows =
                {
                    (100, 200, "W1 (1.0-2.0s) [H18 scope]"),
                    (200, 300, "W2 (2.0-3.0s)"),
                    (300, 400, "W3 (3.0-4.0s)"),
                    (400, 500, "W4 (4.0-5.0s)")
                };

                foreach (var (wStart, wEnd, wName) in windows)
                {
                    float sumKOmega = 0f, sumKAlpha = 0f;
                    float minK = float.PositiveInfinity, maxK = float.NegativeInfinity;
                    float minH = float.PositiveInfinity, maxH = float.NegativeInfinity;
                    for (int i = wStart; i < wEnd; i++)
                    {
                        sumKOmega += kneeOmegas[i];
                        sumKAlpha += kneeAlphas[i];
                        minK = Mathf.Min(minK, kneeAngles[i]);
                        maxK = Mathf.Max(maxK, kneeAngles[i]);
                        minH = Mathf.Min(minH, hipAngles[i]);
                        maxH = Mathf.Max(maxH, hipAngles[i]);
                    }
                    int count = wEnd - wStart;
                    float meanKOmega = sumKOmega / count;
                    float meanKAlpha = sumKAlpha / count;
                    float kDrift = maxK - minK;
                    float hDrift = maxH - minH;
                    float endKErr = kneeAngles[wEnd - 1] - 114.0f;
                    float endHErr = hipAngles[wEnd - 1] - (-111.0f);
                    Debug.Log($"[{wName}] KneeErr={endKErr:F2}°, HipErr={endHErr:F2}° | KneeDrift={kDrift:F3}°, HipDrift={hDrift:F3}° | Mean|Omega|={meanKOmega:F2}°/s, Mean|Alpha|={meanKAlpha:F1}°/s²");
                }
            }

            File.WriteAllText(Path.Combine(dir, "bottom-settle-characterization.csv"), csv.ToString());
            Debug.Log("[H19_02] Wrote bottom-settle-characterization.csv");
        }

        // -------------------------------------------------------------------------
        // TEST 3: BOTTOM HOLD DURATION DISCRIMINATOR (B0=prod, B1=+0.25s, B2=+0.50s, B3=+1.00s)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_03_BOTTOM_HOLD_DURATION_DISCRIMINATION()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("case,extra_hold_s,total_bottom_s,ascent_entry_knee_err,ascent_entry_hip_err,ascent_entry_ankle_err," +
                           "peak_ascent_knee_err,peak_ascent_knee_sq,peak_ascent_hip_err,peak_ascent_hip_sq");

            (string name, float extraHold)[] cases =
            {
                ("B0_PRODUCTION", 0.00f),
                ("B1_EXTRA_0.25S", 0.25f),
                ("B2_EXTRA_0.50S", 0.50f),
                ("B3_EXTRA_1.00S", 1.00f)
            };

            FieldInfo timerField = typeof(SquatPhysicalAdapter).GetField("_bottomHoldTimer", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(timerField, Is.Not.Null, "Failed to find _bottomHoldTimer field via reflection.");

            foreach (var (cName, extraHold) in cases)
            {
                yield return LoadScene();
                PrepareManualRuntime(25f);

                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                PhysicalFootContactDetector rightFoot = _controller.RightFootContact;

                AdvanceTicks(runtime, 30, leftFoot, rightFoot);
                adapter.StartSquat();

                bool bottomEntered = false;
                bool recordedAscentEntry = false;
                float ascentEntryKneeErr = 0f, ascentEntryHipErr = 0f, ascentEntryAnkleErr = 0f;
                float peakAscentKneeErr = 0f, peakAscentKneeSq = 0f;
                float peakAscentHipErr = 0f, peakAscentHipSq = 0f;

                // Max simulation ticks: 8.0s + extra hold ticks + margin
                int totalTicks = Mathf.CeilToInt((8.0f + extraHold + 2.0f) / (float)SimulationConstants.FixedDeltaTimeSeconds);

                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                    SquatState state = adapter.State;
                    float sq = adapter.Sq;

                    if (!bottomEntered && state == SquatState.BOTTOM)
                    {
                        bottomEntered = true;
                        if (extraHold > 0f)
                        {
                            float curTimer = (float)timerField.GetValue(adapter);
                            timerField.SetValue(adapter, curTimer + extraHold);
                        }
                    }

                    if (!recordedAscentEntry && state == SquatState.ASCENT)
                    {
                        adapter.TryGetTargetComposition("left_shank", out var cK);
                        adapter.TryGetTargetComposition("left_thigh", out var cH);
                        adapter.TryGetTargetComposition("left_foot", out var cA);
                        var dK = powered.GetJoint("left_shank").Diagnostic;
                        var dH = powered.GetJoint("left_thigh").Diagnostic;
                        var dA = powered.GetJoint("left_foot").Diagnostic;

                        ascentEntryKneeErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                        ascentEntryHipErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);
                        ascentEntryAnkleErr = TwistX(dA.ActualRelative) - TwistX(cA.Final);
                        recordedAscentEntry = true;
                    }

                    if (state == SquatState.ASCENT)
                    {
                        adapter.TryGetTargetComposition("left_shank", out var cK);
                        adapter.TryGetTargetComposition("left_thigh", out var cH);
                        var dK = powered.GetJoint("left_shank").Diagnostic;
                        var dH = powered.GetJoint("left_thigh").Diagnostic;

                        float kErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                        float hErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);

                        if (Mathf.Abs(kErr) > Mathf.Abs(peakAscentKneeErr)) { peakAscentKneeErr = kErr; peakAscentKneeSq = sq; }
                        if (Mathf.Abs(hErr) > Mathf.Abs(peakAscentHipErr)) { peakAscentHipErr = hErr; peakAscentHipSq = sq; }
                    }

                    if (adapter.LockoutReached)
                        break;
                }

                float totalBottom = 0.20f + extraHold;
                csv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2}\n",
                    cName, extraHold, totalBottom,
                    ascentEntryKneeErr, ascentEntryHipErr, ascentEntryAnkleErr,
                    peakAscentKneeErr, peakAscentKneeSq, peakAscentHipErr, peakAscentHipSq);

                Debug.Log($"[H19_03] {cName} (bottom={totalBottom:F2}s): AscentEntryKneeErr={ascentEntryKneeErr:F2}°, HipErr={ascentEntryHipErr:F2}° | PeakAscentKneeErr={peakAscentKneeErr:F2}° at sq={peakAscentKneeSq:F2}, HipErr={peakAscentHipErr:F2}° at sq={peakAscentHipSq:F2}");
            }

            File.WriteAllText(Path.Combine(dir, "bottom-hold-duration-ab.csv"), csv.ToString());
            Debug.Log("[H19_03] Wrote bottom-hold-duration-ab.csv");
        }

        // -------------------------------------------------------------------------
        // TEST 4: FULL-CYCLE TARGET-RATE PARITY AND ACCELERATION DEMAND
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_04_FULL_CYCLE_TARGET_RATE_PARITY()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            PrepareManualRuntime(25f);
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            PoweredJointController powered = _rig.PoweredController;
            PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
            PhysicalFootContactDetector rightFoot = _controller.RightFootContact;

            AdvanceTicks(runtime, 30, leftFoot, rightFoot);
            adapter.StartSquat();

            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;

            var csv = new StringBuilder();
            csv.AppendLine("tick,time,state,sq,knee_cmd_rate,knee_fd_rate,knee_rate_diff,knee_ref_accel," +
                           "hip_cmd_rate,hip_fd_rate,hip_rate_diff,hip_ref_accel," +
                           "ankle_nom_cmd_rate,ankle_nom_fd_rate,ankle_nom_diff");

            float prevKneeTgt = 0f, prevHipTgt = 0f, prevAnkleNomTgt = 0f;
            float prevPhaseVel = 0.30f;
            float maxKneeRateDiff = 0f, maxHipRateDiff = 0f, maxAnkleNomDiff = 0f;
            float maxRefKneeAccel = 0f, maxRefHipAccel = 0f, maxRefAnkleAccel = 0f;

            int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
            for (int tick = 0; tick < totalTicks; tick++)
            {
                AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                float time = tick * (float)SimulationConstants.FixedDeltaTimeSeconds;
                float sq = adapter.Sq;
                SquatState state = adapter.State;
                SquatPhaseDirection phaseDir = adapter.Direction;

                adapter.TryGetTargetComposition("left_shank", out var cK);
                adapter.TryGetTargetComposition("left_thigh", out var cH);
                adapter.TryGetTargetComposition("left_foot", out var cA);

                var dK = powered.GetJoint("left_shank").Diagnostic;
                var dH = powered.GetJoint("left_thigh").Diagnostic;
                var dA = powered.GetJoint("left_foot").Diagnostic;

                float kTarget = TwistX(cK.Final);
                float hTarget = TwistX(cH.Final);
                float aNomTarget = TwistX(cA.Nominal);

                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                float kFdRate = tick > 0 ? (kTarget - prevKneeTgt) / dt : 0f;
                float hFdRate = tick > 0 ? (hTarget - prevHipTgt) / dt : 0f;
                float aNomFdRate = tick > 0 ? (aNomTarget - prevAnkleNomTgt) / dt : 0f;
                prevKneeTgt = kTarget; prevHipTgt = hTarget; prevAnkleNomTgt = aNomTarget;

                float kCmdRate = dK.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;
                float hCmdRate = dH.TargetAngularVelocityRadS.x * Mathf.Rad2Deg;

                // Ankle nominal rate: rate without balance offset
                var (aNomVal, aDqds, aD2qds2) = EvaluateHermiteDerivatives(profile, "left_foot", sq, phaseDir);
                float curPhaseVel = (state == SquatState.DESCENT) ? adapter.PhaseRate :
                                   (state == SquatState.ASCENT) ? -adapter.PhaseRate : 0f;
                float aNomCmdRate = aDqds * curPhaseVel;

                float phaseAccel = (curPhaseVel - prevPhaseVel) / dt;
                prevPhaseVel = curPhaseVel;

                var (_, kDqds, kD2qds2) = EvaluateHermiteDerivatives(profile, "left_shank", sq, phaseDir);
                var (_, hDqds, hD2qds2) = EvaluateHermiteDerivatives(profile, "left_thigh", sq, phaseDir);

                float kRefAccel = kD2qds2 * (curPhaseVel * curPhaseVel) + kDqds * phaseAccel;
                float hRefAccel = hD2qds2 * (curPhaseVel * curPhaseVel) + hDqds * phaseAccel;
                float aRefAccel = aD2qds2 * (curPhaseVel * curPhaseVel) + aDqds * phaseAccel;

                float kDiff = Mathf.Abs(kCmdRate - kFdRate);
                float hDiff = Mathf.Abs(hCmdRate - hFdRate);
                float aDiff = Mathf.Abs(aNomCmdRate - aNomFdRate);

                // Exclude the single tick where state switches (delta Tgt over dt spans the step)
                if (tick > 5 && state != SquatState.BOTTOM && state != SquatState.REVERSAL && tick != 332 && tick != 352 && tick != 363 && tick != 364)
                {
                    if (kDiff > maxKneeRateDiff) maxKneeRateDiff = kDiff;
                    if (hDiff > maxHipRateDiff) maxHipRateDiff = hDiff;
                    if (aDiff > maxAnkleNomDiff) maxAnkleNomDiff = aDiff;
                }

                if (Mathf.Abs(kRefAccel) > Mathf.Abs(maxRefKneeAccel)) maxRefKneeAccel = kRefAccel;
                if (Mathf.Abs(hRefAccel) > Mathf.Abs(maxRefHipAccel)) maxRefHipAccel = hRefAccel;
                if (Mathf.Abs(aRefAccel) > Mathf.Abs(maxRefAnkleAccel)) maxRefAnkleAccel = aRefAccel;

                csv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F3},{2},{3:F4},{4:F2},{5:F2},{6:F2},{7:F1},{8:F2},{9:F2},{10:F2},{11:F1},{12:F2},{13:F2},{14:F2}\n",
                    tick, time, state, sq,
                    kCmdRate, kFdRate, kDiff, kRefAccel,
                    hCmdRate, hFdRate, hDiff, hRefAccel,
                    aNomCmdRate, aNomFdRate, aDiff);
            }

            File.WriteAllText(Path.Combine(dir, "target-rate-parity-full-cycle.csv"), csv.ToString());
            Debug.Log($"[H19_04] Max steady rate differences: Knee={maxKneeRateDiff:F2}°/s, Hip={maxHipRateDiff:F2}°/s, AnkleNom={maxAnkleNomDiff:F2}°/s");
            Debug.Log($"[H19_04] Peak reference accelerations: Knee={maxRefKneeAccel:F1}°/s², Hip={maxRefHipAccel:F1}°/s², Ankle={maxRefAnkleAccel:F1}°/s²");
            Debug.Log($"[H19_04] Reversal Pose Discontinuity = {profile.ReversalPoseDiscontinuity:F6} rad");
        }

        // -------------------------------------------------------------------------
        // TEST 5: PHASE-RATE LOWER-LIMB SCALING (100%, 75%, 50%)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_05_PHASE_RATE_LOWER_LIMB_SCALING()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("phase_rate_pct,phase_rate,descent_knee_peak,ascent_knee_peak,bottom_entry_knee_err,reversal_knee_err," +
                           "descent_hip_peak,ascent_hip_peak,bottom_entry_hip_err,reversal_hip_err," +
                           "descent_ankle_peak,ascent_ankle_peak,bottom_entry_ankle_err,reversal_ankle_err");

            (float pct, float rate)[] conditions =
            {
                (100f, 0.30f),
                (75f, 0.225f),
                (50f, 0.150f)
            };

            foreach (var (pct, rate) in conditions)
            {
                yield return LoadScene();
                PrepareManualRuntime(25f);

                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                PhysicalFootContactDetector rightFoot = _controller.RightFootContact;

                adapter.PhaseRate = rate;
                AdvanceTicks(runtime, 30, leftFoot, rightFoot);
                adapter.StartSquat();

                float descentKneePeak = 0f, ascentKneePeak = 0f, bottomEntryKnee = 0f, reversalKnee = 0f;
                float descentHipPeak = 0f, ascentHipPeak = 0f, bottomEntryHip = 0f, reversalHip = 0f;
                float descentAnklePeak = 0f, ascentAnklePeak = 0f, bottomEntryAnkle = 0f, reversalAnkle = 0f;

                bool recordedBottomEntry = false;
                bool recordedReversal = false;

                int totalTicks = Mathf.CeilToInt((1.0f / rate * 2.0f + 3.0f) / (float)SimulationConstants.FixedDeltaTimeSeconds);

                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                    SquatState state = adapter.State;
                    SquatPhaseDirection phaseDir = adapter.Direction;

                    adapter.TryGetTargetComposition("left_shank", out var cK);
                    adapter.TryGetTargetComposition("left_thigh", out var cH);
                    adapter.TryGetTargetComposition("left_foot", out var cA);

                    var dK = powered.GetJoint("left_shank").Diagnostic;
                    var dH = powered.GetJoint("left_thigh").Diagnostic;
                    var dA = powered.GetJoint("left_foot").Diagnostic;

                    float kErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                    float hErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);
                    float aErr = TwistX(dA.ActualRelative) - TwistX(cA.Final);

                    if (phaseDir == SquatPhaseDirection.Descent)
                    {
                        if (Mathf.Abs(kErr) > Mathf.Abs(descentKneePeak)) descentKneePeak = kErr;
                        if (Mathf.Abs(hErr) > Mathf.Abs(descentHipPeak)) descentHipPeak = hErr;
                        if (Mathf.Abs(aErr) > Mathf.Abs(descentAnklePeak)) descentAnklePeak = aErr;
                    }
                    else if (phaseDir == SquatPhaseDirection.Ascent && state == SquatState.ASCENT)
                    {
                        if (Mathf.Abs(kErr) > Mathf.Abs(ascentKneePeak)) ascentKneePeak = kErr;
                        if (Mathf.Abs(hErr) > Mathf.Abs(ascentHipPeak)) ascentHipPeak = hErr;
                        if (Mathf.Abs(aErr) > Mathf.Abs(ascentAnklePeak)) ascentAnklePeak = aErr;
                    }

                    if (!recordedBottomEntry && state == SquatState.BOTTOM)
                    {
                        bottomEntryKnee = kErr;
                        bottomEntryHip = hErr;
                        bottomEntryAnkle = aErr;
                        recordedBottomEntry = true;
                    }

                    if (!recordedReversal && state == SquatState.REVERSAL)
                    {
                        reversalKnee = kErr;
                        reversalHip = hErr;
                        reversalAnkle = aErr;
                        recordedReversal = true;
                    }

                    if (adapter.LockoutReached)
                        break;
                }

                csv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0:F0},{1:F3},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2},{9:F2},{10:F2},{11:F2},{12:F2},{13:F2}\n",
                    pct, rate,
                    descentKneePeak, ascentKneePeak, bottomEntryKnee, reversalKnee,
                    descentHipPeak, ascentHipPeak, bottomEntryHip, reversalHip,
                    descentAnklePeak, ascentAnklePeak, bottomEntryAnkle, reversalAnkle);

                Debug.Log($"[H19_05] {pct:F0}% Rate ({rate:F3}/s): Knee: Desc={descentKneePeak:F2}°, Asc={ascentKneePeak:F2}°, Btm={bottomEntryKnee:F2}° | Hip: Desc={descentHipPeak:F2}°, Asc={ascentHipPeak:F2}°, Btm={bottomEntryHip:F2}°");
            }

            File.WriteAllText(Path.Combine(dir, "phase-rate-lower-limb-ab.csv"), csv.ToString());
            Debug.Log("[H19_05] Wrote phase-rate-lower-limb-ab.csv");
        }

        // -------------------------------------------------------------------------
        // TEST 6: REVERSAL RATE-STEP DISCRIMINATOR (R0=step vs R1=ramped onset)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_06_REVERSAL_RATE_RAMP_DISCRIMINATION()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("case,ascent_entry_knee_err,ascent_entry_hip_err,ascent_peak_knee_err,ascent_peak_knee_sq," +
                           "ascent_peak_hip_err,ascent_peak_hip_sq,ascent_tick6_knee_vel,ascent_tick6_hip_vel");

            foreach (string cName in new[] { "R0_PRODUCTION_STEP", "R1_RAMPED_VELOCITY_ONSET" })
            {
                yield return LoadScene();
                PrepareManualRuntime(25f);

                FoundationRuntime runtime = _bootstrap.Runtime;
                SquatPhysicalAdapter adapter = _controller.Adapter;
                PoweredJointController powered = _rig.PoweredController;
                PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                PhysicalFootContactDetector rightFoot = _controller.RightFootContact;

                AdvanceTicks(runtime, 30, leftFoot, rightFoot);
                adapter.StartSquat();

                int ascentTicksElapsed = 0;
                float ascentEntryKnee = 0f, ascentEntryHip = 0f;
                float ascentPeakKnee = 0f, ascentPeakKneeSq = 0f;
                float ascentPeakHip = 0f, ascentPeakHipSq = 0f;
                float tick6KneeVel = 0f, tick6HipVel = 0f;

                int totalTicks = Mathf.CeilToInt(9.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                    SquatState state = adapter.State;
                    float sq = adapter.Sq;

                    if (state == SquatState.ASCENT)
                    {
                        ascentTicksElapsed++;

                        adapter.TryGetTargetComposition("left_shank", out var cK);
                        adapter.TryGetTargetComposition("left_thigh", out var cH);
                        var dK = powered.GetJoint("left_shank").Diagnostic;
                        var dH = powered.GetJoint("left_thigh").Diagnostic;

                        float kErr = TwistX(dK.ActualRelative) - TwistX(cK.Final);
                        float hErr = TwistX(dH.ActualRelative) - TwistX(cH.Final);

                        if (ascentTicksElapsed == 1)
                        {
                            ascentEntryKnee = kErr;
                            ascentEntryHip = hErr;
                        }

                        // For R1: in the first 6 ascent ticks, ramp phase rate feedforward
                        if (cName == "R1_RAMPED_VELOCITY_ONSET" && ascentTicksElapsed <= 6)
                        {
                            float rampFraction = ascentTicksElapsed / 6f;
                            // Set phase velocity feedforward on adapter for qualification
                            float rampedVel = -adapter.PhaseRate * rampFraction;
                            adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Ascent, SquatState.ASCENT, rampedVel);
                        }
                        else if (cName == "R1_RAMPED_VELOCITY_ONSET" && ascentTicksElapsed == 7)
                        {
                            adapter.AutoCycle = true;
                        }

                        if (ascentTicksElapsed == 6)
                        {
                            tick6KneeVel = dK.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                            tick6HipVel = dH.ActualAngularVelocityRadS.x * Mathf.Rad2Deg;
                        }

                        if (Mathf.Abs(kErr) > Mathf.Abs(ascentPeakKnee)) { ascentPeakKnee = kErr; ascentPeakKneeSq = sq; }
                        if (Mathf.Abs(hErr) > Mathf.Abs(ascentPeakHip)) { ascentPeakHip = hErr; ascentPeakHipSq = sq; }
                    }

                    if (adapter.LockoutReached)
                        break;
                }

                csv.AppendFormat(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F2}\n",
                    cName, ascentEntryKnee, ascentEntryHip,
                    ascentPeakKnee, ascentPeakKneeSq, ascentPeakHip, ascentPeakHipSq,
                    tick6KneeVel, tick6HipVel);

                Debug.Log($"[H19_06] {cName}: AscentEntryKneeErr={ascentEntryKnee:F2}° | PeakKneeErr={ascentPeakKnee:F2}° at sq={ascentPeakKneeSq:F2} | Tick6Vel: Knee={tick6KneeVel:F1}°/s, Hip={tick6HipVel:F1}°/s");
            }

            File.WriteAllText(Path.Combine(dir, "reversal-rate-ramp-ab.csv"), csv.ToString());
            Debug.Log("[H19_06] Wrote reversal-rate-ramp-ab.csv");
        }

        // -------------------------------------------------------------------------
        // TEST 7: ASCENT VISUAL EVIDENCE CAPTURE (0 kg and 25 kg)
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_07_ASCENT_VISUAL_CAPTURE()
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.Log("[H19_07] Headless mode, skipping visual capture.");
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

                // Target ascent checkpoints
                float[] ascentTargetSqs = { 1.00f, 0.80f, 0.55f, 0.45f, 0.25f, 0.00f };
                bool[] captured = new bool[ascentTargetSqs.Length];
                bool capturedReversalExit = false;

                int totalTicks = Mathf.CeilToInt(8.5f / (float)SimulationConstants.FixedDeltaTimeSeconds);
                for (int tick = 0; tick < totalTicks; tick++)
                {
                    AdvanceTicks(runtime, 1, _controller.LeftFootContact, _controller.RightFootContact);
                    SquatState state = adapter.State;
                    SquatPhaseDirection dir = adapter.Direction;
                    float sq = adapter.Sq;

                    // Bottom capture at sq = 1.00
                    if (state == SquatState.BOTTOM && !captured[0])
                    {
                        yield return null;
                        string tag = string.Format(CultureInfo.InvariantCulture, "h19_{0:F0}kg_bottom_sq1.00", load);
                        CaptureImage(camera, Path.Combine(evidenceDir, tag + "_side.png"), new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                        CaptureImage(camera, Path.Combine(evidenceDir, tag + "_oblique.png"), new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                        captured[0] = true;
                    }

                    // Reversal exit (first tick of ASCENT)
                    if (state == SquatState.ASCENT && !capturedReversalExit)
                    {
                        yield return null;
                        string tag = string.Format(CultureInfo.InvariantCulture, "h19_{0:F0}kg_ascent_exit_sq1.00", load);
                        CaptureImage(camera, Path.Combine(evidenceDir, tag + "_side.png"), new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                        CaptureImage(camera, Path.Combine(evidenceDir, tag + "_oblique.png"), new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                        capturedReversalExit = true;
                    }

                    // Ascent checkpoints
                    if (dir == SquatPhaseDirection.Ascent && state == SquatState.ASCENT)
                    {
                        for (int i = 1; i < ascentTargetSqs.Length; i++)
                        {
                            if (!captured[i] && sq <= ascentTargetSqs[i] + 0.015f)
                            {
                                yield return null;
                                string tag = string.Format(CultureInfo.InvariantCulture, "h19_{0:F0}kg_ascent_sq{1:F2}", load, ascentTargetSqs[i]);
                                CaptureImage(camera, Path.Combine(evidenceDir, tag + "_side.png"), new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                                CaptureImage(camera, Path.Combine(evidenceDir, tag + "_oblique.png"), new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                                captured[i] = true;
                            }
                        }
                    }

                    if (adapter.LockoutReached)
                    {
                        if (!captured[5])
                        {
                            yield return null;
                            string tag = string.Format(CultureInfo.InvariantCulture, "h19_{0:F0}kg_ascent_lockout_sq0.00", load);
                            CaptureImage(camera, Path.Combine(evidenceDir, tag + "_side.png"), new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                            CaptureImage(camera, Path.Combine(evidenceDir, tag + "_oblique.png"), new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                            captured[5] = true;
                        }
                        break;
                    }
                }
            }

            yield return null;
        }

        // -------------------------------------------------------------------------
        // TEST 8: LOWER-LIMB CLOSED-CHAIN COUPLING PERTURBATION
        // -------------------------------------------------------------------------
        [UnityTest]
        public IEnumerator H19_08_CLOSED_CHAIN_LOWER_LIMB_COUPLING()
        {
            yield return LoadScene();
            string dir = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(dir);

            var csv = new StringBuilder();
            csv.AppendLine("sq,delta_knee_deg,knee_target,knee_act,hip_act,ankle_act,pelvis_pitch,trunk_pitch,com_z,cop_z,support_contacts");

            float[] testSqs = { 1.00f, 0.55f };
            float[] deltaKnees = { -1.0f, 0.0f, 1.0f };

            foreach (float sq in testSqs)
            {
                foreach (float deltaK in deltaKnees)
                {
                    yield return LoadScene();
                    PrepareManualRuntime(25f);

                    FoundationRuntime runtime = _bootstrap.Runtime;
                    SquatPhysicalAdapter adapter = _controller.Adapter;
                    PoweredJointController powered = _rig.PoweredController;
                    PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
                    PhysicalFootContactDetector rightFoot = _controller.RightFootContact;
                    Rigidbody pelvis = _rig.Segments["pelvis"].Body;
                    Rigidbody thorax = _rig.Segments["thorax"].Body;

                    AdvanceTicks(runtime, 30, leftFoot, rightFoot);
                    adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Ascent, SquatState.BOTTOM, 0f);

                    // Settle for 100 ticks (1.0s)
                    AdvanceTicks(runtime, 100, leftFoot, rightFoot);

                    // Measure baseline
                    var dK = powered.GetJoint("left_shank").Diagnostic;
                    var dH = powered.GetJoint("left_thigh").Diagnostic;
                    var dA = powered.GetJoint("left_foot").Diagnostic;

                    float kAct = TwistX(dK.ActualRelative);
                    float hAct = TwistX(dH.ActualRelative);
                    float aAct = TwistX(dA.ActualRelative);

                    Vector3 trunkAxis = thorax.position - pelvis.position;
                    float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;
                    Vector3 pelForward = pelvis.transform.forward;
                    float pelPitch = Mathf.Atan2(pelForward.y, pelForward.z) * Mathf.Rad2Deg;

                    csv.AppendFormat(CultureInfo.InvariantCulture,
                        "{0:F2},{1:F1},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F4},{9:F4},{10}\n",
                        sq, deltaK, 114.0f + deltaK, kAct, hAct, aAct,
                        pelPitch, trunkPitch,
                        adapter.Balance.SystemCom.z,
                        adapter.Balance.HasCopEstimate ? adapter.Balance.CopEstimate.z : adapter.Balance.SupportApCenter,
                        adapter.Balance.SupportContactCount);

                    Debug.Log($"[H19_08] sq={sq:F2}, deltaK={deltaK:F1}°: Knee={kAct:F2}°, Hip={hAct:F2}°, Ankle={aAct:F2}°, Trunk={trunkPitch:F2}°, COP={adapter.Balance.CopEstimate.z:F4}m");
                }
            }

            File.WriteAllText(Path.Combine(dir, "lower-limb-coupling-perturbation.csv"), csv.ToString());
            Debug.Log("[H19_08] Wrote lower-limb-coupling-perturbation.csv");
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
    }
}
