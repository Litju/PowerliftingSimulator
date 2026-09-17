using System;
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
    public sealed class GAM12ObservationTraceIntegrationTests
    {
        private const int AttemptSampleCount = 800;
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
        public IEnumerator GAM12_P1_POST_PHYSICS_TRACE_QUALIFICATION_0KG_AND_25KG()
        {
            yield return LoadQualificationScene();

            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatObservationCollector collector = _controller.ObservationCollector;
            Assert.That(collector, Is.Not.Null);
            Assert.Throws<InvalidOperationException>(() => runtime.RegisterPostPhysicsStep((_, __, ___) => { }));

            _controller.SetLoad(0f);
            DisableSceneTickOwners();
            _controller.Adapter.StartSquat();
            for (int index = 0; index < AttemptSampleCount; index++)
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            PhysicalBodyObservation[] noTraceBaseline = CopyBodies(runtime.CurrentObservation);

            _controller.SetLoad(0f);
            _controller.Adapter.StartSquat();
            collector.BeginRecording();
            for (int index = 0; index < AttemptSampleCount; index++)
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            collector.EndRecording();

            SquatTrace unloadedTrace = collector.Trace;
            Assert.That(unloadedTrace.Count, Is.EqualTo(AttemptSampleCount));
            Assert.That(unloadedTrace.IsFrozen, Is.True);
            Assert.That(runtime.CurrentObservation.BodyCount, Is.EqualTo(noTraceBaseline.Length));
            for (int index = 0; index < unloadedTrace.Count; index++)
            {
                SquatObservationSnapshot sample = unloadedTrace.GetSnapshot(index);
                AssertPostPhysicsRawSample(sample, index == 0 ? 0ul : unloadedTrace.GetSnapshot(index - 1).SimulationTick);
                Assert.That(sample.Bar.Availability, Is.EqualTo(SquatTelemetryAvailability.NOT_AVAILABLE));
                Assert.That(float.IsNaN(sample.Bar.LoadKilograms), Is.True);
                Assert.That(sample.Depth.Availability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
                AssertFiniteDepth(sample.Depth);
                AssertFiniteCom(sample.Support);
            }
            AssertBodiesEqual(noTraceBaseline, runtime.CurrentObservation);

            _controller.SetLoad(25f);
            _controller.Adapter.StartSquat();
            collector.BeginRecording();
            for (int index = 0; index < AttemptSampleCount; index++)
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            collector.EndRecording();

            SquatTrace loadedTrace = collector.Trace;
            Assert.That(loadedTrace.Count, Is.EqualTo(AttemptSampleCount));
            Assert.That(loadedTrace.IsFrozen, Is.True);
            SquatObservationSnapshot last = loadedTrace.GetSnapshot(loadedTrace.Count - 1);
            AssertPostPhysicsRawSample(last, loadedTrace.GetSnapshot(loadedTrace.Count - 2).SimulationTick);
            Assert.That(last.Bar.Availability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            AssertFinite(last.Bar.PositionWorldMeters);
            AssertFinite(last.Bar.LinearVelocityWorldMetersPerSecond);
            AssertFinite(last.Bar.AngularVelocityBarRadiansPerSecond);
            Assert.That(last.Bar.LoadKilograms, Is.EqualTo(25f).Within(0.0001f));
            Assert.That(last.Bar.SaddleAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Bar.SaddleAttached, Is.True);
            Assert.That(last.Bar.SaddleBroken, Is.False);
            Assert.That(last.Bar.SaddleSeparationMeters, Is.GreaterThanOrEqualTo(0f));
            AssertFiniteDepth(last.Depth);
            AssertFiniteCom(last.Support);
            Assert.That(last.Joints.LeftKnee.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.RightKnee.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.LeftHip.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.RightHip.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.LeftAnkle.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.RightAnkle.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.Abdomen.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(last.Joints.Thorax.JointAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));

            Assert.That(runtime.CurrentObservation.SimulationTick, Is.EqualTo(last.SimulationTick));
            Assert.That(runtime.CurrentObservation.TryGetBody("barbell", out PhysicalBodyObservation rawBar), Is.True);
            Assert.That(last.Bar.PositionWorldMeters.X, Is.EqualTo(rawBar.PositionMeters.X).Within(0.000001f));
            Assert.That(last.Bar.PositionWorldMeters.Y, Is.EqualTo(rawBar.PositionMeters.Y).Within(0.000001f));
            Assert.That(last.Bar.PositionWorldMeters.Z, Is.EqualTo(rawBar.PositionMeters.Z).Within(0.000001f));
            Assert.That(last.Bar.LinearVelocityWorldMetersPerSecond.X, Is.EqualTo(rawBar.LinearVelocityMetersPerSecond.X).Within(0.000001f));
            Assert.That(last.Bar.LinearVelocityWorldMetersPerSecond.Y, Is.EqualTo(rawBar.LinearVelocityMetersPerSecond.Y).Within(0.000001f));
            Assert.That(last.Bar.LinearVelocityWorldMetersPerSecond.Z, Is.EqualTo(rawBar.LinearVelocityMetersPerSecond.Z).Within(0.000001f));

            int frozenCount = loadedTrace.Count;
            Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            Assert.That(loadedTrace.Count, Is.EqualTo(frozenCount));
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

        private static void AssertPostPhysicsRawSample(SquatObservationSnapshot sample, ulong previousTick)
        {
            Assert.That(sample.Quality.HasFlag(SquatTelemetryQualityFlags.POST_PHYSICS), Is.True);
            Assert.That(sample.Quality.HasFlag(SquatTelemetryQualityFlags.RAW), Is.True);
            Assert.That(sample.SimulationTick, Is.GreaterThan(previousTick));
            Assert.That(sample.SimulationTimeSeconds, Is.EqualTo(sample.SimulationTick * SimulationConstants.FixedDeltaTimeSeconds).Within(0.000000001d));
            Assert.That(sample.FixedStepSeconds, Is.EqualTo(SimulationConstants.FixedDeltaTimeSeconds));
            Assert.That(sample.HasAttemptRelativeTime, Is.True);
            Assert.That(sample.AttemptRelativeTimeSeconds, Is.EqualTo((sample.SimulationTick - 1ul) * SimulationConstants.FixedDeltaTimeSeconds).Within(0.000000001d));
        }

        private static void AssertFiniteDepth(SquatDepthLandmarks depth)
        {
            AssertFinite(depth.LeftHipCreaseY);
            AssertFinite(depth.RightHipCreaseY);
            AssertFinite(depth.LeftKneeTopY);
            AssertFinite(depth.RightKneeTopY);
            AssertFinite(depth.LeftDepthM);
            AssertFinite(depth.RightDepthM);
            AssertFinite(depth.WorstSideDepthM);
        }

        private static void AssertFiniteCom(SquatSupportObservation support)
        {
            Assert.That(support.SystemComAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            AssertFinite(support.SystemComWorldMeters);
            AssertFinite(support.SystemComVelocityWorldMetersPerSecond);
            Assert.That(support.SystemMassKilograms, Is.GreaterThan(0f));
            Assert.That(support.SupportAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            if (support.HasSupport)
            {
                AssertFinite(support.SupportCenterWorldMeters);
                AssertFinite(support.SupportApMinM);
                AssertFinite(support.SupportApMaxM);
                AssertFinite(support.SupportMlMinM);
                AssertFinite(support.SupportMlMaxM);
            }
        }

        private static void AssertFinite(Vector3Value value)
        {
            Assert.That(float.IsNaN(value.X) || float.IsInfinity(value.X), Is.False);
            Assert.That(float.IsNaN(value.Y) || float.IsInfinity(value.Y), Is.False);
            Assert.That(float.IsNaN(value.Z) || float.IsInfinity(value.Z), Is.False);
        }

        private static void AssertFinite(float value)
        {
            Assert.That(float.IsNaN(value) || float.IsInfinity(value), Is.False);
        }

        private static PhysicalBodyObservation[] CopyBodies(PhysicalObservation observation)
        {
            PhysicalBodyObservation[] copy = new PhysicalBodyObservation[observation.BodyCount];
            for (int index = 0; index < copy.Length; index++)
                copy[index] = observation.BodyAt(index);
            return copy;
        }

        private static void AssertBodiesEqual(PhysicalBodyObservation[] baseline, PhysicalObservation current)
        {
            Assert.That(current.BodyCount, Is.EqualTo(baseline.Length));
            for (int index = 0; index < baseline.Length; index++)
            {
                PhysicalBodyObservation expected = baseline[index];
                PhysicalBodyObservation actual = current.BodyAt(index);
                Assert.That(actual.BodyId, Is.EqualTo(expected.BodyId));
                Assert.That(actual.PositionMeters.X, Is.EqualTo(expected.PositionMeters.X).Within(0.0001f));
                Assert.That(actual.PositionMeters.Y, Is.EqualTo(expected.PositionMeters.Y).Within(0.0001f));
                Assert.That(actual.PositionMeters.Z, Is.EqualTo(expected.PositionMeters.Z).Within(0.0001f));
                Assert.That(actual.LinearVelocityMetersPerSecond.X, Is.EqualTo(expected.LinearVelocityMetersPerSecond.X).Within(0.0001f));
                Assert.That(actual.LinearVelocityMetersPerSecond.Y, Is.EqualTo(expected.LinearVelocityMetersPerSecond.Y).Within(0.0001f));
                Assert.That(actual.LinearVelocityMetersPerSecond.Z, Is.EqualTo(expected.LinearVelocityMetersPerSecond.Z).Within(0.0001f));
            }
        }
    }
}
