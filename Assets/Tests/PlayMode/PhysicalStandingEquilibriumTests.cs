using System;
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
    /// Phase 5H. Asks whether the canonical GAM-10 standing pose can be made a
    /// finite-drive static equilibrium using only a bounded target-space
    /// preload, before any balance feedback is allowed to hide the answer.
    /// </summary>
    public sealed class PhysicalStandingEquilibriumTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements";

        private static readonly SquatJointFamily[] Families =
        {
            SquatJointFamily.Ankle,
            SquatJointFamily.Knee,
            SquatJointFamily.Hip,
            SquatJointFamily.Abdomen,
            SquatJointFamily.Thorax
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
        // Experiment A: immutable baseline. Nominal reference only.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator E1_EXPERIMENT_A_NOMINAL_ONLY_BASELINE()
        {
            SquatPhysicalAdapter adapter = PrepareRun(preloadEnabled: false, balanceEnabled: false);
            var trace = new StringBuilder();
            trace.AppendLine(TraceHeader());

            const int totalTicks = 120;
            for (int tick = 0; tick < totalTicks; tick++)
            {
                Advance(adapter);
                if (tick < 60 ? tick % 5 == 0 : tick % 10 == 0)
                    trace.AppendLine(TraceRow(tick, adapter));
            }
            trace.AppendLine(TraceRow(totalTicks, adapter));

            WriteMeasurement("GAM11-experiment-a-baseline.csv", trace.ToString());
            RunSummary summary = Summarize(adapter);
            Debug.Log("[E1 EXPERIMENT A baseline] " + summary + Environment.NewLine + trace);

            // The baseline is a record, not a target. It exists so the preload
            // result has something honest to be compared against.
            Assert.That(summary.InitialPelvisY, Is.GreaterThan(0.9f), "The athlete did not spawn standing.");
            yield return null;
        }

        // ---------------------------------------------------------------
        // Deterministic one-family-at-a-time preload sensitivity sweep.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator E2_PRELOAD_SENSITIVITY_IDENTIFICATION()
        {
            var trace = new StringBuilder();
            trace.AppendLine("family,bias_deg," + SummaryHeader());

            float[] biases = { -6f, -4f, -2f, 0f, 2f, 4f, 6f };
            foreach (SquatJointFamily family in Families)
            {
                foreach (float biasDeg in biases)
                {
                    SquatPhysicalAdapter adapter = PrepareRun(preloadEnabled: true, balanceEnabled: false);
                    adapter.Preload.Clear();
                    adapter.Preload.SetAnatomicalFlexionBiasDegrees(family, biasDeg);

                    for (int tick = 0; tick < 50; tick++)
                        Advance(adapter);

                    trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F1},{2}", family, biasDeg, Summarize(adapter)));
                    yield return null;
                }
            }

            WriteMeasurement("GAM11-preload-identification.csv", trace.ToString());
            Debug.Log("[E2 PRELOAD SENSITIVITY]" + Environment.NewLine + trace);
            yield return null;
        }

        [UnityTest]
        public IEnumerator E4_SPAWN_CONTACT_GEOMETRY()
        {
            // Baseline A shows zero plantar contacts for the first ten ticks
            // while the pelvis drops. If the athlete spawns clear of the
            // platform then no target-space preload can act, because there is
            // nothing to push against until it lands.
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = false;
            adapter.Preload.Enabled = false;

            var report = new StringBuilder();
            report.AppendLine("tick,pelvis_y,left_foot_min_y,right_foot_min_y,left_foot_vel_y,contacts");

            float leftMinAtSpawn = FootColliderMinY("left_foot");
            float rightMinAtSpawn = FootColliderMinY("right_foot");

            for (int tick = 0; tick <= 20; tick++)
            {
                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F4},{2:F4},{3:F4},{4:F4},{5}",
                    tick, _rig.Segments["pelvis"].Body.position.y,
                    FootColliderMinY("left_foot"), FootColliderMinY("right_foot"),
                    _rig.Segments["left_foot"].Body.linearVelocity.y,
                    adapter.Balance.SupportContactCount));
                _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            }

            float supportPlaneY = adapter.Balance.SupportPlaneY;
            float spawnGap = Mathf.Min(leftMinAtSpawn, rightMinAtSpawn) - supportPlaneY;
            Vector3 leftPlantar = adapter.LeftReferencePlantarAnchorWorld;
            Vector3 rightPlantar = adapter.RightReferencePlantarAnchorWorld;
            Vector3 footSize = _rig.Segments["left_foot"].DimensionsMeters;
            Bounds footBounds = _rig.Segments["left_foot"].Collider.bounds;
            string summary = string.Format(CultureInfo.InvariantCulture,
                "supportPlaneY={0:F4} leftFootMinAtSpawn={1:F4} rightFootMinAtSpawn={2:F4} spawnGap={3:F4} m " +
                "referencePlantarY left={4:F4} right={5:F4} colliderMinusPlantar={6:F4} " +
                "footColliderSize=({7:F4},{8:F4},{9:F4}) footBoundsZ=[{10:F4},{11:F4}]",
                supportPlaneY, leftMinAtSpawn, rightMinAtSpawn, spawnGap,
                leftPlantar.y, rightPlantar.y, leftMinAtSpawn - leftPlantar.y,
                footSize.x, footSize.y, footSize.z, footBounds.min.z, footBounds.max.z);
            WriteMeasurement("GAM11-spawn-contact-geometry.csv", report.ToString());
            Debug.Log("[E4 SPAWN CONTACT GEOMETRY] " + summary + Environment.NewLine + report);
            yield return null;
        }

        private float FootColliderMinY(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Collider == null)
                return float.NaN;
            return foot.Collider.bounds.min.y;
        }

        [UnityTest]
        public IEnumerator E5_COMBINED_PRELOAD_CANDIDATES()
        {
            // The single-family sweep says ankle dominates and the others are
            // worth about a fifth of it combined. This tests whether the
            // combination is actually additive, and whether the ankle keeps
            // paying off up to the 12 deg investigation boundary, rather than
            // extrapolating a straight line off four points.
            var trace = new StringBuilder();
            trace.AppendLine("ankle_deg,knee_deg,hip_deg,abdomen_deg,thorax_deg," + SummaryHeader());

            float[][] candidates =
            {
                new[] { 0f, 0f, 0f, 0f, 0f },
                new[] { -6f, 0f, 6f, 6f, 6f },
                new[] { -8f, 0f, 6f, 6f, 6f },
                new[] { -10f, 0f, 6f, 6f, 6f },
                new[] { -12f, 0f, 6f, 6f, 6f },
                new[] { -12f, 2f, 6f, 6f, 6f },
                new[] { -10f, 0f, 4f, 4f, 4f },
                new[] { -8f, 0f, 8f, 8f, 8f },
                new[] { -12f, 0f, 12f, 12f, 12f }
            };

            foreach (float[] candidate in candidates)
            {
                SquatPhysicalAdapter adapter = PrepareRun(preloadEnabled: true, balanceEnabled: false);
                adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Ankle, candidate[0]);
                adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Knee, candidate[1]);
                adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Hip, candidate[2]);
                adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Abdomen, candidate[3]);
                adapter.Preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Thorax, candidate[4]);

                for (int tick = 0; tick < 150; tick++)
                    Advance(adapter);

                trace.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F1},{1:F1},{2:F1},{3:F1},{4:F1},{5}",
                    candidate[0], candidate[1], candidate[2], candidate[3], candidate[4], Summarize(adapter)));
                yield return null;
            }

            WriteMeasurement("GAM11-preload-combined-candidates.csv", trace.ToString());
            Debug.Log("[E5 COMBINED PRELOAD CANDIDATES]" + Environment.NewLine + trace);
            yield return null;
        }

        // ---------------------------------------------------------------
        // Experiment B: nominal + preload, no balance feedback at all.
        // ---------------------------------------------------------------
        [UnityTest]
        public IEnumerator E3_EXPERIMENT_B_PRELOAD_ONLY_EQUILIBRIUM()
        {
            SquatPhysicalAdapter adapter = PrepareRun(preloadEnabled: true, balanceEnabled: false);
            adapter.Preload.CopyFrom(SquatEquilibriumPreload.QualifiedStanding());

            var trace = new StringBuilder();
            trace.AppendLine(TraceHeader());

            const int totalTicks = 500;
            for (int tick = 0; tick < totalTicks; tick++)
            {
                Advance(adapter);
                if (tick % 25 == 0 || tick == totalTicks - 1)
                    trace.AppendLine(TraceRow(tick, adapter));
            }

            WriteMeasurement("GAM11-experiment-b-preload-only.csv", trace.ToString());
            RunSummary summary = Summarize(adapter);
            Debug.Log("[E3 EXPERIMENT B preload only] " + summary + Environment.NewLine + trace);

            Assert.That(adapter.Preload.IsWithinHardBound(), Is.True,
                "The standing preload exceeds the 12 deg investigation boundary.");
            Assert.That(summary.LostContact, Is.False, "The athlete lost plantar contact. " + summary);
            Assert.That(summary.FinalPelvisY, Is.GreaterThan(summary.SettledPelvisY - 0.04f),
                "The pelvis kept sagging after the initial settle. " + summary);
            Assert.That(Mathf.Abs(summary.FinalComApVelocity), Is.LessThan(0.10f),
                "The centre of mass is still diverging. " + summary);
            Assert.That(summary.WorstCaptureMargin, Is.GreaterThan(0.01f),
                "The capture point left the support polygon. " + summary);
            Assert.That(summary.WorstFootPitchDeg, Is.LessThan(12f),
                "The foot is tipping. " + summary);
            Assert.That(summary.SustainedSaturationFraction, Is.LessThan(0.05f),
                "The actuators are sustained at their ceiling. " + summary);
            yield return null;
        }

        // ---------------------------------------------------------------
        // Shared harness.
        // ---------------------------------------------------------------
        private SquatPhysicalAdapter PrepareRun(bool preloadEnabled, bool balanceEnabled)
        {
            _controller.SetLoad(0f);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = balanceEnabled;
            adapter.AnkleSagittalOffsetOverrideRad = null;
            adapter.Preload.Enabled = preloadEnabled;
            adapter.Preload.Clear();
            ResetAccumulators();
            return adapter;
        }

        private float _initialPelvisY;
        private float _settledPelvisY;
        private float _minPelvisY;
        private float _worstCaptureMargin;
        private float _maxAbsComApVelocity;
        private float _worstFootPitchDeg;
        private float _copMinAp;
        private float _copMaxAp;
        private float _maxPostureErrorDeg;
        private int _saturatedTicks;
        private int _measuredTicks;
        private int _tickIndex;
        private bool _lostContact;

        private void ResetAccumulators()
        {
            _initialPelvisY = float.NaN;
            _settledPelvisY = float.NaN;
            _minPelvisY = float.PositiveInfinity;
            _worstCaptureMargin = float.PositiveInfinity;
            _maxAbsComApVelocity = 0f;
            _worstFootPitchDeg = 0f;
            _copMinAp = float.PositiveInfinity;
            _copMaxAp = float.NegativeInfinity;
            _maxPostureErrorDeg = 0f;
            _saturatedTicks = 0;
            _measuredTicks = 0;
            _tickIndex = 0;
            _lostContact = false;
        }

        private const int SettleTicks = 30;

        private void Advance(SquatPhysicalAdapter adapter)
        {
            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);

            SquatBalanceObserver balance = adapter.Balance;
            float pelvisY = _rig.Segments["pelvis"].Body.position.y;
            if (float.IsNaN(_initialPelvisY))
                _initialPelvisY = pelvisY;
            if (_tickIndex == SettleTicks)
                _settledPelvisY = pelvisY;

            if (_tickIndex >= SettleTicks)
            {
                _measuredTicks++;
                _minPelvisY = Mathf.Min(_minPelvisY, pelvisY);
                _worstCaptureMargin = Mathf.Min(_worstCaptureMargin,
                    Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                _maxAbsComApVelocity = Mathf.Max(_maxAbsComApVelocity, Mathf.Abs(balance.SystemComVelocity.z));
                _worstFootPitchDeg = Mathf.Max(_worstFootPitchDeg, Mathf.Abs(FootPitchDegrees("left_foot")));
                _maxPostureErrorDeg = Mathf.Max(_maxPostureErrorDeg, MaxPostureErrorDegrees(adapter));
                if (!balance.HasSupport)
                    _lostContact = true;
                if (balance.HasCopEstimate)
                {
                    _copMinAp = Mathf.Min(_copMinAp, balance.CopEstimate.z);
                    _copMaxAp = Mathf.Max(_copMaxAp, balance.CopEstimate.z);
                }
                if (adapter.MaxDriveSaturation >= 1f)
                    _saturatedTicks++;
            }
            _tickIndex++;
        }

        /// <summary>
        /// How far the ACTUAL physical pose has drifted from the canonical
        /// GAM-10 standing pose. The preload moves the target on purpose; it
        /// is only doing its job if the body stays put.
        /// </summary>
        private float MaxPostureErrorDegrees(SquatPhysicalAdapter adapter)
        {
            float worst = 0f;
            foreach (string jointId in new[] { "left_foot", "right_foot", "left_shank", "right_shank", "left_thigh", "right_thigh", "abdomen", "thorax" })
            {
                if (!adapter.TryGetTargetComposition(jointId, out SquatPhysicalAdapter.JointTargetComposition composition))
                    continue;
                Quaternion actual = _rig.PoweredController.GetJoint(jointId).Diagnostic.ActualRelative;
                worst = Mathf.Max(worst, Quaternion.Angle(composition.Nominal, actual));
            }
            return worst;
        }

        private RunSummary Summarize(SquatPhysicalAdapter adapter)
        {
            return new RunSummary
            {
                InitialPelvisY = _initialPelvisY,
                SettledPelvisY = _settledPelvisY,
                MinPelvisY = _minPelvisY,
                FinalPelvisY = _rig.Segments["pelvis"].Body.position.y,
                FinalComAp = adapter.Balance.SystemCom.z,
                FinalComApVelocity = adapter.Balance.SystemComVelocity.z,
                MaxAbsComApVelocity = _maxAbsComApVelocity,
                WorstCaptureMargin = _worstCaptureMargin,
                CopMinAp = _copMinAp,
                CopMaxAp = _copMaxAp,
                MaxDriveSaturation = adapter.MaxDriveSaturation,
                SustainedSaturationFraction = _measuredTicks > 0 ? _saturatedTicks / (float)_measuredTicks : 0f,
                MaxPostureErrorDeg = _maxPostureErrorDeg,
                WorstFootPitchDeg = _worstFootPitchDeg,
                LostContact = _lostContact
            };
        }

        private static string SummaryHeader() =>
            "initial_pelvis_y,settled_pelvis_y,min_pelvis_y,final_pelvis_y,initial_sag,final_com_ap," +
            "final_com_ap_vel,max_com_ap_vel,worst_capture_margin,cop_min_ap,cop_max_ap," +
            "max_saturation,sustained_saturation,max_posture_error_deg,worst_foot_pitch_deg,lost_contact";

        private struct RunSummary
        {
            public float InitialPelvisY;
            public float SettledPelvisY;
            public float MinPelvisY;
            public float FinalPelvisY;
            public float FinalComAp;
            public float FinalComApVelocity;
            public float MaxAbsComApVelocity;
            public float WorstCaptureMargin;
            public float CopMinAp;
            public float CopMaxAp;
            public float MaxDriveSaturation;
            public float SustainedSaturationFraction;
            public float MaxPostureErrorDeg;
            public float WorstFootPitchDeg;
            public bool LostContact;

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0:F4},{1:F4},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F3},{12:F3},{13:F2},{14:F2},{15}",
                InitialPelvisY, SettledPelvisY, MinPelvisY, FinalPelvisY,
                InitialPelvisY - SettledPelvisY, FinalComAp, FinalComApVelocity, MaxAbsComApVelocity,
                WorstCaptureMargin, CopMinAp, CopMaxAp, MaxDriveSaturation, SustainedSaturationFraction,
                MaxPostureErrorDeg, WorstFootPitchDeg, LostContact);
        }

        private static string TraceHeader() =>
            "tick,time_s,pelvis_y,com_ap,com_ap_vel,com_ml,com_ml_vel,cop_measured_ap,cop_desired_ap," +
            "cop_excursion_from_initial,cop_error,capture_ap,support_ap_rear,support_ap_front," +
            "capture_margin_rear,capture_margin_front,ankle_nominal_deg,ankle_gravity_deg,ankle_balance_deg," +
            "ankle_final_deg,ankle_actual_deg,knee_actual_deg,hip_actual_deg,abdomen_actual_deg,thorax_actual_deg," +
            "max_demand,foot_pitch_deg,contacts,slip_mps";

        private float _initialCopAp = float.NaN;

        private string TraceRow(int tick, SquatPhysicalAdapter adapter)
        {
            SquatBalanceObserver balance = adapter.Balance;
            SquatPredictiveBalanceController controller = adapter.BalanceController;
            float copMeasured = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;
            if (float.IsNaN(_initialCopAp) && !float.IsNaN(copMeasured))
                _initialCopAp = copMeasured;

            adapter.TryGetTargetComposition("left_foot", out SquatPhysicalAdapter.JointTargetComposition ankle);
            float slip = _controller.LeftFootContact != null ? _controller.LeftFootContact.SlipSpeed : 0f;

            return string.Format(CultureInfo.InvariantCulture,
                "{0},{1:F3},{2:F4},{3:F4},{4:F4},{5:F4},{6:F4},{7:F4},{8:F4},{9:F4},{10:F4},{11:F4},{12:F4},{13:F4}," +
                "{14:F4},{15:F4},{16:F2},{17:F2},{18:F2},{19:F2},{20:F2},{21:F2},{22:F2},{23:F2},{24:F2},{25:F3},{26:F2},{27},{28:F4}",
                tick, tick * SimulationConstants.FixedDeltaTimeSeconds,
                _rig.Segments["pelvis"].Body.position.y,
                balance.SystemCom.z, balance.SystemComVelocity.z,
                balance.SystemCom.x, balance.SystemComVelocity.x,
                copMeasured, controller.CopDesiredAp,
                float.IsNaN(_initialCopAp) ? 0f : copMeasured - _initialCopAp,
                controller.CopErrorAp,
                balance.CaptureAp, balance.SupportApMin, balance.SupportApMax,
                balance.CaptureMarginRear, balance.CaptureMarginFront,
                SagittalDegrees(ankle.Nominal), SagittalDegrees(ankle.GravityBias),
                SagittalDegrees(ankle.BalanceOffset), SagittalDegrees(ankle.Final),
                ActualDegrees("left_foot"), ActualDegrees("left_shank"), ActualDegrees("left_thigh"),
                ActualDegrees("abdomen"), ActualDegrees("thorax"),
                adapter.MaxDriveSaturation, FootPitchDegrees("left_foot"),
                balance.SupportContactCount, slip);
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
