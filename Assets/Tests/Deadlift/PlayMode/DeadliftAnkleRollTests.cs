using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 ankle inversion/eversion DOF (owner decision): bounded A/B on the
    /// identical B2 plant. Same placement, references, regulator and bar; the only
    /// change is the deadlift-local powered ankle roll axis. Diagnostic record.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private IEnumerator ExcitationProbe(string name, StringBuilder report, int ticks = 800)
        {
            yield return Prepare(true, true, false);
            InstallRegulatedReference();
            var rng = new System.Random(70);
            float eL = 0f, eR = 0f; int hL = 0, hR = 0;
            float minPlate = float.MaxValue, maxCom = 0f, maxGrip = 0f, maxOff = 0f;
            Vector3 com0 = _world.WholeBodyCom();
            int survived = 0;
            for (int i = 1; i <= ticks; i++)
            {
                if (i > 20)
                {
                    if (--hL <= 0) { eL = rng.Next(2) == 0 ? -0.25f : 0.25f; hL = 1 + rng.Next(3); }
                    if (--hR <= 0) { eR = rng.Next(2) == 0 ? -0.25f : 0.25f; hR = 1 + rng.Next(3); }
                }
                _regulator.ExcitationDegLeft = eL; _regulator.ExcitationDegRight = eR;
                _world.Step(1);
                var t = _ticks.Last();
                minPlate = Mathf.Min(minPlate, (float)System.Math.Min(t.PlateLN, t.PlateRN));
                maxCom = Mathf.Max(maxCom, Vector3.Distance(t.Com, com0));
                maxGrip = Mathf.Max(maxGrip, Mathf.Abs(t.GripRawL.y + t.GripRawR.y));
                maxOff = Mathf.Max(maxOff, Mathf.Abs(_regulator.OffsetDegFor("left_foot")), Mathf.Abs(_regulator.OffsetDegFor("right_foot")));
                survived = i;
                if (t.PlateLN + t.PlateRN < 1 || t.LeftFootN + t.RightFootN < 1) break;
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}: +/-0.25 deg PRBS probe survived {1}/{2} ticks | min plate {3:F1} N | max COM deviation {4:F1} mm | max |grip sum Y| {5:F1} N | max ankle offset {6:F2} deg",
                name, survived, ticks, minPlate, maxCom * 1000f, maxGrip, maxOff));
            _regulator.ExcitationDegLeft = _regulator.ExcitationDegRight = 0f;
            yield return _world.Teardown();
            _world = null;
        }

        [UnityTest, Timeout(3000000), Explicit("GAM-70 ankle-roll DOF A/B; run on demand")]
        public IEnumerator AnkleRoll_StaticsHoldAndRobustnessAB()
        {
            var report = new StringBuilder();
            foreach (bool roll in new[] { false, true })
            {
                _ankleRoll = roll;
                string tag = roll ? "ROLL ON" : "roll off (canonical hinge)";
                yield return Prepare(true, true, false);
                report.AppendLine(tag + "\n" + SolveStatics(0f).Describe());
                yield return _world.Teardown(); _world = null;

                var results = new Dictionary<string, WrenchRun>();
                yield return RunWrenchCase("roll" + (roll ? 1 : 0) + "-unregulated", false, null, 600, results, () => InstallContactReference(0f, null));
                report.AppendLine(tag + " unregulated C4 reference: event " + results.Values.First().Event + " @ " + results.Values.First().EventTick);

                var fails = new List<string>();
                yield return RunA2Hold(tag + " regulated hold", report, fails.Add);
                report.AppendLine(tag + " A2 failures: " + (fails.Count == 0 ? "none" : string.Join("; ", fails)));
                yield return ExcitationProbe(tag, report);
            }
            _ankleRoll = false;
            F.WriteEvidence("ankle-roll-ab.txt", report.ToString());
        }
    }
}
