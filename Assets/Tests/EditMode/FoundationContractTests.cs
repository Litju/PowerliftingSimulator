using System;
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
        public void QuaternionNormalizationProducesUnitLength()
        {
            QuaternionValue normalized = new QuaternionValue(1f, 2f, 3f, 4f).Normalized();
            float lengthSquared = normalized.X * normalized.X +
                normalized.Y * normalized.Y +
                normalized.Z * normalized.Z +
                normalized.W * normalized.W;

            Assert.That(lengthSquared, Is.EqualTo(1f).Within(FoundationTolerances.QuaternionNormalizationThreshold));
        }

        [Test]
        public void NONFINITE_QUATERNION_COMPONENTS_ARE_REJECTED()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QuaternionValue(float.NaN, 0f, 0f, 1f).Normalized());
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new QuaternionValue(0f, 0f, float.PositiveInfinity, 1f).Canonicalized());
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

        [Test]
        public void QUATERNION_Q_AND_NEGATIVE_Q_CANONICALIZE_IDENTICALLY()
        {
            QuaternionValue q = QuaternionValue.FromAxisAngleRadians(
                new Vector3Value(1f, 2f, 3f),
                UnitContract.DegreesToRadians(73f));
            QuaternionValue negativeQ = new QuaternionValue(-q.X, -q.Y, -q.Z, -q.W);

            QuaternionValue normalized = q.Canonicalized();
            QuaternionValue normalizedNegative = negativeQ.Canonicalized();

            Assert.That(normalized, Is.EqualTo(normalizedNegative));
            AssertQuaternion(normalized, normalizedNegative, FoundationTolerances.QuaternionNormalizationThreshold);
        }

        [Test]
        public void QUATERNION_PI_BOUNDARY_HAS_A_DETERMINISTIC_CANONICAL_SIGN()
        {
            QuaternionValue q = QuaternionValue.FromAxisAngleRadians(
                new Vector3Value(0f, 1f, 0f),
                UnitContract.DegreesToRadians(180f));
            QuaternionValue negativeQ = new QuaternionValue(-q.X, -q.Y, -q.Z, -q.W);

            QuaternionValue canonical = q.Canonicalized();
            Assert.That(canonical, Is.EqualTo(negativeQ.Canonicalized()));
            AssertQuaternion(canonical, negativeQ.Canonicalized(), FoundationTolerances.QuaternionCanonicalizationTie);
            Assert.That(canonical.W, Is.EqualTo(0f).Within(FoundationTolerances.QuaternionCanonicalizationTie));
            Assert.That(canonical.X, Is.EqualTo(0f).Within(FoundationTolerances.QuaternionCanonicalizationTie));
            Assert.That(canonical.Y, Is.GreaterThan(0f));
            Assert.That(canonical.Z, Is.EqualTo(0f).Within(FoundationTolerances.QuaternionCanonicalizationTie));
        }

        [Test]
        public void IDENTICAL_ROTATION_DIFFERENT_SIGN_HAS_ZERO_ERROR()
        {
            QuaternionValue q = QuaternionValue.FromAxisAngleRadians(
                new Vector3Value(2f, -1f, 4f),
                UnitContract.DegreesToRadians(121f));
            QuaternionValue negativeQ = new QuaternionValue(-q.X, -q.Y, -q.Z, -q.W);
            float errorRadians = q.ShortestArcRadiansTo(negativeQ);

            Assert.That(errorRadians, Is.EqualTo(0f).Within(FoundationTolerances.UnitConversionRoundTrip));
        }

        [Test]
        public void QuaternionSmallRotationIsNotRoundedToZero()
        {
            QuaternionValue identity = QuaternionValue.Identity;
            QuaternionValue smallRotation = QuaternionValue.FromAxisAngleRadians(
                CoordinateContract.UpAxis,
                UnitContract.DegreesToRadians(0.1f));

            float separationRadians = identity.ShortestArcRadiansTo(smallRotation);

            Assert.That(separationRadians, Is.EqualTo(UnitContract.DegreesToRadians(0.1f))
                .Within(FoundationTolerances.UnitConversionRoundTrip));
        }

        [Test]
        public void SHORTEST_ARC_ACROSS_PLUS_MINUS_PI()
        {
            Vector3Value axis = new Vector3Value(1f, 2f, -1f);
            QuaternionValue plus179 = QuaternionValue.FromAxisAngleRadians(
                axis,
                UnitContract.DegreesToRadians(179f));
            QuaternionValue minus179 = QuaternionValue.FromAxisAngleRadians(
                axis,
                UnitContract.DegreesToRadians(-179f));
            float separationRadians = plus179.ShortestArcRadiansTo(minus179);

            Assert.That(separationRadians, Is.EqualTo(0.0349066f)
                .Within(FoundationTolerances.UnitConversionRoundTrip));
        }

        private static void AssertQuaternion(QuaternionValue actual, QuaternionValue expected, float tolerance)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance));
            Assert.That(actual.W, Is.EqualTo(expected.W).Within(tolerance));
        }

        private static void AssertVector(Vector3Value actual, Vector3Value expected, float tolerance)
        {
            Assert.That(actual.X, Is.EqualTo(expected.X).Within(tolerance));
            Assert.That(actual.Y, Is.EqualTo(expected.Y).Within(tolerance));
            Assert.That(actual.Z, Is.EqualTo(expected.Z).Within(tolerance));
        }
    }
}
