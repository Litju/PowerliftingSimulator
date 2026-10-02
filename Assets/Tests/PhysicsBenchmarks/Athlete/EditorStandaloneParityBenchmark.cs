using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// GAM-50 Editor side of the Editor/standalone parity comparison: the
    /// same GAM50ParityHarness squat the Windows player runs, written to the
    /// raw directory for Tools/Benchmarks/Compare-Parity.py.
    /// </summary>
    [Category("PhysicsBenchmarkParity")]
    public sealed class EditorStandaloneParityBenchmark
    {
        public static readonly float[] Loads = { 25f, 140f };

        [UnityTest]
        public IEnumerator B17_EditorParityTrace([ValueSource(nameof(Loads))] float loadKg)
        {
            var session = new AthleteBenchmarkSession();
            yield return AthleteBenchmarkSession.Load(session, 0f);
            string trace = GAM50ParityHarness.Run(loadKg, "editor");
            string path = Path.Combine(PhysicsBenchmarkRecorder.RawDirectory(),
                string.Format(CultureInfo.InvariantCulture, "parity-editor-{0:000}kg.csv", loadKg));
            File.WriteAllText(path, trace, new UTF8Encoding(false));
            Assert.That(trace.Length, Is.GreaterThan(0));
        }
    }
}
