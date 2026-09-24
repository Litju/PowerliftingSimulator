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
    /// GAM-47 diagnostic experiments. These tests only read copied snapshots,
    /// adapter composition, and post-physics joint diagnostics. The only
    /// control seam used by the held phases freezes reference progression.
    /// </summary>
    public sealed class GAM47DepthRegressionIsolationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const float LoadKg = 25f;
        private const float LegalDepth = -0.005f;
        private const float ApproachRate = 0.30f;
        private const int StandingSettleTicks = 60;
        private const int HeldSettleTicks = 200;
        private const int SettledReportTicks = 50;
        private const int MaximumAttemptTicks = 2200;
        private const int TicksPerYield = 50;

        private static readonly string[] ControlledJoints =
        {
            "left_foot", "right_foot", "left_shank", "right_shank",
            "left_thigh", "right_thigh", "abdomen", "thorax"
        };

        private static readonly PropertyInfo[] StartDiagnosticProperties =
            typeof(SquatStartPredicateDiagnostic)
                .GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .OrderBy(property => property.Name)
                .ToArray();

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;
        private JointFamilyProfile[] _originalProfiles;

        private enum CompositionArm
        {
            Full,
            NoDynamicBalance,
            NominalOnly
        }

        private sealed class HeldResult
        {
            public string Label;
            public float Phase;
            public float SettledMeanWorstSideDepth;
            public float SettledMinimumWorstSideDepth;
            public float SettledMaximumWorstSideDepth;
            public bool SupportRetained;
            public bool FiniteValidControl;
            public bool Legal;
            public float DeepestWorstSideDepth;
            public ulong DeepestTick;
            public float DeepestSq;
            public float BottomBarVelocityMps;
            public float BottomPelvisVelocityMps;
        }

        private readonly struct DeepestDepth
        {
            public DeepestDepth(float left, float right, float worst, ulong tick, float sq)
            {
                Left = left;
                Right = right;
                Worst = worst;
                Tick = tick;
                Sq = sq;
            }

            public float Left { get; }
            public float Right { get; }
            public float Worst { get; }
            public ulong Tick { get; }
            public float Sq { get; }
        }

        private sealed class P3Stages
        {
            public bool PhysicalDescent;
            public bool PhysicalBottom;
            public bool LegalBottom;
            public bool AscentEstablished;
            public bool PhysicalLockout;
            public bool TerminalContextCoverage;
            public SquatFailureTerminalContextStatus TerminalContextStatus;
            public string CompletionPrerequisitesMissing;
        }

        [UnityTest]
        [Explicit("GAM-47 dynamic baseline; run after the frozen plan commit.")]
        public IEnumerator GAM47_DYNAMIC_BASELINE()
        {
            var traceCsv = new StringBuilder();
            AppendDiagnosticHeader(traceCsv);
            _originalProfiles = SnapshotProfiles();
            try
            {
                ConfigurePlant();
                yield return LoadFreshScene(controller =>
                    controller.Adapter.Preload.ExperimentalBiasOverride = GAM43FeedForward.ForVariant("LC1"));

                _controller.SetLoad(LoadKg);
                _controller.BeginAttempt();

                ulong driveTick = SquatAttemptEventTicks.NotAvailable;
                int ticks = 0;
                while (_controller.AttemptRecord == null && ticks < MaximumAttemptTicks)
                {
                    Step(traceCsv, "DYNAMIC_BASELINE", "LIFECYCLE", ticks);
                    ticks++;

                    if (driveTick == SquatAttemptEventTicks.NotAvailable && IsAscentReferenceState(_controller.Adapter.State))
                    {
                        _bootstrap.Runtime.InputBuffer.SetContinuous(
                            IntentAction.Drive,
                            1f,
                            _bootstrap.Runtime.CurrentTime.SimulationTimeSeconds +
                            0.25d * SimulationConstants.FixedDeltaTimeSeconds);
                        driveTick = _bootstrap.Runtime.CurrentTime.Tick + 1ul;
                    }

                    if (ticks % TicksPerYield == 0)
                        yield return null;
                }

                SquatAttemptRecord record = _controller.AttemptRecord;
                Assert.That(record, Is.Not.Null,
                    $"GAM-47 baseline did not finalize within {MaximumAttemptTicks} ticks " +
                    $"(lifecycle={_controller.AttemptLifecycle.State}, adapter={_controller.Adapter.State}, sq={_controller.Adapter.Sq:F3}).");

                WriteEvidence("dynamic-baseline-trace.csv", traceCsv.ToString());
                WriteStartWindowComparison(record);

                DeepestDepth deepest = FindDeepest(record.Trace, record.EventTicks.SquatCommandTick);
                P3Stages p3 = EvaluateP3(record);
                Assert.That(p3.CompletionPrerequisitesMissing, Is.EqualTo("NONE"));
                Assert.That(p3.LegalBottom, Is.False,
                    "The canonical shallow physical attempt should complete P3 without a legal bottom.");
                WriteSupportReport(record);
                WriteP3Report(record, p3);
                WriteDynamicBaselineSummary(record, deepest, p3, driveTick);

                Assert.That(deepest.Worst, Is.GreaterThan(LegalDepth),
                    "The GAM-47 canonical dynamic baseline no longer reproduces the insufficient-depth symptom.");
                Assert.That(HasViolation(record, SquatRuleViolationKind.INSUFFICIENT_DEPTH), Is.True);

                Debug.Log(string.Format(
                    CultureInfo.InvariantCulture,
                    "GAM47_DYNAMIC_BASELINE deepest={0:R} deficit={1:R} tick={2} sq={3:R} p2={4}/{5} p3={6}/{7} missing={8}",
                    deepest.Worst,
                    Mathf.Max(0f, deepest.Worst - LegalDepth),
                    deepest.Tick,
                    deepest.Sq,
                    record.Judgment.EvidenceStatus,
                    record.RuleOutcome,
                    record.FailureResult.EvidenceStatus,
                    record.PhysicalFailureOutcome,
                    p3.CompletionPrerequisitesMissing));
                yield return null;
            }
            finally
            {
                RestorePlant();
            }
        }

        [UnityTest]
        [Explicit("GAM-47 held-phase ladder; run before any composition arms.")]
        public IEnumerator GAM47_HELD_PHASE_LADDER()
        {
            var csv = new StringBuilder();
            AppendDiagnosticHeader(csv);
            var results = new List<HeldResult>();
            _originalProfiles = SnapshotProfiles();
            try
            {
                foreach (float phase in new[] { 0f, 0.25f, 0.55f, 0.80f, 1f })
                    yield return RunHeldCase("HOLD_" + PhaseLabel(phase), phase, CompositionArm.Full, csv, results);

                WriteEvidence("held-phase-ladder.csv", csv.ToString());
                bool bottomLegal = results.Single(result => Mathf.Approximately(result.Phase, 1f)).Legal;
                WriteHeldSummary("held-phase-ladder-summary.md", results, bottomLegal
                    ? "ROOT_DOMAIN=DYNAMIC_TRACKING_TIMING"
                    : "ROOT_DOMAIN=DEEP_PHASE_EQUILIBRIUM_COMPOSITION");

                Assert.That(results.Count, Is.EqualTo(5));
                Debug.Log("GAM47_HELD_LADDER " + (bottomLegal
                    ? "HELD_BOTTOM_LEGAL ROOT_DOMAIN=DYNAMIC_TRACKING_TIMING"
                    : "HELD_BOTTOM_SHALLOW ROOT_DOMAIN=DEEP_PHASE_EQUILIBRIUM_COMPOSITION"));
                yield return null;
            }
            finally
            {
                RestorePlant();
            }
        }

        [UnityTest]
        [Explicit("GAM-47 Decision B composition audit; run only after the held bottom is shallow.")]
        public IEnumerator GAM47_HELD_COMPOSITION_AUDIT()
        {
            var csv = new StringBuilder();
            AppendDiagnosticHeader(csv);
            var results = new List<HeldResult>();
            _originalProfiles = SnapshotProfiles();
            try
            {
                foreach (CompositionArm arm in new[]
                         { CompositionArm.Full, CompositionArm.NoDynamicBalance, CompositionArm.NominalOnly })
                {
                    string label = arm == CompositionArm.Full ? "C0" :
                        arm == CompositionArm.NoDynamicBalance ? "C1" : "C2";
                    yield return RunHeldCase(label, 1f, arm, csv, results);
                }

                WriteEvidence("composition-audit.csv", csv.ToString());
                WriteHeldSummary("composition-audit-summary.md", results,
                    ClassifyComposition(results));

                Assert.That(results.Count, Is.EqualTo(3));
                Debug.Log("GAM47_COMPOSITION " + ClassifyComposition(results));
                yield return null;
            }
            finally
            {
                RestorePlant();
            }
        }

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process dynamic baseline.")]
        public IEnumerator GAM48_DYNAMIC_BASELINE_FRESH_PROCESS() => GAM47_DYNAMIC_BASELINE();

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process HOLD s_q=0.00.")]
        public IEnumerator GAM48_HOLD_0_00_FRESH_PROCESS() =>
            RunStandaloneHeldCase("HOLD_0.00_FULL", 0f, CompositionArm.Full);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process HOLD s_q=0.25.")]
        public IEnumerator GAM48_HOLD_0_25_FRESH_PROCESS() =>
            RunStandaloneHeldCase("HOLD_0.25_FULL", 0.25f, CompositionArm.Full);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process HOLD s_q=0.55.")]
        public IEnumerator GAM48_HOLD_0_55_FRESH_PROCESS() =>
            RunStandaloneHeldCase("HOLD_0.55_FULL", 0.55f, CompositionArm.Full);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process HOLD s_q=0.80.")]
        public IEnumerator GAM48_HOLD_0_80_FRESH_PROCESS() =>
            RunStandaloneHeldCase("HOLD_0.80_FULL", 0.80f, CompositionArm.Full);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process HOLD s_q=1.00.")]
        public IEnumerator GAM48_HOLD_1_00_FULL_FRESH_PROCESS() =>
            RunStandaloneHeldCase("HOLD_1.00_FULL", 1f, CompositionArm.Full);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process C0.")]
        public IEnumerator GAM48_C0_FULL_FRESH_PROCESS() =>
            RunStandaloneHeldCase("C0_FULL", 1f, CompositionArm.Full);

        [UnityTest]
        [Explicit("GAM-48 Gate 4b C0 runtime command-space decomposition; fresh process required.")]
        public IEnumerator GAM48_GATE4B_C0_RUNTIME_COMMAND_SPACE_FRESH_PROCESS() =>
            RunStandaloneHeldCase("C0_FULL", 1f, CompositionArm.Full, captureGate4b: true);

        [UnityTest]
        [Explicit("GAM-48 Gate 4b HOLD_1.00 runtime command-space decomposition; fresh process required.")]
        public IEnumerator GAM48_GATE4B_HOLD_1_00_RUNTIME_COMMAND_SPACE_FRESH_PROCESS() =>
            RunStandaloneHeldCase("HOLD_1.00_FULL", 1f, CompositionArm.Full, captureGate4b: true);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process C1.")]
        public IEnumerator GAM48_C1_NO_DYNAMIC_BALANCE_FRESH_PROCESS() =>
            RunStandaloneHeldCase("C1_NO_DYNAMIC_BALANCE", 1f, CompositionArm.NoDynamicBalance);

        [UnityTest]
        [Explicit("GAM-48 Gate 3 fresh-process C2.")]
        public IEnumerator GAM48_C2_NOMINAL_ONLY_FRESH_PROCESS() =>
            RunStandaloneHeldCase("C2_NOMINAL_ONLY", 1f, CompositionArm.NominalOnly);

        private IEnumerator RunStandaloneHeldCase(
            string label,
            float phase,
            CompositionArm arm,
            bool captureGate4b = false)
        {
            _originalProfiles = SnapshotProfiles();
            try
            {
                var csv = new StringBuilder();
                AppendDiagnosticHeader(csv);
                var results = new List<HeldResult>();
                yield return RunHeldCase(label, phase, arm, csv, results, captureGate4b);
                Assert.That(results.Count, Is.EqualTo(1));
                WriteFreshProcessHeldEvidence(label, csv.ToString(), results[0]);
            }
            finally
            {
                RestorePlant();
            }
        }

        private IEnumerator LoadFreshScene(Action<SquatPhysicalPrototypeController> configure)
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-47 qualification scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);

            _controller.enabled = false;
            _bootstrap.enabled = false;
            configure?.Invoke(_controller);
        }

        private IEnumerator RunHeldCase(
            string label,
            float phase,
            CompositionArm arm,
            StringBuilder csv,
            List<HeldResult> results,
            bool captureGate4b = false)
        {
            ConfigurePlant();
            yield return LoadFreshScene(controller => ConfigureComposition(controller, arm));
            _controller.SetLoad(LoadKg);

            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int tick = 0; tick < StandingSettleTicks; tick++)
            {
                Step(csv, label, "STANDING", tick);
                if (tick % TicksPerYield == 0)
                    yield return null;
            }

            SquatPhaseDirection direction = phase <= 0.0001f
                ? SquatPhaseDirection.None
                : SquatPhaseDirection.Descent;
            SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
            float current = 0f;
            int approachTick = 0;
            while (current < phase - 1e-4f)
            {
                current = Mathf.Min(phase, current + ApproachRate * (float)SimulationConstants.FixedDeltaTimeSeconds);
                adapter.HoldReferencePhaseForQualification(current, direction, state, ApproachRate);
                Step(csv, label, "APPROACH", approachTick++);
                if (approachTick % TicksPerYield == 0)
                    yield return null;
            }

            adapter.HoldReferencePhaseForQualification(phase, direction, state, 0f);
            GAM48Gate4bRecorder gate4b = captureGate4b
                ? new GAM48Gate4bRecorder(adapter)
                : null;
            var held = new List<SquatObservationSnapshot>(HeldSettleTicks);
            for (int tick = 0; tick < HeldSettleTicks; tick++)
            {
                SquatObservationSnapshot snapshot = Step(csv, label, "HOLD", tick);
                held.Add(snapshot);
                if (gate4b != null && tick >= HeldSettleTicks - SettledReportTicks)
                {
                    gate4b.Capture(
                        tick - (HeldSettleTicks - SettledReportTicks),
                        adapter,
                        _rig,
                        snapshot,
                        snapshot.MaximumModeledDemand);
                }
                if (tick % TicksPerYield == 0)
                    yield return null;
            }

            HeldResult result = SummarizeHeld(label, phase, held);
            results.Add(result);
            if (gate4b != null)
            {
                Assert.That(result.SupportRetained, Is.True,
                    "Gate 4b requires supported physical C0/HOLD samples throughout the settled report window.");
                Assert.That(result.FiniteValidControl, Is.True,
                    "Gate 4b requires finite valid control throughout the settled report window.");
                gate4b.Write(label, result.SupportRetained, result.FiniteValidControl);
            }
            yield return null;
        }

        private void ConfigureComposition(SquatPhysicalPrototypeController controller, CompositionArm arm)
        {
            SquatPhysicalAdapter adapter = controller.Adapter;
            adapter.Preload.ExperimentalBiasOverride = arm == CompositionArm.NominalOnly
                ? null
                : GAM43FeedForward.ForVariant("LC1");
            adapter.BalanceCorrectionsEnabled = arm != CompositionArm.NominalOnly &&
                arm != CompositionArm.NoDynamicBalance;

            if (arm == CompositionArm.NominalOnly)
            {
                adapter.Preload.Enabled = false;
                adapter.Preload.SpineCalibrationEnabled = false;
            }
        }

        private void ConfigurePlant()
        {
            RestoreProfiles(_originalProfiles);
            ApplyImpedanceFactor(5f);
            SquatBarSaddle.ExperimentalOverride =
                new SquatBarSaddle.ExperimentalConfiguration(50000f, 3000f, 0.10f);
        }

        private void RestorePlant()
        {
            if (_originalProfiles != null)
                RestoreProfiles(_originalProfiles);
            SquatBarSaddle.ExperimentalOverride = null;
            _originalProfiles = null;
        }

        private static JointFamilyProfile[] SnapshotProfiles() =>
            (JointFamilyProfile[])typeof(PoweredJointController)
                .GetField("Profiles", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static void RestoreProfiles(JointFamilyProfile[] original)
        {
            if (original == null)
                return;
            JointFamilyProfile[] live = SnapshotProfiles();
            for (int index = 0; index < live.Length; index++)
                live[index] = original[index];
        }

        private static void ApplyImpedanceFactor(float factor)
        {
            JointFamilyProfile[] live = SnapshotProfiles();
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

        private SquatObservationSnapshot Step(StringBuilder csv, string label, string window, int localTick)
        {
            Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            _controller.LeftFootContact?.PhysicsTickUpdate(dt);
            _controller.RightFootContact?.PhysicsTickUpdate(dt);
            SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
            AppendDiagnosticRow(csv, label, window, localTick, snapshot, _controller.Adapter, _rig);
            return snapshot;
        }

        private static void AppendDiagnosticHeader(StringBuilder builder)
        {
            var columns = new List<string>
            {
                "case", "window", "local_tick", "tick", "time_s", "adapter_state", "direction", "s_q",
                "bar_y_m", "bar_vy_mps", "bar_speed_mps", "pelvis_y_m", "pelvis_vy_mps",
                "com_x_m", "com_y_m", "com_z_m", "com_vx_mps", "com_vy_mps", "com_vz_mps",
                "capture_ap_m", "capture_ml_m", "support_ap_min_m", "support_ap_max_m", "support_ml_min_m",
                "support_ml_max_m", "support_has_contact", "support_contact_count", "left_foot_contact",
                "right_foot_contact", "left_slip_accumulated_m", "right_slip_accumulated_m",
                "left_slip_speed_mps", "right_slip_speed_mps", "left_hip_crease_y_m", "right_hip_crease_y_m",
                "left_knee_top_y_m", "right_knee_top_y_m", "left_depth_m", "right_depth_m",
                "worst_side_depth_m", "legal_depth", "raw_ankle_authority", "applied_ankle_authority",
                "hip_strategy_blend", "hip_balance_offset_rad", "trunk_balance_offset_rad", "balance_saturated",
                "modeled_drive_demand_high", "maximum_modeled_drive_demand", "canonical_posture_error_rad",
                "target_actual_deflection_rad", "canonical_posture_worst_joint", "saddle_attached",
                "saddle_broken", "saddle_separation_m"
            };

            foreach (string joint in ControlledJoints)
            {
                columns.Add(joint + "_nominal_qx");
                columns.Add(joint + "_nominal_qy");
                columns.Add(joint + "_nominal_qz");
                columns.Add(joint + "_nominal_qw");
                columns.Add(joint + "_preload_qx");
                columns.Add(joint + "_preload_qy");
                columns.Add(joint + "_preload_qz");
                columns.Add(joint + "_preload_qw");
                columns.Add(joint + "_balance_qx");
                columns.Add(joint + "_balance_qy");
                columns.Add(joint + "_balance_qz");
                columns.Add(joint + "_balance_qw");
                columns.Add(joint + "_final_qx");
                columns.Add(joint + "_final_qy");
                columns.Add(joint + "_final_qz");
                columns.Add(joint + "_final_qw");
                columns.Add(joint + "_applied_qx");
                columns.Add(joint + "_applied_qy");
                columns.Add(joint + "_applied_qz");
                columns.Add(joint + "_applied_qw");
                columns.Add(joint + "_actual_qx");
                columns.Add(joint + "_actual_qy");
                columns.Add(joint + "_actual_qz");
                columns.Add(joint + "_actual_qw");
                columns.Add(joint + "_target_actual_deflection_rad");
                columns.Add(joint + "_actual_canonical_error_rad");
                columns.Add(joint + "_solver_error_x_rad");
                columns.Add(joint + "_modeled_drive_demand");
                columns.Add(joint + "_maximum_force_nm");
                columns.Add(joint + "_limit_proximity");
                columns.Add(joint + "_activation");
                columns.Add(joint + "_capacity_scale");
            }

            builder.AppendLine(string.Join(",", columns));
        }

        private static void AppendDiagnosticRow(
            StringBuilder builder,
            string label,
            string window,
            int localTick,
            SquatObservationSnapshot snapshot,
            SquatPhysicalAdapter adapter,
            PhysicalAthleteRig rig)
        {
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController balanceControl = adapter.BalanceController;
            var values = new List<string>
            {
                label,
                window,
                localTick.ToString(CultureInfo.InvariantCulture),
                snapshot.SimulationTick.ToString(CultureInfo.InvariantCulture),
                F(snapshot.SimulationTimeSeconds),
                snapshot.State.ToString(),
                snapshot.Direction.ToString(),
                F(snapshot.Sq),
                F(snapshot.Bar.IsAvailable ? snapshot.Bar.PositionWorldMeters.Y : float.NaN),
                F(snapshot.Bar.IsAvailable ? snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y : float.NaN),
                F(snapshot.Bar.IsAvailable ? snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length : float.NaN),
                F(snapshot.PelvisPositionWorldMeters.Y),
                F(snapshot.PelvisLinearVelocityWorldMetersPerSecond.Y),
                F(snapshot.Support.SystemComWorldMeters.X),
                F(snapshot.Support.SystemComWorldMeters.Y),
                F(snapshot.Support.SystemComWorldMeters.Z),
                F(snapshot.Support.SystemComVelocityWorldMetersPerSecond.X),
                F(snapshot.Support.SystemComVelocityWorldMetersPerSecond.Y),
                F(snapshot.Support.SystemComVelocityWorldMetersPerSecond.Z),
                F(balance.CaptureAp),
                F(balance.CaptureMl),
                F(snapshot.Support.SupportApMinM),
                F(snapshot.Support.SupportApMaxM),
                F(snapshot.Support.SupportMlMinM),
                F(snapshot.Support.SupportMlMaxM),
                B(snapshot.Support.HasSupport),
                snapshot.Support.SupportContactCount.ToString(CultureInfo.InvariantCulture),
                B(snapshot.LeftFoot.IsInContact),
                B(snapshot.RightFoot.IsInContact),
                F(snapshot.LeftFoot.SlipAccumulatedMeters),
                F(snapshot.RightFoot.SlipAccumulatedMeters),
                F(snapshot.LeftFoot.SlipSpeedMetersPerSecond),
                F(snapshot.RightFoot.SlipSpeedMetersPerSecond),
                F(snapshot.Depth.LeftHipCreaseY),
                F(snapshot.Depth.RightHipCreaseY),
                F(snapshot.Depth.LeftKneeTopY),
                F(snapshot.Depth.RightKneeTopY),
                F(snapshot.Depth.LeftDepthM),
                F(snapshot.Depth.RightDepthM),
                F(snapshot.Depth.WorstSideDepthM),
                B(snapshot.Depth.WorstSideDepthM <= LegalDepth),
                F(balanceControl.RawAnkleAuthorityFraction),
                F(balanceControl.AnkleAuthorityFraction),
                F(balanceControl.HipStrategyBlend),
                F(balanceControl.HipSagittalOffsetRad),
                F(balanceControl.TrunkSagittalOffsetRad),
                B(balanceControl.IsAnkleOffsetSaturated),
                B(adapter.IsDriveSaturated),
                F(snapshot.MaximumModeledDemand),
                F(adapter.CanonicalPostureErrorRad),
                F(adapter.TargetActualDeflectionRad),
                adapter.CanonicalPostureWorstJoint,
                B(snapshot.Bar.SaddleAttached),
                B(snapshot.Bar.SaddleBroken),
                F(snapshot.Bar.SaddleSeparationMeters)
            };

            PoweredJointController powered = rig.PoweredController;
            foreach (string jointId in ControlledJoints)
            {
                adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition);
                PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
                if (joint == null || !joint.HasPostPhysicsDiagnostic)
                {
                    AddUnavailableJoint(values);
                    continue;
                }

                PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                AddQuaternion(values, composition.Nominal);
                AddQuaternion(values, composition.GravityBias);
                AddQuaternion(values, composition.BalanceOffset);
                AddQuaternion(values, composition.Final);
                AddQuaternion(values, diagnostic.AppliedTarget);
                AddQuaternion(values, diagnostic.ActualRelative);
                values.Add(F(Quaternion.Angle(composition.Final, diagnostic.ActualRelative) * Mathf.Deg2Rad));
                values.Add(F(Quaternion.Angle(composition.Nominal, diagnostic.ActualRelative) * Mathf.Deg2Rad));
                values.Add(F(diagnostic.ErrorRad.x));
                values.Add(F(diagnostic.ModeledDemand));
                values.Add(F(diagnostic.MaximumForceNm));
                values.Add(F(diagnostic.LimitProximity));
                values.Add(F(diagnostic.Activation));
                values.Add(F(diagnostic.CapacityScale));
            }

            builder.AppendLine(string.Join(",", values));
        }

        private static void AddUnavailableJoint(List<string> values)
        {
            for (int index = 0; index < 32; index++)
                values.Add("NA");
        }

        private static void AddQuaternion(List<string> values, Quaternion value)
        {
            values.Add(F(value.x));
            values.Add(F(value.y));
            values.Add(F(value.z));
            values.Add(F(value.w));
        }

        private static HeldResult SummarizeHeld(
            string label,
            float phase,
            IReadOnlyList<SquatObservationSnapshot> samples)
        {
            int start = Mathf.Max(0, samples.Count - SettledReportTicks);
            IReadOnlyList<SquatObservationSnapshot> settled = samples.Skip(start).ToArray();
            float meanDepth = settled.Count == 0 ? float.NaN : settled.Average(sample => sample.Depth.WorstSideDepthM);
            float minDepth = settled.Count == 0 ? float.NaN : settled.Min(sample => sample.Depth.WorstSideDepthM);
            float maxDepth = settled.Count == 0 ? float.NaN : settled.Max(sample => sample.Depth.WorstSideDepthM);
            bool support = settled.Count == SettledReportTicks && settled.All(HasRetainedSupport);
            bool finite = settled.Count == SettledReportTicks && settled.All(HasFiniteValidControl);
            DeepestDepth deepest = FindDeepest(samples);
            float barVelocity = settled.Count == 0
                ? float.NaN
                : settled.Max(sample => sample.Bar.IsAvailable
                    ? Mathf.Abs(sample.Bar.LinearVelocityWorldMetersPerSecond.Y)
                    : float.PositiveInfinity);
            float pelvisVelocity = settled.Count == 0
                ? float.NaN
                : settled.Max(sample => Mathf.Abs(sample.PelvisLinearVelocityWorldMetersPerSecond.Y));

            return new HeldResult
            {
                Label = label,
                Phase = phase,
                SettledMeanWorstSideDepth = meanDepth,
                SettledMinimumWorstSideDepth = minDepth,
                SettledMaximumWorstSideDepth = maxDepth,
                SupportRetained = support,
                FiniteValidControl = finite,
                Legal = SquatTelemetryValue.IsFinite(meanDepth) && meanDepth <= LegalDepth && support && finite,
                DeepestWorstSideDepth = deepest.Worst,
                DeepestTick = deepest.Tick,
                DeepestSq = deepest.Sq,
                BottomBarVelocityMps = barVelocity,
                BottomPelvisVelocityMps = pelvisVelocity
            };
        }

        private static void WriteHeldSummary(string fileName, IReadOnlyList<HeldResult> results, string classification)
        {
            var builder = new StringBuilder();
            builder.AppendLine("GAM47_HELD_PHASE_RESULT");
            builder.AppendLine(classification);
            builder.AppendLine("settle_window_ticks=200");
            builder.AppendLine("settled_report_ticks=50");
            builder.AppendLine("phase,label,settled_mean_worst_depth_m,settled_min_worst_depth_m,settled_max_worst_depth_m,support_retained,finite_valid_control,legal,deepest_worst_depth_m,deepest_tick,deepest_sq,bar_velocity_near_bottom_mps,pelvis_velocity_near_bottom_mps");
            foreach (HeldResult result in results)
            {
                builder.AppendLine(string.Join(",",
                    F(result.Phase),
                    result.Label,
                    F(result.SettledMeanWorstSideDepth),
                    F(result.SettledMinimumWorstSideDepth),
                    F(result.SettledMaximumWorstSideDepth),
                    B(result.SupportRetained),
                    B(result.FiniteValidControl),
                    B(result.Legal),
                    F(result.DeepestWorstSideDepth),
                    result.DeepestTick.ToString(CultureInfo.InvariantCulture),
                    F(result.DeepestSq),
                    F(result.BottomBarVelocityMps),
                    F(result.BottomPelvisVelocityMps)));
            }
            WriteEvidence(fileName, builder.ToString());
        }

        private static string ClassifyComposition(IReadOnlyList<HeldResult> results)
        {
            HeldResult c0 = results.Single(result => result.Label == "C0");
            HeldResult c1 = results.Single(result => result.Label == "C1");
            HeldResult c2 = results.Single(result => result.Label == "C2");
            if (c1.Legal && !c0.Legal)
                return "ESTABLISHED_CAUSE=DYNAMIC_BALANCE_DEPTH_BIAS";
            if (c2.Legal && !c1.Legal)
                return "ESTABLISHED_CAUSE=EQUILIBRIUM_PRELOAD_DEPTH_BIAS";
            if (!c2.Legal)
                return "SUPPORTED_BLOCKER=NOMINAL_REFERENCE_OR_TRACKING_MAPPING";
            return "ESTABLISHED_CAUSE=NONE_OF_PREDECLARED_ARMS";
        }

        private static DeepestDepth FindDeepest(SquatTrace trace, ulong firstTick)
        {
            int start = 0;
            if (firstTick != SquatAttemptEventTicks.NotAvailable)
            {
                while (start < trace.Count && trace[start].SimulationTick < firstTick)
                    start++;
            }
            return FindDeepest(trace, start, trace.Count);
        }

        private static DeepestDepth FindDeepest(IReadOnlyList<SquatObservationSnapshot> samples)
        {
            float worst = float.PositiveInfinity;
            float left = float.NaN;
            float right = float.NaN;
            ulong tick = SquatAttemptEventTicks.NotAvailable;
            float sq = float.NaN;
            for (int index = 0; index < samples.Count; index++)
            {
                SquatObservationSnapshot sample = samples[index];
                if (!SquatTelemetryValue.IsFinite(sample.Depth.WorstSideDepthM) || sample.Depth.WorstSideDepthM >= worst)
                    continue;
                worst = sample.Depth.WorstSideDepthM;
                left = sample.Depth.LeftDepthM;
                right = sample.Depth.RightDepthM;
                tick = sample.SimulationTick;
                sq = sample.Sq;
            }
            return new DeepestDepth(left, right, worst, tick, sq);
        }

        private static DeepestDepth FindDeepest(SquatTrace trace, int start, int end)
        {
            float worst = float.PositiveInfinity;
            float left = float.NaN;
            float right = float.NaN;
            ulong tick = SquatAttemptEventTicks.NotAvailable;
            float sq = float.NaN;
            for (int index = start; index < end; index++)
            {
                SquatObservationSnapshot sample = trace[index];
                if (!SquatTelemetryValue.IsFinite(sample.Depth.WorstSideDepthM) || sample.Depth.WorstSideDepthM >= worst)
                    continue;
                worst = sample.Depth.WorstSideDepthM;
                left = sample.Depth.LeftDepthM;
                right = sample.Depth.RightDepthM;
                tick = sample.SimulationTick;
                sq = sample.Sq;
            }
            return new DeepestDepth(left, right, worst, tick, sq);
        }

        private static P3Stages EvaluateP3(SquatAttemptRecord record)
        {
            var detector = new SquatFailureDetector();
            detector.Evaluate(
                record.Trace,
                SquatFailureCompletionContext.Terminal(
                    record.TerminalTick,
                    record.TerminalTimeSeconds,
                    record.TerminalReason));

            var missing = new List<string>();
            if (!detector.PhysicalDescentSeen) missing.Add("PHYSICAL_DESCENT");
            if (!detector.PhysicalBottomSeen) missing.Add("PHYSICAL_BOTTOM");
            if (!detector.AscentEstablished) missing.Add("ASCENT_ESTABLISHED");
            if (!detector.PhysicalLockoutSeen) missing.Add("PHYSICAL_LOCKOUT");

            return new P3Stages
            {
                PhysicalDescent = detector.PhysicalDescentSeen,
                PhysicalBottom = detector.PhysicalBottomSeen,
                LegalBottom = detector.LegalBottomSeen,
                AscentEstablished = detector.AscentEstablished,
                PhysicalLockout = detector.PhysicalLockoutSeen,
                TerminalContextCoverage = detector.TerminalContextCovered,
                TerminalContextStatus = detector.TerminalContextStatus,
                CompletionPrerequisitesMissing = missing.Count == 0 ? "NONE" : string.Join("|", missing)
            };
        }

        private static bool HasRetainedSupport(SquatObservationSnapshot sample) =>
            sample.Support.HasSupport && sample.LeftFoot.IsInContact && sample.RightFoot.IsInContact;

        private static bool HasFiniteValidControl(SquatObservationSnapshot sample)
        {
            SquatTelemetryQualityFlags required = SquatTelemetryQualityFlags.JOINTS_AVAILABLE |
                SquatTelemetryQualityFlags.DRIVE_DIAGNOSTICS_AVAILABLE |
                SquatTelemetryQualityFlags.DEPTH_LANDMARKS_AVAILABLE;
            return (sample.Quality & required) == required &&
                SquatTelemetryValue.IsFinite(sample.MaximumModeledDemand) &&
                sample.Joints.LeftKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                sample.Joints.RightKnee.JointAvailability == SquatTelemetryAvailability.AVAILABLE;
        }

        private static bool IsAscentReferenceState(SquatState state) =>
            state == SquatState.REVERSAL ||
            state == SquatState.ASCENT ||
            state == SquatState.STICKING ||
            state == SquatState.LOCKOUT ||
            state == SquatState.RACK_COMMAND ||
            state == SquatState.RERACK;

        private static bool HasViolation(SquatAttemptRecord record, SquatRuleViolationKind kind)
        {
            for (int index = 0; index < record.Judgment.Violations.Count; index++)
                if (record.Judgment.Violations[index].Kind == kind)
                    return true;
            return false;
        }

        private static void WriteDynamicBaselineSummary(
            SquatAttemptRecord record,
            DeepestDepth deepest,
            P3Stages p3,
            ulong driveTick)
        {
            var builder = new StringBuilder();
            builder.AppendLine("MISSION=GAM47_25KG_DEPTH_REGRESSION_ISOLATION");
            builder.AppendLine("PHASE=1_DYNAMIC_BASELINE");
            builder.AppendLine("ARCHITECTURE=5x+LC1+F2_spine+S1");
            builder.AppendLine("LOAD_KG=25");
            builder.AppendLine("DYNAMIC_DEEPEST_LEFT_DEPTH=" + F(deepest.Left));
            builder.AppendLine("DYNAMIC_DEEPEST_RIGHT_DEPTH=" + F(deepest.Right));
            builder.AppendLine("DYNAMIC_DEEPEST_DEPTH=" + F(deepest.Worst));
            builder.AppendLine("DYNAMIC_DEPTH_DEFICIT=" + F(Mathf.Max(0f, deepest.Worst - LegalDepth)));
            builder.AppendLine("DYNAMIC_DEEPEST_TICK=" + deepest.Tick.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("DYNAMIC_SQ_AT_DEEPEST=" + F(deepest.Sq));
            builder.AppendLine("P2_START_RESULT=" + record.Judgment.EvidenceStatus + "/" + record.RuleOutcome);
            builder.AppendLine("P2_VIOLATIONS=" + Violations(record));
            ulong startSamples = record.StartWindowBeginTick == SquatAttemptEventTicks.NotAvailable ||
                record.StartWindowEndTick == SquatAttemptEventTicks.NotAvailable
                ? 0ul
                : record.StartWindowEndTick - record.StartWindowBeginTick + 1ul;
            builder.AppendLine("P2_START_WINDOW_SAMPLES=" + startSamples.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("SUPPORT_VIOLATION_SOURCE_FILE=support-violation-source.md");
            builder.AppendLine("P3_PHYSICAL_DESCENT=" + B(p3.PhysicalDescent));
            builder.AppendLine("P3_PHYSICAL_BOTTOM=" + B(p3.PhysicalBottom));
            builder.AppendLine("P3_LEGAL_BOTTOM_SEEN=" + B(p3.LegalBottom));
            builder.AppendLine("P3_ASCENT_ESTABLISHED=" + B(p3.AscentEstablished));
            builder.AppendLine("P3_PHYSICAL_LOCKOUT=" + B(p3.PhysicalLockout));
            builder.AppendLine("P3_TERMINAL_CONTEXT=" + p3.TerminalContextStatus);
            builder.AppendLine("P3_TERMINAL_CONTEXT_COVERAGE=" + B(p3.TerminalContextCoverage));
            builder.AppendLine("P3_TERMINAL_CONTEXT_MISSING=" + B(!p3.TerminalContextCoverage));
            builder.AppendLine("P3_COMPLETION_PREREQUISITES_MISSING=" + p3.CompletionPrerequisitesMissing);
            builder.AppendLine("DRIVE_INTENT_TICK=" + driveTick.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("CLAIM_CEILING=ENGINE_RUNTIME_OBSERVATION_AND_GAME_DERIVED_PROXY");
            WriteEvidence("dynamic-baseline-summary.md", builder.ToString());
        }

        private static void WriteStartWindowComparison(SquatAttemptRecord record)
        {
            var builder = new StringBuilder();
            var columns = new List<string> { "recorded_pre_squat", "p2_evaluated" };
            columns.AddRange(StartDiagnosticProperties.Select(property => property.Name));
            builder.AppendLine(string.Join(",", columns));
            ulong command = record.EventTicks.SquatCommandTick;
            ulong startBegin = command - (ulong)SquatRuleToleranceSet.Default.StartPositionPersistenceTicks;
            ulong startEnd = command - 1ul;
            foreach (SquatStartPredicateDiagnostic diagnostic in record.Trace.Count == 0
                         ? Array.Empty<SquatStartPredicateDiagnostic>()
                         : FindStartDiagnostics(record))
            {
                bool recorded = ContainsTick(record.Trace, diagnostic.SimulationTick);
                bool evaluated = diagnostic.SimulationTick >= startBegin && diagnostic.SimulationTick <= startEnd;
                var values = new List<string> { B(recorded), B(evaluated) };
                values.AddRange(StartDiagnosticProperties.Select(property => Format(property.GetValue(diagnostic))));
                builder.AppendLine(string.Join(",", values));
            }
            WriteEvidence("p2-start-window-comparison.csv", builder.ToString());
        }

        private static IReadOnlyList<SquatStartPredicateDiagnostic> FindStartDiagnostics(SquatAttemptRecord record)
        {
            // The orchestrator owns this list; the current record's trace is
            // the exact P2 evidence window. The caller writes the list before
            // the next scene can replace it.
            return UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>()
                .AttemptOrchestrator.StartWindowDiagnostics;
        }

        private static void WriteSupportReport(SquatAttemptRecord record)
        {
            int commandIndex = FindIndexAtTick(record.Trace, record.EventTicks.SquatCommandTick);
            float leftBaseline = record.Trace[commandIndex].LeftFoot.SlipAccumulatedMeters;
            float rightBaseline = record.Trace[commandIndex].RightFoot.SlipAccumulatedMeters;
            float maxAccumulated = 0f;
            float maxSpeed = 0f;
            bool actualLoss = false;
            ulong actualLossTick = SquatAttemptEventTicks.NotAvailable;
            ulong accumulatedTick = SquatAttemptEventTicks.NotAvailable;
            ulong speedTick = SquatAttemptEventTicks.NotAvailable;
            SquatRuleToleranceSet tolerances = SquatRuleToleranceSet.Default;
            for (int index = commandIndex; index < record.Trace.Count; index++)
            {
                SquatObservationSnapshot sample = record.Trace[index];
                if (!HasRetainedSupport(sample) && !actualLoss)
                {
                    actualLoss = true;
                    actualLossTick = sample.SimulationTick;
                }
                float accumulated = Mathf.Max(
                    sample.LeftFoot.SlipAccumulatedMeters - leftBaseline,
                    sample.RightFoot.SlipAccumulatedMeters - rightBaseline);
                float speed = Mathf.Max(
                    sample.LeftFoot.SlipSpeedMetersPerSecond,
                    sample.RightFoot.SlipSpeedMetersPerSecond);
                if (accumulated > maxAccumulated)
                {
                    maxAccumulated = accumulated;
                    accumulatedTick = sample.SimulationTick;
                }
                if (speed > maxSpeed)
                {
                    maxSpeed = speed;
                    speedTick = sample.SimulationTick;
                }
            }

            var sources = new List<string>();
            if (actualLoss) sources.Add("ACTUAL_SUPPORT_LOSS");
            if (maxAccumulated > tolerances.SupportSlipToleranceM) sources.Add("ACCUMULATED_SLIP");
            if (maxSpeed > tolerances.SupportSlipSpeedMps) sources.Add("SLIP_SPEED");
            var builder = new StringBuilder();
            builder.AppendLine("SUPPORT_VIOLATION_SOURCE=" + (sources.Count == 0 ? "NONE" : string.Join("|", sources)));
            builder.AppendLine("ACTUAL_SUPPORT_LOSS=" + B(actualLoss));
            builder.AppendLine("ACTUAL_SUPPORT_LOSS_TICK=" + actualLossTick.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("MAX_ACCUMULATED_SLIP_M=" + F(maxAccumulated));
            builder.AppendLine("ACCUMULATED_SLIP_TICK=" + accumulatedTick.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("SUPPORT_SLIP_TOLERANCE_M=" + F(tolerances.SupportSlipToleranceM));
            builder.AppendLine("MAX_SLIP_SPEED_MPS=" + F(maxSpeed));
            builder.AppendLine("SLIP_SPEED_TICK=" + speedTick.ToString(CultureInfo.InvariantCulture));
            builder.AppendLine("SUPPORT_SLIP_SPEED_TOLERANCE_MPS=" + F(tolerances.SupportSlipSpeedMps));
            builder.AppendLine("RULE_RECORD=" + ViolationDetails(record, SquatRuleViolationKind.SUPPORT_VIOLATION));
            builder.AppendLine("CLAIM_CEILING=SUPPORT_SLIP_GAME_PROXY_NOT_FORCE_PLATE_BIOMECHANICS");
            WriteEvidence("support-violation-source.md", builder.ToString());
        }

        private static void WriteP3Report(SquatAttemptRecord record, P3Stages p3)
        {
            var builder = new StringBuilder();
            builder.AppendLine("P3_PHYSICAL_DESCENT=" + B(p3.PhysicalDescent));
            builder.AppendLine("P3_PHYSICAL_BOTTOM=" + B(p3.PhysicalBottom));
            builder.AppendLine("P3_LEGAL_BOTTOM_SEEN=" + B(p3.LegalBottom));
            builder.AppendLine("P3_ASCENT_ESTABLISHED=" + B(p3.AscentEstablished));
            builder.AppendLine("P3_PHYSICAL_LOCKOUT=" + B(p3.PhysicalLockout));
            builder.AppendLine("P3_TERMINAL_CONTEXT=" + p3.TerminalContextStatus);
            builder.AppendLine("P3_TERMINAL_CONTEXT_COVERAGE=" + B(p3.TerminalContextCoverage));
            builder.AppendLine("P3_TERMINAL_CONTEXT_MISSING=" + B(!p3.TerminalContextCoverage));
            builder.AppendLine("P3_COMPLETION_PREREQUISITES_MISSING=" + p3.CompletionPrerequisitesMissing);
            builder.AppendLine("P3_RESULT=" + record.FailureResult.EvidenceStatus + "/" + record.PhysicalFailureOutcome);
            builder.AppendLine("P2_RESULT_NOT_USED_FOR_P3=true");
            WriteEvidence("p3-physical-stage-report.md", builder.ToString());
        }

        private static string Violations(SquatAttemptRecord record)
        {
            if (record.Judgment.Violations.Count == 0)
                return "NONE";
            return string.Join("|", record.Judgment.Violations.Select(violation => violation.Kind.ToString()));
        }

        private static string ViolationDetails(SquatAttemptRecord record, SquatRuleViolationKind kind)
        {
            for (int index = 0; index < record.Judgment.Violations.Count; index++)
            {
                SquatRuleViolationRecord violation = record.Judgment.Violations[index];
                if (violation.Kind == kind)
                    return string.Format(CultureInfo.InvariantCulture,
                        "{0} measured_a={1:R} measured_b={2:R} threshold={3:R} onset_tick={4}",
                        violation.Kind, violation.MeasuredValueA, violation.MeasuredValueB,
                        violation.MeasuredValueC, violation.OnsetTick);
            }
            return "NONE";
        }

        private static int FindIndexAtTick(SquatTrace trace, ulong tick)
        {
            for (int index = 0; index < trace.Count; index++)
                if (trace[index].SimulationTick == tick)
                    return index;
            return -1;
        }

        private static bool ContainsTick(SquatTrace trace, ulong tick) => FindIndexAtTick(trace, tick) >= 0;

        private static void WriteEvidence(string fileName, string content)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-47"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, fileName), content);
        }

        private static void WriteFreshProcessHeldEvidence(
            string label,
            string trace,
            HeldResult result)
        {
            string directory = Path.GetFullPath(Path.Combine(
                Application.dataPath,
                "..",
                "Artifacts",
                "Measurements",
                "GAM-48",
                "fresh-process"));
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, label + "-trace.csv"), trace);
            var summary = new StringBuilder();
            summary.AppendLine("MISSION=GAM48_GATE3_FRESH_PROCESS_DEPTH_ISOLATION");
            summary.AppendLine("ARM=" + label);
            summary.AppendLine("FRESH_UNITY_PROCESS_REQUIRED=true");
            summary.AppendLine("SETTLE_WINDOW_TICKS=200");
            summary.AppendLine("SETTLED_REPORT_TICKS=50");
            summary.AppendLine("PHASE=" + F(result.Phase));
            summary.AppendLine("SETTLED_MEAN_WORST_DEPTH_M=" + F(result.SettledMeanWorstSideDepth));
            summary.AppendLine("SETTLED_MIN_WORST_DEPTH_M=" + F(result.SettledMinimumWorstSideDepth));
            summary.AppendLine("SETTLED_MAX_WORST_DEPTH_M=" + F(result.SettledMaximumWorstSideDepth));
            summary.AppendLine("SUPPORT_RETAINED=" + B(result.SupportRetained));
            summary.AppendLine("FINITE_VALID_CONTROL=" + B(result.FiniteValidControl));
            summary.AppendLine("LEGAL=" + B(result.Legal));
            summary.AppendLine("DEEPEST_WORST_DEPTH_M=" + F(result.DeepestWorstSideDepth));
            summary.AppendLine("DEEPEST_TICK=" + result.DeepestTick.ToString(CultureInfo.InvariantCulture));
            summary.AppendLine("DEEPEST_SQ=" + F(result.DeepestSq));
            summary.AppendLine("BOTTOM_BAR_VELOCITY_MPS=" + F(result.BottomBarVelocityMps));
            summary.AppendLine("BOTTOM_PELVIS_VELOCITY_MPS=" + F(result.BottomPelvisVelocityMps));
            summary.AppendLine("CLAIM_CEILING=ENGINE_RUNTIME_OBSERVATION_AND_GAME_DERIVED_PROXY");
            File.WriteAllText(Path.Combine(directory, label + "-summary.md"), summary.ToString());
        }

        private static string PhaseLabel(float phase) =>
            phase.ToString("0.00", CultureInfo.InvariantCulture).Replace('.', '_');

        private static string Format(object value)
        {
            if (value == null)
                return "NA";
            if (value is float single)
                return float.IsNaN(single) || float.IsInfinity(single)
                    ? "NA"
                    : single.ToString("R", CultureInfo.InvariantCulture);
            if (value is double real)
                return double.IsNaN(real) || double.IsInfinity(real)
                    ? "NA"
                    : real.ToString("R", CultureInfo.InvariantCulture);
            if (value is bool boolean)
                return B(boolean);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string F(float value) => Format(value);
        private static string F(double value) => Format(value);
        private static string B(bool value) => value ? "true" : "false";
    }
}
