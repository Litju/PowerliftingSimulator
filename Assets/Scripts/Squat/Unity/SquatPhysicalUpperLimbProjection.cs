using System;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    /// <summary>
    /// One arm's physical joint targets, together with what the projection
    /// could not deliver.
    /// </summary>
    public readonly struct PhysicalUpperLimbTargets
    {
        public PhysicalUpperLimbTargets(
            Quaternion upperArm,
            Quaternion forearm,
            Quaternion hand,
            float flexionDeg,
            float handPositionResidualM,
            float handOrientationResidualDeg)
        {
            UpperArm = upperArm;
            Forearm = forearm;
            Hand = hand;
            FlexionDeg = flexionDeg;
            HandPositionResidualM = handPositionResidualM;
            HandOrientationResidualDeg = handOrientationResidualDeg;
        }

        /// <summary>Logical shoulder target.</summary>
        public Quaternion UpperArm { get; }

        /// <summary>Logical elbow target. Flexion about the hinge axis only.</summary>
        public Quaternion Forearm { get; }

        /// <summary>Logical wrist target.</summary>
        public Quaternion Hand { get; }

        public float FlexionDeg { get; }
        public float HandPositionResidualM { get; }
        public float HandOrientationResidualDeg { get; }
    }

    /// <summary>
    /// Projects the accepted GAM-10 upper-limb task space onto what the
    /// physical arm can actually hold.
    ///
    /// The reference preview poses a fully articulated visual rig, so it is
    /// free to give the forearm any orientation it likes; it picks the humerus
    /// roll for appearance and then derives the forearm from the hand. Feeding
    /// those bone rotations straight into the physical joints asked a one-axis
    /// elbow for a rotation whose axis was only about half aligned with its
    /// hinge, and the elbow sat against its limit 309 mm from the bar.
    ///
    /// The accepted shoulder, elbow and hand positions come from the preview's
    /// own two-bone solve, so a shoulder plus a one-axis elbow can reach them
    /// by construction. This takes those positions and the accepted palm as
    /// the authority, orients the humerus so the elbow's hinge axis lies on the
    /// arm plane (which is what a real shoulder does), and leaves the elbow a
    /// single flexion angle. Measured across the qualification phases that
    /// lands the hand within 15 mm with 26 deg of elbow limit margin, against a
    /// 7.6 mm floor for a fully free three-axis elbow.
    ///
    /// No physical joint gains a degree of freedom here.
    /// </summary>
    public static class SquatPhysicalUpperLimbProjection
    {
        public const string ProjectionId = "GAM11_TASK_SPACE_UPPER_LIMB_PROJECTION_V1";

        public static PhysicalUpperLimbTargets Project(
            PhysicalAthleteRig rig,
            SquatReferenceRigCalibration calibration,
            SquatReferenceUpperLimbSide limb,
            Quaternion thoraxBodyRotation,
            bool isLeft)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));

            string upperArmId = isLeft ? "left_upper_arm" : "right_upper_arm";
            string forearmId = isLeft ? "left_forearm" : "right_forearm";
            string handId = isLeft ? "left_hand" : "right_hand";
            SquatReferenceBoneFrame upperArmBone = isLeft ? calibration.LeftUpperArm : calibration.RightUpperArm;
            SquatReferenceBoneFrame forearmBone = isLeft ? calibration.LeftForearm : calibration.RightForearm;
            SquatReferenceBoneFrame handBone = isLeft ? calibration.LeftHand : calibration.RightHand;

            Vector3 humerus = (limb.ElbowCenter - limb.ShoulderCenter).normalized;
            Vector3 forearmDirection = (limb.HandCenter - limb.ElbowCenter).normalized;
            Vector3 planeNormal = Vector3.Cross(humerus, forearmDirection);
            if (planeNormal.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException(
                    $"Cannot project the {(isLeft ? "left" : "right")} upper limb: the accepted arm is " +
                    "straight, so it defines no flexion plane.");
            planeNormal = planeNormal.normalized;

            // The hinge axis may lie on either face of the arm plane, and the
            // included angle is unsigned, so four chains reproduce the same
            // accepted geometry. They do not cost the same: the humerus roll
            // sets the shoulder target and the flexion sign decides which end
            // of the elbow's range is used, and picking blind put the elbow on
            // its -5 deg stop with the shoulder outside its own cone. Score all
            // four against both joints' authored limits.
            Vector3 elbowToHand = Quaternion.Inverse(forearmBone.BindRotation) *
                (handBone.BindPosition - forearmBone.BindPosition);
            float magnitude = Vector3.Angle(humerus, forearmDirection);
            PhysicalJointRecipe shoulderRecipe = Recipe(upperArmId);
            PhysicalJointRecipe elbowRecipe = Recipe(forearmId);

            Quaternion upperArmBody = Quaternion.identity;
            Quaternion best = Quaternion.identity;
            float bestScore = float.PositiveInfinity;
            float bestResidual = float.PositiveInfinity;
            float bestFlexion = 0f;
            bool found = false;

            foreach (float normalSign in new[] { 1f, -1f })
            {
                Quaternion candidateUpperArm = HumerusBodyRotation(
                    rig, upperArmBone, forearmBone, upperArmId, forearmId,
                    humerus, planeNormal * normalSign);
                Quaternion shoulderLogical = LogicalFromChildBody(
                    rig, upperArmId, thoraxBodyRotation, candidateUpperArm);
                float shoulderExcess = BallLimitExcessDegrees(shoulderLogical, shoulderRecipe);

                foreach (float flexion in new[] { magnitude, -magnitude })
                {
                    float elbowExcess = Mathf.Max(
                        0f,
                        Mathf.Max(elbowRecipe.LowDegrees - flexion, flexion - elbowRecipe.HighDegrees));
                    Quaternion candidateForearm = ChildBodyFromLogical(
                        rig, forearmId, candidateUpperArm, Quaternion.AngleAxis(flexion, Vector3.right));
                    Vector3 hand = limb.ElbowCenter + ToBone(rig, forearmId, candidateForearm) * elbowToHand;
                    float residual = Vector3.Distance(hand, limb.HandCenter);

                    // The physical upper limb task is to reach the barbell shelf
                    // on the upper back (within 25 mm). A candidate that places the
                    // hand 420 mm away against the chest fails the task completely.
                    // Prioritize reaching the hand target within tolerance, then
                    // minimize limit excess.
                    float residualMm = residual * 1000f;
                    float reachPenalty = residual > 0.05f ? 10000f * residual : 0f;
                    float score = reachPenalty + residualMm + (shoulderExcess + elbowExcess * 10f);
                    if (!found || score < bestScore)
                    {
                        found = true;
                        bestScore = score;
                        bestResidual = residual;
                        bestFlexion = flexion;
                        best = candidateForearm;
                        upperArmBody = candidateUpperArm;
                    }
                }
            }

            Quaternion desiredHandBody = ToBody(rig, handId, limb.HandBoneRotation);
            Quaternion handLogical = LogicalFromChildBody(rig, handId, best, desiredHandBody);
            Quaternion clampedHandLogical = ClampToBallLimits(handLogical, Recipe(handId));

            Quaternion realisedHandBody = ChildBodyFromLogical(rig, handId, best, clampedHandLogical);
            return new PhysicalUpperLimbTargets(
                LogicalFromChildBody(rig, upperArmId, thoraxBodyRotation, upperArmBody),
                LogicalFromChildBody(rig, forearmId, upperArmBody, best),
                clampedHandLogical,
                bestFlexion,
                bestResidual,
                Quaternion.Angle(realisedHandBody, desiredHandBody));
        }

        /// <summary>
        /// The upper arm orientation that points the humerus along the accepted
        /// segment and puts the elbow's own hinge axis on the arm plane.
        /// </summary>
        private static Quaternion HumerusBodyRotation(
            PhysicalAthleteRig rig,
            SquatReferenceBoneFrame upperArmBone,
            SquatReferenceBoneFrame forearmBone,
            string upperArmId,
            string forearmId,
            Vector3 humerus,
            Vector3 hingeAxis)
        {
            PoweredJointController.PoweredJointRuntime elbow = rig.PoweredController.GetJoint(forearmId);
            Vector3 longAxisInBone = Quaternion.Inverse(upperArmBone.BindRotation) *
                (forearmBone.BindPosition - upperArmBone.BindPosition);
            Vector3 longAxisInBody =
                (rig.Segments[upperArmId].BodyToReferenceBoneRotation * longAxisInBone).normalized;
            Vector3 hingeInBody = (elbow.NeutralParentToChild * elbow.Joint.axis.normalized).normalized;

            Quaternion fromBody = Quaternion.LookRotation(
                Vector3.Cross(longAxisInBody, hingeInBody).normalized, longAxisInBody);
            Quaternion toWorld = Quaternion.LookRotation(
                Vector3.Cross(humerus, hingeAxis).normalized, humerus);
            return toWorld * Quaternion.Inverse(fromBody);
        }

        /// <summary>
        /// Holds a logical target inside a ball joint's authored cone, so the
        /// wrist is never handed a residual it cannot take.
        /// </summary>
        private static Quaternion ClampToBallLimits(Quaternion logical, PhysicalJointRecipe recipe)
        {
            if (logical.w < 0f)
                logical = new Quaternion(-logical.x, -logical.y, -logical.z, -logical.w);
            var axis = new Vector3(logical.x, logical.y, logical.z);
            float magnitude = axis.magnitude;
            if (magnitude <= 1e-7f)
                return Quaternion.identity;

            float angleDeg = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(logical.w, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 degrees = axis * (angleDeg / magnitude);
            float clampedX = Mathf.Clamp(degrees.x, recipe.LowDegrees, recipe.HighDegrees);
            var secondary = new Vector2(degrees.y, degrees.z);
            if (secondary.magnitude > recipe.SecondaryLimitDegrees)
                secondary = secondary.normalized * recipe.SecondaryLimitDegrees;

            var clamped = new Vector3(clampedX, secondary.x, secondary.y);
            float clampedMagnitude = clamped.magnitude;
            return clampedMagnitude <= 1e-5f
                ? Quaternion.identity
                : Quaternion.AngleAxis(clampedMagnitude, clamped / clampedMagnitude);
        }

        /// <summary>
        /// How far a logical target reaches outside a ball joint's authored
        /// cone, in degrees, so an unreachable chain can be scored rather than
        /// silently commanded.
        /// </summary>
        private static float BallLimitExcessDegrees(Quaternion logical, PhysicalJointRecipe recipe)
        {
            if (logical.w < 0f)
                logical = new Quaternion(-logical.x, -logical.y, -logical.z, -logical.w);
            var axis = new Vector3(logical.x, logical.y, logical.z);
            float axisMagnitude = axis.magnitude;
            if (axisMagnitude <= 1e-7f)
                return 0f;
            float angleDeg = 2f * Mathf.Atan2(axisMagnitude, Mathf.Clamp(logical.w, -1f, 1f)) * Mathf.Rad2Deg;
            Vector3 degrees = axis * (angleDeg / axisMagnitude);
            float x = Mathf.Max(0f, Mathf.Max(recipe.LowDegrees - degrees.x, degrees.x - recipe.HighDegrees));
            float secondary = Mathf.Max(
                0f, new Vector2(degrees.y, degrees.z).magnitude - recipe.SecondaryLimitDegrees);
            return x + secondary;
        }

        private static PhysicalJointRecipe Recipe(string childId)
        {
            foreach (PhysicalJointRecipe recipe in PhysicalAthleteDefinition.Joints)
                if (string.Equals(recipe.ChildId, childId, StringComparison.Ordinal))
                    return recipe;
            throw new ArgumentException($"No joint recipe for '{childId}'.", nameof(childId));
        }

        private static Quaternion ToBody(PhysicalAthleteRig rig, string segmentId, Quaternion boneRotation) =>
            boneRotation * Quaternion.Inverse(rig.Segments[segmentId].BodyToReferenceBoneRotation);

        private static Quaternion ToBone(PhysicalAthleteRig rig, string segmentId, Quaternion bodyRotation) =>
            bodyRotation * rig.Segments[segmentId].BodyToReferenceBoneRotation;

        private static Quaternion ChildBodyFromLogical(
            PhysicalAthleteRig rig, string childId, Quaternion parentBodyRotation, Quaternion logical)
        {
            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(childId);
            return parentBodyRotation * joint.NeutralParentToChild *
                (joint.JointSpace * logical * Quaternion.Inverse(joint.JointSpace));
        }

        private static Quaternion LogicalFromChildBody(
            PhysicalAthleteRig rig, string childId, Quaternion parentBodyRotation, Quaternion childBodyRotation)
        {
            PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(childId);
            Quaternion relative = Quaternion.Inverse(parentBodyRotation) * childBodyRotation;
            Quaternion neutralDelta = Quaternion.Inverse(joint.NeutralParentToChild) * relative;
            return Quaternion.Inverse(joint.JointSpace) * neutralDelta * joint.JointSpace;
        }
    }
}
