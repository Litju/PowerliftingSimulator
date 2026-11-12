using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// GAM-70 Gate A2 reachable-workspace bound (Lynch and Park, Modern
    /// Robotics ch. 5-6: the workspace of a serial chain is fixed by its link
    /// lengths and joint ranges; no optimizer is involved).
    ///
    /// Family: symmetric sagittal setups with no hip abduction (the
    /// conventional family; legs move in the sagittal plane). For every grid
    /// point over the authored hip-flexion, knee and ankle ranges and a given
    /// spine flexion, the rig's own forward kinematics places the pelvis so the
    /// soles are flat on the platform, puts the bar at the nearest fore-aft
    /// position that clears both shanks and thighs, and measures the straight-arm
    /// reach slack from the shoulder joint to the shaft (best case: grip directly
    /// below the shoulder, so the lateral offset is zero). This is an upper bound
    /// on the reach of the family: a negative best slack proves the family
    /// cannot put the palms on the shaft inside the authored ranges.
    /// </summary>
    public sealed class DeadliftReachBoundTests
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

        private sealed class Point
        {
            public float Hip, Knee, Ankle, Spine, Slack, ShoulderY, ShoulderZ, BarZ, HipY, CopMargin, ThoraxGap, TorsoDeg, ShoulderAboveHip, LiftOff, MinJointMargin;
        }

        [Timeout(3600000)]
        [UnityTest]
        public IEnumerator A2_REACH_BOUND_OF_THE_SAGITTAL_FAMILY_FROM_THE_RIG_FORWARD_KINEMATICS()
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            PhysicalAthleteRig rig = _world.Rig;
            var solver = new DeadliftSetupSolver(rig);
            BenchKinematics kin = solver.Kinematics;
            const float barY = DeadliftSetupAcceptance.BarAxisRestY;
            float reach = solver.StraightArmReachM(true);
            float g = Physics.gravity.magnitude;
            float weight = rig.TotalMassKg * g, barWeight = 75f * g;
            var mass = rig.Segments.ToDictionary(e => e.Key, e => e.Value.Body.mass);
            float total = mass.Values.Sum();
            JointInfoRange(kin, "left_thigh", out float hipLo, out float hipHi);
            JointInfoRange(kin, "left_shank", out float kneeLo, out float kneeHi);
            JointInfoRange(kin, "left_foot", out float ankleLo, out float ankleHi);
            JointInfoRange(kin, "abdomen", out _, out float abdHi);
            JointInfoRange(kin, "thorax", out _, out float thxHi);
            BoxCollider footBox = solver.FootBox("left");
            BoxCollider thoraxBox = (BoxCollider)rig.Segments["thorax"].Collider;

            double[] x = new double[31];
            Dictionary<string, Pose> Fk(float hipDeg, float kneeDeg, float ankleDeg, float abd, float thx, float pitchRad)
            {
                System.Array.Clear(x, 0, x.Length);
                x[2] = pitchRad; x[3] = abd * Mathf.Deg2Rad; x[4] = thx * Mathf.Deg2Rad;
                foreach (int o in new[] { 6, 11 }) { x[o] = hipDeg * Mathf.Deg2Rad; x[o + 3] = kneeDeg * Mathf.Deg2Rad; x[o + 4] = ankleDeg * Mathf.Deg2Rad; }
                return kin.Solve(solver.PelvisPoseOf(x), solver.LogicalRotations(x));
            }
            float FootPitch(Dictionary<string, Pose> p)
            {
                Vector3 f = p["left_foot"].rotation * Vector3.forward;
                return Mathf.Atan2(f.y, f.z);
            }

            // Spine families: neutral (0), neutral limit used by the setup preference
            // (25 deg total), and fully flexed to both authored stops (diagnostic only).
            var families = new (string name, float abd, float thx)[]
            {
                ("spine 0 deg", 0f, 0f),
                ("spine 25 deg (neutral preference limit)", 12.5f, 12.5f),
                ("spine at both stops (rounded back, diagnostic)", abdHi, thxHi)
            };
            const float step = 2f;
            var report = new StringBuilder();
            report.AppendLine($"straight-arm reach shoulder->palm anchor {reach:F4} m; bar axis y {barY:F6} m; hip [{hipLo},{hipHi}] knee [{kneeLo},{kneeHi}] ankle [{ankleLo},{ankleHi}] deg; grid step {step} deg");
            report.AppendLine("conditions per point: soles flat at y=0 (pelvis pitch and height solved exactly), bar at the nearest Z clearing shanks and thighs, thigh/thorax proxy gap >= -0.5 mm, grip below the shoulder (dx = 0)");
            var summary = new StringBuilder("family | grid points | reach-feasible | +thorax clear | +Mode B support | +shoulders above hips | +lift-off support | best slack mm (before lift-off) | best-slack pose hip/knee/ankle deg | hip y | shoulder y | torso deg | LARGEST MIN JOINT MARGIN, all conditions incl. lift-off\n");
            float bestNeutral = float.NegativeInfinity;
            foreach ((string name, float abd, float thx) in families)
            {
                int n = 0, reachOk = 0, thoraxOk = 0, supportOk = 0, allOk = 0, liftOk = 0;
                Point best = null, bestAny = null, bestMargin = null;
                for (float h = hipLo; h <= hipHi + 1e-3f; h += step)
                for (float k = kneeLo; k <= kneeHi + 1e-3f; k += step)
                for (float a = ankleLo; a <= ankleHi + 1e-3f; a += step)
                {
                    n++;
                    float p0 = FootPitch(Fk(h, k, a, abd, thx, 0f));
                    float p1 = FootPitch(Fk(h, k, a, abd, thx, 0.1f));
                    float slope = (p1 - p0) / 0.1f;
                    if (Mathf.Abs(slope) < 1e-4f) continue;
                    float pitch = -p0 / slope;
                    Dictionary<string, Pose> poses = Fk(h, k, a, abd, thx, pitch);
                    // Seat the soles on the platform.
                    Pose foot = poses["left_foot"];
                    float soleY = float.MaxValue;
                    for (int i = 0; i < 4; i++)
                        soleY = Mathf.Min(soleY, (foot.position + foot.rotation * (footBox.center + new Vector3(((i & 1) == 0 ? -1f : 1f) * footBox.size.x * 0.5f, -footBox.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * footBox.size.z * 0.5f))).y);
                    Vector3 shift = new Vector3(0f, -soleY, 0f);
                    var placed = poses.ToDictionary(e => e.Key, e => new Pose(e.Value.position + shift, e.Value.rotation));

                    Vector3 shoulder = kin.JointAnchorWorld(placed, "left_upper_arm");
                    // Prune: the slack can never exceed reach - vertical drop.
                    float slackBound = reach - (shoulder.y - barY);
                    if (slackBound < 0f && bestAny != null && slackBound < bestAny.Slack)
                        continue;

                    // Nearest clearing bar Z in front of the legs. Point-segment
                    // distance is convex in the point, so the blocked Z set at the
                    // bar height is one interval: find its front end.
                    float barZ = float.NegativeInfinity;
                    foreach (string seg in new[] { "left_shank", "left_thigh" })
                    {
                        solver.CapsuleSegment(placed, seg, out Vector3 s0, out Vector3 s1, out float r);
                        float need = r + DeadliftSetupSolver.AnchorBarRadius;
                        Vector2 A = new Vector2(s0.y, s0.z), B = new Vector2(s1.y, s1.z);
                        float D(float zz) => DeadliftSetupSolver.DistancePointSegment2D(new Vector2(barY, zz), A, B);
                        float lo = Mathf.Min(s0.z, s1.z) - 1f, hi = Mathf.Max(s0.z, s1.z) + 1f;
                        for (int it = 0; it < 40; it++)
                        {
                            float m1 = lo + (hi - lo) / 3f, m2 = hi - (hi - lo) / 3f;
                            if (D(m1) < D(m2)) hi = m2; else lo = m1;
                        }
                        float zMin = 0.5f * (lo + hi);
                        if (D(zMin) >= need)
                            continue; // never blocks at the bar height
                        float a0 = zMin, a1 = Mathf.Max(s0.z, s1.z) + 1f;
                        for (int it = 0; it < 30; it++)
                        {
                            float mid = 0.5f * (a0 + a1);
                            if (D(mid) >= need) a1 = mid; else a0 = mid;
                        }
                        barZ = Mathf.Max(barZ, a1);
                    }
                    if (float.IsNegativeInfinity(barZ)) barZ = placed["left_foot"].position.z;

                    float zBar = Mathf.Max(barZ, shoulder.z);
                    float slack = reach - new Vector2(shoulder.y - barY, shoulder.z - zBar).magnitude;

                    // Thigh vs thorax proxy (the production policy ignores only thigh-abdomen).
                    float thoraxGap = float.MaxValue;
                    solver.CapsuleSegment(placed, "left_thigh", out Vector3 t0, out Vector3 t1, out float tr);
                    Pose thorax = placed["thorax"];
                    for (int s = 0; s <= 8; s++)
                    {
                        Vector3 local = Quaternion.Inverse(thorax.rotation) * (Vector3.Lerp(t0, t1, s / 8f) - thorax.position) - thoraxBox.center;
                        Vector3 outside = new Vector3(Mathf.Max(0f, Mathf.Abs(local.x) - thoraxBox.size.x * 0.5f), Mathf.Max(0f, Mathf.Abs(local.y) - thoraxBox.size.y * 0.5f), Mathf.Max(0f, Mathf.Abs(local.z) - thoraxBox.size.z * 0.5f));
                        float inside = Mathf.Min(thoraxBox.size.x * 0.5f - Mathf.Abs(local.x), Mathf.Min(thoraxBox.size.y * 0.5f - Mathf.Abs(local.y), thoraxBox.size.z * 0.5f - Mathf.Abs(local.z)));
                        thoraxGap = Mathf.Min(thoraxGap, (inside > 0f ? -inside : outside.magnitude) - tr);
                    }

                    // Mode B support on the sole hull (feet symmetric, both flat).
                    Vector3 com = Vector3.zero;
                    foreach (KeyValuePair<string, Pose> e in placed) com += e.Value.position * mass[e.Key];
                    com /= total;
                    var hullPts = new List<Vector2>();
                    foreach (string side in new[] { "left_foot", "right_foot" })
                    {
                        Pose fp = placed[side];
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 cc = fp.position + fp.rotation * (footBox.center + new Vector3(((i & 1) == 0 ? -1f : 1f) * footBox.size.x * 0.5f, -footBox.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * footBox.size.z * 0.5f));
                            hullPts.Add(new Vector2(cc.x, cc.z));
                        }
                    }
                    List<Vector2> hullB = DeadliftSupportGeometry.Hull(hullPts);
                    float copMargin = DeadliftSupportGeometry.BestCopMargin(hullB, com, weight, new Vector3(0f, barY, zBar), barWeight, out _);
                    float liftOff = DeadliftSupportGeometry.SignedDistance(hullB, DeadliftSupportGeometry.CopUnderHandPull(com, weight, new Vector3(0f, barY, zBar), barWeight));
                    Vector3 hip = 0.5f * (kin.JointAnchorWorld(placed, "left_thigh") + kin.JointAnchorWorld(placed, "right_thigh"));
                    Vector3 sh = 0.5f * (shoulder + kin.JointAnchorWorld(placed, "right_upper_arm"));
                    var pt = new Point
                    {
                        Hip = h, Knee = k, Ankle = a, Spine = abd + thx, Slack = slack, ShoulderY = shoulder.y, ShoulderZ = shoulder.z, BarZ = zBar,
                        HipY = hip.y, CopMargin = copMargin, ThoraxGap = thoraxGap, TorsoDeg = Mathf.Atan2(sh.y - hip.y, sh.z - hip.z) * Mathf.Rad2Deg,
                        ShoulderAboveHip = sh.y - hip.y, LiftOff = liftOff,
                        MinJointMargin = Mathf.Min(Mathf.Min(h - hipLo, hipHi - h), Mathf.Min(Mathf.Min(k - kneeLo, kneeHi - k), Mathf.Min(a - ankleLo, ankleHi - a)))
                    };
                    if (bestAny == null || slack > bestAny.Slack) bestAny = pt;
                    if (slack < 0f) continue;
                    reachOk++;
                    if (thoraxGap < -0.0005f) continue;
                    thoraxOk++;
                    if (copMargin < 0f) continue;
                    supportOk++;
                    if (pt.ShoulderAboveHip < 0.03f) continue;
                    allOk++;
                    if (best == null || slack > best.Slack) best = pt;
                    if (liftOff < 0f) continue;
                    liftOk++;
                    if (bestMargin == null || pt.MinJointMargin > bestMargin.MinJointMargin ||
                        (pt.MinJointMargin == bestMargin.MinJointMargin && slack > bestMargin.Slack))
                        bestMargin = pt;
                }
                Point show = best ?? bestAny;
                summary.AppendLine(string.Format(C, "{0} | {1} | {2} | {3} | {4} | {5} | {13} | {6} | {7:F0}/{8:F0}/{9:F0} | {10:F3} | {11:F3} | {12:F1} | {14}",
                    name, n, reachOk, thoraxOk, supportOk, allOk,
                    best == null ? $"none (best reach-only slack {bestAny.Slack * 1000f:F1})" : (best.Slack * 1000f).ToString("F1", C),
                    show.Hip, show.Knee, show.Ankle, show.HipY, show.ShoulderY, show.TorsoDeg, liftOk,
                    bestMargin == null ? "none" : string.Format(C, "{0:F0} deg @ hip {1:F0} knee {2:F0} ankle {3:F0}, slack {4:F1} mm, lift-off {5:F1} mm, torso {6:F1} deg",
                        bestMargin.MinJointMargin, bestMargin.Hip, bestMargin.Knee, bestMargin.Ankle, bestMargin.Slack * 1000f, bestMargin.LiftOff * 1000f, bestMargin.TorsoDeg)));
                if (!name.StartsWith("spine at both stops"))
                    bestNeutral = Mathf.Max(bestNeutral, bestMargin == null ? -1f : bestMargin.MinJointMargin);
            }
            report.Append(summary);
            F.WriteEvidence("gateA2-reach-bound.txt", report.ToString());
            Debug.Log(report.ToString());
            // Measured 2026-11-12: with a spine within the 25 deg neutral limit, the
            // zero-abduction family reaches the shaft with every condition met only
            // within 2 deg of a hip, knee or ankle stop (grid step 2 deg).
            Assert.That(bestNeutral, Is.LessThanOrEqualTo(2f), report.ToString());
            yield return null;
        }

        private static void JointInfoRange(BenchKinematics kin, string id, out float lo, out float hi)
        {
            BenchKinematics.JointInfo j = kin.Joint(id);
            lo = j.Recipe.LowDegrees;
            hi = j.Recipe.HighDegrees;
        }
    }
}
