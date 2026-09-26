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
    /// <summary>
    /// Frozen GAM-13 B1 probes. All arms run in a separate Unity process;
    /// this test-only harness changes no production physics or rule defaults.
    /// </summary>
    public sealed class GAM13PhaseB1Tests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int StandingSettleTicks = 500;
        private const int ApproachTicks = 100;
        private const int BottomSettleTicks = 500;
        private const int MaximumSetupTicks = 2200;
        private const int MaximumDynamicTicks = 2200;
        private const int TicksPerYield = 50;
        private const float HipLimitReportCrossing = 0.98f;
        private const float PhaseRatePerSecond = 1f;
        private const string BaselineCommit = "7f29d73424e37254e3b924d50d01cda46b0aa0ae";
        private static readonly string[] JointIds =
        {
            "left_thigh", "right_thigh", "left_shank", "right_shank", "abdomen", "thorax"
        };

        private static readonly PropertyInfo[] PredicateProperties =
            typeof(SquatStartPredicateDiagnostic)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(property => property.Name, StringComparer.Ordinal)
                .ToArray();

        [UnityTest, Explicit("GAM-13 B1-A0: fresh-process 25 kg full-production held canonical bottom.")]
        public IEnumerator GAM13_B1_A0_25KG_HELD_BOTTOM_FRESH_PROCESS() => RunHeldBottom("A0-25kg", 25f);

        [UnityTest, Explicit("GAM-13 B1-A1: fresh-process 60 kg full-production held canonical bottom.")]
        public IEnumerator GAM13_B1_A1_60KG_HELD_BOTTOM_FRESH_PROCESS() => RunHeldBottom("A1-60kg", 60f);

        [UnityTest, Explicit("GAM-13 B1-A2: fresh-process 60 kg canonical full-production dynamic lifecycle.")]
        public IEnumerator GAM13_B1_A2_60KG_DYNAMIC_FRESH_PROCESS() => RunDynamic60Kg();

        [UnityTest, Explicit("GAM-13 B1-B0: fresh-process 60 kg full-production start control.")]
        public IEnumerator GAM13_B1_B0_60KG_PRODUCTION_START_FRESH_PROCESS() => RunStartProbe("B0-60kg", 60f, false, false);

        [UnityTest, Explicit("GAM-13 B1-B1: fresh-process 140 kg full-production start probe.")]
        public IEnumerator GAM13_B1_B1_140KG_PRODUCTION_START_FRESH_PROCESS() => RunStartProbe("B1-140kg-P", 140f, false, false);

        [UnityTest, Explicit("GAM-13 B1-B2: fresh-process 140 kg S0 start probe.")]
        public IEnumerator GAM13_B1_B2_140KG_S0_START_FRESH_PROCESS() => RunStartProbe("B2-140kg-S0", 140f, true, false);

        [UnityTest, Explicit("GAM-13 B1-B3: fresh-process 140 kg B0 start probe.")]
        public IEnumerator GAM13_B1_B3_140KG_B0_START_FRESH_PROCESS() => RunStartProbe("B3-140kg-B0", 140f, false, true);

        [UnityTest, Explicit("GAM-13 B1-B4: fresh-process 140 kg S0+B0 start probe.")]
        public IEnumerator GAM13_B1_B4_140KG_S0_B0_START_FRESH_PROCESS() => RunStartProbe("B4-140kg-S0B0", 140f, true, true);

        private static IEnumerator RunHeldBottom(string arm, float loadKg)
        {
            yield return LoadProductionScene();
            SceneContext scene = CurrentScene();
            scene.Controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = scene.Controller.Adapter;
            var samples = new List<PhysicalSample>(StandingSettleTicks + ApproachTicks + BottomSettleTicks);

            for (int tick = 0; tick < StandingSettleTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                CaptureStep(scene, "STANDING_SETTLE", tick, samples);
                if (tick % TicksPerYield == 0) yield return null;
            }

            for (int tick = 0; tick < ApproachTicks; tick++)
            {
                float phase = (tick + 1f) / ApproachTicks;
                adapter.HoldReferencePhaseForQualification(
                    phase, SquatPhaseDirection.Descent, SquatState.DESCENT, PhaseRatePerSecond);
                CaptureStep(scene, "PRODUCTION_DRIVE_APPROACH", tick, samples);
                if (tick % TicksPerYield == 0) yield return null;
            }

            for (int tick = 0; tick < BottomSettleTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);
                CaptureStep(scene, "BOTTOM_SETTLE", tick, samples);
                if (tick % TicksPerYield == 0) yield return null;
            }

            string output = OutputDirectory(arm);
            WritePhysicalCsv(Path.Combine(output, "measurements.csv"), samples);
            WriteHeldSummary(Path.Combine(output, "summary.md"), arm, loadKg, samples);
            Debug.Log($"GAM13_B1_HELD arm={arm} load_kg={F(loadKg)} ticks={samples.Count} output={output}");
            yield return null;
        }

        private static IEnumerator RunDynamic60Kg()
        {
            yield return LoadProductionScene();
            SceneContext scene = CurrentScene();
            scene.Controller.SetLoad(60f);
            scene.Controller.BeginAttempt();
            var samples = new List<PhysicalSample>(MaximumDynamicTicks);
            bool driveIssued = false;
            int ticks = 0;
            int postRecordTicks = 0;

            while (ticks < MaximumDynamicTicks &&
                   (scene.Controller.AttemptRecord == null || postRecordTicks < 200))
            {
                scene.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                UpdateFootContacts(scene);
                int stageTick = ticks++;
                if (!scene.Controller.ObservationCollector.HasLastSnapshot)
                {
                    if (ticks % TicksPerYield == 0) yield return null;
                    continue;
                }
                SquatObservationSnapshot snapshot = scene.Controller.ObservationCollector.LastSnapshot;
                if (!driveIssued && (scene.Controller.Adapter.State == SquatState.REVERSAL ||
                                     scene.Controller.Adapter.State == SquatState.ASCENT))
                {
                    scene.Runtime.InputBuffer.SetContinuous(
                        IntentAction.Drive,
                        1f,
                        scene.Runtime.CurrentTime.SimulationTimeSeconds +
                        0.25d * SimulationConstants.FixedDeltaTimeSeconds);
                    driveIssued = true;
                }
                samples.Add(Capture(snapshot, scene, "DYNAMIC_LIFECYCLE", stageTick));
                if (scene.Controller.AttemptRecord != null) postRecordTicks++;
                if (ticks % TicksPerYield == 0) yield return null;
            }

            string output = OutputDirectory("A2-60kg-dynamic");
            WritePhysicalCsv(Path.Combine(output, "measurements.csv"), samples);
            WriteDynamicSummary(Path.Combine(output, "summary.md"), scene, samples, ticks, postRecordTicks, driveIssued);
            Debug.Log($"GAM13_B1_DYNAMIC ticks={ticks} post_record_ticks={postRecordTicks} drive_issued={driveIssued} " +
                      $"record={scene.Controller.AttemptRecord != null} output={output}");
            yield return null;
        }

        private static IEnumerator RunStartProbe(string arm, float loadKg, bool spineOff, bool balanceOff)
        {
            yield return LoadProductionScene();
            SceneContext scene = CurrentScene();
            scene.Controller.SetLoad(loadKg);
            scene.Controller.Adapter.Preload.SpineCalibrationEnabled = !spineOff;
            scene.Controller.Adapter.BalanceCorrectionsEnabled = !balanceOff;
            scene.Controller.BeginAttempt();

            var samples = new List<StartSample>(MaximumSetupTicks);
            int diagnosticsSeen = 0;
            bool readinessContradiction = false;
            bool reachedPersistedRun = false;

            for (int tick = 0; tick < MaximumSetupTicks; tick++)
            {
                scene.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                UpdateFootContacts(scene);
                IReadOnlyList<SquatStartPredicateDiagnostic> diagnostics =
                    scene.Controller.AttemptOrchestrator.StartWindowDiagnostics;
                if (diagnostics.Count > diagnosticsSeen && scene.Controller.ObservationCollector.HasLastSnapshot)
                {
                    for (int index = diagnosticsSeen; index < diagnostics.Count; index++)
                    {
                        SquatStartPredicateDiagnostic diagnostic = diagnostics[index];
                        SquatObservationSnapshot snapshot = scene.Controller.ObservationCollector.LastSnapshot;
                        if (snapshot.SimulationTick != diagnostic.SimulationTick)
                            continue;
                        samples.Add(CaptureStart(diagnostic, snapshot, scene));
                        if (diagnostic.ConsecutiveValidRunLength >= diagnostic.RequiredRunLength)
                        {
                            reachedPersistedRun = true;
                            readinessContradiction = IsReadinessContradiction(samples);
                            break;
                        }
                    }
                    diagnosticsSeen = diagnostics.Count;
                }

                // Stop before another production lifecycle tick can issue Squat.
                if (reachedPersistedRun) break;
                if (tick % TicksPerYield == 0) yield return null;
            }

            string output = OutputDirectory(arm);
            WritePredicateCsv(Path.Combine(output, "start-predicate.csv"), samples);
            WriteStartStateCsv(Path.Combine(output, "setup-state.csv"), samples);
            WriteStartSummary(Path.Combine(output, "summary.md"), arm, loadKg, samples,
                reachedPersistedRun, readinessContradiction, spineOff, balanceOff);
            Debug.Log($"GAM13_B1_START arm={arm} samples={samples.Count} persisted={reachedPersistedRun} " +
                      $"readiness_contradiction={readinessContradiction} output={output}");
            yield return null;
        }

        private static IEnumerator LoadProductionScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-13 qualification scene is missing.");
            while (!load.isDone) yield return null;
            yield return null;

            SquatPhysicalPrototypeController controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(bootstrap, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++) yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);

            // Match the existing qualification seam: the harness owns render-frame stepping.
            controller.enabled = false;
            bootstrap.enabled = false;
            yield return null;
        }

        private static SceneContext CurrentScene()
        {
            SquatPhysicalPrototypeController controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            return new SceneContext(controller, bootstrap, rig);
        }

        private static void CaptureStep(SceneContext scene, string stage, int stageTick, List<PhysicalSample> samples)
        {
            scene.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            UpdateFootContacts(scene);
            if (scene.Controller.ObservationCollector.HasLastSnapshot)
                samples.Add(Capture(scene.Controller.ObservationCollector.LastSnapshot, scene, stage, stageTick));
        }

        private static void UpdateFootContacts(SceneContext scene)
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            scene.Controller.LeftFootContact?.PhysicsTickUpdate(dt);
            scene.Controller.RightFootContact?.PhysicsTickUpdate(dt);
        }

        private static PhysicalSample Capture(SquatObservationSnapshot snapshot, SceneContext scene, string stage, int stageTick)
        {
            SquatPhysicalAdapter adapter = scene.Controller.Adapter;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController control = adapter.BalanceController;
            var sample = new PhysicalSample
            {
                Snapshot = snapshot,
                Stage = stage,
                StageTick = stageTick,
                LeftHipLimit = Joint(scene, "left_thigh").PostPhysicsDiagnostic.LimitProximity,
                RightHipLimit = Joint(scene, "right_thigh").PostPhysicsDiagnostic.LimitProximity,
                ComHorizontalSpeed = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude,
                BarSpeed = new Vector3(snapshot.Bar.LinearVelocityWorldMetersPerSecond.X,
                    snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y, snapshot.Bar.LinearVelocityWorldMetersPerSecond.Z).magnitude,
                SupportMargin = MinimumComSupportMargin(snapshot.Support),
                CaptureMargin = balance.CaptureMargin2D,
                CaptureFrontMargin = balance.CaptureMarginFront,
                CaptureRearMargin = balance.CaptureMarginRear,
                LeftSlipSpeed = scene.Controller.LeftFootContact != null ? scene.Controller.LeftFootContact.SlipSpeed : float.NaN,
                RightSlipSpeed = scene.Controller.RightFootContact != null ? scene.Controller.RightFootContact.SlipSpeed : float.NaN,
                LeftSlipAccumulated = scene.Controller.LeftFootContact != null ? scene.Controller.LeftFootContact.SlipAccumulatedM : float.NaN,
                RightSlipAccumulated = scene.Controller.RightFootContact != null ? scene.Controller.RightFootContact.SlipAccumulatedM : float.NaN,
                LeftHipAnchorSeparation = AnchorSeparation(scene, "left_thigh"),
                RightHipAnchorSeparation = AnchorSeparation(scene, "right_thigh"),
                LeftKneeAnchorSeparation = AnchorSeparation(scene, "left_shank"),
                RightKneeAnchorSeparation = AnchorSeparation(scene, "right_shank"),
                PelvisOriginDisagreement = PelvisOriginDisagreement(scene)
            };

            var values = new List<string>(160)
            {
                stage, N(snapshot.SimulationTick), N(stageTick), snapshot.State.ToString(), adapter.Direction.ToString(),
                F(snapshot.Sq), F(snapshot.SimulationTimeSeconds),
                V(snapshot.Bar.PositionWorldMeters), V(snapshot.Bar.LinearVelocityWorldMetersPerSecond),
                V(snapshot.Bar.AngularVelocityBarRadiansPerSecond),
                V(snapshot.PelvisPositionWorldMeters), V(snapshot.PelvisLinearVelocityWorldMetersPerSecond),
                F(snapshot.TrunkWorldPitchRadians), F(snapshot.Depth.LeftDepthM), F(snapshot.Depth.RightDepthM),
                F(snapshot.Depth.WorstSideDepthM), F(adapter.JointCenterDepthDiagnostic.LeftDepthM),
                F(adapter.JointCenterDepthDiagnostic.RightDepthM), B(snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE),
                B(snapshot.Support.HasSupport), N(snapshot.Support.SupportContactCount), F(snapshot.Support.SupportApMinM),
                F(snapshot.Support.SupportApMaxM), F(snapshot.Support.SupportMlMinM), F(snapshot.Support.SupportMlMaxM),
                F(sample.SupportMargin), F(balance.SystemCom.x), F(balance.SystemCom.y), F(balance.SystemCom.z),
                V(balance.SystemComVelocity), F(sample.CaptureMargin), F(sample.CaptureFrontMargin), F(sample.CaptureRearMargin),
                F(balance.CaptureAp), F(balance.CaptureMl),
                B(snapshot.LeftFoot.Availability == SquatTelemetryAvailability.AVAILABLE), B(snapshot.LeftFoot.IsInContact),
                F(sample.LeftSlipSpeed), F(sample.LeftSlipAccumulated),
                B(snapshot.RightFoot.Availability == SquatTelemetryAvailability.AVAILABLE), B(snapshot.RightFoot.IsInContact),
                F(sample.RightSlipSpeed), F(sample.RightSlipAccumulated),
                F(control.AnkleSagittalOffsetRad), F(control.HipSagittalOffsetRad), F(control.TrunkSagittalOffsetRad),
                F(control.RawAnkleAuthorityFraction), F(control.RawAnkleSagittalOffsetRad),
                F(control.AnkleAuthorityFraction), F(control.PostureGuardScale), F(control.HipStrategyBlend),
                F(SaddleSeparation(scene.Controller.Saddle)), B(scene.Controller.Saddle != null && scene.Controller.Saddle.IsAttached),
                B(scene.Controller.Saddle != null && scene.Controller.Saddle.IsBroken),
                F(sample.LeftHipAnchorSeparation), F(sample.RightHipAnchorSeparation),
                F(sample.LeftKneeAnchorSeparation), F(sample.RightKneeAnchorSeparation), F(sample.PelvisOriginDisagreement)
            };
            foreach (string jointId in JointIds) AddJoint(values, snapshot, scene, jointId);
            sample.Csv = string.Join(",", values);
            return sample;
        }

        private static void AddJoint(List<string> values, SquatObservationSnapshot snapshot, SceneContext scene, string jointId)
        {
            PoweredJointController.PoweredJointRuntime joint = Joint(scene, jointId);
            if (joint == null || !joint.HasPostPhysicsDiagnostic)
            {
                for (int index = 0; index < 20; index++) values.Add("NA");
                return;
            }

            PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
            SquatJointObservation observation = JointObservation(snapshot, jointId);
            values.Add(F(PoweredJointController.SignedTwistRadians(diagnostic.RequestedTarget, Vector3.right)));
            AddQuaternion(values, diagnostic.RequestedTarget);
            values.Add(F(PoweredJointController.SignedTwistRadians(diagnostic.AppliedTarget, Vector3.right)));
            AddQuaternion(values, diagnostic.AppliedTarget);
            values.Add(F(observation.ActualAngleRadians));
            values.Add(F(observation.ReferenceAngleRadians));
            values.Add(F(observation.ActualReferenceErrorRadians));
            values.Add(F(diagnostic.ErrorRad.x));
            values.Add(F(diagnostic.LimitProximity));
            values.Add(F(diagnostic.ModeledDemand));
            AddVector(values, diagnostic.SolverTorqueJointSpaceNm);
            values.Add(F(diagnostic.MaximumForceNm));
        }

        private static SquatJointObservation JointObservation(SquatObservationSnapshot snapshot, string jointId)
        {
            switch (jointId)
            {
                case "left_thigh": return snapshot.Joints.LeftHip;
                case "right_thigh": return snapshot.Joints.RightHip;
                case "left_shank": return snapshot.Joints.LeftKnee;
                case "right_shank": return snapshot.Joints.RightKnee;
                case "abdomen": return snapshot.Joints.Abdomen;
                case "thorax": return snapshot.Joints.Thorax;
                default: return SquatJointObservation.Unavailable();
            }
        }

        private static string PredicateHeader() => string.Join(",", PredicateProperties.Select(property => property.Name));

        private static StartSample CaptureStart(SquatStartPredicateDiagnostic diagnostic, SquatObservationSnapshot snapshot, SceneContext scene)
        {
            return new StartSample
            {
                Diagnostic = diagnostic,
                Snapshot = snapshot,
                LeftSlipSpeed = scene.Controller.LeftFootContact != null ? scene.Controller.LeftFootContact.SlipSpeed : float.NaN,
                RightSlipSpeed = scene.Controller.RightFootContact != null ? scene.Controller.RightFootContact.SlipSpeed : float.NaN,
                LeftSlipAccumulated = scene.Controller.LeftFootContact != null ? scene.Controller.LeftFootContact.SlipAccumulatedM : float.NaN,
                RightSlipAccumulated = scene.Controller.RightFootContact != null ? scene.Controller.RightFootContact.SlipAccumulatedM : float.NaN,
                CaptureFrontMargin = scene.Controller.Adapter.Balance.CaptureMarginFront,
                CaptureRearMargin = scene.Controller.Adapter.Balance.CaptureMarginRear
            };
        }

        private static bool IsReadinessContradiction(IReadOnlyList<StartSample> samples)
        {
            if (samples.Count == 0) return false;
            StartSample sample = samples[samples.Count - 1];
            if (!sample.Snapshot.Support.HasSupport ||
                !sample.Snapshot.LeftFoot.IsInContact || !sample.Snapshot.RightFoot.IsInContact ||
                sample.Diagnostic.CaptureMargin2DM < 0f || MinimumComSupportMargin(sample.Snapshot.Support) < 0f)
                return true;

            int baselineCount = Math.Min(100, samples.Count);
            float meanPelvis = 0f, meanBar = 0f, meanAp = 0f, meanMl = 0f;
            for (int index = 0; index < baselineCount; index++)
            {
                StartSample baseline = samples[index];
                meanPelvis += baseline.Snapshot.PelvisPositionWorldMeters.Y;
                meanBar += baseline.Snapshot.Bar.PositionWorldMeters.Y;
                meanAp += baseline.Diagnostic.ComApM;
                meanMl += baseline.Diagnostic.ComMlM;
            }
            meanPelvis /= baselineCount;
            meanBar /= baselineCount;
            meanAp /= baselineCount;
            meanMl /= baselineCount;
            float horizontalDisplacement = Mathf.Sqrt(
                Mathf.Pow(sample.Diagnostic.ComApM - meanAp, 2f) + Mathf.Pow(sample.Diagnostic.ComMlM - meanMl, 2f));
            return horizontalDisplacement >= 0.25f ||
                   meanPelvis - sample.Snapshot.PelvisPositionWorldMeters.Y > 0.15f ||
                   meanBar - sample.Snapshot.Bar.PositionWorldMeters.Y > 0.15f;
        }

        private static void WritePhysicalCsv(string path, IReadOnlyList<PhysicalSample> samples)
        {
            var builder = new StringBuilder(128 + samples.Count * 1400);
            builder.AppendLine(PhysicalHeader());
            for (int index = 0; index < samples.Count; index++) builder.AppendLine(samples[index].Csv);
            File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        }

        private static string PhysicalHeader()
        {
            var headers = new List<string>
            {
                "stage", "simulation_tick", "stage_tick", "adapter_state", "phase_direction", "s_q", "simulation_time_s",
                "bar_position_world_m_xyz", "bar_linear_velocity_world_mps_xyz", "bar_angular_velocity_bar_radps_xyz",
                "pelvis_position_world_m_xyz", "pelvis_linear_velocity_world_mps_xyz", "trunk_world_pitch_rad",
                "surface_rule_depth_left_m", "surface_rule_depth_right_m", "surface_rule_depth_worst_m",
                "joint_center_depth_diagnostic_left_m", "joint_center_depth_diagnostic_right_m",
                "support_available", "support_present", "support_contact_count", "support_ap_min_m", "support_ap_max_m",
                "support_ml_min_m", "support_ml_max_m", "com_to_support_min_margin_m", "system_com_world_x_m",
                "system_com_world_y_m", "system_com_world_z_m", "system_com_velocity_world_mps_xyz",
                "capture_margin_2d_m", "capture_margin_front_m", "capture_margin_rear_m", "capture_ap_m", "capture_ml_m",
                "left_contact_available", "left_contact", "left_slip_speed_mps", "left_slip_accumulated_m",
                "right_contact_available", "right_contact", "right_slip_speed_mps", "right_slip_accumulated_m",
                "balance_ankle_offset_rad", "balance_hip_offset_rad", "balance_trunk_offset_rad",
                "raw_ankle_authority_fraction", "raw_ankle_offset_rad", "applied_ankle_authority_fraction",
                "posture_guard_scale", "hip_strategy_blend",
                "saddle_separation_m", "saddle_attached", "saddle_broken", "left_hip_anchor_separation_m",
                "right_hip_anchor_separation_m", "left_knee_anchor_separation_m", "right_knee_anchor_separation_m",
                "inferred_pelvis_origin_disagreement_m"
            };
            foreach (string jointId in JointIds)
            {
                string prefix = jointId + "_";
                headers.AddRange(new[]
                {
                    prefix + "requested_target_angle_rad", prefix + "requested_target_qx", prefix + "requested_target_qy",
                    prefix + "requested_target_qz", prefix + "requested_target_qw", prefix + "applied_target_angle_rad",
                    prefix + "applied_target_qx", prefix + "applied_target_qy", prefix + "applied_target_qz", prefix + "applied_target_qw",
                    prefix + "actual_angle_rad", prefix + "canonical_reference_angle_rad",
                    prefix + "canonical_reference_error_rad", prefix + "applied_target_error_rad",
                    prefix + "limit_proximity", prefix + "modeled_drive_demand",
                    prefix + "solver_constraint_torque_x_nm", prefix + "solver_constraint_torque_y_nm",
                    prefix + "solver_constraint_torque_z_nm", prefix + "maximum_force_nm"
                });
            }
            return string.Join(",", headers);
        }

        private static void WriteHeldSummary(string path, string arm, float loadKg, IReadOnlyList<PhysicalSample> samples)
        {
            PhysicalSample[] held = samples.Where(sample => sample.Stage == "BOTTOM_SETTLE").ToArray();
            PhysicalSample[] final = held.Skip(Math.Max(0, held.Length - 100)).ToArray();
            var text = new StringBuilder();
            text.AppendLine("MISSION=GAM13_PHASE_B1_A_HELD_BOTTOM");
            text.AppendLine("ARM=" + arm);
            text.AppendLine("LOAD_KG=" + F(loadKg));
            text.AppendLine("BASELINE_COMMIT=" + BaselineCommit);
            text.AppendLine("UNITY_VERSION=" + Application.unityVersion);
            text.AppendLine("UNITY_PROCESS_ID=" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("HOLD_DURATION_S=" + F(BottomSettleTicks * SimulationConstants.FixedDeltaTimeSeconds));
            text.AppendLine("CAPTURED_SAMPLES=" + N(samples.Count));
            text.AppendLine("DEPTH_AUTHORITY=GAM49_SHARED_SURFACE_RULE_LANDMARK_PROVIDER");
            text.AppendLine("JOINT_CENTER_DEPTH_IS_DIAGNOSTIC_ONLY=true");
            text.AppendLine("FINAL_WINDOW_S=1.0");
            text.AppendLine("FINAL_WINDOW_BILATERAL_CONTACT_AND_SUPPORT_FRACTION=" + F(Fraction(final, sample => sample.Snapshot.Support.HasSupport && sample.Snapshot.LeftFoot.IsInContact && sample.Snapshot.RightFoot.IsInContact)));
            bool bilateralSupportAll = final.Length == 100 && final.All(sample => sample.Snapshot.Support.HasSupport && sample.Snapshot.LeftFoot.IsInContact && sample.Snapshot.RightFoot.IsInContact);
            float comSpeedP95 = Percentile95(final.Select(sample => sample.ComHorizontalSpeed));
            text.AppendLine("FINAL_WINDOW_BILATERAL_SUPPORT_ALL_SAMPLES=" + B(bilateralSupportAll));
            text.AppendLine("FINAL_WINDOW_COM_HORIZONTAL_SPEED_P95_MPS=" + F(comSpeedP95));
            text.AppendLine("FINAL_WINDOW_BAR_SPEED_P95_MPS=" + F(Percentile95(final.Select(sample => sample.BarSpeed))));
            text.AppendLine("FINAL_WINDOW_SETTLED=" + B(bilateralSupportAll && comSpeedP95 <= 0.05f));
            text.AppendLine("FINAL_WINDOW_MAX_HIP_LIMIT_PROXIMITY=" + F(Max(final, sample => Mathf.Max(sample.LeftHipLimit, sample.RightHipLimit))));
            text.AppendLine("FINAL_WINDOW_MIN_CAPTURE_MARGIN_M=" + F(Min(final, sample => sample.CaptureMargin)));
            text.AppendLine("FINAL_WINDOW_MIN_COM_SUPPORT_MARGIN_M=" + F(Min(final, sample => sample.SupportMargin)));
            text.AppendLine("MODELED_DEMAND_IS_NOT_MEASURED_ACTUATOR_SATURATION=true");
            text.AppendLine("SOLVER_TORQUE_IS_CONSTRAINT_DIAGNOSTIC_NOT_DRIVE_TORQUE=true");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static void WriteDynamicSummary(
            string path, SceneContext scene, IReadOnlyList<PhysicalSample> samples, int ticks, int postRecordTicks, bool driveIssued)
        {
            SquatAttemptRecord record = scene.Controller.AttemptRecord;
            ulong reversalTick = SquatAttemptEventTicks.NotAvailable;
            ulong limitTick = SquatAttemptEventTicks.NotAvailable;
            ulong supportMarginTick = SquatAttemptEventTicks.NotAvailable;
            ulong footLossTick = SquatAttemptEventTicks.NotAvailable;
            SquatState previous = SquatState.SETUP;
            for (int index = 0; index < samples.Count; index++)
            {
                PhysicalSample sample = samples[index];
                ulong tick = sample.Snapshot.SimulationTick;
                if (reversalTick == SquatAttemptEventTicks.NotAvailable &&
                    sample.Snapshot.State == SquatState.REVERSAL && previous != SquatState.REVERSAL) reversalTick = tick;
                if (limitTick == SquatAttemptEventTicks.NotAvailable &&
                    Mathf.Max(sample.LeftHipLimit, sample.RightHipLimit) >= HipLimitReportCrossing) limitTick = tick;
                if (supportMarginTick == SquatAttemptEventTicks.NotAvailable && sample.SupportMargin <= 0f) supportMarginTick = tick;
                if (footLossTick == SquatAttemptEventTicks.NotAvailable &&
                    (!sample.Snapshot.LeftFoot.IsInContact || !sample.Snapshot.RightFoot.IsInContact)) footLossTick = tick;
                previous = sample.Snapshot.State;
            }

            var text = new StringBuilder();
            text.AppendLine("MISSION=GAM13_PHASE_B1_A2_DYNAMIC_LIFECYCLE");
            text.AppendLine("LOAD_KG=60");
            text.AppendLine("BASELINE_COMMIT=" + BaselineCommit);
            text.AppendLine("UNITY_VERSION=" + Application.unityVersion);
            text.AppendLine("UNITY_PROCESS_ID=" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("TICKS_CAPTURED=" + N(ticks));
            text.AppendLine("POST_RECORD_CAPTURE_TICKS=" + N(postRecordTicks));
            text.AppendLine("DRIVE_INTENT_ISSUED=" + B(driveIssued));
            text.AppendLine("P3_FAILURE_ONSET_TICK=" + Tick(record == null ? SquatAttemptEventTicks.NotAvailable : record.EventTicks.FailureOnsetTick));
            text.AppendLine("P3_FAILURE_LATCH_TICK=" + Tick(record == null ? SquatAttemptEventTicks.NotAvailable : record.EventTicks.FailureLatchTick));
            text.AppendLine("FIRST_REVERSAL_TRANSITION_TICK=" + Tick(reversalTick));
            text.AppendLine("FIRST_HIP_LIMIT_PROXIMITY_GE_0_98_TICK=" + Tick(limitTick));
            text.AppendLine("FIRST_COM_SUPPORT_MARGIN_NONPOSITIVE_TICK=" + Tick(supportMarginTick));
            text.AppendLine("FIRST_BILATERAL_FOOT_CONTACT_LOSS_TICK=" + Tick(footLossTick));
            text.AppendLine("P3_RECORD_CAPTURED=" + B(record != null));
            text.AppendLine("DEPTH_AUTHORITY=GAM49_SHARED_SURFACE_RULE_LANDMARK_PROVIDER");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static void WritePredicateCsv(string path, IReadOnlyList<StartSample> samples)
        {
            var text = new StringBuilder();
            text.AppendLine(PredicateHeader());
            foreach (StartSample sample in samples)
                text.AppendLine(string.Join(",", PredicateProperties.Select(property => Format(property.GetValue(sample.Diagnostic)))));
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static void WriteStartStateCsv(string path, IReadOnlyList<StartSample> samples)
        {
            var text = new StringBuilder();
            text.AppendLine("simulation_tick,adapter_state,phase_direction,s_q,bar_height_m,pelvis_height_m,bar_position_world_m_xyz,bar_velocity_world_mps_xyz," +
                "support_ap_min_m,support_ap_max_m,support_ml_min_m,support_ml_max_m,com_support_min_margin_m,capture_front_margin_m,capture_rear_margin_m," +
                "left_contact,right_contact,left_slip_speed_mps,right_slip_speed_mps,left_slip_accumulated_m,right_slip_accumulated_m,com_ap_m,com_ml_m,capture_ap_m,capture_ml_m");
            foreach (StartSample sample in samples)
            {
                SquatObservationSnapshot s = sample.Snapshot;
                text.AppendLine(string.Join(",", new[]
                {
                    N(s.SimulationTick), s.State.ToString(), s.Direction.ToString(), F(s.Sq),
                    F(s.Bar.PositionWorldMeters.Y), F(s.PelvisPositionWorldMeters.Y), V(s.Bar.PositionWorldMeters),
                    V(s.Bar.LinearVelocityWorldMetersPerSecond), F(s.Support.SupportApMinM), F(s.Support.SupportApMaxM),
                    F(s.Support.SupportMlMinM), F(s.Support.SupportMlMaxM), F(MinimumComSupportMargin(s.Support)),
                    F(sample.CaptureFrontMargin), F(sample.CaptureRearMargin), B(s.LeftFoot.IsInContact), B(s.RightFoot.IsInContact),
                    F(sample.LeftSlipSpeed), F(sample.RightSlipSpeed), F(sample.LeftSlipAccumulated), F(sample.RightSlipAccumulated),
                    F(sample.Diagnostic.ComApM), F(sample.Diagnostic.ComMlM), F(sample.Diagnostic.CaptureApM), F(sample.Diagnostic.CaptureMlM)
                }));
            }
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static void WriteStartSummary(
            string path, string arm, float loadKg, IReadOnlyList<StartSample> samples,
            bool persisted, bool contradiction, bool spineOff, bool balanceOff)
        {
            var text = new StringBuilder();
            text.AppendLine("MISSION=GAM13_PHASE_B1_B_START_CONTROL_DISCRIMINATION");
            text.AppendLine("ARM=" + arm);
            text.AppendLine("LOAD_KG=" + F(loadKg));
            text.AppendLine("BASELINE_COMMIT=" + BaselineCommit);
            text.AppendLine("UNITY_VERSION=" + Application.unityVersion);
            text.AppendLine("UNITY_PROCESS_ID=" + System.Diagnostics.Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("SPINE_CALIBRATION_ENABLED=" + B(!spineOff));
            text.AppendLine("BALANCE_CORRECTIONS_ENABLED=" + B(!balanceOff));
            text.AppendLine("PRODUCTION_DIAGNOSTIC_SOURCE=SquatAttemptOrchestrator.StartWindowDiagnostics");
            text.AppendLine("SETUP_SAMPLES=" + N(samples.Count));
            text.AppendLine("REQUIRED_RUN_LENGTH=" + N(samples.Count == 0 ? 3 : samples[0].Diagnostic.RequiredRunLength));
            text.AppendLine("MAX_CONSECUTIVE_FULL_PREDICATE_RUN=" + N(samples.Count == 0 ? 0 : samples.Max(sample => sample.Diagnostic.ConsecutiveValidRunLength)));
            text.AppendLine("PERSISTED_START_REACHED=" + B(persisted));
            text.AppendLine("START_READINESS_AUTHORITY_CONTRADICTION=" + B(contradiction));
            text.AppendLine("TEST_STOPPED_BEFORE_SQUAT=" + B(persisted));
            text.AppendLine("BEST_ABDOMEN_SIGNED_MARGIN_DEG=" + F(Max(samples, sample => sample.Diagnostic.AbdomenMarginRad) * Mathf.Rad2Deg));
            text.AppendLine("BEST_THORAX_SIGNED_MARGIN_DEG=" + F(Max(samples, sample => sample.Diagnostic.ThoraxMarginRad) * Mathf.Rad2Deg));
            text.AppendLine("ABDOMEN_WITHIN_ACTUAL_PLUS_MINUS_10_DEG_FRACTION=" + F(Fraction(samples, sample => Mathf.Abs(sample.Diagnostic.AbdomenActualAngleRad) <= 10f * Mathf.Deg2Rad)));
            text.AppendLine("THORAX_WITHIN_ACTUAL_PLUS_MINUS_10_DEG_FRACTION=" + F(Fraction(samples, sample => Mathf.Abs(sample.Diagnostic.ThoraxActualAngleRad) <= 10f * Mathf.Deg2Rad)));
            text.AppendLine("BOTH_TRUNKS_WITHIN_ACTUAL_PLUS_MINUS_10_DEG_FRACTION=" + F(Fraction(samples, sample =>
                Mathf.Abs(sample.Diagnostic.AbdomenActualAngleRad) <= 10f * Mathf.Deg2Rad &&
                Mathf.Abs(sample.Diagnostic.ThoraxActualAngleRad) <= 10f * Mathf.Deg2Rad)));
            AppendPredicateFraction(text, samples, "BAR_LINEAR_SPEED_PASS_FRACTION", diagnostic => diagnostic.BarLinearSpeedPass);
            AppendPredicateFraction(text, samples, "BAR_VERTICAL_SPEED_PASS_FRACTION", diagnostic => diagnostic.BarVerticalSpeedPass);
            AppendPredicateFraction(text, samples, "BAR_ANGULAR_SPEED_PASS_FRACTION", diagnostic => diagnostic.BarAngularSpeedPass);
            AppendPredicateFraction(text, samples, "BAR_AVAILABLE_FRACTION", diagnostic => diagnostic.BarAvailable);
            AppendPredicateFraction(text, samples, "SUPPORT_AVAILABLE_FRACTION", diagnostic => diagnostic.SupportAvailable);
            AppendPredicateFraction(text, samples, "SUPPORT_PRESENT_FRACTION", diagnostic => diagnostic.SupportPresent);
            AppendPredicateFraction(text, samples, "LEFT_FOOT_AVAILABLE_FRACTION", diagnostic => diagnostic.LeftFootAvailable);
            AppendPredicateFraction(text, samples, "LEFT_CONTACT_FRACTION", diagnostic => diagnostic.LeftFootContact);
            AppendPredicateFraction(text, samples, "RIGHT_FOOT_AVAILABLE_FRACTION", diagnostic => diagnostic.RightFootAvailable);
            AppendPredicateFraction(text, samples, "RIGHT_CONTACT_FRACTION", diagnostic => diagnostic.RightFootContact);
            AppendPredicateFraction(text, samples, "LEFT_KNEE_PASS_FRACTION", diagnostic => diagnostic.LeftKneePass);
            AppendPredicateFraction(text, samples, "RIGHT_KNEE_PASS_FRACTION", diagnostic => diagnostic.RightKneePass);
            AppendPredicateFraction(text, samples, "LEFT_HIP_PASS_FRACTION", diagnostic => diagnostic.LeftHipPass);
            AppendPredicateFraction(text, samples, "RIGHT_HIP_PASS_FRACTION", diagnostic => diagnostic.RightHipPass);
            AppendPredicateFraction(text, samples, "ABDOMEN_PASS_FRACTION", diagnostic => diagnostic.AbdomenPass);
            AppendPredicateFraction(text, samples, "THORAX_PASS_FRACTION", diagnostic => diagnostic.ThoraxPass);
            AppendPredicateFraction(text, samples, "OVERALL_START_CANDIDATE_FRACTION", diagnostic => diagnostic.OverallStartCandidate);
            text.AppendLine("SUPPORT_DURATION_S=" + F(samples.Count(sample => sample.Diagnostic.SupportPresent) * SimulationConstants.FixedDeltaTimeSeconds));
            text.AppendLine("BILATERAL_CONTACT_DURATION_S=" + F(samples.Count(sample => sample.Diagnostic.LeftFootContact && sample.Diagnostic.RightFootContact) * SimulationConstants.FixedDeltaTimeSeconds));
            text.AppendLine("MIN_CAPTURE_MARGIN_2D_M=" + F(Min(samples, sample => sample.Diagnostic.CaptureMargin2DM)));
            text.AppendLine("MIN_CAPTURE_FRONT_MARGIN_M=" + F(Min(samples, sample => sample.CaptureFrontMargin)));
            text.AppendLine("MIN_CAPTURE_REAR_MARGIN_M=" + F(Min(samples, sample => sample.CaptureRearMargin)));
            text.AppendLine("MIN_COM_SUPPORT_MARGIN_M=" + F(Min(samples, sample => MinimumComSupportMargin(sample.Snapshot.Support))));
            text.AppendLine("MAX_COM_HORIZONTAL_DISPLACEMENT_FROM_FIRST_SAMPLE_M=" + F(MaxComDisplacement(samples)));
            text.AppendLine("COM_AP_DRIFT_INITIAL_TO_FINAL_M=" + F(Drift(samples, true)));
            text.AppendLine("COM_ML_DRIFT_INITIAL_TO_FINAL_M=" + F(Drift(samples, false)));
            text.AppendLine("MAX_FOOT_SLIP_SPEED_MPS=" + F(Max(samples, sample => Mathf.Max(sample.LeftSlipSpeed, sample.RightSlipSpeed))));
            text.AppendLine("MAX_FOOT_SLIP_ACCUMULATED_M=" + F(Max(samples, sample => Mathf.Max(sample.LeftSlipAccumulated, sample.RightSlipAccumulated))));
            text.AppendLine("MIN_POSTURE_GUARD_SCALE=" + F(Min(samples, sample => sample.Diagnostic.PostureGuardScale)));
            text.AppendLine("MAX_RAW_ANKLE_AUTHORITY_FRACTION=" + F(Max(samples, sample => sample.Diagnostic.RawAnkleAuthorityFraction)));
            text.AppendLine("MAX_HIP_AUTHORITY=" + F(Max(samples, sample => sample.Diagnostic.HipAuthority)));
            text.AppendLine("MAX_TRUNK_AUTHORITY=" + F(Max(samples, sample => sample.Diagnostic.TrunkAuthority)));
            text.AppendLine("DEPTH_AUTHORITY=GAM49_SHARED_SURFACE_RULE_LANDMARK_PROVIDER");
            File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        }

        private static void AppendPredicateFraction(StringBuilder text, IReadOnlyList<StartSample> samples, string name, Func<SquatStartPredicateDiagnostic, bool> predicate) =>
            text.AppendLine(name + "=" + F(Fraction(samples, sample => predicate(sample.Diagnostic))));

        private static float MaxComDisplacement(IReadOnlyList<StartSample> samples)
        {
            if (samples.Count == 0) return float.NaN;
            float ap0 = samples[0].Diagnostic.ComApM, ml0 = samples[0].Diagnostic.ComMlM, max = 0f;
            foreach (StartSample sample in samples)
            {
                float ap = sample.Diagnostic.ComApM - ap0, ml = sample.Diagnostic.ComMlM - ml0;
                max = Mathf.Max(max, Mathf.Sqrt(ap * ap + ml * ml));
            }
            return max;
        }

        private static float Drift(IReadOnlyList<StartSample> samples, bool ap)
        {
            if (samples.Count < 2) return float.NaN;
            return ap ? samples[samples.Count - 1].Diagnostic.ComApM - samples[0].Diagnostic.ComApM
                      : samples[samples.Count - 1].Diagnostic.ComMlM - samples[0].Diagnostic.ComMlM;
        }

        private static float MinimumComSupportMargin(SquatSupportObservation support)
        {
            if (!support.HasSupport) return float.NaN;
            return Mathf.Min(support.ComToSupportApFrontMarginM, support.ComToSupportApRearMarginM,
                support.ComToSupportMlRightMarginM, support.ComToSupportMlLeftMarginM);
        }

        private static PoweredJointController.PoweredJointRuntime Joint(SceneContext scene, string jointId) =>
            scene.Rig != null && scene.Rig.PoweredController != null ? scene.Rig.PoweredController.GetJoint(jointId) : null;

        private static float AnchorSeparation(SceneContext scene, string jointId)
        {
            PoweredJointController.PoweredJointRuntime runtime = Joint(scene, jointId);
            ConfigurableJoint joint = runtime != null ? runtime.Joint : null;
            if (joint == null || joint.connectedBody == null) return float.NaN;
            return Vector3.Distance(joint.transform.TransformPoint(joint.anchor),
                joint.connectedBody.transform.TransformPoint(joint.connectedAnchor));
        }

        private static float PelvisOriginDisagreement(SceneContext scene)
        {
            if (!scene.Rig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvis) ||
                pelvis.Body == null || scene.Controller.Adapter.ReferenceCalibration == null ||
                !TryHipAnchor(scene, "left_thigh", out Vector3 leftHip) ||
                !TryHipAnchor(scene, "right_thigh", out Vector3 rightHip)) return float.NaN;
            SquatReferenceRigCalibration calibration = scene.Controller.Adapter.ReferenceCalibration;
            Quaternion pelvisFrame = pelvis.Body.rotation * pelvis.BodyToReferenceBoneRotation *
                Quaternion.Inverse(calibration.Pelvis.BoneFromAnatomicalFrame);
            Vector3 inferredLeft = leftHip - pelvisFrame * calibration.LeftHipOffsetInPelvisFrame;
            Vector3 inferredRight = rightHip - pelvisFrame * calibration.RightHipOffsetInPelvisFrame;
            return Vector3.Distance(inferredLeft, inferredRight);
        }

        private static bool TryHipAnchor(SceneContext scene, string jointId, out Vector3 anchor)
        {
            PoweredJointController.PoweredJointRuntime runtime = Joint(scene, jointId);
            if (runtime == null || runtime.Joint == null) { anchor = Vector3.zero; return false; }
            anchor = runtime.Joint.transform.TransformPoint(runtime.Joint.anchor);
            return true;
        }

        private static float SaddleSeparation(SquatBarSaddle saddle) => saddle != null ? saddle.SaddleSeparationMeters : float.NaN;

        private static void AddQuaternion(List<string> values, Quaternion q)
        {
            values.Add(F(q.x)); values.Add(F(q.y)); values.Add(F(q.z)); values.Add(F(q.w));
        }

        private static void AddVector(List<string> values, Vector3 vector)
        {
            values.Add(F(vector.x)); values.Add(F(vector.y)); values.Add(F(vector.z));
        }

        private static string V(Vector3Value value) => string.Join(";", F(value.X), F(value.Y), F(value.Z));
        private static string V(Vector3 vector) => string.Join(";", F(vector.x), F(vector.y), F(vector.z));
        private static string F(float value) => float.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "NA";
        private static string F(double value) => double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "NA";
        private static string B(bool value) => value ? "true" : "false";
        private static string N(int value) => value.ToString(CultureInfo.InvariantCulture);
        private static string N(ulong value) => value.ToString(CultureInfo.InvariantCulture);
        private static string Tick(ulong value) => value == SquatAttemptEventTicks.NotAvailable ? "NA" : N(value);
        private static string Format(object value) => value is bool b ? B(b) : value is float f ? F(f) : value is double d ? F(d) :
            value is ulong ul ? N(ul) : value is int i ? N(i) : value == null ? "NA" : value.ToString();

        private static float Fraction<T>(IReadOnlyList<T> values, Func<T, bool> predicate) =>
            values.Count == 0 ? float.NaN : (float)values.Count(predicate) / values.Count;

        private static float Percentile95(IEnumerable<float> values)
        {
            float[] finite = values.Where(float.IsFinite).OrderBy(value => value).ToArray();
            return finite.Length == 0 ? float.NaN : finite[Mathf.Clamp(Mathf.CeilToInt(0.95f * finite.Length) - 1, 0, finite.Length - 1)];
        }

        private static float Max<T>(IReadOnlyList<T> values, Func<T, float> selector) =>
            values.Count == 0 ? float.NaN : values.Max(selector);

        private static float Min<T>(IReadOnlyList<T> values, Func<T, float> selector) =>
            values.Count == 0 ? float.NaN : values.Min(selector);

        private static string OutputDirectory(string arm)
        {
            string root = Environment.GetEnvironmentVariable("GAM13_B1_OUTPUT_DIR");
            if (string.IsNullOrWhiteSpace(root))
                root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-13", "phase-b1", "run-20260926", arm));
            Directory.CreateDirectory(root);
            return root;
        }

        private sealed class SceneContext
        {
            public SceneContext(SquatPhysicalPrototypeController controller, FoundationBootstrap bootstrap, PhysicalAthleteRig rig)
            {
                Controller = controller; Bootstrap = bootstrap; Rig = rig; Runtime = bootstrap.Runtime;
            }
            public SquatPhysicalPrototypeController Controller { get; }
            public FoundationBootstrap Bootstrap { get; }
            public PhysicalAthleteRig Rig { get; }
            public FoundationRuntime Runtime { get; }
        }

        private sealed class PhysicalSample
        {
            public SquatObservationSnapshot Snapshot;
            public string Stage;
            public int StageTick;
            public float LeftHipLimit, RightHipLimit, ComHorizontalSpeed, BarSpeed;
            public float SupportMargin, CaptureMargin, CaptureFrontMargin, CaptureRearMargin;
            public float LeftSlipSpeed, RightSlipSpeed, LeftSlipAccumulated, RightSlipAccumulated;
            public float LeftHipAnchorSeparation, RightHipAnchorSeparation, LeftKneeAnchorSeparation, RightKneeAnchorSeparation;
            public float PelvisOriginDisagreement;
            public string Csv;
        }

        private sealed class StartSample
        {
            public SquatStartPredicateDiagnostic Diagnostic;
            public SquatObservationSnapshot Snapshot;
            public float LeftSlipSpeed, RightSlipSpeed, LeftSlipAccumulated, RightSlipAccumulated;
            public float CaptureFrontMargin, CaptureRearMargin;
        }
    }
}
