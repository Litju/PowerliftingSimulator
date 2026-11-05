using NUnit.Framework;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;

namespace PowerliftingSimulator.Tests
{
    public sealed class PlayerDepthFeedbackTests
    {
        [Test]
        public void DepthCueUsesBothObservedSidesAndHandlesUnavailableData()
        {
            Assert.That(PowerliftingGameUiController.FormatDepthFeedback(SquatDepthLandmarks.Unavailable()), Is.EqualTo("DEPTH\nWAITING"));
            Assert.That(PowerliftingGameUiController.FormatDepthFeedback(new SquatDepthLandmarks(0.48f, 0.51f, 0.5f, 0.5f)), Is.EqualTo("DEPTH\nNOT YET"));
            Assert.That(PowerliftingGameUiController.FormatDepthFeedback(new SquatDepthLandmarks(0.48f, 0.48f, 0.5f, 0.5f)), Is.EqualTo("DEPTH\nREACHED"));
        }
    }
}
