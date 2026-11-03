using System;
using System.IO;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Squat.Unity;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class GAM65DemoGameBuilder
{
    public const string ScenePath = "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity";
    public const string MaterialsDir = "Assets/Environment/Gym/Materials";
    public const string PrefabsDir = "Assets/Environment/Gym/Prefabs";
    public const string GymPrefabPath = PrefabsDir + "/PowerliftingGymEnvironment_GAM65.prefab";
    public const string UiPrefabPath = PrefabsDir + "/PowerliftingGameUI_GAM65.prefab";
    public const string WindowsBuildDir = "Builds/GAM65/Windows";
    public const string WindowsExecutablePath = WindowsBuildDir + "/PowerliftingSimulator.exe";

    [MenuItem("Powerlifting Simulator/GAM-65/1. Build Demo Gym Assets & Update Squat Scene")]
    public static void BuildDemoGymAssetsAndScene()
    {
        Directory.CreateDirectory(MaterialsDir);
        Directory.CreateDirectory(PrefabsDir);

        CreateOrUpdateMaterialAsset(MaterialsDir + "/GymRubberFloor.mat", new Color(0.095f, 0.105f, 0.12f), 0.08f, 0.26f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/PlatformRubberDrop.mat", new Color(0.14f, 0.15f, 0.175f), 0.08f, 0.30f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/CompetitionOakDeck.mat", new Color(0.58f, 0.41f, 0.24f), 0.04f, 0.58f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/RackDarkSteel.mat", new Color(0.11f, 0.13f, 0.16f), 0.72f, 0.52f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/CompetitionCrimsonSteel.mat", new Color(0.68f, 0.11f, 0.14f), 0.55f, 0.58f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/CompetitionChromeBar.mat", new Color(0.78f, 0.81f, 0.85f), 0.90f, 0.80f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/GymWallConcrete.mat", new Color(0.13f, 0.145f, 0.17f), 0.10f, 0.25f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/GymWallPanel.mat", new Color(0.08f, 0.095f, 0.115f), 0.30f, 0.40f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/CompetitionPadVinyl.mat", new Color(0.55f, 0.09f, 0.11f), 0.12f, 0.44f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/SafetyLineGold.mat", new Color(0.84f, 0.64f, 0.18f), 0.35f, 0.50f, false, Color.black);
        CreateOrUpdateMaterialAsset(MaterialsDir + "/HighBayLed.mat", new Color(0.95f, 0.97f, 1.0f), 0.10f, 0.70f, true, new Color(2.4f, 2.5f, 2.7f));

        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

        SquatPhysicalPrototypeController squatController = UnityEngine.Object.FindFirstObjectByType<SquatPhysicalPrototypeController>()
            ?? throw new InvalidOperationException($"Missing SquatPhysicalPrototypeController in {ScenePath}.");
        PhysicalBarbell barbell = UnityEngine.Object.FindFirstObjectByType<PhysicalBarbell>()
            ?? throw new InvalidOperationException($"Missing PhysicalBarbell in {ScenePath}.");
        Camera mainCamera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>()
            ?? throw new InvalidOperationException($"Missing MainCamera in {ScenePath}.");

        // Ensure Camera has PowerliftingCameraDirector and AudioListener
        if (mainCamera.GetComponent<AudioListener>() == null)
            mainCamera.gameObject.AddComponent<AudioListener>();
        PowerliftingCameraDirector cameraDirector = mainCamera.GetComponent<PowerliftingCameraDirector>();
        if (cameraDirector == null)
            cameraDirector = mainCamera.gameObject.AddComponent<PowerliftingCameraDirector>();

        // Ensure 3D Gym Environment is authored in the scene and saved as a reusable prefab
        PowerliftingGymEnvironment existingGym = UnityEngine.Object.FindFirstObjectByType<PowerliftingGymEnvironment>();
        if (existingGym != null)
            UnityEngine.Object.DestroyImmediate(existingGym.gameObject);

        GameObject gymRoot = new GameObject("PowerliftingGymEnvironment_GAM65");
        PowerliftingGymEnvironment gymEnv = gymRoot.AddComponent<PowerliftingGymEnvironment>();
        gymEnv.Bind(squatController, barbell);
        PrefabUtility.SaveAsPrefabAssetAndConnect(gymRoot, GymPrefabPath, InteractionMode.AutomatedAction);

        // Ensure Game UI & Settings & Audio Systems are authored in the scene and saved as a prefab
        PowerliftingGameUiController existingUi = UnityEngine.Object.FindFirstObjectByType<PowerliftingGameUiController>();
        if (existingUi != null)
            UnityEngine.Object.DestroyImmediate(existingUi.gameObject);

        GameObject uiRoot = new GameObject("PowerliftingGameUI_GAM65");
        uiRoot.AddComponent<PowerliftingSettingsManager>();
        uiRoot.AddComponent<PowerliftingAudioDirector>();
        uiRoot.AddComponent<PowerliftingGameUiController>();
        PrefabUtility.SaveAsPrefabAssetAndConnect(uiRoot, UiPrefabPath, InteractionMode.AutomatedAction);

        // Ensure Scene 0 in Build Settings is SquatPhysicalPrototype.unity
        EnsureBuildSettingsSceneZero();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"GAM-65: Built 3D gym environment, prefabs, materials, camera director, audio director, and uGUI in {ScenePath}.");
    }

    [MenuItem("Powerlifting Simulator/GAM-65/2. Build Windows Standalone Executable")]
    public static void BuildWindowsStandalonePlayer()
    {
        BuildDemoGymAssetsAndScene();

        string fullOutputDir = Path.GetFullPath(WindowsBuildDir);
        Directory.CreateDirectory(fullOutputDir);
        string fullExePath = Path.GetFullPath(WindowsExecutablePath);

        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = fullExePath,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;
        if (summary.result != BuildResult.Succeeded || !File.Exists(fullExePath))
        {
            throw new InvalidOperationException(
                $"GAM-65 Windows standalone build failed: result={summary.result}, totalErrors={summary.totalErrors}, output={fullExePath}");
        }

        FileInfo exeInfo = new FileInfo(fullExePath);
        Debug.Log($"GAM-65 Windows standalone build succeeded: {fullExePath} ({exeInfo.Length} bytes, {summary.totalTime.TotalSeconds:F1}s).");
    }

    private static void EnsureBuildSettingsSceneZero()
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
        var updated = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new EditorBuildSettingsScene(ScenePath, true)
        };
        foreach (EditorBuildSettingsScene entry in current)
        {
            if (!string.Equals(entry.path, ScenePath, StringComparison.Ordinal))
                updated.Add(entry);
        }
        EditorBuildSettings.scenes = updated.ToArray();
    }

    private static Material CreateOrUpdateMaterialAsset(
        string assetPath,
        Color baseColor,
        float metallic,
        float smoothness,
        bool emissive,
        Color emissionColor)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (mat == null)
        {
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, assetPath);
        }
        else
        {
            mat.shader = shader;
        }

        mat.color = baseColor;
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", baseColor);
        if (mat.HasProperty("_Metallic"))
            mat.SetFloat("_Metallic", metallic);
        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", smoothness);
        if (mat.HasProperty("_Glossiness"))
            mat.SetFloat("_Glossiness", smoothness);

        if (emissive)
        {
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", emissionColor);
        }

        EditorUtility.SetDirty(mat);
        return mat;
    }
}
