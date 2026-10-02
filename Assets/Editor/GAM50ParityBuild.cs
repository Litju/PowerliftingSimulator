using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>GAM-50 Editor/standalone parity player (release, Windows x64, production squat scene).</summary>
public static class GAM50ParityBuild
{
    private const string Scene = "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity";
    private const string Executable = "Builds/GAM50/Windows/PowerliftingSimulator-GAM50-Parity.exe";

    public static void BuildWindowsParity()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string output = Path.Combine(projectRoot, Executable.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(output));
        // The project ships IL2CPP; machines without the IL2CPP module build
        // the parity player on Mono (GAM50_PARITY_BACKEND=Mono). The project
        // setting is restored after the build.
        NamedBuildTarget standalone = NamedBuildTarget.Standalone;
        ScriptingImplementation original = PlayerSettings.GetScriptingBackend(standalone);
        string requested = System.Environment.GetEnvironmentVariable("GAM50_PARITY_BACKEND");
        if (requested == "Mono")
            PlayerSettings.SetScriptingBackend(standalone, ScriptingImplementation.Mono2x);
        BuildReport report;
        try
        {
            report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                locationPathName = output,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            });
        }
        finally
        {
            PlayerSettings.SetScriptingBackend(standalone, original);
        }
        Debug.Log($"GAM50_PARITY_BUILD result={report.summary.result} backend={(requested == "Mono" ? "Mono2x" : original.ToString())} output={output}");
        if (report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
