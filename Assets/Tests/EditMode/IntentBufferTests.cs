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
        public void SAMPLE_FOR_TICK_REQUIRES_CONTIGUOUS_FIXED_INTERVALS()
        {
            var buffer = new IntentBuffer();
            buffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Pressed, 0.005d);

            Assert.Throws<InvalidOperationException>(() =>
                buffer.SampleForTick(2, 0.02d, 0.03d));

            buffer.Reset();
            buffer.SampleForTick(1, 0d, 0.01d);
            Assert.Throws<InvalidOperationException>(() =>
                buffer.SampleForTick(3, 0.01d, 0.02d));
            Assert.Throws<InvalidOperationException>(() =>
                buffer.SampleForTick(1, 0.01d, 0.02d));
        }

        [Test]
        public void EDGE_SEQUENCE_IS_EXPOSED_IN_CONSUMPTION_ORDER()
        {
            var buffer = new IntentBuffer();
            buffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Pressed, 0.001d);
            buffer.PushEdge(IntentAction.Abort, IntentEdgeKind.Pressed, 0.002d);
            buffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Released, 0.003d);

            PlayerIntentFrame frame = buffer.SampleForTick(1, 0d, 0.01d);

            Assert.That(frame.EdgeEventCount, Is.EqualTo(3));
            Assert.That(frame.EdgeEventAt(0).Action, Is.EqualTo(IntentAction.Confirm));
            Assert.That(frame.EdgeEventAt(0).EdgeKind, Is.EqualTo(IntentEdgeKind.Pressed));
            Assert.That(frame.EdgeEventAt(1).Action, Is.EqualTo(IntentAction.Abort));
            Assert.That(frame.EdgeEventAt(1).EdgeKind, Is.EqualTo(IntentEdgeKind.Pressed));
            Assert.That(frame.EdgeEventAt(2).Action, Is.EqualTo(IntentAction.Confirm));
            Assert.That(frame.EdgeEventAt(2).EdgeKind, Is.EqualTo(IntentEdgeKind.Released));
        }

        [Test]
        public void BATCH_REJECTION_DOES_NOT_PARTIALLY_COMMIT_INPUT()
        {
            var buffer = new IntentBuffer();
            IntentInputSample[] invalidBatch =
            {
                IntentInputSample.Edge(IntentAction.Confirm, IntentEdgeKind.Pressed, 0.001d),
                IntentInputSample.Edge(IntentAction.Abort, IntentEdgeKind.Pressed, 0.0005d)
            };

            Assert.Throws<InvalidOperationException>(() =>
                buffer.ApplyBatch(invalidBatch, invalidBatch.Length));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));

            buffer.ApplyBatch(
                new[] { IntentInputSample.Edge(IntentAction.Confirm, IntentEdgeKind.Pressed, 0.001d) },
                1);
            PlayerIntentFrame frame = buffer.SampleForTick(1, 0d, 0.01d);
            Assert.That(frame.WasPressed(IntentAction.Confirm), Is.True);
            Assert.That(frame.EdgeEventCount, Is.EqualTo(1));
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

            Assert.That(inputTimeDomain.AdvanceRenderInterval(10d), Is.EqualTo(0d));
            double slowFrameTimestamp = inputTimeDomain.AdvanceRenderInterval(11d);
            Assert.That(inputTimeDomain.Map(11d), Is.EqualTo(slowFrameTimestamp));
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

            inputTimeDomain.AdvanceRenderInterval(11.01d);
            double freshTimestamp = inputTimeDomain.Map(11.01d);
            Assert.That(freshTimestamp, Is.EqualTo(0.05d).Within(FoundationTolerances.SimulationTimeMapping));
            buffer.SetContinuous(IntentAction.Drive, 1f, freshTimestamp);
            frame = buffer.SampleForTick(5, 0.04d, freshTimestamp);

            Assert.That(frame.Drive01, Is.EqualTo(1f));
            Assert.That(frame.WasPressed(IntentAction.Confirm), Is.False);
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));

            double previousTimestamp = freshTimestamp;
            for (int capture = 1; capture <= 32; capture++)
            {
                double realTimestamp = 11.01d + capture * 0.01d;
                inputTimeDomain.AdvanceRenderInterval(realTimestamp);
                double mappedTimestamp = inputTimeDomain.Map(realTimestamp);
                Assert.That(mappedTimestamp, Is.GreaterThanOrEqualTo(previousTimestamp));
                buffer.SetContinuous(IntentAction.Drive, 0.5f, mappedTimestamp);
                frame = buffer.SampleForTick(
                    (ulong)(5 + capture),
                    previousTimestamp,
                    mappedTimestamp);
                previousTimestamp = mappedTimestamp;
            }

            Assert.That(frame.Drive01, Is.EqualTo(0.5f));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void TIME_MAPPING_USES_THE_AUTHORITATIVE_RENDER_HORIZON()
        {
            var inputTimeDomain = new InputTimeDomain();
            Assert.That(inputTimeDomain.AdvanceRenderInterval(100d, 0d, 0d), Is.EqualTo(0d));
            Assert.That(inputTimeDomain.Map(100d), Is.EqualTo(0d));
            Assert.That(inputTimeDomain.AdvanceRenderInterval(100.009d, 0d, 0.009d), Is.EqualTo(0.009d));
            Assert.That(inputTimeDomain.Map(100.0045d), Is.EqualTo(0.0045d)
                .Within(FoundationTolerances.SimulationTimeMapping));
            Assert.That(inputTimeDomain.Map(100.009d), Is.EqualTo(0.009d));

            inputTimeDomain.AdvanceRenderInterval(101.009d, 0.009d, 0.04d);
            Assert.That(inputTimeDomain.Map(100.509d), Is.EqualTo(0.0245d)
                .Within(FoundationTolerances.SimulationTimeMapping));
            Assert.That(inputTimeDomain.Map(101.009d), Is.EqualTo(0.04d));
            Assert.That(inputTimeDomain.LastMappedTimestampSeconds, Is.EqualTo(0.04d));
        }

        [Test]
        public void TIME_MAPPING_REJECTS_UNBOUNDED_AUTHORITATIVE_HORIZONS()
        {
            var inputTimeDomain = new InputTimeDomain();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                inputTimeDomain.AdvanceRenderInterval(100d, 0d, 0.08d));
        }

        [Test]
        public void TIME_MAPPING_CLAMPS_WITHIN_TOLERATED_EVENT_ORDER()
        {
            var inputTimeDomain = new InputTimeDomain();
            inputTimeDomain.AdvanceRenderInterval(99d, 0d, 0d);
            inputTimeDomain.AdvanceRenderInterval(101d, 0d, 0.04d);

            double first = inputTimeDomain.Map(100.5d);
            double second = inputTimeDomain.Map(100.5d - 0.5e-12d);

            Assert.That(second, Is.EqualTo(first).Within(FoundationTolerances.SimulationTimeMapping));
        }

        [Test]
        public void INPUT_TIME_MAPPING_CAN_RETRY_AFTER_CAPTURE_REJECTION()
        {
            var inputTimeDomain = new InputTimeDomain();
            inputTimeDomain.AdvanceRenderInterval(100d, 0d, 0d);
            inputTimeDomain.AdvanceRenderInterval(101d, 0d, 0.04d);

            Assert.That(inputTimeDomain.Map(100.5d), Is.EqualTo(0.02d)
                .Within(FoundationTolerances.SimulationTimeMapping));
            Assert.Throws<InvalidOperationException>(() => inputTimeDomain.Map(99d));

            Assert.DoesNotThrow(() => inputTimeDomain.AdvanceRenderInterval(101d, 0d, 0.04d));
            Assert.That(inputTimeDomain.Map(100.5d), Is.EqualTo(0.02d)
                .Within(FoundationTolerances.SimulationTimeMapping));
        }

        [Test]
        public void BUFFER_STABILITY()
        {
            var inputTimeDomain = new InputTimeDomain();
            var buffer = new IntentBuffer();
            inputTimeDomain.AdvanceRenderInterval(50d);
            inputTimeDomain.AdvanceRenderInterval(51d);

            int maximumPendingSamples = 0;
            double latestTimestamp = 0d;
            for (int capture = 1; capture <= 128; capture++)
            {
                latestTimestamp = inputTimeDomain.Map(50d + capture / 128d);
                buffer.SetContinuous(IntentAction.Brace, 1f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Yield, 0.5f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Drive, capture == 128 ? 1f : 0.25f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Balance, -0.5f, latestTimestamp);
                buffer.SetContinuous(IntentAction.Grip, 1f, latestTimestamp);
                maximumPendingSamples = Math.Max(maximumPendingSamples, buffer.PendingSampleCount);
            }

            Assert.That(maximumPendingSamples, Is.EqualTo(SimulationConstants.MaxCatchUpTicksPerRenderFrame * 5));
            Assert.That(buffer.PendingSampleCount, Is.LessThanOrEqualTo(IntentBuffer.MaxContinuousPendingSampleCount));

            PlayerIntentFrame frame = PlayerIntentFrame.Empty;
            for (ulong tick = 1; tick <= SimulationConstants.MaxCatchUpTicksPerRenderFrame; tick++)
            {
                frame = buffer.SampleForTick(
                    tick,
                    SimulationConstants.TimeForTick(tick - 1),
                    SimulationConstants.TimeForTick(tick));
            }

            Assert.That(frame.Drive01, Is.EqualTo(1f));
            Assert.That(frame.BalanceX, Is.EqualTo(-0.5f));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
            Assert.That(latestTimestamp, Is.EqualTo(SimulationConstants.MaxAccumulatedTimeSeconds)
                .Within(FoundationTolerances.SimulationTimeMapping));
        }

        [Test]
        public void MULTI_EVENT_STALL_DOES_NOT_CREATE_PERSISTENT_INPUT_LEAD()
        {
            var inputTimeDomain = new InputTimeDomain();
            var buffer = new IntentBuffer();
            Assert.That(inputTimeDomain.AdvanceRenderInterval(100d), Is.EqualTo(0d));
            inputTimeDomain.AdvanceRenderInterval(101d);

            double[] eventTimes = { 100.20d, 100.40d, 100.60d, 100.80d, 101.00d };
            for (int index = 0; index < eventTimes.Length; index++)
            {
                double mappedTimestamp = inputTimeDomain.Map(eventTimes[index]);
                buffer.SetContinuous(IntentAction.Drive, (index + 1) / 5f, mappedTimestamp);
            }

            PlayerIntentFrame frame = PlayerIntentFrame.Empty;
            for (ulong tick = 1; tick <= SimulationConstants.MaxCatchUpTicksPerRenderFrame; tick++)
            {
                frame = buffer.SampleForTick(
                    tick,
                    SimulationConstants.TimeForTick(tick - 1),
                    SimulationConstants.TimeForTick(tick));
            }

            Assert.That(inputTimeDomain.LastMappedTimestampSeconds,
                Is.LessThanOrEqualTo(SimulationConstants.MaxAccumulatedTimeSeconds + FoundationTolerances.SimulationTimeMapping));

            inputTimeDomain.AdvanceRenderInterval(101.01d);
            double freshTimestamp = inputTimeDomain.Map(101.01d);
            Assert.That(freshTimestamp, Is.EqualTo(0.05d).Within(FoundationTolerances.SimulationTimeMapping));
            buffer.SetContinuous(IntentAction.Drive, 1f, freshTimestamp);
            frame = buffer.SampleForTick(5, 0.04d, 0.05d);

            Assert.That(frame.Drive01, Is.EqualTo(1f));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void MULTI_EDGE_STALL_PRESERVES_ORDER_AND_EXACTLY_ONCE()
        {
            var inputTimeDomain = new InputTimeDomain();
            var buffer = new IntentBuffer();
            inputTimeDomain.AdvanceRenderInterval(100d);
            inputTimeDomain.AdvanceRenderInterval(101d);

            double[] eventTimes = { 100.20d, 100.21d, 100.60d, 100.61d };
            IntentAction[] actions =
            {
                IntentAction.Brace,
                IntentAction.Brace,
                IntentAction.Grip,
                IntentAction.Grip
            };
            IntentEdgeKind[] edgeKinds =
            {
                IntentEdgeKind.Pressed,
                IntentEdgeKind.Released,
                IntentEdgeKind.Pressed,
                IntentEdgeKind.Released
            };
            for (int index = 0; index < eventTimes.Length; index++)
                buffer.PushEdge(
                    actions[index],
                    edgeKinds[index],
                    inputTimeDomain.Map(eventTimes[index]));

            for (ulong tick = 1; tick <= 4ul; tick++)
            {
                PlayerIntentFrame frame = buffer.SampleForTick(
                    tick,
                    SimulationConstants.TimeForTick(tick - 1),
                    SimulationConstants.TimeForTick(tick));
                if (tick == 1)
                {
                    Assert.That(frame.WasPressed(IntentAction.Brace), Is.True);
                    Assert.That(frame.WasReleased(IntentAction.Brace), Is.True);
                    Assert.That(frame.IsHeld(IntentAction.Brace), Is.False);
                    Assert.That(frame.EdgeEventCount, Is.EqualTo(2));
                }
                else if (tick == 3)
                {
                    Assert.That(frame.WasPressed(IntentAction.Grip), Is.True);
                    Assert.That(frame.WasReleased(IntentAction.Grip), Is.True);
                    Assert.That(frame.IsHeld(IntentAction.Grip), Is.False);
                    Assert.That(frame.EdgeEventCount, Is.EqualTo(2));
                }
                else
                {
                    Assert.That(frame.Edges, Is.EqualTo(IntentEdgeFlags.None));
                    Assert.That(frame.EdgeEventCount, Is.EqualTo(0));
                }
            }

            PlayerIntentFrame nextFrame = buffer.SampleForTick(5, 0.04d, 0.05d);
            Assert.That(nextFrame.Edges, Is.EqualTo(IntentEdgeFlags.None));
            Assert.That(nextFrame.EdgeEventCount, Is.EqualTo(0));
            Assert.That(buffer.PendingSampleCount, Is.EqualTo(0));
        }

        [Test]
        public void LATE_EDGE_CANNOT_BE_REASSIGNED_TO_A_LATER_TICK()
        {
            var buffer = new IntentBuffer();
            buffer.SampleForTick(1, 0d, 0.01d);

            Assert.Throws<InvalidOperationException>(() =>
                buffer.PushEdge(IntentAction.Confirm, IntentEdgeKind.Pressed, 0.005d));
        }

        [Test]
        public void CONTINUOUS_LATEST_AT_TICK_END_PRESERVES_HISTORY()
        {
            var buffer = new IntentBuffer();
            buffer.SetContinuous(IntentAction.Drive, 0.20f, 0.01d);
            buffer.SetContinuous(IntentAction.Drive, 0.80f, 0.03d);

            PlayerIntentFrame first = buffer.SampleForTick(1, 0d, 0.01d);
            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);
            PlayerIntentFrame third = buffer.SampleForTick(3, 0.02d, 0.03d);

            Assert.That(first.Drive01, Is.EqualTo(0.20f));
            Assert.That(second.Drive01, Is.EqualTo(0.20f));
            Assert.That(third.Drive01, Is.EqualTo(0.80f));
        }

        [Test]
        public void CONTINUOUS_MULTIPLE_CHANNELS_PRESERVE_TIMESTAMP_ORDER()
        {
            var buffer = new IntentBuffer();
            buffer.SetContinuous(IntentAction.Drive, 0.20f, 0.01d);
            buffer.SetContinuous(IntentAction.Balance, -0.20f, 0.015d);
            buffer.SetContinuous(IntentAction.Drive, 0.80f, 0.03d);
            buffer.SetContinuous(IntentAction.Balance, 0.60f, 0.035d);

            PlayerIntentFrame first = buffer.SampleForTick(1, 0d, 0.01d);
            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);
            PlayerIntentFrame third = buffer.SampleForTick(3, 0.02d, 0.03d);
            PlayerIntentFrame fourth = buffer.SampleForTick(4, 0.03d, 0.04d);

            Assert.That(first.Drive01, Is.EqualTo(0.20f));
            Assert.That(first.BalanceX, Is.EqualTo(0f));
            Assert.That(second.Drive01, Is.EqualTo(0.20f));
            Assert.That(second.BalanceX, Is.EqualTo(-0.20f));
            Assert.That(third.Drive01, Is.EqualTo(0.80f));
            Assert.That(third.BalanceX, Is.EqualTo(-0.20f));
            Assert.That(fourth.Drive01, Is.EqualTo(0.80f));
            Assert.That(fourth.BalanceX, Is.EqualTo(0.60f));
        }

        [Test]
        public void CONTINUOUS_CATCHUP_4_TICKS_RECONSTRUCTS_CORRECT_VALUES()
        {
            var buffer = new IntentBuffer();
            buffer.SetContinuous(IntentAction.Drive, 0.10f, 0.01d);
            buffer.SetContinuous(IntentAction.Drive, 0.20f, 0.02d);
            buffer.SetContinuous(IntentAction.Drive, 0.30f, 0.03d);
            buffer.SetContinuous(IntentAction.Drive, 0.40f, 0.04d);

            for (ulong tick = 1; tick <= SimulationConstants.MaxCatchUpTicksPerRenderFrame; tick++)
            {
                PlayerIntentFrame frame = buffer.SampleForTick(
                    tick,
                    SimulationConstants.TimeForTick(tick - 1),
                    SimulationConstants.TimeForTick(tick));
                Assert.That(frame.Drive01, Is.EqualTo(tick / 10f));
            }
        }

        [Test]
        public void CONTINUOUS_BUFFER_REMAINS_BOUNDED_WITHOUT_PHYSICS_PROGRESS()
        {
            var buffer = new IntentBuffer();
            int maximumPendingSamples = 0;
            for (int sample = 0; sample < 128; sample++)
            {
                double timestamp = 0.0001d + sample * 0.0003d;
                buffer.SetContinuous(IntentAction.Drive, sample / 127f, timestamp);
                buffer.SetContinuous(IntentAction.Balance, sample / 127f * 2f - 1f, timestamp);
                maximumPendingSamples = Math.Max(maximumPendingSamples, buffer.PendingSampleCount);
            }

            Assert.That(maximumPendingSamples, Is.LessThanOrEqualTo(25));

            PlayerIntentFrame first = buffer.SampleForTick(1, 0d, 0.01d);
            PlayerIntentFrame second = buffer.SampleForTick(2, 0.01d, 0.02d);
            PlayerIntentFrame third = buffer.SampleForTick(3, 0.02d, 0.03d);
            PlayerIntentFrame fourth = buffer.SampleForTick(4, 0.03d, 0.04d);

            Assert.That(first.Drive01, Is.EqualTo(33f / 127f).Within(0.000001f));
            Assert.That(second.Drive01, Is.EqualTo(66f / 127f).Within(0.000001f));
            Assert.That(third.Drive01, Is.EqualTo(99f / 127f).Within(0.000001f));
            Assert.That(fourth.Drive01, Is.EqualTo(1f).Within(0.000001f));
            Assert.That(maximumPendingSamples, Is.EqualTo(8));
            Assert.That(buffer.PendingSampleCount, Is.LessThanOrEqualTo(IntentBuffer.MaxContinuousPendingSampleCount));
        }
    }
}
