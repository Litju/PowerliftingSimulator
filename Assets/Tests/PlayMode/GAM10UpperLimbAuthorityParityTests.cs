using System.Collections;
using NUnit.Framework;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// The physical adapter has no posed reference rig, so it feeds
    /// <see cref="SquatReferenceUpperLimb"/> shoulder and thorax positions
    /// propagated analytically down the bind hierarchy. The rendered reference
    /// preview feeds the same solver the positions its live rig actually
    /// carries.
    ///
    /// If those two disagree the physical athlete would track an arm pose the
    /// owner never accepted, while every downstream parity check still passed.
    /// This measures the disagreement directly.
    /// </summary>
    public sealed class GAM10UpperLimbAuthorityParityTests
    {
        private const string PreviewScene = "SquatReferencePreview";
        private const float PositionToleranceM = 0.001f;
        private const float RotationToleranceDeg = 0.25f;

        [UnityTest]
        public IEnumerator ANALYTIC_UPPER_LIMB_SOLVE_MATCHES_THE_POSED_REFERENCE_RIG()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(PreviewScene, LoadSceneMode.Single);
            while (!load.isDone)
                yield return null;
            yield return null;

            SquatReferencePreview preview = Object.FindFirstObjectByType<SquatReferencePreview>();
            Assert.That(preview, Is.Not.Null, "The reference preview scene has no preview component.");

            Animator animator = preview.GetComponentInChildren<Animator>();
            SquatReferenceRigCalibration calibration = SquatReferenceRigCalibration.Build(
                animator,
                animator.transform.root,
                "Assets/Scenes/Prototype/SquatReferencePreview.unity");
            Transform leftUpperArm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
            Transform rightUpperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform upperChest = animator.GetBoneTransform(HumanBodyBones.UpperChest);

            foreach (float phase in new[] { 0f, 0.25f, 0.55f, 1f })
            {
                preview.SetReviewPose(phase, SquatPhaseDirection.Descent,
                    phase <= 0f ? SquatState.LOCKOUT : SquatState.DESCENT);
                yield return null;

                SquatReferenceKinematicSolution solution = SquatReferenceKinematics.Solve(
                    calibration,
                    SquatReferenceProfile.CanonicalPowerliftingSquatV1.Evaluate(phase, SquatPhaseDirection.Descent),
                    calibration.LeftFoot.PlantarAnchorWorld,
                    calibration.RightFoot.PlantarAnchorWorld);
                Assert.That(solution.IsValid, Is.True, solution.RejectionReason);

                Assert.That(
                    Vector3.Distance(SquatReferenceUpperLimb.UpperChestBonePosition(calibration, solution),
                        upperChest.position),
                    Is.LessThanOrEqualTo(PositionToleranceM),
                    $"Analytic thorax position diverges from the posed rig at s_q={phase:F2}.");

                SquatReferenceUpperLimbSolution analytic = SquatReferenceUpperLimb.Solve(calibration, solution);
                SquatReferenceUpperLimbSolution live = SquatReferenceUpperLimb.Solve(
                    calibration, solution, upperChest.position, leftUpperArm.position, rightUpperArm.position);

                AssertSideParity(analytic.Left, live.Left, "left", phase);
                AssertSideParity(analytic.Right, live.Right, "right", phase);
            }
        }

        private static void AssertSideParity(
            SquatReferenceUpperLimbSide analytic,
            SquatReferenceUpperLimbSide live,
            string side,
            float phase)
        {
            Assert.That(Vector3.Distance(analytic.ShoulderCenter, live.ShoulderCenter),
                Is.LessThanOrEqualTo(PositionToleranceM),
                $"{side} shoulder root diverges at s_q={phase:F2}.");
            Assert.That(Quaternion.Angle(analytic.UpperArmBoneRotation, live.UpperArmBoneRotation),
                Is.LessThanOrEqualTo(RotationToleranceDeg),
                $"{side} upper arm diverges at s_q={phase:F2}.");
            Assert.That(Quaternion.Angle(analytic.ForearmBoneRotation, live.ForearmBoneRotation),
                Is.LessThanOrEqualTo(RotationToleranceDeg),
                $"{side} forearm diverges at s_q={phase:F2}.");
            Assert.That(Quaternion.Angle(analytic.HandBoneRotation, live.HandBoneRotation),
                Is.LessThanOrEqualTo(RotationToleranceDeg),
                $"{side} hand diverges at s_q={phase:F2}.");
        }
    }
}
