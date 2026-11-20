using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Equipment;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A2: is the deadlift start mechanically reachable by the
    /// existing canonical athlete? Measured on the real rig: actual segment
    /// geometry, joint anchors and limits, then a solved pose, then the real
    /// solver holding that pose with the 75 kg bar resting on the platform.
    /// </summary>
    public sealed class DeadliftSetupQualificationTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private DeadliftPlantWorld _world;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
            {
                yield return _world.Teardown();
                _world = null;
            }
        }

        private static string Survey(DeadliftPlantWorld w)
        {
            var b = new StringBuilder();
            PhysicalAthleteRig rig = w.Rig;
            b.AppendLine($"athlete mass {rig.TotalMassKg:F2} kg; platform support plane y {PhysicalAthleteDefinition.PlatformSupportPlaneY}");
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> s in rig.Segments)
            {
                Collider c = s.Value.Collider;
                string shape = c is BoxCollider box ? $"box {box.size.ToString("F3")} c {box.center.ToString("F3")}"
                    : c is CapsuleCollider cap ? $"capsule r {cap.radius:F3} h {cap.height:F3}" : c.GetType().Name;
                b.AppendLine($"  {s.Key,-15} mass {s.Value.Body.mass,6:F2} kg  pos {s.Value.Body.position.ToString("F3")}  {shape}");
            }
            b.AppendLine("joints (anchor world at the built pose, family, limits, drive):");
            foreach (PoweredJointController.PoweredJointRuntime j in rig.PoweredController.Joints)
            {
                PhysicalJointRecipe r = j.Recipe;
                string drive = j.Profile.HasValue
                    ? $"K {j.Profile.Value.Spring:F0} D {j.Profile.Value.Damper:F0} max {j.Profile.Value.BaseCapacityNm:F0} N m"
                    : "passive";
                Vector3 anchor = j.Joint.transform.TransformPoint(j.Joint.anchor);
                b.AppendLine($"  {j.Id,-15} {r.Family,-9} {r.Kind,-5} range [{r.LowDegrees:F0},{r.HighDegrees:F0}] swing {r.SecondaryLimitDegrees:F0}  {drive}  anchor {anchor.ToString("F3")}");
            }
            return b.ToString();
        }

        private static DeadliftSetupSpec DefaultSpec() => new DeadliftSetupSpec();


        private static string BindingJoints(DeadliftPlantModel m, float withinDeg)
        {
            var list = new List<string>();
            foreach (JointMargin jm in m.Margins)
            {
                if (jm.TwistMarginDeg < withinDeg)
                    list.Add($"{jm.Joint} twist {jm.TwistDeg:F0} of [{jm.LowDeg:F0},{jm.HighDeg:F0}]");
                if (jm.SwingLimitDeg > 0f && jm.SwingMarginDeg < withinDeg)
                    list.Add($"{jm.Joint} swing {jm.SwingConeDeg:F0}/{jm.SwingLimitDeg:F0}");
            }
            return list.Count == 0 ? "none" : string.Join("; ", list);
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_CONSTRAINT_LADDER_SHOWS_WHICH_REQUIREMENT_BREAKS_THE_REACH_TO_A_BAR_ON_450MM_PLATES()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var table = new StringBuilder();
            table.AppendLine(Survey(_world));
            table.AppendLine("rung | foot resid mm | hand resid mm | pelvis y | hip centre y | torso deg | spine flex deg (abd+thx) | hip flex | knee flex | elbow | wrist twist | knuckle line vs shaft deg | COM z margin to heel | binding joints (<1.5 deg to a limit)");
            var rungs = new List<DeadliftSetupSpec>();
            DeadliftSetupSpec Rung(string label, bool balance, bool torso, bool spine, bool arm, bool clear, float margin, bool knuckle = true, int starts = 20)
                => new DeadliftSetupSpec { Label = label, UseBalance = balance, UseTorsoAngle = torso, UseNeutralSpine = spine, UseArmPriors = arm, UseClearances = clear, LimitMarginDeg = margin, RequireKnuckleLineAlongShaft = knuckle, RandomStarts = starts };
            rungs.Add(Rung("P0 position only: soles flat, palm anchors on shaft, floor, trunk upright, limits (0 deg margin)", false, false, false, false, false, 0f, false, 80));
            rungs.Add(Rung("P1 P0 + neutral spine (<=25 deg total)", false, false, true, false, false, 0f, false, 80));
            rungs.Add(Rung("P2 P1 + COM over midfoot", true, false, true, false, false, 0f, false, 80));
            rungs.Add(Rung("R0 hard: soles flat, anchors on shaft, MCP along shaft, limits (0 deg margin)", false, false, false, false, false, 0f, true, 80));
            rungs.Add(Rung("R1 R0 + COM over midfoot", true, false, false, false, false, 0f));
            rungs.Add(Rung("R2 R1 + neutral spine (<=25 deg total)", true, false, true, false, false, 0f));
            rungs.Add(Rung("R3 R2 + straight elbows, neutral wrists, palms toward athlete", true, false, true, true, false, 0f));
            rungs.Add(Rung("R4 R3 + 4 deg limit margin, back angle 42 deg, limb/bar clearances", true, true, true, true, true, 4f));
            var models = new List<DeadliftPlantModel>();
            DeadliftSetupSolver solver0 = null;
            var warm = new List<double[]>();
            foreach (DeadliftSetupSpec spec in rungs)
            {
                spec.WarmStarts.AddRange(warm);
                DeadliftPlantModel m = _world.SolveSetup(spec);
                warm.Add(m.Solution);
                models.Add(m);
                var solver = _world.Solver;
                solver0 = solver;
                Dictionary<string, Pose> poses = m.Poses;
                Vector3 hipMid = 0.5f * (solver.Kinematics.JointAnchorWorld(poses, "left_thigh") + solver.Kinematics.JointAnchorWorld(poses, "right_thigh"));
                JointMargin Get(string id) => m.Margins.First(x => x.Joint == id);
                BoxCollider foot = (BoxCollider)_world.Rig.Segments["left_foot"].Collider;
                Pose fp = poses["left_foot"];
                float heelZ = (fp.position + fp.rotation * (foot.center + new Vector3(0f, -foot.size.y * 0.5f, -foot.size.z * 0.5f))).z;
                table.AppendLine(string.Format(C, "{0} | {1:F2} | {2:F2} | {3:F3} | {4:F3} | {5:F1} | {6:F1}+{7:F1} | {8:F1} | {9:F1} | {10:F1} | {11:F1} | {12:F3} | {14:F1} | {13}",
                    spec.Label, m.MaxFootResidualM * 1000, m.MaxHandResidualM * 1000, m.PelvisWorld.position.y, hipMid.y, m.TorsoAboveHorizontalDeg,
                    Get("abdomen").TwistDeg, Get("thorax").TwistDeg, -Get("left_thigh").TwistDeg, Get("left_shank").TwistDeg, Get("left_forearm").TwistDeg,
                    Get("left_hand").TwistDeg, m.ComWorld.z - heelZ, BindingJoints(m, 1.5f), solver.KnuckleLineAngleDeg(m, true)));
            }
            F.WriteEvidence("gateA2-constraint-ladder.txt", table.ToString());
            Assert.That(models.All(x => float.IsFinite((float)x.SolverCost)), Is.True);
            DeadliftPlantModel p0 = models[0], p1 = models[1], r0 = models.First(x => x.Spec.Label.StartsWith("R0")), r2 = models.First(x => x.Spec.Label.StartsWith("R2"));
            JointMargin Joint(DeadliftPlantModel m, string id) => m.Margins.First(x => x.Joint == id);
            // Measured (best found by a multi-start local solver, warm-started from every earlier rung, so residuals are
            // upper bounds of the true minimum). Pinned loosely so that a change of the plant forces a re-evaluation.
            // 1. Position-only reach with a free spine exists, with both spine joints at their stops.
            Assert.That(p0.MaxFootResidualM, Is.LessThan(0.001), table.ToString());
            Assert.That(p0.MaxHandResidualM, Is.LessThan(0.001), table.ToString());
            Assert.That(Joint(p0, "abdomen").TwistMarginDeg, Is.LessThan(1.5f), table.ToString());
            Assert.That(Joint(p0, "thorax").TwistMarginDeg, Is.LessThan(1.5f), table.ToString());
            // 2. With a neutral spine, reach exists only with hip and knee at their stops, and the knuckle line is far from the shaft.
            Assert.That(p1.MaxFootResidualM, Is.LessThan(0.004), table.ToString());
            Assert.That(p1.MaxHandResidualM, Is.LessThan(0.002), table.ToString());
            Assert.That(Joint(p1, "left_thigh").TwistMarginDeg, Is.LessThan(1f), table.ToString());
            Assert.That(Joint(p1, "left_shank").TwistMarginDeg, Is.LessThan(1f), table.ToString());
            Assert.That(solver0.KnuckleLineAngleDeg(p1, true), Is.GreaterThan(30f), "and the knuckle line is nowhere near the shaft: " + table);
            // 3. Aligning the knuckle line with the shaft is reachable with a free spine, with both wrists at their stops...
            Assert.That(r0.MaxFootResidualM, Is.LessThan(0.001), table.ToString());
            Assert.That(r0.MaxHandResidualM, Is.LessThan(0.001), table.ToString());
            Assert.That(solver0.KnuckleLineAngleDeg(r0, true), Is.LessThan(5f), table.ToString());
            Assert.That(Joint(r0, "left_hand").SwingMarginDeg, Is.LessThan(1.5f), table.ToString());
            Assert.That(Joint(r0, "abdomen").TwistMarginDeg, Is.LessThan(1.5f), table.ToString());
            // ...but not with a neutral spine: the best found keeps a multi-millimetre residual.
            Assert.That(r2.MaxFootResidualM, Is.GreaterThan(0.005), table.ToString());
            yield return null;
        }


        /// <summary>Position-only reach with neutral spine, balance and clearances: the best pose the plant offers for the 75 kg bar.</summary>
        internal static DeadliftSetupSpec BestAvailableSpec() => new DeadliftSetupSpec
        {
            Label = "P3 position-only reach, neutral spine, COM over midfoot, clearances, limits at 0 deg margin",
            UseBalance = true, UseTorsoAngle = false, UseNeutralSpine = true, UseArmPriors = false, UseClearances = true,
            LimitMarginDeg = 0f, RequireKnuckleLineAlongShaft = false, RandomStarts = 40
        };

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_DYNAMIC_HOLD_OF_THE_BEST_AVAILABLE_SETUP_WITH_THE_75KG_BAR_ON_THE_PLATFORM()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            DeadliftPlantModel m = _world.SolveSetup(BestAvailableSpec());
            var report = new StringBuilder();
            report.AppendLine(m.Describe());
            _world.PlaceAtSetup(75f, attachGrips: false);
            report.AppendLine($"tick-0 max penetration {_world.TickZeroMaxPenetrationM * 1000f:F2} mm; non-ignored overlaps: {(_world.TickZeroSelfPenetrations.Count == 0 ? "none" : string.Join("; ", _world.TickZeroSelfPenetrations))}");
            _world.Step(300);
            List<PlantSample> s = _world.Samples;
            List<PlantSample> tail = s.Skip(200).ToList();
            double left = tail.Average(x => x.LeftFootN), right = tail.Average(x => x.RightFootN);
            double weight = _world.Rig.TotalMassKg * 9.81;
            report.AppendLine($"feet: left {left:F1} N right {right:F1} N sum {left + right:F1} N vs athlete weight {weight:F1} N ({100 * (left + right - weight) / weight:F2} %)");
            report.AppendLine($"bar: floor reaction {tail.Average(x => x.BarFloorN):F1} N (weight {75 * 9.81:F1}), speed {tail.Max(x => x.BarSpeed) * 1000f:F2} mm/s, z {tail.Average(x => x.BarPosition.z) * 1000f:F2} mm");
            report.AppendLine($"body drift from the placed pose: max {s.Max(x => x.MaxBodyDriftM) * 1000f:F1} mm (final {s[s.Count - 1].MaxBodyDriftM * 1000f:F1} mm); worst joint error {tail.Max(x => x.MaxJointErrorDeg):F2} deg; worst modeled demand {tail.Max(x => x.MaxDemand):F3} ({tail.OrderByDescending(x => x.MaxDemand).First().MaxDemandJoint})");
            Vector3 com = tail.Aggregate(Vector3.zero, (a, x) => a + x.Com) / tail.Count;
            report.AppendLine($"COM {com.ToString("F4")}");
            foreach (PoweredJointController.PoweredJointRuntime j in _world.Rig.PoweredController.Joints)
                if (j.HasPostPhysicsDiagnostic)
                    report.AppendLine($"  {j.Id,-15} error {j.PostPhysicsDiagnostic.ErrorRad.magnitude * Mathf.Rad2Deg,6:F2} deg  demand {j.PostPhysicsDiagnostic.ModeledDemand:F3}  limit proximity {j.PostPhysicsDiagnostic.LimitProximity:F3}  solver torque {j.PostPhysicsDiagnostic.SolverTorqueJointSpaceNm.ToString("F1")} N m");
            var csv = new StringBuilder("tick,leftFootN,rightFootN,barFloorN,comY,comZ,barZ,barSpeed,maxDriftM,maxJointErrDeg,maxDemand" + System.Environment.NewLine);
            foreach (PlantSample x in s)
                csv.AppendLine(string.Join(",", x.Tick, x.LeftFootN, x.RightFootN, x.BarFloorN, x.Com.y, x.Com.z, x.BarPosition.z, x.BarSpeed, x.MaxBodyDriftM, x.MaxJointErrorDeg, x.MaxDemand));
            F.WriteEvidence("gateA2-hold-trace.csv", csv.ToString());
            F.WriteEvidence("gateA2-hold.txt", report.ToString());
            // Measured: with limb/bar clearances the end-stop pose does not exist (the solver cannot seat the soles),
            // the shins overlap the bar at tick zero, and a drive saturates holding what is left.
            Assert.That(m.MaxFootResidualM, Is.GreaterThan(0.010), report.ToString());
            Assert.That(_world.TickZeroSelfPenetrations.Any(x => x.StartsWith("bar:") && x.Contains("shank")), Is.True, report.ToString());
            Assert.That(s.Max(x => x.MaxDemand), Is.GreaterThan(0.95f), report.ToString());
            yield return null;
        }

        /// <summary>The Gate A2 acceptance itself. It FAILS on the canonical athlete (see the constraint ladder); it runs only on request.</summary>
        [Explicit("GAM-70 Gate A2 acceptance: the canonical athlete cannot meet it; evidence in Logs/GAM70/gateA2-*.txt")]
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_GATE_THE_FLOOR_SETUP_IS_REACHABLE_WITH_ALIGNED_HANDS_INSIDE_THE_JOINT_LIMITS()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            string survey = Survey(_world);
            DeadliftSetupSpec spec = DefaultSpec();
            DeadliftPlantModel m = _world.SolveSetup(spec);
            PhysicalAthleteRig rig = _world.Rig;
            DeadliftSetupSolver solver = _world.Solver;

            var b = new StringBuilder(survey);
            b.AppendLine("--- setup solve ---");
            b.AppendLine(m.Describe());

            Dictionary<string, Pose> poses = m.Poses;
            float armLength(string side) =>
                Vector3.Distance(solver.Kinematics.JointAnchorWorld(poses, side + "_upper_arm"), solver.Kinematics.JointAnchorWorld(poses, side + "_forearm")) +
                Vector3.Distance(solver.Kinematics.JointAnchorWorld(poses, side + "_forearm"), solver.Kinematics.JointAnchorWorld(poses, side + "_hand")) +
                Vector3.Distance(solver.Kinematics.JointAnchorWorld(poses, side + "_hand"), side == "left" ? m.LeftGripWorld : m.RightGripWorld);
            foreach (string side in new[] { "left", "right" })
            {
                Vector3 shoulder = solver.Kinematics.JointAnchorWorld(poses, side + "_upper_arm");
                Vector3 grip = side == "left" ? m.LeftGripWorld : m.RightGripWorld;
                b.AppendLine($"{side} arm: shoulder->grip {Vector3.Distance(shoulder, grip):F4} m of maximum straight reach {armLength(side):F4} m (elbow flexion {Quaternion.Angle(Quaternion.identity, m.SetupLogical[side + "_forearm"]):F1} deg)");
            }
            Vector3 hipMid = 0.5f * (solver.Kinematics.JointAnchorWorld(poses, "left_thigh") + solver.Kinematics.JointAnchorWorld(poses, "right_thigh"));
            b.AppendLine($"hip centre {hipMid.ToString("F3")}; COM {m.ComWorld.ToString("F3")}; bar axis {m.BarCenterWorld.ToString("F4")}");
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider foot = (BoxCollider)rig.Segments[side + "_foot"].Collider;
                Pose p = poses[side + "_foot"];
                Vector3 heel = p.position + p.rotation * (foot.center + new Vector3(0f, -foot.size.y * 0.5f, -foot.size.z * 0.5f));
                Vector3 toe = p.position + p.rotation * (foot.center + new Vector3(0f, -foot.size.y * 0.5f, foot.size.z * 0.5f));
                b.AppendLine($"{side} foot sole heel z {heel.z:F3} toe z {toe.z:F3} (COM z {m.ComWorld.z:F3}; margin to heel {m.ComWorld.z - heel.z:F3}, to toe {toe.z - m.ComWorld.z:F3})");
            }
            F.WriteEvidence("gateA2-setup-pose.txt", b.ToString());

            Assert.That(m.MaxFootResidualM, Is.LessThan(0.001), "soles are not flat on the platform\n" + b);
            Assert.That(m.MaxHandResidualM, Is.LessThan(0.002), "the palm anchors cannot reach the shaft\n" + b);
            foreach (JointMargin jm in m.Margins)
            {
                Assert.That(jm.TwistMarginDeg, Is.GreaterThanOrEqualTo(2f), jm.Joint + " twist is at its limit\n" + b);
                if (jm.SwingLimitDeg > 0f)
                    Assert.That(jm.SwingMarginDeg, Is.GreaterThanOrEqualTo(2f), jm.Joint + " swing is at its limit\n" + b);
            }
            Assert.That(m.TorsoAboveHorizontalDeg, Is.InRange(20f, 70f), b.ToString());
            yield return null;
        }
    }
}
