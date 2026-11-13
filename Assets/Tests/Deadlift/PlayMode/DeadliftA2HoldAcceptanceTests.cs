using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A2 dynamic acceptance on the real plant: exact B2 (re-verified
    /// by the independent kinematic table), real 75 kg bar on its plates, both
    /// rigid grips, the C4 contact-consistent reference (H = 0) and the
    /// identified per-foot ankle COP regulator. No pull requested. The bar is
    /// dynamic throughout; DeadliftSetupHold -> PoweredJointController is the
    /// only joint writer.
    ///
    /// Physical acceptance over 10 s (1000 ticks), on two fresh plants:
    ///   support mode never changes: each plate >= 25 % of half the bar weight,
    ///   each foot >= 50 % of its C4 reaction, no bar-limb contact, no non-foot
    ///   platform contact; grip anchor drift &lt; 1 mm; COM within 5 mm of its
    ///   initial position at every tick and, over the last 3 s, mean COM speed
    ///   &lt; 2 mm/s; bar displacement &lt; 2 mm; regulator never saturated; joint
    ///   modeled demand below the saturation contract; no joint within its
    ///   2 deg limit contact distance (no hard-stop dependence).
    /// These are game-engine qualification bounds on a measured hold, not a
    /// physiological balance claim.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private const int HoldTicks = 1000, SettleTicks = 300;

        private IEnumerator RunA2Hold(string label, StringBuilder report, System.Action<string> fail)
        {
            yield return Prepare(true, true, false);
            InstallRegulatedReference();
            float wb = _world.Bar.Body.mass * -Physics.gravity.y;
            float footRef = Mathf.Min(_statics.FootLN, _statics.FootRN);
            Vector3 com0 = _world.WholeBodyCom();
            Vector3 bar0 = _world.Bar.Body.position;
            float minPlate = float.MaxValue, minFoot = float.MaxValue, maxSep = 0f, maxComDev = 0f, maxBarDisp = 0f,
                maxDemand = 0f, maxProx = 0f, maxOffset = 0f, maxBarLeg = 0f, maxOther = 0f;
            string maxProxJoint = "";
            bool saturated = false;
            Vector3 vSum = Vector3.zero; float vMax = 0f; int vN = 0;
            for (int i = 1; i <= HoldTicks; i++)
            {
                _world.Step(1);
                var t = _ticks.Last();
                minPlate = Mathf.Min(minPlate, (float)System.Math.Min(t.PlateLN, t.PlateRN));
                minFoot = Mathf.Min(minFoot, (float)System.Math.Min(t.LeftFootN, t.RightFootN));
                maxSep = Mathf.Max(maxSep, Mathf.Max(t.SepL, t.SepR));
                maxComDev = Mathf.Max(maxComDev, Vector3.Distance(t.Com, com0));
                maxBarDisp = Mathf.Max(maxBarDisp, Vector3.Distance(_world.Bar.Body.position, bar0));
                maxDemand = Mathf.Max(maxDemand, t.MaxDemand);
                maxBarLeg = Mathf.Max(maxBarLeg, (float)t.BarLegN);
                maxOther = Mathf.Max(maxOther, (float)_world.Sensors.Where(x => x.Key != "bar" && !x.Key.EndsWith("_foot"))
                    .Sum(x => x.Value.NormalForce(BenchSurface.Floor)));
                maxOffset = Mathf.Max(maxOffset, Mathf.Abs(_regulator.OffsetDegFor("left_foot")), Mathf.Abs(_regulator.OffsetDegFor("right_foot")));
                saturated |= _regulator.Saturated;
                foreach (PoweredJointController.PoweredJointRuntime j in _world.Rig.PoweredController.Joints)
                    if (j.HasPostPhysicsDiagnostic && j.PostPhysicsDiagnostic.LimitProximity > maxProx)
                    {
                        maxProx = j.PostPhysicsDiagnostic.LimitProximity;
                        maxProxJoint = j.Id;
                    }
                if (i > HoldTicks - SettleTicks)
                {
                    Vector3 v = _world.Rig.Segments.Values.Aggregate(Vector3.zero, (a, b) => a + b.Body.mass * b.Body.linearVelocity) / _world.Rig.TotalMassKg;
                    vSum += v; vMax = Mathf.Max(vMax, v.magnitude); vN++;
                }
            }
            Vector3 vMean = vSum / vN;
            // Joint-limit contact distance is 2 deg (PhysicalAthleteRig.Limit); the hip flexion stop is the closest (-132 deg).
            float hipStopProximity = 1f - 2f / 132f;
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0}: ticks {1} | min plate {2:F1} N (bound {3:F1}) | min foot {4:F1} N (bound {5:F1}) | grip drift max {6:F3} mm | " +
                "COM deviation max {7:F2} mm | settled mean COM speed {8:F2} mm/s, max {9:F1} mm/s | bar displacement max {10:F2} mm | " +
                "max demand {11:F3} (contract {12:F2}) | max limit proximity {13:F4} ({14}; stop-contact bound {15:F4}) | " +
                "regulator max |offset| {16:F2} deg saturated {17} | bar-limb contact max {18:F1} N | non-foot platform max {19:F1} N",
                label, HoldTicks, minPlate, 0.25f * wb * 0.5f, minFoot, 0.5f * footRef, maxSep * 1000f, maxComDev * 1000f,
                vMean.magnitude * 1000f, vMax * 1000f, maxBarDisp * 1000f, maxDemand, PoweredJointController.ModeledDemandSaturationThreshold,
                maxProx, maxProxJoint, hipStopProximity, maxOffset, saturated, maxBarLeg, maxOther));
            if (minPlate < 0.25f * wb * 0.5f) fail(label + ": plate support dropped");
            if (minFoot < 0.5f * footRef) fail(label + ": foot support dropped");
            if (maxBarLeg > 1f) fail(label + ": bar touched a limb");
            if (maxOther > 1f) fail(label + ": a non-foot body touched the platform");
            if (maxSep > 0.001f) fail(label + ": grip drift");
            if (maxComDev > 0.005f) fail(label + ": COM moved");
            if (vMean.magnitude > 0.002f) fail(label + ": COM not settled");
            if (maxBarDisp > 0.002f) fail(label + ": bar moved");
            if (saturated) fail(label + ": regulator saturated");
            if (maxDemand >= PoweredJointController.ModeledDemandSaturationThreshold) fail(label + ": a drive saturated");
            if (maxProx >= hipStopProximity) fail(label + ": a joint reached its limit contact distance");
            yield return _world.Teardown();
            _world = null;
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator A2_DYN_GRIPPED_B2_HELD_BY_CONTACT_CONSISTENT_REFERENCE_AND_ANKLE_REGULATOR()
        {
            var report = new StringBuilder();
            var failures = new System.Collections.Generic.List<string>();
            _ankleRoll = System.Environment.GetEnvironmentVariable("GAM70_ANKLE_ROLL") != "0";   // qualified deadlift plant: ankle roll DOF on
            report.AppendLine("ankle roll DOF " + (_ankleRoll ? "ON" : "off"));
            yield return RunA2Hold("fresh plant 1", report, failures.Add);
            yield return RunA2Hold("fresh plant 2", report, failures.Add);
            _ankleRoll = false;
            F.WriteEvidence("a2-dynamic-hold.txt", report.ToString());
            Assert.That(failures, Is.Empty, string.Join("; ", failures) + "\n" + report);
        }
    }
}
