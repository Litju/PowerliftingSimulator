using NUnit.Framework;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Tests
{
    public sealed class IntentBufferTests
    {
        [Test]
        public void EdgesAreConsumedOnceAndHeldStatePersists()
        {
            var buffer = new IntentBuffer();
            buffer.PushEdge(IntentAction.Brace, IntentEdgeKind.Pressed, 0.005d);
            buffer.SetContinuous(IntentAction.Drive, 0.75f, 0.006d);

            PlayerIntentFrame first = buffer.SampleForTick(1, 0d, 0.01d);
            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);

            Assert.That(first.WasPressed(IntentAction.Brace), Is.True);
            Assert.That(first.IsHeld(IntentAction.Brace), Is.True);
            Assert.That(first.Value(IntentAction.Drive), Is.EqualTo(0.75f));
            Assert.That(second.WasPressed(IntentAction.Brace), Is.False);
            Assert.That(second.IsHeld(IntentAction.Brace), Is.True);
            Assert.That(second.Value(IntentAction.Drive), Is.EqualTo(0.75f));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void ReleaseEdgeIsAppliedToTheTickContainingIt()
        {
            var buffer = new IntentBuffer();
            buffer.PushEdge(IntentAction.Grip, IntentEdgeKind.Pressed, 0.001d);
            buffer.SampleForTick(1, 0d, 0.01d);
            buffer.PushEdge(IntentAction.Grip, IntentEdgeKind.Released, 0.015d);

            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);

            Assert.That(second.WasReleased(IntentAction.Grip), Is.True);
            Assert.That(second.IsHeld(IntentAction.Grip), Is.False);
        }

        [Test]
        public void ContinuousValuesAreClampedBySemanticRangeAndPersist()
        {
            var buffer = new IntentBuffer();
            buffer.SetContinuous(IntentAction.Balance, 2f, 0.001d);
            buffer.SetContinuous(IntentAction.Brace, -1f, 0.002d);

            PlayerIntentFrame first = buffer.SampleForTick(1, 0d, 0.01d);
            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);

            Assert.That(first.BalanceX, Is.EqualTo(1f));
            Assert.That(first.Brace01, Is.EqualTo(0f));
            Assert.That(second.BalanceX, Is.EqualTo(1f));
            Assert.That(second.Brace01, Is.EqualTo(0f));
        }

        [Test]
        public void FutureSamplesWaitForTheirTick()
        {
            var buffer = new IntentBuffer();
            buffer.SetContinuous(IntentAction.Yield, 1f, 0.015d);

            PlayerIntentFrame first = buffer.SampleForTick(1, 0d, 0.01d);
            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);

            Assert.That(first.Yield01, Is.EqualTo(0f));
            Assert.That(second.Yield01, Is.EqualTo(1f));
        }

        [Test]
        public void ResetClearsQueuedSamplesAndHeldState()
        {
            var buffer = new IntentBuffer();
            buffer.PushEdge(IntentAction.Abort, IntentEdgeKind.Pressed, 0.001d);
            buffer.SampleForTick(1, 0d, 0.01d);
            buffer.Reset();

            PlayerIntentFrame frame = buffer.SampleForTick(1, 0d, 0.01d);

            Assert.That(frame.IsHeld(IntentAction.Abort), Is.False);
            Assert.That(frame.Edges, Is.EqualTo(IntentEdgeFlags.None));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
        }
    }
}
