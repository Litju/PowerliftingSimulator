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
    /// Measures how far a commanded ankle target offset can actually move the
    /// centre of pressure. The zero-step balance law is only viable if the
    /// ankle can put the COP ahead of the COM, so this quantifies the ceiling
    /// on that authority instead of inferring it from a failing gate.
    /// </summary>
    public sealed class PhysicalAnkleCopAuthorityTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";
        private const int TicksPerStep = 30;

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

        [UnityTest]
        public IEnumerator A1_ANKLE_TARGET_OFFSET_TO_COP_AUTHORITY()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = false;
            SquatBalanceObserver balance = adapter.Balance;
            FoundationRuntime runtime = _bootstrap.Runtime;

            // Settle on the accepted standing pose first.
            for (int tick = 0; tick < 20; tick++)
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

            float supportApMax = balance.SupportApMax;
            float supportApMin = balance.SupportApMin;

            var trace = new StringBuilder();
            trace.AppendLine("commanded_offset_deg,cop_ap,com_ap,cop_minus_com,ankle_actual_deg,foot_pitch_deg,contacts");

            float bestCop = float.NegativeInfinity;
            float bestOffsetDeg = 0f;
            var offsets = new[] { 0f, 3f, 6f, 9f, 12f, 15f, 18f, 22f, 26f };

            foreach (float offsetDeg in offsets)
            {
                adapter.AnkleSagittalOffsetOverrideRad = offsetDeg * Mathf.Deg2Rad;
                for (int tick = 0; tick < TicksPerStep; tick++)
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

                float cop = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;
                float ankleActual = SignedXDegrees(_rig.PoweredController.GetJoint("left_foot").Diagnostic.ActualRelative);
                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F1},{1:F4},{2:F4},{3:F4},{4:F2},{5:F2},{6}",
                    offsetDeg, cop, balance.SystemCom.z, cop - balance.SystemCom.z,
                    ankleActual, FootPitchDegrees("left_foot"), balance.SupportContactCount));

                if (!float.IsNaN(cop) && cop > bestCop)
                {
                    bestCop = cop;
                    bestOffsetDeg = offsetDeg;
                }
            }

            adapter.AnkleSagittalOffsetOverrideRad = null;

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-ankle-cop-authority.csv"),
                trace.ToString());

            string summary = string.Format(CultureInfo.InvariantCulture,
                "supportAp=[{0:F4},{1:F4}] bestCopAp={2:F4} atOffset={3:F1}deg reachableFraction={4:F3}",
                supportApMin, supportApMax, bestCop, bestOffsetDeg,
                (bestCop - supportApMin) / Mathf.Max(1e-4f, supportApMax - supportApMin));
            Debug.Log("[A1 ANKLE COP AUTHORITY] " + summary + Environment.NewLine + trace);

            // Winter: the COP has to be able to get past the COM, otherwise
            // sway can never be reversed. This is the enabling condition for
            // the whole zero-step strategy.
            Assert.That(bestCop, Is.GreaterThan(supportApMin),
                "The ankle cannot move the centre of pressure at all. " + summary);
            yield return null;
        }

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return 0f;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static float SignedXDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude) * Mathf.Rad2Deg;
        }
    }
}
