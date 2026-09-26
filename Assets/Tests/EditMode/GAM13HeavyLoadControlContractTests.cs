using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat.Unity;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13HeavyLoadControlContractTests
    {
        [Test]
        public void V2_EFFORT_IS_BOUNDED_AND_RESPONDS_TO_BRACE_OR_DRIVE()
        {
            float neutral = SquatPhysicalAdapter.CalculateEffort(0f, 0f);
            float braced = SquatPhysicalAdapter.CalculateEffort(1f, 0f);
            float driving = SquatPhysicalAdapter.CalculateEffort(0f, 1f);

            Assert.That(neutral, Is.EqualTo(1f / 1.35f).Within(1e-6f));
            Assert.That(braced, Is.EqualTo(1f));
            Assert.That(driving, Is.EqualTo(1f));
            Assert.That(SquatPhysicalAdapter.CalculateEffort(2f, -1f), Is.EqualTo(1f));
        }

        [Test]
        public void V2_BASE_CAPACITY_ABSORBS_FORMER_SQUAT_FAMILY_MULTIPLIERS()
        {
            AssertFamilyCapacity("ankle", 180f, 2.5f);
            AssertFamilyCapacity("knee", 300f, 1.8f);
            AssertFamilyCapacity("hip", 360f, 1.5f);
            AssertFamilyCapacity("trunk", 260f, 1.5f);
        }

        private static void AssertFamilyCapacity(string family, float oldBase, float oldSquatMultiplier)
        {
            JointFamilyProfile profile = PoweredJointController.FindFamilyProfile(family).Value;
            Assert.That(profile.BaseCapacityNm, Is.EqualTo(oldBase * oldSquatMultiplier).Within(1e-5f));

            float brace = 0.45f;
            float oldAuthority = oldBase * SquatPhysicalAdapter.AthleteStrengthScale /
                1.35f * (1f + 0.35f * brace) * oldSquatMultiplier;
            float v2Authority = profile.BaseCapacityNm * SquatPhysicalAdapter.AthleteStrengthScale *
                SquatPhysicalAdapter.CalculateEffort(brace, 0f);
            Assert.That(v2Authority, Is.EqualTo(oldAuthority).Within(1e-4f));
        }
    }
}
