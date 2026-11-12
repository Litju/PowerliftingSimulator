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
    /// GAM-70 Gate A2 observation audit. These tests do not qualify a pose;
    /// they establish what the rig's own frames actually measure before any
    /// feasibility conclusion is drawn from them. The original A2 solver is
    /// replayed unchanged, and every world-space quantity is recomputed from
    /// the rig's real colliders, anchors and bind-pose hand landmarks.
    /// </summary>
    public sealed class DeadliftSetupObservationAuditTests
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

        private static DeadliftSetupSpec LegacyRung(string label, bool balance, bool torso, bool spine, bool arm, bool clear,
            float margin, bool knuckle = true, int starts = 20)
            => new DeadliftSetupSpec
            {
                Label = label,
                UseBalance = balance,
                UseTorsoAngle = torso,
                UseNeutralSpine = spine,
                UseArmPriors = arm,
                UseClearances = clear,
                LimitMarginDeg = margin,
                RequireKnuckleLineAlongShaft = knuckle,
                RandomStarts = starts
            };

        private static string FrameReport(PhysicalAthleteRig rig, DeadliftSetupSolver solver, DeadliftPlantModel model)
        {
            var b = new StringBuilder();
            b.AppendLine("--- hand frames in world at this pose ---");
            foreach (bool left in new[] { true, false })
            {
                solver.MeasureHandWorld(model, left, out Vector3 wrist, out Vector3 palmCenter, out Vector3 palmNormal,
                    out Vector3 anchor, out Vector3 indexMcp, out Vector3 littleMcp, out Vector3 middleMcp);
                Vector3 trueLine = indexMcp - littleMcp;
                Vector3 trueLineInPlane = trueLine - Vector3.Project(trueLine, palmNormal);
                Vector3 proxyLine = indexMcp - anchor;
                Vector3 proxyLineInPlane = proxyLine - Vector3.Project(proxyLine, palmNormal);
                Vector3 handLongAxis = model.Poses[left ? "left_hand" : "right_hand"].rotation * Vector3.right;
                Vector3 shaft = Vector3.right;
                float proxyAngle = Mathf.Min(Vector3.Angle(proxyLineInPlane, shaft), Vector3.Angle(proxyLineInPlane, -shaft));
                float trueAngle = Mathf.Min(Vector3.Angle(trueLineInPlane, shaft), Vector3.Angle(trueLineInPlane, -shaft));
                float between = Vector3.Angle(proxyLineInPlane, trueLineInPlane);
                float normalShaft = Vector3.Angle(palmNormal, shaft);
                Vector3 shaftPointAtGrip = new Vector3(anchor.x, model.BarCenterWorld.y, model.BarCenterWorld.z);
                Vector3 palmToBar = shaftPointAtGrip - palmCenter;
                float signedPalm = Vector3.Dot(palmToBar, palmNormal);
                float palmToAxis = Vector3.ProjectOnPlane(palmToBar, shaft).magnitude;
                b.AppendLine(string.Format(C,
                    "{0,-5} wrist {1} palm {2} n {3} anchor {4}\n" +
                    "      long axis {5} (y {6:F3}) | index MCP {7} | middle MCP {8} | little MCP {9}\n" +
                    "      proxy line {10} vs shaft {11:F2} deg | TRUE index->little line {12} vs shaft {13:F2} deg | proxy-vs-true {14:F2} deg\n" +
                    "      palm n vs shaft {15:F2} deg (90 = tangent) | palm->bar {16:F4} m along n | signed n.(bar-palm) {17:F4} m | palm to axis {18:F4} m\n" +
                    "      wrist twist/surviving logical {19}",
                    left ? "left" : "right", wrist.ToString("F4"), palmCenter.ToString("F4"), palmNormal.ToString("F3"), anchor.ToString("F4"),
                    handLongAxis.ToString("F3"), handLongAxis.y,
                    indexMcp.ToString("F4"), middleMcp.ToString("F4"), littleMcp.ToString("F4"),
                    proxyLineInPlane.normalized.ToString("F3"), proxyAngle,
                    trueLineInPlane.normalized.ToString("F3"), trueAngle, between,
                    normalShaft, palmToAxis, signedPalm, palmToAxis,
                    model.Margins.First(x => x.Joint == (left ? "left_hand" : "right_hand")).ToString()));
            }
            b.AppendLine("--- feet in world at this pose ---");
            foreach (FootAxisResidual f in model.FootResiduals)
                b.AppendLine(f.ToString());
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider box = (BoxCollider)rig.Segments[side + "_foot"].Collider;
                Pose foot = model.Poses[side + "_foot"];
                Vector3 ankle = solver.Kinematics.JointAnchorWorld(model.Poses, side + "_foot");
                Vector3 soleCenter = foot.position + foot.rotation * (box.center - new Vector3(0f, box.size.y * 0.5f, 0f));
                var corners = new List<Vector3>();
                for (int i = 0; i < 4; i++)
                {
                    Vector3 local = box.center + new Vector3(((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f, -box.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f);
                    corners.Add(foot.position + foot.rotation * local);
                }
                Vector3 heel = foot.position + foot.rotation * (box.center + new Vector3(0f, -box.size.y * 0.5f, -box.size.z * 0.5f));
                Vector3 toe = foot.position + foot.rotation * (box.center + new Vector3(0f, -box.size.y * 0.5f, box.size.z * 0.5f));
                b.AppendLine(string.Format(C,
                    "{0,-5} ankle {1} sole centre {2} ankle-above-sole {3:F4} m\n" +
                    "      corners y {4:F5} {5:F5} {6:F5} {7:F5} mm-as-m | heel z {8:F4} toe z {9:F4} | body {10}",
                    side, ankle.ToString("F4"), soleCenter.ToString("F4"), ankle.y - soleCenter.y,
                    corners[0].y, corners[1].y, corners[2].y, corners[3].y, heel.z, toe.z, foot.position.ToString("F4")));
            }
            b.AppendLine("--- bar vs shin/thigh clearance (exact cylinder-vs-capsule, YZ plane) ---");
            foreach (string side in new[] { "left", "right" })
            {
                foreach (string segment in new[] { "_shank", "_thigh" })
                {
                    solver.CapsuleSegment(model.Poses, side + segment, out Vector3 a, out Vector3 b0, out float radius);
                    float distance = DeadliftSetupSolver.DistancePointSegment2D(
                        new Vector2(model.BarCenterWorld.y, model.BarCenterWorld.z), new Vector2(a.y, a.z), new Vector2(b0.y, b0.z));
                    float gap = distance - radius - DeadliftSetupSolver.AnchorBarRadius;
                    b.AppendLine(string.Format(C, "{0}{1,-7} capsule {2} -> {3} r {4:F4} | axis-to-axis {5:F4} m | surface gap {6:F4} m ({7:F2} mm)",
                        side, segment, a.ToString("F4"), b0.ToString("F4"), radius, distance, gap, gap * 1000f));
                }
            }
            b.AppendLine("--- support polygon ---");
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider box = (BoxCollider)rig.Segments[side + "_foot"].Collider;
                Pose foot = model.Poses[side + "_foot"];
                var xs = new List<float>();
                var zs = new List<float>();
                for (int i = 0; i < 4; i++)
                {
                    Vector3 local = box.center + new Vector3(((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f, -box.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f);
                    Vector3 corner = foot.position + foot.rotation * local;
                    xs.Add(corner.x); zs.Add(corner.z);
                }
                b.AppendLine(string.Format(C, "{0,-5} support x [{1:F4},{2:F4}] z [{3:F4},{4:F4}]",
                    side, xs.Min(), xs.Max(), zs.Min(), zs.Max()));
            }
            Vector3 com = model.ComWorld;
            Vector3 heelL = model.Poses["left_foot"].position + model.Poses["left_foot"].rotation *
                (((BoxCollider)rig.Segments["left_foot"].Collider).center + new Vector3(0f, -((BoxCollider)rig.Segments["left_foot"].Collider).size.y * 0.5f, -((BoxCollider)rig.Segments["left_foot"].Collider).size.z * 0.5f));
            Vector3 toeL = model.Poses["left_foot"].position + model.Poses["left_foot"].rotation *
                (((BoxCollider)rig.Segments["left_foot"].Collider).center + new Vector3(0f, -((BoxCollider)rig.Segments["left_foot"].Collider).size.y * 0.5f, ((BoxCollider)rig.Segments["left_foot"].Collider).size.z * 0.5f));
            b.AppendLine(string.Format(C, "COM {0} | z margin to left heel {1:F4} m, to left toe {2:F4} m | pelvis {3} torso {4:F1} deg",
                com.ToString("F4"), com.z - heelL.z, toeL.z - com.z, model.PelvisWorld.position.ToString("F4"), model.TorsoAboveHorizontalDeg));
            b.AppendLine("--- joints ---");
            foreach (JointMargin m in model.Margins)
                b.AppendLine(m.ToString());
            return b.ToString();
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_OBS_RIG_FRAMES_AND_JOINT_LIMITS()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            PhysicalAthleteRig rig = _world.Rig;
            var solver = new DeadliftSetupSolver(rig);
            var b = new StringBuilder();
            b.AppendLine($"athlete {rig.TotalMassKg:F2} kg; platform support y {PhysicalAthleteDefinition.PlatformSupportPlaneY}; bar axis reference y {DeadliftSetupSpec_DefaultBarAxisY:F6} m");

            foreach (string id in new[]
                     {
                         "pelvis", "abdomen", "thorax", "head_neck",
                         "left_upper_arm", "left_forearm", "left_hand",
                         "left_thigh", "left_shank", "left_foot"
                     })
            {
                PhysicalAthleteRig.SegmentRuntime s = rig.Segments[id];
                b.AppendLine($"body {id,-15} pos {s.Body.position.ToString("F5")} rot {s.Body.rotation.eulerAngles.ToString("F2")} mass {s.Body.mass:F4}");
            }
            b.AppendLine("joints (world anchor at bind):");
            foreach (PoweredJointController.PoweredJointRuntime j in rig.PoweredController.Joints)
            {
                Vector3 a = j.Joint.transform.TransformPoint(j.Joint.anchor);
                Vector3 axis = j.Joint.transform.TransformDirection(j.Joint.axis).normalized;
                Vector3 sec = j.Joint.transform.TransformDirection(j.Joint.secondaryAxis).normalized;
                b.AppendLine(string.Format(C, "{0,-15} {1,-9} {2,-5} anchor {3} axis {4} secondary {5} range [{6:F0},{7:F0}] cone {8:F0} drive {9}",
                    j.Id, j.Recipe.Family, j.Recipe.Kind, a.ToString("F4"), axis.ToString("F3"), sec.ToString("F3"),
                    j.Recipe.LowDegrees, j.Recipe.HighDegrees, j.Recipe.SecondaryLimitDegrees,
                    j.Profile.HasValue ? $"{j.Profile.Value.BaseCapacityNm:F0} N m / K {j.Profile.Value.Spring:F0}" : "passive"));
            }

            b.AppendLine("--- measured hand frames in the hand body's local frame ---");
            foreach (bool left in new[] { true, false })
            {
                HandFrameLocal f = solver.MeasureHand(left);
                b.AppendLine(string.Format(C,
                    "{0,-5} palm {1} n {2} anchor {3} index {4} middle {5} little {6}",
                    left ? "left" : "right", f.PalmCenterLocal.ToString("F5"), f.PalmNormalLocal.ToString("F5"), f.AnchorLocal.ToString("F5"),
                    f.IndexMcpLocal.ToString("F5"), f.MiddleMcpLocal.ToString("F5"), f.LittleMcpLocal.ToString("F5")));
                Vector3 mcp = f.IndexMcpLocal - f.LittleMcpLocal;
                Vector3 proxy = f.IndexMcpLocal - f.AnchorLocal;
                Vector3 n = f.PalmNormalLocal;
                mcp -= Vector3.Project(mcp, n);
                proxy -= Vector3.Project(proxy, n);
                b.AppendLine(string.Format(C,
                    "      |index-little| {0:F4} m |index-middle| {1:F4} m |middle-little| {2:F4} m | palm-centre to index {3:F4} m | proxy-vs-true {4:F2} deg | true line local {5} | proxy local {6}",
                    mcp.magnitude, (f.IndexMcpLocal - f.MiddleMcpLocal).magnitude, (f.MiddleMcpLocal - f.LittleMcpLocal).magnitude,
                    (f.IndexMcpLocal - f.PalmCenterLocal).magnitude, Vector3.Angle(mcp, proxy), mcp.normalized.ToString("F3"), proxy.normalized.ToString("F3")));
            }

            b.AppendLine("--- arm and leg chain lengths from the built rig ---");
            Dictionary<string, Pose> bind = null;
            Pose pelvis = new Pose(rig.Segments["pelvis"].Body.position, rig.Segments["pelvis"].Body.rotation);
            bind = solver.Kinematics.Solve(pelvis, new Dictionary<string, Quaternion>());
            foreach (string side in new[] { "left", "right" })
            {
                Vector3 shoulder = solver.Kinematics.JointAnchorWorld(bind, side + "_upper_arm");
                Vector3 elbow = solver.Kinematics.JointAnchorWorld(bind, side + "_forearm");
                Vector3 wrist = solver.Kinematics.JointAnchorWorld(bind, side + "_hand");
                HandFrameLocal frame = solver.MeasureHand(side == "left");
                Pose hand = bind[side + "_hand"];
                b.AppendLine(string.Format(C, "{0,-5} shoulder {1} elbow {2} wrist {3} | upper arm {4:F4} forearm {5:F4} wrist->palm {6:F4} wrist->index {7:F4}",
                    side, shoulder.ToString("F4"), elbow.ToString("F4"), wrist.ToString("F4"),
                    Vector3.Distance(shoulder, elbow), Vector3.Distance(elbow, wrist),
                    Vector3.Distance(wrist, hand.position + hand.rotation * frame.PalmCenterLocal),
                    Vector3.Distance(wrist, hand.position + hand.rotation * frame.IndexMcpLocal)));
                Vector3 hip = solver.Kinematics.JointAnchorWorld(bind, side + "_thigh");
                Vector3 knee = solver.Kinematics.JointAnchorWorld(bind, side + "_shank");
                Vector3 ankle = solver.Kinematics.JointAnchorWorld(bind, side + "_foot");
                b.AppendLine(string.Format(C, "{0,-5} hip {1} knee {2} ankle {3} | thigh {4:F4} shank {5:F4} ankle height {6:F4}",
                    side, hip.ToString("F4"), knee.ToString("F4"), ankle.ToString("F4"),
                    Vector3.Distance(hip, knee), Vector3.Distance(knee, ankle), ankle.y - PhysicalAthleteDefinition.PlatformSupportPlaneY));
            }

            F.WriteEvidence("gateA2-audit-rig.txt", b.ToString());
            Assert.That(b.ToString(), Is.Not.Empty);
            yield return null;
        }

        private const float DeadliftSetupSpec_DefaultBarAxisY = 0.223075f;

        [Timeout(1800000)]
        [UnityTest]
        public IEnumerator A2_OBS_REPLAY_THE_LEGACY_LADDER_AND_MEASURE_THE_TRUE_HAND_AND_FOOT_FRAMES()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var rungs = new List<DeadliftSetupSpec>
            {
                LegacyRung("P0 position only (free spine)", false, false, false, false, false, 0f, false, 80),
                LegacyRung("P1 P0 + neutral spine", false, false, true, false, false, 0f, false, 80),
                LegacyRung("P2 P1 + COM over midfoot", true, false, true, false, false, 0f, false, 80),
                LegacyRung("R0 hard + proxy knuckle line", false, false, false, false, false, 0f, true, 80),
                LegacyRung("R1 R0 + COM over midfoot", true, false, false, false, false, 0f),
                LegacyRung("R2 R1 + neutral spine", true, false, true, false, false, 0f),
                LegacyRung("R3 R2 + arm priors", true, false, true, true, false, 0f),
                LegacyRung("R4 R3 + 4 deg margin, clearances", true, true, true, true, true, 4f)
            };
            var warm = new List<double[]>();
            var table = new StringBuilder();
            table.AppendLine("rung | foot[max] mm | foot[sole y] mm | foot[stance x] mm | foot[fore-aft] mm | sole pitch/roll deg | hand resid mm | PROXY knuckle deg | TRUE mcp deg | proxy-vs-true deg | palm n vs shaft deg | hand long axis y | wrist twist/swing | torso deg | hip flex | knee flex | ankle L");
            foreach (DeadliftSetupSpec spec in rungs)
            {
                spec.WarmStarts.AddRange(warm);
                DeadliftPlantModel m = _world.SolveSetup(spec);
                warm.Add(m.Solution);
                DeadliftSetupSolver solver = _world.Solver;
                JointMargin Get(string id) => m.Margins.First(x => x.Joint == id);
                HandFrameLocal left = solver.MeasureHand(true);
                Pose handPose = m.Poses["left_hand"];
                Vector3 index = handPose.position + handPose.rotation * left.IndexMcpLocal;
                Vector3 little = handPose.position + handPose.rotation * left.LittleMcpLocal;
                Vector3 palm = handPose.position + handPose.rotation * left.PalmCenterLocal;
                Vector3 n = (handPose.rotation * left.PalmNormalLocal).normalized;
                Vector3 anchor = handPose.position + handPose.rotation * left.AnchorLocal;
                Vector3 proxyLine = index - anchor; proxyLine -= Vector3.Project(proxyLine, n);
                Vector3 trueLine = index - little; trueLine -= Vector3.Project(trueLine, n);
                Vector3 longAxis = handPose.rotation * Vector3.right;
                float proxyAngle = Mathf.Min(Vector3.Angle(proxyLine, Vector3.right), Vector3.Angle(proxyLine, Vector3.left));
                float trueAngle = Mathf.Min(Vector3.Angle(trueLine, Vector3.right), Vector3.Angle(trueLine, Vector3.left));
                float between = Vector3.Angle(proxyLine, trueLine);
                float palmShaft = Vector3.Angle(n, Vector3.right);
                FootAxisResidual foot = m.FootResiduals.First(x => x.Side == "left");
                table.AppendLine(string.Format(C,
                    "{0} | {1:F2} | {2:F3} | {3:F3} | {4:F3} | {5:F2}/{6:F2} | {7:F2} | {8:F2} | {9:F2} | {10:F2} | {11:F2} | {12:F3} | {13:F1}/{14:F1} | {15:F1} | {16:F1} | {17:F1} | {18:F1}",
                    spec.Label, m.MaxFootResidualM * 1000, foot.SoleY * 1000, foot.LateralError * 1000, foot.ForeAftError * 1000,
                    foot.PitchDeg, foot.RollDeg, m.MaxHandResidualM * 1000, proxyAngle, trueAngle, between, palmShaft, longAxis.y,
                    Get("left_hand").TwistDeg, Get("left_hand").SwingConeDeg,
                    m.TorsoAboveHorizontalDeg, -Get("left_thigh").TwistDeg, Get("left_shank").TwistDeg, Get("left_foot").TwistDeg));
                if (spec.Label.StartsWith("P2") || spec.Label.StartsWith("R2") || spec.Label.StartsWith("R4"))
                {
                    table.AppendLine("  --- " + spec.Label + " ---");
                    table.AppendLine(FrameReport(_world.Rig, solver, m));
                }
            }
            F.WriteEvidence("gateA2-audit-ladder.txt", table.ToString());
            Assert.That(table.ToString(), Does.Contain("TRUE mcp"));
            yield return null;
        }
    }
}
