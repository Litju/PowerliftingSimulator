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
    /// GAM-70 Gate 4: post-separation reference ownership. The grounded C4
    /// pretension reference is only valid while the plates carry the bar. This
    /// harness records, per tick, which writer last owned the static reference,
    /// what reference it applied, the grounded hand command and measured hand
    /// force, the airborne schedule fraction, plate and foot reactions, COM
    /// state/velocity, bar state/velocity and both ankle channels (target,
    /// applied, actual, velocity, modeled demand, capacity and limit
    /// proximity). It is read-only: no gain, limit, contact or solver value is
    /// changed here.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private sealed class OwnershipCase
        {
            public string Name;
            public int Ticks = 900;
            public float MaxIntent = 1f;
            public int AbortAtTick = -1;
            public bool LegacyGroundedOverwrite;
            public bool ContinuousNormalization;
            public bool WindowOnly;
            public int WindowAfterSeparation = 100;
            public bool AttachLeft = true, AttachRight = true;

            public ulong SeparationTick;
            public string PhaseAtEnd = string.Empty;
            public string OwnerAtSeparation = string.Empty;
            public string OwnerAtRampEnd = string.Empty;
            public string OwnerAfterRamp = string.Empty;
            public string OwnerAtEnd = string.Empty;
            public string AppliedAtRampEnd = string.Empty;
            public float PlateBefore, BarY0, BarYAtSeparation, BarYMax, BarRiseWindow;
            public float MinFoot = float.MaxValue, MinFootWindow = float.MaxValue;
            public float MaxComDev, MaxComSpeed, MaxDemandX, MaxDemandZ, MaxProxX, MaxProxZ;
            public float MaxBarTilt, MaxSep, MaxGroundedH, MinGroundedH;
            public int GroundedWritesAtSeparation, AirborneWritesAtSeparation, PretensionUpdatesAtSeparation;
            public int GroundedWritesAtRampEnd, AirborneWritesAtRampEnd, PretensionUpdatesAtRampEnd;
            public int GroundedWritesAtEnd, AirborneWritesAtEnd, PretensionUpdatesAtEnd;
            public bool FootLost, GripAttached = true, BarLimb, OtherPlatform;
            public string Event = "none";
            public readonly List<double[]> AppliedReference = new List<double[]>();
            public readonly StringBuilder Rows = new StringBuilder(Header);

            public static string Header =>
                "case,tick,phase,owner,groundedH,pretUpdate,handTarget,handMeas,frac," +
                "refALX,refALZ,refARX,refARZ,refKLX,refKRX,refHLX,refHRX,refAbdX,refThxX," +
                "plateL,plateR,footL,footR,copLx,copLz,copRx,copRz," +
                "comX,comY,comZ,vx,vy,vz,barY,barVy,barDisp,barTilt," +
                "aXLreq,aXLapp,aXLact,aXLvel,aXLdem,aXLfrac,aXLcap,aXLprox," +
                "aXZreq,aXZapp,aXZact,aXZvel,aXZdem,aXZfrac,aXZcap,aXZprox," +
                "aXRreq,aXRapp,aXRact,aXRvel,aXRdem,aXRfrac,aXRcap,aXRprox," +
                "aZRreq,aZRapp,aZRact,aZRvel,aZRdem,aZRfrac,aZRcap,aZRprox," +
                "regOL,regOR,regIL,regIR,regRL,regRR,regGL,regGR," +
                "gripLy,gripRy,sepL,sepR,event\n";
        }

        private static string Own(double v) => v.ToString("G6", CultureInfo.InvariantCulture);

        private static double[] AppliedSignature(DeadliftSetupHold hold)
        {
            double[] s = new double[16];
            string[] joints = { "left_foot", "right_foot", "left_shank", "right_shank", "left_thigh", "right_thigh", "abdomen", "thorax" };
            for (int i = 0; i < joints.Length; i++)
            {
                Vector3 v = hold.AppliedReferenceAxesFor(joints[i]);
                s[i * 2] = v.x;
                s[i * 2 + 1] = v.z;
            }
            return s;
        }

        private static string AppliedSignatureText(DeadliftSetupHold hold)
        {
            Vector3 a = hold.AppliedReferenceAxesFor("left_foot"), r = hold.AppliedReferenceAxesFor("right_foot");
            return string.Format(CultureInfo.InvariantCulture, "ankle X {0:F1}/{1:F1} Z {2:F1}/{3:F1} N m", a.x, r.x, a.z, r.z);
        }

        /// <summary>
        /// Full sustained Gate 3 pull with the Gate 4 channel trace. The legacy
        /// flag reproduces the exact-HEAD writer competition; it is an A/B
        /// control, never the judged path.
        /// </summary>
        private IEnumerator RunOwnership(OwnershipCase c)
        {
            _ankleRoll = true;
            yield return Prepare(c.AttachLeft, c.AttachRight, false);
            float wbHalf = 0.5f * _world.Bar.Body.mass * -Physics.gravity.y;
            float h = 0.75f * wbHalf;   // same Gate 3 pretension ceiling
            InstallPretensionReference(0.75f * wbHalf, true, true);
            DeadliftContactStatics groundedBreak = SolveStatics(h);
            DeadliftContactStatics boundaryBreak = SolveStatics(wbHalf);
            DeadliftContactStatics zeroBreak = SolveStatics(0f);
            var schedule = new DeadliftBarBreakReference(groundedBreak, boundaryBreak, zeroBreak);
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
            if (c.ContinuousNormalization)
            {
                // Gate 2: normalize the balance loop to the qualified H = 0
                // foot statics over the same airborne load schedule. No gain
                // value is changed; only the multiplier's reference point.
                _regulator.FootLoadRatio = id => schedule.FootNormalN(id, _pull.LoadScheduleFraction) /
                                                 Mathf.Max(schedule.FootNormalAtZeroN(id), 1f);
            }
            _world.Rig.PrimeCommandSource();
            DeadliftAnkleRoll.ValidateAppliedAuthority(_world.Rig);

            Vector3 com0 = _world.WholeBodyCom();
            Vector3 bar0 = _world.Bar.Body.position;
            c.BarY0 = bar0.y;
            c.BarYMax = bar0.y;
            c.MinGroundedH = float.MaxValue;
            int pretensionTicks = 300;
            bool ramped = false;
            int rampEndTick = -1;

            for (int i = 1; i <= c.Ticks; i++)
            {
                float target = i <= A3Base ? 0f : i <= A3Base + A3Ramp ? h * (i - A3Base) / A3Ramp : h;
                _pretension.TrySetTarget(target);
                if (i == pretensionTicks + 1)
                {
                    c.PlateBefore = PlateLeftN() + PlateRightN();
                    _pull.IntentOverride01 = c.MaxIntent;
                }
                if (c.AbortAtTick > 0 && i == c.AbortAtTick)
                    _pull.IntentOverride01 = 0f;

                _world.Step(1);

                var t = _ticks[_ticks.Count - 1];
                Vector3 com = _world.WholeBodyCom();
                Vector3 comV = WholeBodyComVelocity();
                PoweredJointDiagnostic ln = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
                PoweredJointDiagnostic rn = _world.Rig.PoweredController.GetJoint("right_foot").PostPhysicsDiagnostic;
                float barVy = _world.Bar.Body.linearVelocity.y;
                float tilt = Vector3.Angle(_world.Bar.Body.rotation * Vector3.right, Vector3.right);
                float pL = PlateLeftN(), pR = PlateRightN();
                float handMeas = MeasuredPerHandUpN();
                float groundedH = _groundedRef.HandPullN;
                c.MinGroundedH = Mathf.Min(c.MinGroundedH, groundedH);
                c.MaxGroundedH = Mathf.Max(c.MaxGroundedH, groundedH);
                double copLx = CopComponent(_copL, 0), copLz = CopComponent(_copL, 2);
                double copRx = CopComponent(_copR, 0), copRz = CopComponent(_copR, 2);

                if (c.SeparationTick == 0 && pL < DeadliftPullController.SeparationPlateN &&
                    pR < DeadliftPullController.SeparationPlateN && barVy > 0.005f)
                {
                    c.SeparationTick = (ulong)i;
                    c.BarYAtSeparation = _world.Bar.Body.position.y;
                    c.OwnerAtSeparation = _world.Hold.ReferenceOwner.ToString();
                    c.GroundedWritesAtSeparation = _world.Hold.GroundedReferenceWrites;
                    c.AirborneWritesAtSeparation = _world.Hold.AirborneReferenceWrites;
                    c.PretensionUpdatesAtSeparation = _pretension.UpdateCount;
                }
                if (!ramped && c.SeparationTick != 0 && (int)i >= (int)c.SeparationTick + 10)
                {
                    ramped = true;
                    c.OwnerAtRampEnd = _world.Hold.ReferenceOwner.ToString();
                    c.GroundedWritesAtRampEnd = _world.Hold.GroundedReferenceWrites;
                    c.AirborneWritesAtRampEnd = _world.Hold.AirborneReferenceWrites;
                    c.PretensionUpdatesAtRampEnd = _pretension.UpdateCount;
                    c.AppliedAtRampEnd = AppliedSignatureText(_world.Hold);
                }
                if (rampEndTick < 0 && _pull.Phase == DeadliftPullPhase.Separated && _pull.LoadScheduleFraction >= 0.999f)
                    rampEndTick = i;
                if (rampEndTick > 0 && (int)i == rampEndTick + 1)
                    c.OwnerAfterRamp = _world.Hold.ReferenceOwner.ToString();
                c.OwnerAtEnd = _world.Hold.ReferenceOwner.ToString();
                c.GroundedWritesAtEnd = _world.Hold.GroundedReferenceWrites;
                c.AirborneWritesAtEnd = _world.Hold.AirborneReferenceWrites;
                c.PretensionUpdatesAtEnd = _pretension.UpdateCount;

                string evt = "none";
                if (c.Event == "none" && pL + pR < 1 && c.SeparationTick != 0) evt = "plate-unloaded@" + i;
                if (c.Event == "none" && t.LeftFootN + t.RightFootN < 1) evt = "feet-lost@" + i;
                if (c.Event == "none" && t.BarLegN > 1) evt = "bar-limb@" + i;
                if (c.Event == "none" && evt != "none") { c.Event = evt; c.FootLost |= evt.StartsWith("feet-lost", StringComparison.Ordinal); }

                c.BarYMax = Mathf.Max(c.BarYMax, _world.Bar.Body.position.y);
                c.MinFoot = Mathf.Min(c.MinFoot, (float)Math.Min(t.LeftFootN, t.RightFootN));
                c.MaxComDev = Mathf.Max(c.MaxComDev, Vector3.Distance(com, com0));
                c.MaxComSpeed = Mathf.Max(c.MaxComSpeed, comV.magnitude);
                c.MaxSep = Mathf.Max(c.MaxSep, Mathf.Max(float.IsNaN(t.SepL) ? 0f : t.SepL, float.IsNaN(t.SepR) ? 0f : t.SepR));
                c.MaxBarTilt = Mathf.Max(c.MaxBarTilt, tilt);
                c.MaxDemandX = Mathf.Max(c.MaxDemandX, ln.TwistDriveDemandFraction, rn.TwistDriveDemandFraction);
                c.MaxDemandZ = Mathf.Max(c.MaxDemandZ, ln.SecondaryDriveDemandFraction, rn.SecondaryDriveDemandFraction);
                c.MaxProxX = Mathf.Max(c.MaxProxX, ln.LimitProximity, rn.LimitProximity);
                c.MaxProxZ = Mathf.Max(c.MaxProxZ, ln.SecondaryLimitProximity, rn.SecondaryLimitProximity);
                c.BarLimb |= t.BarLegN > 1;
                c.OtherPlatform |= _world.Sensors.Where(x => x.Key != "bar" && !x.Key.EndsWith("_foot"))
                    .Sum(x => x.Value.NormalForce(PowerliftingSimulator.Bench.Unity.BenchSurface.Floor)) > 1f;

                if (c.SeparationTick != 0 && (int)i > (int)c.SeparationTick &&
                    (c.WindowAfterSeparation <= 0 || (int)i <= (int)c.SeparationTick + c.WindowAfterSeparation))
                {
                    c.MinFootWindow = Mathf.Min(c.MinFootWindow, (float)Math.Min(t.LeftFootN, t.RightFootN));
                    c.BarRiseWindow = Mathf.Max(c.BarRiseWindow, _world.Bar.Body.position.y - c.BarY0);
                }

                c.AppliedReference.Add(AppliedSignature(_world.Hold));

                c.Rows.AppendLine(string.Join(",", new[]
                {
                    c.Name, i.ToString(CultureInfo.InvariantCulture), _pull.Phase.ToString(), _world.Hold.ReferenceOwner.ToString(),
                    Own(groundedH), _pretension.UpdateCount.ToString(CultureInfo.InvariantCulture), Own(_pretension.TargetPerHandN), Own(handMeas),
                    Own(_pull.LoadScheduleFraction),
                    Own(_world.Hold.AppliedReferenceAxesFor("left_foot").x), Own(_world.Hold.AppliedReferenceAxesFor("left_foot").z),
                    Own(_world.Hold.AppliedReferenceAxesFor("right_foot").x), Own(_world.Hold.AppliedReferenceAxesFor("right_foot").z),
                    Own(_world.Hold.AppliedReferenceAxesFor("left_shank").x), Own(_world.Hold.AppliedReferenceAxesFor("right_shank").x),
                    Own(_world.Hold.AppliedReferenceAxesFor("left_thigh").x), Own(_world.Hold.AppliedReferenceAxesFor("right_thigh").x),
                    Own(_world.Hold.AppliedReferenceAxesFor("abdomen").x), Own(_world.Hold.AppliedReferenceAxesFor("thorax").x),
                    Own(pL), Own(pR), Own((float)t.LeftFootN), Own((float)t.RightFootN),
                    Own(copLx), Own(copLz), Own(copRx), Own(copRz),
                    Own(com.x), Own(com.y), Own(com.z), Own(comV.x), Own(comV.y), Own(comV.z),
                    Own(_world.Bar.Body.position.y), Own(barVy), Own(_world.Bar.Body.position.y - bar0.y), Own(tilt),
                    Own(ln.PrimaryRequestedAngleDeg), Own(ln.PrimaryAppliedAngleDeg), Own(ln.PrimaryActualAngleDeg), Own(ln.ActualAngularVelocityRadS.x),
                    Own(ln.TwistDriveDemandNm), Own(ln.TwistDriveDemandFraction), Own(ln.MaximumForceNm), Own(ln.LimitProximity),
                    Own(ln.SecondaryRequestedAngleDeg), Own(ln.SecondaryAppliedAngleDeg), Own(ln.SecondaryActualAngleDeg), Own(ln.SecondaryActualAngularVelocityRadS),
                    Own(ln.SecondaryDriveDemandNm), Own(ln.SecondaryDriveDemandFraction), Own(ln.SecondaryMaximumForceNm), Own(ln.SecondaryLimitProximity),
                    Own(rn.PrimaryRequestedAngleDeg), Own(rn.PrimaryAppliedAngleDeg), Own(rn.PrimaryActualAngleDeg), Own(rn.ActualAngularVelocityRadS.x),
                    Own(rn.TwistDriveDemandNm), Own(rn.TwistDriveDemandFraction), Own(rn.MaximumForceNm), Own(rn.LimitProximity),
                    Own(rn.SecondaryRequestedAngleDeg), Own(rn.SecondaryAppliedAngleDeg), Own(rn.SecondaryActualAngleDeg), Own(rn.SecondaryActualAngularVelocityRadS),
                    Own(rn.SecondaryDriveDemandNm), Own(rn.SecondaryDriveDemandFraction), Own(rn.SecondaryMaximumForceNm), Own(rn.SecondaryLimitProximity),
                    Own(_regulator.OffsetDegFor("left_foot")), Own(_regulator.OffsetDegFor("right_foot")),
                    Own(_regulator.IntegralDegFor("left_foot")), Own(_regulator.IntegralDegFor("right_foot")),
                    Own(_regulator.RollOffsetDegFor("left_foot")), Own(_regulator.RollOffsetDegFor("right_foot")),
                    Own(_regulator.GainScaleFor("left_foot")), Own(_regulator.GainScaleFor("right_foot")),
                    Own(t.GripRawL.y), Own(t.GripRawR.y), Own(t.SepL), Own(t.SepR), evt
                }));

                if (c.WindowOnly && c.SeparationTick != 0 && (int)i >= (int)c.SeparationTick + c.WindowAfterSeparation)
                    break;
                if (c.Event.StartsWith("feet-lost", StringComparison.Ordinal) ||
                    c.Event.StartsWith("bar-limb", StringComparison.Ordinal))
                    break;
            }

            c.PhaseAtEnd = _pull.Phase.ToString();
            c.GripAttached = (c.AttachLeft ? (_world.LeftGrip != null && _world.LeftGrip.IsAttached) : true) &&
                             (c.AttachRight ? (_world.RightGrip != null && _world.RightGrip.IsAttached) : true);
            F.WriteEvidence("gate4-ownership-" + c.Name + ".csv", c.Rows.ToString());
            yield return _world.Teardown();
            _world = null;
            _ankleRoll = false;
        }

        private Vector3 WholeBodyComVelocity()
        {
            Vector3 p = Vector3.zero;
            float mass = 0f;
            foreach (PhysicalAthleteRig.SegmentRuntime segment in _world.Rig.Segments.Values)
            {
                p += segment.Body.linearVelocity * segment.Body.mass;
                mass += segment.Body.mass;
            }
            return mass > 0f ? p / mass : Vector3.zero;
        }

        private static double CopComponent(DeadliftFootCopSensor sensor, int axis)
        {
            if (sensor == null || sensor.NormalImpulse <= 1e-6)
                return double.NaN;
            Vector3 c = sensor.Cop;
            return axis == 0 ? c.x : axis == 2 ? c.z : c.y;
        }

        private static string OwnershipLine(OwnershipCase c) => string.Format(CultureInfo.InvariantCulture,
            "{0,-14} legacy {1,-5} | sep@ {2} (owner {3}, grounded writes {4}, airborne {5}, pretension updates {6}) | " +
            "ramp end owner {7} (+grounded {8}, +airborne {9}, +pretension {10}) | next tick owner {30} | end owner {11} (grounded {12}, airborne {13}, pretension {14}) | " +
            "bar {15:F4} -> {16:F4} m (rise 100t {17:F1} mm) | H {18:F1} -> {19:F1} N | min foot {20:F1} N (100t {21:F1}) | COM dev {22:F1} mm | " +
            "demand X {23:F3} Z {24:F3} | prox X {25:F3} Z {26:F3} | tilt {27:F1} deg | event {28} | phase {29}",
            c.Name, c.LegacyGroundedOverwrite, c.SeparationTick, c.OwnerAtSeparation, c.GroundedWritesAtSeparation, c.AirborneWritesAtSeparation, c.PretensionUpdatesAtSeparation,
            c.OwnerAtRampEnd, c.GroundedWritesAtRampEnd, c.AirborneWritesAtRampEnd, c.PretensionUpdatesAtRampEnd,
            c.OwnerAtEnd, c.GroundedWritesAtEnd, c.AirborneWritesAtEnd, c.PretensionUpdatesAtEnd,
            c.BarY0, c.BarYMax, c.BarRiseWindow * 1000f, c.MaxGroundedH, c.MinGroundedH, c.MinFoot, c.MinFootWindow, c.MaxComDev * 1000f,
            c.MaxDemandX, c.MaxDemandZ, c.MaxProxX, c.MaxProxZ, c.MaxBarTilt, c.Event, c.PhaseAtEnd, c.OwnerAfterRamp);

        /// <summary>
        /// Exact-HEAD baseline reproduction on a fresh Unity plant with the
        /// Gate 4 channel trace. This is the control the ownership A/B is
        /// compared against; it must reproduce the committed Gate 3 failure
        /// (separation, 100-tick rise, first support loss, saturated ankles).
        /// </summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 Gate 4 exact-head baseline reproduction; run on demand")]
        public IEnumerator GATE4_EXACT_HEAD_BASELINE_REPRODUCTION()
        {
            var c = new OwnershipCase
            {
                Name = "baseline-head",
                Ticks = 900,
                MaxIntent = 1f,
                AbortAtTick = 600
            };
            yield return RunOwnership(c);

            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 4 exact-HEAD reference-ownership baseline (fresh plant, real 75 kg bar, both rigid grips)");
            report.AppendLine(OwnershipLine(c));
            report.AppendLine("post-separation applied reference at ramp end: " + c.AppliedAtRampEnd);
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "separation: barY {0:F4} m, plate before {1:F1} N | fan-out: grounded writes {2}, airborne writes {3}, pretension updates {4}",
                c.BarYAtSeparation, c.PlateBefore, c.GroundedWritesAtEnd, c.AirborneWritesAtEnd, c.PretensionUpdatesAtEnd));
            F.WriteEvidence("gate4-baseline-head.txt", report.ToString());

            Assert.That(c.SeparationTick, Is.InRange(394ul, 402ul), "exact-HEAD separation tick drifted\n" + report);
            Assert.That(c.BarRiseWindow, Is.EqualTo(0.0174f).Within(0.0015f), "100-tick bar rise drifted\n" + report);
            Assert.That(c.MinFootWindow, Is.EqualTo(104.3f).Within(6f), "100-tick minimum foot load drifted\n" + report);
            Assert.That(c.MaxDemandX, Is.EqualTo(2.074f).Within(0.02f), "ankle X demand drifted\n" + report);
            Assert.That(c.MaxDemandZ, Is.EqualTo(1.634f).Within(0.02f), "ankle Z demand drifted\n" + report);
            Assert.That(c.GroundedWritesAtEnd, Is.GreaterThan(10 * c.AirborneWritesAtEnd),
                "the exact-HEAD baseline no longer has the grounded writer dominating after separation\n" + report);
            Assert.That(c.OwnerAtRampEnd, Is.EqualTo("AirborneBarBreak"),
                "the exact-HEAD baseline did not apply its own ramp\n" + report);
            Assert.That(c.OwnerAfterRamp, Is.EqualTo("GroundedPretension"),
                "the exact-HEAD baseline did not return reference ownership to the grounded writer after the ramp\n" + report);
            Assert.That(c.OwnerAtEnd, Is.EqualTo("GroundedPretension"),
                "the exact-HEAD baseline did not end with the grounded writer owning the reference\n" + report);
            Assert.That(c.MaxGroundedH - c.MinGroundedH, Is.EqualTo(275.9f).Within(0.5f),
                "the exact-HEAD pretension integral did not continue to zero after separation\n" + report);
        }
    }
}
