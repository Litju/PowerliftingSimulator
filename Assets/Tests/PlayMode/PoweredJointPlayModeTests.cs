using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class PoweredJointPlayModeTests
    {
        [UnityTearDown]
        public IEnumerator ShutdownFoundation()
        {
            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            if (bootstrap != null && bootstrap.Runtime != null && bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator POSITIVE_KNEE_FLEXION_COMMAND_HAS_CORRECT_SIGN()
        {
            PoweredFixture fixture = CreateFixture("left_shank", "knee", PhysicalJointKind.Hinge, Quaternion.identity, Vector3.right);
            yield return RunPositivePulse(fixture, 20f);
            Assert.That(SignedXDegrees(fixture.Controller.GetJoint("left_shank").Diagnostic.ActualRelative), Is.GreaterThan(2f));
            yield return Unload(fixture.Scene);
        }

        [UnityTest]
        public IEnumerator POSITIVE_ELBOW_FLEXION_COMMAND_HAS_CORRECT_SIGN()
        {
            PoweredFixture fixture = CreateFixture("left_forearm", "elbow", PhysicalJointKind.Hinge, Quaternion.identity, Vector3.forward);
            yield return RunPositivePulse(fixture, 20f);
            Assert.That(SignedXDegrees(fixture.Controller.GetJoint("left_forearm").Diagnostic.ActualRelative), Is.GreaterThan(2f));
            yield return Unload(fixture.Scene);
        }

        [UnityTest]
        public IEnumerator TARGET_ROTATION_IS_PARENT_WORLD_ROTATION_INVARIANT()
        {
            PoweredFixture identity = CreateFixture("left_shank", "knee", PhysicalJointKind.Hinge, Quaternion.identity, Vector3.right);
            yield return RunPositivePulse(identity, 20f);
            float identityAngle = SignedXDegrees(identity.Controller.GetJoint("left_shank").Diagnostic.ActualRelative);
            yield return Unload(identity.Scene);

            Quaternion worldRotation = Quaternion.Euler(23f, 61f, -17f);
            PoweredFixture rotated = CreateFixture("left_shank", "knee", PhysicalJointKind.Hinge, worldRotation, worldRotation * Vector3.right);
            yield return RunPositivePulse(rotated, 20f);
            float rotatedAngle = SignedXDegrees(rotated.Controller.GetJoint("left_shank").Diagnostic.ActualRelative);

            Assert.That(rotatedAngle, Is.EqualTo(identityAngle).Within(0.5f));
            yield return Unload(rotated.Scene);
        }

        [UnityTest]
        public IEnumerator FINITE_CAPACITY_SCALES_WITH_ACTIVATION()
        {
            PoweredFixture fixture = CreateFixture("left_shank", "knee", PhysicalJointKind.Hinge, Quaternion.identity, Vector3.right);
            PoweredJointController.PoweredJointRuntime joint = fixture.Controller.GetJoint("left_shank");
            float capacity = joint.Profile.Value.BaseCapacityNm;

            AssertCapacity(fixture.Controller, joint, 0f, 0f);
            AssertCapacity(fixture.Controller, joint, 0.5f, capacity * 0.5f);
            AssertCapacity(fixture.Controller, joint, 1f, capacity);
            yield return Unload(fixture.Scene);
        }

        [UnityTest]
        public IEnumerator ONE_POWERED_JOINT_WRITER_ONLY()
        {
            var runtime = new FoundationRuntime();
            runtime.Initialize("GAM7.WriterFixture." + Guid.NewGuid().ToString("N"));
            runtime.RegisterPrePhysicsStep((_, __) => { });
            Assert.Throws<InvalidOperationException>(() => runtime.RegisterPrePhysicsStep((_, __) => { }));
            AsyncOperation unload = runtime.Shutdown();
            while (unload != null && !unload.isDone)
                yield return null;
        }

        [UnityTest]
        public IEnumerator POWERED_NEUTRAL_OPPOSES_PASSIVE_AND_ACTUAL_HUMAN_RESPONDS_TO_PULSE()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("PhysicalAthletePhysics", LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig.PoweredController.PoweredJointCount, Is.EqualTo(14));
            Assert.That(rig.PoweredController.PassiveJointCount, Is.EqualTo(1));

            rig.ResetPassive();
            float passiveStart = rig.CalculateWholeBodyCom().y;
            yield return WaitForSimulationSeconds(bootstrap, 0.75d);
            float passiveDrop = passiveStart - rig.CalculateWholeBodyCom().y;
            Capture("GAM-7-passive-t075.png");

            rig.StartPoweredNeutral();
            float poweredStart = rig.CalculateWholeBodyCom().y;
            yield return WaitForSimulationSeconds(bootstrap, 0.75d);
            float poweredDrop = poweredStart - rig.CalculateWholeBodyCom().y;
            Capture("GAM-7-powered-neutral-t075.png");

            Assert.That(poweredDrop, Is.LessThan(passiveDrop * 0.85f),
                $"Finite neutral drives did not materially reduce COM drop (passive={passiveDrop:F3} m, powered={poweredDrop:F3} m).");

            rig.StartZeroActivation();
            foreach (PoweredJointController.PoweredJointRuntime joint in rig.PoweredController.Joints)
            {
                if (joint.Profile.HasValue)
                    Assert.That(ActiveDrive(joint).maximumForce, Is.Zero, joint.Id);
            }

            rig.StartSelectedJointPulse(true);
            yield return WaitForSimulationSeconds(bootstrap, 0.45d);
            PoweredJointDiagnostic pulse = rig.PoweredController.GetJoint("left_shank").Diagnostic;
            float pulseDegrees = SignedXDegrees(pulse.ActualRelative);
            Assert.That(pulseDegrees, Is.GreaterThan(1f));
            Capture("GAM-7-positive-knee-pulse.png");
            Capture("GAM-7-finite-drive-diagnostic.png");

            rig.RecordQualification(passiveDrop, poweredDrop, pulseDegrees);
        }

        private static IEnumerator WaitForSimulationSeconds(FoundationBootstrap bootstrap, double durationSeconds)
        {
            float realtimeDeadline = Time.realtimeSinceStartup + 10f;
            while (bootstrap.Runtime.CurrentTime.SimulationTimeSeconds + 0.000001d < durationSeconds)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(realtimeDeadline),
                    $"Authoritative simulation did not reach {durationSeconds:F2} s before the fixture deadline.");
                yield return null;
            }
        }

        private static void AssertCapacity(
            PoweredJointController controller,
            PoweredJointController.PoweredJointRuntime joint,
            float activation,
            float expectedMaximumForce)
        {
            controller.SetJointCommand(joint.Id, new JointCommand(Quaternion.identity, Vector3.zero, activation, 1f));
            controller.Step(default, PlayerIntentFrame.Empty);
            JointDrive drive = ActiveDrive(joint);
            Assert.That(float.IsFinite(drive.maximumForce), Is.True);
            Assert.That(drive.maximumForce, Is.EqualTo(expectedMaximumForce).Within(0.0001f));
            Assert.That(drive.useAcceleration, Is.False);
        }

        private static IEnumerator RunPositivePulse(PoweredFixture fixture, float degrees)
        {
            fixture.Controller.SetJointCommand(
                fixture.Runtime.Recipe.ChildId,
                new JointCommand(Quaternion.AngleAxis(degrees, Vector3.right), Vector3.zero, 1f, 1f));
            for (int step = 0; step < 80; step++)
            {
                fixture.Controller.Step(default, PlayerIntentFrame.Empty);
                fixture.Scene.GetPhysicsScene().Simulate((float)SimulationConstants.FixedDeltaTimeSeconds);
            }
            fixture.Controller.Step(default, PlayerIntentFrame.Empty);
            yield return null;
        }

        private static PoweredFixture CreateFixture(
            string childId,
            string family,
            PhysicalJointKind kind,
            Quaternion worldRotation,
            Vector3 primaryAxisWorld)
        {
            Scene scene = SceneManager.CreateScene(
                "GAM7.PoweredJointFixture." + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));

            GameObject parentObject = new GameObject("parent");
            GameObject childObject = new GameObject(childId);
            SceneManager.MoveGameObjectToScene(parentObject, scene);
            SceneManager.MoveGameObjectToScene(childObject, scene);
            parentObject.transform.SetPositionAndRotation(Vector3.zero, worldRotation);
            childObject.transform.SetPositionAndRotation(Vector3.zero, worldRotation);

            Rigidbody parent = parentObject.AddComponent<Rigidbody>();
            parent.isKinematic = true;
            parent.useGravity = false;
            Rigidbody child = childObject.AddComponent<Rigidbody>();
            child.useGravity = false;
            child.angularDamping = 0f;

            ConfigurableJoint configurable = childObject.AddComponent<ConfigurableJoint>();
            configurable.connectedBody = parent;
            configurable.autoConfigureConnectedAnchor = false;
            configurable.anchor = Vector3.zero;
            configurable.connectedAnchor = Vector3.zero;
            configurable.axis = childObject.transform.InverseTransformDirection(primaryAxisWorld.normalized);
            Vector3 secondaryWorld = Mathf.Abs(Vector3.Dot(primaryAxisWorld.normalized, Vector3.up)) < 0.9f
                ? worldRotation * Vector3.up
                : worldRotation * Vector3.forward;
            configurable.secondaryAxis = childObject.transform.InverseTransformDirection(secondaryWorld);
            configurable.xMotion = ConfigurableJointMotion.Locked;
            configurable.yMotion = ConfigurableJointMotion.Locked;
            configurable.zMotion = ConfigurableJointMotion.Locked;
            configurable.angularXMotion = ConfigurableJointMotion.Limited;
            configurable.angularYMotion = ConfigurableJointMotion.Locked;
            configurable.angularZMotion = ConfigurableJointMotion.Locked;
            configurable.lowAngularXLimit = new SoftJointLimit { limit = -45f };
            configurable.highAngularXLimit = new SoftJointLimit { limit = 90f };
            configurable.projectionMode = JointProjectionMode.None;

            var recipe = new PhysicalJointRecipe(
                childId,
                HumanBodyBones.Hips,
                kind,
                primaryAxisWorld,
                -45f,
                90f,
                0f,
                family);
            var runtime = new PhysicalAthleteRig.JointRuntime(recipe, configurable, Vector3.zero);
            var controller = new PoweredJointController(new[] { runtime });
            return new PoweredFixture(scene, runtime, controller);
        }

        private static JointDrive ActiveDrive(PoweredJointController.PoweredJointRuntime joint) =>
            joint.Recipe.Kind == PhysicalJointKind.Hinge ? joint.Joint.angularXDrive : joint.Joint.slerpDrive;

        private static float SignedXDegrees(Quaternion rotation)
        {
            Vector3 vector = new Vector3(rotation.x, rotation.y, rotation.z);
            Vector3 projection = Vector3.Project(vector, Vector3.right);
            Quaternion twist = PoweredJointController.NormalizeCanonical(new Quaternion(projection.x, projection.y, projection.z, rotation.w));
            twist.ToAngleAxis(out float angle, out Vector3 axis);
            return angle * Mathf.Sign(Vector3.Dot(axis, Vector3.right));
        }

        private static IEnumerator Unload(Scene scene)
        {
            AsyncOperation unload = SceneManager.UnloadSceneAsync(scene);
            while (unload != null && !unload.isDone)
                yield return null;
        }

        private static void Capture(string filename)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            string directory = Path.GetFullPath("Artifacts/Evidence/GAM-7");
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
            UnityEngine.Object.DestroyImmediate(image);
        }

        private readonly struct PoweredFixture
        {
            public PoweredFixture(Scene scene, PhysicalAthleteRig.JointRuntime runtime, PoweredJointController controller)
            {
                Scene = scene;
                Runtime = runtime;
                Controller = controller;
            }

            public Scene Scene { get; }
            public PhysicalAthleteRig.JointRuntime Runtime { get; }
            public PoweredJointController Controller { get; }
        }
    }
}
