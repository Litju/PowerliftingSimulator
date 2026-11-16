using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 Gate 6: kinematic reference path of the deadlift ascent, from the
    /// qualified B2 setup to lockout, over the athlete's actual joints, anchors,
    /// frames and limits (the same parameterization as the setup solver).
    ///
    /// Every key is a pose of the closed task, not an animation frame:
    ///   hard: both feet exactly at their setup poses (planted, flat);
    ///         both palm anchors on the shaft at the key's bar position, with
    ///         the hand orientations of the setup (the grips are welded and the
    ///         bar does not rotate in a symmetric lift);
    ///         every joint inside its authored range with a margin;
    ///         no bar penetration of shins or thighs (sagittal capsule distance);
    ///         no arm/leg capsule overlap;
    ///         the athlete+bar COM over the midfoot (the centre of the sole
    ///         hull), blended from its setup value over the first 0.10 m;
    ///   preference: bar axis rising vertically from its setup Z; torso angle
    ///         scheduled from the setup value to vertical at lockout; neutral
    ///         spine and neck as at setup; straight elbows; continuity with the
    ///         previous key.
    /// The lockout key replaces the bar-height constraint by the lockout
    /// definition: knees and hips extended to their range margin, torso
    /// vertical, shoulders not ahead of the hips. Its bar height is an output.
    /// Execution is a separate, measured question: this class writes nothing.
    /// </summary>
    public sealed class DeadliftLiftPath
    {
        public sealed class Key
        {
            public float BarRiseM;
            public double[] X;
            public Dictionary<string, Quaternion> Logical;
            public double Cost, HardCost;
            public float SystemComZ, BarZ, TorsoDeg, KneeDeg, HipDeg, BarShinClearanceM;
            public bool Lockout;
            /// <summary>Homotopy weight of the lockout definition (1 at the lockout key; 0 for the bar-rise sweep).</summary>
            public float Lambda;
            public bool FreeRise;
        }

        private const int PelvisY = 0, PelvisZ = 1, PelvisPitch = 2, Abdomen = 3, Thorax = 4, Neck = 5;
        private const int LeftLeg = 6, RightLeg = 11, LeftArm = 16, RightArm = 23;

        private readonly DeadliftSetupSolver _legacy;
        private readonly BenchKinematics _kin;
        private readonly PhysicalAthleteRig _rig;
        private readonly DeadliftPlantModel _setup;
        private readonly Dictionary<string, float> _mass = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly float _athleteMass, _barMass;
        private readonly Pose _leftFoot, _rightFoot;
        private readonly Quaternion _leftHand, _rightHand;
        private readonly Vector3 _leftAnchor0, _rightAnchor0, _barCenter0;
        private readonly float _midfootZ, _setupComZ, _setupTorsoDeg;
        public readonly List<Key> Keys = new List<Key>();

        public float JointMarginDeg { get; set; } = 3f;
        public float LockoutTorsoDeg { get; set; } = 86f;
        public float BarLimbClearanceM { get; set; } = 0.006f;
        /// <summary>Hard margin of the system COM inside the sole hull's fore-aft extent.</summary>
        public float ComMarginM { get; set; } = 0.06f;
        /// <summary>
        /// Locked knee short of the hard stop: the plant tracks with a few degrees
        /// of error, and a reference at the range margin was measured to reach the
        /// -5 deg stop. Well inside the referee's 10 deg locked-knee approximation.
        /// </summary>
        public float LockoutKneeSoftDeg { get; set; } = 4f;
        public int HomotopySteps { get; set; } = 12;
        /// <summary>Largest admitted static shoulder demand along the path (fraction of capacity).</summary>
        public float ShoulderDemandMax { get; set; } = 0.85f;
        private float _shoulderCapacityNm = float.PositiveInfinity;
        private float _soleZMin, _soleZMax;

        public DeadliftLiftPath(PhysicalAthleteRig rig, DeadliftSetupSolver legacy, DeadliftPlantModel setup, float barMassKg)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));
            _setup = setup ?? throw new ArgumentNullException(nameof(setup));
            if (setup.Solution == null)
                throw new ArgumentException("the setup model carries no parameter vector");
            _kin = legacy.Kinematics;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> s in rig.Segments)
            {
                _mass[s.Key] = s.Value.Body.mass;
                _athleteMass += s.Value.Body.mass;
            }
            _barMass = barMassKg;
            _leftFoot = setup.Poses["left_foot"];
            _rightFoot = setup.Poses["right_foot"];
            _leftHand = setup.Poses["left_hand"].rotation;
            _rightHand = setup.Poses["right_hand"].rotation;
            _leftAnchor0 = setup.LeftGripWorld;
            _rightAnchor0 = setup.RightGripWorld;
            _barCenter0 = setup.BarCenterWorld;

            float zMin = float.MaxValue, zMax = float.MinValue;
            foreach (Vector3 c in SoleCorners(setup.Poses))
            {
                zMin = Mathf.Min(zMin, c.z);
                zMax = Mathf.Max(zMax, c.z);
            }
            _midfootZ = 0.5f * (zMin + zMax);
            _soleZMin = zMin;
            _soleZMax = zMax;
            _setupComZ = SystemCom(setup.Poses, _barCenter0).z;
            _setupTorsoDeg = TorsoDeg(setup.Poses);
            Vector3 knee = _kin.JointAnchorWorld(setup.Poses, "left_shank"), ankle = _kin.JointAnchorWorld(setup.Poses, "left_foot");
            Vector3 shin = knee - ankle;
            _setupShinLeanDeg = Mathf.Atan2(shin.z, shin.y) * Mathf.Rad2Deg;
            PoweredJointController.PoweredJointRuntime shoulderJoint = rig.PoweredController.GetJoint("left_upper_arm");
            _shoulderCapacityNm = shoulderJoint.Profile.HasValue ? shoulderJoint.Profile.Value.BaseCapacityNm : float.PositiveInfinity;
            _legacy.CapsuleSegment(setup.Poses, "left_shank", out _, out _, out float shankRadius);
            _kneePassageRise = ankle.y + shin.magnitude + shankRadius + DeadliftSetupSolver.AnchorBarRadius - _barCenter0.y;
        }

        public float MidfootZ => _midfootZ;
        public float SetupSystemComZ => _setupComZ;
        public float LockoutRiseM => Keys.Count > 0 ? Keys[Keys.Count - 1].BarRiseM : 0f;

        private IEnumerable<Vector3> SoleCorners(Dictionary<string, Pose> poses)
        {
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider box = _legacy.FootBox(side);
                Pose foot = poses[side + "_foot"];
                for (int i = 0; i < 4; i++)
                    yield return foot.position + foot.rotation * (box.center + new Vector3(
                        ((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f, -box.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f));
            }
        }

        private Vector3 SystemCom(Dictionary<string, Pose> poses, Vector3 barCenter)
        {
            Vector3 sum = Vector3.zero;
            foreach (KeyValuePair<string, Pose> p in poses)
                sum += _mass[p.Key] * ComOf(p.Key, p.Value);
            sum += _barMass * barCenter;
            return sum / (_athleteMass + _barMass);
        }

        private Vector3 ComOf(string id, Pose pose)
        {
            Rigidbody b = _rig.Segments[id].Body;
            Vector3 local = b.transform.InverseTransformPoint(b.worldCenterOfMass);
            return pose.position + pose.rotation * Vector3.Scale(local, b.transform.lossyScale);
        }

        private float TorsoDeg(Dictionary<string, Pose> poses)
        {
            Vector3 hip = 0.5f * (_kin.JointAnchorWorld(poses, "left_thigh") + _kin.JointAnchorWorld(poses, "right_thigh"));
            Vector3 sh = 0.5f * (_kin.JointAnchorWorld(poses, "left_upper_arm") + _kin.JointAnchorWorld(poses, "right_upper_arm"));
            Vector3 t = sh - hip;
            return Mathf.Atan2(t.y, Mathf.Abs(t.z) < 1e-6f ? 1e-6f : t.z) * Mathf.Rad2Deg;
        }

        private static Vector3 RotationError(Quaternion target, Quaternion actual)
        {
            Quaternion d = Quaternion.Inverse(target) * actual;
            if (d.w < 0f) d = new Quaternion(-d.x, -d.y, -d.z, -d.w);
            return 2f * new Vector3(d.x, d.y, d.z);
        }

        private Vector3 AnchorOf(Dictionary<string, Pose> poses, bool left)
        {
            Pose hand = poses[left ? "left_hand" : "right_hand"];
            return hand.position + hand.rotation * (left ? _setup.LeftGripAnchorLocal : _setup.RightGripAnchorLocal);
        }

        private float HandAnchorOffsetY(Dictionary<string, Pose> poses, bool left) =>
            AnchorOf(poses, left).y - (left ? _leftAnchor0.y : _rightAnchor0.y);

        private float BarShiftZ(Dictionary<string, Pose> poses) =>
            0.5f * (AnchorOf(poses, true).z - _leftAnchor0.z + AnchorOf(poses, false).z - _rightAnchor0.z);

        private float _kneePassageRise = 0.25f, _setupShinLeanDeg;

        /// <summary>Bar rise at which the bar axis clears the top of an extended knee (rig geometry).</summary>
        public float KneePassageRiseM => _kneePassageRise;

        private List<double> Residuals(double[] x, double[] previous, float rise, bool lockout, out double hardCost)
        {
            Pose pelvis = _legacy.PelvisPoseOf(x);
            Dictionary<string, Quaternion> logical = _legacy.LogicalRotations(x);
            Dictionary<string, Pose> poses = _kin.Solve(pelvis, logical);
            var r = new List<double>(160);

            foreach (bool left in new[] { true, false })
            {
                Pose target = left ? _leftFoot : _rightFoot;
                Pose foot = poses[left ? "left_foot" : "right_foot"];
                Vector3 dp = foot.position - target.position;
                Vector3 dr = RotationError(target.rotation, foot.rotation);
                r.Add(200d * dp.x); r.Add(200d * dp.y); r.Add(200d * dp.z);
                r.Add(60d * dr.x); r.Add(60d * dr.y); r.Add(60d * dr.z);
                Mark(r, (left ? "L" : "R") + " foot pose");
            }

            // Bar: both anchors share one bar rise (the bar is rigid and level);
            // the lockout key leaves the rise free and keeps the anchors level.
            float liftLeft = HandAnchorOffsetY(poses, true), liftRight = HandAnchorOffsetY(poses, false);
            float barRise = lockout ? 0.5f * (liftLeft + liftRight) : rise;
            // The bar goes where the welded hands are: its fore-aft shift is a
            // result (one shift for both hands, bar level), preferred vertical.
            float shiftZ = 0.5f * (AnchorOf(poses, true).z - _leftAnchor0.z + AnchorOf(poses, false).z - _rightAnchor0.z);
            Vector3 barCenter = _barCenter0 + new Vector3(0f, barRise, shiftZ);
            foreach (bool left in new[] { true, false })
            {
                Pose hand = poses[left ? "left_hand" : "right_hand"];
                Vector3 anchor = AnchorOf(poses, left);
                Vector3 a0 = left ? _leftAnchor0 : _rightAnchor0;
                Vector3 da = anchor - (a0 + new Vector3(0f, barRise, shiftZ));
                Vector3 dr = RotationError(left ? _leftHand : _rightHand, hand.rotation);
                r.Add(200d * da.x); r.Add(200d * da.y); r.Add(200d * da.z);
                r.Add(40d * dr.x); r.Add(40d * dr.y); r.Add(40d * dr.z);
                Mark(r, (left ? "L" : "R") + " hand on bar");
            }

            foreach (KeyValuePair<string, Quaternion> q in logical)
            {
                BenchKinematics.LimitResiduals(_kin.Joint(q.Key), q.Value, JointMarginDeg, r, 60d);
                Mark(r, "limit " + q.Key);
            }

            foreach (string side in new[] { "left", "right" })
                foreach (string seg in new[] { "_shank", "_thigh" })
                {
                    _legacy.CapsuleSegment(poses, side + seg, out Vector3 a, out Vector3 b, out float radius);
                    float d = DeadliftSetupSolver.DistancePointSegment2D(new Vector2(barCenter.y, barCenter.z), new Vector2(a.y, a.z), new Vector2(b.y, b.z));
                    r.Add(60d * Mathf.Max(0f, BarLimbClearanceM - (d - radius - DeadliftSetupSolver.AnchorBarRadius)));
                    Mark(r, "bar clearance " + side + seg);
                }

            foreach (string armSide in new[] { "left", "right" })
                foreach (string armSeg in new[] { "_upper_arm", "_forearm" })
                {
                    _legacy.CapsuleSegment(poses, armSide + armSeg, out Vector3 a0, out Vector3 a1, out float ar);
                    foreach (string legSide in new[] { "left", "right" })
                        foreach (string legSeg in new[] { "_thigh", "_shank" })
                        {
                            _legacy.CapsuleSegment(poses, legSide + legSeg, out Vector3 l0, out Vector3 l1, out float lr);
                            float gap = DeadliftSetupSolver.DistanceSegmentSegment(a0, a1, l0, l1) - ar - lr;
                            r.Add(40d * Mathf.Max(0f, 0.004f - gap));
                            Mark(r, "overlap " + armSide + armSeg + "/" + legSide + legSeg);
                        }
                }

            // Statics of the quasi-static ascent: the athlete+bar COM inside the
            // sole hull with a margin (inequality, hard). Where in the hull is a
            // coordination preference (below).
            Vector3 com = SystemCom(poses, barCenter);
            r.Add(80d * Mathf.Max(0f, (_soleZMin + ComMarginM) - com.z));
            r.Add(80d * Mathf.Max(0f, com.z - (_soleZMax - ComMarginM)));
            r.Add(80d * com.x);
            Mark(r, "system COM over midfoot");
            foreach (string side in new[] { "left", "right" })
            {
                BenchKinematics.JointInfo knee = _kin.Joint(side + "_shank");
                float kneeDeg = PoweredJointController.SignedTwistRadians(PoweredJointController.NormalizeCanonical(logical[side + "_shank"]), Vector3.right) * Mathf.Rad2Deg;
                float floor = knee.Recipe.LowDegrees + JointMarginDeg + LockoutKneeSoftDeg;
                r.Add(20d * Mathf.Max(0f, floor - kneeDeg) * Mathf.Deg2Rad);
            }
            Mark(r, "knee short of its stop");
            // Static feasibility of the arms (hard): with the bar hanging from both
            // hands (W_b / 2 each), the sagittal moment about each shoulder of the
            // hand load and the arm's own weight must stay within ShoulderDemandMax
            // of the shoulder's finite capacity. A kinematically closed lockout with
            // the hands ahead of the shoulders was measured to saturate the
            // shoulder (demand 1.06-1.25) and limit-cycle the bar (Gate 6 run 13).
            float halfBarN = 0.5f * _barMass * -Physics.gravity.y;
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "left" : "right";
                Vector3 shoulder = _kin.JointAnchorWorld(poses, side + "_upper_arm");
                float moment = halfBarN * (AnchorOf(poses, left).z - shoulder.z);
                foreach (string seg in new[] { "_upper_arm", "_forearm", "_hand" })
                    moment += _mass[side + seg] * -Physics.gravity.y * (ComOf(side + seg, poses[side + seg]).z - shoulder.z);
                r.Add(60d * Mathf.Max(0f, Mathf.Abs(moment) - ShoulderDemandMax * _shoulderCapacityNm) / _shoulderCapacityNm);
            }
            Mark(r, "shoulder static moment");

            if (lockout)
            {
                foreach (string side in new[] { "left", "right" })
                {
                    BenchKinematics.JointInfo knee = _kin.Joint(side + "_shank");
                    float kneeDeg = PoweredJointController.SignedTwistRadians(PoweredJointController.NormalizeCanonical(logical[side + "_shank"]), Vector3.right) * Mathf.Rad2Deg;
                    float straight = Mathf.Abs(knee.Recipe.LowDegrees) < Mathf.Abs(knee.Recipe.HighDegrees)
                        ? knee.Recipe.LowDegrees + JointMarginDeg + LockoutKneeSoftDeg : knee.Recipe.HighDegrees - JointMarginDeg - LockoutKneeSoftDeg;
                    r.Add(_lambda * 8d * (kneeDeg - straight) * Mathf.Deg2Rad);
                }
                Vector3 hip = 0.5f * (_kin.JointAnchorWorld(poses, "left_thigh") + _kin.JointAnchorWorld(poses, "right_thigh"));
                Vector3 sh = 0.5f * (_kin.JointAnchorWorld(poses, "left_upper_arm") + _kin.JointAnchorWorld(poses, "right_upper_arm"));
                // Shoulders back (IPF 4.3(2)): shoulder centres not ahead of the hip centres.
                r.Add(_lambda * 60d * Mathf.Max(0f, sh.z - hip.z));
                // IPF 4.3.1(1): no downward movement of the bar before the final position.
                r.Add(200d * Mathf.Max(0f, _minFreeRise - barRise));
                Mark(r, "lockout definition");
            }
            hardCost = 0d;
            foreach (double v in r) hardCost += v * v;

            // Coordination (preferences only). Knee passage first: torso angle
            // held at the setup value while the shins come back to vertical
            // under the rising bar; above the knee the hips extend the torso to
            // vertical. Spine and neck return from the setup curvature to
            // neutral over the hip-extension phase.
            float kneePhase = Mathf.Clamp01(barRise / Mathf.Max(0.05f, _kneePassageRise));
            float hipPhase = lockout ? Mathf.Lerp(_homotopyHip0, 1f, _lambda) : Mathf.Clamp01((barRise - _kneePassageRise) / Mathf.Max(0.05f, _plannedLockoutRise - _kneePassageRise));
            float torsoTarget = lockout ? Mathf.Lerp(_homotopyTorso0, LockoutTorsoDeg, _lambda) : Mathf.Lerp(_setupTorsoDeg, LockoutTorsoDeg, hipPhase);
            r.Add((2d + 6d * hipPhase) * (TorsoDeg(poses) - torsoTarget) * Mathf.Deg2Rad);
            foreach (string side in new[] { "left", "right" })
            {
                Vector3 shin = _kin.JointAnchorWorld(poses, side + "_shank") - _kin.JointAnchorWorld(poses, side + "_foot");
                float lean = Mathf.Atan2(shin.z, shin.y) * Mathf.Rad2Deg;
                r.Add(0.8d * (lean - Mathf.Lerp(_setupShinLeanDeg, 0f, kneePhase)) * Mathf.Deg2Rad);
            }
            r.Add(6d * shiftZ);
            // System COM over the midfoot; weighted up through hip extension: a lockout
            // planned 49 mm inside the heel edge was measured to tip backward on
            // arrival (Gate 6 run 9). Hips through, not a backward lean.
            r.Add((5d + 10d * hipPhase) * (com.z - Mathf.Lerp(_setupComZ, _midfootZ, Mathf.Clamp01(barRise / 0.10f))));
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "left" : "right";
                // A heavy bar hangs from straight arms: the grip under the shoulder (sagittal).
                Vector3 shoulder = _kin.JointAnchorWorld(poses, side + "_upper_arm");
                r.Add(3d * (AnchorOf(poses, left).z - shoulder.z));
                // Hip extension phase: thighs to vertical (with the shins vertical this straightens the knees).
                Vector3 thigh = _kin.JointAnchorWorld(poses, side + "_thigh") - _kin.JointAnchorWorld(poses, side + "_shank");
                r.Add(1.5d * hipPhase * Mathf.Atan2(thigh.z, thigh.y));
            }
            double[] x0 = _setup.Solution;
            foreach (int i in new[] { Abdomen, Thorax, Neck })
                r.Add(1d * (x[i] - (1d - hipPhase) * x0[i]));
            foreach (int o in new[] { LeftArm, RightArm })
                r.Add(3d * x[o + 3]);
            if (previous != null)
                for (int i = 0; i < x.Length; i++)
                    r.Add(0.1d * (x[i] - previous[i]));
            for (int i = 0; i < x.Length; i++)
                r.Add(0.003d * x[i]);
            return r;
        }

        private float _plannedLockoutRise = 0.5f;
        private List<string> _names;
        private float _minFreeRise = float.NegativeInfinity;
        private float _lambda = 1f, _homotopyHip0 = 1f, _homotopyTorso0 = 88f;

        private void Mark(List<double> r, string label)
        {
            if (_names == null) return;
            while (_names.Count < r.Count) _names.Add(label);
        }

        /// <summary>The largest hard terms of a key (label: residual), for the evidence table.</summary>
        public string HardTerms(Key key, int top = 5)
        {
            _names = new List<string>();
            int prevIndex = Keys.IndexOf(key) - 1;
            _lambda = key.FreeRise ? key.Lambda : 1f;
            List<double> r = Residuals(key.X, prevIndex >= 0 ? Keys[prevIndex].X : null, key.BarRiseM, key.FreeRise, out _);
            _lambda = 1f;
            int hard = _names.Count;
            var idx = new List<int>();
            for (int i = 0; i < hard; i++) idx.Add(i);
            idx.Sort((a, b) => Math.Abs(r[b]).CompareTo(Math.Abs(r[a])));
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Min(top, idx.Count); i++)
                if (Math.Abs(r[idx[i]]) > 1e-4)
                    sb.Append(_names[idx[i]]).Append(' ').Append(r[idx[i]].ToString("F3", CultureInfo.InvariantCulture)).Append("; ");
            _names = null;
            return sb.Length == 0 ? "all hard terms < 1e-4" : sb.ToString();
        }

        private Key Evaluate(double[] x, float rise, bool lockout, double cost, double hard)
        {
            Pose pelvis = _legacy.PelvisPoseOf(x);
            Dictionary<string, Quaternion> logical = _legacy.LogicalRotations(x);
            Dictionary<string, Pose> poses = _kin.Solve(pelvis, logical);
            float r = lockout ? 0.5f * (HandAnchorOffsetY(poses, true) + HandAnchorOffsetY(poses, false)) : rise;
            Vector3 bar = _barCenter0 + new Vector3(0f, r, BarShiftZ(poses));
            float clearance = float.MaxValue;
            foreach (string side in new[] { "left", "right" })
                foreach (string seg in new[] { "_shank", "_thigh" })
                {
                    _legacy.CapsuleSegment(poses, side + seg, out Vector3 a, out Vector3 b, out float radius);
                    clearance = Mathf.Min(clearance, DeadliftSetupSolver.DistancePointSegment2D(new Vector2(bar.y, bar.z), new Vector2(a.y, a.z), new Vector2(b.y, b.z)) - radius - DeadliftSetupSolver.AnchorBarRadius);
                }
            return new Key
            {
                BarRiseM = r, X = (double[])x.Clone(), Logical = logical, Cost = cost, HardCost = hard, Lockout = lockout,
                SystemComZ = SystemCom(poses, bar).z, BarZ = bar.z, TorsoDeg = TorsoDeg(poses), BarShinClearanceM = clearance,
                KneeDeg = PoweredJointController.SignedTwistRadians(PoweredJointController.NormalizeCanonical(logical["left_shank"]), Vector3.right) * Mathf.Rad2Deg,
                HipDeg = PoweredJointController.SignedTwistRadians(PoweredJointController.NormalizeCanonical(logical["left_thigh"]), Vector3.right) * Mathf.Rad2Deg
            };
        }

        /// <summary>
        /// Solves the lockout key first (its bar height is an output), then a
        /// warm-started sweep of keys at <paramref name="stepM"/> bar rise.
        /// </summary>
        public void Solve(float stepM = 0.01f, int iterations = 300)
        {
            Keys.Clear();
            _cumulativeDeg.Clear();
            double[] x0 = (double[])_setup.Solution.Clone();

            // Lockout: seeded from a standing configuration of the same vector.
            double[] stand = (double[])x0.Clone();
            stand[PelvisY] = x0[PelvisY] + 0.35; stand[PelvisPitch] = 0.05;
            foreach (int o in new[] { LeftLeg, RightLeg })
            {
                stand[o] = 0.15 * x0[o]; stand[o + 1] = x0[o + 1]; stand[o + 2] = x0[o + 2];
                stand[o + 3] = 0.05 * Math.Sign(x0[o + 3]); stand[o + 4] = 0.2 * x0[o + 4];
            }
            double[] lock1 = BenchKinematics.LevenbergMarquardt(stand, v => Residuals(v, null, 0f, true, out _), iterations * 2, out double lockCost);
            Residuals(lock1, null, 0f, true, out double lockHard);
            Key lockKey = Evaluate(lock1, 0f, true, lockCost, lockHard);
            _plannedLockoutRise = lockKey.BarRiseM;

            Keys.Add(Evaluate(x0, 0f, false, 0d, 0d));
            double[] prev = x0;
            for (float rise = stepM; rise < lockKey.BarRiseM - 0.5f * stepM; rise += stepM)
            {
                float target = rise;
                double[] solved = BenchKinematics.LevenbergMarquardt(prev, v => Residuals(v, prev, target, false, out _), iterations, out double cost);
                Residuals(solved, prev, target, false, out double hard);
                Keys.Add(Evaluate(solved, target, false, cost, hard));
                prev = solved;
            }
            // Homotopy from the last swept pose to the lockout: every intermediate
            // key is a closed pose (feet planted, hands on the level bar, limits,
            // clearances, COM inside the soles); only the lockout definition and
            // the torso target ramp with lambda. A plain slerp between two distant
            // closed poses is not closed in between (Gate 6 run 3: a planted foot
            // was asked to rotate).
            Key lastSweep = Keys[Keys.Count - 1];
            _homotopyTorso0 = lastSweep.TorsoDeg;
            _homotopyHip0 = Mathf.Clamp01((lastSweep.BarRiseM - _kneePassageRise) / Mathf.Max(0.05f, _plannedLockoutRise - _kneePassageRise));
            for (int k = 1; k <= HomotopySteps; k++)
            {
                _lambda = k / (float)HomotopySteps;
                _minFreeRise = Keys[Keys.Count - 1].BarRiseM;
                double[] seed = prev;
                double[] solved = BenchKinematics.LevenbergMarquardt(seed, v => Residuals(v, seed, 0f, true, out _), iterations, out double hc);
                Residuals(solved, seed, 0f, true, out double hh);
                Key key = Evaluate(solved, 0f, true, hc, hh);
                key.Lambda = _lambda;
                key.FreeRise = true;
                key.Lockout = k == HomotopySteps;
                Keys.Add(key);
                prev = solved;
            }
            _lambda = 1f;
            _minFreeRise = float.NegativeInfinity;
        }

        private readonly List<float> _cumulativeDeg = new List<float>();

        /// <summary>Total joint-space length of the path: sum over keys of the largest single-joint rotation (deg).</summary>
        public float JointPathLengthDeg => _cumulativeDeg.Count > 0 ? _cumulativeDeg[_cumulativeDeg.Count - 1] : 0f;

        private void BuildJointArcLength()
        {
            _cumulativeDeg.Clear();
            float sum = 0f;
            for (int i = 0; i < Keys.Count; i++)
            {
                if (i > 0)
                {
                    float step = 0f;
                    foreach (KeyValuePair<string, Quaternion> q in Keys[i].Logical)
                        step = Mathf.Max(step, Quaternion.Angle(Keys[i - 1].Logical[q.Key], q.Value));
                    sum += Mathf.Max(step, 1e-3f);
                }
                _cumulativeDeg.Add(sum);
            }
        }

        /// <summary>
        /// Logical joint target at path progress s in [0, 1], slerp between keys.
        /// s is the fraction of the joint-space arc length (largest single-joint
        /// rotation), not of the bar rise: a constant progress rate is then a
        /// bounded joint speed. Near lockout a millimetre of bar rise can need
        /// degrees of knee and hip motion; parameterizing by bar rise drove the
        /// knees into their stop at ~170 deg/s (Gate 6 run 2).
        /// </summary>
        public Quaternion Target(string jointId, float s)
        {
            if (Keys.Count == 0)
                throw new InvalidOperationException("the path is not solved");
            if (_cumulativeDeg.Count != Keys.Count)
                BuildJointArcLength();
            float d = Mathf.Clamp01(s) * JointPathLengthDeg;
            int i = 0;
            while (i < Keys.Count - 2 && _cumulativeDeg[i + 1] < d) i++;
            Key a = Keys[i], b = Keys[Mathf.Min(i + 1, Keys.Count - 1)];
            float span = _cumulativeDeg[Mathf.Min(i + 1, Keys.Count - 1)] - _cumulativeDeg[i];
            float t = span > 1e-6f ? Mathf.Clamp01((d - _cumulativeDeg[i]) / span) : 1f;
            return Quaternion.Slerp(a.Logical[jointId], b.Logical[jointId], t);
        }

        /// <summary>Planned bar rise at progress s (same arc-length parameterization), for reports.</summary>
        public float BarRiseAt(float s)
        {
            if (_cumulativeDeg.Count != Keys.Count)
                BuildJointArcLength();
            float d = Mathf.Clamp01(s) * JointPathLengthDeg;
            int i = 0;
            while (i < Keys.Count - 2 && _cumulativeDeg[i + 1] < d) i++;
            int j = Mathf.Min(i + 1, Keys.Count - 1);
            float span = _cumulativeDeg[j] - _cumulativeDeg[i];
            return Mathf.Lerp(Keys[i].BarRiseM, Keys[j].BarRiseM, span > 1e-6f ? Mathf.Clamp01((d - _cumulativeDeg[i]) / span) : 1f);
        }

        public bool Has(string jointId) => Keys.Count > 0 && Keys[0].Logical.ContainsKey(jointId);

        public string Describe()
        {
            var b = new StringBuilder();
            b.AppendLine(string.Format(CultureInfo.InvariantCulture, "lift path: {0} keys, lockout rise {1:F3} m, knee passage rise {4:F3} m, midfoot z {2:F4} m, setup system COM z {3:F4} m",
                Keys.Count, LockoutRiseM, _midfootZ, _setupComZ, _kneePassageRise));
            b.AppendLine("rise m   hard cost   cost      sysCOM z  bar z    torso deg  knee deg  hip deg  bar-limb clr mm  lockout");
            foreach (Key k in Keys)
                b.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,6:F3}  {1,9:E2}  {2,9:E2}  {3,8:F4}  {4,7:F4}  {5,8:F1}  {6,8:F1}  {7,7:F1}  {8,10:F1}  {9}",
                    k.BarRiseM, k.HardCost, k.Cost, k.SystemComZ, k.BarZ, k.TorsoDeg, k.KneeDeg, k.HipDeg, k.BarShinClearanceM * 1000f, k.Lockout));
            b.AppendLine("largest hard terms (keys with hard cost > 1e-3):");
            for (int i = 0; i < Keys.Count; i++)
                if (Keys[i].HardCost > 1e-3 && (i % 5 == 0 || Keys[i].Lockout))
                    b.AppendLine(string.Format(CultureInfo.InvariantCulture, "  rise {0:F3}: {1}", Keys[i].BarRiseM, HardTerms(Keys[i])));
            return b.ToString();
        }
    }
}
