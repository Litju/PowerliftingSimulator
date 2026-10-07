using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Squat.Unity;
using PowerliftingSimulator.Tests;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// GAM-50 benchmarks 12-13: the shared physical athlete standing, and
    /// the static canonical pose matrix. Every case is a fresh production
    /// scene; poses are initialised before the first tick only and then held
    /// by the production command path under gravity, contacts, finite drives
    /// and the bar/saddle load path, with no phase progression.
    /// </summary>
    [Category("PhysicsBenchmark")]
    [Category("PhysicsBenchmarkAthlete")]
    public sealed class AthletePhysicsBenchmarks
    {
        public static readonly float[] Loads = { 0f, 25f, 60f, 140f, 170f, 300f };
        public static readonly float[] Phases = { 0.00f, 0.10f, 0.25f, 0.55f, 0.75f, 1.00f };

        // Sealed GAM-13 qualification constants (GAM13V2SquatMechanicsQualificationTests,
        // GAM13StaticTrimEquilibriumTests, PoweredJointController).
        private const float MinimumComSupportMarginM = -0.02f;
        private const float MaximumSaddleSeparationM = 0.02f;
        private const float MaximumSaddleLimitOccupancy = 0.95f;
        private const float MaximumLimitProximity = 0.95f;
        private const float MaximumDemandFraction = 0.95f;
        // GAM-12 sealed lockout bar stillness.
        private const float BarStillnessMps = 0.020f;
        // Explicit engineering / product tolerances (docs/ARCHITECTURE.md).
        private const float MaximumNominalTrackingErrorRad = 0.10f;
        private const float MaximumAnchorSeparationM = 0.01f;
        private const float MaximumPelvisDropM = 0.05f;
        private const float MaximumFootTravelM = 0.005f;
        private const float SettledComSpeedMps = 0.020f;
        private const float ReleaseWindowSeconds = 0.5f;

        // GAM-13 V2-5 envelope: 170 kg is near-max and 300 kg supra-max at the
        // calibrated intrinsic strength, so their held poses are characterised
        // (capacity is meant to bind) while 0-140 kg must qualify. The oracle's
        // physics rows stay gated at every load.
        private const float QualifiedHoldLoadCeilingKg = 140f;

        [UnityTest]
        public IEnumerator B12_SharedAthlete_Standing([ValueSource(nameof(Loads))] float loadKg)
        {
            var rec = new PhysicsBenchmarkRecorder($"B12_standing_{loadKg:000}kg");
            var session = new AthleteBenchmarkSession();
            yield return AthleteBenchmarkSession.Load(session, loadKg);

            string receipt = Path.Combine(PhysicsBenchmarkRecorder.RawDirectory(), $"runtime-receipt-{loadKg:000}kg.json");
            if (File.Exists(receipt))
                File.Delete(receipt);
            GAM13V23BSubstrateAuditTests.WriteRuntimeReceipt(session.Controller, session.Runtime, loadKg, receipt, "GAM-50 baseline");
            if (session.Controller.Saddle != null)
                RecordSaddleReplicaParity(rec, session);

            var samples = new List<AthleteSample>();
            const int ticks = 1000;
            for (int i = 0; i < ticks; i++)
            {
                session.Step();
                AthleteSample sample = AthleteSample.Capture(session);
                samples.Add(sample);
                if (i % 2 == 0)
                    rec.Series("trace", AthleteSample.CsvHeader, sample.ToCsv());
                if (i % 100 == 0)
                    yield return null;
            }
            RecordHold(rec, session, samples, "standing_hold_10s", qualifying: loadKg <= QualifiedHoldLoadCeilingKg);
            rec.Info("cpu_ms_per_tick", session.Cfg("standing_hold_10s"), session.MeanStepMilliseconds,
                "Mean wall time per authoritative tick in the batch editor (budget context: 10 ms real time per tick).",
                CausalLayer.NumericalConvergence);
            OracleExport.Write(session, $"B12_standing_{loadKg:000}kg", "settled", 0f, "production");
            rec.WriteAndAssert();
        }

        [UnityTest]
        public IEnumerator B13_SharedAthlete_StaticPoseMatrix([ValueSource(nameof(Loads))] float loadKg)
        {
            var rec = new PhysicsBenchmarkRecorder($"B13_static_matrix_{loadKg:000}kg");
            foreach (float phase in Phases)
            {
                foreach (string variant in new[] { "production", "no_balance_feedback" })
                {
                    var session = new AthleteBenchmarkSession();
                    yield return AthleteBenchmarkSession.Load(session, loadKg);
                    session.Adapter.BalanceCorrectionsEnabled = variant == "production";
                    var (displacement, footMismatch) = session.PrePose(phase);
                    session.HoldPhase(phase);
                    string caseId = $"B13_{loadKg:000}kg_s{phase:0.00}_{variant}";
                    string label = $"phase={phase:0.00};{variant}";
                    rec.Info("prepose_max_body_displacement_m", session.Cfg(label), displacement,
                        "How far the canonical pose is from the authored bind pose.", CausalLayer.JointTopologyTargetConvention);
                    rec.Record("prepose_foot_rotation_mismatch_deg", session.Cfg(label), 0, footMismatch, 1.0, ToleranceKind.UpperBound,
                        "canonical q_ref keeps the registered plantar foot orientation; 1 deg engineering tolerance",
                        "Canonical targets place the feet off the registered plantar plane (frame/convention error).",
                        CausalLayer.JointTopologyTargetConvention);

                    var samples = new List<AthleteSample>();
                    const int ticks = 300;
                    for (int i = 0; i < ticks; i++)
                    {
                        session.Step();
                        if (i == 0)
                            OracleExport.Write(session, caseId, "tick1", phase, variant);
                        AthleteSample sample = AthleteSample.Capture(session);
                        samples.Add(sample);
                        if (i % 5 == 0)
                            rec.Series($"s{phase:0.00}_{variant}", AthleteSample.CsvHeader, sample.ToCsv());
                        if (i % 100 == 0)
                            yield return null;
                    }
                    OracleExport.Write(session, caseId, "settled", phase, variant);
                    RecordHold(rec, session, samples, label,
                        qualifying: variant == "production" && loadKg <= QualifiedHoldLoadCeilingKg);
                }
            }
            rec.WriteAndAssert();
        }

        /// <summary>
        /// Gates one held pose. Production composition qualifies the shared
        /// athlete; the no-feedback variant is characterisation (a stiffness-
        /// only ankle cannot stabilise every load, which is equilibrium
        /// physics, not a substrate defect).
        /// </summary>
        private static void RecordHold(PhysicsBenchmarkRecorder rec, AthleteBenchmarkSession session,
            List<AthleteSample> samples, string label, bool qualifying)
        {
            string cfg = session.Cfg(label);
            AthleteSample first = samples[0];
            AthleteSample last = samples[samples.Count - 1];
            int tailStart = samples.Count - Mathf.RoundToInt(1f / session.Dt);
            // A pose initialised at exact q_ref with zero velocity and zero
            // drive error is not an equilibrium, so release produces a short
            // transient. Constraint and foot gates apply after this window;
            // the release peaks are reported separately.
            int releaseEnd = Mathf.Min(samples.Count - 1, Mathf.RoundToInt(ReleaseWindowSeconds / session.Dt));
            float releasePeakAnchor = 0f, releasePeakFootTravel = 0f;
            float worstMargin = float.PositiveInfinity, worstNominal = 0f, worstAnchor = 0f, worstDemand = 0f,
                worstLimit = 0f, worstSaddleSep = 0f, worstSaddleOcc = 0f, worstFootTravel = 0f, tailBarSpeed = 0f, tailComSpeed = 0f;
            bool feetHeld = true;
            string worstNominalJoint = "NONE";
            AthleteSample released = samples[releaseEnd];
            for (int i = 0; i < samples.Count; i++)
            {
                AthleteSample s = samples[i];
                float travelFromStart = Mathf.Max(Vector3.Distance(s.LeftFoot, first.LeftFoot), Vector3.Distance(s.RightFoot, first.RightFoot));
                if (i < releaseEnd)
                {
                    releasePeakAnchor = Mathf.Max(releasePeakAnchor, s.MaxAnchorSeparation);
                    releasePeakFootTravel = Mathf.Max(releasePeakFootTravel, travelFromStart);
                }
                else
                {
                    worstAnchor = Mathf.Max(worstAnchor, s.MaxAnchorSeparation);
                    worstFootTravel = Mathf.Max(worstFootTravel,
                        Mathf.Max(Vector3.Distance(s.LeftFoot, released.LeftFoot), Vector3.Distance(s.RightFoot, released.RightFoot)));
                }
                if (i >= 20)
                    feetHeld &= s.BothFeet;
                if (i < tailStart)
                    continue;
                worstMargin = Mathf.Min(worstMargin, s.SupportMargin);
                if (s.MaxNominalError > worstNominal)
                {
                    worstNominal = s.MaxNominalError;
                    worstNominalJoint = s.MaxNominalErrorJoint;
                }
                worstDemand = Mathf.Max(worstDemand, s.MaxDemandFraction);
                worstLimit = Mathf.Max(worstLimit, s.MaxLimitProximity);
                worstSaddleSep = Mathf.Max(worstSaddleSep, s.SaddleSeparation);
                worstSaddleOcc = Mathf.Max(worstSaddleOcc, s.SaddleOccupancy);
                if (!float.IsNaN(s.Bar.x))
                    tailBarSpeed = Mathf.Max(tailBarSpeed, s.BarVelocity.magnitude);
                tailComSpeed = Mathf.Max(tailComSpeed, new Vector2(s.ComVelocity.x, s.ComVelocity.z).magnitude);
            }
            float pelvisDrop = first.PelvisY - last.PelvisY;
            double settleTime = double.NaN;
            for (int i = samples.Count - 1; i >= 0; i--)
            {
                if (new Vector2(samples[i].ComVelocity.x, samples[i].ComVelocity.z).magnitude > SettledComSpeedMps)
                    break;
                settleTime = samples[i].Time;
            }

            void Gate(string metric, double expected, double observed, double tol, ToleranceKind kind, string source, string meaning, CausalLayer layer)
            {
                if (qualifying)
                    rec.Record(metric, cfg, expected, observed, tol, kind, source, meaning, layer);
                else
                    rec.Info(metric, cfg, observed, meaning, layer);
            }

            Gate("feet_in_contact_after_0p2s", 1, feetHeld ? 1 : 0, 0, ToleranceKind.Absolute,
                "bilateral plantar contact (GAM-12 PLANTAR_SUPPORT_LOST)", "The athlete loses plantar support.", CausalLayer.AthleteEquilibrium);
            Gate("foot_travel_m", 0, worstFootTravel, MaximumFootTravelM, ToleranceKind.UpperBound,
                "engineering: planted feet move < 5 mm after the 0.5 s release window", "Feet slide or rock under the held pose.", CausalLayer.ContactFriction);
            Gate("pelvis_drop_m", 0, pelvisDrop, MaximumPelvisDropM, ToleranceKind.UpperBound,
                "engineering: held pose sags < 5 cm", "The athlete collapses out of the canonical pose.", CausalLayer.AthleteEquilibrium);
            Gate("tail_support_margin_m", MinimumComSupportMarginM, worstMargin, MinimumComSupportMarginM, ToleranceKind.LowerBound,
                "sealed GAM-13 MinimumComSupportMarginM", "COM leaves the support polygon.", CausalLayer.AthleteEquilibrium);
            Gate("tail_nominal_tracking_error_rad", 0, worstNominal, MaximumNominalTrackingErrorRad, ToleranceKind.UpperBound,
                "GAM-13 V2-3M product tolerance <= 0.10 rad (worst joint: " + worstNominalJoint + ")",
                "The physical athlete does not realise canonical q_ref.", CausalLayer.AthleteEquilibrium);
            Gate("max_anchor_separation_m", 0, worstAnchor, MaximumAnchorSeparationM, ToleranceKind.UpperBound,
                "engineering: joint gaps < 1 cm after the 0.5 s release window", "Athlete joint constraints stretch.", CausalLayer.ConstraintConvergence);
            Gate("tail_max_demand_fraction", 0, worstDemand, MaximumDemandFraction, ToleranceKind.UpperBound,
                "sealed PoweredJointController.ModeledDemandSaturationThreshold", "A drive saturates holding a static pose.",
                CausalLayer.AthleteEquilibrium);
            Gate("tail_max_limit_proximity", 0, worstLimit, MaximumLimitProximity, ToleranceKind.UpperBound,
                "sealed GAM-13 MaximumAllowedLimitProximity", "A joint rests on its limit.", CausalLayer.JointTopologyTargetConvention);
            Gate("tail_com_speed_mps", 0, tailComSpeed, SettledComSpeedMps, ToleranceKind.UpperBound,
                "engineering: settled horizontal COM speed < 0.020 m/s over the last second", "The held pose does not settle.",
                CausalLayer.AthleteEquilibrium);
            if (session.Controller.Saddle != null)
            {
                Gate("tail_saddle_separation_m", 0, worstSaddleSep, MaximumSaddleSeparationM, ToleranceKind.UpperBound,
                    "sealed GAM-13 MaximumSaddleSeparationM", "The bar leaves the saddle.", CausalLayer.BarSaddleLoadPath);
                Gate("tail_saddle_occupancy", 0, worstSaddleOcc, MaximumSaddleLimitOccupancy, ToleranceKind.UpperBound,
                    "sealed GAM-13 MaximumSaddleLimitOccupancy", "The saddle rides its hard limit.", CausalLayer.BarSaddleLoadPath);
                Gate("tail_bar_speed_mps", 0, tailBarSpeed, BarStillnessMps, ToleranceKind.UpperBound,
                    "sealed GAM-12 lockout bar stillness 0.020 m/s (a held pose must be able to meet it)",
                    "A statically held athlete cannot keep the bar still enough for lockout.", CausalLayer.AthleteEquilibrium);
            }
            rec.Info("settle_time_s", cfg, settleTime, "First time after which COM speed stays below 0.020 m/s.", CausalLayer.AthleteEquilibrium);
            rec.Info("release_peak_anchor_separation_m", cfg, releasePeakAnchor,
                "Largest joint gap during the release window.", CausalLayer.ConstraintConvergence);
            rec.Info("release_peak_foot_travel_m", cfg, releasePeakFootTravel,
                "Largest foot displacement during the release window.", CausalLayer.ContactFriction);
            rec.Info("final_failure_reason_com_outside_support", cfg, last.FailureReason == "COM_OUTSIDE_SUPPORT" ? 1 : 0,
                "Adapter latched COM_OUTSIDE_SUPPORT during the hold.", CausalLayer.AthleteEquilibrium);
        }

        private static void RecordSaddleReplicaParity(PhysicsBenchmarkRecorder rec, AthleteBenchmarkSession session)
        {
            ConfigurableJoint live = session.Controller.Saddle.Joint;
            Rigidbody bar = session.Barbell.Body;
            string cfg = session.Cfg("saddle_replica_parity");
            void Same(string metric, double expected, double observed) =>
                rec.Record(metric, cfg, expected, observed, 1e-6, ToleranceKind.Absolute,
                    "SaddleReplica must equal the live production saddle", "B10/B11 isolated saddle replica drifted from production.",
                    CausalLayer.BarSaddleLoadPath);
            Same("linear_spring", SquatBarSaddle.DefaultLinearSpring, live.yDrive.positionSpring);
            Same("linear_damper", SquatBarSaddle.DefaultLinearDamper, live.yDrive.positionDamper);
            Same("linear_max_force", SquatBarSaddle.DefaultLinearMaxForce, live.yDrive.maximumForce);
            Same("linear_limit", SquatBarSaddle.DefaultLinearLimitM, live.linearLimit.limit);
            Same("angular_spring", SquatBarSaddle.DefaultAngularSpring, live.angularXDrive.positionSpring);
            Same("connected_anchor_dist", 0, Vector3.Distance(live.connectedAnchor, SquatBarSaddle.ThoraxLocalAnchor));
            Same("bar_solver_iterations", 12, bar.solverIterations);
            Same("bar_solver_velocity_iterations", 6, bar.solverVelocityIterations);
            Same("bar_linear_damping", 0.035, bar.linearDamping);
        }
    }
}
