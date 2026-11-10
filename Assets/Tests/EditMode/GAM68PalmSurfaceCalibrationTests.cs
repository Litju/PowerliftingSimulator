using System;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PowerliftingSimulator.Tests.EditMode
{
    /// <summary>
    /// Re-measures, from the actual imported avatar, every hand constant the
    /// GAM-68 presentation grip and live upper-limb coordination depend on.
    /// </summary>
    public sealed class GAM68PalmSurfaceCalibrationTests
    {
        private const string ScenePath = "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity";
        private static readonly string[] Fingers = { "Index", "Middle", "Ring", "Little" };

        private Animator _animator;

        [SetUp]
        public void SetUp()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            Assert.That(rig, Is.Not.Null);
            _animator = rig.VisibleAnimator;
            Assert.That(_animator, Is.Not.Null);
            Assert.That(_animator.avatar != null && _animator.avatar.isHuman, Is.True);
        }

        [Test]
        public void PALM_SURFACE_CONSTANTS_MATCH_THE_RENDERED_SKIN()
        {
            AssertHand(true, SquatPalmSurface.LeftPalmCenterLocal, SquatPalmSurface.LeftPalmNormalLocal);
            AssertHand(false, SquatPalmSurface.RightPalmCenterLocal, SquatPalmSurface.RightPalmNormalLocal);
        }

        /// <summary>
        /// The palmar side is the side the fingers flex toward. Drive Unity's
        /// own humanoid finger "Stretched" muscles to full flexion and require
        /// every fingertip to move toward the calibrated palmar normal on both
        /// hands. This checks the sign, not just |dot|.
        /// </summary>
        [Test]
        public void PALMAR_NORMAL_SIGN_MATCHES_HUMANOID_FINGER_FLEXION()
        {
            AssertFlexionSide(true, SquatPalmSurface.LeftPalmNormalLocal);
            AssertFlexionSide(false, SquatPalmSurface.RightPalmNormalLocal);
        }

        [Test]
        public void FINGER_PAD_DEPTHS_MATCH_THE_RENDERED_SKIN()
        {
            for (int segment = 0; segment < 3; segment++)
            {
                float sum = 0f;
                int count = 0;
                foreach (bool isLeft in new[] { true, false })
                {
                    foreach (string finger in Fingers)
                    {
                        HumanBodyBones bone = FingerBone(isLeft, finger, segment);
                        sum += PalmarExtent(_animator.GetBoneTransform(bone));
                        count++;
                    }
                }
                float measured = sum / count;
                Assert.That(measured, Is.EqualTo(SquatPresentationGrip.FingerPadDepthM[segment]).Within(0.001f),
                    $"Finger segment {segment} palmar pad depth drifted from the rendered skin.");
            }
        }

        [Test]
        public void GRIP_OBLIQUITY_MATCHES_THE_METACARPOPHALANGEAL_LINE()
        {
            Assert.That(SquatPalmSurface.GripObliquityDegrees(_animator, true),
                Is.EqualTo(SquatPalmSurface.MeasuredGripObliquityDegrees).Within(0.2f));
            Assert.That(SquatPalmSurface.GripObliquityDegrees(_animator, false),
                Is.EqualTo(SquatPalmSurface.MeasuredGripObliquityDegrees).Within(0.2f));
        }

        private void AssertHand(bool isLeft, Vector3 expectedCenter, Vector3 expectedNormal)
        {
            bool measured = SquatPalmSurface.TryMeasureFromRenderedMesh(
                _animator, isLeft, out SquatPalmSurfaceFrame frame, out string reason);
            Assert.That(measured, Is.True, reason);
            Assert.That(Vector3.Distance(frame.CenterLocal, expectedCenter), Is.LessThan(0.0015f),
                $"{(isLeft ? "Left" : "Right")} measured palm center drifted from the runtime calibration.");
            Assert.That(Vector3.Dot(frame.NormalLocal, expectedNormal), Is.GreaterThan(0.999f),
                $"{(isLeft ? "Left" : "Right")} measured palm normal drifted or flipped sign.");
            Assert.That(frame.SpanMeters, Is.EqualTo(SquatPalmSurface.PalmSpanMeters).Within(0.002f));
        }

        private void AssertFlexionSide(bool isLeft, Vector3 palmNormalLocal)
        {
            Transform hand = _animator.GetBoneTransform(isLeft ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            var saved = new Dictionary<Transform, Quaternion>();
            foreach (Transform t in _animator.GetComponentsInChildren<Transform>(true))
                saved[t] = t.localRotation;
            Vector3 rootPosition = _animator.transform.position;
            Quaternion rootRotation = _animator.transform.rotation;
            var before = new Dictionary<string, Vector3>();
            foreach (string finger in Fingers)
                before[finger] = hand.InverseTransformPoint(Tip(isLeft, finger).position);

            var handler = new HumanPoseHandler(_animator.avatar, _animator.transform);
            try
            {
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                string prefix = isLeft ? "Left " : "Right ";
                for (int muscle = 0; muscle < HumanTrait.MuscleCount; muscle++)
                {
                    string name = HumanTrait.MuscleName[muscle];
                    if (name.StartsWith(prefix, StringComparison.Ordinal) && name.Contains("Stretched") &&
                        Array.Exists(Fingers, finger => name.Contains(finger)))
                        pose.muscles[muscle] = -1f;
                }
                handler.SetHumanPose(ref pose);
                foreach (string finger in Fingers)
                {
                    Vector3 delta = hand.InverseTransformPoint(Tip(isLeft, finger).position) - before[finger];
                    Assert.That(Vector3.Dot(delta, palmNormalLocal), Is.GreaterThan(0f),
                        $"{(isLeft ? "Left" : "Right")} {finger} flexes away from the calibrated palmar side.");
                }
            }
            finally
            {
                handler.Dispose();
                foreach (KeyValuePair<Transform, Quaternion> pair in saved)
                    pair.Key.localRotation = pair.Value;
                _animator.transform.SetPositionAndRotation(rootPosition, rootRotation);
            }
        }

        private Transform Tip(bool isLeft, string finger)
        {
            Transform distal = _animator.GetBoneTransform(FingerBone(isLeft, finger, 2));
            return distal.childCount > 0 ? distal.GetChild(0) : distal;
        }

        private static HumanBodyBones FingerBone(bool isLeft, string finger, int segment)
        {
            string[] names = { "Proximal", "Intermediate", "Distal" };
            return (HumanBodyBones)Enum.Parse(typeof(HumanBodyBones), (isLeft ? "Left" : "Right") + finger + names[segment]);
        }

        /// <summary>Largest extent of the bone's dominant skin vertices along its palmar +Z.</summary>
        private float PalmarExtent(Transform bone)
        {
            foreach (SkinnedMeshRenderer renderer in _animator.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                int index = Array.IndexOf(renderer.bones, bone);
                if (index < 0 || renderer.sharedMesh == null)
                    continue;
                Mesh mesh = renderer.sharedMesh;
                Vector3[] vertices = mesh.vertices;
                BoneWeight[] weights = mesh.boneWeights;
                Matrix4x4 bind = mesh.bindposes[index];
                float extent = float.NegativeInfinity;
                for (int vertex = 0; vertex < vertices.Length && vertex < weights.Length; vertex++)
                {
                    if (weights[vertex].boneIndex0 != index || weights[vertex].weight0 < 0.5f)
                        continue;
                    extent = Mathf.Max(extent, bind.MultiplyPoint3x4(vertices[vertex]).z);
                }
                if (float.IsFinite(extent))
                    return extent;
            }
            Assert.Fail($"No skinned vertices are dominated by {bone.name}.");
            return 0f;
        }
    }
}
