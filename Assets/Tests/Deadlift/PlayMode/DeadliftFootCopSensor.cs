using System.Collections.Generic;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A2 observer on one foot body: the realized platform contact
    /// of the last completed tick. Centre of pressure = sum(normal impulse x
    /// contact point) / sum(normal impulse) (Winter, ch. 5); active patch = the
    /// XZ bounding box of the contact points that carried a normal impulse.
    /// It only reads Collision data; it never writes a force, pose or velocity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeadliftFootCopSensor : MonoBehaviour
    {
        private static readonly ContactPoint[] s_buffer = new ContactPoint[64];
        private double _pendingN;
        private Vector3 _pendingMoment, _pendingImpulse;
        private int _pendingPoints;
        private Vector2 _pMin = new Vector2(float.MaxValue, float.MaxValue), _pMax = new Vector2(float.MinValue, float.MinValue);

        private readonly List<Vector2> _pendingPatch = new List<Vector2>();
        public readonly List<Vector2> ActivePatch = new List<Vector2>();

        public Collider Ground { get; set; }
        public double NormalImpulse { get; private set; }
        public Vector3 Impulse { get; private set; }
        public Vector3 Cop { get; private set; }
        public int Points { get; private set; }
        public Vector2 PatchMin { get; private set; }
        public Vector2 PatchMax { get; private set; }

        public void CompleteTick()
        {
            ActivePatch.Clear();
            ActivePatch.AddRange(_pendingPatch);
            _pendingPatch.Clear();
            NormalImpulse = _pendingN;
            Impulse = _pendingImpulse;
            Cop = _pendingN > 1e-9 ? _pendingMoment / (float)_pendingN : new Vector3(float.NaN, float.NaN, float.NaN);
            Points = _pendingPoints;
            PatchMin = _pMin;
            PatchMax = _pMax;
            _pendingN = 0d;
            _pendingMoment = _pendingImpulse = Vector3.zero;
            _pendingPoints = 0;
            _pMin = new Vector2(float.MaxValue, float.MaxValue);
            _pMax = new Vector2(float.MinValue, float.MinValue);
        }

        private void OnCollisionEnter(Collision c) => Accumulate(c);
        private void OnCollisionStay(Collision c) => Accumulate(c);

        private void Accumulate(Collision collision)
        {
            if (Ground == null || collision.collider != Ground)
                return;
            int count = collision.GetContacts(s_buffer);
            for (int i = 0; i < count; i++)
            {
                ContactPoint contact = s_buffer[i];
                Vector3 normal = Vector3.Dot(contact.normal, Vector3.up) < 0f ? -contact.normal : contact.normal;
                Vector3 impulse = Vector3.Dot(contact.impulse, normal) < 0f ? -contact.impulse : contact.impulse;
                float n = Vector3.Dot(impulse, normal);
                if (n <= 0f)
                    continue;
                _pendingN += n;
                _pendingImpulse += impulse;
                _pendingMoment += n * contact.point;
                _pendingPoints++;
                _pendingPatch.Add(new Vector2(contact.point.x, contact.point.z));
                _pMin = Vector2.Min(_pMin, new Vector2(contact.point.x, contact.point.z));
                _pMax = Vector2.Max(_pMax, new Vector2(contact.point.x, contact.point.z));
            }
        }
    }
}
