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
            public float SpringOverride;
            public float DamperOverride;

            public static Spec Production(string childId) => new Spec
            {
                ChildId = childId,
                DriveMode = RotationDriveMode.XYAndZ,
                SecondaryMotion = ProductionSecondaryMotion(childId),
                Dt = 0.01f,
                SolverIterations = 6,
                SolverVelocityIterations = 1,
                MaximumForce = ProductionMaximumForce(childId),
                UseAcceleration = false,
                InertiaScale = 1f,
                SecondaryLimitDeg = FindJoint(childId).SecondaryLimitDegrees,
                SpringOverride = -1f,
                DamperOverride = -1f
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
                Assert.That(fixture.Joint.rotationDriveMode, Is.EqualTo(RotationDriveMode.XYAndZ),
                    $"{childId} must drive through the per-axis drives. Slerp realises a quarter " +
                    "of the authored spring.");
                Assert.That(fixture.Joint.angularYMotion,
                    Is.EqualTo(hinge ? ConfigurableJointMotion.Locked : ConfigurableJointMotion.Limited),
                    $"{childId} secondary angular motion must match production.");

                JointDrive driven = fixture.Joint.angularXDrive;
                JointDrive swing = fixture.Joint.angularYZDrive;
                JointDrive idle = fixture.Joint.slerpDrive;
                Assert.That(swing.positionSpring, Is.EqualTo(hinge ? 0f : fixture.Profile.Spring),
                    $"{childId}: a hinge locks its swing axes, a ball joint has to power them or " +
                    "the conversion deletes two anatomical degrees of freedom.");
                Assert.That(driven.positionSpring, Is.EqualTo(fixture.Profile.Spring),
                    $"{childId} must carry the family spring on the drive production actually uses.");
                Assert.That(driven.positionDamper, Is.EqualTo(fixture.Profile.Damper));
                Assert.That(driven.useAcceleration, Is.False);
                Assert.That(idle.positionSpring, Is.EqualTo(0f),
                    $"{childId} must leave slerpDrive at zero, as production does.");
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
        // Multi-axis anatomical parity.
        //
        // Slerp drives the whole orientation error as one shortest arc.
        // XYAndZ splits it into a twist drive on X and a swing drive on Y and
        // Z. For the ball families that is the change being proposed, so each
        // one has to show that a pure command about each intended axis still
        // moves that axis, in the right direction, without dragging the
        // others along. Consistency is not a reason to convert a family that
        // fails this.
        // ---------------------------------------------------------------
        [Test]
        public void H9_MULTI_AXIS_DRIVE_SEMANTICS_PARITY()
        {
            var report = new StringBuilder();
            report.AppendLine("child,family,drive_mode,commanded_axis,commanded_deg," +
                              "response_x_deg,response_y_deg,response_z_deg,intended_axis_deg," +
                              "worst_cross_axis_deg,cross_axis_fraction,sign_correct");

            string[] ballChildren = { "abdomen", "thorax", "left_thigh", "left_upper_arm", "left_hand" };
            foreach (string childId in ballChildren)
            {
                foreach (RotationDriveMode mode in new[] { RotationDriveMode.Slerp, RotationDriveMode.XYAndZ })
                {
                    foreach (int axis in new[] { 0, 1, 2 })
                    {
                        foreach (float commandDeg in new[] { -5f, 5f })
                        {
                            Spec spec = Spec.Production(childId);
                            spec.DriveMode = mode;
                            spec.SecondaryMotion = ConfigurableJointMotion.Limited;
                            spec.SolverIterations = 32;

                            Fixture fixture = Build(spec, useGravity: false, leverM: 0f);
                            fixture.SetLogicalTarget(axis, commandDeg * Mathf.Deg2Rad);

                            Time.fixedDeltaTime = spec.Dt;
                            for (int step = 0; step < 300; step++)
                                Physics.Simulate(spec.Dt);

                            Vector3 response = LogicalDegrees(
                                Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation);
                            float intended = response[axis];
                            float worstCross = 0f;
                            for (int other = 0; other < 3; other++)
                                if (other != axis)
                                    worstCross = Mathf.Max(worstCross, Mathf.Abs(response[other]));

                            report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "{0},{1},{2},{3},{4:F1},{5:F3},{6:F3},{7:F3},{8:F3},{9:F3},{10:F4},{11}",
                                childId, FindJoint(childId).Family, mode, "XYZ"[axis], commandDeg,
                                response.x, response.y, response.z, intended, worstCross,
                                Mathf.Abs(intended) > 1e-4f ? worstCross / Mathf.Abs(intended) : float.NaN,
                                Mathf.Sign(intended) == Mathf.Sign(commandDeg)));

                            Teardown(fixture);
                        }
                    }
                }
            }

            WriteMeasurement("GAM11-h9-multiaxis-parity.csv", report.ToString());
            Debug.Log("[H9 MULTI AXIS PARITY]" + Environment.NewLine + report);
        }

        private static Vector3 LogicalDegrees(Quaternion rotation)
        {
            if (rotation.w < 0f)
                rotation = new Quaternion(-rotation.x, -rotation.y, -rotation.z, -rotation.w);
            float magnitude = Mathf.Sqrt(
                rotation.x * rotation.x + rotation.y * rotation.y + rotation.z * rotation.z);
            if (magnitude <= 1e-6f)
                return Vector3.zero;
            float angle = 2f * Mathf.Atan2(magnitude, Mathf.Clamp(rotation.w, -1f, 1f));
            float scale = angle / magnitude * Mathf.Rad2Deg;
            return new Vector3(rotation.x * scale, rotation.y * scale, rotation.z * scale);
        }

        // ---------------------------------------------------------------
        // Minimum converged solver profile on the final drive semantics.
        //
        // Every powered family, three fresh fixtures each, sweeping position
        // iterations until all of them realize their authored spring within
        // five percent. The answer is the smallest count that clears every
        // family, not the largest count available.
        // ---------------------------------------------------------------
        [Test]
        public void H9_MINIMUM_SOLVER_PROFILE()
        {
            string[] families =
            {
                "left_foot", "left_shank", "left_thigh", "abdomen", "thorax",
                "left_upper_arm", "left_forearm", "left_hand"
            };

            var report = new StringBuilder();
            report.AppendLine("iterations,child,family,authored_k,k_mean,k_min,k_max,k_sd," +
                              "ratio_mean,within_5pct");
            var verdict = new StringBuilder();
            verdict.AppendLine("iterations,families_within_5pct,worst_family,worst_ratio");

            foreach (int iterations in new[] { 16, 20, 24, 28, 32 })
            {
                int passing = 0;
                string worstFamily = "NONE";
                float worstDeviation = 0f;
                float worstRatio = float.NaN;

                foreach (string childId in families)
                {
                    var ratios = new float[3];
                    var stiffness = new float[3];
                    for (int run = 0; run < 3; run++)
                    {
                        Spec spec = Spec.Production(childId);
                        spec.SolverIterations = iterations;
                        Result result = MeasureStatic(spec);
                        stiffness[run] = result.EffectiveStiffness;
                        ratios[run] = result.Ratio;
                    }

                    float mean = (ratios[0] + ratios[1] + ratios[2]) / 3f;
                    float kMean = (stiffness[0] + stiffness[1] + stiffness[2]) / 3f;
                    float kMin = Mathf.Min(stiffness[0], Mathf.Min(stiffness[1], stiffness[2]));
                    float kMax = Mathf.Max(stiffness[0], Mathf.Max(stiffness[1], stiffness[2]));
                    float sd = Mathf.Sqrt((
                        (stiffness[0] - kMean) * (stiffness[0] - kMean) +
                        (stiffness[1] - kMean) * (stiffness[1] - kMean) +
                        (stiffness[2] - kMean) * (stiffness[2] - kMean)) / 3f);
                    bool within = mean >= 0.95f && mean <= 1.05f;
                    if (within)
                        passing++;
                    if (Mathf.Abs(mean - 1f) > worstDeviation)
                    {
                        worstDeviation = Mathf.Abs(mean - 1f);
                        worstFamily = childId;
                        worstRatio = mean;
                    }

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1},{2},{3:F0},{4:F2},{5:F2},{6:F2},{7:F4},{8:F4},{9}",
                        iterations, childId, FindJoint(childId).Family,
                        ResolveProfile(childId).Spring, kMean, kMin, kMax, sd, mean, within));
                }

                verdict.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0},{1}/{2},{3},{4:F4}", iterations, passing, families.Length,
                    worstFamily, worstRatio));
            }

            var combined = new StringBuilder();
            combined.Append(report);
            combined.AppendLine("# verdict");
            combined.Append(verdict);
            WriteMeasurement("GAM11-h9-solver-profile-selection.csv", combined.ToString());
            Debug.Log("[H9 MINIMUM SOLVER PROFILE]" + Environment.NewLine + combined);
        }

        // ---------------------------------------------------------------
        // Is the wrist result a plant defect or a measurement floor?
        //
        // The hand is 0.6 kg on a 0.07 m lever against a 250 Nm/rad spring,
        // so its true equilibrium deflection is about 0.094 deg. If the
        // measurement cannot resolve an angle that small the ratio will read
        // low no matter how well the drive behaves. Loading the same fixture
        // harder moves the expected angle up without touching the actuator:
        // if the ratio climbs toward one as the angle grows, the earlier
        // number was the method, not the wrist.
        // ---------------------------------------------------------------
        [Test]
        public void H9_WRIST_MEASUREMENT_FLOOR()
        {
            var report = new StringBuilder();
            report.AppendLine("child,lever_m,expected_deg,settled_deg,gravity_moment_nm,k_eff,ratio");

            foreach (string childId in new[] { "left_hand", "abdomen" })
            {
                JointFamilyProfile profile = ResolveProfile(childId);
                foreach (float lever in new[] { 0.07f, 0.25f, 0.5f, 1.0f, 2.0f })
                {
                    Spec spec = Spec.Production(childId);
                    spec.SolverIterations = 28;
                    Result result = MeasureStaticWithLever(spec, lever);
                    float mass = FindSegment(childId).MassFraction * 100f;
                    float expectedDeg = mass * 9.81f * lever / profile.Spring * Mathf.Rad2Deg;

                    report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                        "{0},{1:F3},{2:F4},{3:F4},{4:F4},{5:F2},{6:F4}",
                        childId, lever, expectedDeg, result.SettledDeg,
                        result.GravityMoment, result.EffectiveStiffness, result.Ratio));
                }
            }

            WriteMeasurement("GAM11-h9-wrist-measurement-floor.csv", report.ToString());
            Debug.Log("[H9 WRIST MEASUREMENT FLOOR]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // Wrist recalibration sweep.
        //
        // The hand is 0.6 kg with a sagittal inertia near 0.00028 kg m^2. At
        // the authored 250 Nm/rad that puts K dt^2 / I near 89 and the natural
        // frequency times the timestep near 9.4, and the drive measures about
        // a tenth of what it is authored. The authored damper of 30 is a
        // damping ratio near 57 against the same inertia, so both terms are
        // out of scale for this body rather than just the spring.
        //
        // Each candidate is paired with its own critically damped D, since
        // holding D at 30 while lowering K would only make the mismatch worse.
        // The choice comes from the measured realization and step response,
        // not from any threshold on the conditioning numbers.
        // ---------------------------------------------------------------
        [Test]
        public void H9A_WRIST_RECALIBRATION_SWEEP()
        {
            var report = new StringBuilder();
            report.AppendLine("k_authored,d_paired,lambda_k,omega_n_dt,k_eff,k_ratio," +
                              "step_settled_deg,step_target_deg,tracking_error_deg,overshoot_deg,final_omega");

            const float inertia = 0.00028f;
            const float dt = 0.01f;

            foreach (float spring in new[] { 250f, 100f, 50f, 25f, 10f, 5f, 2.5f })
            {
                float damper = 2f * Mathf.Sqrt(spring * inertia);

                Spec staticSpec = Spec.Production("left_hand");
                staticSpec.SpringOverride = spring;
                staticSpec.DamperOverride = damper;
                Result staticResult = MeasureStatic(staticSpec);

                // Step response: command 5 deg and see where it actually ends
                // up, and whether it gets there without ringing.
                Spec stepSpec = staticSpec;
                Fixture fixture = Build(stepSpec, useGravity: false, leverM: 0f);
                fixture.SetLogicalSagittalTarget(5f * Mathf.Deg2Rad);
                Time.fixedDeltaTime = dt;
                float peak = 0f;
                for (int step = 0; step < 300; step++)
                {
                    Physics.Simulate(dt);
                    float current = SagittalRadians(
                        Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation) * Mathf.Rad2Deg;
                    peak = Mathf.Max(peak, current);
                }
                float settled = SagittalRadians(
                    Quaternion.Inverse(fixture.Parent.rotation) * fixture.Child.rotation) * Mathf.Rad2Deg;
                float finalOmega = fixture.Child.angularVelocity.x;
                Teardown(fixture);

                report.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0:F2},{1:F4},{2:F2},{3:F3},{4:F3},{5:F4},{6:F3},{7:F1},{8:F3},{9:F3},{10:F5}",
                    spring, damper, spring * dt * dt / inertia,
                    Mathf.Sqrt(spring / inertia) * dt,
                    staticResult.EffectiveStiffness, staticResult.EffectiveStiffness / spring,
                    settled, 5f, settled - 5f, Mathf.Max(0f, peak - 5f), finalOmega));
            }

            WriteMeasurement("GAM11-h9a-wrist-recalibration.csv", report.ToString());
            Debug.Log("[H9A WRIST RECALIBRATION]" + Environment.NewLine + report);
        }

        // ---------------------------------------------------------------
        // Measurement. Settle, then sample only once the body is stationary.
        // ---------------------------------------------------------------
        private Result MeasureStatic(Spec spec)
        {
            PhysicalSegmentRecipe segment = FindSegment(spec.ChildId);
            Vector3 size = segment.DimensionsMeters;
            return MeasureStaticWithLever(spec, 0.5f * Mathf.Max(size.x, Mathf.Max(size.y, size.z)));
        }

        private Result MeasureStaticWithLever(Spec spec, float lever)
        {

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

            public void SetLogicalTarget(int axis, float radians)
            {
                Vector3 direction = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                Quaternion logical = Quaternion.AngleAxis(radians * Mathf.Rad2Deg, direction);
                Joint.targetRotation = PoweredJointController.ToUnityTargetRotation(logical);
                Joint.targetAngularVelocity = Vector3.zero;
            }

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

            float spring = spec.SpringOverride >= 0f ? spec.SpringOverride : profile.Spring;
            float damper = spec.DamperOverride >= 0f ? spec.DamperOverride : profile.Damper;
            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
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
                // A hinge locks Y and Z, so only the twist drive can act. A
                // ball joint has to carry the family spring on the swing drive
                // as well, or converting it to XYAndZ silently deletes two
                // anatomical degrees of freedom: with angularYZDrive left at
                // zero the secondary axes measured exactly 0.000 response.
                bool hinge = FindJoint(spec.ChildId).Kind == PhysicalJointKind.Hinge;
                joint.angularXDrive = drive;
                joint.angularYZDrive = hinge ? ZeroDrive() : drive;
                joint.slerpDrive = ZeroDrive();
            }

            return new Fixture
            {
                Parent = parent,
                Child = child,
                Joint = joint,
                Profile = new JointFamilyProfile(
                    profile.Id, spring, damper, profile.BaseCapacityNm, profile.MaxTargetRateRadS)
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
