using System;
using System.Collections;
using System.Globalization;
using System.IO;
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
    public sealed class GAM13V2StandingQualificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int WarmupTicks = 100;
        private const int QualificationTicks = 500;
        private const float MaximumTrunkPitchRad = 0.70f;
        private const float MinimumPelvisHeightM = 0.90f;
        private const float MinimumBarHeightM = 1.00f;
        private const float MaximumComSpeedMps = 0.25f;
        private const float MinimumComSupportMarginM = -0.02f;
        private const float MaximumConstraintSeparationM = 0.05f;

        [UnityTest]
        public IEnumerator GAM13_V2_FRESH_PROCESS_STANDING_QUALIFICATION()
        {
            float loadKg = ReadLoadKg();
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
            Assert.That(controller.Saddle, Is.Not.Null);
            Assert.That(controller.Saddle.IsAttached, Is.True);
            Assert.That(controller.Saddle.IsBroken, Is.False);
            Assert.That(controller.Saddle.Barbell.LoadedMassKg, Is.EqualTo(loadKg).Within(0.001f));

            FoundationRuntime runtime = bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            var trace = new GAM13V2QualificationTrace();
            for (int tick = 0; tick < WarmupTicks; tick++)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
            }

            controller.BeginAttempt();

            bool finite = true;
            bool bilateralSupportEverySample = true;
            bool attachedBarEverySample = true;
            bool hasStartWindow = false;
            bool squatCommandIssued = false;
            float minimumPelvisY = float.PositiveInfinity;
            float minimumBarY = float.PositiveInfinity;
            float maximumTrunkPitch = 0f;
            float maximumComSpeed = 0f;
            float minimumComSupportMargin = float.PositiveInfinity;
            float maximumJointAnchorSeparation = 0f;
            float maximumSaddleSeparation = 0f;
            float maximumLimitProximity = 0f;

            for (int tick = 0; tick < QualificationTicks; tick++)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                if (!controller.ObservationCollector.HasLastSnapshot)
                {
                    finite = false;
                    continue;
                }

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                finite &= IsFinite(snapshot, rig, controller);
                bilateralSupportEverySample &= snapshot.Support.HasSupport &&
                    snapshot.LeftFoot.IsInContact && snapshot.RightFoot.IsInContact;
                attachedBarEverySample &= controller.Saddle != null && controller.Saddle.IsAttached && !controller.Saddle.IsBroken;
                hasStartWindow |= controller.AttemptLifecycle.HasStartWindow &&
                    controller.AttemptLifecycle.StartWindowSampleCount >= controller.AttemptOrchestrator.RequiredStartSamples;
                squatCommandIssued |= controller.AttemptOrchestrator.HasSquatCommand;

                minimumPelvisY = Mathf.Min(minimumPelvisY, snapshot.PelvisPositionWorldMeters.Y);
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
                maximumJointAnchorSeparation = Mathf.Max(maximumJointAnchorSeparation, anchorSeparation);
                maximumLimitProximity = Mathf.Max(maximumLimitProximity, limitProximity);
                maximumSaddleSeparation = Mathf.Max(maximumSaddleSeparation, controller.Saddle.SaddleSeparationMeters);

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
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V2_STANDING load={0:F1}kg command={1} window={2} contacts={3} pelvis={4:F3}m trunk={5:F3}rad comSpeed={6:F3}m/s comSupport={7:F3}m anchor={8:F4}m saddle={9:F4}m limit={10:F3} trace={11}",
                loadKg, squatCommandIssued, hasStartWindow, bilateralSupportEverySample,
                minimumPelvisY, maximumTrunkPitch, maximumComSpeed, minimumComSupportMargin,
                maximumJointAnchorSeparation, maximumSaddleSeparation, maximumLimitProximity, tracePath));

            Assert.That(finite, Is.True, "The simulation, body state, or authoritative observations became non-finite.");
            Assert.That(attachedBarEverySample, Is.True, "The physical bar detached or the saddle broke during qualification.");
            Assert.That(controller.Saddle.SpawnAlignmentWithinTolerance, Is.True, "The bar saddle did not begin in a valid alignment.");
            Assert.That(bilateralSupportEverySample, Is.True, "Bilateral plantar support was lost during the standing qualification window.");
            Assert.That(maximumJointAnchorSeparation, Is.LessThanOrEqualTo(MaximumConstraintSeparationM), "A ConfigurableJoint separated pathologically.");
            Assert.That(maximumSaddleSeparation, Is.LessThanOrEqualTo(MaximumConstraintSeparationM), "The bar saddle separated pathologically.");
            Assert.That(minimumPelvisY, Is.GreaterThanOrEqualTo(MinimumPelvisHeightM), "The setup is not upright.");
            Assert.That(minimumBarY, Is.GreaterThanOrEqualTo(MinimumBarHeightM), "The bar is not at a credible supported standing height.");
            Assert.That(maximumTrunkPitch, Is.LessThanOrEqualTo(MaximumTrunkPitchRad), "The trunk is not upright.");
            Assert.That(maximumComSpeed, Is.LessThanOrEqualTo(MaximumComSpeedMps), "COM is moving too quickly for a supported setup.");
            Assert.That(minimumComSupportMargin, Is.GreaterThanOrEqualTo(MinimumComSupportMarginM), "COM left the plantar support region.");
            Assert.That(hasStartWindow, Is.True, "The start window did not qualify.");
            Assert.That(squatCommandIssued, Is.True, "The Squat command could not be issued after setup qualification.");
            Assert.That(controller.Adapter.Sq, Is.EqualTo(0f).Within(0.001f), "No player Yield input was supplied during standing qualification.");
        }

        private static float ReadLoadKg()
        {
            string value = Environment.GetEnvironmentVariable("GAM13_V2_LOAD_KG") ?? "25";
            float load = float.Parse(value, CultureInfo.InvariantCulture);
            bool supported = load == 25f || load == 60f || load == 140f || load == 170f || load == 300f;
            Assert.That(supported, Is.True, $"Unsupported standing probe load: {load:R} kg.");
            return load;
        }

        private static void TickFeet(SquatPhysicalPrototypeController controller, float dt)
        {
            controller.LeftFootContact?.PhysicsTickUpdate(dt);
            controller.RightFootContact?.PhysicsTickUpdate(dt);
        }

        private static bool IsFinite(SquatObservationSnapshot snapshot, PhysicalAthleteRig rig, SquatPhysicalPrototypeController controller)
        {
            if (!SquatTelemetryValue.IsFinite(snapshot.Bar.PositionWorldMeters) ||
                !SquatTelemetryValue.IsFinite(snapshot.Bar.LinearVelocityWorldMetersPerSecond) ||
                !SquatTelemetryValue.IsFinite(snapshot.Bar.LoadKilograms) ||
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

            return controller.Saddle != null && controller.Saddle.Barbell != null &&
                controller.Saddle.Barbell.Body != null && IsFinite(controller.Saddle.Barbell.Body.position) &&
                IsFinite(controller.Saddle.Barbell.Body.linearVelocity) && IsFinite(controller.Saddle.Barbell.Body.angularVelocity);
        }

        private static void MeasureConstraintHealth(
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
                if (!joint.Profile.HasValue)
                    continue;
                if (!joint.HasPostPhysicsDiagnostic || joint.Joint == null || joint.Joint.connectedBody == null)
                {
                    diagnosticsAvailable = false;
                    continue;
                }
                Vector3 childAnchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 parentAnchor = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
                maximumAnchorSeparation = Mathf.Max(maximumAnchorSeparation, Vector3.Distance(childAnchor, parentAnchor));
                maximumLimitProximity = Mathf.Max(maximumLimitProximity, joint.PostPhysicsDiagnostic.LimitProximity);
            }
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
    }
}
