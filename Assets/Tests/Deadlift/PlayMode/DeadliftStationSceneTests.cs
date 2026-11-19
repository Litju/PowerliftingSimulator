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
    /// GAM-70: the shipped gym station scene, run by the foundation's own
    /// clock as in the player. Only the player's Drive is injected (the pull
    /// controller's deterministic seam stands in for holding W with Brace and
    /// Grip); everything else is the scene as built.
    /// </summary>
    public sealed class DeadliftStationSceneTests
    {
        [UnityTest, Timeout(900000)]
        public IEnumerator STATION_SCENE_RUNS_A_GOOD_LIFT_ON_THE_FOUNDATION_CLOCK()
        {
            yield return LoadStation();
            DeadliftStationController station = Object.FindFirstObjectByType<DeadliftStationController>();
            DeadliftAttemptRuntime runtime = station.Runtime;
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

        [UnityTest, Timeout(900000), Explicit("Run Unity with -deadliftPreparedRehearsal")]
        public IEnumerator PREPARED_REHEARSAL_SOLVES_ACROSS_FRAMES_AND_KEEPS_ITS_JUDGED_LIFT()
        {
            Assert.That(System.Environment.GetCommandLineArgs(), Does.Contain(DeadliftStationController.PreparedRehearsalArgument));

            var input = new InputTestFixture();
            input.Setup();
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                yield return LoadStation();
                var station = Object.FindFirstObjectByType<DeadliftStationController>();
                var foundation = Object.FindFirstObjectByType<FoundationBootstrap>();
                DeadliftAttemptRuntime runtime = station.Runtime;
                Assert.That(foundation.Runtime.TicksSuspended, Is.True);
                yield return null;
                Assert.That(runtime.Tick, Is.Zero, "The rehearsal must wait at tick zero for W.");

                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Press(keyboard.wKey);
                float deadline = Time.realtimeSinceStartup + 5f;
                while (runtime.Tick == 0 && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(runtime.Tick, Is.GreaterThan(0ul), "W must start the rehearsal.");
                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Release(keyboard.wKey);

                deadline = Time.realtimeSinceStartup + 180f;
                while (!foundation.Runtime.TicksSuspended && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(foundation.Runtime.TicksSuspended, Is.True);
                Assert.That(runtime.Tick, Is.EqualTo(316ul));
                for (int frame = 0; frame < 3; frame++)
                {
                    yield return null;
                    Assert.That(runtime.Tick, Is.EqualTo(316ul));
                }

                while (!runtime.GripsReleased && Time.realtimeSinceStartup < deadline && string.IsNullOrEmpty(station.StartupFailure))
                    yield return null;
                Assert.That(station.StartupFailure, Is.Empty);
                Assert.That(runtime.Referee.GoodLift, Is.True, runtime.Referee.Summary());
                Assert.That(runtime.GripsReleased, Is.True);
                Assert.That(runtime.Referee.DownTick, Is.EqualTo(856ul));
                yield return ShutdownStation();
            }
            finally
            {
                input.TearDown();
            }
        }

        [UnityTest, Timeout(900000), Explicit("Run Unity with -deadliftCloseLockout")]
        public IEnumerator CLOSE_BAR_STATION_USES_HELD_KEYBOARD_DRIVE()
        {
            Assert.That(System.Environment.GetCommandLineArgs(), Does.Contain(DeadliftStationController.PreparedCloseLockoutArgument));
            var input = new InputTestFixture();
            input.Setup();
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                yield return LoadStation();
                var station = Object.FindFirstObjectByType<DeadliftStationController>();
                var foundation = Object.FindFirstObjectByType<FoundationBootstrap>();
                DeadliftAttemptRuntime rt = station.Runtime;
                float deadline = Time.realtimeSinceStartup + 180f;
                while (!foundation.Runtime.TicksSuspended && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(rt.Tick, Is.EqualTo(DeadliftAttemptRuntime.PreparedDriveReadyTick));
                Assert.That(rt.Pull.Phase, Is.EqualTo(DeadliftPullPhase.Grounded));
                yield return null;
                Assert.That(rt.Tick, Is.EqualTo(DeadliftAttemptRuntime.PreparedDriveReadyTick));

                input.currentTime = Time.realtimeSinceStartupAsDouble;
                input.Press(keyboard.wKey);
                bool capturedDrive = false;
                ulong lastSuspended = 0;
                int measuredSolvePauses = 0;
                while (!rt.Referee.DownGiven && Time.realtimeSinceStartup < deadline && string.IsNullOrEmpty(station.StartupFailure))
                {
                    capturedDrive |= rt.Pull.Phase == DeadliftPullPhase.Lifting && rt.Pull.LastRawDrive01 > 0.99f;
                    if (foundation.Runtime.TicksSuspended && rt.Tick > DeadliftAttemptRuntime.PreparedDriveReadyTick && rt.Tick != lastSuspended)
                    {
                        lastSuspended = rt.Tick;
                        measuredSolvePauses++;
                    }
                    yield return null;
                }
                Assert.That(rt.Pull.IntentOverride01, Is.Null);
                Assert.That(capturedDrive, Is.True);
                Assert.That(measuredSolvePauses, Is.EqualTo(2));
                Assert.That(rt.Referee.DownTick, Is.EqualTo(762ul), rt.Referee.Summary());
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
