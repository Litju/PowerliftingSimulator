using System.Collections;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>
    /// GAM-69 Gate E: the player-facing bench station end to end on the real
    /// solver. The gym routes to the bench scene; the bench UI shows setup,
    /// the referee's signals, a truthful result, retry and the way back. The
    /// lift goes through the controller's player-intent path; the UI may only
    /// request what a player may request.
    /// </summary>
    public sealed partial class BenchGameFlowTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;

        private static BenchGameUiController Ui => Object.FindFirstObjectByType<BenchGameUiController>();

        [UnityTest]
        public IEnumerator GATE_E_GYM_BENCH_STATION_ROUTES_TO_THE_BENCH_AND_BACK()
        {
            PowerliftingGameUiController.ClearPendingReloadScreen();
            yield return LoadScene(BenchGameUiController.GymSceneName);
            PowerliftingGameUiController gym = Object.FindFirstObjectByType<PowerliftingGameUiController>();
            Assert.That(gym, Is.Not.Null, "gym UI missing");
            gym.EnterTrainingGym();
            Assert.That(gym.CurrentScreen, Is.EqualTo(PowerliftingScreenState.TrainingGym));
            gym.InspectBenchStation();
            Assert.That(gym.GymStationNotice, Does.Contain("PLAYABLE"));
            gym.SelectBenchStation();
            yield return WaitForScene(BenchPressPrototypeController.SceneName);
            yield return WaitForBench();

            BenchPressPrototypeController bench = H.Bench;
            Assert.That(Ui, Is.Not.Null);
            Assert.That(Ui.Screen, Is.EqualTo(BenchScreen.Setup));
            Assert.That(bench.LoadKg, Is.EqualTo(BenchPressPrototypeController.QualifiedLoadKg));
            Assert.That(bench.Barbell.Body.isKinematic, Is.False, "the bench bar must be dynamic");
            Assert.That(Object.FindFirstObjectByType<SquatPhysicalPrototypeController>(), Is.Null, "the squat controller must not be in the bench scene");
            PowerliftingGymEnvironment env = Object.FindFirstObjectByType<PowerliftingGymEnvironment>();
            Assert.That(env, Is.Not.Null);
            Assert.That(env.GetComponentsInChildren<Collider>(true), Is.Empty, "decorative gym geometry must not have colliders");
            Assert.That(env.BenchStationRoot.gameObject.activeSelf, Is.False, "the decorative bench must be hidden behind the authoritative one");
            yield return H.WaitTicks(30);
            Assert.That(Ui.DisplayedSetupChecks, Does.Contain("Grip width"));
            Assert.That(Ui.DisplayedSetupChecks, Does.Not.Contain("#e03838"), "a setup check is red at rest:\n" + Ui.DisplayedSetupChecks);

            Ui.ReturnToGym();
            yield return WaitForScene(BenchGameUiController.GymSceneName);
            yield return null;
            gym = Object.FindFirstObjectByType<PowerliftingGameUiController>();
            Assert.That(gym, Is.Not.Null);
            Assert.That(gym.CurrentScreen, Is.EqualTo(PowerliftingScreenState.TrainingGym), "return lands in the training gym");
        }

        [UnityTest]
        public IEnumerator GATE_E_UNQUALIFIED_LOADS_ARE_OFFERED_BUT_NOT_SELECTABLE()
        {
            yield return H.LoadBench();
            BenchPressPrototypeController before = H.Bench;
            foreach (float kg in BenchGameUiController.DisplayedLoadsKg.Where(k => !BenchGameUiController.IsQualifiedLoad(k)))
            {
                Ui.SelectLoad(kg);
                yield return null;
                yield return null;
                Assert.That(Object.FindFirstObjectByType<BenchPressPrototypeController>(), Is.SameAs(before), $"{kg} kg reloaded the bench");
                Assert.That(before.LoadKg, Is.EqualTo(25f));
                Assert.That(Ui.DisplayedSetupStatus, Does.Contain("NOT physically qualified"));
            }
            Assert.That(BenchGameUiController.DisplayedLoadsKg.Count(BenchGameUiController.IsQualifiedLoad), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------

        private static IEnumerator LoadScene(string name)
        {
            AsyncOperation op = SceneManager.LoadSceneAsync(name, LoadSceneMode.Single);
            Assert.That(op, Is.Not.Null, name + " must be in the build settings");
            while (!op.isDone)
                yield return null;
            yield return null;
        }

        private static IEnumerator WaitForScene(string name)
        {
            float start = Time.realtimeSinceStartup;
            while (SceneManager.GetActiveScene().name != name)
            {
                Assert.That(Time.realtimeSinceStartup - start, Is.LessThan(60f), "scene " + name + " did not load");
                yield return null;
            }
            yield return null;
        }

        private static IEnumerator WaitForBench(BenchPressPrototypeController previous = null)
        {
            float start = Time.realtimeSinceStartup;
            while (true)
            {
                BenchPressPrototypeController bench = Object.FindFirstObjectByType<BenchPressPrototypeController>();
                if (bench != null && bench != previous && (bench.IsInitialized || !string.IsNullOrEmpty(bench.StartupFailure)))
                {
                    Assert.That(bench.IsInitialized, bench.StartupFailure);
                    H.Adopt(bench);
                    yield break;
                }
                Assert.That(Time.realtimeSinceStartup - start, Is.LessThan(60f), "bench did not initialize");
                yield return null;
            }
        }
    }
}
