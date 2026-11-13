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
    /// GAM-70 Gate 3: the first real deadlift movement. The player-issued PULL
    /// intent is a bounded hip/knee extension reference on the already
    /// qualified grounded A3 plant. The bar stays dynamic; both grips are
    /// physical; the transition to the separated phase is measured from plate
    /// unloading and positive bar velocity, never from elapsed time.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private sealed class BarBreakCase
        {
            public string Name;
            public int Ticks;
            public float H, MaxIntent, AbortAtTick = -1;
            public bool CleanSeparation, GripAttached = true, FeetLoaded = true, BarLimb, OtherPlatform, Saturated, SecChannels = true;
            public string Event = "none", PhaseAtEnd = "Grounded";
            public ulong SeparationTick;
            public float PlateBefore, PlateAtSeparationL, PlateAtSeparationR, BarY0, BarYMax, BarVyAtSeparation;
            public float MinFoot, MaxComDev, MaxGripSum, MaxDemandX, MaxDemandZ, MaxProxX, MaxProxZ, MaxSep, MaxBarTilt, MaxPullApplied;
            public int WindowAfterSeparation;
            public bool WindowOnly;
            public float WindowMinFoot = float.MaxValue, WindowMaxComDev, WindowMaxDemandX, WindowMaxDemandZ, WindowBarRise, WindowMaxGripSum;
            public int WindowTicks;
            public readonly StringBuilder Rows = new StringBuilder("case,tick,phase,intent,applied,hipDeg,kneeDeg,plateL,plateR,footL,footR,barY,barVy,barTilt,comX,comY,comZ,comDevX,comDevY,comDevZ,gripL,gripR,sepL,sepR,demandX,demandZ,proxX,proxZ,event\n");
        }

        private DeadliftPullController _pull;

        private float PlateLeftN() => (float)(_plates.LeftNormalImpulse / SimulationConstants.FixedDeltaTimeSeconds);
        private float PlateRightN() => (float)(_plates.RightNormalImpulse / SimulationConstants.FixedDeltaTimeSeconds);

        private IEnumerator RunBarBreak(BarBreakCase c, bool attachLeft, bool attachRight, bool drives = true)
        {
            _ankleRoll = true;
            yield return Prepare(attachLeft, attachRight, false);
            float wbHalf = 0.5f * _world.Bar.Body.mass * -Physics.gravity.y;
            c.H = c.H > 0f ? c.H : 0.75f * wbHalf;   // Gate 3 pretension ceiling: plates still loaded, 75 % of half the bar
            InstallPretensionReference(0.75f * wbHalf, drives, true);
            var groundedBreak = SolveStatics(c.H);
            var boundaryBreak = SolveStatics(wbHalf);
            var schedule = new DeadliftBarBreakReference(groundedBreak, boundaryBreak);
            _world.Hold.UseBarBreakReference(schedule);
            _pull = new DeadliftPullController(_world.Rig, _world.Bar, PlateLeftN, PlateRightN)
            {
                RampPerSecond = 1f,
                ReleasePerSecond = 2f,
                HipExtensionLimitDeg = 2.5f,
                KneeExtensionLimitDeg = 2f,
                TrunkExtensionLimitDeg = -1.5f,
                MaxComShiftM = 0.05f,
                GovernorReleaseM = 0.03f,
                IntentOverride01 = 0f
            };
            _world.Hold.Pull = _pull;
            _world.Rig.PrimeCommandSource();
            if (drives)
                DeadliftAnkleRoll.ValidateAppliedAuthority(_world.Rig);

            Vector3 com0 = _world.WholeBodyCom();
            Vector3 bar0 = _world.Bar.Body.position;
            c.BarY0 = bar0.y;
            c.BarYMax = bar0.y;
            c.MinFoot = float.MaxValue;
            int pretensionTicks = 300;
            for (int i = 1; i <= c.Ticks; i++)
            {
                float h = i <= A3Base ? 0f : i <= A3Base + A3Ramp ? c.H * (i - A3Base) / A3Ramp : c.H;
                _pretension.TrySetTarget(h);
                if (i == pretensionTicks + 1)
                {
                    c.PlateBefore = PlateLeftN() + PlateRightN();
                    _pull.IntentOverride01 = c.MaxIntent;
                }
                if (c.AbortAtTick > 0 && i == c.AbortAtTick)
                    _pull.IntentOverride01 = 0f;
                _world.Step(1);
                var t = _ticks.Last();
                Vector3 com = _world.WholeBodyCom();
                PoweredJointDiagnostic l = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
                PoweredJointDiagnostic r = _world.Rig.PoweredController.GetJoint("right_foot").PostPhysicsDiagnostic;
                float barVy = _world.Bar.Body.linearVelocity.y;
                float tilt = Vector3.Angle(_world.Bar.Body.rotation * Vector3.right, Vector3.right);
                float pL = PlateLeftN(), pR = PlateRightN();
                string evt = "none";
                if (c.Event == "none" && pL + pR < 1) evt = "plate-unloaded@" + i;
                if (c.Event == "none" && t.LeftFootN + t.RightFootN < 1) evt = "feet-lost@" + i;
                if (c.Event == "none" && t.BarLegN > 1) evt = "bar-limb@" + i;
                if (c.Event == "none" && evt != "none") c.Event = evt;
                if (c.SeparationTick == 0 && pL < DeadliftPullController.SeparationPlateN && pR < DeadliftPullController.SeparationPlateN && barVy > 0.005f)
                {
                    c.SeparationTick = (ulong)i;
                    c.PlateAtSeparationL = pL; c.PlateAtSeparationR = pR;
                    c.BarVyAtSeparation = barVy;
                }
                c.CleanSeparation |= attachLeft && attachRight && c.SeparationTick != 0 && pL + pR < 1 && barVy > 0.005f && tilt < 2f;
                c.BarYMax = Mathf.Max(c.BarYMax, _world.Bar.Body.position.y);
                if (c.SeparationTick != 0 && c.WindowAfterSeparation > 0 && (int)i <= (int)c.SeparationTick + c.WindowAfterSeparation)
                {
                    c.WindowTicks = Mathf.Max(c.WindowTicks, i - (int)c.SeparationTick + 1);
                    c.WindowMinFoot = Mathf.Min(c.WindowMinFoot, (float)Math.Min(t.LeftFootN, t.RightFootN));
                    c.WindowMaxComDev = Mathf.Max(c.WindowMaxComDev, Vector3.Distance(com, com0));
                    c.WindowMaxDemandX = Mathf.Max(c.WindowMaxDemandX, l.TwistDriveDemandFraction, r.TwistDriveDemandFraction);
                    c.WindowMaxDemandZ = Mathf.Max(c.WindowMaxDemandZ, l.SecondaryDriveDemandFraction, r.SecondaryDriveDemandFraction);
                    c.WindowMaxGripSum = Mathf.Max(c.WindowMaxGripSum, Mathf.Abs(t.GripRawL.y + t.GripRawR.y));
                    c.WindowBarRise = Mathf.Max(c.WindowBarRise, _world.Bar.Body.position.y - c.BarY0);
                }
                c.MinFoot = Mathf.Min(c.MinFoot, (float)Math.Min(t.LeftFootN, t.RightFootN));
                c.MaxComDev = Mathf.Max(c.MaxComDev, Vector3.Distance(com, com0));
                c.MaxGripSum = Mathf.Max(c.MaxGripSum, Mathf.Abs(t.GripRawL.y + t.GripRawR.y));
                c.MaxSep = Mathf.Max(c.MaxSep, Mathf.Max(float.IsNaN(t.SepL) ? 0f : t.SepL, float.IsNaN(t.SepR) ? 0f : t.SepR));
                c.MaxBarTilt = Mathf.Max(c.MaxBarTilt, tilt);
                c.MaxPullApplied = Mathf.Max(c.MaxPullApplied, _pull.Applied01);
                c.Saturated |= _regulator.Saturated;
                c.BarLimb |= t.BarLegN > 1;
                c.OtherPlatform |= _world.Sensors.Where(x => x.Key != "bar" && !x.Key.EndsWith("_foot"))
                    .Sum(x => x.Value.NormalForce(PowerliftingSimulator.Bench.Unity.BenchSurface.Floor)) > 1f;
                c.SecChannels &= l.HasSecondaryChannel && r.HasSecondaryChannel;
                c.MaxDemandX = Mathf.Max(c.MaxDemandX, l.TwistDriveDemandFraction, r.TwistDriveDemandFraction);
                c.MaxDemandZ = Mathf.Max(c.MaxDemandZ, l.SecondaryDriveDemandFraction, r.SecondaryDriveDemandFraction);
                c.MaxProxX = Mathf.Max(c.MaxProxX, l.LimitProximity, r.LimitProximity);
                c.MaxProxZ = Mathf.Max(c.MaxProxZ, l.SecondaryLimitProximity, r.SecondaryLimitProximity);
                c.Rows.AppendLine(string.Join(",", new[]
                {
                    c.Name, i.ToString(CultureInfo.InvariantCulture), _pull.Phase.ToString(), Fmt(_pull.Intent01), Fmt(_pull.Applied01),
                    Fmt(_pull.HipExtensionDeg), Fmt(_pull.KneeExtensionDeg), Fmt(pL), Fmt(pR), Fmt((float)t.LeftFootN), Fmt((float)t.RightFootN),
                    Fmt(_world.Bar.Body.position.y), Fmt(barVy), Fmt(tilt), Fmt(com.x), Fmt(com.y), Fmt(com.z),
                    Fmt(com.x - com0.x), Fmt(com.y - com0.y), Fmt(com.z - com0.z), Fmt(t.GripRawL.y), Fmt(t.GripRawR.y),
                    Fmt(t.SepL), Fmt(t.SepR), Fmt(c.MaxDemandX), Fmt(c.MaxDemandZ), Fmt(c.MaxProxX), Fmt(c.MaxProxZ), evt
                }));
                if (c.WindowOnly && c.SeparationTick != 0 && (int)i >= (int)c.SeparationTick + c.WindowAfterSeparation)
                    break;
                if (c.Event.StartsWith("feet-lost", StringComparison.Ordinal) || c.Event.StartsWith("bar-limb", StringComparison.Ordinal))
                    break;
            }
            c.PhaseAtEnd = _pull.Phase.ToString();
            c.GripAttached = (attachLeft ? (_world.LeftGrip != null && _world.LeftGrip.IsAttached) : true) &&
                             (attachRight ? (_world.RightGrip != null && _world.RightGrip.IsAttached) : true);
            F.WriteEvidence("gate3-" + c.Name + ".csv", c.Rows.ToString());
            yield return _world.Teardown();
            _world = null;
            _ankleRoll = false;
        }

        private static string BarBreakLine(BarBreakCase c) => string.Format(CultureInfo.InvariantCulture,
            "{0,-18} intent {1:F2} | play {2}/{3} | phase {4} sep@ {5} | plate before {6:F1} N | bar Y {7:F4} -> {8:F4} m (rise {9:F1} mm) | sep plates {10:F1}/{11:F1} N vy {12:F3} m/s | min foot {13:F1} N | COM dev {14:F1} mm | grip |sum| {15:F1} N sep {16:F3} mm | demand X {17:F3} Z {18:F3} | prox X {19:F3} Z {20:F3} | tilt {21:F1} deg | event {22} | channels {23}",
            c.Name, c.MaxIntent, c.Ticks, c.Ticks, c.PhaseAtEnd, c.SeparationTick, c.PlateBefore, c.BarY0, c.BarYMax, (c.BarYMax - c.BarY0) * 1000f,
            c.PlateAtSeparationL, c.PlateAtSeparationR, c.BarVyAtSeparation, c.MinFoot, c.MaxComDev * 1000f, c.MaxGripSum, c.MaxSep * 1000f,
            c.MaxDemandX, c.MaxDemandZ, c.MaxProxX, c.MaxProxZ, c.MaxBarTilt, c.Event, c.SecChannels);

        [UnityTest, Timeout(1800000), Explicit("GAM-70 Gate 3 pull-channel mapping; run on demand")]
        public IEnumerator GATE3_PULL_CHANNEL_MAPPING_AND_TRUNK_SIGN()
        {
            var report = new StringBuilder();
            var cases = new (string name, string[] joints, Vector3 axis, float deg)[]
            {
                ("hip+2", new[] { "left_thigh", "right_thigh" }, Vector3.right, 2f),
                ("hip+4", new[] { "left_thigh", "right_thigh" }, Vector3.right, 4f),
                ("hip+6", new[] { "left_thigh", "right_thigh" }, Vector3.right, 6f),
                ("knee-2", new[] { "left_shank", "right_shank" }, Vector3.right, -2f),
                ("trunk+2", new[] { "abdomen", "thorax" }, Vector3.right, 2f),
                ("trunk-2", new[] { "abdomen", "thorax" }, Vector3.right, -2f),
                ("trunk+4", new[] { "abdomen", "thorax" }, Vector3.right, 4f)
            };
            foreach (var c in cases)
            {
                _ankleRoll = true;
                yield return Prepare(true, true, false);
                float wbHalf = 0.5f * _world.Bar.Body.mass * -Physics.gravity.y;
                InstallPretensionReference(0.75f * wbHalf, true, true);
                _world.Rig.PrimeCommandSource();
                for (int i = 1; i <= 300; i++)
                {
                    float h = i <= A3Base ? 0f : i <= A3Base + A3Ramp ? 0.5f * wbHalf * (i - A3Base) / A3Ramp : 0.5f * wbHalf;
                    _pretension.TrySetTarget(h);
                    _world.Step(1);
                }
                int n0 = _ticks.Count;
                float hand0 = 0f, comZ0 = 0f, comY0 = 0f, barY0 = _world.Bar.Body.position.y, plate0 = 0f;
                for (int i = n0 - 10; i < n0; i++)
                {
                    var t = _ticks[i];
                    hand0 += -(t.GripRawL.y + t.GripRawR.y) * 0.5f / 10f;
                    comZ0 += t.Com.z / 10f; comY0 += t.Com.y / 10f;
                    plate0 += (float)((t.PlateLN + t.PlateRN) / 10.0);
                }
                foreach (string joint in c.joints)
                    _world.Hold.IdentificationOffsets[joint] = Quaternion.AngleAxis(c.deg, c.axis);
                for (int i = 0; i < 60; i++)
                    _world.Step(1);
                float hand1 = 0f, comZ1 = 0f, comY1 = 0f, plate1 = 0f;
                for (int i = _ticks.Count - 10; i < _ticks.Count; i++)
                {
                    var t = _ticks[i];
                    hand1 += -(t.GripRawL.y + t.GripRawR.y) * 0.5f / 10f;
                    comZ1 += t.Com.z / 10f; comY1 += t.Com.y / 10f;
                    plate1 += (float)((t.PlateLN + t.PlateRN) / 10.0);
                }
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-9}: hand pull {1:F1} -> {2:F1} N (d {3:F1}) | plates {4:F1} -> {5:F1} N (d {6:F1}) | COM z {7:F1} -> {8:F1} mm (d {9:F1}) | COM y {10:F1} -> {11:F1} mm (d {12:F1}) | bar Y d {13:F2} mm | event {14}",
                    c.name, hand0, hand1, hand1 - hand0, plate0, plate1, plate1 - plate0,
                    comZ0 * 1000f, comZ1 * 1000f, (comZ1 - comZ0) * 1000f,
                    comY0 * 1000f, comY1 * 1000f, (comY1 - comY0) * 1000f,
                    (_world.Bar.Body.position.y - barY0) * 1000f,
                    _ticks.Count > 300 && _ticks[_ticks.Count - 1].PlateLN + _ticks[_ticks.Count - 1].PlateRN < 1 ? "plate-lost" : "none"));
                _world.Hold.IdentificationOffsets.Clear();
                yield return _world.Teardown();
                _world = null;
                _ankleRoll = false;
            }
            F.WriteEvidence("gate3-pull-mapping.txt", report.ToString());
        }

        /// <summary>
        /// Diagnostic bar-separation event: the physical event itself, measured
        /// for 100 ticks after the first sustained plate unload. This does NOT
        /// qualify sustained balance or the full Gate 3 acceptance; the failing
        /// acceptance below is preserved and the first causal boundary is
        /// reported in docs/evidence/GAM-70/gate3-bar-break.txt.
        /// </summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator GATE3_BAR_SEPARATION_EVENT_IS_PHYSICALLY_OBSERVED()
        {
            var c = new BarBreakCase
            {
                Name = "separation-event",
                Ticks = 900,
                MaxIntent = 1f,
                WindowAfterSeparation = 100,
                WindowOnly = true
            };
            yield return RunBarBreak(c, true, true);
            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 3 bar-separation event (not the full sustained-lift acceptance)");
            report.AppendLine(BarBreakLine(c));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "100 ticks after separation: min foot {0:F1} N | max COM dev {1:F1} mm | max |grip sum| {2:F1} N | bar rise {3:F1} mm | X demand {4:F3} | Z demand {5:F3} | window ticks {6}",
                c.WindowMinFoot, c.WindowMaxComDev * 1000f, c.WindowMaxGripSum, c.WindowBarRise * 1000f, c.WindowMaxDemandX, c.WindowMaxDemandZ, c.WindowTicks));
            F.WriteEvidence("gate3-separation-event.txt", report.ToString());
            Assert.That(c.SeparationTick, Is.GreaterThan(0ul), "no measured plate separation\n" + report);
            Assert.That(c.CleanSeparation, Is.True, "both plates did not unload with upward bar motion\n" + report);
            Assert.That(c.WindowBarRise, Is.GreaterThan(0.010f), "the bar did not acquire 10 mm of real upward motion\n" + report);
            Assert.That(c.WindowMinFoot, Is.GreaterThan(50f), "a foot unloaded during the separation window\n" + report);
            Assert.That(c.GripAttached, Is.True, "a grip detached\n" + report);
            Assert.That(c.BarLimb, Is.False, "the bar touched a limb\n" + report);
            Assert.That(c.OtherPlatform, Is.False, "a non-foot body touched the platform\n" + report);
            Assert.That(c.WindowMaxComDev, Is.LessThan(0.10f), "the COM moved more than 100 mm during the separation window\n" + report);
            Assert.That(c.MaxIntent, Is.EqualTo(1f));
        }

        [UnityTest, Timeout(1800000), Explicit("GAM-70 Gate 3 sustained bar-break acceptance FAILS at the balance/ankle-authority boundary; see docs/evidence/GAM-70/gate3-bar-break.txt; run on demand")]
        public IEnumerator GATE3_PLAYER_PULL_BREAKS_THE_GROUNDED_BAR_AND_NO_PULL_DOES_NOT()
        {
            var noPull = new BarBreakCase { Name = "no-pull", Ticks = 900, MaxIntent = 0f };
            yield return RunBarBreak(noPull, true, true);
            var pull = new BarBreakCase { Name = "pull", Ticks = 900, MaxIntent = 1f, AbortAtTick = 600 };
            yield return RunBarBreak(pull, true, true);
            var light = new BarBreakCase { Name = "insufficient-pull", Ticks = 900, MaxIntent = 0.1f };
            yield return RunBarBreak(light, true, true);
            var one = new BarBreakCase { Name = "single-grip", Ticks = 900, MaxIntent = 1f };
            yield return RunBarBreak(one, true, false);
            var abort = new BarBreakCase { Name = "abort", Ticks = 900, MaxIntent = 1f, AbortAtTick = 400 };
            yield return RunBarBreak(abort, true, true);

            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 3 physical bar break: player PULL intent on the qualified grounded A3 plant");
            report.AppendLine("Pull mapping: positive hip X +8 deg * applied, negative knee X -8 deg * applied, one-second intent ramp; trunk 0 (sign not measured).");
            foreach (BarBreakCase c in new[] { noPull, pull, light, one, abort })
                report.AppendLine(BarBreakLine(c));
            F.WriteEvidence("gate3-bar-break.txt", report.ToString());

            Assert.That(noPull.CleanSeparation, Is.False, "the bar separated without a pull\n" + report);
            Assert.That(noPull.Event, Is.EqualTo("none"), "the no-pull control lost a support channel\n" + report);
            Assert.That(pull.CleanSeparation, Is.True, "PULL did not unload both plates and lift the bar\n" + report);
            Assert.That(pull.PhaseAtEnd, Is.EqualTo("Separated"), "the measured separation phase was not entered\n" + report);
            Assert.That(pull.GripAttached, Is.True, "a grip detached during the pull\n" + report);
            Assert.That(pull.MinFoot, Is.GreaterThan(0.5f * 490f), "foot support dropped during the pull\n" + report);
            Assert.That(pull.BarLimb, Is.False, "the bar touched a limb during the pull\n" + report);
            Assert.That(pull.OtherPlatform, Is.False, "a non-foot body touched the platform\n" + report);
            Assert.That(pull.MaxComDev, Is.LessThan(0.05f), "the athlete lost balance during the pull\n" + report);
            Assert.That(pull.MaxDemandX, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), "a drive saturated during the pull\n" + report);
            Assert.That(pull.MaxDemandZ, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), "the ankle roll channel saturated during the pull\n" + report);
            Assert.That(pull.SecChannels, Is.True, "an ankle Z channel disappeared during the pull\n" + report);
            Assert.That(light.CleanSeparation, Is.False, "the insufficient-pull control separated the bar\n" + report);
            Assert.That(one.CleanSeparation, Is.False, "a single grip produced a clean bilateral bar break\n" + report);
            Assert.That(abort.PhaseAtEnd, Is.EqualTo("Separated").Or.EqualTo("Aborted"), "the abort phase was not reported\n" + report);
        }
    }
}
