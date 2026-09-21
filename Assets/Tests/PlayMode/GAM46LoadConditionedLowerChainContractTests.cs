using NUnit.Framework;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM46LoadConditionedLowerChainContractTests
    {
        [Test]
        public void LC1_ALPHA_USES_THE_FROZEN_CUBIC_ANCHORS()
        {
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(25f), Is.EqualTo(0f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(170f), Is.EqualTo(0f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(180f), Is.EqualTo(2f / 27f).Within(1e-6f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(190f), Is.EqualTo(7f / 27f).Within(1e-6f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(200f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(215f), Is.EqualTo(0.84375f).Within(1e-6f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(230f), Is.EqualTo(1f));
            Assert.That(GAM46LoadConditionedLowerChain.Alpha(300f), Is.EqualTo(1f));
        }

        [Test]
        public void LC1_MULTIPLIES_ONLY_THE_EXISTING_F2_LOWER_CHAIN()
        {
            var f2 = GAM43FeedForward.ForVariant("F2");
            var lc1 = GAM43FeedForward.ForVariant("LC1");

            foreach (SquatJointFamily family in new[]
                     { SquatJointFamily.Ankle, SquatJointFamily.Knee, SquatJointFamily.Hip })
            {
                float existing = f2(family, 0.15f, 200f);
                Assert.That(lc1(family, 0.15f, 200f),
                    Is.EqualTo(GAM46LoadConditionedLowerChain.Alpha(200f) * existing).Within(1e-6f));
                Assert.That(lc1(family, 0.15f, 170f), Is.EqualTo(0f).Within(1e-6f));
                Assert.That(lc1(family, 0.15f, 230f), Is.EqualTo(f2(family, 0.15f, 230f)).Within(1e-6f));
            }
        }

        [Test]
        public void LC1_LEAVES_THE_F2_SPINE_LAW_UNCHANGED()
        {
            var f2 = GAM43FeedForward.ForVariant("F2");
            var lc1 = GAM43FeedForward.ForVariant("LC1");

            foreach (SquatJointFamily family in new[]
                     { SquatJointFamily.Abdomen, SquatJointFamily.Thorax })
            {
                foreach (float loadKg in new[] { 25f, 170f, 200f, 230f, 300f })
                {
                    Assert.That(lc1(family, 0.15f, loadKg),
                        Is.EqualTo(f2(family, 0.15f, loadKg)).Within(1e-6f));
                }
            }
        }
    }
}
