using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using PowerliftingSimulator.Equipment;
using PowerliftingSimulator.Squat.Unity;
using UnityEngine;
using UnityEngine.TestTools;
using static PowerliftingSimulator.PhysicsBenchmarks.PhysicsBenchmarkSettings;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// Builds a barbell Rigidbody with the production compound mass model and
    /// attaches it with a ConfigurableJoint configured from the public
    /// SquatBarSaddle constants. The athlete-level benchmark compares this
    /// replica field by field against the live saddle so the two cannot drift.
    /// </summary>
    public static class SaddleReplica
    {
        public static Rigidbody CreateBar(IsolatedPhysicsWorld world, float loadKg, Vector3 position)
        {
            BarbellLoadPlan plan = BarbellLoadingSolver.Solve(loadKg);
            BarbellInertiaModel inertia = BarbellPrototypeConfiguration.ComputeInertia(plan);
            Rigidbody bar = world.CreateBody("bar", position, Quaternion.identity, plan.TotalMassKg, inertia.InertiaTensorKgM2);
            bar.centerOfMass = inertia.CenterOfMassBarMeters;
            // Production PhysicalBarbell body settings.
            bar.linearDamping = 0.035f;
            bar.angularDamping = 0.055f;
            bar.solverIterations = 12;
            bar.solverVelocityIterations = 6;
            bar.maxAngularVelocity = 50f;
            return bar;
        }

        public static ConfigurableJoint Attach(Rigidbody bar, Rigidbody thorax, Vector3 thoraxLocalAnchor)
        {
            ConfigurableJoint joint = bar.gameObject.AddComponent<ConfigurableJoint>();
            joint.connectedBody = thorax;
            joint.autoConfigureConnectedAnchor = false;
            joint.anchor = SquatBarSaddle.BarLocalAnchor;
            joint.connectedAnchor = thoraxLocalAnchor;
            joint.axis = Vector3.right;
            joint.secondaryAxis = Vector3.up;
            joint.xMotion = ConfigurableJointMotion.Limited;
            joint.yMotion = ConfigurableJointMotion.Limited;
            joint.zMotion = ConfigurableJointMotion.Limited;
            joint.linearLimit = new SoftJointLimit { limit = SquatBarSaddle.DefaultLinearLimitM };
            JointDrive linear = IsolatedPhysicsWorld.Drive(SquatBarSaddle.DefaultLinearSpring, SquatBarSaddle.DefaultLinearDamper,
                SquatBarSaddle.DefaultLinearMaxForce);
            joint.xDrive = linear;
            joint.yDrive = linear;
            joint.zDrive = linear;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit = new SoftJointLimit { limit = -20f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 20f };
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularYLimit = new SoftJointLimit { limit = 15f };
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.angularZLimit = new SoftJointLimit { limit = 15f };
            JointDrive angular = IsolatedPhysicsWorld.Drive(SquatBarSaddle.DefaultAngularSpring, SquatBarSaddle.DefaultAngularDamper,
                SquatBarSaddle.DefaultAngularMaxForce);
            joint.angularXDrive = angular;
            joint.angularYZDrive = angular;
            joint.breakForce = SquatBarSaddle.DefaultBreakForce;
            joint.breakTorque = SquatBarSaddle.DefaultBreakTorque;
            joint.projectionMode = JointProjectionMode.None;
            joint.enableCollision = false;
            joint.enablePreprocessing = true;
            return joint;
        }
    }

    /// <summary>
    /// GAM-50 benchmarks 10-11: the bar saddle as a spring/damper load path,
    /// and a 3-link driven chain carrying the bar, the smallest system with
    /// the athlete's mass ratios.
    /// </summary>
    [Category("PhysicsBenchmark")]
    [Category("PhysicsBenchmarkIsolated")]
    public sealed class ConstraintPhysicsBenchmarks
    {
        public static readonly float[] Loads = { 25f, 60f, 140f, 170f, 300f };
        private const double LockoutBarStillVelocityMps = 0.020;

        /// <summary>
        /// Observation window for chain statics. Heavy loads sit close to the
        /// static stability limit, where the slowest mode is soft and slow; a
        /// 4 s window averaged a transient in both ConfigurableJoint and
        /// ArticulationBody alike.
        /// </summary>
        public const float SettleSeconds = 12f;

        [UnityTest]
        public IEnumerator B10_SquatBarSaddle_SpringDamperLoadPath()
        {
            var rec = new PhysicsBenchmarkRecorder("B10_saddle_load_path");
            double g = -Physics.gravity.y;
            double k = SquatBarSaddle.DefaultLinearSpring;
            double c = SquatBarSaddle.DefaultLinearDamper;
            foreach (float load in Loads)
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("saddle"))
                {
                    Rigidbody thorax = world.CreateBody("thorax_carrier", new Vector3(0f, 1.4f, 0f), Quaternion.identity, 21.6f,
                        Vector3.one * 0.3f, kinematic: true, gravity: false);
                    Vector3 anchorWorld = thorax.transform.TransformPoint(SquatBarSaddle.ThoraxLocalAnchor);
                    Rigidbody bar = SaddleReplica.CreateBar(world, load, anchorWorld);
                    ConfigurableJoint joint = SaddleReplica.Attach(bar, thorax, SquatBarSaddle.ThoraxLocalAnchor);
                    double m = bar.mass;
                    double wn = Math.Sqrt(k / m);
                    double zeta = c / (2 * Math.Sqrt(k * m));
                    int steps = Mathf.RoundToInt(2f / world.Dt);
                    double stillAt = double.NaN;
                    double maxSag = 0;
                    for (int i = 0; i < steps; i++)
                    {
                        world.Step();
                        double sag = anchorWorld.y - bar.position.y;
                        maxSag = Math.Max(maxSag, sag);
                        double v = Math.Abs(bar.linearVelocity.y);
                        if (v > LockoutBarStillVelocityMps)
                            stillAt = double.NaN;
                        else if (double.IsNaN(stillAt))
                            stillAt = world.Time;
                        rec.Series("load" + load, "t,sag_m,vy_mps,force_y_n",
                            Inv(world.Time) + "," + Inv(sag) + "," + Inv(bar.linearVelocity.y) + "," + Inv(joint.currentForce.y));
                    }
                    double staticSag = anchorWorld.y - bar.position.y;
                    string cfg = Cfg($"load={load};m={m:F2};wn_dt={wn * world.Dt:F3};zeta={zeta:F3}", world);
                    rec.Record("static_sag_m", cfg, m * g / k, staticSag, 0.05, ToleranceKind.Relative,
                        "spring statics sag = m g / k; " + "sealed realization band 5%",
                        "Saddle linear drive does not realise its authored spring.", CausalLayer.BarSaddleLoadPath);
                    rec.Info("reported_joint_force_kinematic_carrier_n", cfg, joint.currentForce.magnitude,
                        "Joint.currentForce with a kinematic connected body (not the production topology).",
                        CausalLayer.BarSaddleLoadPath);
                    rec.Record("sag_limit_occupancy", cfg, 0, maxSag / SquatBarSaddle.DefaultLinearLimitM, 0.95, ToleranceKind.UpperBound,
                        "saddle qualification: limit occupancy < 0.95 (GAM13 mechanics MaximumSaddleLimitOccupancy)",
                        "Dynamic release reaches the hard linear limit.", CausalLayer.BarSaddleLoadPath);
                    rec.Record("time_to_bar_stillness_s", cfg, 0, double.IsNaN(stillAt) ? double.PositiveInfinity : stillAt, 0.5,
                        ToleranceKind.UpperBound,
                        "bar relative speed below the sealed GAM-12 lockout stillness 0.020 m/s within 0.5 s of release",
                        "The saddle alone keeps the bar moving long enough to defeat lockout stillness.",
                        CausalLayer.BarSaddleLoadPath);
                }
                yield return null;

                // Production topology: the thorax is a dynamic body. Hold a
                // dynamic 21.6 kg carrier on a locked joint to a kinematic
                // anchor and read the engine-reported load path.
                using (IsolatedPhysicsWorld world = ProductionWorld("saddle_dynamic_carrier"))
                {
                    const float thoraxMass = 21.6f;
                    Rigidbody anchor = world.CreateBody("anchor", new Vector3(0f, 1.4f, 0f), Quaternion.identity, 1f, Vector3.one,
                        kinematic: true, gravity: false);
                    Rigidbody thorax = world.CreateBody("thorax", new Vector3(0f, 1.4f, 0f), Quaternion.identity, thoraxMass,
                        new Vector3(0.21f, 0.30f, 0.37f));
                    ConfigurableJoint hold = IsolatedPhysicsWorld.Hinge(thorax, anchor, Vector3.zero, freeTwist: false);
                    Vector3 anchorWorld = thorax.transform.TransformPoint(SquatBarSaddle.ThoraxLocalAnchor);
                    Rigidbody bar = SaddleReplica.CreateBar(world, load, anchorWorld);
                    ConfigurableJoint saddle = SaddleReplica.Attach(bar, thorax, SquatBarSaddle.ThoraxLocalAnchor);
                    world.Step(Mathf.RoundToInt(2f / world.Dt));
                    double m = bar.mass;
                    string cfg = Cfg($"load={load};dynamic_thorax={thoraxMass}", world);
                    rec.Record("saddle_load_path_telemetry_n", cfg, m * g,
                        SquatBarSaddle.ModeledLinearForceOnBar(saddle, bar, thorax).magnitude, 0.02, ToleranceKind.Relative,
                        "static load path: production saddle load telemetry equals the bar weight m g; 2%",
                        "Production saddle load telemetry is not the load the saddle carries.",
                        CausalLayer.BarSaddleLoadPath);
                    rec.Info("engine_reported_saddle_force_n", cfg, saddle.currentForce.magnitude,
                        "Raw Joint.currentForce for the drive-only saddle (characterization).", CausalLayer.BarSaddleLoadPath);
                    rec.Record("carrier_reported_force_n", cfg, (m + thoraxMass) * g, hold.currentForce.magnitude, 0.02,
                        ToleranceKind.Relative, "static load path below the saddle carries bar + thorax weight; 2%",
                        "Load is not transmitted through the dynamic thorax.", CausalLayer.BarSaddleLoadPath);
                    rec.Record("static_sag_dynamic_carrier_m", cfg, m * g / k,
                        thorax.transform.TransformPoint(SquatBarSaddle.ThoraxLocalAnchor).y - bar.position.y, 0.05, ToleranceKind.Relative,
                        "spring statics sag = m g / k on a dynamic carrier; realization band 5%",
                        "Saddle spring is under-realised when its carrier is dynamic (mass ratio).", CausalLayer.BarSaddleLoadPath);
                }

                // B10b angular release: the bar given a small rotation rate on
                // a kinematic carrier. How fast does the saddle's angular
                // spring/damper bring it to the sealed GAM-12 angular
                // stillness (0.20 rad/s), and to 0.02 rad/s?
                using (IsolatedPhysicsWorld world = ProductionWorld("saddle_angular"))
                {
                    Rigidbody thorax = world.CreateBody("thorax_carrier", new Vector3(0f, 1.4f, 0f), Quaternion.identity, 21.6f,
                        Vector3.one * 0.3f, kinematic: true, gravity: false);
                    Vector3 anchorWorld = thorax.transform.TransformPoint(SquatBarSaddle.ThoraxLocalAnchor);
                    Rigidbody bar = SaddleReplica.CreateBar(world, load, anchorWorld);
                    SaddleReplica.Attach(bar, thorax, SquatBarSaddle.ThoraxLocalAnchor);
                    world.Step(50);
                    bar.angularVelocity = new Vector3(0.3f, 0.3f, 0.3f);
                    double stillLockout = double.NaN, stillFine = double.NaN;
                    int steps = Mathf.RoundToInt(4f / world.Dt);
                    for (int i = 0; i < steps; i++)
                    {
                        world.Step();
                        double w = bar.angularVelocity.magnitude;
                        double t = (i + 1) * (double)world.Dt;
                        if (w > 0.20) stillLockout = double.NaN; else if (double.IsNaN(stillLockout)) stillLockout = t;
                        if (w > 0.02) stillFine = double.NaN; else if (double.IsNaN(stillFine)) stillFine = t;
                        rec.Series("angular_load" + load, "t,bar_w_rad_s", Inv(t) + "," + Inv(w));
                    }
                    Vector3 inertia = bar.inertiaTensor;
                    double kTheta = SquatBarSaddle.DefaultAngularSpring, cTheta = SquatBarSaddle.DefaultAngularDamper;
                    string cfg = Cfg($"load={load};w0=0.52rad/s", world);
                    rec.Record("time_to_lockout_angular_stillness_s", cfg, 0, double.IsNaN(stillLockout) ? double.PositiveInfinity : stillLockout,
                        0.5, ToleranceKind.UpperBound,
                        "bar angular speed below the sealed GAM-12 lockout 0.20 rad/s within 0.5 s of a 0.52 rad/s disturbance",
                        "The saddle lets the bar rock long enough to defeat lockout angular stillness.", CausalLayer.BarSaddleLoadPath);
                    rec.Info("time_to_0p02_rad_s_s", cfg, double.IsNaN(stillFine) ? double.PositiveInfinity : stillFine,
                        "Time until bar angular speed stays below 0.02 rad/s.", CausalLayer.BarSaddleLoadPath);
                    for (int axis = 0; axis < 3; axis++)
                        rec.Info("angular_damping_ratio_axis" + axis, cfg, cTheta / (2 * Math.Sqrt(kTheta * inertia[axis])),
                            "Analytic saddle angular damping ratio about each bar principal axis.", CausalLayer.BarSaddleLoadPath);
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }

        // ---------------------------------------------------------------
        // 3-link chain: ankle (kinematic foot) -> shank -> thigh -> torso -> bar.
        // ---------------------------------------------------------------

        public sealed class ChainSpec
        {
            public string[] Names = { "shank", "thigh", "torso" };
            public float[] Mass = { 9.3f, 20f, 67.8f };
            public float[] Length = { 0.43f, 0.43f, 0.50f };
            /// <summary>Initial segment angle from vertical, radians, positive about +X.</summary>
            public float[] Angle = { -0.55f, 1.15f, -0.65f };
            public string[] Family = { "ankle", "knee", "hip" };
            /// <summary>Drive multiplier: two legs drive the planar equivalent.</summary>
            public float DriveMultiplier = 2f;
        }

        public sealed class ChainState
        {
            public Rigidbody Foot;
            public Rigidbody[] Links;
            public ConfigurableJoint[] Joints;
            public Quaternion[] StartRotations;
            public Rigidbody Bar;
            public ConfigurableJoint Saddle;
            public Vector3 TorsoTopLocal;
        }

        public static ChainState BuildChain(IsolatedPhysicsWorld world, ChainSpec spec, float loadKg, float maxForce)
        {
            var state = new ChainState
            {
                Links = new Rigidbody[3],
                Joints = new ConfigurableJoint[3],
                StartRotations = new Quaternion[3]
            };
            state.Foot = world.CreateBody("foot", Vector3.zero, Quaternion.identity, 1f, Vector3.one, kinematic: true, gravity: false);
            Vector3 jointPoint = Vector3.zero;
            Rigidbody parent = state.Foot;
            for (int i = 0; i < 3; i++)
            {
                Quaternion rotation = Quaternion.AngleAxis(spec.Angle[i] * Mathf.Rad2Deg, Vector3.right);
                Vector3 direction = rotation * Vector3.up;
                float length = spec.Length[i];
                float mass = spec.Mass[i];
                Vector3 com = jointPoint + direction * (length * 0.5f);
                float transverse = mass * length * length / 12f;
                Rigidbody link = world.CreateBody(spec.Names[i], com, rotation, mass,
                    new Vector3(transverse, mass * 0.01f, transverse));
                ConfigurableJoint joint = IsolatedPhysicsWorld.Hinge(link, parent, new Vector3(0f, -length * 0.5f, 0f));
                JointFamilyProfile profile = PoweredJointController.FindFamilyProfile(spec.Family[i]).Value;
                joint.angularXDrive = IsolatedPhysicsWorld.Drive(profile.Spring * spec.DriveMultiplier,
                    profile.Damper * spec.DriveMultiplier, maxForce);
                joint.targetRotation = Quaternion.identity;
                state.Links[i] = link;
                state.Joints[i] = joint;
                state.StartRotations[i] = rotation;
                jointPoint += direction * length;
                parent = link;
            }
            state.TorsoTopLocal = new Vector3(0f, spec.Length[2] * 0.5f, 0f);
            if (loadKg > 0f)
            {
                Vector3 top = state.Links[2].transform.TransformPoint(state.TorsoTopLocal);
                state.Bar = SaddleReplica.CreateBar(world, loadKg, top);
                state.Saddle = SaddleReplica.Attach(state.Bar, state.Links[2], state.TorsoTopLocal);
            }
            return state;
        }

        /// <summary>
        /// Independent planar statics: solves K_j e_j = tau_j(q0 + e) for the
        /// chain's gravity torques with a damped fixed point, outside PhysX.
        /// </summary>
        public static double[] ExpectedStaticErrors(ChainSpec spec, float barMassKg, double g)
        {
            var k = new double[3];
            for (int i = 0; i < 3; i++)
                k[i] = PoweredJointController.FindFamilyProfile(spec.Family[i]).Value.Spring * spec.DriveMultiplier;
            // Newton on r(e) = K e - tau(e). A damped fixed point contracts
            // ever more slowly as gravitational stiffness approaches K (heavy
            // loads), and stopped short of the equilibrium at 170 kg.
            var e = new double[3];
            for (int iteration = 0; iteration < 100; iteration++)
            {
                double[] r = Residual(spec, k, e, barMassKg, g);
                double norm = Math.Max(Math.Abs(r[0]), Math.Max(Math.Abs(r[1]), Math.Abs(r[2])));
                if (norm < 1e-9)
                    return e;
                var jacobian = new double[3, 3];
                const double h = 1e-7;
                for (int c = 0; c < 3; c++)
                {
                    var shifted = (double[])e.Clone();
                    shifted[c] += h;
                    double[] rs = Residual(spec, k, shifted, barMassKg, g);
                    for (int row = 0; row < 3; row++)
                        jacobian[row, c] = (rs[row] - r[row]) / h;
                }
                double[] step = Solve3(jacobian, r);
                for (int j = 0; j < 3; j++)
                    e[j] -= step[j];
            }
            throw new InvalidOperationException("Chain statics did not converge.");
        }

        private static double[] Residual(ChainSpec spec, double[] k, double[] e, float barMassKg, double g)
        {
            double[] tau = GravityTorques(spec, e, barMassKg, g);
            return new[] { k[0] * e[0] - tau[0], k[1] * e[1] - tau[1], k[2] * e[2] - tau[2] };
        }

        private static double[] Solve3(double[,] a, double[] b)
        {
            double Det(double[,] m) =>
                m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
                m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
                m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);
            double d = Det(a);
            var x = new double[3];
            for (int c = 0; c < 3; c++)
            {
                var m = (double[,])a.Clone();
                for (int row = 0; row < 3; row++)
                    m[row, c] = b[row];
                x[c] = Det(m) / d;
            }
            return x;
        }

        public static double[] GravityTorques(ChainSpec spec, double[] e, float barMassKg, double g)
        {
            var jointZ = new double[4];
            var comZ = new double[3];
            double z = 0;
            double cumulative = 0;
            for (int i = 0; i < 3; i++)
            {
                cumulative += e[i];
                double angle = spec.Angle[i] + cumulative;
                // AngleAxis(angle, +X) * up = (0, cos, sin).
                double dz = Math.Sin(angle);
                jointZ[i] = z;
                comZ[i] = z + dz * spec.Length[i] * 0.5;
                z += dz * spec.Length[i];
            }
            jointZ[3] = z;
            var tau = new double[3];
            for (int j = 0; j < 3; j++)
            {
                double sum = 0;
                for (int i = j; i < 3; i++)
                    sum += spec.Mass[i] * (comZ[i] - jointZ[j]);
                sum += barMassKg * (jointZ[3] - jointZ[j]);
                tau[j] = g * sum;
            }
            return tau;
        }

        public static double JointTwist(ChainState state, int i)
        {
            Rigidbody parent = i == 0 ? state.Foot : state.Links[i - 1];
            Quaternion parentStart = i == 0 ? Quaternion.identity : state.StartRotations[i - 1];
            return IsolatedPhysicsWorld.TwistAboutLocalX(state.StartRotations[i], state.Links[i].rotation, parentStart, parent.rotation);
        }

        public static double MaxAnchorSeparation(ChainState state, ChainSpec spec)
        {
            double worst = 0;
            for (int i = 0; i < 3; i++)
            {
                Rigidbody link = state.Links[i];
                ConfigurableJoint joint = state.Joints[i];
                Vector3 a = link.transform.TransformPoint(joint.anchor);
                Vector3 b = joint.connectedBody.transform.TransformPoint(joint.connectedAnchor);
                worst = Math.Max(worst, (a - b).magnitude);
            }
            return worst;
        }

        public static (double[] errors, double separation, double speed, double saddleSag) SettleChain(
            IsolatedPhysicsWorld world, ChainState state, ChainSpec spec, float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / world.Dt);
            int tailStart = steps - Mathf.RoundToInt(0.5f / world.Dt);
            var sums = new double[3];
            int count = 0;
            double separation = 0;
            for (int i = 0; i < steps; i++)
            {
                world.Step();
                separation = Math.Max(separation, i >= steps / 2 ? MaxAnchorSeparation(state, spec) : 0);
                if (i >= tailStart)
                {
                    for (int j = 0; j < 3; j++)
                        sums[j] += JointTwist(state, j);
                    count++;
                }
            }
            double speed = 0;
            foreach (Rigidbody link in state.Links)
                speed = Math.Max(speed, link.angularVelocity.magnitude);
            double sag = double.NaN;
            if (state.Bar != null)
            {
                Vector3 anchor = state.Links[2].transform.TransformPoint(state.TorsoTopLocal);
                sag = (anchor - state.Bar.position).magnitude;
            }
            return (new[] { sums[0] / count, sums[1] / count, sums[2] / count }, separation, speed, sag);
        }

        [UnityTest]
        public IEnumerator B11_DrivenChain_StaticLoadAndConvergence()
        {
            var rec = new PhysicsBenchmarkRecorder("B11_driven_chain");
            var spec = new ChainSpec();
            double g = -Physics.gravity.y;
            var loads = new List<float> { 0f };
            loads.AddRange(Loads);
            foreach (float load in loads)
            {
                float barMass = load > 0f ? BarbellLoadingSolver.Solve(load).TotalMassKg : 0f;
                double[] expected = ExpectedStaticErrors(spec, barMass, g);
                bool stable = IsStaticallyStable(spec, expected, barMass, g);
                rec.Info("expected_equilibrium_stable", $"load={load}", stable ? 1 : 0,
                    "1 when the potential-energy Hessian is positive definite at the analytic equilibrium.", CausalLayer.AthleteEquilibrium);
                double[] reference = null;
                var grid = new List<(float dt, int pos, int vel, string tag)>
                {
                    (0.0025f, 255, 4, "reference"),
                    (ProductionDt, ProductionPositionIterations, ProductionVelocityIterations, "production")
                };
                foreach (float dt in SweepDt) grid.Add((dt, ProductionPositionIterations, ProductionVelocityIterations, "dt_sweep"));
                foreach (int p in SweepPositionIterations) grid.Add((ProductionDt, p, ProductionVelocityIterations, "pos_sweep"));
                foreach (int v in SweepVelocityIterations) grid.Add((ProductionDt, ProductionPositionIterations, v, "vel_sweep"));
                grid.Add((0.02f, 14, 1, "corner_coarse"));
                foreach (var cell in grid)
                {
                    using (var world = new IsolatedPhysicsWorld("chain", cell.dt, cell.pos, cell.vel))
                    {
                        ChainState state = BuildChain(world, spec, load, 1e7f);
                        var (errors, separation, speed, sag) = SettleChain(world, state, spec, SettleSeconds);
                        string cfg = Cfg($"load={load};{cell.tag}", world);
                        if (cell.tag == "reference")
                            reference = errors;
                        for (int j = 0; j < 3; j++)
                        {
                            string metric = spec.Family[j] + "_static_error_rad";
                            if (cell.tag == "production" && !stable)
                            {
                                rec.Info(metric, cfg, errors[j],
                                    "Expected equilibrium is statically unstable (potential-energy Hessian not positive definite); no static target exists.",
                                    CausalLayer.AthleteEquilibrium);
                            }
                            else if (cell.tag == "production")
                            {
                                rec.Record(metric, cfg, expected[j], errors[j], RealizationTolerance(expected[j]), ToleranceKind.Absolute,
                                    RealizationSource + "; independent planar statics K e = tau(q0+e)",
                                    "A driven chain carrying the athlete's mass ratios does not reach its static PD equilibrium.",
                                    CausalLayer.ConstraintConvergence);
                                rec.Record(spec.Family[j] + "_vs_refined_reference_rad", cfg, reference[j], errors[j],
                                    RealizationTolerance(reference[j]), ToleranceKind.Absolute,
                                    "numerical convergence: production within the 5% realization band of dt=0.0025/255/4",
                                    "Production timestep/iterations are not converged for this load.",
                                    CausalLayer.NumericalConvergence);
                            }
                            else
                            {
                                rec.Info(spec.Family[j] + "_realization_ratio", cfg, errors[j] / expected[j],
                                    "Realised static compliance relative to the analytic expectation.", CausalLayer.NumericalConvergence);
                            }
                        }
                        if (cell.tag == "production" && stable)
                        {
                            rec.Record("max_anchor_separation_m", cfg, 0, separation, 0.002, ToleranceKind.UpperBound,
                                "locked hinge anchors: 2 mm engineering tolerance",
                                "Joint constraints stretch under the chain's mass ratios.", CausalLayer.ConstraintConvergence);
                            rec.Record("settled_max_angular_speed_rad_s", cfg, 0, speed, 0.01, ToleranceKind.UpperBound,
                                "static chain settles within the 12 s window (near-unstable heavy cases have a slow mode)", "The chain does not settle.", CausalLayer.ConstraintConvergence);
                            if (state.Bar != null)
                                rec.Record("saddle_sag_m", cfg, barMass * g / SquatBarSaddle.DefaultLinearSpring, sag, 0.10,
                                    ToleranceKind.Relative, "saddle statics m g / k on a dynamic carrier; 10%",
                                    "The saddle on a dynamic torso does not carry the bar statically.", CausalLayer.BarSaddleLoadPath);
                        }
                        else
                        {
                            rec.Info("max_anchor_separation_m", cfg, separation, "Constraint stretch off production settings.",
                                CausalLayer.NumericalConvergence);
                            rec.Info("settled_max_angular_speed_rad_s", cfg, speed, "Residual motion at the end of the 12 s window.",
                                CausalLayer.NumericalConvergence);
                        }
                    }
                    yield return null;
                }
            }
            rec.WriteAndAssert();
        }

        /// <summary>
        /// The sealed 5% realization band, with an absolute floor of 0.002 rad
        /// (0.11 deg) below which a static error is not resolvable against
        /// solver noise. Applied identically to expected and reference.
        /// </summary>
        public static double RealizationTolerance(double expected) => Math.Max(0.05 * Math.Abs(expected), 0.002);

        /// <summary>Total potential V(e) = sum 1/2 K e^2 + g sum m y(e).</summary>
        public static double Potential(ChainSpec spec, double[] e, float barMassKg, double g)
        {
            double v = 0, y = 0, cumulative = 0;
            for (int i = 0; i < 3; i++)
            {
                double k = PoweredJointController.FindFamilyProfile(spec.Family[i]).Value.Spring * spec.DriveMultiplier;
                v += 0.5 * k * e[i] * e[i];
                cumulative += e[i];
                double dy = Math.Cos(spec.Angle[i] + cumulative);
                v += g * spec.Mass[i] * (y + dy * spec.Length[i] * 0.5);
                y += dy * spec.Length[i];
            }
            return v + g * barMassKg * y;
        }

        public static bool IsStaticallyStable(ChainSpec spec, double[] e, float barMassKg, double g)
        {
            const double h = 1e-4;
            var hs = new double[3, 3];
            for (int a = 0; a < 3; a++)
            {
                for (int b = 0; b < 3; b++)
                {
                    double Shift(int da, int db)
                    {
                        var x = (double[])e.Clone();
                        x[a] += da * h;
                        x[b] += db * h;
                        return Potential(spec, x, barMassKg, g);
                    }
                    hs[a, b] = (Shift(1, 1) - Shift(1, -1) - Shift(-1, 1) + Shift(-1, -1)) / (4 * h * h);
                }
            }
            double m1 = hs[0, 0];
            double m2 = hs[0, 0] * hs[1, 1] - hs[0, 1] * hs[1, 0];
            double m3 = hs[0, 0] * (hs[1, 1] * hs[2, 2] - hs[1, 2] * hs[2, 1])
                - hs[0, 1] * (hs[1, 0] * hs[2, 2] - hs[1, 2] * hs[2, 0])
                + hs[0, 2] * (hs[1, 0] * hs[2, 1] - hs[1, 1] * hs[2, 0]);
            return m1 > 0 && m2 > 0 && m3 > 0;
        }

        private const string RealizationSource =
            "sealed GAM-11 authority PhysicalAthleteSolverProfile realization band 0.95-1.05";
    }
}
