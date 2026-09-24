using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
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
        private const string EvidenceDirectory = "Artifacts/Evidence/GAM-10/V2-closed-chain";
        private const string MeasurementPath = "Artifacts/Measurements/GAM-10-squat-reference-v2.json";
        private const string CalibrationPath = "Artifacts/Measurements/GAM-10-squat-joint-frame-calibration-v2.json";
        private const float GAM49ReferenceParityToleranceM = 0.00001f;

        [UnityTest]
        public IEnumerator GAM10_CLOSED_CHAIN_REFERENCE_VISUAL_QUALIFICATION()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = FindPreview();
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "GAM-10 visual evidence requires a graphics device.");
            AssertReferenceOnlyTopology(preview);
            PositionSideReviewCamera(Camera.main);

            preview.SetShowReferenceBarGhost(true);
            preview.SetShowLandmarks(false);
            yield return CaptureReviewPose(preview, 0f, SquatPhaseDirection.None, SquatState.LOCKOUT,
                "GAM-10-V2-standing.png");
            yield return CaptureReviewPose(preview, 0.25f, SquatPhaseDirection.Descent, SquatState.DESCENT,
                "GAM-10-V2-quarter-descent.png");
            yield return CaptureReviewPose(preview, 0.55f, SquatPhaseDirection.Descent, SquatState.DESCENT,
                "GAM-10-V2-near-parallel.png");

            preview.SetReviewPose(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);
            AssertValidReference(preview);
            Assert.That(preview.LegalDepthWithPlantedFeet, Is.True,
                $"Legal bottom failed: L={preview.CurrentDepth.LeftDepthM:F4} m, " +
                $"R={preview.CurrentDepth.RightDepthM:F4} m.");
            Assert.That(preview.CurrentDepth.LeftDepthM,
                Is.LessThan(-SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M));
            Assert.That(preview.CurrentDepth.RightDepthM,
                Is.LessThan(-SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M));
            Assert.That(preview.FootAnchorsMaxErrorM,
                Is.LessThanOrEqualTo(SquatReferenceRigCalibration.FootAnchorToleranceM));
            Assert.That(preview.BilateralHipSolutionErrorM,
                Is.LessThanOrEqualTo(SquatReferenceRigCalibration.BilateralHipSolutionToleranceM));
            Assert.That(preview.SegmentLengthErrorM,
                Is.LessThanOrEqualTo(SquatReferenceRigCalibration.SegmentLengthToleranceM));
            yield return null;
            CaptureEvidence("GAM-10-V2-legal-bottom.png");

            yield return CaptureReviewPose(preview, 0.98f, SquatPhaseDirection.Ascent, SquatState.REVERSAL,
                "GAM-10-V2-reversal.png");
            yield return CaptureReviewPose(preview, 0.82f, SquatPhaseDirection.Ascent, SquatState.ASCENT,
                "GAM-10-V2-early-ascent.png");
            yield return CaptureReviewPose(preview, 0.64f, SquatPhaseDirection.Ascent, SquatState.STICKING,
                "GAM-10-V2-sticking.png");
            yield return CaptureReviewPose(preview, 0f, SquatPhaseDirection.Ascent, SquatState.LOCKOUT,
                "GAM-10-V2-lockout.png");

            preview.SetShowLandmarks(true);
            preview.SetReviewPose(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);
            AssertValidReference(preview);
            yield return null;
            CaptureEvidence("GAM-10-V2-depth-landmarks.png");

            SquatReferenceCalibrationReport report = preview.RunJointAxisCalibrationFixtures();
            Assert.That(report.Passed, Is.True, CalibrationFailure(report));
            for (int index = 0; index < report.Results.Count; index++)
            {
                SquatReferenceCalibrationFixtureResult result = report.Results[index];
                Assert.That(result.Passed, Is.True, result.Fixture.ToString());
                preview.SetCalibrationFixture(result.Fixture);
                AssertValidReference(preview);
                yield return null;
                CaptureEvidence($"GAM-10-V2-calibration-{FixtureFileToken(result.Fixture)}.png");
            }
            preview.ClearCalibrationFixture();

            preview.WriteCalibrationArtifact(Path.GetFullPath(CalibrationPath));
            preview.WriteMeasurementArtifact(Path.GetFullPath(MeasurementPath));
            AssertArtifact(CalibrationPath, "GAM10_SQUAT_JOINT_FRAME_CALIBRATION_V2",
                "GAM10_CANONICAL_QUATERNIUS_JOINT_FRAMES_V2", "visualProof");
            string measurement = ReadArtifact(MeasurementPath);
            Assert.That(measurement, Does.Contain("GAM10_SQUAT_REFERENCE_V2_CLOSED_CHAIN"));
            Assert.That(measurement, Does.Contain("CANONICAL_POWERLIFTING_SQUAT_V2_CLOSED_CHAIN"));
            Assert.That(measurement, Does.Contain("JOINT_AXIS_CALIBRATION"));
            Assert.That(measurement, Does.Contain("FOOT_ANCHORS_MAX_ERROR_MM"));
            Assert.That(measurement, Does.Contain("BILATERAL_HIP_SOLUTION_MAX_ERROR_MM"));
            Assert.That(measurement, Does.Contain("SEGMENT_LENGTH_ERROR"));
            Assert.That(measurement, Does.Contain("TRUNK_RELATIVE_ANGLE_ERROR_DEG"));
            Assert.That(measurement, Does.Contain("ROOT_BOUNDS_AUTHORITY"));
            Assert.That(measurement, Does.Contain("LEGAL_DEPTH_WITH_PLANTED_FEET"));
            Assert.That(measurement, Does.Contain("RENDER_RATE_INDEPENDENCE"));
            Assert.That(measurement, Does.Contain("BAR_SUPPORT_HAND_TARGETS"));
            Assert.That(measurement, Does.Not.Contain("referenceRootCorrection"));
            Assert.That(measurement, Does.Not.Contain("renderer bounds min.y"));
            Assert.That(measurement, Does.Contain("physicalAuthorityTouched"));
        }

        [UnityTest]
        public IEnumerator GAM49_SHARED_PROVIDER_PHASE_LADDER_AND_SIDE_VIEW_QUALIFICATION()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = FindPreview();
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(GraphicsDeviceType.Null),
                "GAM-49 side-view evidence requires a graphics device.");
            AssertReferenceOnlyTopology(preview);

            Camera camera = Camera.main;
            PositionExactSideReviewCamera(camera);
            preview.SetShowReferenceBarGhost(false);
            preview.SetShowLandmarks(true);

            float[] phases = { 0f, 0.25f, 0.55f, 0.80f, 1f, 0.64f };
            SquatPhaseDirection[] directions =
            {
                SquatPhaseDirection.None,
                SquatPhaseDirection.Descent,
                SquatPhaseDirection.Descent,
                SquatPhaseDirection.Descent,
                SquatPhaseDirection.Descent,
                SquatPhaseDirection.Ascent
            };
            SquatState[] states =
            {
                SquatState.LOCKOUT,
                SquatState.DESCENT,
                SquatState.DESCENT,
                SquatState.DESCENT,
                SquatState.BOTTOM,
                SquatState.STICKING
            };
            string[] labels = { "standing", "phase_0_25", "phase_0_55", "phase_0_80", "bottom", "ascent_0_64" };
            var receipt = new StringBuilder();
            receipt.AppendLine("MISSION=GAM49_SHARED_PROVIDER_REFERENCE_PHASE_LADDER");
            receipt.AppendLine("UNITY=6000.3.22f1");
            receipt.AppendLine("CALIBRATION_ID=" + SquatReferenceRigCalibration.CalibrationId);
            receipt.AppendLine("TOLERANCE_M=" + GAM49ReferenceParityToleranceM.ToString("R", CultureInfo.InvariantCulture));
            receipt.AppendLine("GAME_JUDGMENT_MARGIN_M=" + SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M.ToString("R", CultureInfo.InvariantCulture));
            receipt.AppendLine("IPF_RULE_PREDICATE=both hip-crease proxies below their corresponding knee-top proxies");
            receipt.AppendLine("GAME_MARGIN_PROVENANCE=GAME_JUDGMENT_MARGIN_M; not IPF-prescribed");

            for (int index = 0; index < phases.Length; index++)
            {
                preview.SetReviewPose(phases[index], directions[index], states[index]);
                AssertValidReference(preview);
                SquatReferenceKinematicSolution solution = preview.CurrentSolution;
                SquatReferenceRigCalibration calibration = preview.Calibration;
                SquatRuleLandmarkSet current = preview.CurrentRuleLandmarks;
                SquatRuleLandmarkSet repeated = EvaluateProvider(solution, calibration);
                SquatDepthObservation legacy = EvaluateExistingGAM10Depth(solution, calibration);
                AssertDepthParity(labels[index], legacy, current.Depth);
                AssertDepthParity(labels[index] + "_REPEAT", current.Depth, repeated.Depth, 1e-7f);
                AssertLandmarkSetFinite(current);
                AssertLandmarkSetMatches(current, repeated, 1e-7f);

                receipt.Append("STATE=").Append(labels[index]);
                receipt.Append(" PHASE=").Append(phases[index].ToString("R", CultureInfo.InvariantCulture));
                receipt.Append(" D_LEGACY_L_M=").Append(F(legacy.LeftDepthM));
                receipt.Append(" D_PROVIDER_L_M=").Append(F(current.Depth.LeftDepthM));
                receipt.Append(" D_LEGACY_R_M=").Append(F(legacy.RightDepthM));
                receipt.Append(" D_PROVIDER_R_M=").Append(F(current.Depth.RightDepthM));
                receipt.Append(" WORST_SIDE_M=").Append(F(current.Depth.WorstSideDepthM));
                receipt.Append(" IPF_RULE_PREDICATE=").Append(current.Depth.IPFRulePredicateSatisfied ? "true" : "false");
                receipt.Append(" GAME_JUDGMENT_QUALIFIED=").Append(current.Depth.BilateralGameJudgmentQualified ? "true" : "false");
                receipt.AppendLine(" POINTS_FINITE=true DETERMINISTIC=true");

                if (states[index] == SquatState.BOTTOM)
                {
                    Assert.That(current.Depth.BilateralGameJudgmentQualified, Is.True,
                        $"GAM-10 bottom lost the fixed game margin: L={current.Depth.LeftDepthM:R}, R={current.Depth.RightDepthM:R}");
                    Assert.That(current.Depth.IPFRulePredicateSatisfied, Is.True);
                    yield return null;
                    CaptureEvidenceAt("Artifacts/Evidence/GAM-49", "GAM49-depth-landmarks-side.png");
                }
            }

            receipt.AppendLine("VISUAL_EVIDENCE=Artifacts/Evidence/GAM-49/GAM49-depth-landmarks-side.png");
            receipt.AppendLine("CLAIM_CEILING=CALIBRATED_BONE_FRAME_PROXY_ON_CANONICAL_HUMANOID");
            string receiptPath = Path.GetFullPath("Artifacts/Measurements/GAM-49/gate2-reference-parity.md");
            Directory.CreateDirectory(Path.GetDirectoryName(receiptPath));
            File.WriteAllText(receiptPath, receipt.ToString());
            Assert.That(File.Exists(receiptPath), Is.True);
            Assert.That(new FileInfo(receiptPath).Length, Is.GreaterThan(1024L));
        }

        [UnityTest]
        public IEnumerator GAM10_REFERENCE_TICK_IS_FIXED_AND_REFERENCE_STATE_IS_SAFE()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = FindPreview();
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
            Vector3 rootBefore = preview.ReferenceRoot.position;
            for (int index = 0; index < 30; index++)
                preview.StepReferenceTick(intent);

            Assert.That(preview.SimulationTick, Is.EqualTo(30ul));
            Assert.That(preview.Phase, Is.GreaterThan(0f));
            Assert.That(preview.Phase, Is.LessThan(1f));
            Assert.That(Mathf.Abs(preview.PhaseRate),
                Is.LessThanOrEqualTo(SquatReferenceMotion.MaxPhaseRatePerSecond));
            Assert.That(preview.CurrentSample.BalanceCorrectionApplied, Is.False);
            Assert.That(preview.CurrentSample.BalanceIntentX, Is.EqualTo(0f));
            Assert.That(preview.ReferencePoseValid, Is.True);
            Assert.That(preview.ReferenceAnimator.enabled, Is.False);
            Assert.That(preview.ReferenceAnimator.applyRootMotion, Is.False);
            Assert.That(Vector3.Distance(rootBefore, preview.ReferenceRoot.position), Is.LessThan(0.000001f));
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM10_RENDER_RATE_INDEPENDENCE_AND_ROOT_AUTHORITY_GATES_PASS()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = FindPreview();
            Assert.That(preview.RootBoundsAuthorityValue, Is.EqualTo("ABSENT"));
            Assert.That(preview.PoseRootSource, Does.Contain("plantar"));

            preview.SetReviewPose(0.55f, SquatPhaseDirection.Descent, SquatState.DESCENT);
            Vector3 firstPelvis = preview.CurrentSolution.PelvisCenter;
            Vector3 firstLeftHip = preview.CurrentSolution.LeftLeg.HipCenter;
            preview.SetReviewPose(0.55f, SquatPhaseDirection.Descent, SquatState.DESCENT);
            Vector3 secondPelvis = preview.CurrentSolution.PelvisCenter;
            Vector3 secondLeftHip = preview.CurrentSolution.LeftLeg.HipCenter;

            Assert.That(Vector3.Distance(firstPelvis, secondPelvis), Is.LessThan(0.000001f));
            Assert.That(Vector3.Distance(firstLeftHip, secondLeftHip), Is.LessThan(0.000001f));
            Assert.That(preview.FeetPlanted, Is.True);
            Assert.That(preview.ReferencePoseValid, Is.True);
            yield return null;
        }

        [UnityTest]
        public IEnumerator GAM10_SOURCE_GATES_REJECT_PHYSICAL_AND_BOUNDS_AUTHORITY()
        {
            yield return LoadQualificationScene();
            SquatReferencePreview preview = FindPreview();
            Assert.That(preview.RigOwnership.PhysicalRigRoot, Is.Null);
            Assert.That(preview.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            Assert.That(preview.GetComponentsInChildren<Collider>(true), Is.Empty);

            string previewSource = File.ReadAllText(Path.GetFullPath(
                "Assets/Scripts/Squat/Unity/SquatReferencePreview.cs"));
            string kinematicsSource = File.ReadAllText(Path.GetFullPath(
                "Assets/Scripts/Squat/Unity/SquatReferenceKinematics.cs"));
            Assert.That(previewSource, Does.Not.Contain("PhysicalAthleteRig"));
            Assert.That(previewSource, Does.Not.Contain("Rigidbody"));
            Assert.That(previewSource, Does.Not.Contain("MovePosition"));
            Assert.That(previewSource, Does.Not.Contain("MoveRotation"));
            Assert.That(previewSource, Does.Not.Contain("AddForce"));
            Assert.That(previewSource, Does.Not.Contain("AddTorque"));
            Assert.That(previewSource, Does.Not.Contain("PhysicsScene"));
            Assert.That(previewSource, Does.Not.Contain("Bench"));
            Assert.That(previewSource, Does.Not.Contain("Deadlift"));
            Assert.That(previewSource, Does.Not.Contain("CalculateBounds"));
            Assert.That(previewSource, Does.Not.Contain("Renderer.bounds"));
            Assert.That(previewSource, Does.Not.Contain("referenceRootCorrection"));
            Assert.That(kinematicsSource, Does.Not.Contain("Renderer.bounds"));
            Assert.That(kinematicsSource, Does.Not.Contain("CalculateBounds"));
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

        private static SquatReferencePreview FindPreview()
        {
            SquatReferencePreview preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);
            return preview;
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
            Assert.That(preview.RootBoundsAuthorityValue, Is.EqualTo("ABSENT"));
            Assert.That(preview.ReferencePoseValid, Is.True);
            Assert.That(preview.FeetPlanted, Is.True);
        }

        private static void AssertValidReference(SquatReferencePreview preview)
        {
            Assert.That(preview.ReferencePoseValid, Is.True,
                preview.CurrentSolution == null ? "No kinematic solution." :
                preview.CurrentSolution.RejectionReason);
            Assert.That(preview.FeetPlanted, Is.True);
        }

        private static IEnumerator CaptureReviewPose(
            SquatReferencePreview preview,
            float phase,
            SquatPhaseDirection direction,
            SquatState state,
            string filename)
        {
            preview.SetReviewPose(phase, direction, state);
            AssertValidReference(preview);
            yield return null;
            CaptureEvidence(filename);
        }

        private static void PositionSideReviewCamera(Camera camera)
        {
            Assert.That(camera, Is.Not.Null);
            camera.fieldOfView = 34f;
            camera.transform.position = new Vector3(3.60f, 1.04f, 1.25f);
            camera.transform.LookAt(new Vector3(0f, 0.96f, 0f), Vector3.up);
        }

        private static void PositionExactSideReviewCamera(Camera camera)
        {
            Assert.That(camera, Is.Not.Null);
            camera.fieldOfView = 34f;
            camera.transform.position = new Vector3(3.60f, 1.04f, 0f);
            camera.transform.LookAt(new Vector3(0f, 0.96f, 0f), Vector3.up);
        }

        private static void CaptureEvidence(string filename)
        {
            CaptureEvidenceAt(EvidenceDirectory, filename);
        }

        private static void CaptureEvidenceAt(string evidenceDirectory, string filename)
        {
            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            string directory = Path.GetFullPath(evidenceDirectory);
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, filename);
            RenderTexture texture = RenderTexture.GetTemporary(
                1280, 720, 24, RenderTextureFormat.ARGB32);
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

        private static SquatRuleLandmarkSet EvaluateProvider(
            SquatReferenceKinematicSolution solution,
            SquatReferenceRigCalibration calibration)
        {
            var provider = new SquatDepthLandmarkProvider(calibration);
            Assert.That(provider.TryEvaluate(
                solution.LeftLeg.HipCenter,
                solution.RightLeg.HipCenter,
                solution.PelvisFrameRotation,
                solution.LeftLeg.KneeCenter,
                solution.LeftLeg.ShankFrameRotation,
                solution.RightLeg.KneeCenter,
                solution.RightLeg.ShankFrameRotation,
                out SquatRuleLandmarkSet landmarks), Is.True);
            return landmarks;
        }

        private static SquatDepthObservation EvaluateExistingGAM10Depth(
            SquatReferenceKinematicSolution solution,
            SquatReferenceRigCalibration calibration)
        {
            Vector3 leftHipCrease = solution.PelvisCenter +
                solution.PelvisFrameRotation * calibration.LeftHipCreaseOffsetInPelvisFrame;
            Vector3 rightHipCrease = solution.PelvisCenter +
                solution.PelvisFrameRotation * calibration.RightHipCreaseOffsetInPelvisFrame;
            Vector3 leftKneeTop = solution.LeftLeg.KneeCenter +
                solution.LeftLeg.ShankFrameRotation * calibration.LeftKneeTopOffsetInShankFrame;
            Vector3 rightKneeTop = solution.RightLeg.KneeCenter +
                solution.RightLeg.ShankFrameRotation * calibration.RightKneeTopOffsetInShankFrame;
            return SquatDepthGeometry.Evaluate(
                new SquatPoint3(leftHipCrease.x, leftHipCrease.y, leftHipCrease.z),
                new SquatPoint3(rightHipCrease.x, rightHipCrease.y, rightHipCrease.z),
                new SquatPoint3(leftKneeTop.x, leftKneeTop.y, leftKneeTop.z),
                new SquatPoint3(rightKneeTop.x, rightKneeTop.y, rightKneeTop.z),
                SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M);
        }

        private static void AssertDepthParity(
            string state,
            SquatDepthObservation expected,
            SquatDepthObservation actual) =>
            AssertDepthParity(state, expected, actual, GAM49ReferenceParityToleranceM);

        private static void AssertDepthParity(
            string state,
            SquatDepthObservation expected,
            SquatDepthObservation actual,
            float toleranceM)
        {
            Assert.That(Mathf.Abs(actual.LeftDepthM - expected.LeftDepthM), Is.LessThanOrEqualTo(toleranceM),
                state + " left reference/provider parity");
            Assert.That(Mathf.Abs(actual.RightDepthM - expected.RightDepthM), Is.LessThanOrEqualTo(toleranceM),
                state + " right reference/provider parity");
        }

        private static void AssertLandmarkSetFinite(SquatRuleLandmarkSet landmarks)
        {
            AssertVectorFinite(landmarks.LeftHipCreaseWorld);
            AssertVectorFinite(landmarks.RightHipCreaseWorld);
            AssertVectorFinite(landmarks.LeftKneeTopWorld);
            AssertVectorFinite(landmarks.RightKneeTopWorld);
        }

        private static void AssertLandmarkSetMatches(
            SquatRuleLandmarkSet expected,
            SquatRuleLandmarkSet actual,
            float toleranceM)
        {
            AssertVectorMatches(expected.LeftHipCreaseWorld, actual.LeftHipCreaseWorld, toleranceM);
            AssertVectorMatches(expected.RightHipCreaseWorld, actual.RightHipCreaseWorld, toleranceM);
            AssertVectorMatches(expected.LeftKneeTopWorld, actual.LeftKneeTopWorld, toleranceM);
            AssertVectorMatches(expected.RightKneeTopWorld, actual.RightKneeTopWorld, toleranceM);
            AssertDepthParity("repeat", expected.Depth, actual.Depth, toleranceM);
        }

        private static void AssertVectorFinite(Vector3 value)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False);
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False);
            Assert.That(float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False);
        }

        private static void AssertVectorMatches(Vector3 expected, Vector3 actual, float toleranceM)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(toleranceM));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(toleranceM));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(toleranceM));
        }

        private static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        private static void AssertArtifact(string relativePath, params string[] fragments)
        {
            string text = ReadArtifact(relativePath);
            for (int index = 0; index < fragments.Length; index++)
                Assert.That(text, Does.Contain(fragments[index]), fragments[index]);
        }

        private static string ReadArtifact(string relativePath)
        {
            string path = Path.GetFullPath(relativePath);
            Assert.That(File.Exists(path), Is.True, path);
            Assert.That(new FileInfo(path).Length, Is.GreaterThan(1024L), path);
            return File.ReadAllText(path);
        }

        private static string CalibrationFailure(SquatReferenceCalibrationReport report)
        {
            if (report.Results == null)
                return "No calibration fixture results.";
            string message = string.Empty;
            for (int index = 0; index < report.Results.Count; index++)
            {
                SquatReferenceCalibrationFixtureResult result = report.Results[index];
                if (!result.Passed)
                    message += $"{result.Fixture}: expected {result.ExpectedDegrees:F3}, " +
                        $"measured {result.MeasuredDegrees:F3}, error {result.ErrorDegrees:F3}; ";
            }
            return message;
        }

        private static string FixtureFileToken(SquatReferenceCalibrationFixture fixture)
        {
            return fixture.ToString().Replace("Plus", "-plus-").ToLowerInvariant();
        }
    }
}
