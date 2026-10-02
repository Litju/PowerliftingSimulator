using System.Collections;
using System.Globalization;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Foundation.Unity;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    public sealed class GAM13V2JointLimitMappingTests
    {
        private const string QualificationScene = "SquatPhysicalPrototype";

        [UnityTest]
        public IEnumerator GAM13_V2_CANONICAL_MAPPED_STATES_RETAIN_FIXED_HEADROOM()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(QualificationScene, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, "The canonical squat scene is missing.");
            while (!load.isDone)
                yield return null;
            yield return null;

            FoundationBootstrap bootstrap = Object.FindFirstObjectByType<FoundationBootstrap>();
            PhysicalAthleteRig rig = Object.FindFirstObjectByType<PhysicalAthleteRig>();
            SquatPhysicalPrototypeController controller = Object.FindFirstObjectByType<SquatPhysicalPrototypeController>();
            Assert.That(bootstrap, Is.Not.Null);
            Assert.That(rig, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.IsInitialized, Is.True, controller.StartupFailure);
            controller.enabled = false;
            bootstrap.enabled = false;

            string[] jointIds =
            {
                "left_foot", "right_foot", "left_shank", "right_shank",
                "left_thigh", "right_thigh", "abdomen", "thorax",
                "head_neck", "left_upper_arm", "right_upper_arm",
                "left_forearm", "right_forearm", "left_hand", "right_hand"
            };
            var minimumMargins = new float[jointIds.Length];
            var minimumSecondaryMargins = new float[jointIds.Length];
            var criticalAngles = new float[jointIds.Length];
            var criticalPhases = new float[jointIds.Length];
            for (int jointIndex = 0; jointIndex < jointIds.Length; jointIndex++)
            {
                minimumMargins[jointIndex] = float.PositiveInfinity;
                minimumSecondaryMargins[jointIndex] = float.PositiveInfinity;
            }

            for (int phaseIndex = 0; phaseIndex <= 100; phaseIndex++)
            {
                float phase = phaseIndex / 100f;
                for (int jointIndex = 0; jointIndex < jointIds.Length; jointIndex++)
                {
                    string jointId = jointIds[jointIndex];
                    PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointId);
                    Assert.That(joint, Is.Not.Null, jointId);
                    Quaternion target = jointId == "head_neck"
                        ? Quaternion.identity
                        : controller.Adapter.ReferenceLogicalTarget(jointId, phase, SquatPhaseDirection.Descent);
                    float angle = PoweredJointController.SignedTwistRadians(target, Vector3.right) * Mathf.Rad2Deg;
                    float margin = angle >= 0f
                        ? joint.Recipe.HighDegrees - angle
                        : angle - joint.Recipe.LowDegrees;
                    if (margin < minimumMargins[jointIndex])
                    {
                        minimumMargins[jointIndex] = margin;
                        criticalAngles[jointIndex] = angle;
                        criticalPhases[jointIndex] = phase;
                    }

                    if (joint.Recipe.Kind != PhysicalJointKind.Hinge && joint.Recipe.SecondaryLimitDegrees > 0f)
                    {
                        float y = PoweredJointController.SignedTwistRadians(target, Vector3.up) * Mathf.Rad2Deg;
                        float z = PoweredJointController.SignedTwistRadians(target, Vector3.forward) * Mathf.Rad2Deg;
                        float secondaryMargin = joint.Recipe.SecondaryLimitDegrees - Mathf.Max(Mathf.Abs(y), Mathf.Abs(z));
                        minimumSecondaryMargins[jointIndex] = Mathf.Min(minimumSecondaryMargins[jointIndex], secondaryMargin);
                        Assert.That(secondaryMargin, Is.GreaterThanOrEqualTo(10f),
                            $"{jointId} secondary headroom {secondaryMargin:F2} deg at phase {phase:F2}.");
                    }
                }
            }

            var report = new StringBuilder("GAM13_V2_JOINT_LIMIT_HEADROOM");
            for (int jointIndex = 0; jointIndex < jointIds.Length; jointIndex++)
            {
                PoweredJointController.PoweredJointRuntime joint = rig.PoweredController.GetJoint(jointIds[jointIndex]);
                float requiredMargin = joint.Recipe.Family == "hip" ? 20f : 10f;
                Assert.That(minimumMargins[jointIndex], Is.GreaterThanOrEqualTo(requiredMargin),
                    $"{joint.Id} has {minimumMargins[jointIndex]:F2} deg headroom; needs {requiredMargin:F1} deg.");
                if (joint.Recipe.SecondaryLimitDegrees > 0f)
                    Assert.That(minimumSecondaryMargins[jointIndex], Is.GreaterThanOrEqualTo(10f), joint.Id);
                report.AppendFormat(CultureInfo.InvariantCulture,
                    "{0}: primary={1:F2}deg at s={2:F2} target={3:F2}deg bounds=[{4:F1},{5:F1}]",
                    joint.Id, minimumMargins[jointIndex], criticalPhases[jointIndex], criticalAngles[jointIndex],
                    joint.Recipe.LowDegrees, joint.Recipe.HighDegrees);
                if (joint.Recipe.SecondaryLimitDegrees > 0f)
                    report.AppendFormat(CultureInfo.InvariantCulture,
                        " secondary={0:F2}deg bound=±{1:F1}deg",
                        minimumSecondaryMargins[jointIndex], joint.Recipe.SecondaryLimitDegrees);
                report.AppendLine();
            }
            Debug.Log(report.ToString());
        }
    }
}
