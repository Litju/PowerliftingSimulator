using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class CanonicalHumanoidAssetTests
    {
        private const string ModelPath = "Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx";

        [Test]
        public void CANONICAL_HUMANOID_REQUIRED_BONES_RESOLVE()
        {
            ModelImporter importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            Assert.That(importer, Is.Not.Null);
            Assert.That(importer.animationType, Is.EqualTo(ModelImporterAnimationType.Human));
            Assert.That(float.IsFinite(importer.globalScale), Is.True);
            Assert.That(importer.globalScale, Is.GreaterThan(0f));

            GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            GameObject instance = Object.Instantiate(model);
            try
            {
                Animator animator = instance.GetComponentInChildren<Animator>(true);
                Assert.That(animator, Is.Not.Null);
                Assert.That(animator.avatar.isValid, Is.True);
                Assert.That(animator.avatar.isHuman, Is.True);

                HumanBodyBones[] required =
                {
                    HumanBodyBones.Hips,
                    HumanBodyBones.Spine,
                    HumanBodyBones.Chest,
                    HumanBodyBones.Head,
                    HumanBodyBones.LeftUpperArm,
                    HumanBodyBones.LeftLowerArm,
                    HumanBodyBones.LeftHand,
                    HumanBodyBones.RightUpperArm,
                    HumanBodyBones.RightLowerArm,
                    HumanBodyBones.RightHand,
                    HumanBodyBones.LeftUpperLeg,
                    HumanBodyBones.LeftLowerLeg,
                    HumanBodyBones.LeftFoot,
                    HumanBodyBones.RightUpperLeg,
                    HumanBodyBones.RightLowerLeg,
                    HumanBodyBones.RightFoot
                };

                var resolved = new HashSet<Transform>();
                foreach (HumanBodyBones bone in required)
                {
                    Transform transform = animator.GetBoneTransform(bone);
                    Assert.That(transform, Is.Not.Null, $"{bone} did not resolve.");
                    Assert.That(resolved.Add(transform), Is.True, $"{bone} reused an already-mapped transform.");
                }
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
    }
}
