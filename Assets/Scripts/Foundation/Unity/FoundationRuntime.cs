using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Foundation.Unity
{
    public sealed class FoundationRuntime : IDisposable
    {
        public const string DefaultPhysicsSceneName = "PowerliftingSimulator.AuthoritativePhysics";

        private AuthoritativePhysicsScene _authoritativeScene;
        private PhysicsTickDriver _tickDriver;
        private InputTimeDomain _inputTimeDomain;

        public bool IsInitialized => _tickDriver != null && _authoritativeScene != null && _authoritativeScene.IsValid;

        public Scene AuthoritativeScene
        {
            get
            {
                EnsureInitialized();
                return _authoritativeScene.Scene;
            }
        }

        public IntentBuffer InputBuffer
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.InputBuffer;
            }
        }

        public InputTimeDomain InputTimeDomain
        {
            get
            {
                EnsureInitialized();
                return _inputTimeDomain;
            }
        }

        public SimulationTime CurrentTime
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.CurrentTime;
            }
        }

        public PlayerIntentFrame LastIntentFrame
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.LastIntentFrame;
            }
        }

        public PhysicalObservation CurrentObservation
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.CurrentObservation;
            }
        }

        public PhysicalObservation PreviousObservation
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.PreviousObservation;
            }
        }

        public int LastCatchUpTicks
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.LastCatchUpTicks;
            }
        }

        public double AccumulatedRenderTimeSeconds
        {
            get
            {
                EnsureInitialized();
                return _tickDriver.AccumulatedRenderTimeSeconds;
            }
        }

        public void Initialize(string physicsSceneName = DefaultPhysicsSceneName)
        {
            if (IsInitialized)
                throw new InvalidOperationException("The foundation runtime is already initialized.");

            _authoritativeScene = new AuthoritativePhysicsScene();
            _authoritativeScene.Initialize(physicsSceneName);
            _inputTimeDomain = new InputTimeDomain();
            _tickDriver = new PhysicsTickDriver(_authoritativeScene, _inputTimeDomain);
        }

        public void RegisterPrimaryBody(Rigidbody body, string bodyId)
        {
            EnsureInitialized();
            _authoritativeScene.RegisterPrimaryBody(body, bodyId);
        }

        public void StepOne()
        {
            EnsureInitialized();
            _tickDriver.StepOne();
        }

        public int AdvanceRenderFrame(double renderDeltaTimeSeconds)
        {
            EnsureInitialized();
            return _tickDriver.AdvanceRenderFrame(renderDeltaTimeSeconds);
        }

        public void Reset()
        {
            EnsureInitialized();
            _tickDriver.Reset();
        }

        public AsyncOperation Shutdown()
        {
            if (_authoritativeScene == null)
                return null;

            _tickDriver = null;
            _inputTimeDomain = null;
            AsyncOperation unloadOperation = _authoritativeScene.Shutdown();
            _authoritativeScene = null;
            return unloadOperation;
        }

        public void Dispose() => Shutdown();

        private void EnsureInitialized()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("The foundation runtime is not initialized.");
        }
    }
}
