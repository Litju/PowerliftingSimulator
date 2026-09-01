using System;

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

        public PhysicalBodyObservations(PhysicalBodyObservation[] items)
        {
            if (items == null)
                throw new ArgumentNullException(nameof(items));

            _items = new PhysicalBodyObservation[items.Length];
            Array.Copy(items, _items, items.Length);
        }

        public int Count => _items == null ? 0 : _items.Length;

        public PhysicalBodyObservation Get(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _items[index];
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
                PhysicalBodyObservation candidate = _items[index];
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
    }
}
