using System;
using System.Collections.Generic;

namespace PowerliftingSimulator.Bench
{
    public enum BenchCommand : byte
    {
        None,
        Start,
        Press,
        Rack,
        Replace
    }

    public enum BenchRefereeState : byte
    {
        AwaitingLiftOff,
        AwaitingStart,
        Descent,
        OnChest,
        PressOut,
        Completed
    }

    /// <summary>Faults, each tied to the rule it implements.</summary>
    public enum BenchFault : byte
    {
        /// <summary>4.2.1(1) bar lowered before Start.</summary>
        DescentBeforeStart,
        /// <summary>4.2.1(1) bar pressed off the chest before Press.</summary>
        PressBeforeCommand,
        /// <summary>4.2.1(2) head raised from the bench.</summary>
        HeadRaised,
        /// <summary>4.2.1(2) shoulders raised from the bench.</summary>
        ShouldersRaised,
        /// <summary>4.2.1(2) buttocks raised from the bench.</summary>
        ButtocksRaised,
        /// <summary>4.2.1(2) lateral movement of a hand on the bar.</summary>
        HandMovedOnBar,
        /// <summary>4.2(2) the complete grip must be maintained throughout the lift.</summary>
        GripLost,
        /// <summary>4.2.1(3) heaving or sinking after the bar is motionless.</summary>
        SinkAfterMotionless,
        /// <summary>4.2.1(4) upper body thrust from the chest.</summary>
        UpperBodyThrust,
        /// <summary>4.2.1(5) downward movement of the whole bar during press-out.</summary>
        BarDownwardDuringPressOut,
        /// <summary>4.2.1(6) bar not lowered to the chest or abdominal area.</summary>
        NoChestTouch,
        /// <summary>4.2.1(6) bar touching the belt.</summary>
        BarOnBelt,
        /// <summary>4.2.1(7) not pressed to straight arms, elbows locked.</summary>
        NotLockedOut,
        /// <summary>4.2.1(8) spotter contact between signals.</summary>
        SpotterAssistance,
        /// <summary>4.2.1(9) feet touching the bench or its supports.</summary>
        FootOnBench,
        /// <summary>4.2.1(9) / 4.2(2) a foot lifted or no longer flat.</summary>
        FootLifted,
        /// <summary>4.2.1(10) contact between the bar and the bar rests.</summary>
        RackContact,
        /// <summary>4.2.1(11) elbows not lowered level with or below the shoulders.</summary>
        ElbowDepthNotReached,
        /// <summary>4.2(8) bar not held motionless on the chest (4.2.1(12)).</summary>
        NotMotionlessOnChest,
        /// <summary>Game rule under 4.2.1(12): bar supported by the safety racks.</summary>
        BarOnSafeties,
        /// <summary>4.2(6): not in position within five seconds, "Replace".</summary>
        NotInPositionReplace,
        /// <summary>Game rule: the player abandoned the attempt and called the spotters.</summary>
        AttemptAborted
    }

    public readonly struct BenchFaultRecord
    {
        public BenchFaultRecord(BenchFault fault, ulong tick, double timeSeconds, string detail)
        {
            Fault = fault;
            Tick = tick;
            TimeSeconds = timeSeconds;
            Detail = detail ?? string.Empty;
        }

        public BenchFault Fault { get; }
        public ulong Tick { get; }
        public double TimeSeconds { get; }
        public string Detail { get; }
        public string RuleReference => BenchReferee.RuleReference(Fault);
    }

    public readonly struct BenchCommandRecord
    {
        public BenchCommandRecord(BenchCommand command, ulong tick, double timeSeconds, string basis)
        {
            Command = command;
            Tick = tick;
            TimeSeconds = timeSeconds;
            Basis = basis ?? string.Empty;
        }

        public BenchCommand Command { get; }
        public ulong Tick { get; }
        public double TimeSeconds { get; }
        public string Basis { get; }
    }

    /// <summary>
    /// The chief referee for one bench press attempt. It sees only completed
    /// physical observations. It issues Start, Press, Rack or Replace and
    /// records every fault with the tick and the measurement that caused it.
    /// It is deliberately independent of the squat referee and of whatever
    /// the controller intends to do.
    /// </summary>
    public sealed class BenchReferee
    {
        private readonly int _motionlessTicks;
        private readonly int _contactLossTicks;
        private readonly List<BenchFaultRecord> _faults = new List<BenchFaultRecord>();
        private readonly List<BenchCommandRecord> _commands = new List<BenchCommandRecord>();
        private readonly HashSet<BenchFault> _raised = new HashSet<BenchFault>();
        private readonly double _tickSeconds;

        private int _offRackTicks;
        private int _onRackTicks;
        private readonly int _liftOffClearTicks;
        private readonly int _returnedTicks;
        private int _readyStillTicks;
        private double _liftOffTime = double.NaN;
        private double _maxBarYSinceLiftOff = double.NegativeInfinity;
        private double _startBarY;
        private double _minDescentBarY;
        private double _touchBarY;
        private double _phaseEntryTime;
        private int _onChestStillTicks;
        private bool _depthSeenOnChest;
        private double _pressBarY;
        private double _pressMaxLeftEndY;
        private double _pressMaxRightEndY;
        private double _pressMaxCenterY;
        private double _lastPressProgressTime;
        private bool _barLeftChestAfterPress;
        private int _lockoutStillTicks;
        private int _headLoss, _shoulderLoss, _buttockLoss, _leftFootLoss, _rightFootLoss, _footOnBench;

        public BenchReferee(double tickSeconds)
        {
            if (!(tickSeconds > 0d) || double.IsInfinity(tickSeconds))
                throw new ArgumentOutOfRangeException(nameof(tickSeconds));
            _tickSeconds = tickSeconds;
            _motionlessTicks = Math.Max(1, (int)Math.Ceiling(BenchRulebook.MotionlessWindowSeconds / tickSeconds - 1e-9));
            _contactLossTicks = BenchRulebook.ContactLossTicks;
            _liftOffClearTicks = Math.Max(1, (int)Math.Ceiling(BenchRulebook.LiftOffClearSeconds / tickSeconds - 1e-9));
            _returnedTicks = Math.Max(1, (int)Math.Ceiling(BenchRulebook.ReturnedToRestsSeconds / tickSeconds - 1e-9));
        }

        public BenchRefereeState State { get; private set; } = BenchRefereeState.AwaitingLiftOff;
        public BenchCommand LastCommand => _commands.Count == 0 ? BenchCommand.None : _commands[_commands.Count - 1].Command;
        public IReadOnlyList<BenchFaultRecord> Faults => _faults;
        public IReadOnlyList<BenchCommandRecord> Commands => _commands;
        public bool IsComplete => State == BenchRefereeState.Completed;
        public bool StartGiven => HasCommand(BenchCommand.Start);
        public bool PressGiven => HasCommand(BenchCommand.Press);
        public bool RackGiven => HasCommand(BenchCommand.Rack);

        /// <summary>GOOD LIFT only when Rack followed a valid lockout and no fault was recorded.</summary>
        public bool IsGoodLift => IsComplete && _faults.Count == 0 && _lockoutReached;

        private bool _lockoutReached;

        public bool HasFault(BenchFault fault) => _raised.Contains(fault);

        public bool HasCommand(BenchCommand command)
        {
            foreach (BenchCommandRecord record in _commands)
            {
                if (record.Command == command)
                    return true;
            }
            return false;
        }

        /// <summary>Processes one completed tick. Returns the command issued this tick, if any.</summary>
        public BenchCommand Observe(in BenchObservation o)
        {
            if (State == BenchRefereeState.Completed)
                return BenchCommand.None;

            switch (State)
            {
                case BenchRefereeState.AwaitingLiftOff:
                    return ObserveAwaitingLiftOff(o);
                case BenchRefereeState.AwaitingStart:
                    return ObserveAwaitingStart(o);
            }

            // The lift proper: from Start until Rack.
            BenchCommand terminal = MonitorLiftProper(o);
            if (terminal != BenchCommand.None)
                return terminal;

            switch (State)
            {
                case BenchRefereeState.Descent:
                    return ObserveDescent(o);
                case BenchRefereeState.OnChest:
                    return ObserveOnChest(o);
                case BenchRefereeState.PressOut:
                    return ObservePressOut(o);
                default:
                    return BenchCommand.None;
            }
        }

        /// <summary>The player abandons the attempt; the spotters take the bar.</summary>
        public BenchCommand Abort(in BenchObservation o)
        {
            if (State == BenchRefereeState.Completed)
                return BenchCommand.None;
            Raise(BenchFault.AttemptAborted, o, "attempt abandoned by the lifter");
            return Complete(BenchCommand.Rack, o, "attempt abandoned");
        }

        private BenchCommand ObserveAwaitingLiftOff(in BenchObservation o)
        {
            bool off = !o.BarOnRack && o.BothGripsAttached;
            if (off && _offRackTicks == 0)
            {
                _liftOffTime = o.TimeSeconds;
                _maxBarYSinceLiftOff = o.BarCenterY;
            }
            _offRackTicks = off ? _offRackTicks + 1 : 0;
            if (off)
                _maxBarYSinceLiftOff = Math.Max(_maxBarYSinceLiftOff, o.BarCenterY);
            if (_offRackTicks < _liftOffClearTicks)
                return BenchCommand.None;

            State = BenchRefereeState.AwaitingStart;
            _readyStillTicks = 0;
            _onRackTicks = 0;
            return ObserveAwaitingStart(o);
        }

        private BenchCommand ObserveAwaitingStart(in BenchObservation o)
        {
            _maxBarYSinceLiftOff = Math.Max(_maxBarYSinceLiftOff, o.BarCenterY);

            _onRackTicks = o.BarOnRack ? _onRackTicks + 1 : 0;
            if (_onRackTicks >= _returnedTicks)
            {
                // Back in the rests before Start: the attempt never commenced.
                Raise(BenchFault.NotInPositionReplace, o, "bar returned to the rests before Start");
                return Complete(BenchCommand.Replace, o, "bar back on the rests before Start");
            }

            if (_maxBarYSinceLiftOff - o.BarCenterY > BenchRulebook.PrematureDescentM)
            {
                Raise(BenchFault.DescentBeforeStart, o,
                    $"bar {(_maxBarYSinceLiftOff - o.BarCenterY) * 1000d:F0} mm below its post-lift-off height before Start");
            }

            bool ready = !o.BarOnRack && !o.BarOnSafety && o.ElbowsLocked && o.GripLegal &&
                o.HeadInContact && o.ShouldersInContact && o.ButtocksInContact &&
                o.LeftFootPlanted && o.RightFootPlanted && o.FeetOnBenchForceN < BenchRulebook.ContactForceFloorN &&
                o.BarHorizontalOffsetFromShouldersM <= BenchRulebook.StartBarOverShoulderToleranceM &&
                !o.SpotterContact;

            _readyStillTicks = ready && !o.BarMoving ? _readyStillTicks + 1 : 0;
            if (_readyStillTicks >= _motionlessTicks)
            {
                State = BenchRefereeState.Descent;
                _startBarY = o.BarCenterY;
                _minDescentBarY = o.BarCenterY;
                _phaseEntryTime = o.TimeSeconds;
                ResetContactCounters();
                return Issue(BenchCommand.Start, o,
                    $"motionless {_motionlessTicks} ticks, elbows {o.LeftElbowFlexionDeg:F1}/{o.RightElbowFlexionDeg:F1} deg, supports and grip legal");
            }

            if (o.TimeSeconds - _liftOffTime > BenchRulebook.ReplaceAfterSeconds)
            {
                Raise(BenchFault.NotInPositionReplace, o, DescribeNotReady(o));
                return Complete(BenchCommand.Replace, o, "not in position five seconds after lift-off");
            }

            return BenchCommand.None;
        }

        private BenchCommand MonitorLiftProper(in BenchObservation o)
        {
            Track(ref _headLoss, !o.HeadInContact, BenchFault.HeadRaised, o, $"head bench force {o.HeadBenchForceN:F1} N");
            Track(ref _shoulderLoss, !o.ShouldersInContact, BenchFault.ShouldersRaised, o, $"shoulder bench force {o.ShouldersBenchForceN:F1} N");
            Track(ref _buttockLoss, !o.ButtocksInContact, BenchFault.ButtocksRaised, o, $"buttocks bench force {o.ButtocksBenchForceN:F1} N");
            Track(ref _leftFootLoss, !o.LeftFootPlanted, BenchFault.FootLifted, o, $"left foot {o.LeftFootFloorForceN:F1} N flat={o.LeftFootFlat}");
            Track(ref _rightFootLoss, !o.RightFootPlanted, BenchFault.FootLifted, o, $"right foot {o.RightFootFloorForceN:F1} N flat={o.RightFootFlat}");
            Track(ref _footOnBench, o.FeetOnBenchForceN >= BenchRulebook.ContactForceFloorN, BenchFault.FootOnBench, o, $"feet on bench {o.FeetOnBenchForceN:F1} N");

            if (o.LeftHandSlipM > BenchRulebook.HandLateralSlipM || o.RightHandSlipM > BenchRulebook.HandLateralSlipM)
                Raise(BenchFault.HandMovedOnBar, o, $"hand slip L {o.LeftHandSlipM * 1000d:F1} mm R {o.RightHandSlipM * 1000d:F1} mm");
            if (o.SpotterContact)
                Raise(BenchFault.SpotterAssistance, o, "spotter contact with bar or lifter");
            if (o.BarOnRack)
                Raise(BenchFault.RackContact, o, $"bar-rest force {o.BarRackForceN:F1} N");

            if (!o.BothGripsAttached)
            {
                Raise(BenchFault.GripLost, o, $"grip attached L={o.LeftGripAttached} R={o.RightGripAttached}");
                return Complete(BenchCommand.Rack, o, "grip lost");
            }

            if (o.BarOnSafety)
            {
                Raise(BenchFault.BarOnSafeties, o, $"safety force {o.BarSafetyForceN:F1} N");
                return Complete(BenchCommand.Rack, o, "bar on the safety racks");
            }

            if (o.BarOnBelt)
            {
                Raise(BenchFault.BarOnBelt, o, $"bar-belt force {o.BarBeltForceN:F1} N");
                return Complete(BenchCommand.Rack, o, "bar lowered to the belt");
            }

            return BenchCommand.None;
        }

        private BenchCommand ObserveDescent(in BenchObservation o)
        {
            _minDescentBarY = Math.Min(_minDescentBarY, o.BarCenterY);
            if (o.BothElbowsAtDepth)
                _depthSeenOnChest = true;

            if (o.BarOnChest)
            {
                State = BenchRefereeState.OnChest;
                _touchBarY = o.BarCenterY;
                _phaseEntryTime = o.TimeSeconds;
                _onChestStillTicks = 0;
                return BenchCommand.None;
            }

            if (o.BarCenterY - _minDescentBarY > BenchRulebook.MissedTouchReboundM &&
                _startBarY - _minDescentBarY > BenchRulebook.MissedTouchReboundM)
            {
                Raise(BenchFault.NoChestTouch, o,
                    $"bar reversed {(o.BarCenterY - _minDescentBarY) * 1000d:F0} mm above its lowest point without chest contact");
                return Complete(BenchCommand.Rack, o, "bar did not touch the chest or abdomen");
            }

            return BenchCommand.None;
        }

        private BenchCommand ObserveOnChest(in BenchObservation o)
        {
            if (o.BothElbowsAtDepth)
                _depthSeenOnChest = true;

            if (!o.BarOnChest && o.BarCenterY - _touchBarY > BenchRulebook.MissedTouchReboundM)
            {
                // The bar left the chest upward before the Press signal.
                Raise(BenchFault.PressBeforeCommand, o,
                    $"bar {(o.BarCenterY - _touchBarY) * 1000d:F0} mm above the touch point before Press");
                BeginPressOut(o, givenByReferee: false);
                return BenchCommand.None;
            }

            _onChestStillTicks = o.BarOnChest && !o.BarMoving ? _onChestStillTicks + 1 : 0;
            if (_onChestStillTicks >= _motionlessTicks)
            {
                if (!_depthSeenOnChest)
                    Raise(BenchFault.ElbowDepthNotReached, o, "elbow underside never level with or below the shoulder top");
                BeginPressOut(o, givenByReferee: true);
                return Issue(BenchCommand.Press, o,
                    $"bar on chest {o.BarChestForceN:F0} N and motionless {_motionlessTicks} ticks");
            }

            if (o.TimeSeconds - _phaseEntryTime > 6.0)
            {
                Raise(BenchFault.NotMotionlessOnChest, o, "bar never motionless on the chest within 6 s");
                return Complete(BenchCommand.Rack, o, "no motionless pause");
            }

            return BenchCommand.None;
        }

        private void BeginPressOut(in BenchObservation o, bool givenByReferee)
        {
            State = BenchRefereeState.PressOut;
            _pressBarY = o.BarCenterY;
            _pressMaxCenterY = o.BarCenterY;
            _pressMaxLeftEndY = o.BarLeftEndY;
            _pressMaxRightEndY = o.BarRightEndY;
            _lastPressProgressTime = o.TimeSeconds;
            _barLeftChestAfterPress = !givenByReferee;
            _lockoutStillTicks = 0;
        }

        private BenchCommand ObservePressOut(in BenchObservation o)
        {
            if (!_barLeftChestAfterPress)
            {
                if (o.BarOnChest)
                {
                    if (o.BarCenterY < _pressBarY - BenchRulebook.SinkAfterMotionlessM)
                        Raise(BenchFault.SinkAfterMotionless, o,
                            $"bar sank {(_pressBarY - o.BarCenterY) * 1000d:F0} mm after it was motionless");
                    if (o.ThoraxVelocityY > BenchRulebook.UpperBodyThrustMps)
                        Raise(BenchFault.UpperBodyThrust, o, $"thorax rising {o.ThoraxVelocityY:F2} m/s under the bar");
                }
                else
                {
                    _barLeftChestAfterPress = true;
                }
            }
            else if (o.BarOnChest)
            {
                Raise(BenchFault.BarDownwardDuringPressOut, o, "bar returned to the chest during the press-out");
            }

            if (o.BarCenterY > _pressMaxCenterY + 0.002)
                _lastPressProgressTime = o.TimeSeconds;
            _pressMaxCenterY = Math.Max(_pressMaxCenterY, o.BarCenterY);
            _pressMaxLeftEndY = Math.Max(_pressMaxLeftEndY, o.BarLeftEndY);
            _pressMaxRightEndY = Math.Max(_pressMaxRightEndY, o.BarRightEndY);

            if (_barLeftChestAfterPress &&
                o.BarLeftEndVelocityY < -BenchRulebook.PressOutDownwardSpeedMps &&
                o.BarRightEndVelocityY < -BenchRulebook.PressOutDownwardSpeedMps &&
                _pressMaxLeftEndY - o.BarLeftEndY > BenchRulebook.PressOutDownwardDisplacementM &&
                _pressMaxRightEndY - o.BarRightEndY > BenchRulebook.PressOutDownwardDisplacementM)
            {
                Raise(BenchFault.BarDownwardDuringPressOut, o,
                    $"both ends descending (L {o.BarLeftEndVelocityY:F3}, R {o.BarRightEndVelocityY:F3} m/s), " +
                    $"{(_pressMaxLeftEndY - o.BarLeftEndY) * 1000d:F0}/{(_pressMaxRightEndY - o.BarRightEndY) * 1000d:F0} mm below the press-out maximum");
            }

            _lockoutStillTicks = o.ElbowsLocked && !o.BarMoving && !o.BarOnChest ? _lockoutStillTicks + 1 : 0;
            if (_lockoutStillTicks >= _motionlessTicks)
            {
                _lockoutReached = true;
                return Complete(BenchCommand.Rack, o,
                    $"elbows locked {o.LeftElbowFlexionDeg:F1}/{o.RightElbowFlexionDeg:F1} deg and motionless {_motionlessTicks} ticks");
            }

            if (o.TimeSeconds - _lastPressProgressTime > BenchRulebook.PressStallSeconds)
            {
                Raise(BenchFault.NotLockedOut, o,
                    $"no press-out progress for {BenchRulebook.PressStallSeconds:F0} s, elbows {o.LeftElbowFlexionDeg:F0}/{o.RightElbowFlexionDeg:F0} deg");
                return Complete(BenchCommand.Rack, o, "failed to lock out");
            }

            return BenchCommand.None;
        }

        private void Track(ref int counter, bool violated, BenchFault fault, in BenchObservation o, string detail)
        {
            counter = violated ? counter + 1 : 0;
            if (counter >= _contactLossTicks)
                Raise(fault, o, detail);
        }

        private void ResetContactCounters()
        {
            _headLoss = _shoulderLoss = _buttockLoss = _leftFootLoss = _rightFootLoss = _footOnBench = 0;
        }

        private void Raise(BenchFault fault, in BenchObservation o, string detail)
        {
            if (_raised.Add(fault))
                _faults.Add(new BenchFaultRecord(fault, o.Tick, o.TimeSeconds, detail));
        }

        private BenchCommand Issue(BenchCommand command, in BenchObservation o, string basis)
        {
            _commands.Add(new BenchCommandRecord(command, o.Tick, o.TimeSeconds, basis));
            return command;
        }

        private BenchCommand Complete(BenchCommand command, in BenchObservation o, string basis)
        {
            State = BenchRefereeState.Completed;
            return Issue(command, o, basis);
        }

        private static string DescribeNotReady(in BenchObservation o)
        {
            var parts = new List<string>();
            if (!o.ElbowsLocked) parts.Add($"elbows not locked ({o.LeftElbowFlexionDeg:F0}/{o.RightElbowFlexionDeg:F0} deg)");
            if (!o.GripLegal) parts.Add("grip not legal");
            if (!o.HeadInContact) parts.Add("head off the bench");
            if (!o.ShouldersInContact) parts.Add("shoulders off the bench");
            if (!o.ButtocksInContact) parts.Add("buttocks off the bench");
            if (!o.LeftFootPlanted || !o.RightFootPlanted) parts.Add("feet not flat on the floor");
            if (o.BarHorizontalOffsetFromShouldersM > BenchRulebook.StartBarOverShoulderToleranceM)
                parts.Add($"bar {o.BarHorizontalOffsetFromShouldersM * 100d:F0} cm from over the shoulders");
            if (o.BarMoving) parts.Add("bar not motionless");
            return parts.Count == 0 ? "not motionless long enough" : string.Join(", ", parts);
        }

        public static string RuleReference(BenchFault fault)
        {
            switch (fault)
            {
                case BenchFault.DescentBeforeStart:
                case BenchFault.PressBeforeCommand: return "4.2.1(1)";
                case BenchFault.HeadRaised:
                case BenchFault.ShouldersRaised:
                case BenchFault.ButtocksRaised:
                case BenchFault.HandMovedOnBar: return "4.2.1(2)";
                case BenchFault.GripLost: return "4.2(2)";
                case BenchFault.SinkAfterMotionless: return "4.2.1(3)";
                case BenchFault.UpperBodyThrust: return "4.2.1(4)";
                case BenchFault.BarDownwardDuringPressOut: return "4.2.1(5)";
                case BenchFault.NoChestTouch:
                case BenchFault.BarOnBelt: return "4.2.1(6)";
                case BenchFault.NotLockedOut: return "4.2.1(7)";
                case BenchFault.SpotterAssistance: return "4.2.1(8)";
                case BenchFault.FootOnBench:
                case BenchFault.FootLifted: return "4.2.1(9)";
                case BenchFault.RackContact: return "4.2.1(10)";
                case BenchFault.ElbowDepthNotReached: return "4.2.1(11)";
                case BenchFault.NotMotionlessOnChest: return "4.2(8)";
                case BenchFault.BarOnSafeties: return "4.2.1(12) game rule";
                case BenchFault.NotInPositionReplace: return "4.2(6)";
                case BenchFault.AttemptAborted: return "game rule";
                default: return "4.2.1(12)";
            }
        }

        public static string Describe(BenchFault fault)
        {
            switch (fault)
            {
                case BenchFault.DescentBeforeStart: return "Bar lowered before the Start signal";
                case BenchFault.PressBeforeCommand: return "Pressed before the Press signal";
                case BenchFault.HeadRaised: return "Head raised from the bench";
                case BenchFault.ShouldersRaised: return "Shoulders raised from the bench";
                case BenchFault.ButtocksRaised: return "Buttocks raised from the bench";
                case BenchFault.HandMovedOnBar: return "Hand moved on the bar";
                case BenchFault.GripLost: return "Grip on the bar lost";
                case BenchFault.SinkAfterMotionless: return "Bar sunk into the chest after the pause";
                case BenchFault.UpperBodyThrust: return "Upper body thrust off the chest";
                case BenchFault.BarDownwardDuringPressOut: return "Downward movement of the bar during the press-out";
                case BenchFault.NoChestTouch: return "Bar did not touch the chest";
                case BenchFault.BarOnBelt: return "Bar touched the belt";
                case BenchFault.NotLockedOut: return "Elbows not locked at completion";
                case BenchFault.SpotterAssistance: return "Spotter assistance during the lift";
                case BenchFault.FootOnBench: return "Foot touched the bench";
                case BenchFault.FootLifted: return "Foot lifted from the platform";
                case BenchFault.RackContact: return "Bar touched the rests during the lift";
                case BenchFault.ElbowDepthNotReached: return "Elbows not lowered to shoulder level";
                case BenchFault.NotMotionlessOnChest: return "Bar not held motionless on the chest";
                case BenchFault.BarOnSafeties: return "Bar came down on the safety racks";
                case BenchFault.NotInPositionReplace: return "Not in position: Replace";
                case BenchFault.AttemptAborted: return "Attempt abandoned";
                default: return fault.ToString();
            }
        }
    }
}
