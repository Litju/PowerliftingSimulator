using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// GAM-13 load-response characterization and qualification on the real
    /// SquatPhysicalPrototype scene. Every probe uses the same harness; only
    /// the bar load differs.
    /// </summary>
    public sealed class GAM13SquatLoadCalibrationTests
    {
        /// <summary>
        /// The canonical light probe is 25 kg, not 20 kg: the repository's
        /// competition barbell is a 20 kg bar with two 2.5 kg collars and
        /// BarbellLoadingSolver rejects any load below that 25 kg base.
        /// </summary>
        public static readonly float[] CanonicalProbesKg = { 25f, 60f, 140f, 170f, 300f };
        public const int CanonicalRepeats = 3;

        private readonly List<string> _unexpectedErrors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _unexpectedErrors.Clear();
            Application.logMessageReceived += CaptureUnexpectedError;
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            if (bootstrap != null && bootstrap.Runtime != null && bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (bootstrap != null)
                Object.DestroyImmediate(bootstrap.gameObject);
            Application.logMessageReceived -= CaptureUnexpectedError;
            LogAssert.ignoreFailingMessages = false;
            Assert.That(_unexpectedErrors, Is.Empty, string.Join("\n", _unexpectedErrors));
            yield return null;
        }

        /// <summary>
        /// Characterization of the unmodified pre-GAM-13 production system.
        /// Excluded from default suites; its CSV is the committed baseline.
        /// </summary>
        [UnityTest]
        [Explicit("GAM-13 baseline characterization; excluded from default qualification suites.")]
        public IEnumerator GAM13_BASELINE_LOAD_SWEEP()
        {
            List<GAM13AttemptResult> results = new List<GAM13AttemptResult>();
            foreach (float load in CanonicalProbesKg)
            {
                for (int run = 1; run <= CanonicalRepeats; run++)
                    yield return GAM13SquatLoadCalibrationHarness.RunAttempt("baseline", "unmodified", load, run, null, results);
            }

            GAM13SquatLoadCalibrationHarness.WriteCsv("baseline-load-sweep.csv", results);
            Assert.That(results.Count, Is.EqualTo(CanonicalProbesKg.Length * CanonicalRepeats));
        }

        private void CaptureUnexpectedError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error || condition.StartsWith("connection.state_change", System.StringComparison.Ordinal))
                return;
            _unexpectedErrors.Add(condition);
        }
    }
}
