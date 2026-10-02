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
    public sealed class GAM13V23CPlantSignTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int MeasurementWindowEndTick = 5;
        private const float MinimumClearCopDifferenceM = 0.002f;
        private const float MaximumWindowComDriftM = 0.01f;
        private const float MinimumSignalToScatterRatio = 3f;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

        [UnityTest]
        public IEnumerator GAM13_V23C_CLEAN_UNLOADED_AP_PLANT_SIGN_PROBE()
        {
            string outputDirectory = Environment.GetEnvironmentVariable("GAM13_V23C_OUTPUT_DIRECTORY");
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Path.Combine(
                    Directory.GetCurrentDirectory(),
                    "Artifacts", "Measurements", "GAM-13", "v2-3c-plant-sign",
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", Invariant));
            }

            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            ProbeRun plus = null;
            ProbeRun minus = null;
            yield return RunArm(+1f, outputDirectory, run => plus = run);
            yield return RunArm(-1f, outputDirectory, run => minus = run);

            Assert.That(plus, Is.Not.Null);
            Assert.That(minus, Is.Not.Null);
            Assert.That(plus.Samples.Count, Is.EqualTo(MeasurementWindowEndTick + 1));
            Assert.That(minus.Samples.Count, Is.EqualTo(MeasurementWindowEndTick + 1));
            Assert.That(plus.Tick1TopThree.Count, Is.EqualTo(3));
            Assert.That(minus.Tick1TopThree.Count, Is.EqualTo(3));

            PlantSummary summary = Summarize(plus, minus);
            WriteSummary(outputDirectory, summary, plus, minus);
            Debug.Log(string.Format(
                Invariant,
                "GAM13_V23C_PLANT_SIGN classification={0} g_ap={1:R}m/rad cop_plus={2:R}m cop_minus={3:R}m delta={4:R}m window=1..{5} path={6}",
                summary.Classification,
                summary.GapMPerRad,
                summary.PlusCopMeanM,
                summary.MinusCopMeanM,
                summary.CopDifferenceM,
                MeasurementWindowEndTick,
                outputDirectory));
        }

        private static IEnumerator RunArm(float sign, string outputDirectory, Action<ProbeRun> completed)
        {
            string arm = sign > 0f ? "PLUS_1_DEG" : "MINUS_1_DEG";
            float residualRad = sign * Mathf.Deg2Rad;
            Assert.That(Application.unityVersion, Is.EqualTo("6000.3.22f1"));

            AsyncOperation loadScene = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(loadScene, Is.Not.Null, "The cleaned squat qualification scene is missing.");
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
            controller.SetLoad(0f);

            FoundationRuntime runtime = bootstrap.Runtime;
            SquatPhysicalAdapter adapter = controller.Adapter;
            Assert.That(controller.Saddle, Is.Null);
            Assert.That(adapter.HasStandingComReference, Is.True);
            Assert.That(adapter.Sq, Is.EqualTo(0f).Within(0.000001f));
            Assert.That(controller.TickZeroSubstrateValidated, Is.True);
            Assert.That(controller.TickZeroValidationTick, Is.EqualTo(0ul));
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul), "Each arm must start from a fresh tick-0 reset.");

            Dictionary<string, JointBaseline> baseline = CaptureJointBaseline(rig.PoweredController);
            adapter.ApFeedbackContributionEnabled = false;
            adapter.AnkleSagittalOffsetAdditiveRad = residualRad;
            rig.PrimeCommandSource();
            AssertOnlyAnkleResidualChanged(rig.PoweredController, baseline, residualRad);

            var run = new ProbeRun(arm, residualRad, outputDirectory);
            float initialComAp = adapter.Balance.MeasureCurrentSystemCom(controller.Saddle).z;
            float initialComVelocityAp = adapter.Balance.SystemComVelocity.z;
            ProbeSample initial = CaptureTickZero(controller, baseline, residualRad, initialComAp, initialComVelocityAp);
            run.Samples.Add(initial);

            for (int tick = 1; tick <= MeasurementWindowEndTick; tick++)
            {
                int stepped = runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                Assert.That(stepped, Is.EqualTo(1), $"Expected exactly one physics step for {arm} tick {tick}.");
                Assert.That(controller.ObservationCollector.HasLastSnapshot, Is.True,
                    $"No post-physics sample was captured for {arm} tick {tick}.");
                SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
                Assert.That(snapshot.SimulationTick, Is.EqualTo((ulong)tick));

                ProbeSample sample = CapturePostPhysics(controller, snapshot, baseline, residualRad);
                ProbeSample previous = run.Samples[run.Samples.Count - 1];
                sample.ComAccelerationApMps2 = (sample.ComVelocityApMps - previous.ComVelocityApMps) /
                    (float)SimulationConstants.FixedDeltaTimeSeconds;
                run.Samples.Add(sample);
                if (tick == 1)
                    run.Tick1TopThree = TopThreeDriveChannels(rig.PoweredController);
            }

            WriteTrace(run);
            WriteTopThree(run);
            completed(run);
        }

        private static Dictionary<string, JointBaseline> CaptureJointBaseline(PoweredJointController powered)
        {
            var result = new Dictionary<string, JointBaseline>(StringComparer.Ordinal);
            foreach (PoweredJointController.PoweredJointRuntime joint in powered.Joints)
            {
                result.Add(joint.Id, new JointBaseline(
                    joint.RequestedCommand.TargetRelativeRotation,
                    joint.RequestedCommand.TargetRelativeAngularVelocityRadS,
                    joint.RequestedCommand.Effort,
                    joint.RequestedCommand.AthleteStrengthScale,
                    joint.Joint.angularXDrive,
                    joint.Joint.angularYZDrive,
                    joint.Diagnostic.MaximumForceNm));
            }
            return result;
        }

        private static void AssertOnlyAnkleResidualChanged(
            PoweredJointController powered,
            Dictionary<string, JointBaseline> baseline,
            float residualRad)
        {
            foreach (PoweredJointController.PoweredJointRuntime joint in powered.Joints)
            {
                JointBaseline before = baseline[joint.Id];
                bool ankle = joint.Id == "left_foot" || joint.Id == "right_foot";
                if (ankle)
                {
                    Quaternion delta = PoweredJointController.NormalizeCanonical(
                        Quaternion.Inverse(before.Target) * joint.RequestedCommand.TargetRelativeRotation);
                    float appliedResidualRad = PoweredJointController.SignedTwistRadians(delta, Vector3.right);
                    Assert.That(appliedResidualRad, Is.EqualTo(residualRad).Within(0.0001f), joint.Id);
                    Quaternion twist = Quaternion.AngleAxis(appliedResidualRad * Mathf.Rad2Deg, Vector3.right);
                    Assert.That(Quaternion.Angle(twist, delta), Is.LessThanOrEqualTo(0.001f), joint.Id);
                }
                else
                {
                    Assert.That(
                        Quaternion.Angle(before.Target, joint.RequestedCommand.TargetRelativeRotation),
                        Is.LessThanOrEqualTo(0.001f),
                        $"Unexpected target change on {joint.Id}.");
                }

                Assert.That(
                    Vector3.Distance(before.TargetVelocity, joint.RequestedCommand.TargetRelativeAngularVelocityRadS),
                    Is.LessThanOrEqualTo(0.000001f),
                    $"Unexpected target-rate change on {joint.Id}.");
                Assert.That(joint.RequestedCommand.Effort, Is.EqualTo(before.Effort).Within(0.000001f), joint.Id);
                Assert.That(joint.RequestedCommand.AthleteStrengthScale,
                    Is.EqualTo(before.StrengthScale).Within(0.000001f), joint.Id);
                AssertDriveEqual(before.TwistDrive, joint.Joint.angularXDrive, joint.Id + " TWIST");
                AssertDriveEqual(before.SwingDrive, joint.Joint.angularYZDrive, joint.Id + " SWING_YZ");
                Assert.That(joint.Diagnostic.MaximumForceNm,
                    Is.EqualTo(before.MaximumForceNm).Within(0.001f), joint.Id);
            }
        }

        private static void AssertDriveEqual(JointDrive expected, JointDrive actual, string context)
        {
            Assert.That(actual.positionSpring, Is.EqualTo(expected.positionSpring).Within(0.0001f), context);
            Assert.That(actual.positionDamper, Is.EqualTo(expected.positionDamper).Within(0.0001f), context);
            Assert.That(actual.maximumForce, Is.EqualTo(expected.maximumForce).Within(0.001f), context);
            Assert.That(actual.useAcceleration, Is.EqualTo(expected.useAcceleration), context);
        }

        private static ProbeSample CaptureTickZero(
            SquatPhysicalPrototypeController controller,
            Dictionary<string, JointBaseline> baseline,
            float residualRad,
            float comAp,
            float comVelocityAp)
        {
            PhysicalAthleteRig rig = controller.AthleteRig;
            PoweredJointController powered = rig.PoweredController;
            PoweredJointController.PoweredJointRuntime left = powered.GetJoint("left_foot");
            PoweredJointController.PoweredJointRuntime right = powered.GetJoint("right_foot");
            return new ProbeSample
            {
                Tick = 0,
                SampleKind = "TICK_0_PRE_PHYSICS_COMMAND",
                RequestedResidualRad = residualRad,
                LeftTargetResidualRad = AppliedTargetResidual(left.Diagnostic, baseline["left_foot"]),
                RightTargetResidualRad = AppliedTargetResidual(right.Diagnostic, baseline["right_foot"]),
                LeftActualTwistRad = PoweredJointController.SignedTwistRadians(left.Diagnostic.ActualRelative, Vector3.right),
                RightActualTwistRad = PoweredJointController.SignedTwistRadians(right.Diagnostic.ActualRelative, Vector3.right),
                CopApM = float.NaN,
                SupportCenterApM = controller.Adapter.CanonicalPlantarSupportCenter.z,
                ComApM = comAp,
                ComVelocityApMps = comVelocityAp,
                ComAccelerationApMps2 = float.NaN,
                ContactSampleAvailable = false,
                LeftFootContact = controller.LeftFootContact != null && controller.LeftFootContact.IsInContact,
                RightFootContact = controller.RightFootContact != null && controller.RightFootContact.IsInContact,
                LeftGeometricContact = GeometricFootContact(rig, "left_foot"),
                RightGeometricContact = GeometricFootContact(rig, "right_foot"),
                LeftTargetActualErrorRad = left.Diagnostic.ErrorRad.x,
                RightTargetActualErrorRad = right.Diagnostic.ErrorRad.x,
                LeftTwistDemandNm = left.Diagnostic.TwistDriveDemandNm,
                RightTwistDemandNm = right.Diagnostic.TwistDriveDemandNm,
                LeftTwistDemandFraction = left.Diagnostic.TwistDriveDemandFraction,
                RightTwistDemandFraction = right.Diagnostic.TwistDriveDemandFraction,
                LeftSwingDemandNm = left.Diagnostic.SwingDriveDemandNm,
                RightSwingDemandNm = right.Diagnostic.SwingDriveDemandNm,
                LeftSwingDemandFraction = left.Diagnostic.SwingDriveDemandFraction,
                RightSwingDemandFraction = right.Diagnostic.SwingDriveDemandFraction,
                TrunkPitchRad = WorldPitch(rig.Segments["thorax"].Body.rotation),
                SolverTorqueAvailable = false
            };
        }

        private static ProbeSample CapturePostPhysics(
            SquatPhysicalPrototypeController controller,
            SquatObservationSnapshot snapshot,
            Dictionary<string, JointBaseline> baseline,
            float residualRad)
        {
            PoweredJointController powered = controller.AthleteRig.PoweredController;
            PoweredJointController.PoweredJointRuntime left = powered.GetJoint("left_foot");
            PoweredJointController.PoweredJointRuntime right = powered.GetJoint("right_foot");
            PoweredJointDiagnostic leftDiagnostic = left.PostPhysicsDiagnostic;
            PoweredJointDiagnostic rightDiagnostic = right.PostPhysicsDiagnostic;
            SquatSupportObservation support = snapshot.Support;
            bool hasCop = support.EngineContactPointAvailability == SquatTelemetryAvailability.AVAILABLE;
            bool hasSupport = support.SupportAvailability == SquatTelemetryAvailability.AVAILABLE && support.HasSupport;

            return new ProbeSample
            {
                Tick = (int)snapshot.SimulationTick,
                SampleKind = "POST_PHYSICS",
                RequestedResidualRad = residualRad,
                LeftTargetResidualRad = AppliedTargetResidual(leftDiagnostic, baseline["left_foot"]),
                RightTargetResidualRad = AppliedTargetResidual(rightDiagnostic, baseline["right_foot"]),
                LeftActualTwistRad = PoweredJointController.SignedTwistRadians(leftDiagnostic.ActualRelative, Vector3.right),
                RightActualTwistRad = PoweredJointController.SignedTwistRadians(rightDiagnostic.ActualRelative, Vector3.right),
                CopApM = hasCop ? support.EngineContactPointWorldMeters.Z : float.NaN,
                SupportCenterApM = hasSupport
                    ? support.SupportCenterWorldMeters.Z
                    : controller.Adapter.CanonicalPlantarSupportCenter.z,
                ComApM = snapshot.Support.SystemComWorldMeters.Z,
                ComVelocityApMps = snapshot.Support.SystemComVelocityWorldMetersPerSecond.Z,
                ComAccelerationApMps2 = float.NaN,
                ContactSampleAvailable = snapshot.LeftFoot.Availability == SquatTelemetryAvailability.AVAILABLE &&
                    snapshot.RightFoot.Availability == SquatTelemetryAvailability.AVAILABLE,
                LeftFootContact = snapshot.LeftFoot.IsInContact,
                RightFootContact = snapshot.RightFoot.IsInContact,
                LeftGeometricContact = GeometricFootContact(controller.AthleteRig, "left_foot"),
                RightGeometricContact = GeometricFootContact(controller.AthleteRig, "right_foot"),
                LeftTargetActualErrorRad = leftDiagnostic.ErrorRad.x,
                RightTargetActualErrorRad = rightDiagnostic.ErrorRad.x,
                LeftTwistDemandNm = leftDiagnostic.TwistDriveDemandNm,
                RightTwistDemandNm = rightDiagnostic.TwistDriveDemandNm,
                LeftTwistDemandFraction = leftDiagnostic.TwistDriveDemandFraction,
                RightTwistDemandFraction = rightDiagnostic.TwistDriveDemandFraction,
                LeftSwingDemandNm = leftDiagnostic.SwingDriveDemandNm,
                RightSwingDemandNm = rightDiagnostic.SwingDriveDemandNm,
                LeftSwingDemandFraction = leftDiagnostic.SwingDriveDemandFraction,
                RightSwingDemandFraction = rightDiagnostic.SwingDriveDemandFraction,
                TrunkPitchRad = snapshot.TrunkWorldPitchRadians,
                SolverTorqueAvailable = true,
                LeftSolverTorqueNm = leftDiagnostic.SolverTorqueJointSpaceNm,
                RightSolverTorqueNm = rightDiagnostic.SolverTorqueJointSpaceNm
            };
        }

        private static float AppliedTargetResidual(
            PoweredJointDiagnostic diagnostic,
            JointBaseline baseline)
        {
            Quaternion delta = PoweredJointController.NormalizeCanonical(
                Quaternion.Inverse(baseline.Target) * diagnostic.AppliedTarget);
            return PoweredJointController.SignedTwistRadians(delta, Vector3.right);
        }

        private static bool GeometricFootContact(PhysicalAthleteRig rig, string footId)
        {
            float footBottom = rig.Segments[footId].Collider.bounds.min.y;
            float platformTop = rig.PlatformCollider.bounds.max.y;
            return Mathf.Abs(footBottom - platformTop) <= PhysicalAthleteDefinition.AnchorToleranceMeters;
        }

        private static float WorldPitch(Quaternion worldRotation)
        {
            Vector3 forward = worldRotation * Vector3.forward;
            return Mathf.Atan2(forward.y, forward.z);
        }

        private static List<DriveChannelPressure> TopThreeDriveChannels(PoweredJointController powered)
        {
            var channels = new List<DriveChannelPressure>(30);
            foreach (PoweredJointController.PoweredJointRuntime joint in powered.Joints)
            {
                if (!joint.Profile.HasValue || !joint.HasPostPhysicsDiagnostic)
                    continue;

                PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                JointFamilyProfile profile = joint.Profile.Value;
                Vector3 target = RotationLog(diagnostic.AppliedTarget);
                Vector3 actual = RotationLog(diagnostic.ActualRelative);
                Vector3 positionTorque = diagnostic.ErrorRad * profile.Spring;
                Vector3 velocityTorque =
                    (diagnostic.TargetAngularVelocityRadS - diagnostic.ActualAngularVelocityRadS) * profile.Damper;

                channels.Add(new DriveChannelPressure(
                    joint.Id,
                    "TWIST",
                    new Vector3(target.x, 0f, 0f),
                    new Vector3(actual.x, 0f, 0f),
                    new Vector3(positionTorque.x, 0f, 0f),
                    new Vector3(velocityTorque.x, 0f, 0f),
                    diagnostic.MaximumForceNm,
                    diagnostic.TwistDriveDemandFraction,
                    diagnostic.SolverTorqueJointSpaceNm));

                if (joint.Recipe.Kind != PhysicalJointKind.Hinge && float.IsFinite(diagnostic.SwingDriveDemandFraction))
                {
                    channels.Add(new DriveChannelPressure(
                        joint.Id,
                        "SWING_YZ",
                        new Vector3(0f, target.y, target.z),
                        new Vector3(0f, actual.y, actual.z),
                        new Vector3(0f, positionTorque.y, positionTorque.z),
                        new Vector3(0f, velocityTorque.y, velocityTorque.z),
                        diagnostic.MaximumForceNm,
                        diagnostic.SwingDriveDemandFraction,
                        diagnostic.SolverTorqueJointSpaceNm));
                }
            }

            channels.Sort((left, right) =>
            {
                int demand = right.NormalizedDemand.CompareTo(left.NormalizedDemand);
                if (demand != 0)
                    return demand;
                int joint = string.CompareOrdinal(left.JointId, right.JointId);
                return joint != 0 ? joint : string.CompareOrdinal(left.Channel, right.Channel);
            });
            if (channels.Count > 3)
                channels.RemoveRange(3, channels.Count - 3);
            return channels;
        }

        private static Vector3 RotationLog(Quaternion value)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(value);
            float vectorMagnitude = Mathf.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z);
            if (vectorMagnitude <= 0.000001f)
                return Vector3.zero;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(q.w, -1f, 1f));
            return new Vector3(q.x, q.y, q.z) * (angle / vectorMagnitude);
        }

        private static PlantSummary Summarize(ProbeRun plus, ProbeRun minus)
        {
            float plusCop = WindowMean(plus, sample => sample.CopApM);
            float minusCop = WindowMean(minus, sample => sample.CopApM);
            float deltaCop = plusCop - minusCop;
            float thetaPlus = plus.RequestedResidualRad;
            float thetaMinus = minus.RequestedResidualRad;
            float gap = (plusCop - minusCop) / (thetaPlus - thetaMinus);
            float pairMidpoint = 0.5f * (plusCop + minusCop);
            float plusCenteredResponse = plusCop - pairMidpoint;
            float minusCenteredResponse = minusCop - pairMidpoint;
            float standardError = PairedCopDifferenceStandardError(plus, minus);
            float signalToScatter = standardError > 0f
                ? Mathf.Abs(deltaCop) / standardError
                : float.PositiveInfinity;
            float maximumComDrift = Mathf.Max(MaximumComDrift(plus), MaximumComDrift(minus));
            bool contactsPersist = BilateralContactEverySample(plus) && BilateralContactEverySample(minus);
            bool copComplete = WindowHasFiniteCop(plus) && WindowHasFiniteCop(minus);
            bool targetRealizationMatches = TargetRealizationMatches(plus) && TargetRealizationMatches(minus);
            bool windowIsEarly = maximumComDrift <= MaximumWindowComDriftM;
            bool oppositeResponses = plusCenteredResponse * minusCenteredResponse < 0f;
            bool clear = Mathf.Abs(deltaCop) >= MinimumClearCopDifferenceM &&
                signalToScatter >= MinimumSignalToScatterRatio;

            string classification = "NON_IDENTIFYING";
            if (copComplete && contactsPersist && windowIsEarly && targetRealizationMatches && oppositeResponses && clear)
            {
                if (gap > 0f)
                    classification = "POSITIVE_ANKLE_TARGET_MOVES_COP_FORWARD";
                else if (gap < 0f)
                    classification = "NEGATIVE_ANKLE_TARGET_MOVES_COP_BACKWARD";
            }

            return new PlantSummary
            {
                PlusCopMeanM = plusCop,
                MinusCopMeanM = minusCop,
                CopDifferenceM = deltaCop,
                ThetaPlusRad = thetaPlus,
                ThetaMinusRad = thetaMinus,
                GapMPerRad = gap,
                PairMidpointCopM = pairMidpoint,
                PlusCenteredResponseM = plusCenteredResponse,
                MinusCenteredResponseM = minusCenteredResponse,
                PairedCopDifferenceStandardErrorM = standardError,
                SignalToScatterRatio = signalToScatter,
                PlusEarlyComAccelerationMps2 = plus.Samples[1].ComAccelerationApMps2,
                MinusEarlyComAccelerationMps2 = minus.Samples[1].ComAccelerationApMps2,
                MaximumWindowComDriftM = maximumComDrift,
                BilateralContactEverySample = contactsPersist,
                WindowCopComplete = copComplete,
                TargetRealizationMatches = targetRealizationMatches,
                MeasurementWindowIsEarly = windowIsEarly,
                OppositeCenteredResponses = oppositeResponses,
                ClearSignal = clear,
                Classification = classification
            };
        }

        private static float WindowMean(ProbeRun run, Func<ProbeSample, float> value)
        {
            float sum = 0f;
            int count = 0;
            for (int index = 1; index < run.Samples.Count; index++)
            {
                float current = value(run.Samples[index]);
                if (!float.IsFinite(current))
                    continue;
                sum += current;
                count++;
            }
            return count == MeasurementWindowEndTick ? sum / count : float.NaN;
        }

        private static float PairedCopDifferenceStandardError(ProbeRun plus, ProbeRun minus)
        {
            var differences = new float[MeasurementWindowEndTick];
            float mean = 0f;
            for (int index = 1; index <= MeasurementWindowEndTick; index++)
            {
                differences[index - 1] = plus.Samples[index].CopApM - minus.Samples[index].CopApM;
                if (!float.IsFinite(differences[index - 1]))
                    return float.NaN;
                mean += differences[index - 1];
            }
            mean /= differences.Length;
            float squaredError = 0f;
            for (int index = 0; index < differences.Length; index++)
            {
                float difference = differences[index] - mean;
                squaredError += difference * difference;
            }
            float sampleVariance = squaredError / (differences.Length - 1);
            return Mathf.Sqrt(sampleVariance / differences.Length);
        }

        private static float MaximumComDrift(ProbeRun run)
        {
            float initial = run.Samples[0].ComApM;
            float maximum = 0f;
            for (int index = 1; index < run.Samples.Count; index++)
                maximum = Mathf.Max(maximum, Mathf.Abs(run.Samples[index].ComApM - initial));
            return maximum;
        }

        private static bool BilateralContactEverySample(ProbeRun run)
        {
            for (int index = 1; index < run.Samples.Count; index++)
            {
                ProbeSample sample = run.Samples[index];
                if (!sample.ContactSampleAvailable || !sample.LeftFootContact || !sample.RightFootContact)
                    return false;
            }
            return true;
        }

        private static bool WindowHasFiniteCop(ProbeRun run)
        {
            for (int index = 1; index < run.Samples.Count; index++)
            {
                if (!float.IsFinite(run.Samples[index].CopApM))
                    return false;
            }
            return true;
        }

        private static bool TargetRealizationMatches(ProbeRun run)
        {
            float toleranceRad = 0.1f * Mathf.Deg2Rad;
            for (int index = 1; index < run.Samples.Count; index++)
            {
                ProbeSample sample = run.Samples[index];
                if (!float.IsFinite(sample.LeftTargetResidualRad) || !float.IsFinite(sample.RightTargetResidualRad) ||
                    Mathf.Abs(sample.LeftTargetResidualRad - run.RequestedResidualRad) > toleranceRad ||
                    Mathf.Abs(sample.RightTargetResidualRad - run.RequestedResidualRad) > toleranceRad)
                    return false;
            }
            return true;
        }

        private static void WriteTrace(ProbeRun run)
        {
            var csv = new StringBuilder(4096);
            csv.AppendLine(
                "arm,requested_ankle_ap_target_residual_rad,tick,sample_kind,ap_feedback_contribution_enabled," +
                "left_logical_ankle_target_delta_rad,right_logical_ankle_target_delta_rad," +
                "left_actual_ankle_twist_rad,right_actual_ankle_twist_rad,cop_ap_m,support_center_ap_m," +
                "com_ap_m,com_velocity_ap_mps,early_com_acceleration_ap_mps2,contact_sample_available," +
                "left_foot_contact,right_foot_contact,left_geometric_contact,right_geometric_contact," +
                "left_ankle_target_actual_error_rad,right_ankle_target_actual_error_rad," +
                "left_ankle_twist_drive_demand_nm,right_ankle_twist_drive_demand_nm," +
                "left_ankle_twist_normalized_demand,right_ankle_twist_normalized_demand," +
                "left_ankle_swing_yz_drive_demand_nm,right_ankle_swing_yz_drive_demand_nm," +
                "left_ankle_swing_yz_normalized_demand,right_ankle_swing_yz_normalized_demand," +
                "trunk_world_pitch_rad,solver_torque_diagnostic_class," +
                "left_solver_torque_x_nm,left_solver_torque_y_nm,left_solver_torque_z_nm," +
                "right_solver_torque_x_nm,right_solver_torque_y_nm,right_solver_torque_z_nm");

            foreach (ProbeSample sample in run.Samples)
            {
                var row = new StringBuilder(512);
                Field(row, run.Arm);
                Field(row, run.RequestedResidualRad);
                Field(row, sample.Tick.ToString(Invariant));
                Field(row, sample.SampleKind);
                Field(row, "false");
                Field(row, sample.LeftTargetResidualRad);
                Field(row, sample.RightTargetResidualRad);
                Field(row, sample.LeftActualTwistRad);
                Field(row, sample.RightActualTwistRad);
                Field(row, sample.CopApM);
                Field(row, sample.SupportCenterApM);
                Field(row, sample.ComApM);
                Field(row, sample.ComVelocityApMps);
                Field(row, sample.ComAccelerationApMps2);
                Field(row, Bool(sample.ContactSampleAvailable));
                Field(row, Bool(sample.LeftFootContact));
                Field(row, Bool(sample.RightFootContact));
                Field(row, Bool(sample.LeftGeometricContact));
                Field(row, Bool(sample.RightGeometricContact));
                Field(row, sample.LeftTargetActualErrorRad);
                Field(row, sample.RightTargetActualErrorRad);
                Field(row, sample.LeftTwistDemandNm);
                Field(row, sample.RightTwistDemandNm);
                Field(row, sample.LeftTwistDemandFraction);
                Field(row, sample.RightTwistDemandFraction);
                Field(row, sample.LeftSwingDemandNm);
                Field(row, sample.RightSwingDemandNm);
                Field(row, sample.LeftSwingDemandFraction);
                Field(row, sample.RightSwingDemandFraction);
                Field(row, sample.TrunkPitchRad);
                Field(row, sample.SolverTorqueAvailable
                    ? "ENGINE_SOLVER_DIAGNOSTIC_NOT_DRIVE_TORQUE"
                    : "NOT_CAPTURED_BEFORE_PHYSICS");
                Field(row, sample.SolverTorqueAvailable ? sample.LeftSolverTorqueNm.x : float.NaN);
                Field(row, sample.SolverTorqueAvailable ? sample.LeftSolverTorqueNm.y : float.NaN);
                Field(row, sample.SolverTorqueAvailable ? sample.LeftSolverTorqueNm.z : float.NaN);
                Field(row, sample.SolverTorqueAvailable ? sample.RightSolverTorqueNm.x : float.NaN);
                Field(row, sample.SolverTorqueAvailable ? sample.RightSolverTorqueNm.y : float.NaN);
                Field(row, sample.SolverTorqueAvailable ? sample.RightSolverTorqueNm.z : float.NaN);
                csv.AppendLine(row.ToString());
            }

            WriteNewFile(Path.Combine(run.OutputDirectory, run.Arm.ToLowerInvariant() + "-trace.csv"), csv.ToString());
        }

        private static void WriteTopThree(ProbeRun run)
        {
            var csv = new StringBuilder(1536);
            csv.AppendLine(
                "arm,tick,rank,joint_id,channel,target_component_x_rad,target_component_y_rad,target_component_z_rad," +
                "actual_component_x_rad,actual_component_y_rad,actual_component_z_rad," +
                "position_error_torque_x_nm,position_error_torque_y_nm,position_error_torque_z_nm," +
                "velocity_error_torque_x_nm,velocity_error_torque_y_nm,velocity_error_torque_z_nm," +
                "maximum_force_nm,normalized_channel_demand,solver_torque_diagnostic_class," +
                "solver_torque_x_nm,solver_torque_y_nm,solver_torque_z_nm");
            for (int index = 0; index < run.Tick1TopThree.Count; index++)
            {
                DriveChannelPressure pressure = run.Tick1TopThree[index];
                var row = new StringBuilder(512);
                Field(row, run.Arm);
                Field(row, "1");
                Field(row, (index + 1).ToString(Invariant));
                Field(row, pressure.JointId);
                Field(row, pressure.Channel);
                Field(row, pressure.TargetComponent.x);
                Field(row, pressure.TargetComponent.y);
                Field(row, pressure.TargetComponent.z);
                Field(row, pressure.ActualComponent.x);
                Field(row, pressure.ActualComponent.y);
                Field(row, pressure.ActualComponent.z);
                Field(row, pressure.PositionTorqueNm.x);
                Field(row, pressure.PositionTorqueNm.y);
                Field(row, pressure.PositionTorqueNm.z);
                Field(row, pressure.VelocityTorqueNm.x);
                Field(row, pressure.VelocityTorqueNm.y);
                Field(row, pressure.VelocityTorqueNm.z);
                Field(row, pressure.MaximumForceNm);
                Field(row, pressure.NormalizedDemand);
                Field(row, "ENGINE_SOLVER_DIAGNOSTIC_NOT_DRIVE_TORQUE");
                Field(row, pressure.SolverTorqueNm.x);
                Field(row, pressure.SolverTorqueNm.y);
                Field(row, pressure.SolverTorqueNm.z);
                csv.AppendLine(row.ToString());
            }

            WriteNewFile(Path.Combine(run.OutputDirectory, run.Arm.ToLowerInvariant() + "-tick1-top3-drive-channels.csv"), csv.ToString());
        }

        private static void WriteSummary(
            string outputDirectory,
            PlantSummary summary,
            ProbeRun plus,
            ProbeRun minus)
        {
            var text = new StringBuilder(2048);
            text.AppendLine("# GAM-13 V2-3C AP plant-sign probe");
            text.AppendLine();
            text.AppendLine("- Unity: `6000.3.22f1`");
            text.AppendLine("- Substrate: cleaned unloaded `SquatPhysicalPrototype`; each arm loaded a fresh scene, reset to tick 0, and recaptured the canonical standing COM reference.");
            text.AppendLine("- Probe: AP feedback contribution disabled at ankle, hip, and trunk target allocations; ML feedback remained enabled; both ankle logical AP targets received only the listed ±1° residual.");
            text.AppendLine("- Measurement window: post-physics ticks 1–5 at 100 Hz (0.05 s), declared before the run.");
            text.AppendLine("- COP source: Unity contact-impulse estimate. Tick 0 has no simulated contact impulse and is recorded as unavailable; geometric plantar registration is recorded separately.");
            text.AppendLine("- Demand source: command-side modeled drive-channel pressure; it is not measured drive torque. Solver torque is labeled separately as an engine solver diagnostic.");
            text.AppendLine();
            text.AppendLine("## Paired measurement");
            text.AppendLine();
            text.AppendLine($"- θ+ = `{N(summary.ThetaPlusRad)}` rad; θ− = `{N(summary.ThetaMinusRad)}` rad.");
            text.AppendLine($"- Mean COP_AP (+1°) = `{N(summary.PlusCopMeanM)}` m.");
            text.AppendLine($"- Mean COP_AP (−1°) = `{N(summary.MinusCopMeanM)}` m.");
            text.AppendLine($"- COP_AP (+) − COP_AP (−) = `{N(summary.CopDifferenceM)}` m.");
            text.AppendLine($"- G_ap = ΔCOP_AP / Δθ = `{N(summary.GapMPerRad)}` m/rad.");
            text.AppendLine($"- Paired-midpoint responses: +1° `{N(summary.PlusCenteredResponseM)}` m; −1° `{N(summary.MinusCenteredResponseM)}` m. This midpoint is derived from the two requested arms; no zero-target arm was added.");
            text.AppendLine($"- Paired COP difference standard error = `{N(summary.PairedCopDifferenceStandardErrorM)}` m; signal/scatter = `{N(summary.SignalToScatterRatio)}`.");
            text.AppendLine($"- First post-physics COM_AP acceleration: +1° `{N(summary.PlusEarlyComAccelerationMps2)}` m/s² ({Direction(summary.PlusEarlyComAccelerationMps2)}); −1° `{N(summary.MinusEarlyComAccelerationMps2)}` m/s² ({Direction(summary.MinusEarlyComAccelerationMps2)}).");
            text.AppendLine($"- Maximum COM_AP displacement in window = `{N(summary.MaximumWindowComDriftM)}` m.");
            text.AppendLine($"- Bilateral foot contact persisted for every post-physics sample: `{summary.BilateralContactEverySample}`.");
            text.AppendLine($"- Target realization within 0.1°: `{summary.TargetRealizationMatches}`.");
            text.AppendLine($"- Clear magnitude (≥2 mm pair separation and ≥3× paired standard error): `{summary.ClearSignal}`.");
            text.AppendLine($"- Opposite-signed paired-midpoint responses: `{summary.OppositeCenteredResponses}`.");
            text.AppendLine();
            text.AppendLine($"## Classification: `{summary.Classification}`");
            text.AppendLine();
            text.AppendLine("Classification requires complete COP samples, persistent bilateral contact, target realization, the predeclared early window, opposite-signed centered responses, and the clear-signal threshold. This classification identifies the measured local target-to-COP derivative; it does not claim drive torque from modeled demand.");
            text.AppendLine();
            text.AppendLine("## Tick-1 top-three active drive channels");
            text.AppendLine();
            AppendTopThreeMarkdown(text, plus);
            AppendTopThreeMarkdown(text, minus);
            text.AppendLine("Full channel components and contributions are in the paired `*-tick1-top3-drive-channels.csv` files. `position_error_torque_*` and `velocity_error_torque_*` are modeled terms; `solver_torque_*` is a separate engine solver diagnostic.");
            WriteNewFile(Path.Combine(outputDirectory, "plant-sign-summary.md"), text.ToString());
        }

        private static void AppendTopThreeMarkdown(StringBuilder text, ProbeRun run)
        {
            text.AppendLine(run.Arm + ":");
            foreach (DriveChannelPressure pressure in run.Tick1TopThree)
            {
                text.AppendLine(string.Format(
                    Invariant,
                    "- `{0}` `{1}`: normalized demand `{2:R}`, position torque contribution `{3:R}/{4:R}/{5:R}` Nm, velocity torque contribution `{6:R}/{7:R}/{8:R}` Nm, maximumForce `{9:R}` Nm.",
                    pressure.JointId,
                    pressure.Channel,
                    pressure.NormalizedDemand,
                    pressure.PositionTorqueNm.x,
                    pressure.PositionTorqueNm.y,
                    pressure.PositionTorqueNm.z,
                    pressure.VelocityTorqueNm.x,
                    pressure.VelocityTorqueNm.y,
                    pressure.VelocityTorqueNm.z,
                    pressure.MaximumForceNm));
            }
            text.AppendLine();
        }

        private static void WriteNewFile(string path, string contents)
        {
            if (File.Exists(path))
                throw new IOException("Refusing to overwrite existing evidence: " + path);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, contents, new UTF8Encoding(false));
        }

        private static void Field(StringBuilder row, string value)
        {
            if (row.Length > 0)
                row.Append(',');
            row.Append(value ?? string.Empty);
        }

        private static void Field(StringBuilder row, float value) =>
            Field(row, N(value));

        private static string N(float value) =>
            float.IsFinite(value) ? value.ToString("R", Invariant) : "NA";

        private static string Bool(bool value) => value ? "true" : "false";

        private static string Direction(float value)
        {
            if (!float.IsFinite(value) || Mathf.Abs(value) < 0.000001f)
                return "near-zero";
            return value > 0f ? "forward" : "backward";
        }

        private sealed class ProbeRun
        {
            public ProbeRun(string arm, float requestedResidualRad, string outputDirectory)
            {
                Arm = arm;
                RequestedResidualRad = requestedResidualRad;
                OutputDirectory = outputDirectory;
            }

            public string Arm { get; }
            public float RequestedResidualRad { get; }
            public string OutputDirectory { get; }
            public List<ProbeSample> Samples { get; } = new List<ProbeSample>(MeasurementWindowEndTick + 1);
            public List<DriveChannelPressure> Tick1TopThree { get; set; } = new List<DriveChannelPressure>(3);
        }

        private sealed class JointBaseline
        {
            public JointBaseline(
                Quaternion target,
                Vector3 targetVelocity,
                float effort,
                float strengthScale,
                JointDrive twistDrive,
                JointDrive swingDrive,
                float maximumForceNm)
            {
                Target = target;
                TargetVelocity = targetVelocity;
                Effort = effort;
                StrengthScale = strengthScale;
                TwistDrive = twistDrive;
                SwingDrive = swingDrive;
                MaximumForceNm = maximumForceNm;
            }

            public Quaternion Target { get; }
            public Vector3 TargetVelocity { get; }
            public float Effort { get; }
            public float StrengthScale { get; }
            public JointDrive TwistDrive { get; }
            public JointDrive SwingDrive { get; }
            public float MaximumForceNm { get; }
        }

        private sealed class ProbeSample
        {
            public int Tick;
            public string SampleKind;
            public float RequestedResidualRad;
            public float LeftTargetResidualRad;
            public float RightTargetResidualRad;
            public float LeftActualTwistRad;
            public float RightActualTwistRad;
            public float CopApM;
            public float SupportCenterApM;
            public float ComApM;
            public float ComVelocityApMps;
            public float ComAccelerationApMps2;
            public bool ContactSampleAvailable;
            public bool LeftFootContact;
            public bool RightFootContact;
            public bool LeftGeometricContact;
            public bool RightGeometricContact;
            public float LeftTargetActualErrorRad;
            public float RightTargetActualErrorRad;
            public float LeftTwistDemandNm;
            public float RightTwistDemandNm;
            public float LeftTwistDemandFraction;
            public float RightTwistDemandFraction;
            public float LeftSwingDemandNm;
            public float RightSwingDemandNm;
            public float LeftSwingDemandFraction;
            public float RightSwingDemandFraction;
            public float TrunkPitchRad;
            public bool SolverTorqueAvailable;
            public Vector3 LeftSolverTorqueNm;
            public Vector3 RightSolverTorqueNm;
        }

        private sealed class DriveChannelPressure
        {
            public DriveChannelPressure(
                string jointId,
                string channel,
                Vector3 targetComponent,
                Vector3 actualComponent,
                Vector3 positionTorqueNm,
                Vector3 velocityTorqueNm,
                float maximumForceNm,
                float normalizedDemand,
                Vector3 solverTorqueNm)
            {
                JointId = jointId;
                Channel = channel;
                TargetComponent = targetComponent;
                ActualComponent = actualComponent;
                PositionTorqueNm = positionTorqueNm;
                VelocityTorqueNm = velocityTorqueNm;
                MaximumForceNm = maximumForceNm;
                NormalizedDemand = normalizedDemand;
                SolverTorqueNm = solverTorqueNm;
            }

            public string JointId { get; }
            public string Channel { get; }
            public Vector3 TargetComponent { get; }
            public Vector3 ActualComponent { get; }
            public Vector3 PositionTorqueNm { get; }
            public Vector3 VelocityTorqueNm { get; }
            public float MaximumForceNm { get; }
            public float NormalizedDemand { get; }
            public Vector3 SolverTorqueNm { get; }
        }

        private sealed class PlantSummary
        {
            public float PlusCopMeanM;
            public float MinusCopMeanM;
            public float CopDifferenceM;
            public float ThetaPlusRad;
            public float ThetaMinusRad;
            public float GapMPerRad;
            public float PairMidpointCopM;
            public float PlusCenteredResponseM;
            public float MinusCenteredResponseM;
            public float PairedCopDifferenceStandardErrorM;
            public float SignalToScatterRatio;
            public float PlusEarlyComAccelerationMps2;
            public float MinusEarlyComAccelerationMps2;
            public float MaximumWindowComDriftM;
            public bool BilateralContactEverySample;
            public bool WindowCopComplete;
            public bool TargetRealizationMatches;
            public bool MeasurementWindowIsEarly;
            public bool OppositeCenteredResponses;
            public bool ClearSignal;
            public string Classification;
        }
    }
}
