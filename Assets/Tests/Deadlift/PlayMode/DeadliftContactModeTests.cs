using System.Collections;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    public sealed partial class DeadliftSetupDynamicTests
    {
        private float CopMargin()
        {
            Vector3 cop = CombinedCop(_copL.NormalImpulse, _copL.Cop, _copR.NormalImpulse, _copR.Cop);
            var points = _copL.ActivePatch.Concat(_copR.ActivePatch).ToList();
            return points.Count < 3 || float.IsNaN(cop.x) ? float.NaN :
                PowerliftingSimulator.Deadlift.Unity.DeadliftSupportGeometry.SignedDistance(
                    PowerliftingSimulator.Deadlift.Unity.DeadliftSupportGeometry.Hull(points), new Vector2(cop.x, cop.z));
        }

        private static Vector3 CombinedCop(double l, Vector3 lp, double r, Vector3 rp)
        {
            if (l + r <= 1e-9) return new Vector3(float.NaN, float.NaN, float.NaN);
            return ((l > 1e-9 ? lp * (float)l : Vector3.zero) +
                    (r > 1e-9 ? rp * (float)r : Vector3.zero)) / (float)(l + r);
        }

        [Test]
        public void CombinedCopUsesContemporaneousImpulsesAndHandlesUnloadedFeet()
        {
            Vector3 missing = new Vector3(float.NaN, float.NaN, float.NaN);
            Assert.That(CombinedCop(0, missing, 2, Vector3.right), Is.EqualTo(Vector3.right));
            Assert.That(float.IsNaN(CombinedCop(0, missing, 0, missing).x), Is.True);
            Assert.That(CombinedCop(1, Vector3.zero, 3, Vector3.right).x, Is.EqualTo(0.75f));
        }

        // Finite observation horizon is inconclusive; only measured physical failures terminate a trial early.
        [UnityTest, Timeout(900000)]
        public IEnumerator ContactModeBoundary()
        {
            var summary = new StringBuilder("case,ticks,event,comX,comY,comZ,velocityX,velocityY,velocityZ\n");
            var cases = new[] {
                ("A0", false, true), ("A1", true, true),
                ("A1-repeat", true, true), ("A1-no-gravity-reference", true, false) };
            foreach (var c in cases)
            {
                yield return Prepare(c.Item2, c.Item2, c.Item3);
                var trace = new StringBuilder("tick,com,velocity,cop,leftImpulse,rightImpulse,leftCop,rightCop,leftPatchMin,leftPatchMax,rightPatchMin,rightPatchMax,plateL,plateR,gripL,gripR,barPosition,barVelocity,pelvisRotation,thoraxRotation,gripDriftL,gripDriftR,unexpectedGroundN\n");
                var joints = new StringBuilder("tick,joint,actualQuaternion,requestedQuaternion,appliedQuaternion,errorRad,angularVelocity,torqueNm,ceilingNm,twistDemandNm,swingDemandNm,limitProximity\n");
                string ev = "horizon-inconclusive";
                Vector3 velocity = Vector3.zero;
                for (int i = 0; i < 600; i++)
                {
                    _world.Step(1);
                    Tick t = _ticks.Last();
                    velocity = _world.Rig.Segments.Values.Aggregate(Vector3.zero,
                        (v, b) => v + b.Body.mass * b.Body.linearVelocity) / _world.Rig.TotalMassKg;
                    double unexpected = _world.Sensors.Where(x => x.Key != "bar" && !x.Key.EndsWith("_foot"))
                        .Sum(x => x.Value.NormalForce(BenchSurface.Floor));
                    string V(Vector3 v) => v.ToString("G9").Replace(',', ';');
                    string Q(Quaternion q) => q.ToString("G9").Replace(',', ';');
                    trace.AppendLine(string.Join(",", i+1, V(t.Com), V(velocity), V(t.Cop),
                        t.LeftFootN * .01, t.RightFootN * .01, V(t.CopL), V(t.CopR),
                        t.PatchMinL.ToString("G9").Replace(',', ';'), t.PatchMaxL.ToString("G9").Replace(',', ';'),
                        t.PatchMinR.ToString("G9").Replace(',', ';'), t.PatchMaxR.ToString("G9").Replace(',', ';'),
                        t.PlateLN, t.PlateRN, V(t.GripRawL), V(t.GripRawR), V(t.BarPos), V(_world.Bar.Body.linearVelocity),
                        Q(_world.Rig.Segments["pelvis"].Body.rotation), Q(_world.Rig.Segments["thorax"].Body.rotation), t.SepL, t.SepR, unexpected));
                    foreach (var j in _world.Rig.PoweredController.Joints.Where(j => j.HasPostPhysicsDiagnostic))
                    {
                        var d = j.PostPhysicsDiagnostic;
                        joints.AppendLine(string.Join(",", i+1, j.Id, Q(d.ActualRelative), Q(d.RequestedTarget), Q(d.AppliedTarget), V(d.ErrorRad), V(d.ActualAngularVelocityRadS),
                            V(d.SolverTorqueJointSpaceNm), d.MaximumForceNm, d.TwistDriveDemandNm, d.SwingDriveDemandNm, d.LimitProximity));
                    }
                    if (i > 2 && t.PlateLN + t.PlateRN < 1 && _world.Bar.Body.linearVelocity.y > 0) ev = "plate-support-lost-bar-rising";
                    else if (unexpected > 1) ev = "nonfoot-platform-contact";
                    else if (t.BarLegN > 1) ev = "bar-leg-contact";
                    else if (t.SepL > .005 || t.SepR > .005) ev = "grip-drift";
                    else if (i > 2 && t.LeftFootN + t.RightFootN < 1) ev = "both-feet-unloaded";
                    if (ev != "horizon-inconclusive") break;
                }
                var last = _ticks.Last();
                summary.AppendLine(string.Join(",", c.Item1, _ticks.Count, ev, last.Com.x, last.Com.y, last.Com.z, velocity.x, velocity.y, velocity.z));
                F.WriteEvidence("contact-" + c.Item1 + ".csv", trace.ToString());
                F.WriteEvidence("joints-" + c.Item1 + ".csv", joints.ToString());
                yield return _world.Teardown();
                _world = null;
            }
            F.WriteEvidence("contact-summary.csv", summary.ToString());
        }
    }
}
