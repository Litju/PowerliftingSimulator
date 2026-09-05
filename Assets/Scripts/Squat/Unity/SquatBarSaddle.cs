using System;
using System.Collections.Generic;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public sealed class SquatBarSaddle
    {
        public const string SaddleType = "ConfigurableJoint_UpperBack_Thorax_V1";
        public const float DefaultLinearLimitM = 0.05f;
        public const float DefaultLinearSpring = 50000f;
        public const float DefaultLinearDamper = 3000f;
        public const float DefaultLinearMaxForce = 60000f;
        public const float DefaultAngularSpring = 1200f;
        public const float DefaultAngularDamper = 100f;
        public const float DefaultAngularMaxForce = 1200f;
        public const float DefaultBreakForce = 60000f;
        public const float DefaultBreakTorque = 15000f;
        public const float MaxPlausibleSeparationM = 0.12f;
        public const float MaxSpawnAnchorErrorM = 0.20f;

        // Calibrated local trap shelf anchor on thorax body
        public static readonly Vector3 ThoraxLocalAnchor = new Vector3(0f, 0.080f, -0.115f);
        public static readonly Vector3 BarLocalAnchor = Vector3.zero;

        private readonly PhysicalBarbell _barbell;
        private readonly Rigidbody _thoraxBody;
        private readonly List<CollisionIgnorePair> _ignoredCollisions = new List<CollisionIgnorePair>();
        private ConfigurableJoint _joint;
        private bool _explicitlyBroken;
        private float _initialAnchorErrorM;

        public SquatBarSaddle(PhysicalBarbell barbell, Rigidbody thoraxBody)
        {
            _barbell = barbell ?? throw new ArgumentNullException(nameof(barbell));
            _thoraxBody = thoraxBody ?? throw new ArgumentNullException(nameof(thoraxBody));
            AttachJoint();
        }

        public ConfigurableJoint Joint => _joint;
        public PhysicalBarbell Barbell => _barbell;
        public Rigidbody ThoraxBody => _thoraxBody;
        public bool IsAttached => _joint != null && !_explicitlyBroken;
        public bool IsBroken => !IsAttached || SaddleSeparationMeters > MaxPlausibleSeparationM;
        public float InitialAnchorErrorMeters => _initialAnchorErrorM;
        public bool SpawnAlignmentWithinTolerance => _initialAnchorErrorM <= MaxSpawnAnchorErrorM;

        public float SaddleSeparationMeters
        {
            get
            {
                if (_barbell == null || _barbell.Body == null || _thoraxBody == null)
                    return float.PositiveInfinity;
                Vector3 barAnchorWorld = _barbell.Body.transform.TransformPoint(BarLocalAnchor);
                Vector3 thoraxAnchorWorld = _thoraxBody.transform.TransformPoint(ThoraxLocalAnchor);
                return Vector3.Distance(barAnchorWorld, thoraxAnchorWorld);
            }
        }

        public Vector3 WorldBarAnchor => _barbell != null && _barbell.Body != null
            ? _barbell.Body.transform.TransformPoint(BarLocalAnchor)
            : Vector3.zero;

        public Vector3 WorldThoraxAnchor => _thoraxBody != null
            ? _thoraxBody.transform.TransformPoint(ThoraxLocalAnchor)
            : Vector3.zero;

        public void BreakSaddle()
        {
            _explicitlyBroken = true;
            for (int index = 0; index < _ignoredCollisions.Count; index++)
            {
                CollisionIgnorePair pair = _ignoredCollisions[index];
                if (pair.BarCollider != null && pair.AthleteCollider != null)
                    Physics.IgnoreCollision(pair.BarCollider, pair.AthleteCollider, false);
            }
            _ignoredCollisions.Clear();
            if (_joint != null)
            {
                UnityEngine.Object.DestroyImmediate(_joint);
                _joint = null;
            }
        }

        private void AttachJoint()
        {
            if (_barbell.Body == null || _thoraxBody == null)
                throw new InvalidOperationException("Barbell and thorax Rigidbody must be initialized.");
            if (_barbell.Body.isKinematic || !_barbell.Body.useGravity || _thoraxBody.isKinematic || !_thoraxBody.useGravity)
                throw new InvalidOperationException("The squat saddle requires two dynamic gravity-enabled bodies.");

            // The scene/runtime builder owns the initial spawn pose. The saddle only
            // creates a finite joint; it never teleports or velocity-resets a body.
            _initialAnchorErrorM = Vector3.Distance(
                _barbell.Body.transform.TransformPoint(BarLocalAnchor),
                _thoraxBody.transform.TransformPoint(ThoraxLocalAnchor));

            // Keep the thorax/back contact available and suppress only non-load-bearing
            // head/limb artifacts. The finite joint remains the coupling authority.
            Collider[] barColliders = _barbell.GetComponentsInChildren<Collider>(true);
            Collider[] athleteColliders = _thoraxBody.transform.root.GetComponentsInChildren<Collider>(true);
            foreach (Collider b in barColliders)
            {
                foreach (Collider a in athleteColliders)
                {
                    if (a == null || a.transform == _thoraxBody.transform)
                        continue;
                    Physics.IgnoreCollision(b, a, true);
                    _ignoredCollisions.Add(new CollisionIgnorePair(b, a));
                }
            }

            _joint = _barbell.Body.gameObject.AddComponent<ConfigurableJoint>();
            _joint.connectedBody = _thoraxBody;
            _joint.autoConfigureConnectedAnchor = false;
            _joint.anchor = BarLocalAnchor;
            _joint.connectedAnchor = ThoraxLocalAnchor;
            _joint.axis = Vector3.right;
            _joint.secondaryAxis = Vector3.up;

            // Small compliant translation
            _joint.xMotion = ConfigurableJointMotion.Limited;
            _joint.yMotion = ConfigurableJointMotion.Limited;
            _joint.zMotion = ConfigurableJointMotion.Limited;
            _joint.linearLimit = new SoftJointLimit { limit = DefaultLinearLimitM };

            JointDrive linearDrive = new JointDrive
            {
                positionSpring = DefaultLinearSpring,
                positionDamper = DefaultLinearDamper,
                maximumForce = DefaultLinearMaxForce,
                useAcceleration = false
            };
            _joint.xDrive = linearDrive;
            _joint.yDrive = linearDrive;
            _joint.zDrive = linearDrive;

            // Bounded rotational freedom
            _joint.angularXMotion = ConfigurableJointMotion.Limited;
            _joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
            _joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
            _joint.angularYMotion = ConfigurableJointMotion.Limited;
            _joint.angularYLimit = new SoftJointLimit { limit = 15f };
            _joint.angularZMotion = ConfigurableJointMotion.Limited;
            _joint.angularZLimit = new SoftJointLimit { limit = 15f };

            JointDrive angularDrive = new JointDrive
            {
                positionSpring = DefaultAngularSpring,
                positionDamper = DefaultAngularDamper,
                maximumForce = DefaultAngularMaxForce,
                useAcceleration = false
            };
            _joint.angularXDrive = angularDrive;
            _joint.angularYZDrive = angularDrive;

            _joint.breakForce = DefaultBreakForce;
            _joint.breakTorque = DefaultBreakTorque;
            _joint.projectionMode = JointProjectionMode.None;
            _joint.enableCollision = false;
            _joint.enablePreprocessing = true;

            _explicitlyBroken = false;
        }

        private readonly struct CollisionIgnorePair
        {
            public CollisionIgnorePair(Collider barCollider, Collider athleteCollider)
            {
                BarCollider = barCollider;
                AthleteCollider = athleteCollider;
            }

            public Collider BarCollider { get; }
            public Collider AthleteCollider { get; }
        }
    }
}
