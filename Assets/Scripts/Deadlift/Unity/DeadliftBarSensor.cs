using System;
using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 production observer on the barbell body: the normal load the
    /// platform puts on each plate, and the contact the athlete's legs make
    /// with the bar, split into shins and thighs and measured per contact in
    /// direction and location, over the last completed authoritative tick
    /// (both PhysX substeps). Read-only.
    ///
    /// IPF 4.3.1(4) judges "supporting the bar on the thighs", not leg
    /// contact: the setup deliberately starts the shaft against the shins and
    /// the rulebook permits the bar to edge up the thighs without support.
    /// The sensor therefore reports where on the leg each normal impulse
    /// arrived and in which direction (Unity applies <see cref="ContactPoint.impulse"/>
    /// to this body along the contact normal; a thigh carrying weight pushes
    /// the bar up, so its upward component is the assistance). The engine does
    /// not expose tangential (friction) impulses; the exact vertical
    /// assistance is instead closed from the bar's momentum by the runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeadliftBarSensor : MonoBehaviour
    {
        private static readonly ContactPoint[] s_buffer = new ContactPoint[64];
        private double _left, _right, _shins, _thighs, _thighUp, _thighUpAbsMax, _thighHighest, _thighDeepest;

        public Collider Ground { get; set; }
        public Collider LeftPlate { get; set; }
        public Collider RightPlate { get; set; }
        public HashSet<Collider> Legs { get; } = new HashSet<Collider>();
        /// <summary>Shank colliders (legal contact surface; not judged).</summary>
        public HashSet<Collider> Shins { get; } = new HashSet<Collider>();
        /// <summary>Thigh colliders (judged surface for IPF 4.3.1(4)).</summary>
        public HashSet<Collider> Thighs { get; } = new HashSet<Collider>();

        public double LeftNormalImpulse { get; private set; }
        public double RightNormalImpulse { get; private set; }
        public double LegNormalImpulse { get; private set; }
        /// <summary>Normal impulse from the shins only (N s per tick; legal contact, telemetry).</summary>
        public double ShinNormalImpulse { get; private set; }
        /// <summary>Normal impulse from the thighs only (N s per tick).</summary>
        public double ThighNormalImpulse { get; private set; }
        /// <summary>Vertical component of the thigh contact impulses on the bar (N s per tick; positive = the thighs push the bar up).</summary>
        public double ThighUpwardImpulse { get; private set; }
        /// <summary>Highest thigh contact point of the tick (m, world Y); NaN without thigh contact.</summary>
        public double ThighContactY { get; private set; }

        public void CompleteTick()
        {
            LeftNormalImpulse = _left;
            RightNormalImpulse = _right;
            LegNormalImpulse = _shins + _thighs;
            ShinNormalImpulse = _shins;
            ThighNormalImpulse = _thighs;
            ThighUpwardImpulse = _thighUp;
            ThighContactY = _thighs > 0d ? _thighHighest : double.NaN;
            _left = _right = _shins = _thighs = _thighUp = 0d;
            _thighUpAbsMax = 0d;
            _thighHighest = _thighDeepest = double.NaN;
        }

        private void OnCollisionEnter(Collision c) => Accumulate(c);
        private void OnCollisionStay(Collision c) => Accumulate(c);

        private void Accumulate(Collision collision)
        {
            int count = collision.GetContacts(s_buffer);
            for (int i = 0; i < count; i++)
            {
                ContactPoint c = s_buffer[i];
                double n = Mathf.Abs(Vector3.Dot(c.impulse, c.normal));
                if (c.otherCollider == Ground)
                {
                    if (c.thisCollider == LeftPlate) _left += n;
                    else if (c.thisCollider == RightPlate) _right += n;
                    continue;
                }
                if (!Legs.Contains(c.otherCollider))
                    continue;
                // Direction and location per contact: the normal's vertical
                // component separates weight-carrying support from brushing,
                // and the contact height separates a thigh rest from a shin drag.
                double up = Vector3.Dot(c.impulse, Vector3.up);
                if (Shins.Contains(c.otherCollider))
                    _shins += n;
                else if (Thighs.Contains(c.otherCollider))
                {
                    _thighs += n;
                    _thighUp += up;
                    _thighUpAbsMax = Math.Max(_thighUpAbsMax, Math.Abs(up));
                    _thighHighest = double.IsNaN(_thighHighest) ? c.point.y : Math.Max(_thighHighest, c.point.y);
                    _thighDeepest = double.IsNaN(_thighDeepest) ? c.point.y : Math.Min(_thighDeepest, c.point.y);
                }
                else
                    _shins += n;
            }
        }

        /// <summary>Largest single-contact vertical thigh impulse of the last tick (N s; magnitude check).</summary>
        public double ThighUpwardImpulseMax => _thighUpAbsMax;
        /// <summary>Lowest thigh contact point of the last tick (m, world Y); NaN without thigh contact.</summary>
        public double ThighContactDeepestY => _thighDeepest;
    }
}
