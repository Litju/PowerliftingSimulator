using System;

namespace PowerliftingSimulator.Athlete
{
    public enum LiftKind : byte
    {
        Squat,
        Bench,
        Deadlift
    }

    public readonly struct LiftStrength
    {
        public LiftStrength(float? earnedPrKg, float? currentCapacityEstimateKg)
        {
            ValidateKilograms(earnedPrKg, nameof(earnedPrKg));
            ValidateKilograms(currentCapacityEstimateKg, nameof(currentCapacityEstimateKg));
            EarnedPrKg = earnedPrKg;
            CurrentCapacityEstimateKg = currentCapacityEstimateKg;
        }

        public float? EarnedPrKg { get; }
        public float? CurrentCapacityEstimateKg { get; }

        private static void ValidateKilograms(float? value, string name)
        {
            if (value.HasValue && (!float.IsFinite(value.Value) || value.Value <= 0f))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct AthleteStrengthState
    {
        public AthleteStrengthState(LiftStrength squat, LiftStrength bench, LiftStrength deadlift)
        {
            Squat = squat;
            Bench = bench;
            Deadlift = deadlift;
        }

        public LiftStrength Squat { get; }
        public LiftStrength Bench { get; }
        public LiftStrength Deadlift { get; }

        public LiftStrength ForLift(LiftKind lift)
        {
            switch (lift)
            {
                case LiftKind.Squat: return Squat;
                case LiftKind.Bench: return Bench;
                case LiftKind.Deadlift: return Deadlift;
                default: throw new ArgumentOutOfRangeException(nameof(lift));
            }
        }
    }

    public readonly struct LoadResponseSample
    {
        public LoadResponseSample(
            LiftKind lift,
            float loadKilograms,
            ulong completedObservationTick,
            float barVerticalVelocityWorldMetersPerSecond)
        {
            if (lift != LiftKind.Squat && lift != LiftKind.Bench && lift != LiftKind.Deadlift)
                throw new ArgumentOutOfRangeException(nameof(lift));
            if (!float.IsFinite(loadKilograms) || loadKilograms <= 0f)
                throw new ArgumentOutOfRangeException(nameof(loadKilograms));
            if (!float.IsFinite(barVerticalVelocityWorldMetersPerSecond))
                throw new ArgumentOutOfRangeException(nameof(barVerticalVelocityWorldMetersPerSecond));

            Lift = lift;
            LoadKilograms = loadKilograms;
            CompletedObservationTick = completedObservationTick;
            BarVerticalVelocityWorldMetersPerSecond = barVerticalVelocityWorldMetersPerSecond;
        }

        public LiftKind Lift { get; }
        public float LoadKilograms { get; }
        public ulong CompletedObservationTick { get; }
        public float BarVerticalVelocityWorldMetersPerSecond { get; }
    }
}
