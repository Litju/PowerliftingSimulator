using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class SquatBarVelocityEventDetectorTests
    {
        private const float SamplePeriodSeconds = 0.01f;

        [Test]
        public void CleanCanonicalSequence_IsDetectedInOrder()
        {
            List<SquatBarVelocitySample> samples = BuildTrace(CanonicalVelocity(220));
            SquatBarVelocityEventDetection result = Detect(samples, new[] { 0f, 0f, 0f, 0f, 0f });

            Assert.That(result.Status, Is.EqualTo(SquatBarVelocityEventStatus.ResolvableStickingRegion));
            Assert.That(result.Events.Count, Is.EqualTo(5));
            Assert.That(result.Events[0].Kind, Is.EqualTo(SquatBarVelocityEventKind.V0));
            Assert.That(result.Events[1].Kind, Is.EqualTo(SquatBarVelocityEventKind.Vmax1));
            Assert.That(result.Events[2].Kind, Is.EqualTo(SquatBarVelocityEventKind.Dmax1));
            Assert.That(result.Events[3].Kind, Is.EqualTo(SquatBarVelocityEventKind.Vmin));
            Assert.That(result.Events[4].Kind, Is.EqualTo(SquatBarVelocityEventKind.Vmax2));
            Assert.That(result.Events[1].FilteredVelocityMetersPerSecond, Is.GreaterThan(0.20f));
            Assert.That(result.Events[3].FilteredVelocityMetersPerSecond, Is.LessThan(result.Events[1].FilteredVelocityMetersPerSecond));
            Assert.That(result.Events[2].FilteredAccelerationMetersPerSecondSquared, Is.LessThan(0f));
        }

        [Test]
        public void MonotonicAscent_ReturnsNoStickingRegion()
        {
            List<SquatBarVelocitySample> samples = BuildTrace(MonotonicVelocity(220));
            SquatBarVelocityEventDetection result = Detect(samples, new[] { 0f, 0f, 0f, 0f, 0f });

            Assert.That(result.Status, Is.EqualTo(SquatBarVelocityEventStatus.NoStickingRegion));
            Assert.That(result.HasStickingRegion, Is.False);
        }

        [Test]
        public void NearlyFlatNoise_DoesNotHallucinateEvents()
        {
            float[] velocity = new float[220];
            float[] noise = new float[80];
            for (int index = 0; index < velocity.Length; index++)
                velocity[index] = ((index * 17) % 11 - 5) * 0.0002f;
            for (int index = 0; index < noise.Length; index++)
                noise[index] = ((index * 17) % 11 - 5) * 0.0002f;

            SquatBarVelocityEventDetection result = Detect(BuildTrace(velocity), noise);

            Assert.That(result.Status, Is.EqualTo(SquatBarVelocityEventStatus.NoStickingRegion));
            Assert.That(result.Events.Count, Is.EqualTo(0));
        }

        [Test]
        public void NumericalWigglesOnMonotonicTrace_DoNotCreateFalseEvents()
        {
            float[] velocity = new float[220];
            for (int index = 0; index < velocity.Length; index++)
            {
                if (index < 50)
                    velocity[index] = -0.20f;
                else if (index == 50)
                    velocity[index] = 0f;
                else
                    velocity[index] = 0.01f + (index - 51) * 0.002f + ((index % 2 == 0) ? 0.0008f : -0.0008f);
            }

            float[] noise = new float[80];
            for (int index = 0; index < noise.Length; index++)
                noise[index] = (index % 2 == 0) ? 0.0008f : -0.0008f;

            SquatBarVelocityEventDetection result = Detect(BuildTrace(velocity), noise);

            Assert.That(result.Status, Is.EqualTo(SquatBarVelocityEventStatus.NoStickingRegion));
            Assert.That(result.Events.Count, Is.EqualTo(0));
        }

        [Test]
        public void OnlyProminentCandidateSurvivesDeterministicSelection()
        {
            float[] velocity = new float[240];
            for (int index = 0; index < velocity.Length; index++)
            {
                if (index < 50)
                    velocity[index] = -0.20f;
                else if (index == 50)
                    velocity[index] = 0f;
                else if (index <= 65)
                    velocity[index] = 0.006f + (index - 51) * 0.0002f;
                else if (index <= 75)
                    velocity[index] = 0.008f - (index - 65) * 0.0002f;
                else if (index <= 105)
                    velocity[index] = 0.10f + (index - 76) * 0.0065f;
                else if (index <= 120)
                    velocity[index] = 0.29f - (index - 105) * 0.012f;
                else if (index <= 165)
                    velocity[index] = 0.11f + (index - 121) * 0.0075f;
                else
                    velocity[index] = 0.44f - (index - 165) * 0.001f;
            }

            float[] noise = new float[80];
            for (int index = 0; index < noise.Length; index++)
                noise[index] = (index % 2 == 0) ? 0.002f : -0.002f;

            SquatBarVelocityEventDetection result = Detect(BuildTrace(velocity), noise);

            Assert.That(result.Status, Is.EqualTo(SquatBarVelocityEventStatus.ResolvableStickingRegion));
            Assert.That(result.Events[1].SampleIndex, Is.GreaterThan(80));
            Assert.That(result.Events[3].SampleIndex, Is.GreaterThan(result.Events[1].SampleIndex));
            Assert.That(result.Events[4].SampleIndex, Is.GreaterThan(result.Events[3].SampleIndex));
        }

        [Test]
        public void RepeatedIdenticalInput_IsNumericallyDeterministic()
        {
            List<SquatBarVelocitySample> samples = BuildTrace(CanonicalVelocity(220));
            float[] noise = { 0f, 0.0001f, -0.0001f, 0f, 0.0001f };
            SquatBarVelocityEventDetection first = Detect(samples, noise);
            SquatBarVelocityEventDetection second = Detect(samples, noise);

            Assert.That(second.Status, Is.EqualTo(first.Status));
            Assert.That(second.NoiseFloor.VelocitySigmaMetersPerSecond,
                Is.EqualTo(first.NoiseFloor.VelocitySigmaMetersPerSecond));
            Assert.That(second.Events.Count, Is.EqualTo(first.Events.Count));
            for (int index = 0; index < first.Events.Count; index++)
            {
                Assert.That(second.Events[index].SampleIndex, Is.EqualTo(first.Events[index].SampleIndex));
                Assert.That(second.Events[index].TimeSeconds, Is.EqualTo(first.Events[index].TimeSeconds));
                Assert.That(second.Events[index].FilteredVelocityMetersPerSecond,
                    Is.EqualTo(first.Events[index].FilteredVelocityMetersPerSecond));
            }
            for (int index = 0; index < first.FilteredVelocityMetersPerSecond.Count; index++)
            {
                Assert.That(second.FilteredVelocityMetersPerSecond[index],
                    Is.EqualTo(first.FilteredVelocityMetersPerSecond[index]));
            }
        }

        private static SquatBarVelocityEventDetection Detect(
            IReadOnlyList<SquatBarVelocitySample> samples,
            IReadOnlyList<float> noise)
        {
            return SquatBarVelocityEventDetector.Detect(
                samples,
                noise,
                SquatBarVelocityDetectorOptions.Default);
        }

        private static List<SquatBarVelocitySample> BuildTrace(IReadOnlyList<float> velocity)
        {
            List<SquatBarVelocitySample> samples = new List<SquatBarVelocitySample>(velocity.Count);
            float position = 1.20f;
            for (int index = 0; index < velocity.Count; index++)
            {
                samples.Add(new SquatBarVelocitySample(
                    index * SamplePeriodSeconds,
                    position,
                    velocity[index]));
                position += velocity[index] * SamplePeriodSeconds;
            }

            return samples;
        }

        private static float[] CanonicalVelocity(int count)
        {
            float[] velocity = new float[count];
            for (int index = 0; index < count; index++)
            {
                if (index < 50)
                    velocity[index] = -0.20f;
                else if (index == 50)
                    velocity[index] = 0f;
                else if (index <= 80)
                    velocity[index] = (index - 50) * 0.01f;
                else if (index <= 95)
                    velocity[index] = 0.30f - (index - 80) * 0.012f;
                else if (index <= 135)
                    velocity[index] = 0.12f + (index - 95) * 0.009f;
                else
                    velocity[index] = 0.48f - (index - 135) * 0.001f;
            }

            return velocity;
        }

        private static float[] MonotonicVelocity(int count)
        {
            float[] velocity = new float[count];
            for (int index = 0; index < count; index++)
            {
                if (index < 50)
                    velocity[index] = -0.20f;
                else if (index == 50)
                    velocity[index] = 0f;
                else
                    velocity[index] = 0.01f + (index - 51) * 0.002f;
            }

            return velocity;
        }
    }
}
