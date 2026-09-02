using System;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public enum SquatState : byte
    {
        SETUP,
        UNRACK,
        WALKOUT,
        SETTLE,
        SQUAT_COMMAND,
        DESCENT,
        BOTTOM,
        REVERSAL,
        ASCENT,
        STICKING,
        LOCKOUT,
        RACK_COMMAND,
        RERACK,
        COMPLETE,
        FAILURE
    }

    public enum SquatPhaseDirection : byte
    {
        None,
        Descent,
        Ascent
    }

    public enum SquatReferenceWaypoint : byte
    {
        STANDING,
        QUARTER_DESCENT,
        NEAR_PARALLEL,
        LEGAL_BOTTOM,
        EARLY_ASCENT,
        STICKING,
        LOCKOUT
    }

    public enum SquatTransitionReason : byte
    {
        None,
        ConfirmPressed,
        BarClearOfHooks,
        WalkoutComplete,
        Settled,
        SquatCommandAndYield,
        BilateralLegalDepth,
        ReversalEvidence,
        UpwardVelocity,
        StickingDetected,
        RecoveredFromSticking,
        LockoutReached,
        RackCommandReceived,
        ConfirmRerack,
        BarSecureOnHooks,
        AbortPressed,
        CollapseDetected,
        ShallowReversal,
        StickingTimeout
    }

    public readonly struct SquatDomainObservation
    {
        public SquatDomainObservation(
            bool barClearOfHooks,
            bool walkoutComplete,
            bool physicallySettled,
            bool squatCommandReceived,
            bool depthLegalBilateral,
            bool reversalEvidence,
            bool upwardVelocity,
            bool upwardWithoutDepth,
            bool collapseDetected,
            bool stickingDetected,
            bool recoveredFromSticking,
            bool lockoutReached,
            bool rackCommandReceived,
            bool barSecureOnHooks)
        {
            BarClearOfHooks = barClearOfHooks;
            WalkoutComplete = walkoutComplete;
            PhysicallySettled = physicallySettled;
            SquatCommandReceived = squatCommandReceived;
            DepthLegalBilateral = depthLegalBilateral;
            ReversalEvidence = reversalEvidence;
            UpwardVelocity = upwardVelocity;
            UpwardWithoutDepth = upwardWithoutDepth;
            CollapseDetected = collapseDetected;
            StickingDetected = stickingDetected;
            RecoveredFromSticking = recoveredFromSticking;
            LockoutReached = lockoutReached;
            RackCommandReceived = rackCommandReceived;
            BarSecureOnHooks = barSecureOnHooks;
        }

        public static SquatDomainObservation Empty => new SquatDomainObservation(
            false, false, false, false, false, false, false, false, false, false, false, false, false, false);

        public bool BarClearOfHooks { get; }
        public bool WalkoutComplete { get; }
        public bool PhysicallySettled { get; }
        public bool SquatCommandReceived { get; }
        public bool DepthLegalBilateral { get; }
        public bool ReversalEvidence { get; }
        public bool UpwardVelocity { get; }
        public bool UpwardWithoutDepth { get; }
        public bool CollapseDetected { get; }
        public bool StickingDetected { get; }
        public bool RecoveredFromSticking { get; }
        public bool LockoutReached { get; }
        public bool RackCommandReceived { get; }
        public bool BarSecureOnHooks { get; }
    }

    public readonly struct SquatTransition
    {
        public SquatTransition(SquatState from, SquatState to, SquatTransitionReason reason)
        {
            From = from;
            To = to;
            Reason = reason;
        }

        public SquatState From { get; }
        public SquatState To { get; }
        public SquatTransitionReason Reason { get; }
        public bool Occurred => From != To;
    }

    public sealed class SquatStateMachine
    {
        public const double StickingTimeoutSeconds = 0.35d;

        private SquatState _state;
        private float _phase;
        private float _phaseRate;
        private double _stickingElapsedSeconds;

        public SquatStateMachine(SquatState initialState = SquatState.SETUP)
        {
            _state = initialState;
            _phase = 0f;
        }

        public SquatState State => _state;
        public float Phase => _phase;
        public float PhaseRate => _phaseRate;

        public void Reset(SquatState initialState = SquatState.SETUP)
        {
            _state = initialState;
            _phase = 0f;
            _phaseRate = 0f;
            _stickingElapsedSeconds = 0d;
        }

        public SquatTransition Step(
            PlayerIntentFrame intent,
            SquatDomainObservation observation,
            double stepSeconds)
        {
            if (double.IsNaN(stepSeconds) || double.IsInfinity(stepSeconds) || stepSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(stepSeconds));

            SquatState previous = _state;
            if (intent.WasPressed(IntentAction.Abort) || intent.AbortHeld)
                return Transition(previous, SquatState.FAILURE, SquatTransitionReason.AbortPressed);

            switch (_state)
            {
                case SquatState.SETUP:
                    if (intent.WasPressed(IntentAction.Confirm))
                        return Transition(previous, SquatState.UNRACK, SquatTransitionReason.ConfirmPressed);
                    break;
                case SquatState.UNRACK:
                    if (observation.BarClearOfHooks)
                        return Transition(previous, SquatState.WALKOUT, SquatTransitionReason.BarClearOfHooks);
                    break;
                case SquatState.WALKOUT:
                    if (observation.WalkoutComplete)
                        return Transition(previous, SquatState.SETTLE, SquatTransitionReason.WalkoutComplete);
                    break;
                case SquatState.SETTLE:
                    if (observation.PhysicallySettled)
                        return Transition(previous, SquatState.SQUAT_COMMAND, SquatTransitionReason.Settled);
                    break;
                case SquatState.SQUAT_COMMAND:
                    if (observation.SquatCommandReceived && HasYieldIntent(intent))
                        return Transition(previous, SquatState.DESCENT, SquatTransitionReason.SquatCommandAndYield);
                    break;
                case SquatState.DESCENT:
                    if (observation.CollapseDetected)
                        return Transition(previous, SquatState.FAILURE, SquatTransitionReason.CollapseDetected);
                    if (observation.DepthLegalBilateral)
                    {
                        _phase = 1f;
                        _phaseRate = 0f;
                        return Transition(previous, SquatState.BOTTOM, SquatTransitionReason.BilateralLegalDepth);
                    }
                    AdvancePhase(+DescentRate(intent), stepSeconds);
                    break;
                case SquatState.BOTTOM:
                    if (observation.UpwardWithoutDepth)
                        return Transition(previous, SquatState.FAILURE, SquatTransitionReason.ShallowReversal);
                    if (observation.ReversalEvidence && HasDriveIntent(intent))
                        return Transition(previous, SquatState.REVERSAL, SquatTransitionReason.ReversalEvidence);
                    break;
                case SquatState.REVERSAL:
                    if (observation.UpwardVelocity)
                        return Transition(previous, SquatState.ASCENT, SquatTransitionReason.UpwardVelocity);
                    break;
                case SquatState.ASCENT:
                    if (observation.StickingDetected)
                    {
                        _stickingElapsedSeconds = 0d;
                        return Transition(previous, SquatState.STICKING, SquatTransitionReason.StickingDetected);
                    }
                    if (observation.LockoutReached)
                    {
                        _phase = 0f;
                        _phaseRate = 0f;
                        return Transition(previous, SquatState.LOCKOUT, SquatTransitionReason.LockoutReached);
                    }
                    AdvancePhase(-AscentRate(intent), stepSeconds);
                    break;
                case SquatState.STICKING:
                    if (observation.RecoveredFromSticking)
                        return Transition(previous, SquatState.ASCENT, SquatTransitionReason.RecoveredFromSticking);
                    _stickingElapsedSeconds += stepSeconds;
                    if (_stickingElapsedSeconds >= StickingTimeoutSeconds)
                        return Transition(previous, SquatState.FAILURE, SquatTransitionReason.StickingTimeout);
                    break;
                case SquatState.LOCKOUT:
                    if (observation.RackCommandReceived)
                        return Transition(previous, SquatState.RACK_COMMAND, SquatTransitionReason.RackCommandReceived);
                    break;
                case SquatState.RACK_COMMAND:
                    if (intent.WasPressed(IntentAction.Confirm))
                        return Transition(previous, SquatState.RERACK, SquatTransitionReason.ConfirmRerack);
                    break;
                case SquatState.RERACK:
                    if (observation.BarSecureOnHooks)
                        return Transition(previous, SquatState.COMPLETE, SquatTransitionReason.BarSecureOnHooks);
                    break;
                case SquatState.COMPLETE:
                case SquatState.FAILURE:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }

            return new SquatTransition(previous, _state, SquatTransitionReason.None);
        }

        private SquatTransition Transition(SquatState previous, SquatState next, SquatTransitionReason reason)
        {
            _state = next;
            if (next == SquatState.STICKING)
                _stickingElapsedSeconds = 0d;
            if (next == SquatState.COMPLETE || next == SquatState.FAILURE)
                _phaseRate = 0f;
            return new SquatTransition(previous, next, reason);
        }

        private void AdvancePhase(float targetRate, double stepSeconds)
        {
            float maxRate = SquatReferenceMotion.MaxPhaseRatePerSecond;
            float maxAcceleration = SquatReferenceMotion.MaxPhaseAccelerationPerSecondSquared;
            float desired = Math.Max(-maxRate, Math.Min(maxRate, targetRate));
            float maximumChange = maxAcceleration * (float)stepSeconds;
            _phaseRate = MoveTowards(_phaseRate, desired, maximumChange);
            _phase = Math.Max(0f, Math.Min(1f, _phase + _phaseRate * (float)stepSeconds));
            if (_phase <= 0f || _phase >= 1f)
                _phaseRate = 0f;
        }

        private static bool HasYieldIntent(PlayerIntentFrame intent) => intent.Yield01 > 0f || intent.YieldHeld;

        private static bool HasDriveIntent(PlayerIntentFrame intent) => intent.Drive01 > 0f || intent.DriveHeld;

        private static float DescentRate(PlayerIntentFrame intent) =>
            SquatReferenceMotion.DescentRatePerSecond * Math.Max(intent.Yield01, intent.YieldHeld ? 1f : 0f);

        private static float AscentRate(PlayerIntentFrame intent) =>
            SquatReferenceMotion.AscentRatePerSecond * Math.Max(intent.Drive01, intent.DriveHeld ? 1f : 0f);

        private static float MoveTowards(float current, float target, float maximumDelta)
        {
            if (Math.Abs(target - current) <= maximumDelta)
                return target;
            return current + Math.Sign(target - current) * maximumDelta;
        }
    }
}
