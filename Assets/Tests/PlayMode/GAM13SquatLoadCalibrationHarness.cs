using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// One completed GAM-13 calibration attempt: the immutable attempt record
    /// plus offline metrics derived from its frozen trace.
    /// </summary>
    public sealed class GAM13AttemptResult
    {
        public string Sweep;
        public string Candidate;
        public float LoadKg;
        public int RunIndex;
        public float AthleteMassKg;
        public float BarMassKg;
        public ulong SquatCommandTick;
        public ulong DriveIntentTick;
        public int HarnessTicks;
        public SquatAttemptRecord Record;
        public SquatLoadResponseMetrics Metrics;
        public string CapacityModelVersion;
        public string FailureCalibrationVersion;

        public bool PhysicalSuccess =>
            Record != null &&
            Record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT &&
            Record.PhysicalFailureOutcome == SquatFailureResultKind.NO_PHYSICAL_FAILURE &&
            Metrics.LegalPhysicalDepth;

        public bool PhysicalFailure =>
            Record != null && Record.PhysicalFailureOutcome == SquatFailureResultKind.PHYSICAL_FAILURE;
    }

    /// <summary>
    /// Reusable deterministic GAM-13 load-sweep harness. Every attempt uses a
    /// fresh LoadSceneMode.Single of the production squat scene, the canonical
    /// GAM-12 attempt lifecycle, the same 100 Hz render-frame clock and the
    /// same intent schedule. Only the bar load changes between probes.
    ///
    /// Canonical intent: nothing is held until the reference controller leaves
    /// BOTTOM, then Drive is held at 1.0 for the rest of the attempt. The
    /// reference clock is open-loop and load-independent, so the Drive tick is
    /// the same offset after Squat for every load; the harness records it so a
    /// test can prove that. Drive does not change the autocycled reference or
    /// capacity; it is recorded intent that lets P3 evaluate a reversal attempt.
    /// </summary>
    public static class GAM13SquatLoadCalibrationHarness
    {
        public const string QualificationScene = "SquatPhysicalPrototype";
        public const int MaximumHarnessTicks = 2200;

        /// <summary>
        /// The production capacity model used by the resumed GAM-13 search.
        /// </summary>
        public const string CurrentCapacityModel = SquatPhysicalAdapter.CapacityCalibrationVersion;
        private const int TicksPerYield = 50;

        public static string MeasurementDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-13"));

        /// <summary>
        /// Runs one attempt on a freshly loaded scene. <paramref name="configure"/>
        /// runs after the scene is initialised and before the load is applied;
        /// it must be identical for every load of a sweep.
        /// </summary>
        public static IEnumerator RunAttempt(
            string sweep,
            string candidate,
            float loadKg,
            int runIndex,
            Action<SquatPhysicalPrototypeController> configure,
            List<GAM13AttemptResult> results)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            SquatPhysicalPrototypeController controller =
                UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(bootstrap.Runtime, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);

            controller.enabled = false;
            bootstrap.enabled = false;
            configure?.Invoke(controller);
            controller.SetLoad(loadKg);
            controller.BeginAttempt();

            FoundationRuntime runtime = bootstrap.Runtime;
            SquatPhysicalAdapter adapter = controller.Adapter;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            ulong squatCommandTick = SquatAttemptEventTicks.NotAvailable;
            ulong driveTick = SquatAttemptEventTicks.NotAvailable;
            int ticks = 0;
            while (controller.AttemptRecord == null && ticks < MaximumHarnessTicks)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                controller.LeftFootContact?.PhysicsTickUpdate(dt);
                controller.RightFootContact?.PhysicsTickUpdate(dt);
                ticks++;

                if (squatCommandTick == SquatAttemptEventTicks.NotAvailable &&
                    controller.AttemptLifecycle.State != SquatAttemptLifecycleState.START_WINDOW &&
                    controller.AttemptLifecycle.State != SquatAttemptLifecycleState.IDLE)
                    squatCommandTick = runtime.CurrentTime.Tick;

                if (driveTick == SquatAttemptEventTicks.NotAvailable && IsAscentReferenceState(adapter.State))
                {
                    runtime.InputBuffer.SetContinuous(
                        IntentAction.Drive,
                        1f,
                        runtime.CurrentTime.SimulationTimeSeconds + 0.25d * SimulationConstants.FixedDeltaTimeSeconds);
                    driveTick = runtime.CurrentTime.Tick + 1ul;
                }

                if (ticks % TicksPerYield == 0)
                    yield return null;
            }

            SquatAttemptRecord record = controller.AttemptRecord;
            Assert.That(
                record,
                Is.Not.Null,
                $"GAM-13 {sweep}/{candidate} {loadKg:F1} kg run {runIndex} did not finalize within {MaximumHarnessTicks} ticks " +
                $"(lifecycle={controller.AttemptLifecycle.State}, adapter={adapter.State}, sq={adapter.Sq:F3}).");

            ulong lockoutTick = record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT
                ? record.TerminalTick
                : SquatLoadResponseMetrics.NotAvailableTick;
            SquatLoadResponseMetrics metrics = SquatLoadResponseAnalyzer.Analyze(record.Trace, lockoutTick);

            GAM13AttemptResult result = new GAM13AttemptResult
            {
                Sweep = sweep,
                Candidate = candidate,
                LoadKg = loadKg,
                RunIndex = runIndex,
                AthleteMassKg = FindAthleteMass(),
                BarMassKg = controller.Saddle != null && controller.Saddle.Barbell != null
                    ? controller.Saddle.Barbell.LoadedMassKg
                    : float.NaN,
                SquatCommandTick = record.EventTicks.SquatCommandTick,
                DriveIntentTick = driveTick,
                HarnessTicks = ticks,
                Record = record,
                Metrics = metrics,
                CapacityModelVersion = CurrentCapacityModel,
                FailureCalibrationVersion = record.FailureCalibrationVersion
            };
            results.Add(result);
            Debug.Log("GAM13_ATTEMPT " + SummaryLine(result));

            if (!metrics.AllEvidenceFinite)
                Assert.Fail($"GAM-13 non-finite calibration evidence at {metrics.FirstNonFiniteField} " +
                    $"({loadKg:F1} kg run {runIndex}); classify the observation-layer blocker instead of sanitizing it.");

            // The next LoadSceneMode.Single destroys this scene; FoundationBootstrap
            // shuts its runtime down in OnDestroy, exactly as the GAM-12 repeats do.
            yield return null;
        }

        private static bool IsAscentReferenceState(SquatState state) =>
            state == SquatState.REVERSAL ||
            state == SquatState.ASCENT ||
            state == SquatState.STICKING ||
            state == SquatState.LOCKOUT ||
            state == SquatState.RACK_COMMAND ||
            state == SquatState.RERACK;

        private static float FindAthleteMass()
        {
            Athlete.PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<Athlete.PhysicalAthleteRig>();
            return rig == null ? float.NaN : rig.TotalMassKg;
        }

        public static string SummaryLine(GAM13AttemptResult result)
        {
            SquatAttemptRecord record = result.Record;
            SquatLoadResponseMetrics m = result.Metrics;
            return string.Format(
                CultureInfo.InvariantCulture,
                "sweep={0} candidate={1} load={2:F1} run={3} terminal={4}@{5} P3={6}/{7} primary={8} onset={9} latch={10} P2={11}/{12} depthMin={13:F4} legal={14} bottom={15} ascent={16} lockout={17} concSec={18:F2} meanV={19:F4} peakV={20:F4} minMidV={21:F4} maxDemand={22:F3} meanDemand={23:F3} sat={24:F3} knee={25:F1}Nm hip={26:F1}Nm sticking={27} vmax1={28:F4} vmin={29:F4} vmax2={30:F4} trunkMax={31:F3} reversal={32:F4} saddleLoss={33} traceCount={34} driveTick={35} squatTick={36}",
                result.Sweep, result.Candidate, result.LoadKg, result.RunIndex,
                record.TerminalReason, record.TerminalTick,
                record.FailureResult.EvidenceStatus, record.PhysicalFailureOutcome, record.FailureResult.PrimaryFailureKind,
                Tick(record.EventTicks.FailureOnsetTick), Tick(record.EventTicks.FailureLatchTick),
                record.Judgment.EvidenceStatus, record.RuleOutcome,
                m.MinimumWorstSideDepthM, m.LegalPhysicalDepth,
                Tick(m.BottomTick), Tick(m.AscentEstablishmentTick), Tick(m.LockoutTick),
                m.ConcentricDurationSeconds, m.MeanConcentricVelocityMps, m.PeakConcentricVelocityMps, m.MinimumMidAscentVelocityMps,
                m.MaximumModeledDemand, m.MeanAscentModeledDemand, m.AscentSaturationFraction,
                m.KneeMaximumForceNm, m.HipMaximumForceNm,
                m.Sticking, m.StickingVmax1Mps, m.StickingVminMps, m.StickingVmax2Mps,
                m.TrunkPitchMaxRad, m.MaximumDownwardReversalAfterAscentM, m.SaddleEverDetachedOrBroken,
                record.TraceSampleCount, Tick(result.DriveIntentTick), Tick(result.SquatCommandTick));
        }

        private static string Tick(ulong tick) =>
            tick == SquatAttemptEventTicks.NotAvailable ? "NA" : tick.ToString(CultureInfo.InvariantCulture);

        public static readonly string[] CsvColumns =
        {
            "sweep", "candidate", "capacity_model_version", "failure_calibration_version", "load_kg", "run",
            "athlete_mass_kg", "bar_mass_kg", "trace_count", "terminal_reason", "terminal_tick",
            "physical_success", "p3_evidence", "p3_outcome", "p3_primary", "p3_secondary", "failure_onset_tick", "failure_latch_tick",
            "p3_evidence_channels", "p2_evidence", "p2_outcome", "p2_violations",
            "squat_command_tick", "drive_intent_tick",
            "min_worst_side_depth_m", "legal_physical_depth",
            "descent_onset_tick", "bottom_tick", "ascent_establishment_tick", "completion_region_tick", "lockout_tick",
            "concentric_duration_s", "bottom_to_completion_region_s", "concentric_displacement_m",
            "mean_concentric_velocity_mps", "time_avg_concentric_velocity_mps", "peak_concentric_velocity_mps",
            "min_mid_ascent_velocity_mps", "max_downward_reversal_after_ascent_m", "max_bar_height_gain_m",
            "max_modeled_demand", "mean_ascent_modeled_demand", "ascent_near_saturation_fraction", "ascent_saturation_fraction",
            "peak_knee_demand", "peak_hip_demand", "peak_ankle_demand", "peak_trunk_demand",
            "peak_knee_tracking_error_rad", "peak_hip_tracking_error_rad", "peak_ankle_tracking_error_rad", "peak_trunk_tracking_error_rad",
            "mean_ascent_knee_tracking_error_rad", "mean_ascent_hip_tracking_error_rad",
            "knee_max_force_nm", "hip_max_force_nm", "ankle_max_force_nm", "trunk_max_force_nm", "knee_capacity_scale",
            "max_force_constant",
            "trunk_pitch_min_rad", "trunk_pitch_max_rad", "min_front_support_margin_m", "min_rear_support_margin_m",
            "com_z_min_m", "com_z_max_m", "max_saddle_separation_m", "saddle_detached_or_broken", "support_ever_lost",
            "sticking", "sticking_detector_status", "sticking_vmax1_mps", "sticking_vmin_mps", "sticking_vmax2_mps",
            "sticking_vmax1_tick", "sticking_vmin_tick", "sticking_vmax2_tick", "sticking_velocity_reduction_fraction",
            "sticking_interval_s", "noise_velocity_threshold_mps", "all_evidence_finite"
        };

        public static string CsvRow(GAM13AttemptResult r)
        {
            SquatAttemptRecord record = r.Record;
            SquatLoadResponseMetrics m = r.Metrics;
            SquatFailureRecord failure = record.FailureResult.FailureRecord;
            object[] values =
            {
                r.Sweep, r.Candidate, r.CapacityModelVersion, r.FailureCalibrationVersion, r.LoadKg, r.RunIndex,
                r.AthleteMassKg, r.BarMassKg, record.TraceSampleCount, record.TerminalReason, Tick(record.TerminalTick),
                r.PhysicalSuccess, record.FailureResult.EvidenceStatus, record.PhysicalFailureOutcome,
                record.FailureResult.PrimaryFailureKind, Secondary(failure),
                Tick(record.EventTicks.FailureOnsetTick), Tick(record.EventTicks.FailureLatchTick),
                failure == null ? "NONE" : failure.Primary.EvidenceChannels.ToString().Replace(", ", "|"),
                record.Judgment.EvidenceStatus, record.RuleOutcome, Violations(record.Judgment),
                Tick(r.SquatCommandTick), Tick(r.DriveIntentTick),
                m.MinimumWorstSideDepthM, m.LegalPhysicalDepth,
                Tick(m.DescentOnsetTick), Tick(m.BottomTick), Tick(m.AscentEstablishmentTick),
                Tick(m.CompletionRegionEntryTick), Tick(m.LockoutTick),
                m.ConcentricDurationSeconds, m.AscentToCompletionRegionSeconds, m.ConcentricDisplacementM,
                m.MeanConcentricVelocityMps, m.TimeAveragedConcentricVelocityMps, m.PeakConcentricVelocityMps,
                m.MinimumMidAscentVelocityMps, m.MaximumDownwardReversalAfterAscentM, m.MaximumBarHeightGainM,
                m.MaximumModeledDemand, m.MeanAscentModeledDemand, m.AscentNearSaturationFraction, m.AscentSaturationFraction,
                m.PeakKneeDemand, m.PeakHipDemand, m.PeakAnkleDemand, m.PeakTrunkDemand,
                m.PeakKneeTrackingErrorRad, m.PeakHipTrackingErrorRad, m.PeakAnkleTrackingErrorRad, m.PeakTrunkTrackingErrorRad,
                m.MeanAscentKneeTrackingErrorRad, m.MeanAscentHipTrackingErrorRad,
                m.KneeMaximumForceNm, m.HipMaximumForceNm, m.AnkleMaximumForceNm, m.TrunkMaximumForceNm, m.KneeCapacityScale,
                m.MaximumForceConstantDuringAttempt,
                m.TrunkPitchMinRad, m.TrunkPitchMaxRad, m.MinimumFrontSupportMarginM, m.MinimumRearSupportMarginM,
                m.ComZMinM, m.ComZMaxM, m.MaximumSaddleSeparationM, m.SaddleEverDetachedOrBroken, m.SupportEverLost,
                m.Sticking, m.StickingDetectorStatus, m.StickingVmax1Mps, m.StickingVminMps, m.StickingVmax2Mps,
                Tick(m.StickingVmax1Tick), Tick(m.StickingVminTick), Tick(m.StickingVmax2Tick),
                m.StickingVelocityReductionFraction, m.StickingIntervalSeconds, m.NoiseVelocityThresholdMps,
                m.AllEvidenceFinite
            };

            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0)
                    builder.Append(',');
                builder.Append(Format(values[index]));
            }
            return builder.ToString();
        }

        public static void WriteCsv(string fileName, IReadOnlyList<GAM13AttemptResult> results)
        {
            Directory.CreateDirectory(MeasurementDirectory);
            StringBuilder builder = new StringBuilder();
            builder.AppendLine(string.Join(",", CsvColumns));
            for (int index = 0; index < results.Count; index++)
                builder.AppendLine(CsvRow(results[index]));
            File.WriteAllText(Path.Combine(MeasurementDirectory, fileName), builder.ToString());
        }

        private static string Secondary(SquatFailureRecord failure)
        {
            if (failure == null || failure.SecondaryCount == 0)
                return "NONE";
            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < failure.SecondaryEvents.Count; index++)
            {
                if (index > 0)
                    builder.Append('|');
                builder.Append(failure.SecondaryEvents[index].Kind);
                if (failure.SecondaryEvents[index].Detail != SquatFailureDetailKind.NONE)
                    builder.Append(':').Append(failure.SecondaryEvents[index].Detail);
            }
            return builder.ToString();
        }

        private static string Violations(SquatAttemptJudgment judgment)
        {
            if (judgment.Violations.Count == 0)
                return "NONE";
            StringBuilder builder = new StringBuilder();
            for (int index = 0; index < judgment.Violations.Count; index++)
            {
                if (index > 0)
                    builder.Append('|');
                builder.Append(judgment.Violations[index].Kind);
            }
            return builder.ToString();
        }

        private static string Format(object value)
        {
            switch (value)
            {
                case float f:
                    return float.IsNaN(f) ? "NA" : f.ToString("R", CultureInfo.InvariantCulture);
                case double d:
                    return double.IsNaN(d) ? "NA" : d.ToString("R", CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "true" : "false";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
