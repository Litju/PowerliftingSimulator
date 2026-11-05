using System.Reflection;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class PlayerDepthFeedbackTests
    {
        [Test]
        public void QualifiedDepthRequiresPostCommandPhysicalDescentAndPersistsUntilRetry()
        {
            var feedback = new PlayerDepthFeedback();
            var trace = new SquatTrace();
            trace.BeginRecording();
            Assert.That(feedback.Update(trace, false, 0, 1), Is.EqualTo(PlayerDepthStatus.Waiting));
            trace.Append(Snapshot(1, -0.02f, -0.02f, -0.1f));
            Assert.That(feedback.Update(trace, true, 2, 1), Is.EqualTo(PlayerDepthStatus.Waiting));
            trace.Append(Snapshot(2, -0.02f, -0.02f, 0f));
            Assert.That(feedback.Update(trace, true, 2, 2), Is.EqualTo(PlayerDepthStatus.NotYet), "A pre-onset legal sample must not count.");
            trace.Append(Snapshot(3, 0.02f, 0.02f, -0.1f));
            Assert.That(feedback.Update(trace, true, 2, 3), Is.EqualTo(PlayerDepthStatus.NotYet));
            trace.Append(Snapshot(4, -0.02f, 0.02f, -0.1f));
            Assert.That(feedback.Update(trace, true, 2, 4), Is.EqualTo(PlayerDepthStatus.NotYet), "Both sides must qualify.");
            trace.Append(Snapshot(5, -0.02f, -0.02f, 0f));
            Assert.That(feedback.Update(trace, true, 2, 5), Is.EqualTo(PlayerDepthStatus.Reached));
            trace.Append(Snapshot(6, 0.02f, 0.02f, 0.1f));
            Assert.That(feedback.Update(trace, true, 2, 6), Is.EqualTo(PlayerDepthStatus.Reached));
            Assert.That(feedback.Update(trace, true, 2, 7), Is.EqualTo(PlayerDepthStatus.Reached), "A recorded achievement remains valid when the live sample is stale.");
            trace.EndRecording();
            trace.Clear();
            Assert.That(feedback.Update(trace, true, 2, 7), Is.EqualTo(PlayerDepthStatus.Waiting));
            Assert.That(feedback.Update(trace, false, 0, 7), Is.EqualTo(PlayerDepthStatus.Waiting));
            var retry = new SquatTrace();
            retry.BeginRecording();
            Assert.That(feedback.Update(retry, true, 8, 8), Is.EqualTo(PlayerDepthStatus.Waiting));
            retry.Append(Snapshot(8, 0.02f, 0.02f, 0f));
            Assert.That(feedback.Update(retry, true, 8, 8), Is.EqualTo(PlayerDepthStatus.NotYet));
        }

        [Test]
        public void MissingOrStaleEvidenceDoesNotClaimDepth()
        {
            var feedback = new PlayerDepthFeedback();
            var trace = new SquatTrace();
            trace.BeginRecording();
            trace.Append(Snapshot(1, 0.02f, 0.02f, -0.1f));
            Assert.That(feedback.Update(trace, true, 1, 2), Is.EqualTo(PlayerDepthStatus.Waiting));
            trace.Append(Snapshot(2, 0.02f, 0.02f, -0.1f, depthAvailable: false));
            Assert.That(feedback.Update(trace, true, 1, 2), Is.EqualTo(PlayerDepthStatus.Waiting));
            trace.Append(Snapshot(3, 0.02f, 0.02f, 0f));
            Assert.That(feedback.Update(trace, true, 1, 3), Is.EqualTo(PlayerDepthStatus.Waiting), "A missing post-onset sample cannot establish that depth was never reached.");
            trace.Append(Snapshot(4, -0.02f, -0.02f, 0f));
            Assert.That(feedback.Update(trace, true, 1, 4), Is.EqualTo(PlayerDepthStatus.Reached));

            var gap = new SquatTrace();
            gap.BeginRecording();
            gap.Append(Snapshot(1, 0.02f, 0.02f, -0.1f));
            gap.Append(Snapshot(3, 0.02f, 0.02f, -0.1f));
            Assert.That(new PlayerDepthFeedback().Update(gap, true, 1, 3), Is.EqualTo(PlayerDepthStatus.Waiting));

            var missingBar = new SquatTrace();
            missingBar.BeginRecording();
            missingBar.Append(Snapshot(1, -0.02f, -0.02f, 0f, barAvailable: false));
            Assert.That(new PlayerDepthFeedback().Update(missingBar, true, 1, 1), Is.EqualTo(PlayerDepthStatus.Waiting));
        }

        [TestCase(SquatState.BOTTOM, PlayerDepthStatus.NotYet)]
        [TestCase(SquatState.BOTTOM, PlayerDepthStatus.Waiting)]
        [TestCase(SquatState.REVERSAL, PlayerDepthStatus.NotYet)]
        [TestCase(SquatState.REVERSAL, PlayerDepthStatus.Waiting)]
        public void UnconfirmedBottomDoesNotInstructThePlayerToDrive(SquatState state, PlayerDepthStatus depth)
        {
            MethodInfo method = typeof(PowerliftingGameUiController).GetMethod("GetRefereeBannerAndPrompt", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = { SquatPlayerGamePhase.JUDGED_SQUAT, SquatAttemptLifecycleState.ACTIVE_ATTEMPT, state, depth, null, null, default(Color) };
            method.Invoke(null, args);
            Assert.That((string)args[4], Does.Not.Contain("DRIVE"));
            Assert.That((string)args[5], Does.Not.Contain("drive"));
            Assert.That((string)args[5], Does.Contain("not confirmed"));
            if (state == SquatState.BOTTOM)
                Assert.That((string)args[5], Does.Contain("Lowest programmed position"));
        }

        private static SquatObservationSnapshot Snapshot(ulong tick, float left, float right, float velocity, bool depthAvailable = true, bool barAvailable = true)
        {
            double time = tick * 0.01;
            var intent = new PlayerIntentFrame(tick, time, IntentEdgeFlags.None, 0f, 0f, 0f, 0f, 0f, false, false, false, false, false, false, false);
            var bar = barAvailable
                ? SquatBarObservation.Available(new Vector3Value(0f, 1f, 0f), new Vector3Value(0f, velocity, 0f), QuaternionValue.Identity, new Vector3Value(0f, 0f, 0f), 25f, SquatTelemetryAvailability.NOT_AVAILABLE, false, false, float.NaN)
                : SquatBarObservation.Unavailable();
            var missingJoint = SquatJointObservation.Unavailable();
            return new SquatObservationSnapshot(tick, time, 0.01, true, time, SquatState.DESCENT, SquatPhaseDirection.Descent, 0.5f,
                SquatIntentSnapshot.From(intent), bar, depthAvailable ? new SquatDepthLandmarks(left, right, 0f, 0f) : SquatDepthLandmarks.Unavailable(),
                SquatSupportObservation.Unavailable(), SquatFootObservation.Unavailable(), SquatFootObservation.Unavailable(),
                new SquatJointObservationSet(missingJoint, missingJoint, missingJoint, missingJoint, missingJoint, missingJoint, missingJoint, missingJoint),
                SquatTelemetryValue.UnavailableVector3, SquatTelemetryValue.UnavailableVector3, SquatTelemetryValue.UnavailableQuaternion,
                float.NaN, SquatTelemetryAvailability.NOT_AVAILABLE, false, float.NaN, SquatTelemetryQualityFlags.POST_PHYSICS | SquatTelemetryQualityFlags.RAW);
        }
    }
}
