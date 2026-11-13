using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 C1/C2: six-dimensional contact-wrench measurement and per-tick
    /// Newton-Euler residuals of the bar, each hand and the whole athlete on
    /// the exact B2 grounded bilateral-grip plant, and the bounded factorial
    /// ablation of the existing static gravity reference groups.
    ///
    /// Frames and units: world frame, +Y up, metres, newtons, newton-metres,
    /// rad/s. Moments of the athlete are taken about one fixed point O, the
    /// athlete COM at tick 0; bar moments about its own COM at tick 0. For each
    /// tick k (dt = 0.01 s, two PhysX substeps):
    ///   linear:  R_F = (P_k - P_{k-1})/dt - m g - sum(J_contact)/dt - sum(F_joint)
    ///   angular: R_M = (L_k - L_{k-1})/dt - c_mid x m g - sum(M_contact)/dt - sum(a x F_joint + T_joint)
    /// with L = I_world w + (c - O) x m v. Contact impulses are summed over both
    /// substeps by <see cref="DeadliftWrenchSensor"/>; Joint.currentForce and
    /// currentTorque report only the last substep's solve (an engine limitation
    /// recorded, not corrected). Grip wrenches use the measured contract
    /// (force and moment on the hand, world frame, about the anchor); the
    /// torque hypotheses "world", "hand-local" and "none" are all evaluated
    /// so the contract is tested, not assumed.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private static readonly string[] HipJoints = { "left_thigh", "right_thigh" };
        private static readonly string[] KneeJoints = { "left_shank", "right_shank" };
        private static readonly string[] TrunkJoints = { "abdomen", "thorax" };

        private sealed class BodyState
        {
            public Vector3 P, L, C;
        }

        private sealed class WrenchRun
        {
            public readonly StringBuilder Trace = new StringBuilder();
            public readonly StringBuilder Joints = new StringBuilder();
            public readonly StringBuilder HandRaw = new StringBuilder("tick,hand,mass,com,P,Lspin,rotation,wristAnchor,wristForce,wristTorque,wristAxis,wristSecondary,gripAnchor,gripForce,gripTorque,forearmRotation,barRotation\n");
            public string Event = "horizon-inconclusive";
            public int Samples, EventTick = -1, FirstPlateUnloadTick = -1, FirstAnkleCeilingTick = -1;
            public float MaxHipLimitBeforeEvent;
            public float MaxBarResF, MaxBarResMWorld, MaxBarResMLocal, MaxBarResMNone;
            public float MaxHandResF, MaxHandResMWorld, MaxHandResMNone, MaxAthResF, MaxAthResM;
            public float MaxSystemVerticalRes, SumSqSystemVerticalRes, MaxGripMoment, MaxGripHorizontal, MaxContactPairMismatch;
            public string Tick5 = string.Empty, AtEvent = string.Empty;
            public float PlateAtTick5;
        }

        private Dictionary<string, DeadliftWrenchSensor> _wr;
        private Dictionary<string, BodyState> _prev;
        private Vector3 _o;

        private static Vector3 Spin(Rigidbody b)
        {
            Quaternion r = b.rotation * b.inertiaTensorRotation;
            Vector3 wl = Quaternion.Inverse(r) * b.angularVelocity;
            return r * Vector3.Scale(b.inertiaTensor, wl);
        }

        private BodyState StateOf(Rigidbody b, Vector3 about)
        {
            Vector3 p = b.mass * b.linearVelocity;
            Vector3 c = b.worldCenterOfMass;
            return new BodyState { P = p, C = c, L = Spin(b) + Vector3.Cross(c - about, p) };
        }

        private void InstallWrenchSensors()
        {
            Collider platform = _world.Rig.PlatformCollider;
            var athleteColliders = new HashSet<Collider>(_world.Rig.Segments.Values.Select(s => s.Collider));
            var barColliders = new HashSet<Collider>(_world.Bar.Body.GetComponentsInChildren<Collider>(true));
            Collider lp = _plates.LeftPlate, rp = _plates.RightPlate;
            _wr = new Dictionary<string, DeadliftWrenchSensor>(StringComparer.Ordinal);
            foreach (var seg in _world.Rig.Segments)
            {
                var s = seg.Value.Body.gameObject.AddComponent<DeadliftWrenchSensor>();
                s.Ground = platform;
                s.Classify = c => c.otherCollider == platform ? "floor" : barColliders.Contains(c.otherCollider) ? "bar" :
                    athleteColliders.Contains(c.otherCollider) ? null : "other";
                _wr[seg.Key] = s;
            }
            var bs = _world.Bar.Body.gameObject.AddComponent<DeadliftWrenchSensor>();
            bs.Ground = platform;
            bs.Classify = c => c.otherCollider == platform ? (c.thisCollider == lp ? "plateL" : c.thisCollider == rp ? "plateR" : "floorOther") :
                athleteColliders.Contains(c.otherCollider) ? "athlete" : "other";
            _wr["bar"] = bs;
            _o = _world.WholeBodyCom();
            _lastAthleteWeightN = _world.Rig.TotalMassKg * -Physics.gravity.y;
            _prev = new Dictionary<string, BodyState>(StringComparer.Ordinal);
            foreach (var seg in _world.Rig.Segments)
                _prev[seg.Key] = StateOf(seg.Value.Body, _o);
            _prev["bar"] = StateOf(_world.Bar.Body, _world.Bar.Body.worldCenterOfMass);
            _prev["bar"].L = Spin(_world.Bar.Body);
            foreach (string hand in new[] { "left_hand", "right_hand" })
            {
                Rigidbody h = _world.Rig.Segments[hand].Body;
                _prev[hand + "#com"] = new BodyState { P = h.mass * h.linearVelocity, C = h.worldCenterOfMass, L = Spin(h) };
            }
            _barO = _world.Bar.Body.worldCenterOfMass;
        }

        private Vector3 _barO;
        private float _lastAthleteWeightN;

        private static string V(Vector3 v) => v.ToString("G7").Replace(',', ';');

        private ConfigurableJoint WristOf(string hand) =>
            _world.Rig.Joints.First(j => j.Recipe.ChildId == hand).Joint;

        private void WrenchTick(WrenchRun run, int tick)
        {
            foreach (var s in _wr.Values) s.CompleteTick();
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            Vector3 g = Physics.gravity;

            // Grip joint readings (force and moment on the hand, world, about the anchor; last substep).
            BenchGrip[] grips = { _world.LeftGrip, _world.RightGrip };
            var gF = new Vector3[2]; var gT = new Vector3[2]; var gA = new Vector3[2]; var gTl = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                if (grips[i] == null || !grips[i].IsAttached) continue;
                gF[i] = grips[i].SolverForceRaw;
                gT[i] = grips[i].SolverTorqueRaw;
                Rigidbody hand = _world.Rig.Segments[i == 0 ? "left_hand" : "right_hand"].Body;
                gA[i] = hand.transform.TransformPoint(grips[i].HandAnchorLocal);
                gTl[i] = hand.rotation * gT[i];
            }

            // --- Bar ---
            Rigidbody bar = _world.Bar.Body;
            var bNow = StateOf(bar, _barO);
            BodyState bPrev = _prev["bar"];
            Vector3 cMid = (bNow.C + bPrev.C) * 0.5f;
            DeadliftWrenchSensor.Wrench bw = _wr["bar"].All.Aggregate(default(DeadliftWrenchSensor.Wrench), (a, e) => a + e.Value);
            Vector3 gripOnBarF = -(gF[0] + gF[1]);
            Vector3 gripOnBarMForce = -(Vector3.Cross(gA[0] - _barO, gF[0]) + Vector3.Cross(gA[1] - _barO, gF[1]));
            Vector3 barResF = (bNow.P - bPrev.P) / dt - bar.mass * g - bw.Impulse / dt - gripOnBarF;
            Vector3 barMBase = (bNow.L - bPrev.L) / dt - Vector3.Cross(cMid - _barO, bar.mass * g) - bw.MomentAbout(_barO) / dt - gripOnBarMForce;
            Vector3 barResMWorld = barMBase + (gT[0] + gT[1]);
            Vector3 barResMLocal = barMBase + (gTl[0] + gTl[1]);
            Vector3 barResMNone = barMBase;
            _prev["bar"] = bNow;

            // --- Hands (wrist joint on the hand + grip joint on the hand) ---
            float handResF = 0f, handResMW = 0f, handResMN = 0f;
            for (int i = 0; i < 2; i++)
            {
                string id = i == 0 ? "left_hand" : "right_hand";
                Rigidbody hand = _world.Rig.Segments[id].Body;
                ConfigurableJoint wrist = WristOf(id);
                Vector3 hc = hand.worldCenterOfMass;
                var hNow = new BodyState { P = hand.mass * hand.linearVelocity, C = hc, L = Spin(hand) };
                // Hand moments about its own current COM; spin-only L (orbital term vanishes about the COM).
                bool hasPrev = _prev.TryGetValue(id + "#com", out BodyState hPrev);
                if (!hasPrev) hPrev = hNow;
                Vector3 wa = wrist.transform.TransformPoint(wrist.anchor);
                DeadliftWrenchSensor.Wrench hw = _wr[id].All.Aggregate(default(DeadliftWrenchSensor.Wrench), (a, e) => a + e.Value);
                Vector3 rf = (hNow.P - hPrev.P) / dt - hand.mass * g - hw.Impulse / dt - wrist.currentForce - gF[i];
                Vector3 mBase = (hNow.L - hPrev.L) / dt - hw.MomentAbout(hc) / dt
                                - (Vector3.Cross(wa - hc, wrist.currentForce) + Vector3.Cross(gA[i] - hc, gF[i]));
                Vector3 rmW = mBase - wrist.currentTorque - gT[i];
                Vector3 rmN = mBase;
                string Qs(Quaternion q) => q.ToString("G9").Replace(',', ';');
                run.HandRaw.AppendLine(string.Join(",", tick, id, hand.mass, V(hc), V(hNow.P), V(hNow.L), Qs(hand.rotation), V(wa), V(wrist.currentForce), V(wrist.currentTorque),
                    V(wrist.transform.TransformDirection(wrist.axis)), V(wrist.transform.TransformDirection(wrist.secondaryAxis)), V(gA[i]), V(gF[i]), V(gT[i]),
                    Qs(wrist.connectedBody.rotation), Qs(_world.Bar.Body.rotation)));
                if (hasPrev)
                {
                    handResF = Mathf.Max(handResF, rf.magnitude);
                    handResMW = Mathf.Max(handResMW, rmW.magnitude);
                    handResMN = Mathf.Max(handResMN, rmN.magnitude);
                }
                _prev[id + "#com"] = hNow;
            }

            // --- Whole athlete about fixed O ---
            Vector3 aP = Vector3.zero, aPp = Vector3.zero, aL = Vector3.zero, aLp = Vector3.zero, aG = Vector3.zero;
            DeadliftWrenchSensor.Wrench floorL = default, floorR = default, floorOther = default, barOnAthlete = default, otherOnAthlete = default;
            foreach (var seg in _world.Rig.Segments)
            {
                var now = StateOf(seg.Value.Body, _o);
                BodyState prev = _prev[seg.Key];
                aP += now.P; aPp += prev.P; aL += now.L; aLp += prev.L;
                aG += Vector3.Cross((now.C + prev.C) * 0.5f - _o, seg.Value.Body.mass * g);
                _prev[seg.Key] = now;
                DeadliftWrenchSensor s = _wr[seg.Key];
                if (seg.Key == "left_foot") floorL = s.Get("floor");
                else if (seg.Key == "right_foot") floorR = s.Get("floor");
                else floorOther += s.Get("floor");
                barOnAthlete += s.Get("bar");
                otherOnAthlete += s.Get("other");
            }
            DeadliftWrenchSensor.Wrench ext = floorL + floorR + floorOther + barOnAthlete + otherOnAthlete;
            float mA = _world.Rig.TotalMassKg;
            Vector3 athResF = (aP - aPp) / dt - mA * g - ext.Impulse / dt - (gF[0] + gF[1]);
            Vector3 athResM = (aL - aLp) / dt - aG - ext.MomentAbout(_o) / dt
                              - (Vector3.Cross(gA[0] - _o, gF[0]) + gT[0] + Vector3.Cross(gA[1] - _o, gF[1]) + gT[1]);

            // Action-reaction of bar-athlete contacts as seen from both sides.
            DeadliftWrenchSensor.Wrench athOnBar = _wr["bar"].Get("athlete");
            float pairMismatch = (athOnBar.Impulse + barOnAthlete.Impulse).magnitude / dt;

            DeadliftWrenchSensor.Wrench pl = _wr["bar"].Get("plateL"), pr = _wr["bar"].Get("plateR");
            Vector3 shaft = bar.rotation * Vector3.right;
            Vector3 gripMoment = gT[0] + gT[1];
            float gripMomentMag = Mathf.Max(gT[0].magnitude, gT[1].magnitude);
            float gripHoriz = Mathf.Max(new Vector2(gF[0].x, gF[0].z).magnitude, new Vector2(gF[1].x, gF[1].z).magnitude);
            Vector3 vCom = aP / mA;
            Vector3 com = _world.WholeBodyCom();

            run.Trace.AppendLine(string.Join(",", tick,
                pl.NormalImpulse / dt, pr.NormalImpulse / dt, V(pl.Impulse / dt), V(pr.Impulse / dt), V(pl.MomentAbout(_barO) / dt), V(pr.MomentAbout(_barO) / dt),
                V(floorL.Impulse / dt), V(floorR.Impulse / dt), V(floorL.MomentAbout(_o) / dt), V(floorR.MomentAbout(_o) / dt), V(floorOther.Impulse / dt),
                V(barOnAthlete.Impulse / dt), pairMismatch,
                V(gF[0]), V(gF[1]), V(gT[0]), V(gT[1]), V(gA[0]), V(gA[1]), Vector3.Dot(gripMoment, shaft),
                V(barResF), V(barResMWorld), V(barResMLocal), V(barResMNone),
                handResF, handResMW, handResMN, V(athResF), V(athResM),
                V(com), V(vCom), V((aL - Vector3.Cross(com - _o, aP))), V(bar.position), V(bar.linearVelocity), V(bar.angularVelocity),
                V(_world.Rig.Segments["left_foot"].Body.angularVelocity), V(_world.Rig.Segments["right_foot"].Body.angularVelocity), V(_world.Rig.Segments["pelvis"].Body.angularVelocity), floorL.Points, floorR.Points,
                _world.Hold.Regulator != null ? _world.Hold.Regulator.OffsetDeg : 0f, _world.Hold.Regulator != null ? _world.Hold.Regulator.LastTargetCopZ : float.NaN,
                _world.Hold.Regulator != null ? _world.Hold.Regulator.LastCopZ : float.NaN, _world.Hold.Regulator != null && _world.Hold.Regulator.Saturated));

            // Joint diagnostics: requested/applied/actual, velocity, demand, ceiling, limit proximity.
            foreach (var j in _world.Rig.PoweredController.Joints.Where(j => j.HasPostPhysicsDiagnostic))
            {
                var d = j.PostPhysicsDiagnostic;
                run.Joints.AppendLine(string.Join(",", tick, j.Id,
                    V(d.RequestedTarget.eulerAngles), V(d.AppliedTarget.eulerAngles), V(d.ActualRelative.eulerAngles),
                    V(d.ErrorRad * Mathf.Rad2Deg), V(d.ActualAngularVelocityRadS), d.TwistDriveDemandNm, d.SwingDriveDemandNm, d.MaximumForceNm,
                    d.LimitProximity, V(d.SolverTorqueJointSpaceNm), Quaternion.Angle(Quaternion.identity, _world.Hold.StaticGravityBias(j.Id))));
                if (run.FirstAnkleCeilingTick < 0 && j.Id.EndsWith("_foot", StringComparison.Ordinal) &&
                    Mathf.Max(Mathf.Abs(d.TwistDriveDemandNm), Mathf.Abs(d.SwingDriveDemandNm)) > d.MaximumForceNm)
                    run.FirstAnkleCeilingTick = tick;
                if (run.EventTick < 0 && j.Id.EndsWith("_thigh", StringComparison.Ordinal))
                    run.MaxHipLimitBeforeEvent = Mathf.Max(run.MaxHipLimitBeforeEvent, d.LimitProximity);
            }

            if (tick > 1)
            {
                run.MaxBarResF = Mathf.Max(run.MaxBarResF, barResF.magnitude);
                run.MaxBarResMWorld = Mathf.Max(run.MaxBarResMWorld, barResMWorld.magnitude);
                run.MaxBarResMLocal = Mathf.Max(run.MaxBarResMLocal, barResMLocal.magnitude);
                run.MaxBarResMNone = Mathf.Max(run.MaxBarResMNone, barResMNone.magnitude);
                run.MaxHandResF = Mathf.Max(run.MaxHandResF, handResF);
                run.MaxHandResMWorld = Mathf.Max(run.MaxHandResMWorld, handResMW);
                run.MaxHandResMNone = Mathf.Max(run.MaxHandResMNone, handResMN);
                run.MaxAthResF = Mathf.Max(run.MaxAthResF, athResF.magnitude);
                run.MaxAthResM = Mathf.Max(run.MaxAthResM, athResM.magnitude);
                // Athlete + bar vertical: grip readings cancel, friction has no vertical part on a level platform.
                run.MaxSystemVerticalRes = Mathf.Max(run.MaxSystemVerticalRes, Mathf.Abs(athResF.y + barResF.y));
                run.SumSqSystemVerticalRes += (athResF.y + barResF.y) * (athResF.y + barResF.y);
                run.Samples++;
            }
            if (run.EventTick < 0)
            {
                run.MaxGripMoment = Mathf.Max(run.MaxGripMoment, gripMomentMag);
                run.MaxGripHorizontal = Mathf.Max(run.MaxGripHorizontal, gripHoriz);
                run.MaxContactPairMismatch = Mathf.Max(run.MaxContactPairMismatch, pairMismatch);
            }
            double plates = (pl.NormalImpulse + pr.NormalImpulse) / dt;
            if (run.FirstPlateUnloadTick < 0 && tick > 2 && (pl.NormalImpulse / dt < 1 || pr.NormalImpulse / dt < 1))
                run.FirstPlateUnloadTick = tick;
            string line = string.Format(CultureInfo.InvariantCulture,
                "tick {0}: plates L/R {1:F1}/{2:F1} N | feet L/R Y {3:F1}/{4:F1} N | grip F L {5} R {6} N | grip T L {7} R {8} N m | grip moment about shaft {9:F2} N m | COM v {10} m/s | bar v {11} m/s",
                tick, pl.NormalImpulse / dt, pr.NormalImpulse / dt, floorL.Impulse.y / dt, floorR.Impulse.y / dt,
                gF[0].ToString("F1"), gF[1].ToString("F1"), gT[0].ToString("F1"), gT[1].ToString("F1"), Vector3.Dot(gripMoment, shaft),
                vCom.ToString("F4"), bar.linearVelocity.ToString("F4"));
            if (tick == 5) { run.Tick5 = line; run.PlateAtTick5 = (float)plates; }
            if (run.EventTick < 0)
            {
                if (tick > 2 && plates < 1 && bar.linearVelocity.y > 0) run.Event = "plate-support-lost-bar-rising";
                else if (floorOther.NormalImpulse / dt > 1) run.Event = "nonfoot-platform-contact";
                else if (barOnAthlete.NormalImpulse / dt > 1) run.Event = "bar-athlete-contact";
                else if (_world.LeftGrip != null && _world.RightGrip != null && (_world.LeftGrip.AnchorSeparationM > .005f || _world.RightGrip.AnchorSeparationM > .005f)) run.Event = "grip-drift";
                else if (tick > 2 && (floorL.NormalImpulse + floorR.NormalImpulse) / dt < 1) run.Event = "both-feet-unloaded";
                if (run.Event != "horizon-inconclusive") { run.EventTick = tick; run.AtEvent = line; }
            }
        }

        private const string WrenchHeader = "tick,plateLN,plateRN,plateLF,plateRF,plateLM_barCom,plateRM_barCom,footLF,footRF,footLM_O,footRM_O,floorOtherF,barOnAthleteF,contactPairMismatchN," +
                                            "gripFL_onHand,gripFR_onHand,gripTL_onHand,gripTR_onHand,gripAnchorL,gripAnchorR,gripMomentAboutShaft," +
                                            "barResF,barResM_world,barResM_handLocal,barResM_none,handResFmax,handResM_world_max,handResM_none_max,athResF,athResM," +
                                            "com,comVelocity,athleteSpinAboutCom,barPosition,barVelocity,barAngularVelocity,footLOmega,footROmega,pelvisOmega,footLPoints,footRPoints,regOffsetDeg,regTargetCopZ,regCopZ,regSaturated";
        private const string JointHeader = "tick,joint,requestedEulerDeg,appliedEulerDeg,actualEulerDeg,errorDeg,angularVelocityRadS,twistDemandNm,swingDemandNm,ceilingNm,limitProximity,solverTorqueJointSpaceNm,staticRefDeg";

        private IEnumerator RunWrenchCase(string name, bool reference, Func<string, bool> keep, int horizon, Dictionary<string, WrenchRun> results, Action configure = null, bool grips = true)
        {
            yield return Prepare(grips, grips, reference, keep);
            configure?.Invoke();
            InstallWrenchSensors();
            var run = new WrenchRun();
            run.Trace.AppendLine(WrenchHeader);
            run.Joints.AppendLine(JointHeader);
            for (int i = 1; i <= horizon; i++)
            {
                _world.Step(1);
                WrenchTick(run, i);
                // Stop a few ticks after the first support-mode change, to see its onset only.
                if (run.EventTick > 0 && i >= run.EventTick + 3)
                    break;
            }
            F.WriteEvidence("wrench-" + name + ".csv", run.Trace.ToString());
            F.WriteEvidence("wrench-joints-" + name + ".csv", run.Joints.ToString());
            F.WriteEvidence("wrench-hands-" + name + ".csv", run.HandRaw.ToString());
            results[name] = run;
            yield return _world.Teardown();
            _world = null;
        }

        private static string WrenchSummary(Dictionary<string, WrenchRun> results)
        {
            var b = new StringBuilder();
            b.AppendLine("case,event,eventTick,firstSinglePlateUnloadTick,firstAnkleCeilingTick,maxHipLimitBeforeEvent,platesAtTick5N,maxGripMomentNm,maxGripHorizontalN,contactPairMismatchN,systemVerticalResN,barResF,barResM_world,barResM_handLocal,barResM_none,handResF,handResM_world,handResM_none,athResF,athResM");
            foreach (var e in results)
            {
                WrenchRun r = e.Value;
                b.AppendLine(string.Join(",", e.Key, r.Event, r.EventTick, r.FirstPlateUnloadTick, r.FirstAnkleCeilingTick,
                    r.MaxHipLimitBeforeEvent.ToString("F4", CultureInfo.InvariantCulture), r.PlateAtTick5.ToString("F1", CultureInfo.InvariantCulture),
                    r.MaxGripMoment.ToString("F2", CultureInfo.InvariantCulture), r.MaxGripHorizontal.ToString("F2", CultureInfo.InvariantCulture),
                    r.MaxContactPairMismatch.ToString("F2", CultureInfo.InvariantCulture), r.MaxSystemVerticalRes.ToString("F2", CultureInfo.InvariantCulture),
                    r.MaxBarResF, r.MaxBarResMWorld, r.MaxBarResMLocal, r.MaxBarResMNone, r.MaxHandResF, r.MaxHandResMWorld, r.MaxHandResMNone, r.MaxAthResF, r.MaxAthResM));
            }
            b.AppendLine();
            foreach (var e in results)
                b.AppendLine(e.Key + " | " + e.Value.Tick5 + "\n" + e.Key + " | at event: " + e.Value.AtEvent);
            return b.ToString();
        }

        /// <summary>
        /// C1: wrench measurement and Newton-Euler closure on fresh exact-B2
        /// plants, original static reference ON (control A) and OFF (control B).
        /// Diagnostic, not an A2 acceptance. Asserts only that the instrument
        /// closes the bar's linear balance, so later conclusions rest on a
        /// validated measurement.
        /// </summary>
        [UnityTest, Timeout(900000)]
        public IEnumerator C1_WrenchBalanceOfTheGroundedGrippedB2Plant()
        {
            var results = new Dictionary<string, WrenchRun>();
            yield return RunWrenchCase("A-reference-on", true, null, 300, results);
            yield return RunWrenchCase("B-reference-off", false, null, 300, results);
            string summary = WrenchSummary(results);
            F.WriteEvidence("wrench-c1-summary.csv", summary);
            // Unity reports normal contact impulses only (friction is absent from ContactPoint.impulse,
            // measured: every foot/plate impulse has zero X/Z), so horizontal balance is not closable from
            // contacts; the vertical balance of the athlete + bar system is, and validates the instrument.
            // Bound: RMS <= 1 % of the athlete+bar weight over the run; single-tick maximum <= 5 %
            // (control B's 54.5 N maximum occurs at tick 94 while the bar rolls back at 0.16 m/s on its faceted plates).
            float w = _lastAthleteWeightN + 75f * 9.81f;
            foreach (var r in results)
            {
                float rms = Mathf.Sqrt(r.Value.SumSqSystemVerticalRes / Mathf.Max(1, r.Value.Samples));
                Assert.That(rms, Is.LessThan(0.01f * w), r.Key + ": athlete+bar vertical Newton RMS residual does not close\n" + summary);
                Assert.That(r.Value.MaxSystemVerticalRes, Is.LessThan(0.05f * w), r.Key + ": athlete+bar vertical Newton residual exceeds 5 %\n" + summary);
            }
        }

        /// <summary>
        /// C2: complete 2^3 factorial over the EXISTING hip, knee and trunk
        /// static-reference groups (unchanged values; a group is either kept or
        /// removed). Same plant, fresh initialisation per case. Diagnostic.
        /// </summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 C2 bounded causal ablation; run on demand")]
        public IEnumerator C2_StaticReferenceGroupAblation()
        {
            var results = new Dictionary<string, WrenchRun>();
            foreach (var hip in new[] { true, false })
            foreach (var knee in new[] { true, false })
            foreach (var trunk in new[] { true, false })
            {
                string name = $"hip{(hip ? 1 : 0)}-knee{(knee ? 1 : 0)}-trunk{(trunk ? 1 : 0)}";
                bool H = hip, K = knee, T = trunk;
                Func<string, bool> keep = id => (H && HipJoints.Contains(id)) || (K && KneeJoints.Contains(id)) || (T && TrunkJoints.Contains(id));
                yield return RunWrenchCase(name, true, keep, 300, results);
            }
            F.WriteEvidence("wrench-c2-summary.csv", WrenchSummary(results));
        }
    }
}
