using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM63HybridSquatFlowTests
    {
        private const int FreshAttemptCount = 10;
        private const int MaximumWaitFrames = 1800;
        private const string GameScene = "SquatPhysicalPrototype";

        private InputTestFixture _input;
        private Keyboard _keyboard;
        private SquatPhysicalPrototypeController _controller;
        private FoundationBootstrap _bootstrap;
        private readonly List<string> _unexpectedErrors = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _unexpectedErrors.Clear();
            Application.logMessageReceived += CaptureUnexpectedError;
            LogAssert.ignoreFailingMessages = true;
            _input = new InputTestFixture();
            _input.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
                Object.DestroyImmediate(_bootstrap.gameObject);
            _controller = null;
            _bootstrap = null;
            if (_input != null)
            {
                _input.TearDown();
                _input = null;
                _keyboard = null;
            }
            Application.logMessageReceived -= CaptureUnexpectedError;
            LogAssert.ignoreFailingMessages = false;
            Assert.That(_unexpectedErrors, Is.Empty, string.Join("\n", _unexpectedErrors));
            yield return null;
        }

        [UnityTest]
        public IEnumerator TEN_FRESH_25KG_ATTEMPTS_COMPLETE_HYBRID_WALKOUT_PHYSICS_RERACK_AND_RETRY()
        {
            yield return LoadFreshGame();

            for (int attempt = 0; attempt < FreshAttemptCount; attempt++)
            {
                yield return WaitForPlayerReadiness();
                Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f), $"attempt {attempt + 1} did not start at 25 kg");

                _controller.StartGameFromPlayerUi();
                Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.SETUP));
                foreach (PhysicalAthleteRig.SegmentRuntime segment in _controller.AthleteRig.Segments.Values)
                    Assert.That(segment.Body.isKinematic, Is.True, "Locomotion must exclusively own the athlete pose.");
                Assert.That(_controller.Saddle.Barbell.Body.isKinematic, Is.True);

                _controller.UnrackFromPlayerUi();
                var seen = new HashSet<SquatPlayerGamePhase>();
                for (int frame = 0; frame < 600 && _controller.PlayerGamePhase != SquatPlayerGamePhase.JUDGED_SQUAT; frame++)
                {
                    seen.Add(_controller.PlayerGamePhase);
                    yield return new WaitForSecondsRealtime(0.01f);
                }

                Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.JUDGED_SQUAT),
                    $"attempt {attempt + 1} did not leave walkout; last phase={_controller.PlayerGamePhase}");
                Assert.That(seen, Does.Contain(SquatPlayerGamePhase.UNRACK));
                Assert.That(seen, Does.Contain(SquatPlayerGamePhase.WALKOUT_STEP_1));
                Assert.That(seen, Does.Contain(SquatPlayerGamePhase.WALKOUT_STEP_2));
                Assert.That(seen, Does.Contain(SquatPlayerGamePhase.SETTLE));
                Assert.That(_controller.HybridWalkoutDistanceMeters, Is.InRange(0.31f, 0.41f));
                Assert.That(_controller.HybridHandoffPoseErrorMeters, Is.LessThan(0.001f));
                Assert.That(_controller.HybridHandoffBarAngularErrorDegrees, Is.LessThan(0.1f));
                Assert.That(_controller.AttemptOrchestrator.HasStarted, Is.True);
                foreach (PhysicalAthleteRig.SegmentRuntime segment in _controller.AthleteRig.Segments.Values)
                    Assert.That(segment.Body.isKinematic, Is.False, "The physical athlete must own the judged squat.");
                Assert.That(_controller.Saddle.Barbell.Body.isKinematic, Is.False);

                Press(_keyboard.sKey);
                for (int frame = 0; frame < 700 && _controller.Adapter.State != SquatState.BOTTOM; frame++)
                    yield return new WaitForSecondsRealtime(0.01f);
                Assert.That(_controller.Adapter.State, Is.EqualTo(SquatState.BOTTOM),
                    $"attempt {attempt + 1} failed to reach depth; state={_controller.Adapter.State}, sq={_controller.Adapter.Sq:F3}");

                Release(_keyboard.sKey);
                yield return new WaitForSecondsRealtime(0.02f);
                Press(_keyboard.wKey);
                for (int frame = 0; frame < MaximumWaitFrames && _controller.AttemptRecord == null; frame++)
                    yield return new WaitForSecondsRealtime(0.01f);
                Release(_keyboard.wKey);

                SquatAttemptRecord record = _controller.AttemptRecord;
                Assert.That(record, Is.Not.Null, $"attempt {attempt + 1} did not produce a truthful result");
                Assert.That(record.TerminalReason, Is.EqualTo(SquatAttemptTerminalReason.PHYSICAL_LOCKOUT));
                Assert.That(record.PhysicalFailureOutcome, Is.EqualTo(SquatFailureResultKind.NO_PHYSICAL_FAILURE));

                for (int frame = 0; frame < 300 && _controller.PlayerGamePhase != SquatPlayerGamePhase.RERACK; frame++)
                    yield return new WaitForSecondsRealtime(0.01f);
                Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.RERACK),
                    "Only a stable physical lockout can transfer back to animation authority.");
                Assert.That(_controller.Saddle.Barbell.Body.isKinematic, Is.True);

                for (int frame = 0; frame < 300 && _controller.PlayerGamePhase != SquatPlayerGamePhase.RESULT; frame++)
                    yield return new WaitForSecondsRealtime(0.01f);
                Assert.That(_controller.PlayerGamePhase, Is.EqualTo(SquatPlayerGamePhase.RESULT));
                Assert.That(_controller.AttemptRecord, Is.SameAs(record));

                if (attempt + 1 < FreshAttemptCount)
                {
                    SquatPhysicalPrototypeController completedController = _controller;
                    _controller.RetryFromPlayerUi();
                    yield return WaitForRetriedGame(completedController);
                    Assert.That(_controller.CurrentLoadKg, Is.EqualTo(25f), "Retry must preserve the selected 25 kg load.");
                    Assert.That(_controller.AttemptLifecycle.State, Is.EqualTo(SquatAttemptLifecycleState.IDLE));
                    Assert.That(_controller.AttemptRecord, Is.Null);
                }
            }
        }

        private IEnumerator LoadFreshGame()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(0, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The squat game must be the first player build scene.");
            while (!load.isDone)
                yield return null;
            yield return WaitForLoadedGame();
        }

        private IEnumerator WaitForRetriedGame(SquatPhysicalPrototypeController previous)
        {
            for (int frame = 0; frame < 500; frame++)
            {
                SquatPhysicalPrototypeController candidate = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
                if (candidate != null && candidate != previous && candidate.IsInitialized)
                {
                    _controller = candidate;
                    _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
                    yield break;
                }
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.Fail("Retry did not load a fresh player game scene.");
        }

        private IEnumerator WaitForLoadedGame()
        {
            for (int frame = 0; frame < 500; frame++)
            {
                _controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
                _bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
                if (_controller != null && _controller.IsInitialized && _bootstrap != null &&
                    _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
                    yield break;
                yield return new WaitForSecondsRealtime(0.01f);
            }
            Assert.Fail("The player-facing squat scene did not initialize.");
        }

        private IEnumerator WaitForPlayerReadiness()
        {
            for (int frame = 0; frame < MaximumWaitFrames && !_controller.CanBeginPlayerSetup; frame++)
                yield return new WaitForSecondsRealtime(0.01f);
            Assert.That(_controller.CanBeginPlayerSetup, Is.True,
                "The production start predicate never reached its stable 25 kg setup pose.");
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

        private void CaptureUnexpectedError(string condition, string stackTrace, LogType type)
        {
            if (type != LogType.Error && type != LogType.Exception)
                return;
            if (type == LogType.Error && condition.StartsWith("connection.state_change", System.StringComparison.Ordinal))
                return;
            _unexpectedErrors.Add(condition);
        }
    }
}
