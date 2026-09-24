using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM48DepthDecompositionTests
    {
        [UnityTest]
        public IEnumerator GAM49_SHARED_PROVIDER_REFERENCE_PARITY()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("SquatReferencePreview", LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            while (!load.isDone)
                yield return null;
            yield return null;

            SquatReferencePreview preview = UnityEngine.Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null);
            preview.SetReviewPose(1f, SquatPhaseDirection.Descent, SquatState.BOTTOM);

            SquatDepthObservation reference = preview.CurrentDepth;
            SquatReferenceRigCalibration calibration = preview.Calibration;
            SquatReferencePose pose = SquatReferenceProfile.CanonicalPowerliftingSquatV1
                .Evaluate(1f, SquatPhaseDirection.Descent);
            SquatReferenceKinematicSolution reconstructedSolution = SquatReferenceKinematics.Solve(
                calibration,
                pose,
                calibration.LeftFoot.PlantarAnchorWorld,
                calibration.RightFoot.PlantarAnchorWorld);
            Assert.That(reconstructedSolution.IsValid, Is.True, reconstructedSolution.RejectionReason);

            SquatDepthObservation reconstructed = DepthFromSolution(reconstructedSolution, calibration);
            Assert.That(reconstructed.LeftDepthM, Is.EqualTo(reference.LeftDepthM).Within(1e-5f));
            Assert.That(reconstructed.RightDepthM, Is.EqualTo(reference.RightDepthM).Within(1e-5f));
            Assert.That(reference.BilateralGameJudgmentQualified, Is.True);
            Assert.That(reconstructed.BilateralGameJudgmentQualified, Is.True);
            Assert.That(reference.IPFRulePredicateSatisfied, Is.True);
            Assert.That(reconstructed.IPFRulePredicateSatisfied, Is.True);

            string path = Path.GetFullPath("Artifacts/Measurements/GAM-49/gate1-reference-parity.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "MISSION=GAM49_SHARED_PROVIDER_REFERENCE_PARITY\n" +
                "REFERENCE_PREVIEW_LEFT_DEPTH_M=" + reference.LeftDepthM.ToString("R") + "\n" +
                "REFERENCE_PREVIEW_RIGHT_DEPTH_M=" + reference.RightDepthM.ToString("R") + "\n" +
                "REFERENCE_PREVIEW_GAME_JUDGMENT_QUALIFIED=true\n" +
                "REFERENCE_PREVIEW_IPF_RULE_PREDICATE=true\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_LEFT_DEPTH_M=" + reconstructed.LeftDepthM.ToString("R") + "\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_RIGHT_DEPTH_M=" + reconstructed.RightDepthM.ToString("R") + "\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_GAME_JUDGMENT_QUALIFIED=true\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_IPF_RULE_PREDICATE=true\n" +
                "DYNAMICS_INCLUDED=false\n" +
                "PHYSICAL_ADAPTER_MAPPING_INCLUDED=false\n" +
                "TOLERANCE_M=1e-5\n" +
                "GAME_JUDGMENT_MARGIN_M=" + SquatDepthGeometry.GAME_JUDGMENT_MARGIN_M.ToString("R") + "\n" +
                "CLAIM_CEILING=GAM10_CALIBRATED_SURFACE_PROXY_REFERENCE_PARITY\n");
            yield return null;
        }

        private static SquatDepthObservation DepthFromSolution(
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
            return landmarks.Depth;
        }
    }
}
