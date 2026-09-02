using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class PoweredJointControllerTests
    {
        [Test]
        public void TARGET_ROTATION_NEUTRAL_IS_STABLE_AND_QUATERNION_SIGN_IS_EQUIVALENT()
        {
            Assert.That(PoweredJointController.ToUnityTargetRotation(Quaternion.identity), Is.EqualTo(Quaternion.identity));

            Quaternion target = Quaternion.AngleAxis(37f, new Vector3(1f, 2f, -3f).normalized);
            Quaternion negative = new Quaternion(-target.x, -target.y, -target.z, -target.w);
            Quaternion converted = PoweredJointController.ToUnityTargetRotation(target);
            Quaternion convertedNegative = PoweredJointController.ToUnityTargetRotation(negative);

            Assert.That(Quaternion.Angle(converted, convertedNegative), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(
                PoweredJointController.RateLimitShortestArc(Quaternion.identity, target, 0.05f),
                PoweredJointController.RateLimitShortestArc(Quaternion.identity, negative, 0.05f)), Is.LessThan(0.0001f));
        }

        [Test]
        public void POWERED_DRIVE_VALIDATOR_REJECTS_ACCELERATION_NONFINITE_AND_NEGATIVE_AUTHORITY()
        {
            JointDrive valid = new JointDrive
            {
                positionSpring = 1f,
                positionDamper = 1f,
                maximumForce = 0f,
                useAcceleration = false
            };
            Assert.That(PoweredJointController.IsValidPoweredDrive(valid), Is.True);

            JointDrive acceleration = valid;
            acceleration.useAcceleration = true;
            Assert.That(PoweredJointController.IsValidPoweredDrive(acceleration), Is.False);

            JointDrive infinite = valid;
            infinite.maximumForce = float.PositiveInfinity;
            Assert.That(PoweredJointController.IsValidPoweredDrive(infinite), Is.False);

            JointDrive negative = valid;
            negative.maximumForce = -1f;
            Assert.That(PoweredJointController.IsValidPoweredDrive(negative), Is.False);
        }
    }
}
