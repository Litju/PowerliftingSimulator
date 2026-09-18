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
    /// Finds bounded static standing trims on a fixed Unity plant. The solver
    /// disables only the dynamic balance residual while evaluating local
    /// physical equilibrium; joints, gravity, contacts, saddle, and capacity
    /// remain production systems.
    /// </summary>
    public sealed class GAM13StaticTrimEquilibriumTests
    {
        private static readonly float[] ContinuationLoadsKg =
        {
            25f, 35f, 45f, 55f, 60f, 70f, 85f, 105f,
            125f, 140f, 155f, 170f, 200f, 230f, 260f, 300f
        };

        private static readonly float[] CanonicalLoadsKg = { 25f, 60f, 140f, 170f, 300f };
        private static readonly float[] StandingBiasLoadsKg = { 0f, 25f, 60f, 140f, 170f, 300f };
        private const int LocalWarmupTicks = 40;
        private const int LocalMeasurementTicks = 60;
        private const int LongHoldWarmupTicks = 100;
        private const int LongHoldMeasurementTicks = 500;
        private const float FixedDeltaTime = 0.01f;
        private const float MaximumTrunkPitchRad = 0.70f;
        private const float MinimumPelvisHeightM = 0.90f;
        private const float MinimumCaptureMarginM = 0.01f;
        private const float MaximumAllowedLimitProximity = 0.95f;

        private FoundationBootstrap _bootstrap;
        private PhysicalAthleteRig _rig;
        private SquatPhysicalPrototypeController _controller;
        private JointFamilyProfile[] _originalProfiles;
        private readonly Dictionary<string, float[]> _originalStandingBiases =
            new Dictionary<string, float[]>();
        private float _originalStanding0Abdomen;
        private float _originalStanding0Thorax;
        private float _originalStanding25Abdomen;
        private float _originalStanding25Thorax;

        [UnityTest]
        [Explicit("GAM-13 static trim refoundation; deterministic offline solver and physical evidence.")]
        public IEnumerator GAM13_STATIC_TRIM_CONTINUATION_AND_HOLD()
        {
            _originalProfiles = SnapshotProfiles();
            SnapshotStandingBiases();
            var continuationRows = new List<TrimRow>();
            var canonicalRows = new List<TrimRow>();
            var holdRows = new List<HoldRow>();
            var classificationRows = new List<ClassificationRow>();
            double[] previousCandidate = { 0.0, 0.0, 0.0, 3.93, 3.67 };

            try
            {
                ApplyImpedanceExperiment();
                yield return LoadFreshScene();

                for (int index = 0; index < ContinuationLoadsKg.Length; index++)
                {
                    float loadKg = ContinuationLoadsKg[index];
                    SquatStaticTrimSolver.Result result = SolveAtLoad(loadKg, previousCandidate);
                    TrimEvaluation evaluation = Evaluate(loadKg, result.Candidate, balanceEnabled: false, longHold: false);
                    TrimRow row = TrimRow.From(loadKg, result, evaluation, PlantVersion());
                    continuationRows.Add(row);
                    classificationRows.Add(ClassificationRow.From(loadKg, result.Candidate, evaluation, PlantVersion()));
                    previousCandidate = result.Candidate;
                    Debug.Log("GAM13_TRIM " + row.Summary);
                    yield return null;
                }

                for (int index = 0; index < CanonicalLoadsKg.Length; index++)
                {
                    float loadKg = CanonicalLoadsKg[index];
                    TrimRow continuation = FindNearest(continuationRows, loadKg);
                    SquatStaticTrimSolver.Result result = SolveAtLoad(loadKg, continuation.Candidate);
                    TrimEvaluation local = Evaluate(loadKg, result.Candidate, balanceEnabled: false, longHold: false);
                    TrimEvaluation hold = Evaluate(loadKg, result.Candidate, balanceEnabled: true, longHold: true);
                    TrimRow row = TrimRow.From(loadKg, result, local, PlantVersion());
                    canonicalRows.Add(row);
                    holdRows.Add(HoldRow.From(loadKg, result.Candidate, hold, PlantVersion()));
                    classificationRows.Add(ClassificationRow.From(loadKg, result.Candidate, hold, PlantVersion()));
                    Debug.Log("GAM13_TRIM_HOLD " + HoldRow.From(loadKg, result.Candidate, hold, PlantVersion()).Summary);
                    yield return null;
                }
            }
            finally
            {
                RestoreStandingBiases();
                RestoreProfiles(_originalProfiles);
            }

            WriteArtifacts(continuationRows, canonicalRows, holdRows, classificationRows);
            Assert.That(continuationRows.Count, Is.EqualTo(ContinuationLoadsKg.Length));
            Assert.That(canonicalRows.Count, Is.EqualTo(CanonicalLoadsKg.Length));
            Assert.That(holdRows.Count, Is.EqualTo(CanonicalLoadsKg.Length));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            if (bootstrap != null && bootstrap.Runtime != null && bootstrap.Runtime.IsInitialized)
            {
                AsyncOperation unload = bootstrap.Runtime.Shutdown();
                while (unload != null && !unload.isDone)
                    yield return null;
            }
            if (bootstrap != null)
                UnityEngine.Object.DestroyImmediate(bootstrap.gameObject);
            yield return null;
        }

        private IEnumerator LoadFreshScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("SquatPhysicalPrototype", LoadSceneMode.Single);
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
            _controller.enabled = false;
            _bootstrap.enabled = false;
        }

        private SquatStaticTrimSolver.Result SolveAtLoad(float loadKg, double[] initialCandidate)
        {
            var solver = new SquatStaticTrimSolver();
            var options = new SquatStaticTrimSolver.Options
            {
                MaximumIterations = 8,
                FiniteDifferenceStep = 0.5,
                InitialDamping = 0.1,
                DampingIncrease = 10.0,
                MaximumDampingRetries = 4,
                MaximumStep = 3.0,
                ResidualTolerance = 0.25,
                ImprovementTolerance = 1e-4,
                LowerBound = -12.0,
                UpperBound = 12.0
            };
            return solver.Solve(
                candidate => Evaluate(loadKg, candidate, balanceEnabled: false, longHold: false).NormalizedResidual,
                initialCandidate,
                options);
        }

        private TrimEvaluation Evaluate(float loadKg, double[] candidate, bool balanceEnabled, bool longHold)
        {
            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            ApplyCandidate(adapter.Preload, candidate);
            adapter.BalanceCorrectionsEnabled = balanceEnabled;
            adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

            int warmupTicks = longHold ? LongHoldWarmupTicks : LocalWarmupTicks;
            int measurementTicks = longHold ? LongHoldMeasurementTicks : LocalMeasurementTicks;
            var accumulator = new TrimAccumulator(warmupTicks, measurementTicks, candidate, _rig, _controller);
            Vector3 previousComVelocity = Vector3.zero;
            Vector3 previousTrunkVelocity = Vector3.zero;
            bool havePrevious = false;

            for (int tick = 0; tick < warmupTicks + measurementTicks; tick++)
            {
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);
                Assert.That(_bootstrap.Runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds), Is.EqualTo(1));
                TickFeet();

                SquatBalanceObserver balance = adapter.Balance;
                Vector3 comVelocity = balance.SystemComVelocity;
                Vector3 trunkVelocity = TrunkVelocity();
                Vector3 comAcceleration = havePrevious
                    ? (comVelocity - previousComVelocity) / FixedDeltaTime
                    : Vector3.zero;
                Vector3 trunkAcceleration = havePrevious
                    ? (trunkVelocity - previousTrunkVelocity) / FixedDeltaTime
                    : Vector3.zero;
                previousComVelocity = comVelocity;
                previousTrunkVelocity = trunkVelocity;
                havePrevious = true;

                if (tick >= warmupTicks)
                    accumulator.Add(adapter, comVelocity, comAcceleration, trunkVelocity, trunkAcceleration);
            }

            return accumulator.Finish(adapter, _rig, _controller);
        }

        private Vector3 TrunkVelocity()
        {
            PoweredJointController.PoweredJointRuntime abdomen = _rig.PoweredController.GetJoint("abdomen");
            PoweredJointController.PoweredJointRuntime thorax = _rig.PoweredController.GetJoint("thorax");
            Vector3 abdomenVelocity = abdomen != null && abdomen.HasPostPhysicsDiagnostic
                ? abdomen.PostPhysicsDiagnostic.ActualAngularVelocityRadS
                : Vector3.zero;
            Vector3 thoraxVelocity = thorax != null && thorax.HasPostPhysicsDiagnostic
                ? thorax.PostPhysicsDiagnostic.ActualAngularVelocityRadS
                : Vector3.zero;
            return 0.5f * (abdomenVelocity + thoraxVelocity);
        }

        private void TickFeet()
        {
            _controller.LeftFootContact?.PhysicsTickUpdate(FixedDeltaTime);
            _controller.RightFootContact?.PhysicsTickUpdate(FixedDeltaTime);
        }

        private void ApplyCandidate(SquatEquilibriumPreload preload, double[] candidate)
        {
            for (int index = 0; index < 3; index++)
            {
                if (double.IsNaN(candidate[index]) || double.IsInfinity(candidate[index]) || Math.Abs(candidate[index]) > 12.0001)
                    throw new ArgumentOutOfRangeException(nameof(candidate));
            }

            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Ankle, (float)candidate[0]);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Knee, (float)candidate[1]);
            preload.SetAnatomicalFlexionBiasDegrees(SquatJointFamily.Hip, (float)candidate[2]);
            preload.StandingAbdomenBiasDegrees0Kg = (float)candidate[3];
            preload.StandingThoraxBiasDegrees0Kg = (float)candidate[4];
            preload.StandingAbdomenBiasDegrees25Kg = (float)candidate[3];
            preload.StandingThoraxBiasDegrees25Kg = (float)candidate[4];

            foreach (float loadKg in StandingBiasLoadsKg)
            {
                SetStandingBiasTable(loadKg, abdomen: true, (float)candidate[3]);
                SetStandingBiasTable(loadKg, abdomen: false, (float)candidate[4]);
            }
        }

        private static void SetStandingBiasTable(float loadKg, bool abdomen, float value)
        {
            string prefix = abdomen ? "AbdomenBias" : "ThoraxBias";
            string fieldName = prefix + Mathf.RoundToInt(loadKg).ToString(CultureInfo.InvariantCulture) + "Kg";
            FieldInfo field = typeof(SquatEquilibriumPreload).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
                throw new InvalidOperationException("Missing standing-bias field " + fieldName);
            float[] values = (float[])field.GetValue(null);
            values[0] = value;
        }

        private void SnapshotStandingBiases()
        {
            _originalStandingBiases.Clear();
            foreach (float loadKg in StandingBiasLoadsKg)
            {
                _originalStandingBiases["Abdomen" + Mathf.RoundToInt(loadKg)] =
                    SnapshotStandingBiasTable(loadKg, abdomen: true);
                _originalStandingBiases["Thorax" + Mathf.RoundToInt(loadKg)] =
                    SnapshotStandingBiasTable(loadKg, abdomen: false);
            }

            SquatEquilibriumPreload preload = SquatEquilibriumPreload.QualifiedStanding();
            _originalStanding0Abdomen = preload.StandingAbdomenBiasDegrees0Kg;
            _originalStanding0Thorax = preload.StandingThoraxBiasDegrees0Kg;
            _originalStanding25Abdomen = preload.StandingAbdomenBiasDegrees25Kg;
            _originalStanding25Thorax = preload.StandingThoraxBiasDegrees25Kg;
        }

        private static float[] SnapshotStandingBiasTable(float loadKg, bool abdomen)
        {
            string prefix = abdomen ? "AbdomenBias" : "ThoraxBias";
            string fieldName = prefix + Mathf.RoundToInt(loadKg).ToString(CultureInfo.InvariantCulture) + "Kg";
            FieldInfo field = typeof(SquatEquilibriumPreload).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            return (float[])((float[])field.GetValue(null)).Clone();
        }

        private void RestoreStandingBiases()
        {
            foreach (float loadKg in StandingBiasLoadsKg)
            {
                RestoreStandingBiasTable(loadKg, true, _originalStandingBiases["Abdomen" + Mathf.RoundToInt(loadKg)]);
                RestoreStandingBiasTable(loadKg, false, _originalStandingBiases["Thorax" + Mathf.RoundToInt(loadKg)]);
            }

            // The static fields are restored above. Restore the live adapter's
            // per-instance overrides before the scene teardown when present.
            if (_controller != null && _controller.Adapter != null)
            {
                SquatEquilibriumPreload preload = _controller.Adapter.Preload;
                preload.StandingAbdomenBiasDegrees0Kg = _originalStanding0Abdomen;
                preload.StandingThoraxBiasDegrees0Kg = _originalStanding0Thorax;
                preload.StandingAbdomenBiasDegrees25Kg = _originalStanding25Abdomen;
                preload.StandingThoraxBiasDegrees25Kg = _originalStanding25Thorax;
            }
        }

        private static void RestoreStandingBiasTable(float loadKg, bool abdomen, float[] original)
        {
            string prefix = abdomen ? "AbdomenBias" : "ThoraxBias";
            string fieldName = prefix + Mathf.RoundToInt(loadKg).ToString(CultureInfo.InvariantCulture) + "Kg";
            FieldInfo field = typeof(SquatEquilibriumPreload).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Static);
            Array.Copy(original, (float[])field.GetValue(null), original.Length);
        }

        private static JointFamilyProfile[] ProfileArray() =>
            (JointFamilyProfile[])typeof(PoweredJointController)
                .GetField("Profiles", BindingFlags.NonPublic | BindingFlags.Static)
                .GetValue(null);

        private static JointFamilyProfile[] SnapshotProfiles() => (JointFamilyProfile[])ProfileArray().Clone();

        private static void ApplyImpedanceExperiment()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_TRIM_IMPEDANCE") ?? "1";
            float factor = float.Parse(text, CultureInfo.InvariantCulture);
            Assert.That(float.IsFinite(factor) && factor > 0f && factor <= 8f, Is.True);
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

        private static void RestoreProfiles(JointFamilyProfile[] original)
        {
            if (original == null)
                return;
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
                live[index] = original[index];
        }

        private static string PlantVersion()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_TRIM_IMPEDANCE") ?? "1";
            return Mathf.Abs(float.Parse(text, CultureInfo.InvariantCulture) - 1f) <= 1e-6f
                ? "GAM13_PRODUCTION_PLANT_V1"
                : "GAM13_STATIC_TRIM_DIAGNOSTIC_IMPEDANCE_X" + text;
        }

        private static TrimRow FindNearest(List<TrimRow> rows, float loadKg)
        {
            TrimRow best = rows[0];
            float distance = Mathf.Abs(best.LoadKg - loadKg);
            for (int index = 1; index < rows.Count; index++)
            {
                float candidateDistance = Mathf.Abs(rows[index].LoadKg - loadKg);
                if (candidateDistance < distance)
                {
                    best = rows[index];
                    distance = candidateDistance;
                }
            }
            return best;
        }

        private static void WriteArtifacts(
            IReadOnlyList<TrimRow> continuation,
            IReadOnlyList<TrimRow> canonical,
            IReadOnlyList<HoldRow> holds,
            IReadOnlyList<ClassificationRow> classifications)
        {
            string directory = Path.Combine(Directory.GetCurrentDirectory(), "Artifacts/Measurements/GAM-13");
            Directory.CreateDirectory(directory);
            string suffix = ArtifactSuffix();
            WriteRows(Path.Combine(directory, "trim-continuation-search" + suffix + ".csv"), TrimRow.Header, continuation);
            WriteRows(Path.Combine(directory, "trim-canonical-solutions" + suffix + ".csv"), TrimRow.Header, canonical);
            WriteRows(Path.Combine(directory, "trim-hold-validation" + suffix + ".csv"), HoldRow.Header, holds);
            WriteRows(Path.Combine(directory, "trim-constraint-classification" + suffix + ".csv"), ClassificationRow.Header, classifications);
        }

        private static string ArtifactSuffix()
        {
            string text = Environment.GetEnvironmentVariable("GAM13_TRIM_IMPEDANCE") ?? "1";
            return Mathf.Abs(float.Parse(text, CultureInfo.InvariantCulture) - 1f) <= 1e-6f
                ? string.Empty
                : "-impedance-" + text.Replace('.', 'p') + "x";
        }

        private static void WriteRows<T>(string path, string header, IReadOnlyList<T> rows)
        {
            var builder = new StringBuilder();
            builder.AppendLine(header);
            for (int index = 0; index < rows.Count; index++)
                builder.AppendLine(rows[index].ToString());
            File.WriteAllText(path, builder.ToString());
        }

        private sealed class TrimAccumulator
        {
            private readonly int _measurementTicks;
            private readonly double[] _candidate;
            private int _samples;
            private int _supportLossSamples;
            private int _captureViolationSamples;
            private int _nonFiniteSamples;
            private float _comAccelerationAp;
            private float _comAccelerationMl;
            private float _comVelocityAp;
            private float _comVelocityMl;
            private float _trunkVelocity;
            private float _trunkAcceleration;
            private float _pelvisVelocity;
            private float _jointVelocity;
            private float _postureError;
            private float _minimumCaptureMargin = float.PositiveInfinity;
            private float _maximumTrunkPitch;
            private float _minimumPelvisHeight = float.PositiveInfinity;
            private float _maximumDriveSaturation;
            private float _maximumLimitProximity;
            private float _maximumSaddleSeparation;

            public TrimAccumulator(
                int warmupTicks,
                int measurementTicks,
                double[] candidate,
                PhysicalAthleteRig rig,
                SquatPhysicalPrototypeController owner)
            {
                _measurementTicks = measurementTicks;
                _candidate = (double[])candidate.Clone();
                _rig = rig;
                _controller = owner;
            }

            public void Add(
                SquatPhysicalAdapter adapter,
                Vector3 comVelocity,
                Vector3 comAcceleration,
                Vector3 trunkVelocity,
                Vector3 trunkAcceleration)
            {
                _samples++;
                SquatBalanceObserver balance = adapter.Balance;
                float captureMargin = balance.HasSupport
                    ? Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear)
                    : -1f;
                if (!balance.HasSupport)
                    _supportLossSamples++;
                if (captureMargin < MinimumCaptureMarginM)
                    _captureViolationSamples++;

                float pelvisVelocity = 0f;
                float pelvisHeight = float.NaN;
                if (_rig.Segments.TryGetValue("pelvis", out PhysicalAthleteRig.SegmentRuntime pelvis) && pelvis.Body != null)
                {
                    pelvisVelocity = pelvis.Body.linearVelocity.magnitude;
                    pelvisHeight = pelvis.Body.position.y;
                }

                float jointVelocity = 0f;
                string[] jointIds = { "left_foot", "left_shank", "left_thigh", "abdomen", "thorax" };
                for (int index = 0; index < jointIds.Length; index++)
                {
                    PoweredJointController.PoweredJointRuntime joint = _rig.PoweredController.GetJoint(jointIds[index]);
                    if (joint != null && joint.HasPostPhysicsDiagnostic)
                        jointVelocity += joint.PostPhysicsDiagnostic.ActualAngularVelocityRadS.magnitude;
                }

                float trunkPitch = 0f;
                if (_controller.ObservationCollector != null && _controller.ObservationCollector.HasLastSnapshot)
                    trunkPitch = Mathf.Abs(_controller.ObservationCollector.LastSnapshot.TrunkWorldPitchRadians);

                AddFinite(ref _comAccelerationAp, Mathf.Abs(comAcceleration.z));
                AddFinite(ref _comAccelerationMl, Mathf.Abs(comAcceleration.x));
                AddFinite(ref _comVelocityAp, Mathf.Abs(comVelocity.z));
                AddFinite(ref _comVelocityMl, Mathf.Abs(comVelocity.x));
                AddFinite(ref _trunkVelocity, trunkVelocity.magnitude);
                AddFinite(ref _trunkAcceleration, trunkAcceleration.magnitude);
                AddFinite(ref _pelvisVelocity, pelvisVelocity);
                AddFinite(ref _jointVelocity, jointVelocity / jointIds.Length);
                AddFinite(ref _postureError, adapter.CanonicalPostureErrorRad);
                if (float.IsFinite(captureMargin))
                    _minimumCaptureMargin = Mathf.Min(_minimumCaptureMargin, captureMargin);
                else
                    _nonFiniteSamples++;
                if (float.IsFinite(trunkPitch))
                    _maximumTrunkPitch = Mathf.Max(_maximumTrunkPitch, trunkPitch);
                else
                    _nonFiniteSamples++;
                if (float.IsFinite(pelvisHeight))
                    _minimumPelvisHeight = Mathf.Min(_minimumPelvisHeight, pelvisHeight);
                else
                    _nonFiniteSamples++;
                _maximumDriveSaturation = Mathf.Max(_maximumDriveSaturation, adapter.MaxDriveSaturation);
                _maximumLimitProximity = Mathf.Max(_maximumLimitProximity, adapter.CanonicalPostureLimitProximity);
                if (adapter.Saddle != null)
                {
                    _maximumSaddleSeparation = Mathf.Max(_maximumSaddleSeparation, adapter.Saddle.SaddleSeparationMeters);
                    if (!adapter.Saddle.IsAttached || adapter.Saddle.IsBroken)
                        _supportLossSamples++;
                }
            }

            private readonly PhysicalAthleteRig _rig;
            private readonly SquatPhysicalPrototypeController _controller;

            public TrimEvaluation Finish(
                SquatPhysicalAdapter adapter,
                PhysicalAthleteRig rig,
                SquatPhysicalPrototypeController owner)
            {
                return new TrimEvaluation(_candidate, _samples, _supportLossSamples, _captureViolationSamples,
                    _nonFiniteSamples, _comAccelerationAp, _comAccelerationMl, _comVelocityAp, _comVelocityMl,
                    _trunkVelocity, _trunkAcceleration, _pelvisVelocity, _jointVelocity, _postureError,
                    _minimumCaptureMargin, _maximumTrunkPitch, _minimumPelvisHeight, _maximumDriveSaturation,
                    _maximumLimitProximity, _maximumSaddleSeparation, _measurementTicks);
            }

            private static void AddFinite(ref float accumulator, float value)
            {
                if (float.IsFinite(value))
                    accumulator += value;
            }
        }

        private sealed class TrimEvaluation
        {
            public TrimEvaluation(
                double[] candidate,
                int samples,
                int supportLossSamples,
                int captureViolationSamples,
                int nonFiniteSamples,
                float comAccelerationAp,
                float comAccelerationMl,
                float comVelocityAp,
                float comVelocityMl,
                float trunkVelocity,
                float trunkAcceleration,
                float pelvisVelocity,
                float jointVelocity,
                float postureError,
                float minimumCaptureMargin,
                float maximumTrunkPitch,
                float minimumPelvisHeight,
                float maximumDriveSaturation,
                float maximumLimitProximity,
                float maximumSaddleSeparation,
                int measurementTicks)
            {
                Candidate = (double[])candidate.Clone();
                Samples = samples;
                SupportLossSamples = supportLossSamples;
                CaptureViolationSamples = captureViolationSamples;
                NonFiniteSamples = nonFiniteSamples;
                ComAccelerationAp = Average(comAccelerationAp, samples);
                ComAccelerationMl = Average(comAccelerationMl, samples);
                ComVelocityAp = Average(comVelocityAp, samples);
                ComVelocityMl = Average(comVelocityMl, samples);
                TrunkVelocity = Average(trunkVelocity, samples);
                TrunkAcceleration = Average(trunkAcceleration, samples);
                PelvisVelocity = Average(pelvisVelocity, samples);
                JointVelocity = Average(jointVelocity, samples);
                PostureError = Average(postureError, samples);
                MinimumCaptureMargin = minimumCaptureMargin;
                MaximumTrunkPitch = maximumTrunkPitch;
                MinimumPelvisHeight = minimumPelvisHeight;
                MaximumDriveSaturation = maximumDriveSaturation;
                MaximumLimitProximity = maximumLimitProximity;
                MaximumSaddleSeparation = maximumSaddleSeparation;
                MeasurementTicks = measurementTicks;
            }

            public double[] Candidate { get; }
            public int Samples { get; }
            public int SupportLossSamples { get; }
            public int CaptureViolationSamples { get; }
            public int NonFiniteSamples { get; }
            public float ComAccelerationAp { get; }
            public float ComAccelerationMl { get; }
            public float ComVelocityAp { get; }
            public float ComVelocityMl { get; }
            public float TrunkVelocity { get; }
            public float TrunkAcceleration { get; }
            public float PelvisVelocity { get; }
            public float JointVelocity { get; }
            public float PostureError { get; }
            public float MinimumCaptureMargin { get; }
            public float MaximumTrunkPitch { get; }
            public float MinimumPelvisHeight { get; }
            public float MaximumDriveSaturation { get; }
            public float MaximumLimitProximity { get; }
            public float MaximumSaddleSeparation { get; }
            public int MeasurementTicks { get; }

            public double[] NormalizedResidual => new double[]
            {
                ComAccelerationAp,
                ComAccelerationMl,
                ComVelocityAp / 0.10f,
                ComVelocityMl / 0.10f,
                TrunkVelocity,
                TrunkAcceleration / 10f,
                PelvisVelocity / 0.10f,
                JointVelocity,
                PostureError / 0.50f,
                SupportPenalty
            };

            public bool Finite => NonFiniteSamples == 0 &&
                float.IsFinite(MinimumCaptureMargin) &&
                float.IsFinite(MaximumTrunkPitch) &&
                float.IsFinite(MinimumPelvisHeight);

            public bool SupportRetained => SupportLossSamples == 0;
            public bool CaptureInterior => CaptureViolationSamples == 0;
            public bool Upright => MinimumPelvisHeight > MinimumPelvisHeightM && MaximumTrunkPitch < MaximumTrunkPitchRad;
            public bool NoJointLimit => MaximumLimitProximity < MaximumAllowedLimitProximity;
            public bool SaddleValid => MaximumSaddleSeparation < 0.05f;
            public float SupportPenalty =>
                10f * SupportLossSamples / Mathf.Max(1, MeasurementTicks) +
                2f * CaptureViolationSamples / Mathf.Max(1, MeasurementTicks);

            public bool IsFeasible(double[] candidate, double residualTolerance) =>
                Samples == MeasurementTicks && Finite && SupportRetained && CaptureInterior &&
                Upright && NoJointLimit && SaddleValid && MaximumDriveSaturation < 0.95f &&
                Rms(NormalizedResidual) <= residualTolerance && WithinBounds(candidate);

            public string PrimaryReason(double[] candidate)
            {
                if (!Finite)
                    return "NUMERICAL_INSTABILITY";
                if (!SaddleValid)
                    return "SADDLE_CONTACT_INFEASIBLE";
                if (!SupportRetained || !CaptureInterior)
                    return "SUPPORT_WRENCH_INFEASIBLE";
                if (!Upright)
                    return "JOINT_LIMIT";
                if (!NoJointLimit)
                    return "JOINT_LIMIT";
                if (MaximumDriveSaturation >= 0.95f)
                    return "ACTUATOR_CAPACITY_BOUND";
                if (TouchesBound(candidate))
                    return "EQUILIBRIUM_BIAS_BOUND";
                return "NO_CONVERGENCE_WITHOUT_PROVEN_CAUSE";
            }

            private static bool WithinBounds(double[] candidate)
            {
                for (int index = 0; index < candidate.Length; index++)
                    if (Math.Abs(candidate[index]) > 12.0001)
                        return false;
                return true;
            }

            private static bool TouchesBound(double[] candidate)
            {
                for (int index = 0; index < candidate.Length; index++)
                    if (Math.Abs(candidate[index]) >= 11.999)
                        return true;
                return false;
            }

            private static float Average(float value, int samples) => samples == 0 ? float.PositiveInfinity : value / samples;
            private static double Rms(double[] values)
            {
                double sum = 0.0;
                for (int index = 0; index < values.Length; index++)
                    sum += values[index] * values[index];
                return Math.Sqrt(sum / values.Length);
            }
        }

        private sealed class TrimRow
        {
            public float LoadKg;
            public double[] Candidate;
            public SquatStaticTrimSolver.Result Solver;
            public TrimEvaluation Evaluation;
            public string Plant;

            public static TrimRow From(float loadKg, SquatStaticTrimSolver.Result solver, TrimEvaluation evaluation, string plant) =>
                new TrimRow { LoadKg = loadKg, Candidate = (double[])solver.Candidate.Clone(), Solver = solver, Evaluation = evaluation, Plant = plant };

            public string Summary => string.Format(CultureInfo.InvariantCulture,
                "load={0:F0} norm={1:F3} converged={2} candidate=[{3:F2},{4:F2},{5:F2},{6:F2},{7:F2}] reason={8}",
                LoadKg, Solver.FinalNorm, Solver.Converged, Candidate[0], Candidate[1], Candidate[2], Candidate[3], Candidate[4],
                Evaluation.PrimaryReason(Candidate));

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0:R},{1},{2},{3},{4},{5},{6},{7},{8},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20:R},{21:R},{22},{23},{24},{25},{26},{27},{28}",
                LoadKg, Plant, Solver.Converged, Solver.Iterations, Solver.Evaluations, Solver.RejectedSteps, Solver.InitialNorm, Solver.FinalNorm,
                Evaluation.PrimaryReason(Candidate), Candidate[0], Candidate[1], Candidate[2], Candidate[3], Candidate[4],
                Evaluation.ComAccelerationAp, Evaluation.ComAccelerationMl, Evaluation.ComVelocityAp, Evaluation.ComVelocityMl,
                Evaluation.TrunkVelocity, Evaluation.TrunkAcceleration, Evaluation.PelvisVelocity, Evaluation.JointVelocity,
                Evaluation.PostureError, Evaluation.MinimumCaptureMargin, Evaluation.MaximumTrunkPitch, Evaluation.MinimumPelvisHeight,
                Evaluation.MaximumDriveSaturation, Evaluation.MaximumLimitProximity, Evaluation.MaximumSaddleSeparation);

            public const string Header = "load_kg,plant,converged,iterations,evaluations,rejected_steps,initial_residual_norm,final_residual_norm,classification,ankle_bias_deg,knee_bias_deg,hip_bias_deg,abdomen_bias_deg,thorax_bias_deg,com_accel_ap_mps2,com_accel_ml_mps2,com_vel_ap_mps,com_vel_ml_mps,trunk_velocity_rad_s,trunk_accel_rad_s2,pelvis_velocity_mps,joint_velocity_rad_s,posture_error_rad,min_capture_margin_m,max_trunk_pitch_rad,min_pelvis_height_m,max_drive_saturation,max_limit_proximity,max_saddle_separation_m";
        }

        private sealed class HoldRow
        {
            public float LoadKg;
            public double[] Candidate;
            public TrimEvaluation Evaluation;
            public string Plant;

            public static HoldRow From(float loadKg, double[] candidate, TrimEvaluation evaluation, string plant) =>
                new HoldRow { LoadKg = loadKg, Candidate = (double[])candidate.Clone(), Evaluation = evaluation, Plant = plant };

            public string Summary => string.Format(CultureInfo.InvariantCulture,
                "load={0:F0} feasible={1} reason={2} support={3} capture={4:F3} trunk={5:F3}",
                LoadKg, Evaluation.IsFeasible(Candidate, 0.25), Evaluation.PrimaryReason(Candidate), Evaluation.SupportRetained,
                Evaluation.MinimumCaptureMargin, Evaluation.MaximumTrunkPitch);

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0:R},{1},{2},{3},{4},{5},{6},{7},{8},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15:R},{16:R},{17:R},{18:R},{19:R},{20:R},{21:R},{22:R},{23:R},{24:R},{25:R},{26:R},{27},{28},{29},{30}",
                LoadKg, Plant, Evaluation.IsFeasible(Candidate, 0.25), Evaluation.PrimaryReason(Candidate), Evaluation.Samples,
                Evaluation.SupportLossSamples, Evaluation.CaptureViolationSamples, Evaluation.NonFiniteSamples,
                Candidate[0], Candidate[1], Candidate[2], Candidate[3], Candidate[4], Evaluation.ComAccelerationAp,
                Evaluation.ComAccelerationMl, Evaluation.ComVelocityAp, Evaluation.ComVelocityMl, Evaluation.TrunkVelocity,
                Evaluation.TrunkAcceleration, Evaluation.PelvisVelocity, Evaluation.JointVelocity, Evaluation.PostureError,
                Evaluation.MinimumCaptureMargin, Evaluation.MaximumTrunkPitch, Evaluation.MinimumPelvisHeight,
                Evaluation.MaximumDriveSaturation, Evaluation.MaximumLimitProximity, Evaluation.MaximumSaddleSeparation,
                Evaluation.SupportRetained, Evaluation.CaptureInterior, Evaluation.Upright, Evaluation.Finite);

            public const string Header = "load_kg,plant,trim_feasible,classification,samples,support_loss_samples,capture_violation_samples,nonfinite_samples,ankle_bias_deg,knee_bias_deg,hip_bias_deg,abdomen_bias_deg,thorax_bias_deg,com_accel_ap_mps2,com_accel_ml_mps2,com_vel_ap_mps,com_vel_ml_mps,trunk_velocity_rad_s,trunk_accel_rad_s2,pelvis_velocity_mps,joint_velocity_rad_s,posture_error_rad,min_capture_margin_m,max_trunk_pitch_rad,min_pelvis_height_m,max_drive_saturation,max_limit_proximity,max_saddle_separation_m,support_retained,capture_interior,upright,finite";
        }

        private sealed class ClassificationRow
        {
            public float LoadKg;
            public double[] Candidate;
            public TrimEvaluation Evaluation;
            public string Plant;

            public static ClassificationRow From(float loadKg, double[] candidate, TrimEvaluation evaluation, string plant) =>
                new ClassificationRow { LoadKg = loadKg, Candidate = (double[])candidate.Clone(), Evaluation = evaluation, Plant = plant };

            public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                "{0:R},{1},{2},{3},{4},{5},{6},{7},{8},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15},{16},{17},{18:R},{19:R},{20:R},{21:R},{22:R},{23:R},{24:R}",
                LoadKg, Plant, Evaluation.PrimaryReason(Candidate), Evaluation.IsFeasible(Candidate, 0.25),
                Candidate[0], Candidate[1], Candidate[2], Candidate[3], Candidate[4], Evaluation.MinimumCaptureMargin,
                Evaluation.MaximumTrunkPitch, Evaluation.MinimumPelvisHeight, Evaluation.MaximumDriveSaturation,
                Evaluation.MaximumLimitProximity, Evaluation.MaximumSaddleSeparation, Evaluation.SupportLossSamples,
                Evaluation.CaptureViolationSamples, Evaluation.NonFiniteSamples, Evaluation.ComAccelerationAp,
                Evaluation.ComAccelerationMl, Evaluation.ComVelocityAp, Evaluation.ComVelocityMl, Evaluation.TrunkVelocity,
                Evaluation.PelvisVelocity, Evaluation.PostureError);

            public const string Header = "load_kg,plant,primary_classification,trim_feasible,ankle_bias_deg,knee_bias_deg,hip_bias_deg,abdomen_bias_deg,thorax_bias_deg,min_capture_margin_m,max_trunk_pitch_rad,min_pelvis_height_m,max_drive_saturation,max_limit_proximity,max_saddle_separation_m,support_loss_samples,capture_violation_samples,nonfinite_samples,com_accel_ap_mps2,com_accel_ml_mps2,com_vel_ap_mps,com_vel_ml_mps,trunk_velocity_rad_s,pelvis_velocity_mps,posture_error_rad";
        }
    }
}
