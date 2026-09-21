using System;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Read-only evidence from the production squat start predicate and the
    /// control state that produced the observed snapshot.
    /// </summary>
    public sealed class SquatStartPredicateDiagnostic
    {
        internal SquatStartPredicateDiagnostic(
            ulong simulationTick,
            double simulationTimeSeconds,
            bool overallStartCandidate,
            int consecutiveValidRunLength,
            int requiredRunLength,
            bool barAvailable,
            float barLinearSpeedMagnitudeMps,
            bool barLinearSpeedPass,
            float barLinearSpeedMarginMps,
            float barLinearSpeedThresholdMps,
            float barVerticalSpeedMps,
            bool barVerticalSpeedPass,
            float barVerticalSpeedMarginMps,
            float barVerticalSpeedThresholdMps,
            float barAngularSpeedMagnitudeRadS,
            bool barAngularSpeedPass,
            float barAngularSpeedMarginRadS,
            float barAngularSpeedThresholdRadS,
            bool supportAvailable,
            bool supportPresent,
            bool leftFootAvailable,
            bool leftFootContact,
            bool rightFootAvailable,
            bool rightFootContact,
            float leftKneeActualAngleRad,
            bool leftKneePass,
            float leftKneeMarginRad,
            float rightKneeActualAngleRad,
            bool rightKneePass,
            float rightKneeMarginRad,
            float leftHipActualAngleRad,
            bool leftHipPass,
            float leftHipMarginRad,
            float rightHipActualAngleRad,
            bool rightHipPass,
            float rightHipMarginRad,
            float abdomenActualAngleRad,
            bool abdomenPass,
            float abdomenMarginRad,
            float thoraxActualAngleRad,
            bool thoraxPass,
            float thoraxMarginRad,
            float rawAnkleAuthorityFraction,
            float guardedAnkleDemandRad,
            float appliedAnkleDemandRad,
            float postureGuardScale,
            float hipStrategyBlend,
            float hipAuthority,
            float trunkAuthority,
            float ankleStandingBiasDegrees,
            float kneeStandingBiasDegrees,
            float hipStandingBiasDegrees,
            float abdomenStandingBiasDegrees,
            float thoraxStandingBiasDegrees,
            float comSpeedMps,
            float captureMargin2DM)
        {
            SimulationTick = simulationTick;
            SimulationTimeSeconds = simulationTimeSeconds;
            OverallStartCandidate = overallStartCandidate;
            ConsecutiveValidRunLength = consecutiveValidRunLength;
            RequiredRunLength = requiredRunLength;
            BarAvailable = barAvailable;
            BarLinearSpeedMagnitudeMps = barLinearSpeedMagnitudeMps;
            BarLinearSpeedPass = barLinearSpeedPass;
            BarLinearSpeedMarginMps = barLinearSpeedMarginMps;
            BarLinearSpeedThresholdMps = barLinearSpeedThresholdMps;
            BarVerticalSpeedMps = barVerticalSpeedMps;
            BarVerticalSpeedPass = barVerticalSpeedPass;
            BarVerticalSpeedMarginMps = barVerticalSpeedMarginMps;
            BarVerticalSpeedThresholdMps = barVerticalSpeedThresholdMps;
            BarAngularSpeedMagnitudeRadS = barAngularSpeedMagnitudeRadS;
            BarAngularSpeedPass = barAngularSpeedPass;
            BarAngularSpeedMarginRadS = barAngularSpeedMarginRadS;
            BarAngularSpeedThresholdRadS = barAngularSpeedThresholdRadS;
            SupportAvailable = supportAvailable;
            SupportPresent = supportPresent;
            LeftFootAvailable = leftFootAvailable;
            LeftFootContact = leftFootContact;
            RightFootAvailable = rightFootAvailable;
            RightFootContact = rightFootContact;
            LeftKneeActualAngleRad = leftKneeActualAngleRad;
            LeftKneePass = leftKneePass;
            LeftKneeMarginRad = leftKneeMarginRad;
            RightKneeActualAngleRad = rightKneeActualAngleRad;
            RightKneePass = rightKneePass;
            RightKneeMarginRad = rightKneeMarginRad;
            LeftHipActualAngleRad = leftHipActualAngleRad;
            LeftHipPass = leftHipPass;
            LeftHipMarginRad = leftHipMarginRad;
            RightHipActualAngleRad = rightHipActualAngleRad;
            RightHipPass = rightHipPass;
            RightHipMarginRad = rightHipMarginRad;
            AbdomenActualAngleRad = abdomenActualAngleRad;
            AbdomenPass = abdomenPass;
            AbdomenMarginRad = abdomenMarginRad;
            ThoraxActualAngleRad = thoraxActualAngleRad;
            ThoraxPass = thoraxPass;
            ThoraxMarginRad = thoraxMarginRad;
            RawAnkleAuthorityFraction = rawAnkleAuthorityFraction;
            GuardedAnkleDemandRad = guardedAnkleDemandRad;
            AppliedAnkleDemandRad = appliedAnkleDemandRad;
            PostureGuardScale = postureGuardScale;
            HipStrategyBlend = hipStrategyBlend;
            HipAuthority = hipAuthority;
            TrunkAuthority = trunkAuthority;
            AnkleStandingBiasDegrees = ankleStandingBiasDegrees;
            KneeStandingBiasDegrees = kneeStandingBiasDegrees;
            HipStandingBiasDegrees = hipStandingBiasDegrees;
            AbdomenStandingBiasDegrees = abdomenStandingBiasDegrees;
            ThoraxStandingBiasDegrees = thoraxStandingBiasDegrees;
            ComSpeedMps = comSpeedMps;
            CaptureMargin2DM = captureMargin2DM;
        }

        public ulong SimulationTick { get; }
        public double SimulationTimeSeconds { get; }
        public bool OverallStartCandidate { get; }
        public int ConsecutiveValidRunLength { get; }
        public int RequiredRunLength { get; }

        public bool BarAvailable { get; }
        public float BarLinearSpeedMagnitudeMps { get; }
        public bool BarLinearSpeedPass { get; }
        public float BarLinearSpeedMarginMps { get; }
        public float BarLinearSpeedThresholdMps { get; }
        public float BarVerticalSpeedMps { get; }
        public bool BarVerticalSpeedPass { get; }
        public float BarVerticalSpeedMarginMps { get; }
        public float BarVerticalSpeedThresholdMps { get; }
        public float BarAngularSpeedMagnitudeRadS { get; }
        public bool BarAngularSpeedPass { get; }
        public float BarAngularSpeedMarginRadS { get; }
        public float BarAngularSpeedThresholdRadS { get; }

        public bool SupportAvailable { get; }
        public bool SupportPresent { get; }
        public bool LeftFootAvailable { get; }
        public bool LeftFootContact { get; }
        public bool RightFootAvailable { get; }
        public bool RightFootContact { get; }

        public float LeftKneeActualAngleRad { get; }
        public bool LeftKneePass { get; }
        public float LeftKneeMarginRad { get; }
        public float RightKneeActualAngleRad { get; }
        public bool RightKneePass { get; }
        public float RightKneeMarginRad { get; }
        public float LeftHipActualAngleRad { get; }
        public bool LeftHipPass { get; }
        public float LeftHipMarginRad { get; }
        public float RightHipActualAngleRad { get; }
        public bool RightHipPass { get; }
        public float RightHipMarginRad { get; }
        public float AbdomenActualAngleRad { get; }
        public bool AbdomenPass { get; }
        public float AbdomenMarginRad { get; }
        public float ThoraxActualAngleRad { get; }
        public bool ThoraxPass { get; }
        public float ThoraxMarginRad { get; }

        public float RawAnkleAuthorityFraction { get; }
        public float GuardedAnkleDemandRad { get; }
        public float AppliedAnkleDemandRad { get; }
        public float PostureGuardScale { get; }
        public float HipStrategyBlend { get; }
        public float HipAuthority { get; }
        public float TrunkAuthority { get; }

        public float AnkleStandingBiasDegrees { get; }
        public float KneeStandingBiasDegrees { get; }
        public float HipStandingBiasDegrees { get; }
        public float AbdomenStandingBiasDegrees { get; }
        public float ThoraxStandingBiasDegrees { get; }

        public float ComSpeedMps { get; }
        public float CaptureMargin2DM { get; }

        internal SquatStartPredicateDiagnostic WithConsecutiveValidRun(int runLength)
        {
            return new SquatStartPredicateDiagnostic(
                SimulationTick,
                SimulationTimeSeconds,
                OverallStartCandidate,
                runLength,
                RequiredRunLength,
                BarAvailable,
                BarLinearSpeedMagnitudeMps,
                BarLinearSpeedPass,
                BarLinearSpeedMarginMps,
                BarLinearSpeedThresholdMps,
                BarVerticalSpeedMps,
                BarVerticalSpeedPass,
                BarVerticalSpeedMarginMps,
                BarVerticalSpeedThresholdMps,
                BarAngularSpeedMagnitudeRadS,
                BarAngularSpeedPass,
                BarAngularSpeedMarginRadS,
                BarAngularSpeedThresholdRadS,
                SupportAvailable,
                SupportPresent,
                LeftFootAvailable,
                LeftFootContact,
                RightFootAvailable,
                RightFootContact,
                LeftKneeActualAngleRad,
                LeftKneePass,
                LeftKneeMarginRad,
                RightKneeActualAngleRad,
                RightKneePass,
                RightKneeMarginRad,
                LeftHipActualAngleRad,
                LeftHipPass,
                LeftHipMarginRad,
                RightHipActualAngleRad,
                RightHipPass,
                RightHipMarginRad,
                AbdomenActualAngleRad,
                AbdomenPass,
                AbdomenMarginRad,
                ThoraxActualAngleRad,
                ThoraxPass,
                ThoraxMarginRad,
                RawAnkleAuthorityFraction,
                GuardedAnkleDemandRad,
                AppliedAnkleDemandRad,
                PostureGuardScale,
                HipStrategyBlend,
                HipAuthority,
                TrunkAuthority,
                AnkleStandingBiasDegrees,
                KneeStandingBiasDegrees,
                HipStandingBiasDegrees,
                AbdomenStandingBiasDegrees,
                ThoraxStandingBiasDegrees,
                ComSpeedMps,
                CaptureMargin2DM);
        }
    }
}
