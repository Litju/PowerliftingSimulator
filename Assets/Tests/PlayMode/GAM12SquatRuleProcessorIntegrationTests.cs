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
    public sealed class GAM12SquatRuleProcessorIntegrationTests
    {
        private const int IntegrationSampleCount = 24;
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
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _controller = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM12_P2_RULE_INTEGRATION_FIXTURE_CONSUMES_REAL_FROZEN_P1_TRACE()
        {
            yield return LoadQualificationScene();
            DisableSceneTickOwners();

            _controller.SetLoad(25f);
            _controller.Adapter.StartSquat();
            SquatObservationCollector collector = _controller.ObservationCollector;
            collector.BeginRecording();
            for (int index = 0; index < IntegrationSampleCount; index++)
                Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            collector.EndRecording();

            SquatTrace trace = collector.Trace;
            Assert.That(trace.IsFrozen, Is.True);
            Assert.That(trace.Count, Is.EqualTo(IntegrationSampleCount));
            int countBefore = trace.Count;
            SquatObservationSnapshot firstBefore = trace.GetSnapshot(0);
            SquatObservationSnapshot lastBefore = trace.GetSnapshot(trace.Count - 1);
            SquatState stateBefore = _controller.Adapter.State;
            ulong firstTick = firstBefore.SimulationTick;
            ulong lastTick = lastBefore.SimulationTick;
            SquatRuleCommandTimeline timeline = new SquatRuleCommandTimeline(
                firstTick + 1ul,
                lastTick + 1ul,
                lastTick + 2ul);

            SquatRuleProcessor processor = new SquatRuleProcessor();
            SquatAttemptJudgment firstJudgment = processor.Evaluate(trace, timeline);
            SquatAttemptJudgment secondJudgment = processor.Evaluate(trace, timeline);

            Assert.That(firstJudgment.EvidenceStatus, Is.Not.EqualTo(SquatJudgmentEvidenceStatus.INVALID_TRACE));
            Assert.That(firstJudgment.EvaluatedTraceSchema, Is.EqualTo(SquatTrace.SchemaVersion));
            Assert.That(secondJudgment.EvidenceStatus, Is.EqualTo(firstJudgment.EvidenceStatus));
            Assert.That(secondJudgment.Outcome, Is.EqualTo(firstJudgment.Outcome));
            Assert.That(secondJudgment.ViolationCount, Is.EqualTo(firstJudgment.ViolationCount));
            Assert.That(trace.Count, Is.EqualTo(countBefore));
            Assert.That(trace.IsFrozen, Is.True);
            Assert.That(trace.GetSnapshot(0).SimulationTick, Is.EqualTo(firstBefore.SimulationTick));
            Assert.That(trace.GetSnapshot(trace.Count - 1).SimulationTick, Is.EqualTo(lastBefore.SimulationTick));
            Assert.That(_controller.Adapter.State, Is.EqualTo(stateBefore));
            yield return null;
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("SquatPhysicalPrototype", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 5 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            Assert.That(_controller.ObservationCollector, Is.Not.Null);
        }

        private void DisableSceneTickOwners()
        {
            _controller.enabled = false;
            _bootstrap.enabled = false;
        }
    }
}
