using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat.Unity;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PowerliftingSimulator.Tests.EditMode
{
    public sealed class GAM68PalmSurfaceCalibrationTests
    {
        private const string ScenePath = "Assets/Scenes/Prototype/SquatPhysicalPrototype.unity";

        [Test]
        public void PALM_SURFACE_CONSTANTS_MATCH_THE_RENDERED_SKIN()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            Assert.That(rig, Is.Not.Null);
            Assert.That(rig.VisibleAnimator, Is.Not.Null);

            AssertHand(rig.VisibleAnimator, true,
                SquatPalmSurface.LeftPalmCenterLocal,
                SquatPalmSurface.LeftPalmNormalLocal);
            AssertHand(rig.VisibleAnimator, false,
                SquatPalmSurface.RightPalmCenterLocal,
                SquatPalmSurface.RightPalmNormalLocal);
        }

        private static void AssertHand(
            Animator animator,
            bool isLeft,
            Vector3 expectedCenter,
            Vector3 expectedNormal)
        {
            bool measured = SquatPalmSurface.TryMeasureFromRenderedMesh(
                animator, isLeft, out SquatPalmSurfaceFrame frame, out string reason);
            Assert.That(measured, Is.True, reason);
            Assert.That(
                Vector3.Distance(frame.CenterLocal, expectedCenter),
                Is.LessThan(0.0015f),
                $"{(isLeft ? "Left" : "Right")} measured palm center drifted from the runtime calibration.");
            Assert.That(
                Mathf.Abs(Vector3.Dot(frame.NormalLocal, expectedNormal)),
                Is.GreaterThan(0.999f),
                $"{(isLeft ? "Left" : "Right")} measured palm normal drifted from the runtime calibration.");
            Assert.That(frame.SpanMeters, Is.EqualTo(SquatPalmSurface.PalmSpanMeters).Within(0.002f));
        }
    }
}
