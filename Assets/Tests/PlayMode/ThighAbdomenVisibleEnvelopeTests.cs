using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H11, section 8. The proxies collide. The question this fixture
    /// exists to answer is whether the human being they stand for collides
    /// too. If the visible thigh and the visible abdomen really do meet on the
    /// accepted reference path, the contact is anatomical and filtering it away
    /// would be lying about the body. If they do not, the proxies are claiming
    /// volume the athlete does not have.
    ///
    /// Everything here is diagnostic. It runs in the reference preview scene,
    /// which has no physical bodies, and it builds its own throwaway geometry.
    /// No MeshCollider is introduced anywhere near production.
    /// </summary>
    public sealed class ThighAbdomenVisibleEnvelopeTests
    {
        private const string ReferenceScene = "SquatReferencePreview";
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-11/thigh-abdomen";

        // The angles H11 measured on the proxies, plus the two ends of the path.
        private static readonly float[] TargetHipFlexionDeg = { 0f, 40f, 51f, 65f, 80f, 95f, 111f };

        private SquatReferencePreview _preview;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ReferenceScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The reference preview scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;

            _preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(_preview, Is.Not.Null);
            _preview.SetShowLandmarks(false);
            _preview.SetShowReferenceBarGhost(false);
            yield return null;
        }

        [UnityTest]
        public IEnumerator V1_VISIBLE_SKIN_ENVELOPE_VERSUS_PROXY_ENVELOPE()
        {
            Animator animator = _preview.ReferenceAnimator;
            Assert.That(animator, Is.Not.Null);

            SkinnedMeshRenderer skin = FindLargestSkin(animator.transform.root);
            Assert.That(skin, Is.Not.Null, "The reference preview has no skinned humanoid to measure.");

            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            Camera camera = Camera.main ?? UnityEngine.Object.FindFirstObjectByType<Camera>();

            var report = new StringBuilder();
            report.AppendLine("GAM-11 PHASE 5H11 V1 VISIBLE SKIN ENVELOPE VERSUS PROXY ENVELOPE");
            report.AppendLine("Skinned mesh baked at the accepted reference pose. Vertices partitioned by");
            report.AppendLine("dominant bone weight into thigh and lower-torso sets. Proxy figures rebuilt");
            report.AppendLine("from the same recipe rule the physical rig uses. Diagnostic only.");
            report.AppendLine();
            report.AppendLine("Crease exclusion radius is swept so the answer does not rest on one choice.");
            report.AppendLine();
            report.AppendLine("hip_deg  phase   skin_gap@0.10  skin_gap@0.13  skin_gap@0.16  proxy_gap_m  proxy_pen_m  proxy_touch");

            var csv = new StringBuilder();
            csv.AppendLine("target_hip_deg,phase,actual_hip_deg,trunk_deg," +
                           "skin_gap_r010_m,skin_gap_r013_m,skin_gap_r016_m," +
                           "proxy_surface_gap_m,proxy_penetration_m,proxy_contacting," +
                           "thigh_vertex_count,torso_vertex_count");

            float visibleContactOnsetHipDeg = float.NaN;
            float proxyContactOnsetHipDeg = float.NaN;
            string overclaimStanding = string.Empty;
            string overclaimDeep = string.Empty;

            foreach (float targetHip in TargetHipFlexionDeg)
            {
                float phase = PhaseForHipFlexion(profile, targetHip);
                SquatReferencePose pose = profile.Evaluate(phase, SquatPhaseDirection.Descent);
                float hipDeg = pose.HipFlexionRad * Mathf.Rad2Deg;
                float trunkDeg = pose.TrunkFlexionRad * Mathf.Rad2Deg;

                _preview.SetReviewPose(phase, SquatPhaseDirection.Descent, SquatState.DESCENT);
                yield return null;

                float gap10 = MeasureSkinGap(skin, animator, 0.10f, out int thighCount, out int torsoCount);
                float gap13 = MeasureSkinGap(skin, animator, 0.13f, out int _, out int _);
                float gap16 = MeasureSkinGap(skin, animator, 0.16f, out int _, out int _);
                // The 0.13 m exclusion is the reported one: wide enough to clear
                // the fold, narrow enough to keep the surfaces that would meet.
                bool skinIntersecting = gap13 <= 0.005f;

                ProxyPose thigh = BuildThighProxy(animator, true);
                ProxyPose abdomen = BuildAbdomenProxy(animator);
                float proxyGap = ProxySurfaceGap(thigh, abdomen, out float proxyPenetration);
                bool proxyContacting = proxyGap <= 0.02f;

                if (float.IsNaN(visibleContactOnsetHipDeg) && skinIntersecting)
                    visibleContactOnsetHipDeg = hipDeg;
                if (float.IsNaN(proxyContactOnsetHipDeg) && proxyContacting)
                    proxyContactOnsetHipDeg = hipDeg;

                if (targetHip <= 0.5f)
                    overclaimStanding = MeasureEnvelopeOverclaim(skin, animator, thigh, abdomen);
                if (targetHip >= 110f)
                    overclaimDeep = MeasureEnvelopeOverclaim(skin, animator, thigh, abdomen);

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0,6:F1}  {1,5:F3}  {2,13:F4}  {3,13:F4}  {4,13:F4}  {5,11:F4}  {6,11:F4}  {7,11}",
                    hipDeg, phase, gap10, gap13, gap16, proxyGap, proxyPenetration, proxyContacting));

                csv.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F1},{1:F4},{2:F2},{3:F2},{4:F5},{5:F5},{6:F5},{7:F5},{8:F5},{9},{10},{11}",
                    targetHip, phase, hipDeg, trunkDeg,
                    gap10, gap13, gap16, proxyGap, proxyPenetration, proxyContacting ? 1 : 0,
                    thighCount, torsoCount));

                CaptureOverlay(camera, skin, thigh, abdomen, hipDeg);
            }

            report.AppendLine();
            report.AppendLine("--- proxy envelope against the skin it stands for ---");
            report.AppendLine("standing: " + overclaimStanding);
            report.AppendLine("deepest : " + overclaimDeep);
            report.AppendLine();
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "VISIBLE_SKIN_CONTACT_ONSET_HIP_DEG = {0}",
                float.IsNaN(visibleContactOnsetHipDeg) ? "NONE_ON_PATH" : visibleContactOnsetHipDeg.ToString("F1", CultureInfo.InvariantCulture)));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "PROXY_CONTACT_ONSET_HIP_DEG        = {0}",
                float.IsNaN(proxyContactOnsetHipDeg) ? "NONE_ON_PATH" : proxyContactOnsetHipDeg.ToString("F1", CultureInfo.InvariantCulture)));

            WriteMeasurement("GAM11-5h11-v1-visible-envelope.txt", report.ToString());
            WriteMeasurement("GAM11-5h11-v1-visible-envelope.csv", csv.ToString());
            Debug.Log(report.ToString());
            yield return null;
        }

        // ------------------------------------------------------------------

        private static float PhaseForHipFlexion(SquatReferenceProfile profile, float targetDeg)
        {
            float bestPhase = 0f;
            float bestError = float.PositiveInfinity;
            for (int step = 0; step <= 1000; step++)
            {
                float phase = step / 1000f;
                float hip = profile.Evaluate(phase, SquatPhaseDirection.Descent).HipFlexionRad * Mathf.Rad2Deg;
                float error = Mathf.Abs(hip - targetDeg);
                if (error < bestError)
                {
                    bestError = error;
                    bestPhase = phase;
                }
            }
            return bestPhase;
        }

        private static SkinnedMeshRenderer FindLargestSkin(Transform root)
        {
            SkinnedMeshRenderer best = null;
            int bestVertices = 0;
            foreach (SkinnedMeshRenderer candidate in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (candidate.sharedMesh == null)
                    continue;
                if (candidate.sharedMesh.vertexCount > bestVertices)
                {
                    bestVertices = candidate.sharedMesh.vertexCount;
                    best = candidate;
                }
            }
            return best;
        }

        /// <summary>
        /// Bakes the posed skin and returns the smallest distance between the
        /// thigh shaft and the belly, with the hip crease excluded.
        ///
        /// The crease has to be excluded or the measurement is meaningless: the
        /// skin is one continuous surface, so thigh-weighted and torso-weighted
        /// vertices are always neighbours across the hip fold, and the raw
        /// minimum just reports the mesh's vertex spacing at that seam no
        /// matter what pose the athlete is in. Excluding a sphere around the
        /// hip bone from both sets leaves the two surfaces that would actually
        /// have to press together for the contact to be real.
        /// </summary>
        private static float MeasureSkinGap(
            SkinnedMeshRenderer skin,
            Animator animator,
            float creaseExclusionRadius,
            out int thighCount,
            out int torsoCount)
        {
            var baked = new Mesh();
            skin.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            Transform[] bones = skin.bones;

            var thighBones = new HashSet<Transform>();
            AddBone(thighBones, animator, HumanBodyBones.LeftUpperLeg);
            AddBone(thighBones, animator, HumanBodyBones.RightUpperLeg);

            var torsoBones = new HashSet<Transform>();
            AddBone(torsoBones, animator, HumanBodyBones.Hips);
            AddBone(torsoBones, animator, HumanBodyBones.Spine);
            AddBone(torsoBones, animator, HumanBodyBones.Chest);

            Vector3 leftHip = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg).position;
            Vector3 rightHip = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg).position;

            var thighPoints = new List<Vector3>();
            var torsoPoints = new List<Vector3>();
            Transform skinTransform = skin.transform;
            int count = Mathf.Min(vertices.Length, weights.Length);
            for (int index = 0; index < count; index++)
            {
                int boneIndex = weights[index].boneIndex0;
                if (boneIndex < 0 || boneIndex >= bones.Length)
                    continue;
                Transform bone = bones[boneIndex];
                Vector3 world = skinTransform.TransformPoint(vertices[index]);
                float creaseDistance = Mathf.Min(
                    Vector3.Distance(world, leftHip),
                    Vector3.Distance(world, rightHip));
                if (creaseDistance < creaseExclusionRadius)
                    continue;
                if (thighBones.Contains(bone))
                    thighPoints.Add(world);
                else if (torsoBones.Contains(bone))
                    torsoPoints.Add(world);
            }

            thighCount = thighPoints.Count;
            torsoCount = torsoPoints.Count;
            UnityEngine.Object.DestroyImmediate(baked);

            float best = float.PositiveInfinity;
            for (int a = 0; a < thighPoints.Count; a++)
            {
                Vector3 pa = thighPoints[a];
                for (int b = 0; b < torsoPoints.Count; b++)
                {
                    float sqr = (pa - torsoPoints[b]).sqrMagnitude;
                    if (sqr < best)
                        best = sqr;
                }
            }
            return best == float.PositiveInfinity ? float.NaN : Mathf.Sqrt(best);
        }

        /// <summary>
        /// How much volume each proxy claims that the athlete does not have.
        /// The thigh capsule is compared with the actual radial spread of
        /// thigh-weighted skin about the femur axis; the abdomen box with the
        /// actual half width and half depth of torso skin inside the box's own
        /// height band, expressed in the box's own frame.
        /// </summary>
        private static string MeasureEnvelopeOverclaim(
            SkinnedMeshRenderer skin,
            Animator animator,
            ProxyPose thigh,
            ProxyPose abdomen)
        {
            var baked = new Mesh();
            skin.BakeMesh(baked, true);
            Vector3[] vertices = baked.vertices;
            BoneWeight[] weights = skin.sharedMesh.boneWeights;
            Transform[] bones = skin.bones;
            Transform skinTransform = skin.transform;

            var thighBones = new HashSet<Transform>();
            AddBone(thighBones, animator, HumanBodyBones.LeftUpperLeg);
            var torsoBones = new HashSet<Transform>();
            AddBone(torsoBones, animator, HumanBodyBones.Hips);
            AddBone(torsoBones, animator, HumanBodyBones.Spine);
            AddBone(torsoBones, animator, HumanBodyBones.Chest);

            Vector3 thighAxis = thigh.Rotation * Vector3.up;
            float thighHalf = thigh.Dimensions.y * 0.5f;
            float maxThighRadius = 0f;
            float sumThighRadius = 0f;
            int thighSamples = 0;

            Quaternion inverseAbdomen = Quaternion.Inverse(abdomen.Rotation);
            Vector3 abdomenHalf = 0.5f * abdomen.Dimensions;
            float maxTorsoHalfWidth = 0f;
            float maxTorsoHalfDepth = 0f;
            int torsoSamples = 0;

            int count = Mathf.Min(vertices.Length, weights.Length);
            for (int index = 0; index < count; index++)
            {
                int boneIndex = weights[index].boneIndex0;
                if (boneIndex < 0 || boneIndex >= bones.Length)
                    continue;
                Transform bone = bones[boneIndex];
                Vector3 world = skinTransform.TransformPoint(vertices[index]);

                if (thighBones.Contains(bone))
                {
                    Vector3 offset = world - thigh.Position;
                    float along = Vector3.Dot(offset, thighAxis);
                    if (Mathf.Abs(along) <= thighHalf)
                    {
                        float radius = (offset - thighAxis * along).magnitude;
                        maxThighRadius = Mathf.Max(maxThighRadius, radius);
                        sumThighRadius += radius;
                        thighSamples++;
                    }
                }
                else if (torsoBones.Contains(bone))
                {
                    Vector3 local = inverseAbdomen * (world - abdomen.Position);
                    if (Mathf.Abs(local.y) <= abdomenHalf.y)
                    {
                        maxTorsoHalfWidth = Mathf.Max(maxTorsoHalfWidth, Mathf.Abs(local.x));
                        maxTorsoHalfDepth = Mathf.Max(maxTorsoHalfDepth, Mathf.Abs(local.z));
                        torsoSamples++;
                    }
                }
            }
            UnityEngine.Object.DestroyImmediate(baked);

            float capsuleRadius = thigh.Dimensions.x * 0.5f;
            return string.Format(CultureInfo.InvariantCulture,
                "thigh capsule radius {0:F4} m vs skin max {1:F4} m mean {2:F4} m over {3} vertices " +
                "(overclaim max {4:F4} m) | abdomen box half width {5:F4} depth {6:F4} vs skin half width " +
                "{7:F4} depth {8:F4} over {9} vertices (overclaim width {10:F4} m depth {11:F4} m)",
                capsuleRadius, maxThighRadius, thighSamples > 0 ? sumThighRadius / thighSamples : 0f, thighSamples,
                capsuleRadius - maxThighRadius,
                abdomenHalf.x, abdomenHalf.z, maxTorsoHalfWidth, maxTorsoHalfDepth, torsoSamples,
                abdomenHalf.x - maxTorsoHalfWidth, abdomenHalf.z - maxTorsoHalfDepth);
        }

        private static void AddBone(HashSet<Transform> set, Animator animator, HumanBodyBones bone)
        {
            Transform transform = animator.GetBoneTransform(bone);
            if (transform != null)
                set.Add(transform);
        }

        private readonly struct ProxyPose
        {
            public ProxyPose(Vector3 position, Quaternion rotation, Vector3 dimensions, bool capsule)
            {
                Position = position;
                Rotation = rotation;
                Dimensions = dimensions;
                Capsule = capsule;
            }

            public Vector3 Position { get; }
            public Quaternion Rotation { get; }
            public Vector3 Dimensions { get; }
            public bool Capsule { get; }
        }

        /// <summary>
        /// Rebuilds the thigh capsule from the same rule PhysicalAthleteRig
        /// uses: centre at the recipe COM fraction between hip and knee, long
        /// axis along the femur, radius half the recipe width, cylinder length
        /// 0.84 of the measured femur.
        /// </summary>
        private static ProxyPose BuildThighProxy(Animator animator, bool left)
        {
            Transform hip = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
            Transform knee = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
            Vector3 axis = knee.position - hip.position;
            float length = axis.magnitude;
            Vector3 centre = Vector3.Lerp(hip.position, knee.position, 0.4095f);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            return new ProxyPose(centre, rotation, new Vector3(0.18f, length * 0.84f, 0.18f), true);
        }

        private static ProxyPose BuildAbdomenProxy(Animator animator)
        {
            Transform spine = animator.GetBoneTransform(HumanBodyBones.Spine);
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            Vector3 axis = chest.position - spine.position;
            Vector3 centre = Vector3.Lerp(spine.position, chest.position, 0.50f);
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, axis.normalized);
            return new ProxyPose(centre, rotation, new Vector3(0.25f, 0.16f, 0.17f), false);
        }

        /// <summary>
        /// Capsule segment against oriented box, sampled along the segment.
        /// Returns the surface gap and, when they overlap, an approximate
        /// penetration depth along the same measure.
        /// </summary>
        private static float ProxySurfaceGap(ProxyPose capsule, ProxyPose box, out float penetration)
        {
            float radius = capsule.Dimensions.x * 0.5f;
            float half = Mathf.Max(0f, capsule.Dimensions.y * 0.5f - radius);
            Vector3 axis = capsule.Rotation * Vector3.up;
            Vector3 tip = capsule.Position + axis * half;
            Vector3 tail = capsule.Position - axis * half;

            Vector3 boxHalf = 0.5f * box.Dimensions;
            Quaternion inverse = Quaternion.Inverse(box.Rotation);

            float best = float.PositiveInfinity;
            const int samples = 400;
            for (int index = 0; index <= samples; index++)
            {
                Vector3 point = Vector3.Lerp(tail, tip, index / (float)samples);
                Vector3 local = inverse * (point - box.Position);
                var clamped = new Vector3(
                    Mathf.Clamp(local.x, -boxHalf.x, boxHalf.x),
                    Mathf.Clamp(local.y, -boxHalf.y, boxHalf.y),
                    Mathf.Clamp(local.z, -boxHalf.z, boxHalf.z));
                best = Mathf.Min(best, (local - clamped).magnitude);
            }
            float gap = best - radius;
            penetration = gap < 0f ? -gap : 0f;
            return Mathf.Max(0f, gap);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Each pose is captured twice from the same camera: the athlete alone,
        /// and the two proxies alone. Comparing the pair shows what volume the
        /// primitives claim that the body does not fill, which a single blended
        /// overlay hides.
        /// </summary>
        private static void CaptureOverlay(
            Camera camera,
            SkinnedMeshRenderer skin,
            ProxyPose thigh,
            ProxyPose abdomen,
            float hipDeg)
        {
            if (camera == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            GameObject thighProxy = BuildVisual(thigh, new Color(0.2f, 0.85f, 1f, 1f));
            GameObject abdomenProxy = BuildVisual(abdomen, new Color(1f, 0.35f, 0.2f, 1f));
            try
            {
                string tag = string.Format(CultureInfo.InvariantCulture, "hip_{0:F0}", hipDeg);

                // Frame the hip region itself, which is where the contact is.
                Vector3 hipFocus = 0.5f * (thigh.Position + abdomen.Position);
                Vector3 sideEye = hipFocus + new Vector3(1.05f, 0.10f, 0.05f);
                Vector3 frontEye = hipFocus + new Vector3(0.05f, 0.10f, 1.05f);
                Vector3 obliqueEye = hipFocus + new Vector3(0.72f, 0.30f, 0.72f);

                Capture(camera, skin, thighProxy, abdomenProxy, true, false,
                    "skin_" + tag + "_hipside.png", sideEye, hipFocus);
                Capture(camera, skin, thighProxy, abdomenProxy, false, true,
                    "proxy_" + tag + "_hipside.png", sideEye, hipFocus);
                Capture(camera, skin, thighProxy, abdomenProxy, true, true,
                    "both_" + tag + "_hipside.png", sideEye, hipFocus);

                Capture(camera, skin, thighProxy, abdomenProxy, true, false,
                    "skin_" + tag + "_hipfront.png", frontEye, hipFocus);
                Capture(camera, skin, thighProxy, abdomenProxy, false, true,
                    "proxy_" + tag + "_hipfront.png", frontEye, hipFocus);

                Capture(camera, skin, thighProxy, abdomenProxy, true, true,
                    "both_" + tag + "_hipoblique.png", obliqueEye, hipFocus);

                Capture(camera, skin, thighProxy, abdomenProxy, true, false,
                    "skin_" + tag + "_whole.png",
                    hipFocus + new Vector3(2.4f, 0.25f, 0f), hipFocus + new Vector3(0f, 0.1f, 0f));
            }
            finally
            {
                skin.enabled = true;
                UnityEngine.Object.DestroyImmediate(thighProxy);
                UnityEngine.Object.DestroyImmediate(abdomenProxy);
            }
        }

        private static void Capture(
            Camera camera,
            SkinnedMeshRenderer skin,
            GameObject thighProxy,
            GameObject abdomenProxy,
            bool showSkin,
            bool showProxies,
            string filename,
            Vector3 eye,
            Vector3 focus)
        {
            skin.enabled = showSkin;
            thighProxy.SetActive(showProxies);
            abdomenProxy.SetActive(showProxies);
            CaptureView(camera, filename, eye, focus);
            skin.enabled = true;
            thighProxy.SetActive(false);
            abdomenProxy.SetActive(false);
        }

        private static GameObject BuildVisual(ProxyPose pose, Color color)
        {
            GameObject visual = GameObject.CreatePrimitive(pose.Capsule ? PrimitiveType.Capsule : PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            visual.transform.localScale = pose.Capsule
                ? new Vector3(pose.Dimensions.x, pose.Dimensions.y * 0.5f, pose.Dimensions.z)
                : pose.Dimensions;
            Renderer renderer = visual.GetComponent<Renderer>();
            Material material = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
            material.color = color;
            renderer.sharedMaterial = material;
            visual.SetActive(false);
            return visual;
        }

        private static void CaptureView(
            Camera camera,
            string filename,
            Vector3 cameraPos,
            Vector3 lookAtTarget)
        {
            Vector3 savedPos = camera.transform.position;
            Quaternion savedRot = camera.transform.rotation;
            camera.transform.position = cameraPos;
            camera.transform.rotation = Quaternion.LookRotation((lookAtTarget - cameraPos).normalized);

            RenderTexture target = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousTarget = camera.targetTexture;
            RenderTexture previousActive = RenderTexture.active;
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
                image.Apply();
                string directory = Path.GetFullPath(EvidenceDirectory);
                Directory.CreateDirectory(directory);
                File.WriteAllBytes(Path.Combine(directory, filename), image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(image);
                camera.transform.position = savedPos;
                camera.transform.rotation = savedRot;
            }
        }

        private static void WriteMeasurement(string filename, string content)
        {
            string directory = Path.GetFullPath(MeasurementDirectory);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, filename), content);
        }
    }
}
