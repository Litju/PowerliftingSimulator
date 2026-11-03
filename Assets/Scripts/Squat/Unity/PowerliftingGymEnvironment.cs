using System.Collections.Generic;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Squat;
using UnityEngine;
using UnityEngine.Rendering;

namespace PowerliftingSimulator.Squat.Unity
{
    [DisallowMultipleComponent]
    public sealed class PowerliftingGymEnvironment : MonoBehaviour
    {
        [SerializeField] private SquatPhysicalPrototypeController squatController;
        [SerializeField] private PhysicalBarbell authoritativeBarbell;
        [SerializeField] private Transform squatStationRoot;
        [SerializeField] private Transform benchStationRoot;
        [SerializeField] private Transform deadliftStationRoot;
        [SerializeField] private Renderer[] refereeLampRenderers;

        private readonly List<Material> _runtimeMaterials = new List<Material>();
        private Material _lampIdleMat;
        private Material _lampWhiteMat;
        private Material _lampRedMat;
        private bool _isBuilt;

        public Transform SquatStationRoot => squatStationRoot;
        public Transform BenchStationRoot => benchStationRoot;
        public Transform DeadliftStationRoot => deadliftStationRoot;
        public bool IsBuilt => _isBuilt;

        public static PowerliftingGymEnvironment EnsureInScene(
            SquatPhysicalPrototypeController controller,
            PhysicalBarbell barbell)
        {
            PowerliftingGymEnvironment existing = FindFirstObjectByType<PowerliftingGymEnvironment>();
            if (existing != null)
            {
                existing.Bind(controller, barbell);
                existing.EnsureEnvironmentBuilt();
                return existing;
            }

            GameObject root = new GameObject("PowerliftingGymEnvironment_GAM65");
            PowerliftingGymEnvironment env = root.AddComponent<PowerliftingGymEnvironment>();
            env.Bind(controller, barbell);
            env.EnsureEnvironmentBuilt();
            return env;
        }

        public void Bind(SquatPhysicalPrototypeController controller, PhysicalBarbell barbell)
        {
            if (controller != null)
                squatController = controller;
            if (barbell != null)
                authoritativeBarbell = barbell;
        }

        private void Awake()
        {
            if (squatController == null)
                squatController = FindFirstObjectByType<SquatPhysicalPrototypeController>();
            if (authoritativeBarbell == null)
                authoritativeBarbell = FindFirstObjectByType<PhysicalBarbell>();

            EnsureEnvironmentBuilt();
        }

        private void Update()
        {
            UpdateRefereeLamps();
        }

        private void OnDestroy()
        {
            for (int i = 0; i < _runtimeMaterials.Count; i++)
            {
                Material mat = _runtimeMaterials[i];
                if (mat == null)
                    continue;
                if (Application.isPlaying)
                    Destroy(mat);
                else
                    DestroyImmediate(mat);
            }
            _runtimeMaterials.Clear();
        }

        public void EnsureEnvironmentBuilt()
        {
            if (_isBuilt && _runtimeMaterials.Count > 0 && squatStationRoot != null && benchStationRoot != null && deadliftStationRoot != null)
            {
                StripAllEnvironmentalColliders();
                ConfigureArenaLighting();
                return;
            }

            BuildCompleteGymHierarchy();
            StripAllEnvironmentalColliders();
            ConfigureArenaLighting();
            _isBuilt = true;
        }

        public void BuildCompleteGymHierarchy()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                GameObject child = transform.GetChild(i).gameObject;
                if (Application.isPlaying)
                    Destroy(child);
                else
                    DestroyImmediate(child);
            }

            GameObject legacyFloor = GameObject.Find("VisualPlatform_1UnitEquals1Meter");
            if (legacyFloor != null)
            {
                MeshRenderer legacyRenderer = legacyFloor.GetComponent<MeshRenderer>();
                if (legacyRenderer != null)
                    legacyRenderer.enabled = false;
            }

            Material matRubberFloor = CreateLitMaterial("GymRubberFloor", new Color(0.095f, 0.105f, 0.12f), 0.08f, 0.26f);
            Material matRubberDrop = CreateLitMaterial("PlatformRubberDrop", new Color(0.14f, 0.15f, 0.175f), 0.08f, 0.30f);
            Material matOakDeck = CreateLitMaterial("CompetitionOakDeck", new Color(0.58f, 0.41f, 0.24f), 0.04f, 0.58f);
            Material matDarkSteel = CreateLitMaterial("RackDarkSteel", new Color(0.11f, 0.13f, 0.16f), 0.72f, 0.52f);
            Material matCrimsonSteel = CreateLitMaterial("CompetitionCrimsonSteel", new Color(0.68f, 0.11f, 0.14f), 0.55f, 0.58f);
            Material matChromeBar = CreateLitMaterial("CompetitionChromeBar", new Color(0.78f, 0.81f, 0.85f), 0.90f, 0.80f);
            Material matWallConcrete = CreateLitMaterial("GymWallConcrete", new Color(0.13f, 0.145f, 0.17f), 0.10f, 0.25f);
            Material matWallPanel = CreateLitMaterial("GymWallPanel", new Color(0.08f, 0.095f, 0.115f), 0.30f, 0.40f);
            Material matBenchPad = CreateLitMaterial("CompetitionPadVinyl", new Color(0.55f, 0.09f, 0.11f), 0.12f, 0.44f);
            Material matChalkWhite = CreateLitMaterial("MagnesiumChalk", new Color(0.92f, 0.93f, 0.95f), 0.02f, 0.12f);
            Material matGoldAccent = CreateLitMaterial("SafetyLineGold", new Color(0.84f, 0.64f, 0.18f), 0.35f, 0.50f);
            Material matHighBayLed = CreateEmissiveMaterial("HighBayLed", new Color(0.95f, 0.97f, 1.0f), new Color(2.4f, 2.5f, 2.7f));

            Material matPlateRed25 = CreateLitMaterial("Plate25Red", new Color(0.78f, 0.12f, 0.14f), 0.35f, 0.52f);
            Material matPlateBlue20 = CreateLitMaterial("Plate20Blue", new Color(0.11f, 0.32f, 0.74f), 0.35f, 0.52f);
            Material matPlateYellow15 = CreateLitMaterial("Plate15Yellow", new Color(0.86f, 0.68f, 0.10f), 0.35f, 0.52f);
            Material matPlateGreen10 = CreateLitMaterial("Plate10Green", new Color(0.12f, 0.56f, 0.24f), 0.35f, 0.52f);

            _lampIdleMat = CreateEmissiveMaterial("RefereeLampIdle", new Color(0.32f, 0.35f, 0.40f), new Color(0.15f, 0.18f, 0.22f));
            _lampWhiteMat = CreateEmissiveMaterial("RefereeLampGood", new Color(0.98f, 0.98f, 1.0f), new Color(2.8f, 2.8f, 3.0f));
            _lampRedMat = CreateEmissiveMaterial("RefereeLampNoLift", new Color(0.95f, 0.14f, 0.16f), new Color(2.8f, 0.20f, 0.22f));

            // 1. Gym Architecture
            Transform archRoot = CreateGroup(transform, "GymArchitecture", Vector3.zero);
            CreateBox(archRoot, "MainGymRubberFloor", new Vector3(0f, -0.032f, 0f), new Vector3(22f, 0.06f, 16f), matRubberFloor);
            CreateBox(archRoot, "RearWall", new Vector3(0f, 2.35f, -5.2f), new Vector3(22f, 4.8f, 0.25f), matWallConcrete);
            CreateBox(archRoot, "FrontWall", new Vector3(0f, 2.35f, 7.8f), new Vector3(22f, 4.8f, 0.25f), matWallConcrete);
            CreateBox(archRoot, "LeftWall", new Vector3(-9.5f, 2.35f, 1.3f), new Vector3(0.25f, 4.8f, 13.2f), matWallConcrete);
            CreateBox(archRoot, "RightWall", new Vector3(9.5f, 2.35f, 1.3f), new Vector3(0.25f, 4.8f, 13.2f), matWallConcrete);
            CreateBox(archRoot, "CeilingDeck", new Vector3(0f, 4.75f, 1.3f), new Vector3(22f, 0.18f, 13.2f), matWallPanel);

            // Rear wall architectural wainscoting and station zone backdrops
            CreateBox(archRoot, "RearWallWainscot", new Vector3(0f, 0.95f, -5.04f), new Vector3(19.2f, 1.9f, 0.04f), matWallPanel);
            CreateBox(archRoot, "RearWallCrimsonStripe", new Vector3(0f, 1.92f, -5.02f), new Vector3(19.2f, 0.07f, 0.05f), matCrimsonSteel);

            CreateStationWallBanner(archRoot, "CenterSquatBackdrop", new Vector3(0f, 3.15f, -5.02f), matWallPanel, matCrimsonSteel, matGoldAccent);
            CreateStationWallBanner(archRoot, "LeftBenchBackdrop", new Vector3(-4.15f, 3.15f, -5.02f), matWallPanel, matDarkSteel, matGoldAccent);
            CreateStationWallBanner(archRoot, "RightDeadliftBackdrop", new Vector3(4.15f, 3.15f, -5.02f), matWallPanel, matDarkSteel, matGoldAccent);

            // Structural columns & ceiling trusses
            float[] columnX = { -7.2f, -2.25f, 2.25f, 7.2f };
            foreach (float cx in columnX)
            {
                CreateBox(archRoot, $"RearColumn_{cx:F1}", new Vector3(cx, 2.35f, -4.92f), new Vector3(0.28f, 4.7f, 0.28f), matDarkSteel);
                CreateBox(archRoot, $"RearColumnStripe_{cx:F1}", new Vector3(cx, 1.92f, -4.77f), new Vector3(0.30f, 0.09f, 0.04f), matCrimsonSteel);
            }

            float[] trussZ = { -3.2f, -0.4f, 2.4f, 5.2f };
            foreach (float tz in trussZ)
            {
                CreateBox(archRoot, $"CeilingTruss_{tz:F1}", new Vector3(0f, 4.42f, tz), new Vector3(18.6f, 0.16f, 0.18f), matDarkSteel);
                CreateHighBayFixture(archRoot, $"HighBayLeft_{tz:F1}", new Vector3(-4.15f, 4.30f, tz), matDarkSteel, matHighBayLed);
                CreateHighBayFixture(archRoot, $"HighBayCenter_{tz:F1}", new Vector3(0f, 4.30f, tz), matDarkSteel, matHighBayLed);
                CreateHighBayFixture(archRoot, $"HighBayRight_{tz:F1}", new Vector3(4.15f, 4.30f, tz), matDarkSteel, matHighBayLed);
            }

            // 2. Center Competition Squat Station (x = 0, z = 0)
            squatStationRoot = CreateGroup(transform, "SquatStation_Center", Vector3.zero);
            BuildCompetitionPlatform(
                squatStationRoot,
                new Vector3(0f, 0f, -0.10f),
                3.0f,
                2.6f,
                1.22f,
                matOakDeck,
                matRubberDrop,
                matDarkSteel,
                matCrimsonSteel);

            Vector3 barSpawn = authoritativeBarbell != null
                ? new Vector3(0f, 1.48f, -0.115f)
                : new Vector3(0f, 1.48f, -0.115f);
            BuildCompetitionSquatComboRack(
                squatStationRoot,
                barSpawn,
                matDarkSteel,
                matCrimsonSteel,
                matChromeBar,
                matGoldAccent,
                matPlateRed25,
                matPlateBlue20,
                matPlateYellow15,
                matPlateGreen10);

            BuildChalkStand(squatStationRoot, new Vector3(-1.45f, 0f, 0.45f), matDarkSteel, matCrimsonSteel, matChalkWhite);
            BuildRefereeDeskAndLamps(squatStationRoot, new Vector3(-1.75f, 0f, 1.78f), matDarkSteel, matCrimsonSteel);

            // 3. Left Bench Press Station (Environmental Staging for Future Lifts)
            benchStationRoot = CreateGroup(transform, "BenchStation_Left", new Vector3(-4.15f, 0f, -0.35f));
            BuildCompetitionPlatform(
                benchStationRoot,
                Vector3.zero,
                2.6f,
                2.4f,
                1.05f,
                matOakDeck,
                matRubberDrop,
                matDarkSteel,
                matGoldAccent);
            BuildBenchPressStation(
                benchStationRoot,
                matDarkSteel,
                matCrimsonSteel,
                matBenchPad,
                matChromeBar,
                matPlateRed25,
                matPlateBlue20,
                matPlateYellow15);

            // 4. Right Deadlift Station (Environmental Staging for Future Lifts)
            deadliftStationRoot = CreateGroup(transform, "DeadliftStation_Right", new Vector3(4.15f, 0f, -0.35f));
            BuildCompetitionPlatform(
                deadliftStationRoot,
                Vector3.zero,
                3.0f,
                2.4f,
                1.20f,
                matOakDeck,
                matRubberDrop,
                matDarkSteel,
                matGoldAccent);
            BuildDeadliftStation(
                deadliftStationRoot,
                matDarkSteel,
                matCrimsonSteel,
                matChromeBar,
                matChalkWhite,
                matPlateRed25,
                matPlateBlue20,
                matPlateGreen10);

            CacheRefereeLamps();
        }

        private static void BuildCompetitionPlatform(
            Transform parent,
            Vector3 centerOffset,
            float totalWidth,
            float totalDepth,
            float oakCenterWidth,
            Material matOak,
            Material matRubber,
            Material matFrame,
            Material matAccent)
        {
            Transform platform = CreateGroup(parent, "CompetitionPlatform", centerOffset);
            const float thickness = 0.024f;
            float yCenter = -thickness * 0.5f; // Top surface at y = 0.000m flush with canonical foot sole plane

            CreateBox(platform, "OakCenterStrip", new Vector3(0f, yCenter, 0f), new Vector3(oakCenterWidth, thickness, totalDepth - 0.12f), matOak);

            float sideRubberWidth = (totalWidth - oakCenterWidth - 0.12f) * 0.5f;
            float sideOffset = (oakCenterWidth + sideRubberWidth) * 0.5f;
            CreateBox(platform, "LeftDropZone", new Vector3(-sideOffset, yCenter, 0f), new Vector3(sideRubberWidth, thickness, totalDepth - 0.12f), matRubber);
            CreateBox(platform, "RightDropZone", new Vector3(sideOffset, yCenter, 0f), new Vector3(sideRubberWidth, thickness, totalDepth - 0.12f), matRubber);

            // Steel perimeter frame
            CreateBox(platform, "FrontBevelFrame", new Vector3(0f, yCenter, totalDepth * 0.5f - 0.03f), new Vector3(totalWidth, thickness + 0.004f, 0.06f), matFrame);
            CreateBox(platform, "RearBevelFrame", new Vector3(0f, yCenter, -totalDepth * 0.5f + 0.03f), new Vector3(totalWidth, thickness + 0.004f, 0.06f), matFrame);
            CreateBox(platform, "LeftBevelFrame", new Vector3(-totalWidth * 0.5f + 0.03f, yCenter, 0f), new Vector3(0.06f, thickness + 0.004f, totalDepth), matFrame);
            CreateBox(platform, "RightBevelFrame", new Vector3(totalWidth * 0.5f - 0.03f, yCenter, 0f), new Vector3(0.06f, thickness + 0.004f, totalDepth), matFrame);

            // Front foot-line accent strip
            CreateBox(platform, "FrontAccentStrip", new Vector3(0f, -0.0005f, totalDepth * 0.5f - 0.075f), new Vector3(oakCenterWidth, 0.002f, 0.025f), matAccent);
        }

        private static void BuildCompetitionSquatComboRack(
            Transform parent,
            Vector3 barPosition,
            Material matDarkSteel,
            Material matCrimsonSteel,
            Material matChrome,
            Material matAccent,
            Material matPlate25,
            Material matPlate20,
            Material matPlate15,
            Material matPlate10)
        {
            Transform rack = CreateGroup(parent, "GAM63 Squat Rack Presentation", Vector3.zero);
            const float halfWidth = 0.78f;
            float postZ = barPosition.z - 0.12f;
            float rearPostZ = barPosition.z - 0.92f;
            float postHeight = 2.26f;

            // Heavy base skids
            CreateBox(rack, "LeftBaseSkid", new Vector3(-halfWidth, 0.035f, postZ - 0.25f), new Vector3(0.11f, 0.07f, 1.55f), matDarkSteel);
            CreateBox(rack, "RightBaseSkid", new Vector3(halfWidth, 0.035f, postZ - 0.25f), new Vector3(0.11f, 0.07f, 1.55f), matDarkSteel);
            CreateBox(rack, "RearBaseCrossmember", new Vector3(0f, 0.04f, rearPostZ), new Vector3(halfWidth * 2f + 0.12f, 0.08f, 0.10f), matDarkSteel);

            // Main competition uprights (crimson powder-coat)
            CreateBox(rack, "Left Upright", new Vector3(-halfWidth, postHeight * 0.5f, postZ), new Vector3(0.085f, postHeight, 0.085f), matCrimsonSteel);
            CreateBox(rack, "Right Upright", new Vector3(halfWidth, postHeight * 0.5f, postZ), new Vector3(0.085f, postHeight, 0.085f), matCrimsonSteel);

            // Rear storage/stabilizer uprights
            CreateBox(rack, "LeftRearUpright", new Vector3(-halfWidth, postHeight * 0.5f, rearPostZ), new Vector3(0.08f, postHeight, 0.08f), matDarkSteel);
            CreateBox(rack, "RightRearUpright", new Vector3(halfWidth, postHeight * 0.5f, rearPostZ), new Vector3(0.08f, postHeight, 0.08f), matDarkSteel);

            // Upper side & top crossbars
            CreateBox(rack, "LeftTopBridge", new Vector3(-halfWidth, postHeight - 0.04f, (postZ + rearPostZ) * 0.5f), new Vector3(0.08f, 0.08f, Mathf.Abs(postZ - rearPostZ) + 0.08f), matDarkSteel);
            CreateBox(rack, "RightTopBridge", new Vector3(halfWidth, postHeight - 0.04f, (postZ + rearPostZ) * 0.5f), new Vector3(0.08f, 0.08f, Mathf.Abs(postZ - rearPostZ) + 0.08f), matDarkSteel);
            CreateBox(rack, "Top Crossbar", new Vector3(0f, postHeight - 0.04f, postZ), new Vector3(halfWidth * 2f + 0.10f, 0.11f, 0.10f), matDarkSteel);
            CreateBox(rack, "TopHeaderAccent", new Vector3(0f, postHeight - 0.04f, postZ + 0.055f), new Vector3(1.10f, 0.045f, 0.015f), matAccent);

            // J-cup cradles aligned with the bar's spawn position
            float hookY = barPosition.y - 0.028f;
            CreateBox(rack, "Left Hook", new Vector3(-0.62f, hookY, barPosition.z + 0.05f), new Vector3(0.14f, 0.045f, 0.24f), matDarkSteel);
            CreateBox(rack, "Right Hook", new Vector3(0.62f, hookY, barPosition.z + 0.05f), new Vector3(0.14f, 0.045f, 0.24f), matDarkSteel);
            CreateBox(rack, "LeftHookLip", new Vector3(-0.62f, hookY + 0.035f, barPosition.z + 0.15f), new Vector3(0.14f, 0.045f, 0.03f), matCrimsonSteel);
            CreateBox(rack, "RightHookLip", new Vector3(0.62f, hookY + 0.035f, barPosition.z + 0.15f), new Vector3(0.14f, 0.045f, 0.03f), matCrimsonSteel);
            CreateBox(rack, "LeftHookArm", new Vector3(-0.70f, hookY, postZ), new Vector3(0.18f, 0.06f, 0.08f), matDarkSteel);
            CreateBox(rack, "RightHookArm", new Vector3(0.70f, hookY, postZ), new Vector3(0.18f, 0.06f, 0.08f), matDarkSteel);

            // Exterior safety spotter arms (kept outside x = ±0.74m so the athlete and plates never intersect)
            CreateBox(rack, "LeftSafetyArm", new Vector3(-halfWidth, 0.72f, postZ + 0.36f), new Vector3(0.07f, 0.05f, 0.68f), matDarkSteel);
            CreateBox(rack, "RightSafetyArm", new Vector3(halfWidth, 0.72f, postZ + 0.36f), new Vector3(0.07f, 0.05f, 0.68f), matDarkSteel);

            // Plate storage horns on rear uprights with calibrated visual plates
            BuildStorageHornWithPlates(rack, "LeftPlateHornHigh", new Vector3(-halfWidth - 0.14f, 1.18f, rearPostZ), -1f, matChrome, matPlate25, 0.225f, 0.028f, 2);
            BuildStorageHornWithPlates(rack, "RightPlateHornHigh", new Vector3(halfWidth + 0.14f, 1.18f, rearPostZ), 1f, matChrome, matPlate25, 0.225f, 0.028f, 2);
            BuildStorageHornWithPlates(rack, "LeftPlateHornMid", new Vector3(-halfWidth - 0.14f, 0.68f, rearPostZ), -1f, matChrome, matPlate20, 0.225f, 0.024f, 2);
            BuildStorageHornWithPlates(rack, "RightPlateHornMid", new Vector3(halfWidth + 0.14f, 0.68f, rearPostZ), 1f, matChrome, matPlate20, 0.225f, 0.024f, 2);
            BuildStorageHornWithPlates(rack, "LeftPlateHornLow", new Vector3(-halfWidth - 0.14f, 0.26f, rearPostZ), -1f, matChrome, matPlate15, 0.200f, 0.022f, 2);
            BuildStorageHornWithPlates(rack, "RightPlateHornLow", new Vector3(halfWidth + 0.14f, 0.26f, rearPostZ), 1f, matChrome, matPlate10, 0.175f, 0.020f, 2);
        }

        private static void BuildStorageHornWithPlates(
            Transform parent,
            string name,
            Vector3 hornCenter,
            float sideSign,
            Material matChrome,
            Material matPlate,
            float plateRadius,
            float plateThickness,
            int count)
        {
            Transform group = CreateGroup(parent, name, hornCenter);
            CreateCylinderAlongX(group, "HornPin", Vector3.zero, 0.024f, 0.26f, matChrome);
            for (int i = 0; i < count; i++)
            {
                float x = -sideSign * 0.06f + sideSign * i * (plateThickness + 0.012f);
                CreateCylinderAlongX(group, $"StoredPlate_{i}", new Vector3(x, 0f, 0f), plateRadius, plateThickness, matPlate);
            }
        }

        private static void BuildChalkStand(
            Transform parent,
            Vector3 position,
            Material matDarkSteel,
            Material matCrimsonSteel,
            Material matChalk)
        {
            Transform stand = CreateGroup(parent, "ChalkBowlStand", position);
            CreateBox(stand, "BasePlate", new Vector3(0f, 0.015f, 0f), new Vector3(0.38f, 0.03f, 0.38f), matDarkSteel);
            CreateBox(stand, "StandColumn", new Vector3(0f, 0.45f, 0f), new Vector3(0.06f, 0.88f, 0.06f), matDarkSteel);
            CreateCylinderAlongY(stand, "Bowl", new Vector3(0f, 0.90f, 0f), 0.18f, 0.09f, matCrimsonSteel);
            CreateCylinderAlongY(stand, "ChalkBlock", new Vector3(0f, 0.94f, 0f), 0.13f, 0.03f, matChalk);
        }

        private void BuildRefereeDeskAndLamps(
            Transform parent,
            Vector3 position,
            Material matDarkSteel,
            Material matCrimsonSteel)
        {
            Transform desk = CreateGroup(parent, "RefereeVerdictDesk", position);
            desk.localRotation = Quaternion.Euler(0f, 28f, 0f);

            CreateBox(desk, "PedestalBase", new Vector3(0f, 0.02f, 0f), new Vector3(0.76f, 0.04f, 0.36f), matDarkSteel);
            CreateBox(desk, "PedestalColumn", new Vector3(0f, 0.46f, 0f), new Vector3(0.18f, 0.88f, 0.18f), matDarkSteel);
            CreateBox(desk, "LightBoxHousing", new Vector3(0f, 0.95f, 0f), new Vector3(0.78f, 0.22f, 0.22f), matDarkSteel);
            CreateBox(desk, "LightBoxCrimsonTrim", new Vector3(0f, 1.07f, 0f), new Vector3(0.80f, 0.025f, 0.24f), matCrimsonSteel);

            refereeLampRenderers = new Renderer[3];
            float[] lampX = { -0.22f, 0f, 0.22f };
            string[] lampNames = { "RefereeLamp_Left", "RefereeLamp_Head", "RefereeLamp_Right" };
            for (int i = 0; i < 3; i++)
            {
                GameObject lamp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                lamp.name = lampNames[i];
                lamp.transform.SetParent(desk, false);
                lamp.transform.localPosition = new Vector3(lampX[i], 0.95f, 0.09f);
                lamp.transform.localScale = Vector3.one * 0.125f;
                RemoveCollider(lamp);
                Renderer r = lamp.GetComponent<Renderer>();
                if (r != null)
                {
                    r.sharedMaterial = _lampIdleMat;
                    refereeLampRenderers[i] = r;
                }
            }
        }

        private static void BuildBenchPressStation(
            Transform parent,
            Material matDarkSteel,
            Material matCrimsonSteel,
            Material matBenchPad,
            Material matChromeBar,
            Material matPlate25,
            Material matPlate20,
            Material matPlate15)
        {
            Transform bench = CreateGroup(parent, "CompetitionBenchRack", Vector3.zero);

            // Bench frame & pad (IPF standard: 1.22m length, 0.305m width, 0.435m height)
            CreateBox(bench, "BenchBaseSpine", new Vector3(0f, 0.04f, 0.05f), new Vector3(0.12f, 0.08f, 1.32f), matDarkSteel);
            CreateBox(bench, "BenchFrontFoot", new Vector3(0f, 0.03f, 0.65f), new Vector3(0.58f, 0.06f, 0.10f), matDarkSteel);
            CreateBox(bench, "BenchRearBase", new Vector3(0f, 0.03f, -0.58f), new Vector3(1.24f, 0.06f, 0.14f), matDarkSteel);
            CreateBox(bench, "BenchSupportFront", new Vector3(0f, 0.22f, 0.48f), new Vector3(0.10f, 0.36f, 0.10f), matDarkSteel);
            CreateBox(bench, "BenchSupportRear", new Vector3(0f, 0.22f, -0.25f), new Vector3(0.10f, 0.36f, 0.10f), matDarkSteel);
            CreateBox(bench, "CompetitionBenchPad", new Vector3(0f, 0.41f, 0.08f), new Vector3(0.305f, 0.065f, 1.22f), matBenchPad);

            // Spotter platform steps
            CreateBox(bench, "SpotterStepLow", new Vector3(0f, 0.08f, -0.78f), new Vector3(0.75f, 0.16f, 0.28f), matDarkSteel);

            // Combo uprights & safety rails
            const float uprightX = 0.60f;
            const float uprightZ = -0.36f;
            CreateBox(bench, "BenchLeftUpright", new Vector3(-uprightX, 0.56f, uprightZ), new Vector3(0.075f, 1.12f, 0.075f), matCrimsonSteel);
            CreateBox(bench, "BenchRightUpright", new Vector3(uprightX, 0.56f, uprightZ), new Vector3(0.075f, 1.12f, 0.075f), matCrimsonSteel);
            CreateBox(bench, "BenchLeftSafety", new Vector3(-uprightX + 0.08f, 0.48f, -0.05f), new Vector3(0.06f, 0.04f, 0.48f), matDarkSteel);
            CreateBox(bench, "BenchRightSafety", new Vector3(uprightX - 0.08f, 0.48f, -0.05f), new Vector3(0.06f, 0.04f, 0.48f), matDarkSteel);

            // Visual-only racked competition barbell at y = 0.94m
            BuildVisualOnlyLoadedBarbell(
                bench,
                "BenchVisualBarbell_115kg",
                new Vector3(0f, 0.94f, uprightZ + 0.04f),
                matChromeBar,
                matPlate25,
                matPlate20,
                matPlate15,
                1,
                1,
                0);
        }

        private static void BuildDeadliftStation(
            Transform parent,
            Material matDarkSteel,
            Material matCrimsonSteel,
            Material matChromeBar,
            Material matChalk,
            Material matPlate25,
            Material matPlate20,
            Material matPlate10)
        {
            Transform dl = CreateGroup(parent, "CompetitionDeadliftSetup", Vector3.zero);

            // Grounded competition barbell at y = 0.225m (450mm competition plate radius)
            BuildVisualOnlyLoadedBarbell(
                dl,
                "DeadliftVisualBarbell_165kg",
                new Vector3(0f, 0.225f, 0.05f),
                matChromeBar,
                matPlate25,
                matPlate20,
                matPlate10,
                2,
                1,
                0);

            // Deadlift bar jack behind platform
            Transform jack = CreateGroup(dl, "DeadliftBarJack", new Vector3(0f, 0f, -0.78f));
            CreateBox(jack, "JackCrossbar", new Vector3(0f, 0.16f, 0f), new Vector3(1.06f, 0.045f, 0.045f), matCrimsonSteel);
            CreateBox(jack, "JackLeftCradle", new Vector3(-0.46f, 0.18f, 0.06f), new Vector3(0.045f, 0.28f, 0.18f), matDarkSteel);
            CreateBox(jack, "JackRightCradle", new Vector3(0.46f, 0.18f, 0.06f), new Vector3(0.045f, 0.28f, 0.18f), matDarkSteel);
            CreateBox(jack, "JackHandle", new Vector3(0f, 0.46f, -0.18f), new Vector3(0.045f, 0.72f, 0.045f), matCrimsonSteel);

            // Chalk stand beside deadlift platform
            BuildChalkStand(dl, new Vector3(1.55f, 0f, 0.45f), matDarkSteel, matCrimsonSteel, matChalk);
        }

        private static void BuildVisualOnlyLoadedBarbell(
            Transform parent,
            string name,
            Vector3 barCenter,
            Material matChrome,
            Material matPlatePrimary,
            Material matPlateSecondary,
            Material matPlateTertiary,
            int primaryPairs,
            int secondaryPairs,
            int tertiaryPairs)
        {
            Transform bar = CreateGroup(parent, name, barCenter);
            // 2.20m IPF shaft
            CreateCylinderAlongX(bar, "Shaft", Vector3.zero, 0.0145f, 2.20f, matChrome);
            CreateCylinderAlongX(bar, "LeftSleeve", new Vector3(-0.885f, 0f, 0f), 0.025f, 0.43f, matChrome);
            CreateCylinderAlongX(bar, "RightSleeve", new Vector3(0.885f, 0f, 0f), 0.025f, 0.43f, matChrome);
            CreateCylinderAlongX(bar, "LeftInnerFlange", new Vector3(-0.665f, 0f, 0f), 0.040f, 0.016f, matChrome);
            CreateCylinderAlongX(bar, "RightInnerFlange", new Vector3(0.665f, 0f, 0f), 0.040f, 0.016f, matChrome);

            float[] sides = { -1f, 1f };
            foreach (float sign in sides)
            {
                float cursor = 0.688f;
                for (int i = 0; i < primaryPairs; i++)
                {
                    CreateCylinderAlongX(bar, $"PlatePrimary_{sign}_{i}", new Vector3(sign * cursor, 0f, 0f), 0.225f, 0.028f, matPlatePrimary);
                    cursor += 0.032f;
                }
                for (int i = 0; i < secondaryPairs; i++)
                {
                    CreateCylinderAlongX(bar, $"PlateSecondary_{sign}_{i}", new Vector3(sign * cursor, 0f, 0f), 0.225f, 0.024f, matPlateSecondary);
                    cursor += 0.028f;
                }
                for (int i = 0; i < tertiaryPairs; i++)
                {
                    CreateCylinderAlongX(bar, $"PlateTertiary_{sign}_{i}", new Vector3(sign * cursor, 0f, 0f), 0.195f, 0.022f, matPlateTertiary);
                    cursor += 0.026f;
                }
                CreateCylinderAlongX(bar, $"Collar_{sign}", new Vector3(sign * (cursor + 0.018f), 0f, 0f), 0.046f, 0.038f, matChrome);
            }
        }

        private static void CreateStationWallBanner(
            Transform parent,
            string name,
            Vector3 position,
            Material matPanel,
            Material matFrame,
            Material matAccent)
        {
            Transform banner = CreateGroup(parent, name, position);
            CreateBox(banner, "BannerFrame", Vector3.zero, new Vector3(2.85f, 1.35f, 0.04f), matFrame);
            CreateBox(banner, "BannerFace", new Vector3(0f, 0f, 0.015f), new Vector3(2.72f, 1.22f, 0.03f), matPanel);
            CreateBox(banner, "BannerGoldBar", new Vector3(0f, -0.46f, 0.028f), new Vector3(2.40f, 0.045f, 0.02f), matAccent);
        }

        private static void CreateHighBayFixture(
            Transform parent,
            string name,
            Vector3 position,
            Material matHousing,
            Material matEmissive)
        {
            Transform fixture = CreateGroup(parent, name, position);
            CreateBox(fixture, "Housing", new Vector3(0f, 0.04f, 0f), new Vector3(1.15f, 0.07f, 0.34f), matHousing);
            CreateBox(fixture, "Diffuser", new Vector3(0f, 0f, 0f), new Vector3(1.08f, 0.025f, 0.28f), matEmissive);
        }

        private void CacheRefereeLamps()
        {
            if (squatStationRoot == null)
                return;

            Transform desk = squatStationRoot.Find("RefereeVerdictDesk");
            if (desk == null)
                return;

            var list = new List<Renderer>(3);
            string[] names = { "RefereeLamp_Left", "RefereeLamp_Head", "RefereeLamp_Right" };
            foreach (string lampName in names)
            {
                Transform t = desk.Find(lampName);
                if (t != null && t.TryGetComponent(out Renderer r))
                    list.Add(r);
            }
            refereeLampRenderers = list.ToArray();
        }

        private void UpdateRefereeLamps()
        {
            if (refereeLampRenderers == null || refereeLampRenderers.Length == 0 || squatController == null)
                return;

            if (_lampIdleMat == null)
                _lampIdleMat = CreateEmissiveMaterial("RefereeLampIdle", new Color(0.32f, 0.35f, 0.40f), new Color(0.15f, 0.18f, 0.22f));
            if (_lampWhiteMat == null)
                _lampWhiteMat = CreateEmissiveMaterial("RefereeLampGood", new Color(0.98f, 0.98f, 1.0f), new Color(2.8f, 2.8f, 3.0f));
            if (_lampRedMat == null)
                _lampRedMat = CreateEmissiveMaterial("RefereeLampNoLift", new Color(0.95f, 0.14f, 0.16f), new Color(2.8f, 0.20f, 0.22f));

            SquatAttemptRecord record = squatController.AttemptRecord;
            Material targetMat = _lampIdleMat;
            if (record != null)
            {
                bool isGoodLift = record.TerminalReason == SquatAttemptTerminalReason.PHYSICAL_LOCKOUT &&
                    record.RuleOutcome == SquatJudgmentOutcome.GOOD_LIFT &&
                    record.PhysicalFailureOutcome != SquatFailureResultKind.PHYSICAL_FAILURE;
                targetMat = isGoodLift ? _lampWhiteMat : _lampRedMat;
            }

            for (int i = 0; i < refereeLampRenderers.Length; i++)
            {
                Renderer r = refereeLampRenderers[i];
                if (r != null && r.sharedMaterial != targetMat)
                    r.sharedMaterial = targetMat;
            }
        }

        private void ConfigureArenaLighting()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.28f, 0.33f, 0.40f);
            RenderSettings.ambientEquatorColor = new Color(0.19f, 0.21f, 0.25f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.10f, 0.12f);

            GameObject keyLightObj = GameObject.Find("Key Light");
            if (keyLightObj != null && keyLightObj.TryGetComponent(out Light keyLight))
            {
                keyLight.color = new Color(1.0f, 0.95f, 0.88f);
                keyLight.intensity = 1.95f;
                keyLight.shadows = LightShadows.Soft;
                keyLightObj.transform.rotation = Quaternion.Euler(48f, -32f, 0f);
            }

            GameObject fillLightObj = GameObject.Find("Fill Light");
            if (fillLightObj != null && fillLightObj.TryGetComponent(out Light fillLight))
            {
                fillLight.color = new Color(0.66f, 0.78f, 0.98f);
                fillLight.intensity = 0.85f;
                fillLightObj.transform.rotation = Quaternion.Euler(28f, 148f, 0f);
            }

            Transform lightsGroup = transform.Find("GymAccentLights");
            if (lightsGroup == null)
            {
                lightsGroup = CreateGroup(transform, "GymAccentLights", Vector3.zero);
                CreateArenaSpotLight(lightsGroup, "CenterSquatSpot", new Vector3(0f, 4.15f, 1.15f), new Vector3(0f, 0.9f, -0.15f), new Color(1.0f, 0.96f, 0.90f), 4.2f, 52f);
                CreateArenaSpotLight(lightsGroup, "LeftBenchSpot", new Vector3(-4.15f, 4.15f, 0.85f), new Vector3(-4.15f, 0.6f, -0.35f), new Color(0.92f, 0.95f, 1.0f), 2.8f, 54f);
                CreateArenaSpotLight(lightsGroup, "RightDeadliftSpot", new Vector3(4.15f, 4.15f, 0.85f), new Vector3(4.15f, 0.4f, -0.35f), new Color(0.92f, 0.95f, 1.0f), 2.8f, 54f);
            }
        }

        private static void CreateArenaSpotLight(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 target,
            Color color,
            float intensity,
            float spotAngle)
        {
            GameObject lightGo = new GameObject(name);
            lightGo.transform.SetParent(parent, false);
            lightGo.transform.position = position;
            lightGo.transform.LookAt(target, Vector3.up);
            Light lightComp = lightGo.AddComponent<Light>();
            lightComp.type = LightType.Spot;
            lightComp.color = color;
            lightComp.intensity = intensity;
            lightComp.range = 9.5f;
            lightComp.spotAngle = spotAngle;
            lightComp.shadows = LightShadows.None;
        }

        private void StripAllEnvironmentalColliders()
        {
            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider col = colliders[i];
                if (col == null)
                    continue;
                col.enabled = false;
                if (Application.isPlaying)
                    Destroy(col);
                else
                    DestroyImmediate(col);
            }
        }

        private Material CreateLitMaterial(string name, Color albedo, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                SkinnedMeshRenderer existing = FindFirstObjectByType<SkinnedMeshRenderer>();
                if (existing != null && existing.sharedMaterial != null)
                    shader = existing.sharedMaterial.shader;
            }
            if (shader == null)
                shader = Shader.Find("Standard");
            Material mat = new Material(shader)
            {
                name = name,
                color = albedo
            };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", albedo);
            if (mat.HasProperty("_Metallic"))
                mat.SetFloat("_Metallic", metallic);
            if (mat.HasProperty("_Smoothness"))
                mat.SetFloat("_Smoothness", smoothness);
            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", smoothness);
            _runtimeMaterials.Add(mat);
            return mat;
        }

        private Material CreateEmissiveMaterial(string name, Color baseColor, Color emissionColor)
        {
            Material mat = CreateLitMaterial(name, baseColor, 0.1f, 0.7f);
            mat.EnableKeyword("_EMISSION");
            if (mat.HasProperty("_EmissionColor"))
                mat.SetColor("_EmissionColor", emissionColor);
            return mat;
        }

        private static Transform CreateGroup(Transform parent, string name, Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static GameObject CreateBox(
            Transform parent,
            string name,
            Vector3 localPos,
            Vector3 localScale,
            Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, false);
            box.transform.localPosition = localPos;
            box.transform.localRotation = Quaternion.identity;
            box.transform.localScale = localScale;
            RemoveCollider(box);
            Renderer renderer = box.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
            return box;
        }

        private static GameObject CreateCylinderAlongX(
            Transform parent,
            string name,
            Vector3 localPos,
            float radius,
            float length,
            Material material)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = name;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = localPos;
            cyl.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            cyl.transform.localScale = new Vector3(radius * 2f, length * 0.5f, radius * 2f);
            RemoveCollider(cyl);
            Renderer renderer = cyl.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
            return cyl;
        }

        private static GameObject CreateCylinderAlongY(
            Transform parent,
            string name,
            Vector3 localPos,
            float radius,
            float height,
            Material material)
        {
            GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cyl.name = name;
            cyl.transform.SetParent(parent, false);
            cyl.transform.localPosition = localPos;
            cyl.transform.localRotation = Quaternion.identity;
            cyl.transform.localScale = new Vector3(radius * 2f, height * 0.5f, radius * 2f);
            RemoveCollider(cyl);
            Renderer renderer = cyl.GetComponent<Renderer>();
            if (renderer != null && material != null)
                renderer.sharedMaterial = material;
            return cyl;
        }

        private static void RemoveCollider(GameObject go)
        {
            Collider col = go.GetComponent<Collider>();
            if (col == null)
                return;
            col.enabled = false;
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }
    }
}
