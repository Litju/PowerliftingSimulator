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
    /// GAME_ENGINE_CONTROL_PLANT_IDENTIFICATION. Fresh-scene, target-space
    /// perturbations around the held setup reference. The balance controller
    /// is disabled and the existing equilibrium layer remains active, so the
    /// measured response belongs to the fixed physical plant rather than to a
    /// controller candidate. Only rows in the local standing window are used
    /// for response summaries; post-collapse rows remain raw evidence.
    /// </summary>
    public sealed class GAM13BalancePlantIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-13";
        private const int ZeroRepeats = 3;
        private const int PreCommandTicks = 5;
        private const int PostCommandTicks = 140;
        private const float LocalPelvisMinimumM = 0.85f;
        private const float LocalTrunkMaximumRad = 0.70f;
        private const float LocalCaptureMarginMinimumM = -0.02f;
        private const float LocalLimitMaximum = 0.95f;
        private const float MinimumCopNoiseM = 0.0001f;
        private const float MinimumComNoiseM = 0.0001f;
        private const float MinimumTrunkNoiseRad = 0.001f;
        private static readonly float[] StageALoadsKg = { 25f, 60f, 140f, 170f, 300f };
        private static readonly string[] Channels = { "ankle", "hip", "trunk" };

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;

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

        [UnityTest]
        [Explicit("GAM-13 system identification; excluded from default qualification suites.")]
        public IEnumerator GAM13_GAME_ENGINE_CONTROL_PLANT_IDENTIFICATION()
        {
            var zeroRuns = new List<PlantRun>();
            var impulseRuns = new List<PlantRun>();
            JointFamilyProfile[] originalProfiles = SnapshotProfiles();
            try
            {
                ApplyImpedance(8f);
                foreach (float loadKg in StageALoadsKg)
                {
                    for (int repeat = 1; repeat <= ZeroRepeats; repeat++)
                        yield return RunExperiment(loadKg, "zero", 0f, repeat, zeroRuns);
                }

                foreach (float loadKg in StageALoadsKg)
                {
                    foreach (string channel in Channels)
                    {
                        float[] amplitudes = loadKg == 25f
                            ? new[] { 0.5f, 1f, 2f }
                            : new[] { 1f };
                        foreach (float amplitude in amplitudes)
                        {
                            yield return RunExperiment(loadKg, channel, -amplitude, 1, impulseRuns);
                            yield return RunExperiment(loadKg, channel, amplitude, 1, impulseRuns);
                        }
                    }
                }
            }
            finally
            {
                RestoreProfiles(originalProfiles);
            }

            WriteNoiseArtifact(zeroRuns);
            WriteImpulseArtifact(zeroRuns, impulseRuns);
            List<ResponseSummary> summaries = AnalyzeResponses(zeroRuns, impulseRuns);
            WriteSummaryArtifact(summaries);
            WriteMatrixArtifact(summaries);

            Assert.That(zeroRuns.Count, Is.EqualTo(StageALoadsKg.Length * ZeroRepeats));
            Assert.That(impulseRuns.Count, Is.EqualTo(42));
            Assert.That(summaries.Count, Is.EqualTo(42));
            Assert.That(summaries.Exists(summary => summary.LoadKg == 25f && summary.Channel == "ankle" && summary.InputDeg == 0.5f), Is.True);
            Assert.That(summaries.Exists(summary => summary.LoadKg == 300f && summary.Channel == "trunk" && summary.InputDeg == -1f), Is.True);
            yield return null;
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

        private static void ApplyImpedance(float factor)
        {
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

        private IEnumerator RunExperiment(
            float loadKg,
            string channel,
            float inputDeg,
            int repeat,
            List<PlantRun> destination)
        {
            yield return LoadFreshScene();

            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = false;
            adapter.Preload.Enabled = true;
            adapter.Preload.SpineCalibrationEnabled = true;
            adapter.AnkleSagittalOffsetOverrideRad = 0f;
            SetFamilyBias(adapter, SquatJointFamily.Ankle, 0f);
            SetFamilyBias(adapter, SquatJointFamily.Knee, 0f);
            SetFamilyBias(adapter, SquatJointFamily.Hip, 0f);
            SetFamilyBias(adapter, SquatJointFamily.Abdomen, 0f);
            SetFamilyBias(adapter, SquatJointFamily.Thorax, 0f);
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

            var run = new PlantRun(loadKg, channel, inputDeg, repeat);
            int totalTicks = PreCommandTicks + PostCommandTicks;
            for (int tick = 0; tick < totalTicks; tick++)
            {
                if (tick == PreCommandTicks)
                    ApplyPerturbation(adapter, channel, inputDeg);

                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                Assert.That(
                    _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds),
                    Is.EqualTo(1),
                    $"Identification did not advance exactly one tick at {loadKg} kg.");

                run.Samples.Add(CaptureSample(tick, adapter));
                if (tick % 40 == 0)
                    yield return null;
            }

            destination.Add(run);
            yield return null;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-13 qualification scene is missing.");
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

        private static void ApplyPerturbation(SquatPhysicalAdapter adapter, string channel, float inputDeg)
        {
            adapter.AnkleSagittalOffsetOverrideRad = 0f;
            SetFamilyBias(adapter, SquatJointFamily.Hip, 0f);
            SetFamilyBias(adapter, SquatJointFamily.Abdomen, 0f);
            SetFamilyBias(adapter, SquatJointFamily.Thorax, 0f);

            switch (channel)
            {
                case "zero":
                    return;
                case "ankle":
                    adapter.AnkleSagittalOffsetOverrideRad = inputDeg * Mathf.Deg2Rad;
                    return;
                case "hip":
                    SetFamilyBias(adapter, SquatJointFamily.Hip, inputDeg);
                    return;
                case "trunk":
                    SetFamilyBias(adapter, SquatJointFamily.Abdomen, inputDeg);
                    SetFamilyBias(adapter, SquatJointFamily.Thorax, inputDeg);
                    return;
                default:
                    throw new ArgumentException("Unknown identification channel " + channel, nameof(channel));
            }
        }

        private static void SetFamilyBias(SquatPhysicalAdapter adapter, SquatJointFamily family, float logicalDegrees)
        {
            float anatomicalDegrees = logicalDegrees * adapter.FamilyFlexionSign(family);
            adapter.Preload.SetAnatomicalFlexionBiasDegrees(family, anatomicalDegrees);
        }

        private PlantSample CaptureSample(int tick, SquatPhysicalAdapter adapter)
        {
            SquatBalanceObserver balance = adapter.Balance;
            Assert.That(_controller.ObservationCollector.HasLastSnapshot, Is.True, "The post-physics observation was not published.");
            SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;

            PoweredJointController.PoweredJointRuntime ankle = _rig.PoweredController.GetJoint("left_foot");
            PoweredJointController.PoweredJointRuntime hip = _rig.PoweredController.GetJoint("left_thigh");
            PoweredJointController.PoweredJointRuntime abdomen = _rig.PoweredController.GetJoint("abdomen");
            PoweredJointController.PoweredJointRuntime thorax = _rig.PoweredController.GetJoint("thorax");
            PoweredJointDiagnostic ankleDiagnostic = ankle.PostPhysicsDiagnostic;
            PoweredJointDiagnostic hipDiagnostic = hip.PostPhysicsDiagnostic;
            PoweredJointDiagnostic abdomenDiagnostic = abdomen.PostPhysicsDiagnostic;
            PoweredJointDiagnostic thoraxDiagnostic = thorax.PostPhysicsDiagnostic;

            float pelvisY = snapshot.PelvisPositionWorldMeters.Y;
            float trunkPitch = snapshot.TrunkWorldPitchRadians;
            float captureMargin = Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear);
            float saddleSeparation = _controller.Saddle == null
                ? float.PositiveInfinity
                : _controller.Saddle.SaddleSeparationMeters;
            string invalidReason = LocalInvalidReason(
                balance,
                pelvisY,
                trunkPitch,
                captureMargin,
                saddleSeparation,
                Mathf.Max(
                    ankleDiagnostic.LimitProximity,
                    hipDiagnostic.LimitProximity,
                    abdomenDiagnostic.LimitProximity,
                    thoraxDiagnostic.LimitProximity));

            return new PlantSample
            {
                Tick = tick,
                TimeSeconds = tick * SimulationConstants.FixedDeltaTimeSeconds,
                ValidLocal = invalidReason == null,
                InvalidReason = invalidReason ?? "NONE",
                HasSupport = balance.HasSupport,
                SupportContacts = balance.SupportContactCount,
                HasCop = balance.HasCopEstimate,
                CopAp = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                ComAp = balance.SystemCom.z,
                ComVelocityAp = balance.SystemComVelocity.z,
                CaptureAp = balance.CaptureAp,
                CaptureMargin = captureMargin,
                SupportApMin = balance.SupportApMin,
                SupportApMax = balance.SupportApMax,
                PelvisY = pelvisY,
                TrunkPitch = trunkPitch,
                SaddleSeparation = saddleSeparation,
                AnkleRequestedTargetDeg = LogicalDegrees(ankleDiagnostic.RequestedTarget),
                AnkleAppliedTargetDeg = LogicalDegrees(ankleDiagnostic.AppliedTarget),
                HipRequestedTargetDeg = LogicalDegrees(hipDiagnostic.RequestedTarget),
                HipAppliedTargetDeg = LogicalDegrees(hipDiagnostic.AppliedTarget),
                AbdomenRequestedTargetDeg = LogicalDegrees(abdomenDiagnostic.RequestedTarget),
                AbdomenAppliedTargetDeg = LogicalDegrees(abdomenDiagnostic.AppliedTarget),
                ThoraxRequestedTargetDeg = LogicalDegrees(thoraxDiagnostic.RequestedTarget),
                ThoraxAppliedTargetDeg = LogicalDegrees(thoraxDiagnostic.AppliedTarget),
                AnkleActualDeg = LogicalDegrees(ankleDiagnostic.ActualRelative),
                HipActualDeg = LogicalDegrees(hipDiagnostic.ActualRelative),
                AbdomenActualDeg = LogicalDegrees(abdomenDiagnostic.ActualRelative),
                ThoraxActualDeg = LogicalDegrees(thoraxDiagnostic.ActualRelative),
                AnkleSolverTorqueNm = ankleDiagnostic.SolverFlexionTorqueNm,
                HipSolverTorqueNm = hipDiagnostic.SolverFlexionTorqueNm,
                AbdomenSolverTorqueNm = abdomenDiagnostic.SolverFlexionTorqueNm,
                ThoraxSolverTorqueNm = thoraxDiagnostic.SolverFlexionTorqueNm,
                MaximumDemand = Mathf.Max(
                    ankleDiagnostic.ModeledDemand,
                    hipDiagnostic.ModeledDemand,
                    abdomenDiagnostic.ModeledDemand,
                    thoraxDiagnostic.ModeledDemand)
            };
        }

        private static string LocalInvalidReason(
            SquatBalanceObserver balance,
            float pelvisY,
            float trunkPitch,
            float captureMargin,
            float saddleSeparation,
            float limitProximity)
        {
            if (!IsFinite(pelvisY) || !IsFinite(trunkPitch) || !IsFinite(captureMargin) || !IsFinite(saddleSeparation))
                return "NON_FINITE";
            if (!balance.HasSupport)
                return "SUPPORT_LOST";
            if (pelvisY <= LocalPelvisMinimumM)
                return "PELVIS_DEPARTURE";
            if (Mathf.Abs(trunkPitch) >= LocalTrunkMaximumRad)
                return "TRUNK_DEPARTURE";
            if (captureMargin < LocalCaptureMarginMinimumM)
                return "CAPTURE_DEPARTURE";
            if (limitProximity >= LocalLimitMaximum)
                return "JOINT_LIMIT";
            if (saddleSeparation > 0.05f)
                return "SADDLE_DEPARTURE";
            return null;
        }

        private static float LogicalDegrees(Quaternion rotation) =>
            PoweredJointController.SignedTwistRadians(rotation, Vector3.right) * Mathf.Rad2Deg;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static List<ResponseSummary> AnalyzeResponses(
            List<PlantRun> zeroRuns,
            List<PlantRun> impulseRuns)
        {
            var summaries = new List<ResponseSummary>(impulseRuns.Count);
            foreach (PlantRun run in impulseRuns)
            {
                NoiseFloor noise = NoiseForLoad(zeroRuns, run.LoadKg);
                var summary = new ResponseSummary
                {
                    LoadKg = run.LoadKg,
                    Channel = run.Channel,
                    InputDeg = run.InputDeg,
                    ValidSamples = 0,
                    FirstValidTick = -1,
                    LastValidTick = -1,
                    LocalWindowStartTick = -1,
                    LocalWindowEndTick = -1,
                    CopDelayTicks = -1,
                    ComDelayTicks = -1,
                    TrunkDelayTicks = -1,
                    CopDcGain = float.NaN,
                    ComDcGain = float.NaN,
                    TrunkDcGain = float.NaN,
                    CopPeakDelta = 0f,
                    ComPeakDelta = 0f,
                    TrunkPeakDelta = 0f,
                    CopTrendPerSecond = float.NaN,
                    ComTrendPerSecond = float.NaN,
                    TrunkTrendPerSecond = float.NaN
                };

                var valid = new List<PlantSample>();
                foreach (PlantSample sample in run.Samples)
                {
                    if (sample.Tick < PreCommandTicks)
                        continue;
                    if (!sample.ValidLocal)
                        break;
                    if (!sample.HasCop)
                        continue;
                    valid.Add(sample);
                    summary.ValidSamples++;
                    if (summary.FirstValidTick < 0)
                        summary.FirstValidTick = sample.Tick;
                    summary.LastValidTick = sample.Tick;
                }

                if (valid.Count == 0 || Mathf.Abs(run.InputDeg) < 1e-6f)
                {
                    summaries.Add(summary);
                    continue;
                }

                float inputRad = run.InputDeg * Mathf.Deg2Rad;
                float copThreshold = Mathf.Max(4f * noise.CopStd, MinimumCopNoiseM);
                float comThreshold = Mathf.Max(4f * noise.ComStd, MinimumComNoiseM);
                float trunkThreshold = Mathf.Max(4f * noise.TrunkStd, MinimumTrunkNoiseRad);
                var copDeltas = new List<float>(valid.Count);
                var comDeltas = new List<float>(valid.Count);
                var trunkDeltas = new List<float>(valid.Count);
                foreach (PlantSample sample in valid)
                {
                    ZeroStatsAtTick(zeroRuns, run.LoadKg, sample.Tick, out float copMean, out _, out float comMean, out _, out float trunkMean, out _);
                    float copDelta = sample.CopAp - copMean;
                    float comDelta = sample.ComAp - comMean;
                    float trunkDelta = sample.TrunkPitch - trunkMean;
                    copDeltas.Add(copDelta);
                    comDeltas.Add(comDelta);
                    trunkDeltas.Add(trunkDelta);
                    summary.CopPeakDelta = Mathf.Max(summary.CopPeakDelta, Mathf.Abs(copDelta));
                    summary.ComPeakDelta = Mathf.Max(summary.ComPeakDelta, Mathf.Abs(comDelta));
                    summary.TrunkPeakDelta = Mathf.Max(summary.TrunkPeakDelta, Mathf.Abs(trunkDelta));
                    if (summary.CopDelayTicks < 0 && Mathf.Abs(copDelta) >= copThreshold)
                        summary.CopDelayTicks = sample.Tick - PreCommandTicks;
                    if (summary.ComDelayTicks < 0 && Mathf.Abs(comDelta) >= comThreshold)
                        summary.ComDelayTicks = sample.Tick - PreCommandTicks;
                    if (summary.TrunkDelayTicks < 0 && Mathf.Abs(trunkDelta) >= trunkThreshold)
                        summary.TrunkDelayTicks = sample.Tick - PreCommandTicks;
                }

                int localStart = PreCommandTicks + 5;
                int localEnd = Mathf.Min(
                    summary.LastValidTick,
                    PreCommandTicks + 25);
                if (localEnd >= localStart)
                {
                    summary.LocalWindowStartTick = localStart;
                    summary.LocalWindowEndTick = localEnd;
                    summary.CopDcGain = MeanDelta(valid, copDeltas, localStart, localEnd) / inputRad;
                    summary.ComDcGain = MeanDelta(valid, comDeltas, localStart, localEnd) / inputRad;
                    summary.TrunkDcGain = MeanDelta(valid, trunkDeltas, localStart, localEnd) / inputRad;
                }

                int trendStart = valid.Count / 2;
                int trendEnd = valid.Count - 1;
                if (trendEnd > trendStart)
                {
                    float seconds = (float)((valid[trendEnd].Tick - valid[trendStart].Tick) * SimulationConstants.FixedDeltaTimeSeconds);
                    summary.CopTrendPerSecond = (copDeltas[trendEnd] - copDeltas[trendStart]) / Mathf.Max(seconds, 0.01f);
                    summary.ComTrendPerSecond = (comDeltas[trendEnd] - comDeltas[trendStart]) / Mathf.Max(seconds, 0.01f);
                    summary.TrunkTrendPerSecond = (trunkDeltas[trendEnd] - trunkDeltas[trendStart]) / Mathf.Max(seconds, 0.01f);
                }

                summaries.Add(summary);
            }
            return summaries;
        }

        private static NoiseFloor NoiseForLoad(List<PlantRun> zeroRuns, float loadKg)
        {
            float cop = 0f;
            float com = 0f;
            float trunk = 0f;
            for (int tick = 0; tick < PreCommandTicks + PostCommandTicks; tick++)
            {
                ZeroStatsAtTick(zeroRuns, loadKg, tick, out _, out float copStd, out _, out float comStd, out _, out float trunkStd);
                cop = Mathf.Max(cop, copStd);
                com = Mathf.Max(com, comStd);
                trunk = Mathf.Max(trunk, trunkStd);
            }
            return new NoiseFloor(cop, com, trunk);
        }

        private static void ZeroStatsAtTick(
            List<PlantRun> zeroRuns,
            float loadKg,
            int tick,
            out float copMean,
            out float copStd,
            out float comMean,
            out float comStd,
            out float trunkMean,
            out float trunkStd)
        {
            var cop = new List<float>(ZeroRepeats);
            var com = new List<float>(ZeroRepeats);
            var trunk = new List<float>(ZeroRepeats);
            foreach (PlantRun run in zeroRuns)
            {
                if (run.LoadKg != loadKg || tick >= run.Samples.Count)
                    continue;
                PlantSample sample = run.Samples[tick];
                if (!ValidThrough(run, tick) || !sample.HasCop)
                    continue;
                cop.Add(sample.CopAp);
                com.Add(sample.ComAp);
                trunk.Add(sample.TrunkPitch);
            }
            copMean = Mean(cop);
            copStd = Std(cop);
            comMean = Mean(com);
            comStd = Std(com);
            trunkMean = Mean(trunk);
            trunkStd = Std(trunk);
        }

        private static float Mean(List<float> values, int first = 0, int last = -1)
        {
            if (values == null || values.Count == 0)
                return 0f;
            first = Mathf.Clamp(first, 0, values.Count - 1);
            last = last < 0 ? values.Count - 1 : Mathf.Clamp(last, first, values.Count - 1);
            float total = 0f;
            int count = 0;
            for (int index = first; index <= last; index++)
            {
                if (!IsFinite(values[index]))
                    continue;
                total += values[index];
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        private static float MeanDelta(
            List<PlantSample> samples,
            List<float> deltas,
            int firstTick,
            int lastTick)
        {
            float total = 0f;
            int count = 0;
            for (int index = 0; index < samples.Count; index++)
            {
                if (samples[index].Tick < firstTick || samples[index].Tick > lastTick || !IsFinite(deltas[index]))
                    continue;
                total += deltas[index];
                count++;
            }
            return count == 0 ? 0f : total / count;
        }

        private static bool ValidThrough(PlantRun run, int tick)
        {
            if (tick < PreCommandTicks || tick >= run.Samples.Count)
                return false;
            for (int index = PreCommandTicks; index <= tick; index++)
                if (!run.Samples[index].ValidLocal)
                    return false;
            return true;
        }

        private static float Std(List<float> values)
        {
            if (values == null || values.Count < 2)
                return 0f;
            float mean = Mean(values);
            float sum = 0f;
            for (int index = 0; index < values.Count; index++)
            {
                float delta = values[index] - mean;
                sum += delta * delta;
            }
            return Mathf.Sqrt(sum / values.Count);
        }

        private static void WriteNoiseArtifact(List<PlantRun> zeroRuns)
        {
            var csv = new StringBuilder();
            csv.AppendLine("load_kg,tick,time_s,valid_repeats,cop_mean_m,cop_std_m,com_mean_m,com_std_m,trunk_mean_rad,trunk_std_rad");
            foreach (float loadKg in StageALoadsKg)
            {
                for (int tick = 0; tick < PreCommandTicks + PostCommandTicks; tick++)
                {
                    ZeroStatsAtTick(zeroRuns, loadKg, tick, out float copMean, out float copStd, out float comMean, out float comStd, out float trunkMean, out float trunkStd);
                    int valid = 0;
                    foreach (PlantRun run in zeroRuns)
                    {
                        if (run.LoadKg == loadKg && ValidThrough(run, tick) && run.Samples[tick].HasCop)
                            valid++;
                    }
                    csv.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0:R},{1},{2:R},{3},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R}",
                        loadKg, tick, tick * SimulationConstants.FixedDeltaTimeSeconds, valid,
                        copMean, copStd, comMean, comStd, trunkMean, trunkStd));
                }
            }
            WriteArtifact("balance-plant-id-noise.csv", csv.ToString());
        }

        private static void WriteImpulseArtifact(List<PlantRun> zeroRuns, List<PlantRun> impulseRuns)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "experiment,load_kg,channel,input_deg,repeat,tick,time_s,command_tick,valid_local,invalid_reason," +
                "has_support,support_contacts,has_cop,cop_ap_m,com_ap_m,com_velocity_ap_mps,capture_ap_m,capture_margin_m," +
                "support_ap_min_m,support_ap_max_m,pelvis_y_m,trunk_pitch_rad,saddle_separation_m," +
                "ankle_requested_target_deg,ankle_applied_target_deg,hip_requested_target_deg,hip_applied_target_deg," +
                "abdomen_requested_target_deg,abdomen_applied_target_deg,thorax_requested_target_deg,thorax_applied_target_deg," +
                "ankle_actual_deg,hip_actual_deg,abdomen_actual_deg,thorax_actual_deg," +
                "ankle_solver_torque_nm,hip_solver_torque_nm,abdomen_solver_torque_nm,thorax_solver_torque_nm,max_demand");
            AppendRuns(csv, "zero", zeroRuns);
            AppendRuns(csv, "impulse", impulseRuns);
            WriteArtifact("balance-plant-id-impulses.csv", csv.ToString());
        }

        private static void AppendRuns(StringBuilder csv, string experiment, List<PlantRun> runs)
        {
            foreach (PlantRun run in runs)
            {
                foreach (PlantSample sample in run.Samples)
                {
                    csv.AppendLine(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1:R},{2},{3:R},{4},{5},{6:R},{7},{8},{9},{10},{11},{12},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20:R},{21:R},{22:R}," +
                        "{23:R},{24:R},{25:R},{26:R},{27:R},{28:R},{29:R},{30:R},{31:R},{32:R},{33:R},{34:R},{35:R},{36:R},{37:R},{38:R},{39:R}",
                        experiment, run.LoadKg, run.Channel, run.InputDeg, run.Repeat,
                        sample.Tick, sample.TimeSeconds, PreCommandTicks,
                        sample.ValidLocal, sample.InvalidReason, sample.HasSupport, sample.SupportContacts,
                        sample.HasCop, sample.CopAp, sample.ComAp, sample.ComVelocityAp, sample.CaptureAp,
                        sample.CaptureMargin, sample.SupportApMin, sample.SupportApMax, sample.PelvisY,
                        sample.TrunkPitch, sample.SaddleSeparation,
                        sample.AnkleRequestedTargetDeg, sample.AnkleAppliedTargetDeg,
                        sample.HipRequestedTargetDeg, sample.HipAppliedTargetDeg,
                        sample.AbdomenRequestedTargetDeg, sample.AbdomenAppliedTargetDeg,
                        sample.ThoraxRequestedTargetDeg, sample.ThoraxAppliedTargetDeg,
                        sample.AnkleActualDeg, sample.HipActualDeg, sample.AbdomenActualDeg, sample.ThoraxActualDeg,
                        sample.AnkleSolverTorqueNm, sample.HipSolverTorqueNm,
                        sample.AbdomenSolverTorqueNm, sample.ThoraxSolverTorqueNm, sample.MaximumDemand));
                }
            }
        }

        private static void WriteSummaryArtifact(List<ResponseSummary> summaries)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "load_kg,channel,input_deg,valid_samples,first_valid_tick,last_valid_tick,local_window_start_tick,local_window_end_tick," +
                "cop_delay_ticks,com_delay_ticks,trunk_delay_ticks,cop_local_gain_m_per_rad,com_local_gain_m_per_rad," +
                "trunk_local_gain_rad_per_rad,cop_peak_delta_m,com_peak_delta_m,trunk_peak_delta_rad," +
                "cop_trend_m_per_s,com_trend_m_per_s,trunk_trend_rad_per_s");
            foreach (ResponseSummary summary in summaries)
            {
                csv.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:R},{1},{2:R},{3},{4},{5},{6},{7},{8},{9},{10},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R}",
                    summary.LoadKg, summary.Channel, summary.InputDeg, summary.ValidSamples,
                    summary.FirstValidTick, summary.LastValidTick, summary.LocalWindowStartTick,
                    summary.LocalWindowEndTick, summary.CopDelayTicks, summary.ComDelayTicks,
                    summary.TrunkDelayTicks, summary.CopDcGain, summary.ComDcGain, summary.TrunkDcGain,
                    summary.CopPeakDelta, summary.ComPeakDelta, summary.TrunkPeakDelta,
                    summary.CopTrendPerSecond, summary.ComTrendPerSecond, summary.TrunkTrendPerSecond));
            }
            WriteArtifact("balance-plant-id-summary.csv", csv.ToString());
        }

        private static void WriteMatrixArtifact(List<ResponseSummary> summaries)
        {
            var csv = new StringBuilder();
            csv.AppendLine(
                "load_kg,valid_common_window_ticks,cop_ankle_m_per_rad,cop_hip_m_per_rad,cop_trunk_m_per_rad," +
                "com_ankle_m_per_rad,com_hip_m_per_rad,com_trunk_m_per_rad," +
                "trunk_ankle_rad_per_rad,trunk_hip_rad_per_rad,trunk_trunk_rad_per_rad," +
                "scaled_column_norm_ratio,scaled_determinant,conditioning_status");

            foreach (float loadKg in StageALoadsKg)
            {
                ResponseSummary[] columns =
                {
                    SymmetricSummary(summaries, loadKg, "ankle"),
                    SymmetricSummary(summaries, loadKg, "hip"),
                    SymmetricSummary(summaries, loadKg, "trunk")
                };
                int common = CommonWindowTicks(columns);
                float[,] matrix = new float[3, 3];
                for (int column = 0; column < columns.Length; column++)
                {
                    matrix[0, column] = columns[column].CopDcGain;
                    matrix[1, column] = columns[column].ComDcGain;
                    matrix[2, column] = columns[column].TrunkDcGain;
                }

                bool complete = common > 0 && AllFinite(matrix);
                float ratio = float.NaN;
                float determinant = float.NaN;
                if (complete)
                {
                    float[,] scaled =
                    {
                        { matrix[0, 0] / 0.1f, matrix[0, 1] / 0.1f, matrix[0, 2] / 0.1f },
                        { matrix[1, 0] / 0.1f, matrix[1, 1] / 0.1f, matrix[1, 2] / 0.1f },
                        { matrix[2, 0] / 0.5f, matrix[2, 1] / 0.5f, matrix[2, 2] / 0.5f }
                    };
                    float maxColumn = 0f;
                    float minColumn = float.PositiveInfinity;
                    for (int column = 0; column < 3; column++)
                    {
                        float norm = Mathf.Sqrt(
                            scaled[0, column] * scaled[0, column] +
                            scaled[1, column] * scaled[1, column] +
                            scaled[2, column] * scaled[2, column]);
                        maxColumn = Mathf.Max(maxColumn, norm);
                        minColumn = Mathf.Min(minColumn, norm);
                    }
                    ratio = maxColumn / Mathf.Max(minColumn, 1e-6f);
                    determinant = Determinant(scaled);
                }

                csv.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0:R},{1},{2:R},{3:R},{4:R},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13}",
                    loadKg, common,
                    matrix[0, 0], matrix[0, 1], matrix[0, 2],
                    matrix[1, 0], matrix[1, 1], matrix[1, 2],
                    matrix[2, 0], matrix[2, 1], matrix[2, 2],
                    ratio, determinant, complete ? "SUPPORTED" : "INSUFFICIENT_VALID_WINDOW"));
            }
            WriteArtifact("balance-plant-id-matrices.csv", csv.ToString());
        }

        private static ResponseSummary SymmetricSummary(List<ResponseSummary> summaries, float loadKg, string channel)
        {
            ResponseSummary positive = FindSummary(summaries, loadKg, channel, 1f);
            ResponseSummary negative = FindSummary(summaries, loadKg, channel, -1f);
            return new ResponseSummary
            {
                LoadKg = loadKg,
                Channel = channel,
                InputDeg = 1f,
                ValidSamples = Mathf.Min(positive.ValidSamples, negative.ValidSamples),
                LocalWindowStartTick = Mathf.Max(positive.LocalWindowStartTick, negative.LocalWindowStartTick),
                LocalWindowEndTick = Mathf.Min(positive.LocalWindowEndTick, negative.LocalWindowEndTick),
                CopDcGain = Average(positive.CopDcGain, negative.CopDcGain),
                ComDcGain = Average(positive.ComDcGain, negative.ComDcGain),
                TrunkDcGain = Average(positive.TrunkDcGain, negative.TrunkDcGain)
            };
        }

        private static ResponseSummary FindSummary(List<ResponseSummary> summaries, float loadKg, string channel, float inputDeg)
        {
            foreach (ResponseSummary summary in summaries)
            {
                if (summary.LoadKg == loadKg && summary.Channel == channel && Mathf.Abs(summary.InputDeg - inputDeg) < 0.001f)
                    return summary;
            }
            return new ResponseSummary
            {
                LoadKg = loadKg,
                Channel = channel,
                InputDeg = inputDeg,
                LocalWindowStartTick = -1,
                LocalWindowEndTick = -1,
                CopDcGain = float.NaN,
                ComDcGain = float.NaN,
                TrunkDcGain = float.NaN
            };
        }

        private static int CommonWindowTicks(ResponseSummary[] columns)
        {
            int start = 0;
            int end = int.MaxValue;
            foreach (ResponseSummary summary in columns)
            {
                if (summary.LocalWindowStartTick < 0 || summary.LocalWindowEndTick < summary.LocalWindowStartTick)
                    return 0;
                start = Mathf.Max(start, summary.LocalWindowStartTick);
                end = Mathf.Min(end, summary.LocalWindowEndTick);
            }
            return Mathf.Max(0, end - start + 1);
        }

        private static bool AllFinite(float[,] matrix)
        {
            for (int row = 0; row < 3; row++)
                for (int column = 0; column < 3; column++)
                    if (!IsFinite(matrix[row, column]))
                        return false;
            return true;
        }

        private static float Determinant(float[,] matrix) =>
            matrix[0, 0] * (matrix[1, 1] * matrix[2, 2] - matrix[1, 2] * matrix[2, 1]) -
            matrix[0, 1] * (matrix[1, 0] * matrix[2, 2] - matrix[1, 2] * matrix[2, 0]) +
            matrix[0, 2] * (matrix[1, 0] * matrix[2, 1] - matrix[1, 1] * matrix[2, 0]);

        private static float Average(float first, float second) =>
            IsFinite(first) && IsFinite(second) ? 0.5f * (first + second) : float.NaN;

        private static void WriteArtifact(string filename, string contents)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), contents);
        }

        private sealed class PlantRun
        {
            public PlantRun(float loadKg, string channel, float inputDeg, int repeat)
            {
                LoadKg = loadKg;
                Channel = channel;
                InputDeg = inputDeg;
                Repeat = repeat;
            }

            public float LoadKg { get; }
            public string Channel { get; }
            public float InputDeg { get; }
            public int Repeat { get; }
            public List<PlantSample> Samples { get; } = new List<PlantSample>(PreCommandTicks + PostCommandTicks);
        }

        private sealed class PlantSample
        {
            public int Tick;
            public double TimeSeconds;
            public bool ValidLocal;
            public string InvalidReason;
            public bool HasSupport;
            public int SupportContacts;
            public bool HasCop;
            public float CopAp;
            public float ComAp;
            public float ComVelocityAp;
            public float CaptureAp;
            public float CaptureMargin;
            public float SupportApMin;
            public float SupportApMax;
            public float PelvisY;
            public float TrunkPitch;
            public float SaddleSeparation;
            public float AnkleRequestedTargetDeg;
            public float AnkleAppliedTargetDeg;
            public float HipRequestedTargetDeg;
            public float HipAppliedTargetDeg;
            public float AbdomenRequestedTargetDeg;
            public float AbdomenAppliedTargetDeg;
            public float ThoraxRequestedTargetDeg;
            public float ThoraxAppliedTargetDeg;
            public float AnkleActualDeg;
            public float HipActualDeg;
            public float AbdomenActualDeg;
            public float ThoraxActualDeg;
            public float AnkleSolverTorqueNm;
            public float HipSolverTorqueNm;
            public float AbdomenSolverTorqueNm;
            public float ThoraxSolverTorqueNm;
            public float MaximumDemand;
        }

        private readonly struct NoiseFloor
        {
            public NoiseFloor(float copStd, float comStd, float trunkStd)
            {
                CopStd = copStd;
                ComStd = comStd;
                TrunkStd = trunkStd;
            }

            public float CopStd { get; }
            public float ComStd { get; }
            public float TrunkStd { get; }
        }

        private sealed class ResponseSummary
        {
            public float LoadKg;
            public string Channel;
            public float InputDeg;
            public int ValidSamples;
            public int FirstValidTick;
            public int LastValidTick;
            public int LocalWindowStartTick;
            public int LocalWindowEndTick;
            public int CopDelayTicks;
            public int ComDelayTicks;
            public int TrunkDelayTicks;
            public float CopDcGain;
            public float ComDcGain;
            public float TrunkDcGain;
            public float CopPeakDelta;
            public float ComPeakDelta;
            public float TrunkPeakDelta;
            public float CopTrendPerSecond;
            public float ComTrendPerSecond;
            public float TrunkTrendPerSecond;
        }
    }
}
