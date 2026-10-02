using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using System;
using System.Globalization;
using System.IO;

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
            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            if (bootstrap != null && bootstrap.Runtime != null && bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (bootstrap != null)
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
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

        /// <summary>
        /// One untouched production attempt, selected by environment variable
        /// so the runner can launch each canonical load in a fresh Unity process.
        /// No controller, joint, balance, preload, capacity, rule, or failure
        /// configuration override is applied by this test.
        /// </summary>
        [UnityTest]
        [Explicit("GAM-13 Phase A untouched single-load screening; fresh Unity process required.")]
        public IEnumerator GAM13_PHASE_A_SINGLE_LOAD_FRESH_PROCESS()
        {
            string loadValue = Environment.GetEnvironmentVariable("GAM13_PHASE_A_LOAD_KG");
            string artifactPath = Environment.GetEnvironmentVariable("GAM13_PHASE_A_ARTIFACT_PATH");
            Assert.That(float.TryParse(loadValue, NumberStyles.Float, CultureInfo.InvariantCulture, out float loadKg), Is.True,
                "GAM13_PHASE_A_LOAD_KG must name one canonical load.");
            Assert.That(Array.IndexOf(CanonicalProbesKg, loadKg), Is.GreaterThanOrEqualTo(0),
                "Only a canonical GAM-13 screening load is allowed.");
            Assert.That(string.IsNullOrWhiteSpace(artifactPath), Is.False,
                "GAM13_PHASE_A_ARTIFACT_PATH must name this process's unique artifact.");

            var results = new List<GAM13AttemptResult>(1);
            var capture = new GAM13UntouchedScreeningTrace();
            yield return GAM13SquatLoadCalibrationHarness.RunAttempt(
                "phase-a-untouched",
                "production-checked-in",
                loadKg,
                1,
                null,
                results,
                capture.CapturePostPhysics,
                allowNoAttemptRecord: true);

            if (results.Count == 0)
            {
                capture.WriteNoRecordArtifact(Path.GetFullPath(artifactPath), loadKg);
                yield break;
            }
            Assert.That(results.Count, Is.EqualTo(1));
            capture.RetainAttemptTrace(results[0].Record.Trace);
            Assert.That(capture.SampleCount, Is.EqualTo(results[0].Record.TraceSampleCount),
                "The detailed raw trace must cover every immutable attempt observation once.");
            capture.WriteArtifact(Path.GetFullPath(artifactPath), results[0]);
        }

        private void CaptureUnexpectedError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error || condition.StartsWith("connection.state_change", System.StringComparison.Ordinal))
                return;
            _unexpectedErrors.Add(condition);
        }
    }
}
