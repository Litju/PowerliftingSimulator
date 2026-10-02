using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM12SquatRuleProcessorTests
    {
        private const double StepSeconds = 0.01d;

        [Test]
        public void GOOD_LIFT_USES_FROZEN_PHYSICAL_EVIDENCE_AND_COMMAND_TIMELINE()
        {
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(), CanonicalTimeline());

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(judgment.HasPrimaryViolation, Is.False);
            Assert.That(judgment.ViolationCount, Is.EqualTo(0));
        }

        [Test]
        public void SHALLOW_BOTH_SIDES_IS_NO_LIFT_WITH_INSUFFICIENT_DEPTH()
        {
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(depthAtBottomM: 0f), CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.INSUFFICIENT_DEPTH);
        }

        [Test]
        public void ONE_HIP_HIGH_REJECTS_THE_ATTEMPT_CONSERVATIVELY()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(leftDepthAtBottomM: -0.02f, rightDepthAtBottomM: 0f),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.INSUFFICIENT_DEPTH);
        }

        [Test]
        public void EARLY_DESCENT_IS_FOUND_FROM_KNEE_AND_BAR_MOTION_NOT_REFERENCE_PHASE()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(descentOnsetTick: 2ul, phaseConflict: true),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.EARLY_DESCENT);
        }

        [Test]
        public void DOUBLE_DESCENT_IS_PRESERVED_FROM_RAW_BAR_REVERSAL_BEFORE_ASCENT_ESTABLISHMENT()
        {
            SquatAttemptJudgment judgment = Evaluate(BuildDoubleDescentTrace(), CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.DOUBLE_DESCENT);
        }

        [Test]
        public void WHOLE_BAR_DOWNWARD_MOVEMENT_AFTER_ASCENT_IS_A_SEPARATE_VIOLATION()
        {
            SquatAttemptJudgment judgment = Evaluate(BuildDownwardMovementTrace(), DownwardMovementTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.DOWNWARD_MOVEMENT);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.DOUBLE_DESCENT), Is.False);
        }

        [Test]
        public void START_WITH_UNLOCKED_KNEES_IS_NO_LIFT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(startKneeAngleRad: 0.15f),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.FAILED_START_POSITION);
        }

        [Test]
        public void FINAL_UNLOCKED_KNEES_ARE_NOT_REPLACED_BY_LEGACY_LOCKOUT_BOOLEAN()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(finalKneeAngleRad: 0.15f),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.FAILED_LOCKOUT);
        }

        [Test]
        public void RERACK_BEFORE_RACK_COMMAND_IS_NO_LIFT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(),
                new SquatRuleCommandTimeline(new[]
                {
                    Command(SquatRuleCommandKind.SquatCommandIssued, 3ul),
                    Command(SquatRuleCommandKind.RackCommandIssued, 13ul),
                    Command(SquatRuleCommandKind.RerackStarted, 12ul)
                }));

            AssertNoLiftWith(judgment, SquatRuleViolationKind.EARLY_RACK);
        }

        [Test]
        public void FOOT_ROCKING_AFTER_VALID_RACK_DOES_NOT_CREATE_A_SUPPORT_VIOLATION()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(postRackSlipM: 0.25f),
                CanonicalTimeline());

            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION), Is.False);
        }

        [Test]
        public void DISALLOWED_SUPPORT_PROXY_IS_NO_LIFT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(supportSlipM: 0.05f),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION);
        }

        [Test]
        public void PRE_SQUAT_CUMULATIVE_SLIP_DOES_NOT_CREATE_SUPPORT_VIOLATION()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(slipAtSquatM: 0.03f, supportSlipM: 0.03f),
                CanonicalTimeline());

            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION), Is.False);
        }

        [Test]
        public void POST_SQUAT_INCREMENTAL_SLIP_CAN_CREATE_SUPPORT_VIOLATION()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(slipAtSquatM: 0.03f, supportSlipM: 0.06f),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION);
            Assert.That(judgment.PrimaryViolation.MeasuredValueA, Is.EqualTo(0.03f).Within(0.00001f));
        }

        [Test]
        public void POST_SQUAT_PRE_DESCENT_FOOT_MOVEMENT_IS_JUDGED()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(
                    descentOnsetTick: 5ul,
                    delayDescentUntilAfterCommand: true,
                    supportLostBeforeDescent: true),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION);
            Assert.That(judgment.PrimaryViolation.OnsetTick, Is.EqualTo(3ul));
        }

        [Test]
        public void BAR_SETTLING_WITH_LOCKED_KNEES_DOES_NOT_CREATE_EARLY_DESCENT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(barSettlingBeforeSquat: true),
                CanonicalTimeline());

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.EARLY_DESCENT), Is.False);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.FAILED_START_POSITION), Is.True);
        }

        [Test]
        public void KNEE_UNLOCK_BEFORE_SQUAT_CREATES_EARLY_DESCENT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(descentOnsetTick: 2ul),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.EARLY_DESCENT);
        }

        [Test]
        public void LEGAL_SETUP_KNEE_FLEX_BEFORE_ESTABLISHED_START_DOES_NOT_CREATE_EARLY_DESCENT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildLegalSetupKneeFlexTrace(),
                new SquatRuleCommandTimeline(6ul, 16ul, 17ul));

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(judgment.HasDecision, Is.True);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.EARLY_DESCENT), Is.False);
        }

        [Test]
        public void SAME_TICK_KNEE_UNLOCK_REMAINS_NOT_EARLY()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(descentOnsetTick: 3ul),
                CanonicalTimeline());

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.EARLY_DESCENT), Is.False);
        }

        [Test]
        public void RACK_AFTER_TRACE_END_IS_INCOMPLETE()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(),
                new SquatRuleCommandTimeline(3ul, 15ul, 16ul));

            AssertIncomplete(judgment);
        }

        [Test]
        public void RERACK_AFTER_TRACE_END_IS_INCOMPLETE()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(),
                new SquatRuleCommandTimeline(3ul, 13ul, 15ul));

            AssertIncomplete(judgment);
        }

        [Test]
        public void SQUAT_COMMAND_BEFORE_TRACE_START_IS_INCOMPLETE()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(tickOffset: 1ul),
                new SquatRuleCommandTimeline(0ul, 13ul, 14ul));

            AssertIncomplete(judgment);
        }

        [Test]
        public void TRACE_GAP_IN_RULE_WINDOW_IS_INCOMPLETE()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(omittedTick: 6ul),
                CanonicalTimeline());

            AssertIncomplete(judgment);
        }

        [Test]
        public void FULLY_COVERED_COMMAND_TIMELINE_REMAINS_JUDGEABLE()
        {
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(), CanonicalTimeline());

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(judgment.HasDecision, Is.True);
        }

        [Test]
        public void COHERENT_TIMELINE_REMAINS_JUDGEABLE()
        {
            SquatRuleCommandTimeline timeline = new SquatRuleCommandTimeline(new[]
            {
                Command(
                    SquatRuleCommandKind.SquatCommandIssued,
                    3ul,
                    SimulationConstants.TimeForTick(3ul) + FoundationTolerances.SimulationTimeMapping * 0.5d),
                Command(SquatRuleCommandKind.RackCommandIssued, 13ul),
                Command(SquatRuleCommandKind.RerackStarted, 14ul)
            });
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(), timeline);

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(judgment.HasDecision, Is.True);
        }

        [Test]
        public void VALID_LOCKOUT_PLUS_SUPPORT_VIOLATION_DOES_NOT_CREATE_FAILED_LOCKOUT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(supportLostAtFinal: true),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.FAILED_LOCKOUT), Is.False);
            Assert.That(judgment.PrimaryViolationKind, Is.EqualTo(SquatRuleViolationKind.SUPPORT_VIOLATION));
        }

        [Test]
        public void FAILED_LOCKOUT_WITH_VALID_SUPPORT_STILL_ADDS_FAILED_LOCKOUT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(finalKneeAngleRad: 0.15f),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.FAILED_LOCKOUT);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION), Is.False);
        }

        [Test]
        public void LOCKOUT_AND_SUPPORT_FAILURE_CAN_COEXIST_WHEN_BOTH_ARE_REAL()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(finalKneeAngleRad: 0.15f, supportLostAtFinal: true),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.FAILED_LOCKOUT);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.SUPPORT_VIOLATION), Is.True);
        }

        [Test]
        public void PRIMARY_REASON_NOT_POLLUTED_BY_SUPPORT_INSIDE_LOCKOUT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(supportLostAtFinal: true),
                CanonicalTimeline());

            Assert.That(judgment.PrimaryViolationKind, Is.EqualTo(SquatRuleViolationKind.SUPPORT_VIOLATION));
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.FAILED_LOCKOUT), Is.False);
        }

        [Test]
        public void MULTIPLE_VIOLATIONS_ARE_RETAINED_AND_PRIMARY_IS_NOT_INSERTION_ORDER()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildMultipleViolationTrace(),
                CanonicalTimeline());

            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.NO_LIFT));
            Assert.That(judgment.ViolationCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.INSUFFICIENT_DEPTH), Is.True);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.DOWNWARD_MOVEMENT), Is.True);
            Assert.That(HasViolation(judgment, SquatRuleViolationKind.FAILED_LOCKOUT), Is.True);
            Assert.That(judgment.PrimaryViolation.Kind, Is.EqualTo(SquatRuleViolationKind.INSUFFICIENT_DEPTH));
        }

        [Test]
        public void PHYSICAL_COMPLETION_WITH_RULE_FAILURE_REMAINS_NO_LIFT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(depthAtBottomM: 0f),
                CanonicalTimeline());

            Assert.That(judgment.PhysicalCompletion, Is.EqualTo(SquatPhysicalCompletionStatus.COMPLETED));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.NO_LIFT));
        }

        [Test]
        public void MISSING_BAR_EVIDENCE_CANNOT_BE_LAUNDERED_INTO_GOOD_LIFT_OR_NO_LIFT()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(barAvailable: false),
                CanonicalTimeline());

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.INSUFFICIENT_EVIDENCE));
            Assert.That(judgment.HasDecision, Is.False);
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.UNDETERMINED));
        }

        [Test]
        public void MISSING_COMMAND_METADATA_IS_INCOMPLETE_WITHOUT_A_COMPETITION_DECISION()
        {
            SquatRuleCommandTimeline timeline = new SquatRuleCommandTimeline(new[]
            {
                Command(SquatRuleCommandKind.SquatCommandIssued, 3ul),
                Command(SquatRuleCommandKind.RackCommandIssued, 13ul)
            });
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(), timeline);

            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.INCOMPLETE_ATTEMPT));
            Assert.That(judgment.HasDecision, Is.False);
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.UNDETERMINED));
        }

        [Test]
        public void COMMAND_EVENT_TICK_TIME_MISMATCH_IS_INCOMPLETE()
        {
            SquatRuleCommandTimeline timeline = new SquatRuleCommandTimeline(new[]
            {
                Command(SquatRuleCommandKind.SquatCommandIssued, 3ul, 0.031d),
                Command(SquatRuleCommandKind.RackCommandIssued, 13ul),
                Command(SquatRuleCommandKind.RerackStarted, 14ul)
            });
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(), timeline);

            AssertIncomplete(judgment);
        }

        [Test]
        public void RERACK_BEFORE_SQUAT_IS_INCOMPLETE()
        {
            SquatRuleCommandTimeline timeline = new SquatRuleCommandTimeline(new[]
            {
                Command(SquatRuleCommandKind.SquatCommandIssued, 3ul),
                Command(SquatRuleCommandKind.RackCommandIssued, 13ul),
                Command(SquatRuleCommandKind.RerackStarted, 2ul)
            });
            SquatAttemptJudgment judgment = Evaluate(BuildGoodTrace(), timeline);

            AssertIncomplete(judgment);
        }

        [Test]
        public void ACTIVE_UNFROZEN_AND_EMPTY_TRACES_ARE_REFUSED_EXPLICITLY()
        {
            SquatTrace active = BuildGoodTrace();
            active.BeginRecording();
            SquatAttemptJudgment activeJudgment = Evaluate(active, CanonicalTimeline());

            SquatTrace empty = new SquatTrace(1);
            empty.BeginRecording();
            empty.EndRecording();
            SquatAttemptJudgment emptyJudgment = Evaluate(empty, CanonicalTimeline());

            Assert.That(activeJudgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.INVALID_TRACE));
            Assert.That(emptyJudgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.INVALID_TRACE));
            Assert.That(activeJudgment.HasDecision, Is.False);
            Assert.That(emptyJudgment.HasDecision, Is.False);
        }

        [Test]
        public void SAME_INPUT_EVALUATED_TWICE_PRODUCES_IDENTICAL_JUDGMENT()
        {
            SquatTrace trace = BuildGoodTrace(depthAtBottomM: 0f);
            SquatRuleCommandTimeline timeline = CanonicalTimeline();
            SquatAttemptJudgment first = Evaluate(trace, timeline);
            SquatAttemptJudgment second = Evaluate(trace, timeline);

            AssertJudgmentsEqual(first, second);
        }

        [Test]
        public void RULE_EVALUATION_DOES_NOT_MUTATE_THE_FROZEN_TRACE()
        {
            SquatTrace trace = BuildGoodTrace();
            int countBefore = trace.Count;
            SquatObservationSnapshot firstBefore = trace.GetSnapshot(0);
            SquatObservationSnapshot lastBefore = trace.GetSnapshot(trace.Count - 1);

            Evaluate(trace, CanonicalTimeline());

            Assert.That(trace.Count, Is.EqualTo(countBefore));
            Assert.That(trace.IsFrozen, Is.True);
            Assert.That(trace.GetSnapshot(0).SimulationTick, Is.EqualTo(firstBefore.SimulationTick));
            Assert.That(trace.GetSnapshot(0).Bar.PositionWorldMeters.Y, Is.EqualTo(firstBefore.Bar.PositionWorldMeters.Y));
            Assert.That(trace.GetSnapshot(trace.Count - 1).SimulationTick, Is.EqualTo(lastBefore.SimulationTick));
            Assert.That(trace.GetSnapshot(trace.Count - 1).Bar.PositionWorldMeters.Y, Is.EqualTo(lastBefore.Bar.PositionWorldMeters.Y));
        }

        [Test]
        public void PHASE_STATE_AND_REFERENCE_PHASE_CANNOT_OVERRIDE_PHYSICAL_EVIDENCE()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(depthAtBottomM: 0f, phaseConflict: true),
                CanonicalTimeline());

            AssertNoLiftWith(judgment, SquatRuleViolationKind.INSUFFICIENT_DEPTH);
        }

        [Test]
        public void LOAD_METADATA_DOES_NOT_DIRECTLY_CHANGE_RULE_OUTCOME()
        {
            SquatAttemptJudgment light = Evaluate(BuildGoodTrace(loadKg: 0f), CanonicalTimeline());
            SquatAttemptJudgment heavy = Evaluate(BuildGoodTrace(loadKg: 500f), CanonicalTimeline());

            Assert.That(light.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(heavy.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(light.ViolationCount, Is.EqualTo(heavy.ViolationCount));
        }

        [Test]
        public void DEPTH_BOUNDARY_USES_EXISTING_NAMED_MARGIN_WITHOUT_AVERAGING_SIDES()
        {
            float margin = SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M;
            SquatAttemptJudgment exactlyAtMargin = Evaluate(
                BuildGoodTrace(depthAtBottomM: -margin),
                CanonicalTimeline());
            SquatAttemptJudgment justInside = Evaluate(
                BuildGoodTrace(depthAtBottomM: -margin + 0.0001f),
                CanonicalTimeline());

            Assert.That(exactlyAtMargin.Outcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT),
                exactlyAtMargin.PrimaryViolationKind + " count=" + exactlyAtMargin.ViolationCount);
            Assert.That(justInside.Outcome, Is.EqualTo(SquatJudgmentOutcome.NO_LIFT));
            Assert.That(HasViolation(justInside, SquatRuleViolationKind.INSUFFICIENT_DEPTH), Is.True);
        }

        [Test]
        public void COMMAND_TICK_BOUNDARY_TREATS_SAME_TICK_AS_NOT_EARLY()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(descentOnsetTick: 3ul),
                CanonicalTimeline());

            Assert.That(HasViolation(judgment, SquatRuleViolationKind.EARLY_DESCENT), Is.False);
        }

        [Test]
        public void RULEBOOK_MAPPING_HAS_EXPLICIT_DISPOSITION_FOR_EVERY_V1_ENTRY()
        {
            SquatRuleSetMetadata metadata = SquatRuleSetMetadata.Default;

            Assert.That(metadata.RuleMappingCount, Is.GreaterThan(0));
            for (int index = 0; index < metadata.RuleMappingCount; index++)
            {
                SquatRuleMapping mapping = metadata.RuleMappingAt(index);
                Assert.That(mapping.OfficialSection, Is.Not.Null.And.Not.Empty);
                Assert.That(mapping.Disposition, Is.EqualTo(SquatRuleMappingDisposition.SOURCE_DIRECT_IMPLEMENTED)
                    .Or.EqualTo(SquatRuleMappingDisposition.RULE_DERIVED_GAME_PROXY)
                    .Or.EqualTo(SquatRuleMappingDisposition.GAME_SIMPLIFICATION)
                    .Or.EqualTo(SquatRuleMappingDisposition.NOT_OBSERVABLE_V1)
                    .Or.EqualTo(SquatRuleMappingDisposition.OUT_OF_SCOPE_V1));
                Assert.That(mapping.ImplementationPredicate, Is.Not.Null.And.Not.Empty);
            }
        }

        [Test]
        public void VIOLATION_PROVENANCE_CARRIES_RULESET_SOURCE_CHANNELS_AND_TOLERANCE_VERSION()
        {
            SquatAttemptJudgment judgment = Evaluate(
                BuildGoodTrace(supportSlipM: 0.05f),
                CanonicalTimeline());
            SquatRuleViolationRecord violation = judgment.PrimaryViolation;

            Assert.That(violation.RuleSetId, Is.EqualTo(SquatRuleSetMetadata.DefaultRuleSetId));
            Assert.That(violation.RuleImplementationVersion, Is.EqualTo("GAM12_P2A1_RULE_PROCESSOR_V1"));
            Assert.That(violation.ToleranceVersion, Is.EqualTo(SquatRuleToleranceSet.DefaultVersion));
            Assert.That(violation.SourceClassification, Is.EqualTo(SquatRuleImplementationClass.GAME_SIMPLIFICATION));
            Assert.That(violation.EvidenceChannels, Is.Not.EqualTo(SquatRuleEvidenceChannel.NONE));
            Assert.That(violation.MeasuredValueA, Is.GreaterThan(0f));
        }

        [Test]
        public void EVERY_NUMERICAL_RULE_BOUNDARY_HAS_NAMED_UNIT_AND_VERSIONED_RATIONALE()
        {
            SquatRuleToleranceSet toleranceSet = SquatRuleToleranceSet.Default;

            Assert.That(toleranceSet.Descriptors.Count, Is.GreaterThan(0));
            for (int index = 0; index < toleranceSet.Descriptors.Count; index++)
            {
                SquatRuleToleranceDescriptor descriptor = toleranceSet.Descriptors[index];
                Assert.That(descriptor.Name, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Unit, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Source, Is.EqualTo("GAME_CALIBRATION"));
                Assert.That(descriptor.Rationale, Is.Not.Null.And.Not.Empty);
                Assert.That(descriptor.Version, Is.EqualTo(toleranceSet.Version));
            }
        }

        [Test]
        public void RULE_DOMAIN_FILES_DO_NOT_REFERENCE_UNITY_OR_PHYSICAL_RUNTIME_TYPES()
        {
            string path = Path.GetFullPath("Assets/Scripts/Squat/SquatRuleProcessor.cs");
            string source = File.ReadAllText(path);

            Assert.That(source, Does.Not.Contain("UnityEngine"));
            Assert.That(source, Does.Not.Contain("Rigidbody"));
            Assert.That(source, Does.Not.Contain("Transform"));
            Assert.That(source, Does.Not.Contain("Animator"));
            Assert.That(source, Does.Not.Contain("Camera"));
            Assert.That(source, Does.Not.Contain("MonoBehaviour"));
            Assert.That(source, Does.Not.Contain("AddForce"));
            Assert.That(source, Does.Not.Contain("AddTorque"));
        }

        private static SquatAttemptJudgment Evaluate(SquatTrace trace, SquatRuleCommandTimeline timeline)
        {
            return new SquatRuleProcessor().Evaluate(trace, timeline);
        }

        private static void AssertNoLiftWith(
            SquatAttemptJudgment judgment,
            SquatRuleViolationKind expected)
        {
            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.NO_LIFT));
            Assert.That(HasViolation(judgment, expected), Is.True, expected.ToString());
        }

        private static void AssertIncomplete(SquatAttemptJudgment judgment)
        {
            Assert.That(judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.INCOMPLETE_ATTEMPT));
            Assert.That(judgment.Outcome, Is.EqualTo(SquatJudgmentOutcome.UNDETERMINED));
            Assert.That(judgment.HasDecision, Is.False);
        }

        private static bool HasViolation(SquatAttemptJudgment judgment, SquatRuleViolationKind kind)
        {
            for (int index = 0; index < judgment.ViolationCount; index++)
            {
                if (judgment.ViolationAt(index).Kind == kind)
                    return true;
            }

            return false;
        }

        private static void AssertJudgmentsEqual(SquatAttemptJudgment left, SquatAttemptJudgment right)
        {
            Assert.That(right.EvidenceStatus, Is.EqualTo(left.EvidenceStatus));
            Assert.That(right.Outcome, Is.EqualTo(left.Outcome));
            Assert.That(right.PhysicalCompletion, Is.EqualTo(left.PhysicalCompletion));
            Assert.That(right.ViolationCount, Is.EqualTo(left.ViolationCount));
            Assert.That(right.RuleSet.RuleSetId, Is.EqualTo(left.RuleSet.RuleSetId));
            Assert.That(right.RuleImplementationVersion, Is.EqualTo(left.RuleImplementationVersion));
            for (int index = 0; index < left.ViolationCount; index++)
            {
                SquatRuleViolationRecord expected = left.ViolationAt(index);
                SquatRuleViolationRecord actual = right.ViolationAt(index);
                Assert.That(actual.Kind, Is.EqualTo(expected.Kind));
                Assert.That(actual.OnsetTick, Is.EqualTo(expected.OnsetTick));
                Assert.That(actual.EvidenceChannels, Is.EqualTo(expected.EvidenceChannels));
                Assert.That(actual.MeasuredValueA, Is.EqualTo(expected.MeasuredValueA));
                Assert.That(actual.MeasuredValueB, Is.EqualTo(expected.MeasuredValueB));
            }
        }

        private static SquatRuleCommandTimeline CanonicalTimeline() => new SquatRuleCommandTimeline(new[]
        {
            Command(SquatRuleCommandKind.SquatCommandIssued, 3ul),
            Command(SquatRuleCommandKind.RackCommandIssued, 13ul),
            Command(SquatRuleCommandKind.RerackStarted, 14ul)
        });

        private static SquatRuleCommandTimeline DownwardMovementTimeline() => new SquatRuleCommandTimeline(new[]
        {
            Command(SquatRuleCommandKind.SquatCommandIssued, 3ul),
            Command(SquatRuleCommandKind.RackCommandIssued, 16ul),
            Command(SquatRuleCommandKind.RerackStarted, 17ul)
        });

        private static SquatRuleCommandEvent Command(SquatRuleCommandKind kind, ulong tick) =>
            new SquatRuleCommandEvent(kind, tick, tick * StepSeconds);

        private static SquatRuleCommandEvent Command(
            SquatRuleCommandKind kind,
            ulong tick,
            double simulationTimeSeconds) => new SquatRuleCommandEvent(
                kind,
                tick,
                simulationTimeSeconds);

        private static SquatTrace BuildGoodTrace(
            float depthAtBottomM = -0.02f,
            float leftDepthAtBottomM = float.NaN,
            float rightDepthAtBottomM = float.NaN,
            ulong descentOnsetTick = 4ul,
            float startKneeAngleRad = 0f,
            float finalKneeAngleRad = 0f,
            bool barAvailable = true,
            bool phaseConflict = false,
            float loadKg = 25f,
            float supportSlipM = 0f,
            float postRackSlipM = 0f,
            float slipAtSquatM = 0f,
            bool supportLostBeforeDescent = false,
            bool supportLostAtFinal = false,
            bool delayDescentUntilAfterCommand = false,
            bool barSettlingBeforeSquat = false,
            ulong tickOffset = 0ul,
            ulong? omittedTick = null)
        {
            float leftBottom = float.IsNaN(leftDepthAtBottomM) ? depthAtBottomM : leftDepthAtBottomM;
            float rightBottom = float.IsNaN(rightDepthAtBottomM) ? depthAtBottomM : rightDepthAtBottomM;
            float preCommandKnee = descentOnsetTick < 3ul ? 0.2f : startKneeAngleRad;
            float commandTickKnee = descentOnsetTick <= 3ul ? 0.2f : 0f;
            float commandTickBarVelocity = descentOnsetTick <= 3ul ? -0.12f : 0f;
            List<SquatObservationSnapshot> snapshots = new List<SquatObservationSnapshot>();
            snapshots.Add(Snapshot(tickOffset + 0ul, 1f, 0f, 0f, 0f, startKneeAngleRad, 0f, 0f, barAvailable, true, slipAtSquatM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 1ul, 1f, 0f, 0f, 0f, startKneeAngleRad, 0f, 0f, barAvailable, true, slipAtSquatM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 2ul, barSettlingBeforeSquat ? 0.95f : 1f, barSettlingBeforeSquat ? -0.12f : (descentOnsetTick == 2ul ? -0.12f : 0f), 0f, 0f, preCommandKnee, 0f, 0f, barAvailable, true, slipAtSquatM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 3ul, 1f, commandTickBarVelocity, 0f, 0f, commandTickKnee, 0f, 0f, barAvailable, true, slipAtSquatM, loadKg, phaseConflict, hasSupport: !supportLostBeforeDescent, footInContact: !supportLostBeforeDescent));
            snapshots.Add(Snapshot(tickOffset + 4ul, delayDescentUntilAfterCommand ? 1f : 0.95f, delayDescentUntilAfterCommand ? 0f : (descentOnsetTick == 4ul ? -0.12f : 0f), 0f, 0f, delayDescentUntilAfterCommand ? 0f : 0.2f, delayDescentUntilAfterCommand ? 0f : 0.2f, delayDescentUntilAfterCommand ? 0f : 0.1f, barAvailable, true, supportSlipM, loadKg, phaseConflict, hasSupport: !supportLostBeforeDescent, footInContact: !supportLostBeforeDescent));
            snapshots.Add(Snapshot(tickOffset + 5ul, 0.83f, -0.12f, 0f, 0f, 0.6f, 0.6f, 0.12f, barAvailable, true, supportSlipM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 6ul, 0.80f, 0f, leftBottom, rightBottom, 0.9f, 0.9f, 0.15f, barAvailable, true, supportSlipM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 7ul, 0.84f, 0.12f, leftBottom, rightBottom, 0.7f, 0.7f, 0.12f, barAvailable, true, supportSlipM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 8ul, 0.90f, 0.12f, leftBottom, rightBottom, 0.4f, 0.4f, 0.08f, barAvailable, true, supportSlipM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 9ul, 1.00f, 0.10f, leftBottom, rightBottom, 0.1f, 0.1f, 0.02f, barAvailable, true, supportSlipM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 10ul, 1.02f, 0.01f, leftBottom, rightBottom, finalKneeAngleRad, 0f, 0f, barAvailable, true, supportSlipM, loadKg, phaseConflict, hasSupport: !supportLostAtFinal, footInContact: !supportLostAtFinal));
            snapshots.Add(Snapshot(tickOffset + 11ul, 1.02f, 0f, leftBottom, rightBottom, finalKneeAngleRad, 0f, 0f, barAvailable, true, supportSlipM, loadKg, phaseConflict, hasSupport: !supportLostAtFinal, footInContact: !supportLostAtFinal));
            snapshots.Add(Snapshot(tickOffset + 12ul, 1.02f, 0f, leftBottom, rightBottom, finalKneeAngleRad, 0f, 0f, barAvailable, true, supportSlipM, loadKg, phaseConflict, hasSupport: !supportLostAtFinal, footInContact: !supportLostAtFinal));
            snapshots.Add(Snapshot(tickOffset + 13ul, 1.02f, 0f, leftBottom, rightBottom, finalKneeAngleRad, 0f, 0f, barAvailable, true, supportSlipM, loadKg, phaseConflict));
            snapshots.Add(Snapshot(tickOffset + 14ul, 1.02f, 0f, leftBottom, rightBottom, finalKneeAngleRad, 0f, 0f, barAvailable, true, postRackSlipM, loadKg, phaseConflict));
            if (omittedTick.HasValue)
                snapshots.RemoveAll(snapshot => snapshot.SimulationTick == omittedTick.Value);
            return Trace(snapshots);
        }

        private static SquatTrace BuildLegalSetupKneeFlexTrace()
        {
            List<SquatObservationSnapshot> snapshots = new List<SquatObservationSnapshot>();
            snapshots.Add(Snapshot(0ul, 1f, 0f, 0f, 0f, 0.15f, 0f, 0f));
            snapshots.Add(Snapshot(1ul, 1f, 0f, 0f, 0f, 0.15f, 0f, 0f));
            snapshots.Add(Snapshot(2ul, 1f, 0f, 0f, 0f, 0.15f, 0f, 0f));
            snapshots.Add(Snapshot(3ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(4ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(5ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(6ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(7ul, 0.90f, -0.12f, 0f, 0f, 0.3f, 0.3f, 0.1f));
            snapshots.Add(Snapshot(8ul, 0.80f, -0.12f, -0.02f, -0.02f, 0.8f, 0.8f, 0.15f));
            snapshots.Add(Snapshot(9ul, 0.79f, 0f, -0.02f, -0.02f, 0.9f, 0.9f, 0.15f));
            snapshots.Add(Snapshot(10ul, 0.84f, 0.12f, -0.02f, -0.02f, 0.7f, 0.7f, 0.12f));
            snapshots.Add(Snapshot(11ul, 0.94f, 0.12f, -0.02f, -0.02f, 0.4f, 0.4f, 0.08f));
            snapshots.Add(Snapshot(12ul, 1.02f, 0.08f, -0.02f, -0.02f, 0.1f, 0.1f, 0.02f));
            snapshots.Add(Snapshot(13ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(14ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(15ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(16ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(17ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            return Trace(snapshots);
        }

        private static SquatTrace BuildDoubleDescentTrace()
        {
            List<SquatObservationSnapshot> snapshots = new List<SquatObservationSnapshot>();
            snapshots.Add(Snapshot(0ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(1ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(2ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(3ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(4ul, 0.90f, -0.12f, 0f, 0f, 0.3f, 0.3f, 0.1f));
            snapshots.Add(Snapshot(5ul, 0.80f, -0.12f, -0.02f, -0.02f, 0.8f, 0.8f, 0.15f));
            snapshots.Add(Snapshot(6ul, 0.79f, 0f, -0.02f, -0.02f, 0.9f, 0.9f, 0.15f));
            snapshots.Add(Snapshot(7ul, 0.83f, 0.10f, -0.02f, -0.02f, 0.8f, 0.8f, 0.15f));
            snapshots.Add(Snapshot(8ul, 0.79f, -0.10f, -0.02f, -0.02f, 0.85f, 0.85f, 0.15f));
            snapshots.Add(Snapshot(9ul, 0.75f, -0.10f, -0.02f, -0.02f, 0.9f, 0.9f, 0.15f));
            snapshots.Add(Snapshot(10ul, 0.86f, 0.12f, -0.02f, -0.02f, 0.6f, 0.6f, 0.1f));
            snapshots.Add(Snapshot(11ul, 0.96f, 0.12f, -0.02f, -0.02f, 0.3f, 0.3f, 0.05f));
            snapshots.Add(Snapshot(12ul, 1.02f, 0.08f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(13ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(14ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(15ul, 1.02f, 0f, -0.02f, -0.02f, 0f, 0f, 0f));
            return Trace(snapshots);
        }

        private static SquatTrace BuildDownwardMovementTrace(
            bool shallowDepth = false,
            bool finalUnlocked = false)
        {
            float bottomDepth = shallowDepth ? 0f : -0.02f;
            float finalKnee = finalUnlocked ? 0.15f : 0f;
            List<SquatObservationSnapshot> snapshots = new List<SquatObservationSnapshot>();
            snapshots.Add(Snapshot(0ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(1ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(2ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(3ul, 1f, 0f, 0f, 0f, 0f, 0f, 0f));
            snapshots.Add(Snapshot(4ul, 0.90f, -0.12f, 0f, 0f, 0.3f, 0.3f, 0.1f));
            snapshots.Add(Snapshot(5ul, 0.80f, -0.12f, bottomDepth, bottomDepth, 0.8f, 0.8f, 0.15f));
            snapshots.Add(Snapshot(6ul, 0.79f, 0f, bottomDepth, bottomDepth, 0.9f, 0.9f, 0.15f));
            snapshots.Add(Snapshot(7ul, 0.84f, 0.12f, bottomDepth, bottomDepth, 0.7f, 0.7f, 0.12f));
            snapshots.Add(Snapshot(8ul, 0.92f, 0.12f, bottomDepth, bottomDepth, 0.4f, 0.4f, 0.08f));
            snapshots.Add(Snapshot(9ul, 1.02f, 0.10f, bottomDepth, bottomDepth, 0.1f, 0.1f, 0.02f));
            snapshots.Add(Snapshot(10ul, 0.92f, -0.12f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(11ul, 0.80f, -0.12f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(12ul, 1.02f, 0.08f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(13ul, 1.02f, 0f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(14ul, 1.02f, 0f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(15ul, 1.02f, 0f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(16ul, 1.02f, 0f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            snapshots.Add(Snapshot(17ul, 1.02f, 0f, bottomDepth, bottomDepth, finalKnee, 0f, 0f));
            return Trace(snapshots);
        }

        private static SquatTrace BuildMultipleViolationTrace()
        {
            SquatTrace trace = BuildDownwardMovementTrace(true, true);
            return trace;
        }

        private static SquatTrace Trace(IReadOnlyList<SquatObservationSnapshot> snapshots)
        {
            SquatTrace trace = new SquatTrace(snapshots.Count);
            trace.BeginRecording();
            for (int index = 0; index < snapshots.Count; index++)
                trace.Append(snapshots[index]);
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
            float hipAngleRad,
            float trunkAngleRad,
            bool barAvailable = true,
            bool supportAvailable = true,
            float slipM = 0f,
            float loadKg = 25f,
            bool phaseConflict = false,
            bool hasSupport = true,
            bool footInContact = true)
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
                    loadKg,
                    SquatTelemetryAvailability.AVAILABLE,
                    true,
                    false,
                    0f)
                : SquatBarObservation.Unavailable();
            SquatDepthLandmarks depth = new SquatDepthLandmarks(
                leftDepthM,
                rightDepthM,
                0f,
                0f);
            SquatSupportObservation support = supportAvailable
                ? new SquatSupportObservation(
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
                    0f)
                : SquatSupportObservation.Unavailable();
            SquatFootObservation foot = new SquatFootObservation(
                SquatTelemetryAvailability.AVAILABLE,
                footInContact,
                footInContact ? 1 : 0,
                1,
                slipM,
                0f);
            SquatJointObservation knee = Joint(kneeAngleRad);
            SquatJointObservation hip = Joint(hipAngleRad);
            SquatJointObservation trunk = Joint(trunkAngleRad);
            SquatJointObservationSet joints = new SquatJointObservationSet(
                knee,
                knee,
                hip,
                hip,
                Joint(0f),
                Joint(0f),
                trunk,
                trunk);
            return new SquatObservationSnapshot(
                tick,
                time,
                StepSeconds,
                true,
                time,
                phaseConflict ? SquatState.LOCKOUT : SquatState.DESCENT,
                phaseConflict ? SquatPhaseDirection.Ascent : SquatPhaseDirection.Descent,
                phaseConflict ? 0f : 0.5f,
                SquatIntentSnapshot.From(intent),
                bar,
                depth,
                support,
                foot,
                foot,
                joints,
                new Vector3Value(0f, 1f, 0f),
                new Vector3Value(0f, 0f, 0f),
                QuaternionValue.Identity,
                0f,
                SquatTelemetryAvailability.AVAILABLE,
                false,
                0f,
                SquatTelemetryQualityFlags.POST_PHYSICS |
                SquatTelemetryQualityFlags.RAW |
                SquatTelemetryQualityFlags.BAR_AVAILABLE |
                SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE |
                SquatTelemetryQualityFlags.SUPPORT_CONTACT_PRESENT |
                SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE |
                SquatTelemetryQualityFlags.JOINTS_AVAILABLE |
                SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE |
                SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE);
        }

        private static SquatJointObservation Joint(float actualAngleRad) => SquatJointObservation.Available(
            actualAngleRad,
            new Vector3Value(0f, 0f, 0f),
            0f,
            actualAngleRad,
            0f,
            0f,
            100f,
            1f,
            1f);
    }
}
