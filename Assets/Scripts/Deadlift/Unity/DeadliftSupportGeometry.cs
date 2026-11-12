using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 static support geometry on the platform plane (world X, Z).
    ///
    /// Potential support region: the convex hull of the sole corners that are in
    /// contact. It is the region where a centre of pressure (COP) can lie for
    /// non-negative normal reactions (Winter, Biomechanics and Motor Control,
    /// ch. 5; Hibbeler, Dynamics, equilibrium of a rigid body). It is not the
    /// active patch, which only realized contacts can give.
    ///
    /// Gripped grounded bar (Lynch and Park, Modern Robotics, ch. 12 contact
    /// wrenches): the athlete receives foot reactions R and a hand reaction from
    /// the bar. At rest the bar is free to roll on its plates, so the static hand
    /// reaction is vertical. With H the downward pull of the bar on the hands
    /// (0 &lt;= H &lt;= m_bar g; above that the bar leaves the floor):
    ///   vertical:  R = W_a + H
    ///   moment:    R * cop = W_a * com + H * bar   (in X and Z)
    /// so cop(H) = (W_a com + H bar) / (W_a + H), which moves monotonically from
    /// the athlete COM (H = 0) to the athlete+bar COM (H = m_bar g).
    /// </summary>
    public static class DeadliftSupportGeometry
    {
        /// <summary>Counter-clockwise convex hull (Andrew monotone chain) of XZ points.</summary>
        public static List<Vector2> Hull(IEnumerable<Vector2> points)
        {
            var p = new List<Vector2>(points);
            p.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            if (p.Count < 3)
                return p;
            var h = new List<Vector2>(2 * p.Count);
            for (int pass = 0; pass < 2; pass++)
            {
                int start = h.Count;
                for (int k = 0; k < p.Count; k++)
                {
                    Vector2 q = pass == 0 ? p[k] : p[p.Count - 1 - k];
                    while (h.Count >= start + 2 && Cross(h[h.Count - 2], h[h.Count - 1], q) <= 0f)
                        h.RemoveAt(h.Count - 1);
                    h.Add(q);
                }
                h.RemoveAt(h.Count - 1);
            }
            return h;
        }

        private static float Cross(Vector2 o, Vector2 a, Vector2 b) => (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);

        /// <summary>
        /// Signed distance (m) from a point to a convex CCW hull boundary:
        /// positive inside (margin), negative outside (violation).
        /// </summary>
        public static float SignedDistance(List<Vector2> hull, Vector2 point)
        {
            if (hull.Count < 3)
                return float.NegativeInfinity;
            bool inside = true;
            float nearest = float.MaxValue;
            for (int i = 0; i < hull.Count; i++)
            {
                Vector2 a = hull[i], b = hull[(i + 1) % hull.Count];
                if (Cross(a, b, point) < 0f)
                    inside = false;
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / Mathf.Max(1e-12f, ab.sqrMagnitude));
                nearest = Mathf.Min(nearest, Vector2.Distance(point, a + t * ab));
            }
            return inside ? nearest : -nearest;
        }

        /// <summary>Foot COP (XZ) when the bar pulls down on the hands with <paramref name="handPullN"/>.</summary>
        public static Vector2 CopUnderHandPull(Vector3 athleteCom, float athleteWeightN, Vector3 barPoint, float handPullN)
        {
            float r = athleteWeightN + handPullN;
            return new Vector2(
                (athleteWeightN * athleteCom.x + handPullN * barPoint.x) / r,
                (athleteWeightN * athleteCom.z + handPullN * barPoint.z) / r);
        }

        /// <summary>
        /// Best achievable COP margin (m) over the admissible hand pull, and the
        /// pull at which it occurs. Sampled densely along the monotone COP path;
        /// the path is a straight segment in XZ, so 65 samples resolve it well
        /// below a millimetre for any pose of this rig.
        /// </summary>
        public static float BestCopMargin(List<Vector2> hull, Vector3 athleteCom, float athleteWeightN, Vector3 barPoint,
            float maxHandPullN, out float bestPullN)
        {
            float best = float.NegativeInfinity;
            bestPullN = 0f;
            const int samples = 64;
            for (int i = 0; i <= samples; i++)
            {
                float h = maxHandPullN * i / samples;
                float m = SignedDistance(hull, CopUnderHandPull(athleteCom, athleteWeightN, barPoint, h));
                if (m > best)
                {
                    best = m;
                    bestPullN = h;
                }
            }
            return best;
        }

        /// <summary>Range of hand pull (N) for which the COP has at least <paramref name="margin"/> inside the hull; NaN when empty.</summary>
        public static Vector2 AdmissiblePullRange(List<Vector2> hull, Vector3 athleteCom, float athleteWeightN, Vector3 barPoint,
            float maxHandPullN, float margin)
        {
            float lo = float.NaN, hi = float.NaN;
            const int samples = 256;
            for (int i = 0; i <= samples; i++)
            {
                float h = maxHandPullN * i / samples;
                if (SignedDistance(hull, CopUnderHandPull(athleteCom, athleteWeightN, barPoint, h)) >= margin)
                {
                    if (float.IsNaN(lo)) lo = h;
                    hi = h;
                }
            }
            return new Vector2(lo, hi);
        }
    }
}
