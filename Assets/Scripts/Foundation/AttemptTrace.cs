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
        public const int EstimatedBodyRecordStorageBytes = 64;
        public const int EstimatedTraceSampleStorageBytes = 64;

        private readonly AttemptTraceSample[] _samples;
        private readonly PhysicalObservationStorage _bodyStorage;
        private readonly int _bodyCapacity;
        private int _count;
        private int _registeredBodyCount;
        private bool _hasLastTick;
        private ulong _lastTick;

        public AttemptTrace(int capacity = DefaultCapacity)
            : this(capacity, PhysicalObservationStorage.DefaultBodyCapacity)
        {
        }

        public AttemptTrace(int capacity, int bodyCapacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (bodyCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(bodyCapacity));

            _samples = new AttemptTraceSample[capacity];
            _bodyCapacity = bodyCapacity;
            try
            {
                _bodyStorage = new PhysicalObservationStorage(checked(capacity * bodyCapacity));
            }
            catch (OverflowException exception)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity),
                    "The bounded attempt trace storage size is too large: " + exception.Message);
            }
        }

        public int Capacity => _samples.Length;
        public int Count => _count;
        public int BodyCapacity => _bodyCapacity;
        public int RegisteredBodyCount => _registeredBodyCount;
        public int ReservedBodyRecordCount => _bodyStorage.Capacity;
        public long ReservedBodyRecordStorageBytes =>
            (long)ReservedBodyRecordCount * EstimatedBodyRecordStorageBytes;
        public long ReservedStorageEstimateBytes =>
            ReservedBodyRecordStorageBytes + (long)Capacity * EstimatedTraceSampleStorageBytes;
        public long LogicalPayloadStorageBytes =>
            (long)Capacity * _registeredBodyCount * EstimatedBodyRecordStorageBytes;
        public long CurrentLogicalPayloadStorageBytes =>
            (long)Count * _registeredBodyCount * EstimatedBodyRecordStorageBytes;
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

        internal void ConfigureRegisteredBodyCount(int registeredBodyCount)
        {
            if (registeredBodyCount < 0 || registeredBodyCount > _bodyCapacity)
                throw new ArgumentOutOfRangeException(nameof(registeredBodyCount));
            if (_registeredBodyCount != 0 && _registeredBodyCount != registeredBodyCount)
                throw new InvalidOperationException("The attempt trace body count cannot change after storage preparation.");

            _registeredBodyCount = registeredBodyCount;
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

            if (_registeredBodyCount == 0)
                _registeredBodyCount = observation.BodyCount;
            if (observation.BodyCount != _registeredBodyCount)
                throw new InvalidOperationException("Attempt trace samples must keep the prepared registered body count.");
            if (observation.BodyCount > _bodyCapacity)
                throw new InvalidOperationException("The registered body count exceeds the bounded attempt trace storage capacity.");

            int bodyOffset = _count * _bodyCapacity;
            for (int bodyIndex = 0; bodyIndex < observation.BodyCount; bodyIndex++)
                _bodyStorage.Set(bodyOffset + bodyIndex, observation.BodyAt(bodyIndex));

            PhysicalObservation immutableObservation = observation.CopyWithStorage(
                _bodyStorage,
                bodyOffset,
                observation.BodyCount);
            _samples[_count] = new AttemptTraceSample(immutableObservation, intent);
            _count++;
            _lastTick = observation.SimulationTick;
            _hasLastTick = true;
        }
    }
}
