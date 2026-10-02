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
    /// GAM-13 per-tick mechanism diagnostics from scene start, including the
    /// setup and walkout interval the attempt trace deliberately excludes.
    /// Explicit only; writes raw CSVs to GAM13_DIAG_DIR (default Temp).
    /// </summary>
    public sealed class GAM13SquatLoadDiagnosticTests
    {
        private static readonly string[] Joints = { "abdomen", "thorax", "left_thigh", "left_shank", "left_foot" };

        [UnityTest]
        [Explicit("GAM-13 mechanism diagnostic; excluded from default qualification suites.")]
        public IEnumerator GAM13_SETUP_AND_ATTEMPT_TICK_DIAGNOSTIC()
        {
            LogAssert.ignoreFailingMessages = true;
            string loadsText = Environment.GetEnvironmentVariable("GAM13_DIAG_LOADS") ?? "25,60";
            string variantsText = Environment.GetEnvironmentVariable("GAM13_DIAG_VARIANTS") ?? "prod";
            string ticksText = Environment.GetEnvironmentVariable("GAM13_DIAG_TICKS") ?? "700";
            string directory = Environment.GetEnvironmentVariable("GAM13_DIAG_DIR") ?? Path.GetTempPath();
            int maximumTicks = int.Parse(ticksText, CultureInfo.InvariantCulture);
            Directory.CreateDirectory(directory);

            foreach (string variant in variantsText.Split(','))
            {
                foreach (string loadText in loadsText.Split(','))
                {
                    float load = float.Parse(loadText, CultureInfo.InvariantCulture);
                    yield return RunOne(variant.Trim(), load, maximumTicks, directory);
                }
            }

            LogAssert.ignoreFailingMessages = false;
        }

        private static readonly string[] SpineTables = { "AbdomenBias0Kg", "ThoraxBias0Kg", "AbdomenBias25Kg", "ThoraxBias25Kg" };

        private static float[] SpineTable(string name) =>
            (float[])typeof(SquatEquilibriumPreload)
                .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);

        private static IEnumerator RunOne(string variant, float load, int maximumTicks, string directory)
        {
            JointFamilyProfile[] original = SnapshotProfiles();
            float[][] originalSpine = new float[SpineTables.Length][];
            for (int index = 0; index < SpineTables.Length; index++)
                originalSpine[index] = (float[])SpineTable(SpineTables[index]).Clone();
            try
            {
                if (Has(variant, "biaslinear"))
                    ExtrapolateSpineTable(load);
                if (Has(variant, "impedance2") || Has(variant, "impedance3"))
                    ScaleSpineTable(1f / ImpedanceFactor(variant));
                ApplyProfileVariant(variant);
                IEnumerator inner = RunOneInner(variant, load, maximumTicks, directory);
                while (true)
                {
                    object current;
                    try
                    {
                        if (!inner.MoveNext())
                            break;
                        current = inner.Current;
                    }
                    catch
                    {
                        RestoreProfiles(original);
                        throw;
                    }
                    yield return current;
                }
            }
            finally
            {
                RestoreProfiles(original);
                for (int index = 0; index < SpineTables.Length; index++)
                    Array.Copy(originalSpine[index], SpineTable(SpineTables[index]), originalSpine[index].Length);
            }
        }

        /// <summary>
        /// Test-only counterfactual: linearly extrapolate the qualified 0/25 kg
        /// spine equilibrium table to the probe load, clamped to the preload's
        /// own 12 degree hard bound, and write it into the 25 kg column. The
        /// adapter is then evaluated at 25 kg so the domain guard selects it.
        /// </summary>
        private static void ExtrapolateSpineTable(float load)
        {
            float hard = SquatEquilibriumPreload.HardBoundRad * Mathf.Rad2Deg;
            float[] a0 = SpineTable("AbdomenBias0Kg"), t0 = SpineTable("ThoraxBias0Kg");
            float[] a25 = SpineTable("AbdomenBias25Kg"), t25 = SpineTable("ThoraxBias25Kg");
            for (int index = 0; index < a25.Length; index++)
            {
                a25[index] = Mathf.Clamp(a0[index] + (a25[index] - a0[index]) * load / 25f, -hard, hard);
                t25[index] = Mathf.Clamp(t0[index] + (t25[index] - t0[index]) * load / 25f, -hard, hard);
            }
            _biasLinearLoad = load;
        }

        private static float _biasLinearLoad;

        private static float ImpedanceFactor(string variant) => Has(variant, "impedance3") ? 3f : Has(variant, "impedance2") ? 2f : 1f;

        private static bool Has(string variant, string token) => Array.IndexOf(variant.Split('+'), token) >= 0;

        /// <summary>
        /// Test-only: a target bias is worth K times its angle, so a K-scaled
        /// plant needs the identified spine table divided by the same factor.
        /// </summary>
        private static void ScaleSpineTable(float factor)
        {
            foreach (string name in SpineTables)
            {
                float[] table = SpineTable(name);
                for (int index = 0; index < table.Length; index++)
                    table[index] *= factor;
            }
        }

        private static JointFamilyProfile[] ProfileArray() =>
            (JointFamilyProfile[])typeof(PoweredJointController)
                .GetField("Profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                .GetValue(null);

        private static JointFamilyProfile[] SnapshotProfiles() => (JointFamilyProfile[])ProfileArray().Clone();

        private static void RestoreProfiles(JointFamilyProfile[] original)
        {
            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
                live[index] = original[index];
        }

        /// <summary>
        /// Test-only counterfactual mutation of the static family table. The
        /// table is restored in finally; every new scene load builds its
        /// controller from the mutated copy.
        /// </summary>
        private static void ApplyProfileVariant(string variant)
        {
            float capacity = 1f;
            float stiffness = 1f;
            bool spineOnly = false;
            foreach (string token in variant.Split('+'))
            {
                switch (token)
                {
                    case "cap4": capacity = 4f; break;
                    case "cap025": capacity = 0.25f; break;
                    case "spine2": stiffness = 2f; spineOnly = true; break;
                    case "stiff2": stiffness = 2f; break;
                    case "stiff3": stiffness = 3f; break;
                    case "stiff4": stiffness = 4f; break;
                    case "impedance2": stiffness = 2f; break;
                    case "impedance3": stiffness = 3f; break;
                }
            }
            if (capacity == 1f && stiffness == 1f)
                return;

            JointFamilyProfile[] live = ProfileArray();
            for (int index = 0; index < live.Length; index++)
            {
                JointFamilyProfile p = live[index];
                bool loadBearing = p.Id == "ankle" || p.Id == "knee" || p.Id == "hip" || p.Id == "trunk";
                bool stiffen = spineOnly ? p.Id == "trunk" : loadBearing;
                float k = stiffen ? stiffness : 1f;
                // Keep the damping ratio: c scales with sqrt(k) for a fixed inertia.
                live[index] = new JointFamilyProfile(p.Id, p.Spring * k, p.Damper * Mathf.Sqrt(k), p.BaseCapacityNm * capacity, p.MaxTargetRateRadS);
            }
        }

        private static IEnumerator RunOneInner(string variant, float load, int maximumTicks, string directory)
        {
            AsyncOperation operation = SceneManager.LoadSceneAsync(GAM13SquatLoadCalibrationHarness.QualificationScene, LoadSceneMode.Single);
            while (!operation.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>();
            SquatPhysicalPrototypeController controller = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            for (int frame = 0; frame < 8 && !controller.IsInitialized; frame++)
                yield return null;
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);
            controller.enabled = false;
            bootstrap.enabled = false;
            controller.SetLoad(load);

            SquatPhysicalAdapter adapter = controller.Adapter;
            ApplyVariant(variant, adapter, bootstrap.Runtime);
            controller.BeginAttempt();

            PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>();
            StringBuilder csv = new StringBuilder();
            csv.Append("tick,lifecycle,state,sq,bar_y,bar_vy,pelvis_y,trunk_pitch,com_z,com_vz,support_ap_min,support_ap_max,has_support,left_contact,right_contact,max_demand,");
            csv.Append("ankle_offset,hip_offset,trunk_offset,guard_scale,com_ap_error,cop_measured,cop_desired");
            foreach (string joint in Joints)
                csv.Append($",{joint}_actual,{joint}_ref,{joint}_drive_err,{joint}_demand,{joint}_solver_tq,{joint}_limit,{joint}_maxforce");
            csv.AppendLine();

            FoundationRuntime runtime = bootstrap.Runtime;
            bool driveIssued = false;
            for (int tick = 0; tick < maximumTicks && controller.AttemptRecord == null; tick++)
            {
                runtime.AdvanceRenderFrame(SimulationConstants.FixedDeltaTimeSeconds);
                if (!driveIssued && (adapter.State == SquatState.REVERSAL || adapter.State == SquatState.ASCENT))
                {
                    runtime.InputBuffer.SetContinuous(IntentAction.Drive, 1f,
                        runtime.CurrentTime.SimulationTimeSeconds + 0.25d * SimulationConstants.FixedDeltaTimeSeconds);
                    driveIssued = true;
                }

                SquatObservationSnapshot s = controller.ObservationCollector.LastSnapshot;
                SquatPredictiveBalanceController balance = adapter.BalanceController;
                csv.Append(string.Join(",",
                    F(s.SimulationTick), controller.AttemptLifecycle.State, s.State, F(s.Sq),
                    F(s.Bar.IsAvailable ? s.Bar.PositionWorldMeters.Y : float.NaN),
                    F(s.Bar.IsAvailable ? s.Bar.LinearVelocityWorldMetersPerSecond.Y : float.NaN),
                    F(s.PelvisPositionWorldMeters.Y), F(s.TrunkWorldPitchRadians),
                    F(s.Support.SystemComWorldMeters.Z), F(s.Support.SystemComVelocityWorldMetersPerSecond.Z),
                    F(s.Support.SupportApMinM), F(s.Support.SupportApMaxM), s.Support.HasSupport,
                    s.LeftFoot.IsInContact, s.RightFoot.IsInContact, F(s.MaximumModeledDemand),
                    F(balance.AnkleSagittalOffsetRad), F(balance.HipSagittalOffsetRad), F(balance.TrunkSagittalOffsetRad),
                    F(balance.PostureGuardScale), F(balance.ComApError), F(balance.CopMeasuredAp), F(balance.CopDesiredAp)));
                foreach (string jointId in Joints)
                {
                    PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
                    PoweredJointDiagnostic d = joint.PostPhysicsDiagnostic;
                    float actual = PoweredJointController.SignedTwistRadians(d.ActualRelative, Vector3.right);
                    float target = PoweredJointController.SignedTwistRadians(d.AppliedTarget, Vector3.right);
                    csv.Append(',').Append(string.Join(",",
                        F(actual), F(target), F(d.ErrorRad.x), F(d.ModeledDemand), F(d.SolverFlexionTorqueNm),
                        F(d.LimitProximity), F(d.MaximumForceNm)));
                }
                csv.AppendLine();
                if (tick % 50 == 0)
                    yield return null;
            }

            SquatAttemptRecord record = controller.AttemptRecord;
            if (record != null)
            {
                ulong lockout = record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT
                    ? record.TerminalTick
                    : SquatLoadResponseMetrics.NotAvailableTick;
                SquatLoadResponseMetrics metrics = SquatLoadResponseAnalyzer.Analyze(record.Trace, lockout);
                GAM13AttemptResult result = new GAM13AttemptResult
                {
                    Sweep = "diag", Candidate = variant, LoadKg = load, RunIndex = 1, Record = record, Metrics = metrics,
                    SquatCommandTick = record.EventTicks.SquatCommandTick, DriveIntentTick = SquatAttemptEventTicks.NotAvailable
                };
                Debug.Log("GAM13_DIAG_ATTEMPT " + GAM13SquatLoadCalibrationHarness.SummaryLine(result));
            }
            else
            {
                Debug.Log($"GAM13_DIAG_ATTEMPT variant={variant} load={load} NO_RECORD lifecycle={controller.AttemptLifecycle.State} state={adapter.State}");
            }

            string path = Path.Combine(directory, $"gam13-diag-{variant}-{load.ToString("0.##", CultureInfo.InvariantCulture)}kg.csv");
            File.WriteAllText(path, csv.ToString());
            Debug.Log("GAM13_DIAG wrote " + path);
            yield return null;
        }

        private static void ApplyVariant(string variant, SquatPhysicalAdapter adapter, FoundationRuntime runtime)
        {
            foreach (string token in variant.Split('+'))
                ApplyVariantToken(token, adapter, runtime);
        }

        private static void ApplyVariantToken(string variant, SquatPhysicalAdapter adapter, FoundationRuntime runtime)
        {
            switch (variant)
            {
                case "noguard":
                    adapter.BalanceController.PostureGuardEnabled = false;
                    break;
                case "prod":
                    break;
                case "nobalance":
                    adapter.BalanceCorrectionsEnabled = false;
                    break;
                case "preload25":
                    adapter.EquilibriumLoadKg = 25f;
                    break;
                case "nopreload":
                    adapter.Preload.SpineCalibrationEnabled = false;
                    break;
                case "biaslinear":
                {
                    float hard = SquatEquilibriumPreload.HardBoundRad * Mathf.Rad2Deg;
                    SquatEquilibriumPreload preload = adapter.Preload;
                    float l = _biasLinearLoad;
                    preload.StandingAbdomenBiasDegrees25Kg = Mathf.Clamp(0.83f + (3.93f - 0.83f) * l / 25f, -hard, hard);
                    preload.StandingThoraxBiasDegrees25Kg = Mathf.Clamp(0.92f + (3.67f - 0.92f) * l / 25f, -hard, hard);
                    adapter.EquilibriumLoadKg = 25f;
                    Debug.Log($"GAM13_DIAG biaslinear load={l} standingAbdomen={preload.StandingAbdomenBiasDegrees25Kg:F2} standingThorax={preload.StandingThoraxBiasDegrees25Kg:F2}");
                    break;
                }
                case "impedance2":
                case "impedance3":
                {
                    float factor = ImpedanceFactor(variant);
                    SquatEquilibriumPreload preload = adapter.Preload;
                    preload.StandingAbdomenBiasDegrees0Kg /= factor;
                    preload.StandingThoraxBiasDegrees0Kg /= factor;
                    preload.StandingAbdomenBiasDegrees25Kg /= factor;
                    preload.StandingThoraxBiasDegrees25Kg /= factor;
                    // The ankle target moves the COP through the ankle spring.
                    adapter.BalanceController.TargetToCopMPerRad *= factor;
                    break;
                }
                case "cap4":
                case "cap025":
                case "spine2":
                case "stiff2":
                case "stiff3":
                case "stiff4":
                    break;
                default:
                    throw new ArgumentException("Unknown GAM-13 diagnostic variant " + variant);
            }
        }

        private static string F(object value)
        {
            switch (value)
            {
                case float f: return float.IsNaN(f) ? "NA" : f.ToString("0.#####", CultureInfo.InvariantCulture);
                case ulong u: return u.ToString(CultureInfo.InvariantCulture);
                case bool b: return b ? "1" : "0";
                default: return Convert.ToString(value, CultureInfo.InvariantCulture);
            }
        }
    }
}
