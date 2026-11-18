using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 Gate 4: which writer currently owns the static joint reference.
    /// The grounded pretension reference is only valid while the plates carry
    /// the bar; the airborne load schedule owns it after the measured
    /// separation. The owner is explicit so a trace can prove who wrote the
    /// command and so no stale writer can silently overwrite it.
    /// </summary>
    public enum DeadliftReferenceOwner : byte
    {
        None,
        GroundedPretension,
        AirborneBarBreak,
        /// <summary>GAM-70 Gate 5: the measured hand-load reference owns the static reference from the player's pull on.</summary>
        PullHandLoad
    }

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

        internal readonly Dictionary<string, Quaternion> PreparationOffsets = new Dictionary<string, Quaternion>(StringComparer.Ordinal);

        /// <summary>Plant-identification hook: extra target offsets (child joint axes) per joint. Empty in judged attempts.</summary>
        public readonly Dictionary<string, Quaternion> IdentificationOffsets = new Dictionary<string, Quaternion>(StringComparer.Ordinal);

        private PhysicalAthleteRig _referenceRig;
        private float _appliedHandPullN = float.NaN;

        /// <summary>Optional A3 pretension servo; when set it selects the grounded reference's hand pull each tick.</summary>
        public DeadliftPretensionLoop Pretension { get; set; }

        /// <summary>
        /// GAM-70 Gate 3: bounded player pull-intent reference shaper. It adds
        /// hip/knee/trunk extension to the solved target; it is not a joint
        /// writer and never touches the bar. Null on the grounded A2/A3 hold.
        /// </summary>
        public DeadliftPullController Pull { get; set; }

        /// <summary>
        /// GAM-70 Gate 6: the ascent reference path. While the pull controller
        /// reports lift progress s &gt; 0, each joint's solved target is taken
        /// from the path at s instead of the setup pose; every other reference
        /// term composes on top exactly as at the setup.
        /// </summary>
        public DeadliftLiftPath LiftPath { get; set; }

        /// <summary>Regulator integral leak applied from the Lifting phase on (0 = pure integrator, the accepted hold law).</summary>
        public float MovingPhaseIntegralLeakSeconds { get; set; }

        /// <summary>From the Lifting phase on, the regulator observes the athlete+bar COM (the bar is airborne and gripped).</summary>
        public bool MovingPhaseSystemCom { get; set; }

        public float PullHipExtensionDeg { get; private set; }
        public float PullKneeExtensionDeg { get; private set; }
        public float PullTrunkExtensionDeg { get; private set; }

        /// <summary>
        /// GAM-70 Gate 3: explicit two-contact-mode load schedule. When set, the
        /// static reference and the regulator's per-foot COP targets follow the
        /// measured plate-unload fraction from the grounded C4 solution to the
        /// separated-bar boundary solution. Never set in A2/A3.
        /// </summary>
        public DeadliftBarBreakReference BarBreakReference { get; private set; }
        public float BarBreakFraction { get; private set; }
        private float _appliedBarBreakFraction = float.NaN;

        /// <summary>
        /// GAM-70 Gate 4 A/B control only: reproduces the exact-HEAD behavior
        /// in which the grounded pretension integral and the grounded reference
        /// writer keep running after the measured separation and can overwrite
        /// the airborne schedule. Default false is the judged path: one
        /// explicit, physically triggered ownership handoff.
        /// </summary>
        public bool ContinueGroundedReferenceAfterSeparation { get; set; }

        /// <summary>
        /// True once the measured, confirmed separation handed the static
        /// reference to the airborne load schedule. Latched; never cleared.
        /// </summary>
        public bool AirborneClaimed => _airborneClaimed;
        private bool _airborneClaimed;

        private readonly Dictionary<string, Vector3> _appliedReferenceAxes = new Dictionary<string, Vector3>(StringComparer.Ordinal);

        /// <summary>
        /// GAM-70 Gate 4: the writer that currently owns the static reference,
        /// and the reference torque set it last applied (N m on each child's
        /// joint axes: X flexion, Z the ankle roll channel). Both are
        /// read-only traces; they never feed the controller.
        /// </summary>
        public DeadliftReferenceOwner ReferenceOwner { get; private set; }
        public IReadOnlyDictionary<string, Vector3> AppliedReferenceAxes => _appliedReferenceAxes;
        public Vector3 AppliedReferenceAxesFor(string jointId) =>
            _appliedReferenceAxes.TryGetValue(jointId, out Vector3 v) ? v : Vector3.zero;

        /// <summary>Writes of the grounded reference and of the airborne schedule seen so far (Gate 4 A/B trace).</summary>
        public int GroundedReferenceWrites { get; private set; }
        public int AirborneReferenceWrites { get; private set; }

        public void UseBarBreakReference(DeadliftBarBreakReference reference)
        {
            BarBreakReference = reference ?? throw new ArgumentNullException(nameof(reference));
            _appliedBarBreakFraction = float.NaN;
            if (Regulator != null)
            {
                Regulator.FootCopTarget = id =>
                    AirborneOwnsReference
                        ? reference.FootCop(id, BarBreakFraction)
                        : (GroundedReference != null ? GroundedReference.FootCop(id) : reference.FootCop(id, 0f));
                Regulator.FootLoadRatio = id =>
                {
                    if (AirborneOwnsReference)
                        return reference.FootNormalN(id, BarBreakFraction) / Mathf.Max(reference.FootNormalN(id, 0f), 1f);
                    return GroundedReference != null
                        ? GroundedReference.FootNormalN(id) / Mathf.Max(GroundedReference.FootNormalAtZeroN(id), 1f)
                        : 1f;
                };
            }
        }

        /// <summary>
        /// The airborne schedule owns the COP target and the load-gain
        /// multiplier from the measured handoff on. The legacy f-test branch is
        /// the exact-HEAD A/B control only; it is unreachable in the judged
        /// path because the owner is latched before the fraction ramps.
        /// </summary>
        private bool AirborneOwnsReference =>
            (_airborneClaimed && !ContinueGroundedReferenceAfterSeparation) ||
            (ContinueGroundedReferenceAfterSeparation && Pull != null && Pull.LoadScheduleFraction > 0.0005f);

        /// <summary>
        /// GAM-70 Gate 4: explicit reference-ownership handoff. Called only
        /// after <see cref="DeadliftPullController"/> has measured and confirmed
        /// the bilateral plate separation. Once claimed, the grounded
        /// pretension integral is dropped (it is undefined with the plates
        /// unloaded) and the grounded reference can never be applied again;
        /// the airborne load schedule owns the static reference for the rest of
        /// the attempt. No force, body or bar is written here.
        /// </summary>
        public void ClaimAirborneReference()
        {
            if (_airborneClaimed)
                return;
            _airborneClaimed = true;
            Pretension = null;
            _appliedHandPullN = float.NaN;
            _appliedBarBreakFraction = float.NaN;
        }

        /// <summary>
        /// GAM-70 Gate 5 A/B control P1: stop the A3 pretension integral when the
        /// player's pull begins, without any other change (legacy airborne schedule kept).
        /// </summary>
        public bool StopPretensionOnPull { get; set; }

        /// <summary>
        /// GAM-70 Gate 5: the measured hand-load reference. When installed, the
        /// player's pull (phase leaves Grounded) hands the static reference and
        /// the regulator's COP targets from the A3 pretension servo to this
        /// reference, fed by the low-pass filtered measured per-hand vertical
        /// load. The airborne load schedule is not used on this path: the same
        /// statics family is valid on both sides of separation.
        /// </summary>
        public DeadliftHandLoadReference HandLoadReference { get; private set; }
        public bool HandLoadClaimed { get; private set; }
        public float HandLoadFilteredN { get; private set; } = float.NaN;
        private Func<float> _measuredPerHandN;
        private float _handLoadFilterSeconds = 0.05f;

        public void UseHandLoadReference(DeadliftHandLoadReference reference, Func<float> measuredPerHandUpN, float filterSeconds = 0.05f)
        {
            HandLoadReference = reference ?? throw new ArgumentNullException(nameof(reference));
            _measuredPerHandN = measuredPerHandUpN ?? throw new ArgumentNullException(nameof(measuredPerHandUpN));
            if (!(filterSeconds > 0f))
                throw new ArgumentOutOfRangeException(nameof(filterSeconds));
            _handLoadFilterSeconds = filterSeconds;
        }

        /// <summary>
        /// Explicit hand-load ownership handoff, at the first tick the pull
        /// controller leaves Grounded. The pretension servo is dropped (its
        /// setpoint contradicts the player's intent), and the filter starts at
        /// the hand load the applied reference already assumes, so the handoff
        /// itself is continuous.
        /// </summary>
        private void ClaimHandLoadReference()
        {
            HandLoadClaimed = true;
            Pretension = null;
            HandLoadFilteredN = GroundedReference != null ? GroundedReference.HandPullN : _measuredPerHandN();
            if (Regulator != null)
            {
                DeadliftHandLoadReference r = HandLoadReference;
                Regulator.FootCopTarget = r.FootCop;
                Regulator.FootLoadRatio = id => r.FootNormalN(id) / Mathf.Max(r.FootNormalAtZeroN(id), 1f);
            }
        }

        private void ApplyHandLoadReference()
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            float alpha = Mathf.Clamp01(dt / _handLoadFilterSeconds);
            float measured = _measuredPerHandN();
            if (!float.IsNaN(measured))
                HandLoadFilteredN += alpha * (measured - HandLoadFilteredN);
            // Returning the bar: once the lowered path is back at the floor posture,
            // the admitted hand load ramps to zero so the plates take the bar (the
            // lifter lets go of the pull). Until then it is the measured load.
            // Only once the plates are measured to carry load again: relaxing while
            // the bar still hangs from the hands removed its feedforward and was
            // measured to topple the athlete forward (Gate 7 run 2).
            float plateN = Pull != null && !float.IsNaN(Pull.PlateLeftN) && !float.IsNaN(Pull.PlateRightN) ? Pull.PlateLeftN + Pull.PlateRightN : 0f;
            bool returning = Pull != null && (Pull.Phase == DeadliftPullPhase.Returned ||
                                              (Pull.Phase == DeadliftPullPhase.Lowering && Pull.LiftProgress01 <= 0f && plateN > ReturnContactPlateN));
            _returnCeilingN = returning
                ? Mathf.MoveTowards(_returnCeilingN, 0f, ReturnRelaxNPerSecond * dt)
                : float.PositiveInfinity;
            if (returning && float.IsInfinity(_returnCeilingN))
                _returnCeilingN = HandLoadFilteredN;
            HandLoadReference.Observe(Mathf.Min(HandLoadFilteredN, _returnCeilingN));
            SetStaticReference(_referenceRig, HandLoadReference.DriveTorquesAxes());
            ReferenceOwner = DeadliftReferenceOwner.PullHandLoad;
            HandLoadReferenceWrites++;
        }

        public int HandLoadReferenceWrites { get; private set; }
        /// <summary>Rate at which the admitted hand load relaxes when the bar is being set down (N per hand per second).</summary>
        public float ReturnRelaxNPerSecond { get; set; } = 400f;
        /// <summary>Plate load that marks the lowered bar as resting on the platform again (both plates, N).</summary>
        public float ReturnContactPlateN { get; set; } = 50f;
        private float _returnCeilingN = float.PositiveInfinity;

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
            {
                Regulator.FootCopTarget = reference.FootCop;
                Regulator.FootLoadRatio = id => reference.FootNormalN(id) / reference.FootNormalAtZeroN(id);
            }
            ApplyGroundedReference();
        }

        private void ApplyGroundedReference()
        {
            if (GroundedReference == null || GroundedReference.HandPullN == _appliedHandPullN)
                return;
            SetStaticReference(_referenceRig, GroundedReference.DriveTorquesAxes());
            _appliedHandPullN = GroundedReference.HandPullN;
            ReferenceOwner = DeadliftReferenceOwner.GroundedPretension;
            GroundedReferenceWrites++;
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
            _appliedReferenceAxes.Clear();
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
                _appliedReferenceAxes[joint.Id] = local.sqrMagnitude < 1e-12f ? Vector3.zero : local * (magnitude / local.magnitude);
            }
        }

        /// <summary>Carries another hold's initial-condition gravity reference over unchanged (no new measurement).</summary>
        public void CopyStaticGravityBias(DeadliftSetupHold source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            _bias.Clear();
            _biasTorqueNm.Clear();
            _appliedReferenceAxes.Clear();
            foreach (KeyValuePair<string, Quaternion> b in source._bias) _bias[b.Key] = b.Value;
            foreach (KeyValuePair<string, float> t in source._biasTorqueNm) _biasTorqueNm[t.Key] = t.Value;
            foreach (KeyValuePair<string, Vector3> t in source._appliedReferenceAxes) _appliedReferenceAxes[t.Key] = t.Value;
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
                _appliedReferenceAxes.Remove(id);
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
            _appliedReferenceAxes.Clear();
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (!joint.Profile.HasValue || !_targets.ContainsKey(joint.Id) || !torqueAxesNm.TryGetValue(joint.Id, out Vector3 local))
                    continue;
                if (joint.Recipe.Kind == PhysicalJointKind.Hinge)
                    local = new Vector3(local.x, 0f, joint.SecondaryProfile.HasValue ? local.z : 0f);
                _appliedReferenceAxes[joint.Id] = local;
                _biasTorqueNm[joint.Id] = local.magnitude;
                // Per-axis offset: a hinge's powered roll channel has its own spring.
                float kx = joint.Profile.Value.Spring;
                float ks = joint.SecondaryProfile.HasValue ? joint.SecondaryProfile.Value.Spring : kx;
                Vector3 rotationRad = new Vector3(local.x / kx, local.y / ks, local.z / ks);
                _bias[joint.Id] = rotationRad.sqrMagnitude < 1e-16f ? Quaternion.identity
                    : Quaternion.AngleAxis(rotationRad.magnitude * Mathf.Rad2Deg, rotationRad.normalized);
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
            if (Pull != null)
            {
                Pull.Update(intent);
                PullHipExtensionDeg = Pull.HipExtensionDeg;
                PullKneeExtensionDeg = Pull.KneeExtensionDeg;
                PullTrunkExtensionDeg = Pull.TrunkExtensionDeg;
            }
            else
            {
                PullHipExtensionDeg = PullKneeExtensionDeg = PullTrunkExtensionDeg = 0f;
            }

            if (HandLoadReference != null && Pull != null && !HandLoadClaimed && Pull.Phase != DeadliftPullPhase.Grounded)
                ClaimHandLoadReference();

            if (HandLoadClaimed)
            {
                ApplyHandLoadReference();
            }
            else
            {
            // Explicit, physically triggered handoff: the pull controller sets
            // Separated only from a measured, confirmed plate separation.
            if (Pull != null && Pull.Phase == DeadliftPullPhase.Separated &&
                !ContinueGroundedReferenceAfterSeparation)
                ClaimAirborneReference();

            if (!_airborneClaimed || ContinueGroundedReferenceAfterSeparation)
            {
                bool pulling = Pull != null && Pull.Phase != DeadliftPullPhase.Grounded;
                if (!(StopPretensionOnPull && pulling))
                    Pretension?.Update();
                ApplyGroundedReference();
            }

            if (BarBreakReference != null && Pull != null)
            {
                // Command-driven load schedule: the player's applied pull, not
                // the measured plate unload, selects the reference blend. The
                // measured plate force is still the transition evidence; using
                // it as the schedule input would close a positive feedback loop.
                BarBreakFraction = Pull.LoadScheduleFraction;
                bool airborne = AirborneOwnsReference;
                bool scheduleWritesEveryTick = _airborneClaimed && !ContinueGroundedReferenceAfterSeparation;
                if (BarBreakFraction <= 0.0005f && !airborne)
                {
                    if (!float.IsNaN(_appliedBarBreakFraction))
                    {
                        SetStaticReference(_referenceRig, GroundedReference.DriveTorquesAxes());
                        _appliedBarBreakFraction = float.NaN;
                        ReferenceOwner = DeadliftReferenceOwner.GroundedPretension;
                        GroundedReferenceWrites++;
                    }
                }
                else if (scheduleWritesEveryTick || float.IsNaN(_appliedBarBreakFraction) || Mathf.Abs(BarBreakFraction - _appliedBarBreakFraction) > 0.001f)
                {
                    SetStaticReference(_referenceRig, BarBreakReference.Torques(BarBreakFraction));
                    _appliedBarBreakFraction = BarBreakFraction;
                    ReferenceOwner = DeadliftReferenceOwner.AirborneBarBreak;
                    AirborneReferenceWrites++;
                }
            }
            }
            if (Regulator != null && Pull != null && Pull.Phase >= DeadliftPullPhase.Lifting)
            {
                Regulator.IntegralLeakSeconds = MovingPhaseIntegralLeakSeconds;
                if (MovingPhaseSystemCom)
                    Regulator.CarriedBar = Pull.BarBody;
            }
            Regulator?.Update();
            float progress = LiftPath != null && Pull != null ? Pull.LiftProgress01 : 0f;
            foreach (KeyValuePair<string, Quaternion> target in _targets)
            {
                float scale = _scale.TryGetValue(target.Key, out float s) ? s : 1f;
                Quaternion baseTarget = progress > 0f && LiftPath.Has(target.Key) ? LiftPath.Target(target.Key, progress) : target.Value;
                Quaternion q = _bias.TryGetValue(target.Key, out Quaternion b)
                    ? (ComposeReferenceOnLeft ? b * baseTarget : baseTarget * b)
                    : baseTarget;
                if (Regulator != null && (target.Key == "left_foot" || target.Key == "right_foot"))
                    q *= Quaternion.AngleAxis(Regulator.OffsetDegFor(target.Key), Vector3.right) *
                         Quaternion.AngleAxis(Regulator.RollOffsetDegFor(target.Key), Vector3.forward);
                if (Regulator != null && (target.Key == "left_thigh" || target.Key == "right_thigh"))
                    q *= Quaternion.AngleAxis(Regulator.HipOffsetDegFor(target.Key == "left_thigh" ? "left_foot" : "right_foot"), Vector3.up);
                if (Pull != null && (target.Key == "left_thigh" || target.Key == "right_thigh"))
                    q *= Quaternion.AngleAxis(PullHipExtensionDeg, Vector3.right);
                if (Pull != null && (target.Key == "left_shank" || target.Key == "right_shank"))
                    q *= Quaternion.AngleAxis(-PullKneeExtensionDeg, Vector3.right);
                if (Pull != null && (target.Key == "abdomen" || target.Key == "thorax"))
                    q *= Quaternion.AngleAxis(PullTrunkExtensionDeg, Vector3.right);
                if (PreparationOffsets.TryGetValue(target.Key, out Quaternion preparation))
                    q *= preparation;
                if (IdentificationOffsets.TryGetValue(target.Key, out Quaternion idq))
                    q *= idq;
                sink.ApplyCommand(target.Key, new JointCommand(q, Vector3.zero, Enabled ? 1f : 0f, scale), time.Tick);
            }
        }
    }
}
