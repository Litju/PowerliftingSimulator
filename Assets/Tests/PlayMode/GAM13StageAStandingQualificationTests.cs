using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
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
    /// GAM-13 Stage-A qualification. The fixture deliberately stops before the
    /// attempt lifecycle: it qualifies the physical standing/setup substrate,
    /// including an independent upright/bar-height diagnostic, on one fixed
    /// production control plant.
    /// </summary>
    public sealed class GAM13StageAStandingQualificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int SettleTicks = 100;
        private const int TotalTicks = 600;
        private const float MaximumTrunkPitchRad = 0.70f;
        private const float MinimumPelvisHeightM = 0.90f;
        private const float MinimumCaptureMarginM = 0.01f;
        private const float MaximumComSpeedMps = 0.25f;
        private const float MaximumFootPitchDeg = 12f;
        private const float MaximumSustainedSaturationFraction = 0.05f;
        private const float MaximumCanonicalPoseErrorDeg = 10f;
        private const float MaximumLimitProximity = 0.95f;
        private const float MaximumSaddleSeparationM = 0.05f;
        private const float FixedCandidateImpedanceFactor = 5f;

        public static readonly float[] CanonicalStageALoadsKg = { 25f, 60f, 140f, 170f, 300f };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnityTest]
        [Explicit("GAM-13 Stage-A candidate qualification; currently blocked by measured heavy-load setup collapse.")]
        public IEnumerator GAM13_STAGE_A_FIXED_PLANT_STANDING_QUALIFICATION()
        {
            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            ApplyImpedanceExperiment();
            float[] loads = RequestedStageALoads();
            int repeats = RequestedStageARepeats();
            var results = new List<StandingResult>(loads.Length * repeats);
            try
            {
                foreach (float loadKg in loads)
                {
                    for (int repeat = 1; repeat <= repeats; repeat++)
                    {
                        yield return LoadFreshScene();
                        StandingResult result = RunStanding(loadKg);
                        result.Repeat = repeat;
                        results.Add(result);
                    }
                }
            }
            finally
            {
                RestoreProfiles(originalProfiles);
            }

            string artifact = string.Equals(
                Environment.GetEnvironmentVariable("GAM13_STAGE_A_IMPEDANCE"),
                FixedCandidateImpedanceFactor.ToString("R", CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
                ? "stage-a-standing-5x.csv"
                : "stage-a-standing-qualification.csv";
            WriteArtifact(results, artifact);
            foreach (StandingResult result in results)
            {
                Assert.That(result.IsPass, Is.True, result.Summary);
            }

            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 fixed 5x plant standing qualification; one candidate plant across all canonical loads.")]
        public IEnumerator GAM13_STAGE_A_FIXED_5X_PLANT_STANDING_QUALIFICATION()
        {
            string previous = Environment.GetEnvironmentVariable("GAM13_STAGE_A_IMPEDANCE");
            string previousRepeats = Environment.GetEnvironmentVariable("GAM13_STAGE_A_REPEATS");
            try
            {
                Environment.SetEnvironmentVariable(
                    "GAM13_STAGE_A_IMPEDANCE",
                    FixedCandidateImpedanceFactor.ToString("R", CultureInfo.InvariantCulture));
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_REPEATS", "3");
                yield return GAM13_STAGE_A_FIXED_PLANT_STANDING_QUALIFICATION();
            }
            finally
            {
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_IMPEDANCE", previous);
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_REPEATS", previousRepeats);
            }
        }

        [UnityTest]
        [Explicit("GAM-13 solver/timestep sensitivity diagnostic; excluded from default qualification suites.")]
        public IEnumerator GAM13_STAGE_A_SOLVER_SENSITIVITY()
        {
            string previousPosition = Environment.GetEnvironmentVariable("GAM13_STAGE_A_SOLVER_POSITION");
            string previousVelocity = Environment.GetEnvironmentVariable("GAM13_STAGE_A_SOLVER_VELOCITY");
            var rows = new StringBuilder();
            rows.AppendLine("solver_profile,load_kg,pass,summary");
            try
            {
                foreach (bool higherIterations in new[] { false, true })
                {
                    Environment.SetEnvironmentVariable("GAM13_STAGE_A_SOLVER_POSITION", higherIterations ? "24" : null);
                    Environment.SetEnvironmentVariable("GAM13_STAGE_A_SOLVER_VELOCITY", higherIterations ? "8" : null);
                    string profile = higherIterations ? "HIGHER_24_8" : "NATIVE_PRODUCTION";
                    foreach (float loadKg in new[] { 25f, 60f, 300f })
                    {
                        yield return LoadFreshScene();
                        StandingResult result = RunStanding(loadKg);
                        rows.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0},{1:R},{2},\"{3}\"",
                            profile, loadKg, result.IsPass,
                            result.Summary.Replace("\"", "'")));
                    }
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_SOLVER_POSITION", previousPosition);
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_SOLVER_VELOCITY", previousVelocity);
            }

            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Measurements/GAM-13");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "solver-sensitivity.csv"), rows.ToString());
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 closed-loop equilibrium identification grid; excluded from default qualification suites.")]
        public IEnumerator GAM13_STAGE_A_CLOSED_LOOP_EQUILIBRIUM_IDENTIFICATION_GRID()
        {
            string loadText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_IDENTIFICATION_LOAD") ?? "60";
            float loadKg = float.Parse(loadText, CultureInfo.InvariantCulture);
            string candidateText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_IDENTIFICATION_BIASES") ??
                "3:3;5:5;7:6;9:8;11:10;12:12";
            string impedanceText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_IMPEDANCE") ?? "3";
            string[] candidateTokens = candidateText.Split(';');

            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            float[] abdomen = SnapshotBiasTable(loadKg, true);
            float[] thorax = SnapshotBiasTable(loadKg, false);
            ApplyImpedanceExperiment();
            var results = new List<StandingResult>(candidateTokens.Length);
            var rows = new StringBuilder();
            rows.AppendLine("load_kg,abdomen_standing_bias_deg,thorax_standing_bias_deg,impedance_factor,pass,summary");

            try
            {
                for (int index = 0; index < candidateTokens.Length; index++)
                {
                    string[] parts = candidateTokens[index].Split(':');
                    Assert.That(parts.Length, Is.EqualTo(2), "Identification bias candidates must use abdomen:thorax pairs.");
                    float abdomenBias = float.Parse(parts[0], CultureInfo.InvariantCulture);
                    float thoraxBias = float.Parse(parts[1], CultureInfo.InvariantCulture);
                    SetStandingBiasTable(loadKg, true, abdomenBias);
                    SetStandingBiasTable(loadKg, false, thoraxBias);

                    yield return LoadFreshScene();
                    StandingResult result = RunStanding(loadKg);
                    results.Add(result);
                    rows.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:R},{1:R},{2:R},{3:R},{4},\"{5}\"",
                        loadKg, abdomenBias, thoraxBias, impedanceText, result.IsPass,
                        result.Summary.Replace("\"", "'")));
                }
            }
            finally
            {
                RestoreBiasTable(loadKg, true, abdomen);
                RestoreBiasTable(loadKg, false, thorax);
                RestoreProfiles(originalProfiles);
            }

            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Measurements/GAM-13");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "stage-a-equilibrium-identification.csv"), rows.ToString());
            Debug.Log("GAM13_STAGE_A_IDENTIFICATION" + Environment.NewLine + rows);
            Assert.That(results.Count, Is.EqualTo(candidateTokens.Length));
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 balance-plant identification; excluded from default qualification suites.")]
        public IEnumerator GAM13_STAGE_A_BALANCE_PLANT_IDENTIFICATION()
        {
            float[] offsetsDeg = { -3f, -2f, -1f, 0f, 1f, 2f, 3f };
            var rows = new StringBuilder();
            rows.AppendLine(
                "load_kg,offset_deg,actual_ankle_deg,cop_ap_m,com_ap_m,com_velocity_ap_mps," +
                "support_ap_min_m,support_ap_max_m,contacts,solver_torque_nm");

            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            try
            {
                ApplyImpedanceExperiment();
                foreach (float loadKg in CanonicalStageALoadsKg)
                {
                    foreach (float offsetDeg in offsetsDeg)
                    {
                        yield return LoadFreshScene();
                        _controller.SetLoad(loadKg);
                        SquatPhysicalAdapter adapter = _controller.Adapter;
                        adapter.BalanceCorrectionsEnabled = false;
                        adapter.Preload.Enabled = false;
                        adapter.Preload.SpineCalibrationEnabled = false;
                        adapter.AnkleSagittalOffsetOverrideRad = offsetDeg * Mathf.Deg2Rad;
                        adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

                        float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
                        for (int tick = 0; tick < 25; tick++)
                        {
                            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                            _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                            TickFeet(dt);
                        }

                        SquatBalanceObserver balance = adapter.Balance;
                        PoweredJointController.PoweredJointRuntime ankle =
                            _rig.PoweredController.GetJoint("left_foot");
                        float cop = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN;
                        rows.AppendLine(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0:R},{1:R},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8},{9:R}",
                            loadKg,
                            offsetDeg,
                            PoweredJointController.SignedTwistRadians(ankle.PostPhysicsDiagnostic.ActualRelative, Vector3.right) * Mathf.Rad2Deg,
                            cop,
                            balance.SystemCom.z,
                            balance.SystemComVelocity.z,
                            balance.SupportApMin,
                            balance.SupportApMax,
                            balance.SupportContactCount,
                            ankle.PostPhysicsDiagnostic.SolverFlexionTorqueNm));
                    }
                }
            }
            finally
            {
                RestoreProfiles(originalProfiles);
            }

            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Measurements/GAM-13");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "stage-a-balance-identification.csv"), rows.ToString());
            Debug.Log("GAM13_STAGE_A_BALANCE_IDENTIFICATION" + Environment.NewLine + rows);
            yield return null;
        }

        [UnityTest]
        [Explicit("GAM-13 closed-loop balance gain identification; excluded from default qualification suites.")]
        public IEnumerator GAM13_STAGE_A_COP_GAIN_GRID()
        {
            string loadText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_IDENTIFICATION_LOAD") ?? "60";
            float loadKg = float.Parse(loadText, CultureInfo.InvariantCulture);
            string gainsText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_COP_GAINS") ??
                "0.1,0.25,0.5,0.75,1,1.5,2,3,4";
            string targetToCopText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_TARGET_TO_COP") ?? "0.30";
            string previousGain = Environment.GetEnvironmentVariable("GAM13_STAGE_A_COP_GAIN");
            string previousTarget = Environment.GetEnvironmentVariable("GAM13_STAGE_A_TARGET_TO_COP");
            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            var rows = new StringBuilder();
            rows.AppendLine("load_kg,cop_gain,target_to_cop_m_per_rad,pass,summary");

            try
            {
                ApplyImpedanceExperiment();
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_TARGET_TO_COP", targetToCopText);
                foreach (string gainText in gainsText.Split(','))
                {
                    float gain = float.Parse(gainText, CultureInfo.InvariantCulture);
                    Environment.SetEnvironmentVariable("GAM13_STAGE_A_COP_GAIN", gain.ToString("R", CultureInfo.InvariantCulture));
                    yield return LoadFreshScene();
                    StandingResult result = RunStanding(loadKg);
                    rows.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:R},{1:R},{2:R},{3},\"{4}\"",
                        loadKg, gain, targetToCopText, result.IsPass,
                        result.Summary.Replace("\"", "'")));
                }
            }
            finally
            {
                RestoreProfiles(originalProfiles);
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_COP_GAIN", previousGain);
                Environment.SetEnvironmentVariable("GAM13_STAGE_A_TARGET_TO_COP", previousTarget);
            }

            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Measurements/GAM-13");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "stage-a-balance-gain-search.csv"), rows.ToString());
            Debug.Log("GAM13_STAGE_A_COP_GAIN_GRID" + Environment.NewLine + rows);
            yield return null;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The Stage-A qualification scene is missing.");
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

            _controller.enabled = false;
            _bootstrap.enabled = false;
        }

        private StandingResult RunStanding(float loadKg)
        {
            _controller.SetLoad(loadKg);
            ConfigureSolverExperiment(_rig, _controller);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            ConfigureBalanceExperiment(adapter.BalanceController);
            ConfigureLowerFamilyBiasExperiment(adapter.Preload);

            adapter.BalanceCorrectionsEnabled = BalanceExperimentEnabled();
            adapter.HoldReferencePhaseForQualification(
                0f,
                SquatPhaseDirection.None,
                SquatState.SETUP);

            var result = new StandingResult { LoadKg = loadKg };
            var supportPoints = new List<Vector3>(32);
            for (int tick = 0; tick < TotalTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(
                    0f,
                    SquatPhaseDirection.None,
                    SquatState.SETUP);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(dt);

                if (!_controller.ObservationCollector.HasLastSnapshot)
                    continue;

                SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
                SquatBalanceObserver balance = adapter.Balance;
                SquatPredictiveBalanceController control = adapter.BalanceController;
                float pelvisY = snapshot.PelvisPositionWorldMeters.Y;
                float trunkPitch = snapshot.TrunkWorldPitchRadians;
                float footPitch = FootPitchDegrees("left_foot");
                float saddleSeparation = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.SaddleSeparationMeters;
                supportPoints.Clear();
                AddCompletedContacts(_controller.LeftFootContact, supportPoints);
                AddCompletedContacts(_controller.RightFootContact, supportPoints);
                SquatSupportGeometry.Measurement capture = SquatSupportGeometry.Measure(
                    supportPoints,
                    new Vector2(balance.CaptureMl, balance.CaptureAp));

                if (!float.IsFinite(pelvisY) || !float.IsFinite(trunkPitch) || !float.IsFinite(saddleSeparation) ||
                    !IsFinite(balance.SystemCom) || !IsFinite(balance.SystemComVelocity) ||
                    !float.IsFinite(adapter.CanonicalPostureErrorRad) ||
                    !float.IsFinite(adapter.TargetActualDeflectionRad) ||
                    (balance.HasSupport && !float.IsFinite(capture.HullSignedMarginM)))
                    result.NonFinite = true;

                if (tick >= SettleTicks)
                {
                    result.MeasuredTicks++;
                    result.MinPelvisY = Mathf.Min(result.MinPelvisY, pelvisY);
                    result.MaxAbsTrunkPitchRad = Mathf.Max(result.MaxAbsTrunkPitchRad, Mathf.Abs(trunkPitch));
                    result.MaxAbsFootPitchDeg = Mathf.Max(result.MaxAbsFootPitchDeg, Mathf.Abs(footPitch));
                    result.MinCaptureApMarginM = Mathf.Min(
                        result.MinCaptureApMarginM,
                        Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                    if (float.IsFinite(capture.HullSignedMarginM))
                        result.MinCaptureHullMarginM = Mathf.Min(
                            result.MinCaptureHullMarginM,
                            capture.HullSignedMarginM);
                    result.MaxComSpeedMps = Mathf.Max(
                        result.MaxComSpeedMps,
                        new Vector2(balance.SystemComVelocity.x, balance.SystemComVelocity.z).magnitude);
                    result.MaxCanonicalPoseErrorDeg = Mathf.Max(
                        result.MaxCanonicalPoseErrorDeg,
                        adapter.CanonicalPostureErrorRad * Mathf.Rad2Deg);
                    result.MaxTargetActualDeflectionDeg = Mathf.Max(
                        result.MaxTargetActualDeflectionDeg,
                        adapter.TargetActualDeflectionRad * Mathf.Rad2Deg);
                    result.MaxLimitProximity = Mathf.Max(
                        result.MaxLimitProximity,
                        adapter.CanonicalPostureLimitProximity);
                    result.MinGuardScale = Mathf.Min(result.MinGuardScale, control.PostureGuardScale);
                    result.MaxAnkleOffsetDeg = Mathf.Max(
                        result.MaxAnkleOffsetDeg,
                        Mathf.Abs(control.AnkleSagittalOffsetRad) * Mathf.Rad2Deg);
                    result.MaxSaddleSeparationM = Mathf.Max(result.MaxSaddleSeparationM, saddleSeparation);
                    if (balance.HasSupport && float.IsFinite(control.ComRefAp))
                    {
                        float rearCopLimit = balance.SupportApMin +
                            SquatPredictiveBalanceController.SupportInteriorMarginM;
                        float rearCopAuthority = control.ComRefAp - rearCopLimit;
                        if (tick == SettleTicks)
                            result.RearwardCopAuthorityAtSettleM = rearCopAuthority;
                        result.MinRearwardCopAuthorityM = Mathf.Min(
                            result.MinRearwardCopAuthorityM, rearCopAuthority);
                    }
                    result.MinSupportContacts = Mathf.Min(result.MinSupportContacts, balance.SupportContactCount);
                    result.MinPelvisHeightAtSettle = tick == SettleTicks
                        ? pelvisY
                        : result.MinPelvisHeightAtSettle;
                    if (!balance.HasSupport)
                        result.SupportLost = true;
                    if (_controller.Saddle == null || _controller.Saddle.IsBroken || !_controller.Saddle.IsAttached)
                        result.SaddleUnstable = true;
                    if (adapter.MaxDriveSaturation >= 1f)
                        result.SaturatedTicks++;
                }
            }

            result.InitialPelvisY = _rig.Segments["pelvis"].Body.position.y;
            result.FinalPelvisY = _rig.Segments["pelvis"].Body.position.y;
            result.Upright = result.MinPelvisY > MinimumPelvisHeightM &&
                result.MaxAbsTrunkPitchRad < MaximumTrunkPitchRad;
            result.IsPass = result.MeasuredTicks == TotalTicks - SettleTicks &&
                !result.NonFinite &&
                !result.SupportLost &&
                !result.SaddleUnstable &&
                result.Upright &&
                result.MinCaptureHullMarginM > MinimumCaptureMarginM &&
                result.MaxComSpeedMps < MaximumComSpeedMps &&
                result.MaxAbsFootPitchDeg < MaximumFootPitchDeg &&
                result.SaturatedTicks / (float)result.MeasuredTicks < MaximumSustainedSaturationFraction &&
                result.MaxCanonicalPoseErrorDeg < MaximumCanonicalPoseErrorDeg &&
                result.MaxLimitProximity < MaximumLimitProximity &&
                result.MaxSaddleSeparationM < MaximumSaddleSeparationM;

            Debug.Log("GAM13_STAGE_A " + result.Summary);
            return result;
        }

        private void TickFeet(float dt)
        {
            if (_controller.LeftFootContact != null)
                _controller.LeftFootContact.PhysicsTickUpdate(dt);
            if (_controller.RightFootContact != null)
                _controller.RightFootContact.PhysicsTickUpdate(dt);
        }

        private static void AddCompletedContacts(
            PhysicalFootContactDetector detector,
            List<Vector3> contacts)
        {
            if (detector == null)
                return;
            for (int index = 0; index < detector.CompletedContactCount; index++)
                contacts.Add(detector.CompletedContactPoint(index));
        }

        private float FootPitchDegrees(string segmentId)
        {
            if (!_rig.Segments.TryGetValue(segmentId, out PhysicalAthleteRig.SegmentRuntime foot) || foot.Body == null)
                return float.NaN;
            Vector3 forward = foot.Body.rotation * Vector3.forward;
            return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static JointFamilyProfile[] ProfileArray() =>
            (JointFamilyProfile[])typeof(PoweredJointController)
                .GetField("Profiles", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static JointFamilyProfile[] SnapshotProfiles() => (JointFamilyProfile[])ProfileArray().Clone();

        private static float[] RequestedStageALoads()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_STAGE_A_LOADS");
            if (string.IsNullOrWhiteSpace(text))
                return (float[])CanonicalStageALoadsKg.Clone();

            string[] tokens = text.Split(',');
            var loads = new float[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
                loads[index] = float.Parse(tokens[index], CultureInfo.InvariantCulture);
            return loads;
        }

        private static int RequestedStageARepeats()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_STAGE_A_REPEATS");
            if (string.IsNullOrWhiteSpace(text))
                return 1;
            int repeats = int.Parse(text, CultureInfo.InvariantCulture);
            Assert.That(repeats, Is.InRange(1, 3));
            return repeats;
        }

        private static void RestoreProfiles(JointFamilyProfile[] original)
        {
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
                live[index] = original[index];
        }

        private static void ApplyImpedanceExperiment()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_STAGE_A_IMPEDANCE") ?? "1";
            float factor = float.Parse(text, CultureInfo.InvariantCulture);
            Assert.That(float.IsFinite(factor) && factor > 0f && factor <= 8f, Is.True,
                "GAM13_STAGE_A_IMPEDANCE must be a finite factor in (0,8].");
            if (Mathf.Abs(factor - 1f) <= 1e-6f)
                return;

            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
            {
                JointFamilyProfile profile = live[index];
                bool loadBearing = profile.Id == "ankle" || profile.Id == "knee" ||
                    profile.Id == "hip" || profile.Id == "trunk";
                if (!loadBearing)
                    continue;
                live[index] = new JointFamilyProfile(
                    profile.Id,
                    profile.Spring * factor,
                    profile.Damper * Mathf.Sqrt(factor),
                    profile.BaseCapacityNm,
                    profile.MaxTargetRateRadS);
            }
        }

        private static void ConfigureSolverExperiment(
            PhysicalAthleteRig rig,
            SquatPhysicalPrototypeController controller)
        {
            string positionText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_SOLVER_POSITION");
            string velocityText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_SOLVER_VELOCITY");
            if (string.IsNullOrWhiteSpace(positionText) && string.IsNullOrWhiteSpace(velocityText))
                return;

            int positionIterations = string.IsNullOrWhiteSpace(positionText)
                ? 12
                : int.Parse(positionText, CultureInfo.InvariantCulture);
            int velocityIterations = string.IsNullOrWhiteSpace(velocityText)
                ? 4
                : int.Parse(velocityText, CultureInfo.InvariantCulture);
            Assert.That(positionIterations, Is.InRange(1, 64));
            Assert.That(velocityIterations, Is.InRange(1, 32));

            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                if (segment.Body == null)
                    continue;
                segment.Body.solverIterations = positionIterations;
                segment.Body.solverVelocityIterations = velocityIterations;
            }

            if (controller.Saddle != null && controller.Saddle.Barbell != null && controller.Saddle.Barbell.Body != null)
            {
                controller.Saddle.Barbell.Body.solverIterations = positionIterations;
                controller.Saddle.Barbell.Body.solverVelocityIterations = velocityIterations == 4
                    ? 6
                    : velocityIterations;
            }
        }

        private static void ConfigureBalanceExperiment(SquatPredictiveBalanceController controller)
        {
            string copGainText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_COP_GAIN");
            if (!string.IsNullOrWhiteSpace(copGainText))
                controller.CopTrackingGain = float.Parse(copGainText, CultureInfo.InvariantCulture);

            string targetToCopText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_TARGET_TO_COP");
            if (!string.IsNullOrWhiteSpace(targetToCopText))
                controller.TargetToCopMPerRad = float.Parse(targetToCopText, CultureInfo.InvariantCulture);

            string guardText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_POSTURE_GUARD");
            if (!string.IsNullOrWhiteSpace(guardText))
                controller.PostureGuardEnabled = !string.Equals(guardText, "false", StringComparison.OrdinalIgnoreCase);

            string proximalText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_HIP_STRATEGY");
            if (!string.IsNullOrWhiteSpace(proximalText))
                controller.HipTrunkStrategyEnabled = !string.Equals(proximalText, "false", StringComparison.OrdinalIgnoreCase);

            string offsetRateText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_OFFSET_RATE");
            if (!string.IsNullOrWhiteSpace(offsetRateText))
                controller.MaxOffsetRateRadPerSecond = float.Parse(offsetRateText, CultureInfo.InvariantCulture);

            string comKpText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_COM_KP");
            if (!string.IsNullOrWhiteSpace(comKpText))
                controller.ComKp = float.Parse(comKpText, CultureInfo.InvariantCulture);

            string comKdText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_COM_KD");
            if (!string.IsNullOrWhiteSpace(comKdText))
                controller.ComKd = float.Parse(comKdText, CultureInfo.InvariantCulture);

            string captureGainText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_CAPTURE_GAIN");
            if (!string.IsNullOrWhiteSpace(captureGainText))
                controller.CapturePointTrackingGain = float.Parse(captureGainText, CultureInfo.InvariantCulture);

            string guardOnsetText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_GUARD_ONSET_RAD");
            if (!string.IsNullOrWhiteSpace(guardOnsetText))
                controller.PostureGuardOnsetThresholdRad = float.Parse(guardOnsetText, CultureInfo.InvariantCulture);

            string guardFullText = Environment.GetEnvironmentVariable("GAM13_STAGE_A_GUARD_FULL_RAD");
            if (!string.IsNullOrWhiteSpace(guardFullText))
                controller.PostureGuardFullThresholdRad = float.Parse(guardFullText, CultureInfo.InvariantCulture);
        }

        private static bool BalanceExperimentEnabled()
        {
            string value = Environment.GetEnvironmentVariable("GAM13_STAGE_A_BALANCE_ENABLED");
            return string.IsNullOrWhiteSpace(value) ||
                !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase);
        }

        private static void ConfigureLowerFamilyBiasExperiment(SquatEquilibriumPreload preload)
        {
            SetBiasFromEnvironment(preload, SquatJointFamily.Ankle, "GAM13_STAGE_A_ANKLE_BIAS");
            SetBiasFromEnvironment(preload, SquatJointFamily.Knee, "GAM13_STAGE_A_KNEE_BIAS");
            SetBiasFromEnvironment(preload, SquatJointFamily.Hip, "GAM13_STAGE_A_HIP_BIAS");
        }

        private static void SetBiasFromEnvironment(
            SquatEquilibriumPreload preload,
            SquatJointFamily family,
            string variable)
        {
            string value = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(value))
                preload.SetAnatomicalFlexionBiasDegrees(
                    family,
                    float.Parse(value, CultureInfo.InvariantCulture));
        }

        private static float[] BiasTable(float loadKg, bool abdomen)
        {
            string suffix = Mathf.RoundToInt(loadKg).ToString(CultureInfo.InvariantCulture) + "Kg";
            string fieldName = (abdomen ? "AbdomenBias" : "ThoraxBias") + suffix;
            FieldInfo field = typeof(SquatEquilibriumPreload).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "No heavy-load calibration knot exists for " + loadKg + " kg.");
            return (float[])field.GetValue(null);
        }

        private static float[] SnapshotBiasTable(float loadKg, bool abdomen) =>
            (float[])BiasTable(loadKg, abdomen).Clone();

        private static void SetStandingBiasTable(float loadKg, bool abdomen, float value)
        {
            if (!float.IsFinite(value) || Mathf.Abs(value) > 12.01f)
                throw new ArgumentOutOfRangeException(nameof(value));
            BiasTable(loadKg, abdomen)[0] = value;
        }

        private static void RestoreBiasTable(float loadKg, bool abdomen, float[] original)
        {
            float[] live = BiasTable(loadKg, abdomen);
            Array.Copy(original, live, live.Length);
        }

        private static void WriteArtifact(IReadOnlyList<StandingResult> results, string fileName)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Measurements/GAM-13");
            Directory.CreateDirectory(directory);
            var csv = new StringBuilder();
            csv.AppendLine(
                "repeat,load_kg,measured_ticks,settle_pelvis_y,min_pelvis_y,final_pelvis_y,max_trunk_pitch_rad," +
                "max_foot_pitch_deg,min_capture_ap_margin_m,min_capture_hull_margin_m,max_com_speed_mps," +
                "max_canonical_pose_error_deg,max_target_actual_deflection_deg,max_limit_proximity," +
                "min_guard_scale,max_ankle_offset_deg,rearward_cop_authority_at_settle_m," +
                "min_rearward_cop_authority_m,max_saddle_separation_m," +
                "min_support_contacts,support_lost,saddle_unstable,non_finite,upright,pass");
            foreach (StandingResult result in results)
                csv.AppendLine(result.CsvRow);
            File.WriteAllText(Path.Combine(directory, fileName), csv.ToString());
        }

        private sealed class StandingResult
        {
            public int Repeat;
            public float LoadKg;
            public int MeasuredTicks;
            public float InitialPelvisY = float.NaN;
            public float MinPelvisHeightAtSettle = float.NaN;
            public float MinPelvisY = float.PositiveInfinity;
            public float FinalPelvisY = float.NaN;
            public float MaxAbsTrunkPitchRad;
            public float MaxAbsFootPitchDeg;
            public float MinCaptureApMarginM = float.PositiveInfinity;
            public float MinCaptureHullMarginM = float.PositiveInfinity;
            public float MaxComSpeedMps;
            public float MaxCanonicalPoseErrorDeg;
            public float MaxTargetActualDeflectionDeg;
            public float MaxLimitProximity;
            public float MinGuardScale = 1f;
            public float MaxAnkleOffsetDeg;
            public float RearwardCopAuthorityAtSettleM = float.NaN;
            public float MinRearwardCopAuthorityM = float.PositiveInfinity;
            public float MaxSaddleSeparationM;
            public int MinSupportContacts = int.MaxValue;
            public int SaturatedTicks;
            public bool SupportLost;
            public bool SaddleUnstable;
            public bool NonFinite;
            public bool Upright;
            public bool IsPass;

            public string Summary => string.Format(
                CultureInfo.InvariantCulture,
                "repeat={0} load={1:F0} measured={2} settlePelvis={3:F4} minPelvis={4:F4} maxTrunk={5:F4} " +
                "captureAp={6:F4} captureHull={7:F4} comSpeed={8:F4} canonical={9:F3} " +
                "deflection={10:F3} limit={11:F3} guard={12:F3} ankle={13:F3} " +
                "rearCopAtSettle={14:F4} rearCopAuthority={15:F4} saddle={16:F4} " +
                "contacts={17} sat={18:F3} supportLost={19} saddleUnstable={20} " +
                "nonFinite={21} upright={22} pass={23}",
                Repeat, LoadKg, MeasuredTicks, MinPelvisHeightAtSettle, MinPelvisY, MaxAbsTrunkPitchRad,
                MinCaptureApMarginM, MinCaptureHullMarginM, MaxComSpeedMps, MaxCanonicalPoseErrorDeg,
                MaxTargetActualDeflectionDeg, MaxLimitProximity, MinGuardScale, MaxAnkleOffsetDeg,
                RearwardCopAuthorityAtSettleM, MinRearwardCopAuthorityM, MaxSaddleSeparationM,
                MinSupportContacts,
                MeasuredTicks == 0 ? 1f : SaturatedTicks / (float)MeasuredTicks,
                SupportLost, SaddleUnstable, NonFinite, Upright, IsPass);

            public string CsvRow => string.Format(
                CultureInfo.InvariantCulture,
                "{0},{1:R},{2},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18},{19},{20},{21},{22},{23},{24}",
                Repeat, LoadKg, MeasuredTicks, MinPelvisHeightAtSettle, MinPelvisY, FinalPelvisY,
                MaxAbsTrunkPitchRad, MaxAbsFootPitchDeg, MinCaptureApMarginM, MinCaptureHullMarginM,
                MaxComSpeedMps, MaxCanonicalPoseErrorDeg, MaxTargetActualDeflectionDeg,
                MaxLimitProximity, MinGuardScale, MaxAnkleOffsetDeg, RearwardCopAuthorityAtSettleM,
                MinRearwardCopAuthorityM, MaxSaddleSeparationM, MinSupportContacts,
                SupportLost, SaddleUnstable,
                NonFinite, Upright, IsPass);
        }
    }
}
