using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    public sealed class DeadliftEndpointTests
    {
        private DeadliftPlantWorld _world;
        private static readonly string[] Feet = { "left_foot", "right_foot" };

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
                yield return _world.Teardown();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator GAM70_SUSPENDED_LOCKOUT_ENDPOINT()
        {
            var report = new StringBuilder();
            yield return Probe("stock-static", false, false, report);
            yield return Probe("stock-cop", false, true, report);
            yield return Probe("close-static", true, false, report);
            yield return Probe("close-velocity-only", true, true, report, positionAnchor: false);
            yield return Probe("close-cop", true, true, report);
            yield return Probe("lat-cop", true, true, report, lat: true);
            yield return Probe("hips-cop", true, true, report, hips: true);
            F.WriteEvidence("lockout-endpoint.txt", report.ToString());
        }

        private IEnumerator Probe(string name, bool close, bool copControl, StringBuilder report, bool positionAnchor = true, bool lat = false, bool hips = false)
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var rig = _world.Rig;
            var solver = new DeadliftSetupSolver(rig);
            var setup = solver.BuildModel(DeadliftQualifiedSetup.B2, DeadliftQualifiedSetup.SumoPronated());
            DeadliftAnkleRoll.Enable(rig);
            var path = new DeadliftLiftPath(rig, solver, setup, DeadliftQualifiedSetup.BarKg) { BarProximity = close, LatEngagement = lat, HipsThroughEngagement = hips };
            var key = path.SolveEndpoint();
            if (hips)
                Debug.Log("GAM-70 hips-through solve: " + path.HipsThroughReport);
            Pose root = solver.PelvisPoseOf(key.X);
            var g = path.GeometryOf(key);
            var poses = solver.Kinematics.Solve(root, key.Logical);
            var endpoint = new DeadliftPlantModel
            {
                Spec = setup.Spec, Poses = poses, PelvisWorld = root, BarCenterWorld = g.BarCenter,
                LeftGripAnchorLocal = setup.LeftGripAnchorLocal, RightGripAnchorLocal = setup.RightGripAnchorLocal
            };
            foreach (var joint in key.Logical)
                endpoint.SetupLogical.Add(joint.Key, joint.Value);
            float footResidual = Feet.Max(id => Vector3.Distance(poses[id].position, setup.Poses[id].position));
            float footAngleResidual = Feet.Max(id => Quaternion.Angle(poses[id].rotation, setup.Poses[id].rotation));
            Vector3 gripShift = g.BarCenter - setup.BarCenterWorld;
            float gripResidual = Mathf.Max(Vector3.Distance(g.LeftAnchor, setup.LeftGripWorld + gripShift),
                Vector3.Distance(g.RightAnchor, setup.RightGripWorld + gripShift));
            float gripAngleResidual = new[] { "left_hand", "right_hand" }.Max(id =>
                Quaternion.Angle(poses[id].rotation, setup.Poses[id].rotation));
            Assert.That(footResidual, Is.LessThan(0.002f), report.ToString());
            Assert.That(gripResidual, Is.LessThan(0.002f));
            Assert.That(gripAngleResidual, Is.LessThan(0.5f));
            Assert.That(footAngleResidual, Is.LessThan(0.5f));

            _world.UseModel(endpoint);
            _world.PlaceAtSetup(DeadliftQualifiedSetup.BarKg, true);
            Assert.That(_world.TickZeroMaxPenetrationM, Is.LessThan(0.002f));
            Assert.That(_world.TickZeroSelfPenetrations, Is.Empty, report.ToString());
            var bar = _world.Bar.Body;
            var observer = new DeadliftAttemptObserver(rig, bar, () => 0f, () => 0f, () => 0f,
                () => _world.LeftGrip.IsAttached, () => _world.RightGrip.IsAttached);
            // Visible-mesh check: the gap the owner sees (skinned thigh surface) against the
            // physical capsule gap the plan and the referee use, at the placed endpoint.
            float meshThigh = float.NaN;
            var visible = new DeadliftBarProximityTests.VisibleLegs(rig);
            if (visible.Available)
            {
                DeadliftBarLegGeometry.LiveCapsule(DeadliftBarLegGeometry.Shaft(bar), out Vector3 s0, out Vector3 s1, out float shaftRadius);
                visible.Measure(0.5f * (s0 + s1), shaftRadius, 0.5f * (s1 - s0).magnitude + shaftRadius, out float meshShin, out meshThigh);
                report.AppendLine(FormattableString.Invariant(
                    $"{name} visible thigh mesh gap {meshThigh * 1000f:F2} mm vs collider {g.Gaps.Thigh * 1000f:F2} mm, mismatch {(g.Gaps.Thigh - meshThigh) * 1000f:F2} mm (mesh nearer the bar)"));
            }
            report.AppendLine(FormattableString.Invariant(
                $"{name} planned bar {g.BarCenter:F5} thigh gap {g.Gaps.Thigh * 1000f:F2} mm, deltoid behind bar {observer.DeltoidBehindBarM * 1000f:F1} mm, COM z {key.SystemComZ:F5} midfoot {path.MidfootZ:F5}, torso {key.TorsoDeg:F1} deg, knee {key.KneeDeg:F1} deg, hip Z {g.HipZ:F5}, shoulder Z {g.ShoulderZ:F5}, feet closure {1000f * footResidual:F3} mm/{footAngleResidual:F3} deg, grip closure {1000f * gripResidual:F3} mm/{gripAngleResidual:F3} deg, roll {g.RollLeftDeg:F3}/{g.RollRightDeg:F3}"));
            var statics = DeadliftContactStatics.SolveSuspended(rig, bar, g.LeftAnchor, g.RightAnchor);
            report.AppendLine(statics.Describe());
            Vector3 handUp = Vector3.up * statics.HandPullN;
            Vector3 barForce = 2f * handUp + bar.mass * Physics.gravity;
            Vector3 barMoment = Vector3.Cross(g.LeftAnchor - bar.worldCenterOfMass, handUp)
                + Vector3.Cross(g.RightAnchor - bar.worldCenterOfMass, handUp) - 2f * statics.HandCoupleOnEachHandNm;
            Vector3 systemForce = bar.mass * Physics.gravity, systemMoment = Vector3.Cross(bar.worldCenterOfMass, systemForce);
            foreach (var seg in rig.Segments.Values)
            {
                Vector3 weight = seg.Body.mass * Physics.gravity;
                systemForce += weight;
                systemMoment += Vector3.Cross(seg.Body.worldCenterOfMass, weight);
            }
            Vector3 fL = statics.FootLN * Vector3.up, fR = statics.FootRN * Vector3.up;
            systemMoment += Vector3.Cross(statics.CopL, fL) + Vector3.Cross(statics.CopR, fR);
            systemForce += fL + fR;
            Assert.That(barForce.magnitude + systemForce.magnitude, Is.LessThan(0.01f));
            Assert.That(barMoment.magnitude + systemMoment.magnitude, Is.LessThan(0.01f));
            report.AppendLine(FormattableString.Invariant($"Explicit static residual bar {barForce.magnitude:F6} N/{barMoment.magnitude:F6} N m, system {systemForce.magnitude:F6} N/{systemMoment.magnitude:F6} N m; grip couples on hands {statics.HandCoupleOnEachHandNm:F4} N m."));
            Assert.That(statics.Feasible, Is.True, statics.Describe());
            if (close)
            {
                // Plan + the regulator's clamp + the joint margin inside the physical limit.
                float rollPlanEnvelope = DeadliftAnkleRoll.LimitDegrees - path.JointMarginDeg - DeadliftBalanceRegulator.HipsThroughMaxRollOffsetDeg;
                Assert.That(Mathf.Abs(g.RollLeftDeg), Is.LessThanOrEqualTo(rollPlanEnvelope),
                    $"planned roll {g.RollLeftDeg:F3} deg leaves less than the regulator's clamp inside the limit");
                Assert.That(Mathf.Abs(g.RollRightDeg), Is.LessThanOrEqualTo(rollPlanEnvelope),
                    $"planned roll {g.RollRightDeg:F3} deg leaves less than the regulator's clamp inside the limit");
            }

            _world.Hold.SetStaticReference(rig, statics.DriveTorquesAxes());
            if (copControl)
            {
                _world.Hold.Regulator = new DeadliftBalanceRegulator(rig, rig.PlatformCollider)
                {
                    RollLoop = true, ContactConsistentControl = true, ComPositionAnchor = positionAnchor,
                    CarriedBar = bar, IntegralLeakSeconds = 0.10f,
                    // The hips-through hold runs under the clamp re-derived for its planned roll.
                    MaxRollOffsetDeg = path.HipsThroughEngagement
                        ? DeadliftBalanceRegulator.HipsThroughMaxRollOffsetDeg : DeadliftBalanceRegulator.DefaultMaxRollOffsetDeg,
                    FootCopTarget = id => id == "left_foot" ? statics.CopL : statics.CopR
                };
            }
            rig.PrimeCommandSource();

            var shaftSensor = bar.gameObject.AddComponent<DeadliftBarSensor>();
            shaftSensor.Ground = rig.PlatformCollider;
            foreach (string limb in new[] { "left_thigh", "right_thigh", "left_shank", "right_shank" })
                shaftSensor.Legs.Add(rig.Segments[limb].Collider);
            var pressure = Feet.Select(id =>
            {
                var segment = rig.Segments[id];
                var sensor = segment.Body.GetComponent<DeadliftFootPressureSensor>()
                    ?? segment.Body.gameObject.AddComponent<DeadliftFootPressureSensor>();
                sensor.Ground = rig.PlatformCollider;
                sensor.Axis = Vector3.ProjectOnPlane(segment.Body.transform.forward, Vector3.up).normalized;
                return sensor;
            }).ToArray();
            var gripReader = rig.Segments["left_foot"].Body.gameObject.AddComponent<DeadliftFootCopSensor>();
            gripReader.Ground = rig.PlatformCollider;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            var grips = new[] { _world.LeftGrip, _world.RightGrip };
            var ankles = Feet.Select(rig.PoweredController.GetJoint).ToArray();
            var ankleForce = new Vector3[2];
            var gripForce = new Vector3[2];
            var gripTorque = new Vector3[2];
            Vector3 barMomentOnBar = Vector3.zero;
            int substeps = 0;
            gripReader.PlatformContactObserved = () =>
            {
                substeps++;
                for (int i = 0; i < grips.Length; i++)
                {
                    Vector3 appliedForce = grips[i].ForceOnBarWorld;
                    gripForce[i] += appliedForce;
                    gripTorque[i] -= grips[i].SolverTorqueRaw;
                    ankleForce[i] += ankles[i].Joint.currentForce;
                    Vector3 anchor = bar.position + bar.rotation * grips[i].BarAnchorLocal;
                    barMomentOnBar += Vector3.Cross(anchor - bar.worldCenterOfMass, appliedForce) - grips[i].SolverTorqueRaw;
                }
            };

            var trace = new StringBuilder("tick,barY,gap3dL,gap3dR,speed,torso,kneeL,kneeR,shoulderAhead,legN,substeps,footLN,footRN,leftSpan,rightSpan,gripErrorL,gripErrorR,footLfResidual,footRfResidual,gripLF,gripRF,gripLT,gripRT,barFResidual,barTResidual,leftZReq,leftZApplied,leftZActual,leftZD,leftZCap,rightZReq,rightZApplied,rightZActual,rightZD,rightZCap\n");
            // Placement settle (not judged): three authoritative ticks let the authored
            // configuration take up its welded grips and contact loads. The first tick of the
            // close and hips-through placements was measured to carry a ~20 mm/s settle
            // velocity that is an artifact of placement, not of the hold.
            for (int settle = 1; settle <= 3; settle++)
            {
                _world.Step(1);
                shaftSensor.CompleteTick();
                foreach (var sensor in pressure)
                    sensor.CompleteTick((ulong)settle);
            }
            int sustained = 0, peakStable = 0, lost = 0, firstCeiling = 0;
            for (int tick = 1; tick <= 300; tick++)
            {
                Array.Clear(gripForce, 0, 2);
                Array.Clear(gripTorque, 0, 2);
                Array.Clear(ankleForce, 0, 2);
                substeps = 0;
                barMomentOnBar = Vector3.zero;
                Vector3 barV0 = bar.linearVelocity;
                Vector3 barL0 = Spin(bar);
                var footV0 = Feet.Select(id => rig.Segments[id].Body.linearVelocity).ToArray();
                _world.Step(1);
                shaftSensor.CompleteTick();
                var feet = pressure.Select(sensor => sensor.PendingContact).ToArray();
                if (!copControl)
                    foreach (var sensor in pressure) sensor.CompleteTick((ulong)tick);
                float leg = (float)shaftSensor.LegNormalImpulse / dt;
                DeadliftBarLegGeometry.Realized(rig, bar, out var gap);
                float kneeL = observer.KneeFlexionDeg("left"), kneeR = observer.KneeFlexionDeg("right");
                float minFoot = (float)feet.Min(foot => foot.NormalImpulse) / dt;
                float maxDemand = rig.PoweredController.Joints.Where(j => j.HasPostPhysicsDiagnostic).Max(j => j.PostPhysicsDiagnostic.ModeledDemand);
                bool supported = minFoot > 50f;
                if (lost == 0 && !supported) lost = tick;
                if (firstCeiling == 0 && maxDemand > 1f) firstCeiling = tick;
                bool otherContact = _world.Sensors["bar"].NormalForce(BenchSurface.Floor) > 0d ||
                    _world.Sensors.Any(s => s.Key != "bar" && !Feet.Contains(s.Key) && s.Value.NormalForce(BenchSurface.Floor) > 0d);
                bool stable = supported && leg == 0f && !otherContact && bar.linearVelocity.magnitude <= DeadliftRulebook.MotionlessSpeedMps &&
                    Math.Min(gap.LeftThigh, gap.RightThigh) > 0f &&
                    (!close || Math.Max(gap.LeftThigh, gap.RightThigh) < 0.12f) &&
                    observer.TorsoDeg >= DeadliftRulebook.ErectTorsoMinDeg &&
                    observer.ShoulderAheadOfHipM <= DeadliftRulebook.ShouldersBackToleranceM &&
                    Math.Max(kneeL, kneeR) <= DeadliftRulebook.KneesLockedMaxFlexionDeg &&
                    grips.Max(grip => grip.AnchorSeparationM) < 0.005f && maxDemand <= 1f;
                sustained = stable ? sustained + 1 : 0;
                peakStable = Math.Max(sustained, peakStable);

                Vector3 RequiredFootForce(int i)
                {
                    if (substeps != SimulationConstants.PhysicsSubstepsPerTick)
                        return Vector3.one * float.NaN;
                    Rigidbody foot = rig.Segments[Feet[i]].Body;
                    return foot.mass * ((foot.linearVelocity - footV0[i]) / dt - Physics.gravity) - ankleForce[i] / substeps;
                }
                Vector3 barForceResidual = bar.mass * ((bar.linearVelocity - barV0) / dt - Physics.gravity)
                    - (gripForce[0] + gripForce[1]) / substeps;
                Vector3 barTorqueResidual = (Spin(bar) - barL0) / dt - barMomentOnBar / substeps;
                var l = ankles[0].PostPhysicsDiagnostic;
                var r = ankles[1].PostPhysicsDiagnostic;
                trace.AppendLine(
                    FormattableString.Invariant($"{tick},{bar.position.y:F6},{gap.LeftThigh:F6},{gap.RightThigh:F6},{bar.linearVelocity.magnitude:F6},{observer.TorsoDeg:F3},{kneeL:F3},{kneeR:F3},{observer.ShoulderAheadOfHipM:F5},{leg:F2},{substeps},") +
                    FormattableString.Invariant($"{feet[0].NormalImpulse / dt:F3},{feet[1].NormalImpulse / dt:F3},{feet[0].LoadedAxisMax - feet[0].LoadedAxisMin:F6},{feet[1].LoadedAxisMax - feet[1].LoadedAxisMin:F6},") +
                    FormattableString.Invariant($"{grips[0].AnchorSeparationM:F6},{grips[1].AnchorSeparationM:F6},{V(RequiredFootForce(0))},{V(RequiredFootForce(1))},{V(gripForce[0] / substeps)},{V(gripForce[1] / substeps)},") +
                    FormattableString.Invariant($"{V(gripTorque[0] / substeps)},{V(gripTorque[1] / substeps)},{V(barForceResidual)},{V(barTorqueResidual)},") +
                    FormattableString.Invariant($"{l.SecondaryRequestedAngleDeg:F3},{l.SecondaryAppliedAngleDeg:F3},{l.SecondaryActualAngleDeg:F3},{l.SecondaryDriveDemandNm:F3},{l.SecondaryMaximumForceNm:F3},") +
                    FormattableString.Invariant($"{r.SecondaryRequestedAngleDeg:F3},{r.SecondaryAppliedAngleDeg:F3},{r.SecondaryActualAngleDeg:F3},{r.SecondaryDriveDemandNm:F3},{r.SecondaryMaximumForceNm:F3}"));
                if (minFoot <= 50f && tick > 10)
                    break;
            }
            File.WriteAllText(Path.Combine(F.EvidenceDirectory, $"lockout-endpoint-{name}.csv"), trace.ToString());
            report.AppendLine($"{name} valid consecutive ticks {peakStable}, final valid ticks {sustained}, first foot below 50 N {lost}, first modeled ceiling crossing {firstCeiling}");
            if (copControl && positionAnchor)
            {
                Assert.That(sustained, Is.EqualTo(300), report.ToString());
                Assert.That(lost, Is.Zero);
                Assert.That(firstCeiling, Is.Zero);
            }
            F.WriteEvidence($"lockout-endpoint-{name}.txt", report.ToString());
            yield return _world.Teardown();
            _world = null;
        }

        private static Vector3 Spin(Rigidbody body)
        {
            Quaternion frame = body.rotation * body.inertiaTensorRotation;
            return frame * Vector3.Scale(body.inertiaTensor, Quaternion.Inverse(frame) * body.angularVelocity);
        }

        private static string V(Vector3 v) => FormattableString.Invariant($"{v.x:F4}|{v.y:F4}|{v.z:F4}");
    }
}
