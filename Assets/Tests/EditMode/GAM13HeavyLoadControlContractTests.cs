using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13HeavyLoadControlContractTests
    {
        private static readonly float[] StageALoadsKg = { 25f, 60f, 140f, 170f, 300f };
        private static readonly float[] PhaseKnots = { 0f, 0.25f, 0.55f, 0.80f, 1f };

        [Test]
        public void LOAD_GENERAL_EQUILIBRIUM_FEEDFORWARD_IS_NONZERO_AND_BOUNDED_ON_STAGE_A_DOMAIN()
        {
            SquatEquilibriumPreload preload = SquatEquilibriumPreload.QualifiedStanding();

            foreach (float loadKg in StageALoadsKg)
            {
                foreach (float phase in PhaseKnots)
                {
                    float abdomenBias = preload.SpineBiasDegrees(SquatJointFamily.Abdomen, phase, loadKg);
                    float thoraxBias = preload.SpineBiasDegrees(SquatJointFamily.Thorax, phase, loadKg);

                    Assert.That(float.IsFinite(abdomenBias), Is.True, $"Abdomen bias is non-finite at {loadKg} kg, phase {phase}.");
                    Assert.That(float.IsFinite(thoraxBias), Is.True, $"Thorax bias is non-finite at {loadKg} kg, phase {phase}.");
                    Assert.That(System.Math.Abs(abdomenBias), Is.LessThanOrEqualTo(12.01f), $"Abdomen bias exceeds the hard bound at {loadKg} kg, phase {phase}.");
                    Assert.That(System.Math.Abs(thoraxBias), Is.LessThanOrEqualTo(12.01f), $"Thorax bias exceeds the hard bound at {loadKg} kg, phase {phase}.");
                }
            }

            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Abdomen, 0f, 60f), Is.Not.EqualTo(0f).Within(0.001f));
            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Thorax, 0f, 60f), Is.Not.EqualTo(0f).Within(0.001f));
            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Abdomen, 0f, 300f), Is.Not.EqualTo(0f).Within(0.001f));
            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Thorax, 0f, 300f), Is.Not.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void ACCEPTED_25_KG_STANDING_FEEDFORWARD_REMAINS_UNCHANGED()
        {
            SquatEquilibriumPreload preload = SquatEquilibriumPreload.QualifiedStanding();

            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Abdomen, 0f, 25f), Is.EqualTo(3.93f).Within(0.001f));
            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Thorax, 0f, 25f), Is.EqualTo(3.67f).Within(0.001f));
            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Abdomen, 1f, 25f), Is.EqualTo(-8.17f).Within(0.001f));
            Assert.That(preload.SpineBiasDegrees(SquatJointFamily.Thorax, 1f, 25f), Is.EqualTo(-3.66f).Within(0.001f));
        }

        [Test]
        public void ATHLETE_CAPACITY_IS_INDEPENDENT_OF_EXTERNAL_BAR_LOAD()
        {
            float unbraced25 = SquatPhysicalAdapter.CalculateAthleteCapacityScale(0f);
            float unbraced300 = SquatPhysicalAdapter.CalculateAthleteCapacityScale(0f);
            float braced25 = SquatPhysicalAdapter.CalculateAthleteCapacityScale(1f);
            float braced300 = SquatPhysicalAdapter.CalculateAthleteCapacityScale(1f);

            Assert.That(unbraced300, Is.EqualTo(unbraced25).Within(1e-6f));
            Assert.That(braced300, Is.EqualTo(braced25).Within(1e-6f));
            Assert.That(braced25, Is.GreaterThan(unbraced25));
            Assert.That(float.IsFinite(braced300), Is.True);
        }
    }
}
