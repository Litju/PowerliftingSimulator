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
    /// Characterizes a finite drive for head_neck, which GAM-7 left passive.
    ///
    /// That was defensible while the athlete was a collapse fixture and is not
    /// now: the rendered ten-second standing evidence shows the head neutral at
    /// spawn and thrown into hard extension by the end, on a body whose trunk,
    /// pelvis, knees and feet are all stable. A joint with no drive has nothing
    /// to hold it.
    ///
    /// The head is a different physical regime from the hand. At 8.1 kg with a
    /// sagittal inertia near 0.095 kg m^2, the natural frequency times the
    /// timestep stays below 0.8 even at 600 Nm/rad, where the 0.6 kg hand is
    /// already at 9.4 by 250. The neck can carry real stiffness; the wrist
    /// cannot, and that is why they need different numbers rather than a
    /// shared distal default.
    ///
    /// GAME_PHYSICS_NECK_POSTURE_CALIBRATION. It is a plausible head, not a
    /// claim about cervical torque.
    /// </summary>
    public sealed class NeckPostureActuatorTests
    {
        private const string MeasurementDirectory = "Artifacts/Measurements/GAM-11";
        private const float Dt = 0.01f;
        private const string NeckChildId = "head_neck";

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
        // N1. Bounded sweep. Each candidate spring is paired with its own
        // critically damped damper, because holding a trunk-sized damper
        // against head-sized inertia is what makes a joint sluggish rather
        // than stable.
        // ---------------------------------------------------------------
        [Test]
        public void N1_NECK_PROFILE_CHARACTERIZATION()
        {
            var report = new StringBuilder();
            report.AppendLine("k,d,lambda_k,omega_n_dt,zeta,static_sag_deg,k_eff," +
                              "step_settled_deg,step_target_deg,tracking_error_deg,overshoot_deg," +
                              "final_omega,limit_proximity");

            PhysicalSegmentRecipe segment = FindSegment(NeckChildId);
            float mass = segment.MassFraction * 100f;
            float inertia = PhysicalAthleteDefinition.BoxInertia(mass, segment.DimensionsMeters).x;
            float lever = 0.5f * Mathf.Max(segment.DimensionsMeters.x,
                Mathf.Max(segment.DimensionsMeters.y, segment.DimensionsMeters.z));

            foreach (float spring in new[] { 50f, 100f, 200f, 300f, 400f, 600f })
            {
                float damper = 2f * Mathf.Sqrt(spring * inertia);

                Fixture sagFixture = Build(spring, damper, useGravity: true, leverM: lever);
                sagFixture.SetSagittalTarget(0f);
                for (int step = 0; step < 600; step++)
                    Physics.Simulate(Dt);
                float sagRad = SagittalRadians(
                    Quaternion.Inverse(sagFixture.Parent.rotation) * sagFixture.Child.rotation);
                float gravityMoment = mass * 9.81f * lever * Mathf.Cos(sagRad);
                float effective = Mathf.Abs(sagRad) > 1e-5f ? gravityMoment / Mathf.Abs(sagRad) : float.NaN;
                float limitProximity = sagFixture.Joint == null ? 0f : LimitProximity(sagRad * Mathf.Rad2Deg);
                Teardown(sagFixture);

                // Perturbation: 3 deg sagittal, gravity on, and see whether it
                // arrives without ringing.
                Fixture stepFixture = Build(spring, damper, useGravity: true, leverM: lever);
                stepFixture.SetSagittalTarget(3f * Mathf.Deg2Rad);
                float peak = float.NegativeInfinity;
                for (int step = 0; step < 400; step++)
                {
                    Physics.Simulate(Dt);
                    peak = Mathf.Max(peak, SagittalRadians(
                        Quaternion.Inverse(stepFixture.Parent.rotation) * stepFixture.Child.rotation)
                        * Mathf.Rad2Deg);
                }
                float settledDeg = SagittalRadians(
                    Quaternion.Inverse(stepFixture.Parent.rotation) * stepFixture.Child.rotation)
                    * Mathf.Rad2Deg;
                float finalOmega = stepFixture.Child.angularVelocity.x;
                Teardown(stepFixture);

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F0},{1:F2},{2:F4},{3:F3},{4:F2},{5:F3},{6:F1},{7:F3},{8:F1},{9:F3},{10:F3},{11:F5},{12:F3}",
                    spring, damper, spring * Dt * Dt / inertia,
                    Mathf.Sqrt(spring / inertia) * Dt,
                    damper / (2f * Mathf.Sqrt(spring * inertia)),
                    sagRad * Mathf.Rad2Deg, effective,
                    settledDeg, 3f, settledDeg - 3f, Mathf.Max(0f, peak - 3f),
                    finalOmega, limitProximity));
            }

            WriteMeasurement("GAM11-h9b-neck-characterization.csv", report.ToString());
            Debug.Log("[N1 NECK CHARACTERIZATION]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // N2. The chosen profile, as a permanent behavioural contract.
        // Distal pose actuators are held to what they do, not to a stiffness
        // ratio, but they are still held to something.
        // ---------------------------------------------------------------
        [Test]
        public void N2_NECK_DISTAL_POSE_RESPONSE_CONTRACT()
        {
            JointFamilyProfile? profile = PoweredJointController.FindFamilyProfile("neck");
            Assert.That(profile.HasValue, Is.True,
                "head_neck has no family profile, so it has no drive and nothing holds the head.");

            PhysicalSegmentRecipe segment = FindSegment(NeckChildId);
            float mass = segment.MassFraction * 100f;
            float lever = 0.5f * Mathf.Max(segment.DimensionsMeters.x,
                Mathf.Max(segment.DimensionsMeters.y, segment.DimensionsMeters.z));

            Fixture fixture = Build(profile.Value.Spring, profile.Value.Damper,
                useGravity: true, leverM: lever);
            fixture.SetSagittalTarget(0f);
            for (int step = 0; step < 600; step++)
                Physics.Simulate(Dt);

            float sagDeg = SagittalRadians(
                Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation) * Mathf.Rad2Deg;
            float omega = fixture.Child.angularVelocity.x;

            Assert.That(fixture.Joint.rotationDriveMode, Is.EqualTo(RotationDriveMode.XYAndZ),
                "The neck must use the qualified per-axis semantics, not slerp.");
            Assert.That(fixture.Joint.angularYZDrive.positionSpring, Is.EqualTo(profile.Value.Spring),
                "The neck is multi-axis, so its swing drive has to be powered too.");
            Assert.That(float.IsFinite(fixture.Joint.angularXDrive.maximumForce), Is.True);

            Assert.That(Mathf.Abs(sagDeg), Is.LessThan(5f),
                $"The head sags {sagDeg:F2} deg under its own weight on a worst-case horizontal lever.");
            Assert.That(LimitProximity(sagDeg), Is.LessThan(0.5f),
                "The head is resting toward its anatomical limit rather than being held by its drive.");
            Assert.That(Mathf.Abs(omega), Is.LessThan(0.05f),
                $"The neck has not settled; final angular velocity {omega:F4} rad/s.");

            Teardown(fixture);
        }

        // ---------------------------------------------------------------
        // Fixture, production-parity for the neck.
        // ---------------------------------------------------------------
        private sealed class Fixture
        {
            public Rigidbody Parent;
            public Rigidbody Child;
            public ConfigurableJoint Joint;

            public void SetSagittalTarget(float radians)
            {
                Quaternion logical = Quaternion.AngleAxis(radians * Mathf.Rad2Deg, Vector3.right);
                Joint.targetRotation = PoweredJointController.ToUnityTargetRotation(logical);
                Joint.targetAngularVelocity = Vector3.zero;
            }
        }

        private Fixture Build(float spring, float damper, bool useGravity, float leverM)
        {
            PhysicalSegmentRecipe segment = FindSegment(NeckChildId);
            PhysicalJointRecipe recipe = FindJoint(NeckChildId);

            var parentObject = new GameObject("NeckFixtureParent");
            var childObject = new GameObject("NeckFixtureChild");
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
            // The neck keeps every anatomical axis it was authored with. The
            // fix for a drifting head is finite actuation, not a narrower joint.
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = recipe.LowDegrees };
            joint.highAngularXLimit = new SoftJointLimit { limit = recipe.HighDegrees };
            joint.angularYLimit = new SoftJointLimit { limit = recipe.SecondaryLimitDegrees };
            joint.angularZLimit = new SoftJointLimit { limit = recipe.SecondaryLimitDegrees };
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            joint.configuredInWorldSpace = false;
            joint.rotationDriveMode = RotationDriveMode.XYAndZ;

            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = NeckMaximumForce(),
                useAcceleration = false
            };
            joint.angularXDrive = drive;
            joint.angularYZDrive = drive;
            joint.slerpDrive = default;

            return new Fixture { Parent = parent, Child = child, Joint = joint };
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

        /// <summary>Production capacity path: base times load scale times family scale.</summary>
        private static float NeckMaximumForce()
        {
            JointFamilyProfile? profile = PoweredJointController.FindFamilyProfile("neck");
            float baseCapacity = profile.HasValue ? profile.Value.BaseCapacityNm : 40f;
            return baseCapacity * 3.8f * 1.5f;
        }

        private static float LimitProximity(float degrees)
        {
            PhysicalJointRecipe recipe = FindJoint(NeckChildId);
            float limit = degrees >= 0f
                ? Mathf.Max(0.001f, recipe.HighDegrees)
                : Mathf.Max(0.001f, -recipe.LowDegrees);
            return Mathf.Clamp01(Mathf.Abs(degrees) / limit);
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
