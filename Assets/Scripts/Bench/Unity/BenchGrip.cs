using System;
using UnityEngine;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// One hand's grasp of the bar: a single ConfigurableJoint on the physical
    /// hand body, connected to the one dynamic bar Rigidbody, with all six
    /// relative degrees of freedom locked at the attach pose (a closed power
    /// grip). There is no world attachment, no projection and no transform
    /// write: whatever the bar does it does because the solver transmits the
    /// arm's forces and moments through this constraint.
    ///
    /// Two of these form a closed chain thorax - arm - bar - arm - thorax. Its
    /// mobility is 7 bodies x 6 = 42 minus shoulders 6, elbows 10, wrists 6,
    /// grips 12 = 8: all six bar DOFs plus one elbow swivel per arm. The grips
    /// therefore do not overconstrain the loop; the qualification tests check
    /// that the solver agrees by measuring constraint drift.
    /// </summary>
    public sealed class BenchGrip
    {
        private Rigidbody _hand;
        private Rigidbody _bar;
        private Vector3 _handAnchorLocal;
        private Vector3 _barAnchorLocal;

        public BenchGrip(string side)
        {
            Side = side ?? throw new ArgumentNullException(nameof(side));
        }

        public string Side { get; }
        public ConfigurableJoint Joint { get; private set; }
        public bool IsAttached => Joint != null;
        public Vector3 HandAnchorLocal => _handAnchorLocal;
        public Vector3 BarAnchorLocal => _barAnchorLocal;

        /// <summary>Bar-local X of the grip at attach, for lateral-slip measurement.</summary>
        public float AttachBarX => _barAnchorLocal.x;

        public void Attach(Rigidbody hand, Rigidbody bar, Vector3 handAnchorLocal)
        {
            if (Joint != null)
                throw new InvalidOperationException($"{Side} grip is already attached.");
            _hand = hand ?? throw new ArgumentNullException(nameof(hand));
            _bar = bar ?? throw new ArgumentNullException(nameof(bar));
            _handAnchorLocal = handAnchorLocal;
            Vector3 world = hand.transform.TransformPoint(handAnchorLocal);
            _barAnchorLocal = bar.transform.InverseTransformPoint(world);

            ConfigurableJoint joint = hand.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = bar;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = handAnchorLocal;
            joint.connectedAnchor = _barAnchorLocal;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Locked;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Locked;
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            joint.breakForce = float.PositiveInfinity;
            joint.breakTorque = float.PositiveInfinity;
            Joint = joint;
        }

        public void Release()
        {
            if (Joint == null)
                return;
            UnityEngine.Object.Destroy(Joint);
            Joint = null;
        }

        /// <summary>Constraint violation: distance between the hand point and its bar point.</summary>
        public float AnchorSeparationM
        {
            get
            {
                if (Joint == null)
                    return float.NaN;
                return Vector3.Distance(_hand.transform.TransformPoint(_handAnchorLocal), _bar.transform.TransformPoint(_barAnchorLocal));
            }
        }

        /// <summary>Movement of the hand along the shaft since attach (lateral hand movement).</summary>
        public float LateralSlipM
        {
            get
            {
                if (Joint == null)
                    return 0f;
                Vector3 handInBar = _bar.transform.InverseTransformPoint(_hand.transform.TransformPoint(_handAnchorLocal));
                return Mathf.Abs(handInBar.x - _barAnchorLocal.x);
            }
        }

        /// <summary>
        /// ENGINE_SOLVER_DIAGNOSTIC: the raw <c>Joint.currentForce</c>.
        /// Unity 6000.3 documents it as "relative to connectedBody"; the
        /// bounded control BenchJointForceFrameControlTests measured on
        /// 6000.3.22f1, with both bodies at arbitrary rotations, that it is
        /// the constraint force on the joint's own body (here the hand) in the
        /// world frame. <see cref="ForceOnHandWorld"/> and
        /// <see cref="ForceOnBarWorld"/> apply that measured contract.
        /// It is the solver's constraint reaction, not a decomposed muscular
        /// hand force.
        /// </summary>
        public Vector3 SolverForceRaw => Joint == null ? Vector3.zero : Joint.currentForce;

        /// <summary>Raw <c>Joint.currentTorque</c>: the moment on the hand about the grip anchor, world frame (same control).</summary>
        public Vector3 SolverTorqueRaw => Joint == null ? Vector3.zero : Joint.currentTorque;

        /// <summary>Force the bar applies to the hand, world frame, newtons.</summary>
        public Vector3 ForceOnHandWorld => SolverForceRaw;

        /// <summary>Force the hand applies to the bar, world frame (action-reaction of <see cref="ForceOnHandWorld"/>).</summary>
        public Vector3 ForceOnBarWorld => -SolverForceRaw;

        /// <summary>The measured contract as a pure function, for the control test: raw value -> force on the joint's own body, world.</summary>
        public static Vector3 RawForceToOwnerWorld(Vector3 raw) => raw;
    }
}
