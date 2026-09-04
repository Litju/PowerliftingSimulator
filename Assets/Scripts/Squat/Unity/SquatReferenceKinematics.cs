using System;
using System.Collections.Generic;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Squat.Unity
{
    public enum SquatReferenceCalibrationFixture : byte
    {
        None,
        AnkleDorsiflexionPlus10,
        KneeFlexionPlus10,
        HipFlexionPlus10,
        TrunkFlexionPlus20
    }

    public readonly struct SquatReferenceFrame
    {
        public SquatReferenceFrame(Vector3 origin, Quaternion rotation)
        {
            Origin = origin;
            Rotation = rotation;
        }

        public Vector3 Origin { get; }
        public Quaternion Rotation { get; }
        public Vector3 Right => Rotation * Vector3.right;
        public Vector3 Up => Rotation * Vector3.up;
        public Vector3 Forward => Rotation * Vector3.forward;

        public static SquatReferenceFrame FromUpAxis(
            Vector3 origin,
            Vector3 upAxis,
            Vector3 forwardHint,
            Vector3 rightHint)
        {
            Vector3 up = NormalizeOrThrow(upAxis, "frame up axis");
            Vector3 forward = Vector3.ProjectOnPlane(forwardHint, up);
            if (forward.sqrMagnitude < 1e-8f)
                forward = Vector3.ProjectOnPlane(rightHint, up);
            forward = NormalizeOrThrow(forward, "frame forward axis");

            Vector3 right = Vector3.Cross(up, forward);
            if (rightHint.sqrMagnitude > 1e-8f && Vector3.Dot(right, rightHint) < 0f)
            {
                right = -right;
                forward = -forward;
            }

            right = NormalizeOrThrow(right, "frame right axis");
            forward = NormalizeOrThrow(Vector3.Cross(right, up), "frame forward axis");
            return new SquatReferenceFrame(origin, Quaternion.LookRotation(forward, up));
        }

        public static SquatReferenceFrame FromAxes(
            Vector3 origin,
            Vector3 right,
            Vector3 up,
            Vector3 forward)
        {
            Vector3 normalizedUp = NormalizeOrThrow(up, "frame up axis");
            Vector3 normalizedRight = NormalizeOrThrow(
                Vector3.ProjectOnPlane(right, normalizedUp),
                "frame right axis");
            Vector3 normalizedForward = NormalizeOrThrow(
                Vector3.Cross(normalizedRight, normalizedUp),
                "frame forward axis");
            if (Vector3.Dot(normalizedForward, forward) < 0f)
            {
                normalizedRight = -normalizedRight;
                normalizedForward = -normalizedForward;
            }
            return new SquatReferenceFrame(
                origin,
                Quaternion.LookRotation(normalizedForward, normalizedUp));
        }

        private static Vector3 NormalizeOrThrow(Vector3 value, string name)
        {
            if (value.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException($"Cannot construct a {name} from a zero vector.");
            return value.normalized;
        }
    }

    public sealed class SquatReferenceBoneFrame
    {
        public SquatReferenceBoneFrame(
            HumanBodyBones bone,
            Transform transform,
            SquatReferenceFrame anatomicalFrame)
        {
            Bone = bone;
            Transform = transform;
            AnatomicalFrameBind = anatomicalFrame;
            BoneFromAnatomicalFrame = Quaternion.Inverse(anatomicalFrame.Rotation) * transform.rotation;
            BindPosition = transform.position;
            BindRotation = transform.rotation;
        }

        public HumanBodyBones Bone { get; }
        public Transform Transform { get; }
        public SquatReferenceFrame AnatomicalFrameBind { get; }
        public Quaternion BoneFromAnatomicalFrame { get; }
        public Vector3 BindPosition { get; }
        public Quaternion BindRotation { get; }
    }

    public sealed class SquatReferenceFootCalibration
    {
        public SquatReferenceFootCalibration(
            Transform footBone,
            Transform toeBone,
            SquatReferenceFrame anatomicalFrame,
            Vector3 plantarAnchorWorld)
        {
            FootBone = footBone;
            ToeBone = toeBone;
            AnatomicalFrameBind = anatomicalFrame;
            BoneFromAnatomicalFrame = Quaternion.Inverse(anatomicalFrame.Rotation) * footBone.rotation;
            PlantarAnchorWorld = plantarAnchorWorld;
            PlantarAnchorInBoneLocal = footBone.InverseTransformPoint(plantarAnchorWorld);
            BindAnkleCenter = footBone.position;
            BindRotation = footBone.rotation;
        }

        public Transform FootBone { get; }
        public Transform ToeBone { get; }
        public SquatReferenceFrame AnatomicalFrameBind { get; }
        public Quaternion BoneFromAnatomicalFrame { get; }
        public Vector3 PlantarAnchorWorld { get; }
        public Vector3 PlantarAnchorInBoneLocal { get; }
        public Vector3 BindAnkleCenter { get; }
        public Quaternion BindRotation { get; }
    }

    public sealed class SquatReferenceRigCalibration
    {
        public const string CalibrationId = "GAM10_CANONICAL_QUATERNIUS_JOINT_FRAMES_V2";
        public const float PlantarSupportBandM = 0.003f;
        public const float BilateralHipSolutionToleranceM = 0.0015f;
        public const float FootAnchorToleranceM = 0.0005f;
        public const float SegmentLengthToleranceM = 0.0005f;
        public const float JointAxisToleranceDeg = 0.5f;
        public const float TrunkRelativeToleranceDeg = 0.5f;
        public const float HipCreaseInsetM = 0.055f;
        public const float HipCreaseDownM = 0.055f;
        public const float HipCreaseForwardM = 0.060f;
        public const float KneeProxyUpM = 0.045f;
        public const float KneeProxyForwardM = 0.035f;

        private SquatReferenceRigCalibration() { }

        public string AssetPath { get; private set; }
        public Vector3 GameRight { get; private set; }
        public Vector3 GameUp { get; private set; }
        public Vector3 GameForward { get; private set; }
        public Quaternion GameFrameRotation { get; private set; }
        public Quaternion PlantedFootFrameRotation { get; private set; }
        public Quaternion PelvisFrameBindRotation { get; private set; }
        public Vector3 HipCenterBindWorld { get; private set; }
        public Vector3 PelvisBoneOffsetInPelvisFrame { get; private set; }
        public Vector3 LeftHipOffsetInPelvisFrame { get; private set; }
        public Vector3 RightHipOffsetInPelvisFrame { get; private set; }
        public Vector3 LeftHipCreaseOffsetInPelvisFrame { get; private set; }
        public Vector3 RightHipCreaseOffsetInPelvisFrame { get; private set; }
        public Vector3 LeftKneeTopOffsetInShankFrame { get; private set; }
        public Vector3 RightKneeTopOffsetInShankFrame { get; private set; }
        public float HipWidthM { get; private set; }
        public float LeftThighLengthM { get; private set; }
        public float RightThighLengthM { get; private set; }
        public float LeftShankLengthM { get; private set; }
        public float RightShankLengthM { get; private set; }
        public string PoseRootSource => "measured plantar foot anchors; fixed standing anchors; renderer bounds absent";

        public SquatReferenceFootCalibration LeftFoot { get; private set; }
        public SquatReferenceFootCalibration RightFoot { get; private set; }
        public SquatReferenceBoneFrame Pelvis { get; private set; }
        public SquatReferenceBoneFrame LeftThigh { get; private set; }
        public SquatReferenceBoneFrame RightThigh { get; private set; }
        public SquatReferenceBoneFrame LeftShank { get; private set; }
        public SquatReferenceBoneFrame RightShank { get; private set; }
        public SquatReferenceBoneFrame Spine { get; private set; }
        public SquatReferenceBoneFrame Chest { get; private set; }
        public SquatReferenceBoneFrame UpperChest { get; private set; }
        public SquatReferenceBoneFrame Neck { get; private set; }
        public SquatReferenceBoneFrame Head { get; private set; }
        public SquatReferenceBoneFrame LeftShoulder { get; private set; }
        public SquatReferenceBoneFrame RightShoulder { get; private set; }
        public SquatReferenceBoneFrame LeftUpperArm { get; private set; }
        public SquatReferenceBoneFrame RightUpperArm { get; private set; }
        public SquatReferenceBoneFrame LeftForearm { get; private set; }
        public SquatReferenceBoneFrame RightForearm { get; private set; }
        public SquatReferenceBoneFrame LeftHand { get; private set; }
        public SquatReferenceBoneFrame RightHand { get; private set; }

        public Vector3 LeftHipCenterBindWorld => LeftThigh.BindPosition;
        public Vector3 RightHipCenterBindWorld => RightThigh.BindPosition;
        public Vector3 LeftKneeCenterBindWorld => LeftShank.BindPosition;
        public Vector3 RightKneeCenterBindWorld => RightShank.BindPosition;
        public Vector3 LeftAnkleCenterBindWorld => LeftFoot.BindAnkleCenter;
        public Vector3 RightAnkleCenterBindWorld => RightFoot.BindAnkleCenter;
        public Vector3 ThoraxCenterBindWorld => UpperChest.BindPosition;

        public static SquatReferenceRigCalibration Build(
            Animator animator,
            Transform referenceRoot,
            string assetPath)
        {
            if (animator == null)
                throw new ArgumentNullException(nameof(animator));
            if (referenceRoot == null)
                throw new ArgumentNullException(nameof(referenceRoot));
            if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                throw new InvalidOperationException("Joint-frame calibration requires a valid Humanoid Avatar.");

            Transform hips = RequireBone(animator, HumanBodyBones.Hips);
            Transform spine = RequireBone(animator, HumanBodyBones.Spine);
            Transform chest = RequireBone(animator, HumanBodyBones.Chest);
            Transform upperChest = RequireBone(animator, HumanBodyBones.UpperChest);
            Transform neck = RequireBone(animator, HumanBodyBones.Neck);
            Transform head = RequireBone(animator, HumanBodyBones.Head);
            Transform leftThigh = RequireBone(animator, HumanBodyBones.LeftUpperLeg);
            Transform rightThigh = RequireBone(animator, HumanBodyBones.RightUpperLeg);
            Transform leftShank = RequireBone(animator, HumanBodyBones.LeftLowerLeg);
            Transform rightShank = RequireBone(animator, HumanBodyBones.RightLowerLeg);
            Transform leftFoot = RequireBone(animator, HumanBodyBones.LeftFoot);
            Transform rightFoot = RequireBone(animator, HumanBodyBones.RightFoot);
            Transform leftToes = RequireBone(animator, HumanBodyBones.LeftToes);
            Transform rightToes = RequireBone(animator, HumanBodyBones.RightToes);
            Transform leftShoulder = RequireBone(animator, HumanBodyBones.LeftShoulder);
            Transform rightShoulder = RequireBone(animator, HumanBodyBones.RightShoulder);
            Transform leftUpperArm = RequireBone(animator, HumanBodyBones.LeftUpperArm);
            Transform rightUpperArm = RequireBone(animator, HumanBodyBones.RightUpperArm);
            Transform leftForearm = RequireBone(animator, HumanBodyBones.LeftLowerArm);
            Transform rightForearm = RequireBone(animator, HumanBodyBones.RightLowerArm);
            Transform leftHand = RequireBone(animator, HumanBodyBones.LeftHand);
            Transform rightHand = RequireBone(animator, HumanBodyBones.RightHand);

            Vector3 referenceUp = NormalizeOrThrow(referenceRoot.up, "reference vertical");
            Vector3 measuredHipLine = NormalizeOrThrow(
                Vector3.ProjectOnPlane(rightThigh.position - leftThigh.position, referenceUp),
                "measured game right");
            Vector3 toeDirection = Vector3.ProjectOnPlane(
                ((leftToes.position - leftFoot.position) + (rightToes.position - rightFoot.position)) * 0.5f,
                referenceUp);
            Vector3 gameForward = NormalizeOrThrow(toeDirection, "measured foot forward");
            Vector3 gameRight = NormalizeOrThrow(
                Vector3.Cross(referenceUp, gameForward),
                "measured game right");
            if (Vector3.Dot(gameRight, measuredHipLine) < 0f)
            {
                gameForward = -gameForward;
                gameRight = -gameRight;
            }
            Vector3 gameUp = NormalizeOrThrow(
                Vector3.Cross(gameForward, gameRight),
                "measured game up");
            Quaternion gameFrame = Quaternion.LookRotation(gameForward, gameUp);

            Vector3 leftPlantarAnchor = MeasurePlantarAnchor(animator, leftFoot, leftToes, gameUp);
            Vector3 rightPlantarAnchor = MeasurePlantarAnchor(animator, rightFoot, rightToes, gameUp);
            SquatReferenceFrame plantedFootFrame = new SquatReferenceFrame(Vector3.zero, gameFrame);
            SquatReferenceFrame pelvisFrame = new SquatReferenceFrame(
                (leftThigh.position + rightThigh.position) * 0.5f,
                gameFrame);
            SquatReferenceFrame leftShankFrame = SquatReferenceFrame.FromUpAxis(
                leftFoot.position,
                leftShank.position - leftFoot.position,
                gameForward,
                gameRight);
            SquatReferenceFrame rightShankFrame = SquatReferenceFrame.FromUpAxis(
                rightFoot.position,
                rightShank.position - rightFoot.position,
                gameForward,
                gameRight);
            SquatReferenceFrame leftThighFrame = SquatReferenceFrame.FromUpAxis(
                leftThigh.position,
                leftThigh.position - leftShank.position,
                gameForward,
                gameRight);
            SquatReferenceFrame rightThighFrame = SquatReferenceFrame.FromUpAxis(
                rightThigh.position,
                rightThigh.position - rightShank.position,
                gameForward,
                gameRight);

            SquatReferenceRigCalibration calibration = new SquatReferenceRigCalibration
            {
                AssetPath = assetPath,
                GameRight = gameRight,
                GameUp = gameUp,
                GameForward = gameForward,
                GameFrameRotation = gameFrame,
                PlantedFootFrameRotation = gameFrame,
                PelvisFrameBindRotation = pelvisFrame.Rotation,
                HipCenterBindWorld = pelvisFrame.Origin,
                LeftFoot = new SquatReferenceFootCalibration(leftFoot, leftToes, plantedFootFrame, leftPlantarAnchor),
                RightFoot = new SquatReferenceFootCalibration(rightFoot, rightToes, plantedFootFrame, rightPlantarAnchor),
                Pelvis = new SquatReferenceBoneFrame(HumanBodyBones.Hips, hips, pelvisFrame),
                LeftThigh = new SquatReferenceBoneFrame(HumanBodyBones.LeftUpperLeg, leftThigh, leftThighFrame),
                RightThigh = new SquatReferenceBoneFrame(HumanBodyBones.RightUpperLeg, rightThigh, rightThighFrame),
                LeftShank = new SquatReferenceBoneFrame(HumanBodyBones.LeftLowerLeg, leftShank, leftShankFrame),
                RightShank = new SquatReferenceBoneFrame(HumanBodyBones.RightLowerLeg, rightShank, rightShankFrame),
                Spine = new SquatReferenceBoneFrame(
                    HumanBodyBones.Spine,
                    spine,
                    SquatReferenceFrame.FromUpAxis(spine.position, chest.position - spine.position, gameForward, gameRight)),
                Chest = new SquatReferenceBoneFrame(
                    HumanBodyBones.Chest,
                    chest,
                    SquatReferenceFrame.FromUpAxis(chest.position, upperChest.position - chest.position, gameForward, gameRight)),
                UpperChest = new SquatReferenceBoneFrame(
                    HumanBodyBones.UpperChest,
                    upperChest,
                    SquatReferenceFrame.FromUpAxis(upperChest.position, neck.position - upperChest.position, gameForward, gameRight)),
                Neck = new SquatReferenceBoneFrame(
                    HumanBodyBones.Neck,
                    neck,
                    SquatReferenceFrame.FromUpAxis(neck.position, head.position - neck.position, gameForward, gameRight)),
                Head = new SquatReferenceBoneFrame(
                    HumanBodyBones.Head,
                    head,
                    SquatReferenceFrame.FromUpAxis(head.position, head.up, gameForward, gameRight)),
                LeftShoulder = new SquatReferenceBoneFrame(
                    HumanBodyBones.LeftShoulder,
                    leftShoulder,
                    SquatReferenceFrame.FromUpAxis(leftShoulder.position, leftUpperArm.position - leftShoulder.position, gameForward, gameRight)),
                RightShoulder = new SquatReferenceBoneFrame(
                    HumanBodyBones.RightShoulder,
                    rightShoulder,
                    SquatReferenceFrame.FromUpAxis(rightShoulder.position, rightUpperArm.position - rightShoulder.position, gameForward, gameRight)),
                LeftUpperArm = new SquatReferenceBoneFrame(
                    HumanBodyBones.LeftUpperArm,
                    leftUpperArm,
                    SquatReferenceFrame.FromUpAxis(leftUpperArm.position, leftForearm.position - leftUpperArm.position, gameForward, gameRight)),
                RightUpperArm = new SquatReferenceBoneFrame(
                    HumanBodyBones.RightUpperArm,
                    rightUpperArm,
                    SquatReferenceFrame.FromUpAxis(rightUpperArm.position, rightForearm.position - rightUpperArm.position, gameForward, gameRight)),
                LeftForearm = new SquatReferenceBoneFrame(
                    HumanBodyBones.LeftLowerArm,
                    leftForearm,
                    SquatReferenceFrame.FromUpAxis(leftForearm.position, leftHand.position - leftForearm.position, gameForward, gameRight)),
                RightForearm = new SquatReferenceBoneFrame(
                    HumanBodyBones.RightLowerArm,
                    rightForearm,
                    SquatReferenceFrame.FromUpAxis(rightForearm.position, rightHand.position - rightForearm.position, gameForward, gameRight)),
                LeftHand = new SquatReferenceBoneFrame(
                    HumanBodyBones.LeftHand,
                    leftHand,
                    SquatReferenceFrame.FromUpAxis(leftHand.position, leftHand.up, gameForward, gameRight)),
                RightHand = new SquatReferenceBoneFrame(
                    HumanBodyBones.RightHand,
                    rightHand,
                    SquatReferenceFrame.FromUpAxis(rightHand.position, rightHand.up, gameForward, gameRight))
            };

            Quaternion pelvisInverse = Quaternion.Inverse(calibration.PelvisFrameBindRotation);
            calibration.PelvisBoneOffsetInPelvisFrame = pelvisInverse * (hips.position - calibration.HipCenterBindWorld);
            calibration.LeftHipOffsetInPelvisFrame = pelvisInverse * (leftThigh.position - calibration.HipCenterBindWorld);
            calibration.RightHipOffsetInPelvisFrame = pelvisInverse * (rightThigh.position - calibration.HipCenterBindWorld);
            calibration.HipWidthM = Vector3.Distance(leftThigh.position, rightThigh.position);
            calibration.LeftThighLengthM = Vector3.Distance(leftThigh.position, leftShank.position);
            calibration.RightThighLengthM = Vector3.Distance(rightThigh.position, rightShank.position);
            calibration.LeftShankLengthM = Vector3.Distance(leftShank.position, leftFoot.position);
            calibration.RightShankLengthM = Vector3.Distance(rightShank.position, rightFoot.position);

            Vector3 leftCreaseWorldOffset =
                gameRight * -HipCreaseInsetM +
                gameUp * -HipCreaseDownM +
                gameForward * HipCreaseForwardM;
            Vector3 rightCreaseWorldOffset =
                gameRight * HipCreaseInsetM +
                gameUp * -HipCreaseDownM +
                gameForward * HipCreaseForwardM;
            calibration.LeftHipCreaseOffsetInPelvisFrame = pelvisInverse * leftCreaseWorldOffset;
            calibration.RightHipCreaseOffsetInPelvisFrame = pelvisInverse * rightCreaseWorldOffset;
            calibration.LeftKneeTopOffsetInShankFrame =
                Quaternion.Inverse(leftShankFrame.Rotation) * (gameUp * KneeProxyUpM + gameForward * KneeProxyForwardM);
            calibration.RightKneeTopOffsetInShankFrame =
                Quaternion.Inverse(rightShankFrame.Rotation) * (gameUp * KneeProxyUpM + gameForward * KneeProxyForwardM);
            return calibration;
        }

        private static Transform RequireBone(Animator animator, HumanBodyBones bone)
        {
            Transform transform = animator.GetBoneTransform(bone);
            return transform != null
                ? transform
                : throw new InvalidOperationException($"Joint-frame calibration could not resolve {bone}.");
        }

        private static Vector3 MeasurePlantarAnchor(
            Animator animator,
            Transform foot,
            Transform toes,
            Vector3 gameUp)
        {
            var candidates = new List<Vector3>();
            SkinnedMeshRenderer[] renderers = animator.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (SkinnedMeshRenderer renderer in renderers)
            {
                Mesh sourceMesh = renderer.sharedMesh;
                if (sourceMesh == null || sourceMesh.boneWeights == null || sourceMesh.boneWeights.Length == 0)
                    continue;

                Mesh bakedMesh = new Mesh { name = "GAM10_PlantarAnchorCalibration" };
                try
                {
                    renderer.BakeMesh(bakedMesh, true);
                    Vector3[] vertices = bakedMesh.vertices;
                    BoneWeight[] weights = sourceMesh.boneWeights;
                    Transform[] bones = renderer.bones;
                    int count = Math.Min(vertices.Length, weights.Length);
                    for (int index = 0; index < count; index++)
                    {
                        if (FootInfluence(weights[index], bones, foot, toes) < 0.15f)
                            continue;
                        candidates.Add(renderer.transform.TransformPoint(vertices[index]));
                    }
                }
                finally
                {
                    if (Application.isPlaying)
                        UnityEngine.Object.Destroy(bakedMesh);
                    else
                        UnityEngine.Object.DestroyImmediate(bakedMesh);
                }
            }

            if (candidates.Count == 0)
                throw new InvalidOperationException($"No skinned vertices could calibrate the plantar anchor for {foot.name}.");

            float minimumHeight = float.PositiveInfinity;
            for (int index = 0; index < candidates.Count; index++)
                minimumHeight = Mathf.Min(minimumHeight, Vector3.Dot(candidates[index], gameUp));

            float supportLimit = minimumHeight + PlantarSupportBandM;
            Vector3 supportSum = Vector3.zero;
            int supportCount = 0;
            for (int index = 0; index < candidates.Count; index++)
            {
                if (Vector3.Dot(candidates[index], gameUp) > supportLimit)
                    continue;
                supportSum += candidates[index];
                supportCount++;
            }

            return supportCount > 0 ? supportSum / supportCount : candidates[0];
        }

        private static float FootInfluence(
            BoneWeight weight,
            Transform[] bones,
            Transform foot,
            Transform toes)
        {
            return Influence(weight.weight0, weight.boneIndex0, bones, foot, toes) +
                Influence(weight.weight1, weight.boneIndex1, bones, foot, toes) +
                Influence(weight.weight2, weight.boneIndex2, bones, foot, toes) +
                Influence(weight.weight3, weight.boneIndex3, bones, foot, toes);
        }

        private static float Influence(
            float weight,
            int boneIndex,
            Transform[] bones,
            Transform foot,
            Transform toes)
        {
            if (weight <= 0f || boneIndex < 0 || boneIndex >= bones.Length)
                return 0f;
            Transform bone = bones[boneIndex];
            return bone == foot || bone == toes ? weight : 0f;
        }

        private static Vector3 NormalizeOrThrow(Vector3 value, string name)
        {
            if (value.sqrMagnitude < 1e-8f)
                throw new InvalidOperationException($"Cannot calibrate {name} from a zero vector.");
            return value.normalized;
        }
    }

    public sealed class SquatReferenceLegSolution
    {
        public Vector3 AnkleCenter { get; internal set; }
        public Vector3 KneeCenter { get; internal set; }
        public Vector3 HipCenter { get; internal set; }
        public Quaternion FootFrameRotation { get; internal set; }
        public Quaternion FootBoneRotation { get; internal set; }
        public Quaternion ShankFrameRotation { get; internal set; }
        public Quaternion ShankBoneRotation { get; internal set; }
        public Quaternion ThighFrameRotation { get; internal set; }
        public Quaternion ThighBoneRotation { get; internal set; }
        public float SegmentLengthErrorM { get; internal set; }
        public float FootAnchorErrorM { get; internal set; }
    }

    public sealed class SquatReferenceKinematicSolution
    {
        public bool IsValid { get; internal set; }
        public string RejectionReason { get; internal set; }
        public SquatReferenceLegSolution LeftLeg { get; internal set; }
        public SquatReferenceLegSolution RightLeg { get; internal set; }
        public Vector3 PelvisCenter { get; internal set; }
        public Vector3 PelvisBonePosition { get; internal set; }
        public Quaternion PelvisFrameRotation { get; internal set; }
        public Quaternion PelvisBoneRotation { get; internal set; }
        public Quaternion SpineFrameRotation { get; internal set; }
        public Quaternion ChestFrameRotation { get; internal set; }
        public Quaternion UpperChestFrameRotation { get; internal set; }
        public Quaternion NeckFrameRotation { get; internal set; }
        public Quaternion HeadFrameRotation { get; internal set; }
        public float BilateralHipSolutionErrorM { get; internal set; }
        public float FootAnchorsMaxErrorM { get; internal set; }
        public float SegmentLengthErrorM { get; internal set; }
        public float TrunkRelativeAngleErrorDeg { get; internal set; }
    }

    public static class SquatReferenceKinematics
    {
        public const float SpineWeight = 0.20f;
        public const float ChestWeight = 0.30f;
        public const float UpperChestWeight = 0.50f;
        public const float SpineWeightTotal = SpineWeight + ChestWeight + UpperChestWeight;
        public const float CervicalCompensationFraction = 0.08f;
        public const float HeadCompensationFraction = 0.12f;
        public const float ArmBarHalfWidthM = 0.45f;
        public const float ArmBackOffsetM = 0.120f;
        public const float ArmBarHeightM = 0.115f;
        public const float BarGhostHalfLengthM = 1.05f;

        public static SquatReferenceKinematicSolution Solve(
            SquatReferenceRigCalibration calibration,
            SquatReferencePose pose,
            Vector3 leftStandingFootAnchor,
            Vector3 rightStandingFootAnchor)
        {
            if (calibration == null)
                throw new ArgumentNullException(nameof(calibration));

            SquatReferenceKinematicSolution solution = new SquatReferenceKinematicSolution
            {
                LeftLeg = SolveLeg(calibration, calibration.LeftFoot, calibration.LeftShank, calibration.LeftThigh,
                    pose, leftStandingFootAnchor),
                RightLeg = SolveLeg(calibration, calibration.RightFoot, calibration.RightShank, calibration.RightThigh,
                    pose, rightStandingFootAnchor)
            };

            Quaternion leftPelvisCandidate = PelvisCandidate(calibration, calibration.LeftThigh, solution.LeftLeg.ThighFrameRotation, pose.HipFlexionRad);
            Quaternion rightPelvisCandidate = PelvisCandidate(calibration, calibration.RightThigh, solution.RightLeg.ThighFrameRotation, pose.HipFlexionRad);
            Quaternion pelvisRotation = Quaternion.Slerp(leftPelvisCandidate, rightPelvisCandidate, 0.5f);
            Vector3 leftPelvisOrigin = solution.LeftLeg.HipCenter - pelvisRotation * calibration.LeftHipOffsetInPelvisFrame;
            Vector3 rightPelvisOrigin = solution.RightLeg.HipCenter - pelvisRotation * calibration.RightHipOffsetInPelvisFrame;
            Vector3 pelvisCenter = (leftPelvisOrigin + rightPelvisOrigin) * 0.5f;

            solution.PelvisFrameRotation = pelvisRotation;
            solution.PelvisCenter = pelvisCenter;
            solution.PelvisBonePosition = pelvisCenter + pelvisRotation * calibration.PelvisBoneOffsetInPelvisFrame;
            solution.PelvisBoneRotation = pelvisRotation * calibration.Pelvis.BoneFromAnatomicalFrame;
            solution.BilateralHipSolutionErrorM = Vector3.Distance(leftPelvisOrigin, rightPelvisOrigin);
            solution.FootAnchorsMaxErrorM = Mathf.Max(solution.LeftLeg.FootAnchorErrorM, solution.RightLeg.FootAnchorErrorM);
            solution.SegmentLengthErrorM = Mathf.Max(solution.LeftLeg.SegmentLengthErrorM, solution.RightLeg.SegmentLengthErrorM);
            SolveTrunk(calibration, pose, solution);

            solution.IsValid = IsFinite(solution) &&
                solution.BilateralHipSolutionErrorM <= SquatReferenceRigCalibration.BilateralHipSolutionToleranceM &&
                solution.FootAnchorsMaxErrorM <= SquatReferenceRigCalibration.FootAnchorToleranceM &&
                solution.SegmentLengthErrorM <= SquatReferenceRigCalibration.SegmentLengthToleranceM;
            solution.RejectionReason = solution.IsValid
                ? string.Empty
                : $"bilateralHip={solution.BilateralHipSolutionErrorM:F6} m; " +
                  $"footAnchor={solution.FootAnchorsMaxErrorM:F6} m; " +
                  $"segment={solution.SegmentLengthErrorM:F6} m";
            return solution;
        }

        public static SquatReferencePose CalibrationFixturePose(SquatReferenceCalibrationFixture fixture)
        {
            switch (fixture)
            {
                case SquatReferenceCalibrationFixture.AnkleDorsiflexionPlus10:
                    return Pose(10f, 0f, 0f, 0f);
                case SquatReferenceCalibrationFixture.KneeFlexionPlus10:
                    return Pose(0f, 10f, 0f, 0f);
                case SquatReferenceCalibrationFixture.HipFlexionPlus10:
                    return Pose(0f, 0f, 10f, 0f);
                case SquatReferenceCalibrationFixture.TrunkFlexionPlus20:
                    return Pose(0f, 0f, 0f, 20f);
                default:
                    throw new ArgumentOutOfRangeException(nameof(fixture));
            }
        }

        public static float MeasureAnkleFixtureDegrees(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution)
        {
            return SignedAngleAroundAxis(
                calibration.LeftShank.AnatomicalFrameBind.Up,
                solution.LeftLeg.ShankFrameRotation * Vector3.up,
                calibration.GameRight);
        }

        public static float MeasureKneeFixtureDegrees(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution)
        {
            float bind = SignedAngleAroundAxis(
                calibration.LeftShank.AnatomicalFrameBind.Up,
                calibration.LeftThigh.AnatomicalFrameBind.Up,
                calibration.GameRight);
            float current = SignedAngleAroundAxis(
                solution.LeftLeg.ShankFrameRotation * Vector3.up,
                solution.LeftLeg.ThighFrameRotation * Vector3.up,
                calibration.GameRight);
            return -(current - bind);
        }

        public static float MeasureHipFixtureDegrees(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution)
        {
            float bind = SignedAngleAroundAxis(
                calibration.PelvisFrameBindRotation * Vector3.up,
                calibration.LeftThigh.AnatomicalFrameBind.Up,
                calibration.GameRight);
            float current = SignedAngleAroundAxis(
                solution.PelvisFrameRotation * Vector3.up,
                solution.LeftLeg.ThighFrameRotation * Vector3.up,
                calibration.GameRight);
            return -(current - bind);
        }

        public static float MeasureTrunkRelativeDegrees(
            SquatReferenceRigCalibration calibration,
            SquatReferenceKinematicSolution solution)
        {
            float bind = SignedAngleAroundAxis(
                calibration.PelvisFrameBindRotation * Vector3.up,
                calibration.UpperChest.AnatomicalFrameBind.Up,
                calibration.GameRight);
            float current = SignedAngleAroundAxis(
                solution.PelvisFrameRotation * Vector3.up,
                solution.UpperChestFrameRotation * Vector3.up,
                calibration.GameRight);
            return current - bind;
        }

        public static float SignedAngleAroundAxis(Vector3 from, Vector3 to, Vector3 axis)
        {
            Vector3 projectedFrom = Vector3.ProjectOnPlane(from, axis).normalized;
            Vector3 projectedTo = Vector3.ProjectOnPlane(to, axis).normalized;
            if (projectedFrom.sqrMagnitude < 1e-8f || projectedTo.sqrMagnitude < 1e-8f)
                return 0f;
            float sine = Vector3.Dot(axis.normalized, Vector3.Cross(projectedFrom, projectedTo));
            float cosine = Mathf.Clamp(Vector3.Dot(projectedFrom, projectedTo), -1f, 1f);
            return Mathf.Atan2(sine, cosine) * Mathf.Rad2Deg;
        }

        public static SquatReferenceFrame FrameFromUpAxis(
            Vector3 origin,
            Vector3 upAxis,
            Vector3 forwardHint,
            Vector3 rightHint) => SquatReferenceFrame.FromUpAxis(origin, upAxis, forwardHint, rightHint);

        private static SquatReferenceLegSolution SolveLeg(
            SquatReferenceRigCalibration calibration,
            SquatReferenceFootCalibration foot,
            SquatReferenceBoneFrame shank,
            SquatReferenceBoneFrame thigh,
            SquatReferencePose pose,
            Vector3 standingFootAnchor)
        {
            Quaternion footBoneRotation = calibration.PlantedFootFrameRotation * foot.BoneFromAnatomicalFrame;
            Vector3 ankleCenter = standingFootAnchor - footBoneRotation * foot.PlantarAnchorInBoneLocal;
            Quaternion shankFrame = Quaternion.AngleAxis(
                pose.AnkleDorsiflexionRad * Mathf.Rad2Deg,
                calibration.GameRight) * shank.AnatomicalFrameBind.Rotation;
            Vector3 kneeCenter = ankleCenter + shankFrame * Vector3.up *
                (shank == calibration.LeftShank ? calibration.LeftShankLengthM : calibration.RightShankLengthM);

            Quaternion shankToThighBind = Quaternion.Inverse(shank.AnatomicalFrameBind.Rotation) * thigh.AnatomicalFrameBind.Rotation;
            Quaternion thighFrame = Quaternion.AngleAxis(
                -pose.KneeFlexionRad * Mathf.Rad2Deg,
                calibration.GameRight) * shankFrame * shankToThighBind;
            float thighLength = shank == calibration.LeftShank
                ? calibration.LeftThighLengthM
                : calibration.RightThighLengthM;
            Vector3 hipCenter = kneeCenter + thighFrame * Vector3.up * thighLength;
            float segmentLengthError = Mathf.Max(
                Mathf.Abs(Vector3.Distance(ankleCenter, kneeCenter) -
                    (shank == calibration.LeftShank ? calibration.LeftShankLengthM : calibration.RightShankLengthM)),
                Mathf.Abs(Vector3.Distance(kneeCenter, hipCenter) - thighLength));
            float anchorError = Vector3.Distance(
                ankleCenter + footBoneRotation * foot.PlantarAnchorInBoneLocal,
                standingFootAnchor);

            return new SquatReferenceLegSolution
            {
                AnkleCenter = ankleCenter,
                KneeCenter = kneeCenter,
                HipCenter = hipCenter,
                FootFrameRotation = calibration.PlantedFootFrameRotation,
                FootBoneRotation = footBoneRotation,
                ShankFrameRotation = shankFrame,
                ShankBoneRotation = shankFrame * shank.BoneFromAnatomicalFrame,
                ThighFrameRotation = thighFrame,
                ThighBoneRotation = thighFrame * thigh.BoneFromAnatomicalFrame,
                SegmentLengthErrorM = segmentLengthError,
                FootAnchorErrorM = anchorError
            };
        }

        private static Quaternion PelvisCandidate(
            SquatReferenceRigCalibration calibration,
            SquatReferenceBoneFrame thigh,
            Quaternion thighFrame,
            float hipFlexionRad)
        {
            Quaternion thighFromPelvisBind =
                Quaternion.Inverse(calibration.PelvisFrameBindRotation) * thigh.AnatomicalFrameBind.Rotation;
            return Quaternion.AngleAxis(
                hipFlexionRad * Mathf.Rad2Deg,
                calibration.GameRight) * thighFrame * Quaternion.Inverse(thighFromPelvisBind);
        }

        private static void SolveTrunk(
            SquatReferenceRigCalibration calibration,
            SquatReferencePose pose,
            SquatReferenceKinematicSolution solution)
        {
            Quaternion pelvisFrame = solution.PelvisFrameRotation;
            Vector3 lateralAxis = pelvisFrame * Vector3.right;
            float trunk = pose.TrunkFlexionRad;
            solution.SpineFrameRotation = DistributedTrunkFrame(
                calibration.PelvisFrameBindRotation,
                calibration.Spine.AnatomicalFrameBind.Rotation,
                pelvisFrame,
                lateralAxis,
                trunk * SpineWeight);
            solution.ChestFrameRotation = DistributedTrunkFrame(
                calibration.PelvisFrameBindRotation,
                calibration.Chest.AnatomicalFrameBind.Rotation,
                pelvisFrame,
                lateralAxis,
                trunk * (SpineWeight + ChestWeight));
            solution.UpperChestFrameRotation = DistributedTrunkFrame(
                calibration.PelvisFrameBindRotation,
                calibration.UpperChest.AnatomicalFrameBind.Rotation,
                pelvisFrame,
                lateralAxis,
                trunk * SpineWeightTotal);
            solution.NeckFrameRotation = DistributedTrunkFrame(
                calibration.PelvisFrameBindRotation,
                calibration.Neck.AnatomicalFrameBind.Rotation,
                pelvisFrame,
                lateralAxis,
                trunk * (1f - CervicalCompensationFraction));
            solution.HeadFrameRotation = DistributedTrunkFrame(
                calibration.PelvisFrameBindRotation,
                calibration.Head.AnatomicalFrameBind.Rotation,
                pelvisFrame,
                lateralAxis,
                trunk * (1f - HeadCompensationFraction));
            solution.TrunkRelativeAngleErrorDeg = Mathf.Abs(
                MeasureTrunkRelativeDegrees(calibration, solution) - trunk * Mathf.Rad2Deg);
        }

        private static Quaternion DistributedTrunkFrame(
            Quaternion pelvisBind,
            Quaternion segmentBind,
            Quaternion pelvisTarget,
            Vector3 lateralAxis,
            float segmentFlexionRad)
        {
            Quaternion segmentFromPelvisBind = Quaternion.Inverse(pelvisBind) * segmentBind;
            Quaternion baseSegment = pelvisTarget * segmentFromPelvisBind;
            return Quaternion.AngleAxis(segmentFlexionRad * Mathf.Rad2Deg, lateralAxis) * baseSegment;
        }

        private static SquatReferencePose Pose(
            float ankleDegrees,
            float kneeDegrees,
            float hipDegrees,
            float trunkDegrees) => new SquatReferencePose(
                ankleDegrees * Mathf.Deg2Rad,
                kneeDegrees * Mathf.Deg2Rad,
                hipDegrees * Mathf.Deg2Rad,
                trunkDegrees * Mathf.Deg2Rad,
                0f,
                0f,
                0f);

        private static bool IsFinite(SquatReferenceKinematicSolution solution)
        {
            return IsFinite(solution.LeftLeg.AnkleCenter) &&
                IsFinite(solution.LeftLeg.KneeCenter) &&
                IsFinite(solution.LeftLeg.HipCenter) &&
                IsFinite(solution.RightLeg.AnkleCenter) &&
                IsFinite(solution.RightLeg.KneeCenter) &&
                IsFinite(solution.RightLeg.HipCenter) &&
                IsFinite(solution.PelvisCenter) &&
                IsFinite(solution.PelvisBonePosition) &&
                IsFinite(solution.BilateralHipSolutionErrorM) &&
                IsFinite(solution.FootAnchorsMaxErrorM) &&
                IsFinite(solution.SegmentLengthErrorM) &&
                IsFinite(solution.TrunkRelativeAngleErrorDeg);
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }

    public readonly struct SquatReferenceCalibrationFixtureResult
    {
        public SquatReferenceCalibrationFixtureResult(
            SquatReferenceCalibrationFixture fixture,
            float expectedDegrees,
            float measuredDegrees,
            float errorDegrees,
            string visualProof)
        {
            Fixture = fixture;
            ExpectedDegrees = expectedDegrees;
            MeasuredDegrees = measuredDegrees;
            ErrorDegrees = errorDegrees;
            VisualProof = visualProof;
        }

        public SquatReferenceCalibrationFixture Fixture { get; }
        public float ExpectedDegrees { get; }
        public float MeasuredDegrees { get; }
        public float ErrorDegrees { get; }
        public string VisualProof { get; }
        public bool Passed => ErrorDegrees <= SquatReferenceRigCalibration.JointAxisToleranceDeg;
    }

    public readonly struct SquatReferenceCalibrationReport
    {
        public SquatReferenceCalibrationReport(SquatReferenceCalibrationFixtureResult[] results)
        {
            Results = results ?? throw new ArgumentNullException(nameof(results));
        }

        public IReadOnlyList<SquatReferenceCalibrationFixtureResult> Results { get; }
        public bool Passed
        {
            get
            {
                for (int index = 0; index < Results.Count; index++)
                {
                    if (!Results[index].Passed)
                        return false;
                }
                return Results.Count == 4;
            }
        }
    }
}
