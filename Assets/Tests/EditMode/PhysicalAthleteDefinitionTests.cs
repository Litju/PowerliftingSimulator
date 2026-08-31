using NUnit.Framework;
using PowerliftingSimulator.Athlete;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalAthleteDefinitionTests
    {
        [Test]
        public void PHYSICAL_ATHLETE_TOTAL_MASS_MATCHES_PROFILE()
        {
            PhysicalAthleteDefinition.ValidateDefinition();
            float assignedMass = 0f;
            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
                assignedMass += PhysicalAthleteDefinition.PrototypeBodyMassKg * segment.MassFraction;

            Assert.That(PhysicalAthleteDefinition.Segments.Count, Is.EqualTo(16));
            Assert.That(PhysicalAthleteDefinition.Joints.Count, Is.EqualTo(15));
            Assert.That(assignedMass, Is.EqualTo(PhysicalAthleteDefinition.PrototypeBodyMassKg).Within(0.0001f));
        }
    }
}
