using System;
using System.Collections.Generic;
using System.Globalization;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70: values for the deadlift referee. Rule text: IPF Technical
    /// Rulebook effective 01 March 2026, v3, section 4.3 (deadlift) and 4.3.1
    /// (causes for disqualification). The book states the deadlift criteria in
    /// words; every number below is a named game approximation, reusing the
    /// bench values where the same physical judgement applies.
    /// </summary>
    public static class DeadliftRulebook
    {
        public const string Source = "IPF Technical Rulebook, effective 01 March 2026, v3, s4.3";
        // "Motionless": the bench approximation (2 cm/s for 0.30 s).
        public const double MotionlessSpeedMps = 0.020;
        public const double MotionlessWindowSeconds = 0.30;
        // 4.3(2) knees locked in a straight position: realized knee flexion at or below this.
        public const double KneesLockedMaxFlexionDeg = 10.0;
        // 4.3(2) standing erect with the shoulders back: hip-to-shoulder line at least this far above horizontal,
        // and the shoulder joint centres not ahead of the hip joint centres by more than the tolerance.
        public const double ErectTorsoMinDeg = 80.0;
        public const double ShouldersBackToleranceM = 0.03;
        // 4.3.1(1) any downward movement of the bar before the final position: bar centre descending faster
        // than this and at least this far below its highest point (bench press-out values).
        public const double DownwardSpeedMps = 0.020;
        public const double DownwardDisplacementM = 0.010;
        // 4.3.1(6) lowering before the signal, judged once the final position has been reached: the bar
        // more than this below its height when the finished position was first observed (settling of a
        // locked-out bar is not lowering).
        public const double LoweringBeforeSignalM = 0.030;
        // 4.3.1(4) supporting the bar on the thighs: bar-thigh normal force above this during the ascent.
        public const double ThighSupportForceN = 50.0;
        // 4.3.1(5) stepping: a foot's horizontal travel beyond this (rocking heel/toe is permitted, so the
        // vertical component and rotation are not judged).
        public const double FootStepM = 0.030;
        // 4.3.1(7) return without control: a grip released before the bar rests on the platform, or a
        // landing faster than this.
        public const double UncontrolledLandingMps = 0.50;
        // The bar rests on the platform: plates carrying at least this fraction of the bar weight.
        public const double RestingPlateFraction = 0.9;
        // An attempt starts when the bar has left both plates (4.3(3): any raising counts as an attempt).
        public const double LiftOffPlateN = 2.0;
    }

    public enum DeadliftFault : byte
    {
        /// <summary>4.3.1(1) downward movement of the bar before it reaches the final position.</summary>
        DownwardMovement,
        /// <summary>4.3.1(2) failure to stand erect with the shoulders back.</summary>
        NotErect,
        /// <summary>4.3.1(3) failure to lock the knees straight at completion.</summary>
        KneesNotLocked,
        /// <summary>4.3.1(4) supporting the bar on the thighs during the performance.</summary>
        ThighSupport,
        /// <summary>4.3.1(5) stepping backward, forward or laterally.</summary>
        FootMoved,
        /// <summary>4.3.1(6) lowering the bar before the chief referee's signal.</summary>
        LoweredBeforeSignal,
        /// <summary>4.3.1(7) the bar returned to the platform without control with both hands.</summary>
        UncontrolledReturn,
        /// <summary>4.3(1)/game: the bar was not completed (no final position within the time limit).</summary>
        NotCompleted
    }

    public readonly struct DeadliftObservation
    {
        public DeadliftObservation(ulong tick, double timeSeconds, double barY, double barVy, double barSpeed, double plateN,
            double thighBarN, double kneeFlexionLDeg, double kneeFlexionRDeg, double torsoDeg, double shoulderAheadOfHipM,
            double footTravelLM, double footTravelRM, bool gripL, bool gripR)
        {
            Tick = tick; TimeSeconds = timeSeconds; BarY = barY; BarVy = barVy; BarSpeed = barSpeed; PlateN = plateN;
            ThighBarN = thighBarN; KneeFlexionLDeg = kneeFlexionLDeg; KneeFlexionRDeg = kneeFlexionRDeg; TorsoDeg = torsoDeg;
            ShoulderAheadOfHipM = shoulderAheadOfHipM; FootTravelLM = footTravelLM; FootTravelRM = footTravelRM; GripL = gripL; GripR = gripR;
        }
        public ulong Tick { get; }
        public double TimeSeconds { get; }
        public double BarY { get; }
        public double BarVy { get; }
        public double BarSpeed { get; }
        public double PlateN { get; }
        public double ThighBarN { get; }
        public double KneeFlexionLDeg { get; }
        public double KneeFlexionRDeg { get; }
        public double TorsoDeg { get; }
        public double ShoulderAheadOfHipM { get; }
        public double FootTravelLM { get; }
        public double FootTravelRM { get; }
        public bool GripL { get; }
        public bool GripR { get; }
    }

    public enum DeadliftRefereeState : byte
    {
        AwaitingLiftOff,
        Ascent,
        AwaitingDown,
        Lowering,
        Completed
    }

    public readonly struct DeadliftFaultRecord
    {
        public DeadliftFaultRecord(DeadliftFault fault, ulong tick, double timeSeconds, string detail)
        {
            Fault = fault; Tick = tick; TimeSeconds = timeSeconds; Detail = detail ?? string.Empty;
        }
        public DeadliftFault Fault { get; }
        public ulong Tick { get; }
        public double TimeSeconds { get; }
        public string Detail { get; }
        public string RuleReference => DeadliftReferee.RuleReference(Fault);
    }

    /// <summary>
    /// The chief referee of one deadlift attempt. It sees only completed
    /// physical observations, gives the single deadlift signal "Down" (4.3(2):
    /// the bar held motionless, the lifter in an apparently finished
    /// position), and records every fault with its tick and measurement. It
    /// is independent of what the controller intends.
    /// </summary>
    public sealed class DeadliftReferee
    {
        private readonly double _barRestY;
        private readonly double _barWeightN;
        private readonly int _motionlessTicks;
        private readonly List<DeadliftFaultRecord> _faults = new List<DeadliftFaultRecord>();
        private readonly HashSet<DeadliftFault> _raised = new HashSet<DeadliftFault>();
        private double _maxBarY = double.NegativeInfinity;
        private double _lockoutBarY = double.NaN;
        private bool _finalReached;
        private double _finalBarY;
        private int _still;
        private double _completionTimeoutSeconds;
        private double _liftOffTime = double.NaN;

        public DeadliftReferee(double barRestY, double barWeightN, double tickSeconds, double completionTimeoutSeconds = 12.0)
        {
            _barRestY = barRestY;
            _barWeightN = barWeightN;
            _motionlessTicks = (int)Math.Ceiling(DeadliftRulebook.MotionlessWindowSeconds / tickSeconds);
            _completionTimeoutSeconds = completionTimeoutSeconds;
        }

        public DeadliftRefereeState State { get; private set; } = DeadliftRefereeState.AwaitingLiftOff;
        public bool DownGiven { get; private set; }
        public ulong DownTick { get; private set; }
        public IReadOnlyList<DeadliftFaultRecord> Faults => _faults;
        public bool Completed => State == DeadliftRefereeState.Completed;
        public bool GoodLift => Completed && _faults.Count == 0;
        public double MaxBarRiseM => double.IsNegativeInfinity(_maxBarY) ? 0d : _maxBarY - _barRestY;

        public static string RuleReference(DeadliftFault f)
        {
            switch (f)
            {
                case DeadliftFault.DownwardMovement: return "IPF 4.3.1(1)";
                case DeadliftFault.NotErect: return "IPF 4.3.1(2)";
                case DeadliftFault.KneesNotLocked: return "IPF 4.3.1(3)";
                case DeadliftFault.ThighSupport: return "IPF 4.3.1(4)";
                case DeadliftFault.FootMoved: return "IPF 4.3.1(5)";
                case DeadliftFault.LoweredBeforeSignal: return "IPF 4.3.1(6)";
                case DeadliftFault.UncontrolledReturn: return "IPF 4.3.1(7)";
                default: return "IPF 4.3(1)";
            }
        }

        private void Raise(DeadliftFault f, DeadliftObservation o, string detail)
        {
            if (!_raised.Add(f))
                return;
            _faults.Add(new DeadliftFaultRecord(f, o.Tick, o.TimeSeconds, detail));
        }

        private static string F(double v) => v.ToString("F3", CultureInfo.InvariantCulture);

        public void Observe(DeadliftObservation o)
        {
            if (State == DeadliftRefereeState.Completed)
                return;
            bool bothGrips = o.GripL && o.GripR;
            if (Math.Max(o.FootTravelLM, o.FootTravelRM) > DeadliftRulebook.FootStepM)
                Raise(DeadliftFault.FootMoved, o, "foot travel " + F(Math.Max(o.FootTravelLM, o.FootTravelRM)) + " m");

            switch (State)
            {
                case DeadliftRefereeState.AwaitingLiftOff:
                    if (o.PlateN < DeadliftRulebook.LiftOffPlateN && o.BarY > _barRestY + 0.002)
                    {
                        State = DeadliftRefereeState.Ascent;
                        _liftOffTime = o.TimeSeconds;
                        _maxBarY = o.BarY;
                    }
                    break;

                case DeadliftRefereeState.Ascent:
                    _maxBarY = Math.Max(_maxBarY, o.BarY);
                    // 4.3.1(1) applies before the final position; after it, only lowering before Down (4.3.1(6)).
                    if (!_finalReached && o.BarVy < -DeadliftRulebook.DownwardSpeedMps && _maxBarY - o.BarY >= DeadliftRulebook.DownwardDisplacementM)
                        Raise(DeadliftFault.DownwardMovement, o, "bar " + F(_maxBarY - o.BarY) + " m below its highest point at " + F(o.BarVy) + " m/s");
                    if (_finalReached && _finalBarY - o.BarY > DeadliftRulebook.LoweringBeforeSignalM)
                        Raise(DeadliftFault.LoweredBeforeSignal, o, "bar " + F(_finalBarY - o.BarY) + " m below the finished position before Down");
                    if (o.ThighBarN > DeadliftRulebook.ThighSupportForceN)
                        Raise(DeadliftFault.ThighSupport, o, "bar on thighs " + F(o.ThighBarN) + " N");
                    if (!bothGrips || o.PlateN > DeadliftRulebook.RestingPlateFraction * _barWeightN)
                    {
                        Raise(DeadliftFault.NotCompleted, o, bothGrips ? "bar returned to the platform before completion" : "grip lost during the ascent");
                        State = DeadliftRefereeState.Completed;
                        break;
                    }
                    bool finished = Math.Max(o.KneeFlexionLDeg, o.KneeFlexionRDeg) <= DeadliftRulebook.KneesLockedMaxFlexionDeg &&
                                    o.TorsoDeg >= DeadliftRulebook.ErectTorsoMinDeg &&
                                    o.ShoulderAheadOfHipM <= DeadliftRulebook.ShouldersBackToleranceM;
                    if (finished && !_finalReached)
                    {
                        _finalReached = true;
                        _finalBarY = o.BarY;
                    }
                    _still = o.BarSpeed < DeadliftRulebook.MotionlessSpeedMps ? _still + 1 : 0;
                    if (_still >= _motionlessTicks)
                    {
                        if (finished)
                        {
                            DownGiven = true;
                            DownTick = o.Tick;
                            _lockoutBarY = o.BarY;
                            State = DeadliftRefereeState.AwaitingDown;
                        }
                    }
                    if (o.TimeSeconds - _liftOffTime > _completionTimeoutSeconds)
                    {
                        if (Math.Max(o.KneeFlexionLDeg, o.KneeFlexionRDeg) > DeadliftRulebook.KneesLockedMaxFlexionDeg)
                            Raise(DeadliftFault.KneesNotLocked, o, "knee flexion " + F(Math.Max(o.KneeFlexionLDeg, o.KneeFlexionRDeg)) + " deg");
                        if (o.TorsoDeg < DeadliftRulebook.ErectTorsoMinDeg || o.ShoulderAheadOfHipM > DeadliftRulebook.ShouldersBackToleranceM)
                            Raise(DeadliftFault.NotErect, o, "torso " + F(o.TorsoDeg) + " deg, shoulders ahead " + F(o.ShoulderAheadOfHipM) + " m");
                        Raise(DeadliftFault.NotCompleted, o, "no final position within " + F(_completionTimeoutSeconds) + " s");
                        State = DeadliftRefereeState.Completed;
                    }
                    break;

                case DeadliftRefereeState.AwaitingDown:
                    // The signal is given on entry; the lifter must not have lowered before it. Lowering is judged
                    // against the bar height at the signal.
                    State = DeadliftRefereeState.Lowering;
                    break;

                case DeadliftRefereeState.Lowering:
                    if (!bothGrips && o.PlateN < DeadliftRulebook.RestingPlateFraction * _barWeightN)
                        Raise(DeadliftFault.UncontrolledReturn, o, "a grip released before the bar rested on the platform");
                    if (o.PlateN >= DeadliftRulebook.RestingPlateFraction * _barWeightN)
                    {
                        if (-o.BarVy > DeadliftRulebook.UncontrolledLandingMps)
                            Raise(DeadliftFault.UncontrolledReturn, o, "landing at " + F(-o.BarVy) + " m/s");
                        State = DeadliftRefereeState.Completed;
                    }
                    break;
            }
        }

        /// <summary>A lowering that starts before the Down signal: called by the station when the controller lowers without it.</summary>
        public void ObserveLoweringWithoutSignal(DeadliftObservation o)
        {
            if (!DownGiven && State == DeadliftRefereeState.Ascent && o.BarVy < -DeadliftRulebook.DownwardSpeedMps)
                Raise(DeadliftFault.LoweredBeforeSignal, o, "lowering before Down");
        }

        public string Summary()
        {
            var b = new System.Text.StringBuilder();
            b.Append(GoodLift ? "GOOD LIFT" : Completed ? "NO LIFT" : "IN PROGRESS");
            b.Append(string.Format(CultureInfo.InvariantCulture, " | state {0} | Down {1} | max bar rise {2:F3} m", State, DownGiven ? "tick " + DownTick : "not given", MaxBarRiseM));
            foreach (DeadliftFaultRecord f in _faults)
                b.Append(" | ").Append(f.Fault).Append(" (").Append(f.RuleReference).Append(") @").Append(f.Tick).Append(": ").Append(f.Detail);
            return b.ToString();
        }
    }
}
