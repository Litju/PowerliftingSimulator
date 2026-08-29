using System;
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

        [Test]
        public void SLOW_FRAME_TIME_DOMAIN()
        {
            var inputTimeDomain = new InputTimeDomain();
            var buffer = new IntentBuffer();

            Assert.That(inputTimeDomain.Map(10d), Is.EqualTo(0d));
            double slowFrameTimestamp = inputTimeDomain.Map(11d);
            Assert.That(slowFrameTimestamp, Is.EqualTo(SimulationConstants.MaxAccumulatedTimeSeconds));

            buffer.SetContinuous(IntentAction.Drive, 0.75f, slowFrameTimestamp);
            buffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Pressed, slowFrameTimestamp);

            PlayerIntentFrame frame = PlayerIntentFrame.Empty;
            for (ulong tick = 1; tick <= SimulationConstants.MaxCatchUpTicksPerRenderFrame; tick++)
            {
                double start = SimulationConstants.TimeForTick(tick - 1);
                double end = SimulationConstants.TimeForTick(tick);
                frame = buffer.SampleForTick(tick, start, end);
                if (tick < SimulationConstants.MaxCatchUpTicksPerRenderFrame)
                    Assert.That(frame.WasPressed(IntentAction.Confirm), Is.False);
            }

            Assert.That(frame.Drive01, Is.EqualTo(0.75f));
            Assert.That(frame.WasPressed(IntentAction.Confirm), Is.True);

            double freshTimestamp = inputTimeDomain.Map(11.01d);
            Assert.That(freshTimestamp, Is.EqualTo(0.05d).Within(FoundationTolerances.SimulationTimeMapping));
            buffer.SetContinuous(IntentAction.Drive, 1f, freshTimestamp);
            frame = buffer.SampleForTick(5, 0.04d, freshTimestamp + FoundationTolerances.SimulationTimeMapping);

            Assert.That(frame.Drive01, Is.EqualTo(1f));
            Assert.That(frame.WasPressed(IntentAction.Confirm), Is.False);
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));

            double previousTimestamp = freshTimestamp;
            for (int capture = 1; capture <= 32; capture++)
            {
                double mappedTimestamp = inputTimeDomain.Map(11.01d + capture * 0.01d);
                Assert.That(mappedTimestamp, Is.GreaterThanOrEqualTo(previousTimestamp));
                buffer.SetContinuous(IntentAction.Drive, 0.5f, mappedTimestamp);
                frame = buffer.SampleForTick(
                    (ulong)(5 + capture),
                    previousTimestamp,
                    mappedTimestamp + FoundationTolerances.SimulationTimeMapping);
                previousTimestamp = mappedTimestamp;
            }

            Assert.That(frame.Drive01, Is.EqualTo(0.5f));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void BUFFER_STABILITY()
        {
            var inputTimeDomain = new InputTimeDomain();
            var buffer = new IntentBuffer();
            inputTimeDomain.Map(50d);

            int maximumPendingSamples = 0;
            double latestTimestamp = 0d;
            for (int capture = 1; capture <= 128; capture++)
            {
                latestTimestamp = inputTimeDomain.Map(50d + capture * 0.01d);
                buffer.SetContinuous(IntentAction.Brace, 1f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Yield, 0.5f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Drive, capture == 128 ? 1f : 0.25f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Balance, -0.5f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Grip, 1f, latestTimestamp);
                maximumPendingSamples = Math.Max(maximumPendingSamples, buffer.PendingSampleCount);
            }

            Assert.That(maximumPendingSamples, Is.EqualTo(5));
            Assert.That(buffer.PendingSampleCount, Is.LessThanOrEqualTo(5));

            PlayerIntentFrame frame = PlayerIntentFrame.Empty;
            for (ulong tick = 1; tick <= 128; tick++)
            {
                frame = buffer.SampleForTick(
                    tick,
                    SimulationConstants.TimeForTick(tick - 1),
                    SimulationConstants.TimeForTick(tick) + FoundationTolerances.SimulationTimeMapping);
            }

            Assert.That(frame.Drive01, Is.EqualTo(1f));
            Assert.That(frame.BalanceX, Is.EqualTo(-0.5f));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
            Assert.That(latestTimestamp, Is.EqualTo(1.28d).Within(FoundationTolerances.SimulationTimeMapping));
        }
    }
}
