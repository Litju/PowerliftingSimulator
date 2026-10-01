using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H2. The ground registration repair landed, and the open-loop
    /// preload identified before it was measured on a moving body rather than
    /// at equilibrium, so it is not adopted. The question this suite opens
    /// with is the one that decides everything after it: does the existing
    /// predictive balance controller hold the properly grounded plant when the
    /// feed-forward preload is zero?
    /// </summary>
    public sealed class PhysicalStandingClosedLoopTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

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

        // ---------------------------------------------------------------
        // Experiment C0. Nominal GAM-10 target, zero gravity preload,
        // predictive balance on, unloaded, s_q = 0. No gain changes: this
        // measures the controller as Phase 5E left it, on the repaired
        // ground, so that any later tuning has an honest starting point.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator C0_ZERO_PRELOAD_CLOSED_LOOP_BASELINE()
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.AnkleSagittalOffsetOverrideRad = null;
            adapter.Preload.Enabled = true;
            adapter.Preload.CopyFrom(SquatEquilibriumPreload.QualifiedStanding());

            var trace = new StringBuilder();
            trace.AppendLine(TraceHeader());

            const int totalTicks = 1000;
            int survivedTicks = totalTicks;
            string failureMode = "NONE";

            float worstCaptureMargin = float.PositiveInfinity;
            float maxAbsComApVelocity = 0f;
            float maxAbsComAp = 0f;
            float copMin = float.PositiveInfinity;
            float copMax = float.NegativeInfinity;
            float maxAnkleOffsetDeg = 0f;
            float maxSaturation = 0f;
            int saturatedTicks = 0;
            int measuredTicks = 0;
            float initialPelvisY = _rig.Segments["pelvis"].Body.position.y;
            float minPelvisY = initialPelvisY;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                Advance();
                SquatBalanceObserver balance = adapter.Balance;
                SquatPredictiveBalanceController control = adapter.BalanceController;
                float pelvisY = _rig.Segments["pelvis"].Body.position.y;

                if (tick < 100 ? tick % 5 == 0 : tick % 10 == 0)
                    trace.AppendLine(TraceRow(tick, adapter));

                if (tick >= SettleTicks)
                {
                    measuredTicks++;
                    minPelvisY = Mathf.Min(minPelvisY, pelvisY);
                    worstCaptureMargin = Mathf.Min(worstCaptureMargin,
                        Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                    maxAbsComApVelocity = Mathf.Max(maxAbsComApVelocity, Mathf.Abs(balance.SystemComVelocity.z));
                    maxAbsComAp = Mathf.Max(maxAbsComAp, Mathf.Abs(balance.SystemCom.z));
                    maxAnkleOffsetDeg = Mathf.Max(maxAnkleOffsetDeg,
                        Mathf.Abs(control.AnkleSagittalOffsetRad) * Mathf.Rad2Deg);
                    maxSaturation = Mathf.Max(maxSaturation, adapter.MaxDriveSaturation);
                    if (adapter.MaxDriveSaturation >= 1f)
                        saturatedTicks++;
                    if (balance.HasCopEstimate)
                    {
                        copMin = Mathf.Min(copMin, balance.CopEstimate.z);
                        copMax = Mathf.Max(copMax, balance.CopEstimate.z);
                    }
                }

                // A fall is not worth simulating past. Stop at the first
                // unambiguous sign and record which one arrived.
                if (pelvisY < initialPelvisY - 0.25f)
                {
                    failureMode = "PELVIS_COLLAPSE";
                    survivedTicks = tick + 1;
                    break;
                }
                if (!balance.HasSupport && tick > SettleTicks)
                {
                    failureMode = "LOST_CONTACT";
                    survivedTicks = tick + 1;
                    break;
                }
                if (Mathf.Abs(FootPitchDegrees("left_foot")) > 25f)
                {
                    failureMode = "FOOT_TIPPED";
                    survivedTicks = tick + 1;
                    break;
                }
            }

            trace.AppendLine(TraceRow(survivedTicks, adapter));
            WriteMeasurement("GAM11-c0-zero-preload-closed-loop.csv", trace.ToString());

            float durationSeconds = survivedTicks * (float)SimulationConstants.FixedDeltaTimeSeconds;

            // K_gravity for the reduced-order ankle-dominant standing mode.
            // GAME_CONTROL_REDUCED_ORDER_MODEL, not a claim about a human.
            float kGravity = adapter.Balance.SystemMassKg *
                SquatBalanceObserver.GravityMagnitudeMps2 * adapter.Balance.ComHeightM;

            string summary = string.Format(CultureInfo.InvariantCulture,
                "result={0} duration={1:F2}s ticks={2} failure={3} mass={4:F2}kg comHeight={5:F4}m " +
                "kGravity={6:F1}Nm/rad maxComAp={7:F4} maxComApVel={8:F4} worstCaptureMargin={9:F4} " +
                "cop=[{10:F4},{11:F4}] maxAnkleOffsetDeg={12:F2} maxSaturation={13:F3} " +
                "sustainedSaturation={14:F3} initialPelvisY={15:F4} minPelvisY={16:F4}",
                survivedTicks >= totalTicks ? "SURVIVED" : "FELL",
                durationSeconds, survivedTicks, failureMode,
                adapter.Balance.SystemMassKg, adapter.Balance.ComHeightM, kGravity,
                maxAbsComAp, maxAbsComApVelocity, worstCaptureMargin, copMin, copMax,
                maxAnkleOffsetDeg, maxSaturation,
                measuredTicks > 0 ? saturatedTicks / (float)measuredTicks : 0f,
                initialPelvisY, minPelvisY);

            Debug.Log("[C0 ZERO PRELOAD CLOSED LOOP] " + summary + Environment.NewLine + trace);

            // C0 is a measurement, not a gate. It only has to have run on a
            // grounded athlete for its answer to mean anything.
            Assert.That(initialPelvisY, Is.GreaterThan(0.9f), "The athlete did not spawn standing. " + summary);
            yield return null;
        }

        // ---------------------------------------------------------------
        // Local grounded system identification.
        //
        // Balance off, preload off, unloaded, s_q = 0, and a held ankle
        // target offset. The window is 0.10 s to 0.25 s after the grounded
        // solve, which is early enough that the body has not departed far
        // enough for the linearisation to be a fiction.
        //
        // Every case reloads the scene. The earlier sweeps reused one spawn
        // across all their conditions, so each case inherited the pose the
        // previous one fell into; that alone is enough to explain why the
        // preload identified from them did not survive contact with the
        // closed loop.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator S1_ANKLE_TARGET_AUTHORITY_IDENTIFICATION()
        {
            var trace = new StringBuilder();
            trace.AppendLine(
                "offset_deg,ankle_actual_deg,ankle_actual_vel_deg_s,solver_tau_left_nm,solver_tau_right_nm," +
                "solver_tau_combined_nm,cop_ap,com_ap,com_ap_vel,com_ap_accel,lean_deg,fz_n," +
                "pelvis_y,foot_pitch_deg,max_demand,contacts");

            float[] offsetsDeg = { -3f, -2f, -1f, 0f, 1f, 2f, 3f };
            var samples = new SystemIdSample[offsetsDeg.Length];

            for (int index = 0; index < offsetsDeg.Length; index++)
            {
                yield return LoadFixture();

                _controller.SetLoad(0f);
                SquatPhysicalAdapter adapter = _controller.Adapter;
                adapter.BalanceCorrectionsEnabled = false;
                adapter.Preload.Enabled = false;
                adapter.Preload.Clear();
                adapter.AnkleSagittalOffsetOverrideRad = offsetsDeg[index] * Mathf.Deg2Rad;

                for (int tick = 0; tick < WindowOpenTick; tick++)
                    Advance();

                float comApAtOpen = adapter.Balance.SystemCom.z;
                float comVelAtOpen = adapter.Balance.SystemComVelocity.z;

                for (int tick = WindowOpenTick; tick < WindowCloseTick; tick++)
                    Advance();

                SystemIdSample sample = Sample(adapter, offsetsDeg[index], comApAtOpen, comVelAtOpen);
                samples[index] = sample;
                trace.AppendLine(sample.ToString());
            }

            // Three different quantities, estimated independently. A slope
            // through all seven points, not a difference between two of them,
            // so a single noisy case cannot carry the answer.
            float gTargetTau = Slope(samples, s => s.OffsetRad, s => s.SolverTorqueCombinedNm);
            float kInner = -Slope(samples, s => s.LeanRad, s => s.SolverTorqueCombinedNm);
            float gTargetCop = Slope(samples, s => s.OffsetRad, s => s.CopAp);

            SquatBalanceObserver finalBalance = _controller.Adapter.Balance;
            float kGravity = finalBalance.SystemMassKg *
                SquatBalanceObserver.GravityMagnitudeMps2 * finalBalance.ComHeightM;

            var report = new StringBuilder();
            report.AppendLine(trace.ToString());
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_tau_Nm_per_rad,{0:F2}", gTargetTau));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# K_inner_effective_Nm_per_rad,{0:F2}", kInner));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# K_gravity_Nm_per_rad,{0:F2}", kGravity));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# K_inner_over_K_gravity,{0:F4}", kGravity > 0f ? kInner / kGravity : float.NaN));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_m_per_rad,{0:F5}", gTargetCop));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# authored_ankle_spring_per_joint_Nm_per_rad,{0:F1}", 650f));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_tau_over_authored_pair_spring,{0:F4}", gTargetTau / 1300f));

            WriteMeasurement("GAM11-ankle-system-identification.csv", report.ToString());
            Debug.Log("[S1 ANKLE SYSTEM IDENTIFICATION]" + Environment.NewLine + report);
            yield return null;
        }

        // ---------------------------------------------------------------
        // C1. The corrected ankle-only closed loop, from tick zero.
        //
        // Same outer law as C0, same GAM-7 plant, same zero preload. The one
        // change is that the ankle offset is now inverted through the
        // measured target-to-COP gain instead of the joint spring. Hip and
        // trunk are held off, so whatever this shows is the ankle's own
        // answer.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator C1_CORRECTED_ANKLE_ONLY_CLOSED_LOOP()
        {
            yield return LoadFixture();
            StandingRun run = RunStanding("GAM11-c1-corrected-ankle-only.csv", hipTrunkEnabled: false, postureGuardEnabled: true);
            Debug.Log("[C1 CORRECTED ANKLE ONLY] " + run.Summary + Environment.NewLine + run.Trace);

            Assert.That(run.InitialPelvisY, Is.GreaterThan(0.9f),
                "The athlete did not spawn standing. " + run.Summary);
            yield return null;
        }

        // ---------------------------------------------------------------
        // The gate has to reject the thing that fooled it.
        //
        // Ankle balance with the posture guard switched off is the exact
        // behaviour the owner rejected: the centre of mass stays over the
        // feet for the full ten seconds while the trunk folds onto its
        // anatomical limit. If a future change ever makes this pass again,
        // the gate has stopped measuring standing.
        // ---------------------------------------------------------------
        [Test]
        public void P1_COM_STABLE_BUT_POSTURE_FOLDED_MUST_FAIL()
        {
            // This used to run the plant and rely on it folding. It no longer
            // folds: after the actuator substrate repair the same
            // configuration holds 1.97 deg of posture error instead of 38.66,
            // with limit proximity 0.086 instead of 1.000.
            //
            // The guarantee still matters, so it is asserted against the
            // classifier directly rather than against a bug that has been
            // fixed. A run with clean balance and a folded trunk must never be
            // called a standing run, whatever the plant happens to do.
            var folded = new StandingRun
            {
                Survived = true,
                DurationSeconds = 10f,
                FailureMode = "NONE",
                WorstCaptureMargin = 0.118f,
                MaxAbsComAp = 0.033f,
                MaxAbsComApVelocity = 0.021f,
                SustainedSaturationFraction = 0f,
                MaxPostureErrorDeg = 38.66f,
                WorstPostureJoint = "abdomen",
                MaxLimitProximity = 1.000f,
                PinnedJoint = "abdomen"
            };

            Assert.That(folded.Verdict(), Is.Not.EqualTo("NONE"),
                "A centre of mass over the feet is not a standing run when the trunk is folded " +
                "onto its anatomical limit.");
            Assert.That(folded.Verdict(), Is.EqualTo("POSTURE_LIMIT"));

            // And the converse: balance failure must not be reported as a
            // posture problem, or the classification is useless for diagnosis.
            var fell = new StandingRun
            {
                Survived = false,
                FailureMode = "PELVIS_COLLAPSE",
                MaxPostureErrorDeg = 42f,
                MaxLimitProximity = 0.9f
            };
            Assert.That(fell.Verdict(), Is.EqualTo("BALANCE_LOST"));
        }

        // ---------------------------------------------------------------
        // The 10 second gate, three exact deterministic resets.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator G1_TEN_SECOND_STANDING_GATE_THREE_RESETS()
        {
            var runs = new StandingRun[3];
            for (int index = 0; index < runs.Length; index++)
            {
                yield return LoadFixture();
                runs[index] = RunStanding($"GAM11-standing-gate-run{index + 1}.csv", hipTrunkEnabled: true, postureGuardEnabled: true);
                Debug.Log($"[G1 STANDING RUN {index + 1}] " + runs[index].Summary);
            }

            float worstDuration = float.PositiveInfinity;
            foreach (StandingRun run in runs)
                worstDuration = Mathf.Min(worstDuration, run.DurationSeconds);

            var repeatability = new StringBuilder();
            repeatability.AppendLine("run," + StandingRun.Header());
            for (int index = 0; index < runs.Length; index++)
                repeatability.AppendLine($"{index + 1},{runs[index].Row()}");
            WriteMeasurement("GAM11-standing-gate-repeatability.csv", repeatability.ToString());
            Debug.Log("[G1 REPEATABILITY]" + Environment.NewLine + repeatability);

            foreach (StandingRun run in runs)
            {
                Assert.That(run.Survived, Is.True,
                    $"Fell after {run.DurationSeconds:F2} s ({run.FailureMode}). {run.Summary}");
                Assert.That(run.LostContact, Is.False, "Lost plantar contact. " + run.Summary);
                Assert.That(run.FinalPelvisY, Is.GreaterThan(run.SettledPelvisY - 0.04f),
                    "The pelvis kept sagging after the initial settle. " + run.Summary);
                Assert.That(run.WorstCaptureMargin, Is.GreaterThan(0f),
                    "The capture point left the support polygon. " + run.Summary);
                Assert.That(run.WorstFootPitchDeg, Is.LessThan(12f), "The foot is tipping. " + run.Summary);
                Assert.That(run.MaxSlipMps, Is.LessThan(0.05f), "The feet are slipping. " + run.Summary);
                Assert.That(run.SustainedSaturationFraction, Is.LessThan(0.05f),
                    "The drives are sustained at their ceiling. " + run.Summary);

                // Standing is a posture, not just a centre of mass over the
                // feet. Without this the gate passes an athlete folded double
                // at the waist, because folding keeps the pelvis high and the
                // COM over the support while every balance signal stays clean.
                Assert.That(run.MaxPostureErrorDeg, Is.LessThan(PostureThresholdDeg),
                    $"The physical pose left the canonical GAM-10 standing pose by " +
                    $"{run.MaxPostureErrorDeg:F1} deg at {run.WorstPostureJoint}. " + run.Summary);
                Assert.That(run.MaxLimitProximity, Is.LessThan(PinnedLimitProximity),
                    $"{run.PinnedJoint} is resting on its anatomical limit, so the limit is " +
                    $"holding the pose rather than the drive. " + run.Summary);
                Assert.That(run.Verdict(), Is.EqualTo("NONE"), run.Summary);
            }
            yield return null;
        }

        // ---------------------------------------------------------------
        // The calibration the controller is actually allowed to use.
        //
        // S1 established the target-to-COP gain; this repeats it twice, adds
        // the fit quality and the per-foot split, and is the artifact the
        // ankle mapping cites. Only the contact estimate is used. Solver
        // currentTorque stays out of it: Unity reports the torque needed to
        // satisfy every constraint on the joint, which is not the drive's
        // authority, and the project has kept it out of the command path
        // since GAM-7.
        //
        // GAME_PHYSICS_CALIBRATION. This is a property of this rig on this
        // platform in this engine, not a biomechanical constant.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator S2_G_TARGET_COP_CALIBRATION()
        {
            float[] offsetsDeg = { -3f, -2f, -1f, 0f, 1f, 2f, 3f };
            const int repeats = 2;

            var trace = new StringBuilder();
            trace.AppendLine("repeat,offset_deg,cop_ap_combined,cop_ap_left,cop_ap_right," +
                             "com_ap,com_minus_cop,fz_n,contacts_left,contacts_right,pelvis_y");

            var byRepeat = new CopSample[repeats][];
            for (int repeat = 0; repeat < repeats; repeat++)
            {
                byRepeat[repeat] = new CopSample[offsetsDeg.Length];
                for (int index = 0; index < offsetsDeg.Length; index++)
                {
                    yield return LoadFixture();
                    _controller.SetLoad(0f);
                    SquatPhysicalAdapter adapter = _controller.Adapter;
                    adapter.BalanceCorrectionsEnabled = false;
                    adapter.Preload.Enabled = false;
                    adapter.Preload.Clear();
                    adapter.AnkleSagittalOffsetOverrideRad = offsetsDeg[index] * Mathf.Deg2Rad;

                    for (int tick = 0; tick < WindowCloseTick; tick++)
                        Advance();

                    SquatBalanceObserver balance = adapter.Balance;
                    var sample = new CopSample
                    {
                        OffsetRad = offsetsDeg[index] * Mathf.Deg2Rad,
                        CopCombined = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                        CopLeft = FootCopAp(_controller.LeftFootContact),
                        CopRight = FootCopAp(_controller.RightFootContact),
                        ComAp = balance.SystemCom.z
                    };
                    byRepeat[repeat][index] = sample;

                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F1},{2:F6},{3:F6},{4:F6},{5:F6},{6:F6},{7:F1},{8},{9},{10:F4}",
                        repeat, offsetsDeg[index], sample.CopCombined, sample.CopLeft, sample.CopRight,
                        sample.ComAp, sample.ComAp - sample.CopCombined,
                        balance.TotalNormalImpulse / (float)SimulationConstants.FixedDeltaTimeSeconds,
                        _controller.LeftFootContact.CompletedContactCount,
                        _controller.RightFootContact.CompletedContactCount,
                        _rig.Segments["pelvis"].Body.position.y));
                }
            }

            LinearFit combined0 = Fit(byRepeat[0], s => s.OffsetRad, s => s.CopCombined);
            LinearFit combined1 = Fit(byRepeat[1], s => s.OffsetRad, s => s.CopCombined);
            LinearFit left = Fit(byRepeat[0], s => s.OffsetRad, s => s.CopLeft);
            LinearFit right = Fit(byRepeat[0], s => s.OffsetRad, s => s.CopRight);

            // The static shift the controller has to buy: at zero commanded
            // offset the COP sits behind the COM, and equilibrium needs it
            // underneath. This, not the width of the support polygon, is what
            // the ankle authority has to cover.
            CopSample atZero = byRepeat[0][3];
            float requiredShiftM = atZero.ComAp - atZero.CopCombined;
            float ankleAuthorityM = combined0.Slope * SquatPredictiveBalanceController.MaxAnkleSagittalOffsetRad;

            var report = new StringBuilder();
            report.AppendLine(trace.ToString());
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_combined_m_per_rad,{0:F5}", combined0.Slope));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_r2,{0:F6}", combined0.RSquared));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_max_abs_residual_m,{0:F7}", combined0.MaxAbsResidual));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_repeat_m_per_rad,{0:F5}", combined1.Slope));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_repeatability_rel,{0:F6}",
                Mathf.Abs(combined1.Slope - combined0.Slope) / Mathf.Max(1e-9f, Mathf.Abs(combined0.Slope))));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_left_m_per_rad,{0:F5}", left.Slope));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# G_target_cop_right_m_per_rad,{0:F5}", right.Slope));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# required_static_cop_shift_m,{0:F5}", requiredShiftM));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# ankle_cop_authority_at_bound_m,{0:F5}", ankleAuthorityM));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "# authority_margin_m,{0:F5}", ankleAuthorityM - requiredShiftM));

            WriteMeasurement("GAM11-g-target-cop-calibration.csv", report.ToString());
            Debug.Log("[S2 G_TARGET_COP CALIBRATION]" + Environment.NewLine + report);

            Assert.That(combined0.Slope, Is.GreaterThan(0f),
                "A more positive ankle target must move the centre of pressure forward. " +
                "If this sign flips, the ankle calibration is wrong and nothing downstream is safe.");
            Assert.That(combined0.RSquared, Is.GreaterThan(0.99f),
                "The target-to-COP response is not linear enough inside +/-3 deg for a single " +
                "scalar inversion; a piecewise calibration is required instead.");
            Assert.That(
                Mathf.Abs(combined1.Slope - combined0.Slope) / Mathf.Abs(combined0.Slope),
                Is.LessThan(0.02f),
                "The calibration did not repeat within 2 percent.");
            yield return null;
        }

        private static float FootCopAp(PhysicalFootContactDetector foot)
        {
            if (foot == null)
                return float.NaN;
            float impulse = 0f;
            float weighted = 0f;
            for (int index = 0; index < foot.CompletedContactCount; index++)
            {
                float normalImpulse = foot.CompletedNormalImpulse(index);
                impulse += normalImpulse;
                weighted += normalImpulse * foot.CompletedContactPoint(index).z;
            }
            return impulse > 1e-6f ? weighted / impulse : float.NaN;
        }

        private struct CopSample
        {
            public float OffsetRad;
            public float CopCombined;
            public float CopLeft;
            public float CopRight;
            public float ComAp;
        }

        private struct LinearFit
        {
            public float Slope;
            public float Intercept;
            public float RSquared;
            public float MaxAbsResidual;
        }

        private static LinearFit Fit(CopSample[] samples, Func<CopSample, float> x, Func<CopSample, float> y)
        {
            float sumX = 0f, sumY = 0f, sumXy = 0f, sumXx = 0f;
            int count = 0;
            foreach (CopSample sample in samples)
            {
                float xi = x(sample), yi = y(sample);
                if (!float.IsFinite(xi) || !float.IsFinite(yi))
                    continue;
                sumX += xi; sumY += yi; sumXy += xi * yi; sumXx += xi * xi; count++;
            }
            if (count < 2)
                return new LinearFit { Slope = float.NaN, Intercept = float.NaN, RSquared = float.NaN };

            float denominator = count * sumXx - sumX * sumX;
            float slope = Mathf.Abs(denominator) < 1e-12f ? float.NaN : (count * sumXy - sumX * sumY) / denominator;
            float intercept = (sumY - slope * sumX) / count;
            float meanY = sumY / count;

            float residualSquares = 0f, totalSquares = 0f, maxAbsResidual = 0f;
            foreach (CopSample sample in samples)
            {
                float xi = x(sample), yi = y(sample);
                if (!float.IsFinite(xi) || !float.IsFinite(yi))
                    continue;
                float residual = yi - (slope * xi + intercept);
                residualSquares += residual * residual;
                totalSquares += (yi - meanY) * (yi - meanY);
                maxAbsResidual = Mathf.Max(maxAbsResidual, Mathf.Abs(residual));
            }
            return new LinearFit
            {
                Slope = slope,
                Intercept = intercept,
                RSquared = totalSquares > 1e-18f ? 1f - residualSquares / totalSquares : float.NaN,
                MaxAbsResidual = maxAbsResidual
            };
        }

        // ---------------------------------------------------------------
        // Reset isolation. The identification above is only worth anything
        // if every case really does start from the same plant.
        //
        // adapter.Reset() clears controller state and leaves the rigidbodies
        // exactly where they were, so the sweeps that relied on it had each
        // case inherit the pose the previous one fell into. This pins the
        // reset that actually works so that cannot come back.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator R1_IDENTIFICATION_CANDIDATES_START_FROM_IDENTICAL_PLANT()
        {
            var first = new PlantSnapshot[2];
            var second = new PlantSnapshot[2];

            for (int trial = 0; trial < 2; trial++)
            {
                yield return LoadFixture();
                _controller.SetLoad(0f);
                SquatPhysicalAdapter adapter = _controller.Adapter;
                adapter.BalanceCorrectionsEnabled = false;
                adapter.Preload.Enabled = false;
                adapter.Preload.Clear();

                first[trial] = CapturePlant(adapter);

                // Drive the trial somewhere clearly different from spawn, so
                // a reset that does nothing would be obvious.
                adapter.AnkleSagittalOffsetOverrideRad = -8f * Mathf.Deg2Rad;
                for (int tick = 0; tick < 60; tick++)
                    Advance();
                second[trial] = CapturePlant(adapter);
            }

            Assert.That(second[0].PelvisY, Is.Not.EqualTo(first[0].PelvisY).Within(1e-4f),
                "The trial never left its spawn state, so this proves nothing about resetting.");

            AssertPlantEquivalent(first[0], first[1], "spawn state after a full fixture reload");

            // The same reset the contaminated sweeps used, for contrast.
            _controller.Adapter.Reset();
            PlantSnapshot afterControllerReset = CapturePlant(_controller.Adapter);
            Assert.That(
                Mathf.Abs(afterControllerReset.PelvisY - first[0].PelvisY),
                Is.GreaterThan(1e-3f),
                "adapter.Reset() appears to restore the physical bodies. If that is now true the " +
                "identification harness can be simplified, but it was not true when this was written.");

            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[R1 RESET ISOLATION] spawn pelvisY={0:F6}/{1:F6} disturbed pelvisY={2:F6} " +
                "afterAdapterReset pelvisY={3:F6}",
                first[0].PelvisY, first[1].PelvisY, second[0].PelvisY, afterControllerReset.PelvisY));
            yield return null;
        }

        private PlantSnapshot CapturePlant(SquatPhysicalAdapter adapter)
        {
            var snapshot = new PlantSnapshot
            {
                PelvisY = _rig.Segments["pelvis"].Body.position.y,
                ComAp = adapter.Balance.SystemCom.z,
                ComApVel = adapter.Balance.SystemComVelocity.z,
                Contacts = adapter.Balance.SupportContactCount,
                AnkleActualDeg = ActualDegrees("left_foot")
            };
            foreach (string id in PlantSegments)
            {
                Rigidbody body = _rig.Segments[id].Body;
                snapshot.Position += body.position;
                snapshot.Rotation += new Vector3(body.rotation.x, body.rotation.y, body.rotation.z);
                snapshot.LinearVelocity += body.linearVelocity;
                snapshot.AngularVelocity += body.angularVelocity;
            }
            return snapshot;
        }

        private static readonly string[] PlantSegments =
        {
            "pelvis", "abdomen", "thorax", "left_thigh", "right_thigh",
            "left_shank", "right_shank", "left_foot", "right_foot"
        };

        private static void AssertPlantEquivalent(PlantSnapshot a, PlantSnapshot b, string what)
        {
            Assert.That((a.Position - b.Position).magnitude, Is.LessThan(1e-5f), "Positions differ at " + what);
            Assert.That((a.Rotation - b.Rotation).magnitude, Is.LessThan(1e-5f), "Rotations differ at " + what);
            Assert.That((a.LinearVelocity - b.LinearVelocity).magnitude, Is.LessThan(1e-5f),
                "Linear velocities differ at " + what);
            Assert.That((a.AngularVelocity - b.AngularVelocity).magnitude, Is.LessThan(1e-5f),
                "Angular velocities differ at " + what);
            Assert.That(a.Contacts, Is.EqualTo(b.Contacts), "Contact state differs at " + what);
            Assert.That(a.AnkleActualDeg, Is.EqualTo(b.AnkleActualDeg).Within(1e-4f),
                "Controller-visible joint state differs at " + what);
        }

        private struct PlantSnapshot
        {
            public Vector3 Position;
            public Vector3 Rotation;
            public Vector3 LinearVelocity;
            public Vector3 AngularVelocity;
            public float PelvisY;
            public float ComAp;
            public float ComApVel;
            public int Contacts;
            public float AnkleActualDeg;
        }

        // ---------------------------------------------------------------
        // One standing run: unloaded, s_q = 0, zero preload, corrected ankle
        // mapping, hip and trunk off. 1000 ticks or a fall.
        // ---------------------------------------------------------------
        private StandingRun RunStanding(string measurementFilename, bool hipTrunkEnabled, bool postureGuardEnabled)
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.AnkleSagittalOffsetOverrideRad = null;
            adapter.Preload.Enabled = true;
            adapter.Preload.CopyFrom(SquatEquilibriumPreload.QualifiedStanding());
            adapter.BalanceController.HipTrunkStrategyEnabled = hipTrunkEnabled;
            adapter.BalanceController.PostureGuardEnabled = postureGuardEnabled;

            var trace = new StringBuilder();
            trace.AppendLine(TraceHeader());

            const int totalTicks = 1000;
            var run = new StandingRun
            {
                InitialPelvisY = _rig.Segments["pelvis"].Body.position.y,
                FailureMode = "NONE",
                WorstCaptureMargin = float.PositiveInfinity,
                CopMinAp = float.PositiveInfinity,
                CopMaxAp = float.NegativeInfinity,
                MinPostureGuardScale = 1f
            };
            run.MinPelvisY = run.InitialPelvisY;
            int survivedTicks = totalTicks;
            int saturatedTicks = 0;
            int measuredTicks = 0;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                Advance();
                SquatBalanceObserver balance = adapter.Balance;
                SquatPredictiveBalanceController control = adapter.BalanceController;
                float pelvisY = _rig.Segments["pelvis"].Body.position.y;

                if (tick < 100 ? tick % 5 == 0 : tick % 10 == 0)
                    trace.AppendLine(TraceRow(tick, adapter));

                if (tick == SettleTicks)
                    run.SettledPelvisY = pelvisY;
                if (tick >= SettleTicks)
                {
                    measuredTicks++;
                    run.MinPelvisY = Mathf.Min(run.MinPelvisY, pelvisY);
                    run.WorstCaptureMargin = Mathf.Min(run.WorstCaptureMargin,
                        Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                    run.MaxAbsComApVelocity = Mathf.Max(run.MaxAbsComApVelocity,
                        Mathf.Abs(balance.SystemComVelocity.z));
                    run.MaxAbsComAp = Mathf.Max(run.MaxAbsComAp, Mathf.Abs(balance.SystemCom.z));
                    run.MaxAnkleOffsetDeg = Mathf.Max(run.MaxAnkleOffsetDeg,
                        Mathf.Abs(control.AnkleSagittalOffsetRad) * Mathf.Rad2Deg);
                    run.MaxAnkleAuthorityFraction = Mathf.Max(run.MaxAnkleAuthorityFraction,
                        control.AnkleAuthorityFraction);
                    run.MaxSaturation = Mathf.Max(run.MaxSaturation, adapter.MaxDriveSaturation);
                    AccumulatePostureError(adapter, ref run);
                    run.MinPostureGuardScale = Mathf.Min(run.MinPostureGuardScale, control.PostureGuardScale);
                    run.MaxRawAnkleOffsetDeg = Mathf.Max(run.MaxRawAnkleOffsetDeg,
                        Mathf.Abs(control.RawAnkleSagittalOffsetRad) * Mathf.Rad2Deg);
                    run.WorstFootPitchDeg = Mathf.Max(run.WorstFootPitchDeg,
                        Mathf.Abs(FootPitchDegrees("left_foot")));
                    if (_controller.LeftFootContact != null)
                        run.MaxSlipMps = Mathf.Max(run.MaxSlipMps, _controller.LeftFootContact.SlipSpeed);
                    if (adapter.MaxDriveSaturation >= 1f)
                        saturatedTicks++;
                    if (!balance.HasSupport)
                        run.LostContact = true;
                    if (balance.HasCopEstimate)
                    {
                        run.CopMinAp = Mathf.Min(run.CopMinAp, balance.CopEstimate.z);
                        run.CopMaxAp = Mathf.Max(run.CopMaxAp, balance.CopEstimate.z);
                    }
                }

                if (pelvisY < run.InitialPelvisY - 0.25f)
                {
                    run.FailureMode = "PELVIS_COLLAPSE";
                    survivedTicks = tick + 1;
                    break;
                }
                if (!balance.HasSupport && tick > SettleTicks)
                {
                    run.FailureMode = "LOST_CONTACT";
                    survivedTicks = tick + 1;
                    break;
                }
                if (Mathf.Abs(FootPitchDegrees("left_foot")) > 25f)
                {
                    run.FailureMode = "FOOT_TIPPED";
                    survivedTicks = tick + 1;
                    break;
                }
            }

            trace.AppendLine(TraceRow(survivedTicks, adapter));
            WriteMeasurement(measurementFilename, trace.ToString());

            run.Survived = survivedTicks >= totalTicks;
            run.DurationSeconds = survivedTicks * (float)SimulationConstants.FixedDeltaTimeSeconds;
            run.FinalPelvisY = _rig.Segments["pelvis"].Body.position.y;
            run.FinalComAp = adapter.Balance.SystemCom.z;
            run.FinalComApVelocity = adapter.Balance.SystemComVelocity.z;
            run.SustainedSaturationFraction =
                measuredTicks > 0 ? saturatedTicks / (float)measuredTicks : 0f;
            run.Trace = trace.ToString();
            return run;
        }

        /// <summary>
        /// How far the ACTUAL physical pose has drifted from the canonical
        /// GAM-10 standing pose, per joint. Balance telemetry cannot see this:
        /// a body folded at the hip keeps its pelvis high and can hold its
        /// centre of mass over the feet, so it passes every COM, COP, capture
        /// and contact check while looking nothing like a person standing.
        /// </summary>
        private static readonly string[] PostureJoints =
        {
            "left_foot", "right_foot", "left_shank", "right_shank",
            "left_thigh", "right_thigh", "abdomen", "thorax"
        };

        private void AccumulatePostureError(SquatPhysicalAdapter adapter, ref StandingRun run)
        {
            foreach (string jointId in PostureJoints)
            {
                if (!adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition))
                    continue;
                PoweredJointDiagnostic diagnostic = _rig.PoweredController.GetJoint(jointId).Diagnostic;
                Quaternion actual = diagnostic.ActualRelative;
                if (diagnostic.LimitProximity > run.MaxLimitProximity)
                {
                    run.MaxLimitProximity = diagnostic.LimitProximity;
                    run.PinnedJoint = jointId;
                }
                float error = Quaternion.Angle(composition.Nominal, actual);
                if (error > run.MaxPostureErrorDeg)
                {
                    run.MaxPostureErrorDeg = error;
                    run.WorstPostureJoint = jointId;
                }
                if (jointId == "thorax")
                    run.TrunkPostureErrorDeg = Mathf.Max(run.TrunkPostureErrorDeg, error);
                if (jointId == "left_thigh")
                    run.HipPostureErrorDeg = Mathf.Max(run.HipPostureErrorDeg, error);
            }
        }

        // ---------------------------------------------------------------
        // D1. What the owner actually sees.
        //
        // The 10 second gate was written with the hip and trunk channel off,
        // and production runs it on. It also measured only balance, and a
        // body folded at the hip holds its centre of mass over the feet, so
        // every balance check passed while the athlete was bent double.
        //
        // This runs both configurations and reports posture next to balance,
        // so the difference between them is a measurement rather than an
        // argument.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator D1_STANDING_POSTURE_AGAINST_PRODUCTION_CONFIGURATION()
        {
            yield return LoadFixture();
            StandingRun hipOff = RunStanding("GAM11-posture-hip-off.csv", hipTrunkEnabled: false, postureGuardEnabled: true);

            yield return LoadFixture();
            StandingRun hipOn = RunStanding("GAM11-posture-hip-on.csv", hipTrunkEnabled: true, postureGuardEnabled: true);

            var report = new StringBuilder();
            report.AppendLine("configuration," + StandingRun.Header());
            report.AppendLine("hip_trunk_off," + hipOff.Row());
            report.AppendLine("hip_trunk_on_production," + hipOn.Row());
            WriteMeasurement("GAM11-standing-posture-comparison.csv", report.ToString());
            Debug.Log("[D1 STANDING POSTURE]" + Environment.NewLine + report);
            yield return null;
        }

        private struct StandingRun
        {
            public bool Survived;
            public float DurationSeconds;
            public string FailureMode;
            public float InitialPelvisY;
            public float SettledPelvisY;
            public float MinPelvisY;
            public float FinalPelvisY;
            public float FinalComAp;
            public float FinalComApVelocity;
            public float MaxAbsComAp;
            public float MaxAbsComApVelocity;
            public float WorstCaptureMargin;
            public float CopMinAp;
            public float CopMaxAp;
            public float MaxAnkleOffsetDeg;
            public float MaxAnkleAuthorityFraction;
            public float MaxSaturation;
            public float SustainedSaturationFraction;
            public float WorstFootPitchDeg;
            public float MaxSlipMps;
            public bool LostContact;
            public float MaxPostureErrorDeg;
            public string WorstPostureJoint;
            public float TrunkPostureErrorDeg;
            public float HipPostureErrorDeg;
            public float MaxLimitProximity;
            public string PinnedJoint;
            public float MinPostureGuardScale;
            public float MaxRawAnkleOffsetDeg;
            public string Trace;

            public static string Header() =>
                "survived,duration_s,failure,initial_pelvis_y,settled_pelvis_y,min_pelvis_y," +
                "final_pelvis_y,final_com_ap,final_com_ap_vel,max_com_ap,max_com_ap_vel," +
                "worst_capture_margin,cop_min_ap,cop_max_ap,max_ankle_offset_deg," +
                "max_ankle_authority_fraction,max_saturation,sustained_saturation," +
                "worst_foot_pitch_deg,max_slip_mps,lost_contact," +
                "max_posture_error_deg,worst_posture_joint,trunk_posture_error_deg,hip_posture_error_deg," +
                "max_limit_proximity,pinned_joint,min_posture_guard_scale,max_raw_ankle_offset_deg,verdict";

            public string Row() => string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F2},{2},{3:F4},{4:F4},{5:F4},{6:F4},{7:F5},{8:F5},{9:F5},{10:F5}," +
                "{11:F5},{12:F5},{13:F5},{14:F2},{15:F3},{16:F3},{17:F3},{18:F2},{19:F4},{20}," +
                "{21:F2},{22},{23:F2},{24:F2},{25:F3},{26},{27:F3},{28:F2},{29}",
                Survived, DurationSeconds, FailureMode, InitialPelvisY, SettledPelvisY, MinPelvisY,
                FinalPelvisY, FinalComAp, FinalComApVelocity, MaxAbsComAp, MaxAbsComApVelocity,
                WorstCaptureMargin, CopMinAp, CopMaxAp, MaxAnkleOffsetDeg, MaxAnkleAuthorityFraction,
                MaxSaturation, SustainedSaturationFraction, WorstFootPitchDeg, MaxSlipMps, LostContact,
                MaxPostureErrorDeg, WorstPostureJoint ?? "NONE", TrunkPostureErrorDeg, HipPostureErrorDeg,
                MaxLimitProximity, PinnedJoint ?? "NONE", MinPostureGuardScale, MaxRawAnkleOffsetDeg,
                Verdict());

            /// <summary>
            /// Honest failure classification. A centre of mass over the feet
            /// is not a pass if the athlete got there by folding, so posture
            /// divergence and joint-limit pinning are named failures rather
            /// than absences of one.
            /// </summary>
            public string Verdict()
            {
                if (!Survived)
                    return FailureMode == "LOST_CONTACT" ? "CONTACT_LOST" : "BALANCE_LOST";
                if (MaxLimitProximity >= PinnedLimitProximity)
                    return "POSTURE_LIMIT";
                if (MaxPostureErrorDeg > PostureThresholdDeg)
                    return "POSTURE_DIVERGENCE";
                if (SustainedSaturationFraction >= 0.05f)
                    return "DRIVE_AUTHORITY_LIMIT";
                return "NONE";
            }

            public string Summary => Header() + " => " + Row();
        }

        private const int WindowOpenTick = 10;   // 0.10 s
        private const int WindowCloseTick = 25;  // 0.25 s

        private SystemIdSample Sample(
            SquatPhysicalAdapter adapter, float offsetDeg, float comApAtOpen, float comVelAtOpen)
        {
            SquatBalanceObserver balance = adapter.Balance;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            float windowSeconds = (WindowCloseTick - WindowOpenTick) * dt;

            PoweredJointDiagnostic left = _rig.PoweredController.GetJoint("left_foot").Diagnostic;
            PoweredJointDiagnostic right = _rig.PoweredController.GetJoint("right_foot").Diagnostic;

            // Lean of the system centre of mass about the support, which is
            // the angular displacement the inner restoring torque acts on.
            float ankleAp = balance.SupportApCenter;
            float leanRad = Mathf.Atan2(balance.SystemCom.z - ankleAp, Mathf.Max(1e-3f, balance.ComHeightM));

            return new SystemIdSample
            {
                OffsetDeg = offsetDeg,
                OffsetRad = offsetDeg * Mathf.Deg2Rad,
                AnkleActualDeg = ActualDegrees("left_foot"),
                AnkleActualVelDegPerS = left.ActualAngularVelocityRadS.x * Mathf.Rad2Deg,
                SolverTorqueLeftNm = left.SolverFlexionTorqueNm,
                SolverTorqueRightNm = right.SolverFlexionTorqueNm,
                SolverTorqueCombinedNm = left.SolverFlexionTorqueNm + right.SolverFlexionTorqueNm,
                CopAp = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                ComAp = balance.SystemCom.z,
                ComApVel = balance.SystemComVelocity.z,
                ComApAccel = (balance.SystemComVelocity.z - comVelAtOpen) / windowSeconds,
                LeanRad = leanRad,
                FzN = balance.TotalNormalImpulse / dt,
                PelvisY = _rig.Segments["pelvis"].Body.position.y,
                FootPitchDeg = FootPitchDegrees("left_foot"),
                MaxDemand = adapter.MaxDriveSaturation,
                Contacts = balance.SupportContactCount
            };
        }

        private struct SystemIdSample
        {
            public float OffsetDeg;
            public float OffsetRad;
            public float AnkleActualDeg;
            public float AnkleActualVelDegPerS;
            public float SolverTorqueLeftNm;
            public float SolverTorqueRightNm;
            public float SolverTorqueCombinedNm;
            public float CopAp;
            public float ComAp;
            public float ComApVel;
            public float ComApAccel;
            public float LeanRad;
            public float FzN;
            public float PelvisY;
            public float FootPitchDeg;
            public float MaxDemand;
            public int Contacts;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0:F1},{1:F3},{2:F3},{3:F3},{4:F3},{5:F3},{6:F5},{7:F5},{8:F5},{9:F5},{10:F4},{11:F1}," +
                "{12:F4},{13:F3},{14:F3},{15}",
                OffsetDeg, AnkleActualDeg, AnkleActualVelDegPerS,
                SolverTorqueLeftNm, SolverTorqueRightNm, SolverTorqueCombinedNm,
                CopAp, ComAp, ComApVel, ComApAccel, LeanRad * Mathf.Rad2Deg, FzN,
                PelvisY, FootPitchDeg, MaxDemand, Contacts);
        }

        /// <summary>Ordinary least squares slope, skipping non-finite pairs.</summary>
        private static float Slope(
            SystemIdSample[] samples, Func<SystemIdSample, float> x, Func<SystemIdSample, float> y)
        {
            float sumX = 0f, sumY = 0f, sumXy = 0f, sumXx = 0f;
            int count = 0;
            foreach (SystemIdSample sample in samples)
            {
                float xi = x(sample);
                float yi = y(sample);
                if (!float.IsFinite(xi) || !float.IsFinite(yi))
                    continue;
                sumX += xi;
                sumY += yi;
                sumXy += xi * yi;
                sumXx += xi * xi;
                count++;
            }
            if (count < 2)
                return float.NaN;
            float denominator = count * sumXx - sumX * sumX;
            return Mathf.Abs(denominator) < 1e-12f ? float.NaN : (count * sumXy - sumX * sumY) / denominator;
        }

        // ---------------------------------------------------------------
        // Shared harness.
        // ---------------------------------------------------------------
        /// <summary>
        /// Maximum deviation of any controlled joint from the canonical
        /// GAM-10 standing pose. GAME_CALIBRATION, and mine rather than the
        /// project's: the sibling suite computes the same quantity but never
        /// asserted on it, so there was no prior threshold to inherit. It is
        /// set where an owner would still read the athlete as standing.
        /// </summary>
        private const float PostureThresholdDeg = 10f;

        /// <summary>
        /// A joint resting on its anatomical limit is being held by the limit
        /// rather than by its drive, which is a different machine from the one
        /// the controller thinks it is commanding.
        /// </summary>
        private const float PinnedLimitProximity = 0.95f;

        private const int SettleTicks = 30;

        /// <summary>
        /// A full scene reload, which is the only reset that actually returns
        /// every dynamic body to its authored spawn pose.
        /// </summary>
        private IEnumerator LoadFixture()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            _bootstrap.enabled = false;
        }

        private void Advance()
        {
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static string TraceHeader() =>
            "tick,time_s,pelvis_y,com_ap,com_ap_vel,cop_measured_ap,cop_desired_ap,cop_error," +
            "capture_ap,support_ap_rear,support_ap_front,capture_margin_rear,capture_margin_front," +
            "ankle_nominal_deg,ankle_preload_deg,ankle_balance_deg,ankle_final_deg,ankle_actual_deg," +
            "hip_balance_deg,trunk_balance_deg,requested_ankle_torque_nm,normal_impulse,fz_estimate_n," +
            "max_demand,ankle_saturated,foot_pitch_deg,contacts,slip_mps";

        private string TraceRow(int tick, SquatPhysicalAdapter adapter)
        {
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController control = adapter.BalanceController;
            float copMeasured = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition ankle);
            float slip = _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f;

            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F3},{2:F4},{3:F5},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9:F5},{10:F5},{11:F5},{12:F5}," +
                "{13:F2},{14:F2},{15:F2},{16:F2},{17:F2},{18:F2},{19:F2},{20:F2},{21:F4},{22:F1}," +
                "{23:F3},{24},{25:F2},{26},{27:F4}",
                tick, tick * SimulationConstants.FixedDeltaTimeSeconds,
                _rig.Segments["pelvis"].Body.position.y,
                balance.SystemCom.z, balance.SystemComVelocity.z,
                copMeasured, control.CopDesiredAp, control.CopErrorAp,
                balance.CaptureAp, balance.SupportApMin, balance.SupportApMax,
                balance.CaptureMarginRear, balance.CaptureMarginFront,
                SagittalDegrees(ankle.Nominal), SagittalDegrees(ankle.GravityBias),
                SagittalDegrees(ankle.BalanceOffset), SagittalDegrees(ankle.Final),
                ActualDegrees("left_foot"),
                control.HipSagittalOffsetRad * Mathf.Rad2Deg,
                control.TrunkSagittalOffsetRad * Mathf.Rad2Deg,
                control.RequestedAnkleTorqueNm,
                balance.TotalNormalImpulse, balance.TotalNormalImpulse / dt,
                adapter.MaxDriveSaturation, control.IsAnkleOffsetSaturated ? 1 : 0,
                FootPitchDegrees("left_foot"), balance.SupportContactCount, slip);
        }

        private float ActualDegrees(string jointId) =>
            SagittalDegrees(_rig.PoweredController.GetJoint(jointId).Diagnostic.ActualRelative);

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return 0f;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static float SagittalDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude) * Mathf.Rad2Deg;
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }
    }
}
