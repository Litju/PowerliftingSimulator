using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM10SquatReferencePreviewQualificationTests
    {
        private const string QualificationScene = "SquatReferencePreview";
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-10";
        private const string MeasurementPath = "Artifacts/Measurements/GAM-10-squat-reference.json";

        [UnityTest]
        public IEnumerator GAM10_ACTUAL_HUMANOID_REFERENCE_PREVIEW_QUALIFICATION()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "GAM-10 visual evidence requires a graphics device.");
            AssertReferenceOnlyTopology(preview);

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            PositionSideReviewCamera(camera);

            preview.SetShowReferenceBarGhost(true);
            preview.SetShowLandmarks(false);
            preview.SetReviewPose(0f, SquatPhaseDirection.None, SquatState.LOCKOUT);
            yield return null;
            CaptureEvidence("GAM-10-standing.png");

            preview.SetReviewPose(0.25f, SquatPhaseDirection.Descent, SquatState.DESCENT);
            yield return null;
            CaptureEvidence("GAM-10-quarter-descent.png");

            preview.SetReviewPose(0.55f, SquatPhaseDirection.Descent, SquatState.DESCENT);
            yield return null;
            CaptureEvidence("GAM-10-near-parallel.png");

            preview.SetReviewPose(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);
            Assert.That(preview.CurrentDepth.BilateralLegalReference, Is.True,
                $"Legal-bottom reference is not bilateral legal: L={preview.CurrentDepth.LeftDepthM:F4} m, R={preview.CurrentDepth.RightDepthM:F4} m.");
            Assert.That(preview.CurrentDepth.LeftDepthM, Is.LessThan(-SquatDepthGeometry.DefaultDepthMarginM));
            Assert.That(preview.CurrentDepth.RightDepthM, Is.LessThan(-SquatDepthGeometry.DefaultDepthMarginM));
            yield return null;
            CaptureEvidence("GAM-10-legal-bottom.png");

            preview.SetReviewPose(0.82f, SquatPhaseDirection.Ascent, SquatState.ASCENT);
            yield return null;
            CaptureEvidence("GAM-10-early-ascent.png");

            preview.SetReviewPose(0.64f, SquatPhaseDirection.Ascent, SquatState.STICKING);
            yield return null;
            CaptureEvidence("GAM-10-sticking.png");

            preview.SetReviewPose(0f, SquatPhaseDirection.Ascent, SquatState.LOCKOUT);
            yield return null;
            CaptureEvidence("GAM-10-lockout.png");

            preview.SetShowLandmarks(true);
            preview.SetReviewPose(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);
            yield return null;
            CaptureEvidence("GAM-10-depth-landmarks.png");

            preview.WriteMeasurementArtifact(Path.GetFullPath(MeasurementPath));
            Assert.That(File.Exists(Path.GetFullPath(MeasurementPath)), Is.True);
            Assert.That(new FileInfo(Path.GetFullPath(MeasurementPath)).Length, Is.GreaterThan(1024L));
            string measurement = File.ReadAllText(Path.GetFullPath(MeasurementPath));
            Assert.That(measurement, Does.Contain("CANONICAL_POWERLIFTING_SQUAT_V1"));
            Assert.That(measurement, Does.Contain("RULE_DERIVED_GAME_PROXY"));
            Assert.That(measurement, Does.Contain("leftDepthM"));
            Assert.That(measurement, Does.Contain("rightDepthM"));
            Assert.That(measurement, Does.Contain("referenceRootCorrection"));
            Assert.That(measurement, Does.Contain("physicalAuthorityTouched"));
        }

        [UnityTest]
        public IEnumerator GAM10_PREVIEW_TICK_IS_FIXED_AND_REFERENCE_STATE_IS_SAFE()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);

            preview.SetReviewPose(0f, SquatPhaseDirection.Descent, SquatState.DESCENT);
            PlayerIntentFrame intent = new PlayerIntentFrame(
                1ul,
                SimulationConstants.FixedDeltaTimeSeconds,
                IntentEdgeFlags.None,
                0f,
                1f,
                0f,
                0f,
                0f,
                false,
                true,
                false,
                false,
                false,
                false,
                false);
            for (int index = 0; index < 30; index++)
                preview.StepReferenceTick(intent);

            Assert.That(preview.SimulationTick, Is.EqualTo(30ul));
            Assert.That(preview.Phase, Is.GreaterThan(0f));
            Assert.That(preview.Phase, Is.LessThan(1f));
            Assert.That(Mathf.Abs(preview.PhaseRate), Is.LessThanOrEqualTo(SquatReferenceMotion.MaxPhaseRatePerSecond));
            Assert.That(preview.CurrentSample.BalanceCorrectionApplied, Is.False);
            Assert.That(preview.CurrentSample.BalanceIntentX, Is.EqualTo(0f));
            Assert.That(preview.ReferenceAnimator.enabled, Is.False);
            Assert.That(preview.ReferenceAnimator.applyRootMotion, Is.False);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM10_PREVIEW_SOURCE_GATES_REJECT_PHYSICAL_AND_CROSS_LIFT_AUTHORITY()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);
            Assert.That(preview.RigOwnership.PhysicalRigRoot, Is.Null);
            Assert.That(preview.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(preview.GetComponentsInChildren<Collider>(true), Is.Empty);

            string source = File.ReadAllText(Path.GetFullPath("Assets/Scripts/Squat/Unity/SquatReferencePreview.cs"));
            Assert.That(source, Does.Not.Contain("PhysicalAthleteRig"));
            Assert.That(source, Does.Not.Contain("Rigidbody"));
            Assert.That(source, Does.Not.Contain("MovePosition"));
            Assert.That(source, Does.Not.Contain("MoveRotation"));
            Assert.That(source, Does.Not.Contain("AddForce"));
            Assert.That(source, Does.Not.Contain("AddTorque"));
            Assert.That(source, Does.Not.Contain("PhysicsScene"));
            Assert.That(source, Does.Not.Contain("Bench"));
            Assert.That(source, Does.Not.Contain("Deadlift"));
            yield return null;
        }

        private static IEnumerator LoadQualificationScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The GAM-10 qualification scene is missing from the project.");
            while (!load.isDone)
                yield return null;
            yield return null;
        }

        private static void AssertReferenceOnlyTopology(SquatReferencePreview preview)
        {
            Assert.That(preview.Profile, Is.EqualTo(SquatReferencePreview.ProfileId));
            Assert.That(preview.ClaimClass, Is.EqualTo("BIOMECHANICALLY_INFORMED_GAME_CALIBRATION"));
            Assert.That(preview.ReferenceAnimator, Is.Not.Null);
            Assert.That(preview.ReferenceAnimator.avatar, Is.Not.Null);
            Assert.That(preview.ReferenceAnimator.avatar.isValid, Is.True);
            Assert.That(preview.ReferenceAnimator.avatar.isHuman, Is.True);
            Assert.That(preview.ReferenceAnimator.enabled, Is.False);
            Assert.That(preview.ReferenceAnimator.applyRootMotion, Is.False);
            Assert.That(preview.RigOwnership.PhysicalRigRoot, Is.Null);
            Assert.That(preview.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(preview.GetComponentsInChildren<Collider>(true), Is.Empty);
        }

        private static void PositionSideReviewCamera(Camera camera)
        {
            camera.fieldOfView = 34f;
            camera.transform.position = new Vector3(2.95f, 1.00f, 0.28f);
            camera.transform.LookAt(new Vector3(0f, 0.96f, 0f), Vector3.up);
        }

        private static void CaptureEvidence(string filename)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            string directory = Path.GetFullPath(EvidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            RenderTexture texture = RenderTexture.GetTemporary(1280, 720, 24, RenderTextureFormat.ARGB32);
            Texture2D image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = texture;
                camera.Render();
                RenderTexture.active = texture;
                image.ReadPixels(new Rect(0f, 0f, 1280f, 720f), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(texture);
                UnityEngine.Object.DestroyImmediate(image);
            }

            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(1024L), path);
        }
    }
}
