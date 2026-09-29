using System;
using System.Collections;
using System.Globalization;
using System.IO;
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
    public sealed class GAM13V2StandingQualificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int QualificationTicks = 500;
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
        public IEnumerator GAM13_V2_FRESH_PROCESS_STANDING_QUALIFICATION()
        {
            float loadKg = ReadLoadKg();
            bool isLoaded = loadKg > 0f;
            string tracePath = Environment.GetEnvironmentVariable("GAM13_V2_TRACE_PATH") ??
                Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-13", "v2-standing",
                    "load-" + loadKg.ToString("000", CultureInfo.InvariantCulture) + "kg.csv");

            AsyncOperation loadScene = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(loadScene, Is.Not.Null, "The GAM-13 qualification scene is missing.");
            while (!loadScene.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            SquatPhysicalPrototypeController controller =
                UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);
            controller.enabled = false;
            bootstrap.enabled = false;

            controller.SetLoad(loadKg);
            PhysicalBarbell physicalBarbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            Assert.That(controller.Saddle != null, Is.EqualTo(isLoaded));
            if (isLoaded)
            {
                Assert.That(controller.Saddle.IsAttached, Is.True);
                Assert.That(controller.Saddle.IsBroken, Is.False);
                Assert.That(controller.Saddle.Barbell.LoadedMassKg, Is.EqualTo(loadKg).Within(0.001f));
            }
            else
            {
                Assert.That(physicalBarbell, Is.Not.Null);
                Assert.That(physicalBarbell.Body.gameObject.activeInHierarchy, Is.False);
            }
            Assert.That(controller.Adapter.HasStandingComReference, Is.True);

            FoundationRuntime runtime = bootstrap.Runtime;
            Assert.That(controller.TickZeroSubstrateValidated, Is.True,
                "The physical substrate was not validated before the first authoritative simulation tick.");
            Assert.That(controller.TickZeroValidationTick, Is.EqualTo(0ul));
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul),
                "Standing qualification must begin from the tick-0 reset state.");
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            var trace = new GAM13V2QualificationTrace();
            if (isLoaded)
                controller.BeginAttempt();

            bool finite = true;
            bool bilateralSupportEverySample = true;
            bool attachedBarEverySample = true;
            bool apCorrectionMappingConsistent = true;
            bool referenceErrorConsistent = true;
            // SignedTwistRadians uses Quaternion.ToAngleAxis; below 1 mrad its float W can round to 1 and report zero.
            const float MinimumMappingCorrectionRad = 0.001f;
            bool hasStartWindow = false;
            bool squatCommandIssued = false;
            int startWindowSampleCount = 0;
            float minimumPelvisY = float.PositiveInfinity;
            float minimumBarY = float.PositiveInfinity;
            float maximumTrunkPitch = 0f;
            float maximumComSpeed = 0f;
            float minimumComSupportMargin = float.PositiveInfinity;
            float maximumJointAnchorSeparation = 0f;
            float maximumSaddleSeparation = 0f;
            float maximumSaddleLimitOccupancy = 0f;
            float maximumSaddleRelativeRotation = 0f;
            float maximumSaddleEngineForce = 0f;
            float maximumSaddleEngineTorque = 0f;
            float maximumLimitProximity = 0f;
            int apCorrectionSamples = 0;

            for (int tick = 0; tick < QualificationTicks; tick++)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                Assert.That(controller.ObservationCollector.HasLastSnapshot, Is.True,
                    $"No post-physics observation was recorded at standing tick {tick}.");

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                finite &= IsFinite(snapshot, rig, controller, isLoaded, physicalBarbell);
                bilateralSupportEverySample &= snapshot.Support.HasSupport &&
                    snapshot.LeftFoot.IsInContact && snapshot.RightFoot.IsInContact;
                attachedBarEverySample &= isLoaded
                    ? controller.Saddle != null && controller.Saddle.IsAttached && !controller.Saddle.IsBroken
                    : controller.Saddle == null && !physicalBarbell.Body.gameObject.activeInHierarchy;
                hasStartWindow |= controller.AttemptLifecycle.HasStartWindow;
                startWindowSampleCount = Mathf.Max(
                    startWindowSampleCount,
                    controller.AttemptLifecycle.StartWindowSampleCount);
                squatCommandIssued |= isLoaded && controller.AttemptOrchestrator.HasSquatCommand;

                minimumPelvisY = Mathf.Min(minimumPelvisY, snapshot.PelvisPositionWorldMeters.Y);
                if (isLoaded)
                    minimumBarY = Mathf.Min(minimumBarY, snapshot.Bar.PositionWorldMeters.Y);
                maximumTrunkPitch = Mathf.Max(maximumTrunkPitch, Mathf.Abs(snapshot.TrunkWorldPitchRadians));
                Vector3Value comVelocity = snapshot.Support.SystemComVelocityWorldMetersPerSecond;
                maximumComSpeed = Mathf.Max(maximumComSpeed, Mathf.Sqrt(comVelocity.X * comVelocity.X + comVelocity.Z * comVelocity.Z));
                minimumComSupportMargin = Mathf.Min(
                    minimumComSupportMargin,
                    snapshot.Support.ComToSupportApFrontMarginM,
                    snapshot.Support.ComToSupportApRearMarginM,
                    snapshot.Support.ComToSupportMlLeftMarginM,
                    snapshot.Support.ComToSupportMlRightMarginM);
                MeasureConstraintHealth(rig, out float anchorSeparation, out float limitProximity, out bool diagnosticsAvailable);
                finite &= diagnosticsAvailable;
                float expectedControlErrorAp =
                    controller.Adapter.SystemCom.z - controller.Adapter.SupportCenter.z - controller.Adapter.ReferenceComOffsetAp;
                float expectedControlErrorMl =
                    controller.Adapter.SystemCom.x - controller.Adapter.SupportCenter.x - controller.Adapter.ReferenceComOffsetMl;
                referenceErrorConsistent &=
                    Mathf.Abs(expectedControlErrorAp - controller.Adapter.BalanceCorrectionV2.ErrorApM) <= 1e-5f &&
                    Mathf.Abs(expectedControlErrorMl - controller.Adapter.BalanceCorrectionV2.ErrorMlM) <= 1e-5f;
                maximumJointAnchorSeparation = Mathf.Max(maximumJointAnchorSeparation, anchorSeparation);
                maximumLimitProximity = Mathf.Max(maximumLimitProximity, limitProximity);
                if (controller.Saddle != null)
                {
                    maximumSaddleSeparation = Mathf.Max(maximumSaddleSeparation, controller.Saddle.SaddleSeparationMeters);
                    float saddleOccupancy = controller.Saddle.CurrentLinearLimitOccupancy;
                    float saddleRotation = controller.Saddle.RelativeRotationDegrees;
                    Vector3 saddleForce = controller.Saddle.CurrentForceEngine;
                    Vector3 saddleTorque = controller.Saddle.CurrentTorqueEngine;
                    finite &= float.IsFinite(saddleOccupancy) && float.IsFinite(saddleRotation) &&
                        IsFinite(saddleForce) && IsFinite(saddleTorque);
                    maximumSaddleLimitOccupancy = Mathf.Max(maximumSaddleLimitOccupancy, saddleOccupancy);
                    maximumSaddleRelativeRotation = Mathf.Max(maximumSaddleRelativeRotation, saddleRotation);
                    maximumSaddleEngineForce = Mathf.Max(maximumSaddleEngineForce, saddleForce.magnitude);
                    maximumSaddleEngineTorque = Mathf.Max(maximumSaddleEngineTorque, saddleTorque.magnitude);
                }

                float ankleApCorrection = controller.Adapter.BalanceCorrectionV2.AnkleApRad;
                if (Mathf.Abs(ankleApCorrection) >= MinimumMappingCorrectionRad &&
                    isLoaded &&
                    controller.Adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition ankleComposition))
                {
                    float logicalBalanceTwist = PoweredJointController.SignedTwistRadians(
                        ankleComposition.BalanceOffset, Vector3.right);
                    Quaternion logicalTargetDelta = ankleComposition.Final * Quaternion.Inverse(ankleComposition.Nominal);
                    float logicalTargetTwist = PoweredJointController.SignedTwistRadians(
                        logicalTargetDelta, Vector3.right);
                    JointCommand command = rig.PoweredController.GetJoint("left_foot").RequestedCommand;
                    Quaternion logicalCommandDelta = command.TargetRelativeRotation * Quaternion.Inverse(ankleComposition.Nominal);
                    float logicalCommandTwist = PoweredJointController.SignedTwistRadians(
                        logicalCommandDelta, Vector3.right);
                    apCorrectionSamples++;
                    apCorrectionMappingConsistent &=
                        logicalBalanceTwist * ankleApCorrection > 0f &&
                        logicalTargetTwist * ankleApCorrection > 0f &&
                        logicalCommandTwist * ankleApCorrection > 0f;
                }

                string ruleOutcome = squatCommandIssued ? "SQUAT_COMMAND_ISSUED" : "START_WINDOW_PENDING";
                trace.Append(
                    loadKg,
                    controller,
                    snapshot,
                    maximumLimitProximity,
                    anchorSeparation,
                    ruleOutcome,
                    "NOT_STARTED");
            }

            trace.Save(tracePath);
            bool physicalStandingQualified = finite && attachedBarEverySample && bilateralSupportEverySample &&
                maximumJointAnchorSeparation <= MaximumConstraintSeparationM &&
                minimumPelvisY >= MinimumPelvisHeightM &&
                maximumTrunkPitch <= MaximumTrunkPitchRad &&
                maximumComSpeed <= MaximumComSpeedMps &&
                minimumComSupportMargin >= MinimumComSupportMarginM &&
                (!isLoaded ||
                    (controller.Saddle != null &&
                     controller.Saddle.InitialAnchorErrorMeters <= SquatBarSaddle.InitialAnchorToleranceM &&
                     maximumSaddleSeparation <= MaximumSaddleSeparationM &&
                     maximumSaddleLimitOccupancy < MaximumSaddleLimitOccupancy &&
                     maximumSaddleRelativeRotation < MaximumSaddleRelativeRotationDegrees &&
                     minimumBarY >= MinimumBarHeightM));
            bool startCommandReady = hasStartWindow &&
                startWindowSampleCount >= controller.AttemptOrchestrator.RequiredStartSamples &&
                squatCommandIssued;
            float saddleInitialError = controller.Saddle != null
                ? controller.Saddle.InitialAnchorErrorMeters
                : float.NaN;
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V2_STANDING load={0:F1}kg PHYSICAL_STANDING_QUALIFIED={1} START_COMMAND_READY={2} HasStartWindow={3} StartWindowSampleCount={4}/{5} HasSquatCommand={6} contacts={7} pelvis={8:F3}m trunk={9:F3}rad comSpeed={10:F3}m/s comSupport={11:F3}m anchor={12:F4}m saddleError0={13:F5}m saddleMax={14:F4}m saddleOcc={15:F3} saddleRot={16:F2}deg saddleForce={17:F1}N saddleTorque={18:F1}Nm limit={19:F3} intrinsicCapacity={20:F5} referenceErrorConsistent={21} apCorrectionSamples={22} apCorrectionMapping={23} trace={24}",
                loadKg, physicalStandingQualified, startCommandReady, hasStartWindow,
                startWindowSampleCount, controller.AttemptOrchestrator.RequiredStartSamples,
                squatCommandIssued, bilateralSupportEverySample,
                minimumPelvisY, maximumTrunkPitch, maximumComSpeed, minimumComSupportMargin,
                maximumJointAnchorSeparation, saddleInitialError,
                maximumSaddleSeparation, maximumSaddleLimitOccupancy, maximumSaddleRelativeRotation,
                maximumSaddleEngineForce, maximumSaddleEngineTorque, maximumLimitProximity,
                trace.MinimumIntrinsicCapacityFraction, referenceErrorConsistent,
                apCorrectionSamples, apCorrectionMappingConsistent, tracePath));

            Assert.That(physicalStandingQualified, Is.True,
                "Physical standing criteria failed; start-window and Squat-command diagnostics do not qualify physical standing.");
            if (!isLoaded)
            {
                Assert.That(controller.Saddle, Is.Null);
                Assert.That(physicalBarbell.Body.gameObject.activeInHierarchy, Is.False);
            }
            Assert.That(controller.Adapter.Sq, Is.EqualTo(0f).Within(0.001f), "No player Yield input was supplied during standing qualification.");
        }

        [UnityTest]
        public IEnumerator GAM13_V23D_TARGET_SPACE_REPRESENTATION_PRESERVES_AP_SIGN()
        {
            AsyncOperation loadScene = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(loadScene, Is.Not.Null, "The GAM-13 qualification scene is missing.");
            while (!loadScene.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            SquatPhysicalPrototypeController controller =
                UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);
            controller.enabled = false;
            bootstrap.enabled = false;
            controller.SetLoad(25f);
            Assert.That(controller.Saddle, Is.Not.Null);
            Assert.That(bootstrap.Runtime.CurrentTime.Tick, Is.EqualTo(0ul));

            bool positiveCorrectionPreserved = false;
            bool negativeCorrectionPreserved = false;
            bool unityRepresentationsMatch = true;
            bool issuedCommandMatchesComposition = true;
            bool appliedTargetMatchesCommand = true;
            const float firstGateBApRad = 0.000330548617f;
            const float firstGateBAnkleMlRad = -3.49903672e-7f;
            SquatComStabilizerV2Calibration calibration = controller.Adapter.StabilizerCalibration;
            float[] apObservationShiftsM = { 0.004f, -0.004f, firstGateBApRad / calibration.KpAp };
            float[] mlObservationShiftsM = { 0f, 0f, -firstGateBAnkleMlRad / calibration.KpMl };
            for (int index = 0; index < apObservationShiftsM.Length; index++)
            {
                float shiftM = apObservationShiftsM[index];
                float shiftMlM = mlObservationShiftsM[index];
                ulong tick = (ulong)(index + 1);
                controller.Adapter.Reset();
                controller.Adapter.SetSaddle(controller.Saddle);
                controller.Adapter.CaptureStandingComReference();
                rig.PoweredController.Reset(PoweredAthleteMode.Controlled);

                PhysicalObservation observation = CreateShiftedApObservation(rig, controller.Saddle, shiftM, shiftMlM, tick);
                var time = new SimulationTime(tick, SimulationConstants.TimeForTick(tick));
                controller.Adapter.PrepareCommands(observation, time, default, rig.PoweredController);
                SquatBalanceCorrectionV2 correction = controller.Adapter.BalanceCorrectionV2;
                Assert.That(controller.Adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition composition), Is.True);

                float balanceTwist = PoweredJointController.SignedTwistRadians(composition.BalanceOffset, Vector3.right);
                Quaternion logicalDelta = composition.Final * Quaternion.Inverse(composition.Nominal);
                float logicalDeltaTwist = PoweredJointController.SignedTwistRadians(logicalDelta, Vector3.right);
                PoweredJointController.PoweredJointRuntime ankle = rig.PoweredController.GetJoint("left_foot");
                JointCommand command = ankle.RequestedCommand;
                Quaternion commandDelta = command.TargetRelativeRotation * Quaternion.Inverse(composition.Nominal);
                float commandDeltaTwist = PoweredJointController.SignedTwistRadians(commandDelta, Vector3.right);
                issuedCommandMatchesComposition &=
                    Quaternion.Angle(command.TargetRelativeRotation, composition.Final) < 0.0001f;

                rig.PoweredController.SnapAppliedTargets();
                rig.PoweredController.Step(
                    new SimulationTime(tick + 1, SimulationConstants.TimeForTick(tick + 1)),
                    default);
                Quaternion appliedTarget = ankle.AppliedTarget;
                Quaternion appliedDelta = appliedTarget * Quaternion.Inverse(composition.Nominal);
                float appliedDeltaTwist = PoweredJointController.SignedTwistRadians(appliedDelta, Vector3.right);
                appliedTargetMatchesCommand &=
                    Quaternion.Angle(appliedTarget, command.TargetRelativeRotation) < 0.0001f;
                Quaternion unityTarget = ankle.Joint.targetRotation;
                Quaternion convertedTarget = PoweredJointController.ToUnityTargetRotation(appliedTarget);
                float unityConversionErrorDegrees = Quaternion.Angle(unityTarget, convertedTarget);

                Debug.Log(string.Format(CultureInfo.InvariantCulture,
                    "GAM13_V23D_TARGET_SPACE sign={0} shiftApM={1:R} shiftMlM={2:R} tick={3} appliedApRad={4:R} ankleApRad={5:R} ankleMlRad={6:R} nominal={7} balanceOffset={8} balanceTwistRad={9:R} final={10} logicalDelta={11} logicalDeltaTwistRad={12:R} issuedCommand={13} commandDelta={14} commandDeltaTwistRad={15:R} requestedCommand={16} appliedTarget={17} appliedDelta={18} appliedDeltaTwistRad={19:R} unityTargetRotation={20} unityTargetTwistRad={21:R} convertedTarget={22} conversionErrorDeg={23:R}",
                    index == 2 ? "gateB-first-failure-reproduction" : shiftM > 0f ? "positive" : "negative", shiftM, shiftMlM, tick,
                    correction.AppliedApRad, correction.AnkleApRad, correction.AnkleMlRad,
                    FormatQuaternion(composition.Nominal), FormatQuaternion(composition.BalanceOffset), balanceTwist,
                    FormatQuaternion(composition.Final), FormatQuaternion(logicalDelta), logicalDeltaTwist,
                    FormatQuaternion(command.TargetRelativeRotation), FormatQuaternion(commandDelta), commandDeltaTwist,
                    FormatQuaternion(ankle.RequestedCommand.TargetRelativeRotation), FormatQuaternion(appliedTarget),
                    FormatQuaternion(appliedDelta), appliedDeltaTwist, FormatQuaternion(unityTarget),
                    PoweredJointController.SignedTwistRadians(unityTarget, Vector3.right),
                    FormatQuaternion(convertedTarget), unityConversionErrorDegrees));

                bool correctionHasExpectedSign = shiftM > 0f
                    ? correction.AppliedApRad > 0f && correction.AnkleApRad > 0f
                    : correction.AppliedApRad < 0f && correction.AnkleApRad < 0f;
                bool preservesLogicalSign = correctionHasExpectedSign &&
                    balanceTwist * correction.AnkleApRad > 0f &&
                    logicalDeltaTwist * correction.AnkleApRad > 0f &&
                    commandDeltaTwist * correction.AnkleApRad > 0f &&
                    appliedDeltaTwist * correction.AnkleApRad > 0f;
                if (Mathf.Abs(correction.AnkleApRad) >= 0.001f)
                {
                    if (shiftM > 0f)
                        positiveCorrectionPreserved = preservesLogicalSign;
                    else
                        negativeCorrectionPreserved = preservesLogicalSign;
                }
                unityRepresentationsMatch &= unityConversionErrorDegrees < 0.0001f;
            }

            Assert.That(positiveCorrectionPreserved, Is.True, "Positive AP correction did not preserve its logical ankle target sign.");
            Assert.That(negativeCorrectionPreserved, Is.True, "Negative AP correction did not preserve its logical ankle target sign.");
            Assert.That(issuedCommandMatchesComposition, Is.True, "The issued JointCommand did not preserve the composed logical target.");
            Assert.That(appliedTargetMatchesCommand, Is.True, "PoweredJointController.AppliedTarget did not preserve the issued logical target.");
            Assert.That(unityRepresentationsMatch, Is.True, "Unity targetRotation did not equal the documented inverse logical target representation.");
        }

        private static PhysicalObservation CreateShiftedApObservation(
            PhysicalAthleteRig rig,
            SquatBarSaddle saddle,
            float shiftApM,
            float shiftMlM,
            ulong tick)
        {
            bool includeBarbell = saddle != null && saddle.Barbell != null && saddle.Barbell.Body != null &&
                saddle.Barbell.Body.gameObject.activeInHierarchy;
            var bodies = new PhysicalBodyObservation[rig.Segments.Count + (includeBarbell ? 1 : 0)];
            int index = 0;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                Rigidbody body = segment.Body;
                Vector3 position = body.worldCenterOfMass + new Vector3(shiftMlM, 0f, shiftApM);
                Vector3 velocity = Vector3.zero;
                Quaternion rotation = body.rotation;
                bodies[index++] = new PhysicalBodyObservation(
                    segment.Recipe.Id,
                    body.mass,
                    new Vector3Value(position.x, position.y, position.z),
                    new QuaternionValue(rotation.x, rotation.y, rotation.z, rotation.w),
                    new Vector3Value(velocity.x, velocity.y, velocity.z),
                    Vector3Value.Zero);
            }

            if (includeBarbell)
            {
                Rigidbody body = saddle.Barbell.Body;
                Vector3 position = body.worldCenterOfMass + new Vector3(shiftMlM, 0f, shiftApM);
                Quaternion rotation = body.rotation;
                bodies[index] = new PhysicalBodyObservation(
                    "barbell",
                    body.mass,
                    new Vector3Value(position.x, position.y, position.z),
                    new QuaternionValue(rotation.x, rotation.y, rotation.z, rotation.w),
                    Vector3Value.Zero,
                    Vector3Value.Zero);
            }

            var time = new SimulationTime(tick, SimulationConstants.TimeForTick(tick));
            return new PhysicalObservation(time, bodies[0], true, bodies);
        }

        private static string FormatQuaternion(Quaternion value) => string.Format(
            CultureInfo.InvariantCulture,
            "({0:R},{1:R},{2:R},{3:R})",
            value.x, value.y, value.z, value.w);

        private static float ReadLoadKg()
        {
            string value = Environment.GetEnvironmentVariable("GAM13_V2_LOAD_KG") ?? "25";
            float load = float.Parse(value, CultureInfo.InvariantCulture);
            bool supported = load == 0f || load == 25f || load == 60f || load == 140f || load == 170f || load == 300f;
            Assert.That(supported, Is.True, $"Unsupported standing probe load: {load:R} kg.");
            return load;
        }

        private static void TickFeet(SquatPhysicalPrototypeController controller, float dt)
        {
            controller.LeftFootContact?.PhysicsTickUpdate(dt);
            controller.RightFootContact?.PhysicsTickUpdate(dt);
        }

        internal static bool IsFinite(
            SquatObservationSnapshot snapshot,
            PhysicalAthleteRig rig,
            SquatPhysicalPrototypeController controller,
            bool isLoaded,
            PhysicalBarbell physicalBarbell)
        {
            if ((isLoaded && (!SquatTelemetryValue.IsFinite(snapshot.Bar.PositionWorldMeters) ||
                 !SquatTelemetryValue.IsFinite(snapshot.Bar.LinearVelocityWorldMetersPerSecond) ||
                 !SquatTelemetryValue.IsFinite(snapshot.Bar.LoadKilograms) ||
                 snapshot.Bar.Availability != SquatTelemetryAvailability.AVAILABLE)) ||
                (!isLoaded && (snapshot.Bar.Availability != SquatTelemetryAvailability.NOT_AVAILABLE ||
                 snapshot.Bar.SaddleAvailability != SquatTelemetryAvailability.NOT_AVAILABLE ||
                 controller.Saddle != null || physicalBarbell == null ||
                 physicalBarbell.Body.gameObject.activeInHierarchy)) ||
                !SquatTelemetryValue.IsFinite(snapshot.Support.SystemComWorldMeters) ||
                !SquatTelemetryValue.IsFinite(snapshot.Support.SystemComVelocityWorldMetersPerSecond) ||
                !SquatTelemetryValue.IsFinite(snapshot.Support.SupportCenterWorldMeters) ||
                !SquatTelemetryValue.IsFinite(snapshot.Depth.WorstSideDepthM) ||
                !SquatTelemetryValue.IsFinite(snapshot.TrunkWorldPitchRadians) ||
                snapshot.Support.SupportAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Depth.Availability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.DriveAvailability != SquatTelemetryAvailability.AVAILABLE ||
                !SquatTelemetryValue.IsFinite(snapshot.MaximumModeledDemand) ||
                snapshot.Joints.LeftAnkle.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightAnkle.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.LeftKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightKnee.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.LeftHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.RightHip.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.Abdomen.JointAvailability != SquatTelemetryAvailability.AVAILABLE ||
                snapshot.Joints.Thorax.JointAvailability != SquatTelemetryAvailability.AVAILABLE)
                return false;

            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                if (segment.Body == null || !IsFinite(segment.Body.position) ||
                    !IsFinite(segment.Body.linearVelocity) || !IsFinite(segment.Body.angularVelocity))
                    return false;
            }

            Rigidbody barBody = physicalBarbell != null ? physicalBarbell.Body : controller.Saddle?.Barbell.Body;
            return barBody != null && IsFinite(barBody.position) &&
                IsFinite(barBody.linearVelocity) && IsFinite(barBody.angularVelocity);
        }

        internal static void MeasureConstraintHealth(
            PhysicalAthleteRig rig,
            out float maximumAnchorSeparation,
            out float maximumLimitProximity,
            out bool diagnosticsAvailable)
        {
            maximumAnchorSeparation = 0f;
            maximumLimitProximity = 0f;
            diagnosticsAvailable = true;
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (joint.Joint == null || joint.Joint.connectedBody == null)
                {
                    diagnosticsAvailable = false;
                    continue;
                }
                Vector3 childAnchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 parentAnchor = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
                maximumAnchorSeparation = Mathf.Max(maximumAnchorSeparation, Vector3.Distance(childAnchor, parentAnchor));
                if (!joint.Profile.HasValue)
                    continue;
                if (!joint.HasPostPhysicsDiagnostic)
                {
                    diagnosticsAvailable = false;
                    continue;
                }
                maximumLimitProximity = Mathf.Max(maximumLimitProximity, joint.PostPhysicsDiagnostic.LimitProximity);
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
