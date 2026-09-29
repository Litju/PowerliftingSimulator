using System;
using System.Collections;
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
            var trace = new StringBuilder();
            trace.AppendLine("load_kg,tick,time_s,phase,direction,legal_depth,legal_depth_reached,depth_left_m,depth_right_m,worst_side_depth_m,descent_tick,reversal_tick,ascent_tick,lockout,physical_failure,physical_failure_reason,bar_y_m,bar_vx_mps,bar_vy_mps,bar_vz_mps,support_available,support_present,support_contact_count,left_foot_contact,right_foot_contact,ap_front_margin_m,ap_rear_margin_m,ml_left_margin_m,ml_right_margin_m,joint_limit_proximity,worst_limit_joint,saddle_attached,saddle_separation_m");

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
                bool setupTimedOut = setupTicks >= SetupQualificationTimeoutTicks &&
                    consecutiveStandingSamples < RequiredSetupQualificationSamples;
                AppendSample(trace, loadKg, standingReference, controller, rig, false,
                    null, null, null, false, setupTimedOut,
                    setupTimedOut ? "SETUP_NOT_PHYSICALLY_QUALIFIED" : "NONE");
                if (setupTicks % 50 == 0)
                    yield return null;
            }

            bool physicalStandingQualified = consecutiveStandingSamples >= RequiredSetupQualificationSamples;
            if (!physicalStandingQualified)
            {
                SaveTrace(tracePath, trace);
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
                physicalFailureReason = FindPhysicalFailure(controller, snapshot);
                physicalFailure = physicalFailureReason != "NONE";
                if (controller.Adapter.LockoutReached && !lockoutStartTick.HasValue)
                {
                    lockoutStartTick = snapshot.SimulationTick;
                }
                if (!physicalFailure && lockoutStartTick.HasValue)
                {
                    consecutiveLockoutSamples = SquatAttemptPhysicalEvidence.IsLockout(
                        snapshot, standingReference, failureCalibration)
                        ? consecutiveLockoutSamples + 1
                        : 0;
                    lockout = consecutiveLockoutSamples >= RequiredLockoutSamples;
                    bool lockoutSettlingTimedOut = snapshot.SimulationTick - lockoutStartTick.Value + 1ul >=
                        (ulong)LockoutSettlingTimeoutTicks;
                    if (!lockout && lockoutSettlingTimedOut)
                    {
                        physicalFailure = true;
                        physicalFailureReason = LockoutFailureReason(
                            hasDescent, legalDepthReached, hasReversal, hasAscent);
                    }
                }

                AppendSample(trace, loadKg, snapshot, controller, rig, legalDepthReached,
                    descentTick, reversalTick, ascentTick, lockout, physicalFailure, physicalFailureReason);
                if (mechanicsTicks % 50 == 0)
                    yield return null;
            }

            SaveTrace(tracePath, trace);
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
                "GAM13_V2_SQUAT_MECHANICS load={0:F1}kg PHYSICAL_STANDING_QUALIFIED=true legal_physical_depth={1} descent_tick={2} reversal_tick={3} ascent_tick={4} lockout={5} physical_failure={6} physical_failure_reason={7} trace={8}",
                loadKg, legalDepthReached, Tick(descentTick), Tick(reversalTick), Tick(ascentTick),
                lockout, physicalFailure, physicalFailureReason, tracePath));
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
            SquatObservationSnapshot snapshot,
            SquatPhysicalPrototypeController controller,
            PhysicalAthleteRig rig,
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
            bool saddleAttached = controller.Saddle != null && controller.Saddle.IsAttached && !controller.Saddle.IsBroken;
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
                .Append(Format(support.ComToSupportApFrontMarginM)).Append(',')
                .Append(Format(support.ComToSupportApRearMarginM)).Append(',')
                .Append(Format(support.ComToSupportMlLeftMarginM)).Append(',')
                .Append(Format(support.ComToSupportMlRightMarginM)).Append(',')
                .Append(Format(maximumLimitProximity)).Append(',').Append(worstLimitJoint).Append(',')
                .Append(saddleAttached ? "true" : "false").Append(',').Append(Format(saddleSeparation))
                .AppendLine();
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
