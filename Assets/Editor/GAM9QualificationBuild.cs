#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PowerliftingSimulator.Qualification
{
    public static class GAM9QualificationBuild
    {
        private const string QualificationScene = "Assets/Scenes/Prototype/PhysicalAthletePhysics.unity";
        private const string QualificationExecutable = "Builds/GAM9/Windows/PowerliftingSimulator-GAM9.exe";

        public static void BuildWindowsSmoke()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string outputPath = Path.Combine(projectRoot, QualificationExecutable.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            var options = new BuildPlayerOptions
            {
                scenes = new[] { QualificationScene },
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            Debug.Log($"GAM9_BUILD scene={QualificationScene} target={options.target} scriptingBackend={PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone)}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            Debug.Log($"GAM9_BUILD result={summary.result} totalTime={summary.totalTime} totalSize={summary.totalSize} totalErrors={summary.totalErrors} totalWarnings={summary.totalWarnings}");

            if (summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"GAM9 Windows qualification build failed with result {summary.result} and {summary.totalErrors} errors.");
        }
    }
}
#endif
