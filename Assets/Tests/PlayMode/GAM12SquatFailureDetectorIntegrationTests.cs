using System.Collections;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM12SquatFailureDetectorIntegrationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int SampleCount = 24;

        private FoundationBootstrap _bootstrap;
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
                Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _controller = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM12_P3_25KG_REAL_FROZEN_P1_TRACE_IS_DETERMINISTIC_AND_NO_FAILURE_IS_FABRICATED()
        {
            yield return LoadQualificationScene();
            _controller.enabled = false;
            _bootstrap.enabled = false;

            _controller.SetLoad(25f);
            _controller.Adapter.StartSquat();
            SquatObservationCollector collector = _controller.ObservationCollector;
            collector.BeginRecording();
            for (int index = 0; index < SampleCount; index++)
                Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            collector.EndRecording();

            SquatTrace trace = collector.Trace;
            Assert.That(trace.IsFrozen, Is.True);
            Assert.That(trace.Count, Is.EqualTo(SampleCount));
            SquatObservationSnapshot firstBefore = trace[0];
            SquatObservationSnapshot lastBefore = trace[trace.Count - 1];
            SquatState stateBefore = _controller.Adapter.State;
            float sqBefore = _controller.Adapter.Sq;

            SquatFailureResult first = new SquatFailureDetector().Evaluate(trace);
            SquatFailureResult second = new SquatFailureDetector().Evaluate(trace);

            Assert.That(first.HasFailure, Is.False);
            Assert.That(first.Outcome, Is.Not.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(
                first.EvidenceStatus == SquatFailureEvidenceStatus.EVALUABLE ||
                first.EvidenceStatus == SquatFailureEvidenceStatus.INCOMPLETE_ATTEMPT ||
                first.EvidenceStatus == SquatFailureEvidenceStatus.INSUFFICIENT_EVIDENCE,
                Is.True);
            Assert.That(second.EvidenceStatus, Is.EqualTo(first.EvidenceStatus));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(second.PrimaryFailureKind, Is.EqualTo(first.PrimaryFailureKind));

            Assert.That(trace.Count, Is.EqualTo(SampleCount));
            Assert.That(trace.IsFrozen, Is.True);
            Assert.That(trace[0].SimulationTick, Is.EqualTo(firstBefore.SimulationTick));
            Assert.That(trace[trace.Count - 1].SimulationTick, Is.EqualTo(lastBefore.SimulationTick));
            Assert.That(_controller.Adapter.State, Is.EqualTo(stateBefore));
            Assert.That(_controller.Adapter.Sq, Is.EqualTo(sqBefore));
            yield return null;
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            Assert.That(_controller.ObservationCollector, Is.Not.Null);
        }

    }
}
