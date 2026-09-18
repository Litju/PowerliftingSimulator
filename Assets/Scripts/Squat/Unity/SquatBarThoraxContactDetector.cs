using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// Read-only diagnostic boundary for physical bar/thorax collision
    /// callbacks. With connected-body collision disabled, zero callbacks is a
    /// meaningful observation of that topology, not a synthesized contact.
    /// The saddle ConfigurableJoint remains the load-bearing authority; this
    /// component never applies physics state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SquatBarThoraxContactDetector : MonoBehaviour
    {
        private const int MaxTrackedContacts = 32;

        private readonly ContactPoint[] _scratchContacts = new ContactPoint[16];
        private readonly Vector3[] _pendingPoints = new Vector3[MaxTrackedContacts];
        private readonly Vector3[] _pendingImpulses = new Vector3[MaxTrackedContacts];
        private readonly Vector3[] _pendingNormals = new Vector3[MaxTrackedContacts];
        private readonly float[] _pendingSeparations = new float[MaxTrackedContacts];
        private readonly Vector3[] _completedPoints = new Vector3[MaxTrackedContacts];
        private readonly Vector3[] _completedImpulses = new Vector3[MaxTrackedContacts];
        private readonly Vector3[] _completedNormals = new Vector3[MaxTrackedContacts];
        private readonly float[] _completedSeparations = new float[MaxTrackedContacts];

        private Rigidbody _barBody;
        private int _pendingCount;
        private int _pendingCallbackCount;
        private Vector3 _pendingTotalImpulse;
        private float _pendingMinimumSeparation;
        private int _completedCount;

        public bool IsBound => _barBody != null;
        public int CompletedContactCount => _completedCount;
        public int CompletedCollisionCallbackCount { get; private set; }
        public Vector3 CompletedTotalImpulse { get; private set; }
        public float CompletedMinimumSeparationM { get; private set; }
        public bool HasCompletedContact => _completedCount > 0;
        public float MaximumPenetrationM =>
            CompletedCountIsFinite() ? Mathf.Max(0f, -CompletedMinimumSeparationM) : float.NaN;

        public void Bind(Rigidbody barBody)
        {
            _barBody = barBody;
            ClearPendingAndCompleted();
        }

        public void Unbind()
        {
            _barBody = null;
            ClearPendingAndCompleted();
        }

        public Vector3 CompletedContactPoint(int index) => _completedPoints[index];
        public Vector3 CompletedContactImpulse(int index) => _completedImpulses[index];
        public Vector3 CompletedContactNormal(int index) => _completedNormals[index];
        public float CompletedContactSeparation(int index) => _completedSeparations[index];

        /// <summary>Promotes callbacks from the just-completed PhysicsScene step.</summary>
        public void CompletePhysicsStep()
        {
            for (int index = 0; index < _pendingCount; index++)
            {
                _completedPoints[index] = _pendingPoints[index];
                _completedImpulses[index] = _pendingImpulses[index];
                _completedNormals[index] = _pendingNormals[index];
                _completedSeparations[index] = _pendingSeparations[index];
            }
            _completedCount = _pendingCount;
            CompletedCollisionCallbackCount = _pendingCallbackCount;
            CompletedTotalImpulse = _pendingTotalImpulse;
            CompletedMinimumSeparationM = _pendingCount == 0 ? float.NaN : _pendingMinimumSeparation;
            _pendingCount = 0;
            _pendingCallbackCount = 0;
            _pendingTotalImpulse = Vector3.zero;
            _pendingMinimumSeparation = float.PositiveInfinity;
        }

        private void OnCollisionEnter(Collision collision) => RecordCollision(collision);
        private void OnCollisionStay(Collision collision) => RecordCollision(collision);

        private void RecordCollision(Collision collision)
        {
            if (_barBody == null || collision == null ||
                (collision.rigidbody != _barBody && collision.collider.attachedRigidbody != _barBody))
                return;

            int written = collision.GetContacts(_scratchContacts);
            if (written <= 0)
                return;

            _pendingCallbackCount++;
            for (int index = 0; index < written; index++)
            {
                ContactPoint contact = _scratchContacts[index];
                _pendingTotalImpulse += contact.impulse;
                _pendingMinimumSeparation = Mathf.Min(_pendingMinimumSeparation, contact.separation);
                if (_pendingCount >= MaxTrackedContacts)
                    continue;
                _pendingPoints[_pendingCount] = contact.point;
                _pendingImpulses[_pendingCount] = contact.impulse;
                _pendingNormals[_pendingCount] = contact.normal;
                _pendingSeparations[_pendingCount] = contact.separation;
                _pendingCount++;
            }
        }

        private void ClearPendingAndCompleted()
        {
            _pendingCount = 0;
            _pendingCallbackCount = 0;
            _pendingTotalImpulse = Vector3.zero;
            _pendingMinimumSeparation = float.PositiveInfinity;
            _completedCount = 0;
            CompletedCollisionCallbackCount = 0;
            CompletedTotalImpulse = Vector3.zero;
            CompletedMinimumSeparationM = float.NaN;
        }

        private bool CompletedCountIsFinite() =>
            _completedCount == 0 || float.IsFinite(CompletedMinimumSeparationM);
    }
}
