using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 C1 observer on one Rigidbody: the contact wrench the solver
    /// applied to this body over the last completed authoritative tick (both
    /// PhysX substeps), split by the partner collider's class. For every
    /// contact it accumulates the impulse J (N s) and its moment about the
    /// world origin p x J (N m s); the moment about any point O is then
    /// M_O = sum(p x J) - O x sum(J). Each impulse is oriented so that its
    /// normal part pushes this body away from the partner (Unity's contact
    /// normal points from the other collider into this one; for the platform
    /// the normal is additionally forced to +Y). It only reads Collision data.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeadliftWrenchSensor : MonoBehaviour
    {
        public struct Wrench
        {
            public Vector3 Impulse;        // N s, on this body
            public Vector3 OriginMoment;   // N m s, sum p x J about world origin
            public double NormalImpulse;   // N s, sum of positive normal parts
            public int Points;

            public Vector3 MomentAbout(Vector3 o) => OriginMoment - Vector3.Cross(o, Impulse);

            public static Wrench operator +(Wrench a, Wrench b) => new Wrench
            {
                Impulse = a.Impulse + b.Impulse, OriginMoment = a.OriginMoment + b.OriginMoment,
                NormalImpulse = a.NormalImpulse + b.NormalImpulse, Points = a.Points + b.Points
            };
        }

        private static readonly ContactPoint[] s_buffer = new ContactPoint[128];
        private readonly Dictionary<string, Wrench> _pending = new Dictionary<string, Wrench>();
        private readonly Dictionary<string, Wrench> _last = new Dictionary<string, Wrench>();

        /// <summary>Maps a contact to a partner class key (e.g. "floor", "plateL", "bar", "athlete").</summary>
        public System.Func<ContactPoint, string> Classify { get; set; }

        public Collider Ground { get; set; }

        public Wrench Get(string key) => _last.TryGetValue(key, out Wrench w) ? w : default;

        public IEnumerable<KeyValuePair<string, Wrench>> All => _last;

        public void CompleteTick()
        {
            _last.Clear();
            foreach (KeyValuePair<string, Wrench> e in _pending)
                _last[e.Key] = e.Value;
            _pending.Clear();
        }

        private void OnCollisionEnter(Collision c) => Accumulate(c);
        private void OnCollisionStay(Collision c) => Accumulate(c);

        private void Accumulate(Collision collision)
        {
            int count = collision.GetContacts(s_buffer);
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = s_buffer[i];
                string key = Classify != null ? Classify(contact) : "other";
                if (key == null)
                    continue;
                Vector3 normal = contact.normal;
                if (Ground != null && contact.otherCollider == Ground && Vector3.Dot(normal, Vector3.up) < 0f)
                    normal = -normal;
                Vector3 impulse = contact.impulse;
                if (Vector3.Dot(impulse, normal) < 0f)
                    impulse = -impulse;
                double n = Vector3.Dot(impulse, normal);
                _pending.TryGetValue(key, out Wrench w);
                w.Impulse += impulse;
                w.OriginMoment += Vector3.Cross(contact.point, impulse);
                if (n > 0d)
                    w.NormalImpulse += n;
                w.Points++;
                _pending[key] = w;
            }
        }
    }
}
