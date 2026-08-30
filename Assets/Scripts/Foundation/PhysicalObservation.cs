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

    public readonly struct PhysicalObservation
    {
        public PhysicalObservation(SimulationTime time, PhysicalBodyObservation primaryBody, bool hasPrimaryBody)
        {
            SimulationTick = time.Tick;
            SimulationTimeSeconds = time.SimulationTimeSeconds;
            FixedDeltaTimeSeconds = time.FixedDeltaTimeSeconds;
            Frame = ReferenceFrame.World;
            UnitSystemId = UnitContract.InternalSystemId;
            HasPrimaryBody = hasPrimaryBody;
            PrimaryBody = primaryBody;
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
    }
}
