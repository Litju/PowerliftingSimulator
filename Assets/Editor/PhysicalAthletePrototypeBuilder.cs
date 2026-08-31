using System;
using System.IO;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class PhysicalAthletePrototypeBuilder
{
    public const string ScenePath = "Assets/Scenes/Prototype/PhysicalAthletePhysics.unity";
    private const string ModelPath = "Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx";
    private const string AthleteMaterialPath = "Assets/Characters/Athlete/Materials/CanonicalAthlete.mat";
    private const string FloorMaterialPath = "Assets/Characters/Athlete/Materials/CalibrationFloor.mat";
    private const string ProxyMaterialPath = "Assets/Characters/Athlete/Materials/PhysicalProxyDebug.mat";
    private const float ImportedModelLiftMeters = 0.10173738f;

    [MenuItem("Powerlifting Simulator/GAM-6/Build Physical Athlete Prototype")]
    public static void BuildPhysicalAthletePrototype()
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath)
            ?? throw new InvalidOperationException($"Canonical model is missing at {ModelPath}.");
        Material athleteMaterial = AssetDatabase.LoadAssetAtPath<Material>(AthleteMaterialPath)
            ?? throw new InvalidOperationException($"Canonical athlete material is missing at {AthleteMaterialPath}.");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject root = new GameObject("PhysicalAthletePrototype_GAM6");
        FoundationBootstrap foundation = root.AddComponent<FoundationBootstrap>();
        AthleteRigOwnership ownership = root.AddComponent<AthleteRigOwnership>();

        Animator reference = CreateRig(model, athleteMaterial, scene, root.transform, "ReferenceRig_GAM6", false);
        Animator visible = CreateRig(model, athleteMaterial, scene, root.transform, "VisibleRig_GAM6", true);
        ownership.ConfigureForCalibration(reference, visible.transform);

        PhysicalAthleteRig physicalRig = root.AddComponent<PhysicalAthleteRig>();
        physicalRig.Configure(foundation, ownership, reference, visible, CreateProxyMaterial());

        CreateVisualFloor();
        CreateLighting();
        CreateCamera();

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes/Prototype");
        EditorSceneManager.SaveScene(scene, ScenePath);
        AddReviewSceneToBuildSettings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"GAM-6 physical-athlete prototype built at {ScenePath}. Press Play for PASSIVE_RAGDOLL.");
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

    private static Animator CreateRig(GameObject model, Material material, Scene scene, Transform parent, string name, bool visible)
    {
        GameObject correctionRoot = new GameObject(name + "_CanonicalFrame");
        correctionRoot.transform.SetParent(parent, false);
        correctionRoot.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
        instance.name = name;
        instance.transform.SetParent(correctionRoot.transform, false);
        instance.transform.SetLocalPositionAndRotation(Vector3.up * ImportedModelLiftMeters, Quaternion.identity);
        instance.transform.localScale = Vector3.one;

        Animator animator = instance.GetComponentInChildren<Animator>(true)
            ?? throw new InvalidOperationException($"{name} contains no Animator.");
        if (animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new InvalidOperationException($"{name} does not have the canonical valid Humanoid Avatar.");
        animator.applyRootMotion = false;
        animator.runtimeAnimatorController = null;
        animator.enabled = false;

        foreach (SkinnedMeshRenderer renderer in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] materials = new Material[Math.Max(1, renderer.sharedMaterials.Length)];
            Array.Fill(materials, material);
            renderer.sharedMaterials = materials;
            renderer.enabled = visible;
        }
        return animator;
    }

    private static Material CreateProxyMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(ProxyMaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, ProxyMaterialPath);
        }

        Color color = new Color(0.05f, 0.85f, 1f, 0.42f);
        material.SetColor("_BaseColor", color);
        material.SetColor("_EmissionColor", color * 0.35f);
        material.EnableKeyword("_EMISSION");
        material.SetFloat("_Surface", 1f);
        material.SetFloat("_Blend", 0f);
        material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetFloat("_ZWrite", 0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static void CreateVisualFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "VisualPlatform_1UnitEquals1Meter";
        floor.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        Material material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        if (material != null)
            floor.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void CreateLighting()
    {
        GameObject keyObject = new GameObject("Key Light");
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 2.1f;
        key.color = new Color(1f, 0.93f, 0.84f);
        key.shadows = LightShadows.Soft;
        keyObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        GameObject fillObject = new GameObject("Fill Light");
        Light fill = fillObject.AddComponent<Light>();
        fill.type = LightType.Directional;
        fill.intensity = 0.85f;
        fill.color = new Color(0.62f, 0.76f, 1f);
        fillObject.transform.rotation = Quaternion.Euler(25f, 145f, 0f);

        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.3f, 0.36f, 0.44f);
        RenderSettings.ambientEquatorColor = new Color(0.18f, 0.2f, 0.24f);
        RenderSettings.ambientGroundColor = new Color(0.08f, 0.09f, 0.11f);
    }

    private static void CreateCamera()
    {
        GameObject cameraObject = new GameObject("Physical Athlete Review Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.07f, 0.095f);
        camera.fieldOfView = 35f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        camera.transform.position = new Vector3(2.7f, 1.35f, 3.8f);
        camera.transform.LookAt(new Vector3(0f, 0.95f, 0f), Vector3.up);
    }
}
