using NUnit.Framework;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Tests
{
    public sealed class FoundationContractTests
    {
        [Test]
        public void CanonicalBasisIsOrthogonalAndRightHanded()
        {
            Assert.That(Vector3Value.Dot(CoordinateContract.UpAxis, CoordinateContract.ForwardAxis),
                Is.EqualTo(0f).Within(FoundationTolerances.BasisOrthogonality));
            Assert.That(Vector3Value.Dot(CoordinateContract.ForwardAxis, CoordinateContract.RightAxis),
                Is.EqualTo(0f).Within(FoundationTolerances.BasisOrthogonality));
            Assert.That(Vector3Value.Cross(CoordinateContract.RightAxis, CoordinateContract.UpAxis),
                Is.EqualTo(CoordinateContract.ForwardAxis));
            Assert.That(Vector3Value.Cross(CoordinateContract.UpAxis, CoordinateContract.ForwardAxis),
                Is.EqualTo(CoordinateContract.RightAxis));
        }

        [Test]
        public void LocalAndWorldPointConversionsUseWorldFromLocalRotation()
        {
            QuaternionValue rotation = QuaternionValue.FromAxisAngleRadians(
                CoordinateContract.UpAxis,
                UnitContract.DegreesToRadians(90f));
            Vector3Value originMeters = new Vector3Value(2f, 1f, -3f);

            Vector3Value worldPointMeters = CoordinateContract.LocalToWorldPoint(
                originMeters,
                rotation,
                CoordinateContract.ForwardAxis);
            Vector3Value localPointMeters = CoordinateContract.WorldToLocalPoint(
                originMeters,
                rotation,
                worldPointMeters);

            AssertVector(worldPointMeters, new Vector3Value(3f, 1f, -3f), FoundationTolerances.BasisOrthogonality);
            AssertVector(localPointMeters, CoordinateContract.ForwardAxis, FoundationTolerances.BasisOrthogonality);
        }

        [Test]
        public void IdentityQuaternionPreservesDirections()
        {
            Vector3Value direction = new Vector3Value(0.25f, -2f, 4f);

            Assert.That(QuaternionValue.Identity.TransformDirection(direction), Is.EqualTo(direction));
            Assert.That(QuaternionValue.Identity.X, Is.EqualTo(0f));
            Assert.That(QuaternionValue.Identity.Y, Is.EqualTo(0f));
            Assert.That(QuaternionValue.Identity.Z, Is.EqualTo(0f));
            Assert.That(QuaternionValue.Identity.W, Is.EqualTo(1f));
        }

        [Test]
        public void DegreeAndRadianConversionsRoundTrip()
        {
            float radians = UnitContract.DegreesToRadians(180f);
            float degrees = UnitContract.RadiansToDegrees(radians);

            Assert.That(radians, Is.EqualTo(3.1415927f).Within(FoundationTolerances.UnitConversionRoundTrip));
            Assert.That(degrees, Is.EqualTo(180f).Within(FoundationTolerances.UnitConversionRoundTrip));
        }

        [Test]
        public void FixedClockMapsOneHundredTicksToOneSecond()
        {
            var clock = new SimulationClock();
            for (int index = 0; index < SimulationConstants.FixedStepHz; index++)
                clock.Advance();

            Assert.That(SimulationConstants.FixedStepHz, Is.EqualTo(100));
            Assert.That(SimulationConstants.FixedDeltaTimeSeconds, Is.EqualTo(0.01d));
            Assert.That(clock.Current.Tick, Is.EqualTo(100ul));
            Assert.That(clock.Current.SimulationTimeSeconds,
                Is.EqualTo(1d).Within(FoundationTolerances.SimulationTimeMapping));
            Assert.That(SimulationConstants.TimeForTick(100),
                Is.EqualTo(1d).Within(FoundationTolerances.SimulationTimeMapping));
        }

        [Test]
        public void ClockResetReturnsToTheZeroTick()
        {
            var clock = new SimulationClock();
            clock.Advance();
            clock.Reset();

            Assert.That(clock.Current.Tick, Is.EqualTo(0ul));
            Assert.That(clock.Current.SimulationTimeSeconds, Is.EqualTo(0d));
        }

        private static void AssertVector(Vector3Value actual, Vector3Value expected, float tolerance)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance));
        }
    }
}
