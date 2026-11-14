using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Deadlift.Unity;
using PowerliftingSimulator.Foundation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using F = PowerliftingSimulator.Deadlift.IsolatedBarWorld;

namespace PowerliftingSimulator.Deadlift
{
    /// <summary>
    /// GAM-70 Gate 1: the deadlift-local ankle inversion/eversion topology,
    /// its lifecycle, its physical limits and its two independent powered
    /// channels, measured on the real Unity plant.
    ///
    /// The canonical rig must remain the GAM-6 16-body/15-joint graph with both
    /// ankle swing axes locked. The deadlift variant is enabled once, before
    /// tick 0, unlocks only Z at +/-15 deg and powers it through the existing
    /// PoweredJointController writer at a 60 N m finite ceiling. These are
    /// engine facts, not physiological claims.
    /// </summary>
    public sealed partial class DeadliftSetupDynamicTests
    {
        private static string Fmt(float v) => v.ToString("G7", CultureInfo.InvariantCulture);

        [UnityTest, Timeout(600000)]
        public IEnumerator GATE1_CANONICAL_RIG_RETAINS_ITS_HINGE_TOPOLOGY_AND_THE_VARIANT_IS_VERSIONED()
        {
            _ankleRoll = false;
            yield return Prepare(true, true, false);
            var report = new StringBuilder();
            PhysicalAthleteRig rig = _world.Rig;
            PoweredJointController controller = rig.PoweredController;

            report.AppendLine("schema " + DeadliftAnkleRoll.TopologyVersion + " | canonical " + PhysicalAthleteRig.CanonicalTopologyVersion);
            report.AppendLine($"rig bodies {rig.Segments.Count} joints {rig.Joints.Count} powered {controller.PoweredJointCount} passive {controller.PassiveJointCount} secondary {controller.SecondaryChannelCount}");
            Assert.That(rig.AnkleRollLimitDegrees.HasValue, Is.False, "a fresh canonical plant already has the deadlift variant");
            Assert.That(controller.SecondaryChannelCount, Is.EqualTo(0), "a fresh canonical plant already has a secondary channel");
            rig.ValidateCanonicalTopology();
            foreach (string footId in new[] { "left_foot", "right_foot" })
            {
                ConfigurableJoint ankle = controller.GetJoint(footId).Joint;
                report.AppendLine($"{footId}: X {ankle.angularXMotion} Y {ankle.angularYMotion} Z {ankle.angularZMotion} zLimit {Fmt(ankle.angularZLimit.limit)} secondaryProfile {controller.GetJoint(footId).SecondaryProfile.HasValue}");
                Assert.That(ankle.angularZMotion, Is.EqualTo(ConfigurableJointMotion.Locked), footId + " canonical Z is not locked");
                Assert.That(ankle.angularYMotion, Is.EqualTo(ConfigurableJointMotion.Locked), footId + " canonical Y is not locked");
            }

            // Enable exactly once before tick 0. The source plant was already
            // primed by PlaceAtSetup; that is initialization, not a physics tick.
            DeadliftAnkleRoll.Enable(rig);
            DeadliftAnkleRoll.TopologyReceipt receipt = DeadliftAnkleRoll.Validate(rig);
            report.Append(receipt.Describe());
            report.AppendLine($"after enable: limit {Fmt(rig.AnkleRollLimitDegrees.Value)} deg | secondary channels {controller.SecondaryChannelCount} | bodies {rig.Segments.Count} joints {rig.Joints.Count}");
            Assert.That(rig.AnkleRollLimitDegrees.Value, Is.EqualTo(DeadliftAnkleRoll.LimitDegrees).Within(0.0001f));
            Assert.That(controller.SecondaryChannelCount, Is.EqualTo(2));

            // Repeated activation with the same declared limit is a no-op;
            // redefining it is refused, and late activation is refused.
            DeadliftAnkleRoll.Enable(rig);
            Assert.That(rig.AnkleRollLimitDegrees.Value, Is.EqualTo(DeadliftAnkleRoll.LimitDegrees).Within(0.0001f));
            bool changed = false;
            try { rig.EnableAnkleRoll(10f); changed = true; } catch (InvalidOperationException) { }
            Assert.That(changed, Is.False, "the active variant was silently redefined");
            bool duplicate = false;
            try { controller.EnableSecondaryChannel("left_foot", DeadliftAnkleRoll.Profile); duplicate = true; } catch (InvalidOperationException) { }
            Assert.That(duplicate, Is.False, "a second secondary channel was installed on the same ankle");

            // Prime with the variant and validate the actual written drive.
            _world.Rig.PrimeCommandSource();
            DeadliftAnkleRoll.ValidateAppliedAuthority(rig);
            foreach (string footId in new[] { "left_foot", "right_foot" })
            {
                PoweredJointController.PoweredJointRuntime j = controller.GetJoint(footId);
                report.AppendLine($"{footId} applied drives: X max {Fmt(j.Joint.angularXDrive.maximumForce)} N m | Z spring {Fmt(j.Joint.angularYZDrive.positionSpring)} damper {Fmt(j.Joint.angularYZDrive.positionDamper)} max {Fmt(j.Joint.angularYZDrive.maximumForce)} N m | YZ mode {j.Joint.rotationDriveMode}");
                Assert.That(j.Joint.angularYZDrive.maximumForce, Is.GreaterThan(0f));
                Assert.That(j.Joint.angularYZDrive.maximumForce, Is.LessThanOrEqualTo(DeadliftAnkleRoll.CeilingNm + 0.001f));
            }

            _world.Step(1);
            bool late = false;
            try { DeadliftAnkleRoll.Enable(rig); late = true; } catch (InvalidOperationException) { }
            Assert.That(late, Is.False, "late activation of a running plant was accepted");
            Assert.That(rig.HasPhysicsStepped, Is.True);
            F.WriteEvidence("gate1-topology.txt", report.ToString());
            yield return _world.Teardown();
            _world = null;
            _ankleRoll = false;
        }

        private sealed class ZCase
        {
            public string Name;
            public float Actual0, Actual1, Actual2, Actual3;
            public float Applied0, Applied3, Requested3, Demand3, DemandFraction3, Velocity3, Error3;
            public float X3, Y3, CopAcross0, CopAcross3, RightAcross0, RightAcross3, FootN3;
            public float TravelNeg3, TravelPos3, LimitProximity3, Solver3, Ceiling3;
            public float MaxActual, MinTravel, MaxDemandFraction, MaxLimitProximity, FinalActual, FinalApplied, FinalRequested, FinalSolver;
            public bool Channels, Saturated, RateLimited;
            public readonly StringBuilder Rows = new StringBuilder("case,tick,side,requestedDeg,appliedDeg,actualDeg,velocityRadS,errorRad,travelNegDeg,travelPosDeg,demandNm,demandFraction,ceilingNm,saturated,rateLimited,limitProx,solverTorqueNm,xDeg,yDeg,copAcross,footN\n");
        }

        private void SampleZ(ZCase c, int tick, string side, bool left)
        {
            PoweredJointDiagnostic d = _world.Rig.PoweredController.GetJoint(side + "_foot").PostPhysicsDiagnostic;
            float across = _regulator.CopAcrossAxis(side + "_foot");
            float footN = left ? (float)_ticks.Last().LeftFootN : (float)_ticks.Last().RightFootN;
            c.Rows.AppendLine(string.Join(",", c.Name, tick.ToString(CultureInfo.InvariantCulture), side,
                Fmt(d.SecondaryRequestedAngleDeg), Fmt(d.SecondaryAppliedAngleDeg), Fmt(d.SecondaryActualAngleDeg),
                Fmt(d.SecondaryActualAngularVelocityRadS), Fmt(d.SecondaryErrorRad),
                Fmt(d.SecondaryTravelToNegativeLimitDeg), Fmt(d.SecondaryTravelToPositiveLimitDeg),
                Fmt(d.SecondaryDriveDemandNm), Fmt(d.SecondaryDriveDemandFraction), Fmt(d.SecondaryMaximumForceNm),
                d.SecondaryDriveSaturated, d.SecondaryTargetRateLimited, Fmt(d.SecondaryLimitProximity),
                Fmt(d.SecondarySolverTorqueNm),
                Fmt(PoweredJointController.SignedTwistRadians(d.ActualRelative, Vector3.right) * Mathf.Rad2Deg),
                Fmt(PoweredJointController.SignedTwistRadians(d.ActualRelative, Vector3.up) * Mathf.Rad2Deg),
                Fmt(across), Fmt(footN)));
        }

        /// <summary>
        /// A paired, frozen-regulator step measurement. Quiet, +1 deg and
        /// -1 deg run on identical fresh plants for 150 ticks; the regulator is
        /// then frozen identically, and the left ankle's Z reference receives
        /// the isolated step. Only the difference from the quiet control is
        /// attributed to the actuator; the frozen foot-roll mode itself drifts.
        /// </summary>
        private IEnumerator RunZStepCase(string name, float stepDeg, ZCase c)
        {
            c.Name = name;
            _ankleRoll = true;
            yield return Prepare(true, true, false);
            InstallContactReference(0f, null);
            _regulator = new DeadliftBalanceRegulator(_world.Rig, _world.Rig.PlatformCollider)
            {
                ComFeedback = true,
                KiDegPerMPerTick = 3f,
                RollLoop = true
            };
            _world.Hold.Regulator = _regulator;
            _world.Rig.PrimeCommandSource();
            DeadliftAnkleRoll.ValidateAppliedAuthority(_world.Rig);
            _world.Step(150);
            _regulator.Frozen = true;
            _world.Step(1);
            PoweredJointDiagnostic d0 = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
            c.Actual0 = d0.SecondaryActualAngleDeg;
            c.Applied0 = d0.SecondaryAppliedAngleDeg;
            c.CopAcross0 = _regulator.CopAcrossAxis("left_foot");
            c.RightAcross0 = _regulator.CopAcrossAxis("right_foot");
            SampleZ(c, 0, "left", true);
            SampleZ(c, 0, "right", false);
            if (stepDeg != 0f)
                _world.Hold.IdentificationOffsets["left_foot"] = Quaternion.AngleAxis(stepDeg, Vector3.forward);
            for (int i = 1; i <= 3; i++)
            {
                _world.Step(1);
                PoweredJointDiagnostic d = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
                if (i == 1) c.Actual1 = d.SecondaryActualAngleDeg;
                if (i == 2) c.Actual2 = d.SecondaryActualAngleDeg;
                if (i == 3)
                {
                    c.Actual3 = d.SecondaryActualAngleDeg;
                    c.Applied3 = d.SecondaryAppliedAngleDeg;
                    c.Requested3 = d.SecondaryRequestedAngleDeg;
                    c.Demand3 = d.SecondaryDriveDemandNm;
                    c.DemandFraction3 = d.SecondaryDriveDemandFraction;
                    c.Velocity3 = d.SecondaryActualAngularVelocityRadS;
                    c.Error3 = d.SecondaryErrorRad;
                    c.X3 = PoweredJointController.SignedTwistRadians(d.ActualRelative, Vector3.right) * Mathf.Rad2Deg;
                    c.Y3 = PoweredJointController.SignedTwistRadians(d.ActualRelative, Vector3.up) * Mathf.Rad2Deg;
                    c.TravelNeg3 = d.SecondaryTravelToNegativeLimitDeg;
                    c.TravelPos3 = d.SecondaryTravelToPositiveLimitDeg;
                    c.LimitProximity3 = d.SecondaryLimitProximity;
                    c.Solver3 = d.SecondarySolverTorqueNm;
                    c.Ceiling3 = d.SecondaryMaximumForceNm;
                    c.Channels = d.HasSecondaryChannel;
                    c.Saturated = d.SecondaryDriveSaturated;
                    c.RateLimited = d.SecondaryTargetRateLimited;
                    c.CopAcross3 = _regulator.CopAcrossAxis("left_foot");
                    c.RightAcross3 = _regulator.CopAcrossAxis("right_foot");
                    c.FootN3 = (float)_ticks.Last().LeftFootN;
                }
                SampleZ(c, i, "left", true);
                SampleZ(c, i, "right", false);
            }
            yield return _world.Teardown();
            _world = null;
        }

        /// <summary>
        /// Over-limit command with the along-sole/COM balance loop active but
        /// the roll loop disabled: the Z target is 20 deg, the realized joint
        /// must remain inside the declared +/-15 deg stop. This is the physical
        /// limit test, not a controller setpoint test.
        /// </summary>
        private IEnumerator RunZLimitCase(ZCase c)
        {
            c.Name = "limit+60";
            _ankleRoll = true;
            yield return Prepare(true, true, false);
            InstallContactReference(0f, null);
            _regulator = new DeadliftBalanceRegulator(_world.Rig, _world.Rig.PlatformCollider)
            {
                ComFeedback = true,
                KiDegPerMPerTick = 3f,
                RollLoop = false
            };
            _world.Hold.Regulator = _regulator;
            _world.Rig.PrimeCommandSource();
            DeadliftAnkleRoll.ValidateAppliedAuthority(_world.Rig);
            _world.Step(150);
            _world.Hold.IdentificationOffsets["left_foot"] = Quaternion.AngleAxis(60f, Vector3.forward);
            c.MaxActual = float.MinValue;
            c.MinTravel = float.MaxValue;
            c.MaxDemandFraction = 0f;
            c.MaxLimitProximity = 0f;
            c.Channels = true;
            for (int i = 1; i <= 150; i++)
            {
                _world.Step(1);
                PoweredJointDiagnostic d = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
                c.MaxActual = Mathf.Max(c.MaxActual, d.SecondaryActualAngleDeg);
                c.MinTravel = Mathf.Min(c.MinTravel, Mathf.Min(d.SecondaryTravelToNegativeLimitDeg, d.SecondaryTravelToPositiveLimitDeg));
                c.MaxDemandFraction = Mathf.Max(c.MaxDemandFraction, d.SecondaryDriveDemandFraction);
                c.MaxLimitProximity = Mathf.Max(c.MaxLimitProximity, d.SecondaryLimitProximity);
                c.Channels &= d.HasSecondaryChannel;
                c.Saturated |= d.SecondaryDriveSaturated;
                c.RateLimited |= d.SecondaryTargetRateLimited;
                if (i <= 20 || i % 10 == 0)
                {
                    SampleZ(c, i, "left", true);
                    SampleZ(c, i, "right", false);
                }
            }
            PoweredJointDiagnostic end = _world.Rig.PoweredController.GetJoint("left_foot").PostPhysicsDiagnostic;
            c.FinalActual = end.SecondaryActualAngleDeg;
            c.FinalApplied = end.SecondaryAppliedAngleDeg;
            c.FinalRequested = end.SecondaryRequestedAngleDeg;
            c.FinalSolver = end.SecondarySolverTorqueNm;
            c.LimitProximity3 = end.SecondaryLimitProximity;
            yield return _world.Teardown();
            _world = null;
        }

        [UnityTest, Timeout(1200000)]
        public IEnumerator GATE1_ANKLE_ROLL_Z_CHANNEL_LIMITS_DRIVE_AND_REALIZED_RESPONSE()
        {
            var report = new StringBuilder();
            var quiet = new ZCase();
            yield return RunZStepCase("quiet", 0f, quiet);
            var plus = new ZCase();
            yield return RunZStepCase("plus1", 1f, plus);
            var minus = new ZCase();
            yield return RunZStepCase("minus1", -1f, minus);
            var limit = new ZCase();
            yield return RunZLimitCase(limit);
            _ankleRoll = false;

            float quietDelta = quiet.Actual3 - quiet.Actual0;
            float plusDelta = plus.Actual3 - plus.Actual0;
            float minusDelta = minus.Actual3 - minus.Actual0;
            float plusCopDelta = (plus.CopAcross3 - plus.CopAcross0) - (quiet.CopAcross3 - quiet.CopAcross0);
            float minusCopDelta = (minus.CopAcross3 - minus.CopAcross0) - (quiet.CopAcross3 - quiet.CopAcross0);
            float plusCrossFoot = (plus.RightAcross3 - plus.RightAcross0) - (quiet.RightAcross3 - quiet.RightAcross0);

            report.AppendLine("schema " + DeadliftAnkleRoll.TopologyVersion);
            report.AppendLine($"quiet frozen step: actual {Fmt(quiet.Actual0)} -> {Fmt(quiet.Actual3)} deg (delta {Fmt(quietDelta)}) | applied {Fmt(quiet.Applied3)} | demand {Fmt(quiet.Demand3)} / {Fmt(quiet.Ceiling3)} N m | COP across delta {Fmt((quiet.CopAcross3 - quiet.CopAcross0) * 1000f)} mm | foot {Fmt(quiet.FootN3)} N");
            report.AppendLine($"+1 deg isolated: actual {Fmt(plus.Actual0)} -> {Fmt(plus.Actual1)} / {Fmt(plus.Actual2)} / {Fmt(plus.Actual3)} deg (delta {Fmt(plusDelta)}) | requested {Fmt(plus.Requested3)} applied {Fmt(plus.Applied3)} | demand {Fmt(plus.Demand3)} / {Fmt(plus.Ceiling3)} N m (frac {Fmt(plus.DemandFraction3)}) | velocity {Fmt(plus.Velocity3)} rad/s | solver {Fmt(plus.Solver3)} N m | travel - {Fmt(plus.TravelNeg3)} / + {Fmt(plus.TravelPos3)} deg | prox {Fmt(plus.LimitProximity3)}");
            report.AppendLine($"-1 deg isolated: actual {Fmt(minus.Actual0)} -> {Fmt(minus.Actual3)} deg (delta {Fmt(minusDelta)}) | requested {Fmt(minus.Requested3)} applied {Fmt(minus.Applied3)} | demand {Fmt(minus.Demand3)} / {Fmt(minus.Ceiling3)} N m (frac {Fmt(minus.DemandFraction3)}) | solver {Fmt(minus.Solver3)} N m | travel - {Fmt(minus.TravelNeg3)} / + {Fmt(minus.TravelPos3)} deg");
            report.AppendLine($"measured actuator effect vs quiet: +1 delta-minus-quiet {Fmt(plusDelta - quietDelta)} deg, -1 delta-minus-quiet {Fmt(minusDelta - quietDelta)} deg | COP across effect +1 {Fmt(plusCopDelta * 1000f)} mm, -1 {Fmt(minusCopDelta * 1000f)} mm | cross-foot +1 {Fmt(plusCrossFoot * 1000f)} mm");
            report.AppendLine($"Y locked: +1 realized Y twist {Fmt(plus.Y3)} deg, -1 {Fmt(minus.Y3)} deg | X flexion {Fmt(plus.X3)} / {Fmt(minus.X3)} deg");
            report.AppendLine($"over-limit +60 deg: realized max {Fmt(limit.MaxActual)} deg, final {Fmt(limit.FinalActual)} deg, applied target {Fmt(limit.FinalApplied)} / requested {Fmt(limit.FinalRequested)} deg, min travel {Fmt(limit.MinTravel)} deg, max demand fraction {Fmt(limit.MaxDemandFraction)}, limit proximity {Fmt(limit.MaxLimitProximity)}, final solver {Fmt(limit.FinalSolver)} N m, channels {limit.Channels}, saturated {limit.Saturated}, rateLimited {limit.RateLimited}");

            F.WriteEvidence("gate1-ankle-roll-z.txt", report.ToString());
            F.WriteEvidence("gate1-ankle-roll-z.csv", quiet.Rows.ToString() + plus.Rows + minus.Rows + limit.Rows);

            Assert.That(plus.Channels && minus.Channels && limit.Channels, Is.True, "an ankle Z channel is missing:\n" + report);
            Assert.That(plus.Ceiling3, Is.EqualTo(DeadliftAnkleRoll.CeilingNm).Within(0.01f), "the active Z ceiling is not 60 N m:\n" + report);
            Assert.That(plus.Applied3 - plus.Actual3, Is.GreaterThan(0f), "the +1 deg Z target did not lead the realized angle:\n" + report);
            Assert.That(plusDelta - quietDelta, Is.GreaterThan(0.15f), "the +1 deg Z command did not move the realized angle beyond the quiet drift:\n" + report);
            Assert.That(minusDelta - quietDelta, Is.LessThan(-0.15f), "the -1 deg Z command did not move the realized angle beyond the quiet drift:\n" + report);
            Assert.That(plusCopDelta * minusCopDelta, Is.LessThan(0f), "the COP across the sole did not reverse with the Z command sign:\n" + report);
            Assert.That(Mathf.Abs(plus.Y3), Is.LessThan(0.5f), "the locked ankle Y axis moved under a Z command:\n" + report);
            Assert.That(Mathf.Abs(minus.Y3), Is.LessThan(0.5f), "the locked ankle Y axis moved under a Z command:\n" + report);
            Assert.That(plus.DemandFraction3, Is.LessThan(PoweredJointController.ModeledDemandSaturationThreshold), "the Z channel saturated on a 1 deg command:\n" + report);
            Assert.That(plus.TravelNeg3, Is.GreaterThan(0f), "the +1 deg command lost negative Z travel:\n" + report);
            Assert.That(plus.TravelPos3, Is.GreaterThan(0f), "the +1 deg command lost positive Z travel:\n" + report);
            // The isolated hinge fixture above holds the declared +15.00 deg
            // limit exactly. On the loaded foot, the same soft limit couples to
            // contact and a 60 deg (far beyond any game input) abuse command
            // realizes 19.7 deg: the measured engine compliance is reported, not
            // widened, and the nominal roll channel stays near zero.
            Assert.That(limit.MaxActual, Is.LessThan(20f), "the realized roll ran far beyond the declared +15 deg stop under a 60 deg command:\n" + report);
            Assert.That(limit.MinTravel, Is.GreaterThan(-5f), "the roll crossed its declared stop by more than the measured soft-stop compliance:\n" + report);
            Assert.That(limit.MaxLimitProximity, Is.LessThanOrEqualTo(1f), "the realized roll left its declared range:\n" + report);
        }

        [UnityTest, Timeout(300000)]
        public IEnumerator GATE1_ANKLE_ROLL_LIMIT_FIXTURE_HOLDS_THE_DECLARED_Z_RANGE_AT_THE_60NM_CEILING()
        {
            // Independent engine fixture: the same hinge topology and the same
            // PoweredJointController secondary profile as the deadlift ankle,
            // with no foot contact to confound the limit. A +45 deg command can
            // only realize the declared +/-15 deg range if the Z limit is active.
            Scene scene = SceneManager.CreateScene(
                "GAM70.AnkleRollLimitFixture." + Guid.NewGuid().ToString("N"),
                new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            GameObject parentObject = new GameObject("parent");
            GameObject childObject = new GameObject("child");
            SceneManager.MoveGameObjectToScene(parentObject, scene);
            SceneManager.MoveGameObjectToScene(childObject, scene);
            Rigidbody parent = parentObject.AddComponent<Rigidbody>();
            parent.isKinematic = true;
            parent.useGravity = false;
            Rigidbody child = childObject.AddComponent<Rigidbody>();
            child.useGravity = false;
            child.angularDamping = 0f;
            ConfigurableJoint joint = childObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = Vector3.zero;
            joint.connectedAnchor = Vector3.zero;
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Locked;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -45f, contactDistance = 2f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 55f, contactDistance = 2f };
            joint.angularZLimit = new SoftJointLimit { limit = DeadliftAnkleRoll.LimitDegrees, contactDistance = 2f };
            joint.projectionMode = JointProjectionMode.None;

            var recipe = new PhysicalJointRecipe("left_foot", HumanBodyBones.LeftFoot, PhysicalJointKind.Hinge,
                Vector3.right, -45f, 55f, 0f, "ankle");
            var runtime = new PhysicalAthleteRig.JointRuntime(recipe, joint, Vector3.zero);
            var controller = new PoweredJointController(new[] { runtime });
            controller.EnableSecondaryChannel("left_foot", DeadliftAnkleRoll.Profile);
            controller.SetJointCommand("left_foot", new JointCommand(
                Quaternion.AngleAxis(45f, Vector3.forward), Vector3.zero, 1f, 1f));

            var report = new StringBuilder();
            float maxActual = float.MinValue, minActual = float.MaxValue, maxDemandFraction = 0f;
            bool saturated = false;
            PhysicsScene physics = scene.GetPhysicsScene();
            for (int i = 0; i < 300; i++)
            {
                controller.Step(new SimulationTime(0ul, 0d), PlayerIntentFrame.Empty);
                physics.Simulate((float)SimulationConstants.FixedDeltaTimeSeconds);
                controller.CapturePostPhysicsDiagnostics();
                PoweredJointDiagnostic d = controller.GetJoint("left_foot").PostPhysicsDiagnostic;
                maxActual = Mathf.Max(maxActual, d.SecondaryActualAngleDeg);
                minActual = Mathf.Min(minActual, d.SecondaryActualAngleDeg);
                maxDemandFraction = Mathf.Max(maxDemandFraction, d.SecondaryDriveDemandFraction);
                saturated |= d.SecondaryDriveSaturated;
            }
            PoweredJointDiagnostic end = controller.GetJoint("left_foot").PostPhysicsDiagnostic;
            report.AppendLine("schema " + DeadliftAnkleRoll.TopologyVersion + " (isolated hinge fixture, 60 N m ceiling, +/-15 deg declared Z)");
            report.AppendLine($"commanded +45 deg: realized max {Fmt(maxActual)} deg, min {Fmt(minActual)} deg, final {Fmt(end.SecondaryActualAngleDeg)} deg | requested {Fmt(end.SecondaryRequestedAngleDeg)} applied {Fmt(end.SecondaryAppliedAngleDeg)} | demand {Fmt(end.SecondaryDriveDemandNm)} / {Fmt(end.SecondaryMaximumForceNm)} N m (max frac {Fmt(maxDemandFraction)}) saturated {saturated} | travel - {Fmt(end.SecondaryTravelToNegativeLimitDeg)} / + {Fmt(end.SecondaryTravelToPositiveLimitDeg)} deg | limit proximity {Fmt(end.SecondaryLimitProximity)}");
            F.WriteEvidence("gate1-ankle-roll-limit-fixture.txt", report.ToString());
            Assert.That(maxActual, Is.LessThan(DeadliftAnkleRoll.LimitDegrees + 0.01f), "the isolated hinge did not enforce its declared +15 deg Z limit:\n" + report);
            Assert.That(minActual, Is.GreaterThan(-0.01f), "the isolated hinge left its declared Z range on the negative side:\n" + report);
            Assert.That(end.SecondaryMaximumForceNm, Is.EqualTo(DeadliftAnkleRoll.CeilingNm).Within(0.01f), "the isolated Z drive is not at the 60 N m ceiling:\n" + report);
            Assert.That(maxDemandFraction, Is.GreaterThan(1f), "a +45 deg command did not demand more than the 60 N m ceiling:\n" + report);
            Assert.That(saturated, Is.True, "the isolated Z drive never reported channel saturation:\n" + report);
            SceneManager.UnloadSceneAsync(scene);
            yield return null;
        }
    }
}
