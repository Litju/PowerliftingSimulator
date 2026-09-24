using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM12SquatFailureDetectorTests
    {
        private const double StepSeconds = 0.01d;

        [Test]
        public void GOOD_PHYSICAL_ATTEMPT_HAS_NO_FAILURE()
        {
            SquatFailureResult result = Evaluate(BuildGoodAttempt());

            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.EVALUABLE));
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.HasFailure, Is.False);
            Assert.That(result.FailureRecord, Is.Null);
        }

        [Test]
        public void FORWARD_BALANCE_LOSS()
        {
            SquatFailureResult result = Evaluate(BuildBalanceLoss(forward: true));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(result.FailureRecord.PrimaryDirection, Is.EqualTo(SquatFailureDirection.FORWARD));
            Assert.That(result.FailureRecord.OnsetTick, Is.EqualTo(3ul));
            Assert.That(result.FailureRecord.LatchedTick, Is.EqualTo(
                3ul + (ulong)SquatFailureCalibration.Default.BalancePersistenceTicks - 1ul));
            Assert.That(result.FailureRecord.Primary.OnsetContext.SimulationTick, Is.EqualTo(3ul));
            Assert.That(result.FailureRecord.Primary.LatchedContext.SimulationTick, Is.EqualTo(result.FailureRecord.LatchedTick));
        }

        [Test]
        public void BACKWARD_BALANCE_LOSS()
        {
            SquatFailureResult result = Evaluate(BuildBalanceLoss(forward: false));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(result.FailureRecord.PrimaryDirection, Is.EqualTo(SquatFailureDirection.BACKWARD));
        }

        [Test]
        public void ONE_TICK_BALANCE_EXCURSION_DOES_NOT_FAIL()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].ComZ = 0.33f;
            samples[6].ComVelocityZ = SquatFailureCalibration.Default.BalanceOutwardComVelocityMps + 0.01f;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void OUTSIDE_SUPPORT_BUT_RECOVERING_VELOCITY_DOES_NOT_IMMEDIATELY_FAIL()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].ComZ = 0.33f;
            samples[6].ComVelocityZ = -0.10f;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void DESCENT_COLLAPSE()
        {
            List<SampleSpec> samples = new List<SampleSpec>
            {
                Sample(0ul, 1.00f, 0f),
                Sample(1ul, 1.00f, 0f),
                Sample(2ul, 1.00f, 0f),
                Sample(3ul, 0.90f, -0.80f),
                Sample(4ul, 0.70f, -0.80f),
                Sample(5ul, 0.50f, -0.80f)
            };
            for (int index = 3; index < samples.Count; index++)
                samples[index].HasSupport = false;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.DESCENT_COLLAPSE));
        }

        [Test]
        public void FAST_CONTROLLED_DESCENT_IS_NOT_COLLAPSE()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[3].BarVelocityY = -0.60f;
            samples[4].BarVelocityY = -0.60f;
            samples[3].PelvisVelocityY = -0.60f;
            samples[4].PelvisVelocityY = -0.60f;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void LEGAL_BOTTOM_WITH_SUCCESSFUL_REVERSAL_IS_NOT_FAILED_REVERSAL()
        {
            SquatFailureResult result = Evaluate(BuildGoodAttempt());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void LEGAL_BOTTOM_DRIVE_WITH_NO_RECOVERY_TIMES_OUT_AS_FAILED_REVERSAL()
        {
            SquatFailureResult result = Evaluate(BuildFailedReversal());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_REVERSAL));
            Assert.That(result.FailureRecord.OnsetTick, Is.EqualTo(4ul));
            Assert.That(result.FailureRecord.LatchedTick, Is.EqualTo(
                4ul + (ulong)SquatFailureCalibration.Default.ReversalTimeoutTicks - 1ul));
        }

        [Test]
        public void NO_DRIVE_ATTEMPT_DOES_NOT_PRODUCE_FAILED_REVERSAL()
        {
            List<SampleSpec> samples = BuildFailedReversalSamples();
            for (int index = 0; index < samples.Count; index++)
                samples[index].Drive01 = 0f;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.Not.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void SUCCESSFUL_STICKING_REGION_IS_NOT_STALL()
        {
            SquatFailureResult result = Evaluate(BuildRecoverableStickingAttempt());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void TERMINAL_HIGH_DEMAND_NO_PROGRESS_IS_MID_ASCENT_STALL()
        {
            SquatFailureResult result = Evaluate(BuildTerminalStall());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.MID_ASCENT_STALL));
        }

        [Test]
        public void SHALLOW_COMPLETED_LIFT_IS_RULE_FAILURE_NOT_PHYSICAL_FAILURE()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            for (int index = 3; index < samples.Count; index++)
            {
                samples[index].LeftDepthM = 0f;
                samples[index].RightDepthM = 0f;
            }
            for (int index = 3; index < 11; index++)
                samples[index].KneeAngleRad = 0.20f;
            samples.Add(Sample(14ul, 1.02f, 0f));

            SquatFailureResult result = Evaluate(Trace(samples));
            SquatAttemptJudgment judgment = new SquatRuleProcessor().Evaluate(
                Trace(samples),
                new SquatRuleCommandTimeline(3ul, 13ul, 14ul));

            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.EVALUABLE));
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord, Is.Null);
            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.NO_LIFT));
            Assert.That(judgment.PrimaryViolationKind, Is.EqualTo(SquatRuleViolationKind.INSUFFICIENT_DEPTH));
        }

        [Test]
        public void BAR_REVERSAL_PERSISTENT_DOWNWARD_MOVEMENT()
        {
            SquatFailureResult result = Evaluate(BuildBarReversal());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BAR_REVERSAL));
        }

        [Test]
        public void ONE_TICK_BAR_NOISE_IS_NOT_REVERSAL()
        {
            SquatFailureResult result = Evaluate(BuildBarNoiseThenRecovery());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void SADDLE_BREAK_IS_POSTURE_OR_BAR_LOSS()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].SaddleBroken = true;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.POSTURE_OR_BAR_LOSS));
            Assert.That(result.FailureRecord.PrimaryDetail, Is.EqualTo(SquatFailureDetailKind.SADDLE_BROKEN));
        }

        [Test]
        public void SADDLE_SEPARATION_BOUND()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].SaddleSeparationM = SquatFailureCalibration.Default.SaddleSeparationFailureM;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.FailureRecord.PrimaryDetail, Is.EqualTo(SquatFailureDetailKind.SADDLE_SEPARATION));
        }

        [Test]
        public void SADDLE_SEPARATION_BELOW_BOUND_DOES_NOT_FAIL()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].SaddleSeparationM = SquatFailureCalibration.Default.SaddleSeparationFailureM - 0.001f;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void TRUNK_WARNING_DOES_NOT_FAIL()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].TrunkPitchRad = SquatFailureCalibration.Default.TrunkWarningLimitRadians;
            samples[7].TrunkPitchRad = SquatFailureCalibration.Default.TrunkWarningLimitRadians;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void TRUNK_HARD_BOUND_FAILS()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].TrunkPitchRad = SquatFailureCalibration.Default.TrunkHardLimitRadians;
            samples[7].TrunkPitchRad = SquatFailureCalibration.Default.TrunkHardLimitRadians;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.POSTURE_OR_BAR_LOSS));
            Assert.That(result.FailureRecord.PrimaryDetail, Is.EqualTo(SquatFailureDetailKind.TRUNK_HARD_LIMIT));
        }

        [Test]
        public void CRITICAL_JOINT_LIMIT_IS_A_POSTURE_DETAIL()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].LimitProximity = SquatFailureCalibration.Default.CriticalJointLimitProximity;
            samples[7].LimitProximity = SquatFailureCalibration.Default.CriticalJointLimitProximity;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.FailureRecord.PrimaryDetail, Is.EqualTo(SquatFailureDetailKind.CRITICAL_JOINT_LIMIT));
        }

        [Test]
        public void CLEAN_PHYSICAL_LOCKOUT_HAS_NO_FAILED_LOCKOUT()
        {
            SquatFailureResult result = Evaluate(BuildGoodAttempt());

            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void TERMINAL_AFTER_TOP_REGION_WITHOUT_LOCKOUT_FAILED_LOCKOUT()
        {
            SquatFailureResult result = EvaluateTerminal(BuildTopRegionWithoutLockout(12));

            Assert.That(result.TerminalContextStatus, Is.EqualTo(SquatFailureTerminalContextStatus.TRACE_COVERED));
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
        }

        [Test]
        public void TOP_REGION_GT_60_TICKS_WITHOUT_TERMINALITY_NO_FAILED_LOCKOUT()
        {
            int beyondOldDwell = SquatFailureCalibration.Default.LockoutCompletionTimeoutTicks + 40;
            SquatFailureResult result = Evaluate(BuildTopRegionWithoutLockout(beyondOldDwell));

            Assert.That(result.TerminalContextStatus, Is.EqualTo(SquatFailureTerminalContextStatus.NOT_TERMINAL));
            Assert.That(result.Outcome, Is.Not.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord, Is.Null);
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void TOP_REGION_GT_60_TICKS_THEN_VALID_LOCKOUT_NO_FAILURE()
        {
            int beyondOldDwell = SquatFailureCalibration.Default.LockoutCompletionTimeoutTicks + 40;
            SquatTrace trace = BuildTopRegionThenLockout(beyondOldDwell);

            SquatFailureResult nonTerminal = Evaluate(trace);
            SquatFailureResult terminal = EvaluateTerminal(
                trace,
                SquatAttemptTerminalReason.PHYSICAL_LOCKOUT);

            Assert.That(nonTerminal.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
            Assert.That(terminal.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(terminal.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
            Assert.That(terminal.FailureRecord, Is.Null);
        }

        [Test]
        public void SLOW_CONTINUOUS_ASCENT_LONGER_THAN_OLD_TIMEOUT_THEN_LOCKOUT_IS_NOT_FAILED_LOCKOUT()
        {
            SquatTrace trace = BuildSlowContinuousAscentThenLockout();

            Assert.That(Evaluate(trace).PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
            Assert.That(
                EvaluateTerminal(trace, SquatAttemptTerminalReason.PHYSICAL_LOCKOUT).Outcome,
                Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void VALID_LOCKOUT_PLUS_TERMINAL_CONTEXT_NO_FAILURE()
        {
            SquatFailureResult result = EvaluateTerminal(
                BuildGoodAttempt(),
                SquatAttemptTerminalReason.PHYSICAL_LOCKOUT);

            Assert.That(result.TerminalContextStatus, Is.EqualTo(SquatFailureTerminalContextStatus.TRACE_COVERED));
            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.EVALUABLE));
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(result.FailureRecord, Is.Null);
        }

        [Test]
        public void LOCKOUT_REACHED_AT_ANY_TIME_BEFORE_TERMINALITY_FORBIDS_FAILED_LOCKOUT()
        {
            int dwell = SquatFailureCalibration.Default.LockoutCompletionTimeoutTicks;

            foreach (int topRegionSamples in new[] { 1, dwell - 1, dwell, dwell + 100 })
            {
                SquatFailureResult result = EvaluateTerminal(
                    BuildTopRegionThenLockout(topRegionSamples),
                    SquatAttemptTerminalReason.PHYSICAL_LOCKOUT);

                Assert.That(
                    result.PrimaryFailureKind,
                    Is.EqualTo(SquatFailureKind.NONE),
                    "topRegionSamples=" + topRegionSamples);
                Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            }
        }

        [Test]
        public void FAILED_LOCKOUT_ONSET_EQUALS_FIRST_COMPLETION_REGION_ENTRY()
        {
            // Ascent is established at tick 8; the bar first satisfies the
            // completion-region height at tick 9.
            SquatFailureResult result = EvaluateTerminal(BuildTopRegionWithoutLockout(30));

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            Assert.That(result.FailureRecord.OnsetTick, Is.EqualTo(9ul));
            Assert.That(result.FailureRecord.OnsetTick, Is.Not.EqualTo(8ul));
        }

        [Test]
        public void FAILED_LOCKOUT_LATCH_EQUALS_TERMINAL_TICK()
        {
            SquatTrace trace = BuildTopRegionWithoutLockout(30);
            ulong terminalTick = trace[trace.Count - 1].SimulationTick;

            SquatFailureResult result = EvaluateTerminal(trace);

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            Assert.That(result.FailureRecord.LatchedTick, Is.EqualTo(terminalTick));
            Assert.That(result.FailureRecord.LatchedTick, Is.GreaterThan(result.FailureRecord.OnsetTick));
        }

        [Test]
        public void FAILED_LOCKOUT_LATCH_FOLLOWS_AN_EARLIER_TERMINAL_TICK()
        {
            SquatTrace trace = BuildTopRegionWithoutLockout(30);
            SquatObservationSnapshot earlierTerminal = trace[trace.Count - 6];

            SquatFailureResult result = new SquatFailureDetector().Evaluate(
                trace,
                SquatFailureCompletionContext.Terminal(
                    earlierTerminal.SimulationTick,
                    earlierTerminal.SimulationTimeSeconds,
                    SquatAttemptTerminalReason.TIMEOUT));

            Assert.That(result.FailureRecord.LatchedTick, Is.EqualTo(earlierTerminal.SimulationTick));
        }

        [Test]
        public void COMPLETION_REGION_ENTRY_AFTER_THE_TERMINAL_TICK_IS_NOT_FAILED_LOCKOUT()
        {
            // Ascent establishes at tick 8 and the completion region is first
            // entered at tick 9. A terminal sample before that entry cannot
            // support the postcondition, and must never emit a record whose
            // onset follows its latch.
            SquatTrace trace = BuildTopRegionWithoutLockout(30);
            SquatObservationSnapshot beforeEntry = trace[8];

            SquatFailureResult result = new SquatFailureDetector().Evaluate(
                trace,
                SquatFailureCompletionContext.Terminal(
                    beforeEntry.SimulationTick,
                    beforeEntry.SimulationTimeSeconds,
                    SquatAttemptTerminalReason.TIMEOUT));

            Assert.That(result.TerminalContextStatus, Is.EqualTo(SquatFailureTerminalContextStatus.TRACE_COVERED));
            Assert.That(result.FailureRecord, Is.Null);
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
        }

        [Test]
        public void FAILED_LOCKOUT_ONSET_NEVER_FOLLOWS_ITS_LATCH()
        {
            SquatTrace trace = BuildTopRegionWithoutLockout(30);

            for (int index = 0; index < trace.Count; index++)
            {
                SquatObservationSnapshot terminal = trace[index];
                SquatFailureResult result = new SquatFailureDetector().Evaluate(
                    trace,
                    SquatFailureCompletionContext.Terminal(
                        terminal.SimulationTick,
                        terminal.SimulationTimeSeconds,
                        SquatAttemptTerminalReason.TIMEOUT));

                if (result.FailureRecord == null)
                    continue;
                Assert.That(
                    result.FailureRecord.OnsetTick,
                    Is.LessThanOrEqualTo(result.FailureRecord.LatchedTick),
                    "terminalTick=" + terminal.SimulationTick);
            }
        }

        [Test]
        public void TERMINAL_BELOW_COMPLETION_REGION_NOT_FAILED_LOCKOUT()
        {
            SquatFailureResult result = EvaluateTerminal(BuildAscentBelowCompletionRegion());

            Assert.That(result.TerminalContextStatus, Is.EqualTo(SquatFailureTerminalContextStatus.TRACE_COVERED));
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
            Assert.That(result.FailureRecord, Is.Null);
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.UNDETERMINED));
            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INCOMPLETE_ATTEMPT));
        }

        [Test]
        public void MID_ASCENT_STALL_REMAINS_STALL()
        {
            SquatFailureResult result = EvaluateTerminal(BuildTerminalStall());

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.MID_ASCENT_STALL));
            Assert.That(ContainsKind(result, SquatFailureKind.FAILED_LOCKOUT), Is.False);
        }

        [Test]
        public void BAR_REVERSAL_REMAINS_BAR_REVERSAL()
        {
            SquatFailureResult result = EvaluateTerminal(BuildBarReversal());

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BAR_REVERSAL));
            Assert.That(ContainsKind(result, SquatFailureKind.FAILED_LOCKOUT), Is.False);
        }

        [Test]
        public void EARLIER_IRREVERSIBLE_FAILURE_REMAINS_PRIMARY()
        {
            SquatFailureResult result = EvaluateTerminal(BuildEarlyBalanceThenFailedLockout());

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.Not.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            Assert.That(ContainsKind(result, SquatFailureKind.FAILED_LOCKOUT), Is.True);
        }

        [Test]
        public void OLD_60_TICK_PARAMETER_MUTATION_DOES_NOT_CHANGE_P3A1_RESULT()
        {
            SquatTrace trace = BuildTopRegionWithoutLockout(30);
            SquatFailureCompletionContext completion = TerminalAtLastSample(trace);

            SquatFailureResult baseline = new SquatFailureDetector().Evaluate(trace, completion);
            SquatFailureResult shortDwell = new SquatFailureDetector(
                CalibrationWithLockoutDwell(1)).Evaluate(trace, completion);
            SquatFailureResult longDwell = new SquatFailureDetector(
                CalibrationWithLockoutDwell(5000)).Evaluate(trace, completion);

            Assert.That(baseline.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            foreach (SquatFailureResult mutated in new[] { shortDwell, longDwell })
            {
                Assert.That(mutated.Outcome, Is.EqualTo(baseline.Outcome));
                Assert.That(mutated.PrimaryFailureKind, Is.EqualTo(baseline.PrimaryFailureKind));
                Assert.That(mutated.FailureRecord.OnsetTick, Is.EqualTo(baseline.FailureRecord.OnsetTick));
                Assert.That(mutated.FailureRecord.LatchedTick, Is.EqualTo(baseline.FailureRecord.LatchedTick));
            }
        }

        [Test]
        public void OLD_60_TICK_PARAMETER_IS_NOT_A_CANONICAL_SELECTOR_THRESHOLD()
        {
            SquatFailureResult result = EvaluateTerminal(BuildTopRegionWithoutLockout(30));
            SquatFailureEvent primary = result.FailureRecord.Primary;

            for (int index = 0; index < primary.Thresholds.Count; index++)
                Assert.That(primary.Thresholds[index].Name, Is.Not.EqualTo("lockout_completion_dwell"));
        }

        [Test]
        public void LOAD_METADATA_MUTATION_DOES_NOT_CHANGE_RESULT()
        {
            List<SampleSpec> original = SamplesForTopRegionWithoutLockout(30);
            List<SampleSpec> mutated = SamplesForTopRegionWithoutLockout(30);
            for (int index = 0; index < mutated.Count; index++)
                mutated[index].LoadKg = 500f;

            SquatFailureResult originalResult = EvaluateTerminal(Trace(original));
            SquatFailureResult mutatedResult = EvaluateTerminal(Trace(mutated));

            Assert.That(originalResult.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            Assert.That(mutatedResult.PrimaryFailureKind, Is.EqualTo(originalResult.PrimaryFailureKind));
            Assert.That(mutatedResult.FailureRecord.OnsetTick, Is.EqualTo(originalResult.FailureRecord.OnsetTick));
            Assert.That(mutatedResult.FailureRecord.LatchedTick, Is.EqualTo(originalResult.FailureRecord.LatchedTick));
        }

        [Test]
        public void SQ_STATE_REFERENCE_PHASE_MUTATION_DOES_NOT_CHANGE_RESULT()
        {
            List<SampleSpec> original = SamplesForTopRegionWithoutLockout(30);
            List<SampleSpec> mutated = SamplesForTopRegionWithoutLockout(30);
            for (int index = 0; index < mutated.Count; index++)
            {
                mutated[index].State = SquatState.COMPLETE;
                mutated[index].Direction = SquatPhaseDirection.None;
                mutated[index].Sq = 0f;
            }

            SquatFailureResult originalResult = EvaluateTerminal(Trace(original));
            SquatFailureResult mutatedResult = EvaluateTerminal(Trace(mutated));

            Assert.That(originalResult.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT));
            Assert.That(mutatedResult.PrimaryFailureKind, Is.EqualTo(originalResult.PrimaryFailureKind));
            Assert.That(mutatedResult.FailureRecord.OnsetTick, Is.EqualTo(originalResult.FailureRecord.OnsetTick));
            Assert.That(mutatedResult.FailureRecord.LatchedTick, Is.EqualTo(originalResult.FailureRecord.LatchedTick));
        }

        [Test]
        public void MALFORMED_TERMINAL_CONTEXT_DOES_NOT_CREATE_FAILURE()
        {
            SquatTrace trace = BuildTopRegionWithoutLockout(30);
            ulong firstTick = trace[0].SimulationTick;
            ulong lastTick = trace[trace.Count - 1].SimulationTick;
            SquatObservationSnapshot last = trace[trace.Count - 1];

            SquatFailureCompletionContext noReason = SquatFailureCompletionContext.Terminal(
                lastTick,
                last.SimulationTimeSeconds,
                SquatAttemptTerminalReason.NONE);
            SquatFailureCompletionContext afterTrace = SquatFailureCompletionContext.Terminal(
                lastTick + 5ul,
                last.SimulationTimeSeconds + 0.05d,
                SquatAttemptTerminalReason.TIMEOUT);
            SquatFailureCompletionContext beforeTrace = SquatFailureCompletionContext.Terminal(
                firstTick == 0ul ? SquatAttemptEventTicks.NotAvailable : firstTick - 1ul,
                0d,
                SquatAttemptTerminalReason.TIMEOUT);
            SquatFailureCompletionContext foreignTime = SquatFailureCompletionContext.Terminal(
                lastTick,
                last.SimulationTimeSeconds + 7d,
                SquatAttemptTerminalReason.TIMEOUT);

            AssertNoTerminalFailure(trace, noReason, SquatFailureTerminalContextStatus.REJECTED_MALFORMED);
            AssertNoTerminalFailure(trace, afterTrace, SquatFailureTerminalContextStatus.REJECTED_UNCOVERED_TICK);
            AssertNoTerminalFailure(trace, beforeTrace, SquatFailureTerminalContextStatus.REJECTED_MALFORMED);
            AssertNoTerminalFailure(trace, foreignTime, SquatFailureTerminalContextStatus.REJECTED_TIME_MISMATCH);
        }

        [Test]
        public void TERMINAL_REASON_DOES_NOT_SELECT_A_FAILURE_CLASS()
        {
            SquatTrace locksOut = BuildTopRegionThenLockout(30);
            SquatTrace neverLocksOut = BuildTopRegionWithoutLockout(30);

            foreach (SquatAttemptTerminalReason reason in new[]
            {
                SquatAttemptTerminalReason.PHYSICAL_LOCKOUT,
                SquatAttemptTerminalReason.PHYSICAL_FAILURE,
                SquatAttemptTerminalReason.TIMEOUT,
                SquatAttemptTerminalReason.ABORTED,
                SquatAttemptTerminalReason.LIFECYCLE_FAULT
            })
            {
                Assert.That(
                    EvaluateTerminal(locksOut, reason).PrimaryFailureKind,
                    Is.EqualTo(SquatFailureKind.NONE),
                    "reason=" + reason);
                Assert.That(
                    EvaluateTerminal(neverLocksOut, reason).PrimaryFailureKind,
                    Is.EqualTo(SquatFailureKind.FAILED_LOCKOUT),
                    "reason=" + reason);
            }
        }

        [Test]
        public void RESULT_IS_DETERMINISTIC_ON_REPEAT()
        {
            SquatTrace trace = BuildTopRegionWithoutLockout(30);
            SquatFailureCompletionContext completion = TerminalAtLastSample(trace);

            SquatFailureResult first = new SquatFailureDetector().Evaluate(trace, completion);
            SquatFailureResult second = new SquatFailureDetector().Evaluate(trace, completion);
            SquatFailureDetector reused = new SquatFailureDetector();
            SquatFailureResult third = reused.Evaluate(trace, completion);
            SquatFailureResult fourth = reused.Evaluate(trace, completion);

            foreach (SquatFailureResult repeat in new[] { second, third, fourth })
            {
                Assert.That(repeat.EvidenceStatus, Is.EqualTo(first.EvidenceStatus));
                Assert.That(repeat.Outcome, Is.EqualTo(first.Outcome));
                Assert.That(repeat.TerminalContextStatus, Is.EqualTo(first.TerminalContextStatus));
                Assert.That(repeat.PrimaryFailureKind, Is.EqualTo(first.PrimaryFailureKind));
                Assert.That(repeat.FailureRecord.OnsetTick, Is.EqualTo(first.FailureRecord.OnsetTick));
                Assert.That(repeat.FailureRecord.LatchedTick, Is.EqualTo(first.FailureRecord.LatchedTick));
                Assert.That(repeat.FailureRecord.SecondaryCount, Is.EqualTo(first.FailureRecord.SecondaryCount));
            }
        }

        [Test]
        public void COMPOUND_FAILURE_PRESERVES_SECONDARY_CAUSES()
        {
            SquatFailureResult result = Evaluate(BuildBalanceThenSaddleBreak());

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(ContainsKind(result, SquatFailureKind.POSTURE_OR_BAR_LOSS), Is.True);
        }

        [Test]
        public void SIMULTANEOUS_FAILURE_TIE_BREAK_IS_EXPLICIT_AND_DETERMINISTIC()
        {
            SquatFailureResult result = Evaluate(BuildSimultaneousBalanceAndSaddleBreak());

            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(result.FailureRecord.PrecedenceVersion, Is.EqualTo(
                SquatFailureCalibration.DefaultPrecedenceVersion));
            Assert.That(ContainsKind(result, SquatFailureKind.POSTURE_OR_BAR_LOSS), Is.True);
        }

        [Test]
        public void FAILURE_DOES_NOT_UNLATCH_AFTER_RECOVERY()
        {
            SquatFailureResult result = Evaluate(BuildBalanceLossThenRecovery());

            Assert.That(result.HasFailure, Is.True);
            Assert.That(result.FailureRecord.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(result.FailureRecord.LatchedTick, Is.LessThan(
                result.FailureRecord.PreFailureEvidence.SampleAt(result.FailureRecord.PreFailureEvidence.Count - 1).Tick + 1ul));
        }

        [Test]
        public void LOAD_METADATA_MUTATION_DOES_NOT_CHANGE_CLASSIFICATION()
        {
            List<SampleSpec> light = BalanceLossSamples(true);
            List<SampleSpec> heavy = BalanceLossSamples(true);
            for (int index = 0; index < heavy.Count; index++)
                heavy[index].LoadKg = 500f;

            SquatFailureResult lightResult = Evaluate(Trace(light));
            SquatFailureResult heavyResult = Evaluate(Trace(heavy));

            Assert.That(heavyResult.PrimaryFailureKind, Is.EqualTo(lightResult.PrimaryFailureKind));
            Assert.That(heavyResult.FailureRecord.PrimaryDirection, Is.EqualTo(lightResult.FailureRecord.PrimaryDirection));
            Assert.That(heavyResult.FailureRecord.OnsetTick, Is.EqualTo(lightResult.FailureRecord.OnsetTick));
            Assert.That(heavyResult.FailureRecord.LatchedTick, Is.EqualTo(lightResult.FailureRecord.LatchedTick));
        }

        [Test]
        public void SQ_STATE_PHASE_MUTATION_DOES_NOT_CHANGE_PHYSICAL_CLASSIFICATION()
        {
            List<SampleSpec> original = BalanceLossSamples(true);
            List<SampleSpec> mutated = BalanceLossSamples(true);
            for (int index = 0; index < mutated.Count; index++)
            {
                mutated[index].State = SquatState.COMPLETE;
                mutated[index].Direction = SquatPhaseDirection.None;
                mutated[index].Sq = 0f;
            }

            SquatFailureResult originalResult = Evaluate(Trace(original));
            SquatFailureResult mutatedResult = Evaluate(Trace(mutated));

            Assert.That(mutatedResult.PrimaryFailureKind, Is.EqualTo(originalResult.PrimaryFailureKind));
            Assert.That(mutatedResult.FailureRecord.PrimaryDirection, Is.EqualTo(originalResult.FailureRecord.PrimaryDirection));
            Assert.That(mutatedResult.FailureRecord.OnsetTick, Is.EqualTo(originalResult.FailureRecord.OnsetTick));
            Assert.That(mutatedResult.FailureRecord.LatchedTick, Is.EqualTo(originalResult.FailureRecord.LatchedTick));
        }

        [Test]
        public void MISSING_REQUIRED_EVIDENCE_IS_NOT_NO_FAILURE()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            for (int index = 0; index < samples.Count; index++)
                samples[index].BarAvailable = false;

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INSUFFICIENT_EVIDENCE));
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.UNDETERMINED));
            Assert.That(result.HasFailure, Is.False);
        }

        [Test]
        public void ZERO_KG_MISSING_BAR_REMAINS_INSUFFICIENT_EVIDENCE()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            for (int index = 0; index < samples.Count; index++)
            {
                samples[index].BarAvailable = false;
                samples[index].LoadKg = 0f;
            }

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INSUFFICIENT_EVIDENCE));
            Assert.That(result.Outcome, Is.EqualTo(SquatFailureResultKind.UNDETERMINED));
        }

        [Test]
        public void SAME_TRACE_EVALUATED_TWICE_PRODUCES_IDENTICAL_FAILURE_RESULT()
        {
            SquatFailureResult first = Evaluate(BuildBalanceLoss(true));
            SquatFailureResult second = Evaluate(BuildBalanceLoss(true));

            Assert.That(second.EvidenceStatus, Is.EqualTo(first.EvidenceStatus));
            Assert.That(second.Outcome, Is.EqualTo(first.Outcome));
            Assert.That(second.PrimaryFailureKind, Is.EqualTo(first.PrimaryFailureKind));
            Assert.That(second.FailureRecord.PrimaryDirection, Is.EqualTo(first.FailureRecord.PrimaryDirection));
            Assert.That(second.FailureRecord.OnsetTick, Is.EqualTo(first.FailureRecord.OnsetTick));
            Assert.That(second.FailureRecord.LatchedTick, Is.EqualTo(first.FailureRecord.LatchedTick));
            Assert.That(second.FailureRecord.PreFailureEvidence.Count, Is.EqualTo(first.FailureRecord.PreFailureEvidence.Count));
            for (int index = 0; index < first.FailureRecord.PreFailureEvidence.Count; index++)
                Assert.That(second.FailureRecord.PreFailureEvidence.SampleAt(index).Tick, Is.EqualTo(
                    first.FailureRecord.PreFailureEvidence.SampleAt(index).Tick));
        }

        [Test]
        public void TRACE_IS_NOT_MUTATED_BY_FAILURE_EVALUATION()
        {
            SquatTrace trace = BuildBalanceLoss(true);
            int count = trace.Count;
            ulong firstTick = trace[0].SimulationTick;
            ulong lastTick = trace[trace.Count - 1].SimulationTick;

            Evaluate(trace);

            Assert.That(trace.Count, Is.EqualTo(count));
            Assert.That(trace.IsFrozen, Is.True);
            Assert.That(trace[0].SimulationTick, Is.EqualTo(firstTick));
            Assert.That(trace[trace.Count - 1].SimulationTick, Is.EqualTo(lastTick));
        }

        [Test]
        public void PRE_FAILURE_WINDOW_IS_BOUNDED_AND_ORDERED()
        {
            SquatFailureResult result = Evaluate(BuildLongBalanceLoss());
            SquatFailureEvidenceWindow window = result.FailureRecord.PreFailureEvidence;

            Assert.That(window.Count, Is.EqualTo(window.Capacity));
            for (int index = 1; index < window.Count; index++)
                Assert.That(window.SampleAt(index).Tick, Is.EqualTo(window.SampleAt(index - 1).Tick + 1ul));
            Assert.That(window.SampleAt(window.Count - 1).Tick, Is.EqualTo(result.FailureRecord.LatchedTick));
        }

        [Test]
        public void SAFETY_HANDOFF_HAS_NO_ACTUATION_BEHAVIOR()
        {
            SquatFailureResult result = Evaluate(BuildBalanceLoss(true));
            SquatFailureSafetyHandoff handoff = result.SafetyHandoff;

            Assert.That(handoff, Is.Not.Null);
            Assert.That(handoff.IsEligible, Is.True);
            Assert.That(handoff.FailureRecord, Is.SameAs(result.FailureRecord));
            Assert.That(handoff.EligibleLatchTick, Is.EqualTo(result.FailureRecord.LatchedTick));
            Assert.That(typeof(SquatFailureSafetyHandoff).GetMethod("AddForce"), Is.Null);
            Assert.That(typeof(SquatFailureSafetyHandoff).GetMethod("AddTorque"), Is.Null);
        }

        [Test]
        public void PHYSICAL_FAILURE_ENUM_DOES_NOT_REUSE_RULE_DOWNWARD_MOVEMENT()
        {
            Assert.That(Enum.IsDefined(typeof(SquatFailureKind), "DOWNWARD_MOVEMENT"), Is.False);
            Assert.That(typeof(SquatFailureKind), Is.Not.EqualTo(typeof(SquatRuleViolationKind)));
        }

        [Test]
        public void ACTIVE_TRACE_IS_INVALID_AND_EMPTY_FROZEN_TRACE_IS_INVALID()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            SquatTrace active = new SquatTrace(samples.Count);
            active.BeginRecording();
            active.Append(Snapshot(samples[0]));

            SquatTrace empty = new SquatTrace(1);
            empty.BeginRecording();
            empty.EndRecording();

            SquatFailureDetector detector = new SquatFailureDetector();
            Assert.That(detector.Evaluate(active).EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INVALID_TRACE));
            Assert.That(detector.Evaluate(empty).EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INVALID_TRACE));
        }

        [Test]
        public void BALANCE_MARGIN_BOUNDARY_IS_INCLUSIVE_AND_RECOVERY_BOUNDARY_IS_NOT_STATIC_FAILURE()
        {
            SquatFailureCalibration calibration = SquatFailureCalibration.Default;
            List<SampleSpec> exact = BalanceLossSamples(true);
            List<SampleSpec> justInside = GoodAttemptSamples();
            float exactCom = 0.30f - calibration.BalanceSupportMarginFailureM + 0.00001f;
            for (int index = 3; index < exact.Count; index++)
            {
                exact[index].ComZ = exactCom;
                exact[index].ComVelocityZ = calibration.BalanceOutwardComVelocityMps;
            }
            for (int index = 6; index < 8; index++)
            {
                justInside[index].ComZ = exactCom - 0.001f;
                justInside[index].ComVelocityZ = calibration.BalanceOutwardComVelocityMps;
            }

            SquatFailureResult exactResult = Evaluate(Trace(exact));
            SquatFailureResult justInsideResult = Evaluate(Trace(justInside));

            Assert.That(exactResult.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.BALANCE_LOSS));
            Assert.That(justInsideResult.Outcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
        }

        [Test]
        public void NONSEQUENTIAL_TRACE_IS_INVALID()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples[6].Tick = 8ul;
            for (int index = 7; index < samples.Count; index++)
                samples[index].Tick = (ulong)(index + 2);

            SquatFailureResult result = Evaluate(Trace(samples));

            Assert.That(result.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.INVALID_TRACE));
        }

        [Test]
        public void CALIBRATION_BOUNDARIES_HAVE_VERSIONED_PROVENANCE()
        {
            SquatFailureCalibration calibration = SquatFailureCalibration.Default;

            Assert.That(calibration.Version, Is.EqualTo("GAM12_P3A1_FAILURE_CALIBRATION_PROVISIONAL_V1"));
            Assert.That(calibration.PrecedenceVersion, Is.EqualTo(
                SquatFailureCalibration.DefaultPrecedenceVersion));
            Assert.That(calibration.Descriptors.Count, Is.GreaterThan(30));
            for (int index = 0; index < calibration.Descriptors.Count; index++)
            {
                SquatFailureCalibrationDescriptor descriptor = calibration.Descriptors[index];
                Assert.That(descriptor.Name, Is.Not.Empty);
                Assert.That(descriptor.Unit, Is.Not.Empty);
                Assert.That(descriptor.Category, Is.Not.Empty);
                Assert.That(descriptor.Source, Is.Not.Empty);
                Assert.That(descriptor.Rationale, Is.Not.Empty);
                Assert.That(descriptor.Version, Is.EqualTo(calibration.Version));
            }
        }

        private static SquatFailureResult Evaluate(SquatTrace trace) =>
            new SquatFailureDetector().Evaluate(trace);

        private static SquatFailureResult EvaluateTerminal(
            SquatTrace trace,
            SquatAttemptTerminalReason reason = SquatAttemptTerminalReason.TIMEOUT) =>
            new SquatFailureDetector().Evaluate(trace, TerminalAtLastSample(trace, reason));

        private static SquatFailureCompletionContext TerminalAtLastSample(
            SquatTrace trace,
            SquatAttemptTerminalReason reason = SquatAttemptTerminalReason.TIMEOUT)
        {
            SquatObservationSnapshot last = trace[trace.Count - 1];
            return SquatFailureCompletionContext.Terminal(
                last.SimulationTick,
                last.SimulationTimeSeconds,
                reason);
        }

        private static void AssertNoTerminalFailure(
            SquatTrace trace,
            SquatFailureCompletionContext completion,
            SquatFailureTerminalContextStatus expectedStatus)
        {
            SquatFailureResult result = new SquatFailureDetector().Evaluate(trace, completion);

            Assert.That(result.TerminalContextStatus, Is.EqualTo(expectedStatus));
            Assert.That(result.FailureRecord, Is.Null);
            Assert.That(result.PrimaryFailureKind, Is.EqualTo(SquatFailureKind.NONE));
            Assert.That(result.Outcome, Is.Not.EqualTo(SquatFailureResultKind.PHYSICAL_FAILURE));
        }

        private static SquatFailureCalibration CalibrationWithLockoutDwell(int dwellTicks) =>
            new SquatFailureCalibration(
                version: SquatFailureCalibration.DefaultVersion,
                lockoutCompletionTimeoutTicks: dwellTicks);

        private static SquatTrace BuildGoodAttempt()
        {
            return Trace(GoodAttemptSamples());
        }

        private static List<SampleSpec> GoodAttemptSamples()
        {
            return new List<SampleSpec>
            {
                Sample(0ul, 1.00f, 0f),
                Sample(1ul, 1.00f, 0f),
                Sample(2ul, 1.00f, 0f),
                Sample(3ul, 0.90f, -0.20f),
                Sample(4ul, 0.80f, -0.20f),
                Sample(5ul, 0.79f, 0f),
                Sample(6ul, 0.82f, 0.05f),
                Sample(7ul, 0.86f, 0.05f),
                Sample(8ul, 0.90f, 0.05f),
                Sample(9ul, 0.98f, 0.05f),
                Sample(10ul, 1.02f, 0.05f),
                Sample(11ul, 1.02f, 0f),
                Sample(12ul, 1.02f, 0f),
                Sample(13ul, 1.02f, 0f)
            };
        }

        private static SquatTrace BuildBalanceLoss(bool forward)
        {
            List<SampleSpec> samples = new List<SampleSpec>
            {
                Sample(0ul, 1.00f, 0f),
                Sample(1ul, 0.90f, -0.20f),
                Sample(2ul, 0.80f, -0.20f)
            };

            SquatFailureCalibration calibration = SquatFailureCalibration.Default;
            float comPosition = forward
                ? 0.30f - calibration.BalanceSupportMarginFailureM + 0.01f
                : -0.30f + calibration.BalanceSupportMarginFailureM - 0.01f;
            float comVelocity = forward
                ? calibration.BalanceOutwardComVelocityMps + 0.01f
                : -calibration.BalanceOutwardComVelocityMps - 0.01f;
            int firstFailureTick = 3;
            int lastFailureTick = firstFailureTick + calibration.BalancePersistenceTicks - 1;
            for (ulong tick = (ulong)firstFailureTick; tick <= (ulong)lastFailureTick; tick++)
            {
                SampleSpec sample = Sample(tick, 0.80f, 0f);
                sample.ComZ = comPosition;
                sample.ComVelocityZ = comVelocity;
                samples.Add(sample);
            }

            return Trace(samples);
        }

        private static SquatTrace BuildFailedReversal()
        {
            return Trace(BuildFailedReversalSamples());
        }

        private static List<SampleSpec> BuildFailedReversalSamples()
        {
            List<SampleSpec> samples = new List<SampleSpec>
            {
                Sample(0ul, 1.00f, 0f),
                Sample(1ul, 1.00f, 0f),
                Sample(2ul, 0.90f, -0.20f),
                Sample(3ul, 0.80f, -0.20f),
                Sample(4ul, 0.79f, 0f)
            };
            ulong lastTick = 4ul + (ulong)SquatFailureCalibration.Default.ReversalTimeoutTicks - 1ul;
            for (ulong tick = 5ul; tick <= lastTick; tick++)
                samples.Add(Sample(tick, 0.79f, 0f));
            return samples;
        }

        private static SquatTrace BuildRecoverableStickingAttempt()
        {
            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            for (ulong tick = 9ul; tick <= 28ul; tick++)
            {
                SampleSpec sample = Sample(tick, 0.90f + (tick - 8ul) * 0.0001f, 0.005f);
                sample.ModeledDemand = 0.90f;
                sample.DriveSaturated = true;
                samples.Add(sample);
            }
            samples.Add(Sample(29ul, 0.94f, 0.05f));
            samples.Add(Sample(30ul, 0.99f, 0.05f));
            samples.Add(Sample(31ul, 1.03f, 0.05f));
            samples.Add(Sample(32ul, 1.03f, 0f));
            samples.Add(Sample(33ul, 1.03f, 0f));
            return Trace(samples);
        }

        private static SquatTrace BuildTerminalStall()
        {
            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            ulong lastTick = 8ul + (ulong)SquatFailureCalibration.Default.StallDwellTicks;
            for (ulong tick = 9ul; tick <= lastTick; tick++)
            {
                SampleSpec sample = Sample(tick, 0.90f, 0f);
                sample.ModeledDemand = 0.90f;
                sample.DriveSaturated = true;
                samples.Add(sample);
            }
            return Trace(samples);
        }

        private static SquatTrace BuildBarReversal()
        {
            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            samples.Add(Sample(9ul, 0.89f, -0.10f));
            samples.Add(Sample(10ul, 0.87f, -0.10f));
            return Trace(samples);
        }

        private static SquatTrace BuildBarNoiseThenRecovery()
        {
            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            samples.Add(Sample(9ul, 0.898f, -0.01f));
            samples.Add(Sample(10ul, 0.90f, 0f));
            samples.Add(Sample(11ul, 0.95f, 0.05f));
            samples.Add(Sample(12ul, 1.02f, 0.05f));
            samples.Add(Sample(13ul, 1.02f, 0f));
            samples.Add(Sample(14ul, 1.02f, 0f));
            return Trace(samples);
        }

        private static SquatTrace BuildSlowContinuousAscentThenLockout()
        {
            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            for (ulong tick = 9ul; tick <= 70ul; tick++)
            {
                SampleSpec sample = Sample(tick, 0.90f + (tick - 8ul) * 0.0004f, 0.004f);
                sample.ModeledDemand = 0f;
                sample.DriveSaturated = false;
                samples.Add(sample);
            }

            SampleSpec topRegion = Sample(71ul, 0.95f, 0f);
            topRegion.KneeAngleRad = 0.20f;
            samples.Add(topRegion);
            samples.Add(Sample(72ul, 0.96f, 0f));
            return Trace(samples);
        }

        private static SquatTrace BuildTopRegionThenLockout(int topRegionSamples)
        {
            List<SampleSpec> samples = SamplesForTopRegionWithoutLockout(topRegionSamples);
            ulong lockoutTick = 9ul + (ulong)topRegionSamples;
            samples.Add(Sample(lockoutTick, 0.96f, 0f));
            return Trace(samples);
        }

        private static SquatTrace BuildTopRegionWithoutLockout(int topRegionSamples)
        {
            return Trace(SamplesForTopRegionWithoutLockout(topRegionSamples));
        }

        private static List<SampleSpec> SamplesForTopRegionWithoutLockout(int topRegionSamples)
        {
            if (topRegionSamples <= 0)
                throw new ArgumentOutOfRangeException(nameof(topRegionSamples));

            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            for (ulong tick = 9ul; tick < 9ul + (ulong)topRegionSamples; tick++)
            {
                SampleSpec sample = Sample(tick, 0.96f, 0f);
                sample.KneeAngleRad = 0.20f;
                samples.Add(sample);
            }
            return samples;
        }

        /// <summary>
        /// Ascent is established and progress continues, but the bar never
        /// reaches the physical completion region. Terminality below the region
        /// is not evidence of a failed lockout.
        /// </summary>
        private static SquatTrace BuildAscentBelowCompletionRegion()
        {
            List<SampleSpec> samples = BaseThroughAscentEstablishment();
            for (ulong tick = 9ul; tick <= 30ul; tick++)
                samples.Add(Sample(tick, 0.90f + (tick - 8ul) * 0.002f, 0.02f));
            return Trace(samples);
        }

        private static SquatTrace BuildEarlyBalanceThenFailedLockout()
        {
            List<SampleSpec> samples = new List<SampleSpec>
            {
                Sample(0ul, 1.00f, 0f),
                Sample(1ul, 1.00f, 0f),
                Sample(2ul, 0.90f, -0.20f)
            };
            SquatFailureCalibration calibration = SquatFailureCalibration.Default;
            float outsideCom = 0.30f - calibration.BalanceSupportMarginFailureM + 0.01f;
            float outwardVelocity = calibration.BalanceOutwardComVelocityMps + 0.01f;
            int lastBalanceTick = 3 + calibration.BalancePersistenceTicks - 1;
            for (ulong tick = 3ul; tick <= (ulong)lastBalanceTick; tick++)
            {
                SampleSpec sample = Sample(tick, 0.80f, 0f);
                sample.ComZ = outsideCom;
                sample.ComVelocityZ = outwardVelocity;
                sample.Drive01 = 0f;
                samples.Add(sample);
            }
            SampleSpec reversal = Sample((ulong)lastBalanceTick + 1ul, 0.80f, 0f);
            samples.Add(reversal);
            SampleSpec ascentOne = Sample((ulong)lastBalanceTick + 2ul, 0.84f, 0.05f);
            samples.Add(ascentOne);
            SampleSpec ascentTwo = Sample((ulong)lastBalanceTick + 3ul, 0.88f, 0.05f);
            samples.Add(ascentTwo);
            SampleSpec ascentThree = Sample((ulong)lastBalanceTick + 4ul, 0.92f, 0.05f);
            samples.Add(ascentThree);
            ulong ascentTick = (ulong)lastBalanceTick + 4ul;
            ulong lastTick = ascentTick + (ulong)calibration.LockoutCompletionTimeoutTicks;
            for (ulong tick = ascentTick + 1ul; tick <= lastTick; tick++)
            {
                SampleSpec sample = Sample(tick, 1.02f, 0f);
                sample.KneeAngleRad = 0.20f;
                samples.Add(sample);
            }
            return Trace(samples);
        }

        private static SquatTrace BuildBalanceThenSaddleBreak()
        {
            List<SampleSpec> samples = BalanceLossSamples(true);
            ulong nextTick = samples[samples.Count - 1].Tick + 1ul;
            SampleSpec saddle = Sample(nextTick, 0.80f, 0f);
            saddle.SaddleBroken = true;
            saddle.ComZ = 0f;
            saddle.ComVelocityZ = 0f;
            samples.Add(saddle);
            return Trace(samples);
        }

        private static SquatTrace BuildSimultaneousBalanceAndSaddleBreak()
        {
            List<SampleSpec> samples = BalanceLossSamples(true);
            samples[samples.Count - 1].SaddleBroken = true;
            return Trace(samples);
        }

        private static SquatTrace BuildBalanceLossThenRecovery()
        {
            List<SampleSpec> samples = BalanceLossSamples(true);
            ulong nextTick = samples[samples.Count - 1].Tick + 1ul;
            samples.Add(Sample(nextTick++, 0.84f, 0.05f));
            samples.Add(Sample(nextTick++, 0.90f, 0.05f));
            samples.Add(Sample(nextTick++, 0.98f, 0.05f));
            samples.Add(Sample(nextTick++, 1.02f, 0f));
            samples.Add(Sample(nextTick, 1.02f, 0f));
            return Trace(samples);
        }

        private static SquatTrace BuildLongBalanceLoss()
        {
            List<SampleSpec> samples = new List<SampleSpec>();
            for (ulong tick = 0ul; tick < 40ul; tick++)
                samples.Add(Sample(tick, 1.00f, 0f));
            samples.Add(Sample(40ul, 0.90f, -0.20f));
            samples.Add(Sample(41ul, 0.80f, -0.20f));

            SquatFailureCalibration calibration = SquatFailureCalibration.Default;
            float comPosition = 0.30f - calibration.BalanceSupportMarginFailureM + 0.01f;
            float comVelocity = calibration.BalanceOutwardComVelocityMps + 0.01f;
            ulong lastFailureTick = 42ul + (ulong)calibration.BalancePersistenceTicks - 1ul;
            for (ulong tick = 42ul; tick <= lastFailureTick; tick++)
            {
                SampleSpec sample = Sample(tick, 0.80f, 0f);
                sample.ComZ = comPosition;
                sample.ComVelocityZ = comVelocity;
                samples.Add(sample);
            }
            return Trace(samples);
        }

        private static List<SampleSpec> BalanceLossSamples(bool forward)
        {
            List<SampleSpec> samples = new List<SampleSpec>
            {
                Sample(0ul, 1.00f, 0f),
                Sample(1ul, 0.90f, -0.20f),
                Sample(2ul, 0.80f, -0.20f)
            };
            SquatFailureCalibration calibration = SquatFailureCalibration.Default;
            float comPosition = forward
                ? 0.30f - calibration.BalanceSupportMarginFailureM + 0.01f
                : -0.30f + calibration.BalanceSupportMarginFailureM - 0.01f;
            float comVelocity = forward
                ? calibration.BalanceOutwardComVelocityMps + 0.01f
                : -calibration.BalanceOutwardComVelocityMps - 0.01f;
            int firstFailureTick = 3;
            int lastFailureTick = firstFailureTick + calibration.BalancePersistenceTicks - 1;
            for (ulong tick = (ulong)firstFailureTick; tick <= (ulong)lastFailureTick; tick++)
            {
                SampleSpec sample = Sample(tick, 0.80f, 0f);
                sample.ComZ = comPosition;
                sample.ComVelocityZ = comVelocity;
                samples.Add(sample);
            }
            return samples;
        }

        private static bool ContainsKind(SquatFailureResult result, SquatFailureKind kind)
        {
            if (result.FailureRecord == null)
                return false;
            if (result.FailureRecord.PrimaryFailureKind == kind)
                return true;
            for (int index = 0; index < result.FailureRecord.SecondaryEvents.Count; index++)
            {
                if (result.FailureRecord.SecondaryEvents[index].Kind == kind)
                    return true;
            }
            return false;
        }

        private static List<SampleSpec> BaseThroughAscentEstablishment()
        {
            List<SampleSpec> samples = GoodAttemptSamples();
            samples.RemoveRange(9, samples.Count - 9);
            return samples;
        }

        private static SampleSpec Sample(ulong tick, float barY, float barVelocityY)
        {
            return new SampleSpec(tick, barY, barVelocityY);
        }

        private static SquatTrace Trace(IReadOnlyList<SampleSpec> samples)
        {
            SquatTrace trace = new SquatTrace(samples.Count);
            trace.BeginRecording();
            for (int index = 0; index < samples.Count; index++)
                trace.Append(Snapshot(samples[index]));
            trace.EndRecording();
            return trace;
        }

        private static SquatObservationSnapshot Snapshot(SampleSpec spec)
        {
            double time = spec.Tick * StepSeconds;
            PlayerIntentFrame intent = new PlayerIntentFrame(
                spec.Tick,
                time,
                IntentEdgeFlags.None,
                0.5f,
                1f,
                spec.Drive01,
                0f,
                1f,
                true,
                true,
                spec.Drive01 > 0f,
                false,
                true,
                false,
                false);

            SquatBarObservation bar = spec.BarAvailable
                ? SquatBarObservation.Available(
                    new Vector3Value(0f, spec.BarY, 0f),
                    new Vector3Value(0f, spec.BarVelocityY, 0f),
                    QuaternionValue.Identity,
                    new Vector3Value(0f, 0f, 0f),
                    spec.LoadKg,
                    spec.SaddleAvailable
                        ? SquatTelemetryAvailability.AVAILABLE
                        : SquatTelemetryAvailability.NOT_AVAILABLE,
                    spec.SaddleAvailable && spec.SaddleAttached,
                    spec.SaddleAvailable && spec.SaddleBroken,
                    spec.SaddleAvailable ? spec.SaddleSeparationM : float.NaN)
                : SquatBarObservation.Unavailable();
            SquatDepthLandmarks depth = spec.DepthAvailable
                ? new SquatDepthLandmarks(
                    spec.LeftDepthM,
                    spec.RightDepthM,
                    0f,
                    0f)
                : SquatDepthLandmarks.Unavailable();
            SquatSupportObservation support = spec.SupportAvailable
                ? new SquatSupportObservation(
                    SquatTelemetryAvailability.AVAILABLE,
                    new Vector3Value(spec.ComX, 1f, spec.ComZ),
                    new Vector3Value(spec.ComVelocityX, 0f, spec.ComVelocityZ),
                    125f,
                    SquatTelemetryAvailability.AVAILABLE,
                    spec.HasSupport,
                    spec.HasSupport ? -0.30f : float.NaN,
                    spec.HasSupport ? 0.30f : float.NaN,
                    spec.HasSupport ? -0.20f : float.NaN,
                    spec.HasSupport ? 0.20f : float.NaN,
                    spec.HasSupport ? 0f : float.NaN,
                    spec.HasSupport ? 2 : 0,
                    SquatTelemetryAvailability.NOT_AVAILABLE,
                    SquatTelemetryValue.UnavailableVector3,
                    0f)
                : SquatSupportObservation.Unavailable();
            SquatJointObservation joint = spec.JointsAvailable
                ? SquatJointObservation.Available(
                    spec.KneeAngleRad,
                    new Vector3Value(0f, 0f, 0f),
                    spec.KneeAngleRad,
                    0f,
                    spec.LimitProximity,
                    spec.ModeledDemand,
                    100f,
                    1f,
                    1f)
                : SquatJointObservation.Unavailable();
            SquatJointObservationSet joints = new SquatJointObservationSet(
                joint, joint, joint, joint, joint, joint, joint, joint);
            SquatFootObservation foot = spec.FootAvailable
                ? SquatFootObservation.Available(spec.FootContact, spec.FootContact ? 1 : 0, 1, 0f, 0f)
                : SquatFootObservation.Unavailable();
            SquatTelemetryQualityFlags quality =
                SquatTelemetryQualityFlags.POST_PHYSICS |
                SquatTelemetryQualityFlags.RAW |
                SquatTelemetryQualityFlags.ATTEMPT_RELATIVE_TIME |
                SquatTelemetryQualityFlags.NONE;
            if (spec.BarAvailable)
                quality |= SquatTelemetryQualityFlags.BAR_AVAILABLE;
            if (spec.SupportAvailable)
                quality |= SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE;
            if (spec.SupportAvailable && spec.HasSupport)
                quality |= SquatTelemetryQualityFlags.SUPPORT_CONTACT_PRESENT;
            if (spec.DepthAvailable)
                quality |= SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE;
            if (spec.JointsAvailable)
                quality |= SquatTelemetryQualityFlags.JOINTS_AVAILABLE;
            if (spec.DriveAvailable)
                quality |= SquatTelemetryQualityFlags.DRIVE_DIAGNOSTICS_AVAILABLE;
            if (spec.FootAvailable)
                quality |= SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE |
                    SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE;

            return new SquatObservationSnapshot(
                spec.Tick,
                time,
                StepSeconds,
                true,
                time,
                spec.State,
                spec.Direction,
                spec.Sq,
                SquatIntentSnapshot.From(intent),
                bar,
                depth,
                support,
                foot,
                foot,
                joints,
                spec.PelvisAvailable
                    ? new Vector3Value(0f, spec.PelvisY, 0f)
                    : SquatTelemetryValue.UnavailableVector3,
                spec.PelvisAvailable
                    ? new Vector3Value(0f, spec.PelvisVelocityY, 0f)
                    : SquatTelemetryValue.UnavailableVector3,
                spec.TrunkAvailable ? QuaternionValue.Identity : SquatTelemetryValue.UnavailableQuaternion,
                spec.TrunkAvailable ? spec.TrunkPitchRad : float.NaN,
                spec.DriveAvailable
                    ? SquatTelemetryAvailability.AVAILABLE
                    : SquatTelemetryAvailability.NOT_AVAILABLE,
                spec.DriveAvailable && spec.DriveSaturated,
                spec.DriveAvailable ? spec.ModeledDemand : float.NaN,
                quality);
        }

        private sealed class SampleSpec
        {
            public SampleSpec(ulong tick, float barY, float barVelocityY)
            {
                Tick = tick;
                BarY = barY;
                BarVelocityY = barVelocityY;
                PelvisY = barY;
                PelvisVelocityY = barVelocityY;
            }

            public ulong Tick;
            public float BarY;
            public float BarVelocityY;
            public float PelvisY;
            public float PelvisVelocityY;
            public float ComX;
            public float ComZ;
            public float ComVelocityX;
            public float ComVelocityZ;
            public float LeftDepthM = -0.02f;
            public float RightDepthM = -0.02f;
            public float KneeAngleRad;
            public float TrunkPitchRad;
            public float LimitProximity;
            public float ModeledDemand;
            public float LoadKg = 25f;
            public float Drive01 = 1f;
            public bool DriveSaturated;
            public bool HasSupport = true;
            public bool FootContact = true;
            public bool BarAvailable = true;
            public bool SaddleAvailable = true;
            public bool SupportAvailable = true;
            public bool DepthAvailable = true;
            public bool JointsAvailable = true;
            public bool DriveAvailable = true;
            public bool FootAvailable = true;
            public bool PelvisAvailable = true;
            public bool TrunkAvailable = true;
            public bool SaddleAttached = true;
            public bool SaddleBroken;
            public float SaddleSeparationM;
            public SquatState State = SquatState.ASCENT;
            public SquatPhaseDirection Direction = SquatPhaseDirection.Ascent;
            public float Sq;
        }
    }
}
