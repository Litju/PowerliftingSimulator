using UnityEngine;
using UnityEngine.InputSystem;

namespace PowerliftingSimulator.Foundation.Unity
{
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class FoundationBootstrap : MonoBehaviour
    {
        [SerializeField] private InputActionAsset inputActions;
        [SerializeField] private string authoritativePhysicsSceneName = FoundationRuntime.DefaultPhysicsSceneName;

        private FoundationRuntime _runtime;
        private UnityIntentInputAdapter _inputAdapter;

        public FoundationRuntime Runtime => _runtime;

        private void Awake()
        {
            _runtime = new FoundationRuntime();
            _runtime.Initialize(authoritativePhysicsSceneName);
            _inputAdapter = new UnityIntentInputAdapter(
                inputActions,
                _runtime.InputBuffer,
                _runtime.InputTimeDomain);
            _inputAdapter.Enable();
        }

        private void Update()
        {
            if (_runtime == null || !_runtime.IsInitialized)
                return;

            _inputAdapter.Capture(Time.realtimeSinceStartupAsDouble);
            _runtime.AdvanceRenderFrame(Time.unscaledDeltaTime);
        }

        private void OnDestroy()
        {
            if (_inputAdapter != null)
            {
                _inputAdapter.Dispose();
                _inputAdapter = null;
            }

            if (_runtime != null)
            {
                _runtime.Shutdown();
                _runtime = null;
            }
        }
    }
}
