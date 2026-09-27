using NUnit.Framework;
using PowerliftingSimulator.Athlete;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13HeavyLoadControlContractTests
    {
        [Test]
        public void V2_RESTORES_PRE_PASS_IMPEDANCE_AND_PRESERVES_NORMALIZED_FINITE_CAPACITIES()
        {
            AssertFamily("ankle", 180f, 2.5f, 650f, 70f);
            AssertFamily("knee", 300f, 1.8f, 800f, 80f);
            AssertFamily("hip", 360f, 1.5f, 900f, 90f);
            AssertFamily("trunk", 260f, 1.5f, 800f, 85f);
        }

        private static void AssertFamily(
            string family,
            float oldBase,
            float oldSquatMultiplier,
            float expectedSpring,
            float expectedDamper)
        {
            JointFamilyProfile profile = PoweredJointController.FindFamilyProfile(family).Value;
            Assert.That(profile.BaseCapacityNm, Is.EqualTo(oldBase * oldSquatMultiplier).Within(1e-5f));
            Assert.That(float.IsFinite(profile.BaseCapacityNm) && profile.BaseCapacityNm > 0f, Is.True);
            Assert.That(profile.Spring, Is.EqualTo(expectedSpring).Within(1e-5f));
            Assert.That(profile.Damper, Is.EqualTo(expectedDamper).Within(1e-5f));
        }
    }
}
