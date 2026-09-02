using System;
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("PowerliftingSimulator.Foundation.Unity")]

namespace PowerliftingSimulator.Foundation
{
    public readonly struct PhysicalBodyObservation
    {
        public PhysicalBodyObservation(
            string bodyId,
            float massKilograms,
            Vector3Value positionMeters,
            QuaternionValue rotationWorldFromBody,
            Vector3Value linearVelocityMetersPerSecond,
            Vector3Value angularVelocityRadiansPerSecond)
        {
            BodyId = bodyId ?? string.Empty;
            MassKilograms = massKilograms;
            PositionMeters = positionMeters;
            RotationWorldFromBody = rotationWorldFromBody.Canonicalized();
            LinearVelocityMetersPerSecond = linearVelocityMetersPerSecond;
            AngularVelocityRadiansPerSecond = angularVelocityRadiansPerSecond;
        }

        public string BodyId { get; }
        public float MassKilograms { get; }
        public Vector3Value PositionMeters { get; }
        public QuaternionValue RotationWorldFromBody { get; }
        public Vector3Value LinearVelocityMetersPerSecond { get; }
        public Vector3Value AngularVelocityRadiansPerSecond { get; }
    }

    public readonly struct PhysicalBodyObservations
    {
        private readonly PhysicalBodyObservation[] _items;
        private readonly int _offset;
        private readonly int _count;

        public PhysicalBodyObservations(PhysicalBodyObservation[] items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            _items = new PhysicalBodyObservation[items.Length];
            Array.Copy(items, _items, items.Length);
            _offset = 0;
            _count = items.Length;
        }

        internal PhysicalBodyObservations(PhysicalObservationStorage storage, int offset, int count)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            if (offset < 0 || count < 0 || offset > storage.Capacity - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _items = storage.Items;
            _offset = offset;
            _count = count;
        }

        public int Count => _items == null ? 0 : _count;

        public PhysicalBodyObservation Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[_offset + index];
        }

        public PhysicalBodyObservation this[int index] => Get(index);

        public bool TryGetBody(string bodyId, out PhysicalBodyObservation body)
        {
            if (string.IsNullOrEmpty(bodyId))
            {
                body = default(PhysicalBodyObservation);
                return false;
            }

            for (int index = 0; index < Count; index++)
            {
                PhysicalBodyObservation candidate = _items[_offset + index];
                if (string.Equals(candidate.BodyId, bodyId, StringComparison.Ordinal))
                {
                    body = candidate;
                    return true;
                }
            }

            body = default(PhysicalBodyObservation);
            return false;
        }
    }

    public readonly struct PhysicalObservation
    {
        public PhysicalObservation(SimulationTime time, PhysicalBodyObservation primaryBody, bool hasPrimaryBody)
            : this(
                time,
                primaryBody,
                hasPrimaryBody,
                hasPrimaryBody
                    ? new[] { primaryBody }
                    : Array.Empty<PhysicalBodyObservation>())
        {
        }

        public PhysicalObservation(
            SimulationTime time,
            PhysicalBodyObservation primaryBody,
            bool hasPrimaryBody,
            PhysicalBodyObservation[] bodyObservations)
        {
            SimulationTick = time.Tick;
            SimulationTimeSeconds = time.SimulationTimeSeconds;
            FixedDeltaTimeSeconds = time.FixedDeltaTimeSeconds;
            Frame = ReferenceFrame.World;
            UnitSystemId = UnitContract.InternalSystemId;
            HasPrimaryBody = hasPrimaryBody;
            PrimaryBody = primaryBody;
            Bodies = new PhysicalBodyObservations(bodyObservations);
        }

        internal PhysicalObservation(
            SimulationTime time,
            PhysicalBodyObservation primaryBody,
            bool hasPrimaryBody,
            PhysicalObservationStorage storage,
            int offset,
            int count)
        {
            SimulationTick = time.Tick;
            SimulationTimeSeconds = time.SimulationTimeSeconds;
            FixedDeltaTimeSeconds = time.FixedDeltaTimeSeconds;
            Frame = ReferenceFrame.World;
            UnitSystemId = UnitContract.InternalSystemId;
            HasPrimaryBody = hasPrimaryBody;
            PrimaryBody = primaryBody;
            Bodies = new PhysicalBodyObservations(storage, offset, count);
        }

        public static PhysicalObservation Empty(SimulationTime time) =>
            new PhysicalObservation(time, default(PhysicalBodyObservation), false);

        public ulong SimulationTick { get; }
        public double SimulationTimeSeconds { get; }
        public double FixedDeltaTimeSeconds { get; }
        public ReferenceFrame Frame { get; }
        public string UnitSystemId { get; }
        public bool HasPrimaryBody { get; }
        public PhysicalBodyObservation PrimaryBody { get; }
        public PhysicalBodyObservations Bodies { get; }
        public int BodyCount => Bodies.Count;

        public PhysicalBodyObservation BodyAt(int index) => Bodies.Get(index);

        public bool TryGetBody(string bodyId, out PhysicalBodyObservation body) => Bodies.TryGetBody(bodyId, out body);

        internal PhysicalObservation CopyWithStorage(PhysicalObservationStorage storage, int offset, int count)
        {
            return new PhysicalObservation(
                new SimulationTime(SimulationTick, SimulationTimeSeconds),
                PrimaryBody,
                HasPrimaryBody,
                storage,
                offset,
                count);
        }
    }

    // Internal bounded storage used by the Unity adapter and AttemptTrace. No public
    // array or mutating accessor is exposed; public observations only return values.
    internal sealed class PhysicalObservationStorage
    {
        internal const int DefaultBodyCapacity = 32;

        private readonly PhysicalBodyObservation[] _items;

        internal PhysicalObservationStorage(int capacity)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            _items = new PhysicalBodyObservation[capacity];
        }

        internal int Capacity => _items.Length;

        internal PhysicalBodyObservation[] Items => _items;

        internal void Set(int index, PhysicalBodyObservation value)
        {
            if (index < 0 || index >= _items.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            _items[index] = value;
        }
    }
}
