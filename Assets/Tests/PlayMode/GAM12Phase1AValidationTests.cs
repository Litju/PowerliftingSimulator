using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM12Phase1AValidationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const string RepeatabilityArtifactPath = "Artifacts/Measurements/GAM-12-phase1a-repeatability.json";
        private const string PerformanceArtifactPath = "Artifacts/Measurements/GAM-12-phase1a-performance.json";
        private const int RepeatCount = 3;
        private const int AttemptSampleCount = 800;
        private const int TimingWarmupTickCount = 100;
        private const int TimingSampleCount = 300;

        // These are declared before the repeatability fixture runs. The values
        // come from existing repository contracts: the GAM-9 reset fixture
        // uses 0.1 mm positions, 0.0001-unit velocities, and 0.01-degree
        // orientations; the foundation coordinate contract supplies the
        // 1e-5 numerical tolerance; existing load/drive tests use 0.0001
        // absolute engineering-unit tolerance.
        private const float PositionToleranceM = PhysicalAthleteDefinition.AnchorToleranceMeters;
        private const float LinearVelocityToleranceMps = 0.0001f;
        private const float AngularVelocityToleranceRadS = 0.0001f;
        private const float DimensionlessTolerance = FoundationTolerances.UnitConversionRoundTrip;
        private const float AngleToleranceRad = FoundationTolerances.UnitConversionRoundTrip;
        private const float MassToleranceKg = 0.0001f;
        private const float ForceToleranceNm = 0.0001f;
        private const float ImpulseToleranceNs = 0.0001f;
        private const float OrientationToleranceDegrees = 0.01f;

        private static readonly float[] QualificationLoadsKg = { 0f, 25f };
        private FoundationBootstrap _bootstrap;
        private SquatPhysicalPrototypeController _controller;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_bootstrap != null && _bootstrap.Runtime != null && _bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = _bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }

            if (_bootstrap != null)
                UnityEngine.Object.DestroyImmediate(_bootstrap.gameObject);
            _bootstrap = null;
            _controller = null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM12_P1A_SAME_MACHINE_COMPLETE_TRACE_REPEATABILITY_0KG_AND_25KG()
        {
            yield return LoadQualificationScene();
            DisableSceneTickOwners();

            LoadRepeatabilityResult[] results = new LoadRepeatabilityResult[QualificationLoadsKg.Length];
            for (int loadIndex = 0; loadIndex < QualificationLoadsKg.Length; loadIndex++)
            {
                float loadKg = QualificationLoadsKg[loadIndex];
                SquatObservationSnapshot[][] traces = new SquatObservationSnapshot[RepeatCount][];
                for (int repeatIndex = 0; repeatIndex < RepeatCount; repeatIndex++)
                {
                    if (loadIndex != 0 || repeatIndex != 0)
                        yield return LoadQualificationScene();
                    DisableSceneTickOwners();
                    traces[repeatIndex] = RunRecordedAttempt(loadKg);
                }

                results[loadIndex] = CompareTraceRepeats(loadKg, traces);
            }

            WriteArtifact(RepeatabilityArtifactPath, new RepeatabilityArtifact
            {
                schema = "GAM12_P1A_COMPLETE_TRACE_REPEATABILITY_V1",
                unityVersion = Application.unityVersion,
                scene = QualificationScene,
                fixedStepSeconds = SimulationConstants.FixedDeltaTimeSeconds,
                repeatCount = RepeatCount,
                sampleCountPerAttempt = AttemptSampleCount,
                inputSequence = "Canonical fresh scene reset via production LoadSceneMode.Single before each attempt; StartSquat(); 800 x FoundationRuntime.AdvanceRenderFrame(0.01).",
                resetBoundary = "Equivalent to SquatPhysicalPrototypeController.ResetPrototype: SceneManager.LoadScene(activeScene.path, LoadSceneMode.Single), which rebuilds the production physics scene and contact state.",
                exactComparisonClass = "simulation tick/time alignment; enums; availability flags; intent edges/held flags; booleans; counts; schema identifiers; quality flags.",
                floatWithToleranceComparisonClass = "positions/landmarks/COM/support; velocities; scalar diagnostics; lengths/impulses; canonical quaternion shortest-arc orientation.",
                toleranceBasis = "Position/length/depth/support=PhysicalAthleteDefinition.AnchorToleranceMeters (0.0001 m); linear and angular velocity=qualified GAM-9 repeatability tolerance (0.0001 in declared units); dimensionless and joint-angle channels=FoundationTolerances.UnitConversionRoundTrip (0.00001, radians for angles); mass/force/impulse=existing load/drive physics scalar tolerance (0.0001 in declared units); orientation=qualified GAM-9 repeatability tolerance (0.01 deg); time=FoundationTolerances.SimulationTimeMapping.",
                loads = results
            });

            for (int index = 0; index < results.Length; index++)
            {
                LoadRepeatabilityResult result = results[index];
                Assert.That(result.tickAlignment, Is.True, result.loadKg + " kg trace tick alignment failed.");
                Assert.That(result.withinDeclaredContract, Is.True, FormatRepeatabilityFailure(result));
            }
        }

        [UnityTest]
        public IEnumerator GAM12_P1A_SYSTEM_COM_BODY_LOCAL_COM_INVARIANT()
        {
            yield return LoadQualificationScene();
            DisableSceneTickOwners();

            _controller.SetLoad(25f);
            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            Assert.That(rig, Is.Not.Null);
            foreach (PhysicalAthleteRig.SegmentRuntime segment in rig.Segments.Values)
            {
                Assert.That(
                    segment.Body.centerOfMass.magnitude,
                    Is.LessThanOrEqualTo(FoundationTolerances.PhysicsFixturePositionMeters),
                    "GAM12_SYSTEM_COM_BODY_LOCAL_COM_INVARIANT: " + segment.Recipe.Id);
            }

            PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>();
            Assert.That(barbell, Is.Not.Null);
            Assert.That(barbell.Body, Is.Not.Null);
            Assert.That(
                barbell.Body.centerOfMass.magnitude,
                Is.LessThanOrEqualTo(FoundationTolerances.PhysicsFixturePositionMeters),
                "GAM12_SYSTEM_COM_BODY_LOCAL_COM_INVARIANT: barbell");
        }

        [UnityTest]
        public IEnumerator GAM12_P1A_TRACE_APPEND_PERTURBATION_AND_WARM_TIMING()
        {
            LoadPerformanceResult[] results = new LoadPerformanceResult[QualificationLoadsKg.Length];
            for (int loadIndex = 0; loadIndex < QualificationLoadsKg.Length; loadIndex++)
            {
                float loadKg = QualificationLoadsKg[loadIndex];
                yield return LoadQualificationScene();
                DisableSceneTickOwners();
                PhysicalBodyObservation[][] recordingOff = RunFixedAttemptBodyTrace(loadKg, false);
                yield return LoadQualificationScene();
                DisableSceneTickOwners();
                PhysicalBodyObservation[][] recordingOn = RunFixedAttemptBodyTrace(loadKg, true);
                BodyComparison perturbation = CompareBodyTraces(recordingOff, recordingOn);

                yield return LoadQualificationScene();
                DisableSceneTickOwners();
                TimingSummary offTiming = MeasureTiming(loadKg, false);
                yield return LoadQualificationScene();
                DisableSceneTickOwners();
                TimingSummary onTiming = MeasureTiming(loadKg, true);
                results[loadIndex] = new LoadPerformanceResult
                {
                    loadKg = loadKg,
                    traceAppendPerturbationWithinContract = perturbation.WithinContract,
                    traceAppendMaxPositionDeltaM = perturbation.MaxPositionDeltaM,
                    traceAppendMaxRotationDeltaDeg = perturbation.MaxRotationDeltaDeg,
                    traceAppendMaxLinearVelocityDeltaMps = perturbation.MaxLinearVelocityDelta,
                    traceAppendMaxAngularVelocityDeltaRadS = perturbation.MaxAngularVelocityDelta,
                    timingRecordingOff = offTiming.ToArtifact(),
                    timingRecordingOn = onTiming.ToArtifact(),
                    timingP95DeltaOnMinusOffMs = onTiming.P95Ms - offTiming.P95Ms,
                    timingInterpretation = InterpretPerformance(offTiming, onTiming)
                };
            }

            WriteArtifact(PerformanceArtifactPath, new PerformanceArtifact
            {
                schema = "GAM12_P1A_TRACE_APPEND_PERFORMANCE_V1",
                unityVersion = Application.unityVersion,
                scene = QualificationScene,
                environment = "Unity 6000.3.22f1 PlayMode with graphics disabled; Stopwatch.GetTimestamp around FoundationRuntime.AdvanceRenderFrame(0.01), one 100 Hz tick per call, after warm-up. This measures the total fixed physics/observation path, not collector-only time.",
                traceRecordingComparison = "The same production collector/runtime and fresh production scene-reload reset are used with trace recording OFF and ON; all 17 registered physical bodies are compared at every 800-tick sample. This is TRACE_APPEND_PERTURBATION, not a PRE_P1_RUNTIME versus POST_P1_RUNTIME comparison.",
                traceAppendTrajectorySampleCount = AttemptSampleCount,
                sampleCount = TimingSampleCount,
                warmupTickCount = TimingWarmupTickCount,
                gcAllocStatus = "UNRESOLVED",
                gcBytesPerPhysicsTick = "NOT_RELIABLY_ISOLATED",
                gcMeasurementReason = "The existing standalone ProfilerRecorder harness qualifies the GAM-9 physical-athlete scene, not the squat collector. PlayMode frame counters cannot honestly isolate one fixed tick from test-runner work; static hot-path inspection is not a zero-byte measurement.",
                loads = results
            });

            for (int index = 0; index < results.Length; index++)
            {
                LoadPerformanceResult result = results[index];
                Assert.That(result.traceAppendPerturbationWithinContract, Is.True,
                    "TRACE_APPEND_PERTURBATION exceeded the declared body-state contract at " + result.loadKg + " kg.");
            }
        }

        private IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            _bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            _controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(_bootstrap, Is.Not.Null);
            Assert.That(_bootstrap.Runtime, Is.Not.Null);
            Assert.That(_bootstrap.Runtime.IsInitialized, Is.True);
            Assert.That(_controller, Is.Not.Null);
            for (int frame = 0; frame < 5 && !_controller.IsInitialized; frame++)
                yield return null;
            Assert.That(_controller.IsInitialized, Is.True, _controller.StartupFailure);
            Assert.That(_controller.ObservationCollector, Is.Not.Null);
        }

        private void DisableSceneTickOwners()
        {
            _controller.enabled = false;
            _bootstrap.enabled = false;
        }

        private SquatObservationSnapshot[] RunRecordedAttempt(float loadKg)
        {
            _controller.SetLoad(loadKg);
            _controller.Adapter.StartSquat();
            SquatObservationCollector collector = _controller.ObservationCollector;
            collector.BeginRecording();
            for (int index = 0; index < AttemptSampleCount; index++)
                Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
            collector.EndRecording();

            SquatObservationSnapshot[] snapshots = new SquatObservationSnapshot[collector.Trace.Count];
            for (int index = 0; index < snapshots.Length; index++)
                snapshots[index] = collector.Trace.GetSnapshot(index);
            Assert.That(snapshots.Length, Is.EqualTo(AttemptSampleCount));
            return snapshots;
        }

        private PhysicalBodyObservation[][] RunFixedAttemptBodyTrace(float loadKg, bool recording)
        {
            _controller.SetLoad(loadKg);
            _controller.Adapter.StartSquat();
            SquatObservationCollector collector = _controller.ObservationCollector;
            if (recording)
                collector.BeginRecording();

            PhysicalBodyObservation[][] bodyTrace = new PhysicalBodyObservation[AttemptSampleCount][];
            for (int index = 0; index < AttemptSampleCount; index++)
            {
                Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                bodyTrace[index] = CopyBodies(_bootstrap.Runtime.CurrentObservation);
            }

            if (recording)
            {
                collector.EndRecording();
                Assert.That(collector.Trace.Count, Is.EqualTo(AttemptSampleCount));
            }

            return bodyTrace;
        }

        private TimingSummary MeasureTiming(float loadKg, bool recording)
        {
            _controller.SetLoad(loadKg);
            _controller.Adapter.StartSquat();
            SquatObservationCollector collector = _controller.ObservationCollector;
            if (recording)
                collector.BeginRecording();

            for (int index = 0; index < TimingWarmupTickCount; index++)
                Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));

            double[] samplesMs = new double[TimingSampleCount];
            for (int index = 0; index < samplesMs.Length; index++)
            {
                long start = Stopwatch.GetTimestamp();
                int ticks = _bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                long end = Stopwatch.GetTimestamp();
                Assert.That(ticks, Is.EqualTo(1));
                samplesMs[index] = (end - start) * 1000d / Stopwatch.Frequency;
            }

            if (recording)
                collector.EndRecording();
            return TimingSummary.From(samplesMs);
        }

        private static LoadRepeatabilityResult CompareTraceRepeats(
            float loadKg,
            SquatObservationSnapshot[][] traces)
        {
            LoadRepeatabilityResult result = new LoadRepeatabilityResult
            {
                loadKg = loadKg,
                repeatCount = traces.Length,
                sampleCount = traces[0].Length,
                pairVerdicts = new string[traces.Length * (traces.Length - 1) / 2]
            };
            ComparisonAccumulator maximum = new ComparisonAccumulator();
            ComparisonAccumulator finalState = new ComparisonAccumulator();
            int pairIndex = 0;

            for (int leftIndex = 0; leftIndex < traces.Length; leftIndex++)
            {
                if (traces[leftIndex].Length != AttemptSampleCount)
                    result.tickAlignment = false;
                for (int rightIndex = leftIndex + 1; rightIndex < traces.Length; rightIndex++)
                {
                    SquatObservationSnapshot[] left = traces[leftIndex];
                    SquatObservationSnapshot[] right = traces[rightIndex];
                    if (left.Length != right.Length)
                    {
                        // Record the verdict for this pair only. Writing the
                        // shared accumulator's verdict would report merged
                        // state here and mark every later pair in this load as
                        // a repeatability failure it was never compared for.
                        ComparisonAccumulator sampleCountPair = new ComparisonAccumulator();
                        sampleCountPair.Mismatch(0ul, "sample_count");
                        maximum.Merge(sampleCountPair);
                        result.tickAlignment = false;
                        result.pairVerdicts[pairIndex++] = sampleCountPair.Verdict;
                        continue;
                    }

                    ComparisonAccumulator pair = new ComparisonAccumulator();
                    for (int sampleIndex = 0; sampleIndex < left.Length; sampleIndex++)
                        CompareSnapshot(left[sampleIndex], right[sampleIndex], pair);
                    CompareSnapshot(left[left.Length - 1], right[right.Length - 1], finalState);
                    maximum.Merge(pair);
                    result.pairVerdicts[pairIndex++] = pair.Verdict;
                    if (!pair.TickAlignment)
                        result.tickAlignment = false;
                }
            }

            result.tickAlignment = result.tickAlignment && maximum.TickAlignment;
            result.earliestDivergenceTick = maximum.HasEarliestDivergence
                ? maximum.EarliestDivergenceTick.ToString()
                : "NONE";
            result.earliestOutOfContractTick = maximum.HasEarliestOutOfContract
                ? maximum.EarliestOutOfContractTick.ToString()
                : "NONE";
            result.firstMismatch = maximum.FirstMismatch.Length == 0 ? "NONE" : maximum.FirstMismatch;
            result.withinDeclaredContract = result.tickAlignment && maximum.WithinContract;
            result.finalStateMaxPositionM = finalState.MaximumFor(PositionFamily);
            result.finalStateMaxVelocityMps = finalState.MaximumFor(VelocityFamily);
            result.finalStateMaxAngularVelocityRadS = finalState.MaximumFor(AngularVelocityFamily);
            result.finalStateMaxOrientationDeg = finalState.MaximumFor(OrientationFamily) * Mathf.Rad2Deg;
            result.maximumAbsoluteDifferenceByChannelFamily = maximum.ToArtifacts();
            result.finalStateDifferenceByChannelFamily = finalState.ToArtifacts();
            result.verdict = !result.withinDeclaredContract
                ? "REPEATABILITY_FAIL"
                : !maximum.AnyDifference
                    ? "EXACT_REPEATABLE"
                    : "REPEATABLE_WITHIN_DECLARED_TOLERANCE";
            return result;
        }

        private static void CompareSnapshot(
            SquatObservationSnapshot left,
            SquatObservationSnapshot right,
            ComparisonAccumulator result)
        {
            ulong tick = left.SimulationTick;
            CheckExact(result, tick, left.SimulationTick, right.SimulationTick, "simulation_tick");
            CheckDouble(result, tick, TimeFamily, left.SimulationTimeSeconds, right.SimulationTimeSeconds, FoundationTolerances.SimulationTimeMapping);
            CheckDouble(result, tick, TimeFamily, left.FixedStepSeconds, right.FixedStepSeconds, FoundationTolerances.SimulationTimeMapping);
            CheckExact(result, tick, left.HasAttemptRelativeTime, right.HasAttemptRelativeTime, "attempt_relative_time_available");
            CheckDouble(result, tick, TimeFamily, left.AttemptRelativeTimeSeconds, right.AttemptRelativeTimeSeconds, FoundationTolerances.SimulationTimeMapping);
            CheckExact(result, tick, left.State, right.State, "squat_state");
            CheckExact(result, tick, left.Direction, right.Direction, "phase_direction");
            CheckFloat(result, tick, PhaseFamily, left.Sq, right.Sq, DimensionlessTolerance);
            CompareIntent(left.Intent, right.Intent, tick, result);
            CompareBar(left.Bar, right.Bar, tick, result);
            CompareDepth(left.Depth, right.Depth, tick, result);
            CompareSupport(left.Support, right.Support, tick, result);
            CompareFoot(left.LeftFoot, right.LeftFoot, tick, "left_foot", result);
            CompareFoot(left.RightFoot, right.RightFoot, tick, "right_foot", result);
            CompareJoints(left.Joints, right.Joints, tick, result);
            CheckVector(result, tick, PositionFamily, left.PelvisPositionWorldMeters, right.PelvisPositionWorldMeters, PositionToleranceM);
            CheckVector(result, tick, VelocityFamily, left.PelvisLinearVelocityWorldMetersPerSecond, right.PelvisLinearVelocityWorldMetersPerSecond, LinearVelocityToleranceMps);
            CheckExact(result, tick, left.PelvisAvailability, right.PelvisAvailability, "pelvis_availability");
            CheckQuaternion(result, tick, OrientationFamily, left.ThoraxOrientationWorldFromBody, right.ThoraxOrientationWorldFromBody, OrientationToleranceDegrees * Mathf.Deg2Rad);
            CheckFloat(result, tick, JointAngleFamily, left.TrunkWorldPitchRadians, right.TrunkWorldPitchRadians, AngleToleranceRad);
            CheckExact(result, tick, left.TrunkAvailability, right.TrunkAvailability, "trunk_availability");
            CheckExact(result, tick, left.DriveAvailability, right.DriveAvailability, "drive_availability");
            CheckExact(result, tick, left.DriveSaturated, right.DriveSaturated, "drive_saturated");
            CheckFloat(result, tick, DriveFamily, left.MaximumModeledDemand, right.MaximumModeledDemand, DimensionlessTolerance);
            CheckExact(result, tick, left.Quality, right.Quality, "quality_flags");
            CheckExact(result, tick, left.Schema, right.Schema, "snapshot_schema");
        }

        private static void CompareIntent(
            SquatIntentSnapshot left,
            SquatIntentSnapshot right,
            ulong tick,
            ComparisonAccumulator result)
        {
            CheckExact(result, tick, left.Tick, right.Tick, "intent_tick");
            CheckDouble(result, tick, TimeFamily, left.SimulationTimeSeconds, right.SimulationTimeSeconds, FoundationTolerances.SimulationTimeMapping);
            CheckExact(result, tick, left.Edges, right.Edges, "intent_edges");
            CheckExact(result, tick, left.EdgeEventCount, right.EdgeEventCount, "intent_edge_count");
            CheckFloat(result, tick, IntentFamily, left.Brace01, right.Brace01, DimensionlessTolerance);
            CheckFloat(result, tick, IntentFamily, left.Yield01, right.Yield01, DimensionlessTolerance);
            CheckFloat(result, tick, IntentFamily, left.Drive01, right.Drive01, DimensionlessTolerance);
            CheckFloat(result, tick, IntentFamily, left.BalanceX, right.BalanceX, DimensionlessTolerance);
            CheckFloat(result, tick, IntentFamily, left.Grip01, right.Grip01, DimensionlessTolerance);
            CheckExact(result, tick, left.Held, right.Held, "intent_held_flags");
        }

        private static void CompareBar(
            SquatBarObservation left,
            SquatBarObservation right,
            ulong tick,
            ComparisonAccumulator result)
        {
            CheckExact(result, tick, left.Availability, right.Availability, "bar_availability");
            CheckVector(result, tick, PositionFamily, left.PositionWorldMeters, right.PositionWorldMeters, PositionToleranceM);
            CheckVector(result, tick, VelocityFamily, left.LinearVelocityWorldMetersPerSecond, right.LinearVelocityWorldMetersPerSecond, LinearVelocityToleranceMps);
            CheckQuaternion(result, tick, OrientationFamily, left.OrientationWorldFromBar, right.OrientationWorldFromBar, OrientationToleranceDegrees * Mathf.Deg2Rad);
            CheckVector(result, tick, AngularVelocityFamily, left.AngularVelocityBarRadiansPerSecond, right.AngularVelocityBarRadiansPerSecond, AngularVelocityToleranceRadS);
            CheckFloat(result, tick, MassFamily, left.LoadKilograms, right.LoadKilograms, MassToleranceKg);
            CheckExact(result, tick, left.SaddleAvailability, right.SaddleAvailability, "saddle_availability");
            CheckExact(result, tick, left.SaddleAttached, right.SaddleAttached, "saddle_attached");
            CheckExact(result, tick, left.SaddleBroken, right.SaddleBroken, "saddle_broken");
            CheckFloat(result, tick, PositionFamily, left.SaddleSeparationMeters, right.SaddleSeparationMeters, PositionToleranceM);
        }

        private static void CompareDepth(
            SquatDepthLandmarks left,
            SquatDepthLandmarks right,
            ulong tick,
            ComparisonAccumulator result)
        {
            CheckExact(result, tick, left.Availability, right.Availability, "depth_availability");
            CheckFloat(result, tick, DepthFamily, left.LeftHipCreaseY, right.LeftHipCreaseY, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.RightHipCreaseY, right.RightHipCreaseY, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.LeftKneeTopY, right.LeftKneeTopY, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.RightKneeTopY, right.RightKneeTopY, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.DepthMarginM, right.DepthMarginM, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.LeftDepthM, right.LeftDepthM, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.RightDepthM, right.RightDepthM, PositionToleranceM);
            CheckFloat(result, tick, DepthFamily, left.WorstSideDepthM, right.WorstSideDepthM, PositionToleranceM);
        }

        private static void CompareSupport(
            SquatSupportObservation left,
            SquatSupportObservation right,
            ulong tick,
            ComparisonAccumulator result)
        {
            CheckExact(result, tick, left.SystemComAvailability, right.SystemComAvailability, "system_com_availability");
            CheckVector(result, tick, PositionFamily, left.SystemComWorldMeters, right.SystemComWorldMeters, PositionToleranceM);
            CheckVector(result, tick, VelocityFamily, left.SystemComVelocityWorldMetersPerSecond, right.SystemComVelocityWorldMetersPerSecond, LinearVelocityToleranceMps);
            CheckFloat(result, tick, MassFamily, left.SystemMassKilograms, right.SystemMassKilograms, MassToleranceKg);
            CheckExact(result, tick, left.SupportAvailability, right.SupportAvailability, "support_availability");
            CheckExact(result, tick, left.HasSupport, right.HasSupport, "has_support");
            CheckFloat(result, tick, PositionFamily, left.SupportApMinM, right.SupportApMinM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.SupportApMaxM, right.SupportApMaxM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.SupportMlMinM, right.SupportMlMinM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.SupportMlMaxM, right.SupportMlMaxM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.SupportPlaneY, right.SupportPlaneY, PositionToleranceM);
            CheckVector(result, tick, PositionFamily, left.SupportCenterWorldMeters, right.SupportCenterWorldMeters, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.ComToSupportApFrontMarginM, right.ComToSupportApFrontMarginM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.ComToSupportApRearMarginM, right.ComToSupportApRearMarginM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.ComToSupportMlRightMarginM, right.ComToSupportMlRightMarginM, PositionToleranceM);
            CheckFloat(result, tick, PositionFamily, left.ComToSupportMlLeftMarginM, right.ComToSupportMlLeftMarginM, PositionToleranceM);
            CheckExact(result, tick, left.SupportContactCount, right.SupportContactCount, "support_contact_count");
            CheckExact(result, tick, left.EngineContactPointAvailability, right.EngineContactPointAvailability, "engine_contact_point_availability");
            CheckVector(result, tick, PositionFamily, left.EngineContactPointWorldMeters, right.EngineContactPointWorldMeters, PositionToleranceM);
            CheckFloat(result, tick, ImpulseFamily, left.TotalNormalImpulseNewtonSeconds, right.TotalNormalImpulseNewtonSeconds, ImpulseToleranceNs);
        }

        private static void CompareFoot(
            SquatFootObservation left,
            SquatFootObservation right,
            ulong tick,
            string side,
            ComparisonAccumulator result)
        {
            CheckExact(result, tick, left.Availability, right.Availability, side + "_availability");
            CheckExact(result, tick, left.IsInContact, right.IsInContact, side + "_in_contact");
            CheckExact(result, tick, left.ContactCount, right.ContactCount, side + "_contact_count");
            CheckExact(result, tick, left.CompletedContactCount, right.CompletedContactCount, side + "_completed_contact_count");
            CheckFloat(result, tick, PositionFamily, left.SlipAccumulatedMeters, right.SlipAccumulatedMeters, PositionToleranceM);
            CheckFloat(result, tick, VelocityFamily, left.SlipSpeedMetersPerSecond, right.SlipSpeedMetersPerSecond, LinearVelocityToleranceMps);
        }

        private static void CompareJoints(
            SquatJointObservationSet left,
            SquatJointObservationSet right,
            ulong tick,
            ComparisonAccumulator result)
        {
            CompareJoint(left.LeftKnee, right.LeftKnee, tick, "left_knee", result);
            CompareJoint(left.RightKnee, right.RightKnee, tick, "right_knee", result);
            CompareJoint(left.LeftHip, right.LeftHip, tick, "left_hip", result);
            CompareJoint(left.RightHip, right.RightHip, tick, "right_hip", result);
            CompareJoint(left.LeftAnkle, right.LeftAnkle, tick, "left_ankle", result);
            CompareJoint(left.RightAnkle, right.RightAnkle, tick, "right_ankle", result);
            CompareJoint(left.Abdomen, right.Abdomen, tick, "abdomen", result);
            CompareJoint(left.Thorax, right.Thorax, tick, "thorax", result);
        }

        private static void CompareJoint(
            SquatJointObservation left,
            SquatJointObservation right,
            ulong tick,
            string joint,
            ComparisonAccumulator result)
        {
            CheckExact(result, tick, left.JointAvailability, right.JointAvailability, joint + "_availability");
            CheckFloat(result, tick, JointAngleFamily, left.ActualAngleRadians, right.ActualAngleRadians, AngleToleranceRad);
            CheckVector(result, tick, JointVelocityFamily, left.ActualAngularVelocityRadiansPerSecond, right.ActualAngularVelocityRadiansPerSecond, AngularVelocityToleranceRadS);
            CheckFloat(result, tick, JointAngleFamily, left.ReferenceAngleRadians, right.ReferenceAngleRadians, AngleToleranceRad);
            CheckFloat(result, tick, JointAngleFamily, left.ActualReferenceErrorRadians, right.ActualReferenceErrorRadians, AngleToleranceRad);
            CheckFloat(result, tick, JointScalarFamily, left.LimitProximity, right.LimitProximity, DimensionlessTolerance);
            CheckExact(result, tick, left.DriveAvailability, right.DriveAvailability, joint + "_drive_availability");
            CheckFloat(result, tick, JointScalarFamily, left.ModeledDemand, right.ModeledDemand, DimensionlessTolerance);
            CheckFloat(result, tick, ForceFamily, left.MaximumForceNewtonMeters, right.MaximumForceNewtonMeters, ForceToleranceNm);
            CheckFloat(result, tick, JointScalarFamily, left.Activation, right.Activation, DimensionlessTolerance);
            CheckFloat(result, tick, JointScalarFamily, left.CapacityScale, right.CapacityScale, DimensionlessTolerance);
        }

        private static BodyComparison CompareBodies(
            PhysicalBodyObservation[] expected,
            PhysicalBodyObservation[] actual)
        {
            BodyComparison result = new BodyComparison();
            if (expected.Length != actual.Length)
            {
                result.WithinContract = false;
                return result;
            }

            for (int index = 0; index < expected.Length; index++)
            {
                PhysicalBodyObservation left = expected[index];
                PhysicalBodyObservation right = actual[index];
                if (!string.Equals(left.BodyId, right.BodyId, StringComparison.Ordinal) ||
                    Math.Abs(left.MassKilograms - right.MassKilograms) > MassToleranceKg)
                    result.WithinContract = false;

                result.MaxPositionDeltaM = Mathf.Max(result.MaxPositionDeltaM, VectorDelta(left.PositionMeters, right.PositionMeters));
                result.MaxLinearVelocityDelta = Mathf.Max(result.MaxLinearVelocityDelta, VectorDelta(left.LinearVelocityMetersPerSecond, right.LinearVelocityMetersPerSecond));
                result.MaxAngularVelocityDelta = Mathf.Max(result.MaxAngularVelocityDelta, VectorDelta(left.AngularVelocityRadiansPerSecond, right.AngularVelocityRadiansPerSecond));
                result.MaxRotationDeltaDeg = Mathf.Max(result.MaxRotationDeltaDeg, left.RotationWorldFromBody.ShortestArcRadiansTo(right.RotationWorldFromBody) * Mathf.Rad2Deg);
            }

            result.WithinContract &= result.MaxPositionDeltaM <= PositionToleranceM &&
                result.MaxLinearVelocityDelta <= LinearVelocityToleranceMps &&
                result.MaxAngularVelocityDelta <= AngularVelocityToleranceRadS &&
                result.MaxRotationDeltaDeg <= OrientationToleranceDegrees;
            return result;
        }

        private static BodyComparison CompareBodyTraces(
            PhysicalBodyObservation[][] expected,
            PhysicalBodyObservation[][] actual)
        {
            BodyComparison result = new BodyComparison();
            if (expected.Length != actual.Length)
            {
                result.WithinContract = false;
                return result;
            }

            for (int tickIndex = 0; tickIndex < expected.Length; tickIndex++)
                result.Merge(CompareBodies(expected[tickIndex], actual[tickIndex]));
            return result;
        }

        private static PhysicalBodyObservation[] CopyBodies(PhysicalObservation observation)
        {
            PhysicalBodyObservation[] copy = new PhysicalBodyObservation[observation.BodyCount];
            for (int index = 0; index < copy.Length; index++)
                copy[index] = observation.BodyAt(index);
            return copy;
        }

        private static float VectorDelta(Vector3Value left, Vector3Value right)
        {
            if (float.IsNaN(left.X) && float.IsNaN(right.X) &&
                float.IsNaN(left.Y) && float.IsNaN(right.Y) &&
                float.IsNaN(left.Z) && float.IsNaN(right.Z))
                return 0f;
            return (left - right).Length;
        }

        private static string InterpretPerformance(TimingSummary off, TimingSummary on)
        {
            double deltaP95Ms = on.P95Ms - off.P95Ms;
            if (on.P95Ms <= 2d)
                return "PERFORMANCE_REGRESSION=NO_EVIDENCE; recording-ON p95 is within the existing 2.0 ms one-tick budget, and the measured OFF/ON delta is reported but does not establish causality in this Editor fallback."
                    + " OFF/ON p95=" + off.P95Ms.ToString("F4") + "/" + on.P95Ms.ToString("F4") + " ms; ON-OFF delta=" + deltaP95Ms.ToString("F4") + " ms.";
            return "PERFORMANCE_REGRESSION=UNRESOLVED; recording-ON p95 exceeded the 2.0 ms budget in the Editor PlayMode fallback, so this path cannot distinguish target-machine regression from environment overhead."
                + " OFF/ON p95=" + off.P95Ms.ToString("F4") + "/" + on.P95Ms.ToString("F4") + " ms; ON-OFF delta=" + deltaP95Ms.ToString("F4") + " ms.";
        }

        private static string FormatRepeatabilityFailure(LoadRepeatabilityResult result)
        {
            return result.loadKg + " kg repeatability verdict=" + result.verdict +
                "; earliest divergence=" + result.earliestDivergenceTick +
                "; earliest out-of-contract=" + result.earliestOutOfContractTick;
        }

        private static void CheckExact<T>(
            ComparisonAccumulator result,
            ulong tick,
            T left,
            T right,
            string field)
        {
            if (!Equals(left, right))
                result.Mismatch(tick, field);
        }

        private static void CheckDouble(
            ComparisonAccumulator result,
            ulong tick,
            int family,
            double left,
            double right,
            double tolerance)
        {
            bool leftNaN = double.IsNaN(left);
            bool rightNaN = double.IsNaN(right);
            if (leftNaN || rightNaN)
            {
                if (!(leftNaN && rightNaN))
                    result.Mismatch(tick, FamilyNames[family]);
                return;
            }

            if (double.IsInfinity(left) || double.IsInfinity(right))
            {
                result.Mismatch(tick, FamilyNames[family]);
                return;
            }
            result.Difference(tick, family, Math.Abs(left - right), tolerance);
        }

        private static void CheckFloat(
            ComparisonAccumulator result,
            ulong tick,
            int family,
            float left,
            float right,
            float tolerance)
        {
            bool leftNaN = float.IsNaN(left);
            bool rightNaN = float.IsNaN(right);
            if (leftNaN || rightNaN)
            {
                if (!(leftNaN && rightNaN))
                    result.Mismatch(tick, FamilyNames[family]);
                return;
            }

            if (float.IsInfinity(left) || float.IsInfinity(right))
            {
                result.Mismatch(tick, FamilyNames[family]);
                return;
            }
            result.Difference(tick, family, Math.Abs((double)left - right), tolerance);
        }

        private static void CheckVector(
            ComparisonAccumulator result,
            ulong tick,
            int family,
            Vector3Value left,
            Vector3Value right,
            float tolerance)
        {
            CheckFloat(result, tick, family, left.X, right.X, tolerance);
            CheckFloat(result, tick, family, left.Y, right.Y, tolerance);
            CheckFloat(result, tick, family, left.Z, right.Z, tolerance);
        }

        private static void CheckQuaternion(
            ComparisonAccumulator result,
            ulong tick,
            int family,
            QuaternionValue left,
            QuaternionValue right,
            float tolerance)
        {
            bool leftUnavailable = float.IsNaN(left.X) && float.IsNaN(left.Y) && float.IsNaN(left.Z) && float.IsNaN(left.W);
            bool rightUnavailable = float.IsNaN(right.X) && float.IsNaN(right.Y) && float.IsNaN(right.Z) && float.IsNaN(right.W);
            if (leftUnavailable || rightUnavailable)
            {
                if (!(leftUnavailable && rightUnavailable))
                    result.Mismatch(tick, FamilyNames[family]);
                return;
            }

            try
            {
                result.Difference(tick, family, left.ShortestArcRadiansTo(right), tolerance);
            }
            catch (Exception)
            {
                result.Mismatch(tick, FamilyNames[family]);
            }
        }

        private static void WriteArtifact<T>(string relativePath, T artifact)
        {
            string path = Path.GetFullPath(relativePath);
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(artifact, true));
        }

        private const int TimeFamily = 0;
        private const int PhaseFamily = 1;
        private const int IntentFamily = 2;
        private const int PositionFamily = 3;
        private const int VelocityFamily = 4;
        private const int OrientationFamily = 5;
        private const int AngularVelocityFamily = 6;
        private const int MassFamily = 7;
        private const int DepthFamily = 8;
        private const int ForceFamily = 9;
        private const int ImpulseFamily = 10;
        private const int JointAngleFamily = 11;
        private const int JointVelocityFamily = 12;
        private const int JointScalarFamily = 13;
        private const int DriveFamily = 14;
        private static readonly string[] FamilyNames =
        {
            "time_s", "phase", "intent_scalar", "position_m", "velocity_mps", "orientation_rad",
            "angular_velocity_rad_s", "mass_kg", "depth_landmarks_m", "force_nm", "impulse_ns",
            "joint_angle_rad", "joint_velocity_rad_s", "joint_scalar", "drive_scalar"
        };
        private static readonly string[] FamilyUnits =
        {
            "s", "1", "1", "m", "m/s", "rad", "rad/s", "kg", "m", "N*m", "N*s", "rad",
            "rad/s", "1", "1"
        };

        private sealed class ComparisonAccumulator
        {
            private readonly double[] _maximum = new double[FamilyNames.Length];

            public bool WithinContract { get; private set; } = true;
            public bool TickAlignment { get; private set; } = true;
            public bool AnyDifference { get; private set; }
            public bool HasEarliestDivergence { get; private set; }
            public ulong EarliestDivergenceTick { get; private set; }
            public bool HasEarliestOutOfContract { get; private set; }
            public ulong EarliestOutOfContractTick { get; private set; }
            public string FirstMismatch { get; private set; } = string.Empty;

            public void Difference(ulong tick, int family, double difference, double tolerance)
            {
                if (difference > _maximum[family])
                    _maximum[family] = difference;
                if (difference > 0d)
                {
                    AnyDifference = true;
                    if (!HasEarliestDivergence)
                    {
                        HasEarliestDivergence = true;
                        EarliestDivergenceTick = tick;
                    }
                }
                if (difference > tolerance)
                {
                    WithinContract = false;
                    if (!HasEarliestOutOfContract)
                    {
                        HasEarliestOutOfContract = true;
                        EarliestOutOfContractTick = tick;
                    }
                }
            }

            public void Mismatch(ulong tick, string field)
            {
                WithinContract = false;
                if (field == "simulation_tick")
                    TickAlignment = false;
                AnyDifference = true;
                if (!HasEarliestDivergence)
                {
                    HasEarliestDivergence = true;
                    EarliestDivergenceTick = tick;
                }
                if (!HasEarliestOutOfContract)
                {
                    HasEarliestOutOfContract = true;
                    EarliestOutOfContractTick = tick;
                }
                if (FirstMismatch.Length == 0)
                    FirstMismatch = field;
            }

            public void Merge(ComparisonAccumulator other)
            {
                WithinContract &= other.WithinContract;
                TickAlignment &= other.TickAlignment;
                AnyDifference |= other.AnyDifference;
                if (!HasEarliestDivergence || (other.HasEarliestDivergence && other.EarliestDivergenceTick < EarliestDivergenceTick))
                {
                    HasEarliestDivergence = other.HasEarliestDivergence;
                    if (other.HasEarliestDivergence)
                        EarliestDivergenceTick = other.EarliestDivergenceTick;
                }
                if (!HasEarliestOutOfContract || (other.HasEarliestOutOfContract && other.EarliestOutOfContractTick < EarliestOutOfContractTick))
                {
                    HasEarliestOutOfContract = other.HasEarliestOutOfContract;
                    if (other.HasEarliestOutOfContract)
                        EarliestOutOfContractTick = other.EarliestOutOfContractTick;
                }
                if (FirstMismatch.Length == 0)
                    FirstMismatch = other.FirstMismatch;
                for (int index = 0; index < _maximum.Length; index++)
                    _maximum[index] = Math.Max(_maximum[index], other._maximum[index]);
            }

            public float MaximumFor(int family) => (float)_maximum[family];

            public ChannelDifference[] ToArtifacts()
            {
                ChannelDifference[] differences = new ChannelDifference[FamilyNames.Length];
                for (int index = 0; index < differences.Length; index++)
                {
                    differences[index] = new ChannelDifference
                    {
                        family = FamilyNames[index],
                        unit = FamilyUnits[index],
                        maxAbsDifference = (float)_maximum[index]
                    };
                }
                return differences;
            }

            public string Verdict => !WithinContract
                ? "REPEATABILITY_FAIL"
                : !AnyDifference
                    ? "EXACT_REPEATABLE"
                    : "REPEATABLE_WITHIN_DECLARED_TOLERANCE";
        }

        private sealed class BodyComparison
        {
            public bool WithinContract = true;
            public float MaxPositionDeltaM;
            public float MaxRotationDeltaDeg;
            public float MaxLinearVelocityDelta;
            public float MaxAngularVelocityDelta;

            public void Merge(BodyComparison other)
            {
                WithinContract &= other.WithinContract;
                MaxPositionDeltaM = Mathf.Max(MaxPositionDeltaM, other.MaxPositionDeltaM);
                MaxRotationDeltaDeg = Mathf.Max(MaxRotationDeltaDeg, other.MaxRotationDeltaDeg);
                MaxLinearVelocityDelta = Mathf.Max(MaxLinearVelocityDelta, other.MaxLinearVelocityDelta);
                MaxAngularVelocityDelta = Mathf.Max(MaxAngularVelocityDelta, other.MaxAngularVelocityDelta);
            }
        }

        [Serializable]
        private sealed class LoadRepeatabilityResult
        {
            public float loadKg;
            public int repeatCount;
            public int sampleCount;
            public bool tickAlignment = true;
            public string earliestDivergenceTick;
            public string earliestOutOfContractTick;
            public string firstMismatch;
            public bool withinDeclaredContract;
            public float finalStateMaxPositionM;
            public float finalStateMaxVelocityMps;
            public float finalStateMaxAngularVelocityRadS;
            public float finalStateMaxOrientationDeg;
            public ChannelDifference[] maximumAbsoluteDifferenceByChannelFamily;
            public ChannelDifference[] finalStateDifferenceByChannelFamily;
            public string verdict;
            public string[] pairVerdicts;
        }

        [Serializable]
        private sealed class ChannelDifference
        {
            public string family;
            public string unit;
            public float maxAbsDifference;
        }

        [Serializable]
        private sealed class RepeatabilityArtifact
        {
            public string schema;
            public string unityVersion;
            public string scene;
            public double fixedStepSeconds;
            public int repeatCount;
            public int sampleCountPerAttempt;
            public string inputSequence;
            public string resetBoundary;
            public string exactComparisonClass;
            public string floatWithToleranceComparisonClass;
            public string toleranceBasis;
            public LoadRepeatabilityResult[] loads;
        }

        [Serializable]
        private sealed class PerformanceArtifact
        {
            public string schema;
            public string unityVersion;
            public string scene;
            public string environment;
            public string traceRecordingComparison;
            public int traceAppendTrajectorySampleCount;
            public int sampleCount;
            public int warmupTickCount;
            public string gcAllocStatus;
            public string gcBytesPerPhysicsTick;
            public string gcMeasurementReason;
            public LoadPerformanceResult[] loads;
        }

        [Serializable]
        private sealed class LoadPerformanceResult
        {
            public float loadKg;
            public bool traceAppendPerturbationWithinContract;
            public float traceAppendMaxPositionDeltaM;
            public float traceAppendMaxRotationDeltaDeg;
            public float traceAppendMaxLinearVelocityDeltaMps;
            public float traceAppendMaxAngularVelocityDeltaRadS;
            public TimingArtifact timingRecordingOff;
            public TimingArtifact timingRecordingOn;
            public double timingP95DeltaOnMinusOffMs;
            public string timingInterpretation;
        }

        [Serializable]
        private sealed class TimingArtifact
        {
            public int sampleCount;
            public double medianMs;
            public double p95Ms;
            public double p99Ms;
            public double maxMs;
        }

        private sealed class TimingSummary
        {
            private readonly double[] _samples;

            private TimingSummary(double[] samples)
            {
                _samples = samples;
            }

            public double MedianMs => Percentile(0.50d);
            public double P95Ms => Percentile(0.95d);
            public double P99Ms => Percentile(0.99d);
            public double MaxMs => _samples[_samples.Length - 1];

            public static TimingSummary From(double[] samples)
            {
                Array.Sort(samples);
                return new TimingSummary(samples);
            }

            public TimingArtifact ToArtifact() => new TimingArtifact
            {
                sampleCount = _samples.Length,
                medianMs = MedianMs,
                p95Ms = P95Ms,
                p99Ms = P99Ms,
                maxMs = MaxMs
            };

            private double Percentile(double percentile)
            {
                int index = (int)Math.Ceiling(_samples.Length * percentile) - 1;
                index = Math.Max(0, Math.Min(index, _samples.Length - 1));
                return _samples[index];
            }
        }
    }
}
