using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// H20 diagnostic qualification. This fixture observes the existing
    /// physical squat and never writes a production target, force, torque,
    /// velocity, or transform.
    /// </summary>
    public sealed class H20EventBasedBiomechanicalValidationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H20";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/H20";
        private const int TrialCount = 3;
        private const int WarmupTicks = 120;
        private const int StationaryWindowTicks = 60;
        private const int MaximumTrialTicks = 1200;
        private const int SensitivityIndexToleranceTicks = 3;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private PhysicalBarbell _bar;
        private SquatPhysicalPrototypeController _controller;
        private H20Trial _lastTrial;
        private readonly List<H20Trial> _trials = new List<H20Trial>(6);
        private readonly StringBuilder _trialSummary = new StringBuilder();
        private readonly StringBuilder _eventSummary = new StringBuilder();
        private readonly StringBuilder _candidateSummary = new StringBuilder();
        private readonly StringBuilder _sensitivitySummary = new StringBuilder();
        private readonly StringBuilder _repeatabilitySummary = new StringBuilder();

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }

            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _bar = null;
            _controller = null;
            _lastTrial = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator H20_EVENT_BASED_PHYSICAL_VALIDATION_0KG_AND_25KG()
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            Directory.CreateDirectory(Path.GetFullPath(EvidenceDirectory));
            _trials.Clear();
            _trialSummary.Clear();
            _eventSummary.Clear();
            _candidateSummary.Clear();
            _sensitivitySummary.Clear();
            _repeatabilitySummary.Clear();

            _trialSummary.AppendLine(
                "load_kg,repeat,bar_signal,status,sample_count,lockout_sample_index,lockout_time_s," +
                "stationary_count,stationary_median_abs_v_mps,velocity_sigma_mps,acceleration_sigma_mps2," +
                "sensitivity_classification,sequence,v0_index,vmax1_index,dmax1_index,vmin_index,vmax2_index," +
                "knee_error_peak_index,knee_error_peak_deg,knee_error_peak_location");
            _eventSummary.AppendLine(
                "load_kg,repeat,status,event,sample_index,time_from_squat_start_s,time_from_v0_s," +
                "bar_y_m,bar_displacement_from_v0_m,bar_vy_raw_mps,bar_vy_filtered_mps," +
                "bar_a_raw_mps2,bar_a_filtered_mps2,s_q,state,direction," +
                "knee_l_joint_space_deg,knee_r_joint_space_deg,hip_l_joint_space_deg,hip_r_joint_space_deg," +
                "ankle_l_joint_space_deg,ankle_r_joint_space_deg,knee_l_included_deg,knee_r_included_deg," +
                "hip_l_included_deg,hip_r_included_deg,ankle_l_included_deg,ankle_r_included_deg," +
                "abdomen_joint_space_deg,thorax_joint_space_deg,world_trunk_pitch_deg," +
                "knee_l_omega_deg_s,knee_r_omega_deg_s,hip_l_omega_deg_s,hip_r_omega_deg_s," +
                "ankle_l_omega_deg_s,ankle_r_omega_deg_s,abdomen_omega_deg_s,thorax_omega_deg_s," +
                "knee_l_ref_deg,knee_r_ref_deg,hip_l_ref_deg,hip_r_ref_deg,ankle_l_ref_deg,ankle_r_ref_deg," +
                "knee_l_error_deg,hip_l_error_deg,ankle_l_error_deg," +
                "com_x_m,com_y_m,com_z_m,com_vel_x_mps,com_vel_y_mps,com_vel_z_mps," +
                "cop_available,cop_z_m,cop_fraction_support_ap,support_ap_min_m,support_ap_max_m," +
                "support_ap_center_m,capture_ap_m,capture_margin_rear_m,capture_margin_front_m," +
                "support_contacts,left_foot_contact,right_foot_contact,left_foot_contact_count,right_foot_contact_count," +
                "left_slip_speed_mps,right_slip_speed_mps,left_slip_accumulated_m,right_slip_accumulated_m," +
                "normal_force_contact_estimate_n,saddle_separation_m,bar_rot_x_deg,bar_rot_y_deg,bar_rot_z_deg," +
                "bar_ap_relative_com_m,bar_ap_relative_support_m,failure_reason,legal_depth,lockout_reached");
            _candidateSummary.AppendLine(
                "load_kg,repeat,candidate_event,sample_index,time_from_squat_start_s,time_from_v0_s,bar_y_m,bar_vy_raw_mps," +
                "bar_vy_filtered_mps,bar_a_raw_mps2,bar_a_filtered_mps2,velocity_noise_sigma_mps,velocity_threshold_mps," +
                "acceleration_noise_sigma_mps2,acceleration_threshold_mps2,accepted_full_sequence,criterion");
            _sensitivitySummary.AppendLine(
                "load_kg,repeat,variant,cutoff_hz,noise_sigma_multiplier,status,v0_index,vmax1_index,dmax1_index,vmin_index,vmax2_index,classification_stable_vs_default");
            _repeatabilitySummary.AppendLine(
                "load_kg,trial_count,bar_signal,repeatability,status_1,status_2,status_3,vmax1_time_range_s,vmin_time_range_s,vmax2_time_range_s");

            foreach (float load in new[] { 0f, 25f })
            {
                for (int repeat = 1; repeat <= TrialCount; repeat++)
                {
                    yield return RunTrial(load, repeat);
                    Assert.That(_lastTrial, Is.Not.Null);
                    WriteTrialTrace(_lastTrial);
                    _trials.Add(_lastTrial);
                    AppendTrialSummary(_lastTrial);
                    AppendEventSummary(_lastTrial);
                    AppendCandidateSummary(_lastTrial);
                }

                EvaluateRepeatability(load);
            }

            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "h20-trial-summary.csv"), _trialSummary.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "h20-event-summary.csv"), _eventSummary.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "h20-event-candidates.csv"), _candidateSummary.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "h20-event-sensitivity.csv"), _sensitivitySummary.ToString());
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), "h20-repeatability.csv"), _repeatabilitySummary.ToString());

            foreach (float load in new[] { 0f, 25f })
            {
                H20Trial referenceTrial = FirstTrial(load);
                yield return CaptureVisualReplay(load, referenceTrial);
            }

            Debug.Log("[H20] Wrote event-based trial, event, sensitivity, repeatability, and visual evidence artifacts.");
            yield return null;
        }

        private IEnumerator RunTrial(float load, int repeat)
        {
            yield return LoadFreshScene();
            PrepareManualRuntime(load);

            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
            PhysicalFootContactDetector rightFoot = _controller.RightFootContact;
            bool hasBar = _bar != null && _bar.Body != null && _bar.Body.gameObject.activeInHierarchy &&
                _controller.Saddle != null && _controller.Saddle.IsAttached;

            var trial = new H20Trial(load, repeat, hasBar);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int tick = 0; tick < WarmupTicks; tick++)
            {
                AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                if (hasBar && tick >= WarmupTicks - StationaryWindowTicks)
                    trial.StationaryVelocities.Add(_bar.Body.linearVelocity.y);
            }

            adapter.StartSquat();
            ulong startTick = runtime.CurrentTime.Tick;
            for (int sampleIndex = 0; sampleIndex < MaximumTrialTicks; sampleIndex++)
            {
                AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                H20Sample sample = ReadSample(
                    trial,
                    sampleIndex,
                    (runtime.CurrentTime.Tick - startTick) * dt,
                    adapter,
                    leftFoot,
                    rightFoot);
                trial.Samples.Add(sample);

                if (adapter.LockoutReached)
                {
                    trial.LockoutSampleIndex = sampleIndex;
                    break;
                }
            }

            Assert.That(trial.Samples.Count, Is.GreaterThan(0));
            Assert.That(trial.LockoutSampleIndex, Is.GreaterThanOrEqualTo(0),
                $"H20 {load:F0} kg repeat {repeat} did not reach physical lockout.");
            if (load > 0f)
                Assert.That(trial.HasBarSignal, Is.True, "The 25 kg H20 trial must have an active physical bar signal.");
            else
                Assert.That(trial.HasBarSignal, Is.False, "The current 0 kg qualification is bodyweight-only and has no active bar body.");

            PopulateRawAcceleration(trial);
            if (trial.HasBarSignal)
            {
                List<SquatBarVelocitySample> detectorSamples = new List<SquatBarVelocitySample>(trial.Samples.Count);
                for (int index = 0; index < trial.Samples.Count; index++)
                {
                    H20Sample sample = trial.Samples[index];
                    detectorSamples.Add(new SquatBarVelocitySample(
                        sample.TimeFromSquatStartSeconds,
                        sample.BarPositionMeters,
                        sample.BarVelocityMetersPerSecond));
                }

                trial.Detection = SquatBarVelocityEventDetector.Detect(
                    detectorSamples,
                    trial.StationaryVelocities,
                    SquatBarVelocityDetectorOptions.Default);
                EvaluateSensitivity(trial, detectorSamples);
            }
            else
            {
                trial.SensitivityStatus = "NOT_APPLICABLE_NO_BAR_SIGNAL";
            }

            trial.KneeErrorPeakSampleIndex = FindKneeErrorPeak(trial);
            trial.KneeErrorPeakLocation = LocateKneeErrorPeak(trial);
            _lastTrial = trial;
            yield return null;
        }

        private void EvaluateSensitivity(H20Trial trial, IReadOnlyList<SquatBarVelocitySample> samples)
        {
            SquatBarVelocityDetectorOptions[] variants =
            {
                new SquatBarVelocityDetectorOptions(0.01f, 8f, 4f),
                new SquatBarVelocityDetectorOptions(0.01f, 10f, 3f),
                SquatBarVelocityDetectorOptions.Default,
                new SquatBarVelocityDetectorOptions(0.01f, 10f, 5f),
                new SquatBarVelocityDetectorOptions(0.01f, 12f, 4f)
            };

            trial.SensitivityStatus = "STABLE_CLASSIFICATION";
            for (int index = 0; index < variants.Length; index++)
            {
                SquatBarVelocityEventDetection result = SquatBarVelocityEventDetector.Detect(
                    samples,
                    trial.StationaryVelocities,
                    variants[index]);
                bool stable = SameEventIdentity(trial.Detection, result);
                if (!stable)
                    trial.SensitivityStatus = "UNRESOLVED";

                _sensitivitySummary.AppendLine(Csv(
                    F(trial.LoadKg),
                    F(trial.Repeat),
                    $"fc{F(variants[index].LowPassCutoffHz)}_sigma{F(variants[index].NoiseSigmaMultiplier)}",
                    F(variants[index].LowPassCutoffHz),
                    F(variants[index].NoiseSigmaMultiplier),
                    DetectionStatus(result),
                    F(EventIndex(result, SquatBarVelocityEventKind.V0)),
                    F(EventIndex(result, SquatBarVelocityEventKind.Vmax1)),
                    F(EventIndex(result, SquatBarVelocityEventKind.Dmax1)),
                    F(EventIndex(result, SquatBarVelocityEventKind.Vmin)),
                    F(EventIndex(result, SquatBarVelocityEventKind.Vmax2)),
                    stable ? "STABLE_CLASSIFICATION" : "UNRESOLVED"));
            }
        }

        private static bool SameEventIdentity(
            SquatBarVelocityEventDetection expected,
            SquatBarVelocityEventDetection candidate)
        {
            if (expected == null || candidate == null || expected.Status != candidate.Status)
                return false;
            if (!expected.HasStickingRegion)
                return true;
            if (expected.Events.Count != candidate.Events.Count)
                return false;
            for (int index = 0; index < expected.Events.Count; index++)
            {
                if (expected.Events[index].Kind != candidate.Events[index].Kind ||
                    Math.Abs(expected.Events[index].SampleIndex - candidate.Events[index].SampleIndex) > SensitivityIndexToleranceTicks)
                    return false;
            }

            return true;
        }

        private IEnumerator CaptureVisualReplay(float load, H20Trial referenceTrial)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                Debug.LogWarning($"[H20] Graphics device is Null; visual replay skipped for {load:F0} kg.");
                yield break;
            }

            yield return LoadFreshScene();
            PrepareManualRuntime(load);
            _rig.enabled = true;

            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatPhysicalAdapter adapter = _controller.Adapter;
            PhysicalFootContactDetector leftFoot = _controller.LeftFootContact;
            PhysicalFootContactDetector rightFoot = _controller.RightFootContact;
            for (int tick = 0; tick < WarmupTicks; tick++)
                AdvanceTicks(runtime, 1, leftFoot, rightFoot);
            adapter.StartSquat();

            var captureLabels = new Dictionary<int, string>();
            if (referenceTrial.Detection != null && referenceTrial.Detection.HasStickingRegion)
            {
                for (int index = 0; index < referenceTrial.Detection.Events.Count; index++)
                {
                    SquatBarVelocityEvent eventValue = referenceTrial.Detection.Events[index];
                    AddCaptureLabel(captureLabels, eventValue.SampleIndex, eventValue.Kind.ToString());
                }
            }
            else if (referenceTrial.Detection != null && referenceTrial.Detection.FilteredVelocityMetersPerSecond.Count > 0)
            {
                int[] candidates = CandidateIndices(referenceTrial);
                string[] labels = { "v0_candidate", "vmax1_candidate", "dmax1_candidate", "vmin_candidate", "vmax2_candidate" };
                for (int index = 0; index < candidates.Length; index++)
                    AddCaptureLabel(captureLabels, candidates[index], labels[index]);
            }
            else
            {
                for (int index = 0; index < referenceTrial.Samples.Count; index++)
                {
                    if (referenceTrial.Samples[index].State == SquatState.BOTTOM)
                    {
                        AddCaptureLabel(
                            captureLabels,
                            index,
                            referenceTrial.HasBarSignal
                                ? "bottom_state_no_resolvable_sequence"
                                : "bottom_state_no_bar_signal");
                        break;
                    }
                }
            }

            AddCaptureLabel(captureLabels, referenceTrial.KneeErrorPeakSampleIndex, "knee_error_peak");
            AddCaptureLabel(captureLabels, referenceTrial.LockoutSampleIndex, "lockout");

            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null)
            {
                Debug.LogWarning($"[H20] No camera available for visual replay at {load:F0} kg.");
                yield break;
            }

            int finalTick = referenceTrial.Samples.Count + 2;
            foreach (int index in captureLabels.Keys)
                finalTick = Mathf.Max(finalTick, index + 2);

            string loadTag = load <= 0f ? "0kg" : "25kg";
            for (int tick = 0; tick < finalTick; tick++)
            {
                AdvanceTicks(runtime, 1, leftFoot, rightFoot);
                if (!captureLabels.TryGetValue(tick, out string label))
                    continue;

                yield return null;
                string prefix = string.Format(CultureInfo.InvariantCulture, "h20_{0}_repeat{1}_{2}", loadTag, referenceTrial.Repeat, label);
                CaptureImage(camera, Path.Combine(Path.GetFullPath(EvidenceDirectory), prefix + "_side.png"),
                    new Vector3(4.0f, 1.35f, 0f), new Vector3(0f, 0.95f, 0f));
                CaptureImage(camera, Path.Combine(Path.GetFullPath(EvidenceDirectory), prefix + "_oblique.png"),
                    new Vector3(3.5f, 1.65f, 3.2f), new Vector3(0f, 0.95f, 0f));
            }

            yield return null;
        }

        private static void AddCaptureLabel(Dictionary<int, string> labels, int sampleIndex, string label)
        {
            if (sampleIndex < 0)
                return;
            if (labels.TryGetValue(sampleIndex, out string existing))
                labels[sampleIndex] = existing + "+" + label;
            else
                labels.Add(sampleIndex, label);
        }

        private void EvaluateRepeatability(float load)
        {
            H20Trial[] trials = new H20Trial[TrialCount];
            int found = 0;
            for (int index = 0; index < _trials.Count && found < TrialCount; index++)
            {
                if (Mathf.Abs(_trials[index].LoadKg - load) <= 0.001f)
                    trials[found++] = _trials[index];
            }

            bool stable = found == TrialCount;
            for (int index = 1; stable && index < found; index++)
            {
                stable = trials[0].HasBarSignal == trials[index].HasBarSignal;
                if (stable && trials[0].HasBarSignal)
                    stable = SameEventIdentity(trials[0].Detection, trials[index].Detection);
            }

            string status = stable ? "PASS" : "REPEATABILITY_FAIL";
            string signal = trials[0] != null && trials[0].HasBarSignal ? "BAR_RIGIDBODY" : "NO_BAR_SIGNAL";
            _repeatabilitySummary.AppendLine(Csv(
                F(load),
                F(found),
                signal,
                stable ? "STABLE" : "MATERIALLY_DIFFERENT",
                trials[0] == null ? "NA" : DetectionStatus(trials[0]),
                trials[1] == null ? "NA" : DetectionStatus(trials[1]),
                trials[2] == null ? "NA" : DetectionStatus(trials[2]),
                EventTimeRange(trials, SquatBarVelocityEventKind.Vmax1),
                EventTimeRange(trials, SquatBarVelocityEventKind.Vmin),
                EventTimeRange(trials, SquatBarVelocityEventKind.Vmax2)));

            if (!stable)
                Assert.Fail($"REPEATABILITY_FAIL for {load:F0} kg: fresh trials did not preserve event identity.");
        }

        private string EventTimeRange(H20Trial[] trials, SquatBarVelocityEventKind kind)
        {
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            for (int index = 0; index < trials.Length; index++)
            {
                if (trials[index] == null || trials[index].Detection == null ||
                    !trials[index].Detection.TryGetEvent(kind, out SquatBarVelocityEvent eventValue))
                    continue;
                minimum = Math.Min(minimum, eventValue.TimeSeconds);
                maximum = Math.Max(maximum, eventValue.TimeSeconds);
            }

            return double.IsPositiveInfinity(minimum)
                ? "NA"
                : F(minimum) + ".." + F(maximum);
        }

        private void AppendTrialSummary(H20Trial trial)
        {
            H20Sample peak = trial.KneeErrorPeakSampleIndex >= 0 && trial.KneeErrorPeakSampleIndex < trial.Samples.Count
                ? trial.Samples[trial.KneeErrorPeakSampleIndex]
                : null;
            _trialSummary.AppendLine(Csv(
                F(trial.LoadKg),
                F(trial.Repeat),
                trial.HasBarSignal ? "BAR_RIGIDBODY" : "NO_BAR_SIGNAL",
                DetectionStatus(trial),
                F(trial.Samples.Count),
                F(trial.LockoutSampleIndex),
                trial.LockoutSampleIndex >= 0 ? F(trial.Samples[trial.LockoutSampleIndex].TimeFromSquatStartSeconds) : "NA",
                F(trial.StationaryVelocities.Count),
                F(StationaryMedianAbsVelocity(trial.StationaryVelocities)),
                trial.Detection == null ? "NA" : F(trial.Detection.NoiseFloor.VelocitySigmaMetersPerSecond),
                trial.Detection == null ? "NA" : F(trial.Detection.NoiseFloor.AccelerationSigmaMetersPerSecondSquared),
                trial.SensitivityStatus,
                SequenceSignature(trial.Detection),
                F(EventIndex(trial.Detection, SquatBarVelocityEventKind.V0)),
                F(EventIndex(trial.Detection, SquatBarVelocityEventKind.Vmax1)),
                F(EventIndex(trial.Detection, SquatBarVelocityEventKind.Dmax1)),
                F(EventIndex(trial.Detection, SquatBarVelocityEventKind.Vmin)),
                F(EventIndex(trial.Detection, SquatBarVelocityEventKind.Vmax2)),
                F(trial.KneeErrorPeakSampleIndex),
                peak == null ? "NA" : F(peak.KneeLErrorDegrees),
                trial.KneeErrorPeakLocation));
        }

        private void AppendEventSummary(H20Trial trial)
        {
            if (trial.Detection == null || !trial.Detection.HasStickingRegion)
                return;
            SquatBarVelocityEvent v0 = trial.Detection.Events[0];
            for (int index = 0; index < trial.Detection.Events.Count; index++)
            {
                SquatBarVelocityEvent eventValue = trial.Detection.Events[index];
                H20Sample sample = trial.Samples[eventValue.SampleIndex];
                _eventSummary.AppendLine(FormatEventLine(trial, eventValue, sample, v0));
            }
        }

        private static string FormatEventLine(
            H20Trial trial,
            SquatBarVelocityEvent eventValue,
            H20Sample sample,
            SquatBarVelocityEvent v0)
        {
            return Csv(
                F(trial.LoadKg), F(trial.Repeat), DetectionStatus(trial), eventValue.Kind.ToString(),
                F(eventValue.SampleIndex), F(eventValue.TimeSeconds), F(eventValue.TimeSeconds - v0.TimeSeconds),
                F(sample.BarPositionMeters), F(eventValue.DisplacementFromV0Meters), F(eventValue.RawVelocityMetersPerSecond),
                F(eventValue.FilteredVelocityMetersPerSecond), F(sample.BarRawAccelerationMetersPerSecondSquared),
                F(eventValue.FilteredAccelerationMetersPerSecondSquared), F(sample.Sq), sample.State.ToString(), sample.Direction.ToString(),
                F(sample.KneeLJointSpaceDegrees), F(sample.KneeRJointSpaceDegrees), F(sample.HipLJointSpaceDegrees), F(sample.HipRJointSpaceDegrees),
                F(sample.AnkleLJointSpaceDegrees), F(sample.AnkleRJointSpaceDegrees), F(sample.KneeLIncludedDegrees), F(sample.KneeRIncludedDegrees),
                F(sample.HipLIncludedDegrees), F(sample.HipRIncludedDegrees), F(sample.AnkleLIncludedDegrees), F(sample.AnkleRIncludedDegrees),
                F(sample.AbdomenJointSpaceDegrees), F(sample.ThoraxJointSpaceDegrees), F(sample.WorldTrunkPitchDegrees),
                F(sample.KneeLOmegaDegreesPerSecond), F(sample.KneeROmegaDegreesPerSecond), F(sample.HipLOmegaDegreesPerSecond), F(sample.HipROmegaDegreesPerSecond),
                F(sample.AnkleLOmegaDegreesPerSecond), F(sample.AnkleROmegaDegreesPerSecond), F(sample.AbdomenOmegaDegreesPerSecond), F(sample.ThoraxOmegaDegreesPerSecond),
                F(sample.KneeLReferenceDegrees), F(sample.KneeRReferenceDegrees), F(sample.HipLReferenceDegrees), F(sample.HipRReferenceDegrees),
                F(sample.AnkleLReferenceDegrees), F(sample.AnkleRReferenceDegrees), F(sample.KneeLErrorDegrees), F(sample.HipLErrorDegrees), F(sample.AnkleLErrorDegrees),
                F(sample.Com.x), F(sample.Com.y), F(sample.Com.z), F(sample.ComVelocity.x), F(sample.ComVelocity.y), F(sample.ComVelocity.z),
                F(sample.CopAvailable), F(sample.CopZMeters), F(sample.CopFractionSupportAp), F(sample.SupportApMinMeters), F(sample.SupportApMaxMeters),
                F(sample.SupportApCenterMeters), F(sample.CaptureApMeters), F(sample.CaptureMarginRearMeters), F(sample.CaptureMarginFrontMeters),
                F(sample.SupportContactCount), F(sample.LeftFootContact), F(sample.RightFootContact), F(sample.LeftFootContactCount), F(sample.RightFootContactCount),
                F(sample.LeftSlipSpeedMetersPerSecond), F(sample.RightSlipSpeedMetersPerSecond), F(sample.LeftSlipAccumulatedMeters), F(sample.RightSlipAccumulatedMeters),
                F(sample.NormalForceContactEstimateNewtons), F(sample.SaddleSeparationMeters), F(sample.BarRotationXDegrees), F(sample.BarRotationYDegrees), F(sample.BarRotationZDegrees),
                F(sample.BarApRelativeComMeters), F(sample.BarApRelativeSupportMeters), sample.FailureReason, F(sample.LegalDepth), F(sample.LockoutReached));
        }

        private void AppendCandidateSummary(H20Trial trial)
        {
            if (trial.Detection == null || trial.Detection.FilteredVelocityMetersPerSecond.Count == 0)
                return;

            int[] candidates = CandidateIndices(trial);

            AppendCandidateRow(trial, "V0_CANDIDATE", candidates[0], "lowest direct bar position");
            AppendCandidateRow(trial, "VMAX1_CANDIDATE", candidates[1], "first filtered local maximum after V0");
            AppendCandidateRow(trial, "DMAX1_CANDIDATE", candidates[2], "most negative filtered acceleration between VMAX1 and VMIN");
            AppendCandidateRow(trial, "VMIN_CANDIDATE", candidates[3], "first filtered local minimum after VMAX1");
            AppendCandidateRow(trial, "VMAX2_CANDIDATE", candidates[4], "first filtered local maximum after VMIN");
        }

        private static int[] CandidateIndices(H20Trial trial)
        {
            int v0Index = IndexOfMinimumPosition(trial.Samples);
            int vmax1Index = FindLocalExtremum(trial.Detection.FilteredVelocityMetersPerSecond, v0Index + 1, true);
            int vminIndex = vmax1Index >= 0
                ? FindLocalExtremum(trial.Detection.FilteredVelocityMetersPerSecond, vmax1Index + 1, false)
                : -1;
            int vmax2Index = vminIndex >= 0
                ? FindLocalExtremum(trial.Detection.FilteredVelocityMetersPerSecond, vminIndex + 1, true)
                : -1;
            int dmax1Index = -1;
            if (vmax1Index >= 0 && vminIndex > vmax1Index)
            {
                dmax1Index = vmax1Index + 1;
                for (int index = vmax1Index + 2; index < vminIndex; index++)
                {
                    if (trial.Detection.FilteredAccelerationMetersPerSecondSquared[index] <
                        trial.Detection.FilteredAccelerationMetersPerSecondSquared[dmax1Index])
                        dmax1Index = index;
                }
            }

            return new[] { v0Index, vmax1Index, dmax1Index, vminIndex, vmax2Index };
        }

        private void AppendCandidateRow(H20Trial trial, string label, int index, string criterion)
        {
            if (index < 0 || index >= trial.Samples.Count)
                return;
            H20Sample sample = trial.Samples[index];
            int v0Index = IndexOfMinimumPosition(trial.Samples);
            float filteredVelocity = trial.Detection.FilteredVelocityMetersPerSecond[index];
            float filteredAcceleration = trial.Detection.FilteredAccelerationMetersPerSecondSquared[index];
            _candidateSummary.AppendLine(Csv(
                F(trial.LoadKg), F(trial.Repeat), label, F(index), F(sample.TimeFromSquatStartSeconds),
                F(sample.TimeFromSquatStartSeconds - trial.Samples[v0Index].TimeFromSquatStartSeconds),
                F(sample.BarPositionMeters), F(sample.BarVelocityMetersPerSecond), F(filteredVelocity),
                F(sample.BarRawAccelerationMetersPerSecondSquared), F(filteredAcceleration),
                F(trial.Detection.NoiseFloor.VelocitySigmaMetersPerSecond),
                F(trial.Detection.NoiseFloor.VelocityThresholdMetersPerSecond * SquatBarVelocityDetectorOptions.Default.NoiseSigmaMultiplier),
                F(trial.Detection.NoiseFloor.AccelerationSigmaMetersPerSecondSquared),
                F(trial.Detection.NoiseFloor.AccelerationThresholdMetersPerSecondSquared * SquatBarVelocityDetectorOptions.Default.NoiseSigmaMultiplier),
                "0", criterion));
        }

        private static int IndexOfMinimumPosition(IReadOnlyList<H20Sample> samples)
        {
            int indexOfMinimum = 0;
            float minimum = samples[0].BarPositionMeters;
            for (int index = 1; index < samples.Count; index++)
            {
                if (samples[index].BarPositionMeters < minimum)
                {
                    minimum = samples[index].BarPositionMeters;
                    indexOfMinimum = index;
                }
            }
            return indexOfMinimum;
        }

        private static int FindLocalExtremum(IReadOnlyList<float> values, int startIndex, bool maximum)
        {
            for (int index = Math.Max(1, startIndex); index < values.Count - 1; index++)
            {
                bool isMaximum = values[index] > values[index - 1] && values[index] >= values[index + 1];
                bool isMinimum = values[index] < values[index - 1] && values[index] <= values[index + 1];
                if ((maximum && isMaximum) || (!maximum && isMinimum))
                    return index;
            }
            return -1;
        }

        private void WriteTrialTrace(H20Trial trial)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "load_kg,repeat,sample_index,time_from_squat_start_s,state,direction,s_q,bar_present,bar_y_m,bar_vy_mps,bar_a_raw_mps2," +
                "knee_l_joint_space_deg,knee_r_joint_space_deg,hip_l_joint_space_deg,hip_r_joint_space_deg,ankle_l_joint_space_deg,ankle_r_joint_space_deg," +
                "knee_l_included_deg,knee_r_included_deg,hip_l_included_deg,hip_r_included_deg,ankle_l_included_deg,ankle_r_included_deg," +
                "abdomen_joint_space_deg,thorax_joint_space_deg,world_trunk_pitch_deg,knee_l_omega_deg_s,knee_r_omega_deg_s,hip_l_omega_deg_s,hip_r_omega_deg_s," +
                "ankle_l_omega_deg_s,ankle_r_omega_deg_s,abdomen_omega_deg_s,thorax_omega_deg_s,knee_l_ref_deg,knee_r_ref_deg,hip_l_ref_deg,hip_r_ref_deg,ankle_l_ref_deg,ankle_r_ref_deg," +
                "knee_l_error_deg,hip_l_error_deg,ankle_l_error_deg,com_x_m,com_y_m,com_z_m,com_vel_x_mps,com_vel_y_mps,com_vel_z_mps," +
                "cop_available,cop_z_m,cop_fraction_support_ap,support_ap_min_m,support_ap_max_m,support_ap_center_m,capture_ap_m,capture_margin_rear_m,capture_margin_front_m," +
                "support_contacts,left_foot_contact,right_foot_contact,left_foot_contact_count,right_foot_contact_count,left_slip_speed_mps,right_slip_speed_mps,left_slip_accumulated_m,right_slip_accumulated_m," +
                "normal_force_contact_estimate_n,saddle_separation_m,bar_rot_x_deg,bar_rot_y_deg,bar_rot_z_deg,bar_ap_relative_com_m,bar_ap_relative_support_m,failure_reason,legal_depth,lockout_reached");
            for (int index = 0; index < trial.Samples.Count; index++)
                csv.AppendLine(FormatTraceLine(trial, trial.Samples[index], index));

            string loadTag = trial.LoadKg <= 0f ? "0kg" : "25kg";
            string path = Path.Combine(Path.GetFullPath(MeasurementDirectory),
                string.Format(CultureInfo.InvariantCulture, "h20-{0}-repeat{1}.csv", loadTag, trial.Repeat));
            File.WriteAllText(path, csv.ToString());
        }

        private static string FormatTraceLine(H20Trial trial, H20Sample sample, int index)
        {
            return Csv(
                F(trial.LoadKg), F(trial.Repeat), F(index), F(sample.TimeFromSquatStartSeconds), sample.State.ToString(), sample.Direction.ToString(), F(sample.Sq),
                F(sample.HasBarSignal), F(sample.BarPositionMeters), F(sample.BarVelocityMetersPerSecond), F(sample.BarRawAccelerationMetersPerSecondSquared),
                F(sample.KneeLJointSpaceDegrees), F(sample.KneeRJointSpaceDegrees), F(sample.HipLJointSpaceDegrees), F(sample.HipRJointSpaceDegrees),
                F(sample.AnkleLJointSpaceDegrees), F(sample.AnkleRJointSpaceDegrees), F(sample.KneeLIncludedDegrees), F(sample.KneeRIncludedDegrees),
                F(sample.HipLIncludedDegrees), F(sample.HipRIncludedDegrees), F(sample.AnkleLIncludedDegrees), F(sample.AnkleRIncludedDegrees),
                F(sample.AbdomenJointSpaceDegrees), F(sample.ThoraxJointSpaceDegrees), F(sample.WorldTrunkPitchDegrees),
                F(sample.KneeLOmegaDegreesPerSecond), F(sample.KneeROmegaDegreesPerSecond), F(sample.HipLOmegaDegreesPerSecond), F(sample.HipROmegaDegreesPerSecond),
                F(sample.AnkleLOmegaDegreesPerSecond), F(sample.AnkleROmegaDegreesPerSecond), F(sample.AbdomenOmegaDegreesPerSecond), F(sample.ThoraxOmegaDegreesPerSecond),
                F(sample.KneeLReferenceDegrees), F(sample.KneeRReferenceDegrees), F(sample.HipLReferenceDegrees), F(sample.HipRReferenceDegrees),
                F(sample.AnkleLReferenceDegrees), F(sample.AnkleRReferenceDegrees), F(sample.KneeLErrorDegrees), F(sample.HipLErrorDegrees), F(sample.AnkleLErrorDegrees),
                F(sample.Com.x), F(sample.Com.y), F(sample.Com.z), F(sample.ComVelocity.x), F(sample.ComVelocity.y), F(sample.ComVelocity.z),
                F(sample.CopAvailable), F(sample.CopZMeters), F(sample.CopFractionSupportAp), F(sample.SupportApMinMeters), F(sample.SupportApMaxMeters),
                F(sample.SupportApCenterMeters), F(sample.CaptureApMeters), F(sample.CaptureMarginRearMeters), F(sample.CaptureMarginFrontMeters),
                F(sample.SupportContactCount), F(sample.LeftFootContact), F(sample.RightFootContact), F(sample.LeftFootContactCount), F(sample.RightFootContactCount),
                F(sample.LeftSlipSpeedMetersPerSecond), F(sample.RightSlipSpeedMetersPerSecond), F(sample.LeftSlipAccumulatedMeters), F(sample.RightSlipAccumulatedMeters),
                F(sample.NormalForceContactEstimateNewtons), F(sample.SaddleSeparationMeters), F(sample.BarRotationXDegrees), F(sample.BarRotationYDegrees), F(sample.BarRotationZDegrees),
                F(sample.BarApRelativeComMeters), F(sample.BarApRelativeSupportMeters), sample.FailureReason, F(sample.LegalDepth), F(sample.LockoutReached));
        }

        private H20Sample ReadSample(
            H20Trial trial,
            int sampleIndex,
            double timeFromSquatStartSeconds,
            SquatPhysicalAdapter adapter,
            PhysicalFootContactDetector leftFoot,
            PhysicalFootContactDetector rightFoot)
        {
            // The adapter's composition is the accepted reference context;
            // actual kinematics below come directly from the live rigid bodies.
            PoweredJointController.PoweredJointRuntime kneeJointL = FindJoint("left_shank");
            PoweredJointController.PoweredJointRuntime kneeJointR = FindJoint("right_shank");
            PoweredJointController.PoweredJointRuntime hipJointL = FindJoint("left_thigh");
            PoweredJointController.PoweredJointRuntime hipJointR = FindJoint("right_thigh");
            PoweredJointController.PoweredJointRuntime ankleJointL = FindJoint("left_foot");
            PoweredJointController.PoweredJointRuntime ankleJointR = FindJoint("right_foot");
            PoweredJointController.PoweredJointRuntime abdomenJoint = FindJoint("abdomen");
            PoweredJointController.PoweredJointRuntime thoraxJoint = FindJoint("thorax");

            JointKinematics kneeLState = ReadJointKinematics(kneeJointL);
            JointKinematics kneeRState = ReadJointKinematics(kneeJointR);
            JointKinematics hipLState = ReadJointKinematics(hipJointL);
            JointKinematics hipRState = ReadJointKinematics(hipJointR);
            JointKinematics ankleLState = ReadJointKinematics(ankleJointL);
            JointKinematics ankleRState = ReadJointKinematics(ankleJointR);
            JointKinematics abdomenState = ReadJointKinematics(abdomenJoint);
            JointKinematics thoraxState = ReadJointKinematics(thoraxJoint);

            SquatPhysicalPrototypeController controller = _controller;
            PhysicalAthleteRig rig = _rig;
            PhysicalBarbell bar = _bar;
            Rigidbody pelvisBody = rig.Segments["pelvis"].Body;
            Rigidbody thoraxBody = rig.Segments["thorax"].Body;
            Rigidbody leftFootBody = rig.Segments["left_foot"].Body;
            Rigidbody rightFootBody = rig.Segments["right_foot"].Body;

            Vector3 hipAnchorL = WorldJointAnchor(hipJointL);
            Vector3 hipAnchorR = WorldJointAnchor(hipJointR);
            Vector3 kneeAnchorL = WorldJointAnchor(kneeJointL);
            Vector3 kneeAnchorR = WorldJointAnchor(kneeJointR);
            Vector3 ankleAnchorL = WorldJointAnchor(ankleJointL);
            Vector3 ankleAnchorR = WorldJointAnchor(ankleJointR);

            float kneeLIncluded = IncludedAngle(kneeAnchorL, hipAnchorL, ankleAnchorL);
            float kneeRIncluded = IncludedAngle(kneeAnchorR, hipAnchorR, ankleAnchorR);
            float hipLIncluded = IncludedAngle(hipAnchorL, thoraxBody.position, kneeAnchorL);
            float hipRIncluded = IncludedAngle(hipAnchorR, thoraxBody.position, kneeAnchorR);
            float ankleLIncluded = Vector3.Angle(kneeAnchorL - ankleAnchorL, leftFootBody.transform.forward);
            float ankleRIncluded = Vector3.Angle(kneeAnchorR - ankleAnchorR, rightFootBody.transform.forward);
            Vector3 trunkAxis = thoraxBody.position - pelvisBody.position;
            float trunkPitch = Mathf.Atan2(trunkAxis.z, trunkAxis.y) * Mathf.Rad2Deg;

            adapter.TryGetTargetComposition("left_shank", out SquatPhysicalAdapter.JointTargetComposition kneeRefL);
            adapter.TryGetTargetComposition("right_shank", out SquatPhysicalAdapter.JointTargetComposition kneeRefR);
            adapter.TryGetTargetComposition("left_thigh", out SquatPhysicalAdapter.JointTargetComposition hipRefL);
            adapter.TryGetTargetComposition("right_thigh", out SquatPhysicalAdapter.JointTargetComposition hipRefR);
            adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition ankleRefL);
            adapter.TryGetTargetComposition("right_foot", out SquatPhysicalAdapter.JointTargetComposition ankleRefR);

            float kneeLReference = TwistX(kneeRefL.Final);
            float kneeRReference = TwistX(kneeRefR.Final);
            float hipLReference = TwistX(hipRefL.Final);
            float hipRReference = TwistX(hipRefR.Final);
            float ankleLReference = TwistX(ankleRefL.Final);
            float ankleRReference = TwistX(ankleRefR.Final);

            SquatBalanceObserver balance = adapter.Balance;
            float supportLength = balance.SupportApMax - balance.SupportApMin;
            float copFraction = balance.HasCopEstimate && supportLength > 0.000001f
                ? (balance.CopEstimate.z - balance.SupportApMin) / supportLength
                : float.NaN;
            bool hasBar = trial.HasBarSignal && bar != null && bar.Body != null && bar.Body.gameObject.activeInHierarchy;
            Vector3 barPosition = hasBar ? bar.Body.position : new Vector3(float.NaN, float.NaN, float.NaN);
            Vector3 barRotation = hasBar ? bar.Body.rotation.eulerAngles : new Vector3(float.NaN, float.NaN, float.NaN);
            if (hasBar)
            {
                barRotation.x = SignedEulerDegrees(barRotation.x);
                barRotation.y = SignedEulerDegrees(barRotation.y);
                barRotation.z = SignedEulerDegrees(barRotation.z);
            }

            return new H20Sample
            {
                TimeFromSquatStartSeconds = timeFromSquatStartSeconds,
                State = adapter.State,
                Direction = adapter.Direction,
                Sq = adapter.Sq,
                HasBarSignal = hasBar,
                BarPositionMeters = barPosition.y,
                BarVelocityMetersPerSecond = hasBar ? bar.Body.linearVelocity.y : float.NaN,
                BarRawAccelerationMetersPerSecondSquared = float.NaN,
                KneeLJointSpaceDegrees = kneeLState.AngleDegrees,
                KneeRJointSpaceDegrees = kneeRState.AngleDegrees,
                HipLJointSpaceDegrees = hipLState.AngleDegrees,
                HipRJointSpaceDegrees = hipRState.AngleDegrees,
                AnkleLJointSpaceDegrees = ankleLState.AngleDegrees,
                AnkleRJointSpaceDegrees = ankleRState.AngleDegrees,
                KneeLIncludedDegrees = kneeLIncluded,
                KneeRIncludedDegrees = kneeRIncluded,
                HipLIncludedDegrees = hipLIncluded,
                HipRIncludedDegrees = hipRIncluded,
                AnkleLIncludedDegrees = ankleLIncluded,
                AnkleRIncludedDegrees = ankleRIncluded,
                AbdomenJointSpaceDegrees = abdomenState.AngleDegrees,
                ThoraxJointSpaceDegrees = thoraxState.AngleDegrees,
                WorldTrunkPitchDegrees = trunkPitch,
                KneeLOmegaDegreesPerSecond = kneeLState.OmegaDegreesPerSecond,
                KneeROmegaDegreesPerSecond = kneeRState.OmegaDegreesPerSecond,
                HipLOmegaDegreesPerSecond = hipLState.OmegaDegreesPerSecond,
                HipROmegaDegreesPerSecond = hipRState.OmegaDegreesPerSecond,
                AnkleLOmegaDegreesPerSecond = ankleLState.OmegaDegreesPerSecond,
                AnkleROmegaDegreesPerSecond = ankleRState.OmegaDegreesPerSecond,
                AbdomenOmegaDegreesPerSecond = abdomenState.OmegaDegreesPerSecond,
                ThoraxOmegaDegreesPerSecond = thoraxState.OmegaDegreesPerSecond,
                KneeLReferenceDegrees = kneeLReference,
                KneeRReferenceDegrees = kneeRReference,
                HipLReferenceDegrees = hipLReference,
                HipRReferenceDegrees = hipRReference,
                AnkleLReferenceDegrees = ankleLReference,
                AnkleRReferenceDegrees = ankleRReference,
                KneeLErrorDegrees = kneeLState.AngleDegrees - kneeLReference,
                HipLErrorDegrees = hipLState.AngleDegrees - hipLReference,
                AnkleLErrorDegrees = ankleLState.AngleDegrees - ankleLReference,
                Com = balance.SystemCom,
                ComVelocity = balance.SystemComVelocity,
                CopAvailable = balance.HasCopEstimate,
                CopZMeters = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                CopFractionSupportAp = copFraction,
                SupportApMinMeters = balance.SupportApMin,
                SupportApMaxMeters = balance.SupportApMax,
                SupportApCenterMeters = balance.SupportApCenter,
                CaptureApMeters = balance.CaptureAp,
                CaptureMarginRearMeters = balance.CaptureMarginRear,
                CaptureMarginFrontMeters = balance.CaptureMarginFront,
                SupportContactCount = balance.SupportContactCount,
                LeftFootContact = leftFoot != null && leftFoot.IsInContact,
                RightFootContact = rightFoot != null && rightFoot.IsInContact,
                LeftFootContactCount = leftFoot == null ? 0 : leftFoot.CompletedContactCount,
                RightFootContactCount = rightFoot == null ? 0 : rightFoot.CompletedContactCount,
                LeftSlipSpeedMetersPerSecond = leftFoot == null ? float.NaN : leftFoot.SlipSpeed,
                RightSlipSpeedMetersPerSecond = rightFoot == null ? float.NaN : rightFoot.SlipSpeed,
                LeftSlipAccumulatedMeters = leftFoot == null ? float.NaN : leftFoot.SlipAccumulatedM,
                RightSlipAccumulatedMeters = rightFoot == null ? float.NaN : rightFoot.SlipAccumulatedM,
                NormalForceContactEstimateNewtons = balance.TotalNormalImpulse / (float)SimulationConstants.FixedDeltaTimeSeconds,
                SaddleSeparationMeters = controller.Saddle == null ? float.NaN : controller.Saddle.SaddleSeparationMeters,
                BarRotationXDegrees = barRotation.x,
                BarRotationYDegrees = barRotation.y,
                BarRotationZDegrees = barRotation.z,
                BarApRelativeComMeters = hasBar ? barPosition.z - balance.SystemCom.z : float.NaN,
                BarApRelativeSupportMeters = hasBar ? barPosition.z - balance.SupportApCenter : float.NaN,
                FailureReason = adapter.FailureReason,
                LegalDepth = adapter.LegalDepth,
                LockoutReached = adapter.LockoutReached
            };
        }

        private PoweredJointController.PoweredJointRuntime FindJoint(string id)
        {
            return _rig.PoweredController.GetJoint(id);
        }

        private static JointKinematics ReadJointKinematics(PoweredJointController.PoweredJointRuntime joint)
        {
            Quaternion currentParentToChild = Quaternion.Inverse(joint.Joint.connectedBody.rotation) * joint.Joint.transform.rotation;
            Quaternion childNeutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * currentParentToChild;
            Quaternion actual = PoweredJointController.NormalizeCanonical(
                Quaternion.Inverse(joint.JointSpace) * childNeutralDelta * joint.JointSpace);
            Rigidbody childBody = joint.Joint.GetComponent<Rigidbody>();
            Vector3 relativeWorld = childBody.angularVelocity - joint.Joint.connectedBody.angularVelocity;
            Vector3 relativeChild = Quaternion.Inverse(childBody.rotation) * relativeWorld;
            Vector3 actualVelocity = Quaternion.Inverse(joint.JointSpace) * relativeChild;
            return new JointKinematics(TwistX(actual), actualVelocity.x * Mathf.Rad2Deg);
        }

        private static Vector3 WorldJointAnchor(PoweredJointController.PoweredJointRuntime joint) =>
            joint.Joint.transform.TransformPoint(joint.Joint.anchor);

        private static float IncludedAngle(Vector3 center, Vector3 first, Vector3 second) =>
            Vector3.Angle(first - center, second - center);

        private static int FindKneeErrorPeak(H20Trial trial)
        {
            float peak = 0f;
            int peakIndex = -1;
            for (int index = 0; index < trial.Samples.Count; index++)
            {
                H20Sample sample = trial.Samples[index];
                if (sample.State != SquatState.ASCENT || sample.Direction != SquatPhaseDirection.Ascent)
                    continue;
                if (Mathf.Abs(sample.KneeLErrorDegrees) > Mathf.Abs(peak))
                {
                    peak = sample.KneeLErrorDegrees;
                    peakIndex = index;
                }
            }

            return peakIndex;
        }

        private static string LocateKneeErrorPeak(H20Trial trial)
        {
            int index = trial.KneeErrorPeakSampleIndex;
            if (index < 0 || trial.Detection == null || !trial.Detection.HasStickingRegion)
                return trial.Detection == null ? "NO_BAR_SIGNAL" : "NO_RESOLVABLE_STICKING_REGION";
            int vmax1 = EventIndex(trial.Detection, SquatBarVelocityEventKind.Vmax1);
            int vmin = EventIndex(trial.Detection, SquatBarVelocityEventKind.Vmin);
            int vmax2 = EventIndex(trial.Detection, SquatBarVelocityEventKind.Vmax2);
            if (index < vmax1) return "BEFORE_VMAX1";
            if (index <= vmin) return "INSIDE_VMAX1_TO_VMIN";
            if (index <= vmax2) return "AFTER_VMIN_BEFORE_VMAX2";
            return "AFTER_VMAX2";
        }

        private static void PopulateRawAcceleration(H20Trial trial)
        {
            if (!trial.HasBarSignal)
                return;
            int count = trial.Samples.Count;
            if (count == 1)
            {
                trial.Samples[0].BarRawAccelerationMetersPerSecondSquared = 0f;
                return;
            }

            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            trial.Samples[0].BarRawAccelerationMetersPerSecondSquared =
                (trial.Samples[1].BarVelocityMetersPerSecond - trial.Samples[0].BarVelocityMetersPerSecond) / dt;
            for (int index = 1; index < count - 1; index++)
            {
                trial.Samples[index].BarRawAccelerationMetersPerSecondSquared =
                    (trial.Samples[index + 1].BarVelocityMetersPerSecond - trial.Samples[index - 1].BarVelocityMetersPerSecond) / (2f * dt);
            }
            trial.Samples[count - 1].BarRawAccelerationMetersPerSecondSquared =
                (trial.Samples[count - 1].BarVelocityMetersPerSecond - trial.Samples[count - 2].BarVelocityMetersPerSecond) / dt;
        }

        private static void AdvanceTicks(
            FoundationRuntime runtime,
            int count,
            PhysicalFootContactDetector leftFoot,
            PhysicalFootContactDetector rightFoot)
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int index = 0; index < count; index++)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                if (leftFoot != null) leftFoot.PhysicsTickUpdate(dt);
                if (rightFoot != null) rightFoot.PhysicsTickUpdate(dt);
            }
        }

        private void PrepareManualRuntime(float loadKg)
        {
            _controller.SetLoad(loadKg);
            _controller.enabled = false;
            _bootstrap.enabled = false;
            _rig.enabled = false;
            _bar.enabled = false;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The squat qualification scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _bar = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_bar, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
        }

        private H20Trial FirstTrial(float load)
        {
            for (int index = 0; index < _trials.Count; index++)
            {
                if (Mathf.Abs(_trials[index].LoadKg - load) <= 0.001f)
                    return _trials[index];
            }

            return null;
        }

        private static string DetectionStatus(H20Trial trial) =>
            trial == null ? "NA" : trial.Detection == null ? "NO_BAR_SIGNAL" : DetectionStatus(trial.Detection);

        private static string DetectionStatus(SquatBarVelocityEventDetection detection)
        {
            if (detection == null)
                return "NA";
            if (detection.Status == SquatBarVelocityEventStatus.NoStickingRegion)
                return "NO_RESOLVABLE_STICKING_REGION";
            return detection.Status.ToString().ToUpperInvariant();
        }

        private static string SequenceSignature(SquatBarVelocityEventDetection detection)
        {
            if (detection == null)
                return "NO_BAR_SIGNAL";
            if (!detection.HasStickingRegion)
                return "NO_RESOLVABLE_STICKING_REGION";
            return "V0>Vmax1>Dmax1>Vmin>Vmax2";
        }

        private static int EventIndex(SquatBarVelocityEventDetection detection, SquatBarVelocityEventKind kind)
        {
            return detection != null && detection.TryGetEvent(kind, out SquatBarVelocityEvent value) ? value.SampleIndex : -1;
        }

        private static float StationaryMedianAbsVelocity(IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0)
                return float.NaN;
            float[] copy = new float[values.Count];
            for (int index = 0; index < values.Count; index++)
                copy[index] = Mathf.Abs(values[index]);
            Array.Sort(copy);
            int middle = copy.Length / 2;
            return copy.Length % 2 == 0 ? 0.5f * (copy[middle - 1] + copy[middle]) : copy[middle];
        }

        private static float SignedEulerDegrees(float degrees) => degrees > 180f ? degrees - 360f : degrees;

        private static float TwistX(Quaternion rotation)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(rotation);
            Vector3 vector = new Vector3(q.x, q.y, q.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            Quaternion twist = new Quaternion(projection.x, projection.y, projection.z, q.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private static void CaptureImage(Camera camera, string path, Vector3 position, Vector3 lookAt)
        {
            camera.transform.position = position;
            camera.transform.LookAt(lookAt);
            RenderTexture renderTexture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            Texture2D texture = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[H20] Capture failed for {path}: {exception.Message}");
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
                UnityEngine.Object.DestroyImmediate(texture);
            }
        }

        private static string Csv(params string[] values) => string.Join(",", values);
        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string F(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        private static string F(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string F(bool value) => value ? "1" : "0";

        private readonly struct JointKinematics
        {
            public JointKinematics(float angleDegrees, float omegaDegreesPerSecond)
            {
                AngleDegrees = angleDegrees;
                OmegaDegreesPerSecond = omegaDegreesPerSecond;
            }

            public float AngleDegrees { get; }
            public float OmegaDegreesPerSecond { get; }
        }

        private sealed class H20Trial
        {
            public H20Trial(float loadKg, int repeat, bool hasBarSignal)
            {
                LoadKg = loadKg;
                Repeat = repeat;
                HasBarSignal = hasBarSignal;
            }

            public float LoadKg { get; }
            public int Repeat { get; }
            public bool HasBarSignal { get; }
            public List<H20Sample> Samples { get; } = new List<H20Sample>(MaximumTrialTicks);
            public List<float> StationaryVelocities { get; } = new List<float>(StationaryWindowTicks);
            public SquatBarVelocityEventDetection Detection { get; set; }
            public int LockoutSampleIndex { get; set; } = -1;
            public int KneeErrorPeakSampleIndex { get; set; } = -1;
            public string KneeErrorPeakLocation { get; set; } = "NONE";
            public string SensitivityStatus { get; set; } = "NOT_RUN";
        }

        private sealed class H20Sample
        {
            public double TimeFromSquatStartSeconds;
            public SquatState State;
            public SquatPhaseDirection Direction;
            public float Sq;
            public bool HasBarSignal;
            public float BarPositionMeters;
            public float BarVelocityMetersPerSecond;
            public float BarRawAccelerationMetersPerSecondSquared;
            public float KneeLJointSpaceDegrees;
            public float KneeRJointSpaceDegrees;
            public float HipLJointSpaceDegrees;
            public float HipRJointSpaceDegrees;
            public float AnkleLJointSpaceDegrees;
            public float AnkleRJointSpaceDegrees;
            public float KneeLIncludedDegrees;
            public float KneeRIncludedDegrees;
            public float HipLIncludedDegrees;
            public float HipRIncludedDegrees;
            public float AnkleLIncludedDegrees;
            public float AnkleRIncludedDegrees;
            public float AbdomenJointSpaceDegrees;
            public float ThoraxJointSpaceDegrees;
            public float WorldTrunkPitchDegrees;
            public float KneeLOmegaDegreesPerSecond;
            public float KneeROmegaDegreesPerSecond;
            public float HipLOmegaDegreesPerSecond;
            public float HipROmegaDegreesPerSecond;
            public float AnkleLOmegaDegreesPerSecond;
            public float AnkleROmegaDegreesPerSecond;
            public float AbdomenOmegaDegreesPerSecond;
            public float ThoraxOmegaDegreesPerSecond;
            public float KneeLReferenceDegrees;
            public float KneeRReferenceDegrees;
            public float HipLReferenceDegrees;
            public float HipRReferenceDegrees;
            public float AnkleLReferenceDegrees;
            public float AnkleRReferenceDegrees;
            public float KneeLErrorDegrees;
            public float HipLErrorDegrees;
            public float AnkleLErrorDegrees;
            public Vector3 Com;
            public Vector3 ComVelocity;
            public bool CopAvailable;
            public float CopZMeters;
            public float CopFractionSupportAp;
            public float SupportApMinMeters;
            public float SupportApMaxMeters;
            public float SupportApCenterMeters;
            public float CaptureApMeters;
            public float CaptureMarginRearMeters;
            public float CaptureMarginFrontMeters;
            public int SupportContactCount;
            public bool LeftFootContact;
            public bool RightFootContact;
            public int LeftFootContactCount;
            public int RightFootContactCount;
            public float LeftSlipSpeedMetersPerSecond;
            public float RightSlipSpeedMetersPerSecond;
            public float LeftSlipAccumulatedMeters;
            public float RightSlipAccumulatedMeters;
            public float NormalForceContactEstimateNewtons;
            public float SaddleSeparationMeters;
            public float BarRotationXDegrees;
            public float BarRotationYDegrees;
            public float BarRotationZDegrees;
            public float BarApRelativeComMeters;
            public float BarApRelativeSupportMeters;
            public string FailureReason;
            public bool LegalDepth;
            public bool LockoutReached;
        }
    }
}
