using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>Inputs of the deadlift setup solve. Every length is a world metre; +Y up, the athlete faces +Z.</summary>
    public sealed class DeadliftSetupSpec
    {
        public float BarAxisY = 0.223075f;          // resting height of the bar axis on its 24-facet plates, facet flat (GAM-70 Gate A1)
        public float BarZ = 0.02f;                  // bar axis, relative to the sole centre line
        public float BarRadius = 0.0145f;
        public float StanceHalfWidth = 0.15f;       // sole centre from the midline
        public float ToeOutDeg = 12f;
        public float GripHalfWidth = 0.30f;         // palm anchor from the midline
        public float TorsoAboveHorizontalDeg = 42f; // hip centre to shoulder centre line, a preference only
        public float ShoulderForwardOfBarM = 0.0f;  // shoulder joint minus bar, a preference only
        public float LimitMarginDeg = 4f;
        public float ShankBarClearanceM = 0.004f;
        public float LimbClearanceM = 0.012f;

        // Constraint ladder. Hard constraints (soles flat, palm anchors on the shaft, MCP line along the shaft,
        // every joint inside its limits) are always on; the rest are preferences that can be switched off to find
        // out which one breaks feasibility.
        public bool UseBalance = true;
        public bool UseTorsoAngle = true;
        public bool UseNeutralSpine = true;
        public float NeutralSpineTotalFlexionDeg = 25f;   // abdomen + thorax flexion allowed before the spine is "rounded"
        public bool UseArmPriors = true;                  // straight elbows, neutral wrists, palm normal toward the athlete
        public bool UseClearances = true;
        public bool RequireKnuckleLineAlongShaft = true;   // false = reach the shaft with the palm anchor only
        public int RandomStarts = 12;
        public string Label = "full";
        public float BarMassKgInBalance = 0f;              // bar mass counted in the COM balance (a bar carried in the hands)
        public bool IncludeStandingSeed = true;
        public List<double[]> WarmStarts = new List<double[]>();   // solutions of earlier rungs, tried as extra seeds
        public float MaxKneeFlexionDeg = 999f;             // standing carry: cap leg flexion
        public float MaxHipFlexionDeg = 999f;
    }

    public sealed class DeadliftPlantModel
    {
        public DeadliftSetupSpec Spec;
        public Pose PelvisWorld;
        public readonly Dictionary<string, Quaternion> SetupLogical = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        public Dictionary<string, Pose> Poses;
        public Vector3 LeftGripAnchorLocal, RightGripAnchorLocal;
        public Vector3 LeftGripWorld, RightGripWorld;
        public Vector3 BarCenterWorld;
        public double SolverCost;
        public double[] Solution;
        public double MaxFootResidualM, MaxHandResidualM;
        public float PalmRollDeg;
        public Vector3 ComWorld;
        public float TorsoAboveHorizontalDeg;
        public float HipFlexionDeg;
        public readonly List<string> Notes = new List<string>();
        public readonly List<JointMargin> Margins = new List<JointMargin>();

        public string Describe()
        {
            var b = new StringBuilder();
            b.AppendLine($"cost {SolverCost:E3}; foot residual {MaxFootResidualM * 1000:F3} mm; hand residual {MaxHandResidualM * 1000:F3} mm; palm roll {PalmRollDeg:F1} deg");
            b.AppendLine($"pelvis {PelvisWorld.position.ToString("F4")} rot {PelvisWorld.rotation.eulerAngles.ToString("F2")}; torso {TorsoAboveHorizontalDeg:F1} deg above horizontal; COM {ComWorld.ToString("F4")}");
            foreach (JointMargin m in Margins)
                b.AppendLine(m.ToString());
            foreach (string n in Notes)
                b.AppendLine(n);
            return b.ToString();
        }
    }

    public readonly struct JointMargin
    {
        public JointMargin(string joint, string family, float twistDeg, float lowDeg, float highDeg, float swingConeDeg, float swingLimitDeg)
        {
            Joint = joint; Family = family; TwistDeg = twistDeg; LowDeg = lowDeg; HighDeg = highDeg;
            SwingConeDeg = swingConeDeg; SwingLimitDeg = swingLimitDeg;
        }

        public string Joint { get; }
        public string Family { get; }
        public float TwistDeg { get; }
        public float LowDeg { get; }
        public float HighDeg { get; }
        public float SwingConeDeg { get; }
        public float SwingLimitDeg { get; }
        public float TwistMarginDeg => Mathf.Min(TwistDeg - LowDeg, HighDeg - TwistDeg);
        public float SwingMarginDeg => SwingLimitDeg - SwingConeDeg;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "{0,-16} {1,-8} twist {2,7:F1} in [{3:F0},{4:F0}] margin {5,6:F1} | swing cone {6,5:F1} / {7:F0} margin {8,6:F1}",
            Joint, Family, TwistDeg, LowDeg, HighDeg, TwistMarginDeg, SwingConeDeg, SwingLimitDeg, SwingMarginDeg);
    }

    /// <summary>
    /// Kinematic setup of the canonical athlete for the conventional
    /// deadlift start: soles flat on the platform, hips hinged, knees flexed,
    /// back inclined, arms hanging to the bar, both palm anchors on the shaft.
    /// The solve runs over the athlete's actual joints (the same anchors,
    /// neutral rotations, joint frames and limits the powered writer uses),
    /// so the pose is exactly what the drives would hold with zero tracking
    /// error. It writes no body, no force and no drive.
    ///
    /// Limits are respected with a margin; if the plant cannot reach, the
    /// residuals and the nearest limit are reported, and nothing is relaxed.
    /// </summary>
    public sealed class DeadliftSetupSolver
    {
        private const int PelvisY = 0, PelvisZ = 1, PelvisPitch = 2, Abdomen = 3, Thorax = 4, Neck = 5;
        private const int LeftLeg = 6, RightLeg = 11, LeftArm = 16, RightArm = 23, Roll = 30, ParameterCount = 31;

        private readonly PhysicalAthleteRig _rig;
        private readonly BenchKinematics _kin;
        private readonly Quaternion _pelvisStanding;
        private readonly float _leftSign;
        private readonly Vector3 _leftAnchorLocal, _rightAnchorLocal, _leftNormalLocal, _rightNormalLocal, _leftIndexLocal, _rightIndexLocal;
        private readonly Dictionary<string, float> _mass = new Dictionary<string, float>(StringComparer.Ordinal);
        private float _totalMass;

        public DeadliftSetupSolver(PhysicalAthleteRig rig)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _kin = new BenchKinematics(rig);
            _pelvisStanding = rig.Segments["pelvis"].Body.rotation;
            Vector3 forward = _pelvisStanding * Vector3.forward;
            forward.y = 0f;
            if (Vector3.Dot(forward.normalized, Vector3.forward) < 0.9f)
                throw new InvalidOperationException("The deadlift setup assumes the canonical athlete faces +Z; it faces " + forward);
            _leftSign = Mathf.Sign(rig.Segments["left_foot"].Body.position.x - rig.Segments["right_foot"].Body.position.x);
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> s in rig.Segments)
            {
                _mass[s.Key] = s.Value.Body.mass;
                _totalMass += s.Value.Body.mass;
            }
            MeasureHandFrame(true, out _leftAnchorLocal, out _leftNormalLocal, out _leftIndexLocal);
            MeasureHandFrame(false, out _rightAnchorLocal, out _rightNormalLocal, out _rightIndexLocal);
        }

        public BenchKinematics Kinematics => _kin;

        /// <summary>Lateral sign (+1/-1) of the athlete's left side in world X.</summary>
        public float LeftSign => _leftSign;

        public static float AnchorBarRadius { get; } = 0.0145f;

        private void MeasureHandFrame(bool left, out Vector3 anchor, out Vector3 normal, out Vector3 indexLocal)
        {
            Animator reference = _rig.ReferenceAnimator;
            Transform bone = reference.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Transform index = reference.GetBoneTransform(left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal);
            Transform body = _rig.Segments[left ? "left_hand" : "right_hand"].Body.transform;
            Vector3 palmCenterBone = left ? SquatPalmSurface.LeftPalmCenterLocal : SquatPalmSurface.RightPalmCenterLocal;
            Vector3 palmNormalBone = left ? SquatPalmSurface.LeftPalmNormalLocal : SquatPalmSurface.RightPalmNormalLocal;
            Vector3 palmCenter = body.InverseTransformPoint(bone.TransformPoint(palmCenterBone));
            normal = body.InverseTransformDirection(bone.TransformDirection(palmNormalBone)).normalized;
            anchor = palmCenter + normal * AnchorBarRadius;
            indexLocal = body.InverseTransformPoint(index.position);
        }

        private Dictionary<string, Quaternion> Logical(double[] x, out Quaternion[] arms)
        {
            var q = new Dictionary<string, Quaternion>(StringComparer.Ordinal)
            {
                ["abdomen"] = Quaternion.AngleAxis((float)(x[Abdomen] * Mathf.Rad2Deg), Vector3.right),
                ["thorax"] = Quaternion.AngleAxis((float)(x[Thorax] * Mathf.Rad2Deg), Vector3.right),
                ["head_neck"] = Quaternion.AngleAxis((float)(x[Neck] * Mathf.Rad2Deg), Vector3.right)
            };
            LegLogical(q, "left", x, LeftLeg);
            LegLogical(q, "right", x, RightLeg);
            ArmLogical(q, "left", x, LeftArm);
            ArmLogical(q, "right", x, RightArm);
            arms = null;
            return q;
        }

        private void LegLogical(Dictionary<string, Quaternion> q, string side, double[] x, int o)
        {
            q[side + "_thigh"] = BenchKinematics.FromParameters(_kin.Joint(side + "_thigh"), x, o);
            q[side + "_shank"] = BenchKinematics.FromParameters(_kin.Joint(side + "_shank"), x, o + 3);
            q[side + "_foot"] = BenchKinematics.FromParameters(_kin.Joint(side + "_foot"), x, o + 4);
        }

        private void ArmLogical(Dictionary<string, Quaternion> q, string side, double[] x, int o)
        {
            q[side + "_upper_arm"] = BenchKinematics.FromParameters(_kin.Joint(side + "_upper_arm"), x, o);
            q[side + "_forearm"] = BenchKinematics.FromParameters(_kin.Joint(side + "_forearm"), x, o + 3);
            q[side + "_hand"] = BenchKinematics.FromParameters(_kin.Joint(side + "_hand"), x, o + 4);
        }

        private Pose PelvisPose(double[] x) => new Pose(
            new Vector3(0f, (float)x[PelvisY], (float)x[PelvisZ]),
            Quaternion.AngleAxis((float)(x[PelvisPitch] * Mathf.Rad2Deg), Vector3.right) * _pelvisStanding);

        public DeadliftPlantModel Solve(DeadliftSetupSpec spec)
        {
            var model = new DeadliftPlantModel { Spec = spec };
            double bestCost = double.MaxValue;
            double[] best = null;

            // Seeds differ in the (unknown a priori) sign of hip and ankle flexion in the logical frames.
            foreach (double hipSign in new[] { -1d, 1d })
            {
                foreach (double ankleSign in new[] { -1d, 1d })
                {
                    foreach (double shoulderSign in new[] { -1d, 1d })
                    {
                        double[] seed = new double[ParameterCount];
                        seed[PelvisY] = 0.62; seed[PelvisZ] = -0.12; seed[PelvisPitch] = 0.8;
                        foreach (int o in new[] { LeftLeg, RightLeg })
                        {
                            seed[o] = hipSign * 1.1;
                            seed[o + 3] = 1.5;
                            seed[o + 4] = ankleSign * 0.4;
                        }
                        foreach (int o in new[] { LeftArm, RightArm })
                        {
                            seed[o] = shoulderSign * 1.0;
                            seed[o + 3] = 0.05;
                        }
                        double[] solved = BenchKinematics.LevenbergMarquardt(seed, x => Residuals(x, spec, model, false), 160, out double cost);
                        if (cost < bestCost)
                        {
                            bestCost = cost;
                            best = solved;
                        }
                    }
                }
            }

            if (spec.IncludeStandingSeed)
            {
                double[] standing = new double[ParameterCount];
                standing[PelvisY] = 1.0; standing[PelvisZ] = -0.04; standing[PelvisPitch] = 0.05;
                foreach (int o in new[] { LeftLeg, RightLeg }) { standing[o + 3] = 0.05; }
                foreach (int o in new[] { LeftArm, RightArm }) { standing[o + 3] = 0.05; }
                double[] solvedStanding = BenchKinematics.LevenbergMarquardt(standing, x => Residuals(x, spec, model, false), 300, out double standingCost);
                if (standingCost < bestCost)
                {
                    bestCost = standingCost;
                    best = solvedStanding;
                }
            }

            foreach (double[] warm in spec.WarmStarts)
            {
                double[] solvedWarm = BenchKinematics.LevenbergMarquardt(warm, x => Residuals(x, spec, model, false), 300, out double warmCost);
                if (warmCost < bestCost)
                {
                    bestCost = warmCost;
                    best = solvedWarm;
                }
            }

            var rng = new System.Random(70);
            for (int start = 0; start < spec.RandomStarts; start++)
            {
                double[] seed = (double[])best.Clone();
                for (int i = 0; i < seed.Length; i++)
                    seed[i] += (rng.NextDouble() * 2d - 1d) * (i == PelvisY ? 0.15 : i == PelvisZ ? 0.15 : 0.6);
                double[] solved = BenchKinematics.LevenbergMarquardt(seed, x => Residuals(x, spec, model, false), 200, out double cost);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = solved;
                }
            }
            best = BenchKinematics.LevenbergMarquardt(best, x => Residuals(x, spec, model, false), 400, out bestCost);
            model.SolverCost = bestCost;
            model.Solution = (double[])best.Clone();
            Finish(model, best, spec);
            return model;
        }

        private List<double> Residuals(double[] x, DeadliftSetupSpec spec, DeadliftPlantModel model, bool report)
        {
            Pose pelvis = PelvisPose(x);
            Dictionary<string, Quaternion> logical = Logical(x, out _);
            Dictionary<string, Pose> poses = _kin.Solve(pelvis, logical);
            var r = new List<double>(120);

            // Feet: soles flat on the platform, stance and toe-out as specified.
            foreach (string side in new[] { "left", "right" })
            {
                float lateral = (side == "left" ? _leftSign : -_leftSign);
                BoxCollider footBox = (BoxCollider)_rig.Segments[side + "_foot"].Collider;
                Vector3 soleLocal = footBox.center - new Vector3(0f, footBox.size.y * 0.5f, 0f);
                Pose foot = poses[side + "_foot"];
                Vector3 sole = foot.position + foot.rotation * soleLocal;
                Vector3 up = foot.rotation * Vector3.up;
                Vector3 fwd = foot.rotation * Vector3.forward;
                float toeX = lateral * Mathf.Sin(spec.ToeOutDeg * Mathf.Deg2Rad);
                r.Add(80d * sole.y);
                r.Add(30d * (sole.x - lateral * spec.StanceHalfWidth));
                r.Add(30d * sole.z);
                r.Add(15d * up.x);
                r.Add(15d * up.z);
                r.Add(6d * (fwd.x - toeX));
            }

            // Hard: the thorax proxy and a thigh proxy may not interpenetrate (the qualified policy ignores only thigh-abdomen).
            foreach (string side in new[] { "left", "right" })
            {
                CapsuleSegment(poses, side + "_thigh", out Vector3 t0, out Vector3 t1, out float tr);
                BoxCollider thoraxBox = (BoxCollider)_rig.Segments["thorax"].Collider;
                Pose thorax = poses["thorax"];
                for (int k = 0; k <= 6; k++)
                {
                    Vector3 pt = Vector3.Lerp(t0, t1, k / 6f);
                    Vector3 local = Quaternion.Inverse(thorax.rotation) * (pt - thorax.position) - thoraxBox.center;
                    Vector3 outside = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - thoraxBox.size.x * 0.5f), Mathf.Max(0f, Mathf.Abs(local.y) - thoraxBox.size.y * 0.5f), Mathf.Max(0f, Mathf.Abs(local.z) - thoraxBox.size.z * 0.5f));
                    float inside = Mathf.Min(thoraxBox.size.x * 0.5f - Mathf.Abs(local.x), Mathf.Min(thoraxBox.size.y * 0.5f - Mathf.Abs(local.y), thoraxBox.size.z * 0.5f - Mathf.Abs(local.z)));
                    float gap = (inside > 0f ? -inside : outside.magnitude) - tr;
                    r.Add(60d * Mathf.Max(0f, 0.002f - gap));
                }
            }

            // Hard: nothing but the soles goes below the platform.
            foreach (string id in _kin.SegmentOrder)
            {
                if (id.EndsWith("_foot", StringComparison.Ordinal))
                    continue;
                r.Add(80d * Mathf.Max(0f, -LowestPoint(poses, id)));
            }

            // Hands: palm anchor on the shaft, palm normal and MCP line.
            float roll = (float)x[Roll];
            Vector3 normalTarget = Quaternion.AngleAxis(roll * Mathf.Rad2Deg, Vector3.right) * Vector3.back;
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "left" : "right";
                float lateral = left ? _leftSign : -_leftSign;
                Pose hand = poses[side + "_hand"];
                Vector3 anchorLocal = left ? _leftAnchorLocal : _rightAnchorLocal;
                Vector3 normalLocal = left ? _leftNormalLocal : _rightNormalLocal;
                Vector3 indexLocal = left ? _leftIndexLocal : _rightIndexLocal;
                Vector3 grip = hand.position + hand.rotation * anchorLocal;
                Vector3 n = hand.rotation * normalLocal;
                Vector3 mcp = hand.rotation * (indexLocal - anchorLocal);
                mcp -= Vector3.Project(mcp, n);
                mcp.Normalize();
                // Index finger medial (thumb-around overhand grip).
                Vector3 mcpTarget = new Vector3(-lateral, 0f, 0f);
                r.Add(80d * (grip.x - lateral * spec.GripHalfWidth));
                r.Add(80d * (grip.y - spec.BarAxisY));
                r.Add(80d * (grip.z - spec.BarZ));
                if (spec.UseArmPriors)
                {
                    r.Add(6d * (n.x - normalTarget.x)); r.Add(6d * (n.y - normalTarget.y)); r.Add(6d * (n.z - normalTarget.z));
                }
                else if (spec.RequireKnuckleLineAlongShaft)
                {
                    // Hard grip only: the shaft must lie along the MCP line, whichever way the palm faces.
                    r.Add(20d * Vector3.Dot(n, Vector3.right));
                }
                if (spec.RequireKnuckleLineAlongShaft || spec.UseArmPriors)
                {
                    r.Add(6d * (mcp.x - mcpTarget.x)); r.Add(6d * (mcp.y - mcpTarget.y)); r.Add(6d * (mcp.z - mcpTarget.z));
                }

                // Reach: the shoulder joint must be within the straight-arm reach of the grip.
                Vector3 shoulder = _kin.JointAnchorWorld(poses, side + "_upper_arm");
                if (spec.UseArmPriors)
                    r.Add(3d * (shoulder.z - spec.BarZ - spec.ShoulderForwardOfBarM));
            }
            if (spec.UseArmPriors)
                r.Add(0.5d * x[Roll]);

            // Balance: whole-body COM over the sole centre line (bar on the ground carries its own weight).
            Vector3 com = Vector3.zero;
            foreach (KeyValuePair<string, Pose> p in poses)
                com += p.Value.position * _mass[p.Key];
            com /= _totalMass;
            if (spec.UseBalance)
            {
                Vector3 barPoint = new Vector3(0f, spec.BarAxisY, spec.BarZ);
                Vector3 combined = (com * _totalMass + barPoint * spec.BarMassKgInBalance) / (_totalMass + spec.BarMassKgInBalance);
                r.Add(12d * combined.z);
            }

            // Back angle preference and neutral spine.
            Vector3 hipMid = 0.5f * (_kin.JointAnchorWorld(poses, "left_thigh") + _kin.JointAnchorWorld(poses, "right_thigh"));
            Vector3 shoulderMid = 0.5f * (_kin.JointAnchorWorld(poses, "left_upper_arm") + _kin.JointAnchorWorld(poses, "right_upper_arm"));
            Vector3 torso = shoulderMid - hipMid;
            float torsoAngle = Mathf.Atan2(torso.y, torso.z) * Mathf.Rad2Deg;
            // Hard: the trunk is a trunk - shoulders above and ahead of the hips, within 15..90 deg of horizontal.
            r.Add(20d * (Math.Max(0d, 15d - torsoAngle) + Math.Max(0d, torsoAngle - 100d)) * Mathf.Deg2Rad);
            if (spec.UseTorsoAngle)
                r.Add(2d * (torsoAngle - spec.TorsoAboveHorizontalDeg) * Mathf.Deg2Rad);
            if (spec.UseNeutralSpine)
            {
                double spineFlexion = (Math.Abs(x[Abdomen]) + Math.Abs(x[Thorax])) * Mathf.Rad2Deg;
                r.Add(30d * Math.Max(0d, spineFlexion - spec.NeutralSpineTotalFlexionDeg) * Mathf.Deg2Rad);
                r.Add(0.5d * x[Abdomen]); r.Add(0.5d * x[Thorax]);
            }
            r.Add(1.0d * x[Neck]);

            // Arms: straight (no elbow flexion), neutral wrists.
            if (spec.UseArmPriors)
            {
                foreach (int o in new[] { LeftArm, RightArm })
                {
                    r.Add(6d * x[o + 3]);
                    r.Add(1.5d * x[o + 4]); r.Add(1.5d * x[o + 5]); r.Add(1.5d * x[o + 6]);
                }
            }

            if (spec.MaxKneeFlexionDeg < 900f)
            {
                double knee = 0.5d * (x[LeftLeg + 3] + x[RightLeg + 3]) * Mathf.Rad2Deg;
                double hip = -0.5d * (x[LeftLeg] + x[RightLeg]) * Mathf.Rad2Deg;
                r.Add(30d * Math.Max(0d, knee - spec.MaxKneeFlexionDeg) * Mathf.Deg2Rad);
                r.Add(30d * Math.Max(0d, hip - spec.MaxHipFlexionDeg) * Mathf.Deg2Rad);
            }

            // Joint limits with margin, every joint.
            foreach (KeyValuePair<string, Quaternion> q in logical)
                BenchKinematics.LimitResiduals(_kin.Joint(q.Key), q.Value, spec.LimitMarginDeg, r, 60d);

            // Collision ownership as hinge penalties: bar vs shin/thigh, arms vs legs.
            if (spec.UseClearances)
                AddClearancePenalties(poses, spec, r);

            // Weak regularization toward the seed region keeps the solve well posed.
            for (int i = 0; i < x.Length; i++)
                r.Add(0.01d * x[i]);
            return r;
        }

        private void AddClearancePenalties(Dictionary<string, Pose> poses, DeadliftSetupSpec spec, List<double> r)
        {
            foreach (string side in new[] { "left", "right" })
            {
                foreach (string leg in new[] { "_shank", "_thigh" })
                {
                    CapsuleSegment(poses, side + leg, out Vector3 a, out Vector3 b, out float radius);
                    // The bar is a line along X: compare in the YZ plane.
                    float d = DistancePointSegment2D(new Vector2(spec.BarAxisY, spec.BarZ), new Vector2(a.y, a.z), new Vector2(b.y, b.z));
                    float gap = d - radius - spec.BarRadius;
                    r.Add(60d * Mathf.Max(0f, spec.ShankBarClearanceM - gap));
                }
            }

            string[] arm = { "_upper_arm", "_forearm" };
            string[] leg2 = { "_thigh", "_shank" };
            foreach (string aSide in new[] { "left", "right" })
            {
                foreach (string aSeg in arm)
                {
                    CapsuleSegment(poses, aSide + aSeg, out Vector3 a0, out Vector3 a1, out float ar);
                    foreach (string lSide in new[] { "left", "right" })
                    {
                        foreach (string lSeg in leg2)
                        {
                            CapsuleSegment(poses, lSide + lSeg, out Vector3 l0, out Vector3 l1, out float lr);
                            float gap = DistanceSegmentSegment(a0, a1, l0, l1) - ar - lr;
                            r.Add(40d * Mathf.Max(0f, spec.LimbClearanceM - gap));
                        }
                    }
                }
                // Hand (a sphere at the hand body centre, radius = half its longest box side) vs the legs.
                Vector3 hand = poses[aSide + "_hand"].position;
                foreach (string lSide in new[] { "left", "right" })
                {
                    foreach (string lSeg in leg2)
                    {
                        CapsuleSegment(poses, lSide + lSeg, out Vector3 l0, out Vector3 l1, out float lr);
                        float gap = DistancePointSegment(hand, l0, l1) - lr - 0.07f;
                        r.Add(40d * Mathf.Max(0f, spec.LimbClearanceM - gap));
                    }
                }
            }
        }

        /// <summary>Lowest world Y of a segment's actual collider (capsule or box) at a pose.</summary>
        public float LowestPoint(Dictionary<string, Pose> poses, string id)
        {
            Collider c = _rig.Segments[id].Collider;
            Pose p = poses[id];
            if (c is CapsuleCollider)
            {
                CapsuleSegment(poses, id, out Vector3 a, out Vector3 b, out float radius);
                return Mathf.Min(a.y, b.y) - radius;
            }
            BoxCollider box = (BoxCollider)c;
            float lowest = float.MaxValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = box.center + Vector3.Scale(box.size * 0.5f, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
                lowest = Mathf.Min(lowest, (p.position + p.rotation * corner).y);
            }
            return lowest;
        }

        /// <summary>Angle (deg) between a hand's knuckle (MCP) line and the shaft axis (world X) at the model's pose.</summary>
        public float KnuckleLineAngleDeg(DeadliftPlantModel model, bool left)
        {
            Pose hand = model.Poses[left ? "left_hand" : "right_hand"];
            Vector3 anchorLocal = left ? _leftAnchorLocal : _rightAnchorLocal;
            Vector3 normalLocal = left ? _leftNormalLocal : _rightNormalLocal;
            Vector3 indexLocal = left ? _leftIndexLocal : _rightIndexLocal;
            Vector3 mcp = hand.rotation * (indexLocal - anchorLocal);
            mcp -= Vector3.Project(mcp, hand.rotation * normalLocal);
            return Mathf.Min(Vector3.Angle(mcp, Vector3.right), Vector3.Angle(mcp, Vector3.left));
        }

        public void CapsuleSegment(Dictionary<string, Pose> poses, string id, out Vector3 a, out Vector3 b, out float radius)
        {
            CapsuleCollider c = (CapsuleCollider)_rig.Segments[id].Collider;
            Pose p = poses[id];
            float half = Mathf.Max(0f, c.height * 0.5f - c.radius);
            Vector3 axis = p.rotation * Vector3.up;
            Vector3 center = p.position + p.rotation * c.center;
            a = center - axis * half;
            b = center + axis * half;
            radius = c.radius;
        }

        public static float DistancePointSegment(Vector3 p, Vector3 a, Vector3 b)
        {
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector3.Distance(p, a + ab * t);
        }

        public static float DistancePointSegment2D(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = ab.sqrMagnitude < 1e-12f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        public static float DistanceSegmentSegment(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
        {
            Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
            float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
            float s, t;
            if (a < 1e-12f && e < 1e-12f) return Vector3.Distance(p1, p2);
            if (a < 1e-12f) { s = 0f; t = Mathf.Clamp01(f / e); }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e < 1e-12f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                else
                {
                    float b = Vector3.Dot(d1, d2), denom = a * e - b * b;
                    s = denom > 1e-12f ? Mathf.Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Mathf.Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Mathf.Clamp01((b - c) / a); }
                }
            }
            return Vector3.Distance(p1 + d1 * s, p2 + d2 * t);
        }

        private void Finish(DeadliftPlantModel model, double[] x, DeadliftSetupSpec spec)
        {
            model.PelvisWorld = PelvisPose(x);
            Dictionary<string, Quaternion> logical = Logical(x, out _);
            foreach (KeyValuePair<string, Quaternion> q in logical)
                model.SetupLogical[q.Key] = q.Value;
            model.Poses = _kin.Solve(model.PelvisWorld, model.SetupLogical);
            model.PalmRollDeg = (float)(x[Roll] * Mathf.Rad2Deg);
            model.LeftGripAnchorLocal = _leftAnchorLocal;
            model.RightGripAnchorLocal = _rightAnchorLocal;
            model.BarCenterWorld = new Vector3(0f, spec.BarAxisY, spec.BarZ);

            double footMax = 0d;
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider footBox = (BoxCollider)_rig.Segments[side + "_foot"].Collider;
                Pose foot = model.Poses[side + "_foot"];
                Vector3 sole = foot.position + foot.rotation * (footBox.center - new Vector3(0f, footBox.size.y * 0.5f, 0f));
                float lateral = side == "left" ? _leftSign : -_leftSign;
                footMax = Math.Max(footMax, Math.Abs(sole.y));
                footMax = Math.Max(footMax, Math.Abs(sole.x - lateral * spec.StanceHalfWidth));
                footMax = Math.Max(footMax, Math.Abs(sole.z));
            }
            model.MaxFootResidualM = footMax;

            double handMax = 0d;
            foreach (bool left in new[] { true, false })
            {
                Pose hand = model.Poses[left ? "left_hand" : "right_hand"];
                Vector3 grip = hand.position + hand.rotation * (left ? _leftAnchorLocal : _rightAnchorLocal);
                if (left) model.LeftGripWorld = grip; else model.RightGripWorld = grip;
                float lateral = left ? _leftSign : -_leftSign;
                Vector3 target = new Vector3(lateral * spec.GripHalfWidth, spec.BarAxisY, spec.BarZ);
                handMax = Math.Max(handMax, Vector3.Distance(grip, target));
            }
            model.MaxHandResidualM = handMax;

            Vector3 com = Vector3.zero;
            foreach (KeyValuePair<string, Pose> p in model.Poses)
                com += p.Value.position * _mass[p.Key];
            model.ComWorld = com / _totalMass;

            Vector3 hipMid = 0.5f * (_kin.JointAnchorWorld(model.Poses, "left_thigh") + _kin.JointAnchorWorld(model.Poses, "right_thigh"));
            Vector3 shoulderMid = 0.5f * (_kin.JointAnchorWorld(model.Poses, "left_upper_arm") + _kin.JointAnchorWorld(model.Poses, "right_upper_arm"));
            Vector3 torso = shoulderMid - hipMid;
            model.TorsoAboveHorizontalDeg = Mathf.Atan2(torso.y, torso.z) * Mathf.Rad2Deg;
            model.HipFlexionDeg = 0f;

            foreach (string id in _kin.SegmentOrder)
            {
                if (!_kin.TryGetJoint(id, out BenchKinematics.JointInfo joint))
                    continue;
                Quaternion q = PoweredJointController.NormalizeCanonical(model.SetupLogical.TryGetValue(id, out Quaternion v) ? v : Quaternion.identity);
                float twist = PoweredJointController.SignedTwistRadians(q, Vector3.right) * Mathf.Rad2Deg;
                float cone = joint.Recipe.Kind == PhysicalJointKind.Hinge ? 0f : BenchKinematics.SwingConeDegrees(q);
                model.Margins.Add(new JointMargin(id, joint.Recipe.Family, twist, joint.Recipe.LowDegrees, joint.Recipe.HighDegrees, cone, joint.Recipe.SecondaryLimitDegrees));
            }
        }
    }
}
