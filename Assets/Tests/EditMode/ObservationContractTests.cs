using NUnit.Framework;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Tests
{
    public sealed class ObservationContractTests
    {
        [Test]
        public void PhysicalObservationCarriesPostStepTimeFrameAndUnits()
        {
            var time = new SimulationTime(7, 0.07d);
            var body = new PhysicalBodyObservation(
                "probe",
                2f,
                new Vector3Value(1f, 2f, 3f),
                new QuaternionValue(0f, 0f, 0f, 2f),
                new Vector3Value(4f, 5f, 6f),
                new Vector3Value(0.1f, 0.2f, 0.3f));
            PhysicalObservation observation = new PhysicalObservation(time, body, true);

            Assert.That(observation.SimulationTick, Is.EqualTo(7ul));
            Assert.That(observation.SimulationTimeSeconds, Is.EqualTo(0.07d));
            Assert.That(observation.FixedDeltaTimeSeconds, Is.EqualTo(0.01d));
            Assert.That(observation.Frame, Is.EqualTo(ReferenceFrame.World));
            Assert.That(observation.UnitSystemId, Is.EqualTo(UnitContract.InternalSystemId));
            Assert.That(observation.HasPrimaryBody, Is.True);
            Assert.That(observation.PrimaryBody.RotationWorldFromBody, Is.EqualTo(QuaternionValue.Identity));
        }

        [Test]
        public void EmptyObservationHasNoPhysicalBodyButRetainsTimeMetadata()
        {
            PhysicalObservation observation = PhysicalObservation.Empty(new SimulationTime(3, 0.03d));

            Assert.That(observation.HasPrimaryBody, Is.False);
            Assert.That(observation.SimulationTick, Is.EqualTo(3ul));
            Assert.That(observation.SimulationTimeSeconds, Is.EqualTo(0.03d));
            Assert.That(observation.UnitSystemId, Is.EqualTo("SI"));
        }
    }
}
