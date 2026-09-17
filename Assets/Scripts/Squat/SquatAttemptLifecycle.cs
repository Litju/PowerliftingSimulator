using System;
using System.Collections.Generic;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public enum SquatAttemptLifecycleState : byte
    {
        IDLE,
        START_WINDOW,
        SQUAT_COMMAND_ISSUED,
        ACTIVE_ATTEMPT,
        PHYSICAL_TERMINAL_CONDITION,
        RACK_COMMAND_ISSUED,
        RERACK_STARTED,
        TRACE_FROZEN,
        RULES_EVALUATED,
        FAILURE_EVALUATED,
        ATTEMPT_TRUTH_FROZEN,
        COMPLETE
    }

    public enum SquatAttemptTerminalReason : byte
    {
        NONE,
        PHYSICAL_LOCKOUT,
        PHYSICAL_FAILURE,
        TIMEOUT,
        ABORTED,
        LIFECYCLE_FAULT
    }

    /// <summary>
    /// Authoritative event ticks for one frozen squat attempt. Missing events
    /// use ulong.MaxValue and are never represented as tick zero.
    /// </summary>
    public readonly struct SquatAttemptEventTicks
    {
        public const ulong NotAvailable = ulong.MaxValue;

        public SquatAttemptEventTicks(
            ulong startWindowBeginTick,
            ulong startWindowEndTick,
            ulong squatCommandTick,
            ulong physicalDescentOnsetTick,
            ulong bottomTick,
            ulong ascentEstablishmentTick,
            ulong lockoutTick,
            ulong rackCommandTick,
            ulong rerackStartedTick,
            ulong failureOnsetTick,
            ulong failureLatchTick,
            ulong truthFreezeTick)
        {
            StartWindowBeginTick = startWindowBeginTick;
            StartWindowEndTick = startWindowEndTick;
            SquatCommandTick = squatCommandTick;
            PhysicalDescentOnsetTick = physicalDescentOnsetTick;
            BottomTick = bottomTick;
            AscentEstablishmentTick = ascentEstablishmentTick;
            LockoutTick = lockoutTick;
            RackCommandTick = rackCommandTick;
            RerackStartedTick = rerackStartedTick;
            FailureOnsetTick = failureOnsetTick;
            FailureLatchTick = failureLatchTick;
            TruthFreezeTick = truthFreezeTick;
        }

        public ulong StartWindowBeginTick { get; }
        public ulong StartWindowEndTick { get; }
        public ulong SquatCommandTick { get; }
        public ulong PhysicalDescentOnsetTick { get; }
        public ulong BottomTick { get; }
        public ulong AscentEstablishmentTick { get; }
        public ulong LockoutTick { get; }
        public ulong RackCommandTick { get; }
        public ulong RerackStartedTick { get; }
        public ulong FailureOnsetTick { get; }
        public ulong FailureLatchTick { get; }
        public ulong TruthFreezeTick { get; }
    }

    public readonly struct SquatAttemptQualityMetadata
    {
        public const string DefaultClaimClassification = "GAME_SIMULATION_ATTEMPT_TRUTH";

        public SquatAttemptQualityMetadata(
            SquatJudgmentEvidenceStatus ruleEvidenceStatus,
            SquatFailureEvidenceStatus failureEvidenceStatus,
            string claimClassification,
            string qualityNote)
        {
            if (string.IsNullOrEmpty(claimClassification))
                throw new ArgumentException("Attempt truth claim classification is required.", nameof(claimClassification));
            if (string.IsNullOrEmpty(qualityNote))
                throw new ArgumentException("Attempt truth quality metadata is required.", nameof(qualityNote));

            RuleEvidenceStatus = ruleEvidenceStatus;
            FailureEvidenceStatus = failureEvidenceStatus;
            ClaimClassification = claimClassification;
            QualityNote = qualityNote;
        }

        public SquatJudgmentEvidenceStatus RuleEvidenceStatus { get; }
        public SquatFailureEvidenceStatus FailureEvidenceStatus { get; }
        public string ClaimClassification { get; }
        public string QualityNote { get; }
        public bool HasCompleteRuleEvidence => RuleEvidenceStatus == SquatJudgmentEvidenceStatus.EVALUABLE;
        public bool HasCompleteFailureEvidence => FailureEvidenceStatus == SquatFailureEvidenceStatus.EVALUABLE;
    }

    /// <summary>
    /// Immutable final truth record. The referenced trace is sealed by the
    /// lifecycle before this record is exposed, so presentation cannot clear,
    /// append, or rewrite the evidence behind the record.
    /// </summary>
    public sealed class SquatAttemptRecord
    {
        public const string DefaultAttemptLifecycleVersion = "GAM12_P4_ATTEMPT_LIFECYCLE_V1";
        public const string DefaultAttemptRecordVersion = "GAM12_P4_ATTEMPT_RECORD_V1";

        internal SquatAttemptRecord(
            SquatTrace trace,
            SquatRuleCommandTimeline commandTimeline,
            SquatAttemptEventTicks eventTicks,
            SquatAttemptTerminalReason terminalReason,
            ulong terminalTick,
            double terminalTimeSeconds,
            SquatAttemptQualityMetadata quality,
            SquatAttemptJudgment judgment,
            SquatFailureResult failureResult,
            ulong traceFreezeTick,
            double traceFreezeTimeSeconds,
            ulong truthFreezeTick,
            double truthFreezeTimeSeconds,
            bool commandTimelineCovered)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (!trace.IsFrozen || trace.IsRecording || !trace.IsTruthSealed)
                throw new InvalidOperationException("An attempt record requires a sealed frozen trace.");
            if (commandTimeline == null)
                throw new ArgumentNullException(nameof(commandTimeline));
            if (judgment == null)
                throw new ArgumentNullException(nameof(judgment));
            if (failureResult == null)
                throw new ArgumentNullException(nameof(failureResult));
            if (trace.Count == 0)
                throw new ArgumentException("An attempt record requires at least one trace sample.", nameof(trace));
            RequireTime(traceFreezeTimeSeconds, nameof(traceFreezeTimeSeconds));
            RequireTime(truthFreezeTimeSeconds, nameof(truthFreezeTimeSeconds));

            Trace = trace;
            CommandTimeline = commandTimeline;
            EventTicks = eventTicks;
            TerminalReason = terminalReason;
            TerminalTick = terminalTick;
            TerminalTimeSeconds = terminalTimeSeconds;
            Quality = quality;
            Judgment = judgment;
            FailureResult = failureResult;
            TraceFreezeTick = traceFreezeTick;
            TraceFreezeTimeSeconds = traceFreezeTimeSeconds;
            TruthFreezeTick = truthFreezeTick;
            TruthFreezeTimeSeconds = truthFreezeTimeSeconds;
            CommandTimelineCovered = commandTimelineCovered;
        }

        public SquatTrace Trace { get; }
        public SquatTrace FrozenTrace => Trace;
        public SquatRuleCommandTimeline CommandTimeline { get; }
        public SquatAttemptEventTicks EventTicks { get; }
        public SquatAttemptTerminalReason TerminalReason { get; }

        /// <summary>
        /// Authoritative terminal sample of this attempt. It is the latch tick
        /// of a terminal FAILED_LOCKOUT postcondition, and lifecycle evidence
        /// only - it never selects a physical failure class.
        /// </summary>
        public ulong TerminalTick { get; }
        public double TerminalTimeSeconds { get; }
        public SquatAttemptQualityMetadata Quality { get; }
        public SquatAttemptQualityMetadata ClaimQuality => Quality;
        public SquatAttemptJudgment Judgment { get; }
        public SquatFailureResult FailureResult { get; }

        public string AttemptLifecycleVersion => AttemptLifecycleVersionValue;
        public string AttemptRecordVersion => AttemptRecordVersionValue;
        public ulong StartWindowBeginTick => EventTicks.StartWindowBeginTick;
        public ulong StartWindowEndTick => EventTicks.StartWindowEndTick;
        public string ObservationSchema => SquatObservationSnapshot.SchemaId;
        public string ObservationProvenanceVersion => SquatObservationSnapshot.ProvenanceVersion;
        public string TraceSchema => Trace.Schema;
        public int TraceSampleCount => Trace.Count;
        public ulong FirstTraceTick => Trace[0].SimulationTick;
        public ulong LastTraceTick => Trace[Trace.Count - 1].SimulationTick;
        public double FirstTraceTimeSeconds => Trace[0].SimulationTimeSeconds;
        public double LastTraceTimeSeconds => Trace[Trace.Count - 1].SimulationTimeSeconds;
        public ulong TraceFreezeTick { get; }
        public double TraceFreezeTimeSeconds { get; }
        public ulong TruthFreezeTick { get; }
        public double TruthFreezeTimeSeconds { get; }
        public bool CommandTimelineCovered { get; }
        public bool IsTruthFrozen => true;
        public bool SafetyHandoffEligible => IsTruthFrozen;

        public string RuleSetId => Judgment.RuleSetId;
        public string RuleImplementationVersion => Judgment.RuleImplementationVersion;
        public string RuleToleranceVersion => Judgment.ToleranceVersion;
        public string FailureModelVersion => FailureResult.FailureModelVersion;
        public string FailureCalibrationVersion => FailureResult.CalibrationVersion;
        public string FailurePrecedenceVersion => FailureResult.PrecedenceVersion;
        public SquatJudgmentOutcome RuleOutcome => Judgment.Outcome;
        public SquatFailureResultKind PhysicalFailureOutcome => FailureResult.Outcome;
        public string ClaimClassification => Quality.ClaimClassification;

        private const string AttemptLifecycleVersionValue = DefaultAttemptLifecycleVersion;
        private const string AttemptRecordVersionValue = DefaultAttemptRecordVersion;

        private static void RequireTime(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>
    /// Pure lifecycle authority for one squat attempt. It owns command
    /// chronology and finalization order, while SquatState remains physical
    /// control context owned by the adapter.
    /// </summary>
    public sealed class SquatAttemptLifecycle
    {
        private readonly List<SquatRuleCommandEvent> _commands = new List<SquatRuleCommandEvent>(3);
        private SquatAttemptLifecycleState _state = SquatAttemptLifecycleState.IDLE;
        private SquatAttemptTerminalReason _terminalReason;
        private ulong _startWindowBeginTick = SquatAttemptEventTicks.NotAvailable;
        private ulong _startWindowEndTick = SquatAttemptEventTicks.NotAvailable;
        private ulong _terminalTick = SquatAttemptEventTicks.NotAvailable;
        private double _terminalTimeSeconds;
        private bool _hasStartWindow;
        private bool _hasSquatCommand;
        private bool _hasRackCommand;
        private bool _hasRerackStarted;
        private SquatRuleCommandEvent _squatCommand;
        private SquatRuleCommandEvent _rackCommand;
        private SquatRuleCommandEvent _rerackStarted;
        private SquatTrace _frozenTrace;
        private SquatAttemptRecord _record;

        public SquatAttemptLifecycleState State => _state;
        public SquatAttemptTerminalReason TerminalReason => _terminalReason;
        public SquatAttemptRecord Record => _record;
        public bool SafetyHandoffEligible => _record != null && _record.IsTruthFrozen;
        public bool HasStartWindow => _hasStartWindow;
        public ulong StartWindowBeginTick => _startWindowBeginTick;
        public ulong StartWindowEndTick => _startWindowEndTick;
        public int StartWindowSampleCount => !_hasStartWindow
            ? 0
            : checked((int)(_startWindowEndTick - _startWindowBeginTick + 1ul));

        public void BeginStartWindow()
        {
            RequireState(SquatAttemptLifecycleState.IDLE);
            _state = SquatAttemptLifecycleState.START_WINDOW;
        }

        public void ObserveStartWindowSample(ulong simulationTick)
        {
            RequireState(SquatAttemptLifecycleState.START_WINDOW);
            if (!_hasStartWindow)
            {
                _startWindowBeginTick = simulationTick;
                _startWindowEndTick = simulationTick;
                _hasStartWindow = true;
                return;
            }

            if (simulationTick != _startWindowEndTick + 1ul)
                throw new InvalidOperationException("Start-window samples must be contiguous simulation ticks.");
            _startWindowEndTick = simulationTick;
        }

        public SquatRuleCommandEvent IssueSquatCommand(ulong simulationTick, double simulationTimeSeconds)
        {
            RequireState(SquatAttemptLifecycleState.START_WINDOW);
            if (!_hasStartWindow)
                throw new InvalidOperationException("A qualified start window is required before Squat.");
            if (simulationTick != _startWindowEndTick + 1ul)
                throw new InvalidOperationException("Squat must be issued on the tick immediately after the start window.");

            _squatCommand = new SquatRuleCommandEvent(
                SquatRuleCommandKind.SquatCommandIssued,
                simulationTick,
                simulationTimeSeconds);
            _commands.Add(_squatCommand);
            _hasSquatCommand = true;
            _state = SquatAttemptLifecycleState.SQUAT_COMMAND_ISSUED;
            return _squatCommand;
        }

        public void BeginActiveAttempt()
        {
            RequireState(SquatAttemptLifecycleState.SQUAT_COMMAND_ISSUED);
            _state = SquatAttemptLifecycleState.ACTIVE_ATTEMPT;
        }

        public void MarkPhysicalTerminalCondition(
            ulong simulationTick,
            double simulationTimeSeconds,
            SquatAttemptTerminalReason terminalReason)
        {
            if (_state != SquatAttemptLifecycleState.SQUAT_COMMAND_ISSUED &&
                _state != SquatAttemptLifecycleState.ACTIVE_ATTEMPT)
                throw new InvalidOperationException("A physical terminal condition requires an active squat attempt.");
            if (terminalReason == SquatAttemptTerminalReason.NONE)
                throw new ArgumentOutOfRangeException(nameof(terminalReason));
            if (simulationTick < _squatCommand.SimulationTick)
                throw new ArgumentOutOfRangeException(nameof(simulationTick));

            RequireTime(simulationTimeSeconds, nameof(simulationTimeSeconds));
            _terminalTick = simulationTick;
            _terminalTimeSeconds = simulationTimeSeconds;
            _terminalReason = terminalReason;
            _state = SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION;
        }

        public void IssueRackCommand(ulong simulationTick, double simulationTimeSeconds)
        {
            RequireState(SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION);
            if (simulationTick <= _terminalTick)
                throw new InvalidOperationException("Rack must follow the physical terminal condition.");

            _rackCommand = new SquatRuleCommandEvent(
                SquatRuleCommandKind.RackCommandIssued,
                simulationTick,
                simulationTimeSeconds);
            _commands.Add(_rackCommand);
            _hasRackCommand = true;
            _state = SquatAttemptLifecycleState.RACK_COMMAND_ISSUED;
        }

        public void StartRerack(ulong simulationTick, double simulationTimeSeconds)
        {
            RequireState(SquatAttemptLifecycleState.RACK_COMMAND_ISSUED);
            if (simulationTick < _rackCommand.SimulationTick)
                throw new InvalidOperationException("Rerack cannot start before the Rack command.");

            _rerackStarted = new SquatRuleCommandEvent(
                SquatRuleCommandKind.RerackStarted,
                simulationTick,
                simulationTimeSeconds);
            _commands.Add(_rerackStarted);
            _hasRerackStarted = true;
            _state = SquatAttemptLifecycleState.RERACK_STARTED;
        }

        public void Terminate(
            ulong simulationTick,
            double simulationTimeSeconds,
            SquatAttemptTerminalReason terminalReason)
        {
            if (terminalReason != SquatAttemptTerminalReason.TIMEOUT &&
                terminalReason != SquatAttemptTerminalReason.ABORTED &&
                terminalReason != SquatAttemptTerminalReason.LIFECYCLE_FAULT &&
                terminalReason != SquatAttemptTerminalReason.PHYSICAL_FAILURE)
                throw new ArgumentOutOfRangeException(nameof(terminalReason));

            if (_state == SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION)
            {
                RequireTime(simulationTimeSeconds, nameof(simulationTimeSeconds));
                if (simulationTick < _terminalTick)
                    throw new ArgumentOutOfRangeException(nameof(simulationTick));
                _terminalTick = simulationTick;
                _terminalTimeSeconds = simulationTimeSeconds;
                _terminalReason = terminalReason;
                return;
            }

            MarkPhysicalTerminalCondition(simulationTick, simulationTimeSeconds, terminalReason);
        }

        public SquatAttemptRecord FinalizeAttempt(
            SquatTrace trace,
            SquatRuleProcessor ruleProcessor = null,
            SquatFailureDetector failureDetector = null)
        {
            if (_record != null)
            {
                if (!ReferenceEquals(trace, _frozenTrace))
                    throw new InvalidOperationException("A finalized lifecycle can only be read with its original trace.");
                return _record;
            }

            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (!trace.IsFrozen || trace.IsRecording || trace.Count == 0)
                throw new InvalidOperationException("Attempt truth requires a non-empty frozen trace before evaluation.");
            if (!_hasSquatCommand)
                throw new InvalidOperationException("Attempt truth requires an explicit Squat command event.");
            if (_state != SquatAttemptLifecycleState.RERACK_STARTED &&
                (_terminalReason == SquatAttemptTerminalReason.NONE ||
                 _terminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT))
                throw new InvalidOperationException("A complete attempt must record Rack and Rerack, or terminate explicitly.");

            _frozenTrace = trace;
            ulong traceFreezeTick = trace[trace.Count - 1].SimulationTick;
            double traceFreezeTime = trace[trace.Count - 1].SimulationTimeSeconds;
            _state = SquatAttemptLifecycleState.TRACE_FROZEN;

            SquatRuleCommandTimeline timeline = new SquatRuleCommandTimeline(_commands);
            SquatAttemptJudgment judgment = (ruleProcessor ?? new SquatRuleProcessor()).Evaluate(trace, timeline);
            _state = SquatAttemptLifecycleState.RULES_EVALUATED;
            // The lifecycle is the authoritative terminal-context source. P3
            // remains the physical-failure authority: terminality only allows
            // the FAILED_LOCKOUT postcondition to be decided, and never
            // selects a failure class.
            SquatFailureResult failure = (failureDetector ?? new SquatFailureDetector())
                .Evaluate(trace, BuildFailureCompletionContext());
            _state = SquatAttemptLifecycleState.FAILURE_EVALUATED;

            SquatAttemptEventTicks eventTicks = BuildEventTicks(judgment, failure, traceFreezeTick);
            SquatAttemptQualityMetadata quality = new SquatAttemptQualityMetadata(
                judgment.EvidenceStatus,
                failure.EvidenceStatus,
                SquatAttemptQualityMetadata.DefaultClaimClassification,
                "Raw post-physics evidence is frozen before independent rule and physical-failure outcomes are handed to later consumers.");

            trace.SealForAttemptTruth();
            _record = new SquatAttemptRecord(
                trace,
                timeline,
                eventTicks,
                _terminalReason,
                _terminalTick,
                _terminalTimeSeconds,
                quality,
                judgment,
                failure,
                traceFreezeTick,
                traceFreezeTime,
                traceFreezeTick,
                traceFreezeTime,
                CommandTimelineCovered(trace, timeline));
            _state = SquatAttemptLifecycleState.ATTEMPT_TRUTH_FROZEN;
            _state = SquatAttemptLifecycleState.COMPLETE;
            return _record;
        }

        public SquatAttemptRecord Finalize(
            SquatTrace trace,
            SquatRuleProcessor ruleProcessor = null,
            SquatFailureDetector failureDetector = null) =>
            FinalizeAttempt(trace, ruleProcessor, failureDetector);

        /// <summary>
        /// Immutable terminal evidence for the physical failure detector. An
        /// attempt without an explicit terminal event stays non-terminal, so no
        /// terminal postcondition can be decided from it.
        /// </summary>
        private SquatFailureCompletionContext BuildFailureCompletionContext()
        {
            if (_terminalTick == SquatAttemptEventTicks.NotAvailable ||
                _terminalReason == SquatAttemptTerminalReason.NONE)
                return SquatFailureCompletionContext.NonTerminal;

            return SquatFailureCompletionContext.Terminal(
                _terminalTick,
                _terminalTimeSeconds,
                _terminalReason);
        }

        private SquatAttemptEventTicks BuildEventTicks(
            SquatAttemptJudgment judgment,
            SquatFailureResult failure,
            ulong truthFreezeTick)
        {
            ulong failureOnset = failure.FailureRecord == null
                ? SquatAttemptEventTicks.NotAvailable
                : failure.FailureRecord.OnsetTick;
            ulong failureLatch = failure.FailureRecord == null
                ? SquatAttemptEventTicks.NotAvailable
                : failure.FailureRecord.LatchedTick;
            SquatRuleEventTicks ruleTicks = judgment.EventTicks;
            return new SquatAttemptEventTicks(
                _startWindowBeginTick,
                _startWindowEndTick,
                _hasSquatCommand ? _squatCommand.SimulationTick : SquatAttemptEventTicks.NotAvailable,
                ruleTicks.DescentOnsetTick,
                ruleTicks.BottomTick,
                ruleTicks.AscentEstablishmentTick,
                ruleTicks.LockoutTick,
                _hasRackCommand ? _rackCommand.SimulationTick : SquatAttemptEventTicks.NotAvailable,
                _hasRerackStarted ? _rerackStarted.SimulationTick : SquatAttemptEventTicks.NotAvailable,
                failureOnset,
                failureLatch,
                truthFreezeTick);
        }

        private static bool CommandTimelineCovered(
            SquatTrace trace,
            SquatRuleCommandTimeline timeline)
        {
            if (!timeline.Has(SquatRuleCommandKind.SquatCommandIssued) ||
                !timeline.Has(SquatRuleCommandKind.RackCommandIssued) ||
                !timeline.Has(SquatRuleCommandKind.RerackStarted))
                return false;

            for (int index = 0; index < timeline.Count; index++)
            {
                SquatRuleCommandEvent command = timeline.Events[index];
                if (command.SimulationTick < trace[0].SimulationTick ||
                    command.SimulationTick > trace[trace.Count - 1].SimulationTick)
                    return false;
                int sampleIndex = FindIndexAtTick(trace, command.SimulationTick);
                if (sampleIndex < 0)
                    return false;
                SquatObservationSnapshot sample = trace[sampleIndex];
                if (Math.Abs(command.SimulationTimeSeconds - sample.SimulationTimeSeconds) >
                    FoundationTolerances.SimulationTimeMapping)
                    return false;
            }

            return true;
        }

        private static int FindIndexAtTick(SquatTrace trace, ulong tick)
        {
            for (int index = 0; index < trace.Count; index++)
            {
                if (trace[index].SimulationTick == tick)
                    return index;
            }

            return -1;
        }

        private void RequireState(SquatAttemptLifecycleState expected)
        {
            if (_state != expected)
                throw new InvalidOperationException(
                    "The squat attempt lifecycle expected state " + expected + " but was " + _state + ".");
        }

        private static void RequireTime(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>
    /// Minimal physical terminal evidence seam used by the lifecycle bridge.
    /// P3 remains the final physical-failure authority; this only identifies
    /// a bounded lockout condition so the bridge can issue Rack explicitly.
    /// </summary>
    public static class SquatAttemptPhysicalEvidence
    {
        public static bool IsLockout(
            SquatObservationSnapshot snapshot,
            SquatObservationSnapshot standingReference,
            SquatFailureCalibration calibration = null)
        {
            SquatFailureCalibration values = calibration ?? SquatFailureCalibration.Default;
            if (!snapshot.Bar.IsAvailable || !standingReference.Bar.IsAvailable ||
                snapshot.Bar.PositionWorldMeters.Y < standingReference.Bar.PositionWorldMeters.Y - values.LockoutHeightToleranceM ||
                snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length > values.LockoutBarStillVelocityMps ||
                snapshot.Bar.AngularVelocityBarRadiansPerSecond.Length > values.LockoutBarStillAngularVelocityRadS)
                return false;
            if (snapshot.Joints.LeftKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.LeftHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE)
                return false;
            if (MaxKneeAngle(snapshot) > values.LockoutKneeToleranceRadians ||
                MaxHipAngle(snapshot) > values.LockoutHipToleranceRadians)
                return false;

            return TryGetMaxTrunkAngle(snapshot, out float trunkAngle) &&
                trunkAngle <= values.LockoutTrunkToleranceRadians;
        }

        private static float MaxKneeAngle(SquatObservationSnapshot snapshot) => Math.Max(
            Math.Abs(snapshot.Joints.LeftKnee.ActualAngleRadians),
            Math.Abs(snapshot.Joints.RightKnee.ActualAngleRadians));

        private static float MaxHipAngle(SquatObservationSnapshot snapshot) => Math.Max(
            Math.Abs(snapshot.Joints.LeftHip.ActualAngleRadians),
            Math.Abs(snapshot.Joints.RightHip.ActualAngleRadians));

        private static bool TryGetMaxTrunkAngle(SquatObservationSnapshot snapshot, out float angle)
        {
            bool has = false;
            angle = 0f;
            if (snapshot.TrunkAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                angle = Math.Abs(snapshot.TrunkWorldPitchRadians);
                has = true;
            }
            if (snapshot.Joints.Abdomen.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                angle = Math.Max(angle, Math.Abs(snapshot.Joints.Abdomen.ActualAngleRadians));
                has = true;
            }
            if (snapshot.Joints.Thorax.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                angle = Math.Max(angle, Math.Abs(snapshot.Joints.Thorax.ActualAngleRadians));
                has = true;
            }
            return has;
        }
    }
}
