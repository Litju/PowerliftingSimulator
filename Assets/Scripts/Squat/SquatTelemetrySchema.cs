using System;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public enum SquatTelemetryAvailabilitySemantics : byte
    {
        REQUIRED,
        OPTIONAL,
        EXPLICIT_NOT_AVAILABLE,
        NOT_OBSERVABLE
    }

    public enum SquatTelemetryChannelId : ushort
    {
        SimulationTick,
        SimulationTimeSeconds,
        FixedStepSeconds,
        AttemptRelativeTimeSeconds,
        AttemptRelativeTimeAvailability,
        SquatState,
        PhaseDirection,
        SqReferencePhase,
        IntentBrace01,
        IntentYield01,
        IntentDrive01,
        IntentBalanceX,
        IntentGrip01,
        IntentEdgeFlags,
        IntentHeldFlags,
        BarAvailable,
        BarPositionWorld,
        BarLinearVelocityWorld,
        BarOrientationWorldFromBar,
        BarAngularVelocityBar,
        BarLoadKg,
        SaddleAvailability,
        SaddleAttached,
        SaddleBroken,
        SaddleSeparationM,
        DepthLandmarksAvailability,
        LeftHipCreaseY,
        RightHipCreaseY,
        LeftKneeTopY,
        RightKneeTopY,
        LeftDepthM,
        RightDepthM,
        WorstSideDepthM,
        DepthMarginM,
        SystemComAvailability,
        SystemComPositionWorld,
        SystemComVelocityWorld,
        SystemMassKg,
        SupportAvailability,
        HasSupport,
        SupportApMinM,
        SupportApMaxM,
        SupportMlMinM,
        SupportMlMaxM,
        SupportPlaneY,
        SupportCenterWorld,
        ComToSupportApFrontMarginM,
        ComToSupportApRearMarginM,
        ComToSupportMlRightMarginM,
        ComToSupportMlLeftMarginM,
        SupportContactCount,
        EngineContactPointAvailability,
        EngineContactPointWorld,
        TotalNormalImpulseNs,
        LeftFootAvailability,
        LeftFootInContact,
        LeftFootContactCount,
        LeftFootCompletedContactCount,
        LeftFootSlipAccumulatedM,
        LeftFootSlipSpeedMps,
        RightFootAvailability,
        RightFootInContact,
        RightFootContactCount,
        RightFootCompletedContactCount,
        RightFootSlipAccumulatedM,
        RightFootSlipSpeedMps,
        PelvisAvailability,
        PelvisPositionWorld,
        PelvisLinearVelocityWorld,
        TrunkAvailability,
        ThoraxOrientationWorldFromBody,
        TrunkWorldPitchRad,
        LeftKneeJointAvailability,
        LeftKneeDriveAvailability,
        LeftKneeActualAngleRad,
        LeftKneeActualAngularVelocityRadS,
        LeftKneeReferenceAngleRad,
        LeftKneeActualReferenceErrorRad,
        LeftKneeLimitProximity,
        LeftKneeModeledDemand,
        LeftKneeMaximumForceNm,
        LeftKneeActivation,
        LeftKneeCapacityScale,
        RightKneeJointAvailability,
        RightKneeDriveAvailability,
        RightKneeActualAngleRad,
        RightKneeActualAngularVelocityRadS,
        RightKneeReferenceAngleRad,
        RightKneeActualReferenceErrorRad,
        RightKneeLimitProximity,
        RightKneeModeledDemand,
        RightKneeMaximumForceNm,
        RightKneeActivation,
        RightKneeCapacityScale,
        LeftHipJointAvailability,
        LeftHipDriveAvailability,
        LeftHipActualAngleRad,
        LeftHipActualAngularVelocityRadS,
        LeftHipReferenceAngleRad,
        LeftHipActualReferenceErrorRad,
        LeftHipLimitProximity,
        LeftHipModeledDemand,
        LeftHipMaximumForceNm,
        LeftHipActivation,
        LeftHipCapacityScale,
        RightHipJointAvailability,
        RightHipDriveAvailability,
        RightHipActualAngleRad,
        RightHipActualAngularVelocityRadS,
        RightHipReferenceAngleRad,
        RightHipActualReferenceErrorRad,
        RightHipLimitProximity,
        RightHipModeledDemand,
        RightHipMaximumForceNm,
        RightHipActivation,
        RightHipCapacityScale,
        LeftAnkleJointAvailability,
        LeftAnkleDriveAvailability,
        LeftAnkleActualAngleRad,
        LeftAnkleActualAngularVelocityRadS,
        LeftAnkleReferenceAngleRad,
        LeftAnkleActualReferenceErrorRad,
        LeftAnkleLimitProximity,
        LeftAnkleModeledDemand,
        LeftAnkleMaximumForceNm,
        LeftAnkleActivation,
        LeftAnkleCapacityScale,
        RightAnkleJointAvailability,
        RightAnkleDriveAvailability,
        RightAnkleActualAngleRad,
        RightAnkleActualAngularVelocityRadS,
        RightAnkleReferenceAngleRad,
        RightAnkleActualReferenceErrorRad,
        RightAnkleLimitProximity,
        RightAnkleModeledDemand,
        RightAnkleMaximumForceNm,
        RightAnkleActivation,
        RightAnkleCapacityScale,
        AbdomenJointAvailability,
        AbdomenDriveAvailability,
        AbdomenActualAngleRad,
        AbdomenActualAngularVelocityRadS,
        AbdomenReferenceAngleRad,
        AbdomenActualReferenceErrorRad,
        AbdomenLimitProximity,
        AbdomenModeledDemand,
        AbdomenMaximumForceNm,
        AbdomenActivation,
        AbdomenCapacityScale,
        ThoraxJointAvailability,
        ThoraxDriveAvailability,
        ThoraxActualAngleRad,
        ThoraxActualAngularVelocityRadS,
        ThoraxReferenceAngleRad,
        ThoraxActualReferenceErrorRad,
        ThoraxLimitProximity,
        ThoraxModeledDemand,
        ThoraxMaximumForceNm,
        ThoraxActivation,
        ThoraxCapacityScale,
        DriveAvailability,
        DriveSaturated,
        MaximumModeledDemand,
        UnmeasuredGroundReactionForce,
        Count
    }

    public readonly struct SquatTelemetryChannelDescriptor
    {
        public SquatTelemetryChannelDescriptor(
            SquatTelemetryChannelId id,
            string canonicalName,
            string unit,
            SquatTelemetryFrame frame,
            SquatTelemetrySourceClass sourceClass,
            SquatTelemetryAvailabilitySemantics availabilitySemantics,
            string provenanceVersion,
            string claimNote)
        {
            if (string.IsNullOrEmpty(canonicalName) || string.IsNullOrEmpty(unit) ||
                string.IsNullOrEmpty(provenanceVersion) || string.IsNullOrEmpty(claimNote))
                throw new ArgumentException("A telemetry descriptor requires complete metadata.");
            if (sourceClass == SquatTelemetrySourceClass.NOT_OBSERVABLE &&
                availabilitySemantics != SquatTelemetryAvailabilitySemantics.NOT_OBSERVABLE)
                throw new ArgumentException("A NOT_OBSERVABLE channel must declare NOT_OBSERVABLE availability.");

            Id = id;
            CanonicalName = canonicalName;
            Unit = unit;
            Frame = frame;
            SourceClass = sourceClass;
            AvailabilitySemantics = availabilitySemantics;
            ProvenanceVersion = provenanceVersion;
            ClaimNote = claimNote;
        }

        public SquatTelemetryChannelId Id { get; }
        public string CanonicalName { get; }
        public string Unit { get; }
        public SquatTelemetryFrame Frame { get; }
        public string FrameId => SquatTelemetrySchema.FrameId(Frame);
        public SquatTelemetrySourceClass SourceClass { get; }
        public SquatTelemetryAvailabilitySemantics AvailabilitySemantics { get; }
        public string ProvenanceVersion { get; }
        public string ClaimNote { get; }
    }

    public static class SquatTelemetrySchema
    {
        public const string SchemaId = SquatObservationSnapshot.SchemaId;
        public const string TraceSchemaId = SquatTrace.SchemaVersion;
        public const string CatalogVersion = "GAM12_P1_SQUAT_TELEMETRY_CATALOG_V1";
        public const string SamplingPointId = "POST_PHYSICS_AFTER_PHYSICAL_OBSERVATION_AND_SQUAT_OBSERVERS";
        public const string MasterSpecId = "PSMS-SQ-17";

        private static readonly SquatTelemetryChannelDescriptor[] Descriptors = CreateDescriptors();

        static SquatTelemetrySchema()
        {
            ValidateComplete();
        }

        public static int ChannelCount => Descriptors.Length;

        public static SquatTelemetryChannelDescriptor ChannelAt(int index)
        {
            if (index < 0 || index >= Descriptors.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return Descriptors[index];
        }

        public static SquatTelemetryChannelDescriptor GetDescriptor(SquatTelemetryChannelId id)
        {
            int index = (int)id;
            if (index < 0 || index >= Descriptors.Length)
                throw new ArgumentOutOfRangeException(nameof(id));
            return Descriptors[index];
        }

        public static bool CanReceiveRuntimeValue(SquatTelemetryChannelId id)
        {
            SquatTelemetryChannelDescriptor descriptor = GetDescriptor(id);
            return descriptor.SourceClass != SquatTelemetrySourceClass.NOT_OBSERVABLE &&
                descriptor.AvailabilitySemantics != SquatTelemetryAvailabilitySemantics.NOT_OBSERVABLE;
        }

        public static string FrameId(SquatTelemetryFrame frame)
        {
            switch (frame)
            {
                case SquatTelemetryFrame.NONE: return "NONE";
                case SquatTelemetryFrame.WORLD: return CoordinateContract.WorldFrameId;
                case SquatTelemetryFrame.ATHLETE_LOCAL: return "B_i";
                case SquatTelemetryFrame.JOINT: return "J_i";
                case SquatTelemetryFrame.BAR: return CoordinateContract.BarFrameId;
                default: throw new ArgumentOutOfRangeException(nameof(frame), frame, null);
            }
        }

        public static void ValidateComplete()
        {
            for (int index = 0; index < Descriptors.Length; index++)
            {
                SquatTelemetryChannelDescriptor descriptor = Descriptors[index];
                if ((int)descriptor.Id != index || string.IsNullOrEmpty(descriptor.CanonicalName) ||
                    string.IsNullOrEmpty(descriptor.Unit) || string.IsNullOrEmpty(descriptor.FrameId) ||
                    string.IsNullOrEmpty(descriptor.ProvenanceVersion) || string.IsNullOrEmpty(descriptor.ClaimNote))
                    throw new InvalidOperationException("The squat telemetry channel catalog is incomplete at index " + index + ".");
            }
        }

        private static SquatTelemetryChannelDescriptor[] CreateDescriptors()
        {
            SquatTelemetryChannelDescriptor[] descriptors = new SquatTelemetryChannelDescriptor[(int)SquatTelemetryChannelId.Count];

            Set(descriptors, SquatTelemetryChannelId.SimulationTick, "simulation_tick", "tick", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, "Fixed-step tick published by the authoritative runtime.");
            Set(descriptors, SquatTelemetryChannelId.SimulationTimeSeconds, "simulation_time_s", "s", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, "Post-step simulation time.");
            Set(descriptors, SquatTelemetryChannelId.FixedStepSeconds, "fixed_step_s", "s", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.REQUIRED, "Versioned simulation step contract.");
            Set(descriptors, SquatTelemetryChannelId.AttemptRelativeTimeSeconds, "attempt_relative_time_s", "s", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Elapsed time from the first recorded sample.");
            Set(descriptors, SquatTelemetryChannelId.AttemptRelativeTimeAvailability, "attempt_relative_time_available", "bool", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.DERIVED, SquatTelemetryAvailabilitySemantics.REQUIRED, "Explicit availability for attempt-relative time.");
            Set(descriptors, SquatTelemetryChannelId.SquatState, "squat_state", "enum", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Existing squat domain state context; not a rule result.");
            Set(descriptors, SquatTelemetryChannelId.PhaseDirection, "phase_direction", "enum", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Existing squat phase-direction context.");
            Set(descriptors, SquatTelemetryChannelId.SqReferencePhase, "sq_reference_phase", "1", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.REQUIRED, "Existing reference phase context only; not physical-event truth.");
            Set(descriptors, SquatTelemetryChannelId.IntentBrace01, "intent_brace_01", "1", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Sampled player brace intent.");
            Set(descriptors, SquatTelemetryChannelId.IntentYield01, "intent_yield_01", "1", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Sampled player yield intent.");
            Set(descriptors, SquatTelemetryChannelId.IntentDrive01, "intent_drive_01", "1", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Sampled player drive intent.");
            Set(descriptors, SquatTelemetryChannelId.IntentBalanceX, "intent_balance_x", "1", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Sampled player lateral balance intent.");
            Set(descriptors, SquatTelemetryChannelId.IntentGrip01, "intent_grip_01", "1", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Sampled player grip intent.");
            Set(descriptors, SquatTelemetryChannelId.IntentEdgeFlags, "intent_edge_flags", "bitmask", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Input edge bits delivered to this physics tick.");
            Set(descriptors, SquatTelemetryChannelId.IntentHeldFlags, "intent_held_flags", "bitmask", SquatTelemetryFrame.NONE, SquatTelemetrySourceClass.GAME_EVENT, SquatTelemetryAvailabilitySemantics.REQUIRED, "Held input state delivered to this physics tick.");

            Set(descriptors, SquatTelemetryChannelId.BarAvailable, "bar_available", "bool", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, "Physical bar availability, independent of numeric sentinel values.");
            Set(descriptors, SquatTelemetryChannelId.BarPositionWorld, "bar_position_world", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Copied physical bar Rigidbody position after PhysX.");
            Set(descriptors, SquatTelemetryChannelId.BarLinearVelocityWorld, "bar_linear_velocity_world", "m/s", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Copied physical bar Rigidbody linear velocity after PhysX; raw, not filtered.");
            Set(descriptors, SquatTelemetryChannelId.BarOrientationWorldFromBar, "bar_orientation_world_from_bar", "quaternion", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Canonicalized world-from-BAR orientation after PhysX.");
            Set(descriptors, SquatTelemetryChannelId.BarAngularVelocityBar, "bar_angular_velocity_bar", "rad/s", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Raw physical bar angular velocity expressed in BAR coordinates.");
            Set(descriptors, SquatTelemetryChannelId.BarLoadKg, "bar_load_kg", "kg", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Copied physical bar Rigidbody mass.");
            Set(descriptors, SquatTelemetryChannelId.SaddleAvailability, "saddle_availability", "enum", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Whether the squat saddle producer exists for the physical bar.");
            Set(descriptors, SquatTelemetryChannelId.SaddleAttached, "saddle_attached", "bool", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Finite saddle joint attachment state.");
            Set(descriptors, SquatTelemetryChannelId.SaddleBroken, "saddle_broken", "bool", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Saddle break/separation diagnostic; not a failure classification.");
            Set(descriptors, SquatTelemetryChannelId.SaddleSeparationM, "saddle_separation_m", "m", SquatTelemetryFrame.BAR, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Distance between the configured bar and thorax saddle anchors.");

            Set(descriptors, SquatTelemetryChannelId.DepthLandmarksAvailability, "depth_landmarks_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.REQUIRED, "Availability of raw calibrated joint-anchor landmarks.");
            Set(descriptors, SquatTelemetryChannelId.LeftHipCreaseY, "left_hip_crease_y", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Calibrated left hip-crease proxy world Y.");
            Set(descriptors, SquatTelemetryChannelId.RightHipCreaseY, "right_hip_crease_y", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Calibrated right hip-crease proxy world Y.");
            Set(descriptors, SquatTelemetryChannelId.LeftKneeTopY, "left_knee_top_y", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Calibrated left knee-top proxy world Y.");
            Set(descriptors, SquatTelemetryChannelId.RightKneeTopY, "right_knee_top_y", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Calibrated right knee-top proxy world Y.");
            Set(descriptors, SquatTelemetryChannelId.LeftDepthM, "left_depth_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.RULE_DERIVED_GAME_PROXY, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Left hip-crease proxy Y minus left knee-top proxy Y; no judgment.");
            Set(descriptors, SquatTelemetryChannelId.RightDepthM, "right_depth_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.RULE_DERIVED_GAME_PROXY, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Right hip-crease proxy Y minus right knee-top proxy Y; no judgment.");
            Set(descriptors, SquatTelemetryChannelId.WorstSideDepthM, "worst_side_depth_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.RULE_DERIVED_GAME_PROXY, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Maximum of the two raw bilateral depth margins; no judgment.");
            Set(descriptors, SquatTelemetryChannelId.DepthMarginM, "depth_rule_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.REQUIRED, "Versioned depth proxy margin retained as context for later rules.");

            Set(descriptors, SquatTelemetryChannelId.SystemComAvailability, "system_com_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.REQUIRED, "Availability of the mass-weighted athlete/system COM producer.");
            Set(descriptors, SquatTelemetryChannelId.SystemComPositionWorld, "system_com_position_world", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Mass-weighted engine-model COM; not a force-plate measurement.");
            Set(descriptors, SquatTelemetryChannelId.SystemComVelocityWorld, "system_com_velocity_world", "m/s", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Mass-weighted engine-model COM velocity; raw observer output.");
            Set(descriptors, SquatTelemetryChannelId.SystemMassKg, "system_mass_kg", "kg", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Mass represented by the COM observer model.");
            Set(descriptors, SquatTelemetryChannelId.SupportAvailability, "support_producer_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.REQUIRED, "Whether plantar support observation exists; not whether contact is present.");
            Set(descriptors, SquatTelemetryChannelId.HasSupport, "has_support_contact", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, "Whether the available plantar contact producer reports support.");
            Set(descriptors, SquatTelemetryChannelId.SupportApMinM, "support_ap_min_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Minimum AP coordinate of observed plantar contacts.");
            Set(descriptors, SquatTelemetryChannelId.SupportApMaxM, "support_ap_max_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Maximum AP coordinate of observed plantar contacts.");
            Set(descriptors, SquatTelemetryChannelId.SupportMlMinM, "support_ml_min_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Minimum ML coordinate of observed plantar contacts.");
            Set(descriptors, SquatTelemetryChannelId.SupportMlMaxM, "support_ml_max_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Maximum ML coordinate of observed plantar contacts.");
            Set(descriptors, SquatTelemetryChannelId.SupportPlaneY, "support_plane_y", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Mean observed plantar contact plane Y.");
            Set(descriptors, SquatTelemetryChannelId.SupportCenterWorld, "support_center_world", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Center of the observed AP/ML support bounds.");
            Set(descriptors, SquatTelemetryChannelId.ComToSupportApFrontMarginM, "com_to_support_ap_front_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "AP front margin from modeled COM to observed support boundary.");
            Set(descriptors, SquatTelemetryChannelId.ComToSupportApRearMarginM, "com_to_support_ap_rear_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "AP rear margin from modeled COM to observed support boundary.");
            Set(descriptors, SquatTelemetryChannelId.ComToSupportMlRightMarginM, "com_to_support_ml_right_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "ML right margin from modeled COM to observed support boundary.");
            Set(descriptors, SquatTelemetryChannelId.ComToSupportMlLeftMarginM, "com_to_support_ml_left_margin_m", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "ML left margin from modeled COM to observed support boundary.");
            Set(descriptors, SquatTelemetryChannelId.SupportContactCount, "support_contact_count", "count", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Count of buffered plantar contact points.");
            Set(descriptors, SquatTelemetryChannelId.EngineContactPointAvailability, "engine_contact_point_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.REQUIRED, "Availability of impulse-weighted engine contact-point estimate.");
            Set(descriptors, SquatTelemetryChannelId.EngineContactPointWorld, "engine_contact_point_world", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Engine contact-point estimate; explicitly not a human COP measurement.");
            Set(descriptors, SquatTelemetryChannelId.TotalNormalImpulseNs, "total_normal_contact_impulse", "N*s", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Buffered PhysX contact impulse sum; not ground-reaction force.");

            SetFoot(descriptors, SquatTelemetryChannelId.LeftFootAvailability, "left", "left foot contact detector availability.");
            SetFoot(descriptors, SquatTelemetryChannelId.RightFootAvailability, "right", "right foot contact detector availability.");

            Set(descriptors, SquatTelemetryChannelId.PelvisAvailability, "pelvis_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, "Availability of copied pelvis Rigidbody state.");
            Set(descriptors, SquatTelemetryChannelId.PelvisPositionWorld, "pelvis_position_world", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Copied pelvis Rigidbody position after PhysX.");
            Set(descriptors, SquatTelemetryChannelId.PelvisLinearVelocityWorld, "pelvis_linear_velocity_world", "m/s", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Copied pelvis Rigidbody linear velocity after PhysX.");
            Set(descriptors, SquatTelemetryChannelId.TrunkAvailability, "trunk_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, "Availability of copied thorax orientation state.");
            Set(descriptors, SquatTelemetryChannelId.ThoraxOrientationWorldFromBody, "thorax_orientation_world_from_body", "quaternion", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Copied thorax Rigidbody orientation after PhysX.");
            Set(descriptors, SquatTelemetryChannelId.TrunkWorldPitchRad, "trunk_world_pitch_rad", "rad", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "World Y-Z pitch projection of the thorax frame.");

            SetJoint(descriptors, SquatTelemetryChannelId.LeftKneeJointAvailability, "left_knee", "left knee joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.RightKneeJointAvailability, "right_knee", "right knee joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.LeftHipJointAvailability, "left_hip", "left hip joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.RightHipJointAvailability, "right_hip", "right hip joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.LeftAnkleJointAvailability, "left_ankle", "left ankle joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.RightAnkleJointAvailability, "right_ankle", "right ankle joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.AbdomenJointAvailability, "abdomen", "abdomen joint availability.");
            SetJoint(descriptors, SquatTelemetryChannelId.ThoraxJointAvailability, "thorax", "thorax joint availability.");

            Set(descriptors, SquatTelemetryChannelId.DriveAvailability, "drive_diagnostics_available", "bool", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.REQUIRED, "Availability of post-physics modeled drive diagnostics.");
            Set(descriptors, SquatTelemetryChannelId.DriveSaturated, "drive_saturated", "bool", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.OPTIONAL, "Existing drive saturation diagnostic; not biological force.");
            Set(descriptors, SquatTelemetryChannelId.MaximumModeledDemand, "maximum_modeled_demand", "1", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Maximum normalized demand from the configured game drive model.");
            Set(descriptors, SquatTelemetryChannelId.UnmeasuredGroundReactionForce, "ground_reaction_force", "N", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.NOT_OBSERVABLE, SquatTelemetryAvailabilitySemantics.NOT_OBSERVABLE, "No force-plate or validated GRF producer exists in this simulation.");

            return descriptors;
        }

        private static void SetFoot(
            SquatTelemetryChannelDescriptor[] descriptors,
            SquatTelemetryChannelId availabilityId,
            string side,
            string claimNote)
        {
            Set(descriptors, availabilityId, side + "_foot_detector_available", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.REQUIRED, claimNote);
            SquatTelemetryChannelId inContact = (SquatTelemetryChannelId)((int)availabilityId + 1);
            SquatTelemetryChannelId contactCount = (SquatTelemetryChannelId)((int)availabilityId + 2);
            SquatTelemetryChannelId completedCount = (SquatTelemetryChannelId)((int)availabilityId + 3);
            SquatTelemetryChannelId slipAccumulated = (SquatTelemetryChannelId)((int)availabilityId + 4);
            SquatTelemetryChannelId slipSpeed = (SquatTelemetryChannelId)((int)availabilityId + 5);
            Set(descriptors, inContact, side + "_foot_in_contact", "bool", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Collision callback contact state.");
            Set(descriptors, contactCount, side + "_foot_contact_count", "count", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Collision callback support-pair count.");
            Set(descriptors, completedCount, side + "_foot_completed_contact_count", "count", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINE_RUNTIME_OBSERVATION, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Buffered post-step plantar contact-point count.");
            Set(descriptors, slipAccumulated, side + "_foot_slip_accumulated", "m", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Accumulated tangential velocity at the observed plantar contact point.");
            Set(descriptors, slipSpeed, side + "_foot_slip_speed", "m/s", SquatTelemetryFrame.WORLD, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Raw tangential velocity at the observed plantar contact point.");
        }

        private static void SetJoint(
            SquatTelemetryChannelDescriptor[] descriptors,
            SquatTelemetryChannelId availabilityId,
            string prefix,
            string claimNote)
        {
            Set(descriptors, availabilityId, prefix + "_joint_available", "bool", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.REQUIRED, claimNote);
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 1), prefix + "_drive_available", "bool", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.OPTIONAL, "Post-physics modeled drive diagnostic availability.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 2), prefix + "_actual_angle", "rad", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Calibrated logical joint twist; not a geometric included angle.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 3), prefix + "_actual_angular_velocity", "rad/s", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Calibrated logical joint-frame angular velocity.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 4), prefix + "_reference_angle", "rad", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Canonical squat reference scalar in the calibrated joint sign convention.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 5), prefix + "_actual_reference_error", "rad", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Actual logical scalar minus canonical reference scalar.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 6), prefix + "_limit_proximity", "1", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.ENGINEERING_DERIVED, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Configured joint-limit occupancy diagnostic.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 7), prefix + "_modeled_demand", "1", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Configured spring/damper demand normalized by modeled capacity; not athlete force.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 8), prefix + "_maximum_force", "N*m", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Configured finite drive authority; not biological joint moment.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 9), prefix + "_activation", "1", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Configured drive activation.");
            Set(descriptors, (SquatTelemetryChannelId)((int)availabilityId + 10), prefix + "_capacity_scale", "1", SquatTelemetryFrame.JOINT, SquatTelemetrySourceClass.GAME_MODEL, SquatTelemetryAvailabilitySemantics.EXPLICIT_NOT_AVAILABLE, "Configured game capacity scale.");
        }

        private static void Set(
            SquatTelemetryChannelDescriptor[] descriptors,
            SquatTelemetryChannelId id,
            string canonicalName,
            string unit,
            SquatTelemetryFrame frame,
            SquatTelemetrySourceClass sourceClass,
            SquatTelemetryAvailabilitySemantics availabilitySemantics,
            string claimNote)
        {
            descriptors[(int)id] = new SquatTelemetryChannelDescriptor(
                id,
                canonicalName,
                unit,
                frame,
                sourceClass,
                availabilitySemantics,
                CatalogVersion,
                claimNote);
        }
    }
}
