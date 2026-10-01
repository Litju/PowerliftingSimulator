using System;
using System.Collections;
using NUnit.Framework;
using PowerliftingSimulator.Athlete;
using UnityEngine;
using UnityEngine.TestTools;
using static PowerliftingSimulator.PhysicsBenchmarks.PhysicsBenchmarkSettings;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// GAM-50 benchmarks 4-7: ConfigurableJoint drive semantics measured on
    /// single-joint fixtures with a kinematic parent, so the only thing under
    /// test is the PhysX D6 drive as the production PoweredJointController
    /// writes it. Nothing here is inferred from the humanoid.
    /// </summary>
    [Category("PhysicsBenchmark")]
    [Category("PhysicsBenchmarkIsolated")]
    public sealed class DrivePhysicsBenchmarks
    {
        private const string RealizationSource =
            "sealed GAM-11 authority PhysicalAthleteSolverProfile realization band 0.95-1.05 of authored spring";

        /// <summary>Kinematic parent at the origin and a child whose COM sits `lever` metres along +Z from the hinge.</summary>
        private static (Rigidbody parent, Rigidbody child, ConfigurableJoint joint) Lever(
            IsolatedPhysicsWorld world, float massKg, float icm, float lever, bool gravity = true)
        {
            Rigidbody parent = world.CreateBody("parent", Vector3.zero, Quaternion.identity, 1f, Vector3.one, kinematic: true, gravity: false);
            Rigidbody child = world.CreateBody("child", new Vector3(0f, 0f, lever), Quaternion.identity, massKg,
                Vector3.one * icm, gravity: gravity);
            ConfigurableJoint joint = IsolatedPhysicsWorld.Hinge(child, parent, new Vector3(0f, 0f, -lever));
            return (parent, child, joint);
        }

        private static float Twist(Rigidbody child, Rigidbody parent) =>
            IsolatedPhysicsWorld.TwistAboutLocalX(Quaternion.identity, child.rotation, Quaternion.identity, parent.rotation);

        /// <summary>Solves K e = m g L cos(e) for the static droop of a horizontal lever.</summary>
        private static double StaticDroop(double k, double m, double g, double lever)
        {
            double e = m * g * lever / k;
            for (int i = 0; i < 60; i++)
            {
                double f = k * e - m * g * lever * Math.Cos(e);
                double df = k + m * g * lever * Math.Sin(e);
                e -= f / df;
            }
            return e;
        }

        [UnityTest]
        public IEnumerator B04_ConfigurableJointPd_StaticKnownLoad()
        {
            var rec = new PhysicsBenchmarkRecorder("B04_pd_static_known_load");
            double g = -Physics.gravity.y;
            const float lever = 0.30f;
            string[] families = { "ankle", "knee", "hip", "trunk" };
            float[] demandFractions = { 0.10f, 0.50f, 0.90f };
            foreach (string family in families)
            {
                JointFamilyProfile profile = PoweredJointController.FindFamilyProfile(family).Value;
                foreach (float fraction in demandFractions)
                {
                    float torque = fraction * profile.BaseCapacityNm;
                    float mass = (float)(torque / (g * lever));
                    float icm = mass * 0.01f;
                    var sweep = new (float dt, int pos, int vel, bool gate)[]
                    {
                        (ProductionDt, ProductionPositionIterations, ProductionVelocityIterations, true),
                        (ProductionDt, 14, 1, false), (ProductionDt, 56, 1, false), (ProductionDt, 28, 4, false),
                        (0.02f, 28, 1, false), (0.005f, 28, 1, false)
                    };
                    foreach (var s in sweep)
                    {
                        using (var world = new IsolatedPhysicsWorld("pd_static", s.dt, s.pos, s.vel))
                        {
                            var (parent, child, joint) = Lever(world, mass, icm, lever);
                            joint.angularXDrive = IsolatedPhysicsWorld.Drive(profile.Spring, profile.Damper, profile.BaseCapacityNm);
                            joint.targetRotation = PoweredJointController.ToUnityTargetRotation(Quaternion.identity);
                            int steps = Mathf.RoundToInt(3f / s.dt);
                            double tail = 0;
                            int tailCount = 0;
                            for (int i = 0; i < steps; i++)
                            {
                                world.Step();
                                if (world.Time >= 2.5)
                                {
                                    tail += Twist(child, parent);
                                    tailCount++;
                                }
                            }
                            double observed = tail / tailCount;
                            double expected = StaticDroop(profile.Spring, mass, g, lever);
                            string cfg = Cfg($"{family};demand={fraction:F2};K={profile.Spring};m={mass:F2}", world);
                            if (s.gate)
                            {
                                rec.Record("static_error_rad", cfg, expected, observed, 0.05, ToleranceKind.Relative,
                                    RealizationSource + "; expected from K e = m g L cos e",
                                    "The drive does not realise its authored spring under a known static load.",
                                    CausalLayer.DriveSemantics);
                                rec.Record("final_speed_rad_s", cfg, 0, Math.Abs(child.angularVelocity.x), 1e-3,
                                    ToleranceKind.UpperBound, "static equilibrium: settled within 3 s",
                                    "The drive does not settle a static load.", CausalLayer.DriveSemantics);
                            }
                            else
                            {
                                rec.Info("realization_ratio", cfg, observed / expected,
                                    "Realised fraction of authored stiffness outside production settings.",
                                    CausalLayer.NumericalConvergence);
                            }
                        }
                    }
                    yield return null;
                }
            }
            rec.WriteAndAssert();
        }

        private static double ContinuousStep(double t, double target, double wn, double zeta)
        {
            if (zeta < 1.0 - 1e-6)
            {
                double wd = wn * Math.Sqrt(1 - zeta * zeta);
                return target * (1 - Math.Exp(-zeta * wn * t) * (Math.Cos(wd * t) + zeta / Math.Sqrt(1 - zeta * zeta) * Math.Sin(wd * t)));
            }
            if (zeta <= 1.0 + 1e-6)
                return target * (1 - Math.Exp(-wn * t) * (1 + wn * t));
            double root = Math.Sqrt(zeta * zeta - 1);
            double r1 = -wn * (zeta - root);
            double r2 = -wn * (zeta + root);
            return target * (1 + (r2 * Math.Exp(r1 * t) - r1 * Math.Exp(r2 * t)) / (r1 - r2));
        }

        [UnityTest]
        public IEnumerator B05_ConfigurableJointPd_StepResponse()
        {
            var rec = new PhysicsBenchmarkRecorder("B05_pd_step_response");
            const double target = 0.2;
            var cases = new (string label, float k, float d, float inertia)[]
            {
                ("knee_family_critical_inertia", 2900f, 152f, 152f * 152f / (4f * 2900f)),
                ("knee_K_loaded_inertia_zeta0p7", 2900f, (float)(2 * 0.7 * Math.Sqrt(2900.0 * 29.0)), 29f),
                ("hip_family_inertia_5", 3150f, 168f, 5f),
                ("trunk_family_inertia_3", 7850f, 266f, 3f)
            };
            foreach (var c in cases)
            {
                double wn = Math.Sqrt(c.k / c.inertia);
                double zeta = c.d / (2 * Math.Sqrt(c.k * c.inertia));
                double previousRms = double.NaN;
                foreach (float dt in SweepDt)
                {
                    using (var world = new IsolatedPhysicsWorld("pd_step", dt, ProductionPositionIterations, ProductionVelocityIterations))
                    {
                        var (parent, child, joint) = Lever(world, 10f, c.inertia, 0f, gravity: false);
                        joint.angularXDrive = IsolatedPhysicsWorld.Drive(c.k, c.d, 1e7f);
                        joint.targetRotation = PoweredJointController.ToUnityTargetRotation(
                            Quaternion.AngleAxis((float)(target * Mathf.Rad2Deg), Vector3.right));
                        int steps = Mathf.RoundToInt(1.5f / dt);
                        double sumSq = 0;
                        double peak = 0;
                        for (int i = 0; i < steps; i++)
                        {
                            world.Step();
                            double x = Twist(child, parent);
                            double expected = ContinuousStep(world.Time, target, wn, zeta);
                            sumSq += (x - expected) * (x - expected);
                            peak = Math.Max(peak, x);
                            if (Math.Abs(dt - ProductionDt) < 1e-6f)
                                rec.Series(c.label, "t,observed_rad,continuous_rad", Inv(world.Time) + "," + Inv(x) + "," + Inv(expected));
                        }
                        double rms = Math.Sqrt(sumSq / steps) / target;
                        double final = Twist(child, parent);
                        string cfg = Cfg($"{c.label};K={c.k};D={c.d:F1};I={c.inertia:F3};wn_dt={wn * dt:F3};zeta={zeta:F3}", world);
                        bool production = Math.Abs(dt - ProductionDt) < 1e-6f;
                        if (production)
                        {
                            rec.Record("final_value_rad", cfg, target, final, 0.005, ToleranceKind.Relative,
                                "steady state of a PD drive is its target", "The drive does not converge to its target.",
                                CausalLayer.DriveSemantics);
                            rec.Record("trajectory_rms_error_fraction", cfg, 0, rms, 0.05, ToleranceKind.UpperBound,
                                "continuous 2nd-order analytic response; 5% of step amplitude product tolerance",
                                "Discrete drive dynamics deviate materially from the authored K/D second-order system at production dt.",
                                CausalLayer.NumericalConvergence);
                            double analyticPeak = 0;
                            for (double t = 0; t < 1.5; t += 1e-4)
                                analyticPeak = Math.Max(analyticPeak, ContinuousStep(t, target, wn, zeta));
                            rec.Record("overshoot_fraction", cfg, analyticPeak / target - 1, peak / target - 1, 0.02, ToleranceKind.Absolute,
                                "continuous analytic overshoot; 2% of amplitude", "Drive damping semantics differ from D.",
                                CausalLayer.DriveSemantics);
                        }
                        else
                        {
                            rec.Info("trajectory_rms_error_fraction", cfg, rms, "Step-response deviation off production dt.",
                                CausalLayer.NumericalConvergence);
                        }
                        if (!double.IsNaN(previousRms) && previousRms > 1e-3)
                            rec.Info("rms_error_reduction_per_dt_halving", cfg, previousRms / Math.Max(rms, 1e-12),
                                "Convergence order evidence (2 = first order).", CausalLayer.NumericalConvergence);
                        previousRms = rms;
                    }
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }

        /// <summary>
        /// Rotates a body about a world pivot after its joint exists, so the
        /// joint's neutral stays at the authored pose and the body starts
        /// already deflected. Initialization only; no simulation step.
        /// </summary>
        private static void PreDeflect(Rigidbody body, Vector3 pivot, Quaternion rotation)
        {
            Vector3 position = pivot + rotation * (body.position - pivot);
            Quaternion orientation = rotation * body.rotation;
            body.transform.SetPositionAndRotation(position, orientation);
            body.position = position;
            body.rotation = orientation;
        }

        /// <summary>
        /// The swing torque magnitude, along a joint-space YZ direction, at
        /// which the production demand model reports a demand fraction of 1.
        /// </summary>
        private static double ProductionSwingAuthority(JointFamilyProfile probe, Vector2 directionYz, float maxForce)
        {
            double lo = 0, hi = 10 * maxForce;
            for (int i = 0; i < 80; i++)
            {
                double mid = 0.5 * (lo + hi);
                // Torque = K * error for the probe profile (D = 0, K = 1).
                Vector3 error = new Vector3(0f, (float)(mid * directionYz.x), (float)(mid * directionYz.y));
                PoweredDriveDemand demand = PoweredJointController.ModelDemandForTest(
                    PhysicalJointKind.Ball, probe, error, Vector3.zero, maxForce);
                if (demand.SwingFraction >= 1f) hi = mid; else lo = mid;
            }
            return 0.5 * (lo + hi);
        }

        [UnityTest]
        public IEnumerator B06_MaximumForce_SaturationIsATorqueLimit()
        {
            var rec = new PhysicsBenchmarkRecorder("B06_maximum_force_saturation");
            double g = -Physics.gravity.y;
            const float lever = 0.4f;
            const float mass = 50f;
            const float icm = 0.5f;
            double tauG = mass * g * lever;
            double ip = icm + mass * lever * lever;
            const float stiffK = 1e5f;
            foreach (double ratio in new[] { 0.5, 0.9, 1.25, 1.5, 2.0 })
            {
                float maxForce = (float)(tauG / ratio);
                using (IsolatedPhysicsWorld world = ProductionWorld("saturation"))
                {
                    var (parent, child, joint) = Lever(world, mass, icm, lever);
                    joint.angularXDrive = IsolatedPhysicsWorld.Drive(stiffK, 0f, maxForce);
                    joint.targetRotation = Quaternion.identity;
                    // Start exactly at the force ceiling, so the drive is
                    // saturated from the first tick and the closed-form
                    // expectation has no onset transient.
                    double theta0 = ratio < 1 ? 0.0 : maxForce / stiffK;
                    PreDeflect(child, Vector3.zero, Quaternion.AngleAxis((float)(theta0 * Mathf.Rad2Deg), Vector3.right));
                    float horizon = ratio < 1 ? 2f : 0.1f;
                    int steps = Mathf.RoundToInt(horizon / world.Dt);
                    for (int i = 0; i < steps; i++)
                    {
                        world.Step();
                        rec.Series("ratio" + Inv(ratio), "t,theta_rad,torque_x_nm",
                            Inv(world.Time) + "," + Inv(Twist(child, parent)) + "," + Inv(joint.currentTorque.x));
                    }
                    double theta = Twist(child, parent);
                    string cfg = Cfg($"tau_g/maxForce={ratio:F2};maxForce={maxForce:F1}", world);
                    if (ratio < 1)
                    {
                        rec.Record("held_static_error_rad", cfg, StaticDroop(stiffK, mass, g, lever), theta, 1e-3, ToleranceKind.Absolute,
                            "below capacity the drive holds at e = tau/K", "The drive yields below its maximumForce.",
                            CausalLayer.DriveSemantics);
                    }
                    else
                    {
                        // Force semantics: the net torque is tau_g - F;
                        // integrate exactly with the engine's semi-implicit scheme.
                        double alpha = (tauG - maxForce) / ip;
                        double expected = theta0 + alpha * world.Dt * world.Dt * steps * (steps + 1) / 2.0;
                        rec.Record("saturated_droop_after_0p1s_rad", cfg, expected, theta, 0.05, ToleranceKind.Relative,
                            "maximumForce is a torque ceiling (N m): alpha = (tau_g - F)/I_pivot from a saturated start; 5%",
                            "maximumForce does not act as a torque limit (e.g. it is applied as an impulse or scaled by dt).",
                            CausalLayer.DriveSemantics);
                        rec.Info("impulse_semantics_prediction_rad", cfg, theta0,
                            "Droop if maximumForce were a per-step impulse (drive would hold).", CausalLayer.DriveSemantics);
                    }
                }
                yield return null;
            }

            // Swing channel: the production demand model's notion of swing
            // authority, checked against what PhysX actually clamps. Gravity
            // loads the swing drive at 45 degrees between joint Y and Z.
            var probe = new JointFamilyProfile("probe", 1f, 0f, 1f, 1f);
            var direction = new Vector2(Mathf.Sqrt(0.5f), Mathf.Sqrt(0.5f));
            foreach (double ratio in new[] { 1.25, 1.6, 2.0 })
            {
                float maxForce = (float)(tauG / ratio);
                double modelAuthority = ProductionSwingAuthority(probe, direction, maxForce);
                using (IsolatedPhysicsWorld world = ProductionWorld("swing_saturation"))
                {
                    Rigidbody parent = world.CreateBody("parent", Vector3.zero, Quaternion.identity, 1f, Vector3.one, kinematic: true, gravity: false);
                    float r = lever / Mathf.Sqrt(2f);
                    Rigidbody child = world.CreateBody("child", new Vector3(r, 0f, r), Quaternion.identity, mass, Vector3.one * icm);
                    ConfigurableJoint joint = IsolatedPhysicsWorld.Hinge(child, parent, new Vector3(-r, 0f, -r));
                    joint.axis = Vector3.up;
                    joint.secondaryAxis = Vector3.right;
                    joint.angularXMotion = ConfigurableJointMotion.Locked;
                    joint.angularYMotion = ConfigurableJointMotion.Free;
                    joint.angularZMotion = ConfigurableJointMotion.Free;
                    joint.angularYZDrive = IsolatedPhysicsWorld.Drive(stiffK, 0f, maxForce);
                    joint.targetRotation = Quaternion.identity;
                    // Pre-deflect along the gravity-torque axis far enough to
                    // saturate either clamp hypothesis (sqrt(2) F / K).
                    Vector3 torqueAxis = new Vector3(1f, 0f, -1f).normalized;
                    double theta0 = Math.Sqrt(2) * maxForce / stiffK;
                    PreDeflect(child, Vector3.zero, Quaternion.AngleAxis((float)(theta0 * Mathf.Rad2Deg), torqueAxis));
                    int steps = Mathf.RoundToInt(0.1f / world.Dt);
                    world.Step(steps);
                    double droop = Vector3.Angle(Vector3.up, child.rotation * Vector3.up) * Mathf.Deg2Rad;
                    double factor = world.Dt * world.Dt * steps * (steps + 1) / 2.0;
                    double modelAlpha = Math.Max(0, tauG - modelAuthority) / ip;
                    // A held case relaxes to its static droop tau/K (per-axis
                    // torques tau/sqrt2 each balanced by K e_axis), not to the
                    // pre-deflection.
                    double heldDroop = tauG / stiffK;
                    double perAxisAlpha = Math.Max(0, tauG - Math.Sqrt(2) * maxForce) / ip;
                    double magnitudeAlpha = Math.Max(0, tauG - maxForce) / ip;
                    string cfg = Cfg($"swing45deg;tau_g/maxForce={ratio:F2}", world);
                    rec.Record("swing_droop_vs_production_demand_model_rad", cfg,
                        modelAlpha > 0 ? theta0 + modelAlpha * factor : heldDroop, droop, 0.05,
                        ToleranceKind.Relative,
                        "production PoweredJointController.ModelDriveDemand swing authority at 45 deg, saturated start; 5%",
                        "PhysX clamps the swing drive differently from the production demand model: swing demand telemetry misstates available authority.",
                        CausalLayer.DriveSemantics);
                    rec.Info("per_axis_clamp_prediction_rad", cfg, theta0 + perAxisAlpha * factor,
                        "Droop if each swing axis is clamped to maximumForce independently.", CausalLayer.DriveSemantics);
                    rec.Info("magnitude_clamp_prediction_rad", cfg, theta0 + magnitudeAlpha * factor,
                        "Droop if the swing torque vector magnitude is clamped to maximumForce.", CausalLayer.DriveSemantics);
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }

        [UnityTest]
        public IEnumerator B07_TargetRotationAndVelocity_SignAndFrame()
        {
            var rec = new PhysicsBenchmarkRecorder("B07_target_sign_frame");
            const float targetDeg = 20f;

            // (a) Production rotation mapping on a hinge aligned with the body.
            foreach (bool production in new[] { true, false })
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("target_sign"))
                {
                    var (parent, child, joint) = Lever(world, 1f, 1f, 0f, gravity: false);
                    joint.angularXDrive = IsolatedPhysicsWorld.Drive(500f, 50f, 1e6f);
                    Quaternion logical = Quaternion.AngleAxis(targetDeg, Vector3.right);
                    joint.targetRotation = production ? PoweredJointController.ToUnityTargetRotation(logical) : logical;
                    world.Step(Mathf.RoundToInt(2f / world.Dt));
                    double observedDeg = Twist(child, parent) * Mathf.Rad2Deg;
                    string cfg = Cfg(production ? "ToUnityTargetRotation(+20deg)" : "raw targetRotation=+20deg", world);
                    rec.Record("hinge_twist_deg", cfg, production ? targetDeg : -targetDeg, observedDeg, 0.05, ToleranceKind.Absolute,
                        production ? "production convention: logical joint-space target is realised with its own sign"
                                   : "Unity convention: targetRotation is the inverse of the realised joint rotation",
                        "targetRotation sign/frame convention differs from what PoweredJointController assumes.",
                        CausalLayer.JointTopologyTargetConvention);
                }
            }

            // (b) Target angular velocity sign with a pure damper.
            foreach (bool production in new[] { true, false })
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("target_velocity"))
                {
                    var (parent, child, joint) = Lever(world, 1f, 1f, 0f, gravity: false);
                    joint.angularXDrive = IsolatedPhysicsWorld.Drive(0f, 50f, 1e6f);
                    var logical = new Vector3(1.0f, 0f, 0f);
                    joint.targetAngularVelocity = production
                        ? PoweredJointController.ToUnityTargetAngularVelocity(logical)
                        : logical;
                    world.Step(Mathf.RoundToInt(1f / world.Dt));
                    double observed = child.angularVelocity.x - parent.angularVelocity.x;
                    string cfg = Cfg(production ? "production ToUnityTargetAngularVelocity(+w)" : "raw targetAngularVelocity=+w", world);
                    if (production)
                        rec.Record("hinge_relative_velocity_rad_s", cfg, 1.0, observed, 0.01, ToleranceKind.Absolute,
                            "production mapping must realise the logical +w about the joint axis",
                            "targetAngularVelocity sign differs from PoweredJointController's convention: the damper fights commanded motion.",
                            CausalLayer.JointTopologyTargetConvention);
                    else
                        rec.Info("raw_hinge_relative_velocity_rad_s", cfg, observed,
                            "Unity's own convention for targetAngularVelocity on an aligned hinge.",
                            CausalLayer.JointTopologyTargetConvention);
                }
            }

            // (b2) The velocity convention in a general joint frame: raw
            // targetAngularVelocity along each joint axis of a rotated ball
            // joint, measured as the child's relative angular velocity about
            // the corresponding child-fixed world axis.
            foreach (var axisCase in new[] { ("x", Vector3.right), ("y", Vector3.up), ("z", Vector3.forward) })
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("target_velocity_frame"))
                {
                    Quaternion childRotation = Quaternion.Euler(25f, -40f, 15f);
                    Rigidbody parent = world.CreateBody("parent", Vector3.zero, Quaternion.Euler(-10f, 30f, 5f), 1f, Vector3.one, kinematic: true, gravity: false);
                    Rigidbody child = world.CreateBody("child", Vector3.zero, childRotation, 1f, Vector3.one, gravity: false);
                    ConfigurableJoint joint = IsolatedPhysicsWorld.Hinge(child, parent, Vector3.zero);
                    joint.axis = new Vector3(0.2f, 0.1f, 1f).normalized;
                    joint.secondaryAxis = new Vector3(0f, 1f, -0.1f).normalized;
                    joint.angularXMotion = ConfigurableJointMotion.Free;
                    joint.angularYMotion = ConfigurableJointMotion.Free;
                    joint.angularZMotion = ConfigurableJointMotion.Free;
                    JointDrive damper = IsolatedPhysicsWorld.Drive(0f, 50f, 1e6f);
                    joint.angularXDrive = damper;
                    joint.angularYZDrive = damper;
                    joint.targetAngularVelocity = axisCase.Item2 * 0.5f;
                    world.Step(Mathf.RoundToInt(0.3f / world.Dt));
                    Vector3 x = joint.axis.normalized;
                    Vector3 z = Vector3.Cross(x, joint.secondaryAxis).normalized;
                    Vector3 y = Vector3.Cross(z, x);
                    Vector3 local = axisCase.Item1 == "x" ? x : axisCase.Item1 == "y" ? y : z;
                    Vector3 worldAxis = child.rotation * local;
                    double observed = Vector3.Dot(child.angularVelocity - parent.angularVelocity, worldAxis);
                    rec.Record("raw_velocity_sign_rotated_frame_rad_s", Cfg("rotated_ball;axis=" + axisCase.Item1, world), 0.5, observed, 0.01,
                        ToleranceKind.Absolute,
                        "Unity measured convention from (b): raw targetAngularVelocity is realised with its own sign in joint space",
                        "The targetAngularVelocity convention depends on the joint frame.", CausalLayer.JointTopologyTargetConvention);
                }
            }

            // (c) Non-trivial joint frame on a rotated ball joint, checked by
            // an independent world-space axis/angle, not the production
            // diagnostic formula.
            var targets = new (string label, Vector3 jointAxis)[]
            {
                ("twist_x", Vector3.right), ("swing_y", Vector3.up), ("swing_z", Vector3.forward)
            };
            foreach (var t in targets)
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("target_frame"))
                {
                    Quaternion childRotation = Quaternion.Euler(25f, -40f, 15f);
                    Rigidbody parent = world.CreateBody("parent", Vector3.zero, Quaternion.Euler(-10f, 30f, 5f), 1f, Vector3.one, kinematic: true, gravity: false);
                    Rigidbody child = world.CreateBody("child", Vector3.zero, childRotation, 1f, Vector3.one, gravity: false);
                    ConfigurableJoint joint = IsolatedPhysicsWorld.Hinge(child, parent, Vector3.zero);
                    joint.axis = new Vector3(0.2f, 0.1f, 1f).normalized;
                    joint.secondaryAxis = new Vector3(0f, 1f, -0.1f).normalized;
                    joint.angularXMotion = ConfigurableJointMotion.Free;
                    joint.angularYMotion = ConfigurableJointMotion.Free;
                    joint.angularZMotion = ConfigurableJointMotion.Free;
                    JointDrive drive = IsolatedPhysicsWorld.Drive(500f, 50f, 1e6f);
                    joint.angularXDrive = drive;
                    joint.angularYZDrive = drive;
                    Quaternion logical = Quaternion.AngleAxis(targetDeg, t.jointAxis);
                    joint.targetRotation = PoweredJointController.ToUnityTargetRotation(logical);
                    world.Step(Mathf.RoundToInt(2f / world.Dt));

                    Vector3 x = joint.axis.normalized;
                    Vector3 z = Vector3.Cross(x, joint.secondaryAxis).normalized;
                    Vector3 y = Vector3.Cross(z, x);
                    Vector3 localAxis = t.label == "twist_x" ? x : t.label == "swing_y" ? y : z;
                    Vector3 expectedWorldAxis = childRotation * localAxis;
                    Quaternion delta = child.rotation * Quaternion.Inverse(childRotation);
                    delta.ToAngleAxis(out float angle, out Vector3 axis);
                    if (angle > 180f) { angle = 360f - angle; axis = -axis; }
                    string cfg = Cfg("rotated_ball;" + t.label, world);
                    rec.Record("realised_angle_deg", cfg, targetDeg, angle, 0.1, ToleranceKind.Absolute,
                        "independent world axis-angle of the child displacement",
                        "Joint-space target magnitude is not realised.", CausalLayer.JointTopologyTargetConvention);
                    rec.Record("realised_axis_alignment", cfg, 1.0, Vector3.Dot(axis.normalized, expectedWorldAxis.normalized), 1e-3,
                        ToleranceKind.Absolute, "realised rotation axis equals child.rotation0 * joint-frame axis",
                        "Joint-space target is applied about the wrong axis or with the wrong sign.",
                        CausalLayer.JointTopologyTargetConvention);
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }
    }
}
