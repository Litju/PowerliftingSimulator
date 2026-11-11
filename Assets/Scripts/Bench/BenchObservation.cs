using System;

namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// One completed physics tick as the bench referee sees it. Every field is
    /// measured from the solved bodies, joints and contact impulses of that
    /// tick; nothing is read from the controller's phase or its references.
    ///
    /// Frame: Unity world, metres, seconds, newtons, +Y up. Forces are the
    /// tick-averaged normal force (sum of solver normal impulses / tick).
    /// </summary>
    public readonly struct BenchObservation
    {
        public BenchObservation(
            ulong tick,
            double timeSeconds,
            double barCenterY,
            double barHorizontalOffsetFromShouldersM,
            double barSpeedMps,
            double barLeftEndVelocityY,
            double barRightEndVelocityY,
            double barLeftEndY,
            double barRightEndY,
            double leftElbowFlexionDeg,
            double rightElbowFlexionDeg,
            bool leftElbowAtDepth,
            bool rightElbowAtDepth,
            double headBenchForceN,
            double shouldersBenchForceN,
            double buttocksBenchForceN,
            double leftFootFloorForceN,
            double rightFootFloorForceN,
            bool leftFootFlat,
            bool rightFootFlat,
            double feetOnBenchForceN,
            double barChestForceN,
            double barBeltForceN,
            double barRackForceN,
            double barSafetyForceN,
            bool leftGripAttached,
            bool rightGripAttached,
            double leftHandSlipM,
            double rightHandSlipM,
            double forefingerSpacingM,
            bool reverseGrip,
            bool spotterContact,
            double thoraxVelocityY,
            double headBenchGapM = double.NaN,
            double shouldersBenchGapM = double.NaN,
            double buttocksBenchGapM = double.NaN,
            double leftFootFloorGapM = double.NaN,
            double rightFootFloorGapM = double.NaN)
        {
            Tick = tick;
            TimeSeconds = timeSeconds;
            BarCenterY = barCenterY;
            BarHorizontalOffsetFromShouldersM = barHorizontalOffsetFromShouldersM;
            BarSpeedMps = barSpeedMps;
            BarLeftEndVelocityY = barLeftEndVelocityY;
            BarRightEndVelocityY = barRightEndVelocityY;
            BarLeftEndY = barLeftEndY;
            BarRightEndY = barRightEndY;
            LeftElbowFlexionDeg = leftElbowFlexionDeg;
            RightElbowFlexionDeg = rightElbowFlexionDeg;
            LeftElbowAtDepth = leftElbowAtDepth;
            RightElbowAtDepth = rightElbowAtDepth;
            HeadBenchForceN = headBenchForceN;
            ShouldersBenchForceN = shouldersBenchForceN;
            ButtocksBenchForceN = buttocksBenchForceN;
            LeftFootFloorForceN = leftFootFloorForceN;
            RightFootFloorForceN = rightFootFloorForceN;
            LeftFootFlat = leftFootFlat;
            RightFootFlat = rightFootFlat;
            FeetOnBenchForceN = feetOnBenchForceN;
            BarChestForceN = barChestForceN;
            BarBeltForceN = barBeltForceN;
            BarRackForceN = barRackForceN;
            BarSafetyForceN = barSafetyForceN;
            LeftGripAttached = leftGripAttached;
            RightGripAttached = rightGripAttached;
            LeftHandSlipM = leftHandSlipM;
            RightHandSlipM = rightHandSlipM;
            ForefingerSpacingM = forefingerSpacingM;
            ReverseGrip = reverseGrip;
            SpotterContact = spotterContact;
            ThoraxVelocityY = thoraxVelocityY;
            HeadBenchGapM = headBenchGapM;
            ShouldersBenchGapM = shouldersBenchGapM;
            ButtocksBenchGapM = buttocksBenchGapM;
            LeftFootFloorGapM = leftFootFloorGapM;
            RightFootFloorGapM = rightFootFloorGapM;
        }

        public ulong Tick { get; }
        public double TimeSeconds { get; }
        public double BarCenterY { get; }
        public double BarHorizontalOffsetFromShouldersM { get; }
        public double BarSpeedMps { get; }
        public double BarLeftEndVelocityY { get; }
        public double BarRightEndVelocityY { get; }
        public double BarLeftEndY { get; }
        public double BarRightEndY { get; }
        public double LeftElbowFlexionDeg { get; }
        public double RightElbowFlexionDeg { get; }

        /// <summary>Underside of the elbow level with or below the top of the shoulder joint (4.2(8)).</summary>
        public bool LeftElbowAtDepth { get; }
        public bool RightElbowAtDepth { get; }
        public double HeadBenchForceN { get; }
        public double ShouldersBenchForceN { get; }
        public double ButtocksBenchForceN { get; }
        public double LeftFootFloorForceN { get; }
        public double RightFootFloorForceN { get; }
        public bool LeftFootFlat { get; }
        public bool RightFootFlat { get; }
        public double FeetOnBenchForceN { get; }
        public double BarChestForceN { get; }
        public double BarBeltForceN { get; }
        public double BarRackForceN { get; }
        public double BarSafetyForceN { get; }
        public bool LeftGripAttached { get; }
        public bool RightGripAttached { get; }
        public double LeftHandSlipM { get; }
        public double RightHandSlipM { get; }
        public double ForefingerSpacingM { get; }
        public bool ReverseGrip { get; }
        public bool SpotterContact { get; }
        public double ThoraxVelocityY { get; }

        /// <summary>
        /// Vertical clearance, metres, of the body region's collider above
        /// the bench pad (head, shoulders, buttocks) or the platform
        /// (feet); 0 when touching or overlapping. NaN when not observed, in
        /// which case contact falls back to the force evidence alone.
        /// </summary>
        public double HeadBenchGapM { get; }
        public double ShouldersBenchGapM { get; }
        public double ButtocksBenchGapM { get; }
        public double LeftFootFloorGapM { get; }
        public double RightFootFloorGapM { get; }

        public bool BarMoving => BarSpeedMps > BenchRulebook.MotionlessSpeedMps ||
            Math.Abs(BarLeftEndVelocityY) > BenchRulebook.MotionlessSpeedMps ||
            Math.Abs(BarRightEndVelocityY) > BenchRulebook.MotionlessSpeedMps;

        public bool ElbowsLocked =>
            LeftElbowFlexionDeg <= BenchRulebook.LockedElbowMaxFlexionDeg &&
            RightElbowFlexionDeg <= BenchRulebook.LockedElbowMaxFlexionDeg;

        public bool BothElbowsAtDepth => LeftElbowAtDepth && RightElbowAtDepth;

        public bool BothGripsAttached => LeftGripAttached && RightGripAttached;

        public bool GripLegal => BothGripsAttached && !ReverseGrip &&
            ForefingerSpacingM <= BenchRulebook.MaxForefingerSpacingM;

        // Rule-level contact (4.2(2), 4.2.1(2), 4.2.1(9)) is geometric: the
        // region touches its surface. A positive normal force proves contact,
        // but zero force does not prove separation - a flat foot resting on
        // the platform may carry nothing while the legs push elsewhere, and
        // feet may move while staying flat. Load-bearing is kept separately as
        // a diagnostic and is never a rule input on its own.
        public bool HeadInContact => Touching(HeadBenchForceN, HeadBenchGapM);
        public bool ShouldersInContact => Touching(ShouldersBenchForceN, ShouldersBenchGapM);
        public bool ButtocksInContact => Touching(ButtocksBenchForceN, ButtocksBenchGapM);

        public bool LeftFootPlanted => LeftFootFlat && Touching(LeftFootFloorForceN, LeftFootFloorGapM);
        public bool RightFootPlanted => RightFootFlat && Touching(RightFootFloorForceN, RightFootFloorGapM);

        /// <summary>Diagnostic only: the region carries at least the contact force floor.</summary>
        public bool HeadLoadBearing => HeadBenchForceN >= BenchRulebook.ContactForceFloorN;
        public bool ShouldersLoadBearing => ShouldersBenchForceN >= BenchRulebook.ContactForceFloorN;
        public bool ButtocksLoadBearing => ButtocksBenchForceN >= BenchRulebook.ContactForceFloorN;
        public bool LeftFootLoadBearing => LeftFootFloorForceN >= BenchRulebook.ContactForceFloorN;
        public bool RightFootLoadBearing => RightFootFloorForceN >= BenchRulebook.ContactForceFloorN;

        private static bool Touching(double forceN, double gapM) =>
            forceN >= BenchRulebook.ContactForceFloorN || gapM <= BenchRulebook.ContactGapToleranceM;

        public bool BarOnChest => BarChestForceN >= BenchRulebook.ChestTouchForceN;
        public bool BarOnBelt => BarBeltForceN >= BenchRulebook.ChestTouchForceN;
        public bool BarOnRack => BarRackForceN >= BenchRulebook.ContactForceFloorN;
        public bool BarOnSafety => BarSafetyForceN >= BenchRulebook.ContactForceFloorN;
    }
}
