using System;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Test-only, read-only collision recorder for the GAM-13 causal audit.
    /// Unlike the production plantar detector it keeps the full reported
    /// impulse vector and the identity of the other body, so the audit can
    /// measure whether tangential impulse is observable at all and which
    /// bodies the bar actually touches. It never writes physics state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GAM13GroundContactProbe : MonoBehaviour
    {
        public const string SupportObjectName = "PhysicalPlatform_GAM6";
        private const int Capacity = 64;

        public readonly struct Sample
        {
            public Sample(string other, bool isPlatform, Vector3 point, Vector3 normal, Vector3 impulse, float separation)
            {
                Other = other;
                IsPlatform = isPlatform;
                Point = point;
                Normal = normal;
                Impulse = impulse;
                Separation = separation;
            }

            public string Other { get; }
            public bool IsPlatform { get; }
            public Vector3 Point { get; }
            public Vector3 Normal { get; }
            public Vector3 Impulse { get; }
            public float Separation { get; }
        }

        private readonly ContactPoint[] _scratch = new ContactPoint[16];
        private readonly Sample[] _pending = new Sample[Capacity];
        private readonly Sample[] _completed = new Sample[Capacity];
        private int _pendingCount;

        public int CompletedCount { get; private set; }

        public Sample Completed(int index) => _completed[index];

        public int CompletedPlatformCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < CompletedCount; index++)
                {
                    if (_completed[index].IsPlatform)
                        count++;
                }
                return count;
            }
        }

        public void Clear()
        {
            _pendingCount = 0;
            CompletedCount = 0;
        }

        public void CompleteStep()
        {
            Array.Copy(_pending, _completed, _pendingCount);
            CompletedCount = _pendingCount;
            _pendingCount = 0;
        }

        private void OnCollisionEnter(Collision collision) => Record(collision);

        private void OnCollisionStay(Collision collision) => Record(collision);

        private void Record(Collision collision)
        {
            if (collision == null || collision.collider == null)
                return;
            bool isPlatform = collision.collider.gameObject.name == SupportObjectName;
            Rigidbody otherBody = collision.collider.attachedRigidbody;
            string other = isPlatform
                ? "platform"
                : otherBody != null ? otherBody.gameObject.name : collision.collider.gameObject.name;
            int written = collision.GetContacts(_scratch);
            for (int index = 0; index < written && _pendingCount < Capacity; index++)
            {
                ContactPoint contact = _scratch[index];
                _pending[_pendingCount++] = new Sample(
                    other, isPlatform, contact.point, contact.normal, contact.impulse, contact.separation);
            }
        }
    }
}
