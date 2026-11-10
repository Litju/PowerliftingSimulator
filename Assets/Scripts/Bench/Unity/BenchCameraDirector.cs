using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    public enum BenchCameraAngle : byte
    {
        ThreeQuarter,
        Side,
        ChiefReferee,
        Overhead
    }

    /// <summary>Presentation-only camera around the bench. Reads the bar position; writes only the camera.</summary>
    [DisallowMultipleComponent]
    public sealed class BenchCameraDirector : MonoBehaviour
    {
        private BenchPressPrototypeController _bench;
        private Vector3 _focus = new Vector3(0f, 0.75f, 0.25f);
        private bool _snapped;

        public BenchCameraAngle Angle { get; private set; } = BenchCameraAngle.ThreeQuarter;

        public static string Label(BenchCameraAngle angle)
        {
            switch (angle)
            {
                case BenchCameraAngle.Side: return "SIDE";
                case BenchCameraAngle.ChiefReferee: return "CHIEF REFEREE";
                case BenchCameraAngle.Overhead: return "OVERHEAD";
                default: return "3/4 VIEW";
            }
        }

        public void SetAngle(BenchCameraAngle angle, bool snap = false)
        {
            Angle = angle;
            if (snap)
                _snapped = false;
        }

        public void Cycle() => SetAngle((BenchCameraAngle)(((int)Angle + 1) % 4));

        private void LateUpdate()
        {
            if (_bench == null)
                _bench = FindFirstObjectByType<BenchPressPrototypeController>();
            if (_bench != null && _bench.Barbell != null && _bench.Barbell.Body != null)
            {
                Vector3 bar = _bench.Barbell.Body.position;
                Vector3 target = new Vector3(0f, Mathf.Lerp(0.62f, bar.y, 0.5f), Mathf.Lerp(0.2f, bar.z, 0.5f));
                _focus = _snapped ? Vector3.Lerp(_focus, target, 1f - Mathf.Exp(-4f * Time.unscaledDeltaTime)) : target;
            }

            Vector3 offset;
            float fov;
            switch (Angle)
            {
                case BenchCameraAngle.Side: offset = new Vector3(-2.35f, 0.35f, 0.05f); fov = 42f; break;
                case BenchCameraAngle.ChiefReferee: offset = new Vector3(0.35f, 0.75f, -2.2f); fov = 44f; break;
                case BenchCameraAngle.Overhead: offset = new Vector3(-0.35f, 2.3f, 0.6f); fov = 50f; break;
                default: offset = new Vector3(-1.85f, 0.95f, 1.75f); fov = 44f; break;
            }

            Vector3 desired = _focus + offset;
            Quaternion look = Quaternion.LookRotation(_focus - desired, Vector3.up);
            float k = _snapped ? 1f - Mathf.Exp(-5f * Time.unscaledDeltaTime) : 1f;
            transform.SetPositionAndRotation(Vector3.Lerp(transform.position, desired, k), Quaternion.Slerp(transform.rotation, look, k));
            Camera cam = GetComponent<Camera>();
            if (cam != null)
                cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, fov, k);
            _snapped = true;
        }
    }
}
