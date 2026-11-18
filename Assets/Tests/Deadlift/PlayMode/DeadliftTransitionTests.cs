using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Equipment;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    public sealed class DeadliftTransitionTests
    {
        private static readonly string[] Sides = { "left", "right" };
        private static readonly string[] Landmarks = { "upper_arm", "thigh", "shank", "foot" };
        private DeadliftPlantWorld _world;

        private sealed class Run
        {
            public ulong Pull, PhysicalSeparation, ConfirmedSeparation, FirstPenetration, Stop;
            public string StopReason;
            public bool Good, Released;
            public ulong Down;
            public string RefereeSummary;
            public float MinGap = float.PositiveInfinity;
            public float MinSupport = float.PositiveInfinity;
            public DeadliftPreparedClearancePath.RealizedSweep Sweep;
            public string SeparationState, SeparationChannels;
            public Quaternion LeftRequestAfterSeparation, RightRequestAfterSeparation;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
                yield return _world.Teardown();
            _world = null;
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 matched B2 transition reconstruction")]
        public IEnumerator GAM70_PREPARED_TRANSITION_RECONSTRUCTION()
        {
            var quiet = new Run();
            yield return Trace("quiet", 301, quiet);
            var prepared = new Run();
            yield return Trace("release", 284, prepared);

            Assert.That(quiet.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.Pull, Is.EqualTo(quiet.Pull));
            Assert.That(quiet.PhysicalSeparation, Is.GreaterThan(quiet.Pull));
            Assert.That(prepared.PhysicalSeparation, Is.GreaterThan(prepared.Pull));
            Assert.That(quiet.StopReason, Is.EqualTo("knee-region end"));
            Assert.That(prepared.StopReason, Is.EqualTo("bar-leg impulse"));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 prepared offset-only A/B through first failure")]
        public IEnumerator GAM70_PREPARED_SHOULDER_RELEASE_AB()
        {
            var release = new Run();
            yield return Trace("ab-release", 284, release);
            Assert.That(release.Pull, Is.EqualTo(301ul));
            Assert.That(release.StopReason, Is.EqualTo("bar-leg impulse"));

            var continuous = new Run();
            yield return Trace("ab-continuous", 284, continuous, retainPreparation: true, lastTick: (int)release.Stop);
            Assert.That(continuous.Pull, Is.EqualTo(release.Pull));
            Assert.That(continuous.ConfirmedSeparation, Is.EqualTo(release.ConfirmedSeparation));
            Assert.That(continuous.SeparationState == release.SeparationState, Is.True, "Plant matches through confirmed separation.");
            Assert.That(continuous.SeparationChannels == release.SeparationChannels, Is.True, "Commands match through confirmed separation.");
            Assert.That(Quaternion.Angle(continuous.LeftRequestAfterSeparation, release.LeftRequestAfterSeparation), Is.EqualTo(2f).Within(0.005f));
            Assert.That(Quaternion.Angle(continuous.RightRequestAfterSeparation, release.RightRequestAfterSeparation), Is.EqualTo(2f).Within(0.005f));
            Assert.That(continuous.Stop, Is.InRange(release.ConfirmedSeparation + 1, release.Stop));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 physically closed prepared first ascent segment")]
        public IEnumerator GAM70_PREPARED_CLOSED_FIRST_SEGMENT()
        {
            var prepared = new Run();
            yield return Trace("closed-prefix", 284, prepared, retainPreparation: true, closedPrefix: true, lastTick: 470);
            AssertClearance(prepared, "first segment end");
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 extend the prepared reference through the knee region")]
        public IEnumerator GAM70_PREPARED_CLOSED_KNEE_PASSAGE()
        {
            var prepared = new Run();
            yield return Trace("closed-knee", 284, prepared, retainPreparation: true, closedPrefix: true, throughKnee: true);
            AssertClearance(prepared, "knee-region end");
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 composed prepared-ascent qualification probe")]
        public IEnumerator GAM70_PREPARED_COMPOSED_ATTEMPT()
        {
            var prepared = new Run();
            yield return Trace("closed-composed", 284, prepared, retainPreparation: true, closedPrefix: true,
                throughKnee: true, complete: true, lastTick: 2100);
            Assert.That(prepared.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.Stop, Is.GreaterThan(538ul));
            Assert.That(prepared.StopReason, Is.Not.EqualTo("comparison bound"));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 closed-reference COM placement probe at lockout")]
        public IEnumerator GAM70_PREPARED_CENTERED_LOCKOUT_PROBE()
        {
            var prepared = new Run();
            yield return Trace("closed-centered", 284, prepared, retainPreparation: true, closedPrefix: true,
                throughKnee: true, complete: true, centered: true, lastTick: 2100);
            Assert.That(prepared.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.Stop, Is.GreaterThan(538ul));
            Assert.That(prepared.StopReason, Is.Not.EqualTo("comparison bound"));
        }

        private static void AssertClearance(Run prepared, string endpoint)
        {
            Assert.That(prepared.Pull, Is.EqualTo(301ul));
            Assert.That(prepared.StopReason, Is.EqualTo(endpoint));
            Assert.That(prepared.FirstPenetration, Is.Zero);
            Assert.That(prepared.MinGap, Is.GreaterThan(0f));
            Assert.That(prepared.MinSupport, Is.GreaterThan(0.04f));
            Assert.That(prepared.Sweep.Samples, Is.EqualTo((prepared.Stop - prepared.ConfirmedSeparation) * SimulationConstants.PhysicsSubstepsPerTick));
            Assert.That(prepared.Sweep.MinBound, Is.GreaterThan(0f));
            Debug.Log($"Prepared realized substeps: {prepared.Sweep.Samples}, min gap {prepared.Sweep.MinGap * 1000f:F2} mm, swept bound {prepared.Sweep.MinBound * 1000f:F2} mm");
        }

        private IEnumerator Trace(string arm, int driveTick, Run result, bool retainPreparation = false, bool closedPrefix = false, bool throughKnee = false, bool complete = false, bool centered = false, int lastTick = 800)
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            Assert.That(DeadliftAttemptRuntime.BarProximityPath, Is.False);
            var rt = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar)
            {
                UpperChainPreparation = driveTick < 301
            };
            rt.Initialize();
            rt.Pull.IntentOverride01 = 0f;
            Vector3 bar0 = rt.Bar.position;
            var feet = Sides.Select(side => (BoxCollider)_world.Rig.Segments[side + "_foot"].Collider).ToArray();
            var sensors = feet.Select(foot => foot.GetComponent<DeadliftFootPressureSensor>()).ToArray();
            var joints = _world.Rig.PoweredController.Joints;
            var rows = new StringBuilder("tick,phase,progress,pullApplied,referenceOwner,handReferenceN,bar,barQ,barV,barW,planBar,pelvis,pelvisQ,pelvisV,pelvisW,planPelvis,planPelvisQ,plateL,plateR,legN,gapL,gapR,planGapL,planGapR,thighL,thighR,planThighL,planThighR,capture,margin,polygon");
            foreach (string side in Sides)
            {
                foreach (string id in Landmarks)
                    rows.Append($",{side}_{id},{side}_{id}_v,{side}_{id}_plan");
                rows.Append($",{side}_handAnchor,{side}_handAnchorV,{side}_barAnchor,{side}_planHandAnchor,{side}_planBarAnchor,{side}_forceOnBar,{side}_footN,{side}_cop,{side}_loadedAxisMin,{side}_loadedAxisMax");
            }
            rows.AppendLine();
            var channels = new StringBuilder("tick,joint,planQ,requestedQ,appliedQ,actualQ,error,targetW,actualW,xDemandNm,xMaxNm,xFraction,xSpring,xDamper,yzDemandNm,yzMaxNm,yzFraction,yzSpring,yzDamper,zDemandNm,zMaxNm,zFraction,zSpring,zDamper,zRequestedDeg,zAppliedDeg,zActualDeg,zActualW,zRateLimited,limitProximity,zLimitProximity,solverTorque,staticReferenceNm\n");
            var leftShoulder = _world.Rig.PoweredController.GetJoint("left_upper_arm");
            var rightShoulder = _world.Rig.PoweredController.GetJoint("right_upper_arm");
            DeadliftPreparedClearancePath closed = null;
            DeadliftFootCopSensor substepContact = null;
            float startBarY = 0f, maxProgress = 0f;
            Quaternion leftStart = Quaternion.identity, rightStart = Quaternion.identity;
            for (int tick = 1; tick <= lastTick; tick++)
            {
                if (tick == driveTick)
                    rt.Pull.IntentOverride01 = 1f;
                Quaternion leftOffset = rt.Hold.PreparationOffsetFor(leftShoulder.Id);
                Quaternion rightOffset = rt.Hold.PreparationOffsetFor(rightShoulder.Id);
                _world.Step(1);
                substepContact?.CompleteTick();
                if (retainPreparation && rt.Tick == rt.Pull.SeparationTick)
                {
                    // Continue the exact two factors the production handoff just dropped.
                    rt.Hold.IdentificationOffsets.Add(leftShoulder.Id, leftOffset);
                    rt.Hold.IdentificationOffsets.Add(rightShoulder.Id, rightOffset);
                    if (closedPrefix)
                    {
                        closed = new DeadliftPreparedClearancePath(rt, _world.Rig, throughKnee, complete, centered);
                        closed.CheckInterpolation();
                        rt.Hold.AscentReferenceOverride = closed.Target;
                        result.Sweep = new DeadliftPreparedClearancePath.RealizedSweep(rt.Bar, _world.Rig);
                        substepContact = feet[0].gameObject.AddComponent<DeadliftFootCopSensor>();
                        substepContact.Ground = _world.Rig.PlatformCollider;
                        substepContact.PlatformContactObserved = result.Sweep.Read;
                        startBarY = rt.Bar.position.y;
                        leftStart = leftOffset;
                        rightStart = rightOffset;
                    }
                }
                if (closed != null)
                {
                    maxProgress = Mathf.Max(maxProgress, rt.Pull.LiftProgress01);
                    float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(maxProgress / 0.07f));
                    rt.Hold.IdentificationOffsets[leftShoulder.Id] = Quaternion.Slerp(leftStart, Quaternion.identity, blend);
                    rt.Hold.IdentificationOffsets[rightShoulder.Id] = Quaternion.Slerp(rightStart, Quaternion.identity, blend);
                }
                if (tick < 280)
                    continue;

                rt.Monitor.Update(); // Read current capture point, then use this tick's foot impulses for the hull.
                var pressure = sensors.Select(sensor => sensor.PendingContact).ToArray();
                var load = pressure.Select(foot => (float)(foot.NormalImpulse / SimulationConstants.FixedDeltaTimeSeconds)).ToArray();
                var points = new List<Vector2>(8);
                for (int side = 0; side < feet.Length; side++)
                {
                    if (load[side] <= rt.Monitor.LoadedFootN)
                        continue;
                    var box = feet[side];
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 corner = box.transform.TransformPoint(box.center + Vector3.Scale(box.size,
                            new Vector3((i & 1) == 0 ? -0.5f : 0.5f, -0.5f, (i & 2) == 0 ? -0.5f : 0.5f)));
                        points.Add(new Vector2(corner.x, corner.z));
                    }
                }
                List<Vector2> hull = DeadliftSupportGeometry.Hull(points);
                Vector2 capture = rt.Monitor.CapturePoint;
                float margin = DeadliftSupportGeometry.SignedDistance(hull, capture);

                float s = rt.Pull.LiftProgress01;
                rt.Path.KeySpan(s, out int a, out int b, out float t);
                Pose rootA = rt.Measurements.PelvisPoseOf(rt.Path.Keys[a].X);
                Pose rootB = rt.Measurements.PelvisPoseOf(rt.Path.Keys[b].X);
                var root = new Pose(Vector3.Lerp(rootA.position, rootB.position, t), Quaternion.Slerp(rootA.rotation, rootB.rotation, t));
                var logical = rt.Model.SetupLogical.Keys.ToDictionary(id => id, id => rt.Path.Target(id, s));
                var kin = rt.Measurements.Kinematics;
                var poses = kin.Solve(root, logical);
                Vector3 planBar = new Vector3(bar0.x, bar0.y + rt.Path.BarRiseAt(s), Mathf.Lerp(rt.Path.Keys[a].BarZ, rt.Path.Keys[b].BarZ, t));
                Quaternion planBarRotation = Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right);
                var planGap = DeadliftBarLegGeometry.Planned(rt.Measurements, poses, planBar);
                if (closed != null)
                {
                    DeadliftPreparedClearancePath.Sample sample = closed.At(s);
                    root = sample.Root;
                    logical = sample.Logical;
                    poses = sample.Poses;
                    planBar = sample.Bar.position;
                    planBarRotation = sample.Bar.rotation;
                    planGap = closed.Gaps(sample);
                }
                DeadliftBarLegGeometry.Realized(_world.Rig, rt.Bar, out var gap);
                if (result.Pull == 0 && rt.Pull.Phase == DeadliftPullPhase.Pulling)
                    result.Pull = rt.Tick;
                if (result.Pull > 0 && result.PhysicalSeparation == 0 && rt.PlateLeftN == 0f && rt.PlateRightN == 0f && rt.Bar.linearVelocity.y > 0.005f)
                    result.PhysicalSeparation = rt.Tick;
                result.ConfirmedSeparation = rt.Pull.SeparationTick;
                if (result.FirstPenetration == 0 && gap.Leg < 0f)
                    result.FirstPenetration = rt.Tick;

                Row(rows, rt.Tick, rt.Pull.Phase, s, rt.Pull.Applied01, rt.Hold.ReferenceOwner, rt.Hold.HandLoadReference.HandPullN,
                    V(rt.Bar.position), Q(rt.Bar.rotation), V(rt.Bar.linearVelocity), V(rt.Bar.angularVelocity), V(planBar),
                    V(rt.Pelvis.position), Q(rt.Pelvis.rotation), V(rt.Pelvis.linearVelocity), V(rt.Pelvis.angularVelocity), V(root.position), Q(root.rotation),
                    rt.PlateLeftN, rt.PlateRightN, rt.BarOnLegN, gap.LeftShin, gap.RightShin, planGap.LeftShin, planGap.RightShin,
                    gap.LeftThigh, gap.RightThigh, planGap.LeftThigh, planGap.RightThigh, V(capture),
                    margin, string.Join(" ", hull.Select(point => V(point))));
                for (int side = 0; side < Sides.Length; side++)
                {
                    string prefix = Sides[side] + "_";
                    foreach (string part in Landmarks)
                    {
                        var joint = _world.Rig.PoweredController.GetJoint(prefix + part).Joint;
                        Vector3 anchor = joint.transform.TransformPoint(joint.anchor);
                        rows.Append(',');
                        Row(rows, V(anchor), V(_world.Rig.Segments[prefix + part].Body.GetPointVelocity(anchor)), V(kin.JointAnchorWorld(poses, prefix + part)));
                    }
                    BenchGrip grip = side == 0 ? rt.LeftGrip : rt.RightGrip;
                    Rigidbody hand = _world.Rig.Segments[prefix + "hand"].Body;
                    Pose planHand = poses[prefix + "hand"];
                    Vector3 anchorWorld = hand.transform.TransformPoint(grip.HandAnchorLocal);
                    rows.Append(',');
                    Row(rows, V(anchorWorld), V(hand.GetPointVelocity(anchorWorld)), V(rt.Bar.transform.TransformPoint(grip.BarAnchorLocal)),
                        V(planHand.position + planHand.rotation * grip.HandAnchorLocal), V(planBar + planBarRotation * grip.BarAnchorLocal),
                        V(grip.ForceOnBarWorld), load[side], V(pressure[side].Cop), pressure[side].LoadedAxisMin, pressure[side].LoadedAxisMax);
                }
                rows.AppendLine();

                foreach (var joint in joints)
                {
                    PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                    JointDrive x = joint.Joint.angularXDrive, yz = joint.Joint.angularYZDrive;
                    Row(channels, rt.Tick, joint.Id, Q(logical.TryGetValue(joint.Id, out Quaternion plan) ? plan : Quaternion.identity),
                        Q(d.RequestedTarget), Q(d.AppliedTarget), Q(d.ActualRelative), V(d.ErrorRad),
                        V(d.TargetAngularVelocityRadS), V(d.ActualAngularVelocityRadS),
                        d.TwistDriveDemandNm, x.maximumForce, d.TwistDriveDemandFraction, x.positionSpring, x.positionDamper,
                        d.HasSecondaryChannel ? 0f : d.SwingDriveDemandNm, d.HasSecondaryChannel ? 0f : yz.maximumForce,
                        d.HasSecondaryChannel ? 0f : d.SwingDriveDemandFraction, d.HasSecondaryChannel ? 0f : yz.positionSpring, d.HasSecondaryChannel ? 0f : yz.positionDamper,
                        d.SecondaryDriveDemandNm, d.SecondaryMaximumForceNm, d.SecondaryDriveDemandFraction,
                        d.HasSecondaryChannel ? yz.positionSpring : 0f, d.HasSecondaryChannel ? yz.positionDamper : 0f,
                        d.SecondaryRequestedAngleDeg, d.SecondaryAppliedAngleDeg, d.SecondaryActualAngleDeg, d.SecondaryActualAngularVelocityRadS,
                        d.SecondaryTargetRateLimited, d.LimitProximity, d.SecondaryLimitProximity, V(d.SolverTorqueJointSpaceNm),
                        V(rt.Hold.AppliedReferenceAxesFor(joint.Id)));
                    channels.AppendLine();
                }

                if (rt.Tick == result.ConfirmedSeparation)
                {
                    result.SeparationState = rows.ToString();
                    result.SeparationChannels = channels.ToString();
                }
                if (result.ConfirmedSeparation != 0 && rt.Tick == result.ConfirmedSeparation + 1)
                {
                    result.LeftRequestAfterSeparation = leftShoulder.PostPhysicsDiagnostic.RequestedTarget;
                    result.RightRequestAfterSeparation = rightShoulder.PostPhysicsDiagnostic.RequestedTarget;
                }

                if (result.Pull == 0)
                    continue;
                result.MinGap = Mathf.Min(result.MinGap, gap.Leg);
                result.MinSupport = Mathf.Min(result.MinSupport, margin);
                result.StopReason = rt.BarOnLegN > 0f ? "bar-leg impulse"
                    : closed != null && gap.Leg <= 0f ? "shaft penetration"
                    : load.Min() <= rt.Monitor.LoadedFootN || margin < 0f ? "support loss"
                    : closed != null && !throughKnee && rt.Bar.position.y - startBarY >= DeadliftPreparedClearancePath.QualifiedRiseM ? "first segment end"
                    : complete && rt.Referee.Completed && rt.GripsReleased ? "judged return"
                    : complete && rt.Referee.Faults.Count > 0 ? "referee fault"
                    : !complete && rt.Bar.position.y - bar0.y >= rt.Path.KneePassageRiseM ? "knee-region end"
                    : tick == lastTick ? "comparison bound" : null;
                if (result.StopReason != null)
                {
                    result.Stop = rt.Tick;
                    break;
                }
            }

            result.Good = rt.Referee.GoodLift;
            result.Released = rt.GripsReleased;
            result.Down = rt.Pull.DownTick;
            result.RefereeSummary = rt.Referee.Summary();
            if (complete)
                Debug.Log($"Composed prepared attempt: {result.RefereeSummary}; DOWN {result.Down}; released {result.Released}; good {result.Good}");
            File.WriteAllText(Path.Combine(F.EvidenceDirectory, $"transition-{arm}.csv"), rows.ToString());
            File.WriteAllText(Path.Combine(F.EvidenceDirectory, $"transition-{arm}-channels.csv"), channels.ToString());
            Debug.Log($"{arm}: PULL {result.Pull}, physical separation {result.PhysicalSeparation}, confirmed {result.ConfirmedSeparation}, first penetration {result.FirstPenetration}, stop {result.Stop} ({result.StopReason}), min gap {result.MinGap * 1000f:F2} mm, min margin {result.MinSupport * 1000f:F1} mm");
            yield return _world.Teardown();
            _world = null;
        }

        private static void Row(StringBuilder b, params object[] cells) =>
            b.AppendJoin(",", cells.Select(cell => cell is float f ? N(f) : cell.ToString()));

        private static string N(float x) => x.ToString("G9", CultureInfo.InvariantCulture);
        private static string V(Vector2 v) => $"{N(v.x)}|{N(v.y)}";
        private static string V(Vector3 v) => $"{N(v.x)}|{N(v.y)}|{N(v.z)}";
        private static string Q(Quaternion q) => $"{N(q.x)}|{N(q.y)}|{N(q.z)}|{N(q.w)}";
    }
}
