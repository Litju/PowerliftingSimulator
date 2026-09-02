using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Foundation.Unity;
using UnityEngine;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace PowerliftingSimulator.Qualification
{
    public sealed class GAM9StandaloneSmoke : MonoBehaviour
    {
        private const string SmokeArgument = "-gam9Smoke";
        private const string AllocationDevelopmentArgument = "-gam9SmokeAllocDev";
        private const string PerformanceReleaseArgument = "-gam9SmokePerfRelease";
        private const string OutputArgument = "-gam9SmokeOutput";
        private const string ScreenshotArgument = "-gam9SmokeScreenshot";
        private const int RenderWarmupFrameCount = 600;
        private const int RenderMeasurementFrameCount = 1000;

        private FrameTimingCollector _frameTimingCollector;
        private StandaloneAllocationCapture _gcFrameCollector;
        private FoundationBootstrap _manualFrameDriver;
        private string _manualFrameDriverFailure;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallForQualificationOnly()
        {
            if (!HasArgument(SmokeArgument))
                return;

            var runner = new GameObject("GAM9StandaloneSmoke");
            DontDestroyOnLoad(runner);
            runner.AddComponent<GAM9StandaloneSmoke>();
        }

        private void Update()
        {
            if (_manualFrameDriver != null)
            {
                try
                {
                    int advanced = _manualFrameDriver.Runtime.AdvanceRenderFrame(0.01d);
                    if (advanced != 1)
                        _manualFrameDriverFailure = "Standalone manual frame driver expected one physics tick and observed " + advanced + ".";
                }
                catch (Exception exception)
                {
                    _manualFrameDriverFailure = exception.ToString();
                }
            }

            if (_frameTimingCollector != null)
                _frameTimingCollector.Capture();
            if (_gcFrameCollector != null)
                _gcFrameCollector.Capture();
        }

        private IEnumerator Start()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = -1;
            bool allocationDevelopment = HasArgument(AllocationDevelopmentArgument) ||
                (!HasArgument(PerformanceReleaseArgument) && UnityEngine.Debug.isDebugBuild);
            bool performanceRelease = !allocationDevelopment;
            string buildConfiguration = allocationDevelopment ? "GAM9_ALLOC_DEV" : "GAM9_PERF_RELEASE";
            yield return null;

            int resolutionGuard = 0;
            while ((Screen.width != 1280 || Screen.height != 720) && resolutionGuard < 600)
            {
                resolutionGuard++;
                yield return null;
            }

            if (Screen.width != 1280 || Screen.height != 720)
            {
                string outputPathBeforeResolutionFailure = ResolvePath(OutputArgument, "GAM-9-standalone-smoke.json");
                SmokeResult resolutionFailure = CreateEnvironmentFailure(buildConfiguration, "The standalone qualification window did not settle at the required 1280x720 resolution.");
                WriteResult(outputPathBeforeResolutionFailure, resolutionFailure);
                UnityEngine.Debug.LogError(resolutionFailure.error);
                Application.Quit(1);
                yield break;
            }

            FrameTimingCollector fullFrameTiming = null;

            string outputPath = ResolvePath(OutputArgument, "GAM-9-standalone-smoke.json");
            string screenshotPath = ResolveOptionalPath(ScreenshotArgument);
            SmokeResult result = null;
            Exception failure = null;
            FoundationBootstrap bootstrap = null;
            PhysicalAthleteRig rig = null;
            PhysicalBarbell bar = null;
            StandaloneAllocationCapture allocationOff = null;
            StandaloneAllocationCapture allocationTrace = null;
            bool traceRecording = false;
            try
            {
                bootstrap = FindFirstObjectByType<FoundationBootstrap>();
                rig = FindFirstObjectByType<PhysicalAthleteRig>();
                bar = FindFirstObjectByType<PhysicalBarbell>();
                if (bootstrap == null || rig == null || bar == null || bootstrap.Runtime == null || !bootstrap.Runtime.IsInitialized)
                    throw new InvalidOperationException("The qualification scene did not initialize its foundation, athlete, and barbell.");
                if (rig.Segments.Count != 16 || rig.Joints.Count != 15)
                    throw new InvalidOperationException($"Unexpected athlete topology {rig.Segments.Count}/{rig.Joints.Count}; expected 16/15.");
                if (rig.PoweredController.PoweredJointCount != 14 || rig.PoweredController.PassiveJointCount != 1)
                    throw new InvalidOperationException("The powered-joint split is not 14 powered and 1 passive.");
                if (!bar.IsBuilt || bar.Body == null || bar.Body.GetComponentsInChildren<Rigidbody>(true).Length != 1)
                    throw new InvalidOperationException("The standalone barbell is not one dynamic Rigidbody.");
                if (performanceRelease)
                {
                    if (UnityEngine.Debug.isDebugBuild)
                        throw new InvalidOperationException("GAM9_PERF_RELEASE must run in a non-development player.");
                    if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D11)
                        throw new InvalidOperationException($"GAM9_PERF_RELEASE requires D3D11; observed {SystemInfo.graphicsDeviceType}.");
                    rig.SetGameplayPerformanceProfile(true);
                    bar.SetGameplayPerformanceProfile(true);
                }
                else if (!UnityEngine.Debug.isDebugBuild)
                {
                    throw new InvalidOperationException("GAM9_ALLOC_DEV must run in a Development player.");
                }

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
                var foundationFrameStepSamplesMs = new double[300];
                for (int index = 0; index < foundationFrameStepSamplesMs.Length; index++)
                {
                    Stopwatch timer = Stopwatch.StartNew();
                    Advance(bootstrap, 1);
                    timer.Stop();
                    foundationFrameStepSamplesMs[index] = timer.Elapsed.TotalMilliseconds;
                }
                Array.Sort(foundationFrameStepSamplesMs);
                double foundationFrameStepP50Ms = Percentile(foundationFrameStepSamplesMs, 0.50d);
                double foundationFrameStepP95Ms = Percentile(foundationFrameStepSamplesMs, 0.95d);
                double foundationFrameStepP99Ms = Percentile(foundationFrameStepSamplesMs, 0.99d);
                double foundationFrameStepWorstMs = foundationFrameStepSamplesMs[foundationFrameStepSamplesMs.Length - 1];
                if (foundationFrameStepP95Ms > 10d)
                    throw new InvalidOperationException($"Standalone foundation frame-step p95 exceeded the 10 ms diagnostic budget ({foundationFrameStepP95Ms:0.000} ms).");

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
                    buildConfiguration = buildConfiguration,
                    screenWidth = Screen.width,
                    screenHeight = Screen.height,
                    refreshRateHz = GetRefreshRateHz(),
                    graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                    targetFrameRate = Application.targetFrameRate,
                    vSyncCount = QualitySettings.vSyncCount,
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
                    foundationFrameStep = new FrameMetricSummary
                    {
                        available = true,
                        sampleCount = foundationFrameStepSamplesMs.Length,
                        p50Ms = foundationFrameStepP50Ms,
                        p95Ms = foundationFrameStepP95Ms,
                        p99Ms = foundationFrameStepP99Ms,
                        worstMs = foundationFrameStepWorstMs
                    },
                    controllerP95Ms = controllerP95Ms,
                    controllerMaxMs = controllerMaxMs,
                    runtimeAllocatedMemoryBytes = runtimeAllocatedMemoryBytes,
                    processWorkingSetBytes = GetProcessWorkingSetBytes(),
                    screenshotPath = screenshotPath ?? string.Empty,
                    allocationMetric = performanceRelease
                        ? "NOT_CAPTURED_IN_GAM9_PERF_RELEASE; run GAM9_ALLOC_DEV for GC.Alloc qualification."
                        : string.Empty,
                    warmupFrameCount = performanceRelease ? RenderWarmupFrameCount : 0,
                    measurementFrameCount = performanceRelease ? RenderMeasurementFrameCount : 0,
                    gameplayPerformanceProfile = performanceRelease
                        ? "G1_GAMEPLAY_PERFORMANCE_PROFILE: visible humanoid=ON; visible barbell=ON; camera=ON; actual 100 Hz physics=ON; finite joint drives=ON; physical proxy renderers, COM/anchor/axis/bar debug visuals, trail, and qualification IMGUI=OFF."
                        : "NOT_APPLICABLE_TO_ALLOCATION_BUILD",
                     authoritativeSceneName = bootstrap.Runtime.AuthoritativeScene.name
                };

                if (performanceRelease)
                {
                    bar.ConfigureLoad(105f);
                    rig.StartPoweredNeutral();
                    fullFrameTiming = new FrameTimingCollector(RenderWarmupFrameCount, RenderMeasurementFrameCount);
                    _frameTimingCollector = fullFrameTiming;
                }
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            if (failure == null && performanceRelease)
            {
                int frameGuard = 0;
                while (!fullFrameTiming.Complete && frameGuard < 2400)
                {
                    frameGuard++;
                    yield return null;
                }
                _frameTimingCollector = null;
                if (!fullFrameTiming.Complete)
                    fullFrameTiming.MarkUnavailable("FrameTimingManager did not return enough actual rendered-frame samples within the collection guard.");
                result.fullCpuFrame = fullFrameTiming.CpuSummary;
                result.gpuFrame = fullFrameTiming.GpuSummary;
                result.cpuMainThreadFrame = fullFrameTiming.CpuMainThreadSummary;
                result.cpuRenderThreadFrame = fullFrameTiming.CpuRenderThreadSummary;
                result.cpuMainThreadPresentWait = fullFrameTiming.CpuMainThreadPresentWaitSummary;
                result.drawCalls = fullFrameTiming.DrawCallsSummary;
                result.batches = fullFrameTiming.BatchesSummary;
                result.setPassCalls = fullFrameTiming.SetPassCallsSummary;
                result.shadowCasters = fullFrameTiming.ShadowCastersSummary;
                result.visibleSkinnedMeshes = fullFrameTiming.VisibleSkinnedMeshesSummary;
                result.fullCpuFrameMeasurement = "UnityEngine.FrameTimingManager.cpuFrameTime over actual rendered standalone frames after warm-up; this is distinct from FoundationRuntime.AdvanceRenderFrame execution time.";
                result.gpuFrameMeasurement = fullFrameTiming.GpuAvailable
                    ? "UnityEngine.FrameTimingManager.gpuFrameTime over the same actual rendered frames."
                    : "GPU=NOT_AVAILABLE_IN_CURRENT_HARNESS";
                result.counterMeasurement = fullFrameTiming.CounterMeasurement;
                fullFrameTiming.Dispose();
                if (!fullFrameTiming.CpuAvailable)
                    failure = new InvalidOperationException(fullFrameTiming.FailureReason);
                else if (fullFrameTiming.CpuSummary.p95Ms > 10d)
                    failure = new InvalidOperationException($"Standalone full CPU frame p95 exceeded the 10 ms budget ({fullFrameTiming.CpuSummary.p95Ms:0.000} ms).");
                else if (fullFrameTiming.GpuAvailable && fullFrameTiming.GpuSummary.p95Ms > 12d)
                    failure = new InvalidOperationException($"Standalone GPU frame p95 exceeded the 12 ms budget ({fullFrameTiming.GpuSummary.p95Ms:0.000} ms).");
            }
            else
            {
                _frameTimingCollector = null;
            }

            if (failure == null && allocationDevelopment)
            {
                try
                {
                    bootstrap.Reset();
                    rig.StartPoweredNeutral();
                    rig.enabled = false;
                    bar.enabled = false;
                    bootstrap.enabled = false;
                    _manualFrameDriver = bootstrap;
                    _manualFrameDriverFailure = null;
                    allocationOff = new StandaloneAllocationCapture(120, 30);
                    _gcFrameCollector = allocationOff;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure == null && allocationDevelopment)
            {
                int allocationGuard = 0;
                while (!allocationOff.Complete && allocationGuard < 400)
                {
                    allocationGuard++;
                    yield return null;
                }
                _gcFrameCollector = null;
                _manualFrameDriver = null;
                allocationOff.Dispose();
                if (_manualFrameDriverFailure != null)
                    failure = new InvalidOperationException(_manualFrameDriverFailure);
                else if (!allocationOff.Complete)
                    failure = new InvalidOperationException("Standalone GC allocation collection did not complete within the guard.");
                else if (!allocationOff.Available)
                    failure = new InvalidOperationException(allocationOff.UnavailableReason);
            }

            if (failure == null && allocationDevelopment)
            {
                try
                {
                    bootstrap.Reset();
                    rig.StartPoweredNeutral();
                    bootstrap.Runtime.BeginAttemptTrace();
                    traceRecording = true;
                    _manualFrameDriverFailure = null;
                    _manualFrameDriver = bootstrap;
                    allocationTrace = new StandaloneAllocationCapture(120, 30);
                    _gcFrameCollector = allocationTrace;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            if (failure == null && allocationDevelopment)
            {
                int allocationGuard = 0;
                while (!allocationTrace.Complete && allocationGuard < 400)
                {
                    allocationGuard++;
                    yield return null;
                }
                _gcFrameCollector = null;
                _manualFrameDriver = null;
                if (traceRecording && bootstrap.Runtime.AttemptTrace.IsRecording)
                    bootstrap.Runtime.EndAttemptTrace();
                traceRecording = false;
                allocationTrace.Dispose();
                bootstrap.enabled = true;
                rig.enabled = true;
                bar.enabled = true;
                if (_manualFrameDriverFailure != null)
                    failure = new InvalidOperationException(_manualFrameDriverFailure);
                else if (!allocationTrace.Complete)
                    failure = new InvalidOperationException("Standalone traced GC allocation collection did not complete within the guard.");
                else if (!allocationTrace.Available)
                    failure = new InvalidOperationException(allocationTrace.UnavailableReason);
                else if (allocationOff.MaxBytes != 0L || allocationTrace.MaxBytes != 0L)
                    failure = new InvalidOperationException($"Standalone warmed GC Allocated In Frame was non-zero (off={allocationOff.MaxBytes} B, trace={allocationTrace.MaxBytes} B).");
                else
                {
                    result.steadyStateGcOff = allocationOff.Summary;
                    result.steadyStateGcTrace = allocationTrace.Summary;
                    result.allocationMetric = "GC.Alloc (current thread)";
                }
            }

            _frameTimingCollector = null;
            if (fullFrameTiming != null)
                fullFrameTiming.Dispose();
            _gcFrameCollector = null;
            _manualFrameDriver = null;
            if (traceRecording && bootstrap != null && bootstrap.Runtime != null && bootstrap.Runtime.IsInitialized && bootstrap.Runtime.AttemptTrace.IsRecording)
                bootstrap.Runtime.EndAttemptTrace();
            if (allocationOff != null)
                allocationOff.Dispose();
            if (allocationTrace != null)
                allocationTrace.Dispose();
            if (bootstrap != null)
                bootstrap.enabled = true;

            if (failure != null)
            {
                if (result == null)
                {
                    result = new SmokeResult
                    {
                        schema = "GAM9_STANDALONE_SMOKE_V1",
                        unityVersion = Application.unityVersion,
                        buildConfiguration = buildConfiguration,
                        screenWidth = Screen.width,
                        screenHeight = Screen.height,
                        refreshRateHz = GetRefreshRateHz(),
                        graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                        targetFrameRate = Application.targetFrameRate,
                        vSyncCount = QualitySettings.vSyncCount,
                        screenshotPath = screenshotPath ?? string.Empty
                    };
                }
                result.status = "FAIL";
                result.error = failure.ToString();
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

        private static float GetRefreshRateHz()
        {
            try
            {
                return (float)Screen.currentResolution.refreshRateRatio.value;
            }
            catch
            {
                return 0f;
            }
        }

        private static SmokeResult CreateEnvironmentFailure(string buildConfiguration, string message)
        {
            return new SmokeResult
            {
                schema = "GAM9_STANDALONE_SMOKE_V1",
                status = "FAIL",
                unityVersion = Application.unityVersion,
                buildConfiguration = buildConfiguration,
                screenWidth = Screen.width,
                screenHeight = Screen.height,
                refreshRateHz = GetRefreshRateHz(),
                graphicsApi = SystemInfo.graphicsDeviceType.ToString(),
                targetFrameRate = Application.targetFrameRate,
                vSyncCount = QualitySettings.vSyncCount,
                error = message
            };
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
        private struct FrameMetricSummary
        {
            public bool available;
            public int sampleCount;
            public double p50Ms;
            public double p95Ms;
            public double p99Ms;
            public double worstMs;
        }

        private sealed class FrameTimingCollector
        {
            private readonly FrameTiming[] _timings = new FrameTiming[1];
            private readonly double[] _cpuFrames;
            private readonly double[] _cpuMainThreadFrames;
            private readonly double[] _cpuRenderThreadFrames;
            private readonly double[] _cpuMainThreadPresentWaitFrames;
            private readonly double[] _gpuFrames;
            private readonly ProfilerCounter _drawCalls;
            private readonly ProfilerCounter _batches;
            private readonly ProfilerCounter _setPassCalls;
            private readonly ProfilerCounter _shadowCasters;
            private readonly ProfilerCounter _visibleSkinnedMeshes;
            private int _warmupFrames;
            private int _cpuCount;
            private int _cpuMainThreadCount;
            private int _cpuRenderThreadCount;
            private int _cpuMainThreadPresentWaitCount;
            private int _gpuCount;
            private bool _cpuAvailable;
            private bool _cpuMainThreadAvailable;
            private bool _cpuRenderThreadAvailable;
            private bool _cpuMainThreadPresentWaitAvailable;
            private bool _gpuAvailable;

            public FrameTimingCollector(int warmupFrames, int sampleCount)
            {
                _warmupFrames = warmupFrames;
                _cpuFrames = new double[sampleCount];
                _cpuMainThreadFrames = new double[sampleCount];
                _cpuRenderThreadFrames = new double[sampleCount];
                _cpuMainThreadPresentWaitFrames = new double[sampleCount];
                _gpuFrames = new double[sampleCount];
                _drawCalls = new ProfilerCounter("Draw Calls", sampleCount, "Draw Calls Count");
                _batches = new ProfilerCounter("Batches", sampleCount, "Batches Count");
                _setPassCalls = new ProfilerCounter("SetPass Calls", sampleCount, "SetPass Calls Count");
                _shadowCasters = new ProfilerCounter("Shadow Casters", sampleCount, "Shadow Casters Count");
                _visibleSkinnedMeshes = new ProfilerCounter("Visible Skinned Meshes", sampleCount, "Visible Skinned Meshes Count", "Visible Skinned Meshes");
                if (!FrameTimingManager.IsFeatureEnabled())
                {
                    Complete = true;
                    FailureReason = "UnityEngine.FrameTimingManager is unavailable in this standalone harness.";
                }
            }

            public bool Complete { get; private set; }
            public bool CpuAvailable => _cpuAvailable;
            public bool GpuAvailable => _gpuAvailable;
            public string FailureReason { get; private set; }
            public FrameMetricSummary CpuSummary => Summarize(_cpuFrames, _cpuCount, _cpuAvailable);
            public FrameMetricSummary CpuMainThreadSummary => Summarize(_cpuMainThreadFrames, _cpuMainThreadCount, _cpuMainThreadAvailable);
            public FrameMetricSummary CpuRenderThreadSummary => Summarize(_cpuRenderThreadFrames, _cpuRenderThreadCount, _cpuRenderThreadAvailable);
            public FrameMetricSummary CpuMainThreadPresentWaitSummary => Summarize(_cpuMainThreadPresentWaitFrames, _cpuMainThreadPresentWaitCount, _cpuMainThreadPresentWaitAvailable);
            public FrameMetricSummary GpuSummary => Summarize(_gpuFrames, _gpuCount, _gpuAvailable);
            public CounterMetricSummary DrawCallsSummary => _drawCalls.Summary;
            public CounterMetricSummary BatchesSummary => _batches.Summary;
            public CounterMetricSummary SetPassCallsSummary => _setPassCalls.Summary;
            public CounterMetricSummary ShadowCastersSummary => _shadowCasters.Summary;
            public CounterMetricSummary VisibleSkinnedMeshesSummary => _visibleSkinnedMeshes.Summary;
            public string CounterMeasurement =>
                "Unity.Profiling.ProfilerRecorder handles resolved from the release-player available counter set; each counter records its value per measured rendered frame. " +
                "drawCalls=" + _drawCalls.MatchedName + "; batches=" + _batches.MatchedName + "; setPassCalls=" + _setPassCalls.MatchedName + "; shadowCasters=" + _shadowCasters.MatchedName + "; visibleSkinnedMeshes=" + _visibleSkinnedMeshes.MatchedName + ".";

            public void Capture()
            {
                if (Complete)
                    return;

                FrameTimingManager.CaptureFrameTimings();
                uint timingCount = FrameTimingManager.GetLatestTimings(1, _timings);
                if (timingCount == 0)
                    return;

                if (_warmupFrames > 0)
                {
                    _warmupFrames--;
                    return;
                }

                FrameTiming timing = _timings[0];
                double cpuFrameTime = timing.cpuFrameTime;
                if (_cpuCount < _cpuFrames.Length && cpuFrameTime > 0d && double.IsFinite(cpuFrameTime))
                {
                    _cpuFrames[_cpuCount++] = cpuFrameTime;
                    double cpuMainThreadFrameTime = timing.cpuMainThreadFrameTime;
                    if (cpuMainThreadFrameTime > 0d && double.IsFinite(cpuMainThreadFrameTime))
                        _cpuMainThreadFrames[_cpuMainThreadCount++] = cpuMainThreadFrameTime;

                    double cpuRenderThreadFrameTime = timing.cpuRenderThreadFrameTime;
                    if (cpuRenderThreadFrameTime > 0d && double.IsFinite(cpuRenderThreadFrameTime))
                        _cpuRenderThreadFrames[_cpuRenderThreadCount++] = cpuRenderThreadFrameTime;

                    double cpuMainThreadPresentWaitTime = timing.cpuMainThreadPresentWaitTime;
                    if (cpuMainThreadPresentWaitTime >= 0d && double.IsFinite(cpuMainThreadPresentWaitTime))
                        _cpuMainThreadPresentWaitFrames[_cpuMainThreadPresentWaitCount++] = cpuMainThreadPresentWaitTime;

                    _drawCalls.Capture();
                    _batches.Capture();
                    _setPassCalls.Capture();
                    _shadowCasters.Capture();
                    _visibleSkinnedMeshes.Capture();
                }

                double gpuFrameTime = timing.gpuFrameTime;
                if (_gpuCount < _gpuFrames.Length && gpuFrameTime > 0d && double.IsFinite(gpuFrameTime))
                    _gpuFrames[_gpuCount++] = gpuFrameTime;

                if (_cpuCount == _cpuFrames.Length)
                {
                    _cpuAvailable = true;
                    _cpuMainThreadAvailable = _cpuMainThreadCount == _cpuMainThreadFrames.Length;
                    _cpuRenderThreadAvailable = _cpuRenderThreadCount == _cpuRenderThreadFrames.Length;
                    _cpuMainThreadPresentWaitAvailable = _cpuMainThreadPresentWaitCount == _cpuMainThreadPresentWaitFrames.Length;
                    _gpuAvailable = _gpuCount > 0;
                    Complete = true;
                }
            }

            public void MarkUnavailable(string reason)
            {
                if (Complete && _cpuAvailable)
                    return;
                Complete = true;
                _cpuAvailable = false;
                FailureReason = reason;
            }

            public void Dispose()
            {
                _drawCalls.Dispose();
                _batches.Dispose();
                _setPassCalls.Dispose();
                _shadowCasters.Dispose();
                _visibleSkinnedMeshes.Dispose();
            }

            private static FrameMetricSummary Summarize(double[] values, int count, bool available)
            {
                if (!available || count == 0)
                {
                    return new FrameMetricSummary
                    {
                        available = false,
                        sampleCount = 0,
                        p50Ms = -1d,
                        p95Ms = -1d,
                        p99Ms = -1d,
                        worstMs = -1d
                    };
                }

                var sorted = new double[count];
                Array.Copy(values, sorted, count);
                Array.Sort(sorted);
                return new FrameMetricSummary
                {
                    available = true,
                    sampleCount = count,
                    p50Ms = Percentile(sorted, 0.50d),
                    p95Ms = Percentile(sorted, 0.95d),
                    p99Ms = Percentile(sorted, 0.99d),
                    worstMs = sorted[count - 1]
                };
            }
        }

        [Serializable]
        private struct CounterMetricSummary
        {
            public bool available;
            public string counter;
            public string matchedName;
            public string unit;
            public int sampleCount;
            public long p50;
            public long p95;
            public long p99;
            public long worst;
            public string unavailableReason;
        }

        private sealed class ProfilerCounter
        {
            private readonly string _counter;
            private readonly long[] _samples;
            private ProfilerRecorder _recorder;
            private bool _started;
            private int _sampleCount;

            public ProfilerCounter(string counter, int sampleCount, params string[] candidateNames)
            {
                _counter = counter;
                _samples = new long[sampleCount];
                MatchedName = "UNAVAILABLE";
                Unit = string.Empty;
                try
                {
                    var handles = new System.Collections.Generic.List<ProfilerRecorderHandle>();
                    ProfilerRecorderHandle.GetAvailable(handles);
                    for (int handleIndex = 0; handleIndex < handles.Count && !_started; handleIndex++)
                    {
                        ProfilerRecorderHandle handle = handles[handleIndex];
                        ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
                        for (int nameIndex = 0; nameIndex < candidateNames.Length; nameIndex++)
                        {
                            if (!string.Equals(description.Name, candidateNames[nameIndex], StringComparison.OrdinalIgnoreCase))
                                continue;

                            _recorder = new ProfilerRecorder(handle, sampleCount > 128 ? 128 : sampleCount, ProfilerRecorderOptions.SumAllSamplesInFrame);
                            _recorder.Start();
                            _started = _recorder.Valid;
                            if (_started)
                            {
                                MatchedName = description.Name;
                                Unit = description.UnitType.ToString();
                            }
                            break;
                        }
                    }
                }
                catch (Exception exception)
                {
                    UnavailableReason = exception.Message;
                }

                if (!_started && string.IsNullOrEmpty(UnavailableReason))
                    UnavailableReason = "The requested ProfilerRecorder counter was not available in this player.";
            }

            public string MatchedName { get; }
            public string Unit { get; }
            public string UnavailableReason { get; private set; }

            public void Capture()
            {
                if (!_started || !_recorder.Valid || _recorder.Count == 0 || _sampleCount == _samples.Length)
                    return;
                _samples[_sampleCount++] = _recorder.LastValue;
            }

            public CounterMetricSummary Summary
            {
                get
                {
                    if (!_started || _sampleCount == 0)
                    {
                        return new CounterMetricSummary
                        {
                            available = false,
                            counter = _counter,
                            matchedName = MatchedName,
                            unit = Unit,
                            unavailableReason = UnavailableReason
                        };
                    }

                    var sorted = new long[_sampleCount];
                    Array.Copy(_samples, sorted, _sampleCount);
                    Array.Sort(sorted);
                    return new CounterMetricSummary
                    {
                        available = _sampleCount == _samples.Length,
                        counter = _counter,
                        matchedName = MatchedName,
                        unit = Unit,
                        sampleCount = _sampleCount,
                        p50 = Percentile(sorted, 0.50d),
                        p95 = Percentile(sorted, 0.95d),
                        p99 = Percentile(sorted, 0.99d),
                        worst = sorted[_sampleCount - 1],
                        unavailableReason = _sampleCount == _samples.Length ? string.Empty : "The counter did not return one sample for every measured frame."
                    };
                }
            }

            public void Dispose()
            {
                if (_started)
                {
                    _recorder.Dispose();
                    _started = false;
                }
            }
        }

        [Serializable]
        private struct AllocationMetricSummary
        {
            public bool available;
            public string channel;
            public int sampleCount;
            public long p50Bytes;
            public long p95Bytes;
            public long p99Bytes;
            public long maxBytes;
            public string unavailableReason;
        }

        private sealed class StandaloneAllocationCapture
        {
            private const string ChannelName = "GC.Alloc";
            private readonly long[] _samples;
            private int _warmupFrames;
            private int _emptyFrames;
            private int _sampleCount;
            private ProfilerRecorder _recorder;
            private bool _recorderStarted;

            public StandaloneAllocationCapture(int sampleCount, int warmupFrames)
            {
                _samples = new long[sampleCount];
                _warmupFrames = warmupFrames;
                try
                {
                    _recorder = ProfilerRecorder.StartNew(
                        ProfilerCategory.Internal,
                        ChannelName,
                        128,
                        ProfilerRecorderOptions.SumAllSamplesInFrame |
                        ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
                    _recorderStarted = _recorder.Valid;
                }
                catch (Exception exception)
                {
                    MarkUnavailable("ProfilerRecorder Internal/GC.Alloc unavailable: " + exception.Message);
                }

                if (!_recorderStarted && !Complete)
                    MarkUnavailable("ProfilerRecorder Internal/GC.Alloc returned an invalid recorder.");
            }

            public bool Complete { get; private set; }
            public bool Available { get; private set; }
            public string UnavailableReason { get; private set; }
            public long MaxBytes
            {
                get
                {
                    if (!Available || _sampleCount == 0)
                        return -1L;
                    long maximum = _samples[0];
                    for (int index = 1; index < _sampleCount; index++)
                    {
                        if (_samples[index] > maximum)
                            maximum = _samples[index];
                    }
                    return maximum;
                }
            }
            public AllocationMetricSummary Summary => BuildSummary();

            public void Capture()
            {
                if (Complete)
                    return;
                if (!_recorderStarted || !_recorder.Valid)
                {
                    MarkUnavailable("ProfilerRecorder Internal/GC.Alloc became invalid during collection.");
                    return;
                }
                if (_warmupFrames > 0)
                {
                    _warmupFrames--;
                    return;
                }
                if (_recorder.Count == 0)
                {
                    _emptyFrames++;
                    if (_emptyFrames > 60)
                        MarkUnavailable("ProfilerRecorder Internal/GC.Alloc returned no samples after warm-up.");
                    return;
                }

                _samples[_sampleCount++] = _recorder.LastValue;
                if (_sampleCount == _samples.Length)
                {
                    Available = true;
                    Complete = true;
                }
            }

            public void Dispose()
            {
                if (_recorderStarted)
                {
                    _recorder.Dispose();
                    _recorderStarted = false;
                }
            }

            private void MarkUnavailable(string reason)
            {
                Complete = true;
                Available = false;
                UnavailableReason = reason;
            }

            private AllocationMetricSummary BuildSummary()
            {
                if (!Available || _sampleCount == 0)
                {
                    return new AllocationMetricSummary
                    {
                        available = false,
                        channel = ChannelName,
                        sampleCount = 0,
                        p50Bytes = -1L,
                        p95Bytes = -1L,
                        p99Bytes = -1L,
                        maxBytes = -1L,
                        unavailableReason = UnavailableReason ?? "ProfilerRecorder channel unavailable."
                    };
                }

                var sorted = new long[_sampleCount];
                Array.Copy(_samples, sorted, _sampleCount);
                Array.Sort(sorted);
                return new AllocationMetricSummary
                {
                    available = true,
                    channel = ChannelName,
                    sampleCount = _sampleCount,
                    p50Bytes = Percentile(sorted, 0.50d),
                    p95Bytes = Percentile(sorted, 0.95d),
                    p99Bytes = Percentile(sorted, 0.99d),
                    maxBytes = sorted[_sampleCount - 1],
                    unavailableReason = string.Empty
                };
            }
        }

        private static long Percentile(long[] sorted, double percentile)
        {
            int index = (int)Math.Ceiling(sorted.Length * percentile) - 1;
            index = Math.Max(0, Math.Min(index, sorted.Length - 1));
            return sorted[index];
        }

        [Serializable]
        private sealed class SmokeResult
        {
            public string schema;
            public string status;
            public string unityVersion;
            public string buildConfiguration;
            public int screenWidth;
            public int screenHeight;
            public float refreshRateHz;
            public string graphicsApi;
            public int targetFrameRate;
            public int vSyncCount;
            public string scene;
            public int warmupFrameCount;
            public int measurementFrameCount;
            public string gameplayPerformanceProfile;
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
            public FrameMetricSummary foundationFrameStep;
            public FrameMetricSummary fullCpuFrame;
            public FrameMetricSummary cpuMainThreadFrame;
            public FrameMetricSummary cpuRenderThreadFrame;
            public FrameMetricSummary cpuMainThreadPresentWait;
            public FrameMetricSummary gpuFrame;
            public CounterMetricSummary drawCalls;
            public CounterMetricSummary batches;
            public CounterMetricSummary setPassCalls;
            public CounterMetricSummary shadowCasters;
            public CounterMetricSummary visibleSkinnedMeshes;
            public string fullCpuFrameMeasurement;
            public string gpuFrameMeasurement;
            public string counterMeasurement;
            public AllocationMetricSummary steadyStateGcOff;
            public AllocationMetricSummary steadyStateGcTrace;
            public string allocationMetric;
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
