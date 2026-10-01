using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using static PowerliftingSimulator.PhysicsBenchmarks.PhysicsBenchmarkSettings;

namespace PowerliftingSimulator.PhysicsBenchmarks
{
    /// <summary>Accumulates the contact impulse PhysX reports for one body per step.</summary>
    public sealed class ContactImpulseProbe : MonoBehaviour
    {
        public Vector3 StepImpulse;
        public int Contacts;

        public void BeginStep()
        {
            StepImpulse = Vector3.zero;
            Contacts = 0;
        }

        private void OnCollisionEnter(Collision collision) => Accumulate(collision);
        private void OnCollisionStay(Collision collision) => Accumulate(collision);

        private void Accumulate(Collision collision)
        {
            StepImpulse += collision.impulse;
            Contacts += collision.contactCount;
        }
    }

    /// <summary>
    /// GAM-50 benchmarks 8-9: Coulomb friction and normal contact. The
    /// athlete's feet use a grip material (mu = 1.0, Maximum combine); these
    /// fixtures use that material against the platform's default so the
    /// thresholds are the ones the squat actually stands on.
    /// </summary>
    [Category("PhysicsBenchmark")]
    [Category("PhysicsBenchmarkIsolated")]
    public sealed class ContactPhysicsBenchmarks
    {
        private static PhysicsMaterial FootGrip() =>
            IsolatedPhysicsWorld.Material("GAM11_FootGrip_replica", 1.0f, 1.0f, PhysicsMaterialCombine.Maximum);

        [UnityTest]
        public IEnumerator B08_Friction_InclineAndPushSlipTransition()
        {
            var rec = new PhysicsBenchmarkRecorder("B08_friction");
            double g = -Physics.gravity.y;
            const double mu = 1.0;

            // Incline: holds below tan(theta) = mu, slides above with
            // a = g (sin - mu cos).
            foreach (double tanRatio in new[] { 0.8, 1.25 })
            {
                foreach (float mass in new[] { 2f, 400f })
                {
                    using (IsolatedPhysicsWorld world = ProductionWorld("incline"))
                    {
                        double theta = Math.Atan(tanRatio * mu);
                        Quaternion tilt = Quaternion.AngleAxis((float)(theta * Mathf.Rad2Deg), Vector3.right);
                        world.CreateStaticBox("incline", new Vector3(4f, 0.2f, 20f), Vector3.zero, tilt, PlatformReplica());
                        var size = new Vector3(0.13f, 0.10f, 0.29f);
                        Vector3 position = tilt * new Vector3(0f, 0.1f + size.y * 0.5f, 0f);
                        Rigidbody block = world.CreateBody("foot", position, tilt, mass,
                            BoxInertia(mass, size), colliderBoxSize: size, material: FootGrip());
                        world.Step(20);
                        Vector3 start = block.position;
                        Vector3 downSlope = tilt * Vector3.back;
                        int steps = Mathf.RoundToInt(1f / world.Dt);
                        world.Step(steps);
                        double displacement = Vector3.Dot(block.position - start, downSlope);
                        string cfg = Cfg($"tan/mu={tanRatio:F2};m={mass}", world);
                        if (tanRatio < 1)
                        {
                            rec.Record("stick_displacement_m", cfg, 0, Math.Abs(displacement), 0.001, ToleranceKind.UpperBound,
                                "Coulomb: no slip below tan(theta)=mu; 1 mm engineering tolerance over 1 s",
                                "Feet creep under sub-threshold shear.", CausalLayer.ContactFriction);
                        }
                        else
                        {
                            double a = g * (Math.Sin(theta) - mu * Math.Cos(theta));
                            double t = steps * (double)world.Dt;
                            double velocity = Vector3.Dot(block.linearVelocity, downSlope);
                            rec.Record("slide_velocity_after_1s_mps", cfg, a * t, velocity, 0.05, ToleranceKind.Relative,
                                "Coulomb sliding a = g (sin - mu cos); 5% engineering tolerance",
                                "Dynamic friction coefficient is not the authored value.", CausalLayer.ContactFriction);
                        }
                    }
                    yield return null;
                }
            }

            // Horizontal push on flat ground: slip threshold F = mu m g.
            foreach (double pushRatio in new[] { 0.9, 1.1 })
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("push"))
                {
                    world.CreateStaticBox("ground", new Vector3(20f, 0.2f, 20f), new Vector3(0f, -0.1f, 0f), Quaternion.identity, PlatformReplica());
                    const float mass = 50f;
                    var size = new Vector3(0.13f, 0.10f, 0.29f);
                    Rigidbody block = world.CreateBody("foot", new Vector3(0f, size.y * 0.5f, 0f), Quaternion.identity, mass,
                        BoxInertia(mass, size), colliderBoxSize: size, material: FootGrip());
                    world.Step(20);
                    Vector3 start = block.position;
                    float force = (float)(pushRatio * mu * mass * g);
                    int steps = Mathf.RoundToInt(1f / world.Dt);
                    for (int i = 0; i < steps; i++)
                    {
                        block.AddForce(new Vector3(0f, 0f, force), ForceMode.Force);
                        world.Step();
                    }
                    double displacement = block.position.z - start.z;
                    string cfg = Cfg($"F/(mu m g)={pushRatio:F2}", world);
                    if (pushRatio < 1)
                        rec.Record("stick_displacement_m", cfg, 0, Math.Abs(displacement), 0.001, ToleranceKind.UpperBound,
                            "Coulomb: no slip below mu m g; 1 mm over 1 s", "Shear below the friction cone slips.",
                            CausalLayer.ContactFriction);
                    else
                    {
                        double a = (force - mu * mass * g) / mass;
                        double t = steps * (double)world.Dt;
                        rec.Record("slide_velocity_after_1s_mps", cfg, a * t, block.linearVelocity.z, 0.10, ToleranceKind.Relative,
                            "Coulomb: a = (F - mu m g)/m; 10% (small net force)", "Friction cone limit is not mu N.",
                            CausalLayer.ContactFriction);
                    }
                }
                yield return null;
            }

            // Characterisation: where does the stick/slip transition actually
            // sit? Push ratios against mu N for the production foot/platform
            // pair and a plain symmetric mu = 0.5 pair.
            var pairs = new (string label, double mu, PhysicsMaterial foot, PhysicsMaterial ground)[]
            {
                ("foot_on_platform_mu1", 1.0, FootGrip(), PlatformReplica()),
                ("plain_mu0p5", 0.5, IsolatedPhysicsWorld.Material("plain_a", 0.5f, 0.5f, PhysicsMaterialCombine.Average),
                    IsolatedPhysicsWorld.Material("plain_b", 0.5f, 0.5f, PhysicsMaterialCombine.Average))
            };
            foreach (var pair in pairs)
            {
                double threshold = double.NaN;
                foreach (double pushRatio in new[] { 1.0, 1.2, 1.5, 1.8, 2.0, 2.2, 2.5, 3.0 })
                {
                    using (IsolatedPhysicsWorld world = ProductionWorld("push_sweep"))
                    {
                        world.CreateStaticBox("ground", new Vector3(20f, 0.2f, 20f), new Vector3(0f, -0.1f, 0f), Quaternion.identity, pair.ground);
                        const float mass = 50f;
                        var size = new Vector3(0.13f, 0.10f, 0.29f);
                        Rigidbody block = world.CreateBody("foot", new Vector3(0f, size.y * 0.5f, 0f), Quaternion.identity, mass,
                            BoxInertia(mass, size), colliderBoxSize: size, material: pair.foot);
                        world.Step(20);
                        float force = (float)(pushRatio * pair.mu * mass * g);
                        for (int i = 0; i < 100; i++)
                        {
                            block.AddForce(new Vector3(0f, 0f, force), ForceMode.Force);
                            world.Step();
                        }
                        double v = block.linearVelocity.z;
                        double expectedV = Math.Max(0, (force - pair.mu * mass * g) / mass) * 1.0;
                        rec.Info("push_slip_velocity_mps", Cfg($"{pair.label};F/(mu m g)={pushRatio:F2};coulomb_v={expectedV:F3}", world), v,
                            "Slip velocity after 1 s of constant push.", CausalLayer.ContactFriction);
                        if (double.IsNaN(threshold) && v > 0.05)
                            threshold = pushRatio;
                    }
                }
                rec.Info("observed_slip_threshold_over_mu_N", pair.label, threshold,
                    "Smallest tested F/(mu N) that slips (NaN = none up to 3.0).", CausalLayer.ContactFriction);
                yield return null;
            }
            rec.WriteAndAssert();
        }

        [UnityTest]
        public IEnumerator B09_DropContact_ImpulseAndResting()
        {
            var rec = new PhysicsBenchmarkRecorder("B09_drop_contact");
            double g = -Physics.gravity.y;
            foreach (float mass in new[] { 10f, 400f })
            {
                using (IsolatedPhysicsWorld world = ProductionWorld("drop"))
                {
                    world.CreateStaticBox("ground", new Vector3(20f, 0.2f, 20f), new Vector3(0f, -0.1f, 0f), Quaternion.identity);
                    var size = new Vector3(0.3f, 0.1f, 0.3f);
                    const float height = 0.2f;
                    Rigidbody block = world.CreateBody("block", new Vector3(0f, size.y * 0.5f + height, 0f), Quaternion.identity,
                        mass, BoxInertia(mass, size), colliderBoxSize: size,
                        material: IsolatedPhysicsWorld.Material("plain", 0.6f, 0.6f, PhysicsMaterialCombine.Average));
                    ContactImpulseProbe probe = block.gameObject.AddComponent<ContactImpulseProbe>();
                    double impactSpeed = 0;
                    double maxRebound = 0;
                    double impulseSum = 0;
                    bool contacted = false;
                    int steps = Mathf.RoundToInt(2f / world.Dt);
                    double restImpulse = 0;
                    int restSamples = 0;
                    for (int i = 0; i < steps; i++)
                    {
                        float vBefore = block.linearVelocity.y;
                        probe.BeginStep();
                        world.Step();
                        if (!contacted && probe.Contacts > 0)
                        {
                            contacted = true;
                            impactSpeed = -vBefore;
                        }
                        if (contacted)
                        {
                            maxRebound = Math.Max(maxRebound, block.linearVelocity.y);
                            impulseSum += probe.StepImpulse.y;
                        }
                        if (world.Time >= 1.5)
                        {
                            restImpulse += probe.StepImpulse.y;
                            restSamples++;
                        }
                    }
                    double restForce = restImpulse / (restSamples * world.Dt);
                    double gap = block.position.y - size.y * 0.5f;
                    string cfg = Cfg($"m={mass};h={height}", world);
                    rec.Record("max_rebound_velocity_mps", cfg, 0, maxRebound, 0.02, ToleranceKind.UpperBound,
                        "bounciness 0 and impact below bounceThreshold: no rebound; 0.02 m/s",
                        "Contact injects energy.", CausalLayer.ContactFriction);
                    rec.Record("resting_normal_force_n", cfg, mass * g, restForce, 0.01, ToleranceKind.Relative,
                        "static equilibrium N = m g; 1%", "Contact load path does not carry the body's weight.",
                        CausalLayer.ContactFriction);
                    rec.Record("resting_gap_m", cfg, 0, Math.Abs(gap), 0.002, ToleranceKind.UpperBound,
                        "rigid contact rests at zero separation; 2 mm (< default contactOffset 10 mm)",
                        "Contact sinks or floats under load.", CausalLayer.ContactFriction);
                    // Momentum balance from first contact to t=2 s: the
                    // contact must remove the impact momentum and support the
                    // weight for the remaining time.
                    double tContact = steps * (double)world.Dt - (Math.Sqrt(2 * height / g));
                    rec.Record("contact_impulse_momentum_balance_ns", cfg, mass * impactSpeed + mass * g * tContact,
                        impulseSum, 0.03, ToleranceKind.Relative,
                        "impulse-momentum theorem over the contact window; 3%", "Reported contact impulse is not the applied impulse.",
                        CausalLayer.ContactFriction);
                }
                yield return null;
            }
            rec.WriteAndAssert();
        }

        /// <summary>Replica of PhysicalAthleteRig's GAM6_PlatformContact (combine Average loses to the foot's Maximum).</summary>
        private static PhysicsMaterial PlatformReplica()
        {
            PhysicsMaterial material = IsolatedPhysicsWorld.Material("GAM6_PlatformContact_replica", 0.85f, 0.75f, PhysicsMaterialCombine.Average);
            return material;
        }

        internal static Vector3 BoxInertia(float mass, Vector3 size) => new Vector3(
            mass * (size.y * size.y + size.z * size.z) / 12f,
            mass * (size.x * size.x + size.z * size.z) / 12f,
            mass * (size.x * size.x + size.y * size.y) / 12f);
    }
}
