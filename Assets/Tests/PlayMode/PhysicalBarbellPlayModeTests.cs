using System.Collections;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalBarbellPlayModeTests
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
        public IEnumerator GAM8_SCENE_HAS_ONE_DYNAMIC_BAR_AND_COMPLETE_OBSERVATION()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("PhysicalAthletePhysics", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            PhysicalBarbell bar = Object.FindFirstObjectByType<PhysicalBarbell>();
            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            Assert.That(bar, Is.Not.Null);
            Assert.That(bar.Body, Is.Not.Null);
            Assert.That(bar.Body.isKinematic, Is.False);
            Assert.That(bar.Body.useGravity, Is.True);
            Assert.That(bar.Body.GetComponentsInChildren<Rigidbody>(true), Has.Length.EqualTo(1));
            Assert.That(bootstrap.Runtime.CurrentObservation.BodyCount, Is.EqualTo(17));
            Assert.That(bootstrap.Runtime.CurrentObservation.PrimaryBody.BodyId, Is.EqualTo("pelvis"));
            Assert.That(bootstrap.Runtime.CurrentObservation.BodyAt(16).BodyId, Is.EqualTo("barbell"));
            Assert.That(bootstrap.Runtime.CurrentObservation.TryGetBody("barbell", out PhysicalBodyObservation _), Is.True);
            Capture("GAM-8-105kg-loaded.png");
            Capture("GAM-8-collider-com-inertia-landmarks.png");

            bar.ConfigureLoad(25f);
            Assert.That(bar.LoadedMassKg, Is.EqualTo(25f).Within(0.0001f));
            Capture("GAM-8-25kg-bar.png");
            Vector3 lightInertia = bar.Body.inertiaTensor;
            bar.ConfigureLoad(205f);
            Assert.That(bar.LoadedMassKg, Is.EqualTo(205f).Within(0.0001f));
            Assert.That(bar.Body.inertiaTensor.y, Is.GreaterThan(lightInertia.y));
            Capture("GAM-8-205kg-loaded.png");

            bar.ResetAndDrop();
            float elevatedY = bar.Body.position.y;
            yield return new WaitForSecondsRealtime(0.75f);
            Assert.That(bar.Body.position.y, Is.LessThan(elevatedY - 0.05f));
            Capture("GAM-8-drop-contact.png");

            bar.ConfigureLoad(25f);
            bar.ApplyDiagnosticImpulse();
            for (int frame = 0; frame < 25 && bar.HasPendingImpulseMeasurement; frame++)
                yield return new WaitForSecondsRealtime(0.02f);
            Assert.That(bar.HasPendingImpulseMeasurement, Is.False, "The diagnostic response was not observed after an authoritative tick.");
            Assert.That(bar.LastImpulseAngularResponse.magnitude, Is.GreaterThan(0f));
            Vector3 lightLinearResponse = bar.LastImpulseLinearResponse;
            Vector3 lightAngularResponse = bar.LastImpulseAngularResponse;

            bar.ConfigureLoad(205f);
            bar.ApplyDiagnosticImpulse();
            for (int frame = 0; frame < 25 && bar.HasPendingImpulseMeasurement; frame++)
                yield return new WaitForSecondsRealtime(0.02f);
            Assert.That(bar.HasPendingImpulseMeasurement, Is.False, "The heavy diagnostic response was not observed after an authoritative tick.");
            Vector3 heavyLinearResponse = bar.LastImpulseLinearResponse;
            Vector3 heavyAngularResponse = bar.LastImpulseAngularResponse;
            Assert.That(heavyLinearResponse.magnitude, Is.LessThan(lightLinearResponse.magnitude * 0.5f));
            Assert.That(heavyAngularResponse.magnitude, Is.LessThan(lightAngularResponse.magnitude * 0.5f));
            Capture("GAM-8-light-vs-heavy-impulse.png");

            bar.ConfigureLoad(105f);
            bootstrap.Runtime.BeginAttemptTrace();
            for (int frame = 0; frame < 6; frame++)
                yield return new WaitForSecondsRealtime(0.02f);
            bootstrap.Runtime.EndAttemptTrace();

            AttemptTrace trace = bootstrap.Runtime.AttemptTrace;
            Assert.That(trace.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(trace.Count, Is.LessThanOrEqualTo(trace.Capacity));
            for (int index = 0; index < trace.Count; index++)
            {
                AttemptTraceSample sample = trace.GetSample(index);
                Assert.That(sample.Observation.BodyCount, Is.EqualTo(17));
                Assert.That(sample.Intent.Tick, Is.EqualTo(sample.Tick));
                Assert.That(sample.Observation.TryGetBody("barbell", out PhysicalBodyObservation _), Is.True);
                if (index > 0)
                    Assert.That(sample.Tick, Is.GreaterThan(trace.GetSample(index - 1).Tick));
            }

            bar.ToggleRecordedTrail();
            Assert.That(bar.IsRecordedTrailVisible, Is.True);
            Assert.That(bar.RecordedTrailPointCount, Is.EqualTo(trace.Count));
            yield return null;
            Capture("GAM-8-recorded-bar-trail-or-ghost.png");
            GameObject recordedTrail = GameObject.Find("RecordedBarTrail_GAM8_PresentationOnly");
            Assert.That(recordedTrail, Is.Not.Null);
            Assert.That(recordedTrail.GetComponent<Rigidbody>(), Is.Null);

            bar.ClearTrace();
            bar.ConfigureLoad(105f);
            bootstrap.Runtime.AdvanceRenderFrame(0.04d);
            PhysicalBodyObservation withoutTrace = ReadBarObservation(bootstrap);

            bar.ConfigureLoad(105f);
            bootstrap.Runtime.BeginAttemptTrace();
            bootstrap.Runtime.AdvanceRenderFrame(0.04d);
            bootstrap.Runtime.EndAttemptTrace();
            PhysicalBodyObservation withTrace = ReadBarObservation(bootstrap);
            Assert.That(bootstrap.Runtime.AttemptTrace.Count, Is.EqualTo(4));
            Assert.That(Vector3.Distance(ToUnityVector(withoutTrace.PositionMeters), ToUnityVector(withTrace.PositionMeters)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(ToUnityVector(withoutTrace.LinearVelocityMetersPerSecond), ToUnityVector(withTrace.LinearVelocityMetersPerSecond)), Is.LessThan(0.0001f));
            Assert.That(Vector3.Distance(ToUnityVector(withoutTrace.AngularVelocityRadiansPerSecond), ToUnityVector(withTrace.AngularVelocityRadiansPerSecond)), Is.LessThan(0.0001f));

            bar.WriteMeasurementArtifact();
            Assert.That(File.Exists(Path.GetFullPath("Artifacts/Measurements/GAM-8-physical-barbell.json")), Is.True);
            bar.ClearTrace();
            Assert.That(trace.Count, Is.Zero);
            Assert.That(bar.IsRecordedTrailVisible, Is.False);
        }

        [UnityTest]
        public IEnumerator GAM8_COLLAR_LAYOUT_DRIVES_PRESENTATION_AND_CHILD_COLLIDERS()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("PhysicalAthletePhysics", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            PhysicalBarbell bar = Object.FindFirstObjectByType<PhysicalBarbell>();
            Assert.That(bar, Is.Not.Null);
            Assert.That(bar.LoadLayout, Is.Not.Null);

            AssertLoadPresentationAndColliders(bar, 105f);
            bar.ConfigureLoad(25f);
            AssertLoadPresentationAndColliders(bar, 25f);
            bar.ConfigureLoad(205f);
            AssertLoadPresentationAndColliders(bar, 205f);
        }

        private static void AssertLoadPresentationAndColliders(PhysicalBarbell bar, float loadKg)
        {
            BarbellSideLayout left = bar.LoadLayout.Left;
            BarbellSideLayout right = bar.LoadLayout.Right;
            Assert.That(bar.LoadedMassKg, Is.EqualTo(loadKg).Within(0.0001f));
            AssertCollarPlacement(bar.Body, "Left", left);
            AssertCollarPlacement(bar.Body, "Right", right);
            AssertPlatePresentation(bar.Body, "left", left);
            AssertPlatePresentation(bar.Body, "right", right);
        }

        private static void AssertCollarPlacement(Rigidbody body, string sideName, BarbellSideLayout layout)
        {
            Transform visual = body.transform.Find(sideName + "CollarVisual");
            Transform colliderObject = body.transform.Find(sideName + "CollarCollider");
            Assert.That(visual, Is.Not.Null);
            Assert.That(colliderObject, Is.Not.Null);
            Assert.That(visual.localPosition.x, Is.EqualTo(layout.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(colliderObject.localPosition.x, Is.EqualTo(layout.RemovableCollarCenterXBarMeters).Within(0.0001f));
            Assert.That(colliderObject.parent, Is.SameAs(body.transform));
            Assert.That(colliderObject.GetComponents<Collider>(), Has.Length.EqualTo(1));
            Assert.That(colliderObject.GetComponent<BoxCollider>(), Is.Not.Null);
            Assert.That(colliderObject.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(colliderObject.GetComponent<Collider>().attachedRigidbody, Is.SameAs(body));
        }

        private static void AssertPlatePresentation(Rigidbody body, string sideName, BarbellSideLayout layout)
        {
            Transform aggregateObject = body.transform.Find(char.ToUpperInvariant(sideName[0]) + sideName.Substring(1) + "PlateAggregateCollider");
            Assert.That(aggregateObject, Is.Not.Null);
            MeshCollider aggregate = aggregateObject.GetComponent<MeshCollider>();
            Assert.That(aggregate, Is.Not.Null);
            if (layout.PlatePlacements.Count == 0)
            {
                Assert.That(aggregateObject.gameObject.activeSelf, Is.False);
                Assert.That(aggregate.sharedMesh, Is.Null);
                return;
            }

            Assert.That(aggregateObject.localPosition.x, Is.EqualTo(
                (layout.PlateStartXBarMeters + layout.PlateStackOuterFaceXBarMeters) * 0.5f).Within(0.0001f));
            Assert.That(aggregateObject.gameObject.activeSelf, Is.True);
            for (int index = 0; index < layout.PlatePlacements.Count; index++)
            {
                BarbellPlatePlacement placement = layout.PlatePlacements[index];
                Transform visual = body.transform.Find(string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "{0}Plate_{1:0.##}kg_{2}",
                    sideName,
                    placement.MassKilograms,
                    index));
                Assert.That(visual, Is.Not.Null);
                Assert.That(visual.localPosition.x, Is.EqualTo(placement.CenterXBarMeters).Within(0.0001f));
            }
        }

        private static void Capture(string filename)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            string directory = Path.GetFullPath("Artifacts/Evidence/GAM-8");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            RenderTexture texture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            camera.targetTexture = texture;
            camera.Render();
            RenderTexture.active = texture;
            image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(texture);
            Object.DestroyImmediate(image);
        }

        private static PhysicalBodyObservation ReadBarObservation(FoundationBootstrap bootstrap)
        {
            Assert.That(bootstrap.Runtime.CurrentObservation.TryGetBody("barbell", out PhysicalBodyObservation bar), Is.True);
            return bar;
        }

        private static Vector3 ToUnityVector(Vector3Value value) => new Vector3(value.X, value.Y, value.Z);
    }
}
