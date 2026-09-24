using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM49DepthLandmarkProviderTests
    {
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            SquatPhysicalPrototypeController controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (controller != null)
                controller.enabled = false;
            if (rig != null)
                rig.enabled = false;
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
        public IEnumerator GAM49_REFERENCE_PROVIDER_IS_FRAME_COVARIANT_AND_CENTER_DIAGNOSTIC_ONLY()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("SquatReferencePreview", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            SquatReferencePreview preview = Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);
            preview.SetReviewPose(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);

            SquatReferenceKinematicSolution pose = preview.CurrentSolution;
            var provider = new SquatDepthLandmarkProvider(preview.Calibration);
            Assert.That(provider.TryEvaluate(
                pose.LeftLeg.HipCenter,
                pose.RightLeg.HipCenter,
                pose.PelvisFrameRotation,
                pose.LeftLeg.KneeCenter,
                pose.LeftLeg.ShankFrameRotation,
                pose.RightLeg.KneeCenter,
                pose.RightLeg.ShankFrameRotation,
                out SquatRuleLandmarkSet landmarks), Is.True);

            AssertPoint(landmarks.LeftHipCreaseWorld, preview.CurrentRuleLandmarks.LeftHipCreaseWorld);
            AssertPoint(landmarks.RightHipCreaseWorld, preview.CurrentRuleLandmarks.RightHipCreaseWorld);
            AssertPoint(landmarks.LeftKneeTopWorld, preview.CurrentRuleLandmarks.LeftKneeTopWorld);
            AssertPoint(landmarks.RightKneeTopWorld, preview.CurrentRuleLandmarks.RightKneeTopWorld);
            Assert.That(landmarks.Depth.LeftDepthM, Is.EqualTo(-0.07631308f).Within(1e-5f));
            Assert.That(landmarks.Depth.RightDepthM, Is.EqualTo(-0.07631314f).Within(1e-5f));
            Assert.That(landmarks.Depth.BilateralGameJudgmentQualified, Is.True);
            Assert.That(landmarks.Depth.IPFRulePredicateSatisfied, Is.True);
            Assert.That(landmarks.JointCenterDepthDiagnostic.LeftDepthM, Is.GreaterThan(0f));
            Assert.That(landmarks.JointCenterDepthDiagnostic.RightDepthM, Is.GreaterThan(0f));

            Quaternion worldRotation = Quaternion.AngleAxis(73f, Vector3.up);
            Vector3 worldTranslation = new Vector3(2.1f, -0.7f, 4.3f);
            Assert.That(provider.TryEvaluate(
                TransformPoint(pose.LeftLeg.HipCenter, worldRotation, worldTranslation),
                TransformPoint(pose.RightLeg.HipCenter, worldRotation, worldTranslation),
                worldRotation * pose.PelvisFrameRotation,
                TransformPoint(pose.LeftLeg.KneeCenter, worldRotation, worldTranslation),
                worldRotation * pose.LeftLeg.ShankFrameRotation,
                TransformPoint(pose.RightLeg.KneeCenter, worldRotation, worldTranslation),
                worldRotation * pose.RightLeg.ShankFrameRotation,
                out SquatRuleLandmarkSet transformed), Is.True);
            AssertPoint(worldRotation * landmarks.LeftHipCreaseWorld + worldTranslation, transformed.LeftHipCreaseWorld);
            AssertPoint(worldRotation * landmarks.RightHipCreaseWorld + worldTranslation, transformed.RightHipCreaseWorld);
            AssertPoint(worldRotation * landmarks.LeftKneeTopWorld + worldTranslation, transformed.LeftKneeTopWorld);
            AssertPoint(worldRotation * landmarks.RightKneeTopWorld + worldTranslation, transformed.RightKneeTopWorld);
            Assert.That(transformed.Depth.LeftDepthM, Is.EqualTo(landmarks.Depth.LeftDepthM).Within(1e-5f));
            Assert.That(transformed.Depth.RightDepthM, Is.EqualTo(landmarks.Depth.RightDepthM).Within(1e-5f));

            Assert.That(provider.TryEvaluate(
                pose.LeftLeg.HipCenter,
                pose.RightLeg.HipCenter,
                pose.PelvisFrameRotation,
                pose.LeftLeg.KneeCenter,
                pose.LeftLeg.ShankFrameRotation,
                pose.RightLeg.KneeCenter + Vector3.down * 0.20f,
                pose.RightLeg.ShankFrameRotation,
                out SquatRuleLandmarkSet unilateral), Is.True);
            Assert.That(unilateral.Depth.LeftDepthM, Is.LessThan(-SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M));
            Assert.That(unilateral.Depth.RightDepthM, Is.GreaterThan(0f));
            Assert.That(unilateral.Depth.WorstSideDepthM, Is.EqualTo(unilateral.Depth.RightDepthM));
            Assert.That(unilateral.Depth.BilateralGameJudgmentQualified, Is.False);

            Assert.That(provider.TryEvaluate(
                new Vector3(float.NaN, 0f, 0f),
                pose.RightLeg.HipCenter,
                pose.PelvisFrameRotation,
                pose.LeftLeg.KneeCenter,
                pose.LeftLeg.ShankFrameRotation,
                pose.RightLeg.KneeCenter,
                pose.RightLeg.ShankFrameRotation,
                out _), Is.False);
        }

        [UnityTest]
        public IEnumerator GAM49_PHYSICAL_PROVIDER_READS_CURRENT_FRAMES_WITHOUT_MUTATING_BODIES()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("SquatPhysicalPrototype", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            SquatPhysicalPrototypeController controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);
            bootstrap.enabled = false;

            var bodies = new List<Rigidbody>(rig.Segments.Count);
            var positions = new List<Vector3>(rig.Segments.Count);
            var rotations = new List<Quaternion>(rig.Segments.Count);
            var velocities = new List<Vector3>(rig.Segments.Count);
            var angularVelocities = new List<Vector3>(rig.Segments.Count);
            var sleeping = new List<bool>(rig.Segments.Count);
            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                bodies.Add(segment.Body);
                positions.Add(segment.Body.position);
                rotations.Add(segment.Body.rotation);
                velocities.Add(segment.Body.linearVelocity);
                angularVelocities.Add(segment.Body.angularVelocity);
                sleeping.Add(segment.Body.IsSleeping());
            }

            SquatPhysicalAdapter adapter = controller.Adapter;
            Assert.That(adapter.TryGetSurfaceRuleLandmarks(out SquatRuleLandmarkSet first), Is.True);
            Assert.That(adapter.TryGetSurfaceRuleLandmarks(out SquatRuleLandmarkSet second), Is.True);
            AssertPoint(first.LeftHipCreaseWorld, second.LeftHipCreaseWorld);
            AssertPoint(first.RightHipCreaseWorld, second.RightHipCreaseWorld);
            AssertPoint(first.LeftKneeTopWorld, second.LeftKneeTopWorld);
            AssertPoint(first.RightKneeTopWorld, second.RightKneeTopWorld);
            Assert.That(first.Depth.LeftDepthM, Is.EqualTo(second.Depth.LeftDepthM));
            Assert.That(first.Depth.RightDepthM, Is.EqualTo(second.Depth.RightDepthM));
            Assert.That(first.JointCenterDepthDiagnostic.LeftDepthM, Is.EqualTo(second.JointCenterDepthDiagnostic.LeftDepthM));
            Assert.That(first.JointCenterDepthDiagnostic.RightDepthM, Is.EqualTo(second.JointCenterDepthDiagnostic.RightDepthM));

            for (int index = 0; index < bodies.Count; index++)
            {
                Assert.That(bodies[index].position, Is.EqualTo(positions[index]));
                Assert.That(bodies[index].rotation, Is.EqualTo(rotations[index]));
                Assert.That(bodies[index].linearVelocity, Is.EqualTo(velocities[index]));
                Assert.That(bodies[index].angularVelocity, Is.EqualTo(angularVelocities[index]));
                Assert.That(bodies[index].IsSleeping(), Is.EqualTo(sleeping[index]));
            }
        }

        private static Vector3 TransformPoint(Vector3 point, Quaternion rotation, Vector3 translation) =>
            rotation * point + translation;

        private static void AssertPoint(Vector3 expected, Vector3 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(1e-5f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(1e-5f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(1e-5f));
        }
    }
}
