using System.Collections;
using System.Globalization;
using NUnit.Framework;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
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
