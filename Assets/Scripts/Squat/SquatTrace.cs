using System;

namespace PowerliftingSimulator.Squat
{
    /// <summary>
    /// Fixed-capacity append-only storage for raw squat observation snapshots.
    /// The trace owns only value records and never exposes its backing array.
    /// </summary>
    public sealed class SquatTrace
    {
        public const string SchemaVersion = "GAM12_SQUAT_TRACE_V1";
        public const int DefaultCapacity = 3000;

        private readonly SquatObservationSnapshot[] _snapshots;
        private int _count;
        private bool _hasLast;
        private ulong _lastTick;
        private double _lastTimeSeconds;

        public SquatTrace(int capacity = DefaultCapacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            _snapshots = new SquatObservationSnapshot[capacity];
        }

        public int Capacity => _snapshots.Length;
        public int Count => _count;
        public bool IsRecording { get; private set; }
        public bool IsFrozen { get; private set; }
        public string Schema => SchemaVersion;

        public double DurationSeconds => _count < 2
            ? 0d
            : _snapshots[_count - 1].SimulationTimeSeconds - _snapshots[0].SimulationTimeSeconds;

        public void BeginRecording()
        {
            if (IsRecording)
                throw new InvalidOperationException("The squat trace is already recording.");

            Clear();
            IsRecording = true;
            IsFrozen = false;
        }

        public void EndRecording()
        {
            IsRecording = false;
            IsFrozen = true;
        }

        public void Clear()
        {
            if (IsRecording)
                throw new InvalidOperationException("A recording squat trace must be finalized before it is cleared.");

            _count = 0;
            _hasLast = false;
            _lastTick = 0ul;
            _lastTimeSeconds = 0d;
            IsFrozen = false;
        }

        public SquatObservationSnapshot GetSnapshot(int index)
        {
            if (index < 0 || index >= _count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _snapshots[index];
        }

        public SquatObservationSnapshot this[int index] => GetSnapshot(index);

        public void Append(SquatObservationSnapshot snapshot)
        {
            if (!IsRecording || IsFrozen)
                throw new InvalidOperationException("Squat trace samples can only be appended while recording.");
            if (_count == _snapshots.Length)
                throw new InvalidOperationException("The bounded squat trace is full; no historical sample was overwritten.");
            if (_hasLast && snapshot.SimulationTick <= _lastTick)
                throw new InvalidOperationException("Squat trace simulation ticks must be strictly increasing.");
            if (_hasLast && snapshot.SimulationTimeSeconds <= _lastTimeSeconds)
                throw new InvalidOperationException("Squat trace simulation time must be strictly increasing.");
            if (!string.Equals(snapshot.Schema, SquatObservationSnapshot.SchemaId, StringComparison.Ordinal))
                throw new InvalidOperationException("The snapshot schema is incompatible with the squat trace.");

            _snapshots[_count] = snapshot;
            _count++;
            _lastTick = snapshot.SimulationTick;
            _lastTimeSeconds = snapshot.SimulationTimeSeconds;
            _hasLast = true;
        }
    }
}
