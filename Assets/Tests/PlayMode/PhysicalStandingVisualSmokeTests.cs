using System.Collections;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Renders the standing athlete and saves frames, so the pose can be
    /// looked at rather than inferred from telemetry.
    ///
    /// This exists because the balance gate once passed an athlete folded
    /// double at the waist: every centre-of-mass, centre-of-pressure and
    /// contact signal was clean, and nobody had rendered a frame. Numbers
    /// alone could not have caught it and cannot catch the next one.
    ///
    /// Run with graphics enabled or the captures are blank.
    /// </summary>
    public sealed class PhysicalStandingVisualSmokeTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/post-actuator-repair";

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
                _controller.enabled = false;
            if (_rig != null)
                _rig.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        /// <summary>
        /// Renders the scene camera into a texture and writes the PNG on the
        /// spot. ScreenCapture.CaptureScreenshot is asynchronous and wrote
        /// nothing at all under the batch test runner.
        /// </summary>
        private static void CaptureFrame(string path)
        {
            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            if (camera == null)
                return;

            var target = new RenderTexture(1280, 720, 24);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;

            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());

            camera.targetTexture = previousTarget;
            RenderTexture.active = previousActive;
            UnityEngine.Object.DestroyImmediate(image);
            target.Release();
            UnityEngine.Object.DestroyImmediate(target);
        }

        [UnityTest]
        public IEnumerator V1_UNLOADED_STANDING_VISUAL_SMOKE()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);

            // Production configuration. Nothing here overrides a default.
            _controller.SetLoad(0f);
            _bootstrap.enabled = false;

            Directory.CreateDirectory(Path.GetFullPath(EvidenceDirectory));
            int[] captureTicks = { 0, 200, 500, 1000 };
            int captureIndex = 0;

            for (int tick = 0; tick <= 1000; tick++)
            {
                if (captureIndex < captureTicks.Length && tick == captureTicks[captureIndex])
                {
                    yield return null;
                    CaptureFrame(Path.Combine(
                        Path.GetFullPath(EvidenceDirectory), $"standing-t{tick * 10}ms.png"));
                    captureIndex++;
                }

                if (tick < 1000)
                {
                    _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                    float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                    if (_controller.LeftFootContact != null)
                        _controller.LeftFootContact.PhysicsTickUpdate(dt);
                    if (_controller.RightFootContact != null)
                        _controller.RightFootContact.PhysicsTickUpdate(dt);
                }
            }

            float pelvisY = _rig.Segments["pelvis"].Body.position.y;
            Debug.Log($"[V1 VISUAL SMOKE] captured to {EvidenceDirectory}, final pelvisY={pelvisY:F4}");
            Assert.That(pelvisY, Is.GreaterThan(0.9f),
                "The athlete did not survive the ten seconds it was being photographed for.");
            yield return null;
        }
    }
}
