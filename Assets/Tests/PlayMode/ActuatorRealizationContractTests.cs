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
    /// The physical meaning of the athlete's authored drive parameters, held
    /// as a permanent contract.
    ///
    /// An authored positionSpring is an input to an iterative solve, not a
    /// statement about behaviour. Treating the two as the same thing cost this
    /// project five phases of controller work against a plant delivering a
    /// fifth of what it was written to: the ankle centre-of-pressure gain was
    /// out by a factor of three and the trunk folded onto its joint limit, and
    /// neither was visible from the authored numbers.
    ///
    /// So the numbers are measured here and asserted. If drive mode, solver
    /// profile, timestep, mass, inertia or the engine itself changes what the
    /// athlete actually delivers, this fails rather than quietly moving the
    /// ground under the controllers.
    ///
    /// Two classes, and neither is unmeasured. A LOAD_BEARING family carries
    /// the athlete and holds its authored stiffness within five percent. A
    /// DISTAL_POSE family does not carry load and is recorded against what it
    /// actually delivers.
    /// </summary>
    public sealed class ActuatorRealizationContractTests
    {
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const float Dt = 0.01f;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private SimulationMode _previousSimulationMode;
        private float _previousFixedDelta;

        private enum ActuatorClass
        {
            LoadBearing,
            DistalPose
        }

        private readonly struct Family
        {
            public Family(string childId, ActuatorClass actuatorClass)
            {
                ChildId = childId;
                Class = actuatorClass;
            }

            public string ChildId { get; }
            public ActuatorClass Class { get; }
        }

        /// <summary>
        /// The wrist is distal because the bar does not go through it. The
        /// saddle joint connects the barbell to the thorax, so the hands
        /// transmit no load in V1; that is production topology, not an
        /// assumption about what hands are for.
        /// </summary>
        private static readonly Family[] Families =
        {
            new Family("left_foot", ActuatorClass.LoadBearing),
            new Family("left_shank", ActuatorClass.LoadBearing),
            new Family("left_thigh", ActuatorClass.LoadBearing),
            new Family("abdomen", ActuatorClass.LoadBearing),
            new Family("thorax", ActuatorClass.LoadBearing),
            new Family("left_upper_arm", ActuatorClass.LoadBearing),
            new Family("left_forearm", ActuatorClass.LoadBearing),
            new Family("left_hand", ActuatorClass.DistalPose)
        };

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

        [Test]
        public void C1_ACTUATOR_STATIC_STIFFNESS_REALIZATION()
        {
            var report = new StringBuilder();
            report.AppendLine("child,family,class,drive_mode,position_iterations,dt," +
                              "k_authored,k_measured,k_ratio,d_authored,max_force_nm," +
                              "lambda_k,omega_n_dt,within_contract");

            var failures = new List<string>();

            foreach (Family family in Families)
            {
                JointFamilyProfile profile = ResolveProfile(family.ChildId);
                Measurement measurement = MeasureStatic(family.ChildId);
                float ratio = measurement.EffectiveStiffness / profile.Spring;
                bool within = ratio >= PhysicalAthleteSolverProfile.RealizationToleranceLow &&
                              ratio <= PhysicalAthleteSolverProfile.RealizationToleranceHigh;

                if (family.Class == ActuatorClass.LoadBearing && !within)
                {
                    failures.Add(
                        $"{family.ChildId} realises {ratio:F3} of its authored {profile.Spring:F0} Nm/rad");
                }

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1},{2},XYAndZ,{3},{4},{5:F0},{6:F2},{7:F4},{8:F0},{9:F1},{10:F2},{11:F3},{12}",
                    family.ChildId, FindJoint(family.ChildId).Family, family.Class,
                    PhysicalAthleteSolverProfile.PositionIterations, Dt,
                    profile.Spring, measurement.EffectiveStiffness, ratio, profile.Damper,
                    ProductionMaximumForce(family.ChildId),
                    profile.Spring * Dt * Dt / measurement.Inertia,
                    Mathf.Sqrt(profile.Spring / measurement.Inertia) * Dt,
                    within));
            }

            WriteMeasurement("GAM11-actuator-realization-contract.csv", report.ToString());
            Debug.Log("[C1 ACTUATOR REALIZATION CONTRACT]" + Environment.NewLine + report);

            Assert.That(failures, Is.Empty,
                "A load-bearing drive no longer delivers its authored stiffness: " +
                string.Join("; ", failures));
        }

        /// <summary>
        /// The wrist is recorded rather than exempted. It cannot realise its
        /// authored 250 Nm/rad into a 0.6 kg hand at a 10 ms step, and no
        /// lower value fixes that without making it worse: the sweep in
        /// GAM11-h9a-wrist-recalibration.csv shows delivered stiffness
        /// saturating near 19 Nm/rad across a hundredfold change in the
        /// authored value, so dropping K to satisfy the ratio buys an honest
        /// label and a ten degree droop instead of a one degree one.
        ///
        /// This asserts what it actually does, so a change in that behaviour
        /// is still caught while the value awaits an owner decision.
        /// </summary>
        [Test]
        public void C2_DISTAL_ACTUATOR_RECORDED_NOT_EXEMPTED()
        {
            Measurement measurement = MeasureStatic("left_hand");
            JointFamilyProfile profile = ResolveProfile("left_hand");

            // 24.9 Nm/rad, measured in the production configuration. The
            // sweep's 18.9 was taken with each candidate paired to its own
            // critically damped D; production still runs the authored damper
            // of 30, which is a damping ratio near 57 against this inertia and
            // settles the joint differently.
            Assert.That(measurement.EffectiveStiffness, Is.EqualTo(24.9f).Within(2f),
                "The wrist's delivered stiffness moved. It is not on its authored value and " +
                "the contract tracks what it actually does, so this is still a real change.");
            Assert.That(measurement.EffectiveStiffness / profile.Spring, Is.LessThan(0.2f),
                "The wrist now realises its authored spring. If that is genuine the distal " +
                "classification can be retired and it should join the load-bearing contract.");
        }

        [Test]
        public void C3_ACTUATOR_AXIS_SIGN_AND_DRIVE_MODE()
        {
            foreach (Family family in Families)
            {
                bool hinge = FindJoint(family.ChildId).Kind == PhysicalJointKind.Hinge;
                Fixture fixture = Build(family.ChildId, useGravity: false, leverM: 0f);

                Assert.That(fixture.Joint.rotationDriveMode, Is.EqualTo(RotationDriveMode.XYAndZ),
                    $"{family.ChildId} must drive per-axis; slerp realises a quarter of authored.");
                Assert.That(fixture.Joint.angularXDrive.positionSpring,
                    Is.EqualTo(fixture.Profile.Spring));
                Assert.That(fixture.Joint.angularYZDrive.positionSpring,
                    Is.EqualTo(hinge ? 0f : fixture.Profile.Spring),
                    $"{family.ChildId}: a ball joint must power its swing drive or it loses two DOFs.");
                Assert.That(fixture.Joint.slerpDrive.positionSpring, Is.EqualTo(0f));
                Assert.That(fixture.Child.solverIterations,
                    Is.EqualTo(PhysicalAthleteSolverProfile.PositionIterations));

                // Positive command, positive response, on the intended axis.
                fixture.SetSagittalTarget(5f * Mathf.Deg2Rad);
                for (int step = 0; step < 200; step++)
                    Physics.Simulate(Dt);
                float response = SagittalRadians(
                    Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation) * Mathf.Rad2Deg;
                Assert.That(response, Is.EqualTo(5f).Within(0.5f),
                    $"{family.ChildId} did not track a 5 deg sagittal command.");

                Teardown(fixture);
            }
        }

        // ---------------------------------------------------------------
        // Fixture, matched to production.
        // ---------------------------------------------------------------
        private readonly struct Measurement
        {
            public Measurement(float effectiveStiffness, float inertia, float settledDeg)
            {
                EffectiveStiffness = effectiveStiffness;
                Inertia = inertia;
                SettledDeg = settledDeg;
            }

            public float EffectiveStiffness { get; }
            public float Inertia { get; }
            public float SettledDeg { get; }
        }

        private Measurement MeasureStatic(string childId)
        {
            PhysicalSegmentRecipe segment = FindSegment(childId);
            Vector3 size = segment.DimensionsMeters;
            float lever = 0.5f * Mathf.Max(size.x, Mathf.Max(size.y, size.z));

            Fixture fixture = Build(childId, useGravity: true, leverM: lever);
            fixture.SetSagittalTarget(0f);
            for (int step = 0; step < 600; step++)
                Physics.Simulate(Dt);

            float settledRad = SagittalRadians(
                Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation);
            float gravityMoment = fixture.Child.mass * 9.81f * lever * Mathf.Cos(settledRad);
            float effective = Mathf.Abs(settledRad) > 1e-5f
                ? gravityMoment / Mathf.Abs(settledRad)
                : float.NaN;
            float inertia = fixture.Child.inertiaTensor.x;
            Teardown(fixture);
            return new Measurement(effective, inertia, settledRad * Mathf.Rad2Deg);
        }

        private sealed class Fixture
        {
            public Rigidbody Parent;
            public Rigidbody Child;
            public ConfigurableJoint Joint;
            public JointFamilyProfile Profile;

            public void SetSagittalTarget(float radians)
            {
                Quaternion logical = Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.right);
                Joint.targetRotation = PoweredJointController.ToUnityTargetRotation(logical);
                Joint.targetAngularVelocity = Vector3.zero;
            }
        }

        private Fixture Build(string childId, bool useGravity, float leverM)
        {
            PhysicalSegmentRecipe segment = FindSegment(childId);
            PhysicalJointRecipe recipe = FindJoint(childId);
            JointFamilyProfile profile = ResolveProfile(childId);
            bool hinge = recipe.Kind == PhysicalJointKind.Hinge;

            var parentObject = new GameObject("ContractParent");
            var childObject = new GameObject("ContractChild");
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
            child.centerOfMass = new Vector3(0f, 0f, leverM);
            child.sleepThreshold = 0f;
            child.angularDamping = 0f;
            child.linearDamping = 0f;
            PhysicalAthleteSolverProfile.Apply(child);

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
            joint.angularYMotion = hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.angularZMotion = hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = recipe.LowDegrees };
            joint.highAngularXLimit = new SoftJointLimit { limit = recipe.HighDegrees };
            joint.angularYLimit = new SoftJointLimit { limit = Mathf.Max(1f, recipe.SecondaryLimitDegrees) };
            joint.angularZLimit = new SoftJointLimit { limit = Mathf.Max(1f, recipe.SecondaryLimitDegrees) };
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            joint.configuredInWorldSpace = false;
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;

            var drive = new JointDrive
            {
                positionSpring = profile.Spring,
                positionDamper = profile.Damper,
                maximumForce = ProductionMaximumForce(childId),
                useAcceleration = false
            };
            joint.angularXDrive = drive;
            joint.angularYZDrive = hinge ? default : drive;
            joint.slerpDrive = default;

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
