using System;
using System.Collections.Generic;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Bench.Unity;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// GAM-70: derives the deadlift scene from the accepted squat scene - same
/// canonical avatar, input asset, foundation, lights and camera - and
/// replaces only the lift-local composition. The squat and bench scenes and
/// their builders are not modified.
/// </summary>
public static class GAM70DeadliftSceneBuilder
{
    public const string SquatScenePath = "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity";
    public const string WindowsBuildDir = "Builds/GAM70/Windows";
    public const string WindowsExecutablePath = WindowsBuildDir + "/PowerliftingSimulator.exe";

    private static string[] PlayerScenes => new[]
    {
        DeadliftStationController.ScenePath, SquatScenePath, BenchPressPrototypeController.ScenePath
    };

    [MenuItem("Powerlifting Simulator/GAM-70/1. Build Deadlift Scene")]
    public static void BuildDeadliftScene()
    {
        string path = DeadliftStationController.ScenePath;
        if (File.Exists(path))
            AssetDatabase.DeleteAsset(path);
        if (!AssetDatabase.CopyAsset(SquatScenePath, path))
            throw new InvalidOperationException($"Could not copy {SquatScenePath} to {path}.");
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);

        SquatPhysicalPrototypeController squat = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>()
            ?? throw new InvalidOperationException("The squat scene has no squat controller to replace.");
        FoundationBootstrap foundation = UnityEngine.Object.FindFirstObjectByType<FoundationBootstrap>()
            ?? throw new InvalidOperationException("Missing FoundationBootstrap.");
        PhysicalAthleteRig rig = UnityEngine.Object.FindFirstObjectByType<PhysicalAthleteRig>()
            ?? throw new InvalidOperationException("Missing PhysicalAthleteRig.");
        PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>()
            ?? throw new InvalidOperationException("Missing PhysicalBarbell.");

        GameObject host = squat.gameObject;
        host.name = "DeadliftStation_GAM70";
        UnityEngine.Object.DestroyImmediate(squat);
        DeadliftStationController station = host.AddComponent<DeadliftStationController>();
        var so = new SerializedObject(station);
        so.FindProperty("foundation").objectReferenceValue = foundation;
        so.FindProperty("athleteRig").objectReferenceValue = rig;
        so.FindProperty("barbell").objectReferenceValue = barbell;
        so.ApplyModifiedPropertiesWithoutUndo();

        foreach (PowerliftingGameUiController squatUi in UnityEngine.Object.FindObjectsByType<PowerliftingGameUiController>(FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(squatUi.gameObject);
        foreach (PowerliftingCameraDirector director in UnityEngine.Object.FindObjectsByType<PowerliftingCameraDirector>(FindObjectsSortMode.None))
            UnityEngine.Object.DestroyImmediate(director);
        Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>()
            ?? throw new InvalidOperationException("Missing camera.");
        if (camera.GetComponent<AudioListener>() == null)
            camera.gameObject.AddComponent<AudioListener>();

        PowerliftingGymEnvironment gym = UnityEngine.Object.FindFirstObjectByType<PowerliftingGymEnvironment>();
        if (gym != null && gym.GetComponent<DeadliftGymStaging>() == null)
            gym.gameObject.AddComponent<DeadliftGymStaging>();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, path);
        EnsureBuildSettings();
        AssetDatabase.SaveAssets();
        Debug.Log($"GAM-70: built {path} from {SquatScenePath}.");
    }

    public static void EnsureBuildSettings()
    {
        var scenes = new List<EditorBuildSettingsScene>();
        foreach (string p in PlayerScenes)
            scenes.Add(new EditorBuildSettingsScene(p, true));
        foreach (EditorBuildSettingsScene entry in EditorBuildSettings.scenes)
            if (Array.IndexOf(PlayerScenes, entry.path) < 0)
                scenes.Add(entry);
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    /// <summary>Builds the Windows player from the committed scenes (deadlift first; squat and bench kept).</summary>
    [MenuItem("Powerlifting Simulator/GAM-70/2. Build Windows Player From Committed Scenes")]
    public static void BuildWindowsPlayerFromCommittedScenes()
    {
        foreach (string scene in PlayerScenes)
            if (!File.Exists(scene))
                throw new InvalidOperationException("Missing committed scene " + scene);
        string exe = Path.GetFullPath(WindowsExecutablePath);
        Directory.CreateDirectory(Path.GetDirectoryName(exe));
        BuildReport report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = PlayerScenes,
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        });
        if (report.summary.result != BuildResult.Succeeded || !File.Exists(exe))
            throw new InvalidOperationException($"GAM-70 Windows build failed: {report.summary.result}, errors={report.summary.totalErrors}");
        Debug.Log($"GAM-70 Windows build succeeded: {exe} ({new FileInfo(exe).Length} bytes, {report.summary.totalTime.TotalSeconds:F1}s, warnings={report.summary.totalWarnings}).");
    }
}
