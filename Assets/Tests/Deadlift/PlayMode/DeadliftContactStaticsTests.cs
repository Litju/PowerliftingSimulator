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
    /// GAM-70 C4 (contact-consistent inverse statics at exact B2) and C5 (the
    /// resulting fixed reference executed on fresh real Unity plants). The bar
    /// stays dynamic; no pull is requested; DeadliftSetupHold through
    /// PoweredJointController is the only joint writer; the original static
    /// gravity reference is NOT used in C5 (it remains the C1 control A).
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private DeadliftContactStatics _statics;

        private DeadliftContactStatics SolveStatics(float handPullN)
        {
            Vector3 la = _world.Rig.Segments["left_hand"].Body.transform.TransformPoint(_world.LeftGrip != null ? _world.LeftGrip.HandAnchorLocal : _world.Model.LeftGripAnchorLocal);
            Vector3 ra = _world.Rig.Segments["right_hand"].Body.transform.TransformPoint(_world.RightGrip != null ? _world.RightGrip.HandAnchorLocal : _world.Model.RightGripAnchorLocal);
            return DeadliftContactStatics.Solve(_world.Rig, _world.Bar.Body, _plates.LeftPlate, _plates.RightPlate, la, ra, handPullN);
        }

        private void InstallContactReference(float handPullN, StringBuilder report, bool left = false)
        {
            _world.Hold.ComposeReferenceOnLeft = left;
            _statics = SolveStatics(handPullN);
            report?.AppendLine(_statics.Describe());
            _world.Hold.SetStaticReference(_world.Rig, _statics.DriveTorquesAxes());
            _world.Rig.PrimeCommandSource();
        }

        /// <summary>C4: feasibility of the declared contact wrench set over hand pull H (A3 pretension range). Diagnostic.</summary>
        [UnityTest, Timeout(600000)]
        public IEnumerator C4_ContactConsistentStaticsAtB2()
        {
            yield return Prepare(true, true, false);
            var report = new StringBuilder();
            float wb = _world.Bar.Body.mass * -Physics.gravity.y;
            foreach (float frac in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
                report.AppendLine(SolveStatics(frac * wb * 0.5f).Describe());
            F.WriteEvidence("c4-statics.txt", report.ToString());
            DeadliftContactStatics h0 = SolveStatics(0f);
            Assert.That(h0.Feasible, Is.True, "B2 admits no contact-consistent static equilibrium under the declared wrench set\n" + report);
        }

        /// <summary>C5 negative control: the C4 H=0 reference alone on two fresh plants realizes the C4 wrenches, then the identified unstable mode leaves the grounded contact mode.</summary>
        [UnityTest, Timeout(1800000)]
        public IEnumerator C5_ContactConsistentReferenceOnTheRealPlant()
        {
            var results = new Dictionary<string, WrenchRun>();
            var report = new StringBuilder();
            yield return RunWrenchCase("C5-H0", false, null, 600, results, () => InstallContactReference(0f, report));
            yield return RunWrenchCase("C5-H0-repeat", false, null, 600, results, () => InstallContactReference(0f, null));
            F.WriteEvidence("c5-reference.txt", report.ToString());
            string summary = WrenchSummary(results);
            F.WriteEvidence("wrench-c5-summary.csv", summary);
            // Regression control for the regulated A2 hold: the same contact-consistent
            // reference WITHOUT the balance regulator realizes the C4 wrenches at tick 5
            // but is dynamically unstable and leaves the grounded contact mode.
            foreach (var r in results)
            {
                Assert.That(r.Value.PlateAtTick5, Is.EqualTo(75f * 9.81f).Within(15f), r.Key + ": C4 plate reaction not realized\n" + summary);
                Assert.That(r.Value.Event, Is.EqualTo("plate-support-lost-bar-rising"), r.Key + ": the unregulated reference no longer exhibits the identified unstable mode\n" + summary);
            }
        }

        /// <summary>C5 closed-chain A/B: the same C4 H=0 reference with no grips (feet-only contact mode). Diagnostic.</summary>
        [UnityTest, Timeout(900000), Explicit("GAM-70 C5 closed-chain discrimination; run on demand")]
        public IEnumerator C5_ClosedChainAB_SameReferenceWithoutGrips()
        {
            var results = new Dictionary<string, WrenchRun>();
            yield return RunWrenchCase("C5-H0-nogrip", false, null, 600, results, () => InstallContactReference(0f, null), grips: false);
            yield return RunWrenchCase("C5-H0-grip", false, null, 600, results, () => InstallContactReference(0f, null));
            F.WriteEvidence("wrench-c5ab-summary.csv", WrenchSummary(results));
        }

        /// <summary>C5 drive-frame A/B: identical C4 torques composed right (q* b) vs left (b q*). Diagnostic.</summary>
        [UnityTest, Timeout(1200000), Explicit("GAM-70 C5 reference composition A/B; run on demand")]
        public IEnumerator C5_ReferenceCompositionAB()
        {
            var results = new Dictionary<string, WrenchRun>();
            yield return RunWrenchCase("C5-H0-left", false, null, 600, results, () => InstallContactReference(0f, null, left: true));
            yield return RunWrenchCase("C5-H0-left-nogrip", false, null, 600, results, () => InstallContactReference(0f, null, left: true), grips: false);
            yield return RunWrenchCase("C5-H0-left-repeat", false, null, 600, results, () => InstallContactReference(0f, null, left: true));
            F.WriteEvidence("wrench-c5lr-summary.csv", WrenchSummary(results));
        }

        /// <summary>
        /// C5 plant identification (prompt step 9): fresh plants with the C4 H=0
        /// reference plus a signed +/-1 deg ankle hinge target offset applied
        /// from tick 1 (bilateral, left only, right only), compared with the
        /// unperturbed baseline. Diagnostic; no feedback.
        /// </summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 C5 ankle plant identification; run on demand")]
        public IEnumerator C5_AnkleTargetIdentification()
        {
            var results = new Dictionary<string, WrenchRun>();
            var cases = new (string name, float l, float r)[] { ("id-0", 0f, 0f), ("id-both+1", 1f, 1f), ("id-both-1", -1f, -1f), ("id-left+1", 1f, 0f), ("id-right+1", 0f, 1f) };
            foreach (var c in cases)
            {
                var cc = c;
                yield return RunWrenchCase(c.name, false, null, 60, results, () =>
                {
                    _statics = SolveStatics(0f);
                    var t = _statics.DriveTorquesAxes();
                    // A target offset of d deg on the hinge axis equals an extra K d of reference torque.
                    float k = PowerliftingSimulator.Athlete.PoweredJointController.FamilyProfiles.First(p => p.Id == "ankle").Spring * Mathf.Deg2Rad;
                    t["left_foot"] += new Vector3(cc.l * k, 0f, 0f);
                    t["right_foot"] += new Vector3(cc.r * k, 0f, 0f);
                    _world.Hold.SetStaticReference(_world.Rig, t);
                    _world.Rig.PrimeCommandSource();
                });
            }
            F.WriteEvidence("wrench-c5id-summary.csv", WrenchSummary(results));
        }

        private DeadliftBalanceRegulator _regulator;
        private bool _regulatorComLoop = true;
        private float _regulatorKi = 3f;

        private void InstallRegulatedReference()
        {
            InstallContactReference(0f, null);
            _regulator = new DeadliftBalanceRegulator(_world.Rig, _world.Rig.PlatformCollider) { ComFeedback = _regulatorComLoop, KiDegPerMPerTick = _regulatorKi, RollLoop = _ankleRoll };
            _world.Hold.Regulator = _regulator;
            _world.Rig.PrimeCommandSource();
        }

        /// <summary>C5 with the identified bilateral ankle COP/COM regulator on fresh gripped plants (+ repeat). Diagnostic run.</summary>
        [UnityTest, Timeout(1800000), Explicit("GAM-70 C5 regulated hold; run on demand")]
        public IEnumerator C5_RegulatedHold()
        {
            var results = new Dictionary<string, WrenchRun>();
            yield return RunWrenchCase("C5-reg", false, null, 1000, results, InstallRegulatedReference);
            yield return RunWrenchCase("C5-reg-repeat", false, null, 1000, results, InstallRegulatedReference);
            F.WriteEvidence("wrench-c5reg-summary.csv", WrenchSummary(results));
        }
    }
}
