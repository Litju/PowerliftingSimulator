using System;
using UnityEngine;
using UnityEngine.InputSystem;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Foundation.Unity
{
    public sealed class UnityIntentInputAdapter : IDisposable
    {
        private readonly InputActionAsset _actions;
        private readonly IntentBuffer _intentBuffer;
        private InputActionMap _gameplayMap;
        private InputAction _brace;
        private InputAction _yield;
        private InputAction _drive;
        private InputAction _balance;
        private InputAction _grip;
        private readonly InputTimeDomain _inputTimeDomain;
        private bool _enabled;

        public UnityIntentInputAdapter(InputActionAsset actions, IntentBuffer intentBuffer, InputTimeDomain inputTimeDomain)
        {
            _actions = actions;
            _intentBuffer = intentBuffer ?? throw new ArgumentNullException(nameof(intentBuffer));
            _inputTimeDomain = inputTimeDomain ?? throw new ArgumentNullException(nameof(inputTimeDomain));
        }

        public void Enable()
        {
            if (_enabled || _actions == null)
                return;

            _gameplayMap = _actions.FindActionMap("Gameplay", true);
            _brace = _gameplayMap.FindAction("Brace", true);
            _yield = _gameplayMap.FindAction("Yield", true);
            _drive = _gameplayMap.FindAction("Drive", true);
            _balance = _gameplayMap.FindAction("Balance", true);
            _grip = _gameplayMap.FindAction("Grip", true);
            _gameplayMap.actionTriggered += OnActionTriggered;
            _gameplayMap.Enable();
            _enabled = true;
        }

        public void Capture(double timestampSeconds)
        {
            if (!_enabled)
                return;

            double simulationTimestampSeconds = _inputTimeDomain.Map(timestampSeconds);
            _intentBuffer.SetContinuous(IntentAction.Brace, _brace.ReadValue<float>(), simulationTimestampSeconds);
            _intentBuffer.SetContinuous(IntentAction.Yield, _yield.ReadValue<float>(), simulationTimestampSeconds);
            _intentBuffer.SetContinuous(IntentAction.Drive, _drive.ReadValue<float>(), simulationTimestampSeconds);
            _intentBuffer.SetContinuous(IntentAction.Balance, _balance.ReadValue<float>(), simulationTimestampSeconds);
            _intentBuffer.SetContinuous(IntentAction.Grip, _grip.ReadValue<float>(), simulationTimestampSeconds);
        }

        public void Disable()
        {
            if (!_enabled)
                return;

            _gameplayMap.actionTriggered -= OnActionTriggered;
            _gameplayMap.Disable();
            _enabled = false;
        }

        public void Dispose() => Disable();

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            IntentAction action = ToIntentAction(context.action.name);
            double simulationTimestampSeconds = _inputTimeDomain.Map(context.time);
            if (context.phase == InputActionPhase.Started)
                _intentBuffer.PushEdge(action, IntentEdgeKind.Pressed, simulationTimestampSeconds);
            else if (context.phase == InputActionPhase.Canceled)
                _intentBuffer.PushEdge(action, IntentEdgeKind.Released, simulationTimestampSeconds);
            else if (context.phase == InputActionPhase.Performed && IsContinuous(action))
                _intentBuffer.SetContinuous(action, context.ReadValue<float>(), simulationTimestampSeconds);
        }

        private static bool IsContinuous(IntentAction action) => action != IntentAction.Confirm && action != IntentAction.Abort;

        private static IntentAction ToIntentAction(string actionName)
        {
            return actionName switch
            {
                "Brace" => IntentAction.Brace,
                "Yield" => IntentAction.Yield,
                "Drive" => IntentAction.Drive,
                "Balance" => IntentAction.Balance,
                "Grip" => IntentAction.Grip,
                "Confirm" => IntentAction.Confirm,
                "Abort" => IntentAction.Abort,
                _ => throw new ArgumentOutOfRangeException(nameof(actionName), actionName, null)
            };
        }
    }
}
