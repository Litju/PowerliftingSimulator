using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Captures current GAM-13 production-candidate standing frames beside the
    /// telemetry that explains each frame. This is intentionally graphics
    /// enabled: the report must show the physical posture, not just numbers.
    /// </summary>
    public sealed class GAM13StageAVisualEvidenceTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-13/stage-a-visual";
        private const int TotalTicks = 500;
        private static readonly int[] CaptureTicks = { 0, 100, 250, 500 };
        private static readonly float[] EvidenceLoadsKg = { 25f, 60f, 300f };

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

        [UnityTest]
        public IEnumerator GAM13_STAGE_A_CAPTURE_CURRENT_CANDIDATE_FRAMES()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                Assert.Ignore("No graphics device: visual evidence would be blank. Run without -nographics.");
            }

            string directory = Path.GetFullPath(EvidenceDirectory);
            Directory.CreateDirectory(directory);
            var rows = new StringBuilder();
            rows.AppendLine(
                "load_kg,tick,time_s,image,pelvis_y_m,trunk_pitch_rad,com_z_m,com_speed_mps," +
                "capture_margin_m,contacts,support,posture_error_deg,limit_proximity,guard_scale");

            foreach (float loadKg in EvidenceLoadsKg)
            {
                yield return LoadFreshScene();
                _controller.SetLoad(loadKg);
                SquatPhysicalAdapter adapter = _controller.Adapter;
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

                int captureIndex = 0;
                for (int tick = 0; tick <= TotalTicks; tick++)
                {
                    if (captureIndex < CaptureTicks.Length && tick == CaptureTicks[captureIndex])
                    {
                        yield return null;
                        string imageName = string.Format(
                            CultureInfo.InvariantCulture,
                            "load-{0:000}kg-t{1:0000}.png",
                            Mathf.RoundToInt(loadKg),
                            tick);
                        string imagePath = Path.Combine(directory, imageName);
                        CaptureFrame(imagePath);
                        rows.AppendLine(BuildTelemetryRow(loadKg, tick, imageName, adapter));
                        captureIndex++;
                    }

                    if (tick < TotalTicks)
                    {
                        adapter.HoldReferencePhaseForQualification(
                            0f,
                            SquatPhaseDirection.None,
                            SquatState.SETUP);
                        Assert.That(
                            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds),
                            Is.EqualTo(1));
                        TickFeet((float)SimulationConstants.FixedDeltaTimeSeconds);
                    }
                }
            }

            File.WriteAllText(
                Path.Combine(directory, "stage-a-visual-telemetry.csv"),
                rows.ToString());
            Debug.Log("GAM13_STAGE_A_VISUAL_EVIDENCE captured to " + directory);
            yield return null;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The Stage-A evidence scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);

            _controller.enabled = false;
            _bootstrap.enabled = false;
        }

        private string BuildTelemetryRow(
            float loadKg,
            int tick,
            string imageName,
            SquatPhysicalAdapter adapter)
        {
            SquatBalanceObserver balance = adapter.Balance;
            float pelvisY = _rig.Segments["pelvis"].Body.position.y;
            float trunkPitch = float.NaN;
            float postureErrorDeg = adapter.CanonicalPostureErrorRad * Mathf.Rad2Deg;
            if (_controller.ObservationCollector.HasLastSnapshot)
            {
                SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
                pelvisY = snapshot.PelvisPositionWorldMeters.Y;
                trunkPitch = snapshot.TrunkWorldPitchRadians;
            }

            float comSpeed = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude;
            float captureMargin = Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear);
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:R},{1},{2:R},\"{3}\",{4:R},{5:R},{6:R},{7:R},{8:R},{9},{10},{11:R},{12:R},{13:R}",
                loadKg,
                tick,
                tick * SimulationConstants.FixedDeltaTimeSeconds,
                imageName,
                pelvisY,
                trunkPitch,
                balance.SystemCom.z,
                comSpeed,
                captureMargin,
                balance.SupportContactCount,
                balance.HasSupport,
                postureErrorDeg,
                adapter.CanonicalPostureLimitProximity,
                adapter.BalanceController.PostureGuardScale);
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void CaptureFrame(string path)
        {
            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();
            Assert.That(camera, Is.Not.Null, "The evidence scene has no camera.");

            var target = new RenderTexture(1280, 720, 24);
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
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                UnityEngine.Object.DestroyImmediate(image);
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
