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
    /// Deterministic GAM-13 causal audit. Evidence tooling only: it holds the
    /// production SETUP reference, records one row per post-physics tick, and
    /// never changes targets, capacities, contact modes, or outcomes. The
    /// only permitted plant changes are the explicitly named solver profile
    /// and at most one environment-selected single-property intervention.
    ///
    /// Onset predicates are frozen in GAM13_CAUSAL_ONSET_PREDICATES_V1
    /// (Artifacts/Research/GAM-13-causal-onset-predicates.md) and were
    /// committed before any trace from this fixture was interpreted.
    /// </summary>
    public sealed class GAM13CausalAuditTests
    {
        public const string PredicateVersion = "GAM13_CAUSAL_ONSET_PREDICATES_V1";

        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int AuditTicks = 600;
        private const int StageASettleTicks = 100;
        private const int ConsecutiveTicks = 3;

        // GAM13_CAUSAL_ONSET_PREDICATES_V1. Existing contracts are reused
        // where one exists; DriveHighDemand and GuardWithdrawalScale are the
        // only new diagnostic thresholds.
        private const float DriveHighDemand = 0.50f;
        private const float DriveSaturationDemand = PoweredJointController.ModeledDemandSaturationThreshold;
        private const float TrackingFailureRad = 10f * Mathf.Deg2Rad;
        private const float PostureJointErrorRad = 10f * Mathf.Deg2Rad;
        private const float PostureTrunkPitchRad = 0.70f;
        private const float PosturePelvisHeightM = 0.90f;
        private const float CaptureMarginM = 0.01f;
        private const float SaddleLinearOccupancy = 0.95f;
        private const float FootSlipMps = 0.05f;
        private const float GuardWithdrawalScale = 0.50f;

        // Stage-A standing contract, reproduced only for classification parity.
        private const float StageAMaxComSpeedMps = 0.25f;
        private const float StageAMaxFootPitchDeg = 12f;
        private const float StageAMaxSaturationFraction = 0.05f;
        private const float StageAMaxPostureErrorDeg = 10f;
        private const float StageAMaxLimitProximity = 0.95f;
        private const float StageAMaxSaddleSeparationM = 0.05f;

        private const int ProductionBarPositionIterations = 12;
        private const int ProductionBarVelocityIterations = 6;

        private static readonly float[] CoreLoadsKg = { 25f, 60f, 300f };
        private static readonly float[] TopologyLoadsKg = { 25f, 60f, 140f, 170f, 300f };
        private static readonly int[] VelocityProfiles = { 1, 4, 8 };
        private static readonly int[] PositionProfiles = { 28, 32, 40 };

        private static readonly string[] LoadBearingJointIds =
        {
            "left_foot", "right_foot", "left_shank", "right_shank",
            "left_thigh", "right_thigh", "abdomen", "thorax"
        };

        private static readonly string[] PoweredJointIds =
        {
            "left_foot", "right_foot", "left_shank", "right_shank",
            "left_thigh", "right_thigh", "abdomen", "thorax", "head_neck",
            "left_upper_arm", "right_upper_arm", "left_forearm", "right_forearm",
            "left_hand", "right_hand"
        };

        /// <summary>Canonical events in declaration order.</summary>
        public static readonly string[] CanonicalEvents =
        {
            "DRIVE_HIGH", "DRIVE_SATURATION", "TRACKING_FAILURE", "POSTURE_DEPARTURE",
            "CAPTURE_DEPARTURE", "SUPPORT_LOSS", "SADDLE_LINEAR_LIMIT", "SADDLE_GROSS_FAILURE",
            "CONTACT_MODE_CHANGE"
        };

        /// <summary>Supplementary decompositions; never used for the canonical order.</summary>
        public static readonly string[] SupplementaryEvents =
        {
            "POSTURE_JOINT_ERROR", "POSTURE_GROSS", "GUARD_WITHDRAWAL", "CAPTURE_DEPARTURE_HULL",
            "COM_OUTSIDE_HULL", "FOOT_LIFT", "FOOT_SLIP", "PLANTAR_COUNT_CHANGE",
            "BAR_THORAX_CONTACT", "BAR_ATHLETE_CONTACT", "NONPLANTAR_GROUND_CONTACT", "SADDLE_ANGULAR_LIMIT"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;
        private readonly Dictionary<string, GAM13GroundContactProbe> _probes =
            new Dictionary<string, GAM13GroundContactProbe>(StringComparer.Ordinal);

        [UnityTest]
        [Explicit("GAM-13 production-profile causal baseline and bar/back topology at the canonical loads.")]
        public IEnumerator GAM13_CAUSAL_AUDIT_BASELINE_AND_TOPOLOGY()
        {
            WriteText("causal-onset-predicates.txt", PredicateDocument());
            var summaries = new List<AuditResult>();
            var topology = new StringBuilder();
            topology.AppendLine(TopologyHeader());
            foreach (float loadKg in TopologyLoadsKg)
            {
                yield return LoadFreshScene();
                summaries.Add(RunAudit(loadKg,
                    PhysicalAthleteSolverProfile.PositionIterations,
                    PhysicalAthleteSolverProfile.VelocityIterations,
                    null, topology));
                yield return null;
            }

            WriteText("causal-topology.csv", topology.ToString());
            WriteSummary("causal-summary-baseline.csv", summaries);
            AssertBaseline(summaries);
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 athlete velocity-iteration isolation; bar fixed at 12/6.")]
        public IEnumerator GAM13_CAUSAL_AUDIT_VELOCITY_ISOLATION()
        {
            var summaries = new List<AuditResult>();
            foreach (int velocityIterations in VelocityProfiles)
            {
                foreach (float loadKg in CoreLoadsKg)
                {
                    yield return LoadFreshScene();
                    summaries.Add(RunAudit(loadKg,
                        PhysicalAthleteSolverProfile.PositionIterations, velocityIterations, null, null));
                    yield return null;
                }
            }

            WriteSummary("causal-summary-velocity.csv", summaries);
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 athlete position-iteration isolation; bar fixed at 12/6.")]
        public IEnumerator GAM13_CAUSAL_AUDIT_POSITION_ISOLATION()
        {
            var summaries = new List<AuditResult>();
            foreach (int positionIterations in PositionProfiles)
            {
                foreach (float loadKg in CoreLoadsKg)
                {
                    yield return LoadFreshScene();
                    summaries.Add(RunAudit(loadKg,
                        positionIterations, PhysicalAthleteSolverProfile.VelocityIterations, null, null));
                    yield return null;
                }
            }

            WriteSummary("causal-summary-position.csv", summaries);
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 single-property causal intervention selected by GAM13_CAUSAL_INTERVENTION.")]
        public IEnumerator GAM13_CAUSAL_AUDIT_INTERVENTION()
        {
            string intervention = Environment.GetEnvironmentVariable("GAM13_CAUSAL_INTERVENTION");
            Assert.That(string.IsNullOrWhiteSpace(intervention), Is.False,
                "GAM13_CAUSAL_INTERVENTION must name exactly one key=value intervention.");
            Assert.That(intervention.IndexOf(';') < 0 && intervention.IndexOf(',') < 0, Is.True,
                "Only one intervention property may change at a time.");
            float[] loads = RequestedLoads();
            var summaries = new List<AuditResult>();
            foreach (float loadKg in loads)
            {
                yield return LoadFreshScene();
                summaries.Add(RunAudit(loadKg,
                    PhysicalAthleteSolverProfile.PositionIterations,
                    PhysicalAthleteSolverProfile.VelocityIterations,
                    intervention, null));
                yield return null;
            }

            WriteSummary("causal-summary-intervention-" + SafeLabel(intervention) + ".csv", summaries);
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

        private AuditResult RunAudit(
            float loadKg,
            int positionIterations,
            int velocityIterations,
            string intervention,
            StringBuilder topology)
        {
            _controller.SetLoad(loadKg);
            ApplyAthleteSolverProfile(positionIterations, velocityIterations);
            SquatBarSaddle saddle = _controller.Saddle;
            Assert.That(saddle, Is.Not.Null, "Loaded causal audit requires the production saddle.");
            Assert.That(saddle.ThoraxContact, Is.Not.Null, "Loaded causal audit requires the bar/thorax callback producer.");
            Rigidbody barBody = saddle.Barbell.Body;
            Assert.That(barBody.solverIterations, Is.EqualTo(ProductionBarPositionIterations), "Bar solver position budget changed.");
            Assert.That(barBody.solverVelocityIterations, Is.EqualTo(ProductionBarVelocityIterations), "Bar solver velocity budget changed.");
            string interventionLabel = ApplyIntervention(intervention);
            AttachProbes(barBody);

            string profile = $"p{positionIterations}-v{velocityIterations}";
            string runId = $"{profile}-{interventionLabel}-{loadKg.ToString("0", CultureInfo.InvariantCulture)}kg";
            topology?.Append(TopologyRow(loadKg, saddle));

            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            string[] bodyIds = SortedBodyIds();
            WriteText("bodymeta-" + runId + ".csv", BodyMeta(bodyIds, barBody));

            var trackers = new Dictionary<string, OnsetTracker>(StringComparer.Ordinal);
            foreach (string name in CanonicalEvents)
                trackers[name] = new OnsetTracker();
            foreach (string name in SupplementaryEvents)
                trackers[name] = new OnsetTracker();

            var trace = new StringBuilder(AuditTicks * 4096);
            var bodies = new StringBuilder(AuditTicks * 2048);
            var contacts = new StringBuilder(AuditTicks * 1024);
            AppendTraceHeader(trace);
            AppendBodyHeader(bodies, bodyIds);
            contacts.AppendLine("tick,body,other,point_x,point_y,point_z,normal_x,normal_y,normal_z,impulse_x,impulse_y,impulse_z,separation_m");

            var stageA = new StageAAccumulator();
            var reference = new ContactModeReference();
            string contactModeReason = "NONE";
            bool nonFinite = false;
            int barThoraxCallbackTicks = 0;
            float maxPenetration = 0f;

            for (int tick = 0; tick < AuditTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                foreach (GAM13GroundContactProbe probe in _probes.Values)
                    probe.CompleteStep();

                SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
                var sample = new TickSample(this, snapshot, adapter, saddle, barBody);
                nonFinite |= !sample.Finite;
                if (sample.BarThoraxCallbacks > 0)
                    barThoraxCallbackTicks++;
                maxPenetration = Mathf.Max(maxPenetration, sample.BarThoraxPenetrationM);

                reference.Observe(sample);
                string modeChange = reference.ChangeReason(sample);
                bool contactModeSignal = modeChange != "NONE";

                var signals = new Dictionary<string, bool>(StringComparer.Ordinal)
                {
                    ["DRIVE_HIGH"] = sample.MaxLoadBearingDemand >= DriveHighDemand,
                    ["DRIVE_SATURATION"] = sample.MaxLoadBearingDemand >= DriveSaturationDemand,
                    ["TRACKING_FAILURE"] = sample.MaxLoadBearingErrorRad >= TrackingFailureRad,
                    ["POSTURE_JOINT_ERROR"] = adapter.CanonicalPostureErrorRad >= PostureJointErrorRad,
                    ["POSTURE_GROSS"] = Mathf.Abs(snapshot.TrunkWorldPitchRadians) >= PostureTrunkPitchRad ||
                        snapshot.PelvisPositionWorldMeters.Y <= PosturePelvisHeightM,
                    ["CAPTURE_DEPARTURE"] = sample.Balance.HasSupport &&
                        Mathf.Min(sample.Balance.CaptureMarginFront, sample.Balance.CaptureMarginRear) <= CaptureMarginM,
                    ["SUPPORT_LOSS"] = !sample.Balance.HasSupport,
                    ["SADDLE_LINEAR_LIMIT"] = saddle.IsAttached && saddle.CurrentLinearLimitOccupancy >= SaddleLinearOccupancy,
                    ["SADDLE_GROSS_FAILURE"] = saddle.IsBroken,
                    ["CONTACT_MODE_CHANGE"] = contactModeSignal,
                    ["GUARD_WITHDRAWAL"] = sample.Control.PostureGuardScale <= GuardWithdrawalScale,
                    ["CAPTURE_DEPARTURE_HULL"] = sample.Capture.ContactCount > 0 &&
                        !(sample.Capture.HullSignedMarginM > CaptureMarginM),
                    ["COM_OUTSIDE_HULL"] = sample.Com.ContactCount > 0 && !(sample.Com.HullSignedMarginM > 0f),
                    ["FOOT_LIFT"] = sample.LeftContacts == 0 || sample.RightContacts == 0,
                    ["FOOT_SLIP"] = sample.LeftSlip > FootSlipMps || sample.RightSlip > FootSlipMps,
                    ["PLANTAR_COUNT_CHANGE"] = modeChange.Contains("COUNT"),
                    ["BAR_THORAX_CONTACT"] = sample.BarThoraxCallbacks > 0,
                    ["BAR_ATHLETE_CONTACT"] = sample.BarAthleteBodies.Length > 0,
                    ["NONPLANTAR_GROUND_CONTACT"] = sample.NonPlantarGroundBodies.Length > 0,
                    ["SADDLE_ANGULAR_LIMIT"] = saddle.IsAttached && Mathf.Max(
                        saddle.CurrentAngularXLimitOccupancy,
                        Mathf.Max(saddle.CurrentAngularYLimitOccupancy, saddle.CurrentAngularZLimitOccupancy)) >= SaddleLinearOccupancy
                };
                signals["POSTURE_DEPARTURE"] = signals["POSTURE_JOINT_ERROR"] || signals["POSTURE_GROSS"];

                ulong simulationTick = snapshot.SimulationTick;
                foreach (KeyValuePair<string, bool> signal in signals)
                    trackers[signal.Key].Update(signal.Value, simulationTick);
                if (contactModeReason == "NONE" && trackers["CONTACT_MODE_CHANGE"].HasOnset)
                    contactModeReason = reference.LastReason;

                stageA.Observe(tick, sample, adapter, saddle, FootPitchDegrees("left_foot"),
                    snapshot.PelvisPositionWorldMeters.Y, snapshot.TrunkWorldPitchRadians);
                AppendTraceRow(trace, loadKg, profile, interventionLabel, snapshot, adapter, saddle, sample, signals, modeChange);
                AppendBodyRow(bodies, simulationTick, bodyIds, barBody);
                AppendContactRows(contacts, simulationTick);
            }

            WriteText("trace-" + runId + ".csv", trace.ToString());
            WriteText("bodies-" + runId + ".csv", bodies.ToString());
            WriteText("contacts-" + runId + ".csv", contacts.ToString());

            var result = new AuditResult
            {
                LoadKg = loadKg,
                PositionIterations = positionIterations,
                VelocityIterations = velocityIterations,
                IslandPositionIterations = Mathf.Max(positionIterations, barBody.solverIterations),
                IslandVelocityIterations = Mathf.Max(velocityIterations, barBody.solverVelocityIterations),
                Intervention = interventionLabel,
                ContactModeReason = contactModeReason,
                BarThoraxCallbackTicks = barThoraxCallbackTicks,
                MaxBarThoraxPenetrationM = maxPenetration,
                ConnectedBodyCollisionEnabled = saddle.ConnectedBodyCollisionEnabled,
                BarToThoraxMassRatio = saddle.BarToThoraxMassRatio,
                NonFinite = nonFinite,
                StageASummary = stageA.Summary(loadKg),
                StageAPass = stageA.Pass,
                Upright = stageA.Upright
            };
            foreach (string name in CanonicalEvents)
                result.Onsets[name] = OnsetTick(trackers[name]);
            foreach (string name in SupplementaryEvents)
                result.Onsets[name] = OnsetTick(trackers[name]);

            Debug.Log("GAM13_CAUSAL " + result.ToCsv());
            Debug.Log("GAM13_CAUSAL_STAGE_A " + result.StageASummary);
            Assert.That(nonFinite, Is.False, $"GAM-13 causal audit produced non-finite telemetry for {runId}.");
            return result;
        }

        private static void AssertBaseline(List<AuditResult> summaries)
        {
            foreach (AuditResult result in summaries)
            {
                if (Mathf.Approximately(result.LoadKg, 25f))
                {
                    Assert.That(result.StageAPass, Is.True, "25 kg production standing must remain qualified: " + result.StageASummary);
                }
                else if (Mathf.Approximately(result.LoadKg, 60f) || Mathf.Approximately(result.LoadKg, 300f))
                {
                    Assert.That(result.StageAPass, Is.False,
                        $"{result.LoadKg:F0} kg baseline did not reproduce the recorded standing failure: {result.StageASummary}");
                }
            }
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

        /// <summary>
        /// Applies exactly one named single-property change after the load is
        /// configured. Everything else in the plant stays at production.
        /// </summary>
        private string ApplyIntervention(string intervention)
        {
            if (string.IsNullOrWhiteSpace(intervention))
                return "production";

            string[] parts = intervention.Split('=');
            Assert.That(parts.Length, Is.EqualTo(2), "Intervention must use key=value.");
            string key = parts[0].Trim();
            string value = parts[1].Trim();
            ConfigurableJoint joint = _controller.Saddle.Joint;
            switch (key)
            {
                case "saddle.linear_limit_m":
                    joint.linearLimit = new SoftJointLimit { limit = ParseFloat(value) };
                    break;
                case "saddle.linear_spring":
                {
                    float spring = ParseFloat(value);
                    joint.xDrive = WithSpring(joint.xDrive, spring);
                    joint.yDrive = WithSpring(joint.yDrive, spring);
                    joint.zDrive = WithSpring(joint.zDrive, spring);
                    break;
                }
                case "saddle.enable_collision":
                    joint.enableCollision = ParseBool(value);
                    break;
                case "saddle.ignore_bar_athlete_non_thorax":
                    if (ParseBool(value))
                        IgnoreBarAthleteNonThorax();
                    break;
                case "balance.posture_guard":
                    _controller.Adapter.BalanceController.PostureGuardEnabled = ParseBool(value);
                    break;
                default:
                    Assert.Fail("Unknown GAM-13 causal intervention key: " + key);
                    break;
            }
            return SafeLabel(key + "=" + value);
        }

        /// <summary>
        /// The collision filter the saddle comment describes: ignore every
        /// bar collider against every non-thorax athlete collider. Used only
        /// as a single-property contact-topology intervention.
        /// </summary>
        private void IgnoreBarAthleteNonThorax()
        {
            Rigidbody thorax = _controller.Saddle.ThoraxBody;
            foreach (Collider bar in _controller.Saddle.Barbell.Body.GetComponentsInChildren<Collider>(true))
            {
                foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
                {
                    if (segment.Body == null || segment.Body == thorax)
                        continue;
                    foreach (Collider athlete in segment.Body.GetComponents<Collider>())
                        Physics.IgnoreCollision(bar, athlete, true);
                }
            }
        }

        private static JointDrive WithSpring(JointDrive drive, float spring) => new JointDrive
        {
            positionSpring = spring,
            positionDamper = drive.positionDamper,
            maximumForce = drive.maximumForce,
            useAcceleration = drive.useAcceleration
        };

        private void AttachProbes(Rigidbody barBody)
        {
            _probes.Clear();
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> pair in _rig.Segments)
            {
                if (pair.Value.Body == null)
                    continue;
                _probes[pair.Key] = AttachProbe(pair.Value.Body.gameObject);
            }
            _probes["barbell"] = AttachProbe(barBody.gameObject);
        }

        private static GAM13GroundContactProbe AttachProbe(GameObject target)
        {
            GAM13GroundContactProbe probe = target.GetComponent<GAM13GroundContactProbe>();
            if (probe == null)
                probe = target.AddComponent<GAM13GroundContactProbe>();
            probe.Clear();
            return probe;
        }

        private string[] SortedBodyIds()
        {
            var ids = new List<string>();
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> pair in _rig.Segments)
            {
                if (pair.Value.Body != null)
                    ids.Add(pair.Key);
            }
            ids.Sort(StringComparer.Ordinal);
            return ids.ToArray();
        }

        private Rigidbody BodyById(string id, Rigidbody barBody) =>
            id == "barbell" ? barBody : _rig.Segments[id].Body;

        private string BodyMeta(string[] bodyIds, Rigidbody barBody)
        {
            var csv = new StringBuilder();
            csv.AppendLine("body,mass_kg,inertia_x,inertia_y,inertia_z,inertia_rot_x,inertia_rot_y,inertia_rot_z,inertia_rot_w,local_com_x,local_com_y,local_com_z,solver_position,solver_velocity,linear_damping,angular_damping");
            var ids = new List<string>(bodyIds) { "barbell" };
            foreach (string id in ids)
            {
                Rigidbody body = BodyById(id, barBody);
                AppendRow(csv, id, body.mass,
                    body.inertiaTensor.x, body.inertiaTensor.y, body.inertiaTensor.z,
                    body.inertiaTensorRotation.x, body.inertiaTensorRotation.y,
                    body.inertiaTensorRotation.z, body.inertiaTensorRotation.w,
                    body.centerOfMass.x, body.centerOfMass.y, body.centerOfMass.z,
                    body.solverIterations, body.solverVelocityIterations,
                    body.linearDamping, body.angularDamping);
            }
            return csv.ToString();
        }

        private static void AppendBodyHeader(StringBuilder csv, string[] bodyIds)
        {
            csv.Append("tick");
            var ids = new List<string>(bodyIds) { "barbell" };
            foreach (string id in ids)
            {
                foreach (string field in new[] { "cx", "cy", "cz", "vx", "vy", "vz", "qx", "qy", "qz", "qw", "wx", "wy", "wz" })
                    csv.Append(',').Append(id).Append('_').Append(field);
            }
            csv.AppendLine();
        }

        private void AppendBodyRow(StringBuilder csv, ulong tick, string[] bodyIds, Rigidbody barBody)
        {
            csv.Append(tick.ToString(CultureInfo.InvariantCulture));
            var ids = new List<string>(bodyIds) { "barbell" };
            foreach (string id in ids)
            {
                Rigidbody body = BodyById(id, barBody);
                Vector3 c = body.worldCenterOfMass;
                Vector3 v = body.linearVelocity;
                Quaternion q = body.rotation;
                Vector3 w = body.angularVelocity;
                foreach (float value in new[] { c.x, c.y, c.z, v.x, v.y, v.z, q.x, q.y, q.z, q.w, w.x, w.y, w.z })
                    csv.Append(',').Append(Format(value));
            }
            csv.AppendLine();
        }

        private void AppendContactRows(StringBuilder csv, ulong tick)
        {
            foreach (KeyValuePair<string, GAM13GroundContactProbe> pair in _probes)
            {
                GAM13GroundContactProbe probe = pair.Value;
                for (int index = 0; index < probe.CompletedCount; index++)
                {
                    GAM13GroundContactProbe.Sample contact = probe.Completed(index);
                    AppendRow(csv, tick, pair.Key, contact.Other,
                        contact.Point.x, contact.Point.y, contact.Point.z,
                        contact.Normal.x, contact.Normal.y, contact.Normal.z,
                        contact.Impulse.x, contact.Impulse.y, contact.Impulse.z,
                        contact.Separation);
                }
            }
        }

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return float.NaN;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private string TopologyHeader() =>
            "load_kg,bar_mass_kg,thorax_mass_kg,athlete_mass_kg,bar_to_thorax_mass_ratio,bar_to_athlete_mass_ratio," +
            "system_mass_kg,enable_collision,bar_thorax_collider_pairs,bar_thorax_ignored_pairs," +
            "bar_athlete_ignored_pairs_total,saddle_enumerated_bar_colliders,bar_body_colliders,athlete_colliders,anchor_x,anchor_y,anchor_z,connected_anchor_x,connected_anchor_y,connected_anchor_z," +
            "auto_configure_connected_anchor,enable_preprocessing,projection_mode,projection_distance_m,projection_angle_deg," +
            "break_force_n,break_torque_nm,mass_scale,connected_mass_scale,linear_limit_m,linear_limit_spring," +
            "linear_drive_spring,linear_drive_damper,linear_drive_max_force,angular_x_low_deg,angular_x_high_deg," +
            "angular_y_deg,angular_z_deg,angular_drive_spring,angular_drive_damper,angular_drive_max_force," +
            "initial_anchor_error_m,initial_relative_qx,initial_relative_qy,initial_relative_qz,initial_relative_qw," +
            "bar_solver_position,bar_solver_velocity,thorax_solver_position,thorax_solver_velocity,static_linear_deflection_at_spring_m";

        private string TopologyRow(float loadKg, SquatBarSaddle saddle)
        {
            ConfigurableJoint joint = saddle.Joint;
            float athleteMass = 0f;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
            {
                if (segment.Body != null)
                    athleteMass += segment.Body.mass;
            }
            int ignoredTotal = 0;
            int athleteColliders = 0;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
            {
                if (segment.Body != null)
                    athleteColliders += segment.Body.GetComponents<Collider>().Length;
            }
            Collider[] barBodyColliders = saddle.Barbell.Body.GetComponentsInChildren<Collider>(true);
            int saddleEnumeratedBarColliders = saddle.Barbell.GetComponentsInChildren<Collider>(true).Length;
            foreach (Collider bar in barBodyColliders)
            {
                foreach (PhysicalAthleteRig.SegmentRuntime segment in _rig.Segments.Values)
                {
                    if (segment.Body == null)
                        continue;
                    foreach (Collider athlete in segment.Body.GetComponents<Collider>())
                    {
                        if (Physics.GetIgnoreCollision(bar, athlete))
                            ignoredTotal++;
                    }
                }
            }
            Quaternion initial = saddle.InitialRelativeBarToThorax;
            float barWeight = saddle.BarMassKg * SquatBalanceObserver.GravityMagnitudeMps2;
            var row = new StringBuilder();
            AppendValues(row, false,
                loadKg, saddle.BarMassKg, saddle.ThoraxMassKg, athleteMass,
                saddle.BarToThoraxMassRatio, saddle.BarMassKg / athleteMass, athleteMass + saddle.BarMassKg,
                joint.enableCollision, saddle.BarThoraxColliderPairCount, saddle.BarThoraxIgnoredPairCount, ignoredTotal,
                saddleEnumeratedBarColliders, barBodyColliders.Length, athleteColliders,
                joint.anchor.x, joint.anchor.y, joint.anchor.z,
                joint.connectedAnchor.x, joint.connectedAnchor.y, joint.connectedAnchor.z,
                joint.autoConfigureConnectedAnchor, joint.enablePreprocessing, joint.projectionMode,
                joint.projectionDistance, joint.projectionAngle, joint.breakForce, joint.breakTorque,
                joint.massScale, joint.connectedMassScale, joint.linearLimit.limit, joint.linearLimitSpring.spring,
                joint.yDrive.positionSpring, joint.yDrive.positionDamper, joint.yDrive.maximumForce,
                joint.lowAngularXLimit.limit, joint.highAngularXLimit.limit, joint.angularYLimit.limit, joint.angularZLimit.limit,
                joint.angularXDrive.positionSpring, joint.angularXDrive.positionDamper, joint.angularXDrive.maximumForce,
                saddle.InitialAnchorErrorMeters, initial.x, initial.y, initial.z, initial.w,
                saddle.Barbell.Body.solverIterations, saddle.Barbell.Body.solverVelocityIterations,
                saddle.ThoraxBody.solverIterations, saddle.ThoraxBody.solverVelocityIterations,
                joint.yDrive.positionSpring > 0f ? barWeight / joint.yDrive.positionSpring : float.PositiveInfinity);
            return row.ToString();
        }

        private void AppendTraceHeader(StringBuilder csv)
        {
            var columns = new List<string>
            {
                "load_kg", "profile", "intervention", "tick", "time_s", "state", "sq",
                // posture
                "pelvis_x", "pelvis_y", "pelvis_z", "trunk_pitch_rad", "thorax_qx", "thorax_qy", "thorax_qz", "thorax_qw",
                "thorax_wx", "thorax_wy", "thorax_wz", "canonical_posture_error_rad", "canonical_posture_worst_joint",
                "canonical_posture_limit_proximity", "canonical_unexpected_margin_consumed", "adapter_max_drive_saturation",
                "max_lb_demand", "max_lb_demand_joint", "max_lb_error_rad", "max_lb_error_joint",
                // balance
                "com_x", "com_y", "com_z", "com_vx", "com_vy", "com_vz", "system_mass_kg", "com_height_m", "omega",
                "cop_available", "cop_x", "cop_z", "total_normal_impulse_n_s", "capture_ap", "capture_ml",
                "capture_margin_front_m", "capture_margin_rear_m", "com_margin_front_m", "com_margin_rear_m",
                "support_ap_min", "support_ap_max", "support_ml_min", "support_ml_max", "support_contact_count", "has_support",
                "ctrl_active", "ctrl_com_ref_ap", "ctrl_com_ap_error", "ctrl_desired_com_acc_ap", "ctrl_cop_desired_ap",
                "ctrl_cop_desired_ap_unclamped", "ctrl_cop_measured_ap", "ctrl_cop_error_ap", "ctrl_has_cop",
                "ctrl_raw_ankle_offset_rad", "ctrl_raw_ankle_authority", "ctrl_ankle_offset_rad", "ctrl_ankle_authority",
                "ctrl_ankle_saturated", "ctrl_guard_scale", "ctrl_guard_enabled", "ctrl_posture_error_rad",
                "ctrl_posture_rate_rad_s", "ctrl_posture_limit", "ctrl_unexpected_margin", "ctrl_hip_strategy_enabled",
                "ctrl_hip_strategy_blend", "ctrl_hip_offset_rad", "ctrl_trunk_offset_rad", "ctrl_ankle_frontal_rad",
                "ctrl_hip_frontal_rad", "ctrl_requested_ankle_torque_nm",
                // plantar contact
                "left_contacts", "right_contacts", "left_in_contact", "right_in_contact",
                "left_normal_impulse_n_s", "right_normal_impulse_n_s", "left_slip_mps", "right_slip_mps",
                "left_probe_contacts", "right_probe_contacts", "plantar_probe_normal_impulse_n_s",
                "plantar_probe_tangential_impulse_n_s", "nonplantar_ground_bodies", "bar_ground_contacts",
                // support geometry
                "support_hull_points", "support_hull_area_m2", "support_aabb_area_m2",
                "com_hull_margin_m", "com_aabb_margin_m", "com_aabb_ap_margin_m",
                "cop_hull_margin_m", "cop_aabb_margin_m", "cop_aabb_ap_margin_m",
                "capture_hull_margin_m", "capture_aabb_margin_m", "capture_aabb_ap_margin_m",
                // bar and saddle
                "bar_x", "bar_y", "bar_z", "bar_qx", "bar_qy", "bar_qz", "bar_qw", "bar_vx", "bar_vy", "bar_vz",
                "bar_wx", "bar_wy", "bar_wz", "bar_mass_kg", "saddle_attached", "saddle_broken",
                "saddle_sep_x", "saddle_sep_y", "saddle_sep_z", "saddle_sep_m", "saddle_sep_bar_x", "saddle_sep_bar_y",
                "saddle_sep_bar_z", "saddle_linear_occupancy", "saddle_angular_x_occupancy", "saddle_angular_y_occupancy",
                "saddle_angular_z_occupancy", "saddle_relative_rotation_deg", "saddle_force_x", "saddle_force_y",
                "saddle_force_z", "saddle_torque_x", "saddle_torque_y", "saddle_torque_z", "bar_thorax_callbacks",
                "bar_thorax_contacts", "bar_thorax_impulse_x", "bar_thorax_impulse_y", "bar_thorax_impulse_z",
                "bar_thorax_min_separation_m", "bar_thorax_penetration_m", "bar_thorax_gap_m", "bar_athlete_bodies",
                "bar_athlete_contacts", "bar_athlete_impulse_x", "bar_athlete_impulse_y", "bar_athlete_impulse_z",
                "contact_mode_change"
            };
            foreach (string jointId in PoweredJointIds)
            {
                foreach (string field in new[]
                {
                    "applied_x", "applied_y", "applied_z", "actual_x", "actual_y", "actual_z",
                    "err_x", "err_y", "err_z", "err_mag", "target_vel_x", "vel_x", "vel_y", "vel_z",
                    "max_force_nm", "demand", "limit", "solver_tq_x", "solver_tq_y", "solver_tq_z"
                })
                    columns.Add(jointId + "_" + field);
            }
            foreach (string name in CanonicalEvents)
                columns.Add("sig_" + name);
            foreach (string name in SupplementaryEvents)
                columns.Add("sig_" + name);
            csv.AppendLine(string.Join(",", columns));
        }

        private void AppendTraceRow(
            StringBuilder csv,
            float loadKg,
            string profile,
            string intervention,
            SquatObservationSnapshot snapshot,
            SquatPhysicalAdapter adapter,
            SquatBarSaddle saddle,
            TickSample s,
            Dictionary<string, bool> signals,
            string modeChange)
        {
            SquatBalanceObserver b = s.Balance;
            SquatPredictiveBalanceController c = s.Control;
            Rigidbody thorax = saddle.ThoraxBody;
            Rigidbody bar = saddle.Barbell.Body;
            Vector3 sep = saddle.AnchorErrorWorld;
            Vector3 sepBar = saddle.AnchorErrorBarLocal;
            Vector3 force = saddle.CurrentForceEngine;
            Vector3 torque = saddle.CurrentTorqueEngine;
            SquatBarThoraxContactDetector barContact = saddle.ThoraxContact;
            Vector3 barImpulse = barContact == null ? Vector3.zero : barContact.CompletedTotalImpulse;
            AppendValues(csv, true,
                loadKg, profile, intervention, snapshot.SimulationTick, snapshot.SimulationTimeSeconds, snapshot.State, snapshot.Sq,
                snapshot.PelvisPositionWorldMeters.X, snapshot.PelvisPositionWorldMeters.Y, snapshot.PelvisPositionWorldMeters.Z,
                snapshot.TrunkWorldPitchRadians, thorax.rotation.x, thorax.rotation.y, thorax.rotation.z, thorax.rotation.w,
                thorax.angularVelocity.x, thorax.angularVelocity.y, thorax.angularVelocity.z,
                adapter.CanonicalPostureErrorRad, adapter.CanonicalPostureWorstJoint, adapter.CanonicalPostureLimitProximity,
                adapter.CanonicalPostureUnexpectedMarginConsumed, adapter.MaxDriveSaturation,
                s.MaxLoadBearingDemand, s.MaxLoadBearingDemandJoint, s.MaxLoadBearingErrorRad, s.MaxLoadBearingErrorJoint,
                b.SystemCom.x, b.SystemCom.y, b.SystemCom.z, b.SystemComVelocity.x, b.SystemComVelocity.y, b.SystemComVelocity.z,
                b.SystemMassKg, b.ComHeightM, b.Omega, b.HasCopEstimate,
                b.HasCopEstimate ? b.CopEstimate.x : float.NaN, b.HasCopEstimate ? b.CopEstimate.z : float.NaN,
                b.TotalNormalImpulse, b.CaptureAp, b.CaptureMl, b.CaptureMarginFront, b.CaptureMarginRear,
                b.ComApMarginFront, b.ComApMarginRear, b.SupportApMin, b.SupportApMax, b.SupportMlMin, b.SupportMlMax,
                b.SupportContactCount, b.HasSupport,
                c.IsActive, c.ComRefAp, c.ComApError, c.DesiredComAccelerationAp, c.CopDesiredAp, c.CopDesiredApUnclamped,
                c.CopMeasuredAp, c.CopErrorAp, c.HasCopMeasurement, c.RawAnkleSagittalOffsetRad, c.RawAnkleAuthorityFraction,
                c.AnkleSagittalOffsetRad, c.AnkleAuthorityFraction, c.IsAnkleOffsetSaturated, c.PostureGuardScale,
                c.PostureGuardEnabled, c.PostureErrorRad, c.PostureErrorRateRadPerS, c.PostureLimitProximity,
                c.UnexpectedMarginConsumedFraction, c.HipTrunkStrategyEnabled, c.HipStrategyBlend, c.HipSagittalOffsetRad,
                c.TrunkSagittalOffsetRad, c.AnkleFrontalOffsetRad, c.HipFrontalOffsetRad, c.RequestedAnkleTorqueNm,
                s.LeftContacts, s.RightContacts,
                _controller.LeftFootContact != null && _controller.LeftFootContact.IsInContact,
                _controller.RightFootContact != null && _controller.RightFootContact.IsInContact,
                s.LeftNormalImpulse, s.RightNormalImpulse, s.LeftSlip, s.RightSlip,
                s.LeftProbeContacts, s.RightProbeContacts, s.PlantarProbeNormalImpulse, s.PlantarProbeTangentialImpulse,
                s.NonPlantarGroundBodies, s.BarGroundContacts,
                s.Com.HullPointCount, s.Com.HullAreaM2, s.Com.AabbAreaM2,
                s.Com.HullSignedMarginM, s.Com.AabbSignedMarginM, s.Com.AabbApSignedMarginM,
                s.Cop.HullSignedMarginM, s.Cop.AabbSignedMarginM, s.Cop.AabbApSignedMarginM,
                s.Capture.HullSignedMarginM, s.Capture.AabbSignedMarginM, s.Capture.AabbApSignedMarginM,
                bar.position.x, bar.position.y, bar.position.z, bar.rotation.x, bar.rotation.y, bar.rotation.z, bar.rotation.w,
                bar.linearVelocity.x, bar.linearVelocity.y, bar.linearVelocity.z,
                bar.angularVelocity.x, bar.angularVelocity.y, bar.angularVelocity.z, bar.mass,
                saddle.IsAttached, saddle.IsBroken, sep.x, sep.y, sep.z, saddle.SaddleSeparationMeters,
                sepBar.x, sepBar.y, sepBar.z, saddle.CurrentLinearLimitOccupancy,
                saddle.CurrentAngularXLimitOccupancy, saddle.CurrentAngularYLimitOccupancy, saddle.CurrentAngularZLimitOccupancy,
                saddle.RelativeRotationDegrees, force.x, force.y, force.z, torque.x, torque.y, torque.z,
                s.BarThoraxCallbacks, barContact == null ? 0 : barContact.CompletedContactCount,
                barImpulse.x, barImpulse.y, barImpulse.z,
                barContact == null ? float.NaN : barContact.CompletedMinimumSeparationM,
                s.BarThoraxPenetrationM, s.BarThoraxGapM, s.BarAthleteBodies, s.BarAthleteContacts,
                s.BarAthleteImpulse.x, s.BarAthleteImpulse.y, s.BarAthleteImpulse.z, modeChange);

            foreach (string jointId in PoweredJointIds)
            {
                PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointId);
                if (joint == null || !joint.HasPostPhysicsDiagnostic)
                {
                    for (int index = 0; index < 20; index++)
                        csv.Append("NA,");
                    continue;
                }
                PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                Vector3 applied = RotationVector(d.AppliedTarget);
                Vector3 actual = RotationVector(d.ActualRelative);
                AppendValues(csv, true,
                    applied.x, applied.y, applied.z, actual.x, actual.y, actual.z,
                    d.ErrorRad.x, d.ErrorRad.y, d.ErrorRad.z, d.ErrorRad.magnitude,
                    d.TargetAngularVelocityRadS.x, d.ActualAngularVelocityRadS.x, d.ActualAngularVelocityRadS.y,
                    d.ActualAngularVelocityRadS.z, d.MaximumForceNm, d.ModeledDemand, d.LimitProximity,
                    d.SolverTorqueJointSpaceNm.x, d.SolverTorqueJointSpaceNm.y, d.SolverTorqueJointSpaceNm.z);
            }

            var flags = new List<string>(CanonicalEvents.Length + SupplementaryEvents.Length);
            foreach (string name in CanonicalEvents)
                flags.Add(signals[name] ? "1" : "0");
            foreach (string name in SupplementaryEvents)
                flags.Add(signals[name] ? "1" : "0");
            csv.AppendLine(string.Join(",", flags));
        }

        private static Vector3 RotationVector(Quaternion value)
        {
            float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (magnitude <= 1e-6f)
                return Vector3.zero;
            value = new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude);
            if (value.w < 0f)
                value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
            float vectorMagnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z);
            if (vectorMagnitude <= 1e-6f)
                return new Vector3(value.x, value.y, value.z) * 2f;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(value.w, -1f, 1f));
            return new Vector3(value.x, value.y, value.z) * (angle / vectorMagnitude);
        }

        private static string PredicateDocument() =>
            PredicateVersion + "\n" +
            "Sampling: one post-physics row per authoritative 0.01 s tick; fresh scene per load; SETUP hold at s_q=0.\n" +
            $"Onset: first tick of the first run of {ConsecutiveTicks} consecutive true samples.\n" +
            $"DRIVE_HIGH: max load-bearing modeled demand >= {DriveHighDemand:0.00} (diagnostic threshold).\n" +
            $"DRIVE_SATURATION: max load-bearing modeled demand >= {DriveSaturationDemand:0.00} (PoweredJointController contract).\n" +
            $"TRACKING_FAILURE: max load-bearing |applied target - actual| >= {TrackingFailureRad * Mathf.Rad2Deg:0.0} deg.\n" +
            $"POSTURE_DEPARTURE: canonical posture error >= {PostureJointErrorRad * Mathf.Rad2Deg:0.0} deg OR |trunk pitch| >= {PostureTrunkPitchRad:0.00} rad OR pelvis y <= {PosturePelvisHeightM:0.00} m (Stage-A contract).\n" +
            $"CAPTURE_DEPARTURE: plantar support present AND min(AP capture margin front, rear) <= {CaptureMarginM:0.00} m (production AABB proxy; Stage-A contract).\n" +
            "SUPPORT_LOSS: no plantar contact on either foot (production HasSupport false).\n" +
            $"SADDLE_LINEAR_LIMIT: saddle attached AND anchor separation / linear limit >= {SaddleLinearOccupancy:0.00}.\n" +
            $"SADDLE_GROSS_FAILURE: saddle IsBroken (detached or separation > {SquatBarSaddle.MaxPlausibleSeparationM:0.00} m).\n" +
            $"CONTACT_MODE_CHANGE: per-foot plantar contact count differs from the first bilateral-contact reference, OR foot slip > {FootSlipMps:0.00} m/s, OR any bar/thorax collision callback, OR any bar/athlete-body collision, OR any non-plantar athlete body touching the platform.\n" +
            $"Supplementary: GUARD_WITHDRAWAL guard scale <= {GuardWithdrawalScale:0.00}; CAPTURE_DEPARTURE_HULL exact hull capture margin <= {CaptureMarginM:0.00} m; COM_OUTSIDE_HULL COM hull margin <= 0.\n" +
            "Joint currentForce/currentTorque are engine diagnostics, not biological forces.\n";

        private static string OnsetText(ulong? tick) =>
            tick.HasValue ? tick.Value.ToString(CultureInfo.InvariantCulture) : "NA";

        private static ulong? OnsetTick(OnsetTracker tracker) =>
            tracker.HasOnset ? (ulong?)tracker.FirstOnsetTick : null;

        private static void WriteSummary(string fileName, List<AuditResult> summaries)
        {
            var csv = new StringBuilder();
            csv.AppendLine(AuditResult.Header);
            foreach (AuditResult result in summaries)
                csv.AppendLine(result.ToCsv());
            WriteText(fileName, csv.ToString());
        }

        private static float[] RequestedLoads()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_CAUSAL_LOADS");
            if (string.IsNullOrWhiteSpace(text))
                return (float[])CoreLoadsKg.Clone();
            string[] tokens = text.Split(':');
            var loads = new float[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
                loads[index] = ParseFloat(tokens[index]);
            return loads;
        }

        private static float ParseFloat(string text) => float.Parse(text, CultureInfo.InvariantCulture);

        private static bool ParseBool(string text) => string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);

        private static string SafeLabel(string text)
        {
            var builder = new StringBuilder(text.Length);
            foreach (char character in text)
                builder.Append(char.IsLetterOrDigit(character) || character == '.' || character == '-' ? character : '_');
            return builder.ToString();
        }

        private static string OutputDirectory
        {
            get
            {
                string configured = Environment.GetEnvironmentVariable("GAM13_CAUSAL_OUTPUT_DIR");
                string directory = string.IsNullOrWhiteSpace(configured)
                    ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-13", "causal-audit"))
                    : configured;
                Directory.CreateDirectory(directory);
                return directory;
            }
        }

        private static void WriteText(string fileName, string content) =>
            File.WriteAllText(Path.Combine(OutputDirectory, fileName), content);

        private static void AppendRow(StringBuilder csv, params object[] values) => AppendValues(csv, false, values);

        private static void AppendValues(StringBuilder csv, bool trailingComma, params object[] values)
        {
            for (int index = 0; index < values.Length; index++)
            {
                if (index > 0)
                    csv.Append(',');
                csv.Append(Format(values[index]));
            }
            if (trailingComma)
                csv.Append(',');
            else
                csv.AppendLine();
        }

        private static string Format(object value)
        {
            switch (value)
            {
                case null:
                    return "NA";
                case float f:
                    return float.IsNaN(f) ? "NA" : f.ToString("R", CultureInfo.InvariantCulture);
                case double d:
                    return double.IsNaN(d) ? "NA" : d.ToString("R", CultureInfo.InvariantCulture);
                case bool b:
                    return b ? "true" : "false";
                case string text:
                    return text.Length == 0 ? "NONE" : text.Replace(',', '|');
                default:
                    return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }

        /// <summary>One post-physics measurement, computed once per tick.</summary>
        private sealed class TickSample
        {
            public TickSample(
                GAM13CausalAuditTests owner,
                SquatObservationSnapshot snapshot,
                SquatPhysicalAdapter adapter,
                SquatBarSaddle saddle,
                Rigidbody barBody)
            {
                Balance = adapter.Balance;
                Control = adapter.BalanceController;

                float maxDemand = 0f;
                string demandJoint = "NONE";
                float maxError = 0f;
                string errorJoint = "NONE";
                foreach (string jointId in LoadBearingJointIds)
                {
                    PoweredJointController.PoweredJointRuntime joint = owner._rig.PoweredController.GetJoint(jointId);
                    if (joint == null || !joint.HasPostPhysicsDiagnostic)
                        continue;
                    PoweredJointDiagnostic diagnostic = joint.PostPhysicsDiagnostic;
                    if (diagnostic.ModeledDemand > maxDemand)
                    {
                        maxDemand = diagnostic.ModeledDemand;
                        demandJoint = jointId;
                    }
                    float error = diagnostic.ErrorRad.magnitude;
                    if (error > maxError)
                    {
                        maxError = error;
                        errorJoint = jointId;
                    }
                }
                MaxLoadBearingDemand = maxDemand;
                MaxLoadBearingDemandJoint = demandJoint;
                MaxLoadBearingErrorRad = maxError;
                MaxLoadBearingErrorJoint = errorJoint;

                PhysicalFootContactDetector left = owner._controller.LeftFootContact;
                PhysicalFootContactDetector right = owner._controller.RightFootContact;
                LeftContacts = left == null ? 0 : left.CompletedContactCount;
                RightContacts = right == null ? 0 : right.CompletedContactCount;
                LeftSlip = left == null ? float.NaN : left.SlipSpeed;
                RightSlip = right == null ? float.NaN : right.SlipSpeed;
                LeftNormalImpulse = SumNormal(left);
                RightNormalImpulse = SumNormal(right);

                var plantar = new List<Vector3>(LeftContacts + RightContacts);
                AddContacts(left, plantar);
                AddContacts(right, plantar);
                Com = SquatSupportGeometry.Measure(plantar, new Vector2(Balance.SystemCom.x, Balance.SystemCom.z));
                Cop = Balance.HasCopEstimate
                    ? SquatSupportGeometry.Measure(plantar, new Vector2(Balance.CopEstimate.x, Balance.CopEstimate.z))
                    : SquatSupportGeometry.Measure(plantar, new Vector2(float.NaN, float.NaN));
                Capture = SquatSupportGeometry.Measure(plantar, new Vector2(Balance.CaptureMl, Balance.CaptureAp));

                owner._probes.TryGetValue("left_foot", out GAM13GroundContactProbe leftProbe);
                owner._probes.TryGetValue("right_foot", out GAM13GroundContactProbe rightProbe);
                LeftProbeContacts = leftProbe == null ? 0 : leftProbe.CompletedPlatformCount;
                RightProbeContacts = rightProbe == null ? 0 : rightProbe.CompletedPlatformCount;
                float normal = 0f;
                Vector3 tangential = Vector3.zero;
                AccumulateProbe(leftProbe, ref normal, ref tangential);
                AccumulateProbe(rightProbe, ref normal, ref tangential);
                PlantarProbeNormalImpulse = normal;
                PlantarProbeTangentialImpulse = tangential.magnitude;

                var nonPlantar = new List<string>();
                int barGround = 0;
                var barAthlete = new List<string>();
                int barAthleteContacts = 0;
                Vector3 barAthleteImpulse = Vector3.zero;
                foreach (KeyValuePair<string, GAM13GroundContactProbe> pair in owner._probes)
                {
                    GAM13GroundContactProbe probe = pair.Value;
                    if (probe.CompletedCount == 0)
                        continue;
                    if (pair.Key == "barbell")
                    {
                        barGround = probe.CompletedPlatformCount;
                        for (int index = 0; index < probe.CompletedCount; index++)
                        {
                            GAM13GroundContactProbe.Sample contact = probe.Completed(index);
                            if (contact.IsPlatform)
                                continue;
                            barAthleteContacts++;
                            barAthleteImpulse += contact.Impulse;
                            if (!barAthlete.Contains(contact.Other))
                                barAthlete.Add(contact.Other);
                        }
                    }
                    else if (pair.Key != "left_foot" && pair.Key != "right_foot" && probe.CompletedPlatformCount > 0)
                    {
                        nonPlantar.Add(pair.Key);
                    }
                }
                nonPlantar.Sort(StringComparer.Ordinal);
                barAthlete.Sort(StringComparer.Ordinal);
                NonPlantarGroundBodies = string.Join("|", nonPlantar);
                BarGroundContacts = barGround;
                BarAthleteBodies = string.Join("|", barAthlete);
                BarAthleteContacts = barAthleteContacts;
                BarAthleteImpulse = barAthleteImpulse;

                SquatBarThoraxContactDetector barContact = saddle.ThoraxContact;
                BarThoraxCallbacks = barContact == null ? 0 : barContact.CompletedCollisionCallbackCount;
                MeasureBarThoraxGeometry(saddle, barBody, out float penetration, out float gap);
                BarThoraxPenetrationM = penetration;
                BarThoraxGapM = gap;

                Finite = snapshot.Bar.IsAvailable &&
                    float.IsFinite(snapshot.Bar.PositionWorldMeters.X) &&
                    float.IsFinite(snapshot.Bar.PositionWorldMeters.Y) &&
                    float.IsFinite(snapshot.Bar.PositionWorldMeters.Z) &&
                    float.IsFinite(Balance.SystemCom.x) && float.IsFinite(Balance.SystemCom.y) &&
                    float.IsFinite(Balance.SystemCom.z) && float.IsFinite(saddle.SaddleSeparationMeters) &&
                    PoweredJointController.IsFinite(saddle.CurrentForceEngine) &&
                    PoweredJointController.IsFinite(saddle.CurrentTorqueEngine);
            }

            public SquatBalanceObserver Balance { get; }
            public SquatPredictiveBalanceController Control { get; }
            public float MaxLoadBearingDemand { get; }
            public string MaxLoadBearingDemandJoint { get; }
            public float MaxLoadBearingErrorRad { get; }
            public string MaxLoadBearingErrorJoint { get; }
            public int LeftContacts { get; }
            public int RightContacts { get; }
            public float LeftSlip { get; }
            public float RightSlip { get; }
            public float LeftNormalImpulse { get; }
            public float RightNormalImpulse { get; }
            public SquatSupportGeometry.Measurement Com { get; }
            public SquatSupportGeometry.Measurement Cop { get; }
            public SquatSupportGeometry.Measurement Capture { get; }
            public int LeftProbeContacts { get; }
            public int RightProbeContacts { get; }
            public float PlantarProbeNormalImpulse { get; }
            public float PlantarProbeTangentialImpulse { get; }
            public string NonPlantarGroundBodies { get; }
            public int BarGroundContacts { get; }
            public string BarAthleteBodies { get; }
            public int BarAthleteContacts { get; }
            public Vector3 BarAthleteImpulse { get; }
            public int BarThoraxCallbacks { get; }
            public float BarThoraxPenetrationM { get; }
            public float BarThoraxGapM { get; }
            public bool Finite { get; }

            private static float SumNormal(PhysicalFootContactDetector detector)
            {
                if (detector == null)
                    return 0f;
                float sum = 0f;
                for (int index = 0; index < detector.CompletedContactCount; index++)
                    sum += detector.CompletedNormalImpulse(index);
                return sum;
            }

            private static void AddContacts(PhysicalFootContactDetector detector, List<Vector3> contacts)
            {
                if (detector == null)
                    return;
                for (int index = 0; index < detector.CompletedContactCount; index++)
                    contacts.Add(detector.CompletedContactPoint(index));
            }

            private static void AccumulateProbe(GAM13GroundContactProbe probe, ref float normal, ref Vector3 tangential)
            {
                if (probe == null)
                    return;
                for (int index = 0; index < probe.CompletedCount; index++)
                {
                    GAM13GroundContactProbe.Sample sample = probe.Completed(index);
                    if (!sample.IsPlatform)
                        continue;
                    float along = Vector3.Dot(sample.Impulse, sample.Normal);
                    normal += Mathf.Abs(along);
                    tangential += sample.Impulse - along * sample.Normal;
                }
            }

            /// <summary>
            /// Read-only geometric query between the bar shaft and the thorax
            /// box. Penetration is PhysX's own depenetration distance; the gap
            /// is the distance from the shaft axis point nearest the thorax to
            /// the box surface, minus the shaft radius.
            /// </summary>
            private static void MeasureBarThoraxGeometry(
                SquatBarSaddle saddle,
                Rigidbody barBody,
                out float penetration,
                out float gap)
            {
                penetration = float.NaN;
                gap = float.NaN;
                CapsuleCollider shaft = barBody.GetComponent<CapsuleCollider>();
                BoxCollider thoraxBox = saddle.ThoraxBody.GetComponent<BoxCollider>();
                if (shaft == null || thoraxBox == null)
                    return;

                Transform shaftTransform = shaft.transform;
                Transform thoraxTransform = thoraxBox.transform;
                bool overlapping = Physics.ComputePenetration(
                    shaft, shaftTransform.position, shaftTransform.rotation,
                    thoraxBox, thoraxTransform.position, thoraxTransform.rotation,
                    out _, out float distance);
                penetration = overlapping ? distance : 0f;

                Vector3 thoraxCenterWorld = thoraxTransform.TransformPoint(thoraxBox.center);
                Vector3 local = shaftTransform.InverseTransformPoint(thoraxCenterWorld);
                float halfSegment = Mathf.Max(0f, 0.5f * shaft.height - shaft.radius);
                Vector3 axisPoint = shaftTransform.TransformPoint(
                    shaft.center + new Vector3(Mathf.Clamp(local.x - shaft.center.x, -halfSegment, halfSegment), 0f, 0f));
                Vector3 closest = Physics.ClosestPoint(axisPoint, thoraxBox, thoraxTransform.position, thoraxTransform.rotation);
                float axisDistance = Vector3.Distance(closest, axisPoint);
                gap = overlapping ? -distance : axisDistance - shaft.radius;
            }
        }

        private sealed class ContactModeReference
        {
            private bool _hasReference;
            private int _left;
            private int _right;

            public string LastReason { get; private set; } = "NONE";

            public void Observe(TickSample sample)
            {
                if (_hasReference || sample.LeftContacts == 0 || sample.RightContacts == 0)
                    return;
                _left = sample.LeftContacts;
                _right = sample.RightContacts;
                _hasReference = true;
            }

            public string ChangeReason(TickSample sample)
            {
                var reasons = new List<string>(4);
                if (_hasReference && (sample.LeftContacts != _left || sample.RightContacts != _right))
                    reasons.Add($"PLANTAR_COUNT_{sample.LeftContacts}_{sample.RightContacts}_FROM_{_left}_{_right}");
                if (sample.LeftSlip > FootSlipMps || sample.RightSlip > FootSlipMps)
                    reasons.Add("FOOT_SLIP");
                if (sample.BarThoraxCallbacks > 0)
                    reasons.Add("BAR_THORAX_CONTACT");
                if (sample.BarAthleteBodies.Length > 0)
                    reasons.Add("BAR_ATHLETE:" + sample.BarAthleteBodies);
                if (sample.NonPlantarGroundBodies.Length > 0)
                    reasons.Add("NONPLANTAR_GROUND:" + sample.NonPlantarGroundBodies);
                string reason = reasons.Count == 0 ? "NONE" : string.Join("+", reasons);
                if (reason != "NONE")
                    LastReason = reason;
                return reason;
            }
        }

        private sealed class StageAAccumulator
        {
            private int _measured;
            private int _saturated;
            private float _settlePelvis = float.NaN;
            private float _minPelvis = float.PositiveInfinity;
            private float _maxTrunk;
            private float _maxFootPitch;
            private float _minCapture = float.PositiveInfinity;
            private float _maxComSpeed;
            private float _maxPostureDeg;
            private float _maxLimit;
            private float _minGuard = 1f;
            private float _maxAnkleDeg;
            private float _maxSaddle;
            private int _minContacts = int.MaxValue;
            private bool _supportLost;
            private bool _saddleUnstable;

            public bool Upright { get; private set; }
            public bool Pass { get; private set; }

            public void Observe(
                int tick,
                TickSample sample,
                SquatPhysicalAdapter adapter,
                SquatBarSaddle saddle,
                float footPitchDeg,
                float pelvisY,
                float trunkPitchRad)
            {
                if (tick < StageASettleTicks)
                    return;
                SquatBalanceObserver balance = sample.Balance;
                _measured++;
                if (tick == StageASettleTicks)
                    _settlePelvis = pelvisY;
                _minPelvis = Mathf.Min(_minPelvis, pelvisY);
                _maxTrunk = Mathf.Max(_maxTrunk, Mathf.Abs(trunkPitchRad));
                _maxFootPitch = Mathf.Max(_maxFootPitch, Mathf.Abs(footPitchDeg));
                _minCapture = Mathf.Min(_minCapture, Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                _maxComSpeed = Mathf.Max(_maxComSpeed,
                    new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude);
                _maxPostureDeg = Mathf.Max(_maxPostureDeg, adapter.CanonicalPostureErrorRad * Mathf.Rad2Deg);
                _maxLimit = Mathf.Max(_maxLimit, adapter.CanonicalPostureLimitProximity);
                _minGuard = Mathf.Min(_minGuard, sample.Control.PostureGuardScale);
                _maxAnkleDeg = Mathf.Max(_maxAnkleDeg, Mathf.Abs(sample.Control.AnkleSagittalOffsetRad) * Mathf.Rad2Deg);
                _maxSaddle = Mathf.Max(_maxSaddle, saddle.SaddleSeparationMeters);
                _minContacts = Mathf.Min(_minContacts, balance.SupportContactCount);
                _supportLost |= !balance.HasSupport;
                _saddleUnstable |= saddle.IsBroken || !saddle.IsAttached;
                if (adapter.MaxDriveSaturation >= 1f)
                    _saturated++;
                Upright = _minPelvis > PosturePelvisHeightM && _maxTrunk < PostureTrunkPitchRad;
                Pass = _measured == AuditTicks - StageASettleTicks && !_supportLost && !_saddleUnstable && Upright &&
                    _minCapture > CaptureMarginM && _maxComSpeed < StageAMaxComSpeedMps &&
                    _maxFootPitch < StageAMaxFootPitchDeg &&
                    _saturated / (float)_measured < StageAMaxSaturationFraction &&
                    _maxPostureDeg < StageAMaxPostureErrorDeg && _maxLimit < StageAMaxLimitProximity &&
                    _maxSaddle < StageAMaxSaddleSeparationM;
            }

            public string Summary(float loadKg) => string.Format(
                CultureInfo.InvariantCulture,
                "load={0:F0} measured={1} settlePelvis={2:F4} minPelvis={3:F4} maxTrunk={4:F4} " +
                "capture={5:F4} comSpeed={6:F4} posture={7:F3} limit={8:F3} guard={9:F3} " +
                "ankle={10:F3} saddle={11:F4} contacts={12} sat={13:F3} supportLost={14} " +
                "saddleUnstable={15} upright={16} pass={17}",
                loadKg, _measured, _settlePelvis, _minPelvis, _maxTrunk, _minCapture, _maxComSpeed,
                _maxPostureDeg, _maxLimit, _minGuard, _maxAnkleDeg, _maxSaddle, _minContacts,
                _measured == 0 ? 1f : _saturated / (float)_measured, _supportLost, _saddleUnstable, Upright, Pass);
        }

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

        private sealed class AuditResult
        {
            public float LoadKg;
            public int PositionIterations;
            public int VelocityIterations;
            public int IslandPositionIterations;
            public int IslandVelocityIterations;
            public string Intervention;
            public string ContactModeReason;
            public int BarThoraxCallbackTicks;
            public float MaxBarThoraxPenetrationM;
            public bool ConnectedBodyCollisionEnabled;
            public float BarToThoraxMassRatio;
            public bool NonFinite;
            public string StageASummary;
            public bool StageAPass;
            public bool Upright;
            public readonly Dictionary<string, ulong?> Onsets = new Dictionary<string, ulong?>(StringComparer.Ordinal);

            public static string Header
            {
                get
                {
                    var columns = new List<string>
                    {
                        "load_kg", "athlete_position_iterations", "athlete_velocity_iterations",
                        "island_position_iterations", "island_velocity_iterations", "intervention"
                    };
                    foreach (string name in CanonicalEvents)
                        columns.Add(name.ToLowerInvariant() + "_onset_tick");
                    foreach (string name in SupplementaryEvents)
                        columns.Add(name.ToLowerInvariant() + "_onset_tick");
                    columns.AddRange(new[]
                    {
                        "canonical_first_onset_order", "contact_mode_reason", "bar_thorax_callback_ticks",
                        "max_bar_thorax_penetration_m", "connected_body_collision_enabled", "bar_to_thorax_mass_ratio",
                        "non_finite", "stage_a_pass", "upright", "stage_a_summary"
                    });
                    return string.Join(",", columns);
                }
            }

            public string CanonicalOrder()
            {
                var events = new List<KeyValuePair<string, ulong>>();
                foreach (string name in CanonicalEvents)
                {
                    if (Onsets.TryGetValue(name, out ulong? tick) && tick.HasValue)
                        events.Add(new KeyValuePair<string, ulong>(name, tick.Value));
                }
                events.Sort((left, right) =>
                {
                    int byTick = left.Value.CompareTo(right.Value);
                    return byTick != 0
                        ? byTick
                        : Array.IndexOf(CanonicalEvents, left.Key).CompareTo(Array.IndexOf(CanonicalEvents, right.Key));
                });
                if (events.Count == 0)
                    return "NONE";
                var parts = new List<string>(events.Count);
                foreach (KeyValuePair<string, ulong> item in events)
                    parts.Add(item.Key + "@" + item.Value.ToString(CultureInfo.InvariantCulture));
                return string.Join(">", parts);
            }

            public string ToCsv()
            {
                var values = new List<string>
                {
                    Format(LoadKg),
                    PositionIterations.ToString(CultureInfo.InvariantCulture),
                    VelocityIterations.ToString(CultureInfo.InvariantCulture),
                    IslandPositionIterations.ToString(CultureInfo.InvariantCulture),
                    IslandVelocityIterations.ToString(CultureInfo.InvariantCulture),
                    Format(Intervention)
                };
                foreach (string name in CanonicalEvents)
                    values.Add(OnsetText(Onsets.TryGetValue(name, out ulong? tick) ? tick : null));
                foreach (string name in SupplementaryEvents)
                    values.Add(OnsetText(Onsets.TryGetValue(name, out ulong? tick) ? tick : null));
                values.Add(CanonicalOrder());
                values.Add(Format(ContactModeReason));
                values.Add(BarThoraxCallbackTicks.ToString(CultureInfo.InvariantCulture));
                values.Add(Format(MaxBarThoraxPenetrationM));
                values.Add(Format(ConnectedBodyCollisionEnabled));
                values.Add(Format(BarToThoraxMassRatio));
                values.Add(Format(NonFinite));
                values.Add(Format(StageAPass));
                values.Add(Format(Upright));
                values.Add("\"" + StageASummary + "\"");
                return string.Join(",", values);
            }
        }
    }
}
