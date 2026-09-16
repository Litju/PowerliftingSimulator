using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM12SquatAttemptLifecycleTests
    {
        private const double StepSeconds = 0.01d;

        [Test]
        public void ATTEMPT_RECORD_REQUIRES_FROZEN_TRACE()
        {
            SquatTrace trace = BuildTrace();
            SquatAttemptLifecycle lifecycle = BuildCompletedLifecycle();

            Assert.Throws<System.InvalidOperationException>(() => lifecycle.FinalizeAttempt(trace));
        }

        [Test]
        public void RULE_AND_FAILURE_USE_SAME_FROZEN_TRACE()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.Trace, Is.SameAs(trace));
            Assert.That(record.TraceSampleCount, Is.EqualTo(trace.Count));
            Assert.That(record.TraceSchema, Is.EqualTo(SquatTrace.SchemaVersion));
            Assert.That(record.Judgment.EvaluatedTraceSchema, Is.EqualTo(record.TraceSchema));
            Assert.That(record.FailureResult.EvaluatedTraceSchema, Is.EqualTo(record.TraceSchema));
            Assert.That(record.FailureResult.TraceCount, Is.EqualTo(record.TraceSampleCount));
            Assert.That(record.IsTruthFrozen, Is.True);
        }

        [Test]
        public void NO_TRACE_APPEND_AFTER_FINALIZATION()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.Trace.IsTruthSealed, Is.True);
            Assert.Throws<System.InvalidOperationException>(() => record.Trace.Append(Snapshot(15ul, 1.02f, 0f, -0.02f, -0.02f, 0f)));
            Assert.Throws<System.InvalidOperationException>(() => record.Trace.Clear());
        }

        [Test]
        public void FINALIZE_IS_DETERMINISTIC()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptLifecycle lifecycle = BuildCompletedLifecycle();

            SquatAttemptRecord first = lifecycle.FinalizeAttempt(trace);
            SquatAttemptRecord second = lifecycle.FinalizeAttempt(trace);

            Assert.That(second, Is.SameAs(first));
            Assert.That(lifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.COMPLETE));
        }

        [Test]
        public void COMMAND_TIMELINE_IS_TRACE_COVERED()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.CommandTimeline.Count, Is.EqualTo(3));
            Assert.That(record.CommandTimeline.Get(SquatRuleCommandKind.SquatCommandIssued).SimulationTick, Is.EqualTo(3ul));
            Assert.That(record.CommandTimeline.Get(SquatRuleCommandKind.RackCommandIssued).SimulationTick, Is.EqualTo(13ul));
            Assert.That(record.CommandTimeline.Get(SquatRuleCommandKind.RerackStarted).SimulationTick, Is.EqualTo(14ul));
            Assert.That(record.CommandTimelineCovered, Is.True);
            Assert.That(record.EventTicks.SquatCommandTick, Is.EqualTo(3ul));
            Assert.That(record.EventTicks.RackCommandTick, Is.EqualTo(13ul));
            Assert.That(record.EventTicks.RerackStartedTick, Is.EqualTo(14ul));
        }

        [Test]
        public void ATTEMPT_RECORD_PRESERVES_ALL_VERSION_PROVENANCE()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.AttemptLifecycleVersion, Is.EqualTo("GAM12_P4_ATTEMPT_LIFECYCLE_V1"));
            Assert.That(record.AttemptRecordVersion, Is.EqualTo("GAM12_P4_ATTEMPT_RECORD_V1"));
            Assert.That(record.ObservationSchema, Is.EqualTo(SquatObservationSnapshot.SchemaId));
            Assert.That(record.ObservationProvenanceVersion, Is.EqualTo(SquatObservationSnapshot.ProvenanceVersion));
            Assert.That(record.RuleSetId, Is.EqualTo("IPF_2026_V3_SQUAT_GAME_V1"));
            Assert.That(record.RuleImplementationVersion, Is.EqualTo("GAM12_P2A1_RULE_PROCESSOR_V1"));
            Assert.That(record.RuleToleranceVersion, Is.EqualTo(SquatRuleToleranceSet.DefaultVersion));
            Assert.That(record.FailureModelVersion, Is.EqualTo("GAM12_P3A1_FAILURE_MODEL_V1"));
            Assert.That(record.FailureCalibrationVersion, Is.EqualTo("GAM12_P3A1_FAILURE_CALIBRATION_PROVISIONAL_V1"));
            Assert.That(record.FailurePrecedenceVersion, Is.EqualTo("GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1"));
            Assert.That(record.TraceFreezeTick, Is.EqualTo(14ul));
            Assert.That(record.TruthFreezeTick, Is.EqualTo(14ul));
            Assert.That(record.SafetyHandoffEligible, Is.True);
        }

        [Test]
        public void SAFETY_HANDOFF_NOT_ELIGIBLE_BEFORE_ATTEMPT_TRUTH_FREEZE()
        {
            SquatAttemptLifecycle lifecycle = BuildCompletedLifecycle();

            Assert.That(lifecycle.SafetyHandoffEligible, Is.False);
            Assert.That(lifecycle.Record, Is.Null);
        }

        [Test]
        public void RULE_NO_LIFT_CAN_COEXIST_WITH_NO_PHYSICAL_FAILURE()
        {
            SquatTrace trace = BuildTrace(shallowDepth: true);
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.RuleOutcome, Is.EqualTo(SquatJudgmentOutcome.NO_LIFT));
            Assert.That(record.Judgment.PrimaryViolationKind, Is.EqualTo(SquatRuleViolationKind.INSUFFICIENT_DEPTH));
            Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(record.FailureResult.FailureRecord, Is.Null);
        }

        [Test]
        public void PHYSICAL_FAILURE_CAN_COEXIST_WITH_RULE_RESULT()
        {
            SquatTrace trace = BuildTrace(saddleBroken: true);
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.RuleOutcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(record.FailureResult.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.POSTURE_OR_BAR_LOSS));
        }

        [Test]
        public void MISSING_EVIDENCE_REMAINS_UNDETERMINED()
        {
            SquatTrace trace = BuildTrace(barAvailable: false);
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.Judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.INSUFFICIENT_EVIDENCE));
            Assert.That(record.Judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.UNDETERMINED));
            Assert.That(record.FailureResult.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INSUFFICIENT_EVIDENCE));
            Assert.That(record.FailureResult.Outcome, Is.EqualTo(SquatFailureResultKind.UNDETERMINED));
            Assert.That(record.EventTicks.PhysicalDescentOnsetTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
            Assert.That(record.EventTicks.BottomTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
        }

        [Test]
        public void SAFETY_HANDOFF_ELIGIBLE_ONLY_AFTER_FAILURE_TRUTH_FREEZE()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptLifecycle lifecycle = BuildCompletedLifecycle();

            Assert.That(lifecycle.SafetyHandoffEligible, Is.False);
            SquatAttemptRecord record = lifecycle.FinalizeAttempt(trace);

            Assert.That(record.SafetyHandoffEligible, Is.True);
            Assert.That(record.FailureResult, Is.Not.Null);
            Assert.That(record.Trace.IsTruthSealed, Is.True);
        }

        [Test]
        public void PRESENTATION_CANNOT_MUTATE_ATTEMPT_TRUTH()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);
            int count = record.TraceSampleCount;
            SquatJudgmentOutcome outcome = record.RuleOutcome;

            Assert.Throws<System.InvalidOperationException>(() => record.Trace.Clear());
            Assert.That(record.TraceSampleCount, Is.EqualTo(count));
            Assert.That(record.RuleOutcome, Is.EqualTo(outcome));
        }

        [Test]
        public void EARLY_SETUP_HISTORY_CANNOT_CHANGE_LOCKOUT_REFERENCE()
        {
            // Setup/walkout samples are intentionally outside the canonical
            // trace, so P3's standing reference is the trace's own first
            // qualified sample and nothing earlier. This is exercised by
            // shifting only the recorded standing height: the same relative
            // attempt must reach lockout at the same tick, while a recorded
            // window that starts far above its own top region must not.
            SquatTrace atReference = BuildTrace();
            atReference.EndRecording();
            SquatAttemptRecord recordAtReference = BuildCompletedLifecycle().FinalizeAttempt(atReference);

            SquatTrace shiftedReference = BuildTrace(standingHeightOffsetM: 0.40f);
            shiftedReference.EndRecording();
            SquatAttemptRecord recordShiftedReference =
                BuildCompletedLifecycle().FinalizeAttempt(shiftedReference);

            Assert.That(recordAtReference.FirstTraceTick, Is.EqualTo(0ul));
            Assert.That(recordShiftedReference.FirstTraceTick, Is.EqualTo(0ul));

            // The unshifted attempt returns to its own recorded standing height
            // and locks out.
            Assert.That(
                recordAtReference.PhysicalFailureOutcome,
                Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));

            // The shifted attempt never returns to the height its own first
            // recorded sample established, so the reference is read from the
            // trace rather than assumed, and no lockout is fabricated.
            Assert.That(
                recordShiftedReference.PhysicalFailureOutcome,
                Is.Not.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(
                recordShiftedReference.FailureResult.PrimaryFailureKind,
                Is.Not.EqualTo(SquatFailureKind.FAILED_LOCKOUT),
                "Terminality is PHYSICAL_LOCKOUT-shaped here and the completion region is never entered, " +
                "so no terminal lockout postcondition may be invented.");
        }

        [Test]
        public void LIFECYCLE_IS_THE_TERMINAL_CONTEXT_AUTHORITY_FOR_PHYSICAL_FAILURE()
        {
            SquatTrace trace = BuildTrace();
            trace.EndRecording();
            SquatAttemptRecord record = BuildCompletedLifecycle().FinalizeAttempt(trace);

            Assert.That(record.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.PHYSICAL_LOCKOUT));
            Assert.That(record.TerminalTick, Is.EqualTo(10ul));
            Assert.That(record.TerminalTimeSeconds, Is.EqualTo(0.10d).Within(1e-9d));
            Assert.That(
                record.FailureResult.TerminalContextStatus,
                Is.EqualTo(SquatFailureTerminalContextStatus.TRACE_COVERED));
            Assert.That(record.FailureResult.TerminalPostconditionEvaluated, Is.True);
            Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void TERMINAL_FAILED_LOCKOUT_LATCHES_AT_THE_LIFECYCLE_TERMINAL_TICK()
        {
            SquatTrace trace = BuildNoLockoutTrace();
            SquatAttemptLifecycle lifecycle = new SquatAttemptLifecycle();
            lifecycle.BeginStartWindow();
            lifecycle.ObserveStartWindowSample(0ul);
            lifecycle.ObserveStartWindowSample(1ul);
            lifecycle.ObserveStartWindowSample(2ul);
            lifecycle.IssueSquatCommand(3ul, 0.03d);
            lifecycle.BeginActiveAttempt();
            lifecycle.Terminate(14ul, 0.14d, SquatAttemptTerminalReason.TIMEOUT);

            SquatAttemptRecord record = lifecycle.FinalizeAttempt(trace);

            Assert.That(record.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.TIMEOUT));
            Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(record.FailureResult.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            Assert.That(record.EventTicks.FailureLatchTick, Is.EqualTo(record.TerminalTick));
            Assert.That(record.EventTicks.FailureOnsetTick, Is.LessThan(record.EventTicks.FailureLatchTick));
        }

        [Test]
        public void AN_UNTERMINATED_ATTEMPT_CANNOT_PRODUCE_A_TERMINAL_FAILED_LOCKOUT()
        {
            SquatTrace trace = BuildNoLockoutTrace();
            SquatFailureResult result = new SquatFailureDetector().Evaluate(trace);

            Assert.That(
                result.TerminalContextStatus,
                Is.EqualTo(SquatFailureTerminalContextStatus.NOT_TERMINAL));
            Assert.That(result.FailureRecord, Is.Null);
            Assert.That(result.Outcome, Is.Not.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
        }

        [Test]
        public void DESCENT_COLLAPSE_PROVENANCE_MATCHES_REAL_SELECTING_PREDICATE()
        {
            SquatTrace trace = BuildCollapseTrace();
            SquatFailureResult result = new SquatFailureDetector().Evaluate(trace);

            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.DESCENT_COLLAPSE));
            SquatFailureEvent primary = result.FailureRecord.Primary;
            Assert.That((uint)(primary.EvidenceChannels & SquatFailureEvidenceChannel.FOOT_CONTACT), Is.EqualTo(0u));
            Assert.That((uint)(primary.EvidenceChannels & SquatFailureEvidenceChannel.DRIVE_DEMAND), Is.EqualTo(0u));
            for (int index = 0; index < primary.Thresholds.Count; index++)
                Assert.That(primary.Thresholds[index].Name, Is.Not.EqualTo("modeled_demand"));
        }

        private static SquatAttemptLifecycle BuildCompletedLifecycle()
        {
            SquatAttemptLifecycle lifecycle = new SquatAttemptLifecycle();
            lifecycle.BeginStartWindow();
            lifecycle.ObserveStartWindowSample(0ul);
            lifecycle.ObserveStartWindowSample(1ul);
            lifecycle.ObserveStartWindowSample(2ul);
            lifecycle.IssueSquatCommand(3ul, 0.03d);
            lifecycle.BeginActiveAttempt();
            lifecycle.MarkPhysicalTerminalCondition(
                10ul,
                0.10d,
                SquatAttemptTerminalReason.PHYSICAL_LOCKOUT);
            lifecycle.IssueRackCommand(13ul, 0.13d);
            lifecycle.StartRerack(14ul, 0.14d);
            return lifecycle;
        }

        private static SquatTrace BuildTrace(
            bool shallowDepth = false,
            bool barAvailable = true,
            bool saddleBroken = false,
            float standingHeightOffsetM = 0f)
        {
            SquatTrace trace = new SquatTrace(15);
            trace.BeginRecording();
            float depth = shallowDepth ? 0f : -0.02f;
            trace.Append(Snapshot(0ul, 1.00f + standingHeightOffsetM, 0f, 0f, 0f, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(1ul, 1.00f + standingHeightOffsetM, 0f, 0f, 0f, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(2ul, 1.00f + standingHeightOffsetM, 0f, 0f, 0f, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(3ul, 1.00f + standingHeightOffsetM, 0f, 0f, 0f, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(4ul, 0.90f, -0.12f, 0f, 0f, 0.30f, barAvailable, saddleBroken));
            trace.Append(Snapshot(5ul, 0.80f, -0.12f, depth, depth, 0.60f, barAvailable, saddleBroken));
            trace.Append(Snapshot(6ul, 0.79f, 0f, depth, depth, 0.90f, barAvailable, saddleBroken));
            trace.Append(Snapshot(7ul, 0.84f, 0.12f, depth, depth, 0.70f, barAvailable, saddleBroken));
            trace.Append(Snapshot(8ul, 0.90f, 0.12f, depth, depth, 0.40f, barAvailable, saddleBroken));
            trace.Append(Snapshot(9ul, 1.00f, 0.10f, depth, depth, 0.10f, barAvailable, saddleBroken));
            trace.Append(Snapshot(10ul, 1.02f, 0.01f, depth, depth, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(11ul, 1.02f, 0f, depth, depth, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(12ul, 1.02f, 0f, depth, depth, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(13ul, 1.02f, 0f, depth, depth, 0f, barAvailable, saddleBroken));
            trace.Append(Snapshot(14ul, 1.02f, 0f, depth, depth, 0f, barAvailable, saddleBroken));
            return trace;
        }

        private static SquatTrace BuildCollapseTrace()
        {
            SquatTrace trace = new SquatTrace(6);
            trace.BeginRecording();
            trace.Append(Snapshot(0ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(1ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(2ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(3ul, 0.90f, -0.80f, 0f, 0f, 0.30f, true, false, false));
            trace.Append(Snapshot(4ul, 0.70f, -0.80f, 0f, 0f, 0.60f, true, false, false));
            trace.Append(Snapshot(5ul, 0.50f, -0.80f, 0f, 0f, 0.90f, true, false, false));
            trace.EndRecording();
            return trace;
        }

        /// <summary>
        /// The bar reaches the physical completion region but the athlete never
        /// satisfies the bilateral posture bounds, so lockout is never achieved.
        /// </summary>
        private static SquatTrace BuildNoLockoutTrace()
        {
            SquatTrace trace = new SquatTrace(15);
            trace.BeginRecording();
            trace.Append(Snapshot(0ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(1ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(2ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(3ul, 1.00f, 0f, 0f, 0f, 0f));
            trace.Append(Snapshot(4ul, 0.90f, -0.12f, 0f, 0f, 0.30f));
            trace.Append(Snapshot(5ul, 0.80f, -0.12f, -0.02f, -0.02f, 0.60f));
            trace.Append(Snapshot(6ul, 0.79f, 0f, -0.02f, -0.02f, 0.90f));
            trace.Append(Snapshot(7ul, 0.84f, 0.12f, -0.02f, -0.02f, 0.70f));
            trace.Append(Snapshot(8ul, 0.90f, 0.12f, -0.02f, -0.02f, 0.40f));
            // Ascent establishes on tick 9, which is also the first sample in
            // the completion region. Posture never reaches lockout.
            trace.Append(Snapshot(9ul, 0.97f, 0.12f, -0.02f, -0.02f, 0.30f));
            for (ulong tick = 10ul; tick <= 14ul; tick++)
                trace.Append(Snapshot(tick, 0.97f, 0f, -0.02f, -0.02f, 0.30f));
            trace.EndRecording();
            return trace;
        }

        private static SquatObservationSnapshot Snapshot(
            ulong tick,
            float barY,
            float barVelocityY,
            float leftDepthM,
            float rightDepthM,
            float kneeAngleRad,
            bool barAvailable = true,
            bool saddleBroken = false,
            bool hasSupport = true)
        {
            double time = tick * StepSeconds;
            PlayerIntentFrame intent = new PlayerIntentFrame(
                tick,
                time,
                IntentEdgeFlags.None,
                0.5f,
                1f,
                1f,
                0f,
                1f,
                true,
                true,
                true,
                false,
                true,
                false,
                false);
            SquatBarObservation bar = barAvailable
                ? SquatBarObservation.Available(
                    new Vector3Value(0f, barY, 0f),
                    new Vector3Value(0f, barVelocityY, 0f),
                    QuaternionValue.Identity,
                    new Vector3Value(0f, 0f, 0f),
                    25f,
                    SquatTelemetryAvailability.AVAILABLE,
                    true,
                    saddleBroken,
                    0f)
                : SquatBarObservation.Unavailable();
            SquatSupportObservation support = new SquatSupportObservation(
                SquatTelemetryAvailability.AVAILABLE,
                new Vector3Value(0f, 1f, 0f),
                new Vector3Value(0f, 0f, 0f),
                125f,
                SquatTelemetryAvailability.AVAILABLE,
                hasSupport,
                hasSupport ? -0.30f : float.NaN,
                hasSupport ? 0.30f : float.NaN,
                hasSupport ? -0.20f : float.NaN,
                hasSupport ? 0.20f : float.NaN,
                hasSupport ? 0f : float.NaN,
                hasSupport ? 2 : 0,
                SquatTelemetryAvailability.NOT_AVAILABLE,
                SquatTelemetryValue.UnavailableVector3,
                0f);
            SquatFootObservation foot = SquatFootObservation.Available(true, 1, 1, 0f, 0f);
            SquatJointObservation joint = SquatJointObservation.Available(
                kneeAngleRad,
                new Vector3Value(0f, 0f, 0f),
                0f,
                kneeAngleRad,
                0f,
                0f,
                100f,
                1f,
                1f);
            SquatJointObservationSet joints = new SquatJointObservationSet(
                joint,
                joint,
                joint,
                joint,
                SquatJointObservation.Available(0f, new Vector3Value(0f, 0f, 0f), 0f, 0f, 0f, 0f, 100f, 1f, 1f),
                SquatJointObservation.Available(0f, new Vector3Value(0f, 0f, 0f), 0f, 0f, 0f, 0f, 100f, 1f, 1f),
                joint,
                joint);
            return new SquatObservationSnapshot(
                tick,
                time,
                StepSeconds,
                true,
                time,
                SquatState.ASCENT,
                SquatPhaseDirection.Ascent,
                0.5f,
                SquatIntentSnapshot.From(intent),
                bar,
                new SquatDepthLandmarks(
                    leftDepthM,
                    rightDepthM,
                    0f,
                    0f,
                    SquatDepthGeometry.DefaultDepthMarginM),
                support,
                foot,
                foot,
                joints,
                new Vector3Value(0f, barY, 0f),
                new Vector3Value(0f, barVelocityY, 0f),
                QuaternionValue.Identity,
                0f,
                SquatTelemetryAvailability.AVAILABLE,
                false,
                0f,
                SquatTelemetryQualityFlags.POST_PHYSICS |
                SquatTelemetryQualityFlags.RAW |
                SquatTelemetryQualityFlags.ATTEMPT_RELATIVE_TIME |
                (barAvailable ? SquatTelemetryQualityFlags.BAR_AVAILABLE : SquatTelemetryQualityFlags.NONE) |
                SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE |
                SquatTelemetryQualityFlags.SUPPORT_CONTACT_PRESENT |
                SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE |
                SquatTelemetryQualityFlags.JOINTS_AVAILABLE |
                SquatTelemetryQualityFlags.DRIVE_DIAGNOSTICS_AVAILABLE |
                SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE |
                SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE);
        }
    }
}
