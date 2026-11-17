using System.Collections.Generic;
using System.Globalization;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70: horizontal bar-to-leg geometry, measured separately for the shins
    /// and the thighs, from the actual segment surfaces (the rig's capsule
    /// colliders) and the shaft radius. The same definition serves the planned
    /// path (solver poses) and the realized plant (live colliders), so a
    /// planned/realized comparison measures one quantity.
    ///
    /// Surface gap = (sagittal distance from the bar axis to the leg capsule's
    /// core segment) - capsule radius - shaft radius. The shaft is a long
    /// capsule along world X spanning both legs in the sumo stance, so the
    /// sagittal (Y-Z) distance is the clearance the shaft actually has; the 3D
    /// segment-segment value is reported beside it as a check. Negative = overlap.
    /// </summary>
    public static class DeadliftBarLegGeometry
    {
        public static readonly string[] Shins = { "left_shank", "right_shank" };
        public static readonly string[] Thighs = { "left_thigh", "right_thigh" };

        public struct Gaps
        {
            public float LeftShin, RightShin, LeftThigh, RightThigh;
            public float Shin => Mathf.Min(LeftShin, RightShin);
            public float Thigh => Mathf.Min(LeftThigh, RightThigh);
            public float Leg => Mathf.Min(Shin, Thigh);

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "shin L/R {0:F1}/{1:F1} mm, thigh L/R {2:F1}/{3:F1} mm", LeftShin * 1000f, RightShin * 1000f, LeftThigh * 1000f, RightThigh * 1000f);
        }

        /// <summary>Sagittal surface gap between the bar axis (through <paramref name="barCenter"/>, along X) and a capsule core segment a-b.</summary>
        public static float SagittalGap(Vector3 barCenter, Vector3 a, Vector3 b, float limbRadius, float shaftRadius) =>
            DeadliftSetupSolver.DistancePointSegment2D(new Vector2(barCenter.y, barCenter.z), new Vector2(a.y, a.z), new Vector2(b.y, b.z))
            - limbRadius - shaftRadius;

        /// <summary>Planned gaps of a solver pose set with the bar centre at <paramref name="barCenter"/>.</summary>
        public static Gaps Planned(DeadliftSetupSolver solver, Dictionary<string, Pose> poses, Vector3 barCenter)
        {
            float G(string id)
            {
                solver.CapsuleSegment(poses, id, out Vector3 a, out Vector3 b, out float r);
                return SagittalGap(barCenter, a, b, r, DeadliftSetupSolver.AnchorBarRadius);
            }
            return new Gaps { LeftShin = G("left_shank"), RightShin = G("right_shank"), LeftThigh = G("left_thigh"), RightThigh = G("right_thigh") };
        }

        /// <summary>World core segment and radius of a live capsule collider.</summary>
        public static void LiveCapsule(CapsuleCollider c, out Vector3 a, out Vector3 b, out float radius)
        {
            Transform t = c.transform;
            Vector3 axisLocal = c.direction == 0 ? Vector3.right : c.direction == 1 ? Vector3.up : Vector3.forward;
            Vector3 s = t.lossyScale;
            float axisScale = Mathf.Abs(c.direction == 0 ? s.x : c.direction == 1 ? s.y : s.z);
            float radialScale = c.direction == 0 ? Mathf.Max(Mathf.Abs(s.y), Mathf.Abs(s.z))
                : c.direction == 1 ? Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)) : Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y));
            radius = c.radius * radialScale;
            float half = Mathf.Max(0f, 0.5f * c.height * axisScale - radius);
            Vector3 center = t.TransformPoint(c.center);
            Vector3 axis = t.TransformDirection(axisLocal).normalized;
            a = center - axis * half;
            b = center + axis * half;
        }

        /// <summary>The bar's shaft capsule (root collider along the bar's X).</summary>
        public static CapsuleCollider Shaft(Rigidbody bar)
        {
            foreach (CapsuleCollider c in bar.GetComponents<CapsuleCollider>())
                if (c.direction == 0)
                    return c;
            return null;
        }

        /// <summary>Realized gaps from the live leg colliders and the live shaft (sagittal definition).</summary>
        public static Gaps Realized(PhysicalAthleteRig rig, Rigidbody bar) => Realized(rig, bar, out _);

        /// <summary>Realized gaps; <paramref name="gaps3D"/> is the 3D shaft-capsule surface distance per segment (check).</summary>
        public static Gaps Realized(PhysicalAthleteRig rig, Rigidbody bar, out Gaps gaps3D)
        {
            CapsuleCollider shaft = Shaft(bar);
            LiveCapsule(shaft, out Vector3 s0, out Vector3 s1, out float sr);
            Vector3 barCenter = 0.5f * (s0 + s1);
            var sag = new Gaps();
            var g3 = new Gaps();
            void One(string id, out float sagittal, out float threeD)
            {
                LiveCapsule((CapsuleCollider)rig.Segments[id].Collider, out Vector3 a, out Vector3 b, out float r);
                sagittal = SagittalGap(barCenter, a, b, r, sr);
                threeD = DeadliftSetupSolver.DistanceSegmentSegment(s0, s1, a, b) - r - sr;
            }
            One("left_shank", out sag.LeftShin, out g3.LeftShin);
            One("right_shank", out sag.RightShin, out g3.RightShin);
            One("left_thigh", out sag.LeftThigh, out g3.LeftThigh);
            One("right_thigh", out sag.RightThigh, out g3.RightThigh);
            gaps3D = g3;
            return sag;
        }
    }
}
