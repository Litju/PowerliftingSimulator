using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Foundation.Unity
{
    public sealed class AuthoritativePhysicsScene
    {
        private static AuthoritativePhysicsScene s_activeOwner;

        private Scene _scene;
        private PhysicsScene _physicsScene;
        private readonly List<RegisteredBody> _bodies = new List<RegisteredBody>();
        private Rigidbody _primaryBody;
        private string _primaryBodyId;
        private bool _initialized;

        public bool IsInitialized => _initialized;

        public bool IsValid => _initialized && _scene.IsValid() && _physicsScene.IsValid();

        public Scene Scene
        {
            get
            {
                EnsureInitialized();
                return _scene;
            }
        }

        internal PhysicsScene PhysicsSceneHandle
        {
            get
            {
                EnsureInitialized();
                return _physicsScene;
            }
        }

        public void Initialize(string sceneName)
        {
            if (_initialized)
                throw new InvalidOperationException("The authoritative physics scene is already initialized.");
            if (s_activeOwner != null)
                throw new InvalidOperationException("Only one authoritative physics scene may be active.");
            if (string.IsNullOrWhiteSpace(sceneName))
                throw new ArgumentException("A physics scene name is required.", nameof(sceneName));

            try
            {
                _scene = SceneManager.CreateScene(sceneName, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                _physicsScene = _scene.GetPhysicsScene();
                if (!_physicsScene.IsValid())
                    throw new InvalidOperationException("Unity created an invalid authoritative physics scene.");

                _initialized = true;
                s_activeOwner = this;
            }
            catch
            {
                if (_scene.IsValid())
                    SceneManager.UnloadSceneAsync(_scene);

                _scene = default(Scene);
                _physicsScene = default(PhysicsScene);
                throw;
            }
        }

        public void RegisterPrimaryBody(Rigidbody body, string bodyId)
        {
            EnsureInitialized();
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.gameObject.scene != _scene)
                throw new ArgumentException("The observed body must belong to the authoritative physics scene.", nameof(body));
            if (string.IsNullOrWhiteSpace(bodyId))
                throw new ArgumentException("A body identifier is required.", nameof(bodyId));
            if (_primaryBody != null)
                throw new InvalidOperationException("Only one primary observed body may be registered.");

            _primaryBody = body;
            _primaryBodyId = bodyId;
            RegisterBodyInternal(body, bodyId);
        }

        public void RegisterBody(Rigidbody body, string bodyId)
        {
            EnsureInitialized();
            if (body == null)
                throw new ArgumentNullException(nameof(body));
            if (body.gameObject.scene != _scene)
                throw new ArgumentException("The registered body must belong to the authoritative physics scene.", nameof(body));
            if (string.IsNullOrWhiteSpace(bodyId))
                throw new ArgumentException("A body identifier is required.", nameof(bodyId));

            RegisterBodyInternal(body, bodyId);
        }

        internal PhysicalObservation CaptureObservation(SimulationTime time)
        {
            EnsureInitialized();
            if (_bodies.Count == 0)
                return PhysicalObservation.Empty(time);

            var bodies = new PhysicalBodyObservation[_bodies.Count];
            PhysicalBodyObservation primaryBody = default(PhysicalBodyObservation);
            bool hasPrimaryBody = _primaryBody != null;
            for (int index = 0; index < _bodies.Count; index++)
            {
                RegisteredBody registered = _bodies[index];
                PhysicalBodyObservation body = CaptureBody(registered);
                bodies[index] = body;
                if (registered.Body == _primaryBody)
                    primaryBody = body;
            }

            return new PhysicalObservation(time, primaryBody, hasPrimaryBody, bodies);
        }

        private static PhysicalBodyObservation CaptureBody(RegisteredBody registered)
        {
            Rigidbody body = registered.Body;
            return new PhysicalBodyObservation(
                registered.BodyId,
                body.mass,
                new Vector3Value(body.position.x, body.position.y, body.position.z),
                new QuaternionValue(body.rotation.x, body.rotation.y, body.rotation.z, body.rotation.w),
                new Vector3Value(body.linearVelocity.x, body.linearVelocity.y, body.linearVelocity.z),
                new Vector3Value(body.angularVelocity.x, body.angularVelocity.y, body.angularVelocity.z));
        }

        internal void ResetBodies()
        {
            EnsureInitialized();
            if (_bodies.Count == 0)
                return;

            foreach (RegisteredBody registered in _bodies)
            {
                Rigidbody body = registered.Body;
                RigidbodyResetState state = registered.ResetState;
                // Unity rejects velocity writes while a Rigidbody is kinematic.
                body.isKinematic = false;
                body.transform.SetPositionAndRotation(state.Position, state.Rotation);
                body.position = state.Position;
                body.rotation = state.Rotation;
                body.linearVelocity = state.LinearVelocity;
                body.angularVelocity = state.AngularVelocity;
                body.isKinematic = state.WasKinematic;
            }
            Physics.SyncTransforms();

            foreach (RegisteredBody registered in _bodies)
            {
                if (registered.ResetState.WasSleeping)
                    registered.Body.Sleep();
                else
                    registered.Body.WakeUp();
            }
        }

        public AsyncOperation Shutdown()
        {
            if (!_initialized)
                return null;

            Scene sceneToUnload = _scene;
            _initialized = false;
            _bodies.Clear();
            _primaryBody = null;
            _primaryBodyId = null;
            _scene = default(Scene);
            _physicsScene = default(PhysicsScene);

            if (s_activeOwner == this)
                s_activeOwner = null;

            AsyncOperation unloadOperation = sceneToUnload.IsValid()
                ? SceneManager.UnloadSceneAsync(sceneToUnload)
                : null;
            return unloadOperation;
        }

        private void EnsureInitialized()
        {
            if (!_initialized || !_scene.IsValid() || !_physicsScene.IsValid())
                throw new InvalidOperationException("The authoritative physics scene is not initialized.");
        }

        private void RegisterBodyInternal(Rigidbody body, string bodyId)
        {
            for (int index = 0; index < _bodies.Count; index++)
            {
                if (_bodies[index].Body == body)
                    throw new InvalidOperationException($"Body '{body.name}' is already registered.");
                if (string.Equals(_bodies[index].BodyId, bodyId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"Body identifier '{bodyId}' is already registered.");
            }

            _bodies.Add(new RegisteredBody(body, bodyId));
        }

        private readonly struct RegisteredBody
        {
            public RegisteredBody(Rigidbody body, string bodyId)
            {
                Body = body;
                BodyId = bodyId;
                ResetState = new RigidbodyResetState(body);
            }

            public Rigidbody Body { get; }
            public string BodyId { get; }
            public RigidbodyResetState ResetState { get; }
        }

        private readonly struct RigidbodyResetState
        {
            public RigidbodyResetState(Rigidbody body)
            {
                Position = body.position;
                Rotation = body.rotation;
                LinearVelocity = body.linearVelocity;
                AngularVelocity = body.angularVelocity;
                WasSleeping = body.IsSleeping();
                WasKinematic = body.isKinematic;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 LinearVelocity { get; }
            public Vector3 AngularVelocity { get; }
            public bool WasSleeping { get; }
            public bool WasKinematic { get; }
        }
    }
}
