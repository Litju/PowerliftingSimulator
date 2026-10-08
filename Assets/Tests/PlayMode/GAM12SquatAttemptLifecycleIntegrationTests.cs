using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
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
        private InputTestFixture _inputTestFixture;
        private Keyboard _testKeyboard;
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
            if (_controller != null)
                _controller.enabled = false;
            if (_bootstrap != null)
                _bootstrap.enabled = false;

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
            if (_inputTestFixture != null)
            {
                _inputTestFixture.TearDown();
                _inputTestFixture = null;
                _testKeyboard = null;
            }
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

                FoundationRuntime runtime = _bootstrap.Runtime;
                yield return CompleteAttempt(runtime);

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

        [UnityTest]
        public IEnumerator PLAYER_HUD_ACTIONS_USE_EXISTING_LOAD_START_AND_RESET_PATHS()
        {
            yield return LoadQualificationScene();

            _controller.SelectLoadFromPlayerUi(0f);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(0f));
            _controller.SelectLoadFromPlayerUi(25f);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));
            _controller.SelectLoadFromPlayerUi(105f);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(105f));

            _controller.SelectLoadFromPlayerUi(25f);
            _controller.StartAttemptFromPlayerUi();
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
            Assert.That(_controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.START_WINDOW));

            System.Reflection.FieldInfo debugField = typeof(SquatPhysicalPrototypeController).GetField(
                "showDebugGui",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.That(debugField, Is.Not.Null);
            Assert.That((bool)debugField.GetValue(_controller), Is.False);
        }

        [UnityTest]
        public IEnumerator YIELD_BEFORE_F_DOES_NOT_BEGIN_PLAYER_SQUAT_MOTION()
        {
            SetupKeyboardInput();
            yield return LoadQualificationScene();

            PressKeyboardButton(_testKeyboard.sKey);
            for (int frame = 0; frame < 12; frame++)
                yield return null;
            ReleaseKeyboardButton(_testKeyboard.sKey);
            yield return null;

            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.False);
            Assert.That(_controller.AttemptOrchestrator.HasSquatCommand, Is.False);
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.SETUP));
            Assert.That(_controller.Adapter.Sq, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator F_KEY_TWICE_IS_IDEMPOTENT_AT_PLAYER_INPUT_LAYER()
        {
            SetupKeyboardInput();
            yield return LoadQualificationScene();

            yield return TapKeyboardControl(_testKeyboard.fKey);
            SquatAttemptOrchestrator orchestrator = _controller.AttemptOrchestrator;
            Assert.That(orchestrator.HasStarted, Is.True);
            yield return TapKeyboardControl(_testKeyboard.fKey);

            Assert.That(_controller.AttemptOrchestrator, Is.SameAs(orchestrator));
            Assert.That(orchestrator.HasStarted, Is.True);
        }

        [UnityTest]
        public IEnumerator PLAYER_F_YIELD_AND_DRIVE_KEYS_ADVANCE_SQUAT_PHASES()
        {
            SetupKeyboardInput();
            yield return LoadQualificationScene();
            System.Reflection.FieldInfo inputActionsField = typeof(FoundationBootstrap).GetField(
                "inputActions",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            InputActionAsset actions = inputActionsField?.GetValue(_bootstrap) as InputActionAsset;
            Assert.That(actions, Is.Not.Null, "The gameplay InputActionAsset is not wired in the scene.");
            InputAction yieldAction = actions.FindActionMap("Gameplay", true).FindAction("Yield", true);
            InputAction driveAction = actions.FindActionMap("Gameplay", true).FindAction("Drive", true);
            Assert.That(yieldAction.controls.Count, Is.GreaterThan(0), "Yield has no resolved controls.");
            Assert.That(driveAction.controls.Count, Is.GreaterThan(0), "Drive has no resolved controls.");

            yield return TapKeyboardControl(_testKeyboard.digit2Key);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));
            yield return TapKeyboardControl(_testKeyboard.fKey);
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
            PressKeyboardButton(_testKeyboard.sKey);
            Assert.That(yieldAction.enabled, Is.True);
            for (int frame = 0; frame < MaximumQualificationTicks &&
                !_controller.AttemptOrchestrator.HasSquatCommand; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(
                _controller.AttemptOrchestrator.HasSquatCommand,
                Is.True,
                "The F key did not progress through the start window; lifecycle=" +
                _controller.AttemptLifecycle.State + ", tick=" + _bootstrap.Runtime.CurrentTime.Tick);

            for (int frame = 0; frame < 600 && _controller.Adapter.State != SquatState.BOTTOM; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.BOTTOM));
            Assert.That(_controller.Adapter.Sq, Is.EqualTo(1f));

            ReleaseKeyboardButton(_testKeyboard.sKey);
            yield return new WaitForSecondsRealtime(0.02f);
            PressKeyboardButton(_testKeyboard.wKey);
            yield return null;
            Assert.That(driveAction.enabled, Is.True);
            Assert.That(driveAction.ReadValue<float>(), Is.GreaterThan(0.5f), "W must bind to Drive.");
            for (int frame = 0; frame < 200 &&
                (_controller.Adapter.State != SquatState.ASCENT || _controller.Adapter.Sq >= 1f); frame++)
                yield return new WaitForSecondsRealtime(0.01f);

            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.ASCENT));
            Assert.That(_controller.Adapter.Sq, Is.LessThan(1f));

            for (int frame = 0; frame < MaximumQualificationTicks && _controller.AttemptRecord == null; frame++)
                yield return new WaitForSecondsRealtime(0.01f);

            SquatAttemptRecord record = _controller.AttemptRecord;
            Assert.That(
                record,
                Is.Not.Null,
                "The real Input System attempt did not finalize within the bounded tick budget. lifecycle=" +
                _controller.AttemptLifecycle.State + ", adapterState=" + _controller.Adapter.State +
                ", traceCount=" + _controller.ObservationTrace.Count);
            Assert.That(record.Trace.IsFrozen, Is.True);
            Assert.That(record.Trace.IsTruthSealed, Is.True);
            Assert.That(record.Judgment.EvidenceStatus, Is.EqualTo(SquatJudgmentEvidenceStatus.EVALUABLE));
            Assert.That(record.FailureResult.EvidenceStatus, Is.EqualTo(SquatFailureEvidenceStatus.EVALUABLE));
            Assert.That(record.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.PHYSICAL_LOCKOUT));
            Assert.That(record.EventTicks.LockoutTick, Is.Not.EqualTo(SquatAttemptEventTicks.NotAvailable));
            Assert.That(record.CommandTimeline.Count, Is.EqualTo(3));
            Assert.That(record.CommandTimelineCovered, Is.True);

            // This physical-input fixture may produce either evaluable rule
            // verdict; NO_LIFT is accepted only when it carries a real violation.
            Assert.That(
                record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT ||
                record.RuleOutcome == SquatJudgmentOutcome.NO_LIFT,
                Is.True);
            if (record.RuleOutcome == SquatJudgmentOutcome.NO_LIFT)
                Assert.That(record.Judgment.PrimaryViolationKind, Is.Not.EqualTo(SquatRuleViolationKind.NONE));
            Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));
            Assert.That(record.FailureResult.HasFailure, Is.False);

            Assert.That(record.IsTruthFrozen, Is.True);
            string playerResult = SquatPhysicalPrototypeController.FormatPlayerResult(record);
            string normalizedResult = playerResult.ToUpperInvariant();
            if (record.Judgment.EvidenceStatus != SquatJudgmentEvidenceStatus.EVALUABLE)
                StringAssert.Contains(record.Judgment.EvidenceStatus.ToString().Replace('_', ' '), normalizedResult);
            else if (record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT)
                StringAssert.Contains("GOOD LIFT", normalizedResult);
            else
            {
                StringAssert.Contains("NO LIFT", normalizedResult);
                if (record.Judgment.PrimaryViolationKind != SquatRuleViolationKind.NONE)
                    StringAssert.Contains(record.Judgment.PrimaryViolationKind.ToString().Replace('_', ' '), normalizedResult);
            }

            if (record.PhysicalFailureOutcome == SquatFailureResultKind.PHYSICAL_FAILURE)
            {
                StringAssert.Contains("PHYSICAL FAILURE", normalizedResult);
                StringAssert.Contains(record.FailureResult.PrimaryFailureKind.ToString().Replace('_', ' '), normalizedResult);
            }
            else if (record.FailureResult.EvidenceStatus != SquatFailureEvidenceStatus.EVALUABLE)
                StringAssert.Contains(record.FailureResult.EvidenceStatus.ToString().Replace('_', ' '), normalizedResult);
        }

        [UnityTest]
        public IEnumerator LOAD_KEYS_AFTER_F_ARE_IGNORED()
        {
            SetupKeyboardInput();
            yield return LoadQualificationScene();
            float loadKg = _controller.CurrentLoadKg;

            yield return TapKeyboardControl(_testKeyboard.fKey);
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
            yield return TapKeyboardControl(_testKeyboard.digit1Key);
            yield return TapKeyboardControl(_testKeyboard.digit2Key);
            yield return TapKeyboardControl(_testKeyboard.digit3Key);

            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(loadKg));
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
        }

        [UnityTest]
        public IEnumerator SCENE_RELOAD_RETRY_STARTS_A_FRESH_ATTEMPT_EPOCH()
        {
            SetupKeyboardInput();
            yield return LoadQualificationScene();
            yield return TapKeyboardControl(_testKeyboard.digit2Key);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));

            FoundationRuntime firstRuntime = _bootstrap.Runtime;
            IntentBuffer firstInputBuffer = firstRuntime.InputBuffer;
            InputTimeDomain firstInputTimeDomain = firstRuntime.InputTimeDomain;
            SquatTrace firstTrace = _controller.ObservationTrace;
            yield return TapKeyboardControl(_testKeyboard.fKey);
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
            Assert.That(_controller.AttemptLifecycle.State, Is.Not.EqualTo(SquatAttemptLifecycleState.IDLE));
            _controller.enabled = false;
            _bootstrap.enabled = false;
            yield return CompleteAttempt(firstRuntime);

            SquatAttemptRecord firstRecord = _controller.AttemptRecord;
            Assert.That(firstRecord, Is.Not.Null);
            Assert.That(_controller.AttemptLifecycle.Record, Is.SameAs(firstRecord));
            Assert.That(firstRecord.Trace, Is.SameAs(firstTrace));
            Assert.That(firstRecord.Trace.IsTruthSealed, Is.True);

            _controller.RetryFromPlayerUi();
            yield return WaitForLoadedQualificationScene();
            yield return TapKeyboardControl(_testKeyboard.digit2Key);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));

            FoundationRuntime retryRuntime = _bootstrap.Runtime;
            Assert.That(retryRuntime, Is.Not.SameAs(firstRuntime));
            Assert.That(retryRuntime.InputBuffer, Is.Not.SameAs(firstInputBuffer));
            Assert.That(retryRuntime.InputTimeDomain, Is.Not.SameAs(firstInputTimeDomain));
            Assert.That(_controller.ObservationTrace, Is.Not.SameAs(firstTrace));
            Assert.That(_controller.ObservationTrace.Count, Is.EqualTo(0));
            Assert.That(_controller.AttemptRecord, Is.Null);
            Assert.That(_controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));

            yield return TapKeyboardControl(_testKeyboard.fKey);
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
            Assert.That(_controller.AttemptLifecycle.State, Is.Not.EqualTo(SquatAttemptLifecycleState.IDLE));
            Assert.That(_controller.AttemptRecord, Is.Null);
            _controller.enabled = false;
            _bootstrap.enabled = false;
        }

        private IEnumerator TapKeyboardControl(ButtonControl control)
        {
            PressKeyboardButton(control);
            yield return null;
            ReleaseKeyboardButton(control);
            yield return null;
        }

        private void PressKeyboardButton(ButtonControl control)
        {
            _inputTestFixture.currentTime = Time.realtimeSinceStartupAsDouble;
            _inputTestFixture.Press(control);
        }

        private void ReleaseKeyboardButton(ButtonControl control)
        {
            _inputTestFixture.currentTime = Time.realtimeSinceStartupAsDouble;
            _inputTestFixture.Release(control);
        }

        private void SetupKeyboardInput()
        {
            _inputTestFixture = new InputTestFixture();
            _inputTestFixture.Setup();
            _testKeyboard = InputSystem.AddDevice<Keyboard>();
        }

        private IEnumerator CompleteAttempt(FoundationRuntime runtime)
        {
            bool driveRequested = false;
            int ticks = 0;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            while (_controller.AttemptRecord == null && ticks < MaximumQualificationTicks)
            {
                // This test owns stepping while the production Update methods are disabled.
                if (_controller.AttemptOrchestrator.HasSquatCommand)
                {
                    SquatState state = _controller.Adapter.State;
                    if (state == SquatState.BOTTOM || state == SquatState.REVERSAL ||
                        state == SquatState.ASCENT || state == SquatState.STICKING)
                        driveRequested = true;

                    double inputTime = runtime.CurrentTime.SimulationTimeSeconds +
                        0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                    runtime.InputBuffer.SetContinuous(IntentAction.Yield, driveRequested ? 0f : 1f, inputTime);
                    runtime.InputBuffer.SetContinuous(IntentAction.Drive, driveRequested ? 1f : 0f, inputTime);
                }

                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                _controller.LeftFootContact?.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                _controller.RightFootContact?.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                ticks++;
                if (ticks % 40 == 0)
                    yield return null;
            }

            Assert.That(_controller.AttemptRecord, Is.Not.Null, "The attempt did not finalize within its tick budget.");
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
            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The first build scene must be the squat gameplay entry point.");
            while (!load.isDone)
                yield return null;
            yield return WaitForLoadedQualificationScene();
        }

        private IEnumerator WaitForLoadedQualificationScene()
        {
            yield return null;

            _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(QualificationScene));
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
            if (type != LogType.Error && type != LogType.Exception)
                return;
            if (type == LogType.Error && condition.StartsWith("connection.state_change", System.StringComparison.Ordinal))
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
