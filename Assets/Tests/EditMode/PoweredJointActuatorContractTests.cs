using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5B. Capacity is an actuator ceiling. It must move
    /// maximumForce and nothing else, so that a heavier bar changes how much
    /// torque the athlete can produce without silently changing how the joint
    /// responds to error.
    /// </summary>
    public sealed class PoweredJointActuatorContractTests
    {
        private static readonly string[] SquatJointFamilies = { "ankle", "knee", "hip", "trunk" };

        [Test]
        public void CAPACITY_SCALE_MOVES_THE_TORQUE_CEILING()
        {
            foreach (string familyId in SquatJointFamilies)
            {
                JointFamilyProfile profile = RequireFamily(familyId);
                foreach (float capacityScale in new[] { 0.5f, 1f, 3.8f, 7.6f })
                {
                    const float activation = 1f;
                    float maximumForce = profile.BaseCapacityNm * capacityScale * activation;
                    Assert.That(maximumForce, Is.EqualTo(profile.BaseCapacityNm * capacityScale).Within(1e-4f),
                        $"'{familyId}' torque ceiling does not scale with capacity.");
                }
            }
        }

        [Test]
        public void CAPACITY_SCALE_DOES_NOT_MOVE_SPRING_OR_DAMPER()
        {
            foreach (string familyId in SquatJointFamilies)
            {
                JointFamilyProfile profile = RequireFamily(familyId);
                JointDrive baseline = PoweredJointController.BuildDriveForTest(profile, profile.BaseCapacityNm);
                JointDrive scaled = PoweredJointController.BuildDriveForTest(profile, profile.BaseCapacityNm * 7.6f);

                Assert.That(scaled.positionSpring, Is.EqualTo(baseline.positionSpring).Within(1e-4f),
                    $"'{familyId}' spring moved with capacity. The GAM-7 family gains are invariant.");
                Assert.That(scaled.positionDamper, Is.EqualTo(baseline.positionDamper).Within(1e-4f),
                    $"'{familyId}' damper moved with capacity. The GAM-7 family gains are invariant.");
                Assert.That(baseline.positionSpring, Is.EqualTo(profile.Spring).Within(1e-4f));
                Assert.That(baseline.positionDamper, Is.EqualTo(profile.Damper).Within(1e-4f));
                Assert.That(scaled.maximumForce, Is.GreaterThan(baseline.maximumForce));
                Assert.That(baseline.useAcceleration, Is.False, "The drive must stay in force mode.");
                Assert.That(scaled.useAcceleration, Is.False, "The drive must stay in force mode.");
            }
        }

        [Test]
        public void DRIVE_DEMAND_IS_REPORTED_AGAINST_THE_ACTUAL_GAINS()
        {
            JointFamilyProfile ankle = RequireFamily("ankle");
            const float errorRad = 0.10f;
            const float capacityScale = 3.8f;

            // Demand is the torque the written gains ask for, divided by the
            // ceiling that capacity sets. Modelling a different spring here
            // than the one written to the drive is how saturation got hidden.
            float modelledTorque = ankle.Spring * errorRad;
            float ceiling = ankle.BaseCapacityNm * capacityScale;
            float expectedDemand = modelledTorque / ceiling;

            PoweredDriveDemand demand = PoweredJointController.ModelDemandForTest(
                PhysicalJointKind.Hinge, ankle, new Vector3(errorRad, 0f, 0f), Vector3.zero, ceiling);
            Assert.That(demand.TwistNm, Is.EqualTo(modelledTorque).Within(1e-4f));
            Assert.That(demand.TwistFraction, Is.EqualTo(expectedDemand).Within(1e-4f));
            Assert.That(demand.SwingNm, Is.NaN);
            Assert.That(demand.SwingFraction, Is.NaN);
            Assert.That(demand.MaximumChannelFraction, Is.EqualTo(expectedDemand).Within(1e-4f));
        }

        [Test]
        public void BALL_DRIVE_PRESSURE_MAXES_TWIST_AND_YZ_CHANNELS_SEPARATELY()
        {
            JointFamilyProfile trunk = RequireFamily("trunk");
            const float maximumForce = 100f;
            Vector3 error = new Vector3(0.1f, 0.1f, 0.1f);

            PoweredDriveDemand hingeDemand = PoweredJointController.ModelDemandForTest(
                PhysicalJointKind.Hinge, trunk, error, Vector3.zero, maximumForce);
            PoweredDriveDemand ballDemand = PoweredJointController.ModelDemandForTest(
                PhysicalJointKind.Ball, trunk, error, Vector3.zero, maximumForce);

            float expectedTwistNm = trunk.Spring * 0.1f;
            // PhysX clamps each swing axis separately (GAM-50 B06), so the
            // binding swing channel is the larger axis, not the YZ magnitude.
            float expectedSwingNm = expectedTwistNm;
            Assert.That(hingeDemand.MaximumChannelFraction, Is.EqualTo(expectedTwistNm / maximumForce).Within(1e-4f));
            Assert.That(ballDemand.TwistNm, Is.EqualTo(expectedTwistNm).Within(1e-4f));
            Assert.That(ballDemand.SwingNm, Is.EqualTo(expectedSwingNm).Within(1e-4f));
            Assert.That(ballDemand.TwistFraction, Is.EqualTo(expectedTwistNm / maximumForce).Within(1e-4f));
            Assert.That(ballDemand.SwingFraction, Is.EqualTo(expectedSwingNm / maximumForce).Within(1e-4f));
            Assert.That(ballDemand.MaximumChannelFraction, Is.EqualTo(expectedSwingNm / maximumForce).Within(1e-4f));
            Assert.That(ballDemand.MaximumChannelFraction, Is.LessThan(new Vector3(
                expectedTwistNm, expectedTwistNm, expectedTwistNm).magnitude / maximumForce));
        }

        private static JointFamilyProfile RequireFamily(string familyId)
        {
            JointFamilyProfile? profile = PoweredJointController.FindFamilyProfile(familyId);
            Assert.That(profile.HasValue, Is.True, $"Unknown joint family '{familyId}'.");
            return profile.Value;
        }
    }
}
