using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;
using UnityEngine.TestTools;

namespace PowerliftingSimulator.Tests
{
    /// <summary>
    /// Phase 5H7. Measures what a production ConfigurableJoint drive actually
    /// delivers, because nothing in this project has ever measured it.
    ///
    /// Every authority claim in GAM-11 so far has rested on one of two
    /// quantities, and neither is a torque measurement. Spring times error is
    /// the authored PD command, a model of what was asked for. Unity's
    /// currentTorque is the constraint torque the solver needed to satisfy
    /// every constraint on the joint at once, which includes the limits and
    /// the connected inertia. The delivered motor moment has been inferred
    /// from angles and never observed.
    ///
    /// The fixture isolates it: a kinematic parent, one dynamic child with
    /// production mass and inertia, the production joint and drive
    /// configuration, gravity off, and the joint anchored at the child's
    /// centre of mass so a commanded error produces a pure rotation about a
    /// known inertia. Torque then follows from angular acceleration and
    /// nothing else.
    ///
    /// The kinematic parent and disabled gravity are diagnostic scaffolding.
    /// They are confined to this fixture and authorise nothing in the game.
    /// </summary>
    public sealed class ConfigurableJointActuatorCharacterizationTests
    {
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const float Dt = 0.01f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private SimulationMode _previousSimulationMode;
        private float _previousFixedDelta;

        [SetUp]
        public void SetUp()
        {
            _previousSimulationMode = Physics.simulationMode;
            _previousFixedDelta = Time.fixedDeltaTime;
            Physics.simulationMode = SimulationMode.Script;
            Time.fixedDeltaTime = Dt;
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

        // ---------------------------------------------------------------
        // Tier A. Target error to delivered torque, zero gravity.
        // ---------------------------------------------------------------
        [Test]
        public void T1_TARGET_TO_TORQUE_GAIN()
        {
            var report = new StringBuilder();
            report.AppendLine("family,target_error_deg,inertia_x,alpha_x,inferred_tau_nm," +
                              "expected_pd_tau_nm,current_torque_x,spring,damper,max_force_nm");

            var summary = new StringBuilder();
            summary.AppendLine("family,authored_spring,measured_gain_nm_per_rad,ratio,r2,max_force_nm");

            foreach (string family in new[] { "abdomen", "thorax", "left_thigh", "left_foot" })
            {
                float[] errorsDeg = { -5f, -3f, -2f, -1f, 1f, 2f, 3f, 5f };
                var x = new float[errorsDeg.Length];
                var y = new float[errorsDeg.Length];

                for (int index = 0; index < errorsDeg.Length; index++)
                {
                    Fixture fixture = BuildFixture(family, useGravity: false);

                    // The drive sees the error on the very first step with the
                    // child still at rest, so the damper contributes nothing
                    // and what is measured is the position term alone.
                    fixture.SetLogicalSagittalTarget(errorsDeg[index] * Mathf.Deg2Rad);
                    Physics.Simulate(Dt);

                    float alpha = fixture.Child.angularVelocity.x / Dt;
                    float inertia = fixture.Child.inertiaTensor.x;
                    float inferred = inertia * alpha;
                    float expected = fixture.Profile.Spring * (errorsDeg[index] * Mathf.Deg2Rad);

                    x[index] = errorsDeg[index] * Mathf.Deg2Rad;
                    y[index] = inferred;

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F1},{2:F6},{3:F4},{4:F4},{5:F4},{6:F4},{7:F0},{8:F0},{9:F1}",
                        family, errorsDeg[index], inertia, alpha, inferred, expected,
                        fixture.Joint.currentTorque.x,
                        fixture.Profile.Spring, fixture.Profile.Damper, fixture.MaximumForce));

                    Teardown(fixture);
                }

                Fit fit = LeastSquares(x, y);
                JointFamilyProfile profile = ResolveProfile(family);
                summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F0},{2:F2},{3:F4},{4:F5},{5:F1}",
                    family, profile.Spring, fit.Slope, fit.Slope / profile.Spring, fit.RSquared,
                    ProductionMaximumForce(family)));
            }

            var combined = new StringBuilder();
            combined.Append(report);
            combined.AppendLine("# summary");
            combined.Append(summary);
            WriteMeasurement("GAM11-actuator-target-to-torque.csv", combined.ToString());
            Debug.Log("[T1 TARGET TO TORQUE]" + Environment.NewLine + combined);
        }

        // ---------------------------------------------------------------
        // Tier A. Damper, from deceleration with the target already met.
        // ---------------------------------------------------------------
        [Test]
        public void T2_VELOCITY_TO_TORQUE_GAIN()
        {
            var report = new StringBuilder();
            report.AppendLine("family,omega_x,inertia_x,alpha_x,inferred_tau_nm,expected_damper_tau_nm,damper");
            var summary = new StringBuilder();
            summary.AppendLine("family,authored_damper,measured_gain_nms_per_rad,ratio,r2");

            foreach (string family in new[] { "abdomen", "thorax" })
            {
                float[] omegas = { -1f, -0.5f, -0.25f, 0.25f, 0.5f, 1f };
                var x = new float[omegas.Length];
                var y = new float[omegas.Length];

                for (int index = 0; index < omegas.Length; index++)
                {
                    Fixture fixture = BuildFixture(family, useGravity: false);
                    fixture.SetLogicalSagittalTarget(0f);
                    fixture.Child.angularVelocity = new Vector3(omegas[index], 0f, 0f);

                    Physics.Simulate(Dt);

                    float alpha = (fixture.Child.angularVelocity.x - omegas[index]) / Dt;
                    float inertia = fixture.Child.inertiaTensor.x;
                    float inferred = inertia * alpha;

                    x[index] = omegas[index];
                    y[index] = inferred;

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F3},{2:F6},{3:F4},{4:F4},{5:F4},{6:F0}",
                        family, omegas[index], inertia, alpha, inferred,
                        -fixture.Profile.Damper * omegas[index], fixture.Profile.Damper));

                    Teardown(fixture);
                }

                Fit fit = LeastSquares(x, y);
                JointFamilyProfile profile = ResolveProfile(family);
                summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F0},{2:F2},{3:F4},{4:F5}",
                    family, profile.Damper, -fit.Slope, -fit.Slope / profile.Damper, fit.RSquared));

                Teardown(null);
            }

            var combined = new StringBuilder();
            combined.Append(report);
            combined.AppendLine("# summary");
            combined.Append(summary);
            WriteMeasurement("GAM11-actuator-velocity-to-torque.csv", combined.ToString());
            Debug.Log("[T2 VELOCITY TO TORQUE]" + Environment.NewLine + combined);
        }

        // ---------------------------------------------------------------
        // Tier A. Does maximumForce cap the delivered moment, and where?
        // ---------------------------------------------------------------
        [Test]
        public void T3_MAXIMUM_FORCE_LIMIT_BEHAVIOR()
        {
            var report = new StringBuilder();
            report.AppendLine("family,ceiling_label,max_force_nm,target_error_deg," +
                              "expected_pd_tau_nm,inferred_tau_nm,ratio_to_ceiling");

            foreach (string family in new[] { "abdomen", "thorax" })
            {
                JointFamilyProfile profile = ResolveProfile(family);
                var ceilings = new (string Label, float Value)[]
                {
                    ("gam7_base", profile.BaseCapacityNm),
                    ("gam11_production", ProductionMaximumForce(family))
                };

                foreach ((string label, float ceiling) in ceilings)
                {
                    foreach (float errorDeg in new[] { 1f, 5f, 10f, 20f, 30f, 45f })
                    {
                        Fixture fixture = BuildFixture(family, useGravity: false, maximumForceOverride: ceiling);
                        fixture.SetLogicalSagittalTarget(errorDeg * Mathf.Deg2Rad);
                        Physics.Simulate(Dt);

                        float inferred = fixture.Child.inertiaTensor.x * (fixture.Child.angularVelocity.x / Dt);
                        report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                            "{0},{1},{2:F1},{3:F1},{4:F2},{5:F2},{6:F4}",
                            family, label, ceiling, errorDeg,
                            profile.Spring * errorDeg * Mathf.Deg2Rad, inferred,
                            ceiling > 0f ? inferred / ceiling : float.NaN));
                        Teardown(fixture);
                    }
                }
            }

            WriteMeasurement("GAM11-actuator-max-force-limit.csv", report.ToString());
            Debug.Log("[T3 MAXIMUM FORCE LIMIT]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // T4. Effective stiffness from a static gravity equilibrium.
        //
        // This is the measurement that means something. The first-step
        // acceleration tests cannot recover the spring: for the abdomen the
        // damper times the timestep is 0.85 and the spring times the timestep
        // squared is 0.08, against a segment inertia of 0.063, so PhysX's
        // implicit solve dominates the first step and torque inferred as
        // I times alpha under-reads by more than an order of magnitude.
        //
        // At equilibrium there is no such problem. Angular velocity and
        // acceleration are both zero, so the damper contributes nothing, the
        // discretization has washed out, and the drive torque must balance a
        // gravity moment that is known exactly from mass, lever and angle.
        // Effective stiffness follows by division, with no integration model
        // anywhere in it.
        // ---------------------------------------------------------------
        [Test]
        public void T4_STATIC_GRAVITY_EQUILIBRIUM_STIFFNESS()
        {
            var report = new StringBuilder();
            report.AppendLine("family,mass_kg,lever_m,authored_spring,settled_error_deg,settled_omega," +
                              "gravity_moment_nm,effective_stiffness_nm_per_rad,stiffness_ratio,max_force_nm");

            foreach (string family in new[] { "abdomen", "thorax", "left_thigh", "left_foot" })
            {
                // Lever from the joint anchor to the segment centre of mass,
                // taken from the production segment's own long dimension so
                // the gravity moment is representative rather than invented.
                PhysicalSegmentRecipe segment = FindSegment(family);
                // Half the segment's longest dimension. Taking the y extent
                // alone gives the thigh a zero lever, because its capsule is
                // authored along a different axis.
                Vector3 size = segment.DimensionsMeters;
                float lever = 0.5f * Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                Fixture fixture = BuildFixture(family, useGravity: true, comLeverM: lever);
                fixture.SetLogicalSagittalTarget(0f);

                // Long enough for the drive and gravity to come to rest.
                for (int step = 0; step < 600; step++)
                    Physics.Simulate(Dt);

                float settledRad = SagittalRadians(
                    Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation);
                float mass = fixture.Child.mass;
                float gravityMoment = mass * 9.81f * lever * Mathf.Cos(settledRad);
                float effective = Mathf.Abs(settledRad) > 1e-5f
                    ? gravityMoment / Mathf.Abs(settledRad)
                    : float.NaN;

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1:F2},{2:F4},{3:F0},{4:F3},{5:F5},{6:F3},{7:F1},{8:F4},{9:F1}",
                    family, mass, lever, fixture.Profile.Spring,
                    settledRad * Mathf.Rad2Deg, fixture.Child.angularVelocity.x,
                    gravityMoment, effective, effective / fixture.Profile.Spring,
                    fixture.MaximumForce));

                Teardown(fixture);
            }

            WriteMeasurement("GAM11-actuator-static-equilibrium.csv", report.ToString());
            Debug.Log("[T4 STATIC GRAVITY EQUILIBRIUM]" + Environment.NewLine + report);
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

        // ---------------------------------------------------------------
        // The fixture must be the production actuator, not a lookalike.
        // ---------------------------------------------------------------
        [Test]
        public void T0_ACTUATOR_FIXTURE_CONFIG_PARITY()
        {
            Fixture fixture = BuildFixture("abdomen", useGravity: false);
            ConfigurableJoint joint = fixture.Joint;

            Assert.That(joint.rotationDriveMode, Is.EqualTo(RotationDriveMode.Slerp),
                "Multiaxial production joints drive through slerpDrive.");
            Assert.That(joint.slerpDrive.positionSpring, Is.EqualTo(800f),
                "The trunk family spring is 800 Nm/rad.");
            Assert.That(joint.slerpDrive.positionDamper, Is.EqualTo(85f));
            Assert.That(joint.slerpDrive.useAcceleration, Is.False,
                "Production drives are force mode, so maximumForce is a torque ceiling.");
            Assert.That(joint.angularXDrive.positionSpring, Is.EqualTo(0f),
                "Slerp mode leaves the per-axis drives at zero in production.");
            Assert.That(joint.angularYZDrive.positionSpring, Is.EqualTo(0f));
            Assert.That(joint.configuredInWorldSpace, Is.False);
            Assert.That(joint.projectionMode, Is.EqualTo(JointProjectionMode.None));
            Assert.That(joint.xMotion, Is.EqualTo(ConfigurableJointMotion.Locked));
            Assert.That(joint.angularXMotion, Is.EqualTo(ConfigurableJointMotion.Limited));
            Assert.That(Mathf.Abs(joint.lowAngularXLimit.limit), Is.EqualTo(35f).Within(0.01f),
                "Abdomen anatomical limits are -35 and +45 degrees.");
            Assert.That(joint.highAngularXLimit.limit, Is.EqualTo(45f).Within(0.01f));
            Assert.That(fixture.Child.mass, Is.EqualTo(13.9f).Within(0.01f));
            Assert.That(fixture.Child.useGravity, Is.False);

            Teardown(fixture);
        }

        // ---------------------------------------------------------------
        // Fixture construction.
        // ---------------------------------------------------------------
        private sealed class Fixture
        {
            public Rigidbody Parent;
            public Rigidbody Child;
            public ConfigurableJoint Joint;
            public JointFamilyProfile Profile;
            public float MaximumForce;
            public Quaternion JointSpace;

            /// <summary>
            /// Commands a logical sagittal target the same way production
            /// does: build the rotation in joint space, then hand the joint
            /// the canonicalized inverse.
            /// </summary>
            public void SetLogicalSagittalTarget(float sagittalRad)
            {
                Quaternion logical = Quaternion.AngleAxis(sagittalRad * Mathf.Rad2Deg, Vector3.right);
                Joint.targetRotation = PoweredJointController.ToUnityTargetRotation(logical);
                Joint.targetAngularVelocity = Vector3.zero;
            }
        }

        private Fixture BuildFixture(string family, bool useGravity, float maximumForceOverride = -1f, float comLeverM = 0f)
        {
            PhysicalSegmentRecipe segment = FindSegment(family);
            JointFamilyProfile profile = ResolveProfile(family);
            float maximumForce = maximumForceOverride >= 0f ? maximumForceOverride : ProductionMaximumForce(family);

            var parentObject = new GameObject("ActuatorFixtureParent");
            var childObject = new GameObject("ActuatorFixtureChild");
            _spawned.Add(parentObject);
            _spawned.Add(childObject);

            Rigidbody parent = parentObject.AddComponent<Rigidbody>();
            parent.isKinematic = true;
            parent.useGravity = false;

            Rigidbody child = childObject.AddComponent<Rigidbody>();
            child.mass = segment.MassFraction * 100f;
            child.useGravity = useGravity;
            child.automaticInertiaTensor = false;
            child.inertiaTensor = PhysicalAthleteDefinition.BoxInertia(child.mass, segment.DimensionsMeters);
            child.inertiaTensorRotation = Quaternion.identity;
            // The lever is horizontal, along the anteroposterior axis. Hung
            // directly below the anchor the centre of mass gives r parallel to
            // gravity and a cross product of exactly zero, which is a pendulum
            // already at rest: the first attempt at this measurement read
            // 0.000 deg on every family for precisely that reason.
            child.centerOfMass = new Vector3(0f, 0f, comLeverM);
            // Sleeping would freeze the fixture at its spawn pose and report a
            // zero equilibrium error that never happened.
            child.sleepThreshold = 0f;

            var joint = childObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent;
            joint.autoConfigureConnectedAnchor = false;
            // Anchored at the child's centre of mass, so a commanded error
            // produces a pure rotation about a known inertia and the torque
            // follows from the angular acceleration alone.
            joint.anchor = Vector3.zero;   // child origin, which is the anchor point
            joint.connectedAnchor = Vector3.zero;
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = FindJoint(family).LowDegrees };
            joint.highAngularXLimit = new SoftJointLimit { limit = FindJoint(family).HighDegrees };
            joint.angularYLimit = new SoftJointLimit { limit = 25f };
            joint.angularZLimit = new SoftJointLimit { limit = 25f };
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            joint.configuredInWorldSpace = false;
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.angularXDrive = ZeroDrive();
            joint.angularYZDrive = ZeroDrive();
            joint.slerpDrive = new JointDrive
            {
                positionSpring = profile.Spring,
                positionDamper = profile.Damper,
                maximumForce = maximumForce,
                useAcceleration = false
            };

            return new Fixture
            {
                Parent = parent,
                Child = child,
                Joint = joint,
                Profile = profile,
                MaximumForce = maximumForce,
                JointSpace = Quaternion.identity
            };
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

        /// <summary>
        /// The ceiling production actually reaches at unloaded standing:
        /// the GAM-7 base capacity, times the adapter's 3.8 load scale, times
        /// the per-family scale it applies on top. Reproduced here rather than
        /// read from a live adapter so the fixture stays isolated.
        /// </summary>
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

        private readonly struct Fit
        {
            public Fit(float slope, float rSquared)
            {
                Slope = slope;
                RSquared = rSquared;
            }

            public float Slope { get; }
            public float RSquared { get; }
        }

        private static Fit LeastSquares(float[] x, float[] y)
        {
            float sumX = 0f, sumY = 0f, sumXy = 0f, sumXx = 0f;
            int count = 0;
            for (int index = 0; index < x.Length; index++)
            {
                if (!float.IsFinite(x[index]) || !float.IsFinite(y[index]))
                    continue;
                sumX += x[index]; sumY += y[index];
                sumXy += x[index] * y[index]; sumXx += x[index] * x[index];
                count++;
            }
            if (count < 2)
                return new Fit(float.NaN, float.NaN);

            float denominator = count * sumXx - sumX * sumX;
            float slope = Mathf.Abs(denominator) < 1e-12f ? float.NaN : (count * sumXy - sumX * sumY) / denominator;
            float intercept = (sumY - slope * sumX) / count;
            float meanY = sumY / count;

            float residual = 0f, total = 0f;
            for (int index = 0; index < x.Length; index++)
            {
                if (!float.IsFinite(x[index]) || !float.IsFinite(y[index]))
                    continue;
                float predicted = slope * x[index] + intercept;
                residual += (y[index] - predicted) * (y[index] - predicted);
                total += (y[index] - meanY) * (y[index] - meanY);
            }
            return new Fit(slope, total > 1e-18f ? 1f - residual / total : float.NaN);
        }

        private static void WriteMeasurement(string filename, string content)
        {
            Directory.CreateDirectory(Path.GetFullPath(MeasurementDirectory));
            File.WriteAllText(Path.Combine(Path.GetFullPath(MeasurementDirectory), filename), content);
        }
    }
}
