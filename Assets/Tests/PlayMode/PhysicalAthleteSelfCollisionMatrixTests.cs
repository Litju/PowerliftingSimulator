using System.Collections;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalAthleteSelfCollisionMatrixTests
    {
        [UnityTearDown]
        public IEnumerator Teardown()
        {
            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            if (bootstrap != null && bootstrap.Runtime != null && bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }

            if (bootstrap != null)
                Object.DestroyImmediate(bootstrap.gameObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator PHYSICAL_ATHLETE_RIG_APPLIES_EXACT_COLLISION_MATRIX()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("PhysicalAthletePhysics", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.Segments.Count, Is.EqualTo(16));

            Collider thoraxCol = rig.Segments["thorax"].Collider;
            Collider leftForearmCol = rig.Segments["left_forearm"].Collider;
            Collider rightForearmCol = rig.Segments["right_forearm"].Collider;
            Collider leftUpperArmCol = rig.Segments["left_upper_arm"].Collider;
            Collider rightUpperArmCol = rig.Segments["right_upper_arm"].Collider;
            Collider leftHandCol = rig.Segments["left_hand"].Collider;
            Collider rightHandCol = rig.Segments["right_hand"].Collider;
            Collider headCol = rig.Segments["head_neck"].Collider;
            Collider pelvisCol = rig.Segments["pelvis"].Collider;
            Collider leftThighCol = rig.Segments["left_thigh"].Collider;
            Collider rightThighCol = rig.Segments["right_thigh"].Collider;
            Collider abdomenCol = rig.Segments["abdomen"].Collider;
            Collider leftShankCol = rig.Segments["left_shank"].Collider;
            Collider leftFootCol = rig.Segments["left_foot"].Collider;

            // 1. Qualified non-adjacent exceptions MUST be ignored
            Assert.That(Physics.GetIgnoreCollision(leftForearmCol, thoraxCol), Is.True, "left_forearm <-> thorax must be ignored");
            Assert.That(Physics.GetIgnoreCollision(rightForearmCol, thoraxCol), Is.True, "right_forearm <-> thorax must be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftThighCol, abdomenCol), Is.True, "left_thigh <-> abdomen must be ignored");
            Assert.That(Physics.GetIgnoreCollision(rightThighCol, abdomenCol), Is.True, "right_thigh <-> abdomen must be ignored");

            // 2. Structural adjacent pairs across joints MUST be ignored
            foreach (PhysicalAthleteRig.JointRuntime joint in rig.Joints)
            {
                Collider childCol = rig.Segments[joint.Recipe.ChildId].Collider;
                Collider parentCol = rig.Segments[joint.Recipe.AnchorBone == HumanBodyBones.Hips && joint.Recipe.ChildId == "pelvis" ? "pelvis" : rig.Segments[joint.Recipe.ChildId].Recipe.ParentId].Collider;
                Assert.That(Physics.GetIgnoreCollision(parentCol, childCol), Is.True, $"Adjacent joint pair {parentCol.name} <-> {childCol.name} must be ignored");
            }

            // 3. Non-adjacent non-whitelisted pairs MUST NOT be ignored
            Assert.That(Physics.GetIgnoreCollision(leftHandCol, thoraxCol), Is.False, "left_hand <-> thorax must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(rightHandCol, thoraxCol), Is.False, "right_hand <-> thorax must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftForearmCol, headCol), Is.False, "left_forearm <-> head_neck must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(rightForearmCol, headCol), Is.False, "right_forearm <-> head_neck must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftForearmCol, pelvisCol), Is.False, "left_forearm <-> pelvis must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftUpperArmCol, pelvisCol), Is.False, "left_upper_arm <-> pelvis must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftThighCol, thoraxCol), Is.False, "left_thigh <-> thorax must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftHandCol, headCol), Is.False, "left_hand <-> head_neck must NOT be ignored");

            // 4. The Phase 5H11 exemption is the thigh against the abdomen and
            // nothing else. The lower limb keeps every other self-collision it
            // had, so a leg cannot pass through the trunk or through itself.
            Assert.That(Physics.GetIgnoreCollision(leftThighCol, headCol), Is.False, "left_thigh <-> head_neck must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftShankCol, abdomenCol), Is.False, "left_shank <-> abdomen must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftFootCol, abdomenCol), Is.False, "left_foot <-> abdomen must NOT be ignored");
            Assert.That(Physics.GetIgnoreCollision(leftThighCol, rightThighCol), Is.False, "left_thigh <-> right_thigh must NOT be ignored");
        }
    }
}
