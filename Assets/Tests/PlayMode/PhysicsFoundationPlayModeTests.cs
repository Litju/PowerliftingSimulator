using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicsFoundationPlayModeTests
    {
        private FoundationRuntime _runtime;

        [UnityTest]
        public IEnumerator CreatesAnIsolatedManualPhysicsScene()
        {
            SimulationMode simulationModeBeforeRuntime = Physics.simulationMode;
            _runtime = CreateRuntime();
            Scene mainScene = SceneManager.GetActiveScene();

            Assert.That(_runtime.IsInitialized, Is.True);
            Assert.That(_runtime.AuthoritativeScene.IsValid(), Is.True);
            Assert.That(_runtime.AuthoritativeScene, Is.Not.EqualTo(mainScene));
            Assert.That(_runtime.AuthoritativeScene.GetPhysicsScene().IsValid(), Is.True);
            Assert.That(_runtime.AuthoritativeScene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
            Assert.That(Physics.simulationMode, Is.EqualTo(simulationModeBeforeRuntime));
            Assert.That(Physics.simulationMode, Is.EqualTo(SimulationMode.FixedUpdate));
            yield return null;
        }

        [UnityTest]
        public IEnumerator BodyChangesOnlyAfterTheOwnedStep()
        {
            _runtime = CreateRuntime();
            Rigidbody body = CreateProbe(_runtime, 1f);
            Vector3 positionBeforeStep = body.position;

            yield return null;
            Assert.That(body.position, Is.EqualTo(positionBeforeStep));

            _runtime.RegisterPrimaryBody(body, "probe");
            _runtime.StepOne();

            Assert.That(_runtime.CurrentTime.Tick, Is.EqualTo(1ul));
            Assert.That(_runtime.CurrentTime.SimulationTimeSeconds, Is.EqualTo(0.01d));
            Assert.That(body.position.y, Is.LessThan(positionBeforeStep.y));
            Assert.That(body.linearVelocity.y, Is.LessThan(0f));
            Assert.That(_runtime.CurrentObservation.HasPrimaryBody, Is.True);
            Assert.That(_runtime.CurrentObservation.SimulationTick, Is.EqualTo(1ul));
            Assert.That(_runtime.CurrentObservation.SimulationTimeSeconds, Is.EqualTo(0.01d));
        }

        [UnityTest]
        public IEnumerator RenderCatchUpIsBoundedAtFourTicks()
        {
            _runtime = CreateRuntime();

            int ticks = _runtime.AdvanceRenderFrame(0.1d);

            Assert.That(ticks, Is.EqualTo(SimulationConstants.MaxCatchUpTicksPerRenderFrame));
            Assert.That(_runtime.LastCatchUpTicks, Is.EqualTo(4));
            Assert.That(_runtime.CurrentTime.Tick, Is.EqualTo(4ul));
            Assert.That(_runtime.CurrentTime.SimulationTimeSeconds, Is.EqualTo(0.04d));
            Assert.That(_runtime.AccumulatedRenderTimeSeconds, Is.EqualTo(0d));
            yield return null;
        }

        [UnityTest]
        public IEnumerator InputRenderWindowTracksTheAuthoritativeCatchUpHorizon()
        {
            _runtime = CreateRuntime();

            _runtime.PrepareRenderFrame(0.009d);
            Assert.That(_runtime.InputRenderIntervalStartSeconds, Is.EqualTo(0d));
            Assert.That(_runtime.InputRenderIntervalEndSeconds, Is.EqualTo(0.009d));
            Assert.That(_runtime.CompleteRenderFrame(), Is.EqualTo(0));

            _runtime.PrepareRenderFrame(1d);
            Assert.That(_runtime.InputRenderIntervalStartSeconds, Is.EqualTo(0.009d));
            Assert.That(_runtime.InputRenderIntervalEndSeconds, Is.EqualTo(0.04d));
            Assert.That(_runtime.CompleteRenderFrame(), Is.EqualTo(SimulationConstants.MaxCatchUpTicksPerRenderFrame));
            Assert.That(_runtime.CurrentTime.SimulationTimeSeconds, Is.EqualTo(0.04d));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GLOBAL_FIXED_TIMESTEP_AUTHORITY()
        {
            _runtime = CreateRuntime();
            float globalFixedDeltaTimeBefore = Time.fixedDeltaTime;
            try
            {
                Assert.That(globalFixedDeltaTimeBefore, Is.EqualTo(0.02f).Within(0.000001f));
                Time.fixedDeltaTime = 1f / 30f;

                Rigidbody body = CreateProbe(_runtime, 1f);
                Vector3 positionBeforeStep = body.position;
                _runtime.RegisterPrimaryBody(body, "probe");
                _runtime.StepOne();

                Assert.That(_runtime.CurrentTime.SimulationTimeSeconds, Is.EqualTo(0.01d));
                Assert.That(body.linearVelocity.y, Is.EqualTo(-9.81f * 0.01f).Within(0.002f));
                Assert.That(body.position.y, Is.EqualTo(positionBeforeStep.y - 9.81f * 0.01f * 0.01f).Within(0.0002f));
            }
            finally
            {
                Time.fixedDeltaTime = globalFixedDeltaTimeBefore;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator MANUAL_STEP_CANNOT_CONSUME_RENDER_RESIDUAL()
        {
            _runtime = CreateRuntime();

            Assert.That(_runtime.AdvanceRenderFrame(0.005d), Is.EqualTo(0));
            Assert.Throws<InvalidOperationException>(() => _runtime.StepOne());
            yield return null;
        }

        [UnityTest]
        public IEnumerator MANUAL_STEP_CANNOT_RESUME_RENDER_MODE()
        {
            _runtime = CreateRuntime();
            _runtime.StepOne();

            Assert.Throws<InvalidOperationException>(() => _runtime.AdvanceRenderFrame(0.01d));
            yield return null;
        }

        [UnityTest]
        public IEnumerator PREPARED_RENDER_FRAME_CAN_BE_CANCELLED_AFTER_CAPTURE_FAILURE()
        {
            _runtime = CreateRuntime();
            _runtime.PrepareRenderFrame(0.005d);
            _runtime.CancelPreparedRenderFrame();

            Assert.That(_runtime.AdvanceRenderFrame(0.01d), Is.EqualTo(1));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SLOW_FRAME_TIME_DOMAIN()
        {
            _runtime = CreateRuntime();
            InputTimeDomain inputTimeDomain = _runtime.InputTimeDomain;

            Assert.That(inputTimeDomain.AdvanceRenderInterval(20d), Is.EqualTo(0d));
            double slowFrameTimestamp = inputTimeDomain.AdvanceRenderInterval(21d);
            Assert.That(inputTimeDomain.Map(21d), Is.EqualTo(slowFrameTimestamp));
            Assert.That(slowFrameTimestamp, Is.EqualTo(SimulationConstants.MaxAccumulatedTimeSeconds));
            _runtime.InputBuffer.SetContinuous(IntentAction.Drive, 0.75f, slowFrameTimestamp);
            _runtime.InputBuffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Pressed, slowFrameTimestamp);

            Assert.That(_runtime.AdvanceRenderFrame(0.1d), Is.EqualTo(SimulationConstants.MaxCatchUpTicksPerRenderFrame));
            Assert.That(_runtime.LastIntentFrame.Drive01, Is.EqualTo(0.75f));
            Assert.That(_runtime.LastIntentFrame.WasPressed(IntentAction.Confirm), Is.True);
            Assert.That(_runtime.InputBuffer.PendingSampleCount, Is.EqualTo(0));

            inputTimeDomain.AdvanceRenderInterval(21.01d);
            double freshTimestamp = inputTimeDomain.Map(21.01d);
            Assert.That(freshTimestamp, Is.EqualTo(0.05d).Within(FoundationTolerances.SimulationTimeMapping));
            _runtime.InputBuffer.SetContinuous(IntentAction.Drive, 1f, freshTimestamp);
            _runtime.InputBuffer.PushEdge(IntentAction.Abort, IntentEdgeKind.Pressed, freshTimestamp);

            Assert.That(_runtime.AdvanceRenderFrame(0.01d), Is.EqualTo(1));
            Assert.That(_runtime.LastIntentFrame.Drive01, Is.EqualTo(1f));
            Assert.That(_runtime.LastIntentFrame.WasPressed(IntentAction.Abort), Is.True);
            Assert.That(_runtime.AdvanceRenderFrame(0.01d), Is.EqualTo(1));
            Assert.That(_runtime.LastIntentFrame.WasPressed(IntentAction.Abort), Is.False);

            int maximumPendingSamples = 0;
            for (int capture = 1; capture <= 32; capture++)
            {
                double realTimestamp = 21.02d + capture * 0.01d;
                inputTimeDomain.AdvanceRenderInterval(realTimestamp);
                double mappedTimestamp = inputTimeDomain.Map(realTimestamp);
                _runtime.InputBuffer.SetContinuous(IntentAction.Brace, 1f, mappedTimestamp);
                _runtime.InputBuffer.SetContinuous(IntentAction.Yield, 0.5f, mappedTimestamp);
                _runtime.InputBuffer.SetContinuous(IntentAction.Drive, 0.5f, mappedTimestamp);
                _runtime.InputBuffer.SetContinuous(IntentAction.Balance, -0.5f, mappedTimestamp);
                _runtime.InputBuffer.SetContinuous(IntentAction.Grip, 1f, mappedTimestamp);
                if (_runtime.InputBuffer.PendingSampleCount > maximumPendingSamples)
                    maximumPendingSamples = _runtime.InputBuffer.PendingSampleCount;

                Assert.That(_runtime.AdvanceRenderFrame(0.01d), Is.EqualTo(1));
            }

            Assert.That(maximumPendingSamples, Is.EqualTo(5));
            Assert.That(_runtime.InputBuffer.PendingSampleCount, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ObservationIsAStablePostStepCopy()
        {
            _runtime = CreateRuntime();
            Rigidbody body = CreateProbe(_runtime, 1f);
            _runtime.RegisterPrimaryBody(body, "probe");
            _runtime.StepOne();
            PhysicalObservation published = _runtime.CurrentObservation;
            Vector3 publishedPosition = body.position;

            body.position += Vector3.right;

            Assert.That(published.PrimaryBody.PositionMeters.X, Is.EqualTo(publishedPosition.x));
            Assert.That(_runtime.CurrentObservation.PrimaryBody.PositionMeters.X, Is.EqualTo(publishedPosition.x));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ResetRestoresTheProbeAndASecondRuntimeCanBeCreated()
        {
            _runtime = CreateRuntime();
            Rigidbody body = CreateProbe(_runtime, 2f);
            Vector3 initialPosition = body.position;
            _runtime.RegisterPrimaryBody(body, "probe");
            _runtime.StepOne();

            _runtime.Reset();

            Assert.That(_runtime.CurrentTime.Tick, Is.EqualTo(0ul));
            Assert.That(_runtime.CurrentObservation.HasPrimaryBody, Is.False);
            Assert.That(body.position, Is.EqualTo(initialPosition));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero));

            AsyncOperation unload = _runtime.Shutdown();
            _runtime = null;
            if (unload != null)
                yield return unload;

            _runtime = CreateRuntime();
            Assert.That(_runtime.IsInitialized, Is.True);
        }

        [UnityTest]
        public IEnumerator RESET_NONZERO_WALL_TIME()
        {
            _runtime = CreateRuntime();
            InputTimeDomain inputTimeDomain = _runtime.InputTimeDomain;
            inputTimeDomain.AdvanceRenderInterval(12d);
            inputTimeDomain.AdvanceRenderInterval(13d);
            double oldInputTimestamp = inputTimeDomain.Map(13d);
            _runtime.InputBuffer.SetContinuous(IntentAction.Drive, 0.25f, oldInputTimestamp);
            _runtime.InputBuffer.PushEdge(IntentAction.Abort, IntentEdgeKind.Pressed, oldInputTimestamp);
            for (ulong tick = 1; tick <= 4ul; tick++)
                _runtime.InputBuffer.SampleForTick(
                    tick,
                    SimulationConstants.TimeForTick(tick - 1),
                    SimulationConstants.TimeForTick(tick));

            _runtime.Reset();

            double freshInputTimestamp = inputTimeDomain.AdvanceRenderInterval(13.01d);
            Assert.That(inputTimeDomain.Map(13.01d), Is.EqualTo(freshInputTimestamp));
            _runtime.InputBuffer.SetContinuous(IntentAction.Drive, 1f, freshInputTimestamp);
            _runtime.InputBuffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Pressed, freshInputTimestamp);
            Assert.That(_runtime.AdvanceRenderFrame(0.01d), Is.EqualTo(1));

            Assert.That(freshInputTimestamp, Is.EqualTo(0d));
            Assert.That(_runtime.LastIntentFrame.Drive01, Is.EqualTo(1f));
            Assert.That(_runtime.LastIntentFrame.WasPressed(IntentAction.Confirm), Is.True);
            Assert.That(_runtime.LastIntentFrame.IsHeld(IntentAction.Abort), Is.False);
            Assert.That(_runtime.LastIntentFrame.WasPressed(IntentAction.Abort), Is.False);
            Assert.That(_runtime.InputBuffer.PendingSampleCount, Is.EqualTo(0));
            yield return null;
        }

        [UnityTest]
        public IEnumerator ShutdownLeavesTheMainSceneLoaded()
        {
            _runtime = CreateRuntime();
            Scene mainScene = SceneManager.GetActiveScene();
            AsyncOperation unload = _runtime.Shutdown();
            _runtime = null;
            if (unload != null)
                yield return unload;

            Assert.That(mainScene.IsValid(), Is.True);
            Assert.That(mainScene.isLoaded, Is.True);
            Assert.That(Physics.defaultPhysicsScene.IsValid(), Is.True);
        }

        private static FoundationRuntime CreateRuntime()
        {
            var runtime = new FoundationRuntime();
            runtime.Initialize("GAM2.FoundationPhysicsTest");
            return runtime;
        }

        private static Rigidbody CreateProbe(FoundationRuntime runtime, float heightMeters)
        {
            var probe = new GameObject("GAM2PhysicsProbe");
            SceneManager.MoveGameObjectToScene(probe, runtime.AuthoritativeScene);
            SphereCollider collider = probe.AddComponent<SphereCollider>();
            collider.radius = 0.1f;
            Rigidbody body = probe.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = true;
            body.position = new Vector3(0f, heightMeters, 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return body;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_runtime == null)
                yield break;

            AsyncOperation unload = _runtime.Shutdown();
            _runtime = null;
            if (unload != null)
                yield return unload;
        }
    }
}
