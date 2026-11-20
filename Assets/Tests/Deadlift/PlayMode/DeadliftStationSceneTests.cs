using System.Collections;
using System.Globalization;
using NUnit.Framework;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70: the shipped gym station scene, run by the foundation's own clock
    /// as in the player.
    /// </summary>
    public sealed class DeadliftStationSceneTests
    {
        [TearDown]
        public void ResetMode() => DeadliftStationController.StockPathOverride = null;

        /// <summary>Negative control: the stock qualified path, Drive injected through the pull controller's seam.</summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator STATION_SCENE_RUNS_A_GOOD_LIFT_ON_THE_FOUNDATION_CLOCK()
        {
            DeadliftStationController.StockPathOverride = true;
            yield return LoadStation();
            DeadliftStationController station = Object.FindFirstObjectByType<DeadliftStationController>();
            DeadliftAttemptRuntime runtime = station.Runtime;
            Assert.That(runtime.PreparedCloseLockout, Is.False);
            var trace = new DeadliftRuntimeTrace();
            trace.Attach(runtime);
            runtime.Pull.IntentOverride01 = 0f;
            // Drive onset on the same tick as the qualified plant run (applied from tick 301).
            runtime.TickCompleted += rt => { if (rt.Tick == 300) rt.Pull.IntentOverride01 = 1f; };

            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < 240f)
            {
                if (runtime.GripsReleased)
                    break;
                yield return null;
            }
            string report = string.Format(CultureInfo.InvariantCulture,
                "station scene: tick {0} | phase {1} | tick-0 penetration {2:F2} mm | {3}",
                runtime.Tick, runtime.Pull.Phase, runtime.TickZeroMaxPenetrationM * 1000f, runtime.Referee.Summary());
            F.WriteEvidence("gate8-station-scene.txt", report);
            F.WriteEvidence("gate8-trace-scene.csv", trace.Rows.ToString());
            Assert.That(runtime.Referee.GoodLift, Is.True, report);
            Assert.That(runtime.GripsReleased, Is.True, report);

            yield return ShutdownStation();
        }

        /// <summary>
        /// The shipped close-bar station driven only by the Input System keyboard binding: W is pressed when the
        /// station reports Ready and held; the simulation must never be suspended, Drive must stay the raw player
        /// command, and the measured preparation decides when the pull starts.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator CLOSE_BAR_STATION_USES_HELD_KEYBOARD_DRIVE()
        {
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                yield return LoadStation();
                var station = Object.FindFirstObjectByType<DeadliftStationController>();
                var foundation = Object.FindFirstObjectByType<FoundationBootstrap>();
                DeadliftAttemptRuntime rt = station.Runtime;
                Assert.That(rt.PreparedCloseLockout && rt.Admission != null && rt.StockPathFromArtifact, Is.True);
                Assert.That(rt.PlayerSetup, Is.True);
                float deadline = Time.realtimeSinceStartup + 240f;
                bool suspended = false;
                // The player taps Space once to brace, then holds Left Shift to take the slack out.
                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Press(keyboard.spaceKey);
                for (int i = 0; i < 5; i++)
                    yield return null;
                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Release(keyboard.spaceKey);
                for (int i = 0; i < 5; i++)
                    yield return null;
                Assert.That(rt.LastBraceHeld, Is.True, "one tap must set the brace");
                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Press(keyboard.leftShiftKey);
                while (!(rt.PretensionEstablished && rt.Pull.Phase == DeadliftPullPhase.Grounded) && Time.realtimeSinceStartup < deadline)
                {
                    suspended |= foundation.Runtime.TicksSuspended;
                    yield return null;
                }
                Assert.That(rt.Pull.Phase, Is.EqualTo(DeadliftPullPhase.Grounded), "brace and grip alone must not pull");
                ulong readyTick = rt.Tick;

                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Press(keyboard.wKey);
                ulong pressTick = rt.Tick, driveTick = 0, preparationTick = 0;
                bool capturedDrive = false;
                float maxFrame = 0f, maxFrameAscent = 0f;
                long gcBefore = System.GC.CollectionCount(0);
                long allocatedBefore = System.GC.GetTotalMemory(false);
                while (!rt.Referee.DownGiven && !rt.Referee.Completed && Time.realtimeSinceStartup < deadline && string.IsNullOrEmpty(station.StartupFailure))
                {
                    yield return null;
                    suspended |= foundation.Runtime.TicksSuspended;
                    maxFrame = Mathf.Max(maxFrame, Time.unscaledDeltaTime);
                    if (rt.Pull.Phase >= DeadliftPullPhase.Separated)
                        maxFrameAscent = Mathf.Max(maxFrameAscent, Time.unscaledDeltaTime);
                    if (driveTick == 0 && rt.Pull.LastRawDrive01 > 0.05f)
                        driveTick = rt.Tick;
                    if (preparationTick == 0 && !rt.Hold.PreparationOffsetFor("left_upper_arm").Equals(Quaternion.identity))
                        preparationTick = rt.Tick;
                    capturedDrive |= rt.Pull.Phase == DeadliftPullPhase.Lifting && rt.Pull.LastRawDrive01 > 0.99f;
                }
                int collections = System.GC.CollectionCount(0) - (int)gcBefore;
                string report = string.Format(CultureInfo.InvariantCulture,
                    "close-bar station: load {0:F0} ms ({1}); ready tick {2}; W pressed at tick {3}, Drive in the simulation at {4}, " +
                    "shoulder preparation from {5}, PULL {6}, separation {7}; ascent {8}; post-knee {9}; DOWN {10}; " +
                    "max render frame {11:F1} ms ({12:F1} ms after separation); max attempt tick work {13:F2} ms @ {14}; " +
                    "gen-0 collections during the lift {15}, managed heap {16:F1} MB -> {17:F1} MB; suspended {18}; {19}",
                    station.LoadMilliseconds, string.Join(", ", System.Linq.Enumerable.Select(rt.InitializationMilliseconds, kv => $"{kv.Key} {kv.Value:F0} ms")),
                    readyTick, pressTick, driveTick, preparationTick, rt.Pull.SeparationTick == 0 ? 0 : FirstPull(rt), rt.Pull.SeparationTick,
                    rt.AscentAdmission?.Describe(), rt.PostKneeAdmission?.Describe(), rt.Referee.DownTick,
                    maxFrame * 1000f, maxFrameAscent * 1000f, rt.MaxTickMilliseconds, rt.MaxTickMillisecondsTick,
                    collections, allocatedBefore / 1048576.0, System.GC.GetTotalMemory(false) / 1048576.0, suspended, rt.Referee.Summary());
                Debug.Log(report);
                F.WriteEvidence("close-bar-station.txt", report);

                Assert.That(suspended, Is.False, "the simulation was suspended during the attempt");
                Assert.That(rt.Pull.IntentOverride01, Is.Null);
                Assert.That(capturedDrive, Is.True, report);
                Assert.That(rt.Referee.DownGiven, Is.True, report);
                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Release(keyboard.wKey);

                while (!rt.GripsReleased && Time.realtimeSinceStartup < deadline && string.IsNullOrEmpty(station.StartupFailure))
                    yield return null;
                Assert.That(station.StartupFailure, Is.Empty);
                Assert.That(rt.Referee.GoodLift && rt.GripsReleased, Is.True, rt.Referee.Summary());
                yield return ShutdownStation();
            }
            finally
            {
                input.TearDown();
            }
        }

        /// <summary>R reloads the station exactly once; the new foundation clock accepts input without a timestamp exception.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator R_RESTARTS_THE_STATION_ONCE_WITH_A_FRESH_CLOCK_AND_INPUT()
        {
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                yield return LoadStation();
                var first = Object.FindFirstObjectByType<DeadliftStationController>();
                float deadline = Time.realtimeSinceStartup + 120f;
                while (first.Runtime.Tick < 150 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                int loads = 0;
                void Count(Scene s, LoadSceneMode m) => loads++;
                SceneManager.sceneLoaded += Count;
                try
                {
                    input.currentTime = Time.realtimeSinceStartupAsDouble;
                    input.Press(keyboard.rKey);
                    yield return null;
                    yield return null;
                    input.currentTime = Time.realtimeSinceStartupAsDouble;
                    input.Release(keyboard.rKey);
                    DeadliftStationController second = null;
                    while (Time.realtimeSinceStartup < deadline)
                    {
                        second = Object.FindFirstObjectByType<DeadliftStationController>();
                        if (second != null && second != first && (second.IsInitialized || !string.IsNullOrEmpty(second.StartupFailure)))
                            break;
                        yield return null;
                    }
                    Assert.That(second, Is.Not.Null.And.Not.EqualTo(first));
                    Assert.That(second.StartupFailure, Is.Empty);
                    // Hold W across the fresh clock: input must reach the new simulation.
                    input.currentTime = Time.realtimeSinceStartupAsDouble;
                    input.Press(keyboard.wKey);
                    while (second.Runtime.Pull.LastRawDrive01 < 0.99f && Time.realtimeSinceStartup < deadline)
                        yield return null;
                    for (int i = 0; i < 30; i++)
                        yield return null;
                    Assert.That(loads, Is.EqualTo(1), "R must reload the station exactly once");
                    Assert.That(second.Runtime.Pull.LastRawDrive01, Is.GreaterThan(0.99f));
                    Assert.That(second.Runtime.Tick, Is.GreaterThan(0ul));
                    input.currentTime = Time.realtimeSinceStartupAsDouble;
                    input.Release(keyboard.wKey);
                }
                finally
                {
                    SceneManager.sceneLoaded -= Count;
                }
                yield return ShutdownStation();
            }
            finally
            {
                input.TearDown();
            }
        }

        private static ulong FirstPull(DeadliftAttemptRuntime rt) => rt.Pull.SeparationTick - (ulong)rt.Pull.PullTicks;

        private static IEnumerator LoadStation()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(DeadliftStationController.SceneName, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            DeadliftStationController station = null;
            float t0 = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t0 < 120f)
            {
                station = Object.FindFirstObjectByType<DeadliftStationController>();
                if (station != null && (station.IsInitialized || !string.IsNullOrEmpty(station.StartupFailure)))
                    break;
                yield return null;
            }
            Assert.That(station, Is.Not.Null, "no deadlift station in the scene");
            Assert.That(station.StartupFailure, Is.Empty, station.StartupFailure);
            Assert.That(station.IsInitialized, Is.True, "the station did not initialize");
        }

        private static IEnumerator ShutdownStation()
        {
            FoundationBootstrap foundation = Object.FindFirstObjectByType<FoundationBootstrap>();
            if (foundation != null && foundation.Runtime != null && foundation.Runtime.IsInitialized)
            {
                AsyncOperation unload = foundation.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
        }
    }
}
