using System;
using System.IO;
using NUnit.Framework;
using PowerliftingSimulator.Foundation;
using PowerliftingSimulator.Squat;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    public sealed class SquatDomainTests
    {
        private const double StepSeconds = 0.01d;

        [Test]
        public void SQUAT_STATE_MACHINE_HAPPY_PATH_REQUIRES_DOMAIN_GATES()
        {
            var machine = new SquatStateMachine();

            Assert.That(machine.Step(Intent(IntentEdgeFlags.ConfirmPressed), SquatDomainObservation.Empty, StepSeconds).To,
                Is.EqualTo(SquatState.UNRACK));
            Assert.That(machine.Step(PlayerIntentFrame.Empty, Observation(barClearOfHooks: true), StepSeconds).To,
                Is.EqualTo(SquatState.WALKOUT));
            Assert.That(machine.Step(PlayerIntentFrame.Empty, Observation(walkoutComplete: true), StepSeconds).To,
                Is.EqualTo(SquatState.SETTLE));
            Assert.That(machine.Step(PlayerIntentFrame.Empty, Observation(physicallySettled: true), StepSeconds).To,
                Is.EqualTo(SquatState.SQUAT_COMMAND));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, yield01: 1f), Observation(squatCommandReceived: true), StepSeconds).To,
                Is.EqualTo(SquatState.DESCENT));

            machine.Step(Intent(IntentEdgeFlags.None, yield01: 1f), SquatDomainObservation.Empty, StepSeconds);
            Assert.That(machine.Phase, Is.GreaterThan(0f));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, yield01: 1f), Observation(depthLegalBilateral: true), StepSeconds).To,
                Is.EqualTo(SquatState.BOTTOM));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, drive01: 1f), Observation(reversalEvidence: true), StepSeconds).To,
                Is.EqualTo(SquatState.REVERSAL));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, drive01: 1f), Observation(upwardVelocity: true), StepSeconds).To,
                Is.EqualTo(SquatState.ASCENT));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, drive01: 1f), Observation(lockoutReached: true), StepSeconds).To,
                Is.EqualTo(SquatState.LOCKOUT));
            Assert.That(machine.Step(PlayerIntentFrame.Empty, Observation(rackCommandReceived: true), StepSeconds).To,
                Is.EqualTo(SquatState.RACK_COMMAND));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.ConfirmPressed), SquatDomainObservation.Empty, StepSeconds).To,
                Is.EqualTo(SquatState.RERACK));
            Assert.That(machine.Step(PlayerIntentFrame.Empty, Observation(barSecureOnHooks: true), StepSeconds).To,
                Is.EqualTo(SquatState.COMPLETE));
        }

        [Test]
        public void SQUAT_STATE_MACHINE_REJECTS_EARLY_SHALLOW_AND_ABORT_PATHS()
        {
            var machine = new SquatStateMachine();
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, yield01: 1f), Observation(squatCommandReceived: true), StepSeconds).To,
                Is.EqualTo(SquatState.SETUP));

            machine.Step(Intent(IntentEdgeFlags.ConfirmPressed), SquatDomainObservation.Empty, StepSeconds);
            machine.Step(PlayerIntentFrame.Empty, Observation(barClearOfHooks: true), StepSeconds);
            machine.Step(PlayerIntentFrame.Empty, Observation(walkoutComplete: true), StepSeconds);
            machine.Step(PlayerIntentFrame.Empty, Observation(physicallySettled: true), StepSeconds);
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, yield01: 0f), Observation(squatCommandReceived: true), StepSeconds).To,
                Is.EqualTo(SquatState.SQUAT_COMMAND));

            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, yield01: 1f), Observation(squatCommandReceived: true), StepSeconds).To,
                Is.EqualTo(SquatState.DESCENT));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, yield01: 1f), Observation(collapseDetected: true), StepSeconds).To,
                Is.EqualTo(SquatState.FAILURE));

            machine.Reset(SquatState.BOTTOM);
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None, drive01: 1f), Observation(upwardWithoutDepth: true), StepSeconds).To,
                Is.EqualTo(SquatState.FAILURE));

            machine.Reset(SquatState.LOCKOUT);
            Assert.That(machine.Step(Intent(IntentEdgeFlags.None), Observation(rackCommandReceived: false), StepSeconds).To,
                Is.EqualTo(SquatState.LOCKOUT));
            Assert.That(machine.Step(Intent(IntentEdgeFlags.AbortPressed), SquatDomainObservation.Empty, StepSeconds).To,
                Is.EqualTo(SquatState.FAILURE));
        }

        [Test]
        public void REFERENCE_PROFILE_HAS_EXACT_ENDPOINTS_AND_BOUNDED_DERIVATIVES()
        {
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            SquatReferencePose standing = profile.Evaluate(0f, SquatPhaseDirection.Descent);
            SquatReferencePose bottom = profile.Evaluate(1f, SquatPhaseDirection.Descent);
            Assert.That(standing.AnkleDorsiflexionRad, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(bottom.KneeFlexionRad, Is.GreaterThan(standing.KneeFlexionRad));
            Assert.That(profile.ReversalPoseDiscontinuity, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(profile.LockoutPoseDiscontinuity, Is.EqualTo(0f).Within(1e-6f));

            foreach (SquatReferenceWaypointRecord waypoint in profile.Waypoints)
            {
                SquatReferencePose pose = profile.Evaluate(
                    waypoint.Phase,
                    waypoint.Waypoint == SquatReferenceWaypoint.EARLY_ASCENT ||
                    waypoint.Waypoint == SquatReferenceWaypoint.STICKING
                        ? SquatPhaseDirection.Ascent
                        : SquatPhaseDirection.Descent);
                AssertPoseEqual(waypoint.Pose, pose, waypoint.Waypoint.ToString());
                AssertPoseFinite(profile.Derivative(waypoint.Phase, SquatPhaseDirection.Descent));
                AssertPoseFinite(profile.Derivative(waypoint.Phase, SquatPhaseDirection.Ascent));
            }
        }

        [Test]
        public void REFERENCE_WAYPOINTS_ARE_CONTINUOUS_ACROSS_CURVE_KEYS()
        {
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            float[] phases = { 0f, 0.25f, 0.55f, 0.64f, 0.82f, 1f };
            for (int index = 1; index < phases.Length - 1; index++)
            {
                float phase = phases[index];
                SquatReferencePose left = profile.Evaluate(phase - 1e-5f, SquatPhaseDirection.Descent);
                SquatReferencePose right = profile.Evaluate(phase + 1e-5f, SquatPhaseDirection.Descent);
                Assert.That(PoseDistance(left, right), Is.LessThan(0.002f), $"phase={phase}");
            }
        }

        [Test]
        public void BRANCH_REVERSAL_IS_POSE_CONTINUOUS_AND_PHASE_ADVANCEMENT_IS_DETERMINISTIC()
        {
            SquatReferenceProfile profile = SquatReferenceProfile.CanonicalPowerliftingSquatV1;
            Assert.That(PoseDistance(
                profile.Evaluate(1f, SquatPhaseDirection.Descent),
                profile.Evaluate(1f, SquatPhaseDirection.Ascent)), Is.LessThan(1e-6f));

            var first = new SquatStateMachine(SquatState.DESCENT);
            var second = new SquatStateMachine(SquatState.DESCENT);
            PlayerIntentFrame intent = Intent(IntentEdgeFlags.None, yield01: 1f);
            for (int index = 0; index < 120; index++)
            {
                first.Step(intent, SquatDomainObservation.Empty, StepSeconds);
                second.Step(intent, SquatDomainObservation.Empty, StepSeconds);
                Assert.That(second.Phase, Is.EqualTo(first.Phase).Within(1e-7f));
                Assert.That(second.PhaseRate, Is.EqualTo(first.PhaseRate).Within(1e-7f));
            }
        }

        [Test]
        public void BILATERAL_DEPTH_GEOMETRY_REQUIRES_BOTH_SIDES_AND_THE_NAMED_MARGIN()
        {
            SquatDepthObservation legal = SquatDepthGeometry.Evaluate(0.40f, 0.40f, 0.46f, 0.46f);
            Assert.That(legal.BilateralLegalReference, Is.True);
            Assert.That(legal.LeftDepthM, Is.EqualTo(-0.06f).Within(1e-6f));
            Assert.That(legal.RightDepthM, Is.EqualTo(-0.06f).Within(1e-6f));

            SquatDepthObservation shallow = SquatDepthGeometry.Evaluate(0.48f, 0.48f, 0.46f, 0.46f);
            Assert.That(shallow.BilateralLegalReference, Is.False);

            SquatDepthObservation unilateralHigh = SquatDepthGeometry.Evaluate(0.40f, 0.48f, 0.46f, 0.46f);
            Assert.That(unilateralHigh.LeftDepthM, Is.LessThan(-SquatDepthGeometry.DefaultDepthMarginM));
            Assert.That(unilateralHigh.RightDepthM, Is.GreaterThan(-SquatDepthGeometry.DefaultDepthMarginM));
            Assert.That(unilateralHigh.BilateralLegalReference, Is.False);
        }

        [Test]
        public void SQUAT_DOMAIN_HAS_NO_PHYSICAL_OR_ANIMATION_AUTHORITY_DEPENDENCY()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string asmdef = File.ReadAllText(Path.Combine(projectRoot, "Assets/Scripts/Squat/PowerliftingSimulator.Squat.asmdef"));
            Assert.That(asmdef, Does.Contain("\"noEngineReferences\": true"));
            string[] sourceFiles =
            {
                "Assets/Scripts/Squat/SquatDomain.cs",
                "Assets/Scripts/Squat/SquatReferenceMotion.cs",
                "Assets/Scripts/Squat/SquatDepthGeometry.cs"
            };
            foreach (string relativePath in sourceFiles)
            {
                string source = File.ReadAllText(Path.Combine(projectRoot, relativePath));
                Assert.That(source, Does.Not.Contain("UnityEngine"), relativePath);
                Assert.That(source, Does.Not.Contain("Rigidbody"), relativePath);
                Assert.That(source, Does.Not.Contain("Animator"), relativePath);
                Assert.That(source, Does.Not.Contain("AddForce"), relativePath);
                Assert.That(source, Does.Not.Contain("AddTorque"), relativePath);
                Assert.That(source, Does.Not.Contain("Bench"), relativePath);
                Assert.That(source, Does.Not.Contain("Deadlift"), relativePath);
            }
        }

        private static SquatDomainObservation Observation(
            bool barClearOfHooks = false,
            bool walkoutComplete = false,
            bool physicallySettled = false,
            bool squatCommandReceived = false,
            bool depthLegalBilateral = false,
            bool reversalEvidence = false,
            bool upwardVelocity = false,
            bool upwardWithoutDepth = false,
            bool collapseDetected = false,
            bool stickingDetected = false,
            bool recoveredFromSticking = false,
            bool lockoutReached = false,
            bool rackCommandReceived = false,
            bool barSecureOnHooks = false) => new SquatDomainObservation(
                barClearOfHooks,
                walkoutComplete,
                physicallySettled,
                squatCommandReceived,
                depthLegalBilateral,
                reversalEvidence,
                upwardVelocity,
                upwardWithoutDepth,
                collapseDetected,
                stickingDetected,
                recoveredFromSticking,
                lockoutReached,
                rackCommandReceived,
                barSecureOnHooks);

        private static PlayerIntentFrame Intent(IntentEdgeFlags edges, float yield01 = 0f, float drive01 = 0f) => new PlayerIntentFrame(
            1ul,
            StepSeconds,
            edges,
            0f,
            yield01,
            drive01,
            0f,
            0f,
            false,
            yield01 > 0f,
            drive01 > 0f,
            false,
            false,
            edges.HasFlag(IntentEdgeFlags.ConfirmPressed),
            edges.HasFlag(IntentEdgeFlags.AbortPressed));

        private static void AssertPoseEqual(SquatReferencePose expected, SquatReferencePose actual, string label)
        {
            Assert.That(actual.AnkleDorsiflexionRad, Is.EqualTo(expected.AnkleDorsiflexionRad).Within(1e-6f), label);
            Assert.That(actual.KneeFlexionRad, Is.EqualTo(expected.KneeFlexionRad).Within(1e-6f), label);
            Assert.That(actual.HipFlexionRad, Is.EqualTo(expected.HipFlexionRad).Within(1e-6f), label);
            Assert.That(actual.TrunkFlexionRad, Is.EqualTo(expected.TrunkFlexionRad).Within(1e-6f), label);
            Assert.That(actual.ShoulderFlexionRad, Is.EqualTo(expected.ShoulderFlexionRad).Within(1e-6f), label);
            Assert.That(actual.ElbowFlexionRad, Is.EqualTo(expected.ElbowFlexionRad).Within(1e-6f), label);
            Assert.That(actual.WristExtensionRad, Is.EqualTo(expected.WristExtensionRad).Within(1e-6f), label);
        }

        private static void AssertPoseFinite(SquatReferencePose pose)
        {
            Assert.That(float.IsFinite(pose.AnkleDorsiflexionRad), Is.True);
            Assert.That(float.IsFinite(pose.KneeFlexionRad), Is.True);
            Assert.That(float.IsFinite(pose.HipFlexionRad), Is.True);
            Assert.That(float.IsFinite(pose.TrunkFlexionRad), Is.True);
            Assert.That(float.IsFinite(pose.ShoulderFlexionRad), Is.True);
            Assert.That(float.IsFinite(pose.ElbowFlexionRad), Is.True);
            Assert.That(float.IsFinite(pose.WristExtensionRad), Is.True);
        }

        private static float PoseDistance(SquatReferencePose left, SquatReferencePose right) => Math.Max(
            Math.Max(Math.Abs(left.AnkleDorsiflexionRad - right.AnkleDorsiflexionRad),
                Math.Abs(left.KneeFlexionRad - right.KneeFlexionRad)),
            Math.Max(Math.Abs(left.HipFlexionRad - right.HipFlexionRad),
                Math.Max(Math.Abs(left.TrunkFlexionRad - right.TrunkFlexionRad),
                    Math.Max(Math.Abs(left.ShoulderFlexionRad - right.ShoulderFlexionRad),
                        Math.Max(Math.Abs(left.ElbowFlexionRad - right.ElbowFlexionRad),
                            Math.Abs(left.WristExtensionRad - right.WristExtensionRad))))));
    }
}
