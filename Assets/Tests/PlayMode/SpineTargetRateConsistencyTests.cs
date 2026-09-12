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
    /// Phase 5H16, sections 4, 5, 8 and 9.
    ///
    /// H15 gave the spine a phase-dependent gravity bias on its target
    /// POSITION. The target angular VELOCITY the drive receives is built by
    /// EvaluateReferenceRatePerPhase from the nominal GAM-10 frames alone. If
    /// that holds, the drive is told to travel along the nominal path while
    /// being asked to arrive on the nominal-plus-bias path, and the damper
    /// resists the difference for the whole descent.
    ///
    /// This does not take that on trust. It differences the composed target
    /// the adapter actually published, tick by tick, and compares the result
    /// against the command that was actually sent.
    ///
    /// The composition is left-multiplied, so omega(A*B) = omega(A) +
    /// Rot(A)*omega(B) and the bias rate must be rotated by the nominal before
    /// it is added. The candidate column applies that exactly so the claim can
    /// be checked rather than assumed.
    /// </summary>
    public sealed class SpineTargetRateConsistencyTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11/H16";
        private const float ApproachPhaseRate = 0.30f;

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
                SquatPhysicalAdapter adapter = _controller.Adapter;
                if (adapter != null)
                    adapter.SpineBiasRateFeedforwardEnabled = false;
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

        private struct Sample
        {
            public float Sq;
            public Quaternion AbdomenNominal;
            public Quaternion AbdomenBias;
            public Quaternion AbdomenComposed;
            public Vector3 AbdomenCommandRate;
            public Quaternion ThoraxNominal;
            public Quaternion ThoraxBias;
            public Quaternion ThoraxComposed;
            public Vector3 ThoraxCommandRate;
            public float AbdomenBiasSlopeDeg;
            public float ThoraxBiasSlopeDeg;
        }

        /// <summary>
        /// One descent, recording the published composition and the command
        /// that went with it every tick. Nominal and bias are pure functions
        /// of phase and load, so differencing consecutive recorded ticks is an
        /// exact oracle for the deterministic part of the target.
        /// </summary>
        private IEnumerator RecordDescent(float loadKg, bool feedforward, List<Sample> samples)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            PoweredJointController powered = _rig.PoweredController;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;

            adapter.Preload.Enabled = true;
            adapter.SpineBiasRateFeedforwardEnabled = feedforward;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
            for (int i = 0; i < 60; i++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
            }

            float sq = 0f;
            int tick = 0;
            while (tick < 340)
            {
                sq = Mathf.Min(1f, sq + ApproachPhaseRate * dt);
                SquatState state = sq >= 0.999f ? SquatState.BOTTOM : SquatState.DESCENT;
                adapter.HoldReferencePhaseForQualification(sq, SquatPhaseDirection.Descent, state, ApproachPhaseRate);
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                TickFeet(dt);
                tick++;

                adapter.TryGetTargetComposition("abdomen", out var abdomen);
                adapter.TryGetTargetComposition("thorax", out var thorax);
                samples.Add(new Sample
                {
                    Sq = sq,
                    AbdomenNominal = abdomen.Nominal,
                    AbdomenBias = abdomen.GravityBias,
                    AbdomenComposed = abdomen.Nominal * abdomen.GravityBias,
                    AbdomenCommandRate = powered.GetJoint("abdomen").Diagnostic.TargetAngularVelocityRadS,
                    ThoraxNominal = thorax.Nominal,
                    ThoraxBias = thorax.GravityBias,
                    ThoraxComposed = thorax.Nominal * thorax.GravityBias,
                    ThoraxCommandRate = powered.GetJoint("thorax").Diagnostic.TargetAngularVelocityRadS,
                    AbdomenBiasSlopeDeg = adapter.Preload.SpineBiasRateDegreesPerPhase(
                        SquatJointFamily.Abdomen, sq, loadKg),
                    ThoraxBiasSlopeDeg = adapter.Preload.SpineBiasRateDegreesPerPhase(
                        SquatJointFamily.Thorax, sq, loadKg)
                });
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator A1_SPINE_TARGET_RATE_CONSISTENCY()
        {
            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H16 A1 SPINE TARGET POSITION AND RATE CONSISTENCY");
            report.AppendLine("Central difference of the published NOMINAL * GRAVITY_BIAS target against");
            report.AppendLine("the target angular velocity actually commanded, same tick, same descent.");
            report.AppendLine("Rates are the sagittal component in rad/s at the production phase rate.");
            report.AppendLine("residual = commanded minus the composed-target oracle.");
            report.AppendLine();

            var csv = new StringBuilder();
            csv.AppendLine("load_kg,feedforward,sq,abd_fd_nominal_x,abd_fd_composed_x,abd_command_x," +
                           "abd_candidate_x,abd_residual_x,abd_bias_slope_deg_per_phase,abd_bias_rate_rad_s," +
                           "tho_fd_nominal_x,tho_fd_composed_x,tho_command_x,tho_candidate_x,tho_residual_x," +
                           "tho_bias_slope_deg_per_phase,tho_bias_rate_rad_s");

            foreach (float load in new[] { 0f, 25f })
            {
                foreach (bool feedforward in new[] { false, true })
                {
                    var samples = new List<Sample>();
                    yield return RecordDescent(load, feedforward, samples);
                    Analyse(load, feedforward, samples, report, csv);
                }
            }

            WriteMeasurement("spine-bias-rate-audit.csv", csv.ToString());
            WriteMeasurement("spine-bias-rate-audit.txt", report.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        private static void Analyse(
            float loadKg, bool feedforward, List<Sample> samples, StringBuilder report, StringBuilder csv)
        {
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            float deltaPhase = ApproachPhaseRate * dt;

            report.AppendLine("--- load " + loadKg.ToString("F0", CultureInfo.InvariantCulture) +
                              " kg, rate feedforward " + (feedforward ? "ON" : "OFF") + " ---");
            report.AppendLine("  s_q   abd: fdNom   fdComp  command  candidate  residual |" +
                              "  tho: fdNom   fdComp  command  candidate  residual");

            float worstAbdomenResidual = 0f;
            float worstThoraxResidual = 0f;
            float worstCandidateParity = 0f;

            var reportPhases = new List<float> { 0.10f, 0.25f, 0.40f, 0.55f, 0.70f, 0.80f, 0.90f, 1.00f };
            int nextReport = 0;

            for (int i = 1; i < samples.Count - 1; i++)
            {
                Sample previous = samples[i - 1];
                Sample current = samples[i];
                Sample next = samples[i + 1];
                float span = 2f * deltaPhase;

                Vector3 fdNominalAbdomen = RatePerPhase(previous.AbdomenNominal, next.AbdomenNominal, span) * ApproachPhaseRate;
                Vector3 fdComposedAbdomen = RatePerPhase(previous.AbdomenComposed, next.AbdomenComposed, span) * ApproachPhaseRate;
                Vector3 fdNominalThorax = RatePerPhase(previous.ThoraxNominal, next.ThoraxNominal, span) * ApproachPhaseRate;
                Vector3 fdComposedThorax = RatePerPhase(previous.ThoraxComposed, next.ThoraxComposed, span) * ApproachPhaseRate;

                // The exact left-composition rule, using the analytic table
                // slope rather than a difference.
                float abdomenBiasRate = current.AbdomenBiasSlopeDeg * Mathf.Deg2Rad * ApproachPhaseRate;
                float thoraxBiasRate = current.ThoraxBiasSlopeDeg * Mathf.Deg2Rad * ApproachPhaseRate;
                Vector3 candidateAbdomen = fdNominalAbdomen +
                    current.AbdomenNominal * (Vector3.right * abdomenBiasRate);
                Vector3 candidateThorax = fdNominalThorax +
                    current.ThoraxNominal * (Vector3.right * thoraxBiasRate);

                float abdomenResidual = current.AbdomenCommandRate.x - fdComposedAbdomen.x;
                float thoraxResidual = current.ThoraxCommandRate.x - fdComposedThorax.x;
                worstAbdomenResidual = Mathf.Max(worstAbdomenResidual, Mathf.Abs(abdomenResidual));
                worstThoraxResidual = Mathf.Max(worstThoraxResidual, Mathf.Abs(thoraxResidual));
                worstCandidateParity = Mathf.Max(worstCandidateParity,
                    Mathf.Abs(candidateAbdomen.x - fdComposedAbdomen.x));

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1},{2:F4},{3:F6},{4:F6},{5:F6},{6:F6},{7:F6},{8:F4},{9:F6}," +
                    "{10:F6},{11:F6},{12:F6},{13:F6},{14:F6},{15:F4},{16:F6}",
                    loadKg, feedforward ? 1 : 0, current.Sq,
                    fdNominalAbdomen.x, fdComposedAbdomen.x, current.AbdomenCommandRate.x,
                    candidateAbdomen.x, abdomenResidual,
                    current.AbdomenBiasSlopeDeg, abdomenBiasRate,
                    fdNominalThorax.x, fdComposedThorax.x, current.ThoraxCommandRate.x,
                    candidateThorax.x, thoraxResidual,
                    current.ThoraxBiasSlopeDeg, thoraxBiasRate));

                if (nextReport < reportPhases.Count && current.Sq >= reportPhases[nextReport] - 1e-3f)
                {
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "  {0,4:F2}     {1,7:F4} {2,8:F4} {3,8:F4} {4,10:F4} {5,9:F4} |" +
                        "      {6,7:F4} {7,8:F4} {8,8:F4} {9,10:F4} {10,9:F4}",
                        current.Sq,
                        fdNominalAbdomen.x, fdComposedAbdomen.x, current.AbdomenCommandRate.x,
                        candidateAbdomen.x, abdomenResidual,
                        fdNominalThorax.x, fdComposedThorax.x, current.ThoraxCommandRate.x,
                        candidateThorax.x, thoraxResidual));
                    nextReport++;
                }
            }

            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  worst |command - composed oracle|  abdomen {0:F5} rad/s   thorax {1:F5} rad/s",
                worstAbdomenResidual, worstThoraxResidual));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "  worst |candidate - composed oracle| abdomen {0:F5} rad/s  " +
                "(this is the exactness of omega(A*B) = omega(A) + Rot(A) omega(B))",
                worstCandidateParity));
            report.AppendLine();
        }

        /// <summary>Same construction EvaluateReferenceRatePerPhase uses.</summary>
        private static Vector3 RatePerPhase(Quaternion from, Quaternion to, float phaseStep)
        {
            Quaternion delta = PoweredJointController.NormalizeCanonical(to * Quaternion.Inverse(from));
            float vectorMagnitude = Mathf.Sqrt(delta.x * delta.x + delta.y * delta.y + delta.z * delta.z);
            if (vectorMagnitude <= 1e-6f)
                return Vector3.zero;
            float angle = 2f * Mathf.Atan2(vectorMagnitude, Mathf.Clamp(delta.w, -1f, 1f));
            return new Vector3(delta.x, delta.y, delta.z) * (angle / vectorMagnitude) / phaseStep;
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
