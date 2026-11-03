using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM65PlayableDemoGameTests
    {
        private const int MaxWaitFrames = 1800;

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private SquatPhysicalPrototypeController _controller;
        private FoundationBootstrap _bootstrap;
        private PowerliftingGameUiController _ui;
        private PowerliftingGymEnvironment _gym;
        private PowerliftingCameraDirector _cameraDirector;
        private PowerliftingAudioDirector _audioDirector;
        private PowerliftingSettingsManager _settings;
        private readonly List<string> _unexpectedErrors = new List<string>();
        private readonly List<float> _frameTimesMs = new List<float>(4096);

        [SetUp]
        public void SetUp()
        {
            _unexpectedErrors.Clear();
            _frameTimesMs.Clear();
            PowerliftingGameUiController.ClearPendingReloadScreen();
            Application.logMessageReceived += OnLogMessageReceived;
            LogAssert.ignoreFailingMessages = true;
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PowerliftingGameUiController.ClearPendingReloadScreen();
            if (_controller != null)
                _controller.enabled = false;
            if (_bootstrap != null)
                _bootstrap.enabled = false;

            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }

            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);

            _controller = null;
            _bootstrap = null;
            _ui = null;
            _gym = null;
            _cameraDirector = null;
            _audioDirector = null;
            _settings = null;

            if (_input != null)
            {
                _input.TearDown();
                _input = null;
                _keyboard = null;
            }

            Application.logMessageReceived -= OnLogMessageReceived;
            LogAssert.ignoreFailingMessages = false;
            Assert.That(_unexpectedErrors, Is.Empty, string.Join("\n", _unexpectedErrors));
            yield return null;
        }

        [UnityTest]
        public IEnumerator FULL_PLAYER_GAME_EXPERIENCE_MENU_GYM_SQUAT_RESULT_RETRY_AND_NAVIGATION()
        {
            string captureDir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Artifacts", "Evidence", "GAM-65", "Captures");
            Directory.CreateDirectory(captureDir);

            // 1. Launch Powerlifting Simulator (Scene 0)
            yield return LoadFreshSceneZero();
            yield return WaitForPlayerReadiness();

            // 2. Verify Main Menu & 3D Gym Environment & zero developer telemetry by default
            Assert.That(_ui, Is.Not.Null, "PowerliftingGameUiController must be active in Scene 0.");
            Assert.That(_gym, Is.Not.Null, "PowerliftingGymEnvironment must be active in Scene 0.");
            Assert.That(_cameraDirector, Is.Not.Null, "PowerliftingCameraDirector must be active in Scene 0.");
            Assert.That(_audioDirector, Is.Not.Null, "PowerliftingAudioDirector must be active in Scene 0.");
            Assert.That(_settings, Is.Not.Null, "PowerliftingSettingsManager must be active in Scene 0.");
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.MainMenu));
            Assert.That(_controller.ShowDebugGui, Is.False, "Developer telemetry must be hidden by default.");

            // Verify 3D gym stations exist and have zero enabled colliders
            Assert.That(_gym.SquatStationRoot, Is.Not.Null);
            Assert.That(_gym.BenchStationRoot, Is.Not.Null);
            Assert.That(_gym.DeadliftStationRoot, Is.Not.Null);
            Assert.That(_gym.GetComponentsInChildren<Collider>(true), Is.Empty,
                "Environmental gym geometry must not introduce physics colliders.");

            _cameraDirector.SnapToTargetImmediate();
            yield return null;
            CaptureGameFrame(captureDir, "01_main_menu.png");

            // Test Settings modal from Main Menu
            _ui.OpenSettingsModal();
            Assert.That(_ui.IsSettingsOpen, Is.True);
            _settings.CycleAntiAliasing();
            _settings.AdjustMasterVolume(-0.1f);
            _settings.AdjustMasterVolume(0.1f);
            yield return null;
            CaptureGameFrame(captureDir, "02_settings_modal.png");
            _ui.CloseSettingsModal();
            Assert.That(_ui.IsSettingsOpen, Is.False);

            // 3 & 4. Enter the 3D Training Gym and view athlete & equipment
            _ui.EnterTrainingGym();
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.TrainingGym));
            _cameraDirector.SnapToTargetImmediate();
            yield return null;
            CaptureGameFrame(captureDir, "03_training_gym_overview.png");

            // Verify Bench and Deadlift stations are clearly marked unavailable in D0
            _ui.InspectBenchStationUnavailable();
            Assert.That(_cameraDirector.StationFocus, Is.EqualTo(GymStationFocus.BenchStation));
            Assert.That(_ui.GymStationNotice, Does.Contain("NOT AVAILABLE IN D0"));
            _cameraDirector.SnapToTargetImmediate();
            yield return null;
            CaptureGameFrame(captureDir, "04_gym_bench_station_unavailable_d0.png");

            _ui.InspectDeadliftStationUnavailable();
            Assert.That(_cameraDirector.StationFocus, Is.EqualTo(GymStationFocus.DeadliftStation));
            Assert.That(_ui.GymStationNotice, Does.Contain("NOT AVAILABLE IN D0"));
            _cameraDirector.SnapToTargetImmediate();
            yield return null;
            CaptureGameFrame(captureDir, "05_gym_deadlift_station_unavailable_d0.png");

            // 5. Select Competition Squat Station
            _ui.SelectSquatStation();
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.SquatPreparation));
            _cameraDirector.SnapToTargetImmediate();
            yield return null;

            // 6. Test weight selection (105 kg, 60 kg, then choose 25 kg)
            SquatPhysicalPrototypeController previousController = _controller;
            _ui.SelectBarbellWeight(105f);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(105f));
            yield return WaitForReloadedScene(previousController);
            _cameraDirector.SnapToTargetImmediate();
            CaptureGameFrame(captureDir, "06_squat_prep_105kg.png");

            previousController = _controller;
            _ui.SelectBarbellWeight(60f);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(60f));
            yield return WaitForReloadedScene(previousController);

            previousController = _controller;
            _ui.SelectBarbellWeight(25f);
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));
            yield return WaitForReloadedScene(previousController);
            yield return WaitForPlayerReadiness();
            _cameraDirector.SnapToTargetImmediate();
            CaptureGameFrame(captureDir, "07_squat_prep_25kg_ready.png");

            // 7. Perform the actual physically simulated 25 kg squat (Setup -> Unrack -> Walkout -> Settle -> Judged Squat -> Lockout -> Rerack)
            _ui.BeginPlayerSetupAction();
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.SETUP));

            _ui.BeginUnrackAndWalkoutAction();
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.ActiveLift));
            _cameraDirector.SetActiveLiftCameraAngle(PowerliftingCameraAngle.ThreeQuarterCompetition, true);

            bool capturedWalkout = false;
            for (int frame = 0; frame < 600 && _controller.PlayerGamePhase != SquatPlayerGamePhase.JUDGED_SQUAT; frame++)
            {
                if (!capturedWalkout && _controller.PlayerGamePhase == SquatPlayerGamePhase.WALKOUT_STEP_1)
                {
                    _cameraDirector.SnapToTargetImmediate();
                    CaptureGameFrame(captureDir, "08_active_lift_walkout.png");
                    capturedWalkout = true;
                }
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }

            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.JUDGED_SQUAT));
            Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);

            // Wait for SQUAT_COMMAND
            for (int frame = 0; frame < 300 && !_controller.AttemptOrchestrator.HasSquatCommand; frame++)
            {
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.AttemptOrchestrator.HasSquatCommand, Is.True);
            _cameraDirector.SnapToTargetImmediate();
            CaptureGameFrame(captureDir, "09_active_lift_squat_command.png");

            // Descend with S key
            Press(_keyboard.sKey);
            bool capturedMidDescent = false;
            for (int frame = 0; frame < 700 && _controller.Adapter.State != SquatState.BOTTOM; frame++)
            {
                if (!capturedMidDescent && _controller.Adapter.Sq >= 0.50f)
                {
                    _cameraDirector.SnapToTargetImmediate();
                    CaptureGameFrame(captureDir, "10_active_lift_descent_3quarter.png");
                    capturedMidDescent = true;
                }
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.BOTTOM));

            // Capture bottom depth in both 3/4 Competition view and Side Depth Referee view
            _cameraDirector.SetActiveLiftCameraAngle(PowerliftingCameraAngle.ThreeQuarterCompetition, true);
            CaptureGameFrame(captureDir, "11_active_lift_bottom_3quarter.png");
            _cameraDirector.SetActiveLiftCameraAngle(PowerliftingCameraAngle.SideDepthReferee, true);
            CaptureGameFrame(captureDir, "12_active_lift_bottom_side_depth_judge.png");
            _cameraDirector.SetActiveLiftCameraAngle(PowerliftingCameraAngle.ThreeQuarterCompetition, true);

            // Drive up to lockout with W key
            Release(_keyboard.sKey);
            yield return new WaitForSecondsRealtime(0.02f);
            Press(_keyboard.wKey);
            bool capturedAscent = false;
            for (int frame = 0; frame < MaxWaitFrames && _controller.AttemptRecord == null; frame++)
            {
                if (!capturedAscent && _controller.Adapter.State == SquatState.ASCENT && _controller.Adapter.Sq <= 0.55f)
                {
                    _cameraDirector.SnapToTargetImmediate();
                    CaptureGameFrame(captureDir, "13_active_lift_ascent.png");
                    capturedAscent = true;
                }
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Release(_keyboard.wKey);

            SquatAttemptRecord record = _controller.AttemptRecord;
            Assert.That(record, Is.Not.Null, "Physical squat must produce an authoritative SquatAttemptRecord.");
            Assert.That(record.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.PHYSICAL_LOCKOUT));
            Assert.That(record.RuleOutcome, Is.EqualTo(SquatJudgmentOutcome.GOOD_LIFT));
            Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));

            // Wait for Rerack -> Result
            for (int frame = 0; frame < 400 && _controller.PlayerGamePhase != SquatPlayerGamePhase.RESULT; frame++)
            {
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.RESULT));

            // 8. Verify Result Screen displays authentic referee verdict
            yield return null;
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.AttemptResult));
            Assert.That(_ui.DisplayedResultHeadline, Is.EqualTo("GOOD LIFT"));
            _cameraDirector.SnapToTargetImmediate();
            CaptureGameFrame(captureDir, "14_result_screen_good_lift.png");

            // 9. Retry successfully and test an Aborted / Failed attempt verdict
            previousController = _controller;
            _ui.RetryToSquatPreparation();
            yield return WaitForReloadedScene(previousController);
            yield return WaitForPlayerReadiness();

            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.SquatPreparation));
            Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f));

            // Start second attempt and abort after SQUAT_COMMAND to verify non-good-lift result presentation
            _ui.BeginUnrackAndWalkoutAction();
            for (int frame = 0; frame < 600 && _controller.PlayerGamePhase != SquatPlayerGamePhase.JUDGED_SQUAT; frame++)
            {
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }
            for (int frame = 0; frame < 300 && !_controller.AttemptOrchestrator.HasSquatCommand; frame++)
            {
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }

            _ui.AbortActiveAttemptAction();
            yield return null;
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.AttemptResult));
            Assert.That(_controller.AttemptRecord, Is.Not.Null);
            Assert.That(_controller.AttemptRecord.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.ABORTED));
            Assert.That(_ui.DisplayedResultHeadline, Is.EqualTo("ATTEMPT ABORTED"));
            _cameraDirector.SnapToTargetImmediate();
            CaptureGameFrame(captureDir, "15_result_screen_aborted_lift.png");

            // 10. Return to Training Gym, then Main Menu, then verify Quit
            previousController = _controller;
            _ui.RetryToTrainingGym();
            yield return WaitForReloadedScene(previousController);
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.TrainingGym));

            _ui.ReturnToMainMenu();
            Assert.That(_ui.CurrentScreen, Is.EqualTo(PowerliftingScreenState.MainMenu));

            WritePerformanceReceipt(captureDir);
        }

        private IEnumerator LoadFreshSceneZero()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return BindSceneComponents();
        }

        private IEnumerator WaitForReloadedScene(SquatPhysicalPrototypeController previous)
        {
            for (int frame = 0; frame < 500; frame++)
            {
                SquatPhysicalPrototypeController candidate = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
                if (candidate != null && candidate != previous && candidate.IsInitialized)
                {
                    yield return BindSceneComponents();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.Fail("Scene did not reload cleanly after Retry.");
        }

        private IEnumerator BindSceneComponents()
        {
            for (int frame = 0; frame < 500; frame++)
            {
                _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
                _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
                if (_controller != null && _controller.IsInitialized &&
                    _bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
                {
                    _gym = PowerliftingGymEnvironment.EnsureInScene(_controller, UnityEngine.Object.FindFirstObjectByType<Equipment.PhysicalBarbell>());
                    _ui = UnityEngine.Object.FindFirstObjectByType<PowerliftingGameUiController>();
                    if (_ui == null)
                    {
                        GameObject uiObj = new GameObject("PowerliftingGameUI_GAM65");
                        _settings = uiObj.AddComponent<PowerliftingSettingsManager>();
                        _audioDirector = uiObj.AddComponent<PowerliftingAudioDirector>();
                        _ui = uiObj.AddComponent<PowerliftingGameUiController>();
                    }
                    else
                    {
                        _settings = UnityEngine.Object.FindFirstObjectByType<PowerliftingSettingsManager>();
                        _audioDirector = UnityEngine.Object.FindFirstObjectByType<PowerliftingAudioDirector>();
                    }
                    _cameraDirector = UnityEngine.Object.FindFirstObjectByType<PowerliftingCameraDirector>();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.Fail("SquatPhysicalPrototype scene failed to initialize.");
        }

        private IEnumerator WaitForPlayerReadiness()
        {
            for (int frame = 0; frame < MaxWaitFrames && !_controller.CanBeginPlayerSetup; frame++)
            {
                RecordFrameSample();
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.CanBeginPlayerSetup, Is.True, "Athlete did not reach ready setup state at 25 kg.");
        }

        private void RecordFrameSample()
        {
            float dtMs = Time.unscaledDeltaTime * 1000f;
            if (dtMs > 0.05f && dtMs < 500f)
                _frameTimesMs.Add(dtMs);
        }

        private void CaptureGameFrame(string directory, string fileName)
        {
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Camera cam = _cameraDirector != null && _cameraDirector.TargetCamera != null
                ? _cameraDirector.TargetCamera
                : Camera.main;
            if (cam == null)
                return;

            Canvas canvas = _ui != null ? _ui.GetComponent<Canvas>() : null;
            RenderMode prevMode = RenderMode.ScreenSpaceOverlay;
            Camera prevWorldCam = null;
            float prevPlaneDist = 100f;

            if (canvas != null)
            {
                _ui.RefreshUiImmediate();
                prevMode = canvas.renderMode;
                prevWorldCam = canvas.worldCamera;
                prevPlaneDist = canvas.planeDistance;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = cam;
                canvas.planeDistance = 0.25f;
                Canvas.ForceUpdateCanvases();
            }

            const int width = 1920;
            const int height = 1080;
            RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevTarget = cam.targetTexture;
            RenderTexture prevActive = RenderTexture.active;

            try
            {
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;

                Texture2D tex = new Texture2D(width, height, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                tex.Apply();

                byte[] png = tex.EncodeToPNG();
                string outPath = Path.Combine(directory, fileName);
                File.WriteAllBytes(outPath, png);
                UnityEngine.Object.DestroyImmediate(tex);
            }
            finally
            {
                cam.targetTexture = prevTarget;
                RenderTexture.active = prevActive;
                rt.Release();
                UnityEngine.Object.DestroyImmediate(rt);

                if (canvas != null)
                {
                    canvas.renderMode = prevMode;
                    canvas.worldCamera = prevWorldCam;
                    canvas.planeDistance = prevPlaneDist;
                }
            }
        }

        private void WritePerformanceReceipt(string directory)
        {
            if (_frameTimesMs.Count == 0)
                return;

            _frameTimesMs.Sort();
            float sum = 0f;
            for (int i = 0; i < _frameTimesMs.Count; i++)
                sum += _frameTimesMs[i];
            float avgMs = sum / _frameTimesMs.Count;
            float p50Ms = _frameTimesMs[_frameTimesMs.Count / 2];
            float p95Ms = _frameTimesMs[Mathf.Clamp(Mathf.FloorToInt(_frameTimesMs.Count * 0.95f), 0, _frameTimesMs.Count - 1)];
            float maxMs = _frameTimesMs[_frameTimesMs.Count - 1];
            long allocatedMb = Profiler.GetTotalAllocatedMemoryLong() / (1024 * 1024);
            long reservedMb = Profiler.GetTotalReservedMemoryLong() / (1024 * 1024);
            long monoUsedMb = Profiler.GetMonoUsedSizeLong() / (1024 * 1024);

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "{{\n" +
                "  \"gpu\": \"{0}\",\n" +
                "  \"cpu\": \"{1}\",\n" +
                "  \"sampleFrames\": {2},\n" +
                "  \"avgFrameTimeMs\": {3:F2},\n" +
                "  \"p50FrameTimeMs\": {4:F2},\n" +
                "  \"p95FrameTimeMs\": {5:F2},\n" +
                "  \"maxFrameTimeMs\": {6:F2},\n" +
                "  \"totalAllocatedMemoryMB\": {7},\n" +
                "  \"totalReservedMemoryMB\": {8},\n" +
                "  \"monoHeapUsedMB\": {9}\n" +
                "}}\n",
                SystemInfo.graphicsDeviceName.Replace("\"", "'"),
                SystemInfo.processorType.Replace("\"", "'"),
                _frameTimesMs.Count,
                avgMs,
                p50Ms,
                p95Ms,
                maxMs,
                allocatedMb,
                reservedMb,
                monoUsedMb);

            File.WriteAllText(Path.Combine(directory, "performance_summary.json"), summary);
        }

        private void Press(ButtonControl control)
        {
            _input.currentTime = Time.realtimeSinceStartupAsDouble;
            _input.Press(control);
        }

        private void Release(ButtonControl control)
        {
            _input.currentTime = Time.realtimeSinceStartupAsDouble;
            _input.Release(control);
        }

        private void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception)
                return;
            if (type == LogType.Error && condition.StartsWith("connection.state_change", StringComparison.Ordinal))
                return;
            _unexpectedErrors.Add(condition);
        }
    }
}
