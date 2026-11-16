using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 Gate 6: the moving-phase support observation. It replaces the
    /// fixed initial-COM displacement of the quiet-standing governor with a
    /// physically meaningful task-space quantity: the signed distance of the
    /// athlete+bar capture point
    ///   xi = c + v / omega0,  omega0 = sqrt(g / h)
    /// (c, v the combined COM position and velocity, h its height above the
    /// soles; Hof 2005) to the realized support polygon, the convex hull of
    /// the sole rectangles of the feet that carried a normal load on the last
    /// completed tick. Positive inside. A foot that carries nothing contributes
    /// nothing. It reads bodies and the foot pressure sensors only.
    /// </summary>
    public sealed class DeadliftSupportMonitor
    {
        private readonly PhysicalAthleteRig _rig;
        private readonly Rigidbody _bar;
        private readonly DeadliftFootPressureSensor _left, _right;
        private readonly BoxCollider _leftBox, _rightBox;

        public DeadliftSupportMonitor(PhysicalAthleteRig rig, Rigidbody bar, Collider ground)
        {
            _rig = rig ?? throw new ArgumentNullException(nameof(rig));
            _bar = bar;
            _left = Sensor("left_foot", ground);
            _right = Sensor("right_foot", ground);
            _leftBox = (BoxCollider)rig.Segments["left_foot"].Collider;
            _rightBox = (BoxCollider)rig.Segments["right_foot"].Collider;
        }

        private DeadliftFootPressureSensor Sensor(string id, Collider ground)
        {
            Rigidbody body = _rig.Segments[id].Body;
            DeadliftFootPressureSensor s = body.GetComponent<DeadliftFootPressureSensor>() ?? body.gameObject.AddComponent<DeadliftFootPressureSensor>();
            if (s.Ground == null) s.Ground = ground;
            return s;
        }

        /// <summary>A foot is load-bearing when its last completed tick carried more than this normal force.</summary>
        public float LoadedFootN { get; set; } = 50f;

        public Vector3 SystemCom { get; private set; }
        public Vector3 SystemVelocity { get; private set; }
        public Vector2 CapturePoint { get; private set; }
        public float MarginM { get; private set; } = float.NaN;
        public int LoadedFeet { get; private set; }

        /// <summary>Recomputes from the current bodies and the last completed contact tick. Returns the margin (m).</summary>
        public float Update()
        {
            float m = 0f;
            Vector3 c = Vector3.zero, p = Vector3.zero;
            foreach (PhysicalAthleteRig.SegmentRuntime s in _rig.Segments.Values)
            {
                m += s.Body.mass;
                c += s.Body.mass * s.Body.worldCenterOfMass;
                p += s.Body.mass * s.Body.linearVelocity;
            }
            if (_bar != null)
            {
                m += _bar.mass;
                c += _bar.mass * _bar.worldCenterOfMass;
                p += _bar.mass * _bar.linearVelocity;
            }
            c /= m;
            Vector3 v = p / m;
            SystemCom = c;
            SystemVelocity = v;

            var points = new List<Vector2>(8);
            float soleY = float.MaxValue;
            LoadedFeet = 0;
            double dt = PowerliftingSimulator.Foundation.SimulationConstants.FixedDeltaTimeSeconds;
            foreach ((DeadliftFootPressureSensor sensor, BoxCollider box) in new[] { (_left, _leftBox), (_right, _rightBox) })
            {
                Transform t = box.transform;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 w = t.TransformPoint(box.center + new Vector3(((i & 1) == 0 ? -1f : 1f) * box.size.x * 0.5f, -box.size.y * 0.5f, ((i & 2) == 0 ? -1f : 1f) * box.size.z * 0.5f));
                    soleY = Mathf.Min(soleY, w.y);
                    if (sensor.NormalImpulse / dt > LoadedFootN)
                        points.Add(new Vector2(w.x, w.z));
                }
                if (sensor.NormalImpulse / dt > LoadedFootN)
                    LoadedFeet++;
            }
            float h = Mathf.Max(0.05f, c.y - soleY);
            float omega = Mathf.Sqrt(-Physics.gravity.y / h);
            CapturePoint = new Vector2(c.x + v.x / omega, c.z + v.z / omega);
            if (points.Count < 3)
            {
                MarginM = float.NegativeInfinity;
                return MarginM;
            }
            MarginM = DeadliftSupportGeometry.SignedDistance(DeadliftSupportGeometry.Hull(points), CapturePoint);
            return MarginM;
        }
    }
}
