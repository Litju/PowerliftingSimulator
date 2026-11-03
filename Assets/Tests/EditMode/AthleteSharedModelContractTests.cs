using System;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;

namespace PowerliftingSimulator.Tests
{
    public sealed class AthleteSharedModelContractTests
    {
        [Test]
        public void EARNED_PR_AND_CURRENT_CAPACITY_ESTIMATE_REMAIN_INDEPENDENT_PER_LIFT()
        {
            var state = new AthleteStrengthState(
                new LiftStrength(240f, 180f),
                new LiftStrength(null, null),
                new LiftStrength(310f, 300f));

            Assert.That(state.ForLift(LiftKind.Squat).EarnedPrKg, Is.EqualTo(240f));
            Assert.That(state.ForLift(LiftKind.Squat).CurrentCapacityEstimateKg, Is.EqualTo(180f));
            Assert.That(state.ForLift(LiftKind.Bench).EarnedPrKg, Is.Null);
            Assert.That(state.ForLift(LiftKind.Bench).CurrentCapacityEstimateKg, Is.Null);
            Assert.That(state.ForLift(LiftKind.Deadlift).EarnedPrKg, Is.EqualTo(310f));
            Assert.That(state.ForLift(LiftKind.Deadlift).CurrentCapacityEstimateKg, Is.EqualTo(300f));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void STRENGTH_VALUES_MUST_BE_POSITIVE_FINITE_KILOGRAMS(float invalidKilograms)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LiftStrength(invalidKilograms, null); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LiftStrength(null, invalidKilograms); });
        }

        [Test]
        public void UNKNOWN_CAPACITY_REMAINS_UNKNOWN_INSTEAD_OF_FALLING_BACK_TO_PR()
        {
            var strength = new LiftStrength(250f, null);

            Assert.That(strength.EarnedPrKg, Is.EqualTo(250f));
            Assert.That(strength.CurrentCapacityEstimateKg, Is.Null);
        }

        [Test]
        public void LOAD_RESPONSE_SAMPLE_PRESERVES_SIGNED_MEASURED_WORLD_Y_VELOCITY()
        {
            var sample = new LoadResponseSample(LiftKind.Squat, 25f, 42ul, -0.125f);
            var stalledSample = new LoadResponseSample(LiftKind.Bench, 25f, 43ul, 0f);

            Assert.That(sample.Lift, Is.EqualTo(LiftKind.Squat));
            Assert.That(sample.LoadKilograms, Is.EqualTo(25f));
            Assert.That(sample.CompletedObservationTick, Is.EqualTo(42ul));
            Assert.That(sample.BarVerticalVelocityWorldMetersPerSecond, Is.EqualTo(-0.125f));
            Assert.That(stalledSample.BarVerticalVelocityWorldMetersPerSecond, Is.Zero);
        }

        [Test]
        public void LOAD_RESPONSE_SAMPLE_REJECTS_INVALID_LOAD_OR_NONFINITE_VELOCITY()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LoadResponseSample(LiftKind.Bench, 0f, 0ul, 0f); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LoadResponseSample(LiftKind.Bench, 25f, 0ul, float.NaN); });
            Assert.Throws<ArgumentOutOfRangeException>(() => { _ = new LoadResponseSample((LiftKind)255, 25f, 0ul, 0f); });
        }
    }
}
