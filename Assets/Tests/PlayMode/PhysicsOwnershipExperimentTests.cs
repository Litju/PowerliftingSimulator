using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicsOwnershipExperimentTests
    {
        private const float StepSeconds = 0.01f;
        private const float Gravity = -9.81f;
        private Scene _localScene;
        private Rigidbody _defaultBody;
        private Rigidbody _defaultControlBody;
        private Rigidbody _localBody;
        private FixedStepCounter _defaultStepCounter;
        private SimulationMode _previousSimulationMode;

        [UnityTest]
        public IEnumerator AutomaticDefaultSceneAndExplicitLocalSceneAdvanceIndependently()
        {
            BeginFixture(SimulationMode.FixedUpdate);
            CreateBodies();

            Vector3 defaultPositionBefore = _defaultBody.position;
            Vector3 localPositionBefore = _localBody.position;
            PhysicsScene localPhysicsScene = _localScene.GetPhysicsScene();
            int defaultStepsBefore = _defaultStepCounter.StepCount;

            localPhysicsScene.Simulate(StepSeconds);

            Assert.That(localPhysicsScene.IsValid(), Is.True);
            Assert.That(_localBody.linearVelocity.y, Is.EqualTo(Gravity * StepSeconds).Within(0.002f));
            Assert.That(_localBody.position.y, Is.EqualTo(localPositionBefore.y + Gravity * StepSeconds * StepSeconds).Within(0.0002f));

            yield return new WaitForFixedUpdate();
            yield return null;

            int defaultSteps = _defaultStepCounter.StepCount - defaultStepsBefore;
            Assert.That(defaultSteps, Is.GreaterThan(0));
            float defaultStepSeconds = Time.fixedDeltaTime;
            Assert.That(_defaultBody.linearVelocity.y, Is.EqualTo(Gravity * defaultStepSeconds * defaultSteps).Within(0.002f));
            Assert.That(_defaultBody.position.y, Is.LessThan(defaultPositionBefore.y));
            Assert.That(Physics.simulationMode, Is.EqualTo(SimulationMode.FixedUpdate));
        }

        [UnityTest]
        public IEnumerator BoundedScriptTransitionDoesNotDoubleStepDefaultScene()
        {
            BeginFixture(SimulationMode.FixedUpdate);
            CreateBodies();

            Vector3 localPositionBefore = _localBody.position;
            PhysicsScene localPhysicsScene = _localScene.GetPhysicsScene();
            int defaultStepsBefore = _defaultStepCounter.StepCount;

            Physics.simulationMode = SimulationMode.Script;
            localPhysicsScene.Simulate(StepSeconds);

            Assert.That(_localBody.linearVelocity.y, Is.EqualTo(Gravity * StepSeconds).Within(0.002f));
            Assert.That(_localBody.position.y, Is.EqualTo(localPositionBefore.y + Gravity * StepSeconds * StepSeconds).Within(0.0002f));

            Physics.simulationMode = SimulationMode.FixedUpdate;

            yield return new WaitForFixedUpdate();
            yield return null;

            int defaultSteps = _defaultStepCounter.StepCount - defaultStepsBefore;
            Assert.That(defaultSteps, Is.GreaterThan(0));
            float defaultStepSeconds = Time.fixedDeltaTime;
            Assert.That(_defaultBody.linearVelocity.y, Is.EqualTo(Gravity * defaultStepSeconds * defaultSteps).Within(0.002f));
            Assert.That(_defaultBody.linearVelocity.y, Is.EqualTo(_defaultControlBody.linearVelocity.y).Within(0.0002f));
            Assert.That(_defaultBody.position.y, Is.EqualTo(_defaultControlBody.position.y).Within(0.0002f));
            Assert.That(Physics.simulationMode, Is.EqualTo(SimulationMode.FixedUpdate));
        }

        private void BeginFixture(SimulationMode simulationMode)
        {
            _previousSimulationMode = Physics.simulationMode;
            Physics.simulationMode = simulationMode;
            _localScene = SceneManager.CreateScene(
                "GAM2.PhysicsOwnershipExperiment",
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        }

        private void CreateBodies()
        {
            _defaultStepCounter = new GameObject("GAM2.DefaultPhysicsStepCounter").AddComponent<FixedStepCounter>();
            _defaultBody = CreateBody("GAM2.DefaultPhysicsProbe", SceneManager.GetActiveScene());
            _defaultControlBody = CreateBody("GAM2.DefaultPhysicsControlProbe", SceneManager.GetActiveScene());
            _defaultControlBody.position = new Vector3(2f, 5f, 0f);
            _localBody = CreateBody("GAM2.LocalPhysicsProbe", _localScene);
            Physics.SyncTransforms();
        }

        private static Rigidbody CreateBody(string name, Scene scene)
        {
            var gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            SphereCollider collider = gameObject.AddComponent<SphereCollider>();
            collider.radius = 0.1f;
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.mass = 1f;
            body.useGravity = true;
            body.position = new Vector3(0f, 5f, 0f);
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            return body;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Physics.simulationMode = SimulationMode.Script;

            if (_defaultBody != null)
                Object.Destroy(_defaultBody.gameObject);
            if (_defaultControlBody != null)
                Object.Destroy(_defaultControlBody.gameObject);
            if (_localBody != null)
                Object.Destroy(_localBody.gameObject);
            if (_defaultStepCounter != null)
                Object.Destroy(_defaultStepCounter.gameObject);

            yield return null;

            if (_localScene.IsValid())
                yield return SceneManager.UnloadSceneAsync(_localScene);

            Physics.simulationMode = _previousSimulationMode;
            _defaultBody = null;
            _defaultControlBody = null;
            _localBody = null;
            _defaultStepCounter = null;
            _localScene = default(Scene);
        }

        private sealed class FixedStepCounter : MonoBehaviour
        {
            public int StepCount { get; private set; }

            private void FixedUpdate() => StepCount++;
        }
    }
}
