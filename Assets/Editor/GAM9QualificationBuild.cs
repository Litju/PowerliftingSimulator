#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PowerliftingSimulator.Qualification
{
    public static class GAM9QualificationBuild
    {
        private const string QualificationScene = "Assets/Scenes/Prototype/PhysicalAthletePhysics.unity";
        private const string AllocationDevelopmentExecutable = "Builds/GAM9/Windows/PowerliftingSimulator-GAM9-AllocDev.exe";
        private const string PerformanceReleaseExecutable = "Builds/GAM9/Windows/PowerliftingSimulator-GAM9-PerfRelease.exe";

        // Kept as a compatibility entry point for existing local qualification scripts.
        public static void BuildWindowsSmoke()
        {
            BuildWindows(BuildConfiguration.AllocationDevelopment);
        }

        public static void BuildWindowsAllocationDev()
        {
            BuildWindows(BuildConfiguration.AllocationDevelopment);
        }

        public static void BuildWindowsPerformanceRelease()
        {
            BuildWindows(BuildConfiguration.PerformanceRelease);
        }

        private static void BuildWindows(BuildConfiguration configuration)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string relativeExecutable = configuration == BuildConfiguration.AllocationDevelopment
                ? AllocationDevelopmentExecutable
                : PerformanceReleaseExecutable;
            string outputPath = Path.Combine(projectRoot, relativeExecutable.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            PlayerSettings.enableFrameTimingStats = true;

            var options = new BuildPlayerOptions
            {
                scenes = new[] { QualificationScene },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.DetailedBuildReport |
                    (configuration == BuildConfiguration.AllocationDevelopment ? BuildOptions.Development : BuildOptions.None)
            };

            string buildConfiguration = configuration == BuildConfiguration.AllocationDevelopment
                ? "GAM9_ALLOC_DEV"
                : "GAM9_PERF_RELEASE";
            Debug.Log($"GAM9_BUILD configuration={buildConfiguration} scene={QualificationScene} target={options.target} development={configuration == BuildConfiguration.AllocationDevelopment} scriptingBackend={PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone)}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"GAM9_BUILD configuration={buildConfiguration} result={summary.result} totalTime={summary.totalTime} totalSize={summary.totalSize} totalErrors={summary.totalErrors} totalWarnings={summary.totalWarnings}");

            WarningAuditArtifact warningAudit = BuildWarningAudit(report);
            warningAudit.buildConfiguration = buildConfiguration;
            warningAudit.executable = relativeExecutable;
            string warningAuditFileName = configuration == BuildConfiguration.AllocationDevelopment
                ? "GAM-9-build-warning-audit-alloc-dev.json"
                : "GAM-9-build-warning-audit.json";
            string warningAuditPath = Path.Combine(projectRoot, "Artifacts", "Measurements", warningAuditFileName);
            Directory.CreateDirectory(Path.GetDirectoryName(warningAuditPath));
            File.WriteAllText(warningAuditPath, JsonUtility.ToJson(warningAudit, true));

            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"GAM9 Windows qualification build failed with result {summary.result} and {summary.totalErrors} errors.");
            if (!string.Equals(warningAudit.status, "PASS", StringComparison.Ordinal))
                throw new InvalidOperationException($"GAM9 Windows build warning audit did not pass: {warningAudit.status}.");
        }

        private enum BuildConfiguration
        {
            AllocationDevelopment,
            PerformanceRelease
        }

        private static WarningAuditArtifact BuildWarningAudit(BuildReport report)
        {
            BuildSummary summary = report.summary;
            var messageCounts = new Dictionary<string, WarningMessageAccumulator>(StringComparer.Ordinal);
            var classCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int observedWarnings = 0;

            BuildStep[] steps = report.steps;
            if (steps != null)
            {
                for (int stepIndex = 0; stepIndex < steps.Length; stepIndex++)
                {
                    BuildStepMessage[] messages = steps[stepIndex].messages;
                    if (messages == null)
                        continue;

                    for (int messageIndex = 0; messageIndex < messages.Length; messageIndex++)
                    {
                        BuildStepMessage message = messages[messageIndex];
                        if (message.type != LogType.Warning)
                            continue;

                        observedWarnings++;
                        string content = message.content ?? string.Empty;
                        string classification = ClassifyWarning(content);
                        if (messageCounts.TryGetValue(content, out WarningMessageAccumulator accumulator))
                            accumulator.count++;
                        else
                            messageCounts.Add(content, new WarningMessageAccumulator(classification));

                        if (classCounts.TryGetValue(classification, out int classCount))
                            classCounts[classification] = classCount + 1;
                        else
                            classCounts.Add(classification, 1);
                    }
                }
            }

            var messageArtifacts = new List<WarningMessageArtifact>(messageCounts.Count);
            foreach (KeyValuePair<string, WarningMessageAccumulator> pair in messageCounts)
            {
                messageArtifacts.Add(new WarningMessageArtifact
                {
                    classification = pair.Value.classification,
                    count = pair.Value.count,
                    message = pair.Key
                });
            }
            messageArtifacts.Sort((first, second) =>
                string.CompareOrdinal(first.classification + first.message, second.classification + second.message));

            var classArtifacts = new List<WarningClassArtifact>(classCounts.Count);
            foreach (KeyValuePair<string, int> pair in classCounts)
            {
                var examples = new List<string>();
                for (int index = 0; index < messageArtifacts.Count && examples.Count < 3; index++)
                {
                    if (string.Equals(messageArtifacts[index].classification, pair.Key, StringComparison.Ordinal))
                        examples.Add(messageArtifacts[index].message);
                }

                int distinctMessages = 0;
                for (int index = 0; index < messageArtifacts.Count; index++)
                {
                    if (string.Equals(messageArtifacts[index].classification, pair.Key, StringComparison.Ordinal))
                        distinctMessages++;
                }

                classArtifacts.Add(new WarningClassArtifact
                {
                    classification = pair.Key,
                    count = pair.Value,
                    distinctMessages = distinctMessages,
                    evidence = ClassificationEvidence(pair.Key),
                    examples = examples.ToArray()
                });
            }
            classArtifacts.Sort((first, second) => string.CompareOrdinal(first.classification, second.classification));

            int blockingWarnings = classCounts.TryGetValue("BLOCKING_RUNTIME_OR_ASSET_WARNING", out int blocking) ? blocking : 0;
            int unclassifiedWarnings = classCounts.TryGetValue("UNCLASSIFIED_WARNING", out int unclassified) ? unclassified : 0;
            bool reportComplete = observedWarnings == summary.totalWarnings;
            string status = reportComplete && blockingWarnings == 0 && unclassifiedWarnings == 0
                ? "PASS"
                : reportComplete ? "BLOCKED" : "BLOCKED_REPORT_MISMATCH";

            return new WarningAuditArtifact
            {
                schema = "GAM9_BUILD_WARNING_AUDIT_V1",
                status = status,
                totalWarnings = summary.totalWarnings,
                observedWarningMessages = observedWarnings,
                distinctWarningMessages = messageArtifacts.Count,
                blockingWarningCount = blockingWarnings,
                unclassifiedWarningCount = unclassifiedWarnings,
                classificationBasis = "BuildReport.steps[].messages[] with LogType.Warning; missing script/assembly, scene-load, serialization, physics-initialization, exception/assertion, and invalid player-configuration markers are blocking. Shader/toolchain markers are non-blocking only with the recorded message evidence.",
                classes = classArtifacts.ToArray(),
                messages = messageArtifacts.ToArray()
            };
        }

        private static string ClassifyWarning(string content)
        {
            string normalized = content.ToLowerInvariant();
            if (ContainsAny(normalized,
                "missing script",
                "missing assembly",
                "script is missing",
                "failed to load scene",
                "unable to load scene",
                "scene could not be loaded",
                "scene couldn't be loaded",
                "asset serialization",
                "serialization error",
                "failed to serialize",
                "physics initialization",
                "physx initialization",
                "assertion failed",
                "assert failed",
                "exception",
                "invalid player configuration"))
                return "BLOCKING_RUNTIME_OR_ASSET_WARNING";

            if (ContainsAny(normalized,
                "shader",
                "shadergraph",
                "shader graph",
                "shader variant",
                "shader compilation",
                "hlsl",
                "d3d11",
                "dx11",
                "toolchain",
                "fallback shader",
                "burst could not load",
                "code integrity policy",
                "smart app control",
                "wdac",
                "no runtimepipelinemanager components found in build scenes"))
                return "NON_BLOCKING_TOOLCHAIN_WARNING";

            return "UNCLASSIFIED_WARNING";
        }

        private static bool ContainsAny(string value, params string[] markers)
        {
            for (int index = 0; index < markers.Length; index++)
            {
                if (value.IndexOf(markers[index], StringComparison.Ordinal) >= 0)
                    return true;
            }
            return false;
        }

        private static string ClassificationEvidence(string classification)
        {
            if (string.Equals(classification, "NON_BLOCKING_TOOLCHAIN_WARNING", StringComparison.Ordinal))
                return "The message matched an approved shader/Burst/toolchain or known Unity pipeline marker; this build completed successfully and the message did not match a blocking runtime, asset, scene, physics, exception, assertion, or player-configuration marker.";
            if (string.Equals(classification, "BLOCKING_RUNTIME_OR_ASSET_WARNING", StringComparison.Ordinal))
                return "Blocking marker found; this warning must be reviewed before G1 qualification can pass.";
            return "No approved warning class marker matched; qualification is blocked until reviewed.";
        }

        [Serializable]
        private sealed class WarningAuditArtifact
        {
            public string schema;
            public string status;
            public string buildConfiguration;
            public string executable;
            public int totalWarnings;
            public int observedWarningMessages;
            public int distinctWarningMessages;
            public int blockingWarningCount;
            public int unclassifiedWarningCount;
            public string classificationBasis;
            public WarningClassArtifact[] classes;
            public WarningMessageArtifact[] messages;
        }

        [Serializable]
        private sealed class WarningClassArtifact
        {
            public string classification;
            public int count;
            public int distinctMessages;
            public string evidence;
            public string[] examples;
        }

        [Serializable]
        private sealed class WarningMessageArtifact
        {
            public string classification;
            public int count;
            public string message;
        }

        private sealed class WarningMessageAccumulator
        {
            public WarningMessageAccumulator(string classification)
            {
                this.classification = classification;
                count = 1;
            }

            public readonly string classification;
            public int count;
        }
    }
}
#endif
