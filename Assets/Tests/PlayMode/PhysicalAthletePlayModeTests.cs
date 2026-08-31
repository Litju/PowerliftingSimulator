using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalAthletePlayModeTests
    {
        [UnityTearDown]
        public IEnumerator RestoreNeutralScene()
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
        public IEnumerator PASSIVE_PHYSICAL_ATHLETE_FALLS_COHERENTLY_AND_RESETS()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("PhysicalAthletePhysics", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-6 review scene is not in EditorBuildSettings.");
            while (!load.isDone)
                yield return null;
            yield return null;

            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.Segments.Count, Is.EqualTo(16));
            Assert.That(rig.Joints.Count, Is.EqualTo(15));
            Assert.That(rig.TotalMassKg, Is.EqualTo(PhysicalAthleteDefinition.PrototypeBodyMassKg).Within(0.0001f));
            Assert.That(rig.MaxInitialNonAdjacentPenetrationMeters, Is.LessThanOrEqualTo(0.015f));

            rig.InspectNeutral();
            yield return null;
            var initialPositions = new Dictionary<string, Vector3>();
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> pair in rig.Segments)
            {
                Rigidbody body = pair.Value.Body;
                initialPositions.Add(pair.Key, body.position);
                Assert.That(body.mass, Is.GreaterThan(0f), pair.Key);
                Assert.That(Finite(body.centerOfMass), Is.True, pair.Key);
                Assert.That(Finite(body.inertiaTensor), Is.True, pair.Key);
                Assert.That(body.inertiaTensor.x, Is.GreaterThan(0f), pair.Key);
                Assert.That(body.inertiaTensor.y, Is.GreaterThan(0f), pair.Key);
                Assert.That(body.inertiaTensor.z, Is.GreaterThan(0f), pair.Key);
            }
            Capture("GAM-6-neutral-physical-visible-overlay.png");
            Capture("GAM-6-collider-com-joint-axis-debug.png");

            float initialComY = rig.CalculateWholeBodyCom().y;
            rig.ReleasePassive();
            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                Assert.That(segment.Body.isKinematic, Is.False, segment.Recipe.Id);
                Assert.That(segment.Body.useGravity, Is.True, segment.Recipe.Id);
            }
            foreach (PhysicalAthleteRig.JointRuntime runtime in rig.Joints)
            {
                ConfigurableJoint joint = runtime.Joint;
                Assert.That(joint.projectionMode, Is.EqualTo(JointProjectionMode.None), runtime.Recipe.ChildId);
                AssertDriveIsZero(joint.angularXDrive, runtime.Recipe.ChildId);
                AssertDriveIsZero(joint.angularYZDrive, runtime.Recipe.ChildId);
                AssertDriveIsZero(joint.slerpDrive, runtime.Recipe.ChildId);
            }

            yield return new WaitForSecondsRealtime(0.35f);
            Capture("GAM-6-passive-ragdoll-fall-t035.png");

            yield return new WaitForSecondsRealtime(0.65f);
            float fallingComY = rig.CalculateWholeBodyCom().y;
            Assert.That(fallingComY, Is.LessThan(initialComY - 0.04f), "The unpowered athlete did not fall under gravity within 1.0 s.");
            Capture("GAM-6-passive-ragdoll-fall-t100.png");

            yield return new WaitForSecondsRealtime(2.0f);
            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                Rigidbody body = segment.Body;
                Assert.That(Finite(body.position), Is.True, segment.Recipe.Id);
                Assert.That(Finite(body.linearVelocity), Is.True, segment.Recipe.Id);
                Assert.That(body.linearVelocity.magnitude, Is.LessThan(100f), segment.Recipe.Id);
                Assert.That(body.angularVelocity.magnitude, Is.LessThan(100f), segment.Recipe.Id);
            }
            foreach (PhysicalAthleteRig.JointRuntime runtime in rig.Joints)
            {
                ConfigurableJoint joint = runtime.Joint;
                float separation = Vector3.Distance(
                    joint.transform.TransformPoint(joint.anchor),
                    joint.connectedBody.transform.TransformPoint(joint.connectedAnchor));
                Assert.That(separation, Is.LessThan(0.08f), runtime.Recipe.ChildId);
            }
            Capture("GAM-6-passive-ragdoll-settled-t300.png");

            rig.InspectNeutral();
            yield return null;
            foreach (KeyValuePair<string, PhysicalAthleteRig.SegmentRuntime> pair in rig.Segments)
            {
                Rigidbody body = pair.Value.Body;
                Assert.That(Vector3.Distance(body.position, initialPositions[pair.Key]), Is.LessThan(0.0001f), pair.Key);
                Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero), pair.Key);
                Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero), pair.Key);
            }
            rig.ReleasePassive();

            FoundationBootstrap activeBootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            Assert.That(activeBootstrap, Is.Not.Null);
            AsyncOperation unload = activeBootstrap.Runtime.Shutdown();
            while (unload != null && !unload.isDone)
                yield return null;
        }

        private static void AssertDriveIsZero(JointDrive drive, string jointName)
        {
            Assert.That(drive.positionSpring, Is.Zero, jointName);
            Assert.That(drive.positionDamper, Is.Zero, jointName);
            Assert.That(drive.maximumForce, Is.Zero, jointName);
        }

        private static bool Finite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static void Capture(string filename)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            string directory = Path.GetFullPath("Artifacts/Evidence/GAM-6");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            RenderTexture texture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(texture);
            Object.DestroyImmediate(image);
        }
    }
}
