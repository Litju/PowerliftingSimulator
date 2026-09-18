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
    /// GAM-13 production-plant dynamic identification. The plant is the
    /// unchanged 1x production impedance. The balance loop is disabled only
    /// for this test-time experiment; the target residuals are bounded,
    /// deterministic, and recorded after target-rate limiting.
    /// </summary>
    public sealed class GAM13DynamicPlantIdentificationTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";
        private const int WarmupTicks = 100;
        private const int ExcitationTicks = 800;
        private const int Repeats = 2;
        private const float MaximumInputDegrees = 0.30f;
        private static readonly float[] LoadsKg = { 25f, 60f, 140f, 170f, 300f };

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
        [Explicit("GAM-13 production dynamic plant identification; excluded from default qualification suites.")]
        public IEnumerator GAM13_DYNAMIC_PLANT_IDENTIFICATION()
        {
            var identification = new List<DynamicSample>(LoadsKg.Length * Repeats * ExcitationTicks);
            var validation = new List<DynamicSample>(LoadsKg.Length * Repeats * ExcitationTicks);

            for (int loadIndex = 0; loadIndex < LoadsKg.Length; loadIndex++)
            {
                for (int repeat = 1; repeat <= Repeats; repeat++)
                {
                    yield return RunTrajectory(LoadsKg[loadIndex], loadIndex, repeat, false, identification);
                    yield return RunTrajectory(LoadsKg[loadIndex], loadIndex, repeat, true, validation);
                }
            }

            WriteCsv("dynamic-id-excitation.csv", identification);
            WriteCsv("dynamic-id-validation.csv", validation);

            Assert.That(identification.Count, Is.GreaterThan(0));
            Assert.That(validation.Count, Is.GreaterThan(0));
            Assert.That(AllFinite(identification), Is.True, "Identification trajectory contained non-finite observations.");
            Assert.That(AllFinite(validation), Is.True, "Validation trajectory contained non-finite observations.");
            yield return null;
        }

        private IEnumerator RunTrajectory(
            float loadKg,
            int loadIndex,
            int repeat,
            bool validation,
            List<DynamicSample> destination)
        {
            yield return LoadFreshScene();

            _controller.SetLoad(loadKg);
            SquatPhysicalAdapter adapter = _controller.Adapter;
            adapter.BalanceCorrectionsEnabled = true;
            adapter.Preload.Enabled = true;
            adapter.Preload.SpineCalibrationEnabled = true;
            SetLogicalBias(adapter, SquatJointFamily.Ankle, 0f);
            SetLogicalBias(adapter, SquatJointFamily.Knee, 0f);
            SetLogicalBias(adapter, SquatJointFamily.Hip, 0f);
            SetLogicalBias(adapter, SquatJointFamily.Abdomen, 0f);
            SetLogicalBias(adapter, SquatJointFamily.Thorax, 0f);

            FoundationRuntime runtime = _bootstrap.Runtime;
            float baselineAnkle = float.NaN;
            float baselineHip = float.NaN;
            float baselineTrunk = float.NaN;
            int totalTicks = WarmupTicks + ExcitationTicks;

            for (int tick = 0; tick < totalTicks; tick++)
            {
                Vector3 inputDegrees = tick < WarmupTicks
                    ? Vector3.zero
                    : Excitation(tick - WarmupTicks, loadIndex, validation);
                ApplyInput(adapter, inputDegrees);
                adapter.HoldReferencePhaseForQualification(0f, SquatPhaseDirection.None, SquatState.SETUP);

                Assert.That(
                    runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds),
                    Is.EqualTo(1),
                    $"Dynamic identification did not advance one tick at {loadKg} kg.");

                DynamicSample sample = CaptureSample(loadKg, loadIndex, repeat, validation, tick, inputDegrees, adapter);
                if (tick == WarmupTicks - 1)
                {
                    baselineAnkle = sample.AppliedAnkleDeg;
                    baselineHip = sample.AppliedHipDeg;
                    baselineTrunk = sample.AppliedTrunkDeg;
                }

                if (tick >= WarmupTicks)
                {
                    sample.AppliedAnkleDeltaRad = (sample.AppliedAnkleDeg - baselineAnkle) * Mathf.Deg2Rad;
                    sample.AppliedHipDeltaRad = (sample.AppliedHipDeg - baselineHip) * Mathf.Deg2Rad;
                    sample.AppliedTrunkDeltaRad = (sample.AppliedTrunkDeg - baselineTrunk) * Mathf.Deg2Rad;
                    destination.Add(sample);
                }

                if (tick % 40 == 0)
                    yield return null;
            }

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

        private DynamicSample CaptureSample(
            float loadKg,
            int loadIndex,
            int repeat,
            bool validation,
            int tick,
            Vector3 inputDegrees,
            SquatPhysicalAdapter adapter)
        {
            Assert.That(_controller.ObservationCollector.HasLastSnapshot, Is.True);
            SquatObservationSnapshot snapshot = _controller.ObservationCollector.LastSnapshot;
            SquatBalanceObserver balance = adapter.Balance;
            PoweredJointController.PoweredJointRuntime ankle = _rig.PoweredController.GetJoint("left_foot");
            PoweredJointController.PoweredJointRuntime hip = _rig.PoweredController.GetJoint("left_thigh");
            PoweredJointController.PoweredJointRuntime abdomen = _rig.PoweredController.GetJoint("abdomen");
            PoweredJointController.PoweredJointRuntime thorax = _rig.PoweredController.GetJoint("thorax");

            float trunkPitch = snapshot.TrunkWorldPitchRadians;
            float pelvisY = snapshot.PelvisPositionWorldMeters.Y;
            float captureMargin = Mathf.Min(balance.CaptureMarginFront, balance.CaptureMarginRear);
            float saddleSeparation = _controller.Saddle == null
                ? float.PositiveInfinity
                : _controller.Saddle.SaddleSeparationMeters;
            float limit = Mathf.Max(
                ankle.PostPhysicsDiagnostic.LimitProximity,
                hip.PostPhysicsDiagnostic.LimitProximity,
                abdomen.PostPhysicsDiagnostic.LimitProximity,
                thorax.PostPhysicsDiagnostic.LimitProximity);

            return new DynamicSample
            {
                LoadKg = loadKg,
                LoadIndex = loadIndex,
                Repeat = repeat,
                Split = validation ? "validation" : "identification",
                Tick = tick - WarmupTicks,
                TimeSeconds = (float)((tick - WarmupTicks) * SimulationConstants.FixedDeltaTimeSeconds),
                InputAnkleRad = inputDegrees.x * Mathf.Deg2Rad,
                InputHipRad = inputDegrees.y * Mathf.Deg2Rad,
                InputTrunkRad = inputDegrees.z * Mathf.Deg2Rad,
                AppliedAnkleDeg = LogicalDegrees(ankle.PostPhysicsDiagnostic.AppliedTarget),
                AppliedHipDeg = LogicalDegrees(hip.PostPhysicsDiagnostic.AppliedTarget),
                AppliedTrunkDeg = 0.5f * (
                    LogicalDegrees(abdomen.PostPhysicsDiagnostic.AppliedTarget) +
                    LogicalDegrees(thorax.PostPhysicsDiagnostic.AppliedTarget)),
                ValidLocal = IsValidLocal(balance, pelvisY, trunkPitch, captureMargin, saddleSeparation, limit),
                InvalidReason = InvalidReasonOf(balance, pelvisY, trunkPitch, captureMargin, saddleSeparation, limit),
                HasSupport = balance.HasSupport,
                SupportContacts = balance.SupportContactCount,
                HasCop = balance.HasCopEstimate,
                CopAp = balance.HasCopEstimate ? balance.CopEstimate.z : float.NaN,
                ComAp = balance.SystemCom.z,
                ComVelocityAp = balance.SystemComVelocity.z,
                CaptureAp = balance.CaptureAp,
                CaptureMargin = captureMargin,
                TrunkPitch = trunkPitch,
                PelvisY = pelvisY,
                SaddleSeparation = saddleSeparation
            };
        }

        private static void ApplyInput(SquatPhysicalAdapter adapter, Vector3 inputDegrees)
        {
            adapter.AnkleSagittalOffsetOverrideRad = null;
            adapter.AnkleSagittalOffsetAdditiveRad =
                Mathf.Clamp(inputDegrees.x, -MaximumInputDegrees, MaximumInputDegrees) * Mathf.Deg2Rad;
            SetLogicalBias(adapter, SquatJointFamily.Hip, inputDegrees.y);
            SetLogicalBias(adapter, SquatJointFamily.Abdomen, inputDegrees.z);
            SetLogicalBias(adapter, SquatJointFamily.Thorax, inputDegrees.z);
        }

        private static void SetLogicalBias(SquatPhysicalAdapter adapter, SquatJointFamily family, float logicalDegrees)
        {
            adapter.Preload.SetAnatomicalFlexionBiasDegrees(
                family,
                logicalDegrees * adapter.FamilyFlexionSign(family));
        }

        private static Vector3 Excitation(int tick, int loadIndex, bool validation)
        {
            float time = tick * (float)SimulationConstants.FixedDeltaTimeSeconds;
            float phase = loadIndex * 0.37f + (validation ? 1.13f : 0f);
            return new Vector3(
                BoundedSignal(time, 0.35f, 0.71f, phase, 0.24f),
                BoundedSignal(time, 0.47f, 0.83f, phase + 1.7f, 0.19f),
                BoundedSignal(time, 0.29f, 0.59f, phase + 3.1f, 0.14f));
        }

        private static float BoundedSignal(float time, float firstHz, float secondHz, float phase, float amplitude) =>
            amplitude * (Mathf.Sin(2f * Mathf.PI * firstHz * time + phase) +
                         0.5f * Mathf.Sin(2f * Mathf.PI * secondHz * time + 0.7f * phase)) / 1.5f;

        private static bool IsValidLocal(
            SquatBalanceObserver balance,
            float pelvisY,
            float trunkPitch,
            float captureMargin,
            float saddleSeparation,
            float limit) =>
            IsFinite(pelvisY) && IsFinite(trunkPitch) && IsFinite(captureMargin) &&
            IsFinite(saddleSeparation) && balance.HasSupport && pelvisY > 0.85f &&
            Mathf.Abs(trunkPitch) < 0.70f && captureMargin >= -0.02f &&
            saddleSeparation <= 0.05f && limit < 0.95f;

        private static string InvalidReasonOf(
            SquatBalanceObserver balance,
            float pelvisY,
            float trunkPitch,
            float captureMargin,
            float saddleSeparation,
            float limit)
        {
            if (!IsFinite(pelvisY) || !IsFinite(trunkPitch) || !IsFinite(captureMargin) || !IsFinite(saddleSeparation))
                return "NON_FINITE";
            if (!balance.HasSupport)
                return "SUPPORT_LOST";
            if (pelvisY <= 0.85f)
                return "PELVIS_DEPARTURE";
            if (Mathf.Abs(trunkPitch) >= 0.70f)
                return "TRUNK_DEPARTURE";
            if (captureMargin < -0.02f)
                return "CAPTURE_DEPARTURE";
            if (saddleSeparation > 0.05f)
                return "SADDLE_DEPARTURE";
            if (limit >= 0.95f)
                return "JOINT_LIMIT";
            return "NONE";
        }

        private static float LogicalDegrees(Quaternion rotation) =>
            PoweredJointController.SignedTwistRadians(rotation, Vector3.right) * Mathf.Rad2Deg;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool AllFinite(List<DynamicSample> samples)
        {
            foreach (DynamicSample sample in samples)
            {
                if (!IsFinite(sample.ComAp) || !IsFinite(sample.ComVelocityAp) ||
                    !IsFinite(sample.CaptureAp) || !IsFinite(sample.TrunkPitch) ||
                    !IsFinite(sample.PelvisY) || !IsFinite(sample.SaddleSeparation))
                    return false;
            }
            return true;
        }

        private static void WriteCsv(string fileName, List<DynamicSample> samples)
        {
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Artifacts", "Measurements", "GAM-13"));
            Directory.CreateDirectory(directory);
            var csv = new StringBuilder();
            csv.AppendLine("plant_version,load_kg,load_index,repeat,split,tick,time_s,input_ankle_rad,input_hip_rad,input_trunk_rad,applied_ankle_deg,applied_hip_deg,applied_trunk_deg,applied_ankle_delta_rad,applied_hip_delta_rad,applied_trunk_delta_rad,valid_local,invalid_reason,has_support,support_contacts,has_cop,cop_ap_m,com_ap_m,com_velocity_ap_mps,capture_ap_m,capture_margin_m,trunk_pitch_rad,pelvis_y_m,saddle_separation_m");
            foreach (DynamicSample sample in samples)
            {
                csv.AppendLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "GAM13_PRODUCTION_PLANT_V1,{0:R},{1},{2},{3},{4},{5:R},{6:R},{7:R},{8:R},{9:R},{10:R},{11:R},{12:R},{13:R},{14:R},{15},{16},{17},{18},{19},{20},{21:R},{22:R},{23:R},{24:R},{25:R},{26:R},{27:R}",
                    sample.LoadKg, sample.LoadIndex, sample.Repeat, sample.Split, sample.Tick, sample.TimeSeconds,
                    sample.InputAnkleRad, sample.InputHipRad, sample.InputTrunkRad,
                    sample.AppliedAnkleDeg, sample.AppliedHipDeg, sample.AppliedTrunkDeg,
                    sample.AppliedAnkleDeltaRad, sample.AppliedHipDeltaRad, sample.AppliedTrunkDeltaRad,
                    sample.ValidLocal, sample.InvalidReason, sample.HasSupport, sample.SupportContacts, sample.HasCop,
                    sample.CopAp, sample.ComAp, sample.ComVelocityAp, sample.CaptureAp, sample.CaptureMargin,
                    sample.TrunkPitch, sample.PelvisY, sample.SaddleSeparation));
            }
            File.WriteAllText(Path.Combine(directory, fileName), csv.ToString());
        }

        private sealed class DynamicSample
        {
            public float LoadKg;
            public int LoadIndex;
            public int Repeat;
            public string Split;
            public int Tick;
            public float TimeSeconds;
            public float InputAnkleRad;
            public float InputHipRad;
            public float InputTrunkRad;
            public float AppliedAnkleDeg;
            public float AppliedHipDeg;
            public float AppliedTrunkDeg;
            public float AppliedAnkleDeltaRad;
            public float AppliedHipDeltaRad;
            public float AppliedTrunkDeltaRad;
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
            public float TrunkPitch;
            public float PelvisY;
            public float SaddleSeparation;
        }
    }
}
