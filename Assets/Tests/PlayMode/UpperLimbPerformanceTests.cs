using System;
using System.Collections;
using System.Diagnostics;
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
    public sealed class UpperLimbPerformanceTests
    {
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

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

        private static double P95(double[] samples)
        {
            var copy = (double[])samples.Clone();
            Array.Sort(copy);
            int index = Mathf.Clamp((int)(copy.Length * 0.95), 0, copy.Length - 1);
            return copy[index];
        }

        [UnityTest]
        public IEnumerator UPPER_LIMB_PERFORMANCE_AND_STABILITY_BUDGET()
        {
            LogAssert.ignoreFailingMessages = true;

            // ---------------------------------------------------------------
            // Phase 1: Foundation Frame, Catch-Up, and Controller (Render Mode)
            // ---------------------------------------------------------------
            yield return LoadScene();
            FoundationRuntime runtime = _bootstrap.Runtime;

            for (int i = 0; i < 50; i++)
                runtime.AdvanceRenderFrame(0.01d);

            var foundationMs = new double[300];
            for (int i = 0; i < foundationMs.Length; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                runtime.AdvanceRenderFrame(0.01d);
                sw.Stop();
                foundationMs[i] = sw.Elapsed.TotalMilliseconds;
            }

            for (int i = 0; i < 20; i++)
                runtime.AdvanceRenderFrame(0.04d);

            var catchUpMs = new double[100];
            for (int i = 0; i < catchUpMs.Length; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                runtime.AdvanceRenderFrame(0.04d);
                sw.Stop();
                catchUpMs[i] = sw.Elapsed.TotalMilliseconds;
            }

            var controllerMs = new double[300];
            for (int i = 0; i < controllerMs.Length; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                _rig.PoweredController.Step(runtime.CurrentTime, PlayerIntentFrame.Empty);
                sw.Stop();
                controllerMs[i] = sw.Elapsed.TotalMilliseconds;
            }

            // ---------------------------------------------------------------
            // Phase 2: Isolated Physics Step (Clean Manual Stepping Mode)
            // ---------------------------------------------------------------
            yield return TearDown();
            yield return LoadScene();
            runtime = _bootstrap.Runtime;

            for (int i = 0; i < 50; i++)
                runtime.StepOne();

            var physicsTickMs = new double[300];
            for (int i = 0; i < physicsTickMs.Length; i++)
            {
                Stopwatch sw = Stopwatch.StartNew();
                runtime.StepOne();
                sw.Stop();
                physicsTickMs[i] = sw.Elapsed.TotalMilliseconds;
            }

            double physicsP95 = P95(physicsTickMs);
            double foundationP95 = P95(foundationMs);
            double catchUpP95 = P95(catchUpMs);
            double controllerP95 = P95(controllerMs);

            UnityEngine.Debug.Log($"[PERF BUDGET] physics_step p95: {physicsP95:F3} ms (budget <= 2.0 ms)");
            UnityEngine.Debug.Log($"[PERF BUDGET] catch_up_frame p95: {catchUpP95:F3} ms (budget <= 8.0 ms)");
            UnityEngine.Debug.Log($"[PERF BUDGET] foundation_frame p95: {foundationP95:F3} ms (budget <= 10.0 ms)");
            UnityEngine.Debug.Log($"[PERF BUDGET] controller_execution p95: {controllerP95:F3} ms (budget <= 0.25 ms)");

            string physicsStatus = physicsP95 <= 2.0 ? "PASS" : "FAIL";
            string catchUpStatus = catchUpP95 <= 8.0 ? "PASS" : "FAIL";
            string foundationStatus = foundationP95 <= 10.0 ? "PASS" : "FAIL";
            string controllerStatus = controllerP95 <= 0.25 ? "PASS" : "FAIL";

            var csv = new StringBuilder();
            csv.AppendLine("metric,p95_ms,budget_ms,status");
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "physics_step,{0:F4},2.0000,{1}", physicsP95, physicsStatus));
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "catch_up_frame,{0:F4},8.0000,{1}", catchUpP95, catchUpStatus));
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "foundation_frame,{0:F4},10.0000,{1}", foundationP95, foundationStatus));
            csv.AppendLine(string.Format(CultureInfo.InvariantCulture, "controller_execution,{0:F4},0.2500,{1}", controllerP95, controllerStatus));

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-upper-limb-perf-budget.csv"), csv.ToString());

            Assert.That(physicsP95, Is.LessThanOrEqualTo(2.0), "Physics step p95 exceeded budget of 2.0 ms");
            Assert.That(catchUpP95, Is.LessThanOrEqualTo(8.0), "Catch-up frame p95 exceeded budget of 8.0 ms");
            Assert.That(foundationP95, Is.LessThanOrEqualTo(10.0), "Foundation frame p95 exceeded budget of 10.0 ms");
            Assert.That(controllerP95, Is.LessThanOrEqualTo(0.25), "Controller execution p95 exceeded budget of 0.25 ms");
        }
    }
}
