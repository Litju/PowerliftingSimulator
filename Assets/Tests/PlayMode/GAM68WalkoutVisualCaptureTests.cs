using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Matched baseline/candidate visual evidence for the GAM-68 walkout. The
    /// fixture samples the actual foot colliders and the phase clock while it
    /// renders a fixed side sequence plus setup, lift, swing, touchdown,
    /// settle, handoff, descent and ascent views. It neither changes nor judges
    /// the physical squat; the separate locomotion fixture owns the contract.
    /// </summary>
    public sealed class GAM68WalkoutVisualCaptureTests
    {
        private const int MaxWaitFrames = 2400;
        private const int MaxWalkoutFrames = 1200;

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private SquatPhysicalPrototypeController _controller;
        private FoundationBootstrap _bootstrap;
        private PowerliftingGameUiController _ui;
        private PowerliftingCameraDirector _cameraDirector;
        private PowerliftingGymEnvironment _gym;

        private string _captureDirectory;
        private string _frameDirectory;
        private string _label;
        private Camera _camera;
        private Vector3 _focus;
        private Vector3 _sidePosition;
        private Vector3 _rearPosition;
        private Vector3 _threeQuarterPosition;
        private Vector3 _gameplayPosition;
        private float _gameplayFieldOfView;
        private Quaternion _sideRotation;
        private Quaternion _rearRotation;
        private Quaternion _threeQuarterRotation;
        private Quaternion _gameplayRotation;
        private int _frameIndex;
        private int _captureSerial;
        private int _stepOneFrames;
        private int _stepTwoFrames;
        private bool _firstLift;
        private bool _firstSwing;
        private bool _firstTouchdown;
        private bool _secondLift;
        private bool _secondSwing;
        private bool _secondTouchdown;
        private bool _settleCaptured;
        private bool _handoffCaptured;
        private bool _descentCaptured;
        private bool _ascentCaptured;
        private float _previousLeftClearance;
        private float _previousRightClearance;
        private readonly StringBuilder _trace = new StringBuilder();
        private readonly List<string> _labels = new List<string>();

        [SetUp]
        public void SetUp()
        {
            PowerliftingGameUiController.ClearPendingReloadScreen();
            _trace.Clear();
            _labels.Clear();
            _frameIndex = 0;
            _captureSerial = 0;
            _stepOneFrames = 0;
            Time.captureFramerate = 30;
            _stepTwoFrames = 0;
            _firstLift = false;
            _firstSwing = false;
            _firstTouchdown = false;
            _secondLift = false;
            _secondSwing = false;
            _secondTouchdown = false;
            _settleCaptured = false;
            _handoffCaptured = false;
            _descentCaptured = false;
            _ascentCaptured = false;
            _previousLeftClearance = 0f;
            _previousRightClearance = 0f;
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            LogAssert.ignoreFailingMessages = true;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            PowerliftingGameUiController.ClearPendingReloadScreen();
            Time.captureFramerate = 0;
            Time.timeScale = 1f;
            if (_controller != null)
                _controller.enabled = false;
            if (_cameraDirector != null)
                _cameraDirector.enabled = true;
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
            _cameraDirector = null;
            _gym = null;
            if (_input != null)
            {
                _input.TearDown();
                _input = null;
                _keyboard = null;
            }
            LogAssert.ignoreFailingMessages = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator CAPTURE_WALKOUT_PHASES_AND_A_SIDE_SEQUENCE()
        {
            _label = Environment.GetEnvironmentVariable("GAM68_WALKOUT_LABEL");
            if (string.IsNullOrWhiteSpace(_label))
                _label = "capture-current";
            _captureDirectory = Path.Combine(
                Directory.GetParent(Application.dataPath).FullName,
                "Artifacts", "Evidence", "GAM-68-walkout", _label, "capture");
            _frameDirectory = Path.Combine(_captureDirectory, "frames");
            Directory.CreateDirectory(_captureDirectory);
            Directory.CreateDirectory(_frameDirectory);
            _trace.AppendLine("index,time,phase,leftSoleX,leftSoleY,leftSoleZ,rightSoleX,rightSoleY,rightSoleZ," +
                "pelvisX,pelvisY,pelvisZ,barX,barY,barZ,leftClearance,rightClearance");

            yield return LoadFreshSceneZero();
            _ui.EnterTrainingGym();
            _ui.SelectSquatStation();
            yield return null;
            yield return WaitForPlayerReadiness();

            _ui.BeginPlayerSetupAction();
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.SETUP));
            yield return WaitRealtime(0.35f);

            _camera = _cameraDirector != null ? _cameraDirector.TargetCamera : Camera.main;
            Assert.That(_camera, Is.Not.Null);
            Vector3 pelvis = _controller.AthleteRig.Segments["pelvis"].Body.position;
            _focus = new Vector3(pelvis.x, pelvis.y - 0.10f, pelvis.z - 0.18f);
            _sidePosition = _focus + new Vector3(-3.6f, 0.25f, 0.60f);
            _rearPosition = _focus + new Vector3(0f, 0.50f, -3.4f);
            _threeQuarterPosition = _focus + new Vector3(-2.4f, 0.60f, -2.4f);
            _sideRotation = Quaternion.LookRotation((_focus - _sidePosition).normalized, Vector3.up);
            _rearRotation = Quaternion.LookRotation((_focus - _rearPosition).normalized, Vector3.up);
            _threeQuarterRotation = Quaternion.LookRotation((_focus - _threeQuarterPosition).normalized, Vector3.up);

            if (_cameraDirector != null)
            {
                _cameraDirector.SetActiveLiftCameraAngle(PowerliftingCameraAngle.ThreeQuarterCompetition, true);
                yield return null;
                _gameplayPosition = _camera.transform.position;
                _gameplayRotation = _camera.transform.rotation;
                _gameplayFieldOfView = _camera.fieldOfView;
            }
            else
            {
                _gameplayPosition = _focus + new Vector3(-2.0f, 1.1f, -2.6f);
                _gameplayRotation = Quaternion.LookRotation((_focus + Vector3.up * 0.35f - _gameplayPosition).normalized, Vector3.up);
                _gameplayFieldOfView = 40f;
            }

            CaptureKey("00_standing_before_walkout", true);
            if (_cameraDirector != null)
                _cameraDirector.enabled = false;

            _ui.BeginUnrackAndWalkoutAction();
            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.UNRACK));

            double start = Time.timeAsDouble;
            for (int frame = 0; frame < MaxWalkoutFrames; frame++)
            {
                yield return null;
                SquatPlayerGamePhase phase = _controller.PlayerGamePhase;
                Vector3 leftSole = Sole(_controller.AthleteRig, "left_foot");
                Vector3 rightSole = Sole(_controller.AthleteRig, "right_foot");
                Vector3 pelvisPosition = _controller.AthleteRig.Segments["pelvis"].Body.position;
                Vector3 barPosition = _controller.Saddle.Barbell.Body.position;
                float leftClearance = leftSole.y - PhysicalAthleteDefinition.PlatformSupportPlaneY;
                float rightClearance = rightSole.y - PhysicalAthleteDefinition.PlatformSupportPlaneY;
                _trace.AppendLine(string.Join(",", new[]
                {
                    _frameIndex.ToString(CultureInfo.InvariantCulture),
                    (Time.timeAsDouble - start).ToString("F5", CultureInfo.InvariantCulture),
                    phase.ToString(),
                    Format(leftSole), Format(rightSole), Format(pelvisPosition), Format(barPosition),
                    leftClearance.ToString("F5", CultureInfo.InvariantCulture),
                    rightClearance.ToString("F5", CultureInfo.InvariantCulture)
                }));

                if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
                    Render(_frameDirectory, $"side_{_frameIndex:0000}", _camera, _sidePosition, _sideRotation, 34f, 960, 540);

                if (phase == SquatPlayerGamePhase.WALKOUT_STEP_1)
                {
                    _stepOneFrames++;
                    if (!_firstLift && leftClearance > 0.012f)
                    {
                        _firstLift = true;
                        CaptureKey("02_first_foot_lifting", true);
                    }
                    if (!_firstSwing && leftClearance > 0.060f)
                    {
                        _firstSwing = true;
                        CaptureKey("03_first_foot_swinging", true);
                    }
                    if (_firstSwing && !_firstTouchdown && leftClearance < 0.001f)
                    {
                        _firstTouchdown = true;
                        CaptureKey("04_first_touchdown_and_transfer", true);
                    }
                    if (_stepOneFrames == 6)
                        CaptureKey("phase_step1_06", true);
                    else if (_stepOneFrames == 15)
                        CaptureKey("phase_step1_15", true);
                    else if (_stepOneFrames == 30)
                        CaptureKey("phase_step1_30", true);
                }
                else if (phase == SquatPlayerGamePhase.WALKOUT_STEP_2)
                {
                    _stepTwoFrames++;
                    if (!_secondLift && rightClearance > 0.012f)
                    {
                        _secondLift = true;
                        CaptureKey("05_second_foot_lifting", true);
                    }
                    if (!_secondSwing && rightClearance > 0.060f)
                    {
                        _secondSwing = true;
                        CaptureKey("06_second_foot_swinging", true);
                    }
                    if (_secondSwing && !_secondTouchdown && rightClearance < 0.001f)
                    {
                        _secondTouchdown = true;
                        CaptureKey("07_second_touchdown", true);
                    }
                    if (_stepTwoFrames == 6)
                        CaptureKey("phase_step2_06", true);
                    else if (_stepTwoFrames == 15)
                        CaptureKey("phase_step2_15", true);
                    else if (_stepTwoFrames == 30)
                        CaptureKey("phase_step2_30", true);
                }
                else if (phase == SquatPlayerGamePhase.SETTLE && !_settleCaptured)
                {
                    _settleCaptured = true;
                    CaptureKey("08_settled_two_foot_stance", true);
                }
                else if (phase == SquatPlayerGamePhase.JUDGED_SQUAT)
                {
                    if (!_handoffCaptured)
                    {
                        _handoffCaptured = true;
                        CaptureKey("09_physics_handoff", true);
                    }
                    break;
                }

                _previousLeftClearance = leftClearance;
                _previousRightClearance = rightClearance;
                _frameIndex++;
            }

            Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.JUDGED_SQUAT));
            Assert.That(_handoffCaptured, Is.True);

            Press(_keyboard.sKey);
            for (int frame = 0; frame < MaxWaitFrames && _controller.Adapter.State != SquatState.BOTTOM; frame++)
            {
                if (!_descentCaptured && _controller.Adapter.Sq >= 0.5f)
                {
                    _descentCaptured = true;
                    CaptureKey("10_judged_descent", true);
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.BOTTOM));
            CaptureKey("11_judged_bottom", true);
            Release(_keyboard.sKey);
            yield return new WaitForSecondsRealtime(0.02f);
            Press(_keyboard.wKey);
            for (int frame = 0; frame < MaxWaitFrames && _controller.Adapter.State != SquatState.LOCKOUT; frame++)
            {
                if (!_ascentCaptured && _controller.Adapter.Sq <= 0.5f && _controller.Adapter.State != SquatState.BOTTOM)
                {
                    _ascentCaptured = true;
                    CaptureKey("12_judged_ascent", true);
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.LOCKOUT));
            Release(_keyboard.wKey);
            CaptureKey("13_judged_lockout", true);

            File.WriteAllText(Path.Combine(_captureDirectory, "walkout-capture.csv"), _trace.ToString());
            File.WriteAllLines(Path.Combine(_captureDirectory, "capture-labels.csv"), _labels);
            TestContext.WriteLine($"GAM68_WALKOUT_CAPTURE label={_label} directory={_captureDirectory} frames={_frameIndex + 1}");
        }

        private void CaptureKey(string label, bool includeGameplay)
        {
            _labels.Add($"{label},{_captureSerial},{_frameIndex},{_controller.PlayerGamePhase}");
            _captureSerial++;
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;
            Camera camera = _camera;
            Vector3 savedPosition = camera.transform.position;
            Quaternion savedRotation = camera.transform.rotation;
            float savedFov = camera.fieldOfView;
            bool savedDirector = _cameraDirector != null && _cameraDirector.enabled;
            if (_cameraDirector != null)
                _cameraDirector.enabled = false;
            Transform station = _gym != null ? _gym.SquatStationRoot : null;
            bool stationWasActive = station != null && station.gameObject.activeSelf;
            try
            {
                if (includeGameplay)
                {
                    camera.transform.position = _gameplayPosition;
                    camera.transform.rotation = _gameplayRotation;
                    camera.fieldOfView = _gameplayFieldOfView;
                    RenderWithHud(label + "_gameplay", 1920, 1080);
                }
                if (station != null)
                    station.gameObject.SetActive(false);
                Render(_captureDirectory, label + "_side", camera, _sidePosition, _sideRotation, 34f, 1280, 720);
                Render(_captureDirectory, label + "_rear", camera, _rearPosition, _rearRotation, 34f, 1280, 720);
                Render(_captureDirectory, label + "_threequarter", camera, _threeQuarterPosition,
                    _threeQuarterRotation, 38f, 1280, 720);
            }
            finally
            {
                if (station != null)
                    station.gameObject.SetActive(stationWasActive);
                camera.transform.position = savedPosition;
                camera.transform.rotation = savedRotation;
                camera.fieldOfView = savedFov;
                if (_cameraDirector != null)
                    _cameraDirector.enabled = savedDirector;
            }
        }

        private void RenderWithHud(string fileName, int width, int height)
        {
            Canvas canvas = _ui != null ? _ui.GetComponent<Canvas>() : null;
            RenderMode previousMode = RenderMode.ScreenSpaceOverlay;
            Camera previousCamera = null;
            float previousPlane = 100f;
            if (canvas != null)
            {
                _ui.RefreshUiImmediate();
                previousMode = canvas.renderMode;
                previousCamera = canvas.worldCamera;
                previousPlane = canvas.planeDistance;
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = _camera;
                canvas.planeDistance = 0.25f;
                Canvas.ForceUpdateCanvases();
            }
            try
            {
                Render(_captureDirectory, fileName, _camera, width, height);
            }
            finally
            {
                if (canvas != null)
                {
                    canvas.renderMode = previousMode;
                    canvas.worldCamera = previousCamera;
                    canvas.planeDistance = previousPlane;
                }
            }
        }

        private static Vector3 Sole(PhysicalAthleteRig rig, string footId)
        {
            PhysicalAthleteRig.SegmentRuntime foot = rig.Segments[footId];
            BoxCollider box = (BoxCollider)foot.Collider;
            Vector3 localSole = box.center - new Vector3(0f, box.size.y * 0.5f, 0f);
            return foot.Body.transform.TransformPoint(localSole);
        }

        private static string Format(Vector3 value) =>
            string.Format(CultureInfo.InvariantCulture, "{0:F5},{1:F5},{2:F5}", value.x, value.y, value.z);

        private static void Render(
            string directory,
            string fileName,
            Camera camera,
            Vector3 position,
            Quaternion rotation,
            float fieldOfView,
            int width,
            int height)
        {
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                camera.transform.position = position;
                camera.transform.rotation = rotation;
                camera.fieldOfView = fieldOfView;
                Render(directory, fileName, camera, width, height);
            }
            finally
            {
                camera.transform.position = previousPosition;
                camera.transform.rotation = previousRotation;
                camera.fieldOfView = previousFov;
            }
        }

        private static void Render(string directory, string fileName, Camera camera, int width, int height)
        {
            RenderTexture target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                texture.Apply();
                File.WriteAllBytes(Path.Combine(directory, fileName + ".png"), texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
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

        private IEnumerator LoadFreshSceneZero()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (load != null && !load.isDone)
                yield return null;
            yield return null;
            for (int frame = 0; frame < 500; frame++)
            {
                _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
                _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
                _ui = UnityEngine.Object.FindFirstObjectByType<PowerliftingGameUiController>();
                _cameraDirector = UnityEngine.Object.FindFirstObjectByType<PowerliftingCameraDirector>();
                _gym = UnityEngine.Object.FindFirstObjectByType<PowerliftingGymEnvironment>();
                if (_controller != null && _controller.IsInitialized && _bootstrap != null &&
                    _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
                    yield break;
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.Fail("The player-facing squat scene did not initialize.");
        }

        private IEnumerator WaitForPlayerReadiness()
        {
            for (int frame = 0; frame < MaxWaitFrames && !_controller.CanBeginPlayerSetup; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.CanBeginPlayerSetup, Is.True,
                "The production start predicate never reached its stable 25 kg setup pose.");
        }

        private static IEnumerator WaitRealtime(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
                yield return null;
        }
    }
}
