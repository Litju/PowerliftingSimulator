using System;
using UnityEngine;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Foundation.Unity
{
    public sealed class PhysicsTickDriver
    {
        private readonly AuthoritativePhysicsScene _authoritativeScene;
        private readonly IntentBuffer _intentBuffer;
        private readonly InputTimeDomain _inputTimeDomain;
        private readonly SimulationClock _clock;
        private readonly ObservationExchange _observations;
        private readonly AttemptTrace _attemptTrace;
        private double _accumulatedRenderTimeSeconds;
        private double _preparedAccumulatedRenderTimeSeconds;
        private bool _renderFramePrepared;
        private bool _renderModeStarted;
        private bool _manualSteppingMode;
        private bool _completingRenderFrame;
        private bool _stepInProgress;
        private Action<SimulationTime, PlayerIntentFrame> _prePhysicsStep;

        internal PhysicsTickDriver(AuthoritativePhysicsScene authoritativeScene, InputTimeDomain inputTimeDomain)
        {
            _authoritativeScene = authoritativeScene ?? throw new ArgumentNullException(nameof(authoritativeScene));
            _inputTimeDomain = inputTimeDomain ?? throw new ArgumentNullException(nameof(inputTimeDomain));
            _intentBuffer = new IntentBuffer();
            _clock = new SimulationClock();
            _observations = new ObservationExchange(AuthoritativePhysicsScene.MaxRegisteredBodyCount);
            _attemptTrace = new AttemptTrace();
        }

        public IntentBuffer InputBuffer => _intentBuffer;

        public SimulationTime CurrentTime => _clock.Current;

        public PlayerIntentFrame LastIntentFrame { get; private set; } = PlayerIntentFrame.Empty;

        /// <summary>
        /// Gets the immutable value view published by the most recent physics tick.
        /// The view remains valid until the next published tick; the exchange retains
        /// a third preallocated slot so current and previous views are not overwritten.
        /// Historical retention belongs to <see cref="AttemptTrace"/>, which copies
        /// observation values into its own flat storage.
        /// </summary>
        public PhysicalObservation CurrentObservation => _observations.Current;

        /// <summary>
        /// Gets the immutable value view published immediately before
        /// <see cref="CurrentObservation"/>. Its lifetime ends at the next exchange,
        /// while trace samples remain immutable for the lifetime of the trace.
        /// </summary>
        public PhysicalObservation PreviousObservation => _observations.Previous;

        public AttemptTrace AttemptTrace => _attemptTrace;

        public int LastCatchUpTicks { get; private set; }

        public double AccumulatedRenderTimeSeconds => _accumulatedRenderTimeSeconds;

        public double InputRenderIntervalStartSeconds { get; private set; }

        public double InputRenderIntervalEndSeconds { get; private set; }

        internal void RegisterPrePhysicsStep(Action<SimulationTime, PlayerIntentFrame> step)
        {
            if (step == null)
                throw new ArgumentNullException(nameof(step));
            if (_prePhysicsStep != null)
                throw new InvalidOperationException("The authoritative pre-physics step already has an owner.");

            _prePhysicsStep = step;
        }

        public void StepOne()
        {
            if (!_authoritativeScene.IsValid)
                throw new InvalidOperationException("Cannot step an invalid authoritative physics scene.");
            if (_renderFramePrepared)
                throw new InvalidOperationException("A prepared render frame must be completed before stepping manually.");
            if (_stepInProgress)
                throw new InvalidOperationException("A physics tick cannot be re-entered.");
            if (!_completingRenderFrame)
            {
                if (_renderModeStarted || _inputTimeDomain.HasEpoch ||
                    _accumulatedRenderTimeSeconds > FoundationTolerances.RenderAccumulatorComparison)
                    throw new InvalidOperationException(
                        "Manual stepping cannot be mixed with the render-frame clock or input capture.");
                _manualSteppingMode = true;
            }

            _stepInProgress = true;
            try
            {
                double tickStartSeconds = _clock.Current.SimulationTimeSeconds;
                SimulationTime time = _clock.Advance();
                LastIntentFrame = _intentBuffer.SampleForTick(time.Tick, tickStartSeconds, time.SimulationTimeSeconds);

                _prePhysicsStep?.Invoke(time, LastIntentFrame);

                _authoritativeScene.PhysicsSceneHandle.Simulate((float)SimulationConstants.FixedDeltaTimeSeconds);

                _attemptTrace.ConfigureRegisteredBodyCount(_authoritativeScene.RegisteredBodyCount);
                PhysicalObservation observation = _authoritativeScene.CaptureObservation(
                    time,
                    _observations.AcquireWriteStorage());
                _observations.Publish(observation);
                if (_attemptTrace.IsRecording)
                    _attemptTrace.Append(observation, LastIntentFrame);
            }
            finally
            {
                _stepInProgress = false;
            }
        }

        public int AdvanceRenderFrame(double renderDeltaTimeSeconds)
        {
            PrepareRenderFrame(renderDeltaTimeSeconds);
            return CompleteRenderFrame();
        }

        public void PrepareRenderFrame(double renderDeltaTimeSeconds)
        {
            if (double.IsNaN(renderDeltaTimeSeconds) || double.IsInfinity(renderDeltaTimeSeconds) || renderDeltaTimeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(renderDeltaTimeSeconds));
            if (_renderFramePrepared)
                throw new InvalidOperationException("The render frame is already prepared.");
            if (_manualSteppingMode)
                throw new InvalidOperationException(
                    "Render-frame advancement cannot be mixed with manual stepping; reset before changing clock modes.");

            double simulationStartSeconds = _clock.Current.SimulationTimeSeconds + _accumulatedRenderTimeSeconds;
            _preparedAccumulatedRenderTimeSeconds = Math.Min(
                _accumulatedRenderTimeSeconds + renderDeltaTimeSeconds,
                SimulationConstants.MaxAccumulatedTimeSeconds);
            InputRenderIntervalStartSeconds = simulationStartSeconds;
            InputRenderIntervalEndSeconds = _clock.Current.SimulationTimeSeconds + _preparedAccumulatedRenderTimeSeconds;
            _renderFramePrepared = true;
            _renderModeStarted = true;
        }

        public int CompleteRenderFrame()
        {
            if (!_renderFramePrepared)
                throw new InvalidOperationException("The render frame must be prepared before it is completed.");

            _accumulatedRenderTimeSeconds = _preparedAccumulatedRenderTimeSeconds;
            _renderFramePrepared = false;
            int ticks = 0;
            _completingRenderFrame = true;
            try
            {
                while (_accumulatedRenderTimeSeconds + FoundationTolerances.RenderAccumulatorComparison >= SimulationConstants.FixedDeltaTimeSeconds &&
                       ticks < SimulationConstants.MaxCatchUpTicksPerRenderFrame)
                {
                    StepOne();
                    _accumulatedRenderTimeSeconds -= SimulationConstants.FixedDeltaTimeSeconds;
                    if (_accumulatedRenderTimeSeconds < FoundationTolerances.RenderAccumulatorComparison)
                        _accumulatedRenderTimeSeconds = 0d;
                    ticks++;
                }
            }
            finally
            {
                _completingRenderFrame = false;
            }

            LastCatchUpTicks = ticks;
            return ticks;
        }

        public void CancelPreparedRenderFrame()
        {
            if (_stepInProgress || _completingRenderFrame)
                throw new InvalidOperationException("A prepared render frame cannot be cancelled during a physics step.");
            if (!_renderFramePrepared)
                return;

            _renderFramePrepared = false;
            _preparedAccumulatedRenderTimeSeconds = _accumulatedRenderTimeSeconds;
            InputRenderIntervalStartSeconds = _clock.Current.SimulationTimeSeconds + _accumulatedRenderTimeSeconds;
            InputRenderIntervalEndSeconds = InputRenderIntervalStartSeconds;
        }

        public void Reset()
        {
            if (!_authoritativeScene.IsValid)
                throw new InvalidOperationException("Cannot reset an invalid authoritative physics scene.");

            _authoritativeScene.ResetBodies();
            _intentBuffer.Reset();
            _inputTimeDomain.Reset();
            _clock.Reset();
            _observations.Reset();
            _attemptTrace.EndRecording();
            _attemptTrace.Clear();
            LastIntentFrame = PlayerIntentFrame.Empty;
            LastCatchUpTicks = 0;
            _accumulatedRenderTimeSeconds = 0d;
            _preparedAccumulatedRenderTimeSeconds = 0d;
            _renderFramePrepared = false;
            _renderModeStarted = false;
            _manualSteppingMode = false;
            _completingRenderFrame = false;
            InputRenderIntervalStartSeconds = 0d;
            InputRenderIntervalEndSeconds = 0d;
        }

        internal void BeginAttemptTrace()
        {
            _attemptTrace.ConfigureRegisteredBodyCount(_authoritativeScene.RegisteredBodyCount);
            _attemptTrace.BeginRecording();
        }

        private sealed class ObservationExchange
        {
            private readonly PhysicalObservationStorage _slotA;
            private readonly PhysicalObservationStorage _slotB;
            private readonly PhysicalObservationStorage _slotC;
            private PhysicalObservation _previous = PhysicalObservation.Empty(new SimulationTime(0, 0d));
            private PhysicalObservation _current = PhysicalObservation.Empty(new SimulationTime(0, 0d));
            private PhysicalObservationStorage _previousStorage;
            private PhysicalObservationStorage _currentStorage;
            private PhysicalObservationStorage _writeStorage;

            public ObservationExchange(int bodyCapacity)
            {
                _slotA = new PhysicalObservationStorage(bodyCapacity);
                _slotB = new PhysicalObservationStorage(bodyCapacity);
                _slotC = new PhysicalObservationStorage(bodyCapacity);
                _writeStorage = _slotA;
            }

            public PhysicalObservation Previous => _previous;

            public PhysicalObservation Current => _current;

            public PhysicalObservationStorage AcquireWriteStorage() => _writeStorage;

            public void Publish(PhysicalObservation observation)
            {
                _previous = _current;
                _previousStorage = _currentStorage;
                _current = observation;
                _currentStorage = _writeStorage;
                _writeStorage = FindSpareStorage();
            }

            public void Reset()
            {
                PhysicalObservation empty = PhysicalObservation.Empty(new SimulationTime(0, 0d));
                _previous = empty;
                _current = empty;
                _previousStorage = null;
                _currentStorage = null;
                _writeStorage = _slotA;
            }

            private PhysicalObservationStorage FindSpareStorage()
            {
                if (_slotA != _currentStorage && _slotA != _previousStorage)
                    return _slotA;
                if (_slotB != _currentStorage && _slotB != _previousStorage)
                    return _slotB;
                return _slotC;
            }
        }
    }
}
