using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PowerliftingSimulator.Athlete
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class HumanoidSkeletonDebug : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField, Min(0.001f)] private float lineWidthMeters = 0.008f;
        [SerializeField] private Color lineColor = new(0.05f, 0.95f, 1f, 1f);

        private static readonly BoneConnection[] Connections =
        {
            new(HumanBodyBones.Hips, HumanBodyBones.Spine),
            new(HumanBodyBones.Spine, HumanBodyBones.Chest),
            new(HumanBodyBones.Chest, HumanBodyBones.UpperChest),
            new(HumanBodyBones.UpperChest, HumanBodyBones.Neck),
            new(HumanBodyBones.Neck, HumanBodyBones.Head),
            new(HumanBodyBones.Chest, HumanBodyBones.LeftShoulder),
            new(HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm),
            new(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
            new(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            new(HumanBodyBones.Chest, HumanBodyBones.RightShoulder),
            new(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm),
            new(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
            new(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
            new(HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg),
            new(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
            new(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
            new(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
            new(HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg),
            new(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
            new(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
            new(HumanBodyBones.RightFoot, HumanBodyBones.RightToes)
        };

        public void Configure(Animator targetAnimator) => animator = targetAnimator;

        private void OnDrawGizmos()
        {
            if (animator == null)
                return;

            Gizmos.color = lineColor;
            foreach (BoneConnection connection in Connections)
            {
                Transform start = animator.GetBoneTransform(connection.Start);
                Transform end = animator.GetBoneTransform(connection.End);
                if (start == null || end == null)
                    continue;

                Gizmos.DrawLine(start.position, end.position);
                Gizmos.DrawSphere(end.position, lineWidthMeters * 1.5f);
            }

            Vector3 origin = transform.position;
            Gizmos.color = Color.red;
            Gizmos.DrawLine(origin, origin + transform.right * 0.25f);
            Gizmos.color = Color.green;
            Gizmos.DrawLine(origin, origin + transform.up * 0.25f);
            Gizmos.color = Color.blue;
            Gizmos.DrawLine(origin, origin + transform.forward * 0.25f);

#if UNITY_EDITOR
            HumanBodyBones[] labelledBones =
            {
                HumanBodyBones.Hips, HumanBodyBones.Head,
                HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
                HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot
            };
            foreach (HumanBodyBones bone in labelledBones)
            {
                Transform target = animator.GetBoneTransform(bone);
                if (target != null)
                    Handles.Label(target.position, bone.ToString());
            }
#endif
        }

        private readonly struct BoneConnection
        {
            public BoneConnection(HumanBodyBones start, HumanBodyBones end)
            {
                Start = start;
                End = end;
            }

            public HumanBodyBones Start { get; }

            public HumanBodyBones End { get; }
        }
    }
}
