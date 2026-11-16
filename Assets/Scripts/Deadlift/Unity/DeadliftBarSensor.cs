using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 production observer on the barbell body: the normal load the
    /// platform puts on each plate, and the normal load from the athlete's
    /// legs on the bar (thigh support, IPF 4.3.1(4)), over the last completed
    /// authoritative tick (both PhysX substeps). Read-only.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeadliftBarSensor : MonoBehaviour
    {
        private static readonly ContactPoint[] s_buffer = new ContactPoint[64];
        private double _left, _right, _legs;

        public Collider Ground { get; set; }
        public Collider LeftPlate { get; set; }
        public Collider RightPlate { get; set; }
        public HashSet<Collider> Legs { get; } = new HashSet<Collider>();

        public double LeftNormalImpulse { get; private set; }
        public double RightNormalImpulse { get; private set; }
        public double LegNormalImpulse { get; private set; }

        public void CompleteTick()
        {
            LeftNormalImpulse = _left;
            RightNormalImpulse = _right;
            LegNormalImpulse = _legs;
            _left = _right = _legs = 0d;
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
                }
                else if (Legs.Contains(c.otherCollider))
                {
                    _legs += n;
                }
            }
        }
    }
}
