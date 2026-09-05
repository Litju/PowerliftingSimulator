using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class PhysicalFootContactDetector : MonoBehaviour
    {
        private const string SupportObjectName = "PhysicalPlatform_GAM6";
        private const float ContactSolverNoiseFloorMps = 0.01f;
        private Rigidbody _rigidbody;
        private int _contactCount;
        private readonly ContactPoint[] _supportContacts = new ContactPoint[8];
        private Vector3 _supportContactPoint;
        private bool _hasSupportContact;
        private float _slipAccumulator;
        private float _lastSlipSpeed;

        public bool IsInContact => _contactCount > 0;
        public int ContactCount => _contactCount;
        public float SlipSpeed => _lastSlipSpeed;
        public float SlipAccumulatedM => _slipAccumulator;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsSupportCollision(collision))
                return;
            _contactCount++;
            UpdateSupportContact(collision);
        }

        private void OnCollisionStay(Collision collision)
        {
            if (!IsSupportCollision(collision))
                return;
            UpdateSupportContact(collision);
        }

        private void OnCollisionExit(Collision collision)
        {
            if (!IsSupportCollision(collision))
                return;
            _contactCount = Mathf.Max(0, _contactCount - 1);
            if (_contactCount == 0)
                _hasSupportContact = false;
        }

        private static bool IsSupportCollision(Collision collision) =>
            collision != null && collision.collider != null &&
            collision.collider.gameObject.name == SupportObjectName;

        private void UpdateSupportContact(Collision collision)
        {
            int contactCount = Mathf.Min(collision.contactCount, _supportContacts.Length);
            if (contactCount <= 0)
                return;

            int written = collision.GetContacts(_supportContacts);
            if (written <= 0)
                return;

            Vector3 pointSum = Vector3.zero;
            for (int index = 0; index < written; index++)
                pointSum += _supportContacts[index].point;

            _supportContactPoint = pointSum / written;
            _hasSupportContact = true;
        }

        public void PhysicsTickUpdate(float dt)
        {
            if (_rigidbody == null)
                return;

            if (!IsInContact || !_hasSupportContact || dt <= 0.0001f)
            {
                _lastSlipSpeed = 0f;
                return;
            }

            // Measure the velocity of the actual plantar contact point rather
            // than the foot body's center. A rotating foot can move its body
            // center while rolling without sliding on the platform; only the
            // tangential velocity at the support contact is ground slip.
            Vector3 contactVelocity = _rigidbody.GetPointVelocity(_supportContactPoint);
            Vector3 tangentialVelocity = Vector3.ProjectOnPlane(contactVelocity, Vector3.up);
            float slipSpeed = tangentialVelocity.magnitude;
            _lastSlipSpeed = slipSpeed;
            // Sub-centimetre-per-second residuals are contact-solver noise at
            // this prototype's 100 Hz timestep, not measurable plantar slip.
            if (slipSpeed > ContactSolverNoiseFloorMps)
                _slipAccumulator += slipSpeed * dt;
        }

        public void ResetContact()
        {
            _contactCount = 0;
            _hasSupportContact = false;
            _slipAccumulator = 0f;
            _lastSlipSpeed = 0f;
        }
    }
}
