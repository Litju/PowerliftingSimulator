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
        public IEnumerator GAM48_REFERENCE_SOLVER_PARITY_ONLY()
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
            Assert.That(reference.BilateralLegalReference, Is.True);
            Assert.That(reconstructed.BilateralLegalReference, Is.True);

            string path = Path.GetFullPath("Artifacts/Measurements/GAM-48/gate4-reference-solver-parity.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path,
                "MISSION=GAM48_REFERENCE_SOLVER_PARITY_ONLY\n" +
                "REFERENCE_PREVIEW_LEFT_DEPTH_M=" + reference.LeftDepthM.ToString("R") + "\n" +
                "REFERENCE_PREVIEW_RIGHT_DEPTH_M=" + reference.RightDepthM.ToString("R") + "\n" +
                "REFERENCE_PREVIEW_LEGAL=true\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_LEFT_DEPTH_M=" + reconstructed.LeftDepthM.ToString("R") + "\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_RIGHT_DEPTH_M=" + reconstructed.RightDepthM.ToString("R") + "\n" +
                "REFERENCE_SOLVER_RECONSTRUCTION_LEGAL=true\n" +
                "DYNAMICS_INCLUDED=false\n" +
                "PHYSICAL_ADAPTER_MAPPING_INCLUDED=false\n" +
                "TOLERANCE_M=1e-5\n" +
                "CLAIM_CEILING=REFERENCE_SOLVER_RECONSTRUCTION_PARITY_ONLY\n");
            yield return null;
        }

        private static SquatDepthObservation DepthFromSolution(
            SquatReferenceKinematicSolution solution,
            SquatReferenceRigCalibration calibration)
        {
            UnityEngine.Vector3 leftHip = solution.PelvisCenter +
                solution.PelvisFrameRotation * calibration.LeftHipCreaseOffsetInPelvisFrame;
            UnityEngine.Vector3 rightHip = solution.PelvisCenter +
                solution.PelvisFrameRotation * calibration.RightHipCreaseOffsetInPelvisFrame;
            UnityEngine.Vector3 leftKnee = solution.LeftLeg.KneeCenter +
                solution.LeftLeg.ShankFrameRotation * calibration.LeftKneeTopOffsetInShankFrame;
            UnityEngine.Vector3 rightKnee = solution.RightLeg.KneeCenter +
                solution.RightLeg.ShankFrameRotation * calibration.RightKneeTopOffsetInShankFrame;
            return SquatDepthGeometry.Evaluate(
                new SquatPoint3(leftHip.x, leftHip.y, leftHip.z),
                new SquatPoint3(rightHip.x, rightHip.y, rightHip.z),
                new SquatPoint3(leftKnee.x, leftKnee.y, leftKnee.z),
                new SquatPoint3(rightKnee.x, rightKnee.y, rightKnee.z));
        }
    }
}
