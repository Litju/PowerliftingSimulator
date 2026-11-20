using System;
using System.Collections.Generic;
using System.Linq;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>Closes the existing chains around the feet and welded bar observed at separation.</summary>
    public sealed class DeadliftPreparedAscentPlan
    {
        public const float QualifiedRiseM = 0.12f;
        private const float FirstPlanRiseM = 0.15f;
        private const float PlanClearanceM = 0.024f;
        private const int FirstIntervals = 15;
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
        private readonly Sample[] _keys;
        private readonly float _progressStep;
        private readonly float _startProgress;
        private readonly Sample _lockout;
        private readonly Vector3 _barTangent;
        private readonly Func<string, float, Quaternion> _entryTarget;
        private readonly float _lateStart;
        private readonly bool _complete;
        private readonly (string Id, Vector3 Center, float Mass)[] _bodyMasses;
        private readonly bool _centered;
        private double[] _parameters;
        internal int SolvedKeys { get; private set; }
        internal int KeyCount => _keys.Length;

        public sealed class Sample
        {
            public Pose Root, Bar;
            public Dictionary<string, Quaternion> Logical;
            public Dictionary<string, Pose> Poses;
        }

        /// <summary>
        /// The measured plant at the physical event that anchors this reference:
        /// pelvis, bar and planted-foot poses, welded grip frames, realized joint
        /// rotations and, for the post-knee continuation, the moving lift state.
        /// Besides the declared athlete, bar and stock-path model it is the only
        /// input of the closed-chain objective.
        /// </summary>
        public sealed class Anchor
        {
            public Pose Root, Bar;
            public Pose[] Feet;
            public Quaternion[] GripRotations;
            public string[] JointIds;
            public Quaternion[] Measured;
            public float StartProgress, LiftRate;
            public Vector3 BarTangent;

            internal static Anchor Capture(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, bool continuation)
            {
                Pose bar = BodyPose(runtime.Bar);
                var joints = rig.PoweredController.Joints;
                var anchor = new Anchor
                {
                    Root = BodyPose(runtime.Pelvis),
                    Bar = bar,
                    Feet = new[] { BodyPose(rig.Segments["left_foot"].Body), BodyPose(rig.Segments["right_foot"].Body) },
                    GripRotations = new[] { runtime.LeftGrip, runtime.RightGrip }
                        .Select(grip => Quaternion.Inverse(bar.rotation) * grip.Joint.transform.rotation).ToArray(),
                    JointIds = joints.Select(j => j.Id).ToArray(),
                    Measured = joints.Select(j => j.PostPhysicsDiagnostic.ActualRelative).ToArray()
                };
                if (continuation)
                {
                    anchor.StartProgress = runtime.Pull.LiftProgress01;
                    anchor.LiftRate = runtime.Pull.LiftRate01PerSecond;
                    float span = 1f - anchor.StartProgress;
                    if (span > 0f && anchor.LiftRate > 0f)
                        anchor.BarTangent = runtime.Bar.linearVelocity * (span / anchor.LiftRate);
                }
                return anchor;
            }

            /// <summary>Exact equality: the admitted state is the state the reference was generated from.</summary>
            public bool IdenticalTo(Anchor other)
            {
                if (other == null || !Same(Root, other.Root) || !Same(Bar, other.Bar) || StartProgress != other.StartProgress ||
                    LiftRate != other.LiftRate || !Same(BarTangent, other.BarTangent) || JointIds.Length != other.JointIds.Length)
                    return false;
                for (int i = 0; i < 2; i++)
                    if (!Same(Feet[i], other.Feet[i]) || !Same(GripRotations[i], other.GripRotations[i]))
                        return false;
                for (int i = 0; i < JointIds.Length; i++)
                    if (JointIds[i] != other.JointIds[i] || !Same(Measured[i], other.Measured[i]))
                        return false;
                return true;
            }

            private static bool Same(Vector3 a, Vector3 b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z);
            private static bool Same(Pose a, Pose b) => Same(a.position, b.position) && Same(a.rotation, b.rotation);
            private static bool Same(Quaternion a, Quaternion b) => a.x.Equals(b.x) && a.y.Equals(b.y) && a.z.Equals(b.z) && a.w.Equals(b.w);
        }

        public Anchor State { get; }
        public bool IsContinuation => _lockout != null;
        public Sample Lockout => _lockout;
        internal Sample Key(int index) => _keys[index];
        internal float KeyProgress(int index) => ProgressOf(index);

        public DeadliftPreparedAscentPlan(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, bool throughKnee, bool complete = false, bool centered = false, float endProgress = 1f)
            : this(runtime, rig, throughKnee, complete, centered, endProgress, incremental: false)
        {
        }

        public static DeadliftPreparedAscentPlan ContinueToLockout(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig,
            DeadliftLiftPath.Key endpoint, Quaternion barRotation, bool incremental = false) =>
            new DeadliftPreparedAscentPlan(runtime, rig, true, false, false, 1f, incremental, LockoutSample(runtime, endpoint, barRotation));

        /// <summary>The suspended close-bar endpoint as the continuation's terminal sample.</summary>
        public static Sample LockoutSample(DeadliftAttemptRuntime runtime, DeadliftLiftPath.Key endpoint, Quaternion barRotation) => new Sample
        {
            Root = runtime.Measurements.PelvisPoseOf(endpoint.X),
            Bar = new Pose(runtime.Path.GeometryOf(endpoint).BarCenter, barRotation),
            Logical = endpoint.Logical
        };

        internal DeadliftPreparedAscentPlan(DeadliftAttemptRuntime runtime, PhysicalAthleteRig rig, bool throughKnee, bool complete,
            bool centered, float endProgress, bool incremental, Sample lockout = null, Anchor state = null)
        {
            _runtime = runtime;
            _complete = complete;
            _centered = centered;
            _bodyMasses = rig.Segments.Select(s => (s.Key, s.Value.Body.centerOfMass, s.Value.Body.mass)).ToArray();
            _measurements = runtime.Measurements;
            _kinematics = _measurements.Kinematics;
            State = state ?? Anchor.Capture(runtime, rig, lockout != null);
            _root0 = State.Root;
            _bar0 = State.Bar;
            _feet = State.Feet;
            _grips = new[] { runtime.LeftGrip, runtime.RightGrip };
            _gripRotations = State.GripRotations;
            _shaft = DeadliftBarLegGeometry.Shaft(runtime.Bar);
            _measured = new Dictionary<string, Quaternion>();
            for (int i = 0; i < State.JointIds.Length; i++)
                _measured.Add(State.JointIds[i], State.Measured[i]);
            _channels = rig.PoweredController.Joints
                .Where(j => j.Id != "head_neck")
                .Select(j => (j.Id, j.Recipe.Family == "ankle" ? 2 : BenchKinematics.ParameterCount(_kinematics.Joint(j.Id))))
                .ToArray();

            _progressStep = ProgressAtRise(FirstPlanRiseM) / FirstIntervals;
            int kneeIntervals = Mathf.CeilToInt(ProgressAtRise(runtime.Path.KneePassageRiseM + 0.035f) / _progressStep);
            _lateStart = _progressStep * kneeIntervals;
            if (complete && !(endProgress > _lateStart && endProgress <= 1f))
                throw new ArgumentOutOfRangeException(nameof(endProgress));
            int intervals = complete ? Mathf.CeilToInt(endProgress / _progressStep) : throughKnee ? kneeIntervals : FirstIntervals;
            _keys = new Sample[intervals + 1];

            if (lockout != null)
            {
                _startProgress = State.StartProgress;
                float span = 1f - _startProgress;
                if (!(span > 0f && State.LiftRate > 0f))
                    throw new InvalidOperationException("The continuation begins at moving knee passage.");
                int remainingIntervals = Mathf.CeilToInt(span / _progressStep);
                _progressStep = span / remainingIntervals;
                _keys = new Sample[remainingIntervals + 1];
                _barTangent = State.BarTangent;
                _entryTarget = runtime.Hold.AscentReferenceOverride;
                _lockout = lockout;
            }

            _parameters = new double[ParameterCount];
            if (!incremental)
                while (!SolveNextKey()) { }
        }

        internal int ParameterCount => 6 + _channels.Sum(j => j.Count);

        internal bool SolveNextKey()
        {
            if (SolvedKeys == _keys.Length)
                return true;

            float progress = ProgressOf(SolvedKeys);
            if (SolvedKeys > 0)
            {
                var nominal = Nominal(progress);
                _parameters = BenchKinematics.LevenbergMarquardt(_parameters,
                    v => Residuals(PoseAt(v, progress), nominal, progress), 130, out _);
            }
            _keys[SolvedKeys++] = PoseAt(_parameters, progress);
            return SolvedKeys == _keys.Length;
        }

        private Dictionary<string, Quaternion> Nominal(float progress) => _lockout == null
            ? _measured.ToDictionary(q => q.Key,
                q => q.Value * Quaternion.Inverse(_runtime.Model.SetupLogical[q.Key]) * _runtime.Path.Target(q.Key, progress))
            : _measured.ToDictionary(q => q.Key,
                q => Quaternion.Slerp(q.Value, _lockout.Logical[q.Key], Mathf.InverseLerp(_startProgress, 1f, progress)));

        internal Dictionary<string, Quaternion> NominalAt(int index) => Nominal(ProgressOf(index));

        /// <summary>Installs keys generated offline for this anchor state, without solving.</summary>
        internal void UseKeys(IReadOnlyList<(Pose Root, Pose Bar, Dictionary<string, Quaternion> Logical)> keys)
        {
            if (keys.Count != _keys.Length)
                throw new ArgumentException($"Expected {_keys.Length} keys, got {keys.Count}.");
            _travelBounds = null;
            for (int i = 0; i < keys.Count; i++)
                _keys[i] = new Sample { Root = keys[i].Root, Bar = keys[i].Bar, Logical = keys[i].Logical, Poses = _kinematics.Solve(keys[i].Root, keys[i].Logical) };
            SolvedKeys = _keys.Length;
        }

        /// <summary>
        /// The closed-chain objective for a configuration seeded at
        /// <paramref name="seed"/> and displaced by <paramref name="x"/> in the
        /// root and joint-channel coordinates the generator solves in.
        /// </summary>
        internal List<double> AnchoredResiduals(Sample seed, double[] x, int index, Dictionary<string, Quaternion> nominal)
        {
            float progress = ProgressOf(index);
            return Residuals(AnchoredPose(seed, x, progress), nominal, progress);
        }

        internal Sample AnchoredPose(Sample seed, double[] x, float progress)
        {
            var root = new Pose(seed.Root.position + Vector(x, 0), seed.Root.rotation * Quaternion.Euler(Vector(x, 3) * Mathf.Rad2Deg));
            var logical = new Dictionary<string, Quaternion>(_measured);
            int k = 6;
            foreach ((string id, int count) in _channels)
            {
                Vector3 delta = count == 3 ? Vector(x, k) : new Vector3((float)x[k], 0f, count == 2 ? (float)x[k + 1] : 0f);
                logical[id] = seed.Logical[id] * Quaternion.Euler(delta * Mathf.Rad2Deg);
                k += count;
            }
            return new Sample { Root = root, Bar = BarAt(progress), Logical = logical, Poses = _kinematics.Solve(root, logical) };
        }

        /// <summary>Installs a key produced by the bounded runtime correction.</summary>
        internal void SetKey(int index, Sample sample)
        {
            _travelBounds = null;
            _keys[index] = sample;
            SolvedKeys = Mathf.Max(SolvedKeys, index + 1);
        }

        public float LateStart => _lateStart;

        private float ProgressOf(int index) => _lockout != null && index == _keys.Length - 1
            ? 1f : Mathf.Min(1f, _startProgress + _progressStep * index);

        public float SystemComZ(float progress)
        {
            Sample sample = At(progress);
            float mass = _runtime.Bar.mass;
            float moment = mass * sample.Bar.position.z;
            foreach (var body in _bodyMasses)
            {
                mass += body.Mass;
                moment += body.Mass * Point(sample.Poses[body.Id], body.Center).z;
            }
            return moment / mass;
        }

        private float ProgressAtRise(float rise)
        {
            float lo = 0f, hi = 1f;
            for (int i = 0; i < 24; i++)
            {
                float m = (lo + hi) * 0.5f;
                if (_runtime.Path.BarRiseAt(m) < rise)
                    lo = m;
                else
                    hi = m;
            }
            return hi;
        }

        private Sample PoseAt(double[] x, float progress)
        {
            var root = new Pose(_root0.position + Vector(x, 0), _root0.rotation * Quaternion.Euler(Vector(x, 3) * Mathf.Rad2Deg));
            var bar = BarAt(progress);
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

        private Pose BarAt(float progress)
        {
            Vector3 barPosition = _bar0.position + Vector3.up * _runtime.Path.BarRiseAt(progress);
            if (_complete && progress > _lateStart)
            {
                _runtime.Path.KeySpan(progress, out int a, out int b, out float t);
                float nominalZ = Mathf.Lerp(_runtime.Path.Keys[a].BarZ, _runtime.Path.Keys[b].BarZ, t);
                float late = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_lateStart, 1f, progress));
                barPosition.z = Mathf.Lerp(barPosition.z, nominalZ - 0.010f * late, late);
            }
            Quaternion orientation = _bar0.rotation;
            if (_lockout != null)
            {
                float t = Mathf.InverseLerp(_startProgress, 1f, progress);
                float u = 1f - t;
                barPosition = Vector3.LerpUnclamped(_bar0.position, _lockout.Bar.position, t * t * (3f - 2f * t)) + t * u * u * _barTangent;
                orientation = Quaternion.Slerp(_bar0.rotation, _lockout.Bar.rotation, t * t * (3f - 2f * t));
            }
            return new Pose(barPosition, orientation);
        }

        private List<double> Residuals(Sample sample, Dictionary<string, Quaternion> nominal, float progress)
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
            if (_centered && progress > _lateStart)
            {
                float late = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_lateStart, 1f, progress));
                _runtime.Path.KeySpan(progress, out int a, out int b, out float t);
                float nominalZ = Mathf.Lerp(_runtime.Path.Keys[a].SystemComZ, _runtime.Path.Keys[b].SystemComZ, t);
                float mass = _runtime.Bar.mass;
                float moment = mass * sample.Bar.position.z;
                foreach (var body in _bodyMasses)
                {
                    mass += body.Mass;
                    moment += Point(sample.Poses[body.Id], body.Center).z * body.Mass;
                }
                residuals.Add(60d * late * (moment / mass - (nominalZ + late * 0.025f)));

                float lead = Legs.Where(id => id.EndsWith("_thigh")).Average(id =>
                    _kinematics.JointAnchorWorld(sample.Poses, id.Replace("_thigh", "_upper_arm")).z -
                    _kinematics.JointAnchorWorld(sample.Poses, id).z);
                residuals.Add(65d * late * Mathf.Max(0f, lead - 0.010f));
            }
            if (_lockout != null)
            {
                float t = Mathf.InverseLerp(_startProgress, 1f, progress);
                Pose root = new Pose(Vector3.Lerp(_root0.position, _lockout.Root.position, t),
                    Quaternion.Slerp(_root0.rotation, _lockout.Root.rotation, t));
                Add(residuals, sample.Root.position - root.position, 20d);
                Add(residuals, RotationError(root.rotation, sample.Root.rotation), 6d);
                Collider trunk = _kinematics.Rig.Segments["abdomen"].Collider;
                Pose abdomen = sample.Poses["abdomen"];
                foreach (string id in new[] { "left_forearm", "right_forearm" })
                {
                    Pose arm = sample.Poses[id];
                    Collider collider = _kinematics.Rig.Segments[id].Collider;
                    bool overlap = Physics.ComputePenetration(trunk, abdomen.position, abdomen.rotation,
                        collider, arm.position, arm.rotation, out _, out float depth);
                    residuals.Add(overlap ? 2000d * depth : 0d);
                }
            }
            return residuals;
        }

        public Sample At(float progress)
        {
            Span(progress, out int i, out float t);
            Sample a = _keys[i], b = _keys[i + 1];
            var root = new Pose(Vector3.Lerp(a.Root.position, b.Root.position, t), Quaternion.Slerp(a.Root.rotation, b.Root.rotation, t));
            Quaternion orientation = _lockout == null ? _bar0.rotation : Quaternion.Slerp(a.Bar.rotation, b.Bar.rotation, t);
            var bar = new Pose(Vector3.Lerp(a.Bar.position, b.Bar.position, t), orientation);
            var logical = a.Logical.ToDictionary(q => q.Key, q => Quaternion.Slerp(q.Value, b.Logical[q.Key], t));
            return new Sample { Root = root, Bar = bar, Logical = logical, Poses = _kinematics.Solve(root, logical) };
        }

        public Quaternion Target(string joint, float progress)
        {
            Span(progress, out int i, out float t);
            Quaternion raw = Quaternion.Slerp(_keys[i].Logical[joint], _keys[i + 1].Logical[joint], t);
            Quaternion initial = _lockout == null ? _runtime.Model.SetupLogical[joint] : _entryTarget(joint, _startProgress);
            Quaternion startCorrection = Quaternion.Inverse(_keys[0].Logical[joint]) * initial;
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - _startProgress) / 0.035f));
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

        /// <summary>Running result of the interpolation contract over the intervals measured so far.</summary>
        internal struct InterpolationCheck
        {
            public float MinGap, MinBound, MaxClosure, MaxRotation;
            public int Intervals;
            public static InterpolationCheck Empty => new InterpolationCheck
            {
                MinGap = float.PositiveInfinity, MinBound = float.PositiveInfinity, MaxClosure = 0f, MaxRotation = 0f
            };
            public (float MinGap, float SweptGap, float PositionError, float RotationError) Result => (MinGap, MinBound, MaxClosure, MaxRotation);
        }

        private const int Subdivisions = 16;
        private float[] _travelBounds;

        internal int IntervalCount => _keys.Length - 1;

        public (float MinGap, float SweptGap, float PositionError, float RotationError) MeasureInterpolation()
        {
            var check = InterpolationCheck.Empty;
            for (int interval = 0; interval < IntervalCount; interval++)
                MeasureInterval(interval, ref check);
            return check.Result;
        }

        /// <summary>
        /// Samples one key interval (16 subdivisions, the final key on the last interval)
        /// against the closure and conservative swept-clearance contract. Needs keys up to
        /// <paramref name="interval"/> + 1; intervals measured in any grouping give the same
        /// result as one complete pass.
        /// </summary>
        internal void MeasureInterval(int interval, ref InterpolationCheck check)
        {
            int intervals = IntervalCount;
            if (_travelBounds == null || _travelBounds.Length != intervals)
                _travelBounds = Enumerable.Repeat(float.NaN, intervals).ToArray();
            for (int i = Mathf.Max(0, interval - 1); i <= interval; i++)
                if (float.IsNaN(_travelBounds[i]))
                    _travelBounds[i] = KeyTravelBound(_keys[i], _keys[i + 1]);
            int last = interval == intervals - 1 ? intervals * Subdivisions : interval * Subdivisions + Subdivisions - 1;
            for (int n = interval * Subdivisions; n <= last; n++)
            {
                float start = ProgressOf(interval);
                float end = _lockout == null ? Mathf.Min(start + _progressStep, 1f) : ProgressOf(interval + 1);
                Sample sample = At(Mathf.Lerp(start, end, (n - interval * Subdivisions) / (float)Subdivisions));
                float gap = Gaps(sample).Leg;
                check.MinGap = Mathf.Min(check.MinGap, gap);
                check.MinBound = Mathf.Min(check.MinBound, gap - _travelBounds[interval] / (2f * Subdivisions));
                if (n > 0 && n % Subdivisions == 0)
                    check.MinBound = Mathf.Min(check.MinBound, gap - _travelBounds[n / Subdivisions - 1] / (2f * Subdivisions));
                for (int i = 0; i < 2; i++)
                {
                    string prefix = i == 0 ? "left_" : "right_";
                    Pose foot = sample.Poses[prefix + "foot"];
                    Pose hand = sample.Poses[prefix + "hand"];
                    check.MaxClosure = Mathf.Max(check.MaxClosure, Vector3.Distance(_feet[i].position, foot.position),
                        Vector3.Distance(Point(hand, _grips[i].HandAnchorLocal), Point(sample.Bar, _grips[i].BarAnchorLocal)));
                    check.MaxRotation = Mathf.Max(check.MaxRotation, Mathf.Rad2Deg * Radians(_feet[i].rotation, foot.rotation),
                        Mathf.Rad2Deg * Radians(sample.Bar.rotation * _gripRotations[i], hand.rotation));
                }
            }
            check.Intervals = interval + 1;
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

            return Vector3.Distance(a.Bar.position, b.Bar.position) +
                (_lockout == null ? 0f : (0.5f * _shaft.height + _shaft.center.magnitude) * Radians(a.Bar.rotation, b.Bar.rotation)) + Legs.Max(id =>
            {
                _measurements.CapsuleSegment(a.Poses, id, out Vector3 p0, out Vector3 p1, out _);
                float lever = Mathf.Max(Vector3.Distance(a.Poses[id].position, p0), Vector3.Distance(a.Poses[id].position, p1));
                return travel[id].Linear + travel[id].Angular * lever;
            });
        }

        private static float Radians(Quaternion a, Quaternion b)
        {
            Vector3 half = RotationError(a, b) * 0.5f;
            return 2f * Mathf.Asin(Mathf.Min(1f, half.magnitude));
        }

        public Quaternion RejoinTarget(string joint, float progress, float end)
        {
            if (progress <= LateStart)
                return Target(joint, progress);
            if (progress >= end)
                return _runtime.Path.Target(joint, progress);
            float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(LateStart, end, progress));
            return Quaternion.Slerp(Target(joint, progress), _runtime.Path.Target(joint, progress), blend);
        }

        private void Span(float progress, out int i, out float t)
        {
            float key = Mathf.Clamp((progress - _startProgress) / _progressStep, 0f, _keys.Length - 1);
            i = Mathf.Min(Mathf.FloorToInt(key), _keys.Length - 2);
            // Keep the qualified prefix interpolation exactly when adding late keys.
            t = _lockout != null ? Mathf.InverseLerp(ProgressOf(i), ProgressOf(i + 1), progress)
                : (i + 1) * _progressStep > 1f ? Mathf.InverseLerp(i * _progressStep, 1f, progress) : key - i;
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
