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
    /// Phase 5H13, sections 3, 4 and 8 to 10.
    ///
    /// 5H12 established that the posture limit guard withdraws the ankle
    /// balance command at depth because the accepted reference itself commands
    /// 93 percent of the hip's authored range, and that simply disabling the
    /// guard collapses the 25 kg case rearward. Two questions are left before
    /// anything may be redesigned.
    ///
    /// First, what else does the guard switch off on its way past. The blend
    /// that brings in the hip is gated on the ankle saturation fraction, and
    /// that fraction is computed after the guard has already multiplied the
    /// command. If that ordering holds, the guard removes the ankle and then
    /// hides the removal from the strategy that exists to rescue it.
    ///
    /// Second, how much of the deep-squat over-command is the guard at all.
    /// 5H12 measured a centre-of-mass reference that is 61 mm stale at the
    /// bottom and a plant nearly twice as sensitive at mid-squat as the
    /// production constant assumes. Those two errors are evaluated here
    /// offline, on one recorded trace, in all four combinations, so the
    /// redesign is aimed at whichever actually carries the magnitude.
    ///
    /// Nothing here changes a control value. D1 and D2 read the running
    /// controller; D3 replays recorded states through the controller's own
    /// equations without a physics step.
    /// </summary>
    public sealed class PhaseAwareBalancePlantTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

        private const float ApproachPhaseRate = 0.30f;
        private const int HoldTicks = 250;

        /// <summary>
        /// The joints the guard reads. Deliberately the same set the adapter
        /// uses, so the decomposition explains the number the guard actually
        /// sees rather than a differently scoped one.
        /// </summary>
        private static readonly string[] GuardedJoints =
        {
            "left_shank", "right_shank", "left_thigh", "right_thigh", "abdomen", "thorax"
        };

        private static readonly string[] ReportedFamilies =
        {
            "left_shank", "left_thigh", "abdomen", "thorax"
        };

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
                _controller.enabled = false;
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

        // ------------------------------------------------------------------
        // D1. The guard cascade, section 3.
        // ------------------------------------------------------------------

        /// <summary>
        /// Records the whole ankle chain in one trace: what the balance law
        /// asked for, what the guard multiplied it by, what survived, and what
        /// the proximal strategy was told about the result.
        ///
        /// The discriminating column is hip_blend_from_raw. It applies the
        /// controller's own blend rule to the pre-guard demand instead of the
        /// post-guard command. If the two disagree while the guard is closed,
        /// the guard is not merely limiting the ankle, it is also withholding
        /// the evidence that the ankle needed rescuing.
        /// </summary>
        [UnityTest]
        public IEnumerator D1_GUARD_CASCADE_AND_PROXIMAL_SUPPRESSION()
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,phase,hold_tick,posture_scale,limit_scale,posture_guard_scale," +
                           "raw_ankle_deg,guarded_ankle_deg,applied_ankle_deg," +
                           "raw_authority_fraction,guarded_authority_fraction," +
                           "hip_blend_actual,hip_blend_from_raw,hip_offset_deg,trunk_offset_deg," +
                           "posture_error_rad,posture_error_rate,limit_prox,limit_driver," +
                           "com_ap,com_ref_ap,cop_desired,cop_measured,contacts");

            var rows = new List<CascadeRow>();
            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float phase in new[] { 0.55f, 0.80f, 1.00f })
                    yield return TraceCascade(load, phase, csv, rows);
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H13 D1 POSTURE GUARD CASCADE");
            report.AppendLine("Held pose, full production composition. raw is the balance law's");
            report.AppendLine("request before the guard; guarded is the same request after it.");
            report.AppendLine("blend_from_raw applies the controller's own hip blend rule to the raw");
            report.AppendLine("demand, which is what the strategy would have seen without the guard.");
            report.AppendLine();
            report.AppendLine("load  s_q   guardScale  rawAnkleDeg  guardedDeg  rawAuth  guardedAuth  blendActual  blendFromRaw  driver");
            foreach (CascadeRow r in rows)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F0}  {1,4:F2}  {2,10:F4}  {3,11:F2}  {4,10:F2}  {5,7:F3}  {6,11:F3}  {7,11:F3}  {8,12:F3}  {9}",
                    r.LoadKg, r.Phase, r.GuardScale, r.RawAnkleDeg, r.GuardedAnkleDeg,
                    r.RawAuthority, r.GuardedAuthority, r.BlendActual, r.BlendFromRaw, r.Driver));
            }

            // The claim under test, stated so the trace can refute it.
            int suppressed = 0;
            int guardClosedSamples = 0;
            foreach (CascadeRow r in rows)
            {
                if (r.GuardScale > 0.5f)
                    continue;
                guardClosedSamples++;
                if (r.BlendFromRaw > r.BlendActual + 0.01f)
                    suppressed++;
            }

            string verdict = guardClosedSamples == 0
                ? "NO_GUARD_CLOSED_SAMPLE"
                : suppressed > 0 ? "YES" : "NO";

            report.AppendLine();
            report.AppendLine("Samples with guard scale at or below 0.5: " +
                              guardClosedSamples.ToString(CultureInfo.InvariantCulture) +
                              ", of which the raw demand would have blended the hip in further: " +
                              suppressed.ToString(CultureInfo.InvariantCulture) + ".");
            report.AppendLine("GUARD_SUPPRESSES_PROXIMAL_STRATEGY = " + verdict);

            WriteMeasurement("GAM11-5h13-d1-guard-cascade.txt", report.ToString());
            WriteMeasurement("GAM11-5h13-d1-guard-cascade.csv", csv.ToString());
            Debug.Log(report.ToString());

            Assert.That(rows.Count, Is.GreaterThan(0), "The cascade trace produced no samples.");
            yield return null;
        }

        private struct CascadeRow
        {
            public float LoadKg;
            public float Phase;
            public float GuardScale;
            public float RawAnkleDeg;
            public float GuardedAnkleDeg;
            public float RawAuthority;
            public float GuardedAuthority;
            public float BlendActual;
            public float BlendFromRaw;
            public string Driver;
        }

        private IEnumerator TraceCascade(float loadKg, float phase, StringBuilder csv, List<CascadeRow> rows)
        {
            yield return ApproachPhase(loadKg, phase);

            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            CascadeRow last = default;
            for (int tick = 0; tick < HoldTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                if (tick % 10 != 0 && tick != HoldTicks - 1)
                    continue;

                float bound = bc.AnkleSagittalBoundRad;
                float raw = bc.RawAnkleSagittalOffsetRad;
                float guarded = raw * bc.PostureGuardScale;
                float rawAuthority = Mathf.Abs(raw) / bound;
                float guardedAuthority = Mathf.Abs(guarded) / bound;

                // The controller's own blend rule, applied to each fraction.
                float blendActual = Mathf.Clamp01(Mathf.InverseLerp(0.85f, 1f, guardedAuthority));
                float blendFromRaw = Mathf.Clamp01(Mathf.InverseLerp(0.85f, 1f, rawAuthority));

                // Reconstruct the two guard terms separately. Both are pure
                // functions of state the controller publishes.
                float excess = Mathf.InverseLerp(
                    SquatPredictiveBalanceController.PostureGuardOnsetRad,
                    SquatPredictiveBalanceController.PostureGuardFullRad,
                    bc.PostureErrorRad);
                float growth = Mathf.Clamp01(Mathf.Max(0f, bc.PostureErrorRateRadPerS) /
                                             SquatPredictiveBalanceController.PostureGuardRateFullRadPerS);
                float postureScale = 1f - excess * growth;
                float limitScale = 1f - Mathf.InverseLerp(
                    SquatPredictiveBalanceController.LimitGuardOnset,
                    SquatPredictiveBalanceController.LimitGuardFull,
                    bc.PostureLimitProximity);

                string driver = WorstGuardedJoint(powered, out float driverProx);

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1:F2},{2},{3:F4},{4:F4},{5:F4},{6:F3},{7:F3},{8:F3},{9:F4},{10:F4}," +
                    "{11:F4},{12:F4},{13:F3},{14:F3},{15:F5},{16:F5},{17:F4},{18},{19:F4},{20:F4},{21:F4},{22:F4},{23}",
                    loadKg, phase, tick,
                    postureScale, limitScale, bc.PostureGuardScale,
                    raw * Mathf.Rad2Deg, guarded * Mathf.Rad2Deg, bc.AnkleSagittalOffsetRad * Mathf.Rad2Deg,
                    rawAuthority, guardedAuthority,
                    blendActual, blendFromRaw,
                    bc.HipSagittalOffsetRad * Mathf.Rad2Deg, bc.TrunkSagittalOffsetRad * Mathf.Rad2Deg,
                    bc.PostureErrorRad, bc.PostureErrorRateRadPerS, bc.PostureLimitProximity, driver,
                    balance.SystemCom.z, bc.ComRefAp, bc.CopDesiredAp, bc.CopMeasuredAp,
                    balance.SupportContactCount));

                last = new CascadeRow
                {
                    LoadKg = loadKg,
                    Phase = phase,
                    GuardScale = bc.PostureGuardScale,
                    RawAnkleDeg = raw * Mathf.Rad2Deg,
                    GuardedAnkleDeg = guarded * Mathf.Rad2Deg,
                    RawAuthority = rawAuthority,
                    GuardedAuthority = guardedAuthority,
                    BlendActual = blendActual,
                    BlendFromRaw = blendFromRaw,
                    Driver = driver
                };
            }
            rows.Add(last);
        }

        // ------------------------------------------------------------------
        // D2. Nominal versus correction-induced limit risk, section 4.
        // ------------------------------------------------------------------

        /// <summary>
        /// The guard treats a joint near its stop as a joint in trouble. That
        /// conflates two different things: a reference that deliberately sits
        /// deep, and a correction that has pushed a joint somewhere the
        /// reference never asked it to go.
        ///
        /// For every guarded family this records the authored range, the
        /// nominal reference angle, the fully composed target and the actual
        /// angle, and derives the directional margins between them. The
        /// quantity a correction-relative guard would need is the last column:
        /// how much of the margin the reference itself left over has been
        /// consumed by something other than the reference.
        /// </summary>
        [UnityTest]
        public IEnumerator D2_NOMINAL_VERSUS_CORRECTION_INDUCED_LIMIT_MARGIN()
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,phase,joint,q_low_deg,q_high_deg,q_nominal_deg,q_gravity_deg," +
                           "q_balance_deg,q_final_target_deg,q_actual_deg," +
                           "nominal_margin_low,nominal_margin_high,final_margin_low,final_margin_high," +
                           "actual_margin_low,actual_margin_high,correction_direction," +
                           "prox_nominal,prox_final,prox_actual," +
                           "correction_reduces_limit_margin,correction_margin_consumed_deg," +
                           "unexpected_margin_consumed_deg,nominal_headroom_fraction");

            var rows = new List<MarginRow>();
            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float phase in new[] { 0.00f, 0.55f, 0.80f, 1.00f })
                    yield return SampleMargins(load, phase, csv, rows);
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H13 D2 NOMINAL VERSUS CORRECTION-INDUCED LIMIT MARGIN");
            report.AppendLine("Angles are the calibrated flexion axis in joint space, degrees.");
            report.AppendLine("prox is |q| / the authored range on the side q sits on, which is exactly");
            report.AppendLine("the quantity PoweredJointDiagnostic.LimitProximity reports and the guard reads.");
            report.AppendLine("consumed is prox_actual - prox_nominal expressed in degrees of range: the");
            report.AppendLine("part of the occupancy the accepted reference did not ask for.");
            report.AppendLine();
            report.AppendLine("load  s_q   joint         range[low,high]   qNom     qFinal   qAct    proxNom  proxFin  proxAct  consumedDeg  reducesMargin");
            foreach (MarginRow r in rows)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,4:F0}  {1,4:F2}  {2,-12}  [{3,6:F1},{4,6:F1}]  {5,7:F2}  {6,7:F2}  {7,7:F2}  {8,7:F3}  {9,7:F3}  {10,7:F3}  {11,11:F3}  {12}",
                    r.LoadKg, r.Phase, r.Joint, r.LowDeg, r.HighDeg,
                    r.NominalDeg, r.FinalDeg, r.ActualDeg,
                    r.ProxNominal, r.ProxFinal, r.ProxActual,
                    r.UnexpectedConsumedDeg, r.CorrectionReducesMargin));
            }

            report.AppendLine();
            report.AppendLine(SummariseMargins(rows));

            WriteMeasurement("GAM11-5h13-d2-limit-margins.txt", report.ToString());
            WriteMeasurement("GAM11-5h13-d2-limit-margins.csv", csv.ToString());
            Debug.Log(report.ToString());

            Assert.That(rows.Count, Is.GreaterThan(0), "The margin sweep produced no samples.");
            yield return null;
        }

        private struct MarginRow
        {
            public float LoadKg;
            public float Phase;
            public string Joint;
            public float LowDeg;
            public float HighDeg;
            public float NominalDeg;
            public float FinalDeg;
            public float ActualDeg;
            public float ProxNominal;
            public float ProxFinal;
            public float ProxActual;
            public float UnexpectedConsumedDeg;
            public bool CorrectionReducesMargin;
        }

        private IEnumerator SampleMargins(float loadKg, float phase, StringBuilder csv, List<MarginRow> rows)
        {
            yield return ApproachPhase(loadKg, phase);

            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            // Let the hold reach the state the guard is actually reading.
            for (int tick = 0; tick < 120; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            foreach (string jointId in ReportedFamilies)
            {
                PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(jointId);
                if (joint == null || !adapter.TryGetTargetComposition(jointId, out var composition))
                    continue;

                float low = joint.Recipe.LowDegrees;
                float high = joint.Recipe.HighDegrees;

                float qNominal = TwistX(composition.Nominal);
                float qGravity = TwistX(composition.GravityBias);
                float qBalance = TwistX(composition.BalanceOffset);
                float qFinal = TwistX(composition.Final);
                float qActual = TwistX(joint.Diagnostic.ActualRelative);

                float proxNominal = Proximity(qNominal, low, high);
                float proxFinal = Proximity(qFinal, low, high);
                float proxActual = Proximity(qActual, low, high);

                // Direction of the correction on this joint, and whether it
                // spends margin or returns it.
                float correction = qFinal - qNominal;
                string direction = Mathf.Abs(correction) < 1e-3f
                    ? "NONE"
                    : correction > 0f ? "TOWARD_HIGH" : "TOWARD_LOW";
                bool reduces = proxFinal > proxNominal + 1e-4f;

                // In degrees of the range the joint actually sits against, so
                // the two proximities are differenced on a common scale.
                float side = qActual >= 0f ? Mathf.Max(0.001f, high) : Mathf.Max(0.001f, -low);
                float commandedConsumed = (proxFinal - proxNominal) * side;
                float unexpectedConsumed = (proxActual - proxNominal) * side;

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1:F2},{2},{3:F2},{4:F2},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4}," +
                    "{10:F4},{11:F4},{12:F4},{13:F4},{14:F4},{15:F4},{16},{17:F4},{18:F4},{19:F4},{20},{21:F4},{22:F4},{23:F4}",
                    loadKg, phase, jointId, low, high,
                    qNominal, qGravity, qBalance, qFinal, qActual,
                    qNominal - low, high - qNominal,
                    qFinal - low, high - qFinal,
                    qActual - low, high - qActual,
                    direction, proxNominal, proxFinal, proxActual,
                    reduces ? 1 : 0, commandedConsumed, unexpectedConsumed,
                    1f - proxNominal));

                rows.Add(new MarginRow
                {
                    LoadKg = loadKg,
                    Phase = phase,
                    Joint = jointId,
                    LowDeg = low,
                    HighDeg = high,
                    NominalDeg = qNominal,
                    FinalDeg = qFinal,
                    ActualDeg = qActual,
                    ProxNominal = proxNominal,
                    ProxFinal = proxFinal,
                    ProxActual = proxActual,
                    UnexpectedConsumedDeg = unexpectedConsumed,
                    CorrectionReducesMargin = reduces
                });
            }
        }

        private static string SummariseMargins(List<MarginRow> rows)
        {
            var text = new StringBuilder();
            foreach (float load in new[] { 0f, 25f })
            {
                foreach (float phase in new[] { 0.80f, 1.00f })
                {
                    foreach (MarginRow r in rows)
                    {
                        if (Mathf.Abs(r.LoadKg - load) > 0.01f || Mathf.Abs(r.Phase - phase) > 0.001f)
                            continue;
                        if (r.Joint != "left_thigh")
                            continue;
                        text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "NOMINAL_HIP_LIMIT_OCCUPANCY  load {0:F0} kg  s_q {1:F2} = {2:F4}   " +
                            "actual {3:F4}   unexpected consumption {4:F3} deg",
                            load, phase, r.ProxNominal, r.ProxActual, r.UnexpectedConsumedDeg));
                    }
                }
            }
            return text.ToString();
        }

        // ------------------------------------------------------------------
        // D3. Four-way offline model decomposition, sections 8 to 10.
        // ------------------------------------------------------------------

        /// <summary>
        /// The reference centre-of-mass trajectory derived in 5H12 R1, sampled
        /// on the accepted GAM-10 pose with the rig's own segment masses.
        /// Provenance: Artifacts/Measurements/GAM-11/GAM11-5h12-r1-reference-com-trajectory.csv.
        /// Held here as the phase delta from standing, which is the only part
        /// the controller needs: the standing value is already calibrated on
        /// the physical rig, so substituting the absolute derived number would
        /// also move the standing setpoint the standing gate passes on.
        /// </summary>
        private static readonly float[] ReferencePhaseSamples =
        {
            0.00f, 0.05f, 0.10f, 0.15f, 0.20f, 0.25f, 0.30f, 0.35f, 0.40f, 0.45f, 0.50f,
            0.55f, 0.60f, 0.65f, 0.70f, 0.75f, 0.80f, 0.85f, 0.90f, 0.95f, 1.00f
        };

        private static readonly float[] ReferenceComApDelta0Kg =
        {
            0.00000f, -0.00281f, -0.00510f, -0.00768f, -0.01133f, -0.01680f, -0.02498f,
            -0.03477f, -0.04409f, -0.05132f, -0.05561f, -0.05696f, -0.05610f, -0.05748f,
            -0.06097f, -0.06369f, -0.06477f, -0.06482f, -0.06436f, -0.06302f, -0.06070f
        };

        private static readonly float[] ReferenceComApDelta25Kg =
        {
            0.00000f, -0.00157f, -0.00238f, -0.00337f, -0.00548f, -0.00966f, -0.01669f,
            -0.02524f, -0.03323f, -0.03907f, -0.04193f, -0.04197f, -0.04056f, -0.04183f,
            -0.04505f, -0.04742f, -0.04814f, -0.04796f, -0.04735f, -0.04587f, -0.04335f
        };

        /// <summary>
        /// Phase-local target-to-COP sensitivity, 5H12 C2. Only two phases
        /// qualified: no static equilibrium exists deeper, so no honest
        /// quasi-static probe does either. Beyond the deepest qualified sample
        /// the mapping holds that sample's value rather than extrapolating a
        /// trend through unmeasured territory, and every consumer of this
        /// table has to say so.
        /// Provenance: Artifacts/Measurements/GAM-11/GAM11-5h12-c2-target-to-cop-gain.csv.
        /// </summary>
        private static readonly float[] QualifiedGainPhases = { 0.00f, 0.55f };
        private static readonly float[] QualifiedGainValues = { 0.26530f, 0.38516f };
        private const float DeepestQualifiedGainPhase = 0.55f;

        [UnityTest]
        public IEnumerator D3_FOUR_WAY_MODEL_DECOMPOSITION()
        {
            var states = new List<TraceState>();
            foreach (float load in new[] { 0f, 25f })
                yield return RecordDescent(load, states);

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,sq,com_ap,com_vel_ap,com_height,support_center,support_min,support_max," +
                           "cop_measured,standing_com_ref,phase_com_ref,gain_standing,gain_phase,gain_extrapolated," +
                           "m00_cop_desired,m00_ankle_deg,m00_exceeds," +
                           "m10_cop_desired,m10_ankle_deg,m10_exceeds," +
                           "m01_cop_desired,m01_ankle_deg,m01_exceeds," +
                           "m11_cop_desired,m11_ankle_deg,m11_exceeds");

            var maxima = new Dictionary<string, float>();
            var boundBreaches = new Dictionary<string, int>();
            foreach (string key in new[] { "M00", "M10", "M01", "M11" })
            {
                maxima["0:" + key] = 0f;
                maxima["25:" + key] = 0f;
                boundBreaches["0:" + key] = 0;
                boundBreaches["25:" + key] = 0;
            }

            float bound = SquatPredictiveBalanceController.MaxAnkleSagittalOffsetRad;

            foreach (TraceState s in states)
            {
                float standingRef = s.SupportCenter + s.StandingComApOffset;
                float phaseDelta = SampleReferenceDelta(s.Sq, s.LoadKg);
                float phaseRef = standingRef + phaseDelta;

                float gainStanding = SquatPredictiveBalanceController.MeasuredTargetToCopMPerRad;
                bool extrapolated = s.Sq > DeepestQualifiedGainPhase;
                float gainPhase = SampleQualifiedGain(s.Sq);

                ModelResult m00 = Evaluate(s, standingRef, gainStanding, bound);
                ModelResult m10 = Evaluate(s, phaseRef, gainStanding, bound);
                ModelResult m01 = Evaluate(s, standingRef, gainPhase, bound);
                ModelResult m11 = Evaluate(s, phaseRef, gainPhase, bound);

                string tag = s.LoadKg < 1f ? "0:" : "25:";
                Accumulate(maxima, boundBreaches, tag + "M00", m00);
                Accumulate(maxima, boundBreaches, tag + "M10", m10);
                Accumulate(maxima, boundBreaches, tag + "M01", m01);
                Accumulate(maxima, boundBreaches, tag + "M11", m11);

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1},{2:F4},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5}," +
                    "{12:F5},{13:F5},{14}," +
                    "{15:F5},{16:F3},{17},{18:F5},{19:F3},{20},{21:F5},{22:F3},{23},{24:F5},{25:F3},{26}",
                    s.LoadKg, s.Tick, s.Sq, s.ComAp, s.ComVelAp, s.ComHeight,
                    s.SupportCenter, s.SupportMin, s.SupportMax, s.CopMeasured,
                    standingRef, phaseRef, gainStanding, gainPhase, extrapolated ? 1 : 0,
                    m00.CopDesired, m00.AnkleDeg, m00.Exceeds ? 1 : 0,
                    m10.CopDesired, m10.AnkleDeg, m10.Exceeds ? 1 : 0,
                    m01.CopDesired, m01.AnkleDeg, m01.Exceeds ? 1 : 0,
                    m11.CopDesired, m11.AnkleDeg, m11.Exceeds ? 1 : 0));
            }

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H13 D3 FOUR-WAY BALANCE MODEL DECOMPOSITION");
            report.AppendLine("One recorded descent per load, replayed offline through the controller's");
            report.AppendLine("own equations. No physics step is taken and no production value changes.");
            report.AppendLine();
            report.AppendLine("  M00  standing COM reference   + standing target-to-COP gain  (production today)");
            report.AppendLine("  M10  phase COM reference      + standing gain");
            report.AppendLine("  M01  standing COM reference   + phase-local gain");
            report.AppendLine("  M11  phase COM reference      + phase-local gain");
            report.AppendLine();
            report.AppendLine("The phase gain table qualifies only s_q 0.00 and 0.55. Deeper than 0.55 it");
            report.AppendLine("holds the 0.55 value; those samples are flagged gain_extrapolated in the CSV.");
            report.AppendLine();
            report.AppendLine("Ankle request is degrees before the 15 deg bound. exceeded counts the");
            report.AppendLine("samples whose request the bound would have to clip.");
            report.AppendLine();
            report.AppendLine("load  model  maxRawAnkleRequestDeg  samplesOverBound");
            foreach (string load in new[] { "0", "25" })
            {
                foreach (string model in new[] { "M00", "M10", "M01", "M11" })
                {
                    string key = load + ":" + model;
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0,4}  {1,-5}  {2,21:F2}  {3,16}",
                        load, model, maxima[key], boundBreaches[key]));
                }
            }

            report.AppendLine();
            report.AppendLine(AttributeContributions(maxima));

            WriteMeasurement("GAM11-5h13-d3-model-decomposition.txt", report.ToString());
            WriteMeasurement("GAM11-5h13-d3-model-decomposition.csv", csv.ToString());
            Debug.Log(report.ToString());

            Assert.That(states.Count, Is.GreaterThan(0), "The decomposition recorded no states.");
            yield return null;
        }

        private static string AttributeContributions(Dictionary<string, float> maxima)
        {
            var text = new StringBuilder();
            foreach (string load in new[] { "0", "25" })
            {
                float m00 = maxima[load + ":M00"];
                float m10 = maxima[load + ":M10"];
                float m01 = maxima[load + ":M01"];
                float m11 = maxima[load + ":M11"];
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "load {0} kg: COM reference alone moves the peak request {1:F2} deg " +
                    "({2:F1} pct of M00), plant gain alone {3:F2} deg ({4:F1} pct), both {5:F2} deg ({6:F1} pct).",
                    load, m10 - m00, m00 > 0f ? 100f * (m10 - m00) / m00 : 0f,
                    m01 - m00, m00 > 0f ? 100f * (m01 - m00) / m00 : 0f,
                    m11 - m00, m00 > 0f ? 100f * (m11 - m00) / m00 : 0f));
            }
            return text.ToString();
        }

        private struct ModelResult
        {
            public float CopDesired;
            public float AnkleDeg;
            public bool Exceeds;
        }

        private static void Accumulate(
            Dictionary<string, float> maxima, Dictionary<string, int> breaches, string key, ModelResult r)
        {
            maxima[key] = Mathf.Max(maxima[key], Mathf.Abs(r.AnkleDeg));
            if (r.Exceeds)
                breaches[key]++;
        }

        /// <summary>
        /// The controller's own outer law and ankle inversion, with the two
        /// substitutable pieces passed in. Every other equation is copied
        /// exactly, including the support interior clamp, so a difference
        /// between models is only ever the reference or the gain.
        /// </summary>
        private static ModelResult Evaluate(TraceState s, float comRefAp, float gain, float bound)
        {
            float error = s.ComAp - comRefAp;
            float desiredAcceleration =
                -SquatPredictiveBalanceController.DefaultComKp * error -
                SquatPredictiveBalanceController.DefaultComKd * s.ComVelAp;

            float heightOverGravity = s.ComHeight / SquatBalanceObserver.GravityMagnitudeMps2;
            float copDesired = s.ComAp - heightOverGravity * desiredAcceleration;

            float supportLength = s.SupportMax - s.SupportMin;
            float interiorMargin = Mathf.Min(
                SquatPredictiveBalanceController.SupportInteriorMarginM,
                Mathf.Max(0f, 0.4f * supportLength));
            float copMin = s.SupportMin + interiorMargin;
            float copMax = s.SupportMax - interiorMargin;
            if (copMin > copMax)
            {
                float mid = 0.5f * (copMin + copMax);
                copMin = copMax = mid;
            }
            copDesired = Mathf.Clamp(copDesired, copMin, copMax);

            float ankle = SquatPredictiveBalanceController.DefaultCopTrackingGain *
                          (copDesired - s.CopMeasured) / Mathf.Max(0.01f, gain);

            return new ModelResult
            {
                CopDesired = copDesired,
                AnkleDeg = ankle * Mathf.Rad2Deg,
                Exceeds = Mathf.Abs(ankle) > bound
            };
        }

        private static float SampleReferenceDelta(float sq, float loadKg)
        {
            float[] table = loadKg < 1f ? ReferenceComApDelta0Kg : ReferenceComApDelta25Kg;
            return Interpolate(ReferencePhaseSamples, table, sq);
        }

        private static float SampleQualifiedGain(float sq)
        {
            float clamped = Mathf.Min(sq, DeepestQualifiedGainPhase);
            return Interpolate(QualifiedGainPhases, QualifiedGainValues, clamped);
        }

        private static float Interpolate(float[] xs, float[] ys, float x)
        {
            if (x <= xs[0])
                return ys[0];
            for (int i = 1; i < xs.Length; i++)
            {
                if (x > xs[i])
                    continue;
                float t = Mathf.InverseLerp(xs[i - 1], xs[i], x);
                return Mathf.Lerp(ys[i - 1], ys[i], t);
            }
            return ys[ys.Length - 1];
        }

        private struct TraceState
        {
            public float LoadKg;
            public int Tick;
            public float Sq;
            public float ComAp;
            public float ComVelAp;
            public float ComHeight;
            public float SupportCenter;
            public float SupportMin;
            public float SupportMax;
            public float CopMeasured;
            public float StandingComApOffset;
        }

        /// <summary>
        /// One production descent, recorded until the phase reaches the bottom.
        /// The standing COM offset is captured from the controller's own
        /// reference before the descent starts, so the replay reproduces the
        /// setpoint the running controller actually used.
        /// </summary>
        private IEnumerator RecordDescent(float loadKg, List<TraceState> states)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController bc = adapter.BalanceController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            float standingOffset = bc.ComRefAp - balance.SupportApCenter;

            float sq = 0f;
            int tick = 0;
            while (sq < 1f - 1e-4f && tick < 3000)
            {
                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;

                if (tick % 4 != 0)
                    continue;
                if (!balance.HasSupport || balance.SystemMassKg <= 0f)
                    continue;

                states.Add(new TraceState
                {
                    LoadKg = loadKg,
                    Tick = tick,
                    Sq = sq,
                    ComAp = balance.SystemCom.z,
                    ComVelAp = balance.SystemComVelocity.z,
                    ComHeight = balance.ComHeightM,
                    SupportCenter = balance.SupportApCenter,
                    SupportMin = balance.SupportApMin,
                    SupportMax = balance.SupportApMax,
                    CopMeasured = balance.HasCopEstimate ? balance.CopEstimate.z : balance.SupportApCenter,
                    StandingComApOffset = standingOffset
                });
            }
            yield return null;
        }

        // ------------------------------------------------------------------
        // Shared helpers.
        // ------------------------------------------------------------------

        private IEnumerator ApproachPhase(float loadKg, float phase)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.BalanceCorrectionsEnabled = true;

            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            SquatState state = phase >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
            float current = 0f;
            int guard = 0;
            while (current < phase - 1e-4f && guard++ < 4000)
            {
                current = Mathf.Min(phase, current + ApproachPhaseRate * dt);
                adapter.HoldReferencePhaseForQualification(current, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }
            adapter.HoldReferencePhaseForQualification(phase, SquatPhaseDirection.Descent, state, 0f);
            yield return null;
        }

        private string WorstGuardedJoint(PoweredJointController powered, out float worstProx)
        {
            worstProx = 0f;
            string worst = "NONE";
            foreach (string id in GuardedJoints)
            {
                PoweredJointController.PoweredJointRuntime joint = powered.GetJoint(id);
                if (joint == null)
                    continue;
                float prox = joint.Diagnostic.LimitProximity;
                if (prox > worstProx)
                {
                    worstProx = prox;
                    worst = id;
                }
            }
            return worst;
        }

        /// <summary>
        /// The calibrated flexion component of a joint-space rotation, in
        /// degrees. Same swing-twist decomposition PoweredJointController uses
        /// to build LimitProximity, replicated here so the diagnostic does not
        /// need production surface it would otherwise not have.
        /// </summary>
        private static float TwistX(Quaternion rotation)
        {
            Quaternion q = PoweredJointController.NormalizeCanonical(rotation);
            var axis = new Vector3(q.x, q.y, q.z);
            Vector3 projection = Vector3.Project(axis, Vector3.right);
            var twist = new Quaternion(projection.x, projection.y, projection.z, q.w);
            if (twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w < 1e-12f)
                return 0f;
            twist = PoweredJointController.NormalizeCanonical(twist.normalized);
            twist.ToAngleAxis(out float angle, out Vector3 resolved);
            if (angle > 180f)
                angle -= 360f;
            return Vector3.Dot(resolved, Vector3.right) >= 0f ? angle : -angle;
        }

        private static float Proximity(float degrees, float lowDegrees, float highDegrees)
        {
            float limit = degrees >= 0f
                ? Mathf.Max(0.001f, highDegrees)
                : Mathf.Max(0.001f, -lowDegrees);
            return Mathf.Clamp01(Mathf.Abs(degrees) / limit);
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
