using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static PowerliftingSimulator.PhysicsBenchmarks.PhysicsBenchmarkSettings;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>
    /// GAM-50 benchmarks 1-3: rigid-body integration against closed-form
    /// mechanics. These say nothing about the athlete; they establish that the
    /// engine, units and frames the athlete is built on behave as Newton says.
    /// </summary>
    [Category("PhysicsBenchmark")]
    [Category("PhysicsBenchmarkIsolated")]
    public sealed class AnalyticalPhysicsBenchmarks
    {
        [UnityTest]
        public IEnumerator B01_FreeFall_MatchesAnalyticMotion()
        {
            var rec = new PhysicsBenchmarkRecorder("B01_free_fall");
            Vector3 gravity = Physics.gravity;
            rec.Record("gravity_y_mps2", "project DynamicsManager", -G, gravity.y, 1e-6, ToleranceKind.Absolute,
                "SI units contract: g = 9.81 m/s^2 along world -Y (CoordinatesAndUnits)",
                "World gravity is not the SI value the project's mechanics assume.", CausalLayer.UnitsFramesEquations);
            rec.Record("gravity_horizontal_mps2", "project DynamicsManager", 0, Mathf.Abs(gravity.x) + Mathf.Abs(gravity.z),
                1e-9, ToleranceKind.Absolute, "world frame contract: +Y up", "Gravity is not vertical.",
                CausalLayer.UnitsFramesEquations);

            foreach (float dt in SweepDt)
            {
                using (var world = new IsolatedPhysicsWorld("free_fall", dt, ProductionPositionIterations, ProductionVelocityIterations))
                {
                    const float y0 = 10f;
                    Rigidbody body = world.CreateBody("ball", new Vector3(0f, y0, 0f), Quaternion.identity, 1f, Vector3.one * 0.01f);
                    int steps = Mathf.RoundToInt(1f / dt);
                    world.Step(steps);
                    double t = steps * (double)dt;
                    double g = -gravity.y;
                    // PhysX integrates semi-implicitly: v += g dt, then x += v dt.
                    double discreteDrop = g * dt * dt * steps * (steps + 1) / 2.0;
                    double continuousDrop = 0.5 * g * t * t;
                    double observedDrop = y0 - body.position.y;
                    string cfg = Cfg("t=1s", world);
                    rec.Record("velocity_y_at_1s", cfg, -g * t, body.linearVelocity.y,
                        Float32AccumulationBound(g * t, steps), ToleranceKind.Absolute,
                        "analytic v = g t; float32 accumulation bound",
                        "Gravity is not integrated as a constant acceleration.", CausalLayer.UnitsFramesEquations);
                    rec.Record("drop_vs_symplectic_euler_m", cfg, discreteDrop, observedDrop,
                        Float32AccumulationBound(y0, steps), ToleranceKind.Absolute,
                        "exact semi-implicit Euler solution g dt^2 N(N+1)/2; float32 accumulation bound",
                        "The engine integrator is not the documented semi-implicit Euler.", CausalLayer.UnitsFramesEquations);
                    // The O(dt) bias of a first-order integrator against the
                    // continuous solution must be exactly g t dt / 2.
                    double bias = observedDrop - continuousDrop;
                    rec.Record("first_order_bias_ratio", cfg, 1.0, bias / (0.5 * g * t * dt), 0.02, ToleranceKind.Absolute,
                        "numerical convergence: first-order integrator bias = g t dt / 2",
                        "Integration error does not converge at first order in dt.", CausalLayer.NumericalConvergence);
                    rec.Info("continuous_drop_error_m", cfg, Math.Abs(bias),
                        "Absolute deviation from 0.5 g t^2 after one second.", CausalLayer.NumericalConvergence);
                }
                yield return null;
            }

            // Characterise the non-physical linear damping the athlete bodies
            // carry (PhysicalAthleteRig: linearDamping 0.04).
            using (IsolatedPhysicsWorld world = ProductionWorld("free_fall_damped"))
            {
                Rigidbody body = world.CreateBody("ball", new Vector3(0f, 10f, 0f), Quaternion.identity, 1f, Vector3.one * 0.01f);
                body.linearDamping = 0.04f;
                int steps = Mathf.RoundToInt(1f / world.Dt);
                world.Step(steps);
                double expected = -(-gravity.y) * steps * world.Dt;
                rec.Info("athlete_linear_damping_velocity_loss_fraction_1s", Cfg("linearDamping=0.04", world),
                    1.0 - body.linearVelocity.y / expected,
                    "Velocity removed by Rigidbody.linearDamping in one second of free fall.", CausalLayer.MassComInertia);
            }

            rec.WriteAndAssert();
        }

        [UnityTest]
        public IEnumerator B02_ConstantTorque_MatchesAlphaEqualsTauOverI()
        {
            var rec = new PhysicsBenchmarkRecorder("B02_constant_torque");
            var inertia = new Vector3(0.5f, 2.0f, 1.0f);
            var cases = new[]
            {
                (label: "principal_x", rot: Quaternion.identity, axis: Vector3.right, i: inertia.x),
                (label: "principal_y", rot: Quaternion.identity, axis: Vector3.up, i: inertia.y),
                (label: "principal_z", rot: Quaternion.identity, axis: Vector3.forward, i: inertia.z),
                // Rotated tensor frame: torque applied along the rotated
                // principal X direction must see I_x, proving the inertia
                // tensor rotation is honoured.
                (label: "rotated_tensor_x", rot: Quaternion.AngleAxis(30f, Vector3.forward), axis: Quaternion.AngleAxis(30f, Vector3.forward) * Vector3.right, i: inertia.x),
                // Body itself rotated in the world: world-space torque along
                // the body's local Z must see I_z.
                (label: "rotated_body_z", rot: Quaternion.identity, axis: Quaternion.Euler(20f, 35f, -10f) * Vector3.forward, i: inertia.z)
            };

            foreach (var c in cases)
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("torque"))
                {
                    Quaternion bodyRotation = c.label == "rotated_body_z" ? Quaternion.Euler(20f, 35f, -10f) : Quaternion.identity;
                    Rigidbody body = world.CreateBody("rotor", Vector3.zero, bodyRotation, 3f, inertia, gravity: false);
                    body.inertiaTensorRotation = c.rot;
                    const float torque = 0.75f;
                    int steps = Mathf.RoundToInt(0.5f / world.Dt);
                    for (int i = 0; i < steps; i++)
                    {
                        body.AddTorque(c.axis * torque, ForceMode.Force);
                        world.Step();
                    }
                    double t = steps * (double)world.Dt;
                    double expected = torque / c.i * t;
                    Vector3 w = body.angularVelocity;
                    double along = Vector3.Dot(w, c.axis);
                    double offAxis = (w - c.axis * (float)along).magnitude;
                    string cfg = Cfg(c.label, world);
                    rec.Record("omega_along_torque_rad_s", cfg, expected, along, 1e-4, ToleranceKind.Relative,
                        "analytic omega = tau t / I about a principal axis; float32 bound",
                        "Inertia, inertia frame, or torque units are wrong.", CausalLayer.MassComInertia);
                    rec.Record("omega_off_axis_rad_s", cfg, 0, offAxis, 1e-4 * Math.Max(1.0, expected), ToleranceKind.Absolute,
                        "principal-axis rotation has no gyroscopic coupling",
                        "Torque produced rotation off the commanded principal axis: inertia frame error.",
                        CausalLayer.MassComInertia);
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }

        [UnityTest]
        public IEnumerator B03_Pendulum_PeriodAndEnergy()
        {
            var rec = new PhysicsBenchmarkRecorder("B03_pendulum");
            const float length = 1.0f;
            const float mass = 1.0f;
            const float icm = 0.004f;
            float theta0 = 5f * Mathf.Deg2Rad;
            double g = -Physics.gravity.y;
            double ip = icm + mass * length * length;
            double t0 = 2.0 * Math.PI * Math.Sqrt(ip / (mass * g * length));
            double expectedPeriod = t0 * (1.0 + theta0 * theta0 / 16.0 + 11.0 * Math.Pow(theta0, 4) / 3072.0);

            foreach (float dt in SweepDt)
            {
                using (var world = new IsolatedPhysicsWorld("pendulum", dt, ProductionPositionIterations, ProductionVelocityIterations))
                {
                    Quaternion start = Quaternion.AngleAxis(theta0 * Mathf.Rad2Deg, Vector3.right);
                    Vector3 bobPosition = start * new Vector3(0f, -length, 0f);
                    Rigidbody bob = world.CreateBody("bob", bobPosition, start, mass, Vector3.one * icm);
                    ConfigurableJoint joint = IsolatedPhysicsWorld.Hinge(bob, null, new Vector3(0f, length, 0f));
                    Assert.That(joint, Is.Not.Null);

                    double Theta() => Math.Atan2(-bob.position.z, -bob.position.y);
                    double Energy()
                    {
                        double th = Theta();
                        double w = bob.angularVelocity.x;
                        return 0.5 * ip * w * w + mass * g * length * (1.0 - Math.Cos(th));
                    }

                    double e0 = mass * g * length * (1.0 - Math.Cos(theta0));
                    int steps = Mathf.RoundToInt((float)(5.5 * t0 / dt));
                    double previous = Theta();
                    double firstCrossing = double.NaN;
                    double lastCrossing = double.NaN;
                    int crossings = 0;
                    double maxSeparation = 0;
                    double maxEnergyDeviation = 0;
                    for (int i = 0; i < steps; i++)
                    {
                        world.Step();
                        double th = Theta();
                        if (previous > 0 && th <= 0)
                        {
                            double fraction = previous / (previous - th);
                            double crossing = (world.Tick - 1 + fraction) * dt;
                            if (crossings == 0) firstCrossing = crossing;
                            lastCrossing = crossing;
                            crossings++;
                        }
                        previous = th;
                        maxSeparation = Math.Max(maxSeparation, Math.Abs(bob.position.magnitude - length));
                        maxEnergyDeviation = Math.Max(maxEnergyDeviation, Math.Abs(Energy() - e0) / e0);
                        rec.Series("trace_dt" + Inv(dt), "t,theta_rad,omega_rad_s,energy_j",
                            Inv(world.Time) + "," + Inv(th) + "," + Inv(bob.angularVelocity.x) + "," + Inv(Energy()));
                    }
                    double period = crossings >= 2 ? (lastCrossing - firstCrossing) / (crossings - 1) : double.NaN;
                    string cfg = Cfg("theta0=5deg;L=1m", world);
                    rec.Record("period_s", cfg, expectedPeriod, period, 0.005, ToleranceKind.Relative,
                        "analytic compound-pendulum period with finite-amplitude series; 0.5% engineering tolerance (symplectic phase error at w dt<=0.06 is <0.02%)",
                        "Gravity/inertia coupling through a joint is wrong.", CausalLayer.MassComInertia);
                    // Gated at production dt like every other sweep in V1; the
                    // other timesteps characterise convergence.
                    if (Mathf.Approximately(dt, ProductionDt))
                        rec.Record("max_energy_deviation_fraction", cfg, 0, maxEnergyDeviation, 0.02, ToleranceKind.UpperBound,
                            "symplectic integration keeps energy bounded; 2% engineering tolerance over 5 periods",
                            "The joint constraint injects or dissipates energy.", CausalLayer.ConstraintConvergence);
                    else
                        rec.Info("max_energy_deviation_fraction", cfg, maxEnergyDeviation,
                            "Energy deviation off production dt (convergence evidence).", CausalLayer.NumericalConvergence);
                    rec.Record("max_anchor_separation_m", cfg, 0, maxSeparation, 0.001, ToleranceKind.UpperBound,
                        "rigid joint: 1 mm engineering tolerance",
                        "A single locked linear joint drifts.", CausalLayer.ConstraintConvergence);
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }
    }
}
