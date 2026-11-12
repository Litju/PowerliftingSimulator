using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>Acceptance class of one measured constraint.</summary>
    internal enum ConstraintClass
    {
        Hard,        // physical necessity or engine contract: fails acceptance
        Reference,   // the candidate's own declared starting reference: fails acceptance
        Preference,  // technique preference: reported, never fails
        Diagnostic   // measurement only: reported, never fails
    }

    internal sealed class ConstraintRow
    {
        public string Quantity, Frame, Units, Bound, Source;
        public double Measured, Margin;
        public ConstraintClass Class;
        public bool Pass => Class == ConstraintClass.Preference || Class == ConstraintClass.Diagnostic || Margin >= 0d;
        public bool Counts => Class == ConstraintClass.Hard || Class == ConstraintClass.Reference;

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "{0,-4} {1,-10} | {2,-46} | {3,-20} | {4,12:F4} {5,-4} | {6,-22} | margin {7,10:F4} | {8}",
            Counts ? (Pass ? "PASS" : "FAIL") : (Margin >= 0d ? "ok" : "miss"),
            Class.ToString().ToUpperInvariant(), Quantity, Frame, Measured, Units, Bound, Margin, Source);
    }

    /// <summary>
    /// GAM-70 Gate A2 independent physical acceptance of a kinematically placed
    /// setup. It recomputes every quantity from the posed rig colliders, the
    /// GAM-68 palm calibration and the bind-pose MCP landmarks; it never reads
    /// a solver residual or cost. Bodies are not moved: exact overlaps use
    /// Physics.ComputePenetration with explicit posed transforms.
    /// </summary>
    internal sealed class DeadliftSetupAcceptance
    {
        public const float BarAxisRestY = 0.223075f;     // GAM-70 A1: 0.225 cos(pi/24), measured rest 0.22308
        public readonly List<ConstraintRow> Rows = new List<ConstraintRow>();
        public readonly StringBuilder Notes = new StringBuilder();
        public string GripStyleMeasuredLeft, GripStyleMeasuredRight;
        public float ModeBBestPullN, ModeBMarginM, ModeAMarginM, LiftOffMarginM;

        public IEnumerable<ConstraintRow> Failures => Rows.Where(r => r.Counts && !r.Pass);
        public bool Passed => !Failures.Any();

        private void Add(ConstraintClass c, string quantity, string frame, double measured, string units, string bound, double margin, string source)
            => Rows.Add(new ConstraintRow { Class = c, Quantity = quantity, Frame = frame, Measured = measured, Units = units, Bound = bound, Margin = margin, Source = source });

        public string Table()
        {
            var b = new StringBuilder();
            b.AppendLine("res  class      | quantity                                       | frame                |     measured      | bound                  |   margin (+ok) | source");
            foreach (ConstraintRow r in Rows)
                b.AppendLine(r.ToString());
            b.AppendLine(Notes.ToString());
            int hard = Rows.Count(r => r.Class == ConstraintClass.Hard), href = Rows.Count(r => r.Class == ConstraintClass.Reference);
            b.AppendLine($"summary: {Failures.Count()} counted failures of {hard} HARD + {href} REFERENCE rows; " +
                         $"{Rows.Count(r => r.Class == ConstraintClass.Preference && r.Margin < 0d)} preference misses (not failures)");
            return b.ToString();
        }

        public static DeadliftSetupAcceptance Check(PhysicalAthleteRig rig, PhysicalBarbell bar, DeadliftSetupSolver solver, DeadliftPlantModel model)
        {
            var a = new DeadliftSetupAcceptance();
            DeadliftSetupSpec spec = model.Spec;
            Dictionary<string, Pose> poses = model.Poses;
            const string W = "world XZ, m";
            float g = Physics.gravity.magnitude;
            float athleteWeight = rig.TotalMassKg * g;

            // ---------- 1. grounded bar geometry ----------
            a.Add(ConstraintClass.Hard, "bar axis height on 450 mm plates (facet flat)", "world Y, m", model.BarCenterWorld.y, "m",
                "0.223075 +/- 0.0005", 0.0005 - Mathf.Abs(model.BarCenterWorld.y - BarAxisRestY), "A1 measured rest");
            a.Add(ConstraintClass.Diagnostic, "bar mass carried by the plates", "kg", spec.BarMassKg, "kg", "A1 75 kg config", 0, "BarbellLoadingSolver");

            // ---------- 7. foot-ground geometry ----------
            var hullPoints = new List<Vector2>();
            float soleZMin = float.MaxValue, soleZMax = float.MinValue;
            foreach (string side in new[] { "left", "right" })
            {
                BoxCollider box = solver.FootBox(side);
                Pose foot = poses[side + "_foot"];
                var c = new Vector3[4];
                for (int i = 0; i < 4; i++)
                {
                    c[i] = foot.position + foot.rotation * (box.center + new Vector3(
                        ((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f, -box.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f));
                    hullPoints.Add(new Vector2(c[i].x, c[i].z));
                    soleZMin = Mathf.Min(soleZMin, c[i].z);
                    soleZMax = Mathf.Max(soleZMax, c[i].z);
                }
                float minY = c.Min(p => p.y), maxY = c.Max(p => p.y);
                Vector3 soleCentre = foot.position + foot.rotation * (box.center - new Vector3(0f, box.size.y * 0.5f, 0f));
                Vector3 fwd = foot.rotation * Vector3.forward, up = foot.rotation * Vector3.up;
                Vector3 ankle = solver.Kinematics.JointAnchorWorld(poses, side + "_foot");
                float lateral = side == "left" ? solver.LeftSign : -solver.LeftSign;
                a.Add(ConstraintClass.Hard, side + " sole lowest corner (no penetration)", "world Y, m", minY, "m", ">= -0.0002", minY + 0.0002, "placement tol; tick-0 ComputePenetration");
                a.Add(ConstraintClass.Hard, side + " sole in contact (support exists)", "world Y, m", minY, "m", "<= +0.0005", 0.0005 - minY, "placement tol");
                a.Add(ConstraintClass.Reference, side + " sole corners level (planted flat start)", "world Y, m", maxY - minY, "m", "spread <= 0.0005", 0.0005 - (maxY - minY), "chosen start; IPF 4.3.1(5) allows rocking");
                a.Add(ConstraintClass.Diagnostic, side + " sole pitch", "deg", Mathf.Atan2(fwd.y, new Vector2(fwd.x, fwd.z).magnitude) * Mathf.Rad2Deg, "deg", "-", 0, "measured");
                a.Add(ConstraintClass.Diagnostic, side + " sole roll (up vs world up)", "deg", Vector3.Angle(up, Vector3.up), "deg", "-", 0, "measured");
                a.Add(ConstraintClass.Diagnostic, side + " foot yaw (toe-out, +lateral)", "deg", Mathf.Atan2(fwd.x * lateral, fwd.z) * Mathf.Rad2Deg, "deg", "-", 0, "measured");
                a.Add(ConstraintClass.Diagnostic, side + " ankle joint above sole", "world Y, m", ankle.y - soleCentre.y, "m", "-", 0, "rig");
                a.Add(ConstraintClass.Diagnostic, side + " sole centre", W, soleCentre.x, "m(x)", $"z {soleCentre.z:F4}", 0, "measured");
                ConstraintClass stanceClass = spec.FreeStance ? ConstraintClass.Preference : ConstraintClass.Reference;
                a.Add(stanceClass, side + " stance half-width error", "world X, m", soleCentre.x - lateral * spec.StanceHalfWidth, "m", "|e| <= 0.002", 0.002 - Mathf.Abs(soleCentre.x - lateral * spec.StanceHalfWidth),
                    spec.FreeStance ? "technique" : "declared candidate stance");
                a.Add(stanceClass, side + " toe-out error", "deg", Mathf.Atan2(fwd.x * lateral, fwd.z) * Mathf.Rad2Deg - spec.ToeOutDeg, "deg", "|e| <= 1", 1d - Mathf.Abs(Mathf.Atan2(fwd.x * lateral, fwd.z) * Mathf.Rad2Deg - spec.ToeOutDeg),
                    spec.FreeStance ? "technique" : "declared candidate stance");
                a.Add(spec.FreeForeAft ? ConstraintClass.Preference : ConstraintClass.Reference, side + " sole centre fore-aft (bar-over-midfoot ref)", "world Z, m", soleCentre.z, "m", "|z| <= 0.002", 0.002 - Mathf.Abs(soleCentre.z),
                    spec.FreeForeAft ? "technique" : "declared candidate");
            }

            // ---------- 7/8. support and COM in both contact modes ----------
            List<Vector2> hull = DeadliftSupportGeometry.Hull(hullPoints);
            Vector3 com = model.ComWorld;
            Vector3 barPoint = model.BarCenterWorld;
            float barWeight = spec.BarMassKg * g;
            a.ModeAMarginM = DeadliftSupportGeometry.SignedDistance(hull, new Vector2(com.x, com.z));
            a.ModeBMarginM = DeadliftSupportGeometry.BestCopMargin(hull, com, athleteWeight, barPoint, barWeight, out a.ModeBBestPullN);
            Vector2 range = DeadliftSupportGeometry.AdmissiblePullRange(hull, com, athleteWeight, barPoint, barWeight, 0f);
            Vector2 cop1 = DeadliftSupportGeometry.CopUnderHandPull(com, athleteWeight, barPoint, barWeight);
            a.LiftOffMarginM = DeadliftSupportGeometry.SignedDistance(hull, cop1);
            bool gripped = spec.Support == DeadliftSupportMode.GrippedGroundedBar;
            a.Add(gripped ? ConstraintClass.Diagnostic : ConstraintClass.Hard, "Mode A feet-only: COM inside sole hull", W, com.z, "m(z)", "signed dist >= 0", a.ModeAMarginM, "statics, feet carry W_a");
            a.Add(gripped ? ConstraintClass.Hard : ConstraintClass.Diagnostic, "Mode B gripped: best foot-COP margin, H in [0,m_b g]", W, a.ModeBBestPullN, "N(H)", "signed dist >= 0", a.ModeBMarginM, "coupled statics; bar rolls (Fx=0)");
            a.Add(ConstraintClass.Diagnostic, "Mode B admissible hand pull range", "N", float.IsNaN(range.x) ? -1 : range.x, "N", float.IsNaN(range.x) ? "empty" : $"..{range.y:F0} N", 0, "COP inside hull");
            a.Add(spec.RequireLiftOffSupport ? ConstraintClass.Hard : ConstraintClass.Diagnostic, "lift-off (H=m_b g): athlete+bar COM inside hull", W, cop1.y, "m(z)", "signed dist >= 0", a.LiftOffMarginM, "statics at plate unload");
            a.Add(ConstraintClass.Preference, "bar axis over the sole Z extent", "world Z, m", barPoint.z, "m", $"[{soleZMin:F4},{soleZMax:F4}]", Mathf.Min(barPoint.z - soleZMin, soleZMax - barPoint.z), "coaching; not IPF 4.3(1)");
            a.Add(ConstraintClass.Diagnostic, "COM lateral offset", "world X, m", com.x, "m", "-", 0, "symmetry");

            // ---------- 2/3. hands ----------
            Vector3[] anchors = new Vector3[2];
            int hi = 0;
            foreach (bool left in new[] { true, false })
            {
                string side = left ? "left" : "right";
                float lateral = left ? solver.LeftSign : -solver.LeftSign;
                solver.MeasureHandWorld(model, left, out Vector3 wrist, out Vector3 palm, out Vector3 n, out Vector3 anchor,
                    out Vector3 index, out Vector3 little, out _);
                anchors[hi++] = anchor;
                Vector3 axisPoint = new Vector3(anchor.x, barPoint.y, barPoint.z);
                Vector3 line = index - little; line -= Vector3.Project(line, n); line.Normalize();
                Vector3 proxy = index - anchor; proxy -= Vector3.Project(proxy, n); proxy.Normalize();
                float trueAngle = Mathf.Min(Vector3.Angle(line, Vector3.right), Vector3.Angle(line, Vector3.left));
                float proxyAngle = Mathf.Min(Vector3.Angle(proxy, Vector3.right), Vector3.Angle(proxy, Vector3.left));
                float tangent = Mathf.Abs(90f - Vector3.Angle(n, Vector3.right));
                float signedPalm = Vector3.Dot(axisPoint - palm, n);
                Vector3 longAxis = (poses[side + "_hand"].rotation * Vector3.right).normalized;
                bool indexLateral = Vector3.Dot(index - little, Vector3.right) * lateral > 0f;
                string style = n.z < -0.10f && !indexLateral ? "pronated" : n.z > 0.10f && indexLateral ? "supinated" : $"other(n.z {n.z:F2}, index {(indexLateral ? "lat" : "med")})";
                if (left) a.GripStyleMeasuredLeft = style; else a.GripStyleMeasuredRight = style;
                Vector3 shoulder = solver.Kinematics.JointAnchorWorld(poses, side + "_upper_arm");
                float reach = solver.StraightArmReachM(left);
                float d = Vector3.Distance(shoulder, anchor);
                Vector3 knee = solver.Kinematics.JointAnchorWorld(poses, side + "_shank");
                solver.CapsuleSegment(poses, side + "_shank", out _, out _, out float shankRadius);
                float outsideKnee = Mathf.Abs(anchor.x) - (Mathf.Abs(knee.x) + shankRadius);

                a.Add(ConstraintClass.Hard, side + " palm anchor to shaft axis", "world, m", Vector3.Distance(anchor, axisPoint), "m", "<= 0.002", 0.002 - Vector3.Distance(anchor, axisPoint), "placement tol; A3 grip drift");
                a.Add(ConstraintClass.Hard, side + " anchor on grippable shaft |x|", "world X, m", Mathf.Abs(anchor.x), "m", $"<= {DeadliftSetupFeasibilitySolver.GrippableHalfLengthM:F4}", DeadliftSetupFeasibilitySolver.GrippableHalfLengthM - Mathf.Abs(anchor.x), "collar face 0.655 - palm/2");
                a.Add(ConstraintClass.Hard, side + " TRUE index-little MCP line vs shaft", "deg", trueAngle, "deg", "<= 14.1", 14.1 - trueAngle, "GAM-68 measured MCP obliquity");
                a.Add(ConstraintClass.Diagnostic, side + " legacy proxy (palm->index) vs shaft", "deg", proxyAngle, "deg", "negative control", 0, "38.21 deg from true");
                a.Add(ConstraintClass.Hard, side + " palm plane tangent to shaft", "deg", tangent, "deg", "<= 5", 5d - tangent, "palm edge lift <= 2.2 mm");
                a.Add(ConstraintClass.Hard, side + " palm surface on shaft (n.(axis-palm) = r)", "m", signedPalm, "m", "0.0145 +/- 0.001", 0.001 - Mathf.Abs(signedPalm - DeadliftSetupSolver.AnchorBarRadius), "contact geometry");
                a.Add(ConstraintClass.Reference, side + " fingers hang around shaft (hand axis y)", "world Y", longAxis.y, "-", "<= -0.20", -0.20 - longAxis.y, "hook grip under load");
                a.Add(ConstraintClass.Reference, side + " grip style measured = declared " + spec.Grip.ToString().ToLowerInvariant(), "class", 0, style, "match", style == spec.Grip.ToString().ToLowerInvariant() ? 0 : -1, "IPF 4.3(1): grip optional");
                a.Add(ConstraintClass.Hard, side + " shoulder->anchor within straight reach", "m", d, "m", $"<= {reach:F4}", reach - d, "rig bone lengths");
                a.Add(ConstraintClass.Diagnostic, side + " reach slack (task-space boundary)", "m", reach - d, "m", "0 = arm singular", 0, "Lynch&Park ch.5 Jacobian rank");
                a.Add(spec.Technique == DeadliftTechnique.Undeclared ? ConstraintClass.Diagnostic : ConstraintClass.Reference,
                    side + " palm anchor lateral of knee surface", "world X, m", outsideKnee, "m",
                    spec.Technique == DeadliftTechnique.Sumo ? "<= -0.010" : spec.Technique == DeadliftTechnique.Conventional ? ">= +0.010" : "-",
                    spec.Technique == DeadliftTechnique.Sumo ? -0.010 - outsideKnee : spec.Technique == DeadliftTechnique.Conventional ? outsideKnee - 0.010 : 0,
                    "declared technique coherence");
                a.Add(ConstraintClass.Diagnostic, side + " wrist joint centre", "world", wrist.y, "m(y)", $"x {wrist.x:F4} z {wrist.z:F4}", 0, "measured");
            }
            a.Add(ConstraintClass.Diagnostic, "bilateral grip asymmetry |xL + xR|", "world X, m", Mathf.Abs(anchors[0].x + anchors[1].x), "m", "-", 0, "symmetry");
            a.Add(ConstraintClass.Preference, "grip half-width vs candidate", "world X, m", 0.5f * Mathf.Abs(anchors[0].x - anchors[1].x), "m", $"{spec.GripHalfWidth:F3}", -Mathf.Abs(0.5f * Mathf.Abs(anchors[0].x - anchors[1].x) - spec.GripHalfWidth), "technique");

            // ---------- 4/5. joint occupancy and remaining travel ----------
            foreach (JointMargin m in model.Margins)
            {
                a.Add(ConstraintClass.Hard, $"{m.Joint} primary {m.TwistDeg:F1} in [{m.LowDeg:F0},{m.HighDeg:F0}]", "joint X, deg", m.TwistDeg, "deg", "inside range", m.TwistMarginDeg, "authored recipe");
                a.Add(ConstraintClass.Reference, $"{m.Joint} primary travel left (actuated, off stop)", "deg", m.TwistMarginDeg, "deg", ">= 1", m.TwistMarginDeg - 1d, "hold must be actuator-borne");
                if (m.SwingLimitDeg > 0f)
                {
                    a.Add(ConstraintClass.Hard, $"{m.Joint} swing cone {m.SwingConeDeg:F1} of {m.SwingLimitDeg:F0}", "joint YZ, deg", m.SwingConeDeg, "deg", "inside cone", m.SwingMarginDeg, "authored recipe");
                    a.Add(ConstraintClass.Reference, $"{m.Joint} swing travel left (actuated, off stop)", "deg", m.SwingMarginDeg, "deg", ">= 1", m.SwingMarginDeg - 1d, "hold must be actuator-borne");
                }
            }

            // ---------- 6. bar-limb and body collisions ----------
            foreach (string side in new[] { "left", "right" })
            {
                foreach (string seg in new[] { "_shank", "_thigh" })
                {
                    solver.CapsuleSegment(poses, side + seg, out Vector3 p0, out Vector3 p1, out float radius);
                    float gap = DeadliftSetupSolver.DistancePointSegment2D(new Vector2(barPoint.y, barPoint.z), new Vector2(p0.y, p0.z), new Vector2(p1.y, p1.z))
                                - radius - DeadliftSetupSolver.AnchorBarRadius;
                    a.Add(ConstraintClass.Hard, $"bar shaft vs {side}{seg} surface gap", "YZ plane, m", gap, "m", ">= -0.0005", gap + 0.0005, "contact allowed, no penetration");
                }
            }
            float worstArmLeg = float.MaxValue; string worstArmLegName = "-";
            foreach (string aSide in new[] { "left", "right" })
                foreach (string aSeg in new[] { "_upper_arm", "_forearm" })
                {
                    solver.CapsuleSegment(poses, aSide + aSeg, out Vector3 a0, out Vector3 a1, out float ar);
                    foreach (string lSide in new[] { "left", "right" })
                        foreach (string lSeg in new[] { "_thigh", "_shank" })
                        {
                            solver.CapsuleSegment(poses, lSide + lSeg, out Vector3 l0, out Vector3 l1, out float lr);
                            float gap = DeadliftSetupSolver.DistanceSegmentSegment(a0, a1, l0, l1) - ar - lr;
                            if (gap < worstArmLeg) { worstArmLeg = gap; worstArmLegName = aSide + aSeg + "/" + lSide + lSeg; }
                        }
                }
            a.Add(ConstraintClass.Hard, "worst arm/leg capsule gap (" + worstArmLegName + ")", "m", worstArmLeg, "m", ">= -0.0005", worstArmLeg + 0.0005, "non-adjacent collision");

            // Exact Unity collider overlaps at the posed transforms, under the
            // production collision policy (bar vs grasping limbs ignored, as in bench).
            ExactOverlaps(a, rig, bar, model);

            // ---------- candidate definition ----------
            Vector3 hipMid = 0.5f * (solver.Kinematics.JointAnchorWorld(poses, "left_thigh") + solver.Kinematics.JointAnchorWorld(poses, "right_thigh"));
            Vector3 shoulderMid = 0.5f * (solver.Kinematics.JointAnchorWorld(poses, "left_upper_arm") + solver.Kinematics.JointAnchorWorld(poses, "right_upper_arm"));
            a.Add(spec.RequireShouldersAboveHips ? ConstraintClass.Reference : ConstraintClass.Diagnostic, "shoulders above hips (hip-hinge setup)", "world Y, m", shoulderMid.y - hipMid.y, "m", ">= 0.03", shoulderMid.y - hipMid.y - 0.03, "candidate definition");
            a.Add(ConstraintClass.Diagnostic, "torso above horizontal", "deg", model.TorsoAboveHorizontalDeg, "deg", "-", 0, "measured");
            a.Add(ConstraintClass.Diagnostic, "hip centre", "world, m", hipMid.y, "m(y)", $"z {hipMid.z:F4}", 0, "measured");
            a.Add(ConstraintClass.Diagnostic, "shoulder centre", "world, m", shoulderMid.y, "m(y)", $"z {shoulderMid.z:F4}", 0, "measured");
            foreach (string side in new[] { "left", "right" })
            {
                Vector3 knee = solver.Kinematics.JointAnchorWorld(poses, side + "_shank");
                a.Add(ConstraintClass.Diagnostic, side + " knee joint centre", "world, m", knee.x, "m(x)", $"y {knee.y:F4} z {knee.z:F4}", 0, "measured");
            }
            return a;
        }

        private static void ExactOverlaps(DeadliftSetupAcceptance a, PhysicalAthleteRig rig, PhysicalBarbell bar, DeadliftPlantModel model)
        {
            Pose PoseOf(Collider c, Rigidbody owner, Pose ownerTarget)
            {
                Vector3 lp = Quaternion.Inverse(owner.rotation) * (c.transform.position - owner.position);
                Quaternion lr = Quaternion.Inverse(owner.rotation) * c.transform.rotation;
                return new Pose(ownerTarget.position + ownerTarget.rotation * lp, ownerTarget.rotation * lr);
            }

            var seg = rig.Segments.ToDictionary(e => e.Key, e => (e.Value.Collider, e.Value.Body, PoseOf(e.Value.Collider, e.Value.Body, model.Poses[e.Key])));
            Pose barTarget = new Pose(model.BarCenterWorld, Quaternion.AngleAxis(180f / PhysicalBarbell.DefaultPlateColliderSides, Vector3.right));
            var barColliders = bar.Body.GetComponentsInChildren<Collider>(true).Where(c => c.enabled && c.gameObject.activeInHierarchy).ToList();
            Collider platform = rig.PlatformCollider;
            var overlaps = new List<string>();
            float worstSelf = 0f, worstBar = 0f, worstFloor = 0f;

            bool Overlap(Collider c1, Pose p1, Collider c2, Pose p2, out float depth)
            {
                depth = 0f;
                return Physics.ComputePenetration(c1, p1.position, p1.rotation, c2, p2.position, p2.rotation, out _, out depth) && depth > 0f;
            }

            var ids = seg.Keys.ToList();
            for (int i = 0; i < ids.Count; i++)
            {
                var si = seg[ids[i]];
                if (!ids[i].EndsWith("_foot") && Overlap(si.Collider, si.Item3, platform, new Pose(platform.transform.position, platform.transform.rotation), out float df))
                {
                    worstFloor = Mathf.Max(worstFloor, df);
                    overlaps.Add($"{ids[i]}-platform {df * 1000f:F2} mm");
                }
                for (int j = i + 1; j < ids.Count; j++)
                {
                    var sj = seg[ids[j]];
                    if (Physics.GetIgnoreCollision(si.Collider, sj.Collider) || Physics.GetIgnoreLayerCollision(si.Collider.gameObject.layer, sj.Collider.gameObject.layer))
                        continue;
                    if (Overlap(si.Collider, si.Item3, sj.Collider, sj.Item3, out float d))
                    {
                        worstSelf = Mathf.Max(worstSelf, d);
                        overlaps.Add($"{ids[i]}-{ids[j]} {d * 1000f:F2} mm");
                    }
                }
                if (DeadliftPlantWorld.GraspingLimbs.Contains(ids[i]))
                    continue;
                foreach (Collider bc in barColliders)
                {
                    if (Overlap(bc, PoseOf(bc, bar.Body, barTarget), si.Collider, si.Item3, out float db))
                    {
                        worstBar = Mathf.Max(worstBar, db);
                        overlaps.Add($"bar:{bc.name}-{ids[i]} {db * 1000f:F2} mm");
                    }
                }
            }
            foreach (string id in new[] { "left_foot", "right_foot" })
            {
                var sf = seg[id];
                Overlap(sf.Collider, sf.Item3, platform, new Pose(platform.transform.position, platform.transform.rotation), out float dfoot);
                a.Add(ConstraintClass.Hard, id + " box vs platform exact overlap", "PhysX, m", dfoot, "m", "<= 0.0005", 0.0005 - dfoot, "ComputePenetration");
            }
            a.Add(ConstraintClass.Hard, "worst exact body-body overlap (enabled pairs)", "PhysX, m", worstSelf, "m", "<= 0.0005", 0.0005 - worstSelf, "ComputePenetration");
            a.Add(ConstraintClass.Hard, "worst exact bar-body overlap (non-grasping)", "PhysX, m", worstBar, "m", "<= 0.0005", 0.0005 - worstBar, "ComputePenetration");
            a.Add(ConstraintClass.Hard, "worst exact non-foot body vs platform", "PhysX, m", worstFloor, "m", "<= 0.0005", 0.0005 - worstFloor, "ComputePenetration");
            a.Notes.AppendLine("exact overlaps: " + (overlaps.Count == 0 ? "none" : string.Join("; ", overlaps)));
        }
    }
}
