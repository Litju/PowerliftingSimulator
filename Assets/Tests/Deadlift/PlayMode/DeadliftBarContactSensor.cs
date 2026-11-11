using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A1 observer: accumulates the solver's contact impulses on
    /// the barbell Rigidbody, split into the left plate collider, the right
    /// plate collider and every other bar collider, for contacts with the
    /// registered ground. It only reads <see cref="Collision"/> data; it never
    /// writes a force, a position or a velocity.
    ///
    /// Impulses of both PhysX substeps of one authoritative tick are summed;
    /// <see cref="CompleteTick"/> publishes and clears them. Each contact
    /// impulse is oriented so that its normal part pushes the bar away from
    /// the ground (+Y), which is unambiguous for a horizontal platform.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeadliftBarContactSensor : MonoBehaviour
    {
        private static readonly ContactPoint[] s_buffer = new ContactPoint[64];

        private readonly HashSet<string> _touchedGround = new HashSet<string>();
        private readonly HashSet<string> _touchedGroundPending = new HashSet<string>();
        private readonly List<string> _touchedGroundLastTick = new List<string>();

        private Vector3 _pendingLeft, _pendingRight, _pendingOther;
        private double _pendingLeftNormal, _pendingRightNormal;
        private int _pendingLeftPoints, _pendingRightPoints, _pendingOtherPoints;

        public Collider Ground { get; set; }
        public Collider LeftPlate { get; set; }
        public Collider RightPlate { get; set; }

        /// <summary>Contact impulse (N s) from the ground on the left plate over the last completed tick.</summary>
        public Vector3 LeftImpulse { get; private set; }
        public Vector3 RightImpulse { get; private set; }
        public Vector3 OtherImpulse { get; private set; }
        public double LeftNormalImpulse { get; private set; }
        public double RightNormalImpulse { get; private set; }
        public int LeftPoints { get; private set; }
        public int RightPoints { get; private set; }
        public int OtherPoints { get; private set; }
        public IReadOnlyCollection<string> TouchedGroundEver => _touchedGround;
        public IReadOnlyList<string> CollidersTouchingGroundLastTick => _touchedGroundLastTick;

        public void CompleteTick()
        {
            LeftImpulse = _pendingLeft;
            RightImpulse = _pendingRight;
            OtherImpulse = _pendingOther;
            LeftNormalImpulse = _pendingLeftNormal;
            RightNormalImpulse = _pendingRightNormal;
            LeftPoints = _pendingLeftPoints;
            RightPoints = _pendingRightPoints;
            OtherPoints = _pendingOtherPoints;
            _pendingLeft = _pendingRight = _pendingOther = Vector3.zero;
            _pendingLeftNormal = _pendingRightNormal = 0d;
            _pendingLeftPoints = _pendingRightPoints = _pendingOtherPoints = 0;
            _touchedGroundLastTick.Clear();
            _touchedGroundLastTick.AddRange(_touchedGroundPending);
            _touchedGroundPending.Clear();
        }

        private void OnCollisionEnter(Collision collision) => Accumulate(collision);

        private void OnCollisionStay(Collision collision) => Accumulate(collision);

        private void Accumulate(Collision collision)
        {
            if (Ground == null || collision.collider != Ground)
                return;
            int count = collision.GetContacts(s_buffer);
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = s_buffer[i];
                Vector3 impulse = contact.impulse;
                Vector3 normal = contact.normal;
                if (Vector3.Dot(normal, Vector3.up) < 0f)
                    normal = -normal;
                if (Vector3.Dot(impulse, normal) < 0f)
                    impulse = -impulse;
                double normalPart = Vector3.Dot(impulse, normal);
                Collider mine = contact.thisCollider;
                if (mine == LeftPlate)
                {
                    _pendingLeft += impulse;
                    _pendingLeftNormal += normalPart;
                    _pendingLeftPoints++;
                }
                else if (mine == RightPlate)
                {
                    _pendingRight += impulse;
                    _pendingRightNormal += normalPart;
                    _pendingRightPoints++;
                }
                else
                {
                    _pendingOther += impulse;
                    _pendingOtherPoints++;
                }
                if (mine != null && normalPart > 0d)
                {
                    _touchedGround.Add(mine.name);
                    _touchedGroundPending.Add(mine.name);
                }
            }
        }
    }
}
