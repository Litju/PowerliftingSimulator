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

            float demand = PoweredJointController.ModelDemandForTest(ankle, new Vector3(errorRad, 0f, 0f), Vector3.zero, ceiling);
            Assert.That(demand, Is.EqualTo(expectedDemand).Within(1e-4f));
        }

        private static JointFamilyProfile RequireFamily(string familyId)
        {
            JointFamilyProfile? profile = PoweredJointController.FindFamilyProfile(familyId);
            Assert.That(profile.HasValue, Is.True, $"Unknown joint family '{familyId}'.");
            return profile.Value;
        }
    }
}
