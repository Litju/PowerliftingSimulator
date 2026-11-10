using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>What a collider physically is, for contact accounting.</summary>
    public enum BenchSurface : byte
    {
        Unknown,
        Floor,
        BenchPad,
        BenchFrame,
        BarRest,
        Safety,
        Upright,
        Bar,
        AthleteHead,
        AthleteThorax,
        AthleteAbdomen,
        AthletePelvis,
        AthleteArm,
        AthleteLeg,
        AthleteFoot
    }

    /// <summary>
    /// Explicit collider role registry. A contact is only attributed to a
    /// surface that was registered by its owner; nothing is inferred from
    /// names or proximity.
    /// </summary>
    public static class BenchSurfaceRegistry
    {
        private static readonly Dictionary<int, BenchSurface> s_roles = new Dictionary<int, BenchSurface>();

        public static void Register(Collider collider, BenchSurface surface)
        {
            if (collider == null)
                throw new ArgumentNullException(nameof(collider));
            s_roles[collider.GetInstanceID()] = surface;
        }

        public static BenchSurface Resolve(Collider collider) =>
            collider != null && s_roles.TryGetValue(collider.GetInstanceID(), out BenchSurface surface)
                ? surface
                : BenchSurface.Unknown;

        public static void Clear() => s_roles.Clear();
    }

    /// <summary>Geometric contact observation, independent of load.</summary>
    public static class BenchGeometry
    {
        /// <summary>Search range of <see cref="VerticalClearance"/>; a clearance at or above it reads as this value.</summary>
        public const float ClearanceSearchM = 0.05f;

        /// <summary>
        /// Rule-level contact measurement: the smallest downward shift (world
        /// -Y) that brings <paramref name="region"/> into overlap with the
        /// horizontal <paramref name="surface"/> below it (bench pad top,
        /// platform), found by bisection on exact penetration queries to
        /// 0.1 mm. 0 when already touching. Independent of load.
        /// </summary>
        public static float VerticalClearance(Collider region, Collider surface)
        {
            if (region == null || surface == null)
                return float.NaN;
            Vector3 p = region.transform.position;
            Quaternion q = region.transform.rotation;
            bool Overlaps(float d) => Physics.ComputePenetration(region, p + Vector3.down * d, q,
                surface, surface.transform.position, surface.transform.rotation, out _, out _);
            if (Overlaps(0f))
                return 0f;
            if (!Overlaps(ClearanceSearchM))
                return ClearanceSearchM;
            float lo = 0f, hi = ClearanceSearchM;
            while (hi - lo > 0.0001f)
            {
                float mid = 0.5f * (lo + hi);
                if (Overlaps(mid))
                    hi = mid;
                else
                    lo = mid;
            }
            return hi;
        }

        /// <summary>
        /// Approximate separation of two convex colliders by alternating
        /// closest-point projection. Converges slowly for nearly parallel
        /// faces (it overestimates there), so it is used only to confirm that
        /// pairs known to be far apart read far apart - never for contact.
        /// </summary>
        public static float Gap(Collider a, Collider b)
        {
            if (a == null || b == null)
                return float.NaN;
            if (Physics.ComputePenetration(a, a.transform.position, a.transform.rotation,
                    b, b.transform.position, b.transform.rotation, out _, out _))
                return 0f;
            Vector3 p = a.bounds.center;
            Vector3 q = b.ClosestPoint(p);
            for (int i = 0; i < 12; i++)
            {
                p = a.ClosestPoint(q);
                Vector3 next = b.ClosestPoint(p);
                if ((next - q).sqrMagnitude < 1e-12f)
                {
                    q = next;
                    break;
                }
                q = next;
            }
            return Vector3.Distance(p, q);
        }
    }

    /// <summary>
    /// Accumulates the solver's normal impulses on one Rigidbody, per surface
    /// it touches, across every substep of a tick. The tick-averaged force is
    /// impulse divided by tick length. Normal impulse is taken as the
    /// magnitude of each contact point's impulse along its contact normal, so
    /// it is independent of which body Unity reports the normal toward.
    ///
    /// Read only through <see cref="CompleteTick"/>, which the post-physics
    /// observer calls once per tick: a reader never sees a partially
    /// accumulated step.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BenchContactSensor : MonoBehaviour
    {
        private const int SurfaceCount = 15;
        private static readonly ContactPoint[] s_buffer = new ContactPoint[32];

        private readonly double[] _pendingNormal = new double[SurfaceCount];
        private readonly Vector3[] _pendingImpulse = new Vector3[SurfaceCount];
        private readonly int[] _pendingPoints = new int[SurfaceCount];
        private readonly double[] _completedForce = new double[SurfaceCount];
        private readonly Vector3[] _completedForceVector = new Vector3[SurfaceCount];
        private readonly int[] _completedPoints = new int[SurfaceCount];

        public BenchSurface OwnSurface { get; set; }

        /// <summary>Tick-averaged normal force from contacts with <paramref name="surface"/>, newtons.</summary>
        public double NormalForce(BenchSurface surface) => _completedForce[(int)surface];

        /// <summary>
        /// Tick-averaged contact force vector applied to this body by
        /// <paramref name="surface"/>, world frame. The sign is fixed so the
        /// normal part pushes this body away from the surface.
        /// </summary>
        public Vector3 ForceVector(BenchSurface surface) => _completedForceVector[(int)surface];

        public int ContactPointCount(BenchSurface surface) => _completedPoints[(int)surface];

        public void CompleteTick(double tickSeconds)
        {
            for (int i = 0; i < SurfaceCount; i++)
            {
                _completedForce[i] = _pendingNormal[i] / tickSeconds;
                _completedForceVector[i] = _pendingImpulse[i] / (float)tickSeconds;
                _completedPoints[i] = _pendingPoints[i];
                _pendingNormal[i] = 0d;
                _pendingImpulse[i] = Vector3.zero;
                _pendingPoints[i] = 0;
            }
        }

        public void ClearAll()
        {
            for (int i = 0; i < SurfaceCount; i++)
            {
                _pendingNormal[i] = 0d;
                _pendingImpulse[i] = Vector3.zero;
                _pendingPoints[i] = 0;
                _completedForce[i] = 0d;
                _completedForceVector[i] = Vector3.zero;
                _completedPoints[i] = 0;
            }
        }

        private void OnCollisionEnter(Collision collision) => Accumulate(collision);

        private void OnCollisionStay(Collision collision) => Accumulate(collision);

        private void Accumulate(Collision collision)
        {
            BenchSurface other = BenchSurfaceRegistry.Resolve(collision.collider);
            int count = collision.GetContacts(s_buffer);
            Rigidbody self = GetComponent<Rigidbody>();
            Vector3 selfCenter = self != null ? self.worldCenterOfMass : transform.position;
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = s_buffer[i];
                float normalImpulse = Mathf.Abs(Vector3.Dot(contact.impulse, contact.normal));
                if (normalImpulse <= 0f)
                    continue;
                // Orient the normal so it pushes this body out of the other
                // surface: from the contact point toward this body's side.
                Vector3 n = contact.normal;
                if (contact.thisCollider != null && contact.otherCollider != null &&
                    Vector3.Dot(n, selfCenter - contact.point) < 0f)
                    n = -n;
                Vector3 impulse = contact.impulse;
                if (Vector3.Dot(impulse, n) < 0f)
                    impulse = -impulse;
                _pendingNormal[(int)other] += normalImpulse;
                _pendingImpulse[(int)other] += impulse;
                _pendingPoints[(int)other]++;
            }
        }
    }
}
