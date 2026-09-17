using System;
using System.Collections.Generic;

namespace PowerliftingSimulator.Squat
{
    /// <summary>
    /// Offline sticking classification of one frozen squat trace. It is a
    /// description of a successful or unsuccessful ascent, never failure truth:
    /// the P3 detector remains the only physical-failure authority.
    /// </summary>
    public enum SquatStickingClassification : byte
    {
        /// <summary>Lockout reached after a noise-resolvable vmax1 → vmin → vmax2 dip.</summary>
        RECOVERABLE_STICKING,
        /// <summary>Lockout reached with no noise-resolvable velocity dip.</summary>
        NO_RESOLVABLE_STICKING,
        /// <summary>No physical lockout, so no successful sticking region can exist.</summary>
        NOT_APPLICABLE_NO_LOCKOUT,
        /// <summary>The trace does not carry enough bar signal to analyse.</summary>
        INSUFFICIENT_SIGNAL
    }

    /// <summary>
    /// Attempt-level load-response metrics derived from one frozen trace.
    /// Every event tick is a raw physical bar event, not a rule label.
    /// Missing values are NaN or <see cref="NotAvailableTick"/>, never zero.
    /// </summary>
    public sealed class SquatLoadResponseMetrics
    {
        public const ulong NotAvailableTick = ulong.MaxValue;

        public int SampleCount { get; internal set; }
        public bool AllEvidenceFinite { get; internal set; }
        public string FirstNonFiniteField { get; internal set; } = string.Empty;

        public float StandingBarY { get; internal set; } = float.NaN;
        public ulong DescentOnsetTick { get; internal set; } = NotAvailableTick;
        public ulong BottomTick { get; internal set; } = NotAvailableTick;
        public float BottomBarY { get; internal set; } = float.NaN;
        public ulong AscentEstablishmentTick { get; internal set; } = NotAvailableTick;
        public ulong CompletionRegionEntryTick { get; internal set; } = NotAvailableTick;
        public ulong LockoutTick { get; internal set; } = NotAvailableTick;
        public bool PhysicalLockout => LockoutTick != NotAvailableTick;

        public float MinimumWorstSideDepthM { get; internal set; } = float.NaN;
        public bool LegalPhysicalDepth { get; internal set; }

        public float ConcentricDurationSeconds { get; internal set; } = float.NaN;
        public float AscentToCompletionRegionSeconds { get; internal set; } = float.NaN;
        public float ConcentricDisplacementM { get; internal set; } = float.NaN;
        public float MeanConcentricVelocityMps { get; internal set; } = float.NaN;
        public float TimeAveragedConcentricVelocityMps { get; internal set; } = float.NaN;
        public float PeakConcentricVelocityMps { get; internal set; } = float.NaN;
        public float MinimumMidAscentVelocityMps { get; internal set; } = float.NaN;
        public float MaximumDownwardReversalAfterAscentM { get; internal set; } = float.NaN;
        public float MaximumBarHeightGainM { get; internal set; } = float.NaN;

        public float MaximumModeledDemand { get; internal set; } = float.NaN;
        public float MeanAscentModeledDemand { get; internal set; } = float.NaN;
        public float AscentNearSaturationFraction { get; internal set; } = float.NaN;
        public float AscentSaturationFraction { get; internal set; } = float.NaN;

        public float PeakKneeDemand { get; internal set; } = float.NaN;
        public float PeakHipDemand { get; internal set; } = float.NaN;
        public float PeakAnkleDemand { get; internal set; } = float.NaN;
        public float PeakTrunkDemand { get; internal set; } = float.NaN;
        public float PeakKneeTrackingErrorRad { get; internal set; } = float.NaN;
        public float PeakHipTrackingErrorRad { get; internal set; } = float.NaN;
        public float PeakAnkleTrackingErrorRad { get; internal set; } = float.NaN;
        public float PeakTrunkTrackingErrorRad { get; internal set; } = float.NaN;
        public float MeanAscentKneeTrackingErrorRad { get; internal set; } = float.NaN;
        public float MeanAscentHipTrackingErrorRad { get; internal set; } = float.NaN;

        public float KneeMaximumForceNm { get; internal set; } = float.NaN;
        public float HipMaximumForceNm { get; internal set; } = float.NaN;
        public float AnkleMaximumForceNm { get; internal set; } = float.NaN;
        public float TrunkMaximumForceNm { get; internal set; } = float.NaN;
        public float KneeCapacityScale { get; internal set; } = float.NaN;
        public bool MaximumForceConstantDuringAttempt { get; internal set; }

        public float TrunkPitchMinRad { get; internal set; } = float.NaN;
        public float TrunkPitchMaxRad { get; internal set; } = float.NaN;
        public float MinimumFrontSupportMarginM { get; internal set; } = float.NaN;
        public float MinimumRearSupportMarginM { get; internal set; } = float.NaN;
        public float ComZMinM { get; internal set; } = float.NaN;
        public float ComZMaxM { get; internal set; } = float.NaN;
        public float MaximumSaddleSeparationM { get; internal set; } = float.NaN;
        public bool SaddleEverDetachedOrBroken { get; internal set; }
        public bool SupportEverLost { get; internal set; }

        public SquatStickingClassification Sticking { get; internal set; } = SquatStickingClassification.INSUFFICIENT_SIGNAL;
        public SquatBarVelocityEventStatus StickingDetectorStatus { get; internal set; } = SquatBarVelocityEventStatus.InsufficientSignal;
        public float StickingVmax1Mps { get; internal set; } = float.NaN;
        public float StickingVminMps { get; internal set; } = float.NaN;
        public float StickingVmax2Mps { get; internal set; } = float.NaN;
        public ulong StickingVmax1Tick { get; internal set; } = NotAvailableTick;
        public ulong StickingVminTick { get; internal set; } = NotAvailableTick;
        public ulong StickingVmax2Tick { get; internal set; } = NotAvailableTick;
        public float StickingVelocityReductionFraction { get; internal set; } = float.NaN;
        public float StickingIntervalSeconds { get; internal set; } = float.NaN;
        public float NoiseVelocityThresholdMps { get; internal set; } = float.NaN;
    }

    /// <summary>
    /// OFFLINE_DIAGNOSTIC_ONLY. GAM-13 post-attempt load-response analysis over
    /// a frozen squat trace. There is no runtime callsite: it reads immutable
    /// snapshots after the attempt truth is frozen and cannot influence physics,
    /// rules, or failure classification.
    ///
    /// The event boundaries below are analysis definitions for comparing
    /// attempts with each other. They reuse the P3 motion-context values where
    /// one exists and are not human biomechanical thresholds.
    /// </summary>
    public static class SquatLoadResponseAnalyzer
    {
        public const string AnalyzerVersion = "GAM13_SQUAT_LOAD_RESPONSE_ANALYZER_V1";
        public const string ClaimClass = "GAME_ANALYSIS_DERIVED";

        /// <summary>Bar drop below the standing sample that marks physical descent onset.</summary>
        public const float DescentOnsetDisplacementM = 0.005f;
        /// <summary>Modeled demand treated as near the drive ceiling.</summary>
        public const float NearSaturationDemand = 0.80f;
        /// <summary>Samples before the first descent used as the stationary noise window.</summary>
        public const int MaximumStationarySamples = 60;
        /// <summary>Consecutive bilateral legal-depth samples (PSMS-SQ-11 persistence seed).</summary>
        public const int DepthPersistenceTicks = 3;
        /// <summary>Samples before the bottom handed to the event detector so v0 is interior.</summary>
        public const int PreBottomDetectorSamples = 10;

        public static SquatLoadResponseMetrics Analyze(
            SquatTrace trace,
            ulong lockoutTick,
            SquatFailureCalibration motionCalibration = null)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));
            if (!trace.IsFrozen || trace.IsRecording)
                throw new InvalidOperationException("Load-response analysis requires a frozen trace.");

            SquatFailureCalibration motion = motionCalibration ?? SquatFailureCalibration.Default;
            SquatLoadResponseMetrics metrics = new SquatLoadResponseMetrics { SampleCount = trace.Count };
            metrics.AllEvidenceFinite = CheckFinite(trace, out string firstNonFinite);
            metrics.FirstNonFiniteField = firstNonFinite;
            if (trace.Count < 8 || !trace[0].Bar.IsAvailable)
                return metrics;

            double stepSeconds = trace[0].FixedStepSeconds;
            ulong firstTick = trace[0].SimulationTick;
            metrics.StandingBarY = trace[0].Bar.PositionWorldMeters.Y;
            if (lockoutTick != SquatLoadResponseMetrics.NotAvailableTick &&
                lockoutTick >= firstTick && lockoutTick <= trace[trace.Count - 1].SimulationTick)
                metrics.LockoutTick = lockoutTick;

            FindMotionEvents(trace, motion, metrics, out int descentIndex, out int bottomIndex, out int ascentIndex, out int completionIndex);
            int lockoutIndex = metrics.PhysicalLockout ? (int)(metrics.LockoutTick - firstTick) : -1;
            int ascentEndIndex = lockoutIndex >= 0 ? lockoutIndex : trace.Count - 1;

            SummarizeDepthPostureAndCapacity(trace, motion, metrics);

            if (bottomIndex >= 0)
            {
                SummarizeKinematics(trace, metrics, bottomIndex, ascentIndex, completionIndex, lockoutIndex, ascentEndIndex, stepSeconds);
                SummarizeControl(trace, metrics, bottomIndex, ascentEndIndex);
                SummarizeSticking(trace, metrics, descentIndex, bottomIndex, ascentEndIndex, stepSeconds);
            }

            return metrics;
        }

        private static void FindMotionEvents(
            SquatTrace trace,
            SquatFailureCalibration motion,
            SquatLoadResponseMetrics metrics,
            out int descentIndex,
            out int bottomIndex,
            out int ascentIndex,
            out int completionIndex)
        {
            descentIndex = -1;
            bottomIndex = -1;
            ascentIndex = -1;
            completionIndex = -1;
            float standing = metrics.StandingBarY;
            float runningMinimum = float.PositiveInfinity;
            int runningMinimumIndex = -1;
            int upwardRun = 0;
            for (int index = 0; index < trace.Count; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (!sample.Bar.IsAvailable)
                {
                    upwardRun = 0;
                    continue;
                }

                float y = sample.Bar.PositionWorldMeters.Y;
                float vy = sample.Bar.LinearVelocityWorldMetersPerSecond.Y;
                if (descentIndex < 0)
                {
                    if (standing - y >= DescentOnsetDisplacementM)
                        descentIndex = index;
                    else
                        continue;
                }

                if (ascentIndex < 0)
                {
                    if (y < runningMinimum)
                    {
                        runningMinimum = y;
                        runningMinimumIndex = index;
                    }

                    upwardRun = vy >= motion.AscentEstablishmentVelocityMps ? upwardRun + 1 : 0;
                    if (upwardRun >= motion.AscentEstablishmentPersistenceTicks &&
                        y - runningMinimum >= motion.AscentEstablishmentDisplacementM)
                    {
                        ascentIndex = index;
                        bottomIndex = runningMinimumIndex;
                    }
                    continue;
                }

                if (completionIndex < 0 && y >= standing - motion.LockoutHeightToleranceM)
                {
                    completionIndex = index;
                    break;
                }
            }

            if (ascentIndex < 0 && runningMinimumIndex >= 0)
                bottomIndex = runningMinimumIndex;

            ulong firstTick = trace[0].SimulationTick;
            if (descentIndex >= 0)
                metrics.DescentOnsetTick = firstTick + (ulong)descentIndex;
            if (bottomIndex >= 0)
            {
                metrics.BottomTick = firstTick + (ulong)bottomIndex;
                metrics.BottomBarY = trace[bottomIndex].Bar.PositionWorldMeters.Y;
            }
            if (ascentIndex >= 0)
                metrics.AscentEstablishmentTick = firstTick + (ulong)ascentIndex;
            if (completionIndex >= 0)
                metrics.CompletionRegionEntryTick = firstTick + (ulong)completionIndex;
        }

        private static void SummarizeKinematics(
            SquatTrace trace,
            SquatLoadResponseMetrics metrics,
            int bottomIndex,
            int ascentIndex,
            int completionIndex,
            int lockoutIndex,
            int ascentEndIndex,
            double stepSeconds)
        {
            float bottomY = trace[bottomIndex].Bar.PositionWorldMeters.Y;
            float peakVelocity = float.NegativeInfinity;
            double velocitySum = 0d;
            int velocityCount = 0;
            float maximumHeight = bottomY;
            float runningMaximumSinceAscent = float.NegativeInfinity;
            float maximumReversal = ascentIndex >= 0 ? 0f : float.NaN;
            for (int index = bottomIndex; index <= ascentEndIndex; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (!sample.Bar.IsAvailable)
                    continue;
                float y = sample.Bar.PositionWorldMeters.Y;
                float vy = sample.Bar.LinearVelocityWorldMetersPerSecond.Y;
                peakVelocity = Math.Max(peakVelocity, vy);
                velocitySum += vy;
                velocityCount++;
                maximumHeight = Math.Max(maximumHeight, y);
                if (ascentIndex >= 0 && index >= ascentIndex)
                {
                    runningMaximumSinceAscent = Math.Max(runningMaximumSinceAscent, y);
                    maximumReversal = Math.Max(maximumReversal, runningMaximumSinceAscent - y);
                }
            }

            metrics.MaximumBarHeightGainM = maximumHeight - bottomY;
            metrics.MaximumDownwardReversalAfterAscentM = maximumReversal;
            if (velocityCount > 0)
            {
                metrics.PeakConcentricVelocityMps = peakVelocity;
                metrics.TimeAveragedConcentricVelocityMps = (float)(velocitySum / velocityCount);
            }

            if (lockoutIndex > bottomIndex)
            {
                float lockoutY = trace[lockoutIndex].Bar.PositionWorldMeters.Y;
                float duration = (float)((lockoutIndex - bottomIndex) * stepSeconds);
                metrics.ConcentricDurationSeconds = duration;
                metrics.ConcentricDisplacementM = lockoutY - bottomY;
                metrics.MeanConcentricVelocityMps = (lockoutY - bottomY) / duration;
            }

            if (completionIndex > bottomIndex)
                metrics.AscentToCompletionRegionSeconds = (float)((completionIndex - bottomIndex) * stepSeconds);

            if (ascentIndex >= 0)
            {
                int midEnd = completionIndex > ascentIndex ? completionIndex : ascentEndIndex;
                float minimum = float.PositiveInfinity;
                for (int index = ascentIndex; index <= midEnd; index++)
                {
                    if (trace[index].Bar.IsAvailable)
                        minimum = Math.Min(minimum, trace[index].Bar.LinearVelocityWorldMetersPerSecond.Y);
                }
                if (!float.IsPositiveInfinity(minimum))
                    metrics.MinimumMidAscentVelocityMps = minimum;
            }
        }

        private static void SummarizeControl(
            SquatTrace trace,
            SquatLoadResponseMetrics metrics,
            int bottomIndex,
            int ascentEndIndex)
        {
            float maximumDemand = float.NegativeInfinity;
            double demandSum = 0d;
            int demandCount = 0;
            int near = 0;
            int saturated = 0;
            float knee = float.NegativeInfinity, hip = float.NegativeInfinity, ankle = float.NegativeInfinity, trunk = float.NegativeInfinity;
            float kneeError = float.NegativeInfinity, hipError = float.NegativeInfinity, ankleError = float.NegativeInfinity, trunkError = float.NegativeInfinity;
            double kneeErrorSum = 0d, hipErrorSum = 0d;
            int errorCount = 0;
            for (int index = bottomIndex; index <= ascentEndIndex; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (sample.DriveAvailability == SquatTelemetryAvailability.AVAILABLE && IsFinite(sample.MaximumModeledDemand))
                {
                    float demand = sample.MaximumModeledDemand;
                    maximumDemand = Math.Max(maximumDemand, demand);
                    demandSum += demand;
                    demandCount++;
                    if (demand >= NearSaturationDemand)
                        near++;
                    if (sample.DriveSaturated)
                        saturated++;
                }

                SquatJointObservationSet joints = sample.Joints;
                knee = MaxDemand(knee, joints.LeftKnee, joints.RightKnee);
                hip = MaxDemand(hip, joints.LeftHip, joints.RightHip);
                ankle = MaxDemand(ankle, joints.LeftAnkle, joints.RightAnkle);
                trunk = MaxDemand(trunk, joints.Abdomen, joints.Thorax);
                float kneeNow = MaxError(joints.LeftKnee, joints.RightKnee);
                float hipNow = MaxError(joints.LeftHip, joints.RightHip);
                kneeError = Math.Max(kneeError, kneeNow);
                hipError = Math.Max(hipError, hipNow);
                ankleError = Math.Max(ankleError, MaxError(joints.LeftAnkle, joints.RightAnkle));
                trunkError = Math.Max(trunkError, MaxError(joints.Abdomen, joints.Thorax));
                if (IsFinite(kneeNow) && IsFinite(hipNow))
                {
                    kneeErrorSum += kneeNow;
                    hipErrorSum += hipNow;
                    errorCount++;
                }
            }

            if (demandCount > 0)
            {
                metrics.MaximumModeledDemand = maximumDemand;
                metrics.MeanAscentModeledDemand = (float)(demandSum / demandCount);
                metrics.AscentNearSaturationFraction = near / (float)demandCount;
                metrics.AscentSaturationFraction = saturated / (float)demandCount;
            }

            metrics.PeakKneeDemand = FiniteOrNaN(knee);
            metrics.PeakHipDemand = FiniteOrNaN(hip);
            metrics.PeakAnkleDemand = FiniteOrNaN(ankle);
            metrics.PeakTrunkDemand = FiniteOrNaN(trunk);
            metrics.PeakKneeTrackingErrorRad = FiniteOrNaN(kneeError);
            metrics.PeakHipTrackingErrorRad = FiniteOrNaN(hipError);
            metrics.PeakAnkleTrackingErrorRad = FiniteOrNaN(ankleError);
            metrics.PeakTrunkTrackingErrorRad = FiniteOrNaN(trunkError);
            if (errorCount > 0)
            {
                metrics.MeanAscentKneeTrackingErrorRad = (float)(kneeErrorSum / errorCount);
                metrics.MeanAscentHipTrackingErrorRad = (float)(hipErrorSum / errorCount);
            }
        }

        private static void SummarizeDepthPostureAndCapacity(
            SquatTrace trace,
            SquatFailureCalibration motion,
            SquatLoadResponseMetrics metrics)
        {
            float minimumDepth = float.PositiveInfinity;
            int legalRun = 0;
            bool legal = false;
            float trunkMin = float.PositiveInfinity, trunkMax = float.NegativeInfinity;
            float front = float.PositiveInfinity, rear = float.PositiveInfinity;
            float comMin = float.PositiveInfinity, comMax = float.NegativeInfinity;
            float saddle = float.NegativeInfinity;
            float firstKneeForce = float.NaN, firstHipForce = float.NaN, firstAnkleForce = float.NaN, firstTrunkForce = float.NaN;
            bool forceConstant = true;
            for (int index = 0; index < trace.Count; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (sample.Depth.Availability == SquatTelemetryAvailability.AVAILABLE)
                {
                    minimumDepth = Math.Min(minimumDepth, sample.Depth.WorstSideDepthM);
                    bool bilateral = sample.Depth.LeftDepthM <= -motion.LegalDepthMarginM &&
                        sample.Depth.RightDepthM <= -motion.LegalDepthMarginM;
                    legalRun = bilateral ? legalRun + 1 : 0;
                    if (legalRun >= DepthPersistenceTicks)
                        legal = true;
                }

                if (sample.TrunkAvailability == SquatTelemetryAvailability.AVAILABLE)
                {
                    trunkMin = Math.Min(trunkMin, sample.TrunkWorldPitchRadians);
                    trunkMax = Math.Max(trunkMax, sample.TrunkWorldPitchRadians);
                }

                if (sample.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE)
                {
                    if (sample.Support.HasSupport)
                    {
                        front = Math.Min(front, sample.Support.ComToSupportApFrontMarginM);
                        rear = Math.Min(rear, sample.Support.ComToSupportApRearMarginM);
                    }
                    else
                    {
                        metrics.SupportEverLost = true;
                    }
                }

                if (sample.Support.SystemComAvailability == SquatTelemetryAvailability.AVAILABLE)
                {
                    comMin = Math.Min(comMin, sample.Support.SystemComWorldMeters.Z);
                    comMax = Math.Max(comMax, sample.Support.SystemComWorldMeters.Z);
                }

                if (sample.Bar.IsAvailable && sample.Bar.SaddleAvailability == SquatTelemetryAvailability.AVAILABLE)
                {
                    if (IsFinite(sample.Bar.SaddleSeparationMeters))
                        saddle = Math.Max(saddle, sample.Bar.SaddleSeparationMeters);
                    if (!sample.Bar.SaddleAttached || sample.Bar.SaddleBroken)
                        metrics.SaddleEverDetachedOrBroken = true;
                }

                forceConstant &= TrackForce(ref firstKneeForce, sample.Joints.LeftKnee, sample.Joints.RightKnee);
                forceConstant &= TrackForce(ref firstHipForce, sample.Joints.LeftHip, sample.Joints.RightHip);
                forceConstant &= TrackForce(ref firstAnkleForce, sample.Joints.LeftAnkle, sample.Joints.RightAnkle);
                forceConstant &= TrackForce(ref firstTrunkForce, sample.Joints.Abdomen, sample.Joints.Thorax);
                if (index == 0 && sample.Joints.LeftKnee.DriveAvailability == SquatTelemetryAvailability.AVAILABLE)
                    metrics.KneeCapacityScale = sample.Joints.LeftKnee.CapacityScale;
            }

            metrics.MinimumWorstSideDepthM = FiniteOrNaN(minimumDepth);
            metrics.LegalPhysicalDepth = legal;
            metrics.TrunkPitchMinRad = FiniteOrNaN(trunkMin);
            metrics.TrunkPitchMaxRad = FiniteOrNaN(trunkMax);
            metrics.MinimumFrontSupportMarginM = FiniteOrNaN(front);
            metrics.MinimumRearSupportMarginM = FiniteOrNaN(rear);
            metrics.ComZMinM = FiniteOrNaN(comMin);
            metrics.ComZMaxM = FiniteOrNaN(comMax);
            metrics.MaximumSaddleSeparationM = FiniteOrNaN(saddle);
            metrics.KneeMaximumForceNm = firstKneeForce;
            metrics.HipMaximumForceNm = firstHipForce;
            metrics.AnkleMaximumForceNm = firstAnkleForce;
            metrics.TrunkMaximumForceNm = firstTrunkForce;
            metrics.MaximumForceConstantDuringAttempt = forceConstant;
        }

        private static void SummarizeSticking(
            SquatTrace trace,
            SquatLoadResponseMetrics metrics,
            int descentIndex,
            int bottomIndex,
            int ascentEndIndex,
            double stepSeconds)
        {
            int stationaryEnd = descentIndex > 0 ? descentIndex : 0;
            int stationaryStart = Math.Max(0, stationaryEnd - MaximumStationarySamples);
            List<float> stationary = new List<float>(stationaryEnd - stationaryStart);
            for (int index = stationaryStart; index < stationaryEnd; index++)
            {
                if (trace[index].Bar.IsAvailable)
                    stationary.Add(trace[index].Bar.LinearVelocityWorldMetersPerSecond.Y);
            }

            int start = Math.Max(0, bottomIndex - PreBottomDetectorSamples);
            List<SquatBarVelocitySample> samples = new List<SquatBarVelocitySample>(ascentEndIndex - start + 1);
            for (int index = start; index <= ascentEndIndex; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (!sample.Bar.IsAvailable)
                    continue;
                samples.Add(new SquatBarVelocitySample(
                    sample.SimulationTimeSeconds,
                    sample.Bar.PositionWorldMeters.Y,
                    sample.Bar.LinearVelocityWorldMetersPerSecond.Y));
            }

            SquatBarVelocityEventDetection detection = SquatBarVelocityEventDetector.Detect(
                samples,
                stationary,
                new SquatBarVelocityDetectorOptions((float)stepSeconds, 10f, 4f));
            metrics.StickingDetectorStatus = detection.Status;
            if (detection.NoiseFloor.IsAvailable)
                metrics.NoiseVelocityThresholdMps = 4f * detection.NoiseFloor.VelocityThresholdMetersPerSecond;

            if (detection.Status == SquatBarVelocityEventStatus.InsufficientSignal ||
                detection.Status == SquatBarVelocityEventStatus.InvalidInput)
            {
                metrics.Sticking = SquatStickingClassification.INSUFFICIENT_SIGNAL;
                return;
            }

            if (detection.HasStickingRegion &&
                detection.TryGetEvent(SquatBarVelocityEventKind.Vmax1, out SquatBarVelocityEvent vmax1) &&
                detection.TryGetEvent(SquatBarVelocityEventKind.Vmin, out SquatBarVelocityEvent vmin) &&
                detection.TryGetEvent(SquatBarVelocityEventKind.Vmax2, out SquatBarVelocityEvent vmax2))
            {
                ulong firstTick = trace[start].SimulationTick;
                metrics.StickingVmax1Mps = vmax1.FilteredVelocityMetersPerSecond;
                metrics.StickingVminMps = vmin.FilteredVelocityMetersPerSecond;
                metrics.StickingVmax2Mps = vmax2.FilteredVelocityMetersPerSecond;
                metrics.StickingVmax1Tick = firstTick + (ulong)vmax1.SampleIndex;
                metrics.StickingVminTick = firstTick + (ulong)vmin.SampleIndex;
                metrics.StickingVmax2Tick = firstTick + (ulong)vmax2.SampleIndex;
                metrics.StickingIntervalSeconds = (float)(vmin.TimeSeconds - vmax1.TimeSeconds);
                metrics.StickingVelocityReductionFraction = vmax1.FilteredVelocityMetersPerSecond > 0f
                    ? (vmax1.FilteredVelocityMetersPerSecond - vmin.FilteredVelocityMetersPerSecond) /
                      vmax1.FilteredVelocityMetersPerSecond
                    : float.NaN;
            }

            if (!metrics.PhysicalLockout)
                metrics.Sticking = SquatStickingClassification.NOT_APPLICABLE_NO_LOCKOUT;
            else
                metrics.Sticking = detection.HasStickingRegion
                    ? SquatStickingClassification.RECOVERABLE_STICKING
                    : SquatStickingClassification.NO_RESOLVABLE_STICKING;
        }

        private static bool CheckFinite(SquatTrace trace, out string field)
        {
            for (int index = 0; index < trace.Count; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (sample.Bar.IsAvailable &&
                    (!SquatTelemetryValue.IsFinite(sample.Bar.PositionWorldMeters) ||
                     !SquatTelemetryValue.IsFinite(sample.Bar.LinearVelocityWorldMetersPerSecond)))
                {
                    field = "bar@" + sample.SimulationTick;
                    return false;
                }
                if (sample.Support.SystemComAvailability == SquatTelemetryAvailability.AVAILABLE &&
                    (!SquatTelemetryValue.IsFinite(sample.Support.SystemComWorldMeters) ||
                     !SquatTelemetryValue.IsFinite(sample.Support.SystemComVelocityWorldMetersPerSecond)))
                {
                    field = "com@" + sample.SimulationTick;
                    return false;
                }
                if (sample.DriveAvailability == SquatTelemetryAvailability.AVAILABLE && !IsFinite(sample.MaximumModeledDemand))
                {
                    field = "demand@" + sample.SimulationTick;
                    return false;
                }
                if (sample.Depth.Availability == SquatTelemetryAvailability.AVAILABLE && !IsFinite(sample.Depth.WorstSideDepthM))
                {
                    field = "depth@" + sample.SimulationTick;
                    return false;
                }
                if (sample.PelvisAvailability == SquatTelemetryAvailability.AVAILABLE &&
                    !SquatTelemetryValue.IsFinite(sample.PelvisPositionWorldMeters))
                {
                    field = "pelvis@" + sample.SimulationTick;
                    return false;
                }
            }

            field = string.Empty;
            return true;
        }

        private static bool TrackForce(ref float first, SquatJointObservation left, SquatJointObservation right)
        {
            if (left.DriveAvailability != SquatTelemetryAvailability.AVAILABLE ||
                right.DriveAvailability != SquatTelemetryAvailability.AVAILABLE)
                return true;
            float value = Math.Max(left.MaximumForceNewtonMeters, right.MaximumForceNewtonMeters);
            if (float.IsNaN(first))
            {
                first = value;
                return true;
            }
            return Math.Abs(value - first) <= 1e-3f * Math.Max(1f, Math.Abs(first));
        }

        private static float MaxDemand(float current, SquatJointObservation left, SquatJointObservation right)
        {
            if (left.DriveAvailability == SquatTelemetryAvailability.AVAILABLE && IsFinite(left.ModeledDemand))
                current = Math.Max(current, left.ModeledDemand);
            if (right.DriveAvailability == SquatTelemetryAvailability.AVAILABLE && IsFinite(right.ModeledDemand))
                current = Math.Max(current, right.ModeledDemand);
            return current;
        }

        private static float MaxError(SquatJointObservation left, SquatJointObservation right)
        {
            float value = float.NegativeInfinity;
            if (left.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                value = Math.Max(value, Math.Abs(left.ActualReferenceErrorRadians));
            if (right.JointAvailability == SquatTelemetryAvailability.AVAILABLE)
                value = Math.Max(value, Math.Abs(right.ActualReferenceErrorRadians));
            return value;
        }

        private static float FiniteOrNaN(float value) => IsFinite(value) ? value : float.NaN;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
