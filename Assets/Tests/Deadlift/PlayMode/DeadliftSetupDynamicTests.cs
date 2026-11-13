using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A2 dynamic setup hold and grounded Gate A3, on the actual
    /// authoritative plant (PhysicalAthletePhysics scene, real 75 kg bar on its
    /// 450 mm plates, canonical athlete).
    ///
    /// Initial condition: the B2 sumo pronated setup accepted by the independent
    /// kinematic table (DeadliftSetupFeasibilityTests). Its solver parameter
    /// vector is recorded below and RE-VERIFIED by the same table before every
    /// run, so a recorded vector can never stand in for acceptance. Placement is
    /// a one-time authored initial condition before tick zero (velocities zero);
    /// after it only the solver moves bodies, and the only joint-command writer
    /// is DeadliftSetupHold through PoweredJointController.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const float BarKg = 75f;
        private const int Ticks = 600;
        private const int Tail = 200;
        private DeadliftPlantWorld _world;

        /// <summary>Recorded B2 accepted parameter vector (gateA2-B2-details.txt, solver joint margin 3 deg).</summary>
        internal static readonly double[] QualifiedB2 = { 0.44873253253505496,-0.33476850625538729,0.35469749710446047,0.60197627522658725,0.095005754191138964,8.1474500898589467E-86,-2.1297258359899325,-0.64981731689105693,-0.63155432879916118,2.0765302440179285,-0.17968501923679961,-2.1297325168325476,0.64980317747623928,0.63154202482236887,2.07652312414234,-0.17967646160892861,0.22568810829812166,1.7143159013914762,-0.5061553689509265,0.01354031767628883,-0.034157440961880739,0.11136849893022829,0.00025151355487624031,-0.22573335661705871,-1.7143017984473989,-0.50616384793256242,0.013510471990099304,0.034374300362708721,-0.11136254286841699,0.00018716187426342558,-0.012886743009090037 };

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
            {
                yield return _world.Teardown();
                _world = null;
            }
        }

        private sealed class Tick
        {
            public double LeftFootN, RightFootN, PlateLN, PlateRN, BarOtherN, BarLegN;
            public Vector3 CopL, CopR, Cop, Com, BarPos, GripRawL, GripRawR;
            public int FootPtsL, FootPtsR;
            public Vector2 PatchMinL, PatchMaxL, PatchMinR, PatchMaxR;
            public float CopMargin;
            public float MaxBodySpeed, Drift, MaxErrDeg, MaxDemand, SepL, SepR;
            public string MaxDemandJoint;
        }

        private readonly List<Tick> _ticks = new List<Tick>();
        private DeadliftFootCopSensor _copL, _copR;
        private DeadliftBarContactSensor _plates;
        private DeadliftSetupAcceptance _accepted;
        private bool _gravityReference;
        private bool _ankleRoll;

        private IEnumerator Prepare(bool attachLeft, bool attachRight, bool gravityReference = true, System.Func<string, bool> keepReference = null)
        {
            _ticks.Clear();
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            var feasibility = new DeadliftSetupFeasibilitySolver(_world.Rig);
            DeadliftSetupSpec spec = DeadliftSetupFeasibilityTests.SumoPronated();
            spec.RequiredJointMarginDeg = 3f;
            DeadliftPlantModel model = feasibility.Measurements.BuildModel(QualifiedB2, spec);
            _accepted = DeadliftSetupAcceptance.Check(_world.Rig, _world.Bar, feasibility.Measurements, model);
            Assert.That(_accepted.Passed, Is.True, "the recorded initial condition no longer passes the independent A2 table\n" + _accepted.Table());
            _world.UseModel(model);
            _world.PlaceAtSetup(BarKg, attachLeft, attachRight, ignoreBarVsGraspingLimbs: true);
            if (_ankleRoll)
                PowerliftingSimulator.Deadlift.Unity.DeadliftAnkleRoll.Enable(_world.Rig);   // GAM-70 deadlift-local DOF, before tick 0
            if (gravityReference)
            {
                // Static gravity reference from the placed initial condition, then re-prime (still before tick 0).
                _world.Hold.ComputeStaticGravityBias(_world.Rig);
                if (keepReference != null)
                    _world.Hold.RetainStaticGravityBias(keepReference);
                _world.Rig.PrimeCommandSource();
            }
            _gravityReference = gravityReference;

            Collider platform = _world.Rig.PlatformCollider;
            _copL = _world.Rig.Segments["left_foot"].Body.gameObject.AddComponent<DeadliftFootCopSensor>();
            _copR = _world.Rig.Segments["right_foot"].Body.gameObject.AddComponent<DeadliftFootCopSensor>();
            _copL.Ground = _copR.Ground = platform;
            Rigidbody bar = _world.Bar.Body;
            _plates = bar.gameObject.GetComponent<DeadliftBarContactSensor>() ?? bar.gameObject.AddComponent<DeadliftBarContactSensor>();
            _plates.Ground = platform;
            _plates.LeftPlate = bar.transform.Find("LeftPlateAggregateCollider")?.GetComponent<MeshCollider>();
            _plates.RightPlate = bar.transform.Find("RightPlateAggregateCollider")?.GetComponent<MeshCollider>();
            _world.AfterSample += Record;
        }

        private void Record(SimulationTime time, PhysicalObservation observation, PlayerIntentFrame intent)
        {
            double dt = SimulationConstants.FixedDeltaTimeSeconds;
            _copL.CompleteTick();
            _copR.CompleteTick();
            _plates.CompleteTick();
            PlantSample s = _world.Samples.Count > 0 ? _world.Samples[_world.Samples.Count - 1] : default;
            float maxSpeed = _world.Rig.Segments.Values.Max(x => x.Body.linearVelocity.magnitude);
            _ticks.Add(new Tick
            {
                CopMargin = CopMargin(),
                LeftFootN = _copL.NormalImpulse / dt, RightFootN = _copR.NormalImpulse / dt,
                PlateLN = _plates.LeftNormalImpulse / dt, PlateRN = _plates.RightNormalImpulse / dt, BarOtherN = _plates.OtherImpulse.y / dt,
                BarLegN = _world.Sensors["bar"].NormalForce(BenchSurface.AthleteLeg) + _world.Sensors["bar"].NormalForce(BenchSurface.AthleteFoot),
                Cop = CombinedCop(_copL.NormalImpulse, _copL.Cop, _copR.NormalImpulse, _copR.Cop),
                CopL = _copL.Cop, CopR = _copR.Cop, Com = _world.WholeBodyCom(), BarPos = _world.Bar.Body.position,
                FootPtsL = _copL.Points, FootPtsR = _copR.Points,
                PatchMinL = _copL.PatchMin, PatchMaxL = _copL.PatchMax, PatchMinR = _copR.PatchMin, PatchMaxR = _copR.PatchMax,
                MaxBodySpeed = maxSpeed, Drift = s.MaxBodyDriftM, MaxErrDeg = s.MaxJointErrorDeg, MaxDemand = s.MaxDemand, MaxDemandJoint = s.MaxDemandJoint,
                GripRawL = s.LeftGripForceRaw, GripRawR = s.RightGripForceRaw, SepL = s.LeftGripSeparationM, SepR = s.RightGripSeparationM
            });
        }

        private static double Mean(IEnumerable<Tick> t, System.Func<Tick, double> f) => t.Average(f);
        private static Vector3 MeanV(IEnumerable<Tick> t, System.Func<Tick, Vector3> f)
        {
            Vector3 sum = Vector3.zero; int n = 0;
            foreach (Tick x in t) { sum += f(x); n++; }
            return n == 0 ? new Vector3(float.NaN, float.NaN, float.NaN) : sum / n;
        }

        /// <summary>Common measured summary of a run segment.</summary>
        private string Summary(string label, List<Tick> seg, out Metrics m)
        {
            float g = Physics.gravity.magnitude;
            float wa = _world.Rig.TotalMassKg * g, wb = _world.Bar.Body.mass * g;
            m = new Metrics();
            m.Feet = Mean(seg, x => x.LeftFootN + x.RightFootN);
            m.FeetL = Mean(seg, x => x.LeftFootN); m.FeetR = Mean(seg, x => x.RightFootN);
            m.Plates = Mean(seg, x => x.PlateLN + x.PlateRN);
            m.PlateL = Mean(seg, x => x.PlateLN); m.PlateR = Mean(seg, x => x.PlateRN);
            m.BarOther = Mean(seg, x => x.BarOtherN);
            m.BarLegMax = seg.Max(x => x.BarLegN);
            Vector3 l = MeanV(seg, x => x.GripRawL), r = MeanV(seg, x => x.GripRawR);
            m.GripL = l; m.GripR = r;
            m.BarDispMax = _ticks.Max(x => Vector3.Distance(x.BarPos, _ticks[0].BarPos));
            m.BarSpeedTail = seg.Zip(seg.Skip(1), (a, b) => Vector3.Distance(a.BarPos, b.BarPos) / (float)SimulationConstants.FixedDeltaTimeSeconds).DefaultIfEmpty(0f).Max();
            m.DriftMax = _ticks.Max(x => x.Drift); m.DriftFinal = _ticks[_ticks.Count - 1].Drift;
            m.BodySpeedTail = seg.Max(x => x.MaxBodySpeed);
            m.ErrTail = seg.Max(x => x.MaxErrDeg);
            Tick worst = seg.OrderByDescending(x => x.MaxDemand).First();
            m.DemandTail = worst.MaxDemand; m.DemandJoint = worst.MaxDemandJoint;
            m.SepMax = seg.Max(x => Mathf.Max(float.IsNaN(x.SepL) ? 0f : x.SepL, float.IsNaN(x.SepR) ? 0f : x.SepR));
            Vector3 com = MeanV(seg, x => x.Com);
            Vector3 cop = MeanV(seg.Where(x => !float.IsNaN(x.Cop.x)), x => x.Cop);
            Tick last = seg[seg.Count - 1];
            m.CopInPatch = seg.Where(x => !float.IsNaN(x.CopMargin)).Select(x => x.CopMargin).DefaultIfEmpty(float.NaN).Min();
            m.Com = com; m.Cop = cop;
            Vector3 handsOnAthlete = l + r;            // measured contract: raw = force on the hand
            m.AthleteResidual = (float)(m.Feet + handsOnAthlete.y - wa);   // vertical, athlete quasi-static
            m.BarResidual = (float)(m.Plates + m.BarOther - handsOnAthlete.y - wb); // bar: plates + (-raw) - m g
            var b = new StringBuilder();
            b.AppendLine($"--- {label} (ticks {seg.Count}, dt {SimulationConstants.FixedDeltaTimeSeconds:F3} s) ---");
            b.AppendLine(string.Format(C, "feet  L {0:F1} R {1:F1} sum {2:F1} N | athlete weight {3:F1} N | foot contact points L {4} R {5}", m.FeetL, m.FeetR, m.Feet, wa, last.FootPtsL, last.FootPtsR));
            b.AppendLine(string.Format(C, "plates L {0:F1} R {1:F1} sum {2:F1} N | other bar-ground {3:F1} N | bar weight {4:F1} N | bar vs leg/foot contact max {5:F1} N", m.PlateL, m.PlateR, m.Plates, m.BarOther, wb, m.BarLegMax));
            b.AppendLine(string.Format(C, "grip raw (force on hand) L {0} R {1} N | anchor separation max {2:F3} mm", l.ToString("F1"), r.ToString("F1"), m.SepMax * 1000f));
            b.AppendLine(string.Format(C, "vertical balance: athlete feet + hands - W_a = {0:F1} N | bar plates + other - hands - W_b = {1:F1} N", m.AthleteResidual, m.BarResidual));
            b.AppendLine(string.Format(C, "COM {0} | realized COP {1} | COP margin inside realized contact patch {2:F1} mm", com.ToString("F4"), cop.ToString("F4"), m.CopInPatch * 1000f));
            b.AppendLine(string.Format(C, "bar displacement max {0:F3} mm | bar speed tail max {1:F3} mm/s | body drift max {2:F1} mm final {3:F1} mm | body speed tail max {4:F2} mm/s",
                m.BarDispMax * 1000f, m.BarSpeedTail * 1000f, m.DriftMax * 1000f, m.DriftFinal * 1000f, m.BodySpeedTail * 1000f));
            b.AppendLine(string.Format(C, "joint tracking error tail max {0:F2} deg | modeled demand tail max {1:F3} ({2}) | saturation threshold {3:F2}", m.ErrTail, m.DemandTail, m.DemandJoint, PoweredJointController.ModeledDemandSaturationThreshold));
            return b.ToString();
        }

        private string Joints()
        {
            var b = new StringBuilder($"joint           err deg  demand  limit-prox  static-ref deg (tau_g N m) [ref {(_gravityReference ? "on" : "off")}]  solver torque (joint space, N m)\n");
            foreach (PoweredJointController.PoweredJointRuntime j in _world.Rig.PoweredController.Joints)
                if (j.HasPostPhysicsDiagnostic)
                    b.AppendLine(string.Format(C, "{0,-15} {1,7:F2}  {2,6:F3}  {3,9:F3}  {5,6:F2} ({6,6:F1})  {4}", j.Id, j.PostPhysicsDiagnostic.ErrorRad.magnitude * Mathf.Rad2Deg,
                        j.PostPhysicsDiagnostic.ModeledDemand, j.PostPhysicsDiagnostic.LimitProximity, j.PostPhysicsDiagnostic.SolverTorqueJointSpaceNm.ToString("F1"),
                        _world.Hold.BiasDegrees(j.Id), _world.Hold.BiasTorqueNm(j.Id)));
            return b.ToString();
        }

        private sealed class Metrics
        {
            public double Feet, FeetL, FeetR, Plates, PlateL, PlateR, BarOther, BarLegMax;
            public Vector3 GripL, GripR, Com, Cop;
            public float BarDispMax, BarSpeedTail, DriftMax, DriftFinal, BodySpeedTail, ErrTail, DemandTail, SepMax, CopInPatch, AthleteResidual, BarResidual;
            public string DemandJoint;
        }

        private string Header()
        {
            var b = new StringBuilder();
            b.AppendLine(_world.Model.Describe());
            b.AppendLine($"tick-0 max penetration vs platform/bar {_world.TickZeroMaxPenetrationM * 1000f:F2} mm; non-ignored overlaps: {(_world.TickZeroSelfPenetrations.Count == 0 ? "none" : string.Join("; ", _world.TickZeroSelfPenetrations))}");
            return b.ToString();
        }

        // ------------------------------------------------------------------ A2 dynamic

        /// <summary>
        /// A2 dynamic: the accepted setup, hands on the shaft but not gripping
        /// (contact mode A: hands are ignored by the bar as in the declared grasp
        /// approximation and carry nothing), held only by the modeled actuators.
        /// Acceptance: no drive above the existing saturation contract
        /// (PoweredJointController.ModeledDemandSaturationThreshold), feet carry
        /// the athlete weight within 2 %, the plates carry the bar weight within
        /// 1 % (A1 closes to 0.02 %), the bar does not move (&lt; 1 mm), the body
        /// is static at the end (tail body speed &lt; 10 mm/s), and the realized COP
        /// is inside the realized contact patch.
        /// </summary>
        [Explicit("GAM-70 A2 dynamic acceptance FAILS (2026-11-12): ungripped fixed-reference hold fails; gripped stability and causal mechanism require independent qualification. Evidence Logs/GAM70/gateA2-dyn-*.txt")]
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_DYN_ACCEPTED_SETUP_IS_HELD_BY_THE_MODELED_ACTUATORS_ON_THE_REAL_PLANT()
        {
            yield return Prepare(false, false);
            var report = new StringBuilder(Header());
            _world.Step(30);
            report.Append("joint snapshot at tick 30 (static gravity reference ON):\n" + Joints());
            _world.Step(Ticks - 30);
            List<Tick> tail = _ticks.Skip(Ticks - Tail).ToList();
            report.Append(Summary("A2 hold, hands on shaft, no grip, static gravity reference", tail, out Metrics m));
            List<Tick> preFall = _ticks.TakeWhile(x => x.Drift < 0.05f).ToList();
            if (preFall.Count > 0)
                report.AppendLine($"pre-fall window (drift < 50 mm): {preFall.Count} of {_ticks.Count} ticks, peak demand {preFall.Max(x => x.MaxDemand):F3} ({preFall.OrderByDescending(x => x.MaxDemand).First().MaxDemandJoint})");
            report.Append(Joints());
            WriteTrace("gateA2-dyn-hold-trace.csv");
            F.WriteEvidence("gateA2-dyn-hold.txt", report.ToString());
            float wa = _world.Rig.TotalMassKg * Physics.gravity.magnitude, wb = _world.Bar.Body.mass * Physics.gravity.magnitude;
            Assert.That(m.DemandTail, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), "a drive saturates holding the setup\n" + report);
            Assert.That(m.Feet, Is.EqualTo(wa).Within(0.02 * wa), report.ToString());
            Assert.That(m.Plates, Is.EqualTo(wb).Within(0.01 * wb), report.ToString());
            Assert.That(m.BarDispMax, Is.LessThan(0.001f), report.ToString());
            Assert.That(m.BodySpeedTail, Is.LessThan(0.010f), "the athlete is not static at the end of the hold\n" + report);
            Assert.That(m.CopInPatch, Is.GreaterThan(0f), "realized COP outside the realized contact patch\n" + report);
            yield return null;
        }

        /// <summary>
        /// Negative control (measured 2026-11-12, run p7): the fixed kinematic
        /// reference without the static gravity term. The drives sag by tau_g/K,
        /// the hips settle onto their flexion stop and the athlete tips backward
        /// with no drive saturated. Kept as the evidence of the cause.
        /// </summary>
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_DYN_AB_WITHOUT_THE_STATIC_GRAVITY_REFERENCE_THE_SETUP_SAGS_AND_TIPS()
        {
            yield return Prepare(false, false, gravityReference: false);
            var report = new StringBuilder(Header());
            _world.Step(30);
            report.Append("joint snapshot at tick 30 (static gravity reference OFF):\n" + Joints());
            _world.Step(Ticks - 30);
            report.Append(Summary("A2 hold, fixed kinematic reference (no gravity term)", _ticks.Skip(Ticks - Tail).ToList(), out Metrics m));
            WriteTrace("gateA2-dyn-ab-no-gravity-ref-trace.csv");
            F.WriteEvidence("gateA2-dyn-ab-no-gravity-ref.txt", report.ToString());
            Assert.That(m.DriftMax, Is.GreaterThan(0.10f), "the uncompensated reference unexpectedly held\n" + report);
            // Authority is judged before the fall: up to the tick the body has drifted 50 mm.
            List<Tick> preFall = _ticks.TakeWhile(x => x.Drift < 0.05f).ToList();
            report.AppendLine($"pre-fall window (drift < 50 mm): {preFall.Count} ticks, peak demand {preFall.Max(x => x.MaxDemand):F3} ({preFall.OrderByDescending(x => x.MaxDemand).First().MaxDemandJoint})");
            F.WriteEvidence("gateA2-dyn-ab-no-gravity-ref.txt", report.ToString());
            Assert.That(preFall.Max(x => x.MaxDemand), Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold),
                "a drive saturated before the fall: it would then be an authority limit\n" + report);
            yield return null;
        }

        /// <summary>Negative control: the same setup with every drive disabled must not stay up.</summary>
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_DYN_AB_DRIVES_DISABLED_THE_SETUP_COLLAPSES_SO_NO_HIDDEN_SUPPORT_HOLDS_IT()
        {
            yield return Prepare(false, false);
            _world.Hold.Enabled = false;
            var report = new StringBuilder(Header());
            _world.Step(Ticks);
            report.Append(Summary("A2 hold, drives disabled", _ticks.Skip(Ticks - Tail).ToList(), out Metrics m));
            F.WriteEvidence("gateA2-dyn-ab-drives-off.txt", report.ToString());
            Assert.That(m.DriftMax, Is.GreaterThan(0.10f), "the posture survived without actuators: something else supports it\n" + report);
            yield return null;
        }

        /// <summary>Negative control: stop at actual loss of foot support, without inferring a fall direction from segment drift.</summary>
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_DYN_FIXED_REFERENCE_UNGRIPPED_SETUP_LOSES_FOOT_SUPPORT()
        {
            yield return Prepare(false, false);
            bool unloaded = false;
            for (int i = 0; i < Ticks; i++)
            {
                _world.Step(1);
                Tick t = _ticks.Last();
                if (i > 2 && t.LeftFootN + t.RightFootN < 1d)
                {
                    unloaded = true;
                    break;
                }
            }
            Tick last = _ticks.Last();
            var report = new StringBuilder(Header());
            report.AppendLine($"first bilateral foot unloading at completed tick {_ticks.Count}; COM {last.Com}; COP {last.Cop}; feet {last.LeftFootN + last.RightFootN:F3} N; plates {last.PlateLN + last.PlateRN:F3} N");
            report.Append(Joints());
            WriteTrace("gateA2-dyn-foot-support-loss.csv");
            F.WriteEvidence("gateA2-dyn-foot-support-loss.txt", report.ToString());
            Assert.That(_world.TickZeroMaxPenetrationM, Is.LessThan(0.0005f), "initial contact invalid\n" + report);
            Assert.That(unloaded, Is.True, "ungripped fixed reference did not lose foot support within the observation horizon\n" + report);
            yield return null;
        }

        /// <summary>Repeatability: a second fresh plant from the same initial condition reproduces the same (failing) hold exactly.</summary>
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A2_DYN_REPEAT_A_FRESH_PLANT_REPRODUCES_THE_HOLD()
        {
            var runs = new List<Metrics>();
            var report = new StringBuilder();
            for (int run = 0; run < 2; run++)
            {
                yield return Prepare(false, false);
                _world.Step(Ticks);
                report.Append(Summary($"repeat run {run}", _ticks.Skip(Ticks - Tail).ToList(), out Metrics m));
                runs.Add(m);
                yield return _world.Teardown();
                _world = null;
                _ticks.Clear();
            }
            F.WriteEvidence("gateA2-dyn-repeat.txt", report.ToString());
            Assert.That(runs[1].DriftFinal, Is.EqualTo(runs[0].DriftFinal).Within(0.0005f), report.ToString());
            Assert.That(runs[1].Feet, Is.EqualTo(runs[0].Feet).Within(5.0), report.ToString());
            yield return null;
        }

        // ------------------------------------------------------------------ A3 grounded grip

        private float RadialAnchorOffset(BenchGrip grip, string side)
        {
            // Independent of BenchGrip.Attach (which takes its bar point from the
            // hand): the hand's grip anchor expressed in the bar frame, distance
            // from the shaft axis (bar local X).
            Rigidbody hand = _world.Rig.Segments[side + "_hand"].Body;
            Vector3 p = _world.Bar.Body.transform.InverseTransformPoint(hand.transform.TransformPoint(grip.HandAnchorLocal));
            return new Vector2(p.y, p.z).magnitude;
        }

        private Dictionary<string, Quaternion> Pretension(float hipExtensionDeg, float kneeExtensionDeg)
        {
            var t = new Dictionary<string, Quaternion>(_world.Model.SetupLogical, System.StringComparer.Ordinal);
            foreach (string side in new[] { "left", "right" })
            {
                // Twist about the joint X axis; hip flexion is negative twist, knee flexion positive.
                t[side + "_thigh"] = t[side + "_thigh"] * Quaternion.AngleAxis(hipExtensionDeg, Vector3.right);
                t[side + "_shank"] = t[side + "_shank"] * Quaternion.AngleAxis(-kneeExtensionDeg, Vector3.right);
            }
            return t;
        }

        /// <summary>
        /// A3 grounded bilateral grip: both physical hands locked to the resting
        /// bar at the accepted setup; then a leg-drive pretension command (hip and
        /// knee extension reference offsets). Reports the full load path: ground
        /// to feet, ground to plates, bar to hands, joint demand and tracking.
        /// </summary>
        [Explicit("GAM-70 A3 grounded acceptance is BLOCKED by the A2 dynamic failure: the gripped athlete is not static, so the pretension load path cannot close. Evidence Logs/GAM70/gateA3-grounded*.txt")]
        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A3_GROUNDED_BILATERAL_GRIP_AND_PRETENSION_LOAD_PATH()
        {
            yield return Prepare(true, true);
            var report = new StringBuilder(Header());
            float radialL = RadialAnchorOffset(_world.LeftGrip, "left"), radialR = RadialAnchorOffset(_world.RightGrip, "right");
            int barJoints = Object.FindObjectsByType<Joint>(FindObjectsSortMode.None).Count(j => j.connectedBody == _world.Bar.Body || j.gameObject == _world.Bar.Body.gameObject);
            report.AppendLine(string.Format(C, "grip anchors vs shaft axis at attach (bar frame, independent of Attach): L {0:F3} mm R {1:F3} mm; joints on the bar {2}", radialL * 1000f, radialR * 1000f, barJoints));
            _world.Step(300);
            report.Append(Summary("A3 neutral hold, bilateral grip, bar grounded", _ticks.Skip(100).Take(200).ToList(), out Metrics neutral));
            report.Append(Joints());
            // Pretension: switch the hold reference; the bar is still grounded.
            var pre = new DeadliftSetupHold(Pretension(6f, 6f));
            pre.CopyStaticGravityBias(_world.Hold);
            _world.Rig.SetCommandSource(pre);
            _world.Step(300);
            report.Append(Summary("A3 pretension (+6 deg hip and knee extension reference)", _ticks.Skip(400).Take(200).ToList(), out Metrics tens));
            report.Append(Joints());
            float g = Physics.gravity.magnitude;
            report.AppendLine(string.Format(C, "pretension effect: hand pull {0:F1} -> {1:F1} N, plates {2:F1} -> {3:F1} N, feet {4:F1} -> {5:F1} N, bar displacement max {6:F3} mm",
                -(neutral.GripL.y + neutral.GripR.y), -(tens.GripL.y + tens.GripR.y), neutral.Plates, tens.Plates, neutral.Feet, tens.Feet, tens.BarDispMax * 1000f));
            WriteTrace("gateA3-grounded-trace.csv");
            F.WriteEvidence("gateA3-grounded.txt", report.ToString());
            float wb = _world.Bar.Body.mass * g;
            Assert.That(barJoints, Is.EqualTo(2), report.ToString());
            Assert.That(Mathf.Max(radialL, radialR), Is.LessThan(0.002f), "a grip anchor is not on the shaft axis\n" + report);
            Assert.That(neutral.SepMax, Is.LessThan(0.001f), "grip constraint drifts\n" + report);
            Assert.That(Mathf.Abs(neutral.BarResidual), Is.LessThan(0.02f * wb), "bar vertical free body does not close\n" + report);
            Assert.That(Mathf.Abs(tens.BarResidual), Is.LessThan(0.02f * wb), "bar vertical free body does not close under pretension\n" + report);
            Assert.That(-(tens.GripL.y + tens.GripR.y), Is.GreaterThan(-(neutral.GripL.y + neutral.GripR.y) + 20f), "pretension did not increase the hand pull\n" + report);
            Assert.That(tens.Plates, Is.LessThan(neutral.Plates - 20f), "pretension did not unload the plates\n" + report);
            Assert.That(tens.DemandTail, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), report.ToString());
            yield return null;
        }

        /// <summary>Controls on the grounded bar: no grip, single grip, grips with drives disabled, and an infeasible elbow command.</summary>
        [Timeout(1800000)]
        [UnityTest]
        public IEnumerator A3_GROUNDED_CONTROLS_NO_GRIP_SINGLE_GRIP_DRIVES_OFF_AND_INFEASIBLE_COMMAND()
        {
            var report = new StringBuilder();
            var cases = new (string name, bool l, bool r, bool drivesOff, bool elbow)[]
            {
                ("no grip, pretension command", false, false, false, false),
                ("left grip only, pretension command", true, false, false, false),
                ("bilateral grip, drives disabled", true, true, true, false),
                ("bilateral grip, infeasible 80 deg elbow flexion command", true, true, false, true)
            };
            var results = new Dictionary<string, Metrics>();
            foreach (var c in cases)
            {
                yield return Prepare(c.l, c.r);
                if (c.drivesOff) _world.Hold.Enabled = false;
                Dictionary<string, Quaternion> targets = Pretension(c.drivesOff || c.elbow ? 0f : 6f, c.drivesOff || c.elbow ? 0f : 6f);
                if (c.elbow)
                    foreach (string side in new[] { "left", "right" })
                        targets[side + "_forearm"] = Quaternion.AngleAxis(80f, Vector3.right);
                if (!c.drivesOff)
                {
                    var swapped = new DeadliftSetupHold(targets);
                    swapped.CopyStaticGravityBias(_world.Hold);
                    _world.Rig.SetCommandSource(swapped);
                }
                _world.Step(400);
                report.Append(Summary(c.name, _ticks.Skip(200).ToList(), out Metrics m));
                if (c.elbow) report.Append(Joints());
                results[c.name] = m;
                yield return _world.Teardown();
                _world = null;
                _ticks.Clear();
            }
            F.WriteEvidence("gateA3-grounded-controls.txt", report.ToString());
            float wb = 75f * Physics.gravity.magnitude;
            Metrics none = results[cases[0].name], one = results[cases[1].name], off = results[cases[2].name], elbow = results[cases[3].name];
            Assert.That(none.Plates, Is.EqualTo(wb).Within(0.01f * wb), "without grips the plates must carry the whole bar\n" + report);
            Assert.That(none.GripL.magnitude + none.GripR.magnitude, Is.EqualTo(0f), report.ToString());
            Assert.That(one.GripR.magnitude, Is.EqualTo(0f), report.ToString());
            Assert.That(off.DriftMax, Is.GreaterThan(0.05f), "with drives disabled the gripped athlete must not hold the setup\n" + report);
            Assert.That(elbow.DemandTail, Is.GreaterThan(0.5f), "an infeasible elbow command must show up as demand\n" + report);
            yield return null;
        }

        private void WriteTrace(string name)
        {
            var csv = new StringBuilder("tick,leftFootN,rightFootN,plateLN,plateRN,barOtherN,barLegN,comZ,copLz,copRz,barY,barZ,maxBodySpeed,drift,maxErrDeg,maxDemand,gripLy,gripRy\n");
            for (int i = 0; i < _ticks.Count; i++)
            {
                Tick t = _ticks[i];
                csv.AppendLine(string.Join(",", new[] { i, t.LeftFootN, t.RightFootN, t.PlateLN, t.PlateRN, t.BarOtherN, t.BarLegN, t.Com.z, t.CopL.z, t.CopR.z, t.BarPos.y, t.BarPos.z, t.MaxBodySpeed, t.Drift, t.MaxErrDeg, t.MaxDemand, t.GripRawL.y, t.GripRawR.y }
                    .Select(v => System.Convert.ToDouble(v).ToString("G6", C))));
            }
            F.WriteEvidence(name, csv.ToString());
        }
    }
}
