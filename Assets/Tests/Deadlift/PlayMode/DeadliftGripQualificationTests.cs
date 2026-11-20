using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate A3: can the qualified bench grip constraint carry the
    /// deadlift bar, and what does it measure? The floor setup is not
    /// reachable by the canonical athlete (Gate A2), so the constraint is
    /// characterized in the nearest reachable configuration: the athlete
    /// standing with the 75 kg bar carried in both hands at 0.90 m, the bar
    /// weight reaching the platform only through the grips, arms, trunk and
    /// feet. The grip is a locked 6-DOF constraint, not a physiological
    /// friction grasp; these numbers describe the constraint.
    ///
    /// Joint.currentForce is read with the engine-specific contract measured
    /// by BenchJointForceFrameControlTests on 6000.3.22f1: world frame, force
    /// on the joint's own body (the hand), so the force on the bar is minus it.
    /// </summary>
    public sealed class DeadliftGripQualificationTests
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private const float G = 9.81f;
        private const float BarKg = 75f;
        private static DeadliftPlantModel s_model;
        private DeadliftPlantWorld _world;

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (_world != null)
            {
                yield return _world.Teardown();
                _world = null;
            }
        }

        internal static DeadliftSetupSpec CarrySpec() => new DeadliftSetupSpec
        {
            Label = "standing, 75 kg bar carried in both hands at 0.90 m",
            BarAxisY = 0.90f, BarZ = 0.12f, MaxKneeFlexionDeg = 20f, MaxHipFlexionDeg = 30f, StanceHalfWidth = 0.12f, ToeOutDeg = 10f, GripHalfWidth = 0.30f,
            UseBalance = true, BarMassKgInBalance = BarKg, UseTorsoAngle = false, UseNeutralSpine = true, UseArmPriors = false,
            RequireKnuckleLineAlongShaft = false, UseClearances = true, LimitMarginDeg = 4f, RandomStarts = 20
        };

        private IEnumerator Prepare(bool attachGrips)
        {
            yield return DeadliftPlantWorld.Load();
            _world = DeadliftPlantWorld.Current;
            if (s_model == null)
                s_model = _world.SolveSetup(CarrySpec());
            else
                _world.UseModel(s_model);
            _world.PlaceAtSetup(BarKg, attachGrips);
        }

        private static Vector3 MeanV(IEnumerable<PlantSample> w, System.Func<PlantSample, Vector3> f)
        {
            Vector3 sum = Vector3.zero;
            int n = 0;
            foreach (PlantSample s in w) { sum += f(s); n++; }
            return n == 0 ? Vector3.zero : sum / n;
        }

        private static string Header(DeadliftPlantWorld w)
        {
            var b = new StringBuilder();
            b.AppendLine(w.Model.Describe());
            b.AppendLine($"tick-0 max penetration vs platform/bar {w.TickZeroMaxPenetrationM * 1000f:F2} mm; grip anchor separation at attach {w.TickZeroMaxGripSeparationM * 1000f:F3} mm; non-ignored overlaps: {(w.TickZeroSelfPenetrations.Count == 0 ? "none" : string.Join("; ", w.TickZeroSelfPenetrations))}");
            return b.ToString();
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A3_BASELINE_TWO_GRIPS_CARRY_THE_BAR_WEIGHT_THROUGH_THE_ARMS_TO_THE_FEET()
        {
            yield return Prepare(true);
            var report = new StringBuilder(Header(_world));
            _world.Step(300);
            List<PlantSample> s = _world.Samples;
            List<PlantSample> w = s.Skip(200).ToList();
            Rigidbody bar = _world.Bar.Body;
            float barWeight = bar.mass * G;
            float athleteWeight = _world.Rig.TotalMassKg * G;

            Vector3 left = MeanV(w, x => x.LeftGripForceRaw), right = MeanV(w, x => x.RightGripForceRaw);
            Vector3 raw = left + right;
            Vector3 acc = MeanV(w.Skip(1).Select((x, i) => new PlantSample { BarVelocity = (x.BarVelocity - w[i].BarVelocity) / (float)IsolatedBarWorld.TickSeconds }), x => x.BarVelocity);
            double fbd = (bar.mass * acc - (bar.mass * Physics.gravity - raw)).magnitude;
            double fbdRejected = (bar.mass * acc - (bar.mass * Physics.gravity + raw)).magnitude;
            double feet = w.Average(x => x.LeftFootN + x.RightFootN);
            float driftMax = w.Max(x => Mathf.Max(x.LeftGripSeparationM, x.RightGripSeparationM));
            float barSpeed = w.Max(x => x.BarSpeed);
            float tilt = w.Max(x => x.BarTiltDeg);
            float barDrop = s[0].BarPosition.y - w.Average(x => x.BarPosition.y);
            PlantSample worstDemand = w.OrderByDescending(x => x.MaxDemand).First();

            int barJoints = Object.FindObjectsByType<Joint>(FindObjectsSortMode.None).Count(j => j.connectedBody == bar || j.gameObject == bar.gameObject);
            report.AppendLine($"grip force raw (force on each hand): L {left.ToString("F1")} R {right.ToString("F1")} sum {raw.ToString("F1")} |sum| {raw.magnitude:F1} N vs bar weight {barWeight:F1} N; share L {left.magnitude / (left.magnitude + right.magnitude):P1}");
            report.AppendLine($"bar free body: |m a - (m g - sum raw)| = {fbd:F2} N under the measured contract; the rejected reading (raw as force on the bar) gives {fbdRejected:F2} N");
            report.AppendLine($"feet: {feet:F1} N vs athlete + bar weight {athleteWeight + barWeight:F1} N ({100 * (feet - athleteWeight - barWeight) / (athleteWeight + barWeight):F2} %); L {w.Average(x => x.LeftFootN):F1} R {w.Average(x => x.RightFootN):F1}");
            report.AppendLine($"hand-to-bar anchor drift max {driftMax * 1000f:F3} mm; bar speed max {barSpeed * 1000f:F2} mm/s; bar tilt max {tilt:F3} deg; bar settled {barDrop * 1000f:F2} mm below its placement");
            report.AppendLine($"worst modeled drive demand {worstDemand.MaxDemand:F3} ({worstDemand.MaxDemandJoint}); worst joint error {w.Max(x => x.MaxJointErrorDeg):F2} deg; joints touching the bar: {barJoints}");
            foreach (PoweredJointController.PoweredJointRuntime j in _world.Rig.PoweredController.Joints.Where(j => j.Id.Contains("arm") || j.Id.Contains("hand")))
                if (j.HasPostPhysicsDiagnostic)
                    report.AppendLine($"  {j.Id,-16} error {j.PostPhysicsDiagnostic.ErrorRad.magnitude * Mathf.Rad2Deg,6:F2} deg  demand {j.PostPhysicsDiagnostic.ModeledDemand:F3}  limit proximity {j.PostPhysicsDiagnostic.LimitProximity:F3}  solver torque {j.PostPhysicsDiagnostic.SolverTorqueJointSpaceNm.ToString("F1")} N m");
            F.WriteEvidence("gateA3-baseline.txt", report.ToString());

            Assert.That(barJoints, Is.EqualTo(2), "the bar must be held by exactly the two grip constraints\n" + report);
            Assert.That(raw.y, Is.LessThan(0f), "the bar must pull the hands down\n" + report);
            Assert.That(raw.magnitude, Is.EqualTo(barWeight).Within(0.05f * barWeight), report.ToString());
            Assert.That(fbd, Is.LessThan(0.05 * barWeight), "bar free-body diagram does not close under the measured force contract\n" + report);
            Assert.That(feet, Is.EqualTo(athleteWeight + barWeight).Within(0.05f * (athleteWeight + barWeight)), report.ToString());
            Assert.That(driftMax, Is.LessThan(0.001f), "grip constraint drifts\n" + report);
            // Not asserted: the carry is not a static equilibrium (trunk drive near saturation, bar sways); it is reported above.
            Assert.That(worstDemand.MaxDemand, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), "a drive saturates while carrying 75 kg\n" + report);
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A3_AB_WITHOUT_GRIPS_THE_BAR_FREE_FALLS_SO_NOTHING_ELSE_SUPPORTS_IT()
        {
            yield return Prepare(false);
            _world.Step(25);
            List<PlantSample> s = _world.Samples;
            double acceleration = (s[19].BarVelocity.y - s[4].BarVelocity.y) / (15 * IsolatedBarWorld.TickSeconds);
            string report = $"no grips: bar acceleration over ticks 5-20 {acceleration:F3} m/s2 (g = {-G}); fell {(s[0].BarPosition.y - s[24].BarPosition.y) * 1000f:F1} mm in 25 ticks";
            F.WriteEvidence("gateA3-ab-no-grips.txt", report);
            Assert.That(acceleration, Is.EqualTo(-G).Within(0.04 * G), report);
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A3_AB_ONE_HAND_CARRIES_THE_WHOLE_BAR_WEIGHT_UNTIL_IT_TIPS()
        {
            yield return Prepare(true);
            _world.Step(100);
            Vector3 twoHandLeft = MeanV(_world.Samples.Skip(60), x => x.LeftGripForceRaw);
            _world.RightGrip.Release();
            yield return null; // deferred destroy; the render clock is disabled so no tick passes
            int released = _world.Samples.Count;
            _world.Step(40);
            List<PlantSample> after = _world.Samples.Skip(released + 1).Take(10).ToList();
            Vector3 oneHandLeft = MeanV(after, x => x.LeftGripForceRaw);
            float tilt = _world.Samples[_world.Samples.Count - 1].BarTiltDeg;
            float barWeight = BarKg * G;
            string report = string.Format(C, "left grip force two hands {0} N (|{1:F1}|) -> right released: {2} N (|{3:F1}|); bar weight {4:F1} N; bar axis tilt after 0.4 s {5:F1} deg",
                twoHandLeft.ToString("F1"), twoHandLeft.magnitude, oneHandLeft.ToString("F1"), oneHandLeft.magnitude, barWeight, tilt);
            F.WriteEvidence("gateA3-ab-one-hand.txt", report);
            Assert.That(-twoHandLeft.y, Is.EqualTo(0.5f * barWeight).Within(0.25f * barWeight), "two-hand share\n" + report);
            Assert.That(-oneHandLeft.y, Is.GreaterThan(1.15f * -twoHandLeft.y), "releasing one hand did not move its load to the other\n" + report);
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A3_AB_ARM_DRIVES_DISABLED_SHOWS_WHICH_PART_OF_THE_LOAD_PATH_IS_PASSIVE_STRUCTURE()
        {
            yield return Prepare(true);
            foreach (string id in new[] { "left_upper_arm", "right_upper_arm", "left_forearm", "right_forearm", "left_hand", "right_hand" })
                _world.Hold.SetStrengthScale(id, 0f);
            _world.Step(200);
            List<PlantSample> s = _world.Samples;
            List<PlantSample> w = s.Skip(120).ToList();
            Vector3 raw = MeanV(w, x => x.LeftGripForceRaw + x.RightGripForceRaw);
            float drop = s[0].BarPosition.y - w.Average(x => x.BarPosition.y);
            float driftMax = w.Max(x => Mathf.Max(x.LeftGripSeparationM, x.RightGripSeparationM));
            string report = $"arm drives at 0 capacity: bar dropped {drop * 1000f:F1} mm, speed {w.Max(x => x.BarSpeed) * 1000f:F2} mm/s, tilt {w.Max(x => x.BarTiltDeg):F2} deg, grip force |sum| {raw.magnitude:F1} N (weight {BarKg * G:F1}), anchor drift {driftMax * 1000f:F3} mm, worst joint error {w.Max(x => x.MaxJointErrorDeg):F1} deg, worst demand {w.Max(x => x.MaxDemand):F3}";
            F.WriteEvidence("gateA3-ab-arm-drives-off.txt", report);
            // Measured: the hold is drive-borne. With the arm drives at zero capacity the bar is not held by passive structure.
            Assert.That(drop, Is.GreaterThan(0.2f), "the bar stayed up without arm actuators: a hidden support exists: " + report);
        }

        [Timeout(900000)]
        [UnityTest]
        public IEnumerator A3_AB_A_DELIBERATELY_INVALID_ELBOW_TARGET_SHOWS_UP_AS_DEMAND_AND_DRIFT()
        {
            yield return Prepare(true);
            var targets = new Dictionary<string, Quaternion>(s_model.SetupLogical);
            targets["left_forearm"] = Quaternion.AngleAxis(80f, Vector3.right);
            targets["right_forearm"] = Quaternion.AngleAxis(80f, Vector3.right);
            _world.Rig.SetCommandSource(new DeadliftSetupHold(targets));
            _world.Step(200);
            List<PlantSample> s = _world.Samples;
            List<PlantSample> w = s.Skip(120).ToList();
            float driftMax = w.Max(x => Mathf.Max(x.LeftGripSeparationM, x.RightGripSeparationM));
            string report = $"elbow targets 80 deg while the hands are locked to a bar resting on the athlete's own geometry: anchor drift {driftMax * 1000f:F3} mm, bar speed {w.Max(x => x.BarSpeed) * 1000f:F2} mm/s, bar moved {(w.Average(x => x.BarPosition.y) - s[0].BarPosition.y) * 1000f:F1} mm, worst joint error {w.Max(x => x.MaxJointErrorDeg):F1} deg, worst demand {w.Max(x => x.MaxDemand):F3} ({w.OrderByDescending(x => x.MaxDemand).First().MaxDemandJoint})";
            F.WriteEvidence("gateA3-ab-invalid-target.txt", report);
            Assert.That(w.Max(x => x.MaxJointErrorDeg), Is.GreaterThan(5f), "an infeasible joint target should leave a measurable tracking error\n" + report);
        }
    }
}
