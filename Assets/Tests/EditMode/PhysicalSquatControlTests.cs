using System;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Squat;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class PhysicalSquatControlTests
    {
        [Test]
        public void SINGLE_WRITER_ENFORCES_EXACTLY_ONE_COMMAND_PER_JOINT_PER_TICK()
        {
            GameObject parentGo = new GameObject("Parent");
            GameObject childGo = new GameObject("Child");
            childGo.transform.SetParent(parentGo.transform);

            Rigidbody parentRb = parentGo.AddComponent<Rigidbody>();
            Rigidbody childRb = childGo.AddComponent<Rigidbody>();

            ConfigurableJoint joint = childGo.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;

            PhysicalJointRecipe recipe = PhysicalAthleteDefinition.Joints[0];
            PhysicalAthleteRig.JointRuntime jointRuntime = new PhysicalAthleteRig.JointRuntime(recipe, joint, Vector3.zero);
            PoweredJointController controller = new PoweredJointController(new[] { jointRuntime });

            var command1 = new JointCommand(Quaternion.identity, Vector3.zero, 1.0f, 1.0f);
            var command2 = new JointCommand(Quaternion.AngleAxis(10f, Vector3.right), Vector3.zero, 1.0f, 1.0f);

            // First apply on tick 1 succeeds
            Assert.DoesNotThrow(() => controller.ApplyCommand(recipe.ChildId, command1, 1ul));

            // Second apply on same joint on same tick 1 MUST throw InvalidOperationException
            Assert.Throws<InvalidOperationException>(() => controller.ApplyCommand(recipe.ChildId, command2, 1ul));

            // Apply on tick 2 succeeds
            Assert.DoesNotThrow(() => controller.ApplyCommand(recipe.ChildId, command2, 2ul));

            UnityEngine.Object.DestroyImmediate(parentGo);
            UnityEngine.Object.DestroyImmediate(childGo);
        }

        [Test]
        public void JOINT_COMMAND_NORMALIZES_AND_VALIDATES_FINITE_INPUTS()
        {
            var cmd = new JointCommand(Quaternion.identity, Vector3.zero, 0.8f, 1.2f);
            Assert.That(cmd.TargetRelativeRotation, Is.EqualTo(Quaternion.identity));
            Assert.That(cmd.Activation, Is.EqualTo(0.8f).Within(0.001f));
            Assert.That(cmd.CapacityScale, Is.EqualTo(1.2f).Within(0.001f));

            // Non-finite rotation throws
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new JointCommand(new Quaternion(float.NaN, 0, 0, 1), Vector3.zero, 1f, 1f));

            // Non-finite velocity throws
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new JointCommand(Quaternion.identity, new Vector3(float.PositiveInfinity, 0, 0), 1f, 1f));
        }

        [Test]
        public void SQUAT_BAR_SADDLE_HAS_FINITE_PHYSICS_CONSTRAINTS_AND_NO_PROJECTION()
        {
            Assert.That(SquatBarSaddle.DefaultLinearSpring, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(SquatBarSaddle.DefaultLinearSpring), Is.True);
            Assert.That(SquatBarSaddle.DefaultLinearDamper, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(SquatBarSaddle.DefaultLinearDamper), Is.True);
            Assert.That(SquatBarSaddle.DefaultLinearMaxForce, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(SquatBarSaddle.DefaultLinearMaxForce), Is.True);
            Assert.That(SquatBarSaddle.DefaultBreakForce, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(SquatBarSaddle.DefaultBreakForce), Is.True);
            Assert.That(SquatBarSaddle.DefaultBreakTorque, Is.GreaterThan(0f));
            Assert.That(float.IsFinite(SquatBarSaddle.DefaultBreakTorque), Is.True);
            Assert.That(SquatBarSaddle.ThoraxLocalAnchor.sqrMagnitude, Is.GreaterThan(0f));
        }

        [Test]
        public void BOUNDED_BALANCE_CORRECTION_SATURATES_AND_PRESERVES_FINITE_BOUNDS()
        {
            // Zero COM error -> zero offset
            float offsetZero = SquatPhysicalAdapter.CalculateBalanceOffset(0f, 0f, 0.02f);
            Assert.That(offsetZero, Is.EqualTo(0f));

            // Positive COM error (COM forward) -> negative offset (plantarflexion/extension)
            float offsetForward = SquatPhysicalAdapter.CalculateBalanceOffset(0.05f, 0f, 0.02f);
            Assert.That(offsetForward, Is.LessThan(0f));

            // Negative COM error (COM backward) -> positive offset (dorsiflexion/flexion)
            float offsetBackward = SquatPhysicalAdapter.CalculateBalanceOffset(-0.05f, 0f, 0.02f);
            Assert.That(offsetBackward, Is.GreaterThan(0f));

            // Extreme COM error must saturate within the bounded game calibration angle.
            float maxAngleRad = SquatPhysicalAdapter.MaxBalanceCorrectionRad;
            float offsetExtreme = SquatPhysicalAdapter.CalculateBalanceOffset(10f, 0f, 0.02f);
            Assert.That(Mathf.Abs(offsetExtreme), Is.LessThanOrEqualTo(maxAngleRad + 0.0001f));
            Assert.That(float.IsFinite(offsetExtreme), Is.True);
        }

        [Test]
        public void CANONICAL_SQUAT_PROFILE_COVERS_LEGAL_DEPTH_AND_LOCKOUT()
        {
            var profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            Assert.That(profile, Is.Not.Null);

            SquatReferencePose standing = profile.Evaluate(0f, SquatPhaseDirection.Descent);
            Assert.That(standing.HipFlexionRad, Is.LessThan(0.3f));
            Assert.That(standing.KneeFlexionRad, Is.LessThan(0.3f));

            SquatReferencePose inflection = profile.Evaluate(1.0f, SquatPhaseDirection.Descent);
            Assert.That(inflection.KneeFlexionRad, Is.GreaterThan(1.5f));
            Assert.That(inflection.HipFlexionRad, Is.GreaterThan(1.5f));

            SquatReferencePose lockout = profile.Evaluate(0f, SquatPhaseDirection.Ascent);
            Assert.That(lockout.HipFlexionRad, Is.LessThan(0.3f));
            Assert.That(lockout.KneeFlexionRad, Is.LessThan(0.3f));
        }
    }
}
