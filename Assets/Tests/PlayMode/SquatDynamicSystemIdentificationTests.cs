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
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H10. The setup pose, the collision model and the upper-limb
    /// projection are all qualified, and the athlete stands. The dynamic squat
    /// still fails. This fixture repairs nothing: it measures every candidate
    /// physical cause on one tick grid and reports which crosses its threshold
    /// first, so the repair that follows is aimed at a measured first cause
    /// instead of the most visible symptom.
    ///
    /// Every channel is sampled after the tick that produced it. The balance
    /// observer and the powered-joint diagnostics are written by the
    /// pre-physics step, so their value at sample time is the state the solver
    /// was given for that tick; the rigid-body channels are the state the
    /// solver produced. That half-tick offset is smaller than any event
    /// separation this fixture is asked to resolve, and it is identical for
    /// every load.
    /// </summary>
    public sealed class SquatDynamicSystemIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

        private const int SettleTicks = 60;
        private const int SquatTicks = 400;

        // Event thresholds. Each is a limit the plant itself declares, or a
        // margin wide enough that crossing it is not noise.
        private const float SaddleLinearLimitM = SquatBarSaddle.DefaultLinearLimitM;
        private const float SaddleBrokenSeparationM = SquatBarSaddle.MaxPlausibleSeparationM;
        private const float SaddleAngularXLimitDeg = 20f;
        private const float SaddleAngularYZLimitDeg = 15f;
        private const float SaddleForceFractionOfBreak = 0.5f;
        private const float SaddleTorqueFractionOfBreak = 0.5f;
        private const float SlipSpeedThresholdMps = 0.05f;
        private const float DriveSaturationThreshold = 1.0f;
        private const float PostureErrorThresholdRad = 0.35f;
        private const float TrunkPitchThresholdDeg = 45f;
        private const float AngularVelocityBlowupRadS = 20f;
        private const float TrackingErrorThresholdDeg = 20f;
        private const float LimitProximityThreshold = 0.98f;

        /// <summary>
        /// The load-bearing chain, parent first. Every one of these is
        /// decomposed per tick into what the reference asked for, what the
        /// control law actually commanded, and what the joint did, because
        /// the difference between those three is the whole classification.
        /// </summary>
        private static readonly string[] DecomposedJoints =
        {
            "left_foot", "right_foot",
            "left_shank", "right_shank",
            "left_thigh", "right_thigh",
            "abdomen", "thorax"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
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
            _bootstrap.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
                _controller.enabled = false;
            if (_rig != null)
                _rig.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// The load sweep is the discriminator. Zero kilograms removes the bar
        /// body and the saddle entirely, isolating the athlete, its reference
        /// and its balance law. Twenty-five and one hundred and five add the
        /// coupling and scale it. A cause present at zero cannot be the
        /// coupling; a cause absent at zero and present at both loaded weights
        /// is the coupling, or something the coupling drives.
        /// </summary>
        [UnityTest]
        public IEnumerator S1_DYNAMIC_SQUAT_FIRST_CAUSE_LOAD_SWEEP()
        {
            var runs = new List<IdentificationRun>();
            yield return RunIdentification(0f, "0kg", runs);
            yield return RunIdentification(25f, "25kg", runs);
            yield return RunIdentification(105f, "105kg", runs);

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H10 DYNAMIC SQUAT FIRST-CAUSE IDENTIFICATION");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "settle_ticks={0} squat_ticks={1} tick_seconds={2:F4} athlete_mass_kg={3:F2}",
                SettleTicks, SquatTicks, SimulationConstants.FixedDeltaTimeSeconds, _rig.TotalMassKg));
            report.AppendLine();

            foreach (IdentificationRun run in runs)
            {
                report.AppendLine("--- LOAD " + run.Label + " ---");
                report.AppendLine(run.Summary);
                report.AppendLine("ordered event timeline (tick after StartSquat, never = threshold not crossed):");
                foreach (EventRecord e in run.OrderedEvents())
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,6}  {1,-36} {2}",
                        e.Tick < 0 ? "never" : e.Tick.ToString(CultureInfo.InvariantCulture),
                        e.Name,
                        e.Detail));
                }
                report.AppendLine();
            }

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-first-cause-report.txt"),
                report.ToString());
            Debug.Log(report.ToString());

            // This fixture is an instrument, not a gate. It fails only when it
            // could not measure.
            foreach (IdentificationRun run in runs)
            {
                Assert.That(run.SampleCount, Is.EqualTo(SquatTicks),
                    "Run " + run.Label + " did not complete its tick budget.");
            }
            yield return null;
        }

        /// <summary>
        /// S1 shows the hip stalling around seventy-six degrees of flexion
        /// while its own drive commands a hundred and eleven, with the knee in
        /// the same chain tracking to a hundred and twenty-one. Three things
        /// can hold a joint short of a target its actuator is not saturated
        /// against: a limit constraint, a collision, or a drive that is
        /// commanded but not realised. This reads all three off the running
        /// plant, unloaded, so the answer does not depend on the bar.
        /// </summary>
        [UnityTest]
        public IEnumerator S2_HIP_FLEXION_BLOCKER_DISCRIMINATION()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H10 HIP FLEXION BLOCKER DISCRIMINATION (unloaded)");
            report.AppendLine();
            report.AppendLine("--- as-built joint configuration, read from the running plant ---");
            foreach (string jointId in DecomposedJoints)
                report.AppendLine(DescribeJoint(powered, jointId));
            report.AppendLine();

            report.AppendLine("--- self-collision state for the hip's neighbours ---");
            report.AppendLine(DescribePair("left_thigh", "pelvis"));
            report.AppendLine(DescribePair("left_thigh", "abdomen"));
            report.AppendLine(DescribePair("right_thigh", "pelvis"));
            report.AppendLine(DescribePair("right_thigh", "abdomen"));
            report.AppendLine(DescribePair("left_thigh", "right_thigh"));
            report.AppendLine(DescribePair("left_thigh", "left_shank"));
            report.AppendLine();

            ContactCensus census = ContactCensus.Attach(_rig, new[]
            {
                "left_thigh", "right_thigh", "pelvis", "abdomen", "left_shank", "right_shank"
            });

            for (int i = 0; i < SettleTicks; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);
            }

            var blockerTrace = new StringBuilder();
            blockerTrace.AppendLine(
                "tick,hip_actual_x_deg,hip_target_x_deg,hip_track_err_deg,hip_limit_prox,hip_demand," +
                "hip_solver_torque_nm,knee_actual_x_deg,knee_target_x_deg," +
                "thigh_self_contact_impulse,thigh_self_contact_partner,thigh_contact_count");

            adapter.StartSquat();
            for (int tick = 0; tick < SquatTicks; tick++)
            {
                ContactCensus.Tick = tick;
                census.BeginTick();
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);

                PoweredJointController.PoweredJointRuntime hip = powered.GetJoint("right_thigh");
                PoweredJointController.PoweredJointRuntime knee = powered.GetJoint("right_shank");
                if (hip == null || knee == null)
                    continue;

                census.CurrentTickPeak("right_thigh", out float peakImpulse, out string peakPartner, out int contactCount);

                blockerTrace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F2},{3:F2},{4:F3},{5:F3},{6:F1},{7:F2},{8:F2},{9:F3},{10},{11}",
                    tick,
                    SignedTwistDegrees(hip.Diagnostic.ActualRelative, Vector3.right),
                    SignedTwistDegrees(hip.AppliedTarget, Vector3.right),
                    Quaternion.Angle(hip.AppliedTarget, hip.Diagnostic.ActualRelative),
                    hip.Diagnostic.LimitProximity,
                    hip.Diagnostic.ModeledDemand,
                    hip.Diagnostic.SolverTorqueJointSpaceNm.magnitude,
                    SignedTwistDegrees(knee.Diagnostic.ActualRelative, Vector3.right),
                    SignedTwistDegrees(knee.AppliedTarget, Vector3.right),
                    peakImpulse, peakPartner, contactCount));
            }

            report.AppendLine("--- contact census over the descent, ranked by total normal impulse ---");
            foreach (string line in census.Ranked(12))
                report.AppendLine("  " + line);

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-hip-blocker-trace.csv"),
                blockerTrace.ToString());
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-hip-blocker-census.csv"),
                census.ToCsv());
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-hip-blocker-report.txt"),
                report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private string DescribeJoint(PoweredJointController powered, string jointId)
        {
            PoweredJointController.PoweredJointRuntime runtimeJoint = powered.GetJoint(jointId);
            if (runtimeJoint == null || runtimeJoint.Joint == null)
                return jointId + ": absent";
            ConfigurableJoint joint = runtimeJoint.Joint;
            return string.Format(CultureInfo.InvariantCulture,
                "{0,-13} recipe[{1:F0},{2:F0}] secondary={3:F0} | asBuilt angX={4} low={5:F1} high={6:F1} " +
                "angY={7} limY={8:F1} angZ={9} limZ={10:F1} | drive spring={11:F0} damper={12:F0} maxForce={13:F0} " +
                "mode={14} axis={15} secondaryAxis={16}",
                jointId,
                runtimeJoint.Recipe.LowDegrees, runtimeJoint.Recipe.HighDegrees, runtimeJoint.Recipe.SecondaryLimitDegrees,
                joint.angularXMotion, joint.lowAngularXLimit.limit, joint.highAngularXLimit.limit,
                joint.angularYMotion, joint.angularYLimit.limit,
                joint.angularZMotion, joint.angularZLimit.limit,
                joint.angularXDrive.positionSpring, joint.angularXDrive.positionDamper, joint.angularXDrive.maximumForce,
                joint.rotationDriveMode, joint.axis, joint.secondaryAxis);
        }

        private string DescribePair(string segmentA, string segmentB)
        {
            if (!_rig.Segments.TryGetValue(segmentA, out PhysicalAthleteRig.SegmentRuntime a) ||
                !_rig.Segments.TryGetValue(segmentB, out PhysicalAthleteRig.SegmentRuntime b) ||
                a.Collider == null || b.Collider == null)
                return segmentA + " <-> " + segmentB + ": absent";

            bool policyIgnored = PhysicalAthleteSelfCollisionPolicy.IsPairIgnoredByPolicy(segmentA, segmentB, out string reason);
            return string.Format(CultureInfo.InvariantCulture,
                "{0,-13} <-> {1,-10} colliders=({2},{3}) policyIgnored={4} reason={5}",
                segmentA, segmentB, a.Collider.GetType().Name, b.Collider.GetType().Name,
                policyIgnored, reason ?? "NONE");
        }

        /// <summary>
        /// The counterfactual. S2 shows the thigh proxy resting on the abdomen
        /// proxy from tick one hundred and forty-six, and the hip falling
        /// behind from tick one hundred and eighty. If that contact is the
        /// first cause and not a passenger, suppressing that one collider pair
        /// and changing nothing else has to let the hip reach its commanded
        /// flexion. Whatever fails next is the phase after this one.
        /// </summary>
        [UnityTest]
        public IEnumerator S3_THIGH_ABDOMEN_SUPPRESSION_COUNTERFACTUAL()
        {
            var runs = new List<IdentificationRun>();
            yield return RunIdentification(0f, "0kg-baseline", runs, false);
            yield return RunIdentification(0f, "0kg-thigh-abdomen-suppressed", runs, true);

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H10 THIGH-ABDOMEN SUPPRESSION COUNTERFACTUAL");
            report.AppendLine("Identical unloaded descent. The only difference is Physics.IgnoreCollision");
            report.AppendLine("on left_thigh/abdomen and right_thigh/abdomen. Nothing is tuned.");
            report.AppendLine();
            foreach (IdentificationRun run in runs)
            {
                report.AppendLine("--- " + run.Label + " ---");
                report.AppendLine(run.Summary);
                foreach (EventRecord e in run.OrderedEvents())
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,6}  {1,-36} {2}",
                        e.Tick < 0 ? "never" : e.Tick.ToString(CultureInfo.InvariantCulture),
                        e.Name, e.Detail));
                }
                report.AppendLine();
            }

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-counterfactual-report.txt"),
                report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private void SetThighAbdomenCollision(bool enabled)
        {
            if (!_rig.Segments.TryGetValue("abdomen", out PhysicalAthleteRig.SegmentRuntime abdomen) ||
                abdomen.Collider == null)
                return;
            foreach (string thighId in new[] { "left_thigh", "right_thigh" })
            {
                if (_rig.Segments.TryGetValue(thighId, out PhysicalAthleteRig.SegmentRuntime thigh) &&
                    thigh.Collider != null)
                {
                    Physics.IgnoreCollision(thigh.Collider, abdomen.Collider, !enabled);
                }
            }
        }

        private IEnumerator RunIdentification(
            float loadKg,
            string label,
            List<IdentificationRun> runs,
            bool suppressThighAbdomen = false)
        {
            _controller.SetLoad(loadKg);
            SetThighAbdomenCollision(!suppressThighAbdomen);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            SquatBalanceObserver balance = adapter.Balance;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            SquatBarSaddle saddle = _controller.Saddle;
            Rigidbody thorax = _rig.Segments["thorax"].Body;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            Rigidbody bar = saddle != null && saddle.Barbell != null ? saddle.Barbell.Body : null;

            // The joint zero is the relative pose it was created at, which is
            // the spawn pose SetLoad has just restored. Deviation is measured
            // from there, so it is comparable with the authored limits.
            Quaternion saddleZero = bar != null
                ? Quaternion.Inverse(thorax.rotation) * bar.rotation
                : Quaternion.identity;
            Vector3 barLocalZero = bar != null
                ? thorax.transform.InverseTransformPoint(bar.position)
                : Vector3.zero;

            var jointTrace = new StringBuilder();
            jointTrace.AppendLine(
                "tick,joint,actual_x_deg,applied_target_x_deg,nominal_x_deg," +
                "tracking_err_deg,command_offset_deg,reference_dev_deg," +
                "limit_proximity,demand,solver_torque_nm,max_force_nm");

            for (int i = 0; i < SettleTicks; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);
                if (i % 5 == 0)
                    AppendJointDecomposition(jointTrace, adapter, powered, i - SettleTicks);
            }

            var run = new IdentificationRun(label, loadKg);
            run.SettlePelvisY = pelvis.position.y;
            run.SettleComAp = balance.SystemCom.z;
            run.SettleTrunkPitchDeg = TrunkInclinationDegrees(pelvis, thorax);
            run.SettleSaddleSeparationM = saddle != null ? saddle.SaddleSeparationMeters : 0f;

            var trace = new StringBuilder();
            trace.AppendLine(
                "tick,sq,state,failure_reason," +
                "com_ap,com_vel_ap,capture_ap,sup_ap_min,sup_ap_max,cop_ap,has_cop,contacts,slip_mps," +
                "pelvis_y,trunk_pitch_deg," +
                "sad_attached,sad_sep_m,sad_force_n,sad_torque_nm,sad_dev_x_deg,sad_dev_y_deg,sad_dev_z_deg," +
                "bar_d_ap_m,bar_d_up_m,bar_d_lat_m," +
                "max_demand,max_demand_joint,max_torque_ratio,max_torque_joint," +
                "chain_demand,chain_demand_joint,chain_track_err_deg,chain_track_joint," +
                "chain_limit_prox,chain_limit_joint,chain_cmd_offset_deg,chain_cmd_joint," +
                "posture_err_rad,posture_worst_joint,max_omega,max_speed");

            adapter.StartSquat();

            for (int tick = 0; tick < SquatTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFootDetectors(dt);
                run.SampleCount++;

                // --- coupling channel -------------------------------------
                bool saddleAttached = saddle != null && saddle.IsAttached;
                float sadSep = saddle != null ? saddle.SaddleSeparationMeters : 0f;
                float sadForce = 0f;
                float sadTorque = 0f;
                Vector3 sadDevDeg = Vector3.zero;
                Vector3 barDelta = Vector3.zero;
                if (saddle != null && bar != null)
                {
                    if (saddleAttached && saddle.Joint != null)
                    {
                        sadForce = saddle.Joint.currentForce.magnitude;
                        sadTorque = saddle.Joint.currentTorque.magnitude;
                    }
                    Quaternion relative = Quaternion.Inverse(thorax.rotation) * bar.rotation;
                    sadDevDeg = SignedEulerDeviationDegrees(saddleZero, relative);
                    barDelta = thorax.transform.InverseTransformPoint(bar.position) - barLocalZero;
                }

                // --- support channel --------------------------------------
                float slip = Mathf.Max(
                    _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f,
                    _controller.RightFootContact != null ? _controller.RightFootContact.SlipSpeed : 0f);
                float copAp = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;

                // --- actuator channel -------------------------------------
                // The whole-body maximum is dominated by the upper limb, which
                // carries no load. The load-bearing chain is tracked separately
                // so a hand cannot mask a hip.
                float maxDemand = 0f;
                string maxDemandJoint = "NONE";
                float maxTorqueRatio = 0f;
                string maxTorqueJoint = "NONE";
                float chainMaxDemand = 0f;
                string chainMaxDemandJoint = "NONE";
                float chainMaxTracking = 0f;
                string chainMaxTrackingJoint = "NONE";
                float chainMaxProximity = 0f;
                string chainMaxProximityJoint = "NONE";
                float chainMaxCommandOffset = 0f;
                string chainMaxCommandOffsetJoint = "NONE";
                foreach (PoweredJointController.PoweredJointRuntime joint in powered.Joints)
                {
                    if (!joint.Profile.HasValue)
                        continue;
                    PoweredJointDiagnostic diagnostic = joint.Diagnostic;
                    if (diagnostic.ModeledDemand > maxDemand)
                    {
                        maxDemand = diagnostic.ModeledDemand;
                        maxDemandJoint = joint.Id;
                    }
                    float ratio = diagnostic.SolverTorqueJointSpaceNm.magnitude /
                        Mathf.Max(diagnostic.MaximumForceNm, 0.001f);
                    if (ratio > maxTorqueRatio)
                    {
                        maxTorqueRatio = ratio;
                        maxTorqueJoint = joint.Id;
                    }

                    if (Array.IndexOf(DecomposedJoints, joint.Id) < 0)
                        continue;
                    if (diagnostic.ModeledDemand > chainMaxDemand)
                    {
                        chainMaxDemand = diagnostic.ModeledDemand;
                        chainMaxDemandJoint = joint.Id;
                    }
                    float tracking = Quaternion.Angle(joint.AppliedTarget, diagnostic.ActualRelative);
                    if (tracking > chainMaxTracking)
                    {
                        chainMaxTracking = tracking;
                        chainMaxTrackingJoint = joint.Id;
                    }
                    if (diagnostic.LimitProximity > chainMaxProximity)
                    {
                        chainMaxProximity = diagnostic.LimitProximity;
                        chainMaxProximityJoint = joint.Id;
                    }
                    if (adapter.TryGetTargetComposition(joint.Id, out SquatPhysicalAdapter.JointTargetComposition composition))
                    {
                        float offset = Quaternion.Angle(composition.Nominal, composition.Final);
                        if (offset > chainMaxCommandOffset)
                        {
                            chainMaxCommandOffset = offset;
                            chainMaxCommandOffsetJoint = joint.Id;
                        }
                    }
                }

                if (tick % 2 == 0)
                    AppendJointDecomposition(jointTrace, adapter, powered, tick);

                // --- numerics channel -------------------------------------
                float maxOmega = 0f;
                float maxSpeed = 0f;
                bool nonFinite = false;
                foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> segment in _rig.Segments)
                {
                    Rigidbody body = segment.Value.Body;
                    if (body == null)
                        continue;
                    maxOmega = Mathf.Max(maxOmega, body.angularVelocity.magnitude);
                    maxSpeed = Mathf.Max(maxSpeed, body.linearVelocity.magnitude);
                    if (!IsFinite(body.position) || !IsFinite(body.linearVelocity) || !IsFinite(body.angularVelocity))
                        nonFinite = true;
                }

                float trunkPitch = TrunkInclinationDegrees(pelvis, thorax);
                float pelvisY = pelvis.position.y;

                // --- event predicates -------------------------------------
                float capturedSep = sadSep;
                float capturedForce = sadForce;
                float capturedTorque = sadTorque;
                Vector3 capturedDev = sadDevDeg;
                float capturedSlip = slip;
                float capturedCop = copAp;
                float capturedDemand = maxDemand;
                string capturedDemandJoint = maxDemandJoint;
                float capturedOmega = maxOmega;
                float capturedTrunk = trunkPitch;
                float supportMin = balance.SupportApMin;
                float supportMax = balance.SupportApMax;
                float comAp = balance.SystemCom.z;
                float captureAp = balance.CaptureAp;
                float postureErr = adapter.CanonicalPostureErrorRad;
                string postureJoint = adapter.CanonicalPostureWorstJoint;
                string failureReason = adapter.FailureReason;

                run.Mark("SADDLE_JOINT_DESTROYED", tick, saddle != null && !saddleAttached,
                    () => "the saddle joint no longer exists");
                run.Mark("SADDLE_SEPARATION_BEYOND_LINEAR_LIMIT", tick,
                    saddle != null && capturedSep > SaddleLinearLimitM,
                    () => string.Format(CultureInfo.InvariantCulture, "sep={0:F4} m > {1:F3} m",
                        capturedSep, SaddleLinearLimitM));
                run.Mark("SADDLE_SEPARATION_IMPLAUSIBLE", tick,
                    saddle != null && capturedSep > SaddleBrokenSeparationM,
                    () => string.Format(CultureInfo.InvariantCulture, "sep={0:F4} m > {1:F3} m",
                        capturedSep, SaddleBrokenSeparationM));
                run.Mark("SADDLE_ANGULAR_X_BEYOND_LIMIT", tick,
                    saddle != null && Mathf.Abs(capturedDev.x) > SaddleAngularXLimitDeg,
                    () => string.Format(CultureInfo.InvariantCulture, "x={0:F1} deg > {1:F0} deg",
                        capturedDev.x, SaddleAngularXLimitDeg));
                run.Mark("SADDLE_ANGULAR_Y_BEYOND_LIMIT", tick,
                    saddle != null && Mathf.Abs(capturedDev.y) > SaddleAngularYZLimitDeg,
                    () => string.Format(CultureInfo.InvariantCulture, "y={0:F1} deg > {1:F0} deg",
                        capturedDev.y, SaddleAngularYZLimitDeg));
                run.Mark("SADDLE_ANGULAR_Z_BEYOND_LIMIT", tick,
                    saddle != null && Mathf.Abs(capturedDev.z) > SaddleAngularYZLimitDeg,
                    () => string.Format(CultureInfo.InvariantCulture, "z={0:F1} deg > {1:F0} deg",
                        capturedDev.z, SaddleAngularYZLimitDeg));
                run.Mark("SADDLE_FORCE_HALF_OF_BREAK", tick,
                    capturedForce > SquatBarSaddle.DefaultBreakForce * SaddleForceFractionOfBreak,
                    () => string.Format(CultureInfo.InvariantCulture, "F={0:F0} N of {1:F0} N break",
                        capturedForce, SquatBarSaddle.DefaultBreakForce));
                run.Mark("SADDLE_TORQUE_HALF_OF_BREAK", tick,
                    capturedTorque > SquatBarSaddle.DefaultBreakTorque * SaddleTorqueFractionOfBreak,
                    () => string.Format(CultureInfo.InvariantCulture, "T={0:F0} Nm of {1:F0} Nm break",
                        capturedTorque, SquatBarSaddle.DefaultBreakTorque));

                run.Mark("PLANTAR_CONTACT_LOST", tick, !balance.HasSupport,
                    () => "no plantar contact this tick");
                run.Mark("FOOT_SLIP", tick, capturedSlip > SlipSpeedThresholdMps,
                    () => string.Format(CultureInfo.InvariantCulture, "slip={0:F3} m/s", capturedSlip));
                run.Mark("COP_OUTSIDE_SUPPORT", tick,
                    balance.HasCopEstimate && (copAp < supportMin || copAp > supportMax),
                    () => string.Format(CultureInfo.InvariantCulture, "cop_ap={0:F4} outside [{1:F4},{2:F4}]",
                        capturedCop, supportMin, supportMax));
                run.Mark("CAPTURE_OUTSIDE_SUPPORT", tick,
                    balance.HasSupport && (captureAp < supportMin || captureAp > supportMax),
                    () => string.Format(CultureInfo.InvariantCulture, "capture_ap={0:F4} outside [{1:F4},{2:F4}]",
                        captureAp, supportMin, supportMax));
                run.Mark("COM_OUTSIDE_SUPPORT", tick,
                    balance.HasSupport && (comAp < supportMin || comAp > supportMax),
                    () => string.Format(CultureInfo.InvariantCulture, "com_ap={0:F4} outside [{1:F4},{2:F4}]",
                        comAp, supportMin, supportMax));

                float capturedChainDemand = chainMaxDemand;
                string capturedChainDemandJoint = chainMaxDemandJoint;
                float capturedChainTracking = chainMaxTracking;
                string capturedChainTrackingJoint = chainMaxTrackingJoint;
                float capturedChainProximity = chainMaxProximity;
                string capturedChainProximityJoint = chainMaxProximityJoint;
                float capturedChainOffset = chainMaxCommandOffset;
                string capturedChainOffsetJoint = chainMaxCommandOffsetJoint;

                run.Mark("DRIVE_SATURATED", tick, capturedDemand >= DriveSaturationThreshold,
                    () => string.Format(CultureInfo.InvariantCulture, "demand={0:F2} at {1}",
                        capturedDemand, capturedDemandJoint));
                run.Mark("LOAD_CHAIN_DRIVE_SATURATED", tick, capturedChainDemand >= DriveSaturationThreshold,
                    () => string.Format(CultureInfo.InvariantCulture, "demand={0:F2} at {1}",
                        capturedChainDemand, capturedChainDemandJoint));
                run.Mark("LOAD_CHAIN_TRACKING_DIVERGENCE", tick, capturedChainTracking > TrackingErrorThresholdDeg,
                    () => string.Format(CultureInfo.InvariantCulture, "track_err={0:F1} deg at {1}",
                        capturedChainTracking, capturedChainTrackingJoint));
                run.Mark("LOAD_CHAIN_AT_ANGULAR_LIMIT", tick, capturedChainProximity >= LimitProximityThreshold,
                    () => string.Format(CultureInfo.InvariantCulture, "proximity={0:F3} at {1}",
                        capturedChainProximity, capturedChainProximityJoint));
                run.Mark("CONTROL_LAW_LEFT_REFERENCE", tick, capturedChainOffset > TrackingErrorThresholdDeg,
                    () => string.Format(CultureInfo.InvariantCulture, "command_offset={0:F1} deg at {1}",
                        capturedChainOffset, capturedChainOffsetJoint));
                run.Mark("POSTURE_TRACKING_BREAKDOWN", tick, postureErr > PostureErrorThresholdRad,
                    () => string.Format(CultureInfo.InvariantCulture, "err={0:F3} rad at {1}",
                        postureErr, postureJoint));
                run.Mark("TRUNK_PITCH_DIVERGENCE", tick,
                    Mathf.Abs(capturedTrunk - run.SettleTrunkPitchDeg) > TrunkPitchThresholdDeg,
                    () => string.Format(CultureInfo.InvariantCulture, "trunk={0:F1} deg, settle {1:F1} deg",
                        capturedTrunk, run.SettleTrunkPitchDeg));
                run.Mark("ANGULAR_VELOCITY_BLOWUP", tick, capturedOmega > AngularVelocityBlowupRadS,
                    () => string.Format(CultureInfo.InvariantCulture, "omega={0:F1} rad/s", capturedOmega));
                run.Mark("NON_FINITE_STATE", tick, nonFinite, () => "a body state is not finite");
                run.Mark("ADAPTER_DECLARED_FAILURE", tick, failureReason != "NONE", () => failureReason);

                run.MaxDemandSeen = Mathf.Max(run.MaxDemandSeen, maxDemand);
                run.MaxSaddleForceSeen = Mathf.Max(run.MaxSaddleForceSeen, sadForce);
                run.MaxSaddleTorqueSeen = Mathf.Max(run.MaxSaddleTorqueSeen, sadTorque);
                run.MaxSaddleSeparationSeen = Mathf.Max(run.MaxSaddleSeparationSeen, sadSep);
                run.FinalPelvisY = pelvisY;
                run.FinalSq = adapter.Sq;

                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2},{3},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10},{11},{12:F4}," +
                    "{13:F4},{14:F2},{15},{16:F4},{17:F1},{18:F1},{19:F2},{20:F2},{21:F2},{22:F4},{23:F4},{24:F4}," +
                    "{25:F3},{26},{27:F3},{28},{29:F3},{30},{31:F2},{32},{33:F3},{34},{35:F2},{36}," +
                    "{37:F4},{38},{39:F2},{40:F2}",
                    tick, adapter.Sq, adapter.State, failureReason,
                    comAp, balance.SystemComVelocity.z, captureAp,
                    supportMin, supportMax, copAp, balance.HasCopEstimate ? 1 : 0,
                    balance.SupportContactCount, slip,
                    pelvisY, trunkPitch,
                    saddleAttached ? 1 : 0, sadSep, sadForce, sadTorque,
                    sadDevDeg.x, sadDevDeg.y, sadDevDeg.z,
                    barDelta.z, barDelta.y, barDelta.x,
                    maxDemand, maxDemandJoint, maxTorqueRatio, maxTorqueJoint,
                    chainMaxDemand, chainMaxDemandJoint,
                    chainMaxTracking, chainMaxTrackingJoint,
                    chainMaxProximity, chainMaxProximityJoint,
                    chainMaxCommandOffset, chainMaxCommandOffsetJoint,
                    postureErr, postureJoint,
                    maxOmega, maxSpeed));
            }

            run.Summary = string.Format(CultureInfo.InvariantCulture,
                "load={0:F0}kg settlePelvisY={1:F4} finalPelvisY={2:F4} finalSq={3:F2} settleTrunk={4:F1}deg " +
                "maxDemand={5:F2} maxSaddleF={6:F0}N maxSaddleT={7:F0}Nm maxSaddleSep={8:F4}m settleSaddleSep={9:F4}m",
                loadKg, run.SettlePelvisY, run.FinalPelvisY, run.FinalSq, run.SettleTrunkPitchDeg,
                run.MaxDemandSeen, run.MaxSaddleForceSeen, run.MaxSaddleTorqueSeen,
                run.MaxSaddleSeparationSeen, run.SettleSaddleSeparationM);

            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-squat-trace-" + label + ".csv"),
                trace.ToString());
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-5h10-joint-decomposition-" + label + ".csv"),
                jointTrace.ToString());

            runs.Add(run);
            yield return null;
        }

        private void TickFootDetectors(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        /// <summary>
        /// Trunk lean measured from world geometry rather than from a body
        /// frame, so it carries no unknown build-time offset: the angle of the
        /// pelvis-to-thorax vector away from vertical, positive anterior.
        /// </summary>
        private static float TrunkInclinationDegrees(Rigidbody pelvis, Rigidbody thorax)
        {
            Vector3 axis = thorax.position - pelvis.position;
            if (axis.sqrMagnitude < 1e-8f)
                return 0f;
            return Mathf.Atan2(axis.z, axis.y) * Mathf.Rad2Deg;
        }

        private static void AppendJointDecomposition(
            StringBuilder builder,
            SquatPhysicalAdapter adapter,
            PoweredJointController powered,
            int tick)
        {
            for (int index = 0; index < DecomposedJoints.Length; index++)
            {
                string jointId = DecomposedJoints[index];
                PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
                if (joint == null || !joint.Profile.HasValue)
                    continue;

                PoweredJointDiagnostic diagnostic = joint.Diagnostic;
                Quaternion nominal = Quaternion.identity;
                if (adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition))
                    nominal = composition.Nominal;

                builder.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2:F2},{3:F2},{4:F2},{5:F2},{6:F2},{7:F2},{8:F3},{9:F3},{10:F1},{11:F1}",
                    tick,
                    jointId,
                    SignedTwistDegrees(diagnostic.ActualRelative, Vector3.right),
                    SignedTwistDegrees(joint.AppliedTarget, Vector3.right),
                    SignedTwistDegrees(nominal, Vector3.right),
                    Quaternion.Angle(joint.AppliedTarget, diagnostic.ActualRelative),
                    adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition c2)
                        ? Quaternion.Angle(c2.Nominal, c2.Final)
                        : 0f,
                    Quaternion.Angle(nominal, diagnostic.ActualRelative),
                    diagnostic.LimitProximity,
                    diagnostic.ModeledDemand,
                    diagnostic.SolverTorqueJointSpaceNm.magnitude,
                    diagnostic.MaximumForceNm));
            }
        }

        /// <summary>
        /// The same swing-twist decomposition the powered controller uses to
        /// report limit proximity, so the angles here and there mean the same
        /// thing.
        /// </summary>
        private static float SignedTwistDegrees(Quaternion rotation, Vector3 axis)
        {
            var vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, axis);
            var twist = new Quaternion(projection.x, projection.y, projection.z, rotation.w);
            float magnitude = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            if (magnitude < 1e-6f)
                return 0f;
            twist = new Quaternion(twist.x / magnitude, twist.y / magnitude, twist.z / magnitude, twist.w / magnitude);
            twist.ToAngleAxis(out float angle, out Vector3 twistAxis);
            if (angle > 180f)
                angle -= 360f;
            return angle * Mathf.Sign(Vector3.Dot(twistAxis, axis));
        }

        private static Vector3 SignedEulerDeviationDegrees(Quaternion zero, Quaternion current)
        {
            Quaternion delta = Quaternion.Inverse(zero) * current;
            Vector3 euler = delta.eulerAngles;
            return new Vector3(Wrap180(euler.x), Wrap180(euler.y), Wrap180(euler.z));
        }

        private static float Wrap180(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
                degrees -= 360f;
            if (degrees < -180f)
                degrees += 360f;
            return degrees;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        /// <summary>
        /// Records athlete-to-athlete contact only. Ground contact is expected
        /// and is measured elsewhere; what this fixture needs to know is
        /// whether the athlete is blocking itself.
        /// </summary>
        private sealed class ContactCensus
        {
            public static int Tick;

            private readonly Dictionary<string, Record> _records = new Dictionary<string, Record>();
            private readonly Dictionary<string, TickPeak> _tickPeaks = new Dictionary<string, TickPeak>();

            private sealed class Record
            {
                public string A;
                public string B;
                public int FirstTick = -1;
                public int LastTick;
                public int TickCount;
                public float MaxImpulse;
                public float TotalImpulse;
                public Vector3 Point;
                public Vector3 Normal;
            }

            private struct TickPeak
            {
                public float Impulse;
                public string Partner;
                public int Count;
            }

            public static ContactCensus Attach(PhysicalAthleteRig rig, IEnumerable<string> segmentIds)
            {
                var census = new ContactCensus();
                var names = new Dictionary<Rigidbody, string>();
                foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> entry in rig.Segments)
                {
                    if (entry.Value.Body != null)
                        names[entry.Value.Body] = entry.Key;
                }

                foreach (string id in segmentIds)
                {
                    if (!rig.Segments.TryGetValue(id, out PhysicalAthleteRig.SegmentRuntime segment) || segment.Body == null)
                        continue;
                    ContactProbe probe = segment.Body.gameObject.AddComponent<ContactProbe>();
                    probe.Initialise(census, id, names);
                }
                return census;
            }

            public void BeginTick() => _tickPeaks.Clear();

            public void CurrentTickPeak(string segmentId, out float impulse, out string partner, out int count)
            {
                if (_tickPeaks.TryGetValue(segmentId, out TickPeak peak))
                {
                    impulse = peak.Impulse;
                    partner = peak.Partner;
                    count = peak.Count;
                    return;
                }
                impulse = 0f;
                partner = "NONE";
                count = 0;
            }

            public void Report(string a, string b, float impulse, Vector3 point, Vector3 normal)
            {
                string key = string.CompareOrdinal(a, b) <= 0 ? a + "|" + b : b + "|" + a;
                if (!_records.TryGetValue(key, out Record record))
                {
                    record = new Record { A = a, B = b, FirstTick = Tick };
                    _records[key] = record;
                }
                record.LastTick = Tick;
                record.TickCount++;
                record.TotalImpulse += impulse;
                if (impulse >= record.MaxImpulse)
                {
                    record.MaxImpulse = impulse;
                    record.Point = point;
                    record.Normal = normal;
                }

                _tickPeaks.TryGetValue(a, out TickPeak peak);
                peak.Count++;
                if (impulse >= peak.Impulse)
                {
                    peak.Impulse = impulse;
                    peak.Partner = b;
                }
                _tickPeaks[a] = peak;
            }

            public IEnumerable<string> Ranked(int count)
            {
                var ordered = new List<Record>(_records.Values);
                ordered.Sort((x, y) => y.TotalImpulse.CompareTo(x.TotalImpulse));
                for (int index = 0; index < ordered.Count && index < count; index++)
                {
                    Record record = ordered[index];
                    yield return string.Format(CultureInfo.InvariantCulture,
                        "{0} <-> {1}: ticks={2} maxImpulse={3:F2} totalImpulse={4:F1} first={5} last={6} normal={7}",
                        record.A, record.B, record.TickCount, record.MaxImpulse, record.TotalImpulse,
                        record.FirstTick, record.LastTick, record.Normal);
                }
            }

            public string ToCsv()
            {
                var csv = new StringBuilder();
                csv.AppendLine("body_a,body_b,first_tick,last_tick,tick_count," +
                               "max_normal_impulse,total_normal_impulse,point_x,point_y,point_z," +
                               "normal_x,normal_y,normal_z");
                var ordered = new List<Record>(_records.Values);
                ordered.Sort((x, y) => y.TotalImpulse.CompareTo(x.TotalImpulse));
                foreach (Record record in ordered)
                {
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3},{4},{5:F3},{6:F3},{7:F4},{8:F4},{9:F4},{10:F3},{11:F3},{12:F3}",
                        record.A, record.B, record.FirstTick, record.LastTick, record.TickCount,
                        record.MaxImpulse, record.TotalImpulse,
                        record.Point.x, record.Point.y, record.Point.z,
                        record.Normal.x, record.Normal.y, record.Normal.z));
                }
                return csv.ToString();
            }
        }

        private sealed class ContactProbe : MonoBehaviour
        {
            private ContactCensus _census;
            private string _id;
            private Dictionary<Rigidbody, string> _names;

            public void Initialise(ContactCensus census, string id, Dictionary<Rigidbody, string> names)
            {
                _census = census;
                _id = id;
                _names = names;
            }

            private void OnCollisionStay(Collision collision) => Record(collision);

            private void OnCollisionEnter(Collision collision) => Record(collision);

            private void Record(Collision collision)
            {
                if (_census == null || collision.contactCount == 0)
                    return;
                // Only athlete-to-athlete contact. A null rigidbody is the
                // platform, which every foot is supposed to be standing on.
                if (collision.rigidbody == null || !_names.TryGetValue(collision.rigidbody, out string other))
                    return;
                ContactPoint contact = collision.GetContact(0);
                _census.Report(_id, other, collision.impulse.magnitude, contact.point, contact.normal);
            }
        }

        private readonly struct EventRecord
        {
            public EventRecord(string name, int tick, string detail)
            {
                Name = name;
                Tick = tick;
                Detail = detail;
            }

            public string Name { get; }
            public int Tick { get; }
            public string Detail { get; }
        }

        private sealed class IdentificationRun
        {
            private readonly List<string> _order = new List<string>();
            private readonly Dictionary<string, EventRecord> _events = new Dictionary<string, EventRecord>();

            public IdentificationRun(string label, float loadKg)
            {
                Label = label;
                LoadKg = loadKg;
            }

            public string Label { get; }
            public float LoadKg { get; }
            public int SampleCount { get; set; }
            public float SettlePelvisY { get; set; }
            public float SettleComAp { get; set; }
            public float SettleTrunkPitchDeg { get; set; }
            public float SettleSaddleSeparationM { get; set; }
            public float FinalPelvisY { get; set; }
            public float FinalSq { get; set; }
            public float MaxDemandSeen { get; set; }
            public float MaxSaddleForceSeen { get; set; }
            public float MaxSaddleTorqueSeen { get; set; }
            public float MaxSaddleSeparationSeen { get; set; }
            public string Summary { get; set; } = string.Empty;

            public void Mark(string name, int tick, bool crossed, Func<string> detail)
            {
                if (!_events.ContainsKey(name))
                {
                    _order.Add(name);
                    _events[name] = new EventRecord(name, -1, string.Empty);
                }
                if (!crossed || _events[name].Tick >= 0)
                    return;
                _events[name] = new EventRecord(name, tick, detail());
            }

            public IEnumerable<EventRecord> OrderedEvents()
            {
                var list = new List<EventRecord>();
                foreach (string name in _order)
                    list.Add(_events[name]);
                list.Sort((a, b) =>
                {
                    int ak = a.Tick < 0 ? int.MaxValue : a.Tick;
                    int bk = b.Tick < 0 ? int.MaxValue : b.Tick;
                    int c = ak.CompareTo(bk);
                    return c != 0 ? c : string.CompareOrdinal(a.Name, b.Name);
                });
                return list;
            }
        }
    }
}
