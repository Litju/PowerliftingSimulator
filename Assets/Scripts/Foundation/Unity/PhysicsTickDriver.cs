using System;
using UnityEngine;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Foundation.Unity
{
    public sealed class PhysicsTickDriver
    {
        private readonly AuthoritativePhysicsScene _authoritativeScene;
        private readonly IntentBuffer _intentBuffer;
        private readonly SimulationClock _clock;
        private readonly ObservationExchange _observations;
        private double _accumulatedRenderTimeSeconds;
        private bool _stepInProgress;

        internal PhysicsTickDriver(AuthoritativePhysicsScene authoritativeScene)
        {
            _authoritativeScene = authoritativeScene ?? throw new ArgumentNullException(nameof(authoritativeScene));
            _intentBuffer = new IntentBuffer();
            _clock = new SimulationClock();
            _observations = new ObservationExchange();
        }

        public IntentBuffer InputBuffer => _intentBuffer;

        public SimulationTime CurrentTime => _clock.Current;

        public PlayerIntentFrame LastIntentFrame { get; private set; } = PlayerIntentFrame.Empty;

        public PhysicalObservation CurrentObservation => _observations.Current;

        public PhysicalObservation PreviousObservation => _observations.Previous;

        public int LastCatchUpTicks { get; private set; }

        public double AccumulatedRenderTimeSeconds => _accumulatedRenderTimeSeconds;

        public void StepOne()
        {
            if (!_authoritativeScene.IsValid)
                throw new InvalidOperationException("Cannot step an invalid authoritative physics scene.");
            if (_stepInProgress)
                throw new InvalidOperationException("A physics tick cannot be re-entered.");

            _stepInProgress = true;
            try
            {
                double tickStartSeconds = _clock.Current.SimulationTimeSeconds;
                SimulationTime time = _clock.Advance();
                LastIntentFrame = _intentBuffer.SampleForTick(time.Tick, tickStartSeconds, time.SimulationTimeSeconds);

                _authoritativeScene.PhysicsSceneHandle.Simulate((float)SimulationConstants.FixedDeltaTimeSeconds);

                _observations.Publish(_authoritativeScene.CaptureObservation(time));
            }
            finally
            {
                _stepInProgress = false;
            }
        }

        public int AdvanceRenderFrame(double renderDeltaTimeSeconds)
        {
            if (double.IsNaN(renderDeltaTimeSeconds) || double.IsInfinity(renderDeltaTimeSeconds) || renderDeltaTimeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(renderDeltaTimeSeconds));

            _accumulatedRenderTimeSeconds = Math.Min(
                _accumulatedRenderTimeSeconds + renderDeltaTimeSeconds,
                SimulationConstants.MaxAccumulatedTimeSeconds);
            int ticks = 0;
            while (_accumulatedRenderTimeSeconds + FoundationTolerances.RenderAccumulatorComparison >= SimulationConstants.FixedDeltaTimeSeconds &&
                   ticks < SimulationConstants.MaxCatchUpTicksPerRenderFrame)
            {
                StepOne();
                _accumulatedRenderTimeSeconds -= SimulationConstants.FixedDeltaTimeSeconds;
                if (_accumulatedRenderTimeSeconds < FoundationTolerances.RenderAccumulatorComparison)
                    _accumulatedRenderTimeSeconds = 0d;
                ticks++;
            }

            LastCatchUpTicks = ticks;
            return ticks;
        }

        public void Reset()
        {
            if (!_authoritativeScene.IsValid)
                throw new InvalidOperationException("Cannot reset an invalid authoritative physics scene.");

            _authoritativeScene.ResetBodies();
            _intentBuffer.Reset();
            _clock.Reset();
            _observations.Reset();
            LastIntentFrame = PlayerIntentFrame.Empty;
            LastCatchUpTicks = 0;
            _accumulatedRenderTimeSeconds = 0d;
        }

        private sealed class ObservationExchange
        {
            private PhysicalObservation _previous = PhysicalObservation.Empty(new SimulationTime(0, 0d));
            private PhysicalObservation _current = PhysicalObservation.Empty(new SimulationTime(0, 0d));

            public PhysicalObservation Previous => _previous;

            public PhysicalObservation Current => _current;

            public void Publish(PhysicalObservation observation)
            {
                _previous = _current;
                _current = observation;
            }

            public void Reset()
            {
                PhysicalObservation empty = PhysicalObservation.Empty(new SimulationTime(0, 0d));
                _previous = empty;
                _current = empty;
            }
        }
    }
}
