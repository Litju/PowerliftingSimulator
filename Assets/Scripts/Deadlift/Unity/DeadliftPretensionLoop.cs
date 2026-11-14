using System;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 A3 pretension servo. With both feet and both rigidly gripped
    /// hands on grounded supports the hand force is statically indeterminate
    /// from joint torques alone, so the contact-consistent feedforward
    /// (<see cref="DeadliftGroundedReference"/>) cannot set it open loop
    /// (measured: commanding 2H = 368 N transferred 448 N and kept creeping
    /// until the bar lifted). This loop closes on the measured hand-bar
    /// vertical force, the quantity a lifter feels in the hands:
    ///   H_cmd &lt;- clamp(H_cmd + Ki (H_target - H_measured), [0, H_max]),
    /// and feeds H_cmd to the reference. It only selects which feasible C4
    /// reference is applied; it never writes a force, body or joint.
    /// H_measured is the mean upward force of the attached hands on the bar
    /// (Joint.currentForce, validated contract: force on the hand, world frame,
    /// last substep), so a hand that is not attached contributes nothing.
    /// </summary>
    public sealed class DeadliftPretensionLoop
    {
        private readonly Func<float> _measuredPerHandN;
        private readonly DeadliftGroundedReference _reference;

        public DeadliftPretensionLoop(DeadliftGroundedReference reference, Func<float> measuredPerHandUpwardForceN)
        {
            _reference = reference ?? throw new ArgumentNullException(nameof(reference));
            _measuredPerHandN = measuredPerHandUpwardForceN ?? throw new ArgumentNullException(nameof(measuredPerHandUpwardForceN));
        }

        /// <summary>Integral gain per tick (dimensionless: N of command per N of error).</summary>
        public float KiPerTick { get; set; } = 0.05f;

        public float TargetPerHandN { get; private set; }
        public float LastMeasuredPerHandN { get; private set; }

        /// <summary>Accepts a target in [0, reference maximum]; otherwise rejects and keeps the previous target.</summary>
        public bool TrySetTarget(float perHandN)
        {
            if (float.IsNaN(perHandN) || perHandN < 0f || perHandN > _reference.MaximumHandPullN)
                return false;
            TargetPerHandN = perHandN;
            return true;
        }

        public void Update()
        {
            LastMeasuredPerHandN = _measuredPerHandN();
            float next = Mathf.Clamp(_reference.HandPullN + KiPerTick * (TargetPerHandN - LastMeasuredPerHandN), 0f, _reference.MaximumHandPullN);
            _reference.TryCommandHandPull(next);
        }
    }
}
