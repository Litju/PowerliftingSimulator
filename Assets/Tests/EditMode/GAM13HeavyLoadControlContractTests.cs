using NUnit.Framework;
using PowerliftingSimulator.Athlete;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13HeavyLoadControlContractTests
    {
        [Test]
        public void V2_RETAINS_CURRENT_SHARED_FAMILY_PROFILES_AND_NORMALIZED_FINITE_CAPACITIES()
        {
            AssertFamily("ankle", 180f, 2.5f, 650f, 70f);
            AssertFamily("knee", 300f, 1.8f, 1123f, 94.8f);
            AssertFamily("hip", 360f, 1.5f, 900f, 90f);
            AssertFamily("trunk", 260f, 1.5f, 3200f, 170f);
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
