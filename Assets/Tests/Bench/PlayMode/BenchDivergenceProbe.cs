using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using H = PowerliftingSimulator.Bench.BenchTestHarness;

namespace PowerliftingSimulator.Bench
{
    /// <summary>Diagnostic only: where and when the racked plant first leaves physical behaviour.</summary>
    public sealed class BenchDivergenceProbe
    {
        [UnityTest, Explicit("diagnostic")]
        public IEnumerator PROBE_FIRST_TICKS_OF_THE_RACKED_PLANT()
        {
            yield return H.LoadBench();
            var b = new StringBuilder();
            b.AppendLine("tick0 self/bar penetrations: " + string.Join("; ", H.Bench.TickZeroSelfPenetrations));
            ulong last = ulong.MaxValue;
            for (int i = 0; i < 400 && H.Tick < 60; i++)
            {
                if (H.Tick != last)
                {
                    last = H.Tick;
                    string worst = "";
                    float worstSpeed = 0f;
                    foreach (var e in H.Bench.AthleteRig.Segments)
                    {
                        float v = e.Value.Body.linearVelocity.magnitude + 0.1f * e.Value.Body.angularVelocity.magnitude;
                        if (v > worstSpeed) { worstSpeed = v; worst = e.Key; }
                    }
                    var forces = new StringBuilder();
                    foreach (string id in H.Bench.AthleteRig.Segments.Keys.Concat(new[] { "bar" }))
                    {
                        BenchContactSensor s = H.Bench.Sensor(id);
                        for (int k = 1; k < 15; k++)
                        {
                            double f = s.NormalForce((BenchSurface)k);
                            if (f > 50) forces.Append($" {id}<{(BenchSurface)k}>{f:F0}");
                        }
                    }
                    b.AppendLine(string.Format(CultureInfo.InvariantCulture, "t{0} worst {1} {2:F3} bar v {3:F3} gripSep {4:F4}/{5:F4} |{6}",
                        H.Tick, worst, worstSpeed, H.Bench.Barbell.Body.linearVelocity.magnitude,
                        H.Bench.LeftGrip.AnchorSeparationM, H.Bench.RightGrip.AnchorSeparationM, forces));
                }
                yield return null;
            }
            H.Write("probe-first-ticks.txt", b.ToString());
            H.WriteTelemetry("probe-first-ticks.csv");
        }
        [UnityTest, Explicit("diagnostic")]
        public IEnumerator PROBE_ALL_CONTACTS_DURING_THE_DESCENT()
        {
            yield return H.LoadBench();
            BenchPressPrototypeController bench = H.Bench;
            ulong liftOff = H.Tick + 60;
            bench.Controller.ScriptedIntent = tick =>
            {
                bool confirm = bench.Controller.Phase == BenchMotionPhase.Racked && tick >= liftOff && tick % 2 == 0;
                float lower = bench.Referee.StartGiven ? 0.8f : 0f;
                return new BenchIntent(lower, 0f, confirm, false);
            };
            var b = new StringBuilder();
            ulong last = 0;
            while (H.Tick < 520)
            {
                if (H.Tick != last && H.Tick % 10 == 0 && H.Tick >= 300)
                {
                    last = H.Tick;
                    b.Append($"t{H.Tick} phase {bench.Controller.Phase} pressS {bench.Controller.PressS:F2} bar {bench.Barbell.Body.position.ToString("F3")} |");
                    foreach (string id in bench.AthleteRig.Segments.Keys.Concat(new[] { "bar" }))
                    {
                        BenchContactSensor s = bench.Sensor(id);
                        for (int k = 1; k < 15; k++)
                        {
                            double f = s.NormalForce((BenchSurface)k);
                            if (f > 5) b.Append($" {id}<{(BenchSurface)k}>{f:F0}");
                        }
                    }
                    b.AppendLine();
                }
                yield return null;
            }
            H.Write("probe-descent-contacts.txt", b.ToString());
        }
    }
}
