using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H13, sections 15 and 16.
    ///
    /// Three limit-guard semantics on identical states, and nothing else
    /// changed between them:
    ///
    ///   G0  absolute occupancy of the authored range, the current rule
    ///   G1  limit term off, the counterfactual 5H12 already ran
    ///   G2  occupancy the reference did not ask for, over the margin the
    ///       reference left
    ///
    /// G2 has to clear two bars that G0 and G1 respectively fail. It must not
    /// zero the ankle merely because the accepted reference commands a deep
    /// hip, which is what G0 does. And it must not reproduce the 25 kg
    /// rearward collapse that removing the limit term outright produces.
    ///
    /// The proximal trigger is varied independently, because reading the hip
    /// blend from the raw demand rather than the guarded command is a separate
    /// defect from the guard semantics and has to be attributable separately.
    /// </summary>
    public sealed class PostureGuardCandidateTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

        private const float ApproachPhaseRate = 0.30f;
        private const int HoldTicks = 250;
        private const int VerdictWindowTicks = 60;

        /// <summary>
        /// Phase-local target-to-COP sensitivity, 5H12 C2, qualified at two
        /// phases only and held at the deepest qualified sample below that.
        /// Provenance: GAM11-5h12-c2-target-to-cop-gain.csv.
        /// </summary>
        private static readonly float[] GainPhases = { 0.00f, 0.55f };
        private static readonly float[] GainValues = { 0.26530f, 0.38516f };
        private const float DeepestQualifiedGainPhase = 0.55f;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_rig, Is.Not.Null);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            LogAssert.ignoreFailingMessages = true;
            if (_controller != null)
            {
                // Leave the shared controller exactly as production configures
                // it, whatever a candidate set during the run.
                SquatPredictiveBalanceController bc = _controller.Adapter?.BalanceController;
                if (bc != null)
                {
                    bc.LimitSemantics = SquatPredictiveBalanceController.LimitGuardSemantics.AbsoluteOccupancy;
                    bc.ProximalTriggerSource = SquatPredictiveBalanceController.ProximalTrigger.GuardedAnkleCommand;
                    bc.TargetToCopMPerRad = SquatPredictiveBalanceController.MeasuredTargetToCopMPerRad;
                    bc.PostureGuardEnabled = true;
                }
                _controller.enabled = false;
            }
            if (_rig != null)
                _rig.enabled = false;
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _rig = null;
            _controller = null;
            yield return null;
            LogAssert.ignoreFailingMessages = false;
        }

        private enum Candidate { G0, G1, G2 }

        private struct Configuration
        {
            public Candidate Guard;
            public bool RawProximalTrigger;
            public bool PhaseGain;
            public string Tag;
        }

        private static readonly Configuration[] Configurations =
        {
            new Configuration { Guard = Candidate.G0, Tag = "G0" },
            new Configuration { Guard = Candidate.G1, Tag = "G1" },
            new Configuration { Guard = Candidate.G2, Tag = "G2" },
            new Configuration { Guard = Candidate.G2, RawProximalTrigger = true, Tag = "G2+raw" },
            new Configuration { Guard = Candidate.G2, RawProximalTrigger = true, PhaseGain = true, Tag = "G2+raw+gain" }
        };

        // ------------------------------------------------------------------
        // E1. Static deep phase, section 16.
        // ------------------------------------------------------------------

        [UnityTest]
        public IEnumerator E1_GUARD_CANDIDATE_STATIC_DEEP_PHASE()
        {
            var rows = new List<StaticRow>();
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,config,phase,guard_scale,limit_prox,unexpected_consumed," +
                           "raw_ankle_deg,applied_ankle_deg,raw_authority,guarded_authority,hip_blend," +
                           "com_ap,cop_desired,cop_measured,support_min,support_max,com_speed,contacts,held,reason");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (Configuration config in Configurations)
                {
                    foreach (float phase in new[] { 0.55f, 0.80f, 1.00f })
                        yield return HoldCandidate(load, config, phase, rows, csv);
                }
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H13 E1 LIMIT GUARD CANDIDATES AT STATIC DEEP PHASE");
            report.AppendLine("Fresh reset per sample, phase ramped at the production rate then held.");
            report.AppendLine("Verdict over the last " + VerdictWindowTicks.ToString(CultureInfo.InvariantCulture) +
                              " ticks of a " + HoldTicks.ToString(CultureInfo.InvariantCulture) + " tick hold.");
            report.AppendLine();
            report.AppendLine("  G0  absolute occupancy of the authored range (current production)");
            report.AppendLine("  G1  limit term disabled");
            report.AppendLine("  G2  unexpected occupancy over the reference's remaining margin");
            report.AppendLine("  +raw   proximal strategy triggered from the pre-guard ankle demand");
            report.AppendLine("  +gain  phase-local target-to-COP gain, held below s_q 0.55");
            report.AppendLine();
            report.AppendLine("load  config        s_q   guardScale  rawAnkDeg  appliedDeg  hipBlend  comAP     |vCOM|   contacts  held   reason");
            foreach (StaticRow r in rows)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F0}  {1,-12}  {2,4:F2}  {3,10:F4}  {4,9:F2}  {5,10:F2}  {6,8:F3}  {7,8:F4}  {8,7:F4}  {9,8}  {10,-5}  {11}",
                    r.LoadKg, r.Config, r.Phase, r.GuardScale, r.RawAnkleDeg, r.AppliedAnkleDeg,
                    r.HipBlend, r.ComAp, r.ComSpeed, r.Contacts, r.Held, r.Reason));
            }

            report.AppendLine();
            report.AppendLine(SummariseStatic(rows));

            WriteMeasurement("GAM11-5h13-e1-guard-candidates-static.txt", report.ToString());
            WriteMeasurement("GAM11-5h13-e1-guard-candidates-static.csv", csv.ToString());
            Debug.Log(report.ToString());

            Assert.That(rows.Count, Is.EqualTo(2 * Configurations.Length * 3),
                "The candidate sweep did not complete every sample.");
            yield return null;
        }

        /// <summary>
        /// The stated bar for G2, checked against the trace rather than
        /// asserted. G0's specific failure is a guard scale of zero at a phase
        /// where the reference itself is the only thing near a limit.
        /// </summary>
        private static string SummariseStatic(List<StaticRow> rows)
        {
            var text = new StringBuilder();
            foreach (string config in new[] { "G0", "G1", "G2", "G2+raw", "G2+raw+gain" })
            {
                int zeroed = 0;
                int samples = 0;
                float minScale = 1f;
                foreach (StaticRow r in rows)
                {
                    if (r.Config != config)
                        continue;
                    samples++;
                    minScale = Mathf.Min(minScale, r.GuardScale);
                    if (r.GuardScale <= 0.0001f)
                        zeroed++;
                }
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} samples {1,2}   minimum guard scale {2:F4}   samples with the ankle fully withdrawn {3}",
                    config, samples, minScale, zeroed));
            }
            return text.ToString();
        }

        private struct StaticRow
        {
            public float LoadKg;
            public string Config;
            public float Phase;
            public float GuardScale;
            public float RawAnkleDeg;
            public float AppliedAnkleDeg;
            public float HipBlend;
            public float ComAp;
            public float ComSpeed;
            public int Contacts;
            public bool Held;
            public string Reason;
        }

        private IEnumerator HoldCandidate(
            float loadKg, Configuration config, float phase, List<StaticRow> rows, StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            Apply(bc, config, 0f);

            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
            float current = 0f;
            int approach = 0;
            while (current < phase - 1e-4f && approach++ < 4000)
            {
                current = Mathf.Min(phase, current + ApproachPhaseRate * dt);
                Apply(bc, config, current);
                adapter.HoldReferencePhaseForQualification(current, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }
            adapter.HoldReferencePhaseForQualification(phase, SquatPhaseDirection.Descent, state, 0f);

            var last = new StaticRow { LoadKg = loadKg, Config = config.Tag, Phase = phase };
            string reason = "HELD";
            bool held = true;

            for (int tick = 0; tick < HoldTicks; tick++)
            {
                Apply(bc, config, phase);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);

                bool inWindow = tick >= HoldTicks - VerdictWindowTicks;
                float comAp = balance.SystemCom.z;
                float speed = new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude;

                if (inWindow)
                {
                    if (balance.SupportContactCount == 0)
                    {
                        held = false;
                        reason = "LOST_PLANTAR_CONTACT";
                    }
                    else if (comAp > balance.SupportApMax)
                    {
                        held = false;
                        reason = "COM_PAST_FRONT";
                    }
                    else if (comAp < balance.SupportApMin)
                    {
                        held = false;
                        reason = "COM_PAST_REAR";
                    }
                    else if (speed > 0.05f && held)
                    {
                        held = false;
                        reason = "DRIFTING";
                    }
                }

                if (tick % 25 != 0 && tick != HoldTicks - 1)
                    continue;

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1},{2:F2},{3:F4},{4:F4},{5:F4},{6:F3},{7:F3},{8:F4},{9:F4},{10:F4}," +
                    "{11:F5},{12:F5},{13:F5},{14:F5},{15:F5},{16:F5},{17},{18},{19}",
                    loadKg, config.Tag, phase,
                    bc.PostureGuardScale, bc.PostureLimitProximity, bc.UnexpectedMarginConsumedFraction,
                    bc.RawAnkleSagittalOffsetRad * Mathf.Rad2Deg, bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                    bc.RawAnkleAuthorityFraction, bc.AnkleAuthorityFraction, bc.HipStrategyBlend,
                    comAp, bc.CopDesiredAp, bc.CopMeasuredAp,
                    balance.SupportApMin, balance.SupportApMax, speed,
                    balance.SupportContactCount, held ? 1 : 0, reason));

                last.GuardScale = bc.PostureGuardScale;
                last.RawAnkleDeg = bc.RawAnkleSagittalOffsetRad * Mathf.Rad2Deg;
                last.AppliedAnkleDeg = bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg;
                last.HipBlend = bc.HipStrategyBlend;
                last.ComAp = comAp;
                last.ComSpeed = speed;
                last.Contacts = balance.SupportContactCount;
            }

            last.Held = held;
            last.Reason = reason;
            rows.Add(last);
            yield return null;
        }

        // ------------------------------------------------------------------
        // E2. Dynamic descent, the collapse G1 produces.
        // ------------------------------------------------------------------

        /// <summary>
        /// 5H12 F3 showed that disabling the limit term unloaded removes the
        /// forward divergence, and at 25 kg replaces it with a rearward
        /// collapse that loses plantar contact entirely. A candidate that only
        /// restored authority would reproduce the second outcome. This runs
        /// the same descent under each candidate so that outcome is measured
        /// rather than assumed.
        /// </summary>
        [UnityTest]
        public IEnumerator E2_GUARD_CANDIDATE_DYNAMIC_DESCENT()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H13 E2 LIMIT GUARD CANDIDATES ON THE DYNAMIC DESCENT");
            report.AppendLine("Identical descent at the production phase rate. Only the guard semantics,");
            report.AppendLine("the proximal trigger and the target-to-COP gain differ between rows.");
            report.AppendLine();
            report.AppendLine("load  config        deepestSq  finalPelvisY  minGuardScale  maxRawAuth  maxHipBlend  firstFrontCross  firstComOut  lostContact");

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,config,tick,sq,com_ap,com_speed,support_min,support_max,guard_scale," +
                           "limit_prox,unexpected_consumed,raw_ankle_deg,applied_ankle_deg,raw_authority," +
                           "hip_blend,pelvis_y,contacts");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (Configuration config in Configurations)
                    yield return RunDescent(load, config, report, csv);
            }

            WriteMeasurement("GAM11-5h13-e2-guard-candidates-dynamic.txt", report.ToString());
            WriteMeasurement("GAM11-5h13-e2-guard-candidates-dynamic.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private IEnumerator RunDescent(float loadKg, Configuration config, StringBuilder report, StringBuilder csv)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            Rigidbody pelvis = _rig.Segments["pelvis"].Body;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            Apply(bc, config, 0f);

            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            float sq = 0f;
            float deepest = 0f;
            float minScale = 1f;
            float maxRawAuthority = 0f;
            float maxHipBlend = 0f;
            int firstFrontCross = -1;
            int firstComOut = -1;
            bool lostContact = false;
            int tick = 0;

            while (tick < 1200)
            {
                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                Apply(bc, config, sq);
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;

                deepest = Mathf.Max(deepest, sq);
                minScale = Mathf.Min(minScale, bc.PostureGuardScale);
                maxRawAuthority = Mathf.Max(maxRawAuthority, bc.RawAnkleAuthorityFraction);
                maxHipBlend = Mathf.Max(maxHipBlend, bc.HipStrategyBlend);

                if (balance.SupportContactCount == 0)
                    lostContact = true;
                if (firstFrontCross < 0 && balance.CaptureAp > balance.SupportApMax)
                    firstFrontCross = tick;
                if (firstComOut < 0 &&
                    (balance.SystemCom.z > balance.SupportApMax || balance.SystemCom.z < balance.SupportApMin))
                    firstComOut = tick;

                if (tick % 10 == 0)
                {
                    csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0:F0},{1},{2},{3:F4},{4:F5},{5:F5},{6:F5},{7:F5},{8:F4},{9:F4},{10:F4}," +
                        "{11:F3},{12:F3},{13:F4},{14:F4},{15:F5},{16}",
                        loadKg, config.Tag, tick, sq, balance.SystemCom.z,
                        new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude,
                        balance.SupportApMin, balance.SupportApMax,
                        bc.PostureGuardScale, bc.PostureLimitProximity, bc.UnexpectedMarginConsumedFraction,
                        bc.RawAnkleSagittalOffsetRad * Mathf.Rad2Deg,
                        bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                        bc.RawAnkleAuthorityFraction, bc.HipStrategyBlend,
                        pelvis.position.y, balance.SupportContactCount));
                }

                if (sq >= 1f && tick > 500)
                    break;
            }

            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "{0,4:F0}  {1,-12}  {2,9:F2}  {3,12:F4}  {4,13:F4}  {5,10:F3}  {6,11:F3}  {7,15}  {8,11}  {9}",
                loadKg, config.Tag, deepest, pelvis.position.y, minScale,
                maxRawAuthority, maxHipBlend,
                firstFrontCross < 0 ? "never" : firstFrontCross.ToString(CultureInfo.InvariantCulture),
                firstComOut < 0 ? "never" : firstComOut.ToString(CultureInfo.InvariantCulture),
                lostContact));
            yield return null;
        }

        // ------------------------------------------------------------------

        private static void Apply(SquatPredictiveBalanceController bc, Configuration config, float sq)
        {
            bc.PostureGuardEnabled = true;
            bc.LimitSemantics = config.Guard switch
            {
                Candidate.G1 => SquatPredictiveBalanceController.LimitGuardSemantics.Disabled,
                Candidate.G2 => SquatPredictiveBalanceController.LimitGuardSemantics.UnexpectedMarginConsumption,
                _ => SquatPredictiveBalanceController.LimitGuardSemantics.AbsoluteOccupancy
            };
            bc.ProximalTriggerSource = config.RawProximalTrigger
                ? SquatPredictiveBalanceController.ProximalTrigger.RawAnkleDemand
                : SquatPredictiveBalanceController.ProximalTrigger.GuardedAnkleCommand;
            bc.TargetToCopMPerRad = config.PhaseGain
                ? SampleGain(sq)
                : SquatPredictiveBalanceController.MeasuredTargetToCopMPerRad;
        }

        private static float SampleGain(float sq)
        {
            float clamped = Mathf.Min(sq, DeepestQualifiedGainPhase);
            if (clamped <= GainPhases[0])
                return GainValues[0];
            float t = Mathf.InverseLerp(GainPhases[0], GainPhases[1], clamped);
            return Mathf.Lerp(GainValues[0], GainValues[1], t);
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }
    }
}
