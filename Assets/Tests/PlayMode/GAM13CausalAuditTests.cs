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
    /// <summary>
    /// Deterministic GAM-13 causal audit. This is evidence tooling only: it
    /// holds the production setup pose, records each post-physics tick, and
    /// never changes targets, capacities, contact modes, or outcomes.
    /// </summary>
    public sealed class GAM13CausalAuditTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int AuditTicks = 500;
        private const int ConsecutiveTicks = 3;
        private const float AuthorityThreshold = 0.95f;
        private const float TrackingThresholdRad = 0.17453292f;
        private const float PostureTrunkThresholdRad = 0.70f;
        private const float PosturePelvisThresholdM = 0.90f;
        private const float CaptureMarginThresholdM = 0.01f;
        private const float SaddleSeparationThresholdM = 0.05f;
        private const float SaddleLimitThreshold = 0.95f;
        private const float FootSlipThresholdMps = 0.05f;
        private const string WrenchFeasibility = "NOT_OBSERVABLE";

        private static readonly float[] AuditLoadsKg = { 25f, 60f, 300f };
        private static readonly int[] VelocityProfiles = { 1, 4, 8 };
        private static readonly int[] PositionProfiles = { 16, 28, 40 };
        private static readonly string[] CausalJointIds =
        {
            "abdomen", "thorax", "head_neck",
            "left_upper_arm", "right_upper_arm", "left_forearm", "right_forearm",
            "left_hand", "right_hand",
            "left_thigh", "right_thigh", "left_shank", "right_shank",
            "left_foot", "right_foot"
        };
        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnityTest]
        [Explicit("GAM-13 causal telemetry and orthogonal velocity-iteration audit.")]
        public IEnumerator GAM13_CAUSAL_AUDIT_BASELINE_AND_VELOCITY_SENSITIVITY()
        {
            string directory = MeasurementDirectory;
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "causal-audit-thresholds.md"), ThresholdDocument());

            var summaries = new List<AuditResult>(AuditLoadsKg.Length * VelocityProfiles.Length);
            foreach (int velocityIterations in VelocityProfiles)
            {
                foreach (float loadKg in AuditLoadsKg)
                {
                    yield return LoadFreshScene();
                    summaries.Add(RunAudit(loadKg, PhysicalAthleteSolverProfile.PositionIterations, velocityIterations));
                    yield return null;
                }
            }

            WriteSummary(Path.Combine(directory, "causal-audit-velocity-summary.csv"), summaries);
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 causal position-iteration audit; run only after velocity sensitivity is reviewed.")]
        public IEnumerator GAM13_CAUSAL_AUDIT_POSITION_SENSITIVITY()
        {
            string directory = MeasurementDirectory;
            Directory.CreateDirectory(directory);
            var summaries = new List<AuditResult>(AuditLoadsKg.Length * PositionProfiles.Length);
            foreach (int positionIterations in PositionProfiles)
            {
                foreach (float loadKg in AuditLoadsKg)
                {
                    yield return LoadFreshScene();
                    summaries.Add(RunAudit(loadKg, positionIterations, PhysicalAthleteSolverProfile.VelocityIterations));
                    yield return null;
                }
            }

            WriteSummary(Path.Combine(directory, "causal-audit-position-summary.csv"), summaries);
            yield return null;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The causal audit scene is missing.");
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

            _controller.enabled = false;
            _bootstrap.enabled = false;
        }

        private AuditResult RunAudit(float loadKg, int positionIterations, int velocityIterations)
        {
            _controller.SetLoad(loadKg);
            ApplyAthleteSolverProfile(positionIterations, velocityIterations);
            Assert.That(_controller.Saddle, Is.Not.Null, "Loaded causal audit requires the production saddle.");
            Assert.That(_controller.Saddle.ThoraxContact, Is.Not.Null, "Loaded causal audit requires the bar/thorax callback producer.");
            Assert.That(_controller.Saddle.Barbell.Body.solverIterations, Is.EqualTo(12), "Bar solver position budget changed during athlete isolation.");
            Assert.That(_controller.Saddle.Barbell.Body.solverVelocityIterations, Is.EqualTo(6), "Bar solver velocity budget changed during athlete isolation.");

            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            var authority = new OnsetTracker();
            var tracking = new OnsetTracker();
            var posture = new OnsetTracker();
            var captureSupport = new OnsetTracker();
            var saddle = new OnsetTracker();
            var footSlip = new OnsetTracker();
            bool barThoraxCallbackSeen = false;
            bool nonFiniteTelemetry = false;
            var csv = new StringBuilder();
            AppendHeader(csv);

            for (int tick = 0; tick < AuditTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
                SquatBalanceObserver balance = adapter.Balance;
                SquatBarSaddle saddleState = _controller.Saddle;
                SquatBarThoraxContactDetector barContact = saddleState.ThoraxContact;
                SquatSupportGeometry.Measurement supportGeometry = MeasureSupport(balance);
                float maximumTrackingError = MaximumTrackingError(snapshot.Joints);
                bool authoritySignal = snapshot.DriveAvailability == SquatTelemetryAvailability.AVAILABLE &&
                    snapshot.MaximumModeledDemand >= AuthorityThreshold;
                bool trackingSignal = maximumTrackingError >= TrackingThresholdRad;
                bool postureSignal = Mathf.Abs(snapshot.TrunkWorldPitchRadians) >= PostureTrunkThresholdRad ||
                    snapshot.PelvisPositionWorldMeters.Y <= PosturePelvisThresholdM;
                bool captureSignal = !balance.HasSupport ||
                    balance.CaptureMarginFront <= CaptureMarginThresholdM ||
                    balance.CaptureMarginRear <= CaptureMarginThresholdM;
                bool saddleSignal = saddleState.SaddleSeparationMeters >= SaddleSeparationThresholdM ||
                    saddleState.MaximumLimitOccupancy >= SaddleLimitThreshold || saddleState.IsBroken;
                bool slipSignal = (_controller.LeftFootContact != null &&
                    _controller.LeftFootContact.SlipSpeed > FootSlipThresholdMps) ||
                    (_controller.RightFootContact != null &&
                    _controller.RightFootContact.SlipSpeed > FootSlipThresholdMps);

                authority.Update(authoritySignal, snapshot.SimulationTick);
                tracking.Update(trackingSignal, snapshot.SimulationTick);
                posture.Update(postureSignal, snapshot.SimulationTick);
                captureSupport.Update(captureSignal, snapshot.SimulationTick);
                saddle.Update(saddleSignal, snapshot.SimulationTick);
                footSlip.Update(slipSignal, snapshot.SimulationTick);
                barThoraxCallbackSeen |= barContact.CompletedCollisionCallbackCount > 0;
                nonFiniteTelemetry |= !IsFiniteTelemetry(snapshot, saddleState);
                AppendSample(csv, loadKg, positionIterations, velocityIterations, snapshot,
                    balance, saddleState, barContact, supportGeometry, maximumTrackingError);
            }

            string profile = $"athlete-p{positionIterations}-v{velocityIterations}";
            string path = Path.Combine(MeasurementDirectory,
                $"causal-audit-{profile}-{loadKg.ToString("0", CultureInfo.InvariantCulture)}kg.csv");
            File.WriteAllText(path, csv.ToString());

            var result = new AuditResult(
                loadKg,
                positionIterations,
                velocityIterations,
                OnsetTick(authority),
                OnsetTick(tracking),
                OnsetTick(posture),
                OnsetTick(captureSupport),
                OnsetTick(saddle),
                OnsetTick(footSlip),
                barThoraxCallbackSeen,
                _controller.Saddle.ConnectedBodyCollisionEnabled,
                _controller.Saddle.BarThoraxColliderPairCount,
                _controller.Saddle.BarThoraxIgnoredPairCount,
                WrenchFeasibility,
                FirstOnsetOrder(authority, tracking, posture, captureSupport, saddle, footSlip));
            Assert.That(nonFiniteTelemetry, Is.False,
                $"GAM-13 causal audit produced non-finite telemetry at {loadKg:F0} kg, p{positionIterations}/v{velocityIterations}.");
            if (positionIterations == PhysicalAthleteSolverProfile.PositionIterations &&
                velocityIterations == PhysicalAthleteSolverProfile.VelocityIterations)
            {
                if (Mathf.Approximately(loadKg, 25f))
                {
                    Assert.That(result.PostureOnset, Is.Null, "25 kg baseline should remain below the causal posture onset gate.");
                    Assert.That(result.CaptureSupportOnset, Is.Null, "25 kg baseline should retain capture/support.");
                }
                else
                {
                    Assert.That(result.PostureOnset.HasValue || result.CaptureSupportOnset.HasValue,
                        $"{loadKg:F0} kg baseline did not reproduce the standing failure symptom within {AuditTicks} ticks.");
                }
            }
            Debug.Log("GAM13_CAUSAL " + result.ToCsv());
            return result;
        }

        private static bool IsFiniteTelemetry(SquatObservationSnapshot snapshot, SquatBarSaddle saddle)
        {
            if (!snapshot.Bar.IsAvailable || !snapshot.Support.SystemComAvailability.Equals(SquatTelemetryAvailability.AVAILABLE) ||
                !float.IsFinite(snapshot.Bar.PositionWorldMeters.X) || !float.IsFinite(snapshot.Bar.PositionWorldMeters.Y) ||
                !float.IsFinite(snapshot.Bar.PositionWorldMeters.Z) || !float.IsFinite(snapshot.MaximumModeledDemand) ||
                !float.IsFinite(saddle.SaddleSeparationMeters) || !float.IsFinite(saddle.CurrentLinearLimitOccupancy) ||
                !float.IsFinite(saddle.CurrentForceEngine.x) || !float.IsFinite(saddle.CurrentForceEngine.y) ||
                !float.IsFinite(saddle.CurrentForceEngine.z) || !float.IsFinite(saddle.CurrentTorqueEngine.x) ||
                !float.IsFinite(saddle.CurrentTorqueEngine.y) || !float.IsFinite(saddle.CurrentTorqueEngine.z))
                return false;
            return true;
        }

        private void ApplyAthleteSolverProfile(int positionIterations, int velocityIterations)
        {
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
            {
                if (segment.Body == null)
                    continue;
                segment.Body.solverIterations = positionIterations;
                segment.Body.solverVelocityIterations = velocityIterations;
            }
        }

        private SquatSupportGeometry.Measurement MeasureSupport(SquatBalanceObserver balance)
        {
            var contacts = new List<Vector3>();
            AddFootContacts(_controller.LeftFootContact, contacts);
            AddFootContacts(_controller.RightFootContact, contacts);
            return SquatSupportGeometry.Measure(
                contacts,
                new Vector2(balance.CaptureMl, balance.CaptureAp));
        }

        private static void AddFootContacts(PhysicalFootContactDetector detector, List<Vector3> contacts)
        {
            if (detector == null)
                return;
            for (int index = 0; index < detector.CompletedContactCount; index++)
                contacts.Add(detector.CompletedContactPoint(index));
        }

        private static float MaximumTrackingError(SquatJointObservationSet joints)
        {
            float maximum = 0f;
            maximum = Mathf.Max(maximum, JointTrackingError(joints.Abdomen));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.Thorax));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.LeftHip));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.RightHip));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.LeftKnee));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.RightKnee));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.LeftAnkle));
            maximum = Mathf.Max(maximum, JointTrackingError(joints.RightAnkle));
            return maximum;
        }

        private static float JointTrackingError(SquatJointObservation joint) =>
            joint.JointAvailability == SquatTelemetryAvailability.AVAILABLE
                ? Mathf.Abs(joint.ActualReferenceErrorRadians)
                : 0f;

        private void AppendHeader(StringBuilder csv)
        {
            csv.Append("load_kg,athlete_position_iterations,athlete_velocity_iterations,tick,time_s,state,sq,");
            csv.Append("bar_y,bar_vy,bar_mass_kg,bar_contact_callback,bar_contact_count,bar_contact_impulse_n_s,bar_contact_min_separation_m,");
            csv.Append("thorax_contact_joint_collision_enabled,bar_thorax_pairs,bar_thorax_ignored_pairs,bar_thorax_anchor_error_m,");
            csv.Append("saddle_separation_m,saddle_linear_limit_occupancy,saddle_angular_x_limit_occupancy,saddle_angular_y_limit_occupancy,saddle_angular_z_limit_occupancy,saddle_max_limit_occupancy,");
            csv.Append("saddle_force_engine_x,saddle_force_engine_y,saddle_force_engine_z,saddle_torque_engine_x,saddle_torque_engine_y,saddle_torque_engine_z,");
            csv.Append("bar_to_thorax_mass_ratio,com_x,com_y,com_z,com_vx,com_vy,com_vz,capture_ap,capture_ml,capture_margin_front_m,capture_margin_rear_m,");
            csv.Append("support_aabb_ap_min_m,support_aabb_ap_max_m,support_aabb_ml_min_m,support_aabb_ml_max_m,support_contact_count,support_aabb_area_m2,");
            csv.Append("support_hull_points,support_hull_area_m2,support_aabb_contains_capture,support_hull_contains_capture,support_hull_signed_margin_m,");
            csv.Append("left_foot_contact,right_foot_contact,left_foot_slip_mps,right_foot_slip_mps,trunk_pitch_rad,pelvis_y_m,max_tracking_error_rad,max_demand,");
            foreach (string jointId in CausalJointIds)
                csv.Append(jointId).Append("_demand,").Append(jointId).Append("_saturated,").Append(jointId).Append("_tracking_error_x_rad,").Append(jointId).Append("_limit,").Append(jointId).Append("_current_force_engine_x,").Append(jointId).Append("_current_force_engine_y,").Append(jointId).Append("_current_force_engine_z,").Append(jointId).Append("_current_torque_engine_x,").Append(jointId).Append("_current_torque_engine_y,").Append(jointId).Append("_current_torque_engine_z,");
            csv.AppendLine("wrench_feasibility");
        }

        private void AppendSample(
            StringBuilder csv,
            float loadKg,
            int positionIterations,
            int velocityIterations,
            SquatObservationSnapshot snapshot,
            SquatBalanceObserver balance,
            SquatBarSaddle saddle,
            SquatBarThoraxContactDetector barContact,
            SquatSupportGeometry.Measurement supportGeometry,
            float maximumTrackingError)
        {
            Vector3 com = balance.SystemCom;
            Vector3 comVelocity = balance.SystemComVelocity;
            Rigidbody barBody = saddle.Barbell.Body;
            AppendValues(csv,
                loadKg, positionIterations, velocityIterations, snapshot.SimulationTick,
                snapshot.SimulationTimeSeconds, snapshot.State, snapshot.Sq,
                snapshot.Bar.PositionWorldMeters.Y, snapshot.Bar.LinearVelocityWorldMetersPerSecond.Y,
                barBody.mass,
                barContact.CompletedCollisionCallbackCount > 0, barContact.CompletedContactCount,
                barContact.CompletedTotalImpulse.magnitude, barContact.CompletedMinimumSeparationM,
                saddle.ConnectedBodyCollisionEnabled, saddle.BarThoraxColliderPairCount, saddle.BarThoraxIgnoredPairCount,
                saddle.AnchorErrorWorld.magnitude, saddle.SaddleSeparationMeters,
                saddle.CurrentLinearLimitOccupancy, saddle.CurrentAngularXLimitOccupancy,
                saddle.CurrentAngularYLimitOccupancy, saddle.CurrentAngularZLimitOccupancy,
                saddle.MaximumLimitOccupancy,
                saddle.CurrentForceEngine.x, saddle.CurrentForceEngine.y, saddle.CurrentForceEngine.z,
                saddle.CurrentTorqueEngine.x, saddle.CurrentTorqueEngine.y, saddle.CurrentTorqueEngine.z,
                saddle.BarToThoraxMassRatio, com.x, com.y, com.z,
                comVelocity.x, comVelocity.y, comVelocity.z,
                balance.CaptureAp, balance.CaptureMl, balance.CaptureMarginFront, balance.CaptureMarginRear,
                balance.SupportApMin, balance.SupportApMax, balance.SupportMlMin, balance.SupportMlMax,
                balance.SupportContactCount, supportGeometry.AabbAreaM2,
                supportGeometry.HullPointCount, supportGeometry.HullAreaM2,
                supportGeometry.AabbContainsQuery, supportGeometry.HullContainsQuery,
                supportGeometry.HullSignedMarginM,
                _controller.LeftFootContact != null && _controller.LeftFootContact.IsInContact,
                _controller.RightFootContact != null && _controller.RightFootContact.IsInContact,
                _controller.LeftFootContact == null ? float.NaN : _controller.LeftFootContact.SlipSpeed,
                _controller.RightFootContact == null ? float.NaN : _controller.RightFootContact.SlipSpeed,
                snapshot.TrunkWorldPitchRadians, snapshot.PelvisPositionWorldMeters.Y,
                maximumTrackingError, snapshot.MaximumModeledDemand);

            for (int index = 0; index < CausalJointIds.Length; index++)
            {
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(CausalJointIds[index]);
                PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                Vector3 force = joint.Joint.currentForce;
                Vector3 torque = joint.Joint.currentTorque;
                AppendValues(csv,
                    diagnostic.ModeledDemand,
                    diagnostic.ModeledDemand >= AuthorityThreshold,
                    diagnostic.ErrorRad.x,
                    diagnostic.LimitProximity,
                    force.x, force.y, force.z,
                    torque.x, torque.y, torque.z);
            }
            csv.AppendLine(WrenchFeasibility);
        }

        private static string ThresholdDocument() =>
            "# GAM-13 causal audit thresholds\n\n" +
            "Sampling: one post-physics row per authoritative 0.01 s tick, fresh scene per load.\n\n" +
            $"Authority: modeled demand >= {AuthorityThreshold.ToString("0.00", CultureInfo.InvariantCulture)} for {ConsecutiveTicks} consecutive ticks.\n" +
            $"Tracking: load-bearing actual/reference error >= {TrackingThresholdRad * Mathf.Rad2Deg:0.0} deg for {ConsecutiveTicks} consecutive ticks.\n" +
            $"Posture: abs trunk pitch >= {PostureTrunkThresholdRad:0.00} rad or pelvis Y <= {PosturePelvisThresholdM:0.00} m for {ConsecutiveTicks} consecutive ticks.\n" +
            $"Capture/support: no plantar support or capture margin <= {CaptureMarginThresholdM:0.00} m for {ConsecutiveTicks} consecutive ticks.\n" +
            $"Saddle/contact: anchor separation >= {SaddleSeparationThresholdM:0.00} m or finite limit occupancy >= {SaddleLimitThreshold:0.00} for {ConsecutiveTicks} consecutive ticks.\n" +
            $"Foot slip: contact-point slip > {FootSlipThresholdMps:0.00} m/s for {ConsecutiveTicks} consecutive ticks.\n\n" +
            "Contact callbacks are observed separately from saddle failure. The current support AABB is a proxy; the convex hull is diagnostic.\n" +
            $"WRENCH_FEASIBILITY={WrenchFeasibility}: captured contact impulses do not identify the admissible contact-wrench cone.\n";

        private static string FirstOnsetOrder(params OnsetTracker[] trackers)
        {
            var events = new List<string>();
            AddOnset(events, "AUTHORITY", trackers[0]);
            AddOnset(events, "TRACKING", trackers[1]);
            AddOnset(events, "POSTURE", trackers[2]);
            AddOnset(events, "CAPTURE_SUPPORT", trackers[3]);
            AddOnset(events, "SADDLE_CONTACT", trackers[4]);
            AddOnset(events, "FOOT_SLIP", trackers[5]);
            events.Sort((left, right) =>
            {
                ulong leftTick = ulong.Parse(left.Substring(left.IndexOf('@') + 1), CultureInfo.InvariantCulture);
                ulong rightTick = ulong.Parse(right.Substring(right.IndexOf('@') + 1), CultureInfo.InvariantCulture);
                return leftTick.CompareTo(rightTick);
            });
            return events.Count == 0 ? "NONE" : string.Join("|", events);
        }

        private static void AddOnset(List<string> events, string name, OnsetTracker tracker)
        {
            if (tracker.HasOnset)
                events.Add(name + "@" + tracker.FirstOnsetTick.ToString(CultureInfo.InvariantCulture));
        }

        private static ulong? OnsetTick(OnsetTracker tracker) =>
            tracker.HasOnset ? (ulong?)tracker.FirstOnsetTick : null;

        private static void WriteSummary(string path, List<AuditResult> summaries)
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,athlete_position_iterations,athlete_velocity_iterations,authority_onset_tick,tracking_onset_tick,posture_onset_tick,capture_support_onset_tick,saddle_contact_onset_tick,foot_slip_onset_tick,bar_thorax_callback_seen,connected_body_collision_enabled,bar_thorax_pairs,bar_thorax_ignored_pairs,wrench_feasibility,first_onset_order");
            foreach (AuditResult result in summaries)
                csv.AppendLine(result.ToCsv());
            File.WriteAllText(path, csv.ToString());
        }

        private static void AppendValues(StringBuilder csv, params object[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0)
                    csv.Append(',');
                csv.Append(Format(values[index]));
            }
            csv.Append(',');
        }

        private static string Format(object value)
        {
            if (value == null)
                return "NA";
            if (value is float f)
                return float.IsNaN(f) ? "NA" : f.ToString("R", CultureInfo.InvariantCulture);
            if (value is double d)
                return double.IsNaN(d) ? "NA" : d.ToString("R", CultureInfo.InvariantCulture);
            if (value is bool b)
                return b ? "true" : "false";
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static string MeasurementDirectory =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-13"));

        private sealed class OnsetTracker
        {
            private int _consecutive;

            public bool HasOnset { get; private set; }
            public ulong FirstOnsetTick { get; private set; }

            public void Update(bool signal, ulong tick)
            {
                if (!signal)
                {
                    _consecutive = 0;
                    return;
                }

                _consecutive++;
                if (!HasOnset && _consecutive >= ConsecutiveTicks)
                {
                    HasOnset = true;
                    FirstOnsetTick = tick >= (ulong)(ConsecutiveTicks - 1)
                        ? tick - (ulong)(ConsecutiveTicks - 1)
                        : 0ul;
                }
            }
        }

        private readonly struct AuditResult
        {
            public AuditResult(
                float loadKg,
                int positionIterations,
                int velocityIterations,
                ulong? authorityOnset,
                ulong? trackingOnset,
                ulong? postureOnset,
                ulong? captureSupportOnset,
                ulong? saddleOnset,
                ulong? footSlipOnset,
                bool callbackSeen,
                bool connectedBodyCollisionEnabled,
                int barThoraxPairs,
                int barThoraxIgnoredPairs,
                string wrenchFeasibility,
                string firstOnsetOrder)
            {
                LoadKg = loadKg;
                PositionIterations = positionIterations;
                VelocityIterations = velocityIterations;
                AuthorityOnset = authorityOnset;
                TrackingOnset = trackingOnset;
                PostureOnset = postureOnset;
                CaptureSupportOnset = captureSupportOnset;
                SaddleOnset = saddleOnset;
                FootSlipOnset = footSlipOnset;
                CallbackSeen = callbackSeen;
                ConnectedBodyCollisionEnabled = connectedBodyCollisionEnabled;
                BarThoraxPairs = barThoraxPairs;
                BarThoraxIgnoredPairs = barThoraxIgnoredPairs;
                WrenchFeasibility = wrenchFeasibility;
                FirstOnsetOrder = firstOnsetOrder;
            }

            public float LoadKg { get; }
            public int PositionIterations { get; }
            public int VelocityIterations { get; }
            public ulong? AuthorityOnset { get; }
            public ulong? TrackingOnset { get; }
            public ulong? PostureOnset { get; }
            public ulong? CaptureSupportOnset { get; }
            public ulong? SaddleOnset { get; }
            public ulong? FootSlipOnset { get; }
            public bool CallbackSeen { get; }
            public bool ConnectedBodyCollisionEnabled { get; }
            public int BarThoraxPairs { get; }
            public int BarThoraxIgnoredPairs { get; }
            public string WrenchFeasibility { get; }
            public string FirstOnsetOrder { get; }

            public string ToCsv() => string.Join(",",
                Format(LoadKg),
                PositionIterations.ToString(CultureInfo.InvariantCulture),
                VelocityIterations.ToString(CultureInfo.InvariantCulture),
                FormatTick(AuthorityOnset), FormatTick(TrackingOnset), FormatTick(PostureOnset),
                FormatTick(CaptureSupportOnset), FormatTick(SaddleOnset), FormatTick(FootSlipOnset),
                Format(CallbackSeen), Format(ConnectedBodyCollisionEnabled),
                BarThoraxPairs.ToString(CultureInfo.InvariantCulture),
                BarThoraxIgnoredPairs.ToString(CultureInfo.InvariantCulture),
                WrenchFeasibility, FirstOnsetOrder);

            private static string FormatTick(ulong? tick) =>
                tick.HasValue ? tick.Value.ToString(CultureInfo.InvariantCulture) : "NA";
        }
    }
}
