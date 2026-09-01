using System;

namespace PowerliftingSimulator.Foundation
{
    public readonly struct AttemptTraceSample
    {
        public AttemptTraceSample(PhysicalObservation observation, PlayerIntentFrame intent)
        {
            Observation = observation;
            Intent = intent;
        }

        public PhysicalObservation Observation { get; }
        public PlayerIntentFrame Intent { get; }
        public ulong Tick => Observation.SimulationTick;
        public double SimulationTimeSeconds => Observation.SimulationTimeSeconds;
    }

    public sealed class AttemptTrace
    {
        public const string SchemaVersion = "GAM8_ATTEMPT_TRACE_V1";
        public const int DefaultCapacity = 3000;

        private readonly AttemptTraceSample[] _samples;
        private int _count;
        private bool _hasLastTick;
        private ulong _lastTick;

        public AttemptTrace(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _samples = new AttemptTraceSample[capacity];
        }

        public int Capacity => _samples.Length;
        public int Count => _count;
        public bool IsRecording { get; private set; }
        public string Schema => SchemaVersion;

        public double DurationSeconds => _count < 2
            ? 0d
            : _samples[_count - 1].SimulationTimeSeconds - _samples[0].SimulationTimeSeconds;

        public void BeginRecording()
        {
            if (IsRecording)
                throw new InvalidOperationException("The attempt trace is already recording.");

            Clear();
            IsRecording = true;
        }

        public void EndRecording() => IsRecording = false;

        public void Clear()
        {
            _count = 0;
            _hasLastTick = false;
            _lastTick = 0ul;
        }

        public AttemptTraceSample GetSample(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _samples[index];
        }

        public void Append(PhysicalObservation observation, PlayerIntentFrame intent)
        {
            if (!IsRecording)
                throw new InvalidOperationException("Attempt trace samples can only be appended while recording.");
            if (_count == _samples.Length)
                throw new InvalidOperationException("The bounded attempt trace is full; the decisive state was not overwritten.");
            if (_hasLastTick && observation.SimulationTick <= _lastTick)
                throw new InvalidOperationException("Attempt trace samples must have strictly increasing ticks.");
            if (intent.Tick != observation.SimulationTick)
                throw new ArgumentException("The sampled intent and physical observation must belong to the same tick.", nameof(intent));

            _samples[_count] = new AttemptTraceSample(observation, intent);
            _count++;
            _lastTick = observation.SimulationTick;
            _hasLastTick = true;
        }
    }
}
