using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public enum SquatFailureKind : byte
    {
        NONE,
        BALANCE_LOSS,
        DESCENT_COLLAPSE,
        FAILED_REVERSAL,
        MID_ASCENT_STALL,
        BAR_REVERSAL,
        POSTURE_OR_BAR_LOSS,
        FAILED_LOCKOUT
    }

    public enum SquatFailureDirection : byte
    {
        NONE,
        FORWARD,
        BACKWARD,
        LEFT,
        RIGHT
    }

    public enum SquatFailureDetailKind : byte
    {
        NONE,
        TRUNK_HARD_LIMIT,
        CRITICAL_JOINT_LIMIT,
        SADDLE_BROKEN,
        SADDLE_SEPARATION,
        BAR_COUPLING_LOSS
    }

    public enum SquatFailureEvidenceStatus : byte
    {
        EVALUABLE,
        INSUFFICIENT_EVIDENCE,
        INVALID_TRACE,
        INCOMPLETE_ATTEMPT
    }

    public enum SquatFailureResultKind : byte
    {
        UNDETERMINED,
        NO_PHYSICAL_FAILURE,
        PHYSICAL_FAILURE
    }

    public enum SquatFailureCalibrationStatus : byte
    {
        FROZEN_GAME_BOUND,
        EXISTING_QUALIFIED_BOUND,
        PROVISIONAL_GAME_CALIBRATION,
        REQUIRES_GAM13_CALIBRATION
    }

    [Flags]
    public enum SquatFailureEvidenceChannel : uint
    {
        NONE = 0,
        COM_POSITION = 1u << 0,
        COM_VELOCITY = 1u << 1,
        SUPPORT_BOUNDS = 1u << 2,
        BAR_POSITION = 1u << 3,
        BAR_LINEAR_VELOCITY = 1u << 4,
        PELVIS_POSITION = 1u << 5,
        PELVIS_LINEAR_VELOCITY = 1u << 6,
        DEPTH_LANDMARKS = 1u << 7,
        JOINT_KINEMATICS = 1u << 8,
        JOINT_LIMITS = 1u << 9,
        TRUNK_KINEMATICS = 1u << 10,
        DRIVE_DEMAND = 1u << 11,
        FOOT_CONTACT = 1u << 12,
        SADDLE_STATE = 1u << 13,
        PLAYER_DRIVE_INTENT = 1u << 14
    }

    public readonly struct SquatFailureCalibrationDescriptor
    {
        public SquatFailureCalibrationDescriptor(
            string name,
            string unit,
            double value,
            string category,
            string source,
            string rationale,
            string version,
            SquatFailureCalibrationStatus status)
        {
            RequireText(name, nameof(name));
            RequireText(unit, nameof(unit));
            RequireText(category, nameof(category));
            RequireText(source, nameof(source));
            RequireText(rationale, nameof(rationale));
            RequireText(version, nameof(version));
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            Name = name;
            Unit = unit;
            Value = value;
            Category = category;
            Source = source;
            Rationale = rationale;
            Version = version;
            Status = status;
        }

        public string Name { get; }
        public string Unit { get; }
        public double Value { get; }
        public string Category { get; }
        public string Source { get; }
        public string Rationale { get; }
        public string Version { get; }
        public SquatFailureCalibrationStatus Status { get; }

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Failure calibration metadata is required.", name);
        }
    }

    /// <summary>
    /// Immutable, named calibration for the GAM-12 Phase 3 physical failure
    /// detector. Values are game/engine boundaries, not human safety limits.
    /// </summary>
    public sealed class SquatFailureCalibration
    {
        public const string FailureModelVersion = "GAM12_P3A1_FAILURE_MODEL_V1";
        public const string DefaultVersion = "GAM12_P3A1_FAILURE_CALIBRATION_PROVISIONAL_V1";
        public const string DefaultPrecedenceVersion = "GAM12_P3_FIRST_IRREVERSIBLE_PRECEDENCE_V1";

        public const float DefaultBalanceSupportMarginFailureM = -0.01f;
        public const float DefaultBalanceOutwardComVelocityMps = 0.03f;
        public const int DefaultBalancePersistenceTicks = 12;
        public const float DefaultPhysicalMotionVelocityMps = 0.01f;
        public const float DefaultPhysicalMotionDisplacementM = 0.002f;
        public const float DefaultBottomTransitionVelocityMps = 0.01f;
        public const float DefaultBottomTransitionDisplacementM = 0.002f;
        public const float DefaultDescentCollapseVelocityMps = 0.50f;
        public const int DefaultDescentCollapsePersistenceTicks = 3;
        public const float DefaultDescentCollapseDemandThreshold01 = 0.95f;
        public const float DefaultDriveAttemptMinimum01 = 0.50f;
        public const float DefaultReversalRecoveryVelocityMps = 0.02f;
        public const float DefaultReversalRecoveryDisplacementM = 0.02f;
        public const int DefaultReversalRecoveryPersistenceTicks = 2;
        public const int DefaultReversalTimeoutTicks = 35;
        public const float DefaultAscentEstablishmentVelocityMps = 0.02f;
        public const int DefaultAscentEstablishmentPersistenceTicks = 3;
        public const float DefaultAscentEstablishmentDisplacementM = 0.02f;
        public const float DefaultStallLowAscentVelocityMps = 0.015f;
        public const float DefaultStallHighDemandThreshold01 = 0.80f;
        public const float DefaultStallNoProgressDisplacementM = 0.005f;
        public const int DefaultStallDwellTicks = 35;
        public const float DefaultStallRecoveryVelocityMps = 0.03f;
        public const float DefaultStallRecoveryDisplacementM = 0.02f;
        public const float DefaultBarReversalVelocityMps = 0.02f;
        public const float DefaultBarReversalSampleDisplacementM = 0.002f;
        public const float DefaultBarReversalDisplacementM = 0.01f;
        public const int DefaultBarReversalPersistenceTicks = 2;
        public const float DefaultTrunkWarningLimitRadians = 0.5235988f;
        public const float DefaultTrunkHardLimitRadians = 0.7853982f;
        public const float DefaultCriticalJointLimitProximity = 0.98f;
        public const int DefaultPosturePersistenceTicks = 2;
        public const int DefaultSaddlePersistenceTicks = 1;
        public const float DefaultSaddleSeparationFailureM = 0.12f;
        public const float DefaultLockoutKneeToleranceRadians = 0.08726646f;
        public const float DefaultLockoutHipToleranceRadians = 0.17453292f;
        public const float DefaultLockoutTrunkToleranceRadians = 0.17453292f;
        public const float DefaultLockoutBarStillVelocityMps = 0.02f;
        public const float DefaultLockoutBarStillAngularVelocityRadS = 0.20f;
        public const float DefaultLockoutHeightToleranceM = 0.05f;
        public const int DefaultLockoutCompletionTimeoutTicks = 60;
        public const int DefaultPreFailureEvidenceCapacity = 32;

        private readonly ReadOnlyCollection<SquatFailureCalibrationDescriptor> _descriptors;

        public SquatFailureCalibration(
            string version = DefaultVersion,
            float balanceSupportMarginFailureM = DefaultBalanceSupportMarginFailureM,
            float balanceOutwardComVelocityMps = DefaultBalanceOutwardComVelocityMps,
            int balancePersistenceTicks = DefaultBalancePersistenceTicks,
            float physicalMotionVelocityMps = DefaultPhysicalMotionVelocityMps,
            float physicalMotionDisplacementM = DefaultPhysicalMotionDisplacementM,
            float bottomTransitionVelocityMps = DefaultBottomTransitionVelocityMps,
            float bottomTransitionDisplacementM = DefaultBottomTransitionDisplacementM,
            float descentCollapseVelocityMps = DefaultDescentCollapseVelocityMps,
            int descentCollapsePersistenceTicks = DefaultDescentCollapsePersistenceTicks,
            float descentCollapseDemandThreshold01 = DefaultDescentCollapseDemandThreshold01,
            float driveAttemptMinimum01 = DefaultDriveAttemptMinimum01,
            float reversalRecoveryVelocityMps = DefaultReversalRecoveryVelocityMps,
            float reversalRecoveryDisplacementM = DefaultReversalRecoveryDisplacementM,
            int reversalRecoveryPersistenceTicks = DefaultReversalRecoveryPersistenceTicks,
            int reversalTimeoutTicks = DefaultReversalTimeoutTicks,
            float ascentEstablishmentVelocityMps = DefaultAscentEstablishmentVelocityMps,
            int ascentEstablishmentPersistenceTicks = DefaultAscentEstablishmentPersistenceTicks,
            float ascentEstablishmentDisplacementM = DefaultAscentEstablishmentDisplacementM,
            float stallLowAscentVelocityMps = DefaultStallLowAscentVelocityMps,
            float stallHighDemandThreshold01 = DefaultStallHighDemandThreshold01,
            float stallNoProgressDisplacementM = DefaultStallNoProgressDisplacementM,
            int stallDwellTicks = DefaultStallDwellTicks,
            float stallRecoveryVelocityMps = DefaultStallRecoveryVelocityMps,
            float stallRecoveryDisplacementM = DefaultStallRecoveryDisplacementM,
            float barReversalVelocityMps = DefaultBarReversalVelocityMps,
            float barReversalSampleDisplacementM = DefaultBarReversalSampleDisplacementM,
            float barReversalDisplacementM = DefaultBarReversalDisplacementM,
            int barReversalPersistenceTicks = DefaultBarReversalPersistenceTicks,
            float trunkWarningLimitRadians = DefaultTrunkWarningLimitRadians,
            float trunkHardLimitRadians = DefaultTrunkHardLimitRadians,
            float criticalJointLimitProximity = DefaultCriticalJointLimitProximity,
            int posturePersistenceTicks = DefaultPosturePersistenceTicks,
            int saddlePersistenceTicks = DefaultSaddlePersistenceTicks,
            float saddleSeparationFailureM = DefaultSaddleSeparationFailureM,
            float lockoutKneeToleranceRadians = DefaultLockoutKneeToleranceRadians,
            float lockoutHipToleranceRadians = DefaultLockoutHipToleranceRadians,
            float lockoutTrunkToleranceRadians = DefaultLockoutTrunkToleranceRadians,
            float lockoutBarStillVelocityMps = DefaultLockoutBarStillVelocityMps,
            float lockoutBarStillAngularVelocityRadS = DefaultLockoutBarStillAngularVelocityRadS,
            float lockoutHeightToleranceM = DefaultLockoutHeightToleranceM,
            int lockoutCompletionTimeoutTicks = DefaultLockoutCompletionTimeoutTicks,
            int preFailureEvidenceCapacity = DefaultPreFailureEvidenceCapacity)
        {
            RequireText(version, nameof(version));
            RequireNegative(balanceSupportMarginFailureM, nameof(balanceSupportMarginFailureM));
            RequirePositive(balanceOutwardComVelocityMps, nameof(balanceOutwardComVelocityMps));
            RequirePositive(balancePersistenceTicks, nameof(balancePersistenceTicks));
            RequirePositive(physicalMotionVelocityMps, nameof(physicalMotionVelocityMps));
            RequirePositive(physicalMotionDisplacementM, nameof(physicalMotionDisplacementM));
            RequirePositive(bottomTransitionVelocityMps, nameof(bottomTransitionVelocityMps));
            RequirePositive(bottomTransitionDisplacementM, nameof(bottomTransitionDisplacementM));
            RequirePositive(descentCollapseVelocityMps, nameof(descentCollapseVelocityMps));
            RequirePositive(descentCollapsePersistenceTicks, nameof(descentCollapsePersistenceTicks));
            RequireUnitInterval(descentCollapseDemandThreshold01, nameof(descentCollapseDemandThreshold01));
            RequireUnitInterval(driveAttemptMinimum01, nameof(driveAttemptMinimum01));
            RequirePositive(reversalRecoveryVelocityMps, nameof(reversalRecoveryVelocityMps));
            RequirePositive(reversalRecoveryDisplacementM, nameof(reversalRecoveryDisplacementM));
            RequirePositive(reversalRecoveryPersistenceTicks, nameof(reversalRecoveryPersistenceTicks));
            RequirePositive(reversalTimeoutTicks, nameof(reversalTimeoutTicks));
            RequirePositive(ascentEstablishmentVelocityMps, nameof(ascentEstablishmentVelocityMps));
            RequirePositive(ascentEstablishmentPersistenceTicks, nameof(ascentEstablishmentPersistenceTicks));
            RequireNonNegative(ascentEstablishmentDisplacementM, nameof(ascentEstablishmentDisplacementM));
            RequirePositive(stallLowAscentVelocityMps, nameof(stallLowAscentVelocityMps));
            RequireUnitInterval(stallHighDemandThreshold01, nameof(stallHighDemandThreshold01));
            RequirePositive(stallNoProgressDisplacementM, nameof(stallNoProgressDisplacementM));
            RequirePositive(stallDwellTicks, nameof(stallDwellTicks));
            RequirePositive(stallRecoveryVelocityMps, nameof(stallRecoveryVelocityMps));
            RequirePositive(stallRecoveryDisplacementM, nameof(stallRecoveryDisplacementM));
            RequirePositive(barReversalVelocityMps, nameof(barReversalVelocityMps));
            RequirePositive(barReversalSampleDisplacementM, nameof(barReversalSampleDisplacementM));
            RequirePositive(barReversalDisplacementM, nameof(barReversalDisplacementM));
            RequirePositive(barReversalPersistenceTicks, nameof(barReversalPersistenceTicks));
            RequireNonNegative(trunkWarningLimitRadians, nameof(trunkWarningLimitRadians));
            RequirePositive(trunkHardLimitRadians, nameof(trunkHardLimitRadians));
            if (trunkHardLimitRadians <= trunkWarningLimitRadians)
                throw new ArgumentOutOfRangeException(nameof(trunkHardLimitRadians));
            RequireUnitInterval(criticalJointLimitProximity, nameof(criticalJointLimitProximity));
            RequirePositive(posturePersistenceTicks, nameof(posturePersistenceTicks));
            RequirePositive(saddlePersistenceTicks, nameof(saddlePersistenceTicks));
            RequirePositive(saddleSeparationFailureM, nameof(saddleSeparationFailureM));
            RequirePositive(lockoutKneeToleranceRadians, nameof(lockoutKneeToleranceRadians));
            RequirePositive(lockoutHipToleranceRadians, nameof(lockoutHipToleranceRadians));
            RequirePositive(lockoutTrunkToleranceRadians, nameof(lockoutTrunkToleranceRadians));
            RequirePositive(lockoutBarStillVelocityMps, nameof(lockoutBarStillVelocityMps));
            RequirePositive(lockoutBarStillAngularVelocityRadS, nameof(lockoutBarStillAngularVelocityRadS));
            RequirePositive(lockoutHeightToleranceM, nameof(lockoutHeightToleranceM));
            RequirePositive(lockoutCompletionTimeoutTicks, nameof(lockoutCompletionTimeoutTicks));
            RequirePositive(preFailureEvidenceCapacity, nameof(preFailureEvidenceCapacity));
            if (stallLowAscentVelocityMps >= ascentEstablishmentVelocityMps)
                throw new ArgumentOutOfRangeException(nameof(stallLowAscentVelocityMps));

            Version = version;
            BalanceSupportMarginFailureM = balanceSupportMarginFailureM;
            BalanceOutwardComVelocityMps = balanceOutwardComVelocityMps;
            BalancePersistenceTicks = balancePersistenceTicks;
            PhysicalMotionVelocityMps = physicalMotionVelocityMps;
            PhysicalMotionDisplacementM = physicalMotionDisplacementM;
            BottomTransitionVelocityMps = bottomTransitionVelocityMps;
            BottomTransitionDisplacementM = bottomTransitionDisplacementM;
            DescentCollapseVelocityMps = descentCollapseVelocityMps;
            DescentCollapsePersistenceTicks = descentCollapsePersistenceTicks;
            DescentCollapseDemandThreshold01 = descentCollapseDemandThreshold01;
            DriveAttemptMinimum01 = driveAttemptMinimum01;
            ReversalRecoveryVelocityMps = reversalRecoveryVelocityMps;
            ReversalRecoveryDisplacementM = reversalRecoveryDisplacementM;
            ReversalRecoveryPersistenceTicks = reversalRecoveryPersistenceTicks;
            ReversalTimeoutTicks = reversalTimeoutTicks;
            AscentEstablishmentVelocityMps = ascentEstablishmentVelocityMps;
            AscentEstablishmentPersistenceTicks = ascentEstablishmentPersistenceTicks;
            AscentEstablishmentDisplacementM = ascentEstablishmentDisplacementM;
            StallLowAscentVelocityMps = stallLowAscentVelocityMps;
            StallHighDemandThreshold01 = stallHighDemandThreshold01;
            StallNoProgressDisplacementM = stallNoProgressDisplacementM;
            StallDwellTicks = stallDwellTicks;
            StallRecoveryVelocityMps = stallRecoveryVelocityMps;
            StallRecoveryDisplacementM = stallRecoveryDisplacementM;
            BarReversalVelocityMps = barReversalVelocityMps;
            BarReversalSampleDisplacementM = barReversalSampleDisplacementM;
            BarReversalDisplacementM = barReversalDisplacementM;
            BarReversalPersistenceTicks = barReversalPersistenceTicks;
            TrunkWarningLimitRadians = trunkWarningLimitRadians;
            TrunkHardLimitRadians = trunkHardLimitRadians;
            CriticalJointLimitProximity = criticalJointLimitProximity;
            PosturePersistenceTicks = posturePersistenceTicks;
            SaddlePersistenceTicks = saddlePersistenceTicks;
            SaddleSeparationFailureM = saddleSeparationFailureM;
            LockoutKneeToleranceRadians = lockoutKneeToleranceRadians;
            LockoutHipToleranceRadians = lockoutHipToleranceRadians;
            LockoutTrunkToleranceRadians = lockoutTrunkToleranceRadians;
            LockoutBarStillVelocityMps = lockoutBarStillVelocityMps;
            LockoutBarStillAngularVelocityRadS = lockoutBarStillAngularVelocityRadS;
            LockoutHeightToleranceM = lockoutHeightToleranceM;
            LockoutCompletionTimeoutTicks = lockoutCompletionTimeoutTicks;
            PreFailureEvidenceCapacity = preFailureEvidenceCapacity;
            _descriptors = Array.AsReadOnly(CreateDescriptors());
        }

        public static SquatFailureCalibration Default { get; } = new SquatFailureCalibration();

        public string FailureModel => FailureModelVersion;
        public string Version { get; }
        public SquatFailureCalibrationStatus Status => SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION;
        public bool RequiresGAM13Calibration => true;
        public string PrecedenceVersion => DefaultPrecedenceVersion;
        public float BalanceSupportMarginFailureM { get; }
        public float BalanceOutwardComVelocityMps { get; }
        public int BalancePersistenceTicks { get; }
        public float PhysicalMotionVelocityMps { get; }
        public float PhysicalMotionDisplacementM { get; }
        public float BottomTransitionVelocityMps { get; }
        public float BottomTransitionDisplacementM { get; }
        public float DescentCollapseVelocityMps { get; }
        public int DescentCollapsePersistenceTicks { get; }
        public float DescentCollapseDemandThreshold01 { get; }
        public float DriveAttemptMinimum01 { get; }
        public float ReversalRecoveryVelocityMps { get; }
        public float ReversalRecoveryDisplacementM { get; }
        public int ReversalRecoveryPersistenceTicks { get; }
        public int ReversalTimeoutTicks { get; }
        public float AscentEstablishmentVelocityMps { get; }
        public int AscentEstablishmentPersistenceTicks { get; }
        public float AscentEstablishmentDisplacementM { get; }
        public float StallLowAscentVelocityMps { get; }
        public float StallHighDemandThreshold01 { get; }
        public float StallNoProgressDisplacementM { get; }
        public int StallDwellTicks { get; }
        public float StallRecoveryVelocityMps { get; }
        public float StallRecoveryDisplacementM { get; }
        public float BarReversalVelocityMps { get; }
        public float BarReversalSampleDisplacementM { get; }
        public float BarReversalDisplacementM { get; }
        public int BarReversalPersistenceTicks { get; }
        public float TrunkWarningLimitRadians { get; }
        public float TrunkHardLimitRadians { get; }
        public float CriticalJointLimitProximity { get; }
        public int PosturePersistenceTicks { get; }
        public int SaddlePersistenceTicks { get; }
        public float SaddleSeparationFailureM { get; }
        public float LockoutKneeToleranceRadians { get; }
        public float LockoutHipToleranceRadians { get; }
        public float LockoutTrunkToleranceRadians { get; }
        public float LockoutBarStillVelocityMps { get; }
        public float LockoutBarStillAngularVelocityRadS { get; }
        public float LockoutHeightToleranceM { get; }
        public int LockoutCompletionTimeoutTicks { get; }
        public int PreFailureEvidenceCapacity { get; }
        public IReadOnlyList<SquatFailureCalibrationDescriptor> Descriptors => _descriptors;

        private SquatFailureCalibrationDescriptor[] CreateDescriptors()
        {
            const string provisional = "GAME_CALIBRATION";
            const string existing = "EXISTING_QUALIFIED_BOUND";
            const string provisionalStatus = "P3 synthetic/domain fixture only; not heavy-load validated.";
            return new[]
            {
                Descriptor("game_judgment_margin", "m", SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M, "REVERSAL", existing, "The existing 0.005 m game judgment margin; IPF specifies no numeric distance. Legal depth does not gate physical completion.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("balance_support_margin_failure", "m", BalanceSupportMarginFailureM, "BALANCE", provisional, "Ten millimetres beyond the observed support bound is a provisional game failure boundary.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("balance_outward_com_velocity", "m/s", BalanceOutwardComVelocityMps, "BALANCE", provisional, "Outward modeled COM velocity distinguishes a persistent dynamic excursion from a static edge sample.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("balance_persistence", "ticks", BalancePersistenceTicks, "BALANCE", provisional, "Fixed-step persistence rejects one-sample support excursions.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("physical_motion_velocity", "m/s", PhysicalMotionVelocityMps, "EVENT_CONTEXT", provisional, "Small direct velocity boundary for identifying physical descent context.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("physical_motion_displacement", "m", PhysicalMotionDisplacementM, "EVENT_CONTEXT", provisional, "Small direct displacement boundary for identifying physical motion.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("bottom_transition_velocity", "m/s", BottomTransitionVelocityMps, "EVENT_CONTEXT", provisional, "Near-zero raw vertical velocity identifies a physical bottom transition.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("bottom_transition_displacement", "m", BottomTransitionDisplacementM, "EVENT_CONTEXT", provisional, "Direct displacement boundary for a reversal without a zero-velocity sample.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("descent_collapse_velocity", "m/s", DescentCollapseVelocityMps, "DESCENT_COLLAPSE", provisional, "Excessive direct downward bar/pelvis speed; never sufficient without control-loss evidence.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("descent_collapse_persistence", "ticks", DescentCollapsePersistenceTicks, "DESCENT_COLLAPSE", provisional, provisionalStatus, SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("descent_collapse_demand", "1", DescentCollapseDemandThreshold01, "DESCENT_COLLAPSE", provisional, "Modeled authority saturation is a conjunction signal, not an independent failure selector.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("drive_attempt_minimum", "1", DriveAttemptMinimum01, "FAILED_REVERSAL", provisional, "Player drive intent threshold required before calling an unrecovered reversal a failed reversal.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("reversal_recovery_velocity", "m/s", ReversalRecoveryVelocityMps, "FAILED_REVERSAL", provisional, "Direct upward physical recovery threshold.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("reversal_recovery_displacement", "m", ReversalRecoveryDisplacementM, "FAILED_REVERSAL", provisional, "Direct upward displacement alternative to velocity-only recovery.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("reversal_recovery_persistence", "ticks", ReversalRecoveryPersistenceTicks, "FAILED_REVERSAL", provisional, provisionalStatus, SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("reversal_timeout", "ticks", ReversalTimeoutTicks, "FAILED_REVERSAL", provisional, "Bounded no-recovery window; heavy-load behavior belongs to GAM-13.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("ascent_establishment_velocity", "m/s", AscentEstablishmentVelocityMps, "ASCENT_CONTEXT", existing, "Reuse the qualified raw upward-motion event boundary.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("ascent_establishment_persistence", "ticks", AscentEstablishmentPersistenceTicks, "ASCENT_CONTEXT", existing, "Reuse the qualified consecutive upward-sample boundary.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("ascent_establishment_displacement", "m", AscentEstablishmentDisplacementM, "ASCENT_CONTEXT", existing, "Reuse the qualified direct upward-displacement boundary.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("stall_low_ascent_velocity", "m/s", StallLowAscentVelocityMps, "MID_ASCENT_STALL", provisional, "Near-zero raw ascent speed is only a candidate signal and is never sufficient alone.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("stall_high_modeled_demand", "1", StallHighDemandThreshold01, "MID_ASCENT_STALL", provisional, "High modeled game demand is required alongside no progress and dwell.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("stall_no_progress_displacement", "m", StallNoProgressDisplacementM, "MID_ASCENT_STALL", provisional, "Small direct displacement ceiling for a no-progress interval.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("stall_dwell", "ticks", StallDwellTicks, "MID_ASCENT_STALL", provisional, "Terminal dwell; successful sticking that recovers before this window is not a failure.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("stall_recovery_velocity", "m/s", StallRecoveryVelocityMps, "MID_ASCENT_STALL", provisional, "Direct upward velocity that ends a recoverable low-speed interval.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("stall_recovery_displacement", "m", StallRecoveryDisplacementM, "MID_ASCENT_STALL", provisional, "Direct upward displacement that ends a recoverable low-speed interval.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("bar_reversal_velocity", "m/s", BarReversalVelocityMps, "BAR_REVERSAL", provisional, "Direct downward bar velocity boundary distinct from the competition rule violation.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("bar_reversal_sample_displacement", "m", BarReversalSampleDisplacementM, "BAR_REVERSAL", provisional, "Minimum direct per-sample drop that can start a reversal run.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("bar_reversal_displacement", "m", BarReversalDisplacementM, "BAR_REVERSAL", provisional, "Cumulative direct bar drop beyond solver/noise tolerance.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("bar_reversal_persistence", "ticks", BarReversalPersistenceTicks, "BAR_REVERSAL", provisional, provisionalStatus, SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("trunk_warning_limit", "rad", TrunkWarningLimitRadians, "POSTURE", provisional, "Warning band is diagnostic only and cannot latch a physical failure.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("trunk_hard_limit", "rad", TrunkHardLimitRadians, "POSTURE", provisional, "Hard game posture bound, explicitly not an injury or tissue limit.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("critical_joint_limit_proximity", "1", CriticalJointLimitProximity, "POSTURE", provisional, "Configured joint-limit occupancy boundary for a game failure diagnostic.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("posture_persistence", "ticks", PosturePersistenceTicks, "POSTURE", provisional, "Persistence rejects a one-sample posture excursion.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("saddle_persistence", "ticks", SaddlePersistenceTicks, "SADDLE", existing, "Direct saddle coupling evidence is allowed to latch immediately.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("saddle_separation_failure", "m", SaddleSeparationFailureM, "SADDLE", existing, "Reuse the qualified maximum plausible saddle separation boundary.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("lockout_knee_tolerance", "rad", LockoutKneeToleranceRadians, "FAILED_LOCKOUT", existing, "Reuse the qualified bilateral knee lockout proxy.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("lockout_hip_tolerance", "rad", LockoutHipToleranceRadians, "FAILED_LOCKOUT", existing, "Reuse the qualified hip erectness proxy.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("lockout_trunk_tolerance", "rad", LockoutTrunkToleranceRadians, "FAILED_LOCKOUT", existing, "Reuse the qualified trunk erectness proxy.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("lockout_bar_still_velocity", "m/s", LockoutBarStillVelocityMps, "FAILED_LOCKOUT", existing, "Reuse the qualified direct bar stillness proxy.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("lockout_bar_still_angular_velocity", "rad/s", LockoutBarStillAngularVelocityRadS, "FAILED_LOCKOUT", existing, "Reuse the qualified direct bar angular stillness proxy.", SquatFailureCalibrationStatus.EXISTING_QUALIFIED_BOUND),
                Descriptor("lockout_height_tolerance", "m", LockoutHeightToleranceM, "FAILED_LOCKOUT", provisional, "Standing-reference height tolerance prevents a stationary low bar from masquerading as lockout.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION),
                Descriptor("lockout_completion_dwell", "ticks", LockoutCompletionTimeoutTicks, "FAILED_LOCKOUT", provisional, "NOT_USED_BY_CANONICAL_FAILED_LOCKOUT_SELECTION. Retained completion-region dwell provenance only; measured 25 kg evidence shows terminal joint extension and bar settling continue far beyond this dwell, so elapsed time is not irreversible evidence. Canonical FAILED_LOCKOUT is a terminal postcondition. REQUIRES_GAM13_CALIBRATION.", SquatFailureCalibrationStatus.REQUIRES_GAM13_CALIBRATION),
                Descriptor("pre_failure_evidence_capacity", "samples", PreFailureEvidenceCapacity, "EVIDENCE_WINDOW", provisional, "Fixed-capacity ring buffer retained at primary latch.", SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION)
            };
        }

        private SquatFailureCalibrationDescriptor Descriptor(
            string name,
            string unit,
            double value,
            string category,
            string source,
            string rationale,
            SquatFailureCalibrationStatus status) => new SquatFailureCalibrationDescriptor(
            name,
            unit,
            value,
            category,
            source,
            rationale,
            Version,
            status);

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Failure calibration version is required.", name);
        }

        private static void RequireFinite(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireNonNegative(float value, string name)
        {
            RequireFinite(value, name);
            if (value < 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequirePositive(float value, string name)
        {
            RequireFinite(value, name);
            if (value <= 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireNegative(float value, string name)
        {
            RequireFinite(value, name);
            if (value >= 0f)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequirePositive(int value, string name)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireUnitInterval(float value, string name)
        {
            RequireFinite(value, name);
            if (value < 0f || value > 1f)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    /// <summary>
    /// Whether, and how, an authoritative terminal attempt context was
    /// accepted for terminal-postcondition evaluation. A rejected context can
    /// never create a failure; it only records why terminality was unusable.
    /// </summary>
    public enum SquatFailureTerminalContextStatus : byte
    {
        NOT_PROVIDED,
        NOT_TERMINAL,
        TRACE_COVERED,
        REJECTED_MALFORMED,
        REJECTED_UNCOVERED_TICK,
        REJECTED_TIME_MISMATCH
    }

    /// <summary>
    /// Immutable terminal-attempt evidence handed to the physical failure
    /// detector by the authoritative attempt lifecycle.
    /// <para>
    /// FAILED_LOCKOUT is the only terminal postcondition in the P3 ontology:
    /// every other physical failure is a streaming irreversible cause. The
    /// detector therefore needs only terminality, the identity of the terminal
    /// sample, and the lifecycle's own terminal classification. The
    /// classification is lifecycle evidence and never selects a physical
    /// failure class by itself.
    /// </para>
    /// </summary>
    public readonly struct SquatFailureCompletionContext
    {
        private SquatFailureCompletionContext(
            bool isTerminal,
            ulong terminalTick,
            double terminalTimeSeconds,
            SquatAttemptTerminalReason terminalReason)
        {
            IsTerminal = isTerminal;
            TerminalTick = terminalTick;
            TerminalTimeSeconds = terminalTimeSeconds;
            TerminalReason = terminalReason;
        }

        /// <summary>
        /// Pure physical interpretation: the attempt is not authoritatively
        /// over, so no terminal postcondition may be decided.
        /// </summary>
        public static SquatFailureCompletionContext NonTerminal =>
            new SquatFailureCompletionContext(false, 0ul, 0d, SquatAttemptTerminalReason.NONE);

        public static SquatFailureCompletionContext Terminal(
            ulong terminalTick,
            double terminalTimeSeconds,
            SquatAttemptTerminalReason terminalReason) =>
            new SquatFailureCompletionContext(true, terminalTick, terminalTimeSeconds, terminalReason);

        public bool IsTerminal { get; }
        public ulong TerminalTick { get; }
        public double TerminalTimeSeconds { get; }
        public SquatAttemptTerminalReason TerminalReason { get; }

        /// <summary>
        /// Self-consistency only. Whether the terminal sample actually belongs
        /// to an attempt is decided against that attempt's frozen trace.
        /// </summary>
        public bool IsWellFormed => !IsTerminal ||
            (TerminalReason != SquatAttemptTerminalReason.NONE &&
             TerminalTick != SquatAttemptEventTicks.NotAvailable &&
             !double.IsNaN(TerminalTimeSeconds) &&
             !double.IsInfinity(TerminalTimeSeconds) &&
             TerminalTimeSeconds >= 0d);
    }

    /// <summary>
    /// Explicit P3 attempt boundary and standing reference. The physical
    /// detector must not infer either from pre-command settling history.
    /// </summary>
    public readonly struct SquatFailureAttemptContext
    {
        private SquatFailureAttemptContext(
            bool isSpecified,
            ulong attemptStartTick,
            ulong standingReferenceTick)
        {
            IsSpecified = isSpecified;
            AttemptStartTick = attemptStartTick;
            StandingReferenceTick = standingReferenceTick;
        }

        public static SquatFailureAttemptContext Unspecified =>
            new SquatFailureAttemptContext(false, SquatAttemptEventTicks.NotAvailable, SquatAttemptEventTicks.NotAvailable);

        public static SquatFailureAttemptContext ForAttempt(
            ulong attemptStartTick,
            ulong standingReferenceTick) =>
            new SquatFailureAttemptContext(true, attemptStartTick, standingReferenceTick);

        public bool IsSpecified { get; }
        public ulong AttemptStartTick { get; }
        public ulong StandingReferenceTick { get; }
        public bool IsWellFormed => !IsSpecified ||
            (AttemptStartTick != SquatAttemptEventTicks.NotAvailable &&
             StandingReferenceTick != SquatAttemptEventTicks.NotAvailable &&
             StandingReferenceTick <= AttemptStartTick);
    }

    public readonly struct SquatFailureContext
    {
        public SquatFailureContext(
            ulong simulationTick,
            double simulationTimeSeconds,
            SquatState state,
            SquatPhaseDirection direction,
            float sq,
            SquatIntentHeldFlags intentHeld,
            float drive01)
        {
            if (double.IsNaN(simulationTimeSeconds) || double.IsInfinity(simulationTimeSeconds) || simulationTimeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(simulationTimeSeconds));
            if (float.IsNaN(sq) || float.IsInfinity(sq) || sq < 0f || sq > 1f)
                throw new ArgumentOutOfRangeException(nameof(sq));
            if (float.IsNaN(drive01) || float.IsInfinity(drive01) || drive01 < 0f || drive01 > 1f)
                throw new ArgumentOutOfRangeException(nameof(drive01));

            SimulationTick = simulationTick;
            SimulationTimeSeconds = simulationTimeSeconds;
            State = state;
            Direction = direction;
            Sq = sq;
            IntentHeld = intentHeld;
            Drive01 = drive01;
        }

        public SquatFailureContext(
            SquatState state,
            SquatPhaseDirection direction,
            float sq,
            SquatIntentHeldFlags intentHeld,
            float drive01)
            : this(0ul, 0d, state, direction, sq, intentHeld, drive01)
        {
        }

        public ulong SimulationTick { get; }
        public double SimulationTimeSeconds { get; }
        public SquatState State { get; }
        public SquatPhaseDirection Direction { get; }
        public float Sq { get; }
        public SquatIntentHeldFlags IntentHeld { get; }
        public float Drive01 { get; }
    }

    public readonly struct SquatFailureMeasurement
    {
        public SquatFailureMeasurement(string name, string unit, double value)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(unit))
                throw new ArgumentException("Failure measurements require names and units.");
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            Name = name;
            Unit = unit;
            Value = value;
        }

        public string Name { get; }
        public string Unit { get; }
        public double Value { get; }
    }

    public readonly struct SquatFailureThresholdValue
    {
        public SquatFailureThresholdValue(
            string name,
            string unit,
            double value,
            string calibrationVersion,
            SquatFailureCalibrationStatus status)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(unit) || string.IsNullOrEmpty(calibrationVersion))
                throw new ArgumentException("Failure thresholds require complete provenance.");
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value));

            Name = name;
            Unit = unit;
            Value = value;
            CalibrationVersion = calibrationVersion;
            Status = status;
        }

        public string Name { get; }
        public string Unit { get; }
        public double Value { get; }
        public string CalibrationVersion { get; }
        public SquatFailureCalibrationStatus Status { get; }
    }

    public readonly struct SquatFailureCandidate
    {
        internal SquatFailureCandidate(
            SquatFailureKind kind,
            SquatFailureDirection direction,
            SquatFailureDetailKind detail,
            ulong onsetTick,
            double onsetTimeSeconds,
            ulong latchedTick,
            double latchedTimeSeconds,
            SquatFailureContext onsetContext,
            SquatFailureContext latchedContext,
            SquatFailureEvidenceChannel evidenceChannels,
            double measuredValueA,
            double measuredValueB,
            double measuredValueC,
            double thresholdValueA,
            double thresholdValueB,
            double thresholdValueC,
            float loadKilograms)
        {
            if (kind == SquatFailureKind.NONE)
                throw new ArgumentOutOfRangeException(nameof(kind));
            RequireTime(onsetTimeSeconds, nameof(onsetTimeSeconds));
            RequireTime(latchedTimeSeconds, nameof(latchedTimeSeconds));
            RequireOptional(measuredValueA, nameof(measuredValueA));
            RequireOptional(measuredValueB, nameof(measuredValueB));
            RequireOptional(measuredValueC, nameof(measuredValueC));
            RequireOptional(thresholdValueA, nameof(thresholdValueA));
            RequireOptional(thresholdValueB, nameof(thresholdValueB));
            RequireOptional(thresholdValueC, nameof(thresholdValueC));
            if (!float.IsNaN(loadKilograms) && (float.IsInfinity(loadKilograms) || loadKilograms < 0f))
                throw new ArgumentOutOfRangeException(nameof(loadKilograms));

            Kind = kind;
            Direction = direction;
            Detail = detail;
            OnsetTick = onsetTick;
            OnsetTimeSeconds = onsetTimeSeconds;
            LatchedTick = latchedTick;
            LatchedTimeSeconds = latchedTimeSeconds;
            OnsetContext = onsetContext;
            LatchedContext = latchedContext;
            EvidenceChannels = evidenceChannels;
            MeasuredValueA = measuredValueA;
            MeasuredValueB = measuredValueB;
            MeasuredValueC = measuredValueC;
            ThresholdValueA = thresholdValueA;
            ThresholdValueB = thresholdValueB;
            ThresholdValueC = thresholdValueC;
            LoadKilograms = loadKilograms;
        }

        public SquatFailureKind Kind { get; }
        public SquatFailureDirection Direction { get; }
        public SquatFailureDetailKind Detail { get; }
        public ulong OnsetTick { get; }
        public double OnsetTimeSeconds { get; }
        public ulong LatchedTick { get; }
        public double LatchedTimeSeconds { get; }
        public SquatFailureContext OnsetContext { get; }
        public SquatFailureContext LatchedContext { get; }
        public SquatFailureEvidenceChannel EvidenceChannels { get; }
        public double MeasuredValueA { get; }
        public double MeasuredValueB { get; }
        public double MeasuredValueC { get; }
        public double ThresholdValueA { get; }
        public double ThresholdValueB { get; }
        public double ThresholdValueC { get; }
        public float LoadKilograms { get; }

        private static void RequireTime(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireOptional(double value, string name)
        {
            if (double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public sealed class SquatFailureEvent
    {
        private readonly ReadOnlyCollection<SquatFailureMeasurement> _measurements;
        private readonly ReadOnlyCollection<SquatFailureThresholdValue> _thresholds;

        internal SquatFailureEvent(
            SquatFailureCandidate candidate,
            SquatFailureMeasurement[] measurements,
            int measurementCount,
            SquatFailureThresholdValue[] thresholds,
            int thresholdCount,
            string calibrationVersion)
        {
            if (measurements == null || thresholds == null)
                throw new ArgumentNullException();
            if (measurementCount < 0 || measurementCount > measurements.Length ||
                thresholdCount < 0 || thresholdCount > thresholds.Length)
                throw new ArgumentOutOfRangeException(nameof(measurementCount));
            if (string.IsNullOrEmpty(calibrationVersion))
                throw new ArgumentException("Failure event calibration version is required.", nameof(calibrationVersion));

            SquatFailureMeasurement[] measurementCopy = new SquatFailureMeasurement[measurementCount];
            SquatFailureThresholdValue[] thresholdCopy = new SquatFailureThresholdValue[thresholdCount];
            Array.Copy(measurements, measurementCopy, measurementCount);
            Array.Copy(thresholds, thresholdCopy, thresholdCount);

            Kind = candidate.Kind;
            Direction = candidate.Direction;
            Detail = candidate.Detail;
            OnsetTick = candidate.OnsetTick;
            OnsetTimeSeconds = candidate.OnsetTimeSeconds;
            LatchedTick = candidate.LatchedTick;
            LatchedTimeSeconds = candidate.LatchedTimeSeconds;
            OnsetContext = candidate.OnsetContext;
            LatchedContext = candidate.LatchedContext;
            EvidenceChannels = candidate.EvidenceChannels;
            CalibrationVersion = calibrationVersion;
            ClaimClassification = "GAME_ENGINE_PHYSICAL_FAILURE_CLASSIFICATION";
            _measurements = Array.AsReadOnly(measurementCopy);
            _thresholds = Array.AsReadOnly(thresholdCopy);
        }

        public SquatFailureKind Kind { get; }
        public SquatFailureDirection Direction { get; }
        public SquatFailureDetailKind Detail { get; }
        public ulong OnsetTick { get; }
        public double OnsetTimeSeconds { get; }
        public ulong LatchedTick { get; }
        public double LatchedTimeSeconds { get; }
        public SquatFailureContext OnsetContext { get; }
        public SquatFailureContext LatchedContext { get; }
        public SquatFailureEvidenceChannel EvidenceChannels { get; }
        public string CalibrationVersion { get; }
        public string ClaimClassification { get; }
        public IReadOnlyList<SquatFailureMeasurement> Measurements => _measurements;
        public IReadOnlyList<SquatFailureThresholdValue> Thresholds => _thresholds;
    }

    /// <summary>
    /// Compact immutable physical evidence sample retained in the bounded
    /// pre-failure window. NaN values preserve missing channels.
    /// </summary>
    public readonly struct SquatFailureEvidenceSample
    {
        internal SquatFailureEvidenceSample(SquatObservationSnapshot snapshot)
        {
            Tick = snapshot.SimulationTick;
            TimeSeconds = snapshot.SimulationTimeSeconds;

            BarAvailable = snapshot.Bar.IsAvailable;
            BarPositionY = BarAvailable ? snapshot.Bar.PositionWorldMeters.Y : float.NaN;
            BarVelocityY = BarAvailable ? snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y : float.NaN;

            PelvisAvailable = snapshot.PelvisAvailability == SquatTelemetryAvailability.AVAILABLE;
            PelvisPositionY = PelvisAvailable ? snapshot.PelvisPositionWorldMeters.Y : float.NaN;
            PelvisVelocityY = PelvisAvailable ? snapshot.PelvisLinearVelocityWorldMetersPerSecond.Y : float.NaN;

            ComAvailable = snapshot.Support.SystemComAvailability == SquatTelemetryAvailability.AVAILABLE;
            ComX = ComAvailable ? snapshot.Support.SystemComWorldMeters.X : float.NaN;
            ComZ = ComAvailable ? snapshot.Support.SystemComWorldMeters.Z : float.NaN;
            ComVelocityX = ComAvailable ? snapshot.Support.SystemComVelocityWorldMetersPerSecond.X : float.NaN;
            ComVelocityZ = ComAvailable ? snapshot.Support.SystemComVelocityWorldMetersPerSecond.Z : float.NaN;
            SupportAvailable = snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE;
            HasSupport = SupportAvailable && snapshot.Support.HasSupport;
            FrontMarginM = HasSupport ? snapshot.Support.ComToSupportApFrontMarginM : float.NaN;
            RearMarginM = HasSupport ? snapshot.Support.ComToSupportApRearMarginM : float.NaN;
            RightMarginM = HasSupport ? snapshot.Support.ComToSupportMlRightMarginM : float.NaN;
            LeftMarginM = HasSupport ? snapshot.Support.ComToSupportMlLeftMarginM : float.NaN;

            DepthAvailable = snapshot.Depth.Availability == SquatTelemetryAvailability.AVAILABLE;
            WorstDepthM = DepthAvailable ? snapshot.Depth.WorstSideDepthM : float.NaN;
            TrunkAvailable = snapshot.TrunkAvailability == SquatTelemetryAvailability.AVAILABLE;
            TrunkPitchRadians = TrunkAvailable ? snapshot.TrunkWorldPitchRadians : float.NaN;
            DriveAvailable = snapshot.DriveAvailability == SquatTelemetryAvailability.AVAILABLE;
            MaximumModeledDemand = DriveAvailable ? snapshot.MaximumModeledDemand : float.NaN;
            DriveSaturated = DriveAvailable && snapshot.DriveSaturated;
            LeftFootAvailable = snapshot.LeftFoot.Availability == SquatTelemetryAvailability.AVAILABLE;
            RightFootAvailable = snapshot.RightFoot.Availability == SquatTelemetryAvailability.AVAILABLE;
            LeftFootInContact = LeftFootAvailable && snapshot.LeftFoot.IsInContact;
            RightFootInContact = RightFootAvailable && snapshot.RightFoot.IsInContact;
            SaddleAvailable = snapshot.Bar.SaddleAvailability == SquatTelemetryAvailability.AVAILABLE;
            SaddleAttached = SaddleAvailable && snapshot.Bar.SaddleAttached;
            SaddleBroken = SaddleAvailable && snapshot.Bar.SaddleBroken;
            SaddleSeparationM = SaddleAvailable ? snapshot.Bar.SaddleSeparationMeters : float.NaN;
        }

        public ulong Tick { get; }
        public double TimeSeconds { get; }
        public bool BarAvailable { get; }
        public float BarPositionY { get; }
        public float BarVelocityY { get; }
        public bool PelvisAvailable { get; }
        public float PelvisPositionY { get; }
        public float PelvisVelocityY { get; }
        public bool ComAvailable { get; }
        public float ComX { get; }
        public float ComZ { get; }
        public float ComVelocityX { get; }
        public float ComVelocityZ { get; }
        public bool SupportAvailable { get; }
        public bool HasSupport { get; }
        public float FrontMarginM { get; }
        public float RearMarginM { get; }
        public float RightMarginM { get; }
        public float LeftMarginM { get; }
        public bool DepthAvailable { get; }
        public float WorstDepthM { get; }
        public bool TrunkAvailable { get; }
        public float TrunkPitchRadians { get; }
        public bool DriveAvailable { get; }
        public float MaximumModeledDemand { get; }
        public bool DriveSaturated { get; }
        public bool LeftFootAvailable { get; }
        public bool RightFootAvailable { get; }
        public bool LeftFootInContact { get; }
        public bool RightFootInContact { get; }
        public bool SaddleAvailable { get; }
        public bool SaddleAttached { get; }
        public bool SaddleBroken { get; }
        public float SaddleSeparationM { get; }
    }

    public sealed class SquatFailureEvidenceWindow
    {
        private readonly ReadOnlyCollection<SquatFailureEvidenceSample> _samples;

        internal SquatFailureEvidenceWindow(
            SquatFailureEvidenceSample[] ring,
            int count,
            int nextIndex,
            int capacity)
        {
            if (ring == null)
                throw new ArgumentNullException(nameof(ring));
            if (capacity <= 0 || ring.Length != capacity || count < 0 || count > capacity)
                throw new ArgumentOutOfRangeException(nameof(capacity));

            SquatFailureEvidenceSample[] ordered = new SquatFailureEvidenceSample[count];
            int firstIndex = count == capacity ? nextIndex : 0;
            for (int index = 0; index < count; index++)
                ordered[index] = ring[(firstIndex + index) % capacity];

            Capacity = capacity;
            _samples = Array.AsReadOnly(ordered);
        }

        public int Capacity { get; }
        public int Count => _samples.Count;
        public IReadOnlyList<SquatFailureEvidenceSample> Samples => _samples;

        public SquatFailureEvidenceSample SampleAt(int index)
        {
            if (index < 0 || index >= _samples.Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _samples[index];
        }
    }

    public sealed class SquatFailureRecord
    {
        private readonly ReadOnlyCollection<SquatFailureEvent> _secondaryEvents;

        internal SquatFailureRecord(
            SquatFailureEvent primaryEvent,
            SquatFailureEvent[] secondaryEvents,
            int secondaryCount,
            SquatFailureEvidenceWindow preFailureEvidence,
            string failureModelVersion,
            string calibrationVersion,
            string precedenceVersion,
            string traceSchema,
            float loadKilograms)
        {
            if (primaryEvent == null)
                throw new ArgumentNullException(nameof(primaryEvent));
            if (secondaryEvents == null)
                throw new ArgumentNullException(nameof(secondaryEvents));
            if (secondaryCount < 0 || secondaryCount > secondaryEvents.Length)
                throw new ArgumentOutOfRangeException(nameof(secondaryCount));
            if (preFailureEvidence == null)
                throw new ArgumentNullException(nameof(preFailureEvidence));
            RequireText(failureModelVersion, nameof(failureModelVersion));
            RequireText(calibrationVersion, nameof(calibrationVersion));
            RequireText(precedenceVersion, nameof(precedenceVersion));
            RequireText(traceSchema, nameof(traceSchema));
            if (!float.IsNaN(loadKilograms) && (float.IsInfinity(loadKilograms) || loadKilograms < 0f))
                throw new ArgumentOutOfRangeException(nameof(loadKilograms));

            SquatFailureEvent[] copy = new SquatFailureEvent[secondaryCount];
            Array.Copy(secondaryEvents, copy, secondaryCount);
            Primary = primaryEvent;
            _secondaryEvents = Array.AsReadOnly(copy);
            PreFailureEvidence = preFailureEvidence;
            FailureModelVersion = failureModelVersion;
            CalibrationVersion = calibrationVersion;
            PrecedenceVersion = precedenceVersion;
            TraceSchema = traceSchema;
            LoadKilograms = loadKilograms;
            ClaimClassification = "GAME_ENGINE_PHYSICAL_FAILURE_CLASSIFICATION";
        }

        public SquatFailureEvent Primary { get; }
        public IReadOnlyList<SquatFailureEvent> SecondaryEvents => _secondaryEvents;
        public IReadOnlyList<SquatFailureEvent> SecondaryCauses => _secondaryEvents;
        public SquatFailureEvidenceWindow PreFailureEvidence { get; }
        public string FailureModelVersion { get; }
        public string CalibrationVersion { get; }
        public string PrecedenceVersion { get; }
        public string TraceSchema { get; }
        public float LoadKilograms { get; }
        public string LoadSourceClass => SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION.ToString();
        public string ClaimClassification { get; }
        public SquatFailureKind PrimaryFailureKind => Primary.Kind;
        public SquatFailureDirection PrimaryDirection => Primary.Direction;
        public SquatFailureDetailKind PrimaryDetail => Primary.Detail;
        public ulong OnsetTick => Primary.OnsetTick;
        public double OnsetTimeSeconds => Primary.OnsetTimeSeconds;
        public ulong LatchedTick => Primary.LatchedTick;
        public double LatchedTimeSeconds => Primary.LatchedTimeSeconds;
        public int SecondaryCount => _secondaryEvents.Count;

        public SquatFailureSafetyHandoff CreateSafetyHandoff() =>
            new SquatFailureSafetyHandoff(this);

        private static void RequireText(string value, string name)
        {
            if (string.IsNullOrEmpty(value))
                throw new ArgumentException("Failure record provenance is required.", name);
        }
    }

    /// <summary>
    /// Value-domain boundary for a later safety consumer. This type contains
    /// no actuation API and cannot modify physics, controller state, or trace.
    /// </summary>
    public sealed class SquatFailureSafetyHandoff
    {
        internal SquatFailureSafetyHandoff(SquatFailureRecord failureRecord)
        {
            FailureRecord = failureRecord ?? throw new ArgumentNullException(nameof(failureRecord));
            TraceSchema = failureRecord.TraceSchema;
            EligibleLatchTick = failureRecord.LatchedTick;
        }

        public SquatFailureRecord FailureRecord { get; }
        public string TraceSchema { get; }
        public ulong EligibleLatchTick { get; }
        public bool IsEligible => FailureRecord != null;
    }

    public sealed class SquatFailureResult
    {
        internal SquatFailureResult(
            SquatFailureEvidenceStatus evidenceStatus,
            SquatFailureResultKind outcome,
            SquatFailureRecord failureRecord,
            string evaluatedTraceSchema,
            int traceCount,
            string failureModelVersion,
            string calibrationVersion,
            string precedenceVersion,
            SquatFailureTerminalContextStatus terminalContextStatus)
        {
            EvidenceStatus = evidenceStatus;
            Outcome = outcome;
            FailureRecord = failureRecord;
            EvaluatedTraceSchema = evaluatedTraceSchema ?? string.Empty;
            TraceCount = traceCount;
            FailureModelVersion = failureModelVersion ?? string.Empty;
            CalibrationVersion = calibrationVersion ?? string.Empty;
            PrecedenceVersion = precedenceVersion ?? string.Empty;
            TerminalContextStatus = terminalContextStatus;
        }

        public SquatFailureEvidenceStatus EvidenceStatus { get; }
        public SquatFailureEvidenceStatus Status => EvidenceStatus;
        public SquatFailureResultKind Outcome { get; }
        public SquatFailureResultKind Classification => Outcome;
        public SquatFailureRecord FailureRecord { get; }
        public bool HasFailure => Outcome == SquatFailureResultKind.PHYSICAL_FAILURE && FailureRecord != null;
        public bool HasDecision => EvidenceStatus == SquatFailureEvidenceStatus.EVALUABLE &&
            Outcome != SquatFailureResultKind.UNDETERMINED;
        public string EvaluatedTraceSchema { get; }
        public int TraceCount { get; }
        public string FailureModelVersion { get; }
        public string CalibrationVersion { get; }
        public string PrecedenceVersion { get; }

        /// <summary>
        /// Whether an authoritative terminal attempt context was supplied and
        /// accepted. Only <see cref="SquatFailureTerminalContextStatus.TRACE_COVERED"/>
        /// permits the FAILED_LOCKOUT terminal postcondition.
        /// </summary>
        public SquatFailureTerminalContextStatus TerminalContextStatus { get; }
        public bool TerminalPostconditionEvaluated =>
            TerminalContextStatus == SquatFailureTerminalContextStatus.TRACE_COVERED;
        public SquatFailureKind PrimaryFailureKind => FailureRecord == null
            ? SquatFailureKind.NONE
            : FailureRecord.PrimaryFailureKind;
        public SquatFailureSafetyHandoff SafetyHandoff => FailureRecord == null
            ? null
            : FailureRecord.CreateSafetyHandoff();
    }

    /// <summary>
    /// Causal, deterministic physical failure interpretation of canonical raw
    /// post-physics squat observations. It never owns Unity objects, safety
    /// actuation, rule judgment, or load-based failure selection.
    /// </summary>
    public sealed class SquatFailureDetector
    {
        private const int MaximumCandidatesPerSample = 16;
        private const int MaximumDistinctEvents = 32;

        private readonly SquatFailureCalibration _calibration;
        private readonly SquatFailureCandidate[] _candidateBuffer =
            new SquatFailureCandidate[MaximumCandidatesPerSample];
        private readonly SquatFailureEvent[] _secondaryEvents =
            new SquatFailureEvent[MaximumDistinctEvents];
        private readonly SquatFailureEvidenceSample[] _evidenceRing;

        private int _candidateCount;
        private int _secondaryEventCount;
        private int _evidenceCount;
        private int _sampleCount;
        private int _evidenceNextIndex;
        private bool _hasLastSample;
        private bool _completed;
        private SquatObservationSnapshot _lastSample;
        private bool _allCoreEvidence = true;
        private bool _hasPhysicalDescent;
        private bool _hasPhysicalBottom;
        private bool _hasLegalBottom;
        private bool _hasAscentEstablished;
        private bool _physicalLockoutReached;
        private bool _hasCompletionRegionEntry;
        private SquatFailureContext _completionRegionEntryContext;
        private int _completionRegionDwellRun;
        private int _maximumCompletionRegionDwellTicks;
        private SquatFailureTerminalContextStatus _terminalContextStatus;
        private bool _hasPreferredVertical;
        private bool _lastPreferredVerticalWasBar;
        private float _lastPreferredVerticalY;
        private float _lastPreferredVerticalVelocity;
        private bool _hasStandingReference;
        private float _standingReferenceY;
        private bool _hasBarPosition;
        private float _lastBarPositionY;
        private float _physicalBottomPositionY;
        private bool _hasReversalAttempt;
        private bool _reversalRecovered;
        private ulong _reversalAttemptTick;
        private SquatFailureContext _reversalAttemptContext;
        private int _reversalRecoveryRun;
        private int _forwardBalanceRun;
        private int _backwardBalanceRun;
        private int _leftBalanceRun;
        private int _rightBalanceRun;
        private SquatFailureContext _forwardBalanceContext;
        private SquatFailureContext _backwardBalanceContext;
        private SquatFailureContext _leftBalanceContext;
        private SquatFailureContext _rightBalanceContext;
        private bool _forwardBalanceEmitted;
        private bool _backwardBalanceEmitted;
        private bool _leftBalanceEmitted;
        private bool _rightBalanceEmitted;
        private int _descentCollapseRun;
        private SquatFailureContext _descentCollapseContext;
        private bool _descentCollapseEmitted;
        private int _stallRun;
        private float _stallStartPositionY;
        private SquatFailureContext _stallContext;
        private bool _stallEmitted;
        private int _barReversalRun;
        private float _barReversalReferencePositionY;
        private SquatFailureContext _barReversalContext;
        private bool _barReversalEmitted;
        private int _trunkFailureRun;
        private SquatFailureContext _trunkFailureContext;
        private bool _trunkFailureEmitted;
        private int _jointLimitFailureRun;
        private SquatFailureContext _jointLimitFailureContext;
        private bool _jointLimitFailureEmitted;
        private int _saddleSeparationRun;
        private SquatFailureContext _saddleSeparationContext;
        private bool _saddleSeparationEmitted;
        private bool _saddleBrokenEmitted;
        private bool _couplingLossEmitted;
        private bool _hasPrimary;
        private SquatFailureCandidate _primaryCandidate;
        private SquatFailureEvidenceWindow _primaryEvidence;
        private float _primaryLoadKilograms = float.NaN;

        public SquatFailureDetector(SquatFailureCalibration calibration = null)
        {
            _calibration = calibration ?? SquatFailureCalibration.Default;
            _evidenceRing = new SquatFailureEvidenceSample[_calibration.PreFailureEvidenceCapacity];
            Reset();
        }

        public SquatFailureCalibration Calibration => _calibration;
        public string FailureModelVersion => SquatFailureCalibration.FailureModelVersion;
        public string CalibrationVersion => _calibration.Version;
        public string PrecedenceVersion => _calibration.PrecedenceVersion;
        public bool HasLatchedFailure => _hasPrimary;
        public SquatFailureCandidate PrimaryCandidate => _primaryCandidate;

        // GAM-47 read-only P3 stage diagnostics. These expose the detector's
        // existing physical-context state without changing classification.
        public bool PhysicalDescentSeen => _hasPhysicalDescent;
        public bool PhysicalBottomSeen => _hasPhysicalBottom;
        public bool LegalBottomSeen => _hasLegalBottom;
        public bool AscentEstablished => _hasAscentEstablished;
        public bool PhysicalLockoutSeen => _physicalLockoutReached;
        public bool CompletionRegionEntered => _hasCompletionRegionEntry;
        public int MaximumCompletionRegionDwellTicks => _maximumCompletionRegionDwellTicks;
        public SquatFailureTerminalContextStatus TerminalContextStatus => _terminalContextStatus;
        public bool TerminalContextCovered =>
            _terminalContextStatus == SquatFailureTerminalContextStatus.TRACE_COVERED;

        public void Reset()
        {
            _candidateCount = 0;
            _secondaryEventCount = 0;
            _evidenceCount = 0;
            _sampleCount = 0;
            _evidenceNextIndex = 0;
            _hasLastSample = false;
            _completed = false;
            _lastSample = default(SquatObservationSnapshot);
            _allCoreEvidence = true;
            _hasPhysicalDescent = false;
            _hasPhysicalBottom = false;
            _hasLegalBottom = false;
            _hasAscentEstablished = false;
            _physicalLockoutReached = false;
            _hasCompletionRegionEntry = false;
            _completionRegionEntryContext = default(SquatFailureContext);
            _completionRegionDwellRun = 0;
            _maximumCompletionRegionDwellTicks = 0;
            _terminalContextStatus = SquatFailureTerminalContextStatus.NOT_PROVIDED;
            _hasPreferredVertical = false;
            _lastPreferredVerticalWasBar = false;
            _lastPreferredVerticalY = 0f;
            _lastPreferredVerticalVelocity = 0f;
            _hasStandingReference = false;
            _standingReferenceY = 0f;
            _hasBarPosition = false;
            _lastBarPositionY = 0f;
            _ascentRun = 0;
            _hasAscentSource = false;
            _ascentSourceWasBar = false;
            _physicalBottomPositionY = 0f;
            _hasReversalAttempt = false;
            _reversalRecovered = false;
            _reversalAttemptTick = 0ul;
            _reversalAttemptContext = default(SquatFailureContext);
            _reversalRecoveryRun = 0;
            _forwardBalanceRun = 0;
            _backwardBalanceRun = 0;
            _leftBalanceRun = 0;
            _rightBalanceRun = 0;
            _forwardBalanceEmitted = false;
            _backwardBalanceEmitted = false;
            _leftBalanceEmitted = false;
            _rightBalanceEmitted = false;
            _descentCollapseRun = 0;
            _descentCollapseContext = default(SquatFailureContext);
            _descentCollapseEmitted = false;
            _stallRun = 0;
            _stallStartPositionY = 0f;
            _stallContext = default(SquatFailureContext);
            _stallEmitted = false;
            _barReversalRun = 0;
            _barReversalReferencePositionY = 0f;
            _barReversalContext = default(SquatFailureContext);
            _barReversalEmitted = false;
            _trunkFailureRun = 0;
            _trunkFailureContext = default(SquatFailureContext);
            _trunkFailureEmitted = false;
            _jointLimitFailureRun = 0;
            _jointLimitFailureContext = default(SquatFailureContext);
            _jointLimitFailureEmitted = false;
            _saddleSeparationRun = 0;
            _saddleSeparationContext = default(SquatFailureContext);
            _saddleSeparationEmitted = false;
            _saddleBrokenEmitted = false;
            _couplingLossEmitted = false;
            _hasPrimary = false;
            _primaryCandidate = default(SquatFailureCandidate);
            _primaryEvidence = null;
            _primaryLoadKilograms = float.NaN;
        }

        public bool TryProcess(SquatObservationSnapshot snapshot)
        {
            if (_completed || !IsSequentiallyValid(snapshot))
                return false;

            _candidateCount = 0;
            UpdateCompleteness(snapshot);
            WriteEvidence(snapshot);
            UpdatePhysicalMotionContext(snapshot);
            UpdateAscentContext(snapshot);
            UpdateBalance(snapshot);
            UpdateDescentCollapse(snapshot);
            UpdateReversal(snapshot);
            UpdatePostureAndBarLoss(snapshot);
            UpdateLockout(snapshot);
            UpdateStall(snapshot);
            UpdateBarReversal(snapshot);
            SortCandidates();
            for (int index = 0; index < _candidateCount; index++)
                RegisterCandidate(_candidateBuffer[index]);
            _candidateCount = 0;

            _lastSample = snapshot;
            _hasLastSample = true;
            _sampleCount++;
            if (snapshot.Bar.IsAvailable)
            {
                _lastBarPositionY = snapshot.Bar.PositionWorldMeters.Y;
                _hasBarPosition = true;
            }
            else
            {
                _hasBarPosition = false;
            }
            return true;
        }

        public void Process(SquatObservationSnapshot snapshot)
        {
            if (!TryProcess(snapshot))
                throw new InvalidOperationException("The squat failure detector received a non-sequential or completed snapshot stream.");
        }

        /// <summary>
        /// Pure physical interpretation of a frozen trace with no authoritative
        /// terminality. The FAILED_LOCKOUT terminal postcondition is not
        /// decided, because absence of lockout is not yet evidence of failure.
        /// </summary>
        public SquatFailureResult Evaluate(SquatTrace trace) =>
            Evaluate(trace, SquatFailureCompletionContext.NonTerminal);

        /// <summary>
        /// Terminal finalization seam. Streaming physical interpretation of the
        /// frozen trace is unchanged; the authoritative terminal context only
        /// permits the FAILED_LOCKOUT terminal postcondition to be decided.
        /// </summary>
        public SquatFailureResult Evaluate(SquatTrace trace, SquatFailureCompletionContext completion) =>
            Evaluate(trace, completion, SquatFailureAttemptContext.Unspecified);

        /// <summary>
        /// Evaluates physical evidence from an explicit attempt boundary. The
        /// full frozen trace remains available to the caller, but samples before
        /// <paramref name="attemptContext"/>.AttemptStartTick cannot establish
        /// P3 descent, bottom, ascent, or lockout.
        /// </summary>
        public SquatFailureResult Evaluate(
            SquatTrace trace,
            SquatFailureCompletionContext completion,
            SquatFailureAttemptContext attemptContext)
        {
            Reset();
            if (!IsValidTrace(trace))
                return InvalidResult(trace);

            if (!attemptContext.IsWellFormed)
                return InvalidResult(trace);

            int attemptStartIndex = 0;
            if (attemptContext.IsSpecified)
            {
                attemptStartIndex = FindIndexAtTick(trace, attemptContext.AttemptStartTick);
                int standingReferenceIndex = FindIndexAtTick(trace, attemptContext.StandingReferenceTick);
                if (attemptStartIndex < 0 || standingReferenceIndex < 0 || standingReferenceIndex > attemptStartIndex ||
                    !TryGetPreferredVertical(trace[standingReferenceIndex], out float standingY, out _, out _))
                    return InvalidResult(trace);

                _hasStandingReference = true;
                _standingReferenceY = standingY;
            }

            for (int index = attemptStartIndex; index < trace.Count; index++)
            {
                if (!TryProcess(trace[index]))
                    return InvalidResult(trace);
            }

            _terminalContextStatus = ResolveTerminalContext(
                trace,
                completion,
                out SquatObservationSnapshot terminalSample);
            if (_terminalContextStatus == SquatFailureTerminalContextStatus.TRACE_COVERED)
                ApplyTerminalLockoutPostcondition(terminalSample);
            return Complete(trace.Schema);
        }

        public SquatFailureResult Evaluate(
            SquatTrace trace,
            SquatFailureAttemptContext attemptContext) =>
            Evaluate(trace, SquatFailureCompletionContext.NonTerminal, attemptContext);

        public SquatFailureResult Detect(SquatTrace trace) => Evaluate(trace);

        public SquatFailureResult Complete(string traceSchema = SquatTrace.SchemaVersion)
        {
            if (_completed)
                throw new InvalidOperationException("The squat failure detector result is already finalized.");
            _completed = true;
            if (_candidateCount != 0)
                throw new InvalidOperationException("The squat failure detector candidate buffer was not finalized.");

            if (_hasPrimary)
            {
                SquatFailureEvent primaryEvent = CreateEvent(_primaryCandidate);
                SortSecondaryEvents();
                SquatFailureRecord record = new SquatFailureRecord(
                    primaryEvent,
                    _secondaryEvents,
                    _secondaryEventCount,
                    _primaryEvidence ?? EmptyEvidenceWindow(),
                    FailureModelVersion,
                    CalibrationVersion,
                    PrecedenceVersion,
                    traceSchema,
                    _primaryLoadKilograms);
                return new SquatFailureResult(
                    SquatFailureEvidenceStatus.EVALUABLE,
                    SquatFailureResultKind.PHYSICAL_FAILURE,
                    record,
                    traceSchema,
                    _sampleCount,
                    FailureModelVersion,
                    CalibrationVersion,
                    PrecedenceVersion,
                    _terminalContextStatus);
            }

            if (!_allCoreEvidence)
                return new SquatFailureResult(
                    SquatFailureEvidenceStatus.INSUFFICIENT_EVIDENCE,
                    SquatFailureResultKind.UNDETERMINED,
                    null,
                    traceSchema,
                    _sampleCount,
                    FailureModelVersion,
                    CalibrationVersion,
                    PrecedenceVersion,
                    _terminalContextStatus);

            bool completedPhysicalAttempt = _hasPhysicalDescent && _hasPhysicalBottom &&
                _hasAscentEstablished && _physicalLockoutReached;
            return new SquatFailureResult(
                completedPhysicalAttempt
                    ? SquatFailureEvidenceStatus.EVALUABLE
                    : SquatFailureEvidenceStatus.INCOMPLETE_ATTEMPT,
                completedPhysicalAttempt
                    ? SquatFailureResultKind.NO_PHYSICAL_FAILURE
                    : SquatFailureResultKind.UNDETERMINED,
                null,
                traceSchema,
                _sampleCount,
                FailureModelVersion,
                CalibrationVersion,
                PrecedenceVersion,
                _terminalContextStatus);
        }

        /// <summary>
        /// Validates that an authoritative terminal context belongs to this
        /// attempt: the terminal tick must be covered by the frozen trace and
        /// its time must map to that canonical sample. A rejected context can
        /// only suppress the terminal postcondition, never create a failure.
        /// </summary>
        private static SquatFailureTerminalContextStatus ResolveTerminalContext(
            SquatTrace trace,
            SquatFailureCompletionContext completion,
            out SquatObservationSnapshot terminalSample)
        {
            terminalSample = default(SquatObservationSnapshot);
            if (!completion.IsTerminal)
                return SquatFailureTerminalContextStatus.NOT_TERMINAL;
            if (!completion.IsWellFormed)
                return SquatFailureTerminalContextStatus.REJECTED_MALFORMED;

            ulong firstTick = trace[0].SimulationTick;
            ulong lastTick = trace[trace.Count - 1].SimulationTick;
            if (completion.TerminalTick < firstTick || completion.TerminalTick > lastTick)
                return SquatFailureTerminalContextStatus.REJECTED_UNCOVERED_TICK;

            // IsValidTrace already guarantees strictly contiguous ticks.
            SquatObservationSnapshot candidate = trace[checked((int)(completion.TerminalTick - firstTick))];
            if (candidate.SimulationTick != completion.TerminalTick)
                return SquatFailureTerminalContextStatus.REJECTED_UNCOVERED_TICK;
            if (Math.Abs(candidate.SimulationTimeSeconds - completion.TerminalTimeSeconds) >
                FoundationTolerances.SimulationTimeMapping)
                return SquatFailureTerminalContextStatus.REJECTED_TIME_MISMATCH;

            terminalSample = candidate;
            return SquatFailureTerminalContextStatus.TRACE_COVERED;
        }

        /// <summary>
        /// The canonical FAILED_LOCKOUT rule. Physical lockout achieved at any
        /// point means no FAILED_LOCKOUT. Otherwise, an authoritatively
        /// terminated attempt that had credibly entered the completion region
        /// without ever achieving lockout owns the terminal postcondition.
        /// Onset stays the first completion-region entry; the latch is the
        /// authoritative terminal tick.
        /// </summary>
        private void ApplyTerminalLockoutPostcondition(SquatObservationSnapshot terminalSample)
        {
            if (_physicalLockoutReached || !_hasCompletionRegionEntry)
                return;
            // A frozen trace may extend past the authoritative terminal sample
            // (Rack and Rerack are later ticks). Completion-region evidence
            // recorded after termination cannot support a terminal
            // postcondition, and admitting it would emit a record whose onset
            // follows its own latch.
            if (_completionRegionEntryContext.SimulationTick > terminalSample.SimulationTick)
                return;

            _candidateCount = 0;
            QueueCandidate(
                SquatFailureKind.FAILED_LOCKOUT,
                SquatFailureDirection.NONE,
                SquatFailureDetailKind.NONE,
                _completionRegionEntryContext,
                terminalSample,
                SquatFailureEvidenceChannel.BAR_POSITION |
                SquatFailureEvidenceChannel.BAR_LINEAR_VELOCITY |
                SquatFailureEvidenceChannel.JOINT_KINEMATICS |
                SquatFailureEvidenceChannel.TRUNK_KINEMATICS,
                _maximumCompletionRegionDwellTicks,
                MaxKneeAngle(terminalSample),
                MaxHipAngle(terminalSample),
                double.NaN,
                _calibration.LockoutKneeToleranceRadians,
                _calibration.LockoutHipToleranceRadians);
            RegisterCandidate(_candidateBuffer[0]);
            _candidateCount = 0;
        }

        private bool IsSequentiallyValid(SquatObservationSnapshot snapshot)
        {
            if (!_hasLastSample)
                return string.Equals(snapshot.Schema, SquatObservationSnapshot.SchemaId, StringComparison.Ordinal);
            if (snapshot.SimulationTick != _lastSample.SimulationTick + 1ul)
                return false;
            if (Math.Abs(snapshot.SimulationTimeSeconds - _lastSample.SimulationTimeSeconds - snapshot.FixedStepSeconds) >
                FoundationTolerances.SimulationTimeMapping)
                return false;
            return Math.Abs(snapshot.FixedStepSeconds - _lastSample.FixedStepSeconds) <=
                FoundationTolerances.SimulationTimeMapping;
        }

        private static bool IsValidTrace(SquatTrace trace)
        {
            if (trace == null || trace.IsRecording || !trace.IsFrozen || trace.Count == 0 ||
                !string.Equals(trace.Schema, SquatTrace.SchemaVersion, StringComparison.Ordinal))
                return false;
            SquatObservationSnapshot prior = trace[0];
            for (int index = 1; index < trace.Count; index++)
            {
                SquatObservationSnapshot current = trace[index];
                if (current.SimulationTick != prior.SimulationTick + 1ul ||
                    Math.Abs(current.SimulationTimeSeconds - prior.SimulationTimeSeconds - current.FixedStepSeconds) >
                    FoundationTolerances.SimulationTimeMapping ||
                    Math.Abs(current.FixedStepSeconds - prior.FixedStepSeconds) > FoundationTolerances.SimulationTimeMapping)
                    return false;
                prior = current;
            }
            return true;
        }

        private static int FindIndexAtTick(SquatTrace trace, ulong tick)
        {
            for (int index = 0; index < trace.Count; index++)
                if (trace[index].SimulationTick == tick)
                    return index;
            return -1;
        }

        private SquatFailureResult InvalidResult(SquatTrace trace) => new SquatFailureResult(
            SquatFailureEvidenceStatus.INVALID_TRACE,
            SquatFailureResultKind.UNDETERMINED,
            null,
            trace == null ? string.Empty : trace.Schema,
            trace == null ? 0 : trace.Count,
            FailureModelVersion,
            CalibrationVersion,
            PrecedenceVersion,
            _terminalContextStatus);

        private void UpdateCompleteness(SquatObservationSnapshot snapshot)
        {
            SquatTelemetryQualityFlags quality = snapshot.Quality;
            bool complete = quality.HasFlag(SquatTelemetryQualityFlags.POST_PHYSICS) &&
                quality.HasFlag(SquatTelemetryQualityFlags.RAW) &&
                quality.HasFlag(SquatTelemetryQualityFlags.ATTEMPT_RELATIVE_TIME) &&
                quality.HasFlag(SquatTelemetryQualityFlags.BAR_AVAILABLE) && snapshot.Bar.IsAvailable &&
                snapshot.Bar.SaddleAvailability == SquatTelemetryAvailability.AVAILABLE &&
                quality.HasFlag(SquatTelemetryQualityFlags.SUPPORT_PRODUCER_AVAILABLE) &&
                snapshot.Support.SystemComAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE &&
                quality.HasFlag(SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE) &&
                snapshot.Depth.Availability == SquatTelemetryAvailability.AVAILABLE &&
                quality.HasFlag(SquatTelemetryQualityFlags.LEFT_FOOT_PRODUCER_AVAILABLE) &&
                quality.HasFlag(SquatTelemetryQualityFlags.RIGHT_FOOT_PRODUCER_AVAILABLE) &&
                snapshot.LeftFoot.Availability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.RightFoot.Availability == SquatTelemetryAvailability.AVAILABLE &&
                quality.HasFlag(SquatTelemetryQualityFlags.JOINTS_AVAILABLE) &&
                RequiredJointEvidenceAvailable(snapshot) &&
                quality.HasFlag(SquatTelemetryQualityFlags.DRIVE_DIAGNOSTICS_AVAILABLE) &&
                snapshot.DriveAvailability == SquatTelemetryAvailability.AVAILABLE;
            _allCoreEvidence &= complete;
        }

        private static bool RequiredJointEvidenceAvailable(SquatObservationSnapshot snapshot)
        {
            return snapshot.Joints.LeftKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.RightKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.LeftHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.RightHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.LeftAnkle.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.RightAnkle.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.Abdomen.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Joints.Thorax.JointAvailability == SquatTelemetryAvailability.AVAILABLE;
        }

        private void WriteEvidence(SquatObservationSnapshot snapshot)
        {
            _evidenceRing[_evidenceNextIndex] = new SquatFailureEvidenceSample(snapshot);
            _evidenceNextIndex = (_evidenceNextIndex + 1) % _evidenceRing.Length;
            if (_evidenceCount < _evidenceRing.Length)
                _evidenceCount++;
        }

        private SquatFailureEvidenceWindow EmptyEvidenceWindow() =>
            new SquatFailureEvidenceWindow(
                _evidenceRing,
                0,
                _evidenceNextIndex,
                _evidenceRing.Length);

        private SquatFailureEvidenceWindow FreezeEvidence() =>
            new SquatFailureEvidenceWindow(
                _evidenceRing,
                _evidenceCount,
                _evidenceNextIndex,
                _evidenceRing.Length);

        private void UpdatePhysicalMotionContext(SquatObservationSnapshot snapshot)
        {
            bool hasVertical = TryGetPreferredVertical(snapshot, out float positionY, out float velocityY, out bool usesBar);
            if (hasVertical)
            {
                if (!_hasPhysicalDescent && !_hasStandingReference &&
                    Math.Abs(velocityY) <= _calibration.PhysicalMotionVelocityMps)
                {
                    _hasStandingReference = true;
                    _standingReferenceY = positionY;
                }
                bool sameSource = _hasPreferredVertical && _lastPreferredVerticalWasBar == usesBar;
                bool previousDownward = sameSource &&
                    (_lastPreferredVerticalVelocity <= -_calibration.PhysicalMotionVelocityMps ||
                     _lastPreferredVerticalY - positionY >= _calibration.PhysicalMotionDisplacementM);
                bool currentDownward = velocityY <= -_calibration.PhysicalMotionVelocityMps ||
                    (sameSource && _lastPreferredVerticalY - positionY >= _calibration.PhysicalMotionDisplacementM);
                if (currentDownward)
                    _hasPhysicalDescent = true;

                if (!_hasPhysicalBottom && _hasPhysicalDescent && previousDownward &&
                    (velocityY >= -_calibration.BottomTransitionVelocityMps ||
                     positionY - _lastPreferredVerticalY >= _calibration.BottomTransitionDisplacementM))
                {
                    _hasPhysicalBottom = true;
                    _physicalBottomPositionY = velocityY >= _calibration.BottomTransitionVelocityMps
                        ? _lastPreferredVerticalY
                        : positionY;
                }

                _lastPreferredVerticalWasBar = usesBar;
                _lastPreferredVerticalY = positionY;
                _lastPreferredVerticalVelocity = velocityY;
                _hasPreferredVertical = true;
            }
            else
            {
                _hasPreferredVertical = false;
            }

            if (_hasPhysicalBottom && IsLegalBottom(snapshot))
                _hasLegalBottom = true;
        }

        private void UpdateAscentContext(SquatObservationSnapshot snapshot)
        {
            if (!_hasPhysicalBottom)
            {
                _ascentRun = 0;
                _hasAscentSource = false;
                return;
            }
            if (!TryGetPreferredVertical(snapshot, out float positionY, out float velocityY, out bool usesBar))
            {
                _ascentRun = 0;
                _hasAscentSource = false;
                return;
            }
            if (_hasAscentSource && _ascentSourceWasBar != usesBar)
                _ascentRun = 0;
            _ascentSourceWasBar = usesBar;
            _hasAscentSource = true;

            bool upward = velocityY >= _calibration.AscentEstablishmentVelocityMps;
            if (!upward)
            {
                _ascentRun = 0;
                return;
            }

            if (_ascentRun == 0)
                _ascentRun = 1;
            else
                _ascentRun++;

            if (!_hasAscentEstablished && _ascentRun >= _calibration.AscentEstablishmentPersistenceTicks &&
                positionY - _physicalBottomPositionY >= _calibration.AscentEstablishmentDisplacementM)
            {
                _hasAscentEstablished = true;
                _reversalRecovered = _hasReversalAttempt;
            }
        }

        private int _ascentRun;
        private bool _hasAscentSource;
        private bool _ascentSourceWasBar;

        private void UpdateBalance(SquatObservationSnapshot snapshot)
        {
            bool available = snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Support.SystemComAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Support.HasSupport &&
                SquatTelemetryValue.IsFinite(snapshot.Support.SystemComVelocityWorldMetersPerSecond) &&
                SquatTelemetryValue.IsFinite(snapshot.Support.ComToSupportApFrontMarginM) &&
                SquatTelemetryValue.IsFinite(snapshot.Support.ComToSupportApRearMarginM) &&
                SquatTelemetryValue.IsFinite(snapshot.Support.ComToSupportMlRightMarginM) &&
                SquatTelemetryValue.IsFinite(snapshot.Support.ComToSupportMlLeftMarginM);
            if (!available)
            {
                ResetBalanceRuns();
                return;
            }

            float velocityZ = snapshot.Support.SystemComVelocityWorldMetersPerSecond.Z;
            float velocityX = snapshot.Support.SystemComVelocityWorldMetersPerSecond.X;
            UpdateBalanceRun(
                snapshot,
                snapshot.Support.ComToSupportApFrontMarginM <= _calibration.BalanceSupportMarginFailureM &&
                    velocityZ >= _calibration.BalanceOutwardComVelocityMps,
                SquatFailureDirection.FORWARD,
                ref _forwardBalanceRun,
                ref _forwardBalanceContext,
                ref _forwardBalanceEmitted);
            UpdateBalanceRun(
                snapshot,
                snapshot.Support.ComToSupportApRearMarginM <= _calibration.BalanceSupportMarginFailureM &&
                    velocityZ <= -_calibration.BalanceOutwardComVelocityMps,
                SquatFailureDirection.BACKWARD,
                ref _backwardBalanceRun,
                ref _backwardBalanceContext,
                ref _backwardBalanceEmitted);
            UpdateBalanceRun(
                snapshot,
                snapshot.Support.ComToSupportMlLeftMarginM <= _calibration.BalanceSupportMarginFailureM &&
                    velocityX <= -_calibration.BalanceOutwardComVelocityMps,
                SquatFailureDirection.LEFT,
                ref _leftBalanceRun,
                ref _leftBalanceContext,
                ref _leftBalanceEmitted);
            UpdateBalanceRun(
                snapshot,
                snapshot.Support.ComToSupportMlRightMarginM <= _calibration.BalanceSupportMarginFailureM &&
                    velocityX >= _calibration.BalanceOutwardComVelocityMps,
                SquatFailureDirection.RIGHT,
                ref _rightBalanceRun,
                ref _rightBalanceContext,
                ref _rightBalanceEmitted);
        }

        private void UpdateBalanceRun(
            SquatObservationSnapshot snapshot,
            bool condition,
            SquatFailureDirection direction,
            ref int run,
            ref SquatFailureContext onsetContext,
            ref bool emitted)
        {
            if (!condition)
            {
                run = 0;
                emitted = false;
                return;
            }
            if (run == 0)
                onsetContext = Context(snapshot);
            run++;
            if (!emitted && run >= _calibration.BalancePersistenceTicks)
            {
                QueueCandidate(
                    SquatFailureKind.BALANCE_LOSS,
                    direction,
                    SquatFailureDetailKind.NONE,
                    onsetContext,
                    snapshot,
                    SquatFailureEvidenceChannel.COM_POSITION |
                    SquatFailureEvidenceChannel.COM_VELOCITY |
                    SquatFailureEvidenceChannel.SUPPORT_BOUNDS,
                    BalanceMargin(snapshot, direction),
                    BalanceVelocity(snapshot, direction),
                    run,
                    _calibration.BalanceSupportMarginFailureM,
                    _calibration.BalanceOutwardComVelocityMps,
                    _calibration.BalancePersistenceTicks);
                emitted = true;
            }
        }

        private void ResetBalanceRuns()
        {
            _forwardBalanceRun = 0;
            _backwardBalanceRun = 0;
            _leftBalanceRun = 0;
            _rightBalanceRun = 0;
            _forwardBalanceEmitted = false;
            _backwardBalanceEmitted = false;
            _leftBalanceEmitted = false;
            _rightBalanceEmitted = false;
        }

        private static float BalanceMargin(SquatObservationSnapshot snapshot, SquatFailureDirection direction)
        {
            switch (direction)
            {
                case SquatFailureDirection.FORWARD: return snapshot.Support.ComToSupportApFrontMarginM;
                case SquatFailureDirection.BACKWARD: return snapshot.Support.ComToSupportApRearMarginM;
                case SquatFailureDirection.LEFT: return snapshot.Support.ComToSupportMlLeftMarginM;
                case SquatFailureDirection.RIGHT: return snapshot.Support.ComToSupportMlRightMarginM;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        private static float BalanceVelocity(SquatObservationSnapshot snapshot, SquatFailureDirection direction)
        {
            switch (direction)
            {
                case SquatFailureDirection.FORWARD:
                case SquatFailureDirection.BACKWARD:
                    return snapshot.Support.SystemComVelocityWorldMetersPerSecond.Z;
                case SquatFailureDirection.LEFT:
                case SquatFailureDirection.RIGHT:
                    return snapshot.Support.SystemComVelocityWorldMetersPerSecond.X;
                default: throw new ArgumentOutOfRangeException(nameof(direction), direction, null);
            }
        }

        private void UpdateDescentCollapse(SquatObservationSnapshot snapshot)
        {
            if (_hasPhysicalBottom)
            {
                _descentCollapseRun = 0;
                _descentCollapseEmitted = false;
                return;
            }

            bool excessiveDownward =
                snapshot.Bar.IsAvailable && snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y <=
                    -_calibration.DescentCollapseVelocityMps ||
                snapshot.PelvisAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.PelvisLinearVelocityWorldMetersPerSecond.Y <= -_calibration.DescentCollapseVelocityMps;
            bool controlLoss = HasControlLossEvidence(snapshot);
            if (!excessiveDownward || !controlLoss)
            {
                _descentCollapseRun = 0;
                _descentCollapseEmitted = false;
                return;
            }
            if (_descentCollapseRun == 0)
                _descentCollapseContext = Context(snapshot);
            _descentCollapseRun++;
            if (!_descentCollapseEmitted && _descentCollapseRun >= _calibration.DescentCollapsePersistenceTicks)
            {
                float downwardVelocity = MostDownwardVelocity(snapshot);
                QueueCandidate(
                    SquatFailureKind.DESCENT_COLLAPSE,
                    SquatFailureDirection.NONE,
                    SquatFailureDetailKind.NONE,
                    _descentCollapseContext,
                    snapshot,
                    SquatFailureEvidenceChannel.BAR_LINEAR_VELOCITY |
                    SquatFailureEvidenceChannel.PELVIS_LINEAR_VELOCITY |
                    SquatFailureEvidenceChannel.SUPPORT_BOUNDS |
                    SquatFailureEvidenceChannel.JOINT_LIMITS |
                    SquatFailureEvidenceChannel.TRUNK_KINEMATICS,
                    downwardVelocity,
                    ControlLossValue(snapshot),
                    _descentCollapseRun,
                    -_calibration.DescentCollapseVelocityMps,
                    float.NaN,
                    _calibration.DescentCollapsePersistenceTicks);
                _descentCollapseEmitted = true;
            }
        }

        private bool HasControlLossEvidence(SquatObservationSnapshot snapshot)
        {
            bool supportLoss = snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE &&
                !snapshot.Support.HasSupport;
            bool postureLoss = HasHardTrunk(snapshot) || HasCriticalJointLimit(snapshot);
            // A saturated modeled drive is corroborating context only. It is
            // deliberately absent from the selecting evidence provenance.
            return supportLoss || postureLoss;
        }

        private float MostDownwardVelocity(SquatObservationSnapshot snapshot)
        {
            float value = float.PositiveInfinity;
            if (snapshot.Bar.IsAvailable)
                value = Math.Min(value, snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y);
            if (snapshot.PelvisAvailability == SquatTelemetryAvailability.AVAILABLE)
                value = Math.Min(value, snapshot.PelvisLinearVelocityWorldMetersPerSecond.Y);
            return value;
        }

        private float ControlLossValue(SquatObservationSnapshot snapshot)
        {
            if (snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE &&
                !snapshot.Support.HasSupport)
                return 1f;
            if (HasHardTrunk(snapshot) && TryGetMaxTrunkAngle(snapshot, out float trunkAngle))
                return trunkAngle;
            if (HasCriticalJointLimit(snapshot))
                return MaxJointLimitProximity(snapshot);
            return 1f;
        }

        private void UpdateReversal(SquatObservationSnapshot snapshot)
        {
            if (!_hasLegalBottom || _hasAscentEstablished)
                return;

            if (!_hasReversalAttempt && snapshot.Intent.Drive01 >= _calibration.DriveAttemptMinimum01)
            {
                _hasReversalAttempt = true;
                _reversalAttemptTick = snapshot.SimulationTick;
                _reversalAttemptContext = Context(snapshot);
            }
            if (!_hasReversalAttempt || _reversalRecovered)
                return;

            bool recovery = TryGetPreferredVertical(snapshot, out float positionY, out float velocityY, out _) &&
                (velocityY >= _calibration.ReversalRecoveryVelocityMps ||
                 positionY - _physicalBottomPositionY >= _calibration.ReversalRecoveryDisplacementM);
            if (recovery)
            {
                _reversalRecoveryRun++;
                if (_reversalRecoveryRun >= _calibration.ReversalRecoveryPersistenceTicks)
                    _reversalRecovered = true;
            }
            else
            {
                _reversalRecoveryRun = 0;
            }

            if (!_reversalRecovered && TryGetPreferredVertical(snapshot, out _, out _, out _) &&
                snapshot.SimulationTick - _reversalAttemptTick + 1ul >= (ulong)_calibration.ReversalTimeoutTicks)
            {
                QueueCandidate(
                    SquatFailureKind.FAILED_REVERSAL,
                    SquatFailureDirection.NONE,
                    SquatFailureDetailKind.NONE,
                    _reversalAttemptContext,
                    snapshot,
                    SquatFailureEvidenceChannel.DEPTH_LANDMARKS |
                    SquatFailureEvidenceChannel.PLAYER_DRIVE_INTENT |
                    SquatFailureEvidenceChannel.BAR_POSITION |
                    SquatFailureEvidenceChannel.BAR_LINEAR_VELOCITY |
                    SquatFailureEvidenceChannel.PELVIS_POSITION |
                    SquatFailureEvidenceChannel.PELVIS_LINEAR_VELOCITY,
                    snapshot.Intent.Drive01,
                    snapshot.Depth.WorstSideDepthM,
                    snapshot.SimulationTick - _reversalAttemptTick + 1ul,
                    _calibration.DriveAttemptMinimum01,
                    -SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M,
                    _calibration.ReversalTimeoutTicks);
            }
        }

        private void UpdateStall(SquatObservationSnapshot snapshot)
        {
            if (!_hasAscentEstablished || _physicalLockoutReached ||
                !TryGetPreferredVertical(snapshot, out float positionY, out float velocityY, out _))
            {
                _stallRun = 0;
                _stallEmitted = false;
                return;
            }

            bool lowVelocity = velocityY >= -_calibration.BarReversalVelocityMps &&
                velocityY <= _calibration.StallLowAscentVelocityMps;
            bool highDemand = snapshot.DriveAvailability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.MaximumModeledDemand >= _calibration.StallHighDemandThreshold01;
            bool outsideBottom = positionY - _physicalBottomPositionY >= _calibration.AscentEstablishmentDisplacementM;
            bool recovered = _stallRun > 0 &&
                (velocityY >= _calibration.StallRecoveryVelocityMps ||
                 positionY - _stallStartPositionY >= _calibration.StallRecoveryDisplacementM);
            if (recovered)
            {
                _stallRun = 0;
                _stallEmitted = false;
                return;
            }
            bool condition = lowVelocity && highDemand && outsideBottom;
            if (!condition)
            {
                _stallRun = 0;
                _stallEmitted = false;
                return;
            }
            if (_stallRun == 0)
            {
                _stallContext = Context(snapshot);
                _stallStartPositionY = positionY;
            }
            if (positionY - _stallStartPositionY >= _calibration.StallNoProgressDisplacementM)
            {
                _stallRun = 0;
                _stallEmitted = false;
                return;
            }
            _stallRun++;
            if (!_stallEmitted && _stallRun >= _calibration.StallDwellTicks)
            {
                QueueCandidate(
                    SquatFailureKind.MID_ASCENT_STALL,
                    SquatFailureDirection.NONE,
                    SquatFailureDetailKind.NONE,
                    _stallContext,
                    snapshot,
                    SquatFailureEvidenceChannel.BAR_POSITION |
                    SquatFailureEvidenceChannel.BAR_LINEAR_VELOCITY |
                    SquatFailureEvidenceChannel.PELVIS_POSITION |
                    SquatFailureEvidenceChannel.PELVIS_LINEAR_VELOCITY |
                    SquatFailureEvidenceChannel.DRIVE_DEMAND,
                    velocityY,
                    snapshot.MaximumModeledDemand,
                    positionY - _stallStartPositionY,
                    _calibration.StallLowAscentVelocityMps,
                    _calibration.StallHighDemandThreshold01,
                    _calibration.StallNoProgressDisplacementM);
                _stallEmitted = true;
            }
        }

        private void UpdateBarReversal(SquatObservationSnapshot snapshot)
        {
            if (!_hasAscentEstablished || _physicalLockoutReached ||
                !snapshot.Bar.IsAvailable || !_hasBarPosition)
            {
                _barReversalRun = 0;
                _barReversalEmitted = false;
                return;
            }

            float currentY = snapshot.Bar.PositionWorldMeters.Y;
            float downwardStep = _lastBarPositionY - currentY;
            bool downward = snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y <= -_calibration.BarReversalVelocityMps ||
                downwardStep >= _calibration.BarReversalSampleDisplacementM;
            if (!downward)
            {
                _barReversalRun = 0;
                _barReversalEmitted = false;
                return;
            }
            if (_barReversalRun == 0)
            {
                _barReversalReferencePositionY = _lastBarPositionY;
                _barReversalContext = Context(snapshot);
            }
            _barReversalRun++;
            float downwardDisplacement = _barReversalReferencePositionY - currentY;
            if (!_barReversalEmitted && _barReversalRun >= _calibration.BarReversalPersistenceTicks &&
                downwardDisplacement >= _calibration.BarReversalDisplacementM)
            {
                QueueCandidate(
                    SquatFailureKind.BAR_REVERSAL,
                    SquatFailureDirection.NONE,
                    SquatFailureDetailKind.NONE,
                    _barReversalContext,
                    snapshot,
                    SquatFailureEvidenceChannel.BAR_POSITION |
                    SquatFailureEvidenceChannel.BAR_LINEAR_VELOCITY,
                    downwardDisplacement,
                    snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y,
                    _barReversalRun,
                    _calibration.BarReversalDisplacementM,
                    -_calibration.BarReversalVelocityMps,
                    _calibration.BarReversalPersistenceTicks);
                _barReversalEmitted = true;
            }
        }

        private void UpdatePostureAndBarLoss(SquatObservationSnapshot snapshot)
        {
            bool hardTrunk = HasHardTrunk(snapshot);
            UpdatePostureRun(
                snapshot,
                hardTrunk,
                SquatFailureDetailKind.TRUNK_HARD_LIMIT,
                ref _trunkFailureRun,
                ref _trunkFailureContext,
                ref _trunkFailureEmitted);

            bool criticalJoint = HasCriticalJointLimit(snapshot);
            UpdatePostureRun(
                snapshot,
                criticalJoint,
                SquatFailureDetailKind.CRITICAL_JOINT_LIMIT,
                ref _jointLimitFailureRun,
                ref _jointLimitFailureContext,
                ref _jointLimitFailureEmitted);

            if (!snapshot.Bar.IsAvailable || snapshot.Bar.SaddleAvailability != SquatTelemetryAvailability.AVAILABLE)
            {
                _saddleSeparationRun = 0;
                _saddleSeparationEmitted = false;
                return;
            }

            if (snapshot.Bar.SaddleBroken)
            {
                if (!_saddleBrokenEmitted)
                {
                    QueueCandidate(
                        SquatFailureKind.POSTURE_OR_BAR_LOSS,
                        SquatFailureDirection.NONE,
                        SquatFailureDetailKind.SADDLE_BROKEN,
                        Context(snapshot),
                        snapshot,
                        SquatFailureEvidenceChannel.SADDLE_STATE,
                        1d,
                        snapshot.Bar.SaddleSeparationMeters,
                        1d,
                        1d,
                        _calibration.SaddleSeparationFailureM,
                        _calibration.SaddlePersistenceTicks);
                    _saddleBrokenEmitted = true;
                }
                return;
            }

            if (!snapshot.Bar.SaddleAttached)
            {
                if (!_couplingLossEmitted)
                {
                    QueueCandidate(
                        SquatFailureKind.POSTURE_OR_BAR_LOSS,
                        SquatFailureDirection.NONE,
                        SquatFailureDetailKind.BAR_COUPLING_LOSS,
                        Context(snapshot),
                        snapshot,
                        SquatFailureEvidenceChannel.SADDLE_STATE,
                        0d,
                        snapshot.Bar.SaddleSeparationMeters,
                        1d,
                        1d,
                        _calibration.SaddleSeparationFailureM,
                        _calibration.SaddlePersistenceTicks);
                    _couplingLossEmitted = true;
                }
                return;
            }

            bool separation = snapshot.Bar.SaddleSeparationMeters >= _calibration.SaddleSeparationFailureM;
            if (!separation)
            {
                _saddleSeparationRun = 0;
                _saddleSeparationEmitted = false;
                return;
            }
            if (_saddleSeparationRun == 0)
                _saddleSeparationContext = Context(snapshot);
            _saddleSeparationRun++;
            if (!_saddleSeparationEmitted && _saddleSeparationRun >= _calibration.SaddlePersistenceTicks)
            {
                QueueCandidate(
                    SquatFailureKind.POSTURE_OR_BAR_LOSS,
                    SquatFailureDirection.NONE,
                    SquatFailureDetailKind.SADDLE_SEPARATION,
                    _saddleSeparationContext,
                    snapshot,
                    SquatFailureEvidenceChannel.SADDLE_STATE,
                    snapshot.Bar.SaddleSeparationMeters,
                    1d,
                    _saddleSeparationRun,
                    _calibration.SaddleSeparationFailureM,
                    1d,
                    _calibration.SaddlePersistenceTicks);
                _saddleSeparationEmitted = true;
            }
        }

        private void UpdatePostureRun(
            SquatObservationSnapshot snapshot,
            bool condition,
            SquatFailureDetailKind detail,
            ref int run,
            ref SquatFailureContext onsetContext,
            ref bool emitted)
        {
            if (!condition)
            {
                run = 0;
                emitted = false;
                return;
            }
            if (run == 0)
                onsetContext = Context(snapshot);
            run++;
            if (!emitted && run >= _calibration.PosturePersistenceTicks)
            {
                double measured = detail == SquatFailureDetailKind.TRUNK_HARD_LIMIT
                    ? TryGetMaxTrunkAngle(snapshot, out float trunk) ? trunk : 0d
                    : MaxJointLimitProximity(snapshot);
                QueueCandidate(
                    SquatFailureKind.POSTURE_OR_BAR_LOSS,
                    SquatFailureDirection.NONE,
                    detail,
                    onsetContext,
                    snapshot,
                    detail == SquatFailureDetailKind.TRUNK_HARD_LIMIT
                        ? SquatFailureEvidenceChannel.TRUNK_KINEMATICS | SquatFailureEvidenceChannel.JOINT_KINEMATICS
                        : SquatFailureEvidenceChannel.JOINT_LIMITS,
                    measured,
                    run,
                    1d,
                    detail == SquatFailureDetailKind.TRUNK_HARD_LIMIT
                        ? _calibration.TrunkHardLimitRadians
                        : _calibration.CriticalJointLimitProximity,
                    _calibration.PosturePersistenceTicks,
                    1d);
                emitted = true;
            }
        }

        /// <summary>
        /// Sequential lockout tracking only.
        /// <para>
        /// Elapsed time inside the completion region is NOT irreversible
        /// evidence: measured 25 kg evidence shows bilateral knee/hip
        /// extension, trunk erectness, and bar settling still converging more
        /// than a second after the bar height enters the region. This therefore
        /// records physical lockout and the first credible completion-region
        /// entry and never latches FAILED_LOCKOUT. FAILED_LOCKOUT is decided
        /// once - as a terminal postcondition - when the attempt is
        /// authoritatively over.
        /// </para>
        /// </summary>
        private void UpdateLockout(SquatObservationSnapshot snapshot)
        {
            if (!_hasAscentEstablished || _physicalLockoutReached)
                return;
            if (IsPhysicalLockout(snapshot))
            {
                _physicalLockoutReached = true;
                _completionRegionDwellRun = 0;
                return;
            }

            if (!IsInLockoutCompletionRegion(snapshot) || !HasLockoutEvidence(snapshot))
            {
                _completionRegionDwellRun = 0;
                return;
            }

            if (!_hasCompletionRegionEntry)
            {
                _hasCompletionRegionEntry = true;
                _completionRegionEntryContext = Context(snapshot);
            }

            _completionRegionDwellRun++;
            if (_completionRegionDwellRun > _maximumCompletionRegionDwellTicks)
                _maximumCompletionRegionDwellTicks = _completionRegionDwellRun;
        }

        /// <summary>
        /// The vicinity in which a final lockout is physically possible. This
        /// is neither lockout nor failed lockout.
        /// </summary>
        private bool IsInLockoutCompletionRegion(SquatObservationSnapshot snapshot)
        {
            return _hasStandingReference && snapshot.Bar.IsAvailable &&
                snapshot.Bar.PositionWorldMeters.Y >= _standingReferenceY - _calibration.LockoutHeightToleranceM;
        }

        private static bool HasLockoutEvidence(SquatObservationSnapshot snapshot)
        {
            if (!snapshot.Bar.IsAvailable ||
                snapshot.Joints.LeftKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.LeftHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE)
                return false;

            return snapshot.TrunkAvailability == SquatTelemetryAvailability.AVAILABLE ||
                (snapshot.Joints.Abdomen.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                 snapshot.Joints.Thorax.JointAvailability == SquatTelemetryAvailability.AVAILABLE);
        }

        private bool IsPhysicalLockout(SquatObservationSnapshot snapshot)
        {
            if (!snapshot.Bar.IsAvailable ||
                !_hasStandingReference ||
                snapshot.Bar.PositionWorldMeters.Y < _standingReferenceY - _calibration.LockoutHeightToleranceM ||
                snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length > _calibration.LockoutBarStillVelocityMps ||
                snapshot.Bar.AngularVelocityBarRadiansPerSecond.Length > _calibration.LockoutBarStillAngularVelocityRadS)
                return false;
            if (snapshot.Joints.LeftKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.LeftHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE)
                return false;
            if (MaxKneeAngle(snapshot) > _calibration.LockoutKneeToleranceRadians ||
                MaxHipAngle(snapshot) > _calibration.LockoutHipToleranceRadians)
                return false;
            return TryGetMaxTrunkAngle(snapshot, out float trunkAngle) &&
                trunkAngle <= _calibration.LockoutTrunkToleranceRadians;
        }

        private bool IsLegalBottom(SquatObservationSnapshot snapshot)
        {
            return snapshot.Depth.Availability == SquatTelemetryAvailability.AVAILABLE &&
                snapshot.Depth.BilateralGameJudgmentQualified;
        }

        private bool TryGetPreferredVertical(
            SquatObservationSnapshot snapshot,
            out float positionY,
            out float velocityY,
            out bool usesBar)
        {
            if (snapshot.Bar.IsAvailable)
            {
                positionY = snapshot.Bar.PositionWorldMeters.Y;
                velocityY = snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y;
                usesBar = true;
            return true;
        }
            if (snapshot.PelvisAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                positionY = snapshot.PelvisPositionWorldMeters.Y;
                velocityY = snapshot.PelvisLinearVelocityWorldMetersPerSecond.Y;
                usesBar = false;
                return true;
            }
            positionY = float.NaN;
            velocityY = float.NaN;
            usesBar = false;
            return false;
        }

        private bool HasHardTrunk(SquatObservationSnapshot snapshot)
        {
            return TryGetMaxTrunkAngle(snapshot, out float angle) && angle >= _calibration.TrunkHardLimitRadians;
        }

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

        private bool HasCriticalJointLimit(SquatObservationSnapshot snapshot)
        {
            return IsCritical(snapshot.Joints.LeftKnee) ||
                IsCritical(snapshot.Joints.RightKnee) ||
                IsCritical(snapshot.Joints.LeftHip) ||
                IsCritical(snapshot.Joints.RightHip) ||
                IsCritical(snapshot.Joints.LeftAnkle) ||
                IsCritical(snapshot.Joints.RightAnkle) ||
                IsCritical(snapshot.Joints.Abdomen) ||
                IsCritical(snapshot.Joints.Thorax);
        }

        private bool IsCritical(SquatJointObservation joint) =>
            joint.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
            joint.LimitProximity >= _calibration.CriticalJointLimitProximity;

        private static float MaxJointLimitProximity(SquatObservationSnapshot snapshot)
        {
            float maximum = 0f;
            if (snapshot.Joints.LeftKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.LeftKnee.LimitProximity);
            if (snapshot.Joints.RightKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.RightKnee.LimitProximity);
            if (snapshot.Joints.LeftHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.LeftHip.LimitProximity);
            if (snapshot.Joints.RightHip.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.RightHip.LimitProximity);
            if (snapshot.Joints.LeftAnkle.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.LeftAnkle.LimitProximity);
            if (snapshot.Joints.RightAnkle.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.RightAnkle.LimitProximity);
            if (snapshot.Joints.Abdomen.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.Abdomen.LimitProximity);
            if (snapshot.Joints.Thorax.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                maximum = Math.Max(maximum, snapshot.Joints.Thorax.LimitProximity);
            return maximum;
        }

        private static float MaxKneeAngle(SquatObservationSnapshot snapshot) => Math.Max(
            Math.Abs(snapshot.Joints.LeftKnee.ActualAngleRadians),
            Math.Abs(snapshot.Joints.RightKnee.ActualAngleRadians));

        private static float MaxHipAngle(SquatObservationSnapshot snapshot) => Math.Max(
            Math.Abs(snapshot.Joints.LeftHip.ActualAngleRadians),
            Math.Abs(snapshot.Joints.RightHip.ActualAngleRadians));

        private static SquatFailureContext Context(SquatObservationSnapshot snapshot) => new SquatFailureContext(
            snapshot.SimulationTick,
            snapshot.SimulationTimeSeconds,
            snapshot.State,
            snapshot.Direction,
            snapshot.Sq,
            snapshot.Intent.Held,
            snapshot.Intent.Drive01);

        private void QueueCandidate(
            SquatFailureKind kind,
            SquatFailureDirection direction,
            SquatFailureDetailKind detail,
            SquatFailureContext onsetContext,
            SquatObservationSnapshot snapshot,
            SquatFailureEvidenceChannel evidenceChannels,
            double measuredValueA,
            double measuredValueB,
            double measuredValueC,
            double thresholdValueA,
            double thresholdValueB,
            double thresholdValueC)
        {
            if (_candidateCount >= _candidateBuffer.Length)
                throw new InvalidOperationException("The bounded squat failure candidate buffer is exhausted.");
            _candidateBuffer[_candidateCount++] = new SquatFailureCandidate(
                kind,
                direction,
                detail,
                onsetContext.SimulationTick,
                onsetContext.SimulationTimeSeconds,
                snapshot.SimulationTick,
                snapshot.SimulationTimeSeconds,
                onsetContext,
                Context(snapshot),
                evidenceChannels,
                measuredValueA,
                measuredValueB,
                measuredValueC,
                thresholdValueA,
                thresholdValueB,
                thresholdValueC,
                snapshot.Bar.IsAvailable ? snapshot.Bar.LoadKilograms : float.NaN);
        }

        private void SortCandidates()
        {
            for (int index = 1; index < _candidateCount; index++)
            {
                SquatFailureCandidate value = _candidateBuffer[index];
                int prior = index - 1;
                while (prior >= 0 && CompareCandidates(value, _candidateBuffer[prior]) < 0)
                {
                    _candidateBuffer[prior + 1] = _candidateBuffer[prior];
                    prior--;
                }
                _candidateBuffer[prior + 1] = value;
            }
        }

        private void RegisterCandidate(SquatFailureCandidate candidate)
        {
            if (HasEvent(candidate))
                return;
            if (!_hasPrimary)
            {
                _hasPrimary = true;
                _primaryCandidate = candidate;
                _primaryEvidence = FreezeEvidence();
                _primaryLoadKilograms = candidate.LoadKilograms;
                return;
            }
            if (_secondaryEventCount >= _secondaryEvents.Length)
                throw new InvalidOperationException("The bounded squat failure event buffer is exhausted.");
            _secondaryEvents[_secondaryEventCount++] = CreateEvent(candidate);
        }

        private bool HasEvent(SquatFailureCandidate candidate)
        {
            if (_hasPrimary && SameCause(_primaryCandidate, candidate))
                return true;
            for (int index = 0; index < _secondaryEventCount; index++)
            {
                SquatFailureEvent existing = _secondaryEvents[index];
                if (existing.Kind == candidate.Kind && existing.Direction == candidate.Direction &&
                    existing.Detail == candidate.Detail)
                    return true;
            }
            return false;
        }

        private static bool SameCause(SquatFailureCandidate left, SquatFailureCandidate right) =>
            left.Kind == right.Kind && left.Direction == right.Direction && left.Detail == right.Detail;

        private void SortSecondaryEvents()
        {
            for (int index = 1; index < _secondaryEventCount; index++)
            {
                SquatFailureEvent value = _secondaryEvents[index];
                int prior = index - 1;
                while (prior >= 0 && CompareEvents(value, _secondaryEvents[prior]) < 0)
                {
                    _secondaryEvents[prior + 1] = _secondaryEvents[prior];
                    prior--;
                }
                _secondaryEvents[prior + 1] = value;
            }
        }

        private static int CompareCandidates(SquatFailureCandidate left, SquatFailureCandidate right)
        {
            int comparison = left.LatchedTick.CompareTo(right.LatchedTick);
            if (comparison != 0)
                return comparison;
            comparison = KindRank(left.Kind).CompareTo(KindRank(right.Kind));
            if (comparison != 0)
                return comparison;
            comparison = left.Kind.CompareTo(right.Kind);
            if (comparison != 0)
                return comparison;
            comparison = DetailRank(left.Detail).CompareTo(DetailRank(right.Detail));
            if (comparison != 0)
                return comparison;
            comparison = left.Detail.CompareTo(right.Detail);
            if (comparison != 0)
                return comparison;
            return left.Direction.CompareTo(right.Direction);
        }

        private static int CompareEvents(SquatFailureEvent left, SquatFailureEvent right)
        {
            int comparison = left.LatchedTick.CompareTo(right.LatchedTick);
            if (comparison != 0)
                return comparison;
            comparison = KindRank(left.Kind).CompareTo(KindRank(right.Kind));
            if (comparison != 0)
                return comparison;
            comparison = left.Kind.CompareTo(right.Kind);
            if (comparison != 0)
                return comparison;
            comparison = DetailRank(left.Detail).CompareTo(DetailRank(right.Detail));
            if (comparison != 0)
                return comparison;
            comparison = left.Detail.CompareTo(right.Detail);
            if (comparison != 0)
                return comparison;
            return left.Direction.CompareTo(right.Direction);
        }

        private static int KindRank(SquatFailureKind kind)
        {
            switch (kind)
            {
                case SquatFailureKind.BALANCE_LOSS: return 10;
                case SquatFailureKind.DESCENT_COLLAPSE: return 20;
                case SquatFailureKind.FAILED_REVERSAL: return 30;
                case SquatFailureKind.MID_ASCENT_STALL: return 40;
                case SquatFailureKind.BAR_REVERSAL: return 50;
                case SquatFailureKind.POSTURE_OR_BAR_LOSS: return 60;
                case SquatFailureKind.FAILED_LOCKOUT: return 70;
                default: return int.MaxValue;
            }
        }

        private static int DetailRank(SquatFailureDetailKind detail)
        {
            switch (detail)
            {
                case SquatFailureDetailKind.TRUNK_HARD_LIMIT: return 10;
                case SquatFailureDetailKind.CRITICAL_JOINT_LIMIT: return 20;
                case SquatFailureDetailKind.SADDLE_BROKEN: return 30;
                case SquatFailureDetailKind.SADDLE_SEPARATION: return 40;
                case SquatFailureDetailKind.BAR_COUPLING_LOSS: return 50;
                default: return int.MaxValue;
            }
        }

        private SquatFailureEvent CreateEvent(SquatFailureCandidate candidate)
        {
            SquatFailureMeasurement[] measurements = new SquatFailureMeasurement[3];
            SquatFailureThresholdValue[] thresholds = new SquatFailureThresholdValue[3];
            int measurementCount = 0;
            int thresholdCount = 0;
            switch (candidate.Kind)
            {
                case SquatFailureKind.BALANCE_LOSS:
                    AddMeasurement(measurements, ref measurementCount, "support_margin", "m", candidate.MeasuredValueA);
                    AddMeasurement(measurements, ref measurementCount, "outward_com_velocity", "m/s", candidate.MeasuredValueB);
                    AddMeasurement(measurements, ref measurementCount, "persistence", "ticks", candidate.MeasuredValueC);
                    AddThreshold(thresholds, ref thresholdCount, "support_margin_failure", "m", candidate.ThresholdValueA);
                    AddThreshold(thresholds, ref thresholdCount, "outward_com_velocity", "m/s", candidate.ThresholdValueB);
                    AddThreshold(thresholds, ref thresholdCount, "persistence", "ticks", candidate.ThresholdValueC);
                    break;
                case SquatFailureKind.DESCENT_COLLAPSE:
                    AddMeasurement(measurements, ref measurementCount, "downward_velocity", "m/s", candidate.MeasuredValueA);
                    AddMeasurement(measurements, ref measurementCount, "control_loss_signal", "1", candidate.MeasuredValueB);
                    AddMeasurement(measurements, ref measurementCount, "persistence", "ticks", candidate.MeasuredValueC);
                    AddThreshold(thresholds, ref thresholdCount, "downward_velocity", "m/s", candidate.ThresholdValueA);
                    AddThreshold(thresholds, ref thresholdCount, "persistence", "ticks", candidate.ThresholdValueC);
                    break;
                case SquatFailureKind.FAILED_REVERSAL:
                    AddMeasurement(measurements, ref measurementCount, "drive_attempt", "1", candidate.MeasuredValueA);
                    AddMeasurement(measurements, ref measurementCount, "bottom_depth", "m", candidate.MeasuredValueB);
                    AddMeasurement(measurements, ref measurementCount, "no_recovery_window", "ticks", candidate.MeasuredValueC);
                    AddThreshold(thresholds, ref thresholdCount, "drive_attempt_minimum", "1", candidate.ThresholdValueA);
                    AddThreshold(thresholds, ref thresholdCount, "game_judgment_margin", "m", candidate.ThresholdValueB);
                    AddThreshold(thresholds, ref thresholdCount, "reversal_timeout", "ticks", candidate.ThresholdValueC);
                    break;
                case SquatFailureKind.MID_ASCENT_STALL:
                    AddMeasurement(measurements, ref measurementCount, "ascent_velocity", "m/s", candidate.MeasuredValueA);
                    AddMeasurement(measurements, ref measurementCount, "modeled_demand", "1", candidate.MeasuredValueB);
                    AddMeasurement(measurements, ref measurementCount, "progress", "m", candidate.MeasuredValueC);
                    AddThreshold(thresholds, ref thresholdCount, "low_ascent_velocity", "m/s", candidate.ThresholdValueA);
                    AddThreshold(thresholds, ref thresholdCount, "high_modeled_demand", "1", candidate.ThresholdValueB);
                    AddThreshold(thresholds, ref thresholdCount, "no_progress", "m", candidate.ThresholdValueC);
                    break;
                case SquatFailureKind.BAR_REVERSAL:
                    AddMeasurement(measurements, ref measurementCount, "downward_displacement", "m", candidate.MeasuredValueA);
                    AddMeasurement(measurements, ref measurementCount, "bar_velocity", "m/s", candidate.MeasuredValueB);
                    AddMeasurement(measurements, ref measurementCount, "persistence", "ticks", candidate.MeasuredValueC);
                    AddThreshold(thresholds, ref thresholdCount, "downward_displacement", "m", candidate.ThresholdValueA);
                    AddThreshold(thresholds, ref thresholdCount, "downward_velocity", "m/s", candidate.ThresholdValueB);
                    AddThreshold(thresholds, ref thresholdCount, "persistence", "ticks", candidate.ThresholdValueC);
                    break;
                case SquatFailureKind.POSTURE_OR_BAR_LOSS:
                    if (candidate.Detail == SquatFailureDetailKind.TRUNK_HARD_LIMIT)
                    {
                        AddMeasurement(measurements, ref measurementCount, "trunk_angle", "rad", candidate.MeasuredValueA);
                        AddMeasurement(measurements, ref measurementCount, "persistence", "ticks", candidate.MeasuredValueB);
                        AddThreshold(thresholds, ref thresholdCount, "trunk_hard_limit", "rad", candidate.ThresholdValueA);
                        AddThreshold(thresholds, ref thresholdCount, "posture_persistence", "ticks", candidate.ThresholdValueB);
                    }
                    else if (candidate.Detail == SquatFailureDetailKind.CRITICAL_JOINT_LIMIT)
                    {
                        AddMeasurement(measurements, ref measurementCount, "joint_limit_proximity", "1", candidate.MeasuredValueA);
                        AddMeasurement(measurements, ref measurementCount, "persistence", "ticks", candidate.MeasuredValueB);
                        AddThreshold(thresholds, ref thresholdCount, "critical_joint_limit_proximity", "1", candidate.ThresholdValueA);
                        AddThreshold(thresholds, ref thresholdCount, "posture_persistence", "ticks", candidate.ThresholdValueB);
                    }
                    else if (candidate.Detail == SquatFailureDetailKind.SADDLE_SEPARATION)
                    {
                        AddMeasurement(measurements, ref measurementCount, "saddle_separation", "m", candidate.MeasuredValueA);
                        AddMeasurement(measurements, ref measurementCount, "persistence", "ticks", candidate.MeasuredValueC);
                        AddThreshold(thresholds, ref thresholdCount, "saddle_separation_failure", "m", candidate.ThresholdValueA);
                        AddThreshold(thresholds, ref thresholdCount, "saddle_persistence", "ticks", candidate.ThresholdValueC);
                    }
                    else
                    {
                        AddMeasurement(measurements, ref measurementCount, "saddle_state", "1", candidate.MeasuredValueA);
                        AddMeasurement(measurements, ref measurementCount, "saddle_separation", "m", candidate.MeasuredValueB);
                        AddThreshold(thresholds, ref thresholdCount, "saddle_attached", "bool", candidate.ThresholdValueA);
                        AddThreshold(thresholds, ref thresholdCount, "saddle_separation_failure", "m", candidate.ThresholdValueB);
                        AddThreshold(thresholds, ref thresholdCount, "saddle_persistence", "ticks", candidate.ThresholdValueC);
                    }
                    break;
                case SquatFailureKind.FAILED_LOCKOUT:
                    // The dwell is observational only. The selectors are
                    // authoritative terminality, credible completion-region
                    // entry, and the direct lockout posture/stillness bounds.
                    AddMeasurement(measurements, ref measurementCount, "completion_region_dwell_observed", "ticks", candidate.MeasuredValueA);
                    AddMeasurement(measurements, ref measurementCount, "knee_angle", "rad", candidate.MeasuredValueB);
                    AddMeasurement(measurements, ref measurementCount, "hip_angle", "rad", candidate.MeasuredValueC);
                    AddThreshold(thresholds, ref thresholdCount, "knee_tolerance", "rad", candidate.ThresholdValueB);
                    AddThreshold(thresholds, ref thresholdCount, "hip_tolerance", "rad", candidate.ThresholdValueC);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(candidate));
            }
            return new SquatFailureEvent(
                candidate,
                measurements,
                measurementCount,
                thresholds,
                thresholdCount,
                CalibrationVersion);
        }

        private static void AddMeasurement(
            SquatFailureMeasurement[] values,
            ref int count,
            string name,
            string unit,
            double value)
        {
            if (double.IsNaN(value))
                return;
            values[count++] = new SquatFailureMeasurement(name, unit, value);
        }

        private void AddThreshold(
            SquatFailureThresholdValue[] values,
            ref int count,
            string name,
            string unit,
            double value)
        {
            if (double.IsNaN(value))
                return;
            SquatFailureCalibrationStatus status = SquatFailureCalibrationStatus.PROVISIONAL_GAME_CALIBRATION;
            for (int index = 0; index < _calibration.Descriptors.Count; index++)
            {
                SquatFailureCalibrationDescriptor descriptor = _calibration.Descriptors[index];
                if (string.Equals(descriptor.Name, name, StringComparison.Ordinal))
                {
                    status = descriptor.Status;
                    break;
                }
            }
            values[count++] = new SquatFailureThresholdValue(name, unit, value, CalibrationVersion, status);
        }
    }
}
