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
            trace.AppendLine("load_kg,tick,time_s,phase,direction,legal_depth,legal_depth_reached,depth_left_m,depth_right_m,worst_side_depth_m,descent_tick,reversal_tick,ascent_tick,lockout,physical_failure,physical_failure_reason,bar_y_m,bar_vx_mps,bar_vy_mps,bar_vz_mps,support_available,support_present,support_contact_count,left_foot_contact,right_foot_contact,left_foot_slip_mps,right_foot_slip_mps,ap_front_margin_m,ap_rear_margin_m,ml_left_margin_m,ml_right_margin_m,joint_limit_proximity,worst_limit_joint,saddle_attached,saddle_separation_m,saddle_initial_anchor_error_m,saddle_linear_limit_occupancy,saddle_relative_rotation_deg,saddle_is_broken,lockout_qualified,lockout_failed_predicates,lockout_reference_bar_y_m,lockout_bar_linear_speed_mps,lockout_bar_angular_speed_rad_s,lockout_max_knee_angle_rad,lockout_max_hip_angle_rad,lockout_max_local_trunk_angle_rad,lockout_height_tolerance_m,lockout_bar_speed_tolerance_mps,lockout_bar_angular_speed_tolerance_rad_s,lockout_knee_tolerance_rad,lockout_hip_tolerance_rad,lockout_trunk_tolerance_rad");

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

            FoundationRuntime runtime = bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul));
            Assert.That(controller.AttemptOrchestrator.HasStarted, Is.False);
            Assert.That(controller.AttemptOrchestrator.HasSquatCommand, Is.False);
            ulong lifecycleSquatCommandTick = controller.AttemptOrchestrator.SquatCommandTick;
            Assert.That(lifecycleSquatCommandTick, Is.EqualTo(SquatAttemptEventTicks.NotAvailable));
            Assert.That(controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
            Assert.That(controller.AttemptRecord, Is.Null);

            SquatObservationSnapshot standingReference = default;
            int consecutiveStandingSamples = 0;
            int setupTicks = 0;
            string physicsContractPath = Environment.GetEnvironmentVariable("GAM13_V2_PHYSICS_CONTRACT_PATH");
            GAM13V23BSubstrateAuditTests.WriteRuntimeReceipt(
                controller, runtime, loadKg, physicsContractPath, "GAM-13 V2-3I");
            while (consecutiveStandingSamples < RequiredSetupQualificationSamples &&
                setupTicks < SetupQualificationTimeoutTicks)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
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
                if (setupTicks % 50 == 0)
                    yield return null;
            }

            bool physicalStandingQualified = consecutiveStandingSamples >= RequiredSetupQualificationSamples;
            if (!physicalStandingQualified)
            {
                SaveTrace(tracePath, trace);
                actuatorTrace.Save(actuatorTracePath);
                Debug.Log($"GAM13_V2_SQUAT_MECHANICS load={loadKg:F1}kg PHYSICAL_STANDING_QUALIFIED=false setup_samples={consecutiveStandingSamples}/{RequiredSetupQualificationSamples} setup_ticks={setupTicks} physical_failure=SETUP_NOT_PHYSICALLY_QUALIFIED trace={tracePath}");
                yield break;
            }

            controller.BeginPhysicalSquatMotionForQualification();
            Assert.That(controller.Adapter.State, Is.EqualTo(SquatState.SQUAT_COMMAND));
            Assert.That(controller.AttemptOrchestrator.HasStarted, Is.False);
            Assert.That(controller.AttemptOrchestrator.SquatCommandTick, Is.EqualTo(lifecycleSquatCommandTick));
            Assert.That(controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
            Assert.That(controller.AttemptRecord, Is.Null);

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
            int consecutiveLockoutSamples = 0;
            float lowestBarY = float.PositiveInfinity;
            float peakAscentDemand = float.NegativeInfinity;
            ulong? peakAscentDemandTick = null;
            bool bottomCaptured = false;
            int mechanicsTicks = 0;

            while (!lockout && !physicalFailure && mechanicsTicks < MaximumMechanicsTicks)
            {
                SquatState phaseBeforeStep = controller.Adapter.State;
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

                Assert.That(controller.AttemptOrchestrator.HasStarted, Is.False,
                    "The mechanics probe must not start the GAM-12 attempt lifecycle.");
                Assert.That(controller.AttemptOrchestrator.SquatCommandTick, Is.EqualTo(lifecycleSquatCommandTick),
                    "The mechanics probe must not change the GAM-12 Squat command tick.");
                Assert.That(controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
                Assert.That(controller.AttemptRecord, Is.Null);

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
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

                legalDepthReached |= controller.Adapter.LegalDepth;
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

                physicalFailureReason = FindPhysicalFailure(controller, snapshot);
                physicalFailure = physicalFailureReason != "NONE";
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
                    physicalFailure, physicalFailureReason);
                if (mechanicsTicks % 50 == 0)
                    yield return null;
            }

            if (peakAscentDemandTick.HasValue)
                actuatorTrace.Mark(peakAscentDemandTick.Value, "PEAK_ASCENT_DEMAND");
            SaveTrace(tracePath, trace);
            actuatorTrace.Save(actuatorTracePath);
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
                "GAM13_V2_SQUAT_MECHANICS load={0:F1}kg PHYSICAL_STANDING_QUALIFIED=true legal_physical_depth={1} descent_tick={2} reversal_tick={3} ascent_tick={4} lockout={5} physical_failure={6} physical_failure_reason={7} trace={8} actuators={9}",
                loadKg, legalDepthReached, Tick(descentTick), Tick(reversalTick), Tick(ascentTick),
                lockout, physicalFailure, physicalFailureReason, tracePath, actuatorTracePath));
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
            SquatObservationSnapshot snapshot)
        {
            if (!string.Equals(controller.Adapter.FailureReason, "NONE", StringComparison.Ordinal))
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
            string physicalFailureReason)
        {
            float maximumLimitProximity = 0f;
            string worstLimitJoint = "NONE";
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !joint.HasPostPhysicsDiagnostic)
                    continue;
                float proximity = joint.PostPhysicsDiagnostic.LimitProximity;
                if (proximity > maximumLimitProximity)
                {
                    maximumLimitProximity = proximity;
                    worstLimitJoint = joint.Id;
                }
            }

            SquatBarObservation bar = snapshot.Bar;
            SquatSupportObservation support = snapshot.Support;
            Vector3Value velocity = bar.LinearVelocityWorldMetersPerSecond;
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
                .Append(snapshot.State).Append(',').Append(snapshot.Direction).Append(',')
                .Append(controller.Adapter.LegalDepth ? "true" : "false").Append(',')
                .Append(legalDepthReached ? "true" : "false").Append(',')
                .Append(Format(controller.Adapter.RuleDepthLeftM)).Append(',')
                .Append(Format(controller.Adapter.RuleDepthRightM)).Append(',')
                .Append(Format(controller.Adapter.WorstSideDepthM)).Append(',')
                .Append(Tick(descentTick)).Append(',').Append(Tick(reversalTick)).Append(',')
                .Append(Tick(ascentTick)).Append(',').Append(lockout ? "true" : "false").Append(',')
                .Append(physicalFailure ? "true" : "false").Append(',').Append(physicalFailureReason).Append(',')
                .Append(Format(bar.PositionWorldMeters.Y)).Append(',')
                .Append(Format(velocity.X)).Append(',').Append(Format(velocity.Y)).Append(',').Append(Format(velocity.Z)).Append(',')
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
                .Append(Format(failureCalibration.LockoutTrunkToleranceRadians))
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
                        requestedAngle, appliedAngle, actualAngle, error, targetVelocity, actualVelocity,
                        velocityError, anchorSeparation));
                    bool hasSwingDrive = joint.Recipe.Kind == PhysicalJointKind.Ball;
                    maximumDemand = Mathf.Max(maximumDemand, AppendChannel(
                        loadKg, snapshot, joint, diagnostic,
                        "swing_yz", hasSwingDrive, joint.Joint.angularYZDrive,
                        requestedAngle, appliedAngle, actualAngle, error, targetVelocity, actualVelocity,
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

        private static string Tick(ulong? tick) =>
            tick.HasValue ? tick.Value.ToString(CultureInfo.InvariantCulture) : "NA";

        private static string Format(float value) =>
            float.IsNaN(value) ? "NA" : value.ToString("R", CultureInfo.InvariantCulture);
    }
}
