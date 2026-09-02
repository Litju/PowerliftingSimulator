using System;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SquatReferencePreviewBuilder
{
    public const string ScenePath = "Assets/Scenes/Prototype/SquatReferencePreview.unity";

    private const string ModelPath = SquatReferencePreview.AssetPath;
    private const string AthleteMaterialPath = "Assets/Characters/Athlete/Materials/CanonicalAthlete.mat";
    private const string FloorMaterialPath = "Assets/Characters/Athlete/Materials/CalibrationFloor.mat";
    private const string GhostMaterialPath = "Assets/Characters/Athlete/Materials/SquatReferenceGhost.mat";
    private const float ImportedModelLiftMeters = 0.10173738f;

    [MenuItem("Powerlifting Simulator/GAM-10/Build Squat Reference Preview")]
    public static void BuildSquatReferencePreview()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
            ?? throw new InvalidOperationException($"Canonical model is missing at {ModelPath}.");
        Material athleteMaterial = AssetDatabase.LoadAssetAtPath<Material>(AthleteMaterialPath)
            ?? throw new InvalidOperationException($"Canonical athlete material is missing at {AthleteMaterialPath}.");
        Material ghostMaterial = CreateGhostMaterial();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject preview = new GameObject("SquatReferencePreview_GAM10");
        GameObject frame = new GameObject("ReferencePreviewRig_GAM10_CanonicalFrame");
        frame.transform.SetParent(preview.transform, false);
        frame.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
        instance.name = "ReferencePreviewRig_GAM10";
        instance.transform.SetParent(frame.transform, false);
        instance.transform.SetLocalPositionAndRotation(Vector3.up * ImportedModelLiftMeters, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        Animator animator = instance.GetComponentInChildren<Animator>(true)
            ?? throw new InvalidOperationException("The canonical model contains no Animator.");
        if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new InvalidOperationException("The canonical model does not have a valid Humanoid Avatar.");
        animator.enabled = false;
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] materials = new Material[Math.Max(1, renderer.sharedMaterials.Length)];
            Array.Fill(materials, athleteMaterial);
            renderer.sharedMaterials = materials;
            renderer.enabled = true;
        }

        AthleteRigOwnership ownership = preview.AddComponent<AthleteRigOwnership>();
        ownership.ConfigureForCalibration(animator, instance.transform);
        SquatReferencePreview referencePreview = preview.AddComponent<SquatReferencePreview>();
        referencePreview.Configure(frame.transform, animator, ownership, ghostMaterial, ghostMaterial);

        CreateFloor();
        CreateLighting();
        CreateCamera();
        AddReviewSceneToBuildSettings();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes/Prototype");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"GAM-10 squat reference preview built at {ScenePath}. Profile={SquatReferencePreview.ProfileId}; " +
            "reference-only hierarchy; physical rig and physical bar are absent.");
    }

    private static void AddReviewSceneToBuildSettings()
    {
        EditorBuildSettingsScene[] current = EditorBuildSettings.scenes;
        foreach (EditorBuildSettingsScene entry in current)
        {
            if (string.Equals(entry.path, ScenePath, StringComparison.Ordinal))
                return;
        }

        var updated = new EditorBuildSettingsScene[current.Length + 1];
        Array.Copy(current, updated, current.Length);
        updated[current.Length] = new EditorBuildSettingsScene(ScenePath, true);
        EditorBuildSettings.scenes = updated;
    }

    private static Material CreateGhostMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(GhostMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit")
                ?? throw new InvalidOperationException("URP Unlit shader is unavailable.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, GhostMaterialPath);
        }

        Color color = new Color(1f, 0.25f, 0.72f, 1f);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        material.renderQueue = 3000;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "ReferencePreviewFloor_1UnitEquals1Meter";
        floor.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        Material floorMaterial = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        if (floorMaterial != null)
            floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
    }

    private static void CreateLighting()
    {
        GameObject keyObject = new GameObject("Reference Preview Key Light");
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 2.2f;
        key.color = new Color(1f, 0.92f, 0.82f);
        key.shadows = LightShadows.Soft;
        keyObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        GameObject fillObject = new GameObject("Reference Preview Fill Light");
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.9f;
        fill.color = new Color(0.62f, 0.76f, 1f);
        fillObject.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.3f, 0.36f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.18f, 0.2f, 0.24f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.11f);
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Squat Reference Preview Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.07f, 0.095f);
        camera.fieldOfView = 34f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        camera.transform.position = new Vector3(2.7f, 1.04f, 3.85f);
        camera.transform.LookAt(new Vector3(0f, 0.95f, 0f), Vector3.up);
    }
}
