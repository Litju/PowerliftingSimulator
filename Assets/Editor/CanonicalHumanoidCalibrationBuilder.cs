using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PowerliftingSimulator.Athlete;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class CanonicalHumanoidCalibrationBuilder
{
    private const string ModelPath = "Assets/Characters/Athlete/Source/Superhero_Male_FullBody.fbx";
    private const string AlbedoPath = "Assets/Characters/Athlete/Textures/T_Superhero_Male_Ligh.png";
    private const string NormalPath = "Assets/Characters/Athlete/Textures/T_Superhero_Male_Normal.png";
    private const string MaterialPath = "Assets/Characters/Athlete/Materials/CanonicalAthlete.mat";
    private const string FloorMaterialPath = "Assets/Characters/Athlete/Materials/CalibrationFloor.mat";
    private const string SkeletonMaterialPath = "Assets/Characters/Athlete/Materials/SkeletonDebug.mat";
    private const string ScenePath = "Assets/Scenes/Prototype/PhysicalAthleteCalibration.unity";
    private const string MeasurementPath = "Artifacts/Measurements/GAM-5-canonical-humanoid.json";
    private const string EvidenceDirectory = "Artifacts/Evidence/GAM-5";
    private const string CalibrationPoseId = "QUATERNIUS_SUPERHERO_MALE_IMPORT_BIND_POSE_V1";

    private static readonly HumanBodyBones[] RequiredBones =
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.RightFoot
    };

    private static readonly HumanBodyBones[] OptionalBones =
    {
        HumanBodyBones.UpperChest,
        HumanBodyBones.Neck,
        HumanBodyBones.LeftToes,
        HumanBodyBones.RightToes,
        HumanBodyBones.LeftShoulder,
        HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftMiddleProximal,
        HumanBodyBones.RightMiddleProximal
    };

    private static readonly BoneConnection[] DebugConnections =
    {
        new(HumanBodyBones.Hips, HumanBodyBones.Spine),
        new(HumanBodyBones.Spine, HumanBodyBones.Chest),
        new(HumanBodyBones.Chest, HumanBodyBones.UpperChest),
        new(HumanBodyBones.UpperChest, HumanBodyBones.Neck),
        new(HumanBodyBones.Neck, HumanBodyBones.Head),
        new(HumanBodyBones.Chest, HumanBodyBones.LeftShoulder),
        new(HumanBodyBones.LeftShoulder, HumanBodyBones.LeftUpperArm),
        new(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
        new(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
        new(HumanBodyBones.Chest, HumanBodyBones.RightShoulder),
        new(HumanBodyBones.RightShoulder, HumanBodyBones.RightUpperArm),
        new(HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
        new(HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
        new(HumanBodyBones.Hips, HumanBodyBones.LeftUpperLeg),
        new(HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
        new(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
        new(HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
        new(HumanBodyBones.Hips, HumanBodyBones.RightUpperLeg),
        new(HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
        new(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
        new(HumanBodyBones.RightFoot, HumanBodyBones.RightToes)
    };

    [MenuItem("Powerlifting Simulator/GAM-5/Build Calibration Scene and Evidence")]
    public static void BuildCalibrationSceneAndEvidence()
    {
        ConfigureImporters();
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new InvalidOperationException($"Canonical model is missing at {ModelPath}.");

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        GameObject calibrationRoot = new("CanonicalAthleteCalibration");
        calibrationRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));

        GameObject athlete = (GameObject)PrefabUtility.InstantiatePrefab(model, scene);
        athlete.name = "ReferenceVisibleRig_GAM5";
        athlete.transform.SetParent(calibrationRoot.transform, false);
        athlete.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        athlete.transform.localScale = Vector3.one;

        Animator animator = athlete.GetComponentInChildren<Animator>(true);
        ValidateAnimator(animator);
        animator.applyRootMotion = false;

        Material athleteMaterial = CreateAthleteMaterial();
        foreach (SkinnedMeshRenderer renderer in athlete.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            Material[] materials = new Material[Math.Max(1, renderer.sharedMaterials.Length)];
            Array.Fill(materials, athleteMaterial);
            renderer.sharedMaterials = materials;
        }

        Bounds bounds = CalculateBounds(athlete);
        athlete.transform.localPosition = Vector3.up * -bounds.min.y;
        bounds = CalculateBounds(athlete);

        AthleteRigOwnership ownership = calibrationRoot.AddComponent<AthleteRigOwnership>();
        ownership.ConfigureForCalibration(animator, athlete.transform);
        HumanoidSkeletonDebug skeletonDebug = calibrationRoot.AddComponent<HumanoidSkeletonDebug>();
        skeletonDebug.Configure(animator);
        GameObject skeletonOverlay = CreateSkeletonOverlay(animator);

        CreateFloor();
        CreateLighting();
        Camera camera = CreateCamera(bounds);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath) ?? "Assets/Scenes/Prototype");
        EditorSceneManager.SaveScene(scene, ScenePath);
        WriteMeasurements(calibrationRoot, athlete, animator, bounds);
        CaptureEvidence(camera, bounds, skeletonOverlay);
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"GAM-5 calibration complete. Scene={ScenePath}; AvatarValid={animator.avatar.isValid}; " +
            $"AvatarHuman={animator.avatar.isHuman}; RenderedHeight_m={bounds.size.y.ToString("F6", CultureInfo.InvariantCulture)}");
    }

    private static void ConfigureImporters()
    {
        ModelImporter modelImporter = AssetImporter.GetAtPath(ModelPath) as ModelImporter
            ?? throw new InvalidOperationException($"No ModelImporter found for {ModelPath}.");
        modelImporter.globalScale = 1f;
        modelImporter.useFileScale = true;
        modelImporter.bakeAxisConversion = false;
        modelImporter.importAnimation = false;
        modelImporter.importCameras = false;
        modelImporter.importLights = false;
        modelImporter.materialImportMode = ModelImporterMaterialImportMode.None;
        modelImporter.animationType = ModelImporterAnimationType.Human;
        modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        modelImporter.SaveAndReimport();

        TextureImporter normalImporter = AssetImporter.GetAtPath(NormalPath) as TextureImporter
            ?? throw new InvalidOperationException($"No TextureImporter found for {NormalPath}.");
        normalImporter.textureType = TextureImporterType.NormalMap;
        normalImporter.sRGBTexture = false;
        normalImporter.SaveAndReimport();
    }

    private static Material CreateAthleteMaterial()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath) ?? "Assets/Characters/Athlete/Materials");
        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? throw new InvalidOperationException("URP Lit shader is unavailable.");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Smoothness", 0.28f);
        material.EnableKeyword("_NORMALMAP");
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateFloorMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(FloorMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(material, FloorMaterialPath);
        }

        material.SetColor("_BaseColor", new Color(0.17f, 0.2f, 0.24f));
        material.SetFloat("_Smoothness", 0.12f);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Material CreateSkeletonMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(SkeletonMaterialPath);
        if (material == null)
        {
            material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            AssetDatabase.CreateAsset(material, SkeletonMaterialPath);
        }

        Color color = new(0.05f, 0.95f, 1f, 1f);
        material.SetColor("_BaseColor", color);
        material.SetColor("_Color", color);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        material.renderQueue = 4000;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject CreateSkeletonOverlay(Animator animator)
    {
        GameObject overlay = new("SkeletonBoneOverlay");
        Material material = CreateSkeletonMaterial();
        foreach (BoneConnection connection in DebugConnections)
        {
            Transform start = animator.GetBoneTransform(connection.Start);
            Transform end = animator.GetBoneTransform(connection.End);
            if (start == null || end == null)
                continue;

            GameObject lineObject = new($"{connection.Start}-{connection.End}");
            lineObject.transform.SetParent(overlay.transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.positionCount = 2;
            line.SetPosition(0, overlay.transform.InverseTransformPoint(start.position));
            line.SetPosition(1, overlay.transform.InverseTransformPoint(end.position));
            line.startWidth = 0.012f;
            line.endWidth = 0.012f;
            line.sharedMaterial = material;
            line.startColor = material.color;
            line.endColor = material.color;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
        }
        return overlay;
    }

    private static void CreateFloor()
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
        floor.name = "CalibrationFloor_1UnitEquals1Meter";
        floor.transform.localScale = new Vector3(0.5f, 1f, 0.5f);
        UnityEngine.Object.DestroyImmediate(floor.GetComponent<Collider>());
        floor.GetComponent<MeshRenderer>().sharedMaterial = CreateFloorMaterial();
    }

    private static void CreateLighting()
    {
        GameObject keyObject = new("Key Light");
        Light key = keyObject.AddComponent<Light>();
        key.type = LightType.Directional;
        key.intensity = 2.2f;
        key.color = new Color(1f, 0.92f, 0.82f);
        key.shadows = LightShadows.Soft;
        keyObject.transform.rotation = Quaternion.Euler(45f, -35f, 0f);

        GameObject fillObject = new("Fill Light");
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

    private static Camera CreateCamera(Bounds bounds)
    {
        GameObject cameraObject = new("Inspection Camera");
        cameraObject.tag = "MainCamera";
        Camera camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.055f, 0.07f, 0.095f);
        camera.fieldOfView = 34f;
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 50f;
        PositionCamera(camera, bounds, new Vector3(0.34f, 0.04f, 1f));
        return camera;
    }

    private static void PositionCamera(Camera camera, Bounds bounds, Vector3 direction)
    {
        float distance = bounds.size.y / (2f * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.22f;
        Vector3 target = bounds.center + Vector3.up * bounds.size.y * 0.015f;
        camera.transform.position = target + direction.normalized * distance;
        camera.transform.LookAt(target, Vector3.up);
    }

    private static void CaptureEvidence(Camera camera, Bounds bounds, GameObject skeletonOverlay)
    {
        Directory.CreateDirectory(EvidenceDirectory);
        skeletonOverlay.SetActive(false);
        PositionCamera(camera, bounds, new Vector3(0.34f, 0.04f, 1f));
        Render(camera, Path.Combine(EvidenceDirectory, "GAM-5-full-body-front-three-quarter.png"));

        PositionCamera(camera, bounds, Vector3.right);
        Render(camera, Path.Combine(EvidenceDirectory, "GAM-5-full-body-side.png"));

        skeletonOverlay.SetActive(true);
        skeletonOverlay.transform.position = Vector3.forward * 0.18f;
        PositionCamera(camera, bounds, new Vector3(0.12f, 0.03f, 1f));
        Render(camera, Path.Combine(EvidenceDirectory, "GAM-5-skeleton-overlay.png"));
        skeletonOverlay.transform.position = Vector3.zero;
    }

    private static void Render(Camera camera, string path)
    {
        const int width = 1920;
        const int height = 1080;
        RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
        Texture2D image = new(width, height, TextureFormat.RGB24, false);
        RenderTexture previous = RenderTexture.active;
        camera.targetTexture = renderTexture;
        camera.Render();
        RenderTexture.active = renderTexture;
        image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        image.Apply();
        File.WriteAllBytes(path, image.EncodeToPNG());
        camera.targetTexture = null;
        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(renderTexture);
        UnityEngine.Object.DestroyImmediate(image);
    }

    private static void WriteMeasurements(GameObject calibrationRoot, GameObject athlete, Animator animator, Bounds bounds)
    {
        var boneRecords = RequiredBones.Concat(OptionalBones)
            .Distinct()
            .Select(bone => CreateBoneRecord(bone, animator, athlete.transform))
            .Where(record => record != null)
            .ToArray();

        var artifact = new MeasurementArtifact
        {
            mission = "POWERLIFTING_SIMULATOR_GAM_5_CANONICAL_HUMANOID",
            asset = new AssetIdentity
            {
                provider = "Quaternius",
                pack = "Universal Base Characters[Standard]",
                packRelease = "August 2025",
                sourceUrl = "https://quaternius.com/packs/universalbasecharacters.html",
                archiveFilename = "Universal Base Characters[Standard].zip",
                archiveSha256 = "fdbf1804c90dfc1ea03e992bff7da2dfd1a79318e13270a660180f9308455f40",
                modelPath = ModelPath,
                modelSha256 = Sha256(ModelPath)
            },
            unityVersion = Application.unityVersion,
            modelImporter = CreateImporterRecord(),
            calibrationPose = new CalibrationPose
            {
                id = CalibrationPoseId,
                rootPosition_m = VectorRecord.From(calibrationRoot.transform.position),
                rootRotation_xyzw = QuaternionRecord.From(calibrationRoot.transform.rotation),
                importedModelLocalPosition_m = VectorRecord.From(athlete.transform.localPosition),
                importedModelLocalRotation_xyzw = QuaternionRecord.From(athlete.transform.localRotation),
                upAxis = "+Y",
                forwardAxis = "+Z",
                poseSource = "Imported FBX bind/reference pose; no animation controller and no root motion"
            },
            avatar = new AvatarRecord
            {
                valid = animator.avatar.isValid,
                human = animator.avatar.isHuman,
                name = animator.avatar.name
            },
            boneMap = boneRecords,
            measurements = CreateMeasurements(animator, bounds),
            method = "Unity Humanoid bone-transform world-position distances in the imported bind pose; rendered height is the combined SkinnedMeshRenderer world bounds. Distances are SI meters because 1 Unity unit = 1 m.",
            scientificClaimCeiling = "Bone-pivot distances and rendered bounds are direct asset/rig measurements. Segment labels are engineering proxies, not anatomical joint centers, biological segment lengths, COM, mass, inertia, muscle, or clinical anatomy."
        };

        Directory.CreateDirectory(Path.GetDirectoryName(MeasurementPath) ?? "Artifacts/Measurements");
        File.WriteAllText(MeasurementPath, JsonUtility.ToJson(artifact, true) + Environment.NewLine);
    }

    private static ImporterRecord CreateImporterRecord()
    {
        ModelImporter importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        return new ImporterRecord
        {
            globalScale = importer.globalScale,
            useFileScale = importer.useFileScale,
            bakeAxisConversion = importer.bakeAxisConversion,
            animationType = importer.animationType.ToString(),
            avatarSetup = importer.avatarSetup.ToString(),
            importAnimation = importer.importAnimation,
            materialImportMode = importer.materialImportMode.ToString()
        };
    }

    private static BoneRecord CreateBoneRecord(HumanBodyBones logicalBone, Animator animator, Transform root)
    {
        Transform bone = animator.GetBoneTransform(logicalBone);
        if (bone == null)
            return null;

        return new BoneRecord
        {
            logicalName = logicalBone.ToString(),
            humanBodyBones = logicalBone.ToString(),
            transformName = bone.name,
            hierarchyPath = AnimationUtility.CalculateTransformPath(bone, root),
            parent = bone.parent != null ? bone.parent.name : null,
            worldPosition_m = VectorRecord.From(bone.position),
            localRotation_xyzw = QuaternionRecord.From(bone.localRotation),
            worldRotation_xyzw = QuaternionRecord.From(bone.rotation),
            sourceClass = "ASSET_DIRECT_MEASUREMENT"
        };
    }

    private static MeasurementRecord[] CreateMeasurements(Animator animator, Bounds bounds)
    {
        var records = new List<MeasurementRecord>
        {
            Measurement("rendered_stature_proxy", bounds.size.y, "Combined SkinnedMeshRenderer world-bounds height", "ASSET_DIRECT_MEASUREMENT"),
            Distance("hip_width_proxy", animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg),
            Distance("left_thigh_bone_pivot_distance", animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg),
            Distance("right_thigh_bone_pivot_distance", animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg),
            Distance("left_shank_bone_pivot_distance", animator, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
            Distance("right_shank_bone_pivot_distance", animator, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
            Distance("left_foot_bone_pivot_proxy", animator, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes),
            Distance("right_foot_bone_pivot_proxy", animator, HumanBodyBones.RightFoot, HumanBodyBones.RightToes),
            Distance("left_upper_arm_bone_pivot_distance", animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm),
            Distance("right_upper_arm_bone_pivot_distance", animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm),
            Distance("left_forearm_bone_pivot_distance", animator, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand),
            Distance("right_forearm_bone_pivot_distance", animator, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand),
            Distance("left_hand_skeletal_proxy", animator, HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal),
            Distance("right_hand_skeletal_proxy", animator, HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal),
            Distance("spine_to_chest_bone_pivot_distance", animator, HumanBodyBones.Spine, HumanBodyBones.Chest),
            Distance("chest_to_upper_chest_bone_pivot_distance", animator, HumanBodyBones.Chest, HumanBodyBones.UpperChest),
            Distance("upper_chest_to_neck_bone_pivot_distance", animator, HumanBodyBones.UpperChest, HumanBodyBones.Neck),
            Distance("neck_to_head_bone_pivot_distance", animator, HumanBodyBones.Neck, HumanBodyBones.Head),
            Distance("shoulder_width_proxy", animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm),
            Distance("stance_foot_center_separation", animator, HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot)
        };
        return records.Where(record => record != null).ToArray();
    }

    private static MeasurementRecord Distance(string id, Animator animator, HumanBodyBones start, HumanBodyBones end)
    {
        Transform startTransform = animator.GetBoneTransform(start);
        Transform endTransform = animator.GetBoneTransform(end);
        if (startTransform == null || endTransform == null)
            return null;

        return Measurement(id, Vector3.Distance(startTransform.position, endTransform.position),
            $"Euclidean world-position distance between {start} and {end} bone pivots", "ENGINEERING_DERIVED");
    }

    private static MeasurementRecord Measurement(string id, float value, string method, string sourceClass) => new()
    {
        id = id,
        value_m = value,
        method = method,
        sourceClass = sourceClass
    };

    private static void ValidateAnimator(Animator animator)
    {
        if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
            throw new InvalidOperationException("Canonical FBX did not import with a valid Unity Humanoid Avatar.");

        foreach (HumanBodyBones bone in RequiredBones)
        {
            if (animator.GetBoneTransform(bone) == null)
                throw new InvalidOperationException($"Required humanoid bone {bone} did not resolve.");
        }
    }

    private static Bounds CalculateBounds(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            throw new InvalidOperationException("Canonical athlete contains no renderer.");

        Bounds bounds = renderers[0].bounds;
        for (int index = 1; index < renderers.Length; index++)
            bounds.Encapsulate(renderers[index].bounds);
        return bounds;
    }

    private static string Sha256(string assetPath)
    {
        using SHA256 algorithm = SHA256.Create();
        using FileStream stream = File.OpenRead(Path.GetFullPath(assetPath));
        return string.Concat(algorithm.ComputeHash(stream).Select(value => value.ToString("x2")));
    }

    [Serializable]
    private sealed class MeasurementArtifact
    {
        public string mission;
        public AssetIdentity asset;
        public string unityVersion;
        public ImporterRecord modelImporter;
        public CalibrationPose calibrationPose;
        public AvatarRecord avatar;
        public BoneRecord[] boneMap;
        public MeasurementRecord[] measurements;
        public string method;
        public string scientificClaimCeiling;
    }

    [Serializable]
    private sealed class AssetIdentity
    {
        public string provider;
        public string pack;
        public string packRelease;
        public string sourceUrl;
        public string archiveFilename;
        public string archiveSha256;
        public string modelPath;
        public string modelSha256;
    }

    [Serializable]
    private sealed class ImporterRecord
    {
        public float globalScale;
        public bool useFileScale;
        public bool bakeAxisConversion;
        public string animationType;
        public string avatarSetup;
        public bool importAnimation;
        public string materialImportMode;
    }

    [Serializable]
    private sealed class CalibrationPose
    {
        public string id;
        public VectorRecord rootPosition_m;
        public QuaternionRecord rootRotation_xyzw;
        public VectorRecord importedModelLocalPosition_m;
        public QuaternionRecord importedModelLocalRotation_xyzw;
        public string upAxis;
        public string forwardAxis;
        public string poseSource;
    }

    [Serializable]
    private sealed class AvatarRecord
    {
        public bool valid;
        public bool human;
        public string name;
    }

    [Serializable]
    private sealed class BoneRecord
    {
        public string logicalName;
        public string humanBodyBones;
        public string transformName;
        public string hierarchyPath;
        public string parent;
        public VectorRecord worldPosition_m;
        public QuaternionRecord localRotation_xyzw;
        public QuaternionRecord worldRotation_xyzw;
        public string sourceClass;
    }

    [Serializable]
    private sealed class MeasurementRecord
    {
        public string id;
        public float value_m;
        public string method;
        public string sourceClass;
    }

    [Serializable]
    private struct VectorRecord
    {
        public float x;
        public float y;
        public float z;

        public static VectorRecord From(Vector3 value) => new() { x = value.x, y = value.y, z = value.z };
    }

    [Serializable]
    private struct QuaternionRecord
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public static QuaternionRecord From(Quaternion value) => new()
        {
            x = value.x,
            y = value.y,
            z = value.z,
            w = value.w
        };
    }

    private readonly struct BoneConnection
    {
        public BoneConnection(HumanBodyBones start, HumanBodyBones end)
        {
            Start = start;
            End = end;
        }

        public HumanBodyBones Start { get; }

        public HumanBodyBones End { get; }
    }
}
