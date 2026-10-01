using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// GAM-50 section 10: ConfigurableJoint vs ArticulationBody on the same
    /// three-link driven chain (B11 geometry, masses, gravity, targets, K/D
    /// and finite authority), carrying the production barbell through the
    /// production saddle. Only a material advantage on this benchmark can
    /// authorise migrating the athlete substrate.
    /// </summary>
    [Category("PhysicsBenchmark")]
    [Category("PhysicsBenchmarkIsolated")]
    [Category("PhysicsBenchmarkSubstrateAB")]
    public sealed class SubstrateABBenchmarks
    {
        private static readonly float[] ABLoads = { 0f, 25f, 60f, 140f, 170f };

        /// <summary>Label of the process-wide PhysX solver for this run (set by the runner).</summary>
        private static string SolverLabel =>
            Environment.GetEnvironmentVariable("PHYSICS_BENCHMARK_SOLVER_LABEL") ?? "PGS";

        private sealed class ArticulatedChain
        {
            public ArticulationBody Root;
            public ArticulationBody[] Links = new ArticulationBody[3];
            public Rigidbody Bar;
            public ConfigurableJoint Saddle;
            public Vector3 TorsoTopLocal;
        }

        /// <summary>
        /// Builds the B11 chain as a reduced-coordinate articulation. Revolute
        /// joints about world +X, drives on xDrive, the articulation root is
        /// the immovable foot.
        /// </summary>
        private static ArticulatedChain BuildArticulated(IsolatedPhysicsWorld world, ConstraintPhysicsBenchmarks.ChainSpec spec,
            float loadKg, float maxForce, float stiffnessUnitScale, int solverIterations, int velocityIterations)
        {
            var chain = new ArticulatedChain();
            GameObject rootObject = world.CreateObject("foot_root", Vector3.zero, Quaternion.identity);
            chain.Root = rootObject.AddComponent<ArticulationBody>();
            chain.Root.immovable = true;
            chain.Root.mass = 1f;
            chain.Root.useGravity = false;
            Transform parent = rootObject.transform;
            Vector3 jointPoint = Vector3.zero;
            for (int i = 0; i < 3; i++)
            {
                Quaternion rotation = Quaternion.AngleAxis(spec.Angle[i] * Mathf.Rad2Deg, Vector3.right);
                Vector3 direction = rotation * Vector3.up;
                float length = spec.Length[i];
                float mass = spec.Mass[i];
                Vector3 com = jointPoint + direction * (length * 0.5f);
                var go = new GameObject(spec.Names[i]);
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(com, rotation);
                ArticulationBody link = go.AddComponent<ArticulationBody>();
                link.jointType = ArticulationJointType.RevoluteJoint;
                link.mass = mass;
                link.automaticCenterOfMass = false;
                link.automaticInertiaTensor = false;
                link.centerOfMass = Vector3.zero;
                float transverse = mass * length * length / 12f;
                link.inertiaTensor = new Vector3(transverse, mass * 0.01f, transverse);
                link.inertiaTensorRotation = Quaternion.identity;
                link.linearDamping = 0f;
                link.angularDamping = 0f;
                link.jointFriction = 0f;
                link.maxAngularVelocity = 1000f;
                link.solverIterations = solverIterations;
                link.solverVelocityIterations = velocityIterations;
                link.sleepThreshold = 0f;
                // Joint frame: anchor at the segment's proximal end, revolute
                // about local +X (= world +X for these planar segments).
                link.matchAnchors = true;
                link.anchorPosition = new Vector3(0f, -length * 0.5f, 0f);
                link.anchorRotation = Quaternion.identity;
                link.twistLock = ArticulationDofLock.FreeMotion;
                JointFamilyProfile profile = PoweredJointController.FindFamilyProfile(spec.Family[i]).Value;
                link.xDrive = new ArticulationDrive
                {
                    stiffness = profile.Spring * spec.DriveMultiplier * stiffnessUnitScale,
                    damping = profile.Damper * spec.DriveMultiplier * stiffnessUnitScale,
                    forceLimit = maxForce,
                    target = 0f,
                    targetVelocity = 0f,
                    driveType = ArticulationDriveType.Force
                };
                chain.Links[i] = link;
                jointPoint += direction * length;
                parent = go.transform;
            }
            chain.TorsoTopLocal = new Vector3(0f, spec.Length[2] * 0.5f, 0f);
            if (loadKg > 0f)
            {
                Vector3 top = chain.Links[2].transform.TransformPoint(chain.TorsoTopLocal);
                chain.Bar = SaddleReplica.CreateBar(world, loadKg, top);
                chain.Saddle = SaddleReplica.Attach(chain.Bar, null, chain.TorsoTopLocal);
                chain.Saddle.connectedArticulationBody = chain.Links[2];
                chain.Saddle.connectedAnchor = chain.TorsoTopLocal;
            }
            return chain;
        }

        private static double[] ArticulatedErrors(ArticulatedChain chain)
        {
            var e = new double[3];
            for (int i = 0; i < 3; i++)
                e[i] = chain.Links[i].jointPosition[0];
            return e;
        }

        /// <summary>
        /// Single revolute articulation link under a known gravity moment:
        /// measures whether ArticulationDrive.stiffness is per radian or per
        /// degree, so both arms are given the same physical K.
        /// </summary>
        private static double ArticulationStiffnessUnitScale(PhysicsBenchmarkRecorder rec)
        {
            using (IsolatedPhysicsWorld world = ProductionWorld("articulation_units"))
            {
                const float k = 2000f, mass = 20f, lever = 0.3f;
                GameObject rootObject = world.CreateObject("root", Vector3.zero, Quaternion.identity);
                ArticulationBody root = rootObject.AddComponent<ArticulationBody>();
                root.immovable = true;
                var go = new GameObject("lever");
                go.transform.SetParent(rootObject.transform, false);
                go.transform.localPosition = new Vector3(0f, 0f, lever);
                ArticulationBody link = go.AddComponent<ArticulationBody>();
                link.jointType = ArticulationJointType.RevoluteJoint;
                link.mass = mass;
                link.automaticCenterOfMass = false;
                link.automaticInertiaTensor = false;
                link.centerOfMass = Vector3.zero;
                link.inertiaTensor = Vector3.one * 0.2f;
                link.linearDamping = 0f;
                link.angularDamping = 0f;
                link.jointFriction = 0f;
                link.matchAnchors = true;
                link.anchorPosition = new Vector3(0f, 0f, -lever);
                link.anchorRotation = Quaternion.identity;
                link.twistLock = ArticulationDofLock.FreeMotion;
                link.xDrive = new ArticulationDrive { stiffness = k, damping = 100f, forceLimit = 1e7f, driveType = ArticulationDriveType.Force };
                world.Step(Mathf.RoundToInt(3f / world.Dt));
                double observed = Math.Abs(link.jointPosition[0]);
                double tau = mass * -Physics.gravity.y * lever;
                double perRadian = tau / k;
                double perDegree = tau / (k * Mathf.Rad2Deg);
                double scale = Math.Abs(observed - perRadian) < Math.Abs(observed - perDegree) ? 1.0 : Mathf.Rad2Deg;
                rec.Info("articulation_static_deflection_rad", Cfg("k=2000;tau=58.86", world), observed,
                    "Single-link articulation deflection under a known moment.", CausalLayer.DriveSemantics);
                rec.Info("articulation_stiffness_unit_scale", Cfg("1=per rad;57.3=per degree", world), scale,
                    "Scale applied to Nm/rad gains for ArticulationDrive.stiffness/damping.", CausalLayer.DriveSemantics);
                rec.Record("articulation_single_joint_realization", Cfg("k=2000", world), scale == 1.0 ? perRadian : perDegree,
                    observed, 0.01, ToleranceKind.Relative,
                    "reduced-coordinate drive statics are exact: e = tau / K within 1%",
                    "ArticulationBody drive does not realise its stiffness on a single joint.", CausalLayer.DriveSemantics);
                return scale;
            }
        }

        [UnityTest]
        public IEnumerator B16_ChainSubstrate_ConfigurableJointVsArticulation()
        {
            var rec = new PhysicsBenchmarkRecorder("B16_substrate_ab_" + SolverLabel);
            rec.Note("solver", SolverLabel);
            double g = -Physics.gravity.y;
            var spec = new ConstraintPhysicsBenchmarks.ChainSpec();
            float unitScale = (float)ArticulationStiffnessUnitScale(rec);
            yield return null;

            var cells = new List<(string arm, float dt, int pos, int vel)>
            {
                ("CJ", ProductionDt, ProductionPositionIterations, ProductionVelocityIterations),
                ("CJ", ProductionDt, 4 * ProductionPositionIterations, ProductionVelocityIterations),
                ("CJ", 0.02f, ProductionPositionIterations, ProductionVelocityIterations),
                ("CJ", 0.005f, ProductionPositionIterations, ProductionVelocityIterations),
                ("AB", ProductionDt, ProductionPositionIterations, ProductionVelocityIterations),
                ("AB", ProductionDt, 14, 1),
                ("AB", ProductionDt, 56, 1),
                ("AB", 0.02f, ProductionPositionIterations, ProductionVelocityIterations),
                ("AB", 0.005f, ProductionPositionIterations, ProductionVelocityIterations),
            };

            foreach (float load in ABLoads)
            {
                float barMass = load > 0f ? BarbellLoadingSolver.Solve(load).TotalMassKg : 0f;
                double[] expected = ConstraintPhysicsBenchmarks.ExpectedStaticErrors(spec, barMass, g);
                if (!ConstraintPhysicsBenchmarks.IsStaticallyStable(spec, expected, barMass, g))
                {
                    rec.Info("expected_equilibrium_unstable", $"load={load}", 1, "Skipped: no stable static target.", CausalLayer.AthleteEquilibrium);
                    continue;
                }
                foreach (var cell in cells)
                {
                    double[] repeatA = null;
                    for (int repeat = 0; repeat < 2; repeat++)
                    {
                        using (var world = new IsolatedPhysicsWorld("ab_" + cell.arm, cell.dt, cell.pos, cell.vel))
                        {
                            int steps = Mathf.RoundToInt(4f / cell.dt);
                            int tailStart = steps - Mathf.RoundToInt(0.5f / cell.dt);
                            var sums = new double[3];
                            int count = 0;
                            double separation = 0, speed = 0;
                            var watch = new Stopwatch();
                            Func<double[]> errors;
                            Func<double> stretch;
                            Func<double> maxSpeed;
                            if (cell.arm == "CJ")
                            {
                                ConstraintPhysicsBenchmarks.ChainState state = ConstraintPhysicsBenchmarks.BuildChain(world, spec, load, 1e7f);
                                errors = () => new[]
                                {
                                    ConstraintPhysicsBenchmarks.JointTwist(state, 0),
                                    ConstraintPhysicsBenchmarks.JointTwist(state, 1),
                                    ConstraintPhysicsBenchmarks.JointTwist(state, 2)
                                };
                                stretch = () => ConstraintPhysicsBenchmarks.MaxAnchorSeparation(state, spec);
                                maxSpeed = () =>
                                {
                                    double s = 0;
                                    foreach (Rigidbody link in state.Links) s = Math.Max(s, link.angularVelocity.magnitude);
                                    return s;
                                };
                            }
                            else
                            {
                                ArticulatedChain chain = BuildArticulated(world, spec, load, 1e7f, unitScale, cell.pos, cell.vel);
                                errors = () => ArticulatedErrors(chain);
                                // Reduced coordinates cannot stretch: report the
                                // world gap between consecutive link anchors.
                                stretch = () =>
                                {
                                    double worst = 0;
                                    for (int i = 1; i < 3; i++)
                                    {
                                        Vector3 a = chain.Links[i].transform.TransformPoint(chain.Links[i].anchorPosition);
                                        Vector3 b = chain.Links[i - 1].transform.TransformPoint(
                                            new Vector3(0f, spec.Length[i - 1] * 0.5f, 0f));
                                        worst = Math.Max(worst, (a - b).magnitude);
                                    }
                                    return worst;
                                };
                                maxSpeed = () =>
                                {
                                    double s = 0;
                                    foreach (ArticulationBody link in chain.Links) s = Math.Max(s, link.angularVelocity.magnitude);
                                    return s;
                                };
                            }
                            for (int i = 0; i < steps; i++)
                            {
                                watch.Start();
                                world.Step();
                                watch.Stop();
                                if (i >= steps / 2)
                                    separation = Math.Max(separation, stretch());
                                if (i >= tailStart)
                                {
                                    double[] e = errors();
                                    for (int j = 0; j < 3; j++) sums[j] += e[j];
                                    count++;
                                }
                            }
                            speed = maxSpeed();
                            var observed = new[] { sums[0] / count, sums[1] / count, sums[2] / count };
                            string cfg = Cfg($"{SolverLabel};{cell.arm};load={load};rep={repeat}", world);
                            bool production = cell.dt == ProductionDt && cell.pos == ProductionPositionIterations;
                            for (int j = 0; j < 3; j++)
                            {
                                string metric = $"{cell.arm}_{spec.Family[j]}_static_error_rad";
                                if (production && repeat == 0)
                                    rec.Record(metric, cfg, expected[j], observed[j], ConstraintPhysicsBenchmarks.RealizationTolerance(expected[j]),
                                        ToleranceKind.Absolute, "sealed 5% realization band (0.002 rad floor); independent planar statics",
                                        $"The {cell.arm} chain does not reach its static PD equilibrium at production dt/iterations.",
                                        CausalLayer.ConstraintConvergence);
                                else
                                    rec.Info(metric, cfg, observed[j], "Static error off production settings or repeat.",
                                        CausalLayer.NumericalConvergence);
                                rec.Info($"{cell.arm}_{spec.Family[j]}_abs_error_vs_statics_rad", cfg, Math.Abs(observed[j] - expected[j]),
                                    "Absolute deviation from the analytic equilibrium.", CausalLayer.NumericalConvergence);
                            }
                            rec.Info($"{cell.arm}_max_stretch_m", cfg, separation, "Constraint stretch (second half of run).", CausalLayer.ConstraintConvergence);
                            rec.Info($"{cell.arm}_settled_speed_rad_s", cfg, speed, "Residual angular speed after 4 s.", CausalLayer.ConstraintConvergence);
                            rec.Info($"{cell.arm}_cpu_ms_per_step", cfg, watch.Elapsed.TotalMilliseconds / steps,
                                "Wall time per PhysicsScene.Simulate (whole isolated scene).", CausalLayer.NumericalConvergence);
                            if (repeat == 0)
                            {
                                repeatA = observed;
                            }
                            else
                            {
                                double diff = 0;
                                for (int j = 0; j < 3; j++) diff = Math.Max(diff, Math.Abs(observed[j] - repeatA[j]));
                                rec.Info($"{cell.arm}_repeat_max_abs_diff_rad", cfg, diff, "Same-process repeatability.", CausalLayer.NumericalConvergence);
                            }
                        }
                    }
                    yield return null;
                }
            }
            rec.WriteAndAssert();
        }
    }
}
