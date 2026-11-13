using System;
using System.Globalization;
using System.Text;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Deadlift.Unity
{
    /// <summary>
    /// GAM-70 owner-approved deadlift-local ankle inversion/eversion DOF.
    ///
    /// Evidence that required it (docs/evidence/GAM-70/a3-grounded-pretension.txt):
    /// with the canonical hinge ankle and knee, foot roll about the sole's long
    /// axis is unactuated below the hip; the frozen baseline drifts 37 mm across
    /// the sole in 10 ticks; four reference-shaping regulators were measured and
    /// rejected. The canonical rig, squat and bench are unchanged: this is enabled
    /// only by the deadlift plant, after build and before tick 0.
    ///
    /// Declared game approximation (not measured physiology):
    ///   range +/-15 deg (symmetric Unity limit; inside typical eversion ROM,
    ///   the smaller side of the subtalar range);
    ///   ceiling 60 N m (order of published inversion/eversion strength);
    ///   spring and damper = ankle-flexion family x 60/450, so the roll channel
    ///   saturates at the same error angle (11.2 deg) as ankle flexion.
    ///
    /// Topology version GAM70_DEADLIFT_ANKLE_ROLL_V1 is validated before the
    /// first physics tick and can never be redefined on a running plant.
    /// </summary>
    public static class DeadliftAnkleRoll
    {
        public const float LimitDegrees = 15f;
        public const float CeilingNm = 60f;
        public const string TopologyVersion = PhysicalAthleteRig.AnkleRollTopologyVersion;
        public const string SecondaryProfileId = "ankle_roll";

        public static JointFamilyProfile Profile
        {
            get
            {
                JointFamilyProfile ankle = default;
                foreach (JointFamilyProfile p in PoweredJointController.FamilyProfiles)
                    if (p.Id == "ankle") ankle = p;
                if (ankle.Id == null)
                    throw new InvalidOperationException("ankle family profile missing");
                float k = CeilingNm / ankle.BaseCapacityNm;
                return new JointFamilyProfile(SecondaryProfileId, ankle.Spring * k, ankle.Damper * k, CeilingNm, ankle.MaxTargetRateRadS);
            }
        }

        /// <summary>
        /// Enables the variant once, before tick 0, then validates the actual
        /// physics topology. Throws rather than silently changing a running plant.
        /// </summary>
        public static void Enable(PhysicalAthleteRig rig)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            if (rig.PoweredController == null)
                throw new InvalidOperationException("The physical rig has no powered joint controller.");
            if (rig.HasPhysicsStepped)
                throw new InvalidOperationException("The deadlift ankle-roll variant must be enabled before the first physics tick.");
            if (rig.AnkleRollLimitDegrees.HasValue)
            {
                // Repeated activation with the same declared variant is a
                // no-op, never a second write to the joint topology.
                if (Mathf.Abs(rig.AnkleRollLimitDegrees.Value - LimitDegrees) > 0.0001f)
                    throw new InvalidOperationException("The deadlift ankle-roll variant is already active with a different limit.");
                if (rig.PoweredController.SecondaryChannelCount != 2)
                    throw new InvalidOperationException("The deadlift ankle-roll variant is only partly installed.");
                Validate(rig);
                return;
            }
            rig.EnableAnkleRoll(LimitDegrees);
            JointFamilyProfile p = Profile;
            rig.PoweredController.EnableSecondaryChannel("left_foot", p);
            rig.PoweredController.EnableSecondaryChannel("right_foot", p);
            Validate(rig);
        }

        /// <summary>
        /// Independently re-checks the executed topology. Called at enable time
        /// and again by the acceptance tests while the plant is running.
        /// </summary>
        public static TopologyReceipt Validate(PhysicalAthleteRig rig)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            if (!rig.AnkleRollLimitDegrees.HasValue)
                throw new InvalidOperationException("The deadlift ankle-roll topology is not active.");
            if (Mathf.Abs(rig.AnkleRollLimitDegrees.Value - LimitDegrees) > 0.001f)
                throw new InvalidOperationException(
                    $"Ankle-roll limit {rig.AnkleRollLimitDegrees.Value:F3} deg does not match the declared {LimitDegrees:F3} deg.");
            rig.ValidateAnkleRollTopology();

            PoweredJointController controller = rig.PoweredController;
            if (controller == null)
                throw new InvalidOperationException("The physical rig has no powered joint controller.");
            if (controller.SecondaryChannelCount != 2)
                throw new InvalidOperationException($"Expected 2 powered secondary channels, found {controller.SecondaryChannelCount}.");

            JointFamilyProfile expected = Profile;
            var receipt = new TopologyReceipt();
            foreach (PoweredJointController.PoweredJointRuntime joint in controller.Joints)
            {
                bool ankle = joint.Id == "left_foot" || joint.Id == "right_foot";
                if (!ankle)
                {
                    if (joint.SecondaryProfile.HasValue)
                        throw new InvalidOperationException($"Canonical joint '{joint.Id}' unexpectedly carries a secondary channel.");
                    continue;
                }

                if (!joint.SecondaryProfile.HasValue)
                    throw new InvalidOperationException($"Ankle '{joint.Id}' is missing its powered secondary channel.");
                JointFamilyProfile sp = joint.SecondaryProfile.Value;
                if (sp.Id != SecondaryProfileId ||
                    Mathf.Abs(sp.BaseCapacityNm - CeilingNm) > 0.001f ||
                    Mathf.Abs(sp.Spring - expected.Spring) > 0.001f ||
                    Mathf.Abs(sp.Damper - expected.Damper) > 0.001f ||
                    Mathf.Abs(sp.MaxTargetRateRadS - expected.MaxTargetRateRadS) > 0.0001f)
                    throw new InvalidOperationException($"Ankle '{joint.Id}' secondary profile does not match the declared game approximation.");

                ConfigurableJoint cj = joint.Joint;
                if (cj.angularZMotion != ConfigurableJointMotion.Limited ||
                    cj.angularYMotion != ConfigurableJointMotion.Locked ||
                    Mathf.Abs(cj.angularZLimit.limit - LimitDegrees) > 0.001f)
                    throw new InvalidOperationException($"Ankle '{joint.Id}' physics topology does not match the declared Z channel.");

                PhysicalAthleteRig.SegmentRuntime foot = rig.Segments[joint.Id];
                if (foot.Body.isKinematic || !foot.Body.useGravity ||
                    foot.Body.GetComponent<ConstantForce>() != null)
                    throw new InvalidOperationException($"Ankle '{joint.Id}' foot is not a free dynamic gravity body.");

                // Runtime axis check in world space: X stays the horizontal
                // flexion axis, Y stays vertical/axial and Z is the sole's long axis.
                Vector3 x = cj.transform.TransformDirection(cj.axis).normalized;
                Vector3 y = cj.transform.TransformDirection(cj.secondaryAxis).normalized;
                Vector3 z = Vector3.Cross(x, y).normalized;
                Vector3 sole = Vector3.ProjectOnPlane(foot.Body.transform.forward, Vector3.up).normalized;
                if (Mathf.Abs(Vector3.Dot(x, Vector3.up)) > 0.02f ||
                    Vector3.Dot(y, Vector3.up) < 0.98f ||
                    Vector3.Dot(z, sole) < 0.999f)
                    throw new InvalidOperationException(
                        $"Ankle '{joint.Id}' world axes are not (horizontal lateral, vertical, sole long axis): x.up {Vector3.Dot(x, Vector3.up):F4}, y.up {Vector3.Dot(y, Vector3.up):F4}, z.sole {Vector3.Dot(z, sole):F4}.");

                receipt.LeftOrRight.Add(joint.Id + " zLimit " + cj.angularZLimit.limit.ToString("F3", CultureInfo.InvariantCulture) +
                    " deg, drive ceiling " + sp.BaseCapacityNm.ToString("F3", CultureInfo.InvariantCulture) +
                    " N m, K " + sp.Spring.ToString("F3", CultureInfo.InvariantCulture) +
                    ", D " + sp.Damper.ToString("F3", CultureInfo.InvariantCulture) +
                    ", axis x " + x.ToString("F4") + " y " + y.ToString("F4") + " z " + z.ToString("F4") +
                    ", sole dot " + Vector3.Dot(z, sole).ToString("F6", CultureInfo.InvariantCulture));
            }

            if (receipt.LeftOrRight.Count != 2)
                throw new InvalidOperationException("Both ankle roll channels were not validated.");

            if (controller.Joints.Count != 15 || rig.Joints.Count != 15 || rig.Segments.Count != 16)
                throw new InvalidOperationException("The variant changed the canonical 16-body/15-joint graph.");
            return receipt;
        }

        /// <summary>
        /// After the first command prime, verifies that the actual PhysX drive
        /// really carries the declared secondary authority through the existing
        /// PoweredJointController writer (no second actuator path).
        /// </summary>
        public static void ValidateAppliedAuthority(PhysicalAthleteRig rig)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            Validate(rig);
            rig.PoweredController.ValidateAppliedDrives();
            JointFamilyProfile expected = Profile;
            foreach (string footId in new[] { "left_foot", "right_foot" })
            {
                PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(footId);
                JointFamilyProfile sp = joint.SecondaryProfile.Value;
                JointDrive drive = joint.Joint.angularYZDrive;
                if (Mathf.Abs(drive.positionSpring - expected.Spring) > 0.001f ||
                    Mathf.Abs(drive.positionDamper - expected.Damper) > 0.001f ||
                    drive.useAcceleration ||
                    drive.maximumForce <= 0f || drive.maximumForce > CeilingNm + 0.001f)
                    throw new InvalidOperationException(
                        $"Ankle '{footId}' applied Z drive is not the declared {CeilingNm:F1} N m finite authority: spring {drive.positionSpring:F3}, damper {drive.positionDamper:F3}, max {drive.maximumForce:F3}.");
                if (joint.Joint.angularXDrive.maximumForce <= 0f)
                    throw new InvalidOperationException($"Ankle '{footId}' lost its canonical X drive authority.");
                if (sp.Id != SecondaryProfileId)
                    throw new InvalidOperationException($"Ankle '{footId}' secondary profile id changed.");
            }
        }

        /// <summary>Compact receipt of the executed variant; schema GAM70_ANKLE_ROLL_TOPOLOGY_V1.</summary>
        public sealed class TopologyReceipt
        {
            public string Version => TopologyVersion;
            public System.Collections.Generic.List<string> LeftOrRight { get; } = new System.Collections.Generic.List<string>();

            public string Describe()
            {
                var b = new StringBuilder();
                b.AppendLine("GAM70_ANKLE_ROLL_TOPOLOGY_V1: 16 bodies / 15 joints / 2 powered secondary channels");
                foreach (string line in LeftOrRight)
                    b.AppendLine("  " + line);
                return b.ToString();
            }
        }
    }
}
