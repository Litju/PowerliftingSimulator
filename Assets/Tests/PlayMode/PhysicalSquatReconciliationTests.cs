using System.Collections;
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
    /// GAM-11 reconciliation diagnostics. These tests tune nothing. They
    /// answer the two questions the owner rejection raised: is the physical
    /// adapter asking for the accepted GAM-10 movement family, and can the
    /// production scene actually hold it.
    /// </summary>
    public sealed class PhysicalSquatReconciliationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

        private static readonly string[] ControlledJoints =
        {
            "left_foot", "right_foot",
            "left_shank", "right_shank",
            "left_thigh", "right_thigh",
            "abdomen", "thorax"
        };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The qualification scene is missing from the project.");
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

            // Own the clock so the diagnostic advances deterministically, but
            // leave every gameplay component enabled so this stays the
            // production command path rather than a test-only rig.
            _bootstrap.enabled = false;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            // The production components keep ticking against a runtime that is
            // being torn down; that shutdown noise is not a test result.
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

        [Test]
        public void R1_REFERENCE_SOURCE_IS_GAM10_CANONICAL_V2()
        {
            SquatPhysicalAdapter adapter = _controller.Adapter;
            Assert.That(adapter.ReferenceProfileId, Is.EqualTo("CANONICAL_POWERLIFTING_SQUAT_V2_CLOSED_CHAIN"),
                "The physical adapter is not sourcing the owner-accepted GAM-10 reference profile.");

            foreach (float phase in new[] { 0f, 0.25f, 0.5f, 0.75f, 1f })
            {
                SquatReferencePose expected = SquatReferenceProfile.CanonicalPowerliftingSquatV1
                    .Evaluate(phase, SquatPhaseDirection.Descent);
                SquatReferencePose actual = adapter.ReferenceAnatomicalPose(phase, SquatPhaseDirection.Descent);
                Assert.That(actual.AnkleDorsiflexionRad, Is.EqualTo(expected.AnkleDorsiflexionRad).Within(1e-6f), "ankle @ " + phase);
                Assert.That(actual.KneeFlexionRad, Is.EqualTo(expected.KneeFlexionRad).Within(1e-6f), "knee @ " + phase);
                Assert.That(actual.HipFlexionRad, Is.EqualTo(expected.HipFlexionRad).Within(1e-6f), "hip @ " + phase);
                Assert.That(actual.TrunkFlexionRad, Is.EqualTo(expected.TrunkFlexionRad).Within(1e-6f), "trunk @ " + phase);
            }
        }

        [Test]
        public void R2_STANDING_LOGICAL_TARGET_IS_SPAWN_NEUTRAL()
        {
            SquatPhysicalAdapter adapter = _controller.Adapter;
            var report = new StringBuilder();
            float worst = 0f;
            string worstJoint = "none";

            foreach (string jointId in ControlledJoints)
            {
                Quaternion target = adapter.ReferenceLogicalTarget(jointId, 0f, SquatPhaseDirection.Descent);
                float degrees = Quaternion.Angle(Quaternion.identity, target);
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,-12} standing_logical_target_deg={1:F2} euler={2}", jointId, degrees, target.eulerAngles));
                if (degrees > worst)
                {
                    worst = degrees;
                    worstJoint = jointId;
                }
            }

            Debug.Log("[R2 STANDING TARGET vs SPAWN NEUTRAL]\n" + report);
            Assert.That(worst, Is.LessThan(6f),
                "At s_q = 0 the GAM-10 standing reference disagrees with the physical spawn neutral. Worst joint '" +
                worstJoint + "' is " + worst.ToString("F2", CultureInfo.InvariantCulture) +
                " deg off, so the athlete is commanded away from its authored standing pose before the squat starts.\n" + report);
        }

        [UnityTest]
        public IEnumerator R3_PRODUCTION_STANDING_HOLD_UNLOADED()
        {
            yield return RunStandingHold(0f, "unloaded");
        }

        [UnityTest]
        public IEnumerator R4_PRODUCTION_STANDING_HOLD_25KG()
        {
            yield return RunStandingHold(25f, "25kg");
        }

        private IEnumerator RunStandingHold(float loadKg, string label)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            Assert.That(adapter.State, Is.EqualTo(SquatState.SETUP));

            float standingPelvisY = _rig.Segments["pelvis"].Body.position.y;
            float minPelvisY = standingPelvisY;
            float maxApError = 0f;
            float maxAnkleCorrection = 0f;
            var trace = new StringBuilder();
            trace.AppendLine("tick,pelvis_y_m,ap_com_error_m,ml_com_error_m,ankle_balance_rad,max_drive_saturation");

            // Five seconds of the accepted standing pose. No squat input and
            // no phase advance, so this isolates whether the finite drives
            // plus the balance correction can hold the reference standing
            // posture at all.
            const int totalTicks = 500;
            for (int tick = 0; tick < totalTicks; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                float pelvisY = _rig.Segments["pelvis"].Body.position.y;
                if (pelvisY < minPelvisY)
                    minPelvisY = pelvisY;
                maxApError = Mathf.Max(maxApError, Mathf.Abs(adapter.ApComError));
                maxAnkleCorrection = Mathf.Max(maxAnkleCorrection, Mathf.Abs(adapter.BalanceCorrectionRad));
                if (tick % 25 == 0 || tick == totalTicks - 1)
                {
                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5:F3}",
                        tick, pelvisY, adapter.ApComError, adapter.MlComError,
                        adapter.BalanceCorrectionRad, adapter.MaxDriveSaturation));
                }
                if (tick % 50 == 0)
                    yield return null;
            }

            float finalPelvisY = _rig.Segments["pelvis"].Body.position.y;
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(
                Path.Combine(Path.GetFullPath(MeasurementDirectory), "GAM11-standing-hold-" + label + ".csv"),
                trace.ToString());
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[STANDING HOLD {0}] standing={1:F3} min={2:F3} final={3:F3} maxApErr={4:F3} maxAnkleCorr={5:F3}\n{6}",
                label, standingPelvisY, minPelvisY, finalPelvisY, maxApError, maxAnkleCorrection, trace));

            Assert.That(finalPelvisY, Is.GreaterThan(standingPelvisY - 0.08f), string.Format(CultureInfo.InvariantCulture,
                "The production scene cannot hold the accepted standing pose for 5 s at {0}: standing={1:F3} m, min={2:F3} m, final={3:F3} m, max AP COM error={4:F3} m, max ankle balance correction={5:F3} rad.",
                label, standingPelvisY, minPelvisY, finalPelvisY, maxApError, maxAnkleCorrection));
        }

        [Test]
        public void R5_BALANCE_CORRECTION_STAYS_WITHIN_ITS_DECLARED_BOUND()
        {
            // The declared bound is the clamp inside CalculateBalanceOffset.
            // Whatever the adapter finally applies to the ankle target must
            // respect it, or the balance layer outranks the reference.
            float saturated = SquatPhysicalAdapter.CalculateBalanceOffset(10f, 0f, 0.01f);
            Assert.That(Mathf.Abs(saturated), Is.EqualTo(SquatPhysicalAdapter.MaxBalanceCorrectionRad).Within(1e-4f));

            float appliedAnkleOffsetRad = Mathf.Abs(saturated) * SquatPhysicalAdapter.AnkleBalanceOffsetFactor;
            Assert.That(appliedAnkleOffsetRad, Is.LessThanOrEqualTo(SquatPhysicalAdapter.MaxBalanceCorrectionRad + 1e-4f),
                string.Format(CultureInfo.InvariantCulture,
                    "The ankle target offset actually applied ({0:F1} deg) exceeds the declared balance bound ({1:F1} deg). A balance correction wider than the reference ankle excursion redefines the movement family.",
                    appliedAnkleOffsetRad * Mathf.Rad2Deg,
                    SquatPhysicalAdapter.MaxBalanceCorrectionRad * Mathf.Rad2Deg));
        }
    }
}
