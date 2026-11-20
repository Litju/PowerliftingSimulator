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
    /// GAM-70 Gate A2 corrected feasibility formulation (Solver B).
    ///
    /// The original A2 solver is kept intact as the negative control. This
    /// formulation differs only where the physical measurements require it:
    ///
    /// * The grip uses the real index-to-little metacarpal line, not the
    ///   palm-centre-to-index proxy (the two differ by 38 deg on this rig).
    /// * Which side of the shaft the palm is on is the declared grip style
    ///   (<see cref="DeadliftGripStyle"/>): pronated = palm toward the athlete,
    ///   index medial; supinated = palm away, index lateral. The fingers hang
    ///   around the shaft; a specific wrist twist is not a requirement.
    /// * The planted flat sole is the chosen starting reference. Stance width,
    ///   toe-out and fore-aft are imposed only when a candidate declares them.
    ///   Static support is checked in the declared contact mode
    ///   (<see cref="DeadliftSupportMode"/>) on the exact sole-corner hull.
    /// * "Bar over a foot" is a coaching preference, not a hard condition
    ///   (IPF 2026 s4.3(1) says only that the bar lies in front of the feet).
    /// * The bar may touch a shin. It may not penetrate it. The exact
    ///   capsule/cylinder distance is used, not a weighted guess.
    /// * Joint ranges are hard. The authored range may be consumed; an extra
    ///   robustness margin is requested explicitly and reported.
    ///
    /// Every hard condition is also re-derived by the independent acceptance
    /// check in the test, so a residual cannot certify itself.
    /// </summary>
    public sealed class DeadliftSetupFeasibilitySolver
    {
        private const int PelvisY = 0, PelvisZ = 1, PelvisPitch = 2, Abdomen = 3, Thorax = 4, Neck = 5;
        private const int LeftLeg = 6, RightLeg = 11, LeftArm = 16, RightArm = 23, Roll = 30, ParameterCount = 31;

        private readonly PhysicalAthleteRig _rig;
        private readonly DeadliftSetupSolver _legacy;
        private readonly BenchKinematics _kin;
        private readonly float _leftSign;
        private readonly Dictionary<string, float> _mass = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly float _totalMass;
        private readonly float _leftReach, _rightReach;

        public DeadliftSetupFeasibilitySolver(PhysicalAthleteRig rig)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _legacy = new DeadliftSetupSolver(rig);
            _kin = _legacy.Kinematics;
            _leftSign = _legacy.LeftSign;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> s in rig.Segments)
            {
                _mass[s.Key] = s.Value.Body.mass;
                _totalMass += s.Value.Body.mass;
            }
            _leftReach = _legacy.StraightArmReachM(true);
            _rightReach = _legacy.StraightArmReachM(false);
        }

        public DeadliftSetupSolver Measurements => _legacy;
        public BenchKinematics Kinematics => _kin;
        public float LeftSign => _leftSign;
        public float LeftStraightArmReachM => _leftReach;
        public float RightStraightArmReachM => _rightReach;

        public DeadliftPlantModel Solve(DeadliftSetupSpec spec)
        {
            spec.CorrectedHandFrame = true;
            var model = new DeadliftPlantModel { Spec = spec };
            double bestCost = double.MaxValue;
            double[] best = null;

            foreach (double[] seed in Seeds(spec))
            {
                double[] solved = BenchKinematics.LevenbergMarquardt(seed, x => Combined(x, spec), 320, out double cost);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = solved;
                }
            }

            // A small bounded set of jittered restarts around the best pose,
            // deterministic and fixed: not an open-ended random campaign.
            var rng = new System.Random(700);
            for (int start = 0; start < 4; start++)
            {
                double[] seed = (double[])best.Clone();
                for (int i = 0; i < seed.Length; i++)
                    seed[i] += (rng.NextDouble() * 2d - 1d) * (i == PelvisY || i == PelvisZ ? 0.08 : 0.25);
                double[] solved = BenchKinematics.LevenbergMarquardt(seed, x => Combined(x, spec), 260, out double cost);
                if (cost < bestCost)
                {
                    bestCost = cost;
                    best = solved;
                }
            }

            best = BenchKinematics.LevenbergMarquardt(best, x => Combined(x, spec), 600, out bestCost);
            model.SolverCost = bestCost;
            model.Solution = (double[])best.Clone();
            model = _legacy.BuildModel(best, spec);
            model.SolverCost = bestCost;
            model.Feasibility = EvaluateHard(best, spec);
            return model;
        }

        private IEnumerable<double[]> Seeds(DeadliftSetupSpec spec)
        {
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
                        yield return seed;
                    }
                }
            }

            // Bent-over family: the shins can only stay out of the bar if the
            // hips are high enough or far enough back, which needs a more
            // horizontal trunk than the squat-like seeds above.
            foreach (double trunkPitch in new[] { 1.0d, 1.35d })
            {
                foreach (double hipFlexion in new[] { -0.35d, -0.9d })
                {
                    foreach (double shoulderSwing in new[] { -0.4d, 0.7d })
                    {
                        double[] seed = new double[ParameterCount];
                        seed[PelvisY] = 0.56; seed[PelvisZ] = -0.22; seed[PelvisPitch] = trunkPitch;
                        seed[Abdomen] = 0.10; seed[Thorax] = 0.08;
                        foreach (int o in new[] { LeftLeg, RightLeg })
                        {
                            seed[o] = hipFlexion;
                            seed[o + 3] = 0.9;
                            seed[o + 4] = 0.15;
                        }
                        foreach (int o in new[] { LeftArm, RightArm })
                        {
                            seed[o] = shoulderSwing;
                            seed[o + 3] = 0.05;
                        }
                        yield return seed;
                    }
                }
            }

            if (spec.IncludeStandingSeed)
            {
                double[] standing = new double[ParameterCount];
                standing[PelvisY] = 1.0; standing[PelvisZ] = -0.04; standing[PelvisPitch] = 0.05;
                foreach (int o in new[] { LeftLeg, RightLeg }) { standing[o + 3] = 0.05; }
                foreach (int o in new[] { LeftArm, RightArm }) { standing[o + 3] = 0.05; }
                yield return standing;
            }

            foreach (double[] warm in spec.WarmStarts)
                yield return (double[])warm.Clone();
        }

        private List<double> Combined(double[] x, DeadliftSetupSpec spec)
        {
            List<double> hard = Hard(x, spec);
            List<double> all = new List<double>(hard.Count + 64);
            all.AddRange(hard);
            Preferences(x, spec, all);
            return all;
        }

        private List<double> Hard(double[] x, DeadliftSetupSpec spec)
        {
            Pose pelvis = _legacy.PelvisPoseOf(x);
            Dictionary<string, Quaternion> logical = _legacy.LogicalRotations(x);
            Dictionary<string, Pose> poses = _kin.Solve(pelvis, logical);
            var r = new List<double>(128);

            // --- Feet: the chosen starting reference is a planted flat foot
            // (sole on the platform, sole plane level). Stance width, toe-out
            // and fore-aft placement are candidate definitions, imposed only
            // when the candidate declares them.
            var corners = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider box = _legacy.FootBox(side);
                Pose foot = poses[side + "_foot"];
                Vector3 sole = foot.position + foot.rotation * (box.center - new Vector3(0f, box.size.y * 0.5f, 0f));
                Vector3 up = foot.rotation * Vector3.up;
                Vector3 fwd = foot.rotation * Vector3.forward;
                float lateralSide = side == "left" ? _leftSign : -_leftSign;
                r.Add(100d * sole.y);
                r.Add(60d * up.x);
                r.Add(60d * up.z);
                if (!spec.FreeStance)
                {
                    r.Add(60d * (sole.x - lateralSide * spec.StanceHalfWidth));
                    r.Add(20d * (fwd.x - lateralSide * Mathf.Sin(spec.ToeOutDeg * Mathf.Deg2Rad)));
                }
                if (!spec.FreeForeAft)
                    r.Add(60d * sole.z);

                var c = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    Vector3 local = box.center + new Vector3(
                        ((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f,
                        -box.size.y * 0.5f,
                        ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f);
                    c[i] = foot.position + foot.rotation * local;
                    r.Add(100d * Mathf.Max(0f, -c[i].y));
                }
                corners[side] = c;
            }

            // --- Whole-body COM at the setup (the grounded bar carries its own weight).
            Vector3 com = Vector3.zero;
            foreach (KeyValuePair<string, Pose> p in poses)
                com += p.Value.position * _mass[p.Key];
            com /= _totalMass;
            Vector3 barPoint = new Vector3(0f, spec.BarAxisY, spec.BarZ);

            // --- Static support in the declared contact mode, on the exact
            // convex hull of the sole corners (not an axis-aligned box).
            var hullPoints = new List<Vector2>(8);
            foreach (Vector3[] c in corners.Values)
                foreach (Vector3 p in c)
                    hullPoints.Add(new Vector2(p.x, p.z));
            List<Vector2> hull = DeadliftSupportGeometry.Hull(hullPoints);
            float weight = _totalMass * Physics.gravity.magnitude;
            float maxPull = spec.Support == DeadliftSupportMode.GrippedGroundedBar ? spec.BarMassKg * Physics.gravity.magnitude : 0f;
            float copMargin = DeadliftSupportGeometry.BestCopMargin(hull, com, weight, barPoint, maxPull, out _);
            r.Add(40d * Mathf.Max(0f, spec.SupportMarginM - copMargin));
            if (spec.RequireLiftOffSupport)
            {
                Vector2 liftOff = DeadliftSupportGeometry.CopUnderHandPull(com, weight, barPoint, spec.BarMassKg * Physics.gravity.magnitude);
                r.Add(40d * Mathf.Max(0f, spec.SupportMarginM - DeadliftSupportGeometry.SignedDistance(hull, liftOff)));
            }

            // Coaching preference "bar over the foot", reproduced only on request
            // (prior candidate): bar axis Z inside the sole Z-extent by 5 mm.
            if (spec.RequireBarOverFoot)
            {
                float minZ = float.MaxValue, maxZ = float.MinValue;
                foreach (Vector2 p in hullPoints) { minZ = Mathf.Min(minZ, p.y); maxZ = Mathf.Max(maxZ, p.y); }
                r.Add(30d * Mathf.Max(0f, (minZ + 0.005f) - spec.BarZ));
                r.Add(30d * Mathf.Max(0f, spec.BarZ - (maxZ - 0.005f)));
            }

            // --- Hands: the palm anchor is on the shaft axis (one shaft radius
            // out along the palm normal), the true MCP line lies along the
            // shaft, the palm plane is tangent to the shaft, the fingers hang
            // around it, and the palm is on the side the declared grip puts it.
            bool supinated = spec.Grip == DeadliftGripStyle.Supinated;
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "left" : "right";
                float lateral = left ? _leftSign : -_leftSign;
                Pose hand = poses[side + "_hand"];
                HandFrameLocal frame = _legacy.MeasureHand(left);
                Vector3 grip = hand.position + hand.rotation * frame.AnchorLocal;
                Vector3 n = (hand.rotation * frame.PalmNormalLocal).normalized;
                Vector3 line = hand.rotation * (frame.IndexMcpLocal - frame.LittleMcpLocal);
                line -= Vector3.Project(line, n);
                line.Normalize();
                Vector3 longAxis = (hand.rotation * Vector3.right).normalized;

                r.Add(80d * (grip.y - spec.BarAxisY));
                r.Add(80d * (grip.z - spec.BarZ));
                r.Add(80d * Mathf.Max(0f, Mathf.Abs(grip.x) - DeadliftSetupFeasibilitySolver.GrippableHalfLengthM));
                if (spec.GripWidthIsHard)
                    r.Add(60d * (grip.x - lateral * spec.GripHalfWidth));
                r.Add(60d * n.x);
                r.Add(60d * line.y);
                r.Add(60d * line.z);
                r.Add(40d * (supinated ? Mathf.Max(0f, 0.10f - n.z) : Mathf.Max(0f, n.z + 0.10f)));
                r.Add(20d * Mathf.Max(0f, longAxis.y + 0.20f));
                r.Add(5d * (1d - Vector3.Dot(line, Vector3.right) * lateral * (supinated ? 1f : -1f)));

                Vector3 shoulder = _kin.JointAnchorWorld(poses, side + "_upper_arm");
                float reach = left ? _leftReach : _rightReach;
                r.Add(60d * Mathf.Max(0f, Vector3.Distance(shoulder, grip) - reach));

                if (spec.Technique != DeadliftTechnique.Undeclared)
                {
                    // Coordination variant: conventional grips outside the knees,
                    // sumo inside. Physical arm/leg overlap is checked separately.
                    _legacy.CapsuleSegment(poses, side + "_shank", out _, out _, out float shankRadius);
                    float knee = Mathf.Abs(_kin.JointAnchorWorld(poses, side + "_shank").x) + shankRadius;
                    float outside = Mathf.Abs(grip.x) - knee;
                    r.Add(40d * (spec.Technique == DeadliftTechnique.Conventional
                        ? Mathf.Max(0f, spec.TechniqueClearanceM - outside)
                        : Mathf.Max(0f, spec.TechniqueClearanceM + outside)));
                }
            }

            // --- Candidate definition (hip-hinge setup): shoulders above hips.
            Vector3 hipMid = 0.5f * (_kin.JointAnchorWorld(poses, "left_thigh") + _kin.JointAnchorWorld(poses, "right_thigh"));
            Vector3 shoulderMid = 0.5f * (_kin.JointAnchorWorld(poses, "left_upper_arm") + _kin.JointAnchorWorld(poses, "right_upper_arm"));
            if (spec.RequireShouldersAboveHips)
                r.Add(30d * Mathf.Max(0f, 0.03f - (shoulderMid.y - hipMid.y)));

            // --- Bar vs shins and thighs: touching is legal, penetration is not.
            foreach (string side in new[] { "left", "right" })
            {
                foreach (string segment in new[] { "_shank", "_thigh" })
                {
                    _legacy.CapsuleSegment(poses, side + segment, out Vector3 a, out Vector3 b, out float radius);
                    float d = DeadliftSetupSolver.DistancePointSegment2D(new Vector2(spec.BarAxisY, spec.BarZ), new Vector2(a.y, a.z), new Vector2(b.y, b.z));
                    r.Add(60d * Mathf.Max(0f, spec.BarLimbClearanceM - (d - radius - DeadliftSetupSolver.AnchorBarRadius)));
                }
            }

            // --- Non-adjacent body collisions that the production policy does
            // not whitelist: thigh/thorax proxy and arm/leg surfaces.
            foreach (string side in new[] { "left", "right" })
            {
                _legacy.CapsuleSegment(poses, side + "_thigh", out Vector3 t0, out Vector3 t1, out float tr);
                BoxCollider thoraxBox = (BoxCollider)_rig.Segments["thorax"].Collider;
                Pose thorax = poses["thorax"];
                for (int k = 0; k <= 6; k++)
                {
                    Vector3 pt = Vector3.Lerp(t0, t1, k / 6f);
                    Vector3 local = Quaternion.Inverse(thorax.rotation) * (pt - thorax.position) - thoraxBox.center;
                    Vector3 outside = new Vector3(
                        Mathf.Max(0f, Mathf.Abs(local.x) - thoraxBox.size.x * 0.5f),
                        Mathf.Max(0f, Mathf.Abs(local.y) - thoraxBox.size.y * 0.5f),
                        Mathf.Max(0f, Mathf.Abs(local.z) - thoraxBox.size.z * 0.5f));
                    float inside = Mathf.Min(thoraxBox.size.x * 0.5f - Mathf.Abs(local.x),
                        Mathf.Min(thoraxBox.size.y * 0.5f - Mathf.Abs(local.y), thoraxBox.size.z * 0.5f - Mathf.Abs(local.z)));
                    float gap = (inside > 0f ? -inside : outside.magnitude) - tr;
                    r.Add(60d * Mathf.Max(0f, 0.002f - gap));
                }
            }
            foreach (string armSide in new[] { "left", "right" })
            {
                foreach (string armSegment in new[] { "_upper_arm", "_forearm" })
                {
                    _legacy.CapsuleSegment(poses, armSide + armSegment, out Vector3 a0, out Vector3 a1, out float ar);
                    foreach (string legSide in new[] { "left", "right" })
                    {
                        foreach (string legSegment in new[] { "_thigh", "_shank" })
                        {
                            _legacy.CapsuleSegment(poses, legSide + legSegment, out Vector3 l0, out Vector3 l1, out float lr);
                            float gap = DeadliftSetupSolver.DistanceSegmentSegment(a0, a1, l0, l1) - ar - lr;
                            r.Add(40d * Mathf.Max(0f, -gap));
                        }
                    }
                }
                Vector3 handCenter = poses[armSide + "_hand"].position;
                foreach (string legSide in new[] { "left", "right" })
                {
                    foreach (string legSegment in new[] { "_thigh", "_shank" })
                    {
                        _legacy.CapsuleSegment(poses, legSide + legSegment, out Vector3 l0, out Vector3 l1, out float lr);
                        float gap = DeadliftSetupSolver.DistancePointSegment(handCenter, l0, l1) - lr - 0.07f;
                        r.Add(40d * Mathf.Max(0f, -gap));
                    }
                }
            }

            // --- Authored joint ranges are hard.
            foreach (KeyValuePair<string, Quaternion> q in logical)
                BenchKinematics.LimitResiduals(_kin.Joint(q.Key), q.Value, spec.RequiredJointMarginDeg, r, 60d);
            return r;
        }

        /// <summary>
        /// Grippable half-length of the shaft (m): inner collar face at
        /// <see cref="PowerliftingSimulator.Equipment.BarbellPrototypeConfiguration.CollarFaceXBarMeters"/>
        /// minus half the measured palm span (GAM-68), so the whole palm is on the shaft.
        /// </summary>
        public static float GrippableHalfLengthM =>
            PowerliftingSimulator.Equipment.BarbellPrototypeConfiguration.CollarFaceXBarMeters - 0.5f * SquatPalmSurfaceSpan;

        private const float SquatPalmSurfaceSpan = PowerliftingSimulator.Squat.Unity.SquatPalmSurface.MeasuredPalmSpanMeters;

        private void Preferences(double[] x, DeadliftSetupSpec spec, List<double> r)
        {
            Pose pelvis = _legacy.PelvisPoseOf(x);
            Dictionary<string, Quaternion> logical = _legacy.LogicalRotations(x);
            Dictionary<string, Pose> poses = _kin.Solve(pelvis, logical);

            foreach (string side in new[] { "left", "right" })
            {
                float lateral = side == "left" ? _leftSign : -_leftSign;
                BoxCollider box = _legacy.FootBox(side);
                Pose foot = poses[side + "_foot"];
                Vector3 sole = foot.position + foot.rotation * (box.center - new Vector3(0f, box.size.y * 0.5f, 0f));
                Vector3 fwd = foot.rotation * Vector3.forward;
                // Technique preferences only (never feasibility): the declared
                // stance, and the bar near the sole centre line ("over midfoot").
                r.Add(0.5d * (sole.x - lateral * spec.StanceHalfWidth));
                r.Add(0.5d * (sole.z - 0f));
                r.Add(1d * (fwd.x - lateral * Mathf.Sin(spec.ToeOutDeg * Mathf.Deg2Rad)));
                Pose hand = poses[side + "_hand"];
                Vector3 grip = hand.position + hand.rotation * _legacy.MeasureHand(side == "left").AnchorLocal;
                if (!spec.GripWidthIsHard)
                    r.Add(2d * (grip.x - lateral * spec.GripHalfWidth));
            }

            Vector3 hipMid = 0.5f * (_kin.JointAnchorWorld(poses, "left_thigh") + _kin.JointAnchorWorld(poses, "right_thigh"));
            Vector3 shoulderMid = 0.5f * (_kin.JointAnchorWorld(poses, "left_upper_arm") + _kin.JointAnchorWorld(poses, "right_upper_arm"));
            Vector3 torso = shoulderMid - hipMid;
            float torsoAngle = Mathf.Atan2(torso.y, torso.z) * Mathf.Rad2Deg;
            r.Add(1d * (torsoAngle - spec.TorsoAboveHorizontalDeg) * Mathf.Deg2Rad);
            r.Add(1d * x[Neck]);

            double spineFlexion = (Math.Abs(x[Abdomen]) + Math.Abs(x[Thorax])) * Mathf.Rad2Deg;
            r.Add(1d * Math.Max(0d, spineFlexion - spec.NeutralSpineTotalFlexionDeg) * Mathf.Deg2Rad);
            r.Add(0.5d * x[Abdomen]);
            r.Add(0.5d * x[Thorax]);

            foreach (int o in new[] { LeftArm, RightArm })
            {
                r.Add(spec.ArmStraightnessWeight * x[o + 3]);
                r.Add(1.5d * x[o + 4]); r.Add(1.5d * x[o + 5]); r.Add(1.5d * x[o + 6]);
            }

            // Weak regularization; it is not part of the hard set.
            for (int i = 0; i < x.Length; i++)
                r.Add(0.005d * x[i]);
        }

        private DeadliftFeasibilityReport EvaluateHard(double[] x, DeadliftSetupSpec spec)
        {
            List<double> hard = Hard(x, spec);
            var report = new DeadliftFeasibilityReport();
            double sum = 0d, worst = 0d;
            int active = 0;
            foreach (double v in hard)
            {
                sum += v * v;
                worst = Math.Max(worst, Math.Abs(v));
                if (Math.Abs(v) > 1e-6) active++;
            }
            report.HardCost = sum;
            report.WorstTerm = worst;
            report.ActiveTerms = active;
            report.TermCount = hard.Count;
            return report;
        }
    }

    /// <summary>
    /// Hard-residual summary of a corrected feasibility solve. Diagnostic only:
    /// acceptance is the independent checker's measured constraint table.
    /// </summary>
    public sealed class DeadliftFeasibilityReport
    {
        public double HardCost;
        public double WorstTerm;
        public int ActiveTerms;
        public int TermCount;
        public bool IsZero => HardCost <= 1e-10;

        public string Describe() => string.Format(CultureInfo.InvariantCulture,
            "hard cost {0:E3}; worst weighted term {1:E2}; {2}/{3} hard terms non-zero ({4})",
            HardCost, WorstTerm, ActiveTerms, TermCount, IsZero ? "all hard terms zero" : "hard terms remain");
    }
}
