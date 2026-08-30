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
        // Edge capacity remains the foundation's existing 64-event ring. A
        // separate bounded continuous staging area prevents high-rate axis
        // callbacks from consuming edge capacity before interval closure.
        private readonly PendingInput[] _pendingEdges = new PendingInput[IntentBuffer.DefaultEdgeCapacity];
        private readonly PendingInput[] _pendingContinuous = new PendingInput[IntentBuffer.MaxContinuousPendingSampleCount];
        private readonly IntentInputSample[] _mappedInputs = new IntentInputSample[IntentBuffer.MaxBatchInputCount];
        private int _pendingEdgeCount;
        private int _pendingContinuousCount;
        private long _nextSequence;
        private bool _hasQueuedRealTimestamp;
        private double _lastQueuedRealTimestampSeconds;
        private bool _hasResetRealTimestamp;
        private double _resetRealTimestampSeconds;
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
            if (!_inputTimeDomain.HasEpoch)
                _inputTimeDomain.AdvanceRenderInterval(Time.realtimeSinceStartupAsDouble);
            _gameplayMap.actionTriggered += OnActionTriggered;
            _gameplayMap.Enable();
            _enabled = true;
        }

        public void Capture(
            double timestampSeconds,
            double authoritativeSimulationStartSeconds,
            double authoritativeSimulationEndSeconds)
        {
            if (!_enabled)
                return;

            InputTimeDomain.Checkpoint checkpoint = _inputTimeDomain.CaptureCheckpoint();
            try
            {
                _inputTimeDomain.AdvanceRenderInterval(
                    timestampSeconds,
                    authoritativeSimulationStartSeconds,
                    authoritativeSimulationEndSeconds);
                CapturePendingInputs(timestampSeconds);
            }
            catch
            {
                _inputTimeDomain.RestoreCheckpoint(checkpoint);
                throw;
            }
        }

        private void CapturePendingInputs(double timestampSeconds)
        {
            int edgeIndex = 0;
            int continuousIndex = 0;
            int mappedInputCount = 0;
            while (edgeIndex < _pendingEdgeCount || continuousIndex < _pendingContinuousCount)
            {
                PendingInput input;
                if (continuousIndex == _pendingContinuousCount ||
                    (edgeIndex < _pendingEdgeCount &&
                     _pendingEdges[edgeIndex].Sequence < _pendingContinuous[continuousIndex].Sequence))
                {
                    input = _pendingEdges[edgeIndex++];
                }
                else
                {
                    input = _pendingContinuous[continuousIndex++];
                }

                double simulationTimestampSeconds = _inputTimeDomain.Map(input.RealTimestampSeconds);
                _mappedInputs[mappedInputCount++] = input.IsEdge
                    ? IntentInputSample.Edge(input.Action, input.EdgeKind, simulationTimestampSeconds)
                    : IntentInputSample.Continuous(input.Action, input.Value, simulationTimestampSeconds);
            }
            double simulationTimestampSecondsAtCapture = _inputTimeDomain.Map(timestampSeconds);
            _mappedInputs[mappedInputCount++] = IntentInputSample.Continuous(
                IntentAction.Brace, _brace.ReadValue<float>(), simulationTimestampSecondsAtCapture);
            _mappedInputs[mappedInputCount++] = IntentInputSample.Continuous(
                IntentAction.Yield, _yield.ReadValue<float>(), simulationTimestampSecondsAtCapture);
            _mappedInputs[mappedInputCount++] = IntentInputSample.Continuous(
                IntentAction.Drive, _drive.ReadValue<float>(), simulationTimestampSecondsAtCapture);
            _mappedInputs[mappedInputCount++] = IntentInputSample.Continuous(
                IntentAction.Balance, _balance.ReadValue<float>(), simulationTimestampSecondsAtCapture);
            _mappedInputs[mappedInputCount++] = IntentInputSample.Continuous(
                IntentAction.Grip, _grip.ReadValue<float>(), simulationTimestampSecondsAtCapture);

            // ApplyBatch validates the complete mapped set before mutating the
            // buffer. Staged callbacks remain available if delivery is rejected.
            _intentBuffer.ApplyBatch(_mappedInputs, mappedInputCount);
            ClearPendingInputs();
        }

        public void Disable()
        {
            if (!_enabled)
                return;

            _gameplayMap.actionTriggered -= OnActionTriggered;
            _gameplayMap.Disable();
            ClearPendingInputs();
            _enabled = false;
        }

        public void Dispose() => Disable();

        public void Reset()
        {
            ClearPendingInputs();
            _nextSequence = 0;
            _hasQueuedRealTimestamp = false;
            _lastQueuedRealTimestampSeconds = 0d;
            _resetRealTimestampSeconds = Time.realtimeSinceStartupAsDouble;
            _hasResetRealTimestamp = true;
            if (_enabled && !_inputTimeDomain.HasEpoch)
                _inputTimeDomain.AdvanceRenderInterval(Time.realtimeSinceStartupAsDouble);
        }

        private void OnActionTriggered(InputAction.CallbackContext context)
        {
            IntentAction action = ToIntentAction(context.action.name);
            if (context.phase == InputActionPhase.Started)
                Queue(new PendingInput(action, IntentEdgeKind.Pressed, 0f, context.time, true));
            else if (context.phase == InputActionPhase.Canceled)
                Queue(new PendingInput(action, IntentEdgeKind.Released, 0f, context.time, true));
            else if (context.phase == InputActionPhase.Performed && IsContinuous(action))
                Queue(new PendingInput(action, IntentEdgeKind.Pressed, context.ReadValue<float>(), context.time, false));
        }

        private void Queue(PendingInput input)
        {
            if (_hasResetRealTimestamp && input.RealTimestampSeconds < _resetRealTimestampSeconds)
                return;
            if (_hasQueuedRealTimestamp && input.RealTimestampSeconds < _lastQueuedRealTimestampSeconds)
                throw new InvalidOperationException("Input callbacks must be received in timestamp order.");
            input = input.WithSequence(_nextSequence++);
            if (input.IsEdge)
            {
                if (_pendingEdgeCount == _pendingEdges.Length)
                    throw new InvalidOperationException("Pending Unity edge capacity exceeded.");
                _pendingEdges[_pendingEdgeCount++] = input;
            }
            else
            {
                if (_pendingContinuousCount == _pendingContinuous.Length)
                    throw new InvalidOperationException(
                        "Pending Unity continuous capacity exceeded before interval mapping; no callback is evicted.");
                _pendingContinuous[_pendingContinuousCount++] = input;
            }

            _lastQueuedRealTimestampSeconds = input.RealTimestampSeconds;
            _hasQueuedRealTimestamp = true;
        }

        private void ClearPendingInputs()
        {
            _pendingEdgeCount = 0;
            _pendingContinuousCount = 0;
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

        private readonly struct PendingInput
        {
            public PendingInput(
                IntentAction action,
                IntentEdgeKind edgeKind,
                float value,
                double realTimestampSeconds,
                bool isEdge,
                long sequence = 0)
            {
                Action = action;
                EdgeKind = edgeKind;
                Value = value;
                RealTimestampSeconds = realTimestampSeconds;
                IsEdge = isEdge;
                Sequence = sequence;
            }

            public PendingInput WithSequence(long sequence) =>
                new PendingInput(Action, EdgeKind, Value, RealTimestampSeconds, IsEdge, sequence);

            public IntentAction Action { get; }
            public IntentEdgeKind EdgeKind { get; }
            public float Value { get; }
            public double RealTimestampSeconds { get; }
            public bool IsEdge { get; }
            public long Sequence { get; }
        }
    }
}
