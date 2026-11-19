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
        /// <summary>
        /// GAM-70 bar proximity, EXPERIMENTAL and off by default (false = the qualified Gate 6
        /// path, which the shipped attempt uses). Two changes, both from the measured diagnosis
        /// (docs/evidence/GAM-70/gate9-bar-proximity.txt):
        ///  1. Kinematics: the planner also uses the plant's powered ankle
        ///     inversion/eversion DOF (<see cref="DeadliftAnkleRoll"/>, the joint's Z
        ///     channel). Without it each leg is a planar ankle+knee hinge chain in its
        ///     42 deg toed-out foot plane, which pins the hip joint centres at a fixed
        ///     world Z (-0.33 m, 0.2 m behind the ankles) for every knee angle: the
        ///     lockout then leans the legs back and hangs the bar 0.18 m off the thighs.
        ///  2. Objective: a phase-dependent bar-proximity preference on the planned
        ///     bar-thigh surface gaps (rig capsules, shaft radius) toward the declared hard
        ///     clearance <see cref="BarLimbClearanceM"/> plus the measured tracking allowance,
        ///     ramped in after knee passage, with the coordination schedule, joint speed and
        ///     path below the ramp identical to the Gate 6 path. The clearance stays a hard
        ///     inequality: the plan never rests the bar on the legs.
        /// Measured outcome (15 matched fresh-plant A/B runs, onset 301): the planned lockout
        /// bar-thigh gap falls from 179 mm to 34-82 mm and the ascent realizes it (run 13:
        /// 75 mm at s 0.76 against ~170 mm on the Gate 6 path), but no variant completed the
        /// lift: each lost a foot either in the plant's lumbar-saturation window after knee
        /// passage or through the near-standing ankle-roll regulator instability (Gate 8
        /// known limitation). It stays off until that regulator is re-identified near standing.
        /// </summary>
        public bool BarProximity { get; set; }
        /// <summary>
        /// Proximity weight on the bar-shin gap below the knee. Off: the Gate 6 path
        /// already runs the bar at the hard clearance along the shins (realized 1-22 mm,
        /// mean 22 mm), and pulling it closer was measured to drive the bar into the
        /// shins at liftoff (A/B run 1: 3.9 kN bar-leg force, thigh-support fault, feet moved).
        /// </summary>
        public float ShinProximityWeight { get; set; } = 0f;
        /// <summary>Proximity weight on the bar-thigh gap above the knee (per metre above the target).</summary>
        public float ThighProximityWeight { get; set; } = 20f;
        /// <summary>
        /// Measured tracking allowance of the thigh gap: on the qualified plant the realized
        /// bar-thigh gap came up to 18.9 mm closer than planned in the upper-thigh ascent and
        /// 12.5 mm at lockout (control A/B, onset 301). Planning to the hard clearance alone
        /// would put the bar on the thighs (IPF 4.3.1(4)).
        /// </summary>
        public float ThighTrackingAllowanceM { get; set; } = 0.019f;
        /// <summary>
        /// Bar rise past the knee passage at which the thigh proximity (and the ankle roll) start to
        /// ramp in; they reach full weight at the planned lockout. The Gate 6 plant already saturates
        /// the lumbar drive just after knee passage (abdomen 1.39, ticks 518-533); starting the approach
        /// there (A/B runs 3, 4, 7) tipped it into a lateral COP excursion and a foot unloaded.
        /// </summary>
        public float ThighRampDelayM { get; set; } = 0.08f;
        /// <summary>
        /// Progress cost of the bar's fore-aft travel (deg of arc length per cm). At the Gate 6 joint
        /// speed (46.5 deg/s) 3 deg/cm bounds the bar's horizontal speed to 0.155 m/s; without it
        /// the plan moved the bar back at 0.11 m/s inside 0.3 s, the realized bar did not follow
        /// and a foot unloaded (A/B run 10, tick 547).
        /// </summary>
        public float BarTravelDegPerCm { get; set; } = 3f;
        /// <summary>Weight of the vertical-bar preference (bar fore-aft shift from setup) on the bar-proximity path; the Gate 6 path uses 6.</summary>
        public float BarVerticalWeight { get; set; } = 30f;
        /// <summary>
        /// The Gate 6 qualified joint speed: lift rate 0.30 /s over the 155.1 deg Gate 6 path.
        /// The runtime scales the bar-proximity path's progress rates to it, so shared segments
        /// move at the qualified speed whatever this path's total length.
        /// </summary>
        public const float QualifiedJointSpeedDegPerS = 0.30f * 155.1f;
        /// <summary>Weight (per rad) holding the planned ankle roll at zero before the thigh ramp.</summary>
        public float RollHoldWeight { get; set; } = 100f;
        /// <summary>Planned bar-thigh surface gap the proximity objective approaches: the declared clearance plus the tracking allowance.</summary>
        public float ThighProximityTargetM => BarLimbClearanceM + ThighTrackingAllowanceM;
        /// <summary>
        /// Largest planned ankle roll (deg). Two measured bounds:
        ///  joint headroom - plan + the regulator's full roll-offset range (8) must stay
        ///  inside the joint limit (15) less the joint margin (3), i.e. <= 4 deg: an 8 deg
        ///  bound let plan + saturated regulator request 16.3 deg and a foot rolled off
        ///  (A/B run 2); the runtime also enforces this from the live regulator;
        ///  near-standing roll loop - with 4 deg planned at lockout the regulator's roll loop
        ///  (identified crouched, the Gate 8 known limitation) went into a ~12-tick
        ///  oscillation and a foot rolled over its edge at s 0.80 (A/B run 6); 3 deg (run 13)
        ///  realized ~5 deg and rolled a foot at s 0.795; 2 and 1 deg (runs 14, 15) did not
        ///  complete either (not monotonic). 3 deg is kept: the best realized geometry.
        /// </summary>
        public float RollPlanLimitDeg { get; set; } = 3f;
        /// <summary>Largest admitted static shoulder demand along the path (fraction of capacity).</summary>
        public float ShoulderDemandMax { get; set; } = 0.85f;
        private float _shoulderCapacityNm = float.PositiveInfinity;
        private int _baseCount;
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
            _baseCount = setup.Solution.Length;

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

        /// <summary>
        /// Logical joint targets of a path vector: the setup-solver parameterization,
        /// plus (bar-proximity path) each ankle's roll about its joint Z axis, composed
        /// as the hold composes the regulator's roll offset.
        /// </summary>
        private Dictionary<string, Quaternion> LogicalOf(double[] x)
        {
            Dictionary<string, Quaternion> q = _legacy.LogicalRotations(x);
            if (x.Length >= _baseCount + 2)
            {
                q["left_foot"] = q["left_foot"] * Quaternion.AngleAxis((float)(x[_baseCount] * Mathf.Rad2Deg), Vector3.forward);
                q["right_foot"] = q["right_foot"] * Quaternion.AngleAxis((float)(x[_baseCount + 1] * Mathf.Rad2Deg), Vector3.forward);
            }
            return q;
        }

        private List<double> Residuals(double[] x, double[] previous, float rise, bool lockout, out double hardCost, bool endpoint = false)
        {
            Pose pelvis = _legacy.PelvisPoseOf(x);
            Dictionary<string, Quaternion> logical = LogicalOf(x);
            Dictionary<string, Pose> poses = _kin.Solve(pelvis, logical);
            var r = new List<double>(160);
            double closureWeight = endpoint ? 8d : 1d;

            foreach (bool left in new[] { true, false })
            {
                Pose target = left ? _leftFoot : _rightFoot;
                Pose foot = poses[left ? "left_foot" : "right_foot"];
                Vector3 dp = foot.position - target.position;
                Vector3 dr = RotationError(target.rotation, foot.rotation);
                r.Add(closureWeight * 200d * dp.x); r.Add(closureWeight * 200d * dp.y); r.Add(closureWeight * 200d * dp.z);
                r.Add(closureWeight * 60d * dr.x); r.Add(closureWeight * 60d * dr.y); r.Add(closureWeight * 60d * dr.z);
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
                r.Add(closureWeight * 200d * da.x); r.Add(closureWeight * 200d * da.y); r.Add(closureWeight * 200d * da.z);
                r.Add(closureWeight * 40d * dr.x); r.Add(closureWeight * 40d * dr.y); r.Add(closureWeight * 40d * dr.z);
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
            if (x.Length >= _baseCount + 2)
            {
                // Planned ankle roll inside its declared range, with headroom for the regulator.
                for (int k = 0; k < 2; k++)
                    r.Add(60d * Math.Max(0d, Math.Abs(x[_baseCount + k]) - RollPlanLimitDeg * Mathf.Deg2Rad));
                Mark(r, "ankle roll range");
            }
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

            if (endpoint)
            {
                Collider abdomen = _rig.Segments["abdomen"].Collider;
                Pose trunk = poses["abdomen"];
                foreach (string id in new[] { "left_forearm", "right_forearm" })
                {
                    Collider arm = _rig.Segments[id].Collider;
                    Pose forearm = poses[id];
                    bool overlaps = Physics.ComputePenetration(abdomen, trunk.position, trunk.rotation,
                        arm, forearm.position, forearm.rotation, out _, out float depth);
                    r.Add(overlaps ? 2000d * depth : 0d);
                }
                Mark(r, "forearm/abdomen clearance");
            }

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
            // Bar-proximity ramp: 0 until ThighRampDelayM past the knee passage, 1 at the planned lockout.
            float thighRamp = !BarProximity ? 0f : lockout ? 1f
                : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_kneePassageRise + ThighRampDelayM, Mathf.Max(_kneePassageRise + ThighRampDelayM + 0.05f, _plannedLockoutRise), barRise));
            float hipPhase = lockout ? Mathf.Lerp(_homotopyHip0, 1f, _lambda) : Mathf.Clamp01((barRise - _kneePassageRise) / Mathf.Max(0.05f, _scheduleLockoutRise - _kneePassageRise));
            float torsoTarget = lockout ? Mathf.Lerp(_homotopyTorso0, LockoutTorsoDeg, _lambda) : Mathf.Lerp(_setupTorsoDeg, LockoutTorsoDeg, hipPhase);
            r.Add((2d + 6d * hipPhase) * (TorsoDeg(poses) - torsoTarget) * Mathf.Deg2Rad);
            foreach (string side in new[] { "left", "right" })
            {
                Vector3 shin = _kin.JointAnchorWorld(poses, side + "_shank") - _kin.JointAnchorWorld(poses, side + "_foot");
                float lean = Mathf.Atan2(shin.z, shin.y) * Mathf.Rad2Deg;
                r.Add(0.8d * (lean - Mathf.Lerp(_setupShinLeanDeg, 0f, kneePhase)) * Mathf.Deg2Rad);
            }
            r.Add(Mathf.Lerp(6f, BarVerticalWeight, thighRamp) * shiftZ);
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
            if (BarProximity)
            {
                // Bar proximity (preference; the clearance above is the hard bound).
                // Below the knee: close along the shins, released before the bar meets
                // the knee so passage keeps its clearance; above it: ramped in over
                // ThighRampM after the bar clears the knee, so the thighs come to the bar
                // and stay close through hip extension to lockout.
                DeadliftBarLegGeometry.Gaps gaps = DeadliftBarLegGeometry.Planned(_legacy, poses, barCenter);
                float shinW = lockout ? 0f : ShinProximityWeight * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_kneePassageRise - 0.10f, _kneePassageRise - 0.03f, barRise)));
                float thighW = ThighProximityWeight * thighRamp;
                r.Add(shinW * Mathf.Max(0f, gaps.LeftShin - BarLimbClearanceM));
                r.Add(shinW * Mathf.Max(0f, gaps.RightShin - BarLimbClearanceM));
                r.Add(thighW * Mathf.Max(0f, gaps.LeftThigh - ThighProximityTargetM));
                r.Add(thighW * Mathf.Max(0f, gaps.RightThigh - ThighProximityTargetM));
                // The ankle roll serves the thigh approach only: held at the setup's zero
                // below the knee and released with the same ramp. Free below the knee the
                // plan rolled -2 deg (hips back) and swung to +3 deg within 0.05 m after
                // passage; the regulator's roll loop saturated and a foot unloaded (A/B run 3).
                if (x.Length >= _baseCount + 2)
                    for (int k = 0; k < 2; k++)
                        r.Add(RollHoldWeight * (1f - thighRamp) * x[_baseCount + k]);
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
        /// <summary>Lockout rise that normalizes the hip-extension coordination schedule (the Gate 6 lockout).</summary>
        private float _scheduleLockoutRise = 0.5f;
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
            Dictionary<string, Quaternion> logical = LogicalOf(x);
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

        /// <summary>Solves the standing endpoint alone, without building an ascent trajectory.</summary>
        public Key SolveEndpoint(int iterations = 600)
        {
            double[] setup = (double[])_setup.Solution.Clone();
            if (BarProximity)
                Array.Resize(ref setup, _baseCount + 2);
            return SolveEndpoint(StandingSeed(setup), iterations, endpoint: true);
        }

        private Key SolveEndpoint(double[] stand, int iterations, bool endpoint = false)
        {
            double[] solution = BenchKinematics.LevenbergMarquardt(stand, v => Residuals(v, null, 0f, true, out _, endpoint), iterations, out double cost);
            Residuals(solution, null, 0f, true, out double hard, endpoint);
            return Evaluate(solution, 0f, true, cost, hard);
        }

        private static double[] StandingSeed(double[] x0)
        {
            // Lockout: seeded from a standing configuration of the same vector.
            double[] stand = (double[])x0.Clone();
            stand[PelvisY] = x0[PelvisY] + 0.35; stand[PelvisPitch] = 0.05;
            foreach (int o in new[] { LeftLeg, RightLeg })
            {
                stand[o] = 0.15 * x0[o]; stand[o + 1] = x0[o + 1]; stand[o + 2] = x0[o + 2];
                stand[o + 3] = 0.05 * Math.Sign(x0[o + 3]); stand[o + 4] = 0.2 * x0[o + 4];
            }
            return stand;
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
            if (BarProximity)
                Array.Resize(ref x0, _baseCount + 2);   // ankle roll starts at the setup's zero

            double[] stand = StandingSeed(x0);
            if (BarProximity)
            {
                // The hip-extension schedule is normalized by the Gate 6 lockout (no proximity,
                // no roll), so this path coincides with the Gate 6 path until the thigh ramp
                // starts. Normalized by its own, 0.11 m lower lockout, the trunk extended ~50 %
                // faster per unit rise right after knee passage, where the plant already
                // saturates the lumbar drive (A/B run 9: foot moved at tick 535).
                BarProximity = false;
                double[] standBase = (double[])stand.Clone();
                Array.Resize(ref standBase, _baseCount);
                double[] lock0 = BenchKinematics.LevenbergMarquardt(standBase, v => Residuals(v, null, 0f, true, out _), iterations * 2, out double c0);
                _scheduleLockoutRise = Evaluate(lock0, 0f, true, c0, 0d).BarRiseM;
                BarProximity = true;
            }
            Key lockKey = SolveEndpoint(stand, iterations * 2);
            _plannedLockoutRise = lockKey.BarRiseM;
            if (!BarProximity)
                _scheduleLockoutRise = _plannedLockoutRise;

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
            _homotopyHip0 = Mathf.Clamp01((lastSweep.BarRiseM - _kneePassageRise) / Mathf.Max(0.05f, _scheduleLockoutRise - _kneePassageRise));
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
        public float JointPathLengthDeg
        {
            get
            {
                if (_cumulativeDeg.Count != Keys.Count)
                    BuildJointArcLength();
                return _cumulativeDeg.Count > 0 ? _cumulativeDeg[_cumulativeDeg.Count - 1] : 0f;
            }
        }

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
                    // Bar-proximity path: the bar's fore-aft travel also bounds the progress, so the
                    // bar is brought back to the thighs at a bounded horizontal speed.
                    if (BarProximity)
                        step = Mathf.Max(step, BarTravelDegPerCm * 100f * Mathf.Abs(Keys[i].BarZ - Keys[i - 1].BarZ));
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
            KeySpan(s, out int i, out int j, out float t);
            return Mathf.Lerp(Keys[i].BarRiseM, Keys[j].BarRiseM, t);
        }

        /// <summary>The two keys bracketing progress s and the fraction between them (same arc-length parameterization as <see cref="Target"/>).</summary>
        public void KeySpan(float s, out int i, out int j, out float t)
        {
            if (_cumulativeDeg.Count != Keys.Count)
                BuildJointArcLength();
            float d = Mathf.Clamp01(s) * JointPathLengthDeg;
            i = 0;
            while (i < Keys.Count - 2 && _cumulativeDeg[i + 1] < d) i++;
            j = Mathf.Min(i + 1, Keys.Count - 1);
            float span = _cumulativeDeg[j] - _cumulativeDeg[i];
            t = span > 1e-6f ? Mathf.Clamp01((d - _cumulativeDeg[i]) / span) : 1f;
        }

        public bool Has(string jointId) => Keys.Count > 0 && Keys[0].Logical.ContainsKey(jointId);

        /// <summary>Planned bar-leg geometry of one key (world frame), the same definition as the realized plant measurement.</summary>
        public struct KeyGeometry
        {
            public DeadliftBarLegGeometry.Gaps Gaps;
            public Vector3 BarCenter, LeftAnchor, RightAnchor;
            public float HipZ, ShoulderZ, KneeY, KneeZ, AnkleZ, ShinLeanDeg, ThighLeanDeg, RollLeftDeg, RollRightDeg;
            /// <summary>Static sagittal moment demand (fraction of capacity) at the lumbar and thoracic joints, bar hanging from both hands.</summary>
            public float AbdomenStatic, ThoraxStatic;
        }

        public KeyGeometry GeometryOf(Key key)
        {
            Dictionary<string, Pose> poses = _kin.Solve(_legacy.PelvisPoseOf(key.X), LogicalOf(key.X));
            Vector3 bar = new Vector3(_barCenter0.x, _barCenter0.y + key.BarRiseM, key.BarZ);
            Vector3 hip = 0.5f * (_kin.JointAnchorWorld(poses, "left_thigh") + _kin.JointAnchorWorld(poses, "right_thigh"));
            Vector3 sh = 0.5f * (_kin.JointAnchorWorld(poses, "left_upper_arm") + _kin.JointAnchorWorld(poses, "right_upper_arm"));
            Vector3 knee = 0.5f * (_kin.JointAnchorWorld(poses, "left_shank") + _kin.JointAnchorWorld(poses, "right_shank"));
            Vector3 ankle = 0.5f * (_kin.JointAnchorWorld(poses, "left_foot") + _kin.JointAnchorWorld(poses, "right_foot"));
            Vector3 shin = knee - ankle, thigh = hip - knee;
            return new KeyGeometry
            {
                Gaps = DeadliftBarLegGeometry.Planned(_legacy, poses, bar), BarCenter = bar,
                LeftAnchor = AnchorOf(poses, true), RightAnchor = AnchorOf(poses, false),
                HipZ = hip.z, ShoulderZ = sh.z, KneeY = knee.y, KneeZ = knee.z, AnkleZ = ankle.z,
                ShinLeanDeg = Mathf.Atan2(shin.z, shin.y) * Mathf.Rad2Deg, ThighLeanDeg = Mathf.Atan2(thigh.z, thigh.y) * Mathf.Rad2Deg,
                RollLeftDeg = key.X.Length >= _baseCount + 2 ? (float)(key.X[_baseCount] * Mathf.Rad2Deg) : 0f,
                RollRightDeg = key.X.Length >= _baseCount + 2 ? (float)(key.X[_baseCount + 1] * Mathf.Rad2Deg) : 0f,
                AbdomenStatic = TrunkStaticDemand(poses, "abdomen"), ThoraxStatic = TrunkStaticDemand(poses, "thorax")
            };
        }

        private static readonly string[] UpperBody = { "abdomen", "thorax", "head_neck", "left_upper_arm", "right_upper_arm", "left_forearm", "right_forearm", "left_hand", "right_hand" };

        /// <summary>
        /// Static sagittal moment about a trunk joint of the segments it carries and the bar
        /// hanging from both hands (W_b/2 each), as a fraction of the joint's capacity.
        /// </summary>
        public float TrunkStaticDemand(Dictionary<string, Pose> poses, string joint)
        {
            float g = -Physics.gravity.y;
            Vector3 p = _kin.JointAnchorWorld(poses, joint);
            float m = 0f;
            int start = Array.IndexOf(UpperBody, joint);
            for (int i = start; i < UpperBody.Length; i++)
                m += _mass[UpperBody[i]] * g * (ComOf(UpperBody[i], poses[UpperBody[i]]).z - p.z);
            float halfBarN = 0.5f * _barMass * g;
            m += halfBarN * (AnchorOf(poses, true).z - p.z + AnchorOf(poses, false).z - p.z);
            PoweredJointController.PoweredJointRuntime j = _rig.PoweredController.GetJoint(joint);
            float cap = j.Profile.HasValue ? j.Profile.Value.BaseCapacityNm : float.PositiveInfinity;
            return Mathf.Abs(m) / cap;
        }

        /// <summary>Per-key planned bar-shin and bar-thigh surface gaps with the body landmarks (world Z), for the evidence table.</summary>
        public string GeometryReport()
        {
            var b = new StringBuilder();
            b.AppendLine("planned bar-leg geometry (surface gaps from the rig's leg capsules and the 14.5 mm shaft radius; world metres, +Z = athlete forward)");
            b.AppendLine("  rise    barZ    hipZ   shldrZ  anchorZ  kneeY   kneeZ  ankleZ  shinLean thighLean  torso  shinGap  thighGap (mm)  roll L/R (deg)  static abd/thx");
            foreach (Key k in Keys)
            {
                KeyGeometry g = GeometryOf(k);
                b.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,6:F3} {1,7:F4} {2,7:F4} {3,7:F4} {4,7:F4} {5,6:F3} {6,7:F4} {7,7:F4} {8,7:F1} {9,8:F1} {10,7:F1} {11,8:F1} {12,8:F1}  {14,5:F1}/{15,5:F1}  {16:F2}/{17:F2}{13}",
                    k.BarRiseM, g.BarCenter.z, g.HipZ, g.ShoulderZ, 0.5f * (g.LeftAnchor.z + g.RightAnchor.z), g.KneeY, g.KneeZ, g.AnkleZ,
                    g.ShinLeanDeg, g.ThighLeanDeg, k.TorsoDeg, g.Gaps.Shin * 1000f, g.Gaps.Thigh * 1000f, k.Lockout ? "  lockout" : string.Empty, g.RollLeftDeg, g.RollRightDeg, g.AbdomenStatic, g.ThoraxStatic));
            }
            return b.ToString();
        }

        public string Describe()
        {
            var b = new StringBuilder();
            b.AppendLine(string.Format(CultureInfo.InvariantCulture, "lift path: {0} keys, lockout rise {1:F3} m, knee passage rise {4:F3} m, midfoot z {2:F4} m, setup system COM z {3:F4} m, joint path length {5:F1} deg",
                Keys.Count, LockoutRiseM, _midfootZ, _setupComZ, _kneePassageRise, JointPathLengthDeg));
            b.AppendLine("rise m   hard cost   cost      sysCOM z  bar z    torso deg  knee deg  hip deg  bar-limb clr mm  lockout  s     step: largest joint (deg)  abdomen  thorax");
            float total = JointPathLengthDeg;
            for (int i = 0; i < Keys.Count; i++)
            {
                Key k = Keys[i];
                string big = "-"; float bigDeg = 0f, abd = 0f, thx = 0f;
                if (i > 0)
                    foreach (KeyValuePair<string, Quaternion> q in k.Logical)
                    {
                        float d = Quaternion.Angle(Keys[i - 1].Logical[q.Key], q.Value);
                        if (d > bigDeg) { bigDeg = d; big = q.Key; }
                        if (q.Key == "abdomen") abd = d;
                        if (q.Key == "thorax") thx = d;
                    }
                b.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,6:F3}  {1,9:E2}  {2,9:E2}  {3,8:F4}  {4,7:F4}  {5,8:F1}  {6,8:F1}  {7,7:F1}  {8,10:F1}  {9,-6} {10,5:F3} {11,-14} {12,5:F2} {13,7:F2} {14,7:F2}",
                    k.BarRiseM, k.HardCost, k.Cost, k.SystemComZ, k.BarZ, k.TorsoDeg, k.KneeDeg, k.HipDeg, k.BarShinClearanceM * 1000f, k.Lockout,
                    total > 0f ? _cumulativeDeg[i] / total : 0f, big, bigDeg, abd, thx));
            }
            b.AppendLine("largest hard terms (keys with hard cost > 1e-3):");
            for (int i = 0; i < Keys.Count; i++)
                if (Keys[i].HardCost > 1e-3 && (i % 5 == 0 || Keys[i].Lockout))
                    b.AppendLine(string.Format(CultureInfo.InvariantCulture, "  rise {0:F3}: {1}", Keys[i].BarRiseM, HardTerms(Keys[i])));
            return b.ToString();
        }
    }
}
