using System;
using System.Collections.Generic;
using PowerliftingSimulator.Equipment;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public sealed class SquatBarSaddle
    {
        public const string SaddleType = "ConfigurableJoint_UpperBack_Thorax_V1";

        public readonly struct ExperimentalConfiguration
        {
            public ExperimentalConfiguration(float linearSpring, float linearDamper, float linearLimitM)
            {
                LinearSpring = linearSpring;
                LinearDamper = linearDamper;
                LinearLimitM = linearLimitM;
            }

            public float LinearSpring { get; }
            public float LinearDamper { get; }
            public float LinearLimitM { get; }
        }

        /// <summary>Null preserves current Saddle V2 defaults.</summary>
        public static ExperimentalConfiguration? ExperimentalOverride { get; set; }

        /// <summary>
        /// Bar/athlete collision topology. V2 applies the bar/non-thorax limb
        /// filter to the bar Rigidbody's own colliders; V1 enumerated the
        /// PhysicalBarbell component, found none, and filtered nothing
        /// (Artifacts/Research/GAM-13-saddle-v2-contact-topology.md).
        /// </summary>
        public const string CollisionTopologyVersion = "GAM13_SADDLE_BAR_LIMB_FILTER_V2";
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
        private SquatBarThoraxContactDetector _thoraxContactDetector;
        private bool _explicitlyBroken;
        private float _initialAnchorErrorM;
        private Quaternion _initialRelativeBarToThorax;
        private int _barThoraxColliderPairCount;
        private int _barThoraxIgnoredPairCount;

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
        public SquatBarThoraxContactDetector ThoraxContact => _thoraxContactDetector;
        public bool ConnectedBodyCollisionEnabled => _joint != null && _joint.enableCollision;
        public int BarThoraxColliderPairCount => _barThoraxColliderPairCount;
        public int BarThoraxIgnoredPairCount => _barThoraxIgnoredPairCount;
        public int FilteredBarAthletePairCount => _ignoredCollisions.Count;
        public Vector3 AnchorErrorWorld => WorldThoraxAnchor - WorldBarAnchor;
        public Vector3 AnchorErrorBarLocal => _barbell != null && _barbell.Body != null
            ? Quaternion.Inverse(_barbell.Body.rotation) * AnchorErrorWorld
            : Vector3.zero;
        /// <summary>
        /// Raw ConfigurableJoint.currentForce/currentTorque diagnostics. The
        /// engine frame/interpretation is intentionally not promoted here to
        /// a biological or world-space force claim.
        /// </summary>
        public Vector3 CurrentForceEngine => _joint == null ? Vector3.zero : _joint.currentForce;
        public Vector3 CurrentTorqueEngine => _joint == null ? Vector3.zero : _joint.currentTorque;
        public float CurrentLinearLimitOccupancy => _joint == null || _joint.linearLimit.limit <= 0f
            ? float.NaN
            : AnchorErrorWorld.magnitude / _joint.linearLimit.limit;
        public float CurrentAngularXLimitOccupancy => AngularLimitOccupancy(AxisComponent.X);
        public float CurrentAngularYLimitOccupancy => AngularLimitOccupancy(AxisComponent.Y);
        public float CurrentAngularZLimitOccupancy => AngularLimitOccupancy(AxisComponent.Z);
        public float MaximumLimitOccupancy => Mathf.Max(
            CurrentLinearLimitOccupancy,
            Mathf.Max(CurrentAngularXLimitOccupancy, Mathf.Max(CurrentAngularYLimitOccupancy, CurrentAngularZLimitOccupancy)));
        public bool AtFiniteLimit =>
            float.IsFinite(MaximumLimitOccupancy) && MaximumLimitOccupancy >= 0.95f;
        public float BarMassKg => _barbell == null || _barbell.Body == null ? float.NaN : _barbell.Body.mass;
        public float ThoraxMassKg => _thoraxBody == null ? float.NaN : _thoraxBody.mass;
        public float BarToThoraxMassRatio =>
            float.IsFinite(BarMassKg) && ThoraxMassKg > 0f ? BarMassKg / ThoraxMassKg : float.NaN;

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
            _thoraxContactDetector?.Unbind();
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
            _initialRelativeBarToThorax = Quaternion.Inverse(_barbell.Body.rotation) * _thoraxBody.rotation;

            _barThoraxColliderPairCount = 0;
            _barThoraxIgnoredPairCount = 0;

            // Suppress the non-load-bearing head/limb contacts. The bar's colliders
            // live on its authoritative Rigidbody root, not under the PhysicalBarbell
            // component. Bar/thorax contact is left to the joint's enableCollision
            // (disabled), so the finite joint is the complete bar/back load path.
            Collider[] barColliders = _barbell.Body.GetComponentsInChildren<Collider>(true);
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

            Collider[] thoraxColliders = _thoraxBody.GetComponents<Collider>();
            foreach (Collider b in barColliders)
            {
                foreach (Collider a in thoraxColliders)
                {
                    if (b == null || a == null)
                        continue;
                    _barThoraxColliderPairCount++;
                    if (Physics.GetIgnoreCollision(b, a))
                        _barThoraxIgnoredPairCount++;
                }
            }

            _thoraxContactDetector = _thoraxBody.GetComponent<SquatBarThoraxContactDetector>();
            if (_thoraxContactDetector == null)
                _thoraxContactDetector = _thoraxBody.gameObject.AddComponent<SquatBarThoraxContactDetector>();
            _thoraxContactDetector.Bind(_barbell.Body);

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
            ExperimentalConfiguration configuration = ExperimentalOverride ??
                new ExperimentalConfiguration(DefaultLinearSpring, DefaultLinearDamper, DefaultLinearLimitM);
            _joint.linearLimit = new SoftJointLimit { limit = configuration.LinearLimitM };

            JointDrive linearDrive = new JointDrive
            {
                positionSpring = configuration.LinearSpring,
                positionDamper = configuration.LinearDamper,
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

        public void CompleteContactStep() => _thoraxContactDetector?.CompletePhysicsStep();

        private enum AxisComponent : byte
        {
            X,
            Y,
            Z
        }

        private float AngularLimitOccupancy(AxisComponent component)
        {
            if (_joint == null)
                return float.NaN;

            float limitDegrees;
            switch (component)
            {
                case AxisComponent.X:
                    limitDegrees = Mathf.Max(Mathf.Abs(_joint.lowAngularXLimit.limit), Mathf.Abs(_joint.highAngularXLimit.limit));
                    break;
                case AxisComponent.Y:
                    limitDegrees = Mathf.Abs(_joint.angularYLimit.limit);
                    break;
                default:
                    limitDegrees = Mathf.Abs(_joint.angularZLimit.limit);
                    break;
            }
            if (limitDegrees <= 0f)
                return 0f;

            // The joint axes are authored in the bar (joint-owner) frame, so the
            // deviation from the creation-time relative pose is expressed there.
            Vector3 rotationVector = QuaternionLog(RelativeDeviationBarFrame());
            Vector3 xAxis = _joint.axis.normalized;
            Vector3 yAxis = _joint.secondaryAxis.normalized;
            Vector3 zAxis = Vector3.Cross(xAxis, yAxis).normalized;
            float componentRadians = component == AxisComponent.X
                ? Vector3.Dot(rotationVector, xAxis)
                : component == AxisComponent.Y
                    ? Vector3.Dot(rotationVector, yAxis)
                    : Vector3.Dot(rotationVector, zAxis);
            return Mathf.Abs(componentRadians) * Mathf.Rad2Deg / limitDegrees;
        }

        /// <summary>
        /// Total thorax-relative bar rotation since the joint was created, in
        /// degrees. Diagnostic only; it is independent of axis decomposition.
        /// </summary>
        public float RelativeRotationDegrees => _joint == null || _barbell.Body == null || _thoraxBody == null
            ? float.NaN
            : QuaternionLog(RelativeDeviationBarFrame()).magnitude * Mathf.Rad2Deg;

        public Quaternion InitialRelativeBarToThorax => _initialRelativeBarToThorax;

        private Quaternion RelativeDeviationBarFrame()
        {
            Quaternion currentRelative = Quaternion.Inverse(_barbell.Body.rotation) * _thoraxBody.rotation;
            return currentRelative * Quaternion.Inverse(_initialRelativeBarToThorax);
        }

        private static Vector3 QuaternionLog(Quaternion value)
        {
            float magnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w);
            if (magnitude <= 1e-6f)
                return Vector3.zero;
            value = new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude);
            if (value.w < 0f)
                value = new Quaternion(-value.x, -value.y, -value.z, -value.w);
            float vectorMagnitude = Mathf.Sqrt(value.x * value.x + value.y * value.y + value.z * value.z);
            if (vectorMagnitude <= 1e-6f)
                return new Vector3(value.x, value.y, value.z) * 2f;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(value.w, -1f, 1f));
            return new Vector3(value.x, value.y, value.z) * (angle / vectorMagnitude);
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
