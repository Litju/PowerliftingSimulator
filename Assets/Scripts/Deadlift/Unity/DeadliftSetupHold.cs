using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// The only joint-command writer of the deadlift setup qualification: it
    /// commands every powered joint to hold the solved setup posture with
    /// full effort at the athlete's finite family capacities, scaled by
    /// <see cref="StrengthScale"/> per joint family (1 = canonical). It never
    /// writes a body, the bar or a force.
    ///
    /// Optional static gravity reference (GAM-70, same physics as the accepted
    /// squat's GAM-50 R9 trim, implemented here so the squat stays untouched):
    /// a PD drive of stiffness K holding a load torque tau_g settles at the
    /// error e = tau_g / K, so commanding the solved pose q* realizes q* - e,
    /// not q*. <see cref="ComputeStaticGravityBias"/> measures tau_g once,
    /// from the placed bodies at the initial condition, as the gravity moment
    /// of each joint's free side about its anchor (grounded legs: the parent
    /// side, with the upper body shared equally by the two legs; trunk, neck
    /// and arms: the child subtree), and offsets each reference by tau_g / K in
    /// the joint's logical frame, bounded by the joint's finite capacity. It is
    /// a fixed reference correction, not feedback: it never reads state after
    /// the initial condition.
    /// </summary>
    public sealed class DeadliftSetupHold : IPhysicalAthleteCommandSource
    {
        private static readonly string[] UpperBody =
        {
            "pelvis", "abdomen", "thorax", "head_neck", "left_upper_arm", "right_upper_arm",
            "left_forearm", "right_forearm", "left_hand", "right_hand"
        };

        private readonly IReadOnlyDictionary<string, Quaternion> _targets;
        private readonly Dictionary<string, float> _scale = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, Quaternion> _bias = new Dictionary<string, Quaternion>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _biasTorqueNm = new Dictionary<string, float>(StringComparer.Ordinal);

        public DeadliftSetupHold(IReadOnlyDictionary<string, Quaternion> logicalTargets)
        {
            _targets = logicalTargets ?? throw new ArgumentNullException(nameof(logicalTargets));
        }

        /// <summary>Scale on the capacity of one joint (default 1). Zero is the "no actuator" control.</summary>
        public void SetStrengthScale(string childId, float scale) => _scale[childId] = scale;

        public bool Enabled { get; set; } = true;

        /// <summary>
        /// How a static reference offset b is composed with the solved target q*.
        /// Right (historical, as GAM-50 R9): q* b, b on the child's current axes.
        /// Left (GAM-70 C5): b q*, so the drive's measured logical-frame error
        /// log(T q^-1) equals b itself at q = q*. The two coincide for hinges.
        /// </summary>
        public bool ComposeReferenceOnLeft { get; set; }

        /// <summary>
        /// Optional GAM-70 balance regulator. When set, its per-foot ankle
        /// offset (deg, about each ankle hinge axis) is added to that ankle's
        /// reference every tick. It only shapes references; this hold remains
        /// the single command source and PoweredJointController the single writer.
        /// </summary>
        public DeadliftBalanceRegulator Regulator { get; set; }

        private PhysicalAthleteRig _referenceRig;
        private float _appliedHandPullN = float.NaN;

        /// <summary>Optional A3 pretension servo; when set it selects the grounded reference's hand pull each tick.</summary>
        public DeadliftPretensionLoop Pretension { get; set; }

        /// <summary>The A3 pretension reference, when installed with <see cref="UseGroundedReference"/>.</summary>
        public DeadliftGroundedReference GroundedReference { get; private set; }

        /// <summary>
        /// GAM-70 A3: drive the static reference from a commanded hand pull.
        /// Each tick, if the accepted command changed, the reference torques are
        /// re-taken from the contact-consistent statics at that pull; the
        /// regulator's per-foot COP targets follow the same statics.
        /// </summary>
        public void UseGroundedReference(PhysicalAthleteRig rig, DeadliftGroundedReference reference)
        {
            _referenceRig = rig ?? throw new ArgumentNullException(nameof(rig));
            GroundedReference = reference ?? throw new ArgumentNullException(nameof(reference));
            _appliedHandPullN = float.NaN;
            if (Regulator != null)
                Regulator.FootCopTarget = reference.FootCop;
            ApplyGroundedReference();
        }

        private void ApplyGroundedReference()
        {
            if (GroundedReference == null || GroundedReference.HandPullN == _appliedHandPullN)
                return;
            SetStaticReference(_referenceRig, GroundedReference.DriveTorquesAxes());
            _appliedHandPullN = GroundedReference.HandPullN;
        }

        /// <summary>Per-joint static gravity reference offset angle (deg) after <see cref="ComputeStaticGravityBias"/>.</summary>
        public float BiasDegrees(string jointId) => _bias.TryGetValue(jointId, out Quaternion q) ? Quaternion.Angle(Quaternion.identity, q) : 0f;

        /// <summary>Magnitude of the gravity torque (N m, capacity-bounded) each bias compensates.</summary>
        public float BiasTorqueNm(string jointId) => _biasTorqueNm.TryGetValue(jointId, out float t) ? t : 0f;

        public void ComputeStaticGravityBias(PhysicalAthleteRig rig)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            _bias.Clear();
            _biasTorqueNm.Clear();
            Vector3 g = Physics.gravity;
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !_targets.ContainsKey(joint.Id))
                    continue;
                // Same scope as the accepted GAM-50 R9 term: hips, knees and the
                // two trunk joints. The ankles get none (they are the balance
                // actuator; a static ankle term was measured to saturate the
                // ankle drives at tick 10, run p8), nor do neck and arms.
                bool inScope = joint.Id.EndsWith("_thigh", StringComparison.Ordinal) || joint.Id.EndsWith("_shank", StringComparison.Ordinal) ||
                               joint.Id == "abdomen" || joint.Id == "thorax";
                if (!inScope)
                    continue;
                ConfigurableJoint cj = joint.Joint;
                Vector3 pivot = cj.transform.TransformPoint(cj.anchor);
                Vector3 x = cj.transform.TransformDirection(cj.axis).normalized;
                Vector3 y = Vector3.ProjectOnPlane(cj.transform.TransformDirection(cj.secondaryAxis), x).normalized;
                Vector3 z = Vector3.Cross(x, y);
                string side = joint.Id.StartsWith("left_", StringComparison.Ordinal) ? "left" : "right";
                bool leg = joint.Id.EndsWith("_thigh", StringComparison.Ordinal) || joint.Id.EndsWith("_shank", StringComparison.Ordinal) ||
                           joint.Id.EndsWith("_foot", StringComparison.Ordinal);

                Vector3 moment = Vector3.zero;
                void Add(string id, float share)
                {
                    Rigidbody b = rig.Segments[id].Body;
                    moment += Vector3.Cross(b.worldCenterOfMass - pivot, b.mass * share * g);
                }

                if (leg)
                {
                    foreach (string id in UpperBody)
                        Add(id, 0.5f);
                    if (joint.Id.EndsWith("_shank", StringComparison.Ordinal) || joint.Id.EndsWith("_foot", StringComparison.Ordinal))
                        Add(side + "_thigh", 1f);
                    if (joint.Id.EndsWith("_foot", StringComparison.Ordinal))
                        Add(side + "_shank", 1f);
                }
                else
                {
                    foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
                        if (InSubtree(recipe.Id, joint.Id))
                            Add(recipe.Id, 1f);
                }

                // Drive torque on the child equals K e. Free child side: K e = -G;
                // free parent side (grounded leg): K e = +G (GAM-50 R9 convention).
                Vector3 required = leg ? moment : -moment;
                Vector3 local = new Vector3(Vector3.Dot(required, x), Vector3.Dot(required, y), Vector3.Dot(required, z));
                if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                    local = new Vector3(local.x, 0f, 0f);
                float capacity = joint.Profile.Value.BaseCapacityNm * (_scale.TryGetValue(joint.Id, out float s) ? s : 1f);
                float magnitude = Mathf.Min(local.magnitude, capacity);
                _biasTorqueNm[joint.Id] = magnitude;
                float angleRad = magnitude / joint.Profile.Value.Spring;
                _bias[joint.Id] = local.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, local.normalized);
            }
        }

        /// <summary>Carries another hold's initial-condition gravity reference over unchanged (no new measurement).</summary>
        public void CopyStaticGravityBias(DeadliftSetupHold source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            _bias.Clear();
            _biasTorqueNm.Clear();
            foreach (KeyValuePair<string, Quaternion> b in source._bias) _bias[b.Key] = b.Value;
            foreach (KeyValuePair<string, float> t in source._biasTorqueNm) _biasTorqueNm[t.Key] = t.Value;
        }

        /// <summary>
        /// GAM-70 C2 ablation control: removes the already-computed static
        /// reference of every joint <paramref name="keep"/> rejects. A kept
        /// reference is never recomputed, rescaled or re-measured.
        /// </summary>
        public void RetainStaticGravityBias(Func<string, bool> keep)
        {
            if (keep == null)
                throw new ArgumentNullException(nameof(keep));
            var drop = new List<string>();
            foreach (string id in _bias.Keys)
                if (!keep(id))
                    drop.Add(id);
            foreach (string id in drop)
            {
                _bias.Remove(id);
                _biasTorqueNm.Remove(id);
            }
        }

        /// <summary>
        /// GAM-70 C4: installs a fixed reference from an explicitly solved,
        /// contact-consistent joint torque set (N m on each child's joint axes,
        /// the same frame and sign as <see cref="ComputeStaticGravityBias"/>),
        /// replacing any previous reference. Offset = tau / K about the torque
        /// direction; hinge joints use the axis component only. No capacity
        /// clamp: the caller must have proven the set within the ceilings.
        /// </summary>
        public void SetStaticReference(PhysicalAthleteRig rig, IReadOnlyDictionary<string, Vector3> torqueAxesNm)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            if (torqueAxesNm == null)
                throw new ArgumentNullException(nameof(torqueAxesNm));
            _bias.Clear();
            _biasTorqueNm.Clear();
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !_targets.ContainsKey(joint.Id) || !torqueAxesNm.TryGetValue(joint.Id, out Vector3 local))
                    continue;
                if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                    local = new Vector3(local.x, 0f, 0f);
                _biasTorqueNm[joint.Id] = local.magnitude;
                float angleRad = local.magnitude / joint.Profile.Value.Spring;
                _bias[joint.Id] = local.sqrMagnitude < 1e-12f ? Quaternion.identity : Quaternion.AngleAxis(angleRad * Mathf.Rad2Deg, local.normalized);
            }
        }

        /// <summary>Joints that currently carry a static reference offset.</summary>
        public IEnumerable<string> StaticGravityBiasJoints => _bias.Keys;

        /// <summary>The offset quaternion (logical frame) applied after the joint's solved target.</summary>
        public Quaternion StaticGravityBias(string jointId) => _bias.TryGetValue(jointId, out Quaternion q) ? q : Quaternion.identity;

        private static bool InSubtree(string segmentId, string rootId)
        {
            string current = segmentId;
            while (current != null)
            {
                if (current == rootId)
                    return true;
                string parent = null;
                foreach (PhysicalSegmentRecipe r in PhysicalAthleteDefinition.Segments)
                    if (r.Id == current) { parent = r.ParentId; break; }
                current = parent;
            }
            return false;
        }

        public void PrepareCommands(
            PhysicalObservation previousObservation,
            SimulationTime time,
            PlayerIntentFrame intent,
            IPhysicalAthleteJointCommandSink sink)
        {
            Pretension?.Update();
            ApplyGroundedReference();
            Regulator?.Update();
            foreach (KeyValuePair<string, Quaternion> target in _targets)
            {
                float scale = _scale.TryGetValue(target.Key, out float s) ? s : 1f;
                Quaternion q = _bias.TryGetValue(target.Key, out Quaternion b)
                    ? (ComposeReferenceOnLeft ? b * target.Value : target.Value * b)
                    : target.Value;
                if (Regulator != null && (target.Key == "left_foot" || target.Key == "right_foot"))
                    q *= Quaternion.AngleAxis(Regulator.OffsetDegFor(target.Key), Vector3.right);
                sink.ApplyCommand(target.Key, new JointCommand(q, Vector3.zero, Enabled ? 1f : 0f, scale), time.Tick);
            }
        }
    }
}
