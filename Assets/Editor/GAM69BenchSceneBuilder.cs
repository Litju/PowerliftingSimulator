using System;
using System.Collections.Generic;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GAM-69: derives the bench scene from the accepted squat scene - same
/// canonical avatar instances, input asset, foundation, lights and camera -
/// and replaces only the lift-local composition. The squat scene and its
/// builder are not modified.
/// </summary>
public static class GAM69BenchSceneBuilder
{
    public const string SquatScenePath = "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity";
    public const string WindowsBuildDir = "Builds/GAM69/Windows";
    public const string WindowsExecutablePath = WindowsBuildDir + "/PowerliftingSimulator.exe";

    [MenuItem("Powerlifting Simulator/GAM-69/1. Build Bench Press Scene")]
    public static void BuildBenchScene()
    {
        string benchPath = BenchPressPrototypeController.ScenePath;
        if (File.Exists(benchPath))
            AssetDatabase.DeleteAsset(benchPath);
        if (!AssetDatabase.CopyAsset(SquatScenePath, benchPath))
            throw new InvalidOperationException($"Could not copy {SquatScenePath} to {benchPath}.");

        Scene scene = EditorSceneManager.OpenScene(benchPath, OpenSceneMode.Single);

        SquatPhysicalPrototypeController squat = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>()
            ?? throw new InvalidOperationException("The squat scene has no squat controller to replace.");
        FoundationBootstrap foundation = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>()
            ?? throw new InvalidOperationException("Missing FoundationBootstrap.");
        PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>()
            ?? throw new InvalidOperationException("Missing PhysicalAthleteRig.");
        PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>()
            ?? throw new InvalidOperationException("Missing PhysicalBarbell.");

        GameObject host = squat.gameObject;
        host.name = "BenchPressPrototype_GAM69";
        UnityEngine.Object.DestroyImmediate(squat);
        BenchPressPrototypeController bench = host.AddComponent<BenchPressPrototypeController>();
        var so = new SerializedObject(bench);
        so.FindProperty("foundation").objectReferenceValue = foundation;
        so.FindProperty("athleteRig").objectReferenceValue = rig;
        so.FindProperty("barbell").objectReferenceValue = barbell;
        so.ApplyModifiedPropertiesWithoutUndo();

        // Squat presentation shell components are replaced by bench ones.
        foreach (PowerliftingGameUiController squatUi in UnityEngine.Object.FindObjectsByType<PowerliftingGameUiController>(FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(squatUi.gameObject);
        foreach (PowerliftingCameraDirector director in UnityEngine.Object.FindObjectsByType<PowerliftingCameraDirector>(FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(director);

        Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>()
            ?? throw new InvalidOperationException("Missing camera.");
        if (camera.GetComponent<AudioListener>() == null)
            camera.gameObject.AddComponent<AudioListener>();
        camera.gameObject.AddComponent<BenchCameraDirector>();

        GameObject ui = new GameObject("BenchPressGameUI_GAM69");
        ui.AddComponent<PowerliftingSettingsManager>();
        ui.AddComponent<BenchAudioCues>();
        ui.AddComponent<BenchGameUiController>();

        // The gym stays decorative. Its bench bay is staged at the origin and
        // its decorative bench hidden; the authoritative bench is built at
        // runtime by the bench controller.
        PowerliftingGymEnvironment gym = UnityEngine.Object.FindFirstObjectByType<PowerliftingGymEnvironment>();
        if (gym != null && gym.GetComponent<BenchGymStaging>() == null)
            gym.gameObject.AddComponent<BenchGymStaging>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, benchPath);
        EnsureBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log($"GAM-69: built {benchPath} from {SquatScenePath}.");
    }

    public static void EnsureBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(SquatScenePath, true),
            new EditorBuildSettingsScene(BenchPressPrototypeController.ScenePath, true)
        };
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
        {
            if (entry.path != SquatScenePath && entry.path != BenchPressPrototypeController.ScenePath)
                scenes.Add(entry);
        }
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    [MenuItem("Powerlifting Simulator/GAM-69/2. Build Windows Player (Squat + Bench)")]
    public static void BuildWindowsPlayer()
    {
        BuildBenchScene();
        string exe = Path.GetFullPath(WindowsExecutablePath);
        Directory.CreateDirectory(Path.GetDirectoryName(exe));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { SquatScenePath, BenchPressPrototypeController.ScenePath },
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded || !File.Exists(exe))
            throw new InvalidOperationException($"GAM-69 Windows build failed: {report.summary.result}, errors={report.summary.totalErrors}");
        Debug.Log($"GAM-69 Windows build succeeded: {exe} ({new FileInfo(exe).Length} bytes, {report.summary.totalTime.TotalSeconds:F1}s, warnings={report.summary.totalWarnings}).");
    }
}
