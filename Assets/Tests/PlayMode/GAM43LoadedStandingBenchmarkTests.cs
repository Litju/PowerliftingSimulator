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
    /// GAM-43 experiment runner. Candidate selection is external to this
    /// fixture; the fixture only executes the frozen arm named by the process
    /// environment and writes deterministic evidence.
    /// </summary>
    public sealed class GAM43LoadedStandingBenchmarkTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int SettleTicks = 100;
        private const int TotalTicks = 600;
        private const float MinimumPelvisHeightM = 0.90f;
        private const float MaximumTrunkPitchRad = 0.70f;
        private const float MinimumCaptureHullMarginM = 0.01f;
        private const float MaximumComSpeedMps = 0.25f;
        private const float MaximumSustainedSaturationFraction = 0.05f;
        private const float MaximumCanonicalPoseErrorDeg = 10f;
        private const float MaximumLimitProximity = 0.95f;
        private const float MaximumSaddleLimitOccupancy = 0.95f;
        private const float MaximumSaddleSeparationM = SquatBarSaddle.MaxPlausibleSeparationM;
        private const int OnsetPersistenceTicks = 3;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

        [UnityTest]
        [Explicit("GAM-43 frozen loaded-standing experiment; run with GAM43_MODE and candidate environment variables.")]
        public IEnumerator GAM43_LOADED_STANDING_BENCHMARK()
        {
            string mode = Environment.GetEnvironmentVariable("GAM43_MODE") ?? "standing";
            if (string.Equals(mode, "guard", StringComparison.OrdinalIgnoreCase))
            {
                yield return RunGuardDiagnostic();
                yield break;
            }

            string phase = Environment.GetEnvironmentVariable("GAM43_PHASE") ?? "phase1";
            string feedForward = Environment.GetEnvironmentVariable("GAM43_FEED_FORWARD") ?? "F0";
            string saddle = Environment.GetEnvironmentVariable("GAM43_SADDLE") ?? "S0";
            float[] loads = ParseLoads(Environment.GetEnvironmentVariable("GAM43_LOADS"));
            int repeats = ParseRepeats(Environment.GetEnvironmentVariable("GAM43_REPEATS"));
            bool raw = IsTrue(Environment.GetEnvironmentVariable("GAM43_RAW"));
            string outputPath = OutputPath(phase + "-" + feedForward + "-" + saddle + "-standing.csv");

            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            SquatBarSaddle.ExperimentalOverride = SaddleConfiguration(saddle);
            var results = new List<StandingResult>(loads.Length * repeats);
            try
            {
                ApplyImpedanceFactor(5f);
                for (int loadIndex = 0; loadIndex < loads.Length; loadIndex++)
                {
                    for (int repeat = 1; repeat <= repeats; repeat++)
                    {
                        yield return LoadFreshScene();
                        ConfigureCandidate(feedForward, false);
                        results.Add(RunStanding(
                            phase,
                            feedForward,
                            saddle,
                            loads[loadIndex],
                            repeat,
                            raw,
                            outputPath));
                    }
                }

                WriteStandingCsv(outputPath, results);

                if (string.Equals(phase, "phase3", StringComparison.OrdinalIgnoreCase))
                {
                    var lifecycle = new List<GAM13AttemptResult>(1);
                    yield return GAM13SquatLoadCalibrationHarness.RunAttempt(
                        "GAM43-integrated",
                        feedForward + "+" + saddle,
                        25f,
                        1,
                        controller => ConfigureCandidate(controller, feedForward, false),
                        lifecycle);
                    WriteLifecycleCsv(OutputPath("integrated-25kg-lifecycle.csv"), lifecycle);
                }
            }
            finally
            {
                RestoreProfiles(originalProfiles);
                SquatBarSaddle.ExperimentalOverride = null;
            }

            yield return null;
        }

        private IEnumerator RunGuardDiagnostic()
        {
            string feedForward = Environment.GetEnvironmentVariable("GAM43_FEED_FORWARD") ?? "F0";
            string saddle = Environment.GetEnvironmentVariable("GAM43_SADDLE") ?? "S0";
            float loadKg = ParseSingleLoad(Environment.GetEnvironmentVariable("GAM43_GUARD_LOAD"), 170f);
            string outputPath = OutputPath("guard-semantics.csv");
            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            SquatBarSaddle.ExperimentalOverride = SaddleConfiguration(saddle);
            var results = new List<StandingResult>(2);
            try
            {
                ApplyImpedanceFactor(5f);
                foreach (bool historical in new[] { false, true })
                {
                    yield return LoadFreshScene();
                    ConfigureCandidate(feedForward, historical);
                    results.Add(RunStanding(
                        "guard-diagnostic",
                        feedForward + (historical ? "-target-deflection" : "-canonical-pose"),
                        saddle,
                        loadKg,
                        historical ? 2 : 1,
                        false,
                        outputPath));
                }
                WriteStandingCsv(outputPath, results);
            }
            finally
            {
                RestoreProfiles(originalProfiles);
                SquatBarSaddle.ExperimentalOverride = null;
            }

            yield return null;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-43 qualification scene is missing.");
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

        private void ConfigureCandidate(string feedForward, bool useHistoricalGuard)
        {
            ConfigureCandidate(_controller, feedForward, useHistoricalGuard);
        }

        private static void ConfigureCandidate(
            SquatPhysicalPrototypeController controller,
            string feedForward,
            bool useHistoricalGuard)
        {
            controller.Adapter.Preload.ExperimentalBiasOverride =
                GAM43FeedForward.ForVariant(feedForward);
            controller.Adapter.ExperimentalUseTargetDeflectionForPostureGuard = useHistoricalGuard;
        }

        private StandingResult RunStanding(
            string phase,
            string feedForward,
            string saddle,
            float loadKg,
            int repeat,
            bool writeRaw,
            string outputPath)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            FoundationRuntime runtime = _bootstrap.Runtime;
            float dt = (float)SimulationConstants.FixedDeltaTimeSeconds;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

            var result = new StandingResult
            {
                Phase = phase,
                FeedForward = feedForward,
                Saddle = saddle,
                LoadKg = loadKg,
                Repeat = repeat,
                MinCaptureHullMarginM = float.PositiveInfinity,
                MinCaptureApMarginM = float.PositiveInfinity,
                MinRearwardCopAuthorityM = float.PositiveInfinity,
                MinForwardCopAuthorityM = float.PositiveInfinity,
                MinPelvisY = float.PositiveInfinity,
                MinSupportContacts = int.MaxValue,
                MaxSaddleSeparationM = 0f,
                MaxSaddleLimitOccupancy = 0f,
                MaxSaddleLinearLimitOccupancy = 0f,
                MaxSaddleAngularLimitOccupancy = 0f,
                MaxSaddleAngularXLimitOccupancy = 0f,
                MaxSaddleAngularYLimitOccupancy = 0f,
                MaxSaddleAngularZLimitOccupancy = 0f,
                MinPostureGuardScale = 1f
            };
            var onsets = new StandingOnsets();
            var supportPoints = new List<Vector3>(32);
            StringBuilder rawRows = writeRaw ? new StringBuilder() : null;
            if (writeRaw)
            {
                rawRows.AppendLine(
                    "phase,feed_forward,saddle,repeat,load_kg,tick,pelvis_y,trunk_pitch_rad,canonical_pose_error_deg," +
                    "target_actual_deflection_deg,capture_hull_margin_m,capture_front_m,capture_rear_m,com_speed_mps," +
                    "raw_ankle_authority_fraction,applied_ankle_authority_fraction,posture_guard_scale,hip_strategy_blend," +
                    "hip_authority_usage,trunk_authority_usage,drive_demand,drive_saturated,joint_limit,saddle_separation_m," +
                    "saddle_linear_limit_occupancy,saddle_angular_x_limit_occupancy,saddle_angular_y_limit_occupancy," +
                    "saddle_angular_z_limit_occupancy,saddle_force_n,saddle_torque_nm,support_contacts,support_retained," +
                    "tracking_failure,capture_failure,posture_failure,support_loss,saddle_linear_limit,saddle_angular_limit");
            }

            for (int tick = 0; tick < TotalTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                Assert.That(runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet(dt);

                if (!_controller.ObservationCollector.HasLastSnapshot)
                    continue;

                SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
                SquatBalanceObserver balance = adapter.Balance;
                SquatPredictiveBalanceController control = adapter.BalanceController;
                float pelvisY = snapshot.PelvisPositionWorldMeters.Y;
                float trunkPitch = snapshot.TrunkWorldPitchRadians;
                supportPoints.Clear();
                AddCompletedContacts(_controller.LeftFootContact, supportPoints);
                AddCompletedContacts(_controller.RightFootContact, supportPoints);
                SquatSupportGeometry.Measurement capture = SquatSupportGeometry.Measure(
                    supportPoints,
                    new Vector2(balance.CaptureMl, balance.CaptureAp));

                SquatJointObservationSet joints = snapshot.Joints;
                float maxJointLimit = MaxFinite(
                    joints.LeftKnee.LimitProximity,
                    joints.RightKnee.LimitProximity,
                    joints.LeftHip.LimitProximity,
                    joints.RightHip.LimitProximity,
                    joints.LeftAnkle.LimitProximity,
                    joints.RightAnkle.LimitProximity,
                    joints.Abdomen.LimitProximity,
                    joints.Thorax.LimitProximity);
                float maxDriveDemand = MaxFinite(
                    joints.LeftKnee.ModeledDemand,
                    joints.RightKnee.ModeledDemand,
                    joints.LeftHip.ModeledDemand,
                    joints.RightHip.ModeledDemand,
                    joints.LeftAnkle.ModeledDemand,
                    joints.RightAnkle.ModeledDemand,
                    joints.Abdomen.ModeledDemand,
                    joints.Thorax.ModeledDemand);
                float comSpeed = new Vector2(
                    balance.SystemComVelocity.x,
                    balance.SystemComVelocity.z).magnitude;
                float saddleSeparation = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.SaddleSeparationMeters;
                float saddleLimit = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.MaximumLimitOccupancy;
                float saddleLinearLimit = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.CurrentLinearLimitOccupancy;
                float saddleAngularXLimit = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.CurrentAngularXLimitOccupancy;
                float saddleAngularYLimit = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.CurrentAngularYLimitOccupancy;
                float saddleAngularZLimit = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.CurrentAngularZLimitOccupancy;
                float saddleAngularLimit = MaxFinite(saddleAngularXLimit, saddleAngularYLimit, saddleAngularZLimit);
                float saddleForce = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.CurrentForceEngine.magnitude;
                float saddleTorque = _controller.Saddle == null
                    ? float.PositiveInfinity
                    : _controller.Saddle.CurrentTorqueEngine.magnitude;
                bool finite = float.IsFinite(pelvisY) && float.IsFinite(trunkPitch) &&
                    float.IsFinite(saddleSeparation) && float.IsFinite(saddleLimit) &&
                    float.IsFinite(saddleLinearLimit) && float.IsFinite(saddleAngularLimit) &&
                    float.IsFinite(saddleForce) && float.IsFinite(saddleTorque) &&
                    IsFinite(balance.SystemCom) && IsFinite(balance.SystemComVelocity) &&
                    float.IsFinite(adapter.CanonicalPostureErrorRad) &&
                    float.IsFinite(adapter.TargetActualDeflectionRad) &&
                    float.IsFinite(maxJointLimit) && float.IsFinite(maxDriveDemand) &&
                    (!balance.HasSupport || float.IsFinite(capture.HullSignedMarginM));
                result.NonFinite |= !finite;

                bool trackingFailure = finite && adapter.TargetActualDeflectionRad >= MaximumCanonicalPoseErrorDeg * Mathf.Deg2Rad;
                bool captureFailure = finite && balance.HasSupport &&
                    Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear) <= MinimumCaptureHullMarginM;
                bool postureFailure = finite && (
                    adapter.CanonicalPostureErrorRad >= MaximumCanonicalPoseErrorDeg * Mathf.Deg2Rad ||
                    Mathf.Abs(trunkPitch) >= MaximumTrunkPitchRad || pelvisY <= MinimumPelvisHeightM);
                bool supportLoss = finite && !balance.HasSupport;
                bool saddleLinearLimitSignal = finite && _controller.Saddle != null && _controller.Saddle.IsAttached &&
                    saddleLinearLimit >= MaximumSaddleLimitOccupancy;
                bool saddleAngularLimitSignal = finite && _controller.Saddle != null && _controller.Saddle.IsAttached &&
                    saddleAngularLimit >= MaximumSaddleLimitOccupancy;
                onsets.Observe(
                    trackingFailure, captureFailure, postureFailure, supportLoss,
                    saddleLinearLimitSignal, saddleAngularLimitSignal, tick);

                if (tick == 0)
                    result.InitialPelvisY = pelvisY;
                if (tick >= SettleTicks)
                {
                    result.MeasuredTicks++;
                    result.MinPelvisY = Mathf.Min(result.MinPelvisY, pelvisY);
                    result.MaxAbsTrunkPitchRad = Mathf.Max(result.MaxAbsTrunkPitchRad, Mathf.Abs(trunkPitch));
                    result.MinCaptureApMarginM = Mathf.Min(
                        result.MinCaptureApMarginM,
                        Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear));
                    if (float.IsFinite(capture.HullSignedMarginM))
                        result.MinCaptureHullMarginM = Mathf.Min(result.MinCaptureHullMarginM, capture.HullSignedMarginM);
                    result.MaxComSpeedMps = Mathf.Max(result.MaxComSpeedMps, comSpeed);
                    result.MaxCanonicalPoseErrorDeg = Mathf.Max(
                        result.MaxCanonicalPoseErrorDeg,
                        adapter.CanonicalPostureErrorRad * Mathf.Rad2Deg);
                    result.MaxTargetActualDeflectionDeg = Mathf.Max(
                        result.MaxTargetActualDeflectionDeg,
                        adapter.TargetActualDeflectionRad * Mathf.Rad2Deg);
                    result.MaxJointLimitProximity = Mathf.Max(result.MaxJointLimitProximity, maxJointLimit);
                    result.MaxDriveDemand = Mathf.Max(result.MaxDriveDemand, maxDriveDemand);
                    result.MaxBalanceAnkleUsage = Mathf.Max(
                        result.MaxBalanceAnkleUsage,
                        control.RawAnkleAuthorityFraction);
                    result.MaxAppliedBalanceAnkleUsage = Mathf.Max(
                        result.MaxAppliedBalanceAnkleUsage,
                        control.AnkleAuthorityFraction);
                    result.MinPostureGuardScale = Mathf.Min(result.MinPostureGuardScale, control.PostureGuardScale);
                    result.MaxHipStrategyBlend = Mathf.Max(result.MaxHipStrategyBlend, control.HipStrategyBlend);
                    result.MaxBalanceHipUsage = Mathf.Max(
                        result.MaxBalanceHipUsage,
                        Mathf.Abs(control.HipSagittalOffsetRad) / SquatPredictiveBalanceController.MaxHipSagittalOffsetRad);
                    result.MaxBalanceTrunkUsage = Mathf.Max(
                        result.MaxBalanceTrunkUsage,
                        Mathf.Abs(control.TrunkSagittalOffsetRad) / SquatPredictiveBalanceController.MaxTrunkSagittalOffsetRad);
                    result.MinRearwardCopAuthorityM = Mathf.Min(
                        result.MinRearwardCopAuthorityM,
                        balance.HasSupport
                            ? control.ComRefAp - (balance.SupportApMin + SquatPredictiveBalanceController.SupportInteriorMarginM)
                            : float.NegativeInfinity);
                    result.MinForwardCopAuthorityM = Mathf.Min(
                        result.MinForwardCopAuthorityM,
                        balance.HasSupport
                            ? (balance.SupportApMax - SquatPredictiveBalanceController.SupportInteriorMarginM) - control.ComRefAp
                            : float.NegativeInfinity);
                    result.MaxSaddleSeparationM = Mathf.Max(result.MaxSaddleSeparationM, saddleSeparation);
                    result.MaxSaddleLimitOccupancy = Mathf.Max(result.MaxSaddleLimitOccupancy, saddleLimit);
                    result.MaxSaddleLinearLimitOccupancy = Mathf.Max(result.MaxSaddleLinearLimitOccupancy, saddleLinearLimit);
                    result.MaxSaddleAngularLimitOccupancy = Mathf.Max(result.MaxSaddleAngularLimitOccupancy, saddleAngularLimit);
                    result.MaxSaddleAngularXLimitOccupancy = Mathf.Max(result.MaxSaddleAngularXLimitOccupancy, saddleAngularXLimit);
                    result.MaxSaddleAngularYLimitOccupancy = Mathf.Max(result.MaxSaddleAngularYLimitOccupancy, saddleAngularYLimit);
                    result.MaxSaddleAngularZLimitOccupancy = Mathf.Max(result.MaxSaddleAngularZLimitOccupancy, saddleAngularZLimit);
                    result.MaxSaddleForceN = Mathf.Max(result.MaxSaddleForceN, saddleForce);
                    result.MaxSaddleTorqueNm = Mathf.Max(result.MaxSaddleTorqueNm, saddleTorque);
                    result.MinSupportContacts = Mathf.Min(result.MinSupportContacts, balance.SupportContactCount);
                    if (!balance.HasSupport)
                        result.SupportLost = true;
                    if (_controller.Saddle == null || !_controller.Saddle.IsAttached || _controller.Saddle.IsBroken)
                        result.SaddleInvalid = true;
                    if (adapter.MaxDriveSaturation >= 1f)
                        result.SaturatedTicks++;
                }

                if (writeRaw)
                {
                    rawRows.AppendLine(string.Join(",", new[]
                    {
                        Csv(phase), Csv(feedForward), Csv(saddle), repeat.ToString(CultureInfo.InvariantCulture),
                        Csv(loadKg), tick.ToString(CultureInfo.InvariantCulture), Csv(pelvisY), Csv(trunkPitch),
                        Csv(adapter.CanonicalPostureErrorRad * Mathf.Rad2Deg),
                        Csv(adapter.TargetActualDeflectionRad * Mathf.Rad2Deg), Csv(capture.HullSignedMarginM),
                        Csv(balance.CaptureMarginFront), Csv(balance.CaptureMarginRear), Csv(comSpeed),
                        Csv(control.RawAnkleAuthorityFraction),
                        Csv(control.AnkleAuthorityFraction), Csv(control.PostureGuardScale), Csv(control.HipStrategyBlend),
                        Csv(Mathf.Abs(control.HipSagittalOffsetRad) / SquatPredictiveBalanceController.MaxHipSagittalOffsetRad),
                        Csv(Mathf.Abs(control.TrunkSagittalOffsetRad) / SquatPredictiveBalanceController.MaxTrunkSagittalOffsetRad),
                        Csv(maxDriveDemand), snapshot.DriveSaturated ? "true" : "false", Csv(maxJointLimit),
                        Csv(saddleSeparation), Csv(saddleLinearLimit), Csv(saddleAngularXLimit), Csv(saddleAngularYLimit),
                        Csv(saddleAngularZLimit), Csv(saddleForce), Csv(saddleTorque),
                        balance.SupportContactCount.ToString(CultureInfo.InvariantCulture), balance.HasSupport ? "true" : "false",
                        trackingFailure ? "true" : "false", captureFailure ? "true" : "false",
                        postureFailure ? "true" : "false", supportLoss ? "true" : "false",
                        saddleLinearLimitSignal ? "true" : "false", saddleAngularLimitSignal ? "true" : "false"
                    }));
                }
            }

            result.FinalPelvisY = _rig.Segments["pelvis"].Body.position.y;
            result.Upright = result.MinPelvisY > MinimumPelvisHeightM &&
                result.MaxAbsTrunkPitchRad < MaximumTrunkPitchRad;
            result.SupportRetained = !result.SupportLost && result.MinSupportContacts > 0;
            result.SaddleValid = !result.SaddleInvalid &&
                result.MaxSaddleLinearLimitOccupancy < MaximumSaddleLimitOccupancy &&
                result.MaxSaddleAngularLimitOccupancy < MaximumSaddleLimitOccupancy &&
                result.MaxSaddleSeparationM < MaximumSaddleSeparationM;
            result.LoadedSetupValid = result.MeasuredTicks > 0 && !result.NonFinite &&
                _controller.Saddle != null && _controller.Saddle.IsAttached && !_controller.Saddle.IsBroken;
            result.SaturationFraction = result.MeasuredTicks == 0
                ? float.PositiveInfinity
                : result.SaturatedTicks / (float)result.MeasuredTicks;
            result.TrackingFailureOnsetTick = onsets.TrackingFailure;
            result.CaptureFailureOnsetTick = onsets.CaptureFailure;
            result.PostureFailureOnsetTick = onsets.PostureFailure;
            result.SupportLossOnsetTick = onsets.SupportLoss;
            result.SaddleLinearLimitOnsetTick = onsets.SaddleLinearLimit;
            result.SaddleAngularLimitOnsetTick = onsets.SaddleAngularLimit;
            bool standingGates = result.MeasuredTicks == TotalTicks - SettleTicks &&
                !result.NonFinite && result.Upright && result.SupportRetained && result.SaddleValid &&
                result.MinCaptureHullMarginM > MinimumCaptureHullMarginM &&
                result.MaxComSpeedMps < MaximumComSpeedMps &&
                result.MaxCanonicalPoseErrorDeg < MaximumCanonicalPoseErrorDeg &&
                result.MaxJointLimitProximity < MaximumLimitProximity &&
                result.SaturationFraction < MaximumSustainedSaturationFraction;
            result.IsPass = standingGates;

            if (writeRaw)
            {
                string rawPath = Path.Combine(
                    Path.GetDirectoryName(outputPath),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "raw-{0}-{1}-{2}-{3:000}kg-r{4}.csv",
                        phase, feedForward, saddle, loadKg, repeat));
                File.WriteAllText(rawPath, rawRows.ToString());
            }

            Debug.Log("GAM43_STANDING " + result.Summary);
            return result;
        }

        private void TickFeet(float dt)
        {
            _controller.LeftFootContact?.PhysicsTickUpdate(dt);
            _controller.RightFootContact?.PhysicsTickUpdate(dt);
        }

        private static void AddCompletedContacts(PhysicalFootContactDetector detector, List<Vector3> contacts)
        {
            if (detector == null)
                return;
            for (int index = 0; index < detector.CompletedContactCount; index++)
                contacts.Add(detector.CompletedContactPoint(index));
        }

        private static JointFamilyProfile[] ProfileArray() =>
            (JointFamilyProfile[])typeof(PoweredJointController)
                .GetField("Profiles", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static JointFamilyProfile[] SnapshotProfiles() => (JointFamilyProfile[])ProfileArray().Clone();

        private static void RestoreProfiles(JointFamilyProfile[] original)
        {
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
                live[index] = original[index];
        }

        private static void ApplyImpedanceFactor(float factor)
        {
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
            {
                JointFamilyProfile profile = live[index];
                if (profile.Id != "ankle" && profile.Id != "knee" && profile.Id != "hip" && profile.Id != "trunk")
                    continue;
                live[index] = new JointFamilyProfile(
                    profile.Id,
                    profile.Spring * factor,
                    profile.Damper * Mathf.Sqrt(factor),
                    profile.BaseCapacityNm,
                    profile.MaxTargetRateRadS);
            }
        }

        private static SquatBarSaddle.ExperimentalConfiguration? SaddleConfiguration(string saddle)
        {
            switch (saddle.ToUpperInvariant())
            {
                case "S0":
                    return new SquatBarSaddle.ExperimentalConfiguration(50000f, 3000f, 0.05f);
                case "S1":
                    return new SquatBarSaddle.ExperimentalConfiguration(50000f, 3000f, 0.10f);
                case "S2":
                    return new SquatBarSaddle.ExperimentalConfiguration(75000f, 3000f * Mathf.Sqrt(1.5f), 0.05f);
                default:
                    throw new ArgumentException("GAM43_SADDLE must be S0, S1, or S2.", nameof(saddle));
            }
        }

        private static float[] ParseLoads(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new[] { 25f, 60f, 140f, 170f };
            string[] tokens = text.Split(',');
            var loads = new float[tokens.Length];
            for (int index = 0; index < tokens.Length; index++)
                loads[index] = float.Parse(tokens[index], CultureInfo.InvariantCulture);
            return loads;
        }

        private static float ParseSingleLoad(string text, float fallback) =>
            string.IsNullOrWhiteSpace(text) ? fallback : float.Parse(text, CultureInfo.InvariantCulture);

        private static int ParseRepeats(string text)
        {
            int repeats = string.IsNullOrWhiteSpace(text) ? 1 : int.Parse(text, CultureInfo.InvariantCulture);
            Assert.That(repeats, Is.InRange(1, 3));
            return repeats;
        }

        private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) || value == "1";

        private static string OutputPath(string fileName)
        {
            string explicitPath = Environment.GetEnvironmentVariable("GAM44_OUTPUT") ??
                Environment.GetEnvironmentVariable("GAM43_OUTPUT");
            if (!string.IsNullOrWhiteSpace(explicitPath))
                return Path.GetFullPath(explicitPath);
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts", "Measurements", "GAM-13", "GAM43");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, fileName);
        }

        private static void WriteStandingCsv(string path, IReadOnlyList<StandingResult> results)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var builder = new StringBuilder();
            builder.AppendLine(StandingResult.Header);
            for (int index = 0; index < results.Count; index++)
                builder.AppendLine(results[index].CsvRow);
            File.WriteAllText(path, builder.ToString());
        }

        private static void WriteLifecycleCsv(string path, IReadOnlyList<GAM13AttemptResult> results)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var builder = new StringBuilder();
            builder.AppendLine(string.Join(",", GAM13SquatLoadCalibrationHarness.CsvColumns));
            for (int index = 0; index < results.Count; index++)
                builder.AppendLine(GAM13SquatLoadCalibrationHarness.CsvRow(results[index]));
            File.WriteAllText(path, builder.ToString());
        }

        private static float MaxFinite(params float[] values)
        {
            float maximum = float.NegativeInfinity;
            for (int index = 0; index < values.Length; index++)
                maximum = Mathf.Max(maximum, values[index]);
            return maximum;
        }

        private static bool IsFinite(Vector3 value) =>
            float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        private static string Csv(float value) =>
            float.IsNaN(value) || float.IsInfinity(value)
                ? "NA"
                : value.ToString("R", CultureInfo.InvariantCulture);

        private static string Csv(string value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private sealed class StandingOnsets
        {
            private readonly OnsetTracker _trackingFailure = new OnsetTracker();
            private readonly OnsetTracker _captureFailure = new OnsetTracker();
            private readonly OnsetTracker _postureFailure = new OnsetTracker();
            private readonly OnsetTracker _supportLoss = new OnsetTracker();
            private readonly OnsetTracker _saddleLinearLimit = new OnsetTracker();
            private readonly OnsetTracker _saddleAngularLimit = new OnsetTracker();

            public int TrackingFailure => _trackingFailure.FirstOnsetTick;
            public int CaptureFailure => _captureFailure.FirstOnsetTick;
            public int PostureFailure => _postureFailure.FirstOnsetTick;
            public int SupportLoss => _supportLoss.FirstOnsetTick;
            public int SaddleLinearLimit => _saddleLinearLimit.FirstOnsetTick;
            public int SaddleAngularLimit => _saddleAngularLimit.FirstOnsetTick;

            public void Observe(
                bool trackingFailure,
                bool captureFailure,
                bool postureFailure,
                bool supportLoss,
                bool saddleLinearLimit,
                bool saddleAngularLimit,
                int tick)
            {
                _trackingFailure.Update(trackingFailure, tick);
                _captureFailure.Update(captureFailure, tick);
                _postureFailure.Update(postureFailure, tick);
                _supportLoss.Update(supportLoss, tick);
                _saddleLinearLimit.Update(saddleLinearLimit, tick);
                _saddleAngularLimit.Update(saddleAngularLimit, tick);
            }
        }

        private sealed class OnsetTracker
        {
            private int _consecutive;

            public int FirstOnsetTick { get; private set; } = -1;

            public void Update(bool signal, int tick)
            {
                if (!signal)
                {
                    _consecutive = 0;
                    return;
                }

                _consecutive++;
                if (FirstOnsetTick < 0 && _consecutive >= OnsetPersistenceTicks)
                    FirstOnsetTick = tick - (OnsetPersistenceTicks - 1);
            }
        }

        private sealed class StandingResult
        {
            public const string Header =
                "phase,feed_forward,saddle,repeat,load_kg,measured_ticks,initial_pelvis_y,final_pelvis_y,min_pelvis_y," +
                "max_trunk_pitch_rad,upright,min_capture_hull_margin_m,min_capture_ap_margin_m,max_com_speed_mps," +
                "max_canonical_pose_error_deg,max_target_actual_deflection_deg,max_joint_limit_proximity," +
                "max_raw_ankle_authority_fraction,max_applied_ankle_authority_fraction,min_posture_guard_scale,max_hip_strategy_blend," +
                "max_hip_authority_usage,max_trunk_authority_usage,max_drive_demand,saturation_fraction," +
                "min_rearward_cop_authority_m,min_forward_cop_authority_m,max_saddle_separation_m,max_saddle_limit_occupancy," +
                "max_saddle_linear_limit_occupancy,max_saddle_angular_limit_occupancy,max_saddle_angular_x_limit_occupancy," +
                "max_saddle_angular_y_limit_occupancy,max_saddle_angular_z_limit_occupancy,max_saddle_force_n,max_saddle_torque_nm," +
                "min_support_contacts,support_lost,saddle_invalid,non_finite,support_retained,saddle_valid,loaded_setup_valid," +
                "tracking_failure_onset_tick,capture_failure_onset_tick,posture_failure_onset_tick,support_loss_onset_tick," +
                "saddle_linear_limit_onset_tick,saddle_angular_limit_onset_tick,standing_gates_pass,pass";

            public string Phase;
            public string FeedForward;
            public string Saddle;
            public int Repeat;
            public float LoadKg;
            public int MeasuredTicks;
            public float InitialPelvisY = float.NaN;
            public float FinalPelvisY = float.NaN;
            public float MinPelvisY = float.PositiveInfinity;
            public float MaxAbsTrunkPitchRad;
            public float MinCaptureHullMarginM;
            public float MinCaptureApMarginM;
            public float MaxComSpeedMps;
            public float MaxCanonicalPoseErrorDeg;
            public float MaxTargetActualDeflectionDeg;
            public float MaxJointLimitProximity;
            public float MaxBalanceAnkleUsage;
            public float MaxAppliedBalanceAnkleUsage;
            public float MinPostureGuardScale;
            public float MaxHipStrategyBlend;
            public float MaxBalanceHipUsage;
            public float MaxBalanceTrunkUsage;
            public float MaxDriveDemand;
            public float SaturationFraction;
            public float MinRearwardCopAuthorityM;
            public float MinForwardCopAuthorityM;
            public float MaxSaddleSeparationM;
            public float MaxSaddleLimitOccupancy;
            public float MaxSaddleLinearLimitOccupancy;
            public float MaxSaddleAngularLimitOccupancy;
            public float MaxSaddleAngularXLimitOccupancy;
            public float MaxSaddleAngularYLimitOccupancy;
            public float MaxSaddleAngularZLimitOccupancy;
            public float MaxSaddleForceN;
            public float MaxSaddleTorqueNm;
            public int MinSupportContacts;
            public int SaturatedTicks;
            public bool SupportLost;
            public bool SaddleInvalid;
            public bool NonFinite;
            public bool Upright;
            public bool SupportRetained;
            public bool SaddleValid;
            public bool LoadedSetupValid;
            public int TrackingFailureOnsetTick = -1;
            public int CaptureFailureOnsetTick = -1;
            public int PostureFailureOnsetTick = -1;
            public int SupportLossOnsetTick = -1;
            public int SaddleLinearLimitOnsetTick = -1;
            public int SaddleAngularLimitOnsetTick = -1;
            public bool IsPass;

            public string Summary => string.Format(
                CultureInfo.InvariantCulture,
                "phase={0} feed={1} saddle={2} repeat={3} load={4:F0} ticks={5} upright={6} " +
                "hull={7:F4} pose={8:F3} deflection={9:F3} jointLimit={10:F3} drive={11:F3} sat={12:F3} " +
                "rearCop={13:F4} frontCop={14:F4} saddleSep={15:F4} saddleLimit={16:F3} supportLost={17} pass={18}",
                Phase, FeedForward, Saddle, Repeat, LoadKg, MeasuredTicks, Upright,
                MinCaptureHullMarginM, MaxCanonicalPoseErrorDeg, MaxTargetActualDeflectionDeg,
                MaxJointLimitProximity, MaxDriveDemand, SaturationFraction,
                MinRearwardCopAuthorityM, MinForwardCopAuthorityM, MaxSaddleSeparationM,
                MaxSaddleLimitOccupancy, SupportLost, IsPass);

            public string CsvRow => string.Join(",", new[]
            {
                Csv(Phase), Csv(FeedForward), Csv(Saddle), Repeat.ToString(CultureInfo.InvariantCulture), Csv(LoadKg),
                MeasuredTicks.ToString(CultureInfo.InvariantCulture), Csv(InitialPelvisY), Csv(FinalPelvisY), Csv(MinPelvisY),
                Csv(MaxAbsTrunkPitchRad), Bool(Upright), Csv(MinCaptureHullMarginM), Csv(MinCaptureApMarginM), Csv(MaxComSpeedMps),
                Csv(MaxCanonicalPoseErrorDeg), Csv(MaxTargetActualDeflectionDeg), Csv(MaxJointLimitProximity),
                Csv(MaxBalanceAnkleUsage), Csv(MaxAppliedBalanceAnkleUsage), Csv(MinPostureGuardScale), Csv(MaxHipStrategyBlend),
                Csv(MaxBalanceHipUsage), Csv(MaxBalanceTrunkUsage), Csv(MaxDriveDemand), Csv(SaturationFraction),
                Csv(MinRearwardCopAuthorityM), Csv(MinForwardCopAuthorityM), Csv(MaxSaddleSeparationM), Csv(MaxSaddleLimitOccupancy),
                Csv(MaxSaddleLinearLimitOccupancy), Csv(MaxSaddleAngularLimitOccupancy),
                Csv(MaxSaddleAngularXLimitOccupancy), Csv(MaxSaddleAngularYLimitOccupancy), Csv(MaxSaddleAngularZLimitOccupancy),
                Csv(MaxSaddleForceN), Csv(MaxSaddleTorqueNm), MinSupportContacts.ToString(CultureInfo.InvariantCulture),
                Bool(SupportLost), Bool(SaddleInvalid), Bool(NonFinite), Bool(SupportRetained), Bool(SaddleValid), Bool(LoadedSetupValid),
                Tick(TrackingFailureOnsetTick), Tick(CaptureFailureOnsetTick), Tick(PostureFailureOnsetTick), Tick(SupportLossOnsetTick),
                Tick(SaddleLinearLimitOnsetTick), Tick(SaddleAngularLimitOnsetTick),
                Bool(IsStandingGatesPass), Bool(IsPass)
            });

            private bool IsStandingGatesPass =>
                MeasuredTicks == TotalTicks - SettleTicks && !NonFinite && Upright && SupportRetained && SaddleValid &&
                MinCaptureHullMarginM > MinimumCaptureHullMarginM && MaxComSpeedMps < MaximumComSpeedMps &&
                MaxCanonicalPoseErrorDeg < MaximumCanonicalPoseErrorDeg && MaxJointLimitProximity < MaximumLimitProximity &&
                SaturationFraction < MaximumSustainedSaturationFraction;

            private static string Bool(bool value) => value ? "true" : "false";

            private static string Tick(int value) => value < 0 ? "NA" : value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
