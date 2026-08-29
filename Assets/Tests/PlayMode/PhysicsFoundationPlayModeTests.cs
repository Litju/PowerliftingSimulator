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
            _runtime = CreateRuntime();
            Scene mainScene = SceneManager.GetActiveScene();

            Assert.That(_runtime.IsInitialized, Is.True);
            Assert.That(_runtime.AuthoritativeScene.IsValid(), Is.True);
            Assert.That(_runtime.AuthoritativeScene, Is.Not.EqualTo(mainScene));
            Assert.That(_runtime.AuthoritativeScene.GetPhysicsScene().IsValid(), Is.True);
            Assert.That(_runtime.AuthoritativeScene.GetPhysicsScene(), Is.Not.EqualTo(Physics.defaultPhysicsScene));
            Assert.That(Physics.simulationMode, Is.EqualTo(SimulationMode.Script));
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
