using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Bench
{
    /// <summary>Shared scene loading, tick waiting and evidence output for the bench gates.</summary>
    public static class BenchTestHarness
    {
        public static string EvidenceDirectory
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("GAM69_EVIDENCE_DIR");
                string dir = string.IsNullOrEmpty(env)
                    ? Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Logs", "GAM69")
                    : env;
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static BenchPressPrototypeController Bench { get; private set; }

        public static IEnumerator LoadBench(float loadKg = 25f)
        {
            BenchPressPrototypeController.SetPendingLoad(loadKg);
            AsyncOperation load = SceneManager.LoadSceneAsync(BenchPressPrototypeController.SceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The bench scene must be in the build settings.");
            while (!load.isDone)
                yield return null;
            Bench = null;
            for (int i = 0; i < 600; i++)
            {
                Bench = UnityEngine.Object.FindFirstObjectByType<BenchPressPrototypeController>();
                if (Bench != null && (Bench.IsInitialized || !string.IsNullOrEmpty(Bench.StartupFailure)))
                    break;
                yield return null;
            }
            Assert.That(Bench, Is.Not.Null, "Bench controller missing.");
            Assert.That(Bench.IsInitialized, Is.True, "Bench failed to initialize: " + Bench.StartupFailure +
                (Bench.Model != null ? "\n" + Bench.Model.Describe() : string.Empty));
        }

        public static ulong Tick => Bench.Foundation.Runtime.CurrentTime.Tick;

        public static IEnumerator WaitUntilTick(ulong tick, float timeoutSeconds = 60f)
        {
            float start = Time.realtimeSinceStartup;
            while (Tick < tick)
            {
                if (Time.realtimeSinceStartup - start > timeoutSeconds)
                    Assert.Fail($"Timed out waiting for tick {tick} (at {Tick}).");
                yield return null;
            }
        }

        public static IEnumerator WaitTicks(int ticks, float timeoutSeconds = 60f) => WaitUntilTick(Tick + (ulong)ticks, timeoutSeconds);

        public static IEnumerator WaitFor(Func<bool> condition, int maxTicks, string what)
        {
            ulong end = Tick + (ulong)maxTicks;
            float start = Time.realtimeSinceStartup;
            while (!condition())
            {
                if (Tick > end || Time.realtimeSinceStartup - start > maxTicks * 0.05f + 30f)
                    Assert.Fail($"{what} did not happen within {maxTicks} ticks (tick {Tick}).");
                yield return null;
            }
        }

        public static List<BenchTelemetrySample> Window(ulong fromTick, ulong toTick) =>
            Bench.Telemetry.Samples.Where(s => s.Tick >= fromTick && s.Tick <= toTick).ToList();

        public static double Mean(IEnumerable<BenchTelemetrySample> samples, Func<BenchTelemetrySample, double> f)
        {
            List<double> values = samples.Select(f).ToList();
            return values.Count == 0 ? double.NaN : values.Average();
        }

        public static void Write(string name, string text)
        {
            File.WriteAllText(Path.Combine(EvidenceDirectory, name), text);
            Debug.Log($"GAM-69 evidence {name}:\n{text}");
        }

        public static void WriteTelemetry(string name) =>
            Bench.Telemetry.WriteCsv(Path.Combine(EvidenceDirectory, name));
    }
}
