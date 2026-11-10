using System;
using System.Collections.Generic;
using PowerliftingSimulator.Bench;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PowerliftingSimulator.Bench.Unity
{
    /// <summary>
    /// Authoritative competition bench geometry in world metres (+Y up, the
    /// bench's long axis along Z, head end toward -Z). The rest and safety
    /// heights are not constants: they are chosen from the measured athlete
    /// and checked against IPF section 2.6 before anything is built.
    /// </summary>
    public readonly struct BenchEquipmentSpec
    {
        public const float PadTopY = 0.440f;          // 2.6: 0.42-0.45 m
        public const float PadWidth = 0.300f;         // 2.6: 0.29-0.32 m
        public const float PadLength = 1.220f;        // 2.6: >= 1.22 m
        public const float PadThickness = 0.080f;
        public const float RestInnerX = 0.555f;       // 2.6: >= 1.10 m between rest insides
        public const float RestOuterX = 0.625f;       // clear of the bar shoulder collar (inner face 0.635 m)
        public const float RestDepth = 0.090f;
        public const float RestFrontLipHeight = 0.020f;  // toward the feet, the lift-off side
        public const float RestBackLipHeight = 0.070f;   // toward the head
        public const float RestLipThickness = 0.010f;
        public const float UprightX = 0.660f;
        // The posts stand behind the bar (head side); the J-hooks reach
        // forward to carry the shaft, so the sleeves pass in front of them.
        public const float RestSetbackFromPostM = 0.100f;  // post front face 1 cm behind the 0.12 m collar
        public const float UprightSize = 0.060f;
        public const float SafetyOuterX = 0.625f;     // clear of the bar shoulder collar (inner face 0.635 m)
        public const float SafetyMinWidth = 0.050f;
        public const float SafetyThickness = 0.040f;

        public BenchEquipmentSpec(float headEndZ, float restTopY, float safetyTopY, float safetyZMin, float safetyZMax, float safetyInnerX)
        {
            SafetyInnerX = safetyInnerX;
            HeadEndZ = headEndZ;
            RestTopY = restTopY;
            SafetyTopY = safetyTopY;
            SafetyZMin = safetyZMin;
            SafetyZMax = safetyZMax;
        }

        public float HeadEndZ { get; }
        public float FootEndZ => HeadEndZ + PadLength;
        /// <summary>Centre of the upright posts: 0.22 m from the head end of the pad (2.6).</summary>
        public float UprightZ => HeadEndZ + (float)BenchRulebook.HeadBeyondUprightCenterM;

        /// <summary>Line of the bar rests, in front of the posts.</summary>
        public float RestZ => UprightZ + RestSetbackFromPostM;
        public float RestTopY { get; }
        public float SafetyTopY { get; }
        public float SafetyZMin { get; }
        public float SafetyZMax { get; }

        /// <summary>Inner edge of the safety arms: outside the measured arm envelope.</summary>
        public float SafetyInnerX { get; }

        /// <summary>Bar centre when resting in the rests.</summary>
        public Vector3 BarRestCenter(float barRadius) => new Vector3(0f, RestTopY + barRadius, RestZ);

        /// <summary>Every 2.6 dimension this bench must satisfy, with the measured value.</summary>
        public IReadOnlyList<string> Violations()
        {
            var violations = new List<string>();
            void Check(bool ok, string text)
            {
                if (!ok) violations.Add(text);
            }
            Check(PadLength >= BenchRulebook.BenchMinLengthM - 1e-6, $"pad length {PadLength} < 1.22");
            Check(PadWidth >= BenchRulebook.BenchMinWidthM - 1e-6 && PadWidth <= BenchRulebook.BenchMaxWidthM + 1e-6, $"pad width {PadWidth}");
            Check(PadTopY >= BenchRulebook.BenchMinHeightM - 1e-6 && PadTopY <= BenchRulebook.BenchMaxHeightM + 1e-6, $"pad height {PadTopY}");
            Check(RestTopY >= BenchRulebook.UprightRestMinHeightM - 1e-6 && RestTopY <= BenchRulebook.UprightRestMaxHeightM + 1e-6,
                $"bar rest height {RestTopY:F3} m outside 0.75-1.10 m");
            Check(2f * RestInnerX >= BenchRulebook.MinWidthBetweenRestsM - 1e-6, $"rest inside width {2f * RestInnerX}");
            Check(SafetyTopY >= BenchRulebook.SafetyRackMinHeightM - 1e-6, $"safety height {SafetyTopY:F3} m < 0.50 m");
            Check(SafetyOuterX - SafetyInnerX >= SafetyMinWidth - 1e-6,
                $"no room for safety arms between the arm envelope ({SafetyInnerX:F3} m) and the bar collar");
            return violations;
        }
    }

    /// <summary>
    /// Builds the bench, rests, uprights and safeties as static colliders in
    /// the authoritative physics scene, each with a visible box of exactly the
    /// collider's size. Nothing here belongs to the decorative gym.
    /// </summary>
    public sealed class BenchEquipment
    {
        public const string RootName = "BenchEquipment_GAM69_Authoritative";

        private readonly List<Collider> _colliders = new List<Collider>();

        public BenchEquipment(BenchEquipmentSpec spec)
        {
            Spec = spec;
        }

        public BenchEquipmentSpec Spec { get; }
        public GameObject Root { get; private set; }
        public Collider Pad { get; private set; }
        public IReadOnlyList<Collider> Colliders => _colliders;

        public void Build(Scene physicsScene, PhysicsMaterial padMaterial, PhysicsMaterial steelMaterial)
        {
            IReadOnlyList<string> violations = Spec.Violations();
            if (violations.Count > 0)
                throw new InvalidOperationException("Bench geometry violates IPF 2.6: " + string.Join("; ", violations));

            Root = new GameObject(RootName);
            SceneManager.MoveGameObjectToScene(Root, physicsScene);

            Material vinyl = Lit(new Color(0.55f, 0.09f, 0.11f), 0.1f, 0.45f);
            Material steel = Lit(new Color(0.11f, 0.13f, 0.16f), 0.7f, 0.5f);
            Material crimson = Lit(new Color(0.68f, 0.11f, 0.14f), 0.55f, 0.58f);
            Material safetyMat = Lit(new Color(0.84f, 0.64f, 0.18f), 0.35f, 0.5f);

            float zc = Spec.HeadEndZ + BenchEquipmentSpec.PadLength * 0.5f;
            Pad = Box("BenchPad", new Vector3(0f, BenchEquipmentSpec.PadTopY - BenchEquipmentSpec.PadThickness * 0.5f, zc),
                new Vector3(BenchEquipmentSpec.PadWidth, BenchEquipmentSpec.PadThickness, BenchEquipmentSpec.PadLength),
                vinyl, BenchSurface.BenchPad, padMaterial);

            // Frame: a centre spine and two cross feet. The frame is "the bench
            // or its supports" in 4.2.1(9), so feet touching it are recorded.
            float spineTop = BenchEquipmentSpec.PadTopY - BenchEquipmentSpec.PadThickness;
            Box("BenchSpine", new Vector3(0f, spineTop * 0.5f, zc), new Vector3(0.10f, spineTop, BenchEquipmentSpec.PadLength - 0.12f),
                steel, BenchSurface.BenchFrame, steelMaterial);
            Box("BenchFootCrossbar", new Vector3(0f, 0.03f, Spec.FootEndZ - 0.06f), new Vector3(0.36f, 0.06f, 0.08f),
                steel, BenchSurface.BenchFrame, steelMaterial);
            Box("BenchHeadCrossbar", new Vector3(0f, 0.03f, Spec.UprightZ), new Vector3(2f * BenchEquipmentSpec.UprightX + 0.10f, 0.06f, 0.10f),
                steel, BenchSurface.BenchFrame, steelMaterial);

            foreach (float side in new[] { -1f, 1f })
            {
                string s = side < 0f ? "Left" : "Right";
                float uprightHeight = Spec.RestTopY + 0.20f;
                Box($"Upright{s}", new Vector3(side * BenchEquipmentSpec.UprightX, uprightHeight * 0.5f, Spec.UprightZ),
                    new Vector3(BenchEquipmentSpec.UprightSize, uprightHeight, BenchEquipmentSpec.UprightSize),
                    crimson, BenchSurface.Upright, steelMaterial);

                float restX = side * 0.5f * (BenchEquipmentSpec.RestInnerX + BenchEquipmentSpec.RestOuterX);
                float restWidth = BenchEquipmentSpec.RestOuterX - BenchEquipmentSpec.RestInnerX;
                Box($"Rest{s}Base", new Vector3(restX, Spec.RestTopY - 0.01f, Spec.RestZ),
                    new Vector3(restWidth, 0.02f, BenchEquipmentSpec.RestDepth), steel, BenchSurface.BarRest, steelMaterial);
                float half = BenchEquipmentSpec.RestDepth * 0.5f;
                Box($"Rest{s}FrontLip", new Vector3(restX, Spec.RestTopY + BenchEquipmentSpec.RestFrontLipHeight * 0.5f,
                        Spec.RestZ + half - BenchEquipmentSpec.RestLipThickness * 0.5f),
                    new Vector3(restWidth, BenchEquipmentSpec.RestFrontLipHeight, BenchEquipmentSpec.RestLipThickness),
                    steel, BenchSurface.BarRest, steelMaterial);
                Box($"Rest{s}BackLip", new Vector3(restX, Spec.RestTopY + BenchEquipmentSpec.RestBackLipHeight * 0.5f,
                        Spec.RestZ - half + BenchEquipmentSpec.RestLipThickness * 0.5f),
                    new Vector3(restWidth, BenchEquipmentSpec.RestBackLipHeight, BenchEquipmentSpec.RestLipThickness),
                    steel, BenchSurface.BarRest, steelMaterial);

                // Hook arm from the post face to the back of the rest.
                float armFront = Spec.RestZ - half - 0.005f;
                float armBack = Spec.UprightZ + BenchEquipmentSpec.UprightSize * 0.5f;
                Box($"Rest{s}Arm", new Vector3(restX, Spec.RestTopY + 0.02f, 0.5f * (armFront + armBack)),
                    new Vector3(restWidth, 0.10f, Mathf.Max(0.005f, armFront - armBack)), steel, BenchSurface.BarRest, steelMaterial);

                float safetyX = side * 0.5f * (Spec.SafetyInnerX + BenchEquipmentSpec.SafetyOuterX);
                float safetyLength = Spec.SafetyZMax - Spec.SafetyZMin;
                Box($"Safety{s}", new Vector3(safetyX, Spec.SafetyTopY - BenchEquipmentSpec.SafetyThickness * 0.5f,
                        0.5f * (Spec.SafetyZMin + Spec.SafetyZMax)),
                    new Vector3(BenchEquipmentSpec.SafetyOuterX - Spec.SafetyInnerX, BenchEquipmentSpec.SafetyThickness, safetyLength),
                    safetyMat, BenchSurface.Safety, steelMaterial);
                Box($"SafetyPost{s}", new Vector3(safetyX, (Spec.SafetyTopY - BenchEquipmentSpec.SafetyThickness) * 0.5f, Spec.SafetyZMax - 0.03f),
                    new Vector3(0.05f, Spec.SafetyTopY - BenchEquipmentSpec.SafetyThickness, 0.05f), steel, BenchSurface.BenchFrame, steelMaterial);
            }
        }

        private Collider Box(string name, Vector3 center, Vector3 size, Material material, BenchSurface surface, PhysicsMaterial physicsMaterial)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(Root.transform, false);
            box.transform.SetPositionAndRotation(center, Quaternion.identity);
            box.transform.localScale = size;
            Renderer renderer = box.GetComponent<Renderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            BoxCollider collider = box.GetComponent<BoxCollider>();
            collider.sharedMaterial = physicsMaterial;
            BenchSurfaceRegistry.Register(collider, surface);
            _colliders.Add(collider);
            return collider;
        }

        private static Material Lit(Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                return null;
            var material = new Material(shader) { color = color };
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic"))
                material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness"))
                material.SetFloat("_Smoothness", smoothness);
            return material;
        }
    }
}
