using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM12SquatAttemptLifecycleIntegrationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int MaximumQualificationTicks = 1600;

        private FoundationBootstrap _bootstrap;
        private SquatPhysicalPrototypeController _controller;
        private readonly List<string> _unexpectedErrors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _unexpectedErrors.Clear();
            Application.logMessageReceived += CaptureUnexpectedError;
            // Unity's installed AI Assistant package attempts a relay
            // connection during batch PlayMode startup. That known editor
            // infrastructure error must not hide project errors, so the
            // callback below records every other error for teardown failure.
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }

            if (_bootstrap != null)
                Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _controller = null;
            Application.logMessageReceived -= CaptureUnexpectedError;
            LogAssert.ignoreFailingMessages = false;
            Assert.That(_unexpectedErrors, Is.Empty, string.Join("\n", _unexpectedErrors));
            yield return null;
        }

        [UnityTest]
        public IEnumerator COMPLETE_25KG_ATTEMPT_RECORD_IS_DETERMINISTIC()
        {
            List<AttemptSummary> summaries = new List<AttemptSummary>(3);
            for (int repeat = 0; repeat < 3; repeat++)
            {
                yield return LoadQualificationScene();
                _controller.enabled = false;
                _bootstrap.enabled = false;

                _controller.SetLoad(25f);
                _controller.BeginAttempt();
                Assert.That(_controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.START_WINDOW));

                int ticks = 0;
                while (_controller.AttemptRecord == null && ticks < MaximumQualificationTicks)
                {
                    Assert.That(
                        _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds),
                        Is.EqualTo(1));
                    ticks++;
                    if (ticks % 40 == 0)
                        yield return null;
                }

                SquatAttemptRecord record = _controller.AttemptRecord;
                Assert.That(
                    record,
                    Is.Not.Null,
                    "The complete 25 kg lifecycle did not finalize within the bounded tick budget. " +
                    "lifecycle=" + _controller.AttemptLifecycle.State +
                    ", recording=" + _controller.AttemptOrchestrator.IsRecording +
                    ", traceCount=" + _controller.ObservationTrace.Count +
                    ", adapterState=" + _controller.Adapter.State +
                    ", adapterSq=" + _controller.Adapter.Sq);
                Assert.That(record.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.PHYSICAL_LOCKOUT));
                Assert.That(record.CommandTimeline.Count, Is.EqualTo(3));
                Assert.That(record.CommandTimelineCovered, Is.True);
                Assert.That(record.StartWindowBeginTick, Is.EqualTo(record.EventTicks.StartWindowBeginTick));
                Assert.That(record.EventTicks.StartWindowEndTick, Is.LessThan(record.EventTicks.SquatCommandTick));
                Assert.That(record.EventTicks.SquatCommandTick, Is.Not.EqualTo(SquatAttemptEventTicks.NotAvailable));
                Assert.That(record.EventTicks.RackCommandTick, Is.Not.EqualTo(SquatAttemptEventTicks.NotAvailable));
                Assert.That(record.EventTicks.RerackStartedTick, Is.Not.EqualTo(SquatAttemptEventTicks.NotAvailable));
                Assert.That(record.Trace.IsFrozen, Is.True);
                Assert.That(record.Trace.IsTruthSealed, Is.True);
                Assert.That(record.TraceSampleCount, Is.GreaterThanOrEqualTo(3));
                Assert.That(
                    record.Judgment.EvidenceStatus,
                    Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE),
                    "rule=" + record.Judgment.EvidenceStatus +
                    ", outcome=" + record.Judgment.Outcome +
                    ", trace=" + record.FirstTraceTick + "-" + record.LastTraceTick +
                    ", commands=" + record.EventTicks.SquatCommandTick + "/" +
                    record.EventTicks.RackCommandTick + "/" + record.EventTicks.RerackStartedTick +
                    ", failure=" + record.FailureResult.EvidenceStatus + "/" + record.FailureResult.Outcome);
                Assert.That(
                    record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT ||
                    record.RuleOutcome == SquatJudgmentOutcome.NO_LIFT,
                    Is.True);
                if (record.RuleOutcome == SquatJudgmentOutcome.NO_LIFT)
                    Assert.That(record.Judgment.PrimaryViolationKind, Is.Not.EqualTo(SquatRuleViolationKind.NONE));

                // Debug.Log so the per-run event table is captured in the batch
                // Unity log and can be quoted as receipt evidence.
                Debug.LogFormat(
                    "25KG_RUN={0} START_WINDOW={1}-{2} SQUAT={3} DESCENT={4} BOTTOM={5} ASCENT={6} LOCKOUT={7} RACK={8} RERACK={9} TRACE_FREEZE={10} TRACE_COUNT={11} P2={12}/{13} VIOLATIONS={14} P3={15}/{16} PRIMARY={17} FAILURE_ONSET={18} FAILURE_LATCH={19} TERMINAL={20}@{21} TERMINAL_CONTEXT={22}",
                    repeat + 1,
                    record.EventTicks.StartWindowBeginTick,
                    record.EventTicks.StartWindowEndTick,
                    record.EventTicks.SquatCommandTick,
                    record.EventTicks.PhysicalDescentOnsetTick,
                    record.EventTicks.BottomTick,
                    record.EventTicks.AscentEstablishmentTick,
                    record.EventTicks.LockoutTick,
                    record.EventTicks.RackCommandTick,
                    record.EventTicks.RerackStartedTick,
                    record.TraceFreezeTick,
                    record.TraceSampleCount,
                    record.Judgment.EvidenceStatus,
                    record.RuleOutcome,
                    FormatViolations(record.Judgment),
                    record.FailureResult.EvidenceStatus,
                    record.PhysicalFailureOutcome,
                    record.FailureResult.PrimaryFailureKind,
                    record.EventTicks.FailureOnsetTick,
                    record.EventTicks.FailureLatchTick,
                    record.TerminalReason,
                    record.TerminalTick,
                    record.FailureResult.TerminalContextStatus);

                Assert.That(
                    record.FailureResult.TerminalContextStatus,
                    Is.EqualTo(SquatFailureTerminalContextStatus.TRACE_COVERED),
                    "The lifecycle must hand P3 a trace-covered terminal context.");
                Assert.That(record.TerminalTick, Is.LessThanOrEqualTo(record.LastTraceTick));
                Assert.That(record.FailureResult.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.EVALUABLE));
                Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
                Assert.That(record.FailureResult.HasFailure, Is.False);
                Assert.That(record.EventTicks.FailureOnsetTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
                Assert.That(record.EventTicks.FailureLatchTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
                Assert.That(record.Trace, Is.SameAs(_controller.ObservationTrace));

                summaries.Add(new AttemptSummary(
                    record.TerminalReason,
                    record.RuleOutcome,
                    record.Judgment.PrimaryViolationKind,
                    record.PhysicalFailureOutcome,
                    record.FailureResult.PrimaryFailureKind,
                    record.CommandTimeline.Count,
                    record.TraceSampleCount));

                yield return null;
            }

            for (int index = 1; index < summaries.Count; index++)
            {
                Assert.That(summaries[index].TerminalReason, Is.EqualTo(summaries[0].TerminalReason));
                Assert.That(summaries[index].RuleOutcome, Is.EqualTo(summaries[0].RuleOutcome));
                Assert.That(summaries[index].PrimaryRuleViolation, Is.EqualTo(summaries[0].PrimaryRuleViolation));
                Assert.That(summaries[index].PhysicalFailureOutcome, Is.EqualTo(summaries[0].PhysicalFailureOutcome));
                Assert.That(summaries[index].PrimaryFailureKind, Is.EqualTo(summaries[0].PrimaryFailureKind));
                Assert.That(summaries[index].CommandCount, Is.EqualTo(summaries[0].CommandCount));
                Assert.That(summaries[index].TraceSampleCount, Is.EqualTo(summaries[0].TraceSampleCount));
            }
        }

        private static string FormatViolations(SquatAttemptJudgment judgment)
        {
            if (judgment.Violations.Count == 0)
                return "NONE";

            string result = string.Empty;
            for (int index = 0; index < judgment.Violations.Count; index++)
            {
                if (index > 0)
                    result += ",";
                result += judgment.Violations[index].Kind.ToString();
            }
            return result;
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            Assert.That(_controller.AttemptOrchestrator, Is.Not.Null);
            Assert.That(_controller.ObservationCollector, Is.Not.Null);
        }

        private void CaptureUnexpectedError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error || condition.StartsWith("connection.state_change", System.StringComparison.Ordinal))
                return;

            _unexpectedErrors.Add(condition);
        }

        private sealed class AttemptSummary
        {
            public AttemptSummary(
                SquatAttemptTerminalReason terminalReason,
                SquatJudgmentOutcome ruleOutcome,
                SquatRuleViolationKind primaryRuleViolation,
                SquatFailureResultKind physicalFailureOutcome,
                SquatFailureKind primaryFailureKind,
                int commandCount,
                int traceSampleCount)
            {
                TerminalReason = terminalReason;
                RuleOutcome = ruleOutcome;
                PrimaryRuleViolation = primaryRuleViolation;
                PhysicalFailureOutcome = physicalFailureOutcome;
                PrimaryFailureKind = primaryFailureKind;
                CommandCount = commandCount;
                TraceSampleCount = traceSampleCount;
            }

            public SquatAttemptTerminalReason TerminalReason { get; }
            public SquatJudgmentOutcome RuleOutcome { get; }
            public SquatRuleViolationKind PrimaryRuleViolation { get; }
            public SquatFailureResultKind PhysicalFailureOutcome { get; }
            public SquatFailureKind PrimaryFailureKind { get; }
            public int CommandCount { get; }
            public int TraceSampleCount { get; }
        }
    }
}
