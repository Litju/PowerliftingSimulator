using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    public sealed class DeadliftClosureTests
    {
        private DeadliftPlantWorld _world;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            DeadliftAttemptRuntime.BarProximityPath = DeadliftAttemptRuntime.BarProximityPathDefault;
            if (_world != null)
                yield return _world.Teardown();
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator GAM70_PLANTED_WELDED_HIP_JACOBIAN()
        {
            var report = new StringBuilder();
            yield return Capture(false, report);
            yield return Capture(true, report);
            F.WriteEvidence("planted-welded-hip-jacobian.txt", report.ToString());
            Debug.Log(report.ToString());
        }

        private IEnumerator Capture(bool close, StringBuilder report)
        {
            DeadliftAttemptRuntime.BarProximityPath = close;
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var rt = new DeadliftAttemptRuntime(_world.Foundation, _world.Rig, _world.Bar);
            rt.Initialize();
            var kin = new BenchKinematics(_world.Rig);
            bool reached = false;
            for (int tick = 1; tick <= 900; tick++)
            {
                rt.Pull.IntentOverride01 = tick >= 301 ? 1f : 0f;
                _world.Step(1);
                if (!close && tick == 300)
                    Audit("B2 loaded setup", rt, kin, report);
                if (close ? rt.Pull.LiftProgress01 < 0.76f : rt.Pull.Phase != DeadliftPullPhase.Lockout)
                    continue;
                Audit(close ? "historical closer ascent" : "stock lockout", rt, kin, report);
                reached = true;
                break;
            }
            Assert.That(reached, Is.True);
            yield return _world.Teardown();
            _world = null;
        }

        private readonly struct Column
        {
            public readonly string Name;
            public readonly string Root;
            public readonly Vector3 Origin, Linear, Angular;

            public Column(string name, string root, Vector3 origin, Vector3 linear, Vector3 angular)
            {
                Name = name;
                Root = root;
                Origin = origin;
                Linear = linear;
                Angular = angular;
            }
        }

        private void Audit(string label, DeadliftAttemptRuntime rt, BenchKinematics kin, StringBuilder report)
        {
            var rig = _world.Rig;
            Pose PoseOf(string id)
            {
                Rigidbody body = id == "bar" ? rt.Bar : rig.Segments[id].Body;
                return new Pose(body.position, body.rotation);
            }
            Vector3 Point(string id, Vector3 local)
            {
                Pose pose = PoseOf(id);
                return pose.position + pose.rotation * local;
            }
            Vector3 Hip(string side)
            {
                var joint = kin.Joint(side + "_thigh");
                return Point("pelvis", joint.ConnectedAnchor);
            }
            var hips = (Hip("left") + Hip("right")) * 0.5f;
            var shoulder = (Point("thorax", kin.Joint("left_upper_arm").ConnectedAnchor)
                          + Point("thorax", kin.Joint("right_upper_arm").ConnectedAnchor)) * 0.5f;
            Vector3 handL = Point("left_hand", rt.LeftGrip.HandAnchorLocal);
            Vector3 handR = Point("right_hand", rt.RightGrip.HandAnchorLocal);
            Vector3 barGripL = Point("bar", rt.LeftGrip.BarAnchorLocal);
            Vector3 barGripR = Point("bar", rt.RightGrip.BarAnchorLocal);

            bool Includes(string root, string body)
            {
                for (string id = body; id != null; id = kin.TryGetJoint(id, out var j) ? j.ParentId : null)
                    if (id == root)
                        return true;
                return false;
            }

            Vector3 Velocity(Column q, string body, Vector3 at) =>
                Includes(q.Root, body) ? q.Linear + Vector3.Cross(q.Angular, at - q.Origin) : Vector3.zero;
            Vector3 Omega(Column q, string body) => Includes(q.Root, body) ? q.Angular : Vector3.zero;

            var axes = new[] { Vector3.right, Vector3.up, Vector3.forward };
            var columns = new List<Column>();
            foreach (string id in new[] { "pelvis", "bar" })
                foreach (Vector3 axis in axes)
                {
                    columns.Add(new Column(id + " translation " + axis, id, PoseOf(id).position, axis, Vector3.zero));
                    columns.Add(new Column(id + " rotation " + axis, id, PoseOf(id).position, Vector3.zero, axis));
                }
            foreach (string id in kin.SegmentOrder)
            {
                if (id == "head_neck" || !kin.TryGetJoint(id, out var joint))
                    continue;
                Vector3 anchor = Point(joint.ParentId, joint.ConnectedAnchor);
                Quaternion frame = PoseOf(id).rotation * joint.JointSpace;
                if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                {
                    Quaternion zero = PoseOf(joint.ParentId).rotation * joint.NeutralParentToChild * joint.JointSpace;
                    columns.Add(new Column(id + ".X", id, anchor, Vector3.zero, zero * Vector3.right));
                    if (id.EndsWith("_foot"))
                        columns.Add(new Column(id + ".Z", id, anchor, Vector3.zero, frame * Vector3.forward));
                    continue;
                }
                for (int axis = 0; axis < 3; axis++)
                    columns.Add(new Column(id + "." + "XYZ"[axis], id, anchor, Vector3.zero, frame * axes[axis]));
            }

            report.AppendLine(FormattableString.Invariant($"{label} tick {rt.Tick}, hip Z {hips.z:F5} m, shoulder Z {shoulder.z:F5} m, bar {rt.Bar.position.ToString("F5")} m, grip closure {1000f * (handL - barGripL).magnitude:F3}/{1000f * (handR - barGripR).magnitude:F3} mm"));
            foreach (bool allowRoll in new[] { false, true })
            {
                var q = columns.Where(c => allowRoll || !c.Name.EndsWith("_foot.Z")).ToArray();
                var rows = new List<double[]>();
                void Row(Func<Column, double> f) => rows.Add(q.Select(f).ToArray());
                void Rows(Func<Column, Vector3> f)
                {
                    foreach (Vector3 axis in axes)
                        Row(c => Vector3.Dot(f(c), axis));
                }
                foreach (string id in new[] { "left_foot", "right_foot" })
                {
                    Rows(c => Velocity(c, id, PoseOf(id).position));
                    Rows(c => Omega(c, id));
                }
                Rows(c => Velocity(c, "left_hand", handL) - Velocity(c, "bar", barGripL));
                Rows(c => Omega(c, "left_hand") - Omega(c, "bar"));
                Rows(c => Velocity(c, "right_hand", handR) - Velocity(c, "bar", barGripR));
                Rows(c => Omega(c, "right_hand") - Omega(c, "bar"));
                double[] task = q.Select(c => (double)Velocity(c, "pelvis", hips).z).ToArray();
                int closureRank = Describe(rows, q, task, "plant/weld", report);
                Assert.That(closureRank, Is.EqualTo(24));
                // Same realized bar height and orientation, fixed lateral bar position, fixed
                // knee flexion and hip-to-shoulder sagittal/frontal lean (local lockout tangent).
                Rows(c => Omega(c, "bar"));
                Row(c => Velocity(c, "bar", PoseOf("bar").position).x);
                Row(c => Velocity(c, "bar", PoseOf("bar").position).y);
                Row(c => c.Name == "left_shank.X" ? 1d : 0d);
                Row(c => c.Name == "right_shank.X" ? 1d : 0d);
                Vector3 torso = shoulder - hips;
                Vector3 TorsoDerivative(Column c) => Velocity(c, "thorax", shoulder) - Velocity(c, "pelvis", hips);
                Row(c => torso.y * TorsoDerivative(c).z - torso.z * TorsoDerivative(c).y);
                Row(c => torso.y * TorsoDerivative(c).x - torso.x * TorsoDerivative(c).y);
                Describe(rows, q, task, "fixed-height/torso/knees", report);
            }
        }

        private static int Describe(List<double[]> rows, Column[] q, double[] task, string condition, StringBuilder report)
        {
            double Dot(double[] a, double[] b)
            {
                double s = 0;
                for (int i = 0; i < a.Length; i++)
                    s += a[i] * b[i];
                return s;
            }
            var basis = new List<double[]>();
            double[] Reject(double[] source)
            {
                double[] v = (double[])source.Clone();
                foreach (double[] u in basis)
                {
                    double d = Dot(v, u);
                    for (int i = 0; i < v.Length; i++)
                        v[i] -= d * u[i];
                }
                return v;
            }
            foreach (double[] row in rows)
            {
                double[] v = Reject(Reject(row));
                double norm = Math.Sqrt(Dot(v, v));
                if (norm > 1e-6 * Math.Sqrt(Dot(row, row)))
                    basis.Add(v.Select(x => x / norm).ToArray());
            }
            var tangent = Reject(Reject(task));
            double score = Math.Sqrt(Dot(tangent, tangent));
            Assert.That(rows.Max(r => Math.Abs(Dot(r, tangent))), Is.LessThan(1e-6));
            report.AppendLine(FormattableString.Invariant($"  {condition}: {rows.Count}x{q.Length} rank {basis.Count}, nullity {q.Length - basis.Count}, projected hip-Z norm {score:G6}"));
            if (score > 1e-4)
                report.AppendLine("    rate for +10 mm/s hip Z (root translations m/s, all other columns deg/s): " +
                    string.Join(", ", q.Select((c, i) => (c.Name, Rate: 0.01 * tangent[i] / (score * score)))
                        .Where(item => Math.Abs(item.Rate) >= 0.001)
                        .Select(item => FormattableString.Invariant($"{item.Name} {item.Rate * (item.Name.Contains("translation") ? 1 : Mathf.Rad2Deg):F3}"))));
            return basis.Count;
        }
    }
}
