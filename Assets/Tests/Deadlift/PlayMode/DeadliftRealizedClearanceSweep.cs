using System.Linq;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift
{
    internal sealed class DeadliftRealizedClearanceSweep
    {
        private readonly CapsuleCollider[] _colliders;
        private CapsulePose[] _previous;
        public int Samples { get; private set; }
        public float MinGap { get; private set; } = float.PositiveInfinity;
        public float MinBound { get; private set; } = float.PositiveInfinity;

        public DeadliftRealizedClearanceSweep(Rigidbody bar, PhysicalAthleteRig rig)
        {
            _colliders = new[] { DeadliftBarLegGeometry.Shaft(bar) }
                .Concat(DeadliftBarLegGeometry.Shins.Concat(DeadliftBarLegGeometry.Thighs).Select(id => (CapsuleCollider)rig.Segments[id].Collider)).ToArray();
            foreach (CapsuleCollider collider in _colliders)
            {
                Assert.That(collider.transform, Is.EqualTo(collider.attachedRigidbody.transform));
                Assert.That(Vector3.Distance(collider.transform.lossyScale, Vector3.one), Is.LessThan(1e-5f));
            }
            Read();
            Samples = 0;
        }

        public void Read()
        {
            CapsulePose[] current = _colliders.Select(c => new CapsulePose(c)).ToArray();
            for (int i = 1; i < current.Length; i++)
            {
                float gap = current[0].Gap(current[i]);
                MinGap = Mathf.Min(MinGap, gap);
                if (_previous == null)
                    continue;
                float bound = Mathf.Min(gap, _previous[0].Gap(_previous[i]))
                    - 0.5f * (current[0].TravelFrom(_previous[0]) + current[i].TravelFrom(_previous[i]));
                MinBound = Mathf.Min(MinBound, bound);
            }
            _previous = current;
            Samples++;
        }

        private readonly struct CapsulePose
        {
            private readonly Pose _body;
            private readonly Vector3 _a, _b;
            private readonly float _radius, _lever;

            public CapsulePose(CapsuleCollider collider)
            {
                Rigidbody body = collider.attachedRigidbody;
                _body = BodyPose(body);
                _radius = collider.radius;
                Vector3 axis = collider.direction == 0 ? Vector3.right : collider.direction == 1 ? Vector3.up : Vector3.forward;
                Vector3 half = axis * Mathf.Max(0f, 0.5f * collider.height - _radius);
                _a = Point(_body, collider.center - half);
                _b = Point(_body, collider.center + half);
                _lever = Mathf.Max((collider.center - half).magnitude, (collider.center + half).magnitude);
            }

            public float Gap(CapsulePose other) =>
                DeadliftSetupSolver.DistanceSegmentSegment(_a, _b, other._a, other._b) - _radius - other._radius;

            public float TravelFrom(CapsulePose other) =>
                Vector3.Distance(_body.position, other._body.position) + Radians(_body.rotation, other._body.rotation) * _lever;
        }

        private static float Radians(Quaternion a, Quaternion b)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(Quaternion.Inverse(a) * b);
            return 2f * Mathf.Asin(Mathf.Min(1f, new Vector3(q.x, q.y, q.z).magnitude));
        }

        private static Pose BodyPose(Rigidbody body) => new Pose(body.position, body.rotation);
        private static Vector3 Point(Pose pose, Vector3 local) => pose.position + pose.rotation * local;
    }
}
