using System.Collections;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class SquatMotionVisualQualificationTests
    {
        private const string ReferenceScene = "SquatReferencePreview";
        private const string PhysicalScene = "SquatPhysicalPrototype";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/squat-motion";

        [UnityTest]
        public IEnumerator SQUAT_REFERENCE_MOTION_FULL_CYCLE_VISUAL_CAPTURE()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ReferenceScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            SquatReferencePreview preview = Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);
            preview.SetShowReferenceBarGhost(true);
            preview.SetShowLandmarks(false);

            Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null);

            // Stages to capture across the full powerlifting squat
            var stages = new[]
            {
                (phase: 0.00f, dir: SquatPhaseDirection.None,    state: SquatState.LOCKOUT,  tag: "01_standing"),
                (phase: 0.30f, dir: SquatPhaseDirection.Descent, state: SquatState.DESCENT,  tag: "02_descent"),
                (phase: 0.65f, dir: SquatPhaseDirection.Descent, state: SquatState.DESCENT,  tag: "03_parallel"),
                (phase: 1.00f, dir: SquatPhaseDirection.Descent, state: SquatState.BOTTOM,   tag: "04_bottom_depth"),
                (phase: 0.98f, dir: SquatPhaseDirection.Ascent,  state: SquatState.REVERSAL, tag: "05_reversal"),
                (phase: 0.60f, dir: SquatPhaseDirection.Ascent,  state: SquatState.STICKING, tag: "06_sticking_ascent"),
                (phase: 0.00f, dir: SquatPhaseDirection.Ascent,  state: SquatState.LOCKOUT,  tag: "07_lockout_final")
            };

            foreach (var stage in stages)
            {
                preview.SetReviewPose(stage.phase, stage.dir, stage.state);
                yield return null;

                // 1. Oblique 3/4 view
                CaptureView(camera, $"ref_squat_{stage.tag}_oblique.png",
                    new Vector3(2.2f, 1.35f, 1.8f), new Vector3(0f, 0.85f, 0f));

                // 2. Pure side profile view
                CaptureView(camera, $"ref_squat_{stage.tag}_side.png",
                    new Vector3(3.2f, 1.05f, 0f), new Vector3(0f, 0.85f, 0f));

                // 3. Front view
                CaptureView(camera, $"ref_squat_{stage.tag}_front.png",
                    new Vector3(0f, 1.25f, 2.6f), new Vector3(0f, 0.85f, 0f));
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator SQUAT_PHYSICAL_PROTOTYPE_MOTION_VISUAL_CAPTURE()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(PhysicalScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            SquatPhysicalPrototypeController controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();

            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);

            for (int f = 0; f < 5 && !controller.IsInitialized; f++)
                yield return null;

            controller.SetLoad(25f);
            FoundationRuntime runtime = bootstrap.Runtime;
            SquatPhysicalAdapter adapter = controller.Adapter;

            // Settle standing for 30 ticks
            for (int i = 0; i < 30; i++)
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);

            Camera camera = Camera.main ?? Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null);

            // Capture standing setup with 25kg barbell on back
            CaptureView(camera, "phys_squat_01_standing_oblique.png",
                new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
            CaptureView(camera, "phys_squat_01_standing_side.png",
                new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
            CaptureView(camera, "phys_squat_01_standing_front.png",
                new Vector3(0f, 1.25f, 2.4f), new Vector3(0f, 0.9f, 0f));

            adapter.StartSquat();

            int totalTicks = Mathf.CeilToInt(8.0f / (float)SimulationConstants.FixedDeltaTimeSeconds);
            int[] captureTicks = new[] { 50, 100, 175, 250, 325, 350 };
            int captureIndex = 0;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                if (controller.LeftFootContact != null)
                    controller.LeftFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);
                if (controller.RightFootContact != null)
                    controller.RightFootContact.PhysicsTickUpdate((float)SimulationConstants.FixedDeltaTimeSeconds);

                if (captureIndex < captureTicks.Length && tick == captureTicks[captureIndex])
                {
                    yield return null;
                    string tag = $"phys_tick_{tick:D3}_sq_{adapter.Sq:F2}";
                    CaptureView(camera, $"{tag}_oblique.png",
                        new Vector3(2.0f, 1.35f, 1.8f), new Vector3(0f, 0.9f, 0f));
                    CaptureView(camera, $"{tag}_side.png",
                        new Vector3(2.6f, 1.1f, 0f), new Vector3(0f, 0.9f, 0f));
                    captureIndex++;
                }
            }

            yield return null;
        }

        private static void CaptureView(Camera camera, string filename, Vector3 cameraPos, Vector3 lookAtTarget)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Vector3 savedPos = camera.transform.position;
            Quaternion savedRot = camera.transform.rotation;

            camera.transform.position = cameraPos;
            camera.transform.rotation = Quaternion.LookRotation((lookAtTarget - cameraPos).normalized);

            var target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();

                string dir = Path.GetFullPath(EvidenceDirectory);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, filename), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(image);
                camera.transform.position = savedPos;
                camera.transform.rotation = savedRot;
            }
        }
    }
}
