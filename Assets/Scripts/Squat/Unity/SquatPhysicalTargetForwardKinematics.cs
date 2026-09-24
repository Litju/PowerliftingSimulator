using System;
using System.Collections.Generic;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public static class SquatPhysicalTargetForwardKinematics
    {
        public static SquatDepthObservation ReconstructRuleDepth(
            PhysicalAthleteRig rig,
            IReadOnlyDictionary<string, Quaternion> logicalTargets,
            Quaternion pelvisWorldRotation)
        {
            if (rig == null)
                throw new ArgumentNullException(nameof(rig));
            if (logicalTargets == null)
                throw new ArgumentNullException(nameof(logicalTargets));
            if (rig.PoweredController == null)
                throw new InvalidOperationException("The physical rig has no powered-joint controller.");

            var poses = new Dictionary<string, BodyPose>(PhysicalAthleteDefinition.Segments.Count);
            foreach (PhysicalSegmentRecipe recipe in PhysicalAthleteDefinition.Segments)
            {
                if (!rig.Segments.TryGetValue(recipe.Id, out PhysicalAthleteRig.SegmentRuntime segment))
                    throw new InvalidOperationException($"Physical segment '{recipe.Id}' is unavailable.");

                if (recipe.ParentId == null)
                {
                    poses.Add(recipe.Id, new BodyPose(Vector3.zero, pelvisWorldRotation));
                    continue;
                }

                if (!logicalTargets.TryGetValue(recipe.Id, out Quaternion logicalTarget))
                    throw new ArgumentException($"Logical target for physical joint '{recipe.Id}' is unavailable.", nameof(logicalTargets));
                if (!poses.TryGetValue(recipe.ParentId, out BodyPose parentPose))
                    throw new InvalidOperationException($"Parent segment '{recipe.ParentId}' was not reconstructed before '{recipe.Id}'.");

                PoweredJointController.PoweredJointRuntime powered = rig.PoweredController.GetJoint(recipe.Id);
                ConfigurableJoint joint = powered.Joint;
                PhysicalAthleteRig.SegmentRuntime parent = rig.Segments[recipe.ParentId];
                if (joint.connectedBody != parent.Body)
                    throw new InvalidOperationException($"Physical joint '{recipe.Id}' is not owned by its production parent segment.");

                Quaternion parentToChild = PoweredJointController.ToParentChildRelativeRotation(
                    powered.NeutralParentToChild,
                    powered.JointSpace,
                    logicalTarget);
                Quaternion childRotation = parentPose.Rotation * parentToChild;
                Vector3 parentAnchor = parentPose.Position + parentPose.Rotation * joint.connectedAnchor;
                Vector3 childPosition = parentAnchor - childRotation * joint.anchor;
                poses.Add(recipe.Id, new BodyPose(childPosition, childRotation));
            }

            Vector3 leftHip = JointAnchor(poses, rig.PoweredController, "left_thigh");
            Vector3 rightHip = JointAnchor(poses, rig.PoweredController, "right_thigh");
            Vector3 leftKnee = JointAnchor(poses, rig.PoweredController, "left_shank");
            Vector3 rightKnee = JointAnchor(poses, rig.PoweredController, "right_shank");
            return SquatDepthGeometry.Evaluate(
                leftHip.y,
                rightHip.y,
                leftKnee.y,
                rightKnee.y);
        }

        private static Vector3 JointAnchor(
            IReadOnlyDictionary<string, BodyPose> poses,
            PoweredJointController controller,
            string childId)
        {
            BodyPose child = poses[childId];
            Vector3 localAnchor = controller.GetJoint(childId).Joint.anchor;
            return child.Position + child.Rotation * localAnchor;
        }

        private readonly struct BodyPose
        {
            public BodyPose(Vector3 position, Quaternion rotation)
            {
                Position = position;
                Rotation = rotation;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
        }
    }
}
