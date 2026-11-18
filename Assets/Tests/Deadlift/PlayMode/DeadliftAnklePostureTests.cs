using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    public sealed class DeadliftAnklePostureTests
    {
        private const float PulseDeg = 0.25f;
        private const int PulseTicks = 3;
        private const int ObservationTicks = 22;
        private static readonly string[] Sides = { "left", "right" };
        private DeadliftPlantWorld _world;

        private sealed class Result
        {
            public string InitialState, Trace;
            public ulong Tick;
            public float FirstRequest;
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            DeadliftAttemptRuntime.BarProximityPath = DeadliftAttemptRuntime.BarProximityPathDefault;
            if (_world != null)
                yield return _world.Teardown();
            _world = null;
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 matched crouched and near-standing ankle Z identification")]
        public IEnumerator GAM70_ANKLE_ROLL_POSTURE_RESPONSE()
        {
            foreach (bool nearStanding in new[] { false, true })
            {
                var quiet = new Result();
                var positive = new Result();
                var negative = new Result();
                var repeat = new Result();
                yield return Run(nearStanding, 0f, "quiet", quiet);
                yield return Run(nearStanding, PulseDeg, "plus", positive);
                yield return Run(nearStanding, -PulseDeg, "minus", negative);
                yield return Run(nearStanding, PulseDeg, "plus-repeat", repeat);
                foreach (var result in new[] { positive, negative, repeat })
                {
                    Assert.That(result.Tick, Is.EqualTo(quiet.Tick));
                    Assert.That(result.InitialState, Is.EqualTo(quiet.InitialState));
                }
                Assert.That(positive.FirstRequest - quiet.FirstRequest, Is.EqualTo(PulseDeg).Within(0.005f));
                Assert.That(negative.FirstRequest - quiet.FirstRequest, Is.EqualTo(-PulseDeg).Within(0.005f));
                Assert.That(positive.Trace == repeat.Trace, Is.True, "The signed pulse response must repeat on a fresh plant.");
            }
        }

        private IEnumerator Run(bool nearStanding, float pulse, string arm, Result result)
        {
            DeadliftAttemptRuntime.BarProximityPath = nearStanding;
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var rt = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar);
            rt.Initialize();
            rt.Pull.IntentOverride01 = 0f;
            var ankles = Sides.Select(side => _world.Rig.PoweredController.GetJoint(side + "_foot")).ToArray();
            var hips = Sides.Select(side => _world.Rig.PoweredController.GetJoint(side + "_thigh")).ToArray();
            var sensors = ankles.Select(joint => joint.Joint.GetComponent<DeadliftFootPressureSensor>()).ToArray();
            var patches = ankles.Select(joint => joint.Joint.gameObject.AddComponent<DeadliftFootCopSensor>()).ToArray();
            foreach (var patch in patches)
                patch.Ground = _world.Rig.PlatformCollider;

            void Step()
            {
                _world.Step(1);
                foreach (var patch in patches)
                    patch.CompleteTick();
            }

            for (int tick = 1; tick <= 750; tick++)
            {
                if (nearStanding && tick == 301)
                    rt.Pull.IntentOverride01 = 1f;
                Step();
                if (nearStanding ? rt.Pull.LiftProgress01 >= 0.76f : tick == 301)
                    break;
            }
            Assert.That(nearStanding ? rt.Pull.LiftProgress01 >= 0.76f : rt.Pull.Phase == DeadliftPullPhase.Grounded, Is.True);
            Assert.That(rt.BarOnLegN, Is.Zero);
            result.Tick = rt.Tick;

            string posture = nearStanding ? "closer-bar" : "crouched";
            var rows = new StringBuilder("tick,sample,progress,pulseDeg,torsoDeg,kneeFlexL,kneeFlexR,barY,thighGapL,thighGapR,plateL,plateR,legN,margin");
            foreach (string side in Sides)
                rows.Append($",{side}_requestedZ,{side}_appliedZ,{side}_actualZ,{side}_velocityZ,{side}_rollDemandNm,{side}_rollCeilingNm,{side}_rollFraction,{side}_regulatorRoll,{side}_rollLimitProximity,{side}_footN,{side}_cop,{side}_copAcross,{side}_copAcrossTarget,{side}_axisMin,{side}_axisMax,{side}_acrossMin,{side}_acrossMax,{side}_hipRotation,{side}_hipW,{side}_hipAnchor,{side}_hipSwingFraction");
            rows.AppendLine();

            string Sample(int n, float input)
            {
                var row = new StringBuilder();
                rt.Monitor.Update();
                DeadliftBarLegGeometry.Realized(_world.Rig, rt.Bar, out var gaps);
                Row(row, rt.Tick, n, rt.Pull.LiftProgress01, input, rt.Observer.TorsoDeg, rt.Observer.KneeFlexionDeg("left"),
                    rt.Observer.KneeFlexionDeg("right"), rt.Bar.position.y, gaps.LeftThigh, gaps.RightThigh, rt.PlateLeftN,
                    rt.PlateRightN, rt.BarOnLegN, rt.Monitor.MarginM);
                for (int i = 0; i < ankles.Length; i++)
                {
                    var d = ankles[i].PostPhysicsDiagnostic;
                    var h = hips[i].PostPhysicsDiagnostic;
                    var foot = sensors[i].PendingContact;
                    Vector3 across = Vector3.Cross(Vector3.up, sensors[i].Axis);
                    var contact = patches[i].ActivePatch;
                    float acrossMin = float.NaN, acrossMax = float.NaN;
                    if (contact.Count > 0)
                    {
                        var projected = contact.Select(p => p.x * across.x + p.y * across.z);
                        acrossMin = projected.Min();
                        acrossMax = projected.Max();
                    }
                    row.Append(',');
                    Row(row, d.SecondaryRequestedAngleDeg, d.SecondaryAppliedAngleDeg, d.SecondaryActualAngleDeg,
                        d.SecondaryActualAngularVelocityRadS, d.SecondaryDriveDemandNm, d.SecondaryMaximumForceNm,
                        d.SecondaryDriveDemandFraction, rt.Regulator.RollOffsetDegFor(ankles[i].Id), d.SecondaryLimitProximity,
                        (float)(foot.NormalImpulse / SimulationConstants.FixedDeltaTimeSeconds), V(foot.Cop),
                        Vector3.Dot(foot.Cop, across), rt.Regulator.TargetAcrossFor(ankles[i].Id), foot.LoadedAxisMin, foot.LoadedAxisMax, acrossMin, acrossMax,
                        Q(h.ActualRelative), V(h.ActualAngularVelocityRadS), V(hips[i].Joint.transform.TransformPoint(hips[i].Joint.anchor)),
                        h.SwingDriveDemandFraction);
                }
                return row.ToString();
            }

            result.InitialState = Sample(0, 0f);
            rows.AppendLine(result.InitialState);
            for (int n = 1; n <= ObservationTicks; n++)
            {
                float command = n <= PulseTicks ? pulse : 0f;
                if (command != 0f)
                    rt.Hold.IdentificationOffsets[ankles[0].Id] = Quaternion.AngleAxis(command, Vector3.forward);
                else
                    rt.Hold.IdentificationOffsets.Remove(ankles[0].Id);

                Step();
                rows.AppendLine(Sample(n, command));
                if (n == 1)
                    result.FirstRequest = ankles[0].PostPhysicsDiagnostic.SecondaryRequestedAngleDeg;
                if (rt.BarOnLegN > 0f || sensors.Any(sensor => sensor.PendingContact.NormalImpulse /
                    SimulationConstants.FixedDeltaTimeSeconds <= rt.Monitor.LoadedFootN))
                    break;
            }

            result.Trace = rows.ToString();
            File.WriteAllText(Path.Combine(F.EvidenceDirectory, $"ankle-posture-{posture}-{arm}.csv"), result.Trace);
            Debug.Log($"Ankle Z pulse {posture}/{arm}: starting tick {result.Tick}, last sampled tick {rt.Tick}");
            yield return _world.Teardown();
            _world = null;
        }

        private static void Row(StringBuilder b, params object[] cells) =>
            b.AppendJoin(",", cells.Select(cell => cell is float f ? N(f) : cell.ToString()));

        private static string N(float x) => x.ToString("G9", CultureInfo.InvariantCulture);
        private static string V(Vector3 v) => $"{N(v.x)}|{N(v.y)}|{N(v.z)}";
        private static string Q(Quaternion q) => $"{N(q.x)}|{N(q.y)}|{N(q.z)}|{N(q.w)}";
    }
}
