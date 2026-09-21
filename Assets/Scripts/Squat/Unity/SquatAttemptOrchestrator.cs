using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Unity bridge for the value-only squat attempt lifecycle. It receives
    /// already-copied post-physics snapshots, owns explicit command events,
    /// and invokes the sealed processors only after the trace is frozen.
    /// </summary>
    public sealed class SquatAttemptOrchestrator
    {
        public const int DefaultMaximumAttemptTicks = 1500;

        private readonly SquatObservationCollector _collector;
        private readonly SquatPhysicalAdapter _adapter;
        private readonly SquatRuleToleranceSet _ruleTolerances;
        private readonly SquatFailureCalibration _failureCalibration;
        private readonly int _maximumAttemptTicks;
        private readonly int _requiredStartSamples;
        private readonly int _requiredLockoutSamples;
        private readonly SquatAttemptLifecycle _lifecycle = new SquatAttemptLifecycle();
        private readonly List<SquatStartPredicateDiagnostic> _startWindowDiagnostics =
            new List<SquatStartPredicateDiagnostic>();
        private readonly ReadOnlyCollection<SquatStartPredicateDiagnostic> _readOnlyStartWindowDiagnostics;

        private bool _started;
        private bool _recordingStarted;
        private int _stableCandidateRun;
        private int _startPredicateRun;
        private int _startWindowSamples;
        private int _lockoutSamples;
        private ulong _squatCommandTick;
        private SquatObservationSnapshot _standingReference;
        private bool _hasStandingReference;
        private SquatAttemptRecord _record;
        private bool _hasPhysicalDescent;
        private bool _hasAscent;
        private float _lowestBarPositionY = float.PositiveInfinity;

        public SquatAttemptOrchestrator(
            SquatObservationCollector collector,
            SquatPhysicalAdapter adapter,
            SquatRuleToleranceSet ruleTolerances = null,
            SquatFailureCalibration failureCalibration = null,
            int maximumAttemptTicks = DefaultMaximumAttemptTicks)
        {
            _collector = collector ?? throw new ArgumentNullException(nameof(collector));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _ruleTolerances = ruleTolerances ?? SquatRuleToleranceSet.Default;
            _failureCalibration = failureCalibration ?? SquatFailureCalibration.Default;
            if (maximumAttemptTicks <= 0)
                throw new ArgumentOutOfRangeException(nameof(maximumAttemptTicks));

            _maximumAttemptTicks = maximumAttemptTicks;
            _requiredStartSamples = _ruleTolerances.StartPositionPersistenceTicks;
            _requiredLockoutSamples = _ruleTolerances.FinalPositionPersistenceTicks;
            _readOnlyStartWindowDiagnostics = _startWindowDiagnostics.AsReadOnly();
            _collector.RegisterSnapshotObserver(HandleSnapshot);
        }

        public SquatAttemptLifecycle Lifecycle => _lifecycle;
        public SquatAttemptRecord Record => _record;
        public bool HasStarted => _started;
        public bool IsRecording => _recordingStarted && _collector.IsRecording;
        public bool IsTruthFrozen => _record != null;
        public int MaximumAttemptTicks => _maximumAttemptTicks;
        public int RequiredStartSamples => _requiredStartSamples;
        public int RequiredLockoutSamples => _requiredLockoutSamples;
        public IReadOnlyList<SquatStartPredicateDiagnostic> StartWindowDiagnostics => _readOnlyStartWindowDiagnostics;

        /// <summary>
        /// Arms one attempt. The trace does not begin until a qualified
        /// stable physical start candidate has persisted; earlier setup and
        /// walkout observations are intentionally excluded.
        /// </summary>
        public void BeginAttempt()
        {
            if (_started)
                throw new InvalidOperationException("The squat attempt lifecycle has already started.");
            if (_collector.IsRecording)
                throw new InvalidOperationException("A squat trace is already recording outside this lifecycle.");

            _lifecycle.BeginStartWindow();
            _started = true;
            _recordingStarted = false;
            _stableCandidateRun = 0;
            _startPredicateRun = 0;
            _startWindowDiagnostics.Clear();
            _startWindowSamples = 0;
            _lockoutSamples = 0;
            _squatCommandTick = SquatAttemptEventTicks.NotAvailable;
            _standingReference = default(SquatObservationSnapshot);
            _hasStandingReference = false;
            _hasPhysicalDescent = false;
            _hasAscent = false;
            _lowestBarPositionY = float.PositiveInfinity;
        }

        public void BeginAttemptForQualification() => BeginAttempt();

        /// <summary>
        /// Finalizes an explicitly aborted/failed active attempt at the last
        /// copied snapshot. Normal successful attempts finalize automatically
        /// after explicit Rack and Rerack events.
        /// </summary>
        public SquatAttemptRecord Abort()
        {
            if (!_started || _record != null)
                throw new InvalidOperationException("There is no active squat attempt to abort.");
            if (!_recordingStarted || !_collector.HasLastSnapshot ||
                _lifecycle.State != SquatAttemptLifecycleState.ACTIVE_ATTEMPT &&
                _lifecycle.State != SquatAttemptLifecycleState.SQUAT_COMMAND_ISSUED)
                throw new InvalidOperationException("An abort requires an active recorded squat attempt.");

            SquatObservationSnapshot snapshot = _collector.LastSnapshot;
            _lifecycle.Terminate(
                snapshot.SimulationTick,
                snapshot.SimulationTimeSeconds,
                SquatAttemptTerminalReason.ABORTED);
            _collector.EndRecording();
            return FinalizeFrozenTrace();
        }

        private void HandleSnapshot(SimulationTime _, SquatObservationSnapshot snapshot)
        {
            if (!_started || _record != null)
                return;

            if (!_recordingStarted)
            {
                HandleStartQualification(snapshot);
                return;
            }

            switch (_lifecycle.State)
            {
                case SquatAttemptLifecycleState.START_WINDOW:
                    HandleStartWindow(snapshot);
                    break;
                case SquatAttemptLifecycleState.SQUAT_COMMAND_ISSUED:
                    _lifecycle.BeginActiveAttempt();
                    HandleActiveAttempt(snapshot);
                    break;
                case SquatAttemptLifecycleState.ACTIVE_ATTEMPT:
                    HandleActiveAttempt(snapshot);
                    break;
                case SquatAttemptLifecycleState.PHYSICAL_TERMINAL_CONDITION:
                    HandlePhysicalTerminal(snapshot);
                    break;
                case SquatAttemptLifecycleState.RACK_COMMAND_ISSUED:
                    HandleRerackStart(snapshot);
                    break;
            }
        }

        private void HandleStartQualification(SquatObservationSnapshot snapshot)
        {
            SquatStartPredicateDiagnostic predicate = ObserveStartPredicate(snapshot);
            if (predicate.OverallStartCandidate)
                _stableCandidateRun++;
            else
                _stableCandidateRun = 0;

            if (_stableCandidateRun < _requiredStartSamples)
                return;

            _collector.BeginRecording();
            _recordingStarted = true;
            _stableCandidateRun = 0;
        }

        private void HandleStartWindow(SquatObservationSnapshot snapshot)
        {
            SquatStartPredicateDiagnostic predicate = ObserveStartPredicate(snapshot);
            if (_startWindowSamples < _requiredStartSamples)
            {
                _lifecycle.ObserveStartWindowSample(snapshot.SimulationTick);
                _startWindowSamples++;
                // The standing reference must come from a sample that still
                // satisfies the raw start predicate. Recording begins on a
                // persisted candidate run, but a later start-window sample can
                // degrade, and an unqualified reference would shift the whole
                // lockout height comparison. The sealed rule processor remains
                // the authority on start legality.
                if (!_hasStandingReference && predicate.OverallStartCandidate)
                {
                    _standingReference = snapshot;
                    _hasStandingReference = true;
                }
                else if (_startWindowSamples == 1)
                {
                    _standingReference = snapshot;
                }
                return;
            }

            SquatRuleCommandEvent command = _lifecycle.IssueSquatCommand(
                snapshot.SimulationTick,
                snapshot.SimulationTimeSeconds);
            _squatCommandTick = command.SimulationTick;
            // The explicit command and the already-qualified physical
            // operation share this controlled boundary. The adapter remains
            // the sole physical drive writer on the following pre-physics tick.
            _adapter.StartSquat();
        }

        private void HandleActiveAttempt(SquatObservationSnapshot snapshot)
        {
            UpdatePhysicalMotionProgress(snapshot);
            if (_hasAscent && SquatAttemptPhysicalEvidence.IsLockout(snapshot, _standingReference, _failureCalibration))
            {
                _lockoutSamples = 1;
                _lifecycle.MarkPhysicalTerminalCondition(
                    snapshot.SimulationTick,
                    snapshot.SimulationTimeSeconds,
                    SquatAttemptTerminalReason.PHYSICAL_LOCKOUT);
                return;
            }

            if (ExceededAttemptBudget(snapshot))
                TerminateForTimeout(snapshot);
        }

        private void HandlePhysicalTerminal(SquatObservationSnapshot snapshot)
        {
            if (_lifecycle.TerminalReason != SquatAttemptTerminalReason.PHYSICAL_LOCKOUT)
            {
                if (ExceededAttemptBudget(snapshot))
                    TerminateAndFinalize(snapshot, _lifecycle.TerminalReason);
                return;
            }

            // Keep the terminal sample itself and the required stable lockout
            // window before issuing Rack on a later covered sample.
            if (_lockoutSamples >= _requiredLockoutSamples)
            {
                _lifecycle.IssueRackCommand(snapshot.SimulationTick, snapshot.SimulationTimeSeconds);
                _adapter.SetState(SquatState.RACK_COMMAND);
                return;
            }

            if (SquatAttemptPhysicalEvidence.IsLockout(snapshot, _standingReference, _failureCalibration))
                _lockoutSamples++;
            else
                _lockoutSamples = 0;

            if (ExceededAttemptBudget(snapshot))
                TerminateAndFinalize(snapshot, SquatAttemptTerminalReason.TIMEOUT);
        }

        private void HandleRerackStart(SquatObservationSnapshot snapshot)
        {
            _lifecycle.StartRerack(snapshot.SimulationTick, snapshot.SimulationTimeSeconds);
            // Rack mechanics are not implemented in the sealed P1/P3 stack;
            // this explicit value event marks the qualification boundary and
            // leaves the physical bar/trace untouched.
            _adapter.SetState(SquatState.RERACK);
            _collector.EndRecording();
            FinalizeFrozenTrace();
        }

        private void TerminateForTimeout(SquatObservationSnapshot snapshot)
        {
            TerminateAndFinalize(snapshot, SquatAttemptTerminalReason.TIMEOUT);
        }

        private void TerminateAndFinalize(
            SquatObservationSnapshot snapshot,
            SquatAttemptTerminalReason reason)
        {
            _lifecycle.Terminate(snapshot.SimulationTick, snapshot.SimulationTimeSeconds, reason);
            _collector.EndRecording();
            FinalizeFrozenTrace();
        }

        private SquatAttemptRecord FinalizeFrozenTrace()
        {
            if (_record != null)
                return _record;
            _record = _lifecycle.FinalizeAttempt(_collector.Trace);
            return _record;
        }

        private bool ExceededAttemptBudget(SquatObservationSnapshot snapshot)
        {
            return _squatCommandTick != SquatAttemptEventTicks.NotAvailable &&
                snapshot.SimulationTick - _squatCommandTick + 1ul >= (ulong)_maximumAttemptTicks;
        }

        private void UpdatePhysicalMotionProgress(SquatObservationSnapshot snapshot)
        {
            if (!snapshot.Bar.IsAvailable)
                return;

            float positionY = snapshot.Bar.PositionWorldMeters.Y;
            float velocityY = snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y;
            if (!_hasPhysicalDescent && velocityY <= -_failureCalibration.PhysicalMotionVelocityMps)
                _hasPhysicalDescent = true;
            if (!_hasPhysicalDescent)
                return;

            _lowestBarPositionY = Math.Min(_lowestBarPositionY, positionY);
            if (!_hasAscent &&
                velocityY >= _failureCalibration.AscentEstablishmentVelocityMps &&
                positionY - _lowestBarPositionY >= _failureCalibration.AscentEstablishmentDisplacementM)
                _hasAscent = true;
        }

        private SquatStartPredicateDiagnostic ObserveStartPredicate(SquatObservationSnapshot snapshot)
        {
            SquatStartPredicateDiagnostic predicate = EvaluateStartPredicate(snapshot);
            _startPredicateRun = predicate.OverallStartCandidate
                ? _startPredicateRun + 1
                : 0;
            predicate = predicate.WithConsecutiveValidRun(_startPredicateRun);
            _startWindowDiagnostics.Add(predicate);
            return predicate;
        }

        /// <summary>
        /// Evaluates the exact production start-position predicate and exposes
        /// the observed margins without creating a second test rule.
        /// </summary>
        public SquatStartPredicateDiagnostic EvaluateStartPredicate(SquatObservationSnapshot snapshot)
        {
            bool barAvailable = snapshot.Bar.IsAvailable;
            float barLinearSpeed = barAvailable
                ? snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length
                : float.NaN;
            float barVerticalSpeed = barAvailable
                ? snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y
                : float.NaN;
            float barAngularSpeed = barAvailable
                ? snapshot.Bar.AngularVelocityBarRadiansPerSecond.Length
                : float.NaN;
            bool barLinearPass = barAvailable && barLinearSpeed <= _ruleTolerances.MotionlessBarVelocityMps;
            bool barVerticalPass = barAvailable &&
                Math.Abs(barVerticalSpeed) <= _failureCalibration.PhysicalMotionVelocityMps;
            bool barAngularPass = barAvailable && barAngularSpeed <= _ruleTolerances.MotionlessBarAngularVelocityRadS;

            bool supportAvailable = snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE;
            bool supportPresent = snapshot.Support.HasSupport;
            bool leftFootAvailable = snapshot.LeftFoot.Availability == SquatTelemetryAvailability.AVAILABLE;
            bool rightFootAvailable = snapshot.RightFoot.Availability == SquatTelemetryAvailability.AVAILABLE;

            bool leftKneePass = IsAvailableAndWithin(snapshot.Joints.LeftKnee, _ruleTolerances.KneeLockoutToleranceRad);
            bool rightKneePass = IsAvailableAndWithin(snapshot.Joints.RightKnee, _ruleTolerances.KneeLockoutToleranceRad);
            bool leftHipPass = IsAvailableAndWithin(snapshot.Joints.LeftHip, _ruleTolerances.HipErectToleranceRad);
            bool rightHipPass = IsAvailableAndWithin(snapshot.Joints.RightHip, _ruleTolerances.HipErectToleranceRad);
            bool abdomenPass = IsAvailableAndWithin(snapshot.Joints.Abdomen, _ruleTolerances.TrunkErectToleranceRad);
            bool thoraxPass = IsAvailableAndWithin(snapshot.Joints.Thorax, _ruleTolerances.TrunkErectToleranceRad);

            bool overall = barAvailable && barLinearPass && barVerticalPass && barAngularPass &&
                supportAvailable && supportPresent && leftFootAvailable && rightFootAvailable &&
                leftKneePass && rightKneePass && leftHipPass && rightHipPass && abdomenPass && thoraxPass;

            SquatPredictiveBalanceController control = _adapter.BalanceController;
            SquatBalanceObserver balance = _adapter.Balance;
            Vector3 comVelocity = balance.SystemComVelocity;
            float comSpeed = Mathf.Sqrt(comVelocity.x * comVelocity.x + comVelocity.z * comVelocity.z);

            return new SquatStartPredicateDiagnostic(
                snapshot.SimulationTick,
                snapshot.SimulationTimeSeconds,
                overall,
                _startPredicateRun,
                _requiredStartSamples,
                barAvailable,
                barLinearSpeed,
                barLinearPass,
                barAvailable ? _ruleTolerances.MotionlessBarVelocityMps - barLinearSpeed : float.NaN,
                _ruleTolerances.MotionlessBarVelocityMps,
                barVerticalSpeed,
                barVerticalPass,
                barAvailable ? _failureCalibration.PhysicalMotionVelocityMps - Math.Abs(barVerticalSpeed) : float.NaN,
                _failureCalibration.PhysicalMotionVelocityMps,
                barAngularSpeed,
                barAngularPass,
                barAvailable ? _ruleTolerances.MotionlessBarAngularVelocityRadS - barAngularSpeed : float.NaN,
                _ruleTolerances.MotionlessBarAngularVelocityRadS,
                supportAvailable,
                supportPresent,
                leftFootAvailable,
                snapshot.LeftFoot.IsInContact,
                rightFootAvailable,
                snapshot.RightFoot.IsInContact,
                snapshot.Joints.LeftKnee.ActualAngleRadians,
                leftKneePass,
                JointMargin(snapshot.Joints.LeftKnee, _ruleTolerances.KneeLockoutToleranceRad),
                snapshot.Joints.RightKnee.ActualAngleRadians,
                rightKneePass,
                JointMargin(snapshot.Joints.RightKnee, _ruleTolerances.KneeLockoutToleranceRad),
                snapshot.Joints.LeftHip.ActualAngleRadians,
                leftHipPass,
                JointMargin(snapshot.Joints.LeftHip, _ruleTolerances.HipErectToleranceRad),
                snapshot.Joints.RightHip.ActualAngleRadians,
                rightHipPass,
                JointMargin(snapshot.Joints.RightHip, _ruleTolerances.HipErectToleranceRad),
                snapshot.Joints.Abdomen.ActualAngleRadians,
                abdomenPass,
                JointMargin(snapshot.Joints.Abdomen, _ruleTolerances.TrunkErectToleranceRad),
                snapshot.Joints.Thorax.ActualAngleRadians,
                thoraxPass,
                JointMargin(snapshot.Joints.Thorax, _ruleTolerances.TrunkErectToleranceRad),
                control.RawAnkleAuthorityFraction,
                control.RawAnkleSagittalOffsetRad * control.PostureGuardScale,
                control.AnkleSagittalOffsetRad,
                control.PostureGuardScale,
                control.HipStrategyBlend,
                Mathf.Abs(control.HipSagittalOffsetRad) / SquatPredictiveBalanceController.MaxHipSagittalOffsetRad,
                Mathf.Abs(control.TrunkSagittalOffsetRad) / SquatPredictiveBalanceController.MaxTrunkSagittalOffsetRad,
                _adapter.StandingEquilibriumBiasDegrees(SquatJointFamily.Ankle),
                _adapter.StandingEquilibriumBiasDegrees(SquatJointFamily.Knee),
                _adapter.StandingEquilibriumBiasDegrees(SquatJointFamily.Hip),
                _adapter.StandingEquilibriumBiasDegrees(SquatJointFamily.Abdomen),
                _adapter.StandingEquilibriumBiasDegrees(SquatJointFamily.Thorax),
                comSpeed,
                balance.CaptureMargin2D);
        }

        private static bool IsAvailableAndWithin(SquatJointObservation joint, float tolerance)
        {
            return joint.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                Math.Abs(joint.ActualAngleRadians) <= tolerance;
        }

        private static float JointMargin(SquatJointObservation joint, float tolerance)
        {
            return joint.JointAvailability == SquatTelemetryAvailability.AVAILABLE
                ? tolerance - Mathf.Abs(joint.ActualAngleRadians)
                : float.NaN;
        }
    }
}
