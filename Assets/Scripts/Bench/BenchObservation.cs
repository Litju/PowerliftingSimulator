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
            double thoraxVelocityY)
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

        public bool HeadInContact => HeadBenchForceN >= BenchRulebook.ContactForceFloorN;
        public bool ShouldersInContact => ShouldersBenchForceN >= BenchRulebook.ContactForceFloorN;
        public bool ButtocksInContact => ButtocksBenchForceN >= BenchRulebook.ContactForceFloorN;

        public bool LeftFootPlanted => LeftFootFlat && LeftFootFloorForceN >= BenchRulebook.ContactForceFloorN;
        public bool RightFootPlanted => RightFootFlat && RightFootFloorForceN >= BenchRulebook.ContactForceFloorN;

        public bool BarOnChest => BarChestForceN >= BenchRulebook.ChestTouchForceN;
        public bool BarOnBelt => BarBeltForceN >= BenchRulebook.ChestTouchForceN;
        public bool BarOnRack => BarRackForceN >= BenchRulebook.ContactForceFloorN;
        public bool BarOnSafety => BarSafetyForceN >= BenchRulebook.ContactForceFloorN;
    }
}
