using System;
using PowerliftingSimulator.Foundation;

namespace PowerliftingSimulator.Squat
{
    public enum SquatTelemetrySourceClass : byte
    {
        ENGINE_RUNTIME_OBSERVATION,
        GAME_EVENT,
        RULE_DERIVED_GAME_PROXY,
        ENGINEERING_DERIVED,
        DERIVED,
        DERIVED_FILTERED,
        GAME_MODEL,
        PROVISIONAL,
        NOT_OBSERVABLE
    }

    public enum SquatTelemetryFrame : byte
    {
        NONE,
        WORLD,
        ATHLETE_LOCAL,
        JOINT,
        BAR
    }

    public enum SquatTelemetryAvailability : byte
    {
        NOT_AVAILABLE,
        AVAILABLE,
        NOT_OBSERVABLE
    }

    [Flags]
    public enum SquatTelemetryQualityFlags : uint
    {
        NONE = 0,
        POST_PHYSICS = 1u << 0,
        RAW = 1u << 1,
        ATTEMPT_RELATIVE_TIME = 1u << 2,
        BAR_AVAILABLE = 1u << 3,
        SUPPORT_PRODUCER_AVAILABLE = 1u << 4,
        SUPPORT_CONTACT_PRESENT = 1u << 5,
        DEPTH_LANDMARKS_AVAILABLE = 1u << 6,
        JOINTS_AVAILABLE = 1u << 7,
        DRIVE_DIAGNOSTICS_AVAILABLE = 1u << 8,
        LEFT_FOOT_PRODUCER_AVAILABLE = 1u << 9,
        RIGHT_FOOT_PRODUCER_AVAILABLE = 1u << 10
    }

    [Flags]
    public enum SquatIntentHeldFlags : byte
    {
        NONE = 0,
        BRACE = 1 << 0,
        YIELD = 1 << 1,
        DRIVE = 1 << 2,
        BALANCE = 1 << 3,
        GRIP = 1 << 4,
        CONFIRM = 1 << 5,
        ABORT = 1 << 6
    }

    public static class SquatTelemetryValue
    {
        public static readonly Vector3Value UnavailableVector3 = new Vector3Value(
            float.NaN,
            float.NaN,
            float.NaN);

        public static readonly QuaternionValue UnavailableQuaternion = new QuaternionValue(
            float.NaN,
            float.NaN,
            float.NaN,
            float.NaN);

        public static bool IsFinite(Vector3Value value) =>
            IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z);

        public static bool IsFinite(QuaternionValue value) =>
            IsFinite(value.X) && IsFinite(value.Y) && IsFinite(value.Z) && IsFinite(value.W);

        public static bool IsUnavailable(Vector3Value value) =>
            float.IsNaN(value.X) && float.IsNaN(value.Y) && float.IsNaN(value.Z);

        public static bool IsUnavailable(QuaternionValue value) =>
            float.IsNaN(value.X) && float.IsNaN(value.Y) &&
            float.IsNaN(value.Z) && float.IsNaN(value.W);

        public static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    /// <summary>
    /// Allocation-free copy of the fixed-step player intent needed by squat
    /// observation. Edge-event arrays are intentionally not retained.
    /// </summary>
    public readonly struct SquatIntentSnapshot
    {
        public SquatIntentSnapshot(PlayerIntentFrame source)
        {
            if (double.IsNaN(source.SimulationTimeSeconds) || double.IsInfinity(source.SimulationTimeSeconds))
                throw new ArgumentOutOfRangeException(nameof(source));

            Tick = source.Tick;
            SimulationTimeSeconds = source.SimulationTimeSeconds;
            Edges = source.Edges;
            EdgeEventCount = source.EdgeEventCount;
            Brace01 = source.Brace01;
            Yield01 = source.Yield01;
            Drive01 = source.Drive01;
            BalanceX = source.BalanceX;
            Grip01 = source.Grip01;
            Held = BuildHeldFlags(source);
            RequireRange(Brace01, 0f, 1f, nameof(source.Brace01));
            RequireRange(Yield01, 0f, 1f, nameof(source.Yield01));
            RequireRange(Drive01, 0f, 1f, nameof(source.Drive01));
            RequireRange(BalanceX, -1f, 1f, nameof(source.BalanceX));
            RequireRange(Grip01, 0f, 1f, nameof(source.Grip01));
        }

        public static SquatIntentSnapshot From(PlayerIntentFrame source) =>
            new SquatIntentSnapshot(source);

        public ulong Tick { get; }
        public double SimulationTimeSeconds { get; }
        public IntentEdgeFlags Edges { get; }
        public int EdgeEventCount { get; }
        public float Brace01 { get; }
        public float Yield01 { get; }
        public float Drive01 { get; }
        public float BalanceX { get; }
        public float Grip01 { get; }
        public SquatIntentHeldFlags Held { get; }

        public bool IsHeld(IntentAction action) => (Held & HeldFlag(action)) != 0;

        public float Value(IntentAction action)
        {
            switch (action)
            {
                case IntentAction.Brace: return Brace01;
                case IntentAction.Yield: return Yield01;
                case IntentAction.Drive: return Drive01;
                case IntentAction.Balance: return BalanceX;
                case IntentAction.Grip: return Grip01;
                case IntentAction.Confirm: return IsHeld(IntentAction.Confirm) ? 1f : 0f;
                case IntentAction.Abort: return IsHeld(IntentAction.Abort) ? 1f : 0f;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        public bool WasPressed(IntentAction action) => Edges.HasFlag(
            EdgeFlag(action, IntentEdgeKind.Pressed));

        public bool WasReleased(IntentAction action) => Edges.HasFlag(
            EdgeFlag(action, IntentEdgeKind.Released));

        private static SquatIntentHeldFlags BuildHeldFlags(PlayerIntentFrame source)
        {
            SquatIntentHeldFlags held = SquatIntentHeldFlags.NONE;
            if (source.BraceHeld) held |= SquatIntentHeldFlags.BRACE;
            if (source.YieldHeld) held |= SquatIntentHeldFlags.YIELD;
            if (source.DriveHeld) held |= SquatIntentHeldFlags.DRIVE;
            if (source.BalanceHeld) held |= SquatIntentHeldFlags.BALANCE;
            if (source.GripHeld) held |= SquatIntentHeldFlags.GRIP;
            if (source.ConfirmHeld) held |= SquatIntentHeldFlags.CONFIRM;
            if (source.AbortHeld) held |= SquatIntentHeldFlags.ABORT;
            return held;
        }

        private static SquatIntentHeldFlags HeldFlag(IntentAction action)
        {
            switch (action)
            {
                case IntentAction.Brace: return SquatIntentHeldFlags.BRACE;
                case IntentAction.Yield: return SquatIntentHeldFlags.YIELD;
                case IntentAction.Drive: return SquatIntentHeldFlags.DRIVE;
                case IntentAction.Balance: return SquatIntentHeldFlags.BALANCE;
                case IntentAction.Grip: return SquatIntentHeldFlags.GRIP;
                case IntentAction.Confirm: return SquatIntentHeldFlags.CONFIRM;
                case IntentAction.Abort: return SquatIntentHeldFlags.ABORT;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        private static IntentEdgeFlags EdgeFlag(IntentAction action, IntentEdgeKind edgeKind)
        {
            switch (action)
            {
                case IntentAction.Brace: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.BracePressed : IntentEdgeFlags.BraceReleased;
                case IntentAction.Yield: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.YieldPressed : IntentEdgeFlags.YieldReleased;
                case IntentAction.Drive: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.DrivePressed : IntentEdgeFlags.DriveReleased;
                case IntentAction.Balance: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.BalancePressed : IntentEdgeFlags.BalanceReleased;
                case IntentAction.Grip: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.GripPressed : IntentEdgeFlags.GripReleased;
                case IntentAction.Confirm: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.ConfirmPressed : IntentEdgeFlags.ConfirmReleased;
                case IntentAction.Abort: return edgeKind == IntentEdgeKind.Pressed ? IntentEdgeFlags.AbortPressed : IntentEdgeFlags.AbortReleased;
                default: throw new ArgumentOutOfRangeException(nameof(action), action, null);
            }
        }

        private static void RequireRange(float value, float minimum, float maximum, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value) || value < minimum || value > maximum)
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct SquatBarObservation
    {
        public SquatBarObservation(
            SquatTelemetryAvailability availability,
            Vector3Value positionWorldMeters,
            Vector3Value linearVelocityWorldMetersPerSecond,
            QuaternionValue orientationWorldFromBar,
            Vector3Value angularVelocityBarRadiansPerSecond,
            float loadKilograms,
            SquatTelemetryAvailability saddleAvailability,
            bool saddleAttached,
            bool saddleBroken,
            float saddleSeparationMeters)
        {
            Availability = availability;
            SaddleAvailability = saddleAvailability;
            if (availability == SquatTelemetryAvailability.AVAILABLE)
            {
                RequireFinite(positionWorldMeters, nameof(positionWorldMeters));
                RequireFinite(linearVelocityWorldMetersPerSecond, nameof(linearVelocityWorldMetersPerSecond));
                RequireFinite(angularVelocityBarRadiansPerSecond, nameof(angularVelocityBarRadiansPerSecond));
                if (!SquatTelemetryValue.IsFinite(orientationWorldFromBar))
                    throw new ArgumentOutOfRangeException(nameof(orientationWorldFromBar));
                if (!SquatTelemetryValue.IsFinite(loadKilograms) || loadKilograms < 0f)
                    throw new ArgumentOutOfRangeException(nameof(loadKilograms));
                PositionWorldMeters = positionWorldMeters;
                LinearVelocityWorldMetersPerSecond = linearVelocityWorldMetersPerSecond;
                OrientationWorldFromBar = orientationWorldFromBar.Canonicalized();
                AngularVelocityBarRadiansPerSecond = angularVelocityBarRadiansPerSecond;
                LoadKilograms = loadKilograms;
            }
            else
            {
                RequireUnavailable(positionWorldMeters, nameof(positionWorldMeters));
                RequireUnavailable(linearVelocityWorldMetersPerSecond, nameof(linearVelocityWorldMetersPerSecond));
                RequireUnavailable(orientationWorldFromBar, nameof(orientationWorldFromBar));
                RequireUnavailable(angularVelocityBarRadiansPerSecond, nameof(angularVelocityBarRadiansPerSecond));
                if (!float.IsNaN(loadKilograms))
                    throw new ArgumentException("Unavailable bar load must be NaN.", nameof(loadKilograms));
                PositionWorldMeters = SquatTelemetryValue.UnavailableVector3;
                LinearVelocityWorldMetersPerSecond = SquatTelemetryValue.UnavailableVector3;
                OrientationWorldFromBar = SquatTelemetryValue.UnavailableQuaternion;
                AngularVelocityBarRadiansPerSecond = SquatTelemetryValue.UnavailableVector3;
                LoadKilograms = float.NaN;
                saddleAttached = false;
                saddleBroken = false;
                saddleSeparationMeters = float.NaN;
            }

            SaddleAttached = saddleAttached;
            SaddleBroken = saddleBroken;
            if (saddleAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                if (!SquatTelemetryValue.IsFinite(saddleSeparationMeters) || saddleSeparationMeters < 0f)
                    throw new ArgumentOutOfRangeException(nameof(saddleSeparationMeters));
                SaddleSeparationMeters = saddleSeparationMeters;
            }
            else
            {
                if (saddleAttached || saddleBroken || !float.IsNaN(saddleSeparationMeters))
                    throw new ArgumentException("Unavailable saddle state must not masquerade as a measurement.");
                SaddleSeparationMeters = float.NaN;
            }
        }

        public static SquatBarObservation Available(
            Vector3Value positionWorldMeters,
            Vector3Value linearVelocityWorldMetersPerSecond,
            QuaternionValue orientationWorldFromBar,
            Vector3Value angularVelocityBarRadiansPerSecond,
            float loadKilograms,
            SquatTelemetryAvailability saddleAvailability,
            bool saddleAttached,
            bool saddleBroken,
            float saddleSeparationMeters) => new SquatBarObservation(
                SquatTelemetryAvailability.AVAILABLE,
                positionWorldMeters,
                linearVelocityWorldMetersPerSecond,
                orientationWorldFromBar,
                angularVelocityBarRadiansPerSecond,
                loadKilograms,
                saddleAvailability,
                saddleAttached,
                saddleBroken,
                saddleSeparationMeters);

        public static SquatBarObservation Unavailable() => new SquatBarObservation(
            SquatTelemetryAvailability.NOT_AVAILABLE,
            SquatTelemetryValue.UnavailableVector3,
            SquatTelemetryValue.UnavailableVector3,
            SquatTelemetryValue.UnavailableQuaternion,
            SquatTelemetryValue.UnavailableVector3,
            float.NaN,
            SquatTelemetryAvailability.NOT_AVAILABLE,
            false,
            false,
            float.NaN);

        public SquatTelemetryAvailability Availability { get; }
        public Vector3Value PositionWorldMeters { get; }
        public Vector3Value LinearVelocityWorldMetersPerSecond { get; }
        public QuaternionValue OrientationWorldFromBar { get; }
        public Vector3Value AngularVelocityBarRadiansPerSecond { get; }
        public float LoadKilograms { get; }
        public SquatTelemetryAvailability SaddleAvailability { get; }
        public bool SaddleAttached { get; }
        public bool SaddleBroken { get; }
        public float SaddleSeparationMeters { get; }

        public bool IsAvailable => Availability == SquatTelemetryAvailability.AVAILABLE;

        private static void RequireFinite(Vector3Value value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireUnavailable(Vector3Value value, string name)
        {
            if (!SquatTelemetryValue.IsUnavailable(value))
                throw new ArgumentException("Unavailable vector must use the explicit NaN representation.", name);
        }

        private static void RequireUnavailable(QuaternionValue value, string name)
        {
            if (!SquatTelemetryValue.IsUnavailable(value))
                throw new ArgumentException("Unavailable quaternion must use the explicit NaN representation.", name);
        }
    }

    public readonly struct SquatDepthLandmarks
    {
        public SquatDepthLandmarks(
            float leftHipCreaseY,
            float rightHipCreaseY,
            float leftKneeTopY,
            float rightKneeTopY,
            float depthMarginM = SquatDepthGeometry.DefaultDepthMarginM)
        {
            RequireFinite(leftHipCreaseY, nameof(leftHipCreaseY));
            RequireFinite(rightHipCreaseY, nameof(rightHipCreaseY));
            RequireFinite(leftKneeTopY, nameof(leftKneeTopY));
            RequireFinite(rightKneeTopY, nameof(rightKneeTopY));
            RequireFinite(depthMarginM, nameof(depthMarginM));
            if (depthMarginM < 0f)
                throw new ArgumentOutOfRangeException(nameof(depthMarginM));

            Availability = SquatTelemetryAvailability.AVAILABLE;
            LeftHipCreaseY = leftHipCreaseY;
            RightHipCreaseY = rightHipCreaseY;
            LeftKneeTopY = leftKneeTopY;
            RightKneeTopY = rightKneeTopY;
            DepthMarginM = depthMarginM;
            LeftDepthM = leftHipCreaseY - leftKneeTopY;
            RightDepthM = rightHipCreaseY - rightKneeTopY;
            WorstSideDepthM = Math.Max(LeftDepthM, RightDepthM);
        }

        private SquatDepthLandmarks(SquatTelemetryAvailability availability)
        {
            Availability = availability;
            LeftHipCreaseY = float.NaN;
            RightHipCreaseY = float.NaN;
            LeftKneeTopY = float.NaN;
            RightKneeTopY = float.NaN;
            DepthMarginM = float.NaN;
            LeftDepthM = float.NaN;
            RightDepthM = float.NaN;
            WorstSideDepthM = float.NaN;
        }

        public static SquatDepthLandmarks Unavailable() =>
            new SquatDepthLandmarks(SquatTelemetryAvailability.NOT_AVAILABLE);

        public SquatTelemetryAvailability Availability { get; }
        public float LeftHipCreaseY { get; }
        public float RightHipCreaseY { get; }
        public float LeftKneeTopY { get; }
        public float RightKneeTopY { get; }
        public float DepthMarginM { get; }
        public float LeftDepthM { get; }
        public float RightDepthM { get; }
        public float WorstSideDepthM { get; }

        private static void RequireFinite(float value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct SquatSupportObservation
    {
        public SquatSupportObservation(
            SquatTelemetryAvailability systemComAvailability,
            Vector3Value systemComWorldMeters,
            Vector3Value systemComVelocityWorldMetersPerSecond,
            float systemMassKilograms,
            SquatTelemetryAvailability supportAvailability,
            bool hasSupport,
            float supportApMinM,
            float supportApMaxM,
            float supportMlMinM,
            float supportMlMaxM,
            float supportPlaneY,
            int supportContactCount,
            SquatTelemetryAvailability engineContactPointAvailability,
            Vector3Value engineContactPointWorldMeters,
            float totalNormalImpulseNewtonSeconds)
        {
            SystemComAvailability = systemComAvailability;
            if (systemComAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                RequireFinite(systemComWorldMeters, nameof(systemComWorldMeters));
                RequireFinite(systemComVelocityWorldMetersPerSecond, nameof(systemComVelocityWorldMetersPerSecond));
                if (!SquatTelemetryValue.IsFinite(systemMassKilograms) || systemMassKilograms < 0f)
                    throw new ArgumentOutOfRangeException(nameof(systemMassKilograms));
                SystemComWorldMeters = systemComWorldMeters;
                SystemComVelocityWorldMetersPerSecond = systemComVelocityWorldMetersPerSecond;
                SystemMassKilograms = systemMassKilograms;
            }
            else
            {
                RequireUnavailable(systemComWorldMeters, nameof(systemComWorldMeters));
                RequireUnavailable(systemComVelocityWorldMetersPerSecond, nameof(systemComVelocityWorldMetersPerSecond));
                if (!float.IsNaN(systemMassKilograms))
                    throw new ArgumentException("Unavailable COM mass must be NaN.", nameof(systemMassKilograms));
                SystemComWorldMeters = SquatTelemetryValue.UnavailableVector3;
                SystemComVelocityWorldMetersPerSecond = SquatTelemetryValue.UnavailableVector3;
                SystemMassKilograms = float.NaN;
            }

            SupportAvailability = supportAvailability;
            HasSupport = hasSupport;
            if (supportAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                if (!SquatTelemetryValue.IsFinite(totalNormalImpulseNewtonSeconds) || totalNormalImpulseNewtonSeconds < 0f)
                    throw new ArgumentOutOfRangeException(nameof(totalNormalImpulseNewtonSeconds));
                if (hasSupport)
                {
                    RequireFinite(supportApMinM, nameof(supportApMinM));
                    RequireFinite(supportApMaxM, nameof(supportApMaxM));
                    RequireFinite(supportMlMinM, nameof(supportMlMinM));
                    RequireFinite(supportMlMaxM, nameof(supportMlMaxM));
                    RequireFinite(supportPlaneY, nameof(supportPlaneY));
                    if (supportApMaxM < supportApMinM || supportMlMaxM < supportMlMinM || supportContactCount <= 0)
                        throw new ArgumentOutOfRangeException(nameof(supportContactCount));
                    SupportApMinM = supportApMinM;
                    SupportApMaxM = supportApMaxM;
                    SupportMlMinM = supportMlMinM;
                    SupportMlMaxM = supportMlMaxM;
                    SupportPlaneY = supportPlaneY;
                    SupportCenterWorldMeters = new Vector3Value(
                        0.5f * (supportMlMinM + supportMlMaxM),
                        supportPlaneY,
                        0.5f * (supportApMinM + supportApMaxM));
                    ComToSupportApFrontMarginM = supportApMaxM - systemComWorldMeters.Z;
                    ComToSupportApRearMarginM = systemComWorldMeters.Z - supportApMinM;
                    ComToSupportMlRightMarginM = supportMlMaxM - systemComWorldMeters.X;
                    ComToSupportMlLeftMarginM = systemComWorldMeters.X - supportMlMinM;
                }
                else
                {
                    if (supportContactCount != 0 || !float.IsNaN(supportApMinM) || !float.IsNaN(supportApMaxM) ||
                        !float.IsNaN(supportMlMinM) || !float.IsNaN(supportMlMaxM) || !float.IsNaN(supportPlaneY))
                        throw new ArgumentException("A support producer with no support must use explicit unavailable bounds.");
                    SupportApMinM = float.NaN;
                    SupportApMaxM = float.NaN;
                    SupportMlMinM = float.NaN;
                    SupportMlMaxM = float.NaN;
                    SupportPlaneY = float.NaN;
                    SupportCenterWorldMeters = SquatTelemetryValue.UnavailableVector3;
                    ComToSupportApFrontMarginM = float.NaN;
                    ComToSupportApRearMarginM = float.NaN;
                    ComToSupportMlRightMarginM = float.NaN;
                    ComToSupportMlLeftMarginM = float.NaN;
                }
                SupportContactCount = supportContactCount;
                TotalNormalImpulseNewtonSeconds = totalNormalImpulseNewtonSeconds;
            }
            else
            {
                if (hasSupport || supportContactCount != -1 || !float.IsNaN(supportApMinM) || !float.IsNaN(supportApMaxM) ||
                    !float.IsNaN(supportMlMinM) || !float.IsNaN(supportMlMaxM) || !float.IsNaN(supportPlaneY) ||
                    !float.IsNaN(totalNormalImpulseNewtonSeconds))
                    throw new ArgumentException("Unavailable support must use explicit missing values.");
                SupportApMinM = float.NaN;
                SupportApMaxM = float.NaN;
                SupportMlMinM = float.NaN;
                SupportMlMaxM = float.NaN;
                SupportPlaneY = float.NaN;
                SupportCenterWorldMeters = SquatTelemetryValue.UnavailableVector3;
                ComToSupportApFrontMarginM = float.NaN;
                ComToSupportApRearMarginM = float.NaN;
                ComToSupportMlRightMarginM = float.NaN;
                ComToSupportMlLeftMarginM = float.NaN;
                SupportContactCount = -1;
                TotalNormalImpulseNewtonSeconds = float.NaN;
            }

            EngineContactPointAvailability = engineContactPointAvailability;
            if (engineContactPointAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                RequireFinite(engineContactPointWorldMeters, nameof(engineContactPointWorldMeters));
                EngineContactPointWorldMeters = engineContactPointWorldMeters;
            }
            else
            {
                RequireUnavailable(engineContactPointWorldMeters, nameof(engineContactPointWorldMeters));
                EngineContactPointWorldMeters = SquatTelemetryValue.UnavailableVector3;
            }
        }

        public static SquatSupportObservation Unavailable() => new SquatSupportObservation(
            SquatTelemetryAvailability.NOT_AVAILABLE,
            SquatTelemetryValue.UnavailableVector3,
            SquatTelemetryValue.UnavailableVector3,
            float.NaN,
            SquatTelemetryAvailability.NOT_AVAILABLE,
            false,
            float.NaN,
            float.NaN,
            float.NaN,
            float.NaN,
            float.NaN,
            -1,
            SquatTelemetryAvailability.NOT_AVAILABLE,
            SquatTelemetryValue.UnavailableVector3,
            float.NaN);

        public SquatTelemetryAvailability SystemComAvailability { get; }
        public Vector3Value SystemComWorldMeters { get; }
        public Vector3Value SystemComVelocityWorldMetersPerSecond { get; }
        public float SystemMassKilograms { get; }
        public SquatTelemetryAvailability SupportAvailability { get; }
        public bool HasSupport { get; }
        public float SupportApMinM { get; }
        public float SupportApMaxM { get; }
        public float SupportMlMinM { get; }
        public float SupportMlMaxM { get; }
        public float SupportPlaneY { get; }
        public Vector3Value SupportCenterWorldMeters { get; }
        public float ComToSupportApFrontMarginM { get; }
        public float ComToSupportApRearMarginM { get; }
        public float ComToSupportMlRightMarginM { get; }
        public float ComToSupportMlLeftMarginM { get; }
        public int SupportContactCount { get; }
        public SquatTelemetryAvailability EngineContactPointAvailability { get; }
        public Vector3Value EngineContactPointWorldMeters { get; }
        public float TotalNormalImpulseNewtonSeconds { get; }

        private static void RequireFinite(Vector3Value value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireFinite(float value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireUnavailable(Vector3Value value, string name)
        {
            if (!SquatTelemetryValue.IsUnavailable(value))
                throw new ArgumentException("Unavailable vector must use the explicit NaN representation.", name);
        }
    }

    public readonly struct SquatFootObservation
    {
        public SquatFootObservation(
            SquatTelemetryAvailability availability,
            bool isInContact,
            int contactCount,
            int completedContactCount,
            float slipAccumulatedMeters,
            float slipSpeedMetersPerSecond)
        {
            Availability = availability;
            if (availability == SquatTelemetryAvailability.AVAILABLE)
            {
                if (contactCount < 0 || completedContactCount < 0)
                    throw new ArgumentOutOfRangeException(nameof(contactCount));
                if (!SquatTelemetryValue.IsFinite(slipAccumulatedMeters) || slipAccumulatedMeters < 0f ||
                    !SquatTelemetryValue.IsFinite(slipSpeedMetersPerSecond) || slipSpeedMetersPerSecond < 0f)
                    throw new ArgumentOutOfRangeException(nameof(slipAccumulatedMeters));
                IsInContact = isInContact;
                ContactCount = contactCount;
                CompletedContactCount = completedContactCount;
                SlipAccumulatedMeters = slipAccumulatedMeters;
                SlipSpeedMetersPerSecond = slipSpeedMetersPerSecond;
            }
            else
            {
                if (isInContact || contactCount != -1 || completedContactCount != -1 ||
                    !float.IsNaN(slipAccumulatedMeters) || !float.IsNaN(slipSpeedMetersPerSecond))
                    throw new ArgumentException("Unavailable foot contact must use explicit missing values.");
                IsInContact = false;
                ContactCount = -1;
                CompletedContactCount = -1;
                SlipAccumulatedMeters = float.NaN;
                SlipSpeedMetersPerSecond = float.NaN;
            }
        }

        public static SquatFootObservation Available(
            bool isInContact,
            int contactCount,
            int completedContactCount,
            float slipAccumulatedMeters,
            float slipSpeedMetersPerSecond) => new SquatFootObservation(
                SquatTelemetryAvailability.AVAILABLE,
                isInContact,
                contactCount,
                completedContactCount,
                slipAccumulatedMeters,
                slipSpeedMetersPerSecond);

        public static SquatFootObservation Unavailable() => new SquatFootObservation(
            SquatTelemetryAvailability.NOT_AVAILABLE,
            false,
            -1,
            -1,
            float.NaN,
            float.NaN);

        public SquatTelemetryAvailability Availability { get; }
        public bool IsInContact { get; }
        public int ContactCount { get; }
        public int CompletedContactCount { get; }
        public float SlipAccumulatedMeters { get; }
        public float SlipSpeedMetersPerSecond { get; }
    }

    public readonly struct SquatJointObservation
    {
        public SquatJointObservation(
            SquatTelemetryAvailability jointAvailability,
            float actualAngleRadians,
            Vector3Value actualAngularVelocityRadiansPerSecond,
            float referenceAngleRadians,
            float actualReferenceErrorRadians,
            float limitProximity,
            SquatTelemetryAvailability driveAvailability,
            float modeledDemand,
            float maximumForceNewtonMeters,
            float activation,
            float capacityScale)
        {
            JointAvailability = jointAvailability;
            DriveAvailability = driveAvailability;
            if (jointAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                RequireFinite(actualAngleRadians, nameof(actualAngleRadians));
                RequireFinite(actualAngularVelocityRadiansPerSecond, nameof(actualAngularVelocityRadiansPerSecond));
                RequireFinite(referenceAngleRadians, nameof(referenceAngleRadians));
                RequireFinite(actualReferenceErrorRadians, nameof(actualReferenceErrorRadians));
                if (limitProximity < 0f || limitProximity > 1f || !SquatTelemetryValue.IsFinite(limitProximity))
                    throw new ArgumentOutOfRangeException(nameof(limitProximity));
                ActualAngleRadians = actualAngleRadians;
                ActualAngularVelocityRadiansPerSecond = actualAngularVelocityRadiansPerSecond;
                ReferenceAngleRadians = referenceAngleRadians;
                ActualReferenceErrorRadians = actualReferenceErrorRadians;
                LimitProximity = limitProximity;
            }
            else
            {
                if (!float.IsNaN(actualAngleRadians) || !SquatTelemetryValue.IsUnavailable(actualAngularVelocityRadiansPerSecond) ||
                    !float.IsNaN(referenceAngleRadians) || !float.IsNaN(actualReferenceErrorRadians) || !float.IsNaN(limitProximity))
                    throw new ArgumentException("Unavailable joint kinematics must use explicit missing values.");
                ActualAngleRadians = float.NaN;
                ActualAngularVelocityRadiansPerSecond = SquatTelemetryValue.UnavailableVector3;
                ReferenceAngleRadians = float.NaN;
                ActualReferenceErrorRadians = float.NaN;
                LimitProximity = float.NaN;
            }

            if (driveAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                RequireFinite(modeledDemand, nameof(modeledDemand));
                RequireFinite(maximumForceNewtonMeters, nameof(maximumForceNewtonMeters));
                RequireFinite(activation, nameof(activation));
                RequireFinite(capacityScale, nameof(capacityScale));
                if (modeledDemand < 0f || maximumForceNewtonMeters < 0f || activation < 0f || activation > 1f || capacityScale < 0f)
                    throw new ArgumentOutOfRangeException(nameof(modeledDemand));
                ModeledDemand = modeledDemand;
                MaximumForceNewtonMeters = maximumForceNewtonMeters;
                Activation = activation;
                CapacityScale = capacityScale;
            }
            else
            {
                if (!float.IsNaN(modeledDemand) || !float.IsNaN(maximumForceNewtonMeters) ||
                    !float.IsNaN(activation) || !float.IsNaN(capacityScale))
                    throw new ArgumentException("Unavailable drive diagnostics must use explicit missing values.");
                ModeledDemand = float.NaN;
                MaximumForceNewtonMeters = float.NaN;
                Activation = float.NaN;
                CapacityScale = float.NaN;
            }
        }

        public static SquatJointObservation Available(
            float actualAngleRadians,
            Vector3Value actualAngularVelocityRadiansPerSecond,
            float referenceAngleRadians,
            float actualReferenceErrorRadians,
            float limitProximity,
            float modeledDemand,
            float maximumForceNewtonMeters,
            float activation,
            float capacityScale) => new SquatJointObservation(
                SquatTelemetryAvailability.AVAILABLE,
                actualAngleRadians,
                actualAngularVelocityRadiansPerSecond,
                referenceAngleRadians,
                actualReferenceErrorRadians,
                limitProximity,
                SquatTelemetryAvailability.AVAILABLE,
                modeledDemand,
                maximumForceNewtonMeters,
                activation,
                capacityScale);

        public static SquatJointObservation Unavailable() => new SquatJointObservation(
            SquatTelemetryAvailability.NOT_AVAILABLE,
            float.NaN,
            SquatTelemetryValue.UnavailableVector3,
            float.NaN,
            float.NaN,
            float.NaN,
            SquatTelemetryAvailability.NOT_AVAILABLE,
            float.NaN,
            float.NaN,
            float.NaN,
            float.NaN);

        public SquatTelemetryAvailability JointAvailability { get; }
        public float ActualAngleRadians { get; }
        public Vector3Value ActualAngularVelocityRadiansPerSecond { get; }
        public float ReferenceAngleRadians { get; }
        public float ActualReferenceErrorRadians { get; }
        public float LimitProximity { get; }
        public SquatTelemetryAvailability DriveAvailability { get; }
        public float ModeledDemand { get; }
        public float MaximumForceNewtonMeters { get; }
        public float Activation { get; }
        public float CapacityScale { get; }

        private static void RequireFinite(float value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireFinite(Vector3Value value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }
    }

    public readonly struct SquatJointObservationSet
    {
        public SquatJointObservationSet(
            SquatJointObservation leftKnee,
            SquatJointObservation rightKnee,
            SquatJointObservation leftHip,
            SquatJointObservation rightHip,
            SquatJointObservation leftAnkle,
            SquatJointObservation rightAnkle,
            SquatJointObservation abdomen,
            SquatJointObservation thorax)
        {
            LeftKnee = leftKnee;
            RightKnee = rightKnee;
            LeftHip = leftHip;
            RightHip = rightHip;
            LeftAnkle = leftAnkle;
            RightAnkle = rightAnkle;
            Abdomen = abdomen;
            Thorax = thorax;
        }

        public SquatJointObservation LeftKnee { get; }
        public SquatJointObservation RightKnee { get; }
        public SquatJointObservation LeftHip { get; }
        public SquatJointObservation RightHip { get; }
        public SquatJointObservation LeftAnkle { get; }
        public SquatJointObservation RightAnkle { get; }
        public SquatJointObservation Abdomen { get; }
        public SquatJointObservation Thorax { get; }
    }

    /// <summary>
    /// One immutable, raw post-physics squat observation. The value contains
    /// no Unity object references and no mutable collections.
    /// </summary>
    public readonly struct SquatObservationSnapshot
    {
        public const string SchemaId = "GAM12_SQUAT_OBSERVATION_SNAPSHOT_V1";
        public const string ProvenanceVersion = "GAM12_P1_OBSERVATION_PROVENANCE_V1";

        public SquatObservationSnapshot(
            ulong simulationTick,
            double simulationTimeSeconds,
            double fixedStepSeconds,
            bool hasAttemptRelativeTime,
            double attemptRelativeTimeSeconds,
            SquatState state,
            SquatPhaseDirection direction,
            float sq,
            SquatIntentSnapshot intent,
            SquatBarObservation bar,
            SquatDepthLandmarks depth,
            SquatSupportObservation support,
            SquatFootObservation leftFoot,
            SquatFootObservation rightFoot,
            SquatJointObservationSet joints,
            Vector3Value pelvisPositionWorldMeters,
            Vector3Value pelvisLinearVelocityWorldMetersPerSecond,
            QuaternionValue thoraxOrientationWorldFromBody,
            float trunkWorldPitchRadians,
            SquatTelemetryAvailability driveAvailability,
            bool driveSaturated,
            float maximumModeledDemand,
            SquatTelemetryQualityFlags quality)
        {
            RequireFinite(simulationTimeSeconds, nameof(simulationTimeSeconds));
            RequireFinite(fixedStepSeconds, nameof(fixedStepSeconds));
            if (fixedStepSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(fixedStepSeconds));
            if (hasAttemptRelativeTime)
            {
                RequireFinite(attemptRelativeTimeSeconds, nameof(attemptRelativeTimeSeconds));
                if (attemptRelativeTimeSeconds < 0d)
                    throw new ArgumentOutOfRangeException(nameof(attemptRelativeTimeSeconds));
            }
            else if (!double.IsNaN(attemptRelativeTimeSeconds))
            {
                throw new ArgumentException("Unavailable attempt-relative time must be NaN.", nameof(attemptRelativeTimeSeconds));
            }
            if (intent.Tick != simulationTick ||
                Math.Abs(intent.SimulationTimeSeconds - simulationTimeSeconds) > FoundationTolerances.SimulationTimeMapping)
                throw new ArgumentException("Snapshot intent must belong to the same simulation tick.", nameof(intent));
            RequireFinite(sq, nameof(sq));
            if (sq < 0f || sq > 1f)
                throw new ArgumentOutOfRangeException(nameof(sq));

            RequireOptionalVector(pelvisPositionWorldMeters, nameof(pelvisPositionWorldMeters));
            RequireOptionalVector(pelvisLinearVelocityWorldMetersPerSecond, nameof(pelvisLinearVelocityWorldMetersPerSecond));
            RequireOptionalQuaternion(thoraxOrientationWorldFromBody, nameof(thoraxOrientationWorldFromBody));
            if (SquatTelemetryValue.IsFinite(thoraxOrientationWorldFromBody))
                thoraxOrientationWorldFromBody = thoraxOrientationWorldFromBody.Canonicalized();
            if (SquatTelemetryValue.IsUnavailable(thoraxOrientationWorldFromBody))
            {
                if (!float.IsNaN(trunkWorldPitchRadians))
                    throw new ArgumentException("Unavailable trunk orientation requires an unavailable pitch.", nameof(trunkWorldPitchRadians));
            }
            else
            {
                RequireFinite(trunkWorldPitchRadians, nameof(trunkWorldPitchRadians));
            }

            if (driveAvailability == SquatTelemetryAvailability.AVAILABLE)
            {
                RequireFinite(maximumModeledDemand, nameof(maximumModeledDemand));
                if (maximumModeledDemand < 0f)
                    throw new ArgumentOutOfRangeException(nameof(maximumModeledDemand));
            }
            else if (!float.IsNaN(maximumModeledDemand))
            {
                throw new ArgumentException("Unavailable drive demand must be NaN.", nameof(maximumModeledDemand));
            }
            if (driveAvailability != SquatTelemetryAvailability.AVAILABLE && driveSaturated)
                throw new ArgumentException("Unavailable drive diagnostics cannot report saturation.", nameof(driveSaturated));

            if (!quality.HasFlag(SquatTelemetryQualityFlags.POST_PHYSICS) ||
                !quality.HasFlag(SquatTelemetryQualityFlags.RAW))
                throw new ArgumentException("Canonical squat snapshots must be raw post-physics observations.", nameof(quality));

            SimulationTick = simulationTick;
            SimulationTimeSeconds = simulationTimeSeconds;
            FixedStepSeconds = fixedStepSeconds;
            HasAttemptRelativeTime = hasAttemptRelativeTime;
            AttemptRelativeTimeSeconds = hasAttemptRelativeTime ? attemptRelativeTimeSeconds : double.NaN;
            State = state;
            Direction = direction;
            Sq = sq;
            Intent = intent;
            Bar = bar;
            Depth = depth;
            Support = support;
            LeftFoot = leftFoot;
            RightFoot = rightFoot;
            Joints = joints;
            PelvisPositionWorldMeters = pelvisPositionWorldMeters;
            PelvisLinearVelocityWorldMetersPerSecond = pelvisLinearVelocityWorldMetersPerSecond;
            ThoraxOrientationWorldFromBody = thoraxOrientationWorldFromBody;
            TrunkWorldPitchRadians = trunkWorldPitchRadians;
            DriveAvailability = driveAvailability;
            DriveSaturated = driveSaturated;
            MaximumModeledDemand = driveAvailability == SquatTelemetryAvailability.AVAILABLE
                ? maximumModeledDemand
                : float.NaN;
            Quality = quality;
        }

        public ulong SimulationTick { get; }
        public double SimulationTimeSeconds { get; }
        public double FixedStepSeconds { get; }
        public bool HasAttemptRelativeTime { get; }
        public double AttemptRelativeTimeSeconds { get; }
        public SquatState State { get; }
        public SquatPhaseDirection Direction { get; }
        public float Sq { get; }
        public SquatIntentSnapshot Intent { get; }
        public SquatBarObservation Bar { get; }
        public SquatDepthLandmarks Depth { get; }
        public SquatSupportObservation Support { get; }
        public SquatFootObservation LeftFoot { get; }
        public SquatFootObservation RightFoot { get; }
        public SquatJointObservationSet Joints { get; }
        public Vector3Value PelvisPositionWorldMeters { get; }
        public Vector3Value PelvisLinearVelocityWorldMetersPerSecond { get; }
        public QuaternionValue ThoraxOrientationWorldFromBody { get; }
        public float TrunkWorldPitchRadians { get; }
        public SquatTelemetryAvailability PelvisAvailability =>
            SquatTelemetryValue.IsFinite(PelvisPositionWorldMeters) &&
            SquatTelemetryValue.IsFinite(PelvisLinearVelocityWorldMetersPerSecond)
                ? SquatTelemetryAvailability.AVAILABLE
                : SquatTelemetryAvailability.NOT_AVAILABLE;
        public SquatTelemetryAvailability TrunkAvailability =>
            SquatTelemetryValue.IsFinite(ThoraxOrientationWorldFromBody) &&
            SquatTelemetryValue.IsFinite(TrunkWorldPitchRadians)
                ? SquatTelemetryAvailability.AVAILABLE
                : SquatTelemetryAvailability.NOT_AVAILABLE;
        public SquatTelemetryAvailability DriveAvailability { get; }
        public bool DriveSaturated { get; }
        public float MaximumModeledDemand { get; }
        public SquatTelemetryQualityFlags Quality { get; }
        public string Schema => SchemaId;

        public static float WorldPitchRadians(QuaternionValue worldFromBody)
        {
            if (!SquatTelemetryValue.IsFinite(worldFromBody))
                throw new ArgumentOutOfRangeException(nameof(worldFromBody));
            Vector3Value forward = worldFromBody.Canonicalized().TransformDirection(CoordinateContract.ForwardAxis);
            return (float)Math.Atan2(forward.Y, forward.Z);
        }

        private static void RequireFinite(double value, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireFinite(float value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value))
                throw new ArgumentOutOfRangeException(name);
        }

        private static void RequireOptionalVector(Vector3Value value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value) && !SquatTelemetryValue.IsUnavailable(value))
                throw new ArgumentException("Optional vector must be finite or explicitly unavailable.", name);
        }

        private static void RequireOptionalQuaternion(QuaternionValue value, string name)
        {
            if (!SquatTelemetryValue.IsFinite(value) && !SquatTelemetryValue.IsUnavailable(value))
                throw new ArgumentException("Optional quaternion must be finite or explicitly unavailable.", name);
        }
    }
}
