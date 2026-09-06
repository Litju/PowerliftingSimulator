using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H2. The ground registration repair landed, and the open-loop
    /// preload identified before it was measured on a moving body rather than
    /// at equilibrium, so it is not adopted. The question this suite opens
    /// with is the one that decides everything after it: does the existing
    /// predictive balance controller hold the properly grounded plant when the
    /// feed-forward preload is zero?
    /// </summary>
    public sealed class PhysicalStandingClosedLoopTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

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

        // ---------------------------------------------------------------
        // Experiment C0. Nominal GAM-10 target, zero gravity preload,
        // predictive balance on, unloaded, s_q = 0. No gain changes: this
        // measures the controller as Phase 5E left it, on the repaired
        // ground, so that any later tuning has an honest starting point.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator C0_ZERO_PRELOAD_CLOSED_LOOP_BASELINE()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.AnkleSagittalOffsetOverrideRad = null;
            adapter.Preload.Enabled = true;
            adapter.Preload.CopyFrom(SquatEquilibriumPreload.QualifiedStanding());

            var trace = new StringBuilder();
            trace.AppendLine(TraceHeader());

            const int totalTicks = 1000;
            int survivedTicks = totalTicks;
            string failureMode = "NONE";

            float worstCaptureMargin = float.PositiveInfinity;
            float maxAbsComApVelocity = 0f;
            float maxAbsComAp = 0f;
            float copMin = float.PositiveInfinity;
            float copMax = float.NegativeInfinity;
            float maxAnkleOffsetDeg = 0f;
            float maxSaturation = 0f;
            int saturatedTicks = 0;
            int measuredTicks = 0;
            float initialPelvisY = _rig.Segments["pelvis"].Body.position.y;
            float minPelvisY = initialPelvisY;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                Advance();
                SquatBalanceObserver balance = adapter.Balance;
                SquatPredictiveBalanceController control = adapter.BalanceController;
                float pelvisY = _rig.Segments["pelvis"].Body.position.y;

                if (tick < 100 ? tick % 5 == 0 : tick % 10 == 0)
                    trace.AppendLine(TraceRow(tick, adapter));

                if (tick >= SettleTicks)
                {
                    measuredTicks++;
                    minPelvisY = Mathf.Min(minPelvisY, pelvisY);
                    worstCaptureMargin = Mathf.Min(worstCaptureMargin,
                        Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                    maxAbsComApVelocity = Mathf.Max(maxAbsComApVelocity, Mathf.Abs(balance.SystemComVelocity.z));
                    maxAbsComAp = Mathf.Max(maxAbsComAp, Mathf.Abs(balance.SystemCom.z));
                    maxAnkleOffsetDeg = Mathf.Max(maxAnkleOffsetDeg,
                        Mathf.Abs(control.AnkleSagittalOffsetRad) * Mathf.Rad2Deg);
                    maxSaturation = Mathf.Max(maxSaturation, adapter.MaxDriveSaturation);
                    if (adapter.MaxDriveSaturation >= 1f)
                        saturatedTicks++;
                    if (balance.HasCopEstimate)
                    {
                        copMin = Mathf.Min(copMin, balance.CopEstimate.z);
                        copMax = Mathf.Max(copMax, balance.CopEstimate.z);
                    }
                }

                // A fall is not worth simulating past. Stop at the first
                // unambiguous sign and record which one arrived.
                if (pelvisY < initialPelvisY - 0.25f)
                {
                    failureMode = "PELVIS_COLLAPSE";
                    survivedTicks = tick + 1;
                    break;
                }
                if (!balance.HasSupport && tick > SettleTicks)
                {
                    failureMode = "LOST_CONTACT";
                    survivedTicks = tick + 1;
                    break;
                }
                if (Mathf.Abs(FootPitchDegrees("left_foot")) > 25f)
                {
                    failureMode = "FOOT_TIPPED";
                    survivedTicks = tick + 1;
                    break;
                }
            }

            trace.AppendLine(TraceRow(survivedTicks, adapter));
            WriteMeasurement("GAM11-c0-zero-preload-closed-loop.csv", trace.ToString());

            float durationSeconds = survivedTicks * (float)SimulationConstants.FixedDeltaTimeSeconds;

            // K_gravity for the reduced-order ankle-dominant standing mode.
            // GAME_CONTROL_REDUCED_ORDER_MODEL, not a claim about a human.
            float kGravity = adapter.Balance.SystemMassKg *
                SquatBalanceObserver.GravityMagnitudeMps2 * adapter.Balance.ComHeightM;

            string summary = string.Format(CultureInfo.InvariantCulture,
                "result={0} duration={1:F2}s ticks={2} failure={3} mass={4:F2}kg comHeight={5:F4}m " +
                "kGravity={6:F1}Nm/rad maxComAp={7:F4} maxComApVel={8:F4} worstCaptureMargin={9:F4} " +
                "cop=[{10:F4},{11:F4}] maxAnkleOffsetDeg={12:F2} maxSaturation={13:F3} " +
                "sustainedSaturation={14:F3} initialPelvisY={15:F4} minPelvisY={16:F4}",
                survivedTicks >= totalTicks ? "SURVIVED" : "FELL",
                durationSeconds, survivedTicks, failureMode,
                adapter.Balance.SystemMassKg, adapter.Balance.ComHeightM, kGravity,
                maxAbsComAp, maxAbsComApVelocity, worstCaptureMargin, copMin, copMax,
                maxAnkleOffsetDeg, maxSaturation,
                measuredTicks > 0 ? saturatedTicks / (float)measuredTicks : 0f,
                initialPelvisY, minPelvisY);

            Debug.Log("[C0 ZERO PRELOAD CLOSED LOOP] " + summary + Environment.NewLine + trace);

            // C0 is a measurement, not a gate. It only has to have run on a
            // grounded athlete for its answer to mean anything.
            Assert.That(initialPelvisY, Is.GreaterThan(0.9f), "The athlete did not spawn standing. " + summary);
            yield return null;
        }

        // ---------------------------------------------------------------
        // Shared harness.
        // ---------------------------------------------------------------
        private const int SettleTicks = 30;

        private void Advance()
        {
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static string TraceHeader() =>
            "tick,time_s,pelvis_y,com_ap,com_ap_vel,cop_measured_ap,cop_desired_ap,cop_error," +
            "capture_ap,support_ap_rear,support_ap_front,capture_margin_rear,capture_margin_front," +
            "ankle_nominal_deg,ankle_preload_deg,ankle_balance_deg,ankle_final_deg,ankle_actual_deg," +
            "hip_balance_deg,trunk_balance_deg,requested_ankle_torque_nm,normal_impulse,fz_estimate_n," +
            "max_demand,ankle_saturated,foot_pitch_deg,contacts,slip_mps";

        private string TraceRow(int tick, SquatPhysicalAdapter adapter)
        {
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController control = adapter.BalanceController;
            float copMeasured = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition ankle);
            float slip = _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f;

            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F3},{2:F4},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12:F5}," +
                "{13:F2},{14:F2},{15:F2},{16:F2},{17:F2},{18:F2},{19:F2},{20:F2},{21:F4},{22:F1}," +
                "{23:F3},{24},{25:F2},{26},{27:F4}",
                tick, tick * SimulationConstants.FixedDeltaTimeSeconds,
                _rig.Segments["pelvis"].Body.position.y,
                balance.SystemCom.z, balance.SystemComVelocity.z,
                copMeasured, control.CopDesiredAp, control.CopErrorAp,
                balance.CaptureAp, balance.SupportApMin, balance.SupportApMax,
                balance.CaptureMarginRear, balance.CaptureMarginFront,
                SagittalDegrees(ankle.Nominal), SagittalDegrees(ankle.GravityBias),
                SagittalDegrees(ankle.BalanceOffset), SagittalDegrees(ankle.Final),
                ActualDegrees("left_foot"),
                control.HipSagittalOffsetRad * Mathf.Rad2Deg,
                control.TrunkSagittalOffsetRad * Mathf.Rad2Deg,
                control.RequestedAnkleTorqueNm,
                balance.TotalNormalImpulse, balance.TotalNormalImpulse / dt,
                adapter.MaxDriveSaturation, control.IsAnkleOffsetSaturated ? 1 : 0,
                FootPitchDegrees("left_foot"), balance.SupportContactCount, slip);
        }

        private float ActualDegrees(string jointId) =>
            SagittalDegrees(_rig.PoweredController.GetJoint(jointId).Diagnostic.ActualRelative);

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return 0f;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static float SagittalDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude) * Mathf.Rad2Deg;
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }
    }
}
