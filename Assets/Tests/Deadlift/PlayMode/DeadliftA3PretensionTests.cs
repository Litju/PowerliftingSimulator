using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A3, grounded: on the A2-qualified B2 hold, a controlled
    /// pretension command H per hand (0 -> 50 % of half the bar weight over
    /// 1 s, then held 2 s) must transfer measured load from the plates to the
    /// grips and on to the feet without lifting the bar. Reference torques and
    /// per-foot COP targets come from DeadliftGroundedReference (exact C4
    /// statics in H); the regulator keeps balance; the bar stays dynamic.
    /// Negative controls: no grip, one grip, drives off, invalid command.
    /// A pass qualifies a game-engine rigid-grip load-transfer mechanism, not
    /// physiological finger-grip strength.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private const int A3Base = 100, A3Ramp = 100, A3Hold = 200;
        private DeadliftGroundedReference _groundedRef;

        private DeadliftPretensionLoop _pretension;

        private float MeasuredPerHandUpN()
        {
            float sum = 0f; int n = 0;
            if (_world.LeftGrip != null && _world.LeftGrip.IsAttached) { sum += -_world.LeftGrip.SolverForceRaw.y; n++; }
            if (_world.RightGrip != null && _world.RightGrip.IsAttached) { sum += -_world.RightGrip.SolverForceRaw.y; n++; }
            return n == 0 ? 0f : sum / 2f;   // per hand of the bilateral grip: a missing hand contributes zero
        }

        private void InstallPretensionReference(float maxHandPullN, bool drives = true, bool closedLoop = false)
        {
            DeadliftContactStatics s0 = SolveStatics(0f), s1 = SolveStatics(maxHandPullN);
            _statics = s0;
            _groundedRef = new DeadliftGroundedReference(s0, s1);
            _regulator = new DeadliftBalanceRegulator(_world.Rig, _world.Rig.PlatformCollider) { ComFeedback = _regulatorComLoop, KiDegPerMPerTick = _regulatorKi, RollLoop = _ankleRoll };
            _world.Hold.Regulator = _regulator;
            _world.Hold.UseGroundedReference(_world.Rig, _groundedRef);
            _pretension = closedLoop ? new DeadliftPretensionLoop(_groundedRef, MeasuredPerHandUpN) : null;
            _world.Hold.Pretension = _pretension;
            if (!drives)
                foreach (var j in _world.Rig.PoweredController.Joints)
                    _world.Hold.SetStrengthScale(j.Id, 0f);   // Enabled=false is ignored in Controlled mode (measured)
            _world.Rig.PrimeCommandSource();
        }

        private sealed class A3Result
        {
            public double PlatesBase, PlatesEnd, FeetBase, FeetEnd, GripBase, GripEnd, PlateAsymEnd;
            public float MinPlate = float.MaxValue, BarDisp, ComDev, Sep, BarTiltDeg, ComDrop;
            public bool Saturated;
            public string Event = "none";
        }

        private IEnumerator RunPretension(string name, bool left, bool right, bool drives, float targetN, A3Result r, bool closedLoop = true)
        {
            yield return Prepare(left, right, false);
            float wbHalf = 0.5f * _world.Bar.Body.mass * -Physics.gravity.y;
            InstallPretensionReference(closedLoop ? 0.75f * wbHalf : targetN, drives, closedLoop);
            var trace = new StringBuilder("tick,hCmd,plates,feet,gripUpOnBar,plateL,plateR,comZ,regL,regR,barY,barTiltDeg\n");
            Vector3 com0 = _world.WholeBodyCom(), bar0 = _world.Bar.Body.position;
            Quaternion barQ0 = _world.Bar.Body.rotation;
            int total = A3Base + A3Ramp + A3Hold;
            for (int i = 1; i <= total; i++)
            {
                float h = i <= A3Base ? 0f : i <= A3Base + A3Ramp ? targetN * (i - A3Base) / A3Ramp : targetN;
                if (_pretension != null)
                    Assert.That(_pretension.TrySetTarget(h), Is.True, "valid pretension target rejected");
                else
                    Assert.That(_groundedRef.TryCommandHandPull(h), Is.True, "valid pretension command rejected");
                _world.Step(1);
                var t = _ticks.Last();
                double plates = t.PlateLN + t.PlateRN;
                trace.AppendLine(string.Join(",", i, _groundedRef.HandPullN, plates, t.LeftFootN + t.RightFootN, -(t.GripRawL.y + t.GripRawR.y), t.PlateLN, t.PlateRN,
                    t.Com.z, _regulator.OffsetDegFor("left_foot"), _regulator.OffsetDegFor("right_foot"), _world.Bar.Body.position.y, Quaternion.Angle(_world.Bar.Body.rotation, barQ0)));
                r.MinPlate = Mathf.Min(r.MinPlate, (float)System.Math.Min(t.PlateLN, t.PlateRN));
                r.BarDisp = Mathf.Max(r.BarDisp, Vector3.Distance(_world.Bar.Body.position, bar0));
                r.BarTiltDeg = Mathf.Max(r.BarTiltDeg, Quaternion.Angle(_world.Bar.Body.rotation, barQ0));
                r.ComDev = Mathf.Max(r.ComDev, Vector3.Distance(t.Com, com0));
                r.ComDrop = Mathf.Max(r.ComDrop, com0.y - t.Com.y);
                r.Sep = Mathf.Max(r.Sep, Mathf.Max(float.IsNaN(t.SepL) ? 0f : t.SepL, float.IsNaN(t.SepR) ? 0f : t.SepR));
                r.Saturated |= _regulator.Saturated;
                if (r.Event == "none")
                {
                    if (plates < 1 && _world.Bar.Body.linearVelocity.y > 0) r.Event = "bar-lifted@" + i;
                    else if (t.LeftFootN + t.RightFootN < 1) r.Event = "feet-unloaded@" + i;
                    else if (t.BarLegN > 1) r.Event = "bar-limb-contact@" + i;
                }
            }
            IEnumerable<int> baseWin = Enumerable.Range(A3Base / 2, A3Base / 2), endWin = Enumerable.Range(total - 50, 50);
            double M(IEnumerable<int> w, System.Func<int, double> f) => w.Average(f);
            r.PlatesBase = M(baseWin, k => _ticks[k].PlateLN + _ticks[k].PlateRN);
            r.PlatesEnd = M(endWin, k => _ticks[k].PlateLN + _ticks[k].PlateRN);
            r.FeetBase = M(baseWin, k => _ticks[k].LeftFootN + _ticks[k].RightFootN);
            r.FeetEnd = M(endWin, k => _ticks[k].LeftFootN + _ticks[k].RightFootN);
            r.GripBase = M(baseWin, k => -(_ticks[k].GripRawL.y + _ticks[k].GripRawR.y));
            r.GripEnd = M(endWin, k => -(_ticks[k].GripRawL.y + _ticks[k].GripRawR.y));
            r.PlateAsymEnd = M(endWin, k => _ticks[k].PlateLN - _ticks[k].PlateRN);
            F.WriteEvidence("a3-trace-" + name + ".csv", trace.ToString());
            yield return _world.Teardown();
            _world = null;
        }

        private static string Line(string label, A3Result r, float target) => string.Format(CultureInfo.InvariantCulture,
            "{0}: target 2H {1:F1} N | plates {2:F1} -> {3:F1} N (d {4:F1}) | feet {5:F1} -> {6:F1} N (d {7:F1}) | grip up on bar {8:F1} -> {9:F1} N (d {10:F1}) | " +
            "plate L-R at end {11:F1} N | min plate {12:F1} N | bar disp {13:F2} mm tilt {14:F2} deg | COM dev {15:F2} mm drop {16:F2} mm | grip drift {17:F3} mm | regulator saturated {18} | event {19}",
            label, 2f * target, r.PlatesBase, r.PlatesEnd, r.PlatesEnd - r.PlatesBase, r.FeetBase, r.FeetEnd, r.FeetEnd - r.FeetBase,
            r.GripBase, r.GripEnd, r.GripEnd - r.GripBase, r.PlateAsymEnd, r.MinPlate, r.BarDisp * 1000f, r.BarTiltDeg, r.ComDev * 1000f, r.ComDrop * 1000f, r.Sep * 1000f, r.Saturated, r.Event);

        [UnityTest, Timeout(1800000)]
        public IEnumerator A3_GROUNDED_BILATERAL_PRETENSION_TRANSFERS_PLATE_LOAD_TO_GRIPS_AND_FEET()
        {
            _ankleRoll = System.Environment.GetEnvironmentVariable("GAM70_ANKLE_ROLL") != "0";   // qualified deadlift plant: ankle roll DOF on
            float wb = 75f * 9.81f;
            float target = 0.5f * wb * 0.5f;   // 50 % of half the bar weight per hand
            var report = new StringBuilder();
            var open = new A3Result(); yield return RunPretension("openloop", true, true, true, target, open, closedLoop: false);
            report.AppendLine(Line("control: open-loop feedforward only", open, target));
            var both = new A3Result(); yield return RunPretension("bilateral", true, true, true, target, both);
            report.AppendLine(Line("bilateral", both, target));
            var both2 = new A3Result(); yield return RunPretension("bilateral-repeat", true, true, true, target, both2);
            report.AppendLine(Line("bilateral fresh repeat", both2, target));
            var none = new A3Result(); yield return RunPretension("nogrip", false, false, true, target, none);
            report.AppendLine(Line("control: no grip", none, target));
            var one = new A3Result(); yield return RunPretension("leftonly", true, false, true, target, one);
            report.AppendLine(Line("control: left grip only", one, target));
            var off = new A3Result(); yield return RunPretension("drivesoff", true, true, false, target, off);
            report.AppendLine(Line("control: drives off", off, target));

            // Invalid command control on a fresh reference (no plant stepping needed).
            yield return Prepare(true, true, false);
            InstallPretensionReference(0.75f * wb * 0.5f, true, true);
            bool over = _groundedRef.TryCommandHandPull(1.2f * wb * 0.5f) | _pretension.TrySetTarget(1.2f * wb * 0.5f) | _pretension.TrySetTarget(wb);
            bool neg = _groundedRef.TryCommandHandPull(-1f) | _pretension.TrySetTarget(-1f);
            bool nan = _groundedRef.TryCommandHandPull(float.NaN) | _pretension.TrySetTarget(float.NaN);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "control: invalid commands accepted? over-max {0}, negative {1}, NaN {2}; retained H {3:F1} N", over, neg, nan, _groundedRef.HandPullN));
            yield return _world.Teardown();
            _world = null;
            F.WriteEvidence("a3-grounded-pretension.txt", report.ToString());
            string rep = report.ToString();
            _ankleRoll = false;

            float twoH = 2f * target;
            // Open-loop feedforward is a recorded comparison: with the roll DOF and the foot-load
            // gain schedule it also realizes the transfer (the earlier overshoot came from the
            // unstable balance loop, not from closed-chain indeterminacy). The force loop must be
            // at least as accurate.
            Assert.That(Mathf.Abs((float)(both.PlatesEnd - both.PlatesBase) + twoH),
                Is.LessThanOrEqualTo(Mathf.Abs((float)(open.PlatesEnd - open.PlatesBase) + twoH) + 1f), "closed-loop pretension is less accurate than open loop\n" + rep);
            foreach (var b in new[] { both2 })
            {
                Assert.That(b.Event, Is.EqualTo("none"), "repeat: " + rep);
                Assert.That(b.PlatesEnd - b.PlatesBase, Is.EqualTo(-twoH).Within(0.1f * twoH), "repeat plates\n" + rep);
            }
            Assert.That(both.Event, Is.EqualTo("none"), rep);
            Assert.That(both.GripEnd - both.GripBase, Is.EqualTo(twoH).Within(0.1f * twoH), "grip upward force did not rise by 2H\n" + rep);
            Assert.That(both.PlatesEnd - both.PlatesBase, Is.EqualTo(-twoH).Within(0.1f * twoH), "plates did not unload by 2H\n" + rep);
            Assert.That(both.FeetEnd - both.FeetBase, Is.EqualTo(twoH).Within(0.1f * twoH), "feet did not take 2H\n" + rep);
            Assert.That(both.MinPlate, Is.GreaterThan(100f), "a plate unloaded: premature bar break\n" + rep);
            Assert.That(both.BarDisp, Is.LessThan(0.002f), "bar moved\n" + rep);
            Assert.That(both.ComDev, Is.LessThan(0.01f), "COM moved\n" + rep);
            Assert.That(both.Sep, Is.LessThan(0.001f), "grip drift\n" + rep);
            Assert.That(both.Saturated, Is.False, "regulator saturated\n" + rep);

            Assert.That(none.PlatesEnd - none.PlatesBase, Is.GreaterThan(-0.1f * twoH), "no-grip control transferred bar load\n" + rep);
            // One hand alone carries the commanded total through a distinctly asymmetric load path.
            Assert.That(Mathf.Abs((float)one.PlateAsymEnd), Is.GreaterThan(Mathf.Max(25f, 5f * Mathf.Abs((float)both.PlateAsymEnd))),
                "one-grip control is not distinguishable from the bilateral load path\n" + rep);
            Assert.That(off.ComDrop > 0.03f || off.Event.StartsWith("feet"), Is.True, "drives-off control did not collapse\n" + rep);
            Assert.That(over || neg || nan, Is.False, "an invalid pretension command was accepted\n" + rep);
        }

        /// <summary>Regulator outer-loop A/B: A2 hold and bilateral closed-loop pretension with the COM term off. Diagnostic.</summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 regulator outer-loop A/B; run on demand")]
        public IEnumerator A2A3_RegulatorComLoopOffAB()
        {
            _regulatorComLoop = true; _regulatorKi = 0f;
            var report = new StringBuilder();
            var failures = new List<string>();
            yield return RunA2Hold("A2 hold, Ki 0", report, failures.Add);
            float target = 0.5f * 75f * 9.81f * 0.5f;
            var both = new A3Result(); yield return RunPretension("bilateral-ki0", true, true, true, target, both);
            report.AppendLine(Line("A3 bilateral, Ki 0", both, target));
            _regulatorKi = 3f;
            F.WriteEvidence("regulator-ki-ab.txt", report.ToString() + "A2 failures: " + string.Join("; ", failures));
        }

        /// <summary>
        /// A3 plant identification in the pretensioned contact mode: fresh plants run the
        /// qualified closed-loop pretension to H (0 or 50 % of half the bar weight), then at
        /// tick <see cref="IdFreezeTick"/> the pretension loop is removed (H held) and the
        /// regulator is frozen with a per-foot ankle step (0, +1 bilateral, +1 left only).
        /// Per-foot COP along the sole axis is recorded for the following ticks. Diagnostic.
        /// </summary>
        [UnityTest, Timeout(2400000), Explicit("GAM-70 A3 pretension-mode ankle identification; run on demand")]
        public IEnumerator A3_PretensionModeAnkleIdentification()
        {
            float wbHalf = 0.5f * 75f * 9.81f;
            var csv = new StringBuilder("case,H,tick,sL,sR,feetL,feetR,plates,grip\n");
            foreach (float frac in new[] { 0f, 0.5f })
            foreach (var step in new (string n, float l, float r)[] { ("0", 0f, 0f), ("both+1", 1f, 1f), ("left+1", 1f, 0f) })
            {
                yield return Prepare(true, true, false);
                InstallPretensionReference(0.75f * wbHalf, true, true);
                float target = frac * wbHalf;
                for (int i = 1; i <= IdFreezeTick + 12; i++)
                {
                    float h = i <= A3Base ? 0f : i <= A3Base + A3Ramp ? target * (i - A3Base) / A3Ramp : target;
                    if (i == IdFreezeTick + 1)
                    {
                        _world.Hold.Pretension = null;   // H held at its current command
                        _regulator.IdentificationStepDegLeft = step.l;
                        _regulator.IdentificationStepDegRight = step.r;
                        _regulator.Frozen = true;
                    }
                    if (_world.Hold.Pretension != null)
                        _pretension.TrySetTarget(h);
                    _world.Step(1);
                    if (i >= IdFreezeTick - 3)
                    {
                        var t = _ticks.Last();
                        csv.AppendLine(string.Join(",", step.n, _groundedRef.HandPullN.ToString("F1", CultureInfo.InvariantCulture), i - IdFreezeTick,
                            _regulator.CopAlongAxis("left_foot").ToString("G7", CultureInfo.InvariantCulture), _regulator.CopAlongAxis("right_foot").ToString("G7", CultureInfo.InvariantCulture),
                            t.LeftFootN, t.RightFootN, t.PlateLN + t.PlateRN, -(t.GripRawL.y + t.GripRawR.y)));
                    }
                }
                yield return _world.Teardown();
                _world = null;
            }
            F.WriteEvidence("a3-id.csv", csv.ToString());
        }

        private const int IdFreezeTick = 240;

        /// <summary>
        /// Foot-roll actuator identification at H = 0: the qualified hold runs to tick 100,
        /// then the regulator is frozen and a +1 deg target step is applied to one joint
        /// channel (left hip x/y/z, left ankle x for reference). Records left/right COP
        /// along and across the sole vs the frozen baseline. Diagnostic.
        /// </summary>
        [UnityTest, Timeout(2400000), Explicit("GAM-70 foot-roll actuator identification; run on demand")]
        public IEnumerator A3_FootRollActuatorIdentification()
        {
            _ankleRoll = System.Environment.GetEnvironmentVariable("GAM70_ANKLE_ROLL") == "1";
            var csv = new StringBuilder("case,tick,alongL,acrossL,alongR,acrossR,feetL,feetR\n");
            var cases = new (string n, string joint, Vector3 axis)[]
            {
                ("base", null, Vector3.zero), ("ankleR-x", "right_foot", Vector3.right),
                ("hipL-y", "left_thigh", Vector3.up), ("hipR-y", "right_thigh", Vector3.up),
                ("rollL-z", "left_foot", Vector3.forward), ("rollR-z", "right_foot", Vector3.forward)
            };
            foreach (var c in cases)
            {
                yield return Prepare(true, true, false);
                InstallRegulatedReference();
                for (int i = 1; i <= 110; i++)
                {
                    if (i == 101)
                    {
                        _regulator.Frozen = true;
                        if (c.joint != null)
                            _world.Hold.IdentificationOffsets[c.joint] = Quaternion.AngleAxis(1f, c.axis);
                    }
                    _world.Step(1);
                    if (i >= 98)
                    {
                        var t = _ticks.Last();
                        string f(float v) => v.ToString("G7", CultureInfo.InvariantCulture);
                        csv.AppendLine(string.Join(",", c.n, i - 100, f(_regulator.CopAlongAxis("left_foot")), f(_regulator.CopAcrossAxis("left_foot")),
                            f(_regulator.CopAlongAxis("right_foot")), f(_regulator.CopAcrossAxis("right_foot")), t.LeftFootN, t.RightFootN));
                    }
                }
                _world.Hold.IdentificationOffsets.Clear();
                yield return _world.Teardown();
                _world = null;
            }
            F.WriteEvidence(_ankleRoll ? "a3-rollid-anklez.csv" : "a3-rollid.csv", csv.ToString());
            _ankleRoll = false;
        }

        /// <summary>
        /// Closed-loop identification for a higher-order per-foot model: the qualified
        /// regulator runs, plus an independent deterministic PRBS (+/-0.25 deg, hold 1-3
        /// ticks) on each ankle reference. H = 0 for 800 ticks, and the closed-loop
        /// pretension ramp to 50 % of half the bar weight. Stops at the first support
        /// change. Diagnostic; data feed an offline ARX fit.
        /// </summary>
        [UnityTest, Timeout(2400000), Explicit("GAM-70 A3 closed-loop ARX identification; run on demand")]
        public IEnumerator A3_ClosedLoopArxIdentification()
        {
            float wbHalf = 0.5f * 75f * 9.81f;
            var csv = new StringBuilder("case,tick,H,uL,uR,sL,sR,ankErrL,ankErrR,ankVelL,ankVelR,kneeVelL,kneeVelR,hipVelL,hipVelR,footWL,footWR,comZ,comVz,feetL,feetR,plates,grip\n");
            foreach (float frac in new[] { 0f, 0.5f })
            {
                yield return Prepare(true, true, false);
                InstallPretensionReference(0.75f * wbHalf, true, true);
                var rng = new System.Random(70 + (int)(frac * 10));
                float eL = 0f, eR = 0f; int holdL = 0, holdR = 0;
                int total = frac == 0f ? 800 : A3Base + A3Ramp + A3Hold;
                string name = frac == 0f ? "H0" : "H182";
                for (int i = 1; i <= total; i++)
                {
                    float h = frac == 0f || i <= A3Base ? 0f : i <= A3Base + A3Ramp ? frac * wbHalf * (i - A3Base) / A3Ramp : frac * wbHalf;
                    _pretension.TrySetTarget(h);
                    if (i > 20)
                    {
                        if (--holdL <= 0) { eL = rng.Next(2) == 0 ? -0.25f : 0.25f; holdL = 1 + rng.Next(3); }
                        if (--holdR <= 0) { eR = rng.Next(2) == 0 ? -0.25f : 0.25f; holdR = 1 + rng.Next(3); }
                    }
                    _regulator.ExcitationDegLeft = eL;
                    _regulator.ExcitationDegRight = eR;
                    _world.Step(1);
                    var t = _ticks.Last();
                    PowerliftingSimulator.Athlete.PoweredJointDiagnostic D(string id) =>
                        _world.Rig.PoweredController.Joints.First(j => j.Id == id).PostPhysicsDiagnostic;
                    string f(float v) => v.ToString("G7", CultureInfo.InvariantCulture);
                    string W(string id) => _world.Rig.Segments[id].Body.angularVelocity.ToString("G6").Replace(',', ';');
                    csv.AppendLine(string.Join(",", name, i, f(_groundedRef.HandPullN), f(_regulator.OffsetDegFor("left_foot")), f(_regulator.OffsetDegFor("right_foot")),
                        f(_regulator.CopAlongAxis("left_foot")), f(_regulator.CopAlongAxis("right_foot")),
                        f(D("left_foot").ErrorRad.x), f(D("right_foot").ErrorRad.x), f(D("left_foot").ActualAngularVelocityRadS.x), f(D("right_foot").ActualAngularVelocityRadS.x),
                        f(D("left_shank").ActualAngularVelocityRadS.x), f(D("right_shank").ActualAngularVelocityRadS.x),
                        f(D("left_thigh").ActualAngularVelocityRadS.x), f(D("right_thigh").ActualAngularVelocityRadS.x), W("left_foot"), W("right_foot"),
                        f(t.Com.z), f(_regulator.LastComVz), t.LeftFootN, t.RightFootN, t.PlateLN + t.PlateRN, -(t.GripRawL.y + t.GripRawR.y)));
                    if (t.PlateLN + t.PlateRN < 1 || t.LeftFootN + t.RightFootN < 1)
                        break;
                }
                _regulator.ExcitationDegLeft = _regulator.ExcitationDegRight = 0f;
                yield return _world.Teardown();
                _world = null;
            }
            F.WriteEvidence("a3-arx.csv", csv.ToString());
        }
    }
}
