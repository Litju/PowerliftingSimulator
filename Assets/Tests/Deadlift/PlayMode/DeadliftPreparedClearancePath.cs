using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>Gate 3 probe. Close the existing chains around the feet and welded bar observed at separation.</summary>
    internal sealed class DeadliftPreparedClearancePath
    {
        public const float QualifiedRiseM = 0.12f;
        private const float PlanRiseM = 0.15f;
        private const float PlanClearanceM = 0.024f;
        private const int Intervals = 15;
        private static readonly string[] Legs = { "left_shank", "right_shank", "left_thigh", "right_thigh" };

        private readonly DeadliftAttemptRuntime _runtime;
        private readonly DeadliftSetupSolver _measurements;
        private readonly BenchKinematics _kinematics;
        private readonly Pose _root0;
        private readonly Pose _bar0;
        private readonly Pose[] _feet;
        private readonly Quaternion[] _gripRotations;
        private readonly BenchGrip[] _grips;
        private readonly CapsuleCollider _shaft;
        private readonly Dictionary<string, Quaternion> _measured;
        private readonly (string Id, int Count)[] _channels;
        private readonly Sample[] _keys = new Sample[Intervals + 1];
        private readonly float _endProgress;

        internal sealed class Sample
        {
            public Pose Root, Bar;
            public Dictionary<string, Quaternion> Logical;
            public Dictionary<string, Pose> Poses;
        }

        public DeadliftPreparedClearancePath(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig)
        {
            _runtime = runtime;
            _measurements = runtime.Measurements;
            _kinematics = _measurements.Kinematics;
            _root0 = BodyPose(runtime.Pelvis);
            _bar0 = BodyPose(runtime.Bar);
            _feet = new[] { BodyPose(rig.Segments["left_foot"].Body), BodyPose(rig.Segments["right_foot"].Body) };
            _grips = new[] { runtime.LeftGrip, runtime.RightGrip };
            _gripRotations = _grips.Select(grip => Quaternion.Inverse(_bar0.rotation) * grip.Joint.transform.rotation).ToArray();
            _shaft = DeadliftBarLegGeometry.Shaft(runtime.Bar);
            _measured = rig.PoweredController.Joints.ToDictionary(j => j.Id, j => j.PostPhysicsDiagnostic.ActualRelative);
            _channels = rig.PoweredController.Joints
                .Where(j => j.Id != "head_neck")
                .Select(j => (j.Id, j.Recipe.Family == "ankle" ? 2 : BenchKinematics.ParameterCount(_kinematics.Joint(j.Id))))
                .ToArray();

            float lo = 0f, hi = 1f;
            for (int i = 0; i < 24; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (runtime.Path.BarRiseAt(m) < PlanRiseM)
                    lo = m;
                else
                    hi = m;
            }
            _endProgress = hi;

            var x = new double[6 + _channels.Sum(j => j.Count)];
            for (int i = 0; i <= Intervals; i++)
            {
                float progress = Progress(i);
                var nominal = _measured.ToDictionary(q => q.Key,
                    q => q.Value * Quaternion.Inverse(runtime.Model.SetupLogical[q.Key]) * runtime.Path.Target(q.Key, progress));
                if (i > 0)
                    x = BenchKinematics.LevenbergMarquardt(x, v => Residuals(PoseAt(v, progress), nominal), 130, out _);
                _keys[i] = PoseAt(x, progress);
            }
        }

        private float Progress(int key) => _endProgress * key / Intervals;

        private Sample PoseAt(double[] x, float progress)
        {
            var root = new Pose(_root0.position + Vector(x, 0), _root0.rotation * Quaternion.Euler(Vector(x, 3) * Mathf.Rad2Deg));
            var bar = new Pose(_bar0.position + Vector3.up * _runtime.Path.BarRiseAt(progress), _bar0.rotation);
            var logical = new Dictionary<string, Quaternion>(_measured);
            int k = 6;
            foreach ((string id, int count) in _channels)
            {
                Vector3 delta = count == 3 ? Vector(x, k) : new Vector3((float)x[k], 0f, count == 2 ? (float)x[k + 1] : 0f);
                logical[id] *= Quaternion.Euler(delta * Mathf.Rad2Deg);
                k += count;
            }
            return new Sample { Root = root, Bar = bar, Logical = logical, Poses = _kinematics.Solve(root, logical) };
        }

        private List<double> Residuals(Sample sample, Dictionary<string, Quaternion> nominal)
        {
            var residuals = new List<double>(100);
            for (int i = 0; i < 2; i++)
            {
                string prefix = i == 0 ? "left_" : "right_";
                Pose foot = sample.Poses[prefix + "foot"];
                Pose hand = sample.Poses[prefix + "hand"];
                BenchGrip grip = _grips[i];
                Add(residuals, foot.position - _feet[i].position, 1000d);
                Add(residuals, RotationError(_feet[i].rotation, foot.rotation), 300d);
                Add(residuals, Point(hand, grip.HandAnchorLocal) - Point(sample.Bar, grip.BarAnchorLocal), 1000d);
                Add(residuals, RotationError(sample.Bar.rotation * _gripRotations[i], hand.rotation), 300d);
            }

            DeadliftBarLegGeometry.Gaps gaps = Gaps(sample);
            foreach (float gap in new[] { gaps.LeftShin, gaps.RightShin, gaps.LeftThigh, gaps.RightThigh })
                residuals.Add(600d * Mathf.Max(0f, PlanClearanceM - gap));

            foreach ((string id, int count) in _channels)
            {
                Quaternion q = sample.Logical[id];
                BenchKinematics.LimitResiduals(_kinematics.Joint(id), q, 3f, residuals, 80d);
                if (count == 2)
                {
                    float roll = PoweredJointController.SignedEulerZ(q);
                    residuals.Add(80d * Mathf.Deg2Rad * Mathf.Max(0f, Mathf.Abs(roll) - (DeadliftAnkleRoll.LimitDegrees - 3f)));
                }
                Add(residuals, RotationError(nominal[id], q), count == 2 ? 4d : 2d);
            }
            return residuals;
        }

        public Sample At(float progress)
        {
            Span(progress, out int i, out float t);
            Sample a = _keys[i], b = _keys[i + 1];
            var root = new Pose(Vector3.Lerp(a.Root.position, b.Root.position, t), Quaternion.Slerp(a.Root.rotation, b.Root.rotation, t));
            var bar = new Pose(Vector3.Lerp(a.Bar.position, b.Bar.position, t), _bar0.rotation);
            var logical = a.Logical.ToDictionary(q => q.Key, q => Quaternion.Slerp(q.Value, b.Logical[q.Key], t));
            return new Sample { Root = root, Bar = bar, Logical = logical, Poses = _kinematics.Solve(root, logical) };
        }

        public Quaternion Target(string joint, float progress)
        {
            Span(progress, out int i, out float t);
            Quaternion raw = Quaternion.Slerp(_keys[i].Logical[joint], _keys[i + 1].Logical[joint], t);
            Quaternion startCorrection = Quaternion.Inverse(_keys[0].Logical[joint]) * _runtime.Model.SetupLogical[joint];
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.035f));
            return raw * Quaternion.Slerp(startCorrection, Quaternion.identity, blend);
        }

        public DeadliftBarLegGeometry.Gaps Gaps(Sample sample)
        {
            Vector3 center = Point(sample.Bar, _shaft.center);
            Vector3 half = sample.Bar.rotation * Vector3.right * (0.5f * _shaft.height - _shaft.radius);
            float Gap(string id)
            {
                _measurements.CapsuleSegment(sample.Poses, id, out Vector3 a, out Vector3 b, out float radius);
                return DeadliftSetupSolver.DistanceSegmentSegment(center - half, center + half, a, b) - radius - _shaft.radius;
            }
            return new DeadliftBarLegGeometry.Gaps
            {
                LeftShin = Gap("left_shank"), RightShin = Gap("right_shank"),
                LeftThigh = Gap("left_thigh"), RightThigh = Gap("right_thigh")
            };
        }

        public void CheckInterpolation()
        {
            const int subdivisions = 16;
            float minGap = float.PositiveInfinity, minBound = float.PositiveInfinity, maxClosure = 0f, maxRotation = 0f;
            var perInterval = Enumerable.Range(0, Intervals).Select(i => KeyTravelBound(_keys[i], _keys[i + 1])).ToArray();
            for (int n = 0; n <= Intervals * subdivisions; n++)
            {
                Sample sample = At(_endProgress * n / (Intervals * subdivisions));
                float gap = Gaps(sample).Leg;
                minGap = Mathf.Min(minGap, gap);
                minBound = Mathf.Min(minBound, gap - perInterval[Mathf.Min(n / subdivisions, Intervals - 1)] / (2f * subdivisions));
                if (n > 0 && n % subdivisions == 0)
                    minBound = Mathf.Min(minBound, gap - perInterval[n / subdivisions - 1] / (2f * subdivisions));
                for (int i = 0; i < 2; i++)
                {
                    string prefix = i == 0 ? "left_" : "right_";
                    Pose foot = sample.Poses[prefix + "foot"];
                    Pose hand = sample.Poses[prefix + "hand"];
                    maxClosure = Mathf.Max(maxClosure, Vector3.Distance(_feet[i].position, foot.position),
                        Vector3.Distance(Point(hand, _grips[i].HandAnchorLocal), Point(sample.Bar, _grips[i].BarAnchorLocal)));
                    maxRotation = Mathf.Max(maxRotation, Mathf.Rad2Deg * Radians(_feet[i].rotation, foot.rotation),
                        Mathf.Rad2Deg * Radians(sample.Bar.rotation * _gripRotations[i], hand.rotation));
                }
            }
            Debug.Log($"Prepared closed plan: min gap {minGap * 1000f:F2} mm, swept bound {minBound * 1000f:F2} mm, max closure {maxClosure * 1000f:F3} mm / {maxRotation:F3} deg");
            Assert.That(minGap, Is.GreaterThan(0.020f));
            Assert.That(minBound, Is.GreaterThan(0f));
            Assert.That(maxClosure, Is.LessThan(0.001f));
            Assert.That(maxRotation, Is.LessThan(0.3f));
        }

        // Relative slerp adds at most its angular travel to the parent's. Sum each
        // anchor's lever-arm excursion to bound every capsule point between samples.
        private float KeyTravelBound(Sample a, Sample b)
        {
            var travel = new Dictionary<string, (float Linear, float Angular)>
            {
                ["pelvis"] = (Vector3.Distance(a.Root.position, b.Root.position), Radians(a.Root.rotation, b.Root.rotation))
            };
            foreach (string id in _kinematics.SegmentOrder.Skip(1))
            {
                BenchKinematics.JointInfo joint = _kinematics.Joint(id);
                var parent = travel[joint.ParentId];
                float angle = parent.Angular + Radians(a.Logical[id], b.Logical[id]);
                travel[id] = (parent.Linear + parent.Angular * joint.ConnectedAnchor.magnitude + angle * joint.Anchor.magnitude, angle);
            }

            return Vector3.Distance(a.Bar.position, b.Bar.position) + Legs.Max(id =>
            {
                _measurements.CapsuleSegment(a.Poses, id, out Vector3 p0, out Vector3 p1, out _);
                float lever = Mathf.Max(Vector3.Distance(a.Poses[id].position, p0), Vector3.Distance(a.Poses[id].position, p1));
                return travel[id].Linear + travel[id].Angular * lever;
            });
        }

        internal sealed class RealizedSweep
        {
            private readonly CapsuleCollider[] _colliders;
            private CapsulePose[] _previous;
            public int Samples { get; private set; }
            public float MinGap { get; private set; } = float.PositiveInfinity;
            public float MinBound { get; private set; } = float.PositiveInfinity;

            public RealizedSweep(Rigidbody bar, PhysicalAthleteRig rig)
            {
                _colliders = new[] { DeadliftBarLegGeometry.Shaft(bar) }
                    .Concat(Legs.Select(id => (CapsuleCollider)rig.Segments[id].Collider)).ToArray();
                foreach (CapsuleCollider collider in _colliders)
                {
                    Assert.That(collider.transform, Is.EqualTo(collider.attachedRigidbody.transform));
                    Assert.That(Vector3.Distance(collider.transform.lossyScale, Vector3.one), Is.LessThan(1e-5f));
                }
                Read();
                Samples = 0;
            }

            public void Read()
            {
                CapsulePose[] current = _colliders.Select(c => new CapsulePose(c)).ToArray();
                for (int i = 1; i < current.Length; i++)
                {
                    float gap = current[0].Gap(current[i]);
                    MinGap = Mathf.Min(MinGap, gap);
                    if (_previous == null)
                        continue;
                    float bound = Mathf.Min(gap, _previous[0].Gap(_previous[i]))
                        - 0.5f * (current[0].TravelFrom(_previous[0]) + current[i].TravelFrom(_previous[i]));
                    MinBound = Mathf.Min(MinBound, bound);
                }
                _previous = current;
                Samples++;
            }

            private readonly struct CapsulePose
            {
                private readonly Pose _body;
                private readonly Vector3 _a, _b;
                private readonly float _radius, _lever;

                public CapsulePose(CapsuleCollider collider)
                {
                    Rigidbody body = collider.attachedRigidbody;
                    _body = BodyPose(body);
                    _radius = collider.radius;
                    Vector3 axis = collider.direction == 0 ? Vector3.right : collider.direction == 1 ? Vector3.up : Vector3.forward;
                    Vector3 half = axis * Mathf.Max(0f, 0.5f * collider.height - _radius);
                    _a = Point(_body, collider.center - half);
                    _b = Point(_body, collider.center + half);
                    _lever = Mathf.Max((collider.center - half).magnitude, (collider.center + half).magnitude);
                }

                public float Gap(CapsulePose other) =>
                    DeadliftSetupSolver.DistanceSegmentSegment(_a, _b, other._a, other._b) - _radius - other._radius;

                public float TravelFrom(CapsulePose other) =>
                    Vector3.Distance(_body.position, other._body.position) + Radians(_body.rotation, other._body.rotation) * _lever;
            }
        }

        private static float Radians(Quaternion a, Quaternion b)
        {
            Vector3 half = RotationError(a, b) * 0.5f;
            return 2f * Mathf.Asin(Mathf.Min(1f, half.magnitude));
        }

        private void Span(float progress, out int i, out float t)
        {
            float key = Mathf.Clamp01(progress / _endProgress) * Intervals;
            i = Mathf.Min(Mathf.FloorToInt(key), Intervals - 1);
            t = key - i;
        }

        private static Vector3 RotationError(Quaternion target, Quaternion actual)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(Quaternion.Inverse(target) * actual);
            return 2f * new Vector3(q.x, q.y, q.z);
        }

        private static void Add(List<double> residuals, Vector3 v, double weight)
        {
            residuals.Add(weight * v.x);
            residuals.Add(weight * v.y);
            residuals.Add(weight * v.z);
        }

        private static Pose BodyPose(Rigidbody body) => new Pose(body.position, body.rotation);
        private static Vector3 Point(Pose pose, Vector3 local) => pose.position + pose.rotation * local;
        private static Vector3 Vector(double[] x, int i) => new Vector3((float)x[i], (float)x[i + 1], (float)x[i + 2]);
    }
}
