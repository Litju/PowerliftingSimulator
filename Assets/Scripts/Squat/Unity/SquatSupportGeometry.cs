using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Diagnostic geometry for the same plantar contact points used by the
    /// balance observer. It does not participate in control or failure truth.
    ///
    /// Points are projected to the support plane as (ML = world x, AP = world z).
    /// Margins are exact signed Euclidean distances to the region boundary:
    /// positive inside, negative outside. The AP-only margin reproduces the
    /// production capture-margin proxy, which ignores ML extent.
    /// </summary>
    public static class SquatSupportGeometry
    {
        public readonly struct Measurement
        {
            public Measurement(
                int contactCount,
                int hullPointCount,
                float aabbAreaM2,
                float hullAreaM2,
                bool aabbContainsQuery,
                bool hullContainsQuery,
                float hullSignedMarginM,
                float aabbSignedMarginM,
                float aabbApSignedMarginM)
            {
                ContactCount = contactCount;
                HullPointCount = hullPointCount;
                AabbAreaM2 = aabbAreaM2;
                HullAreaM2 = hullAreaM2;
                AabbContainsQuery = aabbContainsQuery;
                HullContainsQuery = hullContainsQuery;
                HullSignedMarginM = hullSignedMarginM;
                AabbSignedMarginM = aabbSignedMarginM;
                AabbApSignedMarginM = aabbApSignedMarginM;
            }

            public int ContactCount { get; }
            public int HullPointCount { get; }
            public float AabbAreaM2 { get; }
            public float HullAreaM2 { get; }
            public bool AabbContainsQuery { get; }
            public bool HullContainsQuery { get; }
            public float HullSignedMarginM { get; }
            public float AabbSignedMarginM { get; }
            public float AabbApSignedMarginM { get; }
        }

        public static Measurement Measure(IReadOnlyList<Vector3> contacts, Vector2 queryMlAp)
        {
            if (contacts == null)
                throw new ArgumentNullException(nameof(contacts));

            var points = new List<Vector2>(contacts.Count);
            for (int index = 0; index < contacts.Count; index++)
            {
                Vector3 point = contacts[index];
                if (float.IsFinite(point.x) && float.IsFinite(point.z))
                    points.Add(new Vector2(point.x, point.z));
            }

            if (points.Count == 0 || !float.IsFinite(queryMlAp.x) || !float.IsFinite(queryMlAp.y))
                return new Measurement(points.Count, 0, float.NaN, float.NaN, false, false, float.NaN, float.NaN, float.NaN);

            points.Sort(ComparePoints);
            RemoveDuplicatePoints(points);

            float minX = points[0].x;
            float maxX = points[0].x;
            float minZ = points[0].y;
            float maxZ = points[0].y;
            for (int index = 1; index < points.Count; index++)
            {
                Vector2 point = points[index];
                minX = Mathf.Min(minX, point.x);
                maxX = Mathf.Max(maxX, point.x);
                minZ = Mathf.Min(minZ, point.y);
                maxZ = Mathf.Max(maxZ, point.y);
            }

            List<Vector2> hull = BuildHull(points);
            float hullArea = PolygonArea(hull);
            bool aabbContains = queryMlAp.x >= minX - 1e-6f && queryMlAp.x <= maxX + 1e-6f &&
                queryMlAp.y >= minZ - 1e-6f && queryMlAp.y <= maxZ + 1e-6f;
            bool hullContains = hull.Count >= 3 && ContainsConvexPolygon(hull, queryMlAp);
            float hullMargin = SignedDistanceToHull(hull, queryMlAp, hullContains);
            float aabbMargin = SignedDistanceToRectangle(minX, maxX, minZ, maxZ, queryMlAp);
            float aabbApMargin = Mathf.Min(queryMlAp.y - minZ, maxZ - queryMlAp.y);

            return new Measurement(
                points.Count,
                hull.Count,
                Mathf.Max(0f, (maxX - minX) * (maxZ - minZ)),
                hullArea,
                aabbContains,
                hullContains,
                hullMargin,
                aabbMargin,
                aabbApMargin);
        }

        private static int ComparePoints(Vector2 left, Vector2 right)
        {
            int x = left.x.CompareTo(right.x);
            return x != 0 ? x : left.y.CompareTo(right.y);
        }

        private static void RemoveDuplicatePoints(List<Vector2> points)
        {
            for (int index = points.Count - 1; index > 0; index--)
            {
                if ((points[index] - points[index - 1]).sqrMagnitude <= 1e-12f)
                    points.RemoveAt(index);
            }
        }

        private static List<Vector2> BuildHull(List<Vector2> points)
        {
            if (points.Count <= 2)
                return new List<Vector2>(points);

            var hull = new List<Vector2>(points.Count * 2);
            for (int index = 0; index < points.Count; index++)
            {
                Vector2 point = points[index];
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }

            int lowerCount = hull.Count;
            for (int index = points.Count - 2; index >= 0; index--)
            {
                Vector2 point = points[index];
                while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], point) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(point);
            }

            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static float Cross(Vector2 a, Vector2 b, Vector2 c) =>
            (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

        private static float PolygonArea(List<Vector2> polygon)
        {
            if (polygon.Count < 3)
                return 0f;

            float twiceArea = 0f;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 current = polygon[index];
                Vector2 next = polygon[(index + 1) % polygon.Count];
                twiceArea += current.x * next.y - next.x * current.y;
            }
            return Mathf.Abs(twiceArea) * 0.5f;
        }

        private static bool ContainsConvexPolygon(List<Vector2> polygon, Vector2 point)
        {
            for (int index = 0; index < polygon.Count; index++)
            {
                if (Cross(polygon[index], polygon[(index + 1) % polygon.Count], point) < -1e-6f)
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Exact signed distance to a counter-clockwise convex hull. A hull with
        /// fewer than three vertices has no interior, so the query is outside
        /// by its distance to the point or segment.
        /// </summary>
        private static float SignedDistanceToHull(List<Vector2> hull, Vector2 point, bool inside)
        {
            if (hull.Count == 0)
                return float.NaN;
            if (hull.Count == 1)
                return -Vector2.Distance(hull[0], point);

            float nearest = float.PositiveInfinity;
            int edges = hull.Count == 2 ? 1 : hull.Count;
            for (int index = 0; index < edges; index++)
                nearest = Mathf.Min(nearest, DistanceToSegment(hull[index], hull[(index + 1) % hull.Count], point));
            return inside && hull.Count >= 3 ? nearest : -nearest;
        }

        private static float DistanceToSegment(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 ab = b - a;
            float lengthSquared = ab.sqrMagnitude;
            if (lengthSquared <= 1e-12f)
                return Vector2.Distance(a, point);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSquared);
            return Vector2.Distance(a + t * ab, point);
        }

        private static float SignedDistanceToRectangle(float minX, float maxX, float minZ, float maxZ, Vector2 point)
        {
            float dx = Mathf.Max(minX - point.x, point.x - maxX);
            float dz = Mathf.Max(minZ - point.y, point.y - maxZ);
            if (dx <= 0f && dz <= 0f)
                return -Mathf.Max(dx, dz);
            float outsideX = Mathf.Max(dx, 0f);
            float outsideZ = Mathf.Max(dz, 0f);
            return -Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
        }
    }
}
