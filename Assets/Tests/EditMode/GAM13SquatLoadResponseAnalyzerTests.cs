using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13SquatLoadResponseAnalyzerTests
    {
        private const double StepSeconds = 0.01d;
        private const float StandingY = 1.40f;

        [Test]
        public void ANALYSIS_REQUIRES_A_FROZEN_TRACE()
        {
            SquatTrace trace = new SquatTrace(4);
            trace.BeginRecording();
            trace.Append(Snapshot(0ul, StandingY, 0f, 25f));

            Assert.Throws<InvalidOperationException>(() =>
                SquatLoadResponseAnalyzer.Analyze(trace, SquatLoadResponseMetrics.NotAvailableTick));
        }

        [Test]
        public void CONCENTRIC_EVENTS_COME_FROM_RAW_BAR_MOTION()
        {
            List<float> velocities = StandingThenDescent();
            int bottomIndex = velocities.Count - 1;
            velocities.AddRange(SmoothAscent(peak: 0.40f, samples: 120));
            SquatTrace trace = Build(velocities, 25f, out float[] positions);
            ulong lockoutTick = (ulong)(positions.Length - 1);

            SquatLoadResponseMetrics metrics = SquatLoadResponseAnalyzer.Analyze(trace, lockoutTick);

            Assert.That(metrics.AllEvidenceFinite, Is.True);
            Assert.That(metrics.DescentOnsetTick, Is.Not.EqualTo(SquatLoadResponseMetrics.NotAvailableTick));
            Assert.That(metrics.BottomTick, Is.EqualTo((ulong)IndexOfMinimum(positions)));
            Assert.That(Math.Abs((long)metrics.BottomTick - bottomIndex), Is.LessThanOrEqualTo(1));
            Assert.That(metrics.AscentEstablishmentTick, Is.GreaterThan(metrics.BottomTick));
            Assert.That(metrics.LockoutTick, Is.EqualTo(lockoutTick));
            float expectedDuration = (float)((lockoutTick - metrics.BottomTick) * StepSeconds);
            Assert.That(metrics.ConcentricDurationSeconds, Is.EqualTo(expectedDuration).Within(1e-5f));
            float expectedMean = (positions[lockoutTick] - positions[metrics.BottomTick]) / expectedDuration;
            Assert.That(metrics.MeanConcentricVelocityMps, Is.EqualTo(expectedMean).Within(1e-4f));
            Assert.That(metrics.PeakConcentricVelocityMps, Is.EqualTo(0.40f).Within(0.01f));
            Assert.That(metrics.Sticking, Is.EqualTo(SquatStickingClassification.NO_RESOLVABLE_STICKING));
        }

        [Test]
        public void RESOLVABLE_DIP_WITH_LOCKOUT_IS_RECOVERABLE_STICKING()
        {
            List<float> velocities = StandingThenDescent();
            velocities.AddRange(DippedAscent());
            SquatTrace trace = Build(velocities, 170f, out float[] positions);

            SquatLoadResponseMetrics metrics = SquatLoadResponseAnalyzer.Analyze(trace, (ulong)(positions.Length - 1));

            Assert.That(metrics.Sticking, Is.EqualTo(SquatStickingClassification.RECOVERABLE_STICKING));
            Assert.That(metrics.StickingVminMps, Is.LessThan(metrics.StickingVmax1Mps));
            Assert.That(metrics.StickingVmax2Mps, Is.GreaterThan(metrics.StickingVminMps));
            Assert.That(metrics.StickingVminTick, Is.GreaterThan(metrics.StickingVmax1Tick));
            Assert.That(metrics.StickingVmax2Tick, Is.GreaterThan(metrics.StickingVminTick));
            Assert.That(metrics.StickingVelocityReductionFraction, Is.GreaterThan(0.5f));
        }

        [Test]
        public void THE_SAME_DIP_WITHOUT_LOCKOUT_IS_NOT_SUCCESSFUL_STICKING()
        {
            List<float> velocities = StandingThenDescent();
            velocities.AddRange(DippedAscent());
            SquatTrace trace = Build(velocities, 170f, out _);

            SquatLoadResponseMetrics metrics = SquatLoadResponseAnalyzer.Analyze(trace, SquatLoadResponseMetrics.NotAvailableTick);

            Assert.That(metrics.Sticking, Is.EqualTo(SquatStickingClassification.NOT_APPLICABLE_NO_LOCKOUT));
            Assert.That(metrics.PhysicalLockout, Is.False);
            Assert.That(float.IsNaN(metrics.ConcentricDurationSeconds), Is.True);
        }

        [Test]
        public void MISSING_EVENTS_ARE_NOT_AVAILABLE_NOT_ZERO()
        {
            List<float> velocities = new List<float>();
            for (int index = 0; index < 60; index++)
                velocities.Add(Noise(index));
            SquatTrace trace = Build(velocities, 25f, out _);

            SquatLoadResponseMetrics metrics = SquatLoadResponseAnalyzer.Analyze(trace, SquatLoadResponseMetrics.NotAvailableTick);

            Assert.That(metrics.DescentOnsetTick, Is.EqualTo(SquatLoadResponseMetrics.NotAvailableTick));
            Assert.That(metrics.BottomTick, Is.EqualTo(SquatLoadResponseMetrics.NotAvailableTick));
            Assert.That(metrics.AscentEstablishmentTick, Is.EqualTo(SquatLoadResponseMetrics.NotAvailableTick));
            Assert.That(float.IsNaN(metrics.MeanConcentricVelocityMps), Is.True);
            Assert.That(float.IsNaN(metrics.MaximumModeledDemand), Is.True);
        }

        [Test]
        public void LOAD_METADATA_DOES_NOT_CHANGE_ANALYSIS()
        {
            List<float> velocities = StandingThenDescent();
            velocities.AddRange(DippedAscent());
            SquatTrace light = Build(velocities, 25f, out float[] positions);
            SquatTrace heavy = Build(velocities, 300f, out _);
            ulong lockout = (ulong)(positions.Length - 1);

            SquatLoadResponseMetrics a = SquatLoadResponseAnalyzer.Analyze(light, lockout);
            SquatLoadResponseMetrics b = SquatLoadResponseAnalyzer.Analyze(heavy, lockout);

            Assert.That(b.BottomTick, Is.EqualTo(a.BottomTick));
            Assert.That(b.AscentEstablishmentTick, Is.EqualTo(a.AscentEstablishmentTick));
            Assert.That(b.MeanConcentricVelocityMps, Is.EqualTo(a.MeanConcentricVelocityMps));
            Assert.That(b.Sticking, Is.EqualTo(a.Sticking));
            Assert.That(b.StickingVminTick, Is.EqualTo(a.StickingVminTick));
        }

        private static List<float> StandingThenDescent()
        {
            List<float> velocities = new List<float>();
            for (int index = 0; index < 60; index++)
                velocities.Add(Noise(index));
            for (int index = 0; index < 150; index++)
                velocities.Add(-0.30f * (float)Math.Sin(Math.PI * index / 150d));
            return velocities;
        }

        private static IEnumerable<float> SmoothAscent(float peak, int samples)
        {
            for (int index = 0; index < samples; index++)
                yield return peak * (float)Math.Sin(Math.PI * (index + 1) / (samples + 1));
        }

        /// <summary>vmax1 → deep minimum → vmax2 → settle, all upward.</summary>
        private static IEnumerable<float> DippedAscent()
        {
            for (int index = 0; index < 40; index++)
                yield return 0.35f * (float)Math.Sin(0.5d * Math.PI * (index + 1) / 40d);
            for (int index = 0; index < 60; index++)
                yield return 0.05f + 0.30f * (float)(0.5d + 0.5d * Math.Cos(Math.PI * (index + 1) / 60d));
            for (int index = 0; index < 60; index++)
                yield return 0.05f + 0.35f * (float)Math.Sin(Math.PI * (index + 1) / 60d);
            for (int index = 0; index < 20; index++)
                yield return 0.05f * (1f - (index + 1) / 20f);
        }

        private static float Noise(int index) => (index % 3 - 1) * 0.002f;

        private static int IndexOfMinimum(float[] values)
        {
            int best = 0;
            for (int index = 1; index < values.Length; index++)
            {
                if (values[index] < values[best])
                    best = index;
            }
            return best;
        }

        private static SquatTrace Build(IReadOnlyList<float> velocities, float loadKg, out float[] positions)
        {
            positions = new float[velocities.Count];
            SquatTrace trace = new SquatTrace(velocities.Count);
            trace.BeginRecording();
            float y = StandingY;
            for (int index = 0; index < velocities.Count; index++)
            {
                if (index > 0)
                    y += velocities[index] * (float)StepSeconds;
                positions[index] = y;
                trace.Append(Snapshot((ulong)index, y, velocities[index], loadKg));
            }
            trace.EndRecording();
            return trace;
        }

        private static SquatObservationSnapshot Snapshot(ulong tick, float barY, float barVelocityY, float loadKg)
        {
            double time = tick * StepSeconds;
            PlayerIntentFrame intent = new PlayerIntentFrame(
                tick, time, IntentEdgeFlags.None, 0f, 0f, 1f, 0f, 0f,
                false, false, true, false, false, false, false);
            SquatBarObservation bar = SquatBarObservation.Available(
                new Vector3Value(0f, barY, 0f),
                new Vector3Value(0f, barVelocityY, 0f),
                QuaternionValue.Identity,
                new Vector3Value(0f, 0f, 0f),
                loadKg,
                SquatTelemetryAvailability.AVAILABLE,
                true,
                false,
                0.005f);
            float depth = barY < 1.00f ? -0.02f : 0.10f;
            SquatDepthLandmarks landmarks = new SquatDepthLandmarks(depth, depth, 0f, 0f, SquatDepthGeometry.DefaultDepthMarginM);
            SquatSupportObservation support = new SquatSupportObservation(
                SquatTelemetryAvailability.AVAILABLE,
                new Vector3Value(0f, 1f, 0f),
                new Vector3Value(0f, 0f, 0f),
                100f + loadKg,
                SquatTelemetryAvailability.AVAILABLE,
                true,
                -0.15f, 0.13f, -0.20f, 0.20f, 0f, 8,
                SquatTelemetryAvailability.NOT_AVAILABLE,
                SquatTelemetryValue.UnavailableVector3,
                0f);
            SquatJointObservation joint = SquatJointObservation.Available(
                0f, new Vector3Value(0f, 0f, 0f), 0f, 0f, 0.1f, 0.3f, 500f, 1f, 1f);
            SquatJointObservationSet joints = new SquatJointObservationSet(joint, joint, joint, joint, joint, joint, joint, joint);
            SquatFootObservation foot = SquatFootObservation.Available(true, 4, 4, 0f, 0f);
            SquatTelemetryQualityFlags quality =
                SquatTelemetryQualityFlags.POST_PHYSICS | SquatTelemetryQualityFlags.RAW |
                SquatTelemetryQualityFlags.ATTEMPT_RELATIVE_TIME | SquatTelemetryQualityFlags.BAR_AVAILABLE |
                SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE | SquatTelemetryQualityFlags.SUPPORT_CONTACT_PRESENT |
                SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE | SquatTelemetryQualityFlags.JOINTS_AVAILABLE |
                SquatTelemetryQualityFlags.DRIVE_DIAGNOSTICS_AVAILABLE |
                SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE | SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE;

            return new SquatObservationSnapshot(
                tick, time, StepSeconds, true, time,
                SquatState.ASCENT, SquatPhaseDirection.Ascent, 0f,
                SquatIntentSnapshot.From(intent),
                bar, landmarks, support, foot, foot, joints,
                new Vector3Value(0f, barY - 0.4f, 0f),
                new Vector3Value(0f, barVelocityY, 0f),
                QuaternionValue.Identity,
                0.1f,
                SquatTelemetryAvailability.AVAILABLE,
                false,
                0.3f,
                quality);
        }
    }
}
