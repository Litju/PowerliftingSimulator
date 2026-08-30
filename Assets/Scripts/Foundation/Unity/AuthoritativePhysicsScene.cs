using System;
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
        private Rigidbody _primaryBody;
        private RigidbodyResetState _primaryBodyResetState;
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
                throw new InvalidOperationException("The foundation probe supports one registered primary body.");

            _primaryBody = body;
            _primaryBodyId = bodyId;
            _primaryBodyResetState = new RigidbodyResetState(body);
        }

        internal PhysicalObservation CaptureObservation(SimulationTime time)
        {
            EnsureInitialized();
            if (_primaryBody == null)
                return PhysicalObservation.Empty(time);

            QuaternionValue rotation = new QuaternionValue(
                _primaryBody.rotation.x,
                _primaryBody.rotation.y,
                _primaryBody.rotation.z,
                _primaryBody.rotation.w);
            PhysicalBodyObservation body = new PhysicalBodyObservation(
                _primaryBodyId,
                _primaryBody.mass,
                new Vector3Value(_primaryBody.position.x, _primaryBody.position.y, _primaryBody.position.z),
                rotation,
                new Vector3Value(
                    _primaryBody.linearVelocity.x,
                    _primaryBody.linearVelocity.y,
                    _primaryBody.linearVelocity.z),
                new Vector3Value(
                    _primaryBody.angularVelocity.x,
                    _primaryBody.angularVelocity.y,
                    _primaryBody.angularVelocity.z));
            return new PhysicalObservation(time, body, true);
        }

        internal void ResetBodies()
        {
            EnsureInitialized();
            if (_primaryBody == null)
                return;

            _primaryBody.position = _primaryBodyResetState.Position;
            _primaryBody.rotation = _primaryBodyResetState.Rotation;
            _primaryBody.linearVelocity = _primaryBodyResetState.LinearVelocity;
            _primaryBody.angularVelocity = _primaryBodyResetState.AngularVelocity;
            Physics.SyncTransforms();

            if (_primaryBodyResetState.WasSleeping)
                _primaryBody.Sleep();
            else
                _primaryBody.WakeUp();
        }

        public AsyncOperation Shutdown()
        {
            if (!_initialized)
                return null;

            Scene sceneToUnload = _scene;
            _initialized = false;
            _primaryBody = null;
            _primaryBodyId = null;
            _primaryBodyResetState = default(RigidbodyResetState);
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

        private readonly struct RigidbodyResetState
        {
            public RigidbodyResetState(Rigidbody body)
            {
                Position = body.position;
                Rotation = body.rotation;
                LinearVelocity = body.linearVelocity;
                AngularVelocity = body.angularVelocity;
                WasSleeping = body.IsSleeping();
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 LinearVelocity { get; }
            public Vector3 AngularVelocity { get; }
            public bool WasSleeping { get; }
        }
    }
}
