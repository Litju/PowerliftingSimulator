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
        private enum NormalizationMode
        {
            LegacySchedule,
            ContinuousStaticH0,
            MeasuredH0
        }

        private sealed class OwnershipCase
        {
            public string Name;
            public int Ticks = 900;
            public float MaxIntent = 1f;
            public int AbortAtTick = -1;
            public bool LegacyGroundedOverwrite;
            public NormalizationMode Normalization = NormalizationMode.LegacySchedule;
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
            public float GainLeftAtRampEnd, GainRightAtRampEnd, GainLeftAtEnd, GainRightAtEnd;
            public float MaxCopErrorLPostRamp, MaxCopErrorRPostRamp;
            public int CopSamplesPostRamp;
            public int GroundedWritesAtSeparation, AirborneWritesAtSeparation, PretensionUpdatesAtSeparation;
            public int GroundedWritesAtRampEnd, AirborneWritesAtRampEnd, PretensionUpdatesAtRampEnd;
            public int GroundedWritesAtEnd, AirborneWritesAtEnd, PretensionUpdatesAtEnd;
            public bool FootLost, GripAttached = true, BarLimb, OtherPlatform;
            public bool AirborneClaimedAtEnd;
            public float AnkleLeftXAtEnd, AnkleLeftZAtEnd;
            public string Event = "none";
            public readonly List<double[]> AppliedReference = new List<double[]>();
            public readonly List<double> BarYPerTick = new List<double>();
            public readonly List<double> FootSumPerTick = new List<double>();
            public readonly List<string> OwnerPerTick = new List<string>();
            public readonly List<double> FractionPerTick = new List<double>();
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
            _world.Hold.ContinueGroundedReferenceAfterSeparation = c.LegacyGroundedOverwrite;
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
            if (c.Normalization != NormalizationMode.LegacySchedule)
            {
                // Gate 2: only the loop-gain multiplier's reference point is
                // changed. Kp and Ki keep their qualified values.
                if (c.Normalization == NormalizationMode.ContinuousStaticH0)
                {
                    _regulator.FootLoadRatio = id =>
                        _world.Hold.AirborneClaimed
                            ? schedule.FootNormalN(id, _pull.LoadScheduleFraction) /
                              Mathf.Max(schedule.FootNormalAtZeroN(id), 1f)
                            : _groundedRef.FootNormalN(id) / Mathf.Max(_groundedRef.FootNormalAtZeroN(id), 1f);
                }
                else
                {
                    _regulator.FootLoadRatio = id =>
                    {
                        if (!_world.Hold.AirborneClaimed)
                            return _groundedRef.FootNormalN(id) / Mathf.Max(_groundedRef.FootNormalAtZeroN(id), 1f);
                        if (_ticks.Count == 0)
                            return 1f;
                        Tick last = _ticks[_ticks.Count - 1];
                        float normalN = id == "left_foot" ? (float)last.LeftFootN : (float)last.RightFootN;
                        return Mathf.Max(normalN, 1f) / Mathf.Max(schedule.FootNormalAtZeroN(id), 1f);
                    };
                }
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
                    c.GainLeftAtRampEnd = _regulator.GainScaleFor("left_foot");
                    c.GainRightAtRampEnd = _regulator.GainScaleFor("right_foot");
                }
                if (c.SeparationTick != 0 && _pull.LoadScheduleFraction >= 0.999f && _world.Hold.AirborneClaimed)
                {
                    float el = Mathf.Abs(_regulator.MeasuredAlongFor("left_foot") - _regulator.TargetAlongFor("left_foot"));
                    float er = Mathf.Abs(_regulator.MeasuredAlongFor("right_foot") - _regulator.TargetAlongFor("right_foot"));
                    if (!float.IsNaN(el)) { c.MaxCopErrorLPostRamp = Mathf.Max(c.MaxCopErrorLPostRamp, el); c.CopSamplesPostRamp++; }
                    if (!float.IsNaN(er)) { c.MaxCopErrorRPostRamp = Mathf.Max(c.MaxCopErrorRPostRamp, er); c.CopSamplesPostRamp++; }
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
                c.BarYPerTick.Add(_world.Bar.Body.position.y);
                c.FootSumPerTick.Add(t.LeftFootN + t.RightFootN);
                c.OwnerPerTick.Add(_world.Hold.ReferenceOwner.ToString());
                c.FractionPerTick.Add(_pull.LoadScheduleFraction);

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
            c.AirborneClaimedAtEnd = _world.Hold.AirborneClaimed;
            c.AnkleLeftXAtEnd = _world.Hold.AppliedReferenceAxesFor("left_foot").x;
            c.AnkleLeftZAtEnd = _world.Hold.AppliedReferenceAxesFor("left_foot").z;
            c.GainLeftAtEnd = _regulator.GainScaleFor("left_foot");
            c.GainRightAtEnd = _regulator.GainScaleFor("right_foot");
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
                AbortAtTick = 600,
                LegacyGroundedOverwrite = true
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

        /// <summary>
        /// Gate 4 A/B: the only difference between the two fresh plants is the
        /// post-separation reference owner. A reproduces the exact-HEAD writer
        /// competition; B latches the airborne schedule at the measured
        /// separation and stops the grounded integral. The assertion is the
        /// isolation itself (identical state and reference up to the handoff,
        /// divergent owner one tick after the ramp); the sustained physical
        /// outcome is reported, not assumed.
        /// </summary>
        [UnityTest, Timeout(2400000), Explicit("GAM-70 Gate 4 reference-ownership A/B; run on demand")]
        public IEnumerator GATE4_REFERENCE_OWNERSHIP_AB_ISOLATES_THE_POST_SEPARATION_WRITER()
        {
            var a = new OwnershipCase
            {
                Name = "A-legacy-overwrite",
                Ticks = 900,
                MaxIntent = 1f,
                AbortAtTick = 600,
                LegacyGroundedOverwrite = true
            };
            yield return RunOwnership(a);
            var b = new OwnershipCase
            {
                Name = "B-explicit-handoff",
                Ticks = 900,
                MaxIntent = 1f,
                AbortAtTick = 600
            };
            yield return RunOwnership(b);

            int n = Math.Min(a.AppliedReference.Count, b.AppliedReference.Count);
            int div = -1;
            for (int i = 0; i < n && div < 0; i++)
                for (int k = 0; k < 16; k++)
                    if (Math.Abs(a.AppliedReference[i][k] - b.AppliedReference[i][k]) > 1e-6) { div = i; break; }

            double maxBarDelta = 0d, maxFootDelta = 0d;
            int upto = div < 0 ? n : div;
            for (int i = 0; i < upto; i++)
            {
                maxBarDelta = Math.Max(maxBarDelta, Math.Abs(a.BarYPerTick[i] - b.BarYPerTick[i]));
                maxFootDelta = Math.Max(maxFootDelta, Math.Abs(a.FootSumPerTick[i] - b.FootSumPerTick[i]));
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 4 post-separation reference-ownership A/B (fresh plants, identical initial conditions)");
            report.AppendLine(OwnershipLine(a));
            report.AppendLine(OwnershipLine(b));
            if (div >= 0)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "first divergence at tick {0}: A owner {1} ankle X {2:F1}/{3:F1} Z {4:F1}/{5:F1} | B owner {6} ankle X {7:F1}/{8:F1} Z {9:F1}/{10:F1} | fraction {11:F3}",
                    div + 1, a.OwnerPerTick[div], a.AppliedReference[div][0], a.AppliedReference[div][2], a.AppliedReference[div][1], a.AppliedReference[div][3],
                    b.OwnerPerTick[div], b.AppliedReference[div][0], b.AppliedReference[div][2], b.AppliedReference[div][1], b.AppliedReference[div][3],
                    a.FractionPerTick[div]));
                int last = Math.Max(0, div - 1);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "last common tick {0}: fraction {1:F3}, owners {2}/{3} | pre-divergence max plant delta bar {4:E2} m foot {5:E2} N",
                    last + 1, a.FractionPerTick[last], a.OwnerPerTick[last], b.OwnerPerTick[last], maxBarDelta, maxFootDelta));
            }
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "physical outcome: A min foot {0:F1} N COM dev {1:F1} mm proxZ {2:F3} event {3} | B min foot {4:F1} N COM dev {5:F1} mm proxZ {6:F3} event {7}",
                a.MinFoot, a.MaxComDev * 1000f, a.MaxProxZ, a.Event, b.MinFoot, b.MaxComDev * 1000f, b.MaxProxZ, b.Event));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "B references: claim {0}, grounded writes ramp->end {1}, pretension updates ramp->end {2}, applied ankle X {3:F1} Z {4:F1} N m",
                b.AirborneClaimedAtEnd, b.GroundedWritesAtEnd - b.GroundedWritesAtRampEnd, b.PretensionUpdatesAtEnd - b.PretensionUpdatesAtRampEnd,
                b.AnkleLeftXAtEnd, b.AnkleLeftZAtEnd));
            F.WriteEvidence("gate4-ownership-ab.txt", report.ToString());

            Assert.That(a.SeparationTick, Is.GreaterThan(0ul), "A never separated\n" + report);
            Assert.That(b.SeparationTick, Is.EqualTo(a.SeparationTick), "the two fresh plants did not separate at the same tick\n" + report);
            Assert.That(div, Is.GreaterThan(0), "the A/B never diverged in the applied reference\n" + report);
            Assert.That(maxBarDelta, Is.LessThan(1e-6), "the plants differed before the reference handoff (bar)\n" + report);
            Assert.That(maxFootDelta, Is.LessThan(1e-3), "the plants differed before the reference handoff (feet)\n" + report);
            Assert.That(a.OwnerPerTick[div], Is.EqualTo("GroundedPretension"),
                "A did not return ownership to the grounded writer at the first divergence\n" + report);
            Assert.That(b.OwnerPerTick[div], Is.EqualTo("AirborneBarBreak"),
                "B did not keep airborne ownership at the first divergence\n" + report);
            Assert.That(a.FractionPerTick[div], Is.EqualTo(1f).Within(1e-5f),
                "the first divergence is not at the end of the airborne ramp\n" + report);
            Assert.That(a.GroundedWritesAtEnd, Is.GreaterThan(10 * a.AirborneWritesAtEnd),
                "A no longer reproduces the grounded-writer dominance\n" + report);
            Assert.That(b.AirborneClaimedAtEnd, Is.True, "B did not latch the airborne owner\n" + report);
            Assert.That(b.OwnerAtEnd, Is.EqualTo("AirborneBarBreak"), "B did not end with the airborne owner\n" + report);
            Assert.That(b.GroundedWritesAtEnd, Is.EqualTo(b.GroundedWritesAtRampEnd),
                "B still executed grounded reference writes after the handoff\n" + report);
            Assert.That(b.PretensionUpdatesAtEnd, Is.EqualTo(b.PretensionUpdatesAtRampEnd),
                "B still executed pretension integral updates after the handoff\n" + report);
        }

        /// <summary>
        /// Gate 2: independent test of the ankle loop-gain multiplier. All three
        /// fresh plants run the same fixed airborne ownership, gains, capacities
        /// and load schedule; only the multiplier's reference point changes.
        /// Control is the committed mode-span schedule; C normalizes the same
        /// blend to the qualified H = 0 foot statics; D uses the realized
        /// per-foot normal over the same H = 0 anchor (continuous across the
        /// handoff). The physical outcome is reported, not tuned.
        /// </summary>
        [UnityTest, Timeout(2400000), Explicit("GAM-70 Gate 2 continuous load normalization test; run on demand")]
        public IEnumerator GATE2_CONTINUOUS_LOAD_NORMALIZATION_AGAINST_H0_IS_INDEPENDENTLY_TESTED()
        {
            var control = new OwnershipCase { Name = "N0-control-schedule", Ticks = 900, MaxIntent = 1f, AbortAtTick = 600 };
            yield return RunOwnership(control);
            var continuous = new OwnershipCase
            {
                Name = "N1-static-h0",
                Ticks = 900,
                MaxIntent = 1f,
                AbortAtTick = 600,
                Normalization = NormalizationMode.ContinuousStaticH0
            };
            yield return RunOwnership(continuous);
            var measured = new OwnershipCase
            {
                Name = "N2-measured-h0",
                Ticks = 900,
                MaxIntent = 1f,
                AbortAtTick = 600,
                Normalization = NormalizationMode.MeasuredH0
            };
            yield return RunOwnership(measured);

            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 2 ankle load-gain normalization A/B/C (fixed ownership, identical gains and schedule)");
            foreach (OwnershipCase c in new[] { control, continuous, measured })
            {
                report.AppendLine(OwnershipLine(c));
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-20} gain ramp-end L/R {1:F3}/{2:F3} end L/R {3:F3}/{4:F3} | post-ramp COP error max L/R {5:F1}/{6:F1} mm ({7} samples) | bar rise {8:F1} mm | min foot {9:F1} N | COM {10:F1} mm | demand X {11:F3} Z {12:F3}",
                    c.Name, c.GainLeftAtRampEnd, c.GainRightAtRampEnd, c.GainLeftAtEnd, c.GainRightAtEnd,
                    c.MaxCopErrorLPostRamp * 1000f, c.MaxCopErrorRPostRamp * 1000f, c.CopSamplesPostRamp,
                    (c.BarYMax - c.BarY0) * 1000f, c.MinFoot, c.MaxComDev * 1000f, c.MaxDemandX, c.MaxDemandZ));
            }
            F.WriteEvidence("gate2-normalization-ab.txt", report.ToString());

            Assert.That(control.SeparationTick, Is.GreaterThan(0ul), "control never separated\n" + report);
            Assert.That(continuous.SeparationTick, Is.EqualTo(control.SeparationTick), "the static-H0 plant did not reproduce the transition\n" + report);
            Assert.That(measured.SeparationTick, Is.EqualTo(control.SeparationTick), "the measured-H0 plant did not reproduce the transition\n" + report);
            Assert.That(continuous.GainLeftAtEnd, Is.GreaterThan(control.GainLeftAtEnd + 0.1f),
                "the continuous H = 0 normalization did not change the gain multiplier\n" + report);
            Assert.That(measured.GainLeftAtEnd, Is.Not.EqualTo(control.GainLeftAtEnd).Within(0.01f),
                "the measured H = 0 normalization did not change the gain multiplier\n" + report);
        }

        /// <summary>
        /// Executable regression: reintroducing the exact-HEAD writer
        /// competition (a grounded reference write, or a pretension update,
        /// after the airborne ramp) fails this test.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator GATE4_AIRBORNE_REFERENCE_OWNER_IS_LATCHED_AND_GROUNDED_WRITES_STOP()
        {
            var c = new OwnershipCase
            {
                Name = "regression-ownership",
                Ticks = 900,
                MaxIntent = 1f,
                AbortAtTick = 600,
                WindowOnly = true,
                WindowAfterSeparation = 20
            };
            yield return RunOwnership(c);

            var report = new StringBuilder();
            report.AppendLine("GAM-70 Gate 4 reference-ownership regression (window ends 20 ticks after separation)");
            report.AppendLine(OwnershipLine(c));
            F.WriteEvidence("gate4-ownership-regression.txt", report.ToString());

            Assert.That(c.SeparationTick, Is.GreaterThan(0ul), "no measured separation\n" + report);
            Assert.That(c.AirborneClaimedAtEnd, Is.True, "the airborne reference owner was not claimed\n" + report);
            Assert.That(c.OwnerAtEnd, Is.EqualTo("AirborneBarBreak"), "the airborne schedule does not own the reference\n" + report);
            Assert.That(c.GroundedWritesAtEnd, Is.EqualTo(c.GroundedWritesAtRampEnd),
                "a grounded reference write happened after the airborne ramp\n" + report);
            Assert.That(c.PretensionUpdatesAtEnd, Is.EqualTo(c.PretensionUpdatesAtRampEnd),
                "the grounded pretension integral kept running after the handoff\n" + report);
            Assert.That(c.AnkleLeftXAtEnd, Is.EqualTo(77.5f).Within(3f),
                "the applied airborne ankle X reference is not the boundary solution\n" + report);
            Assert.That(c.AnkleLeftZAtEnd, Is.EqualTo(0f).Within(0.5f),
                "the applied airborne ankle Z reference is not the boundary solution\n" + report);
        }
    }
}
