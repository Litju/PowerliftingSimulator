using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using UnityEngine.Profiling;

namespace PowerliftingSimulator.Qualification
{
    public sealed class GAM9StandaloneSmoke : MonoBehaviour
    {
        private const string SmokeArgument = "-gam9Smoke";
        private const string OutputArgument = "-gam9SmokeOutput";
        private const string ScreenshotArgument = "-gam9SmokeScreenshot";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForQualificationOnly()
        {
            if (!HasArgument(SmokeArgument))
                return;

            var runner = new GameObject("GAM9StandaloneSmoke");
            DontDestroyOnLoad(runner);
            runner.AddComponent<GAM9StandaloneSmoke>();
        }

        private IEnumerator Start()
        {
            yield return null;

            string outputPath = ResolvePath(OutputArgument, "GAM-9-standalone-smoke.json");
            string screenshotPath = ResolveOptionalPath(ScreenshotArgument);
            SmokeResult result = null;
            Exception failure = null;
            try
            {
                FoundationBootstrap bootstrap = FindFirstObjectByType<FoundationBootstrap>();
                PhysicalAthleteRig rig = FindFirstObjectByType<PhysicalAthleteRig>();
                PhysicalBarbell bar = FindFirstObjectByType<PhysicalBarbell>();
                if (bootstrap == null || rig == null || bar == null || bootstrap.Runtime == null || !bootstrap.Runtime.IsInitialized)
                    throw new InvalidOperationException("The qualification scene did not initialize its foundation, athlete, and barbell.");
                if (rig.Segments.Count != 16 || rig.Joints.Count != 15)
                    throw new InvalidOperationException($"Unexpected athlete topology {rig.Segments.Count}/{rig.Joints.Count}; expected 16/15.");
                if (rig.PoweredController.PoweredJointCount != 14 || rig.PoweredController.PassiveJointCount != 1)
                    throw new InvalidOperationException("The powered-joint split is not 14 powered and 1 passive.");
                if (!bar.IsBuilt || bar.Body == null || bar.Body.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                    throw new InvalidOperationException("The standalone barbell is not one dynamic Rigidbody.");

                rig.InspectNeutral();
                bootstrap.Runtime.AdvanceRenderFrame(0.01d);
                if (bootstrap.Runtime.CurrentObservation.BodyCount != 17)
                    throw new InvalidOperationException("The standalone post-physics observation does not contain 17 bodies.");

                rig.StartPoweredNeutral();
                float poweredStart = rig.CalculateWholeBodyCom().y;
                Advance(bootstrap, 75);
                float poweredDrop = poweredStart - rig.CalculateWholeBodyCom().y;
                if (!float.IsFinite(poweredDrop) || poweredDrop < 0f)
                    throw new InvalidOperationException("The standalone powered-neutral COM response is not finite.");

                bar.ConfigureLoad(105f);
                rig.StartPoweredNeutral();
                float barStart = bar.Body.position.y;
                Advance(bootstrap, 75);
                float barDrop = barStart - bar.Body.position.y;
                if (!float.IsFinite(barDrop) || barDrop < 0.05f)
                    throw new InvalidOperationException($"The standalone dynamic bar did not fall by the expected amount ({barDrop:0.000} m).");

                bar.ConfigureLoad(105f);
                rig.StartPoweredNeutral();
                Advance(bootstrap, 4);
                for (int index = 0; index < 25; index++)
                    Advance(bootstrap, 4);
                var catchUpSamplesMs = new double[100];
                for (int index = 0; index < catchUpSamplesMs.Length; index++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    Advance(bootstrap, 4);
                    timer.Stop();
                    catchUpSamplesMs[index] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(catchUpSamplesMs);
                double catchUpP95Ms = catchUpSamplesMs[(int)Math.Ceiling(catchUpSamplesMs.Length * 0.95d) - 1];
                double catchUpMaxMs = catchUpSamplesMs[catchUpSamplesMs.Length - 1];
                if (catchUpP95Ms > 8d)
                    throw new InvalidOperationException($"Standalone four-tick catch-up p95 exceeded the 8 ms budget ({catchUpP95Ms:0.000} ms).");

                rig.StartPoweredNeutral();
                for (int index = 0; index < 50; index++)
                    bootstrap.Runtime.StepOne();
                var physicsSamplesMs = new double[300];
                for (int index = 0; index < physicsSamplesMs.Length; index++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    bootstrap.Runtime.StepOne();
                    timer.Stop();
                    physicsSamplesMs[index] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(physicsSamplesMs);
                double physicsP95Ms = Percentile(physicsSamplesMs, 0.95d);
                double physicsMaxMs = physicsSamplesMs[physicsSamplesMs.Length - 1];
                if (physicsP95Ms > 2d)
                    throw new InvalidOperationException($"Standalone physics-tick p95 exceeded the 2 ms budget ({physicsP95Ms:0.000} ms).");

                rig.StartPoweredNeutral();
                for (int index = 0; index < 50; index++)
                    Advance(bootstrap, 1);
                var renderSamplesMs = new double[300];
                for (int index = 0; index < renderSamplesMs.Length; index++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    Advance(bootstrap, 1);
                    timer.Stop();
                    renderSamplesMs[index] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(renderSamplesMs);
                double renderP95Ms = Percentile(renderSamplesMs, 0.95d);
                double renderMaxMs = renderSamplesMs[renderSamplesMs.Length - 1];
                if (renderP95Ms > 10d)
                    throw new InvalidOperationException($"Standalone CPU frame p95 exceeded the 10 ms budget ({renderP95Ms:0.000} ms).");

                rig.StartPoweredNeutral();
                for (int index = 0; index < 50; index++)
                    rig.PoweredController.Step(bootstrap.Runtime.CurrentTime, PowerliftingSimulator.Foundation.PlayerIntentFrame.Empty);
                var controllerSamplesMs = new double[300];
                for (int index = 0; index < controllerSamplesMs.Length; index++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    rig.PoweredController.Step(bootstrap.Runtime.CurrentTime, PowerliftingSimulator.Foundation.PlayerIntentFrame.Empty);
                    timer.Stop();
                    controllerSamplesMs[index] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(controllerSamplesMs);
                double controllerP95Ms = Percentile(controllerSamplesMs, 0.95d);
                double controllerMaxMs = controllerSamplesMs[controllerSamplesMs.Length - 1];
                if (controllerP95Ms > 0.25d)
                    throw new InvalidOperationException($"Standalone controller p95 exceeded the 0.25 ms budget ({controllerP95Ms:0.000} ms).");

                Advance(bootstrap, 1);
                if (bootstrap.Runtime.CurrentObservation.BodyCount != 17)
                    throw new InvalidOperationException("The final standalone post-physics observation does not contain 17 bodies.");

                long runtimeAllocatedMemoryBytes = Profiler.GetTotalAllocatedMemoryLong();
                if (runtimeAllocatedMemoryBytes > 2L * 1024L * 1024L * 1024L)
                    throw new InvalidOperationException($"Standalone allocated runtime memory exceeded the 2 GB budget ({runtimeAllocatedMemoryBytes} bytes).");

                result = new SmokeResult
                {
                    schema = "GAM9_STANDALONE_SMOKE_V1",
                    status = "PASS",
                    unityVersion = Application.unityVersion,
                    scene = "PhysicalAthletePhysics",
                    bodyCount = bootstrap.Runtime.CurrentObservation.BodyCount,
                    jointCount = rig.Joints.Count,
                    poweredJointCount = rig.PoweredController.PoweredJointCount,
                    passiveJointCount = rig.PoweredController.PassiveJointCount,
                    barMassKg = bar.LoadedMassKg,
                    poweredComDropM = poweredDrop,
                    barDropM = barDrop,
                    fourTickCatchUpP95Ms = catchUpP95Ms,
                    fourTickCatchUpMaxMs = catchUpMaxMs,
                    physicsTickP95Ms = physicsP95Ms,
                    physicsTickMaxMs = physicsMaxMs,
                    renderFrameP95Ms = renderP95Ms,
                    renderFrameMaxMs = renderMaxMs,
                    controllerP95Ms = controllerP95Ms,
                    controllerMaxMs = controllerMaxMs,
                    runtimeAllocatedMemoryBytes = runtimeAllocatedMemoryBytes,
                    processWorkingSetBytes = GetProcessWorkingSetBytes(),
                    screenshotPath = screenshotPath ?? string.Empty,
                    authoritativeSceneName = bootstrap.Runtime.AuthoritativeScene.name
                };
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure != null)
            {
                result = new SmokeResult
                {
                    schema = "GAM9_STANDALONE_SMOKE_V1",
                    status = "FAIL",
                    unityVersion = Application.unityVersion,
                    error = failure.ToString(),
                    screenshotPath = screenshotPath ?? string.Empty
                };
                WriteResult(outputPath, result);
                UnityEngine.Debug.LogException(failure);
                Application.Quit(1);
                yield break;
            }

            if (!string.IsNullOrEmpty(screenshotPath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(screenshotPath));
                yield return new WaitForEndOfFrame();
                ScreenCapture.CaptureScreenshot(screenshotPath);
                yield return null;
            }

            WriteResult(outputPath, result);
            UnityEngine.Debug.Log($"GAM9_STANDALONE_SMOKE PASS bodyCount={result.bodyCount} poweredDrop={result.poweredComDropM:0.000} barDrop={result.barDropM:0.000}");
            Application.Quit(0);
        }

        private static void Advance(FoundationBootstrap bootstrap, int ticks)
        {
            for (int index = 0; index < ticks; index++)
            {
                int advanced = bootstrap.Runtime.AdvanceRenderFrame(0.01d);
                if (advanced != 1)
                    throw new InvalidOperationException($"Expected one authoritative tick per smoke frame, observed {advanced}.");
            }
        }

        private static double Percentile(double[] sorted, double percentile)
        {
            int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
            index = Math.Max(0, Math.Min(index, sorted.Length - 1));
            return sorted[index];
        }

        private static long GetProcessWorkingSetBytes()
        {
#if UNITY_STANDALONE_WIN
            try
            {
                ProcessMemoryCounters counters = new ProcessMemoryCounters
                {
                    cb = (uint)Marshal.SizeOf(typeof(ProcessMemoryCounters))
                };
                Process process = Process.GetCurrentProcess();
                if (GetProcessMemoryInfo(process.Handle, out counters, counters.cb))
                    return unchecked((long)counters.WorkingSetSize.ToUInt64());
            }
            catch
            {
                // Fall through to the managed process API when native metrics are unavailable.
            }
#endif
            try
            {
                Process process = Process.GetCurrentProcess();
                process.Refresh();
                return process.WorkingSet64;
            }
            catch
            {
                return 0L;
            }
        }

#if UNITY_STANDALONE_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCounters
        {
            public uint cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr process, out ProcessMemoryCounters counters, uint size);
#endif

        private static bool HasArgument(string argument)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length; index++)
            {
                if (string.Equals(arguments[index], argument, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static string ResolveOptionalPath(string argument)
        {
            string value = ReadArgument(argument);
            return string.IsNullOrWhiteSpace(value) ? null : Path.GetFullPath(value);
        }

        private static string ResolvePath(string argument, string fallbackFileName)
        {
            string value = ReadArgument(argument);
            if (string.IsNullOrWhiteSpace(value))
                value = Path.Combine(Application.persistentDataPath, fallbackFileName);
            return Path.GetFullPath(value);
        }

        private static string ReadArgument(string argument)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int index = 0; index < arguments.Length - 1; index++)
            {
                if (string.Equals(arguments[index], argument, StringComparison.OrdinalIgnoreCase))
                    return arguments[index + 1];
            }
            return null;
        }

        private static void WriteResult(string path, SmokeResult result)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
        }

        [Serializable]
        private sealed class SmokeResult
        {
            public string schema;
            public string status;
            public string unityVersion;
            public string scene;
            public int bodyCount;
            public int jointCount;
            public int poweredJointCount;
            public int passiveJointCount;
            public float barMassKg;
            public float poweredComDropM;
            public float barDropM;
            public double fourTickCatchUpP95Ms;
            public double fourTickCatchUpMaxMs;
            public double physicsTickP95Ms;
            public double physicsTickMaxMs;
            public double renderFrameP95Ms;
            public double renderFrameMaxMs;
            public double controllerP95Ms;
            public double controllerMaxMs;
            public long runtimeAllocatedMemoryBytes;
            public long processWorkingSetBytes;
            public string screenshotPath;
            public string authoritativeSceneName;
            public string error;
        }
    }
}
