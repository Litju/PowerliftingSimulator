namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// Values quoted from the IPF Technical Rulebook effective 01 March 2026,
    /// version 3 (section 2.6 p.9, section 4.2 p.22, section 4.2.1 p.24 and
    /// the referee signal table). Everything here is either a rulebook number
    /// or is named as a game approximation of a rule the book states in words.
    /// </summary>
    public static class BenchRulebook
    {
        public const string Source = "IPF Technical Rulebook, effective 01 March 2026, v3";

        // 2.6 Bench.
        public const double BenchMinLengthM = 1.22;
        public const double BenchMinWidthM = 0.29;
        public const double BenchMaxWidthM = 0.32;
        public const double BenchMinHeightM = 0.42;
        public const double BenchMaxHeightM = 0.45;
        public const double UprightRestMinHeightM = 0.75;
        public const double UprightRestMaxHeightM = 1.10;
        public const double MinWidthBetweenRestsM = 1.10;
        public const double HeadBeyondUprightCenterM = 0.22;
        public const double HeadBeyondUprightToleranceM = 0.05;
        public const double SafetyRackMinHeightM = 0.50;

        // 4.2(5): spacing of the hands measured between the forefingers.
        public const double MaxForefingerSpacingM = 0.81;

        // 4.2(6): replace the bar if not in position after five seconds.
        public const double ReplaceAfterSeconds = 5.0;

        // ---- Game approximations of rules the book states in words. ----
        // The book says "motionless" without a number. A bar whose centre and
        // both ends move slower than this, continuously for the window below,
        // is treated as motionless. 2 cm/s is above the measured solver noise
        // of a held bar and well below any deliberate movement.
        public const double MotionlessSpeedMps = 0.020;
        public const double MotionlessWindowSeconds = 0.30;

        // "Straight arms' length, elbows locked": realized elbow flexion, from
        // the bind-straight arm, at or below this angle on both sides.
        public const double LockedElbowMaxFlexionDeg = 10.0;

        // "Properly positioned" bar before Start: horizontal distance between
        // the bar centre and the mid-point of the shoulder joints.
        public const double StartBarOverShoulderToleranceM = 0.14;

        // The bar is "removed from the racks" once it has been continuously
        // off the rests this long; leaving the J-hooks it may brush their
        // lips, which is not a return to the rests. A return must be
        // sustained for the second window.
        public const double LiftOffClearSeconds = 0.30;
        public const double ReturnedToRestsSeconds = 0.10;

        // Contact is judged from measured normal force, not overlap. A region
        // carrying less than this is treated as not in contact, and a loss has
        // to persist this many ticks before it is a raise (one or two ticks of
        // solver contact chatter are not a lifter movement).
        public const double ContactForceFloorN = 5.0;
        public const int ContactLossTicks = 3;

        // Geometric contact. A region whose collider is within this distance
        // of its surface (bench pad, platform) touches it, whatever load it
        // carries. Game approximation: 5 mm is half PhysX's default 10 mm
        // contact offset and well below any deliberate lift of a head, hip or
        // heel. Either positive force or this geometry counts as contact.
        public const double ContactGapToleranceM = 0.005;

        // Bar touch on the chest or abdomen.
        public const double ChestTouchForceN = 5.0;

        // 4.2.1(5) downward movement of the whole bar during press-out: both
        // ends descending faster than this ...
        public const double PressOutDownwardSpeedMps = 0.020;

        // ... and the whole bar having lost at least this much height from its
        // highest press-out point.
        public const double PressOutDownwardDisplacementM = 0.010;

        // 4.2.1(2) lateral movement of the hands on the bar.
        public const double HandLateralSlipM = 0.010;

        // 4.2.1(1) a descent before Start: bar centre lower than its Start
        // reference by more than this before the Start signal.
        public const double PrematureDescentM = 0.03;

        // 4.2.1(3) sinking the bar after it is motionless on the chest.
        public const double SinkAfterMotionlessM = 0.012;

        // 4.2.1(4) upper body thrust: thorax rising faster than this while
        // the bar is still in chest contact after Press.
        public const double UpperBodyThrustMps = 0.12;

        // Failure to complete: no new press-out height for this long after
        // Press, or the bar back on the chest after it had left it.
        public const double PressStallSeconds = 3.0;

        // A descent that reverses without a touch.
        public const double MissedTouchReboundM = 0.02;
    }
}
