using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 production observer on one foot body: the platform contact of
    /// the last completed authoritative tick (both PhysX substeps). Centre of
    /// pressure = sum(n_i p_i) / sum(n_i) over positive normal impulses
    /// (Winter ch. 5); the fore-aft (world Z) extent of the loaded points is
    /// kept as the realized support bound. Unity reports normal impulse only,
    /// so no friction is inferred here. Read-only: it never writes a force,
    /// pose or velocity.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeadliftFootPressureSensor : MonoBehaviour
    {
        private static readonly ContactPoint[] s_buffer = new ContactPoint[64];
        private double _n;
        private Vector3 _m;
        private float _zMin = float.MaxValue, _zMax = float.MinValue;
        private float _aMin = float.MaxValue, _aMax = float.MinValue;

        /// <summary>Horizontal axis along which the loaded extent is also reported (e.g. the sole's long axis).</summary>
        public Vector3 Axis { get; set; } = Vector3.forward;
        public float LoadedAxisMin { get; private set; } = float.NaN;
        public float LoadedAxisMax { get; private set; } = float.NaN;

        public Collider Ground { get; set; }
        public double NormalImpulse { get; private set; }
        public Vector3 Cop { get; private set; }
        public float LoadedZMin { get; private set; } = float.NaN;
        public float LoadedZMax { get; private set; } = float.NaN;

        public void CompleteTick()
        {
            NormalImpulse = _n;
            Cop = _n > 1e-9 ? _m / (float)_n : new Vector3(float.NaN, float.NaN, float.NaN);
            LoadedZMin = _n > 1e-9 ? _zMin : float.NaN;
            LoadedZMax = _n > 1e-9 ? _zMax : float.NaN;
            LoadedAxisMin = _n > 1e-9 ? _aMin : float.NaN;
            LoadedAxisMax = _n > 1e-9 ? _aMax : float.NaN;
            _aMin = float.MaxValue;
            _aMax = float.MinValue;
            _n = 0d;
            _m = Vector3.zero;
            _zMin = float.MaxValue;
            _zMax = float.MinValue;
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
                ContactPoint c = s_buffer[i];
                Vector3 normal = Vector3.Dot(c.normal, Vector3.up) < 0f ? -c.normal : c.normal;
                Vector3 impulse = Vector3.Dot(c.impulse, normal) < 0f ? -c.impulse : c.impulse;
                float n = Vector3.Dot(impulse, normal);
                if (n <= 0f)
                    continue;
                _n += n;
                _m += n * c.point;
                _zMin = Mathf.Min(_zMin, c.point.z);
                _zMax = Mathf.Max(_zMax, c.point.z);
                float a = Vector3.Dot(c.point, Axis);
                _aMin = Mathf.Min(_aMin, a);
                _aMax = Mathf.Max(_aMax, a);
            }
        }
    }
}
