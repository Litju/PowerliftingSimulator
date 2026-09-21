using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM45StartWindowLifecycleTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int MaximumProbeTicks = 2200;
        private const int TicksPerYield = 50;

        private static readonly PropertyInfo[] DiagnosticProperties =
            typeof(SquatStartPredicateDiagnostic)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(property => property.Name)
                .ToArray();

        [UnityTest]
        [Explicit("GAM-45 fresh-process start-window probe; set GAM45_PROBE to P0, P1, P2, or P2_NO_LOWER_CHAIN.")]
        public IEnumerator GAM45_START_WINDOW_PROBE()
        {
            ProbeArm arm = ParseArm(Environment.GetEnvironmentVariable("GAM45_PROBE"));
            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            try
            {
                ConfigureStaticArm(arm);
                yield return RunProbe(arm);
            }
            finally
            {
                RestoreProfiles(originalProfiles);
                SquatBarSaddle.ExperimentalOverride = null;
            }
        }

        private static IEnumerator RunProbe(ProbeArm arm)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-45 qualification scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            SquatPhysicalPrototypeController controller =
                UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);

            controller.enabled = false;
            bootstrap.enabled = false;
            ConfigureSceneArm(controller, arm);
            controller.SetLoad(25f);
            controller.BeginAttempt();

            FoundationRuntime runtime = bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            bool enteredStartWindow = false;
            int ticks = 0;
            while (controller.AttemptRecord == null && ticks < MaximumProbeTicks)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                controller.LeftFootContact?.PhysicsTickUpdate(dt);
                controller.RightFootContact?.PhysicsTickUpdate(dt);
                enteredStartWindow |= controller.ObservationCollector.IsRecording;
                ticks++;
                if (ticks % TicksPerYield == 0)
                    yield return null;
            }

            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics =
                controller.AttemptOrchestrator.StartWindowDiagnostics;
            Assert.That(diagnostics.Count, Is.GreaterThan(0), "The production start predicate emitted no diagnostics.");
            WriteEvidence(arm, diagnostics, enteredStartWindow, ticks, controller);
            Debug.Log("GAM45_START_PROBE " + SummaryLine(arm, diagnostics, enteredStartWindow, ticks, controller));
            yield return null;
        }

        private static void ConfigureStaticArm(ProbeArm arm)
        {
            SquatBarSaddle.ExperimentalOverride = null;
            if (arm == ProbeArm.P0)
                return;

            ApplyImpedanceFactor(5f);
            SquatBarSaddle.ExperimentalOverride = new SquatBarSaddle.ExperimentalConfiguration(50000f, 3000f, 0.10f);
        }

        private static void ConfigureSceneArm(SquatPhysicalPrototypeController controller, ProbeArm arm)
        {
            switch (arm)
            {
                case ProbeArm.P0:
                    controller.Adapter.Preload.ExperimentalBiasOverride = null;
                    break;
                case ProbeArm.P1:
                    controller.Adapter.Preload.ExperimentalBiasOverride = GAM43FeedForward.ForVariant("F1");
                    break;
                case ProbeArm.P2:
                    controller.Adapter.Preload.ExperimentalBiasOverride = GAM43FeedForward.ForVariant("F2");
                    break;
                case ProbeArm.P2NoLowerChain:
                    controller.Adapter.Preload.ExperimentalBiasOverride = F2WithoutLowerChainStanding;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(arm));
            }
        }

        private static float F2WithoutLowerChainStanding(SquatJointFamily family, float phase, float loadKg)
        {
            if (family == SquatJointFamily.Ankle ||
                family == SquatJointFamily.Knee ||
                family == SquatJointFamily.Hip)
                return 0f;
            return GAM43FeedForward.ForVariant("F2")(family, phase, loadKg);
        }

        private static void WriteEvidence(
            ProbeArm arm,
            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics,
            bool enteredStartWindow,
            int ticks,
            SquatPhysicalPrototypeController controller)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-45"));
            Directory.CreateDirectory(directory);
            string name = ArmName(arm);
            File.WriteAllText(Path.Combine(directory, "start-window-" + name + ".csv"), DiagnosticCsv(diagnostics));
            File.WriteAllText(
                Path.Combine(directory, "start-window-" + name + "-summary.csv"),
                SummaryCsv(arm, diagnostics, enteredStartWindow, ticks, controller));
        }

        private static string DiagnosticCsv(IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics)
        {
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", DiagnosticProperties.Select(property => property.Name)));
            for (int row = 0; row < diagnostics.Count; row++)
            {
                if (row > 0)
                    builder.AppendLine();
                builder.Append(string.Join(",", DiagnosticProperties.Select(property => Format(property.GetValue(diagnostics[row])))));
            }
            builder.AppendLine();
            return builder.ToString();
        }

        private static string SummaryCsv(
            ProbeArm arm,
            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics,
            bool enteredStartWindow,
            int ticks,
            SquatPhysicalPrototypeController controller)
        {
            SquatStartPredicateDiagnostic first = diagnostics[0];
            SquatStartPredicateDiagnostic last = diagnostics[diagnostics.Count - 1];
            int longestRun = diagnostics.Max(diagnostic => diagnostic.ConsecutiveValidRunLength);
            bool qualified = longestRun >= first.RequiredRunLength;
            string[] observedFailures = FailureLabels(diagnostics).ToArray();
            SquatStartPredicateDiagnostic blockingDiagnostic = BlockingDiagnostic(diagnostics, longestRun);
            string classification = qualified ? "NONE" : Classify(blockingDiagnostic);
            var builder = new StringBuilder();
            builder.AppendLine("field,value");
            Append(builder, "arm", ArmName(arm));
            Append(builder, "load_kg", 25f);
            Append(builder, "probe_ticks", ticks);
            Append(builder, "diagnostic_ticks", diagnostics.Count);
            Append(builder, "required_run_length", first.RequiredRunLength);
            Append(builder, "qualified_three_tick_run", qualified);
            Append(builder, "longest_valid_run", longestRun);
            Append(builder, "entered_start_window", enteredStartWindow);
            Append(builder, "lifecycle_state_at_end", controller.AttemptLifecycle.State);
            Append(builder, "adapter_state_at_end", controller.Adapter.State);
            Append(builder, "classification", classification);
            Append(builder, "observed_failure_predicates", string.Join("|", observedFailures));
            Append(builder, "blocking_failure_predicates", string.Join("|", FailureLabels(blockingDiagnostic)));
            Append(builder, "terminal_failure_predicates", string.Join("|", FailureLabels(last)));
            Append(builder, "squat_command_tick", controller.AttemptRecord == null
                ? SquatAttemptEventTicks.NotAvailable
                : controller.AttemptRecord.EventTicks.SquatCommandTick);
            Append(builder, "terminal_reason", controller.AttemptRecord == null
                ? "NOT_FINALIZED"
                : controller.AttemptRecord.TerminalReason);
            AppendRange(builder, "bar_linear_speed_magnitude_mps", diagnostics,
                diagnostic => diagnostic.BarLinearSpeedMagnitudeMps);
            AppendRange(builder, "bar_angular_speed_magnitude_rad_s", diagnostics,
                diagnostic => diagnostic.BarAngularSpeedMagnitudeRadS);
            AppendRange(builder, "bar_vertical_speed_mps", diagnostics,
                diagnostic => diagnostic.BarVerticalSpeedMps);
            AppendRange(builder, "bar_angular_velocity_x_rad_s", diagnostics,
                diagnostic => diagnostic.BarAngularVelocityBarXRadS);
            AppendRange(builder, "bar_angular_velocity_y_rad_s", diagnostics,
                diagnostic => diagnostic.BarAngularVelocityBarYRadS);
            AppendRange(builder, "bar_angular_velocity_z_rad_s", diagnostics,
                diagnostic => diagnostic.BarAngularVelocityBarZRadS);
            AppendRange(builder, "raw_ankle_demand_rad", diagnostics,
                diagnostic => diagnostic.RawAnkleDemandRad);
            AppendRange(builder, "guarded_ankle_demand_rad", diagnostics,
                diagnostic => diagnostic.GuardedAnkleDemandRad);
            AppendRange(builder, "applied_ankle_demand_rad", diagnostics,
                diagnostic => diagnostic.AppliedAnkleDemandRad);
            AppendRange(builder, "hip_strategy_blend", diagnostics,
                diagnostic => diagnostic.HipStrategyBlend);
            AppendRange(builder, "com_ap_m", diagnostics, diagnostic => diagnostic.ComApM);
            AppendRange(builder, "com_ml_m", diagnostics, diagnostic => diagnostic.ComMlM);
            AppendRange(builder, "cop_measured_ap_m", diagnostics, diagnostic => diagnostic.CopMeasuredApM);
            AppendRange(builder, "cop_measured_ml_m", diagnostics, diagnostic => diagnostic.CopMeasuredMlM);
            AppendRange(builder, "capture_ap_m", diagnostics, diagnostic => diagnostic.CaptureApM);
            AppendRange(builder, "capture_ml_m", diagnostics, diagnostic => diagnostic.CaptureMlM);
            AppendRange(builder, "com_speed_mps", diagnostics, diagnostic => diagnostic.ComSpeedMps);
            AppendRange(builder, "capture_margin_2d_m", diagnostics, diagnostic => diagnostic.CaptureMargin2DM);
            Append(builder, "bar_vertical_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.BarVerticalSpeedMps));
            Append(builder, "bar_angular_x_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.BarAngularVelocityBarXRadS));
            Append(builder, "bar_angular_y_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.BarAngularVelocityBarYRadS));
            Append(builder, "bar_angular_z_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.BarAngularVelocityBarZRadS));
            Append(builder, "com_ap_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.ComApM));
            Append(builder, "com_ml_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.ComMlM));
            Append(builder, "cop_ap_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.CopMeasuredApM));
            Append(builder, "cop_ml_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.CopMeasuredMlM));
            Append(builder, "capture_ap_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.CaptureApM));
            Append(builder, "capture_ml_zero_crossings", ZeroCrossings(diagnostics, diagnostic => diagnostic.CaptureMlM));
            foreach (PropertyInfo property in DiagnosticProperties.Where(IsMarginProperty))
            {
                float firstValue = FirstFinite(diagnostics, property);
                float lastValue = LastFinite(diagnostics, property);
                float minimum = diagnostics
                    .Select(diagnostic => (float)property.GetValue(diagnostic))
                    .Where(float.IsFinite)
                    .DefaultIfEmpty(float.NaN)
                    .Min();
                float maximum = diagnostics
                    .Select(diagnostic => (float)property.GetValue(diagnostic))
                    .Where(float.IsFinite)
                    .DefaultIfEmpty(float.NaN)
                    .Max();
                Append(builder, property.Name + "_first", firstValue);
                Append(builder, property.Name + "_last", lastValue);
                Append(builder, property.Name + "_min", minimum);
                Append(builder, property.Name + "_max", maximum);
            }
            return builder.ToString();
        }

        private static void AppendRange(
            StringBuilder builder,
            string name,
            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics,
            Func<SquatStartPredicateDiagnostic, float> value)
        {
            float[] finite = diagnostics.Select(value).Where(float.IsFinite).ToArray();
            Append(builder, name + "_min", finite.Length == 0 ? float.NaN : finite.Min());
            Append(builder, name + "_max", finite.Length == 0 ? float.NaN : finite.Max());
        }

        private static int ZeroCrossings(
            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics,
            Func<SquatStartPredicateDiagnostic, float> value)
        {
            float previous = float.NaN;
            int crossings = 0;
            for (int index = 0; index < diagnostics.Count; index++)
            {
                float current = value(diagnostics[index]);
                if (!float.IsFinite(current))
                    continue;
                if (float.IsFinite(previous) &&
                    ((previous < 0f && current >= 0f) || (previous >= 0f && current < 0f)))
                    crossings++;
                previous = current;
            }
            return crossings;
        }

        private static IEnumerable<string> FailureLabels(IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics)
        {
            var labels = new HashSet<string>();
            for (int index = 0; index < diagnostics.Count; index++)
                foreach (string label in FailureLabels(diagnostics[index]))
                    labels.Add(label);
            return labels.OrderBy(label => label);
        }

        private static IEnumerable<string> FailureLabels(SquatStartPredicateDiagnostic diagnostic)
        {
            if (!diagnostic.BarAvailable)
                yield return "bar_availability";
            else
            {
                if (!diagnostic.BarLinearSpeedPass) yield return "bar_linear_speed";
                if (!diagnostic.BarVerticalSpeedPass) yield return "bar_vertical_speed";
                if (!diagnostic.BarAngularSpeedPass) yield return "bar_angular_speed";
            }

            if (!diagnostic.SupportAvailable) yield return "support_availability";
            if (!diagnostic.SupportPresent) yield return "support_present";
            if (!diagnostic.LeftFootAvailable) yield return "left_foot_availability";
            if (!diagnostic.RightFootAvailable) yield return "right_foot_availability";
            foreach (string label in JointFailure(diagnostic.LeftKneeActualAngleRad, diagnostic.LeftKneePass, "left_knee"))
                yield return label;
            foreach (string label in JointFailure(diagnostic.RightKneeActualAngleRad, diagnostic.RightKneePass, "right_knee"))
                yield return label;
            foreach (string label in JointFailure(diagnostic.LeftHipActualAngleRad, diagnostic.LeftHipPass, "left_hip"))
                yield return label;
            foreach (string label in JointFailure(diagnostic.RightHipActualAngleRad, diagnostic.RightHipPass, "right_hip"))
                yield return label;
            foreach (string label in JointFailure(diagnostic.AbdomenActualAngleRad, diagnostic.AbdomenPass, "abdomen"))
                yield return label;
            foreach (string label in JointFailure(diagnostic.ThoraxActualAngleRad, diagnostic.ThoraxPass, "thorax"))
                yield return label;
        }

        private static IEnumerable<string> JointFailure(float actual, bool pass, string name)
        {
            if (!pass)
                yield return float.IsNaN(actual) ? name + "_availability" : name + "_angle";
        }

        private static SquatStartPredicateDiagnostic BlockingDiagnostic(
            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics,
            int longestRun)
        {
            for (int index = 0; index + 1 < diagnostics.Count; index++)
            {
                if (diagnostics[index].ConsecutiveValidRunLength == longestRun &&
                    diagnostics[index].OverallStartCandidate &&
                    !diagnostics[index + 1].OverallStartCandidate)
                    return diagnostics[index + 1];
            }
            return diagnostics[diagnostics.Count - 1];
        }

        private static string Classify(SquatStartPredicateDiagnostic diagnostic)
        {
            var groups = new HashSet<string>();
            if (!diagnostic.BarAvailable || !diagnostic.BarLinearSpeedPass ||
                !diagnostic.BarVerticalSpeedPass || !diagnostic.BarAngularSpeedPass)
                groups.Add(diagnostic.BarAvailable ? "START_BAR_MOTION" : "START_OBSERVATION_AVAILABILITY");
            if (!diagnostic.SupportAvailable || !diagnostic.LeftFootAvailable || !diagnostic.RightFootAvailable)
                groups.Add("START_OBSERVATION_AVAILABILITY");
            if (!diagnostic.SupportPresent)
                groups.Add("START_SUPPORT");
            if (!diagnostic.LeftKneePass || !diagnostic.RightKneePass)
                groups.Add("START_POSTURE_KNEE");
            if (!diagnostic.LeftHipPass || !diagnostic.RightHipPass)
                groups.Add("START_POSTURE_HIP");
            if (!diagnostic.AbdomenPass || !diagnostic.ThoraxPass)
                groups.Add("START_POSTURE_TRUNK");
            if (groups.Count == 0)
                return "NONE";
            if (groups.Count > 1)
                return "START_MULTI_FACTOR";
            return groups.Single();
        }

        private static bool IsMarginProperty(PropertyInfo property) =>
            property.PropertyType == typeof(float) && property.Name.Contains("Margin");

        private static float FirstFinite(IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics, PropertyInfo property)
        {
            for (int index = 0; index < diagnostics.Count; index++)
            {
                float value = (float)property.GetValue(diagnostics[index]);
                if (float.IsFinite(value))
                    return value;
            }
            return float.NaN;
        }

        private static float LastFinite(IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics, PropertyInfo property)
        {
            for (int index = diagnostics.Count - 1; index >= 0; index--)
            {
                float value = (float)property.GetValue(diagnostics[index]);
                if (float.IsFinite(value))
                    return value;
            }
            return float.NaN;
        }

        private static string SummaryLine(
            ProbeArm arm,
            IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics,
            bool enteredStartWindow,
            int ticks,
            SquatPhysicalPrototypeController controller)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "arm={0} ticks={1} diagnostics={2} qualified={3} longestRun={4} enteredStartWindow={5} classification={6} state={7} terminal={8}",
                ArmName(arm),
                ticks,
                diagnostics.Count,
                diagnostics.Max(diagnostic => diagnostic.ConsecutiveValidRunLength) >= diagnostics[0].RequiredRunLength,
                diagnostics.Max(diagnostic => diagnostic.ConsecutiveValidRunLength),
                enteredStartWindow,
                diagnostics.Max(diagnostic => diagnostic.ConsecutiveValidRunLength) >= diagnostics[0].RequiredRunLength
                    ? "NONE"
                    : Classify(BlockingDiagnostic(
                        diagnostics,
                        diagnostics.Max(diagnostic => diagnostic.ConsecutiveValidRunLength))),
                controller.AttemptLifecycle.State,
                controller.AttemptRecord == null ? "NOT_FINALIZED" : controller.AttemptRecord.TerminalReason.ToString());
        }

        private static void Append(StringBuilder builder, string key, object value)
        {
            builder.Append(key).Append(',').Append(Format(value)).AppendLine();
        }

        private static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return "NA";
                case float single:
                    return float.IsNaN(single) ? "NA" : single.ToString("R", CultureInfo.InvariantCulture);
                case double real:
                    return double.IsNaN(real) ? "NA" : real.ToString("R", CultureInfo.InvariantCulture);
                case bool boolean:
                    return boolean ? "true" : "false";
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        private static ProbeArm ParseArm(string text)
        {
            switch ((text ?? "P0").ToUpperInvariant())
            {
                case "P0": return ProbeArm.P0;
                case "P1": return ProbeArm.P1;
                case "P2": return ProbeArm.P2;
                case "P2_NO_LOWER_CHAIN": return ProbeArm.P2NoLowerChain;
                default: throw new ArgumentException("GAM45_PROBE must be P0, P1, P2, or P2_NO_LOWER_CHAIN.");
            }
        }

        private static string ArmName(ProbeArm arm) =>
            arm == ProbeArm.P2NoLowerChain ? "P2_NO_LOWER_CHAIN" : arm.ToString();

        private static JointFamilyProfile[] ProfileArray() =>
            (JointFamilyProfile[])typeof(PoweredJointController)
                .GetField("Profiles", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static JointFamilyProfile[] SnapshotProfiles() =>
            (JointFamilyProfile[])ProfileArray().Clone();

        private static void RestoreProfiles(JointFamilyProfile[] original)
        {
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
                live[index] = original[index];
        }

        private static void ApplyImpedanceFactor(float factor)
        {
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
            {
                JointFamilyProfile profile = live[index];
                if (profile.Id != "ankle" && profile.Id != "knee" && profile.Id != "hip" && profile.Id != "trunk")
                    continue;
                live[index] = new JointFamilyProfile(
                    profile.Id,
                    profile.Spring * factor,
                    profile.Damper * Mathf.Sqrt(factor),
                    profile.BaseCapacityNm,
                    profile.MaxTargetRateRadS);
            }
        }

        private enum ProbeArm
        {
            P0,
            P1,
            P2,
            P2NoLowerChain
        }
    }
}
