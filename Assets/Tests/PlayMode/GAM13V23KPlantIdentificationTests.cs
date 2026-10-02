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
    public sealed class GAM13V23KPlantIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const float ProbeLoadKg = 140f;
        private const float HeldDescentPhase = 0.04f;
        private const int StandingTicks = 50;
        private const int DescentHoldTicks = 5;
        private const int ResponseWindowTicks = 10;
        private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
        private static readonly string[] Channels = { "ankle", "hip", "trunk" };

        [UnityTest]
        public IEnumerator GAM13_V23K_PAIRED_SAGITTAL_PLANT_IDENTIFICATION()
        {
            Assert.That(Application.unityVersion, Is.EqualTo("6000.3.22f1"));
            string outputDirectory = Environment.GetEnvironmentVariable("GAM13_V23K_OUTPUT_DIRECTORY");
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                outputDirectory = Path.Combine(
                    Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-13",
                    "v2-3k-plant-identification", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", Invariant));
            }
            outputDirectory = Path.GetFullPath(outputDirectory);
            Directory.CreateDirectory(outputDirectory);

            var runs = new List<ProbeRun>(Channels.Length * 2);
            foreach (string channel in Channels)
            {
                yield return RunArm(channel, +1f, outputDirectory, run => runs.Add(run));
                yield return RunArm(channel, -1f, outputDirectory, run => runs.Add(run));
            }

            string tracePath = Path.Combine(outputDirectory, "paired-plant-trace.csv");
            string summaryPath = Path.Combine(outputDirectory, "paired-plant-summary.csv");
            WriteTrace(tracePath, runs);
            List<PlantSummary> summaries = Summarize(runs);
            FixedAllocation allocation = DeriveFixedAllocation(summaries);
            WriteSummary(summaryPath, summaries, allocation);
            WriteReceipt(Path.Combine(outputDirectory, "runner-receipt.md"), tracePath, summaryPath, summaries, allocation);
            foreach (PlantSummary summary in summaries)
            {
                Debug.Log(string.Format(
                    Invariant,
                    "GAM13_V23K channel={0} G_com_z={1:R}m/rad delta_com={2:R}m delta_bar_z={3:R}m delta_cop_ap={4:R}m direction={5} headroom={6:R}rad",
                    summary.Channel, summary.GComZMPerRad, summary.DeltaComZM, summary.DeltaBarZM,
                    summary.DeltaCopApM, summary.RestoringTargetSign, summary.SymmetricHeadroomRad));
            }
        }

        private static IEnumerator RunArm(string channel, float sign, string outputDirectory, Action<ProbeRun> completed)
        {
            string arm = sign > 0f ? "PLUS_1_DEG" : "MINUS_1_DEG";
            float residualRad = sign * Mathf.Deg2Rad;
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
            controller.SetLoad(ProbeLoadKg);

            FoundationRuntime runtime = bootstrap.Runtime;
            SquatPhysicalAdapter adapter = controller.Adapter;
            Assert.That(runtime.CurrentTime.Tick, Is.EqualTo(0ul), "Every arm must start from a fresh reset.");
            Assert.That(adapter.BalanceCorrectionsEnabled, Is.True);
            Assert.That(adapter.ApFeedbackContributionEnabled, Is.True,
                "The normal V2 AP balance feedback must remain active during identification.");

            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            for (int tick = 0; tick < StandingTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                Step(runtime, controller, dt);
            }
            for (int tick = 0; tick < DescentHoldTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(
                    HeldDescentPhase, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);
                Step(runtime, controller, dt);
            }

            SquatObservationSnapshot startSnapshot = controller.ObservationCollector.LastSnapshot;
            Assert.That(startSnapshot.Support.SupportAvailability, Is.EqualTo(SquatTelemetryAvailability.AVAILABLE));
            Assert.That(startSnapshot.Support.HasSupport, Is.True, "Probe phase must retain plantar support.");
            Assert.That(startSnapshot.Support.ComToSupportApRearMarginM, Is.GreaterThanOrEqualTo(0f),
                "The shallow descent probe must begin before COM reaches the rear support boundary.");

            string[] jointIds = JointIds(channel);
            var run = new ProbeRun(channel, arm, residualRad, HeldDescentPhase);
            foreach (string jointId in jointIds)
                run.StartActualTwistRad.Add(jointId, CurrentActualTwist(rig.PoweredController.GetJoint(jointId)));

            SetResidual(adapter, channel, residualRad);
            adapter.HoldReferencePhaseForQualification(
                HeldDescentPhase, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);
            rig.PrimeCommandSource();
            run.Samples.Add(CaptureSample(0, run, controller, rig));
            AssertAppliedResiduals(run.Samples[0], channel, residualRad);

            for (int tick = 1; tick <= ResponseWindowTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(
                    HeldDescentPhase, SquatPhaseDirection.Descent, SquatState.DESCENT, 0f);
                Step(runtime, controller, dt);
                ProbeSample sample = CaptureSample(tick, run, controller, rig);
                ProbeSample previous = run.Samples[run.Samples.Count - 1];
                sample.ComAccelerationZMps2 = (sample.ComVelocityZMps - previous.ComVelocityZMps) / dt;
                AssertAppliedResiduals(sample, channel, residualRad);
                run.Samples.Add(sample);
                if (tick % 5 == 0)
                    yield return null;
            }

            Assert.That(run.Samples.Count, Is.EqualTo(ResponseWindowTicks + 1));
            completed(run);
            yield return null;
        }

        private static void Step(FoundationRuntime runtime, SquatPhysicalPrototypeController controller, float dt)
        {
            Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            controller.LeftFootContact?.PhysicsTickUpdate(dt);
            controller.RightFootContact?.PhysicsTickUpdate(dt);
            Assert.That(controller.ObservationCollector.HasLastSnapshot, Is.True);
        }

        private static ProbeSample CaptureSample(
            int windowTick,
            ProbeRun run,
            SquatPhysicalPrototypeController controller,
            PhysicalAthleteRig rig)
        {
            SquatObservationSnapshot snapshot = controller.ObservationCollector.LastSnapshot;
            SquatSupportObservation support = snapshot.Support;
            SquatPhysicalAdapter adapter = controller.Adapter;
            PoweredJointController powered = rig.PoweredController;
            float comZ = support.SystemComWorldMeters.Z;
            float comVelocityZ = support.SystemComVelocityWorldMetersPerSecond.Z;
            bool hasCop = support.EngineContactPointAvailability == SquatTelemetryAvailability.AVAILABLE;
            float copZ = hasCop ? support.EngineContactPointWorldMeters.Z : float.NaN;
            var sample = new ProbeSample
            {
                WindowTick = windowTick,
                SimulationTick = (int)snapshot.SimulationTick,
                ComZM = comZ,
                ComVelocityZMps = comVelocityZ,
                ComAccelerationZMps2 = float.NaN,
                BarZM = snapshot.Bar.PositionWorldMeters.Z,
                BarVelocityZMps = snapshot.Bar.LinearVelocityWorldMetersPerSecond.Z,
                CopApM = copZ,
                FrontMarginM = support.ComToSupportApFrontMarginM,
                RearMarginM = support.ComToSupportApRearMarginM,
                CopAvailable = hasCop,
                MaximumAllJointAnchorSeparationM = MaximumAnchorSeparation(powered),
                SaddleSeparationM = controller.Saddle != null ? controller.Saddle.SaddleSeparationMeters : float.NaN
            };

            foreach (string jointId in JointIds(run.Channel))
            {
                PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
                PoweredJointDiagnostic diagnostic = joint.HasPostPhysicsDiagnostic
                    ? joint.PostPhysicsDiagnostic
                    : joint.Diagnostic;
                adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition);
                float appliedResidual = AppliedLogicalResidual(adapter, run.Channel, composition);
                float actualTwist = PoweredJointController.SignedTwistRadians(diagnostic.ActualRelative, Vector3.right);
                JointSample jointSample = new JointSample(
                    jointId,
                    appliedResidual,
                    actualTwist,
                    Mathf.DeltaAngle(run.StartActualTwistRad[jointId] * Mathf.Rad2Deg,
                        actualTwist * Mathf.Rad2Deg) * Mathf.Deg2Rad,
                    diagnostic,
                    HeadroomPositive(joint, actualTwist),
                    HeadroomNegative(joint, actualTwist),
                    AnchorSeparation(joint));
                sample.Joints.Add(jointSample);
            }
            return sample;
        }

        private static float AppliedLogicalResidual(
            SquatPhysicalAdapter adapter,
            string channel,
            SquatPhysicalAdapter.JointTargetComposition composition)
        {
            SquatBalanceCorrectionV2 correction = adapter.BalanceCorrectionV2;
            float baseSagittal = channel == "ankle" ? correction.AnkleApRad :
                channel == "hip" ? correction.HipApRad : correction.TrunkApRad;
            float baseFrontal = channel == "ankle" ? correction.AnkleMlRad :
                channel == "hip" ? correction.HipMlRad : 0f;
            Quaternion withoutProbe = composition.Nominal * composition.GravityBias *
                SagittalAndFrontal(baseSagittal, baseFrontal);
            Quaternion delta = PoweredJointController.NormalizeCanonical(
                Quaternion.Inverse(withoutProbe) * composition.Final);
            return PoweredJointController.SignedTwistRadians(delta, Vector3.right);
        }

        private static void AssertAppliedResiduals(ProbeSample sample, string channel, float expected)
        {
            Assert.That(sample.Joints.Count, Is.EqualTo(2));
            foreach (JointSample joint in sample.Joints)
                Assert.That(joint.AppliedLogicalResidualRad, Is.EqualTo(expected).Within(0.0001f), joint.JointId);
        }

        private static float CurrentActualTwist(PoweredJointController.PoweredJointRuntime joint)
        {
            PoweredJointDiagnostic diagnostic = joint.HasPostPhysicsDiagnostic
                ? joint.PostPhysicsDiagnostic
                : joint.Diagnostic;
            return PoweredJointController.SignedTwistRadians(diagnostic.ActualRelative, Vector3.right);
        }

        private static float HeadroomPositive(PoweredJointController.PoweredJointRuntime joint, float actualRad) =>
            joint.Joint.highAngularXLimit.limit * Mathf.Deg2Rad - actualRad;

        private static float HeadroomNegative(PoweredJointController.PoweredJointRuntime joint, float actualRad) =>
            actualRad - joint.Joint.lowAngularXLimit.limit * Mathf.Deg2Rad;

        private static float AnchorSeparation(PoweredJointController.PoweredJointRuntime joint)
        {
            Vector3 anchor = joint.Joint.transform.TransformPoint(joint.Joint.anchor);
            Vector3 connectedAnchor = joint.Joint.connectedBody != null
                ? joint.Joint.connectedBody.transform.TransformPoint(joint.Joint.connectedAnchor)
                : joint.Joint.connectedAnchor;
            return Vector3.Distance(anchor, connectedAnchor);
        }

        private static float MaximumAnchorSeparation(PoweredJointController powered)
        {
            float maximum = 0f;
            foreach (PoweredJointController.PoweredJointRuntime joint in powered.Joints)
                maximum = Mathf.Max(maximum, AnchorSeparation(joint));
            return maximum;
        }

        private static string[] JointIds(string channel)
        {
            switch (channel)
            {
                case "ankle": return new[] { "left_foot", "right_foot" };
                case "hip": return new[] { "left_thigh", "right_thigh" };
                case "trunk": return new[] { "abdomen", "thorax" };
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        private static void SetResidual(SquatPhysicalAdapter adapter, string channel, float residualRad)
        {
            adapter.AnkleSagittalOffsetAdditiveRad = 0f;
            adapter.HipSagittalOffsetAdditiveRad = 0f;
            adapter.TrunkSagittalOffsetAdditiveRad = 0f;
            switch (channel)
            {
                case "ankle": adapter.AnkleSagittalOffsetAdditiveRad = residualRad; break;
                case "hip": adapter.HipSagittalOffsetAdditiveRad = residualRad; break;
                case "trunk": adapter.TrunkSagittalOffsetAdditiveRad = residualRad; break;
                default: throw new ArgumentOutOfRangeException(nameof(channel));
            }
        }

        private static Quaternion SagittalAndFrontal(float sagittalRad, float frontalRad)
        {
            Quaternion sagittal = Quaternion.AngleAxis(sagittalRad * Mathf.Rad2Deg, Vector3.right);
            Quaternion frontal = Quaternion.AngleAxis(frontalRad * Mathf.Rad2Deg, Vector3.forward);
            return sagittal * frontal;
        }

        private static List<PlantSummary> Summarize(List<ProbeRun> runs)
        {
            var summaries = new List<PlantSummary>(Channels.Length);
            foreach (string channel in Channels)
            {
                ProbeRun plus = FindRun(runs, channel, "PLUS_1_DEG");
                ProbeRun minus = FindRun(runs, channel, "MINUS_1_DEG");
                float deltaCom = MeanPairedResponse(plus, minus, sample => sample.ComZM);
                float deltaComVelocity = MeanPairedResponse(plus, minus, sample => sample.ComVelocityZMps);
                float deltaComAcceleration = MeanPairedDifference(plus, minus, sample => sample.ComAccelerationZMps2);
                float deltaBar = MeanPairedResponse(plus, minus, sample => sample.BarZM);
                float deltaCop = MeanPairedResponse(plus, minus, sample => sample.CopApM);
                float deltaTarget = plus.ResidualRad - minus.ResidualRad;
                float effectiveness = deltaCom / deltaTarget;
                float headroom = SymmetricHeadroom(plus.Samples[0], minus.Samples[0]);
                summaries.Add(new PlantSummary(
                    channel, deltaTarget, deltaCom, deltaComVelocity, deltaComAcceleration,
                    deltaBar, deltaCop, effectiveness, headroom,
                    effectiveness > 0f ? "POSITIVE_TARGET" : effectiveness < 0f ? "NEGATIVE_TARGET" : "UNRESOLVED"));
            }
            return summaries;
        }

        private static float MeanPairedResponse(ProbeRun plus, ProbeRun minus, Func<ProbeSample, float> value)
        {
            float total = 0f;
            int count = 0;
            for (int tick = 1; tick <= ResponseWindowTicks; tick++)
            {
                float plusStart = value(plus.Samples[0]);
                float minusStart = value(minus.Samples[0]);
                float plusNow = value(plus.Samples[tick]);
                float minusNow = value(minus.Samples[tick]);
                if (!float.IsFinite(plusStart) || !float.IsFinite(minusStart) ||
                    !float.IsFinite(plusNow) || !float.IsFinite(minusNow))
                    continue;
                total += (plusNow - plusStart) - (minusNow - minusStart);
                count++;
            }
            return count > 0 ? total / count : float.NaN;
        }

        private static float MeanPairedDifference(ProbeRun plus, ProbeRun minus, Func<ProbeSample, float> value)
        {
            float total = 0f;
            int count = 0;
            for (int tick = 1; tick <= ResponseWindowTicks; tick++)
            {
                float plusNow = value(plus.Samples[tick]);
                float minusNow = value(minus.Samples[tick]);
                if (!float.IsFinite(plusNow) || !float.IsFinite(minusNow))
                    continue;
                total += plusNow - minusNow;
                count++;
            }
            return count > 0 ? total / count : float.NaN;
        }

        private static FixedAllocation DeriveFixedAllocation(List<PlantSummary> summaries)
        {
            PlantSummary ankle = FindSummary(summaries, "ankle");
            PlantSummary hip = FindSummary(summaries, "hip");
            PlantSummary trunk = FindSummary(summaries, "trunk");
            float ankleHeadroom = Mathf.Min(ankle.SymmetricHeadroomRad, SquatComStabilizerV2Calibration.Default.MaxApCorrectionRad);
            float denominator = Mathf.Abs(ankle.GComZMPerRad) * ankleHeadroom +
                Mathf.Abs(hip.GComZMPerRad) * hip.SymmetricHeadroomRad +
                Mathf.Abs(trunk.GComZMPerRad) * trunk.SymmetricHeadroomRad;
            if (!float.IsFinite(denominator) || denominator <= 0f ||
                Mathf.Abs(ankle.GComZMPerRad) <= 1e-8f ||
                Mathf.Abs(hip.GComZMPerRad) <= 1e-8f ||
                Mathf.Abs(trunk.GComZMPerRad) <= 1e-8f)
                throw new InvalidOperationException("A measured sagittal effectiveness or headroom is unusable.");

            float maxApRad = SquatComStabilizerV2Calibration.Default.MaxApCorrectionRad;
            float referenceComEffect = Mathf.Abs(ankle.GComZMPerRad) * ankleHeadroom;
            float scale = referenceComEffect / (maxApRad * denominator);
            var allocation = new FixedAllocation(
                -Mathf.Sign(ankle.GComZMPerRad) * scale * ankleHeadroom,
                -Mathf.Sign(hip.GComZMPerRad) * scale * hip.SymmetricHeadroomRad,
                Mathf.Sign(trunk.GComZMPerRad) * scale * trunk.SymmetricHeadroomRad,
                denominator,
                referenceComEffect / maxApRad,
                Mathf.Abs(ankle.GComZMPerRad * (-Mathf.Sign(ankle.GComZMPerRad) * scale * ankleHeadroom)) +
                Mathf.Abs(hip.GComZMPerRad * (-Mathf.Sign(hip.GComZMPerRad) * scale * hip.SymmetricHeadroomRad)) +
                Mathf.Abs(trunk.GComZMPerRad * (-Mathf.Sign(trunk.GComZMPerRad) * scale * trunk.SymmetricHeadroomRad)));

            Assert.That(Mathf.Abs(allocation.AnkleApWeight) * maxApRad,
                Is.LessThanOrEqualTo(ankle.SymmetricHeadroomRad + 1e-5f));
            Assert.That(Mathf.Abs(allocation.HipApWeight) * maxApRad,
                Is.LessThanOrEqualTo(hip.SymmetricHeadroomRad + 1e-5f));
            Assert.That(Mathf.Abs(allocation.TrunkApCounterWeight) * maxApRad,
                Is.LessThanOrEqualTo(trunk.SymmetricHeadroomRad + 1e-5f));
            Assert.That(Mathf.Abs(allocation.AnkleApWeight), Is.LessThanOrEqualTo(1f));
            return allocation;
        }

        private static PlantSummary FindSummary(List<PlantSummary> summaries, string channel)
        {
            foreach (PlantSummary summary in summaries)
                if (summary.Channel == channel)
                    return summary;
            throw new InvalidOperationException("Missing plant summary: " + channel);
        }

        private static float SymmetricHeadroom(ProbeSample plus, ProbeSample minus)
        {
            float minimum = float.PositiveInfinity;
            foreach (JointSample joint in plus.Joints)
                minimum = Mathf.Min(minimum, joint.PositiveHeadroomRad, joint.NegativeHeadroomRad);
            foreach (JointSample joint in minus.Joints)
                minimum = Mathf.Min(minimum, joint.PositiveHeadroomRad, joint.NegativeHeadroomRad);
            return float.IsPositiveInfinity(minimum) ? float.NaN : minimum;
        }

        private static ProbeRun FindRun(List<ProbeRun> runs, string channel, string arm)
        {
            foreach (ProbeRun run in runs)
                if (run.Channel == channel && run.Arm == arm)
                    return run;
            throw new InvalidOperationException("Missing paired probe arm: " + channel + " " + arm);
        }

        private static void WriteTrace(string path, List<ProbeRun> runs)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "channel,arm,residual_rad,phase,window_tick,simulation_tick,load_kg,system_com_z_m,com_velocity_z_mps,com_acceleration_z_mps2," +
                "bar_z_m,bar_velocity_z_mps,cop_ap_m,cop_available,front_support_margin_m,rear_support_margin_m," +
                "joint_id,applied_logical_residual_rad,actual_joint_angle_rad,actual_joint_delta_rad,active_channel," +
                "active_channel_demand_nm,active_channel_demand_fraction,modeled_joint_demand,limit_proximity," +
                "positive_limit_headroom_rad,negative_limit_headroom_rad,probe_direction_headroom_rad," +
                "joint_anchor_separation_m,max_joint_anchor_separation_m,saddle_separation_m");
            foreach (ProbeRun run in runs)
            {
                foreach (ProbeSample sample in run.Samples)
                {
                    foreach (JointSample joint in sample.Joints)
                    {
                        float twistFraction = joint.Diagnostic.TwistDriveDemandFraction;
                        float swingFraction = joint.Diagnostic.SwingDriveDemandFraction;
                        bool twistActive = twistFraction >= swingFraction;
                        csv.Append(run.Channel).Append(',').Append(run.Arm).Append(',');
                        Value(csv, run.ResidualRad); Value(csv, run.Phase); Value(csv, sample.WindowTick);
                        Value(csv, sample.SimulationTick); Value(csv, ProbeLoadKg);
                        Value(csv, sample.ComZM); Value(csv, sample.ComVelocityZMps); Value(csv, sample.ComAccelerationZMps2);
                        Value(csv, sample.BarZM); Value(csv, sample.BarVelocityZMps); Value(csv, sample.CopApM);
                        Value(csv, sample.CopAvailable); Value(csv, sample.FrontMarginM); Value(csv, sample.RearMarginM);
                        csv.Append(joint.JointId).Append(',');
                        Value(csv, joint.AppliedLogicalResidualRad); Value(csv, joint.ActualAngleRad); Value(csv, joint.ActualDeltaRad);
                        csv.Append(twistActive ? "twist_x" : "swing_yz").Append(',');
                        Value(csv, twistActive ? joint.Diagnostic.TwistDriveDemandNm : joint.Diagnostic.SwingDriveDemandNm);
                        Value(csv, twistActive ? twistFraction : swingFraction); Value(csv, joint.Diagnostic.ModeledDemand);
                        Value(csv, joint.Diagnostic.LimitProximity); Value(csv, joint.PositiveHeadroomRad);
                        Value(csv, joint.NegativeHeadroomRad);
                        Value(csv, run.ResidualRad >= 0f ? joint.PositiveHeadroomRad : joint.NegativeHeadroomRad);
                        Value(csv, joint.AnchorSeparationM); Value(csv, sample.MaximumAllJointAnchorSeparationM);
                        Value(csv, sample.SaddleSeparationM); csv.Length--; csv.AppendLine();
                    }
                }
            }
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        }

        private static void WriteSummary(string path, List<PlantSummary> summaries, FixedAllocation allocation)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "channel,delta_target_per_joint_rad,delta_com_z_response_m,delta_com_velocity_z_mps,delta_com_acceleration_z_mps2," +
                "delta_bar_z_m,delta_cop_ap_m,G_com_z_m_per_rad,symmetric_start_headroom_rad,restoring_target_sign," +
                "derived_AnkleApWeight,derived_HipApWeight,derived_TrunkApCounterWeight");
            foreach (PlantSummary summary in summaries)
            {
                csv.Append(summary.Channel).Append(',');
                Value(csv, summary.DeltaTargetRad); Value(csv, summary.DeltaComZM); Value(csv, summary.DeltaComVelocityZMps);
                Value(csv, summary.DeltaComAccelerationZMps2); Value(csv, summary.DeltaBarZM); Value(csv, summary.DeltaCopApM);
                Value(csv, summary.GComZMPerRad); Value(csv, summary.SymmetricHeadroomRad);
                csv.Append(summary.RestoringTargetSign).Append(',');
                Value(csv, allocation.AnkleApWeight); Value(csv, allocation.HipApWeight);
                Value(csv, allocation.TrunkApCounterWeight); csv.Length--; csv.AppendLine();
            }
            File.WriteAllText(path, csv.ToString(), new UTF8Encoding(false));
        }

        private static void WriteReceipt(
            string path,
            string tracePath,
            string summaryPath,
            List<PlantSummary> summaries,
            FixedAllocation allocation)
        {
            var receipt = new StringBuilder();
            receipt.AppendLine("# GAM-13 V2-3K plant identification");
            receipt.AppendLine();
            receipt.AppendLine("FRESH_RESET_PAIRED_ARMS=true");
            receipt.AppendLine("LOAD_KG=140");
            receipt.AppendLine("HELD_DESCENT_PHASE=0.04");
            receipt.AppendLine("NORMAL_V2_BALANCE_LOOP_ACTIVE=true");
            receipt.AppendLine("PRODUCTION_PHYSICS_CHANGED_DURING_IDENTIFICATION=false");
            receipt.AppendLine("RESPONSE_WINDOW_TICKS=1..10 after each arm's own tick-0 baseline");
            receipt.AppendLine("G=mean paired COM-z displacement difference / per-joint target difference");
            receipt.AppendLine("ALLOCATION_RULE=preserve the measured ankle-only COM-z response at the 15-degree input bound; split that response proportional to |G| times symmetric measured joint headroom; signs make all three channels restore rearward COM");
            receipt.AppendLine("STRENGTH_SCALE_AUTHORITY=5.13 pre-calibration qualification authority; not final calibrated strength");
            receipt.AppendLine("MAX_AP_CORRECTION_RAD=0.26180 (15 degrees)");
            receipt.AppendLine(string.Format(Invariant,
                "DERIVED_ALLOCATION ankle={0:R} hip={1:R} trunk_counter={2:R} target_max_abs_deg={3:R}/{4:R}/{5:R}",
                allocation.AnkleApWeight, allocation.HipApWeight, allocation.TrunkApCounterWeight,
                Mathf.Abs(allocation.AnkleApWeight) * 15f, Mathf.Abs(allocation.HipApWeight) * 15f,
                Mathf.Abs(allocation.TrunkApCounterWeight) * 15f));
            receipt.AppendLine(string.Format(Invariant,
                "MEASURED_AUTHORITY_REFERENCE_G={0:R}m/rad combined_gain={1:R}m/rad headroom_capacity={2:R}m",
                allocation.ReferenceGainMPerRad, allocation.CombinedGainMPerRad, allocation.MeasuredHeadroomCapacity));
            receipt.AppendLine("TRACE=" + tracePath);
            receipt.AppendLine("SUMMARY=" + summaryPath);
            receipt.AppendLine();
            receipt.AppendLine("| Channel | G COM-z (m/rad) | Bar Δz (m) | COP ΔAP (m) | Sign for forward COM response | Symmetric headroom (rad) |");
            receipt.AppendLine("|---|---:|---:|---:|---|---:|");
            foreach (PlantSummary summary in summaries)
            {
                receipt.Append('|').Append(summary.Channel).Append('|')
                    .Append(summary.GComZMPerRad.ToString("R", Invariant)).Append('|')
                    .Append(summary.DeltaBarZM.ToString("R", Invariant)).Append('|')
                    .Append(summary.DeltaCopApM.ToString("R", Invariant)).Append('|')
                    .Append(summary.RestoringTargetSign).Append('|')
                    .Append(summary.SymmetricHeadroomRad.ToString("R", Invariant)).AppendLine("|");
            }
            File.WriteAllText(path, receipt.ToString(), new UTF8Encoding(false));
        }

        private static void Value(StringBuilder csv, float value) => csv.Append(
            float.IsFinite(value) ? value.ToString("R", Invariant) : "NA").Append(',');

        private static void Value(StringBuilder csv, int value) => csv.Append(value.ToString(Invariant)).Append(',');
        private static void Value(StringBuilder csv, bool value) => csv.Append(value ? "true" : "false").Append(',');

        private sealed class ProbeRun
        {
            public ProbeRun(string channel, string arm, float residualRad, float phase)
            {
                Channel = channel;
                Arm = arm;
                ResidualRad = residualRad;
                Phase = phase;
            }

            public string Channel { get; }
            public string Arm { get; }
            public float ResidualRad { get; }
            public float Phase { get; }
            public Dictionary<string, float> StartActualTwistRad { get; } = new Dictionary<string, float>(StringComparer.Ordinal);
            public List<ProbeSample> Samples { get; } = new List<ProbeSample>(ResponseWindowTicks + 1);
        }

        private sealed class ProbeSample
        {
            public int WindowTick;
            public int SimulationTick;
            public float ComZM;
            public float ComVelocityZMps;
            public float ComAccelerationZMps2;
            public float BarZM;
            public float BarVelocityZMps;
            public float CopApM;
            public bool CopAvailable;
            public float FrontMarginM;
            public float RearMarginM;
            public float MaximumAllJointAnchorSeparationM;
            public float SaddleSeparationM;
            public List<JointSample> Joints { get; } = new List<JointSample>(2);
        }

        private sealed class JointSample
        {
            public JointSample(
                string jointId,
                float appliedLogicalResidualRad,
                float actualAngleRad,
                float actualDeltaRad,
                PoweredJointDiagnostic diagnostic,
                float positiveHeadroomRad,
                float negativeHeadroomRad,
                float anchorSeparationM)
            {
                JointId = jointId;
                AppliedLogicalResidualRad = appliedLogicalResidualRad;
                ActualAngleRad = actualAngleRad;
                ActualDeltaRad = actualDeltaRad;
                Diagnostic = diagnostic;
                PositiveHeadroomRad = positiveHeadroomRad;
                NegativeHeadroomRad = negativeHeadroomRad;
                AnchorSeparationM = anchorSeparationM;
            }

            public string JointId { get; }
            public float AppliedLogicalResidualRad { get; }
            public float ActualAngleRad { get; }
            public float ActualDeltaRad { get; }
            public PoweredJointDiagnostic Diagnostic { get; }
            public float PositiveHeadroomRad { get; }
            public float NegativeHeadroomRad { get; }
            public float AnchorSeparationM { get; }
        }

        private sealed class PlantSummary
        {
            public PlantSummary(
                string channel,
                float deltaTargetRad,
                float deltaComZM,
                float deltaComVelocityZMps,
                float deltaComAccelerationZMps2,
                float deltaBarZM,
                float deltaCopApM,
                float gComZMPerRad,
                float symmetricHeadroomRad,
                string restoringTargetSign)
            {
                Channel = channel;
                DeltaTargetRad = deltaTargetRad;
                DeltaComZM = deltaComZM;
                DeltaComVelocityZMps = deltaComVelocityZMps;
                DeltaComAccelerationZMps2 = deltaComAccelerationZMps2;
                DeltaBarZM = deltaBarZM;
                DeltaCopApM = deltaCopApM;
                GComZMPerRad = gComZMPerRad;
                SymmetricHeadroomRad = symmetricHeadroomRad;
                RestoringTargetSign = restoringTargetSign;
            }

            public string Channel { get; }
            public float DeltaTargetRad { get; }
            public float DeltaComZM { get; }
            public float DeltaComVelocityZMps { get; }
            public float DeltaComAccelerationZMps2 { get; }
            public float DeltaBarZM { get; }
            public float DeltaCopApM { get; }
            public float GComZMPerRad { get; }
            public float SymmetricHeadroomRad { get; }
            public string RestoringTargetSign { get; }
        }

        private sealed class FixedAllocation
        {
            public FixedAllocation(
                float ankleApWeight,
                float hipApWeight,
                float trunkApCounterWeight,
                float measuredHeadroomCapacity,
                float referenceGainMPerRad,
                float combinedGainMPerRad)
            {
                AnkleApWeight = ankleApWeight;
                HipApWeight = hipApWeight;
                TrunkApCounterWeight = trunkApCounterWeight;
                MeasuredHeadroomCapacity = measuredHeadroomCapacity;
                ReferenceGainMPerRad = referenceGainMPerRad;
                CombinedGainMPerRad = combinedGainMPerRad;
            }

            public float AnkleApWeight { get; }
            public float HipApWeight { get; }
            public float TrunkApCounterWeight { get; }
            public float MeasuredHeadroomCapacity { get; }
            public float ReferenceGainMPerRad { get; }
            public float CombinedGainMPerRad { get; }
        }
    }
}
