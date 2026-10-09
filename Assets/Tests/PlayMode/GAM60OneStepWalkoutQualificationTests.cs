using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    public sealed class GAM60OneStepWalkoutQualificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int FreshRuns = 3;
        private const int StandingTimeoutTicks = 1200;
        private const int WalkoutTimeoutTicks = 2600;
        private const int StartPredicateTimeoutTicks = 600;
        private static readonly string[] AffectedJoints =
        {
            "left_foot", "right_foot", "left_shank", "right_shank",
            "left_thigh", "right_thigh", "abdomen", "thorax"
        };

        [UnityTest]
        public IEnumerator GAM60_25KG_ONE_BACKWARD_STEP_QUALIFIES_THREE_FRESH_SCENES()
        {
            var evidence = new StringBuilder();
            evidence.AppendLine("run,tick,state,com_x,com_y,com_z,com_vx,com_vy,com_vz,capture_margin,left_impulse,right_impulse,left_contacts,right_contacts,right_x,right_y,right_z,clearance_m,posterior_m,bar_speed,bar_angular_speed,affected_command_delta_deg");
            var jointBaseline = new StringBuilder();
            jointBaseline.AppendLine("run,joint_id,requested_A_deg,applied_B_deg,actual_deg,error_x_rad,error_y_rad,error_z_rad,target_rate_rad_s,actual_rate_rad_s,solver_torque_x_nm,solver_torque_y_nm,solver_torque_z_nm,modeled_demand_nm,maximum_force_nm,limit_proximity");
            var failures = new List<string>();

            for (int run = 1; run <= FreshRuns; run++)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
                Assert.That(load, Is.Not.Null, "The GAM-60 qualification scene is missing.");
                while (!load.isDone)
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

                bootstrap.enabled = false;
                controller.enabled = false;
                controller.SetLoad(25f);
                FoundationRuntime runtime = bootstrap.Runtime;
                float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                int stableStandingTicks = 0;
                for (int tick = 0; tick < StandingTimeoutTicks && stableStandingTicks < 50; tick++)
                {
                    Advance(runtime, controller);
                    SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                    float threshold = LoadedImpulseThreshold(controller);
                    bool standing = IsLoaded(controller.LeftFootContact, threshold) &&
                        IsLoaded(controller.RightFootContact, threshold) &&
                        snapshot.Bar.LinearVelocityWorldMetersPerSecond.Length <= 0.02f &&
                        snapshot.Bar.AngularVelocityBarRadiansPerSecond.Length <= 0.20f &&
                        controller.Adapter.Balance.SystemComVelocity.magnitude <= 0.10f;
                    stableStandingTicks = standing ? stableStandingTicks + 1 : 0;
                    if (tick % 50 == 49)
                        yield return null;
                }

                if (stableStandingTicks < 50)
                {
                    failures.Add($"run {run}: bilateral 25 kg standing did not stabilize within {StandingTimeoutTicks} ticks");
                    continue;
                }

                PhysicalAthleteRig rig = controller.AthleteRig;
                var baselineCommands = new Dictionary<string, Quaternion>(AffectedJoints.Length);
                var baselineAppliedTargets = new Dictionary<string, Quaternion>(AffectedJoints.Length);
                var previousAppliedTargets = new Dictionary<string, Quaternion>(AffectedJoints.Length);
                var maximumCommandDeltaDegrees = new Dictionary<string, float>(AffectedJoints.Length);
                var maximumAppliedDeltaDegrees = new Dictionary<string, float>(AffectedJoints.Length);
                foreach (string jointId in AffectedJoints)
                {
                    PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
                    Assert.That(joint.HasPostPhysicsDiagnostic, Is.True, $"Missing runtime baseline diagnostic for {jointId} in run {run}.");
                    PoweredJointDiagnostic baseline = joint.PostPhysicsDiagnostic;
                    baselineCommands.Add(jointId, baseline.RequestedTarget);
                    baselineAppliedTargets.Add(jointId, baseline.AppliedTarget);
                    previousAppliedTargets.Add(jointId, joint.AppliedTarget);
                    maximumCommandDeltaDegrees.Add(jointId, 0f);
                    maximumAppliedDeltaDegrees.Add(jointId, 0f);
                    jointBaseline.AppendLine(string.Join(",",
                        run.ToString(CultureInfo.InvariantCulture), jointId,
                        F(PoweredJointController.SignedTwistRadians(baseline.RequestedTarget, Vector3.right) * Mathf.Rad2Deg),
                        F(PoweredJointController.SignedTwistRadians(baseline.AppliedTarget, Vector3.right) * Mathf.Rad2Deg),
                        F(PoweredJointController.SignedTwistRadians(baseline.ActualRelative, Vector3.right) * Mathf.Rad2Deg),
                        F(baseline.ErrorRad.x), F(baseline.ErrorRad.y), F(baseline.ErrorRad.z),
                        F(baseline.TargetAngularVelocityRadS.magnitude), F(baseline.ActualAngularVelocityRadS.magnitude),
                        F(baseline.SolverTorqueJointSpaceNm.x), F(baseline.SolverTorqueJointSpaceNm.y), F(baseline.SolverTorqueJointSpaceNm.z),
                        F(baseline.ModeledDemand), F(baseline.MaximumForceNm), F(baseline.LimitProximity)));
                }

                SquatPhysicalAdapter adapter = controller.Adapter;
                Vector3 rightStart = rig.Segments["right_foot"].Body.position;
                float supportPlaneY = adapter.Balance.SupportPlaneY;
                controller.RequestOneStepWalkoutIntent();

                string failure = string.Empty;
                int previousStageRank = -1;
                var stages = new List<SquatOneStepWalkoutState>();
                bool offGroundBeforeTranslation = false;
                bool nearZeroTouchObserved = false;
                bool leftSupportThroughoutSwing = true;
                bool totalSupportLoss = false;
                bool comFiniteThroughout = true;
                int rightLoadedTicksAfterTouchdown = 0;
                float maximumClearanceM = float.NegativeInfinity;
                float maximumPosteriorM = 0f;
                int walkoutTicks = 0;
                bool abortRecoveryCompleted = false;
                string abortReason = string.Empty;

                while (walkoutTicks < WalkoutTimeoutTicks &&
                    adapter.OneStepWalkoutState != SquatOneStepWalkoutState.ONE_STEP_READY &&
                    adapter.OneStepWalkoutState != SquatOneStepWalkoutState.ABORTED)
                {
                    Advance(runtime, controller);
                    walkoutTicks++;
                    SquatOneStepWalkoutState state = adapter.OneStepWalkoutState;
                    int rank = StageRank(state);
                    if (state == SquatOneStepWalkoutState.ABORT_RECOVERY)
                    {
                        if (string.IsNullOrEmpty(abortReason))
                            abortReason = $"walkout entered ABORT_RECOVERY: {adapter.OneStepWalkoutFailure}";
                    }
                    else if (state == SquatOneStepWalkoutState.ABORTED)
                    {
                        abortRecoveryCompleted = true;
                    }
                    else if (rank < 0)
                    {
                        failure = $"walkout entered {state}: {adapter.OneStepWalkoutFailure}";
                        break;
                    }
                    else if (rank != previousStageRank)
                    {
                        if (rank != previousStageRank + 1)
                        {
                            failure = $"state order skipped from rank {previousStageRank} to {state}";
                            break;
                        }
                        stages.Add(state);
                        previousStageRank = rank;
                    }

                    SquatBalanceObserver balance = adapter.Balance;
                    PhysicalFootContactDetector left = controller.LeftFootContact;
                    PhysicalFootContactDetector right = controller.RightFootContact;
                    float loadedImpulse = LoadedImpulseThreshold(controller);
                    float unloadImpulse = UnloadImpulseThreshold(controller);
                    bool leftLoaded = IsLoaded(left, loadedImpulse);
                    bool rightLoaded = IsLoaded(right, loadedImpulse);
                    Vector3 com = balance.SystemCom;
                    Vector3 comVelocity = balance.SystemComVelocity;
                    bool finite = balance.HasSupport && PoweredJointController.IsFinite(com) &&
                        PoweredJointController.IsFinite(comVelocity) && float.IsFinite(balance.CaptureAp) &&
                        float.IsFinite(balance.CaptureMl) && float.IsFinite(balance.CaptureMargin2D) &&
                        float.IsFinite(balance.SupportPlaneY) && float.IsFinite(balance.SystemMassKg);
                    comFiniteThroughout &= finite;
                    if (!finite)
                    {
                        failure = "non-finite COM/support telemetry or no measured support";
                        break;
                    }

                    if (!leftLoaded && !rightLoaded)
                    {
                        totalSupportLoss = true;
                        failure = "both feet lost measurable normal support in one simulation step";
                        break;
                    }
                    if (rank >= 2 && rank <= 4)
                        leftSupportThroughoutSwing &= leftLoaded;
                    if (rank >= 2 && rank <= 4 && !leftLoaded)
                    {
                        failure = "left stance foot stopped bearing measurable load during right-foot swing/touchdown";
                        break;
                    }

                    float rightImpulse = right.CompletedNormalImpulseTotal;
                    float posterior = Vector3.Dot(rig.Segments["right_foot"].Body.position - rightStart, Vector3.back);
                    float clearance = rig.Segments["right_foot"].Collider.bounds.min.y - supportPlaneY;
                    maximumClearanceM = Mathf.Max(maximumClearanceM, clearance);
                    maximumPosteriorM = Mathf.Max(maximumPosteriorM, posterior);
                    if (state == SquatOneStepWalkoutState.VERIFY_RIGHT_UNLOAD &&
                        right.CompletedContactCount > 0 && rightImpulse < unloadImpulse)
                        nearZeroTouchObserved = true;
                    if (right.CompletedContactCount == 0 && rightImpulse < unloadImpulse && posterior < 0.02f)
                        offGroundBeforeTranslation = true;
                    if (posterior > 0.02f && !offGroundBeforeTranslation)
                    {
                        failure = "right foot translated posterior before measured off-ground state";
                        break;
                    }
                    if (state == SquatOneStepWalkoutState.RIGHT_LOAD_ACCEPT && rightLoaded)
                        rightLoadedTicksAfterTouchdown++;
                    if (rig.Segments["pelvis"].Body.position.y < 0.55f ||
                        controller.Saddle.Barbell.Body.position.y < 0.80f)
                    {
                        failure = "pelvis/bar crossed the fall envelope";
                        break;
                    }

                    float maximumJointDeltaDegrees = 0f;
                    foreach (string jointId in AffectedJoints)
                    {
                        PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
                        Quaternion requested = joint.RequestedCommand.TargetRelativeRotation;
                        float deltaDegrees = Quaternion.Angle(baselineCommands[jointId], requested);
                        maximumCommandDeltaDegrees[jointId] = Mathf.Max(maximumCommandDeltaDegrees[jointId], deltaDegrees);
                        float appliedDeltaDegrees = Quaternion.Angle(baselineAppliedTargets[jointId], joint.AppliedTarget);
                        maximumAppliedDeltaDegrees[jointId] = Mathf.Max(maximumAppliedDeltaDegrees[jointId], appliedDeltaDegrees);
                        maximumJointDeltaDegrees = Mathf.Max(maximumJointDeltaDegrees, deltaDegrees);
                        if (!PoweredJointController.IsFinite(requested) ||
                            !PoweredJointController.IsFinite(joint.RequestedCommand.TargetRelativeAngularVelocityRadS))
                        {
                            failure = $"{jointId} produced a non-finite command";
                            break;
                        }
                        float flexionDegrees = PoweredJointController.SignedTwistRadians(requested, Vector3.right) * Mathf.Rad2Deg;
                        if (!float.IsFinite(flexionDegrees) || flexionDegrees < joint.Recipe.LowDegrees - 0.5f ||
                            flexionDegrees > joint.Recipe.HighDegrees + 0.5f)
                        {
                            failure = $"{jointId} exceeded its existing joint recipe flexion bound";
                            break;
                        }
                        float appliedFlexionDegrees = PoweredJointController.SignedTwistRadians(
                            joint.AppliedTarget, Vector3.right) * Mathf.Rad2Deg;
                        if (!PoweredJointController.IsFinite(joint.AppliedTarget) ||
                            !float.IsFinite(appliedFlexionDegrees) ||
                            appliedFlexionDegrees < joint.Recipe.LowDegrees - 0.5f ||
                            appliedFlexionDegrees > joint.Recipe.HighDegrees + 0.5f)
                        {
                            failure = $"{jointId} applied target exceeded its existing joint recipe flexion bound";
                            break;
                        }
                        float appliedStepRad = Quaternion.Angle(previousAppliedTargets[jointId], joint.AppliedTarget) * Mathf.Deg2Rad;
                        float allowedStepRad = joint.Profile.Value.MaxTargetRateRadS * dt + 0.0001f;
                        if (appliedStepRad > allowedStepRad)
                        {
                            failure = $"{jointId} exceeded PoweredJointController's existing target-rate bound";
                            break;
                        }
                        previousAppliedTargets[jointId] = joint.AppliedTarget;
                    }
                    if (!string.IsNullOrEmpty(failure))
                        break;

                    SquatObservationSnapshot sample = controller.ObservationCollector.LastSnapshot;
                    evidence.AppendLine(string.Join(",",
                        run.ToString(CultureInfo.InvariantCulture),
                        runtime.CurrentTime.Tick.ToString(CultureInfo.InvariantCulture),
                        state.ToString(),
                        F(com.x), F(com.y), F(com.z),
                        F(comVelocity.x), F(comVelocity.y), F(comVelocity.z), F(balance.CaptureMargin2D),
                        F(left.CompletedNormalImpulseTotal), F(rightImpulse),
                        left.CompletedContactCount.ToString(CultureInfo.InvariantCulture),
                        right.CompletedContactCount.ToString(CultureInfo.InvariantCulture),
                        F(rig.Segments["right_foot"].Body.position.x),
                        F(rig.Segments["right_foot"].Body.position.y),
                        F(rig.Segments["right_foot"].Body.position.z),
                        F(clearance), F(posterior),
                        F(sample.Bar.LinearVelocityWorldMetersPerSecond.Length),
                        F(sample.Bar.AngularVelocityBarRadiansPerSecond.Length),
                        F(maximumJointDeltaDegrees)));

                    if (walkoutTicks % 50 == 0)
                        yield return null;
                }

                if (!string.IsNullOrEmpty(abortReason))
                    failure = abortReason + (abortRecoveryCompleted
                        ? "; bounded ABORT_RECOVERY reached ABORTED safely"
                        : "; bounded ABORT_RECOVERY did not reach ABORTED");
                else if (string.IsNullOrEmpty(failure) && adapter.OneStepWalkoutState != SquatOneStepWalkoutState.ONE_STEP_READY)
                    failure = $"bounded walkout did not reach ONE_STEP_READY in {WalkoutTimeoutTicks} ticks ({adapter.OneStepWalkoutState}, {adapter.OneStepWalkoutFailure})";
                if (string.IsNullOrEmpty(failure) && (!leftSupportThroughoutSwing || totalSupportLoss || !comFiniteThroughout ||
                    !offGroundBeforeTranslation || !nearZeroTouchObserved || maximumClearanceM < 0.04f ||
                    maximumPosteriorM < SquatPhysicalAdapter.OneStepPosteriorDistanceM - 0.03f ||
                    rightLoadedTicksAfterTouchdown < 10))
                    failure = $"physical event gate failed: leftSupport={leftSupportThroughoutSwing}, totalLoss={totalSupportLoss}, finite={comFiniteThroughout}, nearZero={nearZeroTouchObserved}, offGroundFirst={offGroundBeforeTranslation}, clear={maximumClearanceM:F3}m, posterior={maximumPosteriorM:F3}m, rightLoadedTicks={rightLoadedTicksAfterTouchdown}";

                SquatStartPredicateDiagnostic startPredicate = null;
                int startPredicateRun = 0;
                if (string.IsNullOrEmpty(failure))
                {
                    for (int tick = 0; tick < StartPredicateTimeoutTicks; tick++)
                    {
                        Advance(runtime, controller);
                        startPredicate = controller.AttemptOrchestrator.EvaluateStartPredicate(
                            controller.ObservationCollector.LastSnapshot);
                        startPredicateRun = startPredicate.OverallStartCandidate ? startPredicateRun + 1 : 0;
                        if (startPredicateRun >= controller.AttemptOrchestrator.RequiredStartSamples)
                            break;
                        if (controller.AttemptOrchestrator.HasSquatCommand || adapter.State != SquatState.SETUP || adapter.Sq != 0f)
                        {
                            failure = "qualification started or advanced the judged squat while observing the start predicate";
                            break;
                        }
                        if (tick % 50 == 49)
                            yield return null;
                    }

                    if (string.IsNullOrEmpty(failure) && !StartPredicatePasses(
                            startPredicate, startPredicateRun, controller.AttemptOrchestrator.RequiredStartSamples))
                        failure = "unchanged production squat-start predicate did not pass its persistence window";
                }

                if (string.IsNullOrEmpty(failure))
                {
                    failures.Add($"run {run}: PASS; posterior={maximumPosteriorM:F3}m, clearance={maximumClearanceM:F3}m, start samples={startPredicateRun}/{startPredicate.RequiredRunLength}");
                }
                else
                {
                    failures.Add($"run {run}: FAIL; {failure}");
                }

                foreach (string jointId in AffectedJoints)
                    Debug.Log($"GAM60_RUN={run} JOINT={jointId} MAX_DELTA_FROM_A_REQUEST_DEG={maximumCommandDeltaDegrees[jointId]:F3} MAX_DELTA_FROM_B_APPLIED_DEG={maximumAppliedDeltaDegrees[jointId]:F3}");
                Debug.Log($"GAM60_RUN={run} STATE={adapter.OneStepWalkoutState} TICKS={walkoutTicks} POSTERIOR_M={maximumPosteriorM:F4} CLEARANCE_M={maximumClearanceM:F4} OFFGROUND_BEFORE_TRANSLATION={offGroundBeforeTranslation} NEAR_ZERO_TOUCH={nearZeroTouchObserved} FAILURE={failure}");
            }

            string outputPath = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-60", "one-step-25kg.csv");
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, evidence.ToString());
            string baselinePath = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-60", "one-step-25kg-joint-baseline.csv");
            File.WriteAllText(baselinePath, jointBaseline.ToString());
            Assert.That(failures.Count, Is.EqualTo(FreshRuns), "The qualification did not complete all requested scene resets.");
            Assert.That(failures.FindAll(value => value.Contains(": FAIL;")), Is.Empty,
                string.Join(Environment.NewLine, failures) + Environment.NewLine + "Evidence: " + outputPath + Environment.NewLine + "Joint baseline: " + baselinePath);
            Assert.That(failures.FindAll(value => value.Contains(": PASS;")), Has.Count.EqualTo(FreshRuns),
                string.Join(Environment.NewLine, failures));
        }

        private static void Advance(FoundationRuntime runtime, SquatPhysicalPrototypeController controller)
        {
            Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            controller.LeftFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
            controller.RightFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
        }

        private static float LoadedImpulseThreshold(SquatPhysicalPrototypeController controller) =>
            Mathf.Max(0.02f, controller.Adapter.Balance.SystemMassKg *
                SquatBalanceObserver.GravityMagnitudeMps2 * (float)SimulationConstants.FixedDeltaTimeSeconds * 0.05f);

        private static float UnloadImpulseThreshold(SquatPhysicalPrototypeController controller) =>
            Mathf.Max(0.02f, controller.Adapter.Balance.SystemMassKg *
                SquatBalanceObserver.GravityMagnitudeMps2 * (float)SimulationConstants.FixedDeltaTimeSeconds * 0.01f);

        private static bool IsLoaded(PhysicalFootContactDetector foot, float threshold) =>
            foot != null && foot.CompletedContactCount > 0 &&
            float.IsFinite(foot.CompletedNormalImpulseTotal) && foot.CompletedNormalImpulseTotal >= threshold;

        private static int StageRank(SquatOneStepWalkoutState state)
        {
            switch (state)
            {
                case SquatOneStepWalkoutState.SHIFT_TO_LEFT_STANCE: return 0;
                case SquatOneStepWalkoutState.VERIFY_RIGHT_UNLOAD: return 1;
                case SquatOneStepWalkoutState.RIGHT_SWING_CLEAR: return 2;
                case SquatOneStepWalkoutState.RIGHT_SWING_BACK: return 3;
                case SquatOneStepWalkoutState.RIGHT_TOUCHDOWN: return 4;
                case SquatOneStepWalkoutState.RIGHT_LOAD_ACCEPT: return 5;
                case SquatOneStepWalkoutState.BILATERAL_RECOVERY: return 6;
                case SquatOneStepWalkoutState.ONE_STEP_READY: return 7;
                default: return -1;
            }
        }

        private static bool StartPredicatePasses(
            SquatStartPredicateDiagnostic diagnostic,
            int consecutiveValidSamples,
            int requiredSamples)
        {
            if (diagnostic == null || !diagnostic.OverallStartCandidate ||
                consecutiveValidSamples < requiredSamples ||
                diagnostic.RequiredRunLength != requiredSamples ||
                !diagnostic.BarAvailable || !diagnostic.BarLinearSpeedPass || !diagnostic.BarAngularSpeedPass ||
                !diagnostic.SupportAvailable || !diagnostic.SupportPresent ||
                !diagnostic.LeftFootAvailable || !diagnostic.LeftFootContact ||
                !diagnostic.RightFootAvailable || !diagnostic.RightFootContact ||
                !diagnostic.LeftKneePass || !diagnostic.RightKneePass ||
                !diagnostic.LeftHipPass || !diagnostic.RightHipPass ||
                !diagnostic.AbdomenPass || !diagnostic.ThoraxPass ||
                Mathf.Abs(diagnostic.LeftKneeActualAngleRad) > 5f * Mathf.Deg2Rad ||
                Mathf.Abs(diagnostic.RightKneeActualAngleRad) > 5f * Mathf.Deg2Rad ||
                Mathf.Abs(diagnostic.LeftHipActualAngleRad) > 10f * Mathf.Deg2Rad ||
                Mathf.Abs(diagnostic.RightHipActualAngleRad) > 10f * Mathf.Deg2Rad ||
                Mathf.Abs(diagnostic.AbdomenActualAngleRad) > 10f * Mathf.Deg2Rad ||
                Mathf.Abs(diagnostic.ThoraxActualAngleRad) > 10f * Mathf.Deg2Rad)
                return false;
            return diagnostic.BarLinearSpeedThresholdMps == 0.02f &&
                diagnostic.BarAngularSpeedThresholdRadS == 0.20f;
        }

        private static string F(float value) => value.ToString("F6", CultureInfo.InvariantCulture);
    }
}
