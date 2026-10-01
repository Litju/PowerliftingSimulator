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
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13V2SquatMechanicsQualificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int RequiredSetupQualificationSamples = 50;
        private const int SetupQualificationTimeoutTicks = 500;
        private const int RequiredLockoutSamples = 3;
        private const int LockoutSettlingTimeoutTicks = 100;
        private const int MaximumMechanicsTicks = 2000;
        private const float MaximumTrunkPitchRad = 0.70f;
        private const float MinimumPelvisHeightM = 0.90f;
        private const float MinimumBarHeightM = 1.00f;
        private const float MaximumComSpeedMps = 0.25f;
        private const float MinimumComSupportMarginM = -0.02f;
        private const float MaximumConstraintSeparationM = 0.05f;
        private const float MaximumSaddleSeparationM = 0.02f;
        private const float MaximumSaddleLimitOccupancy = 0.95f;
        private const float MaximumSaddleRelativeRotationDegrees = 35f;

        [UnityTest]
        public IEnumerator GAM13_V23G_NEW_ORCHESTRATOR_HAS_IDLE_LIFECYCLE_DEFAULTS()
        {
            AsyncOperation loadScene = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(loadScene, Is.Not.Null, "The GAM-13 qualification scene is missing.");
            while (!loadScene.isDone)
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

            SquatAttemptOrchestrator orchestrator = controller.AttemptOrchestrator;
            Assert.That(orchestrator.HasStarted, Is.False);
            Assert.That(orchestrator.HasSquatCommand, Is.False);
            Assert.That(orchestrator.SquatCommandTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
        }

        [UnityTest]
        public IEnumerator GAM13_V2_PHYSICAL_SQUAT_MECHANICS()
        {
            float loadKg = ReadLoadKg();
            Assert.That(Array.IndexOf(new[] { 25f, 60f, 140f, 170f, 300f }, loadKg), Is.GreaterThanOrEqualTo(0),
                "GAM13_V2_LOAD_KG must name a requested mechanics load.");
            string tracePath = Environment.GetEnvironmentVariable("GAM13_V2_TRACE_PATH") ??
                Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-13",
                    "v2-squat-mechanics", "load-" + loadKg.ToString("000", CultureInfo.InvariantCulture) + "kg.csv");
            string actuatorTracePath = Environment.GetEnvironmentVariable("GAM13_V2_ACTUATOR_TRACE_PATH") ??
                Path.ChangeExtension(tracePath, ".actuators.csv");
            var actuatorTrace = new GAM13V2ActuatorTrace();
            var trace = new StringBuilder();
            var firstComOutsideSupportLatch = new StringBuilder();
            firstComOutsideSupportLatch.AppendLine(
                "load_kg,input_tick,latch_tick,phase_before_latch,direction_before_latch,sq_before_latch,state_after_latch," +
                "system_com_x_m,system_com_y_m,system_com_z_m,system_com_vx_mps,system_com_vy_mps,system_com_vz_mps," +
                "support_center_x_m,support_center_y_m,support_center_z_m,ap_front_margin_m,ap_rear_margin_m,ml_left_margin_m,ml_right_margin_m," +
                "frozen_standing_com_offset_ap_m,frozen_standing_com_offset_ml_m,current_ap_reference_error_m,current_ml_reference_error_m," +
                "raw_ap_command_rad,raw_ml_command_rad,applied_ap_correction_rad,applied_ml_correction_rad,ap_saturated,ml_saturated," +
                "ankle_ap_offset_rad,ankle_ml_offset_rad,hip_ap_offset_rad,hip_ml_offset_rad,trunk_ap_offset_rad," +
                "cop_available,cop_ap_m,cop_ml_m,left_foot_contact,right_foot_contact,left_foot_slip_mps,right_foot_slip_mps," +
                "bar_x_m,bar_y_m,bar_z_m,bar_vx_mps,bar_vy_mps,bar_vz_mps," +
                "maximum_active_joint_demand_nm,worst_demand_joint,worst_demand_channel,worst_demand_fraction," +
                "maximum_joint_anchor_separation_m,saddle_attached,saddle_broken,saddle_separation_m,saddle_limit_occupancy,saddle_relative_rotation_deg");
            trace.AppendLine("load_kg,tick,time_s,state,phase_s,direction,legal_depth,legal_depth_reached,depth_left_m,depth_right_m,worst_side_depth_m,descent_tick,reversal_tick,ascent_tick,lockout,physical_failure,physical_failure_reason,bar_x_m,bar_y_m,bar_z_m,bar_q_w,bar_q_x,bar_q_y,bar_q_z,bar_vx_mps,bar_vy_mps,bar_vz_mps,bar_wx_rad_s,bar_wy_rad_s,bar_wz_rad_s,support_available,support_present,support_contact_count,left_foot_contact,right_foot_contact,left_foot_slip_mps,right_foot_slip_mps,ap_front_margin_m,ap_rear_margin_m,ml_left_margin_m,ml_right_margin_m,joint_limit_proximity,worst_limit_joint,saddle_attached,saddle_separation_m,saddle_initial_anchor_error_m,saddle_linear_limit_occupancy,saddle_relative_rotation_deg,saddle_is_broken,lockout_qualified,lockout_failed_predicates,lockout_reference_bar_y_m,lockout_bar_linear_speed_mps,lockout_bar_angular_speed_rad_s,lockout_max_knee_angle_rad,lockout_max_hip_angle_rad,lockout_max_local_trunk_angle_rad,lockout_height_tolerance_m,lockout_bar_speed_tolerance_mps,lockout_bar_angular_speed_tolerance_rad_s,lockout_knee_tolerance_rad,lockout_hip_tolerance_rad,lockout_trunk_tolerance_rad,ap_reference_error_m,raw_balance_ap_rad,applied_balance_ap_rad,ap_correction_saturated,ankle_target_error_x_rad,knee_target_error_x_rad,hip_target_error_x_rad,abdomen_target_error_x_rad,thorax_target_error_x_rad,max_active_drive_demand_nm,max_active_drive_demand_fraction,worst_active_drive_joint,worst_active_drive_channel,max_joint_anchor_separation_m,authoritative_balance_loss_onset_tick,authoritative_balance_loss_latched_tick,last_valid_pre_failure_tick,progress_valid,com_x_m,com_y_m,com_z_m,com_vx_mps,com_vy_mps,com_vz_mps,support_center_x_m,support_center_y_m,support_center_z_m,support_ap_min_m,support_ap_max_m,support_ml_min_m,support_ml_max_m,cop_available,cop_estimate_x_m,cop_estimate_y_m,cop_estimate_z_m");

            AsyncOperation loadScene = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(loadScene, Is.Not.Null, "The GAM-13 qualification scene is missing.");
            while (!loadScene.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            SquatPhysicalPrototypeController controller =
                UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            Assert.That(barbell, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);
            controller.enabled = false;
            bootstrap.enabled = false;
            controller.SetLoad(loadKg);
#if UNITY_EDITOR
            string apReferenceMode = Environment.GetEnvironmentVariable("GAM13_V2_AP_REFERENCE") ?? "A";
            Assert.That(apReferenceMode == "A" || apReferenceMode == "B", Is.True,
                "AP-reference mode must be A or B.");
            controller.Adapter.UsePhaseDependentApComReferenceForQualification = apReferenceMode == "B";
            string phaseRateText = Environment.GetEnvironmentVariable("GAM13_V2_PHASE_RATE");
            if (!string.IsNullOrWhiteSpace(phaseRateText))
            {
                Assert.That(float.TryParse(phaseRateText, NumberStyles.Float, CultureInfo.InvariantCulture, out float phaseRate),
                    Is.True, "GAM13_V2_PHASE_RATE must be invariant-culture numeric text.");
                controller.Adapter.PhaseRate = phaseRate;
            }
            bool gateCStaticHold = ReadEnvironmentFlag("GAM13_V2_GATE_C_STATIC_HOLD");
            bool phase0StandingHold = ReadEnvironmentFlag("GAM13_V2_PHASE0_STANDING_HOLD");
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V23M_CAPTURE load={0:F1}kg ap_reference={1} phase_rate={2:R} gate_c_static_hold={3} phase0_standing_hold={4}",
                loadKg, apReferenceMode, controller.Adapter.PhaseRate, gateCStaticHold, phase0StandingHold));
#else
            bool gateCStaticHold = false;
            bool phase0StandingHold = false;
#endif

            FoundationRuntime runtime = bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul));
            Assert.That(controller.AttemptOrchestrator.HasStarted, Is.False);
            Assert.That(controller.AttemptOrchestrator.HasSquatCommand, Is.False);
            ulong lifecycleSquatCommandTick = controller.AttemptOrchestrator.SquatCommandTick;
            Assert.That(lifecycleSquatCommandTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
            Assert.That(controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
            Assert.That(controller.AttemptRecord, Is.Null);

            // GAM-50 determinism benchmark: optional bit-exact per-tick state hashes.
            string stateHashPath = Environment.GetEnvironmentVariable("GAM50_STATE_HASH_PATH");
            var stateHashes = string.IsNullOrWhiteSpace(stateHashPath) ? null : new PhysicsStateHashLog();
            SquatObservationSnapshot standingReference = default;
            int consecutiveStandingSamples = 0;
            int setupTicks = 0;
            var phase0Capture = phase0StandingHold ? new PhaseZeroStandingCapture() : null;
            string phase0TracePath = phase0StandingHold
                ? Path.ChangeExtension(tracePath, ".phase0-standing.csv")
                : null;
            string physicsContractPath = Environment.GetEnvironmentVariable("GAM13_V2_PHYSICS_CONTRACT_PATH");
            GAM13V23BSubstrateAuditTests.WriteRuntimeReceipt(
                controller, runtime, loadKg, physicsContractPath, "GAM-13 V2-3I");
            while (consecutiveStandingSamples < RequiredSetupQualificationSamples &&
                setupTicks < SetupQualificationTimeoutTicks)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                stateHashes?.AppendState(runtime.CurrentTime.Tick, "SETUP", rig, barbell);
                Assert.That(controller.ObservationCollector.HasLastSnapshot, Is.True);
                standingReference = controller.ObservationCollector.LastSnapshot;
                bool sampleQualified = IsPhysicalStandingQualified(
                    standingReference, rig, controller, barbell);
                consecutiveStandingSamples = sampleQualified ? consecutiveStandingSamples + 1 : 0;
                setupTicks++;
                actuatorTrace.Append(loadKg, standingReference, controller);
                if (consecutiveStandingSamples == RequiredSetupQualificationSamples)
                    actuatorTrace.Mark(standingReference.SimulationTick, "SETTLED_STANDING");
                bool setupTimedOut = setupTicks >= SetupQualificationTimeoutTicks &&
                    consecutiveStandingSamples < RequiredSetupQualificationSamples;
                if (setupTimedOut)
                    actuatorTrace.Mark(standingReference.SimulationTick, "SETUP_TIMEOUT");
                AppendSample(trace, loadKg, standingReference, standingReference, controller, rig,
                    failureCalibration: SquatFailureCalibration.Default, legalDepthReached: false,
                    descentTick: null, reversalTick: null, ascentTick: null, lockout: false,
                    physicalFailure: setupTimedOut,
                    physicalFailureReason: setupTimedOut ? "SETUP_NOT_PHYSICALLY_QUALIFIED" : "NONE");
                if (phase0Capture != null)
                    CapturePhaseZeroStandingSample(
                        phase0Capture, "SETUP", standingReference, controller, rig,
                        actuatorTrace, consecutiveStandingSamples == RequiredSetupQualificationSamples);
                if (setupTicks % 50 == 0)
                    yield return null;
            }

            bool physicalStandingQualified = consecutiveStandingSamples >= RequiredSetupQualificationSamples;
            if (!physicalStandingQualified)
            {
                SaveTrace(tracePath, trace);
                actuatorTrace.Save(actuatorTracePath);
                if (phase0Capture != null)
                    SavePhaseZeroStandingTrace(phase0Capture, phase0TracePath);
                Debug.Log($"GAM13_V2_SQUAT_MECHANICS load={loadKg:F1}kg PHYSICAL_STANDING_QUALIFIED=false setup_samples={consecutiveStandingSamples}/{RequiredSetupQualificationSamples} setup_ticks={setupTicks} physical_failure=SETUP_NOT_PHYSICALLY_QUALIFIED trace={tracePath}");
                yield break;
            }

            if (phase0StandingHold)
            {
                Assert.That(loadKg, Is.EqualTo(140f), "The corrected V2-3M phase-zero hold is 140 kg only.");
                Assert.That(controller.Adapter.State, Is.EqualTo(SquatState.SETUP),
                    "Phase-zero standing must not issue a squat command.");
                Assert.That(controller.Adapter.Sq, Is.EqualTo(0f));
                Assert.That(controller.Adapter.UsePhaseDependentApComReferenceForQualification, Is.False,
                    "The corrected hold uses production AP reference A.");
                Assert.That(controller.Adapter.BalanceCorrectionsEnabled, Is.True,
                    "Normal V2 balance feedback must remain active.");
                Assert.That(controller.Adapter.ApFeedbackContributionEnabled, Is.True,
                    "Normal AP balance feedback must remain active.");
                Assert.That(controller.AttemptOrchestrator.HasSquatCommand, Is.False);

                yield return ContinuePhaseZeroStandingHold(
                    controller, runtime, rig, loadKg, dt, phase0Capture, actuatorTrace,
                    trace, phase0TracePath);
                SaveTrace(tracePath, trace);
                actuatorTrace.Save(actuatorTracePath);
                string latchPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(tracePath)),
                    "first-com-outside-support-latch.csv");
                File.WriteAllText(latchPath, firstComOutsideSupportLatch.ToString(), new UTF8Encoding(false));
                yield break;
            }

            controller.BeginPhysicalSquatMotionForQualification();
            Assert.That(controller.Adapter.State, Is.EqualTo(SquatState.SQUAT_COMMAND));
            Assert.That(controller.AttemptOrchestrator.HasStarted, Is.False);
            Assert.That(controller.AttemptOrchestrator.SquatCommandTick, Is.EqualTo(lifecycleSquatCommandTick));
            Assert.That(controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
            Assert.That(controller.AttemptRecord, Is.Null);

            if (gateCStaticHold)
            {
                Assert.That(loadKg, Is.EqualTo(140f), "Gate C is only defined for the 140 kg discriminator.");
                Assert.That(controller.Adapter.BalanceCorrectionsEnabled, Is.True,
                    "Gate C must keep normal COM balance feedback active.");
                controller.BeginPhysicalSquatMotionForQualification();
                var gateCTrace = new StringBuilder();
                string gateCTracePath = Path.ChangeExtension(tracePath, ".quasi-static-hold.csv");
                yield return RunGateCStaticHold(
                    controller, runtime, rig, standingReference, loadKg, dt,
                    trace, actuatorTrace, gateCTrace, gateCTracePath);
                SaveTrace(tracePath, trace);
                actuatorTrace.Save(actuatorTracePath);
                string latchPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(tracePath)),
                    "first-com-outside-support-latch.csv");
                File.WriteAllText(latchPath, firstComOutsideSupportLatch.ToString(), new UTF8Encoding(false));
                yield break;
            }

            SquatFailureCalibration failureCalibration = SquatFailureCalibration.Default;
            bool hasDescent = false;
            bool hasReversal = false;
            bool hasAscent = false;
            bool legalDepthReached = false;
            bool lockout = false;
            bool physicalFailure = false;
            string physicalFailureReason = "NONE";
            ulong? descentTick = null;
            ulong? reversalTick = null;
            ulong? ascentTick = null;
            ulong? bottomOrReversalContextTick = null;
            ulong? lockoutStartTick = null;
            ulong? legalDepthTick = null;
            int consecutiveLockoutSamples = 0;
            float lowestBarY = float.PositiveInfinity;
            float peakAscentDemand = float.NegativeInfinity;
            ulong? peakAscentDemandTick = null;
            bool bottomCaptured = false;
            int mechanicsTicks = 0;
            string firstComOutsideSupportLatchPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(tracePath)), "first-com-outside-support-latch.csv");
            bool capturedComOutsideSupportLatch = false;
            var balanceFailureDetector = new SquatFailureDetector(failureCalibration);
            SquatFailureEvent balanceLossEvent = null;
            ulong? balanceLossOnsetTick = null;
            ulong? balanceLossLatchedTick = null;
            ulong? lastValidPreFailureTick = null;
            var mechanicsSnapshots = new List<SquatObservationSnapshot>(MaximumMechanicsTicks);

            while (!lockout && !physicalFailure && mechanicsTicks < MaximumMechanicsTicks)
            {
                SquatState phaseBeforeStep = controller.Adapter.State;
                SquatPhaseDirection directionBeforeStep = controller.Adapter.Direction;
                float sqBeforeStep = controller.Adapter.Sq;
                SquatObservationSnapshot latchInput = controller.ObservationCollector.LastSnapshot;
                bool copAvailable = controller.Adapter.Balance.HasCopEstimate;
                Vector3 cop = controller.Adapter.Balance.CopEstimate;
                MeasureMaximumActiveJointDemand(
                    rig.PoweredController,
                    out float maximumActiveJointDemandNm,
                    out string worstDemandJoint,
                    out string worstDemandChannel,
                    out float worstDemandFraction);
                GAM13V2StandingQualificationTests.MeasureConstraintHealth(
                    rig, out float maximumJointAnchorSeparationM, out _, out _);
                SquatBarSaddle saddleBeforeStep = controller.Saddle;
                float saddleSeparationM = saddleBeforeStep != null
                    ? saddleBeforeStep.SaddleSeparationMeters
                    : float.NaN;
                float saddleLimitOccupancy = saddleBeforeStep != null
                    ? saddleBeforeStep.CurrentLinearLimitOccupancy
                    : float.NaN;
                float saddleRelativeRotationDegrees = saddleBeforeStep != null
                    ? saddleBeforeStep.RelativeRotationDegrees
                    : float.NaN;
                if (!bottomOrReversalContextTick.HasValue && hasDescent &&
                    (phaseBeforeStep == SquatState.BOTTOM || phaseBeforeStep == SquatState.REVERSAL))
                {
                    bottomOrReversalContextTick = runtime.CurrentTime.Tick;
                }
                double inputTime = runtime.CurrentTime.SimulationTimeSeconds +
                    0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                bool yieldInput = phaseBeforeStep == SquatState.SQUAT_COMMAND ||
                    phaseBeforeStep == SquatState.DESCENT;
                bool driveInput = phaseBeforeStep == SquatState.BOTTOM ||
                    phaseBeforeStep == SquatState.REVERSAL ||
                    phaseBeforeStep == SquatState.ASCENT ||
                    phaseBeforeStep == SquatState.STICKING;
                runtime.InputBuffer.SetContinuous(IntentAction.Yield, yieldInput ? 1f : 0f, inputTime);
                runtime.InputBuffer.SetContinuous(IntentAction.Drive, driveInput ? 1f : 0f, inputTime);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                mechanicsTicks++;
                stateHashes?.AppendState(runtime.CurrentTime.Tick, controller.Adapter.State.ToString(), rig, barbell);

                Assert.That(controller.AttemptOrchestrator.HasStarted, Is.False,
                    "The mechanics probe must not start the GAM-12 attempt lifecycle.");
                Assert.That(controller.AttemptOrchestrator.SquatCommandTick, Is.EqualTo(lifecycleSquatCommandTick),
                    "The mechanics probe must not change the GAM-12 Squat command tick.");
                Assert.That(controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
                Assert.That(controller.AttemptRecord, Is.Null);

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                mechanicsSnapshots.Add(snapshot);
                float barY = snapshot.Bar.PositionWorldMeters.Y;
                float barVelocityY = snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y;
                bool captureBottom = !bottomCaptured && hasDescent && snapshot.State == SquatState.BOTTOM;
                if (captureBottom)
                {
                    bottomCaptured = true;
                }
                lowestBarY = Mathf.Min(lowestBarY, barY);
                if (!hasDescent && barVelocityY <= -failureCalibration.PhysicalMotionVelocityMps)
                {
                    hasDescent = true;
                    descentTick = snapshot.SimulationTick;
                }
                if (hasDescent && !hasReversal && bottomOrReversalContextTick.HasValue &&
                    snapshot.SimulationTick > bottomOrReversalContextTick.Value &&
                    barVelocityY >= failureCalibration.PhysicalMotionVelocityMps)
                {
                    hasReversal = true;
                    reversalTick = snapshot.SimulationTick;
                }
                if (hasReversal && !hasAscent &&
                    barVelocityY >= failureCalibration.AscentEstablishmentVelocityMps &&
                    barY - lowestBarY >= failureCalibration.AscentEstablishmentDisplacementM)
                {
                    hasAscent = true;
                    ascentTick = snapshot.SimulationTick;
                }

                if (controller.Adapter.LegalDepth && !legalDepthTick.HasValue)
                    legalDepthTick = snapshot.SimulationTick;
                legalDepthReached = legalDepthTick.HasValue;
                float maximumDemand = actuatorTrace.Append(loadKg, snapshot, controller);
                if (captureBottom)
                    actuatorTrace.Mark(snapshot.SimulationTick, "BOTTOM");
                if (hasReversal && snapshot.SimulationTick == reversalTick)
                    actuatorTrace.Mark(snapshot.SimulationTick, "REVERSAL");
                if ((snapshot.State == SquatState.ASCENT || snapshot.State == SquatState.STICKING) &&
                    maximumDemand > peakAscentDemand)
                {
                    peakAscentDemand = maximumDemand;
                    peakAscentDemandTick = snapshot.SimulationTick;
                }

                balanceFailureDetector.Process(snapshot);
                if (balanceLossEvent == null && balanceFailureDetector.TryGetLatchedFailureEvent(
                    SquatFailureKind.BALANCE_LOSS, out SquatFailureEvent latchedBalanceLoss))
                {
                    balanceLossEvent = latchedBalanceLoss;
                    balanceLossOnsetTick = balanceLossEvent.OnsetTick;
                    balanceLossLatchedTick = balanceLossEvent.LatchedTick;
                    lastValidPreFailureTick = balanceLossEvent.OnsetTick > 0ul
                        ? balanceLossEvent.OnsetTick - 1ul
                        : (ulong?)null;
                    if (lastValidPreFailureTick.HasValue)
                        actuatorTrace.Mark(lastValidPreFailureTick.Value, "GAM12_LAST_VALID_PRE_FAILURE");

                    if (descentTick.HasValue && descentTick.Value >= balanceLossEvent.OnsetTick)
                    {
                        hasDescent = false;
                        descentTick = null;
                    }
                    if (bottomOrReversalContextTick.HasValue &&
                        bottomOrReversalContextTick.Value >= balanceLossEvent.OnsetTick)
                        bottomOrReversalContextTick = null;
                    if (reversalTick.HasValue && reversalTick.Value >= balanceLossEvent.OnsetTick)
                    {
                        hasReversal = false;
                        reversalTick = null;
                    }
                    if (ascentTick.HasValue && ascentTick.Value >= balanceLossEvent.OnsetTick)
                    {
                        hasAscent = false;
                        ascentTick = null;
                    }
                    if (legalDepthTick.HasValue && legalDepthTick.Value >= balanceLossEvent.OnsetTick)
                    {
                        legalDepthTick = null;
                        legalDepthReached = false;
                    }
                    if (lockoutStartTick.HasValue && lockoutStartTick.Value >= balanceLossEvent.OnsetTick)
                    {
                        lockoutStartTick = null;
                        lockout = false;
                        consecutiveLockoutSamples = 0;
                    }
                    lowestBarY = float.PositiveInfinity;
                    foreach (SquatObservationSnapshot observed in mechanicsSnapshots)
                    {
                        if (observed.SimulationTick >= balanceLossEvent.OnsetTick || !observed.Bar.IsAvailable)
                            continue;
                        lowestBarY = Mathf.Min(lowestBarY, observed.Bar.PositionWorldMeters.Y);
                    }
                }

                physicalFailureReason = balanceLossEvent != null
                    ? "BALANCE_LOSS"
                    : FindPhysicalFailure(controller, snapshot, ignoreComOutsideSupport: true);
                physicalFailure = physicalFailureReason != "NONE";
                if (!capturedComOutsideSupportLatch &&
                    string.Equals(controller.Adapter.FailureReason, "COM_OUTSIDE_SUPPORT", StringComparison.Ordinal))
                {
                    AppendComOutsideSupportLatch(
                        firstComOutsideSupportLatch,
                        loadKg,
                        runtime.CurrentTime.Tick,
                        phaseBeforeStep,
                        directionBeforeStep,
                        sqBeforeStep,
                        latchInput,
                        controller,
                        copAvailable,
                        cop,
                        maximumActiveJointDemandNm,
                        worstDemandJoint,
                        worstDemandChannel,
                        worstDemandFraction,
                        maximumJointAnchorSeparationM,
                        saddleBeforeStep,
                        saddleSeparationM,
                        saddleLimitOccupancy,
                        saddleRelativeRotationDegrees);
                    capturedComOutsideSupportLatch = true;
                }
                if (physicalFailure)
                    actuatorTrace.Mark(snapshot.SimulationTick, "PHYSICAL_FAILURE");
                if (controller.Adapter.LockoutReached && !lockoutStartTick.HasValue)
                {
                    lockoutStartTick = snapshot.SimulationTick;
                    actuatorTrace.Mark(snapshot.SimulationTick, "REFERENCE_LOCKOUT");
                }
                if (!physicalFailure && lockoutStartTick.HasValue)
                {
                    consecutiveLockoutSamples = SquatAttemptPhysicalEvidence.IsLockout(
                        snapshot, standingReference, failureCalibration)
                        ? consecutiveLockoutSamples + 1
                        : 0;
                    lockout = consecutiveLockoutSamples >= RequiredLockoutSamples;
                    if (lockout)
                        actuatorTrace.Mark(snapshot.SimulationTick, "SETTLED_PHYSICAL_LOCKOUT");
                    bool lockoutSettlingTimedOut = snapshot.SimulationTick - lockoutStartTick.Value + 1ul >=
                        (ulong)LockoutSettlingTimeoutTicks;
                    if (!lockout && lockoutSettlingTimedOut)
                    {
                        physicalFailure = true;
                        physicalFailureReason = LockoutFailureReason(
                            hasDescent, legalDepthReached, hasReversal, hasAscent);
                        if (physicalFailureReason == "PHYSICAL_LOCKOUT_NOT_REACHED")
                            actuatorTrace.Mark(snapshot.SimulationTick, "LOCKOUT_TIMEOUT");
                    }
                }

                AppendSample(trace, loadKg, standingReference, snapshot, controller, rig, failureCalibration,
                    legalDepthReached, descentTick, reversalTick, ascentTick, lockout,
                    physicalFailure, physicalFailureReason,
                    balanceLossOnsetTick, balanceLossLatchedTick, lastValidPreFailureTick,
                    !balanceLossOnsetTick.HasValue || snapshot.SimulationTick < balanceLossOnsetTick.Value);
                if (mechanicsTicks % 50 == 0)
                    yield return null;
            }

            // GAM-50 section 7: measurement only. The verdict above is final
            // and unchanged; when it is a lockout-settling timeout, keep
            // stepping the identical physical simulation and record whether
            // the sealed lockout predicates become true naturally.
            int lockoutExtensionTicks = ReadLockoutExtensionTicks();
            if (lockoutExtensionTicks > 0 && physicalFailureReason == "PHYSICAL_LOCKOUT_NOT_REACHED" && lockoutStartTick.HasValue)
            {
                yield return ContinueLockoutExtension(controller, runtime, rig, barbell, standingReference, failureCalibration,
                    loadKg, dt, lockoutExtensionTicks, lockoutStartTick.Value, tracePath, stateHashes);
            }
            if (stateHashes != null)
                stateHashes.Save(stateHashPath);

            if (peakAscentDemandTick.HasValue)
                actuatorTrace.Mark(peakAscentDemandTick.Value, "PEAK_ASCENT_DEMAND");
            if (balanceLossOnsetTick.HasValue)
                InvalidateMechanicsProgressAtOrAfterOnset(trace, balanceLossOnsetTick.Value);
            SaveTrace(tracePath, trace);
            actuatorTrace.Save(actuatorTracePath);
            File.WriteAllText(firstComOutsideSupportLatchPath, firstComOutsideSupportLatch.ToString(), new UTF8Encoding(false));
            if (balanceLossEvent != null)
                SaveBalanceLossTerminationContext(tracePath, actuatorTracePath, failureCalibration,
                    balanceLossEvent, lastValidPreFailureTick);
            Assert.That(lockout || physicalFailure, Is.True,
                "The adapter did not reach a physical lockout or report a physical mechanics failure.");
            if (reversalTick.HasValue)
            {
                Assert.That(bottomOrReversalContextTick.HasValue, Is.True,
                    "A bar reversal requires prior adapter bottom/reversal context.");
                Assert.That(reversalTick.Value, Is.GreaterThan(bottomOrReversalContextTick.Value),
                    "An upward bar-velocity event before adapter bottom/reversal context is not squat reversal.");
            }
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V2_SQUAT_MECHANICS load={0:F1}kg PHYSICAL_STANDING_QUALIFIED=true legal_physical_depth={1} descent_tick={2} reversal_tick={3} ascent_tick={4} lockout={5} physical_failure={6} physical_failure_reason={7} authoritative_balance_loss_direction={8} authoritative_onset_tick={9} authoritative_latched_tick={10} last_valid_pre_failure_tick={11} trace={12} actuators={13}",
                loadKg, legalDepthReached, Tick(descentTick), Tick(reversalTick), Tick(ascentTick),
                lockout, physicalFailure, physicalFailureReason,
                balanceLossEvent != null ? balanceLossEvent.Direction.ToString() : "NA",
                Tick(balanceLossOnsetTick), Tick(balanceLossLatchedTick), Tick(lastValidPreFailureTick),
                tracePath, actuatorTracePath));
        }

        private static void AppendComOutsideSupportLatch(
            StringBuilder trace,
            float loadKg,
            ulong latchTick,
            SquatState phaseBeforeLatch,
            SquatPhaseDirection directionBeforeLatch,
            float sqBeforeLatch,
            SquatObservationSnapshot input,
            SquatPhysicalPrototypeController controller,
            bool copAvailable,
            Vector3 cop,
            float maximumActiveJointDemandNm,
            string worstDemandJoint,
            string worstDemandChannel,
            float worstDemandFraction,
            float maximumJointAnchorSeparationM,
            SquatBarSaddle saddle,
            float saddleSeparationM,
            float saddleLimitOccupancy,
            float saddleRelativeRotationDegrees)
        {
            SquatSupportObservation support = input.Support;
            SquatBalanceCorrectionV2 correction = controller.Adapter.BalanceCorrectionV2;
            Vector3Value com = support.SystemComWorldMeters;
            Vector3Value comVelocity = support.SystemComVelocityWorldMetersPerSecond;
            Vector3Value supportCenter = support.SupportCenterWorldMeters;
            Vector3Value barPosition = input.Bar.PositionWorldMeters;
            Vector3Value barVelocity = input.Bar.LinearVelocityWorldMetersPerSecond;
            trace.AppendLine(string.Join(",", new[]
            {
                Format(loadKg), Tick(input.SimulationTick), Tick(latchTick), phaseBeforeLatch.ToString(),
                directionBeforeLatch.ToString(), Format(sqBeforeLatch), controller.Adapter.State.ToString(),
                Format(com.X), Format(com.Y), Format(com.Z),
                Format(comVelocity.X), Format(comVelocity.Y), Format(comVelocity.Z),
                Format(supportCenter.X), Format(supportCenter.Y), Format(supportCenter.Z),
                Format(support.ComToSupportApFrontMarginM), Format(support.ComToSupportApRearMarginM),
                Format(support.ComToSupportMlLeftMarginM), Format(support.ComToSupportMlRightMarginM),
                Format(controller.Adapter.ReferenceComOffsetAp), Format(controller.Adapter.ReferenceComOffsetMl),
                Format(correction.ErrorApM), Format(correction.ErrorMlM),
                Format(correction.CommandApRad), Format(correction.CommandMlRad),
                Format(correction.AppliedApRad), Format(correction.AppliedMlRad),
                correction.IsApBoundSaturated ? "true" : "false",
                correction.IsMlBoundSaturated ? "true" : "false",
                Format(correction.AnkleApRad), Format(correction.AnkleMlRad),
                Format(correction.HipApRad), Format(correction.HipMlRad), Format(correction.TrunkApRad),
                copAvailable ? "true" : "false", copAvailable ? Format(cop.z) : "NA", copAvailable ? Format(cop.x) : "NA",
                input.LeftFoot.IsInContact ? "true" : "false", input.RightFoot.IsInContact ? "true" : "false",
                Format(input.LeftFoot.SlipSpeedMetersPerSecond), Format(input.RightFoot.SlipSpeedMetersPerSecond),
                Format(barPosition.X), Format(barPosition.Y), Format(barPosition.Z),
                Format(barVelocity.X), Format(barVelocity.Y), Format(barVelocity.Z),
                Format(maximumActiveJointDemandNm), worstDemandJoint, worstDemandChannel, Format(worstDemandFraction),
                Format(maximumJointAnchorSeparationM),
                saddle != null && saddle.IsAttached ? "true" : "false",
                saddle == null || saddle.IsBroken ? "true" : "false",
                Format(saddleSeparationM), Format(saddleLimitOccupancy), Format(saddleRelativeRotationDegrees)
            }));
        }

        private static void MeasureMaximumActiveJointDemand(
            PoweredJointController poweredController,
            out float maximumDemandNm,
            out string worstJoint,
            out string worstChannel,
            out float worstDemandFraction)
        {
            maximumDemandNm = 0f;
            worstJoint = "NONE";
            worstChannel = "NONE";
            worstDemandFraction = 0f;
            foreach (PoweredJointController.PoweredJointRuntime joint in poweredController.Joints)
            {
                if (!joint.Profile.HasValue || !joint.HasPostPhysicsDiagnostic)
                    continue;
                PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                if (diagnostic.TwistDriveDemandNm > maximumDemandNm)
                {
                    maximumDemandNm = diagnostic.TwistDriveDemandNm;
                    worstJoint = joint.Id;
                    worstChannel = "twist_x";
                    worstDemandFraction = diagnostic.TwistDriveDemandFraction;
                }
                if (joint.Recipe.Kind == PhysicalJointKind.Ball &&
                    float.IsFinite(diagnostic.SwingDriveDemandNm) &&
                    diagnostic.SwingDriveDemandNm > maximumDemandNm)
                {
                    maximumDemandNm = diagnostic.SwingDriveDemandNm;
                    worstJoint = joint.Id;
                    worstChannel = "swing_yz";
                    worstDemandFraction = diagnostic.SwingDriveDemandFraction;
                }
            }
        }

        private static bool IsPhysicalStandingQualified(
            SquatObservationSnapshot snapshot,
            PhysicalAthleteRig rig,
            SquatPhysicalPrototypeController controller,
            PhysicalBarbell barbell)
        {
            GAM13V2StandingQualificationTests.MeasureConstraintHealth(
                rig, out float anchorSeparation, out _, out bool diagnosticsAvailable);
            Vector3Value comVelocity = snapshot.Support.SystemComVelocityWorldMetersPerSecond;
            float comSpeed = Mathf.Sqrt(comVelocity.X * comVelocity.X + comVelocity.Z * comVelocity.Z);
            float supportMargin = Mathf.Min(
                snapshot.Support.ComToSupportApFrontMarginM,
                snapshot.Support.ComToSupportApRearMarginM,
                snapshot.Support.ComToSupportMlLeftMarginM,
                snapshot.Support.ComToSupportMlRightMarginM);
            bool supportAndPosturePass = diagnosticsAvailable &&
                snapshot.Support.HasSupport && snapshot.LeftFoot.IsInContact && snapshot.RightFoot.IsInContact &&
                snapshot.PelvisPositionWorldMeters.Y >= MinimumPelvisHeightM &&
                Mathf.Abs(snapshot.TrunkWorldPitchRadians) <= MaximumTrunkPitchRad &&
                comSpeed <= MaximumComSpeedMps && supportMargin >= MinimumComSupportMarginM &&
                anchorSeparation <= MaximumConstraintSeparationM;
            bool barAndSaddlePass = snapshot.Bar.IsAvailable &&
                snapshot.Bar.PositionWorldMeters.Y >= MinimumBarHeightM &&
                controller.Saddle != null && controller.Saddle.IsAttached && !controller.Saddle.IsBroken &&
                controller.Saddle.InitialAnchorErrorMeters <= SquatBarSaddle.InitialAnchorToleranceM &&
                controller.Saddle.SaddleSeparationMeters <= MaximumSaddleSeparationM &&
                controller.Saddle.CurrentLinearLimitOccupancy < MaximumSaddleLimitOccupancy &&
                controller.Saddle.RelativeRotationDegrees < MaximumSaddleRelativeRotationDegrees;
            return GAM13V2StandingQualificationTests.IsFinite(snapshot, rig, controller, true, barbell) &&
                supportAndPosturePass && barAndSaddlePass;
        }

        private static string FindPhysicalFailure(
            SquatPhysicalPrototypeController controller,
            SquatObservationSnapshot snapshot,
            bool ignoreComOutsideSupport = false)
        {
            if (!string.Equals(controller.Adapter.FailureReason, "NONE", StringComparison.Ordinal) &&
                !(ignoreComOutsideSupport && string.Equals(
                    controller.Adapter.FailureReason, "COM_OUTSIDE_SUPPORT", StringComparison.Ordinal)))
                return controller.Adapter.FailureReason;
            if (controller.Saddle == null || !controller.Saddle.IsAttached || controller.Saddle.IsBroken)
                return "BAR_SADDLE_FAILURE";
            if (snapshot.Support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE &&
                (!snapshot.Support.HasSupport || !snapshot.LeftFoot.IsInContact || !snapshot.RightFoot.IsInContact))
                return "PLANTAR_SUPPORT_LOST";
            return "NONE";
        }

        private static string LockoutFailureReason(
            bool hasDescent,
            bool legalDepthReached,
            bool hasReversal,
            bool hasAscent)
        {
            if (!hasDescent)
                return "NO_PHYSICAL_DESCENT";
            if (!legalDepthReached)
                return "NO_LEGAL_PHYSICAL_DEPTH";
            if (!hasReversal)
                return "NO_PHYSICAL_REVERSAL";
            if (!hasAscent)
                return "NO_PHYSICAL_ASCENT";
            return "PHYSICAL_LOCKOUT_NOT_REACHED";
        }

        private static void AppendSample(
            StringBuilder trace,
            float loadKg,
            SquatObservationSnapshot standingReference,
            SquatObservationSnapshot snapshot,
            SquatPhysicalPrototypeController controller,
            PhysicalAthleteRig rig,
            SquatFailureCalibration failureCalibration,
            bool legalDepthReached,
            ulong? descentTick,
            ulong? reversalTick,
            ulong? ascentTick,
            bool lockout,
            bool physicalFailure,
            string physicalFailureReason,
            ulong? balanceLossOnsetTick = null,
            ulong? balanceLossLatchedTick = null,
            ulong? lastValidPreFailureTick = null,
            bool progressValid = true)
        {
            float maximumLimitProximity = 0f;
            string worstLimitJoint = "NONE";
            float ankleTargetError = 0f;
            float kneeTargetError = 0f;
            float hipTargetError = 0f;
            float abdomenTargetError = 0f;
            float thoraxTargetError = 0f;
            float maximumActiveDriveDemandNm = 0f;
            float maximumActiveDriveDemandFraction = 0f;
            string worstActiveDriveJoint = "NONE";
            string worstActiveDriveChannel = "NONE";
            float maximumJointAnchorSeparationM = 0f;
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !joint.HasPostPhysicsDiagnostic)
                    continue;
                PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                float proximity = diagnostic.LimitProximity;
                if (proximity > maximumLimitProximity)
                {
                    maximumLimitProximity = proximity;
                    worstLimitJoint = joint.Id;
                }

                float targetErrorX = Mathf.Abs(diagnostic.ErrorRad.x);
                switch (joint.Id)
                {
                    case "left_foot":
                    case "right_foot": ankleTargetError = Mathf.Max(ankleTargetError, targetErrorX); break;
                    case "left_shank":
                    case "right_shank": kneeTargetError = Mathf.Max(kneeTargetError, targetErrorX); break;
                    case "left_thigh":
                    case "right_thigh": hipTargetError = Mathf.Max(hipTargetError, targetErrorX); break;
                    case "abdomen": abdomenTargetError = targetErrorX; break;
                    case "thorax": thoraxTargetError = targetErrorX; break;
                }

                if (diagnostic.TwistDriveDemandNm > maximumActiveDriveDemandNm)
                {
                    maximumActiveDriveDemandNm = diagnostic.TwistDriveDemandNm;
                    maximumActiveDriveDemandFraction = diagnostic.TwistDriveDemandFraction;
                    worstActiveDriveJoint = joint.Id;
                    worstActiveDriveChannel = "twist_x";
                }
                if (joint.Recipe.Kind == PhysicalJointKind.Ball &&
                    float.IsFinite(diagnostic.SwingDriveDemandNm) &&
                    diagnostic.SwingDriveDemandNm > maximumActiveDriveDemandNm)
                {
                    maximumActiveDriveDemandNm = diagnostic.SwingDriveDemandNm;
                    maximumActiveDriveDemandFraction = diagnostic.SwingDriveDemandFraction;
                    worstActiveDriveJoint = joint.Id;
                    worstActiveDriveChannel = "swing_yz";
                }

            }
            GAM13V2StandingQualificationTests.MeasureConstraintHealth(
                rig, out maximumJointAnchorSeparationM, out _, out _);

            SquatBarObservation bar = snapshot.Bar;
            SquatSupportObservation support = snapshot.Support;
            SquatBalanceCorrectionV2 balanceCorrection = controller.Adapter.BalanceCorrectionV2;
            bool copAvailable = controller.Adapter.Balance.HasCopEstimate;
            Vector3 cop = controller.Adapter.Balance.CopEstimate;
            Vector3Value systemCom = support.SystemComWorldMeters;
            Vector3Value systemComVelocity = support.SystemComVelocityWorldMetersPerSecond;
            Vector3Value supportCenter = support.SupportCenterWorldMeters;
            QuaternionValue barOrientation = bar.OrientationWorldFromBar;
            Vector3Value velocity = bar.LinearVelocityWorldMetersPerSecond;
            Vector3Value angularVelocity = bar.AngularVelocityBarRadiansPerSecond;
            float saddleSeparation = controller.Saddle == null
                ? float.NaN
                : controller.Saddle.SaddleSeparationMeters;
            float saddleInitialAnchorError = controller.Saddle == null
                ? float.NaN
                : controller.Saddle.InitialAnchorErrorMeters;
            float saddleLimitOccupancy = controller.Saddle == null
                ? float.NaN
                : controller.Saddle.CurrentLinearLimitOccupancy;
            float saddleRelativeRotation = controller.Saddle == null
                ? float.NaN
                : controller.Saddle.RelativeRotationDegrees;
            bool saddleBroken = controller.Saddle == null || controller.Saddle.IsBroken;
            bool saddleAttached = controller.Saddle != null && controller.Saddle.IsAttached && !controller.Saddle.IsBroken;
            SquatPhysicalLockoutDiagnostic lockoutDiagnostic = SquatAttemptPhysicalEvidence.MeasureLockout(
                snapshot, standingReference.Bar.IsAvailable, standingReference.Bar.PositionWorldMeters.Y,
                failureCalibration);
            Assert.That(
                lockoutDiagnostic.IsLockout,
                Is.EqualTo(SquatAttemptPhysicalEvidence.IsLockout(snapshot, standingReference, failureCalibration)),
                "Lockout predicate telemetry must match the production qualification method.");
            trace.Append(Format(loadKg)).Append(',')
                .Append(snapshot.SimulationTick.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.SimulationTimeSeconds.ToString("R", CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.State).Append(',').Append(Format(snapshot.Sq)).Append(',').Append(snapshot.Direction).Append(',')
                .Append(controller.Adapter.LegalDepth ? "true" : "false").Append(',')
                .Append(legalDepthReached ? "true" : "false").Append(',')
                .Append(Format(controller.Adapter.RuleDepthLeftM)).Append(',')
                .Append(Format(controller.Adapter.RuleDepthRightM)).Append(',')
                .Append(Format(controller.Adapter.WorstSideDepthM)).Append(',')
                .Append(Tick(descentTick)).Append(',').Append(Tick(reversalTick)).Append(',')
                .Append(Tick(ascentTick)).Append(',').Append(lockout ? "true" : "false").Append(',')
                .Append(physicalFailure ? "true" : "false").Append(',').Append(physicalFailureReason).Append(',')
                .Append(Format(bar.PositionWorldMeters.X)).Append(',')
                .Append(Format(bar.PositionWorldMeters.Y)).Append(',')
                .Append(Format(bar.PositionWorldMeters.Z)).Append(',')
                .Append(Format(barOrientation.W)).Append(',').Append(Format(barOrientation.X)).Append(',')
                .Append(Format(barOrientation.Y)).Append(',').Append(Format(barOrientation.Z)).Append(',')
                .Append(Format(velocity.X)).Append(',').Append(Format(velocity.Y)).Append(',').Append(Format(velocity.Z)).Append(',')
                .Append(Format(angularVelocity.X)).Append(',').Append(Format(angularVelocity.Y)).Append(',').Append(Format(angularVelocity.Z)).Append(',')
                .Append(support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE ? "true" : "false").Append(',')
                .Append(support.HasSupport ? "true" : "false").Append(',')
                .Append(support.SupportContactCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(snapshot.LeftFoot.IsInContact ? "true" : "false").Append(',')
                .Append(snapshot.RightFoot.IsInContact ? "true" : "false").Append(',')
                .Append(Format(snapshot.LeftFoot.SlipSpeedMetersPerSecond)).Append(',')
                .Append(Format(snapshot.RightFoot.SlipSpeedMetersPerSecond)).Append(',')
                .Append(Format(support.ComToSupportApFrontMarginM)).Append(',')
                .Append(Format(support.ComToSupportApRearMarginM)).Append(',')
                .Append(Format(support.ComToSupportMlLeftMarginM)).Append(',')
                .Append(Format(support.ComToSupportMlRightMarginM)).Append(',')
                .Append(Format(maximumLimitProximity)).Append(',').Append(worstLimitJoint).Append(',')
                .Append(saddleAttached ? "true" : "false").Append(',').Append(Format(saddleSeparation)).Append(',')
                .Append(Format(saddleInitialAnchorError)).Append(',').Append(Format(saddleLimitOccupancy)).Append(',')
                .Append(Format(saddleRelativeRotation)).Append(',').Append(saddleBroken ? "true" : "false").Append(',')
                .Append(lockoutDiagnostic.IsLockout ? "true" : "false").Append(',')
                .Append(LockoutFailurePredicates(lockoutDiagnostic)).Append(',')
                .Append(Format(lockoutDiagnostic.StandingReferenceBarY)).Append(',')
                .Append(Format(lockoutDiagnostic.BarSpeed)).Append(',')
                .Append(Format(lockoutDiagnostic.BarAngularSpeed)).Append(',')
                .Append(Format(lockoutDiagnostic.MaximumKneeAngle)).Append(',')
                .Append(Format(lockoutDiagnostic.MaximumHipAngle)).Append(',')
                .Append(Format(lockoutDiagnostic.MaximumTrunkAngle)).Append(',')
                .Append(Format(failureCalibration.LockoutHeightToleranceM)).Append(',')
                .Append(Format(failureCalibration.LockoutBarStillVelocityMps)).Append(',')
                .Append(Format(failureCalibration.LockoutBarStillAngularVelocityRadS)).Append(',')
                .Append(Format(failureCalibration.LockoutKneeToleranceRadians)).Append(',')
                .Append(Format(failureCalibration.LockoutHipToleranceRadians)).Append(',')
                .Append(Format(failureCalibration.LockoutTrunkToleranceRadians)).Append(',')
                .Append(Format(balanceCorrection.ErrorApM)).Append(',')
                .Append(Format(balanceCorrection.CommandApRad)).Append(',')
                .Append(Format(balanceCorrection.AppliedApRad)).Append(',')
                .Append(balanceCorrection.IsApBoundSaturated ? "true" : "false")
                .Append(',').Append(Format(ankleTargetError))
                .Append(',').Append(Format(kneeTargetError))
                .Append(',').Append(Format(hipTargetError))
                .Append(',').Append(Format(abdomenTargetError))
                .Append(',').Append(Format(thoraxTargetError))
                .Append(',').Append(Format(maximumActiveDriveDemandNm))
                .Append(',').Append(Format(maximumActiveDriveDemandFraction))
                .Append(',').Append(worstActiveDriveJoint)
                .Append(',').Append(worstActiveDriveChannel)
                .Append(',').Append(Format(maximumJointAnchorSeparationM))
                .Append(',').Append(Tick(balanceLossOnsetTick))
                .Append(',').Append(Tick(balanceLossLatchedTick))
                .Append(',').Append(Tick(lastValidPreFailureTick))
                .Append(',').Append(progressValid ? "true" : "false")
                .Append(',').Append(Format(systemCom.X)).Append(',').Append(Format(systemCom.Y)).Append(',').Append(Format(systemCom.Z))
                .Append(',').Append(Format(systemComVelocity.X)).Append(',').Append(Format(systemComVelocity.Y)).Append(',').Append(Format(systemComVelocity.Z))
                .Append(',').Append(Format(supportCenter.X)).Append(',').Append(Format(supportCenter.Y)).Append(',').Append(Format(supportCenter.Z))
                .Append(',').Append(Format(support.SupportApMinM)).Append(',').Append(Format(support.SupportApMaxM))
                .Append(',').Append(Format(support.SupportMlMinM)).Append(',').Append(Format(support.SupportMlMaxM))
                .Append(',').Append(copAvailable ? "true" : "false")
                .Append(',').Append(copAvailable ? Format(cop.x) : "NA")
                .Append(',').Append(copAvailable ? Format(cop.y) : "NA")
                .Append(',').Append(copAvailable ? Format(cop.z) : "NA")
                .AppendLine();
        }

        private static string LockoutFailurePredicates(SquatPhysicalLockoutDiagnostic diagnostic)
        {
            var failed = new List<string>();
            if (!diagnostic.BarAvailable) failed.Add("bar_available");
            if (!diagnostic.StandingReferenceAvailable) failed.Add("standing_reference_available");
            if (!diagnostic.BarHeightPass) failed.Add("bar_height");
            if (!diagnostic.BarLinearStillnessPass) failed.Add("bar_linear_stillness");
            if (!diagnostic.BarAngularStillnessPass) failed.Add("bar_angular_stillness");
            if (!diagnostic.KneeJointsAvailable) failed.Add("knee_joint_telemetry");
            if (!diagnostic.KneeExtensionPass) failed.Add("knee_extension");
            if (!diagnostic.HipJointsAvailable) failed.Add("hip_joint_telemetry");
            if (!diagnostic.HipExtensionPass) failed.Add("hip_extension");
            if (!diagnostic.TrunkJointsAvailable) failed.Add("trunk_joint_telemetry");
            if (!diagnostic.TrunkErectnessPass) failed.Add("local_trunk_erectness");
            return failed.Count == 0 ? "NONE" : string.Join("|", failed);
        }

        private sealed class GAM13V2ActuatorTrace
        {
            private const string CapturePlaceholder = "CAPTURE_NONE";
            private readonly StringBuilder _csv = new StringBuilder(512 * 1024);
            private readonly Dictionary<ulong, SampleRange> _sampleRanges = new Dictionary<ulong, SampleRange>();
            private readonly Dictionary<ulong, List<string>> _capturePoints = new Dictionary<ulong, List<string>>();
            private int _headerLength;

            public GAM13V2ActuatorTrace()
            {
                _csv.AppendLine(
                    "load_kg,tick,phase,capture_points,joint_id,family,joint_kind,drive_channel,active," +
                    "requested_target_q_w,requested_target_q_x,requested_target_q_y,requested_target_q_z," +
                    "applied_target_q_w,applied_target_q_x,applied_target_q_y,applied_target_q_z," +
                    "actual_relative_q_w,actual_relative_q_x,actual_relative_q_y,actual_relative_q_z," +
                    "q_ref_target_angle_x_rad,q_ref_target_angle_y_rad,q_ref_target_angle_z_rad," +
                    "requested_target_angle_x_rad,requested_target_angle_y_rad,requested_target_angle_z_rad," +
                    "target_angle_x_rad,target_angle_y_rad,target_angle_z_rad," +
                    "actual_angle_x_rad,actual_angle_y_rad,actual_angle_z_rad,e_x_rad,e_y_rad,e_z_rad," +
                    "target_w_x_rad_s,target_w_y_rad_s,target_w_z_rad_s," +
                    "actual_w_x_rad_s,actual_w_y_rad_s,actual_w_z_rad_s," +
                    "e_dot_x_rad_s,e_dot_y_rad_s,e_dot_z_rad_s,K_nm_per_rad,D_nm_s_per_rad," +
                    "spring_torque_x_nm,spring_torque_y_nm,spring_torque_z_nm," +
                    "damping_torque_x_nm,damping_torque_y_nm,damping_torque_z_nm," +
                    "requested_torque_x_nm,requested_torque_y_nm,requested_torque_z_nm," +
                    "requested_channel_torque_nm,maximum_force_nm,normalized_demand," +
                    "twist_limit_proximity,anchor_separation_m");
                _headerLength = _csv.Length;
            }

            public float Append(float loadKg, SquatObservationSnapshot snapshot, SquatPhysicalPrototypeController controller)
            {
                int start = _csv.Length;
                float maximumDemand = 0f;
                PoweredJointController poweredController = controller.AthleteRig.PoweredController;
                foreach (PoweredJointController.PoweredJointRuntime joint in poweredController.Joints)
                {
                    if (!joint.Profile.HasValue)
                        continue;
                    if (!joint.HasPostPhysicsDiagnostic)
                        throw new InvalidOperationException($"Missing post-physics diagnostic for powered joint '{joint.Id}'.");

                    PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                    bool hasReference = controller.Adapter.TryGetTargetComposition(
                        joint.Id, out SquatPhysicalAdapter.JointTargetComposition composition);
                    bool referenceRequired = joint.Id == "left_foot" || joint.Id == "right_foot" ||
                        joint.Id == "left_shank" || joint.Id == "right_shank" ||
                        joint.Id == "left_thigh" || joint.Id == "right_thigh" ||
                        joint.Id == "abdomen" || joint.Id == "thorax";
                    if (referenceRequired && !hasReference)
                        throw new InvalidOperationException($"Missing nominal squat reference target for '{joint.Id}'.");
                    Vector3 referenceAngle = hasReference
                        ? RotationVector(composition.Nominal)
                        : new Vector3(float.NaN, float.NaN, float.NaN);
                    Vector3 requestedAngle = RotationVector(joint.RequestedCommand.TargetRelativeRotation);
                    Vector3 appliedAngle = RotationVector(diagnostic.AppliedTarget);
                    Vector3 actualAngle = RotationVector(diagnostic.ActualRelative);
                    Vector3 error = diagnostic.ErrorRad;
                    Vector3 targetVelocity = diagnostic.TargetAngularVelocityRadS;
                    Vector3 actualVelocity = diagnostic.ActualAngularVelocityRadS;
                    Vector3 velocityError = targetVelocity - actualVelocity;
                    float anchorSeparation = Vector3.Distance(
                        joint.Joint.transform.TransformPoint(joint.Joint.anchor),
                        joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor));

                    maximumDemand = Mathf.Max(maximumDemand, AppendChannel(
                        loadKg, snapshot, joint, diagnostic,
                        "twist_x", true, joint.Joint.angularXDrive,
                        referenceAngle, requestedAngle, appliedAngle, actualAngle, error, targetVelocity, actualVelocity,
                        velocityError, anchorSeparation));
                    bool hasSwingDrive = joint.Recipe.Kind == PhysicalJointKind.Ball;
                    maximumDemand = Mathf.Max(maximumDemand, AppendChannel(
                        loadKg, snapshot, joint, diagnostic,
                        "swing_yz", hasSwingDrive, joint.Joint.angularYZDrive,
                        referenceAngle, requestedAngle, appliedAngle, actualAngle, error, targetVelocity, actualVelocity,
                        velocityError, anchorSeparation));
                }

                _sampleRanges.Add(snapshot.SimulationTick, new SampleRange(start, _csv.Length - start));
                return maximumDemand;
            }

            public void Mark(ulong tick, string capturePoint)
            {
                if (!_sampleRanges.ContainsKey(tick))
                    return;
                if (!_capturePoints.TryGetValue(tick, out List<string> points))
                {
                    points = new List<string>();
                    _capturePoints.Add(tick, points);
                }
                if (!points.Contains(capturePoint))
                    points.Add(capturePoint);
            }

            public void Save(string path)
            {
                var captures = new List<KeyValuePair<ulong, List<string>>>(_capturePoints);
                captures.Sort((left, right) => left.Key.CompareTo(right.Key));
                var output = new StringBuilder(captures.Count * 128 * 1024);
                output.Append(_csv.ToString(0, _headerLength));
                foreach (KeyValuePair<ulong, List<string>> capture in captures)
                {
                    SampleRange range = _sampleRanges[capture.Key];
                    output.Append(_csv.ToString(range.Start, range.Length)
                        .Replace(CapturePlaceholder, string.Join("|", capture.Value)));
                }
                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, output.ToString(), new UTF8Encoding(false));
            }

            private float AppendChannel(
                float loadKg,
                SquatObservationSnapshot snapshot,
                PoweredJointController.PoweredJointRuntime joint,
                PoweredJointDiagnostic diagnostic,
                string channel,
                bool active,
                JointDrive drive,
                Vector3 referenceAngle,
                Vector3 requestedAngle,
                Vector3 appliedAngle,
                Vector3 actualAngle,
                Vector3 error,
                Vector3 targetVelocity,
                Vector3 actualVelocity,
                Vector3 velocityError,
                float anchorSeparation)
            {
                Vector3 spring = active ? error * drive.positionSpring : Vector3.zero;
                Vector3 damping = active ? velocityError * drive.positionDamper : Vector3.zero;
                Vector3 requested = spring + damping;
                float requestedChannelTorque = channel == "twist_x"
                    ? Mathf.Abs(requested.x)
                    : Mathf.Sqrt(requested.y * requested.y + requested.z * requested.z);
                float demand = active && drive.maximumForce > 0f
                    ? requestedChannelTorque / drive.maximumForce
                    : float.NaN;

                Value(loadKg); Value(snapshot.SimulationTick.ToString(CultureInfo.InvariantCulture));
                Value(snapshot.State.ToString()); Value(CapturePlaceholder);
                Value(joint.Id); Value(joint.Profile.Value.Id); Value(joint.Recipe.Kind.ToString());
                Value(channel); Value(active ? "true" : "false");
                QuaternionValue(joint.RequestedCommand.TargetRelativeRotation);
                QuaternionValue(diagnostic.AppliedTarget);
                QuaternionValue(diagnostic.ActualRelative);
                VectorValue(referenceAngle);
                VectorValue(requestedAngle); VectorValue(appliedAngle); VectorValue(actualAngle); VectorValue(error);
                VectorValue(targetVelocity); VectorValue(actualVelocity); VectorValue(velocityError);
                Value(active ? drive.positionSpring : 0f); Value(active ? drive.positionDamper : 0f);
                VectorValue(spring); VectorValue(damping); VectorValue(active ? requested : new Vector3(float.NaN, float.NaN, float.NaN));
                Value(active ? requestedChannelTorque : float.NaN);
                Value(active ? drive.maximumForce : 0f);
                Value(demand);
                Value(diagnostic.LimitProximity);
                Value(anchorSeparation);
                _csv.AppendLine();
                return float.IsFinite(demand) ? demand : 0f;
            }

            private static Vector3 RotationVector(Quaternion value)
            {
                float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z);
                if (magnitude <= 0.000001f)
                    return Vector3.zero;
                float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(value.w, -1f, 1f));
                return new Vector3(value.x, value.y, value.z) * (angle / magnitude);
            }

            private void QuaternionValue(Quaternion value)
            {
                Value(value.w); Value(value.x); Value(value.y); Value(value.z);
            }

            private void VectorValue(Vector3 value)
            {
                Value(value.x); Value(value.y); Value(value.z);
            }

            private void Value(float value)
            {
                Value(float.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "NA");
            }

            private void Value(string value)
            {
                if (_csv.Length > 0 && _csv[_csv.Length - 1] != '\n')
                    _csv.Append(',');
                _csv.Append(value);
            }

            private readonly struct SampleRange
            {
                public SampleRange(int start, int length)
                {
                    Start = start;
                    Length = length;
                }

                public int Start { get; }
                public int Length { get; }
            }
        }

        private sealed class PhaseZeroStandingCapture
        {
            public readonly StringBuilder Trace = new StringBuilder(64 * 1024);
            public bool ApSaturationCaptured;
            public bool RearMarginCaptured;
            public bool SetupQualificationCaptured;
            public ulong SetupQualificationTick;
            public bool SupportLossReached;
            public bool HasLastValidTick;
            public ulong LastValidTick;

            public PhaseZeroStandingCapture()
            {
                Trace.AppendLine(
                    "stage,capture_points,tick,time_s,state,phase_s,phase_velocity,support_available,support_present,left_foot_contact,right_foot_contact," +
                    "com_z_m,com_vz_mps,ap_front_margin_m,ap_rear_margin_m,ml_left_margin_m,ml_right_margin_m," +
                    "cop_available,cop_ap_m,cop_front_margin_m,cop_rear_margin_m,cop_left_margin_m,cop_right_margin_m," +
                    "ap_reference_error_m,raw_ap_command_rad,applied_ap_correction_rad,ap_saturated,max_joint_anchor_separation_m," +
                    "saddle_attached,saddle_broken,saddle_separation_m,saddle_limit_occupancy,saddle_relative_rotation_deg," +
                    "bar_x_m,bar_y_m,bar_z_m,bar_vx_mps,bar_vy_mps,bar_vz_mps");
            }
        }

        private static IEnumerator ContinuePhaseZeroStandingHold(
            SquatPhysicalPrototypeController controller,
            FoundationRuntime runtime,
            PhysicalAthleteRig rig,
            float loadKg,
            float dt,
            PhaseZeroStandingCapture capture,
            GAM13V2ActuatorTrace actuatorTrace,
            StringBuilder qualificationTrace,
            string phaseZeroTracePath)
        {
            const int maximumAdditionalTicks = 300;
            int additionalTicks = 0;
            while (additionalTicks < maximumAdditionalTicks && !capture.SupportLossReached)
            {
                Assert.That(controller.Adapter.State, Is.EqualTo(SquatState.SETUP),
                    "The phase-zero observation must remain in setup without a squat command.");
                Assert.That(controller.Adapter.Sq, Is.EqualTo(0f), "The phase must remain at standing lockout.");
                double inputTime = runtime.CurrentTime.SimulationTimeSeconds +
                    0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                runtime.InputBuffer.SetContinuous(IntentAction.Yield, 0f, inputTime);
                runtime.InputBuffer.SetContinuous(IntentAction.Drive, 0f, inputTime);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                additionalTicks++;

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                Assert.That(snapshot.Sq, Is.EqualTo(0f), "The observed phase must remain at zero.");
                actuatorTrace.Append(loadKg, snapshot, controller);
                bool supportLost = CapturePhaseZeroStandingSample(
                    capture, "PHASE0_HOLD", snapshot, controller, rig, actuatorTrace, false);
                string failureReason = supportLost ? "AUTHORITATIVE_SUPPORT_LOSS" : "NONE";
                AppendSample(qualificationTrace, loadKg, snapshot, snapshot, controller, rig,
                    SquatFailureCalibration.Default, legalDepthReached: false,
                    descentTick: null, reversalTick: null, ascentTick: null, lockout: false,
                    physicalFailure: supportLost, physicalFailureReason: failureReason);
                if (additionalTicks % 50 == 0)
                    yield return null;
            }

            if (!capture.SupportLossReached && capture.HasLastValidTick)
                actuatorTrace.Mark(capture.LastValidTick, "FINAL_VALID_SAMPLE_300_TICKS");
            SavePhaseZeroStandingTrace(capture, phaseZeroTracePath);
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V23M_PHASE0_STANDING setup_qualified={0} setup_tick={1} additional_ticks={2} support_loss={3} last_valid_tick={4} trace={5}",
                capture.SetupQualificationCaptured,
                Tick(capture.SetupQualificationCaptured ? (ulong?)capture.SetupQualificationTick : null),
                additionalTicks, capture.SupportLossReached,
                capture.HasLastValidTick ? Tick(capture.LastValidTick) : "NA", phaseZeroTracePath));
        }

        private static bool CapturePhaseZeroStandingSample(
            PhaseZeroStandingCapture capture,
            string stage,
            SquatObservationSnapshot snapshot,
            SquatPhysicalPrototypeController controller,
            PhysicalAthleteRig rig,
            GAM13V2ActuatorTrace actuatorTrace,
            bool setupQualified)
        {
            var markers = new List<string>(4);
            if (snapshot.SimulationTick == 50ul)
                markers.Add("TICK_50");
            if (setupQualified && !capture.SetupQualificationCaptured)
            {
                capture.SetupQualificationCaptured = true;
                capture.SetupQualificationTick = snapshot.SimulationTick;
                markers.Add("END_SETUP_QUALIFICATION");
            }

            SquatBalanceCorrectionV2 correction = controller.Adapter.BalanceCorrectionV2;
            if (!capture.ApSaturationCaptured && correction.IsApBoundSaturated)
            {
                capture.ApSaturationCaptured = true;
                markers.Add("FIRST_AP_SATURATION");
            }

            SquatSupportObservation support = snapshot.Support;
            bool supportAvailable = support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE;
            if (!capture.RearMarginCaptured && supportAvailable && support.HasSupport &&
                support.ComToSupportApRearMarginM < 0f)
            {
                capture.RearMarginCaptured = true;
                markers.Add("FIRST_NEGATIVE_REAR_MARGIN");
            }

            bool supportLost = IsPhaseZeroSupportLost(snapshot);
            if (supportLost && !capture.SupportLossReached)
            {
                capture.SupportLossReached = true;
                markers.Add("AUTHORITATIVE_SUPPORT_LOSS");
                if (capture.HasLastValidTick)
                    actuatorTrace.Mark(capture.LastValidTick, "FINAL_VALID_BEFORE_SUPPORT_LOSS");
            }
            else if (!capture.SupportLossReached)
            {
                capture.LastValidTick = snapshot.SimulationTick;
                capture.HasLastValidTick = true;
            }

            actuatorTrace.Mark(snapshot.SimulationTick, "PHASE0_STANDING_EVERY_TICK");
            foreach (string marker in markers)
                actuatorTrace.Mark(snapshot.SimulationTick, marker);
            AppendPhaseZeroStandingRow(capture.Trace, stage, string.Join("|", markers),
                snapshot, controller, rig);
            return supportLost;
        }

        private static bool IsPhaseZeroSupportLost(SquatObservationSnapshot snapshot)
        {
            SquatSupportObservation support = snapshot.Support;
            if (support.SupportAvailability != SquatTelemetryAvailability.AVAILABLE)
                return false;
            if (!support.HasSupport || !snapshot.LeftFoot.IsInContact || !snapshot.RightFoot.IsInContact)
                return true;

            float minimumMargin = Mathf.Min(
                support.ComToSupportApFrontMarginM,
                support.ComToSupportApRearMarginM,
                support.ComToSupportMlLeftMarginM,
                support.ComToSupportMlRightMarginM);
            return minimumMargin < 0f;
        }

        private static void AppendPhaseZeroStandingRow(
            StringBuilder trace,
            string stage,
            string capturePoints,
            SquatObservationSnapshot snapshot,
            SquatPhysicalPrototypeController controller,
            PhysicalAthleteRig rig)
        {
            SquatSupportObservation support = snapshot.Support;
            SquatBalanceCorrectionV2 correction = controller.Adapter.BalanceCorrectionV2;
            bool copAvailable = support.EngineContactPointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                support.HasSupport;
            Vector3Value cop = support.EngineContactPointWorldMeters;
            SquatBarSaddle saddle = controller.Saddle;
            GAM13V2StandingQualificationTests.MeasureConstraintHealth(
                rig, out float maximumAnchorSeparation, out _, out _);
            trace.AppendLine(string.Join(",", new[]
            {
                stage, capturePoints, Tick(snapshot.SimulationTick), Format((float)snapshot.SimulationTimeSeconds),
                snapshot.State.ToString(), Format(snapshot.Sq), Format(0f),
                support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE ? "true" : "false",
                support.HasSupport ? "true" : "false",
                snapshot.LeftFoot.IsInContact ? "true" : "false",
                snapshot.RightFoot.IsInContact ? "true" : "false",
                Format(support.SystemComWorldMeters.Z), Format(support.SystemComVelocityWorldMetersPerSecond.Z),
                Format(support.ComToSupportApFrontMarginM), Format(support.ComToSupportApRearMarginM),
                Format(support.ComToSupportMlLeftMarginM), Format(support.ComToSupportMlRightMarginM),
                copAvailable ? "true" : "false", copAvailable ? Format(cop.Z) : "NA",
                copAvailable ? Format(support.SupportApMaxM - cop.Z) : "NA",
                copAvailable ? Format(cop.Z - support.SupportApMinM) : "NA",
                copAvailable ? Format(support.SupportMlMaxM - cop.X) : "NA",
                copAvailable ? Format(cop.X - support.SupportMlMinM) : "NA",
                Format(correction.ErrorApM), Format(correction.CommandApRad), Format(correction.AppliedApRad),
                correction.IsApBoundSaturated ? "true" : "false", Format(maximumAnchorSeparation),
                saddle != null && saddle.IsAttached ? "true" : "false",
                saddle == null || saddle.IsBroken ? "true" : "false",
                Format(saddle != null ? saddle.SaddleSeparationMeters : float.NaN),
                Format(saddle != null ? saddle.CurrentLinearLimitOccupancy : float.NaN),
                Format(saddle != null ? saddle.RelativeRotationDegrees : float.NaN),
                Format(snapshot.Bar.PositionWorldMeters.X), Format(snapshot.Bar.PositionWorldMeters.Y),
                Format(snapshot.Bar.PositionWorldMeters.Z),
                Format(snapshot.Bar.LinearVelocityWorldMetersPerSecond.X),
                Format(snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y),
                Format(snapshot.Bar.LinearVelocityWorldMetersPerSecond.Z)
            }));
        }

        private static void SavePhaseZeroStandingTrace(PhaseZeroStandingCapture capture, string path)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, capture.Trace.ToString(), new UTF8Encoding(false));
        }

        private static IEnumerator RunGateCStaticHold(
            SquatPhysicalPrototypeController controller,
            FoundationRuntime runtime,
            PhysicalAthleteRig rig,
            SquatObservationSnapshot standingReference,
            float loadKg,
            float dt,
            StringBuilder qualificationTrace,
            GAM13V2ActuatorTrace actuatorTrace,
            StringBuilder gateCTrace,
            string gateCTracePath)
        {
            const float targetPhase = 0.21f;
            const int maximumRampTicks = 1000;
            const int boundedSettleTicks = 300;
            gateCTrace.AppendLine(
                "stage,load_kg,tick,state,phase,phase_velocity,ap_reference_error_m,raw_ap_correction_rad,applied_ap_correction_rad,ap_saturated," +
                "bar_ap_velocity_mps,com_vx_mps,com_vy_mps,com_vz_mps,com_support_front_margin_m,com_support_rear_margin_m," +
                "com_support_left_margin_m,com_support_right_margin_m,cop_available,cop_ap_m,cop_ml_m," +
                "cop_support_front_margin_m,cop_support_rear_margin_m,cop_support_left_margin_m,cop_support_right_margin_m," +
                "maximum_active_drive_demand_nm,worst_demand_joint,worst_demand_channel,worst_demand_fraction,max_joint_anchor_separation_m," +
                "saddle_attached,saddle_broken,saddle_separation_m,saddle_limit_occupancy,saddle_relative_rotation_deg,physical_failure_reason");

            int rampTicks = 0;
            while (controller.Adapter.Sq < targetPhase && rampTicks < maximumRampTicks &&
                (controller.Saddle == null || !controller.Saddle.IsBroken))
            {
                double inputTime = runtime.CurrentTime.SimulationTimeSeconds +
                    0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                runtime.InputBuffer.SetContinuous(IntentAction.Yield, 1f, inputTime);
                runtime.InputBuffer.SetContinuous(IntentAction.Drive, 0f, inputTime);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                rampTicks++;

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                actuatorTrace.Append(loadKg, snapshot, controller);
                GAM13V2StandingQualificationTests.MeasureConstraintHealth(
                    rig, out float anchorSeparation, out _, out _);
                MeasureMaximumActiveJointDemand(rig.PoweredController,
                    out float demandNm, out string demandJoint, out string demandChannel, out float demandFraction);
                string failureReason = FindPhysicalFailure(controller, snapshot);
                AppendGateCStaticSample(gateCTrace, "RAMP", loadKg, controller.Adapter.PhaseRate,
                    snapshot, controller, demandNm, demandJoint, demandChannel, demandFraction,
                    anchorSeparation, failureReason);
                AppendSample(qualificationTrace, loadKg, standingReference, snapshot, controller, rig,
                    SquatFailureCalibration.Default, controller.Adapter.LegalDepth, null, null, null,
                    false, failureReason != "NONE", failureReason);
                if (rampTicks % 50 == 0)
                    yield return null;
            }

            float heldPhase = controller.Adapter.Sq;
            bool reachedHoldPhase = heldPhase >= 0.20f && heldPhase <= 0.22f;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V23M_GATE_C_RAMP phase={0:R} ramp_ticks={1} reached_target_band={2} failure={3}",
                heldPhase, rampTicks, reachedHoldPhase, controller.Adapter.FailureReason));
            if (reachedHoldPhase)
            {
                controller.Adapter.HoldReferencePhaseForQualification(
                    heldPhase, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);
                actuatorTrace.Mark(runtime.CurrentTime.Tick, "GATE_C_HOLD_START");

                for (int settleTick = 0; settleTick < boundedSettleTicks; settleTick++)
                {
                    double inputTime = runtime.CurrentTime.SimulationTimeSeconds +
                        0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                    runtime.InputBuffer.SetContinuous(IntentAction.Yield, 0f, inputTime);
                    runtime.InputBuffer.SetContinuous(IntentAction.Drive, 0f, inputTime);
                    Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                    TickFeet(controller, dt);

                    SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                    actuatorTrace.Append(loadKg, snapshot, controller);
                    actuatorTrace.Mark(snapshot.SimulationTick, "GATE_C_HOLD_TICK");
                    GAM13V2StandingQualificationTests.MeasureConstraintHealth(
                        rig, out float anchorSeparation, out _, out _);
                    MeasureMaximumActiveJointDemand(rig.PoweredController,
                        out float demandNm, out string demandJoint, out string demandChannel, out float demandFraction);
                    string failureReason = FindPhysicalFailure(controller, snapshot);
                    AppendGateCStaticSample(gateCTrace, "HOLD", loadKg, 0f,
                        snapshot, controller, demandNm, demandJoint, demandChannel, demandFraction,
                        anchorSeparation, failureReason);
                    AppendSample(qualificationTrace, loadKg, standingReference, snapshot, controller, rig,
                        SquatFailureCalibration.Default, controller.Adapter.LegalDepth, null, null, null,
                        false, failureReason != "NONE", failureReason);
                    if ((settleTick + 1) % 50 == 0)
                        yield return null;
                }

                actuatorTrace.Mark(runtime.CurrentTime.Tick, "GATE_C_HOLD_END");
            }

            string fullPath = Path.GetFullPath(gateCTracePath);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, gateCTrace.ToString(), new UTF8Encoding(false));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V23M_GATE_C_STATIC_HOLD reached_target_band={0} held_phase={1:R} settle_ticks={2} trace={3}",
                reachedHoldPhase, heldPhase, reachedHoldPhase ? boundedSettleTicks : 0, gateCTracePath));
        }

        private static void AppendGateCStaticSample(
            StringBuilder trace,
            string stage,
            float loadKg,
            float phaseVelocity,
            SquatObservationSnapshot snapshot,
            SquatPhysicalPrototypeController controller,
            float demandNm,
            string demandJoint,
            string demandChannel,
            float demandFraction,
            float anchorSeparation,
            string failureReason)
        {
            SquatSupportObservation support = snapshot.Support;
            bool copAvailable = support.EngineContactPointAvailability == SquatTelemetryAvailability.AVAILABLE &&
                support.HasSupport;
            Vector3Value cop = support.EngineContactPointWorldMeters;
            SquatBalanceCorrectionV2 correction = controller.Adapter.BalanceCorrectionV2;
            SquatBarSaddle saddle = controller.Saddle;
            trace.AppendLine(string.Join(",", new[]
            {
                stage, Format(loadKg), Tick(snapshot.SimulationTick), snapshot.State.ToString(),
                Format(snapshot.Sq), Format(phaseVelocity), Format(correction.ErrorApM),
                Format(correction.CommandApRad), Format(correction.AppliedApRad),
                correction.IsApBoundSaturated ? "true" : "false",
                Format(snapshot.Bar.LinearVelocityWorldMetersPerSecond.Z),
                Format(support.SystemComVelocityWorldMetersPerSecond.X),
                Format(support.SystemComVelocityWorldMetersPerSecond.Y),
                Format(support.SystemComVelocityWorldMetersPerSecond.Z),
                Format(support.ComToSupportApFrontMarginM), Format(support.ComToSupportApRearMarginM),
                Format(support.ComToSupportMlLeftMarginM), Format(support.ComToSupportMlRightMarginM),
                copAvailable ? "true" : "false", copAvailable ? Format(cop.Z) : "NA", copAvailable ? Format(cop.X) : "NA",
                copAvailable ? Format(support.SupportApMaxM - cop.Z) : "NA",
                copAvailable ? Format(cop.Z - support.SupportApMinM) : "NA",
                copAvailable ? Format(support.SupportMlMaxM - cop.X) : "NA",
                copAvailable ? Format(cop.X - support.SupportMlMinM) : "NA",
                Format(demandNm), demandJoint, demandChannel, Format(demandFraction), Format(anchorSeparation),
                saddle != null && saddle.IsAttached ? "true" : "false",
                saddle == null || saddle.IsBroken ? "true" : "false",
                Format(saddle != null ? saddle.SaddleSeparationMeters : float.NaN),
                Format(saddle != null ? saddle.CurrentLinearLimitOccupancy : float.NaN),
                Format(saddle != null ? saddle.RelativeRotationDegrees : float.NaN), failureReason
            }));
        }

        private static int ReadLockoutExtensionTicks()
        {
            string value = Environment.GetEnvironmentVariable("GAM50_LOCKOUT_EXTENSION_TICKS");
            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int ticks)
                ? Mathf.Clamp(ticks, 0, 300)
                : 0;
        }

        private static IEnumerator ContinueLockoutExtension(
            SquatPhysicalPrototypeController controller,
            FoundationRuntime runtime,
            PhysicalAthleteRig rig,
            PhysicalBarbell barbell,
            SquatObservationSnapshot standingReference,
            SquatFailureCalibration calibration,
            float loadKg,
            float dt,
            int extensionTicks,
            ulong lockoutStartTick,
            string tracePath,
            PhysicsStateHashLog stateHashes)
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,ticks_since_lockout_start,state,is_lockout,consecutive_lockout,failed_predicates," +
                "bar_speed_mps,bar_angular_speed_rad_s,bar_vy_mps,max_knee_rad,max_hip_rad,max_trunk_rad," +
                "com_vx_mps,com_vz_mps,com_speed_mps,support_margin_m,max_demand_nm,max_demand_fraction,max_demand_joint," +
                "max_anchor_separation_m,saddle_separation_m,both_feet");
            int consecutive = 0;
            ulong? naturalLockoutTick = null;
            for (int i = 0; i < extensionTicks; i++)
            {
                SquatState phase = controller.Adapter.State;
                double inputTime = runtime.CurrentTime.SimulationTimeSeconds + 0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                bool yieldInput = phase == SquatState.SQUAT_COMMAND || phase == SquatState.DESCENT;
                bool driveInput = phase == SquatState.BOTTOM || phase == SquatState.REVERSAL ||
                    phase == SquatState.ASCENT || phase == SquatState.STICKING;
                runtime.InputBuffer.SetContinuous(IntentAction.Yield, yieldInput ? 1f : 0f, inputTime);
                runtime.InputBuffer.SetContinuous(IntentAction.Drive, driveInput ? 1f : 0f, inputTime);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                stateHashes?.AppendState(runtime.CurrentTime.Tick, "EXTENSION", rig, barbell);
                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                SquatPhysicalLockoutDiagnostic lockout = SquatAttemptPhysicalEvidence.MeasureLockout(
                    snapshot, standingReference.Bar.IsAvailable, standingReference.Bar.PositionWorldMeters.Y, calibration);
                consecutive = lockout.IsLockout ? consecutive + 1 : 0;
                if (!naturalLockoutTick.HasValue && consecutive >= RequiredLockoutSamples)
                    naturalLockoutTick = snapshot.SimulationTick;
                MeasureMaximumActiveJointDemand(rig.PoweredController, out float demandNm, out string demandJoint, out _,
                    out float demandFraction);
                GAM13V2StandingQualificationTests.MeasureConstraintHealth(rig, out float anchorSeparation, out _, out _);
                Vector3Value comVelocity = snapshot.Support.SystemComVelocityWorldMetersPerSecond;
                float margin = Mathf.Min(
                    Mathf.Min(snapshot.Support.ComToSupportApFrontMarginM, snapshot.Support.ComToSupportApRearMarginM),
                    Mathf.Min(snapshot.Support.ComToSupportMlLeftMarginM, snapshot.Support.ComToSupportMlRightMarginM));
                csv.AppendLine(string.Join(",", new[]
                {
                    Format(loadKg), Tick(snapshot.SimulationTick),
                    (snapshot.SimulationTick - lockoutStartTick).ToString(CultureInfo.InvariantCulture),
                    snapshot.State.ToString(), lockout.IsLockout ? "true" : "false",
                    consecutive.ToString(CultureInfo.InvariantCulture), LockoutFailurePredicates(lockout),
                    Format(lockout.BarSpeed), Format(lockout.BarAngularSpeed),
                    Format(snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y),
                    Format(lockout.MaximumKneeAngle), Format(lockout.MaximumHipAngle), Format(lockout.MaximumTrunkAngle),
                    Format(comVelocity.X), Format(comVelocity.Z),
                    Format(Mathf.Sqrt(comVelocity.X * comVelocity.X + comVelocity.Z * comVelocity.Z)), Format(margin),
                    Format(demandNm), Format(demandFraction), demandJoint, Format(anchorSeparation),
                    Format(controller.Saddle != null ? controller.Saddle.SaddleSeparationMeters : float.NaN),
                    snapshot.LeftFoot.IsInContact && snapshot.RightFoot.IsInContact ? "true" : "false"
                }));
                if (i % 50 == 0)
                    yield return null;
            }
            string path = Path.ChangeExtension(tracePath, ".lockout-extension.csv");
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM50_LOCKOUT_EXTENSION load={0:F1}kg extension_ticks={1} natural_lockout_tick={2} lockout_start_tick={3} trace={4}",
                loadKg, extensionTicks, Tick(naturalLockoutTick), lockoutStartTick, path));
        }

        /// <summary>Bit-exact per-tick hash of every athlete and bar body state (GAM-50 determinism).</summary>
        private sealed class PhysicsStateHashLog
        {
            private readonly StringBuilder _rows = new StringBuilder("tick,stage,state_hash\n");

            public void AppendState(ulong tick, string stage, PhysicalAthleteRig rig, PhysicalBarbell barbell)
            {
                ulong hash = 1469598103934665603UL;
                void Mix(float value)
                {
                    uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
                    for (int i = 0; i < 4; i++)
                    {
                        hash ^= (bits >> (8 * i)) & 0xFF;
                        hash *= 1099511628211UL;
                    }
                }
                void MixBody(Rigidbody body)
                {
                    Vector3 p = body.position; Quaternion r = body.rotation;
                    Vector3 v = body.linearVelocity; Vector3 w = body.angularVelocity;
                    Mix(p.x); Mix(p.y); Mix(p.z); Mix(r.x); Mix(r.y); Mix(r.z); Mix(r.w);
                    Mix(v.x); Mix(v.y); Mix(v.z); Mix(w.x); Mix(w.y); Mix(w.z);
                }
                foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                    MixBody(rig.Segments[recipe.Id].Body);
                if (barbell.Body != null && barbell.Body.gameObject.activeInHierarchy)
                    MixBody(barbell.Body);
                _rows.Append(tick.ToString(CultureInfo.InvariantCulture)).Append(',').Append(stage).Append(',')
                    .Append(hash.ToString("x16", CultureInfo.InvariantCulture)).Append('\n');
            }

            public void Save(string path)
            {
                string fullPath = Path.GetFullPath(path);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                File.WriteAllText(fullPath, _rows.ToString(), new UTF8Encoding(false));
            }
        }

        private static bool ReadEnvironmentFlag(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
            return string.Equals(value, "1", StringComparison.Ordinal) ||
                bool.TryParse(value, out bool enabled) && enabled;
        }

        private static void TickFeet(SquatPhysicalPrototypeController controller, float dt)
        {
            controller.LeftFootContact?.PhysicsTickUpdate(dt);
            controller.RightFootContact?.PhysicsTickUpdate(dt);
        }

        private static float ReadLoadKg()
        {
            string value = Environment.GetEnvironmentVariable("GAM13_V2_LOAD_KG");
            return float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float loadKg)
                ? loadKg
                : 25f;
        }

        private static void SaveTrace(string path, StringBuilder trace)
        {
            string fullPath = Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
            File.WriteAllText(fullPath, trace.ToString());
        }

        private static void InvalidateMechanicsProgressAtOrAfterOnset(StringBuilder trace, ulong onsetTick)
        {
            string[] lines = trace.ToString().Split(new[] { '\n' }, StringSplitOptions.None);
            if (lines.Length == 0)
                return;
            string[] header = lines[0].TrimEnd('\r').Split(',');
            int tickColumn = Array.IndexOf(header, "tick");
            int progressColumn = Array.IndexOf(header, "progress_valid");
            if (tickColumn < 0 || progressColumn < 0)
                throw new InvalidOperationException("The mechanics trace is missing balance-progress columns.");

            for (int index = 1; index < lines.Length; index++)
            {
                string line = lines[index].TrimEnd('\r');
                if (line.Length == 0)
                    continue;
                string[] columns = line.Split(',');
                if (columns.Length <= Math.Max(tickColumn, progressColumn) ||
                    !ulong.TryParse(columns[tickColumn], NumberStyles.None, CultureInfo.InvariantCulture, out ulong tick) ||
                    tick < onsetTick)
                    continue;
                columns[progressColumn] = "false";
                lines[index] = string.Join(",", columns);
            }

            trace.Clear();
            trace.Append(string.Join("\n", lines));
        }

        private static void SaveBalanceLossTerminationContext(
            string tracePath,
            string actuatorTracePath,
            SquatFailureCalibration calibration,
            SquatFailureEvent balanceLoss,
            ulong? lastValidTick)
        {
            string contextPath = Path.Combine(
                Path.GetDirectoryName(Path.GetFullPath(tracePath)), "authoritative-failure-context.txt");
            var context = new StringBuilder(1024);
            context.AppendLine("authority=GAM12_SQUAT_FAILURE_DETECTOR");
            context.AppendLine("failure_kind=BALANCE_LOSS");
            context.AppendLine("direction=" + balanceLoss.Direction);
            context.AppendLine("support_margin_threshold_m=" + calibration.BalanceSupportMarginFailureM.ToString("R", CultureInfo.InvariantCulture));
            context.AppendLine("outward_com_velocity_threshold_mps=" + calibration.BalanceOutwardComVelocityMps.ToString("R", CultureInfo.InvariantCulture));
            context.AppendLine("persistence_ticks=" + calibration.BalancePersistenceTicks.ToString(CultureInfo.InvariantCulture));
            context.AppendLine("onset_tick=" + Tick(balanceLoss.OnsetTick));
            context.AppendLine("latched_tick=" + Tick(balanceLoss.LatchedTick));
            context.AppendLine("last_valid_pre_failure_tick=" + Tick(lastValidTick));
            context.AppendLine("onset_phase=" + balanceLoss.OnsetContext.State);
            context.AppendLine("onset_phase_s=" + balanceLoss.OnsetContext.Sq.ToString("R", CultureInfo.InvariantCulture));
            context.AppendLine("onset_context_tick=" + Tick(balanceLoss.OnsetContext.SimulationTick));
            context.AppendLine("latched_context_tick=" + Tick(balanceLoss.LatchedContext.SimulationTick));
            context.AppendLine("qualification_trace=" + Path.GetFullPath(tracePath));
            context.AppendLine("actuator_trace=" + Path.GetFullPath(actuatorTracePath));
            context.AppendLine("last_valid_actuator_capture=GAM12_LAST_VALID_PRE_FAILURE");
            File.WriteAllText(contextPath, context.ToString(), new UTF8Encoding(false));
        }

        private static string Tick(ulong? tick) =>
            tick.HasValue ? tick.Value.ToString(CultureInfo.InvariantCulture) : "NA";

        private static string Format(float value) =>
            float.IsNaN(value) ? "NA" : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
