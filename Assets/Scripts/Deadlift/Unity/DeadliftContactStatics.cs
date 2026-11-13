using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 C4: contact-consistent inverse statics of the grounded,
    /// bilaterally gripped deadlift setup at one placed configuration.
    ///
    /// Bodies: 16 athlete segments + the bar, with their actual masses and
    /// world COMs; gravity (0, -g, 0). Declared contact wrench set (one member
    /// of the statically indeterminate family the 6-DOF welds admit; the
    /// choice is a modelling decision, stated, not discovered):
    ///   * each grip transmits a purely vertical force H (hand pulls the bar
    ///     up by H, the bar pulls the hand down by H) and zero moment;
    ///   * the two plates carry the rest of the bar weight with vertical
    ///     normal reactions only; their split follows the bar's lateral moment;
    ///   * each foot carries a vertical normal reaction at a centre of pressure
    ///     on its own sole centreline, both at the same world Z; the combined
    ///     COP and the L/R split follow the athlete's sagittal and lateral
    ///     moment balance; zero friction and zero free moment are required
    ///     (no horizontal external load exists in this set).
    /// Per joint, the torque the parent must apply to the child subtree is
    ///   tau = -sum_{b in subtree} (c_b - p) x m_b g - sum_{k in subtree} (r_k - p) x F_k,
    /// expressed on the child's current joint axes (x = axis, y = secondary
    /// projected, z = x cross y), the same frame and sign the accepted static
    /// reference uses (drive torque on child = K e). Hinge y/z components are
    /// carried by the hinge constraint, not the drive.
    /// Feasibility: plate and foot normals >= 0, each COP inside its own sole
    /// rectangle, every drive channel within its active ceiling. This is a
    /// mathematical static equilibrium; whether the Unity plant realizes and
    /// sustains it is a separate, measured question (C5).
    /// </summary>
    public sealed class DeadliftContactStatics
    {
        public sealed class JointLoad
        {
            public string Id;
            public PhysicalJointKind Kind;
            public Vector3 TorqueWorldNm;
            public Vector3 TorqueAxesNm;   // on the child's joint axes
            public float DriveTwistNm, DriveSwingNm, CeilingNm, Spring;
            public float DemandFraction => Mathf.Max(Mathf.Abs(DriveTwistNm), Kind == PhysicalJointKind.Hinge ? 0f : DriveSwingNm) / Mathf.Max(CeilingNm, 1e-3f);
            public float OffsetDeg => (Kind == PhysicalJointKind.Hinge ? Mathf.Abs(TorqueAxesNm.x) : TorqueAxesNm.magnitude) / Spring * Mathf.Rad2Deg;
        }

        public float HandPullN;
        public float PlateLN, PlateRN, FootLN, FootRN;
        public Vector3 CopL, CopR;
        public float CopLMarginM, CopRMarginM;
        public float BarSagittalMomentResidualNm;
        public readonly List<JointLoad> Joints = new List<JointLoad>();
        public readonly List<string> Violations = new List<string>();
        public bool Feasible => Violations.Count == 0;

        public static DeadliftContactStatics Solve(PhysicalAthleteRig rig, Rigidbody bar, Collider leftPlate, Collider rightPlate,
            Vector3 leftGripWorld, Vector3 rightGripWorld, float handPullN, float ceilingScale = 1f)
        {
            if (rig == null) throw new ArgumentNullException(nameof(rig));
            if (bar == null) throw new ArgumentNullException(nameof(bar));
            var s = new DeadliftContactStatics { HandPullN = handPullN };
            float g = -Physics.gravity.y;
            float wb = bar.mass * g;
            Vector3 cb = bar.worldCenterOfMass;

            // ---- bar: vertical force and lateral (about world Z) moment ----
            float xl = leftPlate.bounds.center.x, xr = rightPlate.bounds.center.x;
            // P_L + P_R = W_b - 2H ; P_L xl + P_R xr = W_b xb - H (xgl + xgr)
            float sumP = wb - 2f * handPullN;
            float momP = wb * cb.x - handPullN * (leftGripWorld.x + rightGripWorld.x);
            s.PlateRN = (momP - sumP * xl) / (xr - xl);
            s.PlateLN = sumP - s.PlateRN;
            // sagittal (about world X) moment of the vertical set about the bar COM; plates touch directly under the plate centres
            float zl = leftPlate.bounds.center.z, zr = rightPlate.bounds.center.z;
            s.BarSagittalMomentResidualNm = s.PlateLN * (zl - cb.z) + s.PlateRN * (zr - cb.z) +
                                            handPullN * (leftGripWorld.z - cb.z + rightGripWorld.z - cb.z);
            if (s.PlateLN < 0f || s.PlateRN < 0f)
                s.Violations.Add(string.Format(CultureInfo.InvariantCulture, "plate normal negative: L {0:F1} R {1:F1} N (bar would lift)", s.PlateLN, s.PlateRN));

            // ---- athlete: feet ----
            float ma = 0f; Vector3 mc = Vector3.zero;
            foreach (var seg in rig.Segments.Values) { ma += seg.Body.mass; mc += seg.Body.mass * seg.Body.worldCenterOfMass; }
            Vector3 ca = mc / ma;
            float wa = ma * g;
            float n = wa + 2f * handPullN;
            float zCop = (wa * ca.z + handPullN * (leftGripWorld.z + rightGripWorld.z)) / n;
            float xMoment = wa * ca.x + handPullN * (leftGripWorld.x + rightGripWorld.x);
            var soles = new Dictionary<string, Vector3[]>();
            Vector3 Centerline(string side)
            {
                BoxCollider box = (BoxCollider)rig.Segments[side + "_foot"].Collider;
                Transform t = box.transform;
                var c = new Vector3[4];
                for (int i = 0; i < 4; i++)
                    c[i] = t.TransformPoint(box.center + new Vector3(((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f, -box.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f));
                soles[side] = c;
                Vector3 centre = t.TransformPoint(box.center - new Vector3(0f, box.size.y * 0.5f, 0f));
                Vector3 fwd = t.TransformDirection(Vector3.forward);
                float k = Mathf.Abs(fwd.z) < 1e-4f ? 0f : (zCop - centre.z) / fwd.z;
                return centre + k * fwd;
            }
            s.CopL = Centerline("left");
            s.CopR = Centerline("right");
            // F_L + F_R = n ; F_L xL + F_R xR = xMoment
            s.FootRN = (xMoment - n * s.CopL.x) / (s.CopR.x - s.CopL.x);
            s.FootLN = n - s.FootRN;
            s.CopLMarginM = DeadliftSupportGeometry.SignedDistance(DeadliftSupportGeometry.Hull(soles["left"].Select(p => new Vector2(p.x, p.z))), new Vector2(s.CopL.x, s.CopL.z));
            s.CopRMarginM = DeadliftSupportGeometry.SignedDistance(DeadliftSupportGeometry.Hull(soles["right"].Select(p => new Vector2(p.x, p.z))), new Vector2(s.CopR.x, s.CopR.z));
            if (s.FootLN < 0f || s.FootRN < 0f)
                s.Violations.Add(string.Format(CultureInfo.InvariantCulture, "foot normal negative: L {0:F1} R {1:F1} N", s.FootLN, s.FootRN));
            if (s.CopLMarginM < 0f || s.CopRMarginM < 0f)
                s.Violations.Add(string.Format(CultureInfo.InvariantCulture, "foot COP outside its sole: L {0:F4} R {1:F4} m", s.CopLMarginM, s.CopRMarginM));

            // External point forces on athlete bodies.
            var forces = new List<(string body, Vector3 at, Vector3 f)>
            {
                ("left_foot", new Vector3(s.CopL.x, soles["left"].Min(p => p.y), s.CopL.z), new Vector3(0f, s.FootLN, 0f)),
                ("right_foot", new Vector3(s.CopR.x, soles["right"].Min(p => p.y), s.CopR.z), new Vector3(0f, s.FootRN, 0f)),
                ("left_hand", leftGripWorld, new Vector3(0f, -handPullN, 0f)),
                ("right_hand", rightGripWorld, new Vector3(0f, -handPullN, 0f)),
            };

            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue) continue;
                ConfigurableJoint cj = joint.Joint;
                Vector3 p = cj.transform.TransformPoint(cj.anchor);
                Vector3 m = Vector3.zero;
                foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                {
                    if (!InSubtree(recipe.Id, joint.Id)) continue;
                    Rigidbody b = rig.Segments[recipe.Id].Body;
                    m += Vector3.Cross(b.worldCenterOfMass - p, b.mass * Physics.gravity);
                    foreach (var f in forces)
                        if (f.body == recipe.Id)
                            m += Vector3.Cross(f.at - p, f.f);
                }
                Vector3 tau = -m;
                Vector3 x = cj.transform.TransformDirection(cj.axis).normalized;
                Vector3 y = Vector3.ProjectOnPlane(cj.transform.TransformDirection(cj.secondaryAxis), x).normalized;
                Vector3 z = Vector3.Cross(x, y);
                var load = new JointLoad
                {
                    Id = joint.Id, Kind = joint.Recipe.Kind, TorqueWorldNm = tau,
                    TorqueAxesNm = new Vector3(Vector3.Dot(tau, x), Vector3.Dot(tau, y), Vector3.Dot(tau, z)),
                    CeilingNm = joint.Profile.Value.BaseCapacityNm * ceilingScale, Spring = joint.Profile.Value.Spring
                };
                load.DriveTwistNm = load.TorqueAxesNm.x;
                load.DriveSwingNm = Mathf.Max(Mathf.Abs(load.TorqueAxesNm.y), Mathf.Abs(load.TorqueAxesNm.z));
                s.Joints.Add(load);
                if (load.DemandFraction > 1f)
                    s.Violations.Add(string.Format(CultureInfo.InvariantCulture, "{0} drive demand {1:F1}/{2:F1} N m exceeds its ceiling", joint.Id,
                        Mathf.Max(Mathf.Abs(load.DriveTwistNm), load.Kind == PhysicalJointKind.Hinge ? 0f : load.DriveSwingNm), load.CeilingNm));
            }
            return s;
        }

        /// <summary>The per-joint torque on the child's joint axes (drive channels only), for <see cref="DeadliftSetupHold.SetStaticReference"/>.</summary>
        public Dictionary<string, Vector3> DriveTorquesAxes()
        {
            var d = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            foreach (JointLoad j in Joints)
                d[j.Id] = j.Kind == PhysicalJointKind.Hinge ? new Vector3(j.TorqueAxesNm.x, 0f, 0f) : j.TorqueAxesNm;
            return d;
        }

        public string Describe()
        {
            var b = new StringBuilder();
            b.AppendLine(string.Format(CultureInfo.InvariantCulture, "C4 contact statics, hand pull H = {0:F1} N per hand ({1})", HandPullN, Feasible ? "FEASIBLE" : "INFEASIBLE"));
            b.AppendLine(string.Format(CultureInfo.InvariantCulture, "plates L {0:F1} R {1:F1} N | bar sagittal moment residual of the vertical set {2:F3} N m", PlateLN, PlateRN, BarSagittalMomentResidualNm));
            b.AppendLine(string.Format(CultureInfo.InvariantCulture, "feet L {0:F1} R {1:F1} N | COP L {2} R {3} m | margins inside own sole L {4:F4} R {5:F4} m",
                FootLN, FootRN, CopL.ToString("F4"), CopR.ToString("F4"), CopLMarginM, CopRMarginM));
            b.AppendLine("joint           kind    tau axes x/y/z (N m)            drive twist  swing  ceiling  demand  offset deg");
            foreach (JointLoad j in Joints)
                b.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0,-15} {1,-6} {2,-30} {3,8:F1} {4,7:F1} {5,7:F1} {6,7:F3} {7,7:F2}",
                    j.Id, j.Kind, j.TorqueAxesNm.ToString("F1"), j.DriveTwistNm, j.Kind == PhysicalJointKind.Hinge ? 0f : j.DriveSwingNm, j.CeilingNm, j.DemandFraction, j.OffsetDeg));
            foreach (string v in Violations) b.AppendLine("VIOLATION: " + v);
            return b.ToString();
        }

        private static bool InSubtree(string segmentId, string rootId)
        {
            string current = segmentId;
            while (current != null)
            {
                if (current == rootId) return true;
                string parent = null;
                foreach (PhysicalSegmentRecipe r in PhysicalAthleteDefinition.Segments)
                    if (r.Id == current) { parent = r.ParentId; break; }
                current = parent;
            }
            return false;
        }
    }
}
