using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H8. Isolates the cause of the static stiffness discrepancy
    /// measured in 5H7, where production drives settled at roughly a seventh
    /// to a quarter of their authored positionSpring.
    ///
    /// The measurement stands; the explanation does not exist yet. This suite
    /// varies one thing at a time around a reproduced baseline — drive mode,
    /// the motion state of the other angular axes, solver iterations,
    /// timestep, force ceiling, force versus acceleration mode, and inertia —
    /// so the cause is discriminated rather than asserted.
    ///
    /// Every fixture uses a kinematic parent and a known horizontal gravity
    /// lever. That scaffolding is diagnostic and authorises nothing in the
    /// game.
    /// </summary>
    public sealed class D6DriveSemanticsTests
    {
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private SimulationMode _previousSimulationMode;
        private float _previousFixedDelta;

        [SetUp]
        public void SetUp()
        {
            _previousSimulationMode = Physics.simulationMode;
            _previousFixedDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject spawned in _spawned)
                if (spawned != null)
                    UnityEngine.Object.DestroyImmediate(spawned);
            _spawned.Clear();
            Physics.simulationMode = _previousSimulationMode;
            Time.fixedDeltaTime = _previousFixedDelta;
        }

        /// <summary>Everything a fixture varies, defaulted to production.</summary>
        private struct Spec
        {
            public string ChildId;
            public RotationDriveMode DriveMode;
            public ConfigurableJointMotion SecondaryMotion;
            public float Dt;
            public int SolverIterations;
            public int SolverVelocityIterations;
            public float MaximumForce;
            public bool UseAcceleration;
            public float InertiaScale;
            public float SecondaryLimitDeg;

            public static Spec Production(string childId) => new Spec
            {
                ChildId = childId,
                DriveMode = ProductionDriveMode(childId),
                SecondaryMotion = ProductionSecondaryMotion(childId),
                Dt = 0.01f,
                SolverIterations = 6,
                SolverVelocityIterations = 1,
                MaximumForce = ProductionMaximumForce(childId),
                UseAcceleration = false,
                InertiaScale = 1f,
                SecondaryLimitDeg = FindJoint(childId).SecondaryLimitDegrees
            };
        }

        private struct Result
        {
            public float SettledDeg;
            public float SettledOmega;
            public float GravityMoment;
            public float EffectiveStiffness;
            public float Ratio;
            public float LimitMarginDeg;
            public float CurrentTorqueX;
        }

        // ---------------------------------------------------------------
        // Parity. The H7 fixture drove every family through Slerp, including
        // the ankle, which production builds as a hinge on angularXDrive with
        // its other angular axes locked. That number was measured on a
        // configuration the game does not run.
        // ---------------------------------------------------------------
        [Test]
        public void H8_FIXTURE_PRODUCTION_PARITY()
        {
            foreach (string childId in new[] { "abdomen", "thorax", "left_thigh", "left_foot" })
            {
                PhysicalJointRecipe recipe = FindJoint(childId);
                bool hinge = recipe.Kind == PhysicalJointKind.Hinge;

                Fixture fixture = Build(Spec.Production(childId), useGravity: false, leverM: 0f);
                Assert.That(fixture.Joint.rotationDriveMode,
                    Is.EqualTo(hinge ? RotationDriveMode.XYAndZ : RotationDriveMode.Slerp),
                    $"{childId} drive mode must match production.");
                Assert.That(fixture.Joint.angularYMotion,
                    Is.EqualTo(hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited),
                    $"{childId} secondary angular motion must match production.");

                JointDrive driven = hinge ? fixture.Joint.angularXDrive : fixture.Joint.slerpDrive;
                JointDrive idle = hinge ? fixture.Joint.slerpDrive : fixture.Joint.angularXDrive;
                Assert.That(driven.positionSpring, Is.EqualTo(fixture.Profile.Spring),
                    $"{childId} must carry the family spring on the drive production actually uses.");
                Assert.That(driven.positionDamper, Is.EqualTo(fixture.Profile.Damper));
                Assert.That(driven.useAcceleration, Is.False);
                Assert.That(idle.positionSpring, Is.EqualTo(0f),
                    $"{childId} must leave the unused drive at zero, as production does.");
                Assert.That(fixture.Joint.angularXMotion, Is.EqualTo(ConfigurableJointMotion.Limited));
                Assert.That(fixture.Joint.configuredInWorldSpace, Is.False);
                Assert.That(fixture.Joint.projectionMode, Is.EqualTo(JointProjectionMode.None));
                Assert.That(fixture.Joint.swapBodies, Is.False);

                Teardown(fixture);
            }
        }

        // ---------------------------------------------------------------
        // Reproduce the H7 result on the corrected fixture, three runs each.
        // ---------------------------------------------------------------
        [Test]
        public void H8_STATIC_DISCREPANCY_REPRODUCTION()
        {
            var report = new StringBuilder();
            report.AppendLine("child,run,drive_mode,authored_k,settled_deg,settled_omega," +
                              "gravity_moment_nm,k_eff,ratio,limit_margin_deg,current_torque_x");

            foreach (string childId in new[] { "abdomen", "thorax", "left_thigh", "left_foot" })
            {
                for (int run = 0; run < 3; run++)
                {
                    Spec spec = Spec.Production(childId);
                    Result result = MeasureStatic(spec);
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3:F0},{4:F4},{5:F6},{6:F4},{7:F2},{8:F4},{9:F2},{10:F3}",
                        childId, run, spec.DriveMode, ResolveProfile(childId).Spring,
                        result.SettledDeg, result.SettledOmega, result.GravityMoment,
                        result.EffectiveStiffness, result.Ratio, result.LimitMarginDeg,
                        result.CurrentTorqueX));
                }
            }

            WriteMeasurement("GAM11-h8-static-reproduction.csv", report.ToString());
            Debug.Log("[H8 STATIC REPRODUCTION]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // One variable at a time around the abdomen baseline.
        // ---------------------------------------------------------------
        [Test]
        public void H8_CAUSE_DISCRIMINATION_SWEEP()
        {
            var report = new StringBuilder();
            report.AppendLine("child,variation,value,authored_k,settled_deg,gravity_moment_nm,k_eff,ratio");

            foreach (string childId in new[] { "abdomen", "left_foot" })
            {
                float authored = ResolveProfile(childId).Spring;
                void Record(string variation, object value, Spec spec)
                {
                    Result result = MeasureStatic(spec);
                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3:F0},{4:F4},{5:F4},{6:F2},{7:F4}",
                        childId, variation, value, authored, result.SettledDeg,
                        result.GravityMoment, result.EffectiveStiffness, result.Ratio));
                }

                Record("baseline", "production", Spec.Production(childId));

                // Drive mode. Is the shortfall specific to Slerp?
                foreach (RotationDriveMode mode in new[] { RotationDriveMode.Slerp, RotationDriveMode.XYAndZ })
                {
                    Spec spec = Spec.Production(childId);
                    spec.DriveMode = mode;
                    // Slerp needs the other angular axes unlocked to be valid.
                    if (mode == RotationDriveMode.Slerp)
                        spec.SecondaryMotion = ConfigurableJointMotion.Limited;
                    Record("drive_mode", mode, spec);
                }

                // Motion state of the non-primary angular axes.
                foreach (ConfigurableJointMotion motion in new[]
                {
                    ConfigurableJointMotion.Free,
                    ConfigurableJointMotion.Limited,
                    ConfigurableJointMotion.Locked
                })
                {
                    Spec spec = Spec.Production(childId);
                    spec.SecondaryMotion = motion;
                    if (motion == ConfigurableJointMotion.Locked)
                        spec.DriveMode = RotationDriveMode.XYAndZ;
                    Record("secondary_motion", motion, spec);
                }

                // Solver iterations. Six position and one velocity is what the
                // project runs; a soft constraint solved to convergence should
                // not care.
                foreach (int iterations in new[] { 4, 6, 8, 12, 16, 32 })
                {
                    Spec spec = Spec.Production(childId);
                    spec.SolverIterations = iterations;
                    Record("solver_position_iterations", iterations, spec);
                }
                foreach (int iterations in new[] { 1, 4, 8 })
                {
                    Spec spec = Spec.Production(childId);
                    spec.SolverVelocityIterations = iterations;
                    Record("solver_velocity_iterations", iterations, spec);
                }

                // Timestep. A realised spring should be timestep independent.
                foreach (float dt in new[] { 0.005f, 0.01f, 0.02f })
                {
                    Spec spec = Spec.Production(childId);
                    spec.Dt = dt;
                    Record("dt", dt, spec);
                }

                // Force ceiling, all far above the couple of Nm needed.
                foreach (float ceiling in new[] { 100f, 500f, 1500f, 100000f })
                {
                    Spec spec = Spec.Production(childId);
                    spec.MaximumForce = ceiling;
                    Record("max_force", ceiling, spec);
                }

                // Force versus acceleration mode. Diagnostic only; the two have
                // different physical semantics and this is not a tuning knob.
                foreach (bool acceleration in new[] { false, true })
                {
                    Spec spec = Spec.Production(childId);
                    spec.UseAcceleration = acceleration;
                    Record("use_acceleration", acceleration, spec);
                }

                // Inertia at constant gravity moment. A true static spring
                // balance should not care about inertia at all.
                foreach (float scale in new[] { 0.5f, 1f, 2f })
                {
                    Spec spec = Spec.Production(childId);
                    spec.InertiaScale = scale;
                    Record("inertia_scale", scale, spec);
                }
            }

            WriteMeasurement("GAM11-h8-cause-discrimination.csv", report.ToString());
            Debug.Log("[H8 CAUSE DISCRIMINATION]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // The one combination the sweep leaves open.
        //
        // The ankle, on XYAndZ, converges to its authored spring almost
        // exactly once the solver is given enough iterations: 0.105 at four,
        // 0.202 at the project's six, 0.999 at thirty-two. The abdomen, on
        // Slerp, plateaus around a quarter no matter how many iterations it
        // gets. Crossing drive mode with iteration count separates a solver
        // convergence limit from something specific to Slerp.
        // ---------------------------------------------------------------
        [Test]
        public void H8_DRIVE_MODE_BY_SOLVER_ITERATION()
        {
            var report = new StringBuilder();
            report.AppendLine("child,drive_mode,secondary_motion,solver_iterations,dt,authored_k,k_eff,ratio");

            foreach (string childId in new[] { "abdomen", "thorax", "left_thigh", "left_foot" })
            {
                foreach (RotationDriveMode mode in new[] { RotationDriveMode.Slerp, RotationDriveMode.XYAndZ })
                {
                    foreach (int iterations in new[] { 6, 16, 32, 64 })
                    {
                        Spec spec = Spec.Production(childId);
                        spec.DriveMode = mode;
                        // Slerp requires the other angular axes unlocked to be
                        // a valid configuration at all.
                        spec.SecondaryMotion = mode == RotationDriveMode.Slerp
                            ? ConfigurableJointMotion.Limited
                            : ProductionSecondaryMotion(childId);
                        spec.SolverIterations = iterations;

                        Result result = MeasureStatic(spec);
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2},{3},{4},{5:F0},{6:F2},{7:F4}",
                            childId, mode, spec.SecondaryMotion, iterations, spec.Dt,
                            ResolveProfile(childId).Spring, result.EffectiveStiffness, result.Ratio));
                    }
                }
            }

            WriteMeasurement("GAM11-h8-drivemode-by-iteration.csv", report.ToString());
            Debug.Log("[H8 DRIVE MODE BY SOLVER ITERATION]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // Measurement. Settle, then sample only once the body is stationary.
        // ---------------------------------------------------------------
        private Result MeasureStatic(Spec spec)
        {
            PhysicalSegmentRecipe segment = FindSegment(spec.ChildId);
            Vector3 size = segment.DimensionsMeters;
            float lever = 0.5f * Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            Time.fixedDeltaTime = spec.Dt;
            Fixture fixture = Build(spec, useGravity: true, leverM: lever);
            fixture.SetLogicalSagittalTarget(0f);

            // Fixed physical duration, so the settle is the same regardless of
            // the timestep under test.
            int steps = Mathf.CeilToInt(6f / spec.Dt);
            for (int step = 0; step < steps; step++)
                Physics.Simulate(spec.Dt);

            float settledRad = SagittalRadians(
                Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation);
            float mass = fixture.Child.mass;
            float gravityMoment = mass * 9.81f * lever * Mathf.Cos(settledRad);
            float effective = Mathf.Abs(settledRad) > 1e-5f
                ? gravityMoment / Mathf.Abs(settledRad)
                : float.NaN;

            PhysicalJointRecipe recipe = FindJoint(spec.ChildId);
            float settledDeg = settledRad * Mathf.Rad2Deg;
            var result = new Result
            {
                SettledDeg = settledDeg,
                SettledOmega = fixture.Child.angularVelocity.x,
                GravityMoment = gravityMoment,
                EffectiveStiffness = effective,
                Ratio = effective / fixture.Profile.Spring,
                LimitMarginDeg = settledDeg >= 0f
                    ? recipe.HighDegrees - settledDeg
                    : settledDeg - recipe.LowDegrees,
                CurrentTorqueX = fixture.Joint.currentTorque.x
            };

            Teardown(fixture);
            return result;
        }

        private sealed class Fixture
        {
            public Rigidbody Parent;
            public Rigidbody Child;
            public ConfigurableJoint Joint;
            public JointFamilyProfile Profile;

            public void SetLogicalSagittalTarget(float sagittalRad)
            {
                Quaternion logical = Quaternion.AngleAxis(sagittalRad * Mathf.Rad2Deg, Vector3.right);
                Joint.targetRotation = PoweredJointController.ToUnityTargetRotation(logical);
                Joint.targetAngularVelocity = Vector3.zero;
            }
        }

        private Fixture Build(Spec spec, bool useGravity, float leverM)
        {
            PhysicalSegmentRecipe segment = FindSegment(spec.ChildId);
            PhysicalJointRecipe recipe = FindJoint(spec.ChildId);
            JointFamilyProfile profile = ResolveProfile(spec.ChildId);

            var parentObject = new GameObject("D6FixtureParent");
            var childObject = new GameObject("D6FixtureChild");
            _spawned.Add(parentObject);
            _spawned.Add(childObject);

            Rigidbody parent = parentObject.AddComponent<Rigidbody>();
            parent.isKinematic = true;
            parent.useGravity = false;

            Rigidbody child = childObject.AddComponent<Rigidbody>();
            child.mass = segment.MassFraction * 100f;
            child.useGravity = useGravity;
            child.automaticInertiaTensor = false;
            child.inertiaTensor = PhysicalAthleteDefinition.BoxInertia(child.mass, segment.DimensionsMeters)
                * spec.InertiaScale;
            child.inertiaTensorRotation = Quaternion.identity;
            // Horizontal lever, so gravity makes a real moment about the driven
            // axis instead of hanging parallel to it.
            child.centerOfMass = new Vector3(0f, 0f, leverM);
            child.sleepThreshold = 0f;
            child.angularDamping = 0f;
            child.linearDamping = 0f;
            child.solverIterations = spec.SolverIterations;
            child.solverVelocityIterations = spec.SolverVelocityIterations;
            child.maxAngularVelocity = 50f;

            var joint = childObject.AddComponent<ConfigurableJoint>();
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
            joint.angularYMotion = spec.SecondaryMotion;
            joint.angularZMotion = spec.SecondaryMotion;
            joint.lowAngularXLimit = new SoftJointLimit { limit = recipe.LowDegrees };
            joint.highAngularXLimit = new SoftJointLimit { limit = recipe.HighDegrees };
            joint.angularYLimit = new SoftJointLimit { limit = Mathf.Max(1f, spec.SecondaryLimitDeg) };
            joint.angularZLimit = new SoftJointLimit { limit = Mathf.Max(1f, spec.SecondaryLimitDeg) };
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            joint.configuredInWorldSpace = false;
            joint.swapBodies = false;
            joint.rotationDriveMode = spec.DriveMode;

            var drive = new JointDrive
            {
                positionSpring = profile.Spring,
                positionDamper = profile.Damper,
                maximumForce = spec.MaximumForce,
                useAcceleration = spec.UseAcceleration
            };
            if (spec.DriveMode == RotationDriveMode.Slerp)
            {
                joint.angularXDrive = ZeroDrive();
                joint.angularYZDrive = ZeroDrive();
                joint.slerpDrive = drive;
            }
            else
            {
                joint.angularXDrive = drive;
                joint.angularYZDrive = ZeroDrive();
                joint.slerpDrive = ZeroDrive();
            }

            return new Fixture { Parent = parent, Child = child, Joint = joint, Profile = profile };
        }

        private void Teardown(Fixture fixture)
        {
            if (fixture == null)
                return;
            if (fixture.Child != null)
                UnityEngine.Object.DestroyImmediate(fixture.Child.gameObject);
            if (fixture.Parent != null)
                UnityEngine.Object.DestroyImmediate(fixture.Parent.gameObject);
        }

        private static RotationDriveMode ProductionDriveMode(string childId) =>
            FindJoint(childId).Kind == PhysicalJointKind.Hinge
                ? RotationDriveMode.XYAndZ
                : RotationDriveMode.Slerp;

        private static ConfigurableJointMotion ProductionSecondaryMotion(string childId) =>
            FindJoint(childId).Kind == PhysicalJointKind.Hinge
                ? ConfigurableJointMotion.Locked
                : ConfigurableJointMotion.Limited;

        private static JointDrive ZeroDrive() => new JointDrive
        {
            positionSpring = 0f,
            positionDamper = 0f,
            maximumForce = 0f,
            useAcceleration = false
        };

        private static PhysicalSegmentRecipe FindSegment(string id)
        {
            foreach (PhysicalSegmentRecipe segment in PhysicalAthleteDefinition.Segments)
                if (string.Equals(segment.Id, id, StringComparison.Ordinal))
                    return segment;
            throw new ArgumentException($"No segment '{id}'.", nameof(id));
        }

        private static PhysicalJointRecipe FindJoint(string childId)
        {
            foreach (PhysicalJointRecipe joint in PhysicalAthleteDefinition.Joints)
                if (string.Equals(joint.ChildId, childId, StringComparison.Ordinal))
                    return joint;
            throw new ArgumentException($"No joint '{childId}'.", nameof(childId));
        }

        private static JointFamilyProfile ResolveProfile(string childId)
        {
            JointFamilyProfile? profile = PoweredJointController.FindFamilyProfile(FindJoint(childId).Family);
            if (!profile.HasValue)
                throw new InvalidOperationException($"No family profile for '{childId}'.");
            return profile.Value;
        }

        private static float ProductionMaximumForce(string childId)
        {
            JointFamilyProfile profile = ResolveProfile(childId);
            float familyScale = childId switch
            {
                "left_foot" or "right_foot" => 2.5f,
                "left_shank" or "right_shank" => 1.8f,
                _ => 1.5f
            };
            return profile.BaseCapacityNm * 3.8f * familyScale;
        }

        private static float SagittalRadians(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(
                rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return 0f;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            return rotation.x * (angle / magnitude);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }
    }
}
