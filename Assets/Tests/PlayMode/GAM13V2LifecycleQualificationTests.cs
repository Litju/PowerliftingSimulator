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
    public sealed class GAM13V2LifecycleQualificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int WarmupTicks = 100;
        private const int MaximumLifecycleTicks = 1800;

        [UnityTest]
        public IEnumerator GAM13_V2_ONE_LOAD_INTENT_DRIVEN_LIFECYCLE()
        {
            float loadKg = ReadLoadKg();
            string tracePath = Environment.GetEnvironmentVariable("GAM13_V2_TRACE_PATH") ??
                Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-13", "v2-lifecycle",
                    "load-" + loadKg.ToString("000", CultureInfo.InvariantCulture) + "kg.csv");

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
            controller.SetLoad(loadKg);

            FoundationRuntime runtime = bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int tick = 0; tick < WarmupTicks; tick++)
            {
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
            }

            controller.BeginAttempt();
            var trace = new GAM13V2QualificationTrace();
            bool hasSquatCommand = false;
            bool yieldAdvancedPhase = false;
            bool driveReversedPhase = false;
            bool finite = true;
            bool barMoved = false;
            bool authoredSticking = false;
            bool reachedLegalSurfaceDepth = false;
            float maximumBarSpeed = 0f;
            float previousPhase = controller.Adapter.Sq;
            for (int tick = 0; tick < MaximumLifecycleTicks && controller.AttemptRecord == null; tick++)
            {
                if (controller.AttemptOrchestrator.HasSquatCommand)
                {
                    hasSquatCommand = true;
                    bool yield = controller.Adapter.Sq < 0.999f;
                    double sampleTime = runtime.CurrentTime.SimulationTimeSeconds +
                        0.25d * SimulationConstants.FixedDeltaTimeSeconds;
                    runtime.InputBuffer.SetContinuous(IntentAction.Yield, yield ? 1f : 0f, sampleTime);
                    runtime.InputBuffer.SetContinuous(IntentAction.Drive, yield ? 0f : 1f, sampleTime);
                }

                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(controller, dt);
                if (!controller.ObservationCollector.HasLastSnapshot)
                    continue;

                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                finite &= IsFinite(snapshot);
                reachedLegalSurfaceDepth |= snapshot.Depth.BilateralGameJudgmentQualified;
                maximumBarSpeed = Mathf.Max(maximumBarSpeed, snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length);
                barMoved |= snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length > 0.01f;
                float phase = controller.Adapter.Sq;
                if (hasSquatCommand && phase > previousPhase + 1e-4f)
                    yieldAdvancedPhase = true;
                if (yieldAdvancedPhase && phase < previousPhase - 1e-4f)
                    driveReversedPhase = true;
                previousPhase = phase;
                authoredSticking |= snapshot.State == SquatState.STICKING;

                SquatAttemptRecord record = controller.AttemptRecord;
                string ruleOutcome = record == null ? "NOT_FINALIZED" : record.RuleOutcome.ToString();
                string physicalOutcome = record == null ? "IN_PROGRESS" : record.PhysicalFailureOutcome.ToString();
                MeasureConstraintHealth(controller.AthleteRig, out float anchorSeparation, out float limitProximity);
                trace.Append(
                    loadKg,
                    controller,
                    snapshot,
                    limitProximity,
                    anchorSeparation,
                    ruleOutcome,
                    physicalOutcome);

                if (tick % 50 == 49)
                    yield return null;
            }

            SquatAttemptRecord finalRecord = controller.AttemptRecord;
            if (finalRecord != null)
                trace.SetFinalOutcomes(finalRecord.RuleOutcome.ToString(), finalRecord.PhysicalFailureOutcome.ToString());
            trace.Save(tracePath);

            Assert.That(finite, Is.True, "The lifecycle trace contains a non-finite physical observation.");
            Assert.That(hasSquatCommand, Is.True, "The qualified start did not produce an explicit Squat command.");
            Assert.That(yieldAdvancedPhase, Is.True, "Player Yield did not advance phase toward the canonical bottom.");
            Assert.That(driveReversedPhase, Is.True, "Player Drive did not reverse phase toward standing.");
            Assert.That(barMoved, Is.True, "The physical bar did not move under gravity and finite joint authority.");
            Assert.That(authoredSticking, Is.False, "STICKING must remain an observation, not a V2 motor state.");
            Assert.That(finalRecord, Is.Not.Null, "The lifecycle did not finalize a physical/rule outcome within its bounded attempt window.");
            Assert.That(finalRecord.TraceSampleCount, Is.GreaterThan(0));

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "GAM13_V2_LIFECYCLE load={0:F1}kg terminal={1} rule={2} physical={3} legalDepthObserved={4} maxBarSpeed={5:F3}m/s trace={6}",
                loadKg, finalRecord.TerminalReason, finalRecord.RuleOutcome, finalRecord.PhysicalFailureOutcome,
                reachedLegalSurfaceDepth, maximumBarSpeed, tracePath));
        }

        private static float ReadLoadKg()
        {
            string value = Environment.GetEnvironmentVariable("GAM13_V2_LOAD_KG") ?? "25";
            float load = float.Parse(value, CultureInfo.InvariantCulture);
            bool supported = load == 25f || load == 60f || load == 140f || load == 170f || load == 300f;
            Assert.That(supported, Is.True, $"Unsupported lifecycle probe load: {load:R} kg.");
            return load;
        }

        private static void TickFeet(SquatPhysicalPrototypeController controller, float dt)
        {
            controller.LeftFootContact?.PhysicsTickUpdate(dt);
            controller.RightFootContact?.PhysicsTickUpdate(dt);
        }

        private static bool IsFinite(SquatObservationSnapshot snapshot) =>
            SquatTelemetryValue.IsFinite(snapshot.Bar.PositionWorldMeters) &&
            SquatTelemetryValue.IsFinite(snapshot.Bar.LinearVelocityWorldMetersPerSecond) &&
            SquatTelemetryValue.IsFinite(snapshot.Bar.LoadKilograms) &&
            SquatTelemetryValue.IsFinite(snapshot.Depth.WorstSideDepthM) &&
            SquatTelemetryValue.IsFinite(snapshot.Support.SystemComWorldMeters) &&
            SquatTelemetryValue.IsFinite(snapshot.Support.SystemComVelocityWorldMetersPerSecond) &&
            SquatTelemetryValue.IsFinite(snapshot.Support.SupportCenterWorldMeters) &&
            SquatTelemetryValue.IsFinite(snapshot.MaximumModeledDemand);

        private static void MeasureConstraintHealth(
            PhysicalAthleteRig rig,
            out float maximumAnchorSeparation,
            out float maximumLimitProximity)
        {
            maximumAnchorSeparation = 0f;
            maximumLimitProximity = 0f;
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !joint.HasPostPhysicsDiagnostic || joint.Joint == null || joint.Joint.connectedBody == null)
                    continue;
                Vector3 childAnchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
                Vector3 parentAnchor = joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor);
                maximumAnchorSeparation = Mathf.Max(maximumAnchorSeparation, Vector3.Distance(childAnchor, parentAnchor));
                maximumLimitProximity = Mathf.Max(maximumLimitProximity, joint.PostPhysicsDiagnostic.LimitProximity);
            }
        }
    }
}
