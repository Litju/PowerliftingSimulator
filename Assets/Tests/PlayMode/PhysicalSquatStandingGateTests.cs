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
    /// Phase 5F. The athlete has to stand for ten seconds on dynamic feet and
    /// a dynamic pelvis, with no pins, before any squat behaviour is worth
    /// testing.
    /// </summary>
    public sealed class PhysicalSquatStandingGateTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";
        private const int SettleTicks = 100;
        private const int TotalTicks = 1000;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
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

        [UnityTest]
        public IEnumerator S1_TEN_SECOND_UNLOADED_STANDING_GATE()
        {
            yield return RunStandingGate(0f, "unloaded");
        }

        private IEnumerator RunStandingGate(float loadKg, string label)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController balanceController = adapter.BalanceController;
            FoundationRuntime runtime = _bootstrap.Runtime;

            var trace = new StringBuilder();
            trace.AppendLine("tick,com_ap,com_vel_ap,capture_ap,cop_des_ap,cop_meas_ap,support_ap_min,support_ap_max," +
                             "capture_margin_front,capture_margin_rear,ankle_offset_deg,hip_offset_deg,trunk_offset_deg," +
                             "hip_blend,max_drive_saturation,foot_pitch_deg,contacts,slip_mps,pelvis_y");

            float worstCaptureMargin = float.PositiveInfinity;
            float worstCopExcursion = 0f;
            float maxAbsComVelocity = 0f;
            int saturatedTicks = 0;
            int slippingTicks = 0;
            float worstFootPitch = 0f;
            float minPelvisY = float.PositiveInfinity;
            float pelvisYAtSettle = 0f;
            bool lostContact = false;

            for (int tick = 0; tick < TotalTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                if (_controller.LeftFootContact != null)
                    _controller.LeftFootContact.PhysicsTickUpdate(dt);
                if (_controller.RightFootContact != null)
                    _controller.RightFootContact.PhysicsTickUpdate(dt);

                float pelvisY = _rig.Segments["pelvis"].Body.position.y;
                float footPitch = FootPitchDegrees("left_foot");
                float slip = Mathf.Max(
                    _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f,
                    _controller.RightFootContact != null ? _controller.RightFootContact.SlipSpeed : 0f);

                if (tick == SettleTicks)
                    pelvisYAtSettle = pelvisY;

                if (tick >= SettleTicks)
                {
                    minPelvisY = Mathf.Min(minPelvisY, pelvisY);
                    worstCaptureMargin = Mathf.Min(worstCaptureMargin,
                        Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                    maxAbsComVelocity = Mathf.Max(maxAbsComVelocity, Mathf.Abs(balance.SystemComVelocity.z));
                    worstFootPitch = Mathf.Max(worstFootPitch, Mathf.Abs(footPitch));
                    if (!balance.HasSupport)
                        lostContact = true;
                    if (balance.HasCopEstimate)
                    {
                        worstCopExcursion = Mathf.Max(worstCopExcursion, Mathf.Max(
                            balance.SupportApMin - balance.CopEstimate.z,
                            balance.CopEstimate.z - balance.SupportApMax));
                    }
                    if (adapter.MaxDriveSaturation >= 1f)
                        saturatedTicks++;
                    if (slip > 0.02f)
                        slippingTicks++;
                }

                if (tick % 25 == 0 || tick == TotalTicks - 1)
                {
                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F2},{11:F2},{12:F2},{13:F2},{14:F3},{15:F2},{16},{17:F4},{18:F4}",
                        tick, balance.SystemCom.z, balance.SystemComVelocity.z, balance.CaptureAp,
                        balanceController.CopDesiredAp, balanceController.CopMeasuredAp,
                        balance.SupportApMin, balance.SupportApMax,
                        balance.CaptureMarginFront, balance.CaptureMarginRear,
                        balanceController.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                        balanceController.HipSagittalOffsetRad * Mathf.Rad2Deg,
                        balanceController.TrunkSagittalOffsetRad * Mathf.Rad2Deg,
                        balanceController.HipStrategyBlend, adapter.MaxDriveSaturation,
                        footPitch, balance.SupportContactCount, slip, pelvisY));
                }
            }

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-standing-gate-" + label + ".csv"),
                trace.ToString());

            int measuredTicks = TotalTicks - SettleTicks;
            float finalPelvisY = _rig.Segments["pelvis"].Body.position.y;
            float saturatedFraction = saturatedTicks / (float)measuredTicks;
            float slippingFraction = slippingTicks / (float)measuredTicks;
            string summary = string.Format(CultureInfo.InvariantCulture,
                "settlePelvisY={0:F4} finalPelvisY={1:F4} minPelvisY={2:F4} worstCaptureMargin={3:F4} " +
                "worstCopExcursion={4:F4} maxComVel={5:F4} saturatedFraction={6:F3} slippingFraction={7:F3} " +
                "worstFootPitch={8:F2} lostContact={9}",
                pelvisYAtSettle, finalPelvisY, minPelvisY, worstCaptureMargin, worstCopExcursion,
                maxAbsComVelocity, saturatedFraction, slippingFraction, worstFootPitch, lostContact);
            Debug.Log("[S1 TEN SECOND STANDING GATE " + label + "] " + summary + Environment.NewLine + trace);

            Assert.That(lostContact, Is.False, "The athlete lost plantar contact. " + summary);
            Assert.That(finalPelvisY, Is.GreaterThan(pelvisYAtSettle - 0.05f),
                "The athlete did not stay standing. " + summary);
            Assert.That(worstCaptureMargin, Is.GreaterThan(0.01f),
                "The capture point left the support polygon. " + summary);
            Assert.That(worstCopExcursion, Is.LessThan(0.01f),
                "The centre of pressure left the support polygon. " + summary);
            Assert.That(maxAbsComVelocity, Is.LessThan(0.25f),
                "The centre of mass is diverging rather than settling. " + summary);
            Assert.That(saturatedFraction, Is.LessThan(0.05f),
                "The actuators are sustained at their ceiling. " + summary);
            Assert.That(slippingFraction, Is.LessThan(0.05f),
                "The feet are persistently slipping. " + summary);
            Assert.That(worstFootPitch, Is.LessThan(12f),
                "The foot is tipping pathologically. " + summary);
            yield return null;
        }

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return 0f;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }
    }
}
