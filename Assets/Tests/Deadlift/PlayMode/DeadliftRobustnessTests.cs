using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate 2: isolate the reported +/-0.25 deg ankle-target PRBS
    /// instability and qualify a physically representative command/transition
    /// admission contract. The exact qualified B2 plant, 75 kg bar and both
    /// rigid grips are used; only the ankle reference disturbance changes.
    ///
    /// A quiet hold is the positive control, the PRBS is the preserved
    /// adversarial control, a single isolated 0.25 deg step separates amplitude
    /// from reversal, and a slew-limited transition exercises the same magnitude
    /// through the same target path at a rate consistent with posture control.
    /// Every completed tick records both ankle channels, both foot COPs, COM,
    /// reactions, grip, controller state and the first support transition.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private sealed class RobustnessCase
        {
            public string Name, Mode, Event = "none";
            public float H, AmplitudeDeg, RateDegPerS;
            public int StartTick, Window, Survived;
            public double MinPlate = double.MaxValue, MinFoot = double.MaxValue;
            public double PlateEnd, FeetEnd, GripEnd;
            public float MaxComDev, MaxComSpeed, MaxGripSum, MaxOffset, MaxIntegral, MaxRollIntegral;
            public float MaxDemandX, MaxDemandZ, MaxProxX, MaxProxZ, MaxSep, MaxBarDisp, MaxTilt, MaxFootCopAcross;
            public bool Saturated, SecChannels = true, BarLimb;
            public readonly StringBuilder Rows = new StringBuilder(
                "case,tick,H,excL,excR,offsetL,offsetR,intL,intR,rollIntL,rollIntR,requestedXL,appliedXL,actualXL,velXL,requestedZL,appliedZL,actualZL,velZL,demandXL,fracXL,demandZL,fracZL,proxXL,proxZL,copAlongL,copAcrossL,copAlongR,copAcrossR,comX,comY,comZ,comVx,comVy,comVz,footL,footR,plateL,plateR,gripL,gripR,sepL,sepR,barY,barVY,event\n");
        }

        private float _robustExcL, _robustExcR;

        private void RobustRow(RobustnessCase c, int tick, Vector3 com, Vector3 comVel, string evt)
        {
            var t = _ticks.Last();
            PoweredJointDiagnostic l = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
            PoweredJointDiagnostic r = _world.Rig.PoweredController.GetJoint("right_foot").PostPhysicsDiagnostic;
            c.Rows.AppendLine(string.Join(",", new[]
            {
                c.Name, tick.ToString(CultureInfo.InvariantCulture), Fmt(c.H), Fmt(_robustExcL), Fmt(_robustExcR),
                Fmt(_regulator.OffsetDegFor("left_foot")), Fmt(_regulator.OffsetDegFor("right_foot")),
                Fmt(_regulator.IntegralDegFor("left_foot")), Fmt(_regulator.IntegralDegFor("right_foot")),
                Fmt(_regulator.RollIntegralDegFor("left_foot")), Fmt(_regulator.RollIntegralDegFor("right_foot")),
                Fmt(SignedX(l.RequestedTarget)), Fmt(SignedX(l.AppliedTarget)), Fmt(SignedX(l.ActualRelative)), Fmt(l.ActualAngularVelocityRadS.x),
                Fmt(l.SecondaryRequestedAngleDeg), Fmt(l.SecondaryAppliedAngleDeg), Fmt(l.SecondaryActualAngleDeg), Fmt(l.SecondaryActualAngularVelocityRadS),
                Fmt(l.TwistDriveDemandNm), Fmt(l.TwistDriveDemandFraction), Fmt(l.SecondaryDriveDemandNm), Fmt(l.SecondaryDriveDemandFraction),
                Fmt(l.LimitProximity), Fmt(l.SecondaryLimitProximity),
                Fmt(_regulator.CopAlongAxis("left_foot")), Fmt(_regulator.CopAcrossAxis("left_foot")),
                Fmt(_regulator.CopAlongAxis("right_foot")), Fmt(_regulator.CopAcrossAxis("right_foot")),
                Fmt(com.x), Fmt(com.y), Fmt(com.z), Fmt(comVel.x), Fmt(comVel.y), Fmt(comVel.z),
                Fmt((float)t.LeftFootN), Fmt((float)t.RightFootN), Fmt((float)t.PlateLN), Fmt((float)t.PlateRN),
                Fmt(t.GripRawL.y), Fmt(t.GripRawR.y), Fmt(t.SepL), Fmt(t.SepR),
                Fmt(t.BarPos.y), Fmt(_world.Bar.Body.linearVelocity.y), evt
            }));
        }

        private static float SignedX(Quaternion q) =>
            PoweredJointController.SignedTwistRadians(q, Vector3.right) * Mathf.Rad2Deg;

        private IEnumerator RunRobustnessCase(RobustnessCase c, bool freezeRegulator = false, bool closedLoop = true)
        {
            _ankleRoll = true;
            yield return Prepare(true, true, false);
            float wbHalf = 0.5f * _world.Bar.Body.mass * -Physics.gravity.y;
            InstallPretensionReference(0.75f * wbHalf, true, closedLoop);
            DeadliftAnkleRoll.ValidateAppliedAuthority(_world.Rig);
            int total = c.StartTick + c.Window;
            Vector3 com0 = _world.WholeBodyCom();
            var rng = new System.Random(70);
            float eL = 0f, eR = 0f;
            int holdL = 0, holdR = 0;
            bool frozenApplied = false;
            for (int i = 1; i <= total; i++)
            {
                // Same H schedule as the qualified A3: 1 s at zero, 1 s ramp, then hold.
                float h = i <= A3Base ? 0f : i <= A3Base + A3Ramp ? c.H * (i - A3Base) / A3Ramp : c.H;
                if (_pretension != null)
                    _pretension.TrySetTarget(h);
                else
                    _groundedRef.TryCommandHandPull(h);
                if (freezeRegulator && !frozenApplied && i >= c.StartTick)
                {
                    _regulator.Frozen = true;
                    frozenApplied = true;
                }

                _robustExcL = _robustExcR = 0f;
                if (i >= c.StartTick)
                {
                    if (c.Mode == "prbs")
                    {
                        if (--holdL <= 0) { eL = rng.Next(2) == 0 ? -c.AmplitudeDeg : c.AmplitudeDeg; holdL = 1 + rng.Next(3); }
                        if (--holdR <= 0) { eR = rng.Next(2) == 0 ? -c.AmplitudeDeg : c.AmplitudeDeg; holdR = 1 + rng.Next(3); }
                        _robustExcL = eL; _robustExcR = eR;
                    }
                    else if (c.Mode == "prbs10")
                    {
                        if (--holdL <= 0) { eL = rng.Next(2) == 0 ? -c.AmplitudeDeg : c.AmplitudeDeg; holdL = 10; }
                        if (--holdR <= 0) { eR = rng.Next(2) == 0 ? -c.AmplitudeDeg : c.AmplitudeDeg; holdR = 10; }
                        _robustExcL = eL; _robustExcR = eR;
                    }
                    else if (c.Mode == "step")
                    {
                        _robustExcL = _robustExcR = c.AmplitudeDeg;
                    }
                    else if (c.Mode == "slew")
                    {
                        float v = Mathf.Min(c.AmplitudeDeg, (i - c.StartTick + 1) * c.RateDegPerS * (float)SimulationConstants.FixedDeltaTimeSeconds);
                        _robustExcL = _robustExcR = v;
                    }
                    else if (c.Mode == "prbsz")
                    {
                        if (--holdL <= 0) { eL = rng.Next(2) == 0 ? -c.AmplitudeDeg : c.AmplitudeDeg; holdL = 1 + rng.Next(3); }
                        if (--holdR <= 0) { eR = rng.Next(2) == 0 ? -c.AmplitudeDeg : c.AmplitudeDeg; holdR = 1 + rng.Next(3); }
                        _world.Hold.IdentificationOffsets["left_foot"] = Quaternion.AngleAxis(eL, Vector3.forward);
                        _world.Hold.IdentificationOffsets["right_foot"] = Quaternion.AngleAxis(eR, Vector3.forward);
                    }
                }
                _regulator.ExcitationDegLeft = c.Mode == "prbsz" ? 0f : _robustExcL;
                _regulator.ExcitationDegRight = c.Mode == "prbsz" ? 0f : _robustExcR;
                _world.Step(1);
                var t = _ticks.Last();
                Vector3 com = _world.WholeBodyCom();
                Vector3 comVel = _world.Rig.Segments.Values.Aggregate(Vector3.zero,
                    (a, b) => a + b.Body.mass * b.Body.linearVelocity) / _world.Rig.TotalMassKg;
                string evt = "none";
                if (t.PlateLN + t.PlateRN < 1) evt = "plate-lost@" + i;
                else if (t.LeftFootN + t.RightFootN < 1) evt = "feet-lost@" + i;
                else if (t.BarLegN > 1) evt = "bar-limb@" + i;
                if (c.Event == "none" && evt != "none") c.Event = evt;
                c.Survived = i;
                c.MinPlate = Math.Min(c.MinPlate, Math.Min(t.PlateLN, t.PlateRN));
                c.MinFoot = Math.Min(c.MinFoot, Math.Min(t.LeftFootN, t.RightFootN));
                c.MaxComDev = Mathf.Max(c.MaxComDev, Vector3.Distance(com, com0));
                c.MaxComSpeed = Mathf.Max(c.MaxComSpeed, comVel.magnitude);
                c.MaxGripSum = Mathf.Max(c.MaxGripSum, Mathf.Abs(t.GripRawL.y + t.GripRawR.y));
                c.MaxOffset = Mathf.Max(c.MaxOffset, Mathf.Abs(_regulator.OffsetDegFor("left_foot")), Mathf.Abs(_regulator.OffsetDegFor("right_foot")));
                c.MaxIntegral = Mathf.Max(c.MaxIntegral, Mathf.Abs(_regulator.IntegralDegFor("left_foot")), Mathf.Abs(_regulator.IntegralDegFor("right_foot")));
                c.MaxRollIntegral = Mathf.Max(c.MaxRollIntegral, Mathf.Abs(_regulator.RollIntegralDegFor("left_foot")), Mathf.Abs(_regulator.RollIntegralDegFor("right_foot")));
                c.MaxSep = Mathf.Max(c.MaxSep, Mathf.Max(float.IsNaN(t.SepL) ? 0f : t.SepL, float.IsNaN(t.SepR) ? 0f : t.SepR));
                c.MaxBarDisp = Mathf.Max(c.MaxBarDisp, Vector3.Distance(t.BarPos, _ticks[0].BarPos));
                c.MaxTilt = Mathf.Max(c.MaxTilt, Vector3.Angle(_world.Bar.Body.rotation * Vector3.right, Vector3.right));
                c.Saturated |= _regulator.Saturated;
                c.BarLimb |= t.BarLegN > 1;
                foreach (string footId in new[] { "left_foot", "right_foot" })
                {
                    PoweredJointDiagnostic d = _world.Rig.PoweredController.GetJoint(footId).PostPhysicsDiagnostic;
                    c.SecChannels &= d.HasSecondaryChannel;
                    c.MaxDemandX = Mathf.Max(c.MaxDemandX, d.TwistDriveDemandFraction);
                    c.MaxDemandZ = Mathf.Max(c.MaxDemandZ, d.SecondaryDriveDemandFraction);
                    c.MaxProxX = Mathf.Max(c.MaxProxX, d.LimitProximity);
                    c.MaxProxZ = Mathf.Max(c.MaxProxZ, d.SecondaryLimitProximity);
                    float across = _regulator.CopAcrossAxis(footId);
                    if (!float.IsNaN(across)) c.MaxFootCopAcross = Mathf.Max(c.MaxFootCopAcross, Mathf.Abs(across));
                }
                RobustRow(c, i, com, comVel, evt);
                if (evt != "none" && c.Mode.StartsWith("prbs", StringComparison.Ordinal))
                    break;   // adversarial control stops at its first physical transition
            }
            _regulator.ExcitationDegLeft = _regulator.ExcitationDegRight = 0f;
            _world.Hold.IdentificationOffsets.Clear();
            int n = Mathf.Min(50, _ticks.Count);
            for (int i = _ticks.Count - n; i < _ticks.Count; i++)
            {
                c.PlateEnd += _ticks[i].PlateLN + _ticks[i].PlateRN;
                c.FeetEnd += _ticks[i].LeftFootN + _ticks[i].RightFootN;
                c.GripEnd += -(_ticks[i].GripRawL.y + _ticks[i].GripRawR.y);
            }
            if (n > 0) { c.PlateEnd /= n; c.FeetEnd /= n; c.GripEnd /= n; }
            yield return _world.Teardown();
            _world = null;
            _ankleRoll = false;
        }

        private static string RobustnessLine(RobustnessCase c) => string.Format(CultureInfo.InvariantCulture,
            "{0,-22} H {1,6:F1} N mode {2,-6} amp {3:F2} deg start {4,3} | survived {5,4}/{6} | event {7} | min plate {8,6:F1} N min foot {9,6:F1} N | COM dev {10,5:F1} mm speed {11,5:F1} mm/s | grip |sum Y| {12,6:F1} N | offset {13:F2} deg int {14:F2} rollInt {15:F2} sat {16} | X demand {17:F3} prox {18:F3} | Z demand {19:F3} prox {20:F3} | bar disp {21:F1} mm tilt {22:F1} deg | sep {23:F3} mm",
            c.Name, c.H, c.Mode, c.AmplitudeDeg, c.StartTick, c.Survived, c.StartTick + c.Window, c.Event,
            c.MinPlate, c.MinFoot, c.MaxComDev * 1000f, c.MaxComSpeed * 1000f, c.MaxGripSum,
            c.MaxOffset, c.MaxIntegral, c.MaxRollIntegral, c.Saturated, c.MaxDemandX, c.MaxProxX,
            c.MaxDemandZ, c.MaxProxZ, c.MaxBarDisp * 1000f, c.MaxTilt, c.MaxSep * 1000f);

        private IEnumerator RunCaseAndWrite(RobustnessCase c, bool freeze = false)
        {
            yield return RunRobustnessCase(c, freeze);
            F.WriteEvidence("gate2-" + c.Name + ".csv", c.Rows.ToString());
        }

        /// <summary>
        /// Exact reproduction of the reported failure: deterministic +/-0.25 deg
        /// PRBS, per-foot holds of 1-3 ticks, from tick 21, H = 0, 800 ticks.
        /// Adversarial control, not an acceptance contract.
        /// </summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 Gate 2 adversarial PRBS reproduction; run on demand")]
        public IEnumerator GATE2_PRBS_REPRODUCES_THE_REPORTED_FAILURE()
        {
            var c = new RobustnessCase { Name = "prbs-H0", Mode = "prbs", H = 0f, AmplitudeDeg = 0.25f, StartTick = 21, Window = 800 };
            yield return RunCaseAndWrite(c);
            var cz = new RobustnessCase { Name = "prbsz-H0", Mode = "prbsz", H = 0f, AmplitudeDeg = 0.25f, StartTick = 21, Window = 800 };
            yield return RunCaseAndWrite(cz);
            var cf = new RobustnessCase { Name = "prbs-frozen-H0", Mode = "prbs", H = 0f, AmplitudeDeg = 0.25f, StartTick = 21, Window = 800 };
            yield return RunCaseAndWrite(cf, freeze: true);
            var cq = new RobustnessCase { Name = "quiet-frozen-H0", Mode = "quiet", H = 0f, AmplitudeDeg = 0f, StartTick = 21, Window = 800 };
            yield return RunCaseAndWrite(cq, freeze: true);
            var c10 = new RobustnessCase { Name = "prbs10-H0", Mode = "prbs10", H = 0f, AmplitudeDeg = 0.25f, StartTick = 21, Window = 800 };
            yield return RunCaseAndWrite(c10);
            F.WriteEvidence("gate2-prbs-controls.txt", string.Join("\n", new[]
            {
                RobustnessLine(c), RobustnessLine(cz), RobustnessLine(cf), RobustnessLine(cq), RobustnessLine(c10)
            }));
        }

        /// <summary>
        /// Controlled pretension review: the already-qualified open-loop
        /// contact-consistent reference and the integral hand-force loop are
        /// compared under the same physically representative 0.25 deg / 5 deg/s
        /// transition at H = 183.9 N per hand. Both must hold support; the loop
        /// is retained only if it stays at least as accurate without adding a
        /// destabilizing interaction.
        /// </summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator GATE2_PRETENSION_LOOP_REVIEW_OPEN_VS_CLOSED_UNDER_REPRESENTATIVE_TRANSITION()
        {
            float h = 0.5f * 75f * 9.81f * 0.5f;
            var open = new RobustnessCase { Name = "openloop-H184", Mode = "slew", H = h, AmplitudeDeg = 0.25f, RateDegPerS = 5f, StartTick = 250, Window = 800 };
            yield return RunRobustnessCase(open, closedLoop: false);
            F.WriteEvidence("gate2-openloop-H184.csv", open.Rows.ToString());
            var closed = new RobustnessCase { Name = "closedloop-H184", Mode = "slew", H = h, AmplitudeDeg = 0.25f, RateDegPerS = 5f, StartTick = 250, Window = 800 };
            yield return RunRobustnessCase(closed, closedLoop: true);
            F.WriteEvidence("gate2-closedloop-H184.csv", closed.Rows.ToString());

            float twoH = 2f * h;
            var report = new StringBuilder();
            report.AppendLine(RobustnessLine(open));
            report.AppendLine(RobustnessLine(closed));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "transfer at end: open plates {0:F1} N (error {1:F1} N = {2:F2} % of 2H), closed plates {3:F1} N (error {4:F1} N = {5:F2} %), target plates {6:F1} N",
                open.PlateEnd, open.PlateEnd - (735.5 - twoH), 100.0 * Math.Abs(open.PlateEnd - (735.5 - twoH)) / twoH,
                closed.PlateEnd, closed.PlateEnd - (735.5 - twoH), 100.0 * Math.Abs(closed.PlateEnd - (735.5 - twoH)) / twoH,
                735.5 - twoH));
            F.WriteEvidence("gate2-pretension-review.txt", report.ToString());

            Assert.That(open.Event, Is.EqualTo("none"), "open-loop reference lost support under the representative transition:\n" + report);
            Assert.That(closed.Event, Is.EqualTo("none"), "closed-loop reference lost support under the representative transition:\n" + report);
            Assert.That(open.Saturated || closed.Saturated, Is.False, "a pretension configuration saturated the regulator:\n" + report);
            // Retain the already-qualified force loop only if it is at least as accurate as the simpler open-loop reference.
            Assert.That(Math.Abs(closed.PlateEnd - (735.5 - twoH)), Is.LessThanOrEqualTo(Math.Abs(open.PlateEnd - (735.5 - twoH)) + 1f),
                "the retained force loop is less accurate than the open-loop reference:\n" + report);
        }

        /// <summary>
        /// Physically representative admission: quiet, one isolated 0.25 deg
        /// step, and a 0.25 deg transition slew-limited to 5 deg/s, at H = 0 and
        /// at the qualified 183.9 N per hand. Each runs 800 ticks after the
        /// disturbance and must retain plate/foot support without saturation.
        /// </summary>
        [UnityTest, Timeout(3600000)]
        public IEnumerator GATE2_ROBUSTNESS_ADMISSION_PHYSICALLY_REPRESENTATIVE_ANKLE_TRANSITIONS()
        {
            float h = 0.5f * 75f * 9.81f * 0.5f;   // 183.9 N per hand
            var cases = new List<RobustnessCase>();
            foreach (float load in new[] { 0f, h })
            {
                string tag = load == 0f ? "H0" : "H184";
                int start = load == 0f ? 21 : 250;
                var quiet = new RobustnessCase { Name = "quiet-" + tag, Mode = "quiet", H = load, AmplitudeDeg = 0.25f, StartTick = start, Window = 800 };
                yield return RunCaseAndWrite(quiet); cases.Add(quiet);
                var step = new RobustnessCase { Name = "step-" + tag, Mode = "step", H = load, AmplitudeDeg = 0.25f, StartTick = start, Window = 800 };
                yield return RunCaseAndWrite(step); cases.Add(step);
                var slew = new RobustnessCase { Name = "slew-" + tag, Mode = "slew", H = load, AmplitudeDeg = 0.25f, RateDegPerS = 5f, StartTick = start, Window = 800 };
                yield return RunCaseAndWrite(slew); cases.Add(slew);
            }
            var report = new StringBuilder();
            foreach (RobustnessCase c in cases)
                report.AppendLine(RobustnessLine(c));
            report.AppendLine("PRBS (1-3 tick holds) is retained as the adversarial control in GATE2_PRBS_REPRODUCES_THE_REPORTED_FAILURE.");
            report.AppendLine("5 deg/s transition rate: 0.25 deg in 50 ms, the same magnitude through the same target path; the actuator's 114.6 deg/s slew contract is not used as a player command bandwidth.");
            F.WriteEvidence("gate2-robustness-admission.txt", report.ToString());
            foreach (RobustnessCase c in cases)
            {
                Assert.That(c.Event, Is.EqualTo("none"), c.Name + " lost support under a physically representative transition:\n" + report);
                Assert.That(c.Saturated, Is.False, c.Name + " saturated the balance regulator:\n" + report);
                Assert.That(c.MaxDemandX, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), c.Name + " X channel saturated:\n" + report);
                Assert.That(c.MaxDemandZ, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), c.Name + " Z channel saturated:\n" + report);
                Assert.That(c.MaxComDev, Is.LessThan(0.02f), c.Name + " COM moved more than 20 mm:\n" + report);
                Assert.That(c.MaxGripSum, Is.LessThan(2f * c.H + 120f), c.Name + " grip force transient exceeded 120 N above the pretension load:\n" + report);
                Assert.That(c.SecChannels, Is.True, c.Name + " lost an ankle Z channel:\n" + report);
            }
        }
    }
}
