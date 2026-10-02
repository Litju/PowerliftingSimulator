using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Read-only Phase A trace exporter. It consumes the frozen post-physics
    /// squat snapshots and copies additional GAM-49 provider/joint diagnostics.
    /// It never writes to the simulation.
    /// </summary>
    public sealed class GAM13UntouchedScreeningTrace
    {
        private static readonly string[] ControlledJoints =
        {
            "abdomen", "thorax", "head_neck",
            "left_upper_arm", "right_upper_arm", "left_forearm", "right_forearm",
            "left_hand", "right_hand", "left_thigh", "right_thigh",
            "left_shank", "right_shank", "left_foot", "right_foot"
        };

        private readonly List<Sample> _samples = new List<Sample>(1200);
        private PhysicalAthleteRig _rig;

        public int SampleCount => _samples.Count;

        public void RetainAttemptTrace(SquatTrace trace)
        {
            if (trace == null)
                throw new ArgumentNullException(nameof(trace));

            var attemptTicks = new HashSet<ulong>();
            for (int index = 0; index < trace.Count; index++)
                attemptTicks.Add(trace[index].SimulationTick);
            _samples.RemoveAll(sample => !attemptTicks.Contains(sample.Tick));

            if (_samples.Count != trace.Count)
                throw new InvalidOperationException(
                    $"Post-physics capture contains {_samples.Count} of {trace.Count} immutable attempt ticks.");
            for (int index = 0; index < trace.Count; index++)
            {
                Sample sample = _samples[index];
                ulong traceTick = trace[index].SimulationTick;
                if (sample.Tick != traceTick)
                    throw new InvalidOperationException(
                        $"Detailed trace tick {sample.Tick} does not match immutable attempt tick {traceTick} at row {index}.");
                sample.Index = index;
                sample.Values[0] = index.ToString(CultureInfo.InvariantCulture);
                sample.Values[1] = traceTick.ToString(CultureInfo.InvariantCulture);
            }
        }

        public void CapturePostPhysics(SquatPhysicalPrototypeController controller, SquatObservationSnapshot snapshot)
        {
            if (controller == null)
                throw new ArgumentNullException(nameof(controller));
            if (_rig == null)
                _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();

            var sample = new Sample { Index = _samples.Count, Tick = snapshot.SimulationTick };
            CaptureRows(controller, snapshot, sample);
            _samples.Add(sample);
        }

        public void WriteArtifact(string path, GAM13AttemptResult result)
        {
            if (result == null || result.Record == null)
                throw new ArgumentNullException(nameof(result));
            if (_samples.Count != result.Record.TraceSampleCount)
                throw new InvalidOperationException(
                    $"Captured {_samples.Count} detailed rows for {result.Record.TraceSampleCount} immutable attempt samples.");

            SquatAttemptRecord record = result.Record;
            SquatFailureDetector p3 = new SquatFailureDetector();
            p3.Evaluate(record.Trace, SquatFailureCompletionContext.Terminal(
                record.TerminalTick,
                record.TerminalTimeSeconds,
                record.TerminalReason));

            List<string> summary = BuildSummary(result, p3);
            ulong failureEvidenceEndTick = record.EventTicks.FailureOnsetTick;
            if (failureEvidenceEndTick == SquatAttemptEventTicks.NotAvailable &&
                record.PhysicalFailureOutcome == SquatFailureResultKind.PHYSICAL_FAILURE)
                failureEvidenceEndTick = record.TerminalTick;
            ulong failureEvidenceStartTick = failureEvidenceEndTick == SquatAttemptEventTicks.NotAvailable
                ? SquatAttemptEventTicks.NotAvailable
                : failureEvidenceEndTick >= 99ul ? failureEvidenceEndTick - 99ul : 0ul;

            WriteRows(
                path,
                GAM13SquatLoadCalibrationHarness.CsvRow(result).Split(','),
                summary,
                failureEvidenceStartTick,
                failureEvidenceEndTick);
        }

        public void WriteNoRecordArtifact(string path, float loadKg)
        {
            if (_samples.Count == 0)
                throw new InvalidOperationException("A no-record artifact requires captured pre-terminal observations.");

            ulong evidenceEndTick = _samples[_samples.Count - 1].Tick;
            ulong evidenceStartTick = evidenceEndTick >= 99ul ? evidenceEndTick - 99ul : 0ul;
            WriteRows(
                path,
                BuildNoRecordAttemptValues(loadKg),
                BuildNoRecordSummary(evidenceStartTick, evidenceEndTick),
                evidenceStartTick,
                evidenceEndTick);
        }

        private void WriteRows(
            string path,
            IList<string> attemptValues,
            List<string> summary,
            ulong boundedEvidenceStartTick,
            ulong boundedEvidenceEndTick)
        {
            List<string> header = BuildHeader();
            if (header.Count != 5 + GAM13SquatLoadCalibrationHarness.CsvColumns.Length +
                summary.Count + _samples[0].Values.Count + 1)
                throw new InvalidOperationException("GAM13 Phase A trace schema column count is inconsistent.");

            var csv = new StringBuilder(Math.Max(32768, _samples.Count * header.Count * 12));
            AppendCsvLine(csv, header);
            for (int index = 0; index < _samples.Count; index++)
            {
                Sample sample = _samples[index];
                var values = new List<string>(header.Count);
                values.Add(EnvironmentValue("GAM13_PHASE_A_RUN_ID"));
                values.Add(EnvironmentValue("GAM13_PHASE_A_BASE_SHA"));
                values.Add(EnvironmentValue("GAM13_PHASE_A_UNITY_EXE"));
                values.Add(System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
                values.Add(Application.unityVersion);
                values.AddRange(attemptValues);
                values.AddRange(summary);
                values.AddRange(sample.Values);
                values.Add(B(sample.Tick >= boundedEvidenceStartTick && sample.Tick <= boundedEvidenceEndTick &&
                             boundedEvidenceEndTick != SquatAttemptEventTicks.NotAvailable));
                if (values.Count != header.Count)
                    throw new InvalidOperationException(
                        $"GAM13 Phase A row {sample.Index} has {values.Count} values for {header.Count} columns.");
                AppendCsvLine(csv, values);
            }

            string parentDirectory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(parentDirectory))
                Directory.CreateDirectory(parentDirectory);
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        }

        private List<string> BuildNoRecordAttemptValues(float loadKg)
        {
            float minimumWorstDepth = float.PositiveInfinity;
            for (int index = 0; index < _samples.Count; index++)
                if (IsFinite(_samples[index].WorstDepth))
                    minimumWorstDepth = Math.Min(minimumWorstDepth, _samples[index].WorstDepth);

            var values = new List<string>(GAM13SquatLoadCalibrationHarness.CsvColumns.Length);
            foreach (string column in GAM13SquatLoadCalibrationHarness.CsvColumns)
            {
                switch (column)
                {
                    case "sweep": values.Add("phase-a-untouched"); break;
                    case "candidate": values.Add("production-checked-in"); break;
                    case "capacity_model_version": values.Add(GAM13SquatLoadCalibrationHarness.CurrentCapacityModel); break;
                    case "failure_calibration_version": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "load_kg": values.Add(F(loadKg)); break;
                    case "run": values.Add("1"); break;
                    case "athlete_mass_kg": values.Add(_rig == null ? "NA" : F(_rig.TotalMassKg)); break;
                    case "bar_mass_kg": values.Add(F(_samples[_samples.Count - 1].BarLoadKg)); break;
                    case "trace_count": values.Add(_samples.Count.ToString(CultureInfo.InvariantCulture)); break;
                    case "terminal_reason": values.Add("NO_ATTEMPT_RECORD_START_WINDOW_TIMEOUT"); break;
                    case "physical_success": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p3_evidence": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p3_outcome": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p3_primary": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p3_secondary": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p3_evidence_channels": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p2_evidence": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p2_outcome": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "p2_violations": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "min_worst_side_depth_m": values.Add(F(minimumWorstDepth)); break;
                    case "legal_physical_depth": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    case "sticking": values.Add("NOT_APPLICABLE_NO_ATTEMPT_RECORD"); break;
                    case "sticking_detector_status": values.Add("NOT_APPLICABLE_NO_ATTEMPT_RECORD"); break;
                    case "all_evidence_finite": values.Add("NOT_EVALUATED_NO_ATTEMPT_RECORD"); break;
                    default: values.Add("NA"); break;
                }
            }
            return values;
        }

        private List<string> BuildNoRecordSummary(ulong boundedStartTick, ulong boundedEndTick)
        {
            var data = new Dictionary<string, string>(StringComparer.Ordinal);
            float minLeft = float.PositiveInfinity;
            float minRight = float.PositiveInfinity;
            float minWorst = float.PositiveInfinity;
            float minJointLeft = float.PositiveInfinity;
            float minJointRight = float.PositiveInfinity;
            ulong minimumTick = NA;
            ulong firstStrictTick = NA;
            ulong firstGameTick = NA;
            Sample minimumSample = null;
            foreach (Sample sample in _samples)
            {
                if (IsFinite(sample.LeftDepth)) minLeft = Math.Min(minLeft, sample.LeftDepth);
                if (IsFinite(sample.RightDepth)) minRight = Math.Min(minRight, sample.RightDepth);
                if (IsFinite(sample.WorstDepth) && sample.WorstDepth < minWorst)
                {
                    minWorst = sample.WorstDepth;
                    minimumTick = sample.Tick;
                    minimumSample = sample;
                }
                if (IsFinite(sample.LeftJointCenterDepth)) minJointLeft = Math.Min(minJointLeft, sample.LeftJointCenterDepth);
                if (IsFinite(sample.RightJointCenterDepth)) minJointRight = Math.Min(minJointRight, sample.RightJointCenterDepth);
                if (firstStrictTick == NA && sample.LeftDepth < 0f && sample.RightDepth < 0f)
                    firstStrictTick = sample.Tick;
                if (firstGameTick == NA && sample.WorstDepth <= -SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M)
                    firstGameTick = sample.Tick;
            }

            float maxLeftHip = float.NaN;
            float maxRightHip = float.NaN;
            float maxLeftKnee = float.NaN;
            float maxRightKnee = float.NaN;
            float maxPelvisDisagreement = float.NaN;
            float maxLeftSlip = float.NaN;
            float maxRightSlip = float.NaN;
            float maxLeftSlipSpeed = float.NaN;
            float maxRightSlipSpeed = float.NaN;
            int bilateralSamples = 0;
            int evidenceSamples = 0;
            bool persistentBilateralContact = true;
            ulong firstContactLossTick = NA;
            foreach (Sample sample in _samples)
            {
                evidenceSamples++;
                if (sample.BilateralContact) bilateralSamples++;
                else
                {
                    persistentBilateralContact = false;
                    if (firstContactLossTick == NA) firstContactLossTick = sample.Tick;
                }
                maxLeftHip = MaxFinite(maxLeftHip, sample.LeftHipAnchorSeparation);
                maxRightHip = MaxFinite(maxRightHip, sample.RightHipAnchorSeparation);
                maxLeftKnee = MaxFinite(maxLeftKnee, sample.LeftKneeAnchorSeparation);
                maxRightKnee = MaxFinite(maxRightKnee, sample.RightKneeAnchorSeparation);
                maxPelvisDisagreement = MaxFinite(maxPelvisDisagreement, sample.PelvisOriginDisagreement);
                maxLeftSlip = MaxFinite(maxLeftSlip, sample.LeftSlipAccumulated);
                maxRightSlip = MaxFinite(maxRightSlip, sample.RightSlipAccumulated);
                maxLeftSlipSpeed = MaxFinite(maxLeftSlipSpeed, sample.LeftSlipSpeed);
                maxRightSlipSpeed = MaxFinite(maxRightSlipSpeed, sample.RightSlipSpeed);
            }

            data["screening_first_strict_rule_tick"] = Tick(firstStrictTick);
            data["screening_first_game_qualified_tick"] = Tick(firstGameTick);
            data["screening_min_left_surface_depth_m"] = F(minLeft);
            data["screening_min_right_surface_depth_m"] = F(minRight);
            data["screening_min_worst_surface_depth_m"] = F(minWorst);
            data["screening_minimum_depth_tick"] = Tick(minimumTick);
            data["screening_min_joint_center_left_m_diagnostic"] = F(minJointLeft);
            data["screening_min_joint_center_right_m_diagnostic"] = F(minJointRight);
            foreach (string key in new[]
                     {
                         "screening_physical_descent_seen", "screening_physical_bottom_seen",
                         "screening_legal_bottom_seen", "screening_ascent_established",
                         "screening_physical_lockout_seen", "screening_p3_terminal_context_covered"
                     })
                data[key] = "NOT_EVALUATED_NO_ATTEMPT_RECORD";
            data["screening_p3_terminal_context_status"] = "NO_ATTEMPT_RECORD_START_WINDOW_TIMEOUT";
            foreach (string key in new[]
                     {
                         "screening_event_physical_bottom_tick", "screening_analyzer_bottom_tick",
                         "screening_physical_ascent_onset_tick", "screening_analyzer_ascent_onset_tick",
                         "screening_physical_lockout_tick", "screening_physical_failure_onset_tick",
                         "screening_physical_failure_latch_tick", "screening_sticking_onset_tick",
                         "screening_sticking_end_tick", "screening_sticking_duration_s",
                         "screening_sticking_displacement_m", "screening_v_max1_mps",
                         "screening_v_min_mps", "screening_v_max2_mps"
                     })
                data[key] = "NOT_EVALUATED_NO_ATTEMPT_RECORD";
            data["screening_bounded_preterminal_start_tick"] = Tick(boundedStartTick);
            data["screening_bounded_preterminal_end_tick"] = Tick(boundedEndTick);
            int boundedCount = 0;
            foreach (Sample sample in _samples)
                if (sample.Tick >= boundedStartTick && sample.Tick <= boundedEndTick) boundedCount++;
            data["screening_bounded_preterminal_sample_count"] = boundedCount.ToString(CultureInfo.InvariantCulture);
            data["screening_persistent_bilateral_contact"] = B(evidenceSamples > 0 && persistentBilateralContact);
            data["screening_bilateral_contact_fraction"] =
                F(evidenceSamples == 0 ? float.NaN : (float)bilateralSamples / evidenceSamples);
            data["screening_first_support_or_bilateral_contact_loss_tick"] = Tick(firstContactLossTick);
            data["screening_max_left_slip_accumulated_m"] = F(maxLeftSlip);
            data["screening_max_right_slip_accumulated_m"] = F(maxRightSlip);
            data["screening_max_left_slip_speed_mps"] = F(maxLeftSlipSpeed);
            data["screening_max_right_slip_speed_mps"] = F(maxRightSlipSpeed);
            data["screening_max_left_hip_anchor_separation_m"] = F(maxLeftHip);
            data["screening_max_right_hip_anchor_separation_m"] = F(maxRightHip);
            data["screening_max_left_knee_anchor_separation_m"] = F(maxLeftKnee);
            data["screening_max_right_knee_anchor_separation_m"] = F(maxRightKnee);
            data["screening_max_inferred_pelvis_origin_disagreement_m"] = F(maxPelvisDisagreement);
            data["screening_reference_surface_depth_at_minimum_m"] =
                F(minimumSample == null ? float.NaN : minimumSample.ReferenceWorstDepth);
            data["screening_applied_target_surface_depth_at_minimum_m"] =
                F(minimumSample == null ? float.NaN : minimumSample.AppliedWorstDepth);
            data["screening_actual_surface_depth_at_minimum_m"] = F(minimumSample == null ? float.NaN : minimumSample.WorstDepth);
            data["screening_applied_target_actual_surface_delta_at_minimum_m"] =
                F(minimumSample == null ? float.NaN : minimumSample.WorstDepth - minimumSample.AppliedWorstDepth);
            data["modeled_demand_semantics"] = "MODELED_DEMAND_NOT_MEASURED_ACTUATOR_SATURATION";
            data["solver_torque_semantics"] = "ENGINE_SOLVER_DIAGNOSTIC_NOT_DIRECT_DRIVE_TORQUE";

            var values = new List<string>(SummaryColumns.Length);
            foreach (string column in SummaryColumns)
                values.Add(data.TryGetValue(column, out string value) ? value : "NA");
            return values;
        }

        private void CaptureRows(
            SquatPhysicalPrototypeController controller,
            SquatObservationSnapshot snapshot,
            Sample sample)
        {
            SquatPhysicalAdapter adapter = controller.Adapter;
            bool hasActualLandmarks = adapter.TryGetSurfaceRuleLandmarks(out SquatRuleLandmarkSet actualLandmarks);
            sample.LeftJointCenterDepth = hasActualLandmarks
                ? actualLandmarks.JointCenterDepthDiagnostic.LeftDepthM : float.NaN;
            sample.RightJointCenterDepth = hasActualLandmarks
                ? actualLandmarks.JointCenterDepthDiagnostic.RightDepthM : float.NaN;
            sample.LeftDepth = snapshot.Depth.LeftDepthM;
            sample.RightDepth = snapshot.Depth.RightDepthM;
            sample.WorstDepth = snapshot.Depth.WorstSideDepthM;
            sample.BilateralContact = snapshot.Support.HasSupport &&
                                      snapshot.LeftFoot.IsInContact && snapshot.RightFoot.IsInContact;
            sample.LeftSlipAccumulated = snapshot.LeftFoot.SlipAccumulatedMeters;
            sample.RightSlipAccumulated = snapshot.RightFoot.SlipAccumulatedMeters;
            sample.LeftSlipSpeed = snapshot.LeftFoot.SlipSpeedMetersPerSecond;
            sample.RightSlipSpeed = snapshot.RightFoot.SlipSpeedMetersPerSecond;
            sample.BarY = snapshot.Bar.IsAvailable ? snapshot.Bar.PositionWorldMeters.Y : float.NaN;

            bool referenceTargetAvailable = TryReconstructTarget(adapter, snapshot, false,
                out SquatDepthObservation referenceTarget, out string referenceError);
            bool appliedTargetAvailable = TryReconstructTarget(adapter, snapshot, true,
                out SquatDepthObservation appliedTarget, out string appliedError);
            sample.ReferenceLeftDepth = referenceTargetAvailable ? referenceTarget.LeftDepthM : float.NaN;
            sample.ReferenceRightDepth = referenceTargetAvailable ? referenceTarget.RightDepthM : float.NaN;
            sample.ReferenceWorstDepth = referenceTargetAvailable ? referenceTarget.WorstSideDepthM : float.NaN;
            sample.AppliedLeftDepth = appliedTargetAvailable ? appliedTarget.LeftDepthM : float.NaN;
            sample.AppliedRightDepth = appliedTargetAvailable ? appliedTarget.RightDepthM : float.NaN;
            sample.AppliedWorstDepth = appliedTargetAvailable ? appliedTarget.WorstSideDepthM : float.NaN;
            sample.TargetReconstructionError = !referenceTargetAvailable
                ? "REFERENCE:" + referenceError
                : !appliedTargetAvailable ? "APPLIED:" + appliedError
                : string.Join(";", new[] { referenceError, appliedError }.Where(value => !string.IsNullOrEmpty(value)));

            var values = new List<string>(100 + ControlledJoints.Length * 80);
            values.Add(sample.Index.ToString(CultureInfo.InvariantCulture));
            values.Add(snapshot.SimulationTick.ToString(CultureInfo.InvariantCulture));
            values.Add(F(snapshot.SimulationTimeSeconds));
            values.Add(snapshot.HasAttemptRelativeTime ? F(snapshot.AttemptRelativeTimeSeconds) : "NA");
            values.Add(snapshot.State.ToString());
            values.Add(snapshot.Direction.ToString());
            values.Add(F(snapshot.Sq));
            values.Add(snapshot.Quality.ToString());
            values.Add(F(snapshot.Intent.Brace01));
            values.Add(F(snapshot.Intent.Yield01));
            values.Add(F(snapshot.Intent.Drive01));
            values.Add(F(snapshot.Intent.BalanceX));
            values.Add(F(snapshot.Intent.Grip01));

            values.Add(B(snapshot.Bar.IsAvailable));
            values.Add(F(snapshot.Bar.LoadKilograms));
            sample.BarLoadKg = snapshot.Bar.LoadKilograms;
            Add(values, snapshot.Bar.PositionWorldMeters);
            Add(values, snapshot.Bar.LinearVelocityWorldMetersPerSecond);
            Add(values, snapshot.Bar.AngularVelocityBarRadiansPerSecond);
            values.Add(B(snapshot.Bar.SaddleAttached));
            values.Add(B(snapshot.Bar.SaddleBroken));
            values.Add(F(snapshot.Bar.SaddleSeparationMeters));

            values.Add(B(snapshot.Depth.Availability == SquatTelemetryAvailability.AVAILABLE));
            values.Add(F(snapshot.Depth.LeftHipCreaseY));
            values.Add(F(snapshot.Depth.RightHipCreaseY));
            values.Add(F(snapshot.Depth.LeftKneeTopY));
            values.Add(F(snapshot.Depth.RightKneeTopY));
            values.Add(F(snapshot.Depth.LeftDepthM));
            values.Add(F(snapshot.Depth.RightDepthM));
            values.Add(F(snapshot.Depth.WorstSideDepthM));
            values.Add(B(snapshot.Depth.IPFRulePredicateSatisfied));
            values.Add(B(snapshot.Depth.BilateralGameJudgmentQualified));
            values.Add(F(-SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M - snapshot.Depth.WorstSideDepthM));
            values.Add(F(sample.LeftJointCenterDepth));
            values.Add(F(sample.RightJointCenterDepth));
            values.Add(F(Mathf.Max(sample.LeftJointCenterDepth, sample.RightJointCenterDepth)));
            values.Add(B(referenceTargetAvailable));
            values.Add(F(sample.ReferenceLeftDepth));
            values.Add(F(sample.ReferenceRightDepth));
            values.Add(F(sample.ReferenceWorstDepth));
            values.Add(B(appliedTargetAvailable));
            values.Add(F(sample.AppliedLeftDepth));
            values.Add(F(sample.AppliedRightDepth));
            values.Add(F(sample.AppliedWorstDepth));
            values.Add(F(sample.LeftDepth - sample.AppliedLeftDepth));
            values.Add(F(sample.RightDepth - sample.AppliedRightDepth));
            values.Add(F(sample.WorstDepth - sample.AppliedWorstDepth));
            values.Add(sample.TargetReconstructionError);

            values.Add(B(snapshot.Support.SystemComAvailability == SquatTelemetryAvailability.AVAILABLE));
            Add(values, snapshot.Support.SystemComWorldMeters);
            Add(values, snapshot.Support.SystemComVelocityWorldMetersPerSecond);
            values.Add(F(snapshot.Support.SystemMassKilograms));
            values.Add(B(snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE));
            values.Add(B(snapshot.Support.HasSupport));
            values.Add(F(snapshot.Support.SupportApMinM));
            values.Add(F(snapshot.Support.SupportApMaxM));
            values.Add(F(snapshot.Support.SupportMlMinM));
            values.Add(F(snapshot.Support.SupportMlMaxM));
            values.Add(F(snapshot.Support.SupportPlaneY));
            values.Add(F(snapshot.Support.SupportContactCount));
            values.Add(F(snapshot.Support.ComToSupportApFrontMarginM));
            values.Add(F(snapshot.Support.ComToSupportApRearMarginM));
            values.Add(F(snapshot.Support.ComToSupportMlRightMarginM));
            values.Add(F(snapshot.Support.ComToSupportMlLeftMarginM));
            values.Add(B(snapshot.Support.EngineContactPointAvailability == SquatTelemetryAvailability.AVAILABLE));
            Add(values, snapshot.Support.EngineContactPointWorldMeters);
            values.Add(F(snapshot.Support.TotalNormalImpulseNewtonSeconds));

            AddFoot(values, snapshot.LeftFoot);
            AddFoot(values, snapshot.RightFoot);
            values.Add(B(sample.BilateralContact));
            values.Add(B(snapshot.PelvisAvailability == SquatTelemetryAvailability.AVAILABLE));
            Add(values, snapshot.PelvisPositionWorldMeters);
            Add(values, snapshot.PelvisLinearVelocityWorldMetersPerSecond);
            values.Add(F(snapshot.TrunkWorldPitchRadians));

            CaptureAnchorAndPelvisDiagnostics(adapter, sample, values);

            foreach (string jointId in ControlledJoints)
                AddJointDiagnostics(adapter, snapshot, jointId, _rig, values);

            sample.Values.AddRange(values);
        }

        private bool TryReconstructTarget(
            SquatPhysicalAdapter adapter,
            SquatObservationSnapshot snapshot,
            bool useAppliedTargets,
            out SquatDepthObservation depth,
            out string error)
        {
            depth = default;
            error = string.Empty;
            if (adapter == null || adapter.ReferenceCalibration == null ||
                _rig == null || _rig.PoweredController == null)
            {
                error = "PROVIDER_OR_RIG_UNAVAILABLE";
                return false;
            }

            PhysicalAthleteRig rig = _rig;
            var targets = new Dictionary<string, Quaternion>(PhysicalAthleteDefinition.Segments.Count);
            var nonDepthFallbacks = new List<string>();
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                if (recipe.ParentId == null)
                    continue;

                if (useAppliedTargets)
                {
                    PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(recipe.Id);
                    if (joint == null || !joint.HasPostPhysicsDiagnostic)
                    {
                        if (IsDepthChainJoint(recipe.Id))
                        {
                            error = "APPLIED_TARGET_UNAVAILABLE:" + recipe.Id;
                            return false;
                        }
                        targets.Add(recipe.Id, ReferenceOrIdentity(adapter, snapshot, recipe.Id));
                        nonDepthFallbacks.Add(recipe.Id);
                        continue;
                    }
                    targets.Add(recipe.Id, joint.PostPhysicsDiagnostic.AppliedTarget);
                }
                else
                {
                    try
                    {
                        targets.Add(recipe.Id,
                            adapter.ReferenceLogicalTarget(recipe.Id, snapshot.Sq, snapshot.Direction));
                    }
                    catch (Exception exception)
                    {
                        if (IsDepthChainJoint(recipe.Id))
                        {
                            error = "REFERENCE_TARGET_UNAVAILABLE:" + recipe.Id + ":" + exception.GetType().Name;
                            return false;
                        }
                        targets.Add(recipe.Id, Quaternion.identity);
                        nonDepthFallbacks.Add(recipe.Id);
                    }
                }
            }

            try
            {
                SquatRuleLandmarkSet landmarks = SquatPhysicalTargetForwardKinematics.ReconstructLandmarks(
                    rig,
                    adapter.ReferenceCalibration,
                    targets,
                    adapter.ReferencePelvisBodyRotation(snapshot.Sq, snapshot.Direction));
                depth = landmarks.Depth;
                if (nonDepthFallbacks.Count > 0)
                    error = "NON_DEPTH_BRANCH_REFERENCE_OR_IDENTITY_FALLBACKS:" + string.Join("|", nonDepthFallbacks);
                return true;
            }
            catch (Exception exception)
            {
                error = (useAppliedTargets ? "APPLIED" : "REFERENCE") + "_RECONSTRUCTION:" +
                        exception.GetType().Name;
                return false;
            }
        }

        private static Quaternion ReferenceOrIdentity(
            SquatPhysicalAdapter adapter,
            SquatObservationSnapshot snapshot,
            string jointId)
        {
            try
            {
                return adapter.ReferenceLogicalTarget(jointId, snapshot.Sq, snapshot.Direction);
            }
            catch
            {
                return Quaternion.identity;
            }
        }

        private static bool IsDepthChainJoint(string jointId) =>
            jointId == "left_thigh" || jointId == "right_thigh" ||
            jointId == "left_shank" || jointId == "right_shank";

        private void CaptureAnchorAndPelvisDiagnostics(
            SquatPhysicalAdapter adapter,
            Sample sample,
            List<string> values)
        {
            if (_rig == null || _rig.PoweredController == null)
            {
                sample.LeftHipAnchorSeparation = float.NaN;
                sample.RightHipAnchorSeparation = float.NaN;
                sample.LeftKneeAnchorSeparation = float.NaN;
                sample.RightKneeAnchorSeparation = float.NaN;
                sample.PelvisOriginDisagreement = float.NaN;
                for (int i = 0; i < 13; i++) values.Add("NA");
                return;
            }

            sample.LeftHipAnchorSeparation = AnchorSeparation("left_thigh");
            sample.RightHipAnchorSeparation = AnchorSeparation("right_thigh");
            sample.LeftKneeAnchorSeparation = AnchorSeparation("left_shank");
            sample.RightKneeAnchorSeparation = AnchorSeparation("right_shank");
            values.Add(F(sample.LeftHipAnchorSeparation));
            values.Add(F(sample.RightHipAnchorSeparation));
            values.Add(F(sample.LeftKneeAnchorSeparation));
            values.Add(F(sample.RightKneeAnchorSeparation));

            if (!_rig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvis) ||
                pelvis.Body == null || adapter.ReferenceCalibration == null ||
                !TryHipAnchor("left_thigh", out Vector3 leftHip) ||
                !TryHipAnchor("right_thigh", out Vector3 rightHip))
            {
                sample.PelvisOriginDisagreement = float.NaN;
                for (int i = 0; i < 9; i++) values.Add("NA");
                return;
            }

            SquatReferenceRigCalibration calibration = adapter.ReferenceCalibration;
            Quaternion pelvisFrame = pelvis.Body.rotation * pelvis.BodyToReferenceBoneRotation *
                Quaternion.Inverse(calibration.Pelvis.BoneFromAnatomicalFrame);
            Vector3 inferredLeft = leftHip - pelvisFrame * calibration.LeftHipOffsetInPelvisFrame;
            Vector3 inferredRight = rightHip - pelvisFrame * calibration.RightHipOffsetInPelvisFrame;
            sample.PelvisOriginDisagreement = Vector3.Distance(inferredLeft, inferredRight);
            Add(values, inferredLeft);
            Add(values, inferredRight);
            values.Add(F(sample.PelvisOriginDisagreement));
        }

        private float AnchorSeparation(string jointId)
        {
            PoweredJointController.PoweredJointRuntime runtime = _rig.PoweredController.GetJoint(jointId);
            ConfigurableJoint joint = runtime == null ? null : runtime.Joint;
            if (joint == null || joint.connectedBody == null)
                return float.NaN;
            return Vector3.Distance(
                joint.transform.TransformPoint(joint.anchor),
                joint.connectedBody.transform.TransformPoint(joint.connectedAnchor));
        }

        private bool TryHipAnchor(string jointId, out Vector3 anchor)
        {
            anchor = Vector3.zero;
            PoweredJointController.PoweredJointRuntime runtime = _rig.PoweredController.GetJoint(jointId);
            if (runtime == null || runtime.Joint == null)
                return false;
            anchor = runtime.Joint.transform.TransformPoint(runtime.Joint.anchor);
            return true;
        }

        private static void AddJointDiagnostics(
            SquatPhysicalAdapter adapter,
            SquatObservationSnapshot snapshot,
            string jointId,
            PhysicalAthleteRig rig,
            List<string> values)
        {
            bool hasComposition = adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition);
            PoweredJointController.PoweredJointRuntime joint = rig == null || rig.PoweredController == null
                ? null : rig.PoweredController.GetJoint(jointId);
            if (joint == null || !joint.HasPostPhysicsDiagnostic)
            {
                values.Add(JointFamily(jointId));
                for (int i = 0; i < 16 + 12 + 9 + 9; i++) values.Add("NA");
                return;
            }

            PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
            values.Add(JointFamily(jointId));
            if (hasComposition)
            {
                AddQuaternion(values, composition.Nominal);
                AddQuaternion(values, composition.GravityBias);
                AddQuaternion(values, composition.BalanceOffset);
                AddQuaternion(values, composition.Final);
            }
            else
            {
                for (int i = 0; i < 16; i++) values.Add("NA");
            }
            AddQuaternion(values, diagnostic.RequestedTarget);
            AddQuaternion(values, diagnostic.AppliedTarget);
            AddQuaternion(values, diagnostic.ActualRelative);
            SquatJointObservation observation = JointObservation(snapshot, jointId);
            values.Add(F(observation.ActualAngleRadians));
            values.Add(F(observation.ReferenceAngleRadians));
            values.Add(F(observation.ActualReferenceErrorRadians));
            values.Add(F(Quaternion.Angle(diagnostic.AppliedTarget, diagnostic.ActualRelative) * Mathf.Deg2Rad));
            values.Add(F(diagnostic.LimitProximity));
            values.Add(F(diagnostic.ModeledDemand));
            values.Add(F(diagnostic.MaximumForceNm));
            values.Add(F(diagnostic.Activation));
            values.Add(F(diagnostic.CapacityScale));
            Add(values, diagnostic.TargetAngularVelocityRadS);
            Add(values, diagnostic.ActualAngularVelocityRadS);
            Add(values, diagnostic.SolverTorqueJointSpaceNm);
        }

        private static string JointFamily(string jointId)
        {
            switch (jointId)
            {
                case "left_foot":
                case "right_foot": return "ankle";
                case "left_shank":
                case "right_shank": return "knee";
                case "left_thigh":
                case "right_thigh": return "hip";
                case "abdomen":
                case "thorax": return "trunk";
                case "head_neck": return "neck";
                case "left_upper_arm":
                case "right_upper_arm": return "shoulder";
                case "left_forearm":
                case "right_forearm": return "elbow";
                case "left_hand":
                case "right_hand": return "wrist";
                default: return "unknown";
            }
        }

        private static SquatJointObservation JointObservation(SquatObservationSnapshot snapshot, string jointId)
        {
            switch (jointId)
            {
                case "left_foot": return snapshot.Joints.LeftAnkle;
                case "right_foot": return snapshot.Joints.RightAnkle;
                case "left_shank": return snapshot.Joints.LeftKnee;
                case "right_shank": return snapshot.Joints.RightKnee;
                case "left_thigh": return snapshot.Joints.LeftHip;
                case "right_thigh": return snapshot.Joints.RightHip;
                case "abdomen": return snapshot.Joints.Abdomen;
                case "thorax": return snapshot.Joints.Thorax;
                default: return SquatJointObservation.Unavailable();
            }
        }

        private static List<string> BuildHeader()
        {
            var values = new List<string>
            {
                "screening_run_id", "production_baseline_sha", "unity_executable", "unity_process_id",
                "unity_version"
            };
            values.AddRange(GAM13SquatLoadCalibrationHarness.CsvColumns);
            values.AddRange(SummaryColumns);
            values.AddRange(TraceColumns);
            values.Add("bounded_preterminal_evidence_sample");
            return values;
        }

        private static readonly string[] SummaryColumns =
        {
            "screening_first_strict_rule_tick", "screening_first_game_qualified_tick",
            "screening_min_left_surface_depth_m", "screening_min_right_surface_depth_m",
            "screening_min_worst_surface_depth_m", "screening_minimum_depth_tick",
            "screening_min_joint_center_left_m_diagnostic", "screening_min_joint_center_right_m_diagnostic",
            "screening_physical_descent_seen", "screening_physical_bottom_seen", "screening_legal_bottom_seen",
            "screening_ascent_established", "screening_physical_lockout_seen",
            "screening_p3_terminal_context_covered", "screening_p3_terminal_context_status",
            "screening_event_physical_bottom_tick", "screening_analyzer_bottom_tick",
            "screening_physical_ascent_onset_tick", "screening_analyzer_ascent_onset_tick",
            "screening_physical_lockout_tick", "screening_physical_failure_onset_tick",
            "screening_physical_failure_latch_tick", "screening_bounded_preterminal_start_tick",
            "screening_bounded_preterminal_end_tick", "screening_bounded_preterminal_sample_count",
            "screening_sticking_onset_tick", "screening_sticking_end_tick",
            "screening_sticking_duration_s", "screening_sticking_displacement_m",
            "screening_v_max1_mps", "screening_v_min_mps", "screening_v_max2_mps",
            "screening_persistent_bilateral_contact", "screening_bilateral_contact_fraction",
            "screening_first_support_or_bilateral_contact_loss_tick",
            "screening_max_left_slip_accumulated_m", "screening_max_right_slip_accumulated_m",
            "screening_max_left_slip_speed_mps", "screening_max_right_slip_speed_mps",
            "screening_max_left_hip_anchor_separation_m", "screening_max_right_hip_anchor_separation_m",
            "screening_max_left_knee_anchor_separation_m", "screening_max_right_knee_anchor_separation_m",
            "screening_max_inferred_pelvis_origin_disagreement_m",
            "screening_reference_surface_depth_at_minimum_m",
            "screening_applied_target_surface_depth_at_minimum_m",
            "screening_actual_surface_depth_at_minimum_m",
            "screening_applied_target_actual_surface_delta_at_minimum_m",
            "modeled_demand_semantics", "solver_torque_semantics"
        };

        private static readonly string[] TraceColumns = BuildTraceColumns();

        private static string[] BuildTraceColumns()
        {
            var columns = new List<string>
            {
                "sample_index", "simulation_tick", "simulation_time_s", "attempt_relative_time_s",
                "squat_state", "phase_direction", "gam10_reference_phase", "snapshot_quality",
                "intent_brace_01", "intent_yield_01", "intent_drive_01", "intent_balance_x", "intent_grip_01",
                "bar_available", "bar_load_kg", "bar_position_world_x_m", "bar_position_world_y_m", "bar_position_world_z_m",
                "bar_linear_velocity_world_x_mps", "bar_linear_velocity_world_y_mps", "bar_linear_velocity_world_z_mps",
                "bar_angular_velocity_bar_x_rads", "bar_angular_velocity_bar_y_rads", "bar_angular_velocity_bar_z_rads",
                "saddle_attached", "saddle_broken", "saddle_separation_m",
                "surface_landmarks_available", "left_hip_crease_y_m", "right_hip_crease_y_m",
                "left_knee_top_y_m", "right_knee_top_y_m", "actual_left_surface_depth_m",
                "actual_right_surface_depth_m", "actual_worst_surface_depth_m", "strict_bilateral_rule_predicate",
                "game_judgment_qualified_at_minus_0_005_m", "game_depth_margin_threshold_minus_worst_m",
                "joint_center_left_m_diagnostic_only", "joint_center_right_m_diagnostic_only",
                "joint_center_worst_m_diagnostic_only", "reference_surface_target_available",
                "reference_left_surface_depth_m", "reference_right_surface_depth_m", "reference_worst_surface_depth_m",
                "applied_target_surface_available", "applied_target_left_surface_depth_m",
                "applied_target_right_surface_depth_m", "applied_target_worst_surface_depth_m",
                "applied_to_actual_left_surface_delta_m", "applied_to_actual_right_surface_delta_m",
                "applied_to_actual_worst_surface_delta_m", "target_reconstruction_note_or_error"
            };
            columns.AddRange(new[]
            {
                "system_com_available", "system_com_world_x_m", "system_com_world_y_m", "system_com_world_z_m",
                "system_com_velocity_world_x_mps", "system_com_velocity_world_y_mps", "system_com_velocity_world_z_mps",
                "system_mass_kg", "support_observation_available", "has_support", "support_ap_min_m", "support_ap_max_m",
                "support_ml_min_m", "support_ml_max_m", "support_plane_y_m", "support_contact_count",
                "com_to_support_ap_front_margin_m", "com_to_support_ap_rear_margin_m",
                "com_to_support_ml_right_margin_m", "com_to_support_ml_left_margin_m",
                "engine_contact_point_available", "engine_contact_point_proxy_not_measured_cop_x_m",
                "engine_contact_point_proxy_not_measured_cop_y_m", "engine_contact_point_proxy_not_measured_cop_z_m",
                "total_normal_contact_impulse_ns",
                "left_foot_detector_available", "left_foot_in_contact", "left_foot_contact_count",
                "left_foot_completed_contact_count", "left_foot_slip_accumulated_m", "left_foot_slip_speed_mps",
                "right_foot_detector_available", "right_foot_in_contact", "right_foot_contact_count",
                "right_foot_completed_contact_count", "right_foot_slip_accumulated_m", "right_foot_slip_speed_mps",
                "persistent_bilateral_foot_contact_and_support", "pelvis_available", "pelvis_position_world_x_m",
                "pelvis_position_world_y_m", "pelvis_position_world_z_m", "pelvis_velocity_world_x_mps",
                "pelvis_velocity_world_y_mps", "pelvis_velocity_world_z_mps", "trunk_world_pitch_rad",
                "left_hip_anchor_separation_m", "right_hip_anchor_separation_m",
                "left_knee_anchor_separation_m", "right_knee_anchor_separation_m",
                "shared_provider_inferred_left_pelvis_origin_x_m", "shared_provider_inferred_left_pelvis_origin_y_m",
                "shared_provider_inferred_left_pelvis_origin_z_m", "shared_provider_inferred_right_pelvis_origin_x_m",
                "shared_provider_inferred_right_pelvis_origin_y_m", "shared_provider_inferred_right_pelvis_origin_z_m",
                "shared_provider_inferred_pelvis_origin_disagreement_m"
            });
            foreach (string joint in ControlledJoints)
            {
                columns.Add(joint + "_family");
                columns.Add(joint + "_nominal_reference_qx");
                columns.Add(joint + "_nominal_reference_qy");
                columns.Add(joint + "_nominal_reference_qz");
                columns.Add(joint + "_nominal_reference_qw");
                foreach (string layer in new[] { "gravity_bias", "balance_offset", "composed_final", "requested", "applied", "actual_relative" })
                {
                    columns.Add(joint + "_" + layer + "_qx");
                    columns.Add(joint + "_" + layer + "_qy");
                    columns.Add(joint + "_" + layer + "_qz");
                    columns.Add(joint + "_" + layer + "_qw");
                }
                columns.Add(joint + "_actual_angle_rad");
                columns.Add(joint + "_reference_angle_rad");
                columns.Add(joint + "_actual_minus_reference_error_rad");
                columns.Add(joint + "_applied_target_to_actual_angular_error_rad");
                columns.Add(joint + "_limit_proximity_01");
                columns.Add(joint + "_modeled_drive_demand_01");
                columns.Add(joint + "_configured_maximum_force_nm");
                columns.Add(joint + "_activation_01");
                columns.Add(joint + "_capacity_scale_01");
                columns.Add(joint + "_requested_angular_velocity_x_rads");
                columns.Add(joint + "_requested_angular_velocity_y_rads");
                columns.Add(joint + "_requested_angular_velocity_z_rads");
                columns.Add(joint + "_actual_angular_velocity_x_rads");
                columns.Add(joint + "_actual_angular_velocity_y_rads");
                columns.Add(joint + "_actual_angular_velocity_z_rads");
                columns.Add(joint + "_solver_constraint_torque_engine_solver_diagnostic_x_nm");
                columns.Add(joint + "_solver_constraint_torque_engine_solver_diagnostic_y_nm");
                columns.Add(joint + "_solver_constraint_torque_engine_solver_diagnostic_z_nm");
            }
            return columns.ToArray();
        }

        private List<string> BuildSummary(GAM13AttemptResult result, SquatFailureDetector p3)
        {
            SquatAttemptRecord record = result.Record;
            SquatLoadResponseMetrics metrics = result.Metrics;
            ulong firstStrictTick = NA;
            ulong firstGameTick = NA;
            ulong minimumTick = NA;
            float minLeft = float.PositiveInfinity;
            float minRight = float.PositiveInfinity;
            float minWorst = float.PositiveInfinity;
            float minJointLeft = float.PositiveInfinity;
            float minJointRight = float.PositiveInfinity;
            Sample minimumSample = null;
            ulong commandTick = record.EventTicks.SquatCommandTick;
            for (int index = 0; index < _samples.Count; index++)
            {
                Sample sample = _samples[index];
                if (sample.Tick < commandTick)
                    continue;
                if (IsFinite(sample.LeftDepth)) minLeft = Math.Min(minLeft, sample.LeftDepth);
                if (IsFinite(sample.RightDepth)) minRight = Math.Min(minRight, sample.RightDepth);
                if (IsFinite(sample.WorstDepth) && sample.WorstDepth < minWorst)
                {
                    minWorst = sample.WorstDepth;
                    minimumTick = sample.Tick;
                    minimumSample = sample;
                }
                if (IsFinite(sample.LeftJointCenterDepth)) minJointLeft = Math.Min(minJointLeft, sample.LeftJointCenterDepth);
                if (IsFinite(sample.RightJointCenterDepth)) minJointRight = Math.Min(minJointRight, sample.RightJointCenterDepth);
                if (firstStrictTick == NA && sample.LeftDepth < 0f && sample.RightDepth < 0f)
                    firstStrictTick = sample.Tick;
                if (firstGameTick == NA && sample.WorstDepth <= -SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M)
                    firstGameTick = sample.Tick;
            }

            float maxLeftHip = float.NaN;
            float maxRightHip = float.NaN;
            float maxLeftKnee = float.NaN;
            float maxRightKnee = float.NaN;
            float maxPelvisDisagreement = float.NaN;
            float maxLeftSlip = float.NaN;
            float maxRightSlip = float.NaN;
            float maxLeftSlipSpeed = float.NaN;
            float maxRightSlipSpeed = float.NaN;
            int supportSamples = 0;
            int bilateralSamples = 0;
            bool persistentContact = true;
            ulong firstContactLossTick = NA;
            foreach (Sample sample in _samples)
            {
                if (sample.Tick < commandTick)
                    continue;
                supportSamples++;
                if (sample.BilateralContact) bilateralSamples++;
                else
                {
                    persistentContact = false;
                    if (firstContactLossTick == NA) firstContactLossTick = sample.Tick;
                }
                maxLeftHip = MaxFinite(maxLeftHip, sample.LeftHipAnchorSeparation);
                maxRightHip = MaxFinite(maxRightHip, sample.RightHipAnchorSeparation);
                maxLeftKnee = MaxFinite(maxLeftKnee, sample.LeftKneeAnchorSeparation);
                maxRightKnee = MaxFinite(maxRightKnee, sample.RightKneeAnchorSeparation);
                maxPelvisDisagreement = MaxFinite(maxPelvisDisagreement, sample.PelvisOriginDisagreement);
                maxLeftSlip = MaxFinite(maxLeftSlip, sample.LeftSlipAccumulated);
                maxRightSlip = MaxFinite(maxRightSlip, sample.RightSlipAccumulated);
                maxLeftSlipSpeed = MaxFinite(maxLeftSlipSpeed, sample.LeftSlipSpeed);
                maxRightSlipSpeed = MaxFinite(maxRightSlipSpeed, sample.RightSlipSpeed);
            }

            bool stickingResolvable = metrics.StickingDetectorStatus == SquatBarVelocityEventStatus.ResolvableStickingRegion &&
                                      metrics.StickingVmax1Tick != NA && metrics.StickingVmax2Tick != NA;
            Sample vmax1 = stickingResolvable ? FindSample(metrics.StickingVmax1Tick) : null;
            Sample vmax2 = stickingResolvable ? FindSample(metrics.StickingVmax2Tick) : null;
            float stickingDisplacement = vmax1 == null || vmax2 == null ? float.NaN : vmax2.BarY - vmax1.BarY;

            ulong failureEnd = record.EventTicks.FailureOnsetTick;
            if (failureEnd == NA && record.PhysicalFailureOutcome == SquatFailureResultKind.PHYSICAL_FAILURE)
                failureEnd = record.TerminalTick;
            ulong failureStart = failureEnd == NA ? NA : failureEnd >= 99ul ? failureEnd - 99ul : 0ul;
            int failureWindowSamples = 0;
            if (failureEnd != NA)
                foreach (Sample sample in _samples)
                    if (sample.Tick >= failureStart && sample.Tick <= failureEnd) failureWindowSamples++;

            var values = new List<string>
            {
                Tick(firstStrictTick), Tick(firstGameTick), F(minLeft), F(minRight), F(minWorst), Tick(minimumTick),
                F(minJointLeft), F(minJointRight), B(p3.PhysicalDescentSeen), B(p3.PhysicalBottomSeen),
                B(p3.LegalBottomSeen), B(p3.AscentEstablished), B(p3.PhysicalLockoutSeen),
                B(p3.TerminalContextCovered), p3.TerminalContextStatus.ToString(),
                Tick(record.EventTicks.BottomTick), Tick(metrics.BottomTick),
                Tick(record.EventTicks.AscentEstablishmentTick), Tick(metrics.AscentEstablishmentTick),
                Tick(record.EventTicks.LockoutTick), Tick(record.EventTicks.FailureOnsetTick),
                Tick(record.EventTicks.FailureLatchTick), Tick(failureStart), Tick(failureEnd),
                failureWindowSamples.ToString(CultureInfo.InvariantCulture),
                stickingResolvable ? Tick(metrics.StickingVmax1Tick) : "NA",
                stickingResolvable ? Tick(metrics.StickingVmax2Tick) : "NA",
                stickingResolvable ? F(metrics.StickingIntervalSeconds) : "NA",
                stickingResolvable ? F(stickingDisplacement) : "NA",
                stickingResolvable ? F(metrics.StickingVmax1Mps) : "NA",
                stickingResolvable ? F(metrics.StickingVminMps) : "NA",
                stickingResolvable ? F(metrics.StickingVmax2Mps) : "NA",
                B(supportSamples > 0 && persistentContact),
                F(supportSamples == 0 ? float.NaN : (float)bilateralSamples / supportSamples),
                Tick(firstContactLossTick), F(maxLeftSlip), F(maxRightSlip),
                F(maxLeftSlipSpeed), F(maxRightSlipSpeed), F(maxLeftHip), F(maxRightHip),
                F(maxLeftKnee), F(maxRightKnee), F(maxPelvisDisagreement),
                F(minimumSample == null ? float.NaN : minimumSample.ReferenceWorstDepth),
                F(minimumSample == null ? float.NaN : minimumSample.AppliedWorstDepth),
                F(minimumSample == null ? float.NaN : minimumSample.WorstDepth),
                F(minimumSample == null ? float.NaN : minimumSample.WorstDepth - minimumSample.AppliedWorstDepth),
                "MODELED_DEMAND_NOT_MEASURED_ACTUATOR_SATURATION",
                "ENGINE_SOLVER_DIAGNOSTIC_NOT_DIRECT_DRIVE_TORQUE"
            };
            return values;
        }

        private Sample FindSample(ulong tick)
        {
            for (int index = 0; index < _samples.Count; index++)
                if (_samples[index].Tick == tick) return _samples[index];
            return null;
        }

        private static void AddFoot(List<string> values, SquatFootObservation foot)
        {
            values.Add(B(foot.Availability == SquatTelemetryAvailability.AVAILABLE));
            values.Add(B(foot.IsInContact));
            values.Add(F(foot.ContactCount));
            values.Add(F(foot.CompletedContactCount));
            values.Add(F(foot.SlipAccumulatedMeters));
            values.Add(F(foot.SlipSpeedMetersPerSecond));
        }

        private static void Add(List<string> values, Vector3Value vector)
        {
            values.Add(F(vector.X)); values.Add(F(vector.Y)); values.Add(F(vector.Z));
        }

        private static void Add(List<string> values, Vector3 vector)
        {
            values.Add(F(vector.x)); values.Add(F(vector.y)); values.Add(F(vector.z));
        }

        private static void AddQuaternion(List<string> values, Quaternion quaternion)
        {
            values.Add(F(quaternion.x)); values.Add(F(quaternion.y));
            values.Add(F(quaternion.z)); values.Add(F(quaternion.w));
        }

        private static string EnvironmentValue(string key) =>
            Environment.GetEnvironmentVariable(key) ?? string.Empty;

        private static string B(bool value) => value ? "true" : "false";
        private static string F(float value) => float.IsNaN(value) || float.IsInfinity(value)
            ? "NA" : value.ToString("R", CultureInfo.InvariantCulture);
        private static string F(double value) => double.IsNaN(value) || double.IsInfinity(value)
            ? "NA" : value.ToString("R", CultureInfo.InvariantCulture);
        private static string Tick(ulong value) => value == NA ? "NA" : value.ToString(CultureInfo.InvariantCulture);
        private const ulong NA = SquatAttemptEventTicks.NotAvailable;
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static float MaxFinite(float current, float next) => IsFinite(next)
            ? (!IsFinite(current) ? next : Math.Max(current, next)) : current;

        private static void AppendCsvLine(StringBuilder csv, IList<string> values)
        {
            for (int index = 0; index < values.Count; index++)
            {
                if (index > 0) csv.Append(',');
                string value = values[index] ?? string.Empty;
                if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
                    csv.Append('"').Append(value.Replace("\"", "\"\"")).Append('"');
                else
                    csv.Append(value);
            }
            csv.AppendLine();
        }

        private sealed class Sample
        {
            public int Index;
            public ulong Tick;
            public float LeftDepth = float.NaN;
            public float RightDepth = float.NaN;
            public float WorstDepth = float.NaN;
            public float LeftJointCenterDepth = float.NaN;
            public float RightJointCenterDepth = float.NaN;
            public float ReferenceLeftDepth = float.NaN;
            public float ReferenceRightDepth = float.NaN;
            public float ReferenceWorstDepth = float.NaN;
            public float AppliedLeftDepth = float.NaN;
            public float AppliedRightDepth = float.NaN;
            public float AppliedWorstDepth = float.NaN;
            public float LeftHipAnchorSeparation = float.NaN;
            public float RightHipAnchorSeparation = float.NaN;
            public float LeftKneeAnchorSeparation = float.NaN;
            public float RightKneeAnchorSeparation = float.NaN;
            public float PelvisOriginDisagreement = float.NaN;
            public float LeftSlipAccumulated = float.NaN;
            public float RightSlipAccumulated = float.NaN;
            public float LeftSlipSpeed = float.NaN;
            public float RightSlipSpeed = float.NaN;
            public float BarY = float.NaN;
            public float BarLoadKg = float.NaN;
            public bool BilateralContact;
            public string TargetReconstructionError = string.Empty;
            public readonly List<string> Values = new List<string>(700);

            public Sample() { }
        }
    }
}
